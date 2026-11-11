// Label editor window (EditorForm.cs), part: one QL Label Maker window (2026-11-09; SingleInstance.cs). A second
// start (a .qllabel double-clicked, the Start menu, "Open with", QL Label Formats' "Open in QL Label Maker") brings this
// window to the front and hands it its command line (tabs 0.23, EditorForm.Tabs.cs):
//   - a file: opened in a tab of its own, no question; a file already open: its tab comes forward and blinks;
//   - --printer / --format without a file: a new label (tab) on that printer and label format;
//   - nothing: only to the front;
//   - --print-data with a file (Explorer's "Print with data..."): its tab, then Print with data.
// While a dialog of this window is open (Save as, Print with data, a question) or a print is being sent, the window
// comes to the front and the file waits until that is over: no question on top of a dialog.
using System;
using System.IO;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // Second starts' arguments, waiting for a free moment, oldest first: several labels selected in Explorer start
        // one process each, and every one of them is opened (review 2026-11-11: only the last one was, when they arrived
        // while the window was busy).
        readonly System.Collections.Generic.Queue<string[]> startedAgain = new System.Collections.Generic.Queue<string[]>();
        Timer waitForFree;   // checks every 250 ms while a dialog is open

        internal void StartedAgain(string[] args)
        {
            if (args.Length == 0) return;   // only to the front (SingleInstance did that)
            startedAgain.Enqueue(args);
            AfterStartedAgain(null, EventArgs.Empty);
        }

        void AfterStartedAgain(object sender, EventArgs e)
        {
            while (true)
            {
                if (startedAgain.Count == 0 || IsDisposed) { StopWaiting(); return; }
                if (printing || loading || SingleInstance.Busy(this))
                {
                    if (waitForFree == null) { waitForFree = new Timer { Interval = 250 }; waitForFree.Tick += AfterStartedAgain; waitForFree.Start(); }
                    return;
                }
                StopWaiting();
                OneStart(startedAgain.Dequeue());
            }
        }

        void OneStart(string[] args)
        {
            string file, printerName, formName;
            EditorProgram.Parse(args, 0, out file, out printerName, out formName);
            var files = EditorProgram.FilePrinters(args, 0);
            if (files.Count > 0)
            {
                foreach (var f in files) OpenFile(f.Key, f.Value);   // every file, each with its printer
                if (EditorProgram.PrintData(args, 0)) PrintDataSoon(files[files.Count - 1].Key);
                return;
            }
            if (printerName == null && formName == null) return;
            if (!OnlyStartTab() && NewTab() == null) return;
            string p = printerName != null ? QlPrinters.Choose(printers, printerName) : printer;
            if (p != printer && p != null) { printer = p; FillPrinterBox(); }
            SetPrinter(printer, formName);
            CheckRoll();
            UpdateTitle();
        }

        // --print-data: Print with data on that label once the window is free (loaded, no dialog open); nothing when it
        // didn't open (the message about it said why).
        internal void PrintDataSoon(string file)
        {
            var t = new Timer { Interval = 100 };
            int tries = 0;
            t.Tick += delegate
            {
                if (IsDisposed) { t.Dispose(); return; }
                // as long as a dialog or question is open (never on top of it; review 2026-11-11: it did after 30 s);
                // a window that isn't shown within 30 s: not at all
                if (loading || printing || !Visible || SingleInstance.Busy(this))
                {
                    if (Visible || ++tries < 300) return;
                    t.Dispose();
                    return;
                }
                t.Dispose();
                // another label came forward meanwhile (several starts at once): this one's tab first, each in turn
                var tab = path != null && SamePath(path, file) ? current : TabOf(file);
                if (tab == null) return;
                if (tab != current) SwitchTo(tabs.IndexOf(tab));
                PrintWithData();
            };
            t.Start();
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
