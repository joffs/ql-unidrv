// The look shared by all QL Unidrv windows (its colours: Theme.cs): petrol instead of Windows blue for the primary
// button, links, (i) icons, group headings and list selection; orange only for the peeling corner of the mark; state
// colours as in the tray. DrawMark paints the base mark (a petrol label with a peeling corner) at any size, crisp at
// every display scaling. The icon files are drawn by assets\make-icons.ps1 with the same geometry.
// Also in this class: Brand.Icons.cs (the label editor's tool, layer and arrange icons); HeaderBand.cs (the dialogs'
// petrol band).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    enum TrayState { Ok, Warning, Error, Offline }

    static partial class Brand
    {
        // The one left line of every window: the header band's title, the first text, lists and group contents start
        // here (user's choice 2026-10-30: one alignment and more whitespace in all app windows).
        public static int Gutter(int em) { return em * 3 / 2; }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetDpiForWindow(IntPtr window);
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct WinRect { public int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out WinRect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, ref WinRect lParam);

        // The one DPI helper of all windows. A window created directly on a screen whose scaling isn't Windows' system DPI
        // (a second screen at 150 %) keeps DeviceDpi 96 and its content stays at 100 %. It then gets the message Windows
        // sends on a move to that screen, and WinForms scales as on a move (as MainForm, 2026-10-30). Call it in
        // OnHandleCreated and again in OnLoad: a window whose handle exists early (e.g. Theme.TintCaption in the
        // constructor) is placed on its screen only before it shows, and that placement brings no DPI message. Pair it
        // with FollowFont in OnDpiChanged. Needs the app's TargetFramework 4.7+ (a harness exe too, or WinForms ignores
        // the message).
        public static void MatchScreenDpi(Form form)
        {
            int dpi;
            try { dpi = GetDpiForWindow(form.Handle); } catch (EntryPointNotFoundException) { return; }  // before Windows 10 1607
            if (dpi <= 0 || dpi == form.DeviceDpi) return;
            WinRect r;
            if (GetWindowRect(form.Handle, out r)) SendMessage(form.Handle, 0x02E0 /* WM_DPICHANGED */, (IntPtr)(dpi | dpi << 16), ref r);
        }

        // The effective DPI of the screen at a point (where a window will open), else the system DPI. The DPI a window
        // starts with (MainForm, EditorForm).
        public static int ScreenDpi(Point point)
        {
            try
            {
                uint x, y;
                if (GetDpiForMonitor(MonitorFromPoint(point, 2 /* MONITOR_DEFAULTTONEAREST */), 0 /* MDT_EFFECTIVE_DPI */, out x, out y) == 0 && x > 0)
                    return (int)x;
            }
            catch (Exception) { }
            using (var g = Graphics.FromHwnd(IntPtr.Zero)) return (int)g.DpiX;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, int flags);
        [System.Runtime.InteropServices.DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

        // For a form's OnDpiChanged: WinForms scales the window's font and the fonts inherited from it, not fonts a control
        // got as new Font(...) (bold titles, the bold main button stayed at the old size). Bold ones follow the window's font.
        public static void FollowFont(Control root, Font font)
        {
            foreach (Control c in root.Controls)
            {
                if (c.Font.Bold && Math.Abs(c.Font.SizeInPoints - font.SizeInPoints) > 0.1f) c.Font = new Font(font, FontStyle.Bold);
                FollowFont(c, font);
            }
        }

        // The state colour of a printer check (status cards; the tray uses the same colours).
        public static Color HealthColor(Health health)
        {
            switch (health)
            {
                case Health.Ok: case Health.Busy: return Theme.Fixed.StateOk;
                case Health.Warning: return Theme.Fixed.StateWarning;
                case Health.Error: return Theme.Fixed.StateError;
                default: return Theme.Fixed.StateUnknown;
            }
        }

        // Hints, captions under icons, the status bar: 90 % of the window's font. In points, so it follows the window's
        // font across display scalings. The caller disposes it.
        public static Font SmallFont(Font font)
        {
            return new Font(font.FontFamily, font.SizeInPoints * 0.9f, font.Style, GraphicsUnit.Point);
        }

        static readonly Icon[] trayIcons = new Icon[4];

        // The tray icon for a state, from the exe's resources (build.cmd: /resource:assets\tray-ok.ico,tray-ok.ico ...),
        // in the size Windows uses for the notification area; the exe's own icon if the resource is missing. Loaded once
        // per state and kept for the life of the process (the monitor asks every few seconds).
        public static Icon TrayIcon(TrayState state)
        {
            int i = (int)state;
            if (trayIcons[i] == null) trayIcons[i] = LoadTrayIcon(state);
            return trayIcons[i];
        }

        static Icon LoadTrayIcon(TrayState state)
        {
            string name = "tray-" + state.ToString().ToLowerInvariant() + ".ico";
            Size size = SystemInformation.SmallIconSize;
            try
            {
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                    if (stream != null) return new Icon(stream, size.Width, size.Height);
            }
            catch (Exception) { }
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { return SystemIcons.Application; }
        }

        // ---- the mark ----

        // The peeled-off corner of a label whose bottom right corner is (x1, y1); leg = how far it reaches along each
        // edge. Curled half way between a flat fold and a curve (the chosen "88·50" mark).
        internal static GraphicsPath CornerFlap(float x1, float y1, float leg)
        {
            const float t = 0.5f;
            float cx = x1 - leg, cy = y1 - leg, d = 0.18f * leg * t, r = 14f * (leg / 88f) * (1 - t), tx = cx + d, ty = cy + d;
            var p1 = new PointF(x1 - 0.32f * leg, cy + 0.02f * leg * t);
            var p2 = new PointF(tx + r + 0.14f * leg, cy + 0.05f * leg * t);
            var a = new PointF(tx + r, ty);
            var b = new PointF(tx, ty + r);
            var tip = new PointF(tx, ty);
            var path = new GraphicsPath();
            path.AddLine(cx, y1, x1, cy);
            path.AddBezier(new PointF(x1, cy), p1, p2, a);
            path.AddBezier(a, Lerp(a, tip, 2f / 3), Lerp(b, tip, 2f / 3), b);
            path.AddBezier(b, Mirror(p2, cx, cy), Mirror(p1, cx, cy), new PointF(cx, y1));
            path.CloseFigure();
            return path;
        }
        static PointF Lerp(PointF a, PointF b, float f) { return new PointF(a.X + (b.X - a.X) * f, a.Y + (b.Y - a.Y) * f); }
        static PointF Mirror(PointF p, float cx, float cy) { return new PointF(cx + (p.Y - cy), cy + (p.X - cx)); }

        // The flap with a soft shadow (three offset copies; GDI+ has no blur), in orange.
        internal static void PaintFlap(Graphics g, float x1, float y1, float leg, float scale)
        {
            using (var flap = CornerFlap(x1, y1, leg))
            {
                int[] alpha = { 18, 24, 30 };
                for (int i = 0; i < 3; i++)
                    using (var shadow = (GraphicsPath)flap.Clone())
                    using (var brush = new SolidBrush(Color.FromArgb(alpha[i], Theme.Fixed.Ink)))
                    using (var move = new Matrix())
                    {
                        float f = (3 - i) / 3f;
                        move.Translate(-3 * scale * f, 7 * scale * f);
                        shadow.Transform(move);
                        g.FillPath(brush, shadow);
                    }
                using (var brush = new LinearGradientBrush(new PointF(0, y1 - leg - 1), new PointF(0, y1 + 1), Theme.Fixed.OrangeLight, Theme.Fixed.Orange))
                    g.FillPath(brush, flap);
            }
        }

        static GraphicsPath LabelBody(float x0, float y0, float x1, float y1, float radius, float leg)
        {
            float d = 2 * radius;
            var path = new GraphicsPath();
            path.AddLine(x0 + radius, y1, x1 - leg, y1);
            path.AddLine(x1 - leg, y1, x1, y1 - leg);
            path.AddLine(x1, y1 - leg, x1, y0 + radius);
            path.AddArc(x1 - d, y0, d, d, 0, -90);
            path.AddLine(x1 - radius, y0, x0 + radius, y0);
            path.AddArc(x0, y0, d, d, 270, -90);
            path.AddLine(x0, y0 + radius, x0, y1 - radius);
            path.AddArc(x0, y1 - d, d, d, 180, -90);
            path.CloseFigure();
            return path;
        }

        static GraphicsPath LinerCorner(float x1, float y1, float radius, float leg)
        {
            var path = new GraphicsPath();
            path.AddLine(x1 - leg, y1, x1, y1 - leg);
            path.AddLine(x1, y1 - leg, x1, y1 - radius);
            path.AddArc(x1 - 2 * radius, y1 - 2 * radius, 2 * radius, 2 * radius, 0, 90);
            path.CloseFigure();
            return path;
        }

        static GraphicsPath Capsule(float x, float y, float w, float h)
        {
            var path = new GraphicsPath();
            path.AddArc(x, y, h, h, 90, 180);
            path.AddArc(x + w - h, y, h, h, 270, 180);
            path.CloseFigure();
            return path;
        }

        // The base mark in a square of the given size at (x, y): from 20 px the drawing of the 256 px icon, below that the
        // 16 px pixel drawing (as in the icon files).
        public static void DrawMark(Graphics g, float x, float y, float size)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TranslateTransform(x, y);
            if (size < 20)
            {
                g.ScaleTransform(size / 16f, size / 16f);
                using (var liner = LinerCorner(15, 15, 2, 6)) using (var brush = new SolidBrush(Theme.Fixed.Liner)) g.FillPath(brush, liner);
                using (var body = LabelBody(1, 1, 15, 15, 2, 6))
                using (var brush = new LinearGradientBrush(new PointF(0, .5f), new PointF(0, 15.5f), Theme.Fixed.PetrolLight, Theme.Fixed.PetrolMid)) g.FillPath(brush, body);
                using (var flap = new GraphicsPath())
                using (var brush = new SolidBrush(Theme.Fixed.Orange))
                {
                    flap.AddPolygon(new[] { new PointF(9, 15), new PointF(15, 9), new PointF(9, 9) });
                    g.FillPath(brush, flap);
                }
                g.SmoothingMode = SmoothingMode.None;
                using (var white = new SolidBrush(Theme.Fixed.MarkWhite))
                {
                    g.FillRectangle(white, 4, 3, 8, 2);
                    g.FillRectangle(white, 4, 6, 5, 2);
                }
            }
            else
            {
                g.ScaleTransform(size / 256f, size / 256f);
                using (var liner = LinerCorner(232, 232, 36, 88))
                {
                    using (var brush = new SolidBrush(Theme.Fixed.Liner)) g.FillPath(brush, liner);
                    using (var pen = new Pen(Color.FromArgb(89, Theme.Fixed.PetrolDark), 4) { LineJoin = LineJoin.Round }) g.DrawPath(pen, liner);
                }
                using (var body = LabelBody(24, 24, 232, 232, 36, 88))
                {
                    using (var brush = new LinearGradientBrush(new PointF(0, 23), new PointF(0, 233), Theme.Fixed.PetrolLight, Theme.Fixed.PetrolMid)) g.FillPath(brush, body);
                    using (var pen = new Pen(Color.FromArgb(89, Theme.Fixed.PetrolDark), 4) { LineJoin = LineJoin.Round }) g.DrawPath(pen, body);
                }
                var unclipped = g.Save();
                using (var clip = new GraphicsPath())
                {
                    clip.AddArc(24, 24, 72, 72, 180, 90); clip.AddArc(160, 24, 72, 72, 270, 90);
                    clip.AddArc(160, 160, 72, 72, 0, 90); clip.AddArc(24, 160, 72, 72, 90, 90);
                    clip.CloseFigure();
                    g.SetClip(clip, CombineMode.Intersect);
                }
                PaintFlap(g, 232, 232, 88, 1);
                g.Restore(unclipped);
                using (var brush = new SolidBrush(Color.FromArgb(245, Theme.Fixed.MarkWhite)))
                {
                    using (var bar = Capsule(60, 64, 136, 24)) g.FillPath(brush, bar);
                    using (var bar = Capsule(60, 108, 88, 24)) g.FillPath(brush, bar);
                }
            }
            g.Restore(state);
        }
    }
}
