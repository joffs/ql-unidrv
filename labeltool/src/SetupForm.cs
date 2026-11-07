// "Printer setup" window (QlLabelFormats.exe --setup; QL Label Formats' Printer status menu; Windows' Installed apps >
// Modify): the everyday setup tasks without the release zip. Tabs: add a QL printer on the network (model asked from
// the printer) or one shared by another PC, share one of this PC's USB printers (+ the status service), the driver
// log on/off; uninstall.
// Each change runs the installed install.ps1 -Settings as administrator (C:\Program Files\QLUnidrv\setup, copied
// there by every install); updating or repairing the driver needs the new package, so that stays with the
// release zip's Setup.exe. docs/setup.md.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class SetupForm : Form
    {
        static string AskPrinter { get { return Lang.T("(ask the printer)"); } }
        const string ReleasesUrl = "https://github.com/joffs/ql-unidrv/releases";
        static readonly string SetupDir = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "setup");
        static readonly string PluginLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"QLUnidrv\log\plugin.log");

        readonly TextBox address = new TextBox();
        readonly ComboBox netModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox share = new TextBox();
        readonly ComboBox shareModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly CheckBox service = new CheckBox { Text = Lang.T("Printer status service for shared printers"), AutoSize = true, FlatStyle = FlatStyle.System };
        readonly CheckBox log = new CheckBox { Text = Lang.T("Driver log for troubleshooting (C:\\ProgramData\\QLUnidrv\\log\\plugin.log)"), AutoSize = true, FlatStyle = FlatStyle.System };
        readonly Label printers = new Label { AutoSize = true };
        readonly ListBox printerList = new ListBox { IntegralHeight = true, SelectionMode = SelectionMode.One, BorderStyle = BorderStyle.FixedSingle };
        readonly List<string> listed = new List<string>();  // the printer names of printerList's rows
        readonly FlowLayoutPanel printerButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        Button removeButton, propertiesButton;
        readonly FlowLayoutPanel usbRows = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        readonly List<Control[]> usbTexts = new List<Control[]>();  // per line: its text column, the icon, the button
        readonly Label status = new Label { AutoSize = true };
        // The last change's details (a printer still pending deletion) in an (i) beside it, not as lines of their own
        readonly FlowLayoutPanel statusRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Visible = false };
        Control statusInfo;
        string statusDetails;
        readonly HeaderBand header = new HeaderBand { Margin = Padding.Empty };
        // The tabs below the list and their pages (one visible), the "Share a printer" tab's controls and state
        readonly TabBar tabs = new TabBar();
        readonly Panel pageHost = new Panel();
        readonly List<FlowLayoutPanel> pages = new List<FlowLayoutPanel>();
        readonly FlowLayoutPanel reachRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Visible = false };
        readonly Label reachText = new Label { AutoSize = true };
        readonly LinkLabel reachLink = new LinkLabel { AutoSize = true, Text = Lang.T("Open settings") };
        Label shareHint, printerLabel, nameLabel;
        FlowLayoutPanel sharePrinterRow, shareNameRow;
        readonly ComboBox shareList = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly TextBox shareNameBox = new TextBox();
        Button shareButton, renameButton, unshareButton;
        readonly Label shareProblem = new Label { AutoSize = true, Visible = false };
        readonly Label noShareable = new Label { AutoSize = true, Visible = false };
        List<ShareablePrinter> shareables = new List<ShareablePrinter>();
        Dictionary<string, string> takenNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SharingReach reach;
        // The content between the band and the bottom row: scrolls only when the window would be taller than the screen's
        // working area (FitHeight)
        readonly Panel middle = new Panel { Margin = Padding.Empty };
        TableLayoutPanel root;
        bool loading;

        public SetupForm()
        {
            Text = Lang.T("QL Printer Setup");
            Theme.Use(this);
            HelpPages.F1(this, HelpPages.Setup);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterScreen;
            int em = Font.Height, wide = em * 30;

            header.Text = Lang.T("Set up QL printers");
            // Everything starts on the window's left line (Brand.Gutter), with more whitespace (user's choice 2026-10-30)
            int gutter = Brand.Gutter(em);
            root = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(gutter, em * 5 / 4, gutter, 0), Margin = Padding.Empty };
            printers.MaximumSize = new Size(wide, 0);
            printers.Margin = new Padding(0, 0, 0, 0);  // the first element of a line starts on the left line: no default margin
            root.Controls.Add(printers);
            printerList.Width = wide;
            printerList.Margin = new Padding(0, em / 4, 0, 0);
            root.Controls.Add(printerList);
            // Below the list: what can be done with the selected printer (grey until one is selected); the same in the
            // list's context menu, Del removes, a double-click opens the properties (user's choice 2026-11-05)
            removeButton = Action(Lang.T("Remove printer"), RemovePrinter);
            removeButton.Margin = new Padding(0, 3, em / 2, 3);
            printerButtons.Margin = new Padding(0, em / 3, 0, 0);
            propertiesButton = Action(Lang.T("Printer properties"), PrinterProperties);
            propertiesButton.Margin = new Padding(0, 3, 0, 3);
            printerButtons.Controls.Add(removeButton);
            printerButtons.Controls.Add(propertiesButton);
            root.Controls.Add(printerButtons);
            // A QL printer connected by USB without a printer of ours (after "Remove printer" Windows doesn't add one
            // again): a line each with "Add printer", only then (user's choice 2026-11-06)
            usbRows.Margin = new Padding(0, em / 3, 0, 0);
            root.Controls.Add(usbRows);
            var menu = new ContextMenuStrip();
            menu.Items.Add(Lang.T("Printer properties"), null, delegate { PrinterProperties(); });
            menu.Items.Add(Lang.T("Remove"), null, delegate { RemovePrinter(); });
            printerList.DoubleClick += delegate { PrinterProperties(); };
            printerList.ContextMenuStrip = menu;
            menu.Opening += (s, e) => e.Cancel = SelectedPrinter() == null;
            printerList.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                int i = printerList.IndexFromPoint(e.Location);
                if (i >= 0) printerList.SelectedIndex = i;
            };
            printerList.KeyDown += (s, e) => { if (e.KeyCode == Keys.Delete && SelectedPrinter() != null) { e.Handled = true; RemovePrinter(); } };
            printerList.SelectedIndexChanged += delegate { removeButton.Enabled = propertiesButton.Enabled = SelectedPrinter() != null; };

            // Below: adding a printer (network, another PC's share), sharing one of this PC's and the options as tabs, one
            // visible at a time (the window's height was always the bottleneck; user's choice 2026-11-06). The pages
            // share one place as tall as the tallest, so the window keeps its height; the last tab used is remembered.
            tabs.Margin = new Padding(0, em * 3 / 4, 0, 0);
            tabs.Add(Lang.T("Network printer"));
            tabs.Add(Lang.T("Another PC's printer"));
            tabs.Add(Lang.T("Share a printer"));
            tabs.Add(Lang.T("Options"));
            root.Controls.Add(tabs);
            pageHost.Margin = new Padding(0, em * 2 / 3, 0, 0);
            root.Controls.Add(pageHost);

            // A printer on the network
            var net = Page();
            net.Controls.Add(Hint(Lang.T("The printer tells its model; choose it only if it doesn't answer."), wide,
                                  Lang.T("The IP address is on the configuration label the printer prints, or in its network settings. The window asks " +
                                         "the printer for its model; if it doesn't answer, choose it yourself.")));
            var netRow = Row(em);
            address.Width = em * 10;
            netModel.Width = em * 13;
            netRow.Controls.AddRange(new Control[] { new Label { Text = Lang.T("IP address:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 3, 3) }, address,
                                                     new Label { Text = Lang.T("Model:"), AutoSize = true, Anchor = AnchorStyles.Left }, netModel,
                                                     Action(Lang.T("Add"), AddNetworkPrinter) });
            net.Controls.Add(netRow);

            // A printer shared by another PC
            var sh = Page();
            sh.Controls.Add(Hint(Lang.T("First share it on the other PC; then enter \\\\PC\\share name."), wide,
                                 Lang.T("First share the printer on the PC it is plugged into (QL Printer Setup > Share a printer). Then open " +
                                        "\\\\<other PC's name> once in Explorer on this PC and sign in with \"Remember my credentials\".") + "\r\n\r\n" +
                                 Lang.T("Test label and roll display need the printer status service on the other PC.") + " " +
                                 Lang.T("To turn it on, on the other PC: \"QL Printer Setup\" > \"Share a printer\" > " +
                                        "\"Printer status service for shared printers\". Printing works without it too.")));
            var shRow = Row(em);
            share.Width = em * 10;
            share.TextChanged += delegate { GuessShareModel(); };
            shareModel.Width = em * 13;
            shRow.Controls.AddRange(new Control[] { new Label { Text = Lang.T("Share:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 3, 3) }, share,
                                                    new Label { Text = Lang.T("Model:"), AutoSize = true, Anchor = AnchorStyles.Left }, shareModel,
                                                    Action(Lang.T("Add"), AddSharedPrinter) });
            sh.Controls.Add(shRow);

            // Share one of this PC's QL printers on USB (user's choice 2026-11-06): printer, share name, Share or Rename /
            // Stop sharing; the status service belongs here (only shares need it)
            var sp = Page();
            reachRow.Margin = new Padding(0, 0, 0, em / 3);
            Theme.Fore(reachText, Role.WarningText);
            reachText.Margin = new Padding(0, 1, em / 3, 0);
            Brand.StyleLink(reachLink);
            reachLink.LinkClicked += delegate { OpenSharingSettings(); };
            reachRow.Controls.Add(reachText);
            reachRow.Controls.Add(reachLink);
            sp.Controls.Add(reachRow);
            var shareHintRow = Hint(" ", wide, null);
            shareHint = (Label)shareHintRow.Controls[0];
            InfoTip.Attach(shareHintRow.Controls[1], () => ShareDetails(), hover: true);
            sp.Controls.Add(shareHintRow);
            printerLabel = new Label { Text = Lang.T("Printer:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 3, 3) };
            nameLabel = new Label { Text = Lang.T("Share name:"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 3, 3) };
            sharePrinterRow = Row(em);
            sharePrinterRow.Margin = Padding.Empty;
            shareList.Width = em * 21;
            shareList.SelectedIndexChanged += delegate { if (!loading) ShareChoiceChanged(); };
            sharePrinterRow.Controls.AddRange(new Control[] { printerLabel, shareList });
            sp.Controls.Add(sharePrinterRow);
            shareNameRow = Row(em);
            shareNameRow.Margin = new Padding(0, em / 3, 0, 0);
            shareNameBox.Width = em * 10;
            shareNameBox.TextChanged += delegate { UpdateShareButtons(); };
            shareButton = Action(Lang.T("Share printer"), SharePrinter);
            renameButton = Action(Lang.T("Rename"), RenameShare);
            unshareButton = Action(Lang.T("Stop sharing"), StopSharing);
            shareNameRow.Controls.AddRange(new Control[] { nameLabel, shareNameBox, shareButton, renameButton, unshareButton });
            sp.Controls.Add(shareNameRow);
            Theme.Fore(shareProblem, Role.ErrorText);
            sp.Controls.Add(shareProblem);
            noShareable.MaximumSize = new Size(wide, 0);
            // Lines that wrap: never wider than the other tabs' rows (FitWidths sets them to the window's width)
            shareProblem.MaximumSize = new Size(em * 18, 0);
            reachText.MaximumSize = new Size(em * 18, 0);
            noShareable.Margin = new Padding(0, 1, 0, 0);
            Theme.Fore(noShareable, Role.Dim);
            sp.Controls.Add(noShareable);
            service.Margin = log.Margin = new Padding(0, 3, 3, 3);
            var serviceRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, em / 2, 0, 0) };
            var serviceInfo = StatusForm.InfoIcon(em);
            serviceInfo.Cursor = Cursors.Hand;
            serviceInfo.Anchor = AnchorStyles.Left;
            InfoTip.Attach(serviceInfo, () => Lang.T("Only needed when other PCs print to this PC's printers.") + " " +
                                              Lang.T("A Windows service asks this PC's printers every 3 seconds. That way PCs printing through a share also see " +
                                                     "when a printer needs you."), hover: true);
            serviceRow.Controls.Add(service);
            serviceRow.Controls.Add(serviceInfo);
            sp.Controls.Add(serviceRow);
            service.CheckedChanged += delegate
            {
                if (!loading) Option("-StatusService " + (service.Checked ? "on" : "off"), service.Checked ? Lang.T("Turning the service on...") : Lang.T("Turning the service off..."));
            };

            // Options
            var opt = Page();
            opt.Controls.Add(log);
            log.CheckedChanged += delegate
            {
                if (!loading) Option("-Logging " + (log.Checked ? "on" : "off"), log.Checked ? Lang.T("Turning the driver log on...") : Lang.T("Turning the driver log off..."));
            };
            tabs.SelectedChanged += delegate { ShowPage(); SaveTab(); };
            tabs.Selected = LoadTab();
            ShowPage();

            // The last change in one line, only after one (each change also shows its own result dialog)
            status.MaximumSize = new Size(wide - em * 2, 0);
            status.Margin = Padding.Empty;
            statusRow.Margin = new Padding(0, em * 2 / 3, 0, 0);
            statusInfo = StatusForm.InfoIcon(em);
            statusInfo.Cursor = Cursors.Hand;
            statusInfo.Visible = false;
            InfoTip.Attach(statusInfo, () => statusDetails, hover: true);
            statusRow.Controls.Add(status);
            statusRow.Controls.Add(statusInfo);
            root.Controls.Add(statusRow);

            // Bottom: Uninstall, the newest version (how to repair in its (i)) and a clear way out on the right. The
            // section "Update, repair, remove" went for a window that fits 1080p at 150 % (user's choice 2026-10-31).
            var bottom = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill, Padding = new Padding(gutter, em * 5 / 4, gutter, em * 5 / 4), Margin = Padding.Empty };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var uninstall = Action(Lang.T("Uninstall"), Uninstall);
            uninstall.Anchor = AnchorStyles.Left;
            uninstall.Margin = new Padding(0, 3, em, 3);
            string page = Lang.T("download page");
            var link = new LinkLabel { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 1, 0, 0), Text = Lang.F("New version: {0}", page) };
            link.LinkArea = new LinkArea(link.Text.LastIndexOf(page, StringComparison.Ordinal), page.Length);
            Brand.StyleLink(link);
            link.LinkClicked += delegate { Process.Start(ReleasesUrl); };
            var repairInfo = StatusForm.InfoIcon(em);
            InfoTip.Attach(repairInfo, () => Lang.T("To repair: start Setup.exe from the unzipped download again. It checks this PC and installs the driver and the programs anew; printers, settings and label formats stay."), hover: true);
            var update = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            update.Controls.Add(link);
            update.Controls.Add(repairInfo);
            var close = new Button { Text = Lang.T("Done"), AutoSize = true, MinimumSize = new Size(em * 7, em * 2), Anchor = AnchorStyles.Right };
            Brand.StylePrimary(close);
            close.Click += delegate { Close(); };  // DialogResult alone closes only a dialog, not the --setup window
            bottom.Controls.Add(uninstall, 0, 0);
            bottom.Controls.Add(update, 1, 0);
            bottom.Controls.Add(close, 2, 0);
            CancelButton = close;
            // The petrol band on top (a page-style dialog), the content, the bottom row.
            middle.Controls.Add(root);
            var frame = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            frame.Controls.Add(header);
            frame.Controls.Add(middle);
            frame.Controls.Add(bottom);
            Controls.Add(frame);
            LoadState();
        }

        // Opened directly on a second screen with other scaling: the handle exists early (the caption tint) and the window
        // is placed on its screen only before it shows, without the message a move would bring; so the check runs here too
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.MatchScreenDpi(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            Brand.MatchScreenDpi(this);
            FitWidths();
            base.OnLoad(e);
            SizeList();
            FitHeight();
            AfterList();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Brand.FollowFont(this, Font);
            FitWidths();
            SizeList();
            FitHeight();
        }

        // Every printer shown, at most 5 rows (more scroll): from the list's real item height and border, known only once
        // the window has its font and scaling (a fixed "+ 4" left one row hidden behind a scroll bar)
        void SizeList(int maxRows = 8)
        {
            printerList.Height = printerList.ItemHeight * Math.Min(Math.Max(listed.Count, 1), maxRows) + (printerList.Height - printerList.ClientSize.Height);
        }

        // Screenshot tools: a working area this tall instead of the screen's, to show the scrolling below (0 = the screen's)
        internal static int PreviewWorkingHeight { get; set; }

        // Taller than the screen's working area (e.g. 200 % on 1080p): the middle scrolls, the band and the bottom row stay
        // (user's choice 2026-10-31; every window fits a 690 px working area at 100 %, this one is the safety net)
        void FitHeight()
        {
            // The 690 px rule: the printer list shows fewer rows (at least 2; it scrolls) when its 5 would make the window
            // taller, e.g. 5 printers at 175 % (2026-11-05). Otherwise nothing changes.
            if (IsHandleCreated)
            {
                SizeList();
                int rows = Math.Min(Math.Max(listed.Count, 1), 8);
                int over = (Height - middle.Height) + root.GetPreferredSize(Size.Empty).Height - 690 * DeviceDpi / 96;
                // At least 4 rows: fewer make the list worthless (user's choice 2026-11-06); room comes from the compact
                // USB line and hints in (i) icons
                const int least = 4;
                if (over > 0 && rows > least) SizeList(Math.Max(least, rows - (over + printerList.ItemHeight - 1) / printerList.ItemHeight));
            }
            var content = root.GetPreferredSize(Size.Empty);
            var area = Screen.FromControl(this).WorkingArea;
            if (PreviewWorkingHeight > 0) area.Height = PreviewWorkingHeight;
            int room = area.Height - (Height - middle.Height);  // the band, the bottom row and the frame take the rest
            middle.AutoScroll = false;
            if (IsHandleCreated && content.Height > room)
            {
                middle.Size = new Size(Math.Max(content.Width, root.Width) + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) + 2, Math.Max(room, Font.Height * 4));
                // up and down only (the content is as wide as the window)
                middle.HorizontalScroll.Maximum = 0;
                middle.HorizontalScroll.Visible = false;
                middle.AutoScroll = true;
            }
            else middle.Size = content;
            if (!IsHandleCreated) return;
            if (PreviewWorkingHeight == 0 && (Bottom > area.Bottom || Top < area.Top)) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        // The sections' hairlines and the printer list run as wide as the widest row (its Add button), not to a fixed
        // width short of it (user's choice 2026-10-30)
        readonly List<Panel> rules = new List<Panel>();
        readonly List<FlowLayoutPanel> inners = new List<FlowLayoutPanel>();
        readonly List<Label> hints = new List<Label>();

        void FitWidths()
        {
            // The share tab's stretching parts follow the width; measured small, they don't set it
            int em = Font.Height;
            shareList.Width = em * 12;
            foreach (var l in new Control[] { shareProblem, reachText, noShareable }) l.MaximumSize = new Size(em * 12, 0);
            int width = inners.Count == 0 ? 0 : inners.Max(i => i.GetPreferredSize(Size.Empty).Width);
            if (width <= 0) return;
            foreach (var rule in rules) rule.Width = width;
            printerList.Width = width;
            FitUsbRows(width);
            // The share tab's two labels as one column, the problem line under the field (widths change with the scaling)
            printerLabel.MinimumSize = nameLabel.MinimumSize = Size.Empty;
            int labelWidth = Math.Max(printerLabel.PreferredWidth, nameLabel.PreferredWidth);
            printerLabel.MinimumSize = nameLabel.MinimumSize = new Size(labelWidth, 0);
            shareProblem.Margin = new Padding(labelWidth + nameLabel.Margin.Horizontal + shareNameBox.Margin.Left, Font.Height / 4, 0, 0);
            shareProblem.MaximumSize = new Size(Math.Max(Font.Height * 10, width - shareProblem.Margin.Left), 0);
            reachText.MaximumSize = new Size(Math.Max(Font.Height * 10, width - reachLink.PreferredWidth - reachText.Margin.Horizontal - reachLink.Margin.Horizontal), 0);
            noShareable.MaximumSize = new Size(width, 0);
            // The printer list's choice runs to the right edge (its rows say how a printer is shared)
            shareList.Width = width - labelWidth - nameLabel.Margin.Horizontal - shareList.Margin.Horizontal;
            FitPages(width);
            // The last change's line as wide as the list, its (i) included (one line where it fits)
            status.MaximumSize = new Size(width - statusInfo.Width - statusInfo.Margin.Horizontal - status.Margin.Horizontal, 0);
            // A hint may use the section's whole width: text grows a little faster than the em at 150 % and 175 %, and a
            // German hint wrapped to a second line there (window over 690 px, ui_sweep 2026-11-05). At 100 % nothing changes.
            foreach (var l in hints)
            {
                var row = l.Parent;
                if (row == null || row.Controls.Count < 2) continue;
                var icon = row.Controls[1];
                int room = width - row.Margin.Left - icon.Width - icon.Margin.Horizontal - l.Margin.Horizontal;
                if (room > l.MaximumSize.Width) l.MaximumSize = new Size(room, 0);
            }
        }

        // ---- Tabs (user's choice 2026-11-06) ----

        // A tab's page: its content top to bottom, on the window's left line; as wide as the widest row (FitWidths)
        FlowLayoutPanel Page()
        {
            var page = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Location = Point.Empty };
            pages.Add(page);
            inners.Add(page);
            pageHost.Controls.Add(page);
            return page;
        }

        void ShowPage()
        {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = i == tabs.Selected;
        }

        // The pages' place: as tall as the tallest page, so switching tabs doesn't change the window's height
        void FitPages(int width)
        {
            int height = pages.Count == 0 ? 0 : pages.Max(p => p.GetPreferredSize(Size.Empty).Height);
            pageHost.Size = new Size(width, height);
            tabs.Size = new Size(width, tabs.GetPreferredSize(Size.Empty).Height);
        }

        // UI tests and screenshot tools: tabs chosen there aren't remembered for the user
        internal static bool NoSaveTab { get; set; }

        // The printer chosen in the list when the window opens (--setup --select "<printer>"), or null
        internal string SelectOnOpen { get; set; }

        // A printer on \\host\share whose share is gone, or whose host doesn't answer, prints into nothing (status matrix
        // case 5/7, 2026-11-06): its line says so. Checked in the background (an offline host takes up to about 20 s),
        // at most ShareCheckLimit; the same check as the status window's card (PublishedStatus.CheckShare), so the two agree.
        internal static int ShareCheckLimit = 5000;
        int shareCheckRound;

        // The list's printers and their ports; tests replace them (setup_open.cs)
        internal static Func<List<string>> ListPrinters = MainForm.OurPrinters;
        internal static Func<string, string> PortOfPrinter = QlDevice.PortOf;

        // After the list is filled, once the window has its handle: the printer asked for chosen (once), shares checked
        void AfterList()
        {
            if (SelectOnOpen != null)
            {
                int i = listed.FindIndex(n => string.Equals(n, SelectOnOpen, StringComparison.OrdinalIgnoreCase));
                SelectOnOpen = null;
                if (i >= 0) { printerList.SelectedIndex = i; ActiveControl = printerList; }
            }
            CheckShares();
        }

        void CheckShares()
        {
            int round = ++shareCheckRound;
            foreach (string printer in listed.ToList())
            {
                string port = PortOfPrinter(printer), host, share;
                if (!PublishedStatus.IsShare(port, out host, out share)) continue;
                Task.Run(() =>
                {
                    var state = ShareStateWithin(host, share, ShareCheckLimit);
                    try { BeginInvoke((Action)(() => MarkShare(round, printer, port, state))); }
                    catch (Exception) { }   // the window closed meanwhile
                });
            }
        }

        // The share's state, or Unreachable when the check takes longer than ms (it goes on alone and fills the cache)
        internal static PublishedStatus.ShareState ShareStateWithin(string host, string share, int ms)
        {
            var check = Task.Run(() => { string error; return PublishedStatus.CheckShare(host, share, out error); });
            try { return check.Wait(ms) ? check.Result : PublishedStatus.ShareState.Unreachable; }
            catch (Exception) { return PublishedStatus.ShareState.Unknown; }
        }

        // The mark after the port, or null for none (shared, or not known: no guess)
        internal static string ShareMark(PublishedStatus.ShareState state)
        {
            return state == PublishedStatus.ShareState.NotShared ? Lang.T("no longer shared")
                 : state == PublishedStatus.ShareState.Unreachable ? Lang.T("not reachable") : null;
        }

        void MarkShare(int round, string printer, string port, PublishedStatus.ShareState state)
        {
            string mark = ShareMark(state);
            int i = listed.IndexOf(printer);
            if (IsDisposed || round != shareCheckRound || mark == null || i < 0) return;
            int selected = printerList.SelectedIndex, top = printerList.TopIndex;
            printerList.Items[i] = printer + "   (" + port + " \u2013 " + mark + ")";
            if (printerList.SelectedIndex != selected) printerList.SelectedIndex = selected;
            printerList.TopIndex = top;
        }

        // The last tab used, per user (HKCU\Software\QLUnidrv, SetupTab)
        static int LoadTab()
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Lang.SettingsKey)) return k == null ? 0 : Convert.ToInt32(k.GetValue("SetupTab", 0)); }
            catch (Exception) { return 0; }
        }

        void SaveTab()
        {
            if (loading || !Visible || NoSaveTab || InstallForm.Preview) return;
            try { using (var k = Registry.CurrentUser.CreateSubKey(Lang.SettingsKey)) k.SetValue("SetupTab", tabs.Selected, RegistryValueKind.DWord); }
            catch (Exception) { }
        }

        // Ctrl+Tab / Ctrl+Shift+Tab: the next / previous tab, from anywhere in the window (as in Windows' tabbed dialogs)
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.Shift | Keys.Tab))
            {
                int step = (keyData & Keys.Shift) != 0 ? tabs.Count - 1 : 1;
                tabs.Selected = (tabs.Selected + step) % tabs.Count;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- Share a printer ----

        // The printers that can be shared (ours on USB), the share names in use here and whether other PCs can reach
        // this PC's shares; the tab follows. LoadState passes the real ones, the UI tests made-up ones.
        internal void ShowSharePage(List<ShareablePrinter> printers, Dictionary<string, string> taken, SharingReach reachable)
        {
            bool was = loading;
            loading = true;
            string keep = SelectedShareable() != null ? SelectedShareable().Name : null;
            shareables = printers;
            takenNames = taken;
            reach = reachable;
            shareList.Items.Clear();
            foreach (var p in printers)
                shareList.Items.Add(p.Name + "   (" + p.Port + (p.ShareName != null ? ", " + Lang.F("shared as {0}", p.ShareName) : "") + ")");
            int pick = printers.FindIndex(p => p.Name == keep);
            if (pick < 0) pick = printers.FindIndex(p => p.ShareName == null);   // the first one not shared yet
            if (pick < 0 && printers.Count > 0) pick = 0;
            shareList.SelectedIndex = pick;
            loading = was;
            bool any = printers.Count > 0;
            sharePrinterRow.Visible = shareNameRow.Visible = any;
            noShareable.Visible = !any;
            noShareable.Text = Lang.T("A QL printer connected to this PC by USB can be shared here. There is none right now.");
            reachText.Text = reachable == SharingReach.FirewallOffPublic ? Lang.T("This network is set to public: other PCs can't reach the share.")
                                                                         : Lang.T("Windows' file and printer sharing is off: other PCs can't reach the share.");
            reachRow.Visible = any && reachable != SharingReach.Ok;
            ShareChoiceChanged();
        }

        ShareablePrinter SelectedShareable()
        {
            int i = shareList.SelectedIndex;
            return i >= 0 && i < shareables.Count ? shareables[i] : null;
        }

        void ShareChoiceChanged()
        {
            var p = SelectedShareable();
            if (p != null) shareNameBox.Text = p.ShareName ?? Sharing.Suggest(p.Model != null ? p.Model.Name : "QL", p.Name, takenNames);
            bool shared = p != null && p.ShareName != null;
            shareHint.Text = shared ? Lang.F("Shared as \\\\{0}\\{1}.", Environment.MachineName, p.ShareName)
                                    : Lang.F("Other PCs then print through \\\\{0}\\share name.", Environment.MachineName);
            shareButton.Visible = !shared;
            renameButton.Visible = unshareButton.Visible = shared;
            UpdateShareButtons();
            if (IsHandleCreated) { FitWidths(); FitHeight(); }
        }

        // Share / Rename only with a usable name (Rename: a different one); the reason below the row
        void UpdateShareButtons()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            string problem = p == null ? "" : Sharing.NameProblem(name, p.Name, takenNames);
            shareButton.Enabled = p != null && problem == null;
            renameButton.Enabled = p != null && problem == null && !string.Equals(name, p.ShareName, StringComparison.Ordinal);
            bool show = !string.IsNullOrEmpty(problem);
            shareProblem.Text = show ? problem : "";
            if (shareProblem.Visible != show) { shareProblem.Visible = show; if (IsHandleCreated) FitHeight(); }
        }

        string ShareDetails()
        {
            var p = SelectedShareable();
            string where = "\\\\" + Environment.MachineName + "\\" + (p != null && p.ShareName != null ? p.ShareName : shareNameBox.Text.Trim());
            return Lang.F("On the other PC: \"QL Printer Setup\" > \"Another PC's printer\", enter {0}.", where) + " " +
                   Lang.T("Windows' file and printer sharing must be on here (Settings > Network > Advanced network settings > Advanced sharing settings).") +
                   "\r\n\r\n" + Lang.T("Network printers are better added by their address on every PC; shares of other PCs and output files can't be passed on.");
        }

        void OpenSharingSettings()
        {
            try
            {
                if (reach == SharingReach.FirewallOffPublic) Process.Start("ms-settings:network-status");
                else Process.Start("control.exe", "/name Microsoft.NetworkAndSharingCenter /page Advanced");
            }
            catch (Exception ex) { Say(ex.Message); }
        }

        // The printer's and the share's names go to the elevated script as quoted arguments
        static bool Quotable(string s) { return s.IndexOf('"') < 0; }

        void SharePrinter()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            if (p == null || Sharing.NameProblem(name, p.Name, takenNames) != null || !Quotable(p.Name)) return;
            var r = RunSettings("-SharePrinter \"" + p.Name + "\" -ShareName \"" + name + "\"", Lang.T("Sharing the printer..."));
            if (r == null) return;
            ShowResult(ResultKind.Ok, Lang.T("Printer shared"),
                       Fields(Lang.T("Name"), p.Name, Lang.T("Share"), "\\\\" + Environment.MachineName + "\\" + Get(r, "sharename")),
                       Lang.F("On the other PC: \"QL Printer Setup\" > \"Another PC's printer\", enter {0}.", "\\\\" + Environment.MachineName + "\\" + Get(r, "sharename")) +
                       (reach == SharingReach.Ok ? "" : " " + reachText.Text));
        }

        void RenameShare()
        {
            var p = SelectedShareable();
            string name = shareNameBox.Text.Trim();
            if (p == null || p.ShareName == null || Sharing.NameProblem(name, p.Name, takenNames) != null || !Quotable(p.Name)) return;
            string pc = "\\\\" + Environment.MachineName + "\\";
            if (QlMessage.Ask(this, Text, Lang.F("Rename the share {0}?", pc + p.ShareName),
                              Lang.F("PCs that print through {0} no longer reach the printer afterwards; add it there again as {1}.", pc + p.ShareName, pc + name),
                              MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunSettings("-SharePrinter \"" + p.Name + "\" -ShareName \"" + name + "\"", Lang.T("Renaming the share..."));
        }

        void StopSharing()
        {
            var p = SelectedShareable();
            if (p == null || p.ShareName == null || !Quotable(p.Name)) return;
            string pc = "\\\\" + Environment.MachineName + "\\";
            if (QlMessage.Ask(this, Text, Lang.F("Stop sharing \"{0}\"?", p.Name),
                              Lang.F("PCs that print through {0} no longer reach the printer afterwards.", pc + p.ShareName),
                              MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunSettings("-UnsharePrinter \"" + p.Name + "\"", Lang.T("Stopping sharing..."));
        }

        // From the result of "Add printer" (USB): the share tab with that printer
        void OfferShare(string printer)
        {
            tabs.Selected = 2;
            int i = shareables.FindIndex(p => p.Name == printer);
            if (i >= 0) shareList.SelectedIndex = i;
            shareNameBox.Focus();
        }

        // A section: a hairline, its heading in petrol and its content (top to bottom), all on the window's left line.
        // Group boxes drew their titles and frames at Windows' own offsets, so nothing lined up (user, 2026-10-30).
        Control Group(string text, int em, int width, out FlowLayoutPanel inner)
        {
            var section = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new Padding(0, em * 3 / 4, 0, 0) };
            var rule = Theme.Back(new Panel { Height = 1, Width = width, Margin = new Padding(0, 0, 0, em / 2) }, Role.SurfaceLine);
            rules.Add(rule);
            section.Controls.Add(rule);
            var heading = new Label { Text = text, AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold), Margin = new Padding(0, 0, 0, em / 3) };
            Theme.Fore(heading, Role.Heading);
            section.Controls.Add(heading);
            inner = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            inners.Add(inner);
            section.Controls.Add(inner);
            return section;
        }

        static FlowLayoutPanel Row(int em) { return new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, em / 2, 0, 0) }; }

        // A short grey hint and the blue info icon of the status window; the details in the app's own tip (InfoTip) on
        // hover or click.
        Control Hint(string text, int width, string details, int indent = 0)
        {
            int em = Font.Height;
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(indent, 0, 0, 4) };
            var l = Wrap(text, width - em * 2);
            l.Margin = new Padding(0, 1, 0, 0);
            hints.Add(l);
            var icon = StatusForm.InfoIcon(em);
            icon.Cursor = Cursors.Hand;
            if (details != null) InfoTip.Attach(icon, () => details, hover: true);
            row.Controls.Add(l);
            row.Controls.Add(icon);
            return row;
        }

        static Label Wrap(string text, int width, int indent = 0)
        {
            return Theme.Fore(new Label { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(indent, 0, 0, 4) }, Role.Dim);
        }

        Button Action(string text, Action click)
        {
            var b = new Button { Text = text, AutoSize = true };
            b.Click += delegate { click(); };
            return b;
        }

        // Our installed printer drivers, by model number.
        internal static string[] Drivers()
        {
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var envs = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Environments"))
                    if (envs != null)
                        foreach (var env in envs.GetSubKeyNames())
                            using (var d = envs.OpenSubKey(env + @"\Drivers\Version-3"))
                                if (d != null) installed.UnionWith(d.GetSubKeyNames());
            }
            catch (Exception) { }
            return MediaTable.Models.Select(m => m.DriverName).Where(installed.Contains).ToArray();
        }

        void LoadState()
        {
            loading = true;
            var drivers = Drivers();
            netModel.Items.Clear();
            netModel.Items.Add(AskPrinter);
            netModel.Items.AddRange(drivers);
            netModel.SelectedIndex = 0;
            // No model chosen in advance (the first, QL-500, was easily taken over unseen: debug pass 2026-10-31 L6); the
            // share's name picks one when it names a model (\\PC\QL-800)
            shareModel.Items.Clear();
            shareModel.Items.Add(ChooseModel);
            shareModel.Items.AddRange(drivers);
            shareModel.SelectedIndex = 0;
            GuessShareModel();
            // Installed = on (install.ps1 installs and starts it, or removes it).
            try { using (var sc = new ServiceController(PublishedStatus.ServiceName)) service.Checked = sc.Status >= 0; }  // Status throws if it is not installed
            catch (Exception) { service.Checked = false; }  // not installed
            log.Checked = File.Exists(PluginLog);
            var ours = ListPrinters().ToList();
            header.Subtitle = drivers.Length == 0 ? Lang.T("The driver is not installed on this PC")
                            : ours.Count == 0 ? Lang.T("No QL printer on this PC yet")
                            : ours.Count == 1 ? Lang.T("1 QL printer on this PC") : Lang.F("{0} QL printers on this PC", ours.Count);
            printers.Text = drivers.Length == 0 ? Lang.T("The driver is not installed on this PC: run Setup.exe from the release zip.")
                          : ours.Count == 0 ? Lang.T("QL printers on this PC: none yet.") : Lang.F("QL printers on this PC ({0}):", ours.Count);
            // At most 8 rows; more scroll.
            printerList.Items.Clear();
            listed.Clear();
            var shareablesNow = drivers.Length == 0 ? new List<ShareablePrinter>() : Sharing.Printers();
            foreach (var p in ours)
            {
                string shareName = InstallForm.IsShared(p) ? Sharing.ShareNameOf(p) : null;
                printerList.Items.Add(p + "   (" + PortOfPrinter(p) + (shareName != null ? ", " + Lang.F("shared as {0}", shareName) : "") + ")");
                listed.Add(p);
            }
            printerList.Visible = printerButtons.Visible = ours.Count > 0;
            // With printers in the list the band's subtitle already counts them: no line above the list (2026-11-06)
            printers.Visible = ours.Count == 0;
            removeButton.Enabled = propertiesButton.Enabled = false;
            // The first load runs in the constructor: SelectOnOpen isn't set yet then, and a share check's result can't
            // reach a window without a handle (both lost on the real two-PC run, 0.19.7); OnLoad does it then
            if (IsHandleCreated) AfterList();
            ShowUsbRows(drivers.Length == 0 ? new List<UsbCandidate>() : UsbPrinters.Unset());
            ShowSharePage(shareablesNow, Sharing.TakenNames(), shareablesNow.Count == 0 ? SharingReach.Ok : Sharing.Reach());
            SizeList();
            loading = false;
            FitHeight();
        }

        // The lines for connected USB printers without a printer of ours: printer symbol, text (Brother's own printer on
        // the same port in grey below), "Add printer" on the right
        internal void ShowUsbRows(List<UsbCandidate> candidates)
        {
            int em = Font.Height;
            usbRows.SuspendLayout();
            foreach (Control c in usbRows.Controls.Cast<Control>().ToList()) c.Dispose();
            usbRows.Controls.Clear();
            usbTexts.Clear();
            // One line for all of them (two lines made the window too tall at 150 %; user's choice 2026-11-06): symbol |
            // text + (i) | button on the list's right edge; with several printers the button opens a menu of them
            if (candidates.Count > 0)
            {
                var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, em / 4, 0, 0), Padding = Padding.Empty };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var icon = Theme.Fore(new Label { Text = "\uE749", AutoSize = true, Font = new Font("Segoe MDL2 Assets", Font.SizeInPoints * 1.15f), Anchor = AnchorStyles.Left,
                                                  Margin = new Padding(0, 2, em / 4, 0) }, Role.Text);
                var text = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, em / 4, 0) };
                var first = candidates[0];
                var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
                                        Text = candidates.Count == 1 ? Lang.F("{0} on {1} is not set up here yet.", first.Model.Name, first.Port)
                                                                     : Lang.F("{0} QL printers on USB are not set up here yet.", candidates.Count) };
                var info = StatusForm.InfoIcon(em);
                info.Cursor = Cursors.Hand;
                info.Anchor = AnchorStyles.Left;
                string details = UsbDetails(candidates);
                InfoTip.Attach(info, () => details, hover: true);
                text.Controls.Add(label);
                text.Controls.Add(info);
                var add = Action(Lang.T("Add printer"), () => { });
                add.Anchor = AnchorStyles.Right;
                add.Margin = Padding.Empty;
                if (candidates.Count == 1) add.Click += delegate { AddUsbPrinter(first); };
                else
                {
                    var menu = new ContextMenuStrip();
                    foreach (var c in candidates) { var cand = c; menu.Items.Add(c.Model.Name + " (" + c.Port + ")", null, delegate { AddUsbPrinter(cand); }); }
                    add.Click += delegate { menu.Show(add, new Point(0, add.Height)); };
                    add.Disposed += delegate { menu.Dispose(); };
                }
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(text, 1, 0);
                row.Controls.Add(add, 2, 0);
                usbRows.Controls.Add(row);
                usbTexts.Add(new Control[] { text, icon, add });
            }
            usbRows.Visible = candidates.Count > 0;
            usbRows.ResumeLayout();
            FitUsbRows(printerList.Width);
        }

        // The text wraps so that the line is as wide as the list (the button on its right edge)
        void FitUsbRows(int width)
        {
            foreach (var t in usbTexts)
            {
                // the symbol follows the text's size (a font set on a control isn't scaled at a DPI change)
                float size = t[0].Controls[0].Font.SizeInPoints * 1.15f;
                if (Math.Abs(t[1].Font.SizeInPoints - size) > 0.1f) t[1].Font = new Font("Segoe MDL2 Assets", size);
                var info = t[0].Controls[1];
                int room = Math.Max(Font.Height * 10, width - t[1].PreferredSize.Width - t[1].Margin.Horizontal - t[2].PreferredSize.Width - t[0].Margin.Horizontal
                                                      - info.Width - info.Margin.Horizontal);
                t[0].Controls[0].MaximumSize = new Size(room, 0);
                t[0].Parent.MinimumSize = new Size(width, 0);
            }
        }

        // The USB line's (i): why the printer isn't there, what "Add printer" does, Brother's own printers beside them
        internal static string UsbDetails(List<UsbCandidate> candidates)
        {
            var lines = new List<string>();
            if (candidates.Count > 1) lines.Add(string.Join(", ", candidates.Select(c => c.Model.Name + " (" + c.Port + ")")));
            lines.Add(Lang.T("Windows doesn't set up a removed USB printer again by itself, not even after a restart. \"Add printer\" adds it with this driver, not shared."));
            foreach (var c in candidates.Where(c => c.BrotherQueue != null))
                lines.Add(Lang.F("Brother's own printer \"{0}\" stays alongside it.", c.BrotherQueue));
            return string.Join("\r\n\r\n", lines);
        }

        // A QL printer plugged in, unplugged or switched off while the window is open: the USB line follows (Windows'
        // WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to every top-level window; one look a second after the last one)
        Timer usbLook;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x0219 || (long)m.WParam != 0x0007 || loading) return;
            if (usbLook == null)
            {
                usbLook = new Timer { Interval = 1000 };
                usbLook.Tick += delegate { usbLook.Stop(); RefreshUsbRows(); };
                Disposed += delegate { usbLook.Dispose(); };
            }
            usbLook.Stop();
            usbLook.Start();
        }

        void RefreshUsbRows()
        {
            if (IsDisposed || Drivers().Length == 0) return;
            ShowUsbRows(UsbPrinters.Unset());
            FitHeight();
        }

        void AddUsbPrinter(UsbCandidate c)
        {
            // Unplugged or switched off since the line appeared: no printer for a device that isn't there
            if (!UsbPrinters.Unset().Any(u => u.Port == c.Port))
            {
                Say(Lang.F("{0} on {1} is no longer connected.", c.Model.Name, c.Port));
                RefreshUsbRows();
                return;
            }
            var r = RunSettings("-UsbPrinter -UsbPort " + c.Port + " -UsbDriverName \"" + c.Model.DriverName + "\"", Lang.T("Adding the printer..."));
            if (r != null) PrinterAdded(r);
        }

        void AddNetworkPrinter()
        {
            string host = address.Text.Trim();
            if (!Regex.IsMatch(host, @"^[A-Za-z0-9][A-Za-z0-9.:-]*$")) { Say(Lang.T("Enter the printer's IP address (e.g. 192.0.2.50) or host name.")); return; }
            string driver = netModel.SelectedItem as string, model, roll = null;
            bool asked = driver == AskPrinter;
            if (asked)
            {
                List<string> found;
                try
                {
                    found = SetupProgress.Run(this, Text, Lang.F("Asking the printer at {0}...", host), Lang.T("Which model is it, and which roll is loaded (SNMP)?"),
                                              () => NetworkStatus.Identify(host));
                }
                catch (Exception ex)
                {
                    ShowResult(ResultKind.Warning, Lang.T("The printer doesn't answer"), Fields(Lang.T("Address"), host),
                               ex.Message + "\n\n" + Lang.T("If the address is right, choose the model yourself and click Add again."));
                    return;
                }
                driver = found[0];
                model = found[1];
                roll = found[2];
            }
            else model = ModelName(driver);
            // Confirm before the administrator prompt.
            using (var q = new SetupResult(Text, ResultKind.Question,
                                           asked ? Lang.F("Found {0} at {1}", model, host) : Lang.F("Add a {0} at {1}?", model, host),
                                           Fields(Lang.T("Model"), model + " " + (asked ? Lang.T("(the printer told)") : Lang.T("(chosen; the printer wasn't asked)")),
                                                  Lang.T("Loaded roll"), roll, Lang.T("Address"), Lang.F("{0}, print port 9100", host)),
                                           Lang.T("Windows then asks for administrator permission."), null, Lang.T("Cancel")))
            {
                q.AddChoice(Lang.T("Add printer"), DialogResult.OK);
                if (q.ShowDialog(this) != DialogResult.OK) return;
            }
            var r = RunSettings("-NetworkPrinter " + host + " -DriverName \"" + driver + "\"", Lang.T("Adding the printer..."));
            if (r == null) return;
            if (!r.ContainsKey("roll") && roll != null) r["roll"] = roll;
            r["how"] = " " + (asked ? Lang.T("(the printer told)") : Lang.T("(chosen)"));
            PrinterAdded(r);
        }

        static string ChooseModel { get { return Lang.T("(choose the model)"); } }

        // The model a share's name names ("\\PC\QL-800", "\\PC\Brother QL-1110NWB Etiketten"), chosen while no other is
        internal static string ModelInShareName(string shareName, IEnumerable<string> drivers)
        {
            var m = Regex.Match(shareName ?? "", @"QL-?(\d{3,4}[A-Z]*)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string name = "QL-" + m.Groups[1].Value.ToUpperInvariant();
            return drivers.FirstOrDefault(d => string.Equals(ModelName(d), name, StringComparison.OrdinalIgnoreCase));
        }

        string guessedModel;
        void GuessShareModel()
        {
            if (shareModel.SelectedIndex > 0 && (string)shareModel.SelectedItem != guessedModel) return;  // the user chose one
            string driver = ModelInShareName(share.Text, shareModel.Items.Cast<object>().Skip(1).Cast<string>());
            guessedModel = driver;
            shareModel.SelectedItem = driver ?? (object)ChooseModel;
        }

        // A printer share as Windows allows it: \\server\share, server without spaces or \/:*?"<>|, share name without
        // \/[]:|<>+=;,?*" (umlauts, "&" and spaces are fine; debug pass 2026-10-31 L12)
        internal static bool IsShareName(string s)
        {
            return Regex.IsMatch(s ?? "", @"^\\\\[^\\/:*?""<>| ]+\\[^\\/\[\]:|<>+=;,?*""]+$");
        }

        void AddSharedPrinter()
        {
            string s = share.Text.Trim();
            if (!IsShareName(s)) { Say(Lang.T("Enter the share as \\\\PC\\printer share (e.g. \\\\PC\\QL-800).")); return; }
            if (shareModel.Items.Count <= 1) { Say(Lang.T("The driver is not installed on this PC.")); return; }
            if (shareModel.SelectedIndex <= 0) { Say(Lang.T("Choose the printer's model.")); return; }
            string driver = shareModel.SelectedItem.ToString();
            using (var q = new SetupResult(Text, ResultKind.Question, Lang.F("Add the shared printer {0}?", s),
                                           Fields(Lang.T("Share"), s, Lang.T("Model"), ModelName(driver) + " " + Lang.T("(chosen)")),
                                           Lang.T("This PC prints to it through the other PC, which must be on and share it. Windows then asks for administrator permission."),
                                           null, Lang.T("Cancel")))
            {
                q.AddChoice(Lang.T("Add printer"), DialogResult.OK);
                if (q.ShowDialog(this) != DialogResult.OK) return;
            }
            var r = RunSettings("-SharePort \"" + s + "\" -DriverName \"" + driver + "\"", Lang.T("Adding the printer..."));
            if (r != null) PrinterAdded(r);
        }

        void PrinterAdded(Dictionary<string, string> r)
        {
            string name = Get(r, "printer"), driver = Get(r, "driver");
            string where = r.ContainsKey("share") ? r["share"] + " " + Lang.T("(shared by another PC)")
                         : r.ContainsKey("address") ? r["address"] + " " + Lang.T("(print port 9100)")
                         : r.ContainsKey("usb") ? r["usb"] + " (USB)" : null;
            bool existed = Get(r, "existed") == "1";
            using (var d = new SetupResult(Text, ResultKind.Ok, existed ? Lang.T("Already set up") : Lang.T("Printer added"),
                                           Fields(Lang.T("Name"), name, Lang.T("Model"), ModelName(driver) + Get(r, "how"), Lang.T("Loaded roll"), Get(r, "roll"),
                                                  r.ContainsKey("share") ? Lang.T("Share") : r.ContainsKey("usb") ? Lang.T("Port") : Lang.T("Address"), where),
                                           existed ? Lang.T("This printer was already set up on this PC with the same port; nothing was changed.")
                                                   : Lang.T("Programs can print to it now. The label formats are the same on every QL printer.")))
            {
                d.AddAction(Lang.T("Print test label"), () => TestLabel.Print(d, Text, name), true, () => TestLabel.LastInfo);
                d.AddAction(Lang.T("Open print queue"), () => { Process.Start("rundll32.exe", "printui.dll,PrintUIEntry /o /n \"" + name + "\""); return null; });
                d.AddAction(Lang.T("Set as default printer"), () => SetDefaultPrinterW(name)
                    ? Lang.T("Now the default printer. (If Windows manages the default printer, it may pick another one later.)")
                    : Lang.T("Could not set the default printer:") + " " + new Win32Exception().Message);
                // A USB printer: sharing it for other PCs is one step away (the "Share a printer" tab with it; user's
                // choice 2026-11-06)
                bool share = false;
                if (r.ContainsKey("usb") && InstallForm.IsShared(name) == false)
                    d.AddAction(Lang.T("Share printer"), () => { share = true; d.Close(); return null; });
                d.ShowDialog(this);
                if (share) OfferShare(name);
            }
        }

        string SelectedPrinter()
        {
            int i = printerList.SelectedIndex;
            return i >= 0 && i < listed.Count ? listed[i] : null;
        }

        // The question before removing a printer: what goes depends on its port (No preselected, Esc = No; user's
        // choice 2026-11-05). A shared printer: PCs printing to it may keep it open (user's choice 2026-11-06).
        internal static string[] RemoveQuestion(string name, string port, int jobs, bool shared = false)
        {
            string host, shareName, gone, stays;
            if (PublishedStatus.IsShare(port, out host, out shareName))
            {
                gone = Lang.F("The printer is removed from this PC. On the PC {0} it stays shared and keeps working there.", host);
                stays = Lang.T("The driver stays installed. On the tab \"Another PC's printer\" the printer is back quickly.");
            }
            else if (port != null && port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase))
            {
                gone = Lang.F("The printer is removed from this PC. Its port to {0} is removed too.", port.Substring(3));
                stays = Lang.T("The driver stays installed. On the tab \"Network printer\" the printer is back quickly.");
            }
            else if (port != null && port.StartsWith("USB", StringComparison.OrdinalIgnoreCase))
            {
                gone = Lang.T("The printer is removed from this PC.");
                stays = Lang.T("The driver stays installed. While the printer is connected by USB, it can be added again here at any time.");
            }
            else
            {
                gone = port != null && Regex.IsMatch(port, @"^[A-Za-z]:\\") ? Lang.T("The printer is removed from this PC. Its port (the output file) is removed too.")
                                                                           : Lang.T("The printer is removed from this PC.");
                stays = Lang.T("The driver stays installed.");
            }
            if (jobs == 1) gone += " " + Lang.T("1 waiting print job is deleted.");
            else if (jobs > 1) gone += " " + Lang.F("{0} waiting print jobs are deleted.", jobs);
            if (shared) gone += "\n\n" + Lang.T("It is shared. PCs that print to it may still hold it open; then it is removed completely only after they or this PC restart.");
            return new[] { Lang.F("Remove \"{0}\"?", name), gone + "\n\n" + stays };
        }

        // Windows' own properties dialog of the printer (sharing, ports, Advanced; its preferences from there), with
        // the user's rights: Windows asks for administrator permission there itself where needed. Afterwards the list
        // is read again (a printer renamed there).
        void PrinterProperties()
        {
            string name = SelectedPrinter();
            if (name == null) return;
            try
            {
                var p = Process.Start(new ProcessStartInfo("rundll32.exe", "printui.dll,PrintUIEntry /p /n \"" + name + "\"") { UseShellExecute = false });
                p.EnableRaisingEvents = true;
                p.Exited += delegate
                {
                    p.Dispose();
                    if (!IsDisposed) BeginInvoke((Action)delegate { if (!IsDisposed) LoadState(); });
                };
            }
            catch (Exception ex) { Say(ex.Message); }
        }

        void RemovePrinter()
        {
            string name = SelectedPrinter();
            if (name == null) return;
            // The name goes to the elevated script as one quoted argument
            if (name.IndexOf('"') >= 0) { Say(Lang.F("'{0}' can't be removed here: remove it in Windows' printer settings.", name)); return; }
            int jobs = 0;
            try { jobs = Spooler.Jobs(name).Count; } catch (Exception) { }
            var q = RemoveQuestion(name, QlDevice.PortOf(name), jobs, InstallForm.IsShared(name));
            if (QlMessage.Ask(this, Text, q[0], q[1], MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var r = RunSettings("-RemovePrinter \"" + name + "\"", Lang.T("Removing the printer..."));
            // Still held open (as a rule by a PC printing to its share): what helps, in a dialog of its own. No button
            // that restarts this PC's spooler (on ARM that dropped the USB device to Windows' generic driver, 2026-11-06)
            string note = r == null ? null : PendingNote(r);
            if (note != null)
                using (var d = new SetupResult(Text, ResultKind.Info, Lang.T("The printer is being removed"), Fields(Lang.T("Name"), name), note))
                    d.ShowDialog(this);
        }

        void Option(string arguments, string working)
        {
            var r = RunSettings(arguments, working);
            if (r == null) return;
            if (r.ContainsKey("logging"))
            {
                bool on = r["logging"] == "on";
                using (var d = new SetupResult(Text, ResultKind.Ok, on ? Lang.T("Driver log turned on") : Lang.T("Driver log turned off"),
                                               Fields(Lang.T("Log file"), on ? Get(r, "logfile") : null),
                                               on ? Lang.T("The driver writes a few lines for every print job. Turn it off again when you are done; that also deletes the log.")
                                                  : Lang.T("The log files were deleted; the driver writes nothing.")))
                {
                    if (on) d.AddAction(Lang.T("Open log folder"), () => { Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(PluginLog) + "\""); return null; }, true);
                    d.ShowDialog(this);
                }
            }
            else if (r.ContainsKey("service"))
            {
                bool on = r["service"] == "on";
                using (var d = new SetupResult(Text, ResultKind.Ok, on ? Lang.T("Printer status service turned on") : Lang.T("Printer status service turned off"),
                                               Fields(Lang.T("Service"), "QL printer status " + (on ? Lang.T("(running, starts with Windows)") : Lang.T("(removed)"))),
                                               on ? Lang.T("It reads this PC's QL printers every 3 seconds and publishes their status, so PCs that print through a share see it too.")
                                                  : Lang.T("The programs read this PC's printers themselves again. PCs printing through a share no longer see their status.")))
                    d.ShowDialog(this);
            }
        }

        static string Get(Dictionary<string, string> r, string key)
        {
            string v;
            return r.TryGetValue(key, out v) ? v : null;
        }

        static List<KeyValuePair<string, string>> Fields(params string[] pairs)
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) list.Add(new KeyValuePair<string, string>(pairs[i], pairs[i + 1]));
            return list;
        }

        // "Brother QL-800 (Unidrv)" -> "QL-800".
        static string ModelName(string driver)
        {
            var m = MediaTable.Models.FirstOrDefault(x => x.DriverName == driver);
            return m != null ? m.Name : driver;
        }

        void ShowResult(ResultKind kind, string headline, IEnumerable<KeyValuePair<string, string>> fields, string note, string details = null)
        {
            using (var d = new SetupResult(Text, kind, headline, fields, note, details)) d.ShowDialog(this);
        }

        void Uninstall()
        {
            string script = Path.Combine(SetupDir, "install.ps1");
            if (!File.Exists(script)) { Say(Lang.F("Uninstall: use Setup.exe from the release zip ({0} is missing).", script)); return; }
            // Opened from QL Label Formats: the uninstall removes it and waits until its window is closed, so it closes first
            // (with its own question about unsaved changes); kept open, nothing is uninstalled (debug pass 2026-10-31 L5).
            var tool = Owner as MainForm;
            if (tool != null)
            {
                using (var q = new SetupResult(Text, ResultKind.Question, Lang.T("Close QL Label Formats and uninstall?"), null,
                                               Lang.T("The uninstall removes QL Label Formats too, so its window closes first."), null, Lang.T("Cancel")))
                {
                    q.AddChoice(Lang.T("Uninstall"), DialogResult.OK);
                    if (q.ShowDialog(this) != DialogResult.OK) return;
                }
                Close();
                tool.BeginInvoke((Action)delegate
                {
                    tool.Close();
                    if (Application.OpenForms.Cast<Form>().Contains(tool)) return;  // the user kept it open
                    StartUninstall(script);
                });
                return;
            }
            StartUninstall(script);
            Close();
        }

        // The uninstall in the setup window (this exe --uninstall; it runs from a copy in %TEMP%, InstallForm.Uninstall)
        static void StartUninstall(string script)
        {
            Process.Start(Application.ExecutablePath, "--uninstall");
        }

        // install.ps1 -Settings ... as administrator, with a progress dialog; what it did (its -ResultFile), or null
        // after showing why nothing changed.
        Dictionary<string, string> RunSettings(string arguments, string working)
        {
            // UI tests (deskrun, QL_TEST_SILENT=1) never start the elevated script: what asked for it goes to
            // %TEMP%\ql-unidrv-test-run.txt instead (2026-11-06)
            if (Environment.GetEnvironmentVariable("QL_TEST_SILENT") == "1")
            {
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ql-unidrv-test-run.txt"), DateTime.Now + " " + arguments + "\r\n" + Environment.StackTrace + "\r\n\r\n"); } catch (Exception) { }
                return null;
            }
            string script = Path.Combine(SetupDir, "install.ps1");
            if (!File.Exists(script))
            {
                ShowResult(ResultKind.Error, Lang.T("Not available here"), Fields(Lang.T("Missing"), script), Lang.T("Run Setup.exe from the release zip once; it installs the setup scripts."));
                LoadState();
                return null;
            }
            string temp = Path.GetTempPath(), logFile = Path.Combine(temp, "ql-unidrv-settings.log"), resultFile = Path.Combine(temp, "ql-unidrv-settings.txt");
            foreach (var f in new[] { logFile, resultFile }) try { File.Delete(f); } catch (Exception) { }
            var psi = new ProcessStartInfo(NativeTools.Path("powershell.exe", elevated: true),
                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\" -Settings " + arguments + " -Log \"" + logFile + "\" -ResultFile \"" + resultFile + "\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            int code;
            try
            {
                code = SetupProgress.Run(this, Text, working, Lang.T("Windows may ask for administrator permission first; then this takes a few seconds."),
                                         () => { using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; } });
            }
            catch (Win32Exception ex)
            {
                bool declined = ex.NativeErrorCode == 1223;
                LoadState();
                Done(Lang.T("Not changed"));
                ShowResult(ResultKind.Warning, Lang.T("Not changed"), null,
                           declined ? Lang.T("Administrator permission was declined, so nothing was changed.") : ex.Message);
                return null;
            }
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadAllLines(resultFile))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            catch (Exception) { }
            LoadState();
            if (code != 0 || Get(result, "done") != "1")
            {
                var lines = TranscriptBody(logFile);
                string reason = Get(result, "error") ?? lines.LastOrDefault(l => !l.StartsWith(" ") && !l.StartsWith("+")) ?? Lang.T("It stopped without a message.");
                Done(Lang.T("Did not finish"));
                ShowResult(ResultKind.Error, Lang.T("The change did not finish"), Fields(Lang.T("Log"), logFile), reason,
                           string.Join("\r\n", lines.Skip(Math.Max(0, lines.Count - 30))) + "\r\n\r\nLog: " + logFile);
                return null;
            }
            DeleteRunFiles();  // kept only after a failure (debug pass X7)
            Done(result.ContainsKey("removed") ? (Get(result, "pending") != null ? Lang.F("Printer '{0}' is being removed", result["removed"]) : Lang.F("Printer '{0}' removed", result["removed"]))
                 : result.ContainsKey("unshared") ? Lang.F("Printer '{0}' no longer shared", result["unshared"])
                 : result.ContainsKey("renamed-from") ? Lang.F("Share of '{0}' renamed to {1}", Get(result, "shared"), Get(result, "sharename"))
                 : result.ContainsKey("sharename") ? Lang.F("Printer '{0}' shared as {1}", Get(result, "shared"), Get(result, "sharename"))
                 : result.ContainsKey("printer") ? (Get(result, "existed") == "1" ? Lang.F("Printer '{0}' already set up", result["printer"]) : Lang.F("Printer '{0}' added", result["printer"]))
                 : result.ContainsKey("logging") ? (result["logging"] == "on" ? Lang.T("Driver log turned on") : Lang.T("Driver log turned off"))
                 : result.ContainsKey("service") ? (result["service"] == "on" ? Lang.T("Printer status service turned on") : Lang.T("Printer status service turned off"))
                 : Lang.T("Done"), PendingNote(result));
            return result;
        }

        // The log and result files of setup and settings runs in %TEMP%, after a run that finished: also those an
        // earlier run left (a failed one keeps them for Show details). Debug pass 2026-10-31 X7.
        internal static readonly string[] RunFileNames = { "ql-unidrv-install.log", "ql-unidrv-setup.txt", "ql-unidrv-settings.log", "ql-unidrv-settings.txt" };
        internal static void DeleteRunFiles()
        {
            if (InstallForm.Preview) return;
            foreach (string name in RunFileNames) try { File.Delete(Path.Combine(Path.GetTempPath(), name)); } catch (Exception) { }
        }

        // The script's own output from a transcript: without PowerShell's header and footer blocks (lines between
        // "****" rulers, in the system's language) and empty lines.
        internal static List<string> TranscriptBody(string file)
        {
            var all = new List<string>();
            try { all = File.ReadAllLines(file).ToList(); } catch (Exception) { }
            var rulers = all.Select((l, i) => new { l, i }).Where(x => x.l.StartsWith("*****")).Select(x => x.i).ToList();
            int from = rulers.Count >= 2 ? rulers[1] + 1 : 0;
            int to = rulers.Count >= 4 ? rulers[rulers.Count - 2] : all.Count;
            return all.Skip(from).Take(Math.Max(0, to - from)).Where(l => l.Trim().Length > 1 && !l.StartsWith("*****")).ToList();
        }

        // The bottom line: the last change, briefly (the dialog showed the details).
        // A note (PendingNote) goes into the (i) beside the line (690 px rule, user's choice 2026-11-06).
        void Done(string what, string note = null)
        {
            string line = Lang.F("Last change: {0} ({1})", what, DateTime.Now.ToString("t"));
            Say(line, note);
        }

        // A removed printer that another program still has open: Windows keeps it hidden, its own port too (install.ps1
        // result keys pending / pending-port; user's choice 2026-11-06)
        internal static string PendingNote(Dictionary<string, string> result)
        {
            if (Get(result, "pending") == null) return null;
            string note = Get(result, "was-shared") == "1"
                ? Lang.T("A PC that prints to its share (or shows its status) still has it open. To finish: on that PC restart the service \"Print Spooler\", or restart that PC or this one.")
                : Lang.T("Another program still has it open; Windows finishes removing it once it is free, at the latest at the next restart.");
            string port = Get(result, "pending-port");
            if (port != null)
                note += " " + Lang.F("Its port to {0} is left behind unused and does no harm.", port.StartsWith("IP_", StringComparison.OrdinalIgnoreCase) ? port.Substring(3) : port);
            return note;
        }

        [System.Runtime.InteropServices.DllImport("winspool.drv", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        static extern bool SetDefaultPrinterW(string name);

        void Say(string text, string details = null)
        {
            status.Text = text;
            statusDetails = details;
            statusInfo.Visible = details != null;
            statusRow.Visible = !string.IsNullOrEmpty(text);
            FitHeight();
        }
    }
}
