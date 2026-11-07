// What is the printer doing, and what should the user do? Combines the print queue (and its "Use Printer
// Offline" switch), the printer's own messages (QlDevice) and the last message logged by any process (StatusLog).
using System;
using System.Collections.Generic;
using System.Linq;

namespace QlLabels
{
    enum Health { Ok, Busy, Info, Warning, Error }

    sealed class PrinterCheck
    {
        public string Printer;
        public DeviceResult Device;
        public List<JobInfo> Jobs = new List<JobInfo>();  // unfinished jobs
        public QlStatus Media;        // the newest message with media information (reply or logged)
        public bool MediaIsCurrent;   // Media is the printer's answer just now
        public QlStatus LastMessage;  // the newest message (just read or logged)
        public Health Health;
        public string Headline;       // short: "Wrong media"
        public string Cause;          // what causes it, in a few words: "the roll doesn't fit the job" (errors and warnings)
        public string Detail;         // the full sentence, or null
        public bool DetailIsTitle;    // Detail only repeats the printer's problem (the state line says it)
        public readonly List<string> Advice = new List<string>();
        public bool WorkOffline;      // Windows' "Use Printer Offline" is on for this PC's queue
        public bool OfferRemove;      // the host no longer shares the printer: the card offers to remove this PC's printer

        // A client's printer share the host no longer shares, or a host that doesn't answer: printing through it fails
        // or waits (the tray notifies it once, a test print asks first).
        public bool ShareProblem
        {
            get { return Device != null && (Device.State == DeviceState.NotShared || Device.State == DeviceState.Unreachable); }
        }

        static readonly TimeSpan ErrorMemory = TimeSpan.FromHours(12);
        // The models with an Editor Lite button (in that mode Windows sees a USB drive instead of the printer).
        static readonly string[] EditorLiteModels = { "QL-700", "QL-800", "QL-810W", "QL-820NWB" };
        static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(20);

        public bool JobsWaiting { get { return Jobs.Count > 0; } }

        // After a print: asks again every 3 s until the printer's own answer is in or it reports a problem, at most 5 times.
        // The first ask may still see the job in the queue and get no answer ("Connected" / "Printing" stayed on the
        // QL-570, x64 checks 2026-10-31 and 2026-11-01). last: the newest check; ask: starts one; stop: the window is gone
        // or shows another printer.
        public static void AskAfterPrint(Func<PrinterCheck> last, Action ask, Func<bool> stop)
        {
            int tries = 0;
            var later = new System.Windows.Forms.Timer { Interval = 3000 };
            later.Tick += delegate
            {
                var c = last();
                bool settled = tries > 0 && c != null && (c.MediaIsCurrent || c.Health == Health.Error || c.Health == Health.Warning);
                if (stop() || settled || ++tries > 5) { later.Dispose(); return; }
                ask();
            };
            later.Start();
        }

        // request: also ask the printer (only happens when the queue is empty).
        public static PrinterCheck Run(string printer, bool request)
        {
            List<JobInfo> jobs = null;
            string queueError = null;
            try { jobs = Spooler.Jobs(printer).Where(j => !j.Finished).ToList(); }
            catch (Exception ex) { queueError = Lang.T("The print queue can't be read:") + " " + ex.Message; }
            var device = QlDevice.Query(printer, request && jobs != null && jobs.Count == 0);
            // A printer share: the host's queue holds every PC's jobs (this PC's arrive there once sent). Not while the
            // host no longer shares it or doesn't answer (asking would only wait for the network's timeout).
            if (device.IsPublished && device.Source != printer && device.State != DeviceState.NotShared && device.State != DeviceState.Unreachable)
            {
                try
                {
                    var remote = PublishedStatus.WithDeadline("jobs " + device.Source, () => Spooler.Jobs(device.Source).Where(j => !j.Finished).ToList());   // the host may drop off now (case 13b)
                    jobs = (jobs ?? new List<JobInfo>()).Concat(remote).ToList();
                }
                catch (Exception) { }
            }
            var c = From(printer, device, jobs ?? new List<JobInfo>(), device.IsPublished ? device.Logged : StatusLog.Last(printer));
            bool offline = false;
            try { offline = Spooler.WorkOffline(printer); } catch (Exception) { }
            if (offline && OfflineIsCause(device)) c.SetWorkOffline();
            if (queueError != null) c.Advice.Add(queueError);
            return c;
        }

