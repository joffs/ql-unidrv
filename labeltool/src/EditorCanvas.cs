// Label editor: the canvas. Shows the label at its real proportions with the rendered 1-bit bitmap (what prints),
// the area the printer can't reach hatched, and the selected objects (one: with handles); moves and resizes with the
// mouse, snapping to the label's edges and centre lines. Several objects: Ctrl/Shift+click, or a frame dragged from
// an empty spot (everything it touches). Objects beyond the label (after turning the view or a narrower format) stay
// on a grey margin around it: their parts outside greyed and hatched (they don't print), still to be clicked and dragged.
// A line's end snaps to 45° steps within 5° (Shift: 15° steps; Alt: free); a selected line shows a band
// along it, not a box.
// Text, boxes and pictures turn freely with the round grip above the selected object (snaps to 0/90/180/270°; Shift:
// 15° steps; Alt: free). A selected barcode or QR code shows its quiet zone as a light margin (orange when something
// reaches into it); unselected, the canvas shows only what prints.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorCanvas : Control
    {
        public LabelDoc Doc;
        public LabelLayout Geometry;
        Bitmap rendered;
        // The objects beyond the label as the grey margin shows them (EditorRender.RenderOutsideOnScreen): drawn at the
        // screen's pixels where they are in sight (greyedAt), again when they change (OutsideChanged) or the view does.
        // (At the printer's dots for the whole object and scaled at every paint, a big photo cost most of a move.)
        Bitmap greyed;
        Rectangle greyedAt;
        PointF greyedOrigin;
        float greyedK;
        bool outsideChanged = true;
        readonly List<LabelObject> selection = new List<LabelObject>();
        double zoom;          // screen pixels per mm; 0 = fit the window

        public event EventHandler SelectionChanged;
        public event EventHandler BeforeChange;   // the document is about to change (undo snapshot)
        public event EventHandler Changed;        // it changed (render again, properties)
        public event EventHandler ViewChanged;    // a drag ended: the area shown may have grown or shrunk (scrolling)
        public event Action<double> LengthDragged;   // the label's end was dragged to this length (mm)

        // Continuous tape: the length as a measure under the label (right of it, across the tape), and with LengthGrip a
        // grip on the label's end to drag it (1 mm steps, Alt: 0.1 mm), within LengthMin..LengthMax. Set by the form.
        public string LengthText;
        public bool LengthGrip;
        public double LengthMin, LengthMax;
        double lengthDrag;

        // mouse
        enum Drag { None, Move, Resize, Frame, Turn, Length, Text }
        Drag drag;
        int handle;                // 0..7 clockwise from top left; line ends: 0 = start, 1 = end
        PointF downMm;
        RectangleF startBox;       // the object's box (resize) or all selected objects' (move)
        Dictionary<LabelObject, PointF> startAt;   // move: each selected object's position when the drag began
        Point frameStart, frameEnd;                // selection frame (screen)
        List<LabelObject> frameBase;               // selected before the frame (Ctrl/Shift: the frame adds)
        bool startRising;          // a line's direction when the drag began
        double startSize;          // a single-line text's font size when the drag began
        List<TextRun> startRuns;   // and its words' formats (null: one format)
        bool changeStarted;
        float? guideX, guideY;     // snap guides (mm)
        double turnFrom;           // turn: the object's angle and the mouse's direction when the drag began (degrees)
        double turnDown;
        PointF turnCentre;         // turn: the box centre when the drag began (the angle is measured about it)
        Point turnMouse;           // turn: where the mouse is (for the angle shown next to it)
        bool lineSnapped;          // dragging a line's end: its angle is on a 45° step (a dotted guide shows it)

        public EditorCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint | ControlStyles.Selectable, true);
            Theme.Back(this, Role.Desk);
            blink.Tick += OnBlink;
        }

        public Bitmap Rendered
        {
            get { return rendered; }
            set { if (rendered != null && rendered != value) rendered.Dispose(); rendered = value; DropView(); Invalidate(); }
        }

        // The rendered label scaled to the screen once (scaling it at every paint cost most of a paint while dragging);
        // viewAt: where it was scaled for (the label's printable area on the canvas). Made again when that changes.
        Bitmap view;
        RectangleF viewAt;

        void DropView() { if (view != null) { view.Dispose(); view = null; } }
        void DropGreyed() { if (greyed != null) { greyed.Dispose(); greyed = null; } }

        // The objects changed: the grey margin is drawn again at the next paint.
        public void OutsideChanged() { outsideChanged = true; Invalidate(); }

        // The grey margin's objects drawn again when they or the view changed (zoom, window size, scrolling).
        void UpdateGreyed(List<LabelObject> beyond)
        {
            var b = ToScreen(RectangleF.Inflate(BoxOf(beyond), 1, 1));   // a line's width reaches past its box
            var at = Rectangle.FromLTRB((int)Math.Floor(b.Left), (int)Math.Floor(b.Top), (int)Math.Ceiling(b.Right), (int)Math.Ceiling(b.Bottom));
            at.Intersect(ClientRectangle);
            if (Parent != null) at.Intersect(RectangleToClient(Parent.RectangleToScreen(Parent.ClientRectangle)));
            if (!outsideChanged && greyed != null && at == greyedAt && Origin == greyedOrigin && PixelsPerMm == greyedK) return;
            DropGreyed();
            greyed = EditorRender.RenderOutsideOnScreen(Doc, Geometry, at, Origin, PixelsPerMm);
            greyedAt = at; greyedOrigin = Origin; greyedK = PixelsPerMm;
            outsideChanged = false;
        }

        // The rendered bitmap was drawn again inside part (mm, EditorRender.RenderPart): the scaled copy there too.
        // invalidate false: the caller repaints the part itself (editing a text: EditorCanvas.Editing InvalidateEdit).
        public void RenderedPart(RectangleF part, bool invalidate = true)
        {
            if (view != null && rendered != null)
            {
                var s = ToScreen(RectangleF.Inflate(part, 2, 2));   // as RenderPart's reach
                var r = Rectangle.Round(new RectangleF(s.X - (int)Math.Floor(viewAt.X), s.Y - (int)Math.Floor(viewAt.Y), s.Width, s.Height));
                r.Inflate(2, 2);
                using (var g = Graphics.FromImage(view))
                {
                    g.SetClip(r);
                    g.Clear(Color.Transparent);
                    DrawRendered(g, new RectangleF(viewAt.X - (int)Math.Floor(viewAt.X), viewAt.Y - (int)Math.Floor(viewAt.Y), viewAt.Width, viewAt.Height));
                }
            }
            if (invalidate) Invalidate();
        }

        // The rendered label: dot for dot (sharp) at whole screen pixels per printer dot; smoothed otherwise, as uneven
        // pixel counts per dot would show stripes in dithered pictures and hairlines.
        void DrawRendered(Graphics g, RectangleF at)
        {
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = Whole(at.Width / rendered.Width) && Whole(at.Height / rendered.Height)
                ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(rendered, at);
        }

        // The one selected object; null when none or several are selected.
        public LabelObject Selected
        {
            get { return selection.Count == 1 ? selection[0] : null; }
            set { Select(value != null ? new[] { value } : new LabelObject[0]); }
        }

        // All selected objects, in the order they were selected.
        public IList<LabelObject> Selection { get { return selection.AsReadOnly(); } }

        public void Select(IEnumerable<LabelObject> objects)
        {
            var list = objects.Where(o => o != null).Distinct().ToList();
            if (editing != null && !(list.Count == 1 && list[0] == editing.Target)) EndEdit();   // another object, nothing
            if (list.SequenceEqual(selection)) return;
            selection.Clear();
            selection.AddRange(list);
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public static RectangleF BoxOf(IEnumerable<LabelObject> objects)
        {
            return objects.Select(o => o.Bounds).Aggregate((a, b) => RectangleF.FromLTRB(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom)));
        }

        // Quiet zones: where a code's zone lies (mm, set by the form), the codes with something in theirs, the margin's tip.
        public Func<BarcodeObject, RectangleF> QuietZoneOf;
        public HashSet<LabelObject> Crowded = new HashSet<LabelObject>();
        public string QuietTip = "";
        readonly ToolTip zoneTip = new ToolTip();
        bool onZone;

        public double Zoom { get { return zoom; } set { zoom = value; Invalidate(); } }
        public bool Dragging { get { return drag != Drag.None; } }

        // Screen pixels per mm at "100 %" (real size on this screen).
        public double RealSize { get { return DeviceDpi / 25.4; } }

        // ---------- mapping ----------
        float Gap { get { return LogicalToDeviceUnits(28); } }
        // Room around the label (mm): the tape runs on along the feed (left/right shown along, up/down shown across).
        bool Continuous { get { return Geometry != null && Geometry.Label.Media.Continuous; } }
        float Run { get { return (float)Math.Min(6, Geometry.Label.LengthMm * 0.1); } }
        float ExtraX { get { return Continuous && !Geometry.Across ? Run : 2f; } }
        float ExtraY { get { return Continuous && Geometry.Across ? Run : 2f; } }

        // The area shown (mm): the label with the tape or backing around it, and every object, also those beyond the
        // label. Kept while dragging, so the view doesn't jump under the mouse.
        RectangleF? keptExtent;
        RectangleF Extent
        {
            get
            {
                if (keptExtent.HasValue) return keptExtent.Value;
                var e = new RectangleF(-ExtraX, -ExtraY, (float)Geometry.ViewWidthMm + 2 * ExtraX, (float)Geometry.ViewHeightMm + 2 * ExtraY);
                if (Doc != null) foreach (var o in Doc.Objects) e = RectangleF.Union(e, RectangleF.Inflate(o.Bounds, 2, 2));
                return e;
            }
        }

        public float PixelsPerMm
        {
            get
            {
                if (Geometry == null) return 1;
                if (zoom > 0) return (float)zoom;
                var e = Extent;
                float k = Math.Min((Width - 2 * Gap) / e.Width, (Height - 2 * Gap) / e.Height);
                return Math.Max(0.5f, k);
            }
        }

        // The size the canvas needs at a fixed zoom (for scrolling).
        public Size Needed(double k)
        {
            if (Geometry == null) return Size.Empty;
            var e = Extent;
            return new Size((int)(k * e.Width + 2 * Gap), (int)(k * e.Height + 2 * Gap));
        }

        PointF Origin
        {
            get
            {
                float k = PixelsPerMm;
                var e = Extent;
                return new PointF((Width - k * e.Width) / 2 - k * e.X, (Height - k * e.Height) / 2 - k * e.Y);
            }
        }

        PointF ToScreen(double x, double y) { var o = Origin; float k = PixelsPerMm; return new PointF(o.X + (float)x * k, o.Y + (float)y * k); }
        RectangleF ToScreen(RectangleF r) { var p = ToScreen(r.X, r.Y); return new RectangleF(p.X, p.Y, r.Width * PixelsPerMm, r.Height * PixelsPerMm); }
        // Where the context menu opens from the keyboard: the selection's bottom left corner, within the canvas.
        public Point MenuPoint
        {
            get
            {
                if (selection.Count == 0 || Geometry == null) return Point.Empty;
                var b = ToScreen(BoxOf(selection));
                return new Point((int)Math.Max(0, Math.Min(Width - 1, b.Left)), (int)Math.Max(0, Math.Min(Height - 1, b.Bottom)));
            }
        }

        static bool Whole(float pixelsPerDot) { return pixelsPerDot > 0.98f && Math.Abs(pixelsPerDot - Math.Round(pixelsPerDot)) < 0.02f; }

        // For zooming around a point: where a spot on the label is on the canvas, and back.
        public PointF MmToClient(PointF mm) { return ToScreen(mm.X, mm.Y); }
        public PointF ClientToMm(Point p) { return ToMm(p); }

        internal PointF ToMm(Point p) { var o = Origin; float k = PixelsPerMm; return new PointF((p.X - o.X) / k, (p.Y - o.Y) / k); }
        // An object's outline on the screen (turned with it).
        PointF[] Outline(LabelObject o) { return o.Corners.Select(p => ToScreen(p.X, p.Y)).ToArray(); }

        // The shape the grey "beyond the label" marking follows: the object's box, a line's own band (its width, at
        // least a few pixels) instead of the box around it
        PointF[] OutsideShape(LabelObject o)
        {
            var l = o as LineObject;
            if (l == null) return Outline(o);
            PointF a = ToScreen(l.Start.X, l.Start.Y), b = ToScreen(l.End.X, l.End.Y);
            float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.01f) { dx = 1; dy = 0; len = 1; }
            float half = Math.Max((float)l.Thickness * PixelsPerMm / 2, LogicalToDeviceUnits(3)), nx = -dy / len * half, ny = dx / len * half;
            return new[] { new PointF(a.X + nx, a.Y + ny), new PointF(b.X + nx, b.Y + ny), new PointF(b.X - nx, b.Y - ny), new PointF(a.X - nx, a.Y - ny) };
        }
        static PointF CentreOf(RectangleF r) { return new PointF(r.X + r.Width / 2, r.Y + r.Height / 2); }

        // ---------- painting ----------
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (Geometry == null) return;
            var m = Geometry.Label.Media;
            float k = PixelsPerMm;
            var label = ToScreen(new RectangleF(0, 0, (float)Geometry.ViewWidthMm, (float)Geometry.ViewHeightMm));
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Tape (continuous) or backing paper (die-cut, round), then the label.
            if (m.Continuous)
            {
                var tape = Geometry.Across ? new RectangleF(label.X, label.Y - Run * k, label.Width, label.Height + 2 * Run * k)
                                           : new RectangleF(label.X - Run * k, label.Y, label.Width + 2 * Run * k, label.Height);
                using (var b = new SolidBrush(Theme.Fixed.TapeOutside)) g.FillRectangle(b, tape);
            }
            else
            {
                var backing = RectangleF.Inflate(label, 2 * k, 1.5f * k);
                using (var b = new SolidBrush(Theme.Fixed.Backing)) g.FillRectangle(b, backing);
            }
            using (var path = Shape(label, m.Kind == MediaKind.Round, 1.5f * k))
            {
                using (var paper = new SolidBrush(Theme.Fixed.Paper)) g.FillPath(paper, path);

                // The rendered label, from its copy scaled to the screen (DrawRendered)
                var pr = ToScreen(Geometry.Printable);
                if (rendered != null)
                {
                    int ox = (int)Math.Floor(pr.X), oy = (int)Math.Floor(pr.Y);
                    if (view == null || viewAt != pr)
                    {
                        DropView();
                        viewAt = pr;
                        view = new Bitmap(Math.Max(1, (int)Math.Ceiling(pr.Right) - ox + 1), Math.Max(1, (int)Math.Ceiling(pr.Bottom) - oy + 1), PixelFormat.Format32bppPArgb);
                        using (var v = Graphics.FromImage(view)) DrawRendered(v, new RectangleF(pr.X - ox, pr.Y - oy, pr.Width, pr.Height));
                    }
                    var clip = g.Clip;
                    g.SetClip(path);
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(view, new Rectangle(ox, oy, view.Width, view.Height), 0, 0, view.Width, view.Height, GraphicsUnit.Pixel);
                    g.Clip = clip;
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                }

                // Not printable: hatched.
                using (var region = new Region(path))
                using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(80, Theme.Fixed.PetrolLight), Color.Transparent))
                {
                    region.Exclude(pr);
                    g.FillRegion(hatch, region);
                }
                using (var edge = new Pen(Theme.Fixed.TapeEdge, 1f)) g.DrawPath(edge, path);
                if (m.Continuous)
                    using (var cut = new Pen(Theme.Of(Role.Accent), 1.5f) { DashStyle = DashStyle.Dash })
                    {
                        if (Geometry.Across)
                        {
                            g.DrawLine(cut, label.Left - 6, label.Top, label.Right + 6, label.Top);
                            g.DrawLine(cut, label.Left - 6, label.Bottom, label.Right + 6, label.Bottom);
                        }
                        else
                        {
                            g.DrawLine(cut, label.Left, label.Top - 6, label.Left, label.Bottom + 6);
                            g.DrawLine(cut, label.Right, label.Top - 6, label.Right, label.Bottom + 6);
                        }
                    }
            }

            if (Doc != null && Doc.Objects.Count == 0) DrawHint(g, label);
            if (Doc != null) DrawOutside(g, label, m.Kind == MediaKind.Round, k);
            DrawQuietZones(g);
            DrawLength(g, label);

            // Barcodes that can't be printed (wrong digits, too long, too narrow): marked on screen only, printing stops.
            if (Doc != null)
                using (var bad = new Pen(Theme.Fixed.StateError, 1.5f) { DashStyle = DashStyle.Dash })
                using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    foreach (var code in Doc.Objects.OfType<BarcodeObject>())
                    {
                        string problem = EditorRender.Problem(code, Geometry, DateTime.Now);
                        if (problem == null) continue;
                        var r = ToScreen(code.Box);
                        g.DrawRectangle(bad, r.X, r.Y, r.Width, r.Height);
                        using (var b = new SolidBrush(Theme.Fixed.StateError)) g.DrawString("!", Font, b, r, fmt);
                    }
            // Snap guides and the selection.
            using (var guide = new Pen(Theme.Fixed.Petrol, 1f) { DashStyle = DashStyle.Dot })
            {
                if (guideX.HasValue) { var p = ToScreen(guideX.Value, 0); g.DrawLine(guide, p.X, label.Top - 10, p.X, label.Bottom + 10); }
                if (guideY.HasValue) { var p = ToScreen(0, guideY.Value); g.DrawLine(guide, label.Left - 10, p.Y, label.Right + 10, p.Y); }
            }
            // on an inverted object's black box: light, so it shows
            using (var pen = new Pen(Theme.Fixed.Petrol, 1f) { DashStyle = DashStyle.Dash })
            using (var light = new Pen(Theme.Fixed.PetrolLight, 1f) { DashStyle = DashStyle.Dash })
                foreach (var o in selection)
                {
                    if (editing != null && o == editing.Target) continue;
                    var p = Inverted(o) ? light : pen;
                    var ln = o as LineObject;
                    if (ln != null)
                    {
                        // A line: a light band along it, wider than the line (no box around a slanted line)
                        float wide = (float)ln.Thickness * PixelsPerMm + LogicalToDeviceUnits(6);
                        using (var band = new Pen(Color.FromArgb(90, Theme.Fixed.Petrol), wide) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                            g.DrawLine(band, ToScreen(ln.Start.X, ln.Start.Y), ToScreen(ln.End.X, ln.End.Y));
                        continue;
                    }
                    if (o.Tilt != 0) { g.DrawPolygon(p, Outline(o)); continue; }
                    var box = ToScreen(o.Box);
                    g.DrawRectangle(p, box.X, box.Y, Math.Max(1, box.Width), Math.Max(1, box.Height));
                }
            // Text frames too small for their text (the rest is cut off): a solid red line, above the selection's
            // dashed one so it stays visible.
            if (Doc != null)
                using (var cut = new Pen(Theme.Fixed.StateError, LogicalToDeviceUnits(2)) { Alignment = PenAlignment.Inset })
                    foreach (var t in Doc.Objects.OfType<TextObject>().Where(EditorRender.Overflows))
                    {
                        if (t.Tilt != 0) { g.DrawPolygon(cut, Outline(t)); continue; }
                        var r = ToScreen(t.Box);
                        g.DrawRectangle(cut, r.X, r.Y, r.Width, r.Height);
                    }
            DrawEditing(g);
            if (Selected != null && editing == null)
            {
                PointF stem, grip;
                if (Grip(Selected, out stem, out grip))
                {
                    float s = LogicalToDeviceUnits(9);
                    // the grip and the handles may sit on the desk: lines in AccentPressed (the dark petrol in light)
                    using (var line = new Pen(Theme.Of(Role.AccentPressed))) g.DrawLine(line, stem, grip);
                    if (drag == Drag.Turn) using (var fill = new SolidBrush(Theme.Of(Role.Accent))) g.FillEllipse(fill, grip.X - s / 2, grip.Y - s / 2, s, s);
                    else using (var paper = new SolidBrush(Theme.Fixed.Paper)) g.FillEllipse(paper, grip.X - s / 2, grip.Y - s / 2, s, s);
                    using (var edge = new Pen(Theme.Of(Role.AccentPressed), 1.5f)) g.DrawEllipse(edge, grip.X - s / 2, grip.Y - s / 2, s, s);
                }
                foreach (var h in Handles(Selected))
                {
                    using (var paper = new SolidBrush(Theme.Fixed.Paper)) g.FillRectangle(paper, h);
                    using (var edge = new Pen(Theme.Of(Role.AccentPressed))) g.DrawRectangle(edge, h.X, h.Y, h.Width, h.Height);
                }
            }
            if (drag == Drag.Turn && changeStarted && Selected != null) DrawTurn(g, Selected);
            if (drag == Drag.Resize && changeStarted && Selected is LineObject) DrawLineAngle(g, (LineObject)Selected);
            if (drag == Drag.Frame)
            {
                var r = FrameRect;
                using (var fill = new SolidBrush(Color.FromArgb(40, Theme.Fixed.PetrolLight))) g.FillRectangle(fill, r);
                using (var pen = new Pen(Theme.Of(Role.Accent), 1f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            PaintDrawing(g);
        }

        Rectangle FrameRect
        {
            get { return Rectangle.FromLTRB(Math.Min(frameStart.X, frameEnd.X), Math.Min(frameStart.Y, frameEnd.Y), Math.Max(frameStart.X, frameEnd.X), Math.Max(frameStart.Y, frameEnd.Y)); }
        }

        // White on black: an inverted text, or an inverted QR code.
        static bool Inverted(LabelObject o)
        {
            var t = o as TextObject; var b = o as BarcodeObject;
            return t != null ? t.InvertBox : b != null && b.Invert && b.Kind == Symbology.Qr;
        }

        // The label's length: a measure along its end-to-end side, the value in a tag; the grip on its end; while
        // dragging, the new end as a dashed line and the new value.
        void DrawLength(Graphics g, RectangleF label)
        {
            if (LengthText == null || !Continuous) return;
            bool across = Geometry.Across, dragging = drag == Drag.Length;
            float end = dragging ? (across ? ToScreen(0, lengthDrag).Y : ToScreen(lengthDrag, 0).X) : (across ? label.Bottom : label.Right);
            string text = dragging ? Media.Mm(Math.Round(lengthDrag, 1)) + " mm" : LengthText;
            float gap = LogicalToDeviceUnits(14), tick = LogicalToDeviceUnits(5);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Of(Role.Accent), 1.5f))
            {
                if (dragging)
                    using (var cut = new Pen(Theme.Of(Role.Accent), 1.5f) { DashStyle = DashStyle.Dash })
                        if (across) g.DrawLine(cut, label.Left - 6, end, label.Right + 6, end); else g.DrawLine(cut, end, label.Top - 6, end, label.Bottom + 6);
                if (across)
                {
                    float x = label.Right + gap;
                    g.DrawLine(pen, x, label.Top, x, end); g.DrawLine(pen, x - tick, label.Top, x + tick, label.Top); g.DrawLine(pen, x - tick, end, x + tick, end);
                }
                else
                {
                    float y = label.Bottom + gap;
                    g.DrawLine(pen, label.Left, y, end, y); g.DrawLine(pen, label.Left, y - tick, label.Left, y + tick); g.DrawLine(pen, end, y - tick, end, y + tick);
                }
            }
            var size = TextRenderer.MeasureText(text, Font);
            var tag = new RectangleF(0, 0, size.Width + LogicalToDeviceUnits(8), size.Height + LogicalToDeviceUnits(2));
            var state = g.Save();
            if (across)
            {
                // turned, along the measure on the right
                g.TranslateTransform(label.Right + gap, (label.Top + end) / 2);
                g.RotateTransform(-90);
            }
            else g.TranslateTransform((label.Left + end) / 2, label.Bottom + gap);
            tag.Offset(-tag.Width / 2, -tag.Height / 2);
            using (var back = new SolidBrush(Theme.Of(Role.InfoBack))) g.FillRectangle(back, tag);
            using (var edge = new Pen(Theme.Of(Role.DeskLine))) g.DrawRectangle(edge, tag.X, tag.Y, tag.Width, tag.Height);
            using (var b = new SolidBrush(Theme.Of(Role.InfoText)))
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, Font, b, tag, fmt);
            g.Restore(state);
            if (!LengthGrip) return;
            var r = LengthGripRect(label, end);
            using (var path = new GraphicsPath())
            {
                float d = Math.Min(r.Width, r.Height);
                path.AddArc(r.X, r.Y, d, d, across ? 90 : 180, 180);
                if (across) path.AddArc(r.Right - d, r.Y, d, d, 270, 180); else path.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
                path.CloseFigure();
                using (var b = new SolidBrush(Theme.Of(Role.Primary))) g.FillPath(b, path);   // High Contrast: Highlight
                using (var p = new Pen(Theme.Of(Role.OnPrimary), 1.5f)) g.DrawPath(p, path);
            }
            using (var p = new Pen(Theme.Of(Role.OnPrimary), 1.5f))
            {
                float c = across ? r.Y + r.Height / 2 : r.X + r.Width / 2, m = across ? r.X + r.Width / 2 : r.Y + r.Height / 2, l = LogicalToDeviceUnits(5), w = LogicalToDeviceUnits(2);
                if (across) { g.DrawLine(p, m - l, c - w, m + l, c - w); g.DrawLine(p, m - l, c + w, m + l, c + w); }
                else { g.DrawLine(p, c - w, m - l, c - w, m + l); g.DrawLine(p, c + w, m - l, c + w, m + l); }
            }
        }

        // The grip on the label's end (the right edge along the label, the bottom edge across the tape), screen pixels.
        RectangleF LengthGripRect(RectangleF label, float end)
        {
            float thin = LogicalToDeviceUnits(10), wide = LogicalToDeviceUnits(30);
            return Geometry.Across ? new RectangleF((label.Left + label.Right - wide) / 2, end - thin / 2, wide, thin)
                                   : new RectangleF(end - thin / 2, (label.Top + label.Bottom - wide) / 2, thin, wide);
        }

        bool OnLengthGrip(Point p)
        {
            if (!LengthGrip || LengthText == null || Geometry == null || !Continuous) return false;
            var label = ToScreen(new RectangleF(0, 0, (float)Geometry.ViewWidthMm, (float)Geometry.ViewHeightMm));
            return RectangleF.Inflate(LengthGripRect(label, Geometry.Across ? label.Bottom : label.Right), 3, 3).Contains(p);
        }

        // The selected codes' quiet zones: a light margin around the code (orange when something reaches into it).
        void DrawQuietZones(Graphics g)
        {
            if (QuietZoneOf == null) return;
            foreach (var code in selection.OfType<BarcodeObject>())
            {
                var zone = ToScreen(QuietZoneOf(code));
                var box = ToScreen(code.Box);
                if (zone.Width - box.Width < 1 && zone.Height - box.Height < 1) continue;
                bool crowded = Crowded.Contains(code);
                using (var region = new Region(zone))
                using (var fill = new SolidBrush(crowded ? Color.FromArgb(120, Theme.Fixed.Crowded) : Color.FromArgb(60, Theme.Fixed.PetrolLight)))
                using (var edge = new Pen(crowded ? Theme.Fixed.CrowdedLine : Color.FromArgb(120, Theme.Fixed.PetrolLight)))
                {
                    region.Exclude(box);
                    g.FillRegion(fill, region);
                    g.DrawRectangle(edge, zone.X, zone.Y, zone.Width, zone.Height);
                }
            }
        }

        bool OnQuietZone(PointF mm)
        {
            return QuietZoneOf != null && selection.OfType<BarcodeObject>().Any(c => QuietZoneOf(c).Contains(mm) && !c.Box.Contains(mm));
        }

        // While turning: the angle next to the mouse; snapped to 0/90/180/270°, a dotted guide through the centre.
        void DrawTurn(Graphics g, LabelObject o)
        {
            if (o.Tilt == 0)
            {
                var c = CentreOf(o.Box);
                var sc = ToScreen(c.X, c.Y);
                float reach = Math.Max(o.Box.Width, o.Box.Height) * PixelsPerMm / 2 + LogicalToDeviceUnits(30);
                bool level = o.Rotation % 180 == 0;
                using (var guide = new Pen(Theme.Of(Role.DeskLine), 1f) { DashStyle = DashStyle.Dot })
                    if (level) g.DrawLine(guide, sc.X - reach, sc.Y, sc.X + reach, sc.Y);
                    else g.DrawLine(guide, sc.X, sc.Y - reach, sc.X, sc.Y + reach);
            }
            string text = o.Angle.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + "°";
            var size = g.MeasureString(text, Font);
            var r = new RectangleF(turnMouse.X + LogicalToDeviceUnits(14), turnMouse.Y + LogicalToDeviceUnits(10), size.Width + 4, size.Height + 2);
            DrawTag(g, r, text, Font);
        }

        // A value next to the mouse (angle, length), as the label's length tag.
        static void DrawTag(Graphics g, RectangleF r, string text, Font font)
        {
            using (var back = new SolidBrush(Theme.Of(Role.InfoBack))) g.FillRectangle(back, r);
            using (var edge = new Pen(Theme.Of(Role.DeskLine))) g.DrawRectangle(edge, r.X, r.Y, r.Width, r.Height);
            using (var b = new SolidBrush(Theme.Of(Role.InfoText))) g.DrawString(text, font, b, r.X + 2, r.Y + 1);
        }

        // While dragging a line's end: its angle (0-180°, counter-clockwise from the horizontal) and length next to the
        // mouse; on a 45° step a dotted guide through the line.
        void DrawLineAngle(Graphics g, LineObject l)
        {
            PointF a = ToScreen(l.Start.X, l.Start.Y), b = ToScreen(l.End.X, l.End.Y);
            if (lineSnapped)
            {
                float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len > 0)
                {
                    float reach = LogicalToDeviceUnits(30) / len;
                    using (var guide = new Pen(Theme.Of(Role.DeskLine), 1f) { DashStyle = DashStyle.Dot })
                        g.DrawLine(guide, a.X - dx * reach, a.Y - dy * reach, b.X + dx * reach, b.Y + dy * reach);
                }
            }
            double angle = LineAngle(l.Start, l.End);
            double length = Math.Sqrt(l.W * l.W + l.H * l.H);
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            string text = angle.ToString("0.#", culture) + "°  " + length.ToString("0.0", culture) + " mm";
            var size = g.MeasureString(text, Font);
            var r = new RectangleF(turnMouse.X + LogicalToDeviceUnits(14), turnMouse.Y + LogicalToDeviceUnits(10), size.Width + 4, size.Height + 2);
            DrawTag(g, r, text, Font);
        }

        // A line's angle, 0 to 180° counter-clockwise from the horizontal (a line has no direction).
        internal static double LineAngle(PointF a, PointF b)
        {
            double angle = Math.Atan2(-(b.Y - a.Y), b.X - a.X) * 180 / Math.PI;
            angle = Math.Round(((angle % 180) + 180) % 180, 1);
            return angle >= 180 ? 0 : angle;
        }

        // A dragged line end on an angle step from the fixed end: 45° steps when within 5° of one, every 15° with
        // Shift; the end keeps its distance along that direction. Null: not near a step (or no length).
        internal static PointF? SnapLineEnd(PointF fixedEnd, PointF mouse, bool shift)
        {
            double dx = mouse.X - fixedEnd.X, dy = mouse.Y - fixedEnd.Y;
            if (dx == 0 && dy == 0) return null;
            double angle = Math.Atan2(dy, dx) * 180 / Math.PI, unit = shift ? 15 : 45, step = Math.Round(angle / unit) * unit;
            if (!shift && Math.Abs(angle - step) > 5) return null;
            double ux = Math.Round(Math.Cos(step * Math.PI / 180), 9), uy = Math.Round(Math.Sin(step * Math.PI / 180), 9);
            double along = dx * ux + dy * uy;
            return new PointF(ux == 0 ? fixedEnd.X : (float)(fixedEnd.X + along * ux), uy == 0 ? fixedEnd.Y : (float)(fixedEnd.Y + along * uy));
        }

        // The objects' parts beyond the label: light grey boxes, the objects in grey, hatched, a dashed edge.
        void DrawOutside(Graphics g, RectangleF label, bool round, float k)
        {
            var objects = Doc.Objects.Where(o => EditorRender.Beyond(o, Geometry)).ToList();
            if (objects.Count == 0) { DropGreyed(); return; }
            UpdateGreyed(objects);
            var parts = objects.Select(o => OutsideShape(o)).ToList();
            using (var shape = Shape(label, round, 1.5f * k))
            using (var beyond = new Region(ClientRectangle))
            {
                beyond.Exclude(shape);
                var clip = g.Clip;
                g.SetClip(beyond, CombineMode.Intersect);
                using (var fill = new SolidBrush(Theme.Fixed.TapeOutside))
                    foreach (var r in parts) g.FillPolygon(fill, r);
                if (greyed != null)
                {
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(greyed, greyedAt, 0, 0, greyedAt.Width, greyedAt.Height, GraphicsUnit.Pixel);
                }
                using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(70, Theme.Fixed.TapeEdge), Color.Transparent))
                    foreach (var r in parts) g.FillPolygon(hatch, r);
                g.Clip = clip;
            }
            using (var dash = new Pen(Theme.Fixed.TapeEdge, 1f) { DashStyle = DashStyle.Dash })
                foreach (var r in parts) g.DrawPolygon(dash, r);
        }

        void DrawHint(Graphics g, RectangleF label)
        {
            string text = Lang.T("Empty label. Choose a tool on the left and drag on the label.\n" +
                                 "Hatched: the printer can't print there.");
            using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(Theme.Fixed.PaperHint))
                g.DrawString(text, Font, b, RectangleF.Inflate(label, -6, -6), fmt);
        }

        static GraphicsPath Shape(RectangleF r, bool round, float radius)
        {
            var p = new GraphicsPath();
            if (round) { p.AddEllipse(r); return p; }
            float d = Math.Max(1f, Math.Min(radius * 2, Math.Min(r.Width, r.Height) / 2));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // ---------- handles and hit test ----------
        List<RectangleF> Handles(LabelObject o)
        {
            var list = new List<RectangleF>();
            float s = LogicalToDeviceUnits(7);
            var line = o as LineObject;
            if (line != null)
            {
                foreach (var p in new[] { line.Start, line.End })
                {
                    var q = ToScreen(p.X, p.Y);
                    list.Add(new RectangleF(q.X - s / 2, q.Y - s / 2, s, s));
                }
                return list;
            }
            if (!o.Resizable) return list;
            var b = o.Box;
            var c = CentreOf(b);
            float[] xs = { b.Left, b.Left + b.Width / 2, b.Right }, ys = { b.Top, b.Top + b.Height / 2, b.Bottom };
            int[,] at = { { 0, 0 }, { 1, 0 }, { 2, 0 }, { 2, 1 }, { 2, 2 }, { 1, 2 }, { 0, 2 }, { 0, 1 } };
            for (int i = 0; i < 8; i++)
            {
                var p = LabelObject.Turn(new PointF(xs[at[i, 0]], ys[at[i, 1]]), c, o.Tilt);
                var q = ToScreen(p.X, p.Y);
                list.Add(new RectangleF(q.X - s / 2, q.Y - s / 2, s, s));
            }
            return list;
        }

        // The turn grip: a round handle on a short stem above the object's own top edge, turned with the whole angle
        // (quarter turns too: beyond 45° the box swaps its sides, the grip stays under the mouse). Text, boxes and
        // pictures only.
        bool Grip(LabelObject o, out PointF stem, out PointF grip)
        {
            stem = grip = PointF.Empty;
            if (o == null || !o.CanTilt || o is LineObject) return false;
            var b = o.Box;
            var c = CentreOf(b);
            float half = (o.Rotation % 180 == 0 ? b.Height : b.Width) / 2, up = LogicalToDeviceUnits(24) / PixelsPerMm;
            var a = LabelObject.Turn(new PointF(c.X, c.Y - half), c, o.Angle);
            var z = LabelObject.Turn(new PointF(c.X, c.Y - half - up), c, o.Angle);
            stem = ToScreen(a.X, a.Y);
            grip = ToScreen(z.X, z.Y);
            return true;
        }

        bool OnGrip(Point p)
        {
            PointF stem, grip;
            if (!Grip(Selected, out stem, out grip)) return false;
            float r = LogicalToDeviceUnits(8);
            return (p.X - grip.X) * (p.X - grip.X) + (p.Y - grip.Y) * (p.Y - grip.Y) <= r * r;
        }

        // The mouse's direction from the turn centre, clockwise from straight up (degrees).
        double Direction(PointF mm)
        {
            return Math.Atan2(mm.X - turnCentre.X, -(mm.Y - turnCentre.Y)) * 180 / Math.PI;
        }

        LabelObject HitObject(PointF mm)
        {
            float tol = LogicalToDeviceUnits(4) / PixelsPerMm;
            for (int i = Doc.Objects.Count - 1; i >= 0; i--)
            {
                var o = Doc.Objects[i];
                var line = o as LineObject;
                if (line != null)
                {
                    if (DistanceToSegment(mm, line.Start, line.End) <= tol + line.Thickness / 2) return o;
                }
                else if (RectangleF.Inflate(o.Box, tol, tol).Contains(o.Tilt == 0 ? mm : LabelObject.Turn(mm, CentreOf(o.Box), -o.Tilt))) return o;
            }
            return null;
        }

        static double DistanceToSegment(PointF p, PointF a, PointF b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, len = dx * dx + dy * dy;
            double t = len == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len));
            double x = a.X + t * dx - p.X, y = a.Y + t * dy - p.Y;
            return Math.Sqrt(x * x + y * y);
        }

        // ---------- mouse ----------
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (Doc == null || Geometry == null) return;
            var mm = ToMm(e.Location);
            if (DrawMouseDown(e, mm)) return;   // a tool on: drawing (EditorCanvas.Draw.cs)
            if (EditMouseDown(e, mm)) return;
            // Right button: the object under the mouse (its context menu opens on release; a selected one keeps the
            // selection), or nothing.
            if (e.Button == MouseButtons.Right)
            {
                var at = HitObject(mm);
                if (at == null || !selection.Contains(at)) Selected = at;
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            bool add = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            drag = Drag.None;
            if (!add && OnLengthGrip(e.Location))
            {
                drag = Drag.Length;
                lengthDrag = Geometry.Across ? Geometry.ViewHeightMm : Geometry.ViewWidthMm;
                keptExtent = Extent;
                Capture = true;
                Invalidate();
                return;
            }
            if (Selected != null && !add && OnGrip(e.Location))
            {
                drag = Drag.Turn;
                turnFrom = Selected.Angle;
                turnCentre = CentreOf(Selected.Box);
                turnDown = Direction(mm);
                turnMouse = e.Location;
            }
            else if (Selected != null && !add)
            {
                var hs = Handles(Selected);
                for (int i = 0; i < hs.Count; i++)
                    if (RectangleF.Inflate(hs[i], 2, 2).Contains(e.Location)) { drag = Drag.Resize; handle = i; break; }
            }
            if (drag == Drag.None)
            {
                var hit = HitObject(mm);
                if (hit == null)
                {
                    // An empty spot: a selection frame (with Ctrl/Shift it adds to the selection).
                    frameBase = add ? selection.ToList() : new List<LabelObject>();
                    if (!add) Selected = null;
                    frameStart = frameEnd = e.Location;
                    keptExtent = Extent;
                    drag = Drag.Frame;
                    Capture = true;
                    return;
                }
                if (add)
                {
                    // Ctrl/Shift+click: in or out of the selection.
                    if (selection.Contains(hit)) { Select(selection.Where(o => o != hit).ToList()); return; }
                    Select(selection.Concat(new[] { hit }).ToList());
                }
                else if (!selection.Contains(hit)) Selected = hit;
                drag = Drag.Move;
            }
            downMm = mm;
            keptExtent = Extent;
            if (drag == Drag.Move)
            {
                startBox = BoxOf(selection);
                startAt = selection.ToDictionary(o => o, o => new PointF((float)o.X, (float)o.Y));
            }
            else
            {
                var o = Selected;
                startBox = o.Box;
                var line = o as LineObject;
                startRising = line != null && line.Rising;
                var text = o as TextObject;
                startSize = text != null ? text.Size : 0;
                startRuns = text != null && text.FormatRuns != null ? text.FullRuns() : null;
            }
            changeStarted = false;
            Capture = true;
        }

        // Everything the frame touches, with what was selected before (Ctrl/Shift).
        void FrameSelect()
        {
            var r = FrameRect;
            var a = ToMm(r.Location);
            var b = ToMm(new Point(r.Right, r.Bottom));
            var mm = RectangleF.FromLTRB(a.X, a.Y, b.X, b.Y);
            Select(frameBase.Concat(Doc.Objects.Where(o => !frameBase.Contains(o) && RectangleF.Inflate(o.Bounds, 0.01f, 0.01f).IntersectsWith(mm))).ToList());
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (DrawMouseMove(e)) return;
            if (EditMouseMove(e)) return;
            if (drag == Drag.None)
            {
                Cursor = Cursors.Default;
                if (OnLengthGrip(e.Location)) Cursor = Geometry.Across ? Cursors.SizeNS : Cursors.SizeWE;
                else if (Selected != null && OnGrip(e.Location)) Cursor = Cursors.Cross;
                else if (Selected != null && Handles(Selected).Any(h => RectangleF.Inflate(h, 2, 2).Contains(e.Location))) Cursor = Cursors.SizeAll;
                else if (Doc != null && Geometry != null && HitObject(ToMm(e.Location)) != null) Cursor = Cursors.Hand;
                bool zone = Doc != null && Geometry != null && OnQuietZone(ToMm(e.Location));
                if (zone != onZone) { onZone = zone; zoneTip.SetToolTip(this, zone ? QuietTip : null); }
                return;
            }
            if (drag == Drag.Length)
            {
                var at = ToMm(e.Location);
                double v = Geometry.Across ? at.Y : at.X;
                v = (ModifierKeys & Keys.Alt) != 0 ? Math.Round(v, 1) : Math.Round(v);
                lengthDrag = Math.Max(LengthMin, Math.Min(LengthMax, v));
                Invalidate();
                return;
            }
            if (drag == Drag.Frame)
            {
                frameEnd = e.Location;
                FrameSelect();
                Invalidate();
                return;
            }
            var mm = ToMm(e.Location);
            float dx = mm.X - downMm.X, dy = mm.Y - downMm.Y;
            if (!changeStarted)
            {
                if (Math.Abs(dx) * PixelsPerMm < 2 && Math.Abs(dy) * PixelsPerMm < 2) return;   // a click, not a drag
                changeStarted = true;
                if (BeforeChange != null) BeforeChange(this, EventArgs.Empty);
            }
            bool free = (ModifierKeys & Keys.Alt) != 0;   // Alt: no snapping
            if (free) altInDrag = true;   // its release opens no menu (EditorForm.AltKey.cs)
            turnMouse = e.Location;
            if (drag == Drag.Turn) TurnTo(mm, free);
            else if (drag == Drag.Move) MoveTo(startBox.X + dx, startBox.Y + dy, free);
            else ResizeTo(mm, free);
            if (Changed != null) Changed(this, EventArgs.Empty);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (DrawMouseUp(e)) return;
            if (drag == Drag.Text) { drag = Drag.None; Capture = false; return; }
            bool length = drag == Drag.Length;
            drag = Drag.None;
            if (length && LengthDragged != null) LengthDragged(Math.Round(lengthDrag, 1));
            Capture = false;
            guideX = guideY = null;
            lineSnapped = false;
            if (keptExtent.HasValue)
            {
                keptExtent = null;
                if (ViewChanged != null) ViewChanged(this, EventArgs.Empty);
            }
            Invalidate();
        }

        // Snap lines: label edges and centre, printable area edges (mm).
        float[] SnapX { get { var p = Geometry.Printable; return new[] { 0f, (float)Geometry.ViewWidthMm, (float)Geometry.ViewWidthMm / 2, p.Left, p.Right }; } }
        float[] SnapY { get { var p = Geometry.Printable; return new[] { 0f, (float)Geometry.ViewHeightMm, (float)Geometry.ViewHeightMm / 2, p.Top, p.Bottom }; } }

        // Snaps one of the values (left, centre, right) to a line: the shift, or 0.
        float Snap(float[] lines, float[] values, bool free, out float? guide)
        {
            guide = null;
            if (free) return 0;
            float tol = LogicalToDeviceUnits(6) / PixelsPerMm, best = tol + 1;
            float shift = 0;
            foreach (var l in lines)
                foreach (var v in values)
                    if (Math.Abs(l - v) < best) { best = Math.Abs(l - v); shift = l - v; guide = l; }
            return best <= tol ? shift : 0;
        }

        // x, y: the new top left corner of the selection's box; the selected objects move together.
        void MoveTo(float x, float y, bool free)
        {
            float w = startBox.Width, h = startBox.Height;
            x += Snap(SnapX, new[] { x, x + w / 2, x + w }, free, out guideX);
            y += Snap(SnapY, new[] { y, y + h / 2, y + h }, free, out guideY);
            foreach (var kv in startAt)
            {
                kv.Key.X = Math.Round(kv.Value.X + x - startBox.X, 2);
                kv.Key.Y = Math.Round(kv.Value.Y + y - startBox.Y, 2);
            }
        }

        // Turning with the grip: snaps to 0/90/180/270° within 5° (Shift: 15° steps; Alt: free, 0.1°).
        void TurnTo(PointF mm, bool free)
        {
            var o = Selected;
            if (o == null) return;
            double a = turnFrom + Direction(mm) - turnDown;
            a = ((a % 360) + 540) % 360 - 180;   // -180 .. 180
            if (free) a = Math.Round(a, 1);
            else if ((ModifierKeys & Keys.Shift) != 0) a = Math.Round(a / 15) * 15;
            else
            {
                double quarter = Math.Round(a / 90) * 90;
                a = Math.Abs(a - quarter) <= 5 ? quarter : Math.Round(a);
            }
            if (a <= -180) a += 360;
            o.SetAngle(a);
            var t = o as TextObject;
            if (t != null)
            {
                // A text refits its box; it stays centred where the turn began.
                EditorRender.FitText(t);
                t.X = Math.Round(turnCentre.X - t.W / 2, 2);
                t.Y = Math.Round(turnCentre.Y - t.H / 2, 2);
            }
        }

        void ResizeTo(PointF mm, bool free)
        {
            var o = Selected;
            if (o != null && o.Tilt != 0 && !(o is LineObject))
            {
                // A tilted object: the handles work in its own (untilted) frame, without snapping; afterwards the
                // box's centre is turned back, so the side or corner opposite the handle stays where it was.
                var c0 = CentreOf(startBox);
                ResizeBox(LabelObject.Turn(mm, c0, -o.Tilt), true);
                var c = CentreOf(o.Box);
                var turned = LabelObject.Turn(c, c0, o.Tilt);
                o.X = Math.Round(turned.X - o.W / 2, 2);
                o.Y = Math.Round(turned.Y - o.H / 2, 2);
                return;
            }
            ResizeBox(mm, free);
        }

        void ResizeBox(PointF mm, bool free)
        {
            float? gx, gy;
            float sx = mm.X + Snap(SnapX, new[] { mm.X }, free, out gx), sy = mm.Y + Snap(SnapY, new[] { mm.Y }, free, out gy);
            guideX = gx; guideY = gy;
            var selected = Selected;
            var line = selected as LineObject;
            var b = startBox;
            if (line != null)
            {
                // Drag one end; the other stays. The angle snaps to 45° steps within 5° (Shift: 15° steps; Alt: free).
                // On a step the end moves along that direction; level and upright lines still snap to the edges.
                var fixedEnd = EndOf(b, startRising, handle != 0);
                lineSnapped = false;
                if (!free)
                {
                    var end = SnapLineEnd(fixedEnd, mm, (ModifierKeys & Keys.Shift) != 0);
                    if (end.HasValue)
                    {
                        lineSnapped = true;
                        sx = end.Value.X; sy = end.Value.Y;
                        guideX = guideY = null;
                        if (sy == fixedEnd.Y) { sx += Snap(SnapX, new[] { sx }, false, out gx); guideX = gx; }
                        else if (sx == fixedEnd.X) { sy += Snap(SnapY, new[] { sy }, false, out gy); guideY = gy; }
                    }
                }
                float x0 = Math.Min(fixedEnd.X, sx), x1 = Math.Max(fixedEnd.X, sx), y0 = Math.Min(fixedEnd.Y, sy), y1 = Math.Max(fixedEnd.Y, sy);
                line.X = Math.Round(x0, 2); line.Y = Math.Round(y0, 2); line.W = Math.Round(x1 - x0, 2); line.H = Math.Round(y1 - y0, 2);
                // Rising when one end is bottom left and the other top right.
                line.Rising = (sx - fixedEnd.X) * (sy - fixedEnd.Y) < 0;
                return;
            }
            float left = b.Left, top = b.Top, right = b.Right, bottom = b.Bottom;
            if (handle == 0 || handle == 6 || handle == 7) left = sx;
            if (handle == 2 || handle == 3 || handle == 4) right = sx;
            if (handle == 0 || handle == 1 || handle == 2) top = sy;
            if (handle == 4 || handle == 5 || handle == 6) bottom = sy;
            const float min = 0.5f;
            if (right - left < min) { if (left != b.Left) left = right - min; else right = left + min; }
            if (bottom - top < min) { if (top != b.Top) top = bottom - min; else bottom = top + min; }
            // Pictures keep their proportions at the corners (Shift: free).
            if (selected is ImageObject && handle % 2 == 0 && (ModifierKeys & Keys.Shift) == 0 && b.Width > 0 && b.Height > 0)
            {
                float ratio = b.Width / b.Height, w = right - left, h = bottom - top;
                if (w / h > ratio) h = w / ratio; else w = h * ratio;
                if (left != b.Left) left = right - w; else right = left + w;
                if (top != b.Top) top = bottom - h; else bottom = top + h;
            }
            // A QR code stays square: at a corner the larger side wins; at an edge the other side follows, centred.
            var qr = selected as BarcodeObject;
            if (qr != null && qr.Kind == Symbology.Qr)
            {
                float w = right - left, h = bottom - top;
                if (handle % 2 == 0)
                {
                    float side = Math.Max(w, h);
                    if (left != b.Left) left = right - side; else right = left + side;
                    if (top != b.Top) top = bottom - side; else bottom = top + side;
                }
                else if (handle == 3 || handle == 7) { float cy = (b.Top + b.Bottom) / 2; top = cy - w / 2; bottom = cy + w / 2; }
                else { float cx = (b.Left + b.Right) / 2; left = cx - h / 2; right = cx + h / 2; }
            }
            // Shapes at a corner with Shift: regular (square, circle, equilateral triangle, even polygon or star); the
            // larger change wins, the opposite corner stays.
            var shape = selected as ShapeObject;
            if (shape != null && handle % 2 == 0 && (ModifierKeys & Keys.Shift) != 0)
            {
                float ratio = (float)shape.NaturalRatio, w = right - left, h = bottom - top;
                if (w / h > ratio) h = w / ratio; else w = h * ratio;
                if (left != b.Left) left = right - w; else right = left + w;
                if (top != b.Top) top = bottom - h; else bottom = top + h;
            }
            var text = selected as TextObject;
            if (text != null && text.Kind == TextKind.Line) { ScaleText(text, b, right - left, bottom - top); return; }
            selected.X = Math.Round(left, 2); selected.Y = Math.Round(top, 2);
            selected.W = Math.Round(right - left, 2); selected.H = Math.Round(bottom - top, 2);
        }

        // A single-line text: the handle makes the font larger or smaller in proportion (a side handle by its
        // direction, a corner by the direction that changed more); the box follows the text, and the opposite
        // side or corner stays where it was.
        void ScaleText(TextObject t, RectangleF b, float w, float h)
        {
            float fw = w / b.Width, fh = h / b.Height;
            float f = handle == 1 || handle == 5 ? fh : handle == 3 || handle == 7 ? fw : Math.Abs(fw - 1) > Math.Abs(fh - 1) ? fw : fh;
            Func<double, double> scaled = size => Math.Round(Math.Min(400, Math.Max(2, size * f)) * 10) / 10;
            if (startRuns != null) t.SetRuns(startRuns.Select(r => r.With(size: scaled(r.Size.Value))).ToList());
            else t.Size = scaled(startSize);
            EditorRender.FitBox(t);
            double x = handle == 0 || handle == 6 || handle == 7 ? b.Right - t.W : handle == 2 || handle == 3 || handle == 4 ? b.Left : b.Left + (b.Width - t.W) / 2;
            double y = handle == 0 || handle == 1 || handle == 2 ? b.Bottom - t.H : handle == 4 || handle == 5 || handle == 6 ? b.Top : b.Top + (b.Height - t.H) / 2;
            t.X = Math.Round(x, 2); t.Y = Math.Round(y, 2);
        }

        static PointF EndOf(RectangleF b, bool rising, bool start)
        {
            if (rising) return start ? new PointF(b.Left, b.Bottom) : new PointF(b.Right, b.Top);
            return start ? new PointF(b.Left, b.Top) : new PointF(b.Right, b.Bottom);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (editing != null) return true;   // every key goes to the text (the window routes them first: EditKey)
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && rendered != null) rendered.Dispose();
            if (disposing && shiftWatch != null) shiftWatch.Dispose();
            if (disposing) DropView();
            if (disposing) DropGreyed();
            if (disposing) zoneTip.Dispose();
            if (disposing) blink.Dispose();
            base.Dispose(disposing);
        }
    }
}
