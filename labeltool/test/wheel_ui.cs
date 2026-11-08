// Mouse-wheel zoom in QL Label Maker, driven like a user: wheel messages posted as Windows sends them, over the label,
// over the grey area around it while a number field in the panel has the focus, over the panel (no zoom), and a tilted
// wheel (left/right). Prints CHECK lines; changes nothing (nothing saved or printed). Runs only through deskrun.exe
// (a desktop of its own: no mouse, no focus taken).
// Run by wheel-ui.ps1:  wheel_ui.exe <de|en>  ->  CHECK lines
using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class WheelUi
    {
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;

        static EditorForm form;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            Lang.Force(args[0]);
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] : "DONE " + args[0] + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static double Zoom() { return (double)typeof(EditorForm).GetProperty("CurrentZoom", All).GetValue(form, null); }

        // A wheel message as Windows sends it: to the given window, the pointer's screen position in lParam.
        static void Wheel(Control to, Point screen, int delta, bool tilt = false)
        {
            var w = (IntPtr)(delta << 16);
            var l = (IntPtr)((screen.Y << 16) | (screen.X & 0xFFFF));
            PostMessage(to.Handle, tilt ? WM_MOUSEHWHEEL : WM_MOUSEWHEEL, w, l);
            Pump(250);
        }

        static void Run()
        {
            Pump(1500);
            var canvas = F<EditorCanvas>("canvas");
            var host = F<Panel>("host");
            var props = F<TableLayoutPanel>("props");

            // Over the label, the canvas has the focus: wheel up zooms in around the pointer (the point stays put).
            var screen = canvas.PointToScreen(new Point(canvas.ClientSize.Width / 2 + 40, canvas.ClientSize.Height / 2 + 10));
            double before = Zoom();
            canvas.Focus();
            Wheel(canvas, screen, 120);
            Check(Zoom() > before, "wheel up over the label zooms in: " + before.ToString("0") + " % -> " + Zoom().ToString("0") + " %");
            // once the label is larger than the view (room to scroll), the point under the pointer stays put
            Wheel(canvas, screen, 120);
            Wheel(canvas, screen, 120);
            var mm = canvas.ClientToMm(canvas.PointToClient(screen));
            Wheel(canvas, screen, 120);
            double after = Zoom();
            var back = canvas.PointToScreen(Point.Round(canvas.MmToClient(mm)));
            // (only along an axis the view can scroll: a label shorter than the view stays centred)
            bool okX = !host.HorizontalScroll.Visible || Math.Abs(back.X - screen.X) <= 2, okY = !host.VerticalScroll.Visible || Math.Abs(back.Y - screen.Y) <= 2;
            Check(okX && okY && host.HorizontalScroll.Visible, "the point under the pointer stays: " + back + " vs " + screen +
                  " (scroll bars " + (host.HorizontalScroll.Visible ? "H" : "") + (host.VerticalScroll.Visible ? "V" : "") + ")");
            Wheel(canvas, screen, -120);
            Check(Zoom() < after, "wheel down zooms out: " + Zoom().ToString("0") + " %");

            // Over the grey area around the label, while a number field in the panel has the focus.
            Func<Control, System.Collections.Generic.IEnumerable<Control>> all = null;
            all = c => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(all(x)));
            var number = all(props).OfType<NumericUpDown>().FirstOrDefault(n => n.Visible);
            Check(number != null, "a number field in the panel");
            if (number == null) return;
            decimal value = number.Value;
            number.Focus();
            Pump(100);
            before = Zoom();
            var grey = host.PointToScreen(new Point(8, 8));
            Wheel(number, grey, 120);   // without "scroll inactive windows" Windows sends the wheel to the focused field
            Check(Zoom() > before && number.Value == value, "over the grey area with a focused number field: zoom " + before.ToString("0") + " -> " + Zoom().ToString("0") + " %, field kept " + number.Value);

            // Over the panel: no zoom (the panel scrolls its own contents).
            before = Zoom();
            Wheel(props, props.PointToScreen(new Point(10, 10)), 120);
            Check(Math.Abs(Zoom() - before) < 0.01, "wheel over the panel doesn't zoom: " + Zoom().ToString("0") + " %");

            // A tilted wheel scrolls left/right, once the label is wider than the view; it doesn't zoom.
            for (int i = 0; i < 6; i++) Wheel(canvas, screen, 120);
            int x0 = host.AutoScrollPosition.X;
            double z = Zoom();
            Wheel(canvas, screen, 120, true);
            int x1 = host.AutoScrollPosition.X;
            Check(x1 < x0, "tilted wheel scrolls right: " + x0 + " -> " + x1);
            Check(Math.Abs(z - Zoom()) < 0.01, "tilt doesn't zoom: " + z.ToString("0") + " %");
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
