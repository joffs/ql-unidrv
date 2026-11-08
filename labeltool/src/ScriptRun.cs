// Runs driver\install.ps1 for the setup window (install, update, uninstall) and the Printer setup window (-Settings):
// elevated through Elevated.Run with -Log and -ResultFile in %TEMP%, and reads what it wrote: the key=value result
// lines (also while it runs: step=), the transcript's own lines and a one-line error. Also the unelevated runs of
// package.ps1 (signing). The callers keep their own UI, progress and texts (refactor I1, 2026-11-08).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QlLabels
{
    static class ScriptRun
    {
        // The run files in %TEMP%: the setup window's (install, uninstall) and Printer setup's (settings). Kept after a run
        // that failed (Show details names the log), deleted after one that finished. tools\release\setup.ps1 (Remove-Log)
        // deletes the same names (setup_test checks it).
        internal const string SetupLog = "ql-unidrv-install.log", SetupResult = "ql-unidrv-setup.txt";
        internal const string SettingsLog = "ql-unidrv-settings.log", SettingsResult = "ql-unidrv-settings.txt";
        internal static readonly string[] RunFileNames = { SetupLog, SetupResult, SettingsLog, SettingsResult };

        internal static string TempFile(string name) { return Path.Combine(Path.GetTempPath(), name); }

        // Before a run: the files of an earlier one go, so the progress timer never reads an old result.
        internal static void ClearFiles(params string[] files)
        {
            foreach (var f in files) try { if (f != null) File.Delete(f); } catch (Exception) { }
        }

        // The run files of every earlier run (after one that finished; debug pass 2026-10-31 X7).
        internal static void DeleteRunFiles()
        {
            if (InstallForm.Preview) return;
            foreach (string name in RunFileNames) try { File.Delete(TempFile(name)); } catch (Exception) { }
        }

        // UI tests (deskrun, QL_TEST_SILENT=1) never start the elevated script: what asked for it goes to
        // %TEMP%\ql-unidrv-test-run.txt instead (2026-11-06). True = skipped.
        internal static bool SkippedForTest(string arguments)
        {
            if (Environment.GetEnvironmentVariable("QL_TEST_SILENT") != "1") return false;
            try { File.AppendAllText(TempFile("ql-unidrv-test-run.txt"), DateTime.Now + " " + arguments + "\r\n" + Environment.StackTrace + "\r\n\r\n"); } catch (Exception) { }
            return true;
        }

        // powershell -File <script><arguments> -Log <logFile> -ResultFile <resultFile>, elevated, hidden; waits and returns
        // the exit code (callers wrap it in Task.Run or SetupProgress.Run). arguments start with a space. started: called
        // once the script runs (the prompt was accepted). Throws Win32Exception (Elevated.Declined: the user said No).
        internal static int RunElevated(string script, string arguments, string logFile, string resultFile, string workingDirectory = null, Action started = null)
        {
            return Elevated.Run(NativeTools.Path("powershell.exe", elevated: true),
                                "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"" + arguments + " -Log \"" + logFile + "\" -ResultFile \"" + resultFile + "\"",
                                workingDirectory, started);
        }

        // install.ps1's -ResultFile while it is still being written (key=value lines, in order).
        internal static List<KeyValuePair<string, string>> ReadResult(string file)
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = r.ReadLine()) != null)
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) list.Add(new KeyValuePair<string, string>(line.Substring(0, eq).Trim(), line.Substring(eq + 1).Trim()));
                    }
                }
            }
            catch (Exception) { }
            return list;
        }

        // The finished result: the last value of each key.
        internal static Dictionary<string, string> ReadResultMap(string file, IEqualityComparer<string> comparer = null)
        {
            var map = new Dictionary<string, string>(comparer ?? EqualityComparer<string>.Default);
            foreach (var kv in ReadResult(file)) map[kv.Key] = kv.Value;
            return map;
        }

        // The script's own output from a transcript: without PowerShell's header and footer blocks (lines between
        // "****" rulers, in the system's language) and empty lines.
        internal static List<string> TranscriptBody(string file)
        {
            var all = new List<string>();
            try { all = File.ReadAllLines(file).ToList(); } catch (Exception) { }
            var rulers = all.Select((l, i) => new { l, i }).Where(x => x.l.StartsWith("*****")).Select(x => x.i).ToList();
            int from = rulers.Count >= 2 ? rulers[1] + 1 : 0;
            int to = rulers.Count >= 4 ? rulers[rulers.Count - 2] : all.Count;
            return all.Skip(from).Take(Math.Max(0, to - from)).Where(l => l.Trim().Length > 1 && !l.StartsWith("*****")).ToList();
        }

        // TranscriptBody for showing a failure: PowerShell's transcript writes a script's throw as
        // PS>TerminatingError(): "message"; here only the message.
        internal static List<string> FailureLines(string file)
        {
            return TranscriptBody(file).Select(l => Regex.Replace(l, "^PS>TerminatingError\\(\\w*\\): \"(.*)\"$", "$1")).ToList();
        }

        // Exit code, output and error output of a hidden, unelevated process.
        internal static Tuple<int, string, string> RunHidden(string exe, string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Default, StandardErrorEncoding = Encoding.Default,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return Tuple.Create(p.ExitCode, output, err.Result);
            }
        }

        // A script's failure in one line: PowerShell writes the error's message first, then where it happened (in
        // Windows' language); without error output the script's last line.
        internal static string ErrorMessage(string output, string error)
        {
            var first = error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.Trim().Length > 0);
            return first != null ? first.Trim()
                 : output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? Lang.T("no message");
        }
    }
}
