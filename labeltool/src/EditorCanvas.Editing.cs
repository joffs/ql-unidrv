// Label editor: the canvas, part: editing a text on the label. The text keeps being drawn by
// the renderer (as printed); on top of it, in screen pixels from the same layout (EditorRender.MapText): the caret, the
// selection, the fields as codes in a light chip, and (View > Show overflow while editing) the cut-off lines in light
// grey below a paragraph text's frame, else the text scrolled inside it to the caret's line. Keys come as Windows sends
// characters (WM_CHAR: dead keys, AltGr, Alt+number pad, emoji panel); the IME's window sits at the caret. The system
// caret follows ours (hidden) for Magnifier and screen readers.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorCanvas
    {
        TextEditing editing;
        CaretMap editMap;            // the map for the text as it is now (null: lay it out again)
        readonly Timer blink = new Timer();
        bool caretOn = true;
        char highSurrogate;          // the first half of a character sent in two WM_CHARs
        DateTime lastDouble;         // a third click soon after a double-click selects the paragraph
        Point lastDoubleAt;
        bool systemCaret;

        // The text being edited, or null.
        public TextEditing Editing { get { return editing; } }
        public TextObject EditedText { get { return editing != null ? editing.Target : null; } }

        // View > Show overflow while editing (on: the cut-off lines in grey below the frame; off: scroll inside it).
        public bool ShowOverflow = true;

        // Editing started or ended (the window renders the fields as codes or values, the panel and menus follow).
        public event EventHandler EditingChanged;
        // The text changed while editing: before = its bounds before (the window renders that part and the new one).
        public event Action<RectangleF> TextEdited;
        // The caret or the selection moved (the panel shows the format there).
        public event EventHandler EditCaretMoved;
        // The session's first change: the window takes its undo step (the text as it was).
        public event EventHandler EditFirstChange;
        // A message for the status bar (a paste that isn't text).
        public event Action<string> EditMessage;

        // Starts editing a text: the caret at a point on the screen (a double-click), else at the end; selectAll: its
        // whole text selected (a new text's placeholder: typing replaces it).
        public void BeginEdit(TextObject t, Point? at = null, bool selectAll = false)
        {
            if (t == null) return;
            if (editing != null && editing.Target == t) return;
            EndEdit();
            Selected = t;
            editing = new TextEditing(t);
            editing.FirstChange += () => { if (EditFirstChange != null) EditFirstChange(this, EventArgs.Empty); };
            editing.Map = () => Map;
            EditorRender.Codes = t;
            EditorRender.CodesScroll = 0;
            var before = t.Bounds;
            EditorRender.FitText(t);   // the fields as codes may be wider or narrower
            editMap = null;
            if (selectAll) editing.SelectAll();
            else if (at.HasValue) editing.SetCaret(Map.Hit(ToLocal(ToMm(at.Value))), false);
            ResetBlink();
            int ms = SystemInformation.CaretBlinkTime;
            blink.Interval = ms > 0 ? ms : 500;
            blink.Enabled = ms > 0;
            if (Focused) MakeSystemCaret();
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            if (EditingChanged != null) EditingChanged(this, EventArgs.Empty);
            if (TextEdited != null && before != t.Bounds) TextEdited(before);
            Scroll();
            Invalidate();
        }

        // Ends editing (Esc, a click beside the text, another tool, printing, another label): the changes stay; the
        // fields show their values again; an artistic text's box fits them.
        public void EndEdit()
        {
            if (editing == null) return;
            var t = editing.Target;
            editing = null;
            editMap = null;
            EditorRender.Codes = null;
            EditorRender.CodesScroll = 0;
            EditorRender.FitText(t);
            blink.Enabled = false;
            DropSystemCaret();
            Cursor = Cursors.Default;
            AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
            if (EditingChanged != null) EditingChanged(this, EventArgs.Empty);
            Invalidate();
        }

        CaretMap Map
        {
            get
            {
                if (editMap == null && editing != null) editMap = EditorRender.MapText(editing.Target);
                return editMap;
            }
        }

        // ---------- changes from keys, the panel and the menus ----------

        // Runs a change of the edited text: lays it out again, an artistic text's box follows, the window renders it.
        public void EditChange(Action<TextEditing> change)
        {
            if (editing == null) return;
            var t = editing.Target;
            var before = t.Bounds;
            string text = t.Text;
            var runs = t.Runs;
            change(editing);
            editMap = null;
            bool changed = t.Text != text || !ReferenceEquals(t.Runs, runs);
            if (changed)
            {
                EditorRender.FitText(t);
                editMap = null;
                if (TextEdited != null) TextEdited(before);
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            }
            float scrolled = EditorRender.CodesScroll;
            Scroll();
            ResetBlink();
            if (EditCaretMoved != null) EditCaretMoved(this, EventArgs.Empty);
            // per key only the text's area (with its frame, overflow lines and the area before); scrolled: all of it
            if (scrolled != EditorRender.CodesScroll || editing == null) Invalidate();
            else InvalidateEdit(before);
        }

        // The screen area the edited text and its editing marks cover: the frame, the lines (also those below the
        // frame), the field chips, the caret; whole pixels with a margin for the frame's pen.
        Rectangle EditArea()
        {
            var map = Map;
            if (editing == null || map == null || map.Lines.Count == 0) return Rectangle.Empty;
            var b = map.Box;
            float left = Math.Min(b.Left, map.Lines.Min(l => l.Left)), right = Math.Max(b.Right, map.Lines.Max(l => l.Right));
            float top = Math.Min(b.Top, map.Lines[0].Top), bottom = Math.Max(b.Bottom, map.TextBottom);
            var pts = LocalRect(left, top - Math.Abs(Scrolled), right, bottom + Math.Abs(Scrolled));
            var r = Rectangle.FromLTRB((int)Math.Floor(pts.Min(p => p.X)), (int)Math.Floor(pts.Min(p => p.Y)),
                                       (int)Math.Ceiling(pts.Max(p => p.X)), (int)Math.Ceiling(pts.Max(p => p.Y)));
            r.Inflate(LogicalToDeviceUnits(8), LogicalToDeviceUnits(8));
            return r;
        }

        Rectangle lastEditArea;

        // A key changed the text: its area now, its area before the key (beforeMm: its box then) and the last area drawn.
        void InvalidateEdit(RectangleF beforeMm)
        {
            var now = EditArea();
            var s = ToScreen(RectangleF.Inflate(beforeMm, 2, 2));
            var before = Rectangle.FromLTRB((int)Math.Floor(s.Left), (int)Math.Floor(s.Top), (int)Math.Ceiling(s.Right), (int)Math.Ceiling(s.Bottom));
            var r = Rectangle.Union(now, before);
            if (!lastEditArea.IsEmpty) r = Rectangle.Union(r, lastEditArea);
            lastEditArea = now;
            Invalidate(r);
        }

        // Keys while editing (the window's ProcessCmdKey asks first). True: handled. Ctrl+Alt (AltGr) and plain characters
        // are left to Windows, which sends them as WM_CHAR.
        public bool EditKey(Keys keyData)
        {
            if (editing == null) return false;
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            bool shift = (mods & Keys.Shift) != 0, ctrl = (mods & Keys.Control) != 0, alt = (mods & Keys.Alt) != 0;
            if (ctrl && alt) return false;   // AltGr: a character
            if (alt) return false;            // the menus (Alt+letter), Alt+number pad
            CaretMove? move = null;
            switch (key)
            {
                case Keys.Left: move = ctrl ? CaretMove.WordLeft : CaretMove.Left; break;
                case Keys.Right: move = ctrl ? CaretMove.WordRight : CaretMove.Right; break;
                case Keys.Up: if (!ctrl) move = CaretMove.Up; break;
                case Keys.Down: if (!ctrl) move = CaretMove.Down; break;
                case Keys.Home: move = ctrl ? CaretMove.TextStart : CaretMove.Home; break;
                case Keys.End: move = ctrl ? CaretMove.TextEnd : CaretMove.End; break;
            }
            if (move.HasValue) { var m = move.Value; EditChange(e => e.Move(m, shift)); return true; }
            switch (keyData)
            {
                case Keys.Escape: EndEdit(); return true;
                case Keys.Back: case Keys.Back | Keys.Shift: EditChange(e => e.Backspace(false)); return true;
                case Keys.Back | Keys.Control: EditChange(e => e.Backspace(true)); return true;
                case Keys.Delete: EditChange(e => e.Delete(false)); return true;
                case Keys.Delete | Keys.Control: EditChange(e => e.Delete(true)); return true;
                case Keys.Enter: case Keys.Enter | Keys.Shift:
                    if (editing.Paragraph && keyData == Keys.Enter) EditChange(e => e.NewParagraph());
                    else if (!editing.Paragraph && keyData == Keys.Enter) EndEdit();
                    return true;
                case Keys.Tab: case Keys.Tab | Keys.Shift: case Keys.PageUp: case Keys.PageDown: return true;
                case Keys.Control | Keys.B: case Keys.Control | Keys.Shift | Keys.F: EditChange(e => e.Toggle(FontStyle.Bold)); return true;
                case Keys.Control | Keys.I: case Keys.Control | Keys.Shift | Keys.K: EditChange(e => e.Toggle(FontStyle.Italic)); return true;
                case Keys.Control | Keys.U: case Keys.Control | Keys.Shift | Keys.U: EditChange(e => e.Toggle(FontStyle.Underline)); return true;
            }
            return false;
        }

        // A panel row changed the edited text (kind, alignment, spacing): the model takes its runs again.
        public void EditSync()
        {
            if (editing == null) return;
            editing.Reload();
            editMap = null;
            Scroll();
            Invalidate();
        }

        // Edit > Undo / Redo while editing: the session's own steps.
        public bool EditUndo(bool redo)
        {
            if (editing == null) return false;
            bool done = false;
            EditChange(e => done = redo ? e.Redo() : e.Undo());
            return done;
        }

        // Edit > Copy / Cut while editing: the selected characters as text and RTF.
        public void EditCopy(bool cut)
        {
            if (editing == null || !editing.HasSelection) return;
            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, editing.CopyText());
            data.SetData(DataFormats.Rtf, editing.CopyRtf());
            try { Clipboard.SetDataObject(data, true, 5, 100); }
            catch (ExternalException) { Message(Lang.T("Another program is using the clipboard. Try again.")); return; }
            if (cut) EditChange(e => e.CutSelection());
        }

        // Edit > Paste (as plain text) while editing: text or RTF at the caret; objects and pictures aren't pasted into it.
        public void EditPaste(bool plain)
        {
            if (editing == null) return;
            string text = null, rtf = null;
            try
            {
                var data = Clipboard.GetDataObject();
                if (data != null)
                {
                    if (data.GetDataPresent(DataFormats.UnicodeText)) text = data.GetData(DataFormats.UnicodeText) as string;
                    if (!plain && data.GetDataPresent(DataFormats.Rtf)) rtf = data.GetData(DataFormats.Rtf) as string;
                }
            }
            catch (ExternalException) { Message(Lang.T("Another program is using the clipboard. Try again.")); return; }
            if (string.IsNullOrEmpty(text) && string.IsNullOrEmpty(rtf))
            {
                Message(Lang.T("Only text can be pasted into a text. End editing (Esc) to paste objects or pictures."));
                return;
            }
            if (TextEditing.PasteTooLong(ref text, ref rtf))
            {
                Message(Lang.F("The text on the clipboard is too long to paste (at most {0} characters).", TextEditing.MaxPasteChars.ToString("N0")));
                return;
            }
            EditChange(e => e.Paste(text, rtf, plain));
        }

        void Message(string text) { if (EditMessage != null) EditMessage(text); }

        // ---------- characters (WM_CHAR) and the IME ----------

        const int WM_CHAR = 0x0102, WM_IME_STARTCOMPOSITION = 0x010D, WM_IME_COMPOSITION = 0x010F;

        // Typing on a selected text that isn't being edited starts editing with the caret at the end (the text stays).
        public Func<TextObject> TypingTarget { get; set; }
        // True: typing on that text replaces its whole text (a new text's placeholder), else it goes to the end.
        public Func<TextObject, bool> TypingReplaces { get; set; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CHAR)
            {
                char c = (char)m.WParam.ToInt64();
                if (char.IsHighSurrogate(c)) { highSurrogate = c; return; }
                string s = char.IsLowSurrogate(c) && highSurrogate != 0 ? new string(new[] { highSurrogate, c }) : c.ToString();
                highSurrogate = '\0';
                if (s.Length == 1 && (s[0] < ' ' || s[0] == '\x7f')) { base.WndProc(ref m); return; }   // keys handled as keys
                if (editing == null)
                {
                    var t = TypingTarget != null ? TypingTarget() : null;
                    if (t == null) { base.WndProc(ref m); return; }
                    BeginEdit(t, null, TypingReplaces != null && TypingReplaces(t));
                }
                EditChange(e => e.Type(s));
                return;
            }
            if ((m.Msg == WM_IME_STARTCOMPOSITION || m.Msg == WM_IME_COMPOSITION) && editing != null) PlaceIme();
            base.WndProc(ref m);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct COMPOSITIONFORM { public int dwStyle; public Point ptCurrentPos; public Rectangle rcArea; }
        [DllImport("imm32.dll")] static extern IntPtr ImmGetContext(IntPtr hWnd);
        [DllImport("imm32.dll")] static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);
        [DllImport("imm32.dll")] static extern bool ImmSetCompositionWindow(IntPtr hIMC, ref COMPOSITIONFORM form);

        // The IME's own composition window at the caret (its candidate list below the line).
        void PlaceIme()
        {
            var r = CaretBox();
            if (r.IsEmpty) return;
            IntPtr imc = ImmGetContext(Handle);
            if (imc == IntPtr.Zero) return;
            try
            {
                var f = new COMPOSITIONFORM { dwStyle = 0x0002 /* CFS_POINT */, ptCurrentPos = new Point(r.Left, r.Top) };
                ImmSetCompositionWindow(imc, ref f);
            }
            finally { ImmReleaseContext(Handle, imc); }
        }

        // The keys come here: focused, or the window's active control (a window without the foreground, as in tests).
        public bool HasKeys
        {
            get
            {
                if (Focused) return true;
                var form = FindForm();
                return form != null && form.ActiveControl == this;
            }
        }

        // ---------- the system caret (Magnifier, screen readers) ----------

        [DllImport("user32.dll")] static extern bool CreateCaret(IntPtr hWnd, IntPtr hBitmap, int width, int height);
        [DllImport("user32.dll")] static extern bool DestroyCaret();
        [DllImport("user32.dll")] static extern bool SetCaretPos(int x, int y);

        void MakeSystemCaret()
        {
            if (editing == null || !IsHandleCreated) return;
            var r = CaretBox();
            DropSystemCaret();
            systemCaret = CreateCaret(Handle, IntPtr.Zero, Math.Max(1, r.Width), Math.Max(1, r.Height));   // hidden: ours is drawn
            MoveSystemCaret();
        }

        void MoveSystemCaret()
        {
            if (!systemCaret) return;
            var r = CaretBox();
            SetCaretPos(r.Left, r.Top);
        }

        void DropSystemCaret()
        {
            if (!systemCaret) return;
            DestroyCaret();
            systemCaret = false;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (editing != null) { MakeSystemCaret(); ResetBlink(); } }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); DropSystemCaret(); Invalidate(); }

        void ResetBlink()
        {
            caretOn = true;
            if (blink.Enabled) { blink.Stop(); blink.Start(); }
            MoveSystemCaret();
        }

        void OnBlink(object sender, EventArgs e)
        {
            if (editing == null) { blink.Enabled = false; return; }
            caretOn = !caretOn;
            var r = CaretBox();
            r.Inflate(3, 3);
            Invalidate(r);
        }

        // ---------- geometry: the object's own frame and the screen ----------

        PointF CentreOfEdited { get { var t = editing.Target; return new PointF((float)(t.X + t.W / 2), (float)(t.Y + t.H / 2)); } }
        float Scrolled { get { return editing.Paragraph && !ShowOverflow ? EditorRender.CodesScroll : 0; } }

        // A point on the label (mm) in the edited text's own frame (box centred on 0,0, not turned, scrolling undone).
        PointF ToLocal(PointF mm)
        {
            var c = CentreOfEdited;
            var p = LabelObject.Turn(mm, c, -editing.Target.Angle);
            return new PointF(p.X - c.X, p.Y - c.Y + Scrolled);
        }

        // A point of the text's own frame on the screen.
        PointF FromLocal(float x, float y)
        {
            var c = CentreOfEdited;
            var p = LabelObject.Turn(new PointF(c.X + x, c.Y + y - Scrolled), c, editing.Target.Angle);
            return ToScreen(p.X, p.Y);
        }

        PointF[] LocalRect(float x0, float y0, float x1, float y1)
        {
            return new[] { FromLocal(x0, y0), FromLocal(x1, y0), FromLocal(x1, y1), FromLocal(x0, y1) };
        }

        // The caret's bounding box on the screen (whole pixels).
        Rectangle CaretBox()
        {
            if (editing == null || Map == null) return Rectangle.Empty;
            var l = Map.Lines[Map.LineOf(editing.Caret)];
            float x = Map.XOf(editing.Caret);
            var a = FromLocal(x, l.Top);
            var b = FromLocal(x, l.Top + l.Height);
            return Rectangle.FromLTRB((int)Math.Floor(Math.Min(a.X, b.X)), (int)Math.Floor(Math.Min(a.Y, b.Y)),
                                      (int)Math.Ceiling(Math.Max(a.X, b.X)) + 1, (int)Math.Ceiling(Math.Max(a.Y, b.Y)) + 1);
        }

        // View > Show overflow while editing off: the text scrolled inside its frame so the caret's line shows (and back
        // to the top when it fits).
        void Scroll()
        {
            if (editing == null) return;
            float now = EditorRender.CodesScroll, want = 0;
            if (editing.Paragraph && !ShowOverflow)
            {
                var map = Map;
                var l = map.Lines[map.LineOf(editing.Caret)];
                float top = map.Box.Top, bottom = map.Box.Bottom;
                want = now;
                if (l.Top + l.Height - want > bottom) want = l.Top + l.Height - bottom;
                if (l.Top - want < top) want = l.Top - top;
                want = Math.Max(0, Math.Min(want, Math.Max(0, map.TextBottom - bottom)));
                if (want < 0.5f) want = 0;   // the first line's padding: not scrolled
            }
            if (Math.Abs(want - now) < 0.001f) return;
            EditorRender.CodesScroll = want;
            if (TextEdited != null) TextEdited(editing.Target.Bounds);   // drawn again at the new place
        }

        // The View option changed: scrolled back, or scrolled to the caret.
        public void OverflowOptionChanged()
        {
            if (editing == null) { Invalidate(); return; }
            if (ShowOverflow && EditorRender.CodesScroll != 0)
            {
                EditorRender.CodesScroll = 0;
                if (TextEdited != null) TextEdited(editing.Target.Bounds);
            }
            Scroll();
            Invalidate();
        }

        bool OnEdited(PointF mm)
        {
            var t = editing.Target;
            var local = ToLocal(mm);
            float tol = LogicalToDeviceUnits(4) / PixelsPerMm;
            var box = RectangleF.Inflate(Map.Box, tol, tol);
            // the grey overflow lines below the frame belong to the text while they show
            if (editing.Paragraph && ShowOverflow) box = RectangleF.FromLTRB(box.Left, box.Top, box.Right, Math.Max(box.Bottom, Map.TextBottom + tol));
            return box.Contains(local.X, local.Y - Scrolled);
        }

        // ---------- mouse ----------

        // A press while editing: in the text the caret goes there (Shift: the selection grows; double: a word; triple: the
        // paragraph); beside it editing ends and the press does what it does. A double-click on a text starts editing.
        bool EditMouseDown(MouseEventArgs e, PointF mm)
        {
            if (e.Button != MouseButtons.Left) return false;
            if (editing != null)
            {
                if (!OnEdited(mm)) { EndEdit(); return false; }
                int p = Map.Hit(ToLocal(mm));
                bool triple = e.Clicks == 1 && (DateTime.Now - lastDouble).TotalMilliseconds <= SystemInformation.DoubleClickTime
                              && Math.Abs(e.X - lastDoubleAt.X) <= SystemInformation.DoubleClickSize.Width && Math.Abs(e.Y - lastDoubleAt.Y) <= SystemInformation.DoubleClickSize.Height;
                if (e.Clicks >= 2) { lastDouble = DateTime.Now; lastDoubleAt = e.Location; EditChange(x => x.SelectWord(p)); }
                else if (triple) { lastDouble = DateTime.MinValue; EditChange(x => x.SelectParagraph(p)); }
                else
                {
                    EditChange(x => x.SetCaret(p, (ModifierKeys & Keys.Shift) != 0));
                    drag = Drag.Text;
                    Capture = true;
                }
                return true;
            }
            if (e.Clicks >= 2 && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            {
                var hit = HitObject(mm) as TextObject;
                if (hit == null) return false;
                drag = Drag.None;
                Capture = false;
                lastDouble = DateTime.Now; lastDoubleAt = e.Location;
                BeginEdit(hit, e.Location);
                return true;
            }
            return false;
        }

        bool EditMouseMove(MouseEventArgs e)
        {
            if (editing == null) return false;
            if (drag == Drag.Text)
            {
                int p = Map.Hit(ToLocal(ToMm(e.Location)));
                if (p != editing.Caret) EditChange(x => x.SetCaret(p, true));
                return true;
            }
            if (drag == Drag.None)
            {
                Cursor = OnEdited(ToMm(e.Location)) ? Cursors.IBeam : Cursors.Default;
                return true;
            }
            return false;
        }

        // ---------- painting ----------

        // While editing: the editing frame (thin, solid, no handles), the grey overflow lines, the field chips, the
        // selection band and the caret, all from the same layout as the rendered text.
        void DrawEditing(Graphics g)
        {
            if (editing == null || Map == null) return;
            var t = editing.Target;
            var map = Map;
            bool inverted = editing.Current.Invert != false;   // white on black where the caret or the selection is
            var state = g.Save();
            try
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var frame = new Pen(Theme.Fixed.Petrol, 1f)) g.DrawPolygon(frame, Outline(t));
                if (editing.Paragraph && ShowOverflow && EditorRender.Overflows(t))
                {
                    // the cut-off lines, light grey, below (and beside) the frame: screen only
                    var inner = g.Save();
                    var c = CentreOfEdited;
                    var o = Origin;
                    float k = PixelsPerMm;
                    g.TranslateTransform(o.X, o.Y);
                    g.ScaleTransform(k, k);
                    g.TranslateTransform(c.X, c.Y);
                    g.RotateTransform((float)t.Angle);
                    g.TextRenderingHint = TextRenderingHint.AntiAlias;
                    using (var grey = new SolidBrush(Theme.Fixed.PaperHint)) EditorRender.DrawOverflow(g, t, grey);
                    g.Restore(inner);
                }
                // fields: a light chip under their codes
                using (var chip = new SolidBrush(Color.FromArgb(70, Theme.Fixed.PaperHint)))
                    foreach (var f in Fields.Ranges(t.Text)) FillRange(g, chip, f.Key, f.Key + f.Value, map);
                if (editing.HasSelection)
                    using (var band = new SolidBrush(Color.FromArgb(inverted ? 140 : 90, inverted ? Theme.Fixed.PetrolLight : Theme.Fixed.Petrol)))
                        FillRange(g, band, editing.SelectionStart, editing.SelectionStart + editing.SelectionLength, map);
                if (caretOn && HasKeys)
                {
                    var l = map.Lines[map.LineOf(editing.Caret)];
                    float x = map.XOf(editing.Caret);
                    var a = FromLocal(x, l.Top);
                    var b = FromLocal(x, l.Top + l.Height);
                    float width = PixelsPerMm / RealSize >= 2 ? 2 : 1;
                    g.SmoothingMode = t.Angle % 90 == 0 ? SmoothingMode.None : SmoothingMode.AntiAlias;
                    if (inverted) using (var edge = new Pen(Theme.Fixed.Paper, width + 2)) g.DrawLine(edge, a, b);
                    using (var pen = new Pen(inverted ? Theme.Fixed.PetrolLight : Theme.Fixed.Petrol, width)) g.DrawLine(pen, a, b);
                }
            }
            finally { g.Restore(state); }
        }

        // A band over the characters from..to on each line they cover (the selection, a field).
        void FillRange(Graphics g, Brush brush, int from, int to, CaretMap map)
        {
            if (to <= from) return;
            foreach (var l in map.Lines)
            {
                if (l.End < from || l.Start > to) continue;
                // a selection that goes on past this line's end: to its end (the break or the wrap) and a little beyond
                int a = Math.Max(from, l.Start), b = Math.Min(to, l.End);
                // the line's end at its own right edge (a line broken inside a word ends where the next one starts)
                float x0 = map.XOf(a), x1 = b == l.End ? l.Right : map.XOf(b);
                if (to > l.End && map.Lines.IndexOf(l) < map.Lines.Count - 1) x1 = Math.Max(x1, l.Right) + l.Height * 0.25f;
                if (x1 - x0 < 0.01f) continue;
                if (editing.Paragraph && !ShowOverflow && l.Top - Scrolled >= map.Box.Bottom) continue;
                g.FillPolygon(brush, LocalRect(x0, l.Top, x1, l.Top + l.Height));
            }
        }

        // ---------- accessibility ----------

        protected override AccessibleObject CreateAccessibilityInstance() { return new CanvasAccessible(this); }

        // The canvas for screen readers: while editing an editable text with its value; else the selected object's name.
        sealed class CanvasAccessible : ControlAccessibleObject
        {
            readonly EditorCanvas canvas;
            public CanvasAccessible(EditorCanvas c) : base(c) { canvas = c; }

            public override AccessibleRole Role { get { return canvas.editing != null ? AccessibleRole.Text : AccessibleRole.Client; } }

            public override string Value
            {
                get { return canvas.editing != null ? canvas.editing.Text : base.Value; }
                set { }
            }

            public override string Name
            {
                get
                {
                    var t = canvas.editing != null ? canvas.editing.Target : canvas.Selected as TextObject;
                    if (t == null) return base.Name;
                    string words = t.Text.Replace("\n", " ").Trim();
                    if (words.Length > 40) words = words.Substring(0, 39) + "…";
                    return Lang.F("Text: {0}", words);
                }
                set { }
            }

            public override AccessibleStates State
            {
                get { return base.State | (canvas.editing != null ? AccessibleStates.Focusable : 0); }
            }
        }
    }
}
