// Label editor, one object kind: text: the text object, its date/time fields, drawing and line breaking, the settings rows and the insert commands (artistic text, text frame).
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
    enum TextAlign { Left, Center, Right }

    // A single-line text: the box always fits the text, the handles make the font larger or smaller. A text frame:
    // the box is set with the handles, the text wraps inside it at its font size (what doesn't fit is cut off).
    enum TextKind { Line, Frame }

    // One stretch of a text with its own format (step 4: words in bold, another font or size). A null field means "as
    // the text object". Line breaks are "\n" inside Text. Immutable: TextObject.Clone shares the runs list, so a change
    // always makes a new list (SetRuns).
    sealed class TextRun
    {
        public readonly string Text;
        public readonly string Font;          // null: the object's font
        public readonly double? Size;         // points; null: the object's size
        public readonly bool? Bold, Italic, Underline;

        public TextRun(string text, string font = null, double? size = null, bool? bold = null, bool? italic = null, bool? underline = null)
        {
            Text = text ?? ""; Font = font; Size = size; Bold = bold; Italic = italic; Underline = underline;
        }

        public TextRun WithText(string text) { return new TextRun(text, Font, Size, Bold, Italic, Underline); }

        public bool SameFormat(TextRun o)
        {
            return o != null && Font == o.Font && Size == o.Size && Bold == o.Bold && Italic == o.Italic && Underline == o.Underline;
        }

        public bool Plain { get { return Font == null && Size == null && Bold == null && Italic == null && Underline == null; } }

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["t"] = Text;
            if (Font != null) d["font"] = Font;
            if (Size != null) d["size"] = LabelObject.R(Size.Value);
            if (Bold != null) d["bold"] = Bold.Value;
            if (Italic != null) d["italic"] = Italic.Value;
            if (Underline != null) d["underline"] = Underline.Value;
            return d;
        }

        // null for something that isn't a run (a damaged file)
        internal static TextRun FromJson(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || !(LabelObject.Str(d, "t", null) is string)) return null;
            Func<string, bool?> b = k => d.ContainsKey(k) && d[k] is bool ? (bool?)d[k] : null;
            double size = LabelObject.Num(d, "size", double.NaN);
            return new TextRun(LabelObject.Str(d, "t", ""), LabelObject.Str(d, "font", null),
                               double.IsNaN(size) ? (double?)null : Math.Min(400, Math.Max(2, size)), b("bold"), b("italic"), b("underline"));
        }
    }

    // Editing a text's runs as the panel's text box does it (step 4). Works on filled runs (TextObject.FullRuns) and
    // character positions in the text with "\n" breaks; the result goes back through TextObject.SetRuns.
    static class RunEdit
    {
        // The text box's "\r\n" breaks as "\n" (an older text box wrote "\r\n" into the file).
        public static List<TextRun> Normalized(List<TextRun> full)
        {
            return full.Select(r => r.WithText(r.Text.Replace("\r\n", "\n"))).ToList();
        }

        // The text after an edit: 'removed' characters at 'start' replaced by 'inserted' in the format 'typed' (filled).
        public static List<TextRun> Splice(List<TextRun> full, int start, int removed, string inserted, TextRun typed)
        {
            var r = Cut(full, 0, start);
            if (!string.IsNullOrEmpty(inserted)) r.Add(typed.WithText(inserted));
            r.AddRange(Cut(full, start + removed, int.MaxValue));
            return r;
        }

        // The characters from start to start+length set to a format (change gets each filled run, returns the new one).
        public static List<TextRun> Restyle(List<TextRun> full, int start, int length, Func<TextRun, TextRun> change)
        {
            var r = Cut(full, 0, start);
            r.AddRange(Cut(full, start, start + length).Select(change));
            r.AddRange(Cut(full, start + length, int.MaxValue));
            return r;
        }

        // The format the characters from start to start+length share; a field they don't share is null (mixed). No
        // characters: the format typing at 'start' continues (the character before it, at the very start the first).
        public static TextRun Common(List<TextRun> full, int start, int length)
        {
            var part = length > 0 ? Cut(full, start, start + length) : Cut(full, Math.Max(0, start - 1), Math.Max(1, start));
            if (part.Count == 0) part = full.Take(1).ToList();
            if (part.Count == 0) return null;
            var f = part[0];
            Func<Func<TextRun, object>, bool> same = get => part.All(x => Equals(get(x), get(f)));
            return new TextRun("", same(x => x.Font) ? f.Font : null, same(x => x.Size) ? f.Size : null, same(x => x.Bold) ? f.Bold : null,
                               same(x => x.Italic) ? f.Italic : null, same(x => x.Underline) ? f.Underline : null);
        }

        // The runs' pieces between two character positions (empty pieces left out).
        static List<TextRun> Cut(List<TextRun> full, int from, int to)
        {
            var r = new List<TextRun>();
            int at = 0;
            foreach (var x in full)
            {
                int a = Math.Max(from, at), b = Math.Min(to, at + x.Text.Length);
                if (b > a) r.Add(x.WithText(x.Text.Substring(a - at, b - a)));
                at += x.Text.Length;
            }
            return r;
        }
    }

    sealed class TextObject : LabelObject
    {
        public string Text = "";
        public string Font = "Segoe UI";
        public double Size = 12;     // points
        public bool Bold, Italic, Underline;
        public TextAlign Align = TextAlign.Left;
        public TextKind Kind = TextKind.Line;
        public double LineHeight = 1;    // frame text: line pitch as a multiple of the font's (0.5-3)
        public double Spacing;           // extra space between characters, points (-5 to +20)
        public bool Invert;              // white on a black box (the box + EditorRender.InvertPad on each side)
        // Words with their own format (step 4), null when the whole text has one. Joined they are exactly Text; a Text
        // changed without them (an older program, the plain text box) drops them: see FormatRuns.
        public IList<TextRun> Runs;

        public const double MinLineHeight = 0.5, MaxLineHeight = 3, MinSpacing = -5, MaxSpacing = 20;

        public override string Type { get { return "text"; } }
        protected override string[] Keys { get { return new[] { "text", "font", "size", "bold", "italic", "underline", "align", "kind", "lineHeight", "spacing", "invert", "runs" }; } }

        public FontStyle Style
        {
            get { return (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0) | (Underline ? FontStyle.Underline : 0); }
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["text"] = Text; d["font"] = Font; d["size"] = R(Size);
            d["bold"] = Bold; d["italic"] = Italic; d["underline"] = Underline;
            d["align"] = Align == TextAlign.Center ? "center" : Align == TextAlign.Right ? "right" : "left";
            d["kind"] = Kind == TextKind.Frame ? "frame" : "line";
            d["lineHeight"] = R(LineHeight);
            d["spacing"] = R(Spacing);
            if (Invert) d["invert"] = true;
            var runs = FormatRuns;
            if (runs != null) d["runs"] = runs.Select(r => (object)r.ToJson()).ToArray();
        }

        // The runs when they still fit the text, else null (one format). Readers use this, not Runs.
        public IList<TextRun> FormatRuns
        {
            get { return Runs != null && string.Concat(Runs.Select(r => r.Text)) == Text ? Runs : null; }
        }

        // The whole text as runs with every field filled (font, size, bold, italic, underline): one run when it has one format.
        public List<TextRun> FullRuns()
        {
            var runs = FormatRuns ?? new[] { new TextRun(Text) };
            return runs.Select(r => new TextRun(r.Text, r.Font ?? Font, r.Size ?? Size, r.Bold ?? Bold, r.Italic ?? Italic, r.Underline ?? Underline)).ToList();
        }

        // Sets the text from runs (filled or not): the object's own format becomes the one most characters have, the runs
        // keep only what differs, neighbours with the same format merge, empty runs go. Runs stays null for one format.
        public void SetRuns(IEnumerable<TextRun> runs)
        {
            var full = runs.Where(r => r != null && r.Text.Length > 0)
                           .Select(r => new TextRun(r.Text, r.Font ?? Font, r.Size ?? Size, r.Bold ?? Bold, r.Italic ?? Italic, r.Underline ?? Underline)).ToList();
            Text = string.Concat(full.Select(r => r.Text));
            Runs = null;
            if (full.Count == 0) return;
            var main = full.GroupBy(r => r.Font + "|" + r.Size.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + r.Bold + r.Italic + r.Underline)
                           .OrderByDescending(g => g.Sum(r => r.Text.Length)).First().First();
            Font = main.Font; Size = main.Size.Value; Bold = main.Bold.Value; Italic = main.Italic.Value; Underline = main.Underline.Value;
            var list = new List<TextRun>();
            foreach (var r in full)
            {
                var d = new TextRun(r.Text, r.Font == Font ? null : r.Font, r.Size == Size ? null : r.Size, r.Bold == Bold ? null : r.Bold,
                                    r.Italic == Italic ? null : r.Italic, r.Underline == Underline ? null : r.Underline);
                if (list.Count > 0 && list[list.Count - 1].SameFormat(d)) list[list.Count - 1] = d.WithText(list[list.Count - 1].Text + d.Text);
                else list.Add(d);
            }
            if (list.Count > 1 || !list[0].Plain) Runs = list.AsReadOnly();
        }

        protected override void Read(Dictionary<string, object> d)
        {
            Text = Str(d, "text", ""); Font = Str(d, "font", Font);
            Size = Math.Min(400, Math.Max(2, Num(d, "size", Size)));
            Bold = Bool(d, "bold", false); Italic = Bool(d, "italic", false); Underline = Bool(d, "underline", false);
            string a = Str(d, "align", "left");
            Align = a == "center" ? TextAlign.Center : a == "right" ? TextAlign.Right : TextAlign.Left;
            LineHeight = Math.Min(MaxLineHeight, Math.Max(MinLineHeight, Num(d, "lineHeight", 1)));
            Spacing = Math.Min(MaxSpacing, Math.Max(MinSpacing, Num(d, "spacing", 0)));
            Invert = Bool(d, "invert", false);
            Kind = Str(d, "kind", "line") == "frame" ? TextKind.Frame : TextKind.Line;
            // Only when they join to exactly the text (an older version may have changed the text and kept the runs).
            object raw;
            var list = d.TryGetValue("runs", out raw) && raw is object[] ? ((object[])raw).Select(TextRun.FromJson).ToList() : null;
            Runs = null;
            if (list != null && list.Count > 0 && !list.Contains(null) && string.Concat(list.Select(r => r.Text)) == Text) Runs = list.AsReadOnly();
        }
    }

    // Fields in texts and barcodes, filled in when the label is drawn or printed:
    //   {date} today, {date+30} / {date-1} days from today, {time} now; a format after a colon: {date:yyyy-MM-dd},
    //   {date+7:dd.MM.}, {time:HH:mm:ss} (.NET date formats). Without a format: the short date / time of the user's
    //   Windows settings. {{ and }} are literal braces. Anything else in braces stays as it is.
    static class Fields
    {
        static readonly System.Text.RegularExpressions.Regex Field =
            new System.Text.RegularExpressions.Regex(@"\{\{|\}\}|\{(date|time)([+-]\d{1,5})?(?::([^{}]+))?\}");

        public static string Expand(string text, DateTime now)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return text ?? "";
            return Field.Replace(text, m =>
            {
                if (m.Value == "{{") return "{";
                if (m.Value == "}}") return "}";
                bool date = m.Groups[1].Value == "date";
                string format = m.Groups[3].Success ? m.Groups[3].Value : date ? "d" : "t";
                try
                {
                    var t = m.Groups[2].Success ? now.AddDays(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : now;
                    return t.ToString(format, CultureInfo.CurrentCulture);
                }
                catch (FormatException) { return m.Value; }
                catch (ArgumentOutOfRangeException) { return m.Value; }
            });
        }

        public static bool HasFields(string text) { return text != null && Expand(text, new DateTime(2000, 1, 1)) != text; }

        // Applies f to the text between the fields (escaping a QR code's content must leave the fields as they are).
        public static string MapText(string text, Func<string, string> f)
        {
            var b = new StringBuilder();
            int at = 0;
            foreach (System.Text.RegularExpressions.Match m in Field.Matches(text ?? ""))
            {
                b.Append(f(text.Substring(at, m.Index - at))).Append(m.Value);
                at = m.Index + m.Length;
            }
            return b.Append(f((text ?? "").Substring(at))).ToString();
        }
    }

    static partial class EditorRender
    {
        // colours: fixed begin - the label as it prints (white paper, black print), as in EditorRender.cs
        public static Font FontOf(TextObject t) { return FontOf(t.Font, t.Size, t.Style); }

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

        // A text laid out in mm: its lines (the explicit breaks; a frame text also wraps at its box), each a list of
        // stretches in one font (words with their own format, step 4), with their widths and where each line starts.
        // One font (no runs) gives one stretch per line: the same calls and numbers as before runs existed.
        sealed class Seg
        {
            public string Text;
            public int Font;    // index into TextLayout.Fonts
            public Seg(string text, int font) { Text = text; Font = font; }
        }

        sealed class TextLayout : IDisposable
        {
            public readonly List<Font> Fonts = new List<Font>();
            public readonly List<float> FontAscents = new List<float>(), FontSizes = new List<float>();   // per font: ascent, line height
            public readonly List<List<Seg>> Lines = new List<List<Seg>>();
            public readonly List<float> Widths = new List<float>();
            public readonly List<float> Ascents = new List<float>(), Pitches = new List<float>(), LineSizes = new List<float>();   // per line
            public float Width { get { return Widths.Count == 0 ? 0 : Widths.Max(); } }
            public float Height
            {
                get
                {
                    int n = Lines.Count;
                    if (n == 0) return 0;
                    if (Pitches.All(p => p == Pitches[0]) && LineSizes.All(s => s == LineSizes[0])) return (n - 1) * Pitches[0] + LineSizes[0];
                    float h = 0;
                    for (int i = 0; i < n - 1; i++) h += Pitches[i];
                    return h + LineSizes[n - 1];
                }
            }
            public void Dispose() { foreach (var f in Fonts) f.Dispose(); }
        }

        static float SpacingMm(TextObject t) { return (float)(t.Spacing * 25.4 / 72); }

        // Measured on a 300 dpi surface, the same for the screen and both print resolutions. Fields are filled in per run
        // (now). wrap: the width a line may take (mm), 0 = no wrapping. space: an empty text measures as one space.
        static TextLayout Layout(TextObject t, DateTime now, float wrap, bool space = false)
        {
            var lay = new TextLayout();
            using (var bmp = new Bitmap(1, 1))
            {
                bmp.SetResolution(300, 300);
                using (var g = Graphics.FromImage(bmp))
                using (var fmt = Format())
                {
                    g.PageUnit = GraphicsUnit.Millimeter;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    var keys = new List<string>();
                    Func<TextRun, int> fontOf = r =>
                    {
                        var style = (r.Bold.Value ? FontStyle.Bold : 0) | (r.Italic.Value ? FontStyle.Italic : 0) | (r.Underline.Value ? FontStyle.Underline : 0);
                        string key = r.Font + "|" + r.Size.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + (int)style;
                        int i = keys.IndexOf(key);
                        if (i >= 0) return i;
                        var font = FontOf(r.Font, r.Size.Value, style);
                        var family = font.FontFamily;
                        float em = family.GetEmHeight(font.Style);
                        lay.Fonts.Add(font);
                        lay.FontSizes.Add(font.Size * family.GetLineSpacing(font.Style) / em);
                        lay.FontAscents.Add(font.Size * family.GetCellAscent(font.Style) / em);
                        keys.Add(key);
                        return keys.Count - 1;
                    };
                    var runs = t.FullRuns();
                    int main = fontOf(runs[0]);
                    var pieces = runs.Select(r => new Seg(Fields.Expand(r.Text, now).Replace("\r\n", "\n"), fontOf(r))).ToList();
                    if (space && pieces.All(p => p.Text.Length == 0)) pieces = new List<Seg> { new Seg(" ", main) };
                    float spacing = SpacingMm(t);
                    Func<List<Seg>, float> width = line => LineWidth(g, lay.Fonts, fmt, line, spacing);

                    // Paragraphs at the line breaks; an empty one keeps the font it is in (its height).
                    var paras = new List<List<Seg>> { new List<Seg>() };
                    var leads = new List<int> { pieces.Count > 0 ? pieces[0].Font : main };
                    foreach (var p in pieces)
                    {
                        var parts = p.Text.Split('\n');
                        for (int k = 0; k < parts.Length; k++)
                        {
                            if (k > 0) { paras.Add(new List<Seg>()); leads.Add(p.Font); }
                            Append(paras[paras.Count - 1], parts[k], p.Font);
                        }
                    }
                    float lh = (float)(t.Kind == TextKind.Frame ? t.LineHeight : 1);
                    for (int k = 0; k < paras.Count; k++)
                        foreach (var line in wrap > 0 ? Wrap(paras[k], wrap, width) : new List<List<Seg>> { paras[k] })
                        {
                            var used = line.Where(s => s.Text.Length > 0).Select(s => s.Font).Distinct().ToList();
                            if (used.Count == 0) used.Add(line.Count > 0 ? line[0].Font : leads[k]);
                            float size = used.Max(f => lay.FontSizes[f]);
                            lay.Lines.Add(line);
                            lay.Widths.Add(width(line));
                            lay.Ascents.Add(used.Max(f => lay.FontAscents[f]));
                            lay.LineSizes.Add(size);
                            lay.Pitches.Add(size * lh);
                        }
                }
            }
            return lay;
        }

        static Font FontOf(string family, double points, FontStyle style)
        {
            return new Font(family, (float)(points * 25.4 / 72), style, GraphicsUnit.World);   // world unit = mm
        }

        // Adds text in a font to a line: to its last stretch when that has the same font.
        static void Append(List<Seg> line, string text, int font)
        {
            if (line.Count > 0 && line[line.Count - 1].Font == font) line[line.Count - 1] = new Seg(line[line.Count - 1].Text + text, font);
            else if (text.Length > 0 || line.Count == 0) line.Add(new Seg(text, font));
        }

        static List<Seg> Concat(List<Seg> a, List<Seg> b)
        {
            var line = new List<Seg>();
            foreach (var s in a.Concat(b)) Append(line, s.Text, s.Font);
            return line;
        }

        static bool HasText(List<Seg> line) { return line.Any(s => s.Text.Length > 0); }

        // Without the spaces at the end (an empty line keeps its first stretch's font).
        static List<Seg> TrimEnd(List<Seg> line)
        {
            var r = line.Select(s => new Seg(s.Text, s.Font)).ToList();
            while (r.Count > 0)
            {
                var last = r[r.Count - 1];
                last.Text = last.Text.TrimEnd(' ');
                if (last.Text.Length > 0 || r.Count == 1) break;
                r.RemoveAt(r.Count - 1);
            }
            return r;
        }

        static float LineWidth(Graphics g, List<Font> fonts, StringFormat fmt, List<Seg> line, float spacing)
        {
            if (!HasText(line)) return 0;
            float w = 0;
            int elements = 0;
            foreach (var s in line)
            {
                if (s.Text.Length == 0) continue;
                w += g.MeasureString(s.Text, fonts[s.Font], PointF.Empty, fmt).Width;
                elements += Elements(s.Text).Count;
            }
            return Math.Max(0, w + spacing * (elements - 1));
        }

        // The characters as the user sees them (a letter with its accents, a surrogate pair: one element).
        static List<string> Elements(string s)
        {
            var list = new List<string>();
            var e = StringInfo.GetTextElementEnumerator(s);
            while (e.MoveNext()) list.Add((string)e.Current);
            return list;
        }

        // Breaks a paragraph into lines no wider than max: at spaces (a word may change its format inside), a word wider
        // than a line at characters.
        static List<List<Seg>> Wrap(List<Seg> para, float max, Func<List<Seg>, float> width)
        {
            // The words, each with the spaces after it, across the stretches.
            var tokens = new List<List<Seg>>();
            var cur = new List<Seg>();
            foreach (var s in para)
                foreach (var part in Regex.Split(s.Text, "(?<= )"))
                {
                    if (part.Length == 0) continue;
                    Append(cur, part, s.Font);
                    if (part.EndsWith(" ")) { tokens.Add(cur); cur = new List<Seg>(); }
                }
            if (HasText(cur)) tokens.Add(cur);

            int lead = para.Count > 0 ? para[0].Font : 0;
            var lines = new List<List<Seg>>();
            var line = new List<Seg>();
            foreach (var token in tokens)
            {
                if (HasText(line) && width(TrimEnd(Concat(line, token))) > max)
                {
                    lines.Add(TrimEnd(line));
                    line = new List<Seg>();
                }
                var word = token;
                while (!HasText(line) && width(TrimEnd(word)) > max)
                {
                    var chars = TrimEnd(word).SelectMany(s => Elements(s.Text).Select(e => new Seg(e, s.Font))).ToList();
                    if (chars.Count < 2) break;
                    int n = 1;
                    while (n < chars.Count && width(Concat(new List<Seg>(), chars.Take(n + 1).ToList())) <= max) n++;
                    var head = Concat(new List<Seg>(), chars.Take(n).ToList());
                    lines.Add(head);
                    word = Drop(word, chars.Take(n).Sum(c => c.Text.Length));
                }
                line = Concat(line, word);
            }
            lines.Add(HasText(line) ? TrimEnd(line) : new List<Seg> { new Seg("", line.Count > 0 ? line[0].Font : lead) });
            return lines;
        }

        // The stretches without their first 'chars' characters.
        static List<Seg> Drop(List<Seg> line, int chars)
        {
            var r = new List<Seg>();
            foreach (var s in line)
            {
                int cut = Math.Min(chars, s.Text.Length);
                chars -= cut;
                if (cut < s.Text.Length) r.Add(new Seg(s.Text.Substring(cut), s.Font));
            }
            return r;
        }

        static void DrawText(Graphics g, TextObject t, RectangleF box, DateTime now, Brush ink)
        {
            var inner = RectangleF.Inflate(box, -TextPad, -TextPad);
            bool frame = t.Kind == TextKind.Frame;
            using (var lay = Layout(t, now, frame ? Math.Max(0.1f, inner.Width / WidthMargin) : 0))
            using (var fmt = Format())
            {
                float spacing = SpacingMm(t);
                // A frame cuts off what doesn't fit (the canvas marks it); a single line sits in the middle of its box.
                if (frame) g.IntersectClip(box);
                float y = frame ? inner.Top : inner.Top + (inner.Height - lay.Height) / 2;
                var plain = new Dictionary<int, Font>();   // the fonts without underline (drawn character by character)
                try
                {
                    for (int i = 0; i < lay.Lines.Count; y += lay.Pitches[i], i++)
                    {
                        var line = lay.Lines[i];
                        float w = lay.Widths[i];
                        float x = t.Align == TextAlign.Center ? inner.Left + (inner.Width - w) / 2 : t.Align == TextAlign.Right ? inner.Right - w : inner.Left;
                        int c = 0;   // characters before this stretch in the line (the spacing before each)
                        int segs = line.Count(s => s.Text.Length > 0);
                        foreach (var s in line)
                        {
                            if (s.Text.Length == 0) continue;
                            var font = lay.Fonts[s.Font];
                            float asc = lay.FontAscents[s.Font];
                            float top = asc == lay.Ascents[i] ? y : y + (lay.Ascents[i] - asc);   // on the line's baseline
                            float sw = g.MeasureString(s.Text, font, PointF.Empty, fmt).Width;
                            if (spacing == 0)
                            {
                                g.DrawString(s.Text, font, ink, x, top, fmt);
                                x += sw;
                                continue;
                            }
                            // Character by character: each at its place in the line plus the spacing before it.
                            Font p;
                            if (!plain.TryGetValue(s.Font, out p)) plain[s.Font] = p = new Font(font, font.Style & ~FontStyle.Underline);
                            var chars = Elements(s.Text);
                            string before = "";
                            for (int k = 0; k < chars.Count; k++)
                            {
                                float at = before.Length == 0 ? 0 : g.MeasureString(before, p, PointF.Empty, fmt).Width;
                                g.DrawString(chars[k], p, ink, x + at + (c + k) * spacing, top, fmt);
                                before += chars[k];
                            }
                            // the stretch's width with the spacing after each of its characters (not after the line's last)
                            float span = segs == 1 ? w : sw + spacing * chars.Count - (s == line.Last(q => q.Text.Length > 0) ? spacing : 0);
                            if (font.Underline && span > 0)
                                g.FillRectangle(ink, x + c * spacing, top + asc + font.Size * 0.1f, span, Math.Max(font.Size * 0.06f, 0.1f));
                            x += sw;
                            c += chars.Count;
                        }
                    }
                }
                finally { foreach (var f in plain.Values) f.Dispose(); }
            }
        }

        // A frame text whose lines don't all fit its box: the rest is cut off (the canvas marks the box in red).
        public static bool Overflows(TextObject t)
        {
            if (t.Kind != TextKind.Frame) return false;
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            float w = (float)(turned ? t.H : t.W) - 2 * TextPad, h = (float)(turned ? t.W : t.H) - 2 * TextPad;
            using (var lay = Layout(t, DateTime.Now, Math.Max(0.1f, w / WidthMargin)))
                return lay.Height > h + 0.01f || lay.Width * WidthMargin > w + 0.01f;
        }

        // The lines as laid out in the box (a frame wraps), as plain text (tests).
        internal static List<string> LinesOf(TextObject t)
        {
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            float w = (float)(turned ? t.H : t.W) - 2 * TextPad;
            using (var lay = Layout(t, DateTime.Now, t.Kind == TextKind.Frame ? Math.Max(0.1f, w / WidthMargin) : 0))
                return lay.Lines.Select(l => string.Concat(l.Select(x => x.Text))).ToList();
        }

        // Sets the box around the text at its size, without wrapping (keeps the box's centre for rotated text, its
        // top left otherwise).
        public static void FitBox(TextObject t)
        {
            float lw, lh;
            using (var lay = Layout(t, DateTime.Now, 0, true)) { lw = lay.Width; lh = lay.Height; }
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            double w = Math.Ceiling((lw * WidthMargin + 2 * TextPad) * 10) / 10, h = Math.Ceiling((lh + 2 * TextPad) * 10) / 10;
            double nw = turned ? h : w, nh = turned ? w : h;
            if (t.Rotation != 0 || t.Tilt != 0) { t.X += (t.W - nw) / 2; t.Y += (t.H - nh) / 2; }
            t.W = nw; t.H = nh;
        }

        // After a change: a single-line text's box follows its text; a text frame keeps the box it was given.
        public static void FitText(TextObject t)
        {
            if (t.Kind == TextKind.Line) FitBox(t);
        }
        // colours: fixed end
    }

    // The B / I / U bar above the text box: the shared look without the frame a free-standing tool strip gets.
    sealed class StyleBarRenderer : ThemeRenderer
    {
        public StyleBarRenderer() { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    // The panel's text box: a RichTextBox, so a paragraph text shows its word formats. Ctrl+B/I/U (and Ctrl+Shift+F/K/U as
    // in German Word) go to FormatKey before the menu (else Ctrl+B would insert a box); RichEdit's own formatting keys
    // (alignment, line spacing, sub/superscript, bullets, capitals) do nothing; a paste is the text only.
    sealed class RunsBox : RichTextBox
    {
        public Action<string> FormatKey;   // "bold", "italic", "underline"

        public RunsBox() { DetectUrls = false; HideSelection = false; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            string format = keyData == (Keys.Control | Keys.B) || keyData == (Keys.Control | Keys.Shift | Keys.F) ? "bold"
                          : keyData == (Keys.Control | Keys.I) || keyData == (Keys.Control | Keys.Shift | Keys.K) ? "italic"
                          : keyData == (Keys.Control | Keys.U) || keyData == (Keys.Control | Keys.Shift | Keys.U) ? "underline" : null;
            if (format != null && FormatKey != null) { FormatKey(format); return true; }
            if (keyData == (Keys.Shift | Keys.Insert)) { PasteText(); return true; }
            if (base.ProcessCmdKey(ref msg, keyData)) return true;
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            if (mods != Keys.Control && mods != (Keys.Control | Keys.Shift)) return false;
            return key == Keys.E || key == Keys.R || key == Keys.J || key == Keys.L || key == Keys.D1 || key == Keys.D2 || key == Keys.D5
                || key == Keys.Oemplus || key == Keys.OemPeriod || key == Keys.Oemcomma || (key == Keys.A && mods != Keys.Control);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0302) { PasteText(); return; }   // WM_PASTE (Edit > Paste): the text only, in the format at the caret
            base.WndProc(ref m);
        }

        void PasteText()
        {
            string text = null;
            try { if (Clipboard.ContainsText()) text = Clipboard.GetText(); }
            catch (Exception) { }   // another program holds the clipboard
            if (text != null) SelectedText = text.Replace("\r\n", "\n");
        }
    }

    sealed partial class EditorForm
    {
        // The settings rows of a text (BuildProperties: they come in this order).
        void BuildTextRows()
        {
            Func<LabelObject, bool> text = o => o is TextObject;
            Func<LabelObject, bool> oneLine = o => o is TextObject && ((TextObject)o).Kind == TextKind.Line,
                frame = o => o is TextObject && ((TextObject)o).Kind == TextKind.Frame;
            // B / I / U above the text box (paragraph text): on the selected words, with none on what is typed next.
            styleBar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, CanOverflow = false, Dock = DockStyle.None, AutoSize = true, Padding = new Padding(0),
                                       Renderer = new StyleBarRenderer() };
            Func<string, FontStyle, string, string, ToolStripButton> styleButton = (letter, look, tip, format) =>
            {
                var b = new ToolStripButton(letter) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip, Tag = look };
                b.Click += delegate { ToggleStyle(b, format, true); };
                styleBar.Items.Add(b);
                return b;
            };
            boldButton = styleButton(Lang.T("B"), FontStyle.Bold, Lang.T("Bold (Ctrl+B)"), "bold");
            italicButton = styleButton(Lang.T("I"), FontStyle.Italic, Lang.T("Italic (Ctrl+I)"), "italic");
            underlineButton = styleButton(Lang.T("U"), FontStyle.Underline, Lang.T("Underline (Ctrl+U)"), "underline");
            Row("", styleBar, frame);
            // Multi-line for a paragraph text; one line for an artistic text (Enter does nothing, pasted line breaks
            // become spaces). It shows a paragraph text's word formats; the text object stays what counts (TypedText).
            textBox = new RunsBox { Multiline = true, Height = S(64), ScrollBars = RichTextBoxScrollBars.Vertical, Dock = DockStyle.Fill };
            textBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && oneLine(canvas.Selected)) e.SuppressKeyPress = true; };
            textBox.TextChanged += (s, e) => TypedText(s);
            textBox.SelectionChanged += (s, e) =>
            {
                // the caret moved without typing: typing continues in the format before it
                if (loading || textBox.TextLength != boxText.Length || textBox.Text != boxText) return;
                typing = null;
                var t = canvas.Selected as TextObject;
                if (t != null) { loading = true; try { FillStyleRows(t); } finally { loading = false; } }
            };
            textBox.FormatKey = format => ToggleStyle(textBox, format, true);
            BarFonts();
            // A new font (another screen's scaling) sets the whole box to it: the word formats again, B/I/U at its size.
            textBox.FontChanged += delegate
            {
                boxShown = null;
                BarFonts();
                if (!textBox.IsHandleCreated) return;
                textBox.BeginInvoke((MethodInvoker)delegate
                {
                    var t = canvas.Selected as TextObject;
                    if (t == null || IsDisposed) return;
                    loading = true;
                    try { ShowRuns(t); } finally { loading = false; }
                });
            };
            Row(Lang.T("Text:"), textBox, text);
            Row("", FieldButton(textBox), text);
            textKindBox = Combo(Lang.T("Artistic text"), Lang.T("Paragraph text"));
            textKindBox.SelectedIndexChanged += (s, e) =>
            {
                // To artistic text with word formats: asks first (they go; Undo brings them back).
                var t = canvas.Selected as TextObject;
                if (!loading && t != null && (TextKind)textKindBox.SelectedIndex == TextKind.Line && t.FormatRuns != null && !AskOneFormat())
                {
                    loading = true;
                    try { textKindBox.SelectedIndex = (int)t.Kind; } finally { loading = false; }
                    return;
                }
                Edit(s, o => SetTextKind((TextObject)o, (TextKind)textKindBox.SelectedIndex));
            };
            Row(Lang.T("Kind:"), textKindBox, text);
            tips.SetToolTip(textKindBox, Lang.T("Artistic text: one line, the box follows the text, the handles change its size.\n" +
                                                "Paragraph text: you set the box, the text wraps inside it; selected words can have their own format."));
            SpanRow(Hint(Lang.T("Drag a handle to make the text larger or smaller.")), oneLine);
            SpanRow(Hint(Lang.T("The text wraps in its box. Selected words can be formatted on their own.")), frame);
            // What the font, size, bold, italic and underline rows act on: the selected words, else the whole text.
            scopeHint = Theme.Fore(Hint(""), Role.Accent);
            SpanRow(scopeHint, o => frame(o) && textBox.SelectionLength > 0);
            // While the text doesn't fit (the canvas draws the box's edge in red).
            var overflow = Hint(Lang.T("The text doesn't fit the box and is cut off: make the box larger or the text smaller."));
            Theme.Fore(overflow, Role.ErrorText);
            SpanRow(overflow, o => o is TextObject && EditorRender.Overflows((TextObject)o));
            fontBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            foreach (var f in FontFamily.Families) fontBox.Items.Add(f.Name);
            fontBox.SelectedIndexChanged += (s, e) => { string name = fontBox.Text; Style(s, r => With(r, font: name), false); };
            fontBox.Leave += (s, e) =>
            {
                string name = fontBox.Text;
                if (fontBox.Items.Contains(name)) Style(s, r => With(r, font: name), false);
            };
            Row(Lang.T("Font:"), fontBox, text);
            sizeNum = Number(2, 400, 1, 1);
            sizeNum.ValueChanged += (s, e) => { double size = (double)sizeNum.Value; Style(s, r => With(r, size: size), false); };
            Row(Lang.T("Size (pt):"), sizeNum, text);
            var style = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            boldCheck = Check(Lang.T("Bold"), (s, e) => ToggleStyle(s, "bold", false));
            italicCheck = Check(Lang.T("Italic"), (s, e) => ToggleStyle(s, "italic", false));
            underlineCheck = Check(Lang.T("Underline"), (s, e) => ToggleStyle(s, "underline", false));
            style.Controls.AddRange(new Control[] { boldCheck, italicCheck, underlineCheck });
            Row("", style, text);
            // wraps only when its column is too narrow: the panel's scroll bar took the room "Underline" needed (German,
            // 96-144 dpi)
            props.Layout += (s, e) =>
            {
                var widths = props.GetColumnWidths();
                int room = props.Width - (widths.Length > 0 ? widths[0] : 0);
                int needed = style.Controls.Cast<Control>().Sum(c => c.PreferredSize.Width + c.Margin.Horizontal);
                bool wrap = room < needed;
                var most = wrap ? new Size(Math.Max(S(80), room), 0) : Size.Empty;
                if (style.WrapContents != wrap) style.WrapContents = wrap;
                if (style.MaximumSize != most) style.MaximumSize = most;
            };
            textInvertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((TextObject)o).Invert = textInvertCheck.Checked));
            tips.SetToolTip(textInvertCheck, Lang.T("White on black: a black box 1 mm larger than the text's frame, the text white in it. It covers what lies below."));
            Row("", textInvertCheck, text);
            alignBox = Combo(Lang.T("Left"), Lang.T("Centre"), Lang.T("Right"));
            alignBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((TextObject)o).Align = (TextAlign)alignBox.SelectedIndex);
            Row(Lang.T("Lines:"), alignBox, frame);
            lineHeightNum = Number((decimal)TextObject.MinLineHeight, (decimal)TextObject.MaxLineHeight, 0.1m, 1);
            lineHeightNum.ValueChanged += (s, e) => Edit(s, o => ((TextObject)o).LineHeight = (double)lineHeightNum.Value);
            Row(Lang.T("Line spacing (×):"), lineHeightNum, frame);
            spacingNum = Number((decimal)TextObject.MinSpacing, (decimal)TextObject.MaxSpacing, 0.5m, 1);
            spacingNum.ValueChanged += (s, e) => Edit(s, o => ((TextObject)o).Spacing = (double)spacingNum.Value);
            Row(Lang.T("Letter spacing (pt):"), spacingNum, text);
        }

        // ---------- the text box and the format rows (step 4: words with their own format) ----------

        RunsBox textBox;
        ToolStrip styleBar;
        ToolStripButton boldButton, italicButton, underlineButton;
        Label scopeHint;
        string boxText = "";      // the text box's text as last applied to the object ("\n" breaks)
        string boxShown;          // the formats the box shows (to set them again only when they changed)
        TextRun typing;           // B/I/U at the caret with nothing selected: what is typed there next (filled), else null
        bool[] mixedBefore = new bool[3];   // bold, italic, underline were mixed when the rows were filled

        // B / I / U in the text box's font (a tool strip doesn't follow the window's font), bold, italic, underlined.
        void BarFonts()
        {
            foreach (ToolStripButton b in styleBar.Items)
            {
                var old = b.Font;
                b.Font = new Font(textBox.Font, (FontStyle)b.Tag);
                if (old != null && old != styleBar.Font) old.Dispose();
            }
        }

        // The object's runs with "\n" breaks, every field filled (what the text box works on).
        static List<TextRun> BoxRuns(TextObject t) { return RunEdit.Normalized(t.FullRuns()); }

        static TextRun Filled(TextRun r, TextObject t)
        {
            return new TextRun("", r == null || r.Font == null ? t.Font : r.Font, r == null || r.Size == null ? t.Size : r.Size,
                               r == null || r.Bold == null ? t.Bold : r.Bold, r == null || r.Italic == null ? t.Italic : r.Italic,
                               r == null || r.Underline == null ? t.Underline : r.Underline);
        }

        static TextRun With(TextRun r, string font = null, double? size = null, bool? bold = null, bool? italic = null, bool? underline = null)
        {
            return new TextRun(r.Text, font ?? r.Font, size ?? r.Size, bold ?? r.Bold, italic ?? r.Italic, underline ?? r.Underline);
        }

        // Typed, deleted or pasted in the text box: the same edit on the runs (the new characters take the format before
        // them, or the one chosen at the caret).
        void TypedText(object sender)
        {
            var t = canvas.Selected as TextObject;
            if (loading || t == null) return;
            string now = textBox.Text;
            if (t.Kind == TextKind.Line && now.IndexOfAny(new[] { '\r', '\n' }) >= 0)
            {
                int at = textBox.SelectionStart;
                textBox.Text = OneLine(now);   // comes back here and is applied
                textBox.SelectionStart = Math.Min(at, textBox.TextLength);
                return;
            }
            string was = boxText;
            int p = 0;
            while (p < was.Length && p < now.Length && was[p] == now[p]) p++;
            int q = 0;
            while (q < was.Length - p && q < now.Length - p && was[was.Length - 1 - q] == now[now.Length - 1 - q]) q++;
            string inserted = now.Substring(p, now.Length - p - q);
            int removed = was.Length - p - q;
            var typed = typing;
            boxText = now;
            typing = null;
            Edit(sender, o =>
            {
                var x = (TextObject)o;
                var full = BoxRuns(x);
                x.SetRuns(RunEdit.Splice(full, p, removed, inserted, typed ?? Filled(RunEdit.Common(full, p, 0), x)));
            });
        }

        // Font and size: on the selected words of a paragraph text, else on the whole text.
        void Style(object sender, Func<TextRun, TextRun> change, bool caret)
        {
            var t = canvas.Selected as TextObject;
            if (loading || t == null) return;
            int start = 0, length = -1;
            if (t.Kind == TextKind.Frame && textBox.SelectionLength > 0) { start = textBox.SelectionStart; length = textBox.SelectionLength; }
            else if (t.Kind == TextKind.Frame && caret && textBox.Focused)
            {
                // nothing selected: the next characters typed at the caret
                typing = change(typing ?? Filled(RunEdit.Common(BoxRuns(t), textBox.SelectionStart, 0), t));
                loading = true;
                try
                {
                    using (var f = BoxFont(typing, t)) textBox.SelectionFont = f;
                    FillStyleRows(t);
                }
                finally { loading = false; }
                return;
            }
            Edit(sender, o =>
            {
                var x = (TextObject)o;
                var full = BoxRuns(x);
                int n = length < 0 ? full.Sum(r => r.Text.Length) : length;
                x.SetRuns(RunEdit.Restyle(full, start, n, change));
            });
        }

        // Bold, italic, underline: off when all of it has it, else on (a mixed check box turns it on).
        void ToggleStyle(object sender, string format, bool caret)
        {
            var t = canvas.Selected as TextObject;
            if (loading || t == null) return;
            int i = format == "bold" ? 0 : format == "italic" ? 1 : 2;
            var box = sender as CheckBox;
            bool on;
            if (box != null) on = mixedBefore[i] || box.Checked;
            else
            {
                var now = Current(t);
                bool? has = i == 0 ? now.Bold : i == 1 ? now.Italic : now.Underline;
                on = has != true;
            }
            Style(sender, r => i == 0 ? With(r, bold: on) : i == 1 ? With(r, italic: on) : With(r, underline: on), caret);
        }

        // The format the rows show: the selected words', with none at a focused caret what is typed next, else the whole text's.
        TextRun Current(TextObject t)
        {
            var full = BoxRuns(t);
            if (t.Kind == TextKind.Frame && textBox.SelectionLength > 0) return RunEdit.Common(full, textBox.SelectionStart, textBox.SelectionLength) ?? Filled(null, t);
            if (t.Kind == TextKind.Frame && textBox.Focused) return typing ?? Filled(RunEdit.Common(full, textBox.SelectionStart, 0), t);
            return RunEdit.Common(full, 0, full.Sum(r => r.Text.Length)) ?? Filled(null, t);
        }

        // The text rows for a text object (FillProps; skip: the control the user is typing in).
        void FillText(TextObject t, object skip)
        {
            bool one = t.Kind == TextKind.Line;
            if (skip != textBox) ShowRuns(t);
            int height = one ? TextRenderer.MeasureText("Ag", textBox.Font).Height + S(8) : S(64);
            if (textBox.Height != height) textBox.Height = height;
            textBox.ScrollBars = one ? RichTextBoxScrollBars.None : RichTextBoxScrollBars.Vertical;
            textBox.WordWrap = !one;
            textKindBox.SelectedIndex = (int)t.Kind;
            if (skip != lineHeightNum) lineHeightNum.Value = Clamp(lineHeightNum, t.LineHeight);
            if (skip != spacingNum) spacingNum.Value = Clamp(spacingNum, t.Spacing);
            textInvertCheck.Checked = t.Invert;
            alignBox.SelectedIndex = (int)t.Align;
            FillStyleRows(t, skip);
        }

        // Font, size, bold, italic, underline and the line above them: mixed values blank or ■.
        void FillStyleRows(TextObject t, object skip = null)
        {
            var c = Current(t);
            if (skip != fontBox && fontBox.Text != (c.Font ?? "")) fontBox.Text = c.Font ?? "";
            if (skip != sizeNum)
            {
                // the text too: after a mixed (blank) value the same Value doesn't write it again
                if (c.Size != null) { sizeNum.Value = Clamp(sizeNum, c.Size.Value); sizeNum.Text = sizeNum.Value.ToString("F" + sizeNum.DecimalPlaces, CultureInfo.CurrentCulture); }
                else sizeNum.Text = "";
            }
            var checks = new[] { boldCheck, italicCheck, underlineCheck };
            var values = new[] { c.Bold, c.Italic, c.Underline };
            var buttons = new[] { boldButton, italicButton, underlineButton };
            for (int i = 0; i < 3; i++)
            {
                mixedBefore[i] = values[i] == null;
                checks[i].CheckState = values[i] == null ? CheckState.Indeterminate : values[i].Value ? CheckState.Checked : CheckState.Unchecked;
                buttons[i].Checked = values[i] == true;
            }
            bool selected = t.Kind == TextKind.Frame && textBox.SelectionLength > 0;
            if (selected)
            {
                string words = textBox.SelectedText.Replace("\n", " ").Trim();
                if (words.Length > 24) words = words.Substring(0, 23) + "…";
                scopeHint.Text = textBox.SelectionLength >= textBox.TextLength ? Lang.T("All text selected: mixed values stay blank or ■")
                                                                               : Lang.F("Applies to the selection: “{0}”", words);
            }
            if (scopeHint.Visible != selected) scopeHint.Visible = selected;
        }

        // The text and, for a paragraph text, its word formats in the text box (the selection stays).
        void ShowRuns(TextObject t)
        {
            var full = BoxRuns(t);
            string text = string.Concat(full.Select(r => r.Text));
            bool frame = t.Kind == TextKind.Frame;
            string shown = frame ? string.Join("|", full.Select(r => r.Text.Length + ":" + r.Size + r.Bold + r.Italic + r.Underline)) + "|" + t.Size : "";
            if (textBox.Text == text && shown == boxShown) { boxText = text; return; }
            int start = textBox.SelectionStart, length = textBox.SelectionLength;
            bool keep = textBox.Text == text;
            if (!keep) { textBox.Text = text; typing = null; }
            textBox.SelectAll();
            textBox.SelectionFont = textBox.Font;
            if (frame)
            {
                int at = 0;
                foreach (var r in full)
                {
                    textBox.Select(at, r.Text.Length);
                    using (var f = BoxFont(r, t)) textBox.SelectionFont = f;
                    at += r.Text.Length;
                }
            }
            if (keep) textBox.Select(start, length);
            else textBox.Select(textBox.TextLength, 0);
            boxText = textBox.Text;
            boxShown = shown;
        }

        // How a run looks in the text box: the panel's font with its bold/italic/underline, a little larger or smaller
        // for a larger or smaller size (the box is no preview: the canvas shows the real fonts).
        Font BoxFont(TextRun r, TextObject t)
        {
            var style = (r.Bold == true ? FontStyle.Bold : 0) | (r.Italic == true ? FontStyle.Italic : 0) | (r.Underline == true ? FontStyle.Underline : 0);
            double ratio = r.Size == null || t.Size <= 0 ? 1 : Math.Min(1.4, Math.Max(0.85, r.Size.Value / t.Size));
            return new Font(textBox.Font.FontFamily, (float)(textBox.Font.Size * ratio), style, textBox.Font.Unit);
        }

        // "Convert to artistic text?" for a paragraph text with word formats. True: convert.
        bool AskOneFormat()
        {
            using (var d = new SetupResult(AppTitle, ResultKind.Warning, Lang.T("Convert to artistic text?"), new KeyValuePair<string, string>[0],
                                           Lang.T("Artistic text has one format for the whole text. Bold, italic, font and size of single words are removed; the whole text gets the first word's format."),
                                           closeText: Lang.T("Cancel")))
            {
                d.AddChoice(Lang.T("Convert"), DialogResult.OK);
                HelpPages.SetPage(d, "text.html");
                return d.ShowDialog(this) == DialogResult.OK;
            }
        }

        void AddText()
        {
            var at = FreeSpot();
            var t = new TextObject { X = at.X, Y = at.Y, Text = Lang.T("Text"), Size = 14 };
            EditorRender.FitText(t);
            Add(t);
            FocusField(textBox);
        }

        void AddTextFrame()
        {
            var at = FreeSpot();
            double w = layout != null ? Math.Max(10, Math.Min(40, layout.Printable.Width - 2)) : 40;
            Add(new TextObject { X = at.X, Y = at.Y, W = w, H = 12, Text = Lang.T("Text"), Size = 12, Kind = TextKind.Frame });
            FocusField(textBox);
        }

        // Single line: line breaks become spaces, the box follows the text (Edit). Text frame: starts with the box
        // around the text, then keeps the box it is given.
        static void SetTextKind(TextObject t, TextKind kind)
        {
            if (t.Kind == kind) return;
            // artistic text has one format: the first word's (AskOneFormat asked)
            if (kind == TextKind.Line && t.FormatRuns != null)
            {
                var first = t.FullRuns()[0];
                t.Runs = null;
                t.Font = first.Font; t.Size = first.Size.Value; t.Bold = first.Bold.Value; t.Italic = first.Italic.Value; t.Underline = first.Underline.Value;
            }
            t.Kind = kind;
            if (kind == TextKind.Line) t.Text = OneLine(t.Text);
            else EditorRender.FitBox(t);
        }
    }
}
