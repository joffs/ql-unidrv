// The ✕ at the end of a search box (2026-11-09: one click empties it, not Backspace after Backspace). Shown only
// while the box holds text; the box keeps room for it on the right, so the text never runs under it. Esc stays as well.
// Used by "Print with data" (PrintDataDialog.cs).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class SearchClear : Control
    {
        readonly TextBox box;
        bool hot;

        SearchClear(TextBox box)
        {
            this.box = box;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Default;
            Dock = DockStyle.Right;
            AccessibleRole = AccessibleRole.PushButton;
        }

        // Puts the ✕ into the box; tip: its tooltip ("Clear search (Esc)").
        public static SearchClear Attach(TextBox box, ToolTip tips, string tip)
        {
            var x = new SearchClear(box) { AccessibleName = tip };
            box.Controls.Add(x);
            tips.SetToolTip(x, tip);
            Action fit = () =>
            {
                x.Width = box.ClientSize.Height;
                x.Invalidate();
                x.Visible = box.Text.Length > 0;
                if (box.IsHandleCreated) SendMessage(box.Handle, EmSetMargins, (IntPtr)EcRightMargin, (IntPtr)(x.Width << 16));
            };
            box.HandleCreated += delegate { fit(); };
            box.Resize += delegate { fit(); };
            box.FontChanged += delegate { fit(); };
            box.BackColorChanged += delegate { fit(); };
            box.TextChanged += delegate { x.Visible = box.Text.Length > 0; };
            fit();
            return x;
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            box.Text = "";
            box.Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(box.BackColor);   // the box's own (Theme sets it)
            float s = Math.Max(3, Height * 0.17f), cx = Width / 2f, cy = Height / 2f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Of(hot ? Role.Text : Role.Dim), Math.Max(1.2f, LogicalToDeviceUnits(1) * 1.4f)))
            {
                g.DrawLine(p, cx - s, cy - s, cx + s, cy + s);
                g.DrawLine(p, cx - s, cy + s, cx + s, cy - s);
            }
        }

        const int EmSetMargins = 0xD3, EcRightMargin = 2;
        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
