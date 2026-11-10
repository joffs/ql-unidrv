// The petrol band of the page-style dialogs (moved unchanged from Brand.cs, refactor S6).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QlLabels
{
    // The petrol band at the top of page-style dialogs (setup, printer setup, About): title, optional subtitle, and in
    // the bottom right corner the band peels off the window like the mark's corner. Its height follows the font, so
    // it scales with the display.
    sealed class HeaderBand : Control
    {
        string subtitle = "";

        public HeaderBand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            AccessibleRole = AccessibleRole.StaticText;   // screen readers read the title
            UpdateHeight();
        }

        public string Subtitle
        {
            get { return subtitle; }
            set { subtitle = value ?? ""; AccessibleDescription = subtitle; UpdateHeight(); Invalidate(); }
        }

        Font TitleFont()
        {
            return UiFont.Create("Segoe UI Semibold", Font.SizeInPoints * 1.6f, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; UpdateHeight(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateHeight(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); UpdateHeight(); }

        // On a DPI change WinForms scales the font (OnFontChanged sets the new height) and then the bounds once more: the
        // band came out 1.5 times too tall after 96 -> 144 dpi (Printer setup over 690 px, ui_sweep 2026-11-05). The
        // height is the font's alone.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified & ~BoundsSpecified.Height);
            UpdateHeight();
        }

        void UpdateHeight()
        {
            int em = Font.Height;
            using (var title = TitleFont())
                Height = em * 3 / 2 + title.Height + (subtitle.Length > 0 ? em + em / 3 : em / 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(r, Theme.Of(Role.BandTop), Theme.Of(Role.BandBottom), 10f))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { Theme.Of(Role.BandTop), Theme.Of(Role.BandMiddle), Theme.Of(Role.BandBottom) },
                    Positions = new[] { 0f, 0.6f, 1f }
                };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, r);
            }

            // The corner: 5/8 of the band's height along each edge, the liner below it.
            float leg = r.Height * 0.625f, x1 = r.Right, y1 = r.Bottom;
            using (var liner = new GraphicsPath())
            using (var brush = new SolidBrush(Theme.Of(Role.Liner)))
            {
                liner.AddPolygon(new[] { new PointF(x1 - leg, y1), new PointF(x1, y1 - leg), new PointF(x1, y1) });
                g.FillPath(brush, liner);
            }
            Brand.PaintFlap(g, x1, y1, leg, leg / 88f);

            // Both lines start where a normal Label's text starts on the window's left line: the gutter plus the text
            // inset of the normal font (TextRenderer pads text by a part of the font height; the large title's own inset
            // would put it further right). NoPadding, then that one inset added.
            int em = Font.Height, top = em * 3 / 4;
            int left = Brand.Gutter(em) + TextInset(Font);
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int textWidth = Math.Max(0, (int)(r.Width - leg - left));
            // The title's box reaches down to the subtitle: a box of the font's height cut off the descenders (the "g" of
            // "Programme", user's report 2026-11-01), as NoPadding sets the text lower than the font's height allows for.
            using (var title = TitleFont())
            {
                int sub = top + title.Height + em / 3;
                TextRenderer.DrawText(g, Text, title, new Rectangle(left, top, textWidth, sub - top), Theme.Of(Role.OnPrimary), flags);
                if (subtitle.Length > 0)
                    TextRenderer.DrawText(g, subtitle, Font, new Rectangle(left, sub, textWidth, em), Theme.Of(Role.BandSubtitle), flags);
            }
        }

        // How far right of its edge a Label draws its text in this font: GDI's glyph overhang padding, a sixth of the
        // font height (MeasureText doesn't report it)
        static int TextInset(Font font) { return font.Height / 6; }
    }
}
