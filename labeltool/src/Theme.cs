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
        Caption,        // tinted title bar (Brand.TintCaption)
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

    static class Theme
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

        // The title bar in the Caption tone (Brand.TintCaption).
        public static void TintCaption(Form form)
        {
            RolesOf(form).Tinted = true;
            if (form.IsHandleCreated) Caption(form);
            else form.HandleCreated += delegate { Caption(form); };
        }

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
                if (updown != null && Dark) UpDownFrame.Hook(updown);
                if (updown != null) updown.Invalidate();
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

        [DllImport("gdi32.dll")] static extern int SetTextColor(IntPtr hdc, int color);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }

    // Menus, context menus, toolbars and status bars: Windows' look in light (the base renderer's own colours), ours in
    // dark. Shared by all programs (ToolStripManager.Renderer); MainForm's command row derives from it.
    class ThemeRenderer : ToolStripProfessionalRenderer
    {
        public ThemeRenderer() : base(new ThemeColors()) { }

        // Every toolbar, menu and status bar drawn by it: its item tips in the scheme (Theme.Tip).
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            Theme.TipOf(e.ToolStrip);
            base.OnRenderToolStripBackground(e);
        }

        // High Contrast: a checked button (e.g. "Fit") in Highlight with HighlightText; Windows' renderer gives it only a frame
        // around text in HighlightText, which nearly vanishes on the dark ground of Night sky and similar schemes.
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;
            if (Theme.HighContrast && button != null && button.Checked)
            {
                using (var fill = new SolidBrush(Theme.Of(Role.Selection))) e.Graphics.FillRectangle(fill, new Rectangle(Point.Empty, e.Item.Size));
                return;
            }
            base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;
            if (Theme.HighContrast && button != null && button.Checked && button.Enabled) e.TextColor = Theme.Of(Role.OnPrimary);
            if (Theme.Dark)
            {
                var t = e.TextColor;
                if (!e.Item.Enabled) e.TextColor = Theme.Of(Role.Dim);
                else if (t == SystemColors.ControlText || t == SystemColors.MenuText || t == SystemColors.WindowText || t == Theme.Fixed.Ink)
                    e.TextColor = Theme.Of(Role.Text);
            }
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            if (Theme.Dark) e.ArrowColor = e.Item != null && !e.Item.Enabled ? Theme.Of(Role.Dim) : Theme.Of(Role.Text);
            base.OnRenderArrow(e);
        }

        // The tick of a checked menu entry: Windows' black tick would vanish on dark.
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            if (!Theme.Dark) { base.OnRenderItemCheck(e); return; }
            var r = e.ImageRectangle;
            using (var back = new SolidBrush(Theme.Of(Role.Selection))) e.Graphics.FillRectangle(back, r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
            var mode = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var pen = new Pen(Theme.Of(Role.Text), Math.Max(1.5f, r.Height / 8f)))
                e.Graphics.DrawLines(pen, new[] { new PointF(r.Left + r.Width * .2f, r.Top + r.Height * .52f), new PointF(r.Left + r.Width * .42f, r.Top + r.Height * .74f),
                                                  new PointF(r.Left + r.Width * .8f, r.Top + r.Height * .28f) });
            e.Graphics.SmoothingMode = mode;
        }
    }

    // The renderer's colours: the base table in light (as before), the roles in dark and High Contrast stays Windows'.
    sealed class ThemeColors : ProfessionalColorTable
    {
        static bool Ours { get { return Theme.Dark; } }
        static Color R(Role role) { return Theme.Of(role); }

        public override Color MenuStripGradientBegin { get { return Ours ? R(Role.Window) : base.MenuStripGradientBegin; } }
        public override Color MenuStripGradientEnd { get { return Ours ? R(Role.Window) : base.MenuStripGradientEnd; } }
        public override Color ToolStripGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripGradientBegin; } }
        public override Color ToolStripGradientMiddle { get { return Ours ? R(Role.Window) : base.ToolStripGradientMiddle; } }
        public override Color ToolStripGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripGradientEnd; } }
        public override Color ToolStripBorder { get { return Ours ? R(Role.Window) : base.ToolStripBorder; } }
        public override Color ToolStripContentPanelGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripContentPanelGradientBegin; } }
        public override Color ToolStripContentPanelGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripContentPanelGradientEnd; } }
        public override Color ToolStripPanelGradientBegin { get { return Ours ? R(Role.Window) : base.ToolStripPanelGradientBegin; } }
        public override Color ToolStripPanelGradientEnd { get { return Ours ? R(Role.Window) : base.ToolStripPanelGradientEnd; } }
        public override Color OverflowButtonGradientBegin { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientBegin; } }
        public override Color OverflowButtonGradientMiddle { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientMiddle; } }
        public override Color OverflowButtonGradientEnd { get { return Ours ? R(Role.Window) : base.OverflowButtonGradientEnd; } }
        public override Color StatusStripGradientBegin { get { return Ours ? R(Role.Surface) : base.StatusStripGradientBegin; } }
        public override Color StatusStripGradientEnd { get { return Ours ? R(Role.Surface) : base.StatusStripGradientEnd; } }
        public override Color ToolStripDropDownBackground { get { return Ours ? R(Role.Surface) : base.ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientBegin { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientBegin; } }
        public override Color ImageMarginGradientMiddle { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientMiddle; } }
        public override Color ImageMarginGradientEnd { get { return Ours ? R(Role.Surface) : base.ImageMarginGradientEnd; } }
        public override Color MenuBorder { get { return Ours ? R(Role.Border) : base.MenuBorder; } }
        public override Color MenuItemBorder { get { return Ours ? R(Role.Hover) : base.MenuItemBorder; } }
        public override Color MenuItemSelected { get { return Ours ? R(Role.Hover) : base.MenuItemSelected; } }
        public override Color MenuItemSelectedGradientBegin { get { return Ours ? R(Role.Hover) : base.MenuItemSelectedGradientBegin; } }
        public override Color MenuItemSelectedGradientEnd { get { return Ours ? R(Role.Hover) : base.MenuItemSelectedGradientEnd; } }
        public override Color MenuItemPressedGradientBegin { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientBegin; } }
        public override Color MenuItemPressedGradientMiddle { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientMiddle; } }
        public override Color MenuItemPressedGradientEnd { get { return Ours ? R(Role.Surface) : base.MenuItemPressedGradientEnd; } }
        public override Color SeparatorDark { get { return Ours ? R(Role.Border) : base.SeparatorDark; } }
        public override Color SeparatorLight { get { return Ours ? R(Role.Surface) : base.SeparatorLight; } }
        public override Color ButtonSelectedHighlight { get { return Ours ? R(Role.Hover) : base.ButtonSelectedHighlight; } }
        public override Color ButtonSelectedGradientBegin { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientBegin; } }
        public override Color ButtonSelectedGradientMiddle { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientMiddle; } }
        public override Color ButtonSelectedGradientEnd { get { return Ours ? R(Role.Hover) : base.ButtonSelectedGradientEnd; } }
        public override Color ButtonSelectedBorder { get { return Ours ? R(Role.Accent) : base.ButtonSelectedBorder; } }
        public override Color ButtonPressedHighlight { get { return Ours ? R(Role.Selection) : base.ButtonPressedHighlight; } }
        public override Color ButtonPressedGradientBegin { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientBegin; } }
        public override Color ButtonPressedGradientMiddle { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientMiddle; } }
        public override Color ButtonPressedGradientEnd { get { return Ours ? R(Role.Selection) : base.ButtonPressedGradientEnd; } }
        public override Color ButtonPressedBorder { get { return Ours ? R(Role.Accent) : base.ButtonPressedBorder; } }
        public override Color ButtonCheckedHighlight { get { return Ours ? R(Role.Selection) : base.ButtonCheckedHighlight; } }
        public override Color ButtonCheckedGradientBegin { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientBegin; } }
        public override Color ButtonCheckedGradientMiddle { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientMiddle; } }
        public override Color ButtonCheckedGradientEnd { get { return Ours ? R(Role.Selection) : base.ButtonCheckedGradientEnd; } }
        public override Color CheckBackground { get { return Ours ? R(Role.Selection) : base.CheckBackground; } }
        public override Color CheckSelectedBackground { get { return Ours ? R(Role.Selection) : base.CheckSelectedBackground; } }
        public override Color CheckPressedBackground { get { return Ours ? R(Role.Selection) : base.CheckPressedBackground; } }
        public override Color GripDark { get { return Ours ? R(Role.Border) : base.GripDark; } }
        public override Color GripLight { get { return Ours ? R(Role.Window) : base.GripLight; } }
    }
}
