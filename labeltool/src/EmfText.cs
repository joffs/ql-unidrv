// Text from an EMF that holds only text (CorelDRAW keeps text as text records with their font, not as curves): read
// without playing the EMF, so a paste can make editable text objects. The records that matter: map mode,
// window/viewport extents and origins, world transforms, SaveDC/RestoreDC, ExtCreateFontIndirectW + SelectObject,
// SetTextAlign, ExtTextOutW/A with their dx arrays. Each text record is a run (its text at a font, size and style, a
// baseline start and a width in mm on the picture's frame); runs on one baseline make a line, lines close together
// with a common left edge (or centre, or right edge) a paragraph. Font size: lfHeight through the transforms to mm,
// then points (CorelDRAW's own numbers alone look inconsistent). A font that isn't installed gets a substitute and is
// named in Missing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Text;

namespace QlLabels
{
    sealed class EmfRun
    {
        public string Text = "";
        public string Font = "";
        public double SizePt;
        public bool Bold, Italic, Underline;
        public PointF Start;            // baseline start on the frame (mm, y down)
        public float Width;             // along the baseline (mm)
        public double Angle;            // clockwise degrees
        public float Ascent, Descent;   // mm
        public float Em { get { return (float)(SizePt / 72 * 25.4); } }
        public PointF End { get { var d = EmfText.Dir(Angle); return new PointF(Start.X + d.X * Width, Start.Y + d.Y * Width); } }

        public bool SameFormat(EmfRun o)
        {
            return o.Font == Font && Math.Abs(o.SizePt - SizePt) < 0.25 && o.Bold == Bold && o.Italic == Italic && o.Underline == Underline;
        }
    }

    sealed class EmfLine
    {
        public List<EmfRun> Runs = new List<EmfRun>();
        public PointF Start { get { return Runs[0].Start; } }
        public double Angle { get { return Runs[0].Angle; } }
        public float Width { get { var d = EmfText.Dir(Angle); var e = Runs[Runs.Count - 1].End; return (e.X - Start.X) * d.X + (e.Y - Start.Y) * d.Y; } }
        public float Ascent { get { return Runs.Max(r => r.Ascent); } }
        public float Descent { get { return Runs.Max(r => r.Descent); } }
        public float Em { get { return Runs.Max(r => r.Em); } }
        public string Text { get { return string.Concat(Runs.Select(r => r.Text)); } }
    }

    sealed class EmfParagraph
    {
        public List<EmfLine> Lines = new List<EmfLine>();
        public TextAlign Align = TextAlign.Left;
        public double Angle { get { return Lines[0].Angle; } }
    }

    static class EmfText
    {
        public const string Substitute = "Segoe UI";

        // The paragraphs of a text-only EMF (InvalidDataException: no EMF or damaged); missing: fonts not installed.
        public static List<EmfParagraph> Read(byte[] emf, out List<string> missing)
        {
            var runs = Runs(emf);
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families) installed.Add(f.Name);
            missing = new List<string>();
            foreach (var r in runs)
                if (!installed.Contains(r.Font))
                {
                    if (!missing.Contains(r.Font)) missing.Add(r.Font);
                    r.Font = Substitute;
                }
            return Paragraphs(Lines(runs));
        }

        // ---------- records ----------

        sealed class LogFont
        {
            public int Height, Escapement, Weight;
            public bool Italic, Underline;
            public string Face = "";
        }

        sealed class State
        {
            public int MapMode = 1, TextAlign;
            public PointF WindowOrg, ViewportOrg;
            public SizeF WindowExt = new SizeF(1, 1), ViewportExt = new SizeF(1, 1);
            public float[] World = { 1, 0, 0, 1, 0, 0 };   // m11 m12 m21 m22 dx dy
            public int Font = -1;
            public State Copy() { var s = (State)MemberwiseClone(); s.World = (float[])World.Clone(); return s; }
        }

        // Every text record as a run on the frame, in record order.
        internal static List<EmfRun> Runs(byte[] emf) { return Runs(emf, null); }

        // More text records than a label has (thousands take seconds): not read as text (pasted as a picture, not repaired).
        const int MaxTexts = 1000;

