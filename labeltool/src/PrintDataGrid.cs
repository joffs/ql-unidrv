// QL Label Maker, "Print with data" dialog (PrintDataDialog.cs), part: the grid of small previews, one card per data
// row. Selected cards have an accent frame, a light tint and a
// small round tick; like Explorer's thumbnails: click = only this, Ctrl+click = add/remove, Shift+click = range from
// the last click, a click on empty space = none; arrows move the focus (dotted), Space toggles, Shift+arrows extend.
// Previews are rendered lazily on the window's own thread, one per timer tick and only for the cards in view (the
// renderer shares pictures' GDI+ objects with the editor, so no worker thread), cached per row; a light placeholder
// until then, so a list of 500+ rows opens at once.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class PrintDataGrid : Control
    {
        // The card picture of one row at the given pixel size: the whole label as shown (EditorRender.RenderPreview:
        // smooth text, codes and pictures as printed); null: it couldn't be drawn.
        public Func<DataRecord, Size, Bitmap> Renderer;
        public float LabelWidthMm = 62, LabelHeightMm = 29;   // the whole label as shown
        public RectangleF PrintableMm = new RectangleF(0, 0, 62, 29);
        public Func<DataRecord, int> CopiesOf = r => r.Copies;
        public event EventHandler SelectionChanged;
        public event Action<DataRecord> Open;   // double-click, Enter: the large preview
        public event EventHandler SizeStepChanged;   // Ctrl+wheel changed the card size

        // The card size (2026-11-09: small text was hard to read): the narrowest card width per step at 100 %;
        // the cards then fill the row. Step 0 = the size before the slider.
        public static readonly int[] CardSizes = { 132, 175, 225, 290, 370 };
        int sizeStep;
        public int SizeStep
        {
            get { return sizeStep; }
            set
            {
                int v = Math.Max(0, Math.Min(CardSizes.Length - 1, value));
                if (v == sizeStep) return;
                int keep = focus >= 0 ? focus : FirstVisible();
                sizeStep = v;
                Relayout();
                if (keep >= 0 && keep < shown.Count) ScrollTo(keep);
                Invalidate();
            }
        }

        List<DataRecord> shown = new List<DataRecord>();
        readonly HashSet<DataRecord> selected = new HashSet<DataRecord>();
        readonly Dictionary<DataRecord, Bitmap> previews = new Dictionary<DataRecord, Bitmap>();   // at the card picture's size
        readonly LinkedList<DataRecord> renderOrder = new LinkedList<DataRecord>();   // oldest first, for the cache limit
        readonly HashSet<DataRecord> failed = new HashSet<DataRecord>();
        readonly VScrollBar bar = new VScrollBar();
        readonly Timer renderTimer = new Timer { Interval = 1 };
        readonly ToolTip tip = new ToolTip();
        int focus = -1, anchor = -1, hover = -1, scroll;
        string emptyText = "";
        const int CacheLimit = 400;

        public PrintDataGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            bar.Dock = DockStyle.Right;
            bar.Scroll += delegate { scroll = bar.Value; Invalidate(); };
            Controls.Add(bar);
            renderTimer.Tick += delegate { RenderNext(); };
            AccessibleRole = AccessibleRole.List;
        }

        // ---------- data ----------

        public IList<DataRecord> Shown { get { return shown; } }

        // The rows in view (the search applied). keepSelection false: a new file, all selected.
        public void SetRows(IEnumerable<DataRecord> rows, bool newData, string empty)
        {
            shown = rows.ToList();
            emptyText = empty ?? "";
            if (newData)
            {
                ClearPreviews();
                selected.Clear();
                foreach (var r in shown) selected.Add(r);
                scroll = 0;
            }
            focus = shown.Count == 0 ? -1 : Math.Min(Math.Max(focus, 0), shown.Count - 1);
            anchor = focus;
            Relayout();
            Changed();
        }

        // Another label size (the dialog's printer changed, and with it the label): new card shapes and previews.
        public void SetLabel(float widthMm, float heightMm, RectangleF printableMm)
        {
            LabelWidthMm = widthMm;
            LabelHeightMm = heightMm;
            PrintableMm = printableMm;
            ClearPreviews();
            Relayout();
        }

        // The previews must be drawn again ("Leave missing fields empty" switched).
        public void ClearPreviews()
        {
            foreach (var b in previews.Values) if (b != null) b.Dispose();
            previews.Clear(); renderOrder.Clear(); failed.Clear();
            Invalidate();
        }

        // The selected rows that are in view, in their order.
        public List<DataRecord> SelectedShown { get { return shown.Where(selected.Contains).ToList(); } }

        public void SelectAll() { foreach (var r in shown) selected.Add(r); Changed(); }
        public void SelectNone() { foreach (var r in shown) selected.Remove(r); Changed(); }
        public bool IsSelected(DataRecord r) { return selected.Contains(r); }
        public int FocusIndex { get { return focus; } }

        void Changed()
        {
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        // ---------- geometry (pixels at this control's dpi) ----------

        float S { get { return DeviceDpi / 96f; } }
        int Pad { get { return (int)(10 * S); } }
        int Gap { get { return (int)(10 * S); } }
        int Columns { get { return Math.Max(1, (Inner.Width - Pad + Gap) / ((int)(CardSizes[sizeStep] * S) + Gap)); } }
        Rectangle Inner { get { return new Rectangle(0, 0, ClientSize.Width - (bar.Visible ? bar.Width : 0), ClientSize.Height); } }
        int CardWidth { get { int c = Columns; return Math.Max(40, (Inner.Width - 2 * Pad - (c - 1) * Gap) / c); } }
        int HeadHeight { get { return Font.Height + (int)(4 * S); } }
        int CardInset { get { return (int)(5 * S); } }

        // The label picture inside a card: the label's own proportion, at most as high as wide (a long label fits in).
        Size PictureSize
        {
            get
            {
                int w = CardWidth - 2 * CardInset;
                int h = (int)Math.Round(w * LabelHeightMm / Math.Max(1f, LabelWidthMm));
                if (h > w) { w = (int)Math.Round(w * (float)w / h); h = CardWidth - 2 * CardInset; }
                return new Size(Math.Max(8, w), Math.Max(8, h));
            }
        }

        int CardHeight { get { return CardInset + HeadHeight + PictureSize.Height + CardInset; } }

        Rectangle CardRect(int i)
        {
            int c = Columns;
            return new Rectangle(Pad + (i % c) * (CardWidth + Gap), Pad + (i / c) * (CardHeight + Gap) - scroll, CardWidth, CardHeight);
        }

        int ContentHeight { get { int rows = (shown.Count + Columns - 1) / Columns; return rows == 0 ? 0 : 2 * Pad + rows * CardHeight + (rows - 1) * Gap; } }

        void Relayout()
        {
            int content = ContentHeight;
            bool need = content > ClientSize.Height;
            if (bar.Visible != need) bar.Visible = need;
            content = ContentHeight;   // the columns with or without the bar
            int max = Math.Max(0, content - ClientSize.Height);
            scroll = Math.Max(0, Math.Min(scroll, max));
            bar.Minimum = 0;
            bar.LargeChange = Math.Max(1, ClientSize.Height);
            bar.SmallChange = Math.Max(1, CardHeight / 3);
            bar.Maximum = Math.Max(0, content - 1);
            bar.Value = Math.Min(scroll, Math.Max(0, bar.Maximum - bar.LargeChange + 1));
            scroll = bar.Value;
            Invalidate();
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); if (IsHandleCreated) Relayout(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); ClearPreviews(); Relayout(); }

        int HitTest(Point p)
        {
            for (int i = FirstVisible(); i < shown.Count && i <= LastVisible(); i++)
                if (CardRect(i).Contains(p)) return i;
            return -1;
        }

        int FirstVisible() { int rowH = CardHeight + Gap; return Math.Max(0, (scroll - Pad) / Math.Max(1, rowH)) * Columns; }
        int LastVisible() { int rowH = CardHeight + Gap; return Math.Min(shown.Count - 1, ((scroll + ClientSize.Height) / Math.Max(1, rowH) + 1) * Columns - 1); }

        void ScrollTo(int i)
        {
            if (i < 0) return;
            var r = CardRect(i);
            if (r.Top < Pad) scroll += r.Top - Pad;
            else if (r.Bottom > ClientSize.Height - Pad) scroll += r.Bottom - ClientSize.Height + Pad;
            Relayout();
        }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            bool hc = Theme.HighContrast;
            Color back = Theme.Of(Role.Desk), card = Theme.Of(Role.Content), line = Theme.Of(Role.Border);
            Color accent = Theme.Of(Role.Accent);
            Color tint = hc ? Theme.Of(Role.Selection) : Theme.Mix(card, accent, Theme.Dark ? 0.28 : 0.12);
            Color text = Theme.Of(Role.Text), dim = Theme.Of(Role.Dim);
            g.Clear(back);
            if (shown.Count == 0)
            {
                var r = Rectangle.Inflate(Inner, -Pad * 2, -Pad * 2);
                TextRenderer.DrawText(g, emptyText, Font, r, dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            int first = FirstVisible(), last = LastVisible();
            float s = S;
            var pic = PictureSize;
            bool want = false;
            for (int i = first; i <= last; i++)
            {
                var r = CardRect(i);
                if (!r.IntersectsWith(e.ClipRectangle)) continue;
                var rec = shown[i];
                bool sel = selected.Contains(rec);
                using (var b = new SolidBrush(sel ? tint : card)) g.FillRectangle(b, r);
                Color fore = text, foreDim = sel && hc ? text : dim;
                if (sel)
                    using (var p = new Pen(accent, Math.Max(2f, 2.5f * s)) { Alignment = PenAlignment.Inset }) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                else
                    using (var p = new Pen(line)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);

                // head: row number, the labelname or else the first value (cut with …), the copies
                var head = new Rectangle(r.X + CardInset, r.Y + CardInset, r.Width - 2 * CardInset, HeadHeight - (int)(2 * s));
                string nr = rec.Row.ToString();
                int nrW = TextRenderer.MeasureText(g, nr, Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                TextRenderer.DrawText(g, nr, Font, new Rectangle(head.X, head.Y, nrW + 2, head.Height), foreDim, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
                string copies = CopiesBadge(rec);
                int cW = copies == null ? 0 : TextRenderer.MeasureText(g, copies, Font, Size.Empty, TextFormatFlags.NoPadding).Width + (int)(4 * s);
                var title = new Rectangle(head.X + nrW + (int)(5 * s), head.Y, Math.Max(0, head.Width - nrW - (int)(5 * s) - cW - (sel ? (int)(10 * s) : 0)), head.Height);
                TextRenderer.DrawText(g, rec.Title, Font, title, fore, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                if (copies != null)
                    TextRenderer.DrawText(g, copies, Font, new Rectangle(title.Right + (int)(2 * s), head.Y, cW, head.Height),
                                          rec.CopiesError != null ? Theme.Of(Role.ErrorText) : fore, TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);

                // the label as printed
                var lr = new Rectangle(r.X + (r.Width - pic.Width) / 2, r.Y + CardInset + HeadHeight, pic.Width, pic.Height);
                using (var b = new SolidBrush(Theme.Fixed.Paper)) g.FillRectangle(b, lr);
                var thumb = Thumb(rec, lr.Size);
                if (thumb != null) g.DrawImageUnscaled(thumb, lr.X, lr.Y);
                else if (!failed.Contains(rec))
                {
                    want = true;
                    using (var b = new SolidBrush(Color.FromArgb(24, Theme.Fixed.Ink))) g.FillRectangle(b, Rectangle.Inflate(lr, -(int)(6 * s), -(int)(6 * s)));
                }
                using (var p = new Pen(Theme.Fixed.TapeEdge)) g.DrawRectangle(p, lr.X, lr.Y, lr.Width - 1, lr.Height - 1);

                if (sel) DrawTick(g, new Point(r.Right - (int)(3 * s), r.Y + (int)(3 * s)), accent, s);
                if (i == focus && Focused)
                    ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(lr, (int)(2 * s), (int)(2 * s)), fore, sel ? tint : card);
            }
            if (want && !renderTimer.Enabled) renderTimer.Start();
        }

        // "×3" for a copies column other than 1; "×?" for one that isn't a number
        string CopiesBadge(DataRecord r)
        {
            if (r.CopiesError != null) return "×?";
            int n = CopiesOf(r);
            return n == 1 ? null : "×" + n;
        }

        // The small round tick at the card's top right corner (centre: corner moved in).
        static void DrawTick(Graphics g, Point corner, Color accent, float s)
        {
            float d = 16 * s;
            var c = new RectangleF(corner.X - d + 4 * s, corner.Y - 4 * s, d, d);
            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(accent)) g.FillEllipse(b, c);
            using (var p = new Pen(Theme.Of(Role.OnPrimary), 1.8f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(p, new[] { new PointF(c.X + d * 0.27f, c.Y + d * 0.52f), new PointF(c.X + d * 0.44f, c.Y + d * 0.68f), new PointF(c.X + d * 0.74f, c.Y + d * 0.34f) });
            g.SmoothingMode = mode;
        }

        // The card picture at this size; another size (the window or the card size changed): drawn again (RenderNext).
        Bitmap Thumb(DataRecord r, Size size)
        {
            Bitmap t;
            if (!previews.TryGetValue(r, out t) || t == null) return null;
            if (t.Size == size) return t;
            t.Dispose();
            previews.Remove(r);
            renderOrder.Remove(r);
            return null;
        }

        // One preview per tick: the first card in view without one (the window stays responsive while they come).
        void RenderNext()
        {
            if (Renderer == null || !Visible || shown.Count == 0) { renderTimer.Stop(); return; }
            int first = FirstVisible(), last = LastVisible();
            DataRecord next = null;
            for (int i = first; i <= last && next == null; i++)
                if (!previews.ContainsKey(shown[i]) && !failed.Contains(shown[i])) next = shown[i];
            if (next == null) { renderTimer.Stop(); return; }
            Bitmap b = null;
            try { b = Renderer(next, PictureSize); }
            catch (Exception) { }
            if (b == null) { failed.Add(next); Invalidate(); return; }
            previews[next] = b;
            renderOrder.AddLast(next);
            if (previews.Count > CacheLimit) Evict(first, last);
            int index = shown.IndexOf(next, first);
            Invalidate(index >= 0 ? CardRect(index) : ClientRectangle);
        }

        // Over the limit: the oldest previews not in view go (drawn again when scrolled back).
        void Evict(int first, int last)
        {
            var inView = new HashSet<DataRecord>(shown.Skip(first).Take(last - first + 1));
            var node = renderOrder.First;
            while (node != null && previews.Count > CacheLimit * 3 / 4)
            {
                var nextNode = node.Next;
                if (!inView.Contains(node.Value))
                {
                    Bitmap b;
                    if (previews.TryGetValue(node.Value, out b) && b != null) b.Dispose();
                    previews.Remove(node.Value);
                    renderOrder.Remove(node);
                }
                node = nextNode;
            }
        }

        // ---------- mouse and keys ----------

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            int i = HitTest(e.Location);
            if (i < 0)
            {
                if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0) { selected.Clear(); Changed(); }
                return;
            }
            ClickCard(i, ModifierKeys);
        }

        // Explorer's rules for one card (also the keyboard's Space with Ctrl).
        void ClickCard(int i, Keys mods)
        {
            if ((mods & Keys.Shift) != 0 && anchor >= 0)
            {
                if ((mods & Keys.Control) == 0) selected.Clear();
                for (int k = Math.Min(anchor, i); k <= Math.Max(anchor, i); k++) selected.Add(shown[k]);
            }
            else if ((mods & Keys.Control) != 0)
            {
                if (!selected.Remove(shown[i])) selected.Add(shown[i]);
                anchor = i;
            }
            else
            {
                selected.Clear();
                selected.Add(shown[i]);
                anchor = i;
            }
            focus = i;
            Changed();
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            int i = HitTest(e.Location);
            if (i >= 0 && e.Button == MouseButtons.Left && Open != null) Open(shown[i]);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if ((ModifierKeys & Keys.Control) != 0)
            {
                // Ctrl+wheel: the card size, as zooming in Explorer
                int before = sizeStep;
                SizeStep = sizeStep + (e.Delta > 0 ? 1 : e.Delta < 0 ? -1 : 0);
                if (sizeStep != before && SizeStepChanged != null) SizeStepChanged(this, EventArgs.Empty);
                return;
            }
            scroll -= e.Delta * SystemInformation.MouseWheelScrollLines * Font.Height / 120;
            Relayout();
        }

        // The full first value as a tooltip when the card's title is cut.
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int i = HitTest(e.Location);
            if (i == hover) return;
            hover = i;
            string full = null;
            if (i >= 0)
            {
                var rec = shown[i];
                int room = CardWidth - 2 * CardInset - TextRenderer.MeasureText(rec.Row.ToString(), Font).Width - (int)(30 * S);
                if (TextRenderer.MeasureText(rec.Title, Font).Width > room) full = rec.Title;
            }
            tip.SetToolTip(this, full);
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; tip.SetToolTip(this, null); }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Left: case Keys.Right: case Keys.Up: case Keys.Down: case Keys.Home: case Keys.End:
                case Keys.PageUp: case Keys.PageDown: case Keys.Space: return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (shown.Count == 0) return;
            int c = Columns, rowsInView = Math.Max(1, ClientSize.Height / (CardHeight + Gap));
            int to = focus < 0 ? 0 : focus;
            switch (e.KeyCode)
            {
                case Keys.Left: to--; break;
                case Keys.Right: to++; break;
                case Keys.Up: to -= c; break;
                case Keys.Down: to += c; break;
                case Keys.Home: to = 0; break;
                case Keys.End: to = shown.Count - 1; break;
                case Keys.PageUp: to -= c * rowsInView; break;
                case Keys.PageDown: to += c * rowsInView; break;
                case Keys.Space:
                    if (focus < 0) focus = 0;
                    ClickCard(focus, e.Shift ? Keys.Shift : Keys.Control);
                    e.Handled = true;
                    return;
                case Keys.Enter:
                    if (focus >= 0 && Open != null) Open(shown[focus]);
                    e.Handled = true;
                    return;
                case Keys.A:
                    if (e.Control) { SelectAll(); e.Handled = true; }
                    return;
                default: return;
            }
            e.Handled = true;
            to = Math.Max(0, Math.Min(shown.Count - 1, to));
            if (e.Shift)
            {
                if (anchor < 0) anchor = focus < 0 ? to : focus;
                selected.Clear();
                for (int k = Math.Min(anchor, to); k <= Math.Max(anchor, to); k++) selected.Add(shown[k]);
                focus = to;
                Changed();
            }
            else { focus = to; Invalidate(); }
            ScrollTo(focus);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); if (focus < 0 && shown.Count > 0) focus = 0; Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                renderTimer.Dispose();
                tip.Dispose();
                foreach (var b in previews.Values) if (b != null) b.Dispose();
                previews.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
