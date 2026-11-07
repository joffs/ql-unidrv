// A row of tabs in the app's own colours (Printer setup, 2026-11-06): the chosen one in the heading colour with a bar
// below it, a hairline under all. WinForms' TabControl draws Windows' light tabs also in dark mode and High Contrast.
// Click, Left/Right/Home/End while it has the focus, Ctrl+Tab / Ctrl+Shift+Tab from the window (SetupForm).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class TabBar : Control
    {
        readonly List<string> titles = new List<string>();
        int selected;

        public event EventHandler SelectedChanged;

        public TabBar()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            AccessibleRole = AccessibleRole.PageTabList;
        }

        public void Add(string title) { titles.Add(title); Invalidate(); }

        public int Count { get { return titles.Count; } }

        public int Selected
        {
            get { return selected; }
            set
            {
                int v = Math.Max(0, Math.Min(titles.Count - 1, value));
                if (v == selected) return;
                selected = v;
                AccessibleName = titles.Count > 0 ? titles[v] : null;
                Invalidate();
                if (SelectedChanged != null) SelectedChanged(this, EventArgs.Empty);
            }
        }

        int Pad { get { return Font.Height * 2 / 3; } }

        // Each tab: its text plus padding on both sides; the first starts on the window's left line
        List<Rectangle> Tabs()
        {
            var list = new List<Rectangle>();
            int x = 0, pad = Pad;
            for (int i = 0; i < titles.Count; i++)
            {
                int w = TextRenderer.MeasureText(titles[i], BoldFont).Width + (i == 0 ? pad : pad * 2);
                list.Add(new Rectangle(x, 0, w, Height));
                x += w;
            }
            return list;
        }

        Font bold;
        Font BoldFont { get { if (bold == null || bold.Size != Font.Size) { if (bold != null) bold.Dispose(); bold = new Font(Font, FontStyle.Bold); } return bold; } }

        public override Size GetPreferredSize(Size proposed)
        {
            var tabs = Tabs();
            int w = tabs.Count == 0 ? 0 : tabs[tabs.Count - 1].Right;
            return new Size(w, Font.Height + Pad + 4);
        }

        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Height = GetPreferredSize(Size.Empty).Height; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Of(Role.Window));
            int line = Math.Max(1, Font.Height / 16), bar = Math.Max(2, Font.Height / 7);
            using (var b = new SolidBrush(Theme.Of(Role.SurfaceLine))) g.FillRectangle(b, 0, Height - line, Width, line);
            var tabs = Tabs();
            for (int i = 0; i < tabs.Count; i++)
            {
                var r = tabs[i];
                bool on = i == selected;
                var text = new Rectangle(r.X + (i == 0 ? 0 : Pad), 0, r.Width - (i == 0 ? Pad : Pad * 2), Height - bar);
                // Bold for all would keep the widths; the chosen one alone is bold, measured bold so nothing moves
                TextRenderer.DrawText(g, titles[i], on ? BoldFont : Font, text, Theme.Of(on ? Role.Heading : Role.Text),
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                if (on)
                    using (var b = new SolidBrush(Theme.Of(Role.Heading))) g.FillRectangle(b, text.X, Height - bar, text.Width, bar);
                if (on && Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(text, 2, -1), Theme.Of(Role.Text), Theme.Of(Role.Window));
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var tabs = Tabs();
            for (int i = 0; i < tabs.Count; i++) if (tabs[i].Contains(e.Location)) { Selected = i; break; }
            Focus();
        }

        protected override bool IsInputKey(Keys key)
        {
            return key == Keys.Left || key == Keys.Right || key == Keys.Home || key == Keys.End || base.IsInputKey(key);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left) Selected = selected - 1;
            else if (e.KeyCode == Keys.Right) Selected = selected + 1;
            else if (e.KeyCode == Keys.Home) Selected = 0;
            else if (e.KeyCode == Keys.End) Selected = titles.Count - 1;
            else return;
            e.Handled = true;
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && bold != null) bold.Dispose();
            base.Dispose(disposing);
        }
    }
}
