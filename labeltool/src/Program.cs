// QL Label Formats (labels v2) - replacement for Brother's label format tool.
//
//   QlLabelFormats.exe                                   the editor (runs unelevated)
//   QlLabelFormats.exe --apply <list.json> [--result <file>]   elevated helper of "Save & apply"
//   QlLabelFormats.exe --sync [--result <file>]          re-apply C:\ProgramData\QLUnidrv\labels.json (install.ps1)
//   QlLabelFormats.exe --remove-forms [--result <file>]  delete the forms this tool created (uninstall)
//   QlLabelFormats.exe --plan [list.json] [--result <file>]  dry run: what --apply/--sync would do
//   QlLabelFormats.exe --install [<release folder>]      the setup window (release zip: Setup.exe = this exe, no arguments)
//   QlLabelFormats.exe --identify <address> [--result <file>]  asks a network QL printer for its model (SNMP; install.ps1
//                                                  -NetworkPrinter): result lines OK, driver name, model, loaded roll
//   QlLabelFormats.exe --online <printer>... [--result <file>]  clears Windows' "Use Printer Offline" (elevated helper of
//                                                  "Use printer online" in the status window and the tip bar)
// --apply, --sync, --remove-forms and --online need administrator rights. They write "OK"/"ERROR" and the log lines to
// the result file (the exe has no console) and exit with 0 or 1.
//
// Build: labeltool\build.cmd (C# 5, .NET Framework 4.8 inbox csc, AnyCPU -> native on ARM64).
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;

// All exes of this folder (QlLabelFormats, QlLabelMaker, QlPrinterStatus, Setup) target .NET Framework 4.8. The inbox csc doesn't
// write this mark itself; without it the framework keeps its pre-4.7 behaviour, and WinForms ignores a DPI change (window
// moved to a screen with other scaling): the window grows, its fonts and controls don't.
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace QlLabels
{
    static class Program
    {
        public const string AppId = "LabelFormats";   // SingleInstance: one window per user session

        // The starts that are the QL Label Formats window (and so hand over to a running one): none, or --printer (Printer
        // setup's test print, QL Label Maker). Never Setup.exe, --install, --setup or a command mode (--apply, --sync, ...).
        internal static bool IsWindowStart(string[] args, bool setupExe)
        {
            return !setupExe && (args.Length == 0 || (args[0] == "--printer" && args.Length > 1));
        }

        [STAThread]
        static int Main(string[] args)
        {
            StartupTrace.Mark("main");
            UiFont.UseForWindows();   // window fonts without GDI+'s table of every installed font (seconds after a restart)
            // The release zip's Setup.exe is this exe: started without arguments it is the setup window.
            bool setupExe = string.Equals(Path.GetFileNameWithoutExtension(Application.ExecutablePath), "Setup", StringComparison.OrdinalIgnoreCase);
            if ((args.Length == 0 && setupExe) || (args.Length > 0 && args[0] == "--install"))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // --install [<release folder>] [--after-restart] (the ARM64 special start reopens it so; InstallForm)
                Application.Run(new InstallForm(args.Length > 1 && !args[1].StartsWith("--") ? args[1] : null, Array.IndexOf(args, "--after-restart") >= 0));
                return 0;
            }
            if (args.Length > 0 && args[0] == "--uninstall")   // Installed apps > Uninstall, Printer setup > Uninstall... (InstallForm)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                InstallForm.Uninstall(args);
                return 0;
            }
            if (IsWindowStart(args, setupExe) && SingleInstance.HandOff(AppId, args)) return 0;   // a QL Label Formats window is open
            if (args.Length == 0)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                StartupTrace.Mark("visual styles");
                StartupTrace.MarkFonts();
                var form = new MainForm();
                SingleInstance.Serve(form, form.StartedAgain);
                StartupTrace.Mark("window built");
                Application.Run(form);
                return 0;
            }

            if (args[0] == "--printer" && args.Length > 1)   // QL Label Formats with this printer chosen (Printer setup: Test print)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                var main = new MainForm();
                main.UseTestPrinter(args[1]);
                // QL Label Maker's "Save as label format ...": a new format of that paper and length, ready to name and apply
                int paperId, length01;
                if (args.Length > 4 && args[2] == "--new-format" && int.TryParse(args[3], out paperId) && int.TryParse(args[4], out length01))
                    main.Shown += delegate { main.NewFormat(paperId, length01 / 10.0); };
                SingleInstance.Serve(main, main.StartedAgain);
                Application.Run(main);
                return 0;
            }

            if (args[0] == "--setup")
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                // --setup --select "<printer>": that printer chosen in the list (the status window's "Remove printer here")
                int s = Array.IndexOf(args, "--select");
                Application.Run(new SetupForm { SelectOnOpen = s > 0 && s + 1 < args.Length ? args[s + 1] : null });
                return 0;
            }
            return Command(args);
        }

        // The command-line modes (no window): --apply, --sync, --plan, --remove-forms, --identify, --online.
        // QlPrinterStatus.exe runs its --online here too.
        internal static int Command(string[] args)
        {
            string mode = args[0], input = null, result = null;
            var inputs = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--result" && i + 1 < args.Length) result = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) i++;  // Lang.cs reads it
                else inputs.Add(args[i]);
            }
            if (inputs.Count > 0) input = inputs[0];
            var log = new List<string>();
            bool ok = false;
            try
            {
                if (mode != "--plan" && mode != "--identify" && !new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
                    throw new UnauthorizedAccessException("Administrator rights are needed.");
                switch (mode)
                {
                    case "--apply":
                        if (input == null) throw new ArgumentException("--apply needs the label list (JSON).");
                        log.AddRange(Sync.Apply(LabelStore.Load(input).Formats));
                        break;
                    case "--sync":
                        string seeded = Sync.SeedIfMissing();
                        if (seeded != null) log.Add(seeded);
                        log.AddRange(Sync.Apply(null));
                        break;
                    case "--plan":
                        log.AddRange(Sync.Preview(input != null ? LabelStore.Load(input).Formats : null));
                        break;
                    case "--remove-forms":
                        log.AddRange(Sync.RemoveForms());
                        break;
                    case "--identify":
                        if (input == null) throw new ArgumentException("--identify needs the printer's address.");
                        log.AddRange(NetworkStatus.Identify(input));
                        break;
                    case "--online":
                        if (input == null) throw new ArgumentException("--online needs the printer's name.");
                        foreach (var printer in inputs)
                        {
                            if (!Spooler.UseOnline(printer)) throw new UnauthorizedAccessException(Lang.F("Windows didn't allow changing {0}.", printer));
                            log.Add(Lang.F("{0} is online.", printer));
                        }
                        break;
                    default:
                        throw new ArgumentException("Unknown option " + mode);
                }
                ok = true;
            }
            catch (Exception ex)
            {
                log.Add(ex.Message);
            }
            log.Insert(0, ok ? "OK" : "ERROR");
            if (result != null)
            {
                try { File.WriteAllText(result, string.Join(Environment.NewLine, log) + Environment.NewLine, new UTF8Encoding(false)); }
                catch (Exception) { }
            }
            return ok ? 0 : 1;
        }
    }
}
