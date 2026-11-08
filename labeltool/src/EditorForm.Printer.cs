// Label editor window (EditorForm.cs), part: printer and label format, the loaded roll, printing.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- printer and label ----------
        // After Printer setup: printers added or removed; the label stays on its printer while that one is still there.
        void RefreshPrinters()
        {
            printers = QlPrinters.OurPrinters();
            string keep = printers.Contains(printer) ? printer : printers.FirstOrDefault();
            if (keep != printer) ChangePrinter(keep);
            FillPrinterBox();
            UpdateCommands();
            CheckRoll();
        }

        void FillPrinterBox()
        {
            loading = true;
            try
            {
                printerBox.Items.Clear();
                foreach (var p in printers) printerBox.Items.Add(p);
                printerBox.SelectedItem = printer;
                printerBox.Enabled = printers.Count > 0;
                tips.SetToolTip(printerBox, printers.Count == 0 ? MainForm.NoPrinterText : Lang.T("The printer the label is printed on (its model decides which labels it offers)"));
                int w = printers.Count == 0 ? S(120) : printers.Max(n => TextRenderer.MeasureText(n, printerBox.Font).Width) + S(30);
                printerBox.DropDownWidth = Math.Max(w, S(120));
            }
            finally { loading = false; }
        }

        void SetPrinter(string name, string formName)
        {
            bool other = name != printer || model == null;
            printer = name;
            model = QlPrinters.ModelOf(printer);
            choices = LabelChoice.All(model);
            if (other) { roll = null; rollStatus.Text = ""; }
            if (printer != null && other)   // the printer's own resolution setting; the label settings change it for this label
            {
                try { highRes = new PrinterSettings { PrinterName = printer }.DefaultPageSettings.PrinterResolution.Y == 600; }
                catch (Exception) { }
            }
            var c = formName != null ? LabelChoice.Find(choices, formName, 0, 0) : null;
            if (c == null && printer != null)
            {
                string def = null;
                try { def = Spooler.DefaultFormName(printer); } catch (Exception) { }
                if (def != null) c = LabelChoice.Find(choices, def, 0, 0);
            }
            loading = true;
            try
            {
                labelBox.Items.Clear();
                foreach (var ch in choices) labelBox.Items.Add(ch);
                int w = choices.Count == 0 ? S(160) : choices.Max(ch => TextRenderer.MeasureText(ch.ToString(), labelBox.Font).Width) + S(30);
                labelBox.DropDownWidth = Math.Max(w, S(160));
                ShowLabelSettings();
            }
            finally { loading = false; }
            SelectLabel(c ?? choices.FirstOrDefault());
            UpdateCommands();
        }

        // Another printer, or new label formats: the label stays; while the opened file's format is kept, that format
        // is chosen as soon as the printer offers it.
        void ChangePrinter(string name)
        {
            var was = label;
            SetPrinter(name, keepFileFormat ? doc.FormName : label != null ? label.Name : null);
            if (was != null && was.Custom && (model == null || model.HasPaper(was.Media.Id)))
                SelectLabel(LabelChoice.Own(was.Media, Math.Max(MinLength, Math.Min(MaxLength, was.LengthMm))));
            if (keepFileFormat && IsFileFormat(label, doc)) keepFileFormat = false;
        }

        // The label the file was made for: its format, or one with the same paper and length (a renamed format).
        static bool IsFileFormat(LabelChoice c, LabelDoc d)
        {
            return c != null && (string.Equals(c.Name, d.FormName, StringComparison.OrdinalIgnoreCase) ||
                                 c.Media.Id == d.PaperId && Math.Abs(c.LengthMm * 10 - d.Length01) < 1);
        }

        void SelectLabel(LabelChoice c)
        {
            loading = true;
            try
            {
                if (c != null && c.Custom && !labelBox.Items.Contains(c))
                {
                    foreach (var old in labelBox.Items.Cast<LabelChoice>().Where(x => x.Custom).ToList()) labelBox.Items.Remove(old);
                    labelBox.Items.Insert(0, c);
                }
                labelBox.SelectedItem = c;
            }
            finally { loading = false; }
            SetLabel(c);
        }

        void SetLabel(LabelChoice c)
        {
            label = c;
            // An empty, unsaved label takes the usual direction for this paper.
            if (c != null && !c.Custom && doc.Objects.Count == 0 && path == null) doc.Across = LabelLayout.AcrossByDefault(c);
            if (c != null && !c.Custom) { autoLength = false; if (lengthError != null) lengthError.Text = ""; }
            Relayout();
        }

        // Along the label <-> across the tape. The objects keep their places on the screen, so the design turns on the label.
        void TurnView()
        {
            if (label == null) { ShowLabelSettings(); return; }
            PushUndo(null);
            doc.Across = !doc.Across;
            lastEdited = null;
            Relayout();
            Touched();
        }

        void Relayout()
        {
            if (label == null) { layout = null; canvas.Geometry = null; canvas.Invalidate(); return; }
            layout = LabelLayout.For(printer, label, highRes, doc.Across);
            canvas.Geometry = layout;
            ShowLabelSettings();
            ApplyZoom();
            Render();
            var p = layout.Printable;
            // Most important first: a narrow window leaves out the last parts (the whole text is in the tooltip).
            labelParts = new[] { label.Name, Media.Mm(Math.Round(layout.ViewWidthMm, 1)) + " × " + Media.Mm(Math.Round(layout.ViewHeightMm, 1)) + " mm",
                                 highRes ? "300 × 600 dpi" : "300 dpi",
                                 Lang.F("printable {0} × {1} mm", Media.Mm(Math.Round(p.Width, 1)), Media.Mm(Math.Round(p.Height, 1))) }
                         .Concat(layout.Approximate ? new[] { Lang.T("approximate: this label is not on the printer (yet)") } : new string[0]).ToArray();
            labelStatus.Text = string.Join("  ·  ", labelParts);
            labelStatus.ToolTipText = labelStatus.Text + "\n" + (layout.Approximate
                ? Lang.T("The printer doesn't offer this label, so the printable area is estimated. A new or changed label format needs Save & apply in QL Label Formats.")
                : Lang.T("Hatched: the printer can't print there. 'Printable' is what the driver reports for this label."));
            ShowRoll();
        }

        // The label line in the status bar: as many whole parts as fit next to the roll and zoom (none cut in the middle).
        string[] labelParts = new string[0];
        void FitLabelStatus()
        {
            if (statusBar == null || labelParts.Length == 0 || !labelStatus.Text.StartsWith(labelParts[0])) return;   // not while it shows a message
            statusBar.PerformLayout();   // the roll's new width
            int others = statusBar.Items.Cast<ToolStripItem>().Where(i => i != labelStatus && i.Visible).Sum(i => i.Width + i.Margin.Horizontal);
            int room = statusBar.DisplayRectangle.Width - others - labelStatus.Margin.Horizontal - labelStatus.Padding.Horizontal - S(8);
            string text = labelParts[0];
            for (int i = 1; i < labelParts.Length; i++)
            {
                string more = text + "  ·  " + labelParts[i];
                if (TextRenderer.MeasureText(more, statusBar.Font).Width > room) break;
                text = more;
            }
            if (labelStatus.Text != text) labelStatus.Text = text;
        }

        void ApplyZoom()
        {
            if (layout == null) { UpdateZoomUi(); return; }
            if (zoom <= 0)
            {
                canvas.Zoom = 0;
                canvas.Dock = DockStyle.Fill;
                UpdateZoomUi();
                return;
            }
            double k = canvas.RealSize * zoom / 100;
            canvas.Dock = DockStyle.None;
            canvas.Zoom = k;
            var need = canvas.Needed(k);
            canvas.Size = new Size(Math.Max(need.Width, host.ClientSize.Width), Math.Max(need.Height, host.ClientSize.Height));
            UpdateZoomUi();
        }

        void OpenLabelFormats() { OpenLabelFormats(null); }

        // newFormat: a continuous label whose length becomes a new format there ("Save as label format ...").
        void OpenLabelFormats(LabelChoice newFormat)
        {
            // QlLabelFormats.exe next to QlLabelMaker.exe.
            string exe = PrinterStatusProgram.LabelTool;
            string args = printer != null ? "--printer " + EditorProgram.Quote(printer) : "";
            if (printer != null && newFormat != null)
                args += " --new-format " + newFormat.Media.Id + " " + (int)Math.Round(newFormat.LengthMm * 10);
            try { Process.Start(exe, args); }
            catch (Exception ex) { Failed(this, Lang.T("QL Label Formats couldn't be started."), ex); }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Back from the status window (or a roll change meanwhile): the loaded roll again.
            if (!printing && DateTime.Now - lastRollCheck > TimeSpan.FromSeconds(3)) CheckRoll();
            // Back from the label formats window: new or changed formats.
            if (printer == null || label == null || printing) return;
            var fresh = LabelChoice.All(model);
            if (fresh.Select(c => c.ToString()).SequenceEqual(choices.Select(c => c.ToString())) && !layout.Approximate) return;
            ChangePrinter(printer);
        }

        // ---------- loaded roll ----------
        async void CheckRoll()
        {
            string p = printer;
            lastRollCheck = DateTime.Now;
            if (p == null || !QlDevice.HasStatus(QlDevice.PortOf(p))) { roll = null; ShowRoll(); return; }
            PrinterCheck c = null;
            try { c = await Task.Run(() => PrinterCheck.Run(p, true)); }
            catch (Exception) { }
            if (IsDisposed || p != printer) return;
            roll = c;
            // A new, empty label: start on the loaded roll.
            if (c != null && c.Media != null && doc.Objects.Count == 0 && path == null && label != null && c.Media.Fits(label.Media) == false)
            {
                var fit = choices.FirstOrDefault(ch => c.Media.Fits(ch.Media) == true);
                if (fit != null) SelectLabel(fit);
            }
            ShowRoll();
        }

        void ShowRoll()
        {
            ShowRollText();
            FitLabelStatus();   // the roll's text takes room from the label line
        }

        // Light/dark switched (Theme): the icons drawn once, the roll line and the bar over the canvas.
        void ThemeChanged(object sender, EventArgs e)
        {
            SizePalette();
            SizeArrangeBar();
            ShowRollText();
            outsideBar.Invalidate();
        }

        void ShowRollText()
        {
            var c = roll;
            if (c == null)
            {
                rollStatus.Text = printer != null && QlDevice.HasStatus(QlDevice.PortOf(printer)) ? Lang.T("Loaded roll: click to check") : "";
                Theme.ItemFore(rollStatus, Role.Text);
                return;
            }
            bool? fits = c.Media != null && label != null ? c.Media.Fits(label.Media) : null;
            rollStatus.Text = c.Short + (fits == false ? " – " + Lang.T("doesn't fit this label") : "");
            Theme.ItemFore(rollStatus, c.Health == Health.Error || fits == false ? Role.ErrorText : c.Health == Health.Warning ? Role.WarningText : Role.Text);
            rollStatus.ToolTipText = (c.Detail ?? c.Headline) + (c.Advice.Count > 0 ? "\n" + string.Join("\n", c.Advice) : "") + "\n" + Lang.T("Click to check again.");
        }


        // ---------- printing ----------
        async void Print()
        {
            if (printer == null || label == null || printing) return;
            if (lengthError.Text.Length > 0)
            {
                QlMessage.Show(this, lengthError.Text, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (layout.Approximate)
            {
                QlMessage.Show(this, Lang.F("{0} doesn't offer '{1}' yet. A new or changed label format must be saved first: Tools > QL Label Formats, then Save & apply.",
                                             printer, label.Name), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            printing = true;
            UpdateCommands();
            try
            {
                if (!ConfirmProblems()) return;
                if (!await ConfirmPrinterReady()) return;
                using (var bmp = EditorRender.Render(doc, layout))
                using (var portrait = EditorRender.ForPrinter(bmp, layout))
                {
                    string name = path != null ? Path.GetFileNameWithoutExtension(path) : label.Name;
                    if (layout.Custom != null) LabelPrinter.Print(layout.Custom, portrait, layout.SkipX, Lang.T("QL label:") + " " + name, (short)copies.Value, null);
                    else LabelPrinter.Print(layout.Page, portrait, Lang.T("QL label:") + " " + name, (short)copies.Value, null);
                }
                labelStatus.Text = Lang.F("Sent to {0}.", printer);
                string sentTo = printer;
                bool first = true;
                PrinterCheck.AskAfterPrint(() => roll, () => { CheckRoll(); if (first) { first = false; Relayout(); } }, () => IsDisposed || printer != sentTo);
            }
            catch (Exception ex) { Failed(this, Lang.T("The label couldn't be printed."), ex); }
            finally
            {
                printing = false;
                UpdateCommands();
                if (closeWhenPrinted && !IsDisposed) { closeWhenPrinted = false; BeginInvoke((MethodInvoker)Close); }
            }
        }

        // Codes with a problem (red in the panel) don't stop printing: the user decides. The
        // dialog lists them; Cancel is the default. True: print.
        bool ConfirmProblems()
        {
            using (var d = ProblemsDialog())
                return d == null || d.ShowDialog(this) == DialogResult.OK;
        }

        // null when no code has a problem
        SetupResult ProblemsDialog()
        {
            var now = DateTime.Now;
            var found = doc.Objects.OfType<BarcodeObject>().Select(b => new KeyValuePair<BarcodeObject, string>(b, EditorRender.Problem(b, layout, now)))
                           .Where(p => p.Value != null).ToList();
            if (found.Count == 0) return null;
            var fields = found.Select(p => new KeyValuePair<string, string>(CodeName(p.Key), p.Value));
            bool blank = found.Any(p => p.Key.Problem(now) != null);
            string note = blank ? Lang.T("Codes with errors may not scan. A code that can't be made from its content stays blank.") : Lang.T("Codes with errors may not scan.");
            var d = new SetupResult(AppTitle, ResultKind.Warning, Lang.T("The label has errors"), fields, note, closeText: Lang.T("Cancel"));
            d.AddChoice(Lang.T("Print anyway"), DialogResult.OK, primary: false);
            HelpPages.SetPage(d, "codes.html");
            return d;
        }

        // "Code 128 „ABC-123“" for the problems dialog
        static string CodeName(BarcodeObject b)
        {
            string kind = b.Kind == Symbology.Qr ? "QR" : b.Kind == Symbology.Ean13 ? "EAN-13" : b.Kind == Symbology.Gs1_128 ? "GS1-128" : "Code 128";
            string s = (b.Data ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return kind + " " + Lang.F("“{0}”", s.Length > 20 ? s.Substring(0, 19) + "…" : s);
        }

        // As the label tool's test print (PrintReadiness): offline queue, printer errors and a roll that doesn't fit stop here.
        Task<bool> ConfirmPrinterReady()
        {
            string p = printer, shown = null;
            return PrintReadiness.Confirm(new PrintReadiness.Request
            {
                Owner = this,
                Title = AppTitle,
                Printer = p,
                Last = roll,
                OfflineQuestion = Lang.F("{0} is set to 'Use Printer Offline': the label would wait on this PC and not print.\n\n" +
                                         "Use the printer online now? Windows may ask for administrator permission.", p),
                NoPrintsNow = false,   // No goes on to the roll check
                Media = label.Media,
                LabelName = label.Name,
                // the label's line again afterwards, also when the print stops here
                Busy = t => { if (t != null) { shown = labelStatus.Text; labelStatus.Text = t; } else labelStatus.Text = shown; },
                Checked = c => { roll = c; ShowRoll(); },
            });
        }
    }
}
