// Small pictures of a label for previews (QL Label Maker's "Print with data" cards, PrintDataGrid.cs): the printed
// dots (Render), scaled down to the card. Only the small picture is kept, so hundreds of cards stay light.
// Tried 2026-11-09 (small text hard to read): texts drawn straight at the card's pixels with anti-aliasing
// came out thinner and paler than the scaled print, not more readable; what helps is a larger card (the size control).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace QlLabels
{
    static partial class EditorRender
    {
        // size: the picture in pixels for the whole label as shown (labelWidthMm x labelHeightMm); printableMm: where
        // the layout's printable area lies in it (what Render covers).
        public static Bitmap RenderPreview(LabelDoc doc, LabelLayout layout, DateTime now, Size size, float labelWidthMm, float labelHeightMm, RectangleF printableMm)
        {
            if (size.Width < 1 || size.Height < 1) return null;
            float kx = size.Width / Math.Max(1f, labelWidthMm), ky = size.Height / Math.Max(1f, labelHeightMm);
            var dest = new RectangleF(printableMm.X * kx, printableMm.Y * ky, printableMm.Width * kx, printableMm.Height * ky);
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var dots = Render(doc, layout, now))
            using (var g = Graphics.FromImage(bmp))
            using (var ia = new ImageAttributes())
            {
                g.Clear(Color.White);   // colours: fixed - the label as it prints (white paper)
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                ia.SetWrapMode(WrapMode.TileFlipXY);   // no grey seam at the edges
                g.DrawImage(dots, Rectangle.Round(dest), 0, 0, dots.Width, dots.Height, GraphicsUnit.Pixel, ia);
            }
            return bmp;
        }
    }
}
