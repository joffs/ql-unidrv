// The label editor's line icons, part of Brand (refactor S6, moved unchanged from Brand.cs): DrawToolIcon paints the
// tool palette, DrawFlyoutMark the corner triangle of a flyout group, DrawLayerIcon the layer commands (context menu),
// DrawArrangeIcon the Arrange toolbar.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace QlLabels
{
    // The label editor's tool palette (Brand.DrawToolIcon; Brand.DrawFlyoutMark marks a group with a flyout).
    // Text = ArtisticText and Box (rounded) stay until the palette uses the flyout groups.
    enum ToolIcon { Select, Text, Line, Box, Picture, Barcode, QrCode, ArtisticText, ParagraphText, Rectangle, Ellipse, Triangle, Polygon, Star }

    // The label editor's layer commands in the context menu (Brand.DrawLayerIcon).
    enum LayerIcon { BringToFront, BringForward, SendBackward, SendToBack }

    // The label editor's "Arrange" toolbar (Brand.DrawArrangeIcon).
    enum ArrangeIcon { Duplicate, Delete, RotateLeft, RotateRight, ResetRotation, AlignLeft, AlignCenter, AlignRight, AlignTop, AlignMiddle, AlignBottom,
                       DistributeHorizontally, DistributeVertically }

    static partial class Brand
    {
        // ---- tool icons ----

        // A line icon of the editor's tool palette, centred in r and drawn on a 24 grid (stroke 1.5 at 24 px), so it is
        // crisp at every display scaling. color: the Accent normally, OnPrimary on an accent background when the tool is
        // active, Dim when disabled (the caller paints the background).
        public static void DrawToolIcon(Graphics g, ToolIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), s = size / 24f;
            float x = r.X + (r.Width - size) / 2f, y = r.Y + (r.Height - size) / 2f;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (icon == ToolIcon.Barcode)
            {
                // Bars snapped to whole pixels: anti-aliased bar edges look blurred.
                int[,] bars = { { 4, 2 }, { 7, 1 }, { 9, 3 }, { 13, 1 }, { 15, 2 }, { 18, 1 }, { 20, 1 } };
                int top = (int)Math.Round(y + 5 * s), bottom = (int)Math.Round(y + 19 * s);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var brush = new SolidBrush(color))
                    for (int i = 0; i < bars.GetLength(0); i++)
                    {
                        int left = (int)Math.Round(x + bars[i, 0] * s), right = (int)Math.Round(x + (bars[i, 0] + bars[i, 1]) * s);
                        g.FillRectangle(brush, left, top, Math.Max(1, right - left), bottom - top);
                    }
                g.Restore(state);
                return;
            }
            if (icon == ToolIcon.QrCode)
            {
                // Three finder patterns (ring, gap, centre square) and a few modules in the fourth corner, on the
                // 24 grid, snapped to whole pixels like the barcode's bars.
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var brush = new SolidBrush(color))
                {
                    Action<float, float, float, float> cell = (cx, cy, cw, ch) =>
                    {
                        int left = (int)Math.Round(x + cx * s), top = (int)Math.Round(y + cy * s);
                        int right = Math.Max(left + 1, (int)Math.Round(x + (cx + cw) * s)), bottom = Math.Max(top + 1, (int)Math.Round(y + (cy + ch) * s));
                        g.FillRectangle(brush, left, top, right - left, bottom - top);
                    };
                    foreach (var at in new[] { new PointF(3, 3), new PointF(14, 3), new PointF(3, 14) })
                    {
                        // A real finder's proportions 1:1:3:1:1 (whole pixels at 24 px).
                        cell(at.X, at.Y, 7, 1); cell(at.X, at.Y + 6, 7, 1);
                        cell(at.X, at.Y + 1, 1, 5); cell(at.X + 6, at.Y + 1, 1, 5);
                        cell(at.X + 2, at.Y + 2, 3, 3);
                    }
                    cell(14, 14, 2, 2); cell(17, 14, 2, 2); cell(14, 17, 2, 2); cell(19, 17, 2, 2); cell(17, 19, 2, 2);
                }
                g.Restore(state);
                return;
            }
            g.TranslateTransform(x, y);
            g.ScaleTransform(s, s);
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (icon)
                {
                    case ToolIcon.Select:      // the select tool: an outline pointer, tip top left (2026-11-10: variant A)
                        g.DrawPolygon(pen, new[] { new PointF(6, 3.5f), new PointF(6, 18.5f), new PointF(9.8f, 15), new PointF(12.4f, 20.5f),
                                                   new PointF(14.8f, 19.4f), new PointF(12.2f, 14), new PointF(17.5f, 14) });
                        break;
                    case ToolIcon.Text:
                    case ToolIcon.ArtisticText:   // one big letter: one line, scaled as a whole
                        g.DrawLines(pen, new[] { new PointF(5, 20), new PointF(12, 4), new PointF(19, 20) });
                        g.DrawLine(pen, 7.6f, 14.5f, 16.4f, 14.5f);
                        break;
                    case ToolIcon.Line:
                        g.DrawLine(pen, 6.5f, 17.5f, 17.5f, 6.5f);
                        g.FillEllipse(brush, 3, 15, 5, 5);
                        g.FillEllipse(brush, 16, 4, 5, 5);
                        break;
                    case ToolIcon.Box:
                        using (var box = RoundedRect(4, 6, 16, 12, 2.5f)) g.DrawPath(pen, box);
                        break;
                    case ToolIcon.Picture:
                        using (var frame = RoundedRect(3.5f, 4.5f, 17, 15, 2.5f)) g.DrawPath(pen, frame);
                        g.DrawLines(pen, new[] { new PointF(4, 17.5f), new PointF(9.5f, 11.5f), new PointF(13, 15.5f), new PointF(15.5f, 13), new PointF(20, 17.5f) });
                        g.FillEllipse(brush, 14.3f, 7.3f, 3.4f, 3.4f);
                        break;
                    case ToolIcon.ParagraphText:  // a small letter with the lines of a paragraph flowing round it
                        g.DrawLines(pen, new[] { new PointF(3.5f, 12.5f), new PointF(7, 4), new PointF(10.5f, 12.5f) });
                        g.DrawLine(pen, 4.9f, 9.5f, 9.1f, 9.5f);
                        g.DrawLine(pen, 13.5f, 5.5f, 20.5f, 5.5f);
                        g.DrawLine(pen, 13.5f, 9.5f, 20.5f, 9.5f);
                        g.DrawLine(pen, 3.5f, 15.5f, 20.5f, 15.5f);
                        g.DrawLine(pen, 3.5f, 19.5f, 14.5f, 19.5f);
                        break;
                    case ToolIcon.Rectangle:      // square corners are the shape; radius 1 only keeps the stroke soft
                        using (var box = RoundedRect(3.5f, 6, 17, 12, 1f)) g.DrawPath(pen, box);
                        break;
                    case ToolIcon.Ellipse:        // wider than high: not a round button
                        g.DrawEllipse(pen, 3, 5.5f, 18, 13);
                        break;
                    case ToolIcon.Triangle:
                        g.DrawPolygon(pen, new[] { new PointF(12, 4), new PointF(20.5f, 19), new PointF(3.5f, 19) });
                        break;
                    case ToolIcon.Polygon:        // a hexagon, point up: clearly not the star without a name
                        g.DrawPolygon(pen, StarPoints(3, 12, 12, 8.8f, 8.8f));
                        break;
                    case ToolIcon.Star:           // five points, inner radius 45 %
                        g.DrawPolygon(pen, StarPoints(5, 12, 12.7f, 9.3f, 4.2f));
                        break;
                }
            }
            g.Restore(state);
        }

        // n points at the outer radius alternating with n at the inner one, the first at the top. Equal radii give a
        // regular polygon of 2n corners (the hexagon: n = 3).
        static PointF[] StarPoints(int n, float cx, float cy, float outer, float inner)
        {
            var p = new PointF[2 * n];
            for (int i = 0; i < 2 * n; i++)
            {
                double a = (-90 + i * 180.0 / n) * Math.PI / 180;
                float radius = i % 2 == 0 ? outer : inner;
                p[i] = new PointF(cx + (float)(radius * Math.Cos(a)), cy + (float)(radius * Math.Sin(a)));
            }
            return p;
        }

        // The small triangle in the bottom right corner of a palette button whose tool group has a flyout. Whole
        // pixels (a staircase, no anti-aliasing), legs 6 px at 100 % (at least 5), 3 px from the corner. scale: the
        // display scaling (1 at 96 dpi). color: Text at 75 % alpha normally, Accent on the open group or under the mouse.
        public static void DrawFlyoutMark(Graphics g, Rectangle button, Color color, float scale)
        {
            int leg = Math.Max(5, (int)Math.Round(6 * scale)), inset = (int)Math.Round(3 * scale);
            int right = button.Right - inset, bottom = button.Bottom - inset;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var brush = new SolidBrush(color))
                for (int i = 0; i < leg; i++)
                    g.FillRectangle(brush, right - (i + 1), bottom - leg + i, i + 1, 1);
            g.Restore(state);
        }

        // ---- layer icons ----

        // A context menu icon for the layer commands: two overlapping objects, the selected one filled, and an arrow up
        // or down (with a bar for "to front"/"to back"). Drawn on a 16 grid snapped to whole pixels, so it is crisp at
        // 16 px (100 %), 24 px (150 %) and every other size. One colour (the menu's text colour, GrayText when
        // disabled), so it suits light and dark menus alike.
        public static void DrawLayerIcon(Graphics g, LayerIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), k = size / 16f;
            int ox = (int)Math.Round(r.X + (r.Width - size) / 2f), oy = (int)Math.Round(r.Y + (r.Height - size) / 2f);
            int px = (int)Math.Round(size), w = Math.Max(1, (int)(k + 0.5f));
            bool up = icon == LayerIcon.BringToFront || icon == LayerIcon.BringForward;
            bool bar = icon == LayerIcon.BringToFront || icon == LayerIcon.SendToBack;
            Func<float, float, float, float, Rectangle> cells = (cx, cy, cw, ch) => Rectangle.FromLTRB(
                ox + (int)Math.Round(cx * k), oy + (int)Math.Round(cy * k), ox + (int)Math.Round((cx + cw) * k), oy + (int)Math.Round((cy + ch) * k));
            Rectangle other = cells(1, 3, 6, 6), selected = cells(4, 7, 6, 6);
            float radius = k;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            using (var outline = RoundedRect(other.X + w / 2f, other.Y + w / 2f, other.Width - w, other.Height - w, Math.Max(0.5f, radius - w / 2f)))
            using (var fill = RoundedRect(selected.X, selected.Y, selected.Width, selected.Height, radius))
            {
                // The object behind keeps a gap around the one in front.
                Rectangle front = up ? selected : other;
                int gap = Math.Max(1, w / 2);
                front.Inflate(gap, gap);
                var clip = g.Save();
                g.SetClip(front, CombineMode.Exclude);
                if (up) g.DrawPath(pen, outline); else g.FillPath(brush, fill);
                g.Restore(clip);
                if (up) g.FillPath(brush, fill); else g.DrawPath(pen, outline);

                // The arrow, drawn pointing up and mirrored for the "back" commands.
                float cx = ox + (int)Math.Round(12 * k) + w / 2f, d = 2 * k;
                int barTop = oy + (int)Math.Round(k), tipTop = bar ? barTop + 2 * w : barTop;
                float barY = barTop + w / 2f, tipY = tipTop + w / 2f, endY = oy + (int)Math.Round(14 * k) - w / 2f;
                Func<float, float> my = v => up ? v : 2 * oy + px - v;
                g.DrawLine(pen, cx, my(tipY), cx, my(endY));
                g.DrawLines(pen, new[] { new PointF(cx - d, my(tipY + d)), new PointF(cx, my(tipY)), new PointF(cx + d, my(tipY + d)) });
                if (bar) g.DrawLine(pen, cx - d, my(barY), cx + d, my(barY));
            }
            g.Restore(state);
        }

        // ---- arrange icons ----

        // The icons of the editor's "Arrange" toolbar, in the layer icons' system: 16 grid snapped to whole pixels, one
        // colour (ControlText, GrayText when disabled). Align and distribute are drawn for the left/horizontal case and
        // mirrored or transposed for the others, so each pair is exactly symmetric. Rotate shows a filled object inside
        // a circling arrow, so it does not read as undo/redo.
        public static void DrawArrangeIcon(Graphics g, ArrangeIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), k = size / 16f;
            int ox = (int)Math.Round(r.X + (r.Width - size) / 2f), oy = (int)Math.Round(r.Y + (r.Height - size) / 2f);
            int n = (int)Math.Round(size), w = Math.Max(1, (int)(k + 0.5f));
            Func<float, int> at = v => (int)Math.Round(v * k);
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                // A filled block in icon pixels, mirrored left/right and/or swapped to the vertical case.
                bool mirror = icon == ArrangeIcon.AlignRight || icon == ArrangeIcon.AlignBottom;
                bool turn = icon == ArrangeIcon.AlignTop || icon == ArrangeIcon.AlignMiddle || icon == ArrangeIcon.AlignBottom
                    || icon == ArrangeIcon.DistributeVertically;
                Action<int, int, int, int> block = (x0, y0, x1, y1) =>
                {
                    if (mirror) { int t = n - x1; x1 = n - x0; x0 = t; }
                    if (turn) { int t = x0; x0 = y0; y0 = t; t = x1; x1 = y1; y1 = t; }
                    g.FillRectangle(brush, ox + x0, oy + y0, x1 - x0, y1 - y0);
                };
                // Line centres on whole pixels: x(v) from the left, xr(v) the same distance from the right.
                Func<float, float> x = v => ox + at(v) + w / 2f, xr = v => ox + n - at(v) - w / 2f, y = v => oy + at(v) + w / 2f;
                switch (icon)
                {
                    case ArrangeIcon.Duplicate:
                    {
                        // Two equal pages, the front one with a plus; its size keeps the plus exactly centred.
                        int fw = at(9);
                        if ((fw - 3 * w) % 2 != 0) fw--;
                        var front = new Rectangle(ox + at(15) - fw, oy + at(15) - fw, fw, fw);
                        var back = new Rectangle(ox + at(1), oy + at(1), fw, fw);
                        float radius = Math.Max(0.5f, k - w / 2f);
                        var cut = front;
                        int gap = Math.Max(1, w / 2);
                        cut.Inflate(gap, gap);
                        var clip = g.Save();
                        g.SetClip(cut, CombineMode.Exclude);
                        using (var page = RoundedRect(back.X + w / 2f, back.Y + w / 2f, fw - w, fw - w, radius)) g.DrawPath(pen, page);
                        g.Restore(clip);
                        using (var page = RoundedRect(front.X + w / 2f, front.Y + w / 2f, fw - w, fw - w, radius)) g.DrawPath(pen, page);
                        int inner = fw - 2 * w, arm = (inner - w) / 2 - (w + 1) / 2;
                        int px = front.X + w + (inner - w) / 2, py = front.Y + w + (inner - w) / 2;
                        g.FillRectangle(brush, px, py - arm, w, w + 2 * arm);
                        g.FillRectangle(brush, px - arm, py, w + 2 * arm, w);
                        break;
                    }
                    case ArrangeIcon.Delete:
                        g.DrawLine(pen, x(1), y(3), xr(1), y(3));
                        g.DrawLines(pen, new[] { new PointF(x(6), y(3)), new PointF(x(6), y(1)), new PointF(xr(6), y(1)), new PointF(xr(6), y(3)) });
                        g.DrawLines(pen, new[] { new PointF(x(3), y(3)), new PointF(x(3), y(14)), new PointF(xr(3), y(14)), new PointF(xr(3), y(3)) });
                        g.DrawLine(pen, x(6), y(6), x(6), y(11));
                        g.DrawLine(pen, xr(6), y(6), xr(6), y(11));
                        break;
                    case ArrangeIcon.RotateLeft:
                    case ArrangeIcon.RotateRight:
                    {
                        // The object (a filled square) inside an arrow circling it three quarters, the head at the top
                        // pointing into the open quarter (right: clockwise).
                        int h = at(2);
                        using (var obj = RoundedRect(ox + n / 2 - h, oy + n / 2 - h, 2 * h, 2 * h, Math.Max(0.5f, k / 2f))) g.FillPath(brush, obj);
                        bool right = icon == ArrangeIcon.RotateRight;
                        float cx = ox + n / 2f, cy = oy + n / 2f, radius = 5.5f * k - w / 2f;
                        float start = right ? 0 : 180, sweep = right ? 270 : -270, end = (start + sweep) * (float)Math.PI / 180f;
                        g.DrawArc(pen, cx - radius, cy - radius, 2 * radius, 2 * radius, start, sweep);
                        float ex = cx + radius * (float)Math.Cos(end), ey = cy + radius * (float)Math.Sin(end);
                        float tx = -(float)Math.Sin(end) * (right ? 1 : -1), ty = (float)Math.Cos(end) * (right ? 1 : -1);
                        float nx = (float)Math.Cos(end), ny = (float)Math.Sin(end), tip = 2.5f * k, back = 1f * k, half = 2f * k;
                        g.FillPolygon(brush, new[] {
                            new PointF(ex + tx * tip, ey + ty * tip),
                            new PointF(ex - tx * back + nx * half, ey - ty * back + ny * half),
                            new PointF(ex - tx * back - nx * half, ey - ty * back - ny * half) });
                        break;
                    }
                    case ArrangeIcon.ResetRotation:
                    {
                        // "0°": back to no rotation (between the two rotate icons).
                        using (var f = UiFont.Create("Segoe UI", 11f * k, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.DrawString("0°", f, brush, new RectangleF(ox, oy + 0.5f * k, n + 1.5f * k, n), fmt);
                        }
                        break;
                    }
                    case ArrangeIcon.AlignLeft:
                    case ArrangeIcon.AlignRight:
                    case ArrangeIcon.AlignTop:
                    case ArrangeIcon.AlignBottom:
                    {
                        // The guide line, then a long and a short object against it.
                        int gx = at(2), bx = gx + w + Math.Max(1, at(1));
                        block(gx, at(1), gx + w, n - at(1));
                        block(bx, at(3), at(14), at(7));
                        block(bx, at(9), at(10), at(13));
                        break;
                    }
                    case ArrangeIcon.AlignCenter:
                    case ArrangeIcon.AlignMiddle:
                    {
                        int gl = (n - w) / 2, a = at(5), b = at(3);
                        block(gl, at(1), gl + w, n - at(1));
                        block(gl - a, at(3), gl + w + a, at(7));
                        block(gl - b, at(9), gl + w + b, at(13));
                        break;
                    }
                    case ArrangeIcon.DistributeHorizontally:
                    case ArrangeIcon.DistributeVertically:
                    {
                        // Three equal objects at equal spacing between two guide lines.
                        int bw = at(2);
                        if ((n - bw) % 2 != 0) bw++;
                        int cl = (n - bw) / 2, sp = at(4), gap = Math.Max(1, at(0.5f));
                        foreach (int left in new[] { cl - sp, cl, cl + sp }) block(left, at(4), left + bw, n - at(4));
                        int g0 = cl - sp - gap - w, g1 = cl + sp + bw + gap;
                        block(g0, at(1), g0 + w, n - at(1));
                        block(g1, at(1), g1 + w, n - at(1));
                        break;
                    }
                }
            }
            g.Restore(state);
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
        {
            float d = 2 * radius;
            var path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
