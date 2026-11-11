// QL Label Maker, File > "Print with data...": the open label
// is the template, a CSV or JSON file the data (PrintDataFile.cs). Top (mockup A "Kacheln", 2026-11-09;
// PrintDataHead.cs): three tiles, the printer (a list: the wrong one is often noticed only here; the editor switches with
// it) with its state and loaded roll, the template, the data file ("Change...", without a file one large button); under
// them the fields as chips matched both ways (✗ missing first: blocks unless the field has a default or "Leave missing
// fields empty" is on, ✓ found, the special columns, the columns the label doesn't use; "+N more" for the rest). Middle: a search and the grid of previews as printed (PrintDataGrid.cs: selection by frame, all selected at the
// start; a ✕ empties the search). Bottom: copies per label, what was sent, Close, "Print (N labels)" = the selected cards
// in view times their copies. The dialog stays open after printing (2026-11-09: print again, other labels). Double-click: the large preview, hatched where the printer can't print, with its own Print. Printing: the records' problems (blocking ones stop, warnings ask "Print anyway"), the editor's printer
// check (roll, offline), then one job (LabelJob.Print). Resizable (minimum 780 × 660 at 100 %), size remembered per user.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class PrintDataDialog : ScaledForm
    {
        // What the editor gives the dialog.
        public sealed class Options
        {
            public LabelJob Job;
            public string TemplateName;        // the file's name, or the label format's for an unsaved label
            public string LabelText;           // "62 mm × 29 mm" or the label format
            public string PrinterText;         // "Brother QL-800 (Unidrv)": shown when there is no choice (Printers null)
            public List<string> Printers;      // the QL printers to choose from; Printer the one in use
            public string Printer;
            public Func<string, Switched> SwitchPrinter;   // the editor switches to it too; null: no change
            public string DocumentName;        // the spooler's job name
            public string Folder;              // where the file chooser starts
            public string CsvPath;             // a CSV to open at once (tests), else null; not remembered in the label
            public PrintDataLink Link;         // the label's data file (loaded at once), or null
            public string LabelPath;           // the label's file (the link's relative path), null: not saved yet
            public Func<PrintDataLink, bool> LinkChanged;   // another file chosen, found elsewhere, or not remembered (null); true: in the label's file now
            public Func<IWin32Window, Task<bool>> ConfirmPrinterReady;   // the editor's roll and offline check
            public Func<string, object, object> Setting;                 // the editor's per-user settings (value null: read)
        }

        // The editor's answer to a printer change: the new layout and label, or why that printer can't print this template.
        public sealed class Switched
        {
            public LabelLayout Layout;
            public string LabelText;
            public string Error;
        }

        const int MinWidth = 780, MinHeight = 660;   // at 96 dpi (the approved mockup)
        readonly Options o;
        LabelJob job;           // new when the printer changes (its layout)
        string printer;         // the printer in use
        CsvTable table;
        string csvPath;
        bool printing;
        PrintDataLink link;     // the label's data file (Options.Link, then as changed here)
        LinkState? linkState;   // null: still looking (a network folder may take a while); else how it went
        string linkError;       // Unreadable: why
        ContextMenuStrip changeMenu;

        readonly HeadTile printerTile = new HeadTile();
        readonly HeadTile templateTile = new HeadTile();
        readonly HeadTile dataTile = new HeadTile();
        TableLayoutPanel tiles;
        List<string> printers;   // the printers to choose from (null: no choice)
        ContextMenuStrip printerList;
        readonly Button chooseButton = new Button { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(4, 0, 4, 0) };
        readonly ChipRow fieldChips = new ChipRow();
        readonly CheckBox missingEmpty = new CheckBox { AutoSize = true };
        readonly FlowLayoutPanel emptyFrame = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0), Padding = Padding.Empty };
        bool emptyHot;
        readonly Label waitInfo = Theme.Fore(new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 0, 0, 0) }, Role.Dim);
        readonly TextBox search = new TextBox();
        readonly Label shownInfo = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(8, 6, 0, 0) }, Role.Dim);
        readonly Label sizeLabel = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(24, 6, 2, 0) }, Role.Dim);
        readonly StepSlider sizeSlider = new StepSlider { Steps = PrintDataGrid.CardSizes.Length, Width = 110, Height = 22, Margin = new Padding(0, 2, 0, 0) };
        readonly Label selectedInfo = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(0, 6, 4, 0) }, Role.Dim);
        readonly LinkLabel allLink = new LinkLabel { AutoSize = true, Margin = new Padding(4, 6, 4, 0) };
        readonly LinkLabel noneLink = new LinkLabel { AutoSize = true, Margin = new Padding(4, 6, 0, 0) };
        readonly PrintDataGrid grid = new PrintDataGrid { Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 4) };
        readonly NumericUpDown copies = new NumericUpDown { Minimum = 1, Maximum = DataRecord.MaxCopies, Value = 1, Width = 56, TextAlign = HorizontalAlignment.Right };
        readonly Label sentInfo = Theme.Fore(new Label { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, Role.Dim);
        readonly Button cancel = new Button { AutoSize = true, DialogResult = DialogResult.Cancel };
        readonly Button print = new Button { AutoSize = true };
        readonly ToolTip tips = new ToolTip();

        // After printing (the last job sent; null: nothing printed): what the editor's status bar says.
        public string Sent { get; private set; }
        string sentNote;   // the footer's "sent" line (emptied when the printer changes)

        public PrintDataDialog(Options options)
        {
            o = options;
            job = o.Job;
            printer = o.Printer ?? (job.Layout != null ? job.Layout.Printer : null);
            Text = Lang.T("Print with data") + " – " + EditorForm.AppTitle;
            Theme.Use(this);
            HelpPages.F1(this);
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;
            ShowInTaskbar = false;
            MinimizeBox = false;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }   // QL Label Maker's, not WinForms' default (2026-11-09)
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            ClientSize = new Size(MinWidth - 16, MinHeight - 39);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(12, 10, 12, 10) };
            Func<Control[], FlowLayoutPanel> flow = cs =>
            {
                var f = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill, Margin = Padding.Empty };
                f.Controls.AddRange(cs);
                return f;
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            // [Printer ▾] [Template] [Data  Change...]: three tiles, the outer two a little wider (mockup A)
            var tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 125));
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 125));
            printerTile.Icon = TileIcon.Printer;
            templateTile.Icon = TileIcon.Label;
            dataTile.Icon = TileIcon.Table;
            printerTile.Margin = new Padding(0, 0, 4, 0);
            templateTile.Margin = new Padding(4, 0, 4, 0);
            dataTile.Margin = new Padding(4, 0, 0, 0);
            foreach (var t in new[] { printerTile, templateTile, dataTile }) { t.Dock = DockStyle.Fill; t.Font = Font; }
            if (o.Printers != null && o.Printers.Count > 0 && o.SwitchPrinter != null)
            {
                printers = o.Printers.ToList();
                if (printer != null && !printers.Contains(printer)) printers.Insert(0, printer);
                printerTile.Chevron = true;
                printerTile.Clickable = true;
                printerTile.Activated += delegate { ShowPrinterList(); };
                printerTile.Step += (s, by) =>
                {
                    int i = printers.IndexOf(printer) + by;
                    if (i >= 0 && i < printers.Count) ChoosePrinter(printers[i]);
                };
            }
            ShowPrinter(TileState.Unknown, null, "");   // the editor gives the printer's state (ShowPrinter)
            ShowTemplate(o.LabelText);
            tips.SetToolTip(templateTile, o.TemplateName + "\n" + Lang.T("The open label: its fields are filled from the data."));
            link = o.Link;
            chooseButton.Text = Lang.T("Change...");
            // with a remembered file "Change ▾": the arrow (or Alt+Down) opens the menu, the rest chooses another file
            chooseButton.Click += delegate
            {
                if (link != null && chooseButton.PointToClient(Cursor.Position).X >= chooseButton.Width - LogicalToDeviceUnits(20) &&
                    chooseButton.ClientRectangle.Contains(chooseButton.PointToClient(Cursor.Position))) ShowChangeMenu();
                else ChooseCsv();
            };
            chooseButton.KeyDown += (s, e) =>
            {
                if (link != null && (e.KeyCode == Keys.Apps || (e.Alt && e.KeyCode == Keys.Down) || e.KeyCode == Keys.F4)) { ShowChangeMenu(); e.Handled = true; }
            };
            chooseButton.AccessibleName = Lang.T("Choose data file...");
            tips.SetToolTip(chooseButton, Lang.T("Choose another data file"));
            dataTile.Side = chooseButton;
            dataTile.Controls.Add(chooseButton);
            dataTile.Activated += delegate { ChooseCsv(); };
            tiles.Controls.Add(printerTile, 0, 0);
            tiles.Controls.Add(templateTile, 1, 0);
            tiles.Controls.Add(dataTile, 2, 0);
            root.Controls.Add(tiles, 0, 0);
            this.tiles = tiles;

            // Fields  [✗ … ✓ … not on the label: …  +N more]   [✓] Leave missing fields empty (i)
            var fieldsRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            fieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fieldsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var fieldsKey = Theme.Fore(new Label { Text = Lang.T("Fields"), AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 8, 0) }, Role.Dim);
            fieldChips.MoreText = Lang.T("+{0} more");
            fieldChips.Dock = DockStyle.Fill;
            fieldChips.Margin = Padding.Empty;
            fieldChips.Height = fieldChips.ChipHeight;
            fieldChips.AccessibleName = Lang.T("Fields");
            missingEmpty.Text = Lang.T("Leave missing fields empty");
            missingEmpty.Margin = new Padding(4, 2, 4, 2);
            missingEmpty.CheckedChanged += delegate { job.MissingEmpty = missingEmpty.Checked; grid.ClearPreviews(); ShowMatch(); UpdatePrint(); };
            // red frame around the box while a missing field stops printing (it frees it)
            emptyFrame.Controls.Add(missingEmpty);
            emptyFrame.Paint += (s, e) =>
            {
                if (!emptyHot) return;
                using (var p = new Pen(Theme.Of(Role.ErrorText), Math.Max(1, LogicalToDeviceUnits(1))))
                    e.Graphics.DrawRectangle(p, 0, 0, emptyFrame.Width - 1, emptyFrame.Height - 1);
            };
            waitInfo.Text = Lang.T("waiting for data");
            var fieldsInfo = StatusForm.InfoIcon(Font.Height);
            fieldsInfo.Anchor = AnchorStyles.Left;
            fieldsInfo.Margin = new Padding(6, 0, 0, 0);
            InfoTip.Attach(fieldsInfo, () => Lang.T("The label's fields and the data's columns of the same name. ✓ found. ✗ missing: Print stops, unless the field has a default value or “Leave missing fields empty” is on. Columns the label doesn't use are listed after them. A column labelname names the labels, a column copies says how many of each are printed."));
            fieldsRow.Controls.Add(fieldsKey, 0, 0);
            fieldsRow.Controls.Add(fieldChips, 1, 0);
            fieldsRow.Controls.Add(waitInfo, 2, 0);
            fieldsRow.Controls.Add(emptyFrame, 3, 0);
            fieldsRow.Controls.Add(fieldsInfo, 4, 0);
            root.Controls.Add(fieldsRow, 0, 1);

            // search · count · Größe ──●── ........ n selected  All  None
            var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tools.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            search.Width = 190;
            search.Margin = new Padding(0, 2, 0, 0);
            search.TextChanged += delegate { Filter(false); };
            search.HandleCreated += delegate { SendMessage(search.Handle, EmSetCueBanner, (IntPtr)1, Lang.T("Search")); };
            search.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape && search.Text.Length > 0) { search.Text = ""; e.SuppressKeyPress = true; e.Handled = true; } };
            SearchClear.Attach(search, tips, Lang.T("Clear search (Esc)"));
            // the card size (2026-11-09): drag, click, the wheel over the slider, or Ctrl+wheel in the grid
            sizeLabel.Text = Lang.T("Zoom");   // as in Word's status bar (2026-11-09: "Size" didn't read as the previews)
            sizeSlider.Width = LogicalToDeviceUnits(110);
            new ToolTip().SetToolTip(sizeSlider, Lang.T("Make the previews larger or smaller (Ctrl+mouse wheel)"));
            object storedStep = o.Setting != null ? o.Setting("PrintDataCardSize", null) : null;
            int step;
            if (storedStep != null && int.TryParse(storedStep.ToString(), out step)) { sizeSlider.Value = step; grid.SizeStep = step; }
            sizeSlider.Scrolled += delegate { grid.SizeStep = sizeSlider.Value; RememberCardSize(); };
            grid.SizeStepChanged += delegate { sizeSlider.Value = grid.SizeStep; RememberCardSize(); };
            tools.Controls.Add(flow(new Control[] { search, shownInfo, sizeLabel, sizeSlider }), 0, 0);
            allLink.Text = Lang.T("All");
            noneLink.Text = Lang.T("None");
            allLink.LinkClicked += delegate { grid.SelectAll(); };
            noneLink.LinkClicked += delegate { grid.SelectNone(); };
            tools.Controls.Add(flow(new Control[] { selectedInfo, allLink, noneLink }), 1, 0);
            root.Controls.Add(tools, 0, 2);

            // the grid
            grid.Renderer = (r, size) => EditorRender.RenderPreview(job.Fill(r), job.Layout, job.Now, size, grid.LabelWidthMm, grid.LabelHeightMm, grid.PrintableMm);
            grid.CopiesOf = r => r.CopiesError != null ? 0 : r.Copies;
            ShowLabelShape();
            grid.SelectionChanged += delegate { UpdatePrint(); };
            grid.Open += ShowLarge;
            grid.AccessibleName = Lang.T("Labels");
            root.Controls.Add(grid, 0, 3);

            // Copies per label: [1]   (what was sent last)   [Close] [Print (N labels)]
            var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, AutoSize = true, Margin = new Padding(0, 6, 0, 0) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var copiesLabel = new Label { Text = Lang.T("Copies per label:"), AutoSize = true, Margin = new Padding(0, 7, 4, 0) };
            copies.Margin = new Padding(0, 4, 10, 0);
            copies.ValueChanged += delegate { job.Copies = (int)copies.Value; UpdatePrint(); };
            sentInfo.Margin = new Padding(0, 4, 8, 0);
            cancel.Text = Lang.T("Close");
            cancel.Margin = new Padding(0, 2, 6, 0);
            print.Margin = new Padding(0, 2, 0, 0);
            print.Click += delegate { Print(); };
            Theme.Primary(print);
            // Close as large as Print (2026-11-09), in its normal look; Print's text changes with the count
            print.SizeChanged += delegate { cancel.MinimumSize = print.Size; };
            bottom.Controls.Add(copiesLabel, 0, 0);
            bottom.Controls.Add(copies, 1, 0);
            bottom.Controls.Add(sentInfo, 2, 0);
            bottom.Controls.Add(cancel, 3, 0);
            bottom.Controls.Add(print, 4, 0);
            root.Controls.Add(bottom, 0, 4);

            for (int i = 0; i < 5; i++) root.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, 100));
            Controls.Add(root);
            CancelButton = cancel;

            ShowData();
            ShowMatch();
            Filter(true);
        }

        // ---------- size: minimum as the mockup, remembered per user ----------

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            float s = DeviceDpi / 96f;
            var area = Screen.FromControl(Owner ?? this).WorkingArea;
            // the minimum and the size within the screen's working area (a small screen at 150 %: 660 px × 1.5 is taller
            // than 768 px), the window wholly inside it (dialog review 2026-11-09, P4; as the editor's window)
            MinimumSize = new Size(Math.Min((int)(MinWidth * s), area.Width), Math.Min((int)(MinHeight * s), area.Height));
            var size = MinimumSize;
            var stored = o.Setting != null ? o.Setting("PrintDataSize", null) as string : null;
            int w, h;
            if (stored != null && stored.Split(',').Length == 2 && int.TryParse(stored.Split(',')[0], out w) && int.TryParse(stored.Split(',')[1], out h))
                size = new Size(Math.Max(size.Width, (int)(w * s)), Math.Max(size.Height, (int)(h * s)));
            Size = new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
            var at = Owner != null ? new Point(Owner.Left + (Owner.Width - Width) / 2, Owner.Top + (Owner.Height - Height) / 2) : Location;
            Location = Inside(at, Size, area);
            SizeTiles();
            if (o.CsvPath != null) Load(o.CsvPath);
            else if (link != null) FindLinked();
            grid.Select();
        }

        // A window of this size at 'at', moved so that it lies wholly inside the area (as far as it fits).
        internal static Point Inside(Point at, Size size, Rectangle area)
        {
            int x = Math.Max(area.Left, Math.Min(at.X, area.Right - size.Width));
            int y = Math.Max(area.Top, Math.Min(at.Y, area.Bottom - size.Height));
            return new Point(x, y);
        }

        // Moved to a screen with other scaling: the minimum as the mockup at that scaling (within that screen).
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            MinimumSize = Size.Empty;   // the suggested size first, then the new minimum
            base.OnDpiChanged(e);
            float s = e.DeviceDpiNew / 96f;
            var area = Screen.FromRectangle(e.SuggestedRectangle).WorkingArea;
            MinimumSize = new Size(Math.Min((int)(MinWidth * s), area.Width), Math.Min((int)(MinHeight * s), area.Height));
            SizeTiles();
        }

        // The tiles' height: three lines of the window's font (it changes with the screen's scaling).
        void SizeTiles()
        {
            if (tiles == null) return;
            tiles.Height = printerTile.PreferredTileHeight;
            fieldChips.Height = fieldChips.ChipHeight;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (o.Setting != null && WindowState == FormWindowState.Normal)
            {
                float s = DeviceDpi / 96f;
                o.Setting("PrintDataSize", (int)(Width / s) + "," + (int)(Height / s));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (printing) e.Cancel = true;   // the job is being sent
            base.OnFormClosing(e);
        }

        // Ctrl+A in the grid's window (not in the search box): all labels in view.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.A) && !search.Focused) { grid.SelectAll(); return true; }
            if (keyData == (Keys.Control | Keys.F)) { search.Focus(); search.SelectAll(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void RememberCardSize() { if (o.Setting != null) o.Setting("PrintDataCardSize", grid.SizeStep); }

        // ---------- the printer ----------

        // The printer tile: the printer in use, its state from the editor (it checks the roll after a change): the tile's
        // colour, the circle (null: none) and the line under the name ("Ready · 62 mm continuous"); tip: the full text.
        public void ShowPrinter(TileState state, Health? dot, string line, string tip = null)
        {
            printerTile.Show(Lang.T("Printer"), printer ?? o.PrinterText ?? "", line, state, dot);
            string text = (printer ?? o.PrinterText ?? "") + (string.IsNullOrEmpty(tip ?? line) ? "" : "\n" + (tip ?? line));
            if (printers != null) text += "\n" + Lang.T("The printer the labels are printed on (QL Label Maker switches with it)");
            tips.SetToolTip(printerTile, text);
        }

        // The template tile: the file, its label and how many fields it has.
        void ShowTemplate(string labelText)
        {
            int n = job.Fields.Count;
            string fields = n == 0 ? Lang.T("no data fields") : n == 1 ? Lang.T("1 field") : Lang.F("{0} fields", n);
            templateTile.Show(Lang.T("Template"), o.TemplateName, labelText + " · " + fields, n == 0 ? TileState.Warn : TileState.Neutral, null);
        }

        // The printer tile's list: the QL printers, the one in use ticked.
        void ShowPrinterList()
        {
            if (printers == null || printing) return;
            if (printerList != null) printerList.Dispose();
            printerList = new ContextMenuStrip { Font = Font, ShowImageMargin = false, ShowCheckMargin = true };
            foreach (var p in printers)
            {
                string name = p;
                printerList.Items.Add(new ToolStripMenuItem(name.Replace("&", "&&"), null, delegate { ChoosePrinter(name); }) { Checked = name == printer });
            }
            printerList.MinimumSize = new Size(printerTile.Width, 0);
            printerList.Show(printerTile, new Point(0, printerTile.Height));
        }

        // Another printer from the list (or Up/Down on the tile).
        internal void ChoosePrinter(string name)
        {
            if (name == null || name == printer) return;
            SwitchPrinter(name);
        }

        void ShowLabelShape()
        {
            if (job.Layout != null && job.Layout.ViewWidthMm > 0 && job.Layout.ViewHeightMm > 0)
                grid.SetLabel((float)job.Layout.ViewWidthMm, (float)job.Layout.ViewHeightMm, job.Layout.Printable);
        }

        // Another printer chosen: the editor switches with it (its label may change with the model), the previews as that
        // printer prints them. One it can't print this template on: the reason, and back to the printer before.
        void SwitchPrinter(string name)
        {
            if (printing || name == null || name == printer) return;
            Switched s;
            Cursor = Cursors.WaitCursor;
            try { s = o.SwitchPrinter(name); }
            catch (Exception ex) { s = new Switched { Error = ex.Message }; }
            finally { Cursor = Cursors.Default; }
            if (s != null && s.Error == null && s.Layout != null) { UsePrinter(name, s); return; }
            string error = s != null && s.Error != null ? s.Error : Lang.F("{0} can't print this label.", name);
            Switched back = null;
            try { back = o.SwitchPrinter(printer); } catch (Exception) { }
            if (back != null && back.Error == null && back.Layout != null) UsePrinter(printer, back);
            QlMessage.Show(this, error, EditorForm.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void UsePrinter(string name, Switched s)
        {
            printer = name;
            job = job.On(s.Layout);
            if (s.LabelText != null) ShowTemplate(s.LabelText);
            printerTile.Show(printerTile.Caption, name, printerTile.Sub, printerTile.State, printerTile.Dot);   // the editor's check follows (ShowPrinter)
            sentNote = null;
            ShowLabelShape();
            UpdatePrint();
        }

        // ---------- the data file (CSV or JSON) ----------

        // The menu of "Change ▾" (a remembered file): another file, retry (the file not there), don't remember it.
        void ShowChangeMenu()
        {
            if (changeMenu != null) changeMenu.Dispose();
            changeMenu = new ContextMenuStrip { Font = Font, ShowImageMargin = false };
            var other = new ToolStripMenuItem(Lang.T("Other file..."), null, delegate { ChooseCsv(); });
            other.Font = new Font(other.Font, FontStyle.Bold);
            changeMenu.Items.Add(other);
            if (linkState.HasValue && linkState.Value != LinkState.Found)
                changeMenu.Items.Add(new ToolStripMenuItem(Lang.T("Retry"), null, delegate { FindLinked(); }));
            changeMenu.Items.Add(new ToolStripSeparator());
            changeMenu.Items.Add(new ToolStripMenuItem(Lang.T("Don't remember the file"), null, delegate { Forget(); }));
            changeMenu.Show(chooseButton, new Point(0, chooseButton.Height));
        }

        // Looks for the remembered file off the window's thread (an offline share takes a while), then loads it; not
        // there or unreadable: the tile and the card area say so (no message box, the rest stays usable).
        internal async void FindLinked()
        {
            var l = link;
            if (l == null) return;
            linkState = null;
            ShowData();
            Filter(true);
            LinkState state = LinkState.NotFound;
            string found = null;
            try { found = await Task.Run(() => l.Resolve(o.LabelPath, out state)); }
            catch (Exception) { state = LinkState.NotFound; }
            if (IsDisposed || link != l) return;
            if (found == null) { linkState = state; ShowData(); Filter(true); return; }
            linkState = LinkState.Found;
            if (!LoadFile(found, false)) return;
            if (!l.SameFile(found))
            {
                // moved together with the label: kept as saved (no asterisk), the next Save writes the new place
                link = PrintDataLink.For(found, o.LabelPath, l.Saved);
                if (o.LinkChanged != null) o.LinkChanged(link);
                ShowData();
            }
        }

        // "Don't remember the file": the label no longer names it (changed); what is loaded stays until the dialog closes.
        void Forget()
        {
            link = null;
            linkState = null;
            if (o.LinkChanged != null) o.LinkChanged(null);
            ShowData();
            Filter(false);
        }

        // A file chosen here: loaded, and remembered in the label (changed until it is saved).
        internal void UseFile(string path)
        {
            if (!LoadFile(path, true)) return;
            if (link != null && link.SameFile(path) && linkState == LinkState.Found) return;
            link = PrintDataLink.For(path, o.LabelPath);
            linkState = LinkState.Found;
            if (o.LinkChanged != null && o.LinkChanged(link)) link = link.SavedAt(o.LabelPath);   // written at once: "remembered"
            ShowData();
        }

        void ChooseCsv()
        {
            using (var d = new OpenFileDialog { Filter = Lang.T("Data files (*.csv;*.txt;*.json)|*.csv;*.txt;*.json|All files (*.*)|*.*"), Title = Lang.T("Choose data file") })
            {
                string folder = csvPath != null ? Path.GetDirectoryName(csvPath) : o.Folder;
                if (folder != null && Directory.Exists(folder)) d.InitialDirectory = folder;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                UseFile(d.FileName);
            }
        }

        // Reads the file; an unreadable one: the reason in a message (ask), else in the tile and card area (the remembered
        // file, opened by itself); the dialog stays as it was.
        new void Load(string path) { LoadFile(path, true); }

        bool LoadFile(string path, bool ask)
        {
            CsvTable t;
            try
            {
                Cursor = Cursors.WaitCursor;
                t = DataFile.Load(path);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                if (!ask)
                {
                    linkState = LinkState.Unreadable;
                    linkError = ex.Message;
                    ShowData();
                    Filter(true);
                    return false;
                }
                QlMessage.Ask(this, EditorForm.AppTitle, Lang.T("The data file can't be read."), Path.GetFileName(path) + "\n\n" + ex.Message,
                              MessageBoxButtons.OK, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button1, null);
                return false;
            }
            Cursor = Cursors.Default;
            table = t;
            csvPath = path;
            job.Records = DataRecord.FromTable(t);
            search.Text = "";
            ShowData();
            ShowMatch();
            Filter(true);
            return true;
        }

        // The data tile: the file and its rows (CSV) or entries (JSON); without a file one large button.
        void ShowData()
        {
            chooseButton.Text = link != null ? Lang.T("Change") + " \u25BE" : Lang.T("Change...");
            chooseButton.AccessibleName = link != null ? Lang.T("Change data file") : Lang.T("Choose data file...");
            // the remembered file: "Data · 🔗 remembered" (in the label's file) or "· 🔗 when saved" (chosen, not saved yet)
            string caption = link == null ? Lang.T("Data") : link.Saved ? Lang.T("Data · 🔗 remembered") : Lang.T("Data · 🔗 when saved");
            string remembered = link != null ? "\n" + Lang.T("Remembered with the label: Print with data loads it at once next time.") : "";
            if (table == null && link != null && linkState != LinkState.Found)
            {
                dataTile.CallToAction = false;
                dataTile.Clickable = false;
                chooseButton.Visible = true;
                string sub = !linkState.HasValue ? Lang.T("looking for it...")
                           : linkState == LinkState.FolderOffline ? "\u26A0 " + Lang.T("folder offline")
                           : linkState == LinkState.Unreadable ? "\u26A0 " + Lang.T("can't be read")
                           : "\u26A0 " + Lang.T("not found");
                dataTile.Show(caption, Path.GetFileName(link.Absolute), sub, linkState.HasValue ? TileState.Warn : TileState.Neutral, null);
                tips.SetToolTip(dataTile, link.Absolute + remembered);
                dataTile.PerformLayout();
                return;
            }
            if (table == null)
            {
                dataTile.CallToAction = true;
                dataTile.Clickable = true;
                chooseButton.Visible = false;
                dataTile.Show(Lang.T("Data"), "+ " + Lang.T("Choose data file..."), Lang.T("CSV or JSON, one row per label"), TileState.Neutral, null);
                dataTile.AccessibleName = Lang.T("Choose data file...");
                tips.SetToolTip(dataTile, Lang.T("Choose a data file: CSV (the first row names the columns) or JSON (a list of objects). Each column fills the field of the same name on the label; a column labelname names the labels."));
                return;
            }
            dataTile.CallToAction = false;
            dataTile.Clickable = false;
            chooseButton.Visible = true;
            int n = table.Rows.Count;
            string rows = table.Json ? (n == 1 ? Lang.T("1 entry") : Lang.F("{0} entries", n)) : (n == 1 ? Lang.T("1 row") : Lang.F("{0} rows", n));
            dataTile.Show(link != null && link.SameFile(csvPath) ? caption : Lang.T("Data"), Path.GetFileName(csvPath), (table.Json ? "JSON" : "CSV") + " · " + rows,
                          n == 0 || Blocked() != null ? TileState.Warn : TileState.Ok, null);
            tips.SetToolTip(dataTile, csvPath + (link != null && link.SameFile(csvPath) ? remembered : ""));
            dataTile.PerformLayout();
        }

        // Fields: ✗ missing first (red when it stops printing, dashed when a default or "leave empty" fills it), ✓ found,
        // the special columns (labelname, copies), then the columns not on the label. Before a file: the label's fields.
        void ShowMatch()
        {
            var list = new List<Chip>();
            bool data = table != null;
            if (job.Fields.Count == 0)
                list.Add(new Chip(Lang.T("The label has no data fields: every label prints the same."), ChipKind.Note));
            else if (!data)
                list.AddRange(job.Fields.Select(f => new Chip(f.Name, ChipKind.Waiting)));
            else
            {
                var m = job.Match(table.Columns);
                foreach (var f in m.Missing)
                {
                    if (!string.IsNullOrEmpty(f.Default) || !f.Required) list.Add(new Chip("✗ " + f.Name + " · " + Lang.F("default: {0}", f.Default ?? ""), ChipKind.Missing));
                    else if (job.MissingEmpty) list.Add(new Chip("✗ " + f.Name + " · " + Lang.T("stays empty"), ChipKind.Missing));
                    else list.Add(new Chip("✗ " + Lang.F("{0} missing", f.Name), ChipKind.Blocking));
                }
                foreach (var f in m.Found) list.Add(new Chip("✓ " + f.Name, ChipKind.Found));
                foreach (var c in table.Columns)
                {
                    if (string.Equals(c, DataRecord.NameColumn, StringComparison.OrdinalIgnoreCase) && !job.Fields.Any(f => string.Equals(f.Name, c, StringComparison.OrdinalIgnoreCase)))
                        list.Add(new Chip(Lang.F("Name: {0}", c), ChipKind.Special));
                    else if (string.Equals(c, DataRecord.CopiesColumn, StringComparison.OrdinalIgnoreCase))
                        list.Add(new Chip(Lang.F("Copies: {0}", c), ChipKind.Special));
                }
                if (m.Unused.Count > 0)
                {
                    list.Add(new Chip(Lang.T("not on the label:"), ChipKind.Key));
                    list.AddRange(m.Unused.Select(c => new Chip(c, ChipKind.Unused)));
                }
            }
            fieldChips.Set(list);
            tips.SetToolTip(fieldChips, job.Fields.Count == 0 ? Lang.T("The label has no data fields: every label prints the same. A field is a name in braces in a text, filled from the data's column of that name.") : null);
            waitInfo.Visible = !data && job.Fields.Count > 0;
            emptyFrame.Visible = data && job.Fields.Count > 0;
            emptyHot = data && Blocked() != null;
            emptyFrame.Invalidate();
            if (data) ShowData();   // the tile's colour follows a blocking field
        }

        // The search: hides cards (the selection stays).
        void Filter(bool newData)
        {
            string q = search.Text.Trim();
            var rows = job.Records.Where(r => r.Matches(q));
            string empty = table == null && link != null && linkState != LinkState.Found ? LinkProblem()
                         : table == null ? Lang.T("Choose a data file: CSV (the first row names the columns) or JSON (a list of objects). Each column fills the field of the same name on the label; a column labelname names the labels.")
                         : job.Records.Count == 0 ? Lang.T("The data file has no data rows.")
                         : Lang.F("No label matches “{0}”.", q);
            grid.SetRows(rows, newData, empty);
            int all = job.Records.Count, shown = grid.Shown.Count;
            shownInfo.Text = table == null ? "" : q.Length > 0 ? Lang.F("{0} of {1} shown", shown, all) : all == 1 ? Lang.T("1 label") : Lang.F("{0} labels", all);
            UpdatePrint();
        }

        // The card area while the remembered file isn't loaded: looking, not found, folder offline, unreadable (with the path).
        string LinkProblem()
        {
            if (!linkState.HasValue) return Lang.T("Looking for the remembered data file...") + "\n" + link.Absolute;
            switch (linkState.Value)
            {
                case LinkState.FolderOffline:
                    return Lang.T("The folder of the remembered data file isn't reachable right now:") + "\n" + Path.GetDirectoryName(link.Absolute) + "\n" +
                           Lang.T("When it is back: Change > Retry. Or choose another file.");
                case LinkState.Unreadable:
                    return Lang.T("The remembered data file can't be read.") + "\n" + link.Absolute + "\n" + (linkError ?? "");
                default:
                    return Lang.T("The remembered data file wasn't found.") + "\n" + link.Absolute + "\n" +
                           Lang.T("Choose it again with Change – or put it back there.");
            }
        }

        // "Print (N labels)": the selected cards in view times their copies; disabled for none, a blocking field or while printing.
        void UpdatePrint()
        {
            var sel = grid.SelectedShown;
            selectedInfo.Text = Lang.F("{0} selected", sel.Count);
            allLink.Enabled = noneLink.Enabled = !printing && grid.Shown.Count > 0;
            // while printing (the warnings, the awaited roll check, sending) nothing that changes the job: what is
            // printed is what was checked and shown (dialog review 2026-11-09, P1)
            chooseButton.Enabled = copies.Enabled = missingEmpty.Enabled = search.Enabled = grid.Enabled = printerTile.Enabled = dataTile.Enabled = !printing;
            long n = job.PageCount(sel);
            print.Text = n == 1 ? Lang.T("Print (1 label)") : Lang.F("Print ({0} labels)", n);
            string blocked = Blocked();
            print.Enabled = !printing && n > 0 && blocked == null;
            tips.SetToolTip(print, blocked ?? "");
            // a missing field that stops printing: said in the footer, in red (mockup A); else what was sent last
            Theme.Fore(sentInfo, blocked != null ? Role.ErrorText : Role.Dim);
            sentInfo.Text = blocked ?? sentNote ?? "";
            tips.SetToolTip(sentInfo, sentInfo.Text);   // cut off in a narrow window
            AcceptButton = null;
        }

        // Why printing can't start (null: it can).
        string Blocked()
        {
            if (table == null) return null;
            var b = job.Match(table.Columns).Blocking(job.MissingEmpty);
            if (b.Count == 0) return null;
            return Lang.F("{0} is missing in the data: add the column, or switch on “Leave missing fields empty”.", string.Join(", ", b.Select(f => "{" + f.Name + "}")));
        }

        // ---------- the large preview ----------

        void ShowLarge(DataRecord r)
        {
            Bitmap view;
            try { view = job.Render(r); }
            catch (Exception) { return; }
            int n = job.CopiesOf(r);
            using (view)
            using (var f = new PreviewWindow(view, grid.LabelWidthMm, grid.LabelHeightMm, grid.PrintableMm, r.Caption,
                                             Math.Max(1, Math.Min(DataRecord.MaxCopies, n)), !printing && Blocked() == null))
            {
                f.Print = async (copies, owner) =>
                {
                    // only this label, as many as its box says (not the copies column); the same checks as Print
                    var one = job.On(job.Layout);
                    one.Copies = 1;
                    string sent = await Send(one, new List<DataRecord> { r.WithCopies(copies) }, owner);
                    if (sent != null && !IsDisposed) { sentNote = Sent = sent; UpdatePrint(); }
                    return sent != null;
                };
                f.ShowDialog(this);
            }
        }

        sealed class PreviewWindow : ScaledForm
        {
            readonly Bitmap picture;
            readonly float w, h;
            readonly RectangleF printable;

            readonly Control area;
            readonly NumericUpDown copies = new NumericUpDown { Minimum = 1, Maximum = DataRecord.MaxCopies, Width = 56, TextAlign = HorizontalAlignment.Right, Margin = new Padding(0, 4, 10, 0) };
            readonly Button print = new Button { AutoSize = true, Margin = new Padding(6, 2, 0, 0) };
            readonly Button close = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Margin = new Padding(0, 2, 0, 0) };
            public Func<int, IWin32Window, Task<bool>> Print;   // copies → sent (the dialog's checks and job)

            public PreviewWindow(Bitmap view, float widthMm, float heightMm, RectangleF printableMm, string title, int startCopies, bool canPrint)
            {
                picture = (Bitmap)view.Clone();
                w = widthMm; h = heightMm; printable = printableMm;
                Text = title;
                Theme.Use(this);
                Font = UiFont.MessageBox;
                AutoScaleMode = AutoScaleMode.Dpi;
                ShowInTaskbar = false;
                MinimizeBox = MaximizeBox = false;
                try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
                StartPosition = FormStartPosition.CenterParent;
                KeyPreview = true;
                ClientSize = new Size(640, (int)Math.Min(520, Math.Max(200, 600 * h / Math.Max(1f, w) + 84)));

                // Copies: [n]  ........  [Close] [Print]
                var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, ColumnCount = 5, AutoSize = true, Padding = new Padding(12, 6, 12, 10) };
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                copies.Value = Math.Max(1, Math.Min(DataRecord.MaxCopies, startCopies));
                copies.AccessibleName = Lang.T("Copies:");
                close.Text = Lang.T("Close");
                print.Text = Lang.T("Print");
                print.Enabled = canPrint;
                Theme.Primary(print);
                print.Click += async delegate
                {
                    if (Print == null || !print.Enabled) return;
                    print.Enabled = close.Enabled = copies.Enabled = false;
                    bool sent = false;
                    try { sent = await Print((int)copies.Value, this); }
                    finally { if (!IsDisposed) print.Enabled = close.Enabled = copies.Enabled = true; }
                    if (sent && !IsDisposed) Close();
                };
                bar.Controls.Add(new Label { Text = Lang.T("Copies:"), AutoSize = true, Margin = new Padding(0, 7, 4, 0) }, 0, 0);
                bar.Controls.Add(copies, 1, 0);
                bar.Controls.Add(close, 3, 0);
                bar.Controls.Add(print, 4, 0);
                area = new LabelArea(this) { Dock = DockStyle.Fill };
                Controls.Add(area);
                Controls.Add(bar);
                CancelButton = close;
                // Close and Print the same size (2026-11-09, as in the dialog), the wider of the two; Close in its normal look
                Load += delegate
                {
                    var both = new Size(Math.Max(close.PreferredSize.Width, print.PreferredSize.Width), Math.Max(close.PreferredSize.Height, print.PreferredSize.Height));
                    close.MinimumSize = print.MinimumSize = both;
                };
            }

            // Closes while nothing is being sent (the buttons are off then).
            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!close.Enabled) e.Cancel = true;
                base.OnFormClosing(e);
            }

            sealed class LabelArea : Control
            {
                readonly PreviewWindow w;
                public LabelArea(PreviewWindow owner)
                {
                    w = owner;
                    SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                }
                protected override void OnPaint(PaintEventArgs e) { w.Draw(e.Graphics, ClientSize); }
            }

            // The label as the editor shows it: paper, the printed dots, hatched where the printer can't print, its edge.
            void Draw(Graphics g, Size size)
            {
                g.Clear(Theme.Of(Role.Desk));
                int pad = 20 * DeviceDpi / 96;
                float k = Math.Min((size.Width - 2f * pad) / w, (size.Height - 2f * pad) / h);
                if (k <= 0) return;
                var label = new RectangleF((size.Width - w * k) / 2, (size.Height - h * k) / 2, w * k, h * k);
                var pr = new RectangleF(label.X + printable.X * k, label.Y + printable.Y * k, printable.Width * k, printable.Height * k);
                using (var b = new SolidBrush(Theme.Fixed.Paper)) g.FillRectangle(b, label);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(picture, pr);
                // not printable: hatched, as the editor's canvas (EditorCanvas.cs)
                using (var region = new Region(label))
                using (var hatch = new System.Drawing.Drawing2D.HatchBrush(System.Drawing.Drawing2D.HatchStyle.BackwardDiagonal, Color.FromArgb(80, Theme.Fixed.PetrolLight), Color.Transparent))
                {
                    region.Exclude(pr);
                    g.FillRegion(hatch, region);
                }
                using (var p = new Pen(Theme.Fixed.TapeEdge)) g.DrawRectangle(p, label.X, label.Y, label.Width - 1, label.Height - 1);
            }

            protected override void Dispose(bool disposing) { if (disposing) picture.Dispose(); base.Dispose(disposing); }
        }

        // ---------- printing ----------

        async void Print()
        {
            if (printing || !print.Enabled) return;
            string sent = await Send(job, grid.SelectedShown, this);
            if (sent != null && !IsDisposed) { sentNote = Sent = sent; UpdatePrint(); }   // stays open: print again, or other labels
        }

        // One job of these records (the dialog's selection, or the large preview's one label): the records' problems
        // (blocking ones stop, warnings ask), the editor's printer check, then sending. Returns what was sent (null: nothing).
        async Task<string> Send(LabelJob job, IList<DataRecord> records, IWin32Window owner)
        {
            if (printing) return null;
            if (job.PageCount(records) > LabelJob.MaxPages)
            {
                QlMessage.Show(owner, Lang.F("More than {0} labels in one print job.", LabelJob.MaxPages), EditorForm.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return null;
            }
            var focused = ActiveControl;   // given back when the inputs are on again
            printing = true;
            UpdatePrint();
            try
            {
                // the records' problems: blocking ones stop, warnings ask
                List<RecordProblem> problems;
                Cursor = Cursors.WaitCursor;
                try { problems = job.ToCheck(records).SelectMany(r => job.Check(r)).ToList(); }
                finally { Cursor = Cursors.Default; }
                var blocking = problems.Where(p => p.Blocks).ToList();
                if (blocking.Count > 0)
                {
                    using (var d = Problems(blocking, ResultKind.Error, Lang.T("Some labels can't be printed"),
                                            Lang.T("Correct the data file, then choose it again. Or select only the labels without problems."), null))
                        d.ShowDialog(owner);
                    return null;
                }
                if (problems.Count > 0)
                    using (var d = Problems(problems, ResultKind.Warning, Lang.T("Some labels have problems"),
                                            Lang.T("They print as the previews show them."), Lang.T("Print anyway")))
                        if (d.ShowDialog(owner) != DialogResult.OK) return null;
                if (o.ConfirmPrinterReady != null && !await o.ConfirmPrinterReady(owner)) return null;

                Cursor = Cursors.WaitCursor;
                long total = job.PageCount(records);
                job.Now = DateTime.Now;
                job.Print(records, o.DocumentName, (i, n) =>
                {
                    selectedInfo.Text = Lang.F("Printing label {0} of {1}...", i, n);
                    selectedInfo.Refresh();
                });
                return total == 1 ? Lang.F("1 label sent to {0}.", job.Layout.Printer) : Lang.F("{0} labels sent to {1}.", total, job.Layout.Printer);
            }
            catch (Exception ex)
            {
                Cursor = Cursors.Default;
                QlMessage.Ask(owner, EditorForm.AppTitle, Lang.T("The labels couldn't be printed."), null, MessageBoxButtons.OK, MessageBoxIcon.Error,
                              MessageBoxDefaultButton.Button1, ex.Message);
                return null;
            }
            finally
            {
                Cursor = Cursors.Default;
                printing = false;
                if (!IsDisposed)
                {
                    UpdatePrint();
                    if (focused != null && !focused.IsDisposed && focused.CanFocus) focused.Focus();
                }
            }
        }

        // The problems of the records as a list (at most 12 rows, then "and N more").
        SetupResult Problems(List<RecordProblem> list, ResultKind kind, string headline, string note, string printAnyway)
        {
            const int Most = 12;
            var fields = list.Take(Most).Select(p => new KeyValuePair<string, string>(Lang.F("Row {0}", p.Row), p.Message)).ToList();
            if (list.Count > Most) fields.Add(new KeyValuePair<string, string>("", Lang.F("and {0} more", list.Count - Most)));
            var d = new SetupResult(EditorForm.AppTitle, kind, headline, fields, note, closeText: printAnyway != null ? Lang.T("Cancel") : null);
            if (printAnyway != null) d.AddChoice(printAnyway, DialogResult.OK, primary: false);
            return d;
        }

        const int EmSetCueBanner = 0x1501;
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
    }
}
