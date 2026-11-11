// Label editor, one object kind: picture: the picture object (PNG or SVG), its 1-bit conversion and cache, the settings rows and insert/replace.
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System;

namespace QlLabels
{
    enum ImageMode { Threshold, Dither }

    sealed class ImageObject : LabelObject
    {
        byte[] png;                     // the original picture (as inserted, at most MaxPixels per side)
        public byte[] Svg;              // an SVG picture as a vector (SvgPicture, cleaned): drawn at the size printed; Png is its preview
        public byte[] Emf;              // or an EMF picture as a vector (EmfPicture, checked; pasted from CorelDRAW, Word...): the same
        public ImageMode Mode = ImageMode.Dither;
        public int Threshold = 128;     // 1-254: darker than this prints black (Threshold mode)
        public bool Invert;
        public SizeF? OwnSizeMm;        // from a file: the size it was saved at (dropped files are placed 1:1); not saved

        public const int MaxPixels = 2000;
        Bitmap picture;                 // decoded Png (cache)
        bool unreadable;                // Png is no picture (a damaged file): drawn as nothing

        // A copy for another thread: it decodes the picture itself (a Bitmap must not be used by two threads at once).
        public ImageObject Detached()
        {
            var c = (ImageObject)Clone();
            c.picture = null;
            return c;
        }

        // A new picture (Replace picture) is decoded again; the old one stayed in the cache.
        public byte[] Png
        {
            get { return png; }
            set { if (value != png) { png = value; picture = null; unreadable = false; } }
        }

        public override string Type { get { return "image"; } }
        protected override string[] Keys { get { return new[] { "png", "svg", "emf", "mode", "threshold", "invert" }; } }

        public Bitmap Picture
        {
            get
            {
                if (picture == null && png != null && !unreadable)
                    try
                    {
                        using (var ms = new MemoryStream(png))
                        using (var decoded = new Bitmap(ms))
                            picture = new Bitmap(decoded);   // a copy: a Bitmap read from a stream needs the stream open
                    }
                    catch (ArgumentException) { unreadable = true; }
                return picture;
            }
        }

