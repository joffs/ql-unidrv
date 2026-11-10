// QL Label Maker: words in their own format in an artistic text (one line, the box follows the text), as in a paragraph
// text. Checks, while the text is edited on the label: Ctrl+B / Ctrl+Shift+K / Ctrl+U and the panel's Size, Font and
// Underline on a selection (only it changes, the panel says what they apply to), the box around mixed sizes (the
// printed dots inside it), a paste from Word keeping its formats (line breaks become spaces), a turned text, the
// handles scaling every word's size, Kind to paragraph text and back keeping the formats (no question), saving and
// opening the label, and Undo. Prints CHECK lines; changes nothing (nothing saved or printed). Runs only through
// deskrun.exe (a desktop of its own: no mouse, no focus taken; the clipboard locked). A watchdog ends it after 120 s.
// Run by artistic-runs-ui.ps1:  artistic_runs_ui.exe <de|en>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class ArtisticRunsUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int WM_KEYDOWN = 0x0100;

        static EditorForm form;
        static EditorCanvas canvas;
        static int failures;

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
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);   // closes without asking to save
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] : "DONE " + args[0] + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static object Call(string method, params object[] a)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            return m.Invoke(form, a);
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // A key as the window gets it (its ProcessCmdKey first, as for a real key press).
        static void Key(Keys keys)
        {
            var m = Message.Create(canvas.Handle, WM_KEYDOWN, (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero);
            typeof(EditorForm).GetMethod("ProcessCmdKey", All).Invoke(form, new object[] { m, keys });
            Pump(40);
        }

        // The runs as "text:b:i:u:size:font" (only what differs from 12 pt Arial, plain).
        static string Fmt(TextObject t)
        {
            return string.Join("|", t.FullRuns().Select(r => r.Text + (r.Bold == true ? ":b" : "") + (r.Italic == true ? ":i" : "") + (r.Underline == true ? ":u" : "")
                                                            + (r.Size != 12 ? ":" + r.Size : "") + (r.Font != "Arial" ? ":" + r.Font : "")));
        }

        // The runs as "text:inv:spacing" (invert and letter spacing only).
        static string Shade(TextObject t)
        {
            return string.Join("|", t.FullRuns().Select(r => r.Text + (r.Invert == true ? ":inv" : "") + (r.Spacing != 0 ? ":" + r.Spacing : "")));
        }

        static TextObject AddText(string text, double x, double y, int rotation = 0)
        {
            var t = new TextObject { X = x, Y = y, W = 30, H = 10, Text = text, Size = 12, Font = "Arial", Kind = TextKind.Line, Rotation = rotation };
            EditorRender.FitText(t);
            Call("Add", t);
            Pump(100);
            return t;
        }

        // The printed dots of one object (300 dpi, as printed) all inside its box (mm, 0.2 mm slack for grid fitting).
        static bool InkInBox(TextObject t, out string where)
        {
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            var layout = F<LabelLayout>("layout");
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            int lo = int.MaxValue, hi = -1, top = int.MaxValue, bottom = -1;
            using (var bmp = EditorRender.Render(doc, layout))
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0) { lo = Math.Min(lo, x); hi = Math.Max(hi, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
            var ink = RectangleF.FromLTRB(lo / kx + layout.Printable.X, top / ky + layout.Printable.Y, (hi + 1) / kx + layout.Printable.X, (bottom + 1) / ky + layout.Printable.Y);
            var box = RectangleF.Inflate(new RectangleF((float)t.X, (float)t.Y, (float)t.W, (float)t.H), 0.2f, 0.2f);
            where = "ink " + ink.Left.ToString("0.0") + ".." + ink.Right.ToString("0.0") + " x " + ink.Top.ToString("0.0") + ".." + ink.Bottom.ToString("0.0")
                  + ", box " + t.X.ToString("0.0") + ".." + (t.X + t.W).ToString("0.0") + " x " + t.Y.ToString("0.0") + ".." + (t.Y + t.H).ToString("0.0");
            return hi >= 0 && box.Contains(ink);
        }

        static void Run()
        {
            canvas = F<EditorCanvas>("canvas");
            var scope = F<Label>("scopeHint");

            // ---------- keys on a selection ----------
            var t = AddText("Hallo Welt", 4, 4);
            double w0 = t.W, h0 = t.H;
            canvas.BeginEdit(t);
            Pump(60);
            canvas.EditChange(e => e.Select(6, 10));   // "Welt"
            Pump(60);
            Check(scope.Visible && scope.Text.Contains("Welt"), "the panel says what the formats apply to: " + scope.Text);
            Key(Keys.Control | Keys.B);
            Check(Fmt(t) == "Hallo |Welt:b", "Ctrl+B: only the selection bold: " + Fmt(t));
            Check(t.W > w0, "the box follows the bold word (" + w0.ToString("0.0") + " → " + t.W.ToString("0.0") + " mm)");
            Key(Keys.Control | Keys.Shift | Keys.K);
            Key(Keys.Control | Keys.U);
            Check(Fmt(t) == "Hallo |Welt:b:i:u", "Ctrl+Shift+K and Ctrl+U on the selection: " + Fmt(t));
            Check(F<ToolStripButton>("boldButton").Checked && F<ToolStripButton>("italicButton").Checked,
                  "the toolbar shows the selection's format");

            // ---------- the panel's rows on a selection ----------
            canvas.EditChange(e => e.Select(0, 5));   // "Hallo"
            Pump(60);
            var size = F<NumericUpDown>("sizeNum");
            size.Value = 24;
            Pump(60);
            Check(Fmt(t) == "Hallo:24| |Welt:b:i:u", "Size 24 on the selection: " + Fmt(t));
            Check(t.H > h0 * 1.6, "the box as high as the largest word (" + h0.ToString("0.0") + " → " + t.H.ToString("0.0") + " mm)");
            string where;
            Check(InkInBox(t, out where), "printed dots inside the box with mixed sizes: " + where);
            canvas.EditChange(e => e.Select(0, 5));
            F<ToolStripButton>("underlineButton").PerformClick();
            Pump(60);
            Check(Fmt(t) == "Hallo:u:24| |Welt:b:i:u", "Underline in the toolbar on the selection: " + Fmt(t));
            canvas.EditChange(e => e.Restyle(r => new TextRun(r.Text, "Courier New", r.Size, r.Bold, r.Italic, r.Underline)));
            Check(Fmt(t) == "Hallo:u:24:Courier New| |Welt:b:i:u", "a font on the selection: " + Fmt(t));

            // ---------- invert and letter spacing on a selection (2026-11-09: also in artistic text) ----------
            var invert = F<ToolStripButton>("invertButton");
            var letters = F<NumericUpDown>("letterSpacingNum");
            var mixed = F<bool[]>("textMixed");
            canvas.EditChange(e => e.Select(6, 10));   // "Welt"
            Pump(60);
            invert.PerformClick();
            Pump(60);
            Check(Shade(t) == "Hallo| |Welt:inv" && !t.Invert && invert.Checked, "Invert on the selection only: " + Shade(t));
            double w1 = t.W;
            letters.Value = 3;
            Pump(60);
            Check(Shade(t) == "Hallo| |Welt:inv:3" && t.W > w1 + 3 * 3 * 25.4 / 72 - 0.2, "letter spacing 3 pt on the selection only, the box follows: " + Shade(t) +
                  " (" + w1.ToString("0.0") + " → " + t.W.ToString("0.0") + " mm)");
            Check(InkInBox(t, out where), "printed dots inside the box with an inverted, spaced word: " + where);
            canvas.EditChange(e => e.Select(0, e.Length));
            Pump(60);
            Check(mixed[3] && !invert.Checked && letters.Text == "", "all selected: Invert hatched, letter spacing blank (mixed): “" + letters.Text + "”");
            Check(invert.ToolTipText.Contains(Lang.Current == "de" ? "teilweise" : "partly"), "mixed: the Invert tip says partly: " + invert.ToolTipText);
            invert.PerformClick();
            Pump(60);
            Check(t.Invert && t.FullRuns().All(r => r.Invert == true) && invert.Checked && !mixed[3], "the first click inverts all of it (the whole text's black box)");
            invert.PerformClick();
            Pump(60);
            Check(!t.Invert && t.FullRuns().All(r => r.Invert == false), "the second click: invert off everywhere");
            letters.Value = 0;
            Pump(60);
            Check(t.FullRuns().All(r => r.Spacing == 0) && t.Spacing == 0, "letter spacing 0 for all of it");

            // ---------- a paste from Word keeps its formats; a line break becomes a space ----------
            canvas.EditChange(e => e.SetCaret(e.Length, false));
            var data = new DataObject();
            data.SetData(DataFormats.Rtf, TextEditing.Rtf(new List<TextRun> { new TextRun(" X\nY", "Arial", 18, false, true, false) }));
            data.SetData(DataFormats.UnicodeText, " X\r\nY");
            Clipboard.SetDataObject(data, true);
            Pump(60);
            Call("Paste", false, null);
            Pump(100);
            Check(t.Text == "Hallo Welt X Y" && Fmt(t).EndsWith("| X Y:i:18"), "paste from Word: formats kept, one line: " + Fmt(t));
            Key(Keys.Escape);
            Check(canvas.EditedText == null && t.Kind == TextKind.Line && t.FormatRuns != null, "after editing: still an artistic text with its formats");
            Check(InkInBox(t, out where), "printed dots inside the box after the paste: " + where);

            // ---------- Undo: the whole editing session at once ----------
            Call("Undo", false);
            Pump(100);
            var back = form.GetType().GetField("doc", All).GetValue(form) as LabelDoc;
            var restored = back.Objects.OfType<TextObject>().FirstOrDefault();
            Check(restored != null && restored.Text == "Hallo Welt" && restored.FormatRuns == null, "Undo brings back the text in one format");
            Call("Undo", true);
            Pump(100);
            back = form.GetType().GetField("doc", All).GetValue(form) as LabelDoc;   // Undo and Redo bring a copy of the label
            t = back.Objects.OfType<TextObject>().First(o => o.Text.StartsWith("Hallo"));
            Check(t.FormatRuns != null && t.Text == "Hallo Welt X Y", "Redo brings the formats back");

            // ---------- the handles: every word's size in proportion ----------
            var sizes = t.FullRuns().Select(r => r.Size.Value).ToList();
            canvas.Selected = t;
            Pump(60);
            typeof(EditorCanvas).GetField("handle", All).SetValue(canvas, 4);   // bottom right corner
            typeof(EditorCanvas).GetField("startSize", All).SetValue(canvas, t.Size);
            typeof(EditorCanvas).GetField("startRuns", All).SetValue(canvas, t.FullRuns());
            var b = new RectangleF((float)t.X, (float)t.Y, (float)t.W, (float)t.H);
            typeof(EditorCanvas).GetMethod("ScaleText", All).Invoke(canvas, new object[] { t, b, b.Width * 2, b.Height * 2 });
            var scaled = t.FullRuns().Select(r => r.Size.Value).ToList();
            Check(scaled.Count == sizes.Count && scaled.Zip(sizes, (s2, s1) => Math.Abs(s2 - 2 * s1) < 0.11).All(x => x),
                  "a handle doubles every word's size: " + string.Join("/", sizes) + " → " + string.Join("/", scaled));
            Check(Math.Abs(t.X - b.X) < 0.01 && Math.Abs(t.Y - b.Y) < 0.01 && InkInBox(t, out where), "the opposite corner stays, the dots inside the box: " + where);

            // ---------- Kind: to paragraph text and back, the formats stay (no question) ----------
            string before = Fmt(t);
            var kindBox = F<ComboBox>("textKindBox");
            bool asked = false;
            var guard = new System.Windows.Forms.Timer { Interval = 50 };
            guard.Tick += delegate
            {
                var d = Application.OpenForms.OfType<Form>().FirstOrDefault(f => f != form && f.Visible && f.Modal);
                if (d != null) { asked = true; d.DialogResult = DialogResult.Cancel; }
            };
            guard.Start();
            kindBox.SelectedIndex = (int)TextKind.Frame;
            Pump(100);
            Check(t.Kind == TextKind.Frame && Fmt(t) == before, "to paragraph text: formats kept");
            t.SetRuns(t.FullRuns().Select((r, i) => i == 0 ? r.WithText(r.Text + "\n") : r).ToList());
            kindBox.SelectedIndex = (int)TextKind.Line;
            Pump(100);
            guard.Stop();
            guard.Dispose();
            Check(!asked, "back to artistic text without a question");
            Check(t.Kind == TextKind.Line && t.FormatRuns != null && t.Text.IndexOf('\n') < 0 && Fmt(t) == before.Replace("Hallo:", "Hallo :"),
                  "back to artistic text: formats kept, the line break a space: " + Fmt(t));

            // ---------- saving and opening keeps them ----------
            string json = back.ToJson();
            var read = LabelDoc.FromJson(json).Objects.OfType<TextObject>().First(o => o.Text.StartsWith("Hallo"));
            Check(read.Kind == TextKind.Line && Fmt(read) == Fmt(t) && json.Contains("\"runs\""), "saved and opened: an artistic text with its runs: " + Fmt(read));

            // ---------- a turned text ----------
            var r90 = AddText("ABC DEF", 20, 30, 90);
            double tw = r90.W, th = r90.H;
            canvas.BeginEdit(r90);
            Pump(60);
            canvas.EditChange(e => e.Select(4, 7));
            var sz = F<NumericUpDown>("sizeNum");
            sz.Value = 20;
            Pump(60);
            Key(Keys.Control | Keys.B);
            Key(Keys.Escape);
            Check(Fmt(r90) == "ABC |DEF:b:20" && r90.Rotation == 90, "turned 90°: the selection's format: " + Fmt(r90));
            Check(r90.H > th && r90.W > tw, "turned 90°: the box follows along the text (H " + th.ToString("0.0") + " → " + r90.H.ToString("0.0") + ") and across (W " + tw.ToString("0.0") + " → " + r90.W.ToString("0.0") + ")");
            Check(InkInBox(r90, out where), "turned 90°: printed dots inside the box: " + where);

            // ---------- a text only selected: invert and letter spacing on the whole text ----------
            var whole = AddText("Ganz", 60, 4);
            canvas.Select(new LabelObject[] { whole });
            Pump(60);
            var inv = F<ToolStripButton>("invertButton");
            inv.PerformClick();
            F<NumericUpDown>("letterSpacingNum").Value = 2;
            Pump(60);
            Check(whole.Invert && whole.Spacing == 2 && whole.FormatRuns == null, "text only selected: invert and spacing on the whole text (no runs)");
        }
    }
}
