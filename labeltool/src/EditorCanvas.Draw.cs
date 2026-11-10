// Label editor canvas (EditorCanvas.cs), part: drawing a new object with the mouse.
// While a tool is on (Drawing != None, set by EditorForm.SelectTool) the cursor is a crosshair; a drag on the label or
// the grey area around it shows the new object's outline, the release hands its two corners to the window (Drawn),
// which creates the object. A plain click (inside the system's drag size) hands over the click point twice. Shift
// while dragging: a line horizontal, vertical or at 45°, a shape square, round or regular; pressing or releasing it
// during the drag switches at once: the keyboard's real Shift state is read on every mouse move and by a short timer
// while a drag runs (the key messages don't reliably reach the window during a drag with the mouse captured; the
// window's OnKeyDown/OnKeyUp → DrawShift stays as an extra path). A shape tool's outline is the shape
// itself (DrawShape); dragged upwards it is upside down, as PowerPoint draws it (a triangle's apex down). Esc cancels a
// drag in progress, and so does the right mouse button (the tool stays on). The corners snap to the label's edges and
// centre lines as moving does (the same tolerance and dotted guides; with Shift along the constrained direction); Alt
// held: free (read live like Shift). With the text tool a click on a text edits it instead.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    // What the outline shows while drawing.
    enum DrawKind { None, Line, Box, Ellipse, Text }

    sealed partial class EditorCanvas
    {
        DrawKind drawing;
        bool drawDrag;              // a drag of the drawing tool in progress
        PointF drawDownMm;          // where it started (mm: a zoom or scroll during the drag keeps it on the label)
        Point drawAt;               // client pixels: the drawn end (Shift and snapping applied)
        PointF drawEndMm;           // the same in mm (exact: a snapped edge stays on the edge)
        Point drawMouse;            // client pixels: where the mouse is (as it is)
        Point drawPressed;          // client pixels: where the button went down (a click or a drag)
        bool drawShift;             // Shift applied to drawAt
        bool drawFree;              // Alt applied: no snapping
        bool rightCancel;           // the right button cancelled a drag: its release opens no menu
        bool altInDrag;             // Alt was held during a drag (drawing or moving): its release opens no menu (EditorForm.AltKey.cs)
        ShapeObject drawShape;      // a shape tool's shape (its outline and its regular proportions); null: none
        Timer shiftWatch;           // while a drag runs: Shift pressed or released without a mouse move

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        // Shift is down now: the keyboard's state, whichever window has the focus (a test sets its own).
        internal static Func<bool> ShiftState = () => (ModifierKeys & Keys.Shift) != 0 || (GetAsyncKeyState((int)Keys.ShiftKey) & 0x8000) != 0;

        static bool Shift { get { return ShiftState(); } }

        // Alt is down now (no snapping while drawing; a test sets its own).
        internal static Func<bool> AltState = () => (ModifierKeys & Keys.Alt) != 0;

        static bool Alt { get { return AltState(); } }

        void WatchShift(bool on)
        {
            if (!on) { if (shiftWatch != null) shiftWatch.Stop(); return; }
            if (shiftWatch == null)
            {
                shiftWatch = new Timer { Interval = 40 };
                shiftWatch.Tick += delegate
                {
                    if (!drawDrag) { shiftWatch.Stop(); return; }
                    bool shift = Shift, free = Alt;
                    if (shift != drawShift || free != drawFree) DrawTo(drawMouse, shift);
                };
            }
            shiftWatch.Start();
        }

        Point DrawDown { get { return Point.Round(ToScreen(drawDownMm.X, drawDownMm.Y)); } }

        // The active tool's outline; None: the arrow (select and move as usual).
        public DrawKind Drawing
        {
            get { return drawing; }
            set
            {
                if (drawing == value) return;
                CancelDraw();
                drawing = value;
                Cursor = value == DrawKind.None ? Cursors.Default : Cursors.Cross;
            }
        }

        // A shape tool: the shape the outline shows (its kind, corners, points); null for the other tools.
        public ShapeObject DrawShape
        {
            get { return drawShape; }
            set { drawShape = value; if (drawDrag) Invalidate(); }
        }

        // Shift pressed or released (the window's keys): during a drag the outline and the measure follow at once,
        // also without a mouse move.
        public void DrawShift(bool shift)
        {
            if (!drawDrag || shift == drawShift) return;
            DrawTo(drawMouse, shift);
        }

        void DrawTo(Point mouse, bool shift)
        {
            drawMouse = mouse;
            drawShift = shift;
            drawFree = Alt;
            if (drawFree) altInDrag = true;
            drawEndMm = SnapDrawn(ToMm(Constrained(mouse, shift)), shift, drawFree);
            drawAt = Point.Round(ToScreen(drawEndMm.X, drawEndMm.Y));
            Invalidate();
            RaiseDrawMoved();
        }

        // The drawn end snapped to the label's edges and centre lines (EditorCanvas.Snap, as moving does; the guides
        // show). With Shift the drag from the start is only made longer or shorter, so the line keeps its angle and the
        // shape its proportions: the nearer of the two snaps wins.
        PointF SnapDrawn(PointF mm, bool shift, bool free)
        {
            float? gx, gy;
            float sx = SnapOne(SnapX, mm.X, free, out gx), sy = SnapOne(SnapY, mm.Y, free, out gy);
            if (!shift) { guideX = gx; guideY = gy; return new PointF(mm.X + sx, mm.Y + sy); }
            guideX = guideY = null;
            PointF a = drawDownMm;
            float dx = mm.X - a.X, dy = mm.Y - a.Y;
            // a level or upright line exactly so (the pixels' rounding leaves a trace of the other side)
            if (Math.Abs(dy) < 0.05f * Math.Abs(dx)) dy = 0;
            if (Math.Abs(dx) < 0.05f * Math.Abs(dy)) dx = 0;
            mm = new PointF(a.X + dx, a.Y + dy);
            sx = SnapOne(SnapX, mm.X, free, out gx); sy = SnapOne(SnapY, mm.Y, free, out gy);
            // along the drag only by a side that carries it (not a short one: a small error would scale it a lot)
            float most = Math.Max(Math.Abs(dx), Math.Abs(dy));
            bool byX = gx.HasValue && Math.Abs(dx) >= 0.3f * most && Math.Abs(dx) > 0.01f, byY = gy.HasValue && Math.Abs(dy) >= 0.3f * most && Math.Abs(dy) > 0.01f;
            if (byX && byY) { if (Math.Abs(sx) <= Math.Abs(sy)) byY = false; else byX = false; }
            float k = byX ? (dx + sx) / dx : byY ? (dy + sy) / dy : 1;
            var p = new PointF(a.X + dx * k, a.Y + dy * k);
            if (byX) guideX = gx;
            if (byY) guideY = gy;
            if (k != 1)
            {
                // the other side may land on a line too (a level line on the centre line, a square into a corner)
                if (byX && gy.HasValue && Math.Abs(p.Y - gy.Value) < 0.01f) guideY = gy;
                if (byY && gx.HasValue && Math.Abs(p.X - gx.Value) < 0.01f) guideX = gx;
            }
            return p;
        }

        // Snap of one value: the shift (0: none) and its guide (null when it doesn't snap).
        float SnapOne(float[] lines, float v, bool free, out float? guide)
        {
            float s = Snap(lines, new[] { v }, free, out guide);
            if (guide.HasValue && Math.Abs(guide.Value - (v + s)) > 0.001f) guide = null;
            return s;
        }

        // Alt was held during a drag since the last time asked (EditorForm.AltKey.cs: its release opens no menu).
        public bool TakeAltInDrag()
        {
            bool r = altInDrag;
            altInDrag = false;
            return r;
        }

        // The right button released after it cancelled a drag: no context menu (EditorForm). Asked once.
        public bool TakeRightCancel()
        {
            bool r = rightCancel;
            rightCancel = false;
            return r;
        }

        // A drawn object's corners (mm, start and end as dragged; the same point for a plain click).
        public event Action<PointF, PointF, bool> Drawn;

        public bool DrawDragging { get { return drawDrag; } }

        // The live status hint (EditorForm.Palette.cs): raised when a drag starts, on every move, and when it ends or is
        // cancelled (then DrawDragging is false already). The corners as dragged, in mm (the end with Shift applied).
        public event Action DrawMoved;
        public PointF DrawFromMm { get { return drawDownMm; } }
        public PointF DrawToMm { get { return drawEndMm; } }

        void RaiseDrawMoved() { if (DrawMoved != null) DrawMoved(); }

        // Esc: a drag in progress ends without an object. True: there was one.
        public bool CancelDraw()
        {
            if (!drawDrag) return false;
            drawDrag = false;
            WatchShift(false);
            Capture = false;
            guideX = guideY = null;
            Invalidate();
            RaiseDrawMoved();
            return true;
        }

        // Mouse down with a tool on. True: handled (the arrow's handling is skipped).
        bool DrawMouseDown(MouseEventArgs e, PointF mm)
        {
            if (drawDrag && e.Button == MouseButtons.Right)
            {
                // the right button during a drag: cancelled, as Esc (PowerPoint, CorelDRAW); its release opens no menu
                CancelDraw();
                rightCancel = true;
                return true;
            }
            rightCancel = false;
            if (drawing == DrawKind.None || e.Button != MouseButtons.Left) return false;
            if (editing != null)
            {
                if (OnEdited(mm)) return false;   // in the text being edited: the caret, as always
                EndEdit();
            }
            if (drawing == DrawKind.Text)
            {
                var text = HitObject(mm) as TextObject;
                if (text != null) { BeginEdit(text, e.Location); return true; }   // the text tool on a text edits it
            }
            if (e.Clicks > 1) return true;   // the second press of a double-click: no second object on top of the first
            drawFree = Alt;
            if (drawFree) altInDrag = true;
            drawDownMm = SnapDrawn(ToMm(e.Location), false, drawFree);   // the start snaps too
            drawEndMm = drawDownMm;
            drawAt = Point.Round(ToScreen(drawEndMm.X, drawEndMm.Y));
            drawMouse = drawPressed = e.Location;
            drawShift = Shift;   // held already: applies from the start
            drawDrag = true;
            Capture = true;
            WatchShift(true);
            RaiseDrawMoved();
            return true;
        }

        bool DrawMouseMove(MouseEventArgs e)
        {
            if (drawDrag)
            {
                DrawTo(e.Location, Shift);
                return true;
            }
            if (drawing == DrawKind.None) return false;
            if (editing != null && OnEdited(ToMm(e.Location))) return false;   // the edited text's own cursor
            Cursor = drawing == DrawKind.Text && Doc != null && Geometry != null && HitObject(ToMm(e.Location)) is TextObject ? Cursors.IBeam : Cursors.Cross;
            return true;
        }

        bool DrawMouseUp(MouseEventArgs e)
        {
            if (!drawDrag) return false;
            drawDrag = false;
            WatchShift(false);
            Capture = false;
            DrawTo(e.Location, Shift);
            guideX = guideY = null;
            // Windows' drag size is in 96-dpi pixels: scaled to this screen (else a click at 150 % or 200 % shakes into a tiny object)
            var size = SystemInformation.DragSize;
            int dx = Math.Max(size.Width, LogicalToDeviceUnits(size.Width)) / 2, dy = Math.Max(size.Height, LogicalToDeviceUnits(size.Height)) / 2;
            // as the mouse moved (snapping may pull a short drag together or apart)
            bool click = Math.Abs(e.X - drawPressed.X) <= dx && Math.Abs(e.Y - drawPressed.Y) <= dy;
            Invalidate();
            RaiseDrawMoved();
            if (Drawn != null) Drawn(drawDownMm, click ? drawDownMm : drawEndMm, click);
            return true;
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (drawDrag && !Capture) CancelDraw();   // another window took the mouse
        }

        // Shift: a line at a multiple of 45°, a box with equal sides, a shape regular (its natural proportions: circle,
        // equilateral triangle, regular polygon or star).
        Point Constrained(Point p, bool shift)
        {
            if (!shift) return p;
            var drawDown = DrawDown;
            int dx = p.X - drawDown.X, dy = p.Y - drawDown.Y;
            if (drawing == DrawKind.Line)
            {
                double a = Math.Atan2(dy, dx), step = Math.PI / 4;
                a = Math.Round(a / step) * step;
                double len = Math.Sqrt(dx * dx + dy * dy);
                return new Point(drawDown.X + (int)Math.Round(len * Math.Cos(a)), drawDown.Y + (int)Math.Round(len * Math.Sin(a)));
            }
            double k = drawShape != null ? drawShape.NaturalRatio : 1;   // width : height
            if (k <= 0) k = 1;
            double h = Math.Max(Math.Abs(dy), Math.Abs(dx) / k);
            int w = (int)Math.Round(h * k), hh = (int)Math.Round(h);
            return new Point(drawDown.X + (dx < 0 ? -w : w), drawDown.Y + (dy < 0 ? -hh : hh));
        }

        // The outline of the object being drawn (OnPaint, over everything).
        void PaintDrawing(Graphics g)
        {
            if (!drawDrag) return;
            var drawDown = DrawDown;
            var drawAt = Point.Round(ToScreen(drawEndMm.X, drawEndMm.Y));   // also right after a zoom
            var r = Rectangle.FromLTRB(Math.Min(drawDown.X, drawAt.X), Math.Min(drawDown.Y, drawAt.Y), Math.Max(drawDown.X, drawAt.X), Math.Max(drawDown.Y, drawAt.Y));
            using (var pen = new Pen(Theme.Of(Role.Accent), Math.Max(1f, LogicalToDeviceUnits(1))))
            {
                if (drawing == DrawKind.Line) { g.DrawLine(pen, drawDown, drawAt); return; }
                if (drawShape != null && r.Width > 0 && r.Height > 0)
                {
                    // the shape itself, its outline only (round corners scaled as on the label)
                    // dragged upwards: upside down, as it is drawn (OnDrawn: turned 180°)
                    var state = g.Save();
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    if (drawAt.Y < drawDown.Y && drawShape.UpsideDownDiffers)
                    {
                        g.TranslateTransform(r.X + r.Width / 2f, r.Y + r.Height / 2f);
                        g.RotateTransform(180);
                        g.TranslateTransform(-(r.X + r.Width / 2f), -(r.Y + r.Height / 2f));
                    }
                    using (var path = drawShape.Path(r, (float)(drawShape.Radius * PixelsPerMm))) g.DrawPath(pen, path);
                    g.Restore(state);
                    return;
                }
                if (drawing == DrawKind.Ellipse) { g.DrawEllipse(pen, r); return; }
                if (drawing == DrawKind.Text) pen.DashStyle = DashStyle.Dash;
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
        }
    }
}
