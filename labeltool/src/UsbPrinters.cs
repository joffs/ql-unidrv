// QL printers connected by USB now that have no printer (queue) with our driver on this PC: after "Remove printer"
// Windows doesn't add one again by itself, neither when the printer is plugged in again nor after a restart (USB
// remove tests 2026-11-06, ARM and x64). The Printer setup window offers "Add printer" for them (SetupForm), which
// runs install.ps1 -Settings -UsbPrinter: our driver on that USB port, the USB device is not rebound.
// Read without administrator rights: Windows' printer devices (Enum\USBPRINT, instance "...&USB001" = its port),
// present ones only (CM_Locate_DevNode), whatever driver Windows gave them (its generic one, Brother's, ours).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class UsbCandidate
    {
        public QlModel Model;
        public string Port;           // "USB001"
        public string BrotherQueue;   // Brother's own printer on the same port (its driver), else null
    }

    sealed class PrinterQueue
    {
        public string Name, Port, Driver;
    }

    static class UsbPrinters
    {
        const string UsbPrintEnum = @"SYSTEM\CurrentControlSet\Enum\USBPRINT";

        public static List<UsbCandidate> Unset()
        {
            try
            {
                var devices = new List<KeyValuePair<string, string>>();
                using (var en = Registry.LocalMachine.OpenSubKey(UsbPrintEnum))
                    if (en != null)
                        foreach (string hw in en.GetSubKeyNames())
                        {
                            if (ModelOf(hw) == null) continue;
                            using (var k = en.OpenSubKey(hw))
                                if (k != null)
                                    foreach (string inst in k.GetSubKeyNames())
                                        if (Present(@"USBPRINT\" + hw + @"\" + inst)) devices.Add(new KeyValuePair<string, string>(hw, inst));
                        }
                if (devices.Count == 0) return new List<UsbCandidate>();
                return Find(devices, Queues(), SetupForm.Drivers());
            }
            catch (Exception) { return new List<UsbCandidate>(); }
        }

        // The printers Windows lists (not one pending deletion: Spooler.ListedPrinters), with port and driver
        static List<PrinterQueue> Queues()
        {
            var queues = new List<PrinterQueue>();
            var listed = Spooler.ListedPrinters();
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
                if (key != null)
                    foreach (string n in key.GetSubKeyNames())
                    {
                        if (listed != null && !listed.Contains(n)) continue;
                        using (var k = key.OpenSubKey(n))
                            if (k != null) queues.Add(new PrinterQueue { Name = n, Port = Convert.ToString(k.GetValue("Port")), Driver = Convert.ToString(k.GetValue("Printer Driver")) });
                    }
            return queues;
        }

        // devices = (hardware key under Enum\USBPRINT, instance) of the present ones. A device counts when it is one of
        // our models, its driver is installed here and no printer with one of our drivers is on its port.
        internal static List<UsbCandidate> Find(IEnumerable<KeyValuePair<string, string>> devices, IEnumerable<PrinterQueue> queues, ICollection<string> drivers)
        {
            var list = new List<UsbCandidate>();
            var q = queues.ToList();
            foreach (var d in devices)
            {
                var model = ModelOf(d.Key);
                string port = PortOf(d.Value);
                if (model == null || port == null || !drivers.Contains(model.DriverName)) continue;
                if (list.Any(c => c.Port == port)) continue;
                var onPort = q.Where(p => string.Equals((p.Port ?? "").TrimEnd(':'), port, StringComparison.OrdinalIgnoreCase)).ToList();
                if (onPort.Any(p => QlModel.ForDriver(p.Driver) != null)) continue;
                var brother = onPort.FirstOrDefault(p => (p.Driver ?? "").StartsWith("Brother ", StringComparison.OrdinalIgnoreCase));
                list.Add(new UsbCandidate { Model = model, Port = port, BrotherQueue = brother != null ? brother.Name : null });
            }
            return list.OrderBy(c => c.Port, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // "BrotherQL-80053B7" (the IEEE 1284 ID with its checksum) or "BROTHERQL-570" -> QL-800 / QL-570; the longest
        // model name that fits, followed by nothing or 4 hex digits.
        internal static QlModel ModelOf(string hardwareKey)
        {
            var m = Regex.Match(hardwareKey ?? "", @"^Brother\s*QL-?(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string rest = m.Groups[1].Value;
            return MediaTable.Models.Where(x => x.Name.StartsWith("QL-"))
                .OrderByDescending(x => x.Name.Length)
                .FirstOrDefault(x =>
                {
                    string n = x.Name.Substring(3);
                    if (!rest.StartsWith(n, StringComparison.OrdinalIgnoreCase)) return false;
                    string tail = rest.Substring(n.Length);
                    return tail.Length == 0 || Regex.IsMatch(tail, "^[0-9A-Fa-f]{4}$");
                });
        }

        // "6&1DFFA9C2&1&USB002" -> "USB002"
        internal static string PortOf(string instance)
        {
            var m = Regex.Match(instance ?? "", @"&(USB\d{3})$", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
        }

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        static extern int CM_Locate_DevNodeW(out int devInst, string deviceId, int flags);

        // Connected now: a device that isn't there (only remembered) is not found with CM_LOCATE_DEVNODE_NORMAL
        static bool Present(string instanceId)
        {
            int inst;
            try { return CM_Locate_DevNodeW(out inst, instanceId, 0) == 0; }
            catch (Exception) { return false; }
        }
    }
}
