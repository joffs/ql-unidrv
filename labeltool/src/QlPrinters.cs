// The QL printers of this PC: every printer queue whose driver is one of ours (QlModel.ForDriver), as Windows lists
// them, Windows' default printer first. Used by all three programs and the status service (moved out of MainForm,
// 2026-11-08, no change).
using System;
using System.Collections.Generic;
using System.Drawing.Printing;
using System.Linq;
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
            try { windowsDefault = new PrinterSettings().PrinterName; } catch (Exception) { }
            return InOrder(Listed(names, Spooler.ListedPrinters()), windowsDefault);
        }

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
