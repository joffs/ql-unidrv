// QL Label Maker (0.23, tabs): closing the window with two or more unsaved labels asks once, with a list (Visual Studio
// / Office): "Änderungen an 3 Etiketten speichern?", one row per label (ticked; title and its folder or "not saved
// yet"), Save / Don't save / Cancel. Save saves the ticked ones (Items[i].Save), Don't save none, Cancel (also Esc and
// the title bar's x) keeps the window open. Note: an optional line under the list (the form passes it, e.g. that an
// untitled label asks for its folder and name next). The look of QlMessage (question symbol, headline, buttons on the
// right, Save in the brand colour); approved mockup out/mockups/label-tabs ("Fenster schließen, 3 ungespeichert").
// The list scrolls beyond eight rows, so the window stays within 690 px.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class SaveChangesDialog : ScaledForm
    {
        public sealed class Item
        {
            public Item() { Save = true; }
            public string Title { get; set; }
            public string Detail { get; set; }   // the folder, or "not saved yet" (localised by the form)
            public bool Save { get; set; }
        }

        internal const int MaxRows = 8;
        readonly IList<Item> items;
        readonly List<CheckBox> boxes = new List<CheckBox>();
        readonly Label note;
        readonly Button save, dontSave, cancel;
        readonly Panel list;
        readonly TableLayoutPanel rows;
        readonly List<Label> folders = new List<Label>();

        public IList<Item> Items { get { return items; } }

        public string Note
        {
            get { return note.Text; }
            set { note.Text = value ?? ""; note.Visible = !string.IsNullOrEmpty(value); }
        }

        public SaveChangesDialog(IList<Item> items)
        {
            this.items = items;
            Text = "QL Label Maker";
            Theme.Use(this);
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            int em = Font.Height, wide = em * 26;

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(em, em, em, em * 3 / 4) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var glyph = SetupResult.Glyph(ResultKind.Question, em * 2);
            glyph.Margin = new Padding(0, 0, em, 0);
            root.Controls.Add(glyph, 0, 0);
            root.SetRowSpan(glyph, 3);

            string question = items.Count == 1 ? Lang.F("Save the changes to “{0}”?", items[0].Title) : Lang.F("Save the changes to {0} labels?", items.Count);
            var head = new Label { Text = question, AutoSize = true, MaximumSize = new Size(wide, 0), UseMnemonic = false,
                                   Font = new Font(Font.FontFamily, Font.Size * 1.25f, FontStyle.Bold), Margin = new Padding(0, em / 4, 0, em / 2) };
            Theme.Fore(head, Role.Heading);
            root.Controls.Add(head, 1, 0);

            // the list: one row per label, a check box with its title, the folder dim at the right
            list = new Panel { AutoScroll = true, Margin = new Padding(0, 0, 0, em / 2), Padding = new Padding(1) };
            Theme.Back(list, Role.Content);
            rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = Padding.Empty, Padding = new Padding(em / 2, em / 4, em / 2, em / 4), Dock = DockStyle.Top };
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            int folderWidth = em * 12;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var box = new CheckBox { Text = item.Title, Checked = item.Save, AutoSize = true, UseMnemonic = false, Margin = new Padding(0, em / 8, em, em / 8),
                                         AccessibleDescription = item.Detail };
                var it = item;
                box.CheckedChanged += delegate { it.Save = box.Checked; };
                boxes.Add(box);
                var folder = Theme.Fore(new PathLabel { Text = item.Detail ?? "", AutoSize = false, UseMnemonic = false,
                                                        Width = folderWidth, Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom, Margin = new Padding(0, em / 8, 0, em / 8) }, Role.Dim);
                folders.Add(folder);
                rows.Controls.Add(box, 0, i);
                rows.Controls.Add(folder, 1, i);
                rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                folderTips.SetToolTip(folder, item.Detail ?? "");
            }
            list.Controls.Add(rows);
            list.Paint += (s, e) => { using (var p = new Pen(Theme.Of(Role.ButtonLine))) e.Graphics.DrawRectangle(p, 0, 0, list.Width - 1, list.Height - 1); };
            root.Controls.Add(list, 1, 1);

            note = Theme.Fore(new Label { AutoSize = true, Visible = false, UseMnemonic = false, Margin = new Padding(0, 0, 0, em / 2) }, Role.Dim);
            root.Controls.Add(note, 1, 2);

            var buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, em / 2, 0, 0) };
            save = new Button { Text = Lang.T("&Save"), DialogResult = DialogResult.Yes, AutoSize = true, MinimumSize = new Size(em * 6, em * 2), Margin = new Padding(em / 2, 0, 0, 0) };
            save.Font = new Font(Font, FontStyle.Bold);
            Theme.Primary(save);
            dontSave = new Button { Text = Lang.T("Do&n't save"), DialogResult = DialogResult.No, AutoSize = true, MinimumSize = new Size(em * 6, em * 2), Margin = new Padding(em / 2, 0, 0, 0), FlatStyle = FlatStyle.System };
            cancel = new Button { Text = Lang.T("Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(em * 6, em * 2), Margin = new Padding(em / 2, 0, 0, 0), FlatStyle = FlatStyle.System };
            buttonRow.Controls.AddRange(new Control[] { save, dontSave, cancel });
            root.Controls.Add(buttonRow, 0, 3);
            root.SetColumnSpan(buttonRow, 2);
            Controls.Add(root);

            AcceptButton = save;
            CancelButton = cancel;
            AccessibleName = Text;
            AccessibleDescription = question;
            FitList();
            Shown += delegate { save.Focus(); };
        }

        readonly ToolTip folderTips = new ToolTip();

        // The list as high as its rows, up to MaxRows (then it scrolls), as wide as they need within limits; again after
        // the window took its screen's scaling (fonts and margins change then).
        void FitList()
        {
            int em = Font.Height;
            foreach (var f in folders) f.Width = em * 12;
            // a check box measures its box at 96 dpi: at 150 % the last letters would be cut off
            foreach (var box in boxes)
                box.MinimumSize = new Size(TextRenderer.MeasureText(box.Text, box.Font, Size.Empty, TextFormatFlags.NoPrefix).Width + LogicalToDeviceUnits(24), 0);
            var all = rows.GetPreferredSize(Size.Empty);
            int rowHeight = items.Count > 0 ? (all.Height - rows.Padding.Vertical) / items.Count : em;
            int width = Math.Min(Math.Max(em * 26, all.Width + SystemInformation.VerticalScrollBarWidth + list.Padding.Horizontal), em * 34);
            list.Size = new Size(width, Math.Min(items.Count, MaxRows) * rowHeight + rows.Padding.Vertical + list.Padding.Vertical);
            note.MaximumSize = new Size(width, 0);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            FitList();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            BeginInvoke((MethodInvoker)FitList);
        }

        internal CheckBox Box(int i) { return boxes[i]; }
        internal Button SaveButton { get { return save; } }
        internal Button DontSaveButton { get { return dontSave; } }
        internal Button CancelButtonControl { get { return cancel; } }
        internal Panel List { get { return list; } }

        // A folder shortened in the middle (a folder path), right-aligned, as Explorer does.
        sealed class PathLabel : Label
        {
            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.PathEllipsis | TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) folderTips.Dispose();
            base.Dispose(disposing);
        }
    }
}
