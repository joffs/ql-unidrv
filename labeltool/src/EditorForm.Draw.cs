// Label editor window (EditorForm.cs), part: the active drawing tool. A click on a tool
// in the palette switches it on (SelectTool; the palette, its checks and the status bar follow ToolChanged); a drag on
// the canvas then draws its object (EditorCanvas.Draw.cs → OnDrawn), a click places it in its default size. The tool
// stays on for the next one, except the picture (its file dialog comes first, then one drag). Esc: back to the arrow
// (EditorForm.Arrange.cs). Enter on a tool button, the Insert menu and the keys still insert at a free place
// (EditorTool.Insert). One tool per window: a tab switch keeps it. A drawn text starts empty with the caret (CorelDRAW);
// left without typing it goes again. Choosing the arrow or another tool ends a text's editing.
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    // A drag of a drawing tool, for the status bar (mm, °, pt; not rounded).
    sealed class DrawMeasure
    {
        public EditorTool Tool;
        public double W, H;              // the dragged area (Shift applied)
        public double Length, Angle;     // the line tool: its length and the panel's angle (0-180°, counter-clockwise)
        public double FontSize;          // artistic text: the size it gets; else 0
    }

    sealed partial class EditorForm
    {
        EditorTool activeTool;
        ImageObject toolPicture;   // the picture tool's file, placed by the next drag or click
        TextObject drawnText;      // a drawn text being edited for the first time (DropUntypedText)
        bool drawnDirty;           // the label had unsaved changes before it
        object drawnStep;          // Add's undo step for it (UndoStack.Top)

        DrawMeasure measure;

        // The drag in progress, for the live status hint (EditorForm.Palette.cs); null: none.
        public DrawMeasure Measure { get { return measure; } }

        // A drawn text is in its first editing (the status bar's typing hint).
        public bool TypingDrawnText { get { return drawnText != null; } }

        // Measure or TypingDrawnText changed: a drag started, moved, ended or was cancelled (Measure null again); a
        // drawn text's first editing started or ended. Raised after this window's own status bar text.
        public event Action DrawStatusChanged;

        void RaiseDrawStatus() { if (DrawStatusChanged != null) DrawStatusChanged(); }

        // The canvas's drag (EditorCanvas.DrawMoved): the measure, or null once it ended.
        void OnDrawMoved()
        {
            if (!canvas.DrawDragging || activeTool == null) { if (measure == null) return; measure = null; RaiseDrawStatus(); return; }
            PointF a = canvas.DrawFromMm, z = canvas.DrawToMm;
            var m = new DrawMeasure { Tool = activeTool, W = Math.Abs(z.X - a.X), H = Math.Abs(z.Y - a.Y) };
            if (activeTool.Id == "line")
            {
                var l = new LineObject { X = Math.Min(a.X, z.X), Y = Math.Min(a.Y, z.Y), W = m.W, H = m.H, Rising = (z.X - a.X) * (z.Y - a.Y) < 0 };
                m.Length = Math.Sqrt(m.W * m.W + m.H * m.H);
                m.Angle = l.LineAngle;
            }
            if (activeTool.Id == "artistic") m.FontSize = ArtisticSize(m.H);
            measure = m;
            RaiseDrawStatus();
        }

        // Artistic text drawn this high (mm): its font size; a flat drag (under 6 pt) keeps the default one.
        static double ArtisticSize(double height)
        {
            var t = NewText(PointF.Empty);
            if (t.H <= 0) return t.Size;
            double size = Math.Min(400, Math.Round(t.Size * height / t.H * 2) / 2);
            return size >= 6 ? size : t.Size;
        }

        // The tool a drag draws; null: the arrow (select, move).
        public EditorTool ActiveTool { get { return activeTool; } }

        // The active tool changed (the palette, Esc, the picture placed or its dialog cancelled).
        public event Action ToolChanged;

        // null: the arrow. A tool is remembered as its group's (the palette's icon), as Insert does.
        public void SelectTool(EditorTool t)
        {
            canvas.EndEdit();   // the arrow or a tool ends a text's editing (a drawn text never typed in goes: DropUntypedText)
            if (t == activeTool && (t == null || t.Id != "picture")) return;
            canvas.CancelDraw();
            activeTool = t;
            toolPicture = null;
            if (t != null) UseTool(t);
            canvas.Drawing = OutlineOf(t);
            ShapeKind kind;
            canvas.DrawShape = t != null && Enum.TryParse(t.Id, true, out kind) ? NewShape(kind, PointF.Empty) : null;
            if (ToolChanged != null) ToolChanged();
            if (t == null) return;
            if (ActiveForm == this) canvas.Focus();   // Esc, and the drag right away
            if (t.Id == "picture") BeginInvoke(new Action(AskToolPicture));
        }

        // Shift during a drag of a drawing tool: the canvas switches the constraint at once (KeyPreview: whatever has the focus).
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey) canvas.DrawShift(true);
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.ShiftKey) canvas.DrawShift(false);
            base.OnKeyUp(e);
        }

        static DrawKind OutlineOf(EditorTool t)
        {
            if (t == null) return DrawKind.None;
            switch (t.Id)
            {
                case "line": return DrawKind.Line;
                case "ellipse": return DrawKind.Ellipse;
                case "artistic": case "paragraph": return DrawKind.Text;
                default: return DrawKind.Box;
            }
        }

        // The picture tool: the file first; Cancel (or a file that can't be read) goes back to the arrow.
        void AskToolPicture()
        {
            if (activeTool == null || activeTool.Id != "picture" || toolPicture != null) return;
            string file = AskPicture();
            var img = file != null ? LoadPicture(this, file) : null;
            if (activeTool == null || activeTool.Id != "picture") return;   // switched meanwhile
            if (img == null) { SelectTool(null); return; }
            toolPicture = img;
        }

        static double R(double v) { return Math.Round(v, 1); }

        // A drag (a → z, mm, as dragged) or a click (a == z) with the active tool: its object, one undo step.
        void OnDrawn(PointF a, PointF z, bool click)
        {
            var t = activeTool;
            if (t == null || printing) return;
            var r = RectangleF.FromLTRB(Math.Min(a.X, z.X), Math.Min(a.Y, z.Y), Math.Max(a.X, z.X), Math.Max(a.Y, z.Y));
            var at = new PointF((float)R(a.X), (float)R(a.Y));
            switch (t.Id)
            {
                case "artistic":
                {
                    var text = NewText(at);
                    if (!click && text.H > 0)
                    {
                        text.Size = ArtisticSize(r.Height);   // the dragged height is the line's height
                        text.X = R(r.X); text.Y = R(r.Y);
                        EditorRender.FitText(text);
                    }
                    StartDrawnText(text);
                    return;
                }
                case "paragraph":
                {
                    var text = NewTextFrame(at);
                    if (!click) { text.X = R(r.X); text.Y = R(r.Y); text.W = Math.Max(5, R(r.Width)); text.H = Math.Max(3, R(r.Height)); }
                    StartDrawnText(text);
                    return;
                }
                case "line":
                {
                    if (click) { Add(NewLine(at)); return; }
                    Add(new LineObject { X = R(r.X), Y = R(r.Y), W = R(r.Width), H = R(r.Height), Rising = (z.X - a.X) * (z.Y - a.Y) < 0 });
                    return;
                }
                case "barcode":
                {
                    var b = NewBarcode(at);
                    if (!click) { b.X = R(r.X); b.Y = R(r.Y); b.W = Math.Max(10, R(r.Width)); b.H = Math.Max(4, R(r.Height)); }
                    Add(b);
                    FocusField(dataBox);
                    return;
                }
                case "qr":
                {
                    var q = NewQrCode(at);
                    if (!click) { q.X = R(r.X); q.Y = R(r.Y); q.W = q.H = Math.Max(5, R(Math.Min(r.Width, r.Height))); }
                    Add(q);
                    FocusField((TextBox)qrEditors["url"]);
                    return;
                }
                case "picture":
                {
                    var img = toolPicture;
                    if (img == null) { BeginInvoke(new Action(AskToolPicture)); return; }   // the dialog was not answered yet
                    SizeF size;
                    if (click) size = PictureSize(img.W, img.H);
                    else
                    {
                        double k = Math.Min(r.Width / img.W, r.Height / img.H);   // the picture's proportions, inside the area
                        size = new SizeF((float)(img.W * k), (float)(img.H * k));
                        at = new PointF((float)R(r.X), (float)R(r.Y));
                    }
                    img.X = at.X; img.Y = at.Y; img.W = Math.Max(1, R(size.Width)); img.H = Math.Max(1, R(size.Height));
                    Add(img);
                    SelectTool(null);   // a second picture asks for its file again
                    return;
                }
            }
            ShapeKind kind;
            if (!Enum.TryParse(t.Id, true, out kind)) return;
            var s = NewShape(kind, at);
            if (!click) { s.X = R(r.X); s.Y = R(r.Y); s.W = Math.Max(1, R(r.Width)); s.H = Math.Max(1, R(r.Height)); }
            // dragged upwards: upside down, as PowerPoint does (no flip in the model: a half turn is the same for these
            // shapes, all symmetric left to right, so a drag to the left changes nothing)
            if (!click && z.Y < a.Y && s.UpsideDownDiffers) s.Rotation = 180;
            Add(s);
        }

        // Editing ended (EditorForm.TextEdit.cs): a drawn text left empty (Esc, a click beside it, the arrow, another
        // tool, a tab switch) goes again, and so do its undo steps (CorelDRAW). Typed in, it stays (selected). Insert
        // (Enter, menu, key) keeps its placeholder text as before. True: removed.
        bool DropUntypedText(TextObject ended, bool typed)
        {
            var t = drawnText;
            drawnText = null;
            if (t == null || t != ended || t.Text.Length > 0 || !doc.Objects.Contains(t)) { RaiseDrawStatus(); return false; }
            // Add's step, and the editing's (typed, then deleted again). Any other step since (the panel: its size, its
            // place) stays, and the text goes as an emptied text does, in a step of its own (EditorForm.TextEdit.cs).
            if (undo.StepsAfter(drawnStep) != (typed ? 1 : 0)) { RaiseDrawStatus(); return false; }
            doc.Objects.Remove(t);
            if (typed) undo.DropLast();
            undo.DropLast();
            if (canvas.Selection.Contains(t)) canvas.Select(canvas.Selection.Where(o => o != t).ToList());
            if (dirty != drawnDirty) { dirty = drawnDirty; UpdateTitle(); }
            if (newText == t) newText = null;
            RaiseDrawStatus();
            return true;
        }

        // A drawn text: empty, the caret blinking in it (no placeholder; CorelDRAW).
        void StartDrawnText(TextObject text)
        {
            text.Text = "";
            drawnDirty = dirty;
            Add(text);
            drawnStep = undo.Top;
            drawnText = text;
            if (ActiveForm == this) canvas.Focus();
            canvas.BeginEdit(text);
            RaiseDrawStatus();
        }
    }
}
