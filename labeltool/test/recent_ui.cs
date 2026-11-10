// QL Label Maker: File > Recent labels (EditorForm.Recent.cs, RecentLabels.cs). Checks the empty menu, the order
// (newest first, opened and saved labels, an entry again moves to the top), at most 15 with the digits 1-9 as access keys,
// two labels of the same name told apart by their folder, the x (mouse messages; the menus stay open), Del on the marked
// entry, a digit opening its label, "not found" with its question (Remove / Cancel), "Clear list..." with its question,
// a network folder not looked at when the menu opens and "folder not reachable" after a click (Retry, nothing removed by
// itself), the submenu within 690 px. Prints CHECK lines. Works in a folder of its own under %TEMP% (removed afterwards)
// and a registry key of its own (removed afterwards): the user's list stays untouched. Runs only through deskrun.exe (a
// desktop of its own: no mouse, no focus taken). A watchdog ends it after 120 s. With a picture folder: the open menu as
// recent-<lang>-<scheme>.png (a made-up list, as in the mockup).
// Run by recent-ui.ps1:  recent_ui.exe <de|en> <light|dark|highcontrast> [<picture folder>]
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
    static class RecentUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

        static EditorForm form;
        static int failures;
        static string scheme, folder;
        static readonly List<string> asked = new List<string>();
        static readonly Queue<DialogResult> answers = new Queue<DialogResult>();

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        [STAThread]
        static int Main(string[] args)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(120000);
                Console.WriteLine("CHECK watchdog: the test ended within 120 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args[0]);
            scheme = args[1];
            Theme.Force(scheme == "dark" ? Scheme.Dark : scheme == "highcontrast" ? Scheme.HighContrast : Scheme.Light);
            string key = RecentLabels.Key;
            RecentLabels.Key = Lang.SettingsKey + @"\UiTest-" + Guid.NewGuid().ToString("N");
            folder = Path.Combine(Path.GetTempPath(), "ql-recent-ui-" + Guid.NewGuid().ToString("N"));
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            form.RecentAsk = (headline, text, choices) =>
            {
                asked.Add(headline + " | " + string.Join("/", choices.Select(c => c.Key)));
                return answers.Count > 0 ? answers.Dequeue() : DialogResult.Cancel;
            };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(args.Length > 2 ? args[2] : null); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                form.Close();
            };
            try
            {
                form.Show();
                while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(RecentLabels.Key, false); } catch (Exception) { }
                RecentLabels.Key = key;
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + scheme : "DONE " + args[0] + " " + scheme + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + scheme + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static object Call(string method, params object[] a)
        {
            // optional parameters left out: their defaults (OpenFile's printer)
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length >= a.Length &&
                                                                  x.GetParameters().Skip(a.Length).All(q => q.IsOptional));
            return m.Invoke(form, a.Concat(m.GetParameters().Skip(a.Length).Select(q => q.DefaultValue)).ToArray());
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static ToolStripMenuItem FileMenu { get { return (ToolStripMenuItem)F<MenuStrip>("menuBar").Items[0]; } }
        static ToolStripMenuItem Recent { get { return F<ToolStripMenuItem>("recentMenu"); } }
        static List<EditorForm.RecentItem> Items { get { return Recent.DropDown.Items.OfType<EditorForm.RecentItem>().ToList(); } }

        static void OpenMenu()
        {
            if (!FileMenu.DropDown.Visible) FileMenu.ShowDropDown();
            Pump(50);
            if (Recent.DropDown.Visible) Recent.HideDropDown();
            Recent.ShowDropDown();
            Pump(80);
        }

        static void CloseMenus() { Recent.HideDropDown(); FileMenu.HideDropDown(); Pump(50); }

        static string NewLabel(string sub, string name)
        {
            string dir = Path.Combine(folder, sub);
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, name + LabelDoc.Extension);
            new LabelDoc { FormName = "62mm", PaperId = 259 }.Save(p);
            return p;
        }

        static IntPtr Lp(Point p) { return (IntPtr)((p.Y << 16) | (p.X & 0xFFFF)); }

        // The mouse over an entry (a menu ignores mouse messages while the real cursor hasn't moved since it opened, and the
        // test never moves it): marked, and its mouse move at that point of the menu.
        static void Hover(ToolStripItem item, Point at)
        {
            item.Select();
            var b = item.Bounds;
            typeof(ToolStripItem).GetMethod("OnMouseMove", All).Invoke(item, new object[] { new MouseEventArgs(MouseButtons.None, 0, at.X - b.X, at.Y - b.Y, 0) });
            Pump(60);
        }

        static void Run(string pictures)
        {
            Pump(800);

            // ---------- empty ----------
            OpenMenu();
            var first = Recent.DropDown.Items.Cast<ToolStripItem>().ToList();
            Check(first.Count == 1 && !first[0].Enabled && first[0].Text == Lang.T("No labels opened yet"), "empty: one grey row, no Clear list");
            Check(FileMenu.DropDownItems.IndexOf(Recent) == FileMenu.DropDownItems.IndexOf(FileMenu.DropDownItems.Cast<ToolStripItem>().First(i => i.Text == Lang.T("&Open..."))) + 1,
                  "the submenu right under Open...");
            CloseMenus();

            // ---------- order, at most 15, digits, the same name in two folders ----------
            var files = new List<string>();
            for (int i = 1; i <= 16; i++) files.Add(NewLabel(i % 2 == 0 ? "Lager" : "Büro", "Etikett " + i));
            files.Add(NewLabel("Alt", "Etikett 16"));   // the same name as Lager\Etikett 16
            foreach (var p in files) Call("OpenFile", p);
            var list = RecentLabels.Load();
            Check(list.Count == 15 && list[0] == files[16] && list[1] == files[15] && list[14] == files[2], "opened labels: newest first, at most 15");
            OpenMenu();
            var items = Items;
            Check(items.Count == 15 && items[0].Text == "&1 Etikett 16" && items[8].Text.StartsWith("&9 ") && items.Skip(9).All(i => !i.Text.StartsWith("&")),
                  "digits 1-9 as access keys, the 10th to 15th without: " + string.Join(", ", items.Select(i => i.Text)));
            Check(items[0].AccessibleName.Contains(Path.Combine(folder, "Alt")) && items[1].AccessibleName.Contains(Path.Combine(folder, "Lager")) && items.All(i => i.Mark == null),
                  "two labels of the same name: their folders tell them apart");
            Check(Recent.DropDown.Items.OfType<ToolStripMenuItem>().Last().Text == Lang.T("Clear &list..."), "Clear list... at the bottom");
            // 15 entries and Clear list: within 690 px here and at 125 % (the menu's rows grow with the scaling)
            int h125 = (int)Math.Ceiling(Recent.DropDown.Height * 120.0 / Recent.DropDown.DeviceDpi);
            Check(Recent.DropDown.Height <= 690 && h125 <= 690 && Recent.DropDown.Width < 900,
                  "the submenu with 15 entries within 690 px: " + Recent.DropDown.Size + " at " + Recent.DropDown.DeviceDpi + " dpi, " + h125 + " px at 125 %");
            CloseMenus();
            Call("OpenFile", files[5]);
            Check(RecentLabels.Load()[0] == files[5] && RecentLabels.Load().Count(x => x == files[5]) == 1, "opened again: to the top, once");
            string saved = Path.Combine(folder, "Lager", "Neu gespeichert" + LabelDoc.Extension);
            typeof(EditorForm).GetField("path", All).SetValue(form, saved);
            Call("Save", false);
            Check(File.Exists(saved) && RecentLabels.Load()[0] == saved, "a saved label: at the top");

            // ---------- x: removed, the menus stay open ----------
            OpenMenu();
            var victim = Items[2];
            string victimPath = victim.Path;
            var b = victim.Bounds;
            var at = new Point(b.Right - Recent.DropDown.Font.Height, b.Top + b.Height / 2);
            var h = Recent.DropDown.Handle;
            Hover(victim, at);
            Check(victim.Selected && victim.OverX && victim.ToolTipText == Lang.T("Remove from the list (Del)"), "hover over the x: marked, the x's tip");
            SendMessage(h, WM_LBUTTONDOWN, (IntPtr)1, Lp(at));
            SendMessage(h, WM_LBUTTONUP, IntPtr.Zero, Lp(at));
            Pump(150);
            Check(!RecentLabels.Load().Contains(victimPath) && Items.All(i => i.Path != victimPath), "x: the entry is removed");
            Check(Recent.DropDown.Visible && FileMenu.DropDown.Visible, "x: the menus stay open");
            Check(F<ToolStripStatusLabel>("labelStatus").Text == Lang.F("“{0}” removed from the list.", Path.GetFileNameWithoutExtension(victimPath)), "x: said in the status bar");
            Check(F<string>("path") != victimPath, "x: the label isn't opened");

            // ---------- Del on the marked entry ----------
            OpenMenu();
            Items[0].Select();
            string top = Items[0].Path, second = Items[1].Path;
            bool handled = (bool)Recent.DropDown.GetType().GetMethod("ProcessDialogKey", All).Invoke(Recent.DropDown, new object[] { Keys.Delete });
            Pump(80);
            Check(handled && !RecentLabels.Load().Contains(top) && Recent.DropDown.Visible, "Del: the marked entry removed, the menu stays open");
            Check(Items.Count > 0 && Items[0].Path == second && Items[0].Selected, "Del: the next entry is marked");

            // ---------- a digit opens its label ----------
            string third = Items[2].Path;
            typeof(ToolStrip).GetMethod("ProcessMnemonic", All).Invoke(Recent.DropDown, new object[] { '3' });
            Pump(200);
            Check(F<string>("path") == third && RecentLabels.Load()[0] == third, "the digit 3 opens the third label (now at the top)");
            CloseMenus();

            // ---------- not found ----------
            string gone = RecentLabels.Load()[1];
            File.Delete(gone);
            OpenMenu();
            var goneItem = Items.First(i => i.Path == gone);
            Check(goneItem.Mark == Lang.T("not found"), "a missing file: grey with \"not found\" when the menu opens");
            asked.Clear();
            answers.Enqueue(DialogResult.Cancel);
            goneItem.PerformClick();
            Pump(100);
            Check(asked.Count == 1 && asked[0].StartsWith(Lang.F("“{0}” was not found.", Path.GetFileNameWithoutExtension(gone))) && asked[0].EndsWith(Lang.T("Remove") + "/" + Lang.T("Cancel")),
                  "a click on it asks (Remove / Cancel): " + string.Join(" ; ", asked));
            Check(RecentLabels.Load().Contains(gone), "Cancel: it stays in the list");
            OpenMenu();
            answers.Enqueue(DialogResult.OK);
            Items.First(i => i.Path == gone).PerformClick();
            Pump(100);
            Check(!RecentLabels.Load().Contains(gone), "Remove: it leaves the list");

            // ---------- a network folder: not looked at when the menu opens; not reachable after a click ----------
            string net = @"\\ql-recent-test-no-server\labels\Ordnerrücken" + LabelDoc.Extension;
            RecentLabels.Add(net);
            EditorForm.ProbeSeconds = 3;
            var open = Stopwatch.StartNew();
            OpenMenu();
            Check(open.ElapsedMilliseconds < 1500 && Items[0].Path == net && Items[0].Mark == null, "a network entry: not looked at when the menu opens (" + open.ElapsedMilliseconds + " ms)");
            asked.Clear();
            answers.Enqueue(DialogResult.Retry);
            answers.Enqueue(DialogResult.Cancel);
            Items[0].PerformClick();
            var wait = Stopwatch.StartNew();
            while (asked.Count < 2 && wait.ElapsedMilliseconds < 20000) Pump(50);
            string unreachable = Lang.F("The folder {0} can't be reached right now.", Path.GetDirectoryName(net));
            Check(asked.Count == 2 && asked.All(a => a.StartsWith(unreachable) && a.EndsWith(Lang.T("Retry") + "/" + Lang.T("Remove") + "/" + Lang.T("Cancel"))),
                  "not reachable: asks (Retry / Remove / Cancel), Retry looks again: " + string.Join(" ; ", asked));
            Check(RecentLabels.Load().Contains(net), "not reachable: nothing removed by itself");
            OpenMenu();
            Check(Items.First(i => i.Path == net).Mark == Lang.T("folder not reachable"), "after the click: \"folder not reachable\"");
            CloseMenus();

            // ---------- Clear list... asks ----------
            OpenMenu();
            var clear = Recent.DropDown.Items.OfType<ToolStripMenuItem>().Last();
            asked.Clear();
            answers.Enqueue(DialogResult.Cancel);
            clear.PerformClick();
            Pump(80);
            Check(asked.Count == 1 && asked[0].StartsWith(Lang.T("Clear the list of recently opened labels?")) && asked[0].EndsWith(Lang.T("Clear") + "/" + Lang.T("Cancel")) && RecentLabels.Load().Count > 0,
                  "Clear list...: asks (Clear / Cancel); Cancel keeps the list");
            OpenMenu();
            answers.Enqueue(DialogResult.OK);
            Recent.DropDown.Items.OfType<ToolStripMenuItem>().Last().PerformClick();
            Pump(80);
            Check(RecentLabels.Load().Count == 0 && F<ToolStripStatusLabel>("labelStatus").Text == Lang.T("List cleared."), "Clear: the list is empty, said in the status bar");
            OpenMenu();
            Check(Recent.DropDown.Items.Count == 1 && !Recent.DropDown.Items[0].Enabled, "cleared: the grey row again");
            CloseMenus();

            if (pictures != null) Picture(pictures);
        }

        // The open menu with a made-up list as in the mockup (X: and a server: network, not looked at; E: missing).
        static void Picture(string dir)
        {
            var shown = new[]
            {
                @"X:\Lager\Etiketten\Regal 3.qllabel", @"X:\Lager\Etiketten\Regal 4.qllabel", @"\\server\buero\Vorlagen\Versandetikett.qllabel",
                @"\\server\buero\Vorlagen\Ordnerrücken breit.qllabel", @"E:\Etiketten-QL-Test-fehlt\Kabel 12 mm.qllabel",
                @"X:\Lager\Etiketten\Alt\Regal 3.qllabel", @"X:\Lager\Etiketten\Typenschild.qllabel"
            };
            foreach (var p in shown.Reverse()) RecentLabels.Add(p);
            OpenMenu();
            var hot = Items[0];
            var b = hot.Bounds;
            var at = new Point(b.Right - Recent.DropDown.Font.Height, b.Top + b.Height / 2);
            Hover(hot, at);
            Pump(100);
            var windows = new Control[] { form, FileMenu.DropDown, Recent.DropDown };
            var all = windows.Select(w => new Rectangle(w.Location, w.Size)).Aggregate(Rectangle.Union);
            all.Intersect(new Rectangle(form.Location, new Size(form.Width + Recent.DropDown.Width, form.Height)));
            using (var shot = new Bitmap(all.Width, all.Height))
            {
                using (var g = Graphics.FromImage(shot))
                    foreach (var w in windows)
                        using (var one = new Bitmap(w.Width, w.Height))
                        {
                            using (var wg = Graphics.FromImage(one)) { IntPtr dc = wg.GetHdc(); PrintWindow(w.Handle, dc, 2); wg.ReleaseHdc(dc); }
                            g.DrawImage(one, w.Left - all.Left, w.Top - all.Top);
                        }
                string file = Path.Combine(dir, "recent-" + Lang.Current + "-" + scheme + ".png");
                shot.Save(file, ImageFormat.Png);
                Console.WriteLine("PICTURE " + file);
            }
            Check(Items.Count == 7 && Items.First(i => i.Path.StartsWith(@"E:\")).Mark == Lang.T("not found") && Items.Where(i => !i.Path.StartsWith(@"E:\")).All(i => i.Mark == null),
                  "picture list: E: not found, the network entries unmarked");
            CloseMenus();
        }
    }
}
