// QL Label Maker's clipboard and drops, the reading side: what a paste or a drop offers, in this order
// (first match wins):
//   1. our own objects (QLUnidrv.LabelObjects, JSON)
//   2. SVG with real vectors, under any of its names, or SVG source in the plain text (never a text object)
//   3. EMF (CF_ENHMETAFILE), unless it only wraps one bitmap (the PNG/DIB below is the same picture)
//   4. PNG (alpha), CF_DIBV5 (alpha), CF_DIB
//   5. files (CF_HDROP)
//   6. RTF (plain text from it until paragraph runs exist)
//   7. plain text (CF_UNICODETEXT)
// Never used: HTML Format, URLs (no web downloads), PDF, OLE and program-native formats. Pictures are checked as on
// Insert > Picture (SvgPicture.Clean, EmfPicture.Check, a pixel limit). The editor places what this finds.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Xml;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace QlLabels
{
    enum ClipKind { None, Objects, Svg, Emf, Picture, Files, Rtf, Text }

    sealed class ClipContent
    {
        public ClipKind Kind;
        public string ObjectsJson;     // Objects
        public byte[] Svg;             // Svg: cleaned (SvgPicture.Clean)
        public byte[] SvgSource;       // Svg: as the source gave it (its text for Import/SvgText.cs)
        public byte[] Emf;             // Emf: checked (EmfPicture.Check)
        public EmfPicture.Content EmfKind;
        public Bitmap Picture;         // Picture: 32 bpp ARGB, the caller's to dispose
        public string[] Files;         // Files
        public string Rtf;             // Rtf: the source
        public string Text;            // Rtf: its plain text; Text
        public bool AlsoText;          // the source also offered plain text (not SVG source): "Paste as plain text" works
        public SizeF? SizeMm;          // Svg, Emf, Picture: the size it was copied at, mm (pasted 1:1); null: none known
        public ImportSource Source = ImportSource.Unknown;   // the program it came from (Import/ImportSource.cs)
        public byte[] Cmx;             // Emf from CorelDRAW: its CMX as well (its text: Import/Corel/Cmx.cs; outlines and small text of a picture: Import/Corel/CorelPicture.cs); null: none
    }

    // What a clipboard or a drop holds; tests give their own. Get: string, byte[], MemoryStream or string[] (files);
    // EmfFormat: the EMF's bytes (the sources fetch CF_ENHMETAFILE themselves).
    interface IClipSource
    {
        bool Has(string format);
        object Get(string format);
    }

    // A source that knows the clipboard owner's process (a paste; not a drop).
    interface IClipOwner
    {
        string Owner { get; }
    }

    static class EditorClipboard
    {
        public const string OwnFormat = "QLUnidrv.LabelObjects";
        public const string EmfFormat = "QLUnidrv.EmfBytes";
        public static readonly string[] SvgFormats = { "image/svg+xml", "image/x-inkscape-svg", "Scalable Vector Graphics" };
        public const string PngFormat = "PNG", DibV5Format = "Format17";
        const int MaxPixels = 100 * 1000 * 1000;   // a picture's width x height, before it is scaled to ImageObject.MaxPixels
        const int MaxTextBytes = 4 * 1024 * 1024;

        // Paste and drop: the best format by the order above.
        public static ClipContent Read(IClipSource s)
        {
            var c = new ClipContent();
            var owner = s as IClipOwner;
            c.Source = ImportSource.Detect(s, owner != null ? owner.Owner : null);
            string plain = Text(s);
            bool plainIsSvg = plain != null && LooksLikeSvg(plain);
            c.AlsoText = plain != null && !plainIsSvg && plain.Trim().Length > 0;

            string own = Str(s, OwnFormat);
            if (own != null) { c.Kind = ClipKind.Objects; c.ObjectsJson = own; return c; }

            foreach (var f in SvgFormats)
            {
                var raw = Bytes(s, f);
                var svg = VectorSvg(raw);
                if (svg != null) { c.Kind = ClipKind.Svg; c.Svg = svg; c.SvgSource = Trimmed(raw); c.SizeMm = SvgSizeMm(s, c); return c; }
            }
            if (plainIsSvg)
            {
                var raw = Encoding.UTF8.GetBytes(plain);
                var svg = VectorSvg(raw);
                if (svg != null) { c.Kind = ClipKind.Svg; c.Svg = svg; c.SvgSource = Trimmed(raw); c.SizeMm = SvgSizeMm(s, c); return c; }
            }

            // RTF that is only text before the pictures: Word offers its text as an EMF too (dropped files still win)
            string rtf = Str(s, DataFormats.Rtf);
            if (rtf != null && RtfRuns.OnlyText(rtf) && !s.Has(DataFormats.FileDrop))
            {
                string text = RtfText(rtf);
                if (text.Trim().Length > 0) { c.Kind = ClipKind.Rtf; c.Rtf = rtf; c.Text = text; return c; }
            }

            var emf = Bytes(s, EmfFormat);
            SizeF? shown = null;   // an EMF that only wraps the picture: the size the program showed it at (Word)
            if (emf != null)
            {
                try
                {
                    var facts = EmfPicture.What(emf);
                    if (facts.Kind != EmfPicture.Content.OneBitmap && facts.Kind != EmfPicture.Content.Empty)
                    {
                        c.Kind = ClipKind.Emf; c.Emf = emf; c.EmfKind = facts.Kind; c.SizeMm = EmfPicture.FrameSizeMm(emf);
                        if (c.Source.IsCorelDraw) c.Cmx = Bytes(s, ImportSource.Cmx32Format);
                        return c;
                    }
                    if (facts.Kind == EmfPicture.Content.OneBitmap) shown = EmfPicture.FrameSizeMm(emf);
                }
                catch (InvalidDataException) { }   // damaged: the next format
            }

            foreach (var f in new[] { PngFormat, DibV5Format, DataFormats.Dib })
            {
                var b = Bytes(s, f);
                var picture = f == PngFormat ? Png(b) : Dib(b);
                if (picture == null) continue;
                c.Kind = ClipKind.Picture; c.Picture = picture;
                double dpiX, dpiY;
                if (f == PngFormat) PngDpi(b, out dpiX, out dpiY); else DibDpi(b, out dpiX, out dpiY);
                c.SizeMm = shown ?? new SizeF((float)(picture.Width * 25.4 / dpiX), (float)(picture.Height * 25.4 / dpiY));
                return c;
            }

            var files = s.Has(DataFormats.FileDrop) ? s.Get(DataFormats.FileDrop) as string[] : null;
            if (files != null && files.Length > 0) { c.Kind = ClipKind.Files; c.Files = files; return c; }

            if (rtf != null)
            {
                string text = RtfText(rtf);
                if (text.Trim().Length > 0) { c.Kind = ClipKind.Rtf; c.Rtf = rtf; c.Text = text; return c; }
            }

            if (c.AlsoText) { c.Kind = ClipKind.Text; c.Text = plain; }
            return c;
        }

        // Paste as plain text: the plain text only (not SVG source), else nothing.
        public static ClipContent ReadPlainText(IClipSource s)
        {
            var c = new ClipContent();
            string plain = Text(s);
            if (plain != null && !LooksLikeSvg(plain) && plain.Trim().Length > 0) { c.Kind = ClipKind.Text; c.Text = plain; c.AlsoText = true; }
            return c;
        }

        // Something to paste is offered (for greying the commands; cheap: the data isn't read).
        public static bool MayPaste(IClipSource s)
        {
            return s != null && (s.Has(OwnFormat) || SvgFormats.Any(s.Has) || s.Has(EmfFormat) || s.Has(PngFormat) || s.Has(DibV5Format) ||
                                 s.Has(DataFormats.Dib) || s.Has(DataFormats.FileDrop) || s.Has(DataFormats.Rtf) ||
                                 s.Has(DataFormats.UnicodeText) || s.Has(DataFormats.Text));
        }

        // ---------- the formats ----------

        static string Text(IClipSource s)
        {
            return Str(s, DataFormats.UnicodeText) ?? Str(s, DataFormats.Text);
        }

        static string Str(IClipSource s, string format)
        {
            if (!s.Has(format)) return null;
            object o;
            try { o = s.Get(format); } catch (Exception) { return null; }
            var str = o as string;
            if (str != null) return str.Length > MaxTextBytes ? null : str.TrimEnd('\0');
            var b = AsBytes(o);
            return b == null ? null : Encoding.UTF8.GetString(b).TrimEnd('\0');
        }

        static byte[] Bytes(IClipSource s, string format)
        {
            if (!s.Has(format)) return null;
            try { return AsBytes(s.Get(format)); } catch (Exception) { return null; }
        }

        static byte[] AsBytes(object o)
        {
            var b = o as byte[];
            if (b != null) return b.Length > EmfPicture.MaxBytes ? null : b;
            var ms = o as MemoryStream;
            if (ms != null) return ms.Length > EmfPicture.MaxBytes ? null : ms.ToArray();
            var str = o as string;
            if (str != null) return Encoding.UTF8.GetBytes(str.TrimEnd('\0'));
            return null;
        }

        // SVG source: starts with <?xml or <svg (after a byte order mark and white space) and has an svg root.
        public static bool LooksLikeSvg(string text)
        {
            string t = text.TrimStart('﻿', ' ', '\t', '\r', '\n');
            if (!(t.StartsWith("<?xml", StringComparison.Ordinal) || t.StartsWith("<svg", StringComparison.Ordinal))) return false;
            return t.IndexOf("<svg", StringComparison.Ordinal) >= 0;
        }

        // The data without the zeros some programs put at the end and the white space before it (copied source text).
        // An SVG's size as copied: its width and height, or Affinity's true size from its PDF (its SVG counts px at the
        // document's resolution: Import/Affinity/AffinityClip.cs).
        static SizeF? SvgSizeMm(IClipSource s, ClipContent c)
        {
            var own = SvgPicture.SizeMm(c.SvgSource);
            return c.Source.IsAffinity ? AffinityClip.SizeMm(own, Bytes(s, AffinityClip.PdfFormat)) : own;
        }

        static byte[] Trimmed(byte[] source)
        {
            if (source == null) return null;
            int n = source.Length;
            while (n > 0 && source[n - 1] == 0) n--;
            int start = 0;
            while (start < n && (source[start] == ' ' || source[start] == '\t' || source[start] == '\r' || source[start] == '\n')) start++;
            if (start == 0 && n == source.Length) return source;
            var trimmed = new byte[n - start];
            Buffer.BlockCopy(source, start, trimmed, 0, trimmed.Length);
            return trimmed;
        }

        // The cleaned SVG if it draws more than one embedded raster picture; null for no SVG, damaged, or raster-only
        // (Word wraps a PNG picture in an SVG with one <image>: the PNG on the clipboard is the same picture).
        public static byte[] VectorSvg(byte[] source)
        {
            if (source == null || source.Length == 0) return null;
            source = Trimmed(source);   // an XML declaration must come first
            byte[] clean;
            try { clean = SvgPicture.Clean(source); }
            catch (InvalidDataException) { return null; }
            return RasterOnly(clean) ? null : clean;
        }

        static readonly string[] Drawing = { "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "use" };

        // Only one embedded picture, nothing drawn. Word wraps a copied photo so: the <image> in <defs>, drawn by a <use>,
        // clipped by a <rect> in a <clipPath>; neither the <use> of the picture nor what only clips or is only defined
        // counts as drawing.
        public static bool RasterOnly(byte[] svg)
        {
            if (SvgPicture.TooDeep(svg)) return false;
            var doc = new XmlDocument { XmlResolver = null };
            try
            {
                using (var ms = new MemoryStream(svg))
                using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                    doc.Load(reader);
            }
            catch (XmlException) { return false; }
            var all = doc.DocumentElement.SelectNodes("descendant::*").OfType<XmlElement>().ToList();
            int images = all.Count(e => e.LocalName == "image");
            bool drawn = all.Any(e => Drawing.Contains(e.LocalName) && !OnlyDefined(e) && !(e.LocalName == "use" && UsesImage(e)));
            return images == 1 && !drawn;
        }

        static readonly string[] NotDrawn = { "defs", "clipPath", "mask", "symbol", "pattern", "marker" };

        static bool OnlyDefined(XmlElement e)
        {
            for (var p = e.ParentNode as XmlElement; p != null; p = p.ParentNode as XmlElement)
                if (NotDrawn.Contains(p.LocalName)) return true;
            return false;
        }

        // A <use> of an <image> (href or xlink:href "#id").
        static bool UsesImage(XmlElement use)
        {
            string href = use.GetAttribute("href");
            if (href.Length == 0) href = use.GetAttribute("href", "http://www.w3.org/1999/xlink");
            if (!href.StartsWith("#")) return false;
            string id = href.Substring(1);
            return use.OwnerDocument.DocumentElement.SelectNodes("descendant::*").OfType<XmlElement>()
                      .Any(x => x.LocalName == "image" && x.GetAttribute("id") == id);
        }

        static Bitmap Png(byte[] data)
        {
            if (data == null || data.Length < 8) return null;
            try
            {
                using (var ms = new MemoryStream(data))
                using (var img = Image.FromStream(ms, false, true))
                {
                    if ((long)img.Width * img.Height > MaxPixels) return null;
                    var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                    bmp.SetResolution(96, 96);
                    using (var g = Graphics.FromImage(bmp)) g.DrawImage(img, 0, 0, img.Width, img.Height);
                    return bmp;
                }
            }
            catch (Exception) { return null; }
        }

        // A bitmap's resolution, as CorelDRAW reads it: a PNG's pHYs chunk (per metre), a DIB's biX/YPelsPerMeter; 96 dpi
        // when there is none or it is unlikely (under 10 or over 10000 dpi).
        public static void PngDpi(byte[] png, out double x, out double y)
        {
            x = y = 96;
            if (png == null) return;
            for (int at = 8; at + 12 <= png.Length;)
            {
                int len = (png[at] << 24) | (png[at + 1] << 16) | (png[at + 2] << 8) | png[at + 3];
                string type = Encoding.ASCII.GetString(png, at + 4, 4);
                if (len < 0 || at + 12 + len > png.Length || type == "IDAT" || type == "IEND") return;
                if (type == "pHYs" && len >= 9 && png[at + 16] == 1)
                {
                    Dpi(BigEndian(png, at + 8), BigEndian(png, at + 12), out x, out y);
                    return;
                }
                at += 12 + len;
            }
        }

        public static void DibDpi(byte[] dib, out double x, out double y)
        {
            x = y = 96;
            if (dib != null && dib.Length >= 40) Dpi(BitConverter.ToInt32(dib, 24), BitConverter.ToInt32(dib, 28), out x, out y);
        }

        static void Dpi(long perMetreX, long perMetreY, out double x, out double y)
        {
            x = perMetreX * 0.0254; y = perMetreY * 0.0254;
            if (x < 10 || y < 10 || x > 10000 || y > 10000) x = y = 96;
        }

        static long BigEndian(byte[] d, int at) { return ((long)d[at] << 24) | ((long)d[at + 1] << 16) | ((long)d[at + 2] << 8) | d[at + 3]; }

        // A packed DIB (BITMAPINFOHEADER, V4 or V5 header, then colours and pixels): 32 bpp with its alpha (all zero
        // means opaque, as programs write CF_DIB), anything else through a BMP file header and GDI+.
        public static Bitmap Dib(byte[] d)
        {
            if (d == null || d.Length < 40) return null;
            try
            {
                int headerSize = BitConverter.ToInt32(d, 0);
                if (headerSize < 40 || headerSize > d.Length) return null;
                int w = BitConverter.ToInt32(d, 4), hRaw = BitConverter.ToInt32(d, 8);
                int bits = BitConverter.ToInt16(d, 14), compression = BitConverter.ToInt32(d, 16);
                int h = Math.Abs(hRaw);
                if (w <= 0 || h <= 0 || (long)w * h > MaxPixels) return null;
                if (bits == 32 && (compression == 0 || compression == 3))
                {
                    int masks = compression == 3 && headerSize == 40 ? 12 : 0;   // BI_BITFIELDS after a plain header
                    int at = headerSize + masks;
                    long stride = (long)w * 4;
                    if (at + stride * h > d.Length) return null;
                    bool anyAlpha = false;
                    for (long i = at + 3; i < at + stride * h; i += 4) if (d[i] != 0) { anyAlpha = true; break; }
                    var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                    bmp.SetResolution(96, 96);
                    var lockData = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    try
                    {
                        var row = new byte[stride];
                        for (int y = 0; y < h; y++)
                        {
                            int src = hRaw > 0 ? h - 1 - y : y;   // positive height: bottom-up
                            Buffer.BlockCopy(d, (int)(at + src * stride), row, 0, (int)stride);
                            if (!anyAlpha) for (int i = 3; i < row.Length; i += 4) row[i] = 255;
                            Marshal.Copy(row, 0, lockData.Scan0 + y * lockData.Stride, (int)stride);
                        }
                    }
                    finally { bmp.UnlockBits(lockData); }
                    return bmp;
                }
                // Other depths: a BMP file around it.
                int colours = BitConverter.ToInt32(d, 32);
                if (colours == 0 && bits <= 8) colours = 1 << bits;
                int offset = 14 + headerSize + (compression == 3 && headerSize == 40 ? 12 : 0) + colours * 4;
                var file = new byte[14 + d.Length];
                file[0] = (byte)'B'; file[1] = (byte)'M';
                BitConverter.GetBytes(file.Length).CopyTo(file, 2);
                BitConverter.GetBytes(offset).CopyTo(file, 10);
                Buffer.BlockCopy(d, 0, file, 14, d.Length);
                return Png(file);   // GDI+ reads BMP as well
            }
            catch (Exception) { return null; }
        }

        // ---------- RTF to plain text ----------

        // The text of an RTF document (RtfRuns reads it): paragraphs and line breaks as new lines, tabs, \'hh in the
        // font's or document's code page, \uN with its fallback skipped; fonts, colours, styles, pictures and \*
        // destinations left out.
        public static string RtfText(string rtf)
        {
            return RtfRuns.PlainText(rtf).Replace("\n", "\r\n");
        }

        // ---------- the sources ----------

        // The Windows clipboard (UI thread); null when another program holds it.
        public static IClipSource FromClipboard()
        {
            ComTypes.IDataObject data;
            return OleGetClipboard(out data) == 0 && data != null ? new ComSource(data) { Owner = ImportSource.ClipboardOwner() } : null;
        }

        // A drop's data (the drop's own data only; a URL alone is never followed).
        public static IClipSource FromDrop(IDataObject data)
        {
            var com = data as ComTypes.IDataObject;
            return com != null ? new ComSource(com) : null;
        }

        // Reads the formats as bytes through COM: WinForms' GetData would deserialize a .NET object that another program
        // put under a format's name, so it is never used for reading. Get: string for the text formats, string[] for
        // files, byte[] for the others (EmfFormat: the EMF's bytes).
        sealed class ComSource : IClipSource, IClipOwner
        {
            readonly ComTypes.IDataObject data;
            public string Owner { get; set; }
            public ComSource(ComTypes.IDataObject data) { this.data = data; }

            static short Id(string format)
            {
                if (format == DataFormats.UnicodeText) return CF_UNICODETEXT;
                if (format == DataFormats.Text) return CF_TEXT;
                if (format == DataFormats.Dib) return CF_DIB;
                if (format == DibV5Format) return CF_DIBV5;
                if (format == DataFormats.FileDrop) return CF_HDROP;
                if (format == EmfFormat) return CF_ENHMETAFILE;
                return unchecked((short)RegisterClipboardFormat(format));
            }

            static ComTypes.FORMATETC Etc(string format)
            {
                var tymed = format == EmfFormat ? ComTypes.TYMED.TYMED_ENHMF : ComTypes.TYMED.TYMED_HGLOBAL | ComTypes.TYMED.TYMED_ISTREAM;
                return new ComTypes.FORMATETC { cfFormat = Id(format), dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = tymed };
            }

            public bool Has(string format)
            {
                try { var etc = Etc(format); return etc.cfFormat != 0 && data.QueryGetData(ref etc) == 0; }
                catch (Exception) { return false; }
            }

            public object Get(string format)
            {
                var etc = Etc(format);
                if (etc.cfFormat == 0) return null;
                ComTypes.STGMEDIUM m;
                try { data.GetData(ref etc, out m); }
                catch (Exception) { return null; }
                try
                {
                    if (m.tymed == ComTypes.TYMED.TYMED_ENHMF) return EmfPicture.FromHandle(m.unionmember);
                    if (format == DataFormats.FileDrop) return m.tymed == ComTypes.TYMED.TYMED_HGLOBAL ? Files(m.unionmember) : null;
                    byte[] b = m.tymed == ComTypes.TYMED.TYMED_HGLOBAL ? Global(m.unionmember)
                             : m.tymed == ComTypes.TYMED.TYMED_ISTREAM ? Stream(m.unionmember) : null;
                    if (b == null) return null;
                    if (format == DataFormats.UnicodeText) return Encoding.Unicode.GetString(b, 0, b.Length & ~1).TrimEnd('\0');
                    if (format == DataFormats.Text) return Encoding.Default.GetString(b).TrimEnd('\0');
                    return b;
                }
                catch (Exception) { return null; }
                finally { ReleaseStgMedium(ref m); }
            }

            static byte[] Global(IntPtr h)
            {
                long n = GlobalSize(h).ToInt64();
                if (n <= 0 || n > EmfPicture.MaxBytes) return null;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) return null;
                try { var b = new byte[n]; Marshal.Copy(p, b, 0, (int)n); return b; }
                finally { GlobalUnlock(h); }
            }

            static byte[] Stream(IntPtr unk)
            {
                var s = (ComTypes.IStream)Marshal.GetObjectForIUnknown(unk);
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536];
                    IntPtr read = Marshal.AllocHGlobal(4);
                    try
                    {
                        while (true)
                        {
                            s.Read(buf, buf.Length, read);
                            int n = Marshal.ReadInt32(read);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                            if (ms.Length > EmfPicture.MaxBytes) return null;
                        }
                    }
                    finally { Marshal.FreeHGlobal(read); Marshal.ReleaseComObject(s); }
                    return ms.ToArray();
                }
            }

            static string[] Files(IntPtr hdrop)
            {
                uint n = DragQueryFile(hdrop, 0xFFFFFFFF, null, 0);
                var list = new List<string>();
                for (uint i = 0; i < n && i < 100; i++)
                {
                    var sb = new StringBuilder(32768);
                    if (DragQueryFile(hdrop, i, sb, sb.Capacity) > 0) list.Add(sb.ToString());
                }
                return list.ToArray();
            }
        }

        const short CF_TEXT = 1, CF_DIB = 8, CF_UNICODETEXT = 13, CF_ENHMETAFILE = 14, CF_HDROP = 15, CF_DIBV5 = 17;
        [DllImport("ole32.dll")] static extern int OleGetClipboard(out ComTypes.IDataObject data);
        [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetClipboardData(uint format, IntPtr data);
        [DllImport("gdi32.dll")] static extern IntPtr SetEnhMetaFileBits(uint size, byte[] data);
        [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(IntPtr emf);

        // Copy: puts the copied objects on the clipboard as the EMF (CF_ENHMETAFILE), our own format and a DIB, in that
        // order; no PNG: Word pastes a PNG whenever one is offered, also after the EMF (test word-paste.ps1, 2026-11-08).
        // Copy goes out as EMF; the DIB only for programs without EMF (Paint; labels are white, no transparency needed).
        // Windows adds METAFILEPICT, BITMAP and DIBV5 itself. emf and dib may be null. False when the clipboard stays
        // busy (another program holds it).
        public static bool SetCopy(IntPtr owner, byte[] emf, byte[] own, byte[] dib)
        {
            bool open = false;
            for (int i = 0; i < 5 && !(open = OpenClipboard(owner)); i++) System.Threading.Thread.Sleep(100);
            if (!open) return false;
            try
            {
                if (!EmptyClipboard()) return false;
                if (emf != null)
                {
                    IntPtr h = SetEnhMetaFileBits((uint)emf.Length, emf);
                    if (h != IntPtr.Zero && SetClipboardData((uint)CF_ENHMETAFILE, h) == IntPtr.Zero) DeleteEnhMetaFile(h);  // else the clipboard owns it
                }
                SetBytes(RegisterClipboardFormat(OwnFormat), own);
                if (dib != null) SetBytes((uint)CF_DIB, dib);
                return true;
            }
            finally { CloseClipboard(); }
        }

        static void SetBytes(uint format, byte[] data)
        {
            if (format == 0 || data == null) return;
            IntPtr h = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)Math.Max(1, data.Length));
            if (h == IntPtr.Zero) return;
            IntPtr p = GlobalLock(h);
            if (p == IntPtr.Zero) { GlobalFree(h); return; }
            Marshal.Copy(data, 0, p, data.Length);
            GlobalUnlock(h);
            if (SetClipboardData(format, h) == IntPtr.Zero) GlobalFree(h);   // else the clipboard owns it
        }
        const uint GMEM_MOVEABLE = 0x0002;
        [DllImport("user32.dll")] static extern bool EmptyClipboard();
        [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll", EntryPoint = "GlobalSize")] static extern IntPtr GlobalSize(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern uint DragQueryFile(IntPtr hdrop, uint index, StringBuilder file, int size);
    }
}
