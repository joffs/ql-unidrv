// Architecture check (ARM64 natively vs. x64 emulation): runs the paths whose code changes with the process
// architecture - QL Label Formats' test print (the same print code as MainForm.TestPrint), Windows' printing
// preferences of a printer (rundll32 printui, as Printer setup opens it), and the printer status read (PrinterCheck,
// as the status window and the tray monitor do). Run by arch-check.ps1 through deskrun.exe only (refuses the normal
// desktop). Prints, as its first line, the machine it runs as. Nothing is saved, applied or installed; "print" prints
// on the given printer (a capture printer).
//   arch_check print <printer> <label format name>
//   arch_check prefs <printer> <png>          the preferences window, saved as PNG, then closed
//   arch_check status <printer>               the status as the programs show it (no serial number)
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class ArchCheck
    {
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
        [DllImport("kernel32.dll")] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
        [DllImport("kernel32.dll")] static extern bool GetProcessInformation(IntPtr process, int cls, out ProcessMachineInfo info, int size);
        [StructLayout(LayoutKind.Sequential)] struct ProcessMachineInfo { public ushort Machine; public ushort Reserved; public uint Attributes; }
        delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc f, IntPtr lParam);
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder b, int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, int flags);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        static string MachineOf(Process p)
        {
            ProcessMachineInfo i;
            if (!GetProcessInformation(p.Handle, 9, out i, 8)) return "?";
            return i.Machine == 0xAA64 ? "ARM64" : i.Machine == 0x8664 ? "x64" : i.Machine.ToString("X");
        }

        [STAThread]
        static int Main(string[] args)
        {
            var name = new StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            Console.WriteLine("machine " + MachineOf(Process.GetCurrentProcess()));
            var watchdog = new System.Threading.Timer(_ => { Console.WriteLine("timeout"); Environment.Exit(3); }, null, 90000, Timeout.Infinite);
            try
            {
                switch (args[0])
                {
                    case "print": return Print(args[1], args[2]);
                    case "prefs": return Prefs(args[1], args[2]);
                    case "status": return Status(args[1]);
                }
            }
            catch (Exception ex) { Console.WriteLine("error " + ex.GetType().Name + ": " + ex.Message); return 1; }
            finally { watchdog.Dispose(); }
            Console.WriteLine("unknown mode");
            return 1;
        }

        // MainForm.TestPrint's print part, for a format of the saved list.
        static int Print(string printer, string format)
        {
            string unreadable;
            var f = LabelStore.LoadCurrent(out unreadable).Formats.FirstOrDefault(x => x.Name == format);
            if (f == null) { Console.WriteLine("no format " + format); return 1; }
            LabelStore.Normalize(f);
            var doc = new PrintDocument { DocumentName = Lang.T("QL label format test:") + " " + f.Name };
            doc.PrinterSettings.PrinterName = printer;
            var size = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                .FirstOrDefault(p => string.Equals(p.PaperName, f.Name, StringComparison.OrdinalIgnoreCase));
            if (size == null) { Console.WriteLine("paper size not listed: " + f.Name); return 1; }
            doc.DefaultPageSettings.PaperSize = size;
            doc.DefaultPageSettings.Landscape = false;
            doc.PrintController = new StandardPrintController();
            doc.PrintPage += (s, e) => TestLabel.DrawSample(e, f);
            doc.Print();
            Console.WriteLine("printed " + f.Name + " (" + doc.PrinterSettings.PaperSizes.Count + " paper sizes listed)");
            return 0;
        }

        // Windows' printing preferences of the printer, started as Printer setup starts its dialogs (rundll32 printui).
        static int Prefs(string printer, string png)
        {
            var p = Process.Start(new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /e /n \"" + printer + "\"") { UseShellExecute = false });
            Console.WriteLine("rundll32 " + MachineOf(p));
            IntPtr found = IntPtr.Zero;
            var end = DateTime.Now.AddSeconds(30);
            while (found == IntPtr.Zero && DateTime.Now < end && !p.HasExited)
            {
                Thread.Sleep(300);
                EnumWindows((h, l) =>
                {
                    int pid; GetWindowThreadProcessId(h, out pid);
                    if (pid == p.Id && IsWindowVisible(h)) { found = h; return false; }
                    return true;
                }, IntPtr.Zero);
            }
            if (found == IntPtr.Zero) { Console.WriteLine("no window" + (p.HasExited ? " (rundll32 exited " + p.ExitCode + ")" : "")); return 1; }
            Thread.Sleep(2500);   // the pages fill in
            var title = new StringBuilder(256); GetWindowText(found, title, 256);
            RECT r; GetWindowRect(found, out r);
            using (var bmp = new Bitmap(Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top)))
            {
                using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(found, dc, 2); g.ReleaseHdc(dc); }
                bmp.Save(png, ImageFormat.Png);
            }
            Console.WriteLine("window '" + title + "' " + (r.Right - r.Left) + "x" + (r.Bottom - r.Top));
            PostMessage(found, 0x0010, IntPtr.Zero, IntPtr.Zero);   // WM_CLOSE: Cancel, nothing saved
            if (!p.WaitForExit(10000)) { p.Kill(); Console.WriteLine("rundll32 killed"); }
            return 0;
        }

        static int Status(string printer)
        {
            var c = PrinterCheck.Run(printer, true);
            Console.WriteLine(c == null ? "no check" : "status " + c.Health + ": " + c.Short);
            return c == null ? 1 : 0;
        }
    }
}
