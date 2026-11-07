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
    sealed class StatusForm : Form
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
        public StatusForm() : this(MainForm.OurPrinters(), MainForm.OurPrinters) { }

        // printers: the cards at the start; source: the printer list asked again with each refresh (null: the cards stay).
        public StatusForm(IEnumerable<string> printers, Func<IEnumerable<string>> source = null)
        {
            this.source = source;
            Text = "QL Printer Status";
            Font = SystemFonts.MessageBoxFont;
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

        // "Use printer online": clears Windows' "Use Printer Offline", directly when Windows allows it, else through an
        // elevated copy of this exe (--online; Windows asks for administrator permission). True when it worked.
        internal static async Task<bool> UseOnline(IWin32Window owner, IList<string> printers)
        {
            const string title = "QL Printer Status";
            var left = new List<string>();
            try { foreach (var p in printers) if (!Spooler.UseOnline(p)) left.Add(p); }
            catch (Exception ex)
            {
                QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            if (left.Count == 0) return true;
            string result = Path.Combine(Path.GetTempPath(), "QlLabelFormats-online-" + Guid.NewGuid().ToString("N") + ".txt");
            var psi = new ProcessStartInfo(Application.ExecutablePath,
                "--online " + string.Join(" ", left.Select(p => "\"" + p + "\"")) + " --result \"" + result + "\" --lang " + Lang.Current)
            {
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
            };
            int code;
            try
            {
                code = await Task.Run(() =>
                {
                    using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
                });
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                if (ex.NativeErrorCode != 1223)  // 1223: the user declined
                    QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            string text = Lang.T("(no result)");
            try { if (File.Exists(result)) text = File.ReadAllText(result, Encoding.UTF8).Trim(); File.Delete(result); } catch (Exception) { }
            if (code == 0) return true;
            QlMessage.Show(owner, Lang.T("The printer could not be switched online:") + "\n\n" + text, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        // Opened directly on a screen with other scaling than Windows' system DPI (the 150 % second screen): the shared
        // helper lets WinForms scale as on a move there; bold texts follow the window's font after a DPI change.
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.MatchScreenDpi(this);
            PrinterStatusProgram.Mark(this);   // the other programs find it and put it in front (ShowRunning)
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Brand.MatchScreenDpi(this);   // Load placed it on its screen
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Brand.FollowFont(this, Font);
        }

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

        // One printer: status badge, name, state, details and a link to its print queue. An error or warning shows
        // one short line ("Wrong media – the roll doesn't fit the job"); "What to do?" opens the loaded roll and the
        // advice inside the card, and they stay open while the same problem lasts.
        sealed class PrinterCard : TableLayoutPanel
        {
            public readonly string Printer;
            readonly Panel badge = new Panel();
            readonly Label state = new Label { AutoSize = true, Margin = new Padding(0, 1, 0, 0) };
            readonly Label details = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(0, 4, 0, 0) }, Role.Dim);
            readonly LinkLabel help = Theme.Link(new LinkLabel { AutoSize = true, Visible = false, Margin = new Padding(0, 4, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline }, Role.Accent);
            readonly Panel rule = Theme.Back(new Panel { Height = 1, Visible = false }, Role.SurfaceLine);   // above the opened advice
            readonly Label advice = Theme.Fore(new Label { AutoSize = true, Margin = Padding.Empty, Visible = false }, Role.Dim);
            // Under the advice of a printer share the host no longer shares: "Not needed any more? Remove the printer here".
            readonly LinkLabel removeLink = Theme.Link(Theme.Fore(new LinkLabel { AutoSize = false, Margin = Padding.Empty, Visible = false, LinkBehavior = LinkBehavior.HoverUnderline }, Role.Dim), Role.Accent);
            string openFor;      // the state line whose advice is open (null: closed)
            PrinterCheck shown;
            // Only while Windows' "Use Printer Offline" is on.
            readonly LinkLabel online = Theme.Link(new LinkLabel { AutoSize = true, Visible = false, Margin = new Padding(0, 4, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline }, Role.Accent);
            // The link row: a "|" before every visible link but the first.
            readonly List<Control> linkRow = new List<Control>();
            readonly Dictionary<Control, Label> barBefore = new Dictionary<Control, Label>();
            readonly ToolTip tips;
            Health health = Health.Info;
            // "Printer details ▸": model, connection, roll, driver; a network printer's counters (read over SNMP when
            // opened). Opens under the card like "What to do?"; a second click closes it.
            readonly LinkLabel detailsLink = Theme.Link(new LinkLabel { AutoSize = true, Margin = new Padding(0, 4, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline }, Role.Accent);
            readonly Panel detailsRule = Theme.Back(new Panel { Height = 1, Visible = false }, Role.SurfaceLine);
            readonly TableLayoutPanel detailsTable = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, Margin = Padding.Empty, Padding = Padding.Empty, Visible = false };
            bool detailsOpen;
            NetworkDetails network;   // read when the details open; null: not (yet) read
            bool readingNetwork;

            public PrinterCard(string printer, Font font, int em, int textWidth, ToolTip tips)
            {
                Printer = printer;
                this.tips = tips;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                ColumnCount = 2;
                Padding = Padding.Empty;
                badge.Size = new Size(em * 2, em * 2);
                badge.Margin = new Padding(0, 2, em * 2 / 3, 0);
                badge.Paint += PaintBadge;

                var name = new Label { Text = printer, AutoSize = true, Font = new Font(font, FontStyle.Bold), Margin = Padding.Empty, MaximumSize = new Size(textWidth, 0) };
                state.MaximumSize = details.MaximumSize = advice.MaximumSize = new Size(textWidth, 0);
                rule.Width = textWidth;
                rule.Margin = new Padding(0, em / 2, 0, em / 2);
                help.LinkClicked += delegate
                {
                    openFor = openFor == null && shown != null ? shown.Line : null;
                    if (shown != null) Display(shown);
                };
                tips.SetToolTip(help, Lang.T("The loaded roll and what to do about it."));
                state.Text = Lang.T("Reading the printer status...");
                details.Visible = false;
                var queue = Theme.Link(new LinkLabel { Text = Lang.T("Print queue"), AutoSize = true, Margin = new Padding(0, 4, 0, 0), LinkBehavior = LinkBehavior.HoverUnderline }, Role.Accent);
                queue.LinkClicked += delegate { OpenQueue(); };
                tips.SetToolTip(queue, Lang.F("Windows' print queue of {0}: see, pause or cancel print jobs.", printer));
                online.Text = Lang.T("Use printer online");
                online.LinkClicked += async (s, e) =>
                {
                    online.Enabled = false;
                    if (await UseOnline(FindForm(), new[] { Printer }))
                    {
                        var form = FindForm() as StatusForm;
                        if (form != null) form.RefreshNow();
                    }
                    online.Enabled = true;
                };
                tips.SetToolTip(online, Lang.F("Clears Windows' 'Use Printer Offline' for {0}, so waiting print jobs go to the printer. Windows may ask for administrator permission.", printer));
                badge.AccessibleName = printer;  // the badge's state is in the text beside it

                removeLink.LinkClicked += delegate { RemoveHere(); };
                // An auto-sized LinkLabel keeps the size measured with the old font after a DPI change (its link was cut
                // off at 125 %): sized here from its text instead, now and whenever the font or DPI changes.
                removeLink.FontChanged += delegate { FitRemoveLink(); };
                removeLink.DpiChangedAfterParent += delegate { FitRemoveLink(); };
                detailsLink.Text = Lang.T("Printer details") + " ▸";
                detailsLink.LinkClicked += delegate { ToggleDetails(); };
                tips.SetToolTip(detailsLink, Lang.T("Model, connection, roll and driver; for network printers also counters and firmware."));
                detailsRule.Width = textWidth;
                detailsRule.Margin = new Padding(0, em / 2, 0, em / 2);
                detailsTable.MaximumSize = new Size(textWidth, 0);

                links = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
                linkRow.AddRange(new Control[] { help, online, detailsLink, queue });
                foreach (var link in linkRow)
                {
                    if (link != help)
                    {
                        var bar = Theme.Fore(new Label { Text = "|", AutoSize = true, Margin = new Padding(em / 3, 4, em / 3, 0) }, Role.Dim);
                        barBefore[link] = bar;
                        links.Controls.Add(bar);
                    }
                    links.Controls.Add(link);
                }
                UpdateBars();
                var text = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
                text.Controls.AddRange(new Control[] { name, state, details, links, rule, advice, removeLink, detailsRule, detailsTable });
                Controls.Add(badge, 0, 0);
                Controls.Add(text, 1, 0);
            }

            void FitRemoveLink()
            {
                if (removeLink.Text.Length == 0) return;
                var size = removeLink.GetPreferredSize(new Size(advice.MaximumSize.Width, 0));   // the advice's wrap width (scaled with it)
                if (removeLink.Size != size) removeLink.Size = size;
            }

            public void Display(PrinterCheck c)
            {
                shown = c;
                health = c.Health;
                badge.Invalidate();
                bool problem = c.Health == Health.Error || c.Health == Health.Warning;
                state.Text = Fit(c.Line);
                Theme.Fore(state, c.Health == Health.Error ? Role.ErrorText : c.Health == Health.Warning ? Role.WarningText : Role.Text);
                var lines = new List<string>();
                if (c.Detail != null && !(problem && c.DetailIsTitle)) lines.Add(c.Detail);   // the state line says it
                if (c.Health != Health.Ok && c.LoadedText != null) lines.Add(Lang.T("Loaded roll:") + " " + c.LoadedText);
                lines.AddRange(c.Advice.Select(a => "•  " + a));
                string remove = c.OfferRemove ? Lang.T("Remove the printer here") : null;
                SuspendLayout();
                if (problem)
                {
                    // Another problem (or none in between): closed again.
                    if (openFor != c.Line) openFor = null;
                    bool open = openFor != null && lines.Count > 0;
                    details.Visible = false;
                    help.Text = Lang.T("What to do?") + (open ? " ▾" : " ▸");
                    helpShown = help.Visible = lines.Count > 0;
                    advice.Text = string.Join("\n", lines);
                    advice.Visible = rule.Visible = open;
                    if (remove != null)
                    {
                        removeLink.Text = "•  " + Lang.T("Not needed any more?") + " " + remove;
                        removeLink.LinkArea = new LinkArea(removeLink.Text.Length - remove.Length, remove.Length);
                        FitRemoveLink();
                    }
                    removeLink.Visible = open && remove != null;
                }
                else
                {
                    openFor = null;
                    helpShown = help.Visible = advice.Visible = rule.Visible = removeLink.Visible = false;
                    details.Text = string.Join("\n", lines);
                    details.Visible = lines.Count > 0;
                }
                onlineShown = online.Visible = c.WorkOffline;
                UpdateBars();
                ResumeLayout();
                // The raw message, for anyone who wants to know where the status came from.
                var m = c.LastMessage;
                string tip = m == null ? "" : Lang.F("Last message from the printer: {0}, {1}", m.Time.ToString("g"), m.TypeText) + "\n" + m.Hex;
                tips.SetToolTip(state, tip);
                tips.SetToolTip(badge, tip);
                if (detailsOpen) FillDetails();
            }

            // Visible as wanted (Control.Visible reads false while the window isn't shown yet).
            bool helpShown, onlineShown;
            FlowLayoutPanel links;

            void UpdateBars()
            {
                bool before = false;
                int index = 0;
                foreach (var link in linkRow)
                {
                    bool visible = link == help ? helpShown : link == online ? onlineShown : true;
                    Label bar;
                    if (barBefore.TryGetValue(link, out bar))
                    {
                        bar.Visible = visible && before;
                        Order(bar, index++);
                    }
                    Order(link, index++);
                    before |= visible;
                }
            }

            // WinForms sorts a control that was hidden by its window's z-order once it gets one: "Use printer online"
            // landed before its "|" ("online | | details", 2026-11-07). The row's order is set here each time.
            void Order(Control c, int index)
            {
                if (links != null && links.Controls.GetChildIndex(c) != index) links.Controls.SetChildIndex(c, index);
            }

            // A state line too long for one line breaks after its dash ("Not connected –" / "printer off or USB cable at
            // the PC HOST"), not before a last lone word.
            string Fit(string line)
            {
                int max = state.MaximumSize.Width, dash = line.IndexOf(" – ");
                if (max <= 0 || dash < 0 || TextRenderer.MeasureText(line, state.Font).Width <= max) return line;
                return line.Substring(0, dash) + " –\n" + line.Substring(dash + 3);
            }

            void ToggleDetails()
            {
                detailsOpen = !detailsOpen;
                detailsLink.Text = Lang.T("Printer details") + (detailsOpen ? " ▾" : " ▸");
                if (detailsOpen) { network = null; ReadNetwork(); }
                SuspendLayout();
                detailsRule.Visible = detailsTable.Visible = detailsOpen;
                if (detailsOpen) FillDetails();
                ResumeLayout();
            }

            // A network printer's counters, once per opening (not with every refresh).
            void ReadNetwork()
            {
                string port = shown != null && shown.Device != null ? shown.Device.Port : QlDevice.PortOf(Printer);
                if (readingNetwork || !NetworkStatus.IsNetworkPort(port)) return;
                readingNetwork = true;
                Task.Run(() =>
                {
                    NetworkDetails d;
                    try { d = NetworkStatus.ReadDetails(port); }
                    catch (Exception) { d = new NetworkDetails(); }
                    try
                    {
                        BeginInvoke((MethodInvoker)delegate   // back on the window's thread, whatever started it
                        {
                            readingNetwork = false;
                            if (!detailsOpen) return;
                            network = d;
                            FillDetails();
                        });
                    }
                    catch (InvalidOperationException) { }   // the window was closed meanwhile
                });
            }

            void FillDetails()
            {
                var c = shown;
                var d = c != null ? c.Device : null;
                string port = d != null && d.Port != null ? d.Port : QlDevice.PortOf(Printer);
                bool isNetwork = NetworkStatus.IsNetworkPort(port);
                var rows = new List<string[]>();   // {label, value}; {heading} for a heading; {null, text} for a line of its own
                var m = c != null ? c.LastMessage ?? c.Media : null;
                if (m != null) rows.Add(new[] { Lang.T("Model"), m.ModelName });
                string connection = Connection(d, port);
                if (connection != null) rows.Add(new[] { Lang.T("Connection"), connection });
                // A printer share's status comes from the PC it is plugged into (the connection names it): when.
                if (c != null && c.Via != null && d != null && d.State != DeviceState.NotPublished)
                    rows.Add(new[] { Lang.T("Last status"), d.PublishedAt.Value.ToString("T") });
                string loaded = c != null && c.Media != null && !c.Media.NoMedia ? c.LoadedText : null;
                if (loaded != null) rows.Add(new[] { Lang.T("Loaded roll"), loaded });
                string driver = Versions.InstalledDriver();
                if (driver != null) rows.Add(new[] { Lang.T("Driver"), driver });
                if (!isNetwork) rows.Add(new[] { null, Lang.T("Counters such as pages and cuts come only from network printers.") });
                else if (network == null) rows.Add(new[] { null, Lang.T("Reading the counters...") });
                else if (!network.Answered) rows.Add(new[] { null, Lang.T("The printer doesn't answer: no counters.") });
                else
                {
                    var counters = new List<string[]>();
                    if (network.Pages != null) counters.Add(new[] { Lang.T("Pages printed"), network.Pages.Value.ToString("N0") });
                    if (network.Cuts != null) counters.Add(new[] { Lang.T("Cuts"), network.Cuts.Value.ToString("N0") });
                    if (network.Metres != null) counters.Add(new[] { Lang.T("Length printed"), network.Metres.Value.ToString("N0") + " m" });
                    var device = new List<string[]>();
                    if (network.Firmware != null) device.Add(new[] { Lang.T("Firmware"), network.Firmware });
                    if (network.NetworkFirmware != null) device.Add(new[] { Lang.T("Network firmware"), network.NetworkFirmware });
                    if (network.MemoryKb != null)
                        device.Add(new[] { Lang.T("Memory"), network.MemoryKb.Value % 1024 == 0 ? (network.MemoryKb.Value / 1024) + " MB" : network.MemoryKb.Value.ToString("N0") + " KB" });
                    if (network.Serial != null) device.Add(new[] { Lang.T("Serial number"), network.Serial });   // shown only, never logged
                    if (counters.Count > 0) { rows.Add(new[] { Lang.T("Counters") }); rows.AddRange(counters); }
                    if (device.Count > 0) { rows.Add(new[] { Lang.T("Device") }); rows.AddRange(device); }
                    if (counters.Count + device.Count == 0) rows.Add(new[] { null, Lang.T("This printer reports no counters.") });
                }
                // Same rows as shown: leave the controls alone (no flicker with every refresh).
                string key = string.Join("\n", rows.Select(r => string.Join("\t", r)));
                if ((string)detailsTable.Tag == key) return;
                detailsTable.Tag = key;
                detailsTable.SuspendLayout();
                foreach (Control old in detailsTable.Controls.Cast<Control>().ToList()) old.Dispose();
                detailsTable.Controls.Clear();
                detailsTable.RowStyles.Clear();
                detailsTable.RowCount = rows.Count;
                int width = detailsTable.MaximumSize.Width, em = Font.Height;   // the window's font now (it follows a DPI change)
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    if (r.Length == 1 || r[0] == null)
                    {
                        bool heading = r.Length == 1;
                        var l = Theme.Fore(new Label { Text = heading ? r[0] : r[1], AutoSize = true, MaximumSize = new Size(width, 0),
                                                       Margin = new Padding(0, heading ? em / 2 : (i == 0 ? 0 : em / 3), 0, 0) }, Role.Dim);
                        if (heading) l.Font = new Font(Font, FontStyle.Bold);
                        detailsTable.Controls.Add(l, 0, i);
                        detailsTable.SetColumnSpan(l, 2);
                    }
                    else
                    {
                        detailsTable.Controls.Add(Theme.Fore(new Label { Text = r[0], AutoSize = true, Margin = new Padding(0, 0, em, 1) }, Role.Dim), 0, i);
                        detailsTable.Controls.Add(new Label { Text = r[1], AutoSize = true, MaximumSize = new Size(width * 3 / 5, 0), Margin = new Padding(0, 0, 0, 1) }, 1, i);
                    }
                }
                detailsTable.ResumeLayout();
            }

            static string Connection(DeviceResult d, string port)
            {
                string host, share;
                if (d != null && d.Network != null) return Lang.F("Network ({0})", d.Network);
                string h;
                if (NetworkStatus.TryPort(port, out h, out share)) return Lang.F("Network ({0})", h);
                if (d != null && PublishedStatus.IsShare(d.Source, out host, out share)) return Lang.F("Shared by {0}", host);
                if (PublishedStatus.IsShare(port, out host, out share)) return Lang.F("Shared by {0}", host);
                if (port != null && QlDevice.IsUsbPort(port)) return "USB (" + port + ")";
                return string.IsNullOrEmpty(port) ? null : port;
            }

            // Printer setup with this printer selected; removing it stays the user's click there (one question, one
            // administrator prompt: the setup's own remove).
            void RemoveHere()
            {
                try
                {
                    string exe = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "QlLabelFormats.exe");
                    Process.Start(new ProcessStartInfo(exe, "--setup --select \"" + Printer + "\"") { UseShellExecute = true });
                }
                catch (Exception ex) { QlMessage.Ask(FindForm(), "QL Printer Status", Lang.T("Printer setup couldn't be opened."), null, MessageBoxButtons.OK,
                                                     MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message); }
            }

            void OpenQueue()
            {
                try { Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /o /n \"" + Printer + "\""); }
                catch (Exception ex) { QlMessage.Ask(FindForm(), "QL Printer Status", Lang.T("The print queue couldn't be opened."), null, MessageBoxButtons.OK,
                                                     MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message); }
            }

            void PaintBadge(object sender, PaintEventArgs e)
            {
                // The state colours of the tray icons (Brand); Busy is petrol like Ok, told apart by its glyph.
                Color color = Brand.HealthColor(health);
                string glyph;
                switch (health)
                {
                    case Health.Ok: glyph = "✓"; break;
                    case Health.Busy: glyph = "…"; break;
                    case Health.Warning: case Health.Error: glyph = "!"; break;
                    default: glyph = "i"; break;
                }
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var r = new Rectangle(1, 1, badge.Width - 3, badge.Height - 3);
                using (var b = new SolidBrush(color)) g.FillEllipse(b, r);
                if (glyph == "!")
                {
                    // Drawn as a rounded bar and a dot in proportion to the circle (the font's "!" hardly grew at 150 %).
                    float d = r.Width, cx = r.X + d / 2f, w = d * 0.13f;
                    float top = r.Y + d * 0.24f, barBottom = r.Y + d * 0.60f, dotY = r.Y + d * 0.68f;
                    using (var p = new System.Drawing.Drawing2D.GraphicsPath())
                    {
                        p.AddArc(cx - w / 2, top, w, w, 180, 180);
                        p.AddLine(cx + w / 2, top + w / 2, cx + w * 0.35f, barBottom);
                        p.AddLine(cx - w * 0.35f, barBottom, cx - w / 2, top + w / 2);
                        p.CloseFigure();
                        using (var white = new SolidBrush(Theme.Fixed.StateSymbol)) g.FillPath(white, p);
                    }
                    using (var white = new SolidBrush(Theme.Fixed.StateSymbol)) g.FillEllipse(white, cx - w * 0.6f, dotY, w * 1.2f, w * 1.2f);
                    return;
                }
                using (var f = new Font("Segoe UI", badge.Height * 0.36f, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    using (var white = new SolidBrush(Theme.Fixed.StateSymbol)) g.DrawString(glyph, f, white, r, sf);
                }
            }
        }
    }
}
