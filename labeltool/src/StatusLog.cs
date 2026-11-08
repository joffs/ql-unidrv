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
    // Every message read from a printer (consecutive duplicates skipped), so an error read by one process
    // (monitor, QL Label Formats) stays known to the others: C:\ProgramData\QLUnidrv\status\printer-status.log, the one
    // folder users can write to (install.ps1); the driver never writes there, because it writes with the spooler's
    // rights. Messages the driver read (roll check before a black-and-red job) and those the status service read
    // (it runs as SYSTEM) are in C:\ProgramData\QLUnidrv\log\printer-status.log, which users can only read; Last() looks
    // at both.
    //   yyyy-MM-dd HH:mm:ss TAB printer TAB 32 bytes hex TAB summary
    static class StatusLog
    {
        public static string Path { get { return System.IO.Path.Combine(LabelStore.DataDir, @"status\printer-status.log"); } }
        public static string DriverPath { get { return System.IO.Path.Combine(LabelStore.DataDir, @"log\printer-status.log"); } }
        const long MaxSize = 256 * 1024;
        static readonly object Gate = new object();

        public static void Append(string printer, IEnumerable<QlStatus> messages, QlStatus reply) { Append(printer, messages, reply, Path); }

        // The status service: into the driver's log folder (only administrators may change it). Not through a link
        // or junction: SYSTEM would write wherever it points.
        public static void AppendService(string printer, IEnumerable<QlStatus> messages, QlStatus reply)
        {
            try
            {
                string dir = System.IO.Path.GetDirectoryName(DriverPath);
                if (!Directory.Exists(dir) || (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return;
                if (File.Exists(DriverPath) && (File.GetAttributes(DriverPath) & FileAttributes.ReparsePoint) != 0) return;
            }
            catch (Exception) { return; }
            Append(printer, messages, reply, DriverPath);
        }

        static void Append(string printer, IEnumerable<QlStatus> messages, QlStatus reply, string path)
        {
            var all = messages.ToList();
            if (reply != null) all.Add(reply);
            if (all.Count == 0) return;
            lock (Gate)
            {
                try
                {
                    var last = Last(printer);
                    var sb = new StringBuilder();
                    foreach (var m in all)
                    {
                        // Replies (also an error given as the answer) repeat while nothing changes; log them only when
                        // they differ from the last entry.
                        if ((m.Type == QlStatus.TypeReply || ReferenceEquals(m, reply)) && last != null && last.Hex == m.Hex) continue;
                        sb.Append(m.Time.ToString("yyyy-MM-dd HH:mm:ss")).Append('\t').Append(printer).Append('\t')
                          .Append(m.Hex).Append('\t').Append(Lang.InEnglish(() => m.Summary).Replace('\t', ' ')).Append("\r\n");
                        last = m;
                    }
                    if (sb.Length == 0) return;
                    var fi = new FileInfo(path);
                    if (fi.Exists && fi.Length > MaxSize)
                    {
                        string old = System.IO.Path.ChangeExtension(path, ".old.log");
                        File.Copy(path, old, true);
                        File.Delete(path);
                    }
                    File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));
                }
                catch (Exception) { }  // the log is a help, never a reason to fail
            }
        }

        // The newest logged message of this printer (own log or the driver's), or null.
        public static QlStatus Last(string printer)
        {
            var own = LastIn(Path, printer);
            var driver = LastIn(DriverPath, printer);
            if (own == null) return driver;
            return driver != null && driver.Time > own.Time ? driver : own;
        }

        static QlStatus LastIn(string path, string printer)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string[] lines;
                using (var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var rd = new StreamReader(s, Encoding.UTF8))
                    lines = rd.ReadToEnd().Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = lines.Length - 1; i >= 0; i--)
                {
                    var f = lines[i].Split('\t');
                    if (f.Length < 3 || f[1] != printer) continue;
                    DateTime t;
                    if (!DateTime.TryParseExact(f[0], "yyyy-MM-dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out t)) continue;
                    return QlStatus.FromHex(f[2], t);
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}
