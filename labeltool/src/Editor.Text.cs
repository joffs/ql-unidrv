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
    enum TextAlign { Left, Center, Right, Justify }   // Justify: a paragraph text's wrapped lines fill the frame (the last line of a paragraph stays left, as in Word)

    // A single-line text: the box always fits the text, the handles make the font larger or smaller. A text frame:
    // the box is set with the handles, the text wraps inside it at its font size (what doesn't fit is cut off).
    enum TextKind { Line, Frame }

    // One stretch of a text with its own format (step 4: words in bold, another font or size; letter spacing and
    // white on black per selection, Word's character spacing and shading). A null field means "as the text object".
    // Line breaks are "\n" inside Text. Immutable: TextObject.Clone shares the runs list, so a change always makes a
    // new list (SetRuns).
    sealed class TextRun
    {
        public readonly string Text;
        public readonly string Font;          // null: the object's font
        public readonly double? Size;         // points; null: the object's size
        public readonly bool? Bold, Italic, Underline;
        public readonly double? Spacing;      // letter spacing, points (after each character); null: the object's
        public readonly bool? Invert;         // white on black behind these characters; null: the object's

        public TextRun(string text, string font = null, double? size = null, bool? bold = null, bool? italic = null, bool? underline = null,
                       double? spacing = null, bool? invert = null)
        {
            Text = text ?? ""; Font = font; Size = size; Bold = bold; Italic = italic; Underline = underline; Spacing = spacing; Invert = invert;
        }

        public TextRun WithText(string text) { return new TextRun(text, Font, Size, Bold, Italic, Underline, Spacing, Invert); }

        // The same run with the fields given (the others as they are).
        public TextRun With(string font = null, double? size = null, bool? bold = null, bool? italic = null, bool? underline = null,
                            double? spacing = null, bool? invert = null)
        {
            return new TextRun(Text, font ?? Font, size ?? Size, bold ?? Bold, italic ?? Italic, underline ?? Underline, spacing ?? Spacing, invert ?? Invert);
        }

        public bool SameFormat(TextRun o)
        {
            return o != null && Font == o.Font && Size == o.Size && Bold == o.Bold && Italic == o.Italic && Underline == o.Underline &&
                   Spacing == o.Spacing && Invert == o.Invert;
        }

        public bool Plain
        {
            get { return Font == null && Size == null && Bold == null && Italic == null && Underline == null && Spacing == null && Invert == null; }
        }

        internal Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            d["t"] = Text;
            if (Font != null) d["font"] = Font;
            if (Size != null) d["size"] = LabelObject.R(Size.Value);
            if (Bold != null) d["bold"] = Bold.Value;
            if (Italic != null) d["italic"] = Italic.Value;
            if (Underline != null) d["underline"] = Underline.Value;
            if (Spacing != null) d["spacing"] = LabelObject.R(Spacing.Value);
            if (Invert != null) d["invert"] = Invert.Value;
            return d;
        }

        // null for something that isn't a run (a damaged file)
        internal static TextRun FromJson(object o)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || !(LabelObject.Str(d, "t", null) is string)) return null;
            Func<string, bool?> b = k => d.ContainsKey(k) && d[k] is bool ? (bool?)d[k] : null;
            double size = LabelObject.Num(d, "size", double.NaN), spacing = LabelObject.Num(d, "spacing", double.NaN);
            return new TextRun(LabelObject.Str(d, "t", ""), LabelObject.Str(d, "font", null),
                               double.IsNaN(size) ? (double?)null : Math.Min(400, Math.Max(2, size)), b("bold"), b("italic"), b("underline"),
                               double.IsNaN(spacing) ? (double?)null : Math.Min(TextObject.MaxSpacing, Math.Max(TextObject.MinSpacing, spacing)), b("invert"));
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
                               same(x => x.Italic) ? f.Italic : null, same(x => x.Underline) ? f.Underline : null,
                               same(x => x.Spacing) ? f.Spacing : null, same(x => x.Invert) ? f.Invert : null);
        }

        // The runs' pieces between two character positions (empty pieces left out).
        public static List<TextRun> Slice(List<TextRun> full, int from, int to) { return Cut(full, from, to); }

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
        public double Spacing;           // extra space between characters, points (-5 to +20); runs may have their own
        public bool Invert;              // all of it white on black: an artistic text in a black box (the box +
                                         // EditorRender.InvertPad on each side), a paragraph text in bands behind its lines
                                         // (InvertBox); only part of it: its runs (black behind their characters, Word's shading)
        // Words with their own format (step 4), null when the whole text has one. Joined they are exactly Text; a Text
        // changed without them (an older program, the plain text box) drops them: see FormatRuns.
        public IList<TextRun> Runs;

        public const double MinLineHeight = 0.5, MaxLineHeight = 3, MinSpacing = -5, MaxSpacing = 20;

        public override string Type { get { return "text"; } }

        // The whole text in a black box 1 mm beyond its frame: an inverted artistic text. An inverted paragraph text gets
        // black bands behind its lines instead, the empty part of its frame stays white (2026-11-09).
        public bool InvertBox { get { return Invert && Kind == TextKind.Line; } }
        protected override string[] Keys { get { return new[] { "text", "font", "size", "bold", "italic", "underline", "align", "kind", "lineHeight", "spacing", "invert", "runs" }; } }

        public FontStyle Style
        {
            get { return (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0) | (Underline ? FontStyle.Underline : 0); }
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["text"] = Text; d["font"] = Font; d["size"] = R(Size);
            d["bold"] = Bold; d["italic"] = Italic; d["underline"] = Underline;
            d["align"] = Align == TextAlign.Center ? "center" : Align == TextAlign.Right ? "right" : Align == TextAlign.Justify ? "justify" : "left";
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

        // The whole text as runs with every field filled (font, size, bold, italic, underline, spacing, invert): one run
        // when it has one format.
        public List<TextRun> FullRuns()
        {
            var runs = FormatRuns ?? new[] { new TextRun(Text) };
            return runs.Select(Fill).ToList();
        }

        // A run (null: none) with the fields it leaves open taken from the object.
        public TextRun Fill(TextRun r)
        {
            if (r == null) r = new TextRun("");
            return new TextRun(r.Text, r.Font ?? Font, r.Size ?? Size, r.Bold ?? Bold, r.Italic ?? Italic, r.Underline ?? Underline, r.Spacing ?? Spacing, r.Invert ?? Invert);
        }

        // The object's own format from a filled run (an empty text's format, an undo step).
        public void SetOwn(TextRun f)
        {
            Font = f.Font; Size = f.Size.Value; Bold = f.Bold.Value; Italic = f.Italic.Value; Underline = f.Underline.Value;
            Spacing = f.Spacing.Value; Invert = f.Invert.Value;
        }

        // Sets the text from runs (filled or not): the object's own format becomes the one most characters have (Invert:
        // only when all of them have it, the black box with its margin is the whole text's), the runs keep only what
        // differs, neighbours with the same format merge, empty runs go. Runs stays null for one format.
        public void SetRuns(IEnumerable<TextRun> runs)
        {
            var full = runs.Where(r => r != null && r.Text.Length > 0).Select(Fill).ToList();
            Text = string.Concat(full.Select(r => r.Text));
            Runs = null;
            if (full.Count == 0) return;
            var main = full.GroupBy(r => r.Font + "|" + r.Size.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + r.Bold + r.Italic + r.Underline + "|" +
                                         r.Spacing.Value.ToString("R", CultureInfo.InvariantCulture))
                           .OrderByDescending(g => g.Sum(r => r.Text.Length)).First().First();
            Font = main.Font; Size = main.Size.Value; Bold = main.Bold.Value; Italic = main.Italic.Value; Underline = main.Underline.Value;
            Spacing = main.Spacing.Value;
            Invert = full.All(r => r.Invert.Value);
            var list = new List<TextRun>();
            foreach (var r in full)
            {
                var d = new TextRun(r.Text, r.Font == Font ? null : r.Font, r.Size == Size ? null : r.Size, r.Bold == Bold ? null : r.Bold,
                                    r.Italic == Italic ? null : r.Italic, r.Underline == Underline ? null : r.Underline,
                                    r.Spacing == Spacing ? null : r.Spacing, r.Invert == Invert ? null : r.Invert);
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
            Align = a == "center" ? TextAlign.Center : a == "right" ? TextAlign.Right : a == "justify" ? TextAlign.Justify : TextAlign.Left;
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

        // Where the fields are in a text (start, length), not the {{ and }} escapes: the canvas marks them while editing.
        public static List<KeyValuePair<int, int>> Ranges(string text)
        {
            var list = new List<KeyValuePair<int, int>>();
            foreach (System.Text.RegularExpressions.Match m in Field.Matches(text ?? ""))
                if (m.Value != "{{" && m.Value != "}}") list.Add(new KeyValuePair<int, int>(m.Index, m.Length));
            return list;
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
            public int Font;    // index into TextLayout.Fonts (a font with its letter spacing and invert: a style)
            public int Start;   // where its first character is in the stored text (caret map; -1: not known)
            public Seg(string text, int font, int start = -1) { Text = text; Font = font; Start = start; }
        }

        sealed class TextLayout : IDisposable
        {
            public readonly List<Font> Fonts = new List<Font>();
            public readonly List<float> FontAscents = new List<float>(), FontSizes = new List<float>();   // per font: ascent, line height
            public readonly List<float> Spacings = new List<float>();   // per font: letter spacing (mm, after each character)
            public readonly List<bool> Inverts = new List<bool>();      // per font: white on black
            public readonly List<List<Seg>> Lines = new List<List<Seg>>();
            public readonly List<float> Widths = new List<float>();
            public readonly List<float> AlignWidths = new List<float>();   // per line with the spaces at its end (alignment)
            public readonly List<bool> Ends = new List<bool>();            // per line: the last line of its paragraph (not justified)
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

        // The text being edited on the canvas: its fields are laid out and drawn as their codes ({date+30:dd.MM.}), so every
        // stored character has its place (the caret moves through them 1:1). Null outside editing. The screen only: printing,
        // saving and copying end editing first.
        internal static TextObject Codes { get; set; }

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
                        string key = r.Font + "|" + r.Size.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + (int)style + "|" +
                                     r.Spacing.Value.ToString("R", CultureInfo.InvariantCulture) + "|" + r.Invert.Value;
                        int i = keys.IndexOf(key);
                        if (i >= 0) return i;
                        var font = FontOf(r.Font, r.Size.Value, style);
                        var family = font.FontFamily;
                        float em = family.GetEmHeight(font.Style);
                        lay.Fonts.Add(font);
                        lay.FontSizes.Add(font.Size * family.GetLineSpacing(font.Style) / em);
                        lay.FontAscents.Add(font.Size * family.GetCellAscent(font.Style) / em);
                        lay.Spacings.Add((float)(r.Spacing.Value * 25.4 / 72));
                        lay.Inverts.Add(r.Invert.Value);
                        keys.Add(key);
                        return keys.Count - 1;
                    };
                    var runs = t.FullRuns();
                    int main = fontOf(runs[0]);
                    bool codes = t == Codes;
                    var pieces = new List<Seg>();
                    int at = 0;
                    foreach (var r in runs)
                    {
                        pieces.Add(new Seg(codes ? r.Text : Fields.Expand(r.Text, now).Replace("\r\n", "\n"), fontOf(r), at));
                        at += r.Text.Length;
                    }
                    if (space && pieces.All(p => p.Text.Length == 0)) pieces = new List<Seg> { new Seg(" ", main) };
                    Func<List<Seg>, float> width = line => LineWidth(g, lay, fmt, line);

                    // Paragraphs at the line breaks; an empty one keeps the font it is in (its height).
                    var paras = new List<List<Seg>> { new List<Seg>() };
                    var leads = new List<int> { pieces.Count > 0 ? pieces[0].Font : main };
                    foreach (var p in pieces)
                    {
                        var parts = p.Text.Split('\n');
                        int from = p.Start;
                        for (int k = 0; k < parts.Length; k++)
                        {
                            if (k > 0) { paras.Add(new List<Seg>()); leads.Add(p.Font); }
                            Append(paras[paras.Count - 1], parts[k], p.Font, from);
                            from += parts[k].Length + 1;
                        }
                    }
                    float lh = (float)(t.Kind == TextKind.Frame ? t.LineHeight : 1);
                    for (int k = 0; k < paras.Count; k++)
                    {
                        var wrapped = wrap > 0 ? Wrap(paras[k], wrap, width) : new List<List<Seg>> { paras[k] };
                        for (int m = 0; m < wrapped.Count; m++)
                        {
                            var line = wrapped[m];
                            var used = line.Where(s => s.Text.Length > 0).Select(s => s.Font).Distinct().ToList();
                            if (used.Count == 0) used.Add(line.Count > 0 ? line[0].Font : leads[k]);
                            float size = used.Max(f => lay.FontSizes[f]);
                            lay.Lines.Add(line);
                            lay.Widths.Add(width(wrap > 0 ? TrimEnd(line) : line));   // a frame: spaces at a line's end don't overflow it
                            lay.AlignWidths.Add(width(line));
                            lay.Ascents.Add(used.Max(f => lay.FontAscents[f]));
                            lay.LineSizes.Add(size);
                            lay.Pitches.Add(size * lh);
                            lay.Ends.Add(m == wrapped.Count - 1);
                        }
                    }
                }
            }
            return lay;
        }

        static Font FontOf(string family, double points, FontStyle style)
        {
            return new Font(family, (float)(points * 25.4 / 72), style, GraphicsUnit.World);   // world unit = mm
        }

        // Adds text in a font to a line: to its last stretch when that has the same font (start: where the text is in the
        // stored text; an empty stretch takes the new one's).
        static void Append(List<Seg> line, string text, int font, int start = -1)
        {
            if (line.Count > 0 && line[line.Count - 1].Font == font)
            {
                var last = line[line.Count - 1];
                line[line.Count - 1] = new Seg(last.Text + text, font, last.Text.Length == 0 && start >= 0 ? start : last.Start);
            }
            else if (text.Length > 0 || line.Count == 0) line.Add(new Seg(text, font, start));
        }

        static List<Seg> Concat(List<Seg> a, List<Seg> b)
        {
            var line = new List<Seg>();
            foreach (var s in a.Concat(b)) Append(line, s.Text, s.Font, s.Start);
            return line;
        }

        static bool HasText(List<Seg> line) { return line.Any(s => s.Text.Length > 0); }

        // Without the spaces at the end (an empty line keeps its first stretch's font).
        static List<Seg> TrimEnd(List<Seg> line)
        {
            var r = line.Select(s => new Seg(s.Text, s.Font, s.Start)).ToList();
            while (r.Count > 0)
            {
                var last = r[r.Count - 1];
                last.Text = last.Text.TrimEnd(' ');
                if (last.Text.Length > 0 || r.Count == 1) break;
                r.RemoveAt(r.Count - 1);
            }
            return r;
        }

        // The letter spacing goes after each character but the line's last.
        static float LineWidth(Graphics g, TextLayout lay, StringFormat fmt, List<Seg> line)
        {
            if (!HasText(line)) return 0;
            float w = 0, extra = 0, last = 0;
            foreach (var s in line)
            {
                if (s.Text.Length == 0) continue;
                w += g.MeasureString(s.Text, lay.Fonts[s.Font], PointF.Empty, fmt).Width;
                last = lay.Spacings[s.Font];
                extra += last * Elements(s.Text).Count;
            }
            return Math.Max(0, w + extra - last);
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
            {
                int off = 0;
                foreach (var part in Regex.Split(s.Text, "(?<= )"))
                {
                    if (part.Length == 0) continue;
                    Append(cur, part, s.Font, s.Start < 0 ? -1 : s.Start + off);
                    off += part.Length;
                    if (part.EndsWith(" ")) { tokens.Add(cur); cur = new List<Seg>(); }
                }
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
                if (!HasText(line) && width(TrimEnd(word)) > max)
                {
                    // A word wider than a line, broken at characters: walked once (measuring the whole rest of the word
                    // for every line was quadratic: 40,000 characters without a space took 7 s; parser_fuzz).
                    var chars = TrimEnd(word).SelectMany(CharSegs).ToList();
                    int at = 0, dropped = 0;
                    while (chars.Count - at >= 2)
                    {
                        int n = 1;
                        while (at + n < chars.Count && width(Concat(new List<Seg>(), chars.GetRange(at, n + 1))) <= max) n++;
                        if (at + n == chars.Count) break;   // the rest fits on a line
                        lines.Add(Concat(new List<Seg>(), chars.GetRange(at, n)));
                        for (int k = 0; k < n; k++) dropped += chars[at + k].Text.Length;
                        at += n;
                    }
                    word = Drop(word, dropped);
                }
                line = Concat(line, word);
            }
            // The paragraph's last line keeps the spaces at its end: each typed space has its caret place (they hang
            // beyond the line as in Word; Layout measures the line without them).
            lines.Add(HasText(line) ? line : new List<Seg> { new Seg("", line.Count > 0 ? line[0].Font : lead, para.Count > 0 ? para[0].Start : -1) });
            return lines;
        }

        // A stretch as one stretch per character (element), each with its place in the stored text.
        static IEnumerable<Seg> CharSegs(Seg s)
        {
            int off = 0;
            foreach (var e in Elements(s.Text))
            {
                yield return new Seg(e, s.Font, s.Start < 0 ? -1 : s.Start + off);
                off += e.Length;
            }
        }

        // The stretches without their first 'chars' characters.
        static List<Seg> Drop(List<Seg> line, int chars)
        {
            var r = new List<Seg>();
            foreach (var s in line)
            {
                int cut = Math.Min(chars, s.Text.Length);
                chars -= cut;
                if (cut < s.Text.Length) r.Add(new Seg(s.Text.Substring(cut), s.Font, s.Start < 0 ? -1 : s.Start + cut));
            }
            return r;
        }

        // The edited text scrolled up inside its frame by this much (mm), so the caret's line shows (View > Show overflow
        // while editing off). Screen only, as Codes.
        internal static float CodesScroll { get; set; }

        // overflow: only what a frame cuts off (below or beside it), for the canvas to show in grey while editing.
        // Inverted characters (runs, or all of a paragraph text: not in a black box, InvertBox) get a band of ink behind
        // them, as high as their line with half the room to its neighbours (the bands of lines below each other touch), from
        // their first to their last character that isn't a space, TextPad wider at the line's ends; they are drawn in the
        // paper's colour. An empty line gets none. The bands are drawn before any text, so that none covers the line above.
        static void DrawText(Graphics g, TextObject t, RectangleF box, DateTime now, Brush ink, bool overflow = false)
        {
            var inner = RectangleF.Inflate(box, -TextPad, -TextPad);
            bool frame = t.Kind == TextKind.Frame;
            Brush paper = ReferenceEquals(ink, Brushes.White) ? Brushes.Black : Brushes.White;   // colours: fixed (the label as printed)
            using (var lay = Layout(t, now, frame ? Math.Max(0.1f, inner.Width / WidthMargin) : 0))
            using (var fmt = Format())
            {
                // A frame cuts off what doesn't fit (the canvas marks it); a single line sits in the middle of its box.
                if (frame && overflow) using (var outside = new Region(box)) g.ExcludeClip(outside);
                else if (frame) g.IntersectClip(box);
                float y0 = frame ? inner.Top : inner.Top + (inner.Height - lay.Height) / 2;
                if (frame && t == Codes && !overflow) y0 -= CodesScroll;
                var plain = new Dictionary<int, Font>();   // the fonts without underline (drawn character by character)
                bool boxed = t.InvertBox;
                bool shaded = lay.Inverts.Any(v => v != boxed);
                try
                {
                    for (int pass = shaded ? 0 : 1; pass < 2; pass++)
                    {
                        float y = y0;
                        for (int i = 0; i < lay.Lines.Count; y += lay.Pitches[i], i++)
                        {
                            var line = lay.Lines[i];
                            float aw = AlignWidth(lay, i, inner), gap = Gap(t, lay, i, inner);
                            float x = t.Align == TextAlign.Center ? inner.Left + (inner.Width - aw) / 2 : t.Align == TextAlign.Right ? inner.Right - aw : inner.Left;
                            float cum = 0;   // the letter spacing after the characters before this stretch in the line
                            int sp = 0;      // spaces before this stretch in the line (a justified line's gap after each)
                            int lastInk = line.FindLastIndex(q => q.Text.TrimEnd(' ').Length > 0);   // the last stretch with more than spaces
                            int firstText = line.FindIndex(q => q.Text.Length > 0);
                            // the band: the line, with half the room to the line above and below it (the bands of two lines
                            // meet in the middle; the text stays in the middle of its band at any line spacing)
                            float bandTop = i > 0 ? y - Math.Max(0, lay.Pitches[i - 1] - lay.LineSizes[i - 1]) / 2 : y;
                            float bandBottom = y + lay.LineSizes[i] + (i < lay.Lines.Count - 1 ? Math.Max(0, lay.Pitches[i] - lay.LineSizes[i]) / 2 : 0);
                            for (int j = 0; j < line.Count; j++)
                            {
                                var s = line[j];
                                if (s.Text.Length == 0) continue;
                                var font = lay.Fonts[s.Font];
                                float asc = lay.FontAscents[s.Font], spacing = lay.Spacings[s.Font];
                                bool flip = lay.Inverts[s.Font] != boxed;
                                var brush = flip ? paper : ink;
                                float top = asc == lay.Ascents[i] ? y : y + (lay.Ascents[i] - asc);   // on the line's baseline
                                float sw = g.MeasureString(s.Text, font, PointF.Empty, fmt).Width;
                                Font p;
                                if (!plain.TryGetValue(s.Font, out p)) plain[s.Font] = p = new Font(font, font.Style & ~FontStyle.Underline);
                                var chars = Elements(s.Text);
                                float start = x + cum + sp * gap;
                                // the stretch's width with the spacing after each of its characters; the line's last stretch up to
                                // its last character that is not a space (no spacing after it): spaces at the line's end aren't
                                // underlined or shaded
                                float span;
                                if (j < lastInk) span = sw + spacing * chars.Count + gap * Spaces(s.Text);
                                else
                                {
                                    string inked = s.Text.TrimEnd(' ');
                                    int n = Elements(inked).Count;
                                    span = n == 0 ? 0 : g.MeasureString(inked, p, PointF.Empty, fmt).Width + spacing * (n - 1) + gap * Spaces(inked);
                                }
                                if (pass == 0)
                                {
                                    if (flip && span > 0)
                                    {
                                        float left = j == firstText ? TextPad : 0, right = j == lastInk ? TextPad : 0;
                                        g.FillRectangle(ink, start - left, bandTop, span + left + right, bandBottom - bandTop);
                                    }
                                }
                                else if (spacing == 0 && gap == 0)
                                    DrawStretch(g, s.Text, font, brush, start, top, sw, fmt, j < lastInk);
                                else
                                {
                                    // Character by character: each at its place in the line plus the spacing before it (and a
                                    // justified line's gap after each space before it).
                                    string before = "";
                                    for (int k = 0; k < chars.Count; k++)
                                    {
                                        float at = before.Length == 0 ? 0 : g.MeasureString(before, p, PointF.Empty, fmt).Width;
                                        g.DrawString(chars[k], p, brush, x + at + cum + k * spacing + (sp + Spaces(before)) * gap, top, fmt);
                                        before += chars[k];
                                    }
                                    if (font.Underline && span > 0)
                                        g.FillRectangle(brush, start, top + asc + font.Size * 0.1f, span, Math.Max(font.Size * 0.06f, 0.1f));
                                }
                                x += sw;
                                cum += spacing * chars.Count;
                                sp += Spaces(s.Text);
                            }
                        }
                    }
                }
                finally { foreach (var f in plain.Values) f.Dispose(); }
            }
        }

        // Justify: the room each space between the words of a wrapped line gets added, so that the line fills the frame
        // (mm; 0 for the last line of a paragraph, a line without spaces, artistic text and the other alignments).
        static float Gap(TextObject t, TextLayout lay, int i, RectangleF inner)
        {
            if (t.Kind != TextKind.Frame || t.Align != TextAlign.Justify || lay.Ends[i]) return 0;
            int spaces = TrimEnd(lay.Lines[i]).Sum(s => Spaces(s.Text));
            return spaces == 0 ? 0 : Math.Max(0, (inner.Width - lay.Widths[i]) / spaces);
        }

        static int Spaces(string s) { int n = 0; foreach (char ch in s) if (ch == ' ') n++; return n; }

        // The width a line is aligned with: with the spaces typed at its end (a centred or right-aligned line moves left
        // with each one, so the caret moves too), in a frame at most the frame's width (more spaces hang beyond it).
        static float AlignWidth(TextLayout lay, int i, RectangleF inner)
        {
            return Math.Max(lay.Widths[i], Math.Min(lay.AlignWidths[i], inner.Width));
        }

        // A stretch in one font (no letter spacing). GDI+ underlines a drawn string without its leading and trailing spaces:
        // an underlined stretch starting with spaces, or ending in spaces with more text after it on the line (the next word
        // in another format), is drawn with a full stop before or after it, clipped away, so that its spaces get the
        // underline in one line with its letters. Spaces at the line's end stay without (as in Word).
        static void DrawStretch(Graphics g, string text, Font font, Brush ink, float x, float top, float sw, StringFormat fmt, bool more)
        {
            bool lead = font.Underline && text.StartsWith(" ") && (more || text.Trim(' ').Length > 0);
            bool trail = font.Underline && text.EndsWith(" ") && more;
            if (!lead && !trail) { g.DrawString(text, font, ink, x, top, fmt); return; }
            float stop = g.MeasureString(".", font, PointF.Empty, fmt).Width;
            float left = lead ? x : x - 4 * font.Size, right = trail ? x + sw : x + sw + 4 * font.Size;
            var state = g.Save();
            try
            {
                g.IntersectClip(new RectangleF(left, top - 4 * font.Size, right - left, 8 * font.Size));
                g.DrawString((lead ? "." : "") + text + (trail ? "." : ""), font, ink, lead ? x - stop : x, top, fmt);
            }
            finally { g.Restore(state); }
        }

        // The edited text's cut-off part in the object's own frame (g: mm, centred on the box, turned with it), for the
        // canvas to draw it light grey below the frame while editing.
        internal static void DrawOverflow(Graphics g, TextObject t, Brush ink)
        {
            if (t.Kind != TextKind.Frame) return;
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            float w = (float)(turned ? t.H : t.W), h = (float)(turned ? t.W : t.H);
            DrawText(g, t, new RectangleF(-w / 2, -h / 2, w, h), DateTime.Now, ink, true);
        }

        // Where every character of a text is drawn (the caret map, for editing on the canvas): per laid-out line its top,
        // height and each character boundary's x with its index in the stored text, in the object's own frame (mm, centred
        // on the box, not turned), measured as DrawText draws (the same Graphics, format, spacing). For the text being
        // edited (Codes) the fields are their codes, so the indexes match the stored text 1:1.
        internal static CaretMap MapText(TextObject t)
        {
            bool turned = t.Rotation == 90 || t.Rotation == 270;
            float w = (float)(turned ? t.H : t.W), h = (float)(turned ? t.W : t.H);
            var box = new RectangleF(-w / 2, -h / 2, w, h);
            var inner = RectangleF.Inflate(box, -TextPad, -TextPad);
            bool frame = t.Kind == TextKind.Frame;
            var map = new CaretMap { Box = box, Frame = frame, Length = t.Text.Length };
            using (var lay = Layout(t, DateTime.Now, frame ? Math.Max(0.1f, inner.Width / WidthMargin) : 0))
            using (var bmp = new Bitmap(1, 1))
            {
                bmp.SetResolution(300, 300);
                using (var g = Graphics.FromImage(bmp))
                using (var fmt = Format())
                {
                    g.PageUnit = GraphicsUnit.Millimeter;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    // spaces typed at a frame line's end hang beyond it (as in Word): their places stop at the frame's edge
                    Func<float, float> Edge = v => frame ? Math.Min(v, inner.Right) : v;
                    float y = frame ? inner.Top : inner.Top + (inner.Height - lay.Height) / 2;
                    int next = 0;   // where the line after the last one starts at the earliest
                    for (int i = 0; i < lay.Lines.Count; y += lay.Pitches[i], i++)
                    {
                        var line = lay.Lines[i];
                        float lw = AlignWidth(lay, i, inner), gap = Gap(t, lay, i, inner);
                        float x = t.Align == TextAlign.Center ? inner.Left + (inner.Width - lw) / 2 : t.Align == TextAlign.Right ? inner.Right - lw : inner.Left;
                        var index = new List<int>();
                        var xs = new List<float>();
                        int sp = 0;
                        float cum = 0;   // the letter spacing after the characters before this stretch
                        foreach (var s in line)
                        {
                            if (s.Text.Length == 0) continue;
                            var font = lay.Fonts[s.Font];
                            float spacing = lay.Spacings[s.Font];
                            var chars = Elements(s.Text);
                            string before = "";
                            int off = 0;
                            for (int k = 0; k < chars.Count; k++)
                            {
                                float at = before.Length == 0 ? 0 : g.MeasureString(before, font, PointF.Empty, fmt).Width;
                                index.Add(s.Start + off);
                                xs.Add(Edge(x + at + cum + k * spacing + (sp + Spaces(before)) * gap));
                                before += chars[k];
                                off += chars[k].Length;
                            }
                            x += g.MeasureString(s.Text, font, PointF.Empty, fmt).Width;
                            cum += spacing * chars.Count;
                            sp += Spaces(s.Text);
                            index.Add(s.Start + off);
                            xs.Add(Edge(x + cum - spacing + sp * gap));   // the stretch's end: after its last character
                        }
                        if (index.Count == 0)
                        {
                            int start = line.Count > 0 && line[0].Start >= 0 ? line[0].Start : next;
                            index.Add(start);
                            xs.Add(x);
                        }
                        // a stretch's end and the next one's start are the same place: the next one's start counts
                        var ci = new List<int>();
                        var cx = new List<float>();
                        for (int k = 0; k < index.Count; k++)
                        {
                            if (ci.Count > 0 && ci[ci.Count - 1] == index[k]) { cx[cx.Count - 1] = xs[k]; continue; }
                            ci.Add(index[k]); cx.Add(xs[k]);
                        }
                        map.Lines.Add(new CaretLine { Index = ci.ToArray(), X = cx.ToArray(), Top = y, Height = lay.LineSizes[i], Ascent = lay.Ascents[i] });
                        next = ci[ci.Count - 1] + 1;
                    }
                }
            }
            return map;
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
                return lay.Lines.Select(l => string.Concat(l.Select(x => x.Text))).Select(l => t.Kind == TextKind.Frame ? l.TrimEnd(' ') : l).ToList();   // a frame: without the spaces at a line's end, as before
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

    sealed partial class EditorForm
    {
        // The settings rows of a text (BuildProperties: they come in this order).
        // The font box's list (once): the installed families, the typed text kept
        void FillFonts()
        {
            if (fontBox.Items.Count > 0) return;
            string text = fontBox.Text;
            fontBox.BeginUpdate();
            foreach (var f in FontFamily.Families) fontBox.Items.Add(f.Name);
            fontBox.EndUpdate();
            if (fontBox.Text != text) fontBox.Text = text;
        }

        void BuildTextRows()
        {
            Func<LabelObject, bool> text = o => o is TextObject;
            Func<LabelObject, bool> oneLine = o => o is TextObject && ((TextObject)o).Kind == TextKind.Line,
                frame = o => o is TextObject && ((TextObject)o).Kind == TextKind.Frame;
            // The text is typed on the label: the panel says how and has a button for it; the
            // fields go in at the caret.
            SpanRow(Hint(Lang.T("Double-click the text on the label to edit it.")), o => text(o) && canvas.EditedText != o);
            var editButton = new System.Windows.Forms.Button { Text = Lang.T("Edit text (F2)"), AutoSize = true };
            editButton.Click += delegate { StartEditing(); };
            Row("", editButton, o => text(o) && canvas.EditedText != o);
            Row("", FieldButton(InsertField), text);
            textKindBox = Combo(Lang.T("Artistic text"), Lang.T("Paragraph text"));
            textKindBox.SelectedIndexChanged += (s, e) => Edit(s, o => SetTextKind((TextObject)o, (TextKind)textKindBox.SelectedIndex));
            Row(Lang.T("Kind:"), textKindBox, text);
            tips.SetToolTip(textKindBox, Lang.T("Artistic text: one line, the box follows the text, the handles change its size.\n" +
                                                "Paragraph text: you set the box, the text wraps inside it.\n" +
                                                "In both, selected words can have their own format."));
            SpanRow(Hint(Lang.T("Drag a handle to make the text larger or smaller.")), oneLine);
            SpanRow(Hint(Lang.T("The text wraps in its box. Selected words can be formatted on their own.")), frame);
            // What the font, size, bold, italic and underline rows act on: the selected words, else the whole text.
            scopeHint = Theme.Fore(Hint(""), Role.Accent);
            SpanRow(scopeHint, o => frame(o) && canvas.EditedText == o && canvas.Editing.HasSelection);
            // While the text doesn't fit (the canvas draws the box's edge in red).
            var overflow = Hint(Lang.T("The text doesn't fit the box and is cut off: make the box larger or the text smaller."));
            Theme.Fore(overflow, Role.ErrorText);
            SpanRow(overflow, o => o is TextObject && EditorRender.Overflows((TextObject)o));
            fontBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            // The installed fonts are listed when the box is first used: listing them loads GDI+'s table of every installed
            // font (seconds after a restart with many fonts), which the window's start doesn't need (UiFont.cs).
            fontBox.Enter += delegate { FillFonts(); };
            fontBox.DropDown += delegate { FillFonts(); };
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
            // bold, italic, underline, invert, alignment and the spacings: the toolbar's text group (EditorForm.TextBar.cs)
        }

        // ---------- the format rows (words with their own format: the selection while editing on the label) ----------

        Label scopeHint;

        // The object's runs with "\n" breaks, every field filled.
        static List<TextRun> BoxRuns(TextObject t) { return RunEdit.Normalized(t.FullRuns()); }

        static TextRun Filled(TextRun r, TextObject t) { return t.Fill(r == null ? null : r.WithText("")); }

        static TextRun With(TextRun r, string font = null, double? size = null, bool? bold = null, bool? italic = null, bool? underline = null)
        {
            return r.With(font, size, bold, italic, underline);
        }

        // Font, size, bold, italic, underline, letter spacing, invert: while the text is edited on the label on its
        // selection (with nothing selected on what is typed next), else on the whole text.
        void Style(object sender, Func<TextRun, TextRun> change, bool caret)
        {
            var t = canvas.Selected as TextObject;
            if (loading || t == null) return;
            if (canvas.EditedText == t)
            {
                canvas.EditChange(e => e.Restyle(change));
                if (sender is CheckBox) canvas.Focus();   // typing goes on in the text (a space would toggle the check)
                return;
            }
            int start = 0, length = -1;
            Edit(sender, o =>
            {
                var x = (TextObject)o;
                var full = BoxRuns(x);
                int n = length < 0 ? full.Sum(r => r.Text.Length) : length;
                x.SetRuns(RunEdit.Restyle(full, start, n, change));
            });
        }

        // Bold, italic, underline, invert: off when all of it has it, else on (a mixed one turns it on for all, as in Word).
        void ToggleStyle(object sender, string format, bool caret)
        {
            var t = canvas.Selected as TextObject;
            if (loading || t == null) return;
            int i = format == "bold" ? 0 : format == "italic" ? 1 : format == "underline" ? 2 : 3;
            var now = Current(t);
            bool? has = i == 0 ? now.Bold : i == 1 ? now.Italic : i == 2 ? now.Underline : now.Invert;
            bool on = has != true;
            Style(sender, r => i == 0 ? r.With(bold: on) : i == 1 ? r.With(italic: on) : i == 2 ? r.With(underline: on) : r.With(invert: on), caret);
        }

        // The format the rows show: while editing the selection's (with nothing selected what is typed next), else the
        // whole text's.
        TextRun Current(TextObject t)
        {
            if (canvas.EditedText == t) return canvas.Editing.Current;
            var full = BoxRuns(t);
            return RunEdit.Common(full, 0, full.Sum(r => r.Text.Length)) ?? Filled(null, t);
        }

        // The text rows for a text object (FillProps; skip: the control the user is typing in).
        void FillText(TextObject t, object skip)
        {
            textKindBox.SelectedIndex = (int)t.Kind;
            FillStyleRows(t, skip);
        }

        // Font, size and the line above them: mixed values blank (bold, italic, underline: the toolbar, hatched).
        void FillStyleRows(TextObject t, object skip = null)
        {
            var c = Current(t);
            if (skip != fontBox && fontBox.Text != (c.Font ?? "")) fontBox.Text = c.Font ?? "";
            // the box selects the name it was given: no blue name in a box the user isn't in (the text on the label is)
            if (skip != fontBox && !fontBox.Focused && fontBox.IsHandleCreated && fontBox.SelectionLength > 0) fontBox.SelectionLength = 0;
            if (skip != sizeNum)
            {
                // the text too: after a mixed (blank) value the same Value doesn't write it again
                if (c.Size != null) { sizeNum.Value = Clamp(sizeNum, c.Size.Value); sizeNum.Text = sizeNum.Value.ToString("F" + sizeNum.DecimalPlaces, CultureInfo.CurrentCulture); }
                else sizeNum.Text = "";
            }
            var e = canvas.EditedText == t ? canvas.Editing : null;
            bool selected = e != null && e.HasSelection;
            if (selected)
            {
                string words = e.SelectedText.Replace("\n", " ").Trim();
                if (words.Length > 24) words = words.Substring(0, 23) + "…";
                scopeHint.Text = e.SelectionLength >= e.Length ? Lang.T("All text selected: mixed values stay blank")
                                                               : Lang.F("Applies to the selection: “{0}”", words);
            }
            if (scopeHint.Visible != selected) scopeHint.Visible = selected;
        }

        void AddText() { AddNewText(NewText(FreeSpot())); }

        void AddTextFrame() { AddNewText(NewTextFrame(FreeSpot())); }

        static TextObject NewText(PointF at)
        {
            var t = new TextObject { X = at.X, Y = at.Y, Text = Lang.T("Text"), Size = 14 };
            EditorRender.FitText(t);
            return t;
        }

        TextObject NewTextFrame(PointF at)
        {
            double w = layout != null ? Math.Max(10, Math.Min(40, layout.Printable.Width - 2)) : 40;
            return new TextObject { X = at.X, Y = at.Y, W = w, H = 12, Text = Lang.T("Text"), Size = 12, Kind = TextKind.Frame };
        }

        // A new text is only selected, as every new object: it can be moved first, also off something it covers (
        // 2026-11-09; it started editing before). Editing starts as on every text; typing on it replaces its placeholder.
        void AddNewText(TextObject t)
        {
            Add(t);
            newText = t;
            if (ActiveForm == this) canvas.Focus();   // arrow keys move it, typing goes to it (not from automation)
        }

        // Single line: line breaks become spaces (the words keep their formats), the box follows the text (Edit). Text
        // frame: starts with the box around the text, then keeps the box it is given.
        static void SetTextKind(TextObject t, TextKind kind)
        {
            if (t.Kind == kind) return;
            t.Kind = kind;
            if (kind == TextKind.Line && t.FormatRuns != null) t.SetRuns(t.FullRuns().Select(r => r.WithText(OneLine(r.Text))).ToList());
            else if (kind == TextKind.Line) t.Text = OneLine(t.Text);
            else EditorRender.FitBox(t);
        }
    }
}
