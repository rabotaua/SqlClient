// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

internal static partial class Interop
{
    internal static partial class Libraries
    {
        internal const string Crypt32 = "crypt32.dll";
        internal const string Kernel32 = "kernel32.dll";
        internal const string NtDll = "ntdll.dll";
#if !NET8_0_OR_GREATER
        internal const string SspiCli = "sspicli.dll";
#endif
    }
}
