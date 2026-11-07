// A watch on the UI thread of QL Printer Status: when it runs no posted work for 15 s, the UI thread's call stack and
// the subscribers of Windows' setting broadcasts (SystemEvents, with the thread each one is called on) go to
// C:\ProgramData\QLUnidrv\status\hang.log, once per stall. For a hang seen once (0.18.18, after High Contrast on and
// off) that no test reproduced. Writes nothing while the window works; the file stays below 256 KB.
#pragma warning disable 618   // Thread.Suspend / StackTrace(Thread): the only way to read another thread's stack here
using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    static class HangLog
    {
        public static string Path { get { return path ?? System.IO.Path.Combine(LabelStore.DataDir, @"status\hang.log"); } }
        internal static string path = null;   // tests: a file of their own
        internal static int StallMs = 15000;   // tests: shorter
        const long MaxSize = 256 * 1024;
        static bool started;

        // On the UI thread; marshal: a control of that thread whose handle exists already (a form after Shown, a hidden
        // control with CreateControl; a menu has none until it opens): without one the first post fails and the watch
        // ends at once. Disposing it ends the watch.
        public static void Start(Control marshal)
        {
            if (started) return;
            started = true;
            var ui = Thread.CurrentThread;
            new Thread(() => Watch(marshal, ui)) { IsBackground = true, Name = "QL hang log" }.Start();
        }

        static void Watch(Control marshal, Thread ui)
        {
            bool logged = false;
            while (true)
            {
                var ran = new ManualResetEvent(false);
                try { marshal.BeginInvoke((Action)delegate { ran.Set(); }); }
                catch (Exception) { return; }   // the window is gone: the program ends
                if (ran.WaitOne(StallMs)) { logged = false; Thread.Sleep(2000); continue; }
                if (!logged) { Write(ui); logged = true; }
                ran.WaitOne();   // the next round once the thread runs again
            }
        }

        static void Write(Thread ui)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append(" ").Append(Application.ProductName).Append(" ")
              .Append(Application.ProductVersion).Append(": the UI thread has run no posted work for ").Append(StallMs / 1000).Append(" s\r\n");
            try
            {
                ui.Suspend();
                try { sb.Append(new StackTrace(ui, false)).Append("\r\n"); }
                finally { ui.Resume(); }
            }
            catch (Exception ex) { sb.Append("(no stack: ").Append(ex.Message).Append(")\r\n"); }
            sb.Append("SystemEvents subscribers:\r\n");
            try
            {
                var handlers = (IDictionary)typeof(SystemEvents).GetField("_handlers", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
                if (handlers != null)
                    foreach (DictionaryEntry e in handlers)
                        foreach (var info in (IEnumerable)e.Value)
                        {
                            var ctx = info.GetType().GetField("_syncContext", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(info);
                            var d = (Delegate)info.GetType().GetField("_delegate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(info);
                            string thread = "";
                            if (ctx is WindowsFormsSynchronizationContext)
                            {
                                var r = (WeakReference)typeof(WindowsFormsSynchronizationContext).GetField("destinationThreadRef",
                                    BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctx);
                                var t = r != null ? r.Target as Thread : null;
                                thread = t == null ? " thread gone" : t == ui ? " UI thread" : " thread " + t.ManagedThreadId + " (not the UI thread)";
                            }
                            sb.Append("  ").Append(d.Method.DeclaringType).Append(".").Append(d.Method.Name)
                              .Append(ctx == null ? " (direct)" : " -> " + ctx.GetType().Name + thread).Append("\r\n");
                        }
            }
            catch (Exception ex) { sb.Append("  (can't read: ").Append(ex.Message).Append(")\r\n"); }
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                var fi = new FileInfo(Path);
                if (fi.Exists && fi.Length > MaxSize) File.Delete(Path);
                File.AppendAllText(Path, sb.Append("\r\n").ToString(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
