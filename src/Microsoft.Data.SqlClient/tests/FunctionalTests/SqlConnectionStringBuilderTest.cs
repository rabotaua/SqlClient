// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Microsoft.Data.SqlClient.Tests
{
    public partial class SqlConnectionStringBuilderTest
    {

        [Theory]
        [InlineData("Application Name = .Net Tests")]
        [InlineData("Application Intent = ReadOnly")]
        [InlineData("ApplicationIntent = ReadOnly")]
        [InlineData("Attestation Protocol = HGS")]
        [InlineData("Authentication = Active Directory Password ")]
        [InlineData("Authentication = ActiveDirectoryPassword ")]
        [InlineData("Authentication = Active Directory Integrated ")]
        [InlineData("Authentication = ActiveDirectoryIntegrated ")]
        [InlineData("Authentication = Active Directory Interactive ")]
        [InlineData("Authentication = ActiveDirectoryInteractive ")]
        [InlineData("Authentication = Active Directory Device Code Flow ")]
        [InlineData("Authentication = ActiveDirectoryDeviceCodeFlow ")]
        [InlineData("Authentication = Active Directory Service Principal ")]
        [InlineData("Authentication = ActiveDirectoryServicePrincipal ")]
        [InlineData("Authentication = Active Directory Managed Identity ")]
        [InlineData("Authentication = ActiveDirectoryManagedIdentity ")]
        [InlineData("Authentication = Active Directory MSI ")]
        [InlineData("Authentication = ActiveDirectoryMSI ")]
        [InlineData("Authentication = Active Directory Default ")]
        [InlineData("Authentication = ActiveDirectoryDefault ")]
        [InlineData("Authentication = Active Directory Workload Identity ")]
        [InlineData("Authentication = ActiveDirectoryWorkloadIdentity ")]
        [InlineData("Command Timeout = 5")]
        [InlineData("Command Timeout = 15")]
        [InlineData("Command Timeout = 0")]
        [InlineData("ConnectRetryCount = 5")]
        [InlineData("Connect Retry Count = 0")]
        [InlineData("ConnectRetryInterval = 10")]
        [InlineData("Connect Retry Interval = 20")]
        [InlineData("Connect Timeout = 30")]
        [InlineData("Connection Timeout = 5")]
        [InlineData("Connection Lifetime = 30")]
        [InlineData("Load Balance Timeout = 0")]
        [InlineData("Current Language = true")]
        [InlineData("Timeout = 0")]
        [InlineData("Column Encryption Setting = Enabled")]
        [InlineData("Data Source = randomserver.sys.local")]
        [InlineData("Address = np:localhost")]
        [InlineData("Network Address = (localdb)\\myInstance")]
        [InlineData("Server = randomserver.sys.local; uid = a; pwd = b")]
        [InlineData("Addr = randomserver.sys.local; User Id = a; Password = b")]
        [InlineData("Database = master")]
        [InlineData("Enclave Attestation Url = http://dymmyurl")]
        [InlineData("Encrypt = True")]
        [InlineData("Encrypt = False")]
        [InlineData("Encrypt = Strict")]
        [InlineData("Enlist = false")]
        [InlineData("Initial Catalog = Northwind; Failover Partner = randomserver.sys.local")]
        [InlineData("Initial Catalog = tempdb")]
        [InlineData("Integrated Security = true")]
        [InlineData("IPAddressPreference = IPv4First")]
        [InlineData("IPAddressPreference = IPv6First")]
        [InlineData("IPAddressPreference = UsePlatformDefault")]
        [InlineData("Trusted_Connection = false")]
        [InlineData("Max Pool Size = 50")]
        [InlineData("Min Pool Size = 20")]
        [InlineData("MultipleActiveResultSets = true")]
        [InlineData("Multiple Active Result Sets = false")]
        [InlineData("Multi Subnet Failover = true")]
        [InlineData("Packet Size = 12000")]
        [InlineData("Password = some@pass#!@123")]
        [InlineData("Persist Security Info = false")]
        [InlineData("PersistSecurityInfo = true")]
        [InlineData("PoolBlockingPeriod = Auto")]
        [InlineData("PoolBlockingperiod = NeverBlock")]
        [InlineData("Pooling = no")]
        [InlineData("Pooling = false")]
        [InlineData("Replication = true")]
        [InlineData("Transaction Binding = Explicit Unbind")]
        [InlineData("Trust Server Certificate = true")]
        [InlineData("TrustServerCertificate = false")]
        [InlineData("Type System Version = Latest")]
        [InlineData("User Instance = true")]
        [InlineData("Workstation ID = myworkstation")]
        [InlineData("WSID = myworkstation")]
        [InlineData("Host Name In Certificate = tds.test.com")]
        [InlineData("HostNameInCertificate = tds.test.com")]
        [InlineData("Server Certificate = c:\\test.cer")]
        [InlineData("ServerCertificate = c:\\test.cer")]
        [InlineData("Server SPN = server1")]
        [InlineData("ServerSPN = server2")]
        [InlineData("Failover Partner SPN = server3")]
        [InlineData("FailoverPartnerSPN = server4")]
        public void ConnectionStringTests(string connectionString)
        {
            ExecuteConnectionStringTests(connectionString);
        }

        public static readonly IEnumerable<object[]> ConnectionStringTestsNetFx_TestCases = new[]
        {
            new object[] { "Connection Reset = false" },
            new object[] { "Context Connection = false" },
            new object[] { "Network Library = dbmssocn" },
            new object[] { "Network = dbnmpntw" },
            new object[] { "Net = dbmsrpcn" },
            new object[] { "TransparentNetworkIPResolution = false" },
            new object[] { "Transparent Network IP Resolution = true" },
        };

        [Theory]
        [MemberData(nameof(ConnectionStringTestsNetFx_TestCases))]
        #if NETFRAMEWORK
        public void ConnectionStringTestsNetFx_OnNetFx_Success(string connectionString)
        {
            ExecuteConnectionStringTests(connectionString);
        }
        #else
        public void ConnectionStringTestsNetFx_OnNetCore_Throws(string connectionString)
        {
            // Act
            Action action = () => _ = new SqlConnectionStringBuilder(connectionString);

            // Assert
            Assert.Throws<NotSupportedException>(action);
        }
        #endif

        [Fact]
        public void SetInvalidApplicationIntent_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ApplicationIntent invalid = (ApplicationIntent)Enum.GetValues(typeof(ApplicationIntent)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.ApplicationIntent = invalid);
            Assert.Contains("ApplicationIntent", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidAttestationProtocol_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionAttestationProtocol invalid = (SqlConnectionAttestationProtocol)Enum.GetValues(typeof(SqlConnectionAttestationProtocol)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.AttestationProtocol = invalid);
            Assert.Contains("SqlConnectionAttestationProtocol", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidAuthentication_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlAuthenticationMethod invalid = (SqlAuthenticationMethod)Enum.GetValues(typeof(SqlAuthenticationMethod)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Authentication = invalid);
            Assert.Contains("SqlAuthenticationMethod", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidColumnEncryptionSetting_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionColumnEncryptionSetting invalid = (SqlConnectionColumnEncryptionSetting)Enum.GetValues(typeof(SqlConnectionColumnEncryptionSetting)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.ColumnEncryptionSetting = invalid);
            Assert.Contains("SqlConnectionColumnEncryptionSetting", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidConnectTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectTimeout = -1);
            Assert.Contains("connect timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidCommandTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.CommandTimeout = -1);
            Assert.Contains("command timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void SetInvalidConnectRetryCount_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectRetryCount = invalid);
            Assert.Contains("connect retry count", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void SetInvalidConnectRetryInterval_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectRetryInterval = invalid);
            Assert.Contains("connect retry interval", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidIPAddressPreference_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionIPAddressPreference invalid = (SqlConnectionIPAddressPreference)Enum.GetValues(typeof(SqlConnectionIPAddressPreference)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.IPAddressPreference = invalid);
            Assert.Contains("SqlConnectionIPAddressPreference", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidPoolBlockingPeriod_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            PoolBlockingPeriod invalid = (PoolBlockingPeriod)Enum.GetValues(typeof(PoolBlockingPeriod)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.PoolBlockingPeriod = invalid);
            Assert.Contains("PoolBlockingPeriod", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidLoadBalanceTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.LoadBalanceTimeout = -1);
            Assert.Contains("load balance timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidMaxPoolSize_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.MaxPoolSize = 0);
            Assert.Contains("max pool size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidMinPoolSize_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.MinPoolSize = -1);
            Assert.Contains("min pool size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(511)]
        [InlineData(32769)]
        [InlineData(int.MaxValue)]
        public void SetInvalidPacketSize_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.PacketSize = invalid);
            Assert.Contains("packet size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("AttachDBFilename", "somefile.db")]
        public void SetKeyword(string keyword, string value)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder[keyword] = value;
            Assert.Equal(builder[keyword], value);
        }

        [Fact]
        public void SetNotSupportedKeyword_Throws()
        {
            // We may want to remove the unreachable code path for default in the GetIndex(keyword) method already throws UnsupportedKeyword
            // so default: throw UnsupportedKeyword(keyword) is never reached unless it's a supported keyword, but it's not handled in the switch case.

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder["NotSupported"] = "not important");
            Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidApplicationIntent_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ApplicationIntent invalid = (ApplicationIntent)Enum.GetValues(typeof(ApplicationIntent)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.ApplicationIntent = invalid);
            Assert.Contains("ApplicationIntent", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidAttestationProtocol_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionAttestationProtocol invalid = (SqlConnectionAttestationProtocol)Enum.GetValues(typeof(SqlConnectionAttestationProtocol)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.AttestationProtocol = invalid);
            Assert.Contains("SqlConnectionAttestationProtocol", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidAuthentication_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlAuthenticationMethod invalid = (SqlAuthenticationMethod)Enum.GetValues(typeof(SqlAuthenticationMethod)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.Authentication = invalid);
            Assert.Contains("SqlAuthenticationMethod", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidColumnEncryptionSetting_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionColumnEncryptionSetting invalid = (SqlConnectionColumnEncryptionSetting)Enum.GetValues(typeof(SqlConnectionColumnEncryptionSetting)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.ColumnEncryptionSetting = invalid);
            Assert.Contains("SqlConnectionColumnEncryptionSetting", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidConnectTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectTimeout = -1);
            Assert.Contains("connect timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidCommandTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.CommandTimeout = -1);
            Assert.Contains("command timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void SetInvalidConnectRetryCount_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectRetryCount = invalid);
            Assert.Contains("connect retry count", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(256)]
        public void SetInvalidConnectRetryInterval_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.ConnectRetryInterval = invalid);
            Assert.Contains("connect retry interval", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidIPAddressPreference_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            SqlConnectionIPAddressPreference invalid = (SqlConnectionIPAddressPreference)Enum.GetValues(typeof(SqlConnectionIPAddressPreference)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.IPAddressPreference = invalid);
            Assert.Contains("SqlConnectionIPAddressPreference", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidPoolBlockingPeriod_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            PoolBlockingPeriod invalid = (PoolBlockingPeriod)Enum.GetValues(typeof(PoolBlockingPeriod)).Length + 1;
            ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() => builder.PoolBlockingPeriod = invalid);
            Assert.Contains("PoolBlockingPeriod", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidLoadBalanceTimeout_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.LoadBalanceTimeout = -1);
            Assert.Contains("load balance timeout", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidMaxPoolSize_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.MaxPoolSize = 0);
            Assert.Contains("max pool size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void SetInvalidMinPoolSize_Throws()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.MinPoolSize = -1);
            Assert.Contains("min pool size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(511)]
        [InlineData(32769)]
        [InlineData(int.MaxValue)]
        public void SetInvalidPacketSize_Throws(int invalid)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();

            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder.PacketSize = invalid);
            Assert.Contains("packet size", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Theory]
        [InlineData("AttachDBFilename", "somefile.db")]
        public void SetKeyword(string keyword, string value)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            builder[keyword] = value;
            Assert.Equal(builder[keyword], value);
        }

        [Fact]
        public void SetNotSupportedKeyword_Throws()
        {
            // We may want to remove the unreachable code path for default in the GetIndex(keyword) method already throws UnsupportedKeyword
            // so default: throw UnsupportedKeyword(keyword) is never reached unless it's a supported keyword, but it's not handled in the switch case.

            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder();
            ArgumentException ex = Assert.Throws<ArgumentException>(() => builder["NotSupported"] = "not important");
            Assert.Contains("not supported", ex.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void UnexpectedKeywordRetrieval()
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder("Data Source=localhost");
            Assert.Throws<ArgumentException>(() => builder["RandomKeyword"]);
        }

        [Theory]
        [InlineData(@"C:\test\attach.mdf", "AttachDbFilename=C:\\test\\attach.mdf")]
        [InlineData(@"C:\test\attach.mdf;", "AttachDbFilename=\"C:\\test\\attach.mdf;\"")]
        public void ConnectionString_AttachDbFileName_Plain(string value, string expected)
        {
            var builder = new SqlConnectionStringBuilder();
            builder.AttachDBFilename = value;
            Assert.Equal(expected, builder.ConnectionString);
        }

        [Theory]
        [PlatformSpecific(TestPlatforms.Windows)]
        [InlineData(@"|DataDirectory|\attach.mdf",
                    @"AttachDbFilename=|DataDirectory|\attach.mdf",
                    @"C:\test\")]
        [InlineData(@"|DataDirectory|\attach.mdf",
                    @"AttachDbFilename=|DataDirectory|\attach.mdf",
                    @"C:\test")]
        [InlineData(@"|DataDirectory|attach.mdf",
                    @"AttachDbFilename=|DataDirectory|attach.mdf",
                    @"C:\test")]
        [InlineData(@"|DataDirectory|attach.mdf",
                    @"AttachDbFilename=|DataDirectory|attach.mdf",
                    @"C:\test\")]
        [InlineData(@"  |DataDirectory|attach.mdf",
                    "AttachDbFilename=\"  |DataDirectory|attach.mdf\"",
                    @"C:\test\")]
        [InlineData(@"|DataDirectory|attach.mdf  ",
                    "AttachDbFilename=\"|DataDirectory|attach.mdf  \"",
                    @"C:\test\")]
        public void ConnectionStringBuilder_AttachDbFileName_DataDirectory(string value, string expected, string dataDirectory)
        {
            AppDomain.CurrentDomain.SetData("DataDirectory", dataDirectory);

            var builder = new SqlConnectionStringBuilder();
            builder.AttachDBFilename = value;
            Assert.Equal(expected, builder.ConnectionString);
        }

        [Fact]
        public void SetEncryptInConnectionStringMapsToString()
        {
            var data = new List<Tuple<string, SqlConnectionEncryptOption>>
            {
                Tuple.Create("Encrypt=yes", SqlConnectionEncryptOption.Mandatory),
                Tuple.Create("Encrypt=no", SqlConnectionEncryptOption.Optional),
                Tuple.Create("Encrypt=true", SqlConnectionEncryptOption.Mandatory),
                Tuple.Create("Encrypt=false", SqlConnectionEncryptOption.Optional),
                Tuple.Create("Encrypt=mandatory", SqlConnectionEncryptOption.Mandatory),
                Tuple.Create("Encrypt=optional", SqlConnectionEncryptOption.Optional),
                Tuple.Create("Encrypt=strict", SqlConnectionEncryptOption.Strict)
            };

            foreach (var item in data)
            {
                string connectionString = item.Item1;
                SqlConnectionEncryptOption expected = item.Item2;
                SqlConnection sqlConnection = new(connectionString);
                SqlConnectionStringBuilder scsb = new(sqlConnection.ConnectionString);
                Assert.Equal(expected, scsb.Encrypt);
            }
        }

        [Fact]
        public void SetEncryptOnConnectionBuilderMapsToString()
        {
            var data = new List<Tuple<string, SqlConnectionEncryptOption>>
            {
                Tuple.Create("Encrypt=True", SqlConnectionEncryptOption.Mandatory),
                Tuple.Create("Encrypt=False", SqlConnectionEncryptOption.Optional),
                Tuple.Create("Encrypt=Strict", SqlConnectionEncryptOption.Strict)
            };

            foreach (Tuple<string, SqlConnectionEncryptOption> item in data)
            {
                string expected = item.Item1;
                SqlConnectionEncryptOption option = item.Item2;
                SqlConnectionStringBuilder scsb = new();
                scsb.Encrypt = option;
                Assert.Equal(expected, scsb.ConnectionString);
            }
        }

        [Fact]
        public void AbleToSetHostNameInCertificate()
        {
            var testhostname = "somedomain.net";
            var builder = new SqlConnectionStringBuilder
            {
                HostNameInCertificate = testhostname
            };
            Assert.Equal(testhostname, builder.HostNameInCertificate);
        }

        [Fact]
        public void ConnectionBuilderEncryptBackwardsCompatibility()
        {
            SqlConnectionStringBuilder builder = new();
            builder.Encrypt = false;
            Assert.Equal("Encrypt=False", builder.ConnectionString);
            Assert.False(builder.Encrypt);

            builder.Encrypt = true;
            Assert.Equal("Encrypt=True", builder.ConnectionString);
            Assert.True(builder.Encrypt);

            builder.Encrypt = SqlConnectionEncryptOption.Optional;
            Assert.Equal("Encrypt=False", builder.ConnectionString);
            Assert.False(builder.Encrypt);

            builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
            Assert.Equal("Encrypt=True", builder.ConnectionString);
            Assert.True(builder.Encrypt);

            builder.Encrypt = SqlConnectionEncryptOption.Strict;
            Assert.Equal("Encrypt=Strict", builder.ConnectionString);
            Assert.True(builder.Encrypt);

            builder.Encrypt = null;
            Assert.Equal("Encrypt=True", builder.ConnectionString);
            Assert.True(builder.Encrypt);
        }

        [Fact]
        public void EncryptParserValidValuesPropertyIndexerForEncryptionOption()
        {
            SqlConnectionStringBuilder builder = new();
            builder["Encrypt"] = SqlConnectionEncryptOption.Strict;
            CheckEncryptType(builder, SqlConnectionEncryptOption.Strict);
            builder["Encrypt"] = SqlConnectionEncryptOption.Optional;
            CheckEncryptType(builder, SqlConnectionEncryptOption.Optional);
            builder["Encrypt"] = SqlConnectionEncryptOption.Mandatory;
            CheckEncryptType(builder, SqlConnectionEncryptOption.Mandatory);
        }

        [Theory]
        [InlineData("true", "True")]
        [InlineData("mandatory", "True")]
        [InlineData("yes", "True")]
        [InlineData("false", "False")]
        [InlineData("optional", "False")]
        [InlineData("no", "False")]
        [InlineData("strict", "Strict")]
        public void EncryptParserValidValuesParsesSuccessfully(string value, string expectedValue)
            => Assert.Equal(expectedValue, SqlConnectionEncryptOption.Parse(value).ToString());

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void EncryptParserValidValuesPropertyIndexerForBoolean(bool value)
        {
            SqlConnectionStringBuilder builder = new();
            builder["Encrypt"] = value;
            CheckEncryptType(builder, value ? SqlConnectionEncryptOption.Mandatory : SqlConnectionEncryptOption.Optional);
        }

        [Theory]
        [InlineData("something")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("  true  ")]
        public void EncryptParserInvalidValuesThrowsException(string value)
            => Assert.Throws<ArgumentException>(() => SqlConnectionEncryptOption.Parse(value));

        [Theory]
        [InlineData("true", "True")]
        [InlineData("mandatory", "True")]
        [InlineData("yes", "True")]
        [InlineData("false", "False")]
        [InlineData("optional", "False")]
        [InlineData("no", "False")]
        [InlineData("strict", "Strict")]
        public void EncryptTryParseValidValuesReturnsTrue(string value, string expectedValue)
        {
            Assert.True(SqlConnectionEncryptOption.TryParse(value, out SqlConnectionEncryptOption result));
            Assert.Equal(expectedValue, result.ToString());
        }

        [Theory]
        [InlineData("something")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("  true  ")]
        public void EncryptTryParseInvalidValuesReturnsFalse(string value)
        {
            Assert.False(SqlConnectionEncryptOption.TryParse(value, out SqlConnectionEncryptOption result));
            Assert.Null(result);
        }

        #region SqlConnectionEncryptOptionCoverterTests
        [Fact]
        public void ConnectionStringFromJsonTests()
        {
            UserDbConnectionStringSettings settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("false");
            Assert.Equal(SqlConnectionEncryptOption.Optional, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("true");
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("strict");
            Assert.Equal(SqlConnectionEncryptOption.Strict, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("mandatory");
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("optional");
            Assert.Equal(SqlConnectionEncryptOption.Optional, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("yes");
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, settings.UserDb.UserComponents.Encrypt);

            settings = LoadSettingsFromJsonStream<UserDbConnectionStringSettings>("no");
            Assert.Equal(SqlConnectionEncryptOption.Optional, settings.UserDb.UserComponents.Encrypt);
        }

        [Theory]
        [InlineData("absolutely")]
        [InlineData("affirmative")]
        [InlineData("never")]
        [InlineData("always")]
        [InlineData("none")]
        [InlineData(" for sure ")]
        public void ConnectionStringFromJsonThrowsException(string value)
        {
            ExecuteConnectionStringFromJsonThrowsException(value);
        }

        [Fact]
        public void SqlConnectionEncryptOptionConverterCanConvertFromTest()
        {
            // Get a converter
            SqlConnectionEncryptOption option = SqlConnectionEncryptOption.Parse("false");
            TypeConverter converter = TypeDescriptor.GetConverter(option.GetType());
            // Use the converter to determine if can convert from string data type
            Assert.True(converter.CanConvertFrom(null, typeof(string)), "Expecting to convert from a string type.");
            // Use the same converter to determine if can convert from int or bool data types
            Assert.False(converter.CanConvertFrom(null, typeof(int)), "Not expecting to convert from integer type.");
            Assert.False(converter.CanConvertFrom(null, typeof(bool)), "Not expecting to convert from boolean type.");
        }

        [Fact]
        public void SqlConnectionEncryptOptionConverterCanConvertToTest()
        {
            // Get a converter
            SqlConnectionEncryptOption option = SqlConnectionEncryptOption.Parse("false");
            TypeConverter converter = TypeDescriptor.GetConverter(option.GetType());
            // Use the converter to check if can convert from stirng
            Assert.True(converter.CanConvertTo(null, typeof(string)), "Expecting to convert to a string type.");
            // Use the same convert to check if can convert to int or bool
            Assert.False(converter.CanConvertTo(null, typeof(int)), "Not expecting to convert from integer type.");
            Assert.False(converter.CanConvertTo(null, typeof(bool)), "Not expecting to convert from boolean type.");
        }

        [Fact]
        public void SqlConnectionEncryptOptionConverterConvertFromTest()
        {
            // Create a converter
            SqlConnectionEncryptOption option = SqlConnectionEncryptOption.Parse("false");
            TypeConverter converter = TypeDescriptor.GetConverter(option.GetType());
            // Use the converter to convert all possible valid values
            Assert.Equal(SqlConnectionEncryptOption.Parse("false"), converter.ConvertFrom("false"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("true"), converter.ConvertFrom("true"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("strict"), converter.ConvertFrom("strict"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("mandatory"), converter.ConvertFrom("mandatory"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("optional"), converter.ConvertFrom("optional"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("yes"), converter.ConvertFrom("yes"));
            Assert.Equal(SqlConnectionEncryptOption.Parse("no"), converter.ConvertFrom("no"));
            // Use the converter to covert invalid value
            Assert.Throws<ArgumentException>(() => converter.ConvertFrom("affirmative"));
            // Use the same converter to convert from bad data types
            Assert.Throws<ArgumentException>(() => converter.ConvertFrom(1));
            Assert.Throws<ArgumentException>(() => converter.ConvertFrom(true));
        }

        [Fact]
        public void SqlConnectionEncryptOptionConverterConvertToTest()
        {
            // Get a converter
            SqlConnectionEncryptOption option = SqlConnectionEncryptOption.Parse("false");
            TypeConverter converter = TypeDescriptor.GetConverter(option.GetType());
            // Use the converter to convert all possible valid values to string
            Assert.Equal("False", converter.ConvertTo(SqlConnectionEncryptOption.Parse("false"), typeof(string)));
            Assert.Equal("True", converter.ConvertTo(SqlConnectionEncryptOption.Parse("true"), typeof(string)));
            Assert.Equal("Strict", converter.ConvertTo(SqlConnectionEncryptOption.Parse("strict"), typeof(string)));
            Assert.Equal("True", converter.ConvertTo(SqlConnectionEncryptOption.Parse("mandatory"), typeof(string)));
            Assert.Equal("False", converter.ConvertTo(SqlConnectionEncryptOption.Parse("optional"), typeof(string)));
            Assert.Equal("True", converter.ConvertTo(SqlConnectionEncryptOption.Parse("yes"), typeof(string)));
            Assert.Equal("False", converter.ConvertTo(SqlConnectionEncryptOption.Parse("no"), typeof(string)));
            // Use the same converter to try convert to bad data types
            Assert.Throws<ArgumentException>(() => converter.ConvertTo(SqlConnectionEncryptOption.Parse("false"), typeof(int)));
            Assert.Throws<ArgumentException>(() => converter.ConvertTo(SqlConnectionEncryptOption.Parse("false"), typeof(bool)));
        }

        internal class UserDbConnectionStringSettings
        {
            [Required]
            public UserSqlConnectionString UserDb { get; set; }
        }

        internal class UserSqlConnectionString
        {
            public SqlConnectionStringBuilder UserComponents { get; set; } = new();

            public override string ToString()
            {
                return UserComponents!.ConnectionString;
            }
        }

        internal static void ExecuteConnectionStringFromJsonThrowsException(string encryptOption)
        {
            var exception = Assert.Throws<InvalidOperationException>(() => LoadSettingsFromJsonStream<UserDbConnectionStringSettings>(encryptOption));
            Assert.Contains("Failed to convert configuration", exception.Message, StringComparison.Ordinal);
        }

        private static TSettings LoadSettingsFromJsonStream<TSettings>(string encryptOption) where TSettings : class
        {
            TSettings settingsOut = null;

            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((ctx, configBuilder) =>
                {
                    // Note: Inside string interpolation, a { should be {{ and a } should be }}
                    // First, declare a stringified JSON
                    var json = $"{{ \"UserDb\": {{ \"UserComponents\": {{ \"NetworkLibrary\": \"DBMSSOCN\", \"UserID\": \"user\", \"Password\": \"password\", \"DataSource\": \"localhost\", \"InitialCatalog\": \"catalog\", \"Encrypt\": \"{encryptOption}\" }}}}}}";
                    // Load the stringified JSON as a stream into the configuration builder
                    configBuilder.AddJsonStream(new MemoryStream(Encoding.ASCII.GetBytes(json)));
                    configBuilder.AddEnvironmentVariables();
                })
                .ConfigureServices((ctx, services) =>
                {
                    var configuration = ctx.Configuration;
                    services.AddOptions();
                    services.Configure<TSettings>(ctx.Configuration);
                    settingsOut = configuration.Get<TSettings>();
                })
                .Build();

            return settingsOut;
        }
        #endregion

        internal void ExecuteConnectionStringTests(string connectionString)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);
            string retrievedString = builder.ConnectionString;
            SqlConnectionStringBuilder builder2 = new SqlConnectionStringBuilder(retrievedString);

            Assert.Equal(builder, builder2);
            Assert.NotNull(builder.Values);
            Assert.True(builder.Values.Count > 0);
            foreach (string key in builder2.Keys)
            {
                Assert.True(builder.TryGetValue(key, out object valueBuilder1));
                Assert.True(builder2.TryGetValue(key, out object valueBuilder2));
                Assert.Equal(valueBuilder1, valueBuilder2);
                Assert.True(builder2.ContainsKey(key));
            }
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                Assert.NotNull(connection);
            }
        }

        internal static void CheckEncryptType(SqlConnectionStringBuilder builder, SqlConnectionEncryptOption expectedValue)
        {
            Assert.IsType<SqlConnectionEncryptOption>(builder.Encrypt);
            Assert.Equal(expectedValue, builder.Encrypt);
        }

    }
}
