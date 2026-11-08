// Printer setup window (SetupForm.cs), part: the list's share check (a gone share, marked) and the
// "Share a printer" tab (share, rename, stop sharing).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    partial class SetupForm
    {
        void CheckShares()
        {
            int round = ++shareCheckRound;
            foreach (string printer in listed.ToList())
            {
                string port = PortOfPrinter(printer), host, share;
                if (!PublishedStatus.IsShare(port, out host, out share)) continue;
                Task.Run(() =>
                {
                    var state = ShareStateWithin(host, share, ShareCheckLimit);
                    try { BeginInvoke((Action)(() => MarkShare(round, printer, port, state))); }
                    catch (Exception) { }   // the window closed meanwhile
                });
            }
        }

        // The share's state, or Unreachable when the check takes longer than ms (it goes on alone and fills the cache)
        internal static PublishedStatus.ShareState ShareStateWithin(string host, string share, int ms)
        {
            var check = Task.Run(() => { string error; return PublishedStatus.CheckShare(host, share, out error); });
            try { return check.Wait(ms) ? check.Result : PublishedStatus.ShareState.Unreachable; }
            catch (Exception) { return PublishedStatus.ShareState.Unknown; }
        }

        // The mark after the port, or null for none (shared, or not known: no guess)
        internal static string ShareMark(PublishedStatus.ShareState state)
        {
            return state == PublishedStatus.ShareState.NotShared ? Lang.T("no longer shared")
                 : state == PublishedStatus.ShareState.Unreachable ? Lang.T("not reachable") : null;
        }

        void MarkShare(int round, string printer, string port, PublishedStatus.ShareState state)
        {
            string mark = ShareMark(state);
            int i = listed.IndexOf(printer);
            if (IsDisposed || round != shareCheckRound || mark == null || i < 0) return;
            int selected = printerList.SelectedIndex, top = printerList.TopIndex;
            printerList.Items[i] = printer + "   (" + port + " \u2013 " + mark + ")";
            if (printerList.SelectedIndex != selected) printerList.SelectedIndex = selected;
            printerList.TopIndex = top;
        }

        // ---- Share a printer ----

        // The printers that can be shared (ours on USB), the share names in use here and whether other PCs can reach
        // this PC's shares; the tab follows. LoadState passes the real ones, the UI tests made-up ones.
        internal void ShowSharePage(List<ShareablePrinter> printers, Dictionary<string, string> taken, SharingReach reachable)
        {
            bool was = loading;
            loading = true;
            string keep = SelectedShareable() != null ? SelectedShareable().Name : null;
            shareables = printers;
            takenNames = taken;
            reach = reachable;
            shareList.Items.Clear();
            foreach (var p in printers)
                shareList.Items.Add(p.Name + "   (" + p.Port + (p.ShareName != null ? ", " + Lang.F("shared as {0}", p.ShareName) : "") + ")");
            int pick = printers.FindIndex(p => p.Name == keep);
            if (pick < 0) pick = printers.FindIndex(p => p.ShareName == null);   // the first one not shared yet
            if (pick < 0 && printers.Count > 0) pick = 0;
            shareList.SelectedIndex = pick;
            loading = was;
            bool any = printers.Count > 0;
            // Without a printer to share, "Other PCs then print through \\PC\share name." has nothing to refer to (x64 Sandbox)
            sharePrinterRow.Visible = shareNameRow.Visible = shareHintRow.Visible = any;
            noShareable.Visible = !any;
            noShareable.Text = Lang.T("A QL printer connected to this PC by USB can be shared here. There is none right now.");
            reachText.Text = reachable == SharingReach.FirewallOffPublic ? Lang.T("This network is set to public: other PCs can't reach the share.")
                                                                         : Lang.T("Windows' file and printer sharing is off: other PCs can't reach the share.");
            reachRow.Visible = any && reachable != SharingReach.Ok;
            ShareChoiceChanged();
        }

        ShareablePrinter SelectedShareable()
        {
            int i = shareList.SelectedIndex;
            return i >= 0 && i < shareables.Count ? shareables[i] : null;
        }

        void ShareChoiceChanged()
        {
            var p = SelectedShareable();
            if (p != null) shareNameBox.Text = p.ShareName ?? Sharing.Suggest(p.Model != null ? p.Model.Name : "QL", p.Name, takenNames);
            bool shared = p != null && p.ShareName != null;
            shareHint.Text = shared ? Lang.F("Shared as \\\\{0}\\{1}.", Environment.MachineName, p.ShareName)
                                    : Lang.F("Other PCs then print through \\\\{0}\\share name.", Environment.MachineName);
            shareButton.Visible = !shared;
            renameButton.Visible = unshareButton.Visible = shared;
            UpdateShareButtons();
            if (IsHandleCreated) { FitWidths(); FitHeight(); }
        }

        // Share / Rename only with a usable name (Rename: a different one); the reason below the row
        void UpdateShareButtons()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            string problem = p == null ? "" : Sharing.NameProblem(name, p.Name, takenNames);
            shareButton.Enabled = p != null && problem == null;
            renameButton.Enabled = p != null && problem == null && !string.Equals(name, p.ShareName, StringComparison.Ordinal);
            bool show = !string.IsNullOrEmpty(problem);
            shareProblem.Text = show ? problem : "";
            if (shareProblem.Visible != show) { shareProblem.Visible = show; if (IsHandleCreated) FitHeight(); }
        }

        string ShareDetails()
        {
            var p = SelectedShareable();
            string where = "\\\\" + Environment.MachineName + "\\" + (p != null && p.ShareName != null ? p.ShareName : shareNameBox.Text.Trim());
            return Lang.F("On the other PC: \"QL Printer Setup\" > \"Another PC's printer\", enter {0}.", where) + " " +
                   Lang.T("Windows' file and printer sharing must be on here (Settings > Network > Advanced network settings > Advanced sharing settings).") +
                   "\r\n\r\n" + Lang.T("Network printers are better added by their address on every PC; shares of other PCs and output files can't be passed on.");
        }

        void OpenSharingSettings()
        {
            try
            {
                if (reach == SharingReach.FirewallOffPublic) Process.Start("ms-settings:network-status");
                else Process.Start("control.exe", "/name Microsoft.NetworkAndSharingCenter /page Advanced");
            }
            catch (Exception ex) { Say(ex.Message); }
        }

        // The printer's and the share's names go to the elevated script as quoted arguments
        static bool Quotable(string s) { return s.IndexOf('"') < 0; }

        void SharePrinter()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            if (p == null || Sharing.NameProblem(name, p.Name, takenNames) != null || !Quotable(p.Name)) return;
            var r = RunSettings("-SharePrinter \"" + p.Name + "\" -ShareName \"" + name + "\"", Lang.T("Sharing the printer..."));
            if (r == null) return;
            ShowResult(ResultKind.Ok, Lang.T("Printer shared"),
                       Fields(Lang.T("Name"), p.Name, Lang.T("Share"), "\\\\" + Environment.MachineName + "\\" + Get(r, "sharename")),
                       Lang.F("On the other PC: \"QL Printer Setup\" > \"Another PC's printer\", enter {0}.", "\\\\" + Environment.MachineName + "\\" + Get(r, "sharename")) +
                       (reach == SharingReach.Ok ? "" : " " + reachText.Text));
        }

        void RenameShare()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            if (p == null || p.ShareName == null || Sharing.NameProblem(name, p.Name, takenNames) != null || !Quotable(p.Name)) return;
            string pc = "\\\\" + Environment.MachineName + "\\";
            if (QlMessage.Ask(this, Text, Lang.F("Rename the share {0}?", pc + p.ShareName),
                              Lang.F("PCs that print through {0} no longer reach the printer afterwards; add it there again as {1}.", pc + p.ShareName, pc + name),
                              MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunSettings("-SharePrinter \"" + p.Name + "\" -ShareName \"" + name + "\"", Lang.T("Renaming the share..."));
        }

        void StopSharing()
        {
            var p = SelectedShareable();
            if (p == null || p.ShareName == null || !Quotable(p.Name)) return;
            string pc = "\\\\" + Environment.MachineName + "\\";
            if (QlMessage.Ask(this, Text, Lang.F("Stop sharing \"{0}\"?", p.Name),
                              Lang.F("PCs that print through {0} no longer reach the printer afterwards.", pc + p.ShareName),
                              MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunSettings("-UnsharePrinter \"" + p.Name + "\"", Lang.T("Stopping sharing..."));
        }

        // From the result of "Add printer" (USB): the share tab with that printer
        void OfferShare(string printer)
        {
            tabs.Selected = 2;
            int i = shareables.FindIndex(p => p.Name == printer);
            if (i >= 0) shareList.SelectedIndex = i;
            shareNameBox.Focus();
        }
    }
}