        // fixes: the text records read with the first mapping (record position, that mapping, the mapping in force)
        static List<EmfRun> Runs(byte[] emf, List<Tuple<int, State, State>> fixes)
        {
            EmfPicture.Check(emf);
            float pxPerMmX = Int(emf, 72) / (float)Math.Max(1, Int(emf, 80)), pxPerMmY = Int(emf, 76) / (float)Math.Max(1, Int(emf, 84));
            if (pxPerMmX <= 0 || pxPerMmY <= 0) { pxPerMmX = pxPerMmY = 96 / 25.4f; }
            var frame = new PointF(Int(emf, 24) / 100f, Int(emf, 28) / 100f);
            var frameSize = new SizeF((Int(emf, 32) - Int(emf, 24)) / 100f, (Int(emf, 36) - Int(emf, 28)) / 100f);
            State outer = null;   // the mapping before the first SaveDC (see TextRun's fallback)
            var fonts = new Dictionary<int, LogFont>();
            var stack = new Stack<State>();
            var s = new State();
            var runs = new List<EmfRun>();
            int total = Int(emf, 48), pos = 0, texts = 0;
            while (pos + 8 <= total)
            {
                int type = Int(emf, pos), size = Int(emf, pos + 4);
                if (type == 14 || size < 8) break;   // EMR_EOF (sizes were checked by EmfPicture.Check)
                int a = pos + 8;
                switch (type)
                {
                    case 9: s.WindowExt = new SizeF(Int(emf, a), Int(emf, a + 4)); break;             // SETWINDOWEXTEX
                    case 10: s.WindowOrg = new PointF(Int(emf, a), Int(emf, a + 4)); break;           // SETWINDOWORGEX
                    case 11: s.ViewportExt = new SizeF(Int(emf, a), Int(emf, a + 4)); break;          // SETVIEWPORTEXTEX
                    case 12: s.ViewportOrg = new PointF(Int(emf, a), Int(emf, a + 4)); break;         // SETVIEWPORTORGEX
                    case 17: s.MapMode = Int(emf, a); break;                                          // SETMAPMODE
                    case 22: s.TextAlign = Int(emf, a); break;                                        // SETTEXTALIGN
                    case 33:                                                                           // SAVEDC
                        if (outer == null) outer = s.Copy();
                        stack.Push(s.Copy());
                        break;
                    case 34:                                                                           // RESTOREDC
                        int back = Int(emf, a);
                        for (int i = 0; i < -back && stack.Count > 0; i++) s = stack.Pop();
                        break;
                    case 35: s.World = Xform(emf, a); break;                                          // SETWORLDTRANSFORM
                    case 36:                                                                           // MODIFYWORLDTRANSFORM
                        var x = Xform(emf, a);
                        int mode = Int(emf, a + 24);
                        if (mode == 1) s.World = new float[] { 1, 0, 0, 1, 0, 0 };
                        else if (mode == 2) s.World = Multiply(x, s.World);    // left: x first
                        else if (mode == 3) s.World = Multiply(s.World, x);
                        else if (mode == 4) s.World = x;
                        break;
                    case 37:                                                                           // SELECTOBJECT
                        int h = Int(emf, a);
                        if (fonts.ContainsKey(h)) s.Font = h;
                        else if ((h & 0x80000000) != 0 && (h & 0x7FFFFFFF) >= 10 && (h & 0x7FFFFFFF) <= 17) s.Font = -1;   // a stock font
                        break;
                    case 40: fonts.Remove(Int(emf, a)); break;                                        // DELETEOBJECT
                    case 82:                                                                           // EXTCREATEFONTINDIRECTW
                        if (size >= 12 + 92) fonts[Int(emf, a)] = ReadFont(emf, a + 4);
                        break;
                    case 83:                                                                           // EXTTEXTOUTA
                    case 84:                                                                           // EXTTEXTOUTW
                        if (++texts > MaxTexts) throw new InvalidDataException(Lang.T("This is no EMF picture, or it is damaged."));
                        var font = s.Font >= 0 && fonts.ContainsKey(s.Font) ? fonts[s.Font] : null;
                        var run = TextRun(emf, pos, size, type == 84, s, font, pxPerMmX, pxPerMmY, frame);
                        // CorelDRAW resets the page to 1:1 before its text but still writes the text's sizes and
                        // places in the units of the mapping it set first (0.01 mm), so the text lands far outside
                        // the picture (Windows draws it there too). A run outside the frame that the first mapping
                        // puts inside is read with that mapping.
                        if (outer != null && !Inside(run, frameSize))
                        {
                            var o = s.Copy();
                            o.MapMode = outer.MapMode; o.WindowOrg = outer.WindowOrg; o.ViewportOrg = outer.ViewportOrg;
                            o.WindowExt = outer.WindowExt; o.ViewportExt = outer.ViewportExt;
                            var again = TextRun(emf, pos, size, type == 84, o, font, pxPerMmX, pxPerMmY, frame);
                            if (Inside(again, frameSize))
                            {
                                run = again;
                                if (fixes != null) fixes.Add(Tuple.Create(pos, outer.Copy(), s.Copy()));
                            }
                        }
                        if (run != null) runs.Add(run);
                        break;
                }
                pos += size;
            }
            return runs;
        }

