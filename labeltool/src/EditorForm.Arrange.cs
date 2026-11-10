// Label editor window (EditorForm.cs), part: the Arrange toolbar, layers and the context menu.
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
        // ---------- the Arrange toolbar ----------
        // Always visible: the commands of the Arrange menu and the context menu for the selection. A button that can't
        // do anything now is greyed (the row stays, so nothing jumps).
        ToolStrip arrangeBar;

        ToolStrip BuildArrangeBar()
        {
            arrangeBar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
            Func<object, Action, ToolStripButton> button = (icon, click) =>
                new ToolStripButton(null, null, delegate { click(); }) { Tag = icon, DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None };
            arrangeBar.Items.AddRange(new ToolStripItem[]
            {
                button(ArrangeIcon.Duplicate, Duplicate), button(ArrangeIcon.Delete, Delete), new ToolStripSeparator(),
                button(ArrangeIcon.RotateLeft, RotateLeft), button(ArrangeIcon.ResetRotation, ResetRotation), button(ArrangeIcon.RotateRight, Rotate), new ToolStripSeparator(),
                button(ArrangeIcon.AlignLeft, () => Align(0, -1)), button(ArrangeIcon.AlignCenter, () => Align(0, 0)), button(ArrangeIcon.AlignRight, () => Align(0, 1)),
                button(ArrangeIcon.AlignTop, () => Align(1, -1)), button(ArrangeIcon.AlignMiddle, () => Align(1, 0)), button(ArrangeIcon.AlignBottom, () => Align(1, 1)),
                new ToolStripSeparator(),
                button(ArrangeIcon.DistributeHorizontally, () => Distribute(0)), button(ArrangeIcon.DistributeVertically, () => Distribute(1)), new ToolStripSeparator(),
                button(Layer.ToFront, () => Order(Layer.ToFront)), button(Layer.Forward, () => Order(Layer.Forward)),
                button(Layer.Backward, () => Order(Layer.Backward)), button(Layer.ToBack, () => Order(Layer.ToBack)),
            });
            arrangeBar.Items.AddRange(BuildTextBar().ToArray());   // the text group (EditorForm.TextBar.cs)
            SizeArrangeBar();
            return arrangeBar;
        }

        // Icons at the present DPI.
        void SizeArrangeBar()
        {
            int n = S(16);
            arrangeBar.ImageScalingSize = new Size(n, n);
            arrangeBar.Padding = new Padding(S(4), S(2), S(4), S(2));
            foreach (ToolStripItem item in arrangeBar.Items)
            {
                if (!(item.Tag is ArrangeIcon) && !(item.Tag is Layer)) continue;   // the text group: SizeTextBar
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp))
                {
                    var r = new RectangleF(0, 0, n, n);
                    if (item.Tag is Layer) Brand.DrawLayerIcon(g, LayerIcons[(int)(Layer)item.Tag], r, Theme.Of(Role.Text));
                    else Brand.DrawArrangeIcon(g, (ArrangeIcon)item.Tag, r, Theme.Of(Role.Text));
                }
                var old = item.Image;
                item.Image = bmp;
                if (old != null) old.Dispose();
                item.Padding = new Padding(S(3));
            }
            SizeTextBar(n);
            // the panel's rotate buttons: the same icons at the same size
            foreach (var t in turnButtons)
            {
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp)) Brand.DrawArrangeIcon(g, t.Value, new RectangleF(0, 0, n, n), Theme.Of(Role.Text));
                var old = t.Key.Image;
                t.Key.Image = bmp;
                if (old != null) old.Dispose();
            }
        }

        // Enabled or not for the selection; the tooltips say name, key and (aligning) what it aligns with.
        void UpdateArrangeBar()
        {
            if (arrangeBar == null) return;
            var these = SelectedObjects;
            int n = these.Count;
            Func<string, string> plain = s => s.Replace("&", "");
            string with = n > 1 ? Lang.T("with the selection") : Lang.T("with the printable area");
            string[] layerNames = { Lang.T("Bring to &front"), Lang.T("Bring &forward"), Lang.T("Send &backward"), Lang.T("Send to bac&k") };
            string[] layerKeys = { Lang.T("Ctrl+Shift+]"), Lang.T("Ctrl+]"), Lang.T("Ctrl+["), Lang.T("Ctrl+Shift+[") };
            bool turnable = these.Any(o => !(o is LineObject));
            foreach (ToolStripItem item in arrangeBar.Items)
            {
                if (item.Tag is Layer)
                {
                    var how = (Layer)item.Tag;
                    item.Enabled = n > 0 && doc.CanRestack(these, how);
                    item.ToolTipText = plain(layerNames[(int)how]) + " (" + layerKeys[(int)how] + ")";
                    item.AccessibleName = item.ToolTipText;
                    continue;
                }
                if (!(item.Tag is ArrangeIcon)) continue;
                switch ((ArrangeIcon)item.Tag)
                {
                    case ArrangeIcon.Duplicate: item.Enabled = n > 0; item.ToolTipText = plain(Lang.T("D&uplicate")) + " (" + KeyText(Keys.Control | Keys.D) + ")"; break;
                    case ArrangeIcon.Delete: item.Enabled = n > 0; item.ToolTipText = plain(Lang.T("&Delete")) + " (" + Lang.T("Del") + ")"; break;
                    case ArrangeIcon.RotateLeft: item.Enabled = turnable; item.ToolTipText = Lang.T("Turn 90° anticlockwise"); break;
                    case ArrangeIcon.RotateRight: item.Enabled = turnable; item.ToolTipText = Lang.T("Turn 90° clockwise (Ctrl+R)"); break;
                    case ArrangeIcon.ResetRotation: item.Enabled = Turned; item.ToolTipText = Lang.T("Reset rotation (0°)"); break;
                    case ArrangeIcon.AlignLeft: Aligner(item, n, plain(Lang.T("Align &left")), with); break;
                    case ArrangeIcon.AlignCenter: Aligner(item, n, plain(Lang.T("Centre &horizontally")), with); break;
                    case ArrangeIcon.AlignRight: Aligner(item, n, plain(Lang.T("Align &right")), with); break;
                    case ArrangeIcon.AlignTop: Aligner(item, n, plain(Lang.T("Align &top")), with); break;
                    case ArrangeIcon.AlignMiddle: Aligner(item, n, plain(Lang.T("Centre &vertically")), with); break;
                    case ArrangeIcon.AlignBottom: Aligner(item, n, plain(Lang.T("Align &bottom")), with); break;
                    case ArrangeIcon.DistributeHorizontally:
                    case ArrangeIcon.DistributeVertically:
                        item.Enabled = n >= 3;
                        item.ToolTipText = plain((ArrangeIcon)item.Tag == ArrangeIcon.DistributeHorizontally ? Lang.T("Distribute hori&zontally") : Lang.T("Distribute verti&cally")) +
                                           (n >= 3 ? "" : " – " + Lang.T("Select three or more objects."));
                        break;
                }
                item.AccessibleName = item.ToolTipText;   // the buttons show only icons: screen readers read the tip
            }
            LockObjectItems(arrangeBar.Items);   // greyed while a text is edited
            UpdateTextBar();                     // ... but not the text group (EditorForm.TextBar.cs)
        }

        static void Aligner(ToolStripItem item, int n, string name, string with)
        {
            item.Enabled = n > 0;
            item.ToolTipText = name + " (" + with + ")";
        }

        static ToolStripButton Tool(ToolIcon icon, string text, string tip, EventHandler click)
        {
            return new ToolStripButton(text, null, click)
            {
                Tag = icon, ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
                ImageScaling = ToolStripItemImageScaling.None, ImageAlign = ContentAlignment.MiddleCenter, TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };
        }

        // One slim row: printer, label, copies and Print (petrol, at the right end). The boxes narrow in a small window.
        Control BuildCommandRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 8, RowCount = 1, Padding = new Padding(S(6), S(4), S(6), S(4)) };
            for (int i = 0; i < 8; i++) row.ColumnStyles.Add(i == 4 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> caption = t => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(S(6), 0, S(4), 0) };

            printerBox.Anchor = labelBox.Anchor = AnchorStyles.Left;
            row.Layout += delegate { FitBoxes(row); };
            printerBox.SelectedIndexChanged += delegate
            {
                if (loading || printerBox.SelectedItem == null) return;
                QlPrinters.Remember(printerBox.SelectedItem.ToString());   // the next start shows it (also QL Label Formats)
                ChangePrinter(printerBox.SelectedItem.ToString());
                CheckRoll();
            };
            labelBox.SelectedIndexChanged += delegate
            {
                if (loading || labelBox.SelectedItem == null) return;
                keepFileFormat = false;
                SetLabel((LabelChoice)labelBox.SelectedItem);
                Touched();
            };
            var labelCaption = caption(Lang.T("Label:"));
            string labelTip = Lang.T("Your label formats first, then Brother's papers. New lengths: Tools > QL Label Formats.");
            tips.SetToolTip(labelCaption, labelTip);
            tips.SetToolTip(labelBox, labelTip);
            copies.Width = S(60);
            copies.Anchor = AnchorStyles.Left;
            tips.SetToolTip(copies, Lang.T("Number of labels"));
            printButton.Text = Lang.T("Print");
            printButton.Font = new Font(Font, FontStyle.Bold);
            printButton.Padding = new Padding(S(10), S(1), S(10), S(1));
            printButton.Margin = Padding.Empty;
            Theme.Primary(printButton);
            printButton.Click += delegate { Print(); };
            // A split button: Print as before, its ▾ part opens Print and Print with data... (the File menu's commands).
            printMoreButton.Text = "▾";
            printMoreButton.Font = printButton.Font;
            printMoreButton.Padding = new Padding(S(3), S(1), S(3), S(1));
            printMoreButton.Margin = Padding.Empty;
            printMoreButton.Anchor = AnchorStyles.Top | AnchorStyles.Bottom;   // as high as Print
            printMoreButton.AccessibleName = Lang.T("More ways to print");
            tips.SetToolTip(printMoreButton, Lang.T("More ways to print"));
            Theme.Primary(printMoreButton);
            printMenu.Items.Add(new ToolStripMenuItem(Lang.T("&Print"), null, delegate { Print(); }) { ShortcutKeyDisplayString = KeyText(Keys.Control | Keys.P) });
            printMenu.Items.Add(new ToolStripMenuItem(Lang.T("Print with &data..."), null, delegate { PrintWithData(); }) { ShortcutKeyDisplayString = KeyText(PrintWithDataKeys) });
            printMoreButton.Click += delegate { printMenu.Show(printButton, 0, printButton.Height); };
            var printPair = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = Padding.Empty,
                                                  Margin = new Padding(S(8), 0, 0, 0), Anchor = AnchorStyles.Left };
            printPair.Controls.Add(printButton);
            printPair.Controls.Add(printMoreButton);

            printerCaption = caption(Lang.T("Printer:"));
            row.Controls.Add(printerCaption, 0, 0);
            row.Controls.Add(printerBox, 1, 0);
            row.Controls.Add(labelCaption, 2, 0);
            row.Controls.Add(labelBox, 3, 0);
            row.Controls.Add(caption(Lang.T("Copies:")), 5, 0);
            row.Controls.Add(copies, 6, 0);
            row.Controls.Add(printPair, 7, 0);
            commandRow = row;
            return row;
        }

        // The two boxes share what the row leaves over: as wide as their texts (printer up to 260, label up to 320),
        // narrower in a small window, never below 110.
        void FitBoxes(TableLayoutPanel row)
        {
            int rest = row.ClientSize.Width - row.Padding.Horizontal - S(12);
            foreach (Control c in row.Controls)
                if (c != printerBox && c != labelBox) rest -= c.PreferredSize.Width + c.Margin.Horizontal;
            rest -= printerBox.Margin.Horizontal + labelBox.Margin.Horizontal;
            // The longest printer name in the box's present font (its DropDownWidth can lag behind a DPI change).
            int want = printers.Count == 0 ? S(120) : printers.Max(n => TextRenderer.MeasureText(n, printerBox.Font).Width) + S(30);
            int pw = Math.Min(Math.Min(want, S(260)), Math.Max(S(110), rest * 2 / 5));
            if (printerBox.DropDownWidth < want) printerBox.DropDownWidth = want;
            int lw = Math.Min(S(320), Math.Max(S(110), rest - pw));
            if (printerBox.Width != pw) printerBox.Width = pw;
            if (labelBox.Width != lw) labelBox.Width = lw;
        }

        StatusStrip BuildStatusBar()
        {
            var status = new StatusStrip();
            rollStatus.IsLink = true;
            Theme.ItemLink(rollStatus, Role.Accent);
            rollStatus.Click += delegate { CheckRoll(); };
            fitButton.Text = Lang.T("Fit");
            fitButton.ToolTipText = Lang.T("Fit the label to the window (Ctrl+0)");
            fitButton.Click += delegate { SetZoom(0); };
            zoomOutButton.ToolTipText = Lang.T("Zoom out (Ctrl+Minus)");
            zoomOutButton.Click += delegate { ZoomStep(-1, null); };
            zoomInButton.ToolTipText = Lang.T("Zoom in (Ctrl+Plus, Ctrl+mouse wheel)");
            zoomInButton.Click += delegate { ZoomStep(1, null); };
            slider.Scrolled += delegate { ZoomTo(slider.Value, null); };
            sliderHost = new ToolStripControlHost(slider) { AutoSize = false, Size = new Size(S(120), S(20)), ToolTipText = Lang.T("Zoom (100 % = real size)") };
            zoomButton.ToolTipText = Lang.T("Zoom (100 % = real size)");
            zoomButton.DropDownItems.AddRange(ZoomItems(false).ToArray());
            zoomButton.DropDown.ShowItemToolTips = true;
            zoomButton.DropDownOpening += delegate { UpdateZoomItems(zoomButton.DropDownItems); };
            status.Items.AddRange(new ToolStripItem[] { labelStatus, rollStatus, fitButton, zoomOutButton, sliderHost, zoomInButton, zoomButton });
            // The mouse wheel over the status bar or the slider moves the slider (no Ctrl needed there).
            status.MouseWheel += OnStatusWheel;
            slider.MouseWheel += OnStatusWheel;
            SetZoom(0);
            statusBar = status;
            status.SizeChanged += delegate { FitLabelStatus(); };
            return status;
        }


        // ---------- layers and the context menu ----------

        void Order(Layer how)
        {
            var these = SelectedObjects;
            if (these.Count == 0 || !doc.CanRestack(these, how)) return;
            PushUndo(null);
            doc.Restack(these, how);
            Touched();
        }

        static readonly LayerIcon[] LayerIcons = { LayerIcon.BringToFront, LayerIcon.BringForward, LayerIcon.SendBackward, LayerIcon.SendToBack };

        List<ToolStripItem> LayerItems()
        {
            string[] names = { Lang.T("Bring to &front"), Lang.T("Bring &forward"), Lang.T("Send &backward"), Lang.T("Send to bac&k") };
            // Ctrl+] / Ctrl+[ as in Office; German keyboards type ] [ only with AltGr, so the German menu shows the page
            // keys (both work on every keyboard: LayerKey).
            string[] keys = { Lang.T("Ctrl+Shift+]"), Lang.T("Ctrl+]"), Lang.T("Ctrl+["), Lang.T("Ctrl+Shift+[") };
            var list = new List<ToolStripItem>();
            for (int i = 0; i < 4; i++)
            {
                var how = (Layer)i;
                list.Add(new ToolStripMenuItem(names[i], null, delegate { Order(how); })
                         { Tag = how, ShortcutKeyDisplayString = keys[i], ImageScaling = ToolStripItemImageScaling.None });
            }
            return list;
        }

        // Before a menu with layer items opens: possible or not for the selection, icons at the window's DPI.
        void UpdateLayerItems(ToolStripDropDown menu)
        {
            var these = SelectedObjects;
            int n = S(16);
            var dropDown = menu as ToolStripDropDownMenu;
            if (dropDown != null) dropDown.ImageScalingSize = new Size(n, n);
            foreach (ToolStripItem item in menu.Items)
            {
                if (!(item.Tag is Layer)) continue;
                var how = (Layer)item.Tag;
                item.Enabled = these.Count > 0 && doc.CanRestack(these, how);
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp))
                    Brand.DrawLayerIcon(g, LayerIcons[(int)how], new RectangleF(0, 0, n, n), Theme.Of(item.Enabled ? Role.Text : Role.Dim));
                if (item.Image != null) item.Image.Dispose();
                item.Image = bmp;
            }
        }

        ContextMenuStrip objectMenu;

        // Right click on an object (or the menu key / Shift+F10): its commands, the clipboard's first. The shortcuts are
        // shown only; the menu bar and the canvas handle the keys. On an empty spot: the canvas menu (paste there).
        void ShowObjectMenu(Point at)
        {
            if (canvas.Selection.Count == 0) { ShowCanvasMenu(at); return; }
            if (objectMenu != null) objectMenu.Dispose();
            var menu = objectMenu = new ContextMenuStrip { Font = menuBar.Font };
            Func<string, string, Action, ToolStripMenuItem> item = (text, keys, click) =>
                new ToolStripMenuItem(text, null, delegate { click(); }) { ShortcutKeyDisplayString = keys };
            menu.Items.AddRange(ObjectClipboardItems().ToArray());
            menu.Items.Add(item(Lang.T("D&uplicate"), KeyText(Keys.Control | Keys.D), Duplicate));
            menu.Items.Add(item(Lang.T("&Delete"), Lang.T("Del"), Delete));
            menu.Items.Add(new ToolStripSeparator());
            var rotate = item(Lang.T("R&otate 90°"), KeyText(Keys.Control | Keys.R), Rotate);
            rotate.Enabled = canvas.Selection.Any(x => !(x is LineObject));
            menu.Items.Add(rotate);
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Re&set rotation"), null, delegate { ResetRotation(); }) { Enabled = Turned });
            if (CanFit(canvas.Selection))   // pictures only
                menu.Items.Add(new ToolStripMenuItem(Lang.T("Fit to &label"), null, delegate { FitToLabel(canvas.Selection.ToList()); }));
            var align = new ToolStripMenuItem(Lang.T("&Align"));
            align.DropDownItems.AddRange(AlignItems().ToArray());
            UpdateAlignItems(align.DropDownItems);
            menu.Items.Add(align);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.AddRange(LayerItems().ToArray());
            UpdateLayerItems(menu);
            menu.Show(canvas, at);
        }

        // As the menu bar writes shortcuts (in Windows' language, like "Strg+D").
        static string KeyText(Keys keys)
        {
            return System.ComponentModel.TypeDescriptor.GetConverter(typeof(Keys)).ConvertToString(keys);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (TabKey(keyData)) return true;   // Ctrl+Tab, Ctrl+Shift+Tab: EditorForm.Tabs.cs
            if (TextEditingKey(keyData)) return true;   // EditorForm.TextEdit.cs
            // Ctrl + the number pad's + and − zoom too.
            if (keyData == (Keys.Control | Keys.Add) || keyData == (Keys.Control | Keys.Subtract))
            {
                ZoomStep(keyData == (Keys.Control | Keys.Add) ? 1 : -1, null);
                return true;
            }
            if (keyData == Keys.F1) { HelpPages.Open(this, HelpPage(canvas.Selection)); return true; }
            var how = LayerKey(keyData);
            if (how.HasValue && canvas.Selection.Count > 0) { Order(how.Value); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // F1: the help page of the selected kind of object; nothing or several kinds selected: the contents.
        internal static string HelpPage(IEnumerable<LabelObject> selection)
        {
            var pages = selection.Select(o => o is TextObject ? "text.html" : o is ImageObject ? "pictures.html" : o is BarcodeObject ? "codes.html"
                                              : o is LineObject || o is ShapeObject ? "lines-boxes.html" : HelpPages.LabelMaker).Distinct().ToList();
            return pages.Count == 1 ? pages[0] : HelpPages.LabelMaker;
        }

        // Ctrl+PgUp / Ctrl+PgDn on every keyboard, Ctrl+] / Ctrl+[ where the keyboard types ] and [ without AltGr;
        // with Shift all the way to the front or back.
        static Layer? LayerKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            bool all = mods == (Keys.Control | Keys.Shift);
            if (mods != Keys.Control && !all) return null;
            bool? up = key == Keys.PageUp ? true : key == Keys.PageDown ? false : (bool?)null;
            if (up == null && key == PlainKey(']')) up = true;
            if (up == null && key == PlainKey('[')) up = false;
            if (up == null) return null;
            return up.Value ? (all ? Layer.ToFront : Layer.Forward) : (all ? Layer.ToBack : Layer.Backward);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern short VkKeyScanW(char ch);

        // The key that types c on this keyboard without Shift, Ctrl or Alt (AltGr), or None.
        static Keys PlainKey(char c)
        {
            short v = VkKeyScanW(c);
            return v != -1 && (v >> 8) == 0 ? (Keys)(v & 0xFF) : Keys.None;
        }

        void OnCanvasKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // a drag in progress first (text editing ended before: ProcessCmdKey), then back to the arrow
                if (!canvas.CancelDraw()) { if (activeTool != null) SelectTool(null); else canvas.Selected = null; }
                e.Handled = true;
                return;
            }
            if (canvas.Selection.Count == 0) return;
            if (e.KeyCode == Keys.Delete) { Delete(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift))
            {
                if (canvas.Selection.Count > 0) ShowObjectMenu(canvas.MenuPoint); else ShowCanvasMenu(null);
                e.Handled = true;
                return;
            }
            double step = e.Shift ? 1 : 0.1, dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                default: return;
            }
            if (lastEdited != canvas) PushUndo(canvas);   // one undo step for a run of arrow keys
            foreach (var o in canvas.Selection)
            {
                o.X = Math.Round(o.X + dx, 2);
                o.Y = Math.Round(o.Y + dy, 2);
            }
            Touched();
            FillProps(null);
            e.Handled = true;
        }
    }
}
