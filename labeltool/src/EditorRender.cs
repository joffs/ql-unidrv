// Label editor: the label's size and printable area (from the driver, EditorPrint.cs) and the rendering of a label
// into the 1-bit bitmap that is shown on screen and printed dot for dot.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace QlLabels
{
    // A label the editor can use: a label format (labels.json) or a Brother paper the printer offers.
    sealed class LabelChoice
    {
        public string Name;          // the Windows form name (what the driver is asked for)
        public Media Media;
        public double LengthMm;      // continuous: the format's length; die-cut / round: the paper's
        public double FeedMm;        // continuous: feed margin
        public bool IsFormat;        // a label format (else a Brother paper)
        public bool Custom;          // a length of its own on continuous tape: no form (Name is the tape's paper name)

        // A length of its own on this continuous tape (0.1 mm steps).
        public static LabelChoice Own(Media m, double lengthMm)
        {
            return new LabelChoice { Name = m.Name, Media = m, LengthMm = Math.Round(lengthMm, 1), FeedMm = Math.Round(m.TopMm, 1), Custom = true };
        }

        public override string ToString()
        {
            if (Custom) return Media.Display + "  –  " + Lang.T("own length");
            return IsFormat ? Name + "  –  " + LabelStore.Describe(new LabelFormat { Name = Name, PaperId = Media.Id, LengthMm = LengthMm, FeedMm = FeedMm })
                            : Name + "  –  " + Media.Display;
        }

        // Label formats first (in the list's order), then the Brother papers. model: only what it can print (null: all).
        public static List<LabelChoice> All(QlModel model)
        {
            var list = new List<LabelChoice>();
            try
            {
                if (File.Exists(LabelStore.JsonPath))
                    foreach (var f in LabelStore.Load(LabelStore.JsonPath).Formats)
                    {
                        var m = MediaTable.ById(f.PaperId);
                        if (m == null || (model != null && !model.HasPaper(m.Id))) continue;
                        list.Add(new LabelChoice { Name = f.Name, Media = m, LengthMm = m.Continuous ? f.LengthMm : m.LengthMm, FeedMm = f.FeedMm, IsFormat = true });
                    }
            }
            catch (Exception) { }
            foreach (var m in MediaTable.Ordered())
            {
                if (model != null && !model.HasPaper(m.Id)) continue;
                list.Add(new LabelChoice
                {
                    Name = m.Name, Media = m,
                    LengthMm = m.Continuous ? m.DefaultLength01 / 10.0 : m.LengthMm,
                    FeedMm = m.Continuous ? Math.Round(m.TopMm, 1) : 0,
                });
            }
            return list;
        }

        // The choice for a document: its form if still there, else one with the same paper and length.
        public static LabelChoice Find(List<LabelChoice> choices, string name, int paperId, int length01)
        {
            return choices.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? choices.FirstOrDefault(c => c.Media.Id == paperId && Math.Abs(c.LengthMm * 10 - length01) < 1)
                ?? (paperId != 0 ? choices.FirstOrDefault(c => c.Media.Id == paperId) : null);
        }
    }

    // The label as the editor shows it. Along (the default for long labels): the label's length runs left to right,
    // the end that leaves the printer first on the left; the editor turns its bitmap 90° clockwise to print, so the
    // view's top edge is the page's right edge. Across (short, wide labels such as 62 × 29 mm): the tape's width runs
    // left to right, the leading end at the top; that is the driver's portrait page as it is.
    sealed class LabelLayout
    {
        public LabelChoice Label;
        public bool Across;
        public double ViewWidthMm, ViewHeightMm;   // the whole label as shown
        public RectangleF Printable;               // printable area on the label as shown (mm)
        public int SkipX;                          // dots the label starts after the driver's printable area, across the tape
        public int DotsX, DotsY;                   // the printable area in printer dots, as shown
        public int DpiX, DpiY;                     // dots per inch, as shown (300 x 600: 600 along the label)
        public PageSettings Page;                  // a named paper on the printer
        public CustomPage Custom;                  // or a length of its own; both null: no printer, approximate
        public string Printer;

        public bool Approximate { get { return Page == null && Custom == null; } }

        public LabelLayout Copy() { return (LabelLayout)MemberwiseClone(); }

        // Short, wide labels (62 × 29 mm, round labels) are written across the tape; long ones along it.
        public static bool AcrossByDefault(LabelChoice label) { return label.LengthMm <= label.Media.WidthMm; }

        public static LabelLayout For(string printer, LabelChoice label, bool highRes, bool across)
        {
            if (printer != null)
            {
                try
                {
                    if (label.Custom)
                    {
                        var own = new CustomPage { Printer = printer, Width01 = label.Media.Width01, Length01 = (int)Math.Round(label.LengthMm * 10), DpiY = highRes ? 600 : 300 };
                        var l = FromGeometry(label, LabelPrinter.Measure(own), null, printer, across);
                        l.Custom = own;
                        return l;
                    }
                    var page = LabelPrinter.Settings(printer, label.Name, highRes ? 600 : 300, false);
                    return FromGeometry(label, LabelPrinter.Measure(page), page, printer, across);
                }
                catch (Exception) { }  // the form is not on this printer (not applied yet): approximate
            }
            return Estimate(label, highRes, across);
        }

        // From the driver's portrait page (x across the tape, y along the feed). Across the tape the printable area is
        // centred, as the driver's plug-in places it on the print head: for custom sizes Unidrv reports the whole
        // side margin on the left (GPD *MinLeftMargin) and none on the right, but the label prints centred.
        public static LabelLayout FromGeometry(LabelChoice label, LabelGeometry g, PageSettings page, string printer, bool across)
        {
            // A custom size can report more than the tape's band (narrower tapes: the whole paper width, the plug-in crops
            // the rest): the label is the band from the paper table, centred in what the driver reports.
            int skip = 0;
            if (label.Custom && label.Media.PrintDots > 0 && g.DpiX == 300 && g.Width > label.Media.PrintDots)
            {
                skip = (g.Width - label.Media.PrintDots) / 2;
                g = new LabelGeometry { DpiX = g.DpiX, DpiY = g.DpiY, Width = label.Media.PrintDots, Height = g.Height, OffsetX = g.OffsetX + skip,
                                        OffsetY = g.OffsetY, PageWidth = g.PageWidth, PageHeight = g.PageHeight, Landscape = g.Landscape };
            }
            double mmX = 25.4 / g.DpiX, mmY = 25.4 / g.DpiY;
            double side = (g.PageWidth - g.Width) / 2.0;
            var l = new LabelLayout { Label = label, Page = page, Printer = printer, Across = across, SkipX = skip };
            if (across)
            {
                l.ViewWidthMm = label.Media.WidthMm; l.ViewHeightMm = g.PageHeight * mmY;
                l.Printable = new RectangleF((float)(side * mmX), (float)(g.OffsetY * mmY), (float)(g.Width * mmX), (float)(g.Height * mmY));
                l.DotsX = g.Width; l.DotsY = g.Height; l.DpiX = g.DpiX; l.DpiY = g.DpiY;
            }
            else
            {
                l.ViewWidthMm = g.PageHeight * mmY; l.ViewHeightMm = label.Media.WidthMm;
                l.Printable = new RectangleF((float)(g.OffsetY * mmY), (float)(side * mmX),
                                             (float)(g.Height * mmY), (float)(g.Width * mmX));
                l.DotsX = g.Height; l.DotsY = g.Width; l.DpiX = g.DpiY; l.DpiY = g.DpiX;
            }
            return l;
        }

        // Without a printer: the paper table and the driver's usual margins (1.5 mm at the sides, the top offset /
        // feed at both ends).
        public static LabelLayout Estimate(LabelChoice label, bool highRes, bool across)
        {
            var m = label.Media;
            double length = label.LengthMm, width = m.WidthMm;
            double end = m.Continuous ? label.FeedMm : Math.Min(m.TopMm, length / 4);
            double side = Math.Min(1.5, width / 8);
            int dpiAlong = highRes ? 600 : 300;
            var g = new LabelGeometry
            {
                DpiX = 300, DpiY = dpiAlong,
                PageWidth = (int)Math.Round(width * 300 / 25.4), PageHeight = (int)Math.Round(length * dpiAlong / 25.4),
                OffsetX = (int)Math.Round(side * 300 / 25.4), OffsetY = (int)Math.Round(end * dpiAlong / 25.4),
            };
            g.Width = Math.Max(1, g.PageWidth - 2 * g.OffsetX);
            g.Height = Math.Max(1, g.PageHeight - 2 * g.OffsetY);
            return FromGeometry(label, g, null, null, across);
        }
    }

    static partial class EditorRender
    {
        // The label as printed: DotsX x DotsY, pure black and white (32 bpp), as the editor shows it. now: for the
        // date/time fields.
        public static Bitmap Render(LabelDoc doc, LabelLayout layout) { return Render(doc, layout, DateTime.Now); }

        public static Bitmap Render(LabelDoc doc, LabelLayout layout, DateTime now, ICollection<LabelObject> dragged = null)
        {
            var bmp = new Bitmap(layout.DotsX, layout.DotsY, PixelFormat.Format32bppArgb);
            bmp.SetResolution(layout.DpiX, layout.DpiY);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.ScaleTransform((float)(layout.DpiX / 25.4), (float)(layout.DpiY / 25.4));
                g.TranslateTransform(-layout.Printable.X, -layout.Printable.Y);
                foreach (var o in doc.Objects)
                    try { Draw(g, o, layout, now, dragged != null && dragged.Contains(o)); }
                    catch (Exception) { }   // a damaged object (a file from elsewhere): the rest of the label is still drawn
            }
            Binarize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
            return bmp;
        }

        // While dragging: a rendered label (Render with the same layout) drawn again only around part (mm: where the
        // dragged objects were and are), the same dots there as a full Render. Only the objects near it are drawn.
        // How far an object's dots may reach past its box (mm): an inverted text's black margin (InvertPad), a line's
        // width, a letter's overhang
        const float Reach = 2;

        // dragged: the objects being dragged (pictures among them are drawn as a draft, DrawImageDraft)
        public static void RenderPart(Bitmap bmp, LabelDoc doc, LabelLayout layout, RectangleF part, DateTime now, ICollection<LabelObject> dragged)
        {
            part = RectangleF.Inflate(part, Reach, Reach);
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            var dots = Rectangle.FromLTRB((int)Math.Floor((part.Left - layout.Printable.X) * kx), (int)Math.Floor((part.Top - layout.Printable.Y) * ky),
                                          (int)Math.Ceiling((part.Right - layout.Printable.X) * kx), (int)Math.Ceiling((part.Bottom - layout.Printable.Y) * ky));
            dots.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
            if (dots.Width <= 0 || dots.Height <= 0) return;
            using (var g = Graphics.FromImage(bmp))
            {
                g.SetClip(dots);   // device dots: the transforms below don't move it
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.ScaleTransform(kx, ky);
                g.TranslateTransform(-layout.Printable.X, -layout.Printable.Y);
                foreach (var o in doc.Objects)
                {
                    if (!RectangleF.Inflate(o.Bounds, Reach, Reach).IntersectsWith(part)) continue;
                    try { Draw(g, o, layout, now, dragged != null && dragged.Contains(o)); }
                    catch (Exception) { }
                }
            }
            Binarize(bmp, dots);
        }

        // An object whose box reaches beyond the label (after turning the view or a narrower format): the canvas grows by
        // it and shows that part grey. The strip's count goes by the printed dots (CutOff).
        public static bool Beyond(LabelObject o, LabelLayout layout)
        {
            const float t = 0.01f;
            var b = o.Bounds;
            return b.Left < -t || b.Top < -t || b.Right > layout.ViewWidthMm + t || b.Bottom > layout.ViewHeightMm + t;
        }

        // Where an object's printed dots are cut off (Cut): in the label's unprintable margin (the hatched band between
        // its edge and the printable area) and/or beyond the label.
        [Flags]
        public enum Cut { None = 0, Margin = 1, Beyond = 2 }

        // Which of the object's dots don't print. Checked on its own dots, drawn as Render draws them (glyphs, an inverted
        // text's black box, a filled box, a line's width, a dithered picture, a turned object), on Render's dot grid: a
        // dot outside the printable area is exactly a dot Render cuts off; its centre beyond the label's edge makes it
        // Beyond, else Margin. An empty part of a box gives nothing. (The canvas grows by the box: Beyond above.)
        public static Cut CutOff(LabelObject o, LabelLayout layout, DateTime now)
        {
            var p = layout.Printable;
            var near = RectangleF.Inflate(o.Bounds, Reach, Reach);
            if (p.Contains(near)) return Cut.None;
            // Render's dots, numbered from the printable area's corner: x0..x1, y0..y1 cover the area
            double kx = layout.DpiX / 25.4, ky = layout.DpiY / 25.4;
            int x0 = (int)Math.Floor((near.Left - p.X) * kx), y0 = (int)Math.Floor((near.Top - p.Y) * ky);
            int x1 = (int)Math.Ceiling((near.Right - p.X) * kx), y1 = (int)Math.Ceiling((near.Bottom - p.Y) * ky);
            int w = x1 - x0, h = y1 - y0;
            if (w <= 0 || h <= 0) return Cut.None;
            // a huge object (sizes go up to 10000 mm): by its box
            if ((double)w * h > 16e6) return Beyond(o, layout) ? Cut.Beyond : p.Contains(o.Bounds) ? Cut.None : Cut.Margin;
            // per column and row: 0 inside the printable area, 1 in the margin, 2 beyond the label (by the dot's centre)
            Func<int, int, double, double, double, int> where = (dot, dots, origin, k, size) =>
            {
                if (dot >= 0 && dot < dots) return 0;
                double mm = origin + (dot + 0.5) / k;
                return mm < 0 || mm > size ? 2 : 1;
            };
            var cols = new int[w];
            for (int x = 0; x < w; x++) cols[x] = where(x0 + x, layout.DotsX, p.X, kx, layout.ViewWidthMm);
            // the same layout with the printable area moved to the bitmap's corner, a whole number of dots (pictures and
            // codes are placed in dots from it, as in RenderOutsideOnScreen)
            var at = layout.Copy();
            at.Printable = new RectangleF((float)(p.X + x0 / kx), (float)(p.Y + y0 / ky), (float)(w / kx), (float)(h / ky));
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                bmp.SetResolution(layout.DpiX, layout.DpiY);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.ScaleTransform((float)kx, (float)ky);
                    g.TranslateTransform(-at.Printable.X, -at.Printable.Y);
                    try { Draw(g, o, at, now); }
                    catch (Exception) { return Cut.None; }
                }
                Binarize(bmp, new Rectangle(0, 0, w, h));
                var cut = Cut.None;
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var line = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        Marshal.Copy(data.Scan0 + y * data.Stride, line, 0, w);
                        int row = where(y0 + y, layout.DotsY, p.Y, ky, layout.ViewHeightMm);
                        for (int x = 0; x < w; x++)
                        {
                            if (line[x] == -1) continue;
                            int place = Math.Max(row, cols[x]);
                            if (place == 2) cut |= Cut.Beyond; else if (place == 1) cut |= Cut.Margin;
                            if (cut == (Cut.Margin | Cut.Beyond)) return cut;
                        }
                    }
                }
                finally { bmp.UnlockBits(data); }
                return cut;
            }
        }

        // For the editor's grey margin around the label: the objects that reach beyond it, drawn straight at the screen's
        // pixels and greyed (Greyed), only within at (screen pixels: what is in sight; a big photo may reach far beyond the
        // window); origin: where 0,0 mm is on the screen, k: pixels per mm. Pictures from a copy made for the screen
        // (DrawImageScreenDraft). Null when nothing is beyond.
        public static Bitmap RenderOutsideOnScreen(LabelDoc doc, LabelLayout layout, Rectangle at, PointF origin, float k)
        {
            var outside = doc.Objects.Where(o => Beyond(o, layout)).ToList();
            if (outside.Count == 0 || at.Width <= 0 || at.Height <= 0) return null;
            int dpi = Math.Max(1, (int)Math.Round(k * 25.4));
            var l = new LabelLayout
            {
                Label = layout.Label, Across = layout.Across, ViewWidthMm = layout.ViewWidthMm, ViewHeightMm = layout.ViewHeightMm,
                Page = layout.Page, Custom = layout.Custom, SkipX = layout.SkipX, Printer = layout.Printer,
                Printable = new RectangleF((at.X - origin.X) / k, (at.Y - origin.Y) / k, at.Width / k, at.Height / k),
                DpiX = dpi, DpiY = dpi, DotsX = at.Width, DotsY = at.Height,
            };
            var bmp = new Bitmap(at.Width, at.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.TranslateTransform(origin.X - at.X, origin.Y - at.Y);
                g.ScaleTransform(k, k);
                var now = DateTime.Now;
                foreach (var o in outside)
                    try
                    {
                        var img = o as ImageObject;
                        if (img != null) DrawImageScreenDraft(g, img, layout, k);
                        else Draw(g, o, l, now);
                    }
                    catch (Exception) { }
            }
            Greyed(bmp);
            return bmp;
        }

        // How the editor shows what lies beyond the label: dark dots grey (55 %), light ones transparent (a 32bppPArgb
        // bitmap, changed in place).
        public static void Greyed(Bitmap bmp)
        {
            var lut = new int[256];   // red -> premultiplied grey 140 at opacity 255 - red
            for (int v = 0; v < 256; v++) { int a = 255 - v, c = 140 * a / 255; lut[v] = (a << 24) | (c << 16) | (c << 8) | c; }
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                var row = new int[bmp.Width];
                for (int y = 0; y < bmp.Height; y++)
                {
                    IntPtr p = data.Scan0 + y * data.Stride;
                    Marshal.Copy(p, row, 0, row.Length);
                    for (int x = 0; x < row.Length; x++) row[x] = lut[(row[x] >> 16) & 255];
                    Marshal.Copy(row, 0, p, row.Length);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        // The rendered label as the driver's portrait page (feed downwards): turned 90° clockwise when shown along.
        public static Bitmap ForPrinter(Bitmap label, LabelLayout layout)
        {
            var p = (Bitmap)label.Clone();
            if (!layout.Across) p.RotateFlip(RotateFlipType.Rotate90FlipNone);
            return p;
        }

        static void Draw(Graphics g, LabelObject o, LabelLayout layout, DateTime now, bool draft = false)
        {
            var image = o as ImageObject;
            if (image != null) { if (draft) DrawImageDraft(g, image, layout); else DrawImage(g, image, layout); return; }
            var code = o as BarcodeObject;
            if (code != null) { DrawBarcode(g, code, layout, now); return; }
            var state = g.Save();
            try
            {
                // Rotate about the box centre; draw in the unrotated box.
                float cx = (float)(o.X + o.W / 2), cy = (float)(o.Y + o.H / 2);
                bool turned = o.Rotation == 90 || o.Rotation == 270;
                float w = (float)(turned ? o.H : o.W), h = (float)(turned ? o.W : o.H);
                g.TranslateTransform(cx, cy);
                if (o.Rotation != 0 || o.Tilt != 0) g.RotateTransform((float)(o.Rotation + o.Tilt));
                var box = new RectangleF(-w / 2, -h / 2, w, h);
                var text = o as TextObject;
                var line = o as LineObject;
                var shape = o as ShapeObject;
                if (text != null)
                {
                    // inverted: a black box over whatever lies below, the text white in it
                    if (text.Invert) g.FillRectangle(Brushes.Black, RectangleF.Inflate(box, InvertPad, InvertPad));
                    DrawText(g, text, box, now, text.Invert ? Brushes.White : Brushes.Black);
                }
                else if (line != null)
                {
                    using (var pen = new Pen(Color.Black, (float)line.Thickness))
                    {
                        // Start/End in the unrotated box (the line object's box is never rotated by Rotate90: it swaps W/H).
                        var s = line.Rising ? new PointF(box.Left, box.Bottom) : new PointF(box.Left, box.Top);
                        var e = line.Rising ? new PointF(box.Right, box.Top) : new PointF(box.Right, box.Bottom);
                        g.DrawLine(pen, s, e);
                    }
                }
                else if (shape != null) shape.Paint(g, box, Color.Black);
            }
            finally { g.Restore(state); }
        }
    }
}
