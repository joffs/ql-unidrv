// Theme, part 2 (refactor S7, moved unchanged from Theme.cs): colouring windows and controls in the current scheme,
// Windows' native parts (dark scroll bars, list headers, up-down frames), the title bar, tooltips.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    static partial class Theme
    {
        // ---- applying ----

        // Colours a window (or any control) and everything in it in the current scheme.
        public static void Apply(Control root)
        {
            ApplyTree(root);
            root.Invalidate(true);
            var form = root as Form;
            if (form != null && form.IsHandleCreated) Caption(form);
        }

        static void ApplyTree(Control c)
        {
            ApplyOne(c);
            var r = RolesOf(c);
            if (!r.Hooked)
            {
                r.Hooked = true;
                c.ControlAdded += (s, e) => ApplyTree(e.Control);
                TipsOf(c);
            }
            foreach (Control child in c.Controls) ApplyTree(child);
        }

        static void ApplyOne(Control c)
        {
            Roles r;
            roles.TryGetValue(c, out r);
            bool dark = Dark;

            // Light and High Contrast: Windows' own colours (reset), so nothing differs from a window without a theme.
            if (c is Form || c is TrackBar) Colors(c, dark, Role.Window, Role.Text);
            else if (c is TextBoxBase || c is ListBox || c is ListView || c is UpDownBase || c is ComboBox || c is TreeView)
            {
                Colors(c, dark, Role.Content, Role.Text);
                var box = c as TextBoxBase;
                if (box != null && box.Multiline) StyleFrame(box, dark);
                HookNative(c);
            }
            else if (c is LinkLabel) StyleLink((LinkLabel)c);
            else if (c is Button && (r == null || !r.Primary)) StyleButton((Button)c, dark);
            else if (c is CheckBox || c is RadioButton) StyleCheck((ButtonBase)c, dark);
            else if (c is ToolStrip) StyleStrip((ToolStrip)c, dark);
            else if (c is ScrollableControl && ((ScrollableControl)c).AutoScroll) HookNative(c);

            if (r != null)
            {
                if (r.Fore != null) c.ForeColor = Of(r.Fore.Value);
                if (r.Back != null) c.BackColor = Of(r.Back.Value);
                if (r.Primary) StylePrimary((Button)c);
            }
        }

        static void Colors(Control c, bool dark, Role back, Role fore)
        {
            if (dark) { c.BackColor = Of(back); c.ForeColor = Of(fore); }
            else { c.ResetBackColor(); c.ResetForeColor(); }
        }

        static void HookNative(Control c)
        {
            var r = RolesOf(c);
            if (!r.NativeHooked) { r.NativeHooked = true; c.HandleCreated += delegate { Native(c); }; }
            if (c.IsHandleCreated) Native(c);
        }

        // Links in Accent; a link with a role of its own (Theme.Link) in that colour.
        public static void StyleLink(LinkLabel link)
        {
            Roles r;
            roles.TryGetValue(link, out r);
            if (r != null && r.Link != null)
            {
                link.LinkColor = link.VisitedLinkColor = link.ActiveLinkColor = Of(r.Link.Value);
                return;
            }
            link.LinkColor = Of(Role.Accent);
            link.VisitedLinkColor = Of(Role.Accent);
            link.ActiveLinkColor = Of(Role.AccentPressed);
        }

        static void StylePrimary(Button button)
        {
            if (HighContrast)
            {
                button.FlatStyle = FlatStyle.Standard;
                button.UseVisualStyleBackColor = true;
                button.ForeColor = SystemColors.ControlText;
                return;
            }
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.MouseOverBackColor = Of(Role.PrimaryHot);
            button.FlatAppearance.MouseDownBackColor = Of(Role.PrimaryPressed);
            button.BackColor = button.Enabled ? Of(Role.Primary) : Of(Role.DisabledFace);
            button.ForeColor = button.Enabled ? Of(Role.OnPrimary) : Of(Role.Dim);
            button.FlatAppearance.BorderColor = button.Enabled ? Of(Role.PrimaryLine) : Of(Role.DisabledLine);
        }

        // Light: Windows' button as before. Dark: flat with our colours (Windows has no dark push button for WinForms).
        static void StyleButton(Button b, bool dark)
        {
            var r = RolesOf(b);
            if (dark)
            {
                if (r.Flat == null) r.Flat = b.FlatStyle;
                b.FlatStyle = FlatStyle.Flat;
                b.UseVisualStyleBackColor = false;
                b.BackColor = Of(Role.ButtonFace);
                b.ForeColor = Of(Role.Text);
                b.FlatAppearance.BorderColor = Of(Role.ButtonLine);
                b.FlatAppearance.MouseOverBackColor = Of(Role.Hover);
                b.FlatAppearance.MouseDownBackColor = Of(Role.Selection);
            }
            else if (r.Flat != null)
            {
                b.FlatStyle = r.Flat.Value;
                b.ResetBackColor();
                b.UseVisualStyleBackColor = true;
                b.ResetForeColor();
                r.Flat = null;
            }
        }

        // FlatStyle.System ignores the text colour (dark text on dark): Standard in dark, back to System in light.
        static void StyleCheck(ButtonBase b, bool dark)
        {
            var r = RolesOf(b);
            if (dark && b.FlatStyle == FlatStyle.System) { r.Flat = FlatStyle.System; b.FlatStyle = FlatStyle.Standard; }
            else if (!dark && r.Flat != null) { b.FlatStyle = r.Flat.Value; r.Flat = null; }
        }

        // Tooltips: Windows draws them light unless the tip window takes Explorer's dark style, checked at every popup so
        // a switch applies to the next tip; set only when it changes (setting it shows the tip again: a new popup). The windows' own ToolTip fields (found once per control of this program) and
        // every toolbar's and menu's tip (StyleStrip, ThemeRenderer).
        public static void Tip(ToolTip tip)
        {
            TipState state;
            if (tip == null || hookedTips.TryGetValue(tip, out state)) return;
            state = new TipState();
            hookedTips.Add(tip, state);
            tip.Popup += delegate
            {
                bool dark = Dark;
                if (dark == state.Dark) return;
                state.Dark = dark;
                try { SetWindowTheme((IntPtr)tipHandle.GetValue(tip, null), dark ? "DarkMode_Explorer" : null, null); }
                catch (Exception) { }
            };
        }

        static void TipsOf(Control c)
        {
            if (c.GetType().Assembly != typeof(Theme).Assembly) return;
            for (var t = c.GetType(); t != null && t.Assembly == typeof(Theme).Assembly; t = t.BaseType)
                foreach (var f in t.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public
                                            | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly))
                    if (typeof(ToolTip).IsAssignableFrom(f.FieldType)) Tip((ToolTip)f.GetValue(c));
        }

        public static void TipOf(ToolStrip strip)
        {
            try { Tip((ToolTip)stripTip.GetValue(strip, null)); }
            catch (Exception) { }
        }

        sealed class TipState { public bool Dark; }
        static readonly ConditionalWeakTable<ToolTip, TipState> hookedTips = new ConditionalWeakTable<ToolTip, TipState>();
        static readonly System.Reflection.PropertyInfo tipHandle = typeof(ToolTip).GetProperty("Handle",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        static readonly System.Reflection.PropertyInfo stripTip = typeof(ToolStrip).GetProperty("ToolTip",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        // A multi-line text box keeps Explorer's dark scrollbars, whose 3D frame stays white: a single line in dark, the
        // frame again in light.
        static void StyleFrame(TextBoxBase box, bool dark)
        {
            var r = RolesOf(box);
            if (dark && box.BorderStyle == BorderStyle.Fixed3D) { r.Framed = true; box.BorderStyle = BorderStyle.FixedSingle; }
            else if (!dark && r.Framed) { r.Framed = false; box.BorderStyle = BorderStyle.Fixed3D; }
        }

        // Toolbars drawn by Windows (RenderMode System) take our renderer in dark.
        static void StyleStrip(ToolStrip strip, bool dark)
        {
            var r = RolesOf(strip);
            if (dark && strip.RenderMode == ToolStripRenderMode.System) { r.Render = ToolStripRenderMode.System; strip.Renderer = ToolStripManager.Renderer; }
            else if (!dark && r.Render != null) { strip.RenderMode = r.Render.Value; r.Render = null; }
            Colors(strip, dark, Role.Window, Role.Text);
            TipOf(strip);
        }

        // Scrollbars, lists, combo boxes in Windows' own dark style ("DarkMode_*" themes of Windows 10 1809 and later;
        // without them the control stays light and works as before).
        static void Native(Control c)
        {
            // Lists take Explorer's dark items view: light group headings, dark scrollbars. One-line text boxes take the
            // file dialog's (a dark frame; Explorer's stays white), multi-line ones Explorer's (the dialog's scrollbars
            // are light).
            var box = c as TextBoxBase;
            string app = !Dark ? null : c is ComboBox || box != null && !box.Multiline ? "DarkMode_CFD"
                       : c is ListView ? "DarkMode_ItemsView" : "DarkMode_Explorer";
            try
            {
                SetWindowTheme(c.Handle, app, null);
                var updown = c as UpDownBase;
                if (updown != null && Dark) { UpDownFrame.Hook(updown); SpinButtons.Hook(updown); }
                if (updown != null) updown.Invalidate(true);   // with its buttons, after a live switch
                var list = c as ListView;
                if (list != null)
                {
                    IntPtr header = SendMessage(list.Handle, 0x101F /* LVM_GETHEADER */, IntPtr.Zero, IntPtr.Zero);
                    if (header != IntPtr.Zero) SetWindowTheme(header, Dark ? "DarkMode_ItemsView" : null, null);
                    if (header != IntPtr.Zero && Dark) HeaderText.Hook(list, header);
                }
            }
            catch (Exception) { }
        }

        // The title bar: dark or light, and the Caption tone for tinted windows (Windows 11; older Windows ignores it).
        static void Caption(Form form)
        {
            try
            {
                int dark = Dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, 4) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref dark, 4);   // Windows 10 before 20H1
                Roles r;
                if (roles.TryGetValue(form, out r) && r.Tinted)
                {
                    int caption = HighContrast ? -1 /* DWMWA_COLOR_DEFAULT */ : ColorRef(Of(Role.Caption));
                    int text = HighContrast ? -1 : ColorRef(Of(Role.CaptionText));
                    DwmSetWindowAttribute(form.Handle, 35 /* DWMWA_CAPTION_COLOR */, ref caption, 4);
                    DwmSetWindowAttribute(form.Handle, 36 /* DWMWA_TEXT_COLOR */, ref text, 4);
                }
            }
            catch (Exception) { }
        }

        static int ColorRef(Color c) { return c.R | (c.G << 8) | (c.B << 16); }

        // The dark header theme keeps black column titles: the list's header asks it before each title (NM_CUSTOMDRAW),
        // and this sets the Text role then. Once per list handle; in light it lets everything through.
        sealed class HeaderText : NativeWindow
        {
            readonly IntPtr header;
            HeaderText(ListView list, IntPtr header) { this.header = header; AssignHandle(list.Handle); list.HandleDestroyed += delegate { ReleaseHandle(); }; }

            static readonly ConditionalWeakTable<ListView, HeaderText> hooks = new ConditionalWeakTable<ListView, HeaderText>();
            public static void Hook(ListView list, IntPtr header)
            {
                HeaderText h;
                if (hooks.TryGetValue(list, out h)) { if (h.Handle == list.Handle) return; hooks.Remove(list); }
                hooks.Add(list, new HeaderText(list, header));
            }

            protected override void WndProc(ref Message m)
            {
                const int WM_NOTIFY = 0x4E, NM_CUSTOMDRAW = -12, CDDS_PREPAINT = 1, CDDS_ITEMPREPAINT = 0x10001, CDRF_NOTIFYITEMDRAW = 0x20;
                int p = IntPtr.Size;
                if (m.Msg == WM_NOTIFY && Dark && Marshal.ReadIntPtr(m.LParam) == header && Marshal.ReadInt32(m.LParam, 2 * p) == NM_CUSTOMDRAW)
                {
                    int stage = Marshal.ReadInt32(m.LParam, 3 * p);   // NMCUSTOMDRAW after NMHDR (hwndFrom, idFrom, code)
                    if (stage == CDDS_PREPAINT) { m.Result = (IntPtr)CDRF_NOTIFYITEMDRAW; return; }
                    if (stage == CDDS_ITEMPREPAINT) { SetTextColor(Marshal.ReadIntPtr(m.LParam, 4 * p), ColorRef(Of(Role.Text))); m.Result = IntPtr.Zero; return; }
                }
                base.WndProc(ref m);
            }
        }

        // A number box draws its frame itself, always in the light text box style: in dark it gets the dark text box's
        // frame (FieldLine) over it after each paint. Once per handle; in light it lets everything through.
        sealed class UpDownFrame : NativeWindow
        {
            readonly UpDownBase box;
            UpDownFrame(UpDownBase box) { this.box = box; AssignHandle(box.Handle); box.HandleDestroyed += delegate { ReleaseHandle(); }; }

            static readonly ConditionalWeakTable<UpDownBase, UpDownFrame> hooks = new ConditionalWeakTable<UpDownBase, UpDownFrame>();
            public static void Hook(UpDownBase box)
            {
                UpDownFrame h;
                if (hooks.TryGetValue(box, out h)) { if (h.Handle == box.Handle) return; hooks.Remove(box); }
                hooks.Add(box, new UpDownFrame(box));
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0xF /* WM_PAINT */ || !Dark || box.BorderStyle == BorderStyle.None) return;
                using (var g = Graphics.FromHwnd(box.Handle))
                using (var pen = new Pen(Of(Role.FieldLine)))
                    g.DrawRectangle(pen, 0, 0, box.ClientSize.Width - 1, box.ClientSize.Height - 1);
            }
        }

        // A number box's up/down buttons draw themselves light whatever the window theme: in dark they are painted over
        // after each paint, both halves in ButtonFace (Hover under the mouse, Selection while pressed, DisabledFace when
        // off), a Border line, and filled arrows in whole pixels (Text, Dim when off). Once per handle; in light it lets
        // everything through.
        sealed class SpinButtons : NativeWindow
        {
            readonly Control buttons;
            SpinButtons(Control buttons) { this.buttons = buttons; AssignHandle(buttons.Handle); buttons.HandleDestroyed += delegate { ReleaseHandle(); }; }

            static readonly ConditionalWeakTable<Control, SpinButtons> hooks = new ConditionalWeakTable<Control, SpinButtons>();
            public static void Hook(UpDownBase box)
            {
                foreach (Control c in box.Controls)
                {
                    if (c.GetType().Name != "UpDownButtons") continue;
                    SpinButtons h;
                    if (hooks.TryGetValue(c, out h)) { if (h.Handle == c.Handle) continue; hooks.Remove(c); }
                    hooks.Add(c, new SpinButtons(c));
                }
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0xF /* WM_PAINT */ || !Dark) return;
                int w = buttons.ClientSize.Width, h = buttons.ClientSize.Height, half = h / 2;
                if (w <= 0 || h <= 0) return;
                var at = buttons.PointToClient(Control.MousePosition);
                bool pressed = (Control.MouseButtons & MouseButtons.Left) != 0;
                int aw = Math.Max(5, (w * 2 / 5) | 1), ah = (aw + 1) / 2;   // arrow: odd width, height (width + 1) / 2
                using (var g = Graphics.FromHwnd(buttons.Handle))
                {
                    for (int i = 0; i < 2; i++)
                    {
                        var r = i == 0 ? new Rectangle(0, 0, w, half) : new Rectangle(0, half, w, h - half);
                        bool hot = buttons.Enabled && r.Contains(at);
                        var face = !buttons.Enabled ? Role.DisabledFace : hot && pressed ? Role.Selection : hot ? Role.Hover : Role.ButtonFace;
                        using (var b = new SolidBrush(Of(face))) g.FillRectangle(b, r);
                        int x0 = (w - aw) / 2, y0 = r.Y + (r.Height - ah) / 2;
                        using (var b = new SolidBrush(Of(buttons.Enabled ? Role.Text : Role.Dim)))
                            for (int row = 0; row < ah; row++)
                            {
                                int inset = i == 0 ? ah - 1 - row : row;
                                g.FillRectangle(b, x0 + inset, y0 + row, aw - 2 * inset, 1);
                            }
                    }
                    using (var pen = new Pen(Of(Role.Border)))
                    {
                        g.DrawLine(pen, 0, 0, 0, h - 1);
                        g.DrawLine(pen, 1, half, w - 1, half);
                    }
                }
            }
        }

        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
