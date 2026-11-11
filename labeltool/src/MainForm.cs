// Main window: list of label formats (left), editor with a to-scale preview (right).
// Save & apply hands the list to an elevated copy of this exe (--apply), which writes
// C:\ProgramData\QLUnidrv\labels.json/.tsv and syncs the Windows forms. No driver install.
// The status bar shows the selected printer's state and loaded roll (PrinterCheck); formats that don't
// fit the roll are grey, the user's default label format is bold.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed partial class MainForm : ScaledForm
    {
        public const string AppTitle = "QL Label Formats";  // the product name (Start menu), in every language
        public static string NoPrinterText { get { return Lang.F("No {0} printer uses the QL Unidrv driver.", QlModel.AllNames); } }

        List<LabelFormat> formats = new List<LabelFormat>();
        List<LabelFormat> saved = new List<LabelFormat>();
        bool dirty, busy, loading;

        readonly ListView list = new UiFont.NativeHeaderListView();   // no GDI+ font table for its header (UiFont.cs)
        readonly TextBox nameBox = new TextBox();
        readonly ComboBox mediaBox = new ComboBox();
        readonly NumericUpDown lengthBox = new NumericUpDown();
        readonly NumericUpDown feedBox = new NumericUpDown();
        readonly Label lengthHint = new Label(), feedHint = new Label(), problem = new Label();
        readonly LabelPreview preview = new LabelPreview();
        readonly ToolStrip tools = new ToolStrip();
        readonly ToolStripButton printButton = new ToolStripButton(Lang.T("Test print"));
        readonly ToolStripComboBox printerBox = new ToolStripComboBox();
        bool fillingPrinters;
        string shownPrinter;         // the printer the list's styling, default and roll check were last made for
        readonly ToolStripMenuItem factoryItem = new ToolStripMenuItem(Lang.T("Brother factory list"));
        readonly MenuStrip menu = new MenuStrip();
        int printerBoxWidth;         // the width the printer names need (the box gets narrower when the row is short)
        readonly ToolStripStatusLabel printerStatus = new ToolStripStatusLabel(), syncStatus = new ToolStripStatusLabel(),
            applyStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleRight };
        readonly ToolStripStatusLabel mediaStatus = new ToolStripStatusLabel { IsLink = true, LinkBehavior = LinkBehavior.HoverUnderline };
        readonly TableLayoutPanel editor = new TableLayoutPanel();
        readonly Label info = new Label();
        readonly Button defaultButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        readonly Label defaultHint = new Label();
        readonly ToolTip editorTips = new ToolTip();
        readonly ToolStripMenuItem defaultItem = new ToolStripMenuItem(Lang.T("Use as default label format"));
        readonly Panel tipBar = new Panel();
        readonly Timer printerTimer = new Timer { Interval = 5000 };
        string testPrinter;
        QlModel testModel;           // testPrinter's model (null: none / unknown driver)
        PrinterCheck printerCheck;   // last check of testPrinter (null: none yet / not a USB printer)
        DateTime lastCheck;
        bool checking;
        string defaultForm;          // testPrinter's default paper size for this user
        ListViewItem dragItem;
        Font boldFont;
        readonly SplitContainer split = new SplitContainer();
        readonly Label listHeader = new Label();
        readonly StatusStrip statusBar = new StatusStrip { ShowItemToolTips = true };
        ToolStripButton applyButton;

        public MainForm()
        {
            StartupTrace.Mark("window: start");
            Text = AppTitle;
            Theme.Use(this);
            Theme.Changed += ThemeChanged;
            FormClosed += delegate { Theme.Changed -= ThemeChanged; };
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;  // scales nothing here (no AutoScaleDimensions): sizes go through S()
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            // The font already follows the display scaling; the window's own sizes are 100 % pixels scaled the same way.
            // 100 % pixels scaled to this display, never larger than its working area (150 % on a small screen).
            // the screen it opens on (its saved place, else the mouse's): S() scales to that screen's DPI too
            var screen = Screen.FromPoint(WindowBounds.StartPoint(WindowBounds.LabelFormats)).WorkingArea;
            ClientSize = new Size(Math.Min(S(1000), screen.Width * 9 / 10), Math.Min(S(620), screen.Height * 9 / 10));
            MinimumSize = new Size(Math.Min(S(760), screen.Width), Math.Min(S(480), screen.Height));  // the command row fits (German)
            StartPosition = FormStartPosition.CenterScreen;
            WindowBounds.Use(this, WindowBounds.LabelFormats);   // the size and place it had when it was last closed
            KeyPreview = true;

            BuildTools();
            BuildMenu();
            BuildEditor();
            BuildList();
            StartupTrace.Mark("window: tools, menu, editor, list");
            split.Dock = DockStyle.Fill;
            split.FixedPanel = FixedPanel.Panel1;
            split.SplitterWidth = S(6);
            split.Panel1.Controls.Add(list);
            listHeader.Text = Lang.T("Label formats");
            listHeader.Dock = DockStyle.Top;
            listHeader.AutoSize = false;
            listHeader.Height = Font.Height + S(12);
            listHeader.AutoEllipsis = true;
            listHeader.TextAlign = ContentAlignment.MiddleLeft;
            listHeader.FlatStyle = FlatStyle.System;  // drawn by Windows without the left padding: the text starts on the left line
            listHeader.Font = new Font(Font, FontStyle.Bold);
            editorTips.SetToolTip(listHeader, Lang.T("Label formats (shared by all QL printers)"));
            split.Panel1.Controls.Add(listHeader);
            split.Panel2.Controls.Add(editor);
            printerStatus.IsLink = true;
            printerStatus.LinkBehavior = LinkBehavior.HoverUnderline;
            Theme.ItemLink(printerStatus, Role.Text);
            printerStatus.Click += delegate { new SetupForm().ShowDialog(this); UpdatePrinterStatus(); };
            printerStatus.Image = MarkImage(S(16));
            statusBar.Items.AddRange(new ToolStripItem[] { printerStatus, new ToolStripSeparator(), mediaStatus, new ToolStripSeparator(), syncStatus, applySeparator, applyStatus });
            applyStatus.TextChanged += delegate { FitApplyStatus(); };
            statusBar.Layout += delegate
            {
                // The Layout event comes before the items get their new widths: fit after this layout
                if (fitPending || !statusBar.IsHandleCreated) return;
                fitPending = true;
                statusBar.BeginInvoke((MethodInvoker)delegate { fitPending = false; FitApplyStatus(); });
            };
            mediaStatus.Click += delegate { ShowStatus(); };
            Controls.Add(split);
            BuildTipBar();
            Controls.Add(tipBar);
            Controls.Add(tools);
            Controls.Add(menu);
            MainMenuStrip = menu;
            Controls.Add(statusBar);
            StartupTrace.Mark("window: controls");
            // WinForms scales the fonts when the window gets another DPI (opened on or moved to a screen with other
            // scaling); the pixel sizes set through S() follow here (as in the label editor; OnLoad). ScaledForm: a window
            // created directly on a second screen with other scaling, and FollowFont after a DPI change.
            DpiChanged += delegate { BeginInvoke((MethodInvoker)delegate { Rescale(); FitList(); }); };

            LoadFromDisk();
            StartupTrace.Mark("window: labels.json read and listed");
            UpdatePrinterStatus();
            lastCheck = DateTime.Now;   // just read: the first Activated doesn't read them again (Shown checks the printer)
            StartupTrace.Mark("window: printers");
            FormClosing += OnClosing;
            KeyDown += OnKey;
            HelpPages.SetPage(this, HelpPages.Formats);   // F1 itself: OnKey; its dialogs open this page
            Activated += delegate
            {
                if (!dirty && !busy && ChangedElsewhere()) LoadFromDisk();  // another window applied a list: show it
                if (DateTime.Now - lastCheck > TimeSpan.FromSeconds(3)) { UpdatePrinterStatus(); CheckPrinter(); UpdateTipBar(); }
            };
            printerTimer.Tick += delegate
            {
                if (!ContainsFocus) return;
                if (!busy && !printerBox.DroppedDown) UpdatePrinterStatus();  // a printer added or removed meanwhile
                CheckPrinter();
                UpdateTipBar();
            };
            Shown += delegate
            {
                StartupTrace.Mark("shown");
                CheckPrinter();
                StartupTrace.Mark("shown: printer check started");
                printerTimer.Start();
                TrayMonitor.EnsureRunning();
                StartupTrace.Mark("shown: tray monitor");
                StartupTrace.WhenIdle();
            };
        }

        // 100 % pixels to this display's pixels. Before the window exists DeviceDpi is Windows' system DPI, which is not the
        // screen's after the scaling was changed without signing out again, or on a second screen: then the screen the
        // window opens on (WinForms scales the fonts to it when the window is created, the sizes set here it doesn't).
        int S(int pixels) { return IsHandleCreated ? LogicalToDeviceUnits(pixels) : (int)Math.Round(pixels * startDpi / 96.0); }

        readonly int startDpi = Brand.ScreenDpi(WindowBounds.StartPoint(WindowBounds.LabelFormats));   // the screen it opens on

        // Created directly on a second screen with other scaling (ScaledForm, MatchScreenDpi): WinForms then scales as on a
        // move, keeping the window's size (the sizes set through S() are already the screen's; Rescale corrects the rest).
        // The match after the load (Load placed the window), then the sizes, as the Load handler did before.
        protected override bool MatchAfterLoad { get { return true; } }

        protected override void OnLoad(EventArgs e)
        {
            StartupTrace.Mark("load");
            base.OnLoad(e);
            Rescale();
            FitList();
            StartupTrace.Mark("load: scaled");
        }

        // Scales the sizes set through S() to the window's present DPI. On a DPI change WinForms already scales sizes,
        // margins, paddings and wrap widths (again here would double them); when the window opens on a screen with other
        // scaling it may not, or the sizes were already the screen's (OnHandleCreated). The length box (S(90) wide) tells
        // which, and the walk scales only what is left (as in the label editor). The edges' spacing, the splitter, the
        // minimum size and the status bar mark are set absolutely.
        void Rescale()
        {
            if (!printerBox.Font.Equals(tools.Font)) printerBox.Font = tools.Font;  // WinForms scaled it twice on a move
            SuspendLayout();
            try
            {
                split.SplitterWidth = S(6);
                var area = Screen.FromControl(this).WorkingArea;
                MinimumSize = new Size(Math.Min(S(760), area.Width), Math.Min(S(480), area.Height));
                var mark = printerStatus.Image;
                printerStatus.Image = MarkImage(S(16));
                if (mark != null) mark.Dispose();
                float k = (float)S(90) / lengthBox.Width;
                if (Math.Abs(k - 1f) >= 0.03f)
                {
                    Func<int, int> z = v => (int)Math.Round(v * k);
                    Func<Padding, Padding> zp = q => new Padding(z(q.Left), z(q.Top), z(q.Right), z(q.Bottom));
                    Func<Size, Size> zs = q => new Size(z(q.Width), z(q.Height));
                    Action<Control> walk = null;
                    walk = c =>
                    {
                        if (c is ToolStrip) return;
                        if (c != this)
                        {
                            c.Margin = zp(c.Margin);
                            c.Padding = zp(c.Padding);
                            if (c.MaximumSize != Size.Empty) c.MaximumSize = zs(c.MaximumSize);
                            if (c.MinimumSize != Size.Empty) c.MinimumSize = zs(c.MinimumSize);
                            if (!c.AutoSize && c.Dock == DockStyle.None) c.Width = z(c.Width);
                        }
                        foreach (Control child in c.Controls) walk(child);
                    };
                    walk(this);
                }
                SpaceEdges();
            }
            finally { ResumeLayout(true); }
        }

        // The edges at this DPI. One left line, as in all the app's windows (Brand.Gutter): the menu's first word, the
        // first button's frame, the list's header and frame, the tip bar's text and the status bar's mark; the same
        // margin on the right, and some air above and below. Room inside and between the framed buttons (they must read
        // as buttons, also at 150 %).
        void SpaceEdges()
        {
            int g = Brand.Gutter(Font.Height);
            menu.Padding = new Padding(Math.Max(0, g - S(10)), S(3), 0, S(3));  // a menu item's text starts S(10) inside it
            tools.Padding = new Padding(g - S(2), S(5), g - S(2), S(5));        // less the buttons' own margin
            foreach (ToolStripItem item in tools.Items)
            {
                if (!(item is ToolStripButton)) continue;
                item.Padding = new Padding(S(8), S(2), S(8), S(2));
                item.Margin = new Padding(S(2), S(1), S(2), S(1));
            }
            split.Panel1.Padding = new Padding(g, S(8), 0, S(10));
            split.Panel2.Padding = new Padding(S(14), S(8), g, S(10));
            tipBar.Padding = new Padding(g - Font.Height / 6, S(8), g, S(8));  // less the left padding GDI draws text with
            statusBar.Padding = new Padding(Math.Max(0, g - S(1)), 0, S(14), 0);
        }

        // The list's text sizes, measured once the window exists (with its screen's fonts) and after a DPI change:
        // widths measured in the constructor can be too small (cut names and sizes).
        void FitList()
        {
            var old = boldFont;
            boldFont = new Font(list.Font, FontStyle.Bold);
            foreach (ListViewItem item in list.Items) StyleItem(item);
            if (old != null) old.Dispose();
            var fonts = new[] { listHeader.Font, applyButton.Font };
            listHeader.Font = new Font(Font, FontStyle.Bold);
            listHeader.Height = listHeader.Font.Height + S(12);
            applyButton.Font = new Font(tools.Font, FontStyle.Bold);
            foreach (var f in fonts) f.Dispose();
            MeasurePrinterBox();
            list.Columns[1].Width = TextRenderer.MeasureText("000 × 000,0 mm", boldFont).Width + S(20);
            int width = ListWidth();
            split.Panel1MinSize = Math.Min(S(200), width);
            split.SplitterDistance = width;
        }

        // The list as wide as its header, longest name and size need, at most 45 % of the window (the format's settings
        // get the rest).
        int ListWidth()
        {
            int name = formats.Select(f => TextRenderer.MeasureText(f.Name, boldFont).Width).DefaultIfEmpty(S(150)).Max();
            int want = name + S(36) + list.Columns[1].Width + SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi) + S(8);
            want = Math.Max(want, TextRenderer.MeasureText(listHeader.Text, listHeader.Font).Width + listHeader.Padding.Horizontal + S(12));
            return Math.Max(S(260), Math.Min(want + split.Panel1.Padding.Horizontal, (int)(ClientSize.Width * 0.45)));
        }

        // The base mark as a small picture (status bar), drawn at this display's size.
        static Bitmap MarkImage(int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp)) Brand.DrawMark(g, 0, 0, size);
            return bmp;
        }

        // The menu bar as Windows draws it; the command row's buttons framed like the buttons of the other windows (the
        // label editor's command row), the main action (Save & apply) petrol with white text (Theme.Primary).
        // High contrast: Windows draws everything. Dark: the shared ThemeRenderer's colours.
        sealed class ToolRenderer : ThemeRenderer
        {
            static readonly object PrimaryTag = new object(), ButtonTag = new object();

            public ToolRenderer() { RoundedEdges = false; }

            public static void MakePrimary(ToolStripItem item) { if (!SystemInformation.HighContrast) item.Tag = PrimaryTag; }

            public static void MakeButton(ToolStripItem item) { if (!SystemInformation.HighContrast) item.Tag = ButtonTag; }

            protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
            {
                if (e.Item.Tag != PrimaryTag && e.Item.Tag != ButtonTag) { base.OnRenderButtonBackground(e); return; }
                var r = new Rectangle(Point.Empty, e.Item.Size);
                r.Inflate(0, -1);
                if (e.Item.Tag == PrimaryTag)
                {
                    Role fill = !e.Item.Enabled ? Role.DisabledLine : e.Item.Pressed ? Role.PrimaryPressed : e.Item.Selected ? Role.PrimaryHot : Role.Primary;
                    using (var b = new SolidBrush(Theme.Of(fill))) e.Graphics.FillRectangle(b, r);
                    return;
                }
                Role back = !e.Item.Enabled ? Role.DisabledFace : e.Item.Pressed || e.Item.Selected ? Role.Selection : Role.ButtonFace;
                Role line = !e.Item.Enabled ? Role.SurfaceLine : e.Item.Pressed ? Role.AccentPressed : e.Item.Selected ? Role.Accent : Role.ButtonLine;
                using (var b = new SolidBrush(Theme.Of(back))) e.Graphics.FillRectangle(b, r);
                using (var p = new Pen(Theme.Of(line))) e.Graphics.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (e.Item.Tag == PrimaryTag) e.TextColor = Theme.Of(e.Item.Enabled ? Role.OnPrimary : Role.DisabledFace);
                base.OnRenderItemText(e);
            }
        }

        // The selected row in the brand's light petrol with a petrol bar on the left, not Windows blue (owner-drawn
        // cells; the groups and the header stay Windows'). High contrast: Windows draws everything.
        void DrawCell(object sender, DrawListViewSubItemEventArgs e)
        {
            var item = e.Item;
            var bounds = e.Bounds;
            if (e.ColumnIndex == 0) bounds.Width = list.Columns[0].Width;  // column 0's bounds span the whole row
            bool selected = item.Selected;
            using (var back = new SolidBrush(selected ? Theme.Of(Role.Selection) : list.BackColor)) e.Graphics.FillRectangle(back, bounds);
            if (selected && e.ColumnIndex == 0)
                using (var bar = new SolidBrush(Theme.Of(Role.Accent))) e.Graphics.FillRectangle(bar, bounds.Left, bounds.Top + 1, S(3), bounds.Height - 2);
            var text = new Rectangle(bounds.Left + S(6), bounds.Top, Math.Max(0, bounds.Width - S(8)), bounds.Height);
            // High Contrast: Windows' selection text on its selection colour (a grey row was unreadable there)
            var fore = selected && Theme.HighContrast ? Theme.Of(Role.OnPrimary) : item.ForeColor;
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, item.Font, text, fore,
                                  TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // ---------- layout ----------
        // One row with the main commands only, under the menu bar that has them all (the label editor's pattern). No
        // overflow (»): in a short row the printer box gets narrower; MinimumSize keeps the rest visible.
        void BuildTools()
        {
            tools.GripStyle = ToolStripGripStyle.Hidden;
            tools.Renderer = new ToolRenderer();
            tools.CanOverflow = false;
            tools.Items.Add(Button(Lang.T("New"), Lang.T("Add a label format (Ctrl+N)"), delegate { NewFormat(); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(new ToolStripLabel(Lang.T("Printer:")) { ToolTipText = Lang.T("Used for test print, the default label format and the roll check. The label formats are shared by all QL printers.") });
            printerBox.DropDownStyle = ComboBoxStyle.DropDownList;
            printerBox.AutoSize = false;
            printerBox.FlatStyle = FlatStyle.System;
            printerBox.DropDown += delegate { UpdatePrinterStatus(); };
            printerBox.SelectedIndexChanged += delegate
            {
                if (fillingPrinters || printerBox.SelectedItem == null) return;
                testPrinter = printerBox.SelectedItem.ToString();
                QlPrinters.Remember(testPrinter);   // the next start shows it (also QL Label Maker)
                UpdatePrinterStatus();
            };
            tools.Items.Add(printerBox);
            printButton.ToolTipText = Lang.T("Print a sample with the selected format (F5)");
            printButton.Click += delegate { TestPrint(); };
            tools.Items.Add(printButton);
            tools.Items.Add(Button(Lang.T("Printer status"), Lang.T("What the printer is doing, the loaded roll, errors and what to do (F6)"), delegate { ShowStatus(); }));
            tools.Items.Add(Button("QL Label Maker", Lang.T("Design and print labels: text, lines, boxes, pictures (QL Label Maker)"),
                                   delegate { OpenEditor(null); }));

            var apply = applyButton = Button(Lang.T("Save && apply"), Lang.T("Save and make the formats available in all programs (Ctrl+S)"), delegate { SaveAndApply(); });
            apply.Font = new Font(tools.Font, FontStyle.Bold);
            ToolRenderer.MakePrimary(apply);
            apply.Alignment = ToolStripItemAlignment.Right;
            tools.Items.Add(apply);
            foreach (ToolStripItem item in tools.Items)
                if (item is ToolStripButton && item != apply) ToolRenderer.MakeButton(item);
            SpaceEdges();
            tools.SizeChanged += delegate { FitPrinterBox(); };
        }

        void MeasurePrinterBox()
        {
            if (printerBox.Items.Count == 0) return;
            int w = printerBox.Items.Cast<object>().Max(n => TextRenderer.MeasureText(n.ToString(), printerBox.Font).Width) + S(30);
            printerBox.DropDownWidth = Math.Max(w, S(120));
            printerBoxWidth = Math.Min(Math.Max(w, S(120)), S(260));
            FitPrinterBox();
        }

        // The printer box: as wide as the printer names need, narrower when the row is short (never below 120 px).
        void FitPrinterBox()
        {
            if (printerBoxWidth == 0) return;
            int others = 0;
            foreach (ToolStripItem item in tools.Items)
                if (item != printerBox) others += item.Width + item.Margin.Horizontal;
            int room = tools.DisplayRectangle.Width - others - printerBox.Margin.Horizontal - S(4);
            int w = Math.Max(S(120), Math.Min(printerBoxWidth, room));
            if (printerBox.Width != w) printerBox.Size = new Size(w, printerBox.Height);
        }

        // Every command, by subject: File / Edit / Printer / Tools / Help. The shortcuts are handled in OnKey (shown here).
        void BuildMenu()
        {
            menu.Renderer = tools.Renderer;
            menu.ShowItemToolTips = true;

            var file = new ToolStripMenuItem(Lang.T("&File"));
            file.DropDownItems.Add(new ToolStripMenuItem(Lang.T("New"), null, delegate { NewFormat(); }) { ShortcutKeyDisplayString = Lang.T("Ctrl+N") });
            var import = new ToolStripMenuItem(Lang.T("Import"));
            import.DropDownItems.Add(Lang.T("Brother label formats (.dat, .org)..."), null, delegate { ImportFile(true); });
            import.DropDownItems.Add("JSON...", null, delegate { ImportFile(false); });
            factoryItem.Click += delegate { LoadFactory(); };
            import.DropDownItems.Add(new ToolStripSeparator());
            import.DropDownItems.Add(factoryItem);
            file.DropDownItems.Add(import);
            var export = new ToolStripMenuItem(Lang.T("Export"));
            export.DropDownItems.Add(Lang.T("Brother label formats (.dat)..."), null, delegate { ExportFile(true); });
            export.DropDownItems.Add("JSON...", null, delegate { ExportFile(false); });
            file.DropDownItems.Add(export);
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Save && apply"), null, delegate { SaveAndApply(); })
                { ShortcutKeyDisplayString = Lang.T("Ctrl+S"), ToolTipText = Lang.T("Save and make the formats available in all programs (Ctrl+S)") });
            file.DropDownItems.Add(new ToolStripMenuItem(Lang.T("Revert"), null, delegate { Revert(); }) { ToolTipText = Lang.T("Discard unsaved changes") });
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Lang.T("Exit"), null, delegate { Close(); });
            using (var s = FactoryStream()) factoryItem.Enabled = s != null || LabelStore.FindSeed(Lang.Current) != null;
            if (!factoryItem.Enabled) factoryItem.ToolTipText = Lang.T("Not included in this build.");

            var edit = new ToolStripMenuItem(Lang.T("&Edit"));
            var duplicate = new ToolStripMenuItem(Lang.T("Duplicate"), null, delegate { Duplicate(); });
            var delete = new ToolStripMenuItem(Lang.T("Delete"), null, delegate { DeleteSelected(); }) { ShortcutKeyDisplayString = Lang.T("Del") };
            var up = new ToolStripMenuItem(Lang.T("Move up"), null, delegate { MoveSelected(-1); }) { ShortcutKeyDisplayString = "Alt+\u2191" };
            var down = new ToolStripMenuItem(Lang.T("Move down"), null, delegate { MoveSelected(1); }) { ShortcutKeyDisplayString = "Alt+\u2193" };
            var design = new ToolStripMenuItem(Lang.T("Design a label on this format"), null, delegate { OpenEditor(Selected); });
            edit.DropDownItems.AddRange(new ToolStripItem[] { duplicate, delete, new ToolStripSeparator(), up, down, new ToolStripSeparator(), design });
            edit.DropDownOpening += delegate
            {
                foreach (var item in new[] { duplicate, delete, up, down, design }) item.Enabled = Selected != null;
            };

            var printer = new ToolStripMenuItem(Lang.T("&Printer"));
            var print = new ToolStripMenuItem(Lang.T("Test print"), null, delegate { TestPrint(); }) { ShortcutKeyDisplayString = "F5" };
            defaultItem.Click += delegate { SetAsDefault(); };
            printer.DropDownItems.AddRange(new ToolStripItem[]
            {
                print, defaultItem, new ToolStripSeparator(),
                new ToolStripMenuItem(Lang.T("Printer status"), null, delegate { ShowStatus(); }) { ShortcutKeyDisplayString = "F6" },
                PrinterMenu.NotifyItem(printer, this, AppTitle, NotificationsChanged), new ToolStripSeparator(),
                PrinterMenu.SetupItem(this, UpdatePrinterStatus),
            });
            printer.DropDownOpening += delegate
            {
                print.Enabled = testPrinter != null && Selected != null;
                UpdateDefaultItem(defaultItem);
            };

            var extras = new ToolStripMenuItem(Lang.T("&Tools"));
            extras.DropDownItems.Add(new ToolStripMenuItem("QL Label Maker", null, delegate { OpenEditor(null); })   // its name, as in the tray menu
                { ToolTipText = Lang.T("Design and print labels: text, lines, boxes, pictures (QL Label Maker)") });
            extras.DropDownItems.Add(new ToolStripSeparator());
            extras.DropDownItems.Add(LanguageSwitch.Menu(SwitchLanguage));
            extras.DropDownItems.Add(Theme.Menu());

            var help = new ToolStripMenuItem(Lang.T("&Help"));
            help.DropDownItems.Add(new ToolStripMenuItem(Lang.T("QL Label Formats Help"), null, delegate { HelpPages.Open(this, HelpPages.Formats); }) { ShortcutKeyDisplayString = "F1" });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(new ToolStripMenuItem(Lang.T("About QL Unidrv"), null, delegate { AboutForm.ShowAbout(this, AppTitle); }));

            menu.Items.AddRange(new ToolStripItem[] { file, edit, printer, extras, help });
        }

        // The texts are set when the windows are built, so QL Label Formats starts again in the new language. The setting
        // is stored only after the question (LanguageSwitch); unsaved changes are saved first (a running save counts too:
        // Apply refuses it, so nothing switches).
        async void SwitchLanguage(string language)
        {
            bool restart;
            try
            {
                restart = await LanguageSwitch.Run(language, dirty || busy, unsaved => AskLanguage(language, unsaved) == DialogResult.OK,
                                                   Apply, setting => Lang.Setting = setting);
            }
            catch (Exception ex) { Failed(Lang.T("The language couldn't be changed."), ex); return; }
            if (!restart) return;
            SingleInstance.Release();   // the new start is the window from now on
            WindowBounds.Save(this, WindowBounds.LabelFormats);   // the new start opens where this window is
            try { Process.Start(Application.ExecutablePath, testPrinter != null ? "--printer \"" + testPrinter + "\"" : ""); }
            catch (Exception ex) { Failed(Lang.T("QL Label Formats couldn't start again in the new language."), ex); return; }
            Close();
        }

        DialogResult AskLanguage(string setting, bool unsaved)
        {
            using (var d = LanguageSwitch.Question(AppTitle, setting, unsaved ? Lang.T("You have unsaved changes: they are saved and applied first.") : null))
                return d.ShowDialog(this);
        }

        // Introduces the printer monitor until it is turned on or the tip is dismissed. Instead, while the toolbar's
        // printer is set to Windows' "Use Printer Offline" and that is the cause (the printer answers; a switched-off
        // printer is "not connected" in the status bar, no bar): one line with the fix, details in its tooltip.
        const string SettingsKey = Lang.SettingsKey;
        readonly List<Control> monitorTip = new List<Control>(), offlineTip = new List<Control>();
        readonly Label offlineText = Theme.Fore(new Label { AutoSize = true }, Role.WarningText);
        List<string> offlinePrinters = new List<string>();
        LinkLabel offlineOnline;

        void BuildTipBar()
        {
            tipBar.Dock = DockStyle.Top;
            Theme.Back(tipBar, Role.InfoBack);
            var row = tipRow;
            row.Dock = DockStyle.Fill;
            row.WrapContents = true;
            Theme.Back(row, Role.InfoBack);
            var tip = Theme.Fore(new Label
            {
                AutoSize = true, Margin = new Padding(0, S(4), S(12), 0),
                Text = Lang.T("Tip: when the label printer stops, Windows doesn't say why. Printer notifications show the reason and the fix."),
            }, Role.InfoText);
            var on = new LinkLabel { Text = Lang.T("Turn on notifications"), AutoSize = true, Margin = new Padding(0, S(4), S(12), 0) };
            on.LinkClicked += delegate { if (!TrayMonitor.AutostartEnabled) SetNotifications(true); UpdateTipBar(); };
            offlineText.Margin = new Padding(0, S(4), S(12), 0);
            var online = new LinkLabel { Text = Lang.T("Use online"), AutoSize = true, Margin = new Padding(0, S(4), S(12), 0) };
            offlineOnline = online;
            online.LinkClicked += async (s, e) =>
            {
                online.Enabled = false;
                await PrintReadiness.UseOnline(this, offlinePrinters.ToList());
                online.Enabled = true;
                UpdateTipBar();
                CheckPrinter();
            };
            var status = new LinkLabel { Text = Lang.T("Printer status"), AutoSize = true, Margin = new Padding(0, S(4), S(12), 0) };
            status.LinkClicked += delegate { ShowStatus(); };
            var later = new LinkLabel { Text = Lang.T("Don't show again"), AutoSize = true, Margin = new Padding(0, S(4), 0, 0) };
            later.LinkClicked += delegate
            {
                try { using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey)) k.SetValue("MonitorTipDismissed", 1); } catch (Exception) { }
                UpdateTipBar();
            };
            // Save & apply declined in Windows' prompt (ShowDeclined): what happened, Apply again, × (Office-style message bar)
            var declinedIcon = Theme.Fore(new Label { AutoSize = true, Text = "ⓘ", Margin = new Padding(0, S(4), S(6), 0) }, Role.InfoText);
            var declinedText = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(0, S(4), S(12), 0),
                                                      Text = Lang.T("Not applied. Your changes are still there.") }, Role.InfoText);
            var again = new LinkLabel { Text = Lang.T("Apply again"), AutoSize = true, Margin = new Padding(0, S(4), S(12), 0) };
            again.LinkClicked += delegate { SaveAndApply(); };
            declinedClose.Text = "×";
            declinedClose.AutoSize = false;
            declinedClose.Dock = DockStyle.Right;
            declinedClose.Width = S(28);
            declinedClose.TextAlign = ContentAlignment.MiddleCenter;
            declinedClose.Cursor = Cursors.Hand;
            declinedClose.AccessibleName = Lang.T("Close");
            Theme.Fore(declinedClose, Role.InfoText);
            editorTips.SetToolTip(declinedClose, Lang.T("Close"));
            declinedClose.Click += delegate { HideDeclined(); };
            monitorTip.AddRange(new Control[] { tip, on, status, later });
            offlineTip.AddRange(new Control[] { offlineText, online });
            declinedTip.AddRange(new Control[] { declinedIcon, declinedText, again, declinedClose });
            foreach (var link in new[] { on, online, status, later, again }) Theme.StyleLink(link);
            row.Controls.AddRange(new Control[] { declinedIcon, declinedText, again, tip, offlineText, on, online, status, later });
            tipBar.Controls.Add(declinedClose);
            tipBar.Controls.Add(row);
            row.BringToFront();   // fills what the × on the right leaves
            tipBar.SizeChanged += delegate { FitTipBar(); };
            UpdateTipBar();
        }

        // The tip bar as high as its text and links need at the window's width (more than one line at 150 % or in German).
        readonly FlowLayoutPanel tipRow = new FlowLayoutPanel();

        void FitTipBar()
        {
            int w = Math.Max(S(200), tipBar.ClientSize.Width - tipBar.Padding.Horizontal - (declinedClose.Visible ? declinedClose.Width : 0));
            foreach (Control c in tipRow.Controls) c.MaximumSize = new Size(Math.Max(S(100), w - c.Margin.Horizontal - S(4)), 0);
            int h = tipRow.GetPreferredSize(new Size(w, 0)).Height + tipBar.Padding.Vertical;
            if (tipBar.Height != h) tipBar.Height = h;
        }

        void UpdateTipBar()
        {
            bool dismissed = false;
            try { using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey)) dismissed = k != null && Convert.ToInt32(k.GetValue("MonitorTipDismissed", 0)) != 0; }
            catch (Exception) { }
            // The toolbar's printer only, from its last check (OfflineIsCause decided there).
            var check = printerCheck;
            offlinePrinters = check != null && check.WorkOffline && check.Printer == testPrinter ? new List<string> { testPrinter } : new List<string>();
            bool offline = offlinePrinters.Count > 0;
            if (offline)
            {
                var model = QlPrinters.ModelOf(testPrinter);
                string text = Lang.F("{0} is set to offline – jobs wait on this PC.", model != null ? model.Name : testPrinter);
                if (offlineText.Text != text) offlineText.Text = text;
                string tip = Lang.F("Windows' 'Use Printer Offline' is on for {0}: print jobs wait on this PC and don't reach the printer. 'Use online' clears it; Windows may ask for administrator permission.", testPrinter);
                editorTips.SetToolTip(offlineText, tip);
                if (offlineOnline != null) editorTips.SetToolTip(offlineOnline, tip);
            }
            // One message at a time: a declined Save & apply first (the user just did it), then offline, then the monitor tip
            foreach (var c in declinedTip) c.Visible = declined;
            foreach (var c in monitorTip) c.Visible = !declined && !offline;
            foreach (var c in offlineTip) c.Visible = !declined && offline;
            tipBar.Visible = declined || offline || (!dismissed && !TrayMonitor.AutostartEnabled);
            if (tipBar.Visible) FitTipBar();
        }

        // Save & apply was declined in Windows' prompt: the message bar says so until the next change, a successful
        // apply or ×. The status bar stays empty for it.
        readonly List<Control> declinedTip = new List<Control>();
        readonly Label declinedClose = new Label();
        bool declined;

        void ShowDeclined() { declined = true; UpdateTipBar(); }

        void HideDeclined()
        {
            if (!declined) return;
            declined = false;
            UpdateTipBar();
        }

        static ToolStripButton Button(string text, string tip, EventHandler click)
        {
            var b = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            b.Click += click;
            return b;
        }

        void BuildList()
        {
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            if (!SystemInformation.HighContrast)
            {
                list.OwnerDraw = true;
                list.DrawColumnHeader += (s, e) => { e.DrawDefault = true; };
                list.DrawItem += (s, e) => { e.DrawDefault = false; };  // the cells: DrawCell
                list.DrawSubItem += DrawCell;
                typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(list, true, null);
            }
            list.Columns.Add(Lang.T("Name"), S(200));
            list.Columns.Add(Lang.T("Size"), S(120));  // FitList
            list.Groups.Add("c", Lang.T("Continuous tape"));
            list.Groups.Add("d", Lang.T("Die-cut labels"));
            list.Groups.Add("r", Lang.T("Round labels"));
            list.SelectedIndexChanged += delegate { ShowSelected(); };
            boldFont = new Font(list.Font, FontStyle.Bold);  // measured again in FitList
            list.AllowDrop = true;
            list.ItemDrag += OnItemDrag;
            list.DragEnter += OnDragOver;
            list.DragOver += OnDragOver;
            var menu = new ContextMenuStrip();
            var print = new ToolStripMenuItem(Lang.T("Test print"), null, delegate { TestPrint(); }) { ShortcutKeyDisplayString = "F5" };
            print.Font = new Font(print.Font, FontStyle.Bold);
            var asDefault = new ToolStripMenuItem(Lang.T("Use as default label format"), null, delegate { SetAsDefault(); });
            menu.Items.Add(print);
            menu.Items.Add(asDefault);
            menu.Items.Add(Lang.T("Design a label on this format"), null, delegate { OpenEditor(Selected); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("Duplicate"), null, delegate { Duplicate(); });
            menu.Items.Add(Lang.T("Delete"), null, delegate { DeleteSelected(); });
            menu.Opening += (s, e) =>
            {
                if (busy || Selected == null) { e.Cancel = true; return; }
                print.Enabled = testPrinter != null;
                UpdateDefaultItem(asDefault);
            };
            list.ContextMenuStrip = menu;
            list.Resize += delegate
            {
                int w = list.ClientSize.Width - list.Columns[1].Width - S(4);
                if (w > S(80)) list.Columns[0].Width = w;
            };
        }

        void BuildEditor()
        {
            editor.Dock = DockStyle.Fill;
            editor.ColumnCount = 2;  // captions, boxes (an empty third column opened 96 px wide on a 150 % second screen)
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            nameBox.Dock = DockStyle.Fill;
            nameBox.MaxLength = LabelStore.MaxNameLength;
            nameBox.TextChanged += delegate { EditorChanged(); };

            mediaBox.Dock = DockStyle.Fill;
            mediaBox.DropDownStyle = ComboBoxStyle.DropDownList;
            mediaBox.Items.AddRange(MediaTable.Ordered());
            mediaBox.SelectedIndexChanged += delegate { MediaChanged(); };

            foreach (var box in new[] { lengthBox, feedBox })
            {
                box.DecimalPlaces = 2;
                box.Width = S(90);
                box.TextAlign = HorizontalAlignment.Right;
                box.ValueChanged += delegate { EditorChanged(); };
                box.TextChanged += delegate { EditorChanged(); };
            }
            lengthBox.Increment = 1;
            feedBox.Increment = 0.1m;
            feedBox.Maximum = (decimal)LabelStore.MaxFeedMm;

            Theme.Fore(problem, Role.ErrorText);
            problem.AutoSize = true;
            problem.MaximumSize = new Size(S(600), 0);
            preview.Dock = DockStyle.Fill;
            preview.MinimumSize = new Size(S(200), S(140));

            int row = 0;
            AddRow(row++, Lang.T("Name"), nameBox, null);
            AddRow(row++, Lang.T("Brother media"), mediaBox, null);
            AddRow(row++, Lang.T("Length (mm)"), lengthBox, lengthHint);
            AddRow(row++, Lang.T("Feed (mm)"), feedBox, feedHint);
            // The user's default paper size: a printer setting, not part of the label list.
            defaultButton.Click += delegate { SetAsDefault(); };
            defaultButton.Padding = new Padding(S(6), 0, S(6), 0);
            AddRow(row++, Lang.T("Default"), defaultButton, null);
            defaultButton.Anchor = AnchorStyles.Left;
            defaultHint.AutoSize = true;
            Theme.Fore(defaultHint, Role.Dim);
            defaultHint.Margin = new Padding(0, 0, 0, S(4));
            defaultHint.UseMnemonic = false;
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(defaultHint, 1, row);
            row++;
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(problem, 0, row);
            editor.SetColumnSpan(problem, 2);
            row++;
            Theme.Fore(info, Role.WarningText);
            info.AutoSize = true;
            info.MaximumSize = new Size(S(600), 0);
            info.Margin = new Padding(0, S(2), 0, S(4));
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(info, 0, row);
            editor.SetColumnSpan(info, 2);
            row++;
            editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            editor.Controls.Add(preview, 0, row);
            editor.SetColumnSpan(preview, 2);
            editor.RowCount = row + 1;
            // The texts wrap at the editor's width (at 150 % and in German they are longer than a line).
            editor.SizeChanged += delegate
            {
                int w = Math.Max(S(200), editor.ClientSize.Width - editor.Padding.Horizontal - S(8));
                problem.MaximumSize = info.MaximumSize = new Size(w, 0);
                defaultHint.MaximumSize = new Size(Math.Max(S(150), w - defaultHint.Left), 0);
                foreach (var hint in new[] { lengthHint, feedHint })
                    hint.MaximumSize = new Size(Math.Max(S(100), w - lengthHint.Parent.Left - lengthBox.Width - hint.Margin.Horizontal), 0);
            };
        }

        void AddRow(int row, string caption, Control box, Label hint)
        {
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            editor.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(8), S(12), S(8)) }, 0, row);
            box.Margin = new Padding(0, S(5), 0, S(5));
            box.AccessibleName = caption;  // screen readers: the box's caption (a label beside it isn't linked)
            if (hint == null)
            {
                editor.Controls.Add(box, 1, row);
                return;
            }
            // Number box with a grey hint (range) behind it.
            hint.AutoSize = true;
            Theme.Fore(hint, Role.Dim);
            hint.Margin = new Padding(S(8), S(8), 0, 0);
            var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Dock = DockStyle.Fill };
            flow.Controls.Add(box);
            flow.Controls.Add(hint);
            editor.Controls.Add(flow, 1, row);
        }

        // ---------- data ----------
        void LoadFromDisk()
        {
            var keep = Selected;  // stays selected (after Save & apply, Revert) if it is still in the list
            LabelSet set;
            damaged = null;
            try
            {
                if (File.Exists(LabelStore.JsonPath)) set = LabelStore.Load(LabelStore.JsonPath);
                else
                {
                    string seed = LabelStore.FindSeed(Lang.SystemLanguage);   // what the first --sync seeds
                    set = seed != null ? LabelStore.Load(seed) : new LabelSet();
                }
            }
            catch (Exception ex)
            {
                set = new LabelSet();
                if (File.Exists(LabelStore.JsonPath) && LabelStore.IsDamage(ex)) damaged = ex.Message;
                else QlMessage.Show(this, Lang.T("Cannot read the label formats:") + "\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            foreach (var f in set.Formats) LabelStore.Normalize(f);
            saved = set.Formats.Select(f => f.Clone()).ToList();
            formats = set.Formats.Select(f => f.Clone()).ToList();
            List<LabelFormat> factory = null;
            // A damaged list can't be repaired here (the file is written as administrator): Brother's factory list in its
            // place, or an empty one; Save & apply then replaces the file and keeps the damaged one as labels.json.bad.
            if (damaged != null && (factory = FactoryFormats()) != null &&
                QlMessage.Show(this, Lang.F("The label list can't be read; the file is damaged:\n{0}\n({1})\n\nStart again with Brother's factory list? " +
                                             "Save & apply then replaces the damaged file (a copy is kept as {2}).",
                                             LabelStore.JsonPath, damaged, Path.GetFileName(LabelStore.DamagedCopyPath)),
                                AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
            {
                foreach (var f in factory) { f.Key = NewKey(); LabelStore.Normalize(f); formats.Add(f); }
            }
            else if (damaged != null && factory == null)
                QlMessage.Show(this, Lang.F("The label list can't be read; the file is damaged:\n{0}\n({1})\n\nThe list starts empty. Save & apply then replaces " +
                                             "the damaged file (a copy is kept as {2}).", LabelStore.JsonPath, damaged, Path.GetFileName(LabelStore.DamagedCopyPath)),
                                AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            FillList((keep != null ? formats.FirstOrDefault(f => f.Key == keep.Key) : null) ?? formats.FirstOrDefault());
            SetDirty(formats.Count > 0 && (!File.Exists(LabelStore.JsonPath) || damaged != null));
            UpdateSyncStatus();
        }

        string damaged;  // labels.json exists but can't be read: the reason (LoadFromDisk)

        void FillList(LabelFormat select)
        {
            loading = true;
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var f in formats)
            {
                var item = new ListViewItem(new[] { f.Name, LabelStore.SizeText(f) }) { Tag = f };
                item.Group = list.Groups[GroupKey(f)];
                StyleItem(item);
                list.Items.Add(item);
                if (f == select) item.Selected = true;
            }
            list.EndUpdate();
            loading = false;
            if (list.SelectedItems.Count == 0 && list.Items.Count > 0) list.Items[0].Selected = true;
            ShowSelected();
            if (list.SelectedItems.Count > 0) list.SelectedItems[0].EnsureVisible();
        }

        static string GroupKey(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            return m == null || m.Continuous ? "c" : m.Kind == MediaKind.Round ? "r" : "d";
        }

        LabelFormat Selected { get { return list.SelectedItems.Count > 0 ? (LabelFormat)list.SelectedItems[0].Tag : null; } }

        void ShowSelected()
        {
            if (loading) return;
            var f = Selected;
            loading = true;
            editor.Enabled = f != null;
            if (f != null)
            {
                nameBox.Text = f.Name;
                mediaBox.SelectedItem = MediaTable.ById(f.PaperId);
                SetMediaLimits(MediaTable.ById(f.PaperId));
                lengthBox.Value = Clamp(lengthBox, f.LengthMm);
                feedBox.Value = Clamp(feedBox, f.FeedMm);
            }
            else
            {
                nameBox.Text = "";
                mediaBox.SelectedIndex = -1;
            }
            loading = false;
            preview.Format = f;
            ShowProblem(f);
            ShowInfo(f);
        }

        static decimal Clamp(NumericUpDown box, double v)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, (decimal)Math.Round(v, 2)));
        }

        void SetMediaLimits(Media m)
        {
            bool cont = m != null && m.Continuous;
            lengthBox.Enabled = feedBox.Enabled = cont;
            if (m == null) return;
            if (cont)
            {
                lengthBox.Minimum = m.MinLength01 / 10m;
                lengthBox.Maximum = m.MaxLength01 / 10m;
                lengthHint.Text = Media.Mm(m.MinLength01 / 10.0) + "–" + Media.Mm(m.MaxLength01 / 10.0) + " mm";
                feedHint.Text = Lang.F("0–{0} mm, top and bottom (Brother default 3 mm)", Media.Mm(LabelStore.MaxFeedMm));
            }
            else
            {
                lengthBox.Minimum = 0;
                lengthBox.Maximum = 10000;
                lengthHint.Text = Lang.T("fixed by the label");
                feedHint.Text = Lang.T("not used by die-cut labels");
            }
        }

        void MediaChanged()
        {
            if (loading) return;
            var f = Selected;
            var m = mediaBox.SelectedItem as Media;
            if (f == null || m == null) return;
            bool wasContinuous = MediaTable.ById(f.PaperId) != null && MediaTable.ById(f.PaperId).Continuous;
            f.PaperId = m.Id;
            if (m.Continuous && !wasContinuous) { f.LengthMm = m.DefaultLength01 / 10.0; f.FeedMm = 3; }
            if (!m.Continuous) f.FeedMm = Math.Round(m.TopMm);  // what Brother's tool stores (3, round labels 2)
            LabelStore.Normalize(f);
            loading = true;
            SetMediaLimits(m);
            lengthBox.Value = Clamp(lengthBox, f.LengthMm);
            feedBox.Value = Clamp(feedBox, f.FeedMm);
            loading = false;
            FormatChanged(f);
        }

        void EditorChanged()
        {
            if (loading) return;
            var f = Selected;
            if (f == null) return;
            f.Name = nameBox.Text;
            var m = MediaTable.ById(f.PaperId);
            if (m != null && m.Continuous)
            {
                decimal v;
                if (decimal.TryParse(lengthBox.Text, out v)) f.LengthMm = (double)v;
                if (decimal.TryParse(feedBox.Text, out v)) f.FeedMm = (double)v;
            }
            FormatChanged(f);
        }

        void FormatChanged(LabelFormat f)
        {
            var item = list.SelectedItems.Count > 0 ? list.SelectedItems[0] : null;
            if (item != null)
            {
                item.Text = f.Name;
                item.SubItems[1].Text = LabelStore.SizeText(f);
                var g = list.Groups[GroupKey(f)];
                if (item.Group != g) { item.Group = g; item.EnsureVisible(); }
            }
            if (item != null) StyleItem(item);
            preview.Format = f;
            ShowProblem(f);
            ShowInfo(f);
            SetDirty(true);
        }

        void ShowProblem(LabelFormat f)
        {
            string e = f == null ? null : LabelStore.ValidateFormat(f);
            if (e == null && f != null && formats.Count(x => string.Equals(x.Name.Trim(), f.Name.Trim(), StringComparison.OrdinalIgnoreCase)) > 1)
                e = Lang.T("Another format has the same name.");
            problem.Text = e ?? "";
            problem.Visible = e != null;
        }

        void SetDirty(bool value)
        {
            if (value && formats.Count == saved.Count && formats.Zip(saved, (a, b) => a.SameAs(b)).All(x => x) && File.Exists(LabelStore.JsonPath))
                value = false;
            HideDeclined();   // a change, or the list read again (after a successful apply)
            dirty = value;
            Text = AppTitle + (dirty ? " *" : "");
        }

        // The apply status line (MainForm.Apply.cs); declared here, so the field initializers run in the order they had.
        readonly ToolStripSeparator applySeparator = new ToolStripSeparator { Visible = false };
        string applyText = "", applyShown = "";

        // Light/dark switched (Theme): the rows' colours are chosen here, not by Theme.Apply.
        void ThemeChanged(object sender, EventArgs e)
        {
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) StyleItem(item);
            list.EndUpdate();
        }

        // Grey: doesn't fit the loaded roll, or the test printer's model has no such paper. Bold: the user's default label format.
        void StyleItem(ListViewItem item)
        {
            var f = (LabelFormat)item.Tag;
            var roll = LoadedRoll;
            bool? fits = roll != null ? roll.Fits(MediaTable.ById(f.PaperId)) : null;
            Theme.ItemFore(item, fits == false || ModelLacks(f) ? Role.Dim : Role.Text);
            var font = IsDefault(f) ? boldFont : list.Font;
            if (item.Font != font) item.Font = font;
        }

        bool IsDefault(LabelFormat f)
        {
            return f != null && defaultForm != null && string.Equals(defaultForm, f.Name, StringComparison.OrdinalIgnoreCase);
        }

        void ShowInfo(LabelFormat f)
        {
            var lines = new List<string>();
            if (f != null && ModelLacks(f)) lines.Add(LacksText(f));
            else if (f != null)
            {
                var roll = LoadedRoll;
                var m = MediaTable.ById(f.PaperId);
                if (roll != null && roll.Fits(m) == false)
                    lines.Add(Lang.F("Doesn't fit the roll in {0} ({1}); this format needs {2}.", testPrinter, roll.LoadedText, m != null ? m.Display : Lang.T("another roll")));
            }
            info.Text = string.Join("\n", lines);
            info.Visible = lines.Count > 0;
            UpdateDefaultRow(f);
        }

        // "Default" row: setting the default is immediate and per user, unlike the list (Save & apply).
        void UpdateDefaultRow(LabelFormat f)
        {
            string tip;
            string setText = Lang.T("Set as default");
            if (f == null || testPrinter == null)
            {
                defaultButton.Text = setText;
                defaultButton.Enabled = false;
                defaultHint.Text = testPrinter == null ? NoPrinterText : "";
                tip = "";
            }
            else if (ModelLacks(f) && !IsDefault(f))
            {
                defaultButton.Text = setText;
                defaultButton.Enabled = false;
                defaultHint.Text = Lang.F("Not for the {0} ({1}).", testModel.Name, testPrinter);
                tip = LacksText(f);
            }
            else if (IsDefault(f))
            {
                defaultButton.Text = Lang.T("✓ Default");
                defaultButton.Enabled = false;
                defaultHint.Text = Lang.F("Programs start with this format on {0}.", testPrinter);
                tip = Lang.F("This is the paper size programs preselect on {0} for your user account.", testPrinter);
            }
            else if (!IsApplied(f))
            {
                // Clickable: it offers Save & apply first.
                defaultButton.Text = setText;
                defaultButton.Enabled = !busy;
                defaultHint.Text = Lang.T("Not saved yet: Save & apply comes first.");
                tip = Lang.T("New or changed formats must be saved and applied before they can be the default.");
            }
            else
            {
                defaultButton.Text = setText;
                defaultButton.Enabled = !busy;
                defaultHint.Text = Lang.T("Applies immediately, just for you – no Save & apply needed.");
                tip = Lang.F("Makes this the paper size programs preselect on {0} (for your user account).\n" +
                             "This is a printer setting, separate from the label list: it is saved immediately, without administrator rights.", testPrinter);
            }
            editorTips.SetToolTip(defaultButton, tip);
            editorTips.SetToolTip(defaultHint, tip);
        }

        // The one status window of the user session (QL Printer Status); back here, Activated reads the printer again.
        void ShowStatus() { PrinterStatusProgram.ShowStatus(this); }

        // An action that failed: what, as the headline; Windows' text behind Show details.
        void Failed(string what, Exception ex)
        {
            QlMessage.Ask(this, AppTitle, what, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message);
        }

        void SetNotifications(bool on)
        {
            PrinterMenu.SetNotifications(this, AppTitle, on);
            NotificationsChanged();
        }

        void NotificationsChanged()
        {
            UpdateTipBar();
            applyStatus.Text = TrayMonitor.AutostartEnabled
                ? Lang.T("Printer error notifications on: the monitor runs in the notification area and starts with Windows.")
                : Lang.T("Printer error notifications off.");
        }

        // ---------- keys and closing ----------
        void OnKey(object sender, KeyEventArgs e)
        {
            if (busy) return;
            if (e.Control && e.KeyCode == Keys.S) { SaveAndApply(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.Control && e.KeyCode == Keys.N) { NewFormat(); e.Handled = e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.F5) { TestPrint(); e.Handled = true; }
            else if (e.KeyCode == Keys.F6) { ShowStatus(); e.Handled = true; }
            else if (e.KeyCode == Keys.F1) { HelpPages.Open(this, HelpPages.Formats); e.Handled = true; }
            else if (e.Alt && (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && list.Focused)
            {
                MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Delete && list.Focused) { DeleteSelected(); e.Handled = true; }
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (busy)
            {
                e.Cancel = true;
                if (applying) applyStatus.Text = StatusText(Lang.T("Save & apply is still running: confirm or decline Windows' administrator prompt, then close the window."));
                return;
            }
            if (dirty && QlMessage.Show(this, Lang.T("The label formats have unsaved changes. Close without saving?"), AppTitle,
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                e.Cancel = true;
        }
    }
}
