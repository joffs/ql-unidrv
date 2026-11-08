// QL Label Formats' main window: the default label format (per user, per printer).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed partial class MainForm
    {
        // ---------- default label format ----------
        void RefreshDefault()
        {
            defaultForm = null;
            if (testPrinter != null)
                try { defaultForm = Spooler.DefaultFormName(testPrinter); } catch (Exception) { }
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) StyleItem(item);
            list.EndUpdate();
            ShowInfo(Selected);
        }

        // The QL printers whose default label format (for this user) is the paper size `name`.
        static List<string> DefaultOn(string name)
        {
            return QlPrinters.OurPrinters().Where(p =>
            {
                try { return string.Equals(Spooler.DefaultFormName(p), name, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return false; }
            }).ToList();
        }

        // Renamed formats that are a printer's default (printer, new name): Apply moves the default to the new name, since
        // the paper size with the old name is removed.
        List<KeyValuePair<string, string>> DefaultsToMove()
        {
            var moves = new List<KeyValuePair<string, string>>();
            if (!File.Exists(LabelStore.JsonPath) || damaged != null) return moves;
            foreach (var f in formats)
            {
                var old = saved.Where(s => s.Key == f.Key).ToList();
                if (old.Count != 1 || string.Equals(old[0].Name, f.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (formats.Any(x => string.Equals(x.Name, old[0].Name, StringComparison.OrdinalIgnoreCase))) continue;  // the old name stays
                foreach (var p in DefaultOn(old[0].Name)) moves.Add(new KeyValuePair<string, string>(p, f.Name));
            }
            return moves;
        }

        void UpdateDefaultItem(ToolStripMenuItem item)
        {
            var f = Selected;
            item.Text = f == null ? Lang.T("Use as default label format") : Lang.F("Use '{0}' as default label format", f.Name);
            item.Checked = IsDefault(f);
            item.Enabled = f != null && testPrinter != null && !IsDefault(f) && !ModelLacks(f) && !busy;
            item.ToolTipText = testPrinter == null ? "" : Lang.F("Programs then start with this format on {0} (for your user account).", testPrinter);
        }

        async void SetAsDefault()
        {
            var f = Selected;
            if (f == null || testPrinter == null || busy || !PrinterStillThere() || ModelLacks(f)) return;
            if (!IsApplied(f))
            {
                if (!await ApplyFirst(f, Lang.T("Set as default"))) return;
                f = Selected;
            }
            try
            {
                Spooler.SetDefaultForm(testPrinter, f.Name);
                RefreshDefault();
                if (!IsDefault(f)) throw new InvalidOperationException(Lang.F("The printer kept '{0}'.", defaultForm));
                applyStatus.Text = StatusText(Lang.F("Default label format on {0}: '{1}' (in effect now; no Save & apply needed).", testPrinter, f.Name));
            }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.T("The default label format could not be set:") + "\n\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
