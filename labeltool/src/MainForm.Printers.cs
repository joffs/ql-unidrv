// QL Label Formats' main window: the test printer, the test print and the printer status of the toolbar's printer.
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
        // ---------- printers and test print ----------
        // Is the format's Brother paper in the test printer's paper table (a QL-570 has no 62 × 60 mm labels),
        // and is a continuous format long enough for it (QL-500/550/560/650TD: at least 25 mm)?
        bool ModelLacks(LabelFormat f)
        {
            return f != null && testModel != null && MediaTable.ById(f.PaperId) != null &&
                   (!testModel.HasPaper(f.PaperId) || TooShort(f));
        }

        bool TooShort(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            return m != null && m.Continuous && f.LengthMm < testModel.MinLength01 / 10.0;
        }

        string LacksText(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            if (testModel.HasPaper(f.PaperId) && TooShort(f))
                return Lang.F("Not on {0}: the {1}'s continuous labels are at least {2} mm long (the driver would print this one {2} mm long).",
                              testPrinter, testModel.Name, Media.Mm(testModel.MinLength01 / 10.0));
            return Lang.F("Not on {0}: the {1} has no {2} labels (Brother's driver for it doesn't list this paper).",
                          testPrinter, testModel.Name, m != null ? m.Display : "?");
        }

        // The chosen printer is still set up (it may have been removed since the list was read): never print or set the
        // default on another one without the user seeing it.
        bool PrinterStillThere()
        {
            string before = testPrinter;
            UpdatePrinterStatus();
            if (testPrinter == before) return true;
            QlMessage.Show(this, Lang.F("{0} is no longer set up on this PC. Choose the printer in the toolbar.", before), AppTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        // Test print, default format and roll check on this printer (Printer setup window, after adding it).
        public void UseTestPrinter(string printer)
        {
            testPrinter = printer;
            UpdatePrinterStatus();
        }

        void UpdatePrinterStatus()
        {
            var printers = QlPrinters.OurPrinters();
            StartupTrace.Mark("printers: listed");
            string version = AboutForm.DriverVersion();
            string before = shownPrinter;  // not testPrinter: the printer box sets that before calling this
            if (testPrinter == null || !printers.Contains(testPrinter)) testPrinter = printers.FirstOrDefault();
            shownPrinter = testPrinter;
            if (testPrinter != before || testModel == null)
            {
                testModel = QlPrinters.ModelOf(testPrinter);
                list.BeginUpdate();
                foreach (ListViewItem item in list.Items) StyleItem(item);
                list.EndUpdate();
                ShowInfo(Selected);
            }
            if (testPrinter != before || defaultForm == null) RefreshDefault();
            StartupTrace.Mark("printers: default label format");
            if (testPrinter != before && before != null) { printerCheck = null; ShowPrinterCheck(); CheckPrinter(); }
            // Short (a status label that doesn't fit is hidden, with everything after it). The list is in the Printer
            // setup window (a click), not in a tooltip: a tall one ran into the taskbar.
            printerStatus.Text = version == null ? Lang.T("Driver not installed")
                : Lang.F("QL Unidrv {0} · {1}", version, printers.Count == 0 ? Lang.T("no printer") : printers.Count == 1 ? printers[0] : Lang.F("{0} QL printers", printers.Count));
            printerStatus.ToolTipText = Lang.T("Click for the QL printers on this PC (Printer setup)");
            printButton.Enabled = testPrinter != null;
            printButton.ToolTipText = testPrinter == null ? NoPrinterText
                : Lang.F("Print a sample with the selected format on {0} (F5)", testPrinter);
            FillPrinterBox(printers);
        }

        // The toolbar's printer choice: test print, default format and roll check. The label list itself is shared.
        void FillPrinterBox(List<string> printers)
        {
            fillingPrinters = true;
            try
            {
                var items = printerBox.Items.Cast<object>().Select(o => o.ToString()).ToList();
                if (!items.SequenceEqual(printers))
                {
                    printerBox.Items.Clear();
                    foreach (var n in printers) printerBox.Items.Add(n);
                }
                printerBox.SelectedItem = testPrinter;
                printerBox.Enabled = printers.Count > 0;
                MeasurePrinterBox();
                printerBox.ToolTipText = testPrinter == null ? NoPrinterText
                    : Lang.F("Test print, default label format and the roll check use {0}.\nThe label formats themselves are shared by all QL printers.", testPrinter);
            }
            finally { fillingPrinters = false; }
        }

        async void TestPrint()
        {
            var f = Selected;
            if (f == null || testPrinter == null || busy || !PrinterStillThere()) return;
            if (ModelLacks(f))
            {
                QlMessage.Ask(this, AppTitle, Lang.F("'{0}' can't be printed on {1}.", f.Name, testPrinter),
                              LacksText(f) + "\n\n" + Lang.T("Choose another printer in the toolbar, or base the format on another paper."),
                              MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!IsApplied(f))
            {
                if (!await ApplyFirst(f, Lang.T("Test print"))) return;
                f = Selected;  // the list was read again
            }
            if (!await ConfirmPrinterReady(f)) return;
            string printer = testPrinter;
            try
            {
                var doc = new PrintDocument { DocumentName = Lang.T("QL label format test:") + " " + f.Name };
                doc.PrinterSettings.PrinterName = printer;
                var size = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .FirstOrDefault(p => string.Equals(p.PaperName, f.Name, StringComparison.OrdinalIgnoreCase));
                if (size == null)
                {
                    QlMessage.Show(this, Lang.F("{0} doesn't list '{1}' yet. Click Save & apply; if it stays that way, update the driver.", printer, f.Name), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                doc.DefaultPageSettings.PaperSize = size;
                doc.DefaultPageSettings.Landscape = false;
                doc.PrintController = new StandardPrintController();
                doc.PrintPage += (s, e) => TestLabel.DrawSample(e, f);
                doc.Print();
                applyStatus.Text = Lang.F("Test print of '{0}' sent to {1}.", f.Name, printer);
                // also without focus (printerTimer then pauses)
                string sentTo = printer;
                PrinterCheck.AskAfterPrint(() => printerCheck, CheckPrinter, () => IsDisposed || testPrinter != sentTo);
            }
            catch (Exception ex) { Failed(Lang.T("The test print couldn't be sent."), ex); }
        }

        // The label editor QlLabelMaker.exe (its own process, so it stays open when this window closes).
        void OpenEditor(LabelFormat f)
        {
            try { EditorProgram.Start(null, testPrinter, f != null ? f.Name : null); }
            catch (Exception ex) { Failed(Lang.F("{0} couldn't be started.", "QL Label Maker"), ex); }
        }

        // Windows knows the format as it is shown (saved and applied, no pending edits).
        bool IsApplied(LabelFormat f)
        {
            var applied = saved.FirstOrDefault(s => string.Equals(s.Name, f.Name, StringComparison.OrdinalIgnoreCase));
            return applied != null && applied.SameAs(f) && File.Exists(LabelStore.JsonPath);
        }

        // A new or changed format: offers Save & apply, then the action goes on. True when the format (still selected)
        // is applied now.
        async Task<bool> ApplyFirst(LabelFormat f, string action)
        {
            string text = Lang.F("'{0}' has unsaved changes, so Windows doesn't know it yet.\n\nSave & apply the label formats now and then continue ({1})? Windows asks for administrator permission.",
                                 f.Name, action);
            if (QlMessage.Show(this, text, AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
            if (!await Apply()) return false;
            return Selected != null && IsApplied(Selected);
        }

        // Asks the printer before a test print (PrintReadiness: offline, the printer's state, the loaded roll).
        Task<bool> ConfirmPrinterReady(LabelFormat f)
        {
            string printer = testPrinter;
            string status = applyStatus.Text;
            return PrintReadiness.Confirm(new PrintReadiness.Request
            {
                Owner = this,
                Title = AppTitle,
                Printer = printer,
                Last = printerCheck,
                OfflineQuestion = Lang.F("{0} is set to 'Use Printer Offline': the test label would wait on this PC and not print.\n\n" +
                                         "Use the printer online now? Windows may ask for administrator permission.", printer),
                NoPrintsNow = true,
                OnlineTried = UpdateTipBar,
                Media = MediaTable.ById(f.PaperId),
                LabelName = f.Name,
                Busy = text => SetBusy(text != null, text ?? status),
                Checked = c => { printerCheck = c; lastCheck = DateTime.Now; ShowPrinterCheck(); },
            });
        }

        // ---------- printer status ----------
        async void CheckPrinter()
        {
            string p = testPrinter;
            if (checking || busy || p == null || IsDisposed) return;
            if (!QlDevice.HasStatus(QlDevice.PortOf(p)))
            {
                if (printerCheck != null) { printerCheck = null; ShowPrinterCheck(); }
                mediaStatus.Visible = false;
                return;
            }
            checking = true;
            lastCheck = DateTime.Now;
            StartupTrace.Mark("printer check: start");
            PrinterCheck c = null;
            try { c = await Task.Run(() => PrinterCheck.Run(p, true)); }
            catch (Exception) { }
            checking = false;
            StartupTrace.Mark("printer check: done");
            if (IsDisposed) return;
            if (p != testPrinter) { CheckPrinter(); return; }  // the printer was switched meanwhile
            if (c == null) return;
            printerCheck = c;
            ShowPrinterCheck();
            UpdateTipBar();
        }

        void ShowPrinterCheck()
        {
            var c = printerCheck;
            mediaStatus.Visible = c != null;
            if (c != null)
            {
                mediaStatus.Text = testPrinter + ": " + c.Short;
                Theme.ItemLink(mediaStatus, c.Health == Health.Error ? Role.ErrorText : c.Health == Health.Warning ? Role.WarningText : Role.Text);
                mediaStatus.ToolTipText = (c.Detail ?? c.Headline) + (c.Advice.Count > 0 ? "\n" + string.Join("\n", c.Advice) : "") + "\n" + Lang.T("Click for details (F6).");
            }
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) StyleItem(item);
            list.EndUpdate();
            ShowInfo(Selected);
        }

        QlStatus LoadedRoll
        {
            get { return printerCheck != null && printerCheck.Media != null && !printerCheck.Media.NoMedia ? printerCheck.Media : null; }
        }
    }
}
