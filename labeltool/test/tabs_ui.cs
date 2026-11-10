// QL Label Maker's tab strip and close question on screen (0.23; DocTabStrip.cs, SaveChangesDialog.cs), in a test
// window shaped like the editor's middle (strip over a grey desk with a label): three tabs, one tab, nine long ones with
// the ▾ list open, the x under the mouse, a blink, and the close question with three and with 30 labels. Checks sizes
// (28 px at 100 %, 42 at 150 %), the active tab's place, colours from the scheme, the list's entries, the question's
// answers and its height (690 px). Pictures for the comparison with the mockup: tabs-<case>-<lang>-<scheme>-<dpi>.png.
// 150 %: when a screen of 144 dpi is attached, the window is moved there and everything runs again; otherwise that run
// says so (INFO) and is skipped. Prints CHECK lines; only through deskrun.exe; changes nothing of the user's.
// Run by tabs-ui.ps1:  tabs_ui.exe <de|en> <light|dark> <picture folder>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class TabsUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int failures;
        static string scheme, dir, dpiName = "100";

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point pt, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);

        [STAThread]
        static int Main(string[] args)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(150000);
                Console.WriteLine("CHECK watchdog: the test ended within 150 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args[0]);
            scheme = args[1];
            Theme.Force(scheme == "dark" ? Scheme.Dark : scheme == "highcontrast" ? Scheme.HighContrast : Scheme.Light);
            dir = args.Length > 2 ? args[2] : null;
            Application.EnableVisualStyles();
            try
            {
                var normal = Screen.AllScreens.FirstOrDefault(s => Dpi(s) == 96) ?? Screen.PrimaryScreen;
                RunAll(normal);
                var wide = Screen.AllScreens.FirstOrDefault(s => Dpi(s) == 144);
                if (wide != null) { dpiName = "150"; RunAll(wide); }
                else Console.WriteLine("INFO no screen at 150 % (144 dpi) attached: the 150 % run is skipped");
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + scheme : "DONE " + args[0] + " " + scheme + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static int Dpi(Screen s)
        {
            uint x, y;
            var m = MonitorFromPoint(new Point(s.Bounds.Left + 10, s.Bounds.Top + 10), 2);
            return GetDpiForMonitor(m, 0, out x, out y) == 0 ? (int)x : 96;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + scheme + " " + dpiName + "%: " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static void Call(object o, string method, params object[] a)
        {
            var m = o.GetType().GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            m.Invoke(o, a);
        }

        // The editor's middle in small: the strip over the desk, a white label on it.
        sealed class Host : ScaledForm
        {
            public readonly DocTabStrip Strip = new DocTabStrip();
            public readonly Panel Desk = new Panel { Dock = DockStyle.Fill };
            public Host()
            {
                Text = "QL Label Maker";
                Theme.Use(this);
                Font = UiFont.MessageBox;
                AutoScaleMode = AutoScaleMode.Dpi;
                FormBorderStyle = FormBorderStyle.FixedSingle;
                Theme.Back(Desk, Role.Desk);
                Desk.Paint += (s, e) =>
                {
                    int w = Math.Min(Desk.Width - LogicalToDeviceUnits(60), LogicalToDeviceUnits(600));
                    var r = new Rectangle((Desk.Width - w) / 2, LogicalToDeviceUnits(30), w, LogicalToDeviceUnits(150));
                    using (var b = new SolidBrush(Theme.Of(Role.Content))) e.Graphics.FillRectangle(b, r);
                };
                Controls.Add(Desk);
                Controls.Add(Strip);
            }
        }

        static string Folder { get { return Lang.Current == "de" ? @"C:\Users\Public\Documents\Etiketten\" : @"C:\Users\Public\Documents\Labels\"; } }

        static DocTabStrip.Tab T(string title, bool dirty = false, bool saved = true)
        {
            string printer = "Brother QL-800 (Unidrv)";
            return new DocTabStrip.Tab { Title = title, Dirty = dirty,
                ToolTip = (saved ? Folder + title + ".qllabel" : Lang.Current == "de" ? "Noch nicht gespeichert" : "Not saved yet") + "\n" + printer + " · 62 mm" };
        }

        static string Untitled(int n) { return (Lang.Current == "de" ? "Unbenannt " : "Untitled ") + n; }

        static void RunAll(Screen screen)
        {
            var host = new Host { StartPosition = FormStartPosition.Manual };
            host.Location = new Point(screen.WorkingArea.Left + 20, screen.WorkingArea.Top + 20);
            host.ClientSize = new Size(host.LogicalToDeviceUnits(940), host.LogicalToDeviceUnits(220));
            var strip = host.Strip;
            var selected = new List<int>();
            strip.TabSelected += i => { selected.Add(i); strip.Active = i; };
            host.Show();
            Pump(400);
            int dpi = host.DeviceDpi;
            Check(dpi == (dpiName == "150" ? 144 : 96), "window at " + dpi + " dpi");
            int px = (int)Math.Round(28 * dpi / 96.0);
            Check(strip.Height == px, "strip " + strip.Height + " px high (" + px + ")");

            // three labels, the first unsaved (the mockup's normal case)
            strip.Tabs.Clear();
            strip.Tabs.Add(T("Regal 3", true));
            strip.Tabs.Add(T(Untitled(1), false, false));
            strip.Tabs.Add(T("Versand Absender"));
            strip.Active = 0;
            Pump(150);
            Check(!strip.TabLayout.Overflow && strip.TabLayout.Visible.Count == 3, "three labels: all in the strip, no list");
            var r0 = strip.TabRect(0);
            Check(r0.Bottom == strip.Height && strip.TabRect(1).Top > r0.Top, "the active tab is higher and joins the label (" + r0 + ")");
            using (var b = Snap(strip))
            {
                Check(Near(b.GetPixel(r0.X + r0.Width / 2, r0.Y + 1), Theme.Of(Role.Accent)), "accent line on the active tab");
                var r1 = strip.TabRect(1);
                Check(Near(b.GetPixel(r1.X + 4, strip.Height - 4), Theme.Mix(Theme.Of(Role.Window), Theme.Dark ? Theme.Of(Role.Content) : Theme.Of(Role.Border), Theme.Dark ? 0.5 : 0.35)), "an idle tab in its tone");
                Check(Near(b.GetPixel(r0.X + 3, strip.Height - 1), Theme.Of(Role.Content)), "no line under the active tab");
            }
            Shot(host, null, "normal");

            // the x under the mouse on another tab
            var x2 = strip.XRect(2);
            Call(strip, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, x2.X + x2.Width / 2, x2.Y + x2.Height / 2, 0));
            Pump(100);
            Shot(host, null, "hover-x");
            Call(strip, "OnMouseLeave", EventArgs.Empty);

            // one label: the strip stays
            strip.Tabs.Clear();
            strip.Tabs.Add(T("Regal 3"));
            strip.Active = 0;
            Pump(100);
            Check(strip.Visible && strip.Height == px && strip.TabLayout.Visible.Count == 1, "one label: the strip stays, 28 px");
            Shot(host, null, "one");

            // already open: the blink
            strip.Tabs.Clear();
            foreach (var t in new[] { T("Regal 3", true), T(Untitled(1), false, false), T("Versand Absender"), T("Preise Aktion") }) strip.Tabs.Add(t);
            strip.Active = 0;
            strip.Blink(0);
            var w = Stopwatch.StartNew();   // right after Blink: the shot below may take a while on a cold run
            Pump(250);
            Check(strip.Blinking, "blink running");
            using (var b = Snap(strip))
            {
                var r = strip.TabRect(0);
                Check(Near(b.GetPixel(r.X + 4, r.Y + r.Height / 2), Theme.Of(Role.NoteLine), 40), "the blinking tab in the blink colour");
            }
            Shot(host, null, "blink");
            while (strip.Blinking && w.ElapsedMilliseconds < 8000) Pump(50);
            Check(!strip.Blinking && w.ElapsedMilliseconds >= 3000, "the blink ends after about 3 s (" + w.ElapsedMilliseconds + " ms)");

            // nine labels, long titles: shrunk, the list, the active tab in the strip
            strip.Tabs.Clear();
            var many = new[] { T("Regal 1"), T("Regal 2"), T("Regal 3", true), T("Versand Absender"), T("Lagerbeschriftung Halle 2 – Regalböden oben (neu)", true),
                               T("Preise Aktion"), T(Untitled(1), false, false), T("Kabel Netzwerkschrank"), T("Ordnerrücken 2026") };
            foreach (var t in many) strip.Tabs.Add(t);
            strip.Active = 6;
            Pump(100);
            var L = strip.TabLayout;
            Check(L.Overflow && L.Visible.Contains(6) && !strip.ListRect.IsEmpty, "nine labels: the list, the active tab in the strip");
            Check(L.Visible.All(i => strip.TabRect(i).Right <= strip.ListRect.Left) && L.Widths[4] < host.LogicalToDeviceUnits(DocTabStrip.MaxTab),
                  "tabs shrink and end before the list");
            var menu = strip.ShowList();
            Pump(200);
            var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
            Check(items.Count == 9 && items[6].Checked && items.Count(i => i.Checked) == 1, "the list: all nine, the active one ticked");
            Check(items[2].Text.EndsWith(" *") && items.Where((it, i) => !L.Visible.Contains(i)).All(it => it.ShortcutKeyDisplayString == Lang.T("not in the strip")),
                  "the list: * for unsaved, \"" + Lang.T("not in the strip") + "\" for the hidden ones");
            Shot(host, menu, "many");
            items[8].PerformClick();
            Pump(150);
            Check(selected.LastOrDefault() == 8 && strip.TabLayout.Visible.Contains(8), "picked from the list: TabSelected(8), now in the strip");
            menu.Close();

            // the close question: three labels, then 30
            var dialogItems = new List<SaveChangesDialog.Item>
            {
                new SaveChangesDialog.Item { Title = "Regal 3", Detail = Folder },
                new SaveChangesDialog.Item { Title = Untitled(1), Detail = Lang.Current == "de" ? "noch nicht gespeichert" : "not saved yet" },
                new SaveChangesDialog.Item { Title = "Versand Absender", Detail = Folder },
            };
            string note = Lang.Current == "de" ? "„" + Untitled(1) + "“ fragt danach nach Ordner und Namen." : "“" + Untitled(1) + "” asks for its folder and name next.";
            Ask(host, dialogItems, note, "close", d =>
            {
                Check(d.Height <= 690 * host.DeviceDpi / 96, "the question within 690 px (" + d.Height + ")");
                Check(d.Box(0).Checked && d.Box(1).Checked && d.Box(2).Checked, "all ticked");
                Check(d.ActiveControl == d.SaveButton, "Save has the focus");
                d.Box(1).Checked = false;
                d.SaveButton.PerformClick();
            }, DialogResult.Yes);
            Check(dialogItems[0].Save && !dialogItems[1].Save && dialogItems[2].Save, "Save with one unticked: Items[1].Save false");
            Ask(host, dialogItems, null, null, d => Call(d, "ProcessDialogKey", Keys.Escape), DialogResult.Cancel);
            Ask(host, dialogItems, null, null, d => d.DontSaveButton.PerformClick(), DialogResult.No);
            var thirty = Enumerable.Range(1, 30).Select(i => new SaveChangesDialog.Item { Title = (Lang.Current == "de" ? "Etikett " : "Label ") + i, Detail = Folder }).ToList();
            Ask(host, thirty, null, "close-30", d =>
            {
                Check(d.Height <= 690 * host.DeviceDpi / 96 && d.List.VerticalScroll.Visible, "30 labels: within 690 px, the list scrolls (" + d.Height + ")");
                d.CancelButtonControl.PerformClick();
            }, DialogResult.Cancel);

            host.Close();
            host.Dispose();
            Pump(100);
        }

        static void Ask(Form owner, IList<SaveChangesDialog.Item> items, string note, string picture, Action<SaveChangesDialog> act, DialogResult expected)
        {
            using (var d = new SaveChangesDialog(items) { Note = note })
            {
                var t = new System.Windows.Forms.Timer { Interval = 400 };
                t.Tick += delegate
                {
                    t.Stop();
                    if (picture != null) Shot(d, null, picture);
                    act(d);
                };
                d.Shown += delegate { t.Start(); };
                var result = d.ShowDialog(owner);
                t.Dispose();
                Check(result == expected, "close question answered " + result + " (" + expected + ")");
            }
        }

        static Bitmap Snap(Control c)
        {
            var b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(Point.Empty, c.Size));
            return b;
        }

        static bool Near(Color a, Color b, int tolerance = 12)
        {
            return Math.Abs(a.R - b.R) <= tolerance && Math.Abs(a.G - b.G) <= tolerance && Math.Abs(a.B - b.B) <= tolerance;
        }

        // The window (and a dropdown beside it) as a PNG for the comparison with the mockup.
        static void Shot(Form form, ToolStripDropDown drop, string name)
        {
            if (dir == null) return;
            var windows = new List<Control> { form };
            if (drop != null && drop.Visible) windows.Add(drop);
            var all = windows.Select(x => new Rectangle(x.Location, x.Size)).Aggregate(Rectangle.Union);
            using (var shot = new Bitmap(all.Width, all.Height))
            {
                using (var g = Graphics.FromImage(shot))
                    foreach (var x in windows)
                        using (var one = new Bitmap(x.Width, x.Height))
                        {
                            using (var wg = Graphics.FromImage(one)) { IntPtr dc = wg.GetHdc(); PrintWindow(x.Handle, dc, 2); wg.ReleaseHdc(dc); }
                            g.DrawImage(one, x.Left - all.Left, x.Top - all.Top);
                        }
                string file = Path.Combine(dir, "tabs-" + name + "-" + Lang.Current + "-" + scheme + "-" + dpiName + ".png");
                shot.Save(file, ImageFormat.Png);
                Console.WriteLine("PICTURE " + file);
            }
        }
    }
}
