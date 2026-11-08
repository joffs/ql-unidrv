// Before a print to a QL printer: Windows' "Use Printer Offline" (only when it is the cause), the printer's own state
// and the loaded roll. One copy for QL Label Formats' test print and QL Label Maker's print (refactor R5, 2026-11-08):
// the offline fix of 0.19.8 had to be made twice before. Also "Use printer online" (UseOnline), for the status cards,
// the offline bar and both programs. The callers keep their own busy line and what they do with the new check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    static class PrintReadiness
    {
        public sealed class Request
        {
            public IWin32Window Owner;
            public string Title;            // the dialogs' title (the program's name)
            public string Printer;
            public PrinterCheck Last;       // the last check of a printer (offline counts only when it is the cause), or null
            public string OfflineQuestion;  // translated: "<printer> is set to 'Use Printer Offline': ... Use the printer online now?"
            public bool NoPrintsNow;        // the offline question's No: print at once (QL Label Formats; the job waits in
                                            // the queue), else go on to the roll check (QL Label Maker)
            public Action OnlineTried;      // after Use online was tried (the offline bar follows), or null
            public Media Media;             // what the job needs, or null
            public string LabelName;        // for the roll warning
            public Action<string> Busy;     // the line while the printer is asked (the text), and again after (null)
            public Action<PrinterCheck> Checked;   // the printer's new check, before any warning
        }

        // Swappable for tests: the offline flag, whether the printer can be asked, its check, the dialogs and Use online.
        internal static Func<string, bool> WorkOffline = Spooler.WorkOffline;
        internal static Func<string, bool> HasStatus = p => QlDevice.HasStatus(QlDevice.PortOf(p));
        internal static Func<string, PrinterCheck> Check = p => PrinterCheck.Run(p, true);
        internal static Func<IWin32Window, string, string, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton, DialogResult> Show =
            (owner, text, title, buttons, icon, defaultButton) => QlMessage.Show(owner, text, title, buttons, icon, defaultButton);
        internal static Func<IWin32Window, IList<string>, Task<bool>> Online = UseOnline;

        // True: print. Asks off the UI thread; a format for another roll would stop the printer with an error.
        public static async Task<bool> Confirm(Request q)
        {
            string printer = q.Printer;
            bool offline = false;
            try { offline = WorkOffline(printer); } catch (Exception) { }
            // Not when the printer is switched off (Windows set offline by itself): the roll check below says so; the
            // last check decided with PrinterCheck.OfflineIsCause.
            if (offline && q.Last != null && q.Last.Printer == printer && !q.Last.WorkOffline) offline = false;
            if (offline)
            {
                var answer = Show(q.Owner, q.OfflineQuestion, q.Title, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning,
                                  MessageBoxDefaultButton.Button1);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.No)
                {
                    if (q.NoPrintsNow) return true;   // print anyway: it waits in the queue
                }
                else
                {
                    bool done = await Online(q.Owner, new[] { printer });
                    if (q.OnlineTried != null) q.OnlineTried();
                    if (!done) return false;
                }
            }
            if (!HasStatus(printer)) return true;
            PrinterCheck c = null;
            if (q.Busy != null) q.Busy(Lang.F("Asking {0} for the loaded roll...", printer));
            try { c = await Task.Run(() => Check(printer)); }
            catch (Exception) { }
            var form = q.Owner as Control;
            if (form != null && form.IsDisposed) return false;
            if (q.Busy != null) q.Busy(null);
            if (c == null) return true;  // can't be asked: print as before the check existed
            if (q.Checked != null) q.Checked(c);
            string warn = null;
            bool? fits = c.Media != null ? c.Media.Fits(q.Media) : null;
            if (c.Health == Health.Error || c.Health == Health.Warning)
                warn = printer + ": " + (c.Detail ?? c.Headline) + "\n\n" + string.Join("\n", c.Advice);
            else if (fits == false)
                warn = Lang.F("The printer has {0} loaded, but '{1}' needs {2}.\n\nThe printer would stop with a 'wrong media' error (red LED). Load the matching roll first.",
                              c.Media.LoadedText, q.LabelName, q.Media != null ? q.Media.Display : Lang.T("another roll"));
            if (warn == null) return true;
            return Show(q.Owner, warn + "\n\n" + Lang.T("Print anyway?"), q.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        // "Use printer online": clears Windows' "Use Printer Offline", directly when Windows allows it, else through an
        // elevated copy of this exe (--online; Windows asks for administrator permission). True when it worked.
        internal static async Task<bool> UseOnline(IWin32Window owner, IList<string> printers)
        {
            const string title = "QL Printer Status";
            var left = new List<string>();
            try { foreach (var p in printers) if (!Spooler.UseOnline(p)) left.Add(p); }
            catch (Exception ex)
            {
                QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (left.Count == 0) return true;
            string result = Path.Combine(Path.GetTempPath(), "QlLabelFormats-online-" + Guid.NewGuid().ToString("N") + ".txt");
            string arguments = "--online " + string.Join(" ", left.Select(p => "\"" + p + "\"")) + " --result \"" + result + "\" --lang " + Lang.Current;
            int code;
            try
            {
                code = await Task.Run(() => Elevated.Run(Application.ExecutablePath, arguments));
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (!Elevated.Declined(ex))
                    QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            string text = Lang.T("(no result)");
            try { if (File.Exists(result)) text = File.ReadAllText(result, Encoding.UTF8).Trim(); File.Delete(result); } catch (Exception) { }
            if (code == 0) return true;
            QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + text, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }
}
