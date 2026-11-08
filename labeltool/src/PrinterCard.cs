// StatusForm, part 2: one printer's card (PrinterCard). Part 1 (StatusForm.cs): the window.
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
    sealed partial class StatusForm
    {
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
                    if (await PrintReadiness.UseOnline(FindForm(), new[] { Printer }))
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
