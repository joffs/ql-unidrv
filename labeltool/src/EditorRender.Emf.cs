// Copy out as EMF (QL Label Maker's Copy, next to our own format, PNG and DIB): the objects drawn by the same Draw as
// the print onto a GDI+ metafile (EMF+ with the plain EMF records too, for programs that read only those). Text stays
// text with its fonts, lines and shapes stay vectors. Pictures and barcodes are the bitmaps the print uses (300 dpi,
// each module a whole number of dots), rendered on their own and placed in mm: their drawing works in printer dots
// after ResetTransform, which a metafile plays back at the wrong scale.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace QlLabels
{
    static partial class EditorRender
    {
        const int EmfDpi = 300;
        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr hdc, int index);

        // The objects in their box (mm, as on the label) as EMF bytes; null when there's nothing to draw.
        public static byte[] RenderEmf(IList<LabelObject> objects, LabelLayout layout, RectangleF box, DateTime now)
        {
            if (objects.Count == 0 || box.Width <= 0 || box.Height <= 0) return null;
            var l = layout.Copy();
            l.Printable = box; l.DpiX = l.DpiY = EmfDpi;
            l.DotsX = (int)Math.Ceiling(box.Width / 25.4 * EmfDpi); l.DotsY = (int)Math.Ceiling(box.Height / 25.4 * EmfDpi);
            var stream = new MemoryStream();
            using (var reference = new Bitmap(1, 1))
            using (var rg = Graphics.FromImage(reference))
            {
                IntPtr hdc = rg.GetHdc();
                try
                {
                    // The EMF's device pixels per mm as its header says (the screen's size in mm); GDI+'s own millimetres
                    // would go by 96 dpi and draw everything smaller than the frame.
                    float kx = GetDeviceCaps(hdc, 8) / (float)Math.Max(1, GetDeviceCaps(hdc, 4));    // HORZRES / HORZSIZE
                    float ky = GetDeviceCaps(hdc, 10) / (float)Math.Max(1, GetDeviceCaps(hdc, 6));   // VERTRES / VERTSIZE
                    using (var mf = new Metafile(stream, hdc, new RectangleF(0, 0, box.Width, box.Height), MetafileFrameUnit.Millimeter, EmfType.EmfPlusDual))
                    using (var g = Graphics.FromImage(mf))
                    {
                        g.PageUnit = GraphicsUnit.Pixel;
                        g.ScaleTransform(kx, ky);
                        g.SmoothingMode = SmoothingMode.None;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.InterpolationMode = InterpolationMode.NearestNeighbor;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                        g.TranslateTransform(-box.X, -box.Y);
                        foreach (var o in objects)
                            try
                            {
                                if (o is ImageObject || o is BarcodeObject) DrawAsBitmap(g, o, l, now);
                                else Draw(g, o, l, now);
                            }
                            catch (Exception) { }   // as Render: the rest is still drawn
                    }
                }
                finally { rg.ReleaseHdc(hdc); }
            }
            return stream.ToArray();
        }

        // A picture or barcode as printed (Render at 300 dpi around its box), white transparent, placed in mm.
        static void DrawAsBitmap(Graphics g, LabelObject o, LabelLayout layout, DateTime now)
        {
            var near = RectangleF.Inflate(o.Bounds, Reach, Reach);
            var l = layout.Copy();
            int w = (int)Math.Ceiling(near.Width / 25.4 * EmfDpi), h = (int)Math.Ceiling(near.Height / 25.4 * EmfDpi);
            if (w < 1 || h < 1 || (double)w * h > 64e6) return;
            l.Printable = new RectangleF(near.X, near.Y, (float)(w * 25.4 / EmfDpi), (float)(h * 25.4 / EmfDpi));
            l.DotsX = w; l.DotsY = h;
            var one = new LabelDoc();
            one.Objects.Add(o);
            using (var bmp = Render(one, l, now))
            {
                bmp.MakeTransparent(Color.White);   // colours: fixed (the paper)
                g.DrawImage(bmp, l.Printable);
            }
        }
    }
}
