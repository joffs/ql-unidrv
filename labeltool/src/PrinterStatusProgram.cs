// QlPrinterStatus.exe - "QL Printer Status" as its own program:
//   QlPrinterStatus.exe                         the printer status window (all QL printers; Start menu "QL Printer Status");
//                                               one per user session (ShowStatus): a running one comes to the front
//   QlPrinterStatus.exe --monitor               notification-area icon that explains printer errors (TrayMonitor.cs)
//   ... --restore X,Y                           (window, monitor) the status window opens with its bottom right corner
//                                               there: the restart after a language change (Lang.WatchSetting)
//   QlPrinterStatus.exe --service               the service "QL printer status" (started by Windows; StatusService.cs)
//   QlPrinterStatus.exe --online <printer>... [--result <file>]  elevated helper of "Use printer online" (Program.Command)
// Built from the same sources as QlLabelFormats.exe (labeltool\build.cmd, /main:QlLabels.PrinterStatusProgram).
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels
{
    static class PrinterStatusProgram
    {
        public const string ExeName = "QlPrinterStatus.exe";
        public const string LabelToolExe = "QlLabelFormats.exe";

        [STAThread]
        static int Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0] : null;
            if (mode == "--service")
            {
                StatusService.RunService();
                return 0;
            }
            if (mode == "--online") return Program.Command(args);
            Lang.WindowApp();   // the window and the monitor: the user's language
            if (mode == "--monitor") TrayMonitor.Run();
            else RunWindow();
            return 0;
        }

        // The status window, unless another process shows it: then that one comes to the front. With notifications on,
        // the monitor shows it (started first when an update stopped it); else this process.
        public static void RunWindow()
        {
            if (ShowRunning()) return;
            TrayMonitor.EnsureRunning();  // as QL Label Formats does: after an update the monitor was stopped
            if (TrayMonitor.AutostartEnabled && RestoreAnchor == null)
                for (int i = 0; i < 50; i++) { if (ShowRunning()) return; Thread.Sleep(100); }   // the monitor starting
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = new StatusForm() { StartAnchor = RestoreAnchor };
            string restart = null;
            Lang.WatchSetting(delegate
            {
                try { form.BeginInvoke((Action)delegate { restart = RestoreArgument(form); form.Close(); }); }
                catch (Exception) { }   // closed meanwhile
            });
            form.Shown += delegate
            {
                HangLog.Start(form);   // its posts need the window's handle
                Front(form);           // started by the foreground program (F6, the Start menu): it may take the foreground
            };
            using (Listen(delegate { Front(form); }))
                Application.Run(form);
            if (restart != null) Start(restart);
        }

        // ---------- one status window per user session ----------
        // The process that shows the status window (the monitor, or this program on its own) waits on this event; F6 and
        // "Printer status" in the other programs, the Start menu and a second start set it, and it brings its window to
        // the front. Tests use a name of their own.
        internal static string ShowEventName = @"Local\QLUnidrv.StatusWindow.Show";

        // Starts the status program when no process shows the status window; UI tests replace it.
        internal static Action<string> Launch = exe => Process.Start(exe);

        // F6 / "Printer status" in QL Label Formats and QL Label Maker: the one status window, in front.
        public static void ShowStatus(IWin32Window owner)
        {
            if (ShowRunning()) return;
            try { Launch(MonitorExe()); }
            catch (Exception ex)
            {
                QlMessage.Ask(owner, "QL Printer Status", Lang.F("{0} couldn't be started.", "QL Printer Status"), null, MessageBoxButtons.OK,
                              MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message);
            }
        }

        // Asks the process that shows the status window to show it, then puts it in front from here; false when there
        // is none.
        internal static bool ShowRunning()
        {
            EventWaitHandle show;
            if (!EventWaitHandle.TryOpenExisting(ShowEventName, out show)) return false;
            using (show)
            {
                AllowSetForegroundWindow(-1 /* ASFW_ANY */);   // Front over there may work too
                show.Set();
            }
            if (listeners == 0) BringForward();   // in this process Front does it (and this thread must run it)
            return true;
        }

        // The caller is the program the user just used (F6, a click, the Start menu): only it may take the foreground
        // from the window in front. Windows refuses the other process's Activate then (0.18.21: the status window
        // stayed behind a maximised QL Label Maker, only its taskbar button showed). Waits up to 3 s for the window
        // (the monitor may still be creating it).
        static void BringForward()
        {
            for (int i = 0; i < 60; i++)
            {
                IntPtr h = FindStatusWindow();
                if (h != IntPtr.Zero)
                {
                    if (IsIconic(h)) ShowWindow(h, 9 /* SW_RESTORE */);
                    BringWindowToTop(h);   // also when it is the active window but covered
                    SetForegroundWindow(h);
                    return;
                }
                Thread.Sleep(50);
            }
        }

        // The status window carries this mark (StatusForm.OnHandleCreated), whichever process shows it.
        const string WindowMark = "QLUnidrv.StatusWindow";

        public static void Mark(Form form) { SetProp(form.Handle, WindowMark, (IntPtr)1); }

        // The visible status window on this desktop, or zero.
        internal static IntPtr FindStatusWindow()
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                if (!IsWindowVisible(h) || GetProp(h, WindowMark) == IntPtr.Zero) return true;
                found = h;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static int listeners;   // Listen in this process

        // Waits for ShowRunning's signal and runs show on the calling (UI) thread; dispose ends it (before a restart:
        // the next process listens then).
        internal static IDisposable Listen(Action show)
        {
            var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var wait = ThreadPool.RegisterWaitForSingleObject(ev, delegate { ui.Post(delegate { show(); }, null); }, null, -1, false);
            Interlocked.Increment(ref listeners);
            return new Listener(ev, wait);
        }

        sealed class Listener : IDisposable
        {
            readonly EventWaitHandle ev; readonly RegisteredWaitHandle wait;
            public Listener(EventWaitHandle ev, RegisteredWaitHandle wait) { this.ev = ev; this.wait = wait; }
            public void Dispose() { wait.Unregister(null); ev.Dispose(); Interlocked.Decrement(ref listeners); }
        }

        // A window of this process in front (restored when minimised).
        public static void Front(Form form)
        {
            if (form.IsDisposed) return;
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SetProp(IntPtr h, string name, IntPtr value);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetProp(IntPtr h, string name);
        delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);

        // The status window's place for "--restore", and the place given on this start.
        public static string RestoreArgument(StatusForm form) { return "--restore " + form.Corner.X + "," + form.Corner.Y; }

        public static Point? RestoreAnchor
        {
            get
            {
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "--restore");
                if (i < 0 || i + 1 >= args.Length) return null;
                var xy = args[i + 1].Split(',');
                int x, y;
                if (xy.Length != 2 || !int.TryParse(xy[0], out x) || !int.TryParse(xy[1], out y)) return null;
                return new Point(x, y);
            }
        }

        // This exe again (the language changed).
        public static void Start(string arguments)
        {
            try { Process.Start(Application.ExecutablePath, arguments); } catch (Exception) { }
        }

        // QlPrinterStatus.exe next to the running exe, or null (a build or an install without it).
        public static string Path { get { return Sibling(ExeName); } }

        // The exe that runs the monitor: QlPrinterStatus.exe next to exe (default: the running one).
        public static string MonitorExe(string exe = null)
        {
            return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe ?? Application.ExecutablePath), ExeName);
        }

        // QL Label Formats (QlLabelFormats.exe) next to the running exe, else the running exe (it is QL Label Formats).
        public static string LabelTool { get { return Sibling(LabelToolExe) ?? Application.ExecutablePath; } }

        static string Sibling(string name)
        {
            string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), name);
            return File.Exists(p) ? p : null;
        }
    }
}
