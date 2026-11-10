// Label editor window (EditorForm.cs), part: the tools on the left, the arrow (select) at the top, then one button per
// tool group (ToolRegistry.cs). Since 0.23 a click switches a tool on and the canvas draws with it (EditorForm.Draw.cs,
// as in CorelDRAW); Enter or Space on a button inserts at a free place as before (keyboard users, tests). The active tool
// shows checked, the status bar names it with its keys.
// A group with more tools has a small triangle in its corner: a click switches on the group's last tool, a click on the
// triangle or pressing and holding (400 ms) opens the flyout with all its tools (the Right arrow key too); the icon shows
// the last tool, the name stays the group's. The names can be hidden (View > Show tool names, right click on the
// palette): icons only, 44 px wide. When the window is too short, the palette wraps into more columns instead of
// scrolling (editor-next step 3, mockup out/mockups/tool-flyouts/, 2026-11-08).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        PaletteStrip palette;
        bool showToolNames = true;
        ToolStripButton holdButton;         // the group button pressed now (holding opens its flyout)
        bool cornerPressed, flyoutByHold;
        ContextMenuStrip flyout;
        ToolGroup flyoutGroup;              // the group whose flyout is open
        readonly Timer holdTimer = new Timer { Interval = 400 };
        ToolStripButton arrowButton;        // the select tool, at the top
        ToolStripButton mouseUpButton;      // pressed with the mouse (till just after release): its Click is the mouse's, not a key's
        bool flyoutByKey;                   // the flyout was opened with the Right arrow key: its pick inserts
        ToolStripStatusLabel toolStatus;    // the active tool and its keys, left in the status bar
        ToolStripSeparator toolStatusLine;  // between it and the label's summary

        // The palette: a vertical strip that wraps into columns (Flow, top down).
        sealed class PaletteStrip : ToolStrip
        {
            public Action<ToolStripButton> OpenFlyout;

            // Right arrow on a focused group: its flyout (the flyout opens to the right).
            protected override bool ProcessDialogKey(Keys keyData)
            {
                var button = Items.OfType<ToolStripButton>().FirstOrDefault(b => b.Selected);
                var group = button == null ? null : button.Tag as ToolGroup;
                if (keyData == Keys.Right && group != null && group.HasFlyout) { OpenFlyout(button); return true; }
                return base.ProcessDialogKey(keyData);
            }
        }

        ToolStrip BuildPalette()
        {
            object names = ToolSetting("ShowToolNames");
            showToolNames = !(names is int) || (int)names != 0;
            palette = new PaletteStrip
            {
                Dock = DockStyle.Left, LayoutStyle = ToolStripLayoutStyle.Flow, CanOverflow = false,
                GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, AutoSize = false, ShowItemToolTips = true,
                OpenFlyout = b => ShowFlyout(b, true)
            };
            var flow = (FlowLayoutSettings)palette.LayoutSettings;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = true;
            arrowButton = new ToolStripButton(Lang.T("Select"))
            {
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
                ImageScaling = ToolStripItemImageScaling.None, ImageAlign = ContentAlignment.MiddleCenter, TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false, AccessibleName = Lang.T("Select"), Checked = true
            };
            arrowButton.Click += delegate { SelectTool(null); };
            palette.Items.Add(arrowButton);
            foreach (var group in ToolGroups())
            {
                var g = group;
                var button = new ToolStripButton(g.Name)
                {
                    Tag = g, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
                    ImageScaling = ToolStripItemImageScaling.None, ImageAlign = ContentAlignment.MiddleCenter, TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false, AccessibleName = g.Name
                };
                button.MouseDown += (s, e) =>
                {
                    // ToolStrip raises Click before MouseUp (with plain EventArgs): the press marks the click as the mouse's
                    if (e.Button == MouseButtons.Left) mouseUpButton = button;
                    PressGroup(button, e);
                };
                button.MouseUp += delegate
                {
                    holdTimer.Stop();
                    BeginInvoke((MethodInvoker)delegate { mouseUpButton = null; });   // after the Click; later Enter/Space is a key again
                };
                button.Click += delegate { ClickGroup(button); };
                button.Paint += (s, e) => PaintFlyoutMark(button, e.Graphics);
                palette.Items.Add(button);
            }
            holdTimer.Tick += delegate
            {
                holdTimer.Stop();
                if (holdButton == null || (MouseButtons & MouseButtons.Left) == 0) return;
                flyoutByHold = true;   // the Click that follows the release doesn't insert
                ShowFlyout(holdButton, false);
            };
            palette.Resize += delegate { FitPaletteColumns(); };
            ToolChanged += ShowActiveTool;
            DrawStatusChanged += UpdateToolHint;
            var menu = new ContextMenuStrip();
            menu.Items.Add(ToolNamesItem());
            menu.Opening += delegate { ((ToolStripMenuItem)menu.Items[0]).Checked = showToolNames; };
            palette.ContextMenuStrip = menu;
            SizePalette();
            return palette;
        }

        // View > "Show tool names" (on by default, remembered per user): also in the palette's context menu.
        ToolStripMenuItem ToolNamesItem()
        {
            var item = new ToolStripMenuItem(Lang.T("Show tool &names")) { Checked = showToolNames, CheckOnClick = false };
            item.Click += delegate { SetToolNames(!showToolNames); };
            return item;
        }

        // Keeps a menu's "Show tool names" check right (call from its DropDownOpening).
        void UpdateToolNamesItem(ToolStripMenuItem item) { item.Checked = showToolNames; }

        void SetToolNames(bool on)
        {
            if (on == showToolNames) return;
            showToolNames = on;
            ToolSetting("ShowToolNames", on ? 1 : 0);
            SizePalette();
            PerformLayout();
        }

        void PressGroup(ToolStripButton button, MouseEventArgs e)
        {
            holdTimer.Stop();
            if (e.Button != MouseButtons.Left) return;
            var g = (ToolGroup)button.Tag;
            holdButton = button;
            flyoutByHold = false;
            cornerPressed = g.HasFlyout && InCorner(button, e.Location);
            if (g.HasFlyout && !cornerPressed) holdTimer.Start();
        }

        // The click zone of the triangle: the button's bottom right corner, a little larger than the mark.
        bool InCorner(ToolStripButton button, Point p)
        {
            int zone = S(14);
            return p.X >= button.Width - zone && p.Y >= button.Height - zone;
        }

        void ClickGroup(ToolStripButton button)
        {
            var g = (ToolGroup)button.Tag;
            bool corner = cornerPressed, held = flyoutByHold, mouse = mouseUpButton == button;
            cornerPressed = flyoutByHold = false;
            holdButton = mouseUpButton = null;
            if (held) return;   // the flyout is open already
            if (corner) { ShowFlyout(button, false); return; }
            // the mouse switches the tool on (the canvas draws with it); Enter or Space inserts at a free place
            if (mouse) SelectTool(g.Current); else g.Current.Insert();
        }

        // The group's tools to the right of its button: icon, name, key; the explanation in the tooltip; ✓ on the tool
        // the button shows. select: the first entry selected (keyboard); a pick then inserts, with the mouse it switches
        // the tool on.
        void ShowFlyout(ToolStripButton button, bool select)
        {
            var g = (ToolGroup)button.Tag;
            if (!g.HasFlyout) return;
            if (flyout != null) flyout.Dispose();
            int size = S(20);
            flyout = new ContextMenuStrip { Font = menuBar.Font, ShowCheckMargin = true, ShowImageMargin = true, ShowItemToolTips = true, ImageScalingSize = new Size(size, size) };
            foreach (var tool in g.Tools)
            {
                var t = tool;
                var item = new ToolStripMenuItem(t.Name, ToolImage(t.Icon, size), delegate { if (flyoutByKey) t.Insert(); else SelectTool(t); })
                {
                    Checked = t == g.Current, ImageScaling = ToolStripItemImageScaling.None,
                    ToolTipText = t.Name + (t.Keys != Keys.None ? " (" + KeyText(t.Keys) + ")" : "") + (t.Explain != null ? "\n" + t.Explain : "")
                };
                if (t.Keys != Keys.None) item.ShortcutKeyDisplayString = KeyText(t.Keys);
                flyout.Items.Add(item);
            }
            flyoutGroup = g;
            flyoutByKey = select;
            flyout.Closed += delegate { flyoutGroup = null; palette.Invalidate(); };
            flyout.Show(palette, new Point(button.Bounds.Right, button.Bounds.Top));
            if (select && flyout.Items.Count > 0) flyout.Items[0].Select();
            palette.Invalidate();
        }

        Bitmap ToolImage(ToolIcon icon, int size)
        {
            var image = new Bitmap(size, size);
            using (var g = Graphics.FromImage(image)) Brand.DrawToolIcon(g, icon, new RectangleF(0, 0, size, size), Theme.Of(Role.Accent));
            return image;
        }

        // The corner triangle: Text at 75 % normally, Accent while its flyout is open or under the mouse.
        void PaintFlyoutMark(ToolStripButton button, Graphics g)
        {
            var group = (ToolGroup)button.Tag;
            if (!group.HasFlyout) return;
            var color = group == flyoutGroup || button.Selected ? Theme.Of(Role.Accent) : Color.FromArgb(191, Theme.Of(Role.Text));
            Brand.DrawFlyoutMark(g, new Rectangle(0, 0, button.Width, button.Height), color, DeviceDpi / 96f);
        }

        // The group's button after a tool was used: the tool's icon, the tooltip for it.
        void UpdatePaletteButton(ToolGroup g)
        {
            if (palette == null) return;
            var button = palette.Items.OfType<ToolStripButton>().FirstOrDefault(b => b.Tag == g);
            if (button == null) return;
            var old = button.Image;
            button.Image = ToolImage(g.Current.Icon, S(24));
            if (old != null) old.Dispose();
            button.ToolTipText = showToolNames ? g.Tip : g.Name + "\n" + g.Tip;
        }

        // The active tool (ToolChanged): its button checked (the arrow when none), its hint in the status bar.
        void ShowActiveTool()
        {
            var t = ActiveTool;
            if (palette != null)
                foreach (ToolStripButton b in palette.Items)
                    b.Checked = b == arrowButton ? t == null : t != null && b.Tag == t.Group;
            UpdateToolHint();
        }

        // The status bar's hint, left (DrawStatusChanged, ToolChanged): while dragging the size or the line's length and
        // angle (or the font size), while a drawn text gets its first text "Text eingeben · Esc = fertig", otherwise the
        // tool and its keys; none with the arrow.
        void UpdateToolHint()
        {
            if (statusBar == null) return;
            if (toolStatus == null)
            {
                toolStatus = new ToolStripStatusLabel { Visible = false, TextAlign = ContentAlignment.MiddleLeft };
                toolStatusLine = new ToolStripSeparator { Visible = false };
                statusBar.Items.Insert(0, toolStatus);
                statusBar.Items.Insert(1, toolStatusLine);
            }
            var t = ActiveTool;
            var m = Measure;
            string text = null;
            if (m != null && m.Tool != null) text = DragHint(m);
            else if (t != null && TypingDrawnText)
            {
                text = t.Name + ": " + Lang.T("type the text") + "  ·  " + Lang.T("Esc = done");
                // the hint says it: the label's summary instead of "Editing text – Esc ..." beside it
                if (labelParts.Length > 0) labelStatus.Text = string.Join("  ·  ", labelParts);
            }
            else if (t != null) text = ToolHint(t);
            toolStatus.Text = text ?? "";
            toolStatus.Visible = toolStatusLine.Visible = text != null;
            FitLabelStatus();
        }

        // "Linie: 42,0 mm, 25° · Esc = abbrechen", "Rechteck: 30,0 × 12,5 mm · ...", "Grafiktext: 24 pt · ...".
        internal static string DragHint(DrawMeasure m)
        {
            string size;
            switch (m.Tool.Id)
            {
                case "line": size = One(m.Length) + " mm, " + Math.Round(m.Angle).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + "°"; break;
                case "artistic": size = (Math.Round(m.FontSize * 2) / 2).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " pt"; break;
                default: size = One(m.W) + " × " + One(m.H) + " mm"; break;
            }
            return m.Tool.Name + ": " + size + "  ·  " + Lang.T("Esc = cancel");
        }

        static string One(double mm) { return mm.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture); }

        // "Linie: zum Zeichnen ziehen oder klicken · Umschalt = gerade · Esc = Auswahl".
        internal static string ToolHint(EditorTool t)
        {
            string how, shift = null;
            switch (t.Id)
            {
                case "artistic": how = Lang.T("click and type, or drag for the font size"); break;
                case "paragraph": how = Lang.T("drag the frame, or click"); break;
                case "picture": case "barcode": case "qr": how = Lang.T("drag the area, or click"); break;
                default: how = Lang.T("drag to draw, or click"); break;
            }
            switch (t.Id)
            {
                case "rectangle": shift = Lang.T("Shift = square"); break;
                case "ellipse": shift = Lang.T("Shift = circle"); break;
                case "triangle": case "polygon": case "star": shift = Lang.T("Shift = as wide as high"); break;
                case "line": shift = Lang.T("Shift = straight"); break;
            }
            return t.Name + ": " + how + (shift != null ? "  ·  " + shift : "") + "  ·  " + Lang.T("Esc = select");
        }

        // Icons at the present DPI. With names: as wide as the longest name (German at 150 % too), icon and name centred;
        // without: a 24 px icon in a 38 × 38 button, the palette 44 wide (at 100 %).
        void SizePalette()
        {
            int size = S(24), w = S(56), h = 0;
            palette.ImageScalingSize = new Size(size, size);
            palette.Padding = showToolNames ? new Padding(S(4), S(6), S(4), S(6)) : new Padding(S(3), S(6), S(3), S(6));
            if (arrowButton.Image != null) arrowButton.Image.Dispose();
            arrowButton.Image = ToolImage(ToolIcon.Select, size);
            arrowButton.ToolTipText = showToolNames ? Lang.T("Select, move and change objects (Esc)")
                                                    : Lang.T("Select") + "\n" + Lang.T("Select, move and change objects (Esc)");
            foreach (ToolStripButton item in palette.Items)
            {
                var g = item.Tag as ToolGroup;
                if (g != null) UpdatePaletteButton(g);
                item.DisplayStyle = showToolNames ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image;
                if (!showToolNames) continue;
                var text = TextRenderer.MeasureText(item.Text, item.Font);
                w = Math.Max(w, text.Width + S(16));
                h = Math.Max(h, size + text.Height + S(16));
            }
            if (!showToolNames) w = h = S(38);
            foreach (ToolStripItem item in palette.Items)
            {
                item.Size = new Size(w, h);
                item.Margin = new Padding(0, S(2), 0, S(2));
            }
            paletteButton = new Size(w, h);
            FitPaletteColumns();
            if (arrangeBar != null) SizeArrangeBar();
        }

        Size paletteButton;

        // As many columns as the height needs (one with five groups, even in the smallest window at 150 %).
        void FitPaletteColumns()
        {
            if (palette == null || paletteButton.Height == 0) return;
            int n = palette.Items.Count, step = paletteButton.Height + S(4);
            int free = Math.Max(step, palette.ClientSize.Height - palette.Padding.Vertical);
            int perColumn = Math.Max(1, free / step), columns = (n + perColumn - 1) / perColumn;
            int width = columns * paletteButton.Width + palette.Padding.Horizontal + (showToolNames ? S(2) : 0);
            if (palette.Width != width) palette.Width = width;
        }
    }
}
