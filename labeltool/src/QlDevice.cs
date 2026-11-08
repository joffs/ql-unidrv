// Direct USB access to a QL printer through its usbprint.sys device interface (no administrator rights).
//
// A QL printer answers ESC i S with a 32-byte status, and sends 32-byte messages by itself while printing
// (phase change, printing completed, error). The driver has no language monitor, so nobody else reads
// them: they wait in the printer until Query() drains them. Opening the device works even while the
// spooler sends a job, but a request is only sent when the printer's queue is empty: three bytes written
// in the middle of a job would corrupt it. A printer in an error state answers nothing.
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
    // NotPublished: the status comes from the service "QL printer status" (this PC's, or the host's for a printer
    // share), and there is none (service not running there, or the status is outdated). A client's printer share:
    // NotShared = the host no longer shares it under that name (printing through it fails), Unreachable = the host
    // doesn't answer (PublishedStatus.QueryShare).
    enum DeviceState { NotUsb, NotConnected, Failed, Open, NotPublished, NotShared, Unreachable }

    sealed class DeviceResult
    {
        public string Printer, Port, Error;
        public DeviceState State;
        public readonly List<QlStatus> Messages = new List<QlStatus>();  // unsolicited, oldest first
        public QlStatus Reply;                                            // answer to ESC i S
        public bool RequestSent, RequestBlocked;
        public bool Printing;  // the printer's newest message says "printing": no request was sent
        // Published status (PublishedStatus.cs): where it came from (local queue or \\host\share), when the
        // service last published it, whether that is too old; Logged = its newest message.
        public string Source;
        public string Network;  // a network printer (NetworkStatus.cs): its address
        public DateTime? PublishedAt;
        public bool Stale;
        public QlStatus Logged;
        public bool IsPublished { get { return Source != null; } }
    }

    static partial class QlDevice
    {
        const string UsbPrintClass = @"SYSTEM\CurrentControlSet\Control\DeviceClasses\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}";
        static readonly byte[] StatusRequest = { 0x1B, 0x69, 0x53 };

        public static string PortOf(string printer)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k == null ? null : Convert.ToString(k.GetValue("Port"));
            }
            catch (Exception) { return null; }
        }

        public static bool IsUsbPort(string port)
        {
            return port != null && port.StartsWith("USB", StringComparison.OrdinalIgnoreCase);
        }

        // Ports with a printer status: USB, a network printer (Standard TCP/IP port, SNMP), and a printer share
        // (\\host\share) whose host publishes it.
        public static bool HasStatus(string port)
        {
            string host, share;
            return IsLocalStatusPort(port) || PublishedStatus.IsShare(port, out host, out share);
        }

        // A printer this PC asks itself (USB or network), as opposed to a share.
        public static bool IsLocalStatusPort(string port)
        {
            return IsUsbPort(port) || NetworkStatus.IsNetworkPort(port);
        }

        // "USB001" -> the device interface path usbmon uses for that port, or null.
        static string InterfaceOf(string port)
        {
            using (var cls = Registry.LocalMachine.OpenSubKey(UsbPrintClass))
            {
                if (cls == null) return null;
                foreach (string k in cls.GetSubKeyNames())
                {
                    using (var p = cls.OpenSubKey(k + @"\#\Device Parameters"))
                    {
                        if (p == null) continue;
                        object num = p.GetValue("Port Number");
                        if (num == null) continue;
                        string name = Convert.ToString(p.GetValue("Base Name")) + Convert.ToInt32(num).ToString("000");
                        if (!string.Equals(name, port.TrimEnd(':'), StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    using (var h = cls.OpenSubKey(k + @"\#"))
                    {
                        string link = h == null ? null : h.GetValue("SymbolicLink") as string;
                        return link ?? @"\\?\" + k.Substring(4);
                    }
                }
            }
            return null;
        }

        // The Brother USB printers connected now (usbprint interfaces, whatever printer driver they have), each
        // asked for its status (ESC i S unless it is printing): model and loaded roll before our driver is
        // installed (the setup window). Not written to the status log.
        public static List<DeviceResult> ScanUsb()
        {
            var list = new List<DeviceResult>();
            using (var cls = Registry.LocalMachine.OpenSubKey(UsbPrintClass))
            {
                if (cls == null) return list;
                foreach (string k in cls.GetSubKeyNames())
                {
                    if (k.IndexOf("VID_04F9", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    // Interfaces of printers connected earlier stay listed; those don't open (NotConnected, left out).
                    string link, port = null;
                    using (var h = cls.OpenSubKey(k + @"\#"))
                    {
                        if (h == null) continue;
                        link = h.GetValue("SymbolicLink") as string ?? @"\\?\" + k.Substring(4);
                    }
                    using (var p = cls.OpenSubKey(k + @"\#\Device Parameters"))
                        if (p != null && p.GetValue("Port Number") != null)
                            port = Convert.ToString(p.GetValue("Base Name")) + Convert.ToInt32(p.GetValue("Port Number")).ToString("000");
                    DeviceResult r;
                    if (port != null) r = QueryDirect(null, port, true, null, false);
                    else
                    {
                        r = new DeviceResult();
                        try { Read(link, true, null, r); }
                        catch (Exception ex) { r.State = DeviceState.Failed; r.Error = ex.Message; }
                    }
                    if (r.State != DeviceState.NotConnected) list.Add(r);
                }
            }
            return list;
        }

        // The printer's status for QL Label Formats, the status window and the monitor: published by the service "QL printer
        // status" (this PC's while it runs; the host's for a printer share), else asked directly (USB or network).
        // request = also send ESC i S (USB only; only when the printer's queue is empty).
        public static DeviceResult Query(string printer, bool request)
        {
            string port = PortOf(printer), host, share;
            if (PublishedStatus.IsShare(port, out host, out share)) return PublishedStatus.QueryShare(printer, port, host, share);
            string queue = IsLocalStatusPort(port) && PublishedStatus.ServiceRunning ? printer : null;
            if (queue == null) return QueryLocal(printer, port, request, request ? StatusLog.Last(printer) : null, true);
            try
            {
                var p = PublishedStatus.Parse(PublishedStatus.Read(queue));
                var r = PublishedStatus.ToDevice(printer, port, queue, p, DateTime.UtcNow);
                // This PC's own printer and the service just stopped (it published "Stopped"; whether it runs is checked
                // only every 10 s): read the printer itself now, not "no current status" first. Not for a status that is
                // only old: a paused service (the tester kit sends to the printer directly) must stay the only reader.
                if (p != null && p.State == "Stopped")
                {
                    PublishedStatus.ForgetServiceCheck();
                    return QueryLocal(printer, port, request, request ? StatusLog.Last(printer) : null, true);
                }
                return r;
            }
            catch (Exception ex)
            {
                var r = PublishedStatus.ToDevice(printer, port, queue, null, DateTime.UtcNow);
                r.Error = ex.Message;
                return r;
            }
        }

        // Asks the printer itself: over the network (SNMP; safe at any time) or over USB.
        public static DeviceResult QueryLocal(string printer, string port, bool request, QlStatus logged, bool writeLog)
        {
            if (!NetworkStatus.IsNetworkPort(port)) return QueryDirect(printer, port, request, logged, writeLog);
            var r = NetworkStatus.Query(printer, port);
            if (writeLog) StatusLog.Append(printer, r.Messages, r.Reply);
            return r;
        }

        // Set by the service: it is the only reader while it runs, so it needs no gate. Its Local\ name would be in
        // session 0's global namespace, where any user could create it first and lock the service out (security
        // audit 2026-10-24 M2).
        public static bool SoleReader;

        // Reads the printer over USB. logged: the newest message known before (another process's "printing");
        // writeLog: append what was read to the status log (not the service: it runs as SYSTEM and publishes).
        public static DeviceResult QueryDirect(string printer, string port, bool request, QlStatus logged, bool writeLog)
        {
            var r = new DeviceResult { Printer = printer, Port = port };
            if (!IsUsbPort(r.Port)) { r.State = DeviceState.NotUsb; return r; }
            string path;
            try { path = InterfaceOf(r.Port); }
            catch (Exception ex) { r.State = DeviceState.Failed; r.Error = ex.Message; return r; }
            if (path == null) { r.State = DeviceState.NotConnected; return r; }

            // One process at a time per printer: a reply read by another process would look like "no answer". A gate
            // that can't be opened is skipped, not an error.
            Mutex gate = null;
            if (!SoleReader)
                try { gate = new Mutex(false, @"Local\QLUnidrv.Usb." + r.Port.TrimEnd(':')); } catch (Exception) { }
            bool mine = false;
            try
            {
                if (gate != null)
                    try { mine = gate.WaitOne(5000); } catch (AbandonedMutexException) { mine = true; }
                Read(path, request, logged, r);
            }
            finally
            {
                if (mine) gate.ReleaseMutex();
                if (gate != null) gate.Dispose();
            }
            OnlySecondMiss(r, r.Port);
            if (writeLog) StatusLog.Append(printer, r.Messages, r.Reply);
            return r;
        }

        // logged: the newest message any process logged for this printer (a "printing" message read by another).
        static void Read(string path, bool request, QlStatus logged, DeviceResult r)
        {
            var queueDone = QueueDone(r.Printer, DateTime.Now);
            using (var h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                                       OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero))
            {
                if (h.IsInvalid)
                {
                    int e = Marshal.GetLastWin32Error();
                    r.State = e == 2 || e == 3 || e == 1167 ? DeviceState.NotConnected : DeviceState.Failed;
                    r.Error = new Win32Exception(e).Message;
                    return;
                }
                r.State = DeviceState.Open;
                for (int i = 0; i < 16; i++)
                {
                    var m = ReadOne(h);
                    if (m == null) break;
                    r.Messages.Add(m);
                }
                if (!request) return;
                // Still printing: three bytes of ours in the middle of data sent directly would spoil the label (seen
                // 2026-10-19); StillPrinting above.
                var newest = r.Messages.Count > 0 ? r.Messages[r.Messages.Count - 1] : logged;
                if (StillPrinting(newest, DateTime.Now, queueDone))
                {
                    r.Printing = true;
                    return;
                }
                if (Transfer(h, StatusRequest, true, 1000) != StatusRequest.Length) { r.RequestBlocked = true; return; }
                r.RequestSent = true;
                int start = Environment.TickCount;
                while (Environment.TickCount - start < 1500)
                {
                    var m = ReadOne(h);
                    if (m == null) { Thread.Sleep(50); continue; }
                    // An idle printer with an error (cover open, no roll) answers with the error message.
                    if (m.Type == QlStatus.TypeReply || m.Type == QlStatus.TypeError) { r.Reply = m; break; }
                    r.Messages.Add(m);
                }
            }
        }

        static QlStatus ReadOne(SafeFileHandle h)
        {
            var buf = new byte[QlStatus.Size];
            int n = Transfer(h, buf, false, 200);  // waiting messages arrive at once
            return n > 0 ? QlStatus.TryParse(buf, n, DateTime.Now) : null;
        }

        // One overlapped read or write with a timeout. Returns the byte count, or -1 (error / timeout).
        static int Transfer(SafeFileHandle h, byte[] data, bool write, int timeoutMs)
        {
            IntPtr buf = Marshal.AllocHGlobal(data.Length);
            IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
            IntPtr ev = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
            try
            {
                var o = new NativeOverlapped { EventHandle = ev };
                Marshal.StructureToPtr(o, ov, false);
                if (write) Marshal.Copy(data, 0, buf, data.Length);
                int n;
                bool ok = write ? WriteFile(h, buf, data.Length, out n, ov) : ReadFile(h, buf, data.Length, out n, ov);
                if (!ok)
                {
                    if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return -1;
                    if (WaitForSingleObject(ev, (uint)timeoutMs) != 0)
                    {
                        CancelIoEx(h, ov);
                        // A message that arrived while the read was cancelled is kept, not dropped.
                        return GetOverlappedResult(h, ov, out n, true) && !write && n > 0 ? CopyRead(buf, data, n) : -1;
                    }
                    if (!GetOverlappedResult(h, ov, out n, false)) return -1;
                }
                return !write && n > 0 ? CopyRead(buf, data, n) : n;
            }
            finally
            {
                CloseHandle(ev);
                Marshal.FreeHGlobal(ov);
                Marshal.FreeHGlobal(buf);
            }
        }

        static int CopyRead(IntPtr buf, byte[] data, int n)
        {
            Marshal.Copy(buf, data, 0, Math.Min(n, data.Length));
            return n;
        }

        const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
        const int ERROR_IO_PENDING = 997;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int count, out int read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int count, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr CreateEventW(IntPtr security, bool manualReset, bool initialState, IntPtr name);
        [DllImport("kernel32.dll")]
        static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
    }
}
