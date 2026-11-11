// The window size and place of QL Label Maker and QL Label Formats (2026-11-11): saved per user when the window closes
// (or before a restart in another language), used again when it opens, as graphics programs do. Stored in
// HKCU\Software\QLUnidrv (values LabelMakerWindow, LabelFormatsWindow) as "left,top,width,height,maximized": the normal
// bounds (not the maximized ones), the place in screen pixels, the size in 96-dpi units so it follows a scaling change.
// A saved place that isn't mostly on a screen's working area now (screen unplugged, another desk) is not used: the window
// opens as without a value (the default size, centred where the mouse is). Never larger than the working area; minimized
// is never restored. Uninstalling removes the key with the other settings.
using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    static class WindowBounds
    {
        // HKCU; null: off. Only the programs themselves (their exe has a title, version\*.cs): the test exes, built from the
        // same sources, keep their default size and leave the user's value alone unless they set a key of their own.
        internal static string Key = IsProgram() ? Lang.SettingsKey : null;

        static bool IsProgram()
        {
            try
            {
                var exe = System.Reflection.Assembly.GetEntryAssembly();
                return exe != null && exe.GetCustomAttributes(typeof(System.Reflection.AssemblyTitleAttribute), false).Length > 0;
            }
            catch (Exception) { return false; }
        }
        public const string LabelMaker = "LabelMakerWindow", LabelFormats = "LabelFormatsWindow";

        // Where the window will open: the middle of its saved place when that is used, else the mouse (as before). The
        // window's starting DPI is that screen's.
        public static Point StartPoint(string name)
        {
            Rectangle r;
            bool max;
            return Saved(name, out r, out max) ? new Point(r.Left + r.Width / 2, r.Top + r.Height / 2) : Cursor.Position;
        }

        // In the window's constructor, after its default size: the saved place, and saving it again when it closes.
        public static void Use(Form form, string name)
        {
            Rectangle r;
            bool max;
            if (Saved(name, out r, out max))
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Bounds = r;
                if (max) form.WindowState = FormWindowState.Maximized;
            }
            form.FormClosed += delegate { Save(form, name); };
        }

        public static void Save(Form form, string name)
        {
            var r = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            if (Key == null || r.Width <= 0 || r.Height <= 0) return;
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Key))
                    k.SetValue(name, Format(r, form.DeviceDpi, form.WindowState == FormWindowState.Maximized));
            }
            catch (Exception) { }
        }

        static bool Saved(string name, out Rectangle bounds, out bool maximized)
        {
            string value = null;
            if (Key != null) try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) value = k == null ? null : k.GetValue(name) as string; }
            catch (Exception) { value = null; }
            return Parse(value, Screen.AllScreens.Select(s => s.WorkingArea).ToArray(), Brand.ScreenDpi, out bounds, out maximized);
        }

        internal static string Format(Rectangle r, int dpi, bool maximized)
        {
            Func<int, int> to96 = v => (int)Math.Round(v * 96.0 / dpi);
            return string.Join(",", new[] { r.Left, r.Top, to96(r.Width), to96(r.Height), maximized ? 1 : 0 }
                                        .Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }

        // The saved value as pixels on the screen it is mostly on (areas: the screens' working areas; dpiAt: a screen's
        // DPI at a point), at most that working area and moved into it. False: no value, a damaged one, or not mostly on
        // any screen now.
        internal static bool Parse(string value, Rectangle[] areas, Func<Point, int> dpiAt, out Rectangle bounds, out bool maximized)
        {
            bounds = Rectangle.Empty;
            maximized = false;
            if (string.IsNullOrEmpty(value)) return false;
            var parts = value.Split(',');
            if (parts.Length != 5) return false;
            var v = new int[5];
            for (int i = 0; i < 5; i++)
                if (!int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
            if (v[2] < 100 || v[3] < 100 || v[2] > 20000 || v[3] > 20000) return false;
            int dpi = dpiAt(new Point(v[0] + v[2] / 2, v[1] + v[3] / 2));
            if (dpi <= 0) dpi = 96;
            var r = new Rectangle(v[0], v[1], (int)Math.Round(v[2] * dpi / 96.0), (int)Math.Round(v[3] * dpi / 96.0));
            Rectangle area = Rectangle.Empty;
            long most = 0;
            foreach (var a in areas)
            {
                var common = Rectangle.Intersect(a, r);
                long n = (long)common.Width * common.Height;
                if (n > most) { most = n; area = a; }
            }
            if (most * 2 < (long)r.Width * r.Height) return false;   // not mostly on a screen
            // Into the working area; a few pixels beyond it are the window's invisible resize border (Windows 10/11)
            int slack = (int)Math.Round(8 * dpi / 96.0);
            int w = Math.Min(r.Width, area.Width), h = Math.Min(r.Height, area.Height);
            int x = Math.Max(area.Left - slack, Math.Min(r.Left, area.Right + slack - w));
            int y = Math.Max(area.Top, Math.Min(r.Top, area.Bottom + slack - h));   // the title bar stays on the screen
            bounds = new Rectangle(x, y, w, h);
            maximized = v[4] != 0;
            return true;
        }
    }
}
