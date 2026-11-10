// QL Label Maker: several labels as tabs (EditorForm.Tabs.cs, LabelTab.cs, DocTabStrip, SaveChangesDialog; tabs 0.23).
// Checks: the strip with one tab ("Untitled 1"), Ctrl+N takes the printer and label format, each tab keeps its undo, zoom,
// selection and label format, Ctrl+Tab / Ctrl+Shift+Tab, the * in the tab and the window title, a changed tab's question
// on Ctrl+W (Cancel keeps it, Don't save closes it), a file already open (its tab comes forward, the status bar says so,
// no second tab), a second start's file in a new tab without a question, the overflow (many tabs: the active one stays
// in the strip), the window's one question for several changed labels (Cancel keeps the window), the last tab closed
// leaves an empty "Untitled 1", a background tab whose printer was removed (another printer, said in the status bar),
// a second start with two files and a printer, a start with a missing file (a status bar line, no message box), the
// window within 690 px. Prints CHECK lines. Works in a folder of its own under %TEMP%
// and a registry key of its own (both removed afterwards). Runs only through deskrun.exe (a desktop of its own: no
// mouse, no focus taken). A watchdog ends it after 120 s. With a picture folder: the window with three tabs as
// editor-tabs-<lang>-<scheme>.png.
// With 150 as the fourth argument the window opens on a screen at 150 % (144 dpi); without one that run is skipped.
// Run by editor-tabs-ui.ps1:  editor_tabs_ui.exe <de|en> <light|dark> <picture folder> <100|150>
using System;
using System.Collections;
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
    static class EditorTabsUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static int failures, questions, lists;
        static string scheme, folder;
        static readonly Queue<DialogResult> answers = new Queue<DialogResult>();

        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        static string dpiName = "100";

        static int Dpi(Screen s)
        {
            uint x, y;
            var m = MonitorFromPoint(new Point(s.Bounds.X + 1, s.Bounds.Y + 1), 2);
            return GetDpiForMonitor(m, 0, out x, out y) == 0 ? (int)x : 96;
        }

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
            Theme.Force(scheme == "dark" ? Scheme.Dark : Scheme.Light);
            string key = RecentLabels.Key, lastKey = QlPrinters.LastKey;
            RecentLabels.Key = Lang.SettingsKey + @"\UiTest-" + Guid.NewGuid().ToString("N");
            QlPrinters.LastKey = RecentLabels.Key;
            folder = Path.Combine(Path.GetTempPath(), "ql-tabs-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            if (args.Length > 3) dpiName = args[3];
            var screen = Screen.AllScreens.FirstOrDefault(x => Dpi(x) == (dpiName == "150" ? 144 : 96));
            if (screen == null)
            {
                Console.WriteLine("INFO no screen at " + dpiName + " % attached: this run is skipped");
                Console.WriteLine("DONE " + args[0] + " " + scheme + " " + dpiName);
                return 0;
            }
            Cursor.Position = new Point(screen.WorkingArea.X + 40, screen.WorkingArea.Y + 40);   // the window sizes for the cursor's screen (deskrun: no visible cursor)
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(screen.WorkingArea.X + 20, screen.WorkingArea.Y + 20) };
            // the questions: answered from the queue by a timer (they are modal)
            var answerer = new System.Windows.Forms.Timer { Interval = 100 };
            answerer.Tick += delegate { AnswerOpen(); };
            answerer.Start();
            bool done = false;
            form.Shown += delegate
            {
                try { Run(args.Length > 2 ? args[2] : null); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                foreach (var t in Tabs()) t.Dirty = false;
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
                QlPrinters.LastKey = lastKey;
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + scheme + " " + dpiName : "DONE " + args[0] + " " + scheme + " " + dpiName + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void AnswerOpen()
        {
            if (answers.Count == 0) return;
            var q = Application.OpenForms.OfType<QlMessage.MessageForm>().FirstOrDefault(f => f.Visible);
            if (q != null)
            {
                var b = Under(q).OfType<Button>().FirstOrDefault(x => x.DialogResult == answers.Peek());
                if (b == null) return;
                answers.Dequeue();
                questions++;
                b.PerformClick();
                return;
            }
            var d = Application.OpenForms.OfType<SaveChangesDialog>().FirstOrDefault(f => f.Visible);
            if (d != null)
            {
                var want = answers.Dequeue();
                lists++;
                string field = want == DialogResult.Yes ? "save" : want == DialogResult.No ? "dontSave" : "cancel";
                ((Button)typeof(SaveChangesDialog).GetField(field, All).GetValue(d)).PerformClick();
            }
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + scheme + " " + dpiName + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static object Call(string method, params object[] a)
        {
            // optional parameters left out: their defaults
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length >= a.Length &&
                                                                  x.GetParameters().Skip(a.Length).All(q => q.IsOptional));
            return m.Invoke(form, a.Concat(m.GetParameters().Skip(a.Length).Select(q => q.DefaultValue)).ToArray());
        }
        static IEnumerable<Control> Under(Control c) { return c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Under(x))); }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static bool Until(Func<bool> done, int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { if (done()) return true; Application.DoEvents(); Thread.Sleep(10); }
            return done();
        }

        static List<LabelTab> Tabs() { return F<List<LabelTab>>("tabs"); }
        static LabelTab Current { get { return F<LabelTab>("current"); } }
        static DocTabStrip Strip { get { return F<DocTabStrip>("strip"); } }
        static LabelDoc Doc { get { return F<LabelDoc>("doc"); } }
        static EditorCanvas Canvas { get { return F<EditorCanvas>("canvas"); } }
        static LabelChoice Label { get { return F<LabelChoice>("label"); } }
        static bool CanUndo { get { return F<UndoStack>("undo").CanUndo; } }
        static string Untitled(int n) { return Lang.F("Untitled {0}", n); }
        static void Key(Keys k) { Call("TabKey", k); Pump(50); }

        static string SaveTo(string name)
        {
            string p = Path.Combine(folder, name + LabelDoc.Extension);
            typeof(EditorForm).GetField("path", All).SetValue(form, p);
            Call("Save", false);
            return p;
        }

        static void Run(string pictures)
        {
            Pump(300);
            // ---------- one tab ----------
            // nothing selected: the text toolbar's spacing fields are off; the line spacing box in the same face as the
            // letter spacing field (dark: no white box)
            var lineBox = F<ComboBox>("lineSpacingBox");
            var letterNum = F<NumericUpDown>("letterSpacingNum");
            Color lineFace = FaceOf(lineBox), letterFace = FaceOf(letterNum);
            if (lineFace.IsEmpty || letterFace.IsEmpty) Console.WriteLine("INFO the spacing fields are in the toolbar's overflow at this width: face not compared");
            else Check(!lineBox.Enabled && !letterNum.Enabled && Near(lineFace, letterFace),
                  "nothing selected: line spacing box off in the letter spacing field's face (" + Hex(lineFace) + " / " + Hex(letterFace) + ")");
            Check(Strip.Visible && Strip.Tabs.Count == 1 && Strip.Tabs[0].Title == Untitled(1) && Strip.Active == 0, "one tab \"" + Untitled(1) + "\", the strip shown");
            Check(form.Text.StartsWith(Untitled(1) + " – "), "the window title names it: " + form.Text);
            var host = F<Panel>("host");
            Check(Strip.Bottom <= host.Parent.Top + 1 && Strip.Parent.Contains(host), "the strip right above the canvas");
            int dpi = form.DeviceDpi;
            Check(dpi == (dpiName == "150" ? 144 : 96), "the window at " + dpi + " dpi");
            Check(form.Height <= 690 * dpi / 96, "the window within 690 px (scaled: " + form.Height + " px at " + dpi + " dpi); canvas " + host.ClientSize.Height + " px");
            Check(Strip.Height >= 28 * dpi / 96 - 1 && Strip.Height <= 30 * dpi / 96 + 1, "the strip scaled with the window: " + Strip.Height + " px (28 at 100 %)");

            // ---------- tab 1: a text, zoom 150 %, the text selected ----------
            var labelOne = Label;
            var text = new TextObject { Text = "Regal 3", Size = 12, X = 2, Y = 2 };
            Call("Add", text);
            Call("SetZoom", 150.0);
            Check(Strip.Tabs[0].Dirty && form.Text.Contains(" *"), "a change: * in the tab and the window title (" + form.Text + ")");

            // ---------- Ctrl+N ----------
            Call("NewTab");
            Pump(100);
            Check(Tabs().Count == 2 && Strip.Active == 1 && Strip.Tabs[1].Title == Untitled(2), "Ctrl+N: a second tab \"" + Untitled(2) + "\", active");
            Check(F<string>("printer") == "QL-800 Capture" && Label == labelOne, "it takes the printer and the label format (" + (Label != null ? Label.Name : "?") + ")");
            Check(Doc.Objects.Count == 0 && !CanUndo && Canvas.Selection.Count == 0 && !F<bool>("dirty"), "empty: no objects, no undo, nothing selected, unchanged");

            // tab 2: another label format, a box, zoom fit
            var choices = F<List<LabelChoice>>("choices");
            var other = choices.FirstOrDefault(c => c != labelOne && !c.Custom);
            Call("SelectLabel", other);
            var box = new ShapeObject { X = 5, Y = 3, W = 10, H = 6 };
            Call("Add", box);
            Call("SetZoom", 0.0);
            Pump(100);

            // ---------- back to tab 1 (Ctrl+Shift+Tab) ----------
            Key(Keys.Control | Keys.Shift | Keys.Tab);
            Check(Current == Tabs()[0] && Strip.Active == 0, "Ctrl+Shift+Tab: the first tab");
            Check(Doc.Objects.Count == 1 && Doc.Objects[0] == text && Canvas.Selection.Contains(text), "its text, still selected");
            Check(Label == labelOne && F<double>("zoom") == 150.0, "its label format and zoom (" + (Label != null ? Label.Name : "?") + ", " + F<double>("zoom") + " %)");
            Check(CanUndo, "its undo");
            Call("Undo", false);
            Check(Doc.Objects.Count == 0, "Undo takes back the first tab's text only");

            // ---------- tab 2 again (Ctrl+Tab) ----------
            Key(Keys.Control | Keys.Tab);
            Check(Current == Tabs()[1] && Doc.Objects.Count == 1 && Doc.Objects[0] == box && Canvas.Selection.Contains(box), "Ctrl+Tab: the second tab with its box, selected");
            Check(Label == other && F<double>("zoom") == 0 && CanUndo, "its label format, fit zoom and undo (" + (Label != null ? Label.Name : "?") + ")");
            Key(Keys.Control | Keys.Tab);
            Check(Current == Tabs()[0], "Ctrl+Tab goes round");

            // ---------- a file already open ----------
            Call("Add", new TextObject { Text = "Lager", Size = 12, X = 2, Y = 2 });
            string fileA = SaveTo("Regal 3");
            Check(Strip.Tabs[0].Title == "Regal 3" && !Strip.Tabs[0].Dirty && Strip.Tabs[0].ToolTip.Contains(fileA), "saved: the tab's name and its tooltip with the file");
            Key(Keys.Control | Keys.Tab);
            Call("OpenFile", fileA);
            Pump(100);
            Check(Tabs().Count == 2 && Current == Tabs()[0], "opened again: its tab comes forward, no second tab");
            Check(F<ToolStripStatusLabel>("labelStatus").Text == Lang.F("“{0}” is already open.", "Regal 3"), "the status bar says so");

            // ---------- a second start's file: a new tab, no question ----------
            string fileB = Path.Combine(folder, "Versand" + LabelDoc.Extension);
            new LabelDoc { FormName = "62mm", PaperId = 259, Objects = { new TextObject { Text = "Absender", Size = 10, X = 1, Y = 1 } } }.Save(fileB);
            form.StartedAgain(new[] { fileB });
            Check(Until(() => Tabs().Count == 3, 3000) && F<string>("path") == fileB && questions == 0, "a second start's file: a new tab, active, no question");

            if (pictures != null)
            {
                Tabs()[1].Dirty = true;   // as in the mockup: one with changes
                Call("UpdateTabs");
                Pump(200);
                Shot(Path.Combine(pictures, "editor-tabs-" + Lang.Current + "-" + scheme + "-" + dpiName + ".png"));
                // QL_TEST_WIDTHS=1024,1280,1366: the same at these window widths x 690 (logical px), for the mockup comparison
                string widths = Environment.GetEnvironmentVariable("QL_TEST_WIDTHS");
                if (!string.IsNullOrEmpty(widths))
                {
                    var sizeBefore = form.Size;
                    foreach (var w in widths.Split(','))
                    {
                        int lw;
                        if (!int.TryParse(w.Trim(), out lw)) continue;
                        form.Size = new Size(lw * form.DeviceDpi / 96, 690 * form.DeviceDpi / 96);
                        Pump(200);
                        Shot(Path.Combine(pictures, "editor-tabs-" + Lang.Current + "-" + scheme + "-" + dpiName + "-w" + lw + ".png"));
                    }
                    form.Size = sizeBefore;
                    Pump(100);
                }
            }

            // ---------- Ctrl+W on a changed tab: the question ----------
            Call("SwitchTo", 1);
            typeof(EditorForm).GetField("dirty", All).SetValue(form, true);
            answers.Enqueue(DialogResult.Cancel);
            Call("CloseTab", 1);
            Check(questions == 1 && Tabs().Count == 3, "a changed tab: the question; Cancel keeps it");
            answers.Enqueue(DialogResult.No);
            Call("CloseTab", 1);
            Check(questions == 2 && Tabs().Count == 2 && Current != null && Tabs().Contains(Current), "Don't save: closed, its neighbour shown");
            int before = questions;
            var rest = Tabs().First(t => t.Path != fileA);   // made unchanged: closes without a question
            if (rest == Current) typeof(EditorForm).GetField("dirty", All).SetValue(form, false); else rest.Dirty = false;
            Call("CloseTab", Tabs().IndexOf(rest));
            Check(questions == before && Tabs().Count == 1, "an unchanged tab closes without a question");

            // ---------- overflow ----------
            for (int i = 0; i < 16; i++) Call("NewTab");
            Pump(200);
            var lay = Strip.TabLayout;
            Check(Tabs().Count == 17 && lay != null && lay.Overflow && lay.Visible.Contains(Strip.Active), "17 tabs: the overflow list, the active tab in the strip");
            Call("SwitchTo", 0);
            Pump(100);
            lay = Strip.TabLayout;
            Check(lay.Visible.Contains(0) && Strip.Active == 0, "the first tab chosen: shown in the strip");
            while (Tabs().Count > 1) Call("CloseTab", Tabs().Count - 1);
            Check(questions == before, "unchanged new tabs close without a question");

            // ---------- closing the last tab ----------
            Call("CloseTab", 0);
            Check(Tabs().Count == 1 && Current.Pristine && Strip.Tabs[0].Title == Untitled(1) && form.Visible, "the last tab closed: an empty \"" + Untitled(1) + "\", the window stays");
            Check(F<string>("printer") == "QL-800 Capture" && Label != null, "on the same printer and a label format (" + (Label != null ? Label.Name : "?") + ")");

            // ---------- a file: replaces only the window's one untouched tab, else a new tab right of the active one ----------
            string one = Path.Combine(folder, "Einzeln" + LabelDoc.Extension);
            new LabelDoc { FormName = "62mm", PaperId = 259 }.Save(one);
            Call("OpenFile", one);
            Check(Tabs().Count == 1 && F<string>("path") == one, "the only tab empty and untouched: the file takes its place");
            Call("CloseTab", 0);
            Call("NewTab");
            Call("NewTab");
            Call("SwitchTo", 1);
            Call("OpenFile", one);
            Check(Tabs().Count == 4 && Tabs().IndexOf(Current) == 2 && F<string>("path") == one && Tabs()[1].Pristine,
                  "several empty tabs: the file in a new tab right of the active one, the empty tab stays (" + Tabs().Count + " tabs)");
            while (Tabs().Count > 2) Call("CloseTab", Tabs().Count - 1);
            Call("SwitchTo", 0);
            form.StartedAgain(EditorProgram.ForRunning(new[] { "--printer", "QL-800 Capture" }));
            Check(Until(() => Tabs().Count == 3, 3000) && Tabs().IndexOf(Current) == 1, "--printer alone with two empty tabs: a new tab");
            while (Tabs().Count > 1) Call("CloseTab", Tabs().Count - 1);
            form.StartedAgain(EditorProgram.ForRunning(new[] { "--printer", "QL-800 Capture" }));
            Pump(800);
            Check(Tabs().Count == 1 && Current.Pristine && F<string>("printer") == "QL-800 Capture", "--printer alone with the only, untouched tab: no new tab");

            // ---------- a background tab's printer removed meanwhile ----------
            Call("NewTab");
            var bg = Current;
            Call("SwitchTo", 0);
            bg.Printer = "QL removed printer";   // as if removed while another tab was shown
            Call("SwitchTo", Tabs().IndexOf(bg));
            string now = F<string>("printer");
            Check(now != null && F<List<string>>("printers").Contains(now) && (string)F<ComboBox>("printerBox").SelectedItem == now && Label != null,
                  "a removed printer: the tab falls back to a QL printer still there, the box shows it (" + now + ", " + (Label != null ? Label.Name : "?") + ")");
            Check(F<ToolStripStatusLabel>("labelStatus").Text == Lang.F("{0} is no longer there: the label is shown on {1}.", "QL removed printer", now),
                  "the status bar says so: " + F<ToolStripStatusLabel>("labelStatus").Text);
            Check(Strip.Tabs[Tabs().IndexOf(bg)].ToolTip.Contains(now), "the tab's tooltip names the new printer");

            // ---------- a second start with two files and a printer: both open, the first on that printer ----------
            string two1 = Path.Combine(folder, "Zwei 1" + LabelDoc.Extension), two2 = Path.Combine(folder, "Zwei 2" + LabelDoc.Extension);
            new LabelDoc { FormName = "62mm", PaperId = 259 }.Save(two1);
            new LabelDoc { FormName = "62mm", PaperId = 259 }.Save(two2);
            int asked = questions;
            string otherPrinter = F<List<string>>("printers").FirstOrDefault(x => x != now);
            form.StartedAgain(EditorProgram.ForRunning(otherPrinter != null ? new[] { "--printer", otherPrinter, two1, two2 } : new[] { two1, two2 }));
            Check(Until(() => Tabs().Any(t => t.Path == two1) && Tabs().Any(t => t.Path == two2), 3000) && questions == asked, "a second start with two files: both open, no question");
            if (otherPrinter != null) Check(Tabs().First(t => t.Path == two1).Printer == otherPrinter, "the first file on the printer given before it (" + otherPrinter + ")");
            while (Tabs().Count > 1) Call("CloseTab", Tabs().Count - 1);
            Call("CloseTab", 0);
            Check(Tabs().Count == 1 && Current.Pristine && questions == asked, "unchanged tabs closed, an empty one left");

            // ---------- a start with a missing file: no message before the window, a line in its status bar ----------
            var second = new EditorForm(Path.Combine(folder, "fehlt" + LabelDoc.Extension), "QL-800 Capture", null)
                         { StartPosition = FormStartPosition.Manual, Location = new Point(form.Left + 40, form.Top + 40) };
            bool early = Application.OpenForms.OfType<QlMessage.MessageForm>().Any();
            second.Show();
            Pump(600);
            var secondStatus = (ToolStripStatusLabel)typeof(EditorForm).GetField("labelStatus", All).GetValue(second);
            Check(!early && !Application.OpenForms.OfType<QlMessage.MessageForm>().Any() && secondStatus.Text == Lang.F("“{0}” was not found.", "fehlt"),
                  "a start with a missing file: no message box, the status bar says so (" + secondStatus.Text + ")");
            second.Close();
            Pump(200);

            // ---------- the window: one question for several labels ----------
            Call("Add", new TextObject { Text = "A", Size = 12, X = 2, Y = 2 });
            Call("NewTab");
            Call("Add", new TextObject { Text = "B", Size = 12, X = 2, Y = 2 });
            answers.Enqueue(DialogResult.Cancel);
            form.Close();
            Pump(300);
            Check(lists == 1 && form.Visible && Tabs().Count == 2, "two changed labels: one question with the list; Cancel keeps the window");
            Check(Tabs().All(t => t.Dirty), "both keep their changes");
        }

        // The colour inside a field (left of its middle), as Windows paints it (PrintWindow: the themed parts too).
        static Color FaceOf(Control c)
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var dc = g.GetHdc();
                    PrintWindow(form.Handle, dc, 2);
                    g.ReleaseHdc(dc);
                }
                var p = c.PointToScreen(new Point(Math.Max(4, c.Width / 5), c.Height / 2));
                int x = p.X - form.Bounds.X, y = p.Y - form.Bounds.Y;
                return c.Visible && x >= 0 && y >= 0 && x < bmp.Width && y < bmp.Height ? bmp.GetPixel(x, y) : Color.Empty;   // Empty: not in the window (toolbar overflow)
            }
        }
        static bool Near(Color a, Color b) { return Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 24; }
        static string Hex(Color c) { return "#" + (c.ToArgb() & 0xFFFFFF).ToString("X6"); }

        static void Shot(string file)
        {
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    var dc = g.GetHdc();
                    PrintWindow(form.Handle, dc, 2);
                    g.ReleaseHdc(dc);
                }
                bmp.Save(file, ImageFormat.Png);
            }
        }
    }
}
