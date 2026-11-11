// Unit test for print from data (PrintData.cs, PrintJob.cs): data fields and defaults, filling without double
// expansion (dates, barcode tokens, QR escaping, formatted words), the field match both ways, the CSV reader
// (delimiters, quotes, encodings, empty lines, copies) and the job's checks and rendering. No printer needed (the
// layout is estimated). Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace QlLabels.Tests
{
    static class PrintDataTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static int Main()
        {
            Lang.Force("en");
            FieldList();
            Fill();
            Csv();
            Job();
            Limits();
            Console.WriteLine(failures == 0 ? "printdata_test: all OK" : "printdata_test: " + failures + " FAIL");
            return failures == 0 ? 0 : 1;
        }

        static string Join(IEnumerable<DataField> f) { return string.Join(" ", f.Select(x => x.ToString())); }

        static void FieldList()
        {
            Check(Join(DataFields.In("{Artikel} {Preis|0,00} {date} {date+3:dd.MM.} {time} {{x}} {gs} {x1D} {Lager ort} { a}")) == "{Artikel} {Preis|0,00} {Lager ort}",
                  "fields in a text: " + Join(DataFields.In("{Artikel} {Preis|0,00} {date} {date+3:dd.MM.} {time} {{x}} {gs} {x1D} {Lager ort} { a}")));
            var doc = new LabelDoc();
            doc.Objects.Add(new TextObject { Text = "{Artikel} – {Nummer|0}" });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Code128, Data = "{nummer}{gs}{Charge|-}" });
            var qr = new BarcodeObject { Kind = Symbology.Qr };
            qr.SetQrType(QrType.Url);
            qr.SetQrField("url", "https://example.org/{Artikel}");
            doc.Objects.Add(qr);
            var f = DataFields.Of(doc);
            Check(f.Count == 3 && f[0].Name == "Artikel" && f[0].Required && f[1].Name == "Nummer" && f[1].Default == "0" && f[1].Required && !f[2].Required,
                  "the label's fields, one per name, required when one use has no default: " + Join(f) + " " + string.Join(",", f.Select(x => x.Required)));
            var m = FieldMatch.Of(f, new[] { "ARTIKEL", "Preis", "copies", " " });
            Check(m.Found.Select(x => x.Name).SequenceEqual(new[] { "Artikel" }) && m.Missing.Select(x => x.Name).SequenceEqual(new[] { "Nummer", "Charge" }) &&
                  m.Unused.SequenceEqual(new[] { "Preis" }), "match both ways (copies is no unused column)");
            Check(m.Blocking(false).Select(x => x.Name).SequenceEqual(new[] { "Nummer" }) && m.Blocking(true).Count == 0,
                  "blocking: a required field without column, none when missing fields are left empty");
        }

        static void Fill()
        {
            Func<string, string> v = n => n.Equals("a", StringComparison.OrdinalIgnoreCase) ? "{date}" : n == "b" ? "x}y{gs}" : null;
            Check(DataFields.Fill("{A}|{b}|{c|def}|{c}", v, null) == "{{date}}|x}}y{{gs}}|def|{c}", "fill: escaped values, defaults, a missing field stays");
            Check(DataFields.Fill("{c}", v, n => "") == "", "fill: missing as empty");
            Check(QlLabels.Fields.Expand(DataFields.Fill("{A} {date:yyyy}", v, null), new DateTime(2026, 1, 2)) == "{date} 2026", "a value doesn't expand as a date field");
            Check(QlLabels.Fields.Expand(Code128.Expand(DataFields.Fill("{b}", v, null)), DateTime.Now) == "x}y{gs}", "a value doesn't expand as a barcode token");

            var doc = new LabelDoc();
            var t = new TextObject { Text = "", Font = "Arial", Size = 10 };
            t.SetRuns(new[] { new TextRun("Artikel "), new TextRun("{Name}", bold: true), new TextRun(" Ende") });
            doc.Objects.Add(t);
            var cross = new TextObject { Font = "Arial", Size = 10 };
            cross.SetRuns(new[] { new TextRun("{Na"), new TextRun("me}", bold: true) });
            doc.Objects.Add(cross);
            var qr = new BarcodeObject { Kind = Symbology.Qr };
            qr.SetQrType(QrType.Wifi);
            qr.SetQrField("ssid", "{Name}");
            qr.SetQrField("password", "geheim");
            doc.Objects.Add(qr);
            doc.Objects.Add(new ShapeObject());
            var filled = DataFields.Apply(doc, n => n == "Name" ? "M4;x" : null, false);
            var ft = (TextObject)filled.Objects[0];
            Check(ft.Text == "Artikel M4;x Ende" && ft.FormatRuns != null && ft.FormatRuns.Count == 3 && ft.FormatRuns[1].Text == "M4;x" && ft.FormatRuns[1].Bold == true,
                  "formatted word filled in its run: " + ft.Text);
            var fc = (TextObject)filled.Objects[1];
            Check(fc.Text == "M4;x" && fc.Runs == null, "a field across runs: filled, plain: " + fc.Text);
            var fq = (BarcodeObject)filled.Objects[2];
            Check(fq.Data.Contains("S:M4\\;x;"), "QR Wi-Fi: the value escaped by the type: " + fq.Data);
            Check(t.Text == "Artikel {Name} Ende" && ((BarcodeObject)doc.Objects[2]).QrFields["ssid"] == "{Name}", "the template is unchanged");
            Check(ReferenceEquals(filled.Objects[3], doc.Objects[3]), "objects without fields are shared");
        }

        static CsvTable Csv(string text, Encoding enc = null)
        {
            var e = enc ?? new UTF8Encoding(false);
            return CsvReader.Read(e.GetPreamble().Concat(e.GetBytes(text)).ToArray());
        }

        static void Csv()
        {
            var t = Csv("Artikel;Nummer;Preis\r\nSchraube;\"V-0;01\";1,50\r\n\r\n\"Mutter \"\"M4\"\"\";V-002\r\n\"zwei\r\nZeilen\";V-003;x;extra\r\n");
            Check(t.Delimiter == ';' && t.Columns.SequenceEqual(new[] { "Artikel", "Nummer", "Preis" }) && t.Rows.Count == 3, "CSV ; with quotes: " + t.Rows.Count);
            Check(t.Rows[0][1] == "V-0;01" && t.Rows[0][2] == "1,50" && t.Rows[1][0] == "Mutter \"M4\"" && t.Rows[2][0] == "zwei\r\nZeilen", "CSV cells");
            Check(t.Lines.SequenceEqual(new[] { 2, 4, 5 }), "CSV lines: " + string.Join(",", t.Lines));
            var r = DataRecord.FromTable(t);
            Check(r[1].Value("preis") == "" && r[1].Value("nope") == null && r[2].Value("Preis") == "x" && r[0].First == "Schraube", "records: short row empty, extra ignored, case-insensitive");
            Check(r[0].Matches("v-0;0") && r[1].Matches("2") && !r[1].Matches("3") && r[0].Matches(" "), "search: values and row number");
            Check(Csv("a,b\n1,2").Delimiter == ',' && Csv("a\tb\n1\t2").Delimiter == '\t' && Csv("a\n1").Columns.Count == 1, "delimiters , tab and none");
            Check(Csv("\"x;y\",b\n1,2").Delimiter == ',', "a delimiter inside quotes doesn't count");
            var bytes = Encoding.GetEncoding(1252).GetBytes("Größe;Maß\nÄ;ü");
            var w = CsvReader.Read(bytes);
            Check(w.Columns[0] == "Größe" && w.Rows[0][1] == "ü" && w.Encoding.CodePage == 1252, "Windows-1252");
            Check(Csv("Größe\nü").Columns[0] == "Größe" && Csv("Größe\nü", new UTF8Encoding(true)).Columns[0] == "Größe" && Csv("Größe\tx\nü\t1", Encoding.Unicode).Rows[0][0] == "ü",
                  "UTF-8 without and with BOM, UTF-16");
            Check(Csv("a;b").Rows.Count == 0, "only a header: no rows");
            bool threw = false;
            try { Csv(""); } catch (InvalidDataException) { threw = true; }
            Check(threw, "an empty file is refused");
            threw = false;
            try { Csv("a\n\"open"); } catch (InvalidDataException) { threw = true; }
            Check(threw, "an open quote is refused");
            var c = DataRecord.FromTable(Csv("Name;Copies\na;3\nb;\nc;x\nd;1000\ne;0"));
            Check(c[0].Copies == 3 && c[1].Copies == 1 && c[2].CopiesError == "x" && c[3].CopiesError == "1000" && c[4].Copies == 0 && c[0].First == "a",
                  "copies column");

            // labelname (2026-11-09): the card's title, the problems; empty = only the number; none = the first value
            var n = DataRecord.FromTable(Csv("Artikel;labelname\nSchraube;A1234\nMutter;\nScheibe"));
            Check(n[0].Title == "A1234" && n[0].Caption == Lang.F("Row {0}: {1}", 1, "A1234") && n[1].Title == "" && n[1].Caption == Lang.F("Row {0}", 2) &&
                  n[2].Name == "" && n[0].Matches("a12"), "labelname: title, empty and short row only the number, search");
            Check(DataRecord.FromTable(Csv("Artikel\nSchraube"))[0].Title == "Schraube", "no labelname column: the first value");
            Check(!FieldMatch.Of(new List<DataField>(), new[] { "Artikel", "LabelName", "copies" }).Unused.Any(u => u.Equals("labelname", StringComparison.OrdinalIgnoreCase)),
                  "labelname isn't an unused column");
            var p = new RecordProblem { Row = 3, Name = "A1234", Message = "m" };
            Check(p.ToString() == Lang.F("Row {0} ({1}): {2}", 3, "A1234", "m") && new RecordProblem { Row = 3, Message = "m" }.ToString() == Lang.F("Row {0}: {1}", 3, "m"),
                  "problem text with and without labelname");

            // JSON data files (PrintDataFile.cs): the same table as CSV
            var j = DataFile.Read(Encoding.UTF8.GetBytes("[{\"Artikel\":\"Schraube\",\"copies\":2,\"labelname\":\"A1\"},{\"Artikel\":\"Mutter\",\"EAN\":2000000000008,\"ok\":true,\"x\":null},{}]"), "liste.json");
            var jr = DataRecord.FromTable(j);
            Check(j.Columns.SequenceEqual(new[] { "Artikel", "copies", "labelname", "EAN", "ok", "x" }) && jr.Count == 3 && jr[0].Copies == 2 && jr[0].Title == "A1" &&
                  jr[1].Value("EAN") == "2000000000008" && jr[1].Value("ok") == "true" && jr[1].Value("x") == "" && jr[1].Name == "" && jr[2].Value("Artikel") == "",
                  "JSON: columns in order, numbers/true/null as text, a missing name an empty cell");
            Check(DataFile.Read(Encoding.UTF8.GetBytes("{\"a\":\"b\"}"), null).Rows.Count == 1 && DataFile.IsJson(Encoding.UTF8.GetBytes("\uFEFF ["), "daten.txt") &&
                  !DataFile.IsJson(Encoding.UTF8.GetBytes("[a]\n1"), "x.csv"), "JSON by extension or content; .csv stays CSV");
            foreach (var bad in new[] { "[1,2]", "{\"a\":{\"b\":1}}", "{\"a\":[1]}", "[{\"a\":\"\\u12\"}]", "[", "\"text\"" })
            {
                threw = false;
                try { DataFile.ReadJson(Encoding.UTF8.GetBytes(bad)); } catch (InvalidDataException) { threw = true; }
                Check(threw, "bad JSON refused as damaged: " + bad);
            }
            var many = "[{" + string.Join(",", Enumerable.Range(0, 1000).Select(k => "\"k" + k + "\":1")) + "}" + string.Concat(Enumerable.Repeat(",{}", 49999)) + "]";
            var big = DataFile.ReadJson(Encoding.UTF8.GetBytes(many));
            Check(big.Rows.Count == 50000 && big.Rows[1].Length == 0, "JSON: empty objects stay small rows");
        }

        static void Job()
        {
            var doc = new LabelDoc();
            doc.Objects.Add(new TextObject { Text = "{Artikel}", Font = "Arial", Size = 12, X = 3, Y = 3, W = 40, H = 8 });
            doc.Objects.Add(new TextObject { Text = "{Lager}", Font = "Arial", Size = 8, X = 3, Y = 15, W = 40, H = 6 });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Ean13, Data = "{EAN|2000000000008}", X = 3, Y = 30, W = 40, H = 15 });
            var label = new LabelChoice { Name = "Test 62", Media = MediaTable.ById(259), LengthMm = 50, FeedMm = 3, IsFormat = true };
            var job = new LabelJob(doc, LabelLayout.Estimate(label, false, false)) { Copies = 2 };
            job.Records = DataRecord.FromTable(Csv("Artikel;EAN;copies\nSchraube;2000000000008;3\nMutter;123;\nx;;y"));
            var p0 = job.Check(job.Records[0]);
            Check(p0.Count == 1 && p0[0].Kind == RecordProblemKind.MissingField && p0[0].Field == "Lager" && p0[0].Blocks, "missing field blocks: " + string.Join("; ", p0));
            var p1 = job.Check(job.Records[1]);
            Check(p1.Any(p => p.Kind == RecordProblemKind.Barcode && !p.Blocks), "a bad EAN is a warning: " + string.Join("; ", p1));
            var p2 = job.Check(job.Records[2]);
            Check(p2.Any(p => p.Kind == RecordProblemKind.Copies && p.Blocks) && !p2.Any(p => p.Kind == RecordProblemKind.Barcode), "bad copies blocks; an empty EAN cell takes the default: " + string.Join("; ", p2));
            job.MissingEmpty = true;
            Check(job.Check(job.Records[0]).Count == 0, "leave missing fields empty");
            Check(job.CopiesOf(job.Records[0]) == 6 && job.CopiesOf(job.Records[1]) == 2 && job.CopiesOf(job.Records[2]) == 0, "copies multiply");
            // the dialog wholly inside its screen's working area (dialog review P4)
            var area = new Rectangle(0, 0, 1366, 728);
            Check(PrintDataDialog.Inside(new Point(1000, 500), new Size(780, 660), area) == new Point(586, 68) &&
                  PrintDataDialog.Inside(new Point(-50, -20), new Size(780, 660), area) == new Point(0, 0) &&
                  PrintDataDialog.Inside(new Point(100, 30), new Size(780, 660), area) == new Point(100, 30) &&
                  PrintDataDialog.Inside(new Point(300, 300), new Size(1500, 800), area) == new Point(0, 0), "dialog kept inside the working area");
            // a row with copies 0 is skipped: not checked (its missing field doesn't block); a bad copies cell still is
            var skip = new LabelJob(doc, job.Layout);
            skip.Records = DataRecord.FromTable(Csv("Artikel;copies\nSchraube;1\nWeg;0\nMutter;x"));   // no {Lager} column: each row would block
            var checkedRows = skip.ToCheck(skip.Records);
            Check(checkedRows.Select(r => r.Row).SequenceEqual(new[] { 1, 3 }) && skip.Check(skip.Records[1]).Any(p => p.Blocks),
                  "copies 0: not checked, though it would block; copies x: checked: " + string.Join(",", checkedRows.Select(r => r.Row)));
            using (var a = job.Render(job.Records[0]))
            using (var b = job.Render(job.Records[1]))
            {
                Check(a.Width == b.Width && a.Height == b.Height && Ink(a) > 0 && Ink(a) != Ink(b), "rendered per record");
            }
        }

        // Security review 2026-11-09: bad or huge input gives a clear error in little time, never a hang or OOM.
        static void Limits()
        {
            Func<string, string> error = text =>
            {
                try { Csv(text); return null; }
                catch (InvalidDataException e) { return e.Message; }
            };
            Check(error("a;b\n1\u00002") != null, "NUL characters are refused");
            string open = error("a\nx\n\"open\nmore") ?? "";
            Check(open.StartsWith("Line 3:"), "an open quote names its line: " + open);
            Check((error("a\n" + new string('x', CsvReader.MaxCellChars + 5)) ?? "").StartsWith("Line 2:"), "a too long value");
            Check((error(string.Join(";", Enumerable.Range(0, CsvReader.MaxColumns + 1)) + "\n1") ?? "").StartsWith("Line 1:"), "too many columns");
            Check(error("a\n" + string.Concat(Enumerable.Repeat("1\n", CsvReader.MaxRows + 1))) != null &&
                  error("a\n" + string.Concat(Enumerable.Repeat("1\n", CsvReader.MaxRows))) == null, "too many rows");
            bool big = false;
            try { CsvReader.Read(new byte[CsvReader.MaxBytes + 1]); } catch (InvalidDataException) { big = true; }
            Check(big, "too many bytes");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Check(!DataFields.In("{" + new string('a', 200000) + " }").Any() && !DataFields.In("{a|" + new string('b', 200000)).Any() &&
                  DataFields.In("{" + new string('a', 64) + "}").Count() == 1 && !DataFields.In("{" + new string('a', 65) + "}").Any(),
                  "field names up to 64 characters");
            Check(DataFields.Fill(string.Concat(Enumerable.Repeat("{a ", 50000)), n => "x", null).Length > 0, "many open braces");
            Check(sw.ElapsedMilliseconds < 2000, "field regex in linear time: " + sw.ElapsedMilliseconds + " ms");
            var label = new LabelChoice { Name = "Test 62", Media = MediaTable.ById(259), LengthMm = 50, FeedMm = 3, IsFormat = true };
            var job = new LabelJob(new LabelDoc(), LabelLayout.Estimate(label, false, false)) { Copies = int.MaxValue };
            job.Records = DataRecord.FromTable(Csv("a;copies\nx;999\ny;999"));
            Check(job.CopiesOf(job.Records[0]) == 999 * 999 && job.PageCount(job.Records) > LabelJob.MaxPages, "copies within range, page count without overflow");
            bool refused = false;
            try { job.Print(job.Records, "test"); } catch (InvalidDataException) { refused = true; }
            Check(refused, "a job over the page limit is refused before anything is sent");
        }

        static int Ink(System.Drawing.Bitmap b)
        {
            int n = 0;
            for (int y = 0; y < b.Height; y += 2) for (int x = 0; x < b.Width; x += 2) if ((b.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0xFFFFFF) n++;
            return n;
        }
    }
}
