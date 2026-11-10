// QL Label Maker's tab strip (0.23, mockup out/mockups/label-tabs, approved 2026-11-09): one
// tab per open label, directly above the label as in CorelDRAW, always visible. A tab shows its title, "*" while the
// label is unsaved and an x on the active tab and under the mouse; "+" at the end. Too many tabs or long titles: the
// tabs shrink to a minimum width, the rest goes into the list behind "N ▾" at the right (Visual Studio); the active tab
// always stays in the strip. Owner-drawn, colours from Theme roles only (light, dark, High Contrast), sizes in
// logical pixels (S()). The form owns the documents: it fills Tabs, sets Active (no event) and calls Refresh(); the
// strip only reports what the user did (TabSelected, TabCloseRequested, NewTabRequested, TabContextMenu). With the
// focus (TabStop is off unless the form turns it on), Left/Right/Home/End select like a click.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class DocTabStrip : Control
    {
        public sealed class Tab
        {
            public Tab() { Title = ""; }
            public string Title { get; set; }     // without "*": the strip adds it while Dirty
            public bool Dirty { get; set; }
            public string ToolTip { get; set; }   // several lines: path (or not saved yet), printer, format
            public object Tag { get; set; }       // the form's document
        }

        // ---- logical sizes (96 dpi), as in the mockup ----
        internal const int StripHeight = 28, TabHeight = 26, LeftGap = 4, PadLeft = 10, PadRight = 6, Gap = 5, XSize = 16,
                           MaxTab = 210, MinTab = 110, PlusWidth = 26, PlusGap = 6, ListGap = 4, Radius = 4;

        readonly List<Tab> tabs = new List<Tab>();
        readonly ToolTip tip = new ToolTip { ShowAlways = true, InitialDelay = 500, ReshowDelay = 100, AutoPopDelay = 15000 };
        readonly Timer blinkTimer = new Timer { Interval = 30 };
        readonly Stopwatch blinkClock = new Stopwatch();
        int active;
        Tab blinkTab;   // the tab itself, not its index: it keeps blinking when a tab left of it closes
        Font boldFont;
        DocTabLayout.Result layout;
        Rectangle plusRect, listRect;
        Rectangle[] tabRects = new Rectangle[0];
        // under the mouse / pressed: part = 0 tab, 1 its x, 2 plus, 3 list
        int hotIndex = -1, hotPart = -1, downIndex = -1, downPart = -1;
        MouseButtons downButton;
        string shownTip;
        ContextMenuStrip listMenu;

        public event Action<int> TabSelected;
        public event Action<int> TabCloseRequested;
        public event Action NewTabRequested;
        public event Action<int, Point> TabContextMenu;

        public DocTabStrip()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = false;
            Dock = DockStyle.Top;
            AccessibleRole = AccessibleRole.PageTabList;
            AccessibleName = Lang.T("Open labels");
            blinkTimer.Tick += delegate { BlinkStep(); };
        }

        public IList<Tab> Tabs { get { return tabs; } }

        // The tab of the label shown; set by the form, no event. It always stays in the strip.
        public int Active
        {
            get { return active; }
            set { active = value; Refresh(); }
        }

        // After the form changed Tabs or a tab's fields: measured and drawn again.
        public override void Refresh()
        {
            if (active >= tabs.Count) active = tabs.Count - 1;
            if (active < 0 && tabs.Count > 0) active = 0;
            if (blinkTab != null && !tabs.Contains(blinkTab)) StopBlink();
            Arrange();
            UpdateTip();
            base.Refresh();
        }

        // "Already open": the tab lights up twice (about 3 s); the status-bar text is the form's.
        public void Blink(int index)
        {
            if (index < 0 || index >= tabs.Count) return;
            blinkTab = tabs[index];
            blinkClock.Restart();
            blinkTimer.Start();
            Invalidate();
        }

        internal bool Blinking { get { return blinkTab != null; } }
        internal int BlinkingIndex { get { return blinkTab == null ? -1 : tabs.IndexOf(blinkTab); } }
        internal const int BlinkMs = 1600, BlinkPulses = 2;

        void BlinkStep()
        {
            if (blinkClock.ElapsedMilliseconds >= BlinkMs * BlinkPulses) StopBlink();
            Invalidate();
        }

        void StopBlink()
        {
            blinkTimer.Stop();
            blinkTab = null;
            Invalidate();
        }

        // 0..1: how much of the blink colour (each pulse: full for the first 30 %, then fading out).
        internal static double BlinkAmount(long ms)
        {
            if (ms < 0 || ms >= BlinkMs * BlinkPulses) return 0;
            double t = (ms % BlinkMs) / (double)BlinkMs;
            return t <= 0.3 ? 1 : 1 - (t - 0.3) / 0.7;
        }

        int S(int logical) { return LogicalToDeviceUnits(logical); }

        // ---- colours (Theme roles; the mockup's tab tones mixed from them) ----
        static Color Idle { get { return Theme.HighContrast ? Theme.Of(Role.Window) : Theme.Dark ? Theme.Mix(Theme.Of(Role.Window), Theme.Of(Role.Content), 0.5) : Theme.Mix(Theme.Of(Role.Window), Theme.Of(Role.Border), 0.35); } }
        static Color HoverBack { get { return Theme.HighContrast ? Theme.Of(Role.Window) : Theme.Mix(Theme.Of(Role.Window), Theme.Of(Role.Hover), 0.5); } }
        static Color Line { get { return Theme.HighContrast ? Theme.Of(Role.Text) : Theme.Of(Role.Border); } }
        static Color XHover { get { return Theme.HighContrast ? Theme.Of(Role.Hover) : Theme.Dark ? Theme.Of(Role.Hover) : Theme.Mix(Theme.Of(Role.Content), Theme.Of(Role.Border), 0.5); } }
        static Color FlashColor { get { return Theme.HighContrast ? Theme.Of(Role.Selection) : Theme.Of(Role.NoteLine); } }

        // ---- size and layout ----
        // The tab's height: 26 logical px as in the mockup, taller when Windows' text size makes the font taller (the
        // title keeps about 5 px above and below); the strip 2 px more, the + and the list 2 px less.
        internal int TabH { get { return Math.Max(S(TabHeight), Font.Height + S(10)); } }
        int StripH { get { return TabH + S(StripHeight - TabHeight); } }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Height = StripH; Arrange(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); Height = StripH; Arrange(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e)
        {
            if (boldFont != null) { boldFont.Dispose(); boldFont = null; }
            base.OnFontChanged(e);
            Height = StripH;
            Arrange();
            Invalidate();
        }
        protected override void OnResize(EventArgs e) { base.OnResize(e); Arrange(); }
        protected override Size DefaultSize { get { return new Size(400, StripHeight); } }

        Font Bold { get { return boldFont ?? (boldFont = new Font(Font, FontStyle.Bold)); } }

        string Shown(Tab t) { return t.Title ?? ""; }

        // The width a tab wants: padding, the title (bold, so it doesn't jump when it becomes active), "*", the x.
        int Natural(Tab t)
        {
            int text = TextRenderer.MeasureText(Shown(t), Bold, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            int star = t.Dirty ? TextRenderer.MeasureText("*", Bold, Size.Empty, TextFormatFlags.NoPadding).Width + S(2) : 0;
            return S(PadLeft) + text + star + S(Gap) + S(XSize) + S(PadRight);
        }

        string ListText { get { return tabs.Count + " ▾"; } }
        int ListWidth { get { return Math.Max(S(26), TextRenderer.MeasureText(ListText, Bold, Size.Empty, TextFormatFlags.NoPadding).Width + S(12)); } }

        void Arrange()
        {
            if (!IsHandleCreated && Width <= 0) return;
            var natural = tabs.Select(Natural).ToArray();
            int avail = Width - S(LeftGap) - S(PlusWidth) - S(PlusGap) - S(2);
            layout = DocTabLayout.Compute(natural, active, avail, S(MaxTab), S(MinTab), ListWidth + S(ListGap));
            tabRects = new Rectangle[tabs.Count];
            int x = S(LeftGap), h = Height;
            foreach (int i in layout.Visible)
            {
                bool a = i == active;
                int top = a ? h - TabH - S(1) : h - TabH - S(1) + S(1);
                tabRects[i] = new Rectangle(x, top, layout.Widths[i], h - top);
                x += layout.Widths[i] - S(1);
            }
            plusRect = new Rectangle(x + S(1) + S(PlusGap), h - TabH, S(PlusWidth), TabH - S(2));
            listRect = layout.Overflow ? new Rectangle(Width - S(ListGap) - ListWidth, h - TabH, ListWidth, TabH - S(2)) : Rectangle.Empty;
        }

        internal Rectangle TabRect(int i) { return i >= 0 && i < tabRects.Length ? tabRects[i] : Rectangle.Empty; }
        internal Rectangle XRect(int i)
        {
            var r = TabRect(i);
            if (r.IsEmpty) return r;
            int size = S(XSize);
            return new Rectangle(r.Right - S(PadRight) - size, r.Top + (r.Height - size) / 2 - (i == active ? 0 : S(1)) + S(1), size, size);
        }
        internal Rectangle PlusRect { get { return plusRect; } }
        internal Rectangle ListRect { get { return listRect; } }
        internal DocTabLayout.Result TabLayout { get { return layout; } }

        // ---- painting ----
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Of(Role.Window));
            int h = Height;
            if (layout == null) Arrange();
            // the others first, then the line (they stand on it, as in the mockup), the active one on top (it covers
            // the line under it and opens into the label)
            foreach (int i in layout.Visible.Where(i => i != active)) DrawTab(g, i);
            using (var p = new Pen(Line)) g.DrawLine(p, 0, h - 1, Width, h - 1);
            if (active >= 0 && active < tabs.Count && layout.Visible.Contains(active)) DrawTab(g, active);
            DrawPlus(g);
            if (layout.Overflow) DrawList(g);
        }

        void DrawTab(Graphics g, int i)
        {
            var t = tabs[i];
            var r = tabRects[i];
            bool a = i == active, hot = i == hotIndex;
            Color back = a ? Theme.Of(Role.Content) : hot ? HoverBack : Idle;
            if (t == blinkTab) back = Theme.Mix(back, FlashColor, BlinkAmount(blinkClock.ElapsedMilliseconds));
            Color fore = a || hot ? Theme.Of(Role.Text) : Theme.Of(Role.Dim);
            int rad = S(Radius);
            using (var path = TopRounded(new Rectangle(r.X, r.Y, r.Width - 1, r.Height + rad), rad))
            {
                var old = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var p = new Pen(Line)) g.DrawPath(p, path);
                g.SmoothingMode = old;
            }
            if (a)
            {
                // the accent line along the top, inside the frame and bent with its rounded corners (the mockup's inset
                // shadow), and no line at the bottom: the tab joins the label
                using (var inner = TopRounded(new Rectangle(r.X + 1, r.Y + 1, r.Width - 3, r.Height + rad), Math.Max(1, rad - 1)))
                using (var b = new SolidBrush(Theme.Of(Role.Accent)))
                {
                    var oldClip = g.Clip;
                    var oldMode = g.SmoothingMode;
                    var oldOffset = g.PixelOffsetMode;
                    g.SetClip(new Rectangle(r.X, r.Y + 1, r.Width, S(2)));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;   // whole pixels on the straight part
                    g.FillPath(b, inner);
                    g.PixelOffsetMode = oldOffset;
                    g.SmoothingMode = oldMode;
                    g.Clip = oldClip;
                    oldClip.Dispose();
                }
                using (var b = new SolidBrush(back)) g.FillRectangle(b, r.X + 1, Height - 1, r.Width - 3, 1);
            }
            var font = a ? Bold : Font;
            bool showX = a || hot;
            int textLeft = r.X + S(PadLeft);
            int right = r.Right - S(PadRight) - S(XSize) - S(Gap);
            int star = t.Dirty ? TextRenderer.MeasureText(g, "*", Bold, Size.Empty, TextFormatFlags.NoPadding).Width + S(2) : 0;
            var textRect = new Rectangle(textLeft, r.Y, Math.Max(0, right - textLeft - star), Height - r.Y - (a ? 0 : 1));
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, Shown(t), font, textRect, fore, flags);
            if (t.Dirty)
            {
                int w = TextRenderer.MeasureText(g, Shown(t), font, textRect.Size, flags).Width;
                var starRect = new Rectangle(textLeft + Math.Min(w, textRect.Width) + S(1), textRect.Y, star + S(2), textRect.Height);
                TextRenderer.DrawText(g, "*", Bold, starRect, Theme.Of(Role.Accent), flags);
            }
            if (showX)
            {
                var x = XRect(i);
                if (hotIndex == i && hotPart == 1)
                    using (var b = new SolidBrush(XHover)) g.FillRectangle(b, x);
                DrawCross(g, x, hotIndex == i && hotPart == 1 ? Theme.Of(Role.Text) : Theme.Of(Role.Dim));
            }
            if (a && Focused && ShowFocusCues)
            {
                var f = new Rectangle(r.X + S(3), r.Y + S(4), r.Width - S(7), r.Height - S(6));
                ControlPaint.DrawFocusRectangle(g, f, fore, back);
            }
        }

        void DrawCross(Graphics g, Rectangle box, Color c)
        {
            int m = S(5);
            var old = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var p = new Pen(c, Math.Max(1f, S(1) * 1.2f)))
            {
                g.DrawLine(p, box.Left + m, box.Top + m, box.Right - m - 1, box.Bottom - m - 1);
                g.DrawLine(p, box.Right - m - 1, box.Top + m, box.Left + m, box.Bottom - m - 1);
            }
            g.SmoothingMode = old;
        }

        void DrawPlus(Graphics g)
        {
            bool hot = hotPart == 2;
            if (hot) using (var b = new SolidBrush(HoverBack)) g.FillRectangle(b, plusRect);
            var c = hot ? Theme.Of(Role.Text) : Theme.Of(Role.Dim);
            int cx = plusRect.X + plusRect.Width / 2, cy = plusRect.Y + plusRect.Height / 2, arm = S(5);
            using (var p = new Pen(c, Math.Max(1f, S(1) * 1.3f)))
            {
                g.DrawLine(p, cx - arm, cy, cx + arm, cy);
                g.DrawLine(p, cx, cy - arm, cx, cy + arm);
            }
        }

        void DrawList(Graphics g)
        {
            bool open = listMenu != null && listMenu.Visible, hot = hotPart == 3 || open;
            if (hot)
            {
                using (var b = new SolidBrush(HoverBack)) g.FillRectangle(b, listRect);
                using (var p = new Pen(Line)) g.DrawRectangle(p, listRect.X, listRect.Y, listRect.Width - 1, listRect.Height - 1);
            }
            TextRenderer.DrawText(g, ListText, Bold, listRect, hot ? Theme.Of(Role.Text) : Theme.Of(Role.Dim),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        static System.Drawing.Drawing2D.GraphicsPath TopRounded(Rectangle r, int rad)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = rad * 2;
            p.AddLine(r.Left, r.Bottom, r.Left, r.Top + rad);
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddLine(r.Right, r.Top + rad, r.Right, r.Bottom);
            return p;
        }

        // ---- mouse ----
        // (index, part): part 0 tab, 1 its x, 2 plus, 3 list; (-1, -1) nothing.
        internal void HitTest(Point pt, out int index, out int part)
        {
            index = -1; part = -1;
            if (plusRect.Contains(pt)) { part = 2; return; }
            if (layout != null && layout.Overflow && listRect.Contains(pt)) { part = 3; return; }
            if (layout == null) return;
            // the active tab is drawn on top: tested first
            foreach (int i in new[] { active }.Concat(layout.Visible.Where(v => v != active)))
            {
                if (i < 0 || i >= tabRects.Length || !tabRects[i].Contains(pt)) continue;
                index = i;
                part = XRect(i).Contains(pt) ? 1 : 0;   // the x shows under the mouse, so it counts on every tab
                return;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i, part;
            HitTest(e.Location, out i, out part);
            if (i != hotIndex || part != hotPart)
            {
                hotIndex = i;
                hotPart = part;
                Invalidate();
                UpdateTip();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotIndex = hotPart = -1;
            Invalidate();
            UpdateTip();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            HitTest(e.Location, out downIndex, out downPart);
            downButton = e.Button;
            if (e.Button == MouseButtons.Left && downPart == 0 && downIndex != active && TabSelected != null) TabSelected(downIndex);
            if (e.Button == MouseButtons.Left && downPart == 3) ShowList();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i, part;
            HitTest(e.Location, out i, out part);
            bool same = i == downIndex && part == downPart && e.Button == downButton;
            downIndex = downPart = -1;
            if (!same) return;
            if (e.Button == MouseButtons.Left && part == 1) { if (TabCloseRequested != null) TabCloseRequested(i); }
            else if (e.Button == MouseButtons.Left && part == 2) { if (NewTabRequested != null) NewTabRequested(); }
            else if (e.Button == MouseButtons.Middle && (part == 0 || part == 1)) { if (TabCloseRequested != null) TabCloseRequested(i); }
            else if (e.Button == MouseButtons.Right && (part == 0 || part == 1)) { if (TabContextMenu != null) TabContextMenu(i, PointToScreen(e.Location)); }
        }

        void UpdateTip()
        {
            string text = null;
            if (hotPart == 1) text = Lang.T("Close (Ctrl+W or middle mouse button)");
            else if (hotPart == 0 && hotIndex >= 0 && hotIndex < tabs.Count) text = tabs[hotIndex].ToolTip;
            else if (hotPart == 2) text = Lang.T("New label (Ctrl+N)");
            else if (hotPart == 3) text = Lang.F("All open labels ({0})", tabs.Count);
            if (text == shownTip) return;
            shownTip = text;
            tip.SetToolTip(this, text ?? "");
        }

        // ---- the list of all tabs (▾) ----
        internal ContextMenuStrip ShowList()
        {
            if (listMenu != null) { listMenu.Close(); listMenu.Dispose(); }
            var menu = listMenu = new ContextMenuStrip { Font = Font, ShowCheckMargin = true, ShowImageMargin = false };
            for (int i = 0; i < tabs.Count; i++)
            {
                int index = i;
                var item = new ToolStripMenuItem(Shown(tabs[i]).Replace("&", "&&") + (tabs[i].Dirty ? " *" : ""))
                {
                    Checked = i == active,
                    ShortcutKeyDisplayString = layout.Visible.Contains(i) ? null : Lang.T("not in the strip"),
                    ToolTipText = tabs[i].ToolTip,
                };
                item.Click += delegate { if (index != active && TabSelected != null) TabSelected(index); };
                menu.Items.Add(item);
            }
            Theme.Apply(menu);
            menu.Closed += delegate { Invalidate(); };
            menu.Show(this, new Point(listRect.Right, listRect.Bottom + S(2)), ToolStripDropDownDirection.BelowLeft);
            Invalidate();
            return menu;
        }

        // ---- keyboard ----
        protected override bool IsInputKey(Keys keyData)
        {
            var k = keyData & Keys.KeyCode;
            if ((keyData & (Keys.Control | Keys.Alt)) == 0 && (k == Keys.Left || k == Keys.Right || k == Keys.Home || k == Keys.End)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Modifiers != Keys.None || tabs.Count == 0) return;
            int to = -1;
            if (e.KeyCode == Keys.Left) to = Math.Max(0, active - 1);
            else if (e.KeyCode == Keys.Right) to = Math.Min(tabs.Count - 1, active + 1);
            else if (e.KeyCode == Keys.Home) to = 0;
            else if (e.KeyCode == Keys.End) to = tabs.Count - 1;
            if (to < 0) return;
            e.Handled = true;
            if (to != active && TabSelected != null) TabSelected(to);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ---- accessibility: one child per tab, the + and the list ----
        protected override AccessibleObject CreateAccessibilityInstance() { return new StripAccessible(this); }

        sealed class StripAccessible : ControlAccessibleObject
        {
            readonly DocTabStrip strip;
            public StripAccessible(DocTabStrip strip) : base(strip) { this.strip = strip; }
            public override AccessibleRole Role { get { return AccessibleRole.PageTabList; } }
            public override int GetChildCount() { return strip.tabs.Count + 1 + (strip.layout != null && strip.layout.Overflow ? 1 : 0); }
            public override AccessibleObject GetChild(int index)
            {
                if (index < strip.tabs.Count) return new Part(strip, this, index, 0);
                if (index == strip.tabs.Count) return new Part(strip, this, -1, 2);
                return index == strip.tabs.Count + 1 ? new Part(strip, this, -1, 3) : null;
            }
        }

        sealed class Part : AccessibleObject
        {
            readonly DocTabStrip strip;
            readonly AccessibleObject parent;
            readonly int index, part;
            public Part(DocTabStrip strip, AccessibleObject parent, int index, int part) { this.strip = strip; this.parent = parent; this.index = index; this.part = part; }
            public override AccessibleObject Parent { get { return parent; } }
            public override AccessibleRole Role { get { return part == 0 ? AccessibleRole.PageTab : part == 2 ? AccessibleRole.PushButton : AccessibleRole.ButtonDropDown; } }
            public override string Name
            {
                get
                {
                    if (part == 2) return Lang.T("New label (Ctrl+N)");
                    if (part == 3) return Lang.F("All open labels ({0})", strip.tabs.Count);
                    var t = strip.tabs[index];
                    return t.Title + (t.Dirty ? " " + Lang.T("(unsaved)") : "");
                }
            }
            public override string Description { get { return part == 0 ? strip.tabs[index].ToolTip : null; } }
            public override AccessibleStates State
            {
                get
                {
                    var s = AccessibleStates.Focusable | AccessibleStates.Selectable;
                    if (part == 0 && index == strip.active) s |= AccessibleStates.Selected | (strip.Focused ? AccessibleStates.Focused : 0);
                    if (part == 0 && !strip.layout.Visible.Contains(index)) s |= AccessibleStates.Invisible | AccessibleStates.Offscreen;
                    return s;
                }
            }
            public override Rectangle Bounds
            {
                get
                {
                    var r = part == 0 ? strip.TabRect(index) : part == 2 ? strip.plusRect : strip.listRect;
                    return r.IsEmpty ? Rectangle.Empty : strip.RectangleToScreen(r);
                }
            }
            public override string DefaultAction { get { return part == 0 ? Lang.T("Switch") : Lang.T("Press"); } }
            public override void DoDefaultAction()
            {
                if (part == 0 && index != strip.active && strip.TabSelected != null) strip.TabSelected(index);
                else if (part == 2 && strip.NewTabRequested != null) strip.NewTabRequested();
                else if (part == 3) strip.ShowList();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                blinkTimer.Dispose();
                tip.Dispose();
                if (boldFont != null) boldFont.Dispose();
                if (listMenu != null) listMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    // Which tabs fit and how wide (pure, for the unit test): every tab its natural width up to max; when they don't fit,
    // each shrinks to avail / count but not below min; then from the left as many as fit, leaving room for the list
    // button when tabs remain; the active tab, when it fell out, takes the place of the last ones (it always shows).
    static class DocTabLayout
    {
        internal sealed class Result
        {
            public int[] Widths;
            public List<int> Visible;   // tab indexes in the strip, left to right
            public bool Overflow;       // some tabs only in the list
        }

        internal static Result Compute(int[] natural, int active, int avail, int max, int min, int listWidth)
        {
            int n = natural.Length;
            var w = natural.Select(x => Math.Min(x, max)).ToArray();
            if (w.Sum() > avail && n > 0)
            {
                int cap = Math.Max(min, avail / n);
                for (int i = 0; i < n; i++) w[i] = Math.Min(w[i], cap);
            }
            var visible = new List<int>();
            int used = 0;
            bool over = false;
            // all of them fit: all shown (the list's room is only kept while tabs are left; review 0.23)
            if (n > 0 && w.Sum() <= avail) return new Result { Widths = w, Visible = Enumerable.Range(0, n).ToList(), Overflow = false };
            for (int i = 0; i < n; i++)
            {
                if (used + w[i] > avail - (i < n - 1 ? listWidth : 0)) { over = true; break; }
                visible.Add(i);
                used += w[i];
            }
            if (over && active >= 0 && active < n && !visible.Contains(active))
            {
                while (visible.Count > 0 && used + w[active] > avail - listWidth) { used -= w[visible[visible.Count - 1]]; visible.RemoveAt(visible.Count - 1); }
                visible.Add(active);
                used += w[active];
            }
            // too narrow even for the active one alone: it still shows (cut off), nothing else
            if (n > 0 && visible.Count == 0) { visible.Add(Math.Max(0, Math.Min(active, n - 1))); over = n > 1; }
            return new Result { Widths = w, Visible = visible, Overflow = over };
        }
    }
}
