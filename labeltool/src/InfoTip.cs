// The app's own tip box for an (i) icon (StatusForm.InfoIcon): a click opens the explanation in a small white box at
// the icon, with a petrol bar on the left, a hairline border and a shadow; a second click, Esc, a click elsewhere in
// the window, or moving or leaving the window closes it. It doesn't follow the mouse, doesn't take the focus and
// doesn't change the window's size. User's choice 2026-10-30: the Windows ToolTip looked unprofessional, and the
// explanation opened inline at the bottom of the status window made the window grow too much.
// hover (the setup window): it also opens while the mouse rests on the icon and closes when the mouse leaves it; a
// click pins it open until a second click. Rounded corners where Windows 11 offers them.
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class InfoTip : Form, IMessageFilter
    {
        readonly Control icon;
        readonly Func<string> text;
        readonly Label label = Theme.Back(new Label { AutoSize = true, Location = new Point(1, 1), UseMnemonic = false }, Role.Content);
        Form owner;
        bool rounded;  // Windows draws the corners and the border

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        InfoTip(Control icon, Func<string> text)
        {
            this.icon = icon;
            this.text = text;
            AutoScaleMode = AutoScaleMode.None;  // sized in pixels from the owner's font; no second scaling at 150 %
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Theme.Use(this);
            Theme.Back(this, Role.Content);
            Controls.Add(label);
            label.Click += delegate { Close(); };
            Paint += (s, e) => { if (!rounded) using (var line = new Pen(Theme.Of(Role.Border))) e.Graphics.DrawRectangle(line, 0, 0, Width - 1, Height - 1); };
            label.Paint += (s, e) => { using (var bar = new SolidBrush(Theme.Of(Role.Accent))) e.Graphics.FillRectangle(bar, 0, 0, Math.Max(3, Font.Height / 6), label.Height); };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // DWMWA_WINDOW_CORNER_PREFERENCE = small round corners, DWMWA_BORDER_COLOR = the hairline (Windows 11)
            Color border = Theme.Of(Role.Border);
            int corner = 3, color = border.R | border.G << 8 | border.B << 16;
            rounded = DwmSetWindowAttribute(Handle, 33, ref corner, 4) == 0 && DwmSetWindowAttribute(Handle, 34, ref color, 4) == 0;
        }

        // Makes the icon open and close the tip; text is read on each opening (the language may change). hover: it also
        // opens while the mouse rests on the icon (a short delay) and closes when the mouse leaves it, unless a click
        // pinned it.
        public static void Attach(Control icon, Func<string> text, bool hover = false)
        {
            InfoTip open = null;
            bool pinned = false;
            Func<bool> isOpen = () => open != null && !open.IsDisposed;
            Action show = () => { open = new InfoTip(icon, text); open.ShowAt(); };
            icon.Click += delegate
            {
                if (isOpen() && (pinned || !hover)) { open.Close(); pinned = false; return; }
                pinned = true;
                if (!isOpen()) show();
            };
            if (hover)
            {
                var wait = new Timer { Interval = 350 };
                wait.Tick += delegate { wait.Stop(); if (!isOpen()) { pinned = false; show(); } };
                icon.MouseEnter += delegate { if (!isOpen()) wait.Start(); };
                icon.MouseLeave += delegate { wait.Stop(); if (isOpen() && !pinned) open.Close(); };
                icon.Disposed += delegate { wait.Dispose(); };
            }
            icon.Disposed += delegate { if (isOpen()) open.Close(); };
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

        // Below the icon, or above it when the screen ends there (the status window sits above the clock); inside the
        // screen's working area.
        void ShowAt()
        {
            owner = icon.FindForm();
            Font = owner.Font;
            int em = Font.Height, bar = Math.Max(3, em / 6);
            label.Text = text();
            label.Padding = new Padding(bar + em * 2 / 3, em / 2, em * 2 / 3, em / 2);
            label.MaximumSize = new Size(em * 20 + label.Padding.Horizontal, 0);   // wraps at about 20 font heights
            ClientSize = label.GetPreferredSize(Size.Empty) + new Size(2, 2);    // the hairline border around it

            var r = icon.RectangleToScreen(icon.ClientRectangle);
            var area = Screen.FromRectangle(r).WorkingArea;
            int gap = Math.Max(2, em / 4);
            int x = Math.Max(area.Left, Math.Min(r.Left - bar, area.Right - Width));
            int y = r.Bottom + gap + Height <= area.Bottom ? r.Bottom + gap : Math.Max(area.Top, r.Top - gap - Height);
            Location = new Point(x, y);

            Application.AddMessageFilter(this);
            owner.Deactivate += OwnerChanged;
            owner.Move += OwnerChanged;
            owner.FormClosing += OwnerChanged;
            Show(owner);
        }

        void OwnerChanged(object sender, EventArgs e) { Close(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Application.RemoveMessageFilter(this);
            if (owner != null)
            {
                owner.Deactivate -= OwnerChanged;
                owner.Move -= OwnerChanged;
                owner.FormClosing -= OwnerChanged;
            }
            base.OnFormClosed(e);
        }

        // A click elsewhere closes the tip (a click on the icon is its toggle); Esc closes only the tip, not the window.
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_KEYDOWN = 0x100, WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204, WM_NCLBUTTONDOWN = 0xA1;
            if (m.Msg == WM_KEYDOWN && (Keys)(int)m.WParam == Keys.Escape)
            {
                Close();
                return true;
            }
            if ((m.Msg == WM_LBUTTONDOWN || m.Msg == WM_RBUTTONDOWN || m.Msg == WM_NCLBUTTONDOWN) && m.HWnd != icon.Handle && m.HWnd != label.Handle && m.HWnd != Handle)
                Close();
            return false;
        }
    }
}
