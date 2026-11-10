// The QL printers of this PC: every printer queue whose driver is one of ours (QlModel.ForDriver), as Windows lists
// them, Windows' default printer first. Used by all three programs and the status service (moved out of MainForm,
// 2026-11-08, no change).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace QlLabels
{
    static class QlPrinters
    {
        internal static List<string> OurPrinters()
        {
            var names = new List<string>();
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
                {
                    if (key == null) return names;
                    foreach (string n in key.GetSubKeyNames())
                        using (var k = key.OpenSubKey(n))
                            if (k != null && QlModel.ForDriver(Convert.ToString(k.GetValue("Printer Driver"))) != null)
                                names.Add(n);
                }
            }
            catch (Exception) { }
            string windowsDefault = null;
            try { windowsDefault = WindowsDefault(); } catch (Exception) { }
            return InOrder(Listed(names, Spooler.ListedPrinters()), windowsDefault);
        }

        // The QL printer the user last chose in a printer box (QL Label Formats' toolbar, QL Label Maker's printer box),
        // per user and shared by both programs. QL printers are seldom Windows' default (people print A4 there), so the
        // programs start on the last one chosen; if that one is gone, on the first that is still there (
        // 2026-11-09). Only a choice in a printer box is remembered, not a printer handed over with --printer.
        internal static string LastKey = Lang.SettingsKey;   // HKCU; the tests use a key of their own
        const string LastValue = "LastPrinter";

        internal static string LastChosen()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(LastKey)) return k == null ? null : k.GetValue(LastValue) as string; }
            catch (Exception) { return null; }
        }

        internal static void Remember(string printer)
        {
            if (string.IsNullOrEmpty(printer)) return;
            try { using (var k = Registry.CurrentUser.CreateSubKey(LastKey)) k.SetValue(LastValue, printer, RegistryValueKind.String); }
            catch (Exception) { }
        }

        // The printer to show: `wanted` (--printer, the one shown so far) while it is still there, else the last one the
        // user chose, else the first (Windows' default first, OurPrinters); null without QL printers.
        internal static string Choose(List<string> printers, string wanted)
        {
            return Pick(printers, wanted, In(printers, wanted) != null ? null : LastChosen());
        }

        internal static string Pick(List<string> printers, string wanted, string last)
        {
            return In(printers, wanted) ?? In(printers, last) ?? printers.FirstOrDefault();
        }

        // The list's own spelling of a name (printer names don't depend on case).
        static string In(List<string> printers, string name)
        {
            return name == null ? null : printers.FirstOrDefault(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
        }

        // Windows' default printer, or null. GetDefaultPrinter: 2 ms; new PrinterSettings().PrinterName (PrintDlg) took
        // ~50 ms of every window's start (startup_profile, 2026-11-09).
        internal static string WindowsDefault()
        {
            int size = 0;
            GetDefaultPrinter(null, ref size);
            if (size <= 0) return null;
            var name = new StringBuilder(size);
            return GetDefaultPrinter(name, ref size) ? name.ToString() : null;
        }
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetDefaultPrinter(StringBuilder name, ref int size);

        // Only the printers Windows lists: not one removed but pending deletion ("<name>,0" until the spooler restarts).
        // listed null = Windows couldn't be asked: all of them.
        internal static IEnumerable<string> Listed(IEnumerable<string> printers, ICollection<string> listed)
        {
            return listed == null ? printers : printers.Where(listed.Contains);
        }

        // Windows' default printer first when it is one of them (the one preselected), then by name.
        internal static List<string> InOrder(IEnumerable<string> printers, string windowsDefault)
        {
            return printers.OrderBy(n => string.Equals(n, windowsDefault, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                           .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // The model a printer's driver is for, or null.
        internal static QlModel ModelOf(string printer)
        {
            if (printer == null) return null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k != null ? QlModel.ForDriver(Convert.ToString(k.GetValue("Printer Driver"))) : null;
            }
            catch (Exception) { return null; }
        }
    }
}
