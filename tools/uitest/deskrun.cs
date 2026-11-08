// deskrun: runs a UI test program on a Windows desktop of its own, so none of its windows (nor those of the
// processes it starts) can take the user's keyboard focus or come to the front, on any monitor. Meanwhile it checks
// the user's foreground window every 25 ms, logs every change with its process, and stops the test at once when the
// foreground goes to a process of the test. Output of the test program is passed through.
//
//   deskrun.exe [--desktop <name>] [--lock <name,name>] [--machine arm64|amd64|x86] <program.exe> [arguments...]
//   exit code: the program's; 99 = the focus went to a test process (stopped); 98 = could not start;
//   97 = gave up waiting for the desktop or a lock (another run held it, see Main)
//
// Desktop: --desktop, else QL_DESKRUN_DESKTOP, else one per QL_TEST_OUT folder ("QlUiTest-<hash>"), else "QlUiTest".
// Sessions with their own QL_TEST_OUT run their tests in parallel, each on its own desktop; runs on the same desktop
// take turns. Locks (--lock, else QL_DESKRUN_LOCK): what desktops still share, so the tests that use it take turns:
//   capture   - the "QL-800 Capture" printer and its port file (tests that print)
//   clipboard - the clipboard (one per window station, not per desktop)
//   instances - the programs' single-instance events and windows (status window, F6, --monitor)
//   settings  - HKCU\Software\QLUnidrv (tests that let a program save language, appearance or tabs)
//
// --machine: the architecture the program runs as (an AnyCPU or ARM64X exe, Windows 11). Without it the program gets
// Windows' choice for a child of deskrun, which runs as x64 on ARM64 (AnyCPU without a manifest), so .NET test programs
// run as x64 there too; --machine arm64 starts them natively. Only the program itself: what it starts gets the default.
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
    struct STARTUPINFOEX { public STARTUPINFO StartupInfo; public IntPtr lpAttributeList; }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDesktop(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);
    [DllImport("user32.dll")] static extern bool CloseDesktop(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CreateProcess(string app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, int flags, IntPtr env, string dir, ref STARTUPINFOEX si, out PROCESS_INFORMATION pi);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool UpdateProcThreadAttribute(IntPtr list, int flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr prev, IntPtr retSize);
    [DllImport("kernel32.dll")] static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessInformation(IntPtr process, int infoClass, out PROCESS_MACHINE_INFORMATION info, int size);
    [StructLayout(LayoutKind.Sequential)] struct PROCESS_MACHINE_INFORMATION { public ushort ProcessMachine, Res0; public uint MachineAttributes; }
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

    // One run at a time per desktop and per lock: a run waits for each named mutex (in the user's logon session, taken
    // in name order, so two runs never wait for each other) and says who holds it (the holder writes its pid and
    // command line into %TEMP%\ql-deskrun-<mutex>.txt). QL_DESKRUN_WAIT_MIN: give up after that many minutes in all
    // (default 60; exit code 97).
    static readonly string[] KnownLocks = { "capture", "clipboard", "instances", "settings" };

    static string OwnerFile(string what) { return Path.Combine(Path.GetTempPath(), "ql-deskrun-" + what + ".txt"); }

    static string Owner(string what)
    {
        try { return File.ReadAllText(OwnerFile(what)).Trim(); } catch { return "unknown"; }
    }

    // The default desktop: one per QL_TEST_OUT folder (a stable hash of its full path), else the shared "QlUiTest".
    static string DefaultDesktop()
    {
        string named = Environment.GetEnvironmentVariable("QL_DESKRUN_DESKTOP");
        if (!string.IsNullOrEmpty(named)) return named;
        string outDir = Environment.GetEnvironmentVariable("QL_TEST_OUT");
        if (string.IsNullOrEmpty(outDir)) return "QlUiTest";
        uint h = 2166136261;   // FNV-1a
        foreach (char c in Path.GetFullPath(outDir).TrimEnd('\\').ToLowerInvariant()) { h ^= c; h *= 16777619; }
        return "QlUiTest-" + h.ToString("x8");
    }

    static int Main(string[] args)
    {
        string desk = DefaultDesktop();
        string locks = Environment.GetEnvironmentVariable("QL_DESKRUN_LOCK") ?? "";
        string machine = null;
        int first = 0;
        for (; first + 1 < args.Length; first += 2)
        {
            if (args[first] == "--desktop") desk = args[first + 1];
            else if (args[first] == "--lock") locks = args[first + 1];
            else if (args[first] == "--machine") machine = args[first + 1].ToLowerInvariant();
            else break;
        }
        if (first >= args.Length || args[first].StartsWith("--")) { Console.WriteLine("deskrun.exe [--desktop <name>] [--lock " + string.Join(",", KnownLocks) + "] [--machine arm64|amd64|x86] <program.exe> [arguments...]"); return 98; }
        if (machine != null && MachineId(machine) == 0) { Console.WriteLine("deskrun: unknown machine '" + machine + "' (arm64, amd64, x86)"); return 98; }
        if (desk.Length == 0 || desk.IndexOfAny(new[] { '\\', '/', ' ' }) >= 0) { Console.WriteLine("deskrun: bad desktop name '" + desk + "'"); return 98; }
        var names = new SortedSet<string>(StringComparer.Ordinal) { "desktop." + desk };
        foreach (string l in locks.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Array.IndexOf(KnownLocks, l) < 0) { Console.WriteLine("deskrun: unknown lock '" + l + "' (" + string.Join(", ", KnownLocks) + ")"); return 98; }
            names.Add("lock." + l);
        }
        var rest = new string[args.Length - first];
        Array.Copy(args, first, rest, 0, rest.Length);

        int maxMinutes;
        if (!int.TryParse(Environment.GetEnvironmentVariable("QL_DESKRUN_WAIT_MIN"), out maxMinutes) || maxMinutes <= 0) maxMinutes = 60;
        DateTime start = DateTime.Now;
        var held = new List<KeyValuePair<string, System.Threading.Mutex>>();
        try
        {
            foreach (string name in names)
            {
                var m = new System.Threading.Mutex(false, @"Local\QlUiTest." + name);
                string what = name.StartsWith("desktop.") ? "the desktop '" + desk + "'" : "the lock '" + name.Substring(5) + "'";
                bool got = false;
                DateTime note = DateTime.MinValue;
                while (!got)
                {
                    try { got = m.WaitOne(2000); }
                    catch (System.Threading.AbandonedMutexException) { got = true; Console.WriteLine("deskrun: the previous run ended without freeing " + what + "; taking it"); }
                    if (got) break;
                    if (DateTime.Now >= note)
                    {
                        Console.WriteLine("deskrun: " + DateTime.Now.ToString("HH:mm:ss") + " waiting for " + what + " (held by " + Owner(name) + ")");
                        Console.Out.Flush();
                        note = DateTime.Now.AddSeconds(60);
                    }
                    if ((DateTime.Now - start).TotalMinutes > maxMinutes)
                    {
                        Console.WriteLine("deskrun: gave up after " + maxMinutes + " min waiting for " + what + " (held by " + Owner(name) + ")");
                        m.Dispose();
                        return 97;
                    }
                }
                held.Add(new KeyValuePair<string, System.Threading.Mutex>(name, m));
                try { File.WriteAllText(OwnerFile(name), "pid " + Process.GetCurrentProcess().Id + ", since " + DateTime.Now.ToString("HH:mm:ss") + ": " + string.Join(" ", rest)); } catch { }
            }
            return Run(rest, desk, machine);
        }
        finally
        {
            for (int i = held.Count - 1; i >= 0; i--)
            {
                try { File.Delete(OwnerFile(held[i].Key)); } catch { }
                held[i].Value.ReleaseMutex();
                held[i].Value.Dispose();
            }
        }
    }

    static ushort MachineId(string name)
    {
        switch (name) { case "arm64": return 0xAA64; case "amd64": case "x64": return 0x8664; case "x86": return 0x014C; default: return 0; }
    }

    static string MachineName(ushort id)
    {
        switch (id) { case 0xAA64: return "arm64"; case 0x8664: return "amd64"; case 0x014C: return "x86"; default: return "0x" + id.ToString("x4"); }
    }

    // CreateProcess, with --machine through PROC_THREAD_ATTRIBUTE_MACHINE_TYPE (Windows 11).
    static bool Start(string program, StringBuilder cmd, STARTUPINFO si, string machine, out PROCESS_INFORMATION pi, out int error)
    {
        error = 0;
        const int noWindow = 0x08000000 /* CREATE_NO_WINDOW */, extended = 0x00080000 /* EXTENDED_STARTUPINFO_PRESENT */;
        if (machine == null)
        {
            bool ok = CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, noWindow, IntPtr.Zero, Path.GetDirectoryName(program), ref si, out pi);
            error = Marshal.GetLastWin32Error();
            return ok;
        }
        IntPtr size = IntPtr.Zero;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        IntPtr list = Marshal.AllocHGlobal(size), value = Marshal.AllocHGlobal(2);
        bool listReady = false, started = false;
        try
        {
            pi = new PROCESS_INFORMATION();
            if (!(listReady = InitializeProcThreadAttributeList(list, 1, 0, ref size))) return false;
            Marshal.WriteInt16(value, unchecked((short)MachineId(machine)));
            if (!UpdateProcThreadAttribute(list, 0, (IntPtr)0x00020019 /* PROC_THREAD_ATTRIBUTE_MACHINE_TYPE */, value, (IntPtr)2, IntPtr.Zero, IntPtr.Zero)) return false;
            si.cb = Marshal.SizeOf(typeof(STARTUPINFOEX));
            var six = new STARTUPINFOEX { StartupInfo = si, lpAttributeList = list };
            started = CreateProcess(null, cmd, IntPtr.Zero, IntPtr.Zero, true, noWindow | extended, IntPtr.Zero, Path.GetDirectoryName(program), ref six, out pi);
            return started;
        }
        finally
        {
            if (!started) error = Marshal.GetLastWin32Error();
            if (listReady) DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list); Marshal.FreeHGlobal(value);
        }
    }

    static string Arch(IntPtr process)
    {
        PROCESS_MACHINE_INFORMATION m;
        return GetProcessInformation(process, 9 /* ProcessMachineTypeInfo */, out m, Marshal.SizeOf(typeof(PROCESS_MACHINE_INFORMATION))) ? MachineName(m.ProcessMachine) : "?";
    }

    static int Run(string[] args, string desk, string machine)
    {
        string program = Path.GetFullPath(args[0]);
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
        int startError;
        if (!Start(program, cmd, si, machine, out pi, out startError))
        { Console.WriteLine("deskrun: CreateProcess failed: " + startError + (machine != null ? " (--machine " + machine + ")" : "")); CloseDesktop(hDesk); return 98; }
        Console.WriteLine("deskrun: " + Path.GetFileName(program) + " on desktop '" + desk + "' (pid " + pi.dwProcessId + ", " + Arch(pi.hProcess) + "); user's foreground: " + Describe(GetForegroundWindow()));
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
