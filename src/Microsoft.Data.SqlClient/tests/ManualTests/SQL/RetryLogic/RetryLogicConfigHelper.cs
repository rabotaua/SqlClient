﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace Microsoft.Data.SqlClient.ManualTesting.Tests
{
    public class RetryLogicConfigHelper
    {
        private const string ConfigurationLoaderTypeName = "Microsoft.Data.SqlClient.SqlConfigurableRetryLogicLoader";
        private const string AppContextSwitchManagerTypeName = "Microsoft.Data.SqlClient.SqlAppContextSwitchManager";
        private const string ApplyContextSwitchesMethodName = "ApplyContextSwitches";
        private const string InterfaceCnnCfgTypeName = "Microsoft.Data.SqlClient.ISqlConfigurableRetryConnectionSection";
        private const string InterfaceCmdCfgTypeName = "Microsoft.Data.SqlClient.ISqlConfigurableRetryCommandSection";

        private const string CnnCfgTypeName = "Microsoft.Data.SqlClient.SqlConfigurableRetryConnectionSection";
        private const string CmdCfgTypeName = "Microsoft.Data.SqlClient.SqlConfigurableRetryCommandSection";
        private const string AppCtxCfgTypeName = "Microsoft.Data.SqlClient.AppContextSwitchOverridesSection";

        public const string RetryMethodName_Fix = "CreateFixedRetryProvider";
        public const string RetryMethodName_Inc = "CreateIncrementalRetryProvider";
        public const string RetryMethodName_Exp = "CreateExponentialRetryProvider";
        public const string RetryMethodName_None = "CreateNoneRetryProvider";

        private const string SqlRetryLogicTypeName = "Microsoft.Data.SqlClient.SqlRetryLogic";

        private const string CreateExceptionMethodName = "CreateException";
        private const string AddMethodName = "Add";

        public const string DefaultTransientErrors = "1204, 1205, 1222, 49918, 49919, 49920, 4060, 4221, 40143, 40613, 40501, 40540, 40197, 42108, 42109, 10929, 10928, 10060, 10054, 10053, 997, 233, 64, 20, 0, -2, 207, 102, 2812";

        private static readonly Random s_random = new Random();

        private static readonly Assembly s_sqlClientAssembly = typeof(SqlConnection).Assembly;
        private static readonly Type s_appContextSwitchManagerType = s_sqlClientAssembly.GetType(AppContextSwitchManagerTypeName);
        private static readonly Type s_sqlretrylogicType = s_sqlClientAssembly.GetType(SqlRetryLogicTypeName);
        private static readonly Type s_configurationLoaderType = s_sqlClientAssembly.GetType(ConfigurationLoaderTypeName);
        private static readonly Type s_sqlErrorType = typeof(SqlError);
        private static readonly Type s_sqlErrorCollectionType = typeof(SqlErrorCollection);
        private static readonly Type[] s_cfgLoaderParamsType = new Type[]
        {
            s_sqlClientAssembly.GetType(InterfaceCnnCfgTypeName),
            s_sqlClientAssembly.GetType(InterfaceCmdCfgTypeName),
            typeof(string), typeof(string)
        };
        private static readonly Type[] s_sqlErrorParamsType = new Type[]
        {
            typeof(int), typeof(byte), typeof(byte),
            typeof(string), typeof(string), typeof(string),
            typeof(int), typeof(Exception)
        };
        private static readonly ConstructorInfo s_loaderCtorInfo = s_configurationLoaderType.GetConstructor(s_cfgLoaderParamsType);
        private static readonly ConstructorInfo s_sqlErrorCtorInfo = s_sqlErrorType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, s_sqlErrorParamsType, null);
        private static readonly ConstructorInfo s_sqlErrorCollectionCtorInfo = s_sqlErrorCollectionType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        public static object CreateLoader(RetryLogicConfigs cnnConfig, RetryLogicConfigs cmdConfig)
        {
            var cnnCfgType = s_sqlClientAssembly.GetType(CnnCfgTypeName);
            var cnnCfgObj = Activator.CreateInstance(cnnCfgType);
            SetValue(cnnCfgObj, cnnCfgType, "DeltaTime", cnnConfig.DeltaTime);
            SetValue(cnnCfgObj, cnnCfgType, "MinTimeInterval", cnnConfig.MinTimeInterval);
            SetValue(cnnCfgObj, cnnCfgType, "MaxTimeInterval", cnnConfig.MaxTimeInterval);
            SetValue(cnnCfgObj, cnnCfgType, "NumberOfTries", cnnConfig.NumberOfTries);
            SetValue(cnnCfgObj, cnnCfgType, "AuthorizedSqlCondition", cnnConfig.AuthorizedSqlCondition);
            SetValue(cnnCfgObj, cnnCfgType, "TransientErrors", cnnConfig.TransientErrors);
            SetValue(cnnCfgObj, cnnCfgType, "RetryLogicType", cnnConfig.RetryLogicType);
            SetValue(cnnCfgObj, cnnCfgType, "RetryMethod", cnnConfig.RetryMethodName);

            var cmdCfgType = s_sqlClientAssembly.GetType(CmdCfgTypeName);
            var cmdCfgObj = Activator.CreateInstance(cmdCfgType);
            SetValue(cmdCfgObj, cmdCfgType, "DeltaTime", cmdConfig.DeltaTime);
            SetValue(cmdCfgObj, cmdCfgType, "MinTimeInterval", cmdConfig.MinTimeInterval);
            SetValue(cmdCfgObj, cmdCfgType, "MaxTimeInterval", cmdConfig.MaxTimeInterval);
            SetValue(cmdCfgObj, cmdCfgType, "NumberOfTries", cmdConfig.NumberOfTries);
            SetValue(cmdCfgObj, cmdCfgType, "AuthorizedSqlCondition", cmdConfig.AuthorizedSqlCondition);
            SetValue(cmdCfgObj, cmdCfgType, "TransientErrors", cmdConfig.TransientErrors);
            SetValue(cmdCfgObj, cmdCfgType, "RetryLogicType", cmdConfig.RetryLogicType);
            SetValue(cmdCfgObj, cmdCfgType, "RetryMethod", cmdConfig.RetryMethodName);

            return s_loaderCtorInfo.Invoke(new object[] { cnnCfgObj, cmdCfgObj, default, default });
        }

        public static void ApplyContextSwitchByManager(string name, bool value)
        {
#if NET6_0_OR_GREATER
            var appCtxType = s_sqlClientAssembly.GetType(AppCtxCfgTypeName);
            var appCtxObj = Activator.CreateInstance(appCtxType);
            SetValue(appCtxObj, appCtxType, "Value", string.Concat(name, "=", value));

            var method = s_appContextSwitchManagerType.GetMethod(ApplyContextSwitchesMethodName, BindingFlags.Static | BindingFlags.NonPublic);
            method.Invoke(null, new object[] { appCtxObj });
#else
            AppContext.SetSwitch(name, value);
#endif
        }

        public static SqlRetryLogicBaseProvider GetConnectionProvider(object loader)
            => GetValue<SqlRetryLogicBaseProvider>(loader, s_configurationLoaderType, "ConnectionProvider");

        public static SqlRetryLogicBaseProvider GetCommandProvider(object loader)
            => GetValue<SqlRetryLogicBaseProvider>(loader, s_configurationLoaderType, "CommandProvider");

        public static void AssessProvider(SqlRetryLogicBaseProvider provider, RetryLogicConfigs option) 
            => AssessRetryLogic(provider.RetryLogic, option);

        public static void AssessRetryLogic(SqlRetryLogicBase retryLogic, RetryLogicConfigs option)
        {
            Assert.Equal(option.DeltaTime, retryLogic.RetryIntervalEnumerator.GapTimeInterval);
            Assert.Equal(option.MinTimeInterval, retryLogic.RetryIntervalEnumerator.MinTimeInterval);
            Assert.Equal(option.MaxTimeInterval, retryLogic.RetryIntervalEnumerator.MaxTimeInterval);
            Assert.Equal(option.NumberOfTries, retryLogic.NumberOfTries);
            var preCondition = GetValue<Predicate<string>>(retryLogic, s_sqlretrylogicType, "PreCondition");
            if (string.IsNullOrEmpty(option.AuthorizedSqlCondition))
            {
                Assert.Null(preCondition);
            }
            else
            {
                Assert.NotNull(preCondition);
            }
        }

        public static T GetValue<T>(object obj, Type type, string propName, BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            => (T)type.GetProperty(propName, flags)?.GetValue(obj);

        public static void SetValue<T>(object obj, Type type, string propName, T value, BindingFlags flags = BindingFlags.Public | BindingFlags.Instance)
            => type.GetProperty(propName, flags)?.SetValue(obj, value);

        public static IEnumerable<object[]> GetInvalidInternalMethodNames()
        {
            yield return new object[] { RetryMethodName_Fix.ToUpper() };
            yield return new object[] { RetryMethodName_Inc.ToUpper() };
            yield return new object[] { RetryMethodName_Exp.ToUpper() };
            yield return new object[] { RetryMethodName_Fix.ToLower() };
            yield return new object[] { RetryMethodName_Inc.ToLower() };
            yield return new object[] { RetryMethodName_Exp.ToLower() };
        }

        public static IEnumerable<object[]> GetIivalidTimes()
        {
            var start = TimeSpan.FromSeconds(121);
            var end = TimeSpan.FromHours(24);
            for (int i = 0; i < 10; i++)
            {
                yield return new object[] { GenerateTimeSpan(start, end), GenerateTimeSpan(start, end), GenerateTimeSpan(start, end) };
            }
        }

        public static object ReturnLoaderAndProviders(RetryLogicConfigs cnnCfg, RetryLogicConfigs cmdCfg, out SqlRetryLogicBaseProvider cnnProvider, out SqlRetryLogicBaseProvider cmdProvider)
        {
            var loaderObj = CreateLoader(cnnCfg, cmdCfg);
            cnnProvider = GetConnectionProvider(loaderObj);
            cmdProvider = GetCommandProvider(loaderObj);
            return loaderObj;
        }

        public static RetryLogicConfigs CreateRandomConfig(string method, string authorizedSqlCondition = null, string transientErrors = DefaultTransientErrors)
        {
            TimeSpan start = TimeSpan.Zero;
            TimeSpan end = TimeSpan.FromSeconds(60);

            var min = GenerateTimeSpan(start, end);

            return new RetryLogicConfigs()
            {
                DeltaTime = GenerateTimeSpan(start, end),
                MinTimeInterval = min,
                MaxTimeInterval = GenerateTimeSpan(min, end),
                NumberOfTries = s_random.Next(1, 60),
                AuthorizedSqlCondition = authorizedSqlCondition,
                TransientErrors = transientErrors,
                RetryMethodName = method
            };
        }

        public static SqlException CreateSqlException(int errorNumber)
        {
            MethodInfo addSqlErrorMethod = typeof(SqlErrorCollection).GetMethod(AddMethodName, BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo createExceptionMethod = typeof(SqlException).GetMethod(CreateExceptionMethodName, BindingFlags.Static | BindingFlags.NonPublic,
                null, new Type[] { typeof(SqlErrorCollection), typeof(string) }, null);

            SqlError sqlError = s_sqlErrorCtorInfo.Invoke(new object[] { errorNumber, (byte)0, (byte)0, string.Empty, string.Empty, string.Empty, 0, null }) as SqlError;
            SqlErrorCollection sqlErrorCollection = s_sqlErrorCollectionCtorInfo.Invoke(new object[0] { }) as SqlErrorCollection;

            addSqlErrorMethod.Invoke(sqlErrorCollection, new object[] { sqlError });

            SqlException sqlException = createExceptionMethod.Invoke(null, new object[] { sqlErrorCollection, string.Empty }) as SqlException;

            return sqlException;
        }

        private static TimeSpan GenerateTimeSpan(TimeSpan start, TimeSpan end)
        {
            int max = (int)(end - start).TotalSeconds;
            return start.Add(TimeSpan.FromSeconds(s_random.Next(max)));
        }
    }

    public class RetryLogicConfigs : ICloneable
    {
        public TimeSpan DeltaTime { get; set; }
        public TimeSpan MaxTimeInterval { get; set; }
        public TimeSpan MinTimeInterval { get; set; }
        public int NumberOfTries { get; set; }
        public string TransientErrors { get; set; }
        public string AuthorizedSqlCondition { get; set; }
        public string RetryLogicType { get; set; }
        public string RetryMethodName { get; set; }

        public object Clone()
        {
            return MemberwiseClone();
        }
    }
}
