// Unit tests for the printer status decoding (QlStatus) and the diagnosis rules (PrinterCheck.From).
// The messages are real QL-800 replies recorded on 2026-10-16.
// Built and run by labeltool\build.cmd (compiled together with src\*.cs, /main:QlLabels.Tests.StatusTest).
using System;
using System.Collections.Generic;
using System.Linq;

namespace QlLabels.Tests
{
    static class StatusTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static QlStatus Msg(string hex, DateTime? time = null)
        {
            var s = QlStatus.FromHex(hex, time ?? DateTime.Now);
            if (s == null) throw new Exception("not a status message: " + hex);
            return s;
        }

        // Recorded on the QL-800 with the 62 mm continuous roll (DK-22205).
        const string Idle = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
        const string Printing = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 06 01 00 00 00 00 00 01 00 00 00 00 00 00";
        const string WrongMedia = "80 20 42 34 38 30 00 00 00 01 3E 0A 00 00 15 00 00 00 02 01 00 00 00 00 00 01 00 00 00 00 00 00";
        // A black/red job sent to the DK-22205 (2026-10-17): error with both error bytes 0, byte 31 0x23.
        const string RedJobOnBlackRoll = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 02 01 00 00 00 00 00 01 00 00 00 00 00 23";
        const string AfterCover = "80 20 42 34 38 30 00 00 20 00 3E 0A 00 00 15 00 00 00 02 00 00 00 00 00 00 01 00 00 00 00 00 00";
        // Recorded on the QL-570 with the 62 mm continuous roll.
        // QL-800 with the red/black roll DK-22251 (2026-10-17): byte 14 0x23, byte 25 (text colour) 0x81.
        const string BlackRedIdle = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 23 00 00 00 00 00 00 00 00 00 00 81 00 00 00 00 00 00";
        const string QL570Idle = "80 20 42 34 32 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00";
        // Constructed: 29 x 90 mm die-cut, 62 x 29 mm die-cut, 12 mm round, no media.
        const string DieCut29x90 = "80 20 42 34 38 30 00 00 00 00 1D 0B 00 00 15 00 00 5A 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
        const string DieCut62x29 = "80 20 42 34 38 30 00 00 00 00 3E 0B 00 00 15 00 00 1D 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
        const string Round12 = "80 20 42 34 38 30 00 00 00 00 0C 0B 00 00 15 00 00 0C 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
        const string NoMediaError = "80 20 42 34 38 30 00 00 01 00 00 00 00 00 15 00 00 00 02 00 00 00 00 00 00 01 00 00 00 00 00 00";

