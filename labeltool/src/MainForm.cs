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
    sealed class MainForm : Form
    {
        public const string AppTitle = "QL Label Formats";  // the product name (Start menu), in every language
        public static string NoPrinterText { get { return Lang.F("No {0} printer uses the QL Unidrv driver.", QlModel.AllNames); } }

        List<LabelFormat> formats = new List<LabelFormat>();
        List<LabelFormat> saved = new List<LabelFormat>();
        bool dirty, busy, loading;

        readonly ListView list = new ListView();
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
            Text = AppTitle;
            Theme.Use(this);
            Theme.Changed += ThemeChanged;
            FormClosed += delegate { Theme.Changed -= ThemeChanged; };
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;  // scales nothing here (no AutoScaleDimensions): sizes go through S()
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            // The font already follows the display scaling; the window's own sizes are 100 % pixels scaled the same way.
            // 100 % pixels scaled to this display, never larger than its working area (150 % on a small screen).
            var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
            ClientSize = new Size(Math.Min(S(1000), screen.Width * 9 / 10), Math.Min(S(620), screen.Height * 9 / 10));
            MinimumSize = new Size(Math.Min(S(760), screen.Width), Math.Min(S(480), screen.Height));  // the command row fits (German)
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            BuildTools();
            BuildMenu();
            BuildEditor();
            BuildList();
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
            // WinForms scales the fonts when the window gets another DPI (opened on or moved to a screen with other
            // scaling); the pixel sizes set through S() follow here (as in the label editor). Brand.MatchScreenDpi: a
            // window created directly on a second screen with other scaling (OnHandleCreated, and here).
            Load += delegate { Brand.MatchScreenDpi(this); Rescale(); FitList(); };
            DpiChanged += delegate { Brand.FollowFont(this, Font); BeginInvoke((MethodInvoker)delegate { Rescale(); FitList(); }); };

            LoadFromDisk();
            UpdatePrinterStatus();
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
            Shown += delegate { CheckPrinter(); printerTimer.Start(); TrayMonitor.EnsureRunning(); };
        }

        // 100 % pixels to this display's pixels. Before the window exists DeviceDpi is Windows' system DPI, which is not the
        // screen's after the scaling was changed without signing out again, or on a second screen: then the screen the
        // window opens on (WinForms scales the fonts to it when the window is created, the sizes set here it doesn't).
        int S(int pixels) { return IsHandleCreated ? LogicalToDeviceUnits(pixels) : (int)Math.Round(pixels * startDpi / 96.0); }

        readonly int startDpi = ScreenDpi(Cursor.Position);

        static int ScreenDpi(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */), 0 /* MDT_EFFECTIVE_DPI */, out x, out y) == 0 && x > 0)
                    return (int)x;
            }
            catch (Exception) { }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, int flags);
        [System.Runtime.InteropServices.DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        // Created directly on a second screen with other scaling (Brand.MatchScreenDpi): WinForms then scales as on a move,
        // keeping the window's size (the sizes set through S() are already the screen's; Rescale corrects the rest).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.MatchScreenDpi(this);
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
        // label editor's command row), the main action (Save & apply) petrol with white text (Brand.StylePrimary).
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
                await StatusForm.UseOnline(this, offlinePrinters.ToList());
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
            monitorTip.AddRange(new Control[] { tip, on, status, later });
            offlineTip.AddRange(new Control[] { offlineText, online });
            foreach (var link in new[] { on, online, status, later }) Brand.StyleLink(link);
            row.Controls.AddRange(new Control[] { tip, offlineText, on, online, status, later });
            tipBar.Controls.Add(row);
            tipBar.SizeChanged += delegate { FitTipBar(); };
            UpdateTipBar();
        }

        // The tip bar as high as its text and links need at the window's width (more than one line at 150 % or in German).
        readonly FlowLayoutPanel tipRow = new FlowLayoutPanel();

        void FitTipBar()
        {
            int w = Math.Max(S(200), tipBar.ClientSize.Width - tipBar.Padding.Horizontal);
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
                var model = ModelOf(testPrinter);
                string text = Lang.F("{0} is set to offline – jobs wait on this PC.", model != null ? model.Name : testPrinter);
                if (offlineText.Text != text) offlineText.Text = text;
                string tip = Lang.F("Windows' 'Use Printer Offline' is on for {0}: print jobs wait on this PC and don't reach the printer. 'Use online' clears it; Windows may ask for administrator permission.", testPrinter);
                editorTips.SetToolTip(offlineText, tip);
                if (offlineOnline != null) editorTips.SetToolTip(offlineOnline, tip);
            }
            foreach (var c in monitorTip) c.Visible = !offline;
            foreach (var c in offlineTip) c.Visible = offline;
            tipBar.Visible = offline || (!dismissed && !TrayMonitor.AutostartEnabled);
            if (tipBar.Visible) FitTipBar();
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
            dirty = value;
            Text = AppTitle + (dirty ? " *" : "");
        }

        // ---------- commands ----------
        string UniqueName(string name)
        {
            var names = new HashSet<string>(formats.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(name)) return name;
            for (int i = 2; ; i++)
            {
                string suffix = " (" + i + ")";
                string candidate = (name.Length + suffix.Length > LabelStore.MaxNameLength
                    ? name.Substring(0, LabelStore.MaxNameLength - suffix.Length) : name) + suffix;
                if (!names.Contains(candidate)) return candidate;
            }
        }

        // A key no format of this list or of the applied one has: a new format never looks like a rename of a deleted one
        // (Apply moves the default label format along with a rename, by key).
        int NewKey() { return LabelStore.NextKey(formats.Concat(saved)); }

        void Insert(LabelFormat f)
        {
            int at = Selected != null ? formats.IndexOf(Selected) + 1 : formats.Count;
            formats.Insert(at, f);
            FillList(f);
            SetDirty(true);
            nameBox.Focus();
            nameBox.SelectAll();
        }

        void NewFormat()
        {
            var m = MediaTable.ById(259);
            Insert(new LabelFormat { Key = NewKey(), Name = UniqueName(Lang.T("New label")), PaperId = m.Id, LengthMm = 50, FeedMm = 3 });
        }

        // A new format of this continuous paper and length, named after its size ("62 × 40 mm").
        public void NewFormat(int paperId, double lengthMm)
        {
            var m = MediaTable.ById(paperId);
            if (m == null || !m.Continuous) { NewFormat(); return; }
            Insert(new LabelFormat { Key = NewKey(), Name = UniqueName(Media.Mm(m.WidthMm) + " × " + Media.Mm(lengthMm) + " mm"), PaperId = m.Id, LengthMm = lengthMm, FeedMm = 3 });
        }

        void Duplicate()
        {
            var f = Selected;
            if (f == null) return;
            var copy = f.Clone();
            copy.Key = NewKey();
            copy.Name = UniqueName(f.Name);
            Insert(copy);
        }

        void DeleteSelected()
        {
            var f = Selected;
            if (f == null) return;
            string text = Lang.F("Delete the label format '{0}'?", f.Name);
            var defaultOn = saved.Any(s => string.Equals(s.Name, f.Name, StringComparison.OrdinalIgnoreCase)) ? DefaultOn(f.Name) : new List<string>();
            if (defaultOn.Count > 0)
                text += "\n\n" + Lang.F("It is your default label format on {0}. After Save & apply, programs there start with another paper size; choose a new default then.",
                                        string.Join(", ", defaultOn));
            if (QlMessage.Show(this, text, AppTitle, MessageBoxButtons.YesNo, defaultOn.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            int i = formats.IndexOf(f);
            formats.Remove(f);
            FillList(formats.Count == 0 ? null : formats[Math.Min(i, formats.Count - 1)]);
            SetDirty(true);
        }

        // Moves within the format's group (the list shows the groups separately).
        void MoveSelected(int delta)
        {
            var f = Selected;
            if (f == null) return;
            string g = GroupKey(f);
            int i = formats.IndexOf(f), j = i + delta;
            while (j >= 0 && j < formats.Count && GroupKey(formats[j]) != g) j += delta;
            if (j < 0 || j >= formats.Count) return;
            formats[i] = formats[j];
            formats[j] = f;
            FillList(f);
            SetDirty(true);
        }

        void Revert()
        {
            if (dirty && QlMessage.Show(this, Lang.T("Discard all unsaved changes?"), AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LoadFromDisk();
        }

        void ReplaceOrAdd(List<LabelFormat> imported, string what)
        {
            // Buttons that say what they do (not Yes/No); Add is the default: nothing is lost.
            DialogResult answer;
            using (var d = new SetupResult(AppTitle, ResultKind.Question, Lang.F("{0} label formats in {1}", imported.Count, what), null,
                                           Lang.T("Replace the current list, or add them to it?"), null, Lang.T("Cancel")))
            {
                HelpPages.SetPage(d, HelpPages.FormatsList);
                d.AddAction(Lang.T("Replace list"), () => { d.DialogResult = DialogResult.Yes; return null; });
                d.AddChoice(Lang.T("Add"), DialogResult.OK);
                answer = d.ShowDialog(this);
            }
            if (answer != DialogResult.Yes && answer != DialogResult.OK) return;
            if (answer == DialogResult.Yes) formats.Clear();
            int skipped = 0;
            foreach (var f in imported)
            {
                if (MediaTable.ById(f.PaperId) == null) { skipped++; continue; }
                f.Name = f.Name.Trim();
                if (f.Name.Length > LabelStore.MaxNameLength) f.Name = f.Name.Substring(0, LabelStore.MaxNameLength).TrimEnd();
                f.Name = UniqueName(f.Name);
                f.Key = NewKey();
                LabelStore.Normalize(f);
                formats.Add(f);
            }
            FillList(formats.FirstOrDefault());
            SetDirty(true);
            if (skipped > 0)
                QlMessage.Ask(this, AppTitle, Lang.F("{0} format(s) skipped: their paper (e.g. a multi-label paper such as \"62mm x 2\") can't be the base of a label format.", skipped),
                              null, MessageBoxButtons.OK, MessageBoxIcon.Information);   // one sentence (Show would split it at its colon)
        }

        void ImportFile(bool brother)
        {
            using (var dlg = new OpenFileDialog { Title = Lang.T("Import label formats") })
            {
                dlg.Filter = brother ? Lang.T("Brother label formats (*.dat;*.org)|*.dat;*.org|All files|*.*") : Lang.T("Label formats (*.json)|*.json|All files|*.*");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var imported = brother ? BrotherDat.Read(dlg.FileName) : LabelStore.Load(dlg.FileName).Formats;
                    ReplaceOrAdd(imported, Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex) { Failed(Lang.T("The file couldn't be imported."), ex); }
            }
        }

        // Brother's German file, embedded in development builds only; in English the UK or US seed list instead.
        static Stream FactoryStream() { return Lang.Current == "de" ? Assembly.GetExecutingAssembly().GetManifestResourceStream("bsq80V.ORG") : null; }

        void LoadFactory()
        {
            var factory = FactoryFormats();
            if (factory != null) ReplaceOrAdd(factory, Lang.T("Brother's factory list"));
        }

        // Brother's factory list, or null (with a message when the file is there but can't be read).
        List<LabelFormat> FactoryFormats()
        {
            using (var s = FactoryStream())
            {
                if (s != null) return BrotherDat.Read(s);
            }
            // Builds without Brother's file, and English: the same list as JSON (LabelStore.FindSeed, installed next to the exe)
            string seed = LabelStore.FindSeed(Lang.Current);
            if (seed == null) return null;
            try { return LabelStore.Load(seed).Formats; }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.F("Cannot read {0}:", seed) + "\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        void ExportFile(bool brother)
        {
            using (var dlg = new SaveFileDialog { Title = Lang.T("Export label formats") })
            {
                dlg.Filter = brother ? Lang.T("Brother label formats (*.dat)|*.dat|All files|*.*") : Lang.T("Label formats (*.json)|*.json|All files|*.*");
                dlg.FileName = brother ? "bsq80V.DAT" : "label-formats.json";
                if (brother)
                {
                    var lossy = formats.Where(f => !BrotherDat.CanEncode(f.Name)).Select(f => f.Name).ToList();
                    string warn = "";
                    if (formats.Count > BrotherDat.MaxFormats) warn += Lang.F("Brother's driver reads at most {0} formats; only the first {0} are exported.", BrotherDat.MaxFormats) + "\n";
                    if (lossy.Count > 0) warn += Lang.T("These names contain characters the Brother file cannot store:") + " " + string.Join(", ", lossy) + "\n";
                    if (warn.Length > 0 && QlMessage.Show(this, warn + "\n" + Lang.T("Export anyway?"), AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (brother) BrotherDat.Write(dlg.FileName, formats.Take(BrotherDat.MaxFormats).ToList());
                    else File.WriteAllText(dlg.FileName, LabelStore.ToJson(new LabelSet { Formats = formats }), new UTF8Encoding(false));
                    applyStatus.Text = Lang.F("Exported to {0}", dlg.FileName);
                }
                catch (Exception ex) { Failed(Lang.T("The file couldn't be exported."), ex); }
            }
        }

        // ---------- save & apply ----------
        async void SaveAndApply() { await Apply(); }

        // Save & apply; true when Windows has the list now.
        async Task<bool> Apply()
        {
            if (busy) return false;
            if (ChangedElsewhere())
            {
                // Applying this window's older list would remove what the other window added.
                using (var d = new SetupResult(AppTitle, ResultKind.Warning, Lang.T("The label formats were changed in another window"), null,
                                               Lang.T("Saving now would undo those changes. Reload shows them here; your unsaved changes in this window are then lost."),
                                               null, Lang.T("Cancel")))
                {
                    d.AddChoice(Lang.T("Reload"), DialogResult.Retry);
                    if (d.ShowDialog(this) == DialogResult.Retry) LoadFromDisk();
                }
                return false;
            }
            Validate();
            foreach (var f in formats) { f.Name = f.Name.Trim(); LabelStore.Normalize(f); }
            List<FormInfo> forms = null;
            try { forms = FormsDb.Enumerate(); } catch (Exception) { }
            List<string> owned;
            try { string unreadable; owned = LabelStore.LoadCurrent(out unreadable).OwnedForms; }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.T("Cannot read the label formats:") + "\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            var errors = LabelStore.Validate(formats, forms, owned);
            if (errors.Count > 0)
            {
                QlMessage.Ask(this, AppTitle, Lang.T("Please correct these entries first."), string.Join("\n", errors.Take(12)) + (errors.Count > 12 ? "\n..." : ""),
                              MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var moves = DefaultsToMove();
            string input = Path.Combine(Path.GetTempPath(), "QlLabelFormats-apply-" + Guid.NewGuid().ToString("N") + ".json");
            string result = Path.ChangeExtension(input, ".txt");
            File.WriteAllText(input, LabelStore.ToJson(new LabelSet { Formats = formats }), new UTF8Encoding(false));
            SetBusy(true, Lang.T("Applying (administrator rights)..."));
            applying = true;
            int code;
            try
            {
                // --lang: the elevated copy may run as another user (other settings), and writes its summary in this language.
                var psi = new ProcessStartInfo(Application.ExecutablePath, "--apply \"" + input + "\" --result \"" + result + "\" --lang " + Lang.Current)
                {
                    UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden,
                };
                code = await Task.Run(() =>
                {
                    using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; }
                });
            }
            catch (Win32Exception ex)
            {
                SetBusy(false, ex.NativeErrorCode == 1223 ? Lang.T("Not applied: administrator rights were declined.") : Lang.T("Not applied:") + " " + ex.Message);
                TryDelete(input);
                return false;
            }
            string text = File.Exists(result) ? File.ReadAllText(result, Encoding.UTF8).Trim() : Lang.T("(no result)");
            TryDelete(input);
            TryDelete(result);
            if (code == 0)
            {
                LoadFromDisk();
                string summary = text.Split('\n').Last().Trim();
                var moved = new List<string>();
                foreach (var m in moves)
                {
                    try { Spooler.SetDefaultForm(m.Key, m.Value); moved.Add(m.Key); }
                    catch (Exception ex)
                    {
                        QlMessage.Show(this, Lang.T("The default label format could not be set:") + "\n\n" + m.Key + ": " + ex.Message, AppTitle,
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                if (moves.Count > 0) RefreshDefault();
                if (moved.Count > 0) summary += " " + Lang.F("The default label format follows the new name on {0}.", string.Join(", ", moved));
                SetBusy(false, Lang.F("Applied {0}: {1}", DateTime.Now.ToString("t"), summary));
                return true;
            }
            SetBusy(false, Lang.T("Apply failed."));
            QlMessage.Show(this, Lang.T("The label formats could not be applied:") + "\n\n" + text, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        static void TryDelete(string path) { try { File.Delete(path); } catch (Exception) { } }

        // Another QL Label Formats window (or another user) applied a list since this window read labels.json. Compares the
        // formats, not the file's text (an update's --sync writes the same list again).
        bool ChangedElsewhere()
        {
            List<LabelFormat> now;
            try
            {
                if (!File.Exists(LabelStore.JsonPath)) return false;
                now = LabelStore.Load(LabelStore.JsonPath).Formats;
            }
            catch (Exception) { return false; }  // damaged: LoadFromDisk's question
            if (damaged != null) return true;    // was damaged here, readable now: someone applied a list
            foreach (var f in now) LabelStore.Normalize(f);
            return now.Count != saved.Count || now.Zip(saved, (a, b) => a.SameAs(b)).Any(same => !same);
        }

        // The last action's line at the right of the status bar. Right-aligned text longer than its room ran out to the left
        // under the item before it ("…aktuell d“ an Brother QL-570 gesendet.", x64 check 2026-10-31): it is cut at the end
        // with "…" instead (the whole text as its tooltip), after a separator.
        readonly ToolStripSeparator applySeparator = new ToolStripSeparator { Visible = false };
        string applyText = "", applyShown = "";
        bool fittingApply, fitPending;

        void FitApplyStatus()
        {
            if (fittingApply) return;
            fittingApply = true;
            try
            {
                // New text set from outside (the status bar may lay out inside the Text setter, before TextChanged)
                if (applyStatus.Text != applyShown) applyText = applyStatus.Text;
                int room = statusBar.DisplayRectangle.Width - S(16) - applyStatus.Padding.Horizontal - applyStatus.Margin.Horizontal;
                foreach (ToolStripItem item in statusBar.Items)
                    if (item != applyStatus && item.Available) room -= item.Width + item.Margin.Horizontal;
                string shown = applyText ?? "";
                if (shown.Length > 0 && TextRenderer.MeasureText(shown, applyStatus.Font).Width > room)
                {
                    int n = shown.Length;
                    while (n > 0 && TextRenderer.MeasureText(shown.Substring(0, n).TrimEnd() + "…", applyStatus.Font).Width > room) n--;
                    shown = n > 0 ? shown.Substring(0, n).TrimEnd() + "…" : "";
                }
                applyShown = shown;
                if (applyStatus.Text != shown) applyStatus.Text = shown;
                string tip = shown != applyText ? (applyText ?? "").Replace("&&", "&") : "";
                if (applyStatus.ToolTipText != tip) applyStatus.ToolTipText = tip;
                bool separator = shown.Length > 0;
                if (applySeparator.Visible != separator) applySeparator.Visible = separator;
            }
            finally { fittingApply = false; }
        }

        bool applying;  // busy with Save & apply (the elevated --apply), not a roll check

        // A status bar item draws "&" as a shortcut mark ("Save & apply" lost its "&"): doubled, it shows as typed.
        static string StatusText(string text) { return text.Replace("&", "&&"); }

        void SetBusy(bool value, string message)
        {
            busy = value;
            if (!value) applying = false;
            tools.Enabled = !value;
            menu.Enabled = !value;
            list.Enabled = !value;
            editor.Enabled = !value && Selected != null;
            UseWaitCursor = value;
            applyStatus.Text = message;
        }

        void UpdateSyncStatus()
        {
            syncStatus.Text = damaged != null ? Lang.T("Label list damaged: Save && apply writes it again")
                            : File.Exists(LabelStore.JsonPath) ? Sync.Status(saved) : Lang.T("Not applied to Windows yet");
        }

        // ---------- printers and test print ----------
        internal static List<string> OurPrinters()
        {
            var names = new List<string>();
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers"))
                {
                    if (key == null) return names;
                    foreach (string n in key.GetSubKeyNames())
                        using (var k = key.OpenSubKey(n))
                            if (k != null && QlModel.ForDriver(Convert.ToString(k.GetValue("Printer Driver"))) != null)
                                names.Add(n);
                }
            }
            catch (Exception) { }
            string windowsDefault = null;
            try { windowsDefault = new PrinterSettings().PrinterName; } catch (Exception) { }
            return InOrder(Listed(names, Spooler.ListedPrinters()), windowsDefault);
        }

        // Only the printers Windows lists: not one removed but pending deletion ("<name>,0" until the spooler restarts).
        // listed null = Windows couldn't be asked: all of them.
        internal static IEnumerable<string> Listed(IEnumerable<string> printers, ICollection<string> listed)
        {
            return listed == null ? printers : printers.Where(listed.Contains);
        }

        // Windows' default printer first when it is one of them (the one preselected), then by name.
        internal static List<string> InOrder(IEnumerable<string> printers, string windowsDefault)
        {
            return printers.OrderBy(n => string.Equals(n, windowsDefault, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                           .ThenBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        // The model a printer's driver is for, or null.
        internal static QlModel ModelOf(string printer)
        {
            if (printer == null) return null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k != null ? QlModel.ForDriver(Convert.ToString(k.GetValue("Printer Driver"))) : null;
            }
            catch (Exception) { return null; }
        }

        // Is the format's Brother paper in the test printer's paper table (a QL-570 has no 62 × 60 mm labels),
        // and is a continuous format long enough for it (QL-500/550/560/650TD: at least 25 mm)?
        bool ModelLacks(LabelFormat f)
        {
            return f != null && testModel != null && MediaTable.ById(f.PaperId) != null &&
                   (!testModel.HasPaper(f.PaperId) || TooShort(f));
        }

        bool TooShort(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            return m != null && m.Continuous && f.LengthMm < testModel.MinLength01 / 10.0;
        }

        string LacksText(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            if (testModel.HasPaper(f.PaperId) && TooShort(f))
                return Lang.F("Not on {0}: the {1}'s continuous labels are at least {2} mm long (the driver would print this one {2} mm long).",
                              testPrinter, testModel.Name, Media.Mm(testModel.MinLength01 / 10.0));
            return Lang.F("Not on {0}: the {1} has no {2} labels (Brother's driver for it doesn't list this paper).",
                          testPrinter, testModel.Name, m != null ? m.Display : "?");
        }

        // The chosen printer is still set up (it may have been removed since the list was read): never print or set the
        // default on another one without the user seeing it.
        bool PrinterStillThere()
        {
            string before = testPrinter;
            UpdatePrinterStatus();
            if (testPrinter == before) return true;
            QlMessage.Show(this, Lang.F("{0} is no longer set up on this PC. Choose the printer in the toolbar.", before), AppTitle,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        // Test print, default format and roll check on this printer (Printer setup window, after adding it).
        public void UseTestPrinter(string printer)
        {
            testPrinter = printer;
            UpdatePrinterStatus();
        }

        void UpdatePrinterStatus()
        {
            var printers = OurPrinters();
            string version = AboutForm.DriverVersion();
            string before = shownPrinter;  // not testPrinter: the printer box sets that before calling this
            if (testPrinter == null || !printers.Contains(testPrinter)) testPrinter = printers.FirstOrDefault();
            shownPrinter = testPrinter;
            if (testPrinter != before || testModel == null)
            {
                testModel = ModelOf(testPrinter);
                list.BeginUpdate();
                foreach (ListViewItem item in list.Items) StyleItem(item);
                list.EndUpdate();
                ShowInfo(Selected);
            }
            if (testPrinter != before || defaultForm == null) RefreshDefault();
            if (testPrinter != before && before != null) { printerCheck = null; ShowPrinterCheck(); CheckPrinter(); }
            // Short (a status label that doesn't fit is hidden, with everything after it). The list is in the Printer
            // setup window (a click), not in a tooltip: a tall one ran into the taskbar.
            printerStatus.Text = version == null ? Lang.T("Driver not installed")
                : Lang.F("QL Unidrv {0} · {1}", version, printers.Count == 0 ? Lang.T("no printer") : printers.Count == 1 ? printers[0] : Lang.F("{0} QL printers", printers.Count));
            printerStatus.ToolTipText = Lang.T("Click for the QL printers on this PC (Printer setup)");
            printButton.Enabled = testPrinter != null;
            printButton.ToolTipText = testPrinter == null ? NoPrinterText
                : Lang.F("Print a sample with the selected format on {0} (F5)", testPrinter);
            FillPrinterBox(printers);
        }

        // The toolbar's printer choice: test print, default format and roll check. The label list itself is shared.
        void FillPrinterBox(List<string> printers)
        {
            fillingPrinters = true;
            try
            {
                var items = printerBox.Items.Cast<object>().Select(o => o.ToString()).ToList();
                if (!items.SequenceEqual(printers))
                {
                    printerBox.Items.Clear();
                    foreach (var n in printers) printerBox.Items.Add(n);
                }
                printerBox.SelectedItem = testPrinter;
                printerBox.Enabled = printers.Count > 0;
                MeasurePrinterBox();
                printerBox.ToolTipText = testPrinter == null ? NoPrinterText
                    : Lang.F("Test print, default label format and the roll check use {0}.\nThe label formats themselves are shared by all QL printers.", testPrinter);
            }
            finally { fillingPrinters = false; }
        }

        async void TestPrint()
        {
            var f = Selected;
            if (f == null || testPrinter == null || busy || !PrinterStillThere()) return;
            if (ModelLacks(f))
            {
                QlMessage.Ask(this, AppTitle, Lang.F("'{0}' can't be printed on {1}.", f.Name, testPrinter),
                              LacksText(f) + "\n\n" + Lang.T("Choose another printer in the toolbar, or base the format on another paper."),
                              MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!IsApplied(f))
            {
                if (!await ApplyFirst(f, Lang.T("Test print"))) return;
                f = Selected;  // the list was read again
            }
            if (!await ConfirmPrinterReady(f)) return;
            string printer = testPrinter;
            try
            {
                var doc = new PrintDocument { DocumentName = Lang.T("QL label format test:") + " " + f.Name };
                doc.PrinterSettings.PrinterName = printer;
                var size = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .FirstOrDefault(p => string.Equals(p.PaperName, f.Name, StringComparison.OrdinalIgnoreCase));
                if (size == null)
                {
                    QlMessage.Show(this, Lang.F("{0} doesn't list '{1}' yet. Click Save & apply; if it stays that way, update the driver.", printer, f.Name), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                doc.DefaultPageSettings.PaperSize = size;
                doc.DefaultPageSettings.Landscape = false;
                doc.PrintController = new StandardPrintController();
                doc.PrintPage += (s, e) => TestLabel.DrawSample(e, f);
                doc.Print();
                applyStatus.Text = Lang.F("Test print of '{0}' sent to {1}.", f.Name, printer);
                // also without focus (printerTimer then pauses)
                string sentTo = printer;
                PrinterCheck.AskAfterPrint(() => printerCheck, CheckPrinter, () => IsDisposed || testPrinter != sentTo);
            }
            catch (Exception ex) { Failed(Lang.T("The test print couldn't be sent."), ex); }
        }

        // The label editor QlLabelMaker.exe (its own process, so it stays open when this window closes).
        void OpenEditor(LabelFormat f)
        {
            try { EditorProgram.Start(null, testPrinter, f != null ? f.Name : null); }
            catch (Exception ex) { Failed(Lang.F("{0} couldn't be started.", "QL Label Maker"), ex); }
        }

        // Windows knows the format as it is shown (saved and applied, no pending edits).
        bool IsApplied(LabelFormat f)
        {
            var applied = saved.FirstOrDefault(s => string.Equals(s.Name, f.Name, StringComparison.OrdinalIgnoreCase));
            return applied != null && applied.SameAs(f) && File.Exists(LabelStore.JsonPath);
        }

        // A new or changed format: offers Save & apply, then the action goes on. True when the format (still selected)
        // is applied now.
        async Task<bool> ApplyFirst(LabelFormat f, string action)
        {
            string text = Lang.F("'{0}' has unsaved changes, so Windows doesn't know it yet.\n\nSave & apply the label formats now and then continue ({1})? Windows asks for administrator permission.",
                                 f.Name, action);
            if (QlMessage.Show(this, text, AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return false;
            if (!await Apply()) return false;
            return Selected != null && IsApplied(Selected);
        }

        // Asks the printer before a test print (off the UI thread): a format for another roll stops the printer with an error.
        async Task<bool> ConfirmPrinterReady(LabelFormat f)
        {
            string printer = testPrinter;
            bool offline = false;
            try { offline = Spooler.WorkOffline(printer); } catch (Exception) { }
            // Not when the printer is switched off (Windows set offline by itself): the roll check below says so.
            if (offline && printerCheck != null && printerCheck.Printer == printer && !printerCheck.WorkOffline) offline = false;
            if (offline)
            {
                var answer = QlMessage.Show(this, Lang.F("{0} is set to 'Use Printer Offline': the test label would wait on this PC and not print.\n\n" +
                                                          "Use the printer online now? Windows may ask for administrator permission.", printer),
                                             AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.No) return true;  // print anyway: it waits in the queue
                bool done = await StatusForm.UseOnline(this, new[] { printer });
                UpdateTipBar();
                if (!done) return false;
            }
            if (!QlDevice.HasStatus(QlDevice.PortOf(printer))) return true;
            PrinterCheck c = null;
            string status = applyStatus.Text;
            SetBusy(true, Lang.F("Asking {0} for the loaded roll...", printer));
            try { c = await Task.Run(() => PrinterCheck.Run(printer, true)); }
            catch (Exception) { }
            SetBusy(false, status);
            if (IsDisposed) return false;
            if (c == null) return true;  // can't be asked: print as before the check existed
            printerCheck = c;
            lastCheck = DateTime.Now;
            ShowPrinterCheck();
            string warn = null;
            var m = MediaTable.ById(f.PaperId);
            bool? fits = c.Media != null ? c.Media.Fits(m) : null;
            if (c.Health == Health.Error || c.Health == Health.Warning)
                warn = printer + ": " + (c.Detail ?? c.Headline) + "\n\n" + string.Join("\n", c.Advice);
            else if (fits == false)
                warn = Lang.F("The printer has {0} loaded, but '{1}' needs {2}.\n\nThe printer would stop with a 'wrong media' error (red LED). Load the matching roll first.",
                              c.Media.LoadedText, f.Name, m != null ? m.Display : Lang.T("another roll"));
            if (warn == null) return true;
            return QlMessage.Show(this, warn + "\n\n" + Lang.T("Print anyway?"), AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        // ---------- printer status ----------
        async void CheckPrinter()
        {
            string p = testPrinter;
            if (checking || busy || p == null || IsDisposed) return;
            if (!QlDevice.HasStatus(QlDevice.PortOf(p)))
            {
                if (printerCheck != null) { printerCheck = null; ShowPrinterCheck(); }
                mediaStatus.Visible = false;
                return;
            }
            checking = true;
            lastCheck = DateTime.Now;
            PrinterCheck c = null;
            try { c = await Task.Run(() => PrinterCheck.Run(p, true)); }
            catch (Exception) { }
            checking = false;
            if (IsDisposed) return;
            if (p != testPrinter) { CheckPrinter(); return; }  // the printer was switched meanwhile
            if (c == null) return;
            printerCheck = c;
            ShowPrinterCheck();
            UpdateTipBar();
        }

        void ShowPrinterCheck()
        {
            var c = printerCheck;
            mediaStatus.Visible = c != null;
            if (c != null)
            {
                mediaStatus.Text = testPrinter + ": " + c.Short;
                Theme.ItemLink(mediaStatus, c.Health == Health.Error ? Role.ErrorText : c.Health == Health.Warning ? Role.WarningText : Role.Text);
                mediaStatus.ToolTipText = (c.Detail ?? c.Headline) + (c.Advice.Count > 0 ? "\n" + string.Join("\n", c.Advice) : "") + "\n" + Lang.T("Click for details (F6).");
            }
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) StyleItem(item);
            list.EndUpdate();
            ShowInfo(Selected);
        }

        QlStatus LoadedRoll
        {
            get { return printerCheck != null && printerCheck.Media != null && !printerCheck.Media.NoMedia ? printerCheck.Media : null; }
        }

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

        // ---------- default label format ----------
        void RefreshDefault()
        {
            defaultForm = null;
            if (testPrinter != null)
                try { defaultForm = Spooler.DefaultFormName(testPrinter); } catch (Exception) { }
            list.BeginUpdate();
            foreach (ListViewItem item in list.Items) StyleItem(item);
            list.EndUpdate();
            ShowInfo(Selected);
        }

        // The QL printers whose default label format (for this user) is the paper size `name`.
        static List<string> DefaultOn(string name)
        {
            return OurPrinters().Where(p =>
            {
                try { return string.Equals(Spooler.DefaultFormName(p), name, StringComparison.OrdinalIgnoreCase); }
                catch (Exception) { return false; }
            }).ToList();
        }

        // Renamed formats that are a printer's default (printer, new name): Apply moves the default to the new name, since
        // the paper size with the old name is removed.
        List<KeyValuePair<string, string>> DefaultsToMove()
        {
            var moves = new List<KeyValuePair<string, string>>();
            if (!File.Exists(LabelStore.JsonPath) || damaged != null) return moves;
            foreach (var f in formats)
            {
                var old = saved.Where(s => s.Key == f.Key).ToList();
                if (old.Count != 1 || string.Equals(old[0].Name, f.Name, StringComparison.OrdinalIgnoreCase)) continue;
                if (formats.Any(x => string.Equals(x.Name, old[0].Name, StringComparison.OrdinalIgnoreCase))) continue;  // the old name stays
                foreach (var p in DefaultOn(old[0].Name)) moves.Add(new KeyValuePair<string, string>(p, f.Name));
            }
            return moves;
        }

        void UpdateDefaultItem(ToolStripMenuItem item)
        {
            var f = Selected;
            item.Text = f == null ? Lang.T("Use as default label format") : Lang.F("Use '{0}' as default label format", f.Name);
            item.Checked = IsDefault(f);
            item.Enabled = f != null && testPrinter != null && !IsDefault(f) && !ModelLacks(f) && !busy;
            item.ToolTipText = testPrinter == null ? "" : Lang.F("Programs then start with this format on {0} (for your user account).", testPrinter);
        }

        async void SetAsDefault()
        {
            var f = Selected;
            if (f == null || testPrinter == null || busy || !PrinterStillThere() || ModelLacks(f)) return;
            if (!IsApplied(f))
            {
                if (!await ApplyFirst(f, Lang.T("Set as default"))) return;
                f = Selected;
            }
            try
            {
                Spooler.SetDefaultForm(testPrinter, f.Name);
                RefreshDefault();
                if (!IsDefault(f)) throw new InvalidOperationException(Lang.F("The printer kept '{0}'.", defaultForm));
                applyStatus.Text = StatusText(Lang.F("Default label format on {0}: '{1}' (in effect now; no Save & apply needed).", testPrinter, f.Name));
            }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.T("The default label format could not be set:") + "\n\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ---------- drag and drop (within a group) ----------
        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            if (busy) return;
            dragItem = (ListViewItem)e.Item;
            try { list.DoDragDrop(e.Item, DragDropEffects.Move); }
            finally { dragItem = null; }
        }

        // Moves the dragged format while the mouse moves, so the list shows the new order at once.
        void OnDragOver(object sender, DragEventArgs e)
        {
            if (dragItem == null) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            var pt = list.PointToClient(new Point(e.X, e.Y));
            var target = list.GetItemAt(pt.X, pt.Y);
            if (target == null || target == dragItem) return;
            var f = (LabelFormat)dragItem.Tag;
            var t = (LabelFormat)target.Tag;
            if (GroupKey(t) != GroupKey(f)) { e.Effect = DragDropEffects.None; return; }
            int to = formats.IndexOf(t);
            formats.Remove(f);
            formats.Insert(to, f);
            FillList(f);
            dragItem = list.Items.Cast<ListViewItem>().First(i => i.Tag == f);
            SetDirty(true);
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
