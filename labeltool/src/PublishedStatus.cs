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

    static class PublishedStatus
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

        // ---------- a client's printer share ----------
        // The status read (spooler RPC to the host's queue) and printing (SMB, the Local Port \\host\share) go different
        // ways: after "Stop sharing" the host's spooler still opens the queue by its old share name, so the status read
        // works while printing fails (seen 2026-11-06). The published status counts only
        // while printing through the share would work: the SMB print share exists, the host's queue is shared under that
        // name and not being deleted. Checked at most every 30 s per share (the cards refresh every 4 s).

        public enum ShareState { Shared, NotShared, Unreachable, Unknown }

        public sealed class QueueFacts
        {
            public int Error;        // OpenPrinter / GetPrinter: 0 = read
            public int Attributes, Status;
            public string ShareName;
        }

        public static readonly TimeSpan ShareCheckEvery = TimeSpan.FromSeconds(30);
        // The readers; tests replace them. NetShare: NetShareGetInfo level 1 -> { error (0 = found), share type }.
        internal static Func<string, string, int[]> NetShare = NetShareInfo;
        internal static Func<string, QueueFacts> Queue = QueueInfo;
        internal static Func<string, bool> HostAnswers = ProbeHost;
        internal static Func<string, string> ReadValue = Read;
        internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;

        sealed class ShareCheck { public DateTime At; public ShareState State; public string Error; }
        static readonly Dictionary<string, ShareCheck> shareChecks = new Dictionary<string, ShareCheck>(StringComparer.OrdinalIgnoreCase);

        // Per share: the last answer that wasn't "not reachable", and how many refreshes in a row didn't reach the host.
        sealed class Seen { public bool Any; public ShareState State; public string Error, Text; public int Misses; }
        static readonly Dictionary<string, Seen> seen = new Dictionary<string, Seen>(StringComparer.OrdinalIgnoreCase);

        internal static void ForgetShareChecks()
        {
            lock (shareChecks) shareChecks.Clear();
            lock (hanging) hanging.Clear();
            lock (seen) seen.Clear();
        }

        const int NERR_NetNameNotFound = 2310, ERROR_INVALID_PRINTER_NAME = 1801, STYPE_PRINTQ = 1;
        const int PRINTER_ATTRIBUTE_SHARED = 0x8, PRINTER_STATUS_PENDING_DELETION = 0x4;
        // The host or the network doesn't answer (not: the share or the queue is gone). Not ERROR_TIMEOUT: slow isn't gone.
        static readonly int[] UnreachableErrors = {
            51, 53, 59, 64, 67, 121, 1203, 1222, 1231, 1232, 1236, 1311, 1722, 1726, 1727, 1753 };

        // A host that is off the network makes NetShareGetInfo, OpenPrinter and GetPrinterDataEx wait for Windows' own
        // timeouts (SMB, RPC: 20-60 s and more; matrix case 13b). So each call runs on a worker with a deadline, and
        // while it still hangs no second one is started (ERROR_TIMEOUT 1460 at once). "Not reachable" comes from the
        // TCP 445 probe only: a running host opened its queue in 0.8-4.9 s on every call (2026-11-07, 0.19.8 with a
        // 5 s deadline gave false "no connection" toasts). A deadline that runs out while the host answers on 445 is
        // "slow": the last answer stands.
        internal static TimeSpan ShareDeadline = TimeSpan.FromSeconds(15);
        const int ERROR_TIMEOUT = 1460;
        static readonly Dictionary<string, Task> hanging = new Dictionary<string, Task>(StringComparer.OrdinalIgnoreCase);

        internal static T WithDeadline<T>(string key, Func<T> call)
        {
            Task<T> t;
            lock (hanging)
            {
                Task old;
                if (hanging.TryGetValue(key, out old))
                {
                    if (!old.IsCompleted) throw new Win32Exception(ERROR_TIMEOUT);
                    hanging.Remove(key);
                }
                t = Task.Run(call);
            }
            bool done;
            try { done = t.Wait(ShareDeadline); }
            catch (AggregateException ex) { throw ex.InnerException; }
            if (!done)
            {
                lock (hanging) hanging[key] = t;
                throw new Win32Exception(ERROR_TIMEOUT);
            }
            return t.Result;
        }

        // Decides from what the two readers found. error: why, when Unknown.
        public static ShareState Classify(string share, int netError, int shareType, QueueFacts queue, out string error)
        {
            error = null;
            // No such share. While the host's spooler is stopped (restarting) its print shares are gone too, and its
            // queues don't answer: that is "not reachable" for a moment, not "no longer shared".
            if (netError == NERR_NetNameNotFound)
                return queue != null && UnreachableErrors.Contains(queue.Error) ? ShareState.Unreachable : ShareState.NotShared;
            if (UnreachableErrors.Contains(netError)) return ShareState.Unreachable;
            if (netError != 0) { error = new Win32Exception(netError).Message; return ShareState.Unknown; }
            if ((shareType & 0xFF) != STYPE_PRINTQ) return ShareState.NotShared;   // a folder of that name, not a printer
            if (queue == null) { error = new Win32Exception(1).Message; return ShareState.Unknown; }
            if (queue.Error == ERROR_INVALID_PRINTER_NAME) return ShareState.NotShared;
            if (UnreachableErrors.Contains(queue.Error)) return ShareState.Unreachable;
            if (queue.Error != 0) { error = new Win32Exception(queue.Error).Message; return ShareState.Unknown; }
            if ((queue.Attributes & PRINTER_ATTRIBUTE_SHARED) == 0 || (queue.Status & PRINTER_STATUS_PENDING_DELETION) != 0 ||
                !string.Equals(queue.ShareName, share, StringComparison.OrdinalIgnoreCase))
                return ShareState.NotShared;
            return ShareState.Shared;
        }

        // The share's state, from the cache while it is under 30 s old ("not reachable" isn't kept: asked again).
        public static ShareState CheckShare(string host, string share, out string error)
        {
            bool slow;
            return CheckShare(host, share, false, out error, out slow);
        }

        // answered: the caller's probe just reached the host. slow: the host answers, the check didn't finish in time
        // (Unknown then; the caller keeps its last answer).
        static ShareState CheckShare(string host, string share, bool answered, out string error, out bool slow)
        {
            slow = false;
            string key = @"\\" + host + @"\" + share;
            var now = UtcNow();
            ShareCheck c;
            lock (shareChecks)
                if (shareChecks.TryGetValue(key, out c) && now - c.At < ShareCheckEvery && c.At - now < ShareCheckEvery)
                {
                    error = c.Error;
                    return c.State;
                }
            int[] net;
            QueueFacts q = null;
            // A host off the network: NetShareGetInfo may still answer 0 (cached) and OpenPrinter 1801, which looked
            // like "not shared" (2026-11-07). So first: does the host answer at all?
            if (!answered && !Answers(host)) net = new[] { 53, 0 };
            else
            {
                try
                {
                    var found = WithDeadline("check " + key, () =>
                    {
                        int[] n;
                        try { n = NetShare(host, share); }
                        catch (Exception) { n = new[] { 1, 0 }; }
                        QueueFacts f = null;
                        if (n[0] == 0 || n[0] == NERR_NetNameNotFound)
                            try { f = Queue(key); }
                            catch (Exception) { f = null; }
                        return Tuple.Create(n, f);
                    });
                    net = found.Item1;
                    q = found.Item2;
                }
                catch (Win32Exception ex)
                {
                    if (ex.NativeErrorCode != ERROR_TIMEOUT) throw;
                    error = ex.Message;
                    slow = true;
                    return ShareState.Unknown;   // not kept: asked again with the next refresh
                }
            }
            var state = Classify(share, net[0], net[1], q, out error);
            if (state != ShareState.Unreachable)
                lock (shareChecks) shareChecks[key] = new ShareCheck { At = now, State = state, Error = error };
            return state;
        }

        static bool Answers(string host)
        {
            try { return HostAnswers(host); }
            catch (Exception) { return false; }
        }

        // The status of a client's queue on the port \\host\share: the published one only while printing through the
        // share would work, else why not (never the old roll). Every refresh probes the host (TCP 445, milliseconds);
        // "not reachable" only after two refreshes in a row (one miss keeps the last answer), and a slow host keeps
        // the last answer too (its poll time still turns it stale after 3 minutes).
        public static DeviceResult QueryShare(string printer, string port, string host, string share)
        {
            string key = @"\\" + host + @"\" + share;
            string error, text = null;
            ShareState state;
            bool slow = false;
            if (!Answers(host))
            {
                state = ShareState.Unreachable;
                error = new Win32Exception(53).Message;
            }
            else
            {
                state = CheckShare(host, share, true, out error, out slow);
                if (state == ShareState.Shared)
                {
                    try { text = WithDeadline("read " + port, () => ReadValue(port)); }
                    catch (Exception ex)
                    {
                        var w = ex as Win32Exception;
                        int code = w != null ? w.NativeErrorCode : 0;
                        error = ex.Message;
                        if (code == ERROR_TIMEOUT) slow = true;
                        else
                        {
                            // Gone or unreachable since the check: check again next time.
                            lock (shareChecks) shareChecks.Remove(key);
                            state = code == ERROR_INVALID_PRINTER_NAME ? ShareState.NotShared
                                  : UnreachableErrors.Contains(code) ? ShareState.Unreachable : ShareState.Unknown;
                        }
                    }
                }
            }
            lock (seen)
            {
                Seen s;
                if (!seen.TryGetValue(key, out s)) seen[key] = s = new Seen();
                if (slow)
                {
                    s.Misses = 0;   // it answered on 445
                    if (s.Any) { state = s.State; error = s.Error; text = s.Text; }
                    else state = ShareState.Unknown;
                }
                else if (state == ShareState.Unreachable)
                {
                    if (++s.Misses < 2 && s.Any) { state = s.State; error = s.Error; text = s.Text; }
                }
                else
                {
                    s.Misses = 0;
                    s.Any = true;
                    s.State = state; s.Error = error; s.Text = text;
                }
            }
            DeviceResult r;
            if (state == ShareState.Shared)
            {
                r = ToDevice(printer, port, port, Parse(text), UtcNow());
                if (r.Stale) { r.Reply = r.Logged = null; r.Messages.Clear(); }   // no current status: no roll either, not even as "last reported"
                return r;
            }
            r = ToDevice(printer, port, port, null, UtcNow());
            r.Error = error;
            if (state == ShareState.NotShared) r.State = DeviceState.NotShared;
            else if (state == ShareState.Unreachable) r.State = DeviceState.Unreachable;
            return r;
        }

        // The host answers on the port printing uses (SMB, TCP 445): connected, or refused by a running host. No answer in
        // 2 s, or a name that doesn't resolve: off the network.
        static bool ProbeHost(string host)
        {
            try
            {
                using (var tcp = new System.Net.Sockets.TcpClient())
                {
                    var connect = tcp.ConnectAsync(host, 445);
                    try
                    {
                        if (!connect.Wait(2000)) return false;
                        return true;
                    }
                    catch (AggregateException ex)
                    {
                        var se = ex.InnerException as System.Net.Sockets.SocketException;
                        return se != null && se.SocketErrorCode == System.Net.Sockets.SocketError.ConnectionRefused;
                    }
                }
            }
            catch (Exception) { return false; }
        }

        static int[] NetShareInfo(string host, string share)
        {
            IntPtr buf;
            int e = NetShareGetInfo(host, share, 1, out buf);
            int type = 0;
            if (e == 0 && buf != IntPtr.Zero) type = Marshal.ReadInt32(buf, IntPtr.Size);   // SHARE_INFO_1: netname, type, remark
            if (buf != IntPtr.Zero) NetApiBufferFree(buf);
            return new[] { e, type };
        }

        static QueueFacts QueueInfo(string queue)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
            if (!OpenPrinterW(queue, out h, ref defaults)) return new QueueFacts { Error = Marshal.GetLastWin32Error() };
            try
            {
                int needed;
                GetPrinterW(h, 2, IntPtr.Zero, 0, out needed);
                if (needed <= 0) return new QueueFacts { Error = Marshal.GetLastWin32Error() };
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetPrinterW(h, 2, buf, needed, out needed)) return new QueueFacts { Error = Marshal.GetLastWin32Error() };
                    // PRINTER_INFO_2: 13 pointers (pServerName, pPrinterName, pShareName, ... pSecurityDescriptor), then
                    // Attributes, Priority, DefaultPriority, StartTime, UntilTime, Status
                    int p = IntPtr.Size;
                    return new QueueFacts
                    {
                        ShareName = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, 2 * p)),
                        Attributes = Marshal.ReadInt32(buf, 13 * p),
                        Status = Marshal.ReadInt32(buf, 13 * p + 5 * 4),
                    };
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { ClosePrinter(h); }
        }

        // ---------- printer data ----------

        // Reads the published status of a queue (local name or \\host\share); throws on access errors.
        public static string Read(string queue)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
            if (!OpenPrinterW(queue, out h, ref defaults)) throw new Win32Exception();
            try { return ReadKey(h, Key); }
            finally { ClosePrinter(h); }
        }

        static string ReadKey(IntPtr h, string key)
        {
            {
                int type, needed;
                int e = GetPrinterDataExW(h, key, ValueName, out type, null, 0, out needed);
                if (e == ERROR_FILE_NOT_FOUND) return null;
                if (e != ERROR_MORE_DATA && e != 0) throw new Win32Exception(e);
                if (needed <= 0 || type != REG_SZ || needed > MaxBytes) return null;
                var buf = new byte[needed];
                e = GetPrinterDataExW(h, key, ValueName, out type, buf, needed, out needed);
                if (e != 0) throw new Win32Exception(e);
                if (type != REG_SZ) return null;
                return Encoding.Unicode.GetString(buf, 0, needed).TrimEnd('\0');
            }
        }

        // Writes the published status of a local queue (the service; needs administer rights on the queue).
        public static void Write(string printer, string text)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
            if (!OpenPrinterW(printer, out h, ref defaults)) throw new Win32Exception();
            try
            {
                var data = Encoding.Unicode.GetBytes(text + "\0");
                int e = SetPrinterDataExW(h, Key, ValueName, REG_SZ, data, data.Length);
                if (e != 0) throw new Win32Exception(e);
            }
            finally { ClosePrinter(h); }
        }

        // Removes it (the service's queue no longer has a USB printer, or the service is removed).
        public static void Delete(string printer)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
            if (!OpenPrinterW(printer, out h, ref defaults)) return;
            try { DeletePrinterKeyW(h, Key); }
            finally { ClosePrinter(h); }
        }

        const int PRINTER_ACCESS_USE = 0x8, PRINTER_ALL_ACCESS = 0xF000C, REG_SZ = 1;
        const int ERROR_FILE_NOT_FOUND = 2, ERROR_MORE_DATA = 234;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PRINTER_DEFAULTS { public IntPtr pDatatype, pDevMode; public int DesiredAccess; }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterW(string name, out IntPtr handle, ref PRINTER_DEFAULTS defaults);
        [DllImport("winspool.drv", SetLastError = true)]
        static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetPrinterW(IntPtr h, int level, IntPtr buf, int size, out int needed);
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetShareGetInfo(string server, string share, int level, out IntPtr buf);
        [DllImport("netapi32.dll")]
        static extern int NetApiBufferFree(IntPtr buf);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int GetPrinterDataExW(IntPtr h, string key, string value, out int type, byte[] data, int size, out int needed);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int SetPrinterDataExW(IntPtr h, string key, string value, int type, byte[] data, int size);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int DeletePrinterKeyW(IntPtr h, string key);
    }
}
