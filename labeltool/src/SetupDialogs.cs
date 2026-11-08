// Dialogs of the Printer setup window (SetupForm.cs): the result of a change (icon, headline, a few fields, follow-up
// buttons, details on errors), the question before a printer is added, and the progress dialog while install.ps1
// runs as administrator. Styled like the status window (StatusForm.cs).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    enum ResultKind { Ok, Question, Warning, Error, Info }

    sealed class SetupResult : ScaledForm
    {
        // The state colours of the status window and the tray (Brand): OK and questions petrol.
        static readonly Color Green = Theme.Fixed.StateOk, Amber = Theme.Fixed.StateWarning, Red = Theme.Fixed.StateError, Blue = Theme.Fixed.StateOk;

        readonly FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
        readonly Label feedback = Theme.Fore(new Label { AutoSize = true }, Role.Dim);
        readonly FlowLayoutPanel feedbackRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        Control feedbackInfo;
        string feedbackTip;
        readonly TableLayoutPanel root;
        readonly int em, wide;
        readonly Button close;
        readonly Label head;

        static Font HeadFont(Font font) { return new Font(font.FontFamily, font.Size * 1.25f, FontStyle.Bold); }

        // Opened on a second screen with other scaling: CenterParent creates it there directly (ScaledForm).
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            head.Font = HeadFont(Font);  // larger than the window's font: FollowFont made it the plain bold size
        }

        // fields: label and value pairs (a null value is left out); note: a sentence below them; details: shown with
        // "Show details" (errors), copied with "Copy details". closeText: null for "Close", or "Cancel" for a question.
        public SetupResult(string title, ResultKind kind, string headline, IEnumerable<KeyValuePair<string, string>> fields,
                           string note, string details = null, string closeText = null)
        {
            closeText = closeText ?? Lang.T("Close");
            Text = title;
            Theme.Use(this);
            HelpPages.F1(this);   // its owner's page
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            em = Font.Height;
            wide = em * 26;

            root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(em) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var glyph = Glyph(kind, em * 2);
            glyph.Margin = new Padding(0, 0, em, 0);
            root.Controls.Add(glyph, 0, 0);
            root.SetRowSpan(glyph, 3);

            head = new Label { Text = headline, AutoSize = true, MaximumSize = new Size(wide, 0), Font = HeadFont(Font),
                               Margin = new Padding(0, em / 4, 0, em / 2) };
            root.Controls.Add(head, 1, 0);

            var body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Margin = new Padding(0, 0, 0, em / 2) };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            int rows = 0;
            if (fields != null)
                foreach (var f in fields)
                {
                    if (string.IsNullOrEmpty(f.Value)) continue;
                    table.Controls.Add(Theme.Fore(new Label { Text = f.Key, AutoSize = true, Margin = new Padding(0, 2, em, 2) }, Role.Dim), 0, rows);
                    table.Controls.Add(new Label { Text = BreakPath(f.Value, wide - em * 7), AutoSize = true, MaximumSize = new Size(wide - em * 7, 0), Margin = new Padding(0, 2, 0, 2) }, 1, rows);
                    rows++;
                }
            if (rows > 0) body.Controls.Add(table);
            if (!string.IsNullOrEmpty(note))
                body.Controls.Add(new Label { Text = note, AutoSize = true, MaximumSize = new Size(wide, 0), Margin = new Padding(0, 0, 0, em / 2) });
            if (!string.IsNullOrEmpty(details))
            {
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = true, Visible = false,
                                        Font = new Font(FontFamily.GenericMonospace, Font.Size * 0.9f), Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                                        Size = new Size(wide, em * 10), Margin = new Padding(0, 0, 0, em / 2) };
                var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                var show = Link(Lang.T("Show details"));
                show.LinkClicked += delegate { box.Visible = !box.Visible; show.Text = box.Visible ? Lang.T("Hide details") : Lang.T("Show details"); };
                var copy = Link(Lang.T("Copy details"));
                copy.LinkClicked += delegate
                {
                    try { Clipboard.SetText(details); Say(Lang.T("Details copied.")); } catch (Exception) { }
                };
                links.Controls.Add(show);
                links.Controls.Add(copy);
                body.Controls.Add(links);
                body.Controls.Add(box);
            }
            feedback.MaximumSize = new Size(wide - em * 2, 0);
            feedbackInfo = StatusForm.InfoIcon(em);
            feedbackInfo.Cursor = Cursors.Hand;
            feedbackInfo.Visible = false;
            InfoTip.Attach(feedbackInfo, () => feedbackTip, hover: true);
            feedbackRow.Controls.Add(feedback);
            feedbackRow.Controls.Add(feedbackInfo);
            body.Controls.Add(feedbackRow);
            root.Controls.Add(body, 1, 1);

            close = new Button { Text = closeText, AutoSize = true, MinimumSize = new Size(em * 6, 0), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.System };
            buttons.Controls.Add(close);
            buttons.Margin = new Padding(0, em / 2, 0, 0);
            root.Controls.Add(buttons, 1, 2);
            CancelButton = close;
            Controls.Add(root);
        }

        // A file path too wide for a field breaks after a backslash, never inside a name (a label would break it at any
        // character). Other values stay as they are. The full path is also in the details (Copy details).
        string BreakPath(string value, int width)
        {
            if (value.Length < 4 || value[1] != ':' || value[2] != '\\' || value.IndexOf('\n') >= 0
                || TextRenderer.MeasureText(value, Font).Width <= width) return value;
            var lines = new List<string>();
            string line = null;
            foreach (var part in value.Split('\\'))
            {
                if (line == null) line = part;
                else if (TextRenderer.MeasureText(line + "\\" + part, Font).Width > width) { lines.Add(line + "\\"); line = part; }
                else line += "\\" + part;
            }
            lines.Add(line);
            return string.Join("\n", lines);
        }

        LinkLabel Link(string text)
        {
            var link = new LinkLabel { Text = text, AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, em, em / 3) };
            Theme.StyleLink(link);
            return link;
        }

        // A button left of Close. The action's return text (if any) is shown below the fields; info (if given) is read
        // after the click: an explanation behind an (i) next to that text, or null.
        public Button AddAction(string text, Func<string> click, bool primary = false, Func<string> info = null)
        {
            var b = new Button { Text = text, AutoSize = true, FlatStyle = FlatStyle.System };
            b.Click += delegate
            {
                string said;
                try { said = click(); } catch (Exception ex) { said = ex.Message; }
                if (said != null) Say(said, info != null ? info() : null);
            };
            buttons.Controls.Add(b);
            buttons.Controls.SetChildIndex(b, buttons.Controls.Count - 2);  // before Close
            if (primary) { Theme.Primary(b); AcceptButton = b; Shown += delegate { b.Focus(); }; }
            return b;
        }

        // A button that ends the dialog with this result (e.g. "Add" for a question). Not primary: a plain button, and
        // Enter stays on the close button (e.g. "Print anyway" next to a default "Cancel").
        public void AddChoice(string text, DialogResult result, bool primary = true)
        {
            var b = new Button { Text = text, AutoSize = true, MinimumSize = new Size(em * 6, 0), DialogResult = result };
            buttons.Controls.Add(b);
            buttons.Controls.SetChildIndex(b, buttons.Controls.Count - 2);
            if (primary)
            {
                Theme.Primary(b);
                AcceptButton = b;
                Shown += delegate { b.Focus(); };
            }
            else
            {
                b.FlatStyle = FlatStyle.System;
                AcceptButton = close;
                Shown += delegate { close.Focus(); };
            }
        }

        void Say(string text, string tip = null)
        {
            feedback.Text = text;
            feedbackTip = tip;
            feedbackInfo.Visible = tip != null;
        }

        // A round icon like the status window's info icon: check, question mark, exclamation mark, cross or "i" (QlMessage).
        // High Contrast: an outline in the text colour (Windows' colours only).
        internal static Control Glyph(ResultKind kind, int d)
        {
            var p = new Panel { Size = new Size(d, d) };
            p.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float size = Math.Min(p.Width, p.Height) - 1f;  // the window may have rescaled it (150 % screen)
                var color = kind == ResultKind.Ok || kind == ResultKind.Info ? Green : kind == ResultKind.Question ? Blue : kind == ResultKind.Warning ? Amber : Red;
                bool outline = Theme.HighContrast;
                var symbol = outline ? Theme.Of(Role.Text) : Theme.Fixed.StateSymbol;
                if (outline) using (var ring = new Pen(symbol, Math.Max(2f, size * 0.07f))) g.DrawEllipse(ring, 1.5f, 1.5f, size - 3f, size - 3f);
                else using (var fill = new SolidBrush(color)) g.FillEllipse(fill, 0.5f, 0.5f, size - 1f, size - 1f);
                using (var pen = new Pen(symbol, Math.Max(2f, size * 0.11f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var white = new SolidBrush(symbol))
                {
                    float c = size / 2f;
                    switch (kind)
                    {
                        case ResultKind.Ok:
                            g.DrawLines(pen, new[] { new PointF(size * 0.28f, c), new PointF(size * 0.44f, size * 0.66f), new PointF(size * 0.72f, size * 0.34f) });
                            break;
                        case ResultKind.Error:
                            g.DrawLine(pen, size * 0.33f, size * 0.33f, size * 0.67f, size * 0.67f);
                            g.DrawLine(pen, size * 0.67f, size * 0.33f, size * 0.33f, size * 0.67f);
                            break;
                        default:
                            string mark = kind == ResultKind.Question ? "?" : kind == ResultKind.Info ? "i" : "!";
                            using (var font = new Font(FontFamily.GenericSansSerif, size * 0.42f, FontStyle.Bold, GraphicsUnit.Pixel))
                            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                                g.DrawString(mark, font, white, new RectangleF(0, 0, size, size), format);
                            break;
                    }
                }
            };
            return p;
        }
    }

    // "Working..." while a step runs (e.g. install.ps1 as administrator), so the window isn't just greyed out.
    sealed class SetupProgress : ScaledForm
    {
        SetupProgress(string title, string headline, string note)
        {
            Text = title;
            Theme.Use(this);
            HelpPages.F1(this);
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            int em = Font.Height;
            var root = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(em) };
            root.Controls.Add(new Label { Text = headline, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 0, 0, em / 2) });
            if (!string.IsNullOrEmpty(note))
                root.Controls.Add(Theme.Fore(new Label { Text = note, AutoSize = true, MaximumSize = new Size(em * 22, 0), Margin = new Padding(0, 0, 0, em / 2) }, Role.Dim));
            root.Controls.Add(new ProgressBar { Style = ProgressBarStyle.Marquee, MarqueeAnimationSpeed = 30, Size = new Size(em * 22, em) });
            Controls.Add(root);
        }

        // Runs work off the UI thread and shows this dialog until it ends; exceptions come back to the caller.
        public static T Run<T>(IWin32Window owner, string title, string headline, string note, Func<T> work)
        {
            var task = Task.Run(work);
            using (var form = new SetupProgress(title, headline, note))
            {
                form.FormClosing += (s, e) => { if (!task.IsCompleted) e.Cancel = true; };
                form.Shown += delegate
                {
                    task.ContinueWith(t => form.Close(), TaskScheduler.FromCurrentSynchronizationContext());
                };
                form.ShowDialog(owner);
            }
            return task.GetAwaiter().GetResult();
        }
    }
}