        // Windows sets "Use Printer Offline" by itself when a USB printer is switched off, and clears it when the printer
        // is back (2026-11-07): then the printer is "not connected", and "use online" wouldn't help. Offline counts only
        // while the printer (or the PC sharing it) is there and answering: shown only when it is the cause.
        public static bool OfflineIsCause(DeviceResult device)
        {
            return device == null || !(device.State == DeviceState.NotConnected || device.State == DeviceState.Unreachable || device.State == DeviceState.NotShared);
        }

        // Windows' "Use Printer Offline" on this PC's queue: whatever the printer reports, nothing reaches it, and
        // jobs wait without an error. Replaces the diagnosis; the printer's own state stays as a last line.
        public void SetWorkOffline()
        {
            WorkOffline = true;
            string printerSays = Health == Health.Info ? null : Health == Health.Ok ? Short : Headline;
            int waiting = Jobs.Count;
            Health = waiting > 0 ? Health.Error : Health.Warning;
            Headline = Lang.T("'Use Printer Offline' is on");
            Says(Lang.T("Offline"), Lang.T("Windows holds print jobs back"));
            DetailIsTitle = false;
            Detail = waiting == 0 ? Lang.T("Windows holds print jobs back on this PC and doesn't send them to the printer, without an error.")
                : waiting == 1 ? Lang.T("1 print job is waiting on this PC: Windows holds it back and doesn't send it to the printer.")
                : Lang.F("{0} print jobs are waiting on this PC: Windows holds them back and doesn't send them to the printer.", waiting);
            Advice.Clear();
            Advice.Add(Lang.T("Click 'Use printer online' in QL Printer Status, or open the print queue and clear 'Use Printer Offline' in its Printer menu."));
            if (printerSays != null) Advice.Add(Lang.F("The printer itself: {0}", printerSays));
        }

        // logged: the newest message in the status log (already includes device.Messages).
        public static PrinterCheck From(string printer, DeviceResult device, List<JobInfo> jobs, QlStatus logged)
        {
            var c = new PrinterCheck { Printer = printer, Device = device, Jobs = jobs };
            c.LastMessage = device.Reply ?? logged ?? device.Messages.LastOrDefault();
            c.Media = c.LastMessage;
            c.MediaIsCurrent = device.Reply != null;
            c.Evaluate();
            return c;
        }

