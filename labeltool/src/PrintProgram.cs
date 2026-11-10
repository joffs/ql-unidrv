// QlLabelPrint.exe: "Print from data" on the command line. A console exe built
// from the same sources (labeltool\build.cmd, /target:exe /main:QlLabels.PrintProgram), so scripts get its output and
// its exit code. It never asks and never opens a window: what the editor would ask about is reported with a code.
//
//   QlLabelPrint --print <file.qllabel> [--data <file.csv|file.json|->] [--set Name=Value ...] [options]
//   QlLabelPrint --fields <file.qllabel>      the label's data fields
//   QlLabelPrint --printers                   the QL printers with model, port and loaded roll
//
// The answer of --json is one object; the same shape is meant for a later MCP layer (--mcp, stdio): every answer has
// "ok", "code" (= the exit code), "error" (text or null) and "problems" (list), plus the command's own part.
using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace QlLabels
{
    static class PrintProgram
    {
        public const int Ok = 0, Usage = 1, NoTemplate = 2, DataError = 3, PrinterError = 4, NotReady = 5, SpoolerError = 6;
        public const int MaxTemplateBytes = 5 * 1024 * 1024;
        public const int MaxPreview = 1000;   // PNGs per --preview run

        // A failure with its exit code; Main turns it into the answer.
        sealed class Stop : Exception
        {
            public readonly int Code;
            public readonly List<Dictionary<string, object>> Problems;
            public Stop(int code, string message, List<Dictionary<string, object>> problems = null) : base(message) { Code = code; Problems = problems; }
        }

        // The command line, parsed.
        internal sealed class Options
        {
            public string Command;            // print, fields, printers, help
            public string Template;
            public string Data;               // a path or "-" (stdin)
            public List<KeyValuePair<string, string>> Set = new List<KeyValuePair<string, string>>();
            public string Printer;
            public int Copies = 1;
            public int RowFrom = 1, RowTo = int.MaxValue;
            public List<string> Names = new List<string>();   // --name: only the rows with these labelnames
            public string Preview;
            public bool DryRun, NoRollCheck, MissingEmpty, Strict, Json;
        }

        // Swappable for the tests: stdin, the printers, the roll check, the print.
        // stdin up to the CSV limit + 1 byte (the reader refuses more), so an endless pipe can't fill the memory
        internal static Func<byte[]> ReadStdin = () =>
        {
            using (var input = Console.OpenStandardInput())
            using (var ms = new MemoryStream())
            {
                var buffer = new byte[81920];
                int n;
                while (ms.Length <= CsvReader.MaxBytes && (n = input.Read(buffer, 0, (int)Math.Min(buffer.Length, CsvReader.MaxBytes + 1 - ms.Length))) > 0)
                    ms.Write(buffer, 0, n);
                return ms.ToArray();
            }
        };
        internal static Func<List<string>> Printers = QlPrinters.OurPrinters;
        internal static Func<string> WindowsDefault = () => { try { return new System.Drawing.Printing.PrinterSettings().PrinterName; } catch (Exception) { return null; } };
        internal static Func<string, LabelDoc, Tuple<LabelLayout, string>> Layout = (printer, doc) =>
        {
            string err;
            var layout = LabelJob.LayoutFor(doc, printer, out err);
            return Tuple.Create(layout, err);
        };
        internal static Func<string, bool> WorkOffline = p => { try { return Spooler.WorkOffline(p); } catch (Exception) { return false; } };
        internal static Func<string, bool> HasStatus = p => QlDevice.HasStatus(QlDevice.PortOf(p));
        internal static Func<string, PrinterCheck> Check = p => { try { return PrinterCheck.Run(p, true); } catch (Exception) { return null; } };
        internal static Action<LabelJob, IList<DataRecord>, string> PrintJob = (job, records, name) => job.Print(records, name);

        [STAThread]
        static int Main(string[] args)
        {
            Lang.WindowApp();   // the user's language (or --lang); --json keeps codes and field names stable
            if (Console.IsOutputRedirected) Console.OutputEncoding = new UTF8Encoding(false);
            var answer = Run(args);
            Write(answer, args.Contains("--json"));
            return (int)answer["code"];
        }

        // One run: the answer as a dictionary (code, error, problems, the command's own part).
        internal static Dictionary<string, object> Run(string[] args)
        {
            var answer = new Dictionary<string, object> { { "ok", false }, { "code", Ok }, { "error", null }, { "problems", new List<Dictionary<string, object>>() } };
            try
            {
                var o = Parse(args);
                answer["command"] = o.Command;
                if (o.Command == "help") answer["usage"] = UsageText();
                else if (o.Command == "printers") answer["printers"] = ListPrinters();
                else if (o.Command == "fields") FieldsOf(o, answer);
                else PrintData(o, answer);
                answer["ok"] = true;
            }
            catch (Stop s)
            {
                answer["code"] = s.Code;
                answer["error"] = s.Message;
                if (s.Problems != null) answer["problems"] = s.Problems;
                if (s.Code == Usage) answer["usage"] = UsageText();
            }
            catch (Exception e)
            {
                answer["code"] = SpoolerError;
                answer["error"] = e.Message;
            }
            return answer;
        }

        internal static Options Parse(string[] args)
        {
            var o = new Options();
            Func<int, string> next = i =>
            {
                if (i + 1 >= args.Length) throw new Stop(Usage, Lang.F("{0} needs a value.", args[i]));
                return args[i + 1];
            };
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                switch (a)
                {
                    case "--print": SetCommand(o, "print"); o.Template = next(i++); break;
                    case "--fields": SetCommand(o, "fields"); o.Template = next(i++); break;
                    case "--printers": SetCommand(o, "printers"); break;
                    case "--help": case "-h": case "/?": SetCommand(o, "help"); break;
                    case "--data": o.Data = next(i++); break;
                    case "--set":
                        {
                            string v = next(i++);
                            int eq = v.IndexOf('=');
                            if (eq <= 0) throw new Stop(Usage, Lang.F("--set needs Name=Value, not '{0}'.", v));
                            o.Set.Add(new KeyValuePair<string, string>(v.Substring(0, eq).Trim(), v.Substring(eq + 1)));
                            break;
                        }
                    case "--printer": o.Printer = next(i++); break;
                    case "--copies":
                        {
                            string v = next(i++);
                            if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out o.Copies) || o.Copies < 1 || o.Copies > DataRecord.MaxCopies)
                                throw new Stop(Usage, Lang.F("--copies needs a number from 1 to {0}, not '{1}'.", DataRecord.MaxCopies, v));
                            break;
                        }
                    case "--rows": ParseRows(o, next(i++)); break;
                    case "--name": o.Names.Add(next(i++).Trim()); break;
                    case "--preview": o.Preview = next(i++); break;
                    case "--dry-run": o.DryRun = true; break;
                    case "--no-roll-check": o.NoRollCheck = true; break;
                    case "--missing-empty": o.MissingEmpty = true; break;
                    case "--strict": o.Strict = true; break;
                    case "--json": o.Json = true; break;
                    case "--lang": next(i++); break;   // Lang.cs reads it
                    default: throw new Stop(Usage, Lang.F("Unknown option {0}.", a));
                }
            }
            if (o.Command == null) throw new Stop(Usage, Lang.T("Say what to do: --print, --fields or --printers."));
            if (o.Command != "print" && (o.Data != null || o.Set.Count > 0 || o.Preview != null || o.DryRun || o.Names.Count > 0))
                throw new Stop(Usage, Lang.T("--data, --set, --name, --preview and --dry-run only go with --print."));
            return o;
        }

        static void SetCommand(Options o, string command)
        {
            if (o.Command != null && o.Command != command) throw new Stop(Usage, Lang.T("Only one of --print, --fields and --printers at a time."));
            o.Command = command;
        }

        // --rows 3 | 2-10 | 5- (data rows, 1 = the first row after the header)
        static void ParseRows(Options o, string v)
        {
            int dash = v.IndexOf('-');
            string from = dash < 0 ? v : v.Substring(0, dash), to = dash < 0 ? v : v.Substring(dash + 1);
            int f, t = int.MaxValue;
            bool ok = int.TryParse(from, NumberStyles.None, CultureInfo.InvariantCulture, out f) && f >= 1 &&
                      (to.Length == 0 || int.TryParse(to, NumberStyles.None, CultureInfo.InvariantCulture, out t)) && t >= f;
            if (!ok) throw new Stop(Usage, Lang.F("--rows needs a row or a range such as 2-10, not '{0}'.", v));
            o.RowFrom = f; o.RowTo = t;
        }

        // ---------- the label file ----------

        static LabelDoc LoadTemplate(string path)
        {
            if (!File.Exists(path)) throw new Stop(NoTemplate, Lang.F("The label file {0} doesn't exist.", path));
            if (new FileInfo(path).Length > MaxTemplateBytes) throw new Stop(NoTemplate, Lang.F("The label file {0} is larger than {1} MB.", path, MaxTemplateBytes / (1024 * 1024)));
            try { return LabelDoc.Load(path); }
            catch (Exception e) { throw new Stop(NoTemplate, Lang.F("The label file {0} can't be read: {1}", path, e.Message)); }
        }

        static void FieldsOf(Options o, Dictionary<string, object> answer)
        {
            var doc = LoadTemplate(o.Template);
            answer["template"] = Path.GetFullPath(o.Template);
            answer["label"] = doc.FormName;
            answer["fields"] = DataFields.Of(doc).Select(f => new Dictionary<string, object>
            {
                { "name", f.Name }, { "default", f.Default }, { "required", f.Required }
            }).ToList();
        }

        // ---------- printers ----------

        static List<Dictionary<string, object>> ListPrinters()
        {
            string windowsDefault = WindowsDefault();
            var list = new List<Dictionary<string, object>>();
            foreach (var p in Printers())
            {
                var model = QlPrinters.ModelOf(p);
                var item = new Dictionary<string, object>
                {
                    { "name", p }, { "model", model != null ? model.Name : null }, { "port", QlDevice.PortOf(p) },
                    { "default", string.Equals(p, windowsDefault, StringComparison.OrdinalIgnoreCase) },
                    { "offline", WorkOffline(p) }
                };
                AddRoll(item, p);
                list.Add(item);
            }
            return list;
        }

        // The printer's state and loaded roll: "roll" null when it can't be asked.
        static PrinterCheck AddRoll(Dictionary<string, object> item, string printer)
        {
            PrinterCheck c = HasStatus(printer) ? Check(printer) : null;
            item["status"] = c == null ? null : c.Health.ToString().ToLowerInvariant();
            item["message"] = c == null ? null : c.Detail ?? c.Headline;
            item["roll"] = c != null && c.Media != null ? c.Media.LoadedText : null;
            return c;
        }

        // --printer, else Windows' default printer when it has the label's paper, else the only one that has it.
        static string ChoosePrinter(Options o, LabelDoc doc, out LabelLayout layout)
        {
            var ours = Printers();
            if (o.Printer != null)
            {
                string name = ours.FirstOrDefault(p => string.Equals(p, o.Printer, StringComparison.OrdinalIgnoreCase));
                if (name == null) throw new Stop(PrinterError, Lang.F("{0} is not a QL printer of this driver. QL printers: {1}", o.Printer, Names(ours)));
                var l = Layout(name, doc);
                if (l.Item1 == null) throw new Stop(PrinterError, l.Item2);
                layout = l.Item1;
                return name;
            }
            var fit = new List<Tuple<string, LabelLayout>>();
            foreach (var p in ours)
            {
                var l = Layout(p, doc);
                if (l.Item1 != null) fit.Add(Tuple.Create(p, l.Item1));
            }
            string windowsDefault = WindowsDefault();
            var chosen = fit.FirstOrDefault(f => string.Equals(f.Item1, windowsDefault, StringComparison.OrdinalIgnoreCase)) ?? (fit.Count == 1 ? fit[0] : null);
            if (chosen == null && fit.Count > 1)
            {
                // several: the only one whose loaded roll fits
                var media = fit[0].Item2.Label.Media;
                var rolls = fit.Where(f => { var c = HasStatus(f.Item1) ? Check(f.Item1) : null; return c != null && c.Media != null && c.Media.Fits(f.Item2.Label.Media) == true; }).ToList();
                if (rolls.Count == 1) chosen = rolls[0];
            }
            if (chosen == null)
                throw new Stop(PrinterError, fit.Count == 0
                    ? Lang.F("No QL printer has the paper for this label. QL printers: {0}", Names(ours))
                    : Lang.F("Several printers fit: {0}. Choose one with --printer.", Names(fit.Select(f => f.Item1))));
            layout = chosen.Item2;
            return chosen.Item1;
        }

        static string Names(IEnumerable<string> names)
        {
            var list = names.ToList();
            return list.Count == 0 ? Lang.T("none") : string.Join(", ", list);
        }

        // ---------- print ----------

        static void PrintData(Options o, Dictionary<string, object> answer)
        {
            var doc = LoadTemplate(o.Template);
            answer["template"] = Path.GetFullPath(o.Template);
            LabelLayout layout;
            string printer = ChoosePrinter(o, doc, out layout);
            answer["printer"] = printer;
            answer["label"] = layout.Label.Name;
            var job = new LabelJob(doc, layout) { Copies = o.Copies, MissingEmpty = o.MissingEmpty };

            // The records: the data file (CSV or JSON), each with --set's values on top; without data one record
            List<string> columns;
            var records = ReadRecords(o, out columns);
            var match = job.Match(columns);
            answer["fields"] = new Dictionary<string, object>
            {
                { "found", match.Found.Select(f => f.Name).ToList() }, { "missing", match.Missing.Select(f => f.Name).ToList() },
                { "unused", match.Unused }
            };
            records = records.Where(r => r.Row >= o.RowFrom && r.Row <= o.RowTo).ToList();
            if (o.Names.Count > 0)
            {
                // --name A1234 (repeatable): the rows whose labelname is one of them, case-insensitive; a name no row has stops
                var unknown = o.Names.Where(n => !records.Any(r => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
                if (unknown.Count > 0) throw new Stop(DataError, Lang.F("No row has the labelname {0}.", string.Join(", ", unknown)));
                records = records.Where(r => o.Names.Any(n => string.Equals(r.Name, n, StringComparison.OrdinalIgnoreCase))).ToList();
            }
            if (records.Count == 0) throw new Stop(DataError, Lang.T("The data has no rows to print (see --rows)."));
            job.Records = records;

            // All or nothing: every record filled and checked before anything is printed (a row with copies 0 is skipped)
            var problems = new List<Dictionary<string, object>>();
            bool blocking = false;
            foreach (var r in job.ToCheck(records))
                foreach (var p in job.Check(r))
                {
                    bool blocks = p.Blocks || o.Strict;   // barcode/overflow: warnings, printed anyway (--strict: code 3)
                    blocking |= blocks;
                    problems.Add(new Dictionary<string, object>
                    {
                        { "row", p.Row }, { "name", p.Name ?? "" }, { "kind", Kind(p.Kind) }, { "field", p.Field }, { "message", p.ToString() }, { "blocks", blocks }
                    });
                }
            answer["problems"] = problems;
            int labels = (int)Math.Min(job.PageCount(records), int.MaxValue);
            answer["records"] = records.Count;
            answer["labels"] = labels;
            if (labels > LabelJob.MaxPages) throw new Stop(DataError, Lang.F("More than {0} labels in one print job.", LabelJob.MaxPages), problems);
            if (blocking)
                throw new Stop(DataError, Lang.T("The data has errors (see the list); nothing was printed."), problems);

            if (o.Preview != null)
            {
                if (records.Count > MaxPreview)
                    throw new Stop(DataError, Lang.F("--preview makes at most {0} pictures; choose rows with --rows.", MaxPreview), problems);
                Directory.CreateDirectory(o.Preview);
                var files = new List<string>();
                foreach (var r in records)
                    using (var bmp = job.Render(r))
                    {
                        string file = Path.Combine(o.Preview, string.Format(CultureInfo.InvariantCulture, "row-{0:D4}.png", r.Row));
                        bmp.Save(file, ImageFormat.Png);
                        files.Add(Path.GetFullPath(file));
                    }
                answer["preview"] = files;
                answer["printed"] = false;
                return;
            }

            if (!o.NoRollCheck) answer["roll"] = RollCheck(printer, layout, doc);
            if (o.DryRun) { answer["printed"] = false; return; }
            if (labels == 0) { answer["printed"] = false; return; }   // every row has copies 0
            try { PrintJob(job, records, Path.GetFileNameWithoutExtension(o.Template)); }
            catch (InvalidDataException e) { throw new Stop(DataError, e.Message); }   // the engine's limits
            catch (Exception e) { throw new Stop(SpoolerError, Lang.F("Windows didn't take the print job: {0}", e.Message)); }
            answer["printed"] = true;
        }

        static string Kind(RecordProblemKind k)
        {
            switch (k)
            {
                case RecordProblemKind.MissingField: return "missing";
                case RecordProblemKind.Copies: return "copies";
                case RecordProblemKind.Barcode: return "barcode";
                default: return "overflow";
            }
        }

        // §5: offline queue, a printer error or warning, a roll that doesn't fit, or a roll that can't be read → code 5.
        static string RollCheck(string printer, LabelLayout layout, LabelDoc doc)
        {
            if (WorkOffline(printer))
                throw new Stop(NotReady, Lang.F("{0} is set to 'Use Printer Offline' in Windows. Use it online first (QL Printer Status).", printer));
            if (!HasStatus(printer))
                throw new Stop(NotReady, Lang.F("The loaded roll of {0} can't be read, so nothing was printed. On a shared printer, turn on the status service on the PC it is plugged into. To print without the check: --no-roll-check.", printer));
            var c = Check(printer);
            if (c == null || c.Media == null && c.Health != Health.Error)
                throw new Stop(NotReady, Lang.F("{0} didn't answer, so its loaded roll is unknown and nothing was printed. To print without the check: --no-roll-check.", printer));
            if (c.Health == Health.Error || c.Health == Health.Warning)
                throw new Stop(NotReady, printer + ": " + (c.Detail ?? c.Headline) + (c.Advice.Count > 0 ? " " + string.Join(" ", c.Advice) : ""));
            var media = layout.Label.Media;
            if (c.Media.Fits(media) == false)
                throw new Stop(NotReady, Lang.F("The printer has {0} loaded, but '{1}' needs {2}. Load the matching roll first.",
                                                 c.Media.LoadedText, layout.Label.Name, media != null ? media.Display : Lang.T("another roll")));
            return c.Media.LoadedText;
        }

        // ---------- data ----------

        internal static List<DataRecord> ReadRecords(Options o, out List<string> columns)
        {
            List<DataRecord> records;
            columns = new List<string>();
            if (o.Data == null) records = new List<DataRecord> { new DataRecord { Row = 1, Line = 1 } };
            else
            {
                byte[] bytes;
                if (o.Data == "-") bytes = ReadStdin();
                else
                {
                    if (!File.Exists(o.Data)) throw new Stop(DataError, Lang.F("The data file {0} doesn't exist.", o.Data));
                    if (new FileInfo(o.Data).Length > CsvReader.MaxBytes) throw new Stop(DataError, Lang.F("The file is larger than {0} MB.", CsvReader.MaxBytes / (1024 * 1024)));
                    bytes = File.ReadAllBytes(o.Data);
                }
                try
                {
                    var table = DataFile.Read(bytes, o.Data);   // CSV or JSON (PrintDataFile.cs)
                    columns.AddRange(table.Columns);
                    records = DataRecord.FromTable(table);
                }
                catch (InvalidDataException e) { throw new Stop(DataError, e.Message); }
            }
            foreach (var r in records)
                foreach (var kv in o.Set) r.Values[kv.Key] = kv.Value;
            var seen = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in o.Set) if (seen.Add(kv.Key)) columns.Add(kv.Key);
            return records;
        }

        // ---------- output ----------

        static void Write(Dictionary<string, object> answer, bool json) { Write(answer, json, Console.Out, Console.Error); }

        // The answer as text (or JSON). Data values reach the console here (a field's value in a message, a printer's
        // name): control characters except tab and line breaks are dropped, so data can't send escape sequences to the
        // terminal. JSON escapes them itself.
        internal static void Write(Dictionary<string, object> answer, bool json, TextWriter output, TextWriter errors)
        {
            if (json) { output.WriteLine(new JavaScriptSerializer().Serialize(answer)); return; }
            Action<string> outLine = s => output.WriteLine(Clean(s));
            Action<string> errLine = s => errors.WriteLine(Clean(s));
            if (answer.ContainsKey("usage") && (int)answer["code"] == Ok) { outLine((string)answer["usage"]); return; }
            string command = answer.ContainsKey("command") ? answer["command"] as string : null;
            if (command == "printers" && answer.ContainsKey("printers"))
            {
                var printers = (List<Dictionary<string, object>>)answer["printers"];
                if (printers.Count == 0) outLine(Lang.T("No QL printers."));
                foreach (var p in printers)
                    outLine(string.Format("{0}{1}  {2}  {3}  {4}", p["name"], (bool)p["default"] ? " *" : "", p["model"] ?? "?", p["port"] ?? "",
                                          p["roll"] ?? Lang.T("roll unknown")));
            }
            if (command == "fields" && answer.ContainsKey("fields"))
                foreach (var f in (List<Dictionary<string, object>>)answer["fields"])
                    outLine(string.Format(f["default"] != null ? "{0}  ({1}: {2})" : "{0}", f["name"], Lang.T("default"), f["default"]));
            foreach (var p in (List<Dictionary<string, object>>)answer["problems"]) errLine(Convert.ToString(p["message"]));
            if (answer["error"] != null) { errLine((string)answer["error"]); if (answer.ContainsKey("usage")) errLine((string)answer["usage"]); return; }
            if (command != "print") return;
            if (answer.ContainsKey("preview"))
            {
                var files = (List<string>)answer["preview"];
                string folder = Path.GetDirectoryName(files.FirstOrDefault() ?? ".");
                outLine(files.Count == 1 ? Lang.F("1 picture in {0}.", folder) : Lang.F("{0} pictures in {1}.", files.Count, folder));
                return;
            }
            bool one = Convert.ToInt32(answer["labels"]) == 1;
            if (true.Equals(answer["printed"]))
                outLine(one ? Lang.F("1 label sent to {0}.", answer["printer"]) : Lang.F("{0} labels sent to {1}.", answer["labels"], answer["printer"]));
            else
                outLine(one ? Lang.F("Checked: 1 label for {0}, nothing printed.", answer["printer"])
                            : Lang.F("Checked: {0} labels for {1}, nothing printed.", answer["labels"], answer["printer"]));
        }

        // Text without control characters (C0 except tab and line breaks, DEL, C1): no terminal escape sequences
        internal static string Clean(string s)
        {
            if (s == null) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (c == '\t' || c == '\n' || c == '\r' || !(c < 0x20 || (c >= 0x7F && c <= 0x9F))) sb.Append(c);
            return sb.ToString();
        }

        static string UsageText()
        {
            // the options are the same in every language; only the exit codes' line is translated
            return "QlLabelPrint --print <label.qllabel> [--data <file.csv|file.json|->] [--set Name=Value ...]\n" +
                          "             [--printer <name>] [--copies N] [--rows 2-10] [--name <labelname> ...] [--missing-empty] [--strict]\n" +
                          "             [--preview <folder>] [--dry-run] [--no-roll-check] [--json]\n" +
                          "QlLabelPrint --fields <label.qllabel> [--json]\n" +
                   "QlLabelPrint --printers [--json]\n" +
                   Lang.T("Exit codes: 0 done, 1 usage, 2 label file, 3 data, 4 printer, 5 printer not ready or roll unknown, 6 Windows refused the job.");
        }
    }
}
