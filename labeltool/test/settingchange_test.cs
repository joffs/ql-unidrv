// Hang test for QL Printer Status (0.18.18 hung after High Contrast on/off): the status window with every QL printer
// of this PC open, its checks running every 4 s, while a second thread sends what Windows broadcasts on a colour or
// High Contrast switch (WM_SETTINGCHANGE, WM_SYSCOLORCHANGE, WM_THEMECHANGED) to every top-level window of this
// process (as Windows does: also the hidden windows of worker threads), and the UI thread switches the scheme (High
// Contrast, light, dark, like Windows) in between. A watchdog checks that the UI thread still runs posted work; if not
// for 10 s it prints the UI thread's managed stack and fails. Only this process gets the messages; the printers are
// only asked for their status (as the monitor does). Runs only on deskrun's own desktop (tools\uitest):
// Also fails when a SystemEvents subscriber is bound to another thread than the UI thread (the cause of such hangs:
// SystemEvents waits for that thread, which doesn't pump). --real-hc: Windows' own High Contrast on and off every 5 s
// instead of the simulated messages; only in Windows Sandbox (user WDAGUtilityAccount), never on a real PC.
// --real-hc-approved: the same on a real PC, only when its user agreed; the state from
// before the test comes back at the end, on a hang and on an error.
// --watch: nothing simulated, on the normal desktop: the user switches High Contrast (or light/dark) himself meanwhile;
// for catching a hang that needs Windows' real switch.
//   settingchange_test.exe <QlLabelFormats.exe> [seconds] [--real-hc | --real-hc-approved | --watch]
//   exit 0 = OK, 3 = hang, 4 = bad subscriber, 1 = error
#pragma warning disable 618   // Thread.Suspend / StackTrace(Thread): the hung UI thread's stack
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class SettingChangeTest
{
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, System.Text.StringBuilder b, int n, out int need);
    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process, int flags);
    static string Handles() { var p = Process.GetCurrentProcess().Handle; return "GDI " + GetGuiResources(p, 0) + ", USER " + GetGuiResources(p, 1); }
    [StructLayout(LayoutKind.Sequential)] struct HIGHCONTRAST { public int cbSize, dwFlags; public IntPtr lpszDefaultScheme; }
    [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref HIGHCONTRAST value, int winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, string lParam, int flags, int timeout, out IntPtr result);

    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static Assembly asm;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new System.Text.StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        bool sandbox = string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase);
        bool watch = a.Contains("--watch"), approved = a.Contains("--real-hc-approved");
        if (name.ToString() == "Default" && !sandbox && !watch && !approved) { Console.WriteLine("refused: normal desktop (deskrun, Windows Sandbox or --watch)"); return 1; }
        asm = Assembly.LoadFrom(a[0]);
        int seconds = a.Length > 1 ? int.Parse(a[1]) : 60;
        bool realHc = a.Contains("--real-hc") || approved;
        if (realHc && !sandbox && !approved)
        { Console.WriteLine("refused: --real-hc only in Windows Sandbox (on a real PC: --real-hc-approved, with its user's OK)"); return 1; }
        if (realHc)
        {
            originalHc = HighContrastOn();
            Console.WriteLine("High Contrast before the test: " + (originalHc.Value ? "on" : "off") + " (comes back at the end)");
            AppDomain.CurrentDomain.ProcessExit += delegate { Restore(); };
            AppDomain.CurrentDomain.UnhandledException += delegate { Restore(); };
        }
        asm.GetType("QlLabels.Lang").GetMethod("Force", All).Invoke(null, new object[] { "de" });
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // as under Application.Run: awaits in the windows continue on this thread (without it on the thread pool,
        // where a window closed meanwhile crashed QL Label Maker's roll check)
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        // The tray monitor as in QlPrinterStatus.exe --monitor (its icon has no notification area on this desktop; its
        // "show the status window" event under a name of its own, so the user's F6 still reaches the real monitor)
        var showEvent = asm.GetType("QlLabels.PrinterStatusProgram").GetField("ShowEventName", All);   // missing before 0.18.19
        if (showEvent != null) showEvent.SetValue(null, @"Local\QLUnidrv.StatusWindow.Show.Test" + Process.GetCurrentProcess().Id);
        var tray = (ApplicationContext)Activator.CreateInstance(asm.GetType("QlLabels.TrayMonitor"), true);
        var printers = (IEnumerable<string>)asm.GetType("QlLabels.MainForm").GetMethod("OurPrinters", All).Invoke(null, null);
        var list = printers.ToList();
        if (list.Count == 0) list.Add("Brother QL-800 (Unidrv)");   // no printer here (Sandbox): the checks still run and fail
        Console.WriteLine("printers: " + string.Join(" | ", list));
        var status = (Form)Activator.CreateInstance(asm.GetType("QlLabels.StatusForm"), new object[] { list });
        status.Show();
        Pump(3000);

        Console.WriteLine("handles before: " + Handles());
        var ui = Thread.CurrentThread;
        bool done = false, hung = false;
        int pid = Process.GetCurrentProcess().Id;
        int sent = 0;
        var threadsWithWindows = new HashSet<int>();
        int uiThread = GetCurrentThreadId();
        // Windows' broadcast on a switch, from another thread (the sender waits, as Windows' own SendMessageTimeout)
        var broadcaster = new Thread(() =>
        {
            var rnd = new Random(1);
            while (!realHc && !watch && !Volatile.Read(ref done))
            {
                var windows = new List<IntPtr>();
                EnumWindows((h, l) =>
                {
                    int p, t = GetWindowThreadProcessId(h, out p);
                    if (p == pid) { windows.Add(h); lock (threadsWithWindows) threadsWithWindows.Add(t); }
                    return true;
                }, IntPtr.Zero);
                foreach (var h in windows)
                {
                    IntPtr r;
                    SendMessageTimeout(h, 0x1A /* WM_SETTINGCHANGE */, (IntPtr)0x43 /* SPI_SETHIGHCONTRAST */, null, 2 /* SMTO_ABORTIFHUNG */, 3000, out r);
                    SendMessageTimeout(h, 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 3000, out r);
                    SendMessageTimeout(h, 0x15 /* WM_SYSCOLORCHANGE */, IntPtr.Zero, null, 2, 3000, out r);
                    SendMessageTimeout(h, 0x31A /* WM_THEMECHANGED */, IntPtr.Zero, null, 2, 3000, out r);
                    Interlocked.Increment(ref sent);
                }
                Thread.Sleep(150 + rnd.Next(300));
            }
        }) { IsBackground = true };
        // The watchdog: posted work must run on the UI thread within 10 s
        var watchdog = new Thread(() =>
        {
            while (!Volatile.Read(ref done))
            {
                var ran = new ManualResetEventSlim();
                try { status.BeginInvoke((Action)delegate { ran.Set(); }); } catch (Exception) { return; }
                if (!ran.Wait(10000) && !Volatile.Read(ref done))
                {
                    hung = true;
                    Console.WriteLine("HANG: the UI thread runs no posted work for 10 s. Its managed stack:");
                    try { ui.Suspend(); Console.WriteLine(new StackTrace(ui, false)); } catch (Exception e) { Console.WriteLine("(stack: " + e.Message + ")"); }
                    finally { try { ui.Resume(); } catch (Exception) { } }
                    Subscribers(ui.ManagedThreadId);
                    Restore();
                    Console.Out.Flush();
                    Environment.Exit(3);
                }
                Thread.Sleep(500);
            }
        }) { IsBackground = true };
        broadcaster.Start();
        watchdog.Start();

        var theme = asm.GetType("QlLabels.Theme");
        var schemeType = asm.GetType("QlLabels.Scheme");
        var schemes = new object[] { Enum.Parse(schemeType, "HighContrast"), Enum.Parse(schemeType, "Light"), Enum.Parse(schemeType, "Dark"), null };
        var end = DateTime.Now.AddSeconds(seconds);
        int switches = 0;
        while (DateTime.Now < end)
        {
            if (watch) { Pump(1000); continue; }
            if (realHc)
            {
                SetHighContrast(switches % 2 == 0);
                switches++;
                Pump(5000);
                continue;
            }
            theme.GetMethod("Force", All).Invoke(null, new[] { schemes[switches % schemes.Length] });
            theme.GetMethod("Refresh", All).Invoke(null, null);
            switches++;
            Pump(700);
        }
        if (realHc) { Restore(); Pump(3000); }
        int bad = Subscribers(ui.ManagedThreadId);
        Console.WriteLine("handles after " + switches + " switches: " + Handles());
        Volatile.Write(ref done, true);
        status.Close();
        tray.ExitThread();
        Pump(300);
        lock (threadsWithWindows) Console.WriteLine("threads with windows: " + threadsWithWindows.Count + " (UI thread " + uiThread + ": " + string.Join(", ", threadsWithWindows) + ")");
        Console.WriteLine(hung ? "FAIL" : bad > 0 ? "FAIL: " + bad + " SystemEvents subscriber(s) bound to another thread"
                                        : "OK: " + switches + " scheme switches, " + sent + " window broadcasts, no hang");
        return hung ? 3 : bad > 0 ? 4 : 0;
    }

    static bool? originalHc;
    static int restored;

    // Back to the state from before the test (once)
    static void Restore()
    {
        if (originalHc == null || Interlocked.Exchange(ref restored, 1) == 1) return;
        try { SetHighContrast(originalHc.Value); } catch (Exception) { }
    }

    static bool HighContrastOn()
    {
        var hc = new HIGHCONTRAST { cbSize = Marshal.SizeOf(typeof(HIGHCONTRAST)) };
        return SystemParametersInfo(0x42 /* SPI_GETHIGHCONTRAST */, hc.cbSize, ref hc, 0) && (hc.dwFlags & 1) != 0;
    }

    static void SetHighContrast(bool on)
    {
        var hc = new HIGHCONTRAST { cbSize = Marshal.SizeOf(typeof(HIGHCONTRAST)) };
        SystemParametersInfo(0x42 /* SPI_GETHIGHCONTRAST */, hc.cbSize, ref hc, 0);
        hc.dwFlags = on ? hc.dwFlags | 1 : hc.dwFlags & ~1;
        hc.lpszDefaultScheme = IntPtr.Zero;
        Console.WriteLine("High Contrast " + (on ? "on" : "off") + ": " + SystemParametersInfo(0x43 /* SPI_SETHIGHCONTRAST */, hc.cbSize, ref hc, 3 /* update + send change */));
    }

    // SystemEvents' subscribers and the thread each one is called on (its SynchronizationContext); counts those bound to
    // a thread other than the UI thread.
    static int Subscribers(int uiThread)
    {
        int bad = 0;
        try
        {
            var se = typeof(Microsoft.Win32.SystemEvents);
            var handlers = (IDictionary)se.GetField("_handlers", All).GetValue(null);
            if (handlers == null) { Console.WriteLine("SystemEvents: no subscribers"); return 0; }
            foreach (DictionaryEntry e in handlers)
                foreach (var info in (IEnumerable)e.Value)
                {
                    var ctx = info.GetType().GetField("_syncContext", All).GetValue(info);
                    var d = (Delegate)info.GetType().GetField("_delegate", All).GetValue(info);
                    int thread = -1;
                    if (ctx is WindowsFormsSynchronizationContext)
                    {
                        var r = (WeakReference)typeof(WindowsFormsSynchronizationContext).GetField("destinationThreadRef", All).GetValue(ctx);
                        var t = r != null ? r.Target as Thread : null;
                        thread = t != null ? t.ManagedThreadId : 0;
                    }
                    bool wrong = thread != -1 && thread != uiThread;
                    if (wrong) bad++;
                    Console.WriteLine((wrong ? "  BAD " : "      ") + e.Key + ": " + d.Method.DeclaringType + "." + d.Method.Name
                                      + (ctx == null ? " (direct)" : " -> " + ctx.GetType().Name + (thread >= 0 ? " thread " + thread : "")));
                }
        }
        catch (Exception ex) { Console.WriteLine("SystemEvents: can't read the subscribers (" + ex.Message + ")"); }
        return bad;
    }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
    }
}
