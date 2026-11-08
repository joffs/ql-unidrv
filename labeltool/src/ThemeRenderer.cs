// The tool strip renderer of all programs (refactor S7, moved unchanged from Theme.cs).
using System;
using System.Drawing;
using System.Windows.Forms;

namespace QlLabels
{
    // Menus, context menus, toolbars and status bars: Windows' look in light (the base renderer's own colours), ours in
    // dark. Shared by all programs (ToolStripManager.Renderer); MainForm's command row derives from it.
    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { }

        // Every toolbar, menu and status bar drawn by it: its item tips in the scheme (Theme.Tip).
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Theme.TipOf(e.ToolStrip);
            base.OnRenderToolStripBackground(e);
        }

        // High Contrast: a checked button (e.g. "Fit") in Highlight with HighlightText; Windows' renderer gives it only a frame
        // around text in HighlightText, which nearly vanishes on the dark ground of Night sky and similar schemes.
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;
            if (Theme.HighContrast && button != null && button.Checked)
            {
                using (var fill = new SolidBrush(Theme.Of(Role.Selection))) e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
                return;
            }
            base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;
            if (Theme.HighContrast && button != null && button.Checked && button.Enabled) e.TextColor = Theme.Of(Role.OnPrimary);
            if (Theme.Dark)
            {
                var t = e.TextColor;
                if (!e.Item.Enabled) e.TextColor = Theme.Of(Role.Dim);
                else if (t == SystemColors.ControlText || t == SystemColors.MenuText || t == SystemColors.WindowText || t == Theme.Fixed.Ink)
                    e.TextColor = Theme.Of(Role.Text);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (Theme.Dark) e.ArrowColor = e.Item != null && !e.Item.Enabled ? Theme.Of(Role.Dim) : Theme.Of(Role.Text);
            base.OnRenderArrow(e);
        }

        // The tick of a checked menu entry: Windows' black tick would vanish on dark.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!Theme.Dark) { base.OnRenderItemCheck(e); return; }
            var r = e.ImageRectangle;
            using (var back = new SolidBrush(Theme.Of(Role.Selection))) e.Graphics.FillRectangle(back, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            var mode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Of(Role.Text), Math.Max(1.5f, r.Height / 8f)))
                e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + r.Width * .2f, r.Top + r.Height * .52f), new PointF(r.Left + r.Width * .42f, r.Top + r.Height * .74f),
                                                  new PointF(r.Left + r.Width * .8f, r.Top + r.Height * .28f) });
            e.Graphics.SmoothingMode = mode;
        }
    }

    // The renderer's colours: the base table in light (as before), the roles in dark and High Contrast stays Windows'.
    sealed class ThemeColors : ProfessionalColorTable
    {
        static bool Ours { get { return Theme.Dark; } }
        static Color R(Role role) { return Theme.Of(role); }

        public override Color MenuStripGradientBegin { get { return Ours ? R(Role.Window) : base.MenuStripGradientBegin; } }
        public override Color MenuStripGradientEnd { get { return Ours ? R(Role.Window) : base.MenuStripGradientEnd; } }
        public override Color ToolStripGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripGradientBegin; } }
        public override Color ToolStripGradientMiddle { get { return Ours ? R(Role.Window) : base.ToolStripGradientMiddle; } }
        public override Color ToolStripGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripGradientEnd; } }
        public override Color ToolStripBorder { get { return Ours ? R(Role.Window) : base.ToolStripBorder; } }
        public override Color ToolStripContentPanelGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripContentPanelGradientBegin; } }
        public override Color ToolStripContentPanelGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripContentPanelGradientEnd; } }
        public override Color ToolStripPanelGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripPanelGradientBegin; } }
        public override Color ToolStripPanelGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripPanelGradientEnd; } }
        public override Color OverflowButtonGradientBegin { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientBegin; } }
        public override Color OverflowButtonGradientMiddle { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientMiddle; } }
        public override Color OverflowButtonGradientEnd { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientEnd; } }
        public override Color StatusStripGradientBegin { get { return Ours ? R(Role.Surface) : base.StatusStripGradientBegin; } }
        public override Color StatusStripGradientEnd { get { return Ours ? R(Role.Surface) : base.StatusStripGradientEnd; } }
        public override Color ToolStripDropDownBackground { get { return Ours ? R(Role.Surface) : base.ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientBegin { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientBegin; } }
        public override Color ImageMarginGradientMiddle { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientMiddle; } }
        public override Color ImageMarginGradientEnd { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientEnd; } }
        public override Color MenuBorder { get { return Ours ? R(Role.Border) : base.MenuBorder; } }
        public override Color MenuItemBorder { get { return Ours ? R(Role.Hover) : base.MenuItemBorder; } }
        public override Color MenuItemSelected { get { return Ours ? R(Role.Hover) : base.MenuItemSelected; } }
        public override Color MenuItemSelectedGradientBegin { get { return Ours ? R(Role.Hover) : base.MenuItemSelectedGradientBegin; } }
        public override Color MenuItemSelectedGradientEnd { get { return Ours ? R(Role.Hover) : base.MenuItemSelectedGradientEnd; } }
        public override Color MenuItemPressedGradientBegin { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientBegin; } }
        public override Color MenuItemPressedGradientMiddle { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientMiddle; } }
        public override Color MenuItemPressedGradientEnd { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientEnd; } }
        public override Color SeparatorDark { get { return Ours ? R(Role.Border) : base.SeparatorDark; } }
        public override Color SeparatorLight { get { return Ours ? R(Role.Surface) : base.SeparatorLight; } }
        public override Color ButtonSelectedHighlight { get { return Ours ? R(Role.Hover) : base.ButtonSelectedHighlight; } }
        public override Color ButtonSelectedGradientBegin { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientBegin; } }
        public override Color ButtonSelectedGradientMiddle { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientMiddle; } }
        public override Color ButtonSelectedGradientEnd { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientEnd; } }
        public override Color ButtonSelectedBorder { get { return Ours ? R(Role.Accent) : base.ButtonSelectedBorder; } }
        public override Color ButtonPressedHighlight { get { return Ours ? R(Role.Selection) : base.ButtonPressedHighlight; } }
        public override Color ButtonPressedGradientBegin { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientBegin; } }
        public override Color ButtonPressedGradientMiddle { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientMiddle; } }
        public override Color ButtonPressedGradientEnd { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientEnd; } }
        public override Color ButtonPressedBorder { get { return Ours ? R(Role.Accent) : base.ButtonPressedBorder; } }
        public override Color ButtonCheckedHighlight { get { return Ours ? R(Role.Selection) : base.ButtonCheckedHighlight; } }
        public override Color ButtonCheckedGradientBegin { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientBegin; } }
        public override Color ButtonCheckedGradientMiddle { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientMiddle; } }
        public override Color ButtonCheckedGradientEnd { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientEnd; } }
        public override Color CheckBackground { get { return Ours ? R(Role.Selection) : base.CheckBackground; } }
        public override Color CheckSelectedBackground { get { return Ours ? R(Role.Selection) : base.CheckSelectedBackground; } }
        public override Color CheckPressedBackground { get { return Ours ? R(Role.Selection) : base.CheckPressedBackground; } }
        public override Color GripDark { get { return Ours ? R(Role.Border) : base.GripDark; } }
        public override Color GripLight { get { return Ours ? R(Role.Window) : base.GripLight; } }
    }
}
