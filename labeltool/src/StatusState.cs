// The status code's process-wide memory (share checks and misses, unanswered requests, the print queues' job counts,
// the service check) and its swappable readers, in one place for the tests: Reset() forgets what was seen, Saved()
// keeps the readers and puts them back when disposed. A missed restore once let the real host probe run against a
// test's fake hosts (setup_test, 0.19.8).
using System;

namespace QlLabels
{
    static class StatusState
    {
        internal static void Reset()
        {
            PublishedStatus.ForgetShareChecks();
            PublishedStatus.ForgetServiceCheck();
            QlDevice.ForgetMisses();
            QlDevice.ForgetQueues();
        }

        // using (StatusState.Saved()) { replace readers ... }: forgets what was seen now and again at the end.
        internal static IDisposable Saved() { return new Readers(); }

        sealed class Readers : IDisposable
        {
            readonly Func<string, string, int[]> netShare = PublishedStatus.NetShare;
            readonly Func<string, PublishedStatus.QueueFacts> queue = PublishedStatus.Queue;
            readonly Func<string, bool> hostAnswers = PublishedStatus.HostAnswers;
            readonly Func<string, string> readValue = PublishedStatus.ReadValue;
            readonly Func<DateTime> utcNow = PublishedStatus.UtcNow;
            readonly TimeSpan shareDeadline = PublishedStatus.ShareDeadline;
            readonly Func<string, long> jobsPrinted = QlDevice.JobsPrinted;

            public Readers() { Reset(); }

            public void Dispose()
            {
                PublishedStatus.NetShare = netShare;
                PublishedStatus.Queue = queue;
                PublishedStatus.HostAnswers = hostAnswers;
                PublishedStatus.ReadValue = readValue;
                PublishedStatus.UtcNow = utcNow;
                PublishedStatus.ShareDeadline = shareDeadline;
                QlDevice.JobsPrinted = jobsPrinted;
                Reset();
            }
        }
    }
}
