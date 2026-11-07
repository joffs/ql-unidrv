// The setup window's finish states, one window each, in Preview mode (nothing on the PC changes) on a fake release
// folder: the check as on this PC, then the architecture and the key-7 start forced and the finish view made, as after
// a good install. Checks the restart note, the key-7 step and the restart offer after the test label; a PNG per state.
// Run through deskrun (setup_states.ps1). setup_states <out folder> <lang>
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class SetupStates
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, ref RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

        const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type T = typeof(InstallForm);
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
        }

        // name, ARM64, key-7 start, app-only update; after = the view after the normal restart (ShowDone)
        // noPrinter = no printer was set up (no test label to print)
        sealed class State { public string Name; public bool Arm, ChecksOff, AppOnly, After, NoPrinter; }

        [STAThread]
        static int Main(string[] args)
        {
            string outDir = args[0];
            Lang.Force(args[1]);
            InstallForm.Preview = true;
            Application.EnableVisualStyles();
            string root = FakeRelease(Path.Combine(outDir, "release"));
            var states = new[]
            {
                new State { Name = "arm64-normal-app-only", Arm = true, AppOnly = true },
                new State { Name = "arm64-key7-app-only", Arm = true, ChecksOff = true, AppOnly = true },
                new State { Name = "arm64-key7-driver", Arm = true, ChecksOff = true },
                new State { Name = "arm64-key7-no-printer", Arm = true, ChecksOff = true, AppOnly = true, NoPrinter = true },
                new State { Name = "arm64-after-restart", Arm = true, After = true },
                new State { Name = "x64-app-only", AppOnly = true },
            };
            foreach (int dpi in new[] { 96, 144 })
                foreach (var s in states) Run(root, s, Path.Combine(outDir, args[1] + "-" + s.Name + (dpi == 96 ? "" : "-" + dpi * 100 / 96) + ".png"), dpi);
            Uninstall(root);
            Console.WriteLine(failures == 0 ? "DONE all states as expected" : "DONE " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        // A release folder with the installed driver's DriverVer, so the check sees the same driver (else a fresh one)
        static string FakeRelease(string dir)
        {
            foreach (var arch in new[] { "arm64", "amd64" })
            {
                Directory.CreateDirectory(Path.Combine(dir, @"out\package\" + arch));
                File.WriteAllText(Path.Combine(dir, @"out\package\" + arch + @"\QLUnidrv.dll"), "");
            }
            Directory.CreateDirectory(Path.Combine(dir, "driver"));
            File.WriteAllText(Path.Combine(dir, @"driver\install.ps1"), "");
            File.WriteAllText(Path.Combine(dir, @"out\package\qlunidrv.inf"),
                              "[Version]\r\nDriverVer=11/01/2026," + (Versions.InstalledDriver() ?? "0.18.304.1114") + "\r\n");
            return dir;
        }

        static object F(Form f, string name) { return T.GetField(name, All).GetValue(f); }
        static void Set(Form f, string name, object v) { T.GetField(name, All).SetValue(f, v); }
        static object Call(Form f, string name, params object[] a) { return T.GetMethod(name, All | BindingFlags.Static).Invoke(f, a); }
        static object StepField(object step, string name) { return step.GetType().GetField(name).GetValue(step); }

        static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
        }

        static void Run(string root, State s, string png, int dpi)
        {
            var form = new InstallForm(root, false) { StartPosition = FormStartPosition.Manual, Location = new Point(30, 30) };
            form.Show();
            for (int i = 0; i < 600 && F(form, "mode").ToString() == "Checking"; i++) Pump(50);
            Console.WriteLine("INFO " + s.Name + ": the check on this PC says " + F(form, "mode"));
            // The app-only check view says nothing about a restart, in any start (user's choice 2026-11-02)
            string intro = ((Control)F(form, "intro")).Text;
            if (F(form, "mode").ToString() == "AppUpdate" && dpi == 96 && s.Name == "arm64-normal-app-only")
                Check(intro == Lang.F("Only the programs are updated; the driver {0} stays.", Versions.Release(Versions.InstalledDriver())),
                      "app-only intro without a restart sentence: " + intro);
            Set(form, "arm", s.Arm);
            Set(form, "checksOff", s.ChecksOff);
            var special = F(form, "special");
            Call(form, "ShowStep", special, s.Arm && !s.AppOnly);  // as RunCheck: the key-7 step is hidden for an app-only update
            Set(form, "readyPrinters", s.NoPrinter ? new List<string>() : new List<string> { "Brother QL-800 (Unidrv)" });
            var stepState = T.GetNestedType("StepState", BindingFlags.NonPublic);
            if (s.After) Call(form, "ShowDone", true);
            else
            {
                Call(form, "Set", F(form, "install"), Enum.Parse(stepState, "Done"), Lang.T("Updated. In the Start menu: QL Label Formats, QL Label Maker and QL Printer Status."));
                Call(form, "Finish", s.AppOnly);
            }
            Pump(500);
            if (dpi != form.DeviceDpi)
            {
                var r = new RECT { L = form.Left, T = form.Top, R = form.Left + form.Width * dpi / form.DeviceDpi, B = form.Top + form.Height * dpi / form.DeviceDpi };
                SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref r);  // WM_DPICHANGED
                Pump(1200);
            }
            var note = (Control)F(form, "restartNote");
            var main = (Button)F(form, "main");
            var extra = (Button)F(form, "extra");
            var close = (Button)F(form, "close");
            bool key7 = s.Arm && s.ChecksOff;
            string tag = s.Name + (dpi == 96 ? "" : " " + dpi * 100 / 96 + " %");
            Check(note.Visible == key7, tag + ": restart note " + (key7 ? "shown" : "hidden"));
            Check((bool)StepField(special, "Shown") == (s.Arm && (key7 || s.After || !s.AppOnly)), tag + ": key-7 step " + (s.Arm && (key7 || s.After || !s.AppOnly) ? "shown" : "hidden"));
            if (key7) Check((string)StepField(special, "Text") == Lang.T("Started with key 7."), tag + ": key-7 step says " + StepField(special, "Text"));
            if (!s.After)
            {
                string expect = !key7 ? Buttons(s.NoPrinter ? "Check again" : "Print test label", null, "Close")
                              : s.NoPrinter ? Buttons("Check again", "Restart now", "Later") : Buttons("Print test label", "Restart now", "Later");
                Check(Row(form) == expect, tag + ": buttons " + Row(form) + " (default " + ((Button)form.AcceptButton).Text + ")");
            }
            Size(form, tag);
            Shoot(form, png);
            if (!s.After && !s.NoPrinter)
            {
                // What PrintTest does after a test label went out (it would print, so only its key-7 branch)
                if (key7)
                {
                    Call(form, "Set", F(form, "test"), Enum.Parse(stepState, "Done"), Lang.T("Did it come out right?"));
                    Call(form, "OfferRestart", Lang.T("Print another"));
                }
                Pump(300);
                Check((main.Text == Lang.T("Restart now")) == key7, tag + ": after the test label the main button is \"" + main.Text + "\"");
                if (key7)
                {
                    Check(Row(form) == Buttons("Print another", "Restart now", "Later") && form.AcceptButton == main, tag + ": after the test label " + Row(form));
                    Size(form, tag + " after the test label");
                    Shoot(form, Path.ChangeExtension(png, null) + "-test-printed.png");
                }
            }
            form.Close();
            form.Dispose();
            Pump(300);
        }

        // "Uninstall" in the check view opens the uninstall view (it once threw: the link set the constructor's
        // parameter instead of the field, 2026-11-02)
        static void Uninstall(string root)
        {
            var form = new InstallForm(root, false) { StartPosition = FormStartPosition.Manual, Location = new Point(30, 30) };
            form.Show();
            for (int i = 0; i < 600 && F(form, "mode").ToString() == "Checking"; i++) Pump(50);
            var link = (LinkLabel)F(form, "uninstallLink");
            string result;
            try
            {
                typeof(LinkLabel).GetMethod("OnLinkClicked", All).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) });
                result = F(form, "mode").ToString();
            }
            catch (TargetInvocationException e) { result = e.InnerException.GetType().Name + ": " + e.InnerException.Message; }
            Pump(300);
            Check(result == "Uninstall", "Uninstall link opens the uninstall view (" + result + ")");
            form.Close();
            form.Dispose();
            Pump(300);
        }

        // The visible buttons left to right, the default one in *stars*
        static string Row(Form f)
        {
            var list = new List<Control>();
            foreach (var n in new[] { "extra", "main", "close" }) { var b = (Button)F(f, n); if (b.Visible) list.Add(b); }
            list.Sort((a, b) => a.Left.CompareTo(b.Left));
            return string.Join(" ", list.ConvertAll(b => b == f.AcceptButton ? "[*" + b.Text + "*]" : "[" + b.Text + "]"));
        }

        static string Buttons(string first, string second, string third)
        {
            // the main button is the default: the first one before the test label, Restart now after it
            bool restartMain = second != null && first != "Print test label";
            var parts = new List<string>();
            parts.Add(restartMain ? "[" + Lang.T(first) + "]" : "[*" + Lang.T(first) + "*]");
            if (second != null) parts.Add(restartMain ? "[*" + Lang.T(second) + "*]" : "[" + Lang.T(second) + "]");
            parts.Add("[" + Lang.T(third) + "]");
            return string.Join(" ", parts);
        }

        // Rule: every window fits ~690 px working height (logical); report the width too (150 % on FHD = 1280 logical)
        static void Size(Form f, string tag)
        {
            int w = f.Width * 96 / f.DeviceDpi, h = f.Height * 96 / f.DeviceDpi;
            Check(h <= 690 && w <= 1000, tag + ": window " + w + " x " + h + " logical");
            foreach (var n in new[] { "extra", "main", "close" })
            {
                var b = (Button)F(f, n);
                if (b.Visible) Check(b.Width >= TextRenderer.MeasureText(b.Text, b.Font).Width, tag + ": \"" + b.Text + "\" fits its button");
            }
        }

        static void Shoot(Form f, string png)
        {
            using (var b = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(png, ImageFormat.Png);
            }
        }
    }
}
