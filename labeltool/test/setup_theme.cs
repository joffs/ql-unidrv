// The setup window's pages in one scheme and language, for checking the colours (Theme.cs): the check, the key-7
// preparation (with the BitLocker line and the blue-screen picture), a wrong start, the signing and install steps
// (with Windows' question as a picture), a failure, the finish in the key-7 start, the view after the restart, a
// blocked start, the uninstall and its done view; the dialogs "What the setup reported" and "Which printer?"; the
// message box (QlMessage) in its four kinds (information, warning, error with details open, Yes/No/Cancel). Preview mode on a fake release folder: nothing on the PC changes,
// the pages are made by calling the window's own methods. A PNG per page (and per picture window). Run through
// deskrun (setup_theme.ps1). setup_theme <out folder> <de|en> <light|dark|highcontrast> (highcontrast: the scheme with
// this PC's system colours, for the layout; real High Contrast colours only when Windows has it on)
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
    static class SetupTheme
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
        struct RECT { public int L, T, R, B; }

        const BindingFlags All = BindingFlags.NonPublic | BindingFlags.Instance;
        static readonly Type T = typeof(InstallForm);
        static string outDir, prefix, root;
        static int failed;   // messages higher than 690 px

        [STAThread]
        static int Main(string[] args)
        {
            outDir = args[0];
            Lang.Force(args[1]);
            Theme.Force(args[2] == "dark" ? Scheme.Dark : args[2] == "highcontrast" ? Scheme.HighContrast : Scheme.Light);
            prefix = args[2] + "-" + args[1] + "-";
            InstallForm.Preview = true;
            Application.EnableVisualStyles();
            root = FakeRelease(Path.Combine(outDir, "release"));
            int failures = 0;
            foreach (var page in new Action<Form>[] { Check, Prepare, Missed, Signing, Installing, Failed, Finished, AfterRestart, Blocked, Uninstall, Removed })
            {
                try { Page(page); }
                catch (Exception e) { Console.WriteLine("FAIL " + page.Method.Name + ": " + (e.InnerException ?? e)); failures++; }
            }
            try { Messages(); }
            catch (Exception e) { Console.WriteLine("FAIL messages: " + (e.InnerException ?? e)); failures++; }
            failures += failed;
            Console.WriteLine(failures == 0 ? "DONE all pages shot" : "DONE " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        // A release folder with the installed driver's DriverVer (as setup_states.cs)
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
        static object State(string name) { return Enum.Parse(T.GetNestedType("StepState", BindingFlags.NonPublic), name); }

        static void Pump(int ms)
        {
            var until = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < until) { Application.DoEvents(); Thread.Sleep(15); }
        }

        // A fresh window after its check, the page made, shot, closed
        static void Page(Action<Form> make)
        {
            var form = new InstallForm(root, false) { StartPosition = FormStartPosition.Manual, Location = new Point(30, 30) };
            form.Show();
            for (int i = 0; i < 600 && F(form, "mode").ToString() == "Checking"; i++) Pump(50);
            Set(form, "arm", true);
            Set(form, "readyPrinters", new List<string> { "Brother QL-800 (Unidrv)" });
            make(form);
            Pump(600);
            Shot(form, make.Method.Name.ToLowerInvariant());
            var picture = (Form)F(form, "pictureWindow");
            if (picture != null) Shot(picture, make.Method.Name.ToLowerInvariant() + "-picture");
            if (make == Failed) Modal(form, "ShowDetails", "failed-details");
            if (make == Finished)
            {
                Set(form, "readyPrinters", new List<string> { "Brother QL-800 (Unidrv)", "Brother QL-570 (Unidrv)" });
                Modal(form, "PrintTest", "finished-which-printer");
            }
            Set(form, "mode", Enum.Parse(T.GetNestedType("Mode", BindingFlags.NonPublic), "Done"));
            form.Close();
            form.Dispose();
            Pump(300);
        }

        static void Check(Form f) { }

        static void Prepare(Form f)
        {
            Set(f, "encrypted", (bool?)true);
            Call(f, "ShowPrepare", false);
            Call(f, "OpenPicture");
        }

        static void Missed(Form f)
        {
            Set(f, "encrypted", (bool?)false);
            Call(f, "ShowPrepare", true);
        }

        // As RunInstall in the key-7 start: signing turns, the rest waits
        static void Signing(Form f)
        {
            Set(f, "checksOff", true);
            Call(f, "ShowStep", F(f, "special"), true);
            Call(f, "Set", F(f, "special"), State("Done"), Lang.T("Started with key 7."));
            Call(f, "Set", F(f, "sign"), State("Working"), Lang.T("Making a certificate for this PC and signing the driver..."));
            Call(f, "Busy", F(f, "sign"));
            ((Control)F(f, "main")).Visible = false;
        }

        // Signed; Windows asks about the publisher: the picture of its question
        static void Installing(Form f)
        {
            Signing(f);
            Call(f, "Set", F(f, "sign"), State("Done"), Lang.T("Signed."));
            Call(f, "Set", F(f, "install"), State("Working"), Lang.T("Adding the driver to Windows..."));
            Call(f, "Busy", F(f, "install"));
            Call(f, "ShowPicture", (SetupPictureKind?)SetupPictureKind.InstallAnyway);
            Call(f, "OpenPicture");
        }

        static void Failed(Form f)
        {
            Signing(f);
            Call(f, "Set", F(f, "sign"), State("Done"), Lang.T("Signed."));
            Call(f, "Fail", F(f, "install"), Lang.T("Adding the driver to Windows...") + " 0x800B0109", "pnputil: 0x800B0109", false);
        }

        // Done in the key-7 start: the restart note in the warning colour, the done box
        static void Finished(Form f)
        {
            Set(f, "checksOff", true);
            Call(f, "ShowStep", F(f, "special"), true);
            Call(f, "Set", F(f, "special"), State("Done"), Lang.T("Started with key 7."));
            Call(f, "Set", F(f, "sign"), State("Done"), Lang.T("Signed."));
            Call(f, "Set", F(f, "install"), State("Done"), Lang.T("Updated. In the Start menu: QL Label Formats, QL Label Maker and QL Printer Status."));
            Call(f, "Finish", false);
        }

        static void AfterRestart(Form f) { Call(f, "ShowDone", false); }

        static void Blocked(Form f) { Call(f, "Block", Lang.T("Nothing was changed. You can try again."), Lang.T("Check this PC")); }

        static void Uninstall(Form f)
        {
            Set(f, "uninstallScripts", Path.Combine(root, "driver"));
            Call(f, "ShowUninstall");
        }

        static void Removed(Form f)
        {
            Uninstall(f);
            Set(f, "labelsKept", true);
            Call(f, "ShowRemoved", new List<string> { "Brother QL-800 (Unidrv)" });
        }

        // A dialog the window shows modally: shot from a timer while it is open, then cancelled (nothing is printed)
        static void Modal(Form owner, string method, string name)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 800 };
            timer.Tick += delegate
            {
                timer.Stop();
                foreach (Form open in Application.OpenForms)
                    if (open.Modal) { Shot(open, name); open.DialogResult = DialogResult.Cancel; return; }
                Console.WriteLine("FAIL " + name + ": no dialog open");
            };
            timer.Start();
            Call(owner, method);
            timer.Dispose();
        }

        // QlMessage's kinds, each shot while open, then cancelled
        static void Messages()
        {
            Message("message-info", () => QlMessage.Show(null, Lang.T("The help isn't installed. Setup.exe repairs the installation."), "QL Label Maker",
                                                         MessageBoxButtons.OK, MessageBoxIcon.Information));
            Message("message-warning", () => QlMessage.Show(null, Lang.F("The printer has {0} loaded, but '{1}' needs {2}.\n\nThe printer would stop with a 'wrong media' error (red LED). Load the matching roll first.",
                                                                          "62 mm", "29 x 90", "29 x 90 mm") + "\n\n" + Lang.T("Print anyway?"), "QL Label Formats",
                                                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2));
            Message("message-error", () => QlMessage.Show(null, Lang.T("Cannot read the label formats:") + "\n" + @"C:\ProgramData\QLUnidrv\labels.json: Unexpected character at line 14.",
                                                          "QL Label Formats", MessageBoxButtons.OK, MessageBoxIcon.Error), open: true);
            Message("message-question", () => QlMessage.Show(null, Lang.F("{0} is set to 'Use Printer Offline': the label would wait on this PC and not print.\n\nUse the printer online now? Windows may ask for administrator permission.",
                                                                           "Brother QL-800 (Unidrv)"), "QL Label Maker", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning));
            // Removing a printer in the Printer setup window: No preselected
            foreach (var r in new[] { new[] { "network", "Brother QL-720NW (Unidrv)", "IP_192.0.2.50", "0" },
                                      new[] { "share", "Brother QL-570 (Unidrv)", @"\\OFFICE-PC\QL-570", "0" },
                                      new[] { "usb", "Brother QL-800 (Unidrv)", "USB001", "2" } })
            {
                var q = SetupForm.RemoveQuestion(r[1], r[2], int.Parse(r[3]));
                Message("remove-" + r[0], () => QlMessage.Ask(null, Lang.T("QL Printer Setup"), q[0], q[1], MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                                                              MessageBoxDefaultButton.Button2));
            }
            // a text far too long for the window: it scrolls, the window stays within 690 px
            Message("message-long", () => QlMessage.Show(null, "Many printers:\n\n" + string.Join("\n", Enumerable.Range(1, 60).Select(i => "Brother QL-800 (Unidrv) " + i)),
                                                         "QL Printer Status", MessageBoxButtons.OK, MessageBoxIcon.Information));
        }

        // open: the details shown first
        static void Message(string name, Action show, bool open = false)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 700 };
            timer.Tick += delegate
            {
                timer.Stop();
                foreach (Form f in Application.OpenForms)
                    if (f.Modal)
                    {
                        if (open)
                            foreach (var link in Each<LinkLabel>(f)) { link.GetType().GetMethod("OnLinkClicked", All).Invoke(link, new object[] { new LinkLabelLinkClickedEventArgs(link.Links[0]) }); break; }
                        Pump(400);
                        Shot(f, name);
                        int h = f.Height * 96 / f.DeviceDpi;
                        if (h > 690) { Console.WriteLine("FAIL " + name + ": " + h + " px high (logical)"); failed++; }
                        f.DialogResult = DialogResult.Cancel;
                        return;
                    }
                Console.WriteLine("FAIL " + name + ": no message open");
            };
            timer.Start();
            show();
            timer.Dispose();
        }

        static IEnumerable<T> Each<T>(Control c) where T : Control
        {
            foreach (Control child in c.Controls)
            {
                if (child is T) yield return (T)child;
                foreach (var d in Each<T>(child)) yield return d;
            }
        }

        static void Shot(Form f, string name)
        {
            RECT r; GetWindowRect(f.Handle, out r);
            using (var b = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
            {
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(Path.Combine(outDir, prefix + name + ".png"), ImageFormat.Png);
            }
            Console.WriteLine("shot " + prefix + name);
        }
    }
}
