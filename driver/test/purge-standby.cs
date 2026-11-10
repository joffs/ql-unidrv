// purge-standby.exe: empties Windows' standby list (the file cache), so the next program start reads its files
// from disk as after a reboot. For the cold-start measurement in Windows Sandbox (cold-start-sandbox.ps1) ONLY:
// never run it on a real PC (it slows the whole machine for a while). Needs admin or SYSTEM.
// Build: csc /nologo /out:purge-standby.exe purge-standby.cs (the .NET Framework csc). Exit 0 = purged.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

static class PurgeStandby
{
    [DllImport("ntdll.dll")] static extern int NtSetSystemInformation(int cls, ref int info, int length);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, int access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool LookupPrivilegeValue(string system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, int length, IntPtr previous, IntPtr returned);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    struct TokenPrivileges { public int Count; public long Luid; public int Attributes; }

    static void Enable(string privilege)
    {
        IntPtr token;
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x28, out token)) throw new Exception("OpenProcessToken " + Marshal.GetLastWin32Error());
        var tp = new TokenPrivileges { Count = 1, Attributes = 2 };
        if (!LookupPrivilegeValue(null, privilege, out tp.Luid)) throw new Exception("LookupPrivilegeValue " + Marshal.GetLastWin32Error());
        AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
        int err = Marshal.GetLastWin32Error();
        if (err != 0) throw new Exception(privilege + " not held (" + err + ")");
    }

    static int Main()
    {
        try
        {
            Enable("SeProfileSingleProcessPrivilege");
            Enable("SeIncreaseQuotaPrivilege");
            int command = 4;   // SYSTEM_MEMORY_LIST_COMMAND MemoryPurgeStandbyList
            int status = NtSetSystemInformation(80, ref command, 4);   // SystemMemoryListInformation
            if (status != 0) { Console.Error.WriteLine("NtSetSystemInformation 0x" + status.ToString("X8")); return 1; }
            command = 3;       // MemoryFlushModifiedList first would be 3; done after so written pages leave too
            NtSetSystemInformation(80, ref command, 4);
            command = 4;
            NtSetSystemInformation(80, ref command, 4);
            Console.WriteLine("standby list purged");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); return 1; }
    }
}
