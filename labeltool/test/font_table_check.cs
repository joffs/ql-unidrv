// Does a window load GDI+'s table of every installed font? Opens QL Label Formats' or QL Label Maker's window as their
// Main does, waits until it is idle, then times the first installed font by name (new FontFamily("Arial")): about as
// long as the table takes (hundreds of ms with many fonts) = the window didn't load it; ~0 ms = it did. Prints the
// time from process start to the window's Shown too. deskrun only (opens a window); saves and prints nothing.
//   font_table_check formats|maker|status [sample]   (sample: prints the stacks that ask GDI+ for a font)
// QL_FONTCHECK_SHOT=<png>: a picture of the window (PrintWindow) before the font check; with QL_UIFONT=off for the old fonts.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class FontTableCheck
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, int flags);
        [STAThread]
        static int Main(string[] args)
        {
            if (args[0] == "strip-compare") return StripCompare();
            if (args[0] == "logfont")
            {
                // The GDI font (LOGFONT) WinForms hands to controls, private vs installed (QL_UIFONT=off route)
                Func<Font, string> lf = f =>
                {
                    var o = new LOGFONTW(); f.ToLogFont(o);
                    return string.Format("h{0} w{1} weight{2} it{3} cs{4} out{5} clip{6} q{7} pf{8} '{9}' | size {10} pts {11} unit {12} height {13} gdiH {14}",
                        o.lfHeight, o.lfWidth, o.lfWeight, o.lfItalic, o.lfCharSet, o.lfOutPrecision, o.lfClipPrecision, o.lfQuality, o.lfPitchAndFamily, o.lfFaceName,
                        f.Size, f.SizeInPoints, f.Unit, f.Height, f.GetHeight());
                };
                Console.WriteLine("private   " + lf(UiFont.MessageBox));
                Console.WriteLine("installed " + lf(SystemFonts.MessageBoxFont));
                Console.WriteLine("private bold px   " + lf(UiFont.Create("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel)));
                Console.WriteLine("installed bold px " + lf(new Font("Segoe UI", 11f, FontStyle.Bold, GraphicsUnit.Pixel)));
                return 0;
            }
            var start = Process.GetCurrentProcess().StartTime;
            UiFont.UseForWindows();
            if (args.Length > 1 && args[1] == "sample") Sample(Thread.CurrentThread);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Form form = args[0] == "maker" ? (Form)new EditorForm(null, null, null) : args[0] == "formats" ? new MainForm()
                      : args[0] == "status" ? new StatusForm() : Plain(args[0]);
            form.Shown += delegate
            {
                Console.WriteLine("shown {0:0} ms after process start", (DateTime.Now - start).TotalMilliseconds);
                string shot = Environment.GetEnvironmentVariable("QL_FONTCHECK_SHOT");
                if (!string.IsNullOrEmpty(shot))
                {
                    for (var until = DateTime.Now.AddMilliseconds(1500); DateTime.Now < until; ) { Application.DoEvents(); Thread.Sleep(15); }
                    using (var bmp = new Bitmap(form.Width, form.Height))
                    {
                        using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); PrintWindow(form.Handle, dc, 2); g.ReleaseHdc(dc); }
                        bmp.Save(shot, System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
                var end = DateTime.Now.AddMilliseconds(args.Length > 1 && args[1] == "sample" ? 6500 : 2500);
                while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
                var clock = Stopwatch.StartNew();
                using (var f = new FontFamily("Arial")) { }
                long first = clock.ElapsedMilliseconds;
                clock.Restart();
                using (var f = new FontFamily("Tahoma")) { }
                Console.WriteLine("first installed font {0} ms, second {1} ms: {2}", first, clock.ElapsedMilliseconds,
                                  first > 5 * Math.Max(1, clock.ElapsedMilliseconds) && first > 40 ? "the window did NOT load the font table" : "the window LOADED the font table");
                form.Close();
            };
            Application.Run(form);
            return 0;
        }

        // Who asks for an installed font: samples the UI thread's stack every 15 ms for 6 s and prints each distinct stack
        // that is inside a GDI+ font call (the table takes hundreds of ms, so it shows up in the samples).
#pragma warning disable 618   // Thread.Suspend/Resume and StackTrace(Thread): deprecated, fine for a test
        static void Sample(Thread ui)
        {
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            var sampler = new Thread(() =>
            {
                var end = DateTime.Now.AddSeconds(6);
                while (DateTime.Now < end)
                {
                    Thread.Sleep(15);
                    System.Diagnostics.StackTrace st = null;
                    try { ui.Suspend(); try { st = new System.Diagnostics.StackTrace(ui, false); } finally { ui.Resume(); } }
                    catch (Exception) { continue; }
                    string text = st.ToString();
                    if (!text.Contains("System.Drawing.Font") && !text.Contains("Gdip")) continue;
                    var lines = text.Split('\n');
                    string key = string.Join("\n", lines, 0, Math.Min(lines.Length, 22));
                    int n; seen.TryGetValue(key, out n); seen[key] = n + 1;
                }
                foreach (var kv in seen) Console.WriteLine("=== " + kv.Value + " samples\n" + kv.Key);
            }) { IsBackground = true };
            sampler.Start();
        }
#pragma warning restore 618

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        class LOGFONTW
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        // UiFont's menu font per DPI against the one WinForms makes itself (ToolStripManager.DefaultFont with currentDpi set)
        static int StripCompare()
        {
            const System.Reflection.BindingFlags S = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
            var t = typeof(ToolStripManager);
            var cache = (System.Collections.IDictionary)t.GetField("defaultFontCache", S).GetValue(null);
            var current = t.GetField("currentDpi", S);
            var prop = t.GetProperty("DefaultFont", S);
            int bad = 0;
            foreach (int dpi in new[] { 96, 120, 144, 168, 192, 216, 240, 288 })
            {
                var ours = UiFont.MenuFontForDpi(dpi);
                cache.Remove(dpi);
                current.SetValue(null, dpi);
                var theirs = (Font)prop.GetValue(null, null);
                string a = ours == null ? "null" : ours.Name + " " + ours.SizeInPoints + " " + ours.Style + " cs" + ours.GdiCharSet;
                string b = theirs.Name + " " + theirs.SizeInPoints + " " + theirs.Style + " cs" + theirs.GdiCharSet;
                if (a != b) bad++;
                Console.WriteLine("{0}: {1} {2} {3}", dpi, a == b ? "same" : "DIFFERENT", a, a == b ? "" : "(WinForms: " + b + ")");
            }
            return bad;
        }

        // A plain window with one kind of control (to find which one asks for an installed font)
        static Form Plain(string kind)
        {
            var form = new Form { Font = UiFont.MessageBox, Text = kind };
            Control c;
            switch (kind)
            {
                case "empty": c = null; break;
                case "label": c = new Label { Text = "Regal 3", AutoSize = true }; break;
                case "button": c = new Button { Text = "OK" }; break;
                case "textbox": c = new TextBox { Text = "x" }; break;
                case "combo": c = new ComboBox(); break;
                case "list": c = new ListBox(); break;
                case "listview": c = new ListView { View = View.Details }; break;
                case "lv-columns": { var v = new ListView { View = View.Details, Dock = DockStyle.Fill }; v.Columns.Add("Name", 120); v.Items.Add("x"); c = v; break; }
                case "lv-fore": { var v = new ListView { View = View.Details, Dock = DockStyle.Fill, ForeColor = Color.DarkBlue }; v.Columns.Add("Name", 120); v.Items.Add("x"); c = v; break; }
                case "lv-theme": { var v = new ListView { View = View.Details, Dock = DockStyle.Fill }; v.Columns.Add("Name", 120); v.Items.Add("x"); Theme.Use(form); c = v; break; }
                case "updown": c = new NumericUpDown(); break;
                case "link": c = new LinkLabel { Text = "Link", AutoSize = true }; break;
                case "check": c = new CheckBox { Text = "x" }; break;
                case "menu": { var m = new MenuStrip(); m.Items.Add("Datei"); c = m; break; }
                case "toolstrip": { var t = new ToolStrip(); t.Items.Add("x"); t.Items.Add(new ToolStripComboBox()); c = t; break; }
                case "strip-empty": c = new ToolStrip(); break;
                case "strip-system": c = new ToolStrip { RenderMode = ToolStripRenderMode.System }; break;
                case "strip-hidden": { var t = new ToolStrip(); t.Visible = false; c = t; break; }
                case "strip-font": c = new ToolStrip { Font = UiFont.MessageBox }; break;
                case "strip-default":
                    {
                        var f = (Font)typeof(ToolStripManager).GetProperty("DefaultFont", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null, null);
                        Console.WriteLine("ToolStripManager.DefaultFont " + f.Name + " " + f.SizeInPoints);
                        c = null; break;
                    }
                case "status": { var s = new StatusStrip(); s.Items.Add(new ToolStripStatusLabel("x")); c = s; break; }
                case "tooltip": { var b = new Button { Text = "x" }; new ToolTip().SetToolTip(b, "tip"); c = b; break; }
                case "theme": Theme.Use(form); c = null; break;
                case "icon": form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); c = null; break;
                default: throw new ArgumentException(kind);
            }
            if (c != null) form.Controls.Add(c);
            return form;
        }
    }
}
