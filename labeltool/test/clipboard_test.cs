// Unit test for pasting and dropping in QL Label Maker (EditorClipboard.cs, EmfPicture.cs): the format order
// (EditorClipboard.cs), SVG under its names and as source text, raster-only SVG and one-bitmap EMF skipped, PNG and
// DIB pictures with their alpha, files, RTF to text; the editor's side: our own format, EMF pictures, copied DIBs, text. Every fixture is made here (GDI for the EMFs), no clipboard and
// no printer needed. Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class ClipboardTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        // A clipboard or drop made of formats.
        sealed class Source : IClipSource
        {
            readonly Dictionary<string, object> data = new Dictionary<string, object>();
            public Source Add(string format, object value) { data[format] = value; return this; }
            public bool Has(string format) { return data.ContainsKey(format); }
            public object Get(string format) { object o; return data.TryGetValue(format, out o) ? o : null; }
        }

        static int Main()
        {
            Lang.Force("en");
            Emf();
            Order();
            Svg();
            Pictures();
            Rtf();
            Editor();
            TextFromEmf();
            CopyEmf();
            OwnSize();
            Console.WriteLine(failures == 0 ? "clipboard_test: all OK" : "clipboard_test: " + failures + " failure(s)");
            return failures == 0 ? 0 : 1;
        }

        // ---------- fixtures ----------

        const string VectorSvgText = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20mm\" height=\"10mm\" viewBox=\"0 0 20 10\">" +
                                     "<rect x=\"1\" y=\"1\" width=\"18\" height=\"8\" fill=\"black\"/></svg>";
        static readonly string RasterSvgText = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"20\" height=\"10\">" +
                                               "<image width=\"20\" height=\"10\" xlink:href=\"data:image/png;base64," + Convert.ToBase64String(PngBytes(4, 2, Color.Red)) + "\"/></svg>";

        static byte[] PngBytes(int w, int h, Color c)
        {
            using (var b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var ms = new MemoryStream())
            {
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) b.SetPixel(x, y, x == 0 ? Color.Transparent : c);
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        // A packed 32 bpp DIB, bottom-up (positive height) unless topDown; V5 header (124 bytes) or plain (40).
        static byte[] Dib32(int w, int h, bool v5, bool topDown, Func<int, int, Color> pixel)
        {
            int header = v5 ? 124 : 40;
            var d = new byte[header + w * h * 4];
            BitConverter.GetBytes(header).CopyTo(d, 0);
            BitConverter.GetBytes(w).CopyTo(d, 4);
            BitConverter.GetBytes(topDown ? -h : h).CopyTo(d, 8);
            BitConverter.GetBytes((short)1).CopyTo(d, 12);
            BitConverter.GetBytes((short)32).CopyTo(d, 14);
            BitConverter.GetBytes(v5 ? 3 : 0).CopyTo(d, 16);
            if (v5)
            {
                BitConverter.GetBytes(0x00FF0000).CopyTo(d, 40);
                BitConverter.GetBytes(0x0000FF00).CopyTo(d, 44);
                BitConverter.GetBytes(0x000000FF).CopyTo(d, 48);
                BitConverter.GetBytes(unchecked((int)0xFF000000)).CopyTo(d, 52);
            }
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int row = topDown ? y : h - 1 - y;
                    int at = header + (row * w + x) * 4;
                    var c = pixel(x, y);
                    d[at] = c.B; d[at + 1] = c.G; d[at + 2] = c.R; d[at + 3] = c.A;
                }
            return d;
        }

        // A packed 24 bpp DIB (rows padded to 4 bytes), bottom-up.
        static byte[] Dib24(int w, int h, Func<int, int, Color> pixel)
        {
            int stride = (w * 3 + 3) & ~3;
            var d = new byte[40 + stride * h];
            BitConverter.GetBytes(40).CopyTo(d, 0);
            BitConverter.GetBytes(w).CopyTo(d, 4);
            BitConverter.GetBytes(h).CopyTo(d, 8);
            BitConverter.GetBytes((short)1).CopyTo(d, 12);
            BitConverter.GetBytes((short)24).CopyTo(d, 14);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int at = 40 + (h - 1 - y) * stride + x * 3;
                    var c = pixel(x, y);
                    d[at] = c.B; d[at + 1] = c.G; d[at + 2] = c.R;
                }
            return d;
        }

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateEnhMetaFileW(IntPtr hdcRef, string file, ref RECT frame, string description);
        [DllImport("gdi32.dll")] static extern IntPtr CloseEnhMetaFile(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(IntPtr hemf);
        [DllImport("gdi32.dll")] static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr hdc, int x, int y, string text, int n);
        [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr hdc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, byte[] bits, byte[] info, uint usage, uint rop);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        // An EMF of 30 x 20 mm (or frameW x frameH in 0.01 mm) drawn by draw (GDI on the metafile's DC).
        static byte[] MakeEmf(Action<IntPtr> draw, int frameW = 3000, int frameH = 2000)
        {
            var frame = new RECT { L = 0, T = 0, R = frameW, B = frameH };   // 0.01 mm
            IntPtr dc = CreateEnhMetaFileW(IntPtr.Zero, null, ref frame, null);
            draw(dc);
            IntPtr h = CloseEnhMetaFile(dc);
            try { return EmfPicture.FromHandle(h); }
            finally { DeleteEnhMetaFile(h); }
        }

        static void Bitmap(IntPtr dc, int x)
        {
            var dib = Dib32(4, 4, false, false, (a, b) => Color.Black);
            var info = new byte[40];
            Array.Copy(dib, info, 40);
            var bits = new byte[dib.Length - 40];
            Array.Copy(dib, 40, bits, 0, bits.Length);
            StretchDIBits(dc, x, 0, 40, 40, 0, 0, 4, 4, bits, info, 0, 0x00CC0020);
        }

        // ---------- tests ----------

        static void Emf()
        {
            var vector = MakeEmf(dc => Rectangle(dc, 10, 10, 40, 30));
            var oneBitmap = MakeEmf(dc => Bitmap(dc, 0));
            var twoBitmaps = MakeEmf(dc => { Bitmap(dc, 0); Bitmap(dc, 50); });
            var textOnly = MakeEmf(dc => TextOutW(dc, 10, 10, "Abc", 3));
            var bitmapText = MakeEmf(dc => { Bitmap(dc, 0); TextOutW(dc, 60, 10, "Abc", 3); });
            var empty = MakeEmf(dc => { });
            Check(vector != null && EmfPicture.What(vector).Kind == EmfPicture.Content.Vector, "emf: a rectangle is a vector");
            Check(EmfPicture.What(oneBitmap).Kind == EmfPicture.Content.OneBitmap, "emf: one bitmap");
            Check(EmfPicture.What(twoBitmaps).Kind == EmfPicture.Content.Vector, "emf: two bitmaps stay an EMF");
            Check(EmfPicture.What(textOnly).Kind == EmfPicture.Content.TextOnly && EmfPicture.What(textOnly).HasText, "emf: text only");
            Check(EmfPicture.What(bitmapText).Kind == EmfPicture.Content.Vector, "emf: a bitmap with text stays an EMF");
            Check(EmfPicture.What(empty).Kind == EmfPicture.Content.Empty, "emf: nothing drawn");
            var size = EmfPicture.SizeMm(vector);
            Check(Math.Abs(size.Width - 30) < 0.5 && Math.Abs(size.Height - 20) < 0.5, "emf: size from the frame, " + size);
            using (var bmp = EmfPicture.Render(vector, 300, 200))
            {
                bool drawn = false;
                for (int y = 0; y < bmp.Height && !drawn; y += 2) for (int x = 0; x < bmp.Width; x += 2) if (bmp.GetPixel(x, y).A > 128) { drawn = true; break; }
                Check(drawn && bmp.GetPixel(299, 199).A == 0, "emf: drawn on transparent");
            }
            Check(Throws(() => EmfPicture.Check(new byte[10])), "emf: too short");
            var wrong = (byte[])vector.Clone(); wrong[40] = 0;
            Check(Throws(() => EmfPicture.Check(wrong)), "emf: wrong signature");
            var cut = new byte[vector.Length - 20]; Array.Copy(vector, cut, cut.Length);
            Check(Throws(() => EmfPicture.Check(cut)), "emf: cut off");
            var badRecord = (byte[])vector.Clone();
            int firstSize = BitConverter.ToInt32(badRecord, 4);
            BitConverter.GetBytes(7).CopyTo(badRecord, firstSize + 4);   // the second record's size
            Check(Throws(() => EmfPicture.Check(badRecord)), "emf: damaged record size");
            Check(EmfPicture.FromHandle(IntPtr.Zero) == null, "emf: no handle");
        }

        static bool Throws(Action a)
        {
            try { a(); return false; }
            catch (InvalidDataException) { return true; }
        }

        static void Order()
        {
            var png = PngBytes(4, 2, Color.Blue);
            var vector = MakeEmf(dc => Rectangle(dc, 10, 10, 200, 100));
            var oneBitmap = MakeEmf(dc => Bitmap(dc, 0));
            var dib = Dib24(3, 2, (x, y) => Color.Green);

            var all = new Source().Add(EditorClipboard.OwnFormat, "{\"objects\":[]}").Add("image/svg+xml", VectorSvgText)
                .Add(EditorClipboard.EmfFormat, vector).Add(EditorClipboard.PngFormat, new MemoryStream(png))
                .Add(DataFormats.FileDrop, new[] { @"C:\x.png" }).Add(DataFormats.Rtf, @"{\rtf1 Hi\par}").Add(DataFormats.UnicodeText, "Hi");
            var c = EditorClipboard.Read(all);
            Check(c.Kind == ClipKind.Objects && c.ObjectsJson == "{\"objects\":[]}" && c.AlsoText, "order: own objects first");
            all = new Source().Add("image/svg+xml", VectorSvgText).Add(EditorClipboard.EmfFormat, vector).Add(EditorClipboard.PngFormat, png);
            Check(EditorClipboard.Read(all).Kind == ClipKind.Svg, "order: SVG before EMF");
            all = new Source().Add(EditorClipboard.EmfFormat, vector).Add(EditorClipboard.PngFormat, png).Add(DataFormats.UnicodeText, "x");
            c = EditorClipboard.Read(all);
            Check(c.Kind == ClipKind.Emf && c.Emf == vector && c.EmfKind == EmfPicture.Content.Vector && c.AlsoText, "order: EMF before PNG");
            all = new Source().Add(EditorClipboard.EmfFormat, oneBitmap).Add(EditorClipboard.PngFormat, png);
            c = EditorClipboard.Read(all);
            Check(c.Kind == ClipKind.Picture && c.Picture.Width == 4, "order: an EMF of one bitmap gives way to the PNG");
            all = new Source().Add(EditorClipboard.EmfFormat, new byte[] { 1, 2, 3 }).Add(DataFormats.Dib, dib);
            Check(EditorClipboard.Read(all).Kind == ClipKind.Picture, "order: a damaged EMF is skipped");
            var textEmf = MakeEmf(dc => TextOutW(dc, 10, 10, "Abc", 3));
            c = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, textEmf));
            Check(c.Kind == ClipKind.Emf && c.EmfKind == EmfPicture.Content.TextOnly, "order: a text-only EMF is an EMF");
            c = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, vector).Add(EditorClipboard.PngFormat, png).Add(DataFormats.Rtf, @"{\rtf1 Abc\par}"));
            Check(c.Kind == ClipKind.Rtf && c.Text == "Abc", "order: RTF that is only text before EMF and PNG (Word)");
            c = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, vector).Add(DataFormats.Rtf, @"{\rtf1 {\pict\pngblip 00}Abc\par}"));
            Check(c.Kind == ClipKind.Emf, "order: RTF with a picture leaves the EMF first");
            c = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, vector).Add(DataFormats.Rtf, @"{\rtf1 \trowd\cellx1000 A\cell B\cell\row}"));
            Check(c.Kind == ClipKind.Emf, "order: an RTF table leaves the EMF first (Excel)");
            all = new Source().Add(EditorClipboard.PngFormat, png).Add(EditorClipboard.DibV5Format, Dib32(2, 2, true, false, (x, y) => Color.Red)).Add(DataFormats.Dib, dib);
            Check(EditorClipboard.Read(all).Picture.Width == 4, "order: PNG before DIBV5 and DIB");
            all = new Source().Add(EditorClipboard.DibV5Format, Dib32(2, 2, true, false, (x, y) => Color.Red)).Add(DataFormats.Dib, dib);
            Check(EditorClipboard.Read(all).Picture.Width == 2, "order: DIBV5 before DIB");
            all = new Source().Add(DataFormats.FileDrop, new[] { @"C:\a.svg", @"C:\b.png" }).Add(DataFormats.Rtf, @"{\rtf1 x}");
            c = EditorClipboard.Read(all);
            Check(c.Kind == ClipKind.Files && c.Files.Length == 2, "order: files before RTF");
            c = EditorClipboard.Read(new Source().Add(DataFormats.Rtf, @"{\rtf1\ansi Hello\par}").Add(DataFormats.UnicodeText, "Hello"));
            Check(c.Kind == ClipKind.Rtf && c.Text == "Hello" && c.AlsoText, "order: RTF before text");
            c = EditorClipboard.Read(new Source().Add(DataFormats.Rtf, @"{\rtf1\ansi {\fonttbl{\f0 Arial;}}\par}").Add(DataFormats.UnicodeText, "x"));
            Check(c.Kind == ClipKind.Text && c.Text == "x", "order: RTF without text gives way to the text");
            c = EditorClipboard.Read(new Source().Add(DataFormats.UnicodeText, "Line 1\r\nLine 2"));
            Check(c.Kind == ClipKind.Text && c.Text == "Line 1\r\nLine 2", "order: plain text");
            c = EditorClipboard.Read(new Source().Add(DataFormats.Text, "ansi"));
            Check(c.Kind == ClipKind.Text && c.Text == "ansi", "order: CF_TEXT when there is no Unicode text");
            c = EditorClipboard.Read(new Source().Add(DataFormats.Html, "<b>x</b>").Add("UniformResourceLocatorW", "https://example.com/a.png").Add("Portable Document Format", new byte[] { 1 }));
            Check(c.Kind == ClipKind.None, "order: HTML, URL and PDF are never used");
            c = EditorClipboard.Read(new Source().Add(DataFormats.UnicodeText, "  \r\n"));
            Check(c.Kind == ClipKind.None && !c.AlsoText, "order: white space only is nothing");
            Check(EditorClipboard.Read(new Source()).Kind == ClipKind.None, "order: empty");

            c = EditorClipboard.ReadPlainText(new Source().Add(DataFormats.Rtf, @"{\rtf1 Hi\par}").Add(DataFormats.UnicodeText, "Hi there").Add(EditorClipboard.PngFormat, png));
            Check(c.Kind == ClipKind.Text && c.Text == "Hi there", "plain text: only the text");
            Check(EditorClipboard.ReadPlainText(new Source().Add(DataFormats.UnicodeText, VectorSvgText)).Kind == ClipKind.None, "plain text: SVG source isn't text");
            Check(EditorClipboard.ReadPlainText(new Source().Add(EditorClipboard.PngFormat, png)).Kind == ClipKind.None, "plain text: none");
        }

        static void Svg()
        {
            foreach (var name in EditorClipboard.SvgFormats)
            {
                var c = EditorClipboard.Read(new Source().Add(name, new MemoryStream(Encoding.UTF8.GetBytes(VectorSvgText + "\0"))));
                Check(c.Kind == ClipKind.Svg && c.Svg != null && Encoding.UTF8.GetString(c.Svg).Contains("<rect"), "svg: under '" + name + "'");
            }
            var src = EditorClipboard.Read(new Source().Add(DataFormats.UnicodeText, "\r\n  <?xml version=\"1.0\"?>\n" + VectorSvgText));
            Check(src.Kind == ClipKind.Svg && !src.AlsoText, "svg: source in the plain text is a picture, never a text object");
            var raster = EditorClipboard.Read(new Source().Add("image/svg+xml", RasterSvgText).Add(EditorClipboard.PngFormat, PngBytes(4, 2, Color.Red)));
            Check(raster.Kind == ClipKind.Picture, "svg: one embedded picture only gives way to the PNG");
            // Word's copied photo: the picture in <defs>, drawn by a <use>, clipped by a rectangle
            string png = Convert.ToBase64String(PngBytes(4, 2, Color.Red));
            var word = "<svg width=\"20\" height=\"10\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs>" +
                       "<image width=\"4\" height=\"2\" xlink:href=\"data:image/png;base64," + png + "\" id=\"img0\"></image>" +
                       "<clipPath id=\"clip1\"><rect x=\"0\" y=\"0\" width=\"20\" height=\"10\"/></clipPath></defs>" +
                       "<g clip-path=\"url(#clip1)\"><use xlink:href=\"#img0\" transform=\"scale(5)\"></use></g></svg>";
            Check(EditorClipboard.Read(new Source().Add("image/svg+xml", word).Add(EditorClipboard.PngFormat, PngBytes(4, 2, Color.Red))).Kind == ClipKind.Picture,
                  "svg: Word's photo (image used and clipped) gives way to the PNG");
            var drawnUse = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><defs><path id=\"p\" d=\"M0 0 L9 9\"/>" +
                           "<image id=\"i\" width=\"4\" height=\"2\" xlink:href=\"data:image/png;base64," + png + "\"/></defs><use xlink:href=\"#p\"/><use xlink:href=\"#i\"/></svg>";
            Check(EditorClipboard.Read(new Source().Add("image/svg+xml", drawnUse).Add(EditorClipboard.PngFormat, PngBytes(4, 2, Color.Red))).Kind == ClipKind.Svg,
                  "svg: a picture and a drawn path stay an SVG");
            Check(EditorClipboard.Read(new Source().Add("image/svg+xml", "<svg><broken")).Kind == ClipKind.None, "svg: damaged is skipped");
            var hostile = "<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///c:/windows/win.ini\">]>" +
                          "<svg xmlns=\"http://www.w3.org/2000/svg\"><text>&x;</text></svg>";
            Check(EditorClipboard.Read(new Source().Add("image/svg+xml", hostile)).Kind == ClipKind.None, "svg: a DTD is refused");
            Check(EditorClipboard.LooksLikeSvg("<svg/>") && EditorClipboard.LooksLikeSvg("\uFEFF<?xml version=\"1.0\"?><svg/>") &&
                  !EditorClipboard.LooksLikeSvg("text <svg>") && !EditorClipboard.LooksLikeSvg("<?xml version=\"1.0\"?><html/>"), "svg: source detection");
        }

        static void Pictures()
        {
            using (var p = EditorClipboard.Read(new Source().Add(EditorClipboard.PngFormat, PngBytes(4, 2, Color.Blue))).Picture)
                Check(p.PixelFormat == PixelFormat.Format32bppArgb && p.GetPixel(0, 0).A == 0 && p.GetPixel(1, 1).ToArgb() == Color.Blue.ToArgb(), "png: alpha kept");
            // DIBV5: alpha kept, bottom-up rows the right way round
            Func<int, int, Color> corner = (x, y) => x == 0 && y == 0 ? Color.FromArgb(255, 255, 0, 0) : Color.FromArgb(0, 0, 0, 0);
            using (var p = EditorClipboard.Dib(Dib32(3, 2, true, false, corner)))
                Check(p.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && p.GetPixel(2, 1).A == 0, "dibv5: alpha and rows");
            using (var p = EditorClipboard.Dib(Dib32(3, 2, true, true, corner)))
                Check(p.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && p.GetPixel(0, 1).A == 0, "dibv5: top-down");
            // CF_DIB 32 bpp with no alpha at all: opaque
            using (var p = EditorClipboard.Dib(Dib32(2, 2, false, false, (x, y) => Color.FromArgb(0, 0, 255, 0))))
                Check(p.GetPixel(1, 1).ToArgb() == Color.Lime.ToArgb(), "dib: zero alpha means opaque");
            using (var p = EditorClipboard.Dib(Dib24(3, 2, (x, y) => y == 0 ? Color.Red : Color.Blue)))
                Check(p != null && p.Width == 3 && p.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && p.GetPixel(2, 1).ToArgb() == Color.Blue.ToArgb(), "dib: 24 bpp");
            var huge = Dib32(1, 1, false, false, (x, y) => Color.Red);
            BitConverter.GetBytes(20000).CopyTo(huge, 4); BitConverter.GetBytes(20000).CopyTo(huge, 8);
            Check(EditorClipboard.Dib(huge) == null, "dib: too many pixels");
            var cut = Dib32(4, 4, false, false, (x, y) => Color.Red);
            Array.Resize(ref cut, 60);
            Check(EditorClipboard.Dib(cut) == null, "dib: cut off");
            Check(EditorClipboard.Dib(new byte[8]) == null && EditorClipboard.Read(new Source().Add(EditorClipboard.PngFormat, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 })).Kind == ClipKind.None, "picture: damaged");
        }

        static void Rtf()
        {
            Check(EditorClipboard.RtfText(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Arial;}}{\colortbl;\red0\green0\blue0;}" +
                                          @"{\*\generator Test;}\pard\f0\fs20 Gr\'fc\'dfe\par Zeile 2\line x\tab y\par}") == "Grüße\r\nZeile 2\r\nx\ty",
                  "rtf: tables skipped, code page, paragraphs, line, tab");
            Check(EditorClipboard.RtfText(@"{\rtf1\uc1 \u8364?\u-3913? A}") == "\u20AC\uF0B7 A", "rtf: \\u with fallback skipped, negative values");
            Check(EditorClipboard.RtfText(@"{\rtf1\uc2 \u8364\'80\'80 B}") == "\u20AC B", "rtf: \\uc2 skips two fallback bytes");
            Check(EditorClipboard.RtfText(@"{\rtf1 a\\b\{c\}\~d\emdash e}") == "a\\b{c}\u00A0d\u2014e", "rtf: escapes and symbols");
            Check(EditorClipboard.RtfText(@"{\rtf1 {\*\shppict{\pict\pngblip 89504e47}}{\nonshppict{\pict 00}}Text}") == "Text", "rtf: pictures skipped");
            Check(EditorClipboard.RtfText(@"{\rtf1\ansi\ansicpg1251 \'cf}") == "\u041F", "rtf: other code page");

            // runs: Word's way of writing (theme font with panose, groups per word, \plain, \fs in half points)
            const string word = @"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl{\f0\fswiss\fcharset0 Arial;}" +
                @"{\flominor\f31506\fbidi \fswiss\fcharset0\fprq2{\*\panose 020b0004020202020204}Segoe UI;}{\f240\fswiss\fcharset0 Arial Black;}" +
                @"{\f5\fnil\fcharset204 Arial;}}{\colortbl;\red255\green0\blue0;}{\*\generator Test;}" +
                @"\pard\plain \ltrpar\qc \f31506\fs24\lang1031 {\ul Heute} {\b ist }ein sch\'f6ner {\i Tag}! {\f240\cf1 Satz}\par " +
                @"\pard\plain \qc \f31506\fs32 Gro\'df{\v versteckt}{\f5 \'cf}\par }";
            List<string> missing;
            var doc = RtfRuns.Read(word, out missing);
            Check(doc.Text == "Heute ist ein schöner Tag! Satz\nGroß\u041F" && missing.Count == 0, "rtf runs: the text '" + doc.Text.Replace("\n", "/") + "'");
            Func<string, TextRun> at = w => doc.Runs.First(x => x.Text.Contains(w));
            Check(at("Heute").Underline == true && at("Heute").Font == "Segoe UI" && at("Heute").Size == 12 && at("Heute").Bold == false,
                  "rtf runs: underline, theme font, size in half points");
            Check(at("ist").Bold == true && at("Tag").Italic == true && at("Satz").Font == "Arial Black" && at("schöner").Bold == false,
                  "rtf runs: bold, italic, another font, back to the paragraph's format");
            Check(at("Groß").Size == 16 && doc.Align == TextAlign.Center, "rtf runs: second paragraph's size, both centred");
            var t = RtfRuns.Object(doc);
            Check(t.Kind == TextKind.Frame && t.Text == doc.Text && t.Font == "Segoe UI" && t.Size == 12 && t.Align == TextAlign.Center && t.FormatRuns != null && t.W > 0 && t.H > 0,
                  "rtf runs: one text frame with runs, its own format the most frequent");
            var one = RtfRuns.Object(RtfRuns.Read(@"{\rtf1{\fonttbl{\f0 Arial;}}\f0\fs28 Nur Text\par}", out missing));
            Check(one.Kind == TextKind.Line && one.Runs == null && one.Font == "Arial" && one.Size == 14 && one.Text == "Nur Text", "rtf runs: one format on one line is a single-line text");
            var oneMixed = RtfRuns.Object(RtfRuns.Read(@"{\rtf1{\fonttbl{\f0 Arial;}}\f0\fs28 Schrauben {\b M4}\par}", out missing));
            Check(oneMixed.Kind == TextKind.Line && oneMixed.Text == "Schrauben M4" && oneMixed.FormatRuns != null && oneMixed.FullRuns().Last().Bold == true && oneMixed.W > 0,
                  "rtf runs: words in another format on one line: an artistic text with its runs");
            var mixedAlign = RtfRuns.Read(@"{\rtf1 \qc A\par\pard\qr B\par}", out missing);
            Check(mixedAlign.Align == TextAlign.Left, "rtf runs: mixed alignment is left");
            var justified = RtfRuns.Read(@"{\rtf1 \qj Ein Absatz im Blocksatz\par\qj Zweiter\par}", out missing);
            Check(justified.Align == TextAlign.Justify, "rtf runs: \\qj is justify");
            var gone = RtfRuns.Read(@"{\rtf1{\fonttbl{\f0 QL No Such Font 123;}}\f0 X}", out missing);
            Check(missing.Count == 1 && missing[0] == "QL No Such Font 123" && gone.Runs[0].Font == RtfRuns.DefaultFont, "rtf runs: missing font noted");
            Check(RtfRuns.Read(@"{\rtf1 \ulc1 a\ul b\ul0 c\ulnone}", out missing).Runs.Count(x => x.Underline == true) == 1, "rtf runs: \\ulc is a colour, \\ul0 ends");
        }

        // The editor's side (EditorForm.Clipboard.cs, ImageObject): our own format, an EMF picture kept as a vector, the
        // copied DIB, pasted text.
        // Copy out as EMF (EditorRender.RenderEmf): our own EMF reading takes it back, text stays text, the size is the box.
        static void CopyEmf()
        {
            var layout = LabelLayout.Estimate(new LabelChoice { Name = "Test 62", Media = MediaTable.ById(259), LengthMm = 50, FeedMm = 3, IsFormat = true }, false, false);
            var text = new TextObject { Text = "Schrauben M4", Font = "Arial", Size = 14, X = 2, Y = 2 };
            EditorRender.FitBox(text);
            var these = new List<LabelObject>
            {
                text,
                new ShapeObject { X = 2, Y = 12, W = 20, H = 8 },
                new LineObject { X = 2, Y = 24, W = 30, H = 0.1, Thickness = 0.5 },
                new BarcodeObject { Kind = Symbology.Qr, X = 30, Y = 2, W = 15, H = 15, Data = "QL" },
            };
            var box = new RectangleF(1.5f, 1.5f, 44, 23);
            var emf = EditorRender.RenderEmf(these, layout, box, DateTime.Now);
            Check(emf != null && EmfPicture.Check(emf) == emf, "copy emf: a valid EMF");
            var f = EmfPicture.What(emf);
            Check(f.Kind == EmfPicture.Content.Vector && f.HasText, "copy emf: vectors with text records (" + f.Kind + ")");
            var size = EmfPicture.SizeMm(emf);
            Check(Math.Abs(size.Width - box.Width) < 0.5 && Math.Abs(size.Height - box.Height) < 0.5, "copy emf: the box's size " + size);
            // drawn by GDI: ink where the text and the QR code are
            Check(InkFrom(emf, 0) > 500 && InkFrom(emf, 30) > 200, "copy emf: GDI draws it (" + InkFrom(emf, 0) + " dark pixels)");
            Check(EditorRender.RenderEmf(new List<LabelObject>(), layout, box, DateTime.Now) == null, "copy emf: nothing selected, no EMF");
        }

        static void Editor()
        {
            var emf = MakeEmf(dc => Rectangle(dc, 10, 10, 40, 30));
            var pic = ImageObject.FromEmf(emf);
            Check(pic.Emf == emf && pic.Svg == null && pic.Picture != null && pic.W > 0 && pic.H > 0, "editor: EMF picture with a preview");
            var text = new TextObject { Text = "Abc", X = 3, Y = 4 };
            var back = EditorForm.ObjectsFrom(EditorForm.ObjectsJson(new LabelObject[] { text, pic }));
            Check(back.Count == 2 && back[0] is TextObject && ((TextObject)back[0]).Text == "Abc" && back[0].X == 3 && back[0].Y == 4,
                  "own format: text back");
            var picBack = back.Count == 2 ? back[1] as ImageObject : null;
            Check(picBack != null && picBack.Emf != null && picBack.Emf.Length == emf.Length && picBack.Png != null, "own format: the EMF kept");
            Check(EditorForm.ObjectsFrom("no json").Count == 0 && EditorForm.ObjectsFrom("{\"format\":\"QL label\",\"objects\":[{\"type\":\"text\"}]}").Count == 0 &&
                  EditorForm.ObjectsFrom("{\"format\":\"QL label objects\",\"objects\":[{\"type\":\"future\"},{\"type\":\"text\",\"text\":\"x\"}]}").Count == 1,
                  "own format: foreign, damaged and unknown objects left out");
            string damaged = EditorForm.ObjectsJson(new LabelObject[] { pic }).Replace(Convert.ToBase64String(emf), Convert.ToBase64String(new byte[100]));
            var d = EditorForm.ObjectsFrom(damaged);
            Check(d.Count == 1 && ((ImageObject)d[0]).Emf == null && ((ImageObject)d[0]).Png != null, "own format: a damaged EMF is drawn from its preview");

            string file = Path.Combine(Path.GetTempPath(), "clipboard_test_" + Guid.NewGuid().ToString("N") + ".emf");
            try
            {
                File.WriteAllBytes(file, emf);
                var fromFile = ImageObject.FromFile(file);
                Check(fromFile.Emf != null && fromFile.Emf.Length == emf.Length, "editor: an .emf file stays a vector");
            }
            finally { File.Delete(file); }

            using (var b = new Bitmap(3, 2, PixelFormat.Format32bppArgb))
            {
                for (int y = 0; y < 2; y++) for (int x = 0; x < 3; x++) b.SetPixel(x, y, y == 0 ? Color.Red : Color.Blue);
                using (var round = EditorClipboard.Dib(EditorForm.Dib24(b)))
                    Check(round != null && round.GetPixel(0, 0).ToArgb() == Color.Red.ToArgb() && round.GetPixel(2, 1).ToArgb() == Color.Blue.ToArgb(), "copy: DIB round trip");
            }

            var line = EditorForm.TextFrom("One line\r\n");
            Check(line.Kind == TextKind.Line && line.Text == "One line" && line.W > 0, "paste: one line of text");
            var frame = EditorForm.TextFrom("Line 1\nLine 2");
            Check(frame.Kind == TextKind.Frame && frame.Text == "Line 1\nLine 2" && frame.H > 0, "paste: lines make a text frame");
        }

        // ---------- paste 1:1: the size a picture was copied or saved at ----------

        static bool Near(SizeF? s, double w, double h) { return s != null && Math.Abs(s.Value.Width - w) < 0.05 && Math.Abs(s.Value.Height - h) < 0.05; }
        static string Mm(SizeF? s) { return s == null ? "none" : s.Value.Width.ToString("0.00") + " x " + s.Value.Height.ToString("0.00") + " mm"; }

        static byte[] PngAt(int w, int h, float dpi)
        {
            using (var b = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var ms = new MemoryStream())
            {
                b.SetResolution(dpi, dpi);
                using (var g = Graphics.FromImage(b)) g.Clear(Color.Black);
                b.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static void OwnSize()
        {
            // a label copied in CorelDRAW: its frame 60.52 x 15.87 mm is the size it was copied at
            var corel = MakeEmf(dc => Rectangle(dc, 10, 10, 400, 100), 6052, 1587);
            var c = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, corel));
            Check(c.Kind == ClipKind.Emf && Near(c.SizeMm, 60.52, 15.87), "1:1: EMF at its frame (" + Mm(c.SizeMm) + ")");

            Check(Near(EditorClipboard.Read(new Source().Add("image/svg+xml", VectorSvgText)).SizeMm, 20, 10), "1:1: SVG in mm");
            Check(Near(EditorClipboard.Read(new Source().Add("image/svg+xml", new MemoryStream(Encoding.UTF8.GetBytes("\r\n " + VectorSvgText + "\0")))).SizeMm, 20, 10),
                  "1:1: SVG with white space before and a zero after");
            var units = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1in\" height=\"36pt\"><rect width=\"9\" height=\"9\"/></svg>";
            Check(Near(EditorClipboard.Read(new Source().Add("image/svg+xml", units)).SizeMm, 25.4, 12.7), "1:1: SVG in in and pt");
            var px = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"96px\" height=\"48\"><rect width=\"9\" height=\"9\"/></svg>";
            Check(Near(EditorClipboard.Read(new Source().Add("image/svg+xml", px)).SizeMm, 25.4, 12.7), "1:1: SVG in px or without a unit at 96 per inch");
            var box = "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 192 96\"><rect width=\"9\" height=\"9\"/></svg>";
            Check(Near(EditorClipboard.Read(new Source().Add("image/svg+xml", box)).SizeMm, 50.8, 25.4), "1:1: SVG with a viewBox only");

            var png = EditorClipboard.Read(new Source().Add(EditorClipboard.PngFormat, PngAt(300, 150, 300)));
            Check(png.Kind == ClipKind.Picture && Near(png.SizeMm, 25.4, 12.7), "1:1: PNG at its 300 dpi (" + Mm(png.SizeMm) + ")");
            // Word's photo: an EMF that only wraps the picture gives the size Word showed it at, the PNG the pixels
            var wrapped = MakeEmf(dc => Bitmap(dc, 0));
            var word = EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, wrapped).Add(EditorClipboard.PngFormat, PngAt(300, 150, 150)));
            Check(word.Kind == ClipKind.Picture && Near(word.SizeMm, 30, 20), "1:1: a picture at the size of the EMF that only wraps it (" + Mm(word.SizeMm) + ")");
            var dib = Dib24(96, 48, (x, y) => Color.Black);
            var plain = EditorClipboard.Read(new Source().Add(DataFormats.Dib, dib));
            Check(plain.Kind == ClipKind.Picture && Near(plain.SizeMm, 25.4, 12.7), "1:1: DIB without a resolution at 96 dpi (" + Mm(plain.SizeMm) + ")");
            BitConverter.GetBytes(11811).CopyTo(dib, 24); BitConverter.GetBytes(11811).CopyTo(dib, 28);   // 300 dpi
            Check(Near(EditorClipboard.Read(new Source().Add(DataFormats.Dib, dib)).SizeMm, 8.13, 4.06), "1:1: DIB at its 300 dpi");
            BitConverter.GetBytes(1).CopyTo(dib, 24);
            Check(Near(EditorClipboard.Read(new Source().Add(DataFormats.Dib, dib)).SizeMm, 25.4, 12.7), "1:1: an unlikely resolution counts as 96 dpi");

            // dropped files
            string dir = Path.Combine(Path.GetTempPath(), "clipboard_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "a.emf"), corel);
                File.WriteAllBytes(Path.Combine(dir, "b.png"), PngAt(600, 300, 600));
                File.WriteAllText(Path.Combine(dir, "c.svg"), units);
                Check(Near(ImageObject.FromFile(Path.Combine(dir, "a.emf")).OwnSizeMm, 60.52, 15.87), "1:1: an .emf file at its frame");
                Check(Near(ImageObject.FromFile(Path.Combine(dir, "b.png")).OwnSizeMm, 25.4, 12.7), "1:1: a .png file at its 600 dpi");
                Check(Near(ImageObject.FromFile(Path.Combine(dir, "c.svg")).OwnSizeMm, 25.4, 12.7), "1:1: an .svg file at its width and height");
            }
            finally { Directory.Delete(dir, true); }
        }

        // ---------- text from EMF (EmfText.cs) ----------

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
                                                                                          uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern uint SetTextAlign(IntPtr dc, uint align);
        [DllImport("gdi32.dll")] static extern int SetMapMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern int SetGraphicsMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetWorldTransform(IntPtr dc, ref XFORM x);
        [StructLayout(LayoutKind.Sequential)] struct XFORM { public float M11, M12, M21, M22, Dx, Dy; }
        [DllImport("gdi32.dll")] static extern int SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SetEnhMetaFileBits(uint n, byte[] bits);
        [DllImport("gdi32.dll")] static extern bool PlayEnhMetaFile(IntPtr dc, IntPtr emf, ref RECT r);

        // Dark pixels of the EMF played by GDI at 10 px per mm onto its frame, in the frame's right part (from mm x on).
        static int InkFrom(byte[] emf, float fromMm)
        {
            int w = (BitConverter.ToInt32(emf, 32) - BitConverter.ToInt32(emf, 24)) / 10, h = (BitConverter.ToInt32(emf, 36) - BitConverter.ToInt32(emf, 28)) / 10;
            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    IntPtr dc = g.GetHdc(), hm = SetEnhMetaFileBits((uint)emf.Length, emf);
                    var r = new RECT { L = 0, T = 0, R = w, B = h };
                    PlayEnhMetaFile(dc, hm, ref r);
                    DeleteEnhMetaFile(hm);
                    g.ReleaseHdc(dc);
                }
                int ink = 0;
                for (int y = 0; y < h; y++)
                    for (int x = (int)(fromMm * 10); x < w; x++)
                        if (bmp.GetPixel(x, y).GetBrightness() < 0.5f) ink++;
                return ink;
            }
        }
        [DllImport("gdi32.dll")] static extern bool RestoreDC(IntPtr dc, int n);
        [DllImport("gdi32.dll")] static extern bool SetWindowExtEx(IntPtr dc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] static extern bool SetViewportExtEx(IntPtr dc, int x, int y, IntPtr old);

        static void Text(IntPtr dc, string face, int height, int weight, int escapement, int x, int y, string text)
        {
            IntPtr f = CreateFontW(height, 0, escapement, escapement, weight, 0, 0, 0, 1, 0, 0, 0, 0, face);
            IntPtr old = SelectObject(dc, f);
            TextOutW(dc, x, y, text, text.Length);
            SelectObject(dc, old);
            DeleteObject(f);
        }

        static float PxPerMm(byte[] emf) { return BitConverter.ToInt32(emf, 72) / (float)BitConverter.ToInt32(emf, 80); }
        // down: the screen's pixels per mm may differ from across (szlDevice.cy / szlMillimeters.cy; 2026-11-08 4.30 vs 4.36)
        static float PxPerMmY(byte[] emf) { return BitConverter.ToInt32(emf, 76) / (float)BitConverter.ToInt32(emf, 84); }
        static double Pt(double mm) { return mm / 25.4 * 72; }

        static void TextFromEmf()
        {
            List<string> missing;
            // one line, baseline reference, Arial 40 px
            var one = MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -40, 400, 0, 100, 120, "Hallo"); });
            float k = PxPerMm(one), ky = PxPerMmY(one);
            // font sizes: the reader takes the mean of both directions (EmfText: the transform's mean scale) and rounds to
            // 0.5 pt; on a screen whose pixels per mm differ across and down, k alone was off by up to 0.6 pt
            float km = (float)Math.Sqrt(k * ky);
            Check(EmfPicture.What(one).Kind == EmfPicture.Content.TextOnly, "emf text: text only");
            var p = EmfText.Read(one, out missing);
            var r = p.Count == 1 ? p[0].Lines[0].Runs[0] : null;
            Check(r != null && r.Text == "Hallo" && r.Font == "Arial" && Math.Abs(r.SizePt - Pt(40 / km)) <= 0.5 && missing.Count == 0, "emf text: one run, " + (r != null ? r.Font + " " + r.SizePt + " pt" : "none"));
            Check(r != null && Math.Abs(r.Start.X - 100 / k) < 0.1 && Math.Abs(r.Start.Y - 120 / ky) < 0.1 && r.Width > 0 && Math.Abs(r.Angle) < 0.1, "emf text: baseline start " + (r != null ? r.Start.ToString() : ""));
            var objs = EmfText.Objects(p[0]);
            Check(objs.Count == 1 && objs[0].Kind == TextKind.Line && objs[0].Runs == null && objs[0].Text == "Hallo" && objs[0].W > 0, "emf text: one line in one format is a single-line text without runs");

            // top-aligned reference: the same baseline lower by the ascent
            var top = MakeEmf(dc => { SetTextAlign(dc, 0); Text(dc, "Arial", -40, 400, 0, 100, 120, "Hallo"); });
            var rt = EmfText.Read(top, out missing)[0].Lines[0].Runs[0];
            Check(rt.Start.Y > 120 / ky + 0.5 * 40 / ky && rt.Start.Y < 120 / ky + 40 / ky, "emf text: TA_TOP baseline " + rt.Start.Y);

            // a paragraph: two lines, left aligned, a line pitch apart
            var para = MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -30, 400, 0, 50, 100, "Zeile 1"); Text(dc, "Arial", -30, 400, 0, 50, 136, "Zweite Zeile"); });
            p = EmfText.Read(para, out missing);
            Check(p.Count == 1 && p[0].Lines.Count == 2 && p[0].Align == TextAlign.Left, "emf text: two lines one paragraph (" + p.Count + ")");
            objs = EmfText.Objects(p[0]);
            Check(objs.Count == 1 && objs[0].Kind == TextKind.Frame && objs[0].Text == "Zeile 1\nZweite Zeile", "emf text: a text frame");
            // far apart: two paragraphs
            var apart = MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -30, 400, 0, 50, 100, "Oben"); Text(dc, "Arial", -30, 400, 0, 50, 300, "Unten"); });
            Check(EmfText.Read(apart, out missing).Count == 2, "emf text: far apart lines stay apart");
            // centred lines
            var centred = MakeEmf(dc => { SetTextAlign(dc, 24 | 6); Text(dc, "Arial", -30, 400, 0, 200, 100, "Kurz"); Text(dc, "Arial", -30, 400, 0, 200, 136, "Eine lange Zeile"); });
            p = EmfText.Read(centred, out missing);
            Check(p.Count == 1 && p[0].Align == TextAlign.Center, "emf text: centred paragraph");

            // words written one by one (as CorelDRAW does) join, a bold word stays its own run
            var words = MakeEmf(dc =>
            {
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -30, 400, 0, 50, 100, "Schrauben");
                Text(dc, "Arial", -30, 700, 0, 50 + 160, 100, "M4");
            });
            p = EmfText.Read(words, out missing);
            Check(p.Count == 1 && p[0].Lines.Count == 1 && p[0].Lines[0].Runs.Count == 2 && p[0].Lines[0].Runs[1].Bold && p[0].Lines[0].Text == "Schrauben M4",
                  "emf text: two formats on one line '" + (p.Count > 0 ? p[0].Lines[0].Text : "") + "'");
            var mixed = EmfText.Objects(p[0]);
            Check(mixed.Count == 1 && mixed[0].Text == "Schrauben M4" && !mixed[0].Bold && mixed[0].FormatRuns != null && mixed[0].FormatRuns.Count == 2 && mixed[0].FormatRuns[1].Bold == true,
                  "emf text: one object, the bold word a run");
            Check(mixed[0].Kind == TextKind.Line && mixed[0].W > 0, "emf text: words in another format on one line: an artistic text");

            // turned: escapement 90 degrees counter-clockwise = -90 clockwise
            var up = MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -30, 400, 900, 100, 200, "Hoch"); });
            var ru = EmfText.Read(up, out missing)[0].Lines[0].Runs[0];
            Check(Math.Abs(ru.Angle + 90) < 0.5, "emf text: escapement 90 -> " + ru.Angle);
            var o = EmfText.Objects(EmfText.Read(up, out missing)[0])[0];
            Check(Math.Abs(o.Angle + 90) < 0.5, "emf text: the object turned " + o.Angle);

            // a font that isn't installed
            var gone = MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "QL No Such Font 123", -30, 400, 0, 50, 100, "Text"); });
            p = EmfText.Read(gone, out missing);
            Check(missing.Count == 1 && missing[0] == "QL No Such Font 123" && p[0].Lines[0].Runs[0].Font == EmfText.Substitute, "emf text: missing font noted");

            // map mode in 0.1 mm (y up): 50 units = 5 mm
            var metric = MakeEmf(dc => { SetMapMode(dc, 2); SetTextAlign(dc, 24); Text(dc, "Arial", -50, 400, 0, 100, -100, "Metrisch"); });
            var rm = EmfText.Read(metric, out missing)[0].Lines[0].Runs[0];
            Check(Math.Abs(rm.SizePt - Pt(5)) <= 0.5 && Math.Abs(rm.Start.X - 10) < 0.3 && Math.Abs(rm.Start.Y - 10) < 0.3 && Math.Abs(rm.Angle) < 0.1,
                  "emf text: LOMETRIC " + rm.SizePt + " pt at " + rm.Start);
            // a world transform that doubles everything
            var world = MakeEmf(dc =>
            {
                SetGraphicsMode(dc, 2);
                var x = new XFORM { M11 = 2, M22 = 2 };
                SetWorldTransform(dc, ref x);
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -20, 400, 0, 50, 60, "Doppelt");
            });
            var rw = EmfText.Read(world, out missing)[0].Lines[0].Runs[0];
            Check(Math.Abs(rw.SizePt - Pt(40 / km)) <= 0.5 && Math.Abs(rw.Start.X - 100 / k) < 0.1, "emf text: world transform " + rw.SizePt + " pt (" + Pt(40 / km).ToString("0.00") + " expected)");
            // and a rotation in the world transform (30 degrees clockwise as seen)
            var turned = MakeEmf(dc =>
            {
                SetGraphicsMode(dc, 2);
                float c = (float)Math.Cos(Math.PI / 6), s = (float)Math.Sin(Math.PI / 6);
                var x = new XFORM { M11 = c, M12 = s, M21 = -s, M22 = c, Dx = 100, Dy = 100 };
                SetWorldTransform(dc, ref x);
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -30, 400, 0, 0, 0, "Schräg");
            });
            var rr = EmfText.Read(turned, out missing)[0].Lines[0].Runs[0];
            Check(Math.Abs(rr.Angle - 30) < 0.5 && Math.Abs(rr.SizePt - Pt(30 / km)) <= 0.5, "emf text: turned by the world transform " + rr.Angle);

            // CorelDRAW's shape: a page of 0.01 mm units first, then one word in pixels (MM_TEXT) and one after a
            // 1:1 page that still has its size and place in 0.01 mm (lands far outside; read with the first page)
            var corel = MakeEmf(dc =>
            {
                SetMapMode(dc, 8);
                SetWindowExtEx(dc, 100000, 100000, IntPtr.Zero);
                SetViewportExtEx(dc, (int)Math.Round(1000 * k), (int)Math.Round(1000 * k), IntPtr.Zero);
                SaveDC(dc);
                SetMapMode(dc, 1);
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -(int)Math.Round(5 * k), 400, 0, 0, (int)Math.Round(10 * k), "3mm");
                SetGraphicsMode(dc, 2);
                SetMapMode(dc, 8);
                SetWindowExtEx(dc, 1, 1, IntPtr.Zero);
                SetViewportExtEx(dc, 1, 1, IntPtr.Zero);
                var x = new XFORM { M11 = 1, M22 = 1, Dx = 1200, Dy = 1000 };
                SetWorldTransform(dc, ref x);
                Text(dc, "Arial", -500, 700, 0, 0, 0, " - 10");
                RestoreDC(dc, -1);
            });
            p = EmfText.Read(corel, out missing);
            var runs = p.Count == 1 && p[0].Lines.Count == 1 ? p[0].Lines[0].Runs : new List<EmfRun>();
            Check(runs.Count == 2 && Math.Abs(runs[0].SizePt - Pt(5)) <= 0.5 && Math.Abs(runs[1].SizePt - Pt(5)) <= 0.5 && runs[1].Bold
                  && Math.Abs(runs[1].Start.X - 12) < 0.2 && Math.Abs(runs[1].Start.Y - 10) < 0.2,
                  "emf text: CorelDRAW's 1:1 page read with its first mapping (" + p.Count + " paragraph(s)" + (runs.Count == 2 ? ", " + runs[1].SizePt + " pt at " + runs[1].Start : "") + ")");

            // the same EMF as a picture: Windows draws " - 10" off the picture; repaired, it's on it (from 12 mm on)
            var repaired = EmfText.Repair(corel);
            Check(InkFrom(corel, 12) == 0 && InkFrom(repaired, 12) > 100 && InkFrom(repaired, 0) > InkFrom(repaired, 12), "emf repair: CorelDRAW's text drawn on the picture ("
                  + InkFrom(corel, 12) + " -> " + InkFrom(repaired, 12) + " dark pixels)");
            Check(EmfPicture.Check(repaired) == repaired && EmfText.Read(repaired, out missing)[0].Lines[0].Runs.Count == 2, "emf repair: still a valid EMF with the same text");
            Check(ImageObject.FromEmf(corel).Emf.Length == repaired.Length, "emf repair: done when a picture is pasted");
            Check(EmfText.Repair(one) == one && EmfText.Repair(world) == world, "emf repair: nothing to do keeps the same bytes");

            Check(Throws(() => EmfText.Read(new byte[50], out missing)), "emf text: damaged");
        }
    }
}
