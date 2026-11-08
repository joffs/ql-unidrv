// Label editor window (EditorForm.cs), part: the insert tools on the left, one button per tool group (ToolRegistry.cs).
// A group with more tools has a small triangle in its corner: a click inserts the group's last tool, a click on the
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

        // The palette: a vertical strip that wraps into columns (Flow, top down).
        sealed class PaletteStrip : ToolStrip
        {
            public Action<ToolStripButton> OpenFlyout;

            // Right arrow on a focused group: its flyout (the flyout opens to the right).
            protected override bool ProcessDialogKey(Keys keyData)
            {
                var button = Items.OfType<ToolStripButton>().FirstOrDefault(b => b.Selected);
                if (keyData == Keys.Right && button != null && ((ToolGroup)button.Tag).HasFlyout) { OpenFlyout(button); return true; }
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
            foreach (var group in ToolGroups())
            {
                var g = group;
                var button = new ToolStripButton(g.Name)
                {
                    Tag = g, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
                    ImageScaling = ToolStripItemImageScaling.None, ImageAlign = ContentAlignment.MiddleCenter, TextAlign = ContentAlignment.MiddleCenter,
                    AutoSize = false, AccessibleName = g.Name
                };
                button.MouseDown += (s, e) => PressGroup(button, e);
                button.MouseUp += delegate { holdTimer.Stop(); };
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
            bool corner = cornerPressed, held = flyoutByHold;
            cornerPressed = flyoutByHold = false;
            holdButton = null;
            if (held) return;   // the flyout is open already
            if (corner) { ShowFlyout(button, false); return; }
            g.Current.Insert();
        }

        // The group's tools to the right of its button: icon, name, key; the explanation in the tooltip; ✓ on the tool
        // the button shows. select: the first entry selected (keyboard).
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
                var item = new ToolStripMenuItem(t.Name, ToolImage(t.Icon, size), delegate { t.Insert(); })
                {
                    Checked = t == g.Current, ImageScaling = ToolStripItemImageScaling.None,
                    ToolTipText = t.Name + (t.Keys != Keys.None ? " (" + KeyText(t.Keys) + ")" : "") + (t.Explain != null ? "\n" + t.Explain : "")
                };
                if (t.Keys != Keys.None) item.ShortcutKeyDisplayString = KeyText(t.Keys);
                flyout.Items.Add(item);
            }
            flyoutGroup = g;
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

        // Icons at the present DPI. With names: as wide as the longest name (German at 150 % too), icon and name centred;
        // without: a 24 px icon in a 38 × 38 button, the palette 44 wide (at 100 %).
        void SizePalette()
        {
            int size = S(24), w = S(56), h = 0;
            palette.ImageScalingSize = new Size(size, size);
            palette.Padding = showToolNames ? new Padding(S(4), S(6), S(4), S(6)) : new Padding(S(3), S(6), S(3), S(6));
            foreach (ToolStripButton item in palette.Items)
            {
                var g = (ToolGroup)item.Tag;
                UpdatePaletteButton(g);
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