        void Evaluate()
        {
            var d = Device;
            int waiting = Jobs.Count;
            string waitingText = waiting == 1 ? Lang.T("1 print job is waiting") : Lang.F("{0} print jobs are waiting", waiting);
            switch (d.State)
            {
                case DeviceState.NotUsb:
                    if (d.Port != null && d.Port.StartsWith(@"\\"))
                        Set(Health.Info, Lang.T("No printer status for this printer"),
                            Lang.T("Printers on a network share have no status here. The PC the printer is plugged into shows it."));
                    else
                        Set(Health.Info, Lang.T("No printer status for this printer"),
                            Lang.F("The status is read over USB; {0} prints to '{1}'.", Printer, d.Port));
                    return;
                case DeviceState.NotConnected:
                    if (d.Network != null)
                    {
                        Set(Health.Warning, Lang.T("The printer doesn't answer on the network"),
                            Lang.F("Switch the printer on and check its network connection ({0}).", d.Network),
                            Lang.T("The status is read with SNMP (UDP port 161, community 'public'), which must be enabled on the printer."));
                        Says(Lang.T("No connection"), Lang.T("printer off or not on the network"));
                        if (waiting > 0) Advice.Add(Lang.F("{0} and will print when the printer is back.", waitingText));
                        return;
                    }
                    // A printer share: the cable is at the PC the printer is plugged into.
                    string at, shareName;
                    if (!PublishedStatus.IsShare(d.Source, out at, out shareName)) at = null;
                    Set(Health.Warning, Lang.T("The printer is not connected"),
                        at != null ? Lang.F("Switch the printer on and check its connection at the PC {0}.", at) : Lang.T("Switch the printer on and check the USB cable."));
                    var model = MainForm.ModelOf(Printer);
                    if (model == null || EditorLiteModels.Contains(model.Name))
                        Advice.Add(Lang.T("If the Editor Lite LED is on, hold the Editor Lite button until it goes out; otherwise Windows sees a USB drive instead of the printer."));
                    Says(Lang.T("Not connected"), at != null ? Lang.F("printer off or USB cable at {0}", at) : Lang.T("printer off or USB cable"));
                    if (waiting > 0) Advice.Add(Lang.F("{0} and will print when the printer is back.", waitingText));
                    return;
                case DeviceState.Failed:
                    Set(Health.Warning, Lang.T("The printer status can't be read"), d.Error ?? Lang.T("Unknown error."));
                    Says(Lang.T("No status"), Lang.T("reading it failed"));
                    return;
                case DeviceState.NotShared:
                case DeviceState.Unreachable:
                    {
                        string from, name;
                        if (!PublishedStatus.IsShare(d.Source, out from, out name)) { from = d.Source; name = ""; }
                        if (d.State == DeviceState.NotShared)
                        {
                            Set(Health.Warning, Lang.F("{0} no longer shares this printer", from),
                                Lang.F("On {0}, share the printer again: QL Label Formats > Printer setup > Share a printer.", from),
                                Lang.T("Shared under a new name? Then add it here again: Printer setup > Another PC's printer."));
                            Detail = Lang.T("Printing through this share doesn't work: a print job will most likely stay in this PC's queue.");
                            Says(Lang.T("Not shared"), Lang.F("{0} no longer shares '{1}'", from, name));
                            OfferRemove = true;
                            if (waiting > 0) Advice.Add(waitingText + ".");
                        }
                        else
                        {
                            Set(Health.Warning, Lang.F("{0} can't be reached", from), Lang.F("Switch {0} on and check its network connection.", from));
                            Detail = Lang.F("The status and print jobs go through {0}: a print job waits on this PC until {0} can be reached.", from);
                            Says(Lang.T("No connection"), Lang.F("{0} doesn't answer", from));
                            if (waiting > 0) Advice.Add(Lang.F("{0} and will print when the printer is back.", waitingText));
                        }
                        return;
                    }
                case DeviceState.NotPublished:
                    string host, share;
                    if (PublishedStatus.IsShare(d.Source, out host, out share))
                    {
                        if (d.Stale)
                        {
                            Set(Health.Info, Lang.F("No current printer status from {0}", host));
                            Detail = Lang.F("Last status from {1}. Printing probably works, but the service 'QL printer status' on {0} no longer reports.", host, d.PublishedAt.Value.ToString("g"));
                        }
                        else
                            Set(Health.Info, Lang.F("No printer status from {0}", host),
                                d.Error != null ? Lang.F("{0} can't be asked: {1}", host, d.Error) : null,
                                Lang.T("The PC the printer is plugged into shows its status when 'Printer status service for shared printers' is on there (Installed apps > QL Unidrv > Modify)."));
                        Advice.RemoveAll(a => a == null);
                    }
                    else
                    {
                        Set(Health.Warning, Lang.T("The printer status can't be read"),
                            d.Error != null ? Lang.F("The service 'QL printer status' doesn't publish it: {0}", d.Error)
                                            : Lang.T("The service 'QL printer status' doesn't publish it."));
                        Says(Lang.T("No status"), Lang.T("the status service doesn't publish it"));
                    }
                    if (waiting > 0) Advice.Add(waitingText + ".");
                    return;
            }

            // The printer answered: that is the current state.
            var r = d.Reply;
            if (r != null)
            {
                var problems = r.Problems();
                if (r.HasError && problems.Count > 0)
                {
                    Set(Health.Error, problems[0].Headline);
                    Cause = problems[0].Cause;
                    Detail = problems[0].Title;
                    DetailIsTitle = true;
                    Advice.AddRange(problems.Select(p => p.Fix));
                    if (problems.Count > 1) Advice.InsertRange(0, problems.Skip(1).Select(p => Lang.T("Also:") + " " + p.Title));
                }
                else if (r.NoMedia)
                {
                    Set(Health.Warning, Lang.T("No roll loaded"), Lang.T("Insert a DK roll and close the cover."));
                    Cause = Lang.T("the roll compartment is empty");
                }
                else if (r.Phase == 1)  // a network printer answers while it prints (USB: no request then)
                    Set(Health.Busy, Lang.T("Printing"));
                else
                {
                    Set(Health.Ok, Lang.T("Ready"));
                    var earlier = d.Messages.LastOrDefault(m => m.HasError);
                    if (earlier != null)
                        Advice.Add(Lang.F("The printer reported an error at {0} ({1}); it is cleared now.", earlier.Time.ToString("t"), FirstTitle(earlier)));
                }
                return;
            }

            // No answer: the printer is busy with a job, or in an error state.
            var last = LastMessage;
            bool knownError = last != null && last.HasError && DateTime.Now - last.Time < ErrorMemory;
            if (knownError)
            {
                var problems = last.Problems();
                Set(Health.Error, problems[0].Headline);
                Cause = problems[0].Cause;
                Detail = problems[0].Title;
                DetailIsTitle = true;
                if (waiting > 0)
                    Advice.Add(Lang.F("{0}. They come out incomplete once the error is cleared, so cancel them first.", waitingText));
                else
                    Advice.Add(Lang.T("The printer doesn't answer until the error is cleared (red LED blinking)."));
                Advice.AddRange(problems.Select(p => p.Fix));
                return;
            }
            if (d.Printing && waiting == 0)
            {
                Set(Health.Busy, Lang.T("Printing"));
                return;
            }
            if (waiting > 0)
            {
                var oldest = Jobs.Min(j => j.Submitted);
                if (last != null && last.Type == QlStatus.TypeNotification && last.Notification == 3)
                    Set(Health.Busy, Lang.T("Cooling down"), Lang.T("The print head is hot. Printing continues by itself after a short pause."));
                else if (DateTime.Now - oldest > StuckAfter && Jobs.All(j => j.PagesPrinted == 0 || (j.Status & Spooler.JOB_STATUS_ERROR) != 0))
                {
                    Set(Health.Warning, Lang.T("The printer isn't accepting the print job"),
                        Lang.T("If the red LED blinks, the printer is in an error state: cancel the jobs, open and close the roll cover, then print again."),
                        Lang.T("If that doesn't help, switch the printer off and on."));
                    Says(Lang.T("Job stuck"), Lang.T("the printer isn't accepting it"));
                }
                else
                    Set(Health.Busy, Lang.T("Printing"), waiting == 1 ? Lang.T("1 print job is being sent to the printer.")
                                                                       : Lang.F("{0} print jobs are being sent to the printer.", waiting));
                return;
            }
            if (d.RequestBlocked || d.RequestSent)
            {
                Set(Health.Warning, Lang.T("The printer doesn't answer"),
                    Lang.T("If the red LED blinks, it is in an error state: open and close the roll cover."),
                    Lang.T("If that doesn't help, switch the printer off and on."));
                Says(Lang.T("No answer"), Lang.T("maybe an error state (red LED)"));
            }
            else
                Set(Health.Info, Lang.T("Connected"));
        }

