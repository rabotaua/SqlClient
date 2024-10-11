// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO;
using Newtonsoft.Json;

namespace Microsoft.Data.SqlClient.TestUtilities
{
    public class Config
    {
        public string TCPConnectionString = null;
        public string NPConnectionString = null;
        public string TCPConnectionStringHGSVBS = null;
        public string TCPConnectionStringNoneVBS = null;
        public string TCPConnectionStringAASSGX = null;
        public string AADAuthorityURL = null;
        public string AADPasswordConnectionString = null;
        public string AADServicePrincipalId = null;
        public string AADServicePrincipalSecret = null;
        public string AzureKeyVaultURL = null;
        public string AzureKeyVaultTenantId = null;
        public string LocalDbAppName = null;
        public string LocalDbSharedInstanceName = null;
        public bool EnclaveEnabled = false;
        public bool TracingEnabled = false;
        public bool SupportsIntegratedSecurity = false;
        public bool ManagedIdentitySupported = true;
        public string FileStreamDirectory = null;
        public bool UseManagedSNIOnWindows = false;
        public string DNSCachingConnString = null;
        public string DNSCachingServerCR = null;  // this is for the control ring
        public string DNSCachingServerTR = null;  // this is for the tenant ring
        public bool IsAzureSynapse = false; // True for Azure Data Warehouse/Synapse
        public bool IsDNSCachingSupportedCR = false;  // this is for the control ring
        public bool IsDNSCachingSupportedTR = false;  // this is for the tenant ring
        public string EnclaveAzureDatabaseConnString = null;
        public string UserManagedIdentityClientId = null;
        public string PowerShellPath = null;
        public string KerberosDomainPassword = null;
        public string KerberosDomainUser = null;
        public bool IsManagedInstance = false;
        public string AliasName = null;
        public bool IsJsonSupported = false;
        public static Config Load(string configPath = @"config.json")
        {
            try
            {
                using (StreamReader r = new StreamReader(configPath))
                {
                    return JsonConvert.DeserializeObject<Config>(r.ReadToEnd());
                }
            }
            catch
            {
                throw;
            }
        }

        public static void UpdateConfig(Config updatedConfig, string configPath = @"config.json")
        {
            string config = JsonConvert.SerializeObject(updatedConfig);
            File.WriteAllText(configPath, config);
        }
    }
}
