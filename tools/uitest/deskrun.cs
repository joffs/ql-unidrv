// deskrun: runs a UI test program on a Windows desktop of its own, so none of its windows (nor those of the
// processes it starts) can take the user's keyboard focus or come to the front, on any monitor. Meanwhile it checks
// the user's foreground window every 25 ms, logs every change with its process, and stops the test at once when the
// foreground goes to a process of the test. Output of the test program is passed through.
//
//   deskrun.exe <program.exe> [arguments...]
//   exit code: the program's; 99 = the focus went to a test process (stopped); 98 = could not start
//
// The test and what it starts get QL_TEST_SILENT=1: no sounds (QL Unidrv's message box; a desktop doesn't separate sound).
// Screenshots inside the test: PrintWindow(hwnd, dc, 2) works on this desktop; screen captures (CopyFromScreen) don't.
// Build: tools\uitest\build.cmd (inbox csc, no SDK).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class DeskRun
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll")] static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")] static extern bool GetExitCodeProcess(IntPtr h, out uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
    [DllImport("kernel32.dll")] static extern bool SetHandleInformation(IntPtr h, int mask, int flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);

    static string Describe(IntPtr h)
    {
        uint pid; GetWindowThreadProcessId(h, out pid);
        string name = "?";
        try { name = Process.GetProcessById((int)pid).ProcessName; } catch { }
        var cls = new StringBuilder(128); GetClassName(h, cls, 128);
        return name + " (pid " + pid + ", class " + cls + ")";
    }

    // The test's processes: the program and everything started below it.
    static HashSet<int> Tree(int root)
    {
        var tree = new HashSet<int> { root };
        try
        {
            using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
            {
                var all = new List<KeyValuePair<int, int>>();
                foreach (System.Management.ManagementObject o in q.Get()) all.Add(new KeyValuePair<int, int>(Convert.ToInt32(o["ProcessId"]), Convert.ToInt32(o["ParentProcessId"])));
                for (bool grew = true; grew; ) { grew = false; foreach (var p in all) if (tree.Contains(p.Value) && tree.Add(p.Key)) grew = true; }
            }
        }
        catch { }
        return tree;
    }

    static string Quote(string a)
    {
        if (a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return a;
        var s = new StringBuilder("\"");
        int slashes = 0;
        foreach (char c in a)
        {
            if (c == '\\') { slashes++; continue; }
            s.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0;
            s.Append(c);
        }
        return s.Append('\\', slashes * 2).Append('"').ToString();
    }

    static int Main(string[] args)
    {
        if (args.Length == 0) { Console.WriteLine("deskrun.exe <program.exe> [arguments...]"); return 98; }
        string program = Path.GetFullPath(args[0]);
        string desk = "QlUiTest";
        IntPtr hDesk = CreateDesktop(desk, IntPtr.Zero, IntPtr.Zero, 0, 0x10000000 /* GENERIC_ALL */, IntPtr.Zero);
        if (hDesk == IntPtr.Zero) { Console.WriteLine("deskrun: CreateDesktop failed: " + Marshal.GetLastWin32Error()); return 98; }
        var cmd = new StringBuilder(Quote(program));
        for (int i = 1; i < args.Length; i++) cmd.Append(' ').Append(Quote(args[i]));
        // the program writes into this console's output (handles inherited)
        IntPtr outH = GetStdHandle(-11), errH = GetStdHandle(-12);
        SetHandleInformation(outH, 1, 1); SetHandleInformation(errH, 1, 1);
        var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)), lpDesktop = desk, dwFlags = 0x100 /* STARTF_USESTDHANDLES */, hStdOutput = outH, hStdError = errH };
        Environment.SetEnvironmentVariable("QL_TEST_SILENT", "1");   // inherited by the program (CreateProcess without its own environment)
        PROCESS_INFORMATION pi;
        Console.Out.Flush();
        if (!CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, 0x08000000 /* CREATE_NO_WINDOW */, IntPtr.Zero, Path.GetDirectoryName(program), ref si, out pi))
        { Console.WriteLine("deskrun: CreateProcess failed: " + Marshal.GetLastWin32Error()); CloseDesktop(hDesk); return 98; }
        Console.WriteLine("deskrun: " + Path.GetFileName(program) + " on desktop '" + desk + "' (pid " + pi.dwProcessId + "); user's foreground: " + Describe(GetForegroundWindow()));
        int changes = 0, ours = 0;
        IntPtr last = GetForegroundWindow();
        var tree = Tree(pi.dwProcessId);
        DateTime treeAt = DateTime.Now;
        while (WaitForSingleObject(pi.hProcess, 25) == 0x102)
        {
            if ((DateTime.Now - treeAt).TotalSeconds > 2) { tree = Tree(pi.dwProcessId); treeAt = DateTime.Now; }
            IntPtr now = GetForegroundWindow();
            if (now == last) continue;
            last = now;
            changes++;
            uint pid; GetWindowThreadProcessId(now, out pid);
            bool test = tree.Contains((int)pid);
            Console.WriteLine("deskrun: " + DateTime.Now.ToString("HH:mm:ss.fff") + " user's foreground -> " + Describe(now) + (test ? "   <<< TEST PROCESS: stopping" : ""));
            if (test)
            {
                ours++;
                foreach (int p in tree) try { Process.GetProcessById(p).Kill(); } catch { }
                break;
            }
        }
        WaitForSingleObject(pi.hProcess, 10000);
        uint code; GetExitCodeProcess(pi.hProcess, out code);
        CloseHandle(pi.hProcess); CloseHandle(pi.hThread);
        CloseDesktop(hDesk);
        Console.WriteLine("deskrun: user's foreground changed " + changes + " time(s) (the user's own clicks count too), to a test process " + ours + " time(s)" + (ours == 0 ? " - focus-free" : " - STOPPED"));
        return ours > 0 ? 99 : (int)code;
    }
}
