// EMF pictures (Windows' enhanced metafile; CorelDRAW's only vector format on the clipboard, also Word's): kept as a
// vector, like SVG, and drawn at the size printed (Graphics.DrawImage of the metafile, never played into anything
// else). Check() reads the records first: a valid header, sizes inside the data, at most 20 MB. What() tells the kinds
// of drawing in it, so a paste can skip an EMF that only wraps one bitmap (Word's pictures: the PNG on the clipboard is
// the same picture) and notice text (CorelDRAW's text stays text records with their font).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace QlLabels
{
    static class EmfPicture
    {
        public const int MaxBytes = 20 * 1024 * 1024;
        const int EMR_HEADER = 1, EMR_EOF = 14, EMR_GDICOMMENT = 70, Signature = 0x464D4520;   // " EMF"
        const int EmfPlusSignature = 0x2B464D45;   // "EMF+" in a GDI comment

        public enum Content { Empty, Vector, OneBitmap, TextOnly }

        public sealed class Facts
        {
            public Content Kind;
            public bool HasText;        // text records (ExtTextOut, PolyTextOut)
            public bool HasVector;      // lines, curves, shapes, fills, paths
            public int Bitmaps;         // places drawn with a bitmap (StretchDIBits, BitBlt...; a mask and its picture at the
                                        // same place count once)
            public bool EmfPlus;        // GDI+ records in comments (drawn by DrawImage like the rest)
            public RectangleF FrameMm;  // the picture's frame, mm
        }

        // Records that only set state (map mode, transforms, colours, objects, clipping, comments): neither drawing nor text.
        // (MS-EMF record types; path brackets count as setup, the path's fill or stroke as drawing.)
        static readonly int[] Setup = {
            1, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
            38, 39, 40, 48, 49, 50, 51, 52, 57, 58, 59, 60, 61, 65, 66, 67, 68, 70, 75, 82, 93, 94, 95, 98, 99, 100, 101, 102,
            103, 104, 109, 111, 112, 113, 115, 119, 120, 121, 122 };
        static readonly int[] BitmapRecords = { 76, 77, 78, 79, 80, 81, 114, 116 };
        static readonly int[] TextRecords = { 83, 84, 96, 97, 108 };
        // BitBlt, StretchBlt, MaskBlt, AlphaBlend, TransparentBlt: cbBmiSrc at offset 88 (0 = no source bitmap)
        static readonly int[] SourceBlt = { 76, 77, 78, 114, 116 };
        const int RopKeep = 0x00AA0029;   // D: the target stays as it is

        // Where a bitmap record draws (its destination), as a key: records at the same place are one picture.
        static string Place(byte[] emf, int pos, int type, int size)
        {
            if (type == 81 && size >= 80)   // StretchDIBits: xDest, yDest at 24, cxDest, cyDest at 72
                return Int(emf, pos + 24) + "," + Int(emf, pos + 28) + "," + Int(emf, pos + 72) + "," + Int(emf, pos + 76);
            if (type == 80 && size >= 48)   // SetDIBitsToDevice: xDest, yDest at 24, cxSrc, cySrc at 40
                return Int(emf, pos + 24) + "," + Int(emf, pos + 28) + "," + Int(emf, pos + 40) + "," + Int(emf, pos + 44);
            if (type == 79 && size >= 48)   // PlgBlt: three points at 24
                return string.Join(",", Enumerable.Range(0, 6).Select(i => Int(emf, pos + 24 + 4 * i)));
            if (size >= 40)                 // the others: xDest, yDest, cxDest, cyDest at 24
                return Int(emf, pos + 24) + "," + Int(emf, pos + 28) + "," + Int(emf, pos + 32) + "," + Int(emf, pos + 36);
            return "record at " + pos;
        }

        // The checked EMF (InvalidDataException: no EMF, damaged or too large).
        public static byte[] Check(byte[] emf)
        {
            Read(emf);
            return emf;
        }

        public static Facts What(byte[] emf) { return Read(emf); }

        static Facts Read(byte[] emf)
        {
            if (emf == null || emf.Length < 88) throw Damaged();
            if (emf.Length > MaxBytes) throw new InvalidDataException(Lang.T("The EMF picture is too large."));
            if (Int(emf, 0) != EMR_HEADER || Int(emf, 40) != Signature) throw Damaged();
            int total = Int(emf, 48);
            if (total < 88 || total > emf.Length) throw Damaged();
            var f = new Facts();
            // rclFrame: 0.01 mm
            int l = Int(emf, 24), t = Int(emf, 28), r = Int(emf, 32), b = Int(emf, 36);
            f.FrameMm = RectangleF.FromLTRB(l / 100f, t / 100f, r / 100f, b / 100f);
            int pos = 0;
            bool end = false;
            var places = new HashSet<string>();
            while (pos + 8 <= total)
            {
                int type = Int(emf, pos), size = Int(emf, pos + 4);
                if (size < 8 || size % 4 != 0 || size > total - pos) throw Damaged();   // (no overflow with a damaged size)
                if (type == EMR_EOF) { end = true; break; }
                if (type == EMR_GDICOMMENT && size >= 16 && Int(emf, pos + 12) == EmfPlusSignature) f.EmfPlus = true;
                if (Array.IndexOf(BitmapRecords, type) >= 0)
                {
                    // Word draws a photo with transparency as a 1-bit mask and the picture at the same place (two
                    // StretchDIBits), plus a BitBlt that leaves the target as it is: still one picture.
                    bool noSource = SourceBlt.Contains(type) && size >= 92 && Int(emf, pos + 88) == 0;
                    if (noSource) { if (Int(emf, pos + 40) != RopKeep) f.HasVector = true; }   // a pattern fill draws
                    else if (places.Add(Place(emf, pos, type, size))) f.Bitmaps++;
                }
                else if (Array.IndexOf(TextRecords, type) >= 0) f.HasText = true;
                else if (Array.IndexOf(Setup, type) < 0) f.HasVector = true;
                pos += size;
            }
            if (!end) throw Damaged();
            f.Kind = f.HasVector || f.Bitmaps > 1 || (f.Bitmaps == 1 && f.HasText) ? Content.Vector
                   : f.Bitmaps == 1 ? Content.OneBitmap
                   : f.HasText ? Content.TextOnly
                   : f.EmfPlus ? Content.Vector   // GDI+ only: its drawing is in the comments
                   : Content.Empty;
            return f;
        }

        static InvalidDataException Damaged() { return new InvalidDataException(Lang.T("This is no EMF picture, or it is damaged.")); }

        static int Int(byte[] d, int at) { return at >= 0 && at <= d.Length - 4 ? BitConverter.ToInt32(d, at) : 0; }

        // The picture's size in mm (its frame; a frame of nothing: 50 x 50 mm).
        public static SizeF SizeMm(byte[] emf)
        {
            return FrameSizeMm(emf) ?? new SizeF(50, 50);
        }

        // Its own size in mm, the size it was copied at (rclFrame; CorelDRAW's reference device is wrong, its frame
        // right); null for a frame of nothing.
        public static SizeF? FrameSizeMm(byte[] emf)
        {
            var r = Read(emf).FrameMm;
            return r.Width > 0.1f && r.Height > 0.1f ? r.Size : (SizeF?)null;
        }

        // The picture stretched to w x h pixels on a transparent background (null: it couldn't be drawn). Any thread.
        // crisp: for printer dots (Dither/Threshold) - its text without font smoothing, as a GDI print draws it (smoothed
        // edges are greys that Dither scatters into dots: CorelDRAW's white text on white came out as a dotted patch).
        public static Bitmap Render(byte[] emf, int w, int h, bool crisp = false)
        {
            if (emf == null || w < 1 || h < 1) return null;
            if (crisp) emf = UnsmoothedFonts(emf);
            Bitmap bmp = null;
            try
            {
                bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
                using (var ms = new MemoryStream(emf))
                using (var mf = new Metafile(ms))
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(mf, new Rectangle(0, 0, w, h));
                }
                return bmp;
            }
            catch (Exception)   // also out of memory: the caller draws the preview
            {
                if (bmp != null) bmp.Dispose();
                return null;
            }
        }

        // A copy with every font made non-antialiased: EMR_EXTCREATEFONTINDIRECTW's LOGFONTW (after the record's type,
        // size and font index) gets lfQuality = NONANTIALIASED_QUALITY. The same bytes when it has no fonts.
        static byte[] UnsmoothedFonts(byte[] emf)
        {
            byte[] d = null;
            int total = Int(emf, 48);
            for (int pos = Int(emf, 4); pos + 8 <= total && pos + 8 <= emf.Length;)
            {
                int type = Int(emf, pos), size = Int(emf, pos + 4);
                if (size < 8) break;
                if (type == EMR_EXTCREATEFONTINDIRECTW && size >= 12 + 28 && pos + 12 + 26 < emf.Length)
                {
                    if (d == null) d = (byte[])emf.Clone();
                    d[pos + 12 + 26] = NONANTIALIASED_QUALITY;
                }
                if (type == EMR_EOF) break;
                pos += size;
            }
            return d ?? emf;
        }

        const int EMR_EXTCREATEFONTINDIRECTW = 82;
        const byte NONANTIALIASED_QUALITY = 3;

        // The bytes of an EMF handle (a clipboard's CF_ENHMETAFILE, a Metafile's GetHenhmetafile); the handle stays the
        // caller's. null: no handle, or too large.
        public static byte[] FromHandle(IntPtr hemf)
        {
            if (hemf == IntPtr.Zero) return null;
            uint n = GetEnhMetaFileBits(hemf, 0, null);
            if (n == 0 || n > MaxBytes) return null;
            var data = new byte[n];
            return GetEnhMetaFileBits(hemf, n, data) == n ? data : null;
        }

        [DllImport("gdi32.dll")] static extern uint GetEnhMetaFileBits(IntPtr hemf, uint size, byte[] data);
    }
}
