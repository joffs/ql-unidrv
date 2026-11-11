// CorelDRAW's own clipboard data, "Corel 32-bit Presentation Exchange Data": CMX (RIFF "CMX1"). Read for its texts only:
// the real text with paragraph marks (CorelDRAW's EMF has the laid-out lines only), whether it is paragraph text in a
// frame, the frame's width, the font and size, the line spacing, and the laid-out glyph runs (for the cross-check
// against the EMF). Units on the page: 0.1 micrometre.
// The page chunk is a list of instructions: uint16 size (header included) and int16 code, or the long form 0xFFFF,
// int32 size, int16 code. Most carry tags: a tag byte, uint16 size (header included), data; tag 0xFF ends the list.
// Known from CorelDRAW 2024 (one setting per sample copy, tools/corel-samples.ps1; unknown records and tags are skipped):
//   20 BeginTextStream .. 21 EndTextStream: one text object's logical text; tag 1 = uint16 kind (0 paragraph text in
//     a frame, 1 artistic text), ...
//     98 TextFrame: tag 1 = uint16, int32 frame width; tag 2 = int32 frame height (negative), int32, uint16, then the
//        6 doubles of a matrix; tag 3 = uint16 vertical alignment (0 top, 1 centre, 2 bottom). Artistic text has one
//        too, with the text's extent.
//     99 BeginParagraph: tag 2 = uint16 font (1-based in the rfnt chunk), int32 size (em); tag 4 = byte alignment
//        (0 none, 1 left, 2 centre, 3 right, 4 justified, 5 forced justified); tag 5 = int32 values in millionths:
//        [0] character spacing (200000 = 20 %), [2] word spacing (1000000 = 100 %), [3] line spacing (870000 = 87 %),
//        [4] space before the paragraph, [5] after it (% of the line, not read: Label Maker has none)
//     102 Characters: tag 3 (or 1) = int32 count, UTF-16 characters; 13 = a paragraph mark (artistic text); paragraph
//        text has one BeginParagraph .. EndParagraph per paragraph instead
//   70 BeginTextObject .. 71 EndTextObject: the same text laid out
//     65 DrawChars: tag 1 = uint16 count, UTF-16 characters, then per character int32 x, int32 y (y up)
// rfnt chunk: one tag list per font; tag 4 = int32 count + UTF-16 name, tag 1 = uint16 length + ANSI name.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace QlLabels
{
    sealed class CmxGlyphRun
    {
        public string Text = "";
        public PointF Start;   // the first character's position (mm, y up, in the text object's own coordinates)
    }

    sealed class CmxParagraph
    {
        public int Start, Length;             // in CmxText.Text
        public string Font = "";
        public double SizeMm;                 // 0: not known
        public int Align = -1;                // 0 none, 1 left, 2 centre, 3 right, 4/5 justified; -1: not known
        public double CharSpacingPct, WordSpacingPct = 100, LineSpacingPct;   // CorelDRAW's values; line 0: not known
        public double SpaceBeforePct = 100, SpaceAfterPct = 100;              // CorelDRAW's (100: no space added)
    }

    sealed class CmxText
    {
        public string Text = "";              // paragraph marks as "\n"
        public string Font = "";
        public double SizeMm;                 // em
        public double LineSpacingPct;         // 0: not known
        public bool Frame;                    // paragraph text in a frame (else artistic text)
        public bool Artistic;                 // artistic text (stream kind 1)
        public double FrameWidthMm;           // 0: not known
        public double FrameHeightMm;          // 0: not known
        public int VAlign;                    // 0 top, 1 centre, 2 bottom
        public readonly List<CmxParagraph> Paragraphs = new List<CmxParagraph>();
        public double[] Matrix;               // the frame's: m11 m12 m21 m22 dx dy; null: none
        public readonly List<CmxGlyphRun> Glyphs = new List<CmxGlyphRun>();

        public double SizePt { get { return Math.Round(SizeMm / 25.4 * 72, 2); } }
    }

    static class Cmx
    {
        const double MmPerUnit = 1e-4;
        const int MaxInstructions = 200000, MaxChars = 100000;

        // The texts in the CMX, in page order (InvalidDataException: not CMX or damaged where it matters).
        public static List<CmxText> Read(byte[] d)
        {
            if (d == null || d.Length < 12 || Ascii(d, 0, 4) != "RIFF" || Ascii(d, 8, 4) != "CMX1") throw Bad();
            int end = (int)Math.Min(d.Length, 8L + U32(d, 4));
            int page = -1, pageSize = 0, fontsAt = -1, fontsSize = 0;
            for (int at = 12; at + 8 <= end; )
            {
                string id = Ascii(d, at, 4);
                long n = U32(d, at + 4);
                if (n < 0 || at + 8 + n > end) break;
                if (id == "page" && page < 0) { page = at + 8; pageSize = (int)n; }
                if (id == "rfnt" && fontsAt < 0) { fontsAt = at + 8; fontsSize = (int)n; }
                at += 8 + (int)n + (int)(n & 1);
            }
            if (page < 0) throw Bad();
            var fonts = fontsAt >= 0 ? Fonts(d, fontsAt, fontsAt + fontsSize) : new List<string>();
            return Page(d, page, page + pageSize, fonts);
        }

        static List<CmxText> Page(byte[] d, int at, int end, List<string> fonts)
        {
            var texts = new List<CmxText>();
            CmxText stream = null, laidOut = null;
            int next = 0;   // the next text whose laid-out form follows
            for (int count = 0; at + 4 <= end && count < MaxInstructions; count++)
            {
                int size = U16(d, at), code = (short)U16(d, at + 2), head = 4;
                if (size == 0xFFFF)
                {
                    if (at + 8 > end) break;
                    long big = U32(d, at + 2);
                    if (big > int.MaxValue) break;
                    size = (int)big; code = (short)U16(d, at + 6); head = 8;
                }
                if (size < head || at + size > end) break;   // damaged: what was read so far stands
                var tags = Tags(d, at + head, at + size);
                switch (code)
                {
                    case 20:                                                                        // BeginTextStream
                        stream = new CmxText();
                        Tuple<int, int> kind;
                        if (tags.TryGetValue(1, out kind) && kind.Item2 - kind.Item1 >= 2) stream.Artistic = U16(d, kind.Item1) == 1;
                        break;
                    case 98: if (stream != null) Frame(d, tags, stream); break;                    // TextFrame
                    case 99: if (stream != null) Paragraph(d, tags, stream, fonts); break;         // BeginParagraph
                    case 102: if (stream != null) Characters(d, tags, stream); break;              // Characters
                    case 21:                                                                        // EndTextStream
                        if (stream != null)
                        {
                            stream.Text = stream.Text.TrimEnd('\n');
                            var ps = stream.Paragraphs;
                            for (int i = 0; i < ps.Count; i++)
                            {
                                ps[i].Start = Math.Min(ps[i].Start, stream.Text.Length);
                                int stop = i + 1 < ps.Count ? Math.Min(ps[i + 1].Start, stream.Text.Length) : stream.Text.Length;
                                ps[i].Length = Math.Max(0, stop - ps[i].Start);
                            }
                            if (stream.Text.Length > 0) texts.Add(stream);
                        }
                        stream = null;
                        break;
                    case 70: laidOut = next < texts.Count ? texts[next++] : null; break;           // BeginTextObject
                    case 71: laidOut = null; break;                                                 // EndTextObject
                    case 65: if (laidOut != null) Glyphs(d, tags, laidOut); break;                  // DrawChars
                }
                at += size;
            }
            return texts;
        }

        // ---------- the records ----------

        static void Frame(byte[] d, Dictionary<int, Tuple<int, int>> tags, CmxText t)
        {
            t.Frame = !t.Artistic;
            Tuple<int, int> g;
            if (tags.TryGetValue(1, out g) && g.Item2 - g.Item1 >= 6)
            {
                long w = I32(d, g.Item1 + 2);
                if (w > 0 && w < 100000000) t.FrameWidthMm = w * MmPerUnit;
            }
            if (tags.TryGetValue(2, out g) && g.Item2 - g.Item1 >= 4)
            {
                long h = -I32(d, g.Item1);
                if (h > 0 && h < 100000000) t.FrameHeightMm = h * MmPerUnit;
            }
            if (tags.TryGetValue(3, out g) && g.Item2 - g.Item1 >= 2)
            {
                int v = U16(d, g.Item1);
                if (v <= 2) t.VAlign = v;
            }
            if (tags.TryGetValue(2, out g) && g.Item2 - g.Item1 >= 10 + 48)
            {
                var m = new double[6];
                for (int i = 0; i < 6; i++) m[i] = BitConverter.ToDouble(d, g.Item1 + 10 + 8 * i);
                if (m.All(v => !double.IsNaN(v) && !double.IsInfinity(v))) t.Matrix = m;
            }
        }

        static void Paragraph(byte[] d, Dictionary<int, Tuple<int, int>> tags, CmxText t, List<string> fonts)
        {
            // paragraph text: each paragraph is its own record, the marks between them are not in the characters
            if (t.Text.Length > 0 && !t.Text.EndsWith("\n", StringComparison.Ordinal) && t.Text.Length < MaxChars) t.Text += "\n";
            if (t.Paragraphs.Count >= MaxChars) return;
            var p = new CmxParagraph { Start = t.Text.Length };
            Tuple<int, int> g;
            if (tags.TryGetValue(2, out g) && g.Item2 - g.Item1 >= 6)
            {
                int font = U16(d, g.Item1);
                long size = I32(d, g.Item1 + 2);
                if (font >= 1 && font <= fonts.Count) p.Font = fonts[font - 1];
                if (size > 0 && size < 50000000) p.SizeMm = size * MmPerUnit;
            }
            if (tags.TryGetValue(4, out g) && g.Item2 - g.Item1 >= 1 && d[g.Item1] <= 5) p.Align = d[g.Item1];
            if (tags.TryGetValue(5, out g) && g.Item2 - g.Item1 >= 16)
            {
                long c = I32(d, g.Item1), w = I32(d, g.Item1 + 8), l = I32(d, g.Item1 + 12);
                if (Math.Abs(c) <= 100000000) p.CharSpacingPct = c / 10000.0;
                if (w >= 0 && w <= 100000000) p.WordSpacingPct = w / 10000.0;
                if (l > 0 && l <= 100000000) p.LineSpacingPct = l / 10000.0;
                if (g.Item2 - g.Item1 >= 24)
                {
                    long b = I32(d, g.Item1 + 16), a = I32(d, g.Item1 + 20);
                    if (b >= 0 && b <= 100000000) p.SpaceBeforePct = b / 10000.0;
                    if (a >= 0 && a <= 100000000) p.SpaceAfterPct = a / 10000.0;
                }
            }
            t.Paragraphs.Add(p);
            // the text's own values: its first paragraph's
            if (t.Paragraphs.Count == 1) { t.Font = p.Font; t.SizeMm = p.SizeMm; t.LineSpacingPct = p.LineSpacingPct; }
        }

        static void Characters(byte[] d, Dictionary<int, Tuple<int, int>> tags, CmxText t)
        {
            Tuple<int, int> g;
            if (!tags.TryGetValue(3, out g) && !tags.TryGetValue(1, out g)) return;
            long n = I32(d, g.Item1);
            if (n <= 0 || n > MaxChars || g.Item1 + 4 + 2 * n > g.Item2) return;
            var s = Encoding.Unicode.GetString(d, g.Item1 + 4, (int)n * 2);
            if (t.Text.Length + s.Length > MaxChars) return;
            t.Text += s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\0", "");
        }

        static void Glyphs(byte[] d, Dictionary<int, Tuple<int, int>> tags, CmxText t)
        {
            Tuple<int, int> g;
            if (!tags.TryGetValue(1, out g)) return;
            int n = U16(d, g.Item1);
            if (n <= 0 || g.Item1 + 2 + 2 * n + 8 > g.Item2) return;
            var run = new CmxGlyphRun { Text = Encoding.Unicode.GetString(d, g.Item1 + 2, n * 2) };
            int p = g.Item1 + 2 + 2 * n;
            run.Start = new PointF((float)(I32(d, p) * MmPerUnit), (float)(I32(d, p + 4) * MmPerUnit));
            t.Glyphs.Add(run);
        }

        // rfnt: one tag list per font (1-based in the paragraphs).
        static List<string> Fonts(byte[] d, int at, int end)
        {
            var list = new List<string>();
            while (at < end && list.Count < 10000)
            {
                int start = at;
                var tags = Tags(d, at, end, out at);
                if (at <= start) break;
                string name = "";
                Tuple<int, int> g;
                if (tags.TryGetValue(4, out g) && g.Item2 - g.Item1 >= 4)
                {
                    long n = I32(d, g.Item1);
                    if (n > 0 && n < 256 && g.Item1 + 4 + 2 * n <= g.Item2) name = Encoding.Unicode.GetString(d, g.Item1 + 4, (int)n * 2);
                }
                if (name.Length == 0 && tags.TryGetValue(1, out g) && g.Item2 - g.Item1 >= 2)
                {
                    int n = U16(d, g.Item1);
                    if (n > 0 && n < 256 && g.Item1 + 2 + n <= g.Item2) name = Encoding.Default.GetString(d, g.Item1 + 2, n);
                }
                list.Add(name.TrimEnd('\0').Trim());
            }
            return list;
        }

        // ---------- bytes ----------

        // The tags of a list (tag -> data start, data end); the first of a tag wins.
        static Dictionary<int, Tuple<int, int>> Tags(byte[] d, int at, int end)
        {
            int after;
            return Tags(d, at, end, out after);
        }

        static Dictionary<int, Tuple<int, int>> Tags(byte[] d, int at, int end, out int after)
        {
            var tags = new Dictionary<int, Tuple<int, int>>();
            after = at;
            while (at < end)
            {
                int tag = d[at];
                if (tag == 0xFF) { after = at + 1; return tags; }
                if (at + 3 > end) break;
                int n = U16(d, at + 1);
                if (n < 3 || at + n > end) break;
                if (!tags.ContainsKey(tag)) tags[tag] = Tuple.Create(at + 3, at + n);
                at += n;
            }
            after = end;   // no end mark: not a tag list (an old-style record), nothing more to read here
            return tags;
        }

        static string Ascii(byte[] d, int at, int n) { return at >= 0 && at + n <= d.Length ? Encoding.ASCII.GetString(d, at, n) : ""; }
        static int U16(byte[] d, int at) { return at >= 0 && at + 2 <= d.Length ? BitConverter.ToUInt16(d, at) : 0; }
        static long U32(byte[] d, int at) { return at >= 0 && at + 4 <= d.Length ? BitConverter.ToUInt32(d, at) : 0; }
        static long I32(byte[] d, int at) { return at >= 0 && at + 4 <= d.Length ? BitConverter.ToInt32(d, at) : 0; }

        static InvalidDataException Bad() { return new InvalidDataException("not CMX"); }
    }
}
