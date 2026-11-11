// Pair kerning from the font file, as graphics programs apply it (CorelDRAW respects the font's own kerning; GDI+ doesn't
// kern at all). Reads the tables through GDI's GetFontData: GPOS (the 'kern' feature: pair adjustment lookups, format 1
// glyph pairs and format 2 class pairs, also behind extension lookups) and, for fonts without it, the old 'kern' table
// (format 0). Works for TrueType and CFF ("Std"/OTF) fonts alike: both keep their kerning in GPOS or 'kern', and the
// character-to-glyph map comes from 'cmap'.
// Values are in em (negative = closer): multiply by the font's em size (Font.Size in its own unit) for the distance.
// One table per family and style, cached; a font without kerning gets an empty table (Pair = 0, HasPairs = false).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace QlLabels
{
    sealed class Kerning
    {
        // A table without pairs (unknown font, no kerning in it, or a table that couldn't be read).
        public static readonly Kerning None = new Kerning();

        static readonly Dictionary<string, Kerning> cache = new Dictionary<string, Kerning>(StringComparer.OrdinalIgnoreCase);

        // The table for a font's family and style (bold, italic; underline and size don't matter).
        public static Kerning Of(Font font)
        {
            if (font == null) return None;
            return Of(font.FontFamily.Name, font.Style);
        }

        public static Kerning Of(string family, FontStyle style)
        {
            style &= FontStyle.Bold | FontStyle.Italic;
            string key = family + "|" + (int)style;
            lock (cache)
            {
                Kerning k;
                if (cache.TryGetValue(key, out k)) return k;
            }
            Kerning made;
            try { made = Load(family, style); }
            catch (Exception) { made = None; }   // a broken font file kerns nothing rather than stopping the drawing
            lock (cache) cache[key] = made;
            return made;
        }

        // The tables as read from a font file (for tests with a font's own bytes; null for a table the font lacks).
        public static Kerning FromTables(byte[] head, byte[] cmap, byte[] gpos, byte[] kern)
        {
            if (head == null || head.Length < 20 || cmap == null) return None;
            int upem = U16(head, 18);
            if (upem < 16 || upem > 16384) return None;
            var k = new Kerning();
            k.unitsPerEm = upem;
            k.cmap = new CharMap(cmap);
            if (gpos != null) k.gposLookups = GposKernLookups(gpos);
            if (k.gposLookups != null && k.gposLookups.Count > 0) k.gpos = gpos;
            else if (kern != null) k.kernPairs = KernTable(kern);
            if (k.gpos == null && (k.kernPairs == null || k.kernPairs.Count == 0)) return None;
            return k;
        }

        int unitsPerEm = 1000;
        CharMap cmap;
        byte[] gpos;
        List<List<int>> gposLookups;            // per kern lookup: its pair adjustment subtables (offsets into gpos)
        Dictionary<uint, short> kernPairs;      // old 'kern' table: (left << 16 | right) -> font units
        readonly Dictionary<uint, float> pairs = new Dictionary<uint, float>();   // glyph pair -> em, worked out once

        public bool HasPairs { get { return gpos != null || (kernPairs != null && kernPairs.Count > 0); } }
        public int UnitsPerEm { get { return unitsPerEm; } }

        // The adjustment between two characters in em (0 when the font has none for them). A text element of several
        // code units (a letter with accents, a surrogate pair) kerns by its last and the next one's first character.
        public float Pair(string left, string right)
        {
            if (!HasPairs || string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right)) return 0;
            return Pair(left[left.Length - 1], right[0]);
        }

        public float Pair(char left, char right)
        {
            if (!HasPairs) return 0;
            int a = cmap.Glyph(left), b = cmap.Glyph(right);
            if (a <= 0 || b <= 0) return 0;
            uint key = (uint)a << 16 | (uint)b;
            lock (pairs)
            {
                float v;
                if (pairs.TryGetValue(key, out v)) return v;
                v = (float)GlyphPair(a, b) / unitsPerEm;
                pairs[key] = v;
                return v;
            }
        }

        // The pairs before element k of a stretch (em): how far kerning moves element k from where it would be without.
        public float Before(IList<string> elements, int k)
        {
            if (!HasPairs || elements == null) return 0;
            float sum = 0;
            for (int i = 1; i <= k && i < elements.Count; i++) sum += Pair(elements[i - 1], elements[i]);
            return sum;
        }

        // All pairs of a stretch (em): what kerning adds to its width.
        public float Total(IList<string> elements)
        {
            return elements == null ? 0 : Before(elements, elements.Count - 1);
        }

        int GlyphPair(int a, int b)
        {
            if (gpos != null)
            {
                int sum = 0;
                foreach (var lookup in gposLookups)
                    foreach (int sub in lookup)
                    {
                        int v;
                        if (PairPos(gpos, sub, a, b, out v)) { sum += v; break; }   // the first subtable that applies
                    }
                return sum;
            }
            short s;
            return kernPairs.TryGetValue((uint)a << 16 | (uint)b, out s) ? s : 0;
        }

        // ---- GPOS ----

        // The lookups of the 'kern' feature for Latin text (script 'latn', else 'DFLT', else every 'kern' feature), in
        // lookup order, each as its pair adjustment subtables (extension lookups resolved). A damaged or made-up font
        // could point every index at one lookup of 65,535 subtables (billions of entries, every pair asking them all):
        // at most MaxSubtables are taken and the feature lists are read up to MaxEntries (real fonts: a few dozen).
        const int MaxSubtables = 4096, MaxEntries = 1 << 20;

        static List<List<int>> GposKernLookups(byte[] t)
        {
            var result = new List<List<int>>();
            if (t.Length < 10 || U16(t, 0) != 1) return result;
            int scripts = U16(t, 4), features = U16(t, 6), lookups = U16(t, 8);
            if (features == 0 || lookups == 0) return result;

            var wanted = new SortedSet<int>();
            var langSysFeatures = LangSysFeatures(t, scripts);
            int fcount = U16(t, features), entries = 0;
            for (int i = 0; i < fcount && entries < MaxEntries; i++)
            {
                int rec = features + 2 + i * 6;
                if (Tag(t, rec) != "kern") continue;
                if (langSysFeatures != null && !langSysFeatures.Contains(i)) continue;
                int f = features + U16(t, rec + 4);
                int n = U16(t, f + 2);
                for (int j = 0; j < n && entries < MaxEntries; j++, entries++) wanted.Add(U16(t, f + 4 + j * 2));
            }

            int lcount = U16(t, lookups), taken = 0;
            foreach (int li in wanted)
            {
                if (taken >= MaxSubtables) break;
                if (li >= lcount) continue;
                int l = lookups + U16(t, lookups + 2 + li * 2);
                int type = U16(t, l), subs = U16(t, l + 4);
                var list = new List<int>();
                for (int s = 0; s < subs && taken < MaxSubtables; s++)
                {
                    int sub = l + U16(t, l + 6 + s * 2);
                    int stype = type;
                    if (type == 9)   // extension: format 1, the real type, a 32-bit offset from here
                    {
                        if (U16(t, sub) != 1) continue;
                        stype = U16(t, sub + 2);
                        sub += (int)U32(t, sub + 4);
                    }
                    if (stype == 2) { list.Add(sub); taken++; }
                }
                if (list.Count > 0) result.Add(list);
            }
            return result;
        }

        // The feature indexes of the default language of script 'latn', else 'DFLT'; null: no script list, take all.
        static HashSet<int> LangSysFeatures(byte[] t, int scripts)
        {
            if (scripts == 0) return null;
            int n = U16(t, scripts);
            int pick = -1;
            foreach (string want in new[] { "latn", "DFLT" })
            {
                for (int i = 0; i < n && pick < 0; i++)
                    if (Tag(t, scripts + 2 + i * 6) == want) pick = scripts + U16(t, scripts + 2 + i * 6 + 4);
                if (pick >= 0) break;
            }
            if (pick < 0) return null;
            int langSys = U16(t, pick);
            if (langSys == 0) return null;
            int ls = pick + langSys;
            var set = new HashSet<int>();
            int required = U16(t, ls + 2);
            if (required != 0xFFFF) set.Add(required);
            int count = U16(t, ls + 4);
            for (int i = 0; i < count; i++) set.Add(U16(t, ls + 6 + i * 2));
            return set;
        }

        // One pair adjustment subtable: true when it applies to the pair (the first glyph is covered and, for format 1,
        // the pair is listed); value = the first glyph's x advance change in font units.
        static bool PairPos(byte[] t, int sub, int a, int b, out int value)
        {
            value = 0;
            int format = U16(t, sub);
            int coverage = Coverage(t, sub + U16(t, sub + 2), a);
            if (coverage < 0) return false;
            int vf1 = U16(t, sub + 4), vf2 = U16(t, sub + 6);
            int size1 = ValueSize(vf1), size2 = ValueSize(vf2);
            if (format == 1)
            {
                int setCount = U16(t, sub + 8);
                if (coverage >= setCount) return false;
                int set = sub + U16(t, sub + 10 + coverage * 2);
                int n = U16(t, set);
                int rec = 2 + size1 + size2;
                int lo = 0, hi = n - 1;
                while (lo <= hi)   // the records are sorted by the second glyph
                {
                    int mid = (lo + hi) / 2;
                    int at = set + 2 + mid * rec;
                    int g = U16(t, at);
                    if (g == b) { value = XAdvance(t, at + 2, vf1); return true; }
                    if (g < b) lo = mid + 1; else hi = mid - 1;
                }
                return false;
            }
            if (format == 2)
            {
                int c1 = ClassOf(t, sub + U16(t, sub + 8), a), c2 = ClassOf(t, sub + U16(t, sub + 10), b);
                int n1 = U16(t, sub + 12), n2 = U16(t, sub + 14);
                if (c1 >= n1 || c2 >= n2) return false;
                int at = sub + 16 + (c1 * n2 + c2) * (size1 + size2);
                value = XAdvance(t, at, vf1);
                return true;
            }
            return false;
        }

        static int ValueSize(int format)
        {
            int n = 0;
            for (int bit = 0; bit < 8; bit++) if ((format & (1 << bit)) != 0) n += 2;
            return n;
        }

        // The XAdvance field of a value record (after XPlacement and YPlacement when present).
        static int XAdvance(byte[] t, int at, int format)
        {
            if ((format & 4) == 0) return 0;
            if ((format & 1) != 0) at += 2;
            if ((format & 2) != 0) at += 2;
            return S16(t, at);
        }

        // The glyph's index in a coverage table, -1 when it isn't covered.
        static int Coverage(byte[] t, int c, int glyph)
        {
            int format = U16(t, c), n = U16(t, c + 2);
            int lo = 0, hi = n - 1;
            if (format == 1)
            {
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2, g = U16(t, c + 4 + mid * 2);
                    if (g == glyph) return mid;
                    if (g < glyph) lo = mid + 1; else hi = mid - 1;
                }
                return -1;
            }
            if (format == 2)
            {
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2, r = c + 4 + mid * 6;
                    int start = U16(t, r), end = U16(t, r + 2);
                    if (glyph < start) hi = mid - 1;
                    else if (glyph > end) lo = mid + 1;
                    else return U16(t, r + 4) + glyph - start;
                }
            }
            return -1;
        }

        // The glyph's class in a class definition table (0 when it isn't listed).
        static int ClassOf(byte[] t, int c, int glyph)
        {
            int format = U16(t, c);
            if (format == 1)
            {
                int start = U16(t, c + 2), n = U16(t, c + 4);
                return glyph >= start && glyph < start + n ? U16(t, c + 6 + (glyph - start) * 2) : 0;
            }
            if (format == 2)
            {
                int n = U16(t, c + 2), lo = 0, hi = n - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2, r = c + 4 + mid * 6;
                    int start = U16(t, r), end = U16(t, r + 2);
                    if (glyph < start) hi = mid - 1;
                    else if (glyph > end) lo = mid + 1;
                    else return U16(t, r + 4);
                }
            }
            return 0;
        }

        // ---- the old 'kern' table (Microsoft version 0; Apple's version 1 isn't read) ----

        static Dictionary<uint, short> KernTable(byte[] t)
        {
            var pairs = new Dictionary<uint, short>();
            if (t.Length < 4 || U16(t, 0) != 0) return pairs;
            int n = U16(t, 2), at = 4;
            for (int i = 0; i < n && at + 6 <= t.Length; i++)
            {
                int length = U16(t, at + 2), coverage = U16(t, at + 4);
                bool horizontal = (coverage & 1) != 0, minimum = (coverage & 2) != 0, cross = (coverage & 4) != 0;
                bool replace = (coverage & 8) != 0;
                if ((coverage >> 8) == 0 && horizontal && !minimum && !cross)
                {
                    int count = U16(t, at + 6);
                    for (int k = 0; k < count; k++)
                    {
                        int r = at + 14 + k * 6;
                        if (r + 6 > t.Length) break;
                        uint key = (uint)U16(t, r) << 16 | (uint)U16(t, r + 2);
                        short v = (short)S16(t, r + 4), old;
                        if (!replace && pairs.TryGetValue(key, out old)) v = (short)(old + v);
                        pairs[key] = v;
                    }
                }
                // a subtable's length is 16 bits, a big format 0 subtable can overflow it: then it is the last one
                if (length < 6) break;
                at += length;
            }
            return pairs;
        }

        // ---- 'cmap': characters to glyphs (Windows Unicode BMP format 4, full Unicode format 12, symbol fonts) ----

        sealed class CharMap
        {
            readonly byte[] t;
            readonly int sub = -1, format;
            readonly bool symbol;
            readonly Dictionary<char, int> seen = new Dictionary<char, int>();

            public CharMap(byte[] table)
            {
                t = table;
                if (t.Length < 4) return;
                int n = U16(t, 2), best = -1, rank = 0;
                for (int i = 0; i < n; i++)
                {
                    int r = 4 + i * 8;
                    if (r + 8 > t.Length) break;
                    int platform = U16(t, r), encoding = U16(t, r + 2);
                    int off = (int)U32(t, r + 4);
                    if (off + 4 > t.Length) continue;
                    int f = U16(t, off);
                    int score = platform == 3 && encoding == 10 && f == 12 ? 4
                              : platform == 3 && encoding == 1 && f == 4 ? 3
                              : platform == 0 && f == 4 ? 2
                              : platform == 3 && encoding == 0 && f == 4 ? 1 : 0;
                    if (score > rank) { rank = score; best = off; }
                }
                if (best < 0) return;
                sub = best;
                format = U16(t, sub);
                symbol = rank == 1;
            }

            public int Glyph(char ch)
            {
                if (sub < 0) return 0;
                lock (seen)
                {
                    int g;
                    if (seen.TryGetValue(ch, out g)) return g;
                    g = Look(ch);
                    if (g == 0 && symbol) g = Look(0xF000 | (ch & 0xFF));
                    seen[ch] = g;
                    return g;
                }
            }

            int Look(int code)
            {
                if (format == 4)
                {
                    int segX2 = U16(t, sub + 6);
                    int ends = sub + 14, starts = ends + segX2 + 2, deltas = starts + segX2, ranges = deltas + segX2;
                    int lo = 0, hi = segX2 / 2 - 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2;
                        int end = U16(t, ends + mid * 2), start = U16(t, starts + mid * 2);
                        if (code > end) { lo = mid + 1; continue; }
                        if (code < start) { hi = mid - 1; continue; }
                        int delta = U16(t, deltas + mid * 2), range = U16(t, ranges + mid * 2);
                        if (range == 0) return (code + delta) & 0xFFFF;
                        int at = ranges + mid * 2 + range + (code - start) * 2;
                        if (at + 2 > t.Length) return 0;
                        int g = U16(t, at);
                        return g == 0 ? 0 : (g + delta) & 0xFFFF;
                    }
                    return 0;
                }
                if (format == 12)
                {
                    int n = (int)U32(t, sub + 12), lo = 0, hi = n - 1;
                    while (lo <= hi)
                    {
                        int mid = (lo + hi) / 2, r = sub + 16 + mid * 12;
                        uint start = U32(t, r), end = U32(t, r + 4);
                        if (code < start) hi = mid - 1;
                        else if (code > end) lo = mid + 1;
                        else return (int)(U32(t, r + 8) + (uint)code - start);
                    }
                }
                return 0;
            }
        }

        // ---- reading ----

        static int U16(byte[] t, int at) { return at + 2 <= t.Length && at >= 0 ? t[at] << 8 | t[at + 1] : 0; }
        static int S16(byte[] t, int at) { return (short)U16(t, at); }
        static uint U32(byte[] t, int at) { return at + 4 <= t.Length && at >= 0 ? (uint)(t[at] << 24 | t[at + 1] << 16 | t[at + 2] << 8 | t[at + 3]) : 0; }
        static string Tag(byte[] t, int at) { return at + 4 <= t.Length ? new string(new[] { (char)t[at], (char)t[at + 1], (char)t[at + 2], (char)t[at + 3] }) : ""; }

        // ---- GDI: the font file's tables ----

        static Kerning Load(string family, FontStyle style)
        {
            using (var font = new Font(family, 100, style, GraphicsUnit.Pixel))
            {
                IntPtr hfont = font.ToHfont();
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                try
                {
                    IntPtr old = SelectObject(dc, hfont);
                    try
                    {
                        return FromTables(Table(dc, "head"), Table(dc, "cmap"), Table(dc, "GPOS"), Table(dc, "kern"));
                    }
                    finally { SelectObject(dc, old); }
                }
                finally
                {
                    DeleteDC(dc);
                    DeleteObject(hfont);
                }
            }
        }

        // The table as stored in the font (null when the font has none). GDI's tag is the four characters little-endian.
        static byte[] Table(IntPtr dc, string tag)
        {
            uint key = (uint)(tag[0] | tag[1] << 8 | tag[2] << 16 | tag[3] << 24);
            uint size = GetFontData(dc, key, 0, null, 0);
            if (size == GdiError || size == 0 || size > 64 * 1024 * 1024) return null;
            var data = new byte[size];
            return GetFontData(dc, key, 0, data, size) == size ? data : null;
        }

        const uint GdiError = 0xFFFFFFFF;
        [DllImport("gdi32.dll")] static extern uint GetFontData(IntPtr hdc, uint table, uint offset, byte[] buffer, uint length);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
