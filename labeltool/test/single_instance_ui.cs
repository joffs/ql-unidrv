// One window per program (SingleInstance.cs, EditorForm.SingleInstance.cs, MainForm.SingleInstance.cs;
// 2026-11-09). The test process is the running window; second starts are this exe again in its "client" mode, which
// runs the programs' own hand-over (EditorProgram.ForRunning / Program.IsWindowStart + SingleInstance.HandOff). Names of
// their own (SingleInstance.Suffix): the installed programs are never reached, and a running QL Label Maker or QL Label
// Formats of the user doesn't matter.
//   maker:   a second start without a file (window restored from minimised), with a file (relative path, from another
//            folder), the same file again (no question, no second tab), a changed label and another file: a new tab, no
//            question (tabs 0.23); a dialog open: the file waits until it is closed; --printer/--format: a new label in
//            a tab; two starts at once; after Release (language switch) the next start runs on its own.
//   formats: a second start restores the window, --printer chooses that printer, --new-format adds the format; which
//            starts take part (none, --printer) and which never do (Setup.exe, --apply, --sync, --plan, ...).
// Changes nothing of the user's: label files in a folder of its own under %TEMP%, registry keys of its own (all removed
// afterwards); QL Label Formats is left without saving. Prints CHECK lines; only through deskrun.exe.
// Run by single-instance-ui.ps1:  single_instance_ui.exe <de|en> <maker|formats>
//                                 single_instance_ui.exe client <suffix> <maker|formats> [args...]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class SingleInstanceUi
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnableWindow(IntPtr h, bool enable);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static int failures;
        static string suffix, folder, part;

        [STAThread]
        static int Main(string[] args)
        {
            if (args[0] == "client")
            {
                // a second start: 0 handed over, 2 it would have opened a window of its own
                SingleInstance.Suffix = args[1];
                var rest = args.Skip(3).ToArray();
                bool handed = args[2] == "maker" ? SingleInstance.HandOff(EditorProgram.AppId, EditorProgram.ForRunning(rest))
                                                 : Program.IsWindowStart(rest, false) && SingleInstance.HandOff(Program.AppId, rest);
                return handed ? 0 : 2;
            }
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(120000);
                Console.WriteLine("CHECK watchdog: the test ended within 120 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args[0]);
            part = args[1];
            suffix = ".UiTest" + Process.GetCurrentProcess().Id;
            SingleInstance.Suffix = suffix;
            string recentKey = RecentLabels.Key, lastKey = QlPrinters.LastKey;
            RecentLabels.Key = Lang.SettingsKey + @"\UiTest-" + Guid.NewGuid().ToString("N");
            QlPrinters.LastKey = RecentLabels.Key;
            folder = Path.Combine(Path.GetTempPath(), "ql-single-ui-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Application.EnableVisualStyles();
            try
            {
                if (part == "maker") Maker();
                else Formats();
            }
            catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(RecentLabels.Key, false); } catch (Exception) { }
                RecentLabels.Key = recentKey;
                QlPrinters.LastKey = lastKey;
                try { Directory.Delete(folder, true); } catch (Exception) { }
            }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] + " " + part : "DONE " + args[0] + " " + part + " " + failures + " failed");
            Console.Out.Flush();
            // QL Label Formats isn't closed (it would ask about the new format): the process just ends
            Environment.Exit(failures == 0 ? 0 : 1);
            return 0;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + Lang.Current + " " + part + ": " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // Pumps until done() or ms are over; true: done.
        static bool Until(Func<bool> done, int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { if (done()) return true; Application.DoEvents(); Thread.Sleep(10); }
            return done();
        }

        // A second start (this exe in client mode); the window keeps working meanwhile. Exit code, -1: it didn't end in 15 s.
        static Process StartClient(string program, string workDir, params string[] a)
        {
            var psi = new ProcessStartInfo(Application.ExecutablePath,
                "client " + EditorProgram.Quote(suffix) + " " + program + " " + string.Join(" ", a.Select(EditorProgram.Quote)))
            { UseShellExecute = false, WorkingDirectory = workDir ?? folder };
            return Process.Start(psi);
        }

        static int Client(string program, string workDir, params string[] a)
        {
            using (var p = StartClient(program, workDir, a))
            {
                bool ended = Until(() => p.HasExited, 15000);
                return ended ? p.ExitCode : -1;
            }
        }

        // The question is modal: it runs inside whatever pumps the window, so a timer answers it.
        static DialogResult? answer;
        static int questions, buttons;
        static readonly System.Windows.Forms.Timer answerer = new System.Windows.Forms.Timer { Interval = 100 };
        static void StartAnswering()
        {
            answerer.Tick += delegate
            {
                var q = Question();
                if (q == null || !q.Visible || !answer.HasValue) return;
                questions++;
                buttons = AllControls(q).OfType<Button>().Count();
                var result = answer.Value;
                answer = null;
                Answer(result);
            };
            answerer.Start();
        }

        static QlMessage.MessageForm Question()
        {
            return Application.OpenForms.OfType<QlMessage.MessageForm>().FirstOrDefault();
        }

        // Answers the open question with that button.
        static bool Answer(DialogResult result)
        {
            var q = Question();
            if (q == null) return false;
            var b = AllControls(q).OfType<Button>().FirstOrDefault(x => x.DialogResult == result);
            if (b == null) return false;
            b.PerformClick();
            return true;
        }

        static IEnumerable<Control> AllControls(Control c)
        {
            foreach (Control x in c.Controls)
            {
                yield return x;
                foreach (var y in AllControls(x)) yield return y;
            }
        }

        static string NewLabel(string name)
        {
            string p = Path.Combine(folder, name + LabelDoc.Extension);
            new LabelDoc { FormName = "62mm", PaperId = 259 }.Save(p);
            return p;
        }

        // ---------------------------------------------------------------------------------------------------- maker
        static void Maker()
        {
            Check(!SingleInstance.HandOff(EditorProgram.AppId, new string[0]), "the first start is the window");
            var form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            SingleInstance.Serve(form, form.StartedAgain);
            var pathField = typeof(EditorForm).GetField("path", All);
            var dirtyField = typeof(EditorForm).GetField("dirty", All);
            Func<string> path = () => (string)pathField.GetValue(form);
            Action<bool> dirty = v => dirtyField.SetValue(form, v);
            form.Show();
            Pump(500);
            StartAnswering();

            // without a file: only to the front, restored
            form.WindowState = FormWindowState.Minimized;
            Pump(200);
            var w = Stopwatch.StartNew();
            int code = Client("maker", null);
            Check(code == 0, "second start without a file handed over (exit " + code + ", " + w.ElapsedMilliseconds + " ms)");
            Check(Until(() => form.WindowState == FormWindowState.Normal, 3000), "the window restored from minimised");
            Check(path() == null && Question() == null, "nothing opened, nothing asked");

            // a file, given relative to another folder
            string a = NewLabel("Etikett A"), b = NewLabel("Etikett B"), c = NewLabel("Etikett C");
            code = Client("maker", folder, Path.GetFileName(a));
            Check(code == 0 && Until(() => path() == a, 3000), "a file (relative path): opened in the running window (" + path() + ")");

            // tabs (0.23): every file in a tab of its own, never a question
            var tabsField = typeof(EditorForm).GetField("tabs", All);
            Func<int> tabCount = () => ((System.Collections.IList)tabsField.GetValue(form)).Count;
            Check(tabCount() == 1, "the first file took the empty label's place (" + tabCount() + " tab)");

            // the same file again: no question even with changes, its tab only
            dirty(true);
            code = Client("maker", null, a);
            Pump(500);
            Check(code == 0 && questions == 0 && path() == a && tabCount() == 1, "the same file again: only to the front, no question, no second tab");

            // changed label, another file: a new tab, no question; the changed label keeps its changes in its tab
            answer = DialogResult.Cancel;
            code = Client("maker", null, b);
            Check(code == 0 && Until(() => path() == b, 5000), "changed label, another file: opened in a new tab (" + path() + ")");
            Pump(300);
            Check(questions == 0 && tabCount() == 2, "no question, " + tabCount() + " tabs");
            var first = ((System.Collections.IList)tabsField.GetValue(form))[0];
            Check((bool)first.GetType().GetField("Dirty").GetValue(first), "the first label keeps its changes in its tab");
            answer = null;

            // a dialog open (the window disabled, as under Save as or Print with data): the file waits
            var dialog = new Form { Text = "dialog", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(200, 120) };
            dialog.Show(form);
            EnableWindow(form.Handle, false);
            Pump(300);
            dirty(true);
            code = Client("maker", null, c);
            Pump(800);
            Check(code == 0 && path() == b && tabCount() == 2, "a dialog open: handed over, the file waits (" + path() + ")");
            EnableWindow(form.Handle, true);
            dialog.Close();
            Check(Until(() => path() == c, 5000) && questions == 0 && tabCount() == 3, "the dialog closed: the file opened in a new tab, nothing asked (" + path() + ")");

            // --printer / --format without a file: a new label in a new tab
            code = Client("maker", null, "--printer", "QL-800 Capture", "--format", "62mm");
            Check(code == 0 && Until(() => path() == null, 3000) && tabCount() == 4, "--printer and --format: a new label in a tab of its own (" + tabCount() + " tabs)");

            // two starts at once with open files: both handed over, their tabs come forward, no new ones
            using (var p1 = StartClient("maker", null, a))
            using (var p2 = StartClient("maker", null, b))
            {
                bool ended = Until(() => p1.HasExited && p2.HasExited, 15000);
                Check(ended && p1.ExitCode == 0 && p2.ExitCode == 0, "two starts at once: both handed over");
            }
            Pump(500);
            Check(Application.OpenForms.OfType<EditorForm>().Count() == 1 && (path() == a || path() == b) && tabCount() == 4,
                  "still one window, one of the two files' tabs active, no new tab (" + path() + ", " + tabCount() + " tabs)");
            foreach (var t in (System.Collections.IList)tabsField.GetValue(form)) t.GetType().GetField("Dirty").SetValue(t, false);

            // several labels selected in Explorer (one start each) while a dialog is open: every one opens once it closes
            // (review 2026-11-11: only the last one did)
            string d1 = NewLabel("Etikett D"), d2 = NewLabel("Etikett E"), d3 = NewLabel("Etikett F");
            var busy = new Form { Text = "dialog", ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(60, 60), Size = new Size(200, 120) };
            busy.Show(form);
            EnableWindow(form.Handle, false);
            Pump(300);
            Check(Client("maker", null, d1) == 0 && Client("maker", null, d2) == 0 && Client("maker", null, d3) == 0, "three starts while a dialog is open: handed over");
            Pump(800);
            Check(tabCount() == 4, "the files wait while the dialog is open (" + tabCount() + " tabs)");
            EnableWindow(form.Handle, true);
            busy.Close();
            Check(Until(() => tabCount() == 7, 5000) && path() == d3 && questions == 0,
                  "the dialog closed: all three opened, the last one active (" + tabCount() + " tabs, " + path() + ")");
            foreach (var t in (System.Collections.IList)tabsField.GetValue(form)) t.GetType().GetField("Dirty").SetValue(t, false);

            // the language switch: the window lets go, the next start runs on its own
            SingleInstance.Release();
            code = Client("maker", null);
            Check(code == 2, "after Release the next start opens its own window (exit " + code + ")");

            dirty(false);
            form.Close();
            Pump(200);
        }

        // ---------------------------------------------------------------------------------------------------- formats
        static void Formats()
        {
            foreach (var mode in new[] { "--apply", "--sync", "--plan", "--remove-forms", "--identify", "--online", "--install", "--uninstall",
                                         "--setup", "--status", "--monitor", "--service", "--editor" })
                Check(!Program.IsWindowStart(new[] { mode, "x" }, false), mode + " never hands over");
            Check(!Program.IsWindowStart(new string[0], true), "Setup.exe never hands over");
            Check(Program.IsWindowStart(new string[0], false) && Program.IsWindowStart(new[] { "--printer", "P" }, false), "a plain start and --printer do");

            Check(!SingleInstance.HandOff(Program.AppId, new string[0]), "the first start is the window");
            var form = new MainForm { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ShowInTaskbar = false };
            SingleInstance.Serve(form, form.StartedAgain);
            form.Show();
            Pump(1500);

            form.WindowState = FormWindowState.Minimized;
            Pump(200);
            int code = Client("formats", null);
            Check(code == 0, "second start handed over (exit " + code + ")");
            Check(Until(() => form.WindowState == FormWindowState.Normal, 3000), "the window restored from minimised");

            // QL Label Maker's start: the printer and a new format
            var formats = typeof(MainForm).GetField("formats", All);
            Func<int> count = () => formats != null ? ((System.Collections.ICollection)formats.GetValue(form)).Count : -1;
            int before = count();
            code = Client("formats", null, "--printer", "QL-800 Capture", "--new-format", "259", "400");
            Check(code == 0 && Until(() => (string)typeof(MainForm).GetField("testPrinter", All).GetValue(form) == "QL-800 Capture", 3000),
                  "--printer: that printer chosen");
            Check(Until(() => count() == before + 1, 3000), "--new-format: the new format added (" + before + " -> " + count() + ")");

            // a command mode is never caught: the client doesn't even try
            code = Client("formats", null, "--plan");
            Check(code == 2, "--plan runs on its own (exit " + code + ")");
        }
    }
}