        // The EMF with CorelDRAW's misplaced text (see Runs) drawn where CorelDRAW meant it: around each such text
        // record the first mapping is set and the one in force set back. The same bytes when there's nothing to do.
        public static byte[] Repair(byte[] emf)
        {
            var fixes = new List<Tuple<int, State, State>>();
            Runs(emf, fixes);
            if (fixes.Count == 0) return emf;
            var o = new MemoryStream();
            int total = Int(emf, 48), at = 0, added = 0;
            foreach (var f in fixes)
            {
                int size = Int(emf, f.Item1 + 4);
                o.Write(emf, at, f.Item1 - at);
                added += Mapping(o, f.Item2);
                o.Write(emf, f.Item1, size);
                added += Mapping(o, f.Item3);
                at = f.Item1 + size;
            }
            o.Write(emf, at, emf.Length - at);
            var b = o.ToArray();
            BitConverter.GetBytes(total + (int)(b.Length - emf.Length)).CopyTo(b, 48);   // nBytes
            BitConverter.GetBytes(Int(emf, 52) + added).CopyTo(b, 52);                   // nRecords
            return b;
        }

        static int Mapping(Stream o, State s)
        {
            Record(o, 17, s.MapMode);                                                   // SETMAPMODE
            Record(o, 10, (int)s.WindowOrg.X, (int)s.WindowOrg.Y);                       // SETWINDOWORGEX
            Record(o, 9, (int)s.WindowExt.Width, (int)s.WindowExt.Height);              // SETWINDOWEXTEX
            Record(o, 12, (int)s.ViewportOrg.X, (int)s.ViewportOrg.Y);                   // SETVIEWPORTORGEX
            Record(o, 11, (int)s.ViewportExt.Width, (int)s.ViewportExt.Height);         // SETVIEWPORTEXTEX
            return 5;
        }

        static void Record(Stream o, int type, params int[] values)
        {
            var b = new byte[8 + 4 * values.Length];
            BitConverter.GetBytes(type).CopyTo(b, 0);
            BitConverter.GetBytes(b.Length).CopyTo(b, 4);
            for (int i = 0; i < values.Length; i++) BitConverter.GetBytes(values[i]).CopyTo(b, 8 + 4 * i);
            o.Write(b, 0, b.Length);
        }

        // The run starts on the picture (with a little room) and its letters aren't taller than the picture.
        static bool Inside(EmfRun r, SizeF frame)
        {
            if (r == null) return false;
            float tol = 1 + 0.1f * Math.Max(frame.Width, frame.Height);
            return r.Start.X >= -tol && r.Start.X <= frame.Width + tol && r.Start.Y >= -tol && r.Start.Y <= frame.Height + tol
                && r.Ascent <= frame.Height + tol;
        }

        static LogFont ReadFont(byte[] d, int at)
        {
            var f = new LogFont { Height = Int(d, at), Escapement = Int(d, at + 8), Weight = Int(d, at + 16) };
            f.Italic = d[at + 20] != 0; f.Underline = d[at + 21] != 0;
            var face = Encoding.Unicode.GetString(d, at + 28, 64);
            int zero = face.IndexOf('\0');
            f.Face = (zero >= 0 ? face.Substring(0, zero) : face).Trim();
            return f;
        }

        static float[] Xform(byte[] d, int at)
        {
            var x = new float[6];
            for (int i = 0; i < 6; i++) x[i] = at >= 0 && at + 4 * i <= d.Length - 4 ? BitConverter.ToSingle(d, at + 4 * i) : 0;
            return x;
        }

        // a then b (row vectors: p * a * b)
        static float[] Multiply(float[] a, float[] b)
        {
            return new[]
            {
                a[0] * b[0] + a[1] * b[2], a[0] * b[1] + a[1] * b[3],
                a[2] * b[0] + a[3] * b[2], a[2] * b[1] + a[3] * b[3],
                a[4] * b[0] + a[5] * b[2] + b[4], a[4] * b[1] + a[5] * b[3] + b[5],
            };
        }

