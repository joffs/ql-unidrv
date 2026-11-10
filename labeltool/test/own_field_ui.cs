// QL Label Maker: "Insert field ▾" > "Custom field..." (OwnFieldDialog.cs). Checks the name rules (DataFields' own:
// letters, digits, _ and -, spaces only inside, at most 64 characters, no built-in field, no braces in the default),
// the field it makes ({Name}, {Name|} or {Name|default} for report / leave empty / default, read back by DataFields as print with data reads it), the dialog
// (the live field, the three choices without a value, the reason under the name, Insert off while the input can't be used, within 690 px), and the way
// through the text panel's menu into a text being edited (inserted at the caret). Prints CHECK lines; changes nothing
// (nothing saved or printed). Runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken). A watchdog
// ends it after 120 s.
// Run by own-field-ui.ps1:  own_field_ui.exe <de|en> <light|dark|highcontrast> [<picture folder>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using M = QlLabels.OwnFieldDialog.Missing;

namespace QlLabels.Tests
{
    static class OwnFieldUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static int failures;
        static string scheme;

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
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(args.Length > 2 ? args[2] : null); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + scheme : "DONE " + args[0] + " " + scheme + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + scheme + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(object o, string field) { return (T)o.GetType().GetField(field, All).GetValue(o); }
        static IEnumerable<Control> Under(Control c) { return c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Under(x))); }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static void Run(string pictures)
        {
            // ---------- the rules and the field (as print with data reads it) ----------
            Check(OwnFieldDialog.Token("Lagerort", M.Report, "x") == "{Lagerort}" && OwnFieldDialog.Token(" Lagerort ", M.Default, "Lager 1") == "{Lagerort|Lager 1}"
                  && OwnFieldDialog.Token("Lagerort", M.Empty, "x") == "{Lagerort|}" && OwnFieldDialog.Token("Lagerort", M.Default, "") == "{Lagerort|}",
                  "field: {Lagerort}, {Lagerort|}, {Lagerort|Lager 1} (outer spaces of the name dropped; an empty default = empty)");
            var read = DataFields.In("Preis: " + OwnFieldDialog.Token("Lagerort", M.Default, "Lager 1") + " " + OwnFieldDialog.Token("Artikel-Nr", M.Report, "")
                                     + " " + OwnFieldDialog.Token("labelname", M.Empty, "")).ToList();
            Check(read.Count == 3 && read[0].Name == "Lagerort" && read[0].Default == "Lager 1" && !read[0].Required && read[1].Name == "Artikel-Nr" && read[1].Required
                  && read[2].Name == "labelname" && read[2].Default == "" && !read[2].Required,
                  "print with data reads the fields back (name, default, required; left empty = not required, empty default)");
            Check(OwnFieldDialog.Problem("", M.Report, "") == "" && OwnFieldDialog.Problem("  ", M.Default, "x") == "", "an empty name: nothing to say, Insert off");
            foreach (var good in new[] { "Lagerort", "Artikel Nr", "Artikel-Nr", "_x", "Größe", "a" })
                Check(OwnFieldDialog.Problem(good, M.Default, "Lager | 1") == null, "allowed: " + good);
            var bad = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("-x", ""), new KeyValuePair<string, string>("x-", ""), new KeyValuePair<string, string>("Lager{", ""), new KeyValuePair<string, string>("a|b", ""), new KeyValuePair<string, string>("a.b", ""), new KeyValuePair<string, string>("date", ""), new KeyValuePair<string, string>("TIME", ""), new KeyValuePair<string, string>("gs", ""), new KeyValuePair<string, string>("x1D", ""),
                new KeyValuePair<string, string>(new string('a', 65), ""), new KeyValuePair<string, string>("Lagerort", "a{b"), new KeyValuePair<string, string>("Lagerort", "}"),
            };
            foreach (var b in bad)
            {
                string p = OwnFieldDialog.Problem(b.Key, M.Default, b.Value);
                Check(!string.IsNullOrEmpty(p), "refused: '" + (b.Key.Length > 20 ? b.Key.Substring(0, 20) + "…" : b.Key) + "' / '" + b.Value + "': " + p);
            }
            Check(OwnFieldDialog.Problem(new string('a', 64), M.Report, "") == null, "64 characters are allowed");
            Check(OwnFieldDialog.Problem("date", M.Report, "").Contains("date"), "a built-in name is named in the reason");
            Check(OwnFieldDialog.Problem("Lagerort", M.Empty, "a{b") == null && OwnFieldDialog.Problem("Lagerort", M.Report, "}") == null,
                  "a brace in the default box counts only when the default is chosen");
            Check(OwnFieldDialog.Problem("labelname|", M.Report, "") == Lang.T("Enter a default value below at \"Default value\", without |."),
                  "a bar in the name: the reason points to the default box");
            Check(OwnFieldDialog.Problem("{Lagerort}", M.Report, "") == Lang.T("Leave out { and }: they are added for you."), "braces in the name: the reason says they are added");

            // ---------- the dialog ----------
            using (var d = new OwnFieldDialog())
            {
                d.Show(form);
                Pump(150);
                var name = F<TextBox>(d, "nameBox");
                var def = F<TextBox>(d, "defaultBox");
                var report = F<RadioButton>(d, "report");
                var empty = F<RadioButton>(d, "empty");
                var withDefault = F<RadioButton>(d, "withDefault");
                var shown = F<Label>(d, "shown");
                var problem = F<Label>(d, "problem");
                var insert = F<Button>(d, "insert");
                Check(!insert.Enabled && !problem.Visible && shown.Text == "{" + Lang.T("Name") + "}", "empty: Insert off, no reason, shows " + shown.Text);
                name.Text = "date";
                Pump(30);
                Check(!insert.Enabled && problem.Visible && problem.Text.Length > 0, "'date': Insert off, reason: " + problem.Text);
                name.Text = "Lagerort";
                Pump(30);
                Check(insert.Enabled && !problem.Visible && shown.Text == "{Lagerort}" && report.Checked, "a name, Report when printing chosen: " + shown.Text);
                empty.Checked = true;
                Pump(30);
                Check(shown.Text == "{Lagerort|}" && !report.Checked && !withDefault.Checked && insert.Enabled, "Leave empty: " + shown.Text + ", the others off");
                def.Text = "Lager 1";
                Pump(30);
                Check(shown.Text == "{Lagerort|Lager 1}" && withDefault.Checked && !empty.Checked, "typing a default chooses it: " + shown.Text);
                report.Checked = true;
                Pump(30);
                Check(shown.Text == "{Lagerort}" && !withDefault.Checked && def.Text == "Lager 1", "back to Report: " + shown.Text + " (the default text kept)");
                withDefault.Checked = true;
                Pump(30);
                def.Text = "Lager {1}";
                Pump(30);
                Check(!insert.Enabled && problem.Visible, "a brace in the default: Insert off, reason: " + problem.Text);
                def.Text = "Lager 1";
                Pump(30);
                int limit = d.LogicalToDeviceUnits(690);
                Check(d.Height <= limit && d.Width <= d.LogicalToDeviceUnits(600), "dialog size " + d.Width + " x " + d.Height + " (≤ 690 px high)");
                Check(Under(d).All(c => !c.Visible || c is Form || d.ClientRectangle.Contains(d.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)))),
                      "every part inside the dialog");
                Check(d.AcceptButton == insert && d.CancelButton != null, "Enter inserts, Esc cancels");
                if (Theme.HighContrast) Check(insert.FlatStyle == FlatStyle.Standard, "High Contrast: Insert is Windows' own button");
                else Check(insert.BackColor == Theme.Of(Role.Primary), "Insert in the primary colour");
                if (pictures != null)
                    using (var b = new Bitmap(d.Width, d.Height))
                    {
                        d.DrawToBitmap(b, new Rectangle(Point.Empty, d.Size));
                        b.Save(System.IO.Path.Combine(pictures, "dialog-" + Lang.Current + "-" + scheme + ".png"));
                    }
                insert.PerformClick();
                Pump(30);
                Check(d.DialogResult == DialogResult.OK && d.Field == "{Lagerort|Lager 1}", "Insert gives " + d.Field);
                d.Close();
            }

            // ---------- through the text panel's menu into a text being edited ----------
            var t = new TextObject { X = 4, Y = 4, W = 40, H = 10, Text = "Preis: ", Size = 12, Kind = TextKind.Line };
            EditorRender.FitText(t);
            form.GetType().GetMethods(All).First(m => m.Name == "Add" && m.GetParameters().Length == 1).Invoke(form, new object[] { t });
            Pump(150);
            var canvas = F<EditorCanvas>(form, "canvas");
            canvas.BeginEdit(t);   // the caret at the end
            Pump(60);
            var button = Under(form).OfType<Button>().FirstOrDefault(b => b.Visible && b.Text == Lang.T("Insert field ▾"));
            Check(button != null, "the text panel has Insert field ▾");
            if (button == null) return;
            var menu = (ContextMenuStrip)button.Tag;
            var item = menu.Items.OfType<ToolStripMenuItem>().FirstOrDefault(i => i.Text == Lang.T("Custom field..."));
            Check(item != null && menu.Items.IndexOf(item) == menu.Items.Count - 1, "Custom field... is the menu's last entry");
            if (item == null) return;
            bool seen = false;
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = Application.OpenForms.OfType<OwnFieldDialog>().FirstOrDefault(x => x.Visible);
                if (d == null) return;
                timer.Stop();
                seen = true;
                F<TextBox>(d, "nameBox").Text = "Lagerort";
                F<TextBox>(d, "defaultBox").Text = "Lager 1";
                F<Button>(d, "insert").PerformClick();
            };
            timer.Start();
            item.PerformClick();
            timer.Dispose();
            Pump(150);
            Check(seen, "the menu opens the dialog");
            Check(t.Text == "Preis: {Lagerort|Lager 1}", "inserted at the caret: " + t.Text);
            Check(canvas.EditedText == t, "the text stays in editing after the insert");
            var fields = DataFields.Of(F<LabelDoc>(form, "doc"));
            Check(fields.Any(f => f.Name == "Lagerort" && f.Default == "Lager 1"), "the label's data fields include Lagerort (default Lager 1)");

            // cancelled: nothing inserted
            timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = Application.OpenForms.OfType<OwnFieldDialog>().FirstOrDefault(x => x.Visible);
                if (d == null) return;
                timer.Stop();
                F<TextBox>(d, "nameBox").Text = "Nummer";
                d.DialogResult = DialogResult.Cancel;
            };
            timer.Start();
            item.PerformClick();
            timer.Dispose();
            Pump(100);
            Check(t.Text == "Preis: {Lagerort|Lager 1}", "cancelled: nothing inserted");
        }
    }
}
