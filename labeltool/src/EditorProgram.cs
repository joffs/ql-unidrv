// QlLabelMaker.exe - the label editor "QL Label Maker" as its own program:
//   QlLabelMaker.exe [file.qllabel] [--printer name] [--format name] [--lang en|de]
//   QlLabelMaker.exe --print-data file.qllabel   (Explorer's "Print with data...": the label, then Print with data with
//                                                 its remembered data file)
// Built from the same sources as QlLabelFormats.exe (labeltool\build.cmd, /main:QlLabels.EditorProgram); .qllabel files open
// here (file association written by install.ps1).
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace QlLabels
{
    static class EditorProgram
    {
        public const string ExeName = "QlLabelMaker.exe";
        public const string AppId = "LabelMaker";   // SingleInstance: one window per user session

        [STAThread]
        static int Main(string[] args)
        {
            StartupTrace.Mark("Main");
            UiFont.UseForWindows();   // window fonts without GDI+'s table of every installed font (seconds after a restart)
            Lang.WindowApp();   // every start is a window, also with a file as the first argument
            // a QL Label Maker window is open: it gets this start's file (as a full path: its folder may be another)
            if (SingleInstance.HandOff(AppId, ForRunning(args))) return 0;
            Run(args, 0);
            return 0;
        }

        // The editor in this process; args from index start: [file] [--printer name] [--format name] [--lang x].
        public static void Run(string[] args, int start)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string file, printerName, formName;
            Parse(args, start, out file, out printerName, out formName);
            StartupTrace.Mark("arguments read");
            StartupTrace.MarkFonts();
            var files = FilePrinters(args, start);
            var form = new EditorForm(file, files.Count > 0 && files[0].Value != null ? files[0].Value : printerName, formName);
            foreach (var more in files.Skip(1)) form.OpenMore(more.Key, more.Value);   // a restart in another language: every tab
            if (PrintData(args, start) && file != null) form.PrintDataSoon(file);
            SingleInstance.Serve(form, form.StartedAgain);   // later starts: this window
            StartupTrace.Mark("window built");
            Application.Run(form);
        }

        public const string PrintDataFlag = "--print-data";

        // --print-data among the arguments (from index start).
        internal static bool PrintData(string[] args, int start) { return args.Skip(start).Contains(PrintDataFlag); }

        // [file] [--printer name] [--format name] [--lang x] from index start.
        internal static void Parse(string[] args, int start, out string file, out string printerName, out string formName)
        {
            file = printerName = formName = null;
            for (int i = start; i < args.Length; i++)
            {
                if (args[i] == "--printer" && i + 1 < args.Length) printerName = args[++i];
                else if (args[i] == "--format" && i + 1 < args.Length) formName = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) i++;   // Lang.cs reads it
                else if (args[i] == PrintDataFlag) { }
                else if (file == null) file = args[i];
            }
        }

        // Every file argument with its printer (the first is Parse's file): "--printer name" before a file is that file's
        // (a restart in another language: every tab on its own printer); one after the last file is the printer of the
        // files without one ("file --printer name", as before tabs).
        internal static List<KeyValuePair<string, string>> FilePrinters(string[] args, int start)
        {
            var list = new List<KeyValuePair<string, string>>();
            string pending = null;
            for (int i = start; i < args.Length; i++)
            {
                if (args[i] == "--printer" && i + 1 < args.Length) pending = args[++i];
                else if ((args[i] == "--format" || args[i] == "--lang") && i + 1 < args.Length) i++;
                else if (args[i] == PrintDataFlag) { }
                else { list.Add(new KeyValuePair<string, string>(args[i], pending)); pending = null; }
            }
            if (pending != null)
                for (int i = 0; i < list.Count; i++) if (list[i].Value == null) list[i] = new KeyValuePair<string, string>(list[i].Key, pending);
            return list;
        }

        // The command line for these files, each with its printer (FilePrinters reads it back).
        internal static string FileArguments(IEnumerable<KeyValuePair<string, string>> files)
        {
            return string.Join(" ", files.Select(f => (f.Value != null ? "--printer " + Quote(f.Value) + " " : "") + Quote(f.Key)));
        }

        // The arguments for the running window: the file as a full path, the printer and format; no --lang (it keeps its own).
        internal static string[] ForRunning(string[] args)
        {
            string file, printerName, formName;
            Parse(args, 0, out file, out printerName, out formName);
            var list = new List<string>();
            var files = FilePrinters(args, 0);
            foreach (var f in files)   // every file (several selected in Explorer), each with its printer
            {
                string full = f.Key;
                try { full = System.IO.Path.GetFullPath(full); } catch (Exception) { }
                if (f.Value != null) { list.Add("--printer"); list.Add(f.Value); }
                list.Add(full);
            }
            if (files.Count == 0 && printerName != null) { list.Add("--printer"); list.Add(printerName); }
            if (formName != null) { list.Add("--format"); list.Add(formName); }
            if (PrintData(args, 0)) list.Add(PrintDataFlag);
            return list.ToArray();
        }

        // QlLabelMaker.exe next to the running exe, or null (a build without it).
        public static string Path
        {
            get
            {
                string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), ExeName);
                return File.Exists(p) ? p : null;
            }
        }

        // Starts the editor (QlLabelMaker.exe next to this exe) from the label tool. Arguments: [file] [--printer name]
        // [--format name].
        public static void Start(string file, string printer, string format)
        {
            var args = new StringBuilder();
            if (file != null) args.Append(Quote(file)).Append(' ');
            if (printer != null) args.Append("--printer ").Append(Quote(printer)).Append(' ');
            if (format != null) args.Append("--format ").Append(Quote(format));
            Process.Start(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Application.ExecutablePath), ExeName), args.ToString().Trim());
        }

        // Command-line quoting for CommandLineToArgvW (backslashes before a quote doubled).
        public static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append('"'); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2);
            return b.Append('"').ToString();
        }
    }
}
