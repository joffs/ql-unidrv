// Words with their own format in QL Label Maker's panel (editor-next step 4), driven like a user: text typed and pasted
// into the text box, words selected, Ctrl+B/I/U and Ctrl+Shift+F/K/U, the B/I/U buttons, the font, size and check box
// rows on a selection and on the whole text, mixed values, the kind switch to artistic text, Undo. Prints CHECK lines
// and screenshots at 96 and 144 dpi. Runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken;
// --lock clipboard for the paste); changes nothing: nothing saved or printed.
// Run by runs-ui.ps1:  runs_ui.exe <de|en> <out folder>  ->  CHECK lines, <out folder>\<lang>-<dpi>-<shot>.png
using System;
using System.Collections.Generic;
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
    static class RunsUi
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        struct RECT { public int L, T, R, B; }
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static string outDir, lang;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            lang = args[0]; outDir = args[1];
            Directory.CreateDirectory(outDir);
            Lang.Force(lang);
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", null) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                F<object>("dirty", false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + lang : "DONE " + lang + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static string Runs(TextObject t)
        {
            var r = t.FormatRuns;
            return r == null ? "(one format)" : string.Join("|", r.Select(x => x.Text.Replace("\n", "¶") + (x.Bold == true ? "+b" : x.Bold == false ? "-b" : "") +
                                                                              (x.Italic == true ? "+i" : "") + (x.Size != null ? "@" + x.Size : "")));
        }

        static void Run()
        {
            Pump(1500);
            var canvas = F<EditorCanvas>("canvas");
            var box = F<RichTextBox>("textBox");
            Call("AddTextFrame");
            Pump(300);
            var t = (TextObject)canvas.Selected;
            Func<Keys, bool> key = k =>
            {
                var m = new Message();
                return (bool)typeof(RunsBox).GetMethod("ProcessCmdKey", All).Invoke(box, new object[] { m, k });
            };
            var bold = F<CheckBox>("boldCheck");
            var scope = F<Label>("scopeHint");
            var size = F<NumericUpDown>("sizeNum");

            // Typed into the box: one format.
            box.Focus();
            box.SelectAll();
            box.SelectedText = "Schrauben M4 verzinkt\n100 Stück";
            Pump(200);
            Check(t.Text == "Schrauben M4 verzinkt\n100 Stück" && t.Runs == null, "typed: " + Runs(t));

            // "M4" selected, Ctrl+B: only it bold; the rows show the selection.
            box.Select(10, 2);
            Pump(100);
            Check(key(Keys.Control | Keys.B), "Ctrl+B handled by the text box");
            Pump(200);
            Check(Runs(t) == "Schrauben -b|M4+b| verzinkt¶100 Stück-b" || Runs(t) == "Schrauben |M4+b| verzinkt¶100 Stück", "Ctrl+B on 'M4': " + Runs(t));
            Check(bold.CheckState == CheckState.Checked && scope.Visible && scope.Text.Contains("M4"), "rows show the selection: " + bold.CheckState + ", '" + scope.Text + "'");
            Check(box.SelectionStart == 10 && box.SelectionLength == 2, "selection kept: " + box.SelectionStart + "+" + box.SelectionLength);
            foreach (int dpi in new[] { 96, 144 }) { if (form.DeviceDpi != dpi) ToDpi(dpi); box.Select(10, 2); Pump(300); Shot(lang + "-" + dpi + "-selection"); }
            ToDpi(96);

            // Typing right after "M4" continues bold.
            box.Select(12, 0);
            box.SelectedText = "x";
            Pump(200);
            Check(Runs(t).Contains("M4x+b"), "typing after the bold word continues it: " + Runs(t));

            // Ctrl+Shift+K at the caret (nothing selected): the next character italic.
            box.Focus();
            box.Select(t.Text.Length, 0);
            Check(key(Keys.Control | Keys.Shift | Keys.K), "Ctrl+Shift+K handled");
            box.SelectedText = "!";
            Pump(200);
            Check(t.FormatRuns != null && t.FormatRuns.Last().Text == "!" && t.FormatRuns.Last().Italic == true, "Ctrl+Shift+K at the caret: the typed '!' italic: " + Runs(t));

            // All selected: bold is mixed (■), a click makes all of it bold.
            box.SelectAll();
            Pump(200);
            Check(bold.CheckState == CheckState.Indeterminate && scope.Visible, "all selected: bold mixed: " + bold.CheckState + ", '" + scope.Text + "'");
            foreach (int dpi in new[] { 96, 144 }) { if (form.DeviceDpi != dpi) ToDpi(dpi); box.SelectAll(); Pump(300); Shot(lang + "-" + dpi + "-mixed"); }
            ToDpi(96);
            box.SelectAll();
            bold.Checked = false;   // a click on ■ (Indeterminate counts as checked: the click unchecks it)
            Pump(200);
            Check(t.FullRuns().All(r => r.Bold == true), "click on mixed bold: all bold: " + Runs(t));

            // Size on a selection; then mixed size shows blank.
            box.Select(0, 9);
            Pump(100);
            size.Value = 20;
            Pump(200);
            Check(t.FullRuns()[0].Text == "Schrauben" && t.FullRuns()[0].Size == 20, "size on 'Schrauben': " + Runs(t));
            box.SelectAll();
            Pump(200);
            Check(size.Text == "", "mixed size blank: '" + size.Text + "'");

            // Nothing selected, the box not active: the rows act on the whole text.
            box.Select(0, 0);
            canvas.Focus();
            Pump(100);
            var italic = F<CheckBox>("italicCheck");
            italic.Checked = !italic.Checked;
            Pump(200);
            bool allItalic = t.FullRuns().All(r => r.Italic == true), noneItalic = t.FullRuns().All(r => r.Italic == false);
            Check(allItalic || noneItalic, "italic row without selection: the whole text: " + Runs(t));

            // A paste is the text only, in the format at the caret (Edit > Paste goes through WM_PASTE).
            var rtf = new DataObject();
            rtf.SetData(DataFormats.Rtf, @"{\rtf1\ansi{\fonttbl\f0 Courier New;}\f0\fs40\b PASTED\b0}");
            rtf.SetData(DataFormats.UnicodeText, "PASTED");
            Clipboard.SetDataObject(rtf, true);
            box.Focus();
            box.Select(0, 0);
            SendMessage(box.Handle, 0x0302, IntPtr.Zero, IntPtr.Zero);
            Pump(200);
            Check(t.Text.StartsWith("PASTED") && t.FullRuns()[0].Font == t.Font && t.FullRuns()[0].Size == 20, "paste: text only, the format at the caret: " + Runs(t));
            Clipboard.Clear();

            // RichEdit's own keys do nothing (Ctrl+E would centre the paragraph in the box).
            Check(key(Keys.Control | Keys.E) && key(Keys.Control | Keys.D2), "Ctrl+E / Ctrl+2 swallowed");

            // To artistic text: the first word's format for all of it; Undo brings the words back.
            string runsBefore = Runs(t);
            Call("PushUndo", (object)null);
            typeof(EditorForm).GetMethod("SetTextKind", All).Invoke(null, new object[] { t, TextKind.Line });
            Check(t.Runs == null && t.Size == 20 && t.Kind == TextKind.Line && !t.Text.Contains("\n"), "to artistic text: one format (the first word's): size " + t.Size);
            Call("Undo", false);
            Pump(300);
            var back = (TextObject)F<LabelDoc>("doc").Objects.OfType<TextObject>().First();
            Check(Runs(back) == runsBefore && back.Kind == TextKind.Frame, "Undo: the word formats are back: " + Runs(back));

            // Drawn on the label: the larger word makes the first line taller than one at the text's own size.
            Check(EditorRender.LinesOf(back).Count >= 2, "laid out: " + string.Join(" / ", EditorRender.LinesOf(back)));
            canvas.Select(new LabelObject[] { back });
            Pump(300);
            Shot(lang + "-96-canvas");
        }

        static void Shot(string name)
        {
            var r = form.Bounds;
            using (var b = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(form.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
        }

        static void ToDpi(int dpi)
        {
            if (form.DeviceDpi == dpi) return;
            var r = new RECT { L = form.Left, T = form.Top, R = form.Left + form.Width * dpi / form.DeviceDpi, B = form.Top + form.Height * dpi / form.DeviceDpi };
            SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref r);
            Pump(1500);
        }

        static T F<T>(string field, object set = null)
        {
            var f = typeof(EditorForm).GetField(field, All);
            if (set != null) f.SetValue(form, set);
            return (T)f.GetValue(form);
        }

        static object Call(string method, params object[] args)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(form, args);
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
