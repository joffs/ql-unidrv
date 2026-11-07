// Pictures for the setup window's ARM64 steps (InstallForm): the blue "Startup Settings" screen Windows shows after
// the prepared restart (key 7 = Disable driver signature enforcement) and Windows' dialog that asks before it installs
// a driver whose publisher it can't verify. Drawn, not screenshots: they scale with the font, follow the UI language
// (Windows' own wording, German from Strings.de.cs) and show only what matters; the other options are grey bars.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QlLabels
{
    enum SetupPictureKind { StartupSettings, InstallAnyway }

    sealed class SetupPicture : Control
    {
        SetupPictureKind kind;

        public SetupPicture()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public SetupPictureKind Kind { get { return kind; } set { kind = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Of(Role.Window));
            if (kind == SetupPictureKind.StartupSettings) PaintStartupSettings(g);
            else PaintInstallAnyway(g);
        }

        // colours: fixed begin - pictures of Windows' own screens, in Windows' colours in every scheme
        // The blue screen: title, the hint line, options 1-9 with 7 readable and marked, a key cap "7" beside it.
        void PaintStartupSettings(Graphics g)
        {
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var blue = new SolidBrush(Color.FromArgb(0, 84, 166))) g.FillRectangle(blue, r);
            float em = Font.Height, x = em, y = em * 0.7f;
            using (var title = new Font(Font.FontFamily, Font.Size * 1.5f))
            using (var white = new SolidBrush(Color.White))
            using (var faint = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
            using (var bar = new SolidBrush(Color.FromArgb(90, 255, 255, 255)))
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                g.DrawString(Lang.T("Startup Settings"), title, white, x, y);
                y += title.Height * 1.2f;
                g.DrawString(Lang.T("Press a number to choose from the options below:"), Font, faint, x, y);
                y += em * 1.4f;
                float line = (Height - y - em * 0.6f) / 9f;
                for (int i = 1; i <= 9; i++)
                {
                    float ly = y + (i - 1) * line;
                    if (i == 7)
                    {
                        string text = "7) " + Lang.T("Disable driver signature enforcement");
                        var size = g.MeasureString(text, bold);
                        using (var pen = new Pen(Theme.Fixed.Orange, Math.Max(2f, em / 8)))
                            g.DrawRectangle(pen, x - em * 0.3f, ly + line * 0.04f, size.Width + em * 0.6f, line * 0.92f);
                        g.DrawString(text, bold, white, x, ly + (line - bold.Height) / 2);
                        KeyCap(g, "7", x + size.Width + em * 1.2f, ly + line * 0.4f - em * 0.75f, em * 1.5f);
                    }
                    else
                    {
                        g.DrawString(i + ")", Font, faint, x, ly + (line - em) / 2);
                        g.FillRectangle(bar, x + em * 1.6f, ly + line * 0.35f, em * (8 + (i * 7 % 5) * 1.5f), line * 0.3f);
                    }
                }
            }
        }

        // A keyboard key with its label, in the brand's orange so it reads as "press this".
        void KeyCap(Graphics g, string label, float x, float y, float size)
        {
            using (var path = Rounded(x, y, size, size, size / 5))
            using (var fill = new SolidBrush(Color.White))
            using (var edge = new Pen(Theme.Fixed.Orange, Math.Max(2f, size / 12)))
            using (var font = new Font(Font.FontFamily, size * 0.4f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var text = new SolidBrush(Color.FromArgb(32, 32, 32)))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.FillPath(fill, path);
                g.DrawPath(edge, path);
                g.DrawString(label, font, text, new RectangleF(x, y, size, size), center);
            }
        }

        // Windows' publisher dialog: the title bar, the red warning, two choices with the second one marked.
        void PaintInstallAnyway(Graphics g)
        {
            float em = Font.Height;
            var r = new RectangleF(em * 0.2f, em * 0.2f, Width - em * 0.4f - 1, Height - em * 0.4f - 1);
            using (var white = new SolidBrush(Color.White))
            using (var border = new Pen(Color.FromArgb(160, 160, 160)))
            using (var text = new SolidBrush(Color.FromArgb(32, 32, 32)))
            using (var grey = new SolidBrush(Color.FromArgb(110, 110, 110)))
            using (var red = new SolidBrush(Color.FromArgb(196, 43, 28)))
            using (var big = new Font(Font.FontFamily, Font.Size * 1.15f))
            using (var bold = new Font(Font, FontStyle.Bold))
            {
                g.FillRectangle(white, r);
                g.DrawRectangle(border, r.X, r.Y, r.Width, r.Height);
                float x = r.X + em * 0.7f, y = r.Y + em * 0.4f;
                g.DrawString(Lang.T("Windows Security"), Font, grey, x, y);
                y += em * 1.5f;
                // the red shield with "x"
                float s = em * 1.6f;
                g.FillEllipse(red, x, y, s, s);
                using (var cross = new Pen(Color.White, Math.Max(2f, s / 9)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    g.DrawLine(cross, x + s * 0.32f, y + s * 0.32f, x + s * 0.68f, y + s * 0.68f);
                    g.DrawLine(cross, x + s * 0.68f, y + s * 0.32f, x + s * 0.32f, y + s * 0.68f);
                }
                var headRect = new RectangleF(x + s + em * 0.6f, y, r.Right - x - s - em * 1.4f, s * 1.6f);
                g.DrawString(Lang.T("Windows can't verify the publisher of this driver software"), big, red, headRect);
                y += Math.Max(s, g.MeasureString(Lang.T("Windows can't verify the publisher of this driver software"), big, (int)headRect.Width).Height) + em * 0.8f;
                float w = r.Right - x - em * 0.7f;
                y = Choice(g, Lang.T("Don't install this driver software"), x, y, w, Font, text, false) + em * 0.4f;
                Choice(g, Lang.T("Install this driver software anyway"), x, y, w, bold, text, true);
            }
        }

        // One of the dialog's two choices (an arrow and the text); the marked one gets the orange frame.
        float Choice(Graphics g, string label, float x, float y, float w, Font font, Brush brush, bool marked)
        {
            float em = Font.Height, h = em * 1.8f;
            using (var arrow = new Pen(marked ? Theme.Fixed.Petrol : Color.FromArgb(110, 110, 110), Math.Max(1.5f, em / 9)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float ay = y + h / 2;
                g.DrawLine(arrow, x + em * 0.3f, ay, x + em * 1.1f, ay);
                g.DrawLine(arrow, x + em * 0.8f, ay - em * 0.3f, x + em * 1.1f, ay);
                g.DrawLine(arrow, x + em * 0.8f, ay + em * 0.3f, x + em * 1.1f, ay);
            }
            g.DrawString(label, font, brush, x + em * 1.6f, y + (h - font.Height) / 2);
            if (marked)
                using (var pen = new Pen(Theme.Fixed.Orange, Math.Max(2f, em / 8)))
                    g.DrawRectangle(pen, x, y, w, h);
            return y + h;
        }

        // colours: fixed end

        static GraphicsPath Rounded(float x, float y, float w, float h, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(x, y, d, d, 180, 90);
            p.AddArc(x + w - d, y, d, d, 270, 90);
            p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            p.AddArc(x, y + h - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // The picture in the app's look, beside the setup window (user's wish 2026-10-30: more "CI", a frame around it): a
    // white box like the (i) tips (InfoTip) with a petrol bar, a hairline, rounded corners and a shadow; a petrol
    // heading, the picture in a hairline frame, one line of what to do there, and a small x. It never takes the focus;
    // the link in the step, the x or Esc close it.
    sealed class SetupPictureWindow : Form
    {
        public readonly SetupPicture Picture;
        bool rounded;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        public SetupPictureWindow(SetupPictureKind kind, Font font)
        {
            AutoScaleMode = AutoScaleMode.None;  // sized in pixels from the setup window's font (already scaled)
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Theme.Use(this);
            Theme.Back(this, Role.Content);
            KeyPreview = true;
            Font = font;
            int em = font.Height, bar = Math.Max(3, em / 6), pad = em;
            bool blue = kind == SetupPictureKind.StartupSettings;

            var heading = Theme.Fore(new Label
            {
                AutoSize = true, UseMnemonic = false, Font = new Font(font, FontStyle.Bold),
                Text = blue ? Lang.T("What the blue screen looks like") : Lang.T("What Windows' question looks like"),
                Location = new Point(bar + pad, pad * 3 / 4),
            }, Role.Heading);
            var close = Theme.Fore(new Label { AutoSize = true, Text = "✕", Cursor = Cursors.Hand, UseMnemonic = false }, Role.Dim);
            close.MouseEnter += delegate { Theme.Fore(close, Role.Accent); };
            close.MouseLeave += delegate { Theme.Fore(close, Role.Dim); };
            close.Click += delegate { Close(); };
            close.AccessibleName = Lang.T("Close");
            close.AccessibleRole = AccessibleRole.PushButton;

            // The picture inside a hairline frame
            var frame = new Panel { Padding = new Padding(1) };   // the picture clears to this colour (the window's)
            frame.Paint += (s, e) => { using (var line = new Pen(Theme.Of(Role.Border))) e.Graphics.DrawRectangle(line, 0, 0, frame.Width - 1, frame.Height - 1); };
            Picture = new SetupPicture { Kind = kind, Dock = DockStyle.Fill, Font = font };
            frame.Controls.Add(Picture);
            frame.Location = new Point(bar + pad, heading.Bottom + em * 2 / 3);
            frame.Size = new Size(em * 30 + 2, em * (blue ? 15 : 11) + 2);

            var caption = Theme.Fore(new Label
            {
                AutoSize = true, UseMnemonic = false, MaximumSize = new Size(frame.Width, 0),
                Text = blue ? Lang.T("Press 7 (or F7) on this screen.") : Lang.T("Click \"Install this driver software anyway\" there."),
                Location = new Point(bar + pad, frame.Bottom + em / 2),
            }, Role.Dim);
            Controls.AddRange(new Control[] { heading, close, frame, caption });
            ClientSize = new Size(frame.Right + pad, caption.Top + caption.PreferredHeight + pad * 3 / 4);
            close.Location = new Point(ClientSize.Width - pad / 2 - close.PreferredWidth, pad / 2);

            Paint += (s, e) =>
            {
                using (var b = new SolidBrush(Theme.Of(Role.Accent))) e.Graphics.FillRectangle(b, 0, 0, bar, ClientSize.Height);
                if (!rounded) using (var line = new Pen(Theme.Of(Role.Border))) e.Graphics.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
            };
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;            // CS_DROPSHADOW
                cp.ExStyle |= 0x08000000 | 0x80;     // WS_EX_NOACTIVATE, WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Color border = Theme.Of(Role.Border);
            int corner = 3, color = border.R | border.G << 8 | border.B << 16;   // Windows 11: small round corners, the hairline
            rounded = DwmSetWindowAttribute(Handle, 33, ref corner, 4) == 0 && DwmSetWindowAttribute(Handle, 34, ref color, 4) == 0;
        }
    }
}
