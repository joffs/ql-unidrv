// Windows' own programs (PowerShell, cmd, shutdown) as this app starts them, and the PC's real architecture. The app is
// AnyCPU and normally runs natively, but Windows may run it as x86 under emulation (seen on ARM64 2026-10-30, the F7
// test: Setup.exe started from Explorer/RunOnce). Then System32 is redirected to SysWOW64 (a 32-bit PowerShell writes
// HKLM\SOFTWARE into WOW6432Node, bcdedit isn't there at all) and the environment says PROCESSOR_ARCHITECTURE=x86;
// a native .NET AnyCPU process on ARM64 even gets AMD64 there. Children inherit that environment, so scripts must not
// read the architecture from it either (install.ps1 looked for the amd64 driver store folder on ARM64).
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QlLabels
{
    static class NativeTools
    {
        // A program in Windows' System32, native also from a 32-bit process. elevated: started with Verb "runas": the
        // elevation service starts it from the path as given, natively, and doesn't know the Sysnative alias.
        public static string Path(string name, bool elevated = false)
        {
            return PathFor(name, Environment.Is64BitProcess, Environment.Is64BitOperatingSystem, elevated,
                           Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }

        internal static string PathFor(string name, bool is64BitProcess, bool is64BitWindows, bool elevated, string windows)
        {
            string folder = !is64BitProcess && is64BitWindows && !elevated ? "Sysnative" : "System32";
            string sub = string.Equals(name, "powershell.exe", StringComparison.OrdinalIgnoreCase) ? @"WindowsPowerShell\v1.0" : "";
            return System.IO.Path.Combine(System.IO.Path.Combine(windows, folder), System.IO.Path.Combine(sub, name));
        }

        [DllImport("kernel32.dll")] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);

        // ARM64 Windows, whatever this process runs as: the native machine from Windows (Windows 10 1709+), else the
        // system's own PROCESSOR_ARCHITECTURE value in the registry (not this process's environment).
        public static bool IsArm64()
        {
            try
            {
                ushort process, native;
                if (IsWow64Process2(Process.GetCurrentProcess().Handle, out process, out native)) return native == 0xAA64;
            }
            catch (EntryPointNotFoundException) { }
            return string.Equals(SystemArchitecture(), "ARM64", StringComparison.OrdinalIgnoreCase);
        }

        static string SystemArchitecture()
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                return key != null ? key.GetValue("PROCESSOR_ARCHITECTURE") as string : null;
        }
    }
}
