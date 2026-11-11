// An EMF with drawing and text (a whole label copied from CorelDRAW) split into layers in its own z-order: pictures
// (the drawing records only, every text record blanked out) and the places of its text records, so the texts can come in
// as text objects between them. Drawing that comes after a text and covers it (a shape on top of a text) goes into a
// picture above that text; everything else stays in one picture below all texts. Each picture is cut to its drawing
// (the EMF's frame set around it), so a picture above the texts covers only its own place, not the whole label.
// Which records overlap is found by drawing them, not from the records' bounds (CorelDRAW writes them in mixed units):
// each text record and each run of drawing records between two texts is drawn alone, small, and their painted pixels
// are compared. A record is "blanked" by turning it into a comment of the same size, so all other records and the
// file's sizes stay as they were.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace QlLabels
{
    sealed class EmfLayers
    {
        // A picture: its level, the EMF with the texts and the other levels' drawing blanked and its frame cut to its
        // drawing, and that place (mm on the original frame).
        public sealed class Picture
        {
            public int Level;
            public byte[] Emf;
            public RectangleF Mm;
        }

        // The pictures by level (even levels, lowest first).
        public readonly List<Picture> Pictures = new List<Picture>();
        // The text records' painted places (mm on the frame) by level (odd levels).
        public readonly List<KeyValuePair<int, RectangleF>> Texts = new List<KeyValuePair<int, RectangleF>>();

        const int MaxItems = 300, MaxSide = 600, BudgetMs = 3000;   // the first call loads fonts and GDI+ (0.6 s)
        const float PxPerMm = 4;
        const int EMR_COMMENT = 70, EMR_BEGINPATH = 59, EMR_ENDPATH = 60, EMR_ABORTPATH = 68, EMR_EOF = 14;
        static readonly int[] TextRecords = { 83, 84, 96, 97, 108 };
        // records that only set state (EmfPicture's list; inside a path bracket every record only builds the path)
        static readonly int[] Setup = {
            1, 9, 10, 11, 12, 13, 14, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37,
            38, 39, 40, 48, 49, 50, 51, 52, 57, 58, 59, 60, 61, 65, 66, 67, 68, 70, 75, 82, 93, 94, 95, 98, 99, 100, 101, 102,
            103, 104, 109, 111, 112, 113, 115, 119, 120, 121, 122 };

        sealed class Item
        {
            public bool Text;
            public readonly List<int> Records = new List<int>();   // record offsets
            public Mask Ink;
            public int Level;
        }

        sealed class Mask
        {
            public int W, H, X0, Y0, X1, Y1;   // painted box (inclusive); X1 < X0: nothing painted
            public bool[] On;
            public bool Empty { get { return X1 < X0; } }
            public bool At(int x, int y) { return x >= 0 && y >= 0 && x < W && y < H && On[y * W + x]; }
        }

        // The layers (null: no text and drawing both, too many parts, too slow, or not drawable: paste it as one picture).
        // emf: the checked EMF (EmfPicture.Check); CorelDRAW's misplaced text is repaired first (EmfText.Repair).
        public static EmfLayers Split(byte[] emf)
        {
            var sw = Stopwatch.StartNew();
            try { emf = EmfText.Repair(emf); }
            catch (InvalidDataException) { }
            var size = EmfPicture.FrameSizeMm(emf);
            if (!size.HasValue) return null;
            var items = Items(emf);
            if (items == null || items.Count > MaxItems || !items.Any(i => i.Text) || !items.Any(i => !i.Text)) return null;

            float k = Math.Min(PxPerMm, MaxSide / Math.Max(size.Value.Width, size.Value.Height));
            int w = Math.Max(1, (int)Math.Ceiling(size.Value.Width * k)), h = Math.Max(1, (int)Math.Ceiling(size.Value.Height * k));
            var all = items.SelectMany(i => i.Records).ToList();
            foreach (var it in items)
            {
                if (sw.ElapsedMilliseconds > BudgetMs) return null;
                var only = Blank(emf, all.Except(it.Records));
                it.Ink = Draw(only, w, h);
                if (it.Ink == null) return null;
                if (it.Text) it.Ink = Grow(it.Ink);   // a text's place: one pixel more around its letters
            }
            // levels: drawing even, text odd; each at least one above (or, the same kind, level with) every earlier part
            // it overlaps
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                int level = it.Text ? 1 : 0;
                for (int j = 0; j < i; j++)
                    if (Overlap(items[j].Ink, it.Ink)) level = Math.Max(level, items[j].Level + (items[j].Text == it.Text ? 0 : 1));
                it.Level = level;
                if (sw.ElapsedMilliseconds > BudgetMs) return null;
            }

            var result = new EmfLayers();
            var texts = items.Where(i => i.Text).SelectMany(i => i.Records).ToList();
            foreach (var level in items.Where(i => !i.Text).Select(i => i.Level).Distinct().OrderBy(l => l))
            {
                var parts = items.Where(i => !i.Text && i.Level == level).ToList();
                var own = Blank(emf, all.Except(parts.SelectMany(i => i.Records)));
                var ink = parts.Select(i => i.Ink).Where(m => !m.Empty).ToList();
                if (ink.Count == 0) continue;   // draws nothing that shows
                // one pixel more around (antialiased edges), within the frame
                int x0 = Math.Max(0, ink.Min(m => m.X0) - 1), y0 = Math.Max(0, ink.Min(m => m.Y0) - 1);
                int x1 = Math.Min(w, ink.Max(m => m.X1) + 2), y1 = Math.Min(h, ink.Max(m => m.Y1) + 2);
                var mm = RectangleF.FromLTRB(x0 / k, y0 / k, Math.Min(size.Value.Width, x1 / k), Math.Min(size.Value.Height, y1 / k));
                result.Pictures.Add(new Picture { Level = level, Emf = Cut(own, mm, size.Value), Mm = mm });
            }
            if (result.Pictures.Count == 0) return null;
            foreach (var it in items.Where(i => i.Text && !i.Ink.Empty))
                result.Texts.Add(new KeyValuePair<int, RectangleF>(it.Level,
                    RectangleF.FromLTRB(it.Ink.X0 / k, it.Ink.Y0 / k, (it.Ink.X1 + 1) / k, (it.Ink.Y1 + 1) / k)));
            return result;
        }

        // The level of a text object at this place (mm on the frame): the highest of the text records whose painted
        // middle is inside its box; 1 when none is.
        public int LevelOf(RectangleF box)
        {
            int level = 1;
            foreach (var t in Texts)
            {
                var c = new PointF(t.Value.X + t.Value.Width / 2, t.Value.Y + t.Value.Height / 2);
                if (box.Contains(c)) level = Math.Max(level, t.Key);
            }
            return level;
        }

        // ---------- records ----------

        // The EMF with its text records blanked (the same bytes when it can't be read).
        internal static byte[] WithoutText(byte[] emf)
        {
            var items = Items(emf);
            return items == null ? emf : Blank(emf, items.Where(i => i.Text).SelectMany(i => i.Records));
        }

        // Text records (one item each) and runs of drawing records between them, in order; null: damaged.
        static List<Item> Items(byte[] emf)
        {
            var items = new List<Item>();
            int total = Int(emf, 48);
            if (total < 88 || total > emf.Length) return null;
            bool inPath = false;
            Item run = null;
            for (int pos = 0, n = 0; pos + 8 <= total; n++)
            {
                int type = Int(emf, pos), size = Int(emf, pos + 4);
                if (size < 8 || size % 4 != 0 || size > total - pos || n > 1000000) return null;
                if (type == EMR_EOF) break;
                if (type == EMR_BEGINPATH) inPath = true;
                else if (type == EMR_ENDPATH || type == EMR_ABORTPATH) inPath = false;
                bool text = Array.IndexOf(TextRecords, type) >= 0;
                bool draws = !text && !inPath && Array.IndexOf(Setup, type) < 0;
                if ((text || draws) && size < 12) return null;   // can't be blanked
                if (text) { items.Add(new Item { Text = true }); items[items.Count - 1].Records.Add(pos); run = null; }
                else if (draws)
                {
                    if (run == null) { run = new Item(); items.Add(run); }
                    run.Records.Add(pos);
                }
                if (items.Count > MaxItems) return items;
                pos += size;
            }
            return items;
        }

        // Whether it draws anything besides text (paths count by their fill or stroke).
        internal static bool Draws(byte[] emf)
        {
            var items = Items(emf);
            return items == null || items.Any(i => !i.Text);
        }

        // A copy with these records turned into empty comments of the same size.
        internal static byte[] Blank(byte[] emf, IEnumerable<int> records)
        {
            var d = (byte[])emf.Clone();
            foreach (int pos in records)
            {
                int size = Int(d, pos + 4);
                BitConverter.GetBytes(EMR_COMMENT).CopyTo(d, pos);
                BitConverter.GetBytes(size - 12).CopyTo(d, pos + 8);   // cbData
                Array.Clear(d, pos + 12, size - 12);
            }
            return d;
        }

        // A copy whose frame (and bounds) is this part of the old one (mm from its top left): drawn alone, it shows only
        // that part, at its size.
        static byte[] Cut(byte[] emf, RectangleF mm, SizeF size)
        {
            var d = (byte[])emf.Clone();
            CutRect(d, 24, mm, size);   // rclFrame (0.01 mm)
            CutRect(d, 8, mm, size);    // rclBounds (device units)
            return d;
        }

        static void CutRect(byte[] d, int at, RectangleF mm, SizeF size)
        {
            int l = Int(d, at), t = Int(d, at + 4), r = Int(d, at + 8), b = Int(d, at + 12);
            double sx = (r - l) / (double)size.Width, sy = (b - t) / (double)size.Height;
            int nl = l + (int)Math.Floor(mm.Left * sx), nt = t + (int)Math.Floor(mm.Top * sy);
            int nr = l + (int)Math.Ceiling(mm.Right * sx), nb = t + (int)Math.Ceiling(mm.Bottom * sy);
            BitConverter.GetBytes(nl).CopyTo(d, at);
            BitConverter.GetBytes(nt).CopyTo(d, at + 4);
            BitConverter.GetBytes(Math.Max(nr, nl + 1)).CopyTo(d, at + 8);
            BitConverter.GetBytes(Math.Max(nb, nt + 1)).CopyTo(d, at + 12);
        }

        // ---------- pixels ----------

        static Mask Draw(byte[] emf, int w, int h)
        {
            using (var bmp = EmfPicture.Render(emf, w, h))
            {
                if (bmp == null) return null;
                var m = new Mask { W = w, H = h, On = new bool[w * h], X0 = w, Y0 = h, X1 = -1, Y1 = -1 };
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[w];
                    for (int y = 0; y < h; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                        for (int x = 0; x < w; x++)
                            if (((row[x] >> 24) & 0xFF) > 24)
                            {
                                m.On[y * w + x] = true;
                                if (x < m.X0) m.X0 = x;
                                if (x > m.X1) m.X1 = x;
                                if (y < m.Y0) m.Y0 = y;
                                if (y > m.Y1) m.Y1 = y;
                            }
                    }
                }
                finally { bmp.UnlockBits(data); }
                return m;
            }
        }

        static Mask Grow(Mask m)
        {
            if (m.Empty) return m;
            var g = new Mask { W = m.W, H = m.H, On = new bool[m.On.Length],
                               X0 = Math.Max(0, m.X0 - 1), Y0 = Math.Max(0, m.Y0 - 1), X1 = Math.Min(m.W - 1, m.X1 + 1), Y1 = Math.Min(m.H - 1, m.Y1 + 1) };
            for (int y = g.Y0; y <= g.Y1; y++)
                for (int x = g.X0; x <= g.X1; x++)
                    g.On[y * m.W + x] = m.At(x, y) || m.At(x - 1, y) || m.At(x + 1, y) || m.At(x, y - 1) || m.At(x, y + 1);
            return g;
        }

        static bool Overlap(Mask a, Mask b)
        {
            if (a.Empty || b.Empty) return false;
            int x0 = Math.Max(a.X0, b.X0), x1 = Math.Min(a.X1, b.X1), y0 = Math.Max(a.Y0, b.Y0), y1 = Math.Min(a.Y1, b.Y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                    if (a.On[y * a.W + x] && b.On[y * b.W + x]) return true;
            return false;
        }

        static int Int(byte[] d, int at) { return at >= 0 && at <= d.Length - 4 ? BitConverter.ToInt32(d, at) : 0; }
    }
}