        // The page transform's scale (logical units to device pixels) for the map mode.
        static SizeF PageScale(State s, float pxPerMmX, float pxPerMmY)
        {
            float mm;
            switch (s.MapMode)
            {
                case 2: mm = 0.1f; break;          // LOMETRIC
                case 3: mm = 0.01f; break;         // HIMETRIC
                case 4: mm = 0.254f; break;        // LOENGLISH
                case 5: mm = 0.0254f; break;       // HIENGLISH
                case 6: mm = 25.4f / 1440; break;  // TWIPS
                case 7:                            // ISOTROPIC
                case 8:                            // ANISOTROPIC
                    float sx = s.WindowExt.Width != 0 ? s.ViewportExt.Width / s.WindowExt.Width : 1;
                    float sy = s.WindowExt.Height != 0 ? s.ViewportExt.Height / s.WindowExt.Height : 1;
                    if (s.MapMode == 7) { float m = Math.Min(Math.Abs(sx), Math.Abs(sy)); sx = Math.Sign(sx) * m; sy = Math.Sign(sy) * m; }
                    return new SizeF(sx, sy);
                default: return new SizeF(1, 1);   // TEXT: one unit = one pixel
            }
            return new SizeF(mm * pxPerMmX, -mm * pxPerMmY);   // y up
        }

        static EmfRun TextRun(byte[] d, int pos, int size, bool unicode, State s, LogFont font, float pxPerMmX, float pxPerMmY, PointF frame)
        {
            int at = pos + 36;   // EMRTEXT
            if (size < 76) return null;
            var refPt = new PointF(Int(d, at), Int(d, at + 4));
            int n = Int(d, at + 8), offString = Int(d, at + 12), options = Int(d, at + 16), offDx = Int(d, at + 36);
            if (n <= 0 || n > 10000 || offString < 76 || offString > size - n * (unicode ? 2 : 1)) return null;   // (no overflow with a damaged offset)
            string text = unicode ? Encoding.Unicode.GetString(d, pos + offString, n * 2) : Encoding.Default.GetString(d, pos + offString, n);
            if (text.Trim().Length == 0) return null;
            if (font == null) font = new LogFont { Height = -16, Face = Substitute, Weight = 400 };
            var page = PageScale(s, pxPerMmX, pxPerMmY);
            // logical -> mm on the frame
            Func<PointF, PointF> map = p =>
            {
                var w = s.World;
                float wx = p.X * w[0] + p.Y * w[2] + w[4], wy = p.X * w[1] + p.Y * w[3] + w[5];
                float dx = (wx - s.WindowOrg.X) * page.Width + s.ViewportOrg.X, dy = (wy - s.WindowOrg.Y) * page.Height + s.ViewportOrg.Y;
                return new PointF(dx / pxPerMmX - frame.X, dy / pxPerMmY - frame.Y);
            };
            Func<PointF, PointF> vec = v => { var o = map(PointF.Empty); var p = map(v); return new PointF(p.X - o.X, p.Y - o.Y); };
            // The baseline's direction: the escapement counts counter-clockwise as seen; in logical units with the
            // page's axis directions.
            double e = font.Escapement / 10.0 * Math.PI / 180;
            var dirL = new PointF((float)Math.Cos(e) * Math.Sign(page.Width == 0 ? 1 : page.Width), (float)-Math.Sin(e) * Math.Sign(page.Height == 0 ? 1 : page.Height));
            var dirMm = vec(dirL);
            float len = (float)Math.Sqrt(dirMm.X * dirMm.X + dirMm.Y * dirMm.Y);
            if (!(len > 0) || !Finite(len)) return null;
            var D = new PointF(dirMm.X / len, dirMm.Y / len);
            var U = new PointF(D.Y, -D.X);   // up, as seen (y down)
            // mm per logical unit across the text (the transform's mean scale)
            var ax = vec(new PointF(1, 0)); var ay = vec(new PointF(0, 1));
            float scale = (float)Math.Sqrt(Math.Abs(ax.X * ay.Y - ax.Y * ay.X));
            bool bold = font.Weight >= 600;
            var style = (bold ? FontStyle.Bold : 0) | (font.Italic ? FontStyle.Italic : 0);
            float emRatio = 1, ascent = 0.8f, descent = 0.2f;
            try
            {
                using (var fam = new FontFamily(font.Face))
                {
                    var st = fam.IsStyleAvailable(style) ? style : FontStyle.Regular;
                    float em = fam.GetEmHeight(st);
                    ascent = fam.GetCellAscent(st) / em; descent = fam.GetCellDescent(st) / em;
                    emRatio = em / (fam.GetCellAscent(st) + fam.GetCellDescent(st));
                }
            }
            catch (ArgumentException) { emRatio = 1 / 1.2f; }   // not installed: typical proportions
            float emMm = Math.Abs(font.Height) * scale * (font.Height > 0 ? emRatio : 1);
            if (font.Height == 0) emMm = 12 / 72f * 25.4f;
            if (!(emMm > 0.05f && emMm <= 500)) return null;   // (NaN from a damaged transform too)
            // width: the dx array (logical units along the baseline), else measured
            float width = 0;
            if (offDx > 0 && offDx <= size - 4 * n)
            {
                int step = (options & 0x2000) != 0 ? 8 : 4;   // ETO_PDY: x and y per character
                if (offDx <= size - step * n)
                    for (int i = 0; i < n; i++) width += Int(d, pos + offDx + step * i);
                width = Math.Abs(width) * len;
            }
            if (width <= 0) width = Measure(text, font.Face, style, emMm);
            var origin = map(refPt);
            int align = s.TextAlign;
            float back = (align & 6) == 6 ? width / 2 : (align & 6) == 2 ? width : 0;
            float up = (align & 24) == 24 ? 0 : (align & 8) == 8 ? -descent * emMm : ascent * emMm;   // baseline below a top reference
            var start = new PointF(origin.X - D.X * back - U.X * up, origin.Y - D.Y * back - U.Y * up);
            if (!Finite(start.X) || !Finite(start.Y) || !Finite(width)) return null;   // a damaged transform
            return new EmfRun
            {
                Text = text.Replace("\0", ""), Font = font.Face.Length > 0 ? font.Face : Substitute,
                SizePt = Math.Max(1, Math.Round(emMm / 25.4 * 72 * 2) / 2), Bold = bold, Italic = font.Italic, Underline = font.Underline,
                Start = start, Width = width, Angle = Math.Round(Math.Atan2(D.Y, D.X) * 180 / Math.PI, 1),
                Ascent = ascent * emMm, Descent = descent * emMm,
            };
        }

