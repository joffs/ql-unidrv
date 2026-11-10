// Label editor window (EditorForm.cs), part: inserting objects (text, line, box, picture, codes) and moving them.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- objects ----------
        PointF FreeSpot()
        {
            var p = layout != null ? layout.Printable : new RectangleF(3, 3, 50, 20);
            float step = 2f * (doc.Objects.Count % 5);
            return new PointF(p.X + 1 + step, p.Y + 1 + step);
        }

        void Add(LabelObject o)
        {
            PushUndo(null);
            doc.Objects.Add(o);
            canvas.Selected = o;
            lastEdited = null;
            Touched();
            FillProps(null);
        }

        static bool IsQr(LabelObject o) { var b = o as BarcodeObject; return b != null && b.Kind == Symbology.Qr; }

        static string OneLine(string s) { return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '); }

        // A new object's content field, ready to type over. Only while this window is active: focusing a control of an
        // inactive window can bring it to the front (an inserted object from automation, x64 check 2026-11-01).
        void FocusField(TextBoxBase field)
        {
            if (ActiveForm != this) return;
            field.Focus();
            field.SelectAll();
        }

        // The type list's order
        static readonly Symbology[] LinearKinds = { Symbology.Code128, Symbology.Gs1_128, Symbology.Ean13 };

        // "Insert field" for a text or barcode content box: today's date, a date in N days, the time, a custom data field.
        System.Windows.Forms.Button FieldButton(TextBoxBase target)
        {
            return FieldButton(token =>
            {
                target.Focus();
                target.SelectedText = token;   // TextChanged applies it
            });
        }

        System.Windows.Forms.Button FieldButton(Action<string> insert)
        {
            var button = new System.Windows.Forms.Button { Text = Lang.T("Insert field ▾"), AutoSize = true };
            var menu = new ContextMenuStrip();
            Action<string, string> add = (caption, token) => menu.Items.Add(caption + "   " + token, null, delegate { insert(token); });
            add(Lang.T("Today's date"), "{date}");
            add(Lang.T("Date in 30 days"), "{date+30}");
            add(Lang.T("Date as 2026-10-29"), "{date:yyyy-MM-dd}");
            add(Lang.T("Time"), "{time}");
            menu.Items.Add(new ToolStripSeparator());
            // Two short grey lines (one long line made the menu very wide).
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Filled in when shown and printed.")) { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Also: {date+7}, {date-1}, {date:dd.MM.yy}")) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            // A data field for print with data, {Name} or {Name|default} (OwnFieldDialog.cs)
            menu.Items.Add(Lang.T("Custom field..."), null, delegate
            {
                string field = OwnFieldDialog.Ask(this);
                if (field != null) insert(field);
            });
            button.Tag = menu;   // tests reach the menu here
            button.Click += delegate { menu.Show(button, 0, button.Height); };
            return button;
        }

        // A failure: what failed as the headline, Windows' own text behind Show details.
        static void Failed(IWin32Window owner, string what, Exception ex)
        {
            QlMessage.Ask(owner, AppTitle, what, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message);
        }

        // The selected objects in their stacking order (copies and commands keep it).
        List<LabelObject> SelectedObjects { get { return doc.Objects.Where(canvas.Selection.Contains).ToList(); } }

        // Where the selected objects are (mm), for the part drawn again while dragging; dragBounds: where they were.
        RectangleF? dragBounds;
        RectangleF SelectionBounds() { return canvas.Selection.Count == 0 ? RectangleF.Empty : EditorCanvas.BoxOf(canvas.Selection); }
        // Both places; not RectangleF.Union with IsEmpty: a level or upright line's box has no height or width, and
        // "empty" there dropped where it was (trails while moving it)
        static RectangleF Union(RectangleF a, RectangleF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
        }

        // After a change to several objects at once: one undo step (taken before), render, panel.
        void ChangedAll()
        {
            lastEdited = null;
            Touched();
            FillProps(null);
        }

        void Duplicate()
        {
            if (canvas.Editing != null) return;
            var these = SelectedObjects;
            if (these.Count == 0) return;
            PushUndo(null);
            var copies = these.Select(o => { var c = o.Clone(); c.X += 2; c.Y += 2; return c; }).ToList();
            doc.Objects.AddRange(copies);
            canvas.Select(copies);
            ChangedAll();
        }

        void Delete()
        {
            if (canvas.Editing != null) { canvas.EditChange(e => e.Delete(false)); return; }   // the character after the caret
            var these = SelectedObjects;
            if (these.Count == 0) return;
            PushUndo(null);
            doc.Objects.RemoveAll(these.Contains);
            canvas.Selected = null;
            ChangedAll();
        }

        // Ctrl+A: all objects; in a text field its text.
        void SelectAll()
        {
            var field = ActiveControl as TextBoxBase;
            if (field == null) { var up = ActiveControl as UpDownBase; if (up != null) field = up.Controls.OfType<TextBoxBase>().FirstOrDefault(); }
            if (field != null) { field.SelectAll(); return; }
            if (canvas.Editing != null) { canvas.EditChange(e => e.SelectAll()); canvas.Focus(); return; }   // the whole text
            canvas.Select(doc.Objects);
            canvas.Focus();
        }

        void Rotate() { Rotate(1); }

        // "Reset rotation": the selected objects back to 0° (quarter turns and free angle; lines have no rotation).
        bool Turned { get { return SelectedObjects.Any(o => !(o is LineObject) && o.Angle != 0); } }

        void ResetRotation()
        {
            var these = SelectedObjects.Where(o => !(o is LineObject) && o.Angle != 0).ToList();
            if (these.Count == 0) return;
            PushUndo(null);
            foreach (var o in these)
            {
                o.SetAngle(0);
                var t = o as TextObject;
                if (t != null) EditorRender.FitText(t);
            }
            ChangedAll();
        }
        void RotateLeft() { Rotate(3); }

        // quarters: clockwise quarter turns (3 = one to the left).
        void Rotate(int quarters)
        {
            if (canvas.Editing != null) return;
            var these = SelectedObjects.Where(o => !(o is LineObject)).ToList();
            if (these.Count == 0) return;
            PushUndo(null);
            foreach (var o in these)
            {
                for (int i = 0; i < quarters; i++) o.Rotate90();
                var t = o as TextObject;
                if (t != null) EditorRender.FitText(t);
            }
            ChangedAll();
        }

        // axis 0 = along the label (left/right), 1 = across (top/bottom); where -1 / 0 / 1 = start / centre / end. One
        // object: of the printable area; several: of their common box (left = the leftmost object's left edge, ...).
        void Align(int axis, int where)
        {
            var these = SelectedObjects;
            if (these.Count == 0 || layout == null) return;
            var p = these.Count > 1 ? EditorCanvas.BoxOf(these) : layout.Printable;
            PushUndo(null);
            foreach (var x in these)
            {
                var b = x.Bounds;   // a turned object: the box around it
                if (axis == 0) x.X = Math.Round(x.X + (where < 0 ? p.Left : where > 0 ? p.Right - b.Width : p.Left + (p.Width - b.Width) / 2) - b.Left, 2);
                else x.Y = Math.Round(x.Y + (where < 0 ? p.Top : where > 0 ? p.Bottom - b.Height : p.Top + (p.Height - b.Height) / 2) - b.Top, 2);
            }
            ChangedAll();
        }

        // Three or more objects: equal gaps between them along the axis, the first and the last stay where they are.
        // When they don't fit side by side (they overlap), their centres get equal distances instead.
        void Distribute(int axis)
        {
            // By the box around each object (turned ones included).
            Func<LabelObject, double> start = o => axis == 0 ? o.Bounds.X : o.Bounds.Y, size = o => axis == 0 ? o.Bounds.Width : o.Bounds.Height;
            Func<LabelObject, double> centre = o => start(o) + size(o) / 2;
            Action<LabelObject, double> place = (o, v) => { if (axis == 0) o.X = Math.Round(o.X + v - start(o), 2); else o.Y = Math.Round(o.Y + v - start(o), 2); };
            var these = SelectedObjects.OrderBy(start).ToList();
            if (these.Count < 3) return;
            double from = start(these[0]), to = these.Max(o => start(o) + size(o));
            double gap = (to - from - these.Sum(size)) / (these.Count - 1);
            PushUndo(null);
            if (gap >= 0)
            {
                double at = from;
                foreach (var o in these) { place(o, at); at += size(o) + gap; }
            }
            else
            {
                these = these.OrderBy(centre).ToList();
                double first = centre(these[0]), step = (centre(these[these.Count - 1]) - first) / (these.Count - 1);
                for (int i = 0; i < these.Count; i++) place(these[i], first + i * step - size(these[i]) / 2);
            }
            ChangedAll();
        }

        // The align commands (Arrange menu, Align in the context menu). A grey first line says what they align with.
        List<ToolStripItem> AlignItems()
        {
            Func<string, Action, string, ToolStripMenuItem> item = (text, click, tag) => new ToolStripMenuItem(text, null, delegate { click(); }) { Tag = tag };
            return new List<ToolStripItem>
            {
                new ToolStripMenuItem(Lang.T("Aligns with the printable area")) { Enabled = false, Tag = "with" },
                item(Lang.T("Align &left"), () => Align(0, -1), "align"),
                item(Lang.T("Centre &horizontally"), () => Align(0, 0), "align"),
                item(Lang.T("Align &right"), () => Align(0, 1), "align"),
                new ToolStripSeparator(),
                item(Lang.T("Align &top"), () => Align(1, -1), "align"),
                item(Lang.T("Centre &vertically"), () => Align(1, 0), "align"),
                item(Lang.T("Align &bottom"), () => Align(1, 1), "align"),
                new ToolStripSeparator(),
                item(Lang.T("Distribute hori&zontally"), () => Distribute(0), "distribute"),
                item(Lang.T("Distribute verti&cally"), () => Distribute(1), "distribute"),
            };
        }

        void UpdateAlignItems(ToolStripItemCollection items)
        {
            int n = canvas.Selection.Count;
            foreach (ToolStripItem i in items)
            {
                string tag = i.Tag as string;
                if (tag == "with") i.Text = n > 1 ? Lang.T("Aligns with the selection") : Lang.T("Aligns with the printable area");
                else if (tag == "align") i.Enabled = n > 0;
                else if (tag == "distribute") { i.Enabled = n >= 3; i.ToolTipText = n >= 3 ? null : Lang.T("Select three or more objects."); }
            }
        }
    }
}
