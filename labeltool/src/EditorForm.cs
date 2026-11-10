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
    sealed partial class EditorForm : ScaledForm, IMessageFilter
    {
        public const string AppTitle = "QL Label Maker";   // the product name (Start menu), in every language

        readonly EditorCanvas canvas = new EditorCanvas { Dock = DockStyle.Fill };
        readonly Panel host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        // Over the canvas while objects reach beyond the label: how many, and that those parts don't print. Information
        // only: the objects stay visible and movable on the grey margin around the label.
        readonly Panel outsideBar = new Panel { Visible = false };
        string outsideText = "";
        LabelDoc doc = new LabelDoc();
        UndoStack undo = new UndoStack();   // the active tab's (EditorForm.Tabs.cs)
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
        // The window's main action, in the petrol accent (Theme.Primary).
        readonly System.Windows.Forms.Button printButton = new System.Windows.Forms.Button { AutoSize = true };
        // Its small ▾ part (a split button): a menu with Print and Print with data... (EditorForm.Arrange BuildCommandRow).
        readonly System.Windows.Forms.Button printMoreButton = new System.Windows.Forms.Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        readonly ContextMenuStrip printMenu = new ContextMenuStrip();
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
        int wheel;     // mouse wheel over the canvas: the part of a notch not used yet (precision touchpads)
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
        NumericUpDown numX, numY, numW, numH, sizeNum, thickNum, lineAngleNum, lineLengthNum;
        NumericUpDown angleNum;
        Button resetButton;
        readonly List<KeyValuePair<Button, ArrangeIcon>> turnButtons = new List<KeyValuePair<Button, ArrangeIcon>>();   // redrawn at each DPI
        ComboBox rotBox, fontBox, modeBox, kindBox, levelBox, textKindBox, qrTypeBox;
        Label notationLabel;
        readonly Dictionary<string, Control> qrEditors = new Dictionary<string, Control>();   // a QR type's fields by key
        TextBox dataBox;   // textBox (RunsBox): Editor.Text.cs
        CheckBox filledCheck, invertCheck, qrInvertCheck, showTextCheck;
        Label problemLabel, quietLabel, svgLabel;
        FlowLayoutPanel quietRow;
        // Codes with something in their quiet zone and the topmost object there (EditorRender.QuietZoneIntruders).
        List<KeyValuePair<BarcodeObject, LabelObject>> intruders = new List<KeyValuePair<BarcodeObject, LabelObject>>();
        TrackBar thresholdBar;

        public EditorForm(string file, string printerName, string formName)
        {
            StartupTrace.Mark("EditorForm: fields made");
            Text = AppTitle;
            Theme.Use(this);
            Theme.Changed += ThemeChanged;
            FormClosed += delegate { Theme.Changed -= ThemeChanged; };
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;  // scales nothing here (no AutoScaleDimensions): sizes go through S()
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(S(1100), area.Width), Math.Min(S(680), area.Height));
            MinimumSize = new Size(Math.Min(S(780), area.Width), Math.Min(S(520), area.Height));   // 520: all six tools fit the palette
            KeyPreview = true;
            HelpPages.SetPage(this, HelpPages.LabelMaker);   // F1 in its dialogs (About, language question); its own F1: ProcessCmdKey
            sizedDpi = startDpi;

            StartupTrace.Mark("EditorForm: theme, icon, size");
            SuspendLayout();   // one layout for the whole window, not one per added control
            BuildProperties();
            StartupTrace.Mark("EditorForm: properties panel");
            side = new Panel { Dock = DockStyle.Right, Width = S(SideWidth), AutoScroll = true, Padding = new Padding(S(16), S(12), S(12), S(12)) };
            side.Controls.Add(props);
            host.Controls.Add(canvas);
            Theme.Back(host, Role.Desk);
            // The canvas with the strip laid over its top (over the canvas only, not the panel or the palette). The strip takes
            // no room: when it comes or goes, the canvas, the zoom and the label stay where they are.
            var canvasArea = new Panel { Dock = DockStyle.Fill };
            canvasArea.Controls.Add(host);
            canvasArea.Controls.Add(outsideBar);
            // The tabs right above the canvas (between the palette and the panel), always shown: EditorForm.Tabs.cs.
            var tabbed = new Panel { Dock = DockStyle.Fill };
            tabbed.Controls.Add(canvasArea);
            tabbed.Controls.Add(strip);
            outsideBar.BringToFront();
            host.Resize += delegate { outsideBar.Width = host.ClientSize.Width; };   // not over the vertical scroll bar
            outsideBar.Paint += PaintOutsideBar;
            outsideBar.Resize += delegate { FitOutsideBar(); outsideBar.Invalidate(); };
            // Right of the palette: the Arrange toolbar over the canvas and the panel.
            var work = new Panel { Dock = DockStyle.Fill };
            work.Controls.Add(tabbed);
            work.Controls.Add(side);
            work.Controls.Add(BuildFitBar());   // under the Arrange toolbar, over the canvas and the panel
            work.Controls.Add(BuildArrangeBar());
            Controls.Add(work);
            Controls.Add(BuildPalette());
            Controls.Add(BuildCommandRow());
            Controls.Add(BuildMenu());
            Controls.Add(BuildStatusBar());
            ResumeLayout(true);
            StartupTrace.Mark("EditorForm: toolbars, palette, menu, status bar");

            canvas.Doc = doc;
            BuildTabs();
            canvas.SelectionChanged += delegate { lastEdited = null; FillProps(null); UpdateCommands(); };
            canvas.BeforeChange += delegate { PushUndo(null); dragBounds = canvas.Dragging ? SelectionBounds() : (RectangleF?)null; };
            canvas.Changed += delegate { Touched(); if (canvas.Dragging) FillPlace(); else FillProps(null); };
            canvas.KeyDown += OnCanvasKey;
            // The wheel over the canvas area zooms (as in drawing programs), also while a field in the panel has the focus.
            Application.AddMessageFilter(this);
            FormClosed += delegate { Application.RemoveMessageFilter(this); };
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
            canvas.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                if (canvas.TakeRightCancel()) return;   // it cancelled a drawing drag (EditorCanvas.Draw.cs)
                if (canvas.Editing != null) ShowTextMenu(e.Location); else ShowObjectMenu(e.Location);
            };
            WireTextEditing();   // EditorForm.TextEdit.cs
            canvas.Drawn += OnDrawn;   // EditorForm.Draw.cs
            canvas.DrawMoved += OnDrawMoved;
            SetUpPan();   // the middle mouse button drags the view: EditorForm.Pan.cs
            SetUpAltKey();   // Alt as the free-drag key opens no menu: EditorForm.AltKey.cs
            SetUpDrop();
            host.Resize += delegate { ApplyZoom(); };
            FormClosing += OnClosing;
            Shown += delegate { StartupTrace.Mark("shown"); Rescale(); Remeasure(); AlignLeft(); CheckRoll(); StartupTrace.Mark("shown: measured, roll check started"); StartupTrace.WhenIdle(); };
            // WinForms scales the fonts when the window gets another DPI (opened on or moved to a screen with other
            // scaling); the pixel sizes set through S() follow here.
            DpiChanged += delegate { BeginInvoke((MethodInvoker)delegate { Rescale(); Remeasure(); AlignLeft(); }); };

            StartupTrace.Mark("EditorForm: events");
            printers = QlPrinters.OurPrinters();
            StartupTrace.Mark("EditorForm: printers listed");
            printer = QlPrinters.Choose(printers, printerName);   // --printer, else the last one chosen, else the first
            FillPrinterBox();
            SetPrinter(printer, formName);
            StartupTrace.Mark("EditorForm: printer set (labels, page)");
            if (file != null) OpenFile(file);
            FillProps(null);
            UpdateCommands();
            UpdateTitle();
            StartupTrace.Mark("EditorForm: constructor done");
        }

        // Before the window exists DeviceDpi is Windows' system DPI, which can differ from the screen the window opens on
        // (scaling changed without signing out, a second screen): the constructor sizes for that screen (as MainForm).
        int S(int pixels) { return IsHandleCreated ? LogicalToDeviceUnits(pixels) : (int)Math.Round(pixels * startDpi / 96.0); }

        readonly int startDpi = Brand.ScreenDpi(Cursor.Position);
        int sizedDpi;   // the DPI the S() sizes were set for


        // Opened directly on a screen with other scaling than Windows' system DPI: WinForms scales as on a move there
        // (ScaledForm, Brand.MatchScreenDpi; after Load, which places the window); Rescale then corrects what S() had
        // already sized for that screen. Bold texts (Print, the panel's heading) follow the window's font after a DPI
        // change (ScaledForm, Brand.FollowFont; Print stayed small before).
        protected override bool MatchAfterLoad { get { return true; } }

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
                        if (c is TrackBar || (c is TextBoxBase && ((TextBoxBase)c).Multiline)) c.Height = z(c.Height);
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

        // The mouse wheel over the canvas area (the label and the grey area around it): in or out around the mouse
        // pointer, with or without Ctrl; Shift+wheel and a tilted wheel scroll left and right. Elsewhere (panel, palette,
        // toolbars, an open drop-down list, another window) the wheel works as before.
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A, WM_MOUSEHWHEEL = 0x020E;
            if (m.Msg != WM_MOUSEWHEEL && m.Msg != WM_MOUSEHWHEEL) return false;
            var target = Control.FromChildHandle(m.HWnd);
            if (target == null || target.FindForm() != this || !Visible) return false;
            long l = m.LParam.ToInt64();
            var screen = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
            if (!host.RectangleToScreen(host.ClientRectangle).Contains(screen) && !(outsideBar.Visible && outsideBar.RectangleToScreen(outsideBar.ClientRectangle).Contains(screen)))
                return false;
            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            var keys = ModifierKeys;
            if ((keys & Keys.Alt) != 0) return false;
            if (m.Msg == WM_MOUSEHWHEEL || (keys & Keys.Shift) != 0)
            {
                // tilted right / Shift+wheel down: further right
                int px = (m.Msg == WM_MOUSEHWHEEL ? delta : -delta) * S(48) / 120;
                var now = host.AutoScrollPosition;
                host.AutoScrollPosition = new Point(-now.X + px, -now.Y);
                return true;
            }
            var at = canvas.PointToClient(screen);
            wheel += delta;
            while (Math.Abs(wheel) >= 120)
            {
                int direction = Math.Sign(wheel);
                wheel -= direction * 120;
                ZoomStep(direction, at);
            }
            return true;
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
    }
}
