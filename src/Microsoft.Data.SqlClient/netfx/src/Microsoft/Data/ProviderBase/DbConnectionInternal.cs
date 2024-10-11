// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.Data.ProviderBase
{

    using System;
    using System.Data;
    using System.Data.Common;
    using System.Diagnostics;
    using System.Runtime.ConstrainedExecution;
    using System.Security.Permissions;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Data.Common;
    using Microsoft.Data.SqlClient;
    using System.Transactions;

    internal abstract class DbConnectionInternal
    {
        private static int _objectTypeCount;
        internal readonly int _objectID = Interlocked.Increment(ref _objectTypeCount);
        private TransactionCompletedEventHandler _transactionCompletedEventHandler = null;

        internal static readonly StateChangeEventArgs StateChangeClosed = new StateChangeEventArgs(ConnectionState.Open, ConnectionState.Closed);
        internal static readonly StateChangeEventArgs StateChangeOpen = new StateChangeEventArgs(ConnectionState.Closed, ConnectionState.Open);

        private readonly bool _allowSetConnectionString;
        private readonly bool _hidePassword;
        private readonly ConnectionState _state;

        private readonly WeakReference<DbConnection> _owningObject = new WeakReference<DbConnection>(null, false);  // [usage must be thread safe] the owning object, when not in the pool. (both Pooled and Non-Pooled connections)

        private DbConnectionPool _connectionPool;           // the pooler that the connection came from (Pooled connections only)
        private DbConnectionPoolCounters _performanceCounters;      // the performance counters we're supposed to update
        private DbReferenceCollection _referenceCollection;      // collection of objects that we need to notify in some way when we're being deactivated
        private int _pooledCount;              // [usage must be thread safe] the number of times this object has been pushed into the pool less the number of times it's been popped (0 != inPool)

        private bool _connectionIsDoomed;       // true when the connection should no longer be used.
        private bool _cannotBePooled;           // true when the connection should no longer be pooled.
        private bool _isInStasis;

        private DateTime _createTime;               // when the connection was created.

        private Transaction _enlistedTransaction;      // [usage must be thread-safe] the transaction that we're enlisted in, either manually or automatically

        // _enlistedTransaction is a clone, so that transaction information can be queried even if the original transaction object is disposed.
        // However, there are times when we need to know if the original transaction object was disposed, so we keep a reference to it here.
        // This field should only be assigned a value at the same time _enlistedTransaction is updated.
        // Also, this reference should not be disposed, since we aren't taking ownership of it.
        private Transaction _enlistedTransactionOriginal;

#if DEBUG
        private int _activateCount;            // debug only counter to verify activate/deactivates are in sync.
#endif //DEBUG

        protected DbConnectionInternal() : this(ConnectionState.Open, true, false)
        { // V1.1.3300
        }

        // Constructor for internal connections
        internal DbConnectionInternal(ConnectionState state, bool hidePassword, bool allowSetConnectionString)
        {
            _allowSetConnectionString = allowSetConnectionString;
            _hidePassword = hidePassword;
            _state = state;
        }

        internal bool AllowSetConnectionString
        {
            get
            {
                return _allowSetConnectionString;
            }
        }

        internal bool CanBePooled
        {
            get
            {
                return (!_connectionIsDoomed && !_cannotBePooled && !_owningObject.TryGetTarget(out _));
            }
        }

        protected internal Transaction EnlistedTransaction
        {
            get
            {
                return _enlistedTransaction;
            }
            set
            {
                Transaction currentEnlistedTransaction = _enlistedTransaction;
                if ((currentEnlistedTransaction == null && value != null)
                    || (currentEnlistedTransaction != null && !currentEnlistedTransaction.Equals(value)))
                {  // WebData 20000024

                    // Pay attention to the order here:
                    // 1) defect from any notifications
                    // 2) replace the transaction
                    // 3) re-enlist in notifications for the new transaction

                    // SQLBUDT #230558 we need to use a clone of the transaction
                    // when we store it, or we'll end up keeping it past the
                    // duration of the using block of the TransactionScope
                    Transaction valueClone = null;
                    Transaction previousTransactionClone = null;
                    try
                    {
                        if (value != null)
                        {
                            valueClone = value.Clone();
                        }

                        // NOTE: rather than take locks around several potential round-
                        // trips to the server, and/or virtual function calls, we simply
                        // presume that you aren't doing something illegal from multiple
                        // threads, and check once we get around to finalizing things
                        // inside a lock.

                        lock (this)
                        {
                            // NOTE: There is still a race condition here, when we are
                            // called from EnlistTransaction (which cannot re-enlist)
                            // instead of EnlistDistributedTransaction (which can),
                            // however this should have been handled by the outer
                            // connection which checks to ensure that it's OK.  The
                            // only case where we have the race condition is multiple
                            // concurrent enlist requests to the same connection, which
                            // is a bit out of line with something we should have to
                            // support.

                            // enlisted transaction can be nullified in Dispose call without lock
                            previousTransactionClone = Interlocked.Exchange(ref _enlistedTransaction, valueClone);
                            _enlistedTransactionOriginal = value;
                            value = valueClone;
                            valueClone = null; // we've stored it, don't dispose it.
                        }
                    }
                    finally
                    {
                        // we really need to dispose our clones; they may have
                        // native resources and GC may not happen soon enough.
                        // VSDevDiv 479564: don't dispose if still holding reference in _enlistedTransaction
                        if (previousTransactionClone != null &&
                                !Object.ReferenceEquals(previousTransactionClone, _enlistedTransaction))
                        {
                            previousTransactionClone.Dispose();
                        }
                        if (valueClone != null && !Object.ReferenceEquals(valueClone, _enlistedTransaction))
                        {
                            valueClone.Dispose();
                        }
                    }

                    // I don't believe that we need to lock to protect the actual
                    // enlistment in the transaction; it would only protect us
                    // against multiple concurrent calls to enlist, which really
                    // isn't supported anyway.

                    if (value != null)
                    {
                        SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.set_EnlistedTransaction|RES|CPOOL> {0}, Transaction {1}, Enlisting.", ObjectID, value.GetHashCode());
                        TransactionOutcomeEnlist(value);
                    }
                }
            }
        }

        /// <summary>
        /// Get boolean value that indicates whether the enlisted transaction has been disposed.
        /// </summary>
        /// <value>
        /// True if there is an enlisted transaction, and it has been disposed.
        /// False if there is an enlisted transaction that has not been disposed, or if the transaction reference is null.
        /// </value>
        /// <remarks>
        /// This method must be called while holding a lock on the DbConnectionInternal instance.
        /// </remarks>
        protected bool EnlistedTransactionDisposed
        {
            get
            {
                // Until the Transaction.Disposed property is public it is necessary to access a member
                // that throws if the object is disposed to determine if in fact the transaction is disposed.
                try
                {
                    bool disposed;

                    Transaction currentEnlistedTransactionOriginal = _enlistedTransactionOriginal;
                    if (currentEnlistedTransactionOriginal != null)
                    {
                        disposed = currentEnlistedTransactionOriginal.TransactionInformation == null;
                    }
                    else
                    {
                        // Don't expect to get here in the general case,
                        // Since this getter is called by CheckEnlistedTransactionBinding
                        // after checking for a non-null enlisted transaction (and it does so under lock).
                        disposed = false;
                    }

                    return disposed;
                }
                catch (ObjectDisposedException)
                {
                    return true;
                }
            }
        }

        // Is this connection in stasis, waiting for transaction to end before returning to pool?
        internal bool IsTxRootWaitingForTxEnd
        {
            get
            {
                return _isInStasis;
            }
        }

        /// <summary>
        /// Get boolean that specifies whether an enlisted transaction can be unbound from 
        /// the connection when that transaction completes.
        /// </summary>
        /// <value>
        /// True if the enlisted transaction can be unbound on transaction completion; otherwise false.
        /// </value>
        virtual protected bool UnbindOnTransactionCompletion
        {
            get
            {
                return true;
            }
        }

        // Is this a connection that must be put in stasis (or is already in stasis) pending the end of it's transaction?
        virtual protected internal bool IsNonPoolableTransactionRoot
        {
            get
            {
                return false; // if you want to have delegated transactions that are non-poolable, you better override this...
            }
        }

        virtual internal bool IsTransactionRoot
        {
            get
            {
                return false; // if you want to have delegated transactions, you better override this...
            }
        }

        protected internal bool IsConnectionDoomed
        {
            get
            {
                return _connectionIsDoomed;
            }
        }

        internal bool IsEmancipated
        {
            get
            {
                // NOTE: There are race conditions between PrePush, PostPop and this
                //       property getter -- only use this while this object is locked;
                //       (DbConnectionPool.Clear and ReclaimEmancipatedObjects
                //       do this for us)

                // Remember how this works (I keep getting confused...)
                //
                //    _pooledCount is incremented when the connection is pushed into the pool
                //    _pooledCount is decremented when the connection is popped from the pool
                //    _pooledCount is set to -1 when the connection is not pooled (just in case...)
                //
                // That means that:
                //
                //    _pooledCount > 1    connection is in the pool multiple times (this is a serious bug...)
                //    _pooledCount == 1   connection is in the pool
                //    _pooledCount == 0   connection is out of the pool
                //    _pooledCount == -1  connection is not a pooled connection; we shouldn't be here for non-pooled connections.
                //    _pooledCount < -1   connection out of the pool multiple times (not sure how this could happen...)
                //
                // Now, our job is to return TRUE when the connection is out
                // of the pool and it's owning object is no longer around to
                // return it.

                return !IsTxRootWaitingForTxEnd && (_pooledCount < 1) && !_owningObject.TryGetTarget(out _);
            }
        }

        internal bool IsInPool
        {
            get
            {
                Debug.Assert(_pooledCount <= 1 && _pooledCount >= -1, "Pooled count for object is invalid");
                return (_pooledCount == 1);
            }
        }

        internal int ObjectID
        {
            get
            {
                return _objectID;
            }
        }

        protected internal DbConnection Owner
        {
            // We use a weak reference to the owning object so we can identify when
            // it has been garbage collected without throwing exceptions.
            get
            {
                if (_owningObject.TryGetTarget(out DbConnection connection))
                {
                    return connection;
                }
                return null;
            }
        }

        internal DbConnectionPool Pool
        {
            get
            {
                return _connectionPool;
            }
        }

        protected DbConnectionPoolCounters PerformanceCounters
        {
            get
            {
                return _performanceCounters;
            }
        }

        virtual protected bool ReadyToPrepareTransaction
        {
            get
            {
                return true;
            }
        }

        protected internal DbReferenceCollection ReferenceCollection
        {
            get
            {
                return _referenceCollection;
            }
        }

        abstract public string ServerVersion
        {
            get;
        }

        // this should be abstract but until it is added to all the providers virtual will have to do RickFe
        virtual public string ServerVersionNormalized
        {
            get
            {
                throw ADP.NotSupported();
            }
        }

        public bool ShouldHidePassword
        {
            get
            {
                return _hidePassword;
            }
        }

        public ConnectionState State
        {
            get
            {
                return _state;
            }
        }

        internal virtual bool IsAccessTokenExpired => false;

        abstract protected void Activate(Transaction transaction);

        internal void ActivateConnection(Transaction transaction)
        {
            // Internal method called from the connection pooler so we don't expose
            // the Activate method publicly.
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.ActivateConnection|RES|INFO|CPOOL> {0}, Activating", ObjectID);
#if DEBUG
            int activateCount = Interlocked.Increment(ref _activateCount);
            Debug.Assert(1 == activateCount, "activated multiple times?");
#endif // DEBUG

            Activate(transaction);

            PerformanceCounters.NumberOfActiveConnections.Increment();
        }

        internal void AddWeakReference(object value, int tag)
        {
            if (_referenceCollection == null)
            {
                _referenceCollection = CreateReferenceCollection();
                if (_referenceCollection == null)
                {
                    throw ADP.InternalError(ADP.InternalErrorCode.CreateReferenceCollectionReturnedNull);
                }
            }
            _referenceCollection.Add(value, tag);
        }

        abstract public DbTransaction BeginTransaction(System.Data.IsolationLevel il);

        virtual public void ChangeDatabase(string value)
        {
            throw ADP.MethodNotImplemented("ChangeDatabase");
        }

        internal virtual void CloseConnection(DbConnection owningObject, DbConnectionFactory connectionFactory)
        {
            // The implementation here is the implementation required for the
            // "open" internal connections, since our own private "closed"
            // singleton internal connection objects override this method to
            // prevent anything funny from happening (like disposing themselves
            // or putting them into a connection pool)
            //
            // Derived class should override DbConnectionInternal.Deactivate and DbConnectionInternal.Dispose
            // for cleaning up after DbConnection.Close
            //     protected override void Deactivate() { // override DbConnectionInternal.Close
            //         // do derived class connection deactivation for both pooled & non-pooled connections
            //     }
            //     public override void Dispose() { // override DbConnectionInternal.Close
            //         // do derived class cleanup
            //         base.Dispose();
            //     }
            //
            // overriding DbConnection.Close is also possible, but must provider for their own synchronization
            //     public override void Close() { // override DbConnection.Close
            //         base.Close();
            //         // do derived class outer connection for both pooled & non-pooled connections
            //         // user must do their own synchronization here
            //     }
            //
            //     if the DbConnectionInternal derived class needs to close the connection it should
            //     delegate to the DbConnection if one exists or directly call dispose
            //         DbConnection owningObject = (DbConnection)Owner;
            //         if (owningObject != null) {
            //             owningObject.Close(); // force the closed state on the outer object.
            //         }
            //         else {
            //             Dispose();
            //         }
            //
            ////////////////////////////////////////////////////////////////
            // DON'T MESS WITH THIS CODE UNLESS YOU KNOW WHAT YOU'RE DOING!
            ////////////////////////////////////////////////////////////////
            Debug.Assert(owningObject != null, "null owningObject");
            Debug.Assert(connectionFactory != null, "null connectionFactory");
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.CloseConnection|RES|CPOOL> {0} Closing.", ObjectID);

            // if an exception occurs after the state change but before the try block
            // the connection will be stuck in OpenBusy state.  The commented out try-catch
            // block doesn't really help because a ThreadAbort during the finally block
            // would just revert the connection to a bad state.
            // Open->Closed: guarantee internal connection is returned to correct pool
            if (connectionFactory.SetInnerConnectionFrom(owningObject, DbConnectionOpenBusy.SingletonInstance, this))
            {

                // Lock to prevent race condition with cancellation
                lock (this)
                {

                    object lockToken = ObtainAdditionalLocksForClose();
                    try
                    {
                        PrepareForCloseConnection();

                        DbConnectionPool connectionPool = Pool;

                        // Detach from enlisted transactions that are no longer active on close
                        DetachCurrentTransactionIfEnded();

                        // The singleton closed classes won't have owners and
                        // connection pools, and we won't want to put them back
                        // into the pool.
                        if (connectionPool != null)
                        {
                            connectionPool.PutObject(this, owningObject);   // PutObject calls Deactivate for us...
                            // NOTE: Before we leave the PutObject call, another
                            // thread may have already popped the connection from
                            // the pool, so don't expect to be able to verify it.
                        }
                        else
                        {
                            Deactivate();   // ensure we de-activate non-pooled connections, or the data readers and transactions may not get cleaned up...

                            PerformanceCounters.HardDisconnectsPerSecond.Increment();

                            // To prevent an endless recursion, we need to clear
                            // the owning object before we call dispose so that
                            // we can't get here a second time... Ordinarily, I
                            // would call setting the owner to null a hack, but
                            // this is safe since we're about to dispose the
                            // object and it won't have an owner after that for
                            // certain.
                            _owningObject.SetTarget(null);

                            if (IsTransactionRoot)
                            {
                                SetInStasis();
                            }
                            else
                            {
                                PerformanceCounters.NumberOfNonPooledConnections.Decrement();
                                if (this.GetType() != typeof(Microsoft.Data.SqlClient.SqlInternalConnectionSmi))
                                {
                                    Dispose();
                                }
                            }
                        }
                    }
                    finally
                    {
                        ReleaseAdditionalLocksForClose(lockToken);
                        // if a ThreadAbort puts us here then its possible the outer connection will not reference
                        // this and this will be orphaned, not reclaimed by object pool until outer connection goes out of scope.
                        connectionFactory.SetInnerConnectionEvent(owningObject, DbConnectionClosedPreviouslyOpened.SingletonInstance);
                    }
                }
            }
        }

        virtual internal void PrepareForReplaceConnection()
        {
            // By default, there is no preparation required
        }

        virtual protected void PrepareForCloseConnection()
        {
            // By default, there is no preparation required
        }

        virtual protected object ObtainAdditionalLocksForClose()
        {
            return null; // no additional locks in default implementation
        }

        virtual protected void ReleaseAdditionalLocksForClose(object lockToken)
        {
            // no additional locks in default implementation
        }

        virtual protected DbReferenceCollection CreateReferenceCollection()
        {
            throw ADP.InternalError(ADP.InternalErrorCode.AttemptingToConstructReferenceCollectionOnStaticObject);
        }

        abstract protected void Deactivate();

        internal void DeactivateConnection()
        {
            // Internal method called from the connection pooler so we don't expose
            // the Deactivate method publicly.
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.DeactivateConnection|RES|INFO|CPOOL> {0}, Deactivating", ObjectID);
#if DEBUG
            int activateCount = Interlocked.Decrement(ref _activateCount);
            Debug.Assert(0 == activateCount, "activated multiple times?");
#endif // DEBUG

            if (PerformanceCounters != null)
            { // Pool.Clear will DestroyObject that will clean performanceCounters before going here 
                PerformanceCounters.NumberOfActiveConnections.Decrement();
            }

            if (!_connectionIsDoomed && Pool.UseLoadBalancing)
            {
                // If we're not already doomed, check the connection's lifetime and
                // doom it if it's lifetime has elapsed.

                DateTime now = DateTime.UtcNow;  // WebData 111116
                if ((now.Ticks - _createTime.Ticks) > Pool.LoadBalanceTimeout.Ticks)
                {
                    DoNotPoolThisConnection();
                }
            }
            Deactivate();
        }

        virtual internal void DelegatedTransactionEnded()
        {
            // Called by System.Transactions when the delegated transaction has
            // completed.  We need to make closed connections that are in stasis
            // available again, or disposed closed/leaked non-pooled connections.

            // IMPORTANT NOTE: You must have taken a lock on the object before
            // you call this method to prevent race conditions with Clear and
            // ReclaimEmancipatedObjects.
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.DelegatedTransactionEnded|RES|CPOOL> {0}, Delegated Transaction Completed.", ObjectID);

            if (1 == _pooledCount)
            {
                // When _pooledCount is 1, it indicates a closed, pooled,
                // connection so it is ready to put back into the pool for
                // general use.

                TerminateStasis(true);

                Deactivate(); // call it one more time just in case

                DbConnectionPool pool = Pool;

                if (pool == null)
                {
                    throw ADP.InternalError(ADP.InternalErrorCode.PooledObjectWithoutPool);      // pooled connection does not have a pool
                }
                pool.PutObjectFromTransactedPool(this);
            }
            else if (-1 == _pooledCount && !_owningObject.TryGetTarget(out _))
            {
                // When _pooledCount is -1 and the owning object no longer exists,
                // it indicates a closed (or leaked), non-pooled connection so 
                // it is safe to dispose.

                TerminateStasis(false);

                Deactivate(); // call it one more time just in case

                // it's a non-pooled connection, we need to dispose of it
                // once and for all, or the server will have fits about us
                // leaving connections open until the client-side GC kicks 
                // in.
                PerformanceCounters.NumberOfNonPooledConnections.Decrement();
                Dispose();
            }
            // When _pooledCount is 0, the connection is a pooled connection
            // that is either open (if the owning object is alive) or leaked (if
            // the owning object is not alive)  In either case, we can't muck 
            // with the connection here.
        }

        public virtual void Dispose()
        {
            _connectionPool = null;
            _performanceCounters = null;
            _connectionIsDoomed = true;
            _enlistedTransactionOriginal = null; // should not be disposed

            // Dispose of the _enlistedTransaction since it is a clone
            // of the original reference.
            // VSDD 780271 - _enlistedTransaction can be changed by another thread (TX end event)
            Transaction enlistedTransaction = Interlocked.Exchange(ref _enlistedTransaction, null);
            if (enlistedTransaction != null)
            {
                enlistedTransaction.Dispose();
            }
        }

        protected internal void DoNotPoolThisConnection()
        {
            _cannotBePooled = true;
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.DoNotPoolThisConnection|RES|INFO|CPOOL> {0}, Marking pooled object as non-poolable so it will be disposed", ObjectID);
        }

        /// <devdoc>Ensure that this connection cannot be put back into the pool.</devdoc>
        [ReliabilityContract(Consistency.WillNotCorruptState, Cer.Success)]
        protected internal void DoomThisConnection()
        {
            _connectionIsDoomed = true;
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.DoomThisConnection|RES|INFO|CPOOL> {0}, Dooming", ObjectID);
        }

        // Reset connection doomed status so it can be re-connected and pooled.
        protected internal void UnDoomThisConnection()
        {
            _connectionIsDoomed = false;
        }

        abstract public void EnlistTransaction(Transaction transaction);

        virtual protected internal DataTable GetSchema(DbConnectionFactory factory, DbConnectionPoolGroup poolGroup, DbConnection outerConnection, string collectionName, string[] restrictions)
        {
            Debug.Assert(outerConnection != null, "outerConnection may not be null.");

            DbMetaDataFactory metaDataFactory = factory.GetMetaDataFactory(poolGroup, this);
            Debug.Assert(metaDataFactory != null, "metaDataFactory may not be null.");

            return metaDataFactory.GetSchema(outerConnection, collectionName, restrictions);
        }

        internal void MakeNonPooledObject(DbConnection owningObject, DbConnectionPoolCounters performanceCounters)
        {
            // Used by DbConnectionFactory to indicate that this object IS NOT part of
            // a connection pool.

            _connectionPool = null;
            _performanceCounters = performanceCounters;
            _owningObject.SetTarget(owningObject);
            _pooledCount = -1;
        }

        internal void MakePooledConnection(DbConnectionPool connectionPool)
        {
            // Used by DbConnectionFactory to indicate that this object IS part of
            // a connection pool.

            // TODO: consider using ADP.TimerCurrent() for this.
            _createTime = DateTime.UtcNow; // WebData 111116

            _connectionPool = connectionPool;
            _performanceCounters = connectionPool.PerformanceCounters;
        }

        internal void NotifyWeakReference(int message)
        {
            DbReferenceCollection referenceCollection = ReferenceCollection;
            if (referenceCollection != null)
            {
                referenceCollection.Notify(message);
            }
        }

        internal virtual void OpenConnection(DbConnection outerConnection, DbConnectionFactory connectionFactory)
        {
            if (!TryOpenConnection(outerConnection, connectionFactory, null, null))
            {
                throw ADP.InternalError(ADP.InternalErrorCode.SynchronousConnectReturnedPending);
            }
        }

        /// <devdoc>The default implementation is for the open connection objects, and
        /// it simply throws.  Our private closed-state connection objects
        /// override this and do the correct thing.</devdoc>
        // User code should either override DbConnectionInternal.Activate when it comes out of the pool
        // or override DbConnectionFactory.CreateConnection when the connection is created for non-pooled connections
        internal virtual bool TryOpenConnection(DbConnection outerConnection, DbConnectionFactory connectionFactory, TaskCompletionSource<DbConnectionInternal> retry, DbConnectionOptions userOptions)
        {
            throw ADP.ConnectionAlreadyOpen(State);
        }

        internal virtual bool TryReplaceConnection(DbConnection outerConnection, DbConnectionFactory connectionFactory, TaskCompletionSource<DbConnectionInternal> retry, DbConnectionOptions userOptions)
        {
            throw ADP.MethodNotImplemented("TryReplaceConnection");
        }

        protected bool TryOpenConnectionInternal(DbConnection outerConnection, DbConnectionFactory connectionFactory, TaskCompletionSource<DbConnectionInternal> retry, DbConnectionOptions userOptions)
        {
            // ?->Connecting: prevent set_ConnectionString during Open
            if (connectionFactory.SetInnerConnectionFrom(outerConnection, DbConnectionClosedConnecting.SingletonInstance, this))
            {
                DbConnectionInternal openConnection = null;
                try
                {
                    connectionFactory.PermissionDemand(outerConnection);
                    if (!connectionFactory.TryGetConnection(outerConnection, retry, userOptions, this, out openConnection))
                    {
                        return false;
                    }
                }
                catch
                {
                    // This should occur for all exceptions, even ADP.UnCatchableExceptions.
                    connectionFactory.SetInnerConnectionTo(outerConnection, this);
                    throw;
                }
                if (openConnection == null)
                {
                    connectionFactory.SetInnerConnectionTo(outerConnection, this);
                    throw ADP.InternalConnectionError(ADP.ConnectionError.GetConnectionReturnsNull);
                }
                connectionFactory.SetInnerConnectionEvent(outerConnection, openConnection);
            }

            return true;
        }

        internal void PrePush(object expectedOwner)
        {
            // Called by DbConnectionPool when we're about to be put into it's pool, we
            // take this opportunity to ensure ownership and pool counts are legit.

            // IMPORTANT NOTE: You must have taken a lock on the object before
            // you call this method to prevent race conditions with Clear and
            // ReclaimEmancipatedObjects.

            //3 // The following tests are retail assertions of things we can't allow to happen.
            bool isAlive = _owningObject.TryGetTarget(out DbConnection connection);
            if (expectedOwner == null)
            {
                if (isAlive)
                {
                    throw ADP.InternalError(ADP.InternalErrorCode.UnpooledObjectHasOwner);      // new unpooled object has an owner
                }
            }
            else if (isAlive && connection != expectedOwner)
            {
                throw ADP.InternalError(ADP.InternalErrorCode.UnpooledObjectHasWrongOwner); // unpooled object has incorrect owner
            }
            if (0 != _pooledCount)
            {
                throw ADP.InternalError(ADP.InternalErrorCode.PushingObjectSecondTime);         // pushing object onto stack a second time
            }

            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.PrePush|RES|CPOOL> {0}, Preparing to push into pool, owning connection {1}, pooledCount={2}", ObjectID, 0, _pooledCount);

            _pooledCount++;
            _owningObject.SetTarget(null); // NOTE: doing this and checking for InternalError.PooledObjectHasOwner degrades the close by 2%
        }

        internal void PostPop(DbConnection newOwner)
        {
            // Called by DbConnectionPool right after it pulls this from it's pool, we
            // take this opportunity to ensure ownership and pool counts are legit.

            Debug.Assert(!IsEmancipated, "pooled object not in pool");

            // SQLBUDT #356871 -- When another thread is clearing this pool, it 
            // will doom all connections in this pool without prejudice which 
            // causes the following assert to fire, which really mucks up stress 
            // against checked bits.  The assert is benign, so we're commenting 
            // it out.
            //Debug.Assert(CanBePooled,   "pooled object is not poolable");

            // IMPORTANT NOTE: You must have taken a lock on the object before
            // you call this method to prevent race conditions with Clear and
            // ReclaimEmancipatedObjects.
            if (_owningObject.TryGetTarget(out _))
            {
                throw ADP.InternalError(ADP.InternalErrorCode.PooledObjectHasOwner);        // pooled connection already has an owner!
            }
            _owningObject.SetTarget(newOwner);
            _pooledCount--;

            //DbConnection x = (newOwner as DbConnection);
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.PostPop|RES|CPOOL> {0}, Preparing to pop from pool,  owning connection {1}, pooledCount={2}", ObjectID, 0, _pooledCount);

            //3 // The following tests are retail assertions of things we can't allow to happen.
            if (Pool != null)
            {
                if (0 != _pooledCount)
                {
                    throw ADP.InternalError(ADP.InternalErrorCode.PooledObjectInPoolMoreThanOnce);  // popping object off stack with multiple pooledCount
                }
            }
            else if (-1 != _pooledCount)
            {
                throw ADP.InternalError(ADP.InternalErrorCode.NonPooledObjectUsedMoreThanOnce); // popping object off stack with multiple pooledCount
            }
        }

        internal void RemoveWeakReference(object value)
        {
            DbReferenceCollection referenceCollection = ReferenceCollection;
            if (referenceCollection != null)
            {
                referenceCollection.Remove(value);
            }
        }

        // Cleanup connection's transaction-specific structures (currently used by Delegated transaction).
        //  This is a separate method because cleanup can be triggered in multiple ways for a delegated
        //  transaction.
        virtual protected void CleanupTransactionOnCompletion(Transaction transaction)
        {
        }

        internal void DetachCurrentTransactionIfEnded()
        {
            Transaction enlistedTransaction = EnlistedTransaction;
            if (enlistedTransaction != null)
            {
                bool transactionIsDead;
                try
                {
                    transactionIsDead = (TransactionStatus.Active != enlistedTransaction.TransactionInformation.Status);
                }
                catch (TransactionException)
                {
                    // If the transaction is being processed (i.e. is part way through a rollback\commit\etc then TransactionInformation.Status will throw an exception)
                    transactionIsDead = true;
                }
                if (transactionIsDead)
                {
                    DetachTransaction(enlistedTransaction, true);
                }
            }
        }

        // Detach transaction from connection.
        internal void DetachTransaction(Transaction transaction, bool isExplicitlyReleasing)
        {
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.DetachTransaction|RES|CPOOL> {0}, Transaction Completed. (pooledCount={1})", ObjectID, _pooledCount);

            // potentially a multi-threaded event, so lock the connection to make sure we don't enlist in a new
            // transaction between compare and assignment. No need to short circuit outside of lock, since failed comparisons should
            // be the exception, not the rule.
            // locking on anything other than the transaction object would lead to a thread deadlock with sys.Transaction.TransactionCompleted event.
            lock (transaction)
            {
                // Detach if detach-on-end behavior, or if outer connection was closed
                DbConnection owner = Owner;
                if (isExplicitlyReleasing || UnbindOnTransactionCompletion || owner is null)
                {
                    Transaction currentEnlistedTransaction = _enlistedTransaction;
                    if (currentEnlistedTransaction != null && transaction.Equals(currentEnlistedTransaction))
                    {
                        // We need to remove the transaction completed event handler to cease listening for the transaction to end.
                        currentEnlistedTransaction.TransactionCompleted -= _transactionCompletedEventHandler;

                        EnlistedTransaction = null;

                        if (IsTxRootWaitingForTxEnd)
                        {
                            DelegatedTransactionEnded();
                        }
                    }
                }
            }
        }

        // Handle transaction detach, pool cleanup and other post-transaction cleanup tasks associated with
        internal void CleanupConnectionOnTransactionCompletion(Transaction transaction)
        {
            DetachTransaction(transaction, false);

            DbConnectionPool pool = Pool;
            if (pool != null)
            {
                pool.TransactionEnded(transaction, this);
            }
        }

        void TransactionCompletedEvent(object sender, TransactionEventArgs e)
        {
            Transaction transaction = e.Transaction;
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.TransactionCompletedEvent|RES|CPOOL> {0}, Transaction Completed. (pooledCount = {1})", ObjectID, _pooledCount);

            CleanupTransactionOnCompletion(transaction);
            CleanupConnectionOnTransactionCompletion(transaction);
        }


        // TODO: Review whether we need the unmanaged code permission when we have the new object model available.
        [SecurityPermission(SecurityAction.Assert, Flags = SecurityPermissionFlag.UnmanagedCode)]
        private void TransactionOutcomeEnlist(Transaction transaction)
        {
            _transactionCompletedEventHandler ??= new TransactionCompletedEventHandler(TransactionCompletedEvent);
            transaction.TransactionCompleted += _transactionCompletedEventHandler;
        }

        internal void SetInStasis()
        {
            _isInStasis = true;
            SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.SetInStasis|RES|CPOOL> {0}, Non-Pooled Connection has Delegated Transaction, waiting to Dispose.", ObjectID);
            PerformanceCounters.NumberOfStasisConnections.Increment();
        }

        private void TerminateStasis(bool returningToPool)
        {
            if (returningToPool)
            {
                SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.TerminateStasis|RES|CPOOL> {0}, Delegated Transaction has ended, connection is closed.  Returning to general pool.", ObjectID);
            }
            else
            {
                SqlClientEventSource.Log.TryPoolerTraceEvent("<prov.DbConnectionInternal.TerminateStasis|RES|CPOOL> {0}, Delegated Transaction has ended, connection is closed/leaked.  Disposing.", ObjectID);
            }

            PerformanceCounters.NumberOfStasisConnections.Decrement();
            _isInStasis = false;
        }

        /// <summary>
        /// When overridden in a derived class, will check if the underlying connection is still actually alive
        /// </summary>
        /// <param name="throwOnException">If true an exception will be thrown if the connection is dead instead of returning true\false
        /// (this allows the caller to have the real reason that the connection is not alive (e.g. network error, etc))</param>
        /// <returns>True if the connection is still alive, otherwise false (If not overridden, then always true)</returns>
        internal virtual bool IsConnectionAlive(bool throwOnException = false)
        {
            return true;
        }
    }
}
