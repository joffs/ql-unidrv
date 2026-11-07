// QlLabelMaker.exe - the label editor "QL Label Maker" as its own program:
//   QlLabelMaker.exe [file.qllabel] [--printer name] [--format name] [--lang en|de]
// Built from the same sources as QlLabelFormats.exe (labeltool\build.cmd, /main:QlLabels.EditorProgram); .qllabel files open
// here (file association written by install.ps1).
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace QlLabels
{
    static class EditorProgram
    {
        public const string ExeName = "QlLabelMaker.exe";

        [STAThread]
        static int Main(string[] args)
        {
            Lang.WindowApp();   // every start is a window, also with a file as the first argument
            Run(args, 0);
            return 0;
        }

        // The editor in this process; args from index start: [file] [--printer name] [--format name] [--lang x].
        public static void Run(string[] args, int start)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string file = null, printerName = null, formName = null;
            for (int i = start; i < args.Length; i++)
            {
                if (args[i] == "--printer" && i + 1 < args.Length) printerName = args[++i];
                else if (args[i] == "--format" && i + 1 < args.Length) formName = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) i++;   // Lang.cs reads it
                else file = args[i];
            }
            Application.Run(new EditorForm(file, printerName, formName));
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