        static float Measure(string text, string face, FontStyle style, float emMm)
        {
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font(face, emMm, style, GraphicsUnit.Millimeter))
                {
                    g.PageUnit = GraphicsUnit.Millimeter;
                    return g.MeasureString(text, f, PointF.Empty, StringFormat.GenericTypographic).Width;
                }
            }
            catch (ArgumentException) { return text.Length * emMm * 0.55f; }
        }

        static int Int(byte[] d, int at) { return at >= 0 && at <= d.Length - 4 ? BitConverter.ToInt32(d, at) : 0; }
        static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        // ---------- lines and paragraphs ----------

        internal static PointF Dir(double angle)
        {
            double a = angle * Math.PI / 180;
            return new PointF((float)Math.Cos(a), (float)Math.Sin(a));
        }

        static float Along(PointF p, PointF origin, double angle) { var d = Dir(angle); return (p.X - origin.X) * d.X + (p.Y - origin.Y) * d.Y; }
        static float Across(PointF p, PointF origin, double angle) { var d = Dir(angle); return -(p.X - origin.X) * d.Y + (p.Y - origin.Y) * d.X; }   // down positive

        static bool SameAngle(double a, double b) { double x = Math.Abs(a - b) % 360; return Math.Min(x, 360 - x) < 1; }

        // Runs on one baseline, next to each other, make a line (a space where the gap is a word's).
        internal static List<EmfLine> Lines(List<EmfRun> runs)
        {
            var lines = new List<EmfLine>();
            foreach (var r in runs)
            {
                EmfLine into = null;
                foreach (var l in lines)
                {
                    var last = l.Runs[l.Runs.Count - 1];
                    if (!SameAngle(l.Angle, r.Angle)) continue;
                    float em = Math.Max(last.Em, r.Em);
                    float across = Math.Abs(Across(r.Start, last.Start, l.Angle)), gap = Along(r.Start, last.End, l.Angle);
                    if (across < 0.3f * em && gap > -0.5f * em && gap < 2f * em) { into = l; break; }
                }
                if (into == null) { lines.Add(new EmfLine { Runs = { r } }); continue; }
                var prev = into.Runs[into.Runs.Count - 1];
                float g = Along(r.Start, prev.End, into.Angle);
                if (g > 0.2f * Math.Max(prev.Em, r.Em) && !prev.Text.EndsWith(" ") && !r.Text.StartsWith(" ")) prev.Text += " ";
                if (prev.SameFormat(r)) { prev.Text += r.Text; prev.Width = Along(r.End, prev.Start, into.Angle); }
                else into.Runs.Add(r);
            }
            return lines;
        }

        // Lines below each other, a line pitch apart, with a common left edge, centre or right edge make a paragraph.
        internal static List<EmfParagraph> Paragraphs(List<EmfLine> lines)
        {
            var paragraphs = new List<EmfParagraph>();
            foreach (var group in lines.GroupBy(l => Math.Round(((l.Angle % 360) + 360) % 360)))
            {
                double angle = group.First().Angle;
                var origin = group.First().Start;
                var sorted = group.OrderBy(l => Across(l.Start, origin, angle)).ToList();
                EmfParagraph p = null;
                foreach (var l in sorted)
                {
                    if (p != null)
                    {
                        var prev = p.Lines[p.Lines.Count - 1];
                        float em = Math.Max(prev.Em, l.Em);
                        float pitch = Across(l.Start, prev.Start, angle);
                        float left = Along(l.Start, prev.Start, angle), right = left + l.Width - prev.Width, centre = left + (l.Width - prev.Width) / 2;
                        TextAlign? align = null;
                        if (Math.Abs(left) < em) align = TextAlign.Left;
                        else if (Math.Abs(centre) < em) align = TextAlign.Center;
                        else if (Math.Abs(right) < em) align = TextAlign.Right;
                        if (pitch > 0.8f * em && pitch < 2.2f * em && align.HasValue && (p.Lines.Count == 1 || p.Align == align.Value))
                        {
                            if (p.Lines.Count == 1) p.Align = align.Value;
                            p.Lines.Add(l);
                            continue;
                        }
                    }
                    p = new EmfParagraph();
                    p.Lines.Add(l);
                    paragraphs.Add(p);
                }
            }
            return paragraphs;
        }

        // ---------- text objects ----------

        // A paragraph as one text object on the frame (mm): a single line, or a text frame for several lines; parts in
        // other fonts, sizes or styles become its runs (the object's own format is the one most characters have).
        public static List<TextObject> Objects(EmfParagraph p)
        {
            var runs = new List<TextRun>();
            for (int i = 0; i < p.Lines.Count; i++)
            {
                var line = p.Lines[i].Runs;
                for (int j = 0; j < line.Count; j++)
                {
                    var r = line[j];
                    string text = j == line.Count - 1 ? r.Text.TrimEnd() : r.Text;
                    if (i < p.Lines.Count - 1 && j == line.Count - 1) text += "\n";
                    runs.Add(new TextRun(text, r.Font, r.SizePt, r.Bold, r.Italic, r.Underline));
                }
            }
            var t = Make(p.Lines[0].Runs[0], "", p.Lines.Count > 1 ? TextKind.Frame : TextKind.Line, p.Align);
            t.SetRuns(runs);
            if (t.Runs != null) t.Kind = TextKind.Frame;   // words in another format: only a paragraph text has them
            Place(t, p, p.Lines);
            return new List<TextObject> { t };
        }

        static TextObject Make(EmfRun r, string text, TextKind kind, TextAlign align)
        {
            return new TextObject { Text = text, Font = r.Font, Size = Math.Max(1, r.SizePt), Bold = r.Bold, Italic = r.Italic, Underline = r.Underline, Kind = kind, Align = align };
        }

        // The object's box around the lines (its centre where the lines' centre is), then turned.
        static void Place(TextObject t, EmfParagraph p, List<EmfLine> lines)
        {
            EditorRender.FitBox(t);
            double angle = p.Angle;
            var D = Dir(angle);
            var U = new PointF(D.Y, -D.X);
            var first = lines[0];
            var origin = first.Start;
            float left = lines.Min(l => Along(l.Start, origin, angle)), right = lines.Max(l => Along(l.Start, origin, angle) + l.Width);
            float top = -first.Ascent, bottom = Across(lines[lines.Count - 1].Start, origin, angle) + lines[lines.Count - 1].Descent;
            float ca = (left + right) / 2, cd = (top + bottom) / 2;   // along, across (down)
            var c = new PointF(origin.X + D.X * ca - U.X * cd, origin.Y + D.Y * ca - U.Y * cd);
            t.X = Math.Round(c.X - t.W / 2, 2);
            t.Y = Math.Round(c.Y - t.H / 2, 2);
            if (Math.Abs(angle) > 0.05) t.SetAngle(angle);
        }
    }
}
