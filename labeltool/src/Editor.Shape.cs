// Label editor, one object kind: shapes (rectangle with optional round corners, ellipse, triangle, polygon, star): the
// shape object, its file keys, its outline/fill drawing, the settings rows (shape, corners or points, inner radius,
// round corners, line width, filled) and the insert commands (editor-next step 3, 2026-11-08; before: BoxObject).
// File: a rectangle or an ellipse stays "type":"box" (+ "radius" for round corners), so versions before 0.21 still show
// it (with square corners); triangle, polygon and star are "type":"shape" with "shape", "corners"/"points", "inner".
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    // The order of the panel's "Shape" list.
    enum ShapeKind { Rectangle, Ellipse, Triangle, Polygon, Star }

    sealed class ShapeObject : LabelObject
    {
        public ShapeKind Kind;
        public double Thickness = 0.5;  // mm (outline)
        public bool Filled;
        public double Radius;           // mm, round corners of a rectangle (0 = square)
        public int Sides = 6;           // polygon: corners, 3-20
        public int Points = 5;          // star, 3-20
        public int Inner = 50;          // star: inner radius in % of the outer, 10-90

        public const int MinCount = 3, MaxCount = 20, MinInner = 10, MaxInner = 90;
        public const double MaxRadius = 50;

        public bool IsBox { get { return Kind == ShapeKind.Rectangle || Kind == ShapeKind.Ellipse; } }

        public override string Type { get { return IsBox ? "box" : "shape"; } }
        protected override string[] Keys { get { return new[] { "thickness", "filled", "ellipse", "radius", "shape", "corners", "points", "inner" }; } }

        protected override void Write(Dictionary<string, object> d)
        {
            d["thickness"] = R(Thickness); d["filled"] = Filled;
            if (IsBox)
            {
                d["ellipse"] = Kind == ShapeKind.Ellipse;
                if (Kind == ShapeKind.Rectangle && Radius > 0) d["radius"] = R(Radius);
                return;
            }
            d["shape"] = Kind == ShapeKind.Triangle ? "triangle" : Kind == ShapeKind.Polygon ? "polygon" : "star";
            if (Kind == ShapeKind.Polygon) d["corners"] = Sides;
            if (Kind == ShapeKind.Star) { d["points"] = Points; d["inner"] = Inner; }
        }

        protected override void Read(Dictionary<string, object> d)
        {
            Thickness = Math.Min(20, Math.Max(0.1, Num(d, "thickness", Thickness)));
            Filled = Bool(d, "filled", false);
            Radius = Math.Min(MaxRadius, Math.Max(0, Num(d, "radius", 0)));
            Sides = Count(Num(d, "corners", Sides));
            Points = Count(Num(d, "points", Points));
            double inner = Num(d, "inner", Inner);
            Inner = double.IsNaN(inner) ? 50 : (int)Math.Round(Math.Min(MaxInner, Math.Max(MinInner, inner)));
            switch (Str(d, "type", "box") == "box" ? "" : Str(d, "shape", ""))
            {
                case "": Kind = Bool(d, "ellipse", false) ? ShapeKind.Ellipse : ShapeKind.Rectangle; break;
                case "triangle": Kind = ShapeKind.Triangle; break;
                case "star": Kind = ShapeKind.Star; break;
                default: Kind = ShapeKind.Polygon; break;   // "polygon", or a shape of a newer version: its corners
            }
        }

        static int Count(double v) { return double.IsNaN(v) ? 5 : (int)Math.Round(Math.Min(MaxCount, Math.Max(MinCount, v))); }

        // Width : height at which the shape is regular (Shift while dragging): square, circle, equilateral triangle,
        // regular polygon or star (their corners on a circle, the box around them).
        public double NaturalRatio
        {
            get
            {
                if (IsBox) return 1;
                var p = UnitPoints();
                double w = p.Max(q => q.X) - p.Min(q => q.X), h = p.Max(q => q.Y) - p.Min(q => q.Y);
                return h > 0 ? w / h : 1;
            }
        }

        // Triangle, polygon, star: the corners on the unit circle, the first one at the top, clockwise.
        PointF[] UnitPoints()
        {
            int n = Kind == ShapeKind.Triangle ? 3 : Kind == ShapeKind.Polygon ? Sides : Points * 2;
            double inner = Inner / 100.0;
            var p = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                double a = (-90 + i * 360.0 / n) * Math.PI / 180, r = Kind == ShapeKind.Star && i % 2 == 1 ? inner : 1;
                p[i] = new PointF((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a)));
            }
            return p;
        }

        // The corners stretched to fill box (the shape touches all four sides).
        PointF[] PointsIn(RectangleF box)
        {
            var p = UnitPoints();
            float l = p.Min(q => q.X), t = p.Min(q => q.Y), w = p.Max(q => q.X) - l, h = p.Max(q => q.Y) - t;
            return p.Select(q => new PointF(box.X + (q.X - l) / w * box.Width, box.Y + (q.Y - t) / h * box.Height)).ToArray();
        }

        // Draws the shape in ink into box (unrotated, in mm; EditorRender: black). The outline stays inside the shape's edge, as the
        // box's always did, so a thicker line never makes the shape larger.
        public void Paint(Graphics g, RectangleF box, Color ink)
        {
            float w = box.Width, h = box.Height;
            if (Filled)
            {
                using (var brush = new SolidBrush(ink))
                using (var path = Path(box, (float)Radius)) g.FillPath(brush, path);
                return;
            }
            if (IsBox)
            {
                float t = (float)Math.Min(Thickness, Math.Min(w, h) / 2);
                var inner = RectangleF.Inflate(box, -t / 2, -t / 2);
                using (var pen = new Pen(ink, t))
                using (var path = Path(inner, Math.Max(0, (float)Radius - t / 2)))
                    g.DrawPath(pen, path);
                return;
            }
            // Triangle, polygon, star: the corners moved inwards by half the line width along both edges, drawn with
            // mitred corners: the line's outer edge is then the shape's edge, also at sharp star points.
            var p = PointsIn(box);
            // at most about half the space inside (else the moved corners cross over)
            float most = Math.Min(w, h) / 4 * (Kind == ShapeKind.Star ? Inner / 100f : 1);
            float d = (float)Math.Min(Thickness, most);
            var q = Offset(p, d / 2);
            using (var pen = new Pen(ink, d) { LineJoin = LineJoin.Miter, MiterLimit = 100 })
                g.DrawPolygon(pen, q);
        }

        // The filled shape's outline in box (radius: the rectangle's round corners).
        GraphicsPath Path(RectangleF box, float radius)
        {
            var path = new GraphicsPath();
            if (Kind == ShapeKind.Ellipse) path.AddEllipse(box);
            else if (Kind == ShapeKind.Rectangle)
            {
                float r = Math.Min(radius, Math.Min(box.Width, box.Height) / 2);
                if (r <= 0.01f) path.AddRectangle(box);
                else
                {
                    float dd = r * 2;
                    path.AddArc(box.X, box.Y, dd, dd, 180, 90);
                    path.AddArc(box.Right - dd, box.Y, dd, dd, 270, 90);
                    path.AddArc(box.Right - dd, box.Bottom - dd, dd, dd, 0, 90);
                    path.AddArc(box.X, box.Bottom - dd, dd, dd, 90, 90);
                    path.CloseFigure();
                }
            }
            else path.AddPolygon(PointsIn(box));
            return path;
        }

        // The polygon with every edge moved inwards by d (a mitred offset; the corners stay on the bisectors).
        static PointF[] Offset(PointF[] p, float d)
        {
            int n = p.Length;
            double area = 0;
            for (int i = 0; i < n; i++) area += p[i].X * p[(i + 1) % n].Y - p[(i + 1) % n].X * p[i].Y;
            float sign = area >= 0 ? 1 : -1;
            Func<PointF, PointF, PointF> normal = (a, b) =>
            {
                float dx = b.X - a.X, dy = b.Y - a.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                return len > 0 ? new PointF(-dy / len * sign, dx / len * sign) : new PointF(0, 0);
            };
            var q = new PointF[n];
            for (int i = 0; i < n; i++)
            {
                PointF prev = p[(i + n - 1) % n], cur = p[i], next = p[(i + 1) % n];
                PointF n1 = normal(prev, cur), n2 = normal(cur, next);
                float k = 1 + n1.X * n2.X + n1.Y * n2.Y;
                q[i] = k > 0.05f ? new PointF(cur.X + d * (n1.X + n2.X) / k, cur.Y + d * (n1.Y + n2.Y) / k) : cur;
            }
            return q;
        }
    }

    sealed partial class EditorForm
    {
        ComboBox shapeKindBox;
        NumericUpDown cornersNum, innerNum, radiusNum;
        Label cornersCaption;

        // The settings rows of a shape and the line width of a line (BuildProperties).
        void BuildShapeRows()
        {
            Func<LabelObject, bool> any = o => o is ShapeObject,
                shape = o => o is LineObject || (o is ShapeObject && !((ShapeObject)o).Filled),
                count = o => o is ShapeObject && (((ShapeObject)o).Kind == ShapeKind.Polygon || ((ShapeObject)o).Kind == ShapeKind.Star),
                star = o => o is ShapeObject && ((ShapeObject)o).Kind == ShapeKind.Star,
                rect = o => o is ShapeObject && ((ShapeObject)o).Kind == ShapeKind.Rectangle;
            // "Shape" switches the kind; size and position stay (mockup 5).
            shapeKindBox = Combo(Lang.T("Rectangle"), Lang.T("Ellipse"), Lang.T("Triangle"), Lang.T("Polygon"), Lang.T("Star"));
            shapeKindBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((ShapeObject)o).Kind = (ShapeKind)shapeKindBox.SelectedIndex);
            Row(Lang.T("Shape:"), shapeKindBox, any);
            // Corners of a polygon, points of a star: one row, its caption follows the kind.
            cornersNum = Number(ShapeObject.MinCount, ShapeObject.MaxCount, 1, 0);
            cornersNum.Width = S(70);
            cornersNum.ValueChanged += (s, e) => Edit(s, o =>
            {
                var sh = (ShapeObject)o;
                if (sh.Kind == ShapeKind.Star) sh.Points = (int)cornersNum.Value; else sh.Sides = (int)cornersNum.Value;
            });
            Row(Lang.T("Corners:"), cornersNum, count);
            cornersCaption = (Label)rows[rows.Count - 1].Key[0];
            innerNum = Number(ShapeObject.MinInner, ShapeObject.MaxInner, 5, 0);
            innerNum.ValueChanged += (s, e) => Edit(s, o => ((ShapeObject)o).Inner = (int)innerNum.Value);
            Row(Lang.T("Inner radius (%):"), innerNum, star);
            SpanRow(Hint(Lang.T("Smaller = sharper points.")), star);
            radiusNum = Number(0, (decimal)ShapeObject.MaxRadius, 0.5m, 1);
            radiusNum.ValueChanged += (s, e) => Edit(s, o => ((ShapeObject)o).Radius = (double)radiusNum.Value);
            Row(Lang.T("Round corners (mm):"), radiusNum, rect);
            SpanRow(Hint(Lang.T("0 = square corners.")), rect);
            thickNum = Number(0.1m, 20, 0.1m, 1);
            thickNum.ValueChanged += (s, e) => Edit(s, o =>
            {
                var l = o as LineObject; if (l != null) l.Thickness = (double)thickNum.Value;
                var b = o as ShapeObject; if (b != null) b.Thickness = (double)thickNum.Value;
            });
            Row(Lang.T("Line width (mm):"), thickNum, shape);
            filledCheck = Check(Lang.T("Filled"), (s, e) => Edit(s, o => ((ShapeObject)o).Filled = filledCheck.Checked));
            Row("", filledCheck, any);
        }

        // The panel's values of a shape (FillProps).
        void FillShape(ShapeObject b, object skip)
        {
            shapeKindBox.SelectedIndex = (int)b.Kind;
            if (skip != thickNum) thickNum.Value = Clamp(thickNum, b.Thickness);
            filledCheck.Checked = b.Filled;
            bool star = b.Kind == ShapeKind.Star;
            cornersCaption.Text = star ? Lang.T("Points:") : Lang.T("Corners:");
            if (skip != cornersNum) cornersNum.Value = Clamp(cornersNum, star ? b.Points : b.Sides);
            if (skip != innerNum) innerNum.Value = Clamp(innerNum, b.Inner);
            if (skip != radiusNum) radiusNum.Value = Clamp(radiusNum, b.Radius);
        }

        void AddShape(ShapeKind kind)
        {
            var at = FreeSpot();
            var o = new ShapeObject { Kind = kind, X = at.X, Y = at.Y, W = 15, H = 10 };
            if (!o.IsBox) { o.H = 12; o.W = Math.Round(12 * o.NaturalRatio, 2); }
            Add(o);
        }
    }
}
