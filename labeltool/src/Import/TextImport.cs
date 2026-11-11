// Text objects for a paste of text-only EMF: read from the EMF (places, fonts, sizes, styles: Import/EmfText.cs), with
// CorelDRAW's CMX laid over it where the two agree (Import/Corel/Cmx.cs): the real paragraph marks instead of the
// laid-out lines, paragraph text in a frame or artistic text, the frame's size, per paragraph size, alignment and
// spacing. A CMX text is used only when its characters (without spaces and line breaks) are exactly those of text
// records next to each other in record order; anything else stays as the EMF lays it out. White text (Label Maker's
// texts are black): a CMX text drawn all in white comes in white on black (it sits on something dark), white characters
// inside a dark text are left out (they don't show on white; the rest stays where it was). A whole label from CorelDRAW
// (drawing and text): FromLabel, its texts like this between its drawing's layers (Import/EmfLayers.cs). CorelDRAW draws
// an underline as a thin filled bar under each line: such a bar under a text's baseline, along it, makes that text
// underlined and is left out of the picture (Underlines).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace QlLabels
{
    static class TextImport
    {
        // Around the text inside a text object's box, both sides (mm; EditorRender's TextPad).
        const double BoxPadding = 0.6;

        // The objects (InvalidDataException: the EMF can't be read as text); missing: fonts not installed; how: "emf",
        // "cmx" (every CMX text used) or "cmx partly".
        public static List<TextObject> FromEmf(ClipContent c, out List<string> missing, out string how)
        {
            List<EmfBar> underlines;
            return FromEmf(c, out missing, out how, out underlines);
        }

        // underlines: the bars that became underlines (left out of the picture). Any other exception, and texts out of the
        // label's ranges (Sane), come out as InvalidDataException: the paste takes the picture.
        static List<TextObject> FromEmf(ClipContent c, out List<string> missing, out string how, out List<EmfBar> underlines)
        {
            List<TextObject> list;
            try { list = Read(c, out missing, out how, out underlines); }
            catch (InvalidDataException) { throw; }
            catch (Exception ex) { throw new InvalidDataException("The EMF's text can't be read.", ex); }
            if (!list.All(Sane)) throw new InvalidDataException("The EMF's text is out of range.");
            return list;
        }

        // A text object within what a label takes: place and box finite and within the panel's range (mm), every size
        // within a text's (2 to 400 pt, as a file is read: Editor.Text.cs).
        internal static bool Sane(TextObject t)
        {
            Func<double, bool> mm = v => !double.IsNaN(v) && Math.Abs(v) <= LabelObject.MaxMm;
            Func<double, bool> pt = v => !double.IsNaN(v) && v > 0 && v <= 400;
            return t != null && mm(t.X) && mm(t.Y) && mm(t.W) && mm(t.H) && pt(t.Size) && !double.IsNaN(t.Angle) && !double.IsInfinity(t.Angle)
                   && t.FullRuns().All(r => !r.Size.HasValue || pt(r.Size.Value));
        }

        static List<TextObject> Read(ClipContent c, out List<string> missing, out string how, out List<EmfBar> underlines)
        {
            how = "emf";
            var quirks = EmfQuirks.For(c.Source);
            var runs = EmfText.ReadRuns(c.Emf, quirks, out missing);
            underlines = Underlines(runs, EmfText.Bars(c.Emf, quirks));
            var list = Objects(runs);
            if (c.Cmx == null || list.Count == 0) return list;
            List<CmxText> texts;
            try { texts = Cmx.Read(c.Cmx); }
            catch (InvalidDataException) { return list; }
            catch (ArgumentException) { return list; }
            if (texts.Count == 0) return list;
            // each CMX text takes the text records that are its characters: next to each other in record order (CorelDRAW
            // writes one text object's records together, a line's parts in any order), then laid out by the EMF as usual;
            // so texts side by side on one baseline stay apart, and one text's lines stay together
            var left = new List<EmfRun>(runs);
            var result = new List<TextObject>();
            int used = 0;
            foreach (var t in texts)
            {
                var window = Window(left, Sorted(t.Text));
                if (window == null) continue;
                var white = window.Where(r => r.White).ToList();
                bool allWhite = white.Count == window.Count;
                var cmx = allWhite || white.Count == 0 ? t : Without(t, white);
                if (cmx == null) continue;
                var objects = Objects(allWhite ? window : window.Except(white).ToList());
                if (objects.Count == 0 || (objects.Count > 1 && objects.Any(o => o.Angle != 0))) continue;
                foreach (var r in window) left.Remove(r);
                var o1 = Apply(cmx, objects);
                if (allWhite) o1.Invert = true;
                result.Add(o1);
                used++;
            }
            if (used == 0) return list;
            how = used == texts.Count ? "cmx" : "cmx partly";
            result.AddRange(Objects(left));
            return result;
        }

        // The text objects of these runs, in reading order (top to bottom, then left to right).
        static List<TextObject> Objects(List<EmfRun> runs)
        {
            var list = new List<TextObject>();
            if (runs.Count == 0) return list;
            foreach (var p in EmfText.Paragraphs(EmfText.Lines(runs.Select(r => r.Copy()).ToList()))) list.AddRange(EmfText.Objects(p));
            return list.OrderBy(o => Math.Round(o.Y, 1)).ThenBy(o => o.X).ToList();
        }

        static string Sorted(string s) { return new string(Squeeze(s).OrderBy(ch => ch).ToArray()); }

        // The bars that are underlines, their runs set underlined: a bar just under the baseline (its top within 0.4 em
        // below it), at most 0.2 em thick, along the runs of one line it covers (each at least 60 %, together at least
        // 80 % of the bar); runs in white or turned otherwise are not taken.
        static List<EmfBar> Underlines(List<EmfRun> runs, List<EmfBar> bars)
        {
            var used = new List<EmfBar>();
            foreach (var bar in bars)
            {
                List<EmfRun> under = null;
                foreach (var r in runs)
                {
                    if (r.White || r.Width <= 0) continue;
                    var d = EmfText.Dir(r.Angle);
                    var n = new PointF(-d.Y, d.X);   // down, as seen
                    float em = r.Em;
                    var along = bar.Corners.Select(p => (p.X - r.Start.X) * d.X + (p.Y - r.Start.Y) * d.Y).ToList();
                    var below = bar.Corners.Select(p => (p.X - r.Start.X) * n.X + (p.Y - r.Start.Y) * n.Y).ToList();
                    if (below.Min() < -0.05f * em || below.Min() > 0.4f * em || below.Max() - below.Min() > 0.2f * em) continue;
                    float a0 = along.Min(), a1 = along.Max();
                    float covered = Math.Min(a1, r.Width) - Math.Max(a0, 0);
                    if (covered < 0.6f * r.Width) continue;
                    if (under == null) under = new List<EmfRun>();
                    under.Add(r);
                }
                if (under == null) continue;
                // the runs together: along the bar's direction, from the first run's start
                var r0 = under[0];
                var dir = EmfText.Dir(r0.Angle);
                if (under.Any(r => Math.Abs(r.Angle - r0.Angle) > 1)) continue;
                var along0 = bar.Corners.Select(p => (p.X - r0.Start.X) * dir.X + (p.Y - r0.Start.Y) * dir.Y).ToList();
                float b0 = along0.Min(), b1 = along0.Max(), length = b1 - b0;
                float inside = 0;
                foreach (var r in under.OrderBy(r => (r.Start.X - r0.Start.X) * dir.X + (r.Start.Y - r0.Start.Y) * dir.Y))
                {
                    float s0 = (r.Start.X - r0.Start.X) * dir.X + (r.Start.Y - r0.Start.Y) * dir.Y;
                    inside += Math.Max(0, Math.Min(b1, s0 + r.Width) - Math.Max(b0, s0));
                }
                if (length <= 0 || inside < 0.8f * length) continue;
                foreach (var r in under) r.Underline = true;
                used.Add(bar);
            }
            return used;
        }

        // The CMX text without the characters of these (white) runs, each found once only; null: one isn't.
        static CmxText Without(CmxText t, List<EmfRun> white)
        {
            var c = new CmxText
            {
                Text = t.Text, Font = t.Font, SizeMm = t.SizeMm, LineSpacingPct = t.LineSpacingPct, Frame = t.Frame,
                Artistic = t.Artistic, FrameWidthMm = t.FrameWidthMm, FrameHeightMm = t.FrameHeightMm, VAlign = t.VAlign,
                Matrix = t.Matrix
            };
            foreach (var p in t.Paragraphs)
                c.Paragraphs.Add(new CmxParagraph
                {
                    Start = p.Start, Length = p.Length, Font = p.Font, SizeMm = p.SizeMm, Align = p.Align,
                    CharSpacingPct = p.CharSpacingPct, WordSpacingPct = p.WordSpacingPct, LineSpacingPct = p.LineSpacingPct
                });
            c.Glyphs.AddRange(t.Glyphs);
            foreach (var r in white)
            {
                string s = Squeeze(r.Text);
                if (s.Length == 0) continue;
                int at = c.Text.IndexOf(s, StringComparison.Ordinal);
                if (at < 0 || c.Text.IndexOf(s, at + 1, StringComparison.Ordinal) >= 0) return null;
                c.Text = c.Text.Remove(at, s.Length);
                foreach (var p in c.Paragraphs)
                {
                    if (p.Start > at) p.Start = Math.Max(at, p.Start - s.Length);
                    else if (at < p.Start + p.Length) p.Length = Math.Max(0, p.Length - Math.Min(s.Length, p.Start + p.Length - at));
                }
            }
            return Squeeze(c.Text).Length > 0 ? c : null;
        }

        // The first runs next to each other in record order whose characters (without spaces) are these; null: none.
        static List<EmfRun> Window(List<EmfRun> runs, string sorted)
        {
            if (sorted.Length == 0) return null;
            for (int i = 0; i < runs.Count; i++)
            {
                int length = 0;
                for (int j = i; j < runs.Count; j++)
                {
                    length += Squeeze(runs[j].Text).Length;
                    if (length > sorted.Length) break;
                    if (length == sorted.Length)
                    {
                        var window = runs.GetRange(i, j - i + 1);
                        if (Sorted(string.Concat(window.Select(r => r.Text))) == sorted) return window;
                        break;
                    }
                }
            }
            return null;
        }

        // A whole label from CorelDRAW (drawing and text in one EMF): its parts in z-order, each a picture
        // (EmfLayers.Picture: an EMF of drawing only and its place, Import/EmfLayers.cs) or a text object (TextObject, as
        // FromEmf makes them), all in mm on the copied frame. null: not to be split (no text, a source other than
        // CorelDRAW, or the split failed): paste it as one picture.
        public static List<object> FromLabel(ClipContent c, out List<string> missing, out string how)
        {
            missing = new List<string>(); how = "";
            if (c.Emf == null || c.Source == null || !c.Source.IsCorelDraw) return null;
            EmfPicture.Facts facts;
            try { facts = EmfPicture.What(c.Emf); }
            catch (InvalidDataException) { return null; }
            if (facts.Kind != EmfPicture.Content.Vector || !facts.HasText) return null;
            List<TextObject> texts;
            List<EmfBar> underlines;
            try { texts = FromEmf(c, out missing, out how, out underlines); }
            catch (InvalidDataException) { return null; }
            if (texts.Count == 0) return null;
            // the underlines are in the texts now: the drawing without them; none left: only the texts
            var emf = underlines.Count == 0 ? c.Emf : EmfLayers.Blank(c.Emf, underlines.Select(b => b.Fill));
            if (underlines.Count > 0 && !EmfLayers.Draws(emf)) return texts.Cast<object>().ToList();
            var layers = EmfLayers.Split(emf);
            if (layers == null || layers.Pictures.Count == 0) return null;
            var parts = new List<KeyValuePair<int, object>>();
            foreach (var p in layers.Pictures) parts.Add(new KeyValuePair<int, object>(p.Level, p));
            foreach (var t in texts) parts.Add(new KeyValuePair<int, object>(layers.LevelOf(t.Box), t));
            // by level; within one level in the order made (a stable sort)
            return parts.Select((p, i) => new { p, i }).OrderBy(x => x.p.Key).ThenBy(x => x.i).Select(x => x.p.Value).ToList();
        }

        static string Squeeze(string s) { return new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray()); }

        // One object with the CMX text and the EMF objects' formats, where they were. From the CMX per paragraph: the
        // exact size (the EMF's went through CorelDRAW's rounded mapping, up to 2 % off), the alignment and the
        // character spacing; for paragraph text the frame's width and, top aligned, its height.
        static TextObject Apply(CmxText cmx, List<TextObject> from)
        {
            var first = from[0];
            // the formats character by character (spaces and line breaks take the one before)
            var formats = new List<TextRun>();
            foreach (var o in from)
                foreach (var r in o.FullRuns())
                    foreach (char ch in r.Text)
                        if (!char.IsWhiteSpace(ch)) formats.Add(r);
            var runs = new List<TextRun>();
            var sb = new StringBuilder();
            TextRun current = null;
            CmxParagraph currentPara = null;
            int k = 0;
            for (int i = 0; i < cmx.Text.Length; i++)
            {
                char ch = cmx.Text[i];
                var f = char.IsWhiteSpace(ch) ? (current ?? formats[0]) : formats[Math.Min(k++, formats.Count - 1)];
                var para = ParagraphAt(cmx, i) ?? currentPara;
                if (current != null && (!ReferenceEquals(f, current) || !ReferenceEquals(para, currentPara)))
                {
                    runs.Add(Format(current.WithText(sb.ToString()), currentPara));
                    sb.Clear();
                }
                current = f;
                currentPara = para;
                sb.Append(ch);
            }
            if (current != null) runs.Add(Format(current.WithText(sb.ToString()), currentPara));

            // turned text (one object only, see Match): laid out unturned around its centre, turned back at the end
            var t = first;
            double angle = t.Angle;
            if (angle != 0) t.SetAngle(0);
            var box = from.Select(o => o.Box).Aggregate(RectangleF.Union);
            t.Kind = cmx.Frame || cmx.Text.Contains("\n") ? TextKind.Frame : TextKind.Line;
            t.SetRuns(runs);
            var p0 = cmx.Paragraphs.Count > 0 ? cmx.Paragraphs[0] : null;
            if (p0 != null)
            {
                if (p0.Align == 0 || p0.Align == 1) t.Align = TextAlign.Left;
                else if (p0.Align == 2) t.Align = TextAlign.Center;
                else if (p0.Align == 3) t.Align = TextAlign.Right;
                else if (p0.Align >= 4) t.Align = TextAlign.Justify;
            }
            // CorelDRAW's own line spacing where the CMX has it (same measure: % of ascent + descent); else the EMF
            // object's, from its pitch
            if (cmx.LineSpacingPct > 0) t.LineSpacing = TextObject.LimitLineSpacing(cmx.LineSpacingPct);
            // the space between paragraphs: CorelDRAW takes the larger of the one paragraph's space after and the next
            // one's space before (100 %: none added), Label Maker adds both; one value per text, the first paragraph's.
            // Paragraph text only: artistic text keeps the values but lays its lines out by the line spacing alone
            if (cmx.Frame && p0 != null)
            {
                t.SpaceBefore = TextObject.LimitParaSpace(Math.Max(p0.SpaceBeforePct, p0.SpaceAfterPct) - 100);
                t.SpaceAfter = 0;
            }
            t.X = box.X; t.Y = box.Y;
            EditorRender.FitBox(t);
            if (cmx.Frame && cmx.FrameWidthMm > 0)
            {
                // the frame's width, and the height its text then takes (it may wrap); a word wider than the frame
                // (other metrics than CorelDRAW's) widens it instead
                double w0 = t.W, h0 = t.H;
                t.W = Math.Round(cmx.FrameWidthMm + BoxPadding, 1);
                for (int i = 0; i < 400 && EditorRender.Overflows(t); i++) t.H = Math.Round(t.H + 0.2, 1);
                if (EditorRender.Overflows(t)) { t.W = Math.Max(w0, t.W); t.H = h0; }
            }
            // where the EMF had it: the edge or centre the alignment keeps; the text's middle stays where it was
            double cx = box.X + box.Width / 2.0;
            if (t.Align == TextAlign.Center) t.X = Math.Round(cx - t.W / 2, 2);
            else if (t.Align == TextAlign.Right) t.X = Math.Round(box.Right - t.W, 2);
            else t.X = Math.Round(box.X, 2);
            t.Y = Math.Round(box.Y + box.Height / 2.0 - t.H / 2, 2);
            // a frame keeps its height: below the text when top aligned, around it when centred, above it when bottom
            // aligned (the text stays where it was)
            if (cmx.Frame && cmx.FrameHeightMm > 0)
            {
                double fitted = t.H, bottom = t.Y + t.H;
                t.H = Math.Max(t.H, Math.Round(cmx.FrameHeightMm + BoxPadding, 1));
                if (cmx.VAlign == 1) { t.VerticalAlign = VerticalAlign.Middle; t.Y = Math.Round(t.Y + fitted / 2 - t.H / 2, 2); }
                else if (cmx.VAlign == 2) { t.VerticalAlign = VerticalAlign.Bottom; t.Y = Math.Round(bottom - t.H, 2); }
            }
            if (angle != 0) t.SetAngle(angle);
            return t;
        }

        static CmxParagraph ParagraphAt(CmxText cmx, int i)
        {
            foreach (var p in cmx.Paragraphs)
                if (i >= p.Start && i < p.Start + p.Length) return p;
            return null;
        }

        // A run with its paragraph's exact size (when the EMF's is within 3 % of it: a size of its own stays) and
        // character spacing (CorelDRAW's % + 100: Label Maker's measure).
        static TextRun Format(TextRun r, CmxParagraph p)
        {
            if (p == null) return r;
            double pt = Math.Round(p.SizeMm / 25.4 * 72, 2);
            if (pt > 0 && r.Size.HasValue && r.Size.Value != pt && Math.Abs(r.Size.Value - pt) <= pt * 0.03) r = r.With(size: pt);
            if (p.CharSpacingPct != 0)
                r = r.With(spacing: Math.Max(TextObject.MinSpacing, Math.Min(TextObject.MaxSpacing, Math.Round(100 + p.CharSpacingPct, 1))));
            return r;
        }
    }
}
