// QL Label Formats window (MainForm.cs), part: one window (2026-11-09; SingleInstance.cs). A second start without
// arguments (Start menu, the tray menu) only brings this window to the front; with --printer (Printer setup's test
// print, QL Label Maker's "Open QL Label Formats") that printer is chosen here, and --new-format adds the new format
// from QL Label Maker's "Save as label format". While a dialog is open (Save & apply, a question) that waits until it
// is closed. The command modes (--apply, --sync, --plan, --remove-forms, ...) never come here.
using System;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class MainForm
    {
        string[] startedAgain;
        Timer waitForFree;   // checks every 250 ms while a dialog is open

        internal void StartedAgain(string[] args)
        {
            if (args.Length < 2 || args[0] != "--printer") return;   // only to the front
            startedAgain = args;
            AfterStartedAgain(null, EventArgs.Empty);
        }

        void AfterStartedAgain(object sender, EventArgs e)
        {
            if (startedAgain == null || IsDisposed) { StopWaiting(); return; }
            if (busy || loading || SingleInstance.Busy(this))
            {
                if (waitForFree == null) { waitForFree = new Timer { Interval = 250 }; waitForFree.Tick += AfterStartedAgain; waitForFree.Start(); }
                return;
            }
            StopWaiting();
            var args = startedAgain;
            startedAgain = null;
            UseTestPrinter(args[1]);
            int paperId, length01;
            if (args.Length > 4 && args[2] == "--new-format" && int.TryParse(args[3], out paperId) && int.TryParse(args[4], out length01))
                NewFormat(paperId, length01 / 10.0);
        }

        void StopWaiting()
        {
            if (waitForFree == null) return;
            waitForFree.Dispose();
            waitForFree = null;
        }
    }
}
