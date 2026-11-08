// QlDevice, part 2: when a status request is safe, and what one unanswered request means (the second-miss rule,
// the "printing" guard with the print queue's job counter). Part 1 (QlDevice.cs): the USB read.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace QlLabels
{
    static partial class QlDevice
    {
        // A request without an answer means "maybe an error state (red LED)", but one such poll also happens right before
        // the printer is switched off or unplugged (seen 2026-11-07: "No answer" for one poll, then "Not connected"). So
        // only the second miss in a row says so; the first counts as a poll without a request.
        static readonly Dictionary<string, int> misses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        internal static void OnlySecondMiss(DeviceResult r, string key)
        {
            if (key == null) return;
            bool miss = r.State == DeviceState.Open && (r.RequestSent || r.RequestBlocked) && r.Reply == null;
            lock (misses)
            {
                int n;
                misses.TryGetValue(key, out n);
                misses[key] = miss ? n + 1 : 0;
                if (miss && n == 0) r.RequestSent = r.RequestBlocked = false;
            }
        }

        internal static void ForgetMisses() { lock (misses) misses.Clear(); }

        // ---------- "printing": when a status request is safe again ----------
        // The printer says "printing" at the start of a label and "printing completed" / "ready to receive" at its end.
        // Until the end, three bytes of ours could land in the middle of data that doesn't come through the Windows queue
        // (a test stream sent directly): no request for 2 minutes after a "printing". The end messages can go missing
        // (2026-11-07, a job from another PC through the share: both PCs showed "printing" for 2 minutes). When the
        // printer's Windows queue has just printed a job (Windows' counter "Print Queue\Total Jobs Printed" grew), all its
        // data has been sent: a request 10 s later can't hit the middle of a label (long labels, cut at end and several
        // copies are one job, counted when its last byte was sent).
        public static readonly TimeSpan PrintingGuard = TimeSpan.FromMinutes(2), QueueSettle = TimeSpan.FromSeconds(10);

        // Still printing (no request): the newest message says "printing" and is under 2 minutes old, unless the
        // printer's queue printed a job around that time (queueDone: when this process saw the counter grow) at least
        // 10 s ago.
        internal static bool StillPrinting(QlStatus newest, DateTime now, DateTime? queueDone)
        {
            if (newest == null || newest.Type != QlStatus.TypePhase || newest.Phase != 1 || now - newest.Time >= PrintingGuard) return false;
            return !(queueDone.HasValue && queueDone.Value >= newest.Time - TimeSpan.FromSeconds(30) && now - queueDone.Value >= QueueSettle);
        }

        // Windows' count of jobs the queue printed since the spooler started (-1: can't be read); tests replace it.
        internal static Func<string, long> JobsPrinted = ReadJobsPrinted;
        static readonly Dictionary<string, System.Diagnostics.PerformanceCounter> jobCounters = new Dictionary<string, System.Diagnostics.PerformanceCounter>(StringComparer.OrdinalIgnoreCase);
        sealed class QueueSeen { public long Count; public DateTime? Done; }
        static readonly Dictionary<string, QueueSeen> queueSeen = new Dictionary<string, QueueSeen>(StringComparer.OrdinalIgnoreCase);

        static long ReadJobsPrinted(string printer)
        {
            try
            {
                System.Diagnostics.PerformanceCounter c;
                lock (jobCounters)
                    if (!jobCounters.TryGetValue(printer, out c))
                        jobCounters[printer] = c = new System.Diagnostics.PerformanceCounter("Print Queue", "Total Jobs Printed", printer, true);
                return c.RawValue;
            }
            catch (Exception)
            {
                lock (jobCounters) jobCounters.Remove(printer);
                return -1;
            }
        }

        // Called on every read of the printer: when this process last saw the queue's count of printed jobs grow (null:
        // not seen; the first reading only remembers the count).
        internal static DateTime? QueueDone(string printer, DateTime now)
        {
            if (printer == null) return null;
            long count = JobsPrinted(printer);
            lock (queueSeen)
            {
                QueueSeen q;
                if (!queueSeen.TryGetValue(printer, out q)) { queueSeen[printer] = new QueueSeen { Count = count }; return null; }
                if (count >= 0 && count != q.Count) { q.Done = q.Count >= 0 ? now : q.Done; q.Count = count; }
                return q.Done;
            }
        }

        internal static void ForgetQueues() { lock (queueSeen) queueSeen.Clear(); }
    }
}
