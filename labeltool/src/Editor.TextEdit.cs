// Label editor, editing a text on the canvas: the caret map (where each character is drawn,
// EditorRender.MapText) and the editing model (TextEditing: runs, caret, selection, typing format, the session's own undo
// steps, clipboard text and RTF). No UI here: the canvas (EditorCanvas.Editing.cs) and the window use it.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QlLabels
{
    // One laid-out line of a text: its character boundaries (index in the stored text, x in mm), in order.
    sealed class CaretLine
    {
        public int[] Index;
        public float[] X;
        public float Top, Height, Ascent;   // mm, in the object's own frame
        public int Start { get { return Index[0]; } }
        public int End { get { return Index[Index.Length - 1]; } }
        public float Left { get { return X[0]; } }
        public float Right { get { return X[X.Length - 1]; } }
    }

    // Where a text's characters are, in the object's own frame: mm, the box centred on 0,0, not turned (as
    // EditorRender.Draw draws it). The canvas turns and scales it to the screen.
    sealed class CaretMap
    {
        public readonly List<CaretLine> Lines = new List<CaretLine>();
        public RectangleF Box;
        public bool Frame;
        public int Length;

        // The line a caret position is on: the last one starting at or before it (a position between two wrapped lines
        // is the next line's start).
        public int LineOf(int p)
        {
            for (int i = Lines.Count - 1; i > 0; i--)
                if (Lines[i].Start <= p) return i;
            return 0;
        }

        // The caret's x for a position: the boundary at or before it on its line (spaces where a line wraps sit at its end).
        public float XOf(int p)
        {
            var l = Lines[LineOf(p)];
            float x = l.X[0];
            for (int k = 0; k < l.Index.Length && l.Index[k] <= p; k++) x = l.X[k];
            return x;
        }

        // The position on a line nearest to x.
        public int AtX(int line, float x)
        {
            var l = Lines[Math.Max(0, Math.Min(Lines.Count - 1, line))];
            int best = 0;
            for (int k = 1; k < l.X.Length; k++)
                if (Math.Abs(l.X[k] - x) < Math.Abs(l.X[best] - x)) best = k;
            // a line broken inside a word ends where the next one starts: that position is drawn on the next line
            if (best > 0 && best == l.Index.Length - 1 && SharesEnd(Lines.IndexOf(l))) best--;
            return l.Index[best];
        }

        // A line that ends where the next one starts (a word wider than the frame, broken inside it).
        public bool SharesEnd(int line) { return line >= 0 && line < Lines.Count - 1 && Lines[line].End == Lines[line + 1].Start; }

        // The line at a height (above the first: the first; below the last: the last).
        public int LineAt(float y)
        {
            for (int i = 0; i < Lines.Count - 1; i++)
                if (y < Lines[i + 1].Top) return i;
            return Lines.Count - 1;
        }

        // The position nearest to a point in the object's own frame.
        public int Hit(PointF p) { return AtX(LineAt(p.Y), p.X); }

        // The lowest line's bottom (mm, own frame).
        public float TextBottom { get { var l = Lines[Lines.Count - 1]; return l.Top + l.Height; } }
    }

    // How the caret moves (TextEditing.Move).
    enum CaretMove { Left, Right, WordLeft, WordRight, Home, End, Up, Down, TextStart, TextEnd }

    // A text being edited on the canvas: its runs (filled, "\n" breaks), the caret and the selection's other end (anchor),
    // the format typed next at the caret, and the session's own undo steps (typing up to a word break, each format change,
    // each paste or deletion of a selection). Every change goes straight into the text object (SetRuns), so the canvas
    // renders it as printed. Pure: no window, no clipboard (the caller reads and writes it with CopyText/CopyRtf/Paste).
    sealed class TextEditing
    {
        public readonly TextObject Target;
        List<TextRun> full;
        int caret, anchor;
        TextRun typing;          // B/I/U, font or size chosen at the caret with nothing selected (filled), else null
        float? column;           // Up/Down keep this x (mm) while they repeat
        string lastStep;         // the kind of the last undo step ("type", "delete", ...): typing goes into one step

        sealed class Step
        {
            public List<TextRun> Runs; public int Caret, Anchor;
            public TextRun Own;   // the object's own format (an empty artistic text's format change lives only there)
        }
        bool paragraphAtLoad;    // the kind the undo steps were taken in
        readonly List<Step> undo = new List<Step>(), redo = new List<Step>();

        // Where the characters are (the canvas: EditorRender.MapText of the target, laid out as now).
        public Func<CaretMap> Map;
        // Called once, before the session's first change (the window takes its undo step: the text as it was).
        public event Action FirstChange;
        public bool Changed { get; private set; }

        public TextEditing(TextObject t)
        {
            Target = t;
            full = RunEdit.Normalized(t.FullRuns());
            caret = anchor = Length;
            paragraphAtLoad = Paragraph;
            Map = () => EditorRender.MapText(Target);
        }

        // The object changed outside the editing (a panel row: kind, alignment): its runs again, the caret kept.
        public void Reload()
        {
            full = RunEdit.Normalized(Target.FullRuns());
            caret = Math.Min(caret, Length); anchor = Math.Min(anchor, Length);
            caret = Snap(caret); anchor = Snap(anchor);
            typing = null; column = null; lastStep = null;
            // the kind changed (paragraph <-> artistic): the steps before it would bring back line breaks or formats the
            // new kind can't have; the window's undo step for the panel change undoes it after editing
            if (Paragraph != paragraphAtLoad) { undo.Clear(); redo.Clear(); paragraphAtLoad = Paragraph; }
        }

        public string Text { get { return string.Concat(full.Select(r => r.Text)); } }
        public int Length { get { return full.Sum(r => r.Text.Length); } }
        public bool Paragraph { get { return Target.Kind == TextKind.Frame; } }
        public int Caret { get { return caret; } }
        public int Anchor { get { return anchor; } }
        public int SelectionStart { get { return Math.Min(caret, anchor); } }
        public int SelectionLength { get { return Math.Abs(caret - anchor); } }
        public bool HasSelection { get { return caret != anchor; } }
        public string SelectedText { get { return Text.Substring(SelectionStart, SelectionLength); } }
        public List<TextRun> Runs { get { return full.ToList(); } }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        // ---------- caret and selection ----------

        // The character boundaries (a letter with its accents, a surrogate pair: one character).
        int[] Stops()
        {
            string text = Text;
            var starts = StringInfo.ParseCombiningCharacters(text);
            var list = new int[starts.Length + 1];
            starts.CopyTo(list, 0);
            list[starts.Length] = text.Length;
            return list;
        }

        int Snap(int p)
        {
            p = Math.Max(0, Math.Min(Length, p));
            int best = 0;
            foreach (int s in Stops()) if (s <= p) best = s;
            return best;
        }

        public void Select(int anchorAt, int caretAt)
        {
            anchor = Snap(anchorAt); caret = Snap(caretAt);
            typing = null; column = null; lastStep = null;
        }

        public void SetCaret(int p, bool extend)
        {
            int keep = anchor;
            Select(extend ? keep : p, p);
        }

        public void SelectAll() { Select(0, Length); }

        static int Kind(char c) { return char.IsWhiteSpace(c) ? 0 : char.IsLetterOrDigit(c) || c == '_' ? 1 : 2; }

        // The kind of the character at i: a combining accent counts as the letter it sits on (decomposed text, as from a Mac)
        static int Kind(string text, int i)
        {
            while (i > 0 && IsMark(text[i])) i--;
            return Kind(text[i]);
        }

        static bool IsMark(char c)
        {
            var u = char.GetUnicodeCategory(c);
            return u == UnicodeCategory.NonSpacingMark || u == UnicodeCategory.SpacingCombiningMark || u == UnicodeCategory.EnclosingMark;
        }

        // The word at a position (a double-click): letters and digits, a run of other characters, or the spaces.
        public void SelectWord(int p)
        {
            string text = Text;
            p = Snap(p);
            if (text.Length == 0) { Select(0, 0); return; }
            int at = p < text.Length ? p : p - 1;
            if (text[at] == '\n' && at > 0 && p == at) at--;
            int k = Kind(text, at);
            int a = at, b = at + 1;
            while (a > 0 && Kind(text, a - 1) == k && text[a - 1] != '\n') a--;
            while (b < text.Length && Kind(text, b) == k && text[b] != '\n') b++;
            if (text[at] == '\n') { a = at; b = at; }
            Select(a, Up(b));
        }

        // The paragraph at a position (a triple-click), without its line break.
        public void SelectParagraph(int p)
        {
            string text = Text;
            p = Snap(p);
            int a = p > 0 ? text.LastIndexOf('\n', p - 1) + 1 : 0;
            int b = text.IndexOf('\n', p);
            Select(a, b < 0 ? text.Length : b);
        }

        int WordRight(int p)
        {
            string text = Text;
            if (p >= text.Length) return text.Length;
            if (text[p] == '\n') return p + 1;
            int k = Kind(text, p);
            if (k != 0) while (p < text.Length && Kind(text, p) == k && text[p] != '\n') p++;
            while (p < text.Length && Kind(text, p) == 0 && text[p] != '\n') p++;
            return Up(p);
        }

        int WordLeft(int p)
        {
            string text = Text;
            if (p <= 0) return 0;
            if (text[p - 1] == '\n') return p - 1;
            while (p > 0 && Kind(text[p - 1]) == 0 && text[p - 1] != '\n') p--;
            if (p == 0 || text[p - 1] == '\n') return p;
            int k = Kind(text, p - 1);
            while (p > 0 && Kind(text, p - 1) == k && text[p - 1] != '\n') p--;
            return Snap(p);
        }

        // The character boundary at or after p (a word's end never splits a letter from its accent)
        int Up(int p) { foreach (int s in Stops()) if (s >= p) return s; return Length; }

        int Next(int p) { foreach (int s in Stops()) if (s > p) return s; return Length; }
        int Prev(int p) { int best = 0; foreach (int s in Stops()) if (s < p) best = s; return best; }

        // Keys follow the text, not the screen: Right is the next character also when the text is turned.
        public void Move(CaretMove how, bool extend)
        {
            int p = caret;
            float? keepColumn = null;
            switch (how)
            {
                case CaretMove.Left:
                    if (HasSelection && !extend) { p = SelectionStart; break; }
                    p = Prev(p); break;
                case CaretMove.Right:
                    if (HasSelection && !extend) { p = SelectionStart + SelectionLength; break; }
                    p = Next(p); break;
                case CaretMove.WordLeft: p = WordLeft(p); break;
                case CaretMove.WordRight: p = WordRight(p); break;
                case CaretMove.TextStart: p = 0; break;
                case CaretMove.TextEnd: p = Length; break;
                case CaretMove.Home:
                case CaretMove.End:
                {
                    var map = Map();
                    int at = map.LineOf(p);
                    var line = map.Lines[at];
                    p = how == CaretMove.Home ? line.Start : line.End;
                    // a line broken inside a word: its end is drawn at the next line's start, so End stops before its last character
                    if (how == CaretMove.End && map.SharesEnd(at) && p > line.Start) p = Prev(p);
                    break;
                }
                case CaretMove.Up:
                case CaretMove.Down:
                {
                    var map = Map();
                    int line = map.LineOf(p);
                    float x = column ?? map.XOf(p);
                    keepColumn = x;
                    if (how == CaretMove.Up) p = line == 0 ? 0 : map.AtX(line - 1, x);
                    else p = line == map.Lines.Count - 1 ? Length : map.AtX(line + 1, x);
                    break;
                }
            }
            Select(extend ? anchor : p, p);
            column = keepColumn;
        }

        // ---------- changes ----------

        // Before a change: the undo step (a new one unless typing goes on in the same word), the window's step once.
        void Before(string kind)
        {
            if (!Changed)
            {
                Changed = true;
                if (FirstChange != null) FirstChange();
            }
            if (kind != lastStep || kind != "type" && kind != "delete")
            {
                undo.Add(Snapshot());
                if (undo.Count > 500) undo.RemoveAt(0);
            }
            redo.Clear();
            lastStep = kind;
        }

        void Apply()
        {
            // neighbours with the same format as one run, no empty runs (as SetRuns stores them)
            var merged = new List<TextRun>();
            foreach (var r in full)
            {
                if (r.Text.Length == 0) continue;
                if (merged.Count > 0 && merged[merged.Count - 1].SameFormat(r)) merged[merged.Count - 1] = r.WithText(merged[merged.Count - 1].Text + r.Text);
                else merged.Add(r);
            }
            full = merged;
            Target.SetRuns(full);
            column = null;
        }

        // The format of what is typed at the caret: the one chosen there, else the character's before it.
        TextRun TypingFormat()
        {
            if (typing != null) return typing;
            return Filled(RunEdit.Common(full, SelectionStart, 0));
        }

        TextRun Filled(TextRun r) { return Target.Fill(r == null ? null : r.WithText("")); }

        // Characters as Windows sends them (WM_CHAR: dead keys, AltGr, Alt+number pad, IME results): replace the
        // selection. Control characters are left out; an artistic text gets spaces for line breaks.
        public void Type(string s)
        {
            s = Clean(s);
            if (s.Length == 0) return;
            // a new step at a word's start (after a space or a break), else typing goes on in the same step
            bool wordStart = caret > 0 && char.IsWhiteSpace(Text[caret - 1]) && !char.IsWhiteSpace(s[0]);
            Insert(HasSelection || wordStart ? "type-new" : "type", s, TypingFormat());
        }

        string Clean(string s)
        {
            s = (s ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Replace('\t', ' ');
            var b = new StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '\n') b.Append(Paragraph ? '\n' : ' ');
                else if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b.Append(c).Append(s[i + 1]); i++; }   // else half a pair: left out
                }
                else if (char.IsLowSurrogate(c)) { }
                else if (c >= ' ' && c != '\x7f') b.Append(c);
            }
            return b.ToString();
        }

        void Insert(string kind, string s, TextRun format)
        {
            Before(kind == "type-new" ? "type!" : kind);
            if (kind == "type-new") lastStep = "type";
            int start = SelectionStart;
            full = RunEdit.Splice(full, start, SelectionLength, s, format);
            caret = anchor = start + s.Length;
            typing = null;
            Apply();
        }

        // Enter in a paragraph text: a new paragraph (an artistic text: the caller ends editing).
        public void NewParagraph()
        {
            if (!Paragraph) return;
            Insert("break", "\n", TypingFormat());
        }

        // A field's code ({date}) at the caret, as its own undo step.
        public void InsertField(string code) { Insert("field", Clean(code), TypingFormat()); }

        public void Backspace(bool word)
        {
            if (HasSelection) { DeleteSelection("delete-selection"); return; }
            if (caret == 0) return;
            int from = word ? WordLeft(caret) : Prev(caret);
            Remove("delete", from, caret);
        }

        public void Delete(bool word)
        {
            if (HasSelection) { DeleteSelection("delete-selection"); return; }
            if (caret >= Length) return;
            int to = word ? WordRight(caret) : Next(caret);
            Remove("delete", caret, to);
        }

        void DeleteSelection(string kind) { Remove(kind, SelectionStart, SelectionStart + SelectionLength); }

        void Remove(string kind, int from, int to)
        {
            if (to <= from) return;
            var keep = typing;
            Before(kind);
            full = RunEdit.Splice(full, from, to - from, "", null);
            caret = anchor = from;
            typing = kind == "delete" ? keep : null;
            Apply();
        }

        // ---------- formats ----------

        // The format the panel shows: the selection's (mixed fields null), with nothing selected what is typed at the
        // caret (artistic and paragraph text alike: both have words in their own format).
        public TextRun Current
        {
            get
            {
                if (HasSelection) return RunEdit.Common(full, SelectionStart, SelectionLength) ?? Filled(null);
                return TypingFormat();
            }
        }

        // Font, size, bold, italic or underline: on the selection; with nothing selected on what is typed next at the
        // caret (artistic and paragraph text alike); an empty text: its own format (the caret shows it at once).
        public void Restyle(Func<TextRun, TextRun> change)
        {
            if (Length == 0)
            {
                var f = change(Filled(null));
                Before("format");
                Target.SetOwn(f);
                full = RunEdit.Normalized(Target.FullRuns());
                return;
            }
            if (HasSelection)
            {
                Before("format");
                full = RunEdit.Restyle(full, SelectionStart, SelectionLength, change);
                Apply();
                lastStep = "format";
                return;
            }
            typing = change(TypingFormat());
        }

        // Bold, italic, underline: off when all of it has it, else on.
        public void Toggle(FontStyle style)
        {
            var now = Current;
            bool? has = style == FontStyle.Bold ? now.Bold : style == FontStyle.Italic ? now.Italic : now.Underline;
            bool on = has != true;
            Restyle(r => style == FontStyle.Bold ? r.With(bold: on) : style == FontStyle.Italic ? r.With(italic: on) : r.With(underline: on));
        }

        // ---------- the session's undo ----------

        public bool Undo() { return Swap(undo, redo); }
        public bool Redo() { return Swap(redo, undo); }

        Step Snapshot() { return new Step { Runs = full.ToList(), Caret = caret, Anchor = anchor, Own = Filled(null) }; }

        bool Swap(List<Step> from, List<Step> to)
        {
            if (from.Count == 0) return false;
            var s = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(Snapshot());
            Target.SetOwn(s.Own);
            full = s.Runs;
            Apply();
            caret = s.Caret; anchor = s.Anchor;
            typing = null; lastStep = null;
            return true;
        }

        // ---------- clipboard ----------

        // The selection as plain text (Windows line breaks) and as RTF with its fonts, sizes, bold, italic, underline,
        // letter spacing and invert.
        public string CopyText() { return SelectedText.Replace("\n", "\r\n"); }
        public string CopyRtf() { return Rtf(RunEdit.Slice(full, SelectionStart, SelectionStart + SelectionLength)); }

        // Cut: the selection goes (the caller put it on the clipboard first).
        public void CutSelection() { if (HasSelection) DeleteSelection("cut"); }

        // Paste limits (security review 2026-11-09, M3): clipboard text or RTF over 4 M characters isn't read (as the
        // object paste, EditorClipboard), and more than 100,000 characters aren't pasted into a text: a whole document
        // would be laid out again on every key. An RTF too large on its own (a picture in it) falls back to the plain
        // text. true: refuse the paste ("The text on the clipboard is too long to paste ...").
        public const int MaxClipboardChars = 4 * 1024 * 1024, MaxPasteChars = 100000;

        public static bool PasteTooLong(ref string text, ref string rtf)
        {
            if (text != null && text.Length > MaxClipboardChars) return true;
            if (rtf != null && rtf.Length > MaxClipboardChars)
            {
                if (string.IsNullOrEmpty(text)) return true;
                rtf = null;
            }
            int n = !string.IsNullOrEmpty(text) ? text.Length : rtf != null ? RtfRuns.PlainText(rtf).Length : 0;
            return n > MaxPasteChars;
        }

        // A paste: RTF keeps its formats (an artistic text: its line breaks become spaces, Clean); plain text takes the
        // format at the caret. plainOnly: Paste as plain text.
        public void Paste(string text, string rtf, bool plainOnly = false)
        {
            if (!plainOnly && rtf != null)
            {
                List<string> missing;
                RtfDoc doc;
                try { doc = RtfRuns.Read(rtf, out missing); }
                catch (Exception) { doc = null; }
                if (doc != null && doc.Text.Length > 0)
                {
                    var runs = RunEdit.Normalized(doc.Runs).Select(r => r.WithText(Clean(r.Text))).Where(r => r.Text.Length > 0).ToList();
                    Before("paste");
                    int start = SelectionStart;
                    var r0 = RunEdit.Splice(full, start, SelectionLength, "", null);
                    var head = RunEdit.Slice(r0, 0, start);
                    head.AddRange(runs);
                    head.AddRange(RunEdit.Slice(r0, start, int.MaxValue));
                    full = head;
                    caret = anchor = start + runs.Sum(r => r.Text.Length);
                    typing = null;
                    Apply();
                    return;
                }
            }
            if (rtf != null && text == null) text = RtfRuns.PlainText(rtf);
            text = Clean(text);
            if (text.Length == 0) return;
            Insert("paste", text, TypingFormat());
        }

        // RTF for runs (Word, WordPad and Outlook paste it formatted; RtfRuns reads it back). Letter spacing: \expnd
        // (quarter points) and \expndtw (twips), as Word writes it; invert: white letters (\cf2) on black shading
        // (\chcbpat1, Word) and highlight (\highlight1, WordPad).
        internal static string Rtf(List<TextRun> runs)
        {
            var fonts = runs.Select(r => r.Font ?? RtfRuns.DefaultFont).Distinct().ToList();
            if (fonts.Count == 0) fonts.Add(RtfRuns.DefaultFont);
            var b = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0\uc1{\fonttbl");
            for (int i = 0; i < fonts.Count; i++) b.Append(@"{\f").Append(i).Append(@"\fnil ").Append(Escape(fonts[i])).Append(";}");
            b.Append("}");
            if (runs.Any(r => r.Invert == true)) b.Append(@"{\colortbl;\red0\green0\blue0;\red255\green255\blue255;}");
            b.Append("\r\n");
            foreach (var r in runs)
            {
                b.Append(@"{\f").Append(fonts.IndexOf(r.Font ?? RtfRuns.DefaultFont));
                b.Append(@"\fs").Append(((int)Math.Round((r.Size ?? 12) * 2)).ToString(CultureInfo.InvariantCulture));
                if (r.Bold == true) b.Append(@"\b");
                if (r.Italic == true) b.Append(@"\i");
                if (r.Underline == true) b.Append(@"\ul");
                if (r.Spacing != null && r.Spacing != 0)
                    b.Append(@"\expnd").Append(((int)Math.Round(r.Spacing.Value * 4)).ToString(CultureInfo.InvariantCulture))
                     .Append(@"\expndtw").Append(((int)Math.Round(r.Spacing.Value * 20)).ToString(CultureInfo.InvariantCulture));
                if (r.Invert == true) b.Append(@"\cf2\chcbpat1\highlight1");
                b.Append(' ');
                var parts = r.Text.Split('\n');
                for (int k = 0; k < parts.Length; k++)
                {
                    if (k > 0) b.Append(@"\par ");
                    b.Append(Escape(parts[k]));
                }
                b.Append('}');
            }
            return b.Append('}').ToString();
        }

        static string Escape(string s)
        {
            var b = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '\\' || c == '{' || c == '}') b.Append('\\').Append(c);
                else if (c < 128) b.Append(c);
                else b.Append(@"\u").Append(((int)(short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
            }
            return b.ToString();
        }
    }
}
