// QL Label Maker and QL Label Formats remember their window size and place (WindowBounds.cs): for both windows the
// first start without a value (the default placement), moved and resized then closed and opened again (the same
// bounds), maximized (opens maximized, restoring gives the saved normal bounds), minimized (opens normal), a saved place
// off every screen (the default placement) and one larger than the working area (made to fit). Prints CHECK lines.
// Uses a registry key of its own (removed afterwards): the user's values stay untouched. Runs only through deskrun.exe
// (a desktop of its own: no mouse, no focus taken). A watchdog ends it after 120 s.
// Run by window-bounds-ui.ps1:  window_bounds_ui.exe <de|en>
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels.Tests
{
    static class WindowBoundsUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(120000);
                Console.WriteLine("CHECK watchdog: the test ended within 120 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args.Length > 0 ? args[0] : "en");
            Application.EnableVisualStyles();
            Check(WindowBounds.Key == null, "a test exe (no title) leaves the user's value alone by default");
            WindowBounds.Key = Lang.SettingsKey + @"\UiTest-" + Guid.NewGuid().ToString("N");
            try
            {
                Run("maker", WindowBounds.LabelMaker);
                Run("formats", WindowBounds.LabelFormats);
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
            finally
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(WindowBounds.Key, false); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "DONE " + Lang.Current : "DONE " + Lang.Current + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static Form Open(string kind)
        {
            Form f = kind == "maker" ? (Form)new EditorForm(null, "QL-800 Capture", "62mm") : new MainForm();
            f.ShowInTaskbar = false;
            f.Show();
            Pump(500);
            return f;
        }

        static void Shut(Form f)
        {
            var dirty = f.GetType().GetField("dirty", All);
            if (dirty != null) dirty.SetValue(f, false);
            f.Close();
            Pump(150);
            f.Dispose();
        }

        static string Value(string name)
        {
            using (var k = Registry.CurrentUser.OpenSubKey(WindowBounds.Key)) return k == null ? null : k.GetValue(name) as string;
        }

        static void Write(string name, string value)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(WindowBounds.Key)) k.SetValue(name, value);
        }

        static void Run(string kind, string name)
        {
            // first start: no value, the default placement (centred), nothing changed
            var f = Open(kind);
            Rectangle area = Screen.FromControl(f).WorkingArea, first = f.Bounds;
            int dpi = f.DeviceDpi;
            Check(Value(name) == null && f.StartPosition == FormStartPosition.CenterScreen && f.WindowState == FormWindowState.Normal,
                  kind + ": first start without a value: default placement " + first);
            // moved and resized, closed, opened again: the same bounds
            var moved = new Rectangle(area.Left + 40, area.Top + 30, first.Width - 120, first.Height - 60);
            f.Bounds = moved;
            Pump(100);
            moved = f.Bounds;
            Shut(f);
            string saved = Value(name);
            Check(saved != null, kind + ": saved on close: " + saved);
            f = Open(kind);
            Check(f.Bounds == moved && f.WindowState == FormWindowState.Normal, kind + ": opens with the saved bounds " + f.Bounds + " (" + moved + ")");
            // maximized: opens maximized, restoring gives the saved normal bounds
            f.WindowState = FormWindowState.Maximized;
            Pump(150);
            Shut(f);
            Check(Value(name) != null && Value(name).EndsWith(",1"), kind + ": maximized saved with its normal bounds: " + Value(name));
            f = Open(kind);
            Check(f.WindowState == FormWindowState.Maximized, kind + ": opens maximized");
            f.WindowState = FormWindowState.Normal;
            Pump(150);
            Check(f.Bounds == moved, kind + ": restore gives the saved normal bounds " + f.Bounds);
            // minimized when closed: opens normal with its normal bounds
            f.WindowState = FormWindowState.Minimized;
            Pump(150);
            Shut(f);
            f = Open(kind);
            Check(f.WindowState == FormWindowState.Normal && f.Bounds == moved, kind + ": closed minimized, opens normal " + f.Bounds);
            Shut(f);
            // a place off every screen (a screen unplugged): the default placement
            Write(name, "-30000,-30000,900,600,0");
            f = Open(kind);
            Check(f.StartPosition == FormStartPosition.CenterScreen && f.Size == first.Size, kind + ": saved off-screen: default placement " + f.Bounds);
            Shut(f);
            // larger than the working area (another scaling, a smaller screen), mostly on it: made to fit
            Write(name, area.Left + "," + area.Top + "," + (area.Width * 96 * 13 / 10 / dpi) + "," + (area.Height * 96 / dpi + 50) + ",0");
            f = Open(kind);
            var b = f.Bounds;
            Check(b.Width <= area.Width + 16 && b.Height <= area.Height + 8 && area.IntersectsWith(b) && b.Top >= area.Top,
                  kind + ": larger than the working area " + area + ": fitted " + b);
            Shut(f);
            // a damaged value: the default placement
            Write(name, "abc");
            f = Open(kind);
            Check(f.StartPosition == FormStartPosition.CenterScreen && f.Size == first.Size, kind + ": damaged value: default placement");
            Shut(f);
        }
    }
}
