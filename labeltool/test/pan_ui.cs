// Panning with the middle mouse button in QL Label Maker, driven like a user: the
// middle-button messages posted as Windows sends them. Checks: at a high zoom a middle drag scrolls the view by the drag
// distance (over the label and over the grey area around it); the selection, text editing (caret, selection, focus),
// undo and the dirty flag stay as they were; a middle click without moving and Esc change nothing; without scroll bars
// nothing happens. Prints CHECK lines; changes nothing (nothing saved or printed). Runs only through deskrun.exe
// (a desktop of its own: no mouse, no focus taken).
// Run by pan-ui.ps1:  pan_ui.exe <de|en>  ->  CHECK lines
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class PanUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_KEYDOWN = 0x0100, MK_MBUTTON = 0x10;

        [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        static EditorForm form;
        static EditorCanvas canvas;
        static Panel host;
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
            Lang.Force(args[0]);
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);   // closes without asking to save
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] : "DONE " + args[0] + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        // a cursor image's real width (Cursor.Size is always Windows' cursor size)
        static int Px(Cursor c) { using (var i = Icon.FromHandle(c.Handle)) return i.Size.Width; }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static object Call(string method, params object[] a)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            return m.Invoke(form, a);
        }
        static int UndoCount() { return ((System.Collections.ICollection)typeof(UndoStack).GetField("undo", All).GetValue(F<UndoStack>("undo"))).Count; }
        static bool Dirty() { return F<bool>("dirty"); }
        static Point Scroll() { return new Point(-host.AutoScrollPosition.X, -host.AutoScrollPosition.Y); }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // A mouse message as Windows sends it: client coordinates of the window it goes to in lParam.
        static void Post(Control to, int msg, Point client, int keys)
        {
            PostMessage(to.Handle, msg, (IntPtr)keys, (IntPtr)((client.Y << 16) | (client.X & 0xFFFF)));
            Pump(60);
        }

        // Press the wheel at a point of `on`, move by `by` in steps (the moves go to the window holding the capture,
        // the host), release. Returns whether panning was on after the press.
        static bool Drag(Control on, Point at, Size by, bool release = true)
        {
            Post(on, WM_MBUTTONDOWN, at, MK_MBUTTON);
            bool panning = form.Panning;
            var start = host.PointToClient(on.PointToScreen(at));
            for (int i = 1; i <= 4; i++)
                Post(host, WM_MOUSEMOVE, new Point(start.X + by.Width * i / 4, start.Y + by.Height * i / 4), MK_MBUTTON);
            if (release) Post(host, WM_MBUTTONUP, new Point(start.X + by.Width, start.Y + by.Height), 0);
            return panning;
        }

        static void Run()
        {
            Pump(1500);
            canvas = F<EditorCanvas>("canvas");
            host = F<Panel>("host");
            form.ActiveControl = canvas;   // the keys go to the canvas (a test desktop has no foreground)

            // ---------- without scroll bars: nothing happens ----------
            Call("SetZoom", 0.0);   // fit
            Pump(300);
            Check(!host.HorizontalScroll.Visible && !host.VerticalScroll.Visible, "fit: no scroll bars");
            var s0 = Scroll();
            bool on = Drag(canvas, new Point(canvas.ClientSize.Width / 2, canvas.ClientSize.Height / 2), new Size(-60, -40));
            Check(!on && !form.Panning && Scroll() == s0, "no scroll bars: a middle drag does nothing (scroll " + Scroll() + ")");

            // ---------- a text in editing, zoomed in ----------
            var t = new TextObject { X = 4, Y = 4, W = 30, H = 12, Text = "Hallo Welt", Size = 12, Kind = TextKind.Line };
            EditorRender.FitText(t);
            Call("Add", t);
            Pump(100);
            canvas.Selected = t;
            Call("SetZoom", 800.0);
            Pump(300);
            Check(host.HorizontalScroll.Visible || host.VerticalScroll.Visible, "800 %: scroll bars (" +
                  (host.HorizontalScroll.Visible ? "H" : "") + (host.VerticalScroll.Visible ? "V" : "") + ")");
            canvas.BeginEdit(t);
            Pump(100);
            Check(canvas.Editing != null, "editing the text");
            int caret = canvas.Editing != null ? canvas.Editing.Caret : -1;
            int steps = UndoCount();
            bool dirty = Dirty();
            var focused = form.ActiveControl;

            // the view in the middle of its range, so a drag either way isn't stopped at an end
            var range = new Size(Math.Max(0, canvas.Width - host.ClientSize.Width), Math.Max(0, canvas.Height - host.ClientSize.Height));
            host.AutoScrollPosition = new Point(range.Width / 2, range.Height / 2);
            Pump(100);

            // ---------- a middle drag over the label: the view follows the hand 1:1 ----------
            var before = Scroll();
            var by = new Size(-60, -40);   // hand to the upper left: the view shows more of the lower right
            var mid = canvas.PointToClient(host.PointToScreen(new Point(host.ClientSize.Width / 2, host.ClientSize.Height / 2)));
            on = Drag(canvas, mid, by, false);
            Check(on && form.Panning, "middle button down at 800 %: panning");
            var move = EditorCursors.PanMove(form.DeviceDpi);
            int want = (int)Math.Round(32 * form.DeviceDpi / 96.0);
            Check(move != Cursors.SizeAll && Px(move) >= want && Px(move) <= Math.Max(64, want), "the move cross from pan-move.cur, " + Px(move) + " px at " + form.DeviceDpi + " dpi");
            Check(Cursor.Current.Handle == move.Handle, "panning shows the move cross");
            foreach (var d in new[] { 120, 144, 192 })
                Check(Px(EditorCursors.PanMove(d)) == d / 3, "move cross at " + d + " dpi: " + Px(EditorCursors.PanMove(d)) + " px");
            var during = Scroll();
            Post(host, WM_MBUTTONUP, Point.Empty, 0);
            bool okX = range.Width == 0 ? during.X == before.X : during.X - before.X == -by.Width;
            bool okY = range.Height == 0 ? during.Y == before.Y : during.Y - before.Y == -by.Height;
            Check(okX && okY, "the view moved by the drag distance: " + before + " -> " + during + " (drag " + by + ", range " + range + ")");
            Check(!form.Panning, "release ends panning");
            Check(Scroll() == during, "release doesn't move the view: " + Scroll());
            Check(canvas.Editing != null && canvas.EditedText == t && canvas.Editing.Caret == caret, "still editing, caret kept: " + (canvas.Editing != null ? canvas.Editing.Caret : -1));
            Check(canvas.Selected == t, "selection kept");
            Check(UndoCount() == steps && Dirty() == dirty, "no undo step, dirty flag as it was: " + (UndoCount() - steps) + ", " + Dirty());
            Check(form.ActiveControl == focused, "focus kept: " + (form.ActiveControl != null ? form.ActiveControl.GetType().Name : "-"));

            // ---------- the other way, over the grey area / the host ----------
            before = Scroll();
            by = new Size(50, 30);
            on = Drag(host, new Point(host.ClientSize.Width / 2, host.ClientSize.Height / 2), by);
            var after = Scroll();
            okX = range.Width == 0 ? after.X == before.X : after.X - before.X == -by.Width;
            okY = range.Height == 0 ? after.Y == before.Y : after.Y - before.Y == -by.Height;
            Check(on && okX && okY && !form.Panning, "middle drag on the area around the label: " + before + " -> " + after);

            // ---------- a middle click without moving ----------
            before = Scroll();
            Drag(canvas, mid, Size.Empty);
            Check(Scroll() == before && !form.Panning && canvas.Editing != null && canvas.Editing.Caret == caret, "middle click without moving: nothing changes");

            // ---------- Esc while dragging: ends it, editing goes on ----------
            Drag(canvas, mid, new Size(-20, 0), false);
            Check(form.Panning, "dragging again");
            PostMessage(canvas.Handle, WM_KEYDOWN, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
            Pump(60);
            Check(!form.Panning, "Esc ends panning");
            Check(canvas.Editing != null && canvas.EditedText == t, "Esc while panning doesn't end editing");
            var stay = Scroll();
            Post(host, WM_MOUSEMOVE, Point.Empty, 0);
            Post(host, WM_MBUTTONUP, Point.Empty, 0);
            Check(Scroll() == stay, "after Esc the mouse no longer moves the view");
            Check(UndoCount() == steps && Dirty() == dirty, "in all: no undo step, dirty flag as it was");
            canvas.EndEdit();
        }
    }
}
