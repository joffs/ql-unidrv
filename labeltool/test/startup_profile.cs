// Where a window's start goes: opens QL Label Formats', QL Label Maker's or QL Printer Status' window as their Main
// does and samples the UI thread's managed stack every few ms until the window has shown; then prints how often each of
// our methods was on the stack (inclusive), the deepest frames (exclusive, the framework call doing the work) and the
// time to Shown. Sampling slows the start a little; compare runs with each other. deskrun only (opens a window).
//   startup_profile formats|maker|status [<runs, default 1>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class StartupProfile
    {
        static readonly Dictionary<string, int> inclusive = new Dictionary<string, int>();
        static readonly Dictionary<string, int> leaf = new Dictionary<string, int>();
        static readonly Dictionary<string, string> leafStack = new Dictionary<string, string>();
        static int samples;
        static volatile bool stop;

        [STAThread]
        static int Main(string[] args)
        {
            var start = Process.GetCurrentProcess().StartTime;
            var ui = Thread.CurrentThread;
            var sampler = new Thread(() => Sample(ui)) { IsBackground = true, Priority = ThreadPriority.Highest };
            sampler.Start();
            double main = (DateTime.Now - start).TotalMilliseconds;
            UiFont.UseForWindows();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Form form = args[0] == "maker" ? (Form)new EditorForm(null, null, null) : args[0] == "status" ? (Form)new StatusForm() : new MainForm();
            double built = (DateTime.Now - start).TotalMilliseconds;
            form.Shown += delegate
            {
                stop = true;
                sampler.Join();
                double shown = (DateTime.Now - start).TotalMilliseconds;
                Console.WriteLine("main {0:0} ms, window built {1:0} ms, shown {2:0} ms, {3} samples", main, built, shown, samples);
                Console.WriteLine("--- ours, inclusive (samples)");
                foreach (var kv in inclusive.OrderByDescending(k => k.Value).Take(30)) Console.WriteLine("{0,5}  {1}", kv.Value, kv.Key);
                Console.WriteLine("--- deepest frame (samples)");
                foreach (var kv in leaf.OrderByDescending(k => k.Value).Take(25)) Console.WriteLine("{0,5}  {1}   [{2}]", kv.Value, kv.Key, leafStack[kv.Key]);
                form.BeginInvoke((Action)form.Close);
            };
            Application.Run(form);
            return 0;
        }

#pragma warning disable 618   // Thread.Suspend/Resume and StackTrace(Thread): deprecated, fine for a test
        static void Sample(Thread ui)
        {
            while (!stop)
            {
                Thread.Sleep(1);
                StackTrace st;
                try { ui.Suspend(); try { st = new StackTrace(ui, false); } finally { ui.Resume(); } }
                catch (Exception) { continue; }
                samples++;
                var frames = st.GetFrames();
                if (frames == null || frames.Length == 0) continue;
                var m0 = frames[0].GetMethod();
                string top = m0 == null ? "?" : m0.DeclaringType + "." + m0.Name;
                Add(leaf, top);
                if (!leafStack.ContainsKey(top))
                    leafStack[top] = string.Join(" < ", frames.Select(f => f.GetMethod()).Where(m => m != null && m.DeclaringType != null
                        && (m.DeclaringType.FullName ?? "").StartsWith("QlLabels.")).Take(4).Select(m => m.DeclaringType.Name + "." + m.Name));
                var seen = new HashSet<string>();
                foreach (var f in frames)
                {
                    var m = f.GetMethod();
                    if (m == null || m.DeclaringType == null) continue;
                    string type = m.DeclaringType.FullName ?? "";
                    if (!type.StartsWith("QlLabels.") || type.StartsWith("QlLabels.Tests")) continue;
                    string name = type.Substring(9) + "." + m.Name;
                    if (seen.Add(name)) Add(inclusive, name);
                }
            }
        }
#pragma warning restore 618

        static void Add(Dictionary<string, int> d, string key) { int n; d.TryGetValue(key, out n); d[key] = n + 1; }
    }
}
