// Unit tests for the setup window's helpers (InstallForm): install.ps1's result file while it is being written, a
// script's error message, the plain-language failures, "opened from inside the zip", the release version.
// Built and run by labeltool\build.cmd (compiled together with src\*.cs, /main:QlLabels.Tests.SetupTest).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QlLabels.Tests
{
    static class SetupTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static int Main()
        {
            Lang.Force("en");  // the checks compare English texts, whatever this PC's language
            string dir = Path.Combine(Path.GetTempPath(), "ql-setup-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, @"out\package"));
            try
            {
                // install.ps1 writes with a BOM (Add-Content -Encoding UTF8) and appends while the window reads.
                string result = Path.Combine(dir, "result.txt");
                File.WriteAllText(result, "step=certificates\r\nstep=package\r\n", new UTF8Encoding(true));
                using (var writer = new FileStream(result, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                {
                    var r = ScriptRun.ReadResult(result);
                    Check(r.Count == 2 && r[0].Key == "step" && r[0].Value == "certificates" && r[1].Value == "package", "result file read while open, BOM skipped");
                }
                File.AppendAllText(result, "error=pnputil failed (5)\r\ncode=pnputil\r\nno equals sign\r\n");
                var all = ScriptRun.ReadResult(result);
                Check(all.Count == 4 && all.Last().Key == "code" && all.Last().Value == "pnputil", "error and code lines, other lines ignored");
                Check(ScriptRun.ReadResult(Path.Combine(dir, "missing.txt")).Count == 0, "missing result file = nothing yet");

                // package.ps1's failure as powershell.exe -File prints it (German Windows)
                string err = "No package in C:\\x to sign.\r\nIn X:\\driver\\package.ps1:97 Zeichen:57\r\n+ ... throw \"No package in $pkg to sign.\" }\r\n" +
                             "    + CategoryInfo          : OperationStopped: (No package...:String) [], RuntimeException\r\n";
                Check(ScriptRun.ErrorMessage("Created root CA certificate AB\r\n", err) == "No package in C:\\x to sign.", "error message = first line of the error output");
                Check(ScriptRun.ErrorMessage("one\r\nlast line\r\n\r\n", "") == "last line", "no error output: last output line");
                Check(ScriptRun.ErrorMessage("", "") == "no message", "nothing at all");

                // Printer setup: install.ps1's English reasons in the window's language
                string tip;
                string sandbox = "No printer can be reached as \\\\192.0.2.1\\QL-800. Check the name, that the other PC is on and shares the printer, and open \\\\192.0.2.1 in Explorer";
                string unreachable = SetupForm.SettingsReason("share-unreachable", sandbox, out tip);
                Check(unreachable == "No printer can be reached at \\\\192.0.2.1\\QL-800. Is the other PC on and the printer shared?" &&
                      tip == "Tip: open \\\\192.0.2.1 once in Explorer and choose \"Remember my credentials\".", "share not reachable: share in the text, PC in the tip (" + unreachable + " | " + tip + ")");
                Check(SetupForm.SettingsReason("other", "Something broke.", out tip) == "The change was stopped." && tip == null, "Printer setup: an unknown reason, its message in the details");
                Check(SetupForm.SettingsReason(null, "Something broke.", out tip) == "The change was stopped.", "Printer setup: no code");
                SetupForm.SettingsReason("port", "x", out tip);
                Check(tip != null && tip.Contains("README"), "Printer setup: port refused, the details say where to send them");
                // details: the message once (the transcript's unwrapped TerminatingError line is left out), the tip first
                string details = SetupForm.FailureDetails("TIP", sandbox, new List<string> { "Settings only.", sandbox }, @"C:\t\x.log");
                Check(details.StartsWith("TIP\r\n\r\n" + sandbox) && details.IndexOf(sandbox) == details.LastIndexOf(sandbox)
                      && details.Contains("Settings only.") && details.EndsWith("Log: C:\\t\\x.log"), "Printer setup details: the message once (" + details + ")");
                // every code install.ps1 gives in -Settings has its own short text (one or two sentences), in both languages
                foreach (string lang in new[] { "en", "de" })
                {
                    Lang.Force(lang);
                    foreach (string c in new[] { "share-unreachable", "no-driver", "not-ours", "share-usb-only", "remove-failed", "busy", "port", "rdp-printer" })
                    {
                        string t = SetupForm.SettingsReason(c, "ENGLISH MESSAGE", out tip);
                        Check(t.Length > 20 && !t.Contains("ENGLISH MESSAGE") && (c == "rdp-printer" || t.Length <= 120), "Printer setup reason " + c + " (" + lang + "): " + t);
                    }
                }
                Lang.Force("de");
                Check(SetupForm.SettingsReason("share-unreachable", sandbox, out tip) == "Unter \\\\192.0.2.1\\QL-800 ist kein Drucker erreichbar. Ist der andere PC eingeschaltet und der Drucker freigegeben?" &&
                      tip == "Tipp: \\\\192.0.2.1 einmal im Explorer öffnen und „Anmeldedaten speichern“ wählen.", "the x64 Sandbox case in German");
                Check(SetupForm.SettingsReason("other", "x", out tip) == "Die Änderung wurde abgebrochen.", "unknown reason in German");
                Lang.Force("en");
                Check(InstallForm.Explain("label-tool-open", "x").Contains("Close it"), "label tool open explained");
                Check(InstallForm.Explain("other", "Something broke.").EndsWith("Something broke."), "unknown code keeps the message");
                Check(InstallForm.Explain(null, "Something broke.").EndsWith("Something broke."), "no code keeps the message");
                // The x64 Sandbox case: no TrustedPublisher store, code=other while in step=certificates
                string cert = InstallForm.Explain("other", "Access denied (0x80070005)", "certificates");
                Check(cert.StartsWith("Windows didn't let the setup add the driver's certificate") && cert.EndsWith("Access denied (0x80070005)"), "failing step named, Windows' message kept");
                Check(InstallForm.Explain("label-tool-open", "x", "app").Contains("Close it"), "a known code wins over the step");
                Check(InstallForm.InstallTip(true).Contains("ARM64") && !InstallForm.InstallTip(false).Contains("ARM64"), "publisher question in the install tip on ARM64 only");

                string temp = @"C:\Users\a\AppData\Local\Temp\";
                Check(InstallForm.IsZipView(@"C:\Users\a\AppData\Local\Temp\Temp1_ql-unidrv-0.17.zip\ql-unidrv-0.17", temp), "zip view folder");
                Check(InstallForm.IsZipView(@"C:\Users\a\AppData\Local\Temp\Temp2_ql.zip", temp), "zip view folder, exe at the top");
                Check(InstallForm.IsZipView(@"C:\Users\a\AppData\Local\Temp\7334310a-f045-4ac1-a9b5-59c19321eba6_ql-unidrv-0.16.299.827-x64-arm64.zip.ba6\ql-unidrv-0.16.299.827-x64-arm64", temp),
                      "zip view folder, Windows 11 24H2 (whole zip in <GUID>_name.zip.xyz)");
                Check(!InstallForm.IsZipView(@"C:\Users\a\Downloads\ql-unidrv-0.17", temp), "extracted folder");
                Check(!InstallForm.IsZipView(@"C:\Users\a\AppData\Local\Temp\ql-unidrv-0.17.zipper\x", temp), "not a zip folder name");
                Check(!InstallForm.IsZipView(@"C:\Users\a\AppData\Local\TempX\a.zip", temp), "not inside Temp");

                File.WriteAllText(Path.Combine(dir, @"out\package\qlunidrv.inf"), "[Version]\r\nSignature=\"$Windows NT$\"\r\nDriverVer=10/26/2026,0.17.299.5\r\n");
                Check(InstallForm.ZipVersion(dir) == "0.17.299.5", "version from DriverVer");
                Check(InstallForm.ZipVersion(Path.Combine(dir, "none")) == null, "no INF, no version");

                // Printers pending deletion ("<name>,0" in the registry until the spooler restarts) are left out
                var registry = new[] { "Brother QL-800 (Unidrv),0", "Brother QL-570 (Unidrv)", "QL-800 Capture" };
                var windows = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "brother ql-570 (unidrv)", "QL-800 Capture", "Microsoft Print to PDF" };
                Check(string.Join("|", QlPrinters.Listed(registry, windows)) == "Brother QL-570 (Unidrv)|QL-800 Capture", "pending deletion left out, case ignored");
                Check(QlPrinters.Listed(registry, null).Count() == 3, "Windows not asked: all registry printers");
                var listedNow = Spooler.ListedPrinters();
                Check(listedNow != null && listedNow.Count > 0, "EnumPrinters lists this PC's printers");
                Check(listedNow == null || QlPrinters.OurPrinters().All(listedNow.Contains), "our printers are all ones Windows lists");

                // ARM64 special start: code integrity options and the boot's start options
                string normal = " NOEXECUTE=OPTIN  HYPERVISORLAUNCHTYPE=AUTO  FVEBOOT=1074806784  NOVGA";
                Check(!InstallForm.SignatureChecksOff(0xC001, normal), "normal start (ARM64 dev PC 2026-10-30): checks on");
                Check(InstallForm.SignatureChecksOff(0xC000, normal), "code integrity off");
                Check(InstallForm.SignatureChecksOff(0xC003, normal), "test mode");
                Check(InstallForm.SignatureChecksOff(0xC001, normal + "  DISABLE_INTEGRITY_CHECKS"), "key 7: DISABLE_INTEGRITY_CHECKS");
                Check(InstallForm.SignatureChecksOff(0xC001, " testsigning"), "TESTSIGNING in the start options");
                Check(!InstallForm.SignatureChecksOff(0xC001, null), "no start options");
                Check(InstallForm.Explain("enforcement-on", "x").Contains("key 7"), "signature checks on explained");

                // Windows' programs native also from a 32-bit process (F7 test 2026-10-30: 32-bit/emulated starts)
                const string win = @"C:\Windows";
                Check(NativeTools.PathFor("powershell.exe", false, true, false, win) == @"C:\Windows\Sysnative\WindowsPowerShell\v1.0\powershell.exe",
                      "32-bit process on 64-bit Windows: PowerShell from Sysnative");
                Check(NativeTools.PathFor("powershell.exe", false, true, true, win) == @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                      "elevated start: System32 (the elevation service starts it natively; it doesn't know Sysnative)");
                Check(NativeTools.PathFor("powershell.exe", true, true, false, win) == @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
                      "64-bit process: System32");
                Check(NativeTools.PathFor("cmd.exe", false, false, false, win) == @"C:\Windows\System32\cmd.exe", "32-bit Windows: System32, no subfolder");
                Check(NativeTools.PathFor("shutdown.exe", false, true, false, win) == @"C:\Windows\Sysnative\shutdown.exe", "other programs: Sysnative too");
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                    Check(NativeTools.IsArm64() == string.Equals(key.GetValue("PROCESSOR_ARCHITECTURE") as string, "ARM64", StringComparison.OrdinalIgnoreCase),
                          "ARM64 detection agrees with the system's PROCESSOR_ARCHITECTURE");

                // The scripts take the PC's architecture from the registry, not from the environment they inherit (a .NET
                // AnyCPU program on ARM64 passes PROCESSOR_ARCHITECTURE=AMD64 on), and restart themselves natively when
                // started as 32-bit PowerShell
                string repo = FindRepo();
                Check(repo != null, "repository (driver\\install.ps1) found above the test");
                if (repo != null)
                {
                    foreach (var script in new[] { @"driver\install.ps1", @"driver\package.ps1", @"driver\check-signing.ps1", @"tools\release\setup.ps1" })
                    {
                        string path = Path.Combine(repo, script);
                        if (!File.Exists(path)) { Check(script == @"tools\release\setup.ps1", script + " exists"); continue; }
                        string text = File.ReadAllText(path);
                        Check(text.IndexOf("$env:PROCESSOR_ARCHITECTURE", StringComparison.OrdinalIgnoreCase) < 0, script + ": no architecture from the environment");
                        if (script.StartsWith(@"driver\install") || script.StartsWith(@"driver\package"))
                            Check(text.Contains(@"Sysnative\WindowsPowerShell\v1.0\powershell.exe") && text.Contains("[IntPtr]::Size -eq 4"),
                                  script + ": restarts itself in the native PowerShell when 32-bit");
                    }

                    // Older certificates go only after the package step, and those a package in the driver store is signed with
                    // stay (ARM64 2026-10-30: a package Windows knew already kept a catalog whose root had been removed)
                    string install = File.ReadAllText(Path.Combine(repo, @"driver\install.ps1"));
                    int package = install.IndexOf("Set-Step \"package\""), removal = install.IndexOf("Removed old root certificate");
                    Check(package > 0 && removal > package && install.IndexOf("$keep -notcontains $_.Thumbprint", removal - 400) > 0,
                          "install.ps1: old certificates removed after the package step, except those signing a stored package");
                    CheckStoreSigning(install, dir);

                    // Debug pass 2026-10-31: the scripts parse; install.ps1's error classes; one run at a time with the name the
                    // window checks; certificates of a failed run removed; only QL printers' USB devices counted
                    foreach (var script in new[] { @"driver\install.ps1", @"driver\package.ps1", @"driver\check-signing.ps1", @"tools\release\setup.ps1" })
                    {
                        string errors = RunPowerShell(dir, "$e = $null; [void][System.Management.Automation.Language.Parser]::ParseFile($args[0], [ref]$null, [ref]$e); @($e).Count",
                                                      Path.Combine(repo, script));
                        Check(errors == "0", script + " parses (" + errors + " errors)");
                        // A command name glued to its argument (Ask"Remove ..." in 1171: CommandNotFoundException) parses, but
                        // names a command with a space or quote in it
                        string glued = RunPowerShell(dir, "$ast = [System.Management.Automation.Language.Parser]::ParseFile($args[0], [ref]$null, [ref]$null)\n" +
                            "@($ast.FindAll({ param($a) $a -is [System.Management.Automation.Language.CommandAst] -and $a.InvocationOperator -eq 'Unknown' -and $a.GetCommandName() -match '[\\s\"'']' }, $true) | " +
                            "ForEach-Object { $_.Extent.StartLineNumber }) -join ','", Path.Combine(repo, script));
                        Check(glued == "", script + ": no command name glued to its argument (lines " + glued + ")");
                    }
                    File.WriteAllText(Path.Combine(dir, "install-copy.ps1"), install);
                    string codes = RunPowerShell(dir, FunctionFrom("Get-ErrorCode") +
                        "(Get-ErrorCode 'QL Label Formats or QL Label Maker is open (pid 12): save your changes'), (Get-ErrorCode 'Another setup is running on this PC: wait'), " +
                        "(Get-ErrorCode 'pnputil failed (-2146762495, 0x800B0109)'), (Get-ErrorCode 'Something else') -join ' '", Path.Combine(dir, "install-copy.ps1"));
                    Check(codes == "label-tool-open busy enforcement-on other", "install.ps1 error classes (" + codes + ")");
                    // -Settings failures (Printer setup): install.ps1's own texts, as thrown
                    string settings = RunPowerShell(dir, FunctionFrom("Get-ErrorCode") +
                        "@('No printer can be reached as \\\\PC\\QL-800. Check the name', \"Driver 'Brother QL-800 (Unidrv)' is not installed here: install\", " +
                        "'The driver and the programs are not installed here: run Setup.exe', \"'X' is not a QL printer with this driver on this PC.\", " +
                        "\"Only a printer on a USB port is shared here; 'X' uses IP_1\", \"Could not remove the printer 'X': busy\") | ForEach-Object { Get-ErrorCode $_ } | Out-String",
                        Path.Combine(dir, "install-copy.ps1")).Replace("\r\n", " ").Trim();
                    Check(settings == "share-unreachable no-driver no-driver not-ours share-usb-only remove-failed", "install.ps1 Printer setup error classes (" + settings + ")");
                    Check(install.Contains("throw (\"No printer can be reached as $SharePort. Check"), "install.ps1: the share message SettingsReason reads the share from");
                    // The setup window's sources are partial files (InstallForm.cs, InstallForm.Checks.cs, ...; SetupForm.cs, ...)
                    string src = Path.Combine(repo, @"labeltool\src");
                    var windowFiles = Directory.GetFiles(src, "InstallForm*.cs").Concat(Directory.GetFiles(src, "SetupForm*.cs")).ToList();
                    Check(install.Contains("\"Global\\QLUnidrvInstall\"") && windowFiles.Any(f => File.ReadAllText(f).Contains("@\"Global\\QLUnidrvInstall\"")),
                          "install.ps1 and the setup window use the same one-run-at-a-time name");
                    Check(install.Contains("foreach ($c in $script:addedCerts)") && install.IndexOf("Get-StoreSigningThumbprints) -notcontains", StringComparison.Ordinal) > 0,
                          "install.ps1: certificates a failed run added are removed unless a stored package uses them");
                    Check(install.Contains("DEVPKEY_Device_BusReportedDeviceDesc") && install.Contains("-like \"QL-*\""), "install.ps1 waits only for QL printers' USB devices");
                    string shown = RunPowerShell(dir, FunctionFrom("Get-Release") + FunctionFrom("Get-AppRelease") +
                        "(Get-AppRelease '0.18.1 (build 305.700)'), (Get-AppRelease '0.18.304.1157'), (Get-Release '0.18.304.1114'), [string](Get-Release $null) -join '|'",
                        Path.Combine(dir, "install-copy.ps1"));
                    Check(shown == "0.18.1|0.18|0.18|", "Installed apps version: the release of the programs and of the driver (" + shown + ")");
                    Check(Versions.Release("0.18.304.1114") == "0.18" && Versions.Stamp("0.18.304.1114") == "304.1114" && Versions.Release(null) == null,
                          "a driver's release and build stamp");
                    Check(Versions.AppRelease("0.18.1 (build 305.700)") == "0.18.1" && Versions.AppRelease("0.18.304.1157") == "0.18", "the programs' release");
                    Check(Versions.CompareRelease("0.18.304.1114", "0.18.304.1291") == 0 && Versions.CompareRelease("0.19.305.1", "0.18.304.1291") > 0 &&
                          Versions.CompareRelease("0.18.304.1114", null) > 0, "drivers compared by their release");
                    Check(Versions.CompareApps("0.18.1 (build 305.700)", "0.18.304.1157") > 0 && Versions.CompareApps("0.18.1 (build 305.700)", "0.18.1 (build 305.700)") == 0 &&
                          Versions.CompareApps("0.18.1 (build 305.701)", "0.18.1 (build 305.700)") > 0 && Versions.CompareApps("0.18.2 (build 1.0)", "0.18.1 (build 305.700)") > 0 &&
                          Versions.CompareApps("0.18.1 (build 305.700)", null) > 0, "programs compared by release and build");
                    string release = File.ReadAllLines(Path.Combine(repo, @"labeltool\version\VERSION"))[0].Trim();
                    string generated = File.ReadAllText(Path.Combine(repo, @"out\obj\labeltool\Version.g.cs"));
                    Check(Regex.IsMatch(generated, "AssemblyFileVersion\\(\"" + Regex.Escape(release) + "\\.\\d+\"\\)") &&
                          Regex.IsMatch(generated, "AssemblyInformationalVersion\\(\"" + Regex.Escape(release) + " \\(build \\d+\\.\\d+\\)\"\\)"),
                          "the exes' file and product version from labeltool\\version\\VERSION");
                    string setupScript = File.ReadAllText(Path.Combine(repo, @"tools\release\setup.ps1"));
                    Check(ScriptRun.RunFileNames.All(n => setupScript.Contains("\"" + n + "\"")), "setup.ps1 removes the same run files as the setup window");
                    // Screenshot tools set InstallForm.Preview: every method that changes the PC either starts with the guard or
                    // only runs after the user clicked (install, uninstall, settings); a harness once left a RunOnce entry
                    var acting = new[] { "RunInstall", "RunUninstall", "RunSettings", "Uninstall", "StartUninstall", "RemoveTempCopy" };
                    Check(windowFiles.Count >= 2, "the setup windows' source files found (" + windowFiles.Count + ")");
                    foreach (string path in windowFiles)
                    {
                        string file = Path.GetFileName(path);
                        string[] lines = File.ReadAllLines(path);
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (!System.Text.RegularExpressions.Regex.IsMatch(lines[i], @"SetValue\(|DeleteValue\(|store\.Remove\(|shutdown\.exe|File\.Delete\(|DeleteFileW\(\w") || lines[i].TrimStart().StartsWith("//") || lines[i].Contains(" extern ")) continue;
                            int h = i;
                            while (h > 0 && !System.Text.RegularExpressions.Regex.IsMatch(lines[h], @"^        (?:internal |public |private )?(?:static )?(?:async )?[\w<>\[\]]+ (\w+)\(.*\)\s*$")) h--;
                            string name = System.Text.RegularExpressions.Regex.Match(lines[h], @" (\w+)\(").Groups[1].Value;
                            bool guarded = h + 2 < lines.Length && lines[h + 1].Trim() == "{" && lines[h + 2].Trim().EndsWith("Preview) return;");
                            Check(guarded || acting.Contains(name), file + ": " + name + " (line " + (i + 1) + ") changes the PC without the Preview guard");
                        }
                    }
                }
                Check(InstallForm.BlockingPolicy(1, null, null) != null && InstallForm.BlockingPolicy(0, null, null) == null && InstallForm.BlockingPolicy(2, null, null) == null,
                      "Smart App Control blocks only when on (1), not off (0) or evaluation (2)");
                Check(InstallForm.BlockingPolicy(null, 1, null) != null && InstallForm.BlockingPolicy(null, null, 1) != null && InstallForm.BlockingPolicy(null, 0, 0) == null,
                      "Windows protected print mode blocks when on");
                Check(InstallForm.Explain("busy", "x").Contains("Another setup"), "another setup running explained");

                // Printer setup: share names as Windows allows them, the model a share's name names (no QL-500 by default)
                Check(SetupForm.IsShareName(@"\\PRINTHOST\QL-800") && SetupForm.IsShareName(@"\\büro-pc\Etiketten & Co") && SetupForm.IsShareName(@"\\host.lan\QL$"),
                      "share names with umlauts, '&', spaces and $");
                Check(!SetupForm.IsShareName(@"\\host\") && !SetupForm.IsShareName(@"\\ho st\x") && !SetupForm.IsShareName(@"\\host\a\b") && !SetupForm.IsShareName(@"host\x"),
                      "not a share name");
                var drivers = new[] { "Brother QL-500 (Unidrv)", "Brother QL-800 (Unidrv)", "Brother QL-1100 (Unidrv)", "Brother QL-1110NWB (Unidrv)" };
                Check(SetupForm.ModelInShareName(@"\\PC\QL-800", drivers) == "Brother QL-800 (Unidrv)", "model from \\\\PC\\QL-800");
                Check(SetupForm.ModelInShareName(@"\\PC\Brother ql1110nwb Etiketten", drivers) == "Brother QL-1110NWB (Unidrv)", "model from a longer share name");
                Check(SetupForm.ModelInShareName(@"\\PC\Etiketten", drivers) == null && SetupForm.ModelInShareName(@"\\PC\QL-570", drivers) == null,
                      "no model named, or not installed: none chosen");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
            Message();
            RemoveQuestion();
            UsbCandidates();
            ShareNames();
            GoneShares();
            Console.WriteLine(failures == 0 ? "setup_test: all OK" : "setup_test: " + failures + " failure(s)");
            return failures == 0 ? 0 : 1;
        }

        // install.ps1's Get-StoreSigningThumbprints on a driver-store-like folder holding one of Windows' signed catalogs
        // (as qlunidrv.cat): the catalog's signer and the root of its chain. Runs the function alone, unelevated.
        static void CheckStoreSigning(string install, string dir)
        {
            string catRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"CatRoot\{F750E6C3-38EE-11D1-85E5-00C04FC295EE}");
            string sample = Directory.Exists(catRoot) ? Directory.GetFiles(catRoot, "*.cat").FirstOrDefault() : null;
            if (sample == null) { Console.WriteLine("  (no Windows catalog to test the store signing check with)"); return; }
            string repository = Path.Combine(dir, "FileRepository"), package = Path.Combine(repository, "qlunidrv.inf_amd64_0123456789abcdef");
            Directory.CreateDirectory(package);
            File.Copy(sample, Path.Combine(package, "qlunidrv.cat"));
            Directory.CreateDirectory(Path.Combine(repository, "other.inf_amd64_0123456789abcdef"));
            string function = Path.Combine(dir, "store-signing.ps1");
            File.WriteAllText(function,
                "$ast = [System.Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($args[0]), [ref]$null, [ref]$null)\n" +
                "$f = $ast.Find({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq 'Get-StoreSigningThumbprints' }, $true)\n" +
                "Invoke-Expression $f.Extent.Text\n" +
                "$signer = (Get-AuthenticodeSignature (Join-Path $args[1] 'qlunidrv.inf_amd64_0123456789abcdef\\qlunidrv.cat')).SignerCertificate.Thumbprint\n" +
                "$t = @(Get-StoreSigningThumbprints $args[2])\n" +
                "'{0} {1} {2}' -f $t.Count, ($t[0] -eq $signer), ($t[1] -ne $signer)\n");
            string installPath = Path.Combine(dir, "install-copy.ps1");
            File.WriteAllText(installPath, install);
            var psi = new System.Diagnostics.ProcessStartInfo(NativeTools.Path("powershell.exe"),
                "-NoProfile -ExecutionPolicy Bypass -File \"" + function + "\" \"" + installPath + "\" \"" + repository + "\" \"" + repository + "\"")
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            string output;
            using (var p = System.Diagnostics.Process.Start(psi)) { output = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); }
            Check(output == "2 True True", "a stored package's signer and its root are kept (" + output + ")");
        }

        // PowerShell text that defines one function of the script given as $args[0] (found by its syntax tree)
        static string FunctionFrom(string name)
        {
            return "$ast = [System.Management.Automation.Language.Parser]::ParseInput([IO.File]::ReadAllText($args[0]), [ref]$null, [ref]$null)\n" +
                   "$f = $ast.Find({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq '" + name + "' }, $true)\n" +
                   "Invoke-Expression $f.Extent.Text\n";
        }

        // QlMessage: the text split, the buttons, Esc, the default, Ctrl+C's text (the window is made, not shown)
        // The question before removing a printer, by port
        static void RemoveQuestion()
        {
            var q = SetupForm.RemoveQuestion("Brother QL-720NW (Unidrv)", "IP_192.0.2.50", 0);
            Check(q[0] == "Remove \"Brother QL-720NW (Unidrv)\"?" && q[1].StartsWith("The printer is removed from this PC. Its port to 192.0.2.50 is removed too.\n\n")
                  && q[1].Contains("\"Network printer\""), "remove: network printer, its port named");
            q = SetupForm.RemoveQuestion("Brother QL-570 (Unidrv)", @"\\OFFICE-PC\QL-570", 1);
            Check(q[1].StartsWith("The printer is removed from this PC. On the PC OFFICE-PC it stays shared") && q[1].Contains("1 waiting print job is deleted.")
                  && q[1].Contains("\"Another PC's printer\""), "remove: share, the other PC named, one job");
            q = SetupForm.RemoveQuestion("Brother QL-800 (Unidrv)", "USB001", 3);
            Check(q[1].Contains("3 waiting print jobs are deleted.") && q[1].Contains("can be added again here"), "remove: USB can be added again here, jobs counted");
            q = SetupForm.RemoveQuestion("QL-800 Capture", @"C:\ProgramData\QLUnidrv\capture\capture.prn", 0);
            Check(q[1].Contains("output file") && !q[1].Contains("waiting"), "remove: capture printer, no jobs line");
            Check(QlMessage.Split(q[0] + " x", false).Headline == q[0], "remove: the headline stays one sentence");
            Lang.Force("de");
            q = SetupForm.RemoveQuestion("Brother QL-800 (Unidrv)", "USB001", 0);
            Check(q[0] == "„Brother QL-800 (Unidrv)“ entfernen?" && q[1] == "Der Drucker wird von diesem PC entfernt.\n\nDer Treiber bleibt installiert. Solange der Drucker per USB angeschlossen ist, lässt er sich hier jederzeit wieder hinzufügen.", "remove: German");
            var pend = new Dictionary<string, string> { { "removed", "Brother QL-720NW (Unidrv)" }, { "pending", "Brother QL-720NW (Unidrv),0" }, { "pending-port", "IP_192.0.2.50" } };
            q = SetupForm.RemoveQuestion("Brother QL-800 (Unidrv)", "USB001", 0, true);
            Check(q[1] == "Der Drucker wird von diesem PC entfernt.\n\nEr ist freigegeben. Andere PCs, die darüber drucken, halten ihn eventuell noch offen; dann wird er erst nach deren oder diesem Neustart ganz entfernt." +
                  "\n\nDer Treiber bleibt installiert. Solange der Drucker per USB angeschlossen ist, lässt er sich hier jederzeit wieder hinzufügen.", "remove: shared printer, German");
            pend["was-shared"] = "1";
            Check(SetupForm.PendingNote(pend).StartsWith("Ein PC, der über die Freigabe druckt (oder seinen Status anzeigt), hat ihn noch geöffnet. Zum Abschließen dort den Dienst „Druckwarteschlange“ neu starten"),
                  "pending of a shared printer: the remedy, German");
            pend.Remove("was-shared");
            Check(SetupForm.PendingNote(pend) == "Ein anderes Programm hat ihn noch geöffnet; Windows schließt das Entfernen ab, sobald er wieder frei ist, spätestens beim nächsten Neustart. " +
                  "Sein Anschluss zu 192.0.2.50 bleibt ungenutzt zurück und stört nicht.", "pending: German, port named by its address");
            Lang.Force("en");
            pend.Remove("pending-port");
            Check(SetupForm.PendingNote(pend).EndsWith("at the latest at the next restart."), "pending without a port of its own (USB)");
            Check(SetupForm.PendingNote(new Dictionary<string, string> { { "removed", "x" } }) == null, "removed for good: no note");
            // The USB line's (i): with several printers their list first, Brother's own printers named
            var qlModel = (Func<string, QlModel>)(n => MediaTable.Models.First(m => m.Name == n));
            var one = new List<UsbCandidate> { new UsbCandidate { Model = qlModel("QL-800"), Port = "USB001" } };
            string d1 = SetupForm.UsbDetails(one);
            Check(d1.StartsWith("Windows doesn't set up a removed USB printer again") && !d1.Contains("Brother's own"), "usb (i): one printer, no Brother line");
            var three = new List<UsbCandidate> { one[0], new UsbCandidate { Model = qlModel("QL-570"), Port = "USB002", BrotherQueue = "Brother QL-570" },
                                                 new UsbCandidate { Model = qlModel("QL-1110NWB"), Port = "USB003" } };
            string d3 = SetupForm.UsbDetails(three);
            Check(d3.StartsWith("QL-800 (USB001), QL-570 (USB002), QL-1110NWB (USB003)\r\n\r\n") && d3.EndsWith("Brother's own printer \"Brother QL-570\" stays alongside it."), "usb (i): three printers listed, Brother's named");
        }

        // Connected USB printers without a printer of ours (Printer setup's "Add printer" lines), the three cases of
        // the USB remove tests 2026-11-06
        // The printer list's mark for a printer on \\host\share (status matrix case 5/7): gone share, host that doesn't
        // answer, slow host (the window never waits longer than its limit)
        static void GoneShares()
        {
            var restore = StatusState.Saved();   // the status readers back, and what was seen forgotten, at the end
            try
            {
                Lang.Force("en");
                PublishedStatus.HostAnswers = h => true;   // the host answers on the network (PC1, PC2 don't exist)
                PublishedStatus.Queue = q => new PublishedStatus.QueueFacts { Attributes = 0x8, ShareName = "QL-800" };
                PublishedStatus.ForgetShareChecks();
                PublishedStatus.NetShare = (h, s) => new[] { 2310, 0 };
                var st = SetupForm.ShareStateWithin("PC1", "QL-800", 2000);
                Check(st == PublishedStatus.ShareState.NotShared && SetupForm.ShareMark(st) == "no longer shared", "gone share: no longer shared");
                PublishedStatus.ForgetShareChecks();
                PublishedStatus.NetShare = (h, s) => new[] { 53, 0 };
                st = SetupForm.ShareStateWithin("PC1", "QL-800", 2000);
                Check(st == PublishedStatus.ShareState.Unreachable && SetupForm.ShareMark(st) == "not reachable", "host not found: not reachable");
                PublishedStatus.ForgetShareChecks();
                PublishedStatus.NetShare = (h, s) => new[] { 0, 1 };
                st = SetupForm.ShareStateWithin("PC1", "QL-800", 2000);
                Check(st == PublishedStatus.ShareState.Shared && SetupForm.ShareMark(st) == null, "shared: no mark");
                PublishedStatus.ForgetShareChecks();
                PublishedStatus.NetShare = (h, s) => new[] { 5, 0 };
                Check(SetupForm.ShareMark(SetupForm.ShareStateWithin("PC1", "QL-800", 2000)) == null, "access denied: unknown, no mark");
                PublishedStatus.ForgetShareChecks();
                PublishedStatus.NetShare = (h, s) => { System.Threading.Thread.Sleep(3000); return new[] { 0, 1 }; };
                var watch = System.Diagnostics.Stopwatch.StartNew();
                st = SetupForm.ShareStateWithin("PC2", "QL-800", 300);
                Check(st == PublishedStatus.ShareState.Unreachable && watch.ElapsedMilliseconds < 1500, "slow host: not reachable after the limit (" + watch.ElapsedMilliseconds + " ms)");
                Lang.Force("de");
                Check(SetupForm.ShareMark(PublishedStatus.ShareState.NotShared) == "nicht mehr freigegeben" &&
                      SetupForm.ShareMark(PublishedStatus.ShareState.Unreachable) == "nicht erreichbar", "German marks");
            }
            finally
            {
                restore.Dispose();
                Lang.Force("en");
            }
        }

        // "Share a printer": the share name check (as install.ps1 Test-ShareName), the suggestion, which ports can be shared
        static void ShareNames()
        {
            Lang.Force("en");
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "QL-800", "Brother QL-800 (Unidrv)" }, { "Brother QL-570", "Brother QL-570" }, { "Scans", "" },
            };
            Check(Sharing.NameProblem("QL-570", "Brother QL-570 (Unidrv)", taken) == null, "share name: free");
            Check(Sharing.NameProblem("  ", "Brother QL-570 (Unidrv)", taken) == "", "share name: empty, no text (button grey)");
            Check(Sharing.NameProblem("ql-800", "Brother QL-570 (Unidrv)", taken) == "\"ql-800\" is taken on this PC (printer \"Brother QL-800 (Unidrv)\").", "share name: taken by another printer, any case");
            Check(Sharing.NameProblem("QL-800", "Brother QL-800 (Unidrv)", taken) == null, "share name: the printer's own (unchanged or rename)");
            Check(Sharing.NameProblem("scans", "Brother QL-570 (Unidrv)", taken).EndsWith("(a shared folder)."), "share name: taken by a folder");
            foreach (string bad in new[] { "QL\\800", "QL/800", "QL,800", "QL:800", "QL*", "QL?", "a\"b", "x[1]", "a|b", "a<b>", "a+b", "a=b", "a;b" })
                Check(Sharing.NameProblem(bad, "p", taken).StartsWith("At most 80 characters"), "share name: invalid " + bad);
            Check(Sharing.NameProblem(new string('Q', 80), "p", taken) == null && Sharing.NameProblem(new string('Q', 81), "p", taken) != null, "share name: 80 characters at most");
            Check(Sharing.NameProblem(" QL-570 ", "p", taken) == null, "share name: spaces around don't count");
            Check(Sharing.Suggest("QL-570", "Brother QL-570 (Unidrv)", taken) == "QL-570", "suggest: the model");
            Check(Sharing.Suggest("QL-800", "Brother QL-800 (Unidrv) (2)", taken) == "QL-800 (2)", "suggest: a second QL-800");
            Check(Sharing.Suggest("QL-800", "Brother QL-800 (Unidrv)", taken) == "QL-800", "suggest: its own share name");
            Check(Sharing.IsUsbPort("USB001") && Sharing.IsUsbPort("usb002:") && !Sharing.IsUsbPort("IP_192.0.2.5") && !Sharing.IsUsbPort(@"\\PC\QL-800") &&
                  !Sharing.IsUsbPort(@"C:\ProgramData\QLUnidrv\capture\capture.prn") && !Sharing.IsUsbPort(null), "shareable: USB ports only");
            Lang.Force("de");
            Check(Sharing.NameProblem("QL-800", "x", taken) == "„QL-800“ ist auf diesem PC schon vergeben (Drucker „Brother QL-800 (Unidrv)“).", "share name: German");
            Lang.Force("en");
            Check(Sharing.Printers() != null && Sharing.TakenNames() != null, "sharing: reads this PC without rights");
            var reach = Sharing.Reach();
            Check(Enum.IsDefined(typeof(SharingReach), reach), "sharing: reach read (" + reach + ")");
        }

        static void UsbCandidates()
        {
            Check(UsbPrinters.ModelOf("BrotherQL-80053B7").Name == "QL-800" && UsbPrinters.ModelOf("BROTHERQL-570").Name == "QL-570", "model from the printer device's key");
            Check(UsbPrinters.ModelOf("BrotherQL-1110NWB1A2B").Name == "QL-1110NWB" && UsbPrinters.ModelOf("BrotherQL-580N").Name == "QL-580N", "longest model name, checksum after it");
            Check(UsbPrinters.ModelOf("BrotherQL-8001") == null && UsbPrinters.ModelOf("BrotherPT-P7001234") == null && UsbPrinters.ModelOf("HPLaserJet") == null, "no QL model: none");
            Check(UsbPrinters.PortOf("6&1DFFA9C2&1&USB002") == "USB002" && UsbPrinters.PortOf("7&abc&0&usb001") == "USB001" && UsbPrinters.PortOf("6&1&0") == null, "port from the instance");
            var drivers = new[] { "Brother QL-570 (Unidrv)", "Brother QL-800 (Unidrv)" };
            var dev800 = new KeyValuePair<string, string>("BrotherQL-80053B7", "7&2b3c&0&USB001");
            var dev570 = new KeyValuePair<string, string>("BROTHERQL-570", "6&1DFFA9C2&1&USB002");
            // ARM after the spooler restart: the device on Windows' generic driver (prnms013), no printer on its port
            var found = UsbPrinters.Find(new[] { dev800 }, new[] { new PrinterQueue { Name = "QL-800 Capture", Port = @"C:\ProgramData\QLUnidrv\capture\capture.prn", Driver = "Brother QL-800 (Unidrv)" } }, drivers);
            Check(found.Count == 1 && found[0].Model.Name == "QL-800" && found[0].Port == "USB001" && found[0].BrotherQueue == null, "generic driver, no printer on the port: offered");
            // x64: the device on Brother's driver, Brother's own printer on the port
            found = UsbPrinters.Find(new[] { dev570 }, new[] { new PrinterQueue { Name = "Brother QL-570", Port = "USB002", Driver = "Brother QL-570" } }, drivers);
            Check(found.Count == 1 && found[0].BrotherQueue == "Brother QL-570", "Brother's printer on the port: offered, Brother's printer named");
            // our package on the device, but no printer of ours (one pending deletion is not listed by Windows)
            found = UsbPrinters.Find(new[] { dev800, dev800 }, new PrinterQueue[0], drivers);
            Check(found.Count == 1 && found[0].BrotherQueue == null, "our package, no printer: offered once");
            // a printer of ours on the port: nothing to offer; a model whose driver isn't installed: nothing either
            found = UsbPrinters.Find(new[] { dev800, dev570 }, new[] { new PrinterQueue { Name = "Brother QL-800 (Unidrv)", Port = "USB001", Driver = "Brother QL-800 (Unidrv)" },
                                                                   new PrinterQueue { Name = "Brother QL-570", Port = "USB002", Driver = "Brother QL-570" } }, new[] { "Brother QL-800 (Unidrv)" });
            Check(found.Count == 0, "our printer on the port, or the model's driver missing: not offered");
            found = UsbPrinters.Find(new[] { dev570, dev800 }, new[] { new PrinterQueue { Name = "Generic", Port = "USB001", Driver = "Generic / Text Only" } }, drivers);
            Check(found.Count == 2 && found[0].Port == "USB001" && found[0].BrotherQueue == null, "another vendor's printer is not called Brother's; sorted by port");
            Check(UsbPrinters.Unset() != null, "reading this PC works");
        }

        static void Message()
        {
            var p = QlMessage.Split("The printer has 62mm loaded, but 'A' needs 29mm.\n\nThe printer would stop. Load the matching roll first.\n\nPrint anyway?", false);
            Check(p.Headline == "Print anyway?" && p.Text == "The printer has 62mm loaded, but 'A' needs 29mm.\n\nThe printer would stop. Load the matching roll first.", "message: the question is the headline");
            p = QlMessage.Split("QL-800 is set to 'Use Printer Offline': the label would wait.\n\nUse the printer online now? Windows may ask for administrator permission.", false);
            Check(p.Headline == "Use the printer online now?" && p.Text == "QL-800 is set to 'Use Printer Offline': the label would wait.\n\nWindows may ask for administrator permission.",
                  "message: the rest of the question's paragraph stays in place: " + p.Text);
            p = QlMessage.Split("The setup is still running. If you close the window now, the driver is not installed. Close anyway?", false);
            Check(p.Headline == "Close anyway?" && p.Text == "The setup is still running. If you close the window now, the driver is not installed.", "message: question at the end of one paragraph");
            p = QlMessage.Split("The help isn't installed. Setup.exe repairs the installation.", false);
            Check(p.Headline == "The help isn't installed." && p.Text == "Setup.exe repairs the installation." && p.Details == null, "message: first sentence as headline");
            p = QlMessage.Split("Cannot read the label formats:\nUnexpected character at line 14.", true);
            Check(p.Headline == "Cannot read the label formats." && p.Text == null && p.Details == "Unexpected character at line 14.", "message: an error's colon line, Windows' text as details");
            p = QlMessage.Split("Cannot read the label formats:\nUnexpected character.", false);
            Check(p.Text == "Unexpected character." && p.Details == null, "message: not an error: the rest stays text");
            p = QlMessage.Split("Load a roll first (e.g. DK-22205). Then try again.", false);
            Check(p.Headline == "Load a roll first (e.g. DK-22205).", "message: e.g. doesn't end a sentence: " + p.Headline);
            Check(QlMessage.Split("Access denied", true).Headline == "Access denied" && QlMessage.Split("", false).Headline == "", "message: one phrase, or nothing");

            using (var f = new QlMessage.MessageForm("QL Label Formats", "Print anyway?", "Wrong roll.", System.Windows.Forms.MessageBoxButtons.YesNo,
                                                     System.Windows.Forms.MessageBoxIcon.Warning, System.Windows.Forms.MessageBoxDefaultButton.Button2, null))
            {
                Check(f.EscResult == System.Windows.Forms.DialogResult.No, "message: Esc on Yes/No = No");
                Check(f.DefaultButton.DialogResult == System.Windows.Forms.DialogResult.No && f.AcceptButton == f.DefaultButton, "message: No preselected, Enter = No");
                Check(!f.DefaultButton.Font.Bold, "message: a preselected No isn't the filled main button");
                Check(f.CopyText() == "---------------------------\r\nQL Label Formats\r\n---------------------------\r\nPrint anyway?\r\n\r\nWrong roll.\r\n" +
                                      "---------------------------\r\nYes   No   \r\n---------------------------\r\n", "message: Ctrl+C text as MessageBox's: " + f.CopyText());
            }
            using (var f = new QlMessage.MessageForm("T", "Save the changes?", null, System.Windows.Forms.MessageBoxButtons.YesNoCancel,
                                                     System.Windows.Forms.MessageBoxIcon.Question, System.Windows.Forms.MessageBoxDefaultButton.Button1, null))
                Check(f.EscResult == System.Windows.Forms.DialogResult.Cancel && f.DefaultButton.DialogResult == System.Windows.Forms.DialogResult.Yes && f.DefaultButton.Font.Bold,
                      "message: Yes/No/Cancel: Esc = Cancel, Yes the main button");
            using (var f = new QlMessage.MessageForm("T", "Done.", null, System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information,
                                                     System.Windows.Forms.MessageBoxDefaultButton.Button1, "detail"))
                Check(f.EscResult == System.Windows.Forms.DialogResult.OK && f.CopyText().Contains("Done.\r\n\r\ndetail\r\n"), "message: OK: Esc = OK, details copied too");
            Lang.Force("de");
            using (var f = new QlMessage.MessageForm("T", "?", null, System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Question,
                                                     System.Windows.Forms.MessageBoxDefaultButton.Button1, null))
                Check(f.CopyText().Contains("Ja   Nein   "), "message: German buttons");
            Lang.Force("en");
        }

        // Runs a short script unelevated (its arguments: args) and returns its output, trimmed
        static string RunPowerShell(string dir, string script, params string[] args)
        {
            string file = Path.Combine(dir, "t-" + Guid.NewGuid().ToString("N") + ".ps1");
            File.WriteAllText(file, script);
            var psi = new System.Diagnostics.ProcessStartInfo(NativeTools.Path("powershell.exe"),
                "-NoProfile -ExecutionPolicy Bypass -File \"" + file + "\"" + string.Concat(args.Select(a => " \"" + a + "\"")))
            { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
            using (var p = System.Diagnostics.Process.Start(psi)) { string output = p.StandardOutput.ReadToEnd().Trim(); p.WaitForExit(); return output; }
        }

        // The folder with driver\install.ps1 above this test exe (out\test in the repository or a release stage)
        static string FindRepo()
        {
            for (var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
                if (File.Exists(Path.Combine(d.FullName, @"driver\install.ps1"))) return d.FullName;
            return null;
        }
    }
}
