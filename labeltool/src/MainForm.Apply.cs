// QL Label Formats' main window: Save & apply (the elevated --apply run) and the apply status line.
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
        // ---------- save & apply ----------
        async void SaveAndApply() { await Apply(); }

        // Save & apply; true when Windows has the list now.
        async Task<bool> Apply()
        {
            if (busy) return false;
            if (ChangedElsewhere())
            {
                // Applying this window's older list would remove what the other window added.
                using (var d = new SetupResult(AppTitle, ResultKind.Warning, Lang.T("The label formats were changed in another window"), null,
                                               Lang.T("Saving now would undo those changes. Reload shows them here; your unsaved changes in this window are then lost."),
                                               null, Lang.T("Cancel")))
                {
                    d.AddChoice(Lang.T("Reload"), DialogResult.Retry);
                    if (d.ShowDialog(this) == DialogResult.Retry) LoadFromDisk();
                }
                return false;
            }
            Validate();
            foreach (var f in formats) { f.Name = f.Name.Trim(); LabelStore.Normalize(f); }
            List<FormInfo> forms = null;
            try { forms = FormsDb.Enumerate(); } catch (Exception) { }
            List<string> owned;
            try { string unreadable; owned = LabelStore.LoadCurrent(out unreadable).OwnedForms; }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.T("Cannot read the label formats:") + "\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            var errors = LabelStore.Validate(formats, forms, owned);
            if (errors.Count > 0)
            {
                QlMessage.Ask(this, AppTitle, Lang.T("Please correct these entries first."), string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? "\n..." : ""),
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var moves = DefaultsToMove();
            string input = Path.Combine(Path.GetTempPath(), "QlLabelFormats-apply-" + Guid.NewGuid().ToString("N") + ".json");
            string result = Path.ChangeExtension(input, ".txt");
            File.WriteAllText(input, LabelStore.ToJson(new LabelSet { Formats = formats }), new UTF8Encoding(false));
            HideDeclined();
            SetBusy(true, Lang.T("Applying (administrator rights)..."));
            applying = true;
            int code;
            try
            {
                // --lang: the elevated copy may run as another user (other settings), and writes its summary in this language.
                string arguments = "--apply \"" + input + "\" --result \"" + result + "\" --lang " + Lang.Current;
                code = await Task.Run(() => Elevated.Run(Application.ExecutablePath, arguments));
            }
            catch (Win32Exception ex)
            {
                // Windows' prompt answered No: the message bar at the top (Apply again, ×), the status bar stays empty
                bool declined = Elevated.Declined(ex);
                SetBusy(false, declined ? "" : Lang.T("Not applied:") + " " + ex.Message);
                if (declined) ShowDeclined();
                TryDelete(input);
                return false;
            }
            string text = File.Exists(result) ? File.ReadAllText(result, Encoding.UTF8).Trim() : Lang.T("(no result)");
            TryDelete(input);
            TryDelete(result);
            if (code == 0)
            {
                LoadFromDisk();
                string summary = text.Split('\n').Last().Trim();
                var moved = new List<string>();
                foreach (var m in moves)
                {
                    try { Spooler.SetDefaultForm(m.Key, m.Value); moved.Add(m.Key); }
                    catch (Exception ex)
                    {
                        QlMessage.Show(this, Lang.T("The default label format could not be set:") + "\n\n" + m.Key + ": " + ex.Message, AppTitle,
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                if (moves.Count > 0) RefreshDefault();
                if (moved.Count > 0) summary += " " + Lang.F("The default label format follows the new name on {0}.", string.Join(", ", moved));
                // Short in the status bar; the counts (and a moved default) in its tooltip
                string done = Lang.F("Applied at {0}", DateTime.Now.ToString("t"));
                applyDetail = summary;
                applyDetailFor = done;
                SetBusy(false, done);
                FitApplyStatus();
                return true;
            }
            SetBusy(false, Lang.T("Apply failed."));
            QlMessage.Show(this, Lang.T("The label formats could not be applied:") + "\n\n" + text, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }

        // Another QL Label Formats window (or another user) applied a list since this window read labels.json. Compares the
        // formats, not the file's text (an update's --sync writes the same list again).
        bool ChangedElsewhere()
        {
            List<LabelFormat> now;
            try
            {
                if (!File.Exists(LabelStore.JsonPath)) return false;
                now = LabelStore.Load(LabelStore.JsonPath).Formats;
            }
            catch (Exception) { return false; }  // damaged: LoadFromDisk's question
            if (damaged != null) return true;    // was damaged here, readable now: someone applied a list
            foreach (var f in now) LabelStore.Normalize(f);
            return now.Count != saved.Count || now.Zip(saved, (a, b) => a.SameAs(b)).Any(same => !same);
        }

        // The last action's line at the right of the status bar. Right-aligned text longer than its room ran out to the left
        // under the item before it ("…aktuell d“ an Brother QL-570 gesendet.", x64 check 2026-10-31): it is cut at the end
        // with "…" instead (the whole text as its tooltip), after a separator.
        // (applySeparator, applyText, applyShown: in MainForm.cs, so the field initializers run in the order they had)
        bool fittingApply, fitPending;

        void FitApplyStatus()
        {
            if (fittingApply) return;
            fittingApply = true;
            try
            {
                // New text set from outside (the status bar may lay out inside the Text setter, before TextChanged)
                if (applyStatus.Text != applyShown) applyText = applyStatus.Text;
                int room = statusBar.DisplayRectangle.Width - S(16) - applyStatus.Padding.Horizontal - applyStatus.Margin.Horizontal;
                foreach (ToolStripItem item in statusBar.Items)
                    if (item != applyStatus && item.Available) room -= item.Width + item.Margin.Horizontal;
                string shown = applyText ?? "";
                if (shown.Length > 0 && TextRenderer.MeasureText(shown, applyStatus.Font).Width > room)
                {
                    int n = shown.Length;
                    while (n > 0 && TextRenderer.MeasureText(shown.Substring(0, n).TrimEnd() + "…", applyStatus.Font).Width > room) n--;
                    shown = n > 0 ? shown.Substring(0, n).TrimEnd() + "…" : "";
                }
                applyShown = shown;
                if (applyStatus.Text != shown) applyStatus.Text = shown;
                string tip = shown != applyText ? (applyText ?? "").Replace("&&", "&") : "";
                if (applyDetail != null && applyText == applyDetailFor) tip = (tip.Length > 0 ? tip + "\n" : "") + applyDetail;
                if (applyStatus.ToolTipText != tip) applyStatus.ToolTipText = tip;
                bool separator = shown.Length > 0;
                if (applySeparator.Visible != separator) applySeparator.Visible = separator;
            }
            finally { fittingApply = false; }
        }

        bool applying;  // busy with Save & apply (the elevated --apply), not a roll check

        // A status bar item draws "&" as a shortcut mark ("Save & apply" lost its "&"): doubled, it shows as typed.
        static string StatusText(string text) { return text.Replace("&", "&&"); }

        // The tooltip of the short "Applied at ..." (the counts), for that text only (another message replaces it)
        string applyDetail, applyDetailFor;

        void SetBusy(bool value, string message)
        {
            busy = value;
            if (!value) applying = false;
            tools.Enabled = !value;
            menu.Enabled = !value;
            list.Enabled = !value;
            editor.Enabled = !value && Selected != null;
            UseWaitCursor = value;
            applyStatus.Text = message;
        }

        void UpdateSyncStatus()
        {
            syncStatus.Text = damaged != null ? Lang.T("Label list damaged: Save && apply writes it again")
                            : File.Exists(LabelStore.JsonPath) ? Sync.Status(saved) : Lang.T("Not applied to Windows yet");
        }
    }
}
