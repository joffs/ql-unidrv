// Label editor window (EditorForm.cs), part: File > "Print with data..." (PrintDataDialog.cs; the engine: PrintJob.cs,
// PrintData.cs). The open label is the template: the dialog gets a copy of it with the printer, label and layout as the
// editor shows them, and the editor's own printer check (roll, offline).
using System;
using System.IO;
using System.Text;
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
                Link = doc.PrintData,
                LabelPath = path,
                LinkChanged = LinkData,
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
                    string note = linkNote;
                    linkNote = null;
                    if (d.Sent == null && note != null) labelStatus.Text = note;
                    if (d.Sent != null)
                    {
                        labelStatus.Text = d.Sent + (note != null ? "  " + note : "");
                        string sentTo = printer;
                        bool first = true;
                        PrinterCheck.AskAfterPrint(() => roll, () => { CheckRoll(); if (first) { first = false; RelayoutKeepingMessage(); } }, () => IsDisposed || printer != sentTo);
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

        string linkNote;   // what happened to the label's data file, for the status bar when the dialog closes

        // The dialog chose another data file, or "Don't remember the file" (null). A saved label without other changes
        // gets it at once (2026-11-11: the asterisk confused): only its "printData" is written, the rest of the file stays
        // as it is, no asterisk, no undo step. Else (other changes, never saved, the file read-only, offline or changed on
        // disk since): the label is changed and the next Save writes it. A link found at another place than written
        // (moved with the label) is kept as saved: no asterisk, the next Save writes the new place. True: in the file now.
        bool LinkData(PrintDataLink link)
        {
            doc.PrintData = link;
            if (link != null && link.Saved) return true;
            if (path != null && !dirty && WriteLinkOnly(link))
            {
                doc.PrintData = link != null ? link.SavedAt(path) : null;
                linkNote = link != null ? Lang.T("Data file remembered in the label.") : Lang.T("The data file is no longer remembered in the label.");
                return true;
            }
            if (!dirty) { dirty = true; UpdateTitle(); }
            linkNote = link != null ? Lang.T("The data file will be remembered when you save the label.")
                                    : Lang.T("The data file will no longer be remembered when you save the label.");
            return false;
        }

        // Only "printData" into the label's file (atomic, as Save). Not when the file on disk differs from the open label
        // (changed since it was opened or saved, or written by another version); false when it can't be read or written.
        bool WriteLinkOnly(PrintDataLink link)
        {
            try
            {
                string disk = File.ReadAllText(path, Encoding.UTF8);
                string a = PrintDataLink.WithoutLink(disk), b = PrintDataLink.WithoutLink(doc.ToJson(path));
                if (a == null || b == null || a != b) return false;
                string text = PrintDataLink.WithLink(disk, link, path);
                if (text == null) return false;
                LabelStore.WriteAtomic(path, text);
                return true;
            }
            catch (Exception)
            {
                try { File.Delete(path + ".tmp"); } catch (Exception) { }
                return false;
            }
        }

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
                // the tile is narrow: "Wrong roll · 29 mm continuous" (without "loaded", it was cut off); the tip has it all
                d.ShowPrinter(TileState.Warn, Health.Warning, Lang.T("Wrong roll") + (loaded != null ? " · " + loaded : ""),
                              Lang.T("Wrong roll") + (loaded != null ? " · " + Lang.F("{0} loaded", loaded) : "") + " – " + Lang.T("doesn't fit this label"));
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
