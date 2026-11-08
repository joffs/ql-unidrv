// Sharing one of this PC's QL printers on USB for other PCs (Printer setup's "Share a printer" tab, 2026-11-06): which
// printers can be shared and how, whether a share name can be used, and whether Windows lets other PCs reach shares at
// all. Everything read without administrator rights; the change itself runs install.ps1 -SharePrinter / -UnsharePrinter.
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class ShareablePrinter
    {
        public string Name, Port, ShareName;   // ShareName: null while not shared
        public QlModel Model;
    }

    enum SharingReach { Ok, ServerOff, FirewallOff, FirewallOffPublic }

    static class Sharing
    {
        const string PrintersKey = @"SYSTEM\CurrentControlSet\Control\Print\Printers";

        // Our printers on a USB port (network printers are added by address on each PC; shares and files can't be
        // passed on), by name
        public static List<ShareablePrinter> Printers()
        {
            var list = new List<ShareablePrinter>();
            try
            {
                foreach (string name in QlPrinters.OurPrinters())
                    using (var k = Registry.LocalMachine.OpenSubKey(PrintersKey + "\\" + name))
                    {
                        if (k == null) continue;
                        string port = Convert.ToString(k.GetValue("Port"));
                        if (!IsUsbPort(port)) continue;
                        bool shared = (Convert.ToInt32(k.GetValue("Attributes", 0)) & 0x8) != 0;   // PRINTER_ATTRIBUTE_SHARED
                        list.Add(new ShareablePrinter { Name = name, Port = port.TrimEnd(':'), Model = QlModel.ForDriver(Convert.ToString(k.GetValue("Printer Driver"))),
                                                        ShareName = shared ? Convert.ToString(k.GetValue("Share Name")) : null });
                    }
            }
            catch (Exception) { }
            return list.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // A shared printer's share name (registry, no rights needed), else null
        public static string ShareNameOf(string printer)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(PrintersKey + "\\" + printer))
                    if (k != null && (Convert.ToInt32(k.GetValue("Attributes", 0)) & 0x8) != 0)
                    {
                        string s = Convert.ToString(k.GetValue("Share Name"));
                        return s.Length > 0 ? s : null;
                    }
            }
            catch (Exception) { }
            return null;
        }

        // Characters Windows doesn't take in a share name
        internal static readonly char[] Invalid = { '"', '/', '\\', '[', ']', ':', '|', '<', '>', '+', '=', ';', ',', '?', '*' };

        internal static bool IsUsbPort(string port) { return port != null && System.Text.RegularExpressions.Regex.IsMatch(port, @"^USB\d{3}:?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase); }

        // The share names this PC uses, with what uses them (a printer's name, "" for a folder): every shared printer's
        // (registry) and every folder share (LanmanServer\Shares). Share names compare without case, as Windows does.
        public static Dictionary<string, string> TakenNames()
        {
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(PrintersKey))
                    if (key != null)
                        foreach (string n in key.GetSubKeyNames())
                            using (var k = key.OpenSubKey(n))
                                if (k != null && (Convert.ToInt32(k.GetValue("Attributes", 0)) & 0x8) != 0)
                                {
                                    string s = Convert.ToString(k.GetValue("Share Name"));
                                    if (s.Length > 0 && !taken.ContainsKey(s)) taken[s] = n;
                                }
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\LanmanServer\Shares"))
                    if (key != null)
                        foreach (string s in key.GetValueNames())
                            if (s.Length > 0 && !taken.ContainsKey(s)) taken[s] = "";
            }
            catch (Exception) { }
            return taken;
        }

        // Why the share name can't be used for the printer, "" when it is empty (the button is just grey), null when
        // it is fine. The printer's own share counts as free (unchanged or a rename). Windows: up to 80 characters,
        // none of Invalid (install.ps1 Test-ShareName checks the same).
        internal static string NameProblem(string name, string printer, Dictionary<string, string> taken)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "";
            if (name.Length > 80 || name.IndexOfAny(Invalid) >= 0) return Lang.T("At most 80 characters, none of these: \" / \\ [ ] : | < > + = ; , ? *");
            string owner;
            if (taken.TryGetValue(name, out owner) && !string.Equals(owner, printer, StringComparison.OrdinalIgnoreCase))
                return owner.Length == 0 ? Lang.F("\"{0}\" is taken on this PC (a shared folder).", name)
                                         : Lang.F("\"{0}\" is taken on this PC (printer \"{1}\").", name, owner);
            return null;
        }

        // The model's name ("QL-800"), else "QL-800 (2)", "QL-800 (3)" ...
        internal static string Suggest(string model, string printer, Dictionary<string, string> taken)
        {
            string name = model;
            for (int i = 2; NameProblem(name, printer, taken) != null; i++) name = model + " (" + i + ")";
            return name;
        }

        // Whether other PCs can reach this PC's shares: the service "Server" runs, and the firewall lets "File and
        // Printer Sharing" in for the network profile in use (or is off there). Read only; unknown counts as Ok.
        public static SharingReach Reach()
        {
            try
            {
                using (var sc = new ServiceController("LanmanServer"))
                    if (sc.Status != ServiceControllerStatus.Running) return SharingReach.ServerOff;
            }
            catch (Exception) { return SharingReach.ServerOff; }   // not installed
            try
            {
                var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                if (type == null) return SharingReach.Ok;
                object policy = Activator.CreateInstance(type);
                try
                {
                    int profiles = (int)type.InvokeMember("CurrentProfileTypes", System.Reflection.BindingFlags.GetProperty, null, policy, null);
                    bool on = false;
                    foreach (int p in new[] { 1, 2, 4 })   // domain, private, public
                        if ((profiles & p) != 0 && (bool)type.InvokeMember("FirewallEnabled", System.Reflection.BindingFlags.GetProperty, null, policy, new object[] { p }))
                            on = true;
                    if (!on) return SharingReach.Ok;
                    // "File and Printer Sharing" (its group name in FirewallAPI.dll, the same in every language)
                    bool open = (bool)type.InvokeMember("IsRuleGroupCurrentlyEnabled", System.Reflection.BindingFlags.GetProperty, null, policy,
                                                         new object[] { "@FirewallAPI.dll,-28502" });
                    if (open) return SharingReach.Ok;
                    return (profiles & 4) != 0 ? SharingReach.FirewallOffPublic : SharingReach.FirewallOff;
                }
                finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(policy); }
            }
            catch (Exception) { return SharingReach.Ok; }
        }
    }
}
