// One status window per user session (PrinterStatusProgram.ShowStatus / Listen): two signals from other processes
// bring one window to the front, F6 without a running status program starts QlPrinterStatus.exe, and a closed
// listener lets the next process take over; F6 here puts the other process's status window above a maximised window
// (opened and already open behind). Uses an event name of its own, so the user's running monitor is never
// signalled; the window lists no printers (nothing is read, printed or changed). Runs only on deskrun's own desktop
// (tools\uitest): it never takes the user's mouse or focus. Built with the program sources by status-window.ps1:
//   statuswindow.exe                    the test (exit code = failures)
//   statuswindow.exe signal <event>     helper: sets the event like a second program would (exit 0 = it existed)
//   statuswindow.exe host <event>       helper: shows the status window on the event, as the monitor does (20 s)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class StatusWindowTest
    {
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, System.Text.StringBuilder b, int n, out int need);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr h, out int pid);

        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK    " : "FAIL  ") + what); if (!ok) failures++; }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "signal")
            {
                PrinterStatusProgram.ShowEventName = args[1];
                return PrinterStatusProgram.ShowRunning() ? 0 : 1;
            }
            if (args.Length == 2 && args[0] == "host") return Host(args[1]);
            var name = new System.Text.StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop (run status-window.ps1)"); return 2; }

            PrinterStatusProgram.ShowEventName = @"Local\QLUnidrv.StatusWindow.Show.Test" + Process.GetCurrentProcess().Id;
            var launched = new List<string>();
            PrinterStatusProgram.Launch = exe => launched.Add(exe);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Nothing shows the status window: F6 starts QL Printer Status.
            PrinterStatusProgram.ShowStatus(null);
            Check(launched.Count == 1 && launched[0].EndsWith(@"\" + PrinterStatusProgram.ExeName, StringComparison.OrdinalIgnoreCase),
                  "no status program running: F6 starts " + PrinterStatusProgram.ExeName + " (" + string.Join(", ", launched) + ")");

            // This process shows it (as the monitor does): two signals from other processes, one window.
            var host = new Form { ShowInTaskbar = false, Opacity = 0 };
            StatusForm window = null;
            int shown = 0;
            host.Shown += delegate
            {
                var listener = PrinterStatusProgram.Listen(delegate
                {
                    shown++;
                    if (window == null || window.IsDisposed) { window = new StatusForm(new string[0]); window.Show(); }
                    else PrinterStatusProgram.Front(window);
                });
                Check(Signal() == 0, "first program's F6 finds the status window's process");
                Pump(1500);
                Check(Signal() == 0, "second program's F6 finds it too");
                Pump(1500);
                PrinterStatusProgram.ShowStatus(host);   // F6 in this process's own windows
                Pump(1500);
                int windows = Application.OpenForms.OfType<StatusForm>().Count();
                Check(shown == 3 && windows == 1, "three requests, one status window (requests " + shown + ", windows " + windows + ")");
                Check(launched.Count == 1, "nothing started while a status window's process runs");

                // That process ends: the next one takes over.
                listener.Dispose();
                Check(Signal() == 1, "after it ended, no process answers: the next start opens the window itself");
                PrinterStatusProgram.ShowStatus(host);
                Check(launched.Count == 2, "F6 starts " + PrinterStatusProgram.ExeName + " again");
                if (window != null) window.Close();
                Foreground();
                host.Close();
            };
            Application.Run(host);
            Console.WriteLine(failures == 0 ? "status window: all OK" : "status window: " + failures + " failure(s)");
            return failures;
        }

        // 0.18.21: the status window opened behind a maximised QL Label Maker. A maximised window of this process (the
        // program the user works in) and the status window in another process (the monitor): F6 here must put that
        // window above it, when it is opened and when it is already open behind. deskrun's desktop has no foreground
        // window (it isn't the input desktop), so this checks the window order and that F6 here finds the other
        // process's window by its mark (ShowRunning puts it in front itself); the foreground lock itself shows only on
        // the user's desktop.
        static void Foreground()
        {
            string ev = PrinterStatusProgram.ShowEventName + ".Fg";
            var maker = new Form { Text = "maximised program", WindowState = FormWindowState.Maximized };
            maker.Show();
            Pump(800);
            var hostProcess = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "host " + ev) { UseShellExecute = false });
            Pump(2500);   // its listener runs
            string saved = PrinterStatusProgram.ShowEventName;
            PrinterStatusProgram.ShowEventName = ev;
            try
            {
                Pump(500);
                Check(PrinterStatusProgram.FindStatusWindow() == IntPtr.Zero, "no status window yet");
                PrinterStatusProgram.ShowRunning();
                Pump(1500);
                var status = PrinterStatusProgram.FindStatusWindow();
                Check(status != IntPtr.Zero && Owner(status) == hostProcess.Id, "F6 finds the status window the other process opened (its mark)");
                Check(Above(status, maker.Handle), "it opens above the maximised window");
                Back(status);   // behind the maximised window again (raising that one over the active window doesn't work here)
                Pump(800);
                Check(Above(maker.Handle, status), "the status window is behind the maximised window again");
                PrinterStatusProgram.ShowRunning();
                Pump(1500);
                Check(PrinterStatusProgram.FindStatusWindow() == status && Above(status, maker.Handle), "F6 brings the open status window above it, no second one");
            }
            finally
            {
                PrinterStatusProgram.ShowEventName = saved;
                if (!hostProcess.HasExited) hostProcess.Kill();
                maker.Close();
            }
        }

        static int Owner(IntPtr h)
        {
            int pid;
            GetWindowThreadProcessId(h, out pid);
            return pid;
        }

        // a before b in the window order (top first)
        static bool Above(IntPtr a, IntPtr b)
        {
            int i = 0, ia = -1, ib = -1;
            EnumWindows((h, l) => { if (h == a) ia = i; if (h == b) ib = i; i++; return true; }, IntPtr.Zero);
            return ia >= 0 && ib >= 0 && ia < ib;
        }
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);

        // The window at the bottom of the order, not activated
        static void Back(IntPtr h) { SetWindowPos(h, (IntPtr)1 /* HWND_BOTTOM */, 0, 0, 0, 0, 0x0013 /* SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE */); }
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // The monitor's part: the status window on each signal, created or brought back, never activated from here
        // directly (another process has the foreground, as on the real desktop).
        static int Host(string ev)
        {
            PrinterStatusProgram.ShowEventName = ev;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var hidden = new Form { ShowInTaskbar = false, Opacity = 0 };
            StatusForm window = null;
            hidden.Shown += delegate
            {
                var listener = PrinterStatusProgram.Listen(delegate
                {
                    if (window == null || window.IsDisposed) { window = new StatusForm(new string[0]); window.Show(); }
                    else PrinterStatusProgram.Front(window);
                });
                var timer = new System.Windows.Forms.Timer { Interval = 20000 };
                timer.Tick += delegate { listener.Dispose(); hidden.Close(); };
                timer.Start();
            };
            Application.Run(hidden);
            return 0;
        }

        // Another program's F6: a process of its own.
        static int Signal()
        {
            var p = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "signal " + PrinterStatusProgram.ShowEventName) { UseShellExecute = false });
            p.WaitForExit();
            return p.ExitCode;
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(20); }
        }
    }
}
