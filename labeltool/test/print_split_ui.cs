// QL Label Maker's Print split button (the command row): "Print" prints as before, its ▾ part opens a menu with Print
// and Print with data... (Ctrl+Shift+P, also File > Print with data...). Checks in light, dark and High Contrast: the
// two parts side by side and equally high, in the primary colours (Windows' own buttons in High Contrast), the row fits
// at the window's smallest width, the menu's commands and shortcuts, Print with data opened from the menu and with
// Ctrl+Shift+P (the dialog is cancelled). Prints CHECK lines; changes nothing (nothing saved or printed). Runs only
// through deskrun.exe (a desktop of its own: no mouse, no focus taken). A watchdog ends it after 120 s.
// Run by print-split-ui.ps1:  print_split_ui.exe <de|en> <light|dark|highcontrast> [<picture folder>]
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class PrintSplitUi
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

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }

        static System.Collections.Generic.IEnumerable<Control> Under(Control c) { return c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Under(x))); }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // Runs open (a menu click or a key) and reports whether the Print with data dialog came up; cancels it.
        static bool OpensPrintWithData(Action open)
        {
            bool seen = false;
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = F<Form>("printDataDialog");
                if (d == null || !d.Visible) return;
                timer.Stop();
                seen = true;
                d.DialogResult = DialogResult.Cancel;
            };
            timer.Start();
            open();
            timer.Dispose();
            Pump(100);
            return seen;
        }

        static void Run(string pictures)
        {
            var print = F<Button>("printButton");
            var more = F<Button>("printMoreButton");
            var menu = F<ContextMenuStrip>("printMenu");
            var row = F<Control>("commandRow");

            // ---------- the window at its smallest width: the row fits, the two parts side by side ----------
            form.Size = new Size(form.MinimumSize.Width, form.Height);
            Pump(200);
            var right = row.PointToClient(more.Parent.PointToScreen(new Point(more.Right, 0))).X;
            Check(right <= row.ClientSize.Width - row.Padding.Right, "the row fits at the smallest width (▾ ends at " + right + " of " + row.ClientSize.Width + ")");
            Check(more.Parent == print.Parent && more.Left == print.Right && more.Top == print.Top && more.Height == print.Height,
                  "▾ right beside Print, as high (" + print.Bounds + ", " + more.Bounds + ")");
            int min = form.LogicalToDeviceUnits(110);
            var boxes = new[] { F<ComboBox>("printerBox"), F<ComboBox>("labelBox") };
            Check(boxes.All(b => b.Width >= min), "printer and label boxes keep their least width (" + string.Join(", ", boxes.Select(b => b.Width)) + ")");
            Check(more.Width < print.Width && more.Width <= form.LogicalToDeviceUnits(30), "▾ is narrow (" + more.Width + " px)");
            Check(print.Text == Lang.T("Print") && more.Text == "▾", "texts: " + print.Text + " / " + more.Text);
            Check(more.AccessibleName == Lang.T("More ways to print") && F<ToolTip>("tips").GetToolTip(more) == Lang.T("More ways to print"),
                  "▾ named for screen readers and in its tooltip: " + more.AccessibleName);

            // ---------- colours ----------
            if (Theme.HighContrast)
                Check(print.FlatStyle == FlatStyle.Standard && more.FlatStyle == FlatStyle.Standard && more.UseVisualStyleBackColor,
                      "High Contrast: both parts are Windows' own buttons");
            else
                Check(more.BackColor == Theme.Of(Role.Primary) && more.ForeColor == Theme.Of(Role.OnPrimary) && more.BackColor == print.BackColor
                      && more.FlatAppearance.BorderColor == print.FlatAppearance.BorderColor,
                      "▾ in the primary colours, as Print (" + more.BackColor.Name + ")");
            Check(more.Enabled == print.Enabled && print.Enabled, "both parts enabled with a printer");

            if (pictures != null)
            {
                Pump(100);
                using (var b = new Bitmap(row.Width, row.Height))
                {
                    row.DrawToBitmap(b, new Rectangle(Point.Empty, row.Size));
                    b.Save(System.IO.Path.Combine(pictures, "row-" + Lang.Current + "-" + scheme + ".png"));
                }
            }

            // ---------- the menu ----------
            more.PerformClick();
            Pump(150);
            Check(menu.Visible, "a click on ▾ opens the menu");
            var items = menu.Items.OfType<ToolStripMenuItem>().ToList();
            Check(items.Count == 2 && items[0].Text == Lang.T("&Print") && items[1].Text == Lang.T("Print with &data..."),
                  "menu: " + string.Join(" | ", items.Select(i => i.Text + " (" + i.ShortcutKeyDisplayString + ")")));
            var bar = form.MainMenuStrip ?? Under(form).OfType<MenuStrip>().First();
            var file = bar.Items.OfType<ToolStripMenuItem>().First();
            var fileItem = file.DropDownItems.OfType<ToolStripMenuItem>().First(i => i.Text == Lang.T("Print with &data..."));
            var filePrint = file.DropDownItems.OfType<ToolStripMenuItem>().First(i => i.Text == Lang.T("&Print"));
            var conv = System.ComponentModel.TypeDescriptor.GetConverter(typeof(Keys));
            Check(fileItem.ShortcutKeys == (Keys.Control | Keys.Shift | Keys.P) && items[1].ShortcutKeyDisplayString == conv.ConvertToString(fileItem.ShortcutKeys)
                  && items[0].ShortcutKeyDisplayString == conv.ConvertToString(filePrint.ShortcutKeys),
                  "the shortcuts as in the File menu: " + items[0].ShortcutKeyDisplayString + ", " + items[1].ShortcutKeyDisplayString);
            if (pictures != null && menu.Visible)
            {
                using (var b = new Bitmap(menu.Width, menu.Height))
                {
                    menu.DrawToBitmap(b, new Rectangle(Point.Empty, menu.Size));
                    b.Save(System.IO.Path.Combine(pictures, "menu-" + Lang.Current + "-" + scheme + ".png"));
                }
            }
            Check(OpensPrintWithData(() => items[1].PerformClick()), "the menu's Print with data... opens the dialog");
            menu.Close();
            Pump(50);

            // ---------- Ctrl+Shift+P ----------
            Check(OpensPrintWithData(() =>
            {
                var m = Message.Create(form.Handle, 0x0100, (IntPtr)(int)Keys.P, IntPtr.Zero);
                typeof(EditorForm).GetMethod("ProcessCmdKey", All).Invoke(form, new object[] { m, Keys.Control | Keys.Shift | Keys.P });
            }), "Ctrl+Shift+P opens Print with data");
            Check(more.Enabled && print.Enabled, "both parts enabled again after the dialog");
        }
    }
}
