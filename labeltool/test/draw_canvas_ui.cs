// QL Label Maker: drawing objects with the mouse (EditorCanvas.Draw.cs, EditorForm.Draw.cs; draw tools 0.23).
// Checks: the arrow at the start; a tool switched on (ToolChanged, crosshair); a line dragged from A to Z (also rising),
// the tool staying on; a plain click = the default size at the click point; a rectangle in the dragged area; Shift: a
// line horizontal / at 45°, a box square, a triangle regular; Shift pressed and released during a drag switches at once
// (no mouse move; also kept for the release); a shape tool's outline is the shape itself; a drag starting on the grey
// area; Esc cancels a drag (no object, the tool stays), then ends a text edit, then goes back to the arrow; paragraph
// text = a frame of the dragged size, empty, with the caret in it; artistic text = the dragged height as its line; a
// drawn text left empty goes (Esc, a click beside it, the arrow; also typed and deleted again), typed in it stays
// selected (Esc, the arrow); the arrow ends editing; the text tool on a text edits it; barcode and QR code in the area
// (QR square); a picture in the area in its proportions, then the arrow; one undo step per drawn object; Insert (Enter
// on a tool, the menu, the keys) still inserts and keeps the arrow, a text with its placeholder. Snapping while drawing
// (the end on the centre lines, the guides; with Shift the line stays level; Alt held mid-drag: free at once); the right
// button cancels a drag (no object, no menu, the tool stays). The other checks draw with Alt held (no snapping).
// Prints CHECK lines. Works in a folder of its own under %TEMP% (removed afterwards). Runs only through deskrun.exe (a
// desktop of its own: no mouse, no focus taken; the mouse is simulated through the canvas's handlers). A watchdog ends
// it after 120 s. With 150 as the fourth argument the window opens on a screen at 150 % (144 dpi); without one that run
// is skipped.
// Run by draw-canvas-ui.ps1:  draw_canvas_ui.exe <de|en> <light|dark> <picture folder> <100|150>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class DrawCanvasUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static int failures, toolChanges;
        static string scheme, folder;

        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        // A bare Alt press or release as Windows sends it to the focused canvas (WM_SYSKEYDOWN/UP, VK_MENU)
        static void AltKey(bool down)
        {
            PostMessage(Canvas.Handle, down ? 0x0104 : 0x0105, (IntPtr)0x12, (IntPtr)unchecked((int)(down ? 0x20380001u : 0xE0380001u)));
            Pump(60);
        }

        // Windows' Alt alone asks the window for its menu (WM_SYSCOMMAND, SC_KEYMENU): counted here and kept from the
        // window (on the test's desktop it would enter a menu loop that never ends)
        sealed class KeyMenuSpy : NativeWindow
        {
            public int Count;
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x0112 && ((int)m.WParam.ToInt64() & 0xFFF0) == 0xF100) { Count++; return; }
                base.WndProc(ref m);
            }
        }
        static string dpiName = "100";

        static int Dpi(Screen s)
        {
            uint x, y;
            var m = MonitorFromPoint(new Point(s.Bounds.X + 1, s.Bounds.Y + 1), 2);
            return GetDpiForMonitor(m, 0, out x, out y) == 0 ? (int)x : 96;
        }

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
            scheme = args[1];
            Theme.Force(scheme == "dark" ? Scheme.Dark : Scheme.Light);
            string key = RecentLabels.Key, lastKey = QlPrinters.LastKey;
            RecentLabels.Key = Lang.SettingsKey + @"\UiTest-" + Guid.NewGuid().ToString("N");
            QlPrinters.LastKey = RecentLabels.Key;
            folder = Path.Combine(Path.GetTempPath(), "ql-draw-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            if (args.Length > 3) dpiName = args[3];
            var screen = Screen.AllScreens.FirstOrDefault(x => Dpi(x) == (dpiName == "150" ? 144 : 96));
            if (screen == null)
            {
                Console.WriteLine("INFO no screen at " + dpiName + " % attached: this run is skipped");
                Console.WriteLine("DONE " + args[0] + " " + scheme + " " + dpiName);
                return 0;
            }
            Cursor.Position = new Point(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 40);
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(screen.WorkingArea.X + 20, screen.WorkingArea.Y + 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                foreach (var t in F<List<LabelTab>>("tabs")) t.Dirty = false;
                form.Close();
            };
            try
            {
                form.Show();
                while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(RecentLabels.Key, false); } catch (Exception) { }
                RecentLabels.Key = key;
                QlPrinters.LastKey = lastKey;
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + scheme + " " + dpiName : "DONE " + args[0] + " " + scheme + " " + dpiName + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + scheme + " " + dpiName + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static void SetF(string field, object v) { typeof(EditorForm).GetField(field, All).SetValue(form, v); }
        static object Call(string method, params object[] a)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length >= a.Length &&
                                                                  x.GetParameters().Skip(a.Length).All(q => q.IsOptional));
            return m.Invoke(form, a.Concat(m.GetParameters().Skip(a.Length).Select(q => q.DefaultValue)).ToArray());
        }
        static object CanvasCall(string method, params object[] a)
        {
            var m = typeof(EditorCanvas).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            return m.Invoke(Canvas, a);
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static LabelDoc Doc { get { return F<LabelDoc>("doc"); } }
        static EditorCanvas Canvas { get { return F<EditorCanvas>("canvas"); } }
        static int Count { get { return Doc.Objects.Count; } }
        static LabelObject Last { get { return Doc.Objects[Doc.Objects.Count - 1]; } }
        static EditorTool Tool(string id) { return ((List<EditorTool>)Call("InsertTools")).First(t => t.Id == id); }
        static void Select(string id) { form.SelectTool(id == null ? null : Tool(id)); Pump(30); }

        static Point Px(double x, double y) { var p = Canvas.MmToClient(new PointF((float)x, (float)y)); return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)); }

        static void Mouse(string what, Point p, int clicks = 1)
        {
            CanvasCall(what, new MouseEventArgs(MouseButtons.Left, clicks, p.X, p.Y, 0));
        }

        static void Right(string what, Point p) { CanvasCall(what, new MouseEventArgs(MouseButtons.Right, 1, p.X, p.Y, 0)); }

        static float[] SnapLines(string which) { return (float[])typeof(EditorCanvas).GetProperty(which, All).GetValue(Canvas); }
        static float? Guide(string which) { return (float?)typeof(EditorCanvas).GetField(which, All).GetValue(Canvas); }

        // A drag from a to z (mm) in a few steps, as the mouse would.
        static void DragMm(double ax, double ay, double zx, double zy)
        {
            Point a = Px(ax, ay), z = Px(zx, zy);
            Mouse("OnMouseDown", a);
            for (int i = 1; i <= 4; i++) Mouse("OnMouseMove", new Point(a.X + (z.X - a.X) * i / 4, a.Y + (z.Y - a.Y) * i / 4));
            Mouse("OnMouseUp", z);
            Pump(30);
        }

        static void ClickMm(double x, double y)
        {
            var p = Px(x, y);
            Mouse("OnMouseDown", p);
            Mouse("OnMouseUp", p);
            Pump(30);
        }

        static void Esc() { CanvasCall("OnKeyDown", new KeyEventArgs(Keys.Escape)); Pump(30); }

        // The keyboard's Shift as the canvas reads it (EditorCanvas.ShiftState): set here, no key message posted.
        static bool shiftDown, snapOn;
        static void HoldShift(bool down) { shiftDown = down; Pump(150); }   // the drag's timer notices it

        // Shift pressed or released, as the window sees it (KeyPreview)
        static void ShiftKey(bool down)
        {
            var e = new KeyEventArgs(down ? Keys.ShiftKey | Keys.Shift : Keys.ShiftKey);
            typeof(EditorForm).GetMethod(down ? "OnKeyDown" : "OnKeyUp", All, null, new[] { typeof(KeyEventArgs) }, null).Invoke(form, new object[] { e });
        }

        static void StartEdit(TextObject t) { Canvas.BeginEdit(t); Pump(30); }

        static Point DrawAt { get { return (Point)typeof(EditorCanvas).GetField("drawAt", All).GetValue(Canvas); } }

        // The drawing outline as painted now (a bitmap of the canvas's size): is the pixel at p in the accent colour?
        static bool Outlined(Point p)
        {
            using (var b = new Bitmap(Canvas.ClientSize.Width, Canvas.ClientSize.Height))
            {
                using (var g = Graphics.FromImage(b)) { g.Clear(Color.White); CanvasCall("PaintDrawing", g); }
                for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = p.X + dx, y = p.Y + dy;
                        if (x >= 0 && y >= 0 && x < b.Width && y < b.Height && b.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) return true;
                    }
                return false;
            }
        }

        static int UndoCount { get { var u = F<UndoStack>("undo"); return ((System.Collections.IList)typeof(UndoStack).GetField("undo", All).GetValue(u)).Count; } }

        static bool Near(double a, double b, double tol = 0.25) { return Math.Abs(a - b) <= tol; }
        static string Box(LabelObject o) { return o.X.ToString("0.0") + "," + o.Y.ToString("0.0") + " " + o.W.ToString("0.0") + "×" + o.H.ToString("0.0"); }

        static void Run()
        {
            Pump(300);
            EditorCanvas.ShiftState = () => shiftDown;
            EditorCanvas.AltState = () => !snapOn;   // free unless a check turns snapping on
            form.ToolChanged += () => toolChanges++;
            Check(form.ActiveTool == null && Canvas.Drawing == DrawKind.None, "the start: the arrow");
            Check(Lang.T("Empty label. Choose a tool on the left and drag on the label.\nHatched: the printer can't print there.").Contains(Lang.Current == "de" ? "im Etikett ziehen" : "drag on the label"),
                  "the empty label's hint: choose a tool and drag");

            // ---------- line: from A to Z, the tool stays ----------
            Select("line");
            Check(toolChanges == 1 && form.ActiveTool == Tool("line") && Canvas.Drawing == DrawKind.Line && Canvas.Cursor == Cursors.Cross, "the line tool on: ToolChanged, crosshair");
            int n = Count;
            int statuses = 0;
            form.DrawStatusChanged += () => statuses++;
            Mouse("OnMouseDown", Px(5, 5));
            Mouse("OnMouseMove", Px(25, 15));
            var m = form.Measure;
            Check(m != null && m.Tool == Tool("line") && Near(m.Length, Math.Sqrt(500), 0.4) && Near(m.Angle, 153.4, 1.5) && statuses >= 2,
                  "while dragging: the measure for the status bar (" + (m != null ? m.Length.ToString("0.0") + " mm, " + m.Angle.ToString("0.0") + "°" : "none") + ")");
            Mouse("OnMouseUp", Px(25, 15));
            Pump(30);
            Check(form.Measure == null, "after the release: no measure");
            var line = Last as LineObject;
            Check(Count == n + 1 && line != null && Near(line.Start.X, 5) && Near(line.Start.Y, 5) && Near(line.End.X, 25) && Near(line.End.Y, 15),
                  "a line from A to Z as dragged (" + (line != null ? Box(line) : "none") + ")");
            Check(form.ActiveTool == Tool("line") && Canvas.Selected == line, "the tool stays on, the new line selected");
            DragMm(5, 20, 25, 10);
            line = Last as LineObject;
            Check(Count == n + 2 && line != null && line.Rising && Near(line.Start.X, 5) && Near(line.Start.Y, 20) && Near(line.End.Y, 10), "a second line right after, rising");
            Call("Undo", false);
            Check(Count == n + 1, "one undo step removes the last drawn line");
            ClickMm(10, 30);
            line = Last as LineObject;
            Check(Count == n + 2 && line != null && Near(line.X, 10) && Near(line.Y, 30) && line.H == 0 && line.W >= 5, "a click: a line of the default length at the click point (" + Box(line) + ")");

            // ---------- Shift ----------
            Point from = Px(10, 10);
            typeof(EditorCanvas).GetField("drawDownMm", All).SetValue(Canvas, Canvas.ClientToMm(from));
            var flat = (Point)CanvasCall("Constrained", new Point(from.X + 100, from.Y + 10), true);
            var diag = (Point)CanvasCall("Constrained", new Point(from.X + 100, from.Y + 90), true);
            Check(flat.Y == from.Y && diag.X - from.X == diag.Y - from.Y, "Shift: a line horizontal, or at 45° (" + flat + ", " + diag + ")");
            Select("rectangle");
            typeof(EditorCanvas).GetField("drawDownMm", All).SetValue(Canvas, Canvas.ClientToMm(from));
            var sq = (Point)CanvasCall("Constrained", new Point(from.X + 80, from.Y - 30), true);
            Check(sq.X - from.X == 80 && from.Y - sq.Y == 80, "Shift: a box square (" + sq + ")");
            Select("triangle");
            typeof(EditorCanvas).GetField("drawDownMm", All).SetValue(Canvas, Canvas.ClientToMm(from));
            var tri = (Point)CanvasCall("Constrained", new Point(from.X + 80, from.Y + 30), true);
            double k = Canvas.DrawShape.NaturalRatio;
            Check(Canvas.DrawShape.Kind == ShapeKind.Triangle && Math.Abs((tri.X - from.X) / (double)(tri.Y - from.Y) - k) < 0.03 && Math.Abs(k - 1.155) < 0.01,
                  "Shift: a triangle regular (" + (tri.X - from.X) + "×" + (tri.Y - from.Y) + ", " + k.ToString("0.000") + ")");

            // ---------- Shift pressed and released during a drag ----------
            Select("line");
            n = Count;
            statuses = 0;
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(30, 13));
            var slope = DrawAt;
            int before = statuses;
            HoldShift(true);
            m = form.Measure;
            Check(DrawAt.Y == Px(10, 10).Y && DrawAt != slope && statuses > before && m != null && (Near(m.Angle, 0, 0.5) || Near(m.Angle, 180, 0.5)),
                  "Shift pressed mid-drag (key state only, no key message, no mouse move): the line horizontal, the measure too (" + (m != null ? m.Angle.ToString("0.0") + "°" : "none") + ")");
            before = statuses;
            HoldShift(false);
            Check(DrawAt == Px(30, 13) && statuses > before && form.Measure != null && !Near(form.Measure.Angle, 0, 0.5), "Shift released (key state only): back as the mouse is");
            ShiftKey(true);
            Check(DrawAt.Y == Px(10, 10).Y, "the window's Shift key message: the line horizontal at once too");
            ShiftKey(false);
            HoldShift(true);
            Mouse("OnMouseUp", Px(30, 13));
            Pump(30);
            HoldShift(false);
            line = Last as LineObject;
            Check(Count == n + 1 && line != null && line.H == 0 && Near(line.W, 20, 0.4), "released with Shift still down: the line horizontal (" + (line != null ? Box(line) : "none") + ")");
            shiftDown = true;   // held before the press
            Mouse("OnMouseDown", Px(10, 20));
            Check((bool)typeof(EditorCanvas).GetField("drawShift", All).GetValue(Canvas), "Shift held at the press: on from the start");
            Mouse("OnMouseMove", Px(30, 23));
            Mouse("OnMouseUp", Px(30, 23));
            Pump(30);
            shiftDown = false;
            line = Last as LineObject;
            Check(Count == n + 2 && line != null && line.H == 0, "Shift held through the whole drag: the line horizontal (" + (line != null ? Box(line) : "none") + ")");
            Select("rectangle");
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(30, 15));
            HoldShift(true);
            m = form.Measure;
            Check(m != null && Near(m.W, m.H, 0.3) && Near(m.W, 20, 0.3), "Shift mid-drag (key state only): the box square (" + (m != null ? m.W.ToString("0.0") + "×" + m.H.ToString("0.0") : "none") + ")");
            HoldShift(false);
            m = form.Measure;
            Check(m != null && Near(m.H, 5, 0.3), "Shift released: the box as dragged again");
            Esc();
            Select("triangle");
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(40, 15));
            HoldShift(true);
            Mouse("OnMouseUp", Px(40, 15));
            Pump(30);
            HoldShift(false);
            var tri2 = Last as ShapeObject;
            Check(tri2 != null && tri2.Kind == ShapeKind.Triangle && Math.Abs(tri2.W / tri2.H - k) < 0.03,
                  "Shift pressed mid-drag, then released the mouse: the triangle regular (" + (tri2 != null ? Box(tri2) : "none") + ")");
            Call("Undo", false);

            // ---------- dragged upwards: upside down (PowerPoint) ----------
            Mouse("OnMouseDown", Px(10, 30));
            Mouse("OnMouseMove", Px(30, 10));
            bool apexDown = Outlined(Px(10, 10)) && Outlined(Px(20, 30)), apexUp = Outlined(Px(10, 30));   // upside down: the base corners at the top
            Mouse("OnMouseUp", Px(30, 10));
            Pump(30);
            var down = Last as ShapeObject;
            Check(down != null && down.Rotation == 180 && Near(down.X, 10) && Near(down.Y, 10) && Near(down.H, 20) && apexDown && !apexUp,
                  "a triangle dragged upwards: upside down, the outline too (" + (down != null ? Box(down) + ", " + down.Rotation + "°" : "none") + ", apex " + apexDown + "/" + apexUp + ")");
            DragMm(30, 10, 10, 30);
            Check(((ShapeObject)Last).Rotation == 0, "dragged down to the left: upright (left and right are the same)");
            Call("Undo", false);
            Call("Undo", false);
            Select("star");
            DragMm(10, 30, 30, 10);
            Check(((ShapeObject)Last).Rotation == 180, "a star (5 points) dragged upwards: upside down");
            Call("Undo", false);
            Select("rectangle");
            DragMm(10, 30, 30, 10);
            Check(((ShapeObject)Last).Rotation == 0, "a rectangle dragged upwards: unchanged");
            Call("Undo", false);

            // ---------- the outline: the shape itself ----------
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(30, 30));
            bool boxCorner = Outlined(Px(10, 10));
            Esc();
            Select("star");
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(30, 30));
            bool starCorner = Outlined(Px(10, 10)), starTop = Outlined(Px(20, 10));
            Esc();
            Select("ellipse");
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(30, 30));
            bool ellCorner = Outlined(Px(10, 10)), ellTop = Outlined(Px(20, 10));
            Esc();
            Check(boxCorner && !starCorner && starTop && !ellCorner && ellTop && Canvas.DrawShape.Kind == ShapeKind.Ellipse,
                  "the outline while dragging: the real shape (rectangle corner " + boxCorner + ", star " + starCorner + "/" + starTop + ", ellipse " + ellCorner + "/" + ellTop + ")");
            Select("line");
            Check(Canvas.DrawShape == null, "the line tool: no shape outline (its thin line)");
            Select("rectangle");

            // ---------- shapes ----------
            n = Count;
            DragMm(30, 5, 50, 15);
            var rect = Last as ShapeObject;
            Check(Count == n + 1 && rect != null && rect.Kind == ShapeKind.Rectangle && Near(rect.X, 30) && Near(rect.Y, 5) && Near(rect.W, 20) && Near(rect.H, 10),
                  "a rectangle in the dragged area (" + (rect != null ? Box(rect) : "none") + ")");
            DragMm(50, 25, 35, 15);   // dragged up and to the left
            rect = Last as ShapeObject;
            Check(Count == n + 2 && Near(rect.X, 35) && Near(rect.Y, 15) && Near(rect.W, 15) && Near(rect.H, 10), "dragged backwards: the same area (" + Box(rect) + ")");
            ClickMm(40, 20);
            rect = Last as ShapeObject;
            Check(Count == n + 3 && Near(rect.X, 40) && Near(rect.Y, 20) && rect.W == 15 && rect.H == 10, "a click: the default size at the click point");
            DragMm(-3, -2, 8, 6);
            rect = Last as ShapeObject;
            Check(Count == n + 4 && Near(rect.X, -3) && Near(rect.Y, -2), "a drag may start on the grey area (" + Box(rect) + ")");
            Select("ellipse");
            DragMm(5, 5, 15, 12);
            Check(((ShapeObject)Last).Kind == ShapeKind.Ellipse && Canvas.Drawing == DrawKind.Ellipse, "the ellipse tool draws an ellipse");
            Select("star");
            DragMm(5, 5, 15, 12);
            Check(((ShapeObject)Last).Kind == ShapeKind.Star && Near(Last.W, 10), "the star tool draws a star in the area");

            // ---------- Esc ----------
            n = Count;
            Select("rectangle");
            Point a = Px(10, 10);
            Mouse("OnMouseDown", a);
            Mouse("OnMouseMove", Px(20, 20));
            Check(Canvas.DrawDragging, "a drag in progress");
            Esc();
            Mouse("OnMouseUp", Px(20, 20));
            Check(Count == n && !Canvas.DrawDragging && form.ActiveTool == Tool("rectangle"), "Esc: the drag ends without an object, the tool stays");
            Esc();
            Check(form.ActiveTool == null && Canvas.Drawing == DrawKind.None && Canvas.Cursor == Cursors.Default, "Esc again: back to the arrow");
            Mouse("OnMouseDown", Px(60, 20));
            Mouse("OnMouseUp", Px(60, 20));
            Check(Count == n, "the arrow: a click draws nothing");

            // ---------- text ----------
            Select("paragraph");
            n = Count;
            DragMm(5, 5, 45, 20);
            var frame = Last as TextObject;
            Check(Count == n + 1 && frame != null && frame.Kind == TextKind.Frame && Near(frame.X, 5) && Near(frame.W, 40) && Near(frame.H, 15),
                  "paragraph text: a frame of the dragged size (" + (frame != null ? Box(frame) : "none") + ")");
            Check(Canvas.EditedText == frame && frame.Text == "", "empty, the caret in it (no placeholder)");
            Check(form.TypingDrawnText, "the status bar can tell: a drawn text is typed");
            Canvas.EditChange(x => x.Type("Regal 3"));
            Canvas.EditKey(Keys.Escape);   // the window's ProcessCmdKey hands Esc to the text first
            Check(Canvas.EditedText == null && form.ActiveTool == Tool("paragraph") && Doc.Objects.Contains(frame) && frame.Text == "Regal 3" && Canvas.Selected == frame,
                  "typed, then Esc: the editing ends, the text stays selected, the tool stays");
            Check(!form.TypingDrawnText, "after Esc: no longer typing a drawn text");
            Select("artistic");
            n = Count;
            int steps = UndoCount;
            bool wasDirty = F<bool>("dirty");
            DragMm(5, 30, 40, 40);
            var art = Last as TextObject;
            Check(Count == n + 1 && art != null && art.Kind != TextKind.Frame && Math.Abs(art.H - 10) < 2.5 && art.Size > 14,
                  "artistic text: the dragged height is its line (" + (art != null ? Box(art) + ", " + art.Size + " pt" : "none") + ")");
            Check(Canvas.EditedText == art, "typing starts right away");
            Canvas.EditKey(Keys.Escape);
            Check(Count == n && !Doc.Objects.Contains(art) && UndoCount == steps && F<bool>("dirty") == wasDirty,
                  "Esc without typing: the drawn text goes again, no undo step left (" + (UndoCount - steps) + ")");
            DragMm(5, 30, 40, 30.6);
            art = Last as TextObject;
            Check(Count == n + 1 && art != null && art.Size == 14, "a flat drag (under 6 pt): the default size (" + (art != null ? art.Size + " pt" : "none") + ")");
            Canvas.EditChange(x => x.Type("A"));
            Canvas.EditKey(Keys.Escape);
            n = Count;
            ClickMm(frame.X + 2, frame.Y + 2);
            Check(Count == n && Canvas.EditedText == frame, "the text tool on a text: it edits that text, nothing new");
            Canvas.EditKey(Keys.Escape);
            Check(Doc.Objects.Contains(frame), "an existing text left without typing stays");
            ClickMm(70, 5);
            var lone = Last as TextObject;
            Check(Count == n + 1 && lone != null && Near(lone.X, 70) && Canvas.EditedText == lone, "a click on an empty spot: a text at the click point, typing starts");
            ClickMm(70, 15);
            Check(Count == n + 1 && !Doc.Objects.Contains(lone) && Last is TextObject && Near(Last.Y, 15),
                  "a click beside it: the untyped text goes, a new one where clicked");
            Canvas.EditKey(Keys.Escape);
            Check(Count == n, "Esc: that one goes too");

            // ---------- the arrow ends editing ----------
            steps = UndoCount;
            ClickMm(70, 5);
            lone = Last as TextObject;
            Select(null);
            Check(Count == n && !Doc.Objects.Contains(lone) && Canvas.EditedText == null && form.ActiveTool == null && UndoCount == steps,
                  "the arrow while a drawn text is still empty: editing ends, the text goes, no undo step");
            Select("artistic");
            ClickMm(70, 5);
            lone = Last as TextObject;
            Canvas.EditChange(x => x.Type("Ab"));
            Select(null);
            Check(Count == n + 1 && Doc.Objects.Contains(lone) && Canvas.EditedText == null && Canvas.Selected == lone && form.ActiveTool == null,
                  "typed, then the arrow: editing ends, the text stays selected");
            Select("artistic");
            n = Count;
            steps = UndoCount;
            ClickMm(70, 15);
            lone = Last as TextObject;
            Canvas.EditChange(x => x.Type("X"));
            Canvas.EditKey(Keys.Back);
            Canvas.EditKey(Keys.Escape);
            Check(Count == n && !Doc.Objects.Contains(lone) && UndoCount == steps, "typed and deleted again, then Esc: the text goes, no undo step (" + (UndoCount - steps) + ")");
            // a drawn frame widened in the panel, then left empty: it goes in a step of its own, no step left that does nothing
            Select("paragraph");
            var spot = Enumerable.Range(0, 60).Select(i => new RectangleF(5 + i % 6 * 10, 5 + i / 6 * 5, 35, 10))
                                 .First(r => !Doc.Objects.Any(o => { var b = o.Bounds; b.Inflate(1, 1); return b.IntersectsWith(r); }));
            DragMm(spot.X, spot.Y, spot.Right, spot.Bottom);
            lone = Last as TextObject;
            var numW = F<NumericUpDown>("numW");
            numW.Value = numW.Value + 5;
            Pump(30);
            Check(Canvas.EditedText == lone && UndoCount == steps + 2, "a drawn frame widened in the panel while still empty: Add's step and the panel's (" + (UndoCount - steps) + ")");
            Canvas.EditKey(Keys.Escape);
            Check(Count == n && !Doc.Objects.Contains(lone), "then Esc: the frame goes");
            Call("Undo", false);
            Check(Count == n + 1 && Last is TextObject && Near(Last.W, 40), "Undo: the widened empty frame back, no step that does nothing (" + (Last != null ? Box(Last) : "none") + ")");
            Call("Undo", false);
            Call("Undo", false);
            Check(Count == n && UndoCount == steps, "two more: the label as before the frame");
            Select("artistic");
            ClickMm(70, 15);
            lone = Last as TextObject;
            Select("rectangle");
            Check(Count == n && !Doc.Objects.Contains(lone) && Canvas.EditedText == null, "another tool while a drawn text is still empty: it goes");
            Select(null);
            StartEdit(lone = (TextObject)Doc.Objects.First(o => o is TextObject && ((TextObject)o).Text == "Ab"));
            Select(null);
            Check(Canvas.EditedText == null && Canvas.Selected == lone, "the arrow while editing any text (no tool on): editing ends");

            // ---------- a zoom during a drag: the start stays on the label ----------
            Select("rectangle");
            n = Count;
            Point z0 = Px(10, 10);
            Mouse("OnMouseDown", z0);
            Call("SetZoom", 200.0);
            Pump(100);
            Point z1 = Px(20, 15);
            Mouse("OnMouseMove", z1);
            Mouse("OnMouseUp", z1);
            Pump(30);
            Check(Count == n + 1 && Near(Last.X, 10) && Near(Last.Y, 10) && Near(Last.W, 10, 0.3) && Near(Last.H, 5, 0.3), "zoomed during the drag: the start stays where it was on the label (" + Box(Last) + ")");
            Call("SetZoom", 0.0);
            Pump(100);

            // ---------- a double-click with a shape tool: one shape, not two on top of each other ----------
            Select("rectangle");
            n = Count;
            var dc = Px(30, 10);
            Mouse("OnMouseDown", dc, 1); Mouse("OnMouseUp", dc, 1);
            Mouse("OnMouseDown", dc, 2); Mouse("OnMouseUp", dc, 2);
            Pump(30);
            Check(Count == n + 1 && Last is ShapeObject, "a double-click with the rectangle tool: one rectangle (" + (Count - n) + ")");

            // ---------- codes ----------
            Select("barcode");
            n = Count;
            DragMm(5, 5, 45, 20);
            Check(Count == n + 1 && Last is BarcodeObject && Near(Last.W, 40) && Near(Last.H, 15), "a barcode in the dragged area (" + Box(Last) + ")");
            Select("qr");
            DragMm(5, 5, 25, 17);
            Check(Count == n + 2 && Near(Last.W, 12) && Near(Last.H, 12), "a QR code square, inside the area (" + Box(Last) + ")");

            // ---------- picture: the file first (the dialog itself not opened here), then one drag, then the arrow ----------
            string png = Path.Combine(folder, "bild.png");
            using (var b = new Bitmap(200, 100)) { using (var g = Graphics.FromImage(b)) g.Clear(Color.Black); b.Save(png, ImageFormat.Png); }
            var img = (ImageObject)typeof(EditorForm).GetMethod("LoadPicture", All).Invoke(null, new object[] { form, png });
            SetF("activeTool", Tool("picture"));
            SetF("toolPicture", img);
            Canvas.Drawing = DrawKind.Box;
            n = Count;
            DragMm(5, 5, 45, 35);
            var pic = Last as ImageObject;
            Check(Count == n + 1 && pic != null && Near(pic.W, 40) && Near(pic.H, 20), "a picture inside the area, in its proportions (" + (pic != null ? Box(pic) : "none") + ")");
            Check(form.ActiveTool == null && Canvas.Drawing == DrawKind.None, "after the picture: the arrow");

            // ---------- Insert still inserts ----------
            n = Count;
            // ---------- snapping while drawing ----------
            float cx = SnapLines("SnapX")[2], cy = SnapLines("SnapY")[2];   // the label's centre lines
            snapOn = true;
            Select("rectangle");
            n = Count;
            Point c = Px(cx, cy);
            Mouse("OnMouseDown", Px(cx - 15, cy - 6));
            Mouse("OnMouseMove", new Point(c.X + 3, c.Y + 2));
            Check(Guide("guideX") == cx && Guide("guideY") == cy, "drawing near the centre lines: the end snaps, both guides show (" + Guide("guideX") + ", " + Guide("guideY") + ")");
            snapOn = false;   // Alt pressed, no mouse move
            Pump(150);
            Check(Guide("guideX") == null && Guide("guideY") == null && !Near(Canvas.DrawToMm.X, cx, 0.05), "Alt pressed mid-drag: free at once, no guides");
            snapOn = true;
            Pump(150);
            Mouse("OnMouseUp", new Point(c.X + 3, c.Y + 2));
            Pump(30);
            var snapped = Last as ShapeObject;
            Check(Count == n + 1 && snapped != null && Near(snapped.X + snapped.W, cx, 0.06) && Near(snapped.Y + snapped.H, cy, 0.06) && Guide("guideX") == null,
                  "Alt released, then the release: the corner on the centre lines, the guides gone (" + (snapped != null ? Box(snapped) : "none") + ")");
            Select("line");
            Mouse("OnMouseDown", Px(cx - 20, cy));
            shiftDown = true;
            Mouse("OnMouseMove", new Point(c.X + 3, c.Y + 4));
            Mouse("OnMouseUp", new Point(c.X + 3, c.Y + 4));
            Pump(30);
            shiftDown = false;
            line = Last as LineObject;
            Check(Count == n + 2 && line != null && line.H == 0 && Near(line.Y, cy, 0.06) && Near(line.X + line.W, cx, 0.06),
                  "a line with Shift near the centre: level, its end on the centre line (" + (line != null ? Box(line) : "none") + ")");
            snapOn = false;

            // ---------- the right button cancels a drag ----------
            Select("rectangle");
            n = Count;
            SetF("objectMenu", null);
            Mouse("OnMouseDown", Px(10, 10));
            Mouse("OnMouseMove", Px(25, 20));
            Right("OnMouseDown", Px(25, 20));
            Check(!Canvas.DrawDragging && form.ActiveTool == Tool("rectangle"), "the right button during a drag: cancelled, the tool stays");
            Right("OnMouseUp", Px(25, 20));
            Mouse("OnMouseUp", Px(25, 20));
            Pump(30);
            Check(Count == n && F<ContextMenuStrip>("objectMenu") == null, "no object, no context menu");

            // ---------- Alt during a drag: free drawing; its release doesn't open the menu bar ----------
            var spy = new KeyMenuSpy();
            spy.AssignHandle(form.Handle);
            AltKey(true);
            AltKey(false);
            Check(spy.Count == 1, "the probe: Alt alone outside a drag asks for the menu (" + spy.Count + ")");
            Mouse("OnMouseDown", Px(10, 10));
            AltKey(true);
            EditorCanvas.AltState = () => true;
            Mouse("OnMouseMove", Px(25, 20));
            Mouse("OnMouseUp", Px(25, 21));
            EditorCanvas.AltState = () => !snapOn;
            AltKey(false);
            Check(spy.Count == 1, "Alt held while drawing, released after the drag: no menu");
            Mouse("OnMouseDown", Px(10, 30));
            Mouse("OnMouseMove", Px(20, 35));
            AltKey(true);
            AltKey(false);
            Mouse("OnMouseUp", Px(25, 40));
            Check(spy.Count == 1, "Alt pressed and released during a drag: no menu");
            AltKey(true);
            AltKey(false);
            Check(spy.Count == 2, "the next Alt alone: the menu as usual");
            spy.ReleaseHandle();
            Select(null);
            n = Count;

            Tool("rectangle").Insert();
            Check(Count == n + 1 && Last is ShapeObject && form.ActiveTool == null, "Insert (Enter, menu, key): inserted at a free place, the arrow stays");
            Tool("artistic").Insert();
            Pump(30);
            Check(Count == n + 2 && Last is TextObject && ((TextObject)Last).Text == Lang.T("Text") && Canvas.Selected == Last && Canvas.EditedText == null,
                  "Insert of a text: with its placeholder, selected, not editing");
            Console.WriteLine("INFO tool changes: " + toolChanges);
        }
    }
}
