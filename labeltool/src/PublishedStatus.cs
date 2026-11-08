// The printer status published by the service "QL printer status" (StatusService.cs) in the print queue's own
// printer data (key QLUnidrv, value Status), so every program and every PC that may print to the queue can read
// it: locally, or through the printer share (\\host\share) a client prints to. Writing it needs administer
// rights on the queue (the service runs as SYSTEM); reading needs only print rights.
//
// Format (REG_SZ, lines; times UTC, "yyyy-MM-ddTHH:mm:ssZ"):
//   QLUnidrv status 1
//   poll TAB <time> TAB <state: Open|NotConnected|Failed|Stopped> TAB <flags: reply,request,blocked,printing> TAB <error text>
//   <time> TAB <32 bytes hex> TAB <reply|message>        up to 16, oldest first
// "poll" is the newest poll that changed something, or the 60 s heartbeat; messages read in that poll have a
// time at or after it.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QlLabels
{
    sealed class PublishedEntry
    {
        public QlStatus Status;
        public bool IsReply;
    }

    sealed class Published
    {
        public DateTime Poll;              // UTC
        public string State = "Failed", Error;
        public bool Reply, RequestSent, RequestBlocked, Printing;
        public readonly List<PublishedEntry> Entries = new List<PublishedEntry>();  // oldest first
    }

    static partial class PublishedStatus
    {
        public const string ServiceName = "QLStatus";
        public const int MaxEntries = 16;
        // Limits for what another PC publishes (its share is a trust boundary, security audit 2026-10-24 M1): the
        // real text is under 2 KB, an error text under 200 characters.
        const int MaxBytes = 16 * 1024, MaxErrorLength = 200;
        static readonly string[] States = { "Open", "NotConnected", "Failed", "Stopped" };
        const string Key = "QLUnidrv", ValueName = "Status", Header = "QLUnidrv status 1", TimeFormat = "yyyy-MM-ddTHH:mm:ssZ";
        public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);
        static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(3);  // the 60 s heartbeat + 2 min for clock differences between PCs
        static readonly TimeSpan FreshReply = TimeSpan.FromMinutes(1);  // a reply this old still counts after a poll without a request

        // "\\host\share" -> host and share, else false.
        public static bool IsShare(string port, out string host, out string share)
        {
            host = share = null;
            var m = port == null ? null : Regex.Match(port, @"^\\\\([^\\]+)\\([^\\]+)$");
            if (m == null || !m.Success) return false;
            host = m.Groups[1].Value;
            share = m.Groups[2].Value;
            return true;
        }

        // The service runs on this PC (checked at most every 10 s).
        static bool serviceRunning;
        static DateTime serviceChecked = DateTime.MinValue;
        internal static void ForgetServiceCheck() { serviceChecked = DateTime.MinValue; }

        public static bool ServiceRunning
        {
            get
            {
                if (DateTime.UtcNow - serviceChecked > TimeSpan.FromSeconds(10))
                {
                    try { using (var sc = new ServiceController(ServiceName)) serviceRunning = sc.Status == ServiceControllerStatus.Running; }
                    catch (Exception) { serviceRunning = false; }  // not installed
                    serviceChecked = DateTime.UtcNow;
                }
                return serviceRunning;
            }
        }

        public static string Format(Published p)
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append("\r\n");
            var flags = new List<string>();
            if (p.Reply) flags.Add("reply");
            if (p.RequestSent) flags.Add("request");
            if (p.RequestBlocked) flags.Add("blocked");
            if (p.Printing) flags.Add("printing");
            sb.Append("poll\t").Append(p.Poll.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append('\t').Append(p.State)
              .Append('\t').Append(string.Join(",", flags)).Append('\t').Append((p.Error ?? "").Replace('\t', ' ').Replace("\r", " ").Replace("\n", " "))
              .Append("\r\n");
            foreach (var e in p.Entries.Skip(Math.Max(0, p.Entries.Count - MaxEntries)))
                sb.Append(e.Status.Time.ToUniversalTime().ToString(TimeFormat, CultureInfo.InvariantCulture)).Append('\t')
                  .Append(e.Status.Hex).Append('\t').Append(e.IsReply ? "reply" : "message").Append("\r\n");
            return sb.ToString();
        }

        // null if the text is not a published status.
        public static Published Parse(string text)
        {
            if (text == null) return null;
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2 || lines[0] != Header) return null;
            var poll = lines[1].Split('\t');
            DateTime t;
            if (poll.Length < 4 || poll[0] != "poll" || !TryTime(poll[1], out t)) return null;
            var p = new Published { Poll = t, State = States.Contains(poll[2]) ? poll[2] : "Failed", Error = poll.Length > 4 ? Clean(poll[4]) : null };
            var flags = poll[3].Split(',');
            p.Reply = flags.Contains("reply");
            p.RequestSent = flags.Contains("request");
            p.RequestBlocked = flags.Contains("blocked");
            p.Printing = flags.Contains("printing");
            foreach (var line in lines.Skip(2).Take(MaxEntries))
            {
                var f = line.Split('\t');
                if (f.Length < 3 || !TryTime(f[0], out t)) continue;
                var s = QlStatus.FromHex(f[1], t.ToLocalTime());
                if (s != null) p.Entries.Add(new PublishedEntry { Status = s, IsReply = f[2] == "reply" });
            }
            return p;
        }

        // Printable characters only, at most MaxErrorLength; null if nothing is left.
        static string Clean(string s)
        {
            var t = new string(s.Where(c => !char.IsControl(c)).Take(MaxErrorLength).ToArray()).Trim();
            return t.Length > 0 ? t : null;
        }

        static bool TryTime(string s, out DateTime t)
        {
            return DateTime.TryParseExact(s, TimeFormat, CultureInfo.InvariantCulture,
                                          DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out t);
        }

        // The published status as the readers see it: a DeviceResult like a direct USB read. queue: the local
        // queue, or \\host\share for a client.
        public static DeviceResult ToDevice(string printer, string port, string queue, Published p, DateTime utcNow)
        {
            var r = new DeviceResult { Printer = printer, Port = port, Source = queue };
            string host, community;
            if (NetworkStatus.TryPort(port, out host, out community)) r.Network = host;  // this PC's network queue: network advice
            if (p == null)
            {
                r.State = DeviceState.NotPublished;
                return r;
            }
            r.PublishedAt = p.Poll.ToLocalTime();
            // A poll time in the future (clock difference, or a host that wants to look current) counts as stale
            // beyond the allowed difference.
            r.Stale = utcNow - p.Poll > StaleAfter || p.Poll - utcNow > StaleAfter || p.State == "Stopped";
            r.State = r.Stale ? DeviceState.NotPublished
                    : p.State == "Open" ? DeviceState.Open
                    : p.State == "NotConnected" ? DeviceState.NotConnected : DeviceState.Failed;
            r.Error = p.Error;
            r.RequestSent = p.RequestSent;
            r.RequestBlocked = p.RequestBlocked;
            r.Printing = p.Printing;
            DateTime pollLocal = p.Poll.ToLocalTime();
            var last = p.Entries.LastOrDefault();
            // The poll got an answer: the newest reply (a repeated answer isn't added again, so it may be older).
            if (p.Reply && last != null && last.IsReply) r.Reply = last.Status;
            // A poll without a request (a job was in the queue) has no answer of its own: the newest entry, if it is
            // a reply under a minute old, still tells the state (else readers showed a bare "Connected" for one poll).
            else if (!p.RequestSent && !p.Printing && last != null && last.IsReply)
            {
                var age = utcNow - last.Status.Time.ToUniversalTime();
                if (age < FreshReply && age > -FreshReply) r.Reply = last.Status;
            }
            // As in a direct read: the messages of the newest poll (the history is in Logged).
            foreach (var e in p.Entries)
                if (e.Status.Time >= pollLocal && !ReferenceEquals(e.Status, r.Reply)) r.Messages.Add(e.Status);
            r.Logged = last != null ? last.Status : null;
            return r;
        }
    }
}
