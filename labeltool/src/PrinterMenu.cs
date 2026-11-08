using System;
using System.Windows.Forms;

namespace QlLabels
{
    // The Printer menu entries QL Label Formats and QL Label Maker share: "Notify me about printer errors" and "Printer setup".
    static class PrinterMenu
    {
        // The status window's (i) text (StatusForm), in two lines: a tooltip doesn't wrap.
        static string NotifyTip
        {
            get
            {
                string tip = Lang.T("When a QL printer stops, a Windows notification appears at the bottom right with the reason and what " +
                                    "to do – for example cover open, roll empty or wrong roll. For all QL printers and every program. " +
                                    "Starts with Windows; no administrator rights needed.");
                int dash = tip.IndexOf(" – ", StringComparison.Ordinal);
                return dash > 0 ? tip.Substring(0, dash + 2) + "\n" + tip.Substring(dash + 3) : tip;
            }
        }

        // Check mark = the monitor starts with Windows; set again each time `menu` opens. A click switches it, then `changed`.
        public static ToolStripMenuItem NotifyItem(ToolStripMenuItem menu, IWin32Window owner, string title, Action changed)
        {
            var item = new ToolStripMenuItem(Lang.T("Notify me about printer errors")) { ToolTipText = NotifyTip };
            item.Click += delegate { SetNotifications(owner, title, !TrayMonitor.AutostartEnabled); if (changed != null) changed(); };
            menu.DropDownOpening += delegate { item.Checked = TrayMonitor.AutostartEnabled; };
            return item;
        }

        public static void SetNotifications(IWin32Window owner, string title, bool on)
        {
            try { TrayMonitor.SetAutostart(on); }
            catch (Exception ex)
            {
                QlMessage.Ask(owner, title, Lang.T("Printer notifications couldn't be changed."), null,
                              MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message);
            }
        }

        // The Printer setup window (network and shared printers, status service, driver log, uninstall); `after` refreshes
        // the caller's printers once it closes.
        public static ToolStripMenuItem SetupItem(IWin32Window owner, Action after)
        {
            return new ToolStripMenuItem(Lang.T("Printer setup"), null, delegate
            {
                using (var setup = new SetupForm()) setup.ShowDialog(owner);
                if (after != null) after();
            })
            { ToolTipText = Lang.T("Add, share and remove QL printers, printer status service, driver log, uninstall") };
        }
    }
}
