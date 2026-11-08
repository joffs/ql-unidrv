// "About QL Unidrv": the mark and wordmark, which program and driver version this is (builds and Copy for support), the licence and the project page,
// and that the project is not Brother's. The only place in the programs that names JOFFS. Opened from each program's
// menu (AboutForm.ShowAbout).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed class AboutForm : ScaledForm
    {
        readonly ToolTip tips = new ToolTip();
        readonly List<Control> smallText = new List<Control>();   // the build line and Copy: 90 % of the window's font
        Font SmallFont { get { return new Font(Font.FontFamily, Font.SizeInPoints * 0.9f, GraphicsUnit.Point); } }
        const string ProjectUrl = "https://github.com/joffs/ql-unidrv";

        // program: the product name shown in the Start menu ("QL Label Formats", "QL Label Maker", "QL Printer Status").
        public static void ShowAbout(IWin32Window owner, string program)
        {
            if (owner != null)
            {
                using (var form = new AboutForm(program)) form.ShowDialog(owner);
                return;
            }
            // From the tray (no window of its own): one window, not modal (a modal loop of its own kept the tray menu
            // running inside it), at the tray corner like the status window, with a taskbar button, in front. Without
            // an owner CenterParent left it at 0,0 behind other windows.
            if (open != null && !open.IsDisposed) { Front(open); return; }
            open = new AboutForm(program) { StartPosition = FormStartPosition.Manual, ShowInTaskbar = true, atTray = true };
            var shown = open;
            shown.Shown += delegate { shown.PlaceAtTray(); Front(shown); };
            shown.FormClosed += delegate { if (open == shown) open = null; shown.Dispose(); };
            shown.Show();
        }

        static AboutForm open;   // the tray's About window while it is open
        bool atTray;

        // Bottom right corner at the tray corner (StatusForm.TrayCorner), again after a change of scaling.
        void PlaceAtTray()
        {
            var corner = StatusForm.TrayCorner(Font.Height);
            Location = new Point(corner.X - Width, corner.Y - Height);
        }

        // A tray process may bring its window to the front right after the menu click; TopMost for a moment makes sure.
        static void Front(Form form)
        {
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.TopMost = true;
            form.Activate();
            form.TopMost = false;
        }

        // The installed driver's version ("0.18.304.1114"), null when it isn't installed.
        internal static string DriverVersion() { return Versions.InstalledDriver(); }

        // What Copy puts on the clipboard for a support request: program, driver and Windows, one line each.
        static string SupportText(string program, string driver)
        {
            string arch = null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                    if (k != null) arch = Convert.ToString(k.GetValue("PROCESSOR_ARCHITECTURE"));
            }
            catch (Exception) { }
            return program + " " + (Versions.Product ?? "?") + "\r\n" +
                   (driver == null ? Lang.T("No QL driver installed") : Lang.F("Driver {0}", driver)) + "\r\n" +
                   "Windows " + Environment.OSVersion.Version + (arch != null ? " " + arch : "") + "\r\n";
        }

        AboutForm(string program)
        {
            Text = Lang.T("About QL Unidrv");
            HelpPages.F1(this);   // the owner program's page (the tray's window has none)
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Theme.Use(this);
            Theme.Back(this, Role.Content);
            Theme.TintCaption(this);

            int em = Font.Height, width = em * 22;

            // Top: the mark and the wordmark on the liner tone.
            var top = Theme.Back(new Panel { Margin = Padding.Empty, Size = new Size(width + em * 3, em * 7) }, Role.Caption);
            top.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int u = Font.Height;  // the present font: after a move to a screen with other scaling the window's em changed
                float mark = u * 4.5f, x = u * 1.5f, y = (top.Height - mark) / 2f;
                Brand.DrawMark(g, x, y, mark);
                using (var bold = new Font("Segoe UI", Font.SizeInPoints * 2.6f, FontStyle.Bold, GraphicsUnit.Point))
                using (var semi = new Font("Segoe UI Semibold", Font.SizeInPoints * 2.6f, FontStyle.Regular, GraphicsUnit.Point))
                {
                    var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
                    int tx = (int)(x + mark + u), ty = (int)(y + mark / 2f - bold.Height * 0.62f);
                    Size ql = TextRenderer.MeasureText(g, "QL", bold, Size.Empty, flags);
                    TextRenderer.DrawText(g, "QL", bold, new Point(tx, ty), Theme.Of(Role.Accent), flags);
                    TextRenderer.DrawText(g, "Unidrv", semi, new Point(tx + ql.Width + u / 3, ty), Theme.Of(Role.CaptionText), flags);
                    TextRenderer.DrawText(g, "by JOFFS", Font, new Point(tx + 2, ty + bold.Height), Theme.Of(Role.Dim), flags);
                }
            };

            // Body: program and driver releases, their builds (small, for support, with Copy), licence and project page,
            // not Brother's.
            string driver = DriverVersion();
            var title = new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), Margin = Padding.Empty,
                Font = new Font(Font, FontStyle.Bold), Text = program + " " + (Versions.App ?? "")
            };
            var driverLine = new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, em / 6),
                Text = driver == null ? Lang.T("No QL driver installed") : Lang.F("Driver {0}", Versions.Release(driver))
            };
            var builds = new List<string>();
            if (Versions.Build != null) builds.Add(Lang.F("Build {0}", Versions.Build));
            if (Versions.Stamp(driver) != null) builds.Add(Lang.F("Driver build {0}", Versions.Stamp(driver)));
            var buildLine = Theme.Fore(new Label
            {
                AutoSize = true, Font = SmallFont, Margin = new Padding(0, 0, em / 2, 0),
                Text = string.Join("  ·  ", builds)
            }, Role.Dim);
            var copy = new LinkLabel { AutoSize = true, Font = SmallFont, Text = Lang.T("Copy"), Margin = Padding.Empty };
            smallText.Add(buildLine);
            smallText.Add(copy);
            Theme.StyleLink(copy);
            tips.SetToolTip(copy, Lang.T("Copies the versions, e.g. for a support request"));
            var copied = new Timer { Interval = 2000 };
            copied.Tick += delegate { copied.Stop(); copy.Text = Lang.T("Copy"); };
            copy.LinkClicked += delegate
            {
                try { Clipboard.SetText(SupportText(program, driver)); copy.Text = Lang.T("Copied"); copied.Start(); } catch (Exception) { }
            };
            var buildRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, em * 2 / 3), Padding = Padding.Empty };
            buildRow.Controls.AddRange(new Control[] { buildLine, copy });
            var lines = new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, em / 3),
                Text = Lang.T("Free software under the MIT License")
            };
            var link = new LinkLabel { AutoSize = true, Text = ProjectUrl.Substring("https://".Length), Margin = new Padding(0, 0, 0, em * 2 / 3) };
            Theme.StyleLink(link);
            link.LinkClicked += delegate { try { Process.Start(ProjectUrl); } catch (Exception) { } };
            var note = Theme.Fore(new Label
            {
                AutoSize = true, MaximumSize = new Size(width, 0), Margin = Padding.Empty,
                Text = Lang.T("An independent project, not made or endorsed by Brother. Brother and QL are trademarks of Brother Industries, Ltd.")
            }, Role.Dim);
            var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(em * 3 / 2, em, em * 3 / 2, em), Margin = Padding.Empty };
            body.Controls.AddRange(new Control[] { title, driverLine, buildRow, lines, link, note });

            // Footer: OK.
            var ok = new Button { Text = "OK", AutoSize = true, DialogResult = DialogResult.OK, MinimumSize = new Size(em * 6, 0), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            Theme.Primary(ok);
            ok.Click += delegate { if (!Modal) Close(); };   // the tray's window is not modal: DialogResult doesn't close it
            AcceptButton = ok;
            CancelButton = ok;
            var footer = Theme.Back(new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Dock = DockStyle.Fill, Padding = new Padding(em, em * 2 / 3, em, em * 2 / 3), Margin = Padding.Empty }, Role.Surface);
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.Controls.Add(ok, 0, 0);

            var root = new TableLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            root.Controls.Add(top);
            root.Controls.Add(body);
            root.Controls.Add(footer);
            Controls.Add(root);
        }

        // Opened on a second screen with other scaling (ScaledForm): the caption tint creates the handle early.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (atTray) PlaceAtTray();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            foreach (var c in smallText) c.Font = SmallFont;   // FollowFont scales only bold ones
        }
    }
}