        // Reads a picture file (HEIC and a PDF's first page too: PictureFile); large pictures are scaled down (a label
        // needs at most ~1300 dots).
        public static ImageObject FromFile(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".emf", StringComparison.OrdinalIgnoreCase))
            {
                if (new FileInfo(path).Length > EmfPicture.MaxBytes) throw new InvalidDataException(Lang.T("The EMF picture is too large."));
                var emf = EmfPicture.Check(File.ReadAllBytes(path));
                var e = FromEmf(emf);
                e.OwnSizeMm = EmfPicture.FrameSizeMm(emf);
                return e;
            }
            byte[] svg = SvgPicture.IsSvg(path) ? SvgPicture.Read(path) : null;
            using (var src = svg != null ? SvgPreview(svg) : PictureFile.Load(path))
            {
                var img = FromPicture(src, svg, null);
                if (svg != null) img.OwnSizeMm = SvgPicture.SizeMm(File.ReadAllBytes(path));
                else
                {
                    // its resolution (96 dpi when it has none or an unlikely one)
                    double dx = src.HorizontalResolution, dy = src.VerticalResolution;
                    if (dx < 10 || dy < 10 || dx > 10000 || dy > 10000) dx = dy = 96;
                    img.OwnSizeMm = new SizeF((float)(src.Width * 25.4 / dx), (float)(src.Height * 25.4 / dy));
                }
                return img;
            }
        }

        // A pasted or dropped picture: an SVG (cleaned), an EMF (checked; CorelDRAW's misplaced text put back:
        // EmfText.Repair; with CorelDRAW's CMX its outline widths and small text as CorelDRAW draws them:
        // Import/Corel/CorelPicture.cs) or a bitmap.
        public static ImageObject FromSvg(byte[] svg) { using (var src = SvgPreview(svg)) return FromPicture(src, svg, null); }
        public static ImageObject FromEmf(byte[] emf, byte[] cmx = null)
        {
            emf = CorelPicture.Fix(emf, cmx);
            try { emf = EmfText.Repair(emf); }
            catch (InvalidDataException) { }   // drawn as it is
            using (var src = EmfPreview(emf)) return FromPicture(src, null, emf);
        }
        public static ImageObject FromBitmap(Bitmap src) { return FromPicture(src, null, null); }

        static ImageObject FromPicture(Bitmap src, byte[] svg, byte[] emf)
        {
            {
                double k = Math.Min(1.0, (double)MaxPixels / Math.Max(src.Width, src.Height));
                int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);   // colours: fixed - transparent parts print white
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return new ImageObject { Png = ms.ToArray(), Svg = svg, Emf = emf, W = w, H = h };
                    }
                }
            }
        }

        // An SVG's preview, MaxPixels on its longer side (the editor's draft and the picture without Direct2D).
        static Bitmap SvgPreview(byte[] svg)
        {
            var size = SvgPicture.Size(svg);
            double k = MaxPixels / Math.Max(size.Width, size.Height);
            var bmp = SvgPicture.Render(svg, Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
            if (bmp == null) throw new InvalidDataException(Lang.T("Windows can't draw this SVG picture."));
            return bmp;
        }

        // An EMF's preview, the same way (its frame gives the proportions).
        static Bitmap EmfPreview(byte[] emf)
        {
            var size = EmfPicture.SizeMm(emf);
            double k = MaxPixels / Math.Max(size.Width, size.Height);
            var bmp = EmfPicture.Render(emf, Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
            if (bmp == null) throw new InvalidDataException(Lang.T("Windows can't draw this EMF picture."));
            return bmp;
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["png"] = Png != null ? Convert.ToBase64String(Png) : "";
            if (Svg != null) d["svg"] = Encoding.UTF8.GetString(Svg);
            if (Emf != null) d["emf"] = Convert.ToBase64String(Emf);
            d["mode"] = Mode == ImageMode.Threshold ? "threshold" : "dither";
            d["threshold"] = Threshold; d["invert"] = Invert;
        }

        protected override void Read(Dictionary<string, object> d)
        {
            string png = Str(d, "png", "");
            try { Png = png.Length > 0 ? Convert.FromBase64String(png) : null; } catch (FormatException) { Png = null; }
            string svg = Str(d, "svg", "");
            try { Svg = svg.Length > 0 ? SvgPicture.Clean(Encoding.UTF8.GetBytes(svg)) : null; }   // cleaned again: the file may come from elsewhere
            catch (InvalidDataException) { Svg = null; }   // drawn from the png
            string emf = Str(d, "emf", "");
            try { Emf = emf.Length > 0 ? EmfPicture.Check(Convert.FromBase64String(emf)) : null; }   // checked again, as the svg
            catch (FormatException) { Emf = null; }
            catch (InvalidDataException) { Emf = null; }
            Mode = Str(d, "mode", "dither") == "threshold" ? ImageMode.Threshold : ImageMode.Dither;
            Threshold = (int)Math.Min(254, Math.Max(1, Num(d, "threshold", 128)));
            Invert = Bool(d, "invert", false);
        }
    }

    static partial class EditorRender
    {
        // colours: fixed begin - the label as it prints (white paper, black print), as in EditorRender.cs
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
        // go up to 10000 mm) a smaller one that is stretched when drawn; a bitmap of the whole box wouldn't fit in memory.
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
                // a giant box (the panel allows 10000 mm): at most 16384 pixels a side and 25 million in all, scaled up after
                double k = Math.Min(1.0, Math.Min(16384.0 / Math.Max(w, h), Math.Sqrt(25e6 / ((double)w * h))));
                var bmp = SvgPicture.Render(img.Svg, Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)));
                if (bmp != null) return bmp;
            }
            if (img.Emf != null)
            {
                double k = Math.Min(1.0, Math.Min(16384.0 / Math.Max(w, h), Math.Sqrt(25e6 / ((double)w * h))));
                var bmp = EmfPicture.Render(img.Emf, Math.Max(1, (int)(w * k)), Math.Max(1, (int)(h * k)), true);
                if (bmp != null) return bmp;   // else the preview (a record GDI+ can't draw)
            }
            return (Bitmap)img.Picture.Clone();
        }

        static bool Sideways(int rotation) { return rotation == 90 || rotation == 270; }

        // The picture turned by quarter turns, scaled to w x h dots and made black and white. What the picture leaves
        // transparent (an EMF's or SVG's empty parts) stays transparent: no white box over what lies below; drawn white
        // stays white.
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
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        using (var attr = new ImageAttributes())
                        {
                            attr.SetWrapMode(WrapMode.TileFlipXY);   // no grey seam at the edges
                            g.DrawImage(turned, new Rectangle(0, 0, w, h), 0, 0, turned.Width, turned.Height, GraphicsUnit.Pixel, attr);
                        }
                    }
                    var grey = Grey(scaled);   // transparent as white
                    var shown = Opaque(scaled);
                    var bits = img.Mode == ImageMode.Dither ? Dither(grey, w, h) : Threshold(grey, img.Threshold);
                    if (img.Invert) for (int i = 0; i < bits.Length; i++) bits[i] = !bits[i];
                    if (shown != null) for (int i = 0; i < bits.Length; i++) bits[i] &= shown[i];
                    return FromBits(bits, w, h, shown);
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
            bool[] opaque;
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
                    g.Clear(Color.Transparent);
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
                opaque = Opaque(pic);
            }
            var inside = new bool[grey.Length];
            for (int i = 0; i < grey.Length; i++) { inside[i] = shape[i] < 128 && (opaque == null || opaque[i]); if (!inside[i]) grey[i] = 255; }
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

        // Where the picture is drawn (at least half opaque); null: everywhere (a bitmap, or a picture without empty parts).
        static bool[] Opaque(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            var shown = new bool[w * h];
            bool all = true;
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                    for (int x = 0; x < w; x++)
                        if (!(shown[y * w + x] = ((row[x] >> 24) & 255) >= 128)) all = false;
                }
            }
            finally { bmp.UnlockBits(data); }
            return all ? null : shown;
        }

        // true = black
        public static bool[] Threshold(byte[] grey, int threshold)
        {
            var bits = new bool[grey.Length];
            for (int i = 0; i < grey.Length; i++) bits[i] = grey[i] < threshold;
            return bits;
        }

        // Floyd–Steinberg error diffusion. The ends snap: a dot 90 % black or darker is black, one under 10 % black
        // is white, and neither passes an error on (a near-black like CorelDRAW's RGB 19,19,18 gave black text white
        // speckles, a near-white paper tone stray dots); everything in between dithers.
        public const int SnapBlack = 25, SnapWhite = 230;   // grey 0..255: <= 25 = 90 % black or more, >= 230 = under 10 %

        public static bool[] Dither(byte[] grey, int w, int h)
        {
            var err = new float[grey.Length];
            for (int i = 0; i < grey.Length; i++) err[i] = grey[i];
            var bits = new bool[grey.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    if (grey[i] <= SnapBlack) { bits[i] = true; continue; }
                    if (grey[i] >= SnapWhite) continue;
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
        // colours: fixed end
    }

    sealed partial class EditorForm
    {
        // The settings rows of a picture (BuildProperties).
        void BuildPictureRows()
        {
            Func<LabelObject, bool> image = o => o is ImageObject;
            // An SVG with parts Windows doesn't draw (text, effects, ...): one plain line while it is selected, details behind (i)
            svgLabel = Theme.Fore(new Label { AutoSize = true, MaximumSize = new Size(S(230), 0), Margin = new Padding(0) }, Role.WarningText);
            var svgIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(svgIcon, () => { var img = canvas.Selected as ImageObject; return SvgTip(img != null ? SvgPicture.Missing(img.Svg) : new string[0]); });
            var svgRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(2), 0, S(6)) };
            svgRow.Controls.AddRange(new Control[] { svgLabel, svgIcon });
            SpanRow(svgRow, o => o is ImageObject && SvgPicture.Missing(((ImageObject)o).Svg).Length > 0);
            modeBox = Combo(Lang.T("Dither (photos, shades)"), Lang.T("Threshold (logos, drawings)"));
            modeBox.DropDownWidth = S(240);
            modeBox.SelectedIndexChanged +=(s, e) => Edit(s, o => ((ImageObject)o).Mode = modeBox.SelectedIndex == 0 ? ImageMode.Dither : ImageMode.Threshold);
            Row(Lang.T("Black and white:"), modeBox, image);
            thresholdBar = new TrackBar { Minimum = 1, Maximum = 254, TickFrequency = 16, Dock = DockStyle.Fill, AutoSize = false, Height = S(32) };
            thresholdBar.ValueChanged += (s, e) => Edit(s, o => ((ImageObject)o).Threshold = thresholdBar.Value);
            Row(Lang.T("Threshold:"), thresholdBar, o => o is ImageObject && ((ImageObject)o).Mode == ImageMode.Threshold);
            invertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((ImageObject)o).Invert = invertCheck.Checked));
            tips.SetToolTip(invertCheck, Lang.T("White on black: black and white swapped in the picture."));
            Row("", invertCheck, image);
            var pictureButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            var replace = new System.Windows.Forms.Button { Text = Lang.T("Replace..."), AutoSize = true };
            replace.Click += delegate { ReplacePicture(); };
            var natural = new System.Windows.Forms.Button { Text = Lang.T("Original proportions"), AutoSize = true };
            natural.Click += delegate { NaturalProportions(); };
            var fit = new System.Windows.Forms.Button { Text = Lang.T("Fit to label"), AutoSize = true };
            tips.SetToolTip(fit, Lang.T("Makes the picture as large as the label's printable area, in proportion, and centres it."));
            fit.Click += delegate { FitToLabel(canvas.Selection.ToList()); };
            pictureButtons.Controls.AddRange(new Control[] { replace, natural, fit });
            Row("", pictureButtons, image);
        }

        string AskPicture()
        {
            using (var dlg = new OpenFileDialog { Filter = Lang.T("Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.hif;*.pdf;*.svg|" + Lang.T("All files") + " (*.*)|*.*" })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        static ImageObject LoadPicture(IWin32Window owner, string file)
        {
            try { return ImageObject.FromFile(file); }
            catch (Exception ex)
            {
                Failed(owner, Lang.F("Cannot read the picture {0}.", Path.GetFileName(file)), ex);
                return null;
            }
        }

        // Size for a picture of w x h pixels: as large as fits into 80 % of the printable area.
        SizeF PictureSize(double w, double h)
        {
            var p = layout != null ? layout.Printable : new RectangleF(0, 0, 40, 20);
            double k = Math.Min(p.Width * 0.8 / w, p.Height * 0.8 / h);
            return new SizeF((float)(w * k), (float)(h * k));
        }

        void AddPicture()
        {
            string file = AskPicture();
            if (file == null) return;
            var img = LoadPicture(this, file);
            if (img == null) return;
            var at = FreeSpot();
            var size = PictureSize(img.W, img.H);
            img.X = at.X; img.Y = at.Y; img.W = Math.Round(size.Width, 1); img.H = Math.Round(size.Height, 1);
            Add(img);
        }

        void ReplacePicture()
        {
            var old = canvas.Selected as ImageObject;
            if (old == null) return;
            string file = AskPicture();
            if (file == null) return;
            var img = LoadPicture(this, file);
            if (img == null) return;
            Edit(this, o => { var i = (ImageObject)o; i.Png = img.Png; i.Svg = img.Svg; i.Emf = img.Emf; });
            NaturalProportions();
        }

        void NaturalProportions()
        {
            var img = canvas.Selected as ImageObject;
            if (img == null || img.Picture == null) return;
            bool turned = img.Rotation == 90 || img.Rotation == 270;
            double ratio = turned ? (double)img.Picture.Height / img.Picture.Width : (double)img.Picture.Width / img.Picture.Height;
            Edit(this, o => o.H = Math.Round(o.W / ratio, 2));
        }
    }
}
