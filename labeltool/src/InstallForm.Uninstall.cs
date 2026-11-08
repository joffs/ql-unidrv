// Setup window (InstallForm.cs), part: the uninstall (QlLabelFormats.exe --uninstall, from Installed apps or Printer setup):
// the %TEMP% copy, what will be removed, install.ps1 -Uninstall, the done view.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    partial class InstallForm
    {
        // ---------- uninstall ----------

        // QlLabelFormats.exe --uninstall [--scripts <folder with install.ps1>] [--temp-copy]. The installed exe can't remove its own
        // folder while it runs, so from there it starts a copy of itself in %TEMP% (with install.ps1) and ends; the copy
        // deletes itself when its window closes.
        internal static void Uninstall(string[] args)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath), appDir = Path.GetDirectoryName(InstalledExe);
            int at = Array.IndexOf(args, "--scripts");
            string scripts = at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
            if (scripts == null && string.Equals(exeDir.TrimEnd('\\'), appDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                string temp = Path.Combine(Path.GetTempPath(), "ql-unidrv-uninstall-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                string tempScripts = Path.Combine(temp, "setup"), exe = Path.Combine(temp, Path.GetFileName(Application.ExecutablePath));
                try
                {
                    Directory.CreateDirectory(tempScripts);
                    File.Copy(Application.ExecutablePath, exe);
                    if (File.Exists(Application.ExecutablePath + ".config")) File.Copy(Application.ExecutablePath + ".config", exe + ".config");
                    string script = Path.Combine(appDir, @"setup\install.ps1");
                    if (File.Exists(script)) File.Copy(script, Path.Combine(tempScripts, "install.ps1"));
                    Process.Start(exe, "--uninstall --scripts \"" + tempScripts + "\" --temp-copy");
                    return;
                }
                catch (Exception) { scripts = Path.Combine(appDir, "setup"); }  // no copy: from here (the folder may stay)
            }
            var form = new InstallForm(null, false, scripts ?? Path.Combine(exeDir, "setup"));
            if (Array.IndexOf(args, "--temp-copy") >= 0) form.tempCopy = exeDir;
            Application.Run(form);
        }

        // The %TEMP% copy removes itself a moment after it ended
        void RemoveTempCopy()
        {
            if (tempCopy == null || !Path.GetFileName(tempCopy).StartsWith("ql-unidrv-uninstall-", StringComparison.Ordinal)) return;
            try
            {
                Process.Start(new ProcessStartInfo(NativeTools.Path("cmd.exe"), "/c ping -n 4 127.0.0.1 >nul & rd /s /q \"" + tempCopy + "\"")
                { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() });
            }
            catch (Exception) { }
        }

        // What will be removed: the printers (a shared one in orange: other PCs lose it), the programs, the rest; keep the
        // label formats (ticked).
        void ShowUninstall()
        {
            mode = Mode.Uninstall;
            uninstallMode = true;
            string script = Path.Combine(uninstallScripts, "install.ps1");
            SetSubtitle(Versions.App);
            headline.Text = Lang.T("Uninstall the QL printer driver");
            doneBox.Visible = options.Visible = inSpecial.Visible = details.Visible = uninstallLink.Visible = false;
            ShowPicture(null);
            ShowReport(false);
            foreach (var s in new[] { check, special, sign, install, test }) { Set(s, StepState.Waiting, ""); ShowStep(s, false); }
            intro.Visible = true;
            if (!File.Exists(script))
            {
                intro.Text = Lang.F("Uninstall: use Setup.exe from the release zip ({0} is missing).", script);
                main.Visible = false;
                close.Visible = true;
                close.Text = Lang.T("Close");
                return;
            }
            intro.Text = Lang.T("Removes everything the setup installed on this PC:");
            FillRemoveList();
            removeList.Visible = true;
            labelCount = 0;
            try { if (File.Exists(LabelStore.JsonPath)) labelCount = LabelStore.Load(LabelStore.JsonPath).Formats.Count; } catch (Exception) { }
            keepLabels.Text = Lang.F("Keep my label formats ({0} formats, for a later install)", labelCount);
            keepRow.Visible = File.Exists(LabelStore.JsonPath);
            main.Text = Lang.T("Uninstall");
            main.Enabled = main.Visible = true;
            close.Text = Lang.T("Cancel");
            close.Visible = true;
            AcceptButton = close;  // removing is never the default
            close.Focus();
        }

        void FillRemoveList()
        {
            removeList.SuspendLayout();
            foreach (Control c in removeList.Controls.Cast<Control>().ToList()) c.Dispose();
            removeList.Controls.Clear();
            // em and wide are from the start at 100 %: the new labels get the width at this screen's scaling (150 % test 1324)
            int row = 0, valueWidth = (wide - em * 7) * Font.Height / em;
            Action<string, Control> add = (key, value) =>
            {
                removeList.Controls.Add(Theme.Fore(new Label { Text = key, AutoSize = true, Margin = new Padding(0, 0, em, em / 3) }, Role.Dim), 0, row);
                value.Margin = new Padding(0, 0, 0, em / 3);
                removeList.Controls.Add(value, 1, row++);
            };
            Func<string, Role, Label> text = (t, role) => Theme.Fore(new Label { Text = t, AutoSize = true, MaximumSize = new Size(valueWidth, 0), Margin = Padding.Empty }, role);
            bool first = true;
            foreach (string p in QlPrinters.OurPrinters().Where(n => !Redirected(n)))
            {
                string port = QlDevice.PortOf(p) ?? "", share = ShareName(p);
                var cell = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                cell.Controls.Add(text(port.StartsWith(@"\\") ? Lang.F("{0} on {1}", p, port) : p, Role.Text));
                if (share != null) cell.Controls.Add(text(Lang.F("shared as \"{0}\" – other PCs can't print through it any more", share), Role.WarningText));
                add(first ? Lang.T("Printers") : "", cell);
                first = false;
            }
            add(Lang.T("Programs"), text("QL Label Formats, QL Label Maker, QL Printer Status", Role.Text));
            add(Lang.T("Also"), text(Lang.T("the driver, its certificate and the logs"), Role.Text));
            removeList.ResumeLayout();
        }

        // A printer that a Remote Desktop session redirects here (TS port, "(redirected N)"): install.ps1's Test-RedirectedPrinter
        static bool Redirected(string printer)
        {
            return Regex.IsMatch(QlDevice.PortOf(printer) ?? "", @"^TS\d+$") || Regex.IsMatch(printer, @"\((umgeleitet|redirected) \d+\)$");
        }

        // A printer's share name, or null when it isn't shared
        static string ShareName(string printer)
        {
            if (!IsShared(printer)) return null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k == null ? null : (k.GetValue("Share Name") as string ?? printer);
            }
            catch (Exception) { return printer; }
        }

        // The uninstall's two steps (the setup's check and install steps with their own titles and tips)
        void UninstallSteps()
        {
            check.Title.Text = Lang.T("Close the programs");
            check.Tip = Lang.T("QL Label Formats and QL Label Maker are closed first; QL Printer Status closes by itself.");
            install.Title.Text = Lang.T("Remove printers, driver and programs");
            install.Tip = Lang.T("Windows asks once for your permission. Then the printers, the driver, the programs with their Start menu entries and the certificate are removed.");
        }

        async void RunUninstall()
        {
            // install.ps1 stops for redirected printers (they keep the driver in use): ask before anything is closed
            var redirected = QlPrinters.OurPrinters().Where(Redirected).ToList();
            if (redirected.Count > 0)
            {
                removeList.Visible = keepRow.Visible = false;
                close.Text = Lang.T("Close");
                UninstallSteps();
                ShowStep(install, true);
                Fail(install, Explain("rdp-printer", null), "Printers redirected by a Remote Desktop session use this driver: " +
                     string.Join(", ", redirected.Select(p => "'" + p + "'")), true);
                return;
            }
            for (string open; (open = LabelToolOpen()) != null; )
            {
                if (QlMessage.Show(this, Lang.F("\"{0}\" is open. Save your changes and close it, then click OK.", open), Text,
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    return;
            }
            mode = Mode.Running;
            labelsKept = keepRow.Visible && keepLabels.Checked;
            removeList.Visible = keepRow.Visible = intro.Visible = main.Visible = false;
            close.Text = Lang.T("Close");
            AcceptButton = null;
            UninstallSteps();
            ShowStep(check, true);
            ShowStep(install, true);
            Set(check, StepState.Done, Lang.T("Closed."));
            Set(install, StepState.Working, Lang.T("Windows asks about \"Windows PowerShell\": click Yes."));
            Busy(install);

            string temp = Path.GetTempPath(), resultFile = ScriptRun.TempFile(ScriptRun.SetupResult);
            logFile = ScriptRun.TempFile(ScriptRun.SetupLog);
            ScriptRun.ClearFiles(logFile, resultFile);
            string arguments = " -Uninstall" + (labelsKept ? " -KeepLabels" : "");
            var timer = new Timer { Interval = 400 };
            bool switched = false;
            timer.Tick += delegate
            {
                if (!installRunning || switched) return;
                switched = true;
                Set(install, StepState.Working, Lang.T("Removing printers, driver and programs..."));
            };
            int code;
            try
            {
                timer.Start();
                code = await Task.Run(() => ScriptRun.RunElevated(Path.Combine(uninstallScripts, "install.ps1"), arguments, logFile, resultFile, temp, () => installRunning = true));
            }
            catch (Win32Exception ex)
            {
                timer.Dispose();
                installRunning = false;
                Fail(install, Elevated.Declined(ex) ? Lang.T("Declined, nothing removed. To try again: Try again.") : Lang.T("Could not start the install:") + " " + ex.Message, null);
                return;
            }
            timer.Dispose();
            installRunning = false;
            var result = ScriptRun.ReadResultMap(resultFile);
            string done, error, errorCode;
            result.TryGetValue("done", out done);
            bool ok = code == 0 && (done == "1" || (!result.ContainsKey("error") && !File.Exists(InstalledExe)));
            // What Windows didn't let go (left= lines: a port, a paper size): named in the done view, the log kept for Show details
            var left = ScriptRun.ReadResult(resultFile).Where(kv => kv.Key == "left-port" || kv.Key == "left-paper")
                                             .Select(kv => kv.Key == "left-port" ? Lang.F("port {0}", kv.Value) : Lang.F("paper size \"{0}\"", kv.Value)).ToList();
            if (ok) RemoveSigningCopies();
            if (ok && left.Count == 0) DeleteRunFiles(resultFile);
            if (closeWhenDone) { mode = Mode.Removed; Close(); return; }
            if (!ok)
            {
                result.TryGetValue("error", out error);
                result.TryGetValue("code", out errorCode);
                var body = ScriptRun.FailureLines(logFile);
                Fail(install, Explain(errorCode, error ?? body.LastOrDefault(l => !l.StartsWith(" ") && !l.StartsWith("+")) ?? Lang.T("It stopped without a message.")),
                     string.Join("\r\n", body.Skip(Math.Max(0, body.Count - 40))) + "\r\n\r\nLog: " + logFile, errorCode == "rdp-printer");
                return;
            }
            if (left.Count > 0) failureDetails = string.Join("\r\n", ScriptRun.TranscriptBody(logFile).Skip(Math.Max(0, ScriptRun.TranscriptBody(logFile).Count - 40))) + "\r\n\r\nLog: " + logFile;
            ShowRemoved(left);
        }

        // The uninstall's done view: only the done box (and what stayed, with Show details)
        void ShowRemoved(List<string> left = null)
        {
            mode = Mode.Removed;
            bool rest = left != null && left.Count > 0;
            headline.Text = Lang.T("The QL printer driver is removed");
            doneBox.Title = rest ? Lang.T("Done – removed, except:") : Lang.T("Done – everything is removed.");
            var lines = new List<string>();
            if (rest) lines.Add(string.Join(", ", left) + " – " + Lang.T("Windows didn't let it go; Show details says why."));
            if (labelsKept) lines.Add(Lang.F("Your {0} label formats were kept; a later install takes them over.", labelCount));
            doneBox.Line.Text = string.Join("\n", lines);
            doneBox.Line.Visible = lines.Count > 0;
            details.Visible = rest;
            doneBox.Visible = true;
            removeList.Visible = keepRow.Visible = intro.Visible = false;
            foreach (var s in new[] { check, install }) ShowStep(s, false);
            main.Text = Lang.T("Done");
            main.Visible = true;
            close.Visible = false;
            AcceptButton = main;
            main.Focus();
        }

        // The copies of this PC's one-time signing root that signing left among this user's intermediate CAs (package.ps1
        // removes its own since 2026-10-31; older ones stay until here)
        static void RemoveSigningCopies()
        {
            if (Preview) return;
            try
            {
                using (var store = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.CertificateAuthority,
                                                                                                 System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser))
                {
                    store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                    foreach (var c in store.Certificates.Cast<System.Security.Cryptography.X509Certificates.X509Certificate2>().ToList())
                        if (Regex.IsMatch(c.Subject, @"^CN=QL.* driver root CA")) store.Remove(c);
                }
            }
            catch (Exception) { }
        }
    }
}
