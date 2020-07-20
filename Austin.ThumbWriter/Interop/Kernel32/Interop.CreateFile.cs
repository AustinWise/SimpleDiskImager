// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;

internal static partial class Interop
{
    internal static partial class Kernel32
    {
        [DllImport(Libraries.Kernel32, EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
        private static extern unsafe SafeFileHandle CreateFilePrivate(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        internal static unsafe SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            FileShare dwShareMode,
            FileMode dwCreationDisposition,
            int dwFlagsAndAttributes)
        {
            return CreateFilePrivate(lpFileName, dwDesiredAccess, dwShareMode, IntPtr.Zero, dwCreationDisposition, dwFlagsAndAttributes, IntPtr.Zero);
        }
    }
}
