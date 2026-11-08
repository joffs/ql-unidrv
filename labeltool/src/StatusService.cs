// The optional Windows service "QL printer status" (QlLabelFormats.exe --service, LocalSystem; install.ps1
// -StatusService on|off). It reads every QL printer of this PC every 3 s, as the monitor does: over USB (drain
// the printer's messages; ESC i S only while the queue is empty and the printer isn't printing), or with SNMP for
// a queue on a Standard TCP/IP port (NetworkStatus.cs); and it publishes the
// result in the print queue's printer data (PublishedStatus.cs). Then QL Label Formats, the status window and the
// monitor of every user on this PC, and every PC that prints through the printer's share, read it from there;
// none of them opens the printer itself while the service runs, so there is one reader per printer.
// What it reads goes to C:\ProgramData\QLUnidrv\log\printer-status.log (the driver's folder, which users can only read;
// for bug reports); status\, the only folder users may write to, is not its business: it runs as SYSTEM.
// Problems go to the Windows event log (Application, source "QLStatus"), each one at most once an hour.
// The test kit (tools\testkit\qltest.ps1) sends its streams straight to the USB printer, past the queue: a read or
// status request of ours in the middle spoils them (2026-11-03). It pauses the USB reading with a custom control,
// which Windows' default service DACL lets every interactive user send; each one pauses for at most PauseLimit
// (docs/status-service.md "The test kit").
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace QlLabels
{
    sealed class StatusService : ServiceBase
    {
        static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

        sealed class PrinterState
        {
            public readonly List<PublishedEntry> Entries = new List<PublishedEntry>();
            public string LastBody;
            public DateTime LastWrite = DateTime.MinValue;
        }

        readonly Dictionary<string, PrinterState> states = new Dictionary<string, PrinterState>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, DateTime> reported = new Dictionary<string, DateTime>();
        readonly ManualResetEvent stopping = new ManualResetEvent(false);
        Thread worker;

        // Custom controls (128-255): pause the USB reading (again: renews the pause), resume it. Other codes do nothing.
        public const int PauseUsbCommand = 128, ResumeUsbCommand = 129;
        public static readonly TimeSpan PauseLimit = TimeSpan.FromMinutes(10);
        // Written once a pause has begun (no USB read running any more): the pause's end, UTC, round-trip format. In
        // the log folder, which users can only read, so the kit can trust it; removed when the pause ends.
        internal static string PauseFile = System.IO.Path.Combine(LabelStore.DataDir, @"log\status-pause.txt");
        readonly object usbLock = new object();   // held while a USB printer is read
        DateTime usbPausedUntil = DateTime.MinValue;

        public StatusService()
        {
            ServiceName = PublishedStatus.ServiceName;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        public static void RunService() { Run(new StatusService()); }

        protected override void OnStart(string[] args)
        {
            QlDevice.SoleReader = true;
            EndPause();   // left by a service that didn't stop cleanly
            worker = new Thread(Loop) { IsBackground = true, Name = "QL status poll" };
            worker.Start();
        }

        protected override void OnStop()
        {
            stopping.Set();
            // A poll ends within seconds (SNMP: at most 8 s per network printer, and the loop checks stopping between
            // printers); states may only be touched once it has.
            if (worker != null && !worker.Join(10000))
            {
                RequestAdditionalTime(30000);
                worker.Join();
            }
            // Readers then show "the service has stopped" instead of an outdated state. Each printer on its own: one
            // that was removed meanwhile (pending deletion, "invalid printer name", 2026-11-06) must not keep the
            // service from stopping.
            foreach (var printer in states.Keys.ToList())
                try { Publish(printer, new Published { Poll = DateTime.UtcNow, State = "Stopped" }, states[printer], true); }
                catch (Exception ex) { Report(printer, ex); }
            EndPause();
        }

        protected override void OnShutdown() { OnStop(); }

        protected override void OnCustomCommand(int command) { Command(command); }

        // Returns once no USB read runs any more (the lock), then confirms the pause in PauseFile.
        internal void Command(int command)
        {
            if (command == PauseUsbCommand)
                lock (usbLock)
                {
                    usbPausedUntil = DateTime.UtcNow + PauseLimit;
                    try { System.IO.File.WriteAllText(PauseFile, usbPausedUntil.ToString("o")); }
                    catch (Exception ex) { Report("pause", ex); }
                }
            else if (command == ResumeUsbCommand)
                lock (usbLock) EndPause();
        }

        void EndPause()
        {
            usbPausedUntil = DateTime.MinValue;
            try { System.IO.File.Delete(PauseFile); } catch (Exception) { }
        }

        // Called with usbLock held; ends a pause whose time is up (a kit that was closed without resuming).
        bool UsbPaused()
        {
            if (usbPausedUntil == DateTime.MinValue) return false;
            if (DateTime.UtcNow < usbPausedUntil) return true;
            EndPause();
            return false;
        }

        void Loop()
        {
            do
            {
                try { PollAll(); }
                catch (Exception ex) { Report("poll", ex); }
            } while (!stopping.WaitOne(Interval));
        }

        void PollAll()
        {
            var printers = QlPrinters.OurPrinters().Where(p => QlDevice.IsLocalStatusPort(QlDevice.PortOf(p))).ToList();
            foreach (var gone in states.Keys.Where(k => !printers.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            {
                states.Remove(gone);
                try { PublishedStatus.Delete(gone); } catch (Exception) { }  // printer removed or no longer on USB
            }
            foreach (var printer in printers)
            {
                if (stopping.WaitOne(0)) return;
                // Paused: its published status isn't refreshed, readers call it outdated after 3 minutes.
                if (QlDevice.IsUsbPort(QlDevice.PortOf(printer)))
                    lock (usbLock)
                    {
                        if (UsbPaused()) continue;
                        try { Poll(printer); }
                        catch (Exception ex) { Report(printer, ex); }
                    }
                else
                    try { Poll(printer); }
                    catch (Exception ex) { Report(printer, ex); }
            }
        }

        void Poll(string printer)
        {
            PrinterState st;
            if (!states.TryGetValue(printer, out st)) states[printer] = st = new PrinterState();
            List<JobInfo> jobs = null;
            try { jobs = Spooler.Jobs(printer).Where(j => !j.Finished).ToList(); }
            catch (Exception) { }
            var last = st.Entries.LastOrDefault();
            var start = DateTime.UtcNow;
            var r = QlDevice.QueryLocal(printer, QlDevice.PortOf(printer), jobs != null && jobs.Count == 0,
                                        last != null ? last.Status : null, false);
            StatusLog.AppendService(printer, r.Messages, r.Reply);
            foreach (var m in r.Messages) st.Entries.Add(new PublishedEntry { Status = m });
            if (r.Reply != null)
            {
                // A reply that repeats the last entry only refreshes the poll (as the status log skips repeats).
                last = st.Entries.LastOrDefault();
                if (last == null || !last.IsReply || last.Status.Hex != r.Reply.Hex) st.Entries.Add(new PublishedEntry { Status = r.Reply, IsReply = true });
            }
            if (st.Entries.Count > PublishedStatus.MaxEntries) st.Entries.RemoveRange(0, st.Entries.Count - PublishedStatus.MaxEntries);
            var p = new Published
            {
                Poll = start, State = r.State == DeviceState.Open ? "Open" : r.State == DeviceState.NotConnected ? "NotConnected" : "Failed",
                Error = r.Error, Reply = r.Reply != null, RequestSent = r.RequestSent, RequestBlocked = r.RequestBlocked, Printing = r.Printing,
            };
            Publish(printer, p, st, false);
        }

        // Writes only what changed, plus a heartbeat: each write notifies every program watching the queue.
        static void Publish(string printer, Published p, PrinterState st, bool force)
        {
            p.Entries.Clear();
            p.Entries.AddRange(st.Entries);
            var poll = p.Poll;
            p.Poll = DateTime.MinValue;
            string body = PublishedStatus.Format(p);
            p.Poll = poll;
            if (!force && body == st.LastBody && DateTime.UtcNow - st.LastWrite < PublishedStatus.Heartbeat) return;
            PublishedStatus.Write(printer, PublishedStatus.Format(p));
            st.LastBody = body;
            st.LastWrite = DateTime.UtcNow;
        }

        void Report(string what, Exception ex)
        {
            string key = what + ": " + ex.Message;
            DateTime at;
            if (reported.TryGetValue(key, out at) && DateTime.UtcNow - at < TimeSpan.FromHours(1)) return;
            reported[key] = DateTime.UtcNow;
            try { EventLog.WriteEntry(key, EventLogEntryType.Warning); } catch (Exception) { }
        }
    }
}
