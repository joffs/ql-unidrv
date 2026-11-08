// The colours of all QL Unidrv windows, in one place: light, dark, and High Contrast (Windows' own colours).
//
// Every window colour is a Role (what it is for); the tables below give each role its colour per scheme. The windows
// never hold a colour of their own (test\check-colours.ps1 fails the build on a colour literal outside this file):
// - a control's colour: Theme.Fore / Theme.Back / Theme.Link with a role, so a switch can recolour it;
// - painting: Theme.Of(role) (or the properties below) at paint time;
// - fixed colours that don't change with the scheme (the label's paper and print, the mark, the state circles, the
//   header band) are the Fixed fields at the end.
//
// Which scheme: High Contrast always wins; else the user's choice (Extras > Darstellung, HKCU\Software\QLUnidrv, value
// Theme = light / dark, next to Language; missing = like Windows), else Windows' app mode (AppsUseLightTheme). All
// programs share the choice. It switches live: Theme.Use(form) colours a window when its handle is created, and a
// change of the setting, of Windows' mode or of High Contrast recolours every open window of the process (Apply).
// Windows' message boxes, Open/Save and the window menu (Alt+Space) stay as Windows draws them.
// Also in this class: Theme.Apply.cs (colouring windows and controls, native parts, caption, tooltips). The tool
// strip renderer: ThemeRenderer.cs.
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
    enum Scheme { Light, Dark, HighContrast }

    // What a colour is for.
    enum Role
    {
        Window,         // window background
        Content,        // lists, text boxes, the status window and About: the white areas in light
        Surface,        // footers, status bars, menu bar and menus
        SurfaceLine,    // the line above a footer
        Text,
        Dim,            // hints, captions, disabled text
        Border,         // frames of popups and our panels
        FieldLine,      // the frame of a number box in dark (as Windows' dark text box frame)
        Hover,          // a menu entry or button under the mouse
        Selection,      // the selected list row (with an Accent bar), pressed buttons
        Accent,         // links, the selection bar, (i) icons
        Heading,        // section headings (High Contrast: plain text)
        AccentHot,      // a link or accent under the mouse
        AccentPressed,  // a link while clicked
        AccentSoft,     // a pale accent: rings of the setup steps
        Primary,        // the main button's face
        PrimaryHot,
        PrimaryPressed,
        PrimaryLine,
        OnPrimary,      // text and symbols on Primary and on the state circles
        ButtonFace,     // the other buttons
        ButtonLine,
        DisabledFace,   // a disabled main button
        DisabledLine,
        ErrorText,      // text in the error colour
        WarningText,    // text in the warning colour
        InfoBack,       // tip bar, dimension tag
        InfoText,
        NoteBack,       // QL Label Maker's "outside the label" bar
        NoteLine,
        NoteText,
        Caption,        // tinted title bar (Theme.TintCaption)
        CaptionText,
        Desk,           // around the label in QL Label Maker
        DeskLine,       // guides and dashed lines on the desk
        SliderTrack,    // the zoom slider
        SliderTick,
        Liner,          // under the peeling corner of the header band (the window behind it)
        BandTop,        // the header band's gradient
        BandMiddle,
        BandBottom,
        BandSubtitle,
    }

    static partial class Theme
    {
        // ---- the tables ----

        static readonly Color[] LightTable = Table(new Dictionary<Role, Color>
        {
            { Role.Window, Rgb(0xF0F0F0) }, { Role.Content, Rgb(0xFFFFFF) }, { Role.Surface, Rgb(0xF7F9F9) }, { Role.SurfaceLine, Rgb(0xE5EAEA) },
            { Role.Text, Rgb(0x000000) }, { Role.Dim, Rgb(0x6D6D6D) }, { Role.Border, Rgb(0xC9D3D3) }, { Role.FieldLine, Rgb(0x7A7A7A) },
            { Role.Hover, Rgb(0xDCEFEE) }, { Role.Selection, Rgb(0xDCEFEE) },
            { Role.Accent, Rgb(0x0E7C84) }, { Role.Heading, Rgb(0x0E7C84) }, { Role.AccentHot, Rgb(0x0A636A) }, { Role.AccentPressed, Rgb(0x08474E) }, { Role.AccentSoft, Rgb(0xA9D3D3) },
            { Role.Primary, Rgb(0x0E7C84) }, { Role.PrimaryHot, Rgb(0x0A636A) }, { Role.PrimaryPressed, Rgb(0x08474E) }, { Role.PrimaryLine, Rgb(0x0E6F78) },
            { Role.OnPrimary, Rgb(0xFFFFFF) }, { Role.ButtonFace, Rgb(0xFFFFFF) }, { Role.ButtonLine, Rgb(0xB8C4C4) },
            { Role.DisabledFace, Rgb(0xF0F0F0) }, { Role.DisabledLine, Rgb(0xA0A0A0) },
            { Role.ErrorText, Rgb(0xD13438) }, { Role.WarningText, Rgb(0x9D5D00) },
            { Role.InfoBack, Rgb(0xFFFFE1) }, { Role.InfoText, Rgb(0x000000) },
            { Role.NoteBack, Rgb(0xFFF4E0) }, { Role.NoteLine, Rgb(0xF0C880) }, { Role.NoteText, Rgb(0x000000) },
            { Role.Caption, Rgb(0xE6F1F0) }, { Role.CaptionText, Rgb(0x1E2B2F) },
            { Role.Desk, Rgb(0xA0A0A0) }, { Role.DeskLine, Rgb(0x969696) }, { Role.SliderTrack, Rgb(0x8A8A8A) }, { Role.SliderTick, Rgb(0x505050) },
            { Role.Liner, Rgb(0xE6F1F0) },
            { Role.BandTop, Rgb(0x138E95) }, { Role.BandMiddle, Rgb(0x0E6F78) }, { Role.BandBottom, Rgb(0x0B5D65) }, { Role.BandSubtitle, Rgb(0xCDEDEC) },
        });

        static readonly Color[] DarkTable = Table(new Dictionary<Role, Color>
        {
            { Role.Window, Rgb(0x1C2021) }, { Role.Content, Rgb(0x15191A) }, { Role.Surface, Rgb(0x232829) }, { Role.SurfaceLine, Rgb(0x343C3D) },
            { Role.Text, Rgb(0xE6ECEC) }, { Role.Dim, Rgb(0x9AA6A7) }, { Role.Border, Rgb(0x3A4344) }, { Role.FieldLine, Rgb(0x9B9B9B) },
            { Role.Hover, Rgb(0x2E3637) }, { Role.Selection, Rgb(0x18393B) },
            { Role.Accent, Rgb(0x3DB7B6) }, { Role.Heading, Rgb(0x3DB7B6) }, { Role.AccentHot, Rgb(0x62CCCA) }, { Role.AccentPressed, Rgb(0x2A9A99) }, { Role.AccentSoft, Rgb(0x2C5557) },
            { Role.Primary, Rgb(0x0E7C84) }, { Role.PrimaryHot, Rgb(0x11909A) }, { Role.PrimaryPressed, Rgb(0x0A636A) }, { Role.PrimaryLine, Rgb(0x1A9097) },
            { Role.OnPrimary, Rgb(0xFFFFFF) }, { Role.ButtonFace, Rgb(0x2A3031) }, { Role.ButtonLine, Rgb(0x4A5556) },
            { Role.DisabledFace, Rgb(0x2A3031) }, { Role.DisabledLine, Rgb(0x4A5556) },
            { Role.ErrorText, Rgb(0xFF7B7D) }, { Role.WarningText, Rgb(0xFFB900) },
            { Role.InfoBack, Rgb(0x3A3520) }, { Role.InfoText, Rgb(0xF0E6C0) },
            { Role.NoteBack, Rgb(0x3A2E14) }, { Role.NoteLine, Rgb(0x7A5A1E) }, { Role.NoteText, Rgb(0xF0DDB0) },
            { Role.Caption, Rgb(0x172A2C) }, { Role.CaptionText, Rgb(0xE6ECEC) },
            { Role.Desk, Rgb(0x0F1213) }, { Role.DeskLine, Rgb(0x6A7475) }, { Role.SliderTrack, Rgb(0x7A8485) }, { Role.SliderTick, Rgb(0xB0BABA) },
            { Role.Liner, Rgb(0x1C2021) },
            { Role.BandTop, Rgb(0x0F767C) }, { Role.BandMiddle, Rgb(0x0B5F66) }, { Role.BandBottom, Rgb(0x084A50) }, { Role.BandSubtitle, Rgb(0xBFE3E2) },
        });

        // High Contrast: Windows' colours, read each time (the user may change them while a program runs).
        static Color HighContrastColor(Role role)
        {
            switch (role)
            {
                case Role.Window: case Role.Surface: case Role.ButtonFace: case Role.DisabledFace: case Role.Liner: return SystemColors.Control;
                case Role.Content: return SystemColors.Window;
                case Role.Text: case Role.Heading: case Role.ErrorText: case Role.WarningText: case Role.CaptionText: return SystemColors.ControlText;
                case Role.Dim: return SystemColors.GrayText;
                case Role.Hover: case Role.Selection: case Role.Primary: case Role.PrimaryHot: case Role.PrimaryPressed: return SystemColors.Highlight;
                case Role.OnPrimary: return SystemColors.HighlightText;
                case Role.Accent: case Role.AccentHot: case Role.AccentPressed: return SystemColors.HotTrack;
                case Role.InfoBack: case Role.NoteBack: return SystemColors.Info;
                case Role.InfoText: case Role.NoteText: return SystemColors.InfoText;
                case Role.Caption: return SystemColors.ActiveCaption;
                case Role.Desk: return SystemColors.AppWorkspace;
                case Role.BandTop: case Role.BandMiddle: case Role.BandBottom: return SystemColors.Highlight;
                case Role.BandSubtitle: return SystemColors.HighlightText;
                default: return SystemColors.ControlDarkDark;   // lines, frames, rings, the slider
            }
        }

        // ---- colours that don't change with the scheme ----

        public static class Fixed
        {
            public static readonly Color Paper = Rgb(0xFFFFFF);         // the label, the tape, test labels: as printed
            public static readonly Color Ink = Rgb(0x000000);           // the print, shadows (with an alpha)
            public static readonly Color Backing = Rgb(0xCDCDC8);       // the backing paper of die-cut labels
            public static readonly Color TapeOutside = Rgb(0xEBEBEB);   // tape beyond the printable area
            public static readonly Color TapeEdge = Rgb(0x5A5A5A);      // the edge of the tape and of die-cut labels
            public static readonly Color Crowded = Rgb(0xFFC860);       // a label element too close to the edge (with an alpha)
            public static readonly Color CrowdedLine = Rgb(0xE0A040);
            public static readonly Color PaperHint = Rgb(0x6D6D6D);     // grey text on the label (the empty label's hint)

            // The mark (icons, About, the header band's corner).
            public static readonly Color Petrol = Rgb(0x0E7C84);
            public static readonly Color PetrolLight = Rgb(0x23A6A8);
            public static readonly Color PetrolMid = Rgb(0x0E6F78);
            public static readonly Color PetrolDark = Rgb(0x08474E);
            public static readonly Color Liner = Rgb(0xE6F1F0);
            public static readonly Color Orange = Rgb(0xEC6A2C);
            public static readonly Color OrangeLight = Rgb(0xFFA062);
            public static readonly Color MarkWhite = Rgb(0xFFFFFF);

            // The state circles (status cards, the tray icons): the same in both schemes, with a white symbol.
            public static readonly Color StateOk = Rgb(0x0E7C84);
            public static readonly Color StateWarning = Rgb(0xFFB900);
            public static readonly Color StateError = Rgb(0xD13438);
            public static readonly Color StateUnknown = Rgb(0x8A9496);
            public static readonly Color StateSymbol = Rgb(0xFFFFFF);   // the tick, cross or mark on a circle
        }

        // Between a (t = 0) and b (t = 1).
        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static Color Rgb(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        static Color[] Table(Dictionary<Role, Color> colors)
        {
            var roles = (Role[])Enum.GetValues(typeof(Role));
            var missing = roles.Where(r => !colors.ContainsKey(r)).ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Theme: no colour for " + string.Join(", ", missing));
            var table = new Color[roles.Length];
            foreach (var c in colors) table[(int)c.Key] = c.Value;
            return table;
        }

        // ---- the scheme ----

        public const string SettingValue = "Theme";
        const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        static Scheme? current;

        public static Scheme Current { get { return current ?? (current = Resolve()).Value; } }
        public static bool Dark { get { return Current == Scheme.Dark; } }
        public static bool HighContrast { get { return Current == Scheme.HighContrast; } }

        // The colour of a role in the current scheme.
        public static Color Of(Role role)
        {
            switch (Current)
            {
                case Scheme.Dark: return DarkTable[(int)role];
                case Scheme.HighContrast: return HighContrastColor(role);
                default: return LightTable[(int)role];
            }
        }

        // For the tests: always this scheme, or null for the normal choice.
        public static void Force(Scheme? scheme) { forced = scheme; current = null; }
        static Scheme? forced;

        static Scheme Resolve()
        {
            if (forced != null) return forced.Value;
            if (SystemHighContrast) return Scheme.HighContrast;
            return (Setting ?? (SystemDark ? "dark" : "light")) == "dark" ? Scheme.Dark : Scheme.Light;
        }

        // Asked of Windows each time: SystemInformation.HighContrast is cached and may still hold the old state when
        // UserPreferenceChanged arrives (x64 Sandbox test: QL Printer Status lagged one switch behind).
        static bool SystemHighContrast
        {
            get
            {
                var hc = new HIGHCONTRAST { cbSize = Marshal.SizeOf(typeof(HIGHCONTRAST)) };
                try { return SystemParametersInfo(0x42 /* SPI_GETHIGHCONTRAST */, hc.cbSize, ref hc, 0) && (hc.dwFlags & 1 /* HCF_HIGHCONTRASTON */) != 0; }
                catch (Exception) { return SystemInformation.HighContrast; }
            }
        }

        [StructLayout(LayoutKind.Sequential)] struct HIGHCONTRAST { public int cbSize, dwFlags; public IntPtr lpszDefaultScheme; }
        [DllImport("user32.dll")] static extern bool SystemParametersInfo(int action, int param, ref HIGHCONTRAST value, int winIni);

        // The user's choice: "light", "dark", or null for like Windows.
        public static string Setting
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(Lang.SettingsKey))
                    {
                        string v = k != null ? Convert.ToString(k.GetValue(SettingValue)) : null;
                        return v == "light" || v == "dark" ? v : null;
                    }
                }
                catch (Exception) { return null; }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(Lang.SettingsKey))
                {
                    if (value == null) k.DeleteValue(SettingValue, false);
                    else k.SetValue(SettingValue, value);
                }
                Refresh();
            }
        }

        // Windows' mode for apps (Settings > Personalisation > Colours): AppsUseLightTheme = 0 is dark; missing is light.
        public static bool SystemDark
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(PersonalizeKey))
                        return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0;
                }
                catch (Exception) { return false; }
            }
        }

        // Raised on the UI thread after the scheme changed and every open window was recoloured; for what Apply can't
        // reach (pictures drawn once, colours chosen by state).
        public static event EventHandler Changed;

        // Reads the scheme again; recolours the open windows when it changed.
        public static void Refresh()
        {
            var before = current;
            current = null;
            if (before == Current) return;
            foreach (Form f in Application.OpenForms.Cast<Form>().ToArray()) Apply(f);
            var changed = Changed;
            if (changed != null) changed(null, EventArgs.Empty);
        }

        // ---- start ----

        static SynchronizationContext ui;
        static bool started;

        // Once per process with windows (Theme.Use calls it; the tray monitor too): the menus' renderer, and the watch
        // on the setting, on Windows' mode and on High Contrast. Call on the UI thread.
        public static void Start()
        {
            if (started) return;
            started = true;
            ToolStripManager.Renderer = new ThemeRenderer();
            ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Watch(Lang.SettingsKey);
            Watch(PersonalizeKey);
            SystemEvents.UserPreferenceChanged += delegate { Post(); };   // High Contrast on/off (and the colour mode)
        }

        static void Post() { ui.Post(delegate { Refresh(); }, null); }

        // Waits for registry change notifications on HKCU\path for the life of the process (no polling).
        static void Watch(string path)
        {
            RegistryKey key;
            try { key = Registry.CurrentUser.CreateSubKey(path); }
            catch (Exception) { return; }
            var signal = new AutoResetEvent(false);
            WaitOrTimerCallback wait = null;
            Func<bool> arm = () => RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
                                                           signal.SafeWaitHandle.DangerousGetHandle(), true) == 0;
            wait = delegate
            {
                Post();
                if (arm()) ThreadPool.RegisterWaitForSingleObject(signal, wait, null, -1, true);
            };
            if (arm()) ThreadPool.RegisterWaitForSingleObject(signal, wait, null, -1, true);
            lock (watched) watched.Add(key);   // the notification lives as long as the key is open
        }

        static readonly List<RegistryKey> watched = new List<RegistryKey>();
        const int RegNotifyChangeLastSet = 4, RegNotifyThreadAgnostic = 0x10000000;
        [DllImport("advapi32.dll")]
        static extern int RegNotifyChangeKeyValue(IntPtr key, bool subtree, int filter, IntPtr ev, bool async);

        // In every window's constructor: colours it in the current scheme when its handle is created (and its controls
        // added later, too), and again on every switch.
        public static void Use(Form form)
        {
            Start();
            form.HandleCreated += delegate { Apply(form); };
            if (form.IsHandleCreated) Apply(form);
        }

        // Extras > "Appearance" / "Darstellung": Like Windows (Light|Dark), Light, Dark, ticked from the stored setting. A
        // choice applies at once to every open QL program. High Contrast: greyed, Windows sets the colours.
        public static ToolStripMenuItem Menu()
        {
            var appearance = new ToolStripMenuItem(Lang.T("Appearance"));
            var auto = new ToolStripMenuItem("", null, delegate { Setting = null; });
            var light = new ToolStripMenuItem(Lang.T("Light"), null, delegate { Setting = "light"; });
            var dark = new ToolStripMenuItem(Lang.T("Dark"), null, delegate { Setting = "dark"; });
            appearance.DropDownItems.AddRange(new ToolStripItem[] { auto, new ToolStripSeparator(), light, dark });
            Action update = delegate
            {
                string setting = Setting;
                auto.Text = Lang.F("Like Windows ({0})", SystemDark ? Lang.T("Dark") : Lang.T("Light"));
                auto.Checked = setting == null;
                light.Checked = setting == "light";
                dark.Checked = setting == "dark";
                appearance.Enabled = !HighContrast;
                appearance.ToolTipText = HighContrast ? Lang.T("High Contrast is on: Windows sets the colours.")
                                                      : Lang.T("The colours of QL Label Formats, QL Label Maker, QL Printer Status and the setup.");
            };
            update();
            appearance.DropDownOpening += delegate { update(); };
            EventHandler changed = delegate { update(); };
            Changed += changed;
            appearance.Disposed += delegate { Changed -= changed; };
            return appearance;
        }

        // ---- roles of single controls ----

        sealed class Roles { public Role? Fore, Back, Link; public bool Primary, Tinted, Hooked, NativeHooked, Framed; public FlatStyle? Flat; public ToolStripRenderMode? Render; }
        static readonly ConditionalWeakTable<object, Roles> roles = new ConditionalWeakTable<object, Roles>();
        static Roles RolesOf(object o) { return roles.GetValue(o, k => new Roles()); }

        // The text colour of a control by role (e.g. a hint in Dim, an error in ErrorText). Returns the control.
        public static T Fore<T>(T control, Role role) where T : Control
        {
            RolesOf(control).Fore = role;
            control.ForeColor = Of(role);
            return control;
        }

        // The background of a control by role (e.g. the tip bar in InfoBack). Returns the control.
        public static T Back<T>(T control, Role role) where T : Control
        {
            RolesOf(control).Back = role;
            control.BackColor = Of(role);
            return control;
        }

        // A link in another colour than Accent (e.g. the status bar's printer link in Text).
        public static T Link<T>(T link, Role role) where T : LinkLabel
        {
            RolesOf(link).Link = role;
            StyleLink(link);
            return link;
        }

        // A status bar or toolbar link (ToolStripLabel with IsLink) in a role's colour, kept on a switch.
        public static T ItemLink<T>(T item, Role role) where T : ToolStripLabel
        {
            var r = RolesOf(item);
            bool first = r.Link == null;
            r.Link = role;
            Action set = delegate { item.LinkColor = item.ActiveLinkColor = item.VisitedLinkColor = Of(r.Link.Value); };
            set();
            if (first)
            {
                EventHandler changed = delegate { set(); };
                Changed += changed;
                item.Disposed += delegate { Changed -= changed; };
            }
            return item;
        }

        // A status bar item's text colour (the window sets it again on a switch, Theme.Changed).
        public static void ItemFore(ToolStripItem item, Role role) { item.ForeColor = Of(role); }

        // A list row's text colour (the window sets it again on a switch, Theme.Changed).
        public static void ItemFore(ListViewItem item, Role role)
        {
            var color = Of(role);
            if (item.ForeColor != color) item.ForeColor = color;
        }

        // The window's main action (Apply, Print, Install, Done): Primary with white text, grey when disabled. High
        // Contrast keeps Windows' button.
        public static void Primary(Button button)
        {
            var r = RolesOf(button);
            if (!r.Primary) button.EnabledChanged += delegate { StylePrimary(button); };
            r.Primary = true;
            StylePrimary(button);
        }

        // Optional: the title bar in the Caption tone (light: the liner tone with dark text, also in Windows' dark mode).
        public static void TintCaption(Form form)
        {
            RolesOf(form).Tinted = true;
            if (form.IsHandleCreated) Caption(form);
            else form.HandleCreated += delegate { Caption(form); };
        }
    }
}
