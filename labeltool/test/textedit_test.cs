// Unit test for editing a text on the canvas (src/Editor.TextEdit.cs, EditorRender.MapText): the caret map against the
// rendered dots, and the editing model (keys, selection, formats, undo steps, clipboard). Part of editor_test (build.cmd).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace QlLabels.Tests
{
    static partial class EditorTest
    {
        // ---------- step 1: the caret map ----------

        // Each character's ink in the 300 dpi (and 300 x 600) render lies between its two boundaries: the text cut after
        // each boundary is rendered, and its rightmost dot on that line must lie between the boundary before the last
        // character and the boundary itself.
        static void CaretMapTest()
        {
            foreach (bool high in new[] { false, true })
            {
                var layout = LabelLayout.Estimate(Continuous62(90), high, false);
                string res = high ? " (300 x 600)" : "";
                var plain = new TextObject { X = 6, Y = 6, Text = "Hallo Welt, Äpfel!", Size = 14 };
                EditorRender.FitText(plain);
                var runs = new TextObject { X = 6, Y = 14, Size = 12 };
                runs.SetRuns(new[] { new TextRun("Wort "), new TextRun("fett", size: 20, bold: true), new TextRun(" klein", size: 8), new TextRun("Ende") });
                EditorRender.FitText(runs);
                var spaced = new TextObject { X = 6, Y = 24, Text = "A b C", Size = 16, Spacing = 4 };
                EditorRender.FitText(spaced);
                var lines = new TextObject { X = 6, Y = 36, W = 40, H = 14, Text = "erste\nzweite Zeile", Size = 10, Kind = TextKind.Frame };
                foreach (var t in new[] { plain, runs, spaced, lines }) InkBetween(t, layout, res);

                // A paragraph that wraps: every laid-out line's ink within its first and last boundary, the lines as LinesOf.
                var para = new TextObject { X = 50, Y = 6, W = 30, H = 20, Size = 10, Kind = TextKind.Frame };
                para.SetRuns(new[] { new TextRun("Ein Absatz, der in seinem Rahmen "), new TextRun("umbricht", italic: true), new TextRun(" und weiter läuft.") });
                var map = EditorRender.MapText(para);
                var laid = EditorRender.LinesOf(para);
                Check(map.Lines.Count == laid.Count && laid.Count >= 3, "caret map: wrapped lines" + res + " " + map.Lines.Count + "/" + laid.Count);
                for (int i = 0; i < Math.Min(map.Lines.Count, laid.Count); i++)
                {
                    var l = map.Lines[i];
                    string own = para.Text.Substring(l.Start, l.End - l.Start);
                    Check(own == laid[i], "caret map: line " + i + " is “" + own + "”, laid out “" + laid[i] + "”" + res);
                    Check(l.Index.Zip(l.Index.Skip(1), (a, b) => a < b).All(x => x) && l.X.Zip(l.X.Skip(1), (a, b) => a <= b + 0.001f).All(x => x),
                          "caret map: boundaries in order on line " + i + res);
                    var ink = InkSpan(para, layout, l, para);
                    Check(ink != null && ink.Item1 >= l.Left - 0.3f && ink.Item2 <= l.Right + 0.45f,
                          "caret map: wrapped line " + i + " ink " + Show(ink) + " within " + l.Left.ToString("0.00") + ".." + l.Right.ToString("0.00") + res);
                }
                // positions where a line wraps: the spaces at its end show at its end, the next position starts the next line
                int next = map.Lines[1].Start;
                Check(next > map.Lines[0].End && map.LineOf(next - 1) == 0 && map.XOf(next - 1) == map.Lines[0].Right && map.LineOf(next) == 1,
                      "caret map: the space where a line wraps" + res);
                // centred lines: each line's ink within its boundaries
                var centred = new TextObject { X = 48, Y = 32, W = 40, H = 12, Text = "kurz\netwas längere Zeile", Size = 10, Kind = TextKind.Frame, Align = TextAlign.Center };
                var cm = EditorRender.MapText(centred);
                foreach (var l in cm.Lines)
                {
                    var ink = InkSpan(centred, layout, l, centred);
                    Check(ink != null && Math.Abs(ink.Item1 - l.Left) < 0.4f && Math.Abs(ink.Item2 - l.Right) < 0.45f,
                          "caret map: centred line ink " + Show(ink) + " at " + l.Left.ToString("0.00") + ".." + l.Right.ToString("0.00") + res);
                }
                // justified (Blocksatz): the wrapped lines reach both edges of the frame, a paragraph's last line stays left;
                // every character's ink between its caret boundaries (also underlined, with letter spacing)
                foreach (var how in new[] { "plain", "underlined, spaced" })
                {
                    var just = new TextObject { X = 50, Y = 30, W = 34, H = 30, Size = 9, Kind = TextKind.Frame, Align = TextAlign.Justify };
                    bool fancy = how != "plain";
                    just.SetRuns(new[] { new TextRun("Ein Absatz im Blocksatz, der in seinem Rahmen "), new TextRun("mehrmals", bold: true, underline: fancy),
                                         new TextRun(" umbricht und weiter läuft.\nKurz und gut.", underline: fancy) });
                    if (fancy) just.Spacing = 1;
                    var jm = EditorRender.MapText(just);
                    var jl = EditorRender.LinesOf(just);
                    Check(jm.Lines.Count >= 4, "justify: wrapped lines (" + how + ")" + res + " " + jm.Lines.Count);
                    if (!fancy) WordsBetween(just, layout, " justify" + res);
                    var inks = jm.Lines.Select(l => InkSpan(just, layout, l, just)).ToList();
                    int lastOfFirst = jl.Count - 2;   // the first paragraph's last line ("Kurz und gut." is the second paragraph)
                    float left = inks[0].Item1, right = inks.Take(lastOfFirst).Max(k => k.Item2);
                    for (int i = 0; i < lastOfFirst; i++)
                        if (!jl[i].Trim().Contains(" "))   // one word: nothing to spread, it stays left (as in Word)
                            Check(Math.Abs(inks[i].Item1 - left) < 0.4f, "justify: a line of one word stays left (" + how + ")" + res);
                        else Check(inks[i] != null && Math.Abs(inks[i].Item1 - left) < 0.4f && Math.Abs(inks[i].Item2 - right) < 0.5f,
                              "justify: line " + i + " “" + jl[i] + "” fills the frame " + Show(inks[i]) + " to " + right.ToString("0.00") + " (" + how + ")" + res);
                    foreach (int i in new[] { lastOfFirst, jl.Count - 1 })
                        Check(Math.Abs(inks[i].Item1 - left) < 0.4f && inks[i].Item2 < right - 2,
                              "justify: a paragraph's last line stays left " + Show(inks[i]) + " (" + how + ")" + res);
                    var left1 = (TextObject)just.Clone();
                    left1.Align = TextAlign.Left;
                    Check(EditorRender.LinesOf(left1).SequenceEqual(jl), "justify: the same line breaks as left (" + how + ")" + res);
                }
            }

            // Empty text, an empty line, a text ending in a line break: every position has a line.
            var empty = new TextObject { X = 5, Y = 5, W = 30, H = 10, Text = "", Size = 12, Kind = TextKind.Frame };
            var m0 = EditorRender.MapText(empty);
            Check(m0.Lines.Count == 1 && m0.Lines[0].Start == 0 && m0.Lines[0].End == 0, "caret map: empty text has one line at 0");
            var breaks = new TextObject { X = 5, Y = 5, W = 30, H = 20, Text = "a\n\nb\n", Size = 12, Kind = TextKind.Frame };
            var mb = EditorRender.MapText(breaks);
            Check(mb.Lines.Count == 4 && mb.Lines.Select(l => l.Start).SequenceEqual(new[] { 0, 2, 3, 5 }) && mb.LineOf(1) == 0 && mb.LineOf(2) == 1 && mb.LineOf(5) == 3,
                  "caret map: empty lines and a final break: " + string.Join(",", mb.Lines.Select(l => l.Start)));
            Check(mb.Lines.Zip(mb.Lines.Skip(1), (a, b) => b.Top > a.Top).All(x => x), "caret map: lines go down");
            // hit test: a point on the second character's right half gives the boundary after it
            var hm = EditorRender.MapText(new TextObject { X = 5, Y = 5, W = 30, H = 10, Text = "WWW", Size = 20, Kind = TextKind.Frame });
            var hl = hm.Lines[0];
            Check(hm.Hit(new PointF(hl.X[1] + (hl.X[2] - hl.X[1]) * 0.8f, hl.Top + hl.Height / 2)) == 2 && hm.Hit(new PointF(-100, -100)) == 0 && hm.Hit(new PointF(100, 100)) == 3,
                  "caret map: hit test");

            // Fields: laid out as their codes while the text is edited, so the positions match the stored text 1:1.
            var field = new TextObject { X = 5, Y = 5, Text = "Am {date+30:dd.MM.} fertig", Size = 12 };
            EditorRender.FitText(field);
            var shown = EditorRender.MapText(field);
            Check(shown.Lines[0].End != field.Text.Length, "caret map: fields show their values outside editing");
            EditorRender.Codes = field;
            try
            {
                var coded = EditorRender.MapText(field);
                Check(coded.Lines[0].End == field.Text.Length && coded.Lines[0].Index.Length == field.Text.Length + 1, "caret map: fields as codes while editing");
                Check(EditorRender.LinesOf(field)[0] == field.Text, "layout: fields as codes while editing");
            }
            finally { EditorRender.Codes = null; }

            // Speed: a 10-line paragraph laid out, mapped and drawn again where it is (as each key does). Goal: under 20 ms
            // on the ARM PC (the plan); the test stops only at 200 ms (a slow test machine).
            var layout2 = LabelLayout.Estimate(Continuous62(90), false, false);
            var ten = new TextObject { X = 5, Y = 5, W = 50, H = 50, Size = 10, Kind = TextKind.Frame,
                                       Text = string.Join(" ", Enumerable.Repeat("Ein Satz mit einigen Wörtern,", 14)) };
            var doc = new LabelDoc();
            doc.Objects.Add(ten);
            using (var bmp = EditorRender.Render(doc, layout2))
            {
                int n = EditorRender.LinesOf(ten).Count;
                var sw = Stopwatch.StartNew();
                const int rounds = 10;
                for (int i = 0; i < rounds; i++)
                {
                    ten.Text += "x";
                    EditorRender.MapText(ten);
                    EditorRender.Overflows(ten);
                    EditorRender.RenderPart(bmp, doc, layout2, ten.Bounds, DateTime.Now, null);
                }
                double ms = sw.Elapsed.TotalMilliseconds / rounds;
                Console.WriteLine("caret map: " + n + "-line paragraph, per key " + ms.ToString("0.0") + " ms (map, overflow check, partial render)");
                Check(n >= 10 && ms < 200, "caret map: per key " + ms.ToString("0.0") + " ms");
            }
        }

        // ---------- spaces typed at a line's end ----------

        // Each typed space moves the caret, also at a frame's line and text end (they were laid out without those spaces:
        // the caret stayed until the next letter), in every alignment and turned; the frame's text doesn't move for them;
        // many spaces stop at the frame's edge. Underline: spaces between words underlined, also where a word in another
        // format follows; spaces at a line's end not (as Word), with and without letter spacing.
        static void SpacesTest()
        {
            foreach (var kind in new[] { TextKind.Frame, TextKind.Line })
                foreach (var align in new[] { TextAlign.Left, TextAlign.Center, TextAlign.Right })
                    foreach (int turn in new[] { 0, 90 })
                    {
                        string what = kind + " " + align + (turn != 0 ? " turned " + turn : "");
                        var t = new TextObject { X = 5, Y = 5, W = turn == 0 ? 40 : 12, H = turn == 0 ? 12 : 40, Size = 12, Kind = kind, Align = align, Rotation = turn };
                        var e = new TextEditing(t);
                        e.Type("Wort");
                        if (kind == TextKind.Line) EditorRender.FitText(t);
                        float wordStart = Place(t, 0), last = Place(t, e.Caret) - wordStart;
                        bool moves = true, still = true;
                        for (int k = 1; k <= 3; k++)
                        {
                            e.Type(" ");
                            if (kind == TextKind.Line) EditorRender.FitText(t);
                            float at = Place(t, e.Caret);
                            moves &= at - Place(t, 0) > last + 0.3f;   // from the word's start (a right-aligned line moves left)
                            if (kind == TextKind.Frame && align == TextAlign.Left) still &= Math.Abs(Place(t, 0) - wordStart) < 0.001f;
                            if (kind == TextKind.Frame && align != TextAlign.Left) still &= Place(t, 0) < wordStart;   // moves left with each space
                            last = at - Place(t, 0);
                        }
                        Check(e.Text == "Wort   " && moves, "spaces: the caret moves with each space typed at the end (" + what + ")");
                        if (kind == TextKind.Frame) Check(still, (align == TextAlign.Left ? "spaces: the frame's text stays where it is (" : "spaces: the frame's line moves left with the spaces, as aligned (") + what + ")");
                        if (kind == TextKind.Frame && align == TextAlign.Left)
                        {
                            float before = Place(t, e.Caret);
                            e.Type("x");
                            Check(Math.Abs(Place(t, e.Caret - 1) - before) < 0.01f, "spaces: the next letter comes where the caret was (" + what + ")");
                        }
                    }

            // a paragraph's end before a line break, and many spaces: up to the frame's edge, not beyond
            var para = new TextObject { X = 5, Y = 5, W = 40, H = 20, Text = "ab   \ncd", Size = 12, Kind = TextKind.Frame };
            var pm = EditorRender.MapText(para);
            Check(pm.LineOf(5) == 0 && pm.XOf(5) > pm.XOf(4) && pm.XOf(4) > pm.XOf(3) && pm.XOf(3) > pm.XOf(2) && pm.LineOf(6) == 1,
                  "spaces: before a line break every space has its place");
            var many = new TextObject { X = 5, Y = 5, W = 20, H = 10, Text = "ab" + new string(' ', 60), Size = 12, Kind = TextKind.Frame };
            var mm = EditorRender.MapText(many);
            Check(mm.Lines.Count == 1 && mm.XOf(many.Text.Length) <= many.W / 2 + 0.001f && mm.XOf(10) > mm.XOf(2) && !EditorRender.Overflows(many),
                  "spaces: many spaces stop at the frame's edge, the frame doesn't overflow (" + mm.XOf(many.Text.Length).ToString("0.00") + ")");
            Check(EditorRender.LinesOf(many).Single() == "ab", "spaces: the line's visible text is unchanged");

            // underline
            var layout = LabelLayout.Estimate(Continuous62(90), false, false);
            foreach (double spacing in new[] { 0.0, 3.0 })
            {
                string sp = spacing == 0 ? "" : " (letter spacing)";
                Func<TextRun[], TextKind, TextObject> make = (runs, kind) =>
                {
                    var o = new TextObject { X = 4, Y = 4, W = 50, H = 12, Size = 14, Spacing = spacing, Kind = kind };
                    o.SetRuns(runs);
                    if (kind == TextKind.Line) EditorRender.FitText(o);
                    return o;
                };
                foreach (var kind in new[] { TextKind.Line, TextKind.Frame })
                {
                    // "Hallo " underlined, "Welt" not: the underline goes on under the space up to "Welt"
                    var mixed = make(new[] { new TextRun("Hallo ", underline: true), new TextRun("Welt") }, kind);
                    var map = EditorRender.MapText(mixed);
                    float cx = (float)(mixed.X + mixed.W / 2);
                    int run = LongestRun(mixed, layout);
                    float want = (map.XOf(6) - map.XOf(0)) * (float)(layout.DpiX / 25.4);
                    Check(run >= want - 3, "spaces: underlined space before a word in another format" + sp + " " + kind + ": " + run + " of " + want.ToString("0") + " dots");
                    // "Hallo" not, " Welt" underlined: the underline starts under the space
                    var lead = make(new[] { new TextRun("Hallo"), new TextRun(" Welt", underline: true) }, kind);
                    var lm = EditorRender.MapText(lead);
                    float lwant = (lm.XOf(10) - lm.XOf(5)) * (float)(layout.DpiX / 25.4);
                    int lrun = LongestRun(lead, layout);
                    Check(lrun >= lwant - 3, "spaces: underlined space at a stretch's start" + sp + " " + kind + ": " + lrun + " of " + lwant.ToString("0") + " dots");
                    // underlined spaces at the line's end aren't underlined (Word's default)
                    var end = make(new[] { new TextRun("Hallo", underline: true), new TextRun("   ", underline: true) }, kind);
                    var word = make(new[] { new TextRun("Hallo", underline: true) }, kind);
                    if (kind == TextKind.Line) { end.W = word.W; end.X = word.X; }
                    Check(InkCount(end, layout) == InkCount(word, layout), "spaces: no underline under spaces at the line's end" + sp + " " + kind);
                    var allEnd = make(new[] { new TextRun("Hallo "), new TextRun("   ", underline: true) }, kind);
                    var noUl = make(new[] { new TextRun("Hallo ") }, kind);
                    if (kind == TextKind.Line) { allEnd.W = noUl.W; allEnd.X = noUl.X; }
                    Check(InkCount(allEnd, layout) == InkCount(noUl, layout), "spaces: only underlined spaces at the end: no underline" + sp + " " + kind);
                }
            }
        }

        // A caret position's x on the label (mm across the text's own line direction, the box's centre added).
        static float Place(TextObject t, int p)
        {
            var map = EditorRender.MapText(t);
            return (float)((t.Rotation == 90 || t.Rotation == 270 ? t.Y + t.H / 2 : t.X + t.W / 2)) + map.XOf(p);
        }

        static Bitmap RenderOne(TextObject t, LabelLayout layout)
        {
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            return EditorRender.Render(doc, layout);
        }

        static int InkCount(TextObject t, LabelLayout layout)
        {
            int n = 0;
            using (var bmp = RenderOne(t, layout))
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0) n++;
            return n;
        }

        // The longest run of black dots in one row (the underline).
        static int LongestRun(TextObject t, LabelLayout layout)
        {
            int best = 0;
            using (var bmp = RenderOne(t, layout))
                for (int y = 0; y < bmp.Height; y++)
                {
                    int run = 0;
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        run = (bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0 ? run + 1 : 0;
                        best = Math.Max(best, run);
                    }
                }
            return best;
        }

        // ---------- step 2: the editing model ----------

        static string Fmt(TextEditing e)
        {
            return string.Join("|", e.Runs.Select(r => r.Text.Replace("\n", "¶") + (r.Bold == true ? ":b" : "") + (r.Italic == true ? ":i" : "") + (r.Size != null && r.Size != 12 ? ":" + r.Size : "")));
        }

        static TextEditing Edit(string text, TextKind kind = TextKind.Frame)
        {
            var t = new TextObject { X = 5, Y = 5, W = 40, H = 30, Text = text, Size = 12, Kind = kind };
            if (kind == TextKind.Line) EditorRender.FitText(t);
            return new TextEditing(t);
        }

        static void EditingTest()
        {
            // typing at the end (a new session starts there), the object follows at once
            var e = Edit("Hallo");
            int first = 0;
            e.FirstChange += () => first++;
            Check(e.Caret == 5 && !e.HasSelection, "editing: starts with the caret at the end");
            e.Type(" Welt");
            Check(e.Target.Text == "Hallo Welt" && e.Caret == 10 && first == 1 && e.Changed, "editing: typing goes into the object; the window's undo step once");
            e.Type("!");
            Check(first == 1, "editing: the window's undo step only once");
            // control characters are left out, tab becomes a space; in an artistic text a line break too
            e.Type("\x01\x7f\ta");
            Check(e.Target.Text == "Hallo Welt! a", "editing: control characters left out: " + e.Target.Text);
            var line = Edit("Eins", TextKind.Line);
            line.Type("\r\nZwei");
            line.NewParagraph();
            Check(line.Target.Text == "Eins Zwei", "editing: artistic text stays one line: " + line.Target.Text);
            // surrogate pairs and combining accents move as one character
            var emoji = Edit("a\U0001F600éb");
            emoji.Move(CaretMove.Left, false);
            Check(emoji.Caret == 5, "editing: Left over b");
            emoji.Move(CaretMove.Left, false);
            Check(emoji.Caret == 3, "editing: Left over e + accent: " + emoji.Caret);
            emoji.Move(CaretMove.Left, false);
            Check(emoji.Caret == 1, "editing: Left over the emoji (surrogate pair): " + emoji.Caret);
            emoji.Move(CaretMove.Right, false);
            emoji.Backspace(false);
            Check(emoji.Target.Text == "aéb", "editing: Backspace removes the whole emoji");

            // words: Ctrl+Left/Right, Ctrl+Backspace/Delete, double-click; paragraphs: triple-click
            var w = Edit("Eins zwei, drei\nvier");
            w.Move(CaretMove.TextStart, false);
            w.Move(CaretMove.WordRight, false);
            Check(w.Caret == 5, "editing: Ctrl+Right to the next word: " + w.Caret);
            w.Move(CaretMove.WordRight, false);
            Check(w.Caret == 9, "editing: Ctrl+Right stops at the comma: " + w.Caret);
            w.Move(CaretMove.WordLeft, false);
            Check(w.Caret == 5, "editing: Ctrl+Left: " + w.Caret);
            w.SelectWord(6);
            Check(w.SelectedText == "zwei", "editing: double-click selects the word: " + w.SelectedText);
            w.SelectParagraph(2);
            Check(w.SelectedText == "Eins zwei, drei", "editing: triple-click selects the paragraph");
            w.SetCaret(15, false);
            w.Delete(true);
            Check(w.Target.Text == "Eins zwei, dreivier", "editing: Ctrl+Delete at the line end removes the break: " + w.Target.Text);
            w.SetCaret(9, false);
            w.Backspace(true);
            Check(w.Target.Text == "Eins , dreivier", "editing: Ctrl+Backspace removes the word: " + w.Target.Text);

            // Shift+arrows select; an arrow without Shift collapses to the selection's side; typing replaces it
            var s = Edit("abcdef");
            s.SetCaret(1, false);
            s.Move(CaretMove.Right, true); s.Move(CaretMove.Right, true);
            Check(s.SelectionStart == 1 && s.SelectionLength == 2 && s.SelectedText == "bc", "editing: Shift+Right selects");
            s.Move(CaretMove.Left, false);
            Check(s.Caret == 1 && !s.HasSelection, "editing: Left collapses to the start");
            s.Select(1, 3);
            s.Type("X");
            Check(s.Target.Text == "aXdef" && s.Caret == 2, "editing: typing replaces the selection");
            s.SelectAll();
            Check(s.SelectedText == "aXdef", "editing: Ctrl+A selects the whole text");

            // lines: Home/End on the laid-out line, Up/Down keep the column, beyond the first/last line to the ends
            var l = Edit("erste Zeile\nzweite Zeile\ndritte");
            l.SetCaret(14, false);
            l.Move(CaretMove.Home, false);
            Check(l.Caret == 12, "editing: Home: " + l.Caret);
            l.Move(CaretMove.End, false);
            Check(l.Caret == 24, "editing: End: " + l.Caret);
            l.SetCaret(3, false);
            l.Move(CaretMove.Down, false);
            int down = l.Caret;
            l.Move(CaretMove.Down, false);
            l.Move(CaretMove.Up, false);
            Check(down > 12 && down < 18 && l.Caret == down, "editing: Down and Up keep the column: " + down + "/" + l.Caret);
            l.SetCaret(3, false);
            l.Move(CaretMove.Up, false);
            Check(l.Caret == 0, "editing: Up on the first line goes to the start");
            l.Move(CaretMove.TextEnd, true);
            Check(l.SelectionLength == l.Length, "editing: Ctrl+Shift+End selects to the end");
            // a wrapped paragraph: End stops before the space where the line wraps
            var wrap = new TextEditing(new TextObject { X = 5, Y = 5, W = 20, H = 30, Size = 12, Kind = TextKind.Frame, Text = "Wort Wort Wort Wort Wort Wort" });
            wrap.SetCaret(0, false);
            wrap.Move(CaretMove.End, false);
            var wm = wrap.Map();
            Check(wm.Lines.Count > 1 && wrap.Caret == wm.Lines[0].End && wrap.Caret < wm.Lines[1].Start, "editing: End on a wrapped line: " + wrap.Caret);
            wrap.Move(CaretMove.Down, false);
            Check(wm.LineOf(wrap.Caret) == 1, "editing: Down to the next laid-out line");

            // formats: on the selection; with nothing selected on what is typed next; the rows show it
            var f = Edit("eins zwei drei");
            f.Select(5, 9);
            f.Toggle(FontStyle.Bold);
            Check(Fmt(f) == "eins |zwei:b| drei", "editing: Ctrl+B on the selection: " + Fmt(f));
            Check(f.Current.Bold == true, "editing: the rows show the selection's format");
            f.Select(0, 9);
            Check(f.Current.Bold == null, "editing: a mixed selection shows mixed");
            f.Toggle(FontStyle.Bold);
            Check(Fmt(f) == "eins zwei:b| drei", "editing: mixed turns on: " + Fmt(f));
            f.SetCaret(f.Length, false);
            f.Toggle(FontStyle.Italic);
            Check(Fmt(f) == "eins zwei:b| drei" && f.Current.Italic == true, "editing: format at the caret waits for typing");
            f.Type(" vier");
            Check(Fmt(f) == "eins zwei:b| drei| vier:i", "editing: typed in the chosen format: " + Fmt(f));
            f.Select(0, 4);
            f.Restyle(r => new TextRun(r.Text, r.Font, 20.0, r.Bold, r.Italic, r.Underline));
            Check(Fmt(f).StartsWith("eins:b:20| zwei:b"), "editing: size on the selection: " + Fmt(f));
            // typing continues in the format before the caret
            f.SetCaret(9, false);
            f.Type("X");
            Check(Fmt(f).Contains("zweiX:b"), "editing: typing takes the format before the caret: " + Fmt(f));
            // an artistic text has words in their own format too: B on a selection makes only it bold
            var a = Edit("ganz fett", TextKind.Line);
            a.Select(0, 4);
            a.Toggle(FontStyle.Bold);
            Check(Fmt(a) == "ganz:b| fett" && a.Target.FormatRuns != null, "editing: artistic text: Ctrl+B on the selection: " + Fmt(a));

            // undo inside the session: typing up to a word break, each format change, each paste; redo
            var u = Edit("");
            u.Type("a"); u.Type("b"); u.Type(" "); u.Type("c"); u.Type("d");
            u.Undo();
            Check(u.Target.Text == "ab ", "editing: undo removes the last word typed: “" + u.Target.Text + "”");
            u.Undo();
            Check(u.Target.Text == "", "editing: undo the first word: “" + u.Target.Text + "”");
            Check(!u.Undo(), "editing: nothing more to undo");
            u.Redo(); u.Redo();
            Check(u.Target.Text == "ab cd", "editing: redo both: “" + u.Target.Text + "”");
            u.Select(0, 2); u.Toggle(FontStyle.Bold);
            u.Undo();
            Check(u.Target.FormatRuns == null && u.Target.Text == "ab cd", "editing: undo a format change");
            u.SetCaret(5, false);
            u.Backspace(false); u.Backspace(false);
            u.Undo();
            Check(u.Target.Text == "ab cd", "editing: Backspaces in a row are one step");

            // clipboard: the selection as text and RTF (read back with its formats); RTF paste keeps formats in a
            // paragraph text, plain text takes the caret's format, an artistic text takes the text only
            var c = Edit("eins zwei");
            c.Select(5, 9);
            c.Toggle(FontStyle.Bold);
            c.Select(3, 9);
            Check(c.CopyText() == "s zwei", "editing: copy as text");
            List<string> missing;
            var back = RtfRuns.Read(c.CopyRtf(), out missing);
            Check(back.Text == "s zwei" && back.Runs.Count == 2 && back.Runs[1].Bold == true && back.Runs[0].Bold == false && back.Runs[1].Size == 12,
                  "editing: copy as RTF, read back: " + string.Join("|", back.Runs.Select(r => r.Text + r.Bold)));
            var umlaut = RtfRuns.Read(TextEditing.Rtf(new List<TextRun> { new TextRun("Grüße {x} \\ €\n2", "Arial", 9, false, true, false) }), out missing);
            Check(umlaut.Text == "Grüße {x} \\ €\n2" && umlaut.Runs[0].Italic == true && umlaut.Runs[0].Size == 9, "editing: RTF escapes, Unicode, breaks: " + umlaut.Text);
            var p = Edit("A B");
            p.SetCaret(2, false);
            p.Paste("fett\r\nneu", TextEditing.Rtf(new List<TextRun> { new TextRun("fett", "Segoe UI", 12, true, false, false), new TextRun("\nneu ", "Segoe UI", 12, false, false, false) }));
            Check(p.Target.Text == "A fett\nneu B" && Fmt(p).Contains("fett:b"), "editing: RTF paste keeps formats: " + Fmt(p));
            p.Undo();
            Check(p.Target.Text == "A B", "editing: a paste is one undo step");
            p.Paste("x\r\ny", null, true);
            Check(p.Target.Text == "A x\nyB", "editing: plain paste: " + p.Target.Text.Replace("\n", "¶"));
            // paste limits (security review 2026-11-09, M3): 100,000 characters into a text, 4 M characters read
            Func<string, string, bool> tooLong = (t, r) => TextEditing.PasteTooLong(ref t, ref r);
            string big = new string('a', TextEditing.MaxPasteChars), bigRtf = @"{\rtf1 " + new string('b', TextEditing.MaxPasteChars + 1) + "}";
            Check(!tooLong(big, null) && tooLong(big + "a", null), "paste limit: 100,000 characters of plain text");
            Check(tooLong(null, bigRtf) && !tooLong(null, @"{\rtf1 kurz}"), "paste limit: RTF only, measured by its text");
            string huge = @"{\rtf1 {\pict " + new string('0', TextEditing.MaxClipboardChars) + "} kurz}", t2 = "kurz", r2 = huge;
            Check(!TextEditing.PasteTooLong(ref t2, ref r2) && r2 == null, "paste limit: an RTF over 4 M characters (a picture) falls back to the text");
            Check(tooLong(null, huge) && tooLong(new string('c', TextEditing.MaxClipboardChars + 1), null), "paste limit: over 4 M characters without text refused");
            var pb = Edit("A");
            string okText = big, okRtf = null;
            if (!TextEditing.PasteTooLong(ref okText, ref okRtf)) pb.Paste(okText, okRtf, true);
            Check(pb.Target.Text.Length == TextEditing.MaxPasteChars + 1, "paste limit: 100,000 characters still paste: " + pb.Target.Text.Length);
            var pl = Edit("Zeile", TextKind.Line);
            pl.Paste("a\r\nb", TextEditing.Rtf(new List<TextRun> { new TextRun("a\nb", "Arial", 30, true, false, false) }));
            Check(pl.Target.Text == "Zeilea b" && Fmt(pl) == "Zeile|a b:b:30", "editing: artistic text: a paste from Word keeps its format, line breaks become spaces: " + Fmt(pl));
            var cut = Edit("weg damit");
            cut.Select(0, 4);
            cut.CutSelection();
            Check(cut.Target.Text == "damit", "editing: cut");

            // fields: inserted at the caret as one step; their ranges for the canvas
            var fld = Edit("Datum: ");
            fld.InsertField("{date}");
            Check(fld.Target.Text == "Datum: {date}" && fld.Caret == 13, "editing: field at the caret");
            var ranges = Fields.Ranges("a {date} {{x}} {time:HH:mm}");
            Check(ranges.Count == 2 && ranges[0].Key == 2 && ranges[0].Value == 6 && ranges[1].Key == 15, "fields: ranges without the escapes");

            // an empty text: a format change sets the object's own format (both kinds)
            foreach (var kind in new[] { TextKind.Line, TextKind.Frame })
            {
                var em = Edit("", kind);
                em.Toggle(FontStyle.Italic);
                Check(em.Target.Italic, "editing: format of an empty text (" + kind + ")");
            }
            ReviewTest();
        }

        // Review 2026-11-09: edge cases of the editing model.
        static void ReviewTest()
        {
            // a letter with a combining accent (decomposed, as pasted from a Mac): words and deletions keep it whole
            var acc = Edit("éx yz");
            acc.SetCaret(0, false);
            acc.Move(CaretMove.WordRight, false);
            Check(acc.Caret == 4, "review: Ctrl+Right over a combining accent: " + acc.Caret);
            acc.SelectWord(1);
            Check(acc.SelectionStart == 0 && acc.SelectionLength == 3, "review: double-click selects a word with a combining accent: " + acc.SelectionStart + "+" + acc.SelectionLength);
            var accb = Edit("ab é");
            accb.Backspace(true);
            Check(accb.Target.Text == "ab ", "review: Ctrl+Backspace removes a letter with its accent: " + accb.Target.Text.Length);
            var accd = Edit("éx");
            accd.SetCaret(0, false);
            accd.Delete(true);
            Check(accd.Target.Text == "", "review: Ctrl+Delete removes a letter with its accent: " + accd.Target.Text.Length);

            // half a surrogate pair (a lone WM_CHAR, a damaged clipboard text) is not stored
            var sur = Edit("a");
            sur.Type("\uDC00");
            sur.Paste("b\uD800c", null, true);
            Check(sur.Target.Text == "abc", "review: lone surrogates left out: " + sur.Target.Text.Length);
            var emoji = Edit("a");
            emoji.Type("😀");
            Check(emoji.Target.Text == "a😀" && emoji.Caret == 3, "review: an emoji typed whole");

            // the panel makes a paragraph text an artistic one while editing: the session's undo can't bring back its breaks
            var kind = Edit("a\nb");
            kind.Type("c");
            kind.Target.Kind = TextKind.Line;
            kind.Target.Text = kind.Target.Text.Replace('\n', ' ');
            kind.Reload();
            kind.Undo();
            Check(!kind.Target.Text.Contains("\n"), "review: undo after the kind changed keeps an artistic text on one line: " + kind.Target.Text.Replace("\n", "¶"));

            // an empty artistic text's format change is undone too
            var emu = Edit("", TextKind.Line);
            emu.Toggle(FontStyle.Italic);
            emu.Undo();
            Check(!emu.Target.Italic, "review: undo of an empty artistic text's format");

            // a word wider than the frame breaks inside it: End stays on the line (not at the next line's start)
            var wrap = new TextObject { X = 5, Y = 5, W = 14, H = 30, Text = "Donaudampfschifffahrtsgesellschaft", Size = 12, Kind = TextKind.Frame };
            var we = new TextEditing(wrap);
            var wm = EditorRender.MapText(wrap);
            Check(wm.Lines.Count > 1, "review: the long word wraps");
            if (wm.Lines.Count > 1)
            {
                we.SetCaret(0, false);
                we.Move(CaretMove.End, false);
                Check(wm.LineOf(we.Caret) == 0 || wm.Lines[0].End != wm.Lines[1].Start, "review: End on a word broken inside: caret " + we.Caret + " on line " + wm.LineOf(we.Caret)
                      + " (line 0 ends " + wm.Lines[0].End + ", line 1 starts " + wm.Lines[1].Start + ")");
            }
        }

        static string Show(Tuple<float, float> ink) { return ink == null ? "none" : ink.Item1.ToString("0.00") + ".." + ink.Item2.ToString("0.00"); }

        // ---------- letter spacing and invert per selection (runs) ----------

        // Stored per run (the object's own when all characters share it; Invert only then, the black box with its margin
        // is the whole text's), drawn where the caret map puts the characters (an inverted word: black behind it, white
        // letters, the rest of the line on white), edited on the selection with one undo step, copied as RTF and read
        // back (Word's \expndtw and shading, WordPad's highlight).
        static void RunFormatsTest()
        {
            var t = new TextObject { X = 4, Y = 4, W = 56, H = 20, Size = 12, Kind = TextKind.Frame };
            t.SetRuns(new[] { new TextRun("weit ", spacing: 4), new TextRun("hell "), new TextRun("dunkel", invert: true), new TextRun(" hell") });
            Check(!t.Invert && t.Spacing == 0 && t.Runs != null && t.Runs.Count == 4 && t.Runs[0].Spacing == 4 && t.Runs[2].Invert == true && t.Runs[1].Plain,
                  "run formats: spacing and invert per run, the object's stay 0 and off");
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            string json = doc.ToJson();
            var back = (TextObject)LabelDoc.FromJson(json).Objects[0];
            Check(json.Contains("{\"t\":\"weit \",\"spacing\":4}") && json.Contains("{\"t\":\"dunkel\",\"invert\":true}") && LabelDoc.FromJson(json).ToJson() == json &&
                  back.Runs[2].Invert == true && back.Runs[0].Spacing == 4, "run formats: JSON round trip: " + json);
            var all = new TextObject { Size = 12 };
            all.SetRuns(new[] { new TextRun("ganz ", invert: true), new TextRun("invertiert", invert: true, spacing: 2), new TextRun("!", invert: true, spacing: 2) });
            Check(all.Invert && all.Spacing == 2 && all.Runs != null && all.Runs.Count == 2 && all.Runs[0].Spacing == 0 && all.Runs.All(r => r.Invert == null),
                  "run formats: all inverted is the object's Invert, the main spacing the object's");
            var bad = LabelDoc.FromJson(json.Replace("\"spacing\":4", "\"spacing\":99"));
            Check(((TextObject)bad.Objects[0]).Runs[0].Spacing == TextObject.MaxSpacing, "run formats: a run's spacing limited to " + TextObject.MaxSpacing);

            // drawn: the inverted word black with white letters inside its caret boundaries, white around it
            var layout = LabelLayout.Estimate(Continuous62(70), false, false);
            var map = EditorRender.MapText(t);
            var l = map.Lines[0];
            int a = t.Text.IndexOf("dunkel"), b = a + "dunkel".Length;
            Check(map.Lines.Count == 1, "run formats: one line (" + map.Lines.Count + ")");
            float xa = l.X[Array.IndexOf(l.Index, a)], xb = l.X[Array.IndexOf(l.Index, b)];
            var dark = Coverage(t, layout, l, xa + 0.3f, xb - 0.3f);
            var lightBefore = Coverage(t, layout, l, l.X[0], xa - 0.6f);
            var lightAfter = Coverage(t, layout, l, xb + 0.6f, l.Right);
            Check(dark > 0.6 && dark < 0.98, "run formats: the inverted word black with white letters (" + dark.ToString("0.00") + " black)");
            Check(lightBefore < 0.3 && lightAfter < 0.3, "run formats: the words around it on white (" + lightBefore.ToString("0.00") + ", " + lightAfter.ToString("0.00") + ")");
            // the spaced word: its ink between its caret boundaries, wider than unspaced
            var plain = (TextObject)t.Clone();
            plain.SetRuns(t.FullRuns().Select(r => r.With(spacing: 0, invert: false)).ToList());
            var pm = EditorRender.MapText(plain).Lines[0];
            float wide = l.X[Array.IndexOf(l.Index, 4)] - l.X[0], narrow = pm.X[Array.IndexOf(pm.Index, 4)] - pm.X[0];
            Check(Math.Abs(wide - narrow - 4 * 4 * 25.4f / 72) < 0.05f, "run formats: 4 pt after each of “weit” (" + (wide - narrow).ToString("0.00") + " mm wider)");
            var spacedOnly = new TextObject { X = 4, Y = 30, Size = 14 };
            spacedOnly.SetRuns(new[] { new TextRun("Ab "), new TextRun("CdE", spacing: 5, underline: true), new TextRun(" mn", spacing: -1) });
            EditorRender.FitText(spacedOnly);
            InkBetween(spacedOnly, layout, " (spacing per run)");
            var artistic = new TextObject { X = 4, Y = 42, Size = 14 };
            artistic.SetRuns(new[] { new TextRun("Gr"), new TextRun("af", invert: true), new TextRun("ik") });
            EditorRender.FitText(artistic);
            var am = EditorRender.MapText(artistic).Lines[0];
            Check(Coverage(artistic, layout, am, am.X[2] + 0.2f, am.X[4] - 0.2f) > 0.5 && Coverage(artistic, layout, am, am.X[0], am.X[2] - 0.5f) < 0.35,
                  "run formats: artistic text, two letters inverted");

            // a paragraph text inverted as a whole (2026-11-09): black bands behind its lines, touching from one line
            // to the next (also at 1.5 line spacing), each as long as its line; an empty line, the rest of the frame and
            // the space beyond it stay white. An artistic text keeps its black box 1 mm beyond its frame.
            foreach (double pitch in new[] { 1.0, 1.5 })
            {
                var pf = new TextObject { X = 4, Y = 4, W = 40, H = 34, Size = 10, Kind = TextKind.Frame, LineHeight = pitch,
                                          Text = "Eine lange erste Zeile, die im Rahmen umbricht\n\nkurz" };
                pf.SetRuns(pf.FullRuns().Select(r => r.With(invert: true)).ToList());
                Check(pf.Invert && !pf.InvertBox && pf.FormatRuns == null, "run formats: all of a paragraph text inverted is the object's Invert");
                var pl = EditorRender.MapText(pf).Lines;
                var texts = EditorRender.LinesOf(pf);
                int blank = texts.IndexOf("");
                string res = " (line spacing " + pitch + ")";
                Check(pl.Count == 4 && blank == 2, "run formats: two wrapped lines, an empty one, “kurz”: " + string.Join("|", texts) + res);
                float half = (float)pf.W / 2;
                Check(Coverage(pf, layout, pl[0], pl[0].Left + 0.3f, pl[0].Right - 0.3f) > 0.6, "run formats: paragraph inverted, its first line black" + res);
                // between the first two lines: the bands meet in the middle
                float between = (pl[0].Top + pl[0].Height + pl[1].Top) / 2;
                Check(Coverage(pf, layout, pl[0].Left + 1, between - 0.15f, pl[0].Left + 6, between + 0.15f) > 0.97,
                      "run formats: the bands of two lines touch (no white stripe)" + res);
                Check(Coverage(pf, layout, -half + 1, pl[2].Top + 0.2f, half - 1, pl[2].Top + pl[2].Height - 0.2f) < 0.01, "run formats: an empty line stays white" + res);
                Check(Coverage(pf, layout, pl[3].Right + 0.6f, pl[3].Top, half - 0.5f, pl[3].Top + pl[3].Height) < 0.01, "run formats: beside a short line white" + res);
                Check(Coverage(pf, layout, -half + 0.5f, pl[3].Top + pl[3].Height + 0.5f, half - 0.5f, (float)pf.H / 2 - 0.5f) < 0.01,
                      "run formats: the frame below the text white" + res);
                Check(Coverage(pf, layout, -half - 0.9f, pl[0].Top, -half - 0.1f, pl[0].Top + pl[0].Height) < 0.01, "run formats: no black beyond the frame" + res);
                Check(Coverage(pf, layout, pl[0].Left - TextPadMm + 0.05f, pl[0].Top + 0.3f, pl[0].Left - 0.05f, pl[0].Top + pl[0].Height - 0.3f) > 0.9,
                      "run formats: the band starts a little before the line's first letter" + res);
            }
            var boxed = new TextObject { X = 4, Y = 50, Size = 12, Text = "Box", Invert = true };
            EditorRender.FitText(boxed);
            Check(boxed.InvertBox && Coverage(boxed, layout, -(float)boxed.W / 2 - 0.9f, -1, -(float)boxed.W / 2 - 0.1f, 1) > 0.97,
                  "run formats: an inverted artistic text keeps its black box beyond its frame");

            // edited: the selection only, one undo step; Undo brings the object's own format back
            var e = new TextEditing(new TextObject { Size = 12, Text = "eins zwei drei" });
            e.Select(5, 9);
            e.Restyle(r => r.With(invert: true));
            e.Restyle(r => r.With(spacing: 3));
            var runs = e.Target.FullRuns();
            Check(runs.Count == 3 && runs[1].Text == "zwei" && runs[1].Invert == true && runs[1].Spacing == 3 && runs[0].Invert == false && runs[2].Spacing == 0,
                  "run formats: invert and spacing on the selection: " + string.Join("|", runs.Select(r => r.Text + ":" + r.Invert + ":" + r.Spacing)));
            Check(e.Current.Invert == true && e.Current.Spacing == 3, "run formats: the selection's format");
            e.Select(0, 9);
            Check(e.Current.Invert == null && e.Current.Spacing == null, "run formats: mixed is null (the toolbar hatches and blanks)");
            e.Undo(); e.Undo();
            Check(e.Target.FormatRuns == null && !e.Target.Invert && e.Target.Spacing == 0, "run formats: undo");
            var empty = new TextEditing(new TextObject { Size = 12, Text = "" });
            empty.Restyle(r => r.With(invert: true, spacing: 2));
            Check(empty.Target.Invert && empty.Target.Spacing == 2, "run formats: an empty text takes them as its own");

            // RTF: our copy read back; Word's shading and WordPad's highlight
            string rtf = TextEditing.Rtf(t.FullRuns());
            List<string> missing;
            var rd = RtfRuns.Read(rtf, out missing);
            Check(rtf.Contains("\\expndtw80") && rtf.Contains("\\chcbpat1") && rd.Text == t.Text &&
                  rd.Runs.Any(r => r.Text == "dunkel" && r.Invert == true) && rd.Runs.Any(r => r.Text == "weit " && r.Spacing == 4) &&
                  rd.Runs.Where(r => r.Text.Contains("hell")).All(r => r.Invert == false && r.Spacing == 0), "run formats: RTF round trip: " + rtf);
            var word = RtfRuns.Read(@"{\rtf1\ansi{\fonttbl{\f0 Arial;}}{\colortbl;\red0\green0\blue0;\red255\green255\blue0;}" +
                                    @"\f0\fs24 a {\chshdng0\chcbpat1\cf8 schwarz} {\highlight2 gelb} {\expnd10\expndtw50 weit}\par}", out missing);
            Check(word.Runs.Any(r => r.Text == "schwarz" && r.Invert == true) && word.Runs.Any(r => r.Text.Contains("gelb") && r.Invert == false) &&
                  word.Runs.Any(r => r.Text == "weit" && r.Spacing == 2.5), "run formats: Word's shading inverts, a light highlight doesn't, \\expndtw 2.5 pt: " +
                  string.Join("|", word.Runs.Select(r => r.Text + ":" + r.Invert + ":" + r.Spacing)));
        }

        // The share of black dots in a line's band between two x (mm, the object's frame).
        static double Coverage(TextObject t, LabelLayout layout, CaretLine l, float x0, float x1)
        {
            return Coverage(t, layout, x0, l.Top, x1, l.Top + l.Height);
        }

        // The share of black dots between x0..x1 and y0..y1 (mm, the object's frame: its box centred on 0,0), one dot in
        // from the edges.
        static double Coverage(TextObject t, LabelLayout layout, float x0, float top, float x1, float bottom)
        {
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            float cx = (float)(t.X + t.W / 2), cy = (float)(t.Y + t.H / 2);
            using (var bmp = EditorRender.Render(doc, layout))
            {
                int y0 = (int)Math.Ceiling((cy + top - layout.Printable.Y) * ky) + 1, y1 = (int)Math.Floor((cy + bottom - layout.Printable.Y) * ky) - 1;
                int c0 = (int)Math.Ceiling((cx + x0 - layout.Printable.X) * kx), c1 = (int)Math.Floor((cx + x1 - layout.Printable.X) * kx);
                int black = 0, n = 0;
                for (int y = Math.Max(0, y0); y <= Math.Min(bmp.Height - 1, y1); y++)
                    for (int x = Math.Max(0, c0); x <= Math.Min(bmp.Width - 1, c1); x++)
                    {
                        n++;
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0) black++;
                    }
                return n == 0 ? 0 : (double)black / n;
            }
        }

        const float TextPadMm = 0.3f;   // EditorRender.TextPad

        static void InkBetween(TextObject t, LabelLayout layout, string res)
        {
            var map = EditorRender.MapText(t);
            Check(map.Lines.Last().End == t.Text.Length, "caret map: “" + t.Text + "” reaches the end" + res);
            foreach (var l in map.Lines)
                for (int k = 1; k < l.Index.Length; k++)
                {
                    string ch = t.Text.Substring(l.Index[k - 1], l.Index[k] - l.Index[k - 1]);
                    if (ch.Trim().Length == 0) continue;
                    // the text up to this boundary, in the same box (positions don't move)
                    var cut = (TextObject)t.Clone();
                    cut.SetRuns(Cut(t.FullRuns(), l.Index[k]));
                    if (cut.Text.Length == 0) continue;
                    var ink = InkSpan(cut, layout, l, t);
                    float tol = 0.25f;
                    Check(ink != null && ink.Item2 >= l.X[k - 1] - tol && ink.Item2 <= l.X[k] + tol,
                          "caret map: “" + ch + "” of “" + t.Text + "” ends at " + Show(ink) + ", boundaries " + l.X[k - 1].ToString("0.00") + ".." + l.X[k].ToString("0.00") + res);
                }
        }

        static List<TextRun> Cut(List<TextRun> full, int length)
        {
            var r = new List<TextRun>();
            int at = 0;
            foreach (var x in full)
            {
                int n = Math.Max(0, Math.Min(x.Text.Length, length - at));
                if (n > 0) r.Add(x.WithText(x.Text.Substring(0, n)));
                at += x.Text.Length;
            }
            return r;
        }

        // The leftmost and rightmost black dot of an object's line (rows between the line's top and bottom), in mm of the
        // object's own frame (box centred on 0,0); null when the line has no ink.
        // Justified lines: each word's ink between its caret boundaries and no ink in the gap between two words (the gaps
        // the drawing adds are the ones the caret map adds).
        static void WordsBetween(TextObject t, LabelLayout layout, string res)
        {
            var map = EditorRender.MapText(t);
            foreach (var l in map.Lines)
            {
                var ink = InkColumns(t, layout, l);
                for (int k = 0; k < l.Index.Length - 1; )
                {
                    int a = k;
                    bool space = t.Text[l.Index[k]] == ' ';
                    while (k < l.Index.Length - 1 && (t.Text[l.Index[k]] == ' ') == space) k++;
                    float x0 = l.X[a], x1 = l.X[k];
                    if (space)
                    {
                        if (k >= l.Index.Length - 1) continue;   // the spaces at the line's end
                        bool clear = !ink.Any(x => x > x0 + 0.25f && x < x1 - 0.25f);
                        Check(clear, "justify: no ink in the gap " + x0.ToString("0.00") + ".." + x1.ToString("0.00") + res);
                    }
                    else
                    {
                        var own = ink.Where(x => x > x0 - 0.45f && x < x1 + 0.45f).ToList();
                        string word = t.Text.Substring(l.Index[a], l.Index[k] - l.Index[a]);
                        Check(own.Count > 0 && own.Min() < x0 + (x1 - x0) / 3 && own.Max() > x1 - (x1 - x0) / 3,
                              "justify: “" + word + "” ink " + (own.Count > 0 ? own.Min().ToString("0.00") + ".." + own.Max().ToString("0.00") : "none") +
                              " at its boundaries " + x0.ToString("0.00") + ".." + x1.ToString("0.00") + res);
                    }
                }
            }
        }

        // The x (mm, as the caret map) of every inked pixel column in a line's band.
        static List<float> InkColumns(TextObject t, LabelLayout layout, CaretLine l)
        {
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            float cx = (float)(t.X + t.W / 2), cy = (float)(t.Y + t.H / 2);
            var xs = new List<float>();
            using (var bmp = EditorRender.Render(doc, layout))
            {
                int y0 = Math.Max(0, (int)Math.Floor((cy + l.Top - layout.Printable.Y) * ky)), y1 = Math.Min(bmp.Height - 1, (int)Math.Ceiling((cy + l.Top + l.Height - layout.Printable.Y) * ky));
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var inked = new bool[bmp.Width];
                try
                {
                    var row = new int[bmp.Width];
                    for (int y = y0; y <= y1; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width);
                        for (int x = 0; x < bmp.Width; x++) if ((row[x] & 0xFFFFFF) == 0) inked[x] = true;
                    }
                }
                finally { bmp.UnlockBits(data); }
                for (int x = 0; x < inked.Length; x++) if (inked[x]) xs.Add((x + 0.5f) / kx + (float)layout.Printable.X - cx);
            }
            return xs;
        }

        static Tuple<float, float> InkSpan(TextObject drawn, LabelLayout layout, CaretLine l, TextObject frameOf)
        {
            var doc = new LabelDoc();
            doc.Objects.Add(drawn);
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            float cx = (float)(frameOf.X + frameOf.W / 2), cy = (float)(frameOf.Y + frameOf.H / 2);
            using (var bmp = EditorRender.Render(doc, layout))
            {
                int y0 = (int)Math.Floor((cy + l.Top - layout.Printable.Y) * ky), y1 = (int)Math.Ceiling((cy + l.Top + l.Height - layout.Printable.Y) * ky);
                y0 = Math.Max(0, y0); y1 = Math.Min(bmp.Height - 1, y1);
                int lo = int.MaxValue, hi = -1;
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[bmp.Width];
                    for (int y = y0; y <= y1; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width);
                        for (int x = 0; x < bmp.Width; x++)
                            if ((row[x] & 0xFFFFFF) == 0) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); }
                    }
                }
                finally { bmp.UnlockBits(data); }
                if (hi < 0) return null;
                return Tuple.Create(lo / kx + layout.Printable.X - cx, (hi + 1) / kx + layout.Printable.X - cx);
            }
        }
    }
}
