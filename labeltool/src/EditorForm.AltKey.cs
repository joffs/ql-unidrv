using System;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- Alt as the free-drag key ----------
        // Alt held during a drag (drawing or moving: no snapping) and then released alone would make Windows open the
        // menu bar (its Alt key alone). Its release is kept from the window during a drag, and right after a drag that
        // used Alt. Alt alone at any other time opens the menu as usual.
        void SetUpAltKey()
        {
            var filter = new AltKeyFilter(this);
            Application.AddMessageFilter(filter);
            FormClosed += delegate { Application.RemoveMessageFilter(filter); };
        }

        sealed class AltKeyFilter : IMessageFilter
        {
            const int WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105, VK_MENU = 0x12;
            readonly EditorForm form;

            public AltKeyFilter(EditorForm form) { this.form = form; }

            public bool PreFilterMessage(ref Message m)
            {
                if (m.Msg != WM_SYSKEYDOWN && m.Msg != WM_SYSKEYUP || (int)m.WParam.ToInt64() != VK_MENU) return false;
                var target = Control.FromChildHandle(m.HWnd);
                if (target == null || target.FindForm() != form) return false;
                var canvas = form.canvas;
                bool dragging = canvas.DrawDragging || canvas.Dragging;
                if (m.Msg == WM_SYSKEYDOWN)
                {
                    // pressed anew outside a drag: an old mark (its release went to another window) no longer counts
                    if (!dragging && (m.LParam.ToInt64() & 0x40000000) == 0) canvas.TakeAltInDrag();
                    return false;
                }
                bool used = canvas.TakeAltInDrag();
                return used || dragging;
            }
        }
    }
}
