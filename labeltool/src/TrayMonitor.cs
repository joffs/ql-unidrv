// QlPrinterStatus.exe --monitor: a notification-area icon that explains printer errors while programs print.
//
// Windows only sees whether the job's data was delivered: a wrong-media job leaves the queue as if
// printed, and the next job just hangs. The printer itself reports the reason in a 32-byte message that
// nobody reads, and an idle printer reports an open cover or a missing roll only when asked. Every few
// seconds the monitor asks each QL printer on USB (only while its queue is empty, so a job is never
// disturbed), watches the queues, and shows a notification with the reason and what to do, once per problem.
// Started at logon when the user enables it in QL Label Formats or QL Printer Status (HKCU Run, no administrator rights).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class TrayMonitor : ApplicationContext
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunValue = "QL printer monitor";
        const string MutexName = @"Local\QLUnidrv.PrinterMonitor";
        const string Title = "QL Printer Status";   // the icon's tooltip: the program's name, as in the Start menu

        readonly NotifyIcon icon = new NotifyIcon();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 3000 };
        readonly Dictionary<string, string> notifiedProblem = new Dictionary<string, string>();     // printer -> headline shown
        readonly Dictionary<string, KeyValuePair<string, DateTime>> problemSince = new Dictionary<string, KeyValuePair<string, DateTime>>();   // printer -> headline, first seen (UTC)
        static readonly TimeSpan NotifyAfter = TimeSpan.FromSeconds(10);
        StatusForm statusForm;
        IDisposable showListener;   // F6 and the Start menu in other processes: this monitor shows the one status window
        bool running, opening;
        volatile bool exitRequested;
        readonly Control hangMarshal = new Control { Visible = false };
        bool exiting;   // ExitThreadCore ran: the icon is gone, a poll still running must not touch it
        static TrayMonitor instance;
        static string restartArguments;   // the language changed: started again with these once the mutex is free

        // ---------- autostart ----------
        public static bool AutostartEnabled
        {
            get
            {
                try { using (var k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue(RunValue) != null; }
                catch (Exception) { return false; }
            }
        }

        static string RunCommand(string exe) { return "\"" + exe + "\" --monitor"; }

        // Enables or disables "start at logon" and starts or stops the running monitor.
        public static void SetAutostart(bool on)
        {
            string exe = PrinterStatusProgram.MonitorExe();
            if (instance != null)  // the status window of the running monitor itself
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(RunValue, RunCommand(exe));
                    else { k.DeleteValue(RunValue, false); }
                }
                instance.exitRequested = !on;  // exits when its status window is closed
                return;
            }
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, RunCommand(exe));
                else { k.DeleteValue(RunValue, false); }
            }
            if (on) Process.Start(exe, "--monitor");
            else
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, MutexName + ".Exit"))
                    ev.Set();
        }

        // The setup window (runs from the release folder): autostart and the running monitor for the installed exe
        // (QlPrinterStatus.exe when it is next to exe).
        public static void SetAutostart(bool on, string exe)
        {
            exe = PrinterStatusProgram.MonitorExe(exe);
            using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) k.SetValue(RunValue, RunCommand(exe));
                else { k.DeleteValue(RunValue, false); }
            }
            if (!on)
                using (var ev = new EventWaitHandle(false, EventResetMode.AutoReset, MutexName + ".Exit"))
                    ev.Set();
            else
            {
                Mutex running;
                if (Mutex.TryOpenExisting(MutexName, out running)) running.Dispose();
                else Process.Start(exe, "--monitor");
            }
        }

        // Called when QL Label Formats or the status window starts: restarts a monitor that an update (install.ps1)
        // stopped, and moves the autostart entry to the installed exe when another copy (a development build) set it.
        public static void EnsureRunning()
        {
            if (!AutostartEnabled) return;
            try
            {
                string exe = PrinterStatusProgram.MonitorExe(), want = RunCommand(exe);
                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                    if (k != null && Convert.ToString(k.GetValue(RunValue)) != want && exe.StartsWith(programFiles + "\\", StringComparison.OrdinalIgnoreCase))
                        k.SetValue(RunValue, want);
                Mutex running;
                if (Mutex.TryOpenExisting(MutexName, out running)) { running.Dispose(); return; }
                Process.Start(exe, "--monitor");
            }
            catch (Exception) { }
        }

        public static void Run()
        {
            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created) return;  // already running
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayMonitor());
                GC.KeepAlive(mutex);
            }
            if (restartArguments != null) PrinterStatusProgram.Start(restartArguments);
        }

        TrayMonitor()
        {
            Theme.Start();   // the menu's colours follow light / dark
            icon.Icon = Brand.TrayIcon(TrayState.Ok);  // the state icon follows every poll (ShowState)
            icon.Text = Title;
            icon.BalloonTipTitle = "";
            var menu = new ContextMenuStrip();
            var status = menu.Items.Add("", null, delegate { ShowStatus(); });
            status.Font = new Font(status.Font, FontStyle.Bold);
            // The other programs by their names (as in the Start menu, not translated), each as its own process.
            menu.Items.Add("QL Label Formats", null, delegate { Start("QL Label Formats", () => Process.Start(PrinterStatusProgram.LabelTool)); });
            menu.Items.Add("QL Label Maker", null, delegate { Start("QL Label Maker", () => EditorProgram.Start(null, null, null)); });
            menu.Items.Add(new ToolStripSeparator());
            var off = menu.Items.Add("", null, delegate
            {
                try { SetAutostart(false); } catch (Exception) { }
                ExitThread();
            });
            var about = menu.Items.Add("", null, delegate { AboutForm.ShowAbout(null, "QL Printer Status"); });
            var exitItem = menu.Items.Add("", null, delegate { ExitThread(); });
            // Texts in the language this process started with (a language change restarts the monitor).
            EventHandler texts = delegate
            {
                status.Text = Lang.T("Printer status");
                off.Text = Lang.T("Turn off printer notifications");
                about.Text = Lang.T("About QL Unidrv");
                exitItem.Text = Lang.T("Exit");
            };
            texts(null, EventArgs.Empty);
            menu.Opening += delegate { texts(null, EventArgs.Empty); };
            icon.ContextMenuStrip = menu;
            // A stalled UI thread: its stack goes to status\hang.log. HangLog posts through a control of this thread, which
            // needs a window handle (the menu has none until it opens).
            if (hangMarshal.Handle != IntPtr.Zero) HangLog.Start(hangMarshal);   // Handle creates it: a hidden window
            icon.DoubleClick += delegate { ShowStatus(); };
            icon.BalloonTipClicked += delegate { ShowStatus(); };
            icon.Visible = true;

            // "Don't notify" in QL Label Formats signals this event.
            var exit = new EventWaitHandle(false, EventResetMode.AutoReset, MutexName + ".Exit");
            ThreadPool.RegisterWaitForSingleObject(exit, delegate { exitRequested = true; }, null, -1, true);

            // A language change: start again in the new one, with the status window where it was.
            var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Lang.WatchSetting(() => ui.Post(delegate
            {
                bool open = statusForm != null && !statusForm.IsDisposed && statusForm.Visible;
                restartArguments = "--monitor" + (open ? " " + PrinterStatusProgram.RestoreArgument(statusForm) : "");
                ExitThread();
            }, null));

            showListener = PrinterStatusProgram.Listen(delegate { ShowStatus(); });
            instance = this;
            timer.Tick += delegate { Poll(); };
            timer.Start();
            Poll();
            var restore = PrinterStatusProgram.RestoreAnchor;
            if (restore != null) ShowStatus(restore);
        }

        protected override void ExitThreadCore()
        {
            exiting = true;
            timer.Stop();
            hangMarshal.Dispose();   // ends the hang watch
            showListener.Dispose();
            icon.Visible = false;
            icon.Dispose();
            base.ExitThreadCore();
        }

        static void Start(string program, Action start)
        {
            try { start(); }
            catch (Exception ex)
            {
                QlMessage.Ask(null, Title, Lang.F("{0} couldn't be started.", program), null, MessageBoxButtons.OK, MessageBoxIcon.Error,
                              MessageBoxDefaultButton.Button1, ex.Message);
            }
        }

        void ShowStatus(Point? at = null)
        {
            if (statusForm != null && !statusForm.IsDisposed) { PrinterStatusProgram.Front(statusForm); return; }
            if (opening) return;   // menu, double click and balloon arriving while the window is being built: one window
            opening = true;
            try
            {
                statusForm = new StatusForm() { StartAnchor = at };
                statusForm.Show();
            }
            finally { opening = false; }
        }

        static List<string> StatusPrinters()
        {
            return QlLabels.MainForm.OurPrinters().Where(p => QlDevice.HasStatus(QlDevice.PortOf(p))).ToList();
        }

        async void Poll()
        {
            if (running) return;
            // The status window shows the same and reads the printers itself.
            if (statusForm != null && !statusForm.IsDisposed && statusForm.Visible) return;
            if (exitRequested) { ExitThread(); return; }
            running = true;
            List<PrinterCheck> checks;
            try { checks = await Task.Run(() => StatusPrinters().AsParallel().AsOrdered().Select(Check).Where(c => c != null).ToList()); }
            catch (Exception) { running = false; return; }
            running = false;
            if (exiting) return;   // tray Exit, the setup or a language restart ended the monitor during the poll
            var problems = checks.Where(Report).ToList();
            ShowState(checks, problems);
            // Neutral: the details are in the notification and the status window.
            icon.Text = problems.Count == 0 ? Title
                : Title + ": " + (problems.Count == 1 ? Lang.T("1 printer needs attention") : Lang.F("{0} printers need attention", problems.Count));
        }

        // The icon's state: an error (red badge), another problem (yellow badge), no printer to watch or none
        // connected (grey), else ready (petrol). The icons are loaded once (Brand.TrayIcon).
        void ShowState(List<PrinterCheck> checks, List<PrinterCheck> problems)
        {
            TrayState state = problems.Any(c => c.Health == Health.Error) ? TrayState.Error
                : problems.Count > 0 ? TrayState.Warning
                : checks.Count == 0 || checks.All(c => c.Device != null && c.Device.State == DeviceState.NotConnected) ? TrayState.Offline
                : TrayState.Ok;
            var shown = Brand.TrayIcon(state);
            if (icon.Icon != shown) icon.Icon = shown;
        }

        // Asks the printer (only while its queue is empty, so a job is never disturbed): an idle printer reports an
        // open cover or a missing roll only when asked.
        static PrinterCheck Check(string printer)
        {
            try { return PrinterCheck.Run(printer, true); }
            catch (Exception) { return null; }
        }

        // Notifies a new problem; true while the printer has one.
        bool Report(PrinterCheck c)
        {
            // What stops printing: an error, a warning while jobs wait (not accepted, not connected, no answer), or
            // Windows' "Use Printer Offline" (the next job would wait silently; the notification opens the fix).
            // A printer share the host no longer shares or a host that doesn't answer: once on the change.
            bool problem = c.Health == Health.Error || (c.Health == Health.Warning && c.JobsWaiting) || c.WorkOffline || c.ShareProblem;
            if (!problem)
            {
                notifiedProblem.Remove(c.Printer);
                problemSince.Remove(c.Printer);
                return false;
            }
            string shown;
            if (notifiedProblem.TryGetValue(c.Printer, out shown) && shown == c.Line) return true;  // once per problem
            // The badge at once, the notification once the problem has held 10 s (a single slow or missed refresh of a
            // printer share popped up "no connection" in 0.19.8).
            KeyValuePair<string, DateTime> since;
            var now = DateTime.UtcNow;
            if (!problemSince.TryGetValue(c.Printer, out since) || since.Key != c.Line)
                problemSince[c.Printer] = since = new KeyValuePair<string, DateTime>(c.Line, now);
            if (now - since.Value < NotifyAfter) return true;
            notifiedProblem[c.Printer] = c.Line;
            // The printer as the title (a long one is cut there), the status window's short line first.
            var lines = new List<string> { c.Line };
            if (c.Detail != null && !c.DetailIsTitle) lines.Add(c.Detail);
            lines.AddRange(c.Advice);
            Notify(c.Printer, string.Join("\n", lines), c.Health == Health.Error ? ToolTipIcon.Error : ToolTipIcon.Warning);
            return true;
        }

        void Notify(string title, string text, ToolTipIcon kind)
        {
            icon.ShowBalloonTip(15000, title, text.Length > 250 ? text.Substring(0, 247) + "..." : text, kind);
        }
    }
}
