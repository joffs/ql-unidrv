// Copy out of the INSTALLED QL Label Maker into Word: loads C:\Program Files\QLUnidrv\QlLabelMaker.exe (or the exe given),
// opens a label (text, box, QR code), selects all and runs the Maker's own Copy; then Word via COM, invisible: new
// document, Paste, reports what arrived (picture type, image part content type, size in mm, reported only), exports
// the page as PDF, closes the document without saving and quits Word. Stops when Word is already running (never
// attaches). Writes the user's clipboard: run only through deskrun with --lock clipboard (word-paste.ps1 does).
//   word_paste <label.qllabel> <out.pdf> [<QlLabelMaker.exe>]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class WordPaste
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        [DllImport("user32.dll")] static extern bool OpenClipboard(IntPtr owner);
        [DllImport("user32.dll")] static extern bool CloseClipboard();
        [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
        [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClipboardFormatName(uint format, System.Text.StringBuilder name, int size);
        [DllImport("gdi32.dll")] static extern uint GetEnhMetaFileHeader(IntPtr h, uint size, byte[] header);

        static int failures;
        static void Check(bool ok, string what) { Console.WriteLine((ok ? "OK   " : "FAIL ") + what); if (!ok) failures++; }

        [STAThread]
        static int Main(string[] args)
        {
            string label = Path.GetFullPath(args[0]), pdf = Path.GetFullPath(args[1]);
            string exe = args.Length > 2 ? args[2] : @"C:\Program Files\QLUnidrv\QlLabelMaker.exe";
            Console.WriteLine("machine " + GetMachine());
            Console.WriteLine("maker " + FileVersionInfo.GetVersionInfo(exe).FileVersion + " (" + exe + ")");
            if (Process.GetProcessesByName("WINWORD").Length > 0) { Console.WriteLine("STOP Word is already running: not attaching"); return 2; }

            var asm = Assembly.LoadFrom(exe);
            var editorType = asm.GetType("QlLabels.EditorForm", true);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var form = (Form)Activator.CreateInstance(editorType, All, null, new object[] { label, null, null }, null);
            form.Shown += delegate
            {
                try { Run(form, editorType, pdf); }
                catch (Exception ex) { Console.WriteLine("FAIL " + ex); failures++; }
                form.Close();
            };
            Application.Run(form);
            Console.WriteLine(failures == 0 ? "PASS" : failures + " FAIL");
            return failures;
        }

        // The machine this process runs as (ProcessMachineTypeInfo; ARM64 or x64 on an ARM64 PC)
        [DllImport("kernel32.dll")] static extern bool GetProcessInformation(IntPtr process, int cls, out PROCESS_MACHINE_INFORMATION info, int size);
        [StructLayout(LayoutKind.Sequential)] struct PROCESS_MACHINE_INFORMATION { public ushort ProcessMachine; public ushort Res0; public uint Attributes; }
        static string GetMachine()
        {
            PROCESS_MACHINE_INFORMATION info;
            if (!GetProcessInformation(Process.GetCurrentProcess().Handle, 9, out info, Marshal.SizeOf(typeof(PROCESS_MACHINE_INFORMATION)))) return "?";
            return info.ProcessMachine == 0xAA64 ? "ARM64" : info.ProcessMachine == 0x8664 ? "x64" : info.ProcessMachine.ToString("X");
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }

        static void Run(Form form, Type t, string pdf)
        {
            Pump(1500);
            t.GetMethod("SelectAll", All, null, Type.EmptyTypes, null).Invoke(form, null);
            Pump(200);
            var selected = (System.Collections.IList)t.GetProperty("SelectedObjects", All).GetValue(form, null);
            Console.WriteLine("selected " + selected.Count + " objects");
            RectangleF box = RectangleF.Empty;
            foreach (var o in selected)
            {
                var ot = o.GetType();
                Func<string, float> f = n => Convert.ToSingle((ot.GetField(n, All) != null ? ot.GetField(n, All).GetValue(o) : ot.GetProperty(n, All).GetValue(o, null)));
                var r = new RectangleF(f("X"), f("Y"), f("W"), f("H"));
                box = box.IsEmpty ? r : RectangleF.Union(box, r);
            }
            Console.WriteLine(string.Format("objects' box {0:0.0} x {1:0.0} mm", box.Width, box.Height));
            t.GetMethod("Copy", All, null, Type.EmptyTypes, null).Invoke(form, null);
            Pump(300);

            // What the clipboard holds now
            var formats = new List<string>();
            float emfW = 0, emfH = 0;
            if (OpenClipboard(form.Handle))
            {
                try
                {
                    for (uint f = EnumClipboardFormats(0); f != 0; f = EnumClipboardFormats(f))
                    {
                        var sb = new System.Text.StringBuilder(128);
                        formats.Add(GetClipboardFormatName(f, sb, 128) > 0 ? sb.ToString() : "#" + f);
                    }
                    IntPtr h = GetClipboardData(14);
                    if (h != IntPtr.Zero)
                    {
                        var head = new byte[100];
                        GetEnhMetaFileHeader(h, 100, head);
                        // rclFrame (0.01 mm) at offset 24
                        int l = BitConverter.ToInt32(head, 24), tp = BitConverter.ToInt32(head, 28), r = BitConverter.ToInt32(head, 32), b = BitConverter.ToInt32(head, 36);
                        emfW = (r - l) / 100f; emfH = (b - tp) / 100f;
                    }
                }
                finally { CloseClipboard(); }
            }
            Console.WriteLine("clipboard: " + string.Join(", ", formats));
            Console.WriteLine(string.Format("emf frame {0:0.0} x {1:0.0} mm", emfW, emfH));
            Check(formats.Contains("#14"), "clipboard has an EMF (CF_ENHMETAFILE)");

            // Word, invisible, its own instance
            if (Process.GetProcessesByName("WINWORD").Length > 0) { Console.WriteLine("STOP Word started meanwhile: not attaching"); failures++; return; }
            dynamic word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application", true));
            try
            {
                word.Visible = false;
                word.DisplayAlerts = 0;
                dynamic doc = word.Documents.Add();
                try
                {
                    word.Selection.Paste();
                    int inline = doc.InlineShapes.Count, floating = doc.Shapes.Count;
                    Console.WriteLine("word: " + inline + " inline shape(s), " + floating + " floating shape(s)");
                    Check(inline + floating == 1, "one pasted object");
                    if (inline >= 1)
                    {
                        dynamic s = doc.InlineShapes[1];
                        int type = s.Type;   // 3 = wdInlineShapePicture
                        float w = (float)s.Width * 25.4f / 72, h = (float)s.Height * 25.4f / 72;
                        Console.WriteLine(string.Format("pasted: inline shape type {0} ({1}), {2:0.0} x {3:0.0} mm", type, type == 3 ? "picture" : "other", w, h));
                        Check(type == 3, "pasted as a picture");
                        string xml = s.Range.WordOpenXML;
                        var parts = Regex.Matches(xml, "pkg:contentType=\"(image/[^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
                        Console.WriteLine("image part(s): " + string.Join(", ", parts));
                        Check(parts.Contains("image/x-emf"), "the picture is the EMF");
                        // The size is only reported (Word shows it larger than the EMF frame; checked by eye, it stays as is)
                    }
                    doc.ExportAsFixedFormat(pdf, 17);   // wdExportFormatPDF
                    Check(File.Exists(pdf), "pdf " + pdf);
                }
                // (no ReleaseComObject: the dynamic binder keeps a reference to the object and fails with 0x80010114 on the next call)
                finally { doc.Close(0); doc = null; }
            }
            finally { word.Quit(0); word = null; GC.Collect(); GC.WaitForPendingFinalizers(); }
            Pump(500);
        }
    }
}
