// The top of "Print with data" (PrintDataDialog.cs; mockup variant A "Kacheln", 2026-11-09): three tiles side by
// side (printer, template, data) and one line of field chips under them.
// HeadTile: a caption, a name in bold and a line below (with a state circle), a coloured bar on the left: petrol when all
// is well, amber when something needs doing (the tile tinted), grey when unknown. The printer tile opens the list of
// printers (click, Enter, Space, F4, Alt+Down; Up/Down choose the one before or after); the data tile without a file is
// one large dashed button ("+ Choose data file..."). High Contrast: frames in the window's text colour, no tint; the
// state is in the circle's symbol.
// ChipRow: the fields as chips in one line (missing ones first, then found ones, the special columns, the columns not on
// the label); what doesn't fit goes behind "+N more", which lists them all.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    enum TileState { Neutral, Ok, Warn, Unknown }
    enum TileIcon { Printer, Label, Table }

    sealed class HeadTile : Control
    {
        string caption = "", main = "", sub = "";
        TileState state;
        Health? dot;
        bool hot;

        public TileIcon Icon;
        public bool Chevron;       // a list opens (the printer)
        public bool CallToAction;  // the whole tile is a button (the data tile without a file)
        public Control Side;       // a button at the right (the data tile's "Change...")
        public event EventHandler Activated;   // click, Enter, Space (F4 and Alt+Down with Chevron)
        public event EventHandler<int> Step;   // Up (-1) / Down (+1) with Chevron

        public HeadTile()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.Grouping;
            EventHandler changed = delegate { Invalidate(); };
            Theme.Changed += changed;
            Disposed += delegate { Theme.Changed -= changed; };
        }

        // Clickable tiles take the focus (Tab) and answer the keys.
        public bool Clickable
        {
            get { return GetStyle(ControlStyles.Selectable); }
            set
            {
                SetStyle(ControlStyles.Selectable, value);
                TabStop = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                AccessibleRole = value ? (Chevron ? AccessibleRole.ComboBox : AccessibleRole.PushButton) : AccessibleRole.Grouping;
                Invalidate();
            }
        }

        public void Show(string caption, string main, string sub, TileState state, Health? dot)
        {
            this.caption = caption ?? "";
            this.main = main ?? "";
            this.sub = sub ?? "";
            this.state = state;
            this.dot = dot;
            AccessibleName = this.caption + ": " + this.main;
            AccessibleDescription = this.sub;
            Invalidate();
        }

        public string Caption { get { return caption; } }
        public string Main { get { return main; } }
        public string Sub { get { return sub; } }
        public TileState State { get { return state; } }
        public Health? Dot { get { return dot; } }

        Font small, bold;
        Font Small { get { return small ?? (small = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f, FontStyle.Regular, GraphicsUnit.Point)); } }
        Font Bold { get { return bold ?? (bold = new Font(Font, FontStyle.Bold)); } }
        protected override void OnFontChanged(EventArgs e)
        {
            if (small != null) small.Dispose();
            if (bold != null) bold.Dispose();
            small = bold = null;
            base.OnFontChanged(e);
        }

        // The height of three lines of text and the padding (the window's font at its scaling).
        public int PreferredTileHeight
        {
            get
            {
                int pad = LogicalToDeviceUnits(4);
                return pad * 2 + Small.Height + Bold.Height + Font.Height - LogicalToDeviceUnits(2);
            }
        }

        int Pad { get { return LogicalToDeviceUnits(8); } }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (Side != null && Side.Parent == this)
                Side.Location = new Point(Width - Pad - Side.Width, (Height - Side.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool hc = Theme.HighContrast;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Of(Role.Window));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float radius = LogicalToDeviceUnits(5), bar = LogicalToDeviceUnits(3);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color text = Enabled ? Theme.Of(Role.Text) : Theme.Of(Role.Dim);
            Color dim = Theme.Of(Role.Dim);

            if (CallToAction)
            {
                using (var path = Rounded(r, radius))
                using (var pen = new Pen(hot || Focused ? Theme.Of(Role.AccentHot) : Theme.Of(Role.Accent), Math.Max(1.5f, LogicalToDeviceUnits(1) * 1.5f)) { DashStyle = DashStyle.Dash })
                {
                    if (hot) using (var b = new SolidBrush(Theme.Of(Role.Hover))) g.FillPath(b, path);
                    g.DrawPath(pen, path);
                }
                var all = new Rectangle(Pad, 0, Width - 2 * Pad, Height);
                int h1 = Bold.Height, h2 = Font.Height, top = (Height - h1 - h2) / 2;
                TextRenderer.DrawText(g, main, Bold, new Rectangle(all.X, top, all.Width, h1), Enabled ? Theme.Of(Role.Accent) : dim,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(g, sub, Font, new Rectangle(all.X, top + h1, all.Width, h2), dim,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
                return;
            }

            // face, frame, the bar on the left
            Color face = hc ? Theme.Of(Role.Window) : state == TileState.Warn ? Theme.Of(Role.NoteBack) : Theme.Of(Role.Surface);
            Color line = hc ? Theme.Of(Role.Text) : hot ? Theme.Of(Role.Accent) : state == TileState.Warn ? Theme.Of(Role.NoteLine) : Theme.Of(Role.Border);
            Color barColor = hc ? Theme.Of(Role.Text)
                           : state == TileState.Ok ? Theme.Fixed.StateOk : state == TileState.Warn ? Theme.Fixed.StateWarning
                           : state == TileState.Unknown ? Theme.Fixed.StateUnknown : Theme.Of(Role.Border);
            using (var path = Rounded(r, radius))
            {
                using (var b = new SolidBrush(face)) g.FillPath(b, path);
                float w = hc && state == TileState.Warn ? LogicalToDeviceUnits(2) : 1f;
                using (var pen = new Pen(line, w)) g.DrawPath(pen, path);
                if (!hc || state != TileState.Neutral)
                {
                    var clip = g.Clip;
                    g.SetClip(path);
                    using (var b = new SolidBrush(barColor)) g.FillRectangle(b, 0, 0, bar, Height);
                    g.Clip = clip;
                }
            }

            int x = (int)bar + Pad;
            int icon = LogicalToDeviceUnits(16);
            DrawIcon(g, new RectangleF(x, (Height - icon) / 2f, icon, icon), dim);
            x += icon + Pad;
            int right = Width - Pad;
            if (Side != null && Side.Visible) right = Side.Left - Pad;
            if (Chevron)
            {
                int c = LogicalToDeviceUnits(12);
                right -= c;
                using (var pen = new Pen(dim, Math.Max(1f, LogicalToDeviceUnits(1) * 1.3f)))
                {
                    float cx = right + c / 2f, cy = Height / 2f, s = c / 3f;
                    g.DrawLines(pen, new[] { new PointF(cx - s, cy - s / 2), new PointF(cx, cy + s / 2), new PointF(cx + s, cy - s / 2) });
                }
                right -= LogicalToDeviceUnits(4);
            }
            int width = Math.Max(0, right - x);
            int top2 = (Height - (Small.Height + Bold.Height + Font.Height - LogicalToDeviceUnits(2))) / 2;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, caption, Small, new Rectangle(x, top2, width, Small.Height), dim, flags);
            int y = top2 + Small.Height - LogicalToDeviceUnits(1);
            TextRenderer.DrawText(g, main, Bold, new Rectangle(x, y, width, Bold.Height), text, flags);
            y += Bold.Height - LogicalToDeviceUnits(1);
            int sx = x;
            if (dot != null)
            {
                int d = LogicalToDeviceUnits(12);
                DrawDot(g, new RectangleF(sx, y + (Font.Height - d) / 2f, d, d), dot.Value, hc);
                sx += d + LogicalToDeviceUnits(4);
            }
            Color subColor = dot == Health.Warning || dot == Health.Error ? Theme.Of(Role.WarningText)
                           : dot == null ? dim : state == TileState.Unknown ? dim : text;
            TextRenderer.DrawText(g, sub, Font, new Rectangle(sx, y, Math.Max(0, right - sx), Font.Height), subColor, flags);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(ClientRectangle, -3, -3));
        }

        // The state circle (the status window's colours) with its symbol.
        void DrawDot(Graphics g, RectangleF r, Health h, bool hc)
        {
            Color fill = Brand.HealthColor(h);
            string glyph = h == Health.Ok ? "✓" : h == Health.Busy ? "…" : h == Health.Warning ? "!" : h == Health.Error ? "✗" : "?";
            Color sym = Theme.Fixed.StateSymbol;
            if (hc) { fill = Theme.Of(Role.Window); sym = Theme.Of(Role.Text); }
            else if (h == Health.Warning) sym = Theme.Fixed.Ink;   // black on amber, as in the mockup
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            if (hc) using (var p = new Pen(Theme.Of(Role.Text))) g.DrawEllipse(p, r);
            using (var f = new Font(Font.FontFamily, r.Height * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var b = new SolidBrush(sym))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(glyph, f, b, new RectangleF(r.X, r.Y + r.Height * 0.04f, r.Width, r.Height), sf);
        }

        // The icons of the mockup (16 × 16 units), as lines.
        void DrawIcon(Graphics g, RectangleF r, Color color)
        {
            float k = r.Width / 16f;
            using (var pen = new Pen(color, Math.Max(1f, 1.2f * k)) { LineJoin = LineJoin.Round })
            {
                var s = g.Save();
                g.TranslateTransform(r.X, r.Y);
                g.ScaleTransform(k, k);
                pen.Width = Math.Max(1f, 1.2f * k) / k;
                switch (Icon)
                {
                    case TileIcon.Printer:
                        g.DrawLines(pen, new[] { new PointF(4.5f, 6), new PointF(4.5f, 2.5f), new PointF(11.5f, 2.5f), new PointF(11.5f, 6) });
                        g.DrawLines(pen, new[] { new PointF(4.5f, 11.5f), new PointF(2.5f, 11.5f), new PointF(2.5f, 6.5f), new PointF(13.5f, 6.5f), new PointF(13.5f, 11.5f), new PointF(11.5f, 11.5f) });
                        g.DrawRectangle(pen, 4.5f, 9, 7, 4.5f);
                        break;
                    case TileIcon.Label:
                        using (var p = Rounded(new RectangleF(3, 2, 10, 12), 1)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 5.5f, 5.5f, 10.5f, 5.5f);
                        g.DrawLine(pen, 5.5f, 8, 10.5f, 8);
                        g.DrawLine(pen, 5.5f, 10.5f, 8.5f, 10.5f);
                        break;
                    default:
                        using (var p = Rounded(new RectangleF(2, 3, 12, 10), 1)) g.DrawPath(pen, p);
                        g.DrawLine(pen, 2, 6.5f, 14, 6.5f);
                        g.DrawLine(pen, 2, 9.8f, 14, 9.8f);
                        g.DrawLine(pen, 6.5f, 6.5f, 6.5f, 13);
                        break;
                }
                g.Restore(s);
            }
        }

        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); if (Clickable) { hot = true; Invalidate(); } }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && Clickable && Enabled && ClientRectangle.Contains(e.Location)) Activate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (Clickable && Chevron && (keyData == Keys.Up || keyData == Keys.Down)) return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Clickable || !Enabled) return;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || Chevron && (e.KeyCode == Keys.F4 || e.Alt && e.KeyCode == Keys.Down))
            {
                e.Handled = e.SuppressKeyPress = true;
                Activate();
            }
            else if (Chevron && !e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down))
            {
                e.Handled = true;
                var step = Step;
                if (step != null) step(this, e.KeyCode == Keys.Up ? -1 : 1);
            }
        }

        // Tab stops on the tile, the button of a call to action answers Enter (the dialog's AcceptButton is none).
        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (Clickable && Focused && keyData == Keys.Enter) { Activate(); return true; }
            return base.ProcessDialogKey(keyData);
        }

        public void Activate()
        {
            var a = Activated;
            if (a != null) a(this, EventArgs.Empty);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (small != null) small.Dispose(); if (bold != null) bold.Dispose(); }
            base.Dispose(disposing);
        }
    }

    enum ChipKind { Found, Missing, Blocking, Waiting, Special, Unused, Key, Note }

    sealed class Chip
    {
        public string Text;
        public ChipKind Kind;
        public Chip(string text, ChipKind kind) { Text = text; Kind = kind; }
    }

    sealed class ChipRow : Control
    {
        List<Chip> chips = new List<Chip>();
        int shown;              // chips drawn (the rest behind "+N more")
        Rectangle moreRect;
        bool moreHot;
        public string MoreText = "+{0} more";   // the dialog sets the translated format

        public ChipRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            AccessibleRole = AccessibleRole.StaticText;
            EventHandler changed = delegate { Invalidate(); };
            Theme.Changed += changed;
            Disposed += delegate { Theme.Changed -= changed; };
        }

        public IList<Chip> Chips { get { return chips; } }

        // All chips as one line (tests, the accessible description, the list behind "+N more").
        public string Line { get { return string.Join(" | ", chips.Select(c => c.Text)); } }

        public int Hidden { get { return chips.Count - shown; } }

        public void Set(IEnumerable<Chip> list)
        {
            chips = list.ToList();
            AccessibleDescription = Line;
            Fit();
            Invalidate();
        }

        public int ChipHeight { get { return Font.Height + LogicalToDeviceUnits(6); } }
        int Gap { get { return LogicalToDeviceUnits(4); } }
        int Inner(Chip c) { return c.Kind == ChipKind.Unused || c.Kind == ChipKind.Key || c.Kind == ChipKind.Note ? LogicalToDeviceUnits(3) : LogicalToDeviceUnits(8); }
        int WidthOf(Chip c) { return TextRenderer.MeasureText(c.Text, Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width + 2 * Inner(c); }
        string More(int n) { return string.Format(MoreText, n); }

        // How many chips fit in the line, with "+N more" when not all do; a "not on the label:" key never ends the line.
        void Fit()
        {
            int x = 0, n = 0;
            foreach (var c in chips) { x += WidthOf(c) + Gap; if (x - Gap > Width) break; n++; }
            if (n < chips.Count)
            {
                while (n > 0)
                {
                    int w = chips.Take(n).Sum(c => WidthOf(c) + Gap) + WidthOf(new Chip(More(chips.Count - n), ChipKind.Found));
                    if (w <= Width && chips[n - 1].Kind != ChipKind.Key) break;
                    n--;
                }
            }
            shown = n;
            bool more = shown < chips.Count;
            SetStyle(ControlStyles.Selectable, more);
            TabStop = more;
            AccessibleRole = more ? AccessibleRole.PushButton : AccessibleRole.StaticText;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); Fit(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Of(Role.Window));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int h = ChipHeight, y = (Height - h) / 2, x = 0;
            for (int i = 0; i < shown; i++) { x += Draw(g, chips[i], x, y, h, false) + Gap; }
            moreRect = Rectangle.Empty;
            if (shown < chips.Count)
            {
                var m = new Chip(More(chips.Count - shown), ChipKind.Found);
                int w = Draw(g, m, x, y, h, true);
                moreRect = new Rectangle(x, y, w, h);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(moreRect, 1, 1));
            }
        }

        int Draw(Graphics g, Chip c, int x, int y, int h, bool more)
        {
            bool hc = Theme.HighContrast;
            int w = WidthOf(c);
            var r = new RectangleF(x + 0.5f, y + 0.5f, w - 1, h - 1);
            Color back = Color.Empty, line = Color.Empty, text = Theme.Of(Role.Text);
            bool dashed = false;
            switch (more ? (ChipKind)(-1) : c.Kind)
            {
                case ChipKind.Found: back = Theme.Of(Role.Selection); line = Theme.Of(Role.AccentSoft); break;
                case ChipKind.Missing: case ChipKind.Waiting: line = Theme.Of(Role.FieldLine); text = Theme.Of(Role.Dim); dashed = true; break;
                case ChipKind.Blocking:
                    back = Theme.Of(Role.ErrorBack);
                    line = Theme.Of(Role.ErrorLine);
                    text = Theme.Of(Role.ErrorText);
                    break;
                case ChipKind.Special: line = Theme.Of(Role.Border); text = Theme.Of(Role.Dim); break;
                case ChipKind.Unused: case ChipKind.Key: text = Theme.Of(Role.Dim); break;
                case ChipKind.Note: text = Theme.Of(Role.WarningText); break;
                default: line = moreHot || Focused ? Theme.Of(Role.Accent) : Theme.Of(Role.AccentSoft); text = Theme.Of(Role.Accent); break;   // "+N more"
            }
            if (hc)
            {
                back = Color.Empty;
                if (line != Color.Empty) line = Theme.Of(Role.Text);
                if (c.Kind == ChipKind.Found && !more) text = Theme.Of(Role.Text);
            }
            using (var path = HeadTile.Rounded(r, h / 2f))
            {
                if (back != Color.Empty) using (var b = new SolidBrush(back)) g.FillPath(b, path);
                if (line != Color.Empty)
                    using (var p = new Pen(line, hc && c.Kind == ChipKind.Blocking ? 2f : 1f) { DashStyle = dashed ? DashStyle.Dash : DashStyle.Solid })
                        g.DrawPath(p, path);
            }
            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter;
            if (c.Kind == ChipKind.Found && !more && !hc && c.Text.StartsWith("✓", StringComparison.Ordinal))
            {
                // the tick in the accent colour, the name in the text colour
                int tick = TextRenderer.MeasureText("✓", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                int left = x + Inner(c);
                TextRenderer.DrawText(g, "✓", Font, new Rectangle(left, y, tick, h), Theme.Of(Role.Accent), flags & ~TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(g, c.Text.Substring(1), Font, new Rectangle(left + tick, y, w - 2 * Inner(c) - tick, h), text, flags & ~TextFormatFlags.HorizontalCenter);
            }
            else TextRenderer.DrawText(g, c.Text, Font, new Rectangle(x, y, w, h), text, flags);
            return w;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool on = moreRect.Contains(e.Location);
            Cursor = on ? Cursors.Hand : Cursors.Default;
            if (on != moreHot) { moreHot = on; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (moreHot) { moreHot = false; Invalidate(); } }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left && moreRect.Contains(e.Location)) ShowAll();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (shown < chips.Count && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space)) { e.Handled = e.SuppressKeyPress = true; ShowAll(); }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        ContextMenuStrip list;

        // "+N more": every chip in a list under the line.
        public void ShowAll()
        {
            if (list != null) list.Dispose();
            list = new ContextMenuStrip { Font = Font, ShowImageMargin = false };
            foreach (var c in chips)
            {
                if (c.Kind == ChipKind.Key) { list.Items.Add(new ToolStripSeparator()); }
                list.Items.Add(new ToolStripMenuItem(c.Text.Replace("&", "&&")));
            }
            list.Show(this, new Point(moreRect.Left, Height));
        }

        protected override void Dispose(bool disposing) { if (disposing && list != null) list.Dispose(); base.Dispose(disposing); }
    }
}
