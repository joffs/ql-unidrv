// The programs' window fonts without GDI+'s table of every installed font. GDI+ builds that table in each process on
// its first font by name (new Font("Segoe UI", ...), SystemFonts.*, Control.DefaultFont) and opens every installed
// font file for it: seconds on a PC with many fonts after a restart (1811 files: 7.7 s, 2026-11-08). The window
// fonts come from a private collection instead, with only the few files of the faces asked for (found through the
// Fonts key in the registry), a few milliseconds. Drawing is unchanged: TextRenderer (GDI) picks the font by its
// name as before. A face that can't be found this way falls back to the usual installed font. The label's own
// fonts (QL Label Maker's font list, text on the label) still use the installed fonts.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    static class UiFont
    {
        // QL_UIFONT=off: the installed fonts as before (a way back if a PC shows a window font wrongly; tests compare)
        static readonly bool Off = string.Equals(Environment.GetEnvironmentVariable("QL_UIFONT"), "off", StringComparison.OrdinalIgnoreCase);
        static readonly object gate = new object();
        static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        static readonly Dictionary<string, FontFamily> families = new Dictionary<string, FontFamily>(StringComparer.OrdinalIgnoreCase);
        static readonly HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static string lastMiss = "";   // why the last face wasn't found (startup trace)

        // Instead of SystemFonts.MessageBoxFont: Windows' message font (face and size from SystemParametersInfo).
        public static Font MessageBox
        {
            get
            {
                string face; float points;
                if (Off || !MessageFont(out face, out points)) return SystemFonts.MessageBoxFont;
                return Create(face, points, FontStyle.Regular, GraphicsUnit.Point);
            }
        }

        // Instead of new Font(face, size, style, unit).
        public static Font Create(string face, float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            var family = Family(face);
            if (family != null && family.IsStyleAvailable(style))
                try { return new Font(family, size, style, unit); }
                catch (ArgumentException) { }
            return new Font(face, size, style, unit);
        }

        // Instead of new Font(FontFamily.GenericMonospace, size): Courier New, as GDI+ maps it.
        public static Font Monospace(float size) { return Create("Courier New", size); }

        // WinForms' own defaults (Control.DefaultFont = SystemFonts.DefaultFont, ToolStripManager.DefaultFont =
        // SystemFonts.MenuFont) set to private ones of the same face and size. Call first in Main, before any control.
        public static void UseForWindows()
        {
            if (Off) return;
            try
            {
                var control = typeof(Control).GetField("defaultFont", BindingFlags.Static | BindingFlags.NonPublic);
                Font dialog = Logical(GetStockObject(DEFAULT_GUI_FONT));
                if (control != null && dialog != null) control.SetValue(null, dialog);
                // traced: whether the window controls' font is private (else their first one loads GDI+'s table)
                StartupTrace.Mark("ui font: " + (control == null ? "no Control.defaultFont field" : dialog == null ? "dialog font NOT found (" + lastMiss + ")" : "dialog font " + dialog.Name + " " + dialog.SizeInPoints + (lastMiss != "" ? " (" + lastMiss + ")" : "")));
            }
            catch (Exception e) { StartupTrace.Mark("ui font: " + e.GetType().Name + " " + e.Message); }
            try
            {
                var strip = typeof(ToolStripManager).GetField("defaultFont", BindingFlags.Static | BindingFlags.NonPublic);
                string face; float points;
                if (strip != null && MenuFont(out face, out points)) strip.SetValue(null, Create(face, points));
                // Per-monitor DPI (app.config): ToolStripManager keeps a menu font per DPI, made with Font.FromLogFont
                // from SystemParametersInfoForDpi (in points of the screen DC). The same fonts, private, for the usual DPIs.
                var cache = typeof(ToolStripManager).GetField("defaultFontCache", BindingFlags.Static | BindingFlags.NonPublic);
                var fonts = cache != null ? cache.GetValue(null) as System.Collections.IDictionary : null;
                if (fonts != null)
                    foreach (int dpi in StripDpis)
                    {
                        var font = MenuFontForDpi(dpi);
                        if (font != null && !fonts.Contains(dpi)) fonts[dpi] = font;
                    }
            }
            catch (Exception e) { StartupTrace.Mark("ui font, menus: " + e.GetType().Name + " " + e.Message); }   // a later WinForms: its defaults as before
        }

        // The private family for a face, its regular, bold, italic and bold italic files; null when not found.
        static FontFamily Family(string face)
        {
            if (Off) return null;
            lock (gate)
            {
                FontFamily family;
                if (families.TryGetValue(face, out family)) return family;
                family = null;
                try
                {
                    // "MS Shell Dlg" (the dialog font) is only a substitute name: its real face
                    string real = face;
                    using (var subs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontSubstitutes"))
                    {
                        string to = subs != null ? subs.GetValue(face) as string : null;
                        if (!string.IsNullOrEmpty(to)) real = to.Split(',')[0].Trim();
                    }
                    // GDI's built-in mapping when the substitute entry is missing
                    if (real == face && string.Equals(face, "MS Shell Dlg", StringComparison.OrdinalIgnoreCase)) real = "Microsoft Sans Serif";
                    var files = Files(real).ToList();
                    foreach (var file in files)
                        if (added.Add(file))
                            try { fonts.AddFontFile(file); }
                            catch (Exception e) { lastMiss = real + ": " + Path.GetFileName(file) + " " + e.GetType().Name; }   // one broken file: the others
                    foreach (var f in fonts.Families)
                        if (string.Equals(f.Name, real, StringComparison.OrdinalIgnoreCase)) family = f;
                    if (family == null && files.Count == 0) lastMiss = real + ": no file in the Fonts keys";
                    else if (family == null) lastMiss = real + ": " + files.Count + " file(s), no family of that name";
                }
                catch (Exception e) { family = null; lastMiss = face + ": " + e.GetType().Name + " " + e.Message; }
                families[face] = family;
                return family;
            }
        }

        // The face's files from the Fonts keys (all users, then this user): "Segoe UI (TrueType)" = segoeui.ttf,
        // "Segoe UI Bold (TrueType)" = segoeuib.ttf, collections "A & B (TrueType)".
        static IEnumerable<string> Files(string face)
        {
            var found = new List<string>();
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
                using (var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts"))
                {
                    if (key == null) continue;
                    foreach (var name in key.GetValueNames())
                    {
                        int paren = name.LastIndexOf(" (", StringComparison.Ordinal);
                        string faces = paren > 0 ? name.Substring(0, paren) : name;
                        foreach (var one in faces.Split(new[] { " & " }, StringSplitOptions.None))
                        {
                            string rest = one.Trim();
                            if (!rest.StartsWith(face, StringComparison.OrdinalIgnoreCase)) continue;
                            rest = rest.Substring(face.Length).Trim();
                            if (rest != "" && rest != "Bold" && rest != "Italic" && rest != "Bold Italic") continue;
                            string file = key.GetValue(name) as string;
                            if (string.IsNullOrEmpty(file)) continue;
                            try { if (!Path.IsPathRooted(file)) file = Path.Combine(windows, file); }
                            catch (ArgumentException) { continue; }   // a value that isn't a path
                            if (File.Exists(file) && !found.Contains(file)) found.Add(file);
                        }
                    }
                }
            return found;
        }

        // ---------- Windows' UI fonts (GDI only) ----------

        static bool MessageFont(out string face, out float points) { return NonClient(true, out face, out points); }
        static bool MenuFont(out string face, out float points) { return NonClient(false, out face, out points); }

        static bool NonClient(bool message, out string face, out float points)
        {
            face = null; points = 0;
            var m = new NONCLIENTMETRICS();
            m.cbSize = Marshal.SizeOf(typeof(NONCLIENTMETRICS));
            if (!SystemParametersInfo(SPI_GETNONCLIENTMETRICS, m.cbSize, ref m, 0)) return false;
            var lf = message ? m.lfMessageFont : m.lfMenuFont;
            return FromLogFont(lf, out face, out points);
        }

        // Windows' scale steps 100 % to 300 % (a DPI not listed: WinForms makes its font itself, loading the table once)
        static readonly int[] StripDpis = { 96, 120, 144, 168, 192, 216, 240, 288 };

        // The menu font at a DPI as WinForms makes it (SystemInformation.GetMenuFontForDpi + FontInPoints)
        internal static Font MenuFontForDpi(int dpi)
        {
            var m = new NONCLIENTMETRICS();
            m.cbSize = Marshal.SizeOf(typeof(NONCLIENTMETRICS));
            try { if (!SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, m.cbSize, ref m, 0, dpi)) return null; }
            catch (EntryPointNotFoundException) { return null; }
            string face; float points;
            if (!FromLogFont(m.lfMenuFont, out face, out points)) return null;
            var family = Family(face);
            var style = (m.lfMenuFont.lfWeight >= 700 ? FontStyle.Bold : 0) | (m.lfMenuFont.lfItalic != 0 ? FontStyle.Italic : 0);
            return family != null && family.IsStyleAvailable(style) ? new Font(family, points, style, GraphicsUnit.Point, m.lfMenuFont.lfCharSet) : null;
        }

        static bool FromLogFont(LOGFONT lf, out string face, out float points)
        {
            face = lf.lfFaceName; points = 0;
            if (string.IsNullOrEmpty(face) || lf.lfHeight == 0) return false;
            IntPtr dc = GetDC(IntPtr.Zero);
            int dpi = dc != IntPtr.Zero ? GetDeviceCaps(dc, LOGPIXELSY) : 96;
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            points = Math.Abs(lf.lfHeight) * 72f / dpi;
            return true;
        }

        // A GDI font object (stock font) as a private GDI+ font of the same face and size.
        static Font Logical(IntPtr gdiFont)
        {
            var lf = new LOGFONT();
            if (gdiFont == IntPtr.Zero || GetObject(gdiFont, Marshal.SizeOf(typeof(LOGFONT)), ref lf) == 0) { lastMiss = "no stock font"; return null; }
            string face; float points;
            if (!FromLogFont(lf, out face, out points)) { lastMiss = "stock font '" + lf.lfFaceName + "' height " + lf.lfHeight; return null; }
            var family = Family(face);
            // its face not installed (Windows Sandbox has no Microsoft Sans Serif): GDI+ shows Arial for it, so Arial,
            // private (else the first control would load GDI+'s table to find that out)
            if (family == null) { family = Family("Arial"); if (family != null) lastMiss += "; Arial instead"; }
            return family != null ? new Font(family, points, FontStyle.Regular, GraphicsUnit.Point) : null;
        }

        const int SPI_GETNONCLIENTMETRICS = 0x29, LOGPIXELSY = 90, DEFAULT_GUI_FONT = 17;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct LOGFONT
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct NONCLIENTMETRICS
        {
            public int cbSize, iBorderWidth, iScrollWidth, iScrollHeight, iCaptionWidth, iCaptionHeight;
            public LOGFONT lfCaptionFont;
            public int iSmCaptionWidth, iSmCaptionHeight;
            public LOGFONT lfSmCaptionFont;
            public int iMenuWidth, iMenuHeight;
            public LOGFONT lfMenuFont, lfStatusFont, lfMessageFont;
            public int iPaddedBorderWidth;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(int action, int param, ref NONCLIENTMETRICS metrics, int ini);
        // An owner-drawn ListView's column headers: WinForms makes a GDI+ font of the header's font on every header paint
        // (ListView.GetListHeaderFont: Font.FromHfont), even when the header is drawn by default, which loads the
        // table. This list lets the header draw itself (CDRF_DODEFAULT, what DrawDefault = true asked for anyway);
        // in dark mode Theme's HeaderText sees the message first and sets its text colour as before.
        internal sealed class NativeHeaderListView : ListView
        {
            protected override void WndProc(ref Message m)
            {
                const int WM_NOTIFY = 0x4E, NM_CUSTOMDRAW = -12, LVM_GETHEADER = 0x101F;
                if (m.Msg == WM_NOTIFY && OwnerDraw && IsHandleCreated
                    && Marshal.ReadInt32(m.LParam, 2 * IntPtr.Size) == NM_CUSTOMDRAW
                    && Marshal.ReadIntPtr(m.LParam) == SendMessage(Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero))
                {
                    m.Result = IntPtr.Zero;   // CDRF_DODEFAULT
                    return;
                }
                base.WndProc(ref m);
            }
        }
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool SystemParametersInfoForDpi(int action, int param, ref NONCLIENTMETRICS metrics, int ini, int dpi);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int index);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int GetObject(IntPtr obj, int size, ref LOGFONT lf);
    }
}
