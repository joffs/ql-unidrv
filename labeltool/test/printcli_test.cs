// Unit test for QlLabelPrint.exe (PrintProgram.cs): the options, the exit codes 0-6, the printer choice, the roll
// check, CSV/JSON/stdin/--set data, --rows, --missing-empty, warnings vs --strict, --preview and the --json answer.
// No printer and no spooler: the printers, layouts, roll checks and the print itself are swapped for fakes (the layout
// is estimated). Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace QlLabels.Tests
{
    static class PrintCliTest
    {
        static int failures;
        static string dir;
        static List<string> printers;
        static string windowsDefault;
        static Dictionary<string, PrinterCheck> checks;
        static HashSet<string> withPaper, offline;
        static List<int> printed;   // pages per print call
        static Exception printFails;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static int Main()
        {
            Lang.Force("en");
            dir = Path.Combine(Path.GetTempPath(), "ql-printcli-test-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(dir);
            try
            {
                Fakes();
                Usage();
                Template();
                PrinterChoice();
                Data();
                Roll();
                Output();
            }
            finally { try { Directory.Delete(dir, true); } catch (Exception) { } }
            Console.WriteLine(failures == 0 ? "printcli_test: all OK" : "printcli_test: " + failures + " FAIL");
            return failures == 0 ? 0 : 1;
        }

        static void Fakes()
        {
            printers = new List<string> { "QL-800", "QL-570" };
            windowsDefault = "Microsoft Print to PDF";
            withPaper = new HashSet<string> { "QL-800" };
            offline = new HashSet<string>();
            checks = new Dictionary<string, PrinterCheck> { { "QL-800", Roll(62, 0x0A) }, { "QL-570", Roll(62, 0x0A) } };
            printed = new List<int>();
            var label = new LabelChoice { Name = "Test 62", Media = MediaTable.ById(259), LengthMm = 50, FeedMm = 3, IsFormat = true };
            PrintProgram.Printers = () => printers.ToList();
            PrintProgram.WindowsDefault = () => windowsDefault;
            PrintProgram.Layout = (p, doc) => withPaper.Contains(p) ? Tuple.Create(LabelLayout.Estimate(label, false, false), (string)null)
                                                                     : Tuple.Create((LabelLayout)null, p + " has no paper for this label.");
            PrintProgram.WorkOffline = p => offline.Contains(p);
            PrintProgram.HasStatus = p => checks.ContainsKey(p);
            PrintProgram.Check = p => checks.ContainsKey(p) ? checks[p] : null;
            PrintProgram.PrintJob = (job, records, name) =>
            {
                if (printFails != null) throw printFails;
                printed.Add(records.Sum(r => job.CopiesOf(r)));
            };
        }

        // A printer check with a loaded roll: width in mm, media type 0x0A continuous / 0x0B die-cut
        static PrinterCheck Roll(int widthMm, int type)
        {
            var raw = new byte[QlStatus.Size];
            raw[10] = (byte)widthMm; raw[11] = (byte)type;
            return new PrinterCheck { Media = new QlStatus(raw, DateTime.Now), Health = Health.Ok };
        }

        static string templatePath;

        static Dictionary<string, object> Run(params string[] args) { return PrintProgram.Run(args); }
        static int Code(Dictionary<string, object> a) { return (int)a["code"]; }
        static string Err(Dictionary<string, object> a) { return a["error"] as string ?? ""; }

        static string File(string name, string content)
        {
            string path = Path.Combine(dir, name);
            System.IO.File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        static void Usage()
        {
            Check(Code(Run()) == 1, "no command: 1");
            Check(Code(Run("--bogus")) == 1 && Err(Run("--bogus")).Contains("--bogus"), "unknown option: 1");
            Check(Code(Run("--print")) == 1, "--print without a file: 1");
            Check(Code(Run("--printers", "--fields", "x")) == 1, "two commands: 1");
            Check(Code(Run("--fields", "x.qllabel", "--data", "a.csv")) == 1, "--data only with --print: 1");
            Check(Code(Run("--print", "x", "--copies", "0")) == 1 && Code(Run("--print", "x", "--copies", "1000")) == 1, "--copies 1-999");
            Check(Code(Run("--print", "x", "--rows", "5-2")) == 1 && Code(Run("--print", "x", "--rows", "a")) == 1, "--rows checked");
            Check(Code(Run("--print", "x", "--set", "novalue")) == 1, "--set needs Name=Value");
            var help = Run("--help");
            Check(Code(help) == 0 && ((string)help["usage"]).Contains("--missing-empty") && ((string)help["usage"]).Contains("--strict"), "--help");
        }

        static void Template()
        {
            var doc = new LabelDoc { FormName = "Test 62", PaperId = 259 };
            doc.Objects.Add(new TextObject { Text = "{Artikel}", Font = "Arial", Size = 12, X = 3, Y = 3, W = 40, H = 8 });
            doc.Objects.Add(new TextObject { Text = "{Lager|A1}", Font = "Arial", Size = 8, X = 3, Y = 15, W = 40, H = 6 });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Ean13, Data = "{EAN|4006381333931}", X = 3, Y = 30, W = 40, H = 15 });
            templatePath = Path.Combine(dir, "Artikel.qllabel");
            doc.Save(templatePath);

            Check(Code(Run("--print", Path.Combine(dir, "nope.qllabel"))) == 2, "missing label file: 2");
            Check(Code(Run("--fields", File("bad.qllabel", "{not json"))) == 2, "unreadable label file: 2");
            var big = Path.Combine(dir, "big.qllabel");
            using (var f = System.IO.File.Create(big)) f.SetLength(PrintProgram.MaxTemplateBytes + 1);
            Check(Code(Run("--fields", big)) == 2, "label file over 5 MB: 2");

            var fields = Run("--fields", templatePath);
            var list = (List<Dictionary<string, object>>)fields["fields"];
            Check(Code(fields) == 0 && list.Count == 3 && (string)list[0]["name"] == "Artikel" && (bool)list[0]["required"] &&
                  (string)list[1]["default"] == "A1" && !(bool)list[1]["required"], "--fields: names, defaults, required");
        }

        static void PrinterChoice()
        {
            var a = Run("--print", templatePath, "--set", "Artikel=x", "--no-roll-check");
            Check(Code(a) == 0 && (string)a["printer"] == "QL-800", "the only printer with the paper: " + Err(a));
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x", "--printer", "Nope")) == 4, "--printer not a QL printer: 4");
            var b = Run("--print", templatePath, "--set", "Artikel=x", "--printer", "ql-570");
            Check(Code(b) == 4 && Err(b).Contains("no paper"), "--printer without the paper: 4 with the reason");
            withPaper.Add("QL-570");
            checks["QL-570"] = Roll(29, 0x0A);
            var c = Run("--print", templatePath, "--set", "Artikel=x", "--dry-run");
            Check(Code(c) == 0 && (string)c["printer"] == "QL-800", "several: the only one whose roll fits: " + Err(c));
            checks["QL-570"] = Roll(62, 0x0A);
            var d = Run("--print", templatePath, "--set", "Artikel=x", "--dry-run");
            Check(Code(d) == 4 && Err(d).Contains("--printer"), "several fit: 4, choose with --printer");
            windowsDefault = "QL-570";
            var e = Run("--print", templatePath, "--set", "Artikel=x", "--dry-run");
            Check(Code(e) == 0 && (string)e["printer"] == "QL-570", "Windows' default printer wins");
            windowsDefault = "Microsoft Print to PDF";
            withPaper.Remove("QL-570");
            printers.Clear();
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x")) == 4, "no QL printers: 4");
            printers.AddRange(new[] { "QL-800", "QL-570" });
        }

        static void Data()
        {
            string csv = File("liste.csv", "Artikel;EAN;copies\nSchraube;4006381333931;2\nMutter;;1\nScheibe;;0\n");
            printed.Clear();
            var a = Run("--print", templatePath, "--data", csv, "--copies", "3");
            Check(Code(a) == 0 && (bool)a["printed"] && printed.SequenceEqual(new[] { 9 }) && (int)a["labels"] == 9 && (int)a["records"] == 3,
                  "CSV: copies column times --copies, a 0 row skipped: " + string.Join(",", printed) + " " + Err(a));
            printed.Clear();
            var r = Run("--print", templatePath, "--data", csv, "--rows", "2");
            Check(Code(r) == 0 && (int)r["records"] == 1 && printed.SequenceEqual(new[] { 1 }), "--rows 2: one row");
            Check(Code(Run("--print", templatePath, "--data", csv, "--rows", "9-")) == 3, "--rows past the end: 3");

            string missing = File("missing.csv", "EAN\n4006381333931\n");
            printed.Clear();
            var m = Run("--print", templatePath, "--data", missing);
            var mp = (List<Dictionary<string, object>>)m["problems"];
            Check(Code(m) == 3 && printed.Count == 0 && mp.Count == 1 && (string)mp[0]["kind"] == "missing" && (int)mp[0]["row"] == 1 && (bool)mp[0]["blocks"],
                  "missing field: 3, nothing printed, row and kind named");
            var me = Run("--print", templatePath, "--data", missing, "--missing-empty");
            Check(Code(me) == 0 && printed.Count == 1, "--missing-empty prints");
            Check(((List<string>)((Dictionary<string, object>)me["fields"])["missing"]).Contains("Artikel"), "field match: Artikel missing");

            string badEan = File("badean.csv", "Artikel,EAN\nx,123\n");
            printed.Clear();
            var w = Run("--print", templatePath, "--data", badEan);
            var wp = (List<Dictionary<string, object>>)w["problems"];
            Check(Code(w) == 0 && printed.Count == 1 && wp.Count == 1 && (string)wp[0]["kind"] == "barcode" && !(bool)wp[0]["blocks"],
                  "bad EAN: a warning, printed: " + Err(w));
            printed.Clear();
            var s = Run("--print", templatePath, "--data", badEan, "--strict");
            Check(Code(s) == 3 && printed.Count == 0, "--strict: a warning is 3");

            string copies = File("copies.csv", "Artikel,copies\nx,zwei\n");
            Check(Code(Run("--print", templatePath, "--data", copies)) == 3, "bad copies cell: 3");
            Check(Code(Run("--print", templatePath, "--data", File("open.csv", "Artikel\n\"x\n"))) == 3, "bad CSV: 3");
            Check(Code(Run("--print", templatePath, "--data", Path.Combine(dir, "none.csv"))) == 3, "missing data file: 3");

            // --name: rows by their labelname (case-insensitive, repeatable); an unknown name stops with 3; problems carry the name
            string named = File("named.csv", "Artikel;labelname;copies\nSchraube;A1;2\nMutter;b2;1\nScheibe;;5\n");
            printed.Clear();
            var byName = Run("--print", templatePath, "--data", named, "--name", "B2");
            Check(Code(byName) == 0 && printed.SequenceEqual(new[] { 1 }), "--name B2: one label: " + Err(byName));
            printed.Clear();
            Check(Code(Run("--print", templatePath, "--data", named, "--name", "a1", "--name", "B2")) == 0 && printed.SequenceEqual(new[] { 3 }), "--name twice: 3 labels");
            printed.Clear();
            var unknownName = Run("--print", templatePath, "--data", named, "--name", "zz9");
            Check(Code(unknownName) == 3 && printed.Count == 0 && Err(unknownName).Contains("zz9"), "--name unknown: 3: " + Err(unknownName));
            Check(Code(Run("--fields", templatePath, "--name", "a1")) == 1, "--name without --print: 1");
            var namedMissing = Run("--print", templatePath, "--data", File("named-missing.json", "[{\"labelname\":\"A1\"}]"));
            var nmProblems = (List<Dictionary<string, object>>)namedMissing["problems"];
            Check(Code(namedMissing) == 3 && nmProblems.Count > 0 && (string)nmProblems[0]["name"] == "A1", "a problem names the labelname (JSON data)");

            string json = File("liste.json", "[{\"Artikel\":\"Schraube\",\"copies\":2},{\"Artikel\":\"Mutter\",\"EAN\":4006381333931}]");
            printed.Clear();
            var j = Run("--print", templatePath, "--data", json);
            Check(Code(j) == 0 && printed.SequenceEqual(new[] { 3 }), "JSON list with copies: " + Err(j));
            Check(Code(Run("--print", templatePath, "--data", File("bad.json", "[1,2]"))) == 3, "JSON of numbers: 3");
            Check(Code(Run("--print", templatePath, "--data", File("nested.json", "{\"Artikel\":{\"a\":1}}"))) == 3, "nested JSON value: 3");

            PrintProgram.ReadStdin = () => Encoding.UTF8.GetBytes("{\"Artikel\":\"von stdin\"}");
            printed.Clear();
            Check(Code(Run("--print", templatePath, "--data", "-")) == 0 && printed.SequenceEqual(new[] { 1 }), "stdin JSON object");
            PrintProgram.ReadStdin = () => Encoding.UTF8.GetBytes("Artikel\na\nb\n");
            printed.Clear();
            Check(Code(Run("--print", templatePath, "--data", "-")) == 0 && printed.SequenceEqual(new[] { 2 }), "stdin CSV");

            string eleven = File("eleven.csv", "Artikel\n" + string.Join("\n", Enumerable.Range(1, 11).Select(i => "a" + i)) + "\n");
            printed.Clear();
            var many = Run("--print", templatePath, "--data", eleven, "--copies", "999");
            Check(Code(many) == 3 && printed.Count == 0 && Err(many).Contains("10000"), "over 10,000 labels: 3, nothing sent: " + Err(many));
            Check(Code(Run("--print", templatePath, "--data", File("long.json", "{\"Artikel\":\"" + new string('x', CsvReader.MaxCellChars + 1) + "\"}"))) == 3,
                  "JSON value over the cell limit: 3");

            // distinct JSON names over all rows: at most 1,000, like CSV columns
            var names = "[" + string.Join(",", Enumerable.Range(0, 3).Select(row => "{" + string.Join(",", Enumerable.Range(0, 400).Select(k => "\"k" + row + "_" + k + "\":1")) + "}")) + "]";
            var tooMany = Run("--print", templatePath, "--data", File("names.json", names));
            Check(Code(tooMany) == 3 && Err(tooMany).Contains("1000"), "over 1,000 JSON names across rows: 3: " + Err(tooMany));

            string rows = File("rows.csv", "Artikel\n" + string.Join("\n", Enumerable.Range(1, PrintProgram.MaxPreview + 1).Select(i => "a" + i)) + "\n");
            var bigPreview = Run("--print", templatePath, "--data", rows, "--preview", Path.Combine(dir, "big-preview"));
            Check(Code(bigPreview) == 3 && !Directory.Exists(Path.Combine(dir, "big-preview")), "--preview over 1,000 rows: 3, no folder");

            // control characters from the data never reach the console text (JSON escapes them)
            var esc = Run("--print", templatePath, "--data", File("esc.csv", "EAN\n123\n"), "--set", "Artikel=a\u001b[31mb");
            var o = new StringWriter(); var e = new StringWriter();
            PrintProgram.Write(new Dictionary<string, object> { { "ok", false }, { "code", 3 }, { "error", "x\u001b]0;title\u0007y\u009b\tz" },
                                                                { "problems", new List<Dictionary<string, object>>() } }, false, o, e);
            Check(e.ToString() == "x]0;titley\tz" + Environment.NewLine && o.ToString() == "", "Clean: ESC, BEL and C1 dropped, tab kept: " + e);
            Check(PrintProgram.Clean("a\r\nb\u007f") == "a\r\nb" && Code(esc) == 0, "Clean keeps line breaks, drops DEL");

            printed.Clear();
            var set = Run("--print", templatePath, "--data", missing, "--set", "Artikel=fest");
            Check(Code(set) == 0 && printed.Count == 1, "--set fills a field for every row");
        }

        static void Roll()
        {
            printed.Clear();
            var ok = Run("--print", templatePath, "--set", "Artikel=x");
            Check(Code(ok) == 0 && (string)ok["roll"] != null && printed.Count == 1, "roll fits: printed");
            checks["QL-800"] = Roll(29, 0x0A);
            printed.Clear();
            var wrong = Run("--print", templatePath, "--set", "Artikel=x");
            Check(Code(wrong) == 5 && printed.Count == 0 && Err(wrong).Contains("29"), "wrong roll: 5: " + Err(wrong));
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x", "--dry-run")) == 5, "--dry-run checks the roll too");
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x", "--no-roll-check")) == 0, "--no-roll-check prints");
            checks["QL-800"] = new PrinterCheck { Health = Health.Error, Headline = "Cover open" };
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x")) == 5, "printer error: 5");
            checks.Remove("QL-800");
            var unknown = Run("--print", templatePath, "--set", "Artikel=x");
            Check(Code(unknown) == 5 && Err(unknown).Contains("--no-roll-check"), "roll unknown (no status): 5");
            checks["QL-800"] = Roll(62, 0x0A);
            offline.Add("QL-800");
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x")) == 5, "Use Printer Offline: 5");
            offline.Clear();
            printFails = new System.ComponentModel.Win32Exception(1801);
            Check(Code(Run("--print", templatePath, "--set", "Artikel=x")) == 6, "spooler error: 6");
            printFails = null;
            printed.Clear();
            var dry = Run("--print", templatePath, "--set", "Artikel=x", "--dry-run");
            Check(Code(dry) == 0 && !(bool)dry["printed"] && printed.Count == 0, "--dry-run prints nothing");
        }

        static void Output()
        {
            string csv = File("preview.csv", "Artikel\neins\nzwei\n");
            string folder = Path.Combine(dir, "preview");
            printed.Clear();
            var p = Run("--print", templatePath, "--data", csv, "--preview", folder);
            var files = (List<string>)p["preview"];
            Check(Code(p) == 0 && printed.Count == 0 && files.Count == 2 && files.All(System.IO.File.Exists) && Path.GetFileName(files[1]) == "row-0002.png",
                  "--preview: one PNG per row, nothing printed");
            var a = Run("--print", templatePath, "--set", "Artikel=x", "--json");
            string json = new JavaScriptSerializer().Serialize(a);
            var back = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
            Check((bool)back["ok"] && (int)back["code"] == 0 && back.ContainsKey("problems") && back.ContainsKey("printer") && (string)back["command"] == "print",
                  "--json answer: ok, code, problems, printer, command");
            var one = new StringWriter();
            PrintProgram.Write(Run("--print", templatePath, "--set", "Artikel=x"), false, one, new StringWriter());
            var two = new StringWriter();
            PrintProgram.Write(Run("--print", templatePath, "--set", "Artikel=x", "--copies", "2", "--dry-run"), false, two, new StringWriter());
            Check(one.ToString().StartsWith("1 label sent to QL-800.") && two.ToString().StartsWith("Checked: 2 labels for QL-800"),
                  "text: one label in the singular: " + one + two);
            var e = new JavaScriptSerializer().Serialize(Run("--print", Path.Combine(dir, "nope.qllabel"), "--json"));
            Check(e.Contains("\"ok\":false") && e.Contains("\"code\":2") && e.Contains("\"error\":"), "--json error answer");
        }
    }
}
