// Label editor window (EditorForm.cs), part: File > "Print with data..." (PrintDataDialog.cs; the engine: PrintJob.cs,
// PrintData.cs). The open label is the template: the dialog gets a copy of it with the printer, label and layout as the
// editor shows them, and the editor's own printer check (roll, offline).
using System;
using System.IO;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // File > Print with data... and the Print button's ▾ menu.
        const Keys PrintWithDataKeys = Keys.Control | Keys.Shift | Keys.P;

        // csv: a file to open at once (tests); null: the dialog's button.
        void PrintWithData(string csv = null)
        {
            if (printer == null || label == null || printing) return;
            canvas.EndEdit();   // the fields as codes end; the template as it is
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
            string name = path != null ? Path.GetFileNameWithoutExtension(path) : label.Name;
            var job = new LabelJob(doc.Clone(), layout.Copy());
            var options = new PrintDataDialog.Options
            {
                Job = job,
                TemplateName = path != null ? Path.GetFileName(path) : Lang.T("New label"),
                LabelText = label.Name,
                PrinterText = printer,
                Printers = printers,
                Printer = printer,
                SwitchPrinter = DialogPrinter,
                DocumentName = Lang.T("QL label:") + " " + name,
                Folder = path != null ? Path.GetDirectoryName(path) : null,
                CsvPath = csv,
                ConfirmPrinterReady = owner => ConfirmPrinterReady(owner),
                Setting = (key, value) => ToolSetting(key, value),
            };
            printing = true;
            UpdateCommands();
            try
            {
                using (var d = new PrintDataDialog(options))
                {
                    printDataDialog = d;
                    RollToDialog();
                    d.ShowDialog(this);   // closed with Close, after any number of prints
                    if (d.Sent != null)
                    {
                        labelStatus.Text = d.Sent;
                        string sentTo = printer;
                        bool first = true;
                        PrinterCheck.AskAfterPrint(() => roll, () => { CheckRoll(); if (first) { first = false; Relayout(); } }, () => IsDisposed || printer != sentTo);
                    }
                }
            }
            finally
            {
                printDataDialog = null;
                printing = false;
                UpdateCommands();
            }
        }

        PrintDataDialog printDataDialog;   // open (tests reach it here)

        // The dialog's printer box: the editor switches as with its own box (remembered for the next start), then the
        // dialog gets the new layout, or why that printer can't print the template.
        PrintDataDialog.Switched DialogPrinter(string name)
        {
            if (name == printer && layout != null) return new PrintDataDialog.Switched { Layout = layout.Copy(), LabelText = label != null ? label.Name : null };
            QlPrinters.Remember(name);
            ChangePrinter(name);
            FillPrinterBox();
            CheckRoll();
            if (label == null || layout == null)
                return new PrintDataDialog.Switched { Error = Lang.F("{0} offers no label for this template.", name) };
            if (layout.Approximate)
                return new PrintDataDialog.Switched { Error = Lang.F("{0} doesn't offer '{1}' yet. A new or changed label format must be saved first: Tools > QL Label Formats, then Save & apply.", name, label.Name) };
            if (lengthError != null && lengthError.Text.Length > 0)
                return new PrintDataDialog.Switched { Error = lengthError.Text };
            return new PrintDataDialog.Switched { Layout = layout.Copy(), LabelText = label.Name };
        }

        // The dialog's printer tile: the state as the status bar's (mockup A): ready with the loaded roll, a roll that
        // doesn't fit the label, the printer's problem, or unknown (a port without status, or not checked yet).
        void RollToDialog()
        {
            var d = printDataDialog;
            if (d == null || d.IsDisposed) return;
            var c = roll;
            if (c == null)
            {
                string port = printer != null ? QlDevice.PortOf(printer) ?? "" : "";
                string line = Lang.T("Status unknown") + (port.Contains(":\\") ? " · " + Lang.T("prints to a file") : "");
                d.ShowPrinter(TileState.Unknown, Health.Info, line,
                              QlDevice.HasStatus(port) ? Lang.T("Status unknown") : Lang.T("QL Label Maker can't ask this printer for its state and loaded roll."));
                return;
            }
            string loaded = c.Media != null && !c.Media.NoMedia ? c.Media.LoadedText : null;
            string tip = (c.Detail ?? c.Line) + (c.Advice.Count > 0 ? "\n" + string.Join("\n", c.Advice) : "");
            bool? fits = c.Media != null && label != null ? c.Media.Fits(label.Media) : null;
            if (fits == false)
                d.ShowPrinter(TileState.Warn, Health.Warning, Lang.T("Wrong roll") + (loaded != null ? " · " + Lang.F("{0} loaded", loaded) : ""),
                              c.Short + " – " + Lang.T("doesn't fit this label"));
            else if (c.Health == Health.Error || c.Health == Health.Warning)
                d.ShowPrinter(TileState.Warn, c.Health, Capital(c.Short), tip);
            else if (c.Health == Health.Ok)
                d.ShowPrinter(TileState.Ok, Health.Ok, Lang.T("Ready") + (loaded != null ? " · " + loaded : ""), c.Short);
            else
                d.ShowPrinter(c.Health == Health.Busy ? TileState.Ok : TileState.Unknown, c.Health, Capital(c.Short), tip);
        }

        static string Capital(string s) { return string.IsNullOrEmpty(s) ? "" : char.ToUpper(s[0]) + s.Substring(1); }
    }
}