        static int Main()
        {
            Lang.Force("en");  // the checks compare English texts, whatever this PC's language
            Decoding();
            Diagnosis();
            PaperNumber256();
            PublishedFormat();
            ShareMatrix();
            PrintingGuard();
            ShareDeadline();
            SnmpEncoding();
            ServicePause();
            HelpFolder();
            failures += FormatTest.Run();
            failures += LangTest.Run();
            Console.WriteLine(failures == 0 ? "program tests (status, formats, languages): all OK" : "program tests: " + failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        // The forms database in memory: AddForm appends, DeleteForm closes the gap (as Windows does).
        sealed class FakeForms : IFormsWriter
        {
            public readonly List<FormInfo> Forms = new List<FormInfo>();
            public List<FormInfo> Enumerate() { return Forms.ToList(); }
            public void Add(string name, int cx, int cy) { Forms.Add(new FormInfo { Name = name, Flags = FormsDb.FORM_USER, Cx = cx, Cy = cy }); }
            public bool Delete(string name) { return Forms.RemoveAll(f => f.Name == name && f.Flags == FormsDb.FORM_USER) > 0; }
            public FakeForms(int builtIn, int printer, params string[] user)
            {
                for (int i = 0; i < builtIn; i++) Forms.Add(new FormInfo { Name = "W" + i, Flags = FormsDb.FORM_BUILTIN });
                for (int i = 0; i < printer; i++) Forms.Add(new FormInfo { Name = "P" + i, Flags = FormsDb.FORM_PRINTER });
                foreach (var u in user) Add(u, 1, 1);
            }
            public string At256 { get { return Forms.Count >= 256 ? Forms[255].Name : null; } }
        }

        // HelpPages: <exe folder>\help, else (Setup.exe at the top of a release zip) advanced\out\tools\help; the page in
        // the program's language, else English.
        static void HelpFolder()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qlunidrv-help-" + Guid.NewGuid().ToString("N"));
            try
            {
                Check(HelpPages.Folder(root) == null, "help: no folder");
                string zipHelp = System.IO.Path.Combine(root, @"advanced\out\tools\help");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(zipHelp, @"formats\en"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(zipHelp, @"formats\en\setup.html"), "");
                Check(HelpPages.Folder(root) == zipHelp, "help: the release zip's place");
                string help = System.IO.Path.Combine(root, "help");
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(help, @"formats\de"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(help, @"formats\en"));
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(help, "de"));
                System.IO.File.WriteAllText(System.IO.Path.Combine(help, @"formats\de\status.html"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(help, @"formats\en\status.html"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(help, @"formats\en\setup.html"), "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(help, @"de\index.html"), "");
                Check(HelpPages.Folder(root) == help, "help: next to the exe first");
                Check(HelpPages.Find(help, HelpPages.Status, "de") == System.IO.Path.Combine(help, @"formats\de\status.html"), "help: German page");
                Check(HelpPages.Find(help, HelpPages.Status, "en") == System.IO.Path.Combine(help, @"formats\en\status.html"), "help: English page");
                Check(HelpPages.Find(help, HelpPages.Setup, "de") == System.IO.Path.Combine(help, @"formats\en\setup.html"), "help: English when German is missing");
                Check(HelpPages.Find(help, HelpPages.LabelMaker, "de") == System.IO.Path.Combine(help, @"de\index.html"), "help: QL Label Maker's pages");
                Check(HelpPages.Find(help, HelpPages.Install, "en") == null && HelpPages.Find(null, HelpPages.Status, "en") == null, "help: missing page");
            }
            finally { try { System.IO.Directory.Delete(root, true); } catch (Exception) { } }
        }

        // The test kit's pause of the service's USB reading (StatusService.Command): confirmed in the pause file only
        // once a running USB read has ended, at most PauseLimit long, ended by resume.
        static void ServicePause()
        {
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qlunidrv-pause-" + Guid.NewGuid().ToString("N") + ".txt");
            StatusService.PauseFile = file;
            var service = new StatusService();
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            object usbLock = typeof(StatusService).GetField("usbLock", flags).GetValue(service);
            Func<bool> paused = () => { lock (usbLock) return (bool)typeof(StatusService).GetMethod("UsbPaused", flags).Invoke(service, null); };
            try
            {
                var reading = new System.Threading.ManualResetEvent(false);
                var reader = new System.Threading.Thread(() => { lock (usbLock) { reading.Set(); System.Threading.Thread.Sleep(600); } });
                reader.Start();
                reading.WaitOne();
                var start = DateTime.UtcNow;
                service.Command(StatusService.PauseUsbCommand);
                Check(DateTime.UtcNow - start > TimeSpan.FromMilliseconds(400), "pause: waits for the running USB read");
                reader.Join();
                Check(System.IO.File.Exists(file), "pause: confirmed in the pause file");
                var until = DateTime.Parse(System.IO.File.ReadAllText(file), null, System.Globalization.DateTimeStyles.RoundtripKind);
                Check(Math.Abs((until - DateTime.UtcNow - StatusService.PauseLimit).TotalSeconds) < 5, "pause: ends after PauseLimit (" + until.ToString("o") + ")");
                Check(paused(), "pause: USB reading paused");
                service.Command(130);
                Check(paused() && System.IO.File.Exists(file), "pause: other codes change nothing");
                service.Command(StatusService.ResumeUsbCommand);
                Check(!paused() && !System.IO.File.Exists(file), "resume: reading again, pause file removed");
                service.Command(StatusService.PauseUsbCommand);
                typeof(StatusService).GetField("usbPausedUntil", flags).SetValue(service, DateTime.UtcNow.AddSeconds(-1));
                Check(!paused() && !System.IO.File.Exists(file), "pause: ends by itself when its time is up");
            }
            finally { try { System.IO.File.Delete(file); } catch (Exception) { } }
        }

        // The service's published status (PublishedStatus.cs): text round trip and what the readers make of it.
        static void PublishedFormat()
        {
            const string Share = @"\\PRINTHOST\QL-800";
            string host, share;
            Check(PublishedStatus.IsShare(Share, out host, out share) && host == "PRINTHOST" && share == "QL-800", "share port");
            Check(!PublishedStatus.IsShare("USB001", out host, out share) && !PublishedStatus.IsShare(@"\\h\s\x", out host, out share) &&
                  !PublishedStatus.IsShare(@"C:\ProgramData\QLUnidrv\capture\capture.prn", out host, out share), "not share ports");
            Check(QlDevice.HasStatus(@"\\PRINTHOST\QL-570") && QlDevice.HasStatus("USB002") && !QlDevice.HasStatus("FILE:"), "ports with a status");

            var poll = new DateTime(2026, 10, 24, 12, 0, 3, DateTimeKind.Utc);
            var p = new Published { Poll = poll, State = "Open", Reply = true, RequestSent = true };
            p.Entries.Add(new PublishedEntry { Status = Msg(WrongMedia, poll.AddMinutes(-5).ToLocalTime()) });
            p.Entries.Add(new PublishedEntry { Status = Msg(Printing, poll.AddSeconds(1).ToLocalTime()) });
            p.Entries.Add(new PublishedEntry { Status = Msg(Idle, poll.AddSeconds(2).ToLocalTime()), IsReply = true });
            string text = PublishedStatus.Format(p);
            var q = PublishedStatus.Parse(text);
            Check(q != null && q.Poll == poll && q.State == "Open" && q.Reply && q.RequestSent && !q.Printing && q.Entries.Count == 3 &&
                  q.Entries[2].IsReply && q.Entries[0].Status.Hex == WrongMedia && PublishedStatus.Format(q) == text, "published: round trip");
            Check(PublishedStatus.Parse("something else") == null && PublishedStatus.Parse(null) == null, "published: not ours");

            // What another PC publishes is limited (security audit 2026-10-24 M1).
            var hostile = new System.Text.StringBuilder("QLUnidrv status 1\r\npoll\t2026-10-24T12:00:03Z\tWhatever\t\tCall\u0007 +1 555 " + new string('x', 500) + "\r\n");
            hostile.Append("2026-10-24T12:00:03Z\t" + WrongMedia.Replace("80", "FFF") + "\tmessage\r\n");  // bad byte: line skipped
            for (int i = 0; i < 40; i++) hostile.Append("2026-10-24T12:00:03Z\t" + Idle + "\tmessage\r\n");
            var h = PublishedStatus.Parse(hostile.ToString());
            Check(h != null && h.State == "Failed" && h.Error.Length == 200 && !h.Error.Contains("\u0007") &&
                  h.Entries.Count == PublishedStatus.MaxEntries - 1, "published: hostile text limited");
            Check(PublishedStatus.ToDevice("x", Share, Share, q, poll.AddMinutes(-10)).Stale, "published: poll time in the future");

            var d = PublishedStatus.ToDevice("Brother QL-800 (Unidrv)", Share, Share, q, poll.AddSeconds(30));
            Check(d.State == DeviceState.Open && d.IsPublished && !d.Stale && d.Reply != null && d.Reply.Hex == Idle &&
                  d.Messages.Count == 1 && d.Messages[0].Hex == Printing && d.Logged.Hex == Idle, "published: current reply, this poll's messages");
            var c = PrinterCheck.From(d.Printer, d, new List<JobInfo>(), d.Logged);
            Check(c.Health == Health.Ok && c.MediaIsCurrent && c.Via != null && c.Via.StartsWith("Status from PRINTHOST"), "published: ready, via host");

            d = PublishedStatus.ToDevice("x", Share, Share, q, poll.AddMinutes(10));
            c = PrinterCheck.From(d.Printer, d, new List<JobInfo>(), d.Logged);
            Check(d.Stale && d.State == DeviceState.NotPublished && c.Health == Health.Info && c.Headline == "No current printer status from PRINTHOST", "published: outdated");
            // Outdated after 3 minutes (the 60 s heartbeat + 2 min for clock differences), 2026-11-07
            Check(!PublishedStatus.ToDevice("x", Share, Share, q, poll.AddSeconds(170)).Stale && PublishedStatus.ToDevice("x", Share, Share, q, poll.AddSeconds(190)).Stale &&
                  !PublishedStatus.ToDevice("x", Share, Share, q, poll.AddSeconds(-170)).Stale, "published: outdated after 3 minutes, either clock ahead");
            q.State = "Stopped";
            Check(PublishedStatus.ToDevice("x", "USB001", "x", q, poll.AddSeconds(5)).Stale, "published: service stopped");
            c = PrinterCheck.From("x", PublishedStatus.ToDevice("x", Share, Share, null, poll), new List<JobInfo>(), null);
            Check(c.Health == Health.Info && c.Headline == "No printer status from PRINTHOST", "published: host without the service");

            // A network printer that doesn't answer, as the service publishes it (Network is set by NetworkStatus.Query).
            var off = PublishedStatus.Parse("QLUnidrv status 1\r\npoll\t2026-10-24T12:00:03Z\tNotConnected\trequest\tNo answer.\r\n");
            d = PublishedStatus.ToDevice("x", "USB001", "x", off, poll.AddSeconds(4));
            d.Network = "192.0.2.50";
            c = PrinterCheck.From("x", d, new List<JobInfo>(), d.Logged);
            Check(d.State == DeviceState.NotConnected && c.Headline == "The printer doesn't answer on the network", "published: network printer off");

            // The printer reported an error and answers nothing: the error stays known.
            // (now, not the fixed test time: PrinterCheck remembers an error for a while by the real clock)
            var now = DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond));
            var e = new Published { Poll = now, State = "Open", RequestSent = true };
            e.Entries.Add(new PublishedEntry { Status = Msg(WrongMedia, now.AddSeconds(1).ToLocalTime()) });
            d = PublishedStatus.ToDevice("x", "USB001", "x", PublishedStatus.Parse(PublishedStatus.Format(e)), now.AddSeconds(4));
            c = PrinterCheck.From("x", d, new List<JobInfo>(), d.Logged);
            Check(d.Reply == null && c.Health == Health.Error && c.Headline == "Wrong media", "published: error message without a reply");

            // A poll without a request (job in the queue, since gone): a reply under a minute old still counts,
            // an older one doesn't (Verbunden gap, 2026-10-31).
            var g = new Published { Poll = now, State = "Open" };
            g.Entries.Add(new PublishedEntry { Status = Msg(Idle, now.AddSeconds(-20).ToLocalTime()), IsReply = true });
            g = PublishedStatus.Parse(PublishedStatus.Format(g));
            d = PublishedStatus.ToDevice("x", "USB001", "x", g, now.AddSeconds(2));
            c = PrinterCheck.From("x", d, new List<JobInfo>(), d.Logged);
            Check(d.Reply != null && d.Reply.Hex == Idle && c.Health == Health.Ok && c.MediaIsCurrent, "published: fresh reply without a request");
            d = PublishedStatus.ToDevice("x", "USB001", "x", g, now.AddSeconds(70));
            Check(d.Reply == null, "published: old reply without a request");
            g.Printing = true;
            Check(PublishedStatus.ToDevice("x", "USB001", "x", g, now.AddSeconds(2)).Reply == null, "published: no old reply while printing");
        }

        // SNMPv1 GET of Brother's status OID (NetworkStatus.cs) and decoding a GetResponse with the 32-byte status.
        // A client's printer share (the test matrix of shared printers): the published status counts only while printing
        // through the share would work. The host is simulated: its SMB share, its spooler (which still opens a queue by
        // its old share name after "Stop sharing", seen 2026-11-06) and its published value. Case numbers = the matrix's.
        sealed class FakeHost
        {
            public bool Reachable = true, SpoolerUp = true, PrinterExists = true, Shared = true, Pending;
            public string ShareName = "QL-800";      // the queue's pShareName (stays after "Stop sharing")
            public string Published;                 // the value QLUnidrv\Status
            public int NetCalls;
            public int[] NetShare(string host, string share)
            {
                NetCalls++;
                if (!Reachable) return new[] { 53, 0 };
                bool smb = SpoolerUp && PrinterExists && Shared && string.Equals(share, ShareName, StringComparison.OrdinalIgnoreCase);
                return smb ? new[] { 0, 1 } : new[] { 2310, 0 };
            }
            int Resolve(string queue)
            {
                if (!Reachable) return 53;
                if (!SpoolerUp) return 1722;
                if (!PrinterExists || !queue.EndsWith(@"\" + ShareName, StringComparison.OrdinalIgnoreCase)) return 1801;
                return 0;
            }
            public PublishedStatus.QueueFacts Queue(string queue)
            {
                int e = Resolve(queue);
                if (e != 0) return new PublishedStatus.QueueFacts { Error = e };
                return new PublishedStatus.QueueFacts { Attributes = Shared ? 0x8 | 0x40 : 0x40, ShareName = ShareName, Status = Pending ? 0x4 : 0 };
            }
            public string Read(string queue)
            {
                int e = Resolve(queue);
                if (e != 0) throw new System.ComponentModel.Win32Exception(e);
                return Published;
            }
        }

        // "Printing" with the end messages missing (matrix case 21): no request for 2 minutes, unless the printer's
        // Windows queue printed a job around then; 10 s after that the request is safe again.
        static void PrintingGuard()
        {
            var t0 = new DateTime(2026, 11, 7, 0, 3, 9);
            var printing = Msg(Printing, t0);
            Check(QlDevice.StillPrinting(printing, t0.AddSeconds(30), null), "guard: 'printing', no job through the queue: still printing after 30 s");
            Check(QlDevice.StillPrinting(printing, t0.AddSeconds(119), null) && !QlDevice.StillPrinting(printing, t0.AddSeconds(121), null), "guard: 2 minutes without a queue job");
            Check(QlDevice.StillPrinting(printing, t0.AddSeconds(8), t0.AddSeconds(1)), "guard: queue done 7 s ago: still settling");
            Check(!QlDevice.StillPrinting(printing, t0.AddSeconds(12), t0.AddSeconds(1)), "guard: queue done 11 s ago: ask again");
            Check(!QlDevice.StillPrinting(printing, t0.AddSeconds(11), t0.AddSeconds(-2)), "guard: the job was sent just before the 'printing' was read");
            // A 1 m label: the job is counted when its last byte was sent, long after "printing".
            Check(QlDevice.StillPrinting(printing, t0.AddSeconds(40), null) && !QlDevice.StillPrinting(printing, t0.AddSeconds(55), t0.AddSeconds(44)), "guard: long label, counted at its end");
            Check(QlDevice.StillPrinting(printing, t0.AddSeconds(30), t0.AddSeconds(-60)), "guard: a queue job a minute before doesn't explain this 'printing' (direct stream)");
            Check(!QlDevice.StillPrinting(Msg(Idle, t0), t0.AddSeconds(1), null), "guard: not printing");

            // The counter: the first reading only remembers it; a change is seen at the next reading.
            long count = 5;
            var saved = QlDevice.JobsPrinted;
            QlDevice.JobsPrinted = p => count;
            QlDevice.ForgetQueues();
            try
            {
                Check(QlDevice.QueueDone("Q", t0) == null, "queue: first reading");
                Check(QlDevice.QueueDone("Q", t0.AddSeconds(3)) == null, "queue: unchanged");
                count = 6;
                Check(QlDevice.QueueDone("Q", t0.AddSeconds(6)) == t0.AddSeconds(6), "queue: a job printed");
                Check(QlDevice.QueueDone("Q", t0.AddSeconds(9)) == t0.AddSeconds(6), "queue: remembered");
                count = -1;
                Check(QlDevice.QueueDone("Q", t0.AddSeconds(12)) == t0.AddSeconds(6), "queue: counter unreadable: kept");
                count = 0;   // the spooler restarted
                Check(QlDevice.QueueDone("Q", t0.AddSeconds(15)) == t0.AddSeconds(15), "queue: counter reset counts as a change");
                Check(QlDevice.QueueDone(null, t0) == null, "queue: no printer name (USB scan)");
            }
            finally { QlDevice.JobsPrinted = saved; QlDevice.ForgetQueues(); }

            // "No answer" only from the second miss in a row (one miss right before the printer goes away, 2026-11-07).
            QlDevice.ForgetMisses();
            Func<bool, bool, DeviceResult> poll = (sent, answered) =>
            {
                var r = new DeviceResult { Printer = "P", Port = "USB009", State = DeviceState.Open, RequestSent = sent };
                if (answered) r.Reply = Msg(Idle);
                QlDevice.OnlySecondMiss(r, r.Port);
                return r;
            };
            var miss1 = poll(true, false);
            Check(!miss1.RequestSent && PrinterCheck.From("P", miss1, new List<JobInfo>(), null).Line != "No answer – maybe an error state (red LED)", "first miss: not 'no answer'");
            var miss2 = poll(true, false);
            Check(miss2.RequestSent && PrinterCheck.From("P", miss2, new List<JobInfo>(), null).Line == "No answer – maybe an error state (red LED)", "second miss: no answer");
            poll(true, true);
            Check(!poll(true, false).RequestSent, "an answer in between starts again");
            QlDevice.ForgetMisses();

            // The client: the host's "printing" while it waits, its reply once it asked again (one poll later).
            var host = new Published { Poll = DateTime.UtcNow.AddSeconds(-1), State = "Open", Printing = true };
            host.Entries.Add(new PublishedEntry { Status = Msg(Printing, DateTime.Now.AddSeconds(-20)) });
            var d = PublishedStatus.ToDevice("P", @"\\PRINTHOST\QL-800", @"\\PRINTHOST\QL-800", PublishedStatus.Parse(PublishedStatus.Format(host)), DateTime.UtcNow);
            var c = PrinterCheck.From("Brother QL-800 (Unidrv)", d, new List<JobInfo>(), d.Logged);
            Check(c.Health == Health.Busy, "case 21 client: printing while the host waits (" + c.Headline + ")");
            host.Printing = false; host.RequestSent = true; host.Reply = true;
            host.Entries.Add(new PublishedEntry { Status = Msg(Idle, DateTime.Now), IsReply = true });
            d = PublishedStatus.ToDevice("P", @"\\PRINTHOST\QL-800", @"\\PRINTHOST\QL-800", PublishedStatus.Parse(PublishedStatus.Format(host)), DateTime.UtcNow);
            c = PrinterCheck.From("Brother QL-800 (Unidrv)", d, new List<JobInfo>(), d.Logged);
            Check(c.Health == Health.Ok, "case 21 client: ready with the host's next poll (" + c.Headline + ")");
        }

        // Matrix case 13b: the host drops off the network; Windows' calls to it hang for their own timeouts (60 s
        // here), the TCP 445 probe fails at once. One missed refresh keeps the last answer, the second says "No
        // connection"; nothing waits for the hanging calls, and none is started while the host doesn't answer.
        static void ShareDeadline()
        {
            const string Port = @"\\PRINTHOST\QL-570", Printer = "Brother QL-570 (Unidrv)";
            var saved = new object[] { PublishedStatus.NetShare, PublishedStatus.Queue, PublishedStatus.ReadValue, PublishedStatus.UtcNow, PublishedStatus.ShareDeadline, PublishedStatus.HostAnswers };
            var cable = new System.Threading.ManualResetEventSlim(true);   // reset: the host's network cable is out
            PublishedStatus.HostAnswers = x => cable.IsSet;
            int netCalls = 0, reads = 0;
            var clock = DateTime.UtcNow;
            string value = PublishedStatus.Format(new Published { Poll = clock.AddSeconds(-2), State = "Open", Reply = true, RequestSent = true,
                Entries = { new PublishedEntry { Status = Msg(QL570Idle, clock.ToLocalTime()), IsReply = true } } });
            int delay = 0;   // ms each OpenPrinter takes on a running host
            PublishedStatus.ShareDeadline = TimeSpan.FromMilliseconds(300);
            PublishedStatus.UtcNow = () => clock;
            PublishedStatus.NetShare = (h, n) => { System.Threading.Interlocked.Increment(ref netCalls); if (!cable.Wait(60000)) return new[] { 53, 0 }; return new[] { 0, 1 }; };
            PublishedStatus.Queue = q => { System.Threading.Thread.Sleep(delay); cable.Wait(60000); return new PublishedStatus.QueueFacts { Attributes = 0x48, ShareName = "QL-570" }; };
            PublishedStatus.ReadValue = q => { System.Threading.Interlocked.Increment(ref reads); System.Threading.Thread.Sleep(delay); if (!cable.Wait(60000)) throw new System.ComponentModel.Win32Exception(53); return value; };
            PublishedStatus.ForgetShareChecks();
            Func<PrinterCheck> ask = () => PrinterCheck.From(Printer, PublishedStatus.QueryShare(Printer, Port, "PRINTHOST", "QL-570"), new List<JobInfo>(), null);
            try
            {
                Check(PublishedStatus.ShareDeadline.TotalSeconds < 1 && ((TimeSpan)saved[4]).TotalSeconds >= 15, "slow host: the real deadline leaves 15 s for a slow OpenPrinter");
                Check(ask().Health == Health.Ok, "13b: ready while the host answers");
                // Slow but running (2026-11-07: OpenPrinter 0.8-4.9 s on every call; scaled to the 300 ms deadline here).
                delay = 200;
                PublishedStatus.ForgetShareChecks();
                Check(ask().Health == Health.Ok, "slow host: an open under the deadline is ready");
                delay = 700;   // past the deadline, while the host still answers on 445
                for (int i = 0; i < 4; i++)
                {
                    var slow = ask();
                    Check(slow.Health == Health.Ok && slow.Device.State == DeviceState.Open, "slow host: a deadline run out keeps the last answer, never 'no connection' (" + i + ": " + slow.Line + ")");
                    System.Threading.Thread.Sleep(150);
                }
                delay = 0;
                System.Threading.Thread.Sleep(800);   // the slow calls finish
                PublishedStatus.ForgetShareChecks();
                Check(ask().Health == Health.Ok, "13b: ready again");
                // Cable out, the share check still cached.
                cable.Reset();
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var c = ask();
                Check(c.Health == Health.Ok && watch.ElapsedMilliseconds < 200, "13b: one missed refresh keeps the last answer (" + c.Line + ")");
                c = ask();
                Check(c.Device.State == DeviceState.Unreachable && c.Line == "No connection – PRINTHOST doesn't answer" && c.Media == null && watch.ElapsedMilliseconds < 200,
                      "13b: the second missed refresh: no connection (" + c.Line + ", " + watch.ElapsedMilliseconds + " ms)");
                int calls = netCalls, readCalls = reads;
                c = ask(); c = ask();
                Check(c.Device.State == DeviceState.Unreachable && netCalls == calls && reads == readCalls,
                      "13b: while the host doesn't answer no call to it starts");
                // Cable back: the next refresh is ready again.
                cable.Set();
                c = ask();
                Check(c.Health == Health.Ok, "13b: host back: ready with the next refresh (" + c.Line + ")");
                // The probe failing also with nothing seen before: not reachable at once (nothing to keep).
                PublishedStatus.ForgetShareChecks();
                cable.Reset();
                Check(ask().Device.State == DeviceState.Unreachable, "13b: probe fails on the first refresh: no connection");
            }
            finally
            {
                cable.Set();
                PublishedStatus.NetShare = (Func<string, string, int[]>)saved[0];
                PublishedStatus.Queue = (Func<string, PublishedStatus.QueueFacts>)saved[1];
                PublishedStatus.ReadValue = (Func<string, string>)saved[2];
                PublishedStatus.UtcNow = (Func<DateTime>)saved[3];
                PublishedStatus.ShareDeadline = (TimeSpan)saved[4];
                PublishedStatus.HostAnswers = (Func<string, bool>)saved[5];
                PublishedStatus.ForgetShareChecks();
            }
        }

        static void ShareMatrix()
        {
            const string Port = @"\\PRINTHOST\QL-800", Printer = "Brother QL-800 (Unidrv)";
            var h = new FakeHost();
            var clock = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
            var saved = new object[] { PublishedStatus.NetShare, PublishedStatus.Queue, PublishedStatus.ReadValue, PublishedStatus.UtcNow, PublishedStatus.HostAnswers };
            PublishedStatus.HostAnswers = x => h.Reachable;
            PublishedStatus.NetShare = h.NetShare;
            PublishedStatus.Queue = h.Queue;
            PublishedStatus.ReadValue = h.Read;
            PublishedStatus.UtcNow = () => clock;
            PublishedStatus.ForgetShareChecks();
            Func<string, string> poll = reply =>   // the host's service: polled just now, the printer's reply
            {
                var p = new Published { Poll = clock.AddSeconds(-2), State = reply == null ? "NotConnected" : "Open", RequestSent = true, Reply = reply != null };
                if (reply != null) p.Entries.Add(new PublishedEntry { Status = Msg(reply, clock.AddSeconds(-1).ToLocalTime()), IsReply = true });
                return PublishedStatus.Format(p);
            };
            Func<int, PrinterCheck> ask = jobs =>
            {
                var d = PublishedStatus.QueryShare(Printer, Port, "PRINTHOST", "QL-800");
                return PrinterCheck.From(Printer, d, Enumerable.Range(0, jobs).Select(i => new JobInfo()).ToList(), d.IsPublished ? d.Logged : null);
            };
            Action later = () => clock = clock.AddSeconds(31);   // past the 30 s share check
            Func<PrinterCheck, bool> noRoll = c => c.Media == null && c.LoadedText == null;
            try
            {
                h.Published = poll(Idle);
                var c = ask(0);
                Check(c.Health == Health.Ok && c.MediaIsCurrent && c.Via != null && !c.ShareProblem, "share 1: ready with the roll, via the host");
                int calls = h.NetCalls;
                ask(0); ask(0);
                Check(h.NetCalls == calls, "share: checked at most every 30 s");

                h.Published = poll(null);
                c = ask(0);
                Check(c.Health == Health.Warning && c.Headline == "The printer is not connected" && c.Line.Contains("PRINTHOST"), "share 2: printer off, via the host");
                h.Published = poll(Idle);
                Check(ask(0).Health == Health.Ok, "share 3: plugged in again: ready with the next poll");
                h.Published = poll(WrongMedia);
                Check(ask(0).Health == Health.Error, "share 4: the printer's error");
                h.Published = poll(Idle);

                h.Shared = false;   // "Stop sharing": SMB share gone, the spooler still opens the old name, not SHARED
                later();
                c = ask(0);
                Check(c.Device.State == DeviceState.NotShared && c.Health == Health.Warning && noRoll(c) && c.ShareProblem && c.OfferRemove &&
                      c.Line == "Not shared – PRINTHOST no longer shares 'QL-800'" && c.Detail.Contains("stay in this PC's queue") && c.Via == null,
                      "share 5: stop sharing: not shared, no roll, offers removal (" + c.Line + ")");
                Check(!c.Advice.Any(a => a.Contains("Remove")), "share 5: the removal is the card's link, not in a test print's question");
                Lang.Force("de");
                c = ask(0);
                Check(c.Line == "Nicht freigegeben – PRINTHOST gibt „QL-800“ nicht mehr frei", "share 5: German line (" + c.Line + ")");
                Lang.Force("en");
                // The spooler alone would still say "shared": the SMB share decides.
                h.Shared = true; h.ShareName = "QL-800"; later();
                PublishedStatus.NetShare = (x, y) => new[] { 2310, 0 };
                Check(ask(0).Device.State == DeviceState.NotShared, "share 5: SMB share gone while the queue says shared");
                PublishedStatus.NetShare = h.NetShare;
                later();
                Check(ask(0).Health == Health.Ok, "share 6: shared again: ready within 30 s");

                h.ShareName = "QL-800B";   // renamed on the host
                later();
                c = ask(0);
                Check(c.Device.State == DeviceState.NotShared && c.OfferRemove && c.Advice.Any(a => a.Contains("Another PC's printer")),
                      "share 7: renamed: the old printer not shared, hint to add the new name");
                h.ShareName = "QL-800";

                h.PrinterExists = false;   // removed on the host
                later();
                Check(ask(0).Device.State == DeviceState.NotShared, "share 8: removed on the host: not shared");
                h.PrinterExists = true; later(); h.Published = poll(Idle);   // the host's service keeps polling (stale after 3 min)
                Check(ask(0).Health == Health.Ok, "share 9: removed, added and shared again: ready");

                clock = clock.AddMinutes(6);   // the host's service stopped: no new poll
                c = ask(0);
                Check(c.Device.State == DeviceState.NotPublished && c.Device.Stale && c.Health == Health.Info && noRoll(c) && !c.ShareProblem &&
                      c.Headline == "No current printer status from PRINTHOST" && c.Detail.StartsWith("Last status from"),
                      "share 10: service stopped: no current status (grey), no roll, no notification");
                h.Published = poll(Idle);

                h.SpoolerUp = false; later();   // its print shares and queues are gone while it restarts
                Check(ask(0).Device.State == DeviceState.NotPublished, "share 11: one missed refresh keeps the last answer (service stopped, case 10)");
                c = ask(0);
                Check(c.Device.State == DeviceState.Unreachable && c.ShareProblem, "share 11: host spooler restarting: not reachable, not 'not shared'");
                h.SpoolerUp = true; later();
                Check(ask(0).Health == Health.Ok, "share 11: spooler back: ready");

                h.Reachable = false; later();   // host off, or this PC's network gone
                Check(ask(0).Health == Health.Ok, "share 12: one missed refresh keeps the last answer");
                c = ask(1);
                Check(c.Device.State == DeviceState.Unreachable && c.Health == Health.Warning && noRoll(c) && c.ShareProblem && !c.OfferRemove &&
                      c.Line == "No connection – PRINTHOST doesn't answer" && c.Detail.Contains("waits on this PC until PRINTHOST can be reached") &&
                      c.Advice.Any(a => a.Contains("1 print job is waiting")), "share 12/13: host or network gone: not reachable (" + c.Line + ")");
                // 14: this PC started first: not reachable, then ready once the host is up, no restart.
                // Off the network, seen 2026-11-07: NetShareGetInfo still 0 (cached), OpenPrinter 1801: not "not shared".
                PublishedStatus.NetShare = (x, y) => new[] { 0, 1 };
                PublishedStatus.Queue = q => new PublishedStatus.QueueFacts { Error = 1801 };
                later();
                Check(ask(0).Device.State == DeviceState.Unreachable, "share 13: 1801 while the host doesn't answer: not reachable, not 'not shared'");
                PublishedStatus.NetShare = h.NetShare;
                PublishedStatus.Queue = h.Queue;
                h.Reachable = true; later();
                Check(ask(0).Health == Health.Ok, "share 14: host up: ready without a restart");

                h.Published = poll(null);
                c = ask(1);
                Check(c.Health == Health.Warning && c.Advice.Any(a => a.Contains("will print when the printer is back")), "share 15: job while the printer is off: waits, said so");
                h.Published = poll(Idle);

                // 17: Brother's own queue on the same share isn't ours: no card, never touched.
                Check(QlModel.ForDriver("Brother QL-800") == null, "share 17: Brother's driver has no card");

                h.Pending = true; later();   // removed on the host, pending deletion (",0")
                Check(ask(0).Device.State == DeviceState.NotShared, "share 20: pending deletion on the host: not shared");
                h.Pending = false; later();

                PublishedStatus.NetShare = (x, y) => new[] { 5, 0 };   // can't be checked (access denied): no status, never the roll
                later();
                c = ask(0);
                Check(c.Device.State == DeviceState.NotPublished && c.Health == Health.Info && noRoll(c) && c.Advice.Any(a => a.Contains("can't be asked")),
                      "share: can't be checked: no status, no roll");
                PublishedStatus.NetShare = h.NetShare;
                later();
                h.Shared = true; c = ask(0);
                // Removed between the check and the read: not shared at once, checked again next time.
                h.PrinterExists = false;
                c = ask(0);
                Check(c.Device.State == DeviceState.NotShared, "share: gone between the check and the read");
            }
            finally
            {
                PublishedStatus.NetShare = (Func<string, string, int[]>)saved[0];
                PublishedStatus.Queue = (Func<string, PublishedStatus.QueueFacts>)saved[1];
                PublishedStatus.ReadValue = (Func<string, string>)saved[2];
                PublishedStatus.UtcNow = (Func<DateTime>)saved[3];
                PublishedStatus.HostAnswers = (Func<string, bool>)saved[4];
                PublishedStatus.ForgetShareChecks();
                Lang.Force("en");
            }
        }

        static void SnmpEncoding()
        {
            byte[] oid = { 0x2B, 0x06, 0x01, 0x04, 0x01, 0x93, 0x03, 0x03, 0x03, 0x09, 0x01, 0x06, 0x01, 0x00 };
            var get = Snmp.BuildGet("public", NetworkStatus.StatusOid, 0x1234);
            var expected = new byte[] { 0x30, 0x2D, 0x02, 0x01, 0x00, 0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63,
                                        0xA0, 0x20, 0x02, 0x02, 0x12, 0x34, 0x02, 0x01, 0x00, 0x02, 0x01, 0x00,
                                        0x30, 0x14, 0x30, 0x12, 0x06, 0x0E }.Concat(oid).Concat(new byte[] { 0x05, 0x00 }).ToArray();
            Check(get.SequenceEqual(expected), "snmp: GetRequest bytes");
            Check(Snmp.BuildGet("public", "1.3.6", 0x80)[16] == 0x02 && Snmp.BuildGet("public", "1.3.6", 0x80)[17] == 0x00, "snmp: positive id with the top bit set");

            Func<int, byte[], byte[]> response = (status, value) =>
            {
                var bind = new byte[] { 0x06, 0x0E }.Concat(oid).Concat(value).ToArray();
                var list = new byte[] { 0x30, (byte)(bind.Length + 2), 0x30, (byte)bind.Length }.Concat(bind).ToArray();
                var pdu = new byte[] { 0x02, 0x02, 0x12, 0x34, 0x02, 0x01, (byte)status, 0x02, 0x01, 0x00 }.Concat(list).ToArray();
                var body = new byte[] { 0x02, 0x01, 0x00, 0x04, 0x06, 0x70, 0x75, 0x62, 0x6C, 0x69, 0x63, 0xA2, (byte)pdu.Length }.Concat(pdu).ToArray();
                return new byte[] { 0x30, 0x81, (byte)body.Length }.Concat(body).ToArray();  // long-form length on purpose
            };
            var idle = Msg(Idle).Raw;
            int id;
            byte[] got;
            string error;
            Check(Snmp.ParseResponse(response(0, new byte[] { 0x04, 0x20 }.Concat(idle).ToArray()), out id, out got, out error) &&
                  id == 0x1234 && got.SequenceEqual(idle) && error == null && QlStatus.TryParse(got, got.Length, DateTime.Now).LoadedText == "62 mm continuous",
                  "snmp: status reply");
            Check(!Snmp.ParseResponse(response(2, new byte[] { 0x05, 0x00 }), out id, out got, out error) && id == 0x1234 && error.Contains("noSuchName"), "snmp: noSuchName");
            Check(!Snmp.ParseResponse(response(0, new byte[] { 0x02, 0x01, 0x05 }), out id, out got, out error) && error.Contains("value type 0x02"), "snmp: not an OCTET STRING");
            Check(!Snmp.ParseResponse(new byte[] { 0x30, 0x05, 0x02 }, out id, out got, out error), "snmp: truncated");

            // Printer details: any value type (a Counter32 3878 as prtMarkerLifeCount), Brother's counter block as a
            // real QL-720NW sent it (network-status.md §9: 3970 cuts, 3878 pages, 156 m).
            byte tag;
            Check(Snmp.ParseAny(response(0, new byte[] { 0x41, 0x02, 0x0F, 0x26 }), true, out id, out tag, out got, out error) && tag == 0x41 &&
                  Snmp.ToNumber(got, tag) == 3878, "snmp: Counter32 value");
            Check(Snmp.ToNumber(new byte[] { 0xFF, 0xFE }, 0x02) == -2 && Snmp.ToNumber(new byte[] { 0x00, 0x80, 0x00, 0x00, 0x00 }, 0x41) == 0x80000000L &&
                  Snmp.ToNumber(new byte[] { 1 }, 0x04) == null, "snmp: INTEGER signed, Counter32 unsigned, others none");
            var block = new byte[] { 0xD0, 0x01, 0x04, 0x00, 0x00, 0x0F, 0x82, 0x00, 0x01, 0x04, 0x00, 0x00, 0x0F, 0x26, 0x60, 0x01, 0x04, 0x00, 0x00, 0x00, 0x9C,
                                     0x12, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0x16, 0x00, 0x04, 0x00, 0x00, 0x00, 0x00, 0xFF };
            var counters = NetworkStatus.ParseCounters(block);
            Check(counters.Count == 5 && counters[NetworkStatus.CutsId] == 3970 && counters[NetworkStatus.PagesId] == 3878 && counters[NetworkStatus.MetresId] == 156,
                  "snmp: Brother counter block");
            Check(NetworkStatus.ParseCounters(block.Take(10).ToArray()).Count == 1, "snmp: counter block cut short keeps whole records");
        }

        static string[] Labels(int n) { return Enumerable.Range(0, n).Select(i => "L" + i).ToArray(); }

        static void PaperNumber256()
        {
            // 254 forms: nothing to do.
            var db = new FakeForms(118, 115, Labels(21));
            Check(Sync.KeepNumber256Free(db, Labels(21)).Count == 0 && db.Forms.Count == 254, "256: below 255 untouched");
            // 255 forms (the ARM64 VM after 0.15): the placeholder takes number 256.
            db = new FakeForms(118, 116, Labels(21));
            Sync.KeepNumber256Free(db, Labels(21));
            Check(db.At256 == Sync.ReservedForm && db.Forms.Count == 256, "256: placeholder added at 255 forms");
            // A label format at 256: it moves behind the placeholder.
            db = new FakeForms(118, 116, Labels(23));
            var log = Sync.KeepNumber256Free(db, Labels(23));
            Check(db.At256 == Sync.ReservedForm && db.Forms.Count == 258 && db.Forms[256].Name == "L21" && db.Forms[257].Name == "L22" &&
                  log.Count == 1 && !log[0].StartsWith("WARNING"), "256: own label format moved");
            // A driver paper at 256 (the x64 PC) and one after it: two label formats before them move, both papers
            // move below 256.
            db = new FakeForms(118, 116, Labels(21));
            db.Forms.Add(new FormInfo { Name = "DT 102mm x 152mm", Flags = FormsDb.FORM_PRINTER });
            db.Forms.Add(new FormInfo { Name = "P-late", Flags = FormsDb.FORM_PRINTER });
            Check(db.At256 == "DT 102mm x 152mm", "256: setup");
            Sync.KeepNumber256Free(db, Labels(21));
            Check(db.At256 == Sync.ReservedForm && db.Forms[253].Name == "DT 102mm x 152mm" && db.Forms[254].Name == "P-late" &&
                  db.Forms.Count == 258 &&
                  db.Forms.Count(f => f.Name.StartsWith("L")) == 21, "256: driver paper moved below 256");
            // A driver paper at 256 and no label formats of ours: only a warning.
            db = new FakeForms(118, 140);
            log = Sync.KeepNumber256Free(db, new string[0]);
            Check(db.Forms.Count == 258 && log.Count == 1 && log[0].StartsWith("WARNING") && log[0].Contains("'P137'"), "256: warning");
            // The placeholder at the wrong place (forms deleted before it) is moved back to 256.
            db = new FakeForms(118, 116, Labels(23));
            Sync.KeepNumber256Free(db, Labels(23));
            db.Delete("L0");
            Sync.KeepNumber256Free(db, Labels(23).Skip(1).ToArray());
            Check(db.At256 == Sync.ReservedForm, "256: placeholder kept at 256 after a deletion");
            // Running twice changes nothing.
            var before = db.Forms.Select(f => f.Name).ToList();
            Check(Sync.KeepNumber256Free(db, Labels(23).Skip(1).ToArray()).Count == 0 && before.SequenceEqual(db.Forms.Select(f => f.Name)),
                  "256: idempotent");
        }

        static void Decoding()
        {
            var idle = Msg(Idle);
            Check(idle.ModelName == "QL-800", "model");
            // Model byte 4 per Brother's raster command reference: '9' = QL-810W, 'A' = QL-820NWB.
            Check(Msg(Idle.Substring(0, 12) + "39" + Idle.Substring(14)).ModelName == "QL-810W", "model QL-810W");
            Check(Msg(Idle.Substring(0, 12) + "41" + Idle.Substring(14)).ModelName == "QL-820NWB", "model QL-820NWB");
            Check(Msg(QL570Idle).ModelName == "QL-570" && Msg(QL570Idle).LoadedText == "62 mm continuous" && !Msg(QL570Idle).HasError, "QL-570 idle reply");
            // Model table (generated from driver/gen/media.py): driver names and per-model papers
            Check(QlModel.ForDriver("Brother QL-570 (Unidrv)").Name == "QL-570", "model for driver name");
            Check(QlModel.DriverNames.Length == QlModel.AllNames.Split('/').Length && QlModel.DriverNames.Contains("Brother QL-810W (Unidrv)"), "driver names");
            Check(QlModel.ForDriver("Brother QL-700") == null, "foreign driver");
            Check(QlModel.AllNames == "QL-500/550/560/570/580N/600/650TD/700/710W/720NW/800/810W/820NWB/1050/1060N/1100/1110NWB/1115NWB", "model list text");
            // QL-1050/1060N (0.13): status codes from Brother's .ini ("P", "4"), 102 / 103 mm papers, 25 mm minimum
            Check(Msg(QL570Idle.Substring(0, 12) + "50" + QL570Idle.Substring(14)).ModelName == "QL-1050" &&
                  Msg(QL570Idle.Substring(0, 12) + "34" + QL570Idle.Substring(14)).ModelName == "QL-1060N", "models QL-1050/1060N");
            Check(QlModel.ForDriver("Brother QL-1050 (Unidrv)").HasPaper(260) && QlModel.ForDriver("Brother QL-1060N (Unidrv)").HasPaper(366) &&
                  !QlModel.ForDriver("Brother QL-800 (Unidrv)").HasPaper(260) && QlModel.ForDriver("Brother QL-1050 (Unidrv)").MinLength01 == 250,
                  "QL-1050/1060N papers and minimum length");
            // QL-580N (0.14): status code from Brother's .ini ("3"), the QL-570's papers
            Check(Msg(QL570Idle.Substring(0, 12) + "33" + QL570Idle.Substring(14)).ModelName == "QL-580N" &&
                  QlModel.ForDriver("Brother QL-580N (Unidrv)").HasPaper(271) && !QlModel.ForDriver("Brother QL-580N (Unidrv)").HasPaper(358),
                  "model QL-580N");
            // QL-700/710W/720NW (0.12): status codes from Brother's .ini, the QL-800's papers minus 382, 388, 389
            Check(Msg(Idle.Substring(0, 12) + "35" + Idle.Substring(14)).ModelName == "QL-700" &&
                  Msg(Idle.Substring(0, 12) + "36" + Idle.Substring(14)).ModelName == "QL-710W" &&
                  Msg(Idle.Substring(0, 12) + "37" + Idle.Substring(14)).ModelName == "QL-720NW", "models QL-700/710W/720NW");
            Check(QlModel.ForDriver("Brother QL-720NW (Unidrv)").HasPaper(358) && !QlModel.ForDriver("Brother QL-700 (Unidrv)").HasPaper(382) &&
                  QlModel.ForDriver("Brother QL-710W (Unidrv)").MinLength01 == 127, "QL-7x0 papers and minimum length");
            // QL-560/550/500/650TD (0.11): status codes from Brother's .ini; the QL-500 and QL-550 share one
            Check(Msg(QL570Idle.Substring(0, 12) + "31" + QL570Idle.Substring(14)).ModelName == "QL-560", "model QL-560");
            Check(Msg(Idle.Substring(0, 9) + "30 4F" + Idle.Substring(14)).ModelName == "QL-500/550", "model QL-500/550");
            Check(Msg(Idle.Substring(0, 9) + "30 51" + Idle.Substring(14)).ModelName == "QL-650TD", "model QL-650TD");
            Check(QlModel.ForDriver("Brother QL-650TD (Unidrv)").MinLength01 == 250 && QlModel.ForDriver("Brother QL-570 (Unidrv)").MinLength01 == 127 &&
                  QlModel.ForDriver("Brother QL-500 (Unidrv)").HasPaper(271), "older models: 25 mm minimum, QL-570 papers");
            Check(QlModel.ForDriver("Brother QL-800 (Unidrv)").HasPaper(388) && !QlModel.ForDriver("Brother QL-810W (Unidrv)").HasPaper(388), "62x60 on QL-800, not QL-810W");
            Check(!QlModel.ForDriver("Brother QL-570 (Unidrv)").HasPaper(358) && QlModel.ForDriver("Brother QL-570 (Unidrv)").HasPaper(259), "QL-570 papers");
            Check(Msg(BlackRedIdle).BlackRed && !idle.BlackRed && Msg(BlackRedIdle).LoadedText == "62 mm continuous black/red", "red/black roll");
            Check(Msg(BlackRedIdle).Fits(MediaTable.ById(259)) == true, "62 mm formats fit the red/black roll");
            Check(QlModel.ForDriver("Brother QL-800 (Unidrv)").TwoColor && !QlModel.ForDriver("Brother QL-570 (Unidrv)").TwoColor, "two-colour models");
            Check(Msg(RedJobOnBlackRoll).HasError && Msg(RedJobOnBlackRoll).Problems().Count == 1 && Msg(RedJobOnBlackRoll).Problems()[0].Headline == "Black/red job, black roll", "black/red job on a black roll");
            Check(idle.Type == QlStatus.TypeReply && !idle.HasError, "idle reply without error");
            Check(idle.Continuous && idle.WidthMm == 62, "62 mm continuous");
            Check(idle.LoadedMedia != null && idle.LoadedMedia.Id == 259, "loaded media 259");
            Check(idle.LoadedText == "62 mm continuous", "loaded text: " + idle.LoadedText);
            Check(idle.Fits(MediaTable.ById(259)) == true, "62 mm format fits");
            Check(idle.Fits(MediaTable.ById(258)) == false, "29 mm continuous doesn't fit");
            Check(idle.Fits(MediaTable.ById(271)) == false, "29x90 die-cut doesn't fit");
            Check(idle.Fits(MediaTable.ById(274)) == false, "62x29 die-cut doesn't fit continuous");

            Check(Msg(Printing).TypeText == "printing", "phase change = printing");

            var wrong = Msg(WrongMedia);
            Check(wrong.HasError && wrong.Type == QlStatus.TypeError, "wrong media is an error");
            var p = wrong.Problems();
            Check(p.Count == 1 && p[0].Title.StartsWith("Wrong media") && p[0].Title.Contains("62 mm continuous"), "wrong media text: " + (p.Count > 0 ? p[0].Title : "-"));
            Check(p.Count == 1 && p[0].Fix.Contains("cover"), "wrong media fix mentions the cover");

            var cover = Msg(AfterCover);
            Check(cover.HasError && cover.Problems().Count == 1 && cover.Problems()[0].Title.Contains("code 20 00"), "unknown error bits reported with code");

            var die = Msg(DieCut29x90);
            Check(die.DieCut && die.LoadedMedia != null && die.LoadedMedia.Id == 271, "29x90 die-cut = 271");
            Check(die.Fits(MediaTable.ById(271)) == true && die.Fits(MediaTable.ById(272)) == false && die.Fits(MediaTable.ById(258)) == false, "29x90 fits only itself");
            Check(Msg(DieCut62x29).LoadedMedia.Id == 274, "62x29 die-cut = 274");
            Check(Msg(Round12).LoadedMedia.Id == 362, "12 mm round = 362");

            var none = Msg(NoMediaError);
            Check(none.NoMedia && none.LoadedText == "no roll" && none.Problems()[0].Title.StartsWith("No media"), "no media");
            Check(none.Fits(MediaTable.ById(259)) == false, "nothing fits without a roll");

            Check(QlStatus.FromHex("00 20 42", DateTime.Now) == null, "short message rejected");
            Check(QlStatus.FromHex(Idle.Replace("80 20", "81 20"), DateTime.Now) == null, "wrong header rejected");
            Check(QlStatus.FromHex("zz", DateTime.Now) == null, "garbage rejected");
            Check(Msg(Idle).Hex == Idle, "hex round trip");
        }

        static DeviceResult Open(QlStatus reply, bool requestSent, params QlStatus[] messages)
        {
            var d = new DeviceResult { Printer = "P", Port = "USB001", State = DeviceState.Open, Reply = reply, RequestSent = requestSent };
            d.Messages.AddRange(messages);
            return d;
        }

        static List<JobInfo> Jobs(int ageSeconds)
        {
            return new List<JobInfo> { new JobInfo { Id = 7, Document = "Label", Status = Spooler.JOB_STATUS_PRINTING, Submitted = DateTime.Now.AddSeconds(-ageSeconds) } };
        }

        static void Diagnosis()
        {
            var none = new List<JobInfo>();

            var ok = PrinterCheck.From("P", Open(Msg(Idle), true), none, Msg(Idle));
            Check(ok.Health == Health.Ok && ok.Short == "Ready, 62 mm continuous loaded", "ready: " + ok.Short);
            Check(ok.LoadedText == "62 mm continuous", "current media without 'last reported'");

            var cleared = PrinterCheck.From("P", Open(Msg(Idle), true, Msg(WrongMedia)), none, Msg(Idle));
            Check(cleared.Health == Health.Ok && cleared.Advice.Any(a => a.Contains("cleared")), "earlier error reported as cleared");

            var err = PrinterCheck.From("P", Open(null, true, Msg(WrongMedia)), none, Msg(WrongMedia));
            Check(err.Health == Health.Error && err.Headline == "Wrong media" && err.Detail.Contains("62 mm continuous"), "error state without answer: " + err.Headline);
            Check(err.Advice.Any(a => a.Contains("doesn't answer")) && err.Advice.Any(a => a.Contains("cover")), "error advice");

            var errJobs = PrinterCheck.From("P", Open(null, false), Jobs(5), Msg(WrongMedia));
            Check(errJobs.Health == Health.Error && errJobs.Advice[0].Contains("cancel them first"), "waiting jobs during an error");

            var stuck = PrinterCheck.From("P", Open(null, false), Jobs(60), Msg(Printing));
            Check(stuck.Health == Health.Warning && stuck.Headline.Contains("isn't accepting"), "stuck job: " + stuck.Headline);

            var busy = PrinterCheck.From("P", Open(null, false), Jobs(2), Msg(Printing));
            Check(busy.Health == Health.Busy && busy.Headline == "Printing", "printing: " + busy.Headline);

            var old = PrinterCheck.From("P", Open(null, true), none, Msg(WrongMedia, DateTime.Now.AddDays(-1)));
            Check(old.Health == Health.Warning && old.Headline == "The printer doesn't answer", "old error forgotten: " + old.Headline);
            Check(old.LoadedText != null && old.LoadedText.Contains("last reported"), "stale media marked");

            var gone = PrinterCheck.From("P", new DeviceResult { Printer = "P", Port = "USB001", State = DeviceState.NotConnected }, Jobs(5), null);
            Check(gone.Health == Health.Warning && gone.Advice.Any(a => a.Contains("Editor Lite")) && gone.Advice.Any(a => a.Contains("waiting")), "not connected");

            var file = PrinterCheck.From("P", new DeviceResult { Printer = "P", Port = @"C:\x.prn", State = DeviceState.NotUsb }, none, null);
            Check(file.Health == Health.Info, "file port");
            var share = PrinterCheck.From("P", new DeviceResult { Printer = "P", Port = @"\\host\QL-800", State = DeviceState.NotUsb }, none, null);
            Check(share.Health == Health.Info && share.Advice.Any(a => a.StartsWith("Printers on a network share have no status")), "share port");

            var noRoll = PrinterCheck.From("P", Open(Msg(NoMediaError), true), none, null);
            Check(noRoll.Health == Health.Error && noRoll.Headline == "No roll" && noRoll.Detail.StartsWith("No media"), "no media reply: " + noRoll.Headline);

            // Windows' "Use Printer Offline": wins over a ready printer, the printer's state stays as the last line.
            var offline = PrinterCheck.From("P", Open(Msg(Idle), true), none, Msg(Idle));
            offline.SetWorkOffline();
            Check(offline.WorkOffline && offline.Health == Health.Warning && offline.Headline == "'Use Printer Offline' is on" &&
                  offline.Advice.Count == 2 && offline.Advice[0].Contains("Use printer online") && offline.Advice[1] == "The printer itself: Ready, 62 mm continuous loaded",
                  "offline, printer ready: " + offline.Headline);
            var offlineJobs = PrinterCheck.From("P", Open(null, false), Jobs(60), Msg(Printing));
            offlineJobs.SetWorkOffline();
            Check(offlineJobs.Health == Health.Error && offlineJobs.Detail.StartsWith("1 print job is waiting on this PC") &&
                  offlineJobs.Advice[1].Contains("isn't accepting"), "offline with a waiting job: " + offlineJobs.Detail);
            var offlineShare = PrinterCheck.From("P", new DeviceResult { Printer = "P", Port = @"\\host\QL-570", State = DeviceState.NotUsb }, none, null);
            offlineShare.SetWorkOffline();
            Check(offlineShare.Health == Health.Warning && offlineShare.Advice.Count == 1, "offline without a printer status");

            // Offline only when it is the cause (2026-11-07): Windows sets it by itself for a switched-off USB printer.
            Check(!PrinterCheck.OfflineIsCause(new DeviceResult { State = DeviceState.NotConnected }) &&
                  !PrinterCheck.OfflineIsCause(new DeviceResult { State = DeviceState.Unreachable }) &&
                  !PrinterCheck.OfflineIsCause(new DeviceResult { State = DeviceState.NotShared }) &&
                  PrinterCheck.OfflineIsCause(Open(Msg(Idle), true)) && PrinterCheck.OfflineIsCause(new DeviceResult { State = DeviceState.NotUsb }),
                  "offline: the cause only while the printer is there");
            var off = PrinterCheck.From("P", new DeviceResult { Printer = "P", Port = "USB001", State = DeviceState.NotConnected }, none, null);
            Check(off.Short == "not connected" && off.Line == "Not connected – printer off or USB cable" && !off.WorkOffline, "switched off: not connected (" + off.Short + ")");
            Check(offline.Short == "offline – Windows holds print jobs back", "offline: status bar text");

            // The status window's one line: kind – cause for errors and warnings, as before otherwise.
            Check(err.Line == "Wrong media – the roll doesn't fit the job" && err.DetailIsTitle, "error line: " + err.Line);
            Check(noRoll.Line == "No roll – missing or not inserted correctly", "no roll line: " + noRoll.Line);
            Check(offline.Line == "Offline – Windows holds print jobs back" && !offline.DetailIsTitle, "offline line: " + offline.Line);
            Check(stuck.Line == "Job stuck – the printer isn't accepting it" && old.Line == "No answer – maybe an error state (red LED)" &&
                  gone.Line == "Not connected – printer off or USB cable", "warning lines");
            Check(ok.Line == ok.Short && busy.Line == "Printing" && file.Line == file.Headline, "other states keep their text");
            foreach (var p in Msg(WrongMedia).Problems().Concat(Msg(NoMediaError).Problems()).Concat(Msg(RedJobOnBlackRoll).Problems()))
                Check(p.Cause != null && p.Line.Length <= 50, "short problem line: " + p.Line);
            Lang.Force("de");
            var de = PrinterCheck.From("P", Open(null, true, Msg(WrongMedia)), none, Msg(WrongMedia));
            Check(de.Line == "Falsches Medium – Rolle passt nicht zum Auftrag", "German line: " + de.Line);
            Lang.Force("en");

        }
    }
}
