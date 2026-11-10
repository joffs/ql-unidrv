// Real mixed-DPI check (needs a second screen with other scaling, e.g. the 150 % monitor; nothing is emulated): each
// window opens on the 100 % screen, is moved to the other screen and back, then opens directly on the other screen and
// is moved to the 100 % one. Windows sends its own WM_DPICHANGED on each move. After each step one line
// "STATE <window> <step> dpi=.. size=WxH client=WxH font=.." and a PrintWindow picture <out>\<window>-<step>.png.
// Runs only on deskrun's desktop (moves never take the focus there). Changes nothing: preview modes, no tab saved,
// nothing printed. dpi_move <out folder> <de|en>; built and run by dpi_move.ps1.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class DpiMove
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int thread);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int index, StringBuilder info, int length, out int needed);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, int flags);
        struct RECT { public int L, T, R, B; }
        const uint SWP_NOSIZE = 1, SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10;
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        static string outDir;

        static int DpiAt(Point p)
        {
            uint x, y;
            return GetDpiForMonitor(MonitorFromPoint(p, 2), 0, out x, out y) == 0 ? (int)x : 0;
        }

        [STAThread]
        static int Main(string[] args)
        {
            var name = new StringBuilder(256); int needed;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 512, out needed);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            outDir = args[0];
            Directory.CreateDirectory(outDir);
            Lang.Force(args[1]);
            InstallForm.Preview = true;
            SetupForm.NoSaveTab = true;
            Application.EnableVisualStyles();
            if (args.Length > 2 && args[2] == "close") return CloseCheck();

            Screen normal = null, other = null;
            foreach (var s in Screen.AllScreens)
            {
                int dpi = DpiAt(new Point(s.Bounds.X + 10, s.Bounds.Y + 10));
                Console.WriteLine("INFO screen " + s.DeviceName + " " + s.Bounds + " dpi=" + dpi);
                if (dpi == 96 && normal == null) normal = s;
                else if (dpi != 96 && other == null) other = s;
            }
            if (args.Length > 2 && args[2] == "same" && normal != null) other = normal;   // the run itself, without a second screen (harness test)
            if (normal == null || other == null) { Console.WriteLine("refused: needs a 100 % screen and one with other scaling"); return 2; }
            var a = new Point(normal.WorkingArea.X + 60, normal.WorkingArea.Y + 60);
            var b = new Point(other.WorkingArea.X + (other == normal ? 400 : 60), other.WorkingArea.Y + 60);
            string root = FakeRelease(Path.Combine(outDir, "release"));

            var windows = new List<KeyValuePair<string, Func<Form>>>
            {
                new KeyValuePair<string, Func<Form>>("install", () => new InstallForm(root, false)),
                new KeyValuePair<string, Func<Form>>("setup", () => new SetupForm()),
                new KeyValuePair<string, Func<Form>>("result", () => new SetupResult("QL Unidrv", ResultKind.Ok, Lang.T("Printer added"),
                    new[] { new KeyValuePair<string, string>(Lang.T("Printer"), "Brother QL-800 (Unidrv)") }, null)),
                new KeyValuePair<string, Func<Form>>("progress", () => (Form)Activator.CreateInstance(typeof(SetupProgress), All, null,
                    new object[] { "QL Unidrv", Lang.T("Printer setup"), null }, null)),
                new KeyValuePair<string, Func<Form>>("formats", () => new MainForm()),
                new KeyValuePair<string, Func<Form>>("status", () => new StatusForm(new[] { "QL-800 Capture" }, () => new[] { "QL-800 Capture" })),
                new KeyValuePair<string, Func<Form>>("about", () => (Form)Activator.CreateInstance(typeof(AboutForm), All, null,
                    new object[] { "QL Label Formats" }, null)),
                new KeyValuePair<string, Func<Form>>("editor", () => new EditorForm(null, "QL-800 Capture", null)),   // QL Label Maker (Label Maker's case)
                new KeyValuePair<string, Func<Form>>("printdata", PrintData),   // QL Label Maker's Print with data, with 20 rows
            };
            // QL_DPI_ONLY=printdata,editor: only these windows
            string only = Environment.GetEnvironmentVariable("QL_DPI_ONLY");
            if (!string.IsNullOrEmpty(only)) windows = windows.Where(w => only.Split(',').Contains(w.Key)).ToList();
            int failures = 0;
            foreach (var w in windows)
            {
                try
                {
                    Run(w.Key, w.Value, a, b, "open100", "on-other", "back100");
                    Run(w.Key, w.Value, b, a, "open-other", "moved100", null);
                }
                catch (Exception e) { Console.WriteLine("FAIL " + w.Key + ": " + (e.InnerException ?? e).Message); failures++; }
            }
            Console.WriteLine(failures == 0 ? "DONE" : "DONE " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        // QL Label Maker's "Print with data" dialog: a 62 mm label with two data fields and a CSV of 20 rows (previews).
        static Form PrintData()
        {
            var doc = new LabelDoc { FormName = "62mm", PaperId = 259 };
            doc.Objects.Add(new TextObject { X = 4, Y = 3, W = 50, H = 8, Text = "{Artikel}", Size = 14, Kind = TextKind.Line });
            doc.Objects.Add(new TextObject { X = 4, Y = 13, W = 50, H = 6, Text = "Nr. {Nummer}", Size = 10, Kind = TextKind.Line });
            foreach (TextObject t in doc.Objects) EditorRender.FitText(t);
            string error;
            var layout = LabelJob.LayoutFor(doc, "QL-800 Capture", out error);
            if (layout == null) throw new InvalidOperationException(error);
            var csv = new StringBuilder("Artikel;Nummer\r\n");
            for (int i = 1; i <= 20; i++) csv.Append("Schraube M" + (3 + i % 5) + " × " + (10 + i) + ";V-" + i.ToString("000") + "\r\n");
            string path = Path.Combine(outDir, "printdata.csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(true));
            return new PrintDataDialog(new PrintDataDialog.Options
            {
                Job = new LabelJob(doc, layout), TemplateName = "Regal.qllabel", LabelText = "62mm", PrinterText = "QL-800 Capture",
                DocumentName = "dpi_move", CsvPath = path,
            });
        }

        // "close": QL Label Formats opened and closed the normal way (WM_CLOSE, nothing changed by the test): closed within
        // 20 s, or which windows are still open (a question?). No second screen needed.
        static int CloseCheck()
        {
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), ShowInTaskbar = false };
            bool closed = false;
            form.FormClosed += delegate { closed = true; };
            form.Show();
            Pump(2000);
            var dirty = form.GetType().GetField("dirty", All);
            Console.WriteLine("INFO before close: dirty=" + (dirty == null ? "?" : dirty.GetValue(form)));
            PostMessage(form.Handle, 0x10 /* WM_CLOSE */, IntPtr.Zero, IntPtr.Zero);
            var until = DateTime.Now.AddSeconds(20);
            var t = new System.Windows.Forms.Timer { Interval = 20000 };
            t.Tick += delegate
            {
                t.Stop();
                foreach (Form f in Application.OpenForms) if (f != form) Console.WriteLine("INFO still open: " + f.GetType().Name + " \"" + f.Text + "\"");
                foreach (Form f in Application.OpenForms.Cast<Form>().ToList()) if (f != form) f.Close();
            };
            t.Start();
            while (!closed && DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(closed ? "CLOSE ok" : "CLOSE not closed after 20 s");
            if (!closed) { Pump(200); Console.Out.Flush(); Environment.Exit(3); }
            return 0;
        }
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        // Opens at 'from', moves to 'to' (and back to 'from' when 'back' is given), a STATE line and a picture after each.
        static void Run(string window, Func<Form> make, Point from, Point to, string opened, string moved, string back)
        {
            Console.WriteLine("INFO " + window + ": " + opened);
            // A step that hangs (e.g. opening directly on the other screen) ends the run with its name, not silently
            using (new System.Threading.Timer(delegate { Console.WriteLine("FAIL " + window + " " + opened + ": no end after 60 s (hangs)"); Console.Out.Flush(); Environment.Exit(3); }, null, 60000, Timeout.Infinite))
                RunSteps(window, make, from, to, opened, moved, back);
        }

        static void RunSteps(string window, Func<Form> make, Point from, Point to, string opened, string moved, string back)
        {
            var form = make();
            form.StartPosition = FormStartPosition.Manual;
            form.Location = from;
            form.ShowInTaskbar = false;
            form.Show();
            Pump(1500);
            Record(form, window, opened);
            Move(form, to); Record(form, window, moved);
            if (back != null) { Move(form, from); Record(form, window, back); }
            // No "unsaved changes?" question on close (it would wait unseen on this desktop; as formatsweep.cs)
            var dirty = form.GetType().GetField("dirty", All);
            if (dirty != null && dirty.FieldType == typeof(bool)) dirty.SetValue(form, false);
            // A dialog that still opens: fail instead of hanging
            using (new System.Threading.Timer(delegate { Console.WriteLine("FAIL " + window + ": closing it hangs (a dialog?)"); Console.Out.Flush(); Environment.Exit(3); }, null, 30000, Timeout.Infinite))
                form.Close();
            form.Dispose();
            Pump(200);
        }

        static void Move(Form form, Point p)
        {
            SetWindowPos(form.Handle, IntPtr.Zero, p.X, p.Y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
            Pump(1500);
        }

        static void Record(Form form, string window, string step)
        {
            RECT r; GetWindowRect(form.Handle, out r);
            Console.WriteLine("STATE " + window + " " + step + " dpi=" + form.DeviceDpi + " size=" + (r.R - r.L) + "x" + (r.B - r.T) +
                              " client=" + form.ClientSize.Width + "x" + form.ClientSize.Height + " font=" + form.Font.SizeInPoints.ToString("0.##") +
                              (form.Height > 690 * form.DeviceDpi / 96 ? " TALL" : ""));
            using (var bmp = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    IntPtr dc = g.GetHdc();
                    PrintWindow(form.Handle, dc, 2);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(Path.Combine(outDir, window + "-" + step + ".png"), ImageFormat.Png);
            }
        }

        static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
        }

        // A release folder for the install window's preview (as setup_theme.cs)
        static string FakeRelease(string dir)
        {
            foreach (var arch in new[] { "arm64", "amd64" })
            {
                Directory.CreateDirectory(Path.Combine(dir, @"out\package\" + arch));
                File.WriteAllText(Path.Combine(dir, @"out\package\" + arch + @"\QLUnidrv.dll"), "");
            }
            Directory.CreateDirectory(Path.Combine(dir, "driver"));
            File.WriteAllText(Path.Combine(dir, @"driver\install.ps1"), "");
            File.WriteAllText(Path.Combine(dir, @"out\package\qlunidrv.inf"),
                              "[Version]\r\nDriverVer=11/01/2026," + (Versions.InstalledDriver() ?? "0.18.304.1114") + "\r\n");
            return dir;
        }
    }
}
