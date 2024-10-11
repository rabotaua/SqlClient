// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Data.Common;
using Microsoft.Data.SqlClient;
using System;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Microsoft.Data.ProviderBase
{
    internal class DbMetaDataFactory
    {

        private DataSet _metaDataCollectionsDataSet;
        private string _normalizedServerVersion;
        private string _serverVersionString;
        // well known column names
        private const string CollectionNameKey = "CollectionName";
        private const string PopulationMechanismKey = "PopulationMechanism";
        private const string PopulationStringKey = "PopulationString";
        private const string MaximumVersionKey = "MaximumVersion";
        private const string MinimumVersionKey = "MinimumVersion";
        private const string DataSourceProductVersionNormalizedKey = "DataSourceProductVersionNormalized";
        private const string DataSourceProductVersionKey = "DataSourceProductVersion";
        private const string RestrictionNumberKey = "RestrictionNumber";
        private const string NumberOfRestrictionsKey = "NumberOfRestrictions";
        private const string RestrictionNameKey = "RestrictionName";
        private const string ParameterNameKey = "ParameterName";

        // population mechanisms
        private const string DataTableKey = "DataTable";
        private const string SqlCommandKey = "SQLCommand";
        private const string PrepareCollectionKey = "PrepareCollection";

        public DbMetaDataFactory(Stream xmlStream, string serverVersion, string normalizedServerVersion)
        {
            ADP.CheckArgumentNull(xmlStream, nameof(xmlStream));
            ADP.CheckArgumentNull(serverVersion, nameof(serverVersion));
            ADP.CheckArgumentNull(normalizedServerVersion, nameof(normalizedServerVersion));

            LoadDataSetFromXml(xmlStream);

            _serverVersionString = serverVersion;
            _normalizedServerVersion = normalizedServerVersion;
        }

        protected DataSet CollectionDataSet => _metaDataCollectionsDataSet;

        protected string ServerVersion => _serverVersionString;

        protected string ServerVersionNormalized => _normalizedServerVersion;

        protected DataTable CloneAndFilterCollection(string collectionName, string[] hiddenColumnNames)
        {
            DataTable destinationTable;
            DataColumn[] filteredSourceColumns;
            DataColumnCollection destinationColumns;
            DataRow newRow;

            DataTable sourceTable = _metaDataCollectionsDataSet.Tables[collectionName];

            if ((sourceTable == null) || (collectionName != sourceTable.TableName))
            {
                throw ADP.DataTableDoesNotExist(collectionName);
            }

            destinationTable = new DataTable(collectionName)
            {
                Locale = CultureInfo.InvariantCulture
            };
            destinationColumns = destinationTable.Columns;

            filteredSourceColumns = FilterColumns(sourceTable, hiddenColumnNames, destinationColumns);

            foreach (DataRow row in sourceTable.Rows)
            {
                if (SupportedByCurrentVersion(row))
                {
                    newRow = destinationTable.NewRow();
                    for (int i = 0; i < destinationColumns.Count; i++)
                    {
                        newRow[destinationColumns[i]] = row[filteredSourceColumns[i], DataRowVersion.Current];
                    }
                    destinationTable.Rows.Add(newRow);
                    newRow.AcceptChanges();
                }
            }

            return destinationTable;
        }

        public void Dispose() => Dispose(true);

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _normalizedServerVersion = null;
                _serverVersionString = null;
                _metaDataCollectionsDataSet.Dispose();
            }
        }

        private DataTable ExecuteCommand(DataRow requestedCollectionRow, string[] restrictions, DbConnection connection)
        {
            DataTable metaDataCollectionsTable = _metaDataCollectionsDataSet.Tables[DbMetaDataCollectionNames.MetaDataCollections];
            DataColumn populationStringColumn = metaDataCollectionsTable.Columns[PopulationStringKey];
            DataColumn numberOfRestrictionsColumn = metaDataCollectionsTable.Columns[NumberOfRestrictionsKey];
            DataColumn collectionNameColumn = metaDataCollectionsTable.Columns[CollectionNameKey];

            DataTable resultTable = null;

            Debug.Assert(requestedCollectionRow != null);
            string sqlCommand = requestedCollectionRow[populationStringColumn, DataRowVersion.Current] as string;
            int numberOfRestrictions = (int)requestedCollectionRow[numberOfRestrictionsColumn, DataRowVersion.Current];
            string collectionName = requestedCollectionRow[collectionNameColumn, DataRowVersion.Current] as string;

            if ((restrictions != null) && (restrictions.Length > numberOfRestrictions))
            {
                throw ADP.TooManyRestrictions(collectionName);
            }

            DbCommand command = connection.CreateCommand();
            SqlConnection castConnection = connection as SqlConnection;

            command.CommandText = sqlCommand;
            command.CommandTimeout = Math.Max(command.CommandTimeout, 180);
            command.Transaction = castConnection?.GetOpenTdsConnection()?.CurrentTransaction?.Parent;

            for (int i = 0; i < numberOfRestrictions; i++)
            {

                DbParameter restrictionParameter = command.CreateParameter();

                if ((restrictions != null) && (restrictions.Length > i) && (restrictions[i] != null))
                {
                    restrictionParameter.Value = restrictions[i];
                }
                else
                {
                    // This is where we have to assign null to the value of the parameter.
                    restrictionParameter.Value = DBNull.Value;
                }

                restrictionParameter.ParameterName = GetParameterName(collectionName, i + 1);
                restrictionParameter.Direction = ParameterDirection.Input;
                command.Parameters.Add(restrictionParameter);
            }

            DbDataReader reader = null;
            try
            {
                try
                {
                    reader = command.ExecuteReader();
                }
                catch (Exception e)
                {
                    if (!ADP.IsCatchableExceptionType(e))
                    {
                        throw;
                    }
                    throw ADP.QueryFailed(collectionName, e);
                }

                // Build a DataTable from the reader
                resultTable = new DataTable(collectionName)
                {
                    Locale = CultureInfo.InvariantCulture
                };

                DataTable schemaTable = reader.GetSchemaTable();
                foreach (DataRow row in schemaTable.Rows)
                {
                    resultTable.Columns.Add(row["ColumnName"] as string, (Type)row["DataType"] as Type);
                }
                object[] values = new object[resultTable.Columns.Count];
                while (reader.Read())
                {
                    reader.GetValues(values);
                    resultTable.Rows.Add(values);
                }
            }
            finally
            {
                reader?.Dispose();
            }
            return resultTable;
        }

        private DataColumn[] FilterColumns(DataTable sourceTable, string[] hiddenColumnNames, DataColumnCollection destinationColumns)
        {
            int columnCount = 0;
            foreach (DataColumn sourceColumn in sourceTable.Columns)
            {
                if (IncludeThisColumn(sourceColumn, hiddenColumnNames))
                {
                    columnCount++;
                }
            }

            if (columnCount == 0)
            {
                throw ADP.NoColumns();
            }

            int currentColumn = 0;
            DataColumn[] filteredSourceColumns = new DataColumn[columnCount];

            foreach (DataColumn sourceColumn in sourceTable.Columns)
            {
                if (IncludeThisColumn(sourceColumn, hiddenColumnNames))
                {
                    DataColumn newDestinationColumn = new(sourceColumn.ColumnName, sourceColumn.DataType);
                    destinationColumns.Add(newDestinationColumn);
                    filteredSourceColumns[currentColumn] = sourceColumn;
                    currentColumn++;
                }
            }
            return filteredSourceColumns;
        }

        internal DataRow FindMetaDataCollectionRow(string collectionName)
        {
            bool versionFailure;
            bool haveExactMatch;
            bool haveMultipleInexactMatches;
            string candidateCollectionName;

            DataTable metaDataCollectionsTable = _metaDataCollectionsDataSet.Tables[DbMetaDataCollectionNames.MetaDataCollections];
            if (metaDataCollectionsTable == null)
            {
                throw ADP.InvalidXml();
            }

            DataColumn collectionNameColumn = metaDataCollectionsTable.Columns[DbMetaDataColumnNames.CollectionName];

            if (collectionNameColumn == null || (typeof(string) != collectionNameColumn.DataType))
            {
                throw ADP.InvalidXmlMissingColumn(DbMetaDataCollectionNames.MetaDataCollections, DbMetaDataColumnNames.CollectionName);
            }

            DataRow requestedCollectionRow = null;
            string exactCollectionName = null;

            // find the requested collection
            versionFailure = false;
            haveExactMatch = false;
            haveMultipleInexactMatches = false;

            foreach (DataRow row in metaDataCollectionsTable.Rows)
            {

                candidateCollectionName = row[collectionNameColumn, DataRowVersion.Current] as string;
                if (string.IsNullOrEmpty(candidateCollectionName))
                {
                    throw ADP.InvalidXmlInvalidValue(DbMetaDataCollectionNames.MetaDataCollections, DbMetaDataColumnNames.CollectionName);
                }

                if (ADP.CompareInsensitiveInvariant(candidateCollectionName, collectionName))
                {
                    if (!SupportedByCurrentVersion(row))
                    {
                        versionFailure = true;
                    }
                    else
                    {
                        if (collectionName == candidateCollectionName)
                        {
                            if (haveExactMatch)
                            {
                                throw ADP.CollectionNameIsNotUnique(collectionName);
                            }
                            requestedCollectionRow = row;
                            exactCollectionName = candidateCollectionName;
                            haveExactMatch = true;
                        }
                        else if (!haveExactMatch)
                        {
                            // have an inexact match - ok only if it is the only one
                            if (exactCollectionName != null)
                            {
                                // can't fail here becasue we may still find an exact match
                                haveMultipleInexactMatches = true;
                            }
                            requestedCollectionRow = row;
                            exactCollectionName = candidateCollectionName;
                        }
                    }
                }
            }

            if (requestedCollectionRow == null)
            {
                if (!versionFailure)
                {
                    throw ADP.UndefinedCollection(collectionName);
                }
                else
                {
                    throw ADP.UnsupportedVersion(collectionName);
                }
            }

            if (!haveExactMatch && haveMultipleInexactMatches)
            {
                throw ADP.AmbiguousCollectionName(collectionName);
            }

            return requestedCollectionRow;

        }

        private void FixUpVersion(DataTable dataSourceInfoTable)
        {
            Debug.Assert(dataSourceInfoTable.TableName == DbMetaDataCollectionNames.DataSourceInformation);
            DataColumn versionColumn = dataSourceInfoTable.Columns[DataSourceProductVersionKey];
            DataColumn normalizedVersionColumn = dataSourceInfoTable.Columns[DataSourceProductVersionNormalizedKey];

            if ((versionColumn == null) || (normalizedVersionColumn == null))
            {
                throw ADP.MissingDataSourceInformationColumn();
            }

            if (dataSourceInfoTable.Rows.Count != 1)
            {
                throw ADP.IncorrectNumberOfDataSourceInformationRows();
            }

            DataRow dataSourceInfoRow = dataSourceInfoTable.Rows[0];

            dataSourceInfoRow[versionColumn] = _serverVersionString;
            dataSourceInfoRow[normalizedVersionColumn] = _normalizedServerVersion;
            dataSourceInfoRow.AcceptChanges();
        }


        private string GetParameterName(string neededCollectionName, int neededRestrictionNumber)
        {
            DataColumn collectionName = null;
            DataColumn parameterName = null;
            DataColumn restrictionName = null;
            DataColumn restrictionNumber = null;

            string result = null;

            DataTable restrictionsTable = _metaDataCollectionsDataSet.Tables[DbMetaDataCollectionNames.Restrictions];
            if (restrictionsTable != null)
            {
                DataColumnCollection restrictionColumns = restrictionsTable.Columns;
                if (restrictionColumns != null)
                {
                    collectionName = restrictionColumns[DbMetaDataFactory.CollectionNameKey];
                    parameterName = restrictionColumns[ParameterNameKey];
                    restrictionName = restrictionColumns[RestrictionNameKey];
                    restrictionNumber = restrictionColumns[RestrictionNumberKey];
                }
            }

            if ((parameterName == null) || (collectionName == null) || (restrictionName == null) || (restrictionNumber == null))
            {
                throw ADP.MissingRestrictionColumn();
            }

            foreach (DataRow restriction in restrictionsTable.Rows)
            {

                if (((string)restriction[collectionName] == neededCollectionName) &&
                    ((int)restriction[restrictionNumber] == neededRestrictionNumber) &&
                    (SupportedByCurrentVersion(restriction)))
                {

                    result = (string)restriction[parameterName];
                    break;
                }
            }

            if (result == null)
            {
                throw ADP.MissingRestrictionRow();
            }

            return result;
        }

        public virtual DataTable GetSchema(DbConnection connection, string collectionName, string[] restrictions)
        {
            Debug.Assert(_metaDataCollectionsDataSet != null);

            DataTable metaDataCollectionsTable = _metaDataCollectionsDataSet.Tables[DbMetaDataCollectionNames.MetaDataCollections];
            DataColumn populationMechanismColumn = metaDataCollectionsTable.Columns[PopulationMechanismKey];
            DataColumn collectionNameColumn = metaDataCollectionsTable.Columns[DbMetaDataColumnNames.CollectionName];

            string[] hiddenColumns;

            DataRow requestedCollectionRow = FindMetaDataCollectionRow(collectionName);
            string exactCollectionName = requestedCollectionRow[collectionNameColumn, DataRowVersion.Current] as string;

            if (!ADP.IsEmptyArray(restrictions))
            {

                for (int i = 0; i < restrictions.Length; i++)
                {
                    if ((restrictions[i] != null) && (restrictions[i].Length > 4096))
                    {
                        // use a non-specific error because no new beta 2 error messages are allowed
                        // TODO: will add a more descriptive error in RTM
                        throw ADP.NotSupported();
                    }
                }
            }

            string populationMechanism = requestedCollectionRow[populationMechanismColumn, DataRowVersion.Current] as string;

            DataTable requestedSchema;
            switch (populationMechanism)
            {

                case DataTableKey:
                    if (exactCollectionName == DbMetaDataCollectionNames.MetaDataCollections)
                    {
                        hiddenColumns = new string[2];
                        hiddenColumns[0] = PopulationMechanismKey;
                        hiddenColumns[1] = PopulationStringKey;
                    }
                    else
                    {
                        hiddenColumns = null;
                    }
                    // none of the datatable collections support restrictions
                    if (!ADP.IsEmptyArray(restrictions))
                    {
                        throw ADP.TooManyRestrictions(exactCollectionName);
                    }


                    requestedSchema = CloneAndFilterCollection(exactCollectionName, hiddenColumns);

                    // TODO: Consider an alternate method that doesn't involve special casing -- perhaps _prepareCollection

                    // for the data source information table we need to fix up the version columns at run time
                    // since the version is determined at run time
                    if (exactCollectionName == DbMetaDataCollectionNames.DataSourceInformation)
                    {
                        FixUpVersion(requestedSchema);
                    }
                    break;

                case SqlCommandKey:
                    requestedSchema = ExecuteCommand(requestedCollectionRow, restrictions, connection);
                    break;

                case PrepareCollectionKey:
                    requestedSchema = PrepareCollection(exactCollectionName, restrictions, connection);
                    break;

                default:
                    throw ADP.UndefinedPopulationMechanism(populationMechanism);
            }

            return requestedSchema;
        }

        private bool IncludeThisColumn(DataColumn sourceColumn, string[] hiddenColumnNames)
        {

            bool result = true;
            string sourceColumnName = sourceColumn.ColumnName;

            switch (sourceColumnName)
            {

                case MinimumVersionKey:
                case MaximumVersionKey:
                    result = false;
                    break;

                default:
                    if (hiddenColumnNames == null)
                    {
                        break;
                    }
                    for (int i = 0; i < hiddenColumnNames.Length; i++)
                    {
                        if (hiddenColumnNames[i] == sourceColumnName)
                        {
                            result = false;
                            break;
                        }
                    }
                    break;
            }

            return result;
        }

        private void LoadDataSetFromXml(Stream XmlStream)
        {
            _metaDataCollectionsDataSet = new DataSet
            {
                Locale = CultureInfo.InvariantCulture
            };
            XmlReaderSettings settings = new()
            {
                XmlResolver = null
            };
            using XmlReader reader = XmlReader.Create(XmlStream, settings);
            _metaDataCollectionsDataSet.ReadXml(reader);
        }

        protected virtual DataTable PrepareCollection(string collectionName, string[] restrictions, DbConnection connection)
        {
            throw ADP.NotSupported();
        }

        private bool SupportedByCurrentVersion(DataRow requestedCollectionRow)
        {
            bool result = true;
            DataColumnCollection tableColumns = requestedCollectionRow.Table.Columns;
            DataColumn versionColumn;
            object version;

            // check the minimum version first
            versionColumn = tableColumns[MinimumVersionKey];
            if (versionColumn != null)
            {
                version = requestedCollectionRow[versionColumn];
                if (version != null)
                {
                    if (version != DBNull.Value)
                    {
                        if (0 > string.Compare(_normalizedServerVersion, (string)version, StringComparison.OrdinalIgnoreCase))
                        {
                            result = false;
                        }
                    }
                }
            }

            // if the minimum version was ok what about the maximum version
            if (result)
            {
                versionColumn = tableColumns[MaximumVersionKey];
                if (versionColumn != null)
                {
                    version = requestedCollectionRow[versionColumn];
                    if (version != null)
                    {
                        if (version != DBNull.Value)
                        {
                            if (0 < string.Compare(_normalizedServerVersion, (string)version, StringComparison.OrdinalIgnoreCase))
                            {
                                result = false;
                            }
                        }
                    }
                }
            }
            return result;
        }
    }
}
