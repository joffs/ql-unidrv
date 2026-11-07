// Barcode print test: opens a .qllabel in the real QL Label Maker window and prints it with the window's own Print
// (the editor's whole print path) on a capture printer. Run by barcode_print.ps1 through deskrun.exe only (refuses
// the normal desktop: the window must never take the user's focus).
//   barcode_print <file.qllabel> <printer> <300|600>
// Exit 0 = Print finished; 2 = normal desktop; 3 = timeout (e.g. a message box the editor showed).
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class BarcodePrint
    {
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        [STAThread]
        static int Main(string[] args)
        {
            var name = new StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            Lang.Force("de");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var watchdog = new System.Threading.Timer(_ => { Console.WriteLine("timeout"); Environment.Exit(3); }, null, 60000, Timeout.Infinite);
            var form = new EditorForm(args[0], args[1], null);
            var t = typeof(EditorForm);
            bool done = false;
            form.Shown += delegate
            {
                Pump(2500);
                if (args[2] == "600") { ((CheckBox)t.GetField("highResCheck", All).GetValue(form)).Checked = true; Pump(1500); }
                t.GetMethod("Print", All, null, Type.EmptyTypes, null).Invoke(form, null);
                Pump(300);
                while ((bool)t.GetField("printing", All).GetValue(form)) Pump(100);
                Pump(500);
                t.GetField("dirty", All).SetValue(form, false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            watchdog.Dispose();
            Console.WriteLine("printed " + args[0]);
            return 0;
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
