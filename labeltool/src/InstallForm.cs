// The setup window for beginners (QlLabelFormats.exe --install [<release folder>]; in the release zip as Setup.exe, the same
// exe): first install, update and repair from the release folder. One window instead of wizard pages: what will
// happen, a checklist of the steps with their state, questions only where needed, one main button whose text
// changes. Signing runs unelevated (driver\package.ps1 -SignOnly -OneTimeKeys, Windows' own functions, no SDK);
// the privileged work is driver\install.ps1, elevated once, whose -ResultFile step= lines show
// the progress and whose code= line explains a failure. The window itself never runs elevated, so the printer
// monitor's autostart goes to the signed-in user. ARM64 PCs install only in a one-time special start (key 7 =
// Disable driver signature enforcement): the window prepares it (bcdedit, elevated, on the user's click), restarts,
// reopens after sign-in (HKCU RunOnce, --after-restart), sees the special start itself (code integrity state) and
// installs; after the next normal start it opens once more to check. docs/setup.md.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class InstallForm : Form
    {
        enum StepState { Waiting, Working, Done, Problem, Skipped }
        // Prepare = ARM64 outside the special start: "Prepare and restart"; Restart = prepared, waiting for the restart.
        // AppUpdate = the zip's driver is the installed one (same release, Versions): only the programs (install.ps1 -AppOnly),
        // no signing, no special start, no restart.
        // Done = the view after the normal restart that ended the special start: the main button closes the window.
        // Uninstall = what will be removed, before; Removed = the uninstall's done view.
        enum Mode { Checking, Blocked, Install, Update, AppUpdate, Repair, Older, Prepare, Restart, Running, Finished, Failed, Done, Uninstall, Removed }

        static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "QLUnidrv");
        // The installed label tool.
        static readonly string InstalledExe = Path.Combine(AppDir, "QlLabelFormats.exe");
        static readonly string[] NetworkModels = { "QL-580N", "QL-710W", "QL-720NW", "QL-810W", "QL-820NWB", "QL-1060N", "QL-1110NWB", "QL-1115NWB" };
        // install.ps1's step= ids, in the words the window shows (null: unknown id)
        static string StepText(string step, bool appOnly)
        {
            switch (step)
            {
                case "certificates": return Lang.T("Trusting your certificate on this PC...");
                case "package": return Lang.T("Adding the driver to Windows...");
                case "printers": return Lang.T("Setting up the connected printers (can take a minute)...");
                case "app": return appOnly ? Lang.T("Updating the programs...") : Lang.T("Installing the programs...");
                case "labels": return Lang.T("Creating the label formats...");
                default: return null;
            }
        }

        // A step: one line of what to do now (Detail); the why and the details behind the (i) next to its title (Tip);
        // Links = the step's own links (picture, report). F7 test 2026-10-30: less text, one line per step.
        sealed class Step
        {
            public readonly StepMark Mark = new StepMark();
            public readonly Label Title = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 0, 0) };
            public readonly Label Detail = Theme.Fore(new Label { AutoSize = true }, Role.Dim);
            public readonly FlowLayoutPanel Head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            public readonly FlowLayoutPanel Links = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
            // The explanation: the app's own tip at the (i) (InfoTip; user's choice 2026-10-30: no Windows ToolTip,
            // and no text opening inline in the window)
            public Control Info;
            public string Tip;
            public bool Shown = true;  // the step is part of this window (special: ARM64 only); Visible reads false before the window shows
            public string Text = "";    // the detail without the elapsed time
            public DateTime? BusySince;  // the window waits for work outside it (a script, Windows' prompt): ring turns, time counts
        }

        // A step's marker (colours from Brand): done = petrol circle with a white tick, current = petrol ring with an
        // orange dot, busy = a petrol arc turning on a light ring, waiting = grey ring, problem = red circle with "!",
        // skipped = grey ring with a dash. Drawn, so it scales with the font and doesn't depend on a symbol font.
        sealed class StepMark : Control
        {
            StepState state;
            Timer spin;
            int angle;

            public StepMark()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            public StepState State { get { return state; } set { state = value; Invalidate(); } }

            // The setup waits for work outside the window (variant A of the progress mockup, 0504d06, user's choice
            // 2026-10-30: the install step looked frozen for 30-40 s)
            public bool Spinning
            {
                get { return spin != null; }
                set
                {
                    if (value == Spinning) return;
                    if (value)
                    {
                        spin = new Timer { Interval = 40 };
                        spin.Tick += delegate { angle = (angle + 12) % 360; Invalidate(); };
                        spin.Start();
                    }
                    else { spin.Dispose(); spin = null; }
                    Invalidate();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && spin != null) { spin.Dispose(); spin = null; }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = Math.Min(Width, Height) - 2, pen = Math.Max(1.5f, d / 10), x = (Width - d) / 2, y = 1;
                var ring = new RectangleF(x + pen / 2, y + pen / 2, d - pen, d - pen);
                if (spin != null)
                {
                    float wide = pen * 1.3f;
                    var r = new RectangleF(x + wide / 2, y + wide / 2, d - wide, d - wide);
                    using (var light = new Pen(Theme.Of(Role.AccentSoft), wide)) g.DrawEllipse(light, r);
                    using (var arc = new Pen(Theme.Of(Role.Accent), wide) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, r, angle - 90, 100);
                    return;
                }
                switch (state)
                {
                    case StepState.Done:
                        using (var fill = new SolidBrush(Theme.Fixed.StateOk)) g.FillEllipse(fill, x, y, d, d);
                        using (var tick = new Pen(Theme.Fixed.StateSymbol, pen * 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(tick, new[] { new PointF(x + d * 0.28f, y + d * 0.52f), new PointF(x + d * 0.44f, y + d * 0.67f), new PointF(x + d * 0.72f, y + d * 0.36f) });
                        break;
                    case StepState.Working:
                        using (var p = new Pen(Theme.Of(Role.Accent), pen)) g.DrawEllipse(p, ring);
                        using (var dot = new SolidBrush(Theme.Fixed.Orange)) g.FillEllipse(dot, x + d * 0.3f, y + d * 0.3f, d * 0.4f, d * 0.4f);
                        break;
                    case StepState.Problem:
                        using (var fill = new SolidBrush(Theme.Fixed.StateError)) g.FillEllipse(fill, x, y, d, d);
                        using (var mark = new Pen(Theme.Fixed.StateSymbol, pen * 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(mark, x + d / 2, y + d * 0.25f, x + d / 2, y + d * 0.56f);
                            g.DrawLine(mark, x + d / 2, y + d * 0.74f, x + d / 2, y + d * 0.75f);
                        }
                        break;
                    default:
                        using (var p = new Pen(Theme.Fixed.StateUnknown, pen)) g.DrawEllipse(p, ring);
                        if (state == StepState.Skipped)
                            using (var dash = new Pen(Theme.Fixed.StateUnknown, pen) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                g.DrawLine(dash, x + d * 0.32f, y + d / 2, x + d * 0.68f, y + d / 2);
                        break;
                }
            }
        }

        // The box at the top of the after-restart view: a large done mark, "Done – ..." in bold, a grey line under it; white
        // with a hairline and a petrol bar on the left (F7 test 2026-10-30: that view didn't read as finished).
        sealed class DoneBox : TableLayoutPanel
        {
            public readonly Label Line = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 0) }, Role.Dim);
            readonly Label title = new Label { AutoSize = true, Margin = Padding.Empty };

            // A little larger than the steps' titles; semibold (not Bold), so Brand.FollowFont leaves it alone and it
            // follows the window's font here when the scaling changes
            void TitleFont(Font font)
            {
                title.Font = new Font("Segoe UI Semibold", font.SizeInPoints * 1.15f, FontStyle.Regular, GraphicsUnit.Point);
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                TitleFont(Font);
            }

            public DoneBox(Font font, int em, int width)
            {
                AutoSize = true;
                ColumnCount = 2;
                Theme.Back(this, Role.Content);
                Padding = new Padding(em * 3 / 4 + Bar(em), em * 3 / 4, em, em * 3 / 4);
                Margin = new Padding(0, 0, 0, em * 5 / 4);
                MinimumSize = new Size(width, 0);
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var mark = new StepMark { State = StepState.Done, Size = new Size(em * 2, em * 2), Margin = new Padding(0, 0, em * 3 / 4, 0), Anchor = AnchorStyles.Left };
                var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
                Title = Lang.T("Done – the driver is installed.");
                TitleFont(font);
                int textWidth = width - Padding.Horizontal - mark.Width - mark.Margin.Horizontal;
                title.MaximumSize = Line.MaximumSize = new Size(textWidth, 0);
                text.Controls.Add(title);
                text.Controls.Add(Line);
                Controls.Add(mark, 0, 0);
                Controls.Add(text, 1, 0);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            public string Title { set { title.Text = value; } }

            static int Bar(int em) { return Math.Max(3, em / 4); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var line = new Pen(Theme.Of(Role.SurfaceLine))) e.Graphics.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
                using (var bar = new SolidBrush(Theme.Of(Role.Accent))) e.Graphics.FillRectangle(bar, 0, 0, Bar(Font.Height), Height);
            }
        }

        readonly string root;  // the release folder (out\package, driver\), or null
        readonly HeaderBand headline = new HeaderBand();  // the band at the top: what this window does now
        readonly Label intro = new Label { AutoSize = true };
        readonly Step check = new Step(), special = new Step(), sign = new Step(), install = new Step(), test = new Step();
        // The picture of what Windows shows next opens in its own small window from this link (inline it was too loud)
        readonly LinkLabel pictureLink = new LinkLabel { AutoSize = true, Visible = false };
        SetupPictureKind pictureKind;
        Form pictureWindow;
        bool pictureShown, reportShown;
        readonly LinkLabel inSpecial = new LinkLabel { AutoSize = true, Visible = false, Text = Lang.T("I pressed 7 already") };
        // ARM64, done in the key-7 start: restart normally soon (orange like the uninstall's share warning; F7 test 1215:
        // the grey line in the step was missed); after the test label the main button restarts
        readonly FlowLayoutPanel restartNote = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        bool restartOffered;
        readonly LinkLabel report = new LinkLabel { AutoSize = true, Visible = false, Text = Lang.T("Check the driver's signatures (report)") };
        DoneBox doneBox;
        // After the restart: the test label as a link in its step, not as the main button
        readonly LinkLabel testLink = new LinkLabel { AutoSize = true };
        readonly Panel options = new Panel { AutoSize = true };
        readonly CheckBox notify = new CheckBox { AutoSize = true, FlatStyle = FlatStyle.System, Checked = true, Text = Lang.T("Notify me about printer errors") };
        readonly CheckBox service = new CheckBox { AutoSize = true, FlatStyle = FlatStyle.System, Text = Lang.T("Printer status service for shared printers") };
        Control serviceRow;
        readonly LinkLabel netLink = new LinkLabel { AutoSize = true, Text = Lang.T("Also set up a QL printer on the network") };
        readonly FlowLayoutPanel netPanel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
        readonly TextBox address = new TextBox();
        readonly ComboBox netModel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Visible = false };
        readonly Label netResult = Theme.Fore(new Label { AutoSize = true }, Role.Dim);
        readonly Button main = new Button { AutoSize = true };
        readonly Button close = new Button { Text = Lang.T("Close"), AutoSize = true };
        // ARM64 key-7 start, finished: the second action beside the main button (Restart now before the test label;
        // Print another / Check again once Restart now is the main button). Hidden elsewhere.
        readonly Button extra = new Button { AutoSize = true, Visible = false };
        readonly LinkLabel details = new LinkLabel { AutoSize = true, Text = Lang.T("Show details"), Visible = false };
        readonly int em, wide;

        Mode mode = Mode.Checking;
        bool driverInstalled, shareOrService;
        // ARM64: afterRestart = opened by the RunOnce entry this window wrote; checksOff = this start lets self-signed
        // drivers in (the special start, or test mode); assumeSpecial = the user says so although checksOff is false.
        readonly bool afterRestart;
        bool arm, checksOff, assumeSpecial;
        int usbFound;
        string zipVersion, installedLine;  // the check step's "Installed: version x." line, updated after an install
        bool olderApps;  // AppUpdate from a zip whose programs are older than the installed ones
        // A USB QL printer whose port only another vendor's printer uses (its device on e.g. Brother's own package, which
        // install.ps1 never rebinds): our printer is added next to it on that port (-UsbPrinter). x64 PC 2026-10-28:
        // without this "Set up printer" ran the whole install and ended where it started (N6).
        // Every such printer (debug pass 2026-10-31 X1a): ports and drivers as comma lists for install.ps1, null for none
        string foreignPort, foreignDriver;
        bool foreignOnly;  // this version installed, and that printer is all there is to set up: -Settings, no signing
        string netDriver, failureDetails, logFile;
        List<string> readyPrinters = new List<string>();
        // install.ps1 runs (the administrator prompt was confirmed); closeWhenDone = the user closed the window meanwhile
        volatile bool installRunning;
        bool closeWhenDone;
        // The uninstall in this window (as in the uninstall mockup, user's OK 2026-10-31): the folder with the
        // install.ps1 it runs; the list of what goes; keep the label formats (ticked); the link from the check view.
        string uninstallScripts, tempCopy;
        bool uninstallMode, uninstallFromCheck, labelsKept;
        int labelCount;
        readonly TableLayoutPanel removeList = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Visible = false };
        readonly CheckBox keepLabels = new CheckBox { AutoSize = true, FlatStyle = FlatStyle.System, Checked = true };
        Control keepRow;
        readonly LinkLabel uninstallLink = new LinkLabel { AutoSize = true, Visible = false, Text = Lang.T("Uninstall") };
        bool? encrypted;  // the system drive has BitLocker (asked once, before the restart with key 7)
        TableLayoutPanel page, bottom;  // the content and the button row (FitWindow)
        int fitWidth;
        bool fitting;

        public InstallForm(string releaseFolder, bool afterRestart) : this(releaseFolder, afterRestart, null) { }

        // uninstallScripts: the folder with install.ps1 for an uninstall (QlLabelFormats.exe --uninstall); null = the setup
        InstallForm(string releaseFolder, bool afterRestart, string uninstallScripts)
        {
            this.uninstallScripts = uninstallScripts;
            root = FindRoot(releaseFolder);
            this.afterRestart = afterRestart;
            Text = Lang.T("QL printer driver setup");
            Theme.Use(this);
            HelpPages.F1(this, HelpPages.Install);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            Brand.TintCaption(this);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            em = Font.Height;
            wide = em * 32;

            // The band spans the window above the padded content (its row stretches to the content's width).
            var page = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty, Dock = DockStyle.Top };
            headline.Dock = DockStyle.None;
            headline.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            headline.Margin = Padding.Empty;
            SetSubtitle(null);
            page.Controls.Add(headline);
            // The window's left line (Brand.Gutter) for the band's title, the intro and the steps' marks; more whitespace
            int gutter = Brand.Gutter(em);
            var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Padding = new Padding(gutter, em * 5 / 4, gutter, 0), Margin = Padding.Empty };
            page.Controls.Add(layout);
            intro.MaximumSize = intro.MinimumSize = new Size(wide, 0);  // the full width from the start: an opened explanation doesn't widen the window
            intro.Margin = new Padding(0, 0, 0, em * 5 / 4);
            doneBox = new DoneBox(Font, em, wide) { Visible = false };
            layout.Controls.Add(doneBox);
            layout.Controls.Add(intro);
            // The uninstall's "what goes" list and the label formats question (only in that view)
            removeList.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            removeList.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            removeList.Margin = new Padding(0, 0, 0, em * 3 / 4);
            layout.Controls.Add(removeList);
            keepRow = WithInfo(keepLabels, Lang.T("The list in \"QL Label Formats\" stays on this PC; a later install takes it over."));
            keepRow.Visible = false;
            keepRow.Margin = new Padding(0, 0, 0, em);
            layout.Controls.Add(keepRow);

            var steps = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, em) };
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            steps.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int row = 0;
            foreach (var s in new[] { check, special, sign, install, test })
            {
                s.Title.Font = new Font(Font, FontStyle.Bold);
                s.Detail.MaximumSize = new Size(wide - em * 3, 0);
                // The space between steps sits above each title: a step may end with its title, its line or its links
                int gap = s == check ? 0 : em;
                s.Head.Margin = new Padding(0, gap, 0, 0);
                s.Detail.Margin = new Padding(3, em / 6, 3, 0);
                s.Links.Margin = Padding.Empty;
                s.Mark.Size = new Size(em * 4 / 3, em * 4 / 3);
                s.Mark.Margin = new Padding(0, gap, em / 2, 0);
                var step = s;
                s.Info = InfoIcon();
                InfoTip.Attach(s.Info, () => step.Tip, hover: true);
                s.Head.Controls.Add(s.Title);
                s.Head.Controls.Add(s.Info);
                steps.Controls.Add(s.Mark, 0, row);
                steps.SetRowSpan(s.Mark, 3);
                steps.Controls.Add(s.Head, 1, row++);
                steps.Controls.Add(s.Detail, 1, row++);
                steps.Controls.Add(s.Links, 1, row++);
            }
            SetupSteps();
            ShowStep(special, false);  // ARM64 only
            layout.Controls.Add(steps);
            restartNote.Margin = new Padding(0, 0, 0, em);
            restartNote.Controls.Add(Theme.Fore(new Label { Text = Lang.T("Please restart normally soon."), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = Padding.Empty }, Role.WarningText));
            restartNote.Controls.Add(Theme.Fore(new Label { Text = Lang.T("In this start Windows doesn't check driver signatures. After the restart the protection is back on, the driver stays installed and this window checks it once more."),
                                                 AutoSize = true, MaximumSize = new Size(wide, 0), Margin = Padding.Empty }, Role.WarningText));
            layout.Controls.Add(restartNote);

            // ARM64: the picture of what Windows shows next (in the step it belongs to), a way past a wrong detection,
            // the signature report (in the special start's step)
            pictureLink.LinkClicked += delegate { OpenPicture(); };
            Brand.StyleLink(pictureLink);
            inSpecial.Margin = new Padding(em * 2, 0, 0, em / 2);
            inSpecial.LinkClicked += delegate { assumeSpecial = true; RunCheck(); };
            Brand.StyleLink(inSpecial);
            layout.Controls.Add(inSpecial);
            report.LinkClicked += delegate { RunSigningReport(); };
            Brand.StyleLink(report);
            special.Links.Controls.Add(report);
            testLink.LinkClicked += delegate { PrintTest(); };
            Brand.StyleLink(testLink);
            test.Links.Controls.Add(testLink);

            // Questions, only before the install; their explanations behind an (i)
            var opt = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
            // The first sentence is the printer status window's too
            opt.Controls.Add(WithInfo(notify, Lang.T("When a QL printer stops, a Windows notification appears at the bottom right with the reason and what to do " +
                                                     "– for example cover open, roll empty or wrong roll. You can turn it off later in \"QL Printer Status\".")));
            serviceRow = WithInfo(service, Lang.T("Only needed when other PCs print to this PC's printers through a share. Then those PCs also see when a printer needs you."));
            opt.Controls.Add(serviceRow);
            opt.Controls.Add(netLink);
            netLink.Margin = new Padding(0, em / 2, 0, 0);
            netLink.LinkClicked += delegate { netPanel.Visible = !netPanel.Visible; };
            var netRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            address.Width = em * 10;
            netModel.Width = em * 9;
            netModel.Items.Add(Lang.T("(choose the model)"));
            netModel.Items.AddRange(NetworkModels);
            netModel.SelectedIndex = 0;
            var ask = new Button { Text = Lang.T("Ask the printer"), AutoSize = true };
            ask.Click += delegate { AskNetworkPrinter(); };
            netRow.Controls.AddRange(new Control[] { new Label { Text = Lang.T("IP address:"), AutoSize = true, Anchor = AnchorStyles.Left }, address, ask, netModel });
            netPanel.Controls.Add(netRow);
            netResult.MaximumSize = new Size(wide - em * 2, 0);
            netPanel.Controls.Add(netResult);
            netPanel.Controls.Add(Grey(Lang.T("The address is on the printer's network settings page or on the configuration label it prints."), 0));
            netPanel.Margin = new Padding(em * 3 / 2, 0, 0, 0);
            opt.Controls.Add(netPanel);
            options.Controls.Add(opt);
            options.Margin = new Padding(0, 0, 0, em * 3 / 4);
            layout.Controls.Add(options);

            // The buttons sit at the window's bottom edge, and the window fits its content each time it changes
            // (FitWindow; F7 test 2026-10-30: the auto-sized window didn't shrink, so the buttons floated ~110 px
            // above an empty band while the admin prompt was open).
            bottom = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Bottom, Padding = new Padding(gutter, em * 3 / 4, gutter, em * 5 / 4) };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.Anchor = AnchorStyles.Left;
            details.LinkClicked += delegate { ShowDetails(); };
            Brand.StyleLink(details);
            Brand.StyleLink(netLink);
            main.MinimumSize = new Size(em * 8, em * 2);
            main.Font = new Font(Font, FontStyle.Bold);
            Brand.StylePrimary(main);
            main.Click += delegate { MainClicked(); };
            close.MinimumSize = new Size(em * 6, em * 2);
            extra.MinimumSize = new Size(em * 6, em * 2);
            extra.Click += delegate { if (!restartOffered) ConfirmRestart(); else if (readyPrinters.Count == 0) RunCheck(); else PrintTest(); };
            // Cancel in the uninstall view opened from this window goes back to the check
            close.Click += delegate { if (mode == Mode.Uninstall && uninstallFromCheck) { uninstallMode = false; RunCheck(); } else Close(); };
            uninstallLink.Anchor = AnchorStyles.Left;
            // this.: the constructor's parameter of the same name would take it, and ShowUninstall found none (2026-11-02)
            uninstallLink.LinkClicked += delegate { uninstallFromCheck = true; this.uninstallScripts = Path.Combine(root, "driver"); ShowUninstall(); };
            Brand.StyleLink(uninstallLink);
            var leftLinks = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            leftLinks.Controls.Add(details);
            leftLinks.Controls.Add(uninstallLink);
            bottom.Controls.Add(leftLinks, 0, 0);
            bottom.Controls.Add(extra, 1, 0);
            bottom.Controls.Add(main, 2, 0);
            bottom.Controls.Add(close, 3, 0);
            Controls.Add(page);
            Controls.Add(bottom);
            this.page = page;
            // Only once the window exists: AutoScaleMode.Dpi scales it on load, which would scale a size set before
            page.Layout += delegate { if (IsHandleCreated) FitWindow(); };
            Load += delegate { headline.MinimumSize = new Size(HeadlineWidth(), 0); FitWindow(); };
            AcceptButton = main;

            // While signing or at the administrator prompt nothing runs outside the window yet: closing stops the setup. Once
            // install.ps1 runs, the window only hides and does what follows the install (notifications, the ARM64 check
            // after the restart) when it ends (debug pass 2026-10-31 I3).
            FormClosing += (s, e) =>
            {
                if (mode != Mode.Running) return;
                if (!installRunning && uninstallMode) return;  // before the administrator prompt was confirmed nothing was removed
                if (!installRunning)
                {
                    if (QlMessage.Show(this, Lang.T("The setup is still running. If you close the window now, the driver is not installed. Close anyway?"),
                                        Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                        e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                if (QlMessage.Show(this, uninstallMode ? Lang.T("The uninstall is still running. Close this window anyway? It goes on in the background.")
                                                        : Lang.T("The setup is still running. Close this window anyway? The install goes on in the background."),
                                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                closeWhenDone = true;
                if (pictureWindow != null) pictureWindow.Close();
                Hide();
            };
            Shown += delegate { if (uninstallScripts != null) ShowUninstall(); else RunCheck(); };
            FormClosed += delegate { RemoveTempCopy(); };
        }

        // Sizes the window to its content plus the button row every time the content changes: no empty band below or
        // above the buttons (at most the screen's height: the buttons stay visible, the content is cut below then).
        void FitWindow()
        {
            if (fitting || page == null || bottom == null) return;
            fitting = true;
            try
            {
                Size p = page.GetPreferredSize(Size.Empty), b = bottom.GetPreferredSize(Size.Empty);
                int limit = Screen.FromControl(this).WorkingArea.Height - (Height - ClientSize.Height);
                // The width only grows, so the buttons don't shift sideways
                fitWidth = Math.Max(fitWidth, Math.Max(p.Width, b.Width));
                var size = new Size(fitWidth, Math.Min(p.Height + b.Height, limit));
                if (ClientSize != size) ClientSize = size;
                // Grown after it was centred: moved up so the buttons stay above the taskbar
                var area = Screen.FromControl(this).WorkingArea;
                if (Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
            }
            finally { fitting = false; }
        }

        static Label Grey(string text, int indent)
        {
            return Theme.Fore(new Label { Text = text, AutoSize = true, MaximumSize = new Size(SystemFonts.MessageBoxFont.Height * 30, 0),
                                          Margin = new Padding(indent, 0, 0, 4) }, Role.Dim);
        }

        // The (i) of the printer status window; a click opens or closes the explanation under it
        Control InfoIcon()
        {
            var info = StatusForm.InfoIcon(em);
            info.Cursor = Cursors.Hand;
            info.AccessibleRole = AccessibleRole.PushButton;
            info.AccessibleName = Lang.T("Explanation");
            return info;
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
            base.OnLoad(e);
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            Brand.FollowFont(this, Font);
            if (removeList.Visible) FillRemoveList();  // its labels are made with the scaling of their time
        }

        // The band's width for its longest title in this language, with a long version number, measured with the band's
        // title font at this display's scaling (HeaderBand: left em*1.5, the corner 5/8 of its height on the right).
        // F7 test 2026-10-30: "QL-Druckertreiber auf 0.16.302.1024 aktualisier..." was cut off.
        int HeadlineWidth()
        {
            const string v = "0.99.99";
            var titles = new[]
            {
                Lang.F("Update the driver and programs to {0}", v), Lang.F("Update the programs to {0}", v), Lang.F("Go back to the older programs {0}", v),
                Lang.F("Go back to the older version {0}", v), Lang.T("Install the QL printer driver"), Lang.T("The programs are updated"),
                Lang.T("The QL printer driver is installed"), Lang.T("The QL printer driver is ready"), Lang.T("Set up the new printer"),
                Lang.T("Key 7 wasn't pressed"), Lang.T("The setup did not finish"), Lang.T("Can't start the setup here"),
                Lang.T("Uninstall the QL printer driver"), Lang.T("The QL printer driver is removed"), Lang.T("The uninstall did not finish"),
            };
            using (var font = new Font("Segoe UI Semibold", headline.Font.SizeInPoints * 1.6f, FontStyle.Regular, GraphicsUnit.Point))
            {
                int text = titles.Max(t => TextRenderer.MeasureText(t, font, Size.Empty, TextFormatFlags.NoPrefix).Width);
                int e = headline.Font.Height;  // the band's present scaling (the window may have been rescaled)
                return Brand.Gutter(e) + e / 6 + text + (int)(headline.Height * 0.625f) + e;
            }
        }

        // An option with its (i) and the app's tip
        Control WithInfo(Control c, string text)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, em / 4) };
            c.Anchor = AnchorStyles.Left;
            var info = InfoIcon();
            info.Anchor = AnchorStyles.Left;
            info.Margin = new Padding(2, 0, 0, 0);
            InfoTip.Attach(info, () => text, hover: true);
            row.Controls.Add(c);
            row.Controls.Add(info);
            return row;
        }

        // The publisher question comes only on ARM64 (x64 PC 2026-10-30: the sentence was there on x64 too)
        internal static string InstallTip(bool arm)
        {
            return arm ? Lang.T("Windows asks once for your permission; your printers, settings and label formats stay. On ARM64 PCs Windows then asks whether " +
                                "you trust the driver software: click \"Install this driver software anyway\". Afterwards you find the programs in the Start " +
                                "menu: QL Label Formats, QL Label Maker and QL Printer Status.")
                       : Lang.T("Windows asks once for your permission; your printers, settings and label formats stay. Afterwards you find the programs " +
                                "in the Start menu: QL Label Formats, QL Label Maker and QL Printer Status.");
        }

        // The band's second line: product, version (the release's, once known) and what it is.
        void SetSubtitle(string version)
        {
            headline.Subtitle = "QL Unidrv " + (version != null ? version + " " : "") + "· " + Lang.T("Driver and label programs for QL printers");
        }

        static void ShowStep(Step s, bool visible)
        {
            s.Shown = visible;
            s.Mark.Visible = s.Head.Visible = visible;
            s.Detail.Visible = visible && s.Detail.Text.Length > 0;
            if (!visible) s.Links.Visible = false;
        }

        // Offers the picture (the link, in the step it belongs to), or takes it away with its window when the step is past
        void ShowPicture(SetupPictureKind? kind)
        {
            if (kind != null)
            {
                pictureKind = kind.Value;
                var owner = kind == SetupPictureKind.StartupSettings ? special : install;
                if (pictureLink.Parent != owner.Links) { owner.Links.Controls.Add(pictureLink); owner.Links.Controls.SetChildIndex(pictureLink, 0); }
            }
            pictureShown = kind != null;
            if (pictureWindow != null && (kind == null || kind.Value != ((SetupPicture)pictureWindow.Tag).Kind)) pictureWindow.Close();
            SetPictureLinkText();
            UpdateLinks();
        }

        void ShowReport(bool on)
        {
            reportShown = on;
            UpdateLinks();
        }

        // The link opens and closes the picture (a second click hides it; not only the window's x)
        void SetPictureLinkText()
        {
            pictureLink.Text = pictureWindow != null ? Lang.T("Hide picture")
                : pictureKind == SetupPictureKind.StartupSettings ? Lang.T("What the blue screen looks like (picture)")
                : Lang.T("What Windows' question looks like (picture)");
        }

        // A small window beside this one, so both stay readable
        void OpenPicture()
        {
            if (pictureWindow != null) { pictureWindow.Close(); return; }
            // No automatic scaling in it: at 150 % that shrank the window by 1/1.5 and cut the picture off (2026-10-30)
            var w = new SetupPictureWindow(pictureKind, Font);
            w.Tag = w.Picture;
            var area = Screen.FromControl(this).WorkingArea;
            int x = Right + em / 2 + w.Width <= area.Right ? Right + em / 2 : Math.Max(area.Left, Left - em / 2 - w.Width);
            w.Location = new Point(x, Math.Max(area.Top, Math.Min(Top + em * 4, area.Bottom - w.Height)));
            w.FormClosed += delegate { pictureWindow = null; SetPictureLinkText(); };
            pictureWindow = w;
            SetPictureLinkText();
            w.Show(this);
        }

        // Any state but Working ends the busy time (Busy)
        void Set(Step s, StepState state, string detail)
        {
            s.Mark.State = state;
            Theme.Fore(s.Title, state == StepState.Skipped ? Role.Dim : Role.Text);
            if (detail != null) s.Text = detail;
            if (state != StepState.Working) s.BusySince = null;
            s.Mark.Spinning = s.BusySince != null;
            ShowDetail(s);
        }

        // The step waits for work outside the window: its ring turns and the time since then counts after the first line
        // ("Adding the driver to Windows... 0:24"). Only for real work, not while the window waits for the user's click.
        void Busy(Step s)
        {
            s.BusySince = DateTime.Now;
            s.Mark.Spinning = true;
            ShowDetail(s);
            if (busyClock == null)
            {
                busyClock = new Timer { Interval = 1000 };
                busyClock.Tick += delegate { foreach (var b in new[] { check, special, sign, install, test }) if (b.BusySince != null) ShowDetail(b); };
                busyClock.Start();
            }
        }

        Timer busyClock;

        static void ShowDetail(Step s)
        {
            string text = s.Text;
            if (s.BusySince != null)
            {
                var t = DateTime.Now - s.BusySince.Value;
                string clock = string.Format("{0}:{1:00}", (int)t.TotalMinutes, t.Seconds);
                int end = text.IndexOf('\n');
                text = end < 0 ? text + " " + clock : text.Substring(0, end) + " " + clock + text.Substring(end);
            }
            s.Detail.Text = text;
            s.Detail.Visible = s.Shown && s.Detail.Text.Length > 0;  // a step still to come shows only its title
        }

        // The step a picture belongs to shows its link; the special start's step also the signature report link
        void UpdateLinks()
        {
            foreach (var s in new[] { special, install })
                s.Links.Visible = s.Shown && s.Links.Controls.Cast<Control>().Any(c => c == report ? reportShown : c == pictureLink && pictureShown);
            test.Links.Visible = mode == Mode.Done && readyPrinters.Count > 0;
            report.Visible = reportShown;
            pictureLink.Visible = pictureShown;
        }

        // ---------- the release folder ----------

        // The folder with out\package and driver\: given, the exe's own (Setup.exe in the zip's root) or two up
        // (out\tools\QlLabelFormats.exe in the zip).
        static string FindRoot(string given)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            // the release zip: Setup.exe at the top, the rest in advanced\; the source tree; out\tools\ in either
            foreach (var dir in new[] { given, Path.Combine(exeDir, "advanced"), exeDir, Path.GetFullPath(Path.Combine(exeDir, @"..\..")) })
                if (dir != null && File.Exists(Path.Combine(dir, @"out\package\qlunidrv.inf")) && File.Exists(Path.Combine(dir, @"driver\install.ps1")))
                    return Path.GetFullPath(dir);
            return null;
        }

        // Windows' zip view runs a program from a temporary copy (Windows 11 24H2: of the whole zip) under %TEMP%:
        // ...\Temp\Temp1_name.zip\Setup.exe, or ...\Temp\<GUID>_name.zip.ba6\name\Setup.exe (x64 Sandbox 2026-10-27).
        static bool StartedFromZip() { return IsZipView(LongPath(Path.GetDirectoryName(Application.ExecutablePath)), LongPath(Path.GetTempPath())); }

        internal static bool IsZipView(string dir, string temp)
        {
            return dir.StartsWith(temp.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(dir, @"\.zip(\.[0-9a-z]{3})?(\\|$)", RegexOptions.IgnoreCase);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetLongPathNameW(string shortPath, StringBuilder longPath, int size);

        // %TEMP% can be an 8.3 path (C:\Users\WDAGUT~1\...) while the exe's path is the long one.
        static string LongPath(string path)
        {
            var sb = new StringBuilder(1024);
            int n = GetLongPathNameW(path, sb, sb.Capacity);
            return n > 0 && n < sb.Capacity ? sb.ToString() : path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFileW(string path);

        // Removes the download mark (Mark of the Web) from the extracted files, as Properties > Unblock would.
        static void Unblock(string dir)
        {
            if (Preview) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) DeleteFileW(f + ":Zone.Identifier");
            }
            catch (Exception) { }
        }

        internal static string ZipVersion(string dir)
        {
            try
            {
                var m = File.ReadAllLines(Path.Combine(dir, @"out\package\qlunidrv.inf")).Select(l => Regex.Match(l, @"^DriverVer=[^,]+,\s*([\d.]+)")).FirstOrDefault(x => x.Success);
                return m != null ? m.Groups[1].Value : null;
            }
            catch (Exception) { return null; }
        }

        // The PC's own architecture, not this process's (NativeTools)
        static bool IsArm64() { return NativeTools.IsArm64(); }

        // ---------- ARM64: the special start ----------

        [StructLayout(LayoutKind.Sequential)]
        struct CodeIntegrityInformation { public uint Length, Options; }

        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int infoClass, ref CodeIntegrityInformation info, int length, out int returned);

        // Does this start let self-signed drivers in? Asked without admin rights: the code integrity options
        // (SystemCodeIntegrityInformation = 103; normal start on the ARM64 dev PC 2026-10-30: 0xC001) and the start
        // options Windows records for this boot (Control\SystemStartOptions).
        static bool SignatureChecksOff()
        {
            uint options = 1;
            try
            {
                var info = new CodeIntegrityInformation { Length = 8 };
                int returned;
                if (NtQuerySystemInformation(103, ref info, 8, out returned) == 0) options = info.Options;
            }
            catch (Exception) { }
            string start = null;
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control")) start = k == null ? null : k.GetValue("SystemStartOptions") as string; }
            catch (Exception) { }
            return SignatureChecksOff(options, start);
        }

        // Off when code integrity is off (CODEINTEGRITY_OPTION_ENABLED 0x1 clear) or in test mode (TESTSIGN 0x2), or the
        // boot loader passed DISABLE_INTEGRITY_CHECKS (key 7) or TESTSIGNING. Which of these the special start shows
        // on ARM64 is not measured yet: any of them counts.
        internal static bool SignatureChecksOff(uint options, string startOptions)
        {
            if ((options & 0x1) == 0 || (options & 0x2) != 0) return true;
            return startOptions != null && Regex.IsMatch(startOptions, @"\b(DISABLE_INTEGRITY_CHECKS|TESTSIGNING)\b", RegexOptions.IgnoreCase);
        }

        // install.ps1 holds this while it runs (one run at a time, debug pass 2026-10-31 I4). Made by an elevated
        // process, it may refuse this one access: that also means it exists.
        static bool SetupRunning()
        {
            try
            {
                System.Threading.Mutex m;
                if (!System.Threading.Mutex.TryOpenExisting(@"Global\QLUnidrvInstall", out m)) return false;
                m.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException) { return true; }
            catch (Exception) { return false; }
        }

        // Windows settings that keep this driver out (debug pass 2026-10-31 I11): Smart App Control on (blocks programs
        // without a paid signature, the label tool and probably the driver's plug-in) and Windows protected print mode
        // (only Windows' own printer drivers). Not measured on a PC with either on: the documented registry values.
        static string BlockingPolicy()
        {
            object sac = null, wppPolicy = null, wppMode = null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy")) if (k != null) sac = k.GetValue("VerifiedAndReputablePolicyState");
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\WPP"))
                    if (k != null) { wppPolicy = k.GetValue("WindowsProtectedPrintGroupPolicyState"); wppMode = k.GetValue("WindowsProtectedPrintMode"); }
            }
            catch (Exception) { }
            return BlockingPolicy(sac, wppPolicy, wppMode);
        }

        internal static string BlockingPolicy(object sac, object wppPolicy, object wppMode)
        {
            Func<object, int> num = v => v is int ? (int)v : -1;
            if (num(sac) == 1)
                return Lang.T("Smart App Control is on. It blocks programs without a paid signature, so this driver and its programs can't run here. " +
                              "(Windows Security > App & browser control > Smart App Control; once off, Windows can turn it on again only with a reset.)");
            if (num(wppPolicy) == 1 || num(wppMode) == 1)
                return Lang.T("Windows protected print mode is on. It allows only Windows' own printer drivers, so this driver can't be installed here. " +
                              "Your organisation or you can turn it off in Settings > Bluetooth & devices > Printers & scanners.");
            return null;
        }

        // BitLocker (or device encryption) on the system drive: the restart with key 7 may ask for its recovery key.
        // Shell property System.Volume.BitLockerProtection, readable without admin rights: 1 = on, 3 = encrypting.
        static bool SystemDriveEncrypted()
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(type);
                object computer = type.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { 17 });  // This PC
                string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
                object item = computer.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, computer, new object[] { drive });
                object v = item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Volume.BitLockerProtection" });
                int state = v == null ? 0 : Convert.ToInt32(v);
                return state == 1 || state == 3;
            }
            catch (Exception) { return false; }
        }

        // Set by screenshot tools before a window is made: the views render, but nothing on the PC changes (no RunOnce,
        // Unblock, temp-file or certificate cleanup, restart). A harness run once left a RunOnce entry pointing at itself
        // (2026-10-31); setup_test checks that every such method starts with this guard.
        internal static bool Preview { get; set; }

        const string RunOnceKey =@"Software\Microsoft\Windows\CurrentVersion\RunOnce", RunOnceValue = "QL printer driver setup";

        // Opens this window once at the user's next sign-in (Windows deletes the entry before it runs it).
        void SetReopen(bool on, bool testPrinted = false)
        {
            if (Preview) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunOnceKey))
                {
                    if (on) k.SetValue(RunOnceValue, "\"" + Application.ExecutablePath + "\" --install \"" + root + "\" --after-restart" + (testPrinted ? " --test-printed" : ""));
                    else k.DeleteValue(RunOnceValue, false);
                }
            }
            catch (Exception) { }
        }

        // The open window that may hold unsaved changes: the label tool's ("QL Label Formats") or the label editor's
        // ("<label> – QL Label Maker", QlLabelMaker.exe); null when none. The monitor and status windows close themselves.
        static string LabelToolOpen()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in new[] { "QlLabelFormats", "QlLabelMaker" }.SelectMany(Process.GetProcessesByName))
            {
                try
                {
                    if (p.Id == me) continue;
                    string title = p.MainWindowTitle;
                    if (title.StartsWith(MainForm.AppTitle, StringComparison.Ordinal)) return MainForm.AppTitle;
                    if (title.EndsWith(EditorForm.AppTitle, StringComparison.Ordinal)) return EditorForm.AppTitle;
                }
                catch (Exception) { }
            }
            return null;
        }

        internal static bool IsShared(string printer)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k != null && (Convert.ToInt32(k.GetValue("Attributes", 0)) & 0x8) != 0;  // PRINTER_ATTRIBUTE_SHARED
            }
            catch (Exception) { return false; }
        }

        static bool ServiceInstalled()
        {
            try { using (var sc = new ServiceController(PublishedStatus.ServiceName)) return sc.Status >= 0; }  // throws when not installed
            catch (Exception) { return false; }
        }

        // "QL-800 on USB001: 62 mm continuous" for each Brother USB printer connected now.
        static QlModel ModelOf(DeviceResult d)
        {
            var s = d.Reply ?? d.Messages.LastOrDefault();
            return s != null ? QlModel.ForStatusCode(s.Model) : null;
        }

        static List<string> DescribeUsb(List<DeviceResult> found)
        {
            var list = new List<string>();
            foreach (var d in found)
            {
                var s = d.Reply ?? d.Messages.LastOrDefault();
                var model = s != null ? QlModel.ForStatusCode(s.Model) : null;
                string name = model != null ? model.Name : s != null ? Lang.F("{0} (not supported)", s.ModelName) : Lang.T("a Brother USB printer");
                string state = d.Printing ? Lang.T("printing right now")
                             : s == null ? (d.State == DeviceState.Failed ? Lang.T("didn't answer") : Lang.T("no answer yet"))
                             : s.HasError ? Lang.T("needs attention:") + " " + string.Join(", ", s.Problems().Select(p => p.Headline))
                             : s.NoMedia ? Lang.T("no roll loaded") : s.LoadedText;
                list.Add((d.Port != null ? Lang.F("{0} on {1}", name, d.Port) : name) + " (" + state + ")");
            }
            return list;
        }

        // ---------- check ----------

        async void RunCheck()
        {
            mode = Mode.Checking;
            uninstallMode = false;
            SetupSteps();
            uninstallLink.Visible = removeList.Visible = keepRow.Visible = restartNote.Visible = false;
            restartOffered = false;
            ShowExtra(null);
            main.Visible = false;
            close.Visible = true;
            doneBox.Visible = false;
            intro.Visible = true;
            details.Visible = false;
            options.Visible = false;
            ShowPicture(null);
            inSpecial.Visible = false;
            ShowReport(false);
            headline.Text = Lang.T("QL printer driver");
            intro.Text = Lang.T("Checking this PC...");
            Set(check, StepState.Working, Lang.T("Looking for an installed driver and connected QL printers..."));
            foreach (var s in new[] { special, sign, install, test }) Set(s, StepState.Waiting, "");  // the why is behind the (i)

            // Explorer's zip view unpacks the whole zip into a temporary folder, so the package is found there; but that
            // folder is Explorer's and may go away (Windows Sandbox test 2026-10-26).
            if (StartedFromZip())
            {
                Block(Lang.T("This window was opened from inside the zip file. Close it, right-click the zip > Extract All " +
                             "(German: Alle extrahieren), then open Setup.exe in the extracted folder."),
                      Lang.T("Opened from inside the zip: extract it first."));
                return;
            }
            if (root == null)
            {
                Block(Lang.T("The driver package is missing next to this program. Unzip the whole release (right-click the zip > Extract All; German: " +
                             "Alle extrahieren) and open Setup.exe in that folder."),
                      Lang.T("The driver package was not found next to this program."));
                return;
            }
            if (SetupRunning())
            {
                Block(Lang.T("A setup is already running on this PC (perhaps from a window you closed). Wait until it has finished, then open this window again."),
                      Lang.T("Another setup is running."));
                return;
            }
            string policy = BlockingPolicy();
            if (policy != null) { Block(policy, Lang.T("Windows blocks drivers like this one here.")); return; }
            arm = IsArm64();
            install.Tip = InstallTip(arm);
            checksOff = arm && (assumeSpecial || SignatureChecksOff());
            // Steps that don't apply are not shown (user's choice 2026-11-01): the special start only on ARM64, signing only
            // with a driver package to install
            ShowStep(special, arm);
            ShowStep(sign, true);
            string zip = ZipVersion(root), installed = Versions.InstalledDriver(), apps = Versions.InstalledApps();
            if (!File.Exists(Path.Combine(root, arm ? @"out\package\arm64\QLUnidrv.dll" : @"out\package\amd64\QLUnidrv.dll")))
            {
                Block(Lang.F("This download has no driver for this PC ({0}). Download the release for {0} PCs (or the one for both).", arm ? "ARM64" : "x64"),
                      Lang.T("This download has no driver for this PC."));
                return;
            }
            var found = await Task.Run(() =>
            {
                Unblock(root);
                try { return QlDevice.ScanUsb(); } catch (Exception) { return new List<DeviceResult>(); }
            });
            driverInstalled = SetupForm.Drivers().Length > 0;
            var ours = MainForm.OurPrinters();
            Func<string, string, bool> onPort = (printer, port) => string.Equals(QlDevice.PortOf(printer), port, StringComparison.OrdinalIgnoreCase);
            var others = PrinterSettings.InstalledPrinters.Cast<string>().Where(p => !ours.Contains(p)).ToList();
            var foreign = found.Where(d => d.Port != null && !ours.Any(p => onPort(p, d.Port)) && others.Any(p => onPort(p, d.Port)))
                               .Select(d => new { d.Port, Model = ModelOf(d) }).Where(x => x.Model != null).ToList();
            foreignPort = foreign.Count > 0 ? string.Join(",", foreign.Select(x => x.Port)) : null;
            foreignDriver = foreign.Count > 0 ? string.Join(",", foreign.Select(x => x.Model.DriverName)) : null;
            shareOrService = ServiceInstalled() || ours.Any(IsShared);
            usbFound = found.Count;
            var usb = DescribeUsb(found);

            // One line: what is installed and the USB printers (the download's version is in the header)
            var lines = new List<string>();
            // The driver by its release (driver\VERSION), the programs by their build: this exe is the zip's programs
            int cmp = installed == null ? 1 : Versions.CompareRelease(zip, installed), appCmp = Versions.CompareApps(Versions.Product, apps);
            if (!driverInstalled) mode = Mode.Install;
            else mode = cmp > 0 ? Mode.Update : cmp < 0 ? Mode.Older : appCmp != 0 ? Mode.AppUpdate : Mode.Repair;
            olderApps = appCmp < 0;
            string zipDriver = Versions.Release(zip), driver = Versions.Release(installed) ?? Lang.T("unknown");
            zipVersion = Versions.App;
            SetSubtitle(Versions.App);
            installedLine = !driverInstalled ? null : apps != null ? Lang.F("Installed: programs {0}, driver {1}", Versions.AppRelease(apps), driver) : Lang.F("Installed: {0}", driver);
            if (installedLine != null) lines.Add(installedLine);
            else if (File.Exists(InstalledExe)) lines.Add(Lang.T("Installed: only the programs."));
            lines.Add(usb.Count == 0 ? Lang.T("No QL printer on USB.") : string.Join(" · ", usb));
            Set(check, StepState.Done, string.Join(" · ", lines));  // no USB printer is fine (network, share): no warning

            // This version installed already: a USB printer without a Windows printer is set up ("Set up printer"), else
            // there is nothing to do and Repair is only offered (x64 Sandbox 2026-10-27: "Repair" right after a good
            // install read like an error).
            var unset = found.Where(d => d.Port == null || !ours.Any(p => onPort(p, d.Port))).ToList();
            bool newUsb = mode == Mode.Repair && unset.Count > 0;
            foreignOnly = newUsb && foreignPort != null && unset.All(d => d.Port != null && foreign.Any(x => x.Port == d.Port));
            headline.Text = mode == Mode.Install ? Lang.T("Install the QL printer driver") : mode == Mode.Update ? Lang.F("Update the driver and programs to {0}", Versions.App)
                          : mode == Mode.AppUpdate ? (olderApps ? Lang.F("Go back to the older programs {0}", Versions.App) : Lang.F("Update the programs to {0}", Versions.App))
                          : mode == Mode.Repair ? (newUsb ? Lang.T("Set up the new printer") : Lang.T("The QL printer driver is installed"))
                          : Lang.F("Go back to the older version {0}", zipDriver);
            intro.Text = mode == Mode.Install ? Lang.T("Installs the driver for QL printers and the label programs.")
                : mode == Mode.Older ? Lang.F("This download is older than the installed driver ({0}).", driver)
                : mode == Mode.AppUpdate ? Lang.F("Only the programs are updated; the driver {0} stays.", driver)
                : mode == Mode.Update ? Lang.F("The driver is updated from {0} to {1}, the programs too. Your printers, settings and label formats stay.", driver, zipDriver)
                : mode == Mode.Repair && newUsb ? Lang.T("A QL printer on USB isn't set up yet: Set up printer adds it.")
                : mode == Mode.Repair ? Lang.T("This version is installed already. If something doesn't print, Repair installs it again.")
                : Lang.T("Your printers, settings and label formats stay.");
            notify.Checked = mode == Mode.Install || TrayMonitor.AutostartEnabled;
            service.Checked = ServiceInstalled();
            serviceRow.Visible = shareOrService;
            options.Visible = options.Enabled = true;   // locked while an install ran; Check again after it (x64 Sandbox 2026-11-01 F5)
            main.Text = mode == Mode.Install ? Lang.T("Install") : mode == Mode.Update || mode == Mode.AppUpdate ? Lang.T("Update")
                      : mode == Mode.Repair ? (newUsb ? Lang.T("Set up printer") : Lang.T("Repair")) : Lang.T("Install older version");
            main.Visible = true;
            uninstallLink.Visible = driverInstalled || File.Exists(InstalledExe);
            bool nothingToDo = mode == Mode.Repair && !newUsb;
            if (nothingToDo)  // the install-time texts with "○" read as "not done yet" (x64 Sandbox retest 3)
            {
                Set(sign, StepState.Done, Lang.T("Signed."));
                Set(install, StepState.Done, Lang.F("Installed ({0}).", driver));
                Set(test, StepState.Skipped, usbFound == 0 ? Lang.T("Test label: connect the printer and switch it on, then open this window again.")
                                                           : Lang.T("Test label: \"QL Label Formats\" > Test print (F5)."));
            }
            install.Title.Text = mode == Mode.AppUpdate ? Lang.T("Update the programs") : Lang.T("Install the driver and the programs");
            if (mode == Mode.AppUpdate || foreignOnly) ShowStep(sign, false);
            AcceptButton = nothingToDo ? close : main;
            (nothingToDo ? close : main).Focus();
            if (!arm) return;

            // ARM64: in the special start the install runs as on x64; outside it only what needs no driver package
            if (mode == Mode.AppUpdate)
                ShowStep(special, false);
            else if (checksOff)
                Set(special, StepState.Done, Lang.T("Started with key 7: Windows can install the driver now."));
            else if (afterRestart && mode == Mode.Repair)
            {
                // Opened once more after the normal restart that ended the special start: the check (installed, checks on)
                Set(sign, StepState.Done, Lang.T("Signed."));
                Set(install, StepState.Done, Lang.F("Installed ({0}).", driver));
                readyPrinters = MainForm.OurPrinters().Where(p => QlDevice.IsLocalStatusPort(QlDevice.PortOf(p))).ToList();
                ShowDone(TestPrintedBeforeRestart());
            }
            else if (nothingToDo || foreignOnly)
                ShowStep(special, false);
            else
                ShowPrepare(afterRestart);
        }

        // After the normal restart that ended the special start: clearly finished (the done box, main button Done, which
        // closes the window). The test label is done when it was printed before the restart, else only offered as a link.
        void ShowDone(bool testPrinted)
        {
            mode = Mode.Done;
            options.Visible = false;
            headline.Text = Lang.T("The QL printer driver is installed");
            doneBox.Line.Text = Lang.T("Windows checks driver signatures as usual again.");
            doneBox.Visible = true;
            intro.Visible = false;
            Set(special, StepState.Done, Lang.T("Started normally again."));
            ShowPicture(null);
            inSpecial.Visible = false;
            ShowReport(true);
            if (readyPrinters.Count == 0)
                Set(test, StepState.Skipped, usbFound == 0 ? Lang.T("Connect the printer, then open this window again – setting it up needs another restart with key 7.")
                                                           : Lang.T("Test label: \"QL Label Formats\" > Test print (F5)."));
            else if (testPrinted)
            {
                Set(test, StepState.Done, Lang.T("Printed before the restart."));
                SetTestLink(null, Lang.T("Print another"));
            }
            else
            {
                Set(test, StepState.Waiting, "");
                SetTestLink(Lang.T("If you like:"), Lang.T("Print test label"));
            }
            UpdateLinks();
            main.Text = Lang.T("Done");
            main.Visible = true;
            close.Visible = false;  // Done does the same
            AcceptButton = main;
            main.Focus();
        }

        // "If you like: Print test label" with only the second part as the link
        void SetTestLink(string lead, string link)
        {
            testLink.Text = lead != null ? lead + " " + link : link;
            testLink.LinkArea = new LinkArea(testLink.Text.Length - link.Length, link.Length);
        }

        // The RunOnce entry written after a test label in the special start carries --test-printed (nothing is left
        // behind: Windows deletes the entry when it runs it)
        static bool TestPrintedBeforeRestart()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "--test-printed") >= 0;
        }

        // ARM64 outside the special start: what will happen, the picture of the blue screen, "Prepare and restart".
        // missed = opened after the prepared restart, but Windows started normally (7 not pressed).
        void ShowPrepare(bool missed)
        {
            mode = Mode.Prepare;
            ShowStep(special, true);  // hidden while there was nothing to do (Repair asks for the special start, too)
            options.Visible = false;
            if (missed) headline.Text = Lang.T("Key 7 wasn't pressed");
            intro.Text = missed ? Lang.T("Windows started without the blue screen. Choose Restart (not Shut down), then press 7 on the blue screen.")
                                : Lang.T("On ARM64 PCs Windows takes this driver only after a restart with key 7.");
            if (encrypted == null) encrypted = SystemDriveEncrypted();
            Set(special, missed ? StepState.Problem : StepState.Working, Lang.T("Click Prepare and restart, then press 7 on the blue screen.") +
                (encrypted.Value ? "\n" + Lang.T("This PC is encrypted (BitLocker): if Windows asks for the recovery key after the restart, you find it at aka.ms/myrecoverykey.") : ""));
            ShowPicture(SetupPictureKind.StartupSettings);
            inSpecial.Visible = true;
            main.Text = missed ? Lang.T("Try again") : Lang.T("Prepare and restart");
            main.Enabled = true;
            main.Visible = true;
            AcceptButton = main;
            main.Focus();
        }

        // bcdedit /set {current} onetimeadvancedoptions on (elevated, cmd: PowerShell would misread {current}): Windows
        // shows the Startup Settings once at the next start. Then this window is set to reopen and offers the restart.
        async void PrepareSpecialStart()
        {
            main.Enabled = false;
            inSpecial.Visible = false;
            // The prompt names the program that asks (bcdedit runs in cmd.exe), not this setup
            Set(special, StepState.Working, Lang.T("Windows asks about \"Windows Command Processor\": click Yes."));
            Busy(special);
            var psi = new ProcessStartInfo(NativeTools.Path("cmd.exe", elevated: true), "/c bcdedit /set {current} onetimeadvancedoptions on")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            int code;
            try { code = await Task.Run(() => { using (var p = Process.Start(psi)) { p.WaitForExit(); return p.ExitCode; } }); }
            catch (Win32Exception ex)
            {
                ShowPrepare(false);
                Set(special, StepState.Problem, ex.NativeErrorCode == 1223
                    ? Lang.T("Declined, nothing changed. To try again: Prepare and restart.")
                    : Lang.T("Could not prepare the restart with key 7:") + " " + ex.Message);
                return;
            }
            if (code != 0)
            {
                ShowPrepare(false);
                Set(special, StepState.Problem, Lang.F("Windows couldn't prepare the restart with key 7 (bcdedit ended with code {0}). Nothing was changed. Click Show details for the command.", code));
                failureDetails = "cmd.exe /c bcdedit /set {current} onetimeadvancedoptions on\r\nexit code " + code;
                logFile = null;
                details.Visible = true;
                return;
            }
            SetReopen(true);
            mode = Mode.Restart;
            Set(special, StepState.Working, Lang.T("Prepared. Restart now and press 7 on the blue screen."));
            main.Text = Lang.T("Restart now");
            main.Enabled = true;
            main.Focus();
            if (QlMessage.Show(this, Lang.T("Restart the PC now? Save your work and close other programs first.") + "\n\n" +
                                      Lang.T("After the restart: press 7 on the blue screen \"Startup Settings\".") +
                                      (encrypted == true ? "\n\n" + Lang.T("This PC is encrypted (BitLocker): if Windows asks for the recovery key after the restart, you find it at aka.ms/myrecoverykey.") : ""),
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Restart();
        }

        void Restart()
        {
            if (Preview) return;
            try
            {
                Process.Start(new ProcessStartInfo(NativeTools.Path("shutdown.exe"), "/r /t 0") { UseShellExecute = false, CreateNoWindow = true });
                Close();
            }
            catch (Exception ex)
            {
                Set(special, StepState.Problem, Lang.T("Could not restart:") + " " + ex.Message + " " + Lang.T("Restart from the Start menu (Power > Restart; German: Ein/Aus > Neu starten)."));
            }
        }

        // driver\check-signing.ps1 (elevated, read-only): the signing state report, opened in Notepad.
        async void RunSigningReport()
        {
            string script = Path.Combine(root, @"driver\check-signing.ps1"), dir = Path.Combine(root, @"out\check-signing");
            if (!File.Exists(script)) return;
            var started = DateTime.Now.AddSeconds(-5);
            var psi = new ProcessStartInfo(NativeTools.Path("powershell.exe", elevated: true), "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root };
            report.Enabled = false;
            try { await Task.Run(() => { using (var p = Process.Start(psi)) p.WaitForExit(); }); }
            catch (Win32Exception) { report.Enabled = true; return; }  // declined
            report.Enabled = true;
            var file = Directory.Exists(dir) ? new DirectoryInfo(dir).GetFiles("check-*.txt").Where(f => f.LastWriteTime >= started)
                                                                     .OrderByDescending(f => f.LastWriteTime).FirstOrDefault() : null;
            if (file != null) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + file.FullName + "\"") { UseShellExecute = true });
        }

        // why = the explanation under the headline, step = the short version in the check step (not the same sentence twice).
        void Block(string why, string step)
        {
            mode = Mode.Blocked;
            headline.Text = Lang.T("Can't start the setup here");
            intro.Text = why;
            Set(check, StepState.Problem, step);
            foreach (var s in new[] { special, sign, install, test }) Set(s, StepState.Skipped, null);
        }

        // ---------- network printer ----------

        async void AskNetworkPrinter()
        {
            string host = address.Text.Trim();
            netDriver = null;
            if (!Regex.IsMatch(host, @"^[A-Za-z0-9][A-Za-z0-9.:-]*$")) { netResult.Text = Lang.T("Enter the printer's IP address (e.g. 192.0.2.50)."); return; }
            netResult.Text = Lang.F("Asking the printer at {0}...", host);
            try
            {
                var found = await Task.Run(() => NetworkStatus.Identify(host));
                netDriver = found[0];
                netModel.Visible = false;
                netResult.Text = Lang.F("Found: {0} at {1} ({2} loaded). It is set up with the install.", found[1], host, found[2]);
            }
            catch (Exception ex)
            {
                netModel.Visible = true;
                netResult.Text = ex.Message + " " + Lang.T("If the address is right, choose the model; it is set up with the install (the printer status works once it answers).");
            }
        }

        // "-NetworkPrinter <address> -DriverName ..." for install.ps1, "" for none, or null after telling what is missing.
        string NetworkArguments()
        {
            string host = address.Text.Trim();
            if (!netPanel.Visible || host.Length == 0) return "";
            if (!Regex.IsMatch(host, @"^[A-Za-z0-9][A-Za-z0-9.:-]*$")) { netResult.Text = Lang.T("Enter the printer's IP address (e.g. 192.0.2.50), or clear the field."); return null; }
            string driver = netDriver;
            if (driver == null && netModel.Visible && netModel.SelectedIndex > 0) driver = "Brother " + netModel.SelectedItem + " (Unidrv)";
            if (driver == null) { netResult.Text = netModel.Visible ? Lang.T("Click \"Ask the printer\" first, or choose the model.") : Lang.T("Click \"Ask the printer\" first."); return null; }
            return " -NetworkPrinter " + host + " -DriverName \"" + driver + "\"";
        }

        // ---------- install ----------

        void MainClicked()
        {
            switch (mode)
            {
                case Mode.Prepare:
                    PrepareSpecialStart();
                    break;
                case Mode.Restart:
                    Restart();
                    break;
                case Mode.AppUpdate:
                    RunInstall();
                    break;
                case Mode.Install: case Mode.Update: case Mode.Repair: case Mode.Older:
                    if (arm && !checksOff && !foreignOnly) ShowPrepare(false);  // Repair on ARM64 needs the special start too
                    else RunInstall();
                    break;
                case Mode.Failed:
                    if (uninstallMode) ShowUninstall(); else RunCheck();
                    break;
                case Mode.Uninstall:
                    RunUninstall();
                    break;
                case Mode.Removed:
                    Close();
                    break;
                case Mode.Finished:
                    if (restartOffered) ConfirmRestart();
                    else if (readyPrinters.Count == 0) RunCheck();
                    else PrintTest();
                    break;
                case Mode.Done:
                    Close();
                    break;
            }
        }

        async void RunInstall()
        {
            string net = NetworkArguments();
            if (net == null) { netPanel.Visible = true; return; }
            for (string open; (open = LabelToolOpen()) != null; )
            {
                if (QlMessage.Show(this, Lang.F("\"{0}\" is open. Save your changes and close it, then click OK.", open), Text,
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return;
            }
            bool wasUpdate = driverInstalled, appOnly = mode == Mode.AppUpdate;
            mode = Mode.Running;
            inSpecial.Visible = false;
            uninstallLink.Visible = false;
            options.Enabled = false;
            main.Visible = false;
            details.Visible = false;
            close.Text = Lang.T("Close");

            // -UsbDriverName: -DriverName may be the network printer's in the same run
            string usbArguments = foreignPort != null ? " -UsbPrinter -UsbDriverName \"" + foreignDriver + "\" -UsbPort " + foreignPort : "";
            bool settingsOnly = foreignOnly;  // only our printer next to the other vendor's: no driver install, no signing

            // 1. Sign (unelevated)
            if (!appOnly && !settingsOnly)
            {
                Set(sign, StepState.Working, Lang.T("Making a certificate for this PC and signing the driver..."));
                Busy(sign);
                var signing = await Task.Run(() => RunHidden(NativeTools.Path("powershell.exe"), "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, @"driver\package.ps1") +
                                                             "\" -SignOnly -OneTimeKeys"));
                if (signing.Item1 != 0)
                {
                    Fail(sign, Lang.T("Signing failed:") + " " + ErrorMessage(signing.Item2, signing.Item3), (signing.Item2 + "\r\n" + signing.Item3).Trim());
                    return;
                }
                Set(sign, StepState.Done, Lang.T("Signed."));
            }

            // 2. install.ps1 as administrator, its steps shown while it runs
            string temp = Path.GetTempPath(), resultFile = Path.Combine(temp, "ql-unidrv-setup.txt");
            logFile = Path.Combine(temp, "ql-unidrv-install.log");
            foreach (var f in new[] { logFile, resultFile }) try { File.Delete(f); } catch (Exception) { }
            string arguments = appOnly ? " -AppOnly" : settingsOnly ? " -Settings" + usbArguments + net
                : (wasUpdate ? " -Update" : "") + (shareOrService ? " -StatusService " + (service.Checked ? "on" : "off") : "") + net + usbArguments;
            var psi = new ProcessStartInfo(NativeTools.Path("powershell.exe", elevated: true), "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, @"driver\install.ps1") + "\"" + arguments +
                                           " -Log \"" + logFile + "\" -ResultFile \"" + resultFile + "\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = root };
            Set(install, StepState.Working, Lang.T("Windows asks about \"Windows PowerShell\": click Yes."));
            Busy(install);
            // ARM64 (special start, a driver package to install): Windows asks once more, about the driver's publisher
            bool publisherPrompt = arm && !settingsOnly && !appOnly;
            packageKnown = false;
            if (publisherPrompt)
            {
                Set(install, StepState.Working, install.Text + "\n" + PublisherHint);
                ShowPicture(SetupPictureKind.InstallAnyway);
            }
            var timer = new Timer { Interval = 400 };
            timer.Tick += delegate
            {
                var lines = ReadResult(resultFile);
                string step = lines.Where(kv => kv.Key == "step").Select(kv => kv.Value).LastOrDefault();
                // The package was in Windows' driver store already (an earlier attempt): no publisher question comes
                if (publisherPrompt && lines.Any(kv => kv.Key == "package" && kv.Value == "known")) { publisherPrompt = false; packageKnown = true; ShowPicture(null); }
                string text = step != null ? StepText(step, appOnly) : null;
                if (text != null) Set(install, StepState.Working, publisherPrompt && step == "package" ? text + "\n" + PublisherHint
                                                                 : packageKnown ? text + "\n" + PackageKnownText : text);
                if (publisherPrompt && step != null && step != "certificates" && step != "package") ShowPicture(null);
            };
            int code;
            try
            {
                timer.Start();
                code = await Task.Run(() => { using (var p = Process.Start(psi)) { installRunning = true; p.WaitForExit(); return p.ExitCode; } });
            }
            catch (Win32Exception ex)
            {
                timer.Dispose();
                installRunning = false;
                ShowPicture(null);
                Fail(install, ex.NativeErrorCode == 1223 ? Lang.T("Declined, nothing installed. To try again: Try again.")
                                                         : Lang.T("Could not start the install:") + " " + ex.Message, null);
                return;
            }
            timer.Dispose();
            installRunning = false;
            ShowPicture(null);
            var result = ReadResult(resultFile).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            string done, error, errorCode, failedStep;
            result.TryGetValue("done", out done);
            if (closeWhenDone)  // closed while it ran: what follows a good install, then the window really closes
            {
                if (code == 0 && done == "1")
                {
                    if (File.Exists(InstalledExe) && (notify.Checked || TrayMonitor.AutostartEnabled))
                        try { TrayMonitor.SetAutostart(notify.Checked, InstalledExe); } catch (Exception) { }
                    if (arm && checksOff) SetReopen(true);
                    DeleteRunFiles(resultFile);
                }
                mode = Mode.Finished;
                Close();
                return;
            }
            if (code != 0 || done != "1")
            {
                result.TryGetValue("error", out error);
                result.TryGetValue("code", out errorCode);
                result.TryGetValue("step", out failedStep);
                // ARM64: a refused package in a start that checks signatures = not the special start (wrong detection or
                // "I pressed 7" when it wasn't): Try again then shows the preparation
                if (arm && errorCode == "pnputil" && !SignatureChecksOff()) errorCode = "enforcement-on";
                if (errorCode == "enforcement-on") assumeSpecial = false;
                // PowerShell's transcript writes a script's throw as: PS>TerminatingError(): "message"
                var body = SetupForm.TranscriptBody(logFile).Select(l => Regex.Replace(l, "^PS>TerminatingError\\(\\w*\\): \"(.*)\"$", "$1")).ToList();
                Fail(install, Explain(errorCode, error ?? body.LastOrDefault(l => !l.StartsWith(" ") && !l.StartsWith("+")) ?? Lang.T("It stopped without a message."), failedStep),
                     string.Join("\r\n", body.Skip(Math.Max(0, body.Count - 40))) + "\r\n\r\nLog: " + logFile, errorCode == "rdp-printer");
                return;
            }

            // 3. The printer monitor for the signed-in user (this window is not elevated)
            string monitorNote = "";
            if (File.Exists(InstalledExe) && (notify.Checked || TrayMonitor.AutostartEnabled))
            {
                try { TrayMonitor.SetAutostart(notify.Checked, InstalledExe); }
                catch (Exception ex) { monitorNote = " " + Lang.F("Printer notifications could not be turned on ({0}): use QL Printer Status.", ex.Message); }
            }
            if (installedLine != null && zipVersion != null)  // the check step said the old version (x64 test 2026-10-28)
                Set(check, StepState.Done, check.Text.Replace(installedLine, Lang.F("Installed: programs {0}, driver {1}", Versions.App,
                                                                                    Versions.Release(Versions.InstalledDriver()) ?? Lang.T("unknown"))));
            readyPrinters = MainForm.OurPrinters().Where(p => QlDevice.IsLocalStatusPort(QlDevice.PortOf(p))).ToList();
            Set(install, StepState.Done, (wasUpdate ? Lang.T("Updated. In the Start menu: QL Label Formats, QL Label Maker and QL Printer Status.")
                                                     : Lang.T("Installed. In the Start menu: QL Label Formats, QL Label Maker and QL Printer Status.")) + monitorNote +
                                         (packageKnown ? "\n" + PackageKnownText : ""));
            DeleteRunFiles(resultFile);
            Finish(appOnly);
        }

        // The log and result file in %TEMP% after a run that finished; a failed run keeps them (Show details names the
        // log). Debug pass 2026-10-31 X7.
        void DeleteRunFiles(string resultFile)
        {
            if (Preview) return;
            foreach (var f in new[] { logFile, resultFile }) try { if (f != null) File.Delete(f); } catch (Exception) { }
            SetupForm.DeleteRunFiles();
            logFile = null;
        }

        static string PublisherHint
        {
            get
            {
                return Lang.T("When Windows asks about the publisher: \"Install this driver software anyway\".");
            }
        }

        // Instead of the publisher hint when Windows had the package already (install.ps1: package=known)
        static string PackageKnownText { get { return Lang.T("Windows already knew the driver package, so it didn't ask about the publisher."); } }
        bool packageKnown;

        void Finish(bool appOnly = false)
        {
            mode = Mode.Finished;
            options.Visible = false;
            options.Enabled = true;   // locked while the install ran (x64 Sandbox 0.17: still disabled in the done view)
            // The next normal start ends the special start; the window opens then to check. Also after an app-only update
            // run in that start (it offers the restart after the test label too; ARM 0.18.1 update 2026-11-02)
            if (arm && checksOff)
            {
                SetReopen(true);
                ShowStep(special, true);  // hidden for an app-only update
                Set(special, StepState.Done, Lang.T("Started with key 7."));
                restartNote.Visible = true;
            }
            headline.Text = appOnly ? Lang.T("The programs are updated") : Lang.T("The QL printer driver is ready");
            bool key7 = arm && checksOff;
            if (readyPrinters.Count > 0)
            {
                intro.Text = Lang.F("Set up: {0}.", string.Join(", ", readyPrinters));
                Set(test, StepState.Working, Lang.T("Click Print test label."));
                main.Text = Lang.T("Print test label");
                if (key7) ShowExtra(Lang.T("Restart now"));  // [Print test label] [Restart now] [Later] (user's choice 2026-11-02)
            }
            else
            {
                intro.Text = usbFound == 0 ? Lang.T("No QL printer on USB, so none was set up.")
                                           : Lang.T("A QL printer was found on USB, but no printer was set up for it yet.");
                // ARM64 in the key-7 start: a printer connected after the normal restart needs another such restart (debug pass I9)
                Set(test, StepState.Skipped, usbFound > 0 ? Lang.T("Click Check again, then Set up printer.")
                                           : arm && checksOff ? Lang.T("Connect the printer now and click Check again – later, setting it up needs another restart with key 7.")
                                           : Lang.T("Test label: connect the printer and switch it on, then click Check again."));
                main.Text = Lang.T("Check again");
            }
            main.Visible = true;
            close.Text = key7 ? Lang.T("Later") : Lang.T("Close");
            AcceptButton = main;
            main.Focus();
            if (key7 && readyPrinters.Count == 0) OfferRestart(Lang.T("Check again"));  // [Check again] [Restart now] [Later]
        }

        void PrintTest()
        {
            string printer = readyPrinters[0];
            if (readyPrinters.Count > 1)
            {
                using (var pick = new Form { Text = Text, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
                                             AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, StartPosition = FormStartPosition.CenterParent, Font = Font })
                {
                    Theme.Use(pick);
                    var list = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = em * 18 };
                    list.Items.AddRange(readyPrinters.ToArray());
                    list.SelectedIndex = 0;
                    var ok = new Button { Text = Lang.T("Next"), DialogResult = DialogResult.OK, AutoSize = true };
                    var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(em) };
                    panel.Controls.AddRange(new Control[] { new Label { Text = Lang.T("Which printer?"), AutoSize = true }, list, ok });
                    pick.Controls.Add(panel);
                    pick.AcceptButton = ok;
                    if (pick.ShowDialog(this) != DialogResult.OK) return;
                    printer = (string)list.SelectedItem;
                }
            }
            string what = TestLabel.Print(this, Text, printer);
            if (what == null) return;
            bool sent = TestLabel.WasSent(what);
            Set(test, sent ? StepState.Done : StepState.Problem, what + (!sent ? "" : " " + (arm && checksOff ? Lang.T("Did it come out right?") : Lang.T("Did it come out right? Then you're done."))));
            if (mode == Mode.Done)  // the link stays the way to print; Done stays the main button
            {
                SetTestLink(null, sent ? Lang.T("Print another") : Lang.T("Try again"));
                return;
            }
            if (sent && arm && checksOff)
            {
                SetReopen(true, testPrinted: true);  // the view after the normal restart shows it done
                OfferRestart(Lang.T("Print another"));
                return;
            }
            main.Text = sent ? Lang.T("Print another") : Lang.T("Try again");
            if (sent) AcceptButton = close;
        }

        // In the key-7 start after the test label (or with no printer to test): Restart now becomes the main button (a
        // normal start; the window opens again then); first the other action (Print another / Check again), then Later
        void OfferRestart(string other)
        {
            restartOffered = true;
            ShowExtra(other);
            main.Text = Lang.T("Restart now");
            close.Text = Lang.T("Later");
            AcceptButton = main;
            main.Focus();
        }

        // The second button left of the main one before the test label ([Print test label] [Restart now]), right of it once
        // Restart now is the main button ([Print another] [Restart now]); null hides it. The main button keeps its colour.
        void ShowExtra(string text)
        {
            extra.Visible = text != null;
            if (text != null) extra.Text = text;
            bool first = restartOffered;
            bottom.SetColumn(extra, first ? 1 : 2);
            bottom.SetColumn(main, first ? 2 : 1);
        }

        // A normal restart (shutdown /r, no /f) after a question: other programs are closed
        void ConfirmRestart()
        {
            using (var q = new SetupResult(Text, ResultKind.Question, Lang.T("Restart now?"), null,
                                           Lang.T("Save your work in other programs first; they will be closed."), null, Lang.T("Cancel")))
            {
                q.AddChoice(Lang.T("Restart now"), DialogResult.OK);
                if (q.ShowDialog(this) != DialogResult.OK) return;
            }
            Restart();
        }

        // Plain words for install.ps1's code= classes; for other failures the step it stopped in (its last step= line),
        // then Windows' own message (often in Windows' language).
        internal static string Explain(string code, string message, string step = null)
        {
            string where = step == "certificates" ? Lang.T("Windows didn't let the setup add the driver's certificate to this PC.")
                         : step == "package" ? Lang.T("Windows didn't accept the driver package.")
                         : step == "printers" ? Lang.T("Setting up the connected printers failed.")
                         : step == "app" ? Lang.T("Installing the programs failed.")
                         : step == "labels" ? Lang.T("Creating the label formats failed.") : null;
            if (where != null && (code == null || code == "other")) return Lang.F("{0} Windows reported: {1}", where, message);
            switch (code)
            {
                case "label-tool-open": return Lang.T("QL Label Formats or QL Label Maker was open. Close it, then click Try again.");
                case "busy": return Lang.T("Another setup is running on this PC. Wait until it has finished, then click Try again.");
                case "enforcement-on": return Lang.T("Refused: Windows was started without key 7. Try again leads through the restart.");
                case "pnputil": return Lang.T("Windows refused the driver package. Try again; if it fails again, click Show details and send them to the project (see README).");
                case "port": return Lang.T("Windows didn't let the setup add a printer port. Try again; if it still doesn't work, click Show details and send them to the project (see README).");
                case "rdp-printer": return Lang.T("A Remote Desktop session is redirecting printers that use this driver. Close it, or connect without printers (Remote Desktop Connection > Local Resources > Printers), then try again.");
                case "no-package": return Lang.T("This download has no driver for this PC. Download the release for this PC.");
                case "not-admin": return Lang.T("Administrator permission is needed for the install.");
                case "no-printer": return Lang.T("No QL printer found on USB. Connect it and switch it on (QL-700/800: Editor Lite LED off), then click Try again.");
                // install.ps1's own messages are English: in another language only under Show details (debug pass I14)
                default: return Lang.German ? Lang.T("The install did not finish. Show details says why (in English).") : Lang.F("The install did not finish: {0}", message);
            }
        }

        // nothingChanged: stopped before anything was touched (code=rdp-printer)
        void Fail(Step s, string why, string log, bool nothingChanged = false)
        {
            mode = Mode.Failed;
            failureDetails = log;
            Set(s, StepState.Problem, why);
            headline.Text = uninstallMode ? Lang.T("The uninstall did not finish") : Lang.T("The setup did not finish");
            intro.Text = nothingChanged ? Lang.T("Nothing was changed. You can try again.")
                       : uninstallMode ? Lang.T("Not everything was removed. You can try again.") : Lang.T("Nothing else was changed after this step. You can try again.");
            intro.Visible = true;
            options.Enabled = true;
            details.Visible = !string.IsNullOrEmpty(log);
            main.Text = Lang.T("Try again");
            main.Visible = true;
            AcceptButton = main;
            main.Focus();  // Try again, not Close (x64 test 2026-10-28: after declining UAC)
        }

        // The setup's last output lines, shown at once (the result dialog would hide them behind a second "Show details").
        void ShowDetails()
        {
            using (var d = new Form { Text = Lang.T("What the setup reported"), Font = Font, ShowInTaskbar = false, MinimizeBox = false,
                                      StartPosition = FormStartPosition.CenterParent, Size = new Size(em * 46, em * 26) })
            {
                Theme.Use(d);
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
                                        Font = new Font(FontFamily.GenericMonospace, Font.Size * 0.9f), Text = (failureDetails ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(em / 2) };
                var closeButton = new Button { Text = Lang.T("Close"), AutoSize = true, DialogResult = DialogResult.Cancel };
                var copy = new Button { Text = Lang.T("Copy details"), AutoSize = true };
                var note = Theme.Fore(new Label { AutoSize = true, Anchor = AnchorStyles.Left,
                                                  Text = logFile != null ? Lang.T("Log") + ": " + logFile : "" }, Role.Dim);
                copy.Click += delegate
                {
                    try { Clipboard.SetText(box.Text); note.Text = Lang.T("Details copied."); } catch (Exception) { }
                };
                bar.Controls.AddRange(new Control[] { closeButton, copy, note });
                d.Controls.Add(box);
                d.Controls.Add(bar);
                d.CancelButton = closeButton;
                box.Select(box.TextLength, 0);
                d.ShowDialog(this);
            }
        }

        // The steps' titles and tips of the setup (the uninstall uses two of the steps with its own)
        void SetupSteps()
        {
            check.Title.Text = Lang.T("Check this PC");
            special.Title.Text = Lang.T("Restart with key 7");
            sign.Title.Text = Lang.T("Sign the driver for this PC");
            install.Title.Text = Lang.T("Install the driver and the programs");
            test.Title.Text = Lang.T("Print a test label");
            // The tips say plainly what you see or do, each understandable on its own (user's choice 2026-10-30)
            check.Tip = Lang.T("The setup looks for the installed driver and for QL printers connected by USB. If your printer is missing, connect it, " +
                               "switch it on (QL-700/800: Editor Lite LED off) and open the setup again.");
            special.Tip = Lang.T("On ARM64 PCs Windows installs this driver only after a restart in which you press 7 on the blue screen. The next normal " +
                                 "restart sets everything back, and the driver stays installed.");
            sign.Tip = Lang.T("Windows only installs signed drivers. For this the setup makes a certificate that is valid on this PC only and deletes its key right afterwards.");
            install.Tip = InstallTip(arm);
            test.Tip = Lang.T("Prints a small label on the loaded roll, so you can see that everything works. First the setup asks the printer for its roll; " +
                              "if it doesn't answer, nothing is printed.");
            foreach (var s in new[] { check, sign, install, test }) ShowStep(s, true);
        }

        // ---------- uninstall ----------

        // QlLabelFormats.exe --uninstall [--scripts <folder with install.ps1>] [--temp-copy]. The installed exe can't remove its own
        // folder while it runs, so from there it starts a copy of itself in %TEMP% (with install.ps1) and ends; the copy
        // deletes itself when its window closes.
        internal static void Uninstall(string[] args)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath), appDir = Path.GetDirectoryName(InstalledExe);
            int at = Array.IndexOf(args, "--scripts");
            string scripts = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            if (scripts == null && string.Equals(exeDir.TrimEnd('\\'), appDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                string temp = Path.Combine(Path.GetTempPath(), "ql-unidrv-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string tempScripts = Path.Combine(temp, "setup"), exe = Path.Combine(temp, Path.GetFileName(Application.ExecutablePath));
                try
                {
                    Directory.CreateDirectory(tempScripts);
                    File.Copy(Application.ExecutablePath, exe);
                    if (File.Exists(Application.ExecutablePath + ".config")) File.Copy(Application.ExecutablePath + ".config", exe + ".config");
                    string script = Path.Combine(appDir, @"setup\install.ps1");
                    if (File.Exists(script)) File.Copy(script, Path.Combine(tempScripts, "install.ps1"));
                    Process.Start(exe, "--uninstall --scripts \"" + tempScripts + "\" --temp-copy");
                    return;
                }
                catch (Exception) { scripts = Path.Combine(appDir, "setup"); }  // no copy: from here (the folder may stay)
            }
            var form = new InstallForm(null, false, scripts ?? Path.Combine(exeDir, "setup"));
            if (Array.IndexOf(args, "--temp-copy") >= 0) form.tempCopy = exeDir;
            Application.Run(form);
        }

        // The %TEMP% copy removes itself a moment after it ended
        void RemoveTempCopy()
        {
            if (tempCopy == null || !Path.GetFileName(tempCopy).StartsWith("ql-unidrv-uninstall-", StringComparison.Ordinal)) return;
            try
            {
                Process.Start(new ProcessStartInfo(NativeTools.Path("cmd.exe"), "/c ping -n 4 127.0.0.1 >nul & rd /s /q \"" + tempCopy + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
            }
            catch (Exception) { }
        }

        // What will be removed: the printers (a shared one in orange: other PCs lose it), the programs, the rest; keep the
        // label formats (ticked).
        void ShowUninstall()
        {
            mode = Mode.Uninstall;
            uninstallMode = true;
            string script = Path.Combine(uninstallScripts, "install.ps1");
            SetSubtitle(Versions.App);
            headline.Text = Lang.T("Uninstall the QL printer driver");
            doneBox.Visible = options.Visible = inSpecial.Visible = details.Visible = uninstallLink.Visible = false;
            ShowPicture(null);
            ShowReport(false);
            foreach (var s in new[] { check, special, sign, install, test }) { Set(s, StepState.Waiting, ""); ShowStep(s, false); }
            intro.Visible = true;
            if (!File.Exists(script))
            {
                intro.Text = Lang.F("Uninstall: use Setup.exe from the release zip ({0} is missing).", script);
                main.Visible = false;
                close.Visible = true;
                close.Text = Lang.T("Close");
                return;
            }
            intro.Text = Lang.T("Removes everything the setup installed on this PC:");
            FillRemoveList();
            removeList.Visible = true;
            labelCount = 0;
            try { if (File.Exists(LabelStore.JsonPath)) labelCount = LabelStore.Load(LabelStore.JsonPath).Formats.Count; } catch (Exception) { }
            keepLabels.Text = Lang.F("Keep my label formats ({0} formats, for a later install)", labelCount);
            keepRow.Visible = File.Exists(LabelStore.JsonPath);
            main.Text = Lang.T("Uninstall");
            main.Enabled = main.Visible = true;
            close.Text = Lang.T("Cancel");
            close.Visible = true;
            AcceptButton = close;  // removing is never the default
            close.Focus();
        }

        void FillRemoveList()
        {
            removeList.SuspendLayout();
            foreach (Control c in removeList.Controls.Cast<Control>().ToList()) c.Dispose();
            removeList.Controls.Clear();
            // em and wide are from the start at 100 %: the new labels get the width at this screen's scaling (150 % test 1324)
            int row = 0, valueWidth = (wide - em * 7) * Font.Height / em;
            Action<string, Control> add = (key, value) =>
            {
                removeList.Controls.Add(Theme.Fore(new Label { Text = key, AutoSize = true, Margin = new Padding(0, 0, em, em / 3) }, Role.Dim), 0, row);
                value.Margin = new Padding(0, 0, 0, em / 3);
                removeList.Controls.Add(value, 1, row++);
            };
            Func<string, Role, Label> text = (t, role) => Theme.Fore(new Label { Text = t, AutoSize = true, MaximumSize = new Size(valueWidth, 0), Margin = Padding.Empty }, role);
            bool first = true;
            foreach (string p in MainForm.OurPrinters().Where(n => !Redirected(n)))
            {
                string port = QlDevice.PortOf(p) ?? "", share = ShareName(p);
                var cell = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                cell.Controls.Add(text(port.StartsWith(@"\\") ? Lang.F("{0} on {1}", p, port) : p, Role.Text));
                if (share != null) cell.Controls.Add(text(Lang.F("shared as \"{0}\" – other PCs can't print through it any more", share), Role.WarningText));
                add(first ? Lang.T("Printers") : "", cell);
                first = false;
            }
            add(Lang.T("Programs"), text("QL Label Formats, QL Label Maker, QL Printer Status", Role.Text));
            add(Lang.T("Also"), text(Lang.T("the driver, its certificate and the logs"), Role.Text));
            removeList.ResumeLayout();
        }

        // A printer that a Remote Desktop session redirects here (TS port, "(redirected N)"): install.ps1's Test-RedirectedPrinter
        static bool Redirected(string printer)
        {
            return Regex.IsMatch(QlDevice.PortOf(printer) ?? "", @"^TS\d+$") || Regex.IsMatch(printer, @"\((umgeleitet|redirected) \d+\)$");
        }

        // A printer's share name, or null when it isn't shared
        static string ShareName(string printer)
        {
            if (!IsShared(printer)) return null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k == null ? null : (k.GetValue("Share Name") as string ?? printer);
            }
            catch (Exception) { return printer; }
        }

        // The uninstall's two steps (the setup's check and install steps with their own titles and tips)
        void UninstallSteps()
        {
            check.Title.Text = Lang.T("Close the programs");
            check.Tip = Lang.T("QL Label Formats and QL Label Maker are closed first; QL Printer Status closes by itself.");
            install.Title.Text = Lang.T("Remove printers, driver and programs");
            install.Tip = Lang.T("Windows asks once for your permission. Then the printers, the driver, the programs with their Start menu entries and the certificate are removed.");
        }

        async void RunUninstall()
        {
            // install.ps1 stops for redirected printers (they keep the driver in use): ask before anything is closed
            var redirected = MainForm.OurPrinters().Where(Redirected).ToList();
            if (redirected.Count > 0)
            {
                removeList.Visible = keepRow.Visible = false;
                close.Text = Lang.T("Close");
                UninstallSteps();
                ShowStep(install, true);
                Fail(install, Explain("rdp-printer", null), "Printers redirected by a Remote Desktop session use this driver: " +
                     string.Join(", ", redirected.Select(p => "'" + p + "'")), true);
                return;
            }
            for (string open; (open = LabelToolOpen()) != null; )
            {
                if (QlMessage.Show(this, Lang.F("\"{0}\" is open. Save your changes and close it, then click OK.", open), Text,
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return;
            }
            mode = Mode.Running;
            labelsKept = keepRow.Visible && keepLabels.Checked;
            removeList.Visible = keepRow.Visible = intro.Visible = main.Visible = false;
            close.Text = Lang.T("Close");
            AcceptButton = null;
            UninstallSteps();
            ShowStep(check, true);
            ShowStep(install, true);
            Set(check, StepState.Done, Lang.T("Closed."));
            Set(install, StepState.Working, Lang.T("Windows asks about \"Windows PowerShell\": click Yes."));
            Busy(install);

            string temp = Path.GetTempPath(), resultFile = Path.Combine(temp, "ql-unidrv-setup.txt");
            logFile = Path.Combine(temp, "ql-unidrv-install.log");
            foreach (var f in new[] { logFile, resultFile }) try { File.Delete(f); } catch (Exception) { }
            var psi = new ProcessStartInfo(NativeTools.Path("powershell.exe", elevated: true), "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(uninstallScripts, "install.ps1") +
                                           "\" -Uninstall" + (labelsKept ? " -KeepLabels" : "") + " -Log \"" + logFile + "\" -ResultFile \"" + resultFile + "\"")
            { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = temp };
            var timer = new Timer { Interval = 400 };
            bool switched = false;
            timer.Tick += delegate
            {
                if (!installRunning || switched) return;
                switched = true;
                Set(install, StepState.Working, Lang.T("Removing printers, driver and programs..."));
            };
            int code;
            try
            {
                timer.Start();
                code = await Task.Run(() => { using (var p = Process.Start(psi)) { installRunning = true; p.WaitForExit(); return p.ExitCode; } });
            }
            catch (Win32Exception ex)
            {
                timer.Dispose();
                installRunning = false;
                Fail(install, ex.NativeErrorCode == 1223 ? Lang.T("Declined, nothing removed. To try again: Try again.") : Lang.T("Could not start the install:") + " " + ex.Message, null);
                return;
            }
            timer.Dispose();
            installRunning = false;
            var result = ReadResult(resultFile).GroupBy(kv => kv.Key).ToDictionary(g => g.Key, g => g.Last().Value);
            string done, error, errorCode;
            result.TryGetValue("done", out done);
            bool ok = code == 0 && (done == "1" || (!result.ContainsKey("error") && !File.Exists(InstalledExe)));
            // What Windows didn't let go (left= lines: a port, a paper size): named in the done view, the log kept for Show details
            var left = ReadResult(resultFile).Where(kv => kv.Key == "left-port" || kv.Key == "left-paper")
                                             .Select(kv => kv.Key == "left-port" ? Lang.F("port {0}", kv.Value) : Lang.F("paper size \"{0}\"", kv.Value)).ToList();
            if (ok) RemoveSigningCopies();
            if (ok && left.Count == 0) DeleteRunFiles(resultFile);
            if (closeWhenDone) { mode = Mode.Removed; Close(); return; }
            if (!ok)
            {
                result.TryGetValue("error", out error);
                result.TryGetValue("code", out errorCode);
                var body = SetupForm.TranscriptBody(logFile).Select(l => Regex.Replace(l, "^PS>TerminatingError\\(\\w*\\): \"(.*)\"$", "$1")).ToList();
                Fail(install, Explain(errorCode, error ?? body.LastOrDefault(l => !l.StartsWith(" ") && !l.StartsWith("+")) ?? Lang.T("It stopped without a message.")),
                     string.Join("\r\n", body.Skip(Math.Max(0, body.Count - 40))) + "\r\n\r\nLog: " + logFile, errorCode == "rdp-printer");
                return;
            }
            if (left.Count > 0) failureDetails = string.Join("\r\n", SetupForm.TranscriptBody(logFile).Skip(Math.Max(0, SetupForm.TranscriptBody(logFile).Count - 40))) + "\r\n\r\nLog: " + logFile;
            ShowRemoved(left);
        }

        // The uninstall's done view: only the done box (and what stayed, with Show details)
        void ShowRemoved(List<string> left = null)
        {
            mode = Mode.Removed;
            bool rest = left != null && left.Count > 0;
            headline.Text = Lang.T("The QL printer driver is removed");
            doneBox.Title = rest ? Lang.T("Done – removed, except:") : Lang.T("Done – everything is removed.");
            var lines = new List<string>();
            if (rest) lines.Add(string.Join(", ", left) + " – " + Lang.T("Windows didn't let it go; Show details says why."));
            if (labelsKept) lines.Add(Lang.F("Your {0} label formats were kept; a later install takes them over.", labelCount));
            doneBox.Line.Text = string.Join("\n", lines);
            doneBox.Line.Visible = lines.Count > 0;
            details.Visible = rest;
            doneBox.Visible = true;
            removeList.Visible = keepRow.Visible = intro.Visible = false;
            foreach (var s in new[] { check, install }) ShowStep(s, false);
            main.Text = Lang.T("Done");
            main.Visible = true;
            close.Visible = false;
            AcceptButton = main;
            main.Focus();
        }

        // The copies of this PC's one-time signing root that signing left among this user's intermediate CAs (package.ps1
        // removes its own since 2026-10-31; older ones stay until here)
        static void RemoveSigningCopies()
        {
            if (Preview) return;
            try
            {
                using (var store = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority,
                                                                                                 System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser))
                {
                    store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                    foreach (var c in store.Certificates.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().ToList())
                        if (Regex.IsMatch(c.Subject, @"^CN=QL.* driver root CA")) store.Remove(c);
                }
            }
            catch (Exception) { }
        }

        // ---------- helpers ----------

        // Exit code, output and error output of a hidden, unelevated process.
        static Tuple<int, string, string> RunHidden(string exe, string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return Tuple.Create(p.ExitCode, output, err.Result);
            }
        }

        // A script's failure in one line: PowerShell writes the error's message first, then where it happened (in
        // Windows' language); without error output the script's last line.
        internal static string ErrorMessage(string output, string error)
        {
            var first = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Trim().Length > 0);
            return first != null ? first.Trim()
                 : output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? Lang.T("no message");
        }

        // install.ps1's -ResultFile while it is still being written (key=value lines, in order).
        internal static List<KeyValuePair<string, string>> ReadResult(string file)
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) list.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                    }
                }
            }
            catch (Exception) { }
            return list;
        }
    }
}
