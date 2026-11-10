// Label editor window (EditorForm.cs), part: one QL Label Maker window (2026-11-09; SingleInstance.cs). A second
// start (a .qllabel double-clicked, the Start menu, "Open with", QL Label Formats' "Open in QL Label Maker") brings this
// window to the front and hands it its command line (tabs 0.23, EditorForm.Tabs.cs):
//   - a file: opened in a tab of its own, no question; a file already open: its tab comes forward and blinks;
//   - --printer / --format without a file: a new label (tab) on that printer and label format;
//   - nothing: only to the front.
// While a dialog of this window is open (Save as, Print with data, a question) or a print is being sent, the window
// comes to the front and the file waits until that is over: no question on top of a dialog.
using System;
using System.IO;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        string[] startedAgain;   // the latest second start's arguments, waiting for a free moment
        Timer waitForFree;   // checks every 250 ms while a dialog is open

        internal void StartedAgain(string[] args)
        {
            if (args.Length == 0) return;   // only to the front (SingleInstance did that)
            startedAgain = args;
            AfterStartedAgain(null, EventArgs.Empty);
        }

        void AfterStartedAgain(object sender, EventArgs e)
        {
            if (startedAgain == null || IsDisposed) { StopWaiting(); return; }
            if (printing || loading || SingleInstance.Busy(this))
            {
                if (waitForFree == null) { waitForFree = new Timer { Interval = 250 }; waitForFree.Tick += AfterStartedAgain; waitForFree.Start(); }
                return;
            }
            StopWaiting();
            var args = startedAgain;
            startedAgain = null;
            string file, printerName, formName;
            EditorProgram.Parse(args, 0, out file, out printerName, out formName);
            var files = EditorProgram.FilePrinters(args, 0);
            if (files.Count > 0) { foreach (var f in files) OpenFile(f.Key, f.Value); return; }   // every file, each with its printer
            if (printerName == null && formName == null) return;
            if (!OnlyStartTab() && NewTab() == null) return;
            string p = printerName != null ? QlPrinters.Choose(printers, printerName) : printer;
            if (p != printer && p != null) { printer = p; FillPrinterBox(); }
            SetPrinter(printer, formName);
            CheckRoll();
            UpdateTitle();
        }

        void StopWaiting()
        {
            if (waitForFree == null) return;
            waitForFree.Dispose();
            waitForFree = null;
        }

        static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }
    }
}
