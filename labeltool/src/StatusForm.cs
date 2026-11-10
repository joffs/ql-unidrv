// "Printer status" window: every QL printer at once, each with what it is doing, the loaded roll and what
// to do about an error, plus a link to its Windows print queue (and "Use printer online" while Windows' "Use
// Printer Offline" is on). The footer sets the printer
// notifications (tray monitor, which also watches all QL printers). Refreshes itself every few seconds.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class StatusForm : ScaledForm
    {
        static string NotifyTip
        {
            get
            {
                return Lang.T("When a QL printer stops, a Windows notification appears at the bottom right with the reason and what " +
                              "to do – for example cover open, roll empty or wrong roll. For all QL printers and every program. " +
                              "Starts with Windows; no administrator rights needed.");
            }
        }

        readonly List<PrinterCard> cards = new List<PrinterCard>();
        readonly CheckBox notify = new CheckBox { Text = Lang.T("Notify me about printer errors"), AutoSize = true, FlatStyle = FlatStyle.System };
        readonly Timer timer = new Timer { Interval = 4000 };
        readonly ToolTip tips = new ToolTip { AutoPopDelay = 20000, InitialDelay = 300 };
        bool running;
        // The window sits in the corner above the clock and grows upwards, so a long error message never pushes
        // it off the screen. The user may move it; its bottom right corner stays put.
        Point anchor;  // bottom right corner
        public Point? StartAnchor;   // where it opens instead of the screen's corner (a restart after a language change)
        public Point Corner { get { return anchor != Point.Empty ? anchor : new Point(Right, Bottom); } }
        bool placing;
        bool dragging;  // between WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE
        const int WM_ENTERSIZEMOVE = 0x231, WM_EXITSIZEMOVE = 0x232, WM_DPICHANGED = 0x2E0;

        // Follows Windows' printer list: a QL printer added while the window is open gets a card, a removed one loses it.
        public StatusForm() : this(QlPrinters.OurPrinters(), QlPrinters.OurPrinters) { }

        // printers: the cards at the start; source: the printer list asked again with each refresh (null: the cards stay).
        public StatusForm(IEnumerable<string> printers, Func<IEnumerable<string>> source = null)
        {
            this.source = source;
            Text = "QL Printer Status";
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.Manual;
            Theme.Use(this);
            Theme.Back(this, Role.Content);

            // Sizes from the font, so the window follows the display scaling.
            // 20 em: every status line fits on one line, except "Not connected – ... at the PC <host>" (longer host
            // names), which breaks after the dash (PrinterCard.Fit).
            int em = Font.Height, textWidth = em * 20;

            // One card per printer whose status can be shown (not file ports such as the capture printer).
            content = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(Brand.Gutter(em), em, Brand.Gutter(em), em / 4), Margin = Padding.Empty };
            foreach (var p in printers.Where(HasStatus))
            {
                var card = NewCard(p);
                cards.Add(card);
                content.Controls.Add(card);
            }
            none = new Label { AutoSize = true, MaximumSize = new Size(textWidth + em * 3, 0), Margin = new Padding(0, 0, 0, em), Text = MainForm.NoPrinterText, Visible = cards.Count == 0 };
            content.Controls.Add(none);

            // Footer: notifications for all printers, and Close.
            var footer = Theme.Back(new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, Dock = DockStyle.Fill, Padding = new Padding(Brand.Gutter(em), em * 3 / 4, Brand.Gutter(em), em * 3 / 4), Margin = Padding.Empty }, Role.Surface);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            notify.Checked = TrayMonitor.AutostartEnabled;
            notify.Anchor = AnchorStyles.Left;
            notify.Margin = Padding.Empty;
            notify.CheckedChanged += delegate { SetNotify(notify.Checked); };
            var info = InfoIcon(em);
            // The explanation in the app's own tip at the (i), on hover or click (a click pins it); the window keeps its size
            InfoTip.Attach(info, () => NotifyTip, hover: true);
            var close = new Button { Text = Lang.T("Close"), AutoSize = true, DialogResult = DialogResult.Cancel, Anchor = AnchorStyles.Right, Margin = new Padding(em, 0, 0, 0), FlatStyle = FlatStyle.System };
            close.Click += delegate { Close(); };
            CancelButton = close;
            footer.Controls.Add(notify, 0, 0);
            footer.Controls.Add(info, 1, 0);
            footer.Controls.Add(close, 2, 0);
            // The tray monitor can change it too.
            Activated += delegate { if (notify.Checked != TrayMonitor.AutostartEnabled) notify.Checked = TrayMonitor.AutostartEnabled; };

            // The cards scroll when they need more height than the screen offers (many printers, advice open):
            // the window stays inside the working area, the footer stays visible (FitScroller).
            content.Dock = DockStyle.None;
            content.Location = Point.Empty;
            scroller = new Panel { AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty };
            scroller.Controls.Add(content);
            content.SizeChanged += delegate { FitScroller(); };

            root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            root.Controls.Add(scroller);
            root.Controls.Add(Theme.Back(new Panel { Height = 1, Dock = DockStyle.Fill, Margin = Padding.Empty }, Role.SurfaceLine));  // line above the footer
            root.Controls.Add(footer);
            Controls.Add(root);
            AddHelpToSystemMenu(this, "QL Printer Status");

            timer.Tick += delegate { RefreshNow(); };
            Load += delegate
            {
                anchor = StartAnchor ?? TrayCorner(em);
                FitScroller();
                Place();
            };
            SizeChanged += delegate { if (anchor != Point.Empty) Place(); };
            Move += delegate { if (!placing && anchor != Point.Empty) anchor = new Point(Right, Bottom); };
            // The sizes above come from the font in the constructor, which is Windows' system DPI: when the window
            // opens on, or is moved to, a screen with other scaling, the sizes follow the font here (Rescale).
            DpiChanged += delegate { BeginInvoke((MethodInvoker)Rescale); };
            Shown += delegate { Rescale(); RefreshNow(); timer.Start(); };
            FormClosed += delegate { timer.Stop(); timer.Dispose(); };
        }

        readonly Func<IEnumerable<string>> source;
        Label none;                // "no printer" while there is no card
        TableLayoutPanel content;  // its left padding is the gutter (Brand.Gutter): shows whether the sizes fit the font
        Panel scroller;            // holds content; as high as content, at most what the working area leaves
        TableLayoutPanel root;

        // The card area's size: all of content, or (higher than the working area allows) a vertical scroll bar.
        void FitScroller()
        {
            if (scroller == null || dragging) return;
            var area = Screen.FromPoint(anchor != Point.Empty ? anchor : Cursor.Position).WorkingArea;
            // Title bar, borders, the line and the footer (from their own sizes: the window's may not be laid out yet)
            int rest = Height - ClientSize.Height + root.GetPreferredSize(Size.Empty).Height - scroller.Height;
            // At most 690 logical px high (small laptop screens), however many cards or details are open
            int max = Math.Max(Font.Height * 4, Math.Min(area.Height, 690 * DeviceDpi / 96) - rest);
            bool scroll = content.Height > max;
            var size = new Size(content.Width + (scroll ? SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) : 0),
                                scroll ? max : content.Height);
            if (scroller.Size != size) scroller.Size = size;
        }

        // Scales margins, paddings, wrap widths and the drawn icons to the font height; bold texts get the window's
        // new font size. On a DPI change WinForms already scales sizes, margins and paddings (again here would double
        // them); when the window opens on a screen with other scaling it may not. The content's padding tells which.
        void Rescale()
        {
            float k = (float)Brand.Gutter(Font.Height) / content.Padding.Left;
            if (Math.Abs(k - 1f) < 0.03f) k = 1f;
            Func<int, int> z = v => (int)Math.Round(v * k);
            Func<Padding, Padding> zp = q => new Padding(z(q.Left), z(q.Top), z(q.Right), z(q.Bottom));
            Func<Size, Size> zs = q => new Size(z(q.Width), z(q.Height));
            SuspendLayout();
            try
            {
                Action<Control> walk = null;
                walk = c =>
                {
                    if (c != this && k != 1f)
                    {
                        c.Margin = zp(c.Margin);
                        c.Padding = zp(c.Padding);
                        if (c.MaximumSize != Size.Empty) c.MaximumSize = zs(c.MaximumSize);
                        if (c.MinimumSize != Size.Empty) c.MinimumSize = zs(c.MinimumSize);
                        if (!c.AutoSize && c.Dock == DockStyle.None && c != scroller) c.Size = zs(c.Size);  // badges, info icon (FitScroller sizes the card area)
                    }
                    if (c != this)
                    {
                        if (c.Font.Style != FontStyle.Regular && Math.Abs(c.Font.SizeInPoints - Font.SizeInPoints) > 0.1f)
                            c.Font = new Font(Font, c.Font.Style);
                    }
                    foreach (Control child in c.Controls) walk(child);
                };
                walk(this);
            }
            finally { ResumeLayout(true); }
            FitScroller();
            if (anchor != Point.Empty) Place();
        }

        // Where a window from the tray puts its bottom right corner: the working area's corner (beside the taskbar's
        // notification area) on the cursor's screen, a little inside. em: the window's font height.
        internal static Point TrayCorner(int em)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            int gap = em * 3 / 4;
            return new Point(area.Right - gap, area.Bottom - gap);
        }

        // Keeps the bottom right corner at the anchor, and the window on the screen.
        void Place()
        {
            if (dragging) return;
            var area = Screen.FromPoint(anchor).WorkingArea;
            placing = true;
            Location = new Point(Math.Max(area.Left, anchor.X - Width), Math.Max(area.Top, anchor.Y - Height));
            placing = false;
        }

        // Printers the window shows: not those printing to a file (the capture printer).
        static bool HasStatus(string printer)
        {
            string port = QlDevice.PortOf(printer) ?? "";
            return !(port.Contains(":\\") || port.EndsWith(":", StringComparison.Ordinal) && !QlDevice.IsUsbPort(port));
        }

        // The info glyph: a filled blue circle with a white "i" drawn as shapes (a dot and a rounded stem), so it is
        // crisp at any display scaling; shows that there is an explanation (InfoTip: hover or click).
        internal static Control InfoIcon(int em)
        {
            int d = em + 3;
            var icon = new Panel { Size = new Size(d, d), Anchor = AnchorStyles.Left, Margin = new Padding(6, 0, 0, 0), Cursor = Cursors.Hand,
                                   AccessibleRole = AccessibleRole.PushButton, AccessibleName = Lang.T("Explanation") };
            bool hot = false;
            icon.MouseEnter += delegate { hot = true; icon.Invalidate(); };
            icon.MouseLeave += delegate { hot = false; icon.Invalidate(); };
            icon.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                float size = icon.Width - 1f, cx = size / 2f;  // the window may have rescaled it
                using (var fill = new SolidBrush(hot ? Theme.Of(Role.AccentHot) : Theme.Of(Role.Accent))) g.FillEllipse(fill, 0.5f, 0.5f, size - 1f, size - 1f);
                float stem = Math.Max(2f, size * 0.15f);
                using (var white = new SolidBrush(Theme.Of(Role.OnPrimary)))
                {
                    float dot = stem * 1.15f;
                    g.FillEllipse(white, cx - dot / 2f, size * 0.20f, dot, dot);
                    using (var path = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        float top = size * 0.42f, bottom = size * 0.78f, r = stem / 2f;
                        path.AddArc(cx - r, top, stem, stem, 180, 180);
                        path.AddArc(cx - r, bottom - stem, stem, stem, 0, 180);
                        path.CloseFigure();
                        g.FillPath(white, path);
                    }
                }
            };
            return icon;
        }

        // Sizes from the window's current font (it may have moved to a screen with other scaling since the start).
        PrinterCard NewCard(string printer)
        {
            int em = Font.Height;
            return new PrinterCard(printer, Font, em, em * 20, tips) { Margin = new Padding(0, 0, 0, em) };
        }

        async void RefreshNow()
        {
            if (running || IsDisposed) return;
            running = true;
            List<PrinterCheck> checks;
            try
            {
                if (source != null)
                {
                    List<string> now = null;
                    try { now = await Task.Run(() => source().Where(HasStatus).ToList()); }
                    catch (Exception) { }   // Windows couldn't be asked: the cards stay
                    if (IsDisposed) return;
                    if (now != null) UpdateCards(now);
                }
                var names = cards.Select(c => c.Printer).ToList();
                if (names.Count == 0) return;
                checks = await Task.Run(() => names.AsParallel().AsOrdered().Select(Check).ToList());
            }
            finally { running = false; }
            if (IsDisposed) return;
            SuspendLayout();
            for (int i = 0; i < cards.Count; i++) cards[i].Display(checks[i]);
            ResumeLayout();
        }

        // Cards for the printers Windows lists now, in its order: kept cards keep their open details and advice, a new
        // printer gets a card, a removed one loses it. The scroll position stays; FitScroller follows the new height.
        void UpdateCards(List<string> now)
        {
            if (now.SequenceEqual(cards.Select(c => c.Printer), StringComparer.OrdinalIgnoreCase)) return;
            var old = cards.ToDictionary(c => c.Printer, StringComparer.OrdinalIgnoreCase);
            var scroll = scroller.AutoScrollPosition;
            SuspendLayout();
            content.SuspendLayout();
            cards.Clear();
            foreach (var p in now)
            {
                PrinterCard card;
                if (old.TryGetValue(p, out card)) old.Remove(p);
                else { card = NewCard(p); content.Controls.Add(card); }
                cards.Add(card);
            }
            foreach (var gone in old.Values) { content.Controls.Remove(gone); gone.Dispose(); }
            for (int i = 0; i < cards.Count; i++) content.Controls.SetChildIndex(cards[i], i);
            content.Controls.SetChildIndex(none, cards.Count);
            none.Visible = cards.Count == 0;
            content.ResumeLayout();
            ResumeLayout();
            scroller.AutoScrollPosition = new Point(-scroll.X, -scroll.Y);
        }

        static PrinterCheck Check(string printer)
        {
            try { return PrinterCheck.Run(printer, true); }
            catch (Exception ex)
            {
                var c = new PrinterCheck { Printer = printer, Health = Health.Warning, Headline = Lang.T("The printer status can't be read") };
                c.Advice.Add(ex.Message);
                return c;
            }
        }

        // Opened directly on a screen with other scaling than Windows' system DPI (the 150 % second screen): ScaledForm lets
        // WinForms scale as on a move there; bold texts follow the window's font after a DPI change.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            PrinterStatusProgram.Mark(this);   // the other programs find it and put it in front (ShowRunning)
        }

        protected override bool MatchAfterLoad { get { return true; } }   // Load placed it on its screen

        // Help (F1: the status page of the QL Label Formats help; QL Printer Status has none of its own) and "About QL
        // Unidrv" in the window menu (right-click on the title bar, Alt+Space) of a window without a menu bar.
        const int WM_SYSCOMMAND = 0x112, MF_SEPARATOR = 0x800, MF_STRING = 0, AboutCommand = 0x5150, HelpCommand = 0x5160;
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetSystemMenu(IntPtr hwnd, bool revert);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool AppendMenu(IntPtr menu, int flags, IntPtr id, string text);

        string aboutProgram;

        internal static void AddHelpToSystemMenu(StatusForm form, string program)
        {
            form.aboutProgram = program;
            HelpPages.F1(form, HelpPages.Status);
            form.HandleCreated += delegate
            {
                IntPtr menu = GetSystemMenu(form.Handle, false);
                if (menu == IntPtr.Zero) return;
                AppendMenu(menu, MF_SEPARATOR, IntPtr.Zero, null);
                AppendMenu(menu, MF_STRING, new IntPtr(HelpCommand), Lang.T("QL Printer Status Help") + "\tF1");
                AppendMenu(menu, MF_STRING, new IntPtr(AboutCommand), Lang.T("About QL Unidrv"));
            };
        }

        protected override void WndProc(ref Message m)
        {
            // While the user drags the window (also onto a screen with other scaling, which resizes it), it goes where
            // it is dragged; afterwards its new bottom right corner is the anchor. Place() during the drag snapped it
            // back to the screen it came from.
            // Opened (or placed) on a screen with other scaling: Windows' suggested size for it can reach into the
            // neighbour screen; taken as the new corner, Place() then moved the window there (seen 2026-11-03 at 150 %).
            if (m.Msg == WM_DPICHANGED)
            {
                placing = true;
                try { base.WndProc(ref m); }
                finally { placing = false; }
                if (anchor != Point.Empty) Place();
                return;
            }
            if (m.Msg == WM_ENTERSIZEMOVE) dragging = true;
            if (m.Msg == WM_EXITSIZEMOVE)
            {
                base.WndProc(ref m);   // WinForms ends its drag mode first
                dragging = false;
                // The corner where it was dropped, inside the screen it is mostly on (dropped partly below the screen,
                // the corner alone could name the neighbour screen); then its size for this screen's scaling (it kept
                // the old one during the drag) and working area.
                var area = Screen.FromControl(this).WorkingArea;
                anchor = new Point(Math.Min(Right, area.Right), Math.Min(Bottom, area.Bottom));
                FitScroller();
                var fit = GetPreferredSize(Size.Empty);
                if (fit != Size) Size = fit;
                Place();
                return;
            }
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == AboutCommand && aboutProgram != null)
            {
                AboutForm.ShowAbout(this, aboutProgram);
                return;
            }
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt64() & 0xFFF0) == HelpCommand)
            {
                HelpPages.Open(this, HelpPages.Status);
                return;
            }
            base.WndProc(ref m);
        }

        void SetNotify(bool on)
        {
            if (on == TrayMonitor.AutostartEnabled) return;
            try { TrayMonitor.SetAutostart(on); }
            catch (Exception ex) { QlMessage.Ask(this, Text, Lang.T("Printer notifications couldn't be changed."), null, MessageBoxButtons.OK, MessageBoxIcon.Error,
                                                 MessageBoxDefaultButton.Button1, ex.Message); }
            notify.Checked = TrayMonitor.AutostartEnabled;
        }
    }
}
