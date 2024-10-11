// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#if !NET8_0_OR_GREATER

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

internal static partial class Interop
{
    internal static partial class NetSecurityNative
    {
        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_ReleaseGssBuffer")]
        internal static extern void ReleaseGssBuffer(
            IntPtr bufferPtr,
            ulong length);

        [DllImport(Interop.Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_EnsureGssInitialized")]
        private static extern int EnsureGssInitialized();

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_DisplayMinorStatus")]
        internal static extern Status DisplayMinorStatus(
            out Status minorStatus,
            Status statusValue,
            ref GssBuffer buffer);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_DisplayMajorStatus")]
        internal static extern Status DisplayMajorStatus(
            out Status minorStatus,
            Status statusValue,
            ref GssBuffer buffer);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_ImportUserName")]
        internal static extern Status ImportUserName(
            out Status minorStatus,
            string inputName,
            int inputNameByteCount,
            out SafeGssNameHandle outputName);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_ImportPrincipalName")]
        internal static extern Status ImportPrincipalName(
            out Status minorStatus,
            string inputName,
            int inputNameByteCount,
            out SafeGssNameHandle outputName);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_ReleaseName")]
        internal static extern Status ReleaseName(
            out Status minorStatus,
            ref IntPtr inputName);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_InitiateCredSpNego")]
        internal static extern Status InitiateCredSpNego(
            out Status minorStatus,
            SafeGssNameHandle desiredName,
            out SafeGssCredHandle outputCredHandle);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_InitiateCredWithPassword")]
        internal static extern Status InitiateCredWithPassword(
            out Status minorStatus,
            bool isNtlm,
            SafeGssNameHandle desiredName,
            string password,
            int passwordLen,
            out SafeGssCredHandle outputCredHandle);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_ReleaseCred")]
        internal static extern Status ReleaseCred(
            out Status minorStatus,
            ref IntPtr credHandle);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_InitSecContext")]
        internal static extern Status InitSecContext(
            out Status minorStatus,
            SafeGssCredHandle initiatorCredHandle,
            ref SafeGssContextHandle contextHandle,
            bool isNtlmOnly,
            SafeGssNameHandle targetName,
            uint reqFlags,
            byte[] inputBytes,
            int inputLength,
            ref GssBuffer token,
            out uint retFlags,
            out int isNtlmUsed);

        [DllImport(Libraries.NetSecurityNative, EntryPoint = "NetSecurityNative_DeleteSecContext")]
        internal static extern Status DeleteSecContext(
            out Status minorStatus,
            ref IntPtr contextHandle);

        internal enum Status : uint
        {
            GSS_S_COMPLETE = 0,
            GSS_S_CONTINUE_NEEDED = 1
        }

        [Flags]
        internal enum GssFlags : uint
        {
            GSS_C_DELEG_FLAG = 0x1,
            GSS_C_MUTUAL_FLAG = 0x2,
            GSS_C_REPLAY_FLAG = 0x4,
            GSS_C_SEQUENCE_FLAG = 0x8,
            GSS_C_CONF_FLAG = 0x10,
            GSS_C_INTEG_FLAG = 0x20,
            GSS_C_ANON_FLAG = 0x40,
            GSS_C_PROT_READY_FLAG = 0x80,
            GSS_C_TRANS_FLAG = 0x100,
            GSS_C_DCE_STYLE = 0x1000,
            GSS_C_IDENTIFY_FLAG = 0x2000,
            GSS_C_EXTENDED_ERROR_FLAG = 0x4000,
            GSS_C_DELEG_POLICY_FLAG = 0x8000
        }

        // This constructor is added to address the issue with net6 regarding 
        // Shim gss api on Linux to delay loading libgssapi_krb5.so
        // issue https://github.com/dotnet/SqlClient/issues/1390
        // dotnet runtime issue https://github.com/dotnet/runtime/pull/55037
        static NetSecurityNative()
        {
            if (Environment.Version.Major >= 6)
            {
                EnsureGssInitialized();
            }
        }
    }
}

#endif
