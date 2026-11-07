// Printer setup opened as the status window's "Remove printer here" opens it (--setup --select "<printer>"), on a list
// of 18 printers with fake ports: the printer is chosen (buttons on) and its line marked when its share is gone, also
// when the check answers at once (net 0 but the host's queue no longer SHARED, as on the real two-PC run 2026-11-06)
// or late. Nothing on the PC changes (printer list, ports, share check and queue read are replaced). Run through deskrun
// (setup_open.ps1). setup_open <out folder> <lang>; exit 1 when a check fails. A PNG per case.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class SetupOpen
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static int failures;

        static void Check(bool ok, string what)
        {
            Console.WriteLine((ok ? "PASS " : "FAIL ") + what);
            if (!ok) failures++;
        }

        static T Field<T>(Form f, string name) { return (T)typeof(SetupForm).GetField(name, All).GetValue(f); }

        static void Pump(Func<bool> until, int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end && !until()) { Application.DoEvents(); Thread.Sleep(20); }
            Application.DoEvents();
        }

        const string QL800 = "Brother QL-800 (Unidrv)", Port800 = @"\\192.0.2.65\QL-800";

        [STAThread]
        static int Main(string[] args)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            var name = new StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            string outDir = args[0];
            Lang.Force(args[1]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SetupForm.NoSaveTab = true;
            // 18 printers as on the x64 PC: the QL-570 shared from here (row 1), the QL-800 on the ARM PC's withdrawn
            // share (row 2), capture printers on file ports
            var printers = new List<string> { "Brother QL-570 (Unidrv)", QL800 };
            for (int i = 1; printers.Count < 18; i++) printers.Add("QL-800 Capture " + i);
            var ports = printers.ToDictionary(p => p, p => p == QL800 ? Port800 : p.StartsWith("Brother QL-570") ? "USB002" : @"C:\ProgramData\QLUnidrv\capture\capture" + p.Length + ".prn");
            SetupForm.ListPrinters = () => new List<string>(printers);
            SetupForm.PortOfPrinter = p => { string port; return ports.TryGetValue(p, out port) ? port : null; };
            string gone = SetupForm.ShareMark(PublishedStatus.ShareState.NotShared);

            // net 0 type 1 (the SMB share still answers), the queue opens but isn't SHARED: no longer shared, at once
            Case(outDir, args[1], "instant", 0, gone, "brother ql-800 (unidrv)");
            // the same answer after 1.5 s (the window is up long before)
            Case(outDir, args[1], "late", 1500, gone, QL800);
            Console.WriteLine(failures == 0 ? "RESULT: all PASS" : "RESULT: " + failures + " FAILED");
            return failures == 0 ? 0 : 1;
        }

        static void Case(string outDir, string lang, string label, int delay, string gone, string select)
        {
            PublishedStatus.ForgetShareChecks();
            PublishedStatus.HostAnswers = h => true;   // the host answers on TCP 445 (192.0.2.65 doesn't exist)
            PublishedStatus.NetShare = (h, s) => { if (delay > 0) Thread.Sleep(delay); return new[] { 0, 1 }; };
            PublishedStatus.Queue = q => new PublishedStatus.QueueFacts { Attributes = 0x10, ShareName = "QL-800" };
            var f = new SetupForm { SelectOnOpen = select, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            f.Show();
            var list = Field<ListBox>(f, "printerList");
            Func<string> row = () => list.Items.Count > 1 ? list.Items[1].ToString() : "";
            Pump(() => row().Contains(gone), delay + 3000);
            Pump(() => false, 300);
            Check(list.Items.Count == 18, label + ": 18 printers listed (" + list.Items.Count + ")");
            Check(list.SelectedIndex == 1, label + ": --select chose row 2 (" + list.SelectedIndex + ")");
            Check(Field<Button>(f, "removeButton").Enabled && Field<Button>(f, "propertiesButton").Enabled, label + ": Remove printer and Printer properties on");
            Check(row() == QL800 + "   (" + Port800 + " \u2013 " + gone + ")", label + ": row 2 marked: " + row());
            Check(list.Items[0].ToString().Contains("USB002") && !list.Items[0].ToString().Contains("–"), label + ": row 1 unmarked: " + list.Items[0]);
            Check(list.Items.Cast<object>().Skip(2).All(o => !o.ToString().Contains("\u2013")), label + ": capture printers unmarked");
            int height = f.Height * 96 / f.DeviceDpi;
            Check(height <= 690, label + ": window " + height + " px");
            using (var b = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(b)) { IntPtr dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(Path.Combine(outDir, "setup-open-" + label + "-" + lang + ".png"), ImageFormat.Png);
            }
            f.Close();
            f.Dispose();
        }
    }
}
