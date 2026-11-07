// To-scale preview of a label format: tape, label, feed margins (continuous), die-cut and round labels.
// The label's length runs left to right (the feed direction is drawn horizontally).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class LabelPreview : Control
    {
        LabelFormat format;

        // Feed margins and cut lines in the accent colour; the tape and labels white like paper in every scheme.
        static Color Accent { get { return Theme.Of(Role.Accent); } }

        public LabelPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.UserPaint, true);
        }

        public LabelFormat Format
        {
            get { return format; }
            set { format = value; Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Of(Role.Window));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var m = format == null ? null : MediaTable.ById(format.PaperId);
            if (m == null) return;

            double width = m.WidthMm;
            double length = m.Continuous ? format.LengthMm : m.LengthMm;
            if (length <= 0) return;
            double feed = m.Continuous ? format.FeedMm : 0;
            double gap = m.Continuous ? 0 : 3;     // space between die-cut labels on the backing
            double backing = m.Continuous ? 0 : 3; // backing paper wider than the labels

            float fontPx = Font.Height;
            float dimSpace = fontPx * 5f;  // width dimension on the right, outside the tape
            var area = new RectangleF(fontPx * 1.5f, fontPx * 1.2f, Width - fontPx * 2.5f - dimSpace, Height - fontPx * 4f);
            if (area.Width < 20 || area.Height < 20) return;
            // Label plus a bit of the tape before and after it.
            double extra = Math.Min(Math.Max(length * 0.15, 4), 25);
            double scale = Math.Min(area.Width / (length + 2 * (extra + gap)), area.Height / (width + backing));
            float lenPx = (float)(length * scale), widPx = (float)(width * scale);
            float x0 = area.X + (area.Width - lenPx) / 2, y0 = area.Y + (area.Height - widPx) / 2;
            var label = new RectangleF(x0, y0, lenPx, widPx);

            using (var tapeBrush = new SolidBrush(m.Continuous ? Theme.Fixed.Paper : Theme.Fixed.Backing))
            using (var edge = new Pen(Theme.Fixed.TapeEdge, 1f))
            {
                float bPx = (float)(backing * scale / 2);
                var tape = new RectangleF(area.X - fontPx, y0 - bPx, area.Width + 2 * fontPx, widPx + 2 * bPx);
                g.FillRectangle(tapeBrush, tape);

                if (m.Continuous)
                {
                    // Feed margins (not printable) and the cut lines.
                    float feedPx = (float)(feed * scale);
                    using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(90, Accent), Color.Transparent))
                    {
                        if (feedPx > 0.5f)
                        {
                            g.FillRectangle(hatch, x0, y0, feedPx, widPx);
                            g.FillRectangle(hatch, x0 + lenPx - feedPx, y0, feedPx, widPx);
                        }
                    }
                    g.DrawLine(edge, tape.Left, y0, tape.Right, y0);
                    g.DrawLine(edge, tape.Left, y0 + widPx, tape.Right, y0 + widPx);
                    using (var cut = new Pen(Accent, 1.5f) { DashStyle = DashStyle.Dash })
                    {
                        g.DrawLine(cut, x0, y0 - fontPx * 0.4f, x0, y0 + widPx + fontPx * 0.4f);
                        g.DrawLine(cut, x0 + lenPx, y0 - fontPx * 0.4f, x0 + lenPx, y0 + widPx + fontPx * 0.4f);
                    }
                }
                else
                {
                    float gapPx = (float)(gap * scale);
                    g.SetClip(tape);
                    foreach (float dx in new[] { -(lenPx + gapPx), 0f, lenPx + gapPx })
                    {
                        var r = new RectangleF(x0 + dx, y0, lenPx, widPx);
                        using (var path = Shape(r, m.Kind == MediaKind.Round, (float)(1.5 * scale)))
                        using (var fill = new SolidBrush(dx == 0 ? Theme.Fixed.Paper : Theme.Mix(Theme.Fixed.Paper, Theme.Fixed.Backing, 0.2)))
                        {
                            g.FillPath(fill, path);
                            g.DrawPath(edge, path);
                        }
                    }
                    g.ResetClip();
                }

                // The tape runs on beyond the preview: fade out both ends.
                var outer = RectangleF.Inflate(tape, 0, fontPx);
                using (var fade = new LinearGradientBrush(outer, Theme.Of(Role.Window), Color.Transparent, LinearGradientMode.Horizontal))
                {
                    Color ground = Theme.Of(Role.Window);
                    if (Theme.Dark) fade.InterpolationColors = SoftFade(ground);   // white into dark: a straight ramp looked harsh
                    else fade.InterpolationColors = new ColorBlend
                    {
                        Colors = new[] { ground, Color.FromArgb(0, ground), Color.FromArgb(0, ground), ground },
                        Positions = new[] { 0f, 0.12f, 0.88f, 1f },
                    };
                    g.FillRectangle(fade, outer);
                }
            }

            // Dimensions: length below, width to the right.
            using (var dim = new Pen(Theme.Of(Role.Text), 1f))
            using (var text = new SolidBrush(Theme.Of(Role.Text)))
            {
                float yd = y0 + widPx + (float)(backing * scale / 2) + fontPx * 0.8f;
                g.DrawLine(dim, x0, yd, x0 + lenPx, yd);
                g.DrawLine(dim, x0, yd - 4, x0, yd + 4);
                g.DrawLine(dim, x0 + lenPx, yd - 4, x0 + lenPx, yd + 4);
                string lt = Media.Mm(length) + " mm";
                if (m.Continuous && feed > 0) lt += "   " + Lang.F("(feed {0} mm)", Media.Mm(feed));
                var ls = g.MeasureString(lt, Font);
                g.DrawString(lt, Font, text, x0 + (lenPx - ls.Width) / 2, yd + 2);

                float xd = area.Right + fontPx * 1.6f;
                g.DrawLine(dim, xd, y0, xd, y0 + widPx);
                g.DrawLine(dim, xd - 4, y0, xd + 4, y0);
                g.DrawLine(dim, xd - 4, y0 + widPx, xd + 4, y0 + widPx);
                string wt = Media.Mm(width) + " mm";
                g.DrawString(wt, Font, text, xd + 4, y0 + (widPx - fontPx) / 2);
            }
        }

        // The ends' fade in dark: the ground covers the tape only near the ends and gives way quickly (a cubic curve instead
        // of a straight ramp), over a little more of the tape.
        static ColorBlend SoftFade(Color ground)
        {
            const int steps = 8;
            const float span = 0.16f;
            var colors = new List<Color>();
            var positions = new List<float>();
            for (int i = 0; i <= steps; i++)
            {
                float u = (float)i / steps;
                colors.Add(Color.FromArgb((int)(255 * (1 - u) * (1 - u) * (1 - u)), ground));
                positions.Add(span * u);
            }
            for (int i = steps; i >= 0; i--)
            {
                float u = (float)i / steps;
                colors.Add(Color.FromArgb((int)(255 * (1 - u) * (1 - u) * (1 - u)), ground));
                positions.Add(1 - span * u);
            }
            return new ColorBlend { Colors = colors.ToArray(), Positions = positions.ToArray() };
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
    }
}
