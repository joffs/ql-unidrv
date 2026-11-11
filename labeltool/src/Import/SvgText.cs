// Text from an SVG on the clipboard (Affinity puts its copies there as SVG, text as <text> and <tspan>): editable text
// objects instead of the curves SvgPicture makes of them. Each stretch of characters on one baseline, in one format,
// becomes a run (EmfRun: baseline start, width, font, size in mm on the picture), and EmfText's line and paragraph
// rules join them, as for an EMF's text records. Positions: x/y lists (one entry per character, as Affinity writes
// them), dx/dy, text-anchor, the transforms of the text and its groups, the root's viewBox and size (px = 1/96 inch, as
// SvgPicture.SizeMm, so text and picture of one copy line up). Font: the first installed family of the font-family
// list (a list may start with a PostScript name such as 'Arial-BoldMT'), bold from font-weight >= 600, italic from
// font-style. letter-spacing, or x positions wider than the font's own advances, become the run's character spacing.
// Not read: textPath, vertical writing, rotate lists, textLength, fills and strokes (text prints black).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace QlLabels
{
    sealed class SvgTextResult
    {
        public List<TextObject> Objects = new List<TextObject>();
        public List<string> Missing = new List<string>();   // fonts not installed (a substitute is used)
        public bool OnlyText;                               // the SVG draws nothing but its text (and its underlines)
        public byte[] Rest;                                 // the SVG without the text read (same frame); null: nothing left
        public int Skipped;                                 // <text> elements not read (textPath, vertical, ...)
    }

    // What one program writes differently (Import/Affinity/AffinityClip.cs).
    sealed class SvgQuirks
    {
        // A <g> holding only <text> elements, two or more, is one text: each <text> one laid-out line of a frame or of a
        // multi-line artistic text (Affinity). Its lines' pitch beyond the leading is the paragraphs' space after; a line
        // that ends where the next line's first word wouldn't fit the frame was wrapped (a space, not a line break).
        public bool TextGroups;
        // Resolutions the source's px may be at: with a size it was copied at that gives one of them within 1 %, the
        // text is scaled by that one exactly (the picture keeps the size it was copied at).
        public int[] Resolutions;

        public static readonly SvgQuirks None = new SvgQuirks();

        public static SvgQuirks For(ImportSource source)
        {
            return source != null && source.IsAffinity ? AffinityClip.Quirks : None;
        }
    }

    static class SvgText
    {
        const int MaxBytes = 20 * 1024 * 1024;
        // More <text> elements than this: pasted as a picture (a label has a few dozen; thousands of text objects would
        // make every redraw slow)
        const int MaxTexts = 1000;

        // Elements that draw something besides text (a picture with these keeps them as a picture).
        static readonly HashSet<string> Drawing = new HashSet<string>
        {
            "path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "image", "use", "foreignObject"
        };

        // Elements that never draw by themselves.
        static readonly HashSet<string> NoDrawing = new HashSet<string>
        {
            "defs", "clipPath", "mask", "pattern", "linearGradient", "radialGradient", "symbol", "marker", "filter", "style",
            "title", "desc", "metadata"
        };

        // Around the text inside a text object's box, both sides (mm; EditorRender's TextPad, as Import/TextImport.cs).
        const double BoxPadding = 0.6;

        // The text of an SVG source (not the cleaned picture: Clean turns text into paths) on its own picture's frame (mm,
        // the picture's top left at 0,0); null when the source can't be read or holds no text. sizeMm: the size it was
        // copied at when the source program says so elsewhere (Affinity: its PDF), else its width and height (px = 1/96
        // inch, as SvgPicture.SizeMm).
        // Anything it can't make sense of (an exception, or a text whose place, size or font size is out of the label's
        // ranges: NaN, infinity, a scale of 0) gives null too: the paste takes the picture then.
        public static SvgTextResult Read(byte[] source, SizeF? sizeMm = null, SvgQuirks quirks = null)
        {
            SvgTextResult result;
            try { result = ReadText(source, sizeMm, quirks); }
            catch (Exception) { return null; }
            return result == null || result.Objects.All(TextImport.Sane) ? result : null;
        }

        static SvgTextResult ReadText(byte[] source, SizeF? sizeMm, SvgQuirks quirks)
        {
            quirks = quirks ?? SvgQuirks.None;
            var doc = Load(source);
            if (doc == null || doc.SelectNodes("//*[local-name()='text']").Count > MaxTexts) return null;
            var root = doc.DocumentElement;
            var result = new SvgTextResult { OnlyText = true };
            var runs = new List<Run>();
            var texts = new List<XmlElement>();
            var bars = new List<Bar>();
            var top = RootMatrix(root, sizeMm, quirks.Resolutions);
            Walk(root, top, runs, texts, bars, result, false, quirks, null);
            if (runs.Count == 0) return null;

            var installed = Installed();
            foreach (var r in runs) Pick(r, installed, result.Missing);
            foreach (var r in runs) Measure(r);
            var under = Underlines(runs, bars);
            var marks = White(runs, bars);
            under.AddRange(marks);
            if (runs.Count == 0) return null;

            // per baseline and format the runs as EmfText reads them; character spacing kept per run
            var spacing = runs.ToDictionary(r => r.Emf, r => r.Spacing);
            var frame = FrameMm(root, top);
            bool alone = runs.Select(r => r.Group).Distinct().Count() == 1 && runs[0].Group != null;
            var paragraphs = new List<KeyValuePair<EmfParagraph, List<bool>>>();
            // (a group split where white characters were left out: each part on its own)
            foreach (var g in runs.Where(r => r.Group != null).GroupBy(r => new { r.Group, r.Part }))
            {
                List<bool> wraps;
                var gp = GroupParagraph(g.Select(r => r.Emf).ToList(), alone ? frame.Width : 0, out wraps);
                if (gp != null) paragraphs.Add(new KeyValuePair<EmfParagraph, List<bool>>(gp, wraps));
                else foreach (var r in g) r.Group = null;   // turned lines and such: by EmfText's rules
            }
            foreach (var part in runs.Where(r => r.Group == null).GroupBy(r => r.Part))
                foreach (var p in EmfText.Paragraphs(EmfText.Lines(part.Select(r => r.Emf).ToList())))
                    paragraphs.Add(new KeyValuePair<EmfParagraph, List<bool>>(p, null));
            foreach (var pw in paragraphs)
            {
                var p = pw.Key;
                var used = p.Lines.SelectMany(l => l.Runs).ToList();
                foreach (var t in EmfText.Objects(p))
                {
                    // white text: all of it white on black; white characters on their own black mark: those
                    if (used.All(u => u.White)) t.Invert = true;
                    else if (used.Any(u => u.White)) t.SetRuns(Inverted(p));
                    if (pw.Value != null) Paragraphs(t, p, pw.Value);
                    // one spacing for the whole object when its runs agree within 5 % (each line's is measured from
                    // rounded positions: a few % apart for the same setting)
                    var s = used.Select(u => spacing.ContainsKey(u) ? spacing[u] : 100).ToList();
                    double mean = s.Average();
                    if (s.Max() - s.Min() <= 5 && Math.Abs(mean - 100) >= 2)
                    {
                        double v = Math.Max(TextObject.MinSpacing, Math.Min(TextObject.MaxSpacing, Math.Round(mean)));
                        t.SetRuns(t.FullRuns().Select(x => x.With(spacing: v)).ToList());
                        EditorRender.FitBox(t);
                    }
                    result.Objects.Add(t);
                }
            }

            // what is left without the text and its underlines
            foreach (var t in texts) t.ParentNode.RemoveChild(t);
            foreach (var b in under) if (b.Element.ParentNode != null) b.Element.ParentNode.RemoveChild(b.Element);
            result.OnlyText = result.Skipped == 0 && !Draws(root, false);
            if (!result.OnlyText)
                using (var ms = new MemoryStream())
                {
                    using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false) })) doc.Save(w);
                    result.Rest = ms.ToArray();
                }

            // a frame copied alone: the copy's size is the frame's (Affinity: the frame, not the text in it)
            if (result.OnlyText && result.Objects.Count == 1 && result.Objects[0].Kind == TextKind.Frame && result.Objects[0].Angle == 0)
                ToFrame(result.Objects[0], FrameWidth(frame, paragraphs[0].Key));
            return result;
        }

        // The copy's size in mm (its root's box through the root matrix).
        static SizeF FrameMm(XmlElement root, double[] top)
        {
            double[] box = ViewBox(root.GetAttribute("viewBox"));
            double w = box != null ? box[2] : Length(root.GetAttribute("width"), 0), h = box != null ? box[3] : Length(root.GetAttribute("height"), 0);
            return new SizeF((float)(w * top[0]), (float)(h * top[3]));
        }

        // White text (Label Maker's texts print black; Invert prints white on black). A run is white when its fill is
        // (240 or more in each channel). A white run on its own black mark (a filled rect just around its characters, as
        // Affinity draws a highlight) keeps EmfRun.White and the mark goes (returned, removed from the picture with the
        // underlines); a white run in a text with dark characters on its line and no mark is left out (it doesn't show
        // on white; the rest stays where it was); a text all in white keeps White on every run (its object inverted).
        static List<Bar> White(List<Run> runs, List<Bar> bars)
        {
            int parts = runs.Count == 0 ? 0 : runs.Max(x => x.Part);
            var marks = new List<Bar>();
            foreach (var r in runs) r.Emf.White = IsWhite(Prop(r.Style, "fill", true));
            foreach (var r in runs.Where(x => x.Emf.White).ToList())
            {
                var e = r.Emf;
                float em = e.Em;
                var mark = e.Angle != 0 ? null : bars.FirstOrDefault(b => !marks.Contains(b)
                    && b.Left <= e.Start.X + 0.1 * em && b.Right >= e.End.X - 0.1 * em && b.Right - b.Left <= e.Width + 0.6 * em
                    && b.Top <= e.Start.Y - 0.6 * em && b.Bottom >= e.Start.Y && b.Bottom - b.Top <= 1.6 * em);
                if (mark != null) { marks.Add(mark); continue; }
                if (runs.Any(o => !o.Emf.White && SameLine(o, r)))
                {
                    // left out; what follows on its line is laid out apart (it stays where it was, not moved up)
                    int at = runs.IndexOf(r);
                    runs.Remove(r);
                    int part = ++parts;
                    for (int i = at; i < runs.Count; i++) if (SameLine(runs[i], r)) runs[i].Part = part;
                    if (at < runs.Count && SameLine(runs[at], r)) TrimStart(runs[at]);
                }
            }
            return marks;
        }

        // Two runs of one line: the same <text>, or the same text group (Affinity writes a line's colour changes as
        // <text> elements side by side in its group), on one baseline.
        static bool SameLine(Run a, Run b)
        {
            if (!ReferenceEquals(a.Text0, b.Text0) && (a.Group == null || !ReferenceEquals(a.Group, b.Group))) return false;
            float em = Math.Max(a.Emf.Em, b.Emf.Em);
            return Math.Abs(a.Emf.Angle - b.Emf.Angle) < 0.5
                   && Math.Abs(-(a.Emf.Start.X - b.Emf.Start.X) * EmfText.Dir(b.Emf.Angle).Y + (a.Emf.Start.Y - b.Emf.Start.Y) * EmfText.Dir(b.Emf.Angle).X) < 0.3 * em;
        }

        // A run without its leading spaces, measured again (it starts where its first character is).
        static void TrimStart(Run r)
        {
            int n = 0;
            while (n < r.Text.Length - 1 && r.Text[n] == ' ') n++;
            if (n == 0) return;
            double x = r.X;
            if (!double.IsNaN(r.Xs[n])) x = r.Xs[n];
            else
            {
                double scale = Math.Sqrt(Math.Abs(r.M[0] * r.M[3] - r.M[1] * r.M[2]));
                x += (MeasureMm(r.Text.ToString(0, n) + "x", r.Face, r.FontStyle, r.SizeUser * scale) - MeasureMm("x", r.Face, r.FontStyle, r.SizeUser * scale)) / scale
                     + r.Ls.Take(n).Sum();
            }
            r.Text.Remove(0, n);
            r.Xs.RemoveRange(0, n);
            r.Ls.RemoveRange(0, n);
            r.X = x;
            r.Xs[0] = x;
            bool white = r.Emf.White, underline = r.Emf.Underline;
            Measure(r);
            r.Emf.White = white;
            r.Emf.Underline = underline;
        }

        // white: the keyword, #fff, #ffffff, rgb(...) with 240 or more in each channel
        static bool IsWhite(string fill)
        {
            if (string.IsNullOrEmpty(fill)) return false;
            fill = fill.Trim().ToLowerInvariant();
            if (fill == "white") return true;
            int[] c = null;
            if (fill.StartsWith("#") && (fill.Length == 4 || fill.Length == 7))
            {
                string h = fill.Length == 4 ? new string(new[] { fill[1], fill[1], fill[2], fill[2], fill[3], fill[3] }) : fill.Substring(1);
                int v;
                if (int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) c = new[] { v >> 16 & 255, v >> 8 & 255, v & 255 };
            }
            else if (fill.StartsWith("rgb(") && fill.EndsWith(")"))
            {
                var parts = fill.Substring(4, fill.Length - 5).Split(',');
                double v;
                if (parts.Length >= 3)
                    c = parts.Take(3).Select(x => double.TryParse(x.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? (int)(x.Trim().EndsWith("%") ? v * 2.55 : v) : 0).ToArray();
            }
            return c != null && c.All(v => v >= 240);
        }

        // The object's runs as EmfText.Objects lays them out, white runs inverted (white on black behind them).
        static List<TextRun> Inverted(EmfParagraph p)
        {
            var list = new List<TextRun>();
            for (int i = 0; i < p.Lines.Count; i++)
            {
                var line = p.Lines[i].Runs;
                for (int j = 0; j < line.Count; j++)
                {
                    var r = line[j];
                    string text = j == line.Count - 1 ? r.Text.TrimEnd() : r.Text;
                    if (i < p.Lines.Count - 1 && j == line.Count - 1) text += "\n";
                    list.Add(new TextRun(text, r.Font, r.SizePt, r.Bold, r.Italic, r.Underline, invert: r.White ? true : (bool?)null));
                }
            }
            return list;
        }

        // A text group's lines as one paragraph (null: lines turned differently, or one line only): in order down the
        // page, aligned by the edge or centre they share best. wraps: for each line but the last, true when it was wrapped
        // (frameMm: the frame's width, 0 when unknown: every line ends a paragraph).
        static EmfParagraph GroupParagraph(List<EmfRun> runs, double frameMm, out List<bool> wraps)
        {
            wraps = null;
            var lines = EmfText.Lines(runs);
            if (lines.Count < 2 || lines.Any(l => Math.Abs(l.Angle - lines[0].Angle) > 0.5)) return null;
            double angle = lines[0].Angle;
            var d = EmfText.Dir(angle);
            var o = lines[0].Start;
            Func<PointF, double> along = q => (q.X - o.X) * d.X + (q.Y - o.Y) * d.Y;
            Func<PointF, double> across = q => -(q.X - o.X) * d.Y + (q.Y - o.Y) * d.X;
            lines = lines.OrderBy(l => across(l.Start)).ToList();
            Func<Func<EmfLine, double>, double> spread = f => lines.Max(f) - lines.Min(f);
            double left = spread(l => along(l.Start)), centre = spread(l => along(l.Start) + l.Width / 2), right = spread(l => along(l.Start) + l.Width);
            var p = new EmfParagraph { Lines = lines, Align = TextAlign.Left };
            double em = lines.Max(l => l.Em);
            if (centre < left - 0.02 * em && centre <= right) p.Align = TextAlign.Center;
            else if (right < left - 0.02 * em && right < centre) p.Align = TextAlign.Right;

            wraps = new List<bool>();
            // a frame wider than its widest line (half an em): text in a frame; else multi-line artistic text (its copy is
            // just as wide as the text) never wraps
            if (frameMm < lines.Max(l => l.Width) + 0.5 * em) frameMm = 0;
            for (int i = 0; i + 1 < lines.Count; i++)
            {
                bool wrapped = false;
                if (frameMm > 0)
                {
                    var r = lines[i].Runs[lines[i].Runs.Count - 1];
                    string next = lines[i + 1].Text.TrimStart();
                    int sp = next.IndexOf(' ');
                    string word = sp < 0 ? next : next.Substring(0, sp);
                    double width = lines[i].Width + MeasureMm(" " + word, r.Font, (r.Bold ? FontStyle.Bold : 0) | (r.Italic ? FontStyle.Italic : 0), r.Em);
                    wrapped = word.Length > 0 && width > frameMm;
                }
                wraps.Add(wrapped);
            }
            return p;
        }

        // A text group's line spacing and paragraph space from its pitches: the leading is the wrapped lines' pitch, else
        // the font's own (100 %); what a paragraph's last line has beyond it is the space after. Wrapped lines end with a
        // space instead of a line break (the frame wraps them again).
        static void Paragraphs(TextObject t, EmfParagraph p, List<bool> wraps)
        {
            var d = EmfText.Dir(p.Angle);
            Func<PointF, PointF, double> across = (a, b) => -(a.X - b.X) * d.Y + (a.Y - b.Y) * d.X;
            var pct = new List<double>();
            for (int i = 0; i + 1 < p.Lines.Count; i++) pct.Add(EditorRender.LineSpacingPct(t, across(p.Lines[i + 1].Start, p.Lines[i].Start)));
            var soft = pct.Where((v, i) => wraps[i]).ToList();
            var hard = pct.Where((v, i) => !wraps[i]).ToList();
            // the font's own leading (ascent + descent + line gap, as Affinity's automatic leading) in Label Maker's measure
            double own = OwnLeadingPct(t);
            double leading = soft.Count > 0 ? soft.Average() : hard.Min() <= own * 1.1 ? hard.Min() : own;
            if (hard.Count > 0) leading = Math.Min(leading, hard.Min());
            t.LineSpacing = TextObject.LimitLineSpacing(Math.Round(leading, 1));
            double after = hard.Count > 0 ? hard.Average() - leading : 0;
            t.SpaceAfter = after >= 0.5 ? TextObject.LimitParaSpace(Math.Round(after, 1)) : 0;
            if (soft.Count > 0)
            {
                int k = 0;
                var runs = new List<TextRun>();
                foreach (var r in t.FullRuns())
                {
                    var sb = new StringBuilder();
                    foreach (char c in r.Text)
                    {
                        if (c == '\n') { sb.Append(k < wraps.Count && wraps[k] ? ' ' : '\n'); k++; }
                        else sb.Append(c);
                    }
                    runs.Add(r.WithText(sb.ToString()));
                }
                t.SetRuns(runs);
            }
            EditorRender.FitBox(t);
        }

        // GDI+'s line spacing (ascent + descent + line gap) of the object's font, in % of ascent + descent (Arial: 102.9).
        static double OwnLeadingPct(TextObject t)
        {
            try
            {
                using (var fam = new FontFamily(t.Font))
                {
                    var st = t.Bold ? FontStyle.Bold : FontStyle.Regular;
                    if (!fam.IsStyleAvailable(st)) st = FontStyle.Regular;
                    double h = fam.GetCellAscent(st) + fam.GetCellDescent(st);
                    return h > 0 ? Math.Round(fam.GetLineSpacing(st) / h * 100, 1) : 100;
                }
            }
            catch (ArgumentException) { return 100; }
        }

        // The text object widened to the frame (left edge and width) and down to its bottom; the text stays where it is.
        // The frame's size: the copy's, but for right aligned or centred lines the width their right edges or centres give
        // (the copy may reach beyond the frame: Affinity's right aligned frame text came out 16 px wider than its frame).
        static SizeF FrameWidth(SizeF copy, EmfParagraph p)
        {
            if (p.Angle != 0 || p.Align == TextAlign.Left || p.Align == TextAlign.Justify) return copy;
            double w = p.Align == TextAlign.Right ? p.Lines.Max(l => l.Start.X + l.Width) : 2 * p.Lines.Average(l => l.Start.X + l.Width / 2);
            return w > 0 && w <= copy.Width + 0.5 ? new SizeF((float)w, copy.Height) : copy;
        }

        static void ToFrame(TextObject t, SizeF frame)
        {
            if (frame.Width <= 0 || frame.Height <= 0) return;
            double w0 = t.W, x0 = t.X, h0 = t.H;
            t.X = Math.Round(-BoxPadding / 2, 2);
            t.W = Math.Round(frame.Width + BoxPadding, 1);
            t.H = Math.Max(t.H, Math.Round(frame.Height + BoxPadding / 2 - t.Y, 1));
            if (EditorRender.Overflows(t)) { t.X = x0; t.W = w0; t.H = h0; }   // other metrics: the text's own box
        }

        static XmlDocument Load(byte[] source)
        {
            if (source == null || source.Length == 0 || source.Length > MaxBytes || SvgPicture.TooDeep(source)) return null;
            var doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            try
            {
                using (var ms = new MemoryStream(source))
                using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaxBytes }))
                    doc.Load(reader);
            }
            catch (XmlException) { return null; }
            return doc.DocumentElement != null && doc.DocumentElement.LocalName == "svg" ? doc : null;
        }

        // ---------- the tree ----------

        // user units of the root -> mm on the picture: the viewBox stretched onto width x height (px = 1/96 inch), or onto
        // the size it was copied at
        static double[] RootMatrix(XmlElement root, SizeF? sizeMm, int[] resolutions)
        {
            double[] box = ViewBox(root.GetAttribute("viewBox"));
            double w = Length(root.GetAttribute("width"), box != null ? box[2] : 0);
            double h = Length(root.GetAttribute("height"), box != null ? box[3] : 0);
            double bw = box != null ? box[2] : w, bh = box != null ? box[3] : h;
            double kx = 25.4 / 96 * (box != null && w > 0 ? w / box[2] : 1), ky = 25.4 / 96 * (box != null && h > 0 ? h / box[3] : 1);
            if (sizeMm.HasValue && bw > 0 && bh > 0 && sizeMm.Value.Width > 0 && sizeMm.Value.Height > 0)
            {
                kx = sizeMm.Value.Width / bw;
                ky = sizeMm.Value.Height / bh;
                // by the longer side (a whole px is less of it); the same both ways
                double k = bw >= bh ? kx : ky;
                if (resolutions != null && box == null)
                    foreach (int r in resolutions)
                        if (Math.Abs(25.4 / k - r) <= 0.01 * r) { kx = ky = 25.4 / r; break; }
            }
            return box == null ? new[] { kx, 0, 0, ky, 0, 0 } : new[] { kx, 0, 0, ky, -box[0] * kx, -box[1] * ky };
        }

        // A filled rectangle without a stroke: maybe a line's underline (Affinity draws underlines as rects).
        sealed class Bar
        {
            public XmlElement Element;
            public double Left, Top, Right, Bottom;   // mm, unturned only
        }

        static void Walk(XmlElement el, double[] m, List<Run> runs, List<XmlElement> texts, List<Bar> bars, SvgTextResult result, bool hidden,
                         SvgQuirks quirks, XmlElement group)
        {
            foreach (var child in el.ChildNodes.OfType<XmlElement>())
            {
                string name = child.LocalName;
                if (NoDrawing.Contains(name)) continue;
                bool off = hidden || Prop(child, "display", false) == "none";
                var cm = Multiply(m, Transform(child.GetAttribute("transform")));
                if (name == "text")
                {
                    if (off) continue;
                    int before = runs.Count;
                    if (TextRuns(child, cm, runs)) texts.Add(child); else result.Skipped++;
                    for (int i = before; i < runs.Count; i++) runs[i].Group = group;
                }
                else if (name == "rect" && !off && Math.Abs(cm[1]) < 1e-9 && Math.Abs(cm[2]) < 1e-9)
                {
                    string stroke = Prop(child, "stroke", true);
                    double x = Length(child.GetAttribute("x"), 0, 16, true), y = Length(child.GetAttribute("y"), 0, 16, true);
                    double w = Length(child.GetAttribute("width"), 0), h = Length(child.GetAttribute("height"), 0);
                    if ((stroke == null || stroke == "none") && Prop(child, "fill", true) != "none" && w > 0 && h > 0)
                    {
                        var a = Apply(cm, x, y); var b = Apply(cm, x + w, y + h);
                        bars.Add(new Bar { Element = child, Left = Math.Min(a[0], b[0]), Right = Math.Max(a[0], b[0]), Top = Math.Min(a[1], b[1]), Bottom = Math.Max(a[1], b[1]) });
                    }
                }
                else if (!Drawing.Contains(name))
                {
                    var elements = child.ChildNodes.OfType<XmlElement>().ToList();
                    bool textGroup = quirks.TextGroups && name == "g" && elements.Count(e => e.LocalName == "text") >= 2
                                     && elements.All(e => e.LocalName == "text" || e.LocalName == "rect");
                    Walk(child, cm, runs, texts, bars, result, off, quirks, textGroup ? child : group);
                }
            }
        }

        // Anything visible left that draws (text read and underlines are gone by then).
        static bool Draws(XmlElement el, bool hidden)
        {
            foreach (var child in el.ChildNodes.OfType<XmlElement>())
            {
                if (NoDrawing.Contains(child.LocalName)) continue;
                bool off = hidden || Prop(child, "display", false) == "none";
                if ((Drawing.Contains(child.LocalName) || child.LocalName == "text") && !off) return true;
                if (Draws(child, off)) return true;
            }
            return false;
        }

        // The bars that underline a line: just below its baseline (up to a quarter em), thin (under a tenth em), from its
        // first run's start to its last run's end (a tenth em either way). Those runs get underlined.
        static List<Bar> Underlines(List<Run> runs, List<Bar> bars)
        {
            var found = new List<Bar>();
            foreach (var bar in bars)
            {
                var on = runs.Where(r => r.Emf.Angle == 0 && bar.Bottom - bar.Top < 0.1 * r.Emf.Em
                                         && bar.Top >= r.Emf.Start.Y - 0.02 * r.Emf.Em && bar.Top <= r.Emf.Start.Y + 0.25 * r.Emf.Em
                                         && r.Emf.Start.X >= bar.Left - 0.1 * r.Emf.Em && r.Emf.End.X <= bar.Right + 0.1 * r.Emf.Em).ToList();
                if (on.Count == 0) continue;
                float em = on.Max(r => r.Emf.Em);
                if (Math.Abs(on.Min(r => r.Emf.Start.X) - bar.Left) > 0.1 * em || Math.Abs(on.Max(r => r.Emf.End.X) - bar.Right) > 0.1 * em) continue;
                foreach (var r in on) r.Emf.Underline = true;
                found.Add(bar);
            }
            return found;
        }

        // ---------- one <text> ----------

        sealed class Run
        {
            public XmlElement Style;          // the element whose format the characters have
            public double[] M;                // the text's user units -> mm
            public double X, Y;               // baseline start, user units
            public readonly StringBuilder Text = new StringBuilder();
            public readonly List<double> Xs = new List<double>();   // each character's x where the source gave it, else NaN
            public string Anchor = "start";
            public EmfRun Emf;
            public XmlElement Group;          // the text group it belongs to (SvgQuirks.TextGroups), else null
            public XmlElement Text0;          // its <text> element
            public int Part;                  // laid out apart from other parts (after white characters left out)
            public double Spacing = 100;      // Label Maker's measure: 100 = normal
            public string FontList;
            public double SizeUser;           // font-size in user units
            public readonly List<double> Ls = new List<double>();   // each character's letter-spacing, user units
            public FontStyle FontStyle;
            public string Face;
        }

        // A list attribute (x, y, dx, dy) with how many of its entries are used.
        sealed class Positions
        {
            public double[] X, Y, Dx, Dy;
            public int Used;
            public XmlElement Owner;
        }

        // false: something this reader doesn't take (textPath, vertical text, rotate, textLength)
        static bool TextRuns(XmlElement text, double[] m, List<Run> runs)
        {
            if (Prop(text, "writing-mode", true) != null && !Prop(text, "writing-mode", true).StartsWith("lr", StringComparison.Ordinal)
                && Prop(text, "writing-mode", true) != "horizontal-tb") return false;
            var chars = new List<KeyValuePair<char, XmlElement>>();
            var stacks = new List<List<Positions>>();
            if (!Collect(text, new List<Positions>(), chars, stacks)) return false;
            Whitespace(text, chars, stacks);
            if (chars.Count == 0) return true;

            double x = 0, y = 0;
            Run run = null;
            for (int i = 0; i < chars.Count; i++)
            {
                char ch = chars[i].Key;
                var style = chars[i].Value;
                // the innermost element with an entry left for this character sets it (SVG's rule for x/y/dx/dy lists)
                double? ax = null, ay = null;
                double dx = 0, dy = 0;
                bool gotX = false, gotY = false, gotDx = false, gotDy = false;
                var stack = stacks[i];
                for (int s = stack.Count - 1; s >= 0; s--)
                {
                    var p = stack[s];
                    int k = p.Used;
                    if (!gotX && p.X != null && k < p.X.Length) { ax = p.X[k]; gotX = true; }
                    if (!gotY && p.Y != null && k < p.Y.Length) { ay = p.Y[k]; gotY = true; }
                    if (!gotDx && p.Dx != null && k < p.Dx.Length) { dx = p.Dx[k]; gotDx = true; }
                    if (!gotDy && p.Dy != null && k < p.Dy.Length) { dy = p.Dy[k]; gotDy = true; }
                }
                foreach (var p in stack) p.Used++;

                bool newChunk = ax.HasValue || ay.HasValue;   // an absolute position starts a new text chunk
                if (ax.HasValue) x = ax.Value;
                if (ay.HasValue) y = ay.Value;
                x += dx; y += dy;
                bool sameLine = run != null && !ay.HasValue && dy == 0;
                bool sameFormat = run != null && SameFormat(run.Style, style);
                string anchor = Prop(style, "text-anchor", true) ?? "start";
                // per-character x lists (Affinity) stay one run while the characters follow on one baseline
                if (run == null || !sameFormat || !sameLine || (newChunk && anchor != "start") || anchor != run.Anchor
                    || (dx != 0 && !ax.HasValue))
                {
                    run = new Run { Style = style, M = m, X = x, Y = y, Anchor = Prop(style, "text-anchor", true) ?? "start", Text0 = text };
                    runs.Add(run);
                }
                else if (ax.HasValue && run.Xs.Count > 0 && ax.Value < LastX(run) - 1e-6)
                {
                    // going back on the line: another chunk
                    run = new Run { Style = style, M = m, X = x, Y = y, Anchor = Prop(style, "text-anchor", true) ?? "start", Text0 = text };
                    runs.Add(run);
                }
                run.Text.Append(ch);
                run.Xs.Add(ax.HasValue || run.Xs.Count == 0 ? x : double.NaN);
                // Affinity writes tracking and kerning as letter-spacing per character (a tspan for each change)
                run.Ls.Add(Length(Prop(style, "letter-spacing", true) ?? "", 0, FontSize(style), true));
            }
            return true;
        }

        static double LastX(Run r)
        {
            for (int i = r.Xs.Count - 1; i >= 0; i--) if (!double.IsNaN(r.Xs[i])) return r.Xs[i];
            return r.X;
        }

        static bool Collect(XmlElement el, List<Positions> stack, List<KeyValuePair<char, XmlElement>> chars, List<List<Positions>> stacks)
        {
            if (el.HasAttribute("rotate") || el.HasAttribute("textLength")) return false;
            var p = new Positions { Owner = el, X = List(el, "x"), Y = List(el, "y"), Dx = List(el, "dx"), Dy = List(el, "dy") };
            var here = p.X != null || p.Y != null || p.Dx != null || p.Dy != null ? stack.Concat(new[] { p }).ToList() : stack;
            foreach (XmlNode n in el.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Text || n.NodeType == XmlNodeType.CDATA || n.NodeType == XmlNodeType.Whitespace
                    || n.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    foreach (char c in n.Value) { chars.Add(new KeyValuePair<char, XmlElement>(c, el)); stacks.Add(here); }
                }
                else if (n.NodeType == XmlNodeType.Element)
                {
                    var child = (XmlElement)n;
                    if (child.LocalName == "title" || child.LocalName == "desc") continue;
                    if (child.LocalName != "tspan" && child.LocalName != "a") return false;
                    if (Prop(child, "display", false) == "none") continue;
                    if (!Collect(child, here, chars, stacks)) return false;
                }
            }
            return true;
        }

        // Whitespace as browsers treat it (SvgPicture.Spaces): line breaks go, tabs become spaces, runs of spaces one
        // (xml:space="preserve": kept); none at the start and end. A dropped character still uses up its list entries.
        static void Whitespace(XmlElement text, List<KeyValuePair<char, XmlElement>> chars, List<List<Positions>> stacks)
        {
            bool preserve = text.GetAttribute("xml:space") == "preserve";
            var keepC = new List<KeyValuePair<char, XmlElement>>();
            var keepS = new List<List<Positions>>();
            bool lastSpace = true;
            for (int i = 0; i < chars.Count; i++)
            {
                char c = chars[i].Key;
                if (c == '\r') continue;
                if (c == '\n') { if (!preserve) continue; c = ' '; }
                if (c == '\t') c = ' ';
                if (!preserve && c == ' ' && lastSpace) continue;
                lastSpace = c == ' ';
                keepC.Add(new KeyValuePair<char, XmlElement>(c, chars[i].Value));
                keepS.Add(stacks[i]);
            }
            if (!preserve)
                while (keepC.Count > 0 && keepC[keepC.Count - 1].Key == ' ') { keepC.RemoveAt(keepC.Count - 1); keepS.RemoveAt(keepS.Count - 1); }
            chars.Clear(); chars.AddRange(keepC);
            stacks.Clear(); stacks.AddRange(keepS);
        }

        static bool SameFormat(XmlElement a, XmlElement b)
        {
            if (ReferenceEquals(a, b)) return true;
            foreach (var name in new[] { "font-family", "font-size", "font-weight", "font-style", "text-decoration", "fill" })
                if (Prop(a, name, true) != Prop(b, name, true)) return false;
            return true;
        }

        // ---------- fonts and sizes ----------

        static HashSet<string> Installed()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families) set.Add(f.Name);
            return set;
        }

        static readonly Dictionary<string, string> Generic = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "serif", "Times New Roman" }, { "sans-serif", "Arial" }, { "monospace", "Courier New" }, { "cursive", "Comic Sans MS" },
            { "fantasy", "Impact" }, { "system-ui", "Segoe UI" }
        };

        // The run's font: the first installed name of its font-family list. When none is installed: the substitute, and
        // the list's family name goes to missing (the last named entry: a list may start with a PostScript name).
        static void Pick(Run r, HashSet<string> installed, List<string> missing)
        {
            r.FontList = Prop(r.Style, "font-family", true) ?? "sans-serif";
            string face = null, named = null;
            foreach (var entry in r.FontList.Split(','))
            {
                string name = entry.Trim().Trim('"', '\'').Trim();
                if (name.Length == 0) continue;
                string generic;
                if (Generic.TryGetValue(name, out generic)) { if (face == null && named == null) face = generic; break; }
                if (installed.Contains(name)) { face = name; break; }
                named = name;
            }
            if (face == null)
            {
                face = EmfText.Substitute;
                if (named != null && !missing.Contains(named)) missing.Add(named);
            }
            r.Face = face;
            string weight = Prop(r.Style, "font-weight", true) ?? "normal", slant = Prop(r.Style, "font-style", true) ?? "normal";
            int w;
            bool bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, out w) && w >= 600);
            bool italic = slant == "italic" || slant == "oblique";
            r.FontStyle = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
            r.SizeUser = FontSize(r.Style);
        }

        // The run in mm on the picture (EmfRun), its width from its characters' positions where the source gave them.
        static void Measure(Run r)
        {
            var m = r.M;
            double scale = Math.Sqrt(Math.Abs(m[0] * m[3] - m[1] * m[2]));
            double angle = Math.Atan2(m[1], m[0]) * 180 / Math.PI;
            double emMm = r.SizeUser * scale;
            string text = r.Text.ToString();
            bool underline = (Prop(r.Style, "text-decoration", true) ?? "").Contains("underline");

            // the natural width (GDI+ typographic, as EmfText measures) and the width the source laid out
            double natural = MeasureMm(text, r.Face, r.FontStyle, emMm);
            // letter-spacing comes after each character; the last one's doesn't move anything of this run
            var inner = r.Ls.Take(Math.Max(0, text.Length - 1)).ToList();
            double laid = natural + inner.Sum() * scale;
            // the tracking: the value most characters have (single characters with their own are kerning pairs)
            double tracking = inner.Count == 0 ? 0 : inner.OrderBy(v => v).ElementAt(inner.Count / 2) * scale;
            int lastKnown = -1;
            for (int i = r.Xs.Count - 1; i >= 0; i--) if (!double.IsNaN(r.Xs[i])) { lastKnown = i; break; }
            if (lastKnown > 0)
            {
                // from the first to the last given position, plus the rest measured
                double tail = MeasureMm(text.Substring(lastKnown), r.Face, r.FontStyle, emMm);
                laid = (r.Xs[lastKnown] - r.Xs[0]) * scale + tail;
            }
            double spaceMm = SpaceMm(r.Face, r.FontStyle, emMm);
            int gaps = text.Length - 1;
            if (gaps > 0 && spaceMm > 0)
            {
                double extra = lastKnown > 0 ? (laid - natural) / gaps : tracking;   // per gap, mm
                r.Spacing = 100 + extra / spaceMm * 100;
                if (Math.Abs(extra) < 0.01 * emMm) r.Spacing = 100;   // rounding of the source's positions
            }

            double startX = r.X;
            if (r.Anchor == "middle") startX -= laid / scale / 2;
            else if (r.Anchor == "end") startX -= laid / scale;
            var start = Apply(m, startX, r.Y);

            float ascent = (float)emMm * 0.9f, descent = (float)emMm * 0.2f;
            try
            {
                using (var fam = new FontFamily(r.Face))
                {
                    var st = fam.IsStyleAvailable(r.FontStyle) ? r.FontStyle : FontStyle.Regular;
                    double em = fam.GetEmHeight(st);
                    ascent = (float)(fam.GetCellAscent(st) / em * emMm);
                    descent = (float)(fam.GetCellDescent(st) / em * emMm);
                }
            }
            catch (ArgumentException) { }

            r.Emf = new EmfRun
            {
                Text = text, Font = r.Face, SizePt = Math.Round(emMm / 25.4 * 72, 2),
                Bold = (r.FontStyle & FontStyle.Bold) != 0, Italic = (r.FontStyle & FontStyle.Italic) != 0, Underline = underline,
                Start = new PointF((float)start[0], (float)start[1]), Width = (float)laid, Angle = Math.Round(angle, 2),
                Ascent = ascent, Descent = descent
            };
        }

        static double MeasureMm(string text, string face, FontStyle style, double emMm)
        {
            if (text.Length == 0 || emMm <= 0) return 0;
            try
            {
                using (var bmp = new Bitmap(1, 1))
                using (var g = Graphics.FromImage(bmp))
                using (var f = new Font(face, (float)emMm, style, GraphicsUnit.Millimeter))
                using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    g.PageUnit = GraphicsUnit.Millimeter;
                    format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                    return g.MeasureString(text, f, PointF.Empty, format).Width;
                }
            }
            catch (ArgumentException) { return text.Length * emMm * 0.55; }
        }

        static double SpaceMm(string face, FontStyle style, double emMm)
        {
            return MeasureMm("  ", face, style, emMm) - MeasureMm(" ", face, style, emMm);
        }

        // font-size in user units: a number, px, pt, em or % of the parent's (16 at the root, as in browsers)
        static double FontSize(XmlElement el)
        {
            if (el == null) return 16;
            double parent = el.ParentNode is XmlElement ? FontSize((XmlElement)el.ParentNode) : 16;
            string s = Prop(el, "font-size", false);
            if (s == null || s == "inherit") return parent;
            double v;
            if (s.EndsWith("%") && double.TryParse(s.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return parent * v / 100;
            double px = Length(s, -1, parent);
            return px > 0 ? px : parent;
        }

        // ---------- styles ----------

        // A property of the element (its style attribute before its presentation attribute); inherited: or its ancestors'.
        static string Prop(XmlElement el, string name, bool inherited)
        {
            for (var n = el; n != null; n = inherited ? n.ParentNode as XmlElement : null)
            {
                string v = FromStyle(n.GetAttribute("style"), name);
                if (v == null && n.HasAttribute(name)) v = n.GetAttribute(name).Trim();
                if (v != null && v != "inherit") return v;
            }
            return null;
        }

        static string FromStyle(string style, string name)
        {
            if (string.IsNullOrEmpty(style)) return null;
            string found = null;
            foreach (var decl in SplitDeclarations(style))
            {
                int colon = decl.IndexOf(':');
                if (colon <= 0) continue;
                if (decl.Substring(0, colon).Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                    found = decl.Substring(colon + 1).Replace("!important", "").Trim();
            }
            return found;
        }

        // style="a:b;c:'x;y'": split at semicolons outside quotes
        static IEnumerable<string> SplitDeclarations(string style)
        {
            var sb = new StringBuilder();
            char quote = '\0';
            foreach (char c in style)
            {
                if (quote != '\0') { if (c == quote) quote = '\0'; }
                else if (c == '\'' || c == '"') quote = c;
                else if (c == ';') { yield return sb.ToString(); sb.Clear(); continue; }
                sb.Append(c);
            }
            if (sb.Length > 0) yield return sb.ToString();
        }

        // ---------- numbers, lengths, transforms ----------

        static double[] List(XmlElement el, string name)
        {
            if (!el.HasAttribute(name)) return null;
            var parts = el.GetAttribute(name).Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<double>();
            foreach (var p in parts)
            {
                double v = Length(p, double.NaN, 16, true);
                if (double.IsNaN(v)) return null;
                list.Add(v);
            }
            return list.Count > 0 ? list.ToArray() : null;
        }

        // A length in user units (px = 1; mm, cm, in, pt, pc; em = font size); a percentage or nothing: fallback.
        static double Length(string s, double fallback, double em = 16, bool signed = false)
        {
            s = (s ?? "").Trim();
            if (s.Length == 0 || s.EndsWith("%") || s == "normal") return fallback;
            string[] units = { "px", "pt", "pc", "mm", "cm", "in", "em", "ex" };
            double[] k = { 1, 96 / 72.0, 16, 96 / 25.4, 96 / 2.54, 96, em, em / 2 };
            double f = 1;
            for (int i = 0; i < units.Length; i++)
                if (s.EndsWith(units[i], StringComparison.OrdinalIgnoreCase)) { f = k[i]; s = s.Substring(0, s.Length - 2).Trim(); break; }
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return fallback;
            return signed || v > 0 || fallback == 0 ? v * f : fallback;
        }

        static double[] ViewBox(string s)
        {
            var parts = (s ?? "").Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return null;
            var n = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return null;
            return n[2] > 0 && n[3] > 0 ? n : null;
        }

        static readonly double[] Identity = { 1, 0, 0, 1, 0, 0 };

        // An SVG transform list as a matrix (a b c d e f); anything unreadable: identity for that part.
        internal static double[] Transform(string s)
        {
            var m = Identity;
            if (string.IsNullOrWhiteSpace(s)) return m;
            int i = 0;
            while (i < s.Length)
            {
                int open = s.IndexOf('(', i);
                if (open < 0) break;
                int close = s.IndexOf(')', open);
                if (close < 0) break;
                string name = s.Substring(i, open - i).Trim(' ', ',', '\t', '\r', '\n');
                var a = s.Substring(open + 1, close - open - 1).Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(x => { double v; return double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : 0; }).ToArray();
                double[] t = null;
                switch (name)
                {
                    case "matrix": if (a.Length == 6) t = a; break;
                    case "translate": if (a.Length >= 1) t = new[] { 1, 0, 0, 1, a[0], a.Length > 1 ? a[1] : 0 }; break;
                    case "scale": if (a.Length >= 1) t = new[] { a[0], 0, 0, a.Length > 1 ? a[1] : a[0], 0, 0 }; break;
                    case "rotate":
                        if (a.Length >= 1)
                        {
                            double r = a[0] * Math.PI / 180, c = Math.Cos(r), sn = Math.Sin(r);
                            t = new[] { c, sn, -sn, c, 0, 0 };
                            if (a.Length >= 3) t = Multiply(Multiply(new[] { 1, 0, 0, 1, a[1], a[2] }, t), new[] { 1, 0, 0, 1, -a[1], -a[2] });
                        }
                        break;
                    case "skewX": if (a.Length >= 1) t = new[] { 1, 0, Math.Tan(a[0] * Math.PI / 180), 1, 0, 0 }; break;
                    case "skewY": if (a.Length >= 1) t = new[] { 1, Math.Tan(a[0] * Math.PI / 180), 0, 1, 0, 0 }; break;
                }
                if (t != null) m = Multiply(m, t);
                i = close + 1;
            }
            return m;
        }

        // m then n: a point goes through n first (SVG: the parent's matrix times the child's)
        internal static double[] Multiply(double[] m, double[] n)
        {
            return new[]
            {
                m[0] * n[0] + m[2] * n[1], m[1] * n[0] + m[3] * n[1],
                m[0] * n[2] + m[2] * n[3], m[1] * n[2] + m[3] * n[3],
                m[0] * n[4] + m[2] * n[5] + m[4], m[1] * n[4] + m[3] * n[5] + m[5]
            };
        }

        static double[] Apply(double[] m, double x, double y)
        {
            return new[] { m[0] * x + m[2] * y + m[4], m[1] * x + m[3] * y + m[5] };
        }
    }
}
