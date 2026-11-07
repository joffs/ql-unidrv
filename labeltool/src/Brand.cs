// The look shared by all QL Unidrv windows (its colours: Theme.cs): petrol instead of Windows blue for the primary
// button, links, (i) icons, group headings and list selection; orange only for the peeling corner of the mark; state
// colours as in the tray. HeaderBand is the page-style dialogs' petrol band whose bottom right corner peels off the window.
// DrawMark paints the base mark (a petrol label with a peeling corner) at any size, crisp at every display scaling.
// The icon files are drawn by assets\make-icons.ps1 with the same geometry. DrawToolIcon paints the label editor's
// tool palette as line icons, DrawLayerIcon the icons of its layer commands (context menu).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    enum TrayState { Ok, Warning, Error, Offline }

    // The label editor's tool palette (Brand.DrawToolIcon).
    enum ToolIcon { Select, Text, Line, Box, Picture, Barcode, QrCode }

    // The label editor's layer commands in the context menu (Brand.DrawLayerIcon).
    enum LayerIcon { BringToFront, BringForward, SendBackward, SendToBack }

    // The label editor's "Arrange" toolbar (Brand.DrawArrangeIcon).
    enum ArrangeIcon { Duplicate, Delete, RotateLeft, RotateRight, ResetRotation, AlignLeft, AlignCenter, AlignRight, AlignTop, AlignMiddle, AlignBottom,
                       DistributeHorizontally, DistributeVertically }

    static class Brand
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
        // OnHandleCreated and again in OnLoad: a window whose handle exists early (e.g. Brand.TintCaption in the
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

        // The window's main action (Apply, Print, Install, Done): Primary with white text (Theme.Primary).
        public static void StylePrimary(Button button) { Theme.Primary(button); }

        // Links in the accent colour (Theme.StyleLink; every window's links get it anyway when Theme.Use colours it).
        public static void StyleLink(LinkLabel link) { Theme.StyleLink(link); }

        // Hints, captions under icons, the status bar: 90 % of the window's font. In points, so it follows the window's
        // font across display scalings. The caller disposes it.
        public static Font SmallFont(Font font)
        {
            return new Font(font.FontFamily, font.SizeInPoints * 0.9f, font.Style, GraphicsUnit.Point);
        }

        // Optional: the title bar in the Caption tone (light: the liner tone with dark text, also in Windows' dark mode).
        public static void TintCaption(Form form) { Theme.TintCaption(form); }

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

        // ---- tool icons ----

        // A line icon of the editor's tool palette, centred in r and drawn on a 24 grid (stroke 1.5 at 24 px), so it is
        // crisp at every display scaling. color: the Accent normally, OnPrimary on an accent background when the tool is
        // active, Dim when disabled (the caller paints the background).
        public static void DrawToolIcon(Graphics g, ToolIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), s = size / 24f;
            float x = r.X + (r.Width - size) / 2f, y = r.Y + (r.Height - size) / 2f;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (icon == ToolIcon.Barcode)
            {
                // Bars snapped to whole pixels: anti-aliased bar edges look blurred.
                int[,] bars = { { 4, 2 }, { 7, 1 }, { 9, 3 }, { 13, 1 }, { 15, 2 }, { 18, 1 }, { 20, 1 } };
                int top = (int)Math.Round(y + 5 * s), bottom = (int)Math.Round(y + 19 * s);
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var brush = new SolidBrush(color))
                    for (int i = 0; i < bars.GetLength(0); i++)
                    {
                        int left = (int)Math.Round(x + bars[i, 0] * s), right = (int)Math.Round(x + (bars[i, 0] + bars[i, 1]) * s);
                        g.FillRectangle(brush, left, top, Math.Max(1, right - left), bottom - top);
                    }
                g.Restore(state);
                return;
            }
            if (icon == ToolIcon.QrCode)
            {
                // Three finder patterns (ring, gap, centre square) and a few modules in the fourth corner, on the
                // 24 grid, snapped to whole pixels like the barcode's bars.
                g.SmoothingMode = SmoothingMode.None;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var brush = new SolidBrush(color))
                {
                    Action<float, float, float, float> cell = (cx, cy, cw, ch) =>
                    {
                        int left = (int)Math.Round(x + cx * s), top = (int)Math.Round(y + cy * s);
                        int right = Math.Max(left + 1, (int)Math.Round(x + (cx + cw) * s)), bottom = Math.Max(top + 1, (int)Math.Round(y + (cy + ch) * s));
                        g.FillRectangle(brush, left, top, right - left, bottom - top);
                    };
                    foreach (var at in new[] { new PointF(3, 3), new PointF(14, 3), new PointF(3, 14) })
                    {
                        // A real finder's proportions 1:1:3:1:1 (whole pixels at 24 px).
                        cell(at.X, at.Y, 7, 1); cell(at.X, at.Y + 6, 7, 1);
                        cell(at.X, at.Y + 1, 1, 5); cell(at.X + 6, at.Y + 1, 1, 5);
                        cell(at.X + 2, at.Y + 2, 3, 3);
                    }
                    cell(14, 14, 2, 2); cell(17, 14, 2, 2); cell(14, 17, 2, 2); cell(19, 17, 2, 2); cell(17, 19, 2, 2);
                }
                g.Restore(state);
                return;
            }
            g.TranslateTransform(x, y);
            g.ScaleTransform(s, s);
            using (var pen = new Pen(color, 1.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                switch (icon)
                {
                    case ToolIcon.Select:
                        g.DrawPolygon(pen, new[] { new PointF(6, 3.5f), new PointF(6, 18.5f), new PointF(9.8f, 15), new PointF(12.4f, 20.5f),
                                                   new PointF(14.8f, 19.4f), new PointF(12.2f, 14), new PointF(17.5f, 14) });
                        break;
                    case ToolIcon.Text:
                        g.DrawLines(pen, new[] { new PointF(5, 20), new PointF(12, 4), new PointF(19, 20) });
                        g.DrawLine(pen, 7.6f, 14.5f, 16.4f, 14.5f);
                        break;
                    case ToolIcon.Line:
                        g.DrawLine(pen, 6.5f, 17.5f, 17.5f, 6.5f);
                        g.FillEllipse(brush, 3, 15, 5, 5);
                        g.FillEllipse(brush, 16, 4, 5, 5);
                        break;
                    case ToolIcon.Box:
                        using (var box = RoundedRect(4, 6, 16, 12, 2.5f)) g.DrawPath(pen, box);
                        break;
                    case ToolIcon.Picture:
                        using (var frame = RoundedRect(3.5f, 4.5f, 17, 15, 2.5f)) g.DrawPath(pen, frame);
                        g.DrawLines(pen, new[] { new PointF(4, 17.5f), new PointF(9.5f, 11.5f), new PointF(13, 15.5f), new PointF(15.5f, 13), new PointF(20, 17.5f) });
                        g.FillEllipse(brush, 14.3f, 7.3f, 3.4f, 3.4f);
                        break;
                }
            }
            g.Restore(state);
        }

        // ---- layer icons ----

        // A context menu icon for the layer commands: two overlapping objects, the selected one filled, and an arrow up
        // or down (with a bar for "to front"/"to back"). Drawn on a 16 grid snapped to whole pixels, so it is crisp at
        // 16 px (100 %), 24 px (150 %) and every other size. One colour (the menu's text colour, GrayText when
        // disabled), so it suits light and dark menus alike.
        public static void DrawLayerIcon(Graphics g, LayerIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), k = size / 16f;
            int ox = (int)Math.Round(r.X + (r.Width - size) / 2f), oy = (int)Math.Round(r.Y + (r.Height - size) / 2f);
            int px = (int)Math.Round(size), w = Math.Max(1, (int)(k + 0.5f));
            bool up = icon == LayerIcon.BringToFront || icon == LayerIcon.BringForward;
            bool bar = icon == LayerIcon.BringToFront || icon == LayerIcon.SendToBack;
            Func<float, float, float, float, Rectangle> cells = (cx, cy, cw, ch) => Rectangle.FromLTRB(
                ox + (int)Math.Round(cx * k), oy + (int)Math.Round(cy * k), ox + (int)Math.Round((cx + cw) * k), oy + (int)Math.Round((cy + ch) * k));
            Rectangle other = cells(1, 3, 6, 6), selected = cells(4, 7, 6, 6);
            float radius = k;
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            using (var outline = RoundedRect(other.X + w / 2f, other.Y + w / 2f, other.Width - w, other.Height - w, Math.Max(0.5f, radius - w / 2f)))
            using (var fill = RoundedRect(selected.X, selected.Y, selected.Width, selected.Height, radius))
            {
                // The object behind keeps a gap around the one in front.
                Rectangle front = up ? selected : other;
                int gap = Math.Max(1, w / 2);
                front.Inflate(gap, gap);
                var clip = g.Save();
                g.SetClip(front, CombineMode.Exclude);
                if (up) g.DrawPath(pen, outline); else g.FillPath(brush, fill);
                g.Restore(clip);
                if (up) g.FillPath(brush, fill); else g.DrawPath(pen, outline);

                // The arrow, drawn pointing up and mirrored for the "back" commands.
                float cx = ox + (int)Math.Round(12 * k) + w / 2f, d = 2 * k;
                int barTop = oy + (int)Math.Round(k), tipTop = bar ? barTop + 2 * w : barTop;
                float barY = barTop + w / 2f, tipY = tipTop + w / 2f, endY = oy + (int)Math.Round(14 * k) - w / 2f;
                Func<float, float> my = v => up ? v : 2 * oy + px - v;
                g.DrawLine(pen, cx, my(tipY), cx, my(endY));
                g.DrawLines(pen, new[] { new PointF(cx - d, my(tipY + d)), new PointF(cx, my(tipY)), new PointF(cx + d, my(tipY + d)) });
                if (bar) g.DrawLine(pen, cx - d, my(barY), cx + d, my(barY));
            }
            g.Restore(state);
        }

        // ---- arrange icons ----

        // The icons of the editor's "Arrange" toolbar, in the layer icons' system: 16 grid snapped to whole pixels, one
        // colour (ControlText, GrayText when disabled). Align and distribute are drawn for the left/horizontal case and
        // mirrored or transposed for the others, so each pair is exactly symmetric. Rotate shows a filled object inside
        // a circling arrow, so it does not read as undo/redo.
        public static void DrawArrangeIcon(Graphics g, ArrangeIcon icon, RectangleF r, Color color)
        {
            float size = Math.Min(r.Width, r.Height), k = size / 16f;
            int ox = (int)Math.Round(r.X + (r.Width - size) / 2f), oy = (int)Math.Round(r.Y + (r.Height - size) / 2f);
            int n = (int)Math.Round(size), w = Math.Max(1, (int)(k + 0.5f));
            Func<float, int> at = v => (int)Math.Round(v * k);
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            using (var brush = new SolidBrush(color))
            {
                // A filled block in icon pixels, mirrored left/right and/or swapped to the vertical case.
                bool mirror = icon == ArrangeIcon.AlignRight || icon == ArrangeIcon.AlignBottom;
                bool turn = icon == ArrangeIcon.AlignTop || icon == ArrangeIcon.AlignMiddle || icon == ArrangeIcon.AlignBottom
                    || icon == ArrangeIcon.DistributeVertically;
                Action<int, int, int, int> block = (x0, y0, x1, y1) =>
                {
                    if (mirror) { int t = n - x1; x1 = n - x0; x0 = t; }
                    if (turn) { int t = x0; x0 = y0; y0 = t; t = x1; x1 = y1; y1 = t; }
                    g.FillRectangle(brush, ox + x0, oy + y0, x1 - x0, y1 - y0);
                };
                // Line centres on whole pixels: x(v) from the left, xr(v) the same distance from the right.
                Func<float, float> x = v => ox + at(v) + w / 2f, xr = v => ox + n - at(v) - w / 2f, y = v => oy + at(v) + w / 2f;
                switch (icon)
                {
                    case ArrangeIcon.Duplicate:
                    {
                        // Two equal pages, the front one with a plus; its size keeps the plus exactly centred.
                        int fw = at(9);
                        if ((fw - 3 * w) % 2 != 0) fw--;
                        var front = new Rectangle(ox + at(15) - fw, oy + at(15) - fw, fw, fw);
                        var back = new Rectangle(ox + at(1), oy + at(1), fw, fw);
                        float radius = Math.Max(0.5f, k - w / 2f);
                        var cut = front;
                        int gap = Math.Max(1, w / 2);
                        cut.Inflate(gap, gap);
                        var clip = g.Save();
                        g.SetClip(cut, CombineMode.Exclude);
                        using (var page = RoundedRect(back.X + w / 2f, back.Y + w / 2f, fw - w, fw - w, radius)) g.DrawPath(pen, page);
                        g.Restore(clip);
                        using (var page = RoundedRect(front.X + w / 2f, front.Y + w / 2f, fw - w, fw - w, radius)) g.DrawPath(pen, page);
                        int inner = fw - 2 * w, arm = (inner - w) / 2 - (w + 1) / 2;
                        int px = front.X + w + (inner - w) / 2, py = front.Y + w + (inner - w) / 2;
                        g.FillRectangle(brush, px, py - arm, w, w + 2 * arm);
                        g.FillRectangle(brush, px - arm, py, w + 2 * arm, w);
                        break;
                    }
                    case ArrangeIcon.Delete:
                        g.DrawLine(pen, x(1), y(3), xr(1), y(3));
                        g.DrawLines(pen, new[] { new PointF(x(6), y(3)), new PointF(x(6), y(1)), new PointF(xr(6), y(1)), new PointF(xr(6), y(3)) });
                        g.DrawLines(pen, new[] { new PointF(x(3), y(3)), new PointF(x(3), y(14)), new PointF(xr(3), y(14)), new PointF(xr(3), y(3)) });
                        g.DrawLine(pen, x(6), y(6), x(6), y(11));
                        g.DrawLine(pen, xr(6), y(6), xr(6), y(11));
                        break;
                    case ArrangeIcon.RotateLeft:
                    case ArrangeIcon.RotateRight:
                    {
                        // The object (a filled square) inside an arrow circling it three quarters, the head at the top
                        // pointing into the open quarter (right: clockwise).
                        int h = at(2);
                        using (var obj = RoundedRect(ox + n / 2 - h, oy + n / 2 - h, 2 * h, 2 * h, Math.Max(0.5f, k / 2f))) g.FillPath(brush, obj);
                        bool right = icon == ArrangeIcon.RotateRight;
                        float cx = ox + n / 2f, cy = oy + n / 2f, radius = 5.5f * k - w / 2f;
                        float start = right ? 0 : 180, sweep = right ? 270 : -270, end = (start + sweep) * (float)Math.PI / 180f;
                        g.DrawArc(pen, cx - radius, cy - radius, 2 * radius, 2 * radius, start, sweep);
                        float ex = cx + radius * (float)Math.Cos(end), ey = cy + radius * (float)Math.Sin(end);
                        float tx = -(float)Math.Sin(end) * (right ? 1 : -1), ty = (float)Math.Cos(end) * (right ? 1 : -1);
                        float nx = (float)Math.Cos(end), ny = (float)Math.Sin(end), tip = 2.5f * k, back = 1f * k, half = 2f * k;
                        g.FillPolygon(brush, new[] {
                            new PointF(ex + tx * tip, ey + ty * tip),
                            new PointF(ex - tx * back + nx * half, ey - ty * back + ny * half),
                            new PointF(ex - tx * back - nx * half, ey - ty * back - ny * half) });
                        break;
                    }
                    case ArrangeIcon.ResetRotation:
                    {
                        // "0°": back to no rotation (between the two rotate icons).
                        using (var f = new Font("Segoe UI", 11f * k, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                            g.DrawString("0°", f, brush, new RectangleF(ox, oy + 0.5f * k, n + 1.5f * k, n), fmt);
                        }
                        break;
                    }
                    case ArrangeIcon.AlignLeft:
                    case ArrangeIcon.AlignRight:
                    case ArrangeIcon.AlignTop:
                    case ArrangeIcon.AlignBottom:
                    {
                        // The guide line, then a long and a short object against it.
                        int gx = at(2), bx = gx + w + Math.Max(1, at(1));
                        block(gx, at(1), gx + w, n - at(1));
                        block(bx, at(3), at(14), at(7));
                        block(bx, at(9), at(10), at(13));
                        break;
                    }
                    case ArrangeIcon.AlignCenter:
                    case ArrangeIcon.AlignMiddle:
                    {
                        int gl = (n - w) / 2, a = at(5), b = at(3);
                        block(gl, at(1), gl + w, n - at(1));
                        block(gl - a, at(3), gl + w + a, at(7));
                        block(gl - b, at(9), gl + w + b, at(13));
                        break;
                    }
                    case ArrangeIcon.DistributeHorizontally:
                    case ArrangeIcon.DistributeVertically:
                    {
                        // Three equal objects at equal spacing between two guide lines.
                        int bw = at(2);
                        if ((n - bw) % 2 != 0) bw++;
                        int cl = (n - bw) / 2, sp = at(4), gap = Math.Max(1, at(0.5f));
                        foreach (int left in new[] { cl - sp, cl, cl + sp }) block(left, at(4), left + bw, n - at(4));
                        int g0 = cl - sp - gap - w, g1 = cl + sp + bw + gap;
                        block(g0, at(1), g0 + w, n - at(1));
                        block(g1, at(1), g1 + w, n - at(1));
                        break;
                    }
                }
            }
            g.Restore(state);
        }

        static GraphicsPath RoundedRect(float x, float y, float w, float h, float radius)
        {
            float d = 2 * radius;
            var path = new GraphicsPath();
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + w - d, y, d, d, 270, 90);
            path.AddArc(x + w - d, y + h - d, d, d, 0, 90);
            path.AddArc(x, y + h - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // The petrol band at the top of page-style dialogs (setup, printer setup, About): title, optional subtitle, and in
    // the bottom right corner the band peels off the window like the mark's corner. Its height follows the font, so
    // it scales with the display.
    sealed class HeaderBand : Control
    {
        string subtitle = "";

        public HeaderBand()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Dock = DockStyle.Top;
            AccessibleRole = AccessibleRole.StaticText;   // screen readers read the title
            UpdateHeight();
        }

        public string Subtitle
        {
            get { return subtitle; }
            set { subtitle = value ?? ""; AccessibleDescription = subtitle; UpdateHeight(); Invalidate(); }
        }

        Font TitleFont()
        {
            return new Font("Segoe UI Semibold", Font.SizeInPoints * 1.6f, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); AccessibleName = Text; UpdateHeight(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); UpdateHeight(); }
        protected override void OnParentChanged(EventArgs e) { base.OnParentChanged(e); UpdateHeight(); }

        // On a DPI change WinForms scales the font (OnFontChanged sets the new height) and then the bounds once more: the
        // band came out 1.5 times too tall after 96 -> 144 dpi (Printer setup over 690 px, ui_sweep 2026-11-05). The
        // height is the font's alone.
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            base.ScaleControl(factor, specified & ~BoundsSpecified.Height);
            UpdateHeight();
        }

        void UpdateHeight()
        {
            int em = Font.Height;
            using (var title = TitleFont())
                Height = em * 3 / 2 + title.Height + (subtitle.Length > 0 ? em + em / 3 : em / 6);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new LinearGradientBrush(r, Theme.Of(Role.BandTop), Theme.Of(Role.BandBottom), 10f))
            {
                var blend = new ColorBlend
                {
                    Colors = new[] { Theme.Of(Role.BandTop), Theme.Of(Role.BandMiddle), Theme.Of(Role.BandBottom) },
                    Positions = new[] { 0f, 0.6f, 1f }
                };
                brush.InterpolationColors = blend;
                g.FillRectangle(brush, r);
            }

            // The corner: 5/8 of the band's height along each edge, the liner below it.
            float leg = r.Height * 0.625f, x1 = r.Right, y1 = r.Bottom;
            using (var liner = new GraphicsPath())
            using (var brush = new SolidBrush(Theme.Of(Role.Liner)))
            {
                liner.AddPolygon(new[] { new PointF(x1 - leg, y1), new PointF(x1, y1 - leg), new PointF(x1, y1) });
                g.FillPath(brush, liner);
            }
            Brand.PaintFlap(g, x1, y1, leg, leg / 88f);

            // Both lines start where a normal Label's text starts on the window's left line: the gutter plus the text
            // inset of the normal font (TextRenderer pads text by a part of the font height; the large title's own inset
            // would put it further right). NoPadding, then that one inset added.
            int em = Font.Height, top = em * 3 / 4;
            int left = Brand.Gutter(em) + TextInset(Font);
            var flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
            int textWidth = Math.Max(0, (int)(r.Width - leg - left));
            // The title's box reaches down to the subtitle: a box of the font's height cut off the descenders (the "g" of
            // "Programme", user's report 2026-11-01), as NoPadding sets the text lower than the font's height allows for.
            using (var title = TitleFont())
            {
                int sub = top + title.Height + em / 3;
                TextRenderer.DrawText(g, Text, title, new Rectangle(left, top, textWidth, sub - top), Theme.Of(Role.OnPrimary), flags);
                if (subtitle.Length > 0)
                    TextRenderer.DrawText(g, subtitle, Font, new Rectangle(left, sub, textWidth, em), Theme.Of(Role.BandSubtitle), flags);
            }
        }

        // How far right of its edge a Label draws its text in this font: GDI's glyph overhang padding, a sixth of the
        // font height (MeasureText doesn't report it)
        static int TextInset(Font font) { return font.Height / 6; }
    }
}
