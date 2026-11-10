// Startup timing for the programs' first start (why a start is slow): off unless the environment variable
// QL_STARTUP_TRACE=1 is set; then each Mark("phase") is kept with the milliseconds since the process started, and the
// list goes to %TEMP%\ql-startup-<exe name>.txt (overwritten per start) when the first window is idle and at exit.
// Off it costs one bool check per Mark. Changes nothing else; no file is written when it is off.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QlLabels
{
    static class StartupTrace
    {
        public static readonly bool On = Environment.GetEnvironmentVariable("QL_STARTUP_TRACE") == "1";
        static readonly object gate = new object();
        static List<string> lines;
        static DateTime start;
        static bool idleHooked;

        static StartupTrace()
        {
            if (!On) return;
            lines = new List<string>();
            try { start = Process.GetCurrentProcess().StartTime; } catch (Exception) { start = DateTime.Now; }
            AppDomain.CurrentDomain.ProcessExit += delegate { Mark("exit"); Write(); };
        }

        // A phase reached (any thread); the thread is named when it isn't the first one.
        public static void Mark(string phase)
        {
            if (!On) return;
            double ms = (DateTime.Now - start).TotalMilliseconds;
            int thread = System.Threading.Thread.CurrentThread.ManagedThreadId;
            lock (gate) lines.Add(ms.ToString("0").PadLeft(7) + " ms  " + (thread != 1 ? "[thread " + thread + "] " : "") + phase);
        }

        // Traced only: the window font made here (UiFont: a private collection, milliseconds; with QL_UIFONT=off GDI+'s
        // table of every installed font, seconds with many fonts after a restart), so the trace shows that time on its
        // own instead of inside the window's constructor. Off it does nothing.
        public static void MarkFonts()
        {
            if (!On) return;
            using (UiFont.MessageBox) { }
            Mark("fonts: window font");
        }

        // Writes the list once the first window has shown and the message loop is idle (call from Shown).
        public static void WhenIdle()
        {
            if (!On || idleHooked) return;
            idleHooked = true;
            EventHandler idle = null;
            idle = delegate
            {
                Application.Idle -= idle;
                Mark("first idle");
                Write();
            };
            Application.Idle += idle;
        }

        static void Write()
        {
            try
            {
                string name = Path.GetFileNameWithoutExtension(Application.ExecutablePath);
                string text;
                lock (gate) text = string.Join(Environment.NewLine, lines) + Environment.NewLine;
                File.WriteAllText(Path.Combine(Path.GetTempPath(), "ql-startup-" + name + ".txt"), text, Encoding.UTF8);
            }
            catch (Exception) { }   // a trace never stops the program
        }
    }
}
