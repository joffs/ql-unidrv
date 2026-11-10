// Unit test for QL Label Maker's tab strip and close question (0.23; DocTabStrip.cs, SaveChangesDialog.cs), no window
// shown: the layout (natural widths up to the maximum, shrinking to the minimum, the overflow list, the active tab
// always in the strip; also 2000 random cases against these rules), the blink's timing, the strip's hit test and its
// events (click, x, middle click, +, right click, keys), and the close question's list (rows, ticks, answers).
// Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class TabsTest
    {
        static int failures;
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        [STAThread]
        static int Main()
        {
            Lang.Force("en");
            Theme.Force(Scheme.Light);
            Layout();
            Random();
            Blink();
            Strip();
            Height();
            Dialog();
            if (failures > 0) { Console.WriteLine(failures + " tabs test(s) FAILED"); return 1; }
            Console.WriteLine("tabs_test: all OK");
            return 0;
        }

        static string Show(DocTabLayout.Result r) { return "[" + string.Join(",", r.Visible) + "] w=" + string.Join(",", r.Widths) + (r.Overflow ? " over" : ""); }

        static void Layout()
        {
            // three short tabs: as wide as they want, no list
            var r = DocTabLayout.Compute(new[] { 90, 120, 150 }, 0, 800, 210, 110, 60);
            Check(!r.Overflow && r.Visible.SequenceEqual(new[] { 0, 1, 2 }) && r.Widths.SequenceEqual(new[] { 90, 120, 150 }), "three short tabs: " + Show(r));
            // a long title: at most 210
            r = DocTabLayout.Compute(new[] { 90, 400 }, 1, 800, 210, 110, 60);
            Check(r.Widths[1] == 210 && !r.Overflow, "long title capped at 210: " + Show(r));
            // they fit only shrunk: each avail / n, not below the natural width
            r = DocTabLayout.Compute(new[] { 200, 200, 200, 200 }, 0, 640, 210, 110, 60);
            Check(!r.Overflow && r.Widths.All(w => w == 160), "four shrunk to 160: " + Show(r));
            Check(DocTabLayout.Compute(new[] { 80, 200, 200, 200 }, 0, 640, 210, 110, 60).Widths[0] == 80, "a short tab keeps its natural width when the others shrink");
            // nine tabs in 600 px (the mockup's "Viele"): minimum 110, overflow, the list's room kept
            var nine = new[] { 90, 90, 100, 150, 210, 120, 110, 160, 130 };
            r = DocTabLayout.Compute(nine, 0, 600, 210, 110, 60);
            Check(r.Overflow && r.Visible.SequenceEqual(new[] { 0, 1, 2, 3, 4 }) && r.Widths.Skip(3).All(w => w == 110), "nine tabs, active first: shrunk to 110, the first five, then the list: " + Show(r));
            Check(r.Visible.Sum(i => r.Widths[i]) <= 600 - 60, "the list's room kept: " + Show(r));
            // the active tab beyond the strip: it replaces the last ones
            r = DocTabLayout.Compute(nine, 6, 600, 210, 110, 60);
            Check(r.Overflow && r.Visible.Contains(6) && r.Visible.Last() == 6 && r.Visible.Sum(i => r.Widths[i]) <= 540, "active tab 7 shown at the end: " + Show(r));
            // the last tab needs no room for the list when it fits
            r = DocTabLayout.Compute(new[] { 100, 100, 100 }, 0, 300, 210, 110, 60);
            Check(!r.Overflow && r.Visible.Count == 3, "exactly full: no list: " + Show(r));
            // all fit, but the first ones plus the list's room wouldn't: still all shown, no list (review 0.23)
            r = DocTabLayout.Compute(new[] { 100, 170, 25 }, 0, 300, 210, 110, 60);
            Check(!r.Overflow && r.Visible.SequenceEqual(new[] { 0, 1, 2 }), "all fit with a short last tab: no list: " + Show(r));
            // far too narrow: the active one alone
            r = DocTabLayout.Compute(new[] { 150, 150, 150 }, 2, 80, 210, 110, 60);
            Check(r.Visible.SequenceEqual(new[] { 2 }) && r.Overflow, "too narrow: only the active one: " + Show(r));
            // one tab, no tabs
            Check(DocTabLayout.Compute(new[] { 120 }, 0, 500, 210, 110, 60).Visible.SequenceEqual(new[] { 0 }), "one tab");
            r = DocTabLayout.Compute(new int[0], -1, 500, 210, 110, 60);
            Check(r.Visible.Count == 0 && !r.Overflow, "no tabs: " + Show(r));
        }

        // The rules on random strips: the active tab always shows, the shown tabs fit (with the list's room when some
        // are left out), no width above the maximum, the shown ones in order, the left-out ones only in the list.
        static void Random()
        {
            var rnd = new Random(20261110);
            for (int k = 0; k < 2000; k++)
            {
                int n = rnd.Next(1, 25);
                var nat = Enumerable.Range(0, n).Select(_ => rnd.Next(50, 400)).ToArray();
                int active = rnd.Next(n), avail = rnd.Next(150, 1600), list = 60;
                var r = DocTabLayout.Compute(nat, active, avail, 210, 110, list);
                string what = "random " + k + " n=" + n + " active=" + active + " avail=" + avail + ": " + Show(r);
                Check(r.Visible.Contains(active), "the active tab shows, " + what);
                Check(r.Widths.All(w => w <= 210) && r.Widths.Select((w, i) => w <= nat[i]).All(x => x), "widths at most natural and 210, " + what);
                Check(r.Visible.Zip(r.Visible.Skip(1), (a, b) => a < b).All(x => x), "shown in order, " + what);
                int used = r.Visible.Sum(i => r.Widths[i]);
                if (r.Visible.Count > 1) Check(used <= avail - (r.Overflow ? list : 0), "the shown tabs fit, " + what);
                Check(r.Overflow == (r.Visible.Count < n), "overflow exactly when tabs are left out, " + what);
                if (r.Widths.Sum() <= avail) Check(!r.Overflow, "all fit: all shown, " + what);
                Check(r.Widths.Select((w, i) => w >= Math.Min(nat[i], 110)).All(x => x), "no tab below the minimum (unless it wants less), " + what);
            }
        }

        static void Blink()
        {
            Check(DocTabStrip.BlinkAmount(0) == 1 && DocTabStrip.BlinkAmount(400) == 1, "blink: full at the start of a pulse");
            Check(Math.Abs(DocTabStrip.BlinkAmount(1600 - 1) - 0) < 0.01, "blink: faded at the end of the first pulse");
            Check(DocTabStrip.BlinkAmount(1600) == 1, "blink: the second pulse starts full");
            Check(DocTabStrip.BlinkAmount(3200) == 0 && DocTabStrip.BlinkAmount(5000) == 0, "blink: over after two pulses (3.2 s)");
        }

        static void Call(object o, string method, params object[] args)
        {
            var m = o.GetType().GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == args.Length);
            m.Invoke(o, args);
        }

        static Point Mid(Rectangle r) { return new Point(r.X + r.Width / 2, r.Y + r.Height / 2); }

        static void Strip()
        {
            using (var s = new DocTabStrip { Width = 700, Height = 28, Font = new Font("Segoe UI", 9f) })
            {
                foreach (var t in new[] { "Regal 3", "Untitled 1", "Versand Absender" }) s.Tabs.Add(new DocTabStrip.Tab { Title = t, ToolTip = t + "\nprinter" });
                s.Tabs[0].Dirty = true;
                s.Active = 0;
                var selected = new List<int>(); var closed = new List<int>(); int added = 0; var menus = new List<int>();
                s.TabSelected += i => selected.Add(i);
                s.TabCloseRequested += i => closed.Add(i);
                s.NewTabRequested += () => added++;
                s.TabContextMenu += (i, p) => menus.Add(i);
                Check(s.TabLayout.Visible.Count == 3 && !s.TabLayout.Overflow, "strip: three tabs shown");
                Check(s.TabRect(1).Left < s.TabRect(2).Left && s.TabRect(0).Right - 1 <= s.TabRect(1).Left + 1, "strip: tabs side by side, overlapping by one pixel");
                Check(s.PlusRect.Left > s.TabRect(2).Right, "strip: + after the last tab");
                int index, part;
                s.HitTest(Mid(s.TabRect(1)), out index, out part);
                Check(index == 1 && part == 0, "hit: tab 2");
                s.HitTest(Mid(s.XRect(0)), out index, out part);
                Check(index == 0 && part == 1, "hit: the x of tab 1");
                s.HitTest(Mid(s.PlusRect), out index, out part);
                Check(index == -1 && part == 2, "hit: +");

                Action<MouseButtons, Point> click = (b, p) =>
                {
                    Call(s, "OnMouseDown", new MouseEventArgs(b, 1, p.X, p.Y, 0));
                    Call(s, "OnMouseUp", new MouseEventArgs(b, 1, p.X, p.Y, 0));
                };
                click(MouseButtons.Left, Mid(s.TabRect(2)));
                Check(selected.SequenceEqual(new[] { 2 }) && s.Active == 0, "click: TabSelected(2), Active stays the form's");
                click(MouseButtons.Left, Mid(s.TabRect(0)));
                Check(selected.Count == 1, "click on the active tab: no event");
                click(MouseButtons.Left, Mid(s.XRect(0)));
                Check(closed.SequenceEqual(new[] { 0 }), "x: TabCloseRequested(0)");
                click(MouseButtons.Middle, Mid(s.TabRect(1)));
                Check(closed.SequenceEqual(new[] { 0, 1 }), "middle click: TabCloseRequested(1)");
                click(MouseButtons.Left, Mid(s.PlusRect));
                Check(added == 1, "+: NewTabRequested");
                click(MouseButtons.Right, Mid(s.TabRect(2)));
                Check(menus.SequenceEqual(new[] { 2 }), "right click: TabContextMenu(2)");
                // pressed on one tab's x, released elsewhere: nothing
                Call(s, "OnMouseDown", new MouseEventArgs(MouseButtons.Left, 1, Mid(s.XRect(0)).X, Mid(s.XRect(0)).Y, 0));
                Call(s, "OnMouseUp", new MouseEventArgs(MouseButtons.Left, 1, Mid(s.TabRect(2)).X, Mid(s.TabRect(2)).Y, 0));
                Check(closed.Count == 2, "x pressed, released elsewhere: not closed");

                // keys (with the focus): Right, End, Home, Left at the start
                selected.Clear();
                foreach (var k in new[] { Keys.Right, Keys.End, Keys.Home }) Call(s, "OnKeyDown", new KeyEventArgs(k));
                Check(selected.SequenceEqual(new[] { 1, 2 }), "keys: Right 1, End 2, Home = active (no event): " + string.Join(",", selected));
                s.Active = 2;
                selected.Clear();
                Call(s, "OnKeyDown", new KeyEventArgs(Keys.Left));
                Call(s, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.Left));
                Check(selected.SequenceEqual(new[] { 1 }), "keys: Left 1, Ctrl+Left left to the form");

                // many tabs: the list, the active one stays
                s.Tabs.Clear();
                for (int i = 0; i < 9; i++) s.Tabs.Add(new DocTabStrip.Tab { Title = "Lagerbeschriftung Halle " + i + " – Regalböden oben" });
                s.Active = 7;
                Check(s.TabLayout.Overflow && s.TabLayout.Visible.Contains(7) && !s.ListRect.IsEmpty, "nine long tabs: list shown, tab 8 in the strip: " + Show(s.TabLayout));
                Check(s.TabLayout.Visible.All(i => s.TabRect(i).Right <= s.ListRect.Left), "the tabs end before the list");
                s.HitTest(Mid(s.ListRect), out index, out part);
                Check(part == 3, "hit: the list");
                s.Width = 2000;
                s.Refresh();
                Check(!s.TabLayout.Overflow && s.ListRect.IsEmpty, "wide enough again: no list");
                s.Tabs.RemoveAt(8);
                s.Refresh();
                Check(s.Active == 7, "Active kept when a later tab goes");
                s.Tabs.Clear();
                s.Refresh();
                Check(s.Active == -1 && s.TabLayout.Visible.Count == 0, "no tabs: Active -1");

                // the blink stays with its tab (review 0.23): a tab left of it closes, then the blinking one itself
                for (int i = 0; i < 4; i++) s.Tabs.Add(new DocTabStrip.Tab { Title = "Etikett " + i });
                s.Active = 0;
                var third = s.Tabs[2];
                s.Blink(2);
                s.Tabs.RemoveAt(0);
                s.Refresh();
                Check(s.Blinking && s.BlinkingIndex == 1 && s.Tabs[1] == third, "blink: moves with its tab when a tab left of it closes (" + s.BlinkingIndex + ")");
                s.Tabs.Insert(0, new DocTabStrip.Tab { Title = "neu" });
                s.Refresh();
                Check(s.BlinkingIndex == 2, "blink: moves with its tab when a tab is added before it");
                s.Tabs.Remove(third);
                s.Refresh();
                Check(!s.Blinking, "blink: ends when its tab closes");
            }
        }

        // The tab's height follows the font (review 0.23): 26 px with the normal UI font, taller with a large Windows
        // text size, and the title then still fits.
        static void Height()
        {
            using (var form = new Form())
            using (var s = new DocTabStrip { Width = 700, Font = new Font("Segoe UI", 9f) })
            {
                form.Controls.Add(s);
                form.CreateControl();
                s.CreateControl();
                s.Tabs.Add(new DocTabStrip.Tab { Title = "Regal 3" });
                s.Refresh();
                int normal = s.TabH, k = s.LogicalToDeviceUnits(1);
                Check(normal == s.LogicalToDeviceUnits(26), "height: 26 px with Segoe UI 9 pt (" + normal + ")");
                Check(s.Height == normal + 2 * k, "height: the strip 2 px taller than a tab (" + s.Height + ")");
                s.Font = new Font("Segoe UI", 16f);
                s.Refresh();
                int text = TextRenderer.MeasureText("Regal 3", s.Font).Height;
                Check(s.TabH > normal && s.TabH >= text + 4 * k, "height: taller with a 16 pt font (" + s.TabH + " for text " + text + ")");
                Check(s.Height == s.TabH + 2 * k && s.TabRect(0).Height >= text, "height: the strip and the tab grow with it (strip " + s.Height + ", tab " + s.TabRect(0).Height + ", TabH " + s.TabH + ")");
                Check(s.PlusRect.Bottom <= s.Height && s.PlusRect.Height == s.TabH - 2 * k, "height: + as tall as a tab less 2 px");
            }
        }

        static void Dialog()
        {
            var items = new List<SaveChangesDialog.Item>
            {
                new SaveChangesDialog.Item { Title = "Regal 3", Detail = @"C:\Etiketten\" },
                new SaveChangesDialog.Item { Title = "Untitled 1", Detail = "not saved yet" },
                new SaveChangesDialog.Item { Title = "Versand Absender", Detail = @"C:\Etiketten\" },
            };
            using (var d = new SaveChangesDialog(items) { Note = "“Untitled 1” asks for its folder and name next." })
            {
                Check(items.All(i => i.Save) && Enumerable.Range(0, 3).All(i => d.Box(i).Checked), "close question: all ticked");
                d.Box(1).Checked = false;
                Check(!items[1].Save && items[0].Save, "untick: that item's Save false");
                Check(d.SaveButton.DialogResult == DialogResult.Yes && d.DontSaveButton.DialogResult == DialogResult.No && d.CancelButton == d.CancelButtonControl
                      && d.CancelButtonControl.DialogResult == DialogResult.Cancel && d.AcceptButton == d.SaveButton, "answers: Save = Yes, Don't save = No, Cancel / Esc = Cancel, Enter = Save");
                Check(d.Note.Length > 0, "the note line");
            }
            var many = Enumerable.Range(1, 30).Select(i => new SaveChangesDialog.Item { Title = "Etikett " + i, Detail = @"C:\Etiketten\" }).ToList();
            using (var d = new SaveChangesDialog(many))
            {
                var size = d.GetPreferredSize(Size.Empty);
                Check(size.Height < 690, "30 labels: the question stays within 690 px (" + size.Height + ")");
                Check(d.List.Height < SaveChangesDialog.MaxRows * 40, "30 labels: the list scrolls (" + d.List.Height + " px)");
            }
        }
    }
}
