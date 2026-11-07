// Label editor window ("QL Label Maker", QlLabelMaker.exe [file] [--printer name] [--format name], EditorProgram.cs):
// design a label with text, lines, boxes and pictures on a label format or Brother paper and print it.
// The canvas shows the label as it prints (EditorRender: 1-bit at the printer's dots); printing sends that bitmap
// dot for dot (EditorPrint). Files: .qllabel (JSON, EditorModel). Plan: label editor phase 1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class EditorForm : Form
    {
        public const string AppTitle = "QL Label Maker";   // the product name (Start menu), in every language

        readonly EditorCanvas canvas = new EditorCanvas { Dock = DockStyle.Fill };
        readonly Panel host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        // Over the canvas while objects reach beyond the label: how many, and that those parts don't print. Information
        // only: the objects stay visible and movable on the grey margin around the label.
        readonly Panel outsideBar = new Panel { Visible = false };
        string outsideText = "";
        LabelDoc doc = new LabelDoc();
        readonly UndoStack undo = new UndoStack();
        string path;
        bool dirty, loading, printing;
        // The opened file's label format isn't on this printer (shown on another label): Save keeps the file's format
        // until a label is picked in the Label box or a printer offers it.
        bool keepFileFormat;
        object lastEdited;   // the property control whose edits share one undo step

        List<string> printers = new List<string>();
        string printer;
        QlModel model;
        List<LabelChoice> choices = new List<LabelChoice>();
        LabelChoice label;
        bool highRes;
        LabelLayout layout;
        PrinterCheck roll;

        // Command row (one slim row that fits at 150 % in German: no overflow): printer, label, copies, Print.
        readonly ComboBox printerBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly ComboBox labelBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        readonly NumericUpDown copies = new NumericUpDown { Minimum = 1, Maximum = 999, Value = 1, TextAlign = HorizontalAlignment.Right };
        // The window's main action, in the petrol accent (Brand.StylePrimary).
        readonly System.Windows.Forms.Button printButton = new System.Windows.Forms.Button { AutoSize = true };
        readonly ToolTip tips = new ToolTip();
        readonly ToolStripMenuItem undoItem = new ToolStripMenuItem(), redoItem = new ToolStripMenuItem();
        readonly ToolStripMenuItem highResItem = new ToolStripMenuItem(), rollItem = new ToolStripMenuItem();
        readonly ToolStripStatusLabel labelStatus = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        readonly ToolStripStatusLabel rollStatus = new ToolStripStatusLabel();
        // Zoom in the status bar (Office style): Fit (a toggle), −, the slider, +, the percentage with its menu.
        readonly ToolStripButton fitButton = new ToolStripButton();
        readonly ToolStripButton zoomOutButton = new ToolStripButton("−"), zoomInButton = new ToolStripButton("+");
        readonly ZoomSlider slider = new ZoomSlider();
        ToolStripControlHost sliderHost;
        readonly ToolStripDropDownButton zoomButton = new ToolStripDropDownButton();
        double zoom;   // percent, 100 = real size on this screen; 0 = fit to the window
        int wheel;     // Ctrl+mouse wheel: the part of a notch not used yet (precision touchpads)
        DateTime lastRollCheck;   // CheckRoll's last start (OnActivated reads the roll again after 3 s)

        // label settings (the panel when nothing is selected)
        ComboBox viewBox;
        CheckBox highResCheck;
        // A length of its own on continuous tape (LabelChoice.Custom): the panel's field, "Automatic from the content",
        // a length outside the printer's range (printing stops with it), the fixed length of die-cut and round labels.
        NumericUpDown lengthNum;
        CheckBox autoCheck;
        Label lengthError, lengthFixed;
        bool autoLength;

        // properties
        readonly TableLayoutPanel props = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Top };
        readonly List<KeyValuePair<Control[], Func<LabelObject, bool>>> rows = new List<KeyValuePair<Control[], Func<LabelObject, bool>>>();
        readonly Label nothingSelected = Theme.Fore(new Label { AutoSize = true }, Role.Dim);
        readonly Label severalSelected = new Label { AutoSize = true };
        NumericUpDown numX, numY, numW, numH, sizeNum, thickNum, lineHeightNum, spacingNum, lineAngleNum, lineLengthNum;
        NumericUpDown angleNum;
        Button resetButton;
        readonly List<KeyValuePair<Button, ArrangeIcon>> turnButtons = new List<KeyValuePair<Button, ArrangeIcon>>();   // redrawn at each DPI
        ComboBox rotBox, fontBox, alignBox, modeBox, kindBox, levelBox, textKindBox, qrTypeBox;
        Label notationLabel;
        readonly Dictionary<string, Control> qrEditors = new Dictionary<string, Control>();   // a QR type's fields by key
        TextBox textBox, dataBox;
        CheckBox boldCheck, italicCheck, underlineCheck, filledCheck, ellipseCheck, invertCheck, textInvertCheck, qrInvertCheck, showTextCheck;
        Label problemLabel, quietLabel, svgLabel;
        FlowLayoutPanel quietRow;
        // Codes with something in their quiet zone and the topmost object there (EditorRender.QuietZoneIntruders).
        List<KeyValuePair<BarcodeObject, LabelObject>> intruders = new List<KeyValuePair<BarcodeObject, LabelObject>>();
        TrackBar thresholdBar;

        public EditorForm(string file, string printerName, string formName)
        {
            Text = AppTitle;
            Theme.Use(this);
            Theme.Changed += ThemeChanged;
            FormClosed += delegate { Theme.Changed -= ThemeChanged; };
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;  // scales nothing here (no AutoScaleDimensions): sizes go through S()
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(S(1100), area.Width), Math.Min(S(680), area.Height));
            MinimumSize = new Size(Math.Min(S(780), area.Width), Math.Min(S(520), area.Height));   // 520: all six tools fit the palette
            KeyPreview = true;
            HelpPages.SetPage(this, HelpPages.LabelMaker);   // F1 in its dialogs (About, language question); its own F1: ProcessCmdKey
            sizedDpi = startDpi;

            BuildProperties();
            side = new Panel { Dock = DockStyle.Right, Width = S(SideWidth), AutoScroll = true, Padding = new Padding(S(16), S(12), S(12), S(12)) };
            side.Controls.Add(props);
            host.Controls.Add(canvas);
            Theme.Back(host, Role.Desk);
            // The canvas with the strip laid over its top (over the canvas only, not the panel or the palette). The strip takes
            // no room: when it comes or goes, the canvas, the zoom and the label stay where they are.
            var canvasArea = new Panel { Dock = DockStyle.Fill };
            canvasArea.Controls.Add(host);
            canvasArea.Controls.Add(outsideBar);
            outsideBar.BringToFront();
            host.Resize += delegate { outsideBar.Width = host.ClientSize.Width; };   // not over the vertical scroll bar
            outsideBar.Paint += PaintOutsideBar;
            outsideBar.Resize += delegate { FitOutsideBar(); outsideBar.Invalidate(); };
            // Right of the palette: the Arrange toolbar over the canvas and the panel.
            var work = new Panel { Dock = DockStyle.Fill };
            work.Controls.Add(canvasArea);
            work.Controls.Add(side);
            work.Controls.Add(BuildArrangeBar());
            Controls.Add(work);
            Controls.Add(BuildPalette());
            Controls.Add(BuildCommandRow());
            Controls.Add(BuildMenu());
            Controls.Add(BuildStatusBar());

            canvas.Doc = doc;
            canvas.SelectionChanged += delegate { lastEdited = null; FillProps(null); UpdateCommands(); };
            canvas.BeforeChange += delegate { PushUndo(null); dragBounds = canvas.Dragging ? SelectionBounds() : (RectangleF?)null; };
            canvas.Changed += delegate { Touched(); if (canvas.Dragging) FillPlace(); else FillProps(null); };
            canvas.KeyDown += OnCanvasKey;
            canvas.MouseWheel += OnCanvasWheel;
            canvas.ViewChanged += delegate
            {
                // after a drag: the whole label once more (during it only the part around the dragged objects)
                bool dragged = dragBounds.HasValue;
                dragBounds = null;
                if (FollowContent()) return;
                if (dragged) Render();   // with the quiet zones and the strip
                else { CheckQuietZones(); ShowInfo(); }
                FillProps(null); ApplyZoom(); canvas.Invalidate();
            };
            canvas.LengthDragged += mm => OwnLength(mm, false);
            canvas.QuietZoneOf = c => layout != null ? EditorRender.QuietZone(c, layout, DateTime.Now) : c.Box;
            canvas.QuietTip = Lang.T("Quiet zone: stays clear, doesn't print.");
            canvas.MouseUp += (s, e) => { if (e.Button == MouseButtons.Right) ShowObjectMenu(e.Location); };
            host.Resize += delegate { ApplyZoom(); };
            FormClosing += OnClosing;
            Shown += delegate { Rescale(); Remeasure(); AlignLeft(); CheckRoll(); };
            // WinForms scales the fonts when the window gets another DPI (opened on or moved to a screen with other
            // scaling); the pixel sizes set through S() follow here.
            DpiChanged += delegate { BeginInvoke((MethodInvoker)delegate { Rescale(); Remeasure(); AlignLeft(); }); };

            printers = MainForm.OurPrinters();
            printer = printerName != null && printers.Contains(printerName) ? printerName : printers.FirstOrDefault();
            FillPrinterBox();
            SetPrinter(printer, formName);
            if (file != null) Open(file);
            FillProps(null);
            UpdateCommands();
            UpdateTitle();
        }

        // Before the window exists DeviceDpi is Windows' system DPI, which can differ from the screen the window opens on
        // (scaling changed without signing out, a second screen): the constructor sizes for that screen (as MainForm).
        int S(int pixels) { return IsHandleCreated ? LogicalToDeviceUnits(pixels) : (int)Math.Round(pixels * startDpi / 96.0); }

        readonly int startDpi = ScreenDpi(Cursor.Position);
        int sizedDpi;   // the DPI the S() sizes were set for

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

        // Opened directly on a screen with other scaling than Windows' system DPI: WinForms scales as on a move there
        // (Brand.MatchScreenDpi); Rescale then corrects what S() had already sized for that screen. Bold texts (Print,
        // the panel's heading) follow the window's font after a DPI change (Print stayed small).
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Brand.MatchScreenDpi(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Brand.MatchScreenDpi(this);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Brand.FollowFont(this, Font);
        }

        // Widths from text: measured again once the window has its screen's fonts (and after a DPI change).
        void Remeasure()
        {
            SizePalette();
            viewBox.Width = Math.Max(S(150), viewBox.Items.Cast<string>().Max(t => TextRenderer.MeasureText(t, viewBox.Font).Width) + S(30));
            if (printers.Count > 0) printerBox.DropDownWidth = Math.Max(S(120), printers.Max(n => TextRenderer.MeasureText(n, printerBox.Font).Width) + S(30));
            if (choices.Count > 0) labelBox.DropDownWidth = Math.Max(S(160), choices.Max(ch => TextRenderer.MeasureText(ch.ToString(), labelBox.Font).Width) + S(30));
            FitPanelBoxes();
            PerformLayout();
        }

        // The panel's drop-downs as wide as their longest item (at least 150, at most what the column leaves with the
        // scroll bar shown); the open list always shows every item whole ("Rastern (Fotos, Graustufen)" was cut).
        void FitPanelBoxes()
        {
            int arrow = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
            var cols = props.GetColumnWidths();
            int room = S(SideWidth) - side.Padding.Horizontal - arrow - (cols.Length > 0 ? cols[0] : 0);
            foreach (var c in props.Controls.OfType<ComboBox>())
            {
                if (c.Items.Count == 0) continue;
                int need = c.Items.Cast<object>().Max(i => TextRenderer.MeasureText(c.GetItemText(i), c.Font).Width) + S(8);
                int list = need + (c.Items.Count > c.MaxDropDownItems ? arrow : 0);
                if (c.DropDownStyle == ComboBoxStyle.DropDownList && c.Dock != DockStyle.Fill)
                    c.Width = Math.Max(S(150), Math.Min(need + arrow, room));
                c.DropDownWidth = Math.Max(c.Width, list);
            }
        }

        const int SideWidth = 360;
        Panel side;  // the properties panel, S(SideWidth) wide: shows whether the sizes fit the window's DPI

        // Scales the sizes set through S() to the window's present DPI: widths and heights of fixed-size controls,
        // margins, paddings, wrap widths, drop-down widths; the palette is drawn again. Menus, the status bar and the
        // canvas scale themselves. On a DPI change WinForms already scales positions, sizes, margins and paddings
        // (and again here would double them); when the window opens on a screen with other scaling it may not. The
        // side panel's width tells which, and the walk does only what is left.
        void Rescale()
        {
            if (sizedDpi == DeviceDpi) return;
            sizedDpi = DeviceDpi;
            MinimumSize = new Size(Math.Min(S(780), Screen.FromControl(this).WorkingArea.Width), Math.Min(S(520), Screen.FromControl(this).WorkingArea.Height));
            float k = (float)S(SideWidth) / side.Width;
            if (Math.Abs(k - 1f) < 0.03f) { SizePalette(); return; }
            Func<int, int> z = v => (int)Math.Round(v * k);
            Func<Padding, Padding> zp = q => new Padding(z(q.Left), z(q.Top), z(q.Right), z(q.Bottom));
            Func<Size, Size> zs = q => new Size(z(q.Width), z(q.Height));
            SuspendLayout();
            try
            {
                Action<Control> walk = null;
                walk = c =>
                {
                    if (c is ToolStrip || c == canvas || c == host) return;
                    if (c != this)
                    {
                        c.Margin = zp(c.Margin);
                        c.Padding = zp(c.Padding);
                        if (c.MaximumSize != Size.Empty) c.MaximumSize = zs(c.MaximumSize);
                        if (c.MinimumSize != Size.Empty) c.MinimumSize = zs(c.MinimumSize);
                        if (!c.AutoSize && c.Dock != DockStyle.Top && c.Dock != DockStyle.Bottom) c.Width = z(c.Width);
                        if (c is TrackBar || (c is TextBox && ((TextBox)c).Multiline)) c.Height = z(c.Height);
                        var combo = c as ComboBox;
                        if (combo != null) combo.DropDownWidth = z(combo.DropDownWidth);
                    }
                    foreach (Control child in c.Controls) walk(child);
                };
                walk(this);
                SizePalette();
            }
            finally { ResumeLayout(true); }
        }

        // ---------- menu and toolbar ----------
        MenuStrip menuBar;
        StatusStrip statusBar;
        TableLayoutPanel commandRow;
        Label printerCaption;

        // One left line for the menu's first word, "Printer:" and the status bar's first text, at Brand.Gutter as in all
        // QL windows. The strips place each item's text themselves (centred, or left for the
        // status bar's label), so their paddings are measured.
        void AlignLeft()
        {
            int gutter = Brand.Gutter(Font.Height);
            var p = commandRow.Padding;
            commandRow.Padding = new Padding(Math.Max(0, gutter - printerCaption.Margin.Left), p.Top, p.Right, p.Bottom);
            foreach (ToolStrip strip in new ToolStrip[] { menuBar, statusBar })
            {
                if (strip.Items.Count == 0) continue;
                var item = strip.Items[0];
                bool left = item.TextAlign == ContentAlignment.MiddleLeft || item.TextAlign == ContentAlignment.TopLeft || item.TextAlign == ContentAlignment.BottomLeft;
                int text = left ? item.Bounds.X + item.Padding.Left
                                : item.Bounds.X + (item.Bounds.Width - TextRenderer.MeasureText(item.Text.Replace("&", ""), item.Font).Width) / 2;
                var q = strip.Padding;
                if (text != gutter) strip.Padding = new Padding(Math.Max(0, q.Left + gutter - text), q.Top, q.Right, q.Bottom);
            }
        }

        MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem(Lang.T("&File"));
            file.DropDownItems.Add(Item(Lang.T("&New"), Keys.Control | Keys.N, delegate { NewLabel(); }));
            file.DropDownItems.Add(Item(Lang.T("&Open..."), Keys.Control | Keys.O, delegate { OpenDialog(); }));
            file.DropDownItems.Add(Item(Lang.T("&Save"), Keys.Control | Keys.S, delegate { Save(false); }));
            file.DropDownItems.Add(Item(Lang.T("Save &as..."), Keys.Control | Keys.Shift | Keys.S, delegate { Save(true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("&Print"), Keys.Control | Keys.P, delegate { Print(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("&Close"), Keys.None, delegate { Close(); }));

            var edit = new ToolStripMenuItem(Lang.T("&Edit"));
            undoItem.Text = Lang.T("&Undo"); undoItem.ShortcutKeys = Keys.Control | Keys.Z; undoItem.Click += delegate { Undo(false); };
            redoItem.Text = Lang.T("&Redo"); redoItem.ShortcutKeys = Keys.Control | Keys.Y; redoItem.Click += delegate { Undo(true); };
            edit.DropDownItems.Add(undoItem);
            edit.DropDownItems.Add(redoItem);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item(Lang.T("D&uplicate"), Keys.Control | Keys.D, delegate { Duplicate(); }));
            edit.DropDownItems.Add(new ToolStripMenuItem(Lang.T("&Delete"), null, delegate { Delete(); }) { ShortcutKeyDisplayString = Lang.T("Del") });
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item(Lang.T("Select &all"), Keys.Control | Keys.A, delegate { SelectAll(); }));

            var insert = new ToolStripMenuItem(Lang.T("&Insert"));
            insert.DropDownItems.Add(Item(Lang.T("&Text"), Keys.Control | Keys.T, delegate { AddText(); }));
            insert.DropDownItems.Add(Item(Lang.T("Text &frame"), Keys.Control | Keys.Shift | Keys.T, delegate { AddTextFrame(); }));
            insert.DropDownItems.Add(Item(Lang.T("&Line"), Keys.Control | Keys.L, delegate { AddLine(); }));
            insert.DropDownItems.Add(Item(Lang.T("&Box"), Keys.Control | Keys.B, delegate { AddBox(); }));
            insert.DropDownItems.Add(Item(Lang.T("&Picture..."), Keys.Control | Keys.I, delegate { AddPicture(); }));
            insert.DropDownItems.Add(Item(Lang.T("Bar&code"), Keys.Control | Keys.K, delegate { AddBarcode(); }));
            insert.DropDownItems.Add(Item(Lang.T("&QR code"), Keys.Control | Keys.Q, delegate { AddQrCode(); }));

            var arrange = new ToolStripMenuItem(Lang.T("&Arrange"));
            arrange.DropDownItems.AddRange(AlignItems().ToArray());
            arrange.DropDownItems.Add(new ToolStripSeparator());
            arrange.DropDownItems.Add(Item(Lang.T("R&otate 90°"), Keys.Control | Keys.R, delegate { Rotate(); }));
            var resetItem = new ToolStripMenuItem(Lang.T("Re&set rotation"), null, delegate { ResetRotation(); });
            arrange.DropDownItems.Add(resetItem);
            arrange.DropDownItems.Add(new ToolStripSeparator());
            arrange.DropDownItems.AddRange(LayerItems().ToArray());
            arrange.DropDownOpening += delegate { UpdateAlignItems(arrange.DropDownItems); UpdateLayerItems(arrange.DropDown); resetItem.Enabled = Turned; };

            // The view: direction and zoom (the label settings in the panel and the zoom in the status bar do the same).
            var view = new ToolStripMenuItem(Lang.T("&View"));
            view.DropDownItems.Add(Item(Lang.T("&Turn view"), Keys.Control | Keys.Shift | Keys.R, delegate { TurnView(); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.AddRange(ZoomItems(true).ToArray());
            view.DropDown.ShowItemToolTips = true;
            view.DropDownOpening += delegate { UpdateZoomItems(view.DropDownItems); };

            var printerMenu = new ToolStripMenuItem(Lang.T("&Printer"));
            highResItem.Text = Lang.T("300 × 600 dpi (sharper, slower)");
            highResItem.Click += delegate { SetHighRes(!highRes); };
            printerMenu.DropDownItems.Add(highResItem);
            printerMenu.DropDownItems.Add(new ToolStripSeparator());
            rollItem.Text = Lang.T("Check the loaded &roll");
            rollItem.Click += delegate { CheckRoll(); };
            printerMenu.DropDownItems.Add(rollItem);
            printerMenu.DropDownItems.Add(Item(Lang.T("Printer status"), Keys.F6, delegate { ShowStatus(); }));
            // As in QL Label Formats: the printer monitor, then the Printer setup window.
            printerMenu.DropDownItems.Add(PrinterMenu.NotifyItem(printerMenu, this, AppTitle, null));
            printerMenu.DropDownItems.Add(new ToolStripSeparator());
            printerMenu.DropDownItems.Add(PrinterMenu.SetupItem(this, RefreshPrinters));
            printerMenu.DropDown.ShowItemToolTips = true;

            // As in QL Label Formats: the other program, then the language of all QL programs.
            var extras = new ToolStripMenuItem(Lang.T("&Tools"));
            extras.DropDownItems.Add(new ToolStripMenuItem(Lang.T("QL Label Formats"), null, delegate { OpenLabelFormats(); })
                { ToolTipText = Lang.T("Add, change and sort the label formats (QL Label Formats)") });
            extras.DropDownItems.Add(new ToolStripSeparator());
            extras.DropDownItems.Add(LanguageSwitch.Menu(SwitchLanguage));
            extras.DropDownItems.Add(Theme.Menu());

            var help = new ToolStripMenuItem(Lang.T("&Help"));
            // F1 opens the page of what is selected (ProcessCmdKey); the menu entry always opens the contents.
            help.DropDownItems.Add(new ToolStripMenuItem(Lang.T("QL Label Maker Help"), null, delegate { HelpPages.Open(this, HelpPages.LabelMaker); })
                { ShortcutKeyDisplayString = "F1" });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Item(Lang.T("About QL Unidrv"), Keys.None, delegate { AboutForm.ShowAbout(this, AppTitle); }));
            menu.Items.AddRange(new ToolStripItem[] { file, edit, view, insert, arrange, printerMenu, extras, help });
            menuBar = menu;
            return menu;
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler click)
        {
            var item = new ToolStripMenuItem(text, null, click);
            if (keys != Keys.None) item.ShortcutKeys = keys;
            return item;
        }

        // The insert tools, always visible on the left (as in Paint): icon over the name.
        ToolStrip BuildPalette()
        {
            palette = new ToolStrip
            {
                Dock = DockStyle.Left, LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow, CanOverflow = false,
                GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System, AutoSize = false
            };
            palette.Items.Add(Tool(ToolIcon.Text, Lang.T("Text"), Lang.T("Add text (Ctrl+T)"), delegate { AddText(); }));
            palette.Items.Add(Tool(ToolIcon.Line, Lang.T("Line"), Lang.T("Add a line (Ctrl+L)"), delegate { AddLine(); }));
            palette.Items.Add(Tool(ToolIcon.Box, Lang.T("Box"), Lang.T("Add a box or ellipse (Ctrl+B)"), delegate { AddBox(); }));
            palette.Items.Add(Tool(ToolIcon.Picture, Lang.T("Picture"), Lang.T("Add a picture: logo, photo, symbol (Ctrl+I)"), delegate { AddPicture(); }));
            palette.Items.Add(Tool(ToolIcon.Barcode, Lang.T("Barcode"), Lang.T("Add a barcode: Code 128 or EAN-13 (Ctrl+K)"), delegate { AddBarcode(); }));
            palette.Items.Add(Tool(ToolIcon.QrCode, Lang.T("QR code"), Lang.T("Add a QR code: text, web address, contact, Wi-Fi... (Ctrl+Q)"), delegate { AddQrCode(); }));
            SizePalette();
            return palette;
        }

        ToolStrip palette;

        // Icons at the present DPI; as wide as the longest name (German at 150 % too), icon and name centred.
        void SizePalette()
        {
            int size = S(24), w = S(56), h = 0;
            palette.ImageScalingSize = new Size(size, size);
            palette.Padding = new Padding(S(4), S(6), S(4), S(6));
            foreach (ToolStripItem item in palette.Items)
            {
                var image = new Bitmap(size, size);
                using (var g = Graphics.FromImage(image)) Brand.DrawToolIcon(g, (ToolIcon)item.Tag, new RectangleF(0, 0, size, size), Theme.Of(Role.Accent));
                var old = item.Image;
                item.Image = image;
                if (old != null) old.Dispose();
                var text = TextRenderer.MeasureText(item.Text, item.Font);
                w = Math.Max(w, text.Width + S(16));
                h = Math.Max(h, size + text.Height + S(16));
            }
            foreach (ToolStripItem item in palette.Items)
            {
                item.Size = new Size(w, h);
                item.Margin = new Padding(0, S(2), 0, S(2));
            }
            palette.Width = w + palette.Padding.Horizontal + S(2);
            if (arrangeBar != null) SizeArrangeBar();
        }

        // ---------- the Arrange toolbar ----------
        // Always visible: the commands of the Arrange menu and the context menu for the selection. A button that can't
        // do anything now is greyed (the row stays, so nothing jumps).
        ToolStrip arrangeBar;

        ToolStrip BuildArrangeBar()
        {
            arrangeBar = new ToolStrip { Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden, RenderMode = ToolStripRenderMode.System };
            Func<object, Action, ToolStripButton> button = (icon, click) =>
                new ToolStripButton(null, null, delegate { click(); }) { Tag = icon, DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None };
            arrangeBar.Items.AddRange(new ToolStripItem[]
            {
                button(ArrangeIcon.Duplicate, Duplicate), button(ArrangeIcon.Delete, Delete), new ToolStripSeparator(),
                button(ArrangeIcon.RotateLeft, RotateLeft), button(ArrangeIcon.ResetRotation, ResetRotation), button(ArrangeIcon.RotateRight, Rotate), new ToolStripSeparator(),
                button(ArrangeIcon.AlignLeft, () => Align(0, -1)), button(ArrangeIcon.AlignCenter, () => Align(0, 0)), button(ArrangeIcon.AlignRight, () => Align(0, 1)),
                button(ArrangeIcon.AlignTop, () => Align(1, -1)), button(ArrangeIcon.AlignMiddle, () => Align(1, 0)), button(ArrangeIcon.AlignBottom, () => Align(1, 1)),
                new ToolStripSeparator(),
                button(ArrangeIcon.DistributeHorizontally, () => Distribute(0)), button(ArrangeIcon.DistributeVertically, () => Distribute(1)), new ToolStripSeparator(),
                button(Layer.ToFront, () => Order(Layer.ToFront)), button(Layer.Forward, () => Order(Layer.Forward)),
                button(Layer.Backward, () => Order(Layer.Backward)), button(Layer.ToBack, () => Order(Layer.ToBack)),
            });
            SizeArrangeBar();
            return arrangeBar;
        }

        // Icons at the present DPI.
        void SizeArrangeBar()
        {
            int n = S(16);
            arrangeBar.ImageScalingSize = new Size(n, n);
            arrangeBar.Padding = new Padding(S(4), S(2), S(4), S(2));
            foreach (ToolStripItem item in arrangeBar.Items)
            {
                if (item.Tag == null) continue;
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp))
                {
                    var r = new RectangleF(0, 0, n, n);
                    if (item.Tag is Layer) Brand.DrawLayerIcon(g, LayerIcons[(int)(Layer)item.Tag], r, Theme.Of(Role.Text));
                    else Brand.DrawArrangeIcon(g, (ArrangeIcon)item.Tag, r, Theme.Of(Role.Text));
                }
                var old = item.Image;
                item.Image = bmp;
                if (old != null) old.Dispose();
                item.Padding = new Padding(S(3));
            }
            // the panel's rotate buttons: the same icons at the same size
            foreach (var t in turnButtons)
            {
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp)) Brand.DrawArrangeIcon(g, t.Value, new RectangleF(0, 0, n, n), Theme.Of(Role.Text));
                var old = t.Key.Image;
                t.Key.Image = bmp;
                if (old != null) old.Dispose();
            }
        }

        // Enabled or not for the selection; the tooltips say name, key and (aligning) what it aligns with.
        void UpdateArrangeBar()
        {
            if (arrangeBar == null) return;
            var these = SelectedObjects;
            int n = these.Count;
            Func<string, string> plain = s => s.Replace("&", "");
            string with = n > 1 ? Lang.T("with the selection") : Lang.T("with the printable area");
            string[] layerNames = { Lang.T("Bring to &front"), Lang.T("Bring &forward"), Lang.T("Send &backward"), Lang.T("Send to bac&k") };
            string[] layerKeys = { Lang.T("Ctrl+Shift+]"), Lang.T("Ctrl+]"), Lang.T("Ctrl+["), Lang.T("Ctrl+Shift+[") };
            bool turnable = these.Any(o => !(o is LineObject));
            foreach (ToolStripItem item in arrangeBar.Items)
            {
                if (item.Tag is Layer)
                {
                    var how = (Layer)item.Tag;
                    item.Enabled = n > 0 && doc.CanRestack(these, how);
                    item.ToolTipText = plain(layerNames[(int)how]) + " (" + layerKeys[(int)how] + ")";
                    item.AccessibleName = item.ToolTipText;
                    continue;
                }
                if (!(item.Tag is ArrangeIcon)) continue;
                switch ((ArrangeIcon)item.Tag)
                {
                    case ArrangeIcon.Duplicate: item.Enabled = n > 0; item.ToolTipText = plain(Lang.T("D&uplicate")) + " (" + KeyText(Keys.Control | Keys.D) + ")"; break;
                    case ArrangeIcon.Delete: item.Enabled = n > 0; item.ToolTipText = plain(Lang.T("&Delete")) + " (" + Lang.T("Del") + ")"; break;
                    case ArrangeIcon.RotateLeft: item.Enabled = turnable; item.ToolTipText = Lang.T("Turn 90° anticlockwise"); break;
                    case ArrangeIcon.RotateRight: item.Enabled = turnable; item.ToolTipText = Lang.T("Turn 90° clockwise (Ctrl+R)"); break;
                    case ArrangeIcon.ResetRotation: item.Enabled = Turned; item.ToolTipText = Lang.T("Reset rotation (0°)"); break;
                    case ArrangeIcon.AlignLeft: Aligner(item, n, plain(Lang.T("Align &left")), with); break;
                    case ArrangeIcon.AlignCenter: Aligner(item, n, plain(Lang.T("Centre &horizontally")), with); break;
                    case ArrangeIcon.AlignRight: Aligner(item, n, plain(Lang.T("Align &right")), with); break;
                    case ArrangeIcon.AlignTop: Aligner(item, n, plain(Lang.T("Align &top")), with); break;
                    case ArrangeIcon.AlignMiddle: Aligner(item, n, plain(Lang.T("Centre &vertically")), with); break;
                    case ArrangeIcon.AlignBottom: Aligner(item, n, plain(Lang.T("Align &bottom")), with); break;
                    case ArrangeIcon.DistributeHorizontally:
                    case ArrangeIcon.DistributeVertically:
                        item.Enabled = n >= 3;
                        item.ToolTipText = plain((ArrangeIcon)item.Tag == ArrangeIcon.DistributeHorizontally ? Lang.T("Distribute hori&zontally") : Lang.T("Distribute verti&cally")) +
                                           (n >= 3 ? "" : " – " + Lang.T("Select three or more objects."));
                        break;
                }
                item.AccessibleName = item.ToolTipText;   // the buttons show only icons: screen readers read the tip
            }
        }

        static void Aligner(ToolStripItem item, int n, string name, string with)
        {
            item.Enabled = n > 0;
            item.ToolTipText = name + " (" + with + ")";
        }

        static ToolStripButton Tool(ToolIcon icon, string text, string tip, EventHandler click)
        {
            return new ToolStripButton(text, null, click)
            {
                Tag = icon, ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.ImageAndText, TextImageRelation = TextImageRelation.ImageAboveText,
                ImageScaling = ToolStripItemImageScaling.None, ImageAlign = ContentAlignment.MiddleCenter, TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false
            };
        }

        // One slim row: printer, label, copies and Print (petrol, at the right end). The boxes narrow in a small window.
        Control BuildCommandRow()
        {
            var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 8, RowCount = 1, Padding = new Padding(S(6), S(4), S(6), S(4)) };
            for (int i = 0; i < 8; i++) row.ColumnStyles.Add(i == 4 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> caption = t => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(S(6), 0, S(4), 0) };

            printerBox.Anchor = labelBox.Anchor = AnchorStyles.Left;
            row.Layout += delegate { FitBoxes(row); };
            printerBox.SelectedIndexChanged += delegate
            {
                if (loading || printerBox.SelectedItem == null) return;
                ChangePrinter(printerBox.SelectedItem.ToString());
                CheckRoll();
            };
            labelBox.SelectedIndexChanged += delegate
            {
                if (loading || labelBox.SelectedItem == null) return;
                keepFileFormat = false;
                SetLabel((LabelChoice)labelBox.SelectedItem);
                Touched();
            };
            var labelCaption = caption(Lang.T("Label:"));
            string labelTip = Lang.T("Your label formats first, then Brother's papers. New lengths: Tools > QL Label Formats.");
            tips.SetToolTip(labelCaption, labelTip);
            tips.SetToolTip(labelBox, labelTip);
            copies.Width = S(60);
            copies.Anchor = AnchorStyles.Left;
            tips.SetToolTip(copies, Lang.T("Number of labels"));
            printButton.Text = Lang.T("Print");
            printButton.Font = new Font(Font, FontStyle.Bold);
            printButton.Padding = new Padding(S(10), S(1), S(10), S(1));
            printButton.Anchor = AnchorStyles.Left;
            printButton.Margin = new Padding(S(8), 0, 0, 0);
            Brand.StylePrimary(printButton);
            printButton.Click += delegate { Print(); };

            printerCaption = caption(Lang.T("Printer:"));
            row.Controls.Add(printerCaption, 0, 0);
            row.Controls.Add(printerBox, 1, 0);
            row.Controls.Add(labelCaption, 2, 0);
            row.Controls.Add(labelBox, 3, 0);
            row.Controls.Add(caption(Lang.T("Copies:")), 5, 0);
            row.Controls.Add(copies, 6, 0);
            row.Controls.Add(printButton, 7, 0);
            commandRow = row;
            return row;
        }

        // The two boxes share what the row leaves over: as wide as their texts (printer up to 260, label up to 320),
        // narrower in a small window, never below 110.
        void FitBoxes(TableLayoutPanel row)
        {
            int rest = row.ClientSize.Width - row.Padding.Horizontal - S(12);
            foreach (Control c in row.Controls)
                if (c != printerBox && c != labelBox) rest -= c.PreferredSize.Width + c.Margin.Horizontal;
            rest -= printerBox.Margin.Horizontal + labelBox.Margin.Horizontal;
            // The longest printer name in the box's present font (its DropDownWidth can lag behind a DPI change).
            int want = printers.Count == 0 ? S(120) : printers.Max(n => TextRenderer.MeasureText(n, printerBox.Font).Width) + S(30);
            int pw = Math.Min(Math.Min(want, S(260)), Math.Max(S(110), rest * 2 / 5));
            if (printerBox.DropDownWidth < want) printerBox.DropDownWidth = want;
            int lw = Math.Min(S(320), Math.Max(S(110), rest - pw));
            if (printerBox.Width != pw) printerBox.Width = pw;
            if (labelBox.Width != lw) labelBox.Width = lw;
        }

        StatusStrip BuildStatusBar()
        {
            var status = new StatusStrip();
            rollStatus.IsLink = true;
            Theme.ItemLink(rollStatus, Role.Accent);
            rollStatus.Click += delegate { CheckRoll(); };
            fitButton.Text = Lang.T("Fit");
            fitButton.ToolTipText = Lang.T("Fit the label to the window (Ctrl+0)");
            fitButton.Click += delegate { SetZoom(0); };
            zoomOutButton.ToolTipText = Lang.T("Zoom out (Ctrl+Minus)");
            zoomOutButton.Click += delegate { ZoomStep(-1, null); };
            zoomInButton.ToolTipText = Lang.T("Zoom in (Ctrl+Plus, Ctrl+mouse wheel)");
            zoomInButton.Click += delegate { ZoomStep(1, null); };
            slider.Scrolled += delegate { ZoomTo(slider.Value, null); };
            sliderHost = new ToolStripControlHost(slider) { AutoSize = false, Size = new Size(S(120), S(20)), ToolTipText = Lang.T("Zoom (100 % = real size)") };
            zoomButton.ToolTipText = Lang.T("Zoom (100 % = real size)");
            zoomButton.DropDownItems.AddRange(ZoomItems(false).ToArray());
            zoomButton.DropDown.ShowItemToolTips = true;
            zoomButton.DropDownOpening += delegate { UpdateZoomItems(zoomButton.DropDownItems); };
            status.Items.AddRange(new ToolStripItem[] { labelStatus, rollStatus, fitButton, zoomOutButton, sliderHost, zoomInButton, zoomButton });
            // The mouse wheel over the status bar or the slider moves the slider (no Ctrl needed there).
            status.MouseWheel += OnStatusWheel;
            slider.MouseWheel += OnStatusWheel;
            SetZoom(0);
            statusBar = status;
            status.SizeChanged += delegate { FitLabelStatus(); };
            return status;
        }

        // ---------- zoom ----------
        // Steps of − / + / Ctrl+wheel (with the dot-for-dot zooms in between).
        static readonly double[] ZoomSteps = { 25, 33, 50, 67, 75, 80, 90, 100, 110, 125, 150, 175, 200, 250, 300, 400, 500, 600, 800 };

        // Dot for dot: 1 or 2 screen pixels per printer dot (300 dpi); the percentage depends on the screen's scaling.
        double SharpZoom(int pixels) { return pixels * 300.0 / DeviceDpi * 100; }
        static string Percent(double z) { return Math.Round(z).ToString(System.Globalization.CultureInfo.CurrentCulture) + " %"; }

        // The zoom shown now (also when fitted to the window).
        double CurrentZoom { get { return zoom > 0 || layout == null ? zoom : canvas.PixelsPerMm / canvas.RealSize * 100; } }

        // View menu and the percentage's menu: Fit, 100 %, the dot-for-dot zooms under a grey heading, in, out. The
        // shortcuts work through the View menu (shortcuts: true); the other menu shows them only.
        List<ToolStripItem> ZoomItems(bool shortcuts)
        {
            Func<string, Keys, string, object, Action, ToolStripMenuItem> item = (text, keys, shown, tag, click) =>
            {
                var i = new ToolStripMenuItem(text, null, delegate { click(); }) { Tag = tag };
                if (shortcuts) i.ShortcutKeys = keys;
                i.ShortcutKeyDisplayString = shown ?? KeyText(keys);
                return i;
            };
            var list = new List<ToolStripItem>
            {
                item(Lang.T("Fit"), Keys.Control | Keys.D0, null, 0.0, () => SetZoom(0)),
                item(Lang.T("100 % (real size)"), Keys.Control | Keys.D1, null, 100.0, () => SetZoom(100)),
                new ToolStripSeparator(),
                new ToolStripMenuItem(Lang.T("Dot for dot")) { Enabled = false },
                item("", Keys.Control | Keys.D2, null, 1, () => SetZoom(SharpZoom(1))),
                item("", Keys.Control | Keys.D4, null, 2, () => SetZoom(SharpZoom(2))),
                new ToolStripSeparator(),
                item(Lang.T("Zoom &in"), Keys.Control | Keys.Oemplus, Lang.T("Ctrl+Plus"), "in", () => ZoomStep(1, null)),
                item(Lang.T("Zoom &out"), Keys.Control | Keys.OemMinus, Lang.T("Ctrl+Minus"), "out", () => ZoomStep(-1, null)),
            };
            UpdateZoomItems(list);
            return list;
        }

        void UpdateZoomItems(System.Collections.IEnumerable items)
        {
            foreach (ToolStripItem i in items)
            {
                var m = i as ToolStripMenuItem;
                if (m == null) continue;
                if (m.Tag is int)
                {
                    int pixels = (int)m.Tag;
                    double z = SharpZoom(pixels);
                    m.Text = Percent(z);
                    m.ToolTipText = pixels == 1 ? Lang.T("1 screen pixel per printer dot") : Lang.T("2 screen pixels per printer dot");
                    m.Checked = zoom > 0 && Math.Abs(zoom - z) < 0.01;
                }
                else if (m.Tag is double) m.Checked = Math.Abs(zoom - (double)m.Tag) < 0.01;
            }
        }

        void SetZoom(double z) { ZoomTo(z, null); }

        // z: percent (0 = fit). at: the canvas point that stays where it is on the screen (null: the middle of the view).
        void ZoomTo(double z, Point? at)
        {
            if (z > 0) z = Math.Max(ZoomSlider.Min, Math.Min(ZoomSlider.Max, z));
            var spot = at ?? new Point(host.ClientSize.Width / 2 - canvas.Left, host.ClientSize.Height / 2 - canvas.Top);
            var inHost = new Point(spot.X + canvas.Left, spot.Y + canvas.Top);
            var mm = layout != null ? canvas.ClientToMm(spot) : PointF.Empty;
            zoom = z;
            ApplyZoom();
            if (layout != null && z > 0)
            {
                var p = canvas.MmToClient(mm);
                host.AutoScrollPosition = new Point((int)Math.Round(p.X) - inHost.X, (int)Math.Round(p.Y) - inHost.Y);
            }
        }

        void ZoomStep(int direction, Point? at)
        {
            double now = CurrentZoom;
            if (now <= 0) return;
            var steps = ZoomSteps.Concat(new[] { SharpZoom(1), SharpZoom(2) }).OrderBy(v => v).ToList();
            double next = direction > 0 ? steps.Where(v => v > now * 1.005).DefaultIfEmpty(ZoomSlider.Max).First()
                                        : steps.Where(v => v < now / 1.005).DefaultIfEmpty(ZoomSlider.Min).Last();
            ZoomTo(next, at);
        }

        // Ctrl+mouse wheel: in or out around the mouse pointer.
        void OnCanvasWheel(object sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0) return;
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;   // not also a scroll of the view
            var inHost = new Point(e.X + canvas.Left, e.Y + canvas.Top);
            wheel += e.Delta;
            while (Math.Abs(wheel) >= 120)
            {
                int direction = Math.Sign(wheel);
                wheel -= direction * 120;
                ZoomStep(direction, new Point(inHost.X - canvas.Left, inHost.Y - canvas.Top));
            }
        }

        void OnStatusWheel(object sender, MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            wheel += e.Delta;
            while (Math.Abs(wheel) >= 120)
            {
                int direction = Math.Sign(wheel);
                wheel -= direction * 120;
                ZoomStep(direction, null);
            }
        }

        // The status bar's zoom controls follow the zoom (also when fitted: the percentage it gives).
        void UpdateZoomUi()
        {
            if (sliderHost == null) return;
            var size = new Size(S(120), S(20));
            if (sliderHost.Size != size) sliderHost.Size = size;
            fitButton.Checked = zoom <= 0;
            slider.Stops = new[] { SharpZoom(1), SharpZoom(2) };
            double now = CurrentZoom;
            if (now > 0) slider.Value = now;
            zoomButton.Text = now > 0 ? Percent(now) : Lang.T("Fit");
        }

        void SetHighRes(bool on)
        {
            if (on == highRes) return;
            highRes = on;
            ShowLabelSettings();
            Relayout();
        }

        // The label settings in the panel and the menus follow the label (direction) and the printer (resolution).
        void ShowLabelSettings()
        {
            bool was = loading;
            loading = true;
            try
            {
                highResCheck.Checked = highResItem.Checked = highRes;
                viewBox.SelectedIndex = doc.Across ? 1 : 0;
                if (lengthNum == null) { }
                else if (label != null)
                {
                    var m = label.Media;
                    // a refused value stays in the field next to its message
                    if (lengthError.Text.Length == 0) lengthNum.Value = Math.Min(lengthNum.Maximum, (decimal)Math.Round(label.LengthMm, 1));
                    lengthNum.Enabled = !autoLength;
                    autoCheck.Checked = autoLength;
                    lengthFixed.Text = m.Kind == MediaKind.Round ? Lang.F("Ø {0} mm – round, fixed", Media.Mm(m.WidthMm))
                                                                  : Lang.F("{0} mm – die-cut, fixed", Media.Mm(m.LengthMm));
                    canvas.LengthText = m.Continuous ? Media.Mm(Math.Round(label.LengthMm, 1)) + " mm" + (autoLength ? " · " + Lang.T("automatic") : "") : null;
                    canvas.LengthGrip = m.Continuous && !autoLength;
                    canvas.LengthMin = MinLength;
                    canvas.LengthMax = MaxLength;
                }
                else canvas.LengthText = null;
            }
            finally { loading = was; }
            if (lengthNum != null) FillProps(null);
        }

        // The printer's range for a continuous label (the model's shortest and the GPD's longest; without a printer the paper's).
        double MinLength { get { return label == null ? 0 : Math.Max(label.Media.MinLength01, model != null ? model.MinLength01 : 0) / 10.0; } }
        double MaxLength { get { return label == null ? 0 : (model != null ? model.MaxLength01 : label.Media.MaxLength01) / 10.0; } }

        // A length of its own on the label's continuous tape: outside the printer's range it is refused with the range
        // (the driver would quietly print 150 mm), else the label box shows "own length" and the canvas the new length.
        // Typing or dragging a length ends "automatic".
        void OwnLength(double mm, bool auto)
        {
            if (label == null || !label.Media.Continuous) return;
            mm = Math.Round(mm, 1);
            if (!auto && autoLength) autoLength = false;
            if (mm < MinLength || mm > MaxLength)
            {
                lengthError.Text = Lang.F("Possible: {0}–{1} mm on this printer.", Media.Mm(MinLength), Media.Mm(MaxLength));
                ShowLabelSettings();
                UpdateCommands();
                return;
            }
            bool had = lengthError.Text.Length > 0;
            lengthError.Text = "";
            if (label.Custom && Math.Abs(label.LengthMm - mm) < 0.05) { ShowLabelSettings(); if (had) UpdateCommands(); return; }
            keepFileFormat = false;
            SelectLabel(LabelChoice.Own(label.Media, mm));
            Touched();
        }

        // "Automatic from the content": the end follows the last object, with the same margin as at the start (at least
        // the unprintable feed), within the printer's range. True when the length changed (the label is laid out again).
        bool FollowContent()
        {
            if (!autoLength || label == null || layout == null || !label.Media.Continuous || doc.Objects.Count == 0 || canvas.Dragging) return false;
            var b = EditorCanvas.BoxOf(doc.Objects);
            bool across = doc.Across;
            double start = across ? b.Top : b.Left, end = across ? b.Bottom : b.Right;
            double margin = Math.Max(across ? layout.Printable.Top : layout.Printable.Left, start);
            double mm = Math.Round(Math.Max(MinLength, Math.Min(MaxLength, end + margin)), 1);
            if (label.Custom && Math.Abs(label.LengthMm - mm) < 0.05) return false;
            OwnLength(mm, true);
            return true;
        }

        // The one status window of the user session (QL Printer Status); back here, OnActivated reads the roll again.
        void ShowStatus() { PrinterStatusProgram.ShowStatus(this); }

        // ---------- properties ----------
        void BuildProperties()
        {
            props.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nothingSelected.Text = Lang.T("Nothing selected. Click an object on the label to change it.");
            nothingSelected.MaximumSize = new Size(S(260), 0);
            nothingSelected.Margin = new Padding(0, 0, 0, S(8));

            // Several objects selected: how many, and what works on all of them.
            Func<LabelObject, bool> several = o => canvas.Selection.Count > 1;
            severalSelected.Font = new Font(Font, FontStyle.Bold);
            severalSelected.Margin = new Padding(0, S(4), 0, S(4));
            SpanRow(severalSelected, several);
            var severalHint = Hint(Lang.T("Drag or use the arrow keys to move them together. Del deletes them. Click an object to edit just that one."));
            SpanRow(severalHint, several);

            // Label settings, shown while nothing is selected.
            Func<LabelObject, bool> none = o => canvas.Selection.Count == 0;
            var heading = Theme.Fore(new Label { Text = Lang.T("Label"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) }, Role.Heading);
            SpanRow(heading, none);
            SpanRow(nothingSelected, none);
            viewBox = Combo(Lang.T("Along the label"), Lang.T("Across the tape"));
            viewBox.SelectedIndexChanged += delegate { if (!loading && label != null && (viewBox.SelectedIndex == 1) != doc.Across) TurnView(); };
            Row(Lang.T("Direction:"), viewBox, none);
            viewBox.Width = Math.Max(S(150), viewBox.Items.Cast<string>().Max(t => TextRenderer.MeasureText(t, Font).Width) + S(30));
            tips.SetToolTip(viewBox, Lang.T("Across the tape: the label is shown turned by 90°, for text across a long label."));
            highResCheck = Check("300 × 600 dpi", (s, e) => { if (!loading) SetHighRes(highResCheck.Checked); });
            Row(Lang.T("Resolution:"), highResCheck, none);
            tips.SetToolTip(highResCheck, Lang.T("Twice the dots along the label: sharper text, slower printing."));
            // Length: on continuous tape a field (a length of its own), on die-cut and round labels the fixed length.
            Func<LabelObject, bool> tape = o => none(o) && label != null && label.Media.Continuous,
                fixedSize = o => none(o) && label != null && !label.Media.Continuous;
            lengthNum = Number(0, 9999, 1, 1);
            lengthNum.Width = S(80);
            lengthNum.ValueChanged += delegate { if (!loading) OwnLength((double)lengthNum.Value, false); };
            var lengthInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(lengthInfo, () => Lang.F("A length of its own on continuous tape, {0}–{1} mm on this printer. It prints exactly this length; no label format is needed.",
                                                     Media.Mm(MinLength), Media.Mm(MaxLength)));
            var lengthRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            lengthRow.Controls.AddRange(new Control[] { lengthNum, new Label { Text = "mm", AutoSize = true, Margin = new Padding(S(4), S(6), S(6), 0) }, lengthInfo });
            Row(Lang.T("Length:"), lengthRow, tape);
            lengthError = Theme.Fore(new Label { AutoSize = true, MaximumSize = new Size(S(230), 0) }, Role.ErrorText);
            Row("", lengthError, o => tape(o) && lengthError.Text.Length > 0);
            autoCheck = Check(Lang.T("Automatic from the content"), delegate
            {
                if (loading) return;
                autoLength = autoCheck.Checked;
                if (!FollowContent()) ShowLabelSettings();
                Touched();
            });
            Row("", autoCheck, tape);
            var saveFormat = new LinkLabel { Text = Lang.T("Save as label format …"), AutoSize = true, Margin = new Padding(0, S(4), S(6), S(4)) };
            saveFormat.LinkClicked += delegate { OpenLabelFormats(label != null && label.Media.Continuous ? label : null); };
            var saveInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(saveInfo, () => Lang.T("Then the length is also offered in other programs. Opens QL Label Formats with a new format of this length (Save & apply as usual)."));
            var saveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, S(4)) };
            saveRow.Controls.AddRange(new Control[] { saveFormat, saveInfo });
            SpanRow(saveRow, tape);
            lengthFixed = new Label { AutoSize = true, Margin = new Padding(0, S(6), S(6), 0) };
            var fixedInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(fixedInfo, () => Lang.T("The labels on the roll set the length. For a length of your own, choose continuous tape."));
            var fixedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            fixedRow.Controls.AddRange(new Control[] { lengthFixed, fixedInfo });
            Row(Lang.T("Length:"), fixedRow, fixedSize);
            SpanRow(Hint(Lang.T("Arrow keys: 0.1 mm, with Shift 1 mm. Alt while dragging: no snapping.")), none);

            Func<LabelObject, bool> any = o => o != null, text = o => o is TextObject, line = o => o is LineObject,
                box = o => o is BoxObject, shape = o => o is LineObject || (o is BoxObject && !((BoxObject)o).Filled), image = o => o is ImageObject;
            Func<LabelObject, bool> oneLine = o => o is TextObject && ((TextObject)o).Kind == TextKind.Line,
                frame = o => o is TextObject && ((TextObject)o).Kind == TextKind.Frame;
            // Multi-line for a text frame; one line for a single-line text (Enter does nothing, pasted line breaks
            // become spaces).
            textBox = new TextBox { Multiline = true, AcceptsReturn = true, Height = S(64), ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            textBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && oneLine(canvas.Selected)) e.SuppressKeyPress = true; };
            textBox.TextChanged += (s, e) =>
            {
                if (!loading && oneLine(canvas.Selected) && textBox.Text.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                {
                    int at = textBox.SelectionStart;
                    textBox.Text = OneLine(textBox.Text);   // comes back here and is applied
                    textBox.SelectionStart = Math.Min(at, textBox.TextLength);
                    return;
                }
                Edit(s, o => ((TextObject)o).Text = textBox.Text);
            };
            Row(Lang.T("Text:"), textBox, text);
            Row("", FieldButton(textBox), text);
            textKindBox = Combo(Lang.T("Single line"), Lang.T("Text frame"));
            textKindBox.SelectedIndexChanged += (s, e) => Edit(s, o => SetTextKind((TextObject)o, (TextKind)textKindBox.SelectedIndex));
            Row(Lang.T("Kind:"), textKindBox, text);
            tips.SetToolTip(textKindBox, Lang.T("Single line: the box follows the text, the handles change its size.\n" +
                                                "Text frame: you set the box, the text wraps inside it."));
            SpanRow(Hint(Lang.T("Drag a handle to make the text larger or smaller.")), oneLine);
            SpanRow(Hint(Lang.T("The text wraps inside the box.")), frame);
            // While the text doesn't fit (the canvas draws the box's edge in red).
            var overflow = Hint(Lang.T("The text doesn't fit the box and is cut off: make the box larger or the text smaller."));
            Theme.Fore(overflow, Role.ErrorText);
            SpanRow(overflow, o => o is TextObject && EditorRender.Overflows((TextObject)o));
            fontBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, AutoCompleteMode = AutoCompleteMode.SuggestAppend, AutoCompleteSource = AutoCompleteSource.ListItems };
            foreach (var f in FontFamily.Families) fontBox.Items.Add(f.Name);
            fontBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((TextObject)o).Font = fontBox.Text);
            fontBox.Leave += (s, e) => { if (fontBox.Items.Contains(fontBox.Text)) Edit(s, o => ((TextObject)o).Font = fontBox.Text); };
            Row(Lang.T("Font:"), fontBox, text);
            sizeNum = Number(2, 400, 1, 1);
            sizeNum.ValueChanged += (s, e) => Edit(s, o => ((TextObject)o).Size = (double)sizeNum.Value);
            Row(Lang.T("Size (pt):"), sizeNum, text);
            var style = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            boldCheck = Check(Lang.T("Bold"), (s, e) => Edit(s, o => ((TextObject)o).Bold = boldCheck.Checked));
            italicCheck = Check(Lang.T("Italic"), (s, e) => Edit(s, o => ((TextObject)o).Italic = italicCheck.Checked));
            underlineCheck = Check(Lang.T("Underline"), (s, e) => Edit(s, o => ((TextObject)o).Underline = underlineCheck.Checked));
            style.Controls.AddRange(new Control[] { boldCheck, italicCheck, underlineCheck });
            Row("", style, text);
            // wraps only when its column is too narrow: the panel's scroll bar took the room "Underline" needed (German,
            // 96-144 dpi)
            props.Layout += (s, e) =>
            {
                var widths = props.GetColumnWidths();
                int room = props.Width - (widths.Length > 0 ? widths[0] : 0);
                int needed = style.Controls.Cast<Control>().Sum(c => c.PreferredSize.Width + c.Margin.Horizontal);
                bool wrap = room < needed;
                var most = wrap ? new Size(Math.Max(S(80), room), 0) : Size.Empty;
                if (style.WrapContents != wrap) style.WrapContents = wrap;
                if (style.MaximumSize != most) style.MaximumSize = most;
            };
            textInvertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((TextObject)o).Invert = textInvertCheck.Checked));
            tips.SetToolTip(textInvertCheck, Lang.T("White on black: a black box 1 mm larger than the text's frame, the text white in it. It covers what lies below."));
            Row("", textInvertCheck, text);
            alignBox = Combo(Lang.T("Left"), Lang.T("Centre"), Lang.T("Right"));
            alignBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((TextObject)o).Align = (TextAlign)alignBox.SelectedIndex);
            Row(Lang.T("Lines:"), alignBox, frame);
            lineHeightNum = Number((decimal)TextObject.MinLineHeight, (decimal)TextObject.MaxLineHeight, 0.1m, 1);
            lineHeightNum.ValueChanged += (s, e) => Edit(s, o => ((TextObject)o).LineHeight = (double)lineHeightNum.Value);
            Row(Lang.T("Line spacing (×):"), lineHeightNum, frame);
            spacingNum = Number((decimal)TextObject.MinSpacing, (decimal)TextObject.MaxSpacing, 0.5m, 1);
            spacingNum.ValueChanged += (s, e) => Edit(s, o => ((TextObject)o).Spacing = (double)spacingNum.Value);
            Row(Lang.T("Letter spacing (pt):"), spacingNum, text);

            // Barcodes (Code 128, GS1-128, EAN-13) and QR codes are separate tools; the type switches between the barcodes only.
            kindBox = Combo("Code 128", "GS1-128", "EAN-13");
            kindBox.SelectedIndexChanged += (s, e) => Edit(s, o => SetKind((BarcodeObject)o, LinearKinds[kindBox.SelectedIndex]));
            Row(Lang.T("Type:"), kindBox, o => o is BarcodeObject && ((BarcodeObject)o).Kind != Symbology.Qr);
            Func<LabelObject, bool> qr = o => o is BarcodeObject && ((BarcodeObject)o).Kind == Symbology.Qr,
                plain = o => o is BarcodeObject && !((BarcodeObject)o).TypedQr;
            qrTypeBox = Combo(Lang.T("Text"), Lang.T("Web address"), Lang.T("Contact"), Lang.T("Wi-Fi"), Lang.T("E-mail"), Lang.T("Phone number"), "SMS");
            qrTypeBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrType((QrType)qrTypeBox.SelectedIndex));
            Row(Lang.T("Contains:"), qrTypeBox, qr);
            tips.SetToolTip(qrTypeBox, Lang.T("What a phone does with the code: open a web page, save a contact, join a Wi-Fi network,\n" +
                                              "write an e-mail, call or send an SMS. Date fields such as {date} work in every field."));
            dataBox = new TextBox { Dock = DockStyle.Fill };
            dataBox.TextChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).Data = dataBox.Text);
            Row(Lang.T("Content:"), dataBox, plain);
            Row("", FieldButton(dataBox), plain);
            // How to write control characters (Code 128) or the elements (GS1-128): one line, the rest behind the (i)
            notationLabel = Theme.Fore(new Label { AutoSize = true, Margin = Padding.Empty }, Role.Dim);
            var notationIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(notationIcon, () =>
            {
                var b = canvas.Selected as BarcodeObject;
                return b != null && b.Kind == Symbology.Gs1_128
                    ? Lang.T("Each element starts with its application identifier (AI) in brackets, e.g. (01) GTIN, 14 digits; (17) best before, YYMMDD; " +
                             "(10) batch; (21) serial number. The brackets are printed under the bars but not encoded; the FNC1 separators are added automatically.")
                    : Lang.T("Typed like the date fields: {tab} tab, {cr} carriage return, {lf} line feed, {gs} group separator, {rs} record separator, " +
                             "{eot} end of transmission, {fnc1} FNC1, {xNN} any ASCII character by its hex code (00-7F). They are encoded but not printed under the bars.");
            });
            var notationRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            notationRow.Controls.AddRange(new Control[] { notationLabel, notationIcon });
            Row("", notationRow, o => { var b = o as BarcodeObject; return b != null && (b.Kind == Symbology.Code128 || b.Kind == Symbology.Gs1_128); });
            Action<string, string, Control> field = (key, caption, editor) =>
            {
                qrEditors[key] = editor;
                Row(caption, editor, o => { var b = o as BarcodeObject; return b != null && b.TypedQr && QrContent.FieldsOf(b.QrType).Contains(key); });
            };
            Func<string, bool, TextBox> qrBox = (key, multi) =>
            {
                var t = new TextBox { Dock = DockStyle.Fill, Multiline = multi, AcceptsReturn = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None };
                if (multi) t.Height = S(48);
                t.TextChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField(key, t.Text));
                return t;
            };
            field("url", Lang.T("Web address:"), qrBox("url", false));
            field("firstName", Lang.T("First name:"), qrBox("firstName", false));
            field("lastName", Lang.T("Last name:"), qrBox("lastName", false));
            field("company", Lang.T("Company:"), qrBox("company", false));
            field("phone", Lang.T("Phone:"), qrBox("phone", false));
            field("email", Lang.T("E-mail:"), qrBox("email", false));
            field("street", Lang.T("Street:"), qrBox("street", false));
            field("city", Lang.T("Postcode, city:"), qrBox("city", false));
            field("website", Lang.T("Website:"), qrBox("website", false));
            field("ssid", Lang.T("Network name:"), qrBox("ssid", false));
            field("password", Lang.T("Password:"), qrBox("password", false));
            var security = Combo("WPA/WPA2/WPA3", "WEP", Lang.T("None (open network)"));
            security.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField("security", QrContent.Security[security.SelectedIndex]));
            field("security", Lang.T("Encryption:"), security);
            CheckBox hidden = null;
            hidden = Check(Lang.T("Hidden network"), (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField("hidden", hidden.Checked ? "true" : "false")));
            field("hidden", "", hidden);
            field("to", Lang.T("To:"), qrBox("to", false));
            field("subject", Lang.T("Subject:"), qrBox("subject", false));
            field("body", Lang.T("Message:"), qrBox("body", true));
            field("number", Lang.T("Number:"), qrBox("number", false));
            field("message", Lang.T("Message:"), qrBox("message", true));
            problemLabel = new Label { AutoSize = true, MaximumSize = new Size(S(160), 0) };
            // Only while there is something to say (an empty row would leave a gap).
            Row("", problemLabel, o =>
            {
                var b = o as BarcodeObject;
                return b != null && (EditorRender.Problem(b, layout, DateTime.Now) != null || Fields.HasFields(b.Data) || (layout != null && EditorRender.BarcodeHint(b, layout, DateTime.Now) != null));
            });
            // The light margin around a selected code, explained; a warning when something reaches into it.
            quietLabel = new Label { AutoSize = true, MaximumSize = new Size(S(230), 0), Margin = new Padding(0, 0, 0, 0) };
            var quietIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(quietIcon, () => Lang.T("Scanners need a clear margin around the code to find where it starts and ends (the quiet zone): " +
                                                   "4 modules all round for a QR code, 10 modules left and right for Code 128, 11 left and 7 right for EAN-13. " +
                                                   "It isn't printed; the canvas shows it only while the code is selected."));
            quietRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(2), 0, S(6)) };
            quietRow.Controls.AddRange(new Control[] { quietLabel, quietIcon });
            SpanRow(quietRow, o => { var b = o as BarcodeObject; return b != null && b.Problem(DateTime.Now) == null; });
            showTextCheck = Check(Lang.T("Text under the bars"), (s, e) => Edit(s, o => ((BarcodeObject)o).ShowText = showTextCheck.Checked));
            Row("", showTextCheck, o => o is BarcodeObject && ((BarcodeObject)o).Kind != Symbology.Qr);
            levelBox = Combo(Lang.T("L – 7 % (smallest)"), Lang.T("M – 15 % (usual)"), Lang.T("Q – 25 %"), Lang.T("H – 30 % (most robust)"));
            levelBox.DropDownWidth = S(200);
            levelBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).Level = levelBox.SelectedIndex);
            Row(Lang.T("Error correction:"), levelBox, o => o is BarcodeObject && ((BarcodeObject)o).Kind == Symbology.Qr);
            // Inverted only for QR codes: the scanners of Code 128 and EAN-13 read dark on light only (no checkbox there).
            qrInvertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((BarcodeObject)o).Invert = qrInvertCheck.Checked));
            var qrInvertIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(qrInvertIcon, () => Lang.T("White on black: the standard allows it. Phone cameras usually read it, older or simple scanners often don't. Test it if in doubt."));
            var qrInvertRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            qrInvertRow.Controls.AddRange(new Control[] { qrInvertCheck, qrInvertIcon });
            Row("", qrInvertRow, qr);

            thickNum = Number(0.1m, 20, 0.1m, 1);
            thickNum.ValueChanged += (s, e) => Edit(s, o =>
            {
                var l = o as LineObject; if (l != null) l.Thickness = (double)thickNum.Value;
                var b = o as BoxObject; if (b != null) b.Thickness = (double)thickNum.Value;
            });
            Row(Lang.T("Line width (mm):"), thickNum, shape);
            filledCheck = Check(Lang.T("Filled"), (s, e) => Edit(s, o => ((BoxObject)o).Filled = filledCheck.Checked));
            ellipseCheck = Check(Lang.T("Ellipse"), (s, e) => Edit(s, o => ((BoxObject)o).Ellipse = ellipseCheck.Checked));
            var boxFlags = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            boxFlags.Controls.AddRange(new Control[] { filledCheck, ellipseCheck });
            Row("", boxFlags, box);

            // An SVG with parts Windows doesn't draw (text, effects, ...): one plain line while it is selected, details behind (i)
            svgLabel = Theme.Fore(new Label { AutoSize = true, MaximumSize = new Size(S(230), 0), Margin = new Padding(0) }, Role.WarningText);
            var svgIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(svgIcon, () => { var img = canvas.Selected as ImageObject; return SvgTip(img != null ? SvgPicture.Missing(img.Svg) : new string[0]); });
            var svgRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(2), 0, S(6)) };
            svgRow.Controls.AddRange(new Control[] { svgLabel, svgIcon });
            SpanRow(svgRow, o => o is ImageObject && SvgPicture.Missing(((ImageObject)o).Svg).Length > 0);
            modeBox = Combo(Lang.T("Dither (photos, shades)"), Lang.T("Threshold (logos, drawings)"));
            modeBox.DropDownWidth = S(240);
            modeBox.SelectedIndexChanged +=(s, e) => Edit(s, o => ((ImageObject)o).Mode = modeBox.SelectedIndex == 0 ? ImageMode.Dither : ImageMode.Threshold);
            Row(Lang.T("Black and white:"), modeBox, image);
            thresholdBar = new TrackBar { Minimum = 1, Maximum = 254, TickFrequency = 16, Dock = DockStyle.Fill, AutoSize = false, Height = S(32) };
            thresholdBar.ValueChanged += (s, e) => Edit(s, o => ((ImageObject)o).Threshold = thresholdBar.Value);
            Row(Lang.T("Threshold:"), thresholdBar, o => o is ImageObject && ((ImageObject)o).Mode == ImageMode.Threshold);
            invertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((ImageObject)o).Invert = invertCheck.Checked));
            tips.SetToolTip(invertCheck, Lang.T("White on black: black and white swapped in the picture."));
            Row("", invertCheck, image);
            var pictureButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
            var replace = new System.Windows.Forms.Button { Text = Lang.T("Replace..."), AutoSize = true };
            replace.Click += delegate { ReplacePicture(); };
            var natural = new System.Windows.Forms.Button { Text = Lang.T("Original proportions"), AutoSize = true };
            natural.Click += delegate { NaturalProportions(); };
            pictureButtons.Controls.AddRange(new Control[] { replace, natural });
            Row("", pictureButtons, image);

            numX = Number(-500, 3000, 0.1m, 1); numY = Number(-500, 3000, 0.1m, 1);
            numW = Number(0, 3000, 0.1m, 1); numH = Number(0, 3000, 0.1m, 1);
            numX.ValueChanged += (s, e) => Edit(s, o => o.X = (double)numX.Value);
            numY.ValueChanged += (s, e) => Edit(s, o => o.Y = (double)numY.Value);
            // a QR code stays square: width and height follow each other
            numW.ValueChanged += (s, e) => Edit(s, o => { o.W = (double)numW.Value; if (IsQr(o)) o.H = o.W; });
            numH.ValueChanged += (s, e) => Edit(s, o => { o.H = (double)numH.Value; if (IsQr(o)) o.W = o.H; });
            Row(Lang.T("Left (mm):"), numX, any);
            Row(Lang.T("Top (mm):"), numY, any);
            // A single-line text's box follows its text: no width and height to type. A line: its angle and length.
            Func<LabelObject, bool> sized = o => o != null && o.Resizable && !oneLine(o) && !(o is LineObject);
            Row(Lang.T("Width (mm):"), numW, sized);
            Row(Lang.T("Height (mm):"), numH, sized);
            lineAngleNum = Number(0, 180, 1, 1);
            lineLengthNum = Number(0, 3000, 0.1m, 1);
            lineAngleNum.ValueChanged += (s, e) => Edit(s, o => { var l = (LineObject)o; l.SetAngleLength((double)lineAngleNum.Value, l.Length); });
            lineLengthNum.ValueChanged += (s, e) => Edit(s, o => { var l = (LineObject)o; l.SetAngleLength(l.LineAngle, (double)lineLengthNum.Value); });
            tips.SetToolTip(lineAngleNum, Lang.T("0° level, 90° upright; the left end stays where it is."));
            tips.SetToolTip(lineLengthNum, Lang.T("The left end stays where it is."));
            Row(Lang.T("Angle (°):"), lineAngleNum, line);
            Row(Lang.T("Length (mm):"), lineLengthNum, line);
            // Text, boxes and pictures: any angle (also with the grip on the canvas); barcodes and QR codes in 90° steps.
            Func<LabelObject, bool> tilts = o => o != null && o.CanTilt && !(o is LineObject);
            Func<LabelObject, bool> quarters = o => o != null && !o.CanTilt && !(o is LineObject);
            angleNum = Number(-180, 180, 1, 1);
            angleNum.Width = S(70);
            angleNum.Margin = new Padding(0, S(3), 0, 0);
            angleNum.ValueChanged += (s, e) => Edit(s, o => o.SetAngle((double)angleNum.Value));
            Func<ArrangeIcon, string, int, Button> turn = (icon, tip, by) =>
            {
                int n = S(16);
                var pic = new Bitmap(n, n);
                using (var g = Graphics.FromImage(pic)) Brand.DrawArrangeIcon(g, icon, new RectangleF(0, 0, n, n), Theme.Of(Role.Text));
                var b = new Button { Image = pic, Size = new Size(S(30), S(26)), Margin = new Padding(S(6), S(1), 0, 0), UseVisualStyleBackColor = true };
                b.Click += (s, e) => Edit(s, o => o.SetAngle(by == 0 ? 0 : o.Angle + by));   // by 0: back to 0°
                turnButtons.Add(new KeyValuePair<Button, ArrangeIcon>(b, icon));
                tips.SetToolTip(b, tip);
                return b;
            };
            var angleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            angleRow.Controls.AddRange(new Control[]
            {
                angleNum, new Label { Text = "°", AutoSize = true, Margin = new Padding(S(2), S(6), 0, 0) },
                turn(ArrangeIcon.RotateLeft, Lang.T("Turn 90° anticlockwise"), -90), resetButton = turn(ArrangeIcon.ResetRotation, Lang.T("Reset rotation (0°)"), 0),
                turn(ArrangeIcon.RotateRight, Lang.T("Turn 90° clockwise (Ctrl+R)"), 90),
            });
            Row(Lang.T("Rotation:"), angleRow, tilts);
            SpanRow(Hint(Lang.T("Any angle (−180° to 180°), also with the round grip above the object. The buttons turn by 90° (like Ctrl+R).")), tilts);
            rotBox = Combo("0°", "90°", "180°", "270°");
            rotBox.SelectedIndexChanged += (s, e) => Edit(s, o => { while (o.Rotation != rotBox.SelectedIndex * 90) o.Rotate90(); });
            Row(Lang.T("Rotation:"), rotBox, quarters);
            SpanRow(Hint(Lang.T("Barcodes and QR codes: only 0°, 90°, 180°, 270° (scanners read them best straight).")), quarters);
        }

        void Row(string caption, Control editor, Func<LabelObject, bool> visible)
        {
            int r = props.RowCount++;
            var l = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(6), S(6), 0) };
            editor.Margin = new Padding(0, S(3), 0, S(3));
            if (!(editor is FlowLayoutPanel) && !(editor is CheckBox)) editor.Width = S(150);
            props.Controls.Add(l, 0, r);
            props.Controls.Add(editor, 1, r);
            rows.Add(new KeyValuePair<Control[], Func<LabelObject, bool>>(new Control[] { l, editor }, visible));
        }

        // The (i) of the SVG hint: what is missing, in plain words, the fonts replaced, and how to print it all.
        static string SvgTip(string[] missing)
        {
            var fonts = missing.Where(m => m.StartsWith("font:")).Select(m => m.Substring(5)).ToList();
            var words = missing.Where(m => !m.StartsWith("font:"))
                               .Select(m => m == "text" ? Lang.T("text") : m == "filter" ? Lang.T("effects such as shadows or blur")
                                          : m == "mask" ? Lang.T("fades and cut-outs") : Lang.T("some colours or lines")).ToList();
            var parts = new List<string>();
            if (words.Count > 0) parts.Add(Lang.F("Windows can't draw everything in this picture: {0}. The canvas shows what prints.", string.Join(", ", words)));
            if (fonts.Count > 0) parts.Add(string.Join("\n", fonts.Select(f => Lang.F("Font \"{0}\" is missing on this PC – a substitute font is used.", f))));
            parts.Add(Lang.T("To print it all: save the picture as a PNG in your drawing program and insert it here with \"Replace...\"."));
            if (fonts.Count > 0) parts.Add(Lang.T("Or install the font and insert the picture again with \"Replace...\"."));
            if (missing.Contains("text"))
                parts.Add(Lang.T("You can also convert the text to paths in your drawing program (Inkscape: Path › Object to Path); the picture then stays a drawing."));
            return string.Join("\n\n", parts);
        }

        void SpanRow(Control c, Func<LabelObject, bool> visible)
        {
            int r = props.RowCount++;
            props.Controls.Add(c, 0, r);
            props.SetColumnSpan(c, 2);
            rows.Add(new KeyValuePair<Control[], Func<LabelObject, bool>>(new[] { c }, visible));
        }

        Label Hint(string text)
        {
            return Theme.Fore(new Label { Text = text, AutoSize = true, MaximumSize = new Size(S(260), 0), Margin = new Padding(0, 0, 0, S(8)) }, Role.Dim);
        }

        NumericUpDown Number(decimal min, decimal max, decimal step, int decimals)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, TextAlign = HorizontalAlignment.Right };
        }

        static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            return c;
        }

        static CheckBox Check(string text, EventHandler changed)
        {
            var c = new CheckBox { Text = text, AutoSize = true };
            c.CheckedChanged += changed;
            return c;
        }

        static decimal Clamp(NumericUpDown n, double v) { return Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)Math.Round(v, n.DecimalPlaces))); }

        // Shows the selected object's values (skip: the control the user is typing in).
        void FillProps(object skip)
        {
            var o = canvas.Selected;
            loading = true;
            props.SuspendLayout();
            try
            {
                if (canvas.Selection.Count > 1) severalSelected.Text = Lang.F("{0} objects selected", canvas.Selection.Count);
                foreach (var row in rows)
                {
                    bool v = row.Value(o);
                    foreach (var c in row.Key) c.Visible = v;
                }
                if (o == null) return;
                if (skip != numX) numX.Value = Clamp(numX, o.X);
                if (skip != numY) numY.Value = Clamp(numY, o.Y);
                if (skip != numW) numW.Value = Clamp(numW, o.W);
                if (skip != numH) numH.Value = Clamp(numH, o.H);
                if (skip != rotBox) rotBox.SelectedIndex = o.Rotation / 90;
                if (skip != angleNum) angleNum.Value = Clamp(angleNum, o.Angle);
                resetButton.Enabled = o.Angle != 0;
                var t = o as TextObject;
                if (t != null)
                {
                    // a file may hold bare line feeds; the text box needs CR LF to break the line
                    string shown = t.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    if (skip != textBox && textBox.Text != shown) textBox.Text = shown;
                    if (skip != fontBox && fontBox.Text != t.Font) fontBox.Text = t.Font;
                    if (skip != sizeNum) sizeNum.Value = Clamp(sizeNum, t.Size);
                    textKindBox.SelectedIndex = (int)t.Kind;
                    if (skip != lineHeightNum) lineHeightNum.Value = Clamp(lineHeightNum, t.LineHeight);
                    if (skip != spacingNum) spacingNum.Value = Clamp(spacingNum, t.Spacing);
                    bool one = t.Kind == TextKind.Line;
                    int height = one ? TextRenderer.MeasureText("Ag", textBox.Font).Height + S(8) : S(64);
                    if (textBox.Height != height) textBox.Height = height;
                    textBox.ScrollBars = one ? ScrollBars.None : ScrollBars.Vertical;
                    textBox.WordWrap = !one;
                    boldCheck.Checked = t.Bold; italicCheck.Checked = t.Italic; underlineCheck.Checked = t.Underline;
                    textInvertCheck.Checked = t.Invert;
                    alignBox.SelectedIndex = (int)t.Align;
                }
                var l = o as LineObject;
                if (l != null)
                {
                    if (skip != thickNum) thickNum.Value = Clamp(thickNum, l.Thickness);
                    if (skip != lineAngleNum) lineAngleNum.Value = Clamp(lineAngleNum, l.LineAngle);
                    if (skip != lineLengthNum) lineLengthNum.Value = Clamp(lineLengthNum, l.Length);
                }
                var b = o as BoxObject;
                if (b != null) { if (skip != thickNum) thickNum.Value = Clamp(thickNum, b.Thickness); filledCheck.Checked = b.Filled; ellipseCheck.Checked = b.Ellipse; }
                var code = o as BarcodeObject;
                if (code != null)
                {
                    if (code.Kind != Symbology.Qr) kindBox.SelectedIndex = Array.IndexOf(LinearKinds, code.Kind);
                    notationLabel.Text = code.Kind == Symbology.Gs1_128 ? Lang.T("Elements: (AI)data, e.g. (01)…(17)…") : Lang.T("Control characters: {tab} {gs} {fnc1} …");
                    if (skip != dataBox && dataBox.Text != code.Data) dataBox.Text = code.Data;
                    qrTypeBox.SelectedIndex = (int)code.QrType;
                    foreach (var kv in qrEditors)
                    {
                        if (kv.Value == skip) continue;
                        string v;
                        if (!code.QrFields.TryGetValue(kv.Key, out v)) v = "";
                        var box = kv.Value as TextBox;
                        if (box != null && box.Text != v) box.Text = v;
                        var combo = kv.Value as ComboBox;
                        if (combo != null) combo.SelectedIndex = Math.Max(0, Array.IndexOf(QrContent.Security, v));
                        var check = kv.Value as CheckBox;
                        if (check != null) check.Checked = v == "true";
                    }
                    showTextCheck.Checked = code.ShowText;
                    levelBox.SelectedIndex = code.Level;
                    qrInvertCheck.Checked = code.Invert;
                    string problem = EditorRender.Problem(code, layout, DateTime.Now);
                    string hint = problem != null || layout == null ? null : EditorRender.BarcodeHint(code, layout, DateTime.Now);
                    Theme.Fore(problemLabel, problem != null ? Role.ErrorText : hint != null ? Role.WarningText : Role.Dim);
                    problemLabel.Text = problem ?? hint ?? (Fields.HasFields(code.Data) ? Lang.F("Prints now as: {0}", code.Caption(code.Content(DateTime.Now))) : "");
                    problemLabel.Visible = problemLabel.Text.Length > 0;
                    var hit = intruders.FirstOrDefault(p => p.Key == code);
                    Theme.Fore(quietLabel, hit.Key != null ? Role.WarningText : Role.Dim);
                    quietLabel.Text = hit.Key != null
                        ? Lang.F("{0} reaches into the quiet zone – scanners may not read the code well. Move the object away or make the code smaller.", NameOf(hit.Value))
                        : code.Invert && code.Kind == Symbology.Qr ? Lang.T("Quiet zone: printed black.")
                        : Lang.T("Light margin: quiet zone, stays clear, doesn't print.");
                }
                var img = o as ImageObject;
                if (img != null)
                {
                    modeBox.SelectedIndex = img.Mode == ImageMode.Dither ? 0 : 1;
                    if (skip != thresholdBar) thresholdBar.Value = img.Threshold;
                    invertCheck.Checked = img.Invert;
                    // only a font replaced: it all prints, in another font
                    svgLabel.Text = SvgPicture.Missing(img.Svg).All(m => m.StartsWith("font:"))
                        ? Lang.T("This picture prints with a substitute font.") : Lang.T("Parts of this picture can't be printed – tip: insert it as a PNG.");
                }
            }
            finally
            {
                props.ResumeLayout();
                // the side panel again: the docked table only takes its new height (rows shown or hidden, a hint that
                // wraps to another line) when its parent lays out, else its last rows were cut (German, 120/168 dpi)
                side.PerformLayout();
                loading = false;
            }
        }

        // While dragging: only the fields a drag changes (the whole panel again at the drop)
        void FillPlace()
        {
            var o = canvas.Selected;
            if (o == null) return;
            loading = true;
            try
            {
                numX.Value = Clamp(numX, o.X);
                numY.Value = Clamp(numY, o.Y);
                numW.Value = Clamp(numW, o.W);
                numH.Value = Clamp(numH, o.H);
                rotBox.SelectedIndex = o.Rotation / 90;
                angleNum.Value = Clamp(angleNum, o.Angle);
                resetButton.Enabled = o.Angle != 0;
                var t = o as TextObject;
                if (t != null) sizeNum.Value = Clamp(sizeNum, t.Size);
                var l = o as LineObject;
                if (l != null) { lineAngleNum.Value = Clamp(lineAngleNum, l.LineAngle); lineLengthNum.Value = Clamp(lineLengthNum, l.Length); }
            }
            finally { loading = false; }
        }

        // A property was changed in the panel: one undo step per control until another control or object is edited.
        void Edit(object sender, Action<LabelObject> change)
        {
            var o = canvas.Selected;
            if (loading || o == null) return;
            if (sender != lastEdited) { PushUndo(sender); }
            change(o);
            var t = o as TextObject;
            if (t != null) EditorRender.FitText(t);
            Touched();
            FillProps(sender);
        }

        // ---------- document ----------
        void PushUndo(object editor)
        {
            undo.Push(doc);
            lastEdited = editor;
            UpdateCommands();
        }

        // The document changed: render, mark unsaved.
        void Touched()
        {
            if (!dirty) { dirty = true; UpdateTitle(); }
            Render();
            UpdateCommands();
        }

        void Render()
        {
            if (layout == null) { CheckQuietZones(); ShowInfo(); return; }
            if (FollowContent()) return;   // laid out again at the new length (and rendered there)
            var shown = canvas.Rendered;
            if (canvas.Dragging && dragBounds.HasValue && shown != null && shown.Width == layout.DotsX && shown.Height == layout.DotsY)
            {
                // While dragging: only where the dragged objects were and are now (a full render costs most of a move)
                var now = SelectionBounds();
                var part = Union(dragBounds.Value, now);
                EditorRender.RenderPart(shown, doc, layout, part, DateTime.Now, canvas.Selection);
                dragBounds = now;
                canvas.RenderedPart(part);
            }
            else using (EditorRender.Later(PictureConverted)) canvas.Rendered = EditorRender.Render(doc, layout);
            canvas.OutsideChanged();
            // While dragging, the strip and the quiet-zone check wait for the drop (the strip would cover the canvas under
            // the mouse).
            if (!canvas.Dragging) { CheckQuietZones(); ShowInfo(); }
            if (zoom > 0) ApplyZoom();   // the area shown follows the objects (scrolling)
        }

        // A picture's exact black-and-white version is ready (converted after a drop on a worker thread): the label with it.
        void PictureConverted()
        {
            if (IsDisposed || canvas.Dragging || layout == null) return;
            Render();
        }

        void CheckQuietZones()
        {
            using (EditorRender.Later(PictureConverted))
                intruders = layout == null ? new List<KeyValuePair<BarcodeObject, LabelObject>>() : EditorRender.QuietZoneIntruders(doc, layout, DateTime.Now);
            canvas.Crowded = new HashSet<LabelObject>(intruders.Select(p => (LabelObject)p.Key));
            canvas.Invalidate();
        }

        // How the panel names an object reaching into a quiet zone.
        static string NameOf(LabelObject o)
        {
            var t = o as TextObject;
            if (t != null)
            {
                string s = t.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                return Lang.F("“{0}”", s.Length > 24 ? s.Substring(0, 23) + "…" : s);
            }
            if (o is LineObject) return Lang.T("A line");
            if (o is BoxObject) return Lang.T("A box");
            if (o is ImageObject) return Lang.T("A picture");
            var b = o as BarcodeObject;
            if (b != null) return b.Kind == Symbology.Qr ? Lang.T("A QR code") : Lang.T("A barcode");
            return Lang.T("Another object");
        }

        // The strip over the top of the canvas: objects with printed dots beyond the label, objects with printed dots in the
        // unprintable margin only (EditorRender.CutOff: an empty part of a box gives nothing), codes with something in
        // their quiet zone (one line each).
        void ShowInfo()
        {
            var now = DateTime.Now;
            var cuts = layout == null ? new List<EditorRender.Cut>() : doc.Objects.Select(o => EditorRender.CutOff(o, layout, now)).ToList();
            int count = cuts.Count(c => (c & EditorRender.Cut.Beyond) != 0), margin = cuts.Count(c => c == EditorRender.Cut.Margin);
            int crowded = intruders.Count;
            var lines = new List<string>();
            if (count > 0) lines.Add(count == 1 ? Lang.T("1 object reaches beyond the label – what lies outside doesn't print.")
                                                : Lang.F("{0} objects reach beyond the label – what lies outside doesn't print.", count));
            if (margin > 0) lines.Add(margin == 1 ? Lang.T("1 object reaches into the hatched margin – that part doesn't print.")
                                                  : Lang.F("{0} objects reach into the hatched margin – those parts don't print.", margin));
            if (crowded > 0) lines.Add(crowded == 1 ? Lang.T("1 code has objects in its quiet zone – scanners may not read it well.")
                                                    : Lang.F("{0} codes have objects in their quiet zone – scanners may not read them well.", crowded));
            string text = string.Join("\n", lines);
            if (text != outsideText) { outsideText = text; FitOutsideBar(); outsideBar.Invalidate(); }
            outsideBar.Visible = lines.Count > 0;
        }

        // The text wraps when the canvas is narrow (150 %, German).
        int OutsideTextLeft { get { return S(10) + Font.Height + S(8); } }
        void FitOutsideBar()
        {
            int width = Math.Max(1, outsideBar.Width - OutsideTextLeft - S(6));
            int text = TextRenderer.MeasureText(outsideText, Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int height = Math.Max(Font.Height, text) + S(12);
            if (outsideBar.Height != height) outsideBar.Height = height;
        }

        void PaintOutsideBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = outsideBar.ClientRectangle;
            using (var b = new SolidBrush(Theme.Of(Role.NoteBack))) g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Of(Role.NoteLine))) g.DrawLine(p, 0, r.Bottom - 1, r.Right, r.Bottom - 1);
            // a warning circle with "!" (a bar and a dot), as in the printer status
            int d = Font.Height, x = S(10), y = (r.Height - d) / 2;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Fixed.StateWarning)) g.FillEllipse(b, x, y, d, d);
            float cx = x + d / 2f, w = d * 0.13f;
            using (var white = new SolidBrush(Theme.Fixed.StateSymbol))
            {
                g.FillRectangle(white, cx - w / 2, y + d * 0.22f, w, d * 0.4f);
                g.FillEllipse(white, cx - w * 0.6f, y + d * 0.68f, w * 1.2f, w * 1.2f);
            }
            int tx = OutsideTextLeft;
            TextRenderer.DrawText(g, outsideText, Font, new Rectangle(tx, 0, Math.Max(1, r.Width - tx - S(6)), r.Height), Theme.Of(Role.NoteText),
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        void SetDoc(LabelDoc d, IEnumerable<LabelObject> select)
        {
            doc = d;
            canvas.Doc = d;
            canvas.Select(select ?? new LabelObject[0]);
            Render();
            FillProps(null);
            UpdateCommands();
        }

        void Undo(bool redo)
        {
            if (redo ? !undo.CanRedo : !undo.CanUndo) return;
            var indexes = canvas.Selection.Select(o => doc.Objects.IndexOf(o)).ToList();
            var d = redo ? undo.Redo(doc) : undo.Undo(doc);
            lastEdited = null;
            bool turned = d.Across != doc.Across;
            SetDoc(d, indexes.Where(i => i >= 0 && i < d.Objects.Count).Select(i => d.Objects[i]).ToList());
            if (turned) Relayout();
            if (!dirty) { dirty = true; UpdateTitle(); }
        }

        void UpdateCommands()
        {
            undoItem.Enabled = undo.CanUndo;
            redoItem.Enabled = undo.CanRedo;
            printButton.Enabled = printer != null && !printing;
            // While a print is checked and sent, printer and label stay as they were checked.
            printerBox.Enabled = printers.Count > 0 && !printing;
            labelBox.Enabled = !printing;
            tips.SetToolTip(printButton, printer == null ? MainForm.NoPrinterText : Lang.F("Print the label on {0} (Ctrl+P)", printer));
            UpdateArrangeBar();
        }

        void UpdateTitle()
        {
            string name = path != null ? Path.GetFileNameWithoutExtension(path) : Lang.T("New label");
            Text = name + (dirty ? " *" : "") + " – " + AppTitle;
        }

        // The texts are set when the window is built, so QL Label Maker starts again in the new language: with the label's
        // file (saved first when changed), else a new label on the same printer and label format. The setting is stored
        // only after the question (LanguageSwitch).
        async void SwitchLanguage(string language)
        {
            if (printing) return;
            bool restart;
            try
            {
                restart = await LanguageSwitch.Run(language, dirty, unsaved => AskLanguage(language, unsaved) == DialogResult.OK,
                                                   () => Task.FromResult(Save(false)), setting => Lang.Setting = setting);
            }
            catch (Exception ex) { Failed(this, Lang.T("The language couldn't be switched."), ex); return; }
            if (!restart) return;
            try { Process.Start(Application.ExecutablePath, RestartArguments(path, printer, label != null && !label.Custom ? label.Name : null)); }
            catch (Exception ex) { Failed(this, Lang.T("QL Label Maker couldn't start again in the new language."), ex); return; }
            dirty = false;   // saved, or nothing changed
            Close();
        }

        DialogResult AskLanguage(string setting, bool unsaved)
        {
            using (var d = LanguageSwitch.Question(AppTitle, setting, unsaved ? Lang.T("The label has unsaved changes: it is saved first.") : null))
                return d.ShowDialog(this);
        }

        // The file opens with its own label; without one, the printer and the label format (an own length has none).
        internal static string RestartArguments(string file, string printer, string formName)
        {
            if (file != null) return EditorProgram.Quote(file) + (printer != null ? " --printer " + EditorProgram.Quote(printer) : "");
            string args = printer != null ? "--printer " + EditorProgram.Quote(printer) : "";
            if (printer != null && formName != null) args += " --format " + EditorProgram.Quote(formName);
            return args;
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var answer = QlMessage.Show(this, Lang.T("Save the changes to this label?"), AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            return answer == DialogResult.No || Save(false);
        }

        void NewLabel()
        {
            if (printing || !ConfirmDiscard()) return;
            path = null;
            dirty = false;
            keepFileFormat = false;
            undo.Clear();
            SetDoc(new LabelDoc { Across = label != null && LabelLayout.AcrossByDefault(label) }, null);
            Relayout();
            UpdateTitle();
        }

        void OpenDialog()
        {
            if (printing || !ConfirmDiscard()) return;
            using (var dlg = new OpenFileDialog { Filter = Lang.T("QL labels") + " (*.qllabel)|*.qllabel|" + Lang.T("All files") + " (*.*)|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
        }

        void Open(string file)
        {
            LabelDoc d;
            try { d = LabelDoc.Load(file); }
            catch (Exception ex)
            {
                // A damaged or foreign file: the reader's message (about JSON, in Windows' language) doesn't help. A
                // missing file or one without access keeps Windows' message.
                bool damaged = ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException;
                string what = Lang.F("Cannot open {0}.", Path.GetFileName(file));
                if (damaged) QlMessage.Ask(this, AppTitle, what, Lang.T("The file is damaged or is not a QL label file.") + "\n\n" + file,
                                           MessageBoxButtons.OK, MessageBoxIcon.Error);
                else Failed(this, what, ex);
                return;
            }
            path = file;
            dirty = false;
            undo.Clear();
            var c = LabelChoice.Find(choices, d.FormName, d.PaperId, d.Length01);
            // A length of its own: on the same tape, if this printer has it.
            var tapeOf = d.LengthMode.Length > 0 ? MediaTable.ById(d.PaperId) : null;
            if (tapeOf != null && tapeOf.Continuous && d.Length01 > 0 && (model == null || model.HasPaper(tapeOf.Id))) c = LabelChoice.Own(tapeOf, d.Length01 / 10.0);
            autoLength = c != null && c.Custom && d.LengthMode == "auto";
            lengthError.Text = "";
            // Only a label of the same paper (another length) or none: shown there, saved as it was made.
            keepFileFormat = d.FormName.Length > 0 && !IsFileFormat(c, d);
            string note = null;
            if (c != null) SelectLabel(c);
            if (keepFileFormat)
                note = Lang.F("The label was made for '{0}', which {1} doesn't offer. It is shown on {2}.", d.FormName, printer ?? "?", label != null ? label.Name : "?");
            SetDoc(d, null);
            Relayout();
            UpdateTitle();
            if (d.Skipped > 0) note = (note != null ? note + "\n\n" : "") + Lang.F("{0} object(s) from a newer version of QL Label Maker were left out.", d.Skipped);
            if (note != null) QlMessage.Show(this, note, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        bool Save(bool saveAs)
        {
            if (label != null && !keepFileFormat)
            {
                doc.FormName = label.Custom ? "" : label.Name;   // an own length has no form
                doc.PaperId = label.Media.Id;
                doc.Length01 = (int)Math.Round(label.LengthMm * 10);
                doc.LengthMode = label.Custom ? (autoLength ? "auto" : "own") : "";
            }
            string target = path;
            if (saveAs || target == null)
            {
                using (var dlg = new SaveFileDialog { Filter = Lang.T("QL labels") + " (*.qllabel)|*.qllabel", DefaultExt = "qllabel", AddExtension = true })
                {
                    if (path != null) dlg.FileName = Path.GetFileName(path);
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    target = dlg.FileName;
                }
            }
            try { doc.Save(target); }
            catch (Exception ex)
            {
                Failed(this, Lang.F("Cannot save {0}.", Path.GetFileName(target)), ex);
                return false;
            }
            path = target;
            dirty = false;
            UpdateTitle();
            return true;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            // While a print is checked and sent: the window closes by itself when it is sent or a question stops it.
            if (printing) { closeWhenPrinted = true; e.Cancel = true; return; }
            if (!ConfirmDiscard()) e.Cancel = true;
        }

        bool closeWhenPrinted;

        // ---------- printer and label ----------
        // After Printer setup: printers added or removed; the label stays on its printer while that one is still there.
        void RefreshPrinters()
        {
            printers = MainForm.OurPrinters();
            string keep = printers.Contains(printer) ? printer : printers.FirstOrDefault();
            if (keep != printer) ChangePrinter(keep);
            FillPrinterBox();
            UpdateCommands();
            CheckRoll();
        }

        void FillPrinterBox()
        {
            loading = true;
            try
            {
                printerBox.Items.Clear();
                foreach (var p in printers) printerBox.Items.Add(p);
                printerBox.SelectedItem = printer;
                printerBox.Enabled = printers.Count > 0;
                tips.SetToolTip(printerBox, printers.Count == 0 ? MainForm.NoPrinterText : Lang.T("The printer the label is printed on (its model decides which labels it offers)"));
                int w = printers.Count == 0 ? S(120) : printers.Max(n => TextRenderer.MeasureText(n, printerBox.Font).Width) + S(30);
                printerBox.DropDownWidth = Math.Max(w, S(120));
            }
            finally { loading = false; }
        }

        void SetPrinter(string name, string formName)
        {
            bool other = name != printer || model == null;
            printer = name;
            model = MainForm.ModelOf(printer);
            choices = LabelChoice.All(model);
            if (other) { roll = null; rollStatus.Text = ""; }
            if (printer != null && other)   // the printer's own resolution setting; the label settings change it for this label
            {
                try { highRes = new PrinterSettings { PrinterName = printer }.DefaultPageSettings.PrinterResolution.Y == 600; }
                catch (Exception) { }
            }
            var c = formName != null ? LabelChoice.Find(choices, formName, 0, 0) : null;
            if (c == null && printer != null)
            {
                string def = null;
                try { def = Spooler.DefaultFormName(printer); } catch (Exception) { }
                if (def != null) c = LabelChoice.Find(choices, def, 0, 0);
            }
            loading = true;
            try
            {
                labelBox.Items.Clear();
                foreach (var ch in choices) labelBox.Items.Add(ch);
                int w = choices.Count == 0 ? S(160) : choices.Max(ch => TextRenderer.MeasureText(ch.ToString(), labelBox.Font).Width) + S(30);
                labelBox.DropDownWidth = Math.Max(w, S(160));
                ShowLabelSettings();
            }
            finally { loading = false; }
            SelectLabel(c ?? choices.FirstOrDefault());
            UpdateCommands();
        }

        // Another printer, or new label formats: the label stays; while the opened file's format is kept, that format
        // is chosen as soon as the printer offers it.
        void ChangePrinter(string name)
        {
            var was = label;
            SetPrinter(name, keepFileFormat ? doc.FormName : label != null ? label.Name : null);
            if (was != null && was.Custom && (model == null || model.HasPaper(was.Media.Id)))
                SelectLabel(LabelChoice.Own(was.Media, Math.Max(MinLength, Math.Min(MaxLength, was.LengthMm))));
            if (keepFileFormat && IsFileFormat(label, doc)) keepFileFormat = false;
        }

        // The label the file was made for: its format, or one with the same paper and length (a renamed format).
        static bool IsFileFormat(LabelChoice c, LabelDoc d)
        {
            return c != null && (string.Equals(c.Name, d.FormName, StringComparison.OrdinalIgnoreCase) ||
                                 c.Media.Id == d.PaperId && Math.Abs(c.LengthMm * 10 - d.Length01) < 1);
        }

        void SelectLabel(LabelChoice c)
        {
            loading = true;
            try
            {
                if (c != null && c.Custom && !labelBox.Items.Contains(c))
                {
                    foreach (var old in labelBox.Items.Cast<LabelChoice>().Where(x => x.Custom).ToList()) labelBox.Items.Remove(old);
                    labelBox.Items.Insert(0, c);
                }
                labelBox.SelectedItem = c;
            }
            finally { loading = false; }
            SetLabel(c);
        }

        void SetLabel(LabelChoice c)
        {
            label = c;
            // An empty, unsaved label takes the usual direction for this paper.
            if (c != null && !c.Custom && doc.Objects.Count == 0 && path == null) doc.Across = LabelLayout.AcrossByDefault(c);
            if (c != null && !c.Custom) { autoLength = false; if (lengthError != null) lengthError.Text = ""; }
            Relayout();
        }

        // Along the label <-> across the tape. The objects keep their places on the screen, so the design turns on the label.
        void TurnView()
        {
            if (label == null) { ShowLabelSettings(); return; }
            PushUndo(null);
            doc.Across = !doc.Across;
            lastEdited = null;
            Relayout();
            Touched();
        }

        void Relayout()
        {
            if (label == null) { layout = null; canvas.Geometry = null; canvas.Invalidate(); return; }
            layout = LabelLayout.For(printer, label, highRes, doc.Across);
            canvas.Geometry = layout;
            ShowLabelSettings();
            ApplyZoom();
            Render();
            var p = layout.Printable;
            // Most important first: a narrow window leaves out the last parts (the whole text is in the tooltip).
            labelParts = new[] { label.Name, Media.Mm(Math.Round(layout.ViewWidthMm, 1)) + " × " + Media.Mm(Math.Round(layout.ViewHeightMm, 1)) + " mm",
                                 highRes ? "300 × 600 dpi" : "300 dpi",
                                 Lang.F("printable {0} × {1} mm", Media.Mm(Math.Round(p.Width, 1)), Media.Mm(Math.Round(p.Height, 1))) }
                         .Concat(layout.Approximate ? new[] { Lang.T("approximate: this label is not on the printer (yet)") } : new string[0]).ToArray();
            labelStatus.Text = string.Join("  ·  ", labelParts);
            labelStatus.ToolTipText = labelStatus.Text + "\n" + (layout.Approximate
                ? Lang.T("The printer doesn't offer this label, so the printable area is estimated. A new or changed label format needs Save & apply in QL Label Formats.")
                : Lang.T("Hatched: the printer can't print there. 'Printable' is what the driver reports for this label."));
            ShowRoll();
        }

        // The label line in the status bar: as many whole parts as fit next to the roll and zoom (none cut in the middle).
        string[] labelParts = new string[0];
        void FitLabelStatus()
        {
            if (statusBar == null || labelParts.Length == 0 || !labelStatus.Text.StartsWith(labelParts[0])) return;   // not while it shows a message
            statusBar.PerformLayout();   // the roll's new width
            int others = statusBar.Items.Cast<ToolStripItem>().Where(i => i != labelStatus && i.Visible).Sum(i => i.Width + i.Margin.Horizontal);
            int room = statusBar.DisplayRectangle.Width - others - labelStatus.Margin.Horizontal - labelStatus.Padding.Horizontal - S(8);
            string text = labelParts[0];
            for (int i = 1; i < labelParts.Length; i++)
            {
                string more = text + "  ·  " + labelParts[i];
                if (TextRenderer.MeasureText(more, statusBar.Font).Width > room) break;
                text = more;
            }
            if (labelStatus.Text != text) labelStatus.Text = text;
        }

        void ApplyZoom()
        {
            if (layout == null) { UpdateZoomUi(); return; }
            if (zoom <= 0)
            {
                canvas.Zoom = 0;
                canvas.Dock = DockStyle.Fill;
                UpdateZoomUi();
                return;
            }
            double k = canvas.RealSize * zoom / 100;
            canvas.Dock = DockStyle.None;
            canvas.Zoom = k;
            var need = canvas.Needed(k);
            canvas.Size = new Size(Math.Max(need.Width, host.ClientSize.Width), Math.Max(need.Height, host.ClientSize.Height));
            UpdateZoomUi();
        }

        void OpenLabelFormats() { OpenLabelFormats(null); }

        // newFormat: a continuous label whose length becomes a new format there ("Save as label format ...").
        void OpenLabelFormats(LabelChoice newFormat)
        {
            // QlLabelFormats.exe next to QlLabelMaker.exe.
            string exe = PrinterStatusProgram.LabelTool;
            string args = printer != null ? "--printer " + EditorProgram.Quote(printer) : "";
            if (printer != null && newFormat != null)
                args += " --new-format " + newFormat.Media.Id + " " + (int)Math.Round(newFormat.LengthMm * 10);
            try { Process.Start(exe, args); }
            catch (Exception ex) { Failed(this, Lang.T("QL Label Formats couldn't be started."), ex); }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Back from the status window (or a roll change meanwhile): the loaded roll again.
            if (!printing && DateTime.Now - lastRollCheck > TimeSpan.FromSeconds(3)) CheckRoll();
            // Back from the label formats window: new or changed formats.
            if (printer == null || label == null || printing) return;
            var fresh = LabelChoice.All(model);
            if (fresh.Select(c => c.ToString()).SequenceEqual(choices.Select(c => c.ToString())) && !layout.Approximate) return;
            ChangePrinter(printer);
        }

        // ---------- loaded roll ----------
        async void CheckRoll()
        {
            string p = printer;
            lastRollCheck = DateTime.Now;
            if (p == null || !QlDevice.HasStatus(QlDevice.PortOf(p))) { roll = null; ShowRoll(); return; }
            PrinterCheck c = null;
            try { c = await Task.Run(() => PrinterCheck.Run(p, true)); }
            catch (Exception) { }
            if (IsDisposed || p != printer) return;
            roll = c;
            // A new, empty label: start on the loaded roll.
            if (c != null && c.Media != null && doc.Objects.Count == 0 && path == null && label != null && c.Media.Fits(label.Media) == false)
            {
                var fit = choices.FirstOrDefault(ch => c.Media.Fits(ch.Media) == true);
                if (fit != null) SelectLabel(fit);
            }
            ShowRoll();
        }

        void ShowRoll()
        {
            ShowRollText();
            FitLabelStatus();   // the roll's text takes room from the label line
        }

        // Light/dark switched (Theme): the icons drawn once, the roll line and the bar over the canvas.
        void ThemeChanged(object sender, EventArgs e)
        {
            SizePalette();
            SizeArrangeBar();
            ShowRollText();
            outsideBar.Invalidate();
        }

        void ShowRollText()
        {
            var c = roll;
            if (c == null)
            {
                rollStatus.Text = printer != null && QlDevice.HasStatus(QlDevice.PortOf(printer)) ? Lang.T("Loaded roll: click to check") : "";
                Theme.ItemFore(rollStatus, Role.Text);
                return;
            }
            bool? fits = c.Media != null && label != null ? c.Media.Fits(label.Media) : null;
            rollStatus.Text = c.Short + (fits == false ? " – " + Lang.T("doesn't fit this label") : "");
            Theme.ItemFore(rollStatus, c.Health == Health.Error || fits == false ? Role.ErrorText : c.Health == Health.Warning ? Role.WarningText : Role.Text);
            rollStatus.ToolTipText = (c.Detail ?? c.Headline) + (c.Advice.Count > 0 ? "\n" + string.Join("\n", c.Advice) : "") + "\n" + Lang.T("Click to check again.");
        }

        // ---------- objects ----------
        PointF FreeSpot()
        {
            var p = layout != null ? layout.Printable : new RectangleF(3, 3, 50, 20);
            float step = 2f * (doc.Objects.Count % 5);
            return new PointF(p.X + 1 + step, p.Y + 1 + step);
        }

        void Add(LabelObject o)
        {
            PushUndo(null);
            doc.Objects.Add(o);
            canvas.Selected = o;
            lastEdited = null;
            Touched();
            FillProps(null);
        }

        void AddText()
        {
            var at = FreeSpot();
            var t = new TextObject { X = at.X, Y = at.Y, Text = Lang.T("Text"), Size = 14 };
            EditorRender.FitText(t);
            Add(t);
            FocusField(textBox);
        }

        void AddTextFrame()
        {
            var at = FreeSpot();
            double w = layout != null ? Math.Max(10, Math.Min(40, layout.Printable.Width - 2)) : 40;
            Add(new TextObject { X = at.X, Y = at.Y, W = w, H = 12, Text = Lang.T("Text"), Size = 12, Kind = TextKind.Frame });
            FocusField(textBox);
        }

        static bool IsQr(LabelObject o) { var b = o as BarcodeObject; return b != null && b.Kind == Symbology.Qr; }

        static string OneLine(string s) { return s.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' '); }

        // Single line: line breaks become spaces, the box follows the text (Edit). Text frame: starts with the box
        // around the text, then keeps the box it is given.
        static void SetTextKind(TextObject t, TextKind kind)
        {
            if (t.Kind == kind) return;
            t.Kind = kind;
            if (kind == TextKind.Line) t.Text = OneLine(t.Text);
            else EditorRender.FitBox(t);
        }

        void AddLine()
        {
            var at = FreeSpot();
            double len = layout != null ? Math.Min(30, layout.Printable.Width - 2) : 30;
            Add(new LineObject { X = at.X, Y = at.Y + 2, W = Math.Max(5, len), H = 0 });
        }

        void AddBarcode()
        {
            var at = FreeSpot();
            Add(new BarcodeObject { X = at.X, Y = at.Y, W = 32, H = 12 });   // with its quiet zone about 40 mm
            FocusField(dataBox);
        }

        // A QR code starts as a web address (the panel's "Contains" offers the other kinds).
        void AddQrCode()
        {
            var at = FreeSpot();
            // the code itself; its quiet zone (4 modules on each side) comes around it
            double side = layout != null ? Math.Max(8, Math.Min(16, (Math.Min(layout.Printable.Width, layout.Printable.Height) - 2) * 0.75)) : 16;
            var q = new BarcodeObject { X = at.X, Y = at.Y, W = side, H = side, Kind = Symbology.Qr };
            q.SetQrType(QrType.Url);
            q.SetQrField("url", "example.org");
            Add(q);
            FocusField((TextBox)qrEditors["url"]);
        }

        // A new object's content field, ready to type over. Only while this window is active: focusing a control of an
        // inactive window can bring it to the front (an inserted object from automation, x64 check 2026-11-01).
        void FocusField(TextBox field)
        {
            if (ActiveForm != this) return;
            field.Focus();
            field.SelectAll();
        }

        // The type list's order
        static readonly Symbology[] LinearKinds = { Symbology.Code128, Symbology.Gs1_128, Symbology.Ean13 };

        // Code 128 <-> GS1-128 <-> EAN-13: an EAN-13 needs digits and a GS1-128 elements, so a content that doesn't fit
        // becomes an example.
        static void SetKind(BarcodeObject b, Symbology kind)
        {
            if (b.Kind == kind) return;
            b.Kind = kind;
            string error;
            if (kind == Symbology.Ean13 && Ean13.Check(b.Data) != null) b.Data = "0000000000000";   // neutral (a real number would be someone's product)
            if (kind == Symbology.Gs1_128 && Code128.Gs1(b.Data, out error) == null) b.Data = "(01)00000000000000(10)ABC123";
        }

        // "Insert field" for a text or barcode content box: today's date, a date in N days, the time.
        System.Windows.Forms.Button FieldButton(TextBox target)
        {
            var button = new System.Windows.Forms.Button { Text = Lang.T("Insert field ▾"), AutoSize = true };
            var menu = new ContextMenuStrip();
            Action<string, string> add = (caption, token) => menu.Items.Add(caption + "   " + token, null, delegate
            {
                target.Focus();
                target.SelectedText = token;   // TextChanged applies it
            });
            add(Lang.T("Today's date"), "{date}");
            add(Lang.T("Date in 30 days"), "{date+30}");
            add(Lang.T("Date as 2026-10-29"), "{date:yyyy-MM-dd}");
            add(Lang.T("Time"), "{time}");
            menu.Items.Add(new ToolStripSeparator());
            // Two short grey lines (one long line made the menu very wide).
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Filled in when shown and printed.")) { Enabled = false });
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Also: {date+7}, {date-1}, {date:dd.MM.yy}")) { Enabled = false });
            button.Click += delegate { menu.Show(button, 0, button.Height); };
            return button;
        }

        void AddBox()
        {
            var at = FreeSpot();
            Add(new BoxObject { X = at.X, Y = at.Y, W = 15, H = 10 });
        }

        string AskPicture()
        {
            using (var dlg = new OpenFileDialog { Filter = Lang.T("Pictures") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff;*.webp;*.heic;*.heif;*.hif;*.pdf;*.svg|" + Lang.T("All files") + " (*.*)|*.*" })
                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
        }

        // A failure: what failed as the headline, Windows' own text behind Show details.
        static void Failed(IWin32Window owner, string what, Exception ex)
        {
            QlMessage.Ask(owner, AppTitle, what, null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message);
        }

        static ImageObject LoadPicture(IWin32Window owner, string file)
        {
            try { return ImageObject.FromFile(file); }
            catch (Exception ex)
            {
                Failed(owner, Lang.F("Cannot read the picture {0}.", Path.GetFileName(file)), ex);
                return null;
            }
        }

        // Size for a picture of w x h pixels: as large as fits into 80 % of the printable area.
        SizeF PictureSize(double w, double h)
        {
            var p = layout != null ? layout.Printable : new RectangleF(0, 0, 40, 20);
            double k = Math.Min(p.Width * 0.8 / w, p.Height * 0.8 / h);
            return new SizeF((float)(w * k), (float)(h * k));
        }

        void AddPicture()
        {
            string file = AskPicture();
            if (file == null) return;
            var img = LoadPicture(this, file);
            if (img == null) return;
            var at = FreeSpot();
            var size = PictureSize(img.W, img.H);
            img.X = at.X; img.Y = at.Y; img.W = Math.Round(size.Width, 1); img.H = Math.Round(size.Height, 1);
            Add(img);
        }

        void ReplacePicture()
        {
            var old = canvas.Selected as ImageObject;
            if (old == null) return;
            string file = AskPicture();
            if (file == null) return;
            var img = LoadPicture(this, file);
            if (img == null) return;
            Edit(this, o => { var i = (ImageObject)o; i.Png = img.Png; i.Svg = img.Svg; });
            NaturalProportions();
        }

        void NaturalProportions()
        {
            var img = canvas.Selected as ImageObject;
            if (img == null || img.Picture == null) return;
            bool turned = img.Rotation == 90 || img.Rotation == 270;
            double ratio = turned ? (double)img.Picture.Height / img.Picture.Width : (double)img.Picture.Width / img.Picture.Height;
            Edit(this, o => o.H = Math.Round(o.W / ratio, 2));
        }

        // The selected objects in their stacking order (copies and commands keep it).
        List<LabelObject> SelectedObjects { get { return doc.Objects.Where(canvas.Selection.Contains).ToList(); } }

        // Where the selected objects are (mm), for the part drawn again while dragging; dragBounds: where they were.
        RectangleF? dragBounds;
        RectangleF SelectionBounds() { return canvas.Selection.Count == 0 ? RectangleF.Empty : EditorCanvas.BoxOf(canvas.Selection); }
        // Both places; not RectangleF.Union with IsEmpty: a level or upright line's box has no height or width, and
        // "empty" there dropped where it was (trails while moving it)
        static RectangleF Union(RectangleF a, RectangleF b)
        {
            return RectangleF.FromLTRB(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
        }

        // After a change to several objects at once: one undo step (taken before), render, panel.
        void ChangedAll()
        {
            lastEdited = null;
            Touched();
            FillProps(null);
        }

        void Duplicate()
        {
            var these = SelectedObjects;
            if (these.Count == 0) return;
            PushUndo(null);
            var copies = these.Select(o => { var c = o.Clone(); c.X += 2; c.Y += 2; return c; }).ToList();
            doc.Objects.AddRange(copies);
            canvas.Select(copies);
            ChangedAll();
        }

        void Delete()
        {
            var these = SelectedObjects;
            if (these.Count == 0) return;
            PushUndo(null);
            doc.Objects.RemoveAll(these.Contains);
            canvas.Selected = null;
            ChangedAll();
        }

        // Ctrl+A: all objects; in a text field its text.
        void SelectAll()
        {
            var field = ActiveControl as TextBoxBase;
            if (field == null) { var up = ActiveControl as UpDownBase; if (up != null) field = up.Controls.OfType<TextBoxBase>().FirstOrDefault(); }
            if (field != null) { field.SelectAll(); return; }
            canvas.Select(doc.Objects);
            canvas.Focus();
        }

        void Rotate() { Rotate(1); }

        // "Reset rotation": the selected objects back to 0° (quarter turns and free angle; lines have no rotation).
        bool Turned { get { return SelectedObjects.Any(o => !(o is LineObject) && o.Angle != 0); } }

        void ResetRotation()
        {
            var these = SelectedObjects.Where(o => !(o is LineObject) && o.Angle != 0).ToList();
            if (these.Count == 0) return;
            PushUndo(null);
            foreach (var o in these)
            {
                o.SetAngle(0);
                var t = o as TextObject;
                if (t != null) EditorRender.FitText(t);
            }
            ChangedAll();
        }
        void RotateLeft() { Rotate(3); }

        // quarters: clockwise quarter turns (3 = one to the left).
        void Rotate(int quarters)
        {
            var these = SelectedObjects.Where(o => !(o is LineObject)).ToList();
            if (these.Count == 0) return;
            PushUndo(null);
            foreach (var o in these)
            {
                for (int i = 0; i < quarters; i++) o.Rotate90();
                var t = o as TextObject;
                if (t != null) EditorRender.FitText(t);
            }
            ChangedAll();
        }

        // axis 0 = along the label (left/right), 1 = across (top/bottom); where -1 / 0 / 1 = start / centre / end. One
        // object: of the printable area; several: of their common box (left = the leftmost object's left edge, ...).
        void Align(int axis, int where)
        {
            var these = SelectedObjects;
            if (these.Count == 0 || layout == null) return;
            var p = these.Count > 1 ? EditorCanvas.BoxOf(these) : layout.Printable;
            PushUndo(null);
            foreach (var x in these)
            {
                var b = x.Bounds;   // a turned object: the box around it
                if (axis == 0) x.X = Math.Round(x.X + (where < 0 ? p.Left : where > 0 ? p.Right - b.Width : p.Left + (p.Width - b.Width) / 2) - b.Left, 2);
                else x.Y = Math.Round(x.Y + (where < 0 ? p.Top : where > 0 ? p.Bottom - b.Height : p.Top + (p.Height - b.Height) / 2) - b.Top, 2);
            }
            ChangedAll();
        }

        // Three or more objects: equal gaps between them along the axis, the first and the last stay where they are.
        // When they don't fit side by side (they overlap), their centres get equal distances instead.
        void Distribute(int axis)
        {
            // By the box around each object (turned ones included).
            Func<LabelObject, double> start = o => axis == 0 ? o.Bounds.X : o.Bounds.Y, size = o => axis == 0 ? o.Bounds.Width : o.Bounds.Height;
            Func<LabelObject, double> centre = o => start(o) + size(o) / 2;
            Action<LabelObject, double> place = (o, v) => { if (axis == 0) o.X = Math.Round(o.X + v - start(o), 2); else o.Y = Math.Round(o.Y + v - start(o), 2); };
            var these = SelectedObjects.OrderBy(start).ToList();
            if (these.Count < 3) return;
            double from = start(these[0]), to = these.Max(o => start(o) + size(o));
            double gap = (to - from - these.Sum(size)) / (these.Count - 1);
            PushUndo(null);
            if (gap >= 0)
            {
                double at = from;
                foreach (var o in these) { place(o, at); at += size(o) + gap; }
            }
            else
            {
                these = these.OrderBy(centre).ToList();
                double first = centre(these[0]), step = (centre(these[these.Count - 1]) - first) / (these.Count - 1);
                for (int i = 0; i < these.Count; i++) place(these[i], first + i * step - size(these[i]) / 2);
            }
            ChangedAll();
        }

        // The align commands (Arrange menu, Align in the context menu). A grey first line says what they align with.
        List<ToolStripItem> AlignItems()
        {
            Func<string, Action, string, ToolStripMenuItem> item = (text, click, tag) => new ToolStripMenuItem(text, null, delegate { click(); }) { Tag = tag };
            return new List<ToolStripItem>
            {
                new ToolStripMenuItem(Lang.T("Aligns with the printable area")) { Enabled = false, Tag = "with" },
                item(Lang.T("Align &left"), () => Align(0, -1), "align"),
                item(Lang.T("Centre &horizontally"), () => Align(0, 0), "align"),
                item(Lang.T("Align &right"), () => Align(0, 1), "align"),
                new ToolStripSeparator(),
                item(Lang.T("Align &top"), () => Align(1, -1), "align"),
                item(Lang.T("Centre &vertically"), () => Align(1, 0), "align"),
                item(Lang.T("Align &bottom"), () => Align(1, 1), "align"),
                new ToolStripSeparator(),
                item(Lang.T("Distribute hori&zontally"), () => Distribute(0), "distribute"),
                item(Lang.T("Distribute verti&cally"), () => Distribute(1), "distribute"),
            };
        }

        void UpdateAlignItems(ToolStripItemCollection items)
        {
            int n = canvas.Selection.Count;
            foreach (ToolStripItem i in items)
            {
                string tag = i.Tag as string;
                if (tag == "with") i.Text = n > 1 ? Lang.T("Aligns with the selection") : Lang.T("Aligns with the printable area");
                else if (tag == "align") i.Enabled = n > 0;
                else if (tag == "distribute") { i.Enabled = n >= 3; i.ToolTipText = n >= 3 ? null : Lang.T("Select three or more objects."); }
            }
        }

        // ---------- layers and the context menu ----------

        void Order(Layer how)
        {
            var these = SelectedObjects;
            if (these.Count == 0 || !doc.CanRestack(these, how)) return;
            PushUndo(null);
            doc.Restack(these, how);
            Touched();
        }

        static readonly LayerIcon[] LayerIcons = { LayerIcon.BringToFront, LayerIcon.BringForward, LayerIcon.SendBackward, LayerIcon.SendToBack };

        List<ToolStripItem> LayerItems()
        {
            string[] names = { Lang.T("Bring to &front"), Lang.T("Bring &forward"), Lang.T("Send &backward"), Lang.T("Send to bac&k") };
            // Ctrl+] / Ctrl+[ as in Office; German keyboards type ] [ only with AltGr, so the German menu shows the page
            // keys (both work on every keyboard: LayerKey).
            string[] keys = { Lang.T("Ctrl+Shift+]"), Lang.T("Ctrl+]"), Lang.T("Ctrl+["), Lang.T("Ctrl+Shift+[") };
            var list = new List<ToolStripItem>();
            for (int i = 0; i < 4; i++)
            {
                var how = (Layer)i;
                list.Add(new ToolStripMenuItem(names[i], null, delegate { Order(how); })
                         { Tag = how, ShortcutKeyDisplayString = keys[i], ImageScaling = ToolStripItemImageScaling.None });
            }
            return list;
        }

        // Before a menu with layer items opens: possible or not for the selection, icons at the window's DPI.
        void UpdateLayerItems(ToolStripDropDown menu)
        {
            var these = SelectedObjects;
            int n = S(16);
            var dropDown = menu as ToolStripDropDownMenu;
            if (dropDown != null) dropDown.ImageScalingSize = new Size(n, n);
            foreach (ToolStripItem item in menu.Items)
            {
                if (!(item.Tag is Layer)) continue;
                var how = (Layer)item.Tag;
                item.Enabled = these.Count > 0 && doc.CanRestack(these, how);
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp))
                    Brand.DrawLayerIcon(g, LayerIcons[(int)how], new RectangleF(0, 0, n, n), Theme.Of(item.Enabled ? Role.Text : Role.Dim));
                if (item.Image != null) item.Image.Dispose();
                item.Image = bmp;
            }
        }

        ContextMenuStrip objectMenu;

        // Right click on an object (or the menu key / Shift+F10): its commands. The shortcuts are shown only; the
        // menu bar and the canvas handle the keys.
        void ShowObjectMenu(Point at)
        {
            if (canvas.Selection.Count == 0) return;
            if (objectMenu != null) objectMenu.Dispose();
            var menu = objectMenu = new ContextMenuStrip { Font = menuBar.Font };
            Func<string, string, Action, ToolStripMenuItem> item = (text, keys, click) =>
                new ToolStripMenuItem(text, null, delegate { click(); }) { ShortcutKeyDisplayString = keys };
            menu.Items.Add(item(Lang.T("D&uplicate"), KeyText(Keys.Control | Keys.D), Duplicate));
            menu.Items.Add(item(Lang.T("&Delete"), Lang.T("Del"), Delete));
            menu.Items.Add(new ToolStripSeparator());
            var rotate = item(Lang.T("R&otate 90°"), KeyText(Keys.Control | Keys.R), Rotate);
            rotate.Enabled = canvas.Selection.Any(x => !(x is LineObject));
            menu.Items.Add(rotate);
            menu.Items.Add(new ToolStripMenuItem(Lang.T("Re&set rotation"), null, delegate { ResetRotation(); }) { Enabled = Turned });
            var align = new ToolStripMenuItem(Lang.T("&Align"));
            align.DropDownItems.AddRange(AlignItems().ToArray());
            UpdateAlignItems(align.DropDownItems);
            menu.Items.Add(align);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.AddRange(LayerItems().ToArray());
            UpdateLayerItems(menu);
            menu.Show(canvas, at);
        }

        // As the menu bar writes shortcuts (in Windows' language, like "Strg+D").
        static string KeyText(Keys keys)
        {
            return System.ComponentModel.TypeDescriptor.GetConverter(typeof(Keys)).ConvertToString(keys);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Ctrl + the number pad's + and − zoom too.
            if (keyData == (Keys.Control | Keys.Add) || keyData == (Keys.Control | Keys.Subtract))
            {
                ZoomStep(keyData == (Keys.Control | Keys.Add) ? 1 : -1, null);
                return true;
            }
            if (keyData == Keys.F1) { HelpPages.Open(this, HelpPage(canvas.Selection)); return true; }
            var how = LayerKey(keyData);
            if (how.HasValue && canvas.Selection.Count > 0) { Order(how.Value); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // F1: the help page of the selected kind of object; nothing or several kinds selected: the contents.
        internal static string HelpPage(IEnumerable<LabelObject> selection)
        {
            var pages = selection.Select(o => o is TextObject ? "text.html" : o is ImageObject ? "pictures.html" : o is BarcodeObject ? "codes.html"
                                              : o is LineObject || o is BoxObject ? "lines-boxes.html" : HelpPages.LabelMaker).Distinct().ToList();
            return pages.Count == 1 ? pages[0] : HelpPages.LabelMaker;
        }

        // Ctrl+PgUp / Ctrl+PgDn on every keyboard, Ctrl+] / Ctrl+[ where the keyboard types ] and [ without AltGr;
        // with Shift all the way to the front or back.
        static Layer? LayerKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode, mods = keyData & Keys.Modifiers;
            bool all = mods == (Keys.Control | Keys.Shift);
            if (mods != Keys.Control && !all) return null;
            bool? up = key == Keys.PageUp ? true : key == Keys.PageDown ? false : (bool?)null;
            if (up == null && key == PlainKey(']')) up = true;
            if (up == null && key == PlainKey('[')) up = false;
            if (up == null) return null;
            return up.Value ? (all ? Layer.ToFront : Layer.Forward) : (all ? Layer.ToBack : Layer.Backward);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern short VkKeyScanW(char ch);

        // The key that types c on this keyboard without Shift, Ctrl or Alt (AltGr), or None.
        static Keys PlainKey(char c)
        {
            short v = VkKeyScanW(c);
            return v != -1 && (v >> 8) == 0 ? (Keys)(v & 0xFF) : Keys.None;
        }

        void OnCanvasKey(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { canvas.Selected = null; e.Handled = true; return; }
            if (canvas.Selection.Count == 0) return;
            if (e.KeyCode == Keys.Delete) { Delete(); e.Handled = true; return; }
            if (e.KeyCode == Keys.Apps || (e.KeyCode == Keys.F10 && e.Shift)) { ShowObjectMenu(canvas.MenuPoint); e.Handled = true; return; }
            double step = e.Shift ? 1 : 0.1, dx = 0, dy = 0;
            switch (e.KeyCode)
            {
                case Keys.Left: dx = -step; break;
                case Keys.Right: dx = step; break;
                case Keys.Up: dy = -step; break;
                case Keys.Down: dy = step; break;
                default: return;
            }
            if (lastEdited != canvas) PushUndo(canvas);   // one undo step for a run of arrow keys
            foreach (var o in canvas.Selection)
            {
                o.X = Math.Round(o.X + dx, 2);
                o.Y = Math.Round(o.Y + dy, 2);
            }
            Touched();
            FillProps(null);
            e.Handled = true;
        }

        // ---------- printing ----------
        async void Print()
        {
            if (printer == null || label == null || printing) return;
            if (lengthError.Text.Length > 0)
            {
                QlMessage.Show(this, lengthError.Text, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (layout.Approximate)
            {
                QlMessage.Show(this, Lang.F("{0} doesn't offer '{1}' yet. A new or changed label format must be saved first: Tools > QL Label Formats, then Save & apply.",
                                             printer, label.Name), AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            printing = true;
            UpdateCommands();
            try
            {
                if (!ConfirmProblems()) return;
                if (!await ConfirmPrinterReady()) return;
                using (var bmp = EditorRender.Render(doc, layout))
                using (var portrait = EditorRender.ForPrinter(bmp, layout))
                {
                    string name = path != null ? Path.GetFileNameWithoutExtension(path) : label.Name;
                    if (layout.Custom != null) LabelPrinter.Print(layout.Custom, portrait, layout.SkipX, Lang.T("QL label:") + " " + name, (short)copies.Value, null);
                    else LabelPrinter.Print(layout.Page, portrait, Lang.T("QL label:") + " " + name, (short)copies.Value, null);
                }
                labelStatus.Text = Lang.F("Sent to {0}.", printer);
                string sentTo = printer;
                bool first = true;
                PrinterCheck.AskAfterPrint(() => roll, () => { CheckRoll(); if (first) { first = false; Relayout(); } }, () => IsDisposed || printer != sentTo);
            }
            catch (Exception ex) { Failed(this, Lang.T("The label couldn't be printed."), ex); }
            finally
            {
                printing = false;
                UpdateCommands();
                if (closeWhenPrinted && !IsDisposed) { closeWhenPrinted = false; BeginInvoke((MethodInvoker)Close); }
            }
        }

        // Codes with a problem (red in the panel) don't stop printing: the user decides. The
        // dialog lists them; Cancel is the default. True: print.
        bool ConfirmProblems()
        {
            using (var d = ProblemsDialog())
                return d == null || d.ShowDialog(this) == DialogResult.OK;
        }

        // null when no code has a problem
        SetupResult ProblemsDialog()
        {
            var now = DateTime.Now;
            var found = doc.Objects.OfType<BarcodeObject>().Select(b => new KeyValuePair<BarcodeObject, string>(b, EditorRender.Problem(b, layout, now)))
                           .Where(p => p.Value != null).ToList();
            if (found.Count == 0) return null;
            var fields = found.Select(p => new KeyValuePair<string, string>(CodeName(p.Key), p.Value));
            bool blank = found.Any(p => p.Key.Problem(now) != null);
            string note = blank ? Lang.T("Codes with errors may not scan. A code that can't be made from its content stays blank.") : Lang.T("Codes with errors may not scan.");
            var d = new SetupResult(AppTitle, ResultKind.Warning, Lang.T("The label has errors"), fields, note, closeText: Lang.T("Cancel"));
            d.AddChoice(Lang.T("Print anyway"), DialogResult.OK, primary: false);
            HelpPages.SetPage(d, "codes.html");
            return d;
        }

        // "Code 128 „ABC-123“" for the problems dialog
        static string CodeName(BarcodeObject b)
        {
            string kind = b.Kind == Symbology.Qr ? "QR" : b.Kind == Symbology.Ean13 ? "EAN-13" : b.Kind == Symbology.Gs1_128 ? "GS1-128" : "Code 128";
            string s = (b.Data ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return kind + " " + Lang.F("“{0}”", s.Length > 20 ? s.Substring(0, 19) + "…" : s);
        }

        // As the label tool's test print: offline queue, printer errors and a roll that doesn't fit stop here.
        async Task<bool> ConfirmPrinterReady()
        {
            string p = printer;
            bool offline = false;
            try { offline = Spooler.WorkOffline(p); } catch (Exception) { }
            // As in QL Label Formats: not when the printer is switched off (Windows set offline by itself, the roll
            // check below says "not connected"); the last check decided with PrinterCheck.OfflineIsCause.
            if (offline && roll != null && roll.Printer == p && !roll.WorkOffline) offline = false;
            if (offline)
            {
                var answer = QlMessage.Show(this, Lang.F("{0} is set to 'Use Printer Offline': the label would wait on this PC and not print.\n\n" +
                                                          "Use the printer online now? Windows may ask for administrator permission.", p),
                                             AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.Yes && !await StatusForm.UseOnline(this, new[] { p })) return false;
            }
            if (!QlDevice.HasStatus(QlDevice.PortOf(p))) return true;
            string shown = labelStatus.Text;
            labelStatus.Text = Lang.F("Asking {0} for the loaded roll...", p);
            PrinterCheck c = null;
            try { c = await Task.Run(() => PrinterCheck.Run(p, true)); }
            catch (Exception) { }
            if (IsDisposed) return false;
            labelStatus.Text = shown;   // the label's line again, also when the print stops here
            if (c == null) return true;
            roll = c;
            ShowRoll();
            string warn = null;
            bool? fits = c.Media != null ? c.Media.Fits(label.Media) : null;
            if (c.Health == Health.Error || c.Health == Health.Warning)
                warn = p + ": " + (c.Detail ?? c.Headline) + "\n\n" + string.Join("\n", c.Advice);
            else if (fits == false)
                warn = Lang.F("The printer has {0} loaded, but '{1}' needs {2}.\n\nThe printer would stop with a 'wrong media' error (red LED). Load the matching roll first.",
                              c.Media.LoadedText, label.Name, label.Media.Display);
            if (warn == null) return true;
            return QlMessage.Show(this, warn + "\n\n" + Lang.T("Print anyway?"), AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                                   MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
    }
}
