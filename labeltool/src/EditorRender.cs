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

    static class EditorRender
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
            // a huge object (sizes go up to 3000 mm): by its box
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
                var shape = o as BoxObject;
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
                else if (shape != null)
                {
                    if (shape.Filled)
                    {
                        if (shape.Ellipse) g.FillEllipse(Brushes.Black, box); else g.FillRectangle(Brushes.Black, box);
                    }
                    else
                    {
                        float t = (float)Math.Min(shape.Thickness, Math.Min(w, h) / 2);
                        var inner = RectangleF.Inflate(box, -t / 2, -t / 2);   // the outline stays inside the box
                        using (var pen = new Pen(Color.Black, t))
                        {
                            if (shape.Ellipse) g.DrawEllipse(pen, inner); else g.DrawRectangle(pen, inner.X, inner.Y, inner.Width, inner.Height);
                        }
                    }
                }
            }
            finally { g.Restore(state); }
        }

        public static Font FontOf(TextObject t) { return FontOf(t, t.Size, t.Style); }

        static Font FontOf(TextObject t, double points, FontStyle style)
        {
            return new Font(t.Font, (float)(points * 25.4 / 72), style, GraphicsUnit.World);   // world unit = mm
        }

        // Each line is placed by the editor (alignment, line pitch), so the format only measures and draws.
        static StringFormat Format()
        {
            var f = (StringFormat)StringFormat.GenericTypographic.Clone();
            f.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
            return f;
        }

        const float TextPad = 0.3f;      // mm around the text inside its box
        public const float InvertPad = 1f;   // mm: an inverted text's black box reaches this far beyond its box
        const float WidthMargin = 1.02f; // text grid-fitted to the printer's dots can come out a little wider than measured

        // A text laid out in mm: its lines (the explicit breaks; a frame text also wraps at its box) with their widths,
        // the line pitch and the font's line height and ascent.
        sealed class TextLayout
        {
            public readonly List<string> Lines = new List<string>();
            public readonly List<float> Widths = new List<float>();
            public float Pitch, LineSize, Ascent;
            public float Width { get { return Widths.Count == 0 ? 0 : Widths.Max(); } }
            public float Height { get { return Lines.Count == 0 ? 0 : (Lines.Count - 1) * Pitch + LineSize; } }
        }

        static float SpacingMm(TextObject t) { return (float)(t.Spacing * 25.4 / 72); }

        // Measured on a 300 dpi surface, the same for the screen and both print resolutions. wrap: the width a line
        // may take (mm), 0 = no wrapping.
        static TextLayout Layout(TextObject t, string text, float wrap)
        {
            var lay = new TextLayout();
            using (var bmp = new Bitmap(1, 1))
            {
                bmp.SetResolution(300, 300);
                using (var g = Graphics.FromImage(bmp))
                using (var font = FontOf(t))
                using (var fmt = Format())
                {
                    g.PageUnit = GraphicsUnit.Millimeter;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    var family = font.FontFamily;
                    float em = family.GetEmHeight(font.Style);
                    lay.LineSize = font.Size * family.GetLineSpacing(font.Style) / em;
                    lay.Ascent = font.Size * family.GetCellAscent(font.Style) / em;
                    lay.Pitch = lay.LineSize * (float)(t.Kind == TextKind.Frame ? t.LineHeight : 1);
                    float spacing = SpacingMm(t);
                    Func<string, float> width = s => LineWidth(g, font, fmt, s, spacing);
                    foreach (var para in text.Replace("\r\n", "\n").Split('\n'))
                        foreach (var line in wrap > 0 ? Wrap(para, wrap, width) : new List<string> { para })
                        {
                            lay.Lines.Add(line);
                            lay.Widths.Add(width(line));
                        }
                }
            }
            return lay;
        }

        static float LineWidth(Graphics g, Font font, StringFormat fmt, string s, float spacing)
        {
            if (s.Length == 0) return 0;
            float w = g.MeasureString(s, font, PointF.Empty, fmt).Width;
            return Math.Max(0, w + spacing * (Elements(s).Count - 1));
        }

        // The characters as the user sees them (a letter with its accents, a surrogate pair: one element).
        static List<string> Elements(string s)
        {
            var list = new List<string>();
            var e = StringInfo.GetTextElementEnumerator(s);
            while (e.MoveNext()) list.Add((string)e.Current);
            return list;
        }

        // Breaks a paragraph into lines no wider than max: at spaces, a word wider than a line at characters.
        static List<string> Wrap(string para, float max, Func<string, float> width)
        {
            var lines = new List<string>();
            string line = "";
            foreach (var token in Regex.Split(para, "(?<= )"))
            {
                if (token.Length == 0) continue;
                if (line.Length > 0 && width((line + token).TrimEnd(' ')) > max)
                {
                    lines.Add(line.TrimEnd(' '));
                    line = "";
                }
                string word = token;
                while (line.Length == 0 && width(word.TrimEnd(' ')) > max)
                {
                    var chars = Elements(word.TrimEnd(' '));
                    if (chars.Count < 2) break;
                    int n = 1;
                    while (n < chars.Count && width(string.Concat(chars.Take(n + 1))) <= max) n++;
                    string head = string.Concat(chars.Take(n));
                    lines.Add(head);
                    word = word.Substring(head.Length);
                }
                line += word;
            }
            lines.Add(line.TrimEnd(' '));
            return lines;
        }

        static void DrawText(Graphics g, TextObject t, RectangleF box, DateTime now, Brush ink)
        {
            var inner = RectangleF.Inflate(box, -TextPad, -TextPad);
            bool frame = t.Kind == TextKind.Frame;
            var lay = Layout(t, Fields.Expand(t.Text, now), frame ? Math.Max(0.1f, inner.Width / WidthMargin) : 0);
            float spacing = SpacingMm(t);
            // A frame cuts off what doesn't fit (the canvas marks it); a single line sits in the middle of its box.
            if (frame) g.IntersectClip(box);
            float y = frame ? inner.Top : inner.Top + (inner.Height - lay.Height) / 2;
            using (var font = FontOf(t))
            using (var plain = FontOf(t, t.Size, t.Style & ~FontStyle.Underline))
            using (var fmt = Format())
                for (int i = 0; i < lay.Lines.Count; i++, y += lay.Pitch)
                {
                    string line = lay.Lines[i];
                    float w = lay.Widths[i];
                    float x = t.Align == TextAlign.Center ? inner.Left + (inner.Width - w) / 2 : t.Align == TextAlign.Right ? inner.Right - w : inner.Left;
                    if (spacing == 0)
                    {
                        g.DrawString(line, font, ink, x, y, fmt);
                        continue;
                    }
                    // Character by character: each at its place in the line plus the spacing before it.
                    var chars = Elements(line);
                    string before = "";
                    for (int c = 0; c < chars.Count; c++)
                    {
                        float at = before.Length == 0 ? 0 : g.MeasureString(before, plain, PointF.Empty, fmt).Width;
                        g.DrawString(chars[c], plain, ink, x + at + c * spacing, y, fmt);
                        before += chars[c];
                    }
                    if (t.Underline && w > 0)
                        g.FillRectangle(ink, x, y + lay.Ascent + font.Size * 0.1f, w, Math.Max(font.Size * 0.06f, 0.1f));
                }
        }

        // A frame text whose lines don't all fit its box: the rest is cut off (the canvas marks the box in red).
        public static bool Overflows(TextObject t)
        {
            if (t.Kind != TextKind.Frame) return false;
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            float w = (float)(turned ? t.H : t.W) - 2 * TextPad, h = (float)(turned ? t.W : t.H) - 2 * TextPad;
            var lay = Layout(t, Fields.Expand(t.Text, DateTime.Now), Math.Max(0.1f, w / WidthMargin));
            return lay.Height > h + 0.01f || lay.Width * WidthMargin > w + 0.01f;
        }

        // Sets the box around the text at its size, without wrapping (keeps the box's centre for rotated text, its
        // top left otherwise).
        public static void FitBox(TextObject t)
        {
            string text = Fields.Expand(t.Text, DateTime.Now);
            var lay = Layout(t, text.Length == 0 ? " " : text, 0);
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            double w = Math.Ceiling((lay.Width * WidthMargin + 2 * TextPad) * 10) / 10, h = Math.Ceiling((lay.Height + 2 * TextPad) * 10) / 10;
            double nw = turned ? h : w, nh = turned ? w : h;
            if (t.Rotation != 0 || t.Tilt != 0) { t.X += (t.W - nw) / 2; t.Y += (t.H - nh) / 2; }
            t.W = nw; t.H = nh;
        }

        // After a change: a single-line text's box follows its text; a text frame keeps the box it was given.
        public static void FitText(TextObject t)
        {
            if (t.Kind == TextKind.Line) FitBox(t);
        }

        // ---------- barcodes ----------
        // The box is the code itself; the quiet zone scanners need lies around it (upright, in modules): QR code 4 on
        // every side, Code 128 10 left and right, EAN-13 11 left and 7 right. It isn't printed: other objects may reach
        // into it (QuietZoneIntruders finds them, the editor warns).
        static void QuietModules(BarcodeObject b, out int left, out int top, out int right, out int bottom)
        {
            if (b.Kind == Symbology.Qr) { left = top = right = bottom = 4; return; }
            top = bottom = 0;
            left = b.Kind == Symbology.Ean13 ? 11 : 10;
            right = b.Kind == Symbology.Ean13 ? 7 : 10;
        }

        // Dots per module (across, along the bars) for the upright code in a w x h dot box, and its modules across.
        static bool Modules(BarcodeObject b, int w, int h, int dpiW, int dpiH, DateTime now, out int mx, out int my, out int n)
        {
            mx = my = n = 0;
            if (w < 1 || h < 1 || b.Problem(now) != null) return false;
            string content = b.Content(now);
            if (b.Kind == Symbology.Qr)
            {
                n = QrCode.Encode(content, b.Level).GetLength(0);
                QrModule(n, w, h, dpiW, dpiH, out mx, out my);
                return true;
            }
            bool[] guards;
            n = b.Kind == Symbology.Ean13 ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
            mx = Math.Max(1, w / n);
            return true;
        }

        // As large as the box allows in whole dots; square modules also at 300 x 600 dpi.
        static void QrModule(int total, int w, int h, int dpiW, int dpiH, out int mx, out int my)
        {
            double moduleMm = Math.Min(w * 25.4 / dpiW, h * 25.4 / dpiH) / total;
            mx = Math.Max(1, (int)(moduleMm * dpiW / 25.4 + 1e-6));
            my = Math.Max(1, (int)Math.Round(mx * (double)dpiH / dpiW));
            if (my * total > h) { my = Math.Max(1, h / total); mx = Math.Max(1, (int)Math.Round(my * (double)dpiW / dpiH)); }
        }

        // The code with its quiet zone, in the printable area's dots (the box when the content can't be encoded).
        public static Rectangle QuietDots(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            int mx, my, n, l, t, rt, bt;
            if (!Modules(b, turned ? r.Height : r.Width, turned ? r.Width : r.Height, turned ? layout.DpiY : layout.DpiX, turned ? layout.DpiX : layout.DpiY,
                         now, out mx, out my, out n)) return r;
            QuietModules(b, out l, out t, out rt, out bt);
            // upright left, top, right, bottom in dots, then turned with the code (clockwise)
            var q = new[] { l * mx, t * my, rt * mx, bt * my };
            int shift = b.Rotation / 90;
            var s = new int[4];
            for (int i = 0; i < 4; i++) s[(i + shift) % 4] = q[i];
            return Rectangle.FromLTRB(r.Left - s[0], r.Top - s[1], r.Right + s[2], r.Bottom + s[3]);
        }

        // The same in mm on the label.
        public static RectangleF QuietZone(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            var q = QuietDots(b, layout, now);
            float kx = (float)(25.4 / layout.DpiX), ky = (float)(25.4 / layout.DpiY);
            return new RectangleF(layout.Printable.X + q.X * kx, layout.Printable.Y + q.Y * ky, q.Width * kx, q.Height * ky);
        }

        // Codes with something printed in their quiet zone, and the topmost object there: each code's zone is checked on
        // the label drawn without that code.
        public static List<KeyValuePair<BarcodeObject, LabelObject>> QuietZoneIntruders(LabelDoc doc, LabelLayout layout, DateTime now)
        {
            var found = new List<KeyValuePair<BarcodeObject, LabelObject>>();
            foreach (var code in doc.Objects.OfType<BarcodeObject>().ToList())
            {
                var zone = QuietDots(code, layout, now);
                var box = DotRect(code, layout);
                if (zone == box) continue;
                var others = doc.Objects.Where(o => o != code).ToList();
                if (!InkIn(others, doc.Across, layout, zone, box, now)) continue;
                // which one: the topmost whose own dots are there
                var mm = QuietZone(code, layout, now);
                var who = others.Where(o => o.Bounds.IntersectsWith(mm)).Reverse()
                                .FirstOrDefault(o => InkIn(new[] { o }, doc.Across, layout, zone, box, now));
                found.Add(new KeyValuePair<BarcodeObject, LabelObject>(code, who));
            }
            return found;
        }

        // Black dots of these objects in the zone, outside the code's box. Only the zone is drawn, with the objects near it
        // (the whole label for each code made a drop with a big photo pause).
        static bool InkIn(IEnumerable<LabelObject> objects, bool across, LabelLayout layout, Rectangle zone, Rectangle box, DateTime now)
        {
            zone.Intersect(new Rectangle(0, 0, layout.DotsX, layout.DotsY));
            if (zone.Width <= 0 || zone.Height <= 0) return false;
            float kx = (float)(25.4 / layout.DpiX), ky = (float)(25.4 / layout.DpiY);
            var mm = new RectangleF(layout.Printable.X + zone.X * kx, layout.Printable.Y + zone.Y * ky, zone.Width * kx, zone.Height * ky);
            var near = objects.Where(o => RectangleF.Inflate(o.Bounds, Reach, Reach).IntersectsWith(mm)).ToList();
            if (near.Count == 0) return false;
            // the same dots as in the whole label: its layout moved by whole dots (DotsX/DotsY kept: Workable sizes as there)
            var l = layout.Copy();
            l.Printable = mm;
            using (var bmp = new Bitmap(zone.Width, zone.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.ScaleTransform(1 / kx, 1 / ky);
                    g.TranslateTransform(-mm.X, -mm.Y);
                    foreach (var o in near)
                        try { Draw(g, o, l, now); }
                        catch (Exception) { }
                }
                Binarize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[bmp.Width];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width);
                        for (int x = 0; x < bmp.Width; x++)
                            if ((row[x] & 0xFFFFFF) == 0 && !box.Contains(x + zone.X, y + zone.Y)) return true;
                    }
                }
                finally { bmp.UnlockBits(data); }
            }
            return false;
        }

        static void DrawBarcode(Graphics g, BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (b.W <= 0 || b.H <= 0) return;
            var r = QuietDots(b, layout, now);   // drawn with its quiet zone (transparent: it covers nothing)
            if (r.Width < 1 || r.Height < 1) return;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            // The symbol upright: its own width/height in dots and dots per inch (fewer for a giant box: Workable).
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            using (var bmp = BarcodeBitmap(b, w, h, dpiW, dpiH, now))
            {
                if (bmp == null) return;
                bmp.RotateFlip(b.Rotation == 90 ? RotateFlipType.Rotate90FlipNone : b.Rotation == 180 ? RotateFlipType.Rotate180FlipNone
                               : b.Rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone);
                var state = g.Save();
                try
                {
                    g.ResetTransform();
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(bmp, r, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
                }
                finally { g.Restore(state); }
            }
        }

        // The symbol upright in a w x h dot box (the code and its quiet zones), centred, each module a whole number of
        // dots; the code white and black, the quiet zones transparent (an EAN-13's first digit stands in its left one).
        // null when the content can't be encoded (BarcodeObject.Problem says why).
        public static Bitmap BarcodeBitmap(BarcodeObject b, int w, int h, int dpiW, int dpiH, DateTime now)
        {
            if (b.Problem(now) != null) return null;
            string content = b.Content(now);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpiW, dpiH);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                if (b.Kind == Symbology.Qr)
                {
                    if (b.Invert) g.Clear(Color.Black);   // the quiet zone too: the black box around the code
                    DrawQr(g, QrCode.Encode(content, b.Level), w, h, dpiW, dpiH, b.Invert);
                }
                else DrawLinear(g, b, content, w, h, dpiW, dpiH);
            }
            return bmp;
        }

        static void DrawQr(Graphics g, bool[,] m, int w, int h, int dpiW, int dpiH, bool invert)
        {
            int n = m.GetLength(0), total = n + 8;   // 4 modules quiet zone on each side
            int mx, my;
            QrModule(total, w, h, dpiW, dpiH, out mx, out my);
            int x0 = (w - mx * total) / 2 + 4 * mx, y0 = (h - my * total) / 2 + 4 * my;
            g.FillRectangle(invert ? Brushes.Black : Brushes.White, x0, y0, n * mx, n * my);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) g.FillRectangle(invert ? Brushes.White : Brushes.Black, x0 + x * mx, y0 + y * my, mx, my);
        }

        static void DrawLinear(Graphics g, BarcodeObject b, string content, int w, int h, int dpiW, int dpiH)
        {
            bool ean = b.Kind == Symbology.Ean13;
            bool[] guards = null;
            var bars = ean ? Ean13.Modules(content, out guards) : Code128.Modules(b.Encoded(content));
            int left = ean ? 11 : 10, right = ean ? 7 : 10, total = left + bars.Length + right;
            int mx = Math.Max(1, w / total);
            int x0 = (w - mx * total) / 2 + left * mx;
            g.FillRectangle(Brushes.White, x0, 0, bars.Length * mx, h);
            // Text under the bars: about 1/5 of the height, 1.6-3.5 mm.
            double hMm = h * 25.4 / dpiH;
            double textMm = b.ShowText ? Math.Max(1.6, Math.Min(3.5, hMm * 0.2)) : 0;
            int textDots = (int)Math.Round(textMm * dpiH / 25.4);
            int barH = Math.Max(1, h - textDots - (b.ShowText ? (int)(0.3 * dpiH / 25.4) : 0));
            int guardH = ean && b.ShowText ? Math.Min(h, barH + textDots / 2) : barH;
            for (int i = 0; i < bars.Length; i++)
                if (bars[i]) g.FillRectangle(Brushes.Black, x0 + i * mx, 0, mx, guards != null && guards[i] ? guardH : barH);
            if (!b.ShowText) return;
            double sizeMm = CaptionMm(b, content, mx, bars.Length, textMm, dpiW);
            if (sizeMm <= 0) return;   // too narrow even at the smallest size: the panel says so (CaptionFits)
            // Text in mm units with the symbol's dots per inch (also right at 300 x 600).
            g.ScaleTransform((float)(dpiW / 25.4), (float)(dpiH / 25.4));
            float k = (float)(25.4 / dpiW);
            using (var font = new Font("Segoe UI", (float)sizeMm, FontStyle.Regular, GraphicsUnit.World))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center })
            {
                // The baseline lies one ascent below the top: the digits (no descenders) end just above the bottom
                // (from the top of the text band they reached a little below the edge and lost their last dots).
                float ascent = font.Size * font.FontFamily.GetCellAscent(FontStyle.Regular) / font.FontFamily.GetEmHeight(FontStyle.Regular);
                float top = (float)(hMm - textMm * 0.06 - 1.5 * 25.4 / dpiH) - ascent;   // round digits reach a little below the baseline
                fmt.FormatFlags |= StringFormatFlags.NoClip;
                if (ean)
                {
                    string d = Ean13.Full(content);
                    var first = new RectangleF((x0 - left * mx) * k, top, left * mx * k, (float)textMm);
                    g.DrawString(d.Substring(0, 1), font, Brushes.Black, first, fmt);
                    g.DrawString(d.Substring(1, 6), font, Brushes.Black, new RectangleF((x0 + 3 * mx) * k, top, 42 * mx * k, (float)textMm), fmt);
                    g.DrawString(d.Substring(7, 6), font, Brushes.Black, new RectangleF((x0 + 50 * mx) * k, top, 42 * mx * k, (float)textMm), fmt);
                }
                else g.DrawString(b.Caption(content), font, Brushes.Black, new RectangleF(x0 * k, top, bars.Length * mx * k, (float)textMm), fmt);
            }
        }

        // The text under the bars: its size (mm) so that it fits the width it has, an EAN-13's six digits between the
        // guard bars, else the bars' width (wider text was wrapped out of its band and vanished); at most the band's
        // size, 0 when it doesn't fit even at MinCaptionMm.
        const double MinCaptionMm = 1.3;

        static double CaptionMm(BarcodeObject b, string content, int mx, int bars, double textMm, int dpiW)
        {
            bool ean = b.Kind == Symbology.Ean13;
            string text = ean ? "000000" : b.Caption(content);
            if (text.Length == 0) return textMm * 0.95;
            double perMm;   // the text's width per mm of font size
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 100f, FontStyle.Regular, GraphicsUnit.World))
                perMm = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width / 100.0;
            double room = (ean ? 42 : bars) * mx * 25.4 / dpiW;
            double size = Math.Min(textMm * 0.95, room / perMm);
            return size < MinCaptionMm ? 0 : size;
        }

        // false when a code's text under the bars doesn't fit its box at all (the panel's hint): as DrawBarcode sizes it
        public static bool CaptionFits(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (!b.ShowText || b.Kind == Symbology.Qr || b.W <= 0 || b.H <= 0 || b.Problem(now) != null) return true;
            var r = QuietDots(b, layout, now);
            if (r.Width < 1 || r.Height < 1) return true;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            string content = b.Content(now);
            bool ean = b.Kind == Symbology.Ean13;
            bool[] guards;
            int bars = ean ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
            int mx = Math.Max(1, w / ((ean ? 18 : 20) + bars));
            double textMm = Math.Max(1.6, Math.Min(3.5, h * 25.4 / dpiH * 0.2));
            return CaptionMm(b, content, mx, bars, textMm, dpiW) > 0;
        }

        // What is wrong with a code (null: nothing): BarcodeObject.Problem (it can't be encoded: it prints blank), or a box
        // so small that the code is cut off: a QR code smaller than one dot per module, a linear code narrower than its
        // bars at one dot each even with both quiet zones (below that it is drawn at one dot, centred over its quiet zones,
        // and cut at their edge; measured on the printer 2026-11-01). Printing asks first (EditorForm.ConfirmProblems).
        public static string Problem(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            string problem = b.Problem(now);
            if (problem != null || layout == null || b.W <= 0 || b.H <= 0) return problem;
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            int w = turned ? r.Height : r.Width, h = turned ? r.Width : r.Height;
            int dpiW = turned ? layout.DpiY : layout.DpiX, dpiH = turned ? layout.DpiX : layout.DpiY;
            string content = b.Content(now);
            if (b.Kind == Symbology.Qr)
            {
                int n = QrCode.Encode(content, b.Level).GetLength(0);
                double needMm = Math.Max(n * 25.4 / dpiW, n * 25.4 / dpiH);
                return w * 25.4 / dpiW < needMm - 1e-6 || h * 25.4 / dpiH < needMm - 1e-6 ? Lang.F("Too small: at least {0} mm.", Media.Mm(Math.Ceiling(needMm * 10) / 10)) : null;
            }
            int bars, l, t, rt, bt;
            LinearWidth(b, layout, now, out w, out bars, out dpiW);
            QuietModules(b, out l, out t, out rt, out bt);
            return w + l + rt < bars ? Lang.F("Too narrow: at least {0} mm.", Media.Mm(Math.Ceiling(bars * 25.4 / dpiW * 10) / 10)) : null;
        }

        // A linear code's box width (upright, in dots), its bars (modules) and the dots per inch across it.
        static void LinearWidth(BarcodeObject b, LabelLayout layout, DateTime now, out int w, out int bars, out int dpiW)
        {
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            w = turned ? r.Height : r.Width;
            dpiW = turned ? layout.DpiY : layout.DpiX;
            bool[] guards;
            string content = b.Content(now);
            bars = b.Kind == Symbology.Ean13 ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
        }

        // A code's narrowest bar (a QR code's module) as printed, mm, as DrawBarcode sizes it; 0 when it can't be encoded.
        public static double ModuleMm(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (b.W <= 0 || b.H <= 0 || b.Problem(now) != null) return 0;
            var r = QuietDots(b, layout, now);
            if (r.Width < 1 || r.Height < 1) return 0;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            string content = b.Content(now);
            int mx, my;
            if (b.Kind == Symbology.Qr) QrModule(QrCode.Encode(content, b.Level).GetLength(0) + 8, w, h, dpiW, dpiH, out mx, out my);
            else
            {
                bool ean = b.Kind == Symbology.Ean13;
                bool[] guards;
                int bars = ean ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
                mx = Math.Max(1, w / ((ean ? 18 : 20) + bars));
            }
            return mx * 25.4 / dpiW;
        }

        // The panel's warnings about a code that prints but may not scan (null: none): partly outside the printable
        // area, bars narrower than 3 dots (an EAN-13 below 80 % of its nominal 0.33 mm), the text under the bars too wide.
        public static string BarcodeHint(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (Problem(b, layout, now) != null) return null;
            var lines = new List<string>();
            if (b.Kind != Symbology.Qr)
            {
                int w, bars, dpiW;
                LinearWidth(b, layout, now, out w, out bars, out dpiW);
                if (w < bars) lines.Add(Lang.F("The code is wider than its frame – better at least {0} mm.", Media.Mm(Math.Ceiling(bars * 25.4 / dpiW * 10) / 10)));
            }
            const float t = 0.01f;
            var bounds = b.Bounds;
            var p = layout.Printable;
            if (bounds.IntersectsWith(p) && (bounds.Left < p.Left - t || bounds.Top < p.Top - t || bounds.Right > p.Right + t || bounds.Bottom > p.Bottom + t))
                lines.Add(Lang.T("The code lies partly outside the printable area – it gets cut off."));
            double module = ModuleMm(b, layout, now);
            double least = b.Kind == Symbology.Ean13 ? 0.33 * 0.8 : 3 * 25.4 / Math.Max(layout.DpiX, layout.DpiY) - 1e-6;
            if (module > 0 && module < least)
            {
                string mm = module.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + " mm";   // kept on one line
                lines.Add(b.Kind == Symbology.Qr ? Lang.F("Dots very small ({0}) – make the code larger.", mm)
                                                 : Lang.F("Bars very narrow ({0}) – make the code wider.", mm));
            }
            if (!CaptionFits(b, layout, now)) lines.Add(Lang.T("The text under the bars doesn't fit at this size – make the code wider."));
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }

        // ---------- images ----------
        sealed class Converted { public string Key; public Bitmap Bitmap; }
        static readonly ConditionalWeakTableCache cache = new ConditionalWeakTableCache();

        static void DrawImage(Graphics g, ImageObject img, LabelLayout layout)
        {
            if (img.Picture == null) return;
            var r = SizedDotRect(img.Box, layout);
            if (r.Width < 1 || r.Height < 1) return;
            // Turned freely: the picture turned first, then made black and white, in the box around it.
            bool tilted = img.Tilt != 0;
            if (tilted) r = SizedDotRect(img.Bounds, layout);
            if (r.Width < 1 || r.Height < 1) return;
            var s = Workable(r, layout);
            string key = tilted ? TiltedKey(img, s) : OneBitKey(img, s.Width, s.Height);
            var c = cache.Get(img.Png);
            Bitmap bmp;
            if (c != null && c.Key == key) bmp = c.Bitmap;
            else if (later != null && SynchronizationContext.Current != null)
            {
                ConvertLater(img, key, tilted, s);
                DrawImageDraft(g, img, layout);
                return;
            }
            else bmp = tilted ? OneBitTilted(img, s) : OneBit(img, s.Width, s.Height);
            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, r, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
            }
            finally { g.Restore(state); }
        }

        // The size in dots to convert a picture or barcode at: its box, or for a box far larger than the label (sizes
        // go up to 3000 mm) a smaller one that is stretched when drawn; a bitmap of the whole box wouldn't fit in memory.
        static Size Workable(Rectangle r, LabelLayout layout)
        {
            double most = Math.Max(4.0 * layout.DotsX * layout.DotsY, 4e6), area = (double)r.Width * r.Height;
            if (area <= most) return r.Size;
            double k = Math.Sqrt(most / area);
            return new Size(Math.Max(1, (int)(r.Width * k)), Math.Max(1, (int)(r.Height * k)));
        }

        // As DotRect, but the size from the area's size alone, not from its rounded edges: a picture moved keeps its size
        // in dots and its black-and-white version (OneBit's cache); converting it again made each drop pause.
        static Rectangle SizedDotRect(RectangleF mm, LabelLayout layout)
        {
            double ka = layout.DpiX / 25.4, kc = layout.DpiY / 25.4;
            int x0 = (int)Math.Round((mm.Left - layout.Printable.X) * ka), y0 = (int)Math.Round((mm.Top - layout.Printable.Y) * kc);
            return new Rectangle(x0, y0, (int)Math.Round(mm.Width * ka), (int)Math.Round(mm.Height * kc));
        }

        // An object's box (or any area, mm) in the printable area's dots.
        public static Rectangle DotRect(LabelObject o, LabelLayout layout) { return DotRect(o.Box, layout); }

        public static Rectangle DotRect(RectangleF mm, LabelLayout layout)
        {
            double ka = layout.DpiX / 25.4, kc = layout.DpiY / 25.4;
            int x0 = (int)Math.Round((mm.Left - layout.Printable.X) * ka), y0 = (int)Math.Round((mm.Top - layout.Printable.Y) * kc);
            int x1 = (int)Math.Round((mm.Right - layout.Printable.X) * ka), y1 = (int)Math.Round((mm.Bottom - layout.Printable.Y) * kc);
            return new Rectangle(x0, y0, x1 - x0, y1 - y0);
        }

        // The picture turned, scaled to w x h dots and converted to black and white (cached per object and settings).
        public static Bitmap OneBit(ImageObject img, int w, int h)
        {
            string key = OneBitKey(img, w, h);
            var c = cache.Get(img.Png);
            if (c != null && c.Key == key) return c.Bitmap;
            var result = Convert(img, img.Rotation, w, h);
            if (c != null && c.Bitmap != null) c.Bitmap.Dispose();
            cache.Set(img.Png, new Converted { Key = key, Bitmap = result });
            return result;
        }

        // While dragging a picture: its black-and-white version made once (upright, at the size it had then) and only
        // turned and stretched dot for dot; the exact one when the drag ends (turning and converting at 300 dpi took up
        // to 70 ms a move).
        static void DrawImageDraft(Graphics g, ImageObject img, LabelLayout layout)
        {
            if (img.Picture == null) return;
            bool turned = img.Rotation == 90 || img.Rotation == 270;
            float w = (float)(turned ? img.H : img.W), h = (float)(turned ? img.W : img.H);
            if (w <= 0 || h <= 0) return;
            DrawTurned(g, img, DraftOf(img, layout, w, h), w, h);
        }

        // The draft for an upright w x h mm at layout's dots
        static Bitmap DraftOf(ImageObject img, LabelLayout layout, float w, float h)
        {
            string key = img.Mode + "/" + img.Threshold + "/" + img.Invert;
            var c = drafts.Get(img.Png);
            var need = Workable(new Rectangle(0, 0, (int)Math.Ceiling(w * layout.DpiX / 25.4f), (int)Math.Ceiling(h * layout.DpiY / 25.4f)), layout);
            // made again for another look or when a quarter too small (a resize); a larger one stays (the grey part
            // beyond the label is drawn at the screen's resolution, the label at the printer's)
            if (c == null || c.Key != key || c.Bitmap.Width * 4 < need.Width * 3 || c.Bitmap.Height * 4 < need.Height * 3)
            {
                var s = need;
                if (c != null && c.Bitmap != null) c.Bitmap.Dispose();
                c = new Converted { Key = key, Bitmap = Convert(img, 0, Math.Max(1, s.Width), Math.Max(1, s.Height)) };
                drafts.Set(img.Png, c);
            }
            return c.Bitmap;
        }

        // A picture's upright bitmap drawn turned in its box (w x h mm), dot for dot
        static void DrawTurned(Graphics g, ImageObject img, Bitmap bmp, float w, float h)
        {
            var state = g.Save();
            try
            {
                g.TranslateTransform((float)(img.X + img.W / 2), (float)(img.Y + img.H / 2));
                g.RotateTransform((float)img.Angle);
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                g.DrawImage(bmp, new RectangleF(-w / 2, -h / 2, w, h));
            }
            finally { g.Restore(state); }
        }

        // The grey margin (RenderOutsideOnScreen): the draft (at the printer's dots, layout) scaled down
        // to the screen (k pixels per mm) once and smoothed, as the margin shows it after the drop; dot for dot its
        // dither showed as noise. Made again when the size on the screen changes by a quarter.
        static void DrawImageScreenDraft(Graphics g, ImageObject img, LabelLayout layout, float k)
        {
            if (img.Picture == null) return;
            bool turned = img.Rotation == 90 || img.Rotation == 270;
            float w = (float)(turned ? img.H : img.W), h = (float)(turned ? img.W : img.H);
            if (w <= 0 || h <= 0) return;
            var draft = DraftOf(img, layout, w, h);
            int sw = Math.Max(1, (int)Math.Ceiling(w * k)), sh = Math.Max(1, (int)Math.Ceiling(h * k));
            string key = img.Mode + "/" + img.Threshold + "/" + img.Invert + "/" + draft.Width + "x" + draft.Height;
            var c = screenDrafts.Get(img.Png);
            if (c == null || c.Key != key || c.Bitmap.Width * 4 < sw * 3 || c.Bitmap.Width * 3 > sw * 4 || c.Bitmap.Height * 4 < sh * 3 || c.Bitmap.Height * 3 > sh * 4)
            {
                if (c != null && c.Bitmap != null) c.Bitmap.Dispose();
                var bmp = new Bitmap(sw, sh, PixelFormat.Format32bppPArgb);
                using (var s = Graphics.FromImage(bmp))
                {
                    s.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    s.PixelOffsetMode = PixelOffsetMode.Half;
                    s.DrawImage(draft, new Rectangle(0, 0, sw, sh));
                }
                c = new Converted { Key = key, Bitmap = bmp };
                screenDrafts.Set(img.Png, c);
            }
            DrawTurned(g, img, c.Bitmap, w, h);
        }

        static readonly ConditionalWeakTableCache drafts = new ConditionalWeakTableCache(), screenDrafts = new ConditionalWeakTableCache();

        // The picture before its quarter turns, for drawing at w x h pixels: an SVG drawn at that size (sharp at any size),
        // else a copy of the picture.
        static Bitmap Unturned(ImageObject img, int w, int h)
        {
            if (img.Svg != null)
            {
                // a giant box (the panel allows 3000 mm): at most 16384 pixels a side and 25 million in all, scaled up after
                double k = Math.Min(1.0, Math.Min(16384.0 / Math.Max(w, h), Math.Sqrt(25e6 / ((double)w * h))));
                var bmp = SvgPicture.Render(img.Svg, Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)));
                if (bmp != null) return bmp;
            }
            return (Bitmap)img.Picture.Clone();
        }

        static bool Sideways(int rotation) { return rotation == 90 || rotation == 270; }

        // The picture turned by quarter turns, scaled to w x h dots and made black and white.
        static Bitmap Convert(ImageObject img, int rotation, int w, int h)
        {
            using (var turned = Sideways(rotation) ? Unturned(img, h, w) : Unturned(img, w, h))
            {
                turned.RotateFlip(rotation == 90 ? RotateFlipType.Rotate90FlipNone : rotation == 180 ? RotateFlipType.Rotate180FlipNone
                                  : rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone);
                using (var scaled = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(scaled))
                    {
                        g.Clear(Color.White);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var attr = new ImageAttributes())
                        {
                            attr.SetWrapMode(WrapMode.TileFlipXY);   // no grey seam at the edges
                            g.DrawImage(turned, new Rectangle(0, 0, w, h), 0, 0, turned.Width, turned.Height, GraphicsUnit.Pixel, attr);
                        }
                    }
                    var grey = Grey(scaled);
                    var bits = img.Mode == ImageMode.Dither ? Dither(grey, w, h) : Threshold(grey, img.Threshold);
                    if (img.Invert) for (int i = 0; i < bits.Length; i++) bits[i] = !bits[i];
                    return FromBits(bits, w, h);
                }
            }
        }

        static string OneBitKey(ImageObject img, int w, int h)
        {
            return w + "x" + h + "/" + img.Rotation + "/" + img.Mode + "/" + img.Threshold + "/" + img.Invert;
        }

        static string TiltedKey(ImageObject img, Size s)
        {
            return s.Width + "x" + s.Height + "/" + img.Rotation + "/" + img.Tilt + "/" + img.W + "x" + img.H + "/" + img.Mode + "/" + img.Threshold + "/" + img.Invert;
        }

        // A freely turned picture in s dots covering the box around it (img.Bounds): turned and scaled in mm (right also
        // at 300 x 600 dpi), then converted to black and white; outside the turned picture transparent.
        static Bitmap OneBitTilted(ImageObject img, Size s)
        {
            string key = TiltedKey(img, s);
            var c = cache.Get(img.Png);
            if (c != null && c.Key == key) return c.Bitmap;
            var result = MakeTilted(img, s);
            if (c != null && c.Bitmap != null) c.Bitmap.Dispose();
            cache.Set(img.Png, new Converted { Key = key, Bitmap = result });
            return result;
        }

        static Bitmap MakeTilted(ImageObject img, Size s)
        {
            var bounds = img.Bounds;
            int w = s.Width, h = s.Height;
            var box = new RectangleF((float)(-img.W / 2), (float)(-img.H / 2), (float)img.W, (float)img.H);
            Action<Graphics> place = gg =>
            {
                gg.ScaleTransform(w / bounds.Width, h / bounds.Height);
                gg.TranslateTransform((float)(img.X + img.W / 2) - bounds.X, (float)(img.Y + img.H / 2) - bounds.Y);
                gg.RotateTransform((float)img.Tilt);
            };
            byte[] grey, shape;
            double dots = Math.Max(w / bounds.Width, h / bounds.Height);   // per mm, the finer direction
            int pw = Math.Max(1, (int)Math.Ceiling(img.W * dots)), ph = Math.Max(1, (int)Math.Ceiling(img.H * dots));
            using (var turned = Sideways(img.Rotation) ? Unturned(img, ph, pw) : Unturned(img, pw, ph))
            using (var pic = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var mask = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            {
                turned.RotateFlip(img.Rotation == 90 ? RotateFlipType.Rotate90FlipNone : img.Rotation == 180 ? RotateFlipType.Rotate180FlipNone
                                  : img.Rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone);
                using (var g = Graphics.FromImage(pic))
                using (var attr = new ImageAttributes())
                {
                    g.Clear(Color.White);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    place(g);
                    attr.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(turned, new[] { box.Location, new PointF(box.Right, box.Top), new PointF(box.Left, box.Bottom) },
                                new RectangleF(0, 0, turned.Width, turned.Height), GraphicsUnit.Pixel, attr);
                }
                using (var g = Graphics.FromImage(mask))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.None;
                    place(g);
                    g.FillRectangle(Brushes.Black, box);
                }
                grey = Grey(pic);
                shape = Grey(mask);
            }
            var inside = new bool[grey.Length];
            for (int i = 0; i < grey.Length; i++) { inside[i] = shape[i] < 128; if (!inside[i]) grey[i] = 255; }
            var bits = img.Mode == ImageMode.Dither ? Dither(grey, w, h) : Threshold(grey, img.Threshold);
            for (int i = 0; i < bits.Length; i++) bits[i] = inside[i] && (img.Invert ? !bits[i] : bits[i]);
            return FromBits(bits, w, h, inside);
        }

        // While the editor renders for the screen (Later): a picture not yet converted at its size and angle is drawn
        // from its draft and converted on a worker thread, from its own decoded copy; done runs on this thread when it is
        // ready (the editor renders again). Converting a large photo at 300 dpi held each drop up to 200 ms. Printing
        // renders without it: always exact.
        [ThreadStatic] static Action later;
        static readonly HashSet<string> pending = new HashSet<string>();

        public static IDisposable Later(Action done)
        {
            var before = later;
            later = done;
            return new Restore(() => later = before);
        }

        sealed class Restore : IDisposable
        {
            Action undo;
            public Restore(Action undo) { this.undo = undo; }
            public void Dispose() { if (undo != null) { undo(); undo = null; } }
        }

        static void ConvertLater(ImageObject img, string key, bool tilted, Size s)
        {
            var png = img.Png;
            string id = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(png) + "/" + key;
            if (!pending.Add(id)) return;
            var copy = img.Detached();
            var done = later;
            var ui = SynchronizationContext.Current;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Bitmap result = null;
                try
                {
                    result = tilted ? MakeTilted(copy, s) : Convert(copy, copy.Rotation, s.Width, s.Height);
                    if (copy.Picture != null) copy.Picture.Dispose();
                }
                catch (Exception) { }
                ui.Post(__ =>
                {
                    pending.Remove(id);
                    if (result == null) return;
                    var c = cache.Get(png);
                    if (c != null && c.Key == key) { result.Dispose(); done(); return; }   // converted meanwhile (a print)
                    if (c != null && c.Bitmap != null) c.Bitmap.Dispose();
                    cache.Set(png, new Converted { Key = key, Bitmap = result });
                    done();
                }, null);
            });
        }

        // Luminance 0 (black) .. 255 (white).
        static byte[] Grey(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var grey = new byte[w * h];
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        int p = row[x], a = (p >> 24) & 255, r = (p >> 16) & 255, gg = (p >> 8) & 255, b = p & 255;
                        int l = (r * 299 + gg * 587 + b * 114) / 1000;
                        grey[y * w + x] = (byte)(255 - (255 - l) * a / 255);   // transparent = white
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            return grey;
        }

        // true = black
        public static bool[] Threshold(byte[] grey, int threshold)
        {
            var bits = new bool[grey.Length];
            for (int i = 0; i < grey.Length; i++) bits[i] = grey[i] < threshold;
            return bits;
        }

        // Floyd–Steinberg error diffusion.
        public static bool[] Dither(byte[] grey, int w, int h)
        {
            var err = new float[grey.Length];
            for (int i = 0; i < grey.Length; i++) err[i] = grey[i];
            var bits = new bool[grey.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    float v = err[i];
                    bool black = v < 128;
                    bits[i] = black;
                    float e = v - (black ? 0 : 255);
                    if (x + 1 < w) err[i + 1] += e * 7 / 16;
                    if (y + 1 < h)
                    {
                        if (x > 0) err[i + w - 1] += e * 3 / 16;
                        err[i + w] += e * 5 / 16;
                        if (x + 1 < w) err[i + w + 1] += e * 1 / 16;
                    }
                }
            return bits;
        }

        static Bitmap FromBits(bool[] bits, int w, int h) { return FromBits(bits, w, h, null); }

        // shown: null = all dots; else only these (the rest transparent).
        static Bitmap FromBits(bool[] bits, int w, int h, bool[] shown)
        {
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                        row[x] = shown != null && !shown[y * w + x] ? 0 : bits[y * w + x] ? unchecked((int)0xFF000000) : -1;
                    Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, w);
                }
            }
            finally { bmp.UnlockBits(data); }
            return bmp;
        }

        // Every dot black or white (GDI+ may leave grey at shape edges in rare cases): darker than mid-grey = black.
        static void Binarize(Bitmap bmp, Rectangle part)
        {
            int w = part.Width, h = part.Height;
            var data = bmp.LockBits(part, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    IntPtr p = data.Scan0 + y * data.Stride;
                    Marshal.Copy(p, row, 0, w);
                    for (int x = 0; x < w; x++)
                    {
                        int v = row[x], l = (((v >> 16) & 255) * 299 + ((v >> 8) & 255) * 587 + (v & 255) * 114) / 1000;
                        row[x] = l < 128 ? unchecked((int)0xFF000000) : -1;
                    }
                    Marshal.Copy(row, 0, p, w);
                }
            }
            finally { bmp.UnlockBits(data); }
        }

        // Converted pictures, keyed by the picture's bytes (shared by an object's undo copies).
        sealed class ConditionalWeakTableCache
        {
            readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Converted> table =
                new System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Converted>();
            public Converted Get(byte[] key) { Converted c; return key != null && table.TryGetValue(key, out c) ? c : null; }
            public void Set(byte[] key, Converted value) { table.Remove(key); table.Add(key, value); }
        }
    }
}
