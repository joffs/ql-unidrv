// Label editor window (EditorForm.cs), part: editing a text on the label (the canvas does the
// editing: EditorCanvas.Editing.cs). Here: starting it (double-click, F2, Enter, typing, a new text, "Edit text (F2)"),
// key routing, the undo step per editing session, rendering per key, an emptied text removed when editing ends, the
// status bar line, View > Show overflow while editing (remembered per user), the menus while editing.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // The undo step of an editing session (one per session; arrow keys after it start a new one).
        readonly object editingStep = new object();
        TextObject editingObject;   // the text being edited (for when editing ends)
        bool editingChanged;        // the session changed it (its undo step is taken)
        TextObject newText;         // a text from the Text tool not edited yet (AddNewText)
        ToolStripMenuItem overflowItem;

        void WireTextEditing()
        {
            object on = ToolSetting("ShowOverflowWhileEditing");
            canvas.ShowOverflow = !(on is int) || (int)on != 0;
            // typing on one selected text starts editing it (the caret at the end)
            canvas.TypingTarget = () => canvas.Selection.Count == 1 ? canvas.Selected as TextObject : null;
            // ... on a new text, still its placeholder: typing replaces it (no one wants "TextAbc")
            canvas.TypingReplaces = IsNewText;
            canvas.EditFirstChange += delegate { PushUndo(editingStep); editingChanged = true; };
            canvas.TextEdited += TextTouched;
            canvas.EditCaretMoved += delegate { EditCaretMoved(); };
            canvas.EditingChanged += delegate { EditingChanged(); };
            canvas.EditMessage += text => labelStatus.Text = text;
        }

        // F2, Enter, "Edit text (F2)": the selected text, the caret at its end (also on a new text, as on every text).
        void StartEditing()
        {
            var t = canvas.Selected as TextObject;
            if (t == null || printing) return;
            canvas.BeginEdit(t);
            if (ActiveForm == this) canvas.Focus();   // not from automation into an inactive window
        }

        // "Insert field": at the caret; a text not being edited starts editing with the caret at its end.
        void InsertField(string code)
        {
            var t = canvas.Selected as TextObject;
            if (t == null) return;
            if (canvas.EditedText != t) canvas.BeginEdit(t, null, IsNewText(t));   // a new text's placeholder makes way for the field
            canvas.EditChange(e => e.InsertField(code));
            canvas.Focus();
        }

        // A new text from the Text tool, its placeholder unchanged and not edited yet (moving or resizing it keeps that).
        bool IsNewText(TextObject t) { return t != null && t == newText && t.Text == Lang.T("Text"); }

        void EditingChanged()
        {
            var t = canvas.EditedText;
            if (t != null)
            {
                newText = null;   // edited once: from now on as every text
                editingObject = t;
                editingChanged = false;
                labelStatus.Text = Lang.T("Editing text – Esc or a click beside it ends.");
            }
            else
            {
                var ended = editingObject;
                editingObject = null;
                if (DropUntypedText(ended, editingChanged)) { }   // a drawn text never typed in: gone (EditorForm.Draw.cs)
                else if (ended != null && ended.Text.Length == 0 && doc.Objects.Contains(ended))
                {
                    // an emptied text goes, in the session's undo step
                    if (!editingChanged) PushUndo(null);
                    doc.Objects.Remove(ended);
                    if (canvas.Selection.Contains(ended)) canvas.Select(canvas.Selection.Where(o => o != ended).ToList());
                    if (!dirty) { dirty = true; UpdateTitle(); }
                }
                lastEdited = null;
                if (labelParts.Length > 0) { labelStatus.Text = string.Join("  ·  ", labelParts); FitLabelStatus(); }
            }
            Render();   // the fields as codes or values, the quiet zones, the strip
            FillProps(null);
            UpdateCommands();
        }

        // A key changed the edited text: render only around it (as dragging does), the panel, the title.
        void TextTouched(RectangleF before)
        {
            if (!dirty) { dirty = true; UpdateTitle(); }
            var t = canvas.EditedText;
            var shown = canvas.Rendered;
            if (t != null && layout != null && !autoLength && shown != null && shown.Width == layout.DotsX && shown.Height == layout.DotsY)
            {
                var part = Union(before, t.Bounds);
                EditorRender.RenderPart(shown, doc, layout, part, DateTime.Now, null);
                canvas.RenderedPart(part, false);   // the canvas repaints only the text's area after the key
                // the grey copy beyond the label's edge: only when the text reaches it (now or before the key)
                if (part.Left < 0 || part.Top < 0 || part.Right > layout.ViewWidthMm || part.Bottom > layout.ViewHeightMm) canvas.OutsideChanged();
            }
            else Render();
            FillProps(null);
            UpdateCommands();
        }

        void EditCaretMoved()
        {
            var t = canvas.EditedText;
            if (t == null) return;
            bool was = loading;
            loading = true;
            try { FillStyleRows(t); }
            finally { loading = was; }
            UpdateCommands();
        }

        // ProcessCmdKey, first: while editing the keys go to the text (the canvas has the focus); the object commands'
        // keys do nothing then. Not editing: F2 (or Enter on the canvas) edits the selected text.
        bool TextEditingKey(Keys keyData)
        {
            if (TextBarKey(keyData)) return true;   // alignment while editing, bold/italic/underline/justify selected
            if (canvas.Editing != null)
            {
                if (canvas.HasKeys && canvas.EditKey(keyData)) return true;
                if (keyData == (Keys.Control | Keys.D) || keyData == (Keys.Control | Keys.R) || LayerKey(keyData).HasValue) return true;
                if (keyData == Keys.F2) { canvas.Focus(); return true; }
                return false;
            }
            if ((keyData == Keys.F2 || keyData == Keys.Enter && canvas.HasKeys) && canvas.Selection.Count == 1 && canvas.Selected is TextObject)
            {
                StartEditing();
                return true;
            }
            return false;
        }

        // View > Show overflow while editing.
        ToolStripMenuItem OverflowItem()
        {
            overflowItem = new ToolStripMenuItem(Lang.T("Show o&verflow while editing"))
            {
                Checked = canvas.ShowOverflow,
                ToolTipText = Lang.T("On: the lines that don't fit a paragraph text's frame show light grey below it while you edit. Off: the text scrolls inside the frame. Printing is the same either way."),
            };
            overflowItem.Click += delegate
            {
                canvas.ShowOverflow = !canvas.ShowOverflow;
                overflowItem.Checked = canvas.ShowOverflow;
                ToolSetting("ShowOverflowWhileEditing", canvas.ShowOverflow ? 1 : 0);
                canvas.OverflowOptionChanged();
            };
            return overflowItem;
        }

        // Right click in the text being edited: the clipboard commands and Select all for its characters.
        void ShowTextMenu(Point at)
        {
            if (objectMenu != null) objectMenu.Dispose();
            var menu = objectMenu = new ContextMenuStrip { Font = menuBar.Font };
            var e = canvas.Editing;
            var cut = CutItem(); var copy = CopyItem(); var paste = PasteItem(null); var plain = PastePlainItem(null);
            cut.Enabled = copy.Enabled = e.HasSelection;
            menu.Items.AddRange(new ToolStripItem[] { cut, copy, paste, plain, new ToolStripSeparator() });
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Select &all"), null, delegate { SelectAll(); }) { ShortcutKeyDisplayString = KeyText(Keys.Control | Keys.A) });
            menu.Show(canvas, at);
        }

        // While editing, the object commands (Duplicate, turn, align, layers) are greyed in the menus and the toolbar.
        void LockObjectItems(ToolStripItemCollection items)
        {
            if (canvas.Editing == null) return;
            foreach (ToolStripItem i in items)
                if (!(i is ToolStripSeparator)) i.Enabled = false;
        }
    }
}