        void Set(Health h, string headline, params string[] advice)
        {
            Health = h;
            Headline = headline;
            Advice.AddRange(advice);
        }

        // An error or warning's short form: its kind (the state line's first part) and cause.
        void Says(string kind, string cause)
        {
            Kind = kind;
            Cause = cause;
        }

        string Kind;  // when it differs from the Headline (a whole sentence)

        // One line for the status window and the tray notification: "Wrong media – the roll doesn't fit the job";
        // ready: "Ready, 62 mm continuous loaded"; other states: the headline.
        public string Line
        {
            get
            {
                if (Health == Health.Ok) return Short;
                if (Cause == null) return Headline;
                return (Kind ?? Headline) + " – " + Cause;
            }
        }

        static string FirstTitle(QlStatus s)
        {
            var p = s.Problems();
            return p.Count > 0 ? p[0].Title.TrimEnd('.') : s.TypeText;
        }

        public string LoadedText
        {
            get
            {
                if (Media == null) return null;
                return Media.LoadedText + (MediaIsCurrent ? "" : " " + Lang.F("(last reported {0})", Media.Time.ToString("g")));
            }
        }

        // "Status from HOST (12:03:15)" for a printer share, else null.
        public string Via
        {
            get
            {
                string host, share;
                if (Device == null || !PublishedStatus.IsShare(Device.Source, out host, out share) || Device.PublishedAt == null) return null;
                return Lang.F("Status from {0} ({1})", host, Device.PublishedAt.Value.ToString("T"));
            }
        }

        // For the status bar / tray tooltip.
        public string Short
        {
            get
            {
                string loaded = Media != null && !Media.NoMedia ? Media.LoadedText : null;
                if (Health == Health.Ok) return loaded != null ? Lang.F("Ready, {0} loaded", loaded) : Lang.T("Ready");
                if (WorkOffline) return Lang.T("offline – Windows holds print jobs back");
                if (Device != null && Device.State == DeviceState.NotConnected) return Lang.T("not connected");
                return Headline;
            }
        }
    }
}
