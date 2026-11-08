// Runs a program with administrator rights: Windows' UAC prompt, the program's window hidden, waiting for its exit code.
// The one copy of what the elevated runs did each on their own (Save & apply, Use online, Printer setup, the setup
// window's install, uninstall, key-7 restart and signing check; refactor S10, 2026-11-08). The callers keep their own UI
// and texts.
using System;
using System.ComponentModel;
using System.Diagnostics;

namespace QlLabels
{
    static class Elevated
    {
        // Starts file with Verb "runas" (UseShellExecute, hidden window) and waits on the calling thread: callers wrap it in
        // Task.Run or SetupProgress.Run. started: called once the program runs (after the prompt was accepted).
        // Throws Win32Exception like Process.Start; Declined(ex) tells the user's "No" in the prompt from other failures.
        // For a program in System32 pass NativeTools.Path(name, elevated: true).
        public static int Run(string file, string arguments, string workingDirectory = null, Action started = null)
        {
            var psi = new ProcessStartInfo(file, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
            if (workingDirectory != null) psi.WorkingDirectory = workingDirectory;
            using (var p = Process.Start(psi))
            {
                if (started != null) started();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ERROR_CANCELLED: the user answered No in Windows' prompt.
        public static bool Declined(Win32Exception ex) { return ex.NativeErrorCode == 1223; }
    }
}
