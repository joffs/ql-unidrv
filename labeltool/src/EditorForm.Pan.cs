using System;
using System.Drawing;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- pan with the middle mouse button (as in CorelDRAW; 2026-11-10) ----------
        // Press and hold the wheel over the label or the grey area around it: the move cross, the view follows the mouse
        // 1:1, release ends it. Only when there is something to scroll. A message filter of its own, so the canvas never
        // sees the middle button: the selection, text editing (caret and all), the focus, undo and the dirty flag stay as
        // they are. Esc or a lost capture end it. The middle click on a tab (DocTabStrip, outside the host) is untouched.
        PanFilter pan;

        void SetUpPan()
        {
            pan = new PanFilter(this);
            Application.AddMessageFilter(pan);
            FormClosed += delegate { Application.RemoveMessageFilter(pan); };
            host.MouseCaptureChanged += delegate { if (pan.Active && !host.Capture) pan.End(); };
            Deactivate += delegate { if (pan.Active) pan.End(); };
        }

        // true while the view is being dragged (for the UI test)
        internal bool Panning { get { return pan != null && pan.Active; } }

        sealed class PanFilter : IMessageFilter
        {
            const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208, WM_MBUTTONDBLCLK = 0x0209,
                WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_KEYDOWN = 0x0100, MK_LBUTTON = 1, MK_RBUTTON = 2;
            readonly EditorForm form;
            Point grabbed, scrollAtGrab;   // screen point of the press; the scroll position then (positive)
            bool ending;

            public PanFilter(EditorForm form) { this.form = form; }

            public bool Active { get; private set; }

            public bool PreFilterMessage(ref Message m)
            {
                if (Active)
                {
                    switch (m.Msg)
                    {
                        case WM_MOUSEMOVE: Follow(ScreenOf(m)); return true;
                        case WM_MBUTTONUP: End(); return true;
                        case WM_LBUTTONDOWN: case WM_RBUTTONDOWN: case WM_MBUTTONDOWN: case WM_MBUTTONDBLCLK: return true;
                        case WM_KEYDOWN: if ((Keys)(m.WParam.ToInt64() & 0xFFFF) == Keys.Escape) { End(); return true; } return false;
                    }
                    return false;
                }
                if (m.Msg != WM_MBUTTONDOWN && m.Msg != WM_MBUTTONDBLCLK) return false;
                var target = Control.FromChildHandle(m.HWnd);
                if (target == null || target.FindForm() != form || !form.Visible) return false;
                var host = form.host;
                var screen = ScreenOf(m);
                if (!host.RectangleToScreen(host.ClientRectangle).Contains(screen)) return false;
                // the canvas area: from here on the middle button is ours, whether or not there is anything to scroll
                if ((m.WParam.ToInt64() & (MK_LBUTTON | MK_RBUTTON)) != 0 || form.canvas.Capture) return true;
                if (!host.HorizontalScroll.Visible && !host.VerticalScroll.Visible) return true;
                grabbed = screen;
                scrollAtGrab = new Point(-host.AutoScrollPosition.X, -host.AutoScrollPosition.Y);
                Active = true;
                host.Capture = true;
                Cursor.Current = EditorCursors.PanMove(form.DeviceDpi);
                return true;
            }

            // The pointer's screen position of a mouse message (lParam: client coordinates of the window it went to).
            static Point ScreenOf(Message m)
            {
                long l = m.LParam.ToInt64();
                var at = new Point((short)(l & 0xFFFF), (short)((l >> 16) & 0xFFFF));
                var to = Control.FromHandle(m.HWnd);
                return to != null ? to.PointToScreen(at) : Control.MousePosition;
            }

            void Follow(Point now)
            {
                form.host.AutoScrollPosition = new Point(scrollAtGrab.X - (now.X - grabbed.X), scrollAtGrab.Y - (now.Y - grabbed.Y));
                Cursor.Current = EditorCursors.PanMove(form.DeviceDpi);
            }

            public void End()
            {
                if (!Active || ending) return;
                ending = true;
                try
                {
                    Active = false;
                    if (form.host.Capture) form.host.Capture = false;
                    Cursor.Current = Cursors.Default;
                    // the canvas sets its own cursor (I-beam, hand, sizes ...) on the next move over it
                }
                finally { ending = false; }
            }
        }
    }
}
