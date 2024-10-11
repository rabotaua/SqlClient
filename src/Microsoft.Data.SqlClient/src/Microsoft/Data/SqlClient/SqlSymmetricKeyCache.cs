// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.Data.SqlClient
{
    /// <summary>
    /// <para> Implements a cache of Symmetric Keys (once they are decrypted).Useful for rapidly decrypting multiple data values.</para>
    /// </summary>
    sealed internal class SqlSymmetricKeyCache
    {
        private readonly MemoryCache _cache;
        private static readonly SqlSymmetricKeyCache _singletonInstance = new SqlSymmetricKeyCache();


        private SqlSymmetricKeyCache()
        {
            _cache = new MemoryCache(new MemoryCacheOptions());
        }

        internal static SqlSymmetricKeyCache GetInstance()
        {
            return _singletonInstance;
        }

        /// <summary>
        /// <para> Retrieves Symmetric Key (in plaintext) given the encryption material.</para>
        /// </summary>
        internal SqlClientSymmetricKey GetKey(SqlEncryptionKeyInfo keyInfo, SqlConnection connection, SqlCommand command)
        {
            string serverName = connection.DataSource;
            Debug.Assert(serverName is not null, @"serverName should not be null.");
            StringBuilder cacheLookupKeyBuilder = new StringBuilder(serverName, capacity: serverName.Length + SqlSecurityUtility.GetBase64LengthFromByteLength(keyInfo.encryptedKey.Length) + keyInfo.keyStoreName.Length + 2/*separators*/);

#if DEBUG
            int capacity = cacheLookupKeyBuilder.Capacity;
#endif //DEBUG

            cacheLookupKeyBuilder.Append(":");
            cacheLookupKeyBuilder.Append(Convert.ToBase64String(keyInfo.encryptedKey));
            cacheLookupKeyBuilder.Append(":");
            cacheLookupKeyBuilder.Append(keyInfo.keyStoreName);

            string cacheLookupKey = cacheLookupKeyBuilder.ToString();

#if DEBUG
            Debug.Assert(cacheLookupKey.Length <= capacity, "We needed to allocate a larger array");
#endif //DEBUG

            // Lookup the key in cache
            SqlClientSymmetricKey encryptionKey;
            if (!(_cache.TryGetValue(cacheLookupKey, out encryptionKey)))
            {
                Debug.Assert(SqlConnection.ColumnEncryptionTrustedMasterKeyPaths is not null, @"SqlConnection.ColumnEncryptionTrustedMasterKeyPaths should not be null");

                SqlSecurityUtility.ThrowIfKeyPathIsNotTrustedForServer(serverName, keyInfo.keyPath);

                // Key Not found, attempt to look up the provider and decrypt CEK
                if (!SqlSecurityUtility.TryGetColumnEncryptionKeyStoreProvider(keyInfo.keyStoreName, out SqlColumnEncryptionKeyStoreProvider provider, connection, command))
                {
                    throw SQL.UnrecognizedKeyStoreProviderName(keyInfo.keyStoreName,
                            SqlConnection.GetColumnEncryptionSystemKeyStoreProvidersNames(),
                            SqlSecurityUtility.GetListOfProviderNamesThatWereSearched(connection, command));
                }

                // Decrypt the CEK
                // We will simply bubble up the exception from the DecryptColumnEncryptionKey function.
                byte[] plaintextKey;
                try
                {
                    // to prevent conflicts between CEK caches, global providers should not use their own CEK caches
                    provider.ColumnEncryptionKeyCacheTtl = new TimeSpan(0);
                    plaintextKey = provider.DecryptColumnEncryptionKey(keyInfo.keyPath, keyInfo.algorithmName, keyInfo.encryptedKey);
                }
                catch (Exception e)
                {
                    // Generate a new exception and throw.
                    string keyHex = SqlSecurityUtility.GetBytesAsString(keyInfo.encryptedKey, fLast: true, countOfBytes: 10);
                    throw SQL.KeyDecryptionFailed(keyInfo.keyStoreName, keyHex, e);
                }

                encryptionKey = new SqlClientSymmetricKey(plaintextKey);

                // If the cache TTL is zero, don't even bother inserting to the cache.
                if (SqlConnection.ColumnEncryptionKeyCacheTtl != TimeSpan.Zero)
                {
                    // In case multiple threads reach here at the same time, the first one wins.
                    // The allocated memory will be reclaimed by Garbage Collector.
                    MemoryCacheEntryOptions options = new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = SqlConnection.ColumnEncryptionKeyCacheTtl
                    };
                    _cache.Set<SqlClientSymmetricKey>(cacheLookupKey, encryptionKey, options);
                }
            }

            return encryptionKey;
        }
    }
}
