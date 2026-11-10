// Print from data: data fields {Name} and {Name|default} in a label, the CSV reader
// and the records. One engine for QL Label Maker's "Print with data" dialog, QlLabelPrint.exe and later the web port
// and MCP. LabelJob (PrintJob.cs) fills, checks, renders and prints.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QlLabels
{
    // A data field as the label uses it. Default: the text after "|" ({Name|default}); null when an occurrence has
    // none. Required: at least one occurrence has no default, so a record must give a value (or "leave missing empty").
    sealed class DataField
    {
        public string Name;
        public string Default;
        public bool Required;
        public override string ToString() { return "{" + Name + (Default != null ? "|" + Default : "") + "}"; }
    }

    static class DataFields
    {
        // {Name} or {Name|default}; {{ and }} are literal braces (as in Fields). A name: letters, digits, _, - and
        // spaces inside; date, time and the barcode tokens ({gs}, {x1D} ...) are reserved and stay as they are.
        // Bounded lengths (name ≤ 64, default ≤ 1000 characters): linear time on any text.
        static readonly Regex Field = new Regex(@"\{\{|\}\}|\{([\p{L}\p{N}_](?:[\p{L}\p{N}_ \-]{0,62}[\p{L}\p{N}_])?)(?:\|([^{}]{0,1000}))?\}");
        static readonly Regex Reserved = new Regex(@"^(date|time|tab|cr|lf|gs|rs|eot|fnc1|x[0-9a-f]{2})$", RegexOptions.IgnoreCase);

        public static bool IsReserved(string name) { return Reserved.IsMatch(name ?? ""); }

        // The data fields in a text, in order (each occurrence).
        public static IEnumerable<DataField> In(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) yield break;
            foreach (Match m in Field.Matches(text))
            {
                if (!m.Groups[1].Success || IsReserved(m.Groups[1].Value)) continue;
                yield return new DataField { Name = m.Groups[1].Value, Default = m.Groups[2].Success ? m.Groups[2].Value : null, Required = !m.Groups[2].Success };
            }
        }

        // The label's data fields, one per name (case-insensitive, the first spelling), in the order they appear: texts,
        // barcode contents and a QR type's fields. Default: the first one given.
        public static List<DataField> Of(LabelDoc doc)
        {
            var list = new List<DataField>();
            foreach (var text in Texts(doc))
                foreach (var f in In(text))
                {
                    var had = list.FirstOrDefault(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (had == null) { list.Add(f); continue; }
                    had.Required |= f.Required;
                    if (had.Default == null) had.Default = f.Default;
                }
            return list;
        }

        static IEnumerable<string> Texts(LabelDoc doc)
        {
            foreach (var o in doc.Objects)
            {
                var t = o as TextObject;
                if (t != null) { yield return t.Text; continue; }
                var b = o as BarcodeObject;
                if (b == null) continue;
                if (b.TypedQr) foreach (var v in b.QrFields.Values) yield return v;
                else yield return b.Data;
            }
        }

        // The text with its data fields filled. value(name): the record's value, or null when the record has none; for
        // null or an empty cell the occurrence's default, else missing(name) (null: the field stays as typed, e.g. "{Lagerort}"). A value never
        // expands again: its braces are doubled, so the date/time fields and the barcode tokens leave them as they are.
        public static string Fill(string text, Func<string, string> value, Func<string, string> missing)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            return Field.Replace(text, m =>
            {
                if (!m.Groups[1].Success || IsReserved(m.Groups[1].Value)) return m.Value;
                string v = value(m.Groups[1].Value);
                if (string.IsNullOrEmpty(v) && m.Groups[2].Success) v = m.Groups[2].Value;   // an empty cell takes the default too
                if (v == null) v = missing != null ? missing(m.Groups[1].Value) : null;
                return v == null ? m.Value : Escape(v);
            });
        }

        public static string Escape(string value) { return (value ?? "").Replace("{", "{{").Replace("}", "}}"); }

        // A copy of the label with the record's values in its texts, barcodes and QR fields (pictures, lines and shapes
        // are shared, not copied). missingEmpty: a field without value and without default prints as empty text; else it
        // stays as typed ({Lagerort}) and LabelJob.Check reports it.
        public static LabelDoc Apply(LabelDoc template, Func<string, string> value, bool missingEmpty)
        {
            Func<string, string> missing = missingEmpty ? (Func<string, string>)(n => "") : null;
            var doc = (LabelDoc)template.Clone();
            doc.Objects = template.Objects.Select(o => FillObject(o, value, missing)).ToList();
            return doc;
        }

        static LabelObject FillObject(LabelObject o, Func<string, string> value, Func<string, string> missing)
        {
            var t = o as TextObject;
            if (t != null)
            {
                if (!In(t.Text).Any()) return o;
                var c = (TextObject)t.Clone();
                var runs = t.FormatRuns;
                // Words with their own format: each run filled on its own, as long as no field crosses a run's border.
                if (runs != null && runs.Sum(r => In(r.Text).Count()) == In(t.Text).Count())
                {
                    var filled = runs.Select(r => r.WithText(Fill(r.Text, value, missing))).ToList();
                    c.Text = string.Concat(filled.Select(r => r.Text));
                    c.SetRuns(filled);
                }
                else
                {
                    c.Text = Fill(t.Text, value, missing);
                    c.Runs = null;
                }
                return c;
            }
            var b = o as BarcodeObject;
            if (b == null) return o;
            if (b.TypedQr)
            {
                if (!b.QrFields.Values.Any(v => In(v).Any())) return o;
                var q = (BarcodeObject)b.Clone();
                q.QrFields = b.QrFields.ToDictionary(kv => kv.Key, kv => Fill(kv.Value, value, missing));
                q.Data = QrContent.Build(q.QrType, q.QrFields);
                return q;
            }
            if (!In(b.Data).Any()) return o;
            var d = (BarcodeObject)b.Clone();
            d.Data = Fill(b.Data, value, missing);
            return d;
        }
    }

    // How the label's fields and the data's columns match, both ways (the dialog's line, the CLI's --fields check).
    sealed class FieldMatch
    {
        public List<DataField> Found = new List<DataField>();     // a column of the same name (case-insensitive)
        public List<DataField> Missing = new List<DataField>();   // no column; a default or "leave empty" fills it
        public List<string> Unused = new List<string>();          // columns the label doesn't use (info only)

        // Fields that stop printing: no column, required (an occurrence without default), and not "leave empty".
        public List<DataField> Blocking(bool missingEmpty) { return missingEmpty ? new List<DataField>() : Missing.Where(f => f.Required).ToList(); }

        public static FieldMatch Of(IList<DataField> fields, IEnumerable<string> columns)
        {
            var cols = columns.Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            var m = new FieldMatch();
            foreach (var f in fields)
                (cols.Any(c => string.Equals(c, f.Name, StringComparison.OrdinalIgnoreCase)) ? m.Found : m.Missing).Add(f);
            m.Unused = cols.Where(c => !string.Equals(c, DataRecord.CopiesColumn, StringComparison.OrdinalIgnoreCase) &&
                                       !string.Equals(c, DataRecord.NameColumn, StringComparison.OrdinalIgnoreCase) &&
                                       !fields.Any(f => string.Equals(f.Name, c, StringComparison.OrdinalIgnoreCase))).ToList();
            return m;
        }
    }

    // One row of data: its values by column name (case-insensitive) and how many labels of it.
    sealed class DataRecord
    {
        public const string CopiesColumn = "copies";
        // The label's name in the preview, the search, the problems and QlLabelPrint --name (2026-11-09, as named
        // pages in CorelDRAW). Optional; a label may use it as a field {labelname} too. Empty: only the row number.
        public const string NameColumn = "labelname";
        public const int MaxCopies = 999;

        public int Row;        // 1 = the first data row (the header not counted)
        public int Line;       // the line in the file where the row starts (1-based; the header is line 1)
        public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public int Copies = 1; // from the "copies" column (0: not printed); the dialog's/CLI's copies multiply
        public string CopiesError;   // the copies column holds no number 0-999 (the raw text)
        public string First = "";    // the first non-empty value, the card's title when the data has no labelname

        // The labelname cell (trimmed); null when the data has no such column.
        public string Name { get { string v = Value(NameColumn); return v == null ? null : v.Trim(); } }

        // The card's title after the row number: the labelname (empty: none), else the first value.
        public string Title { get { return Name ?? First; } }

        // "Row 15" or "Row 15: A1234" (the dialog's preview window, the problem list)
        public string Caption { get { string t = Title; return t.Length == 0 ? Lang.F("Row {0}", Row) : Lang.F("Row {0}: {1}", Row, t); } }

        // This row with its own number of labels (the large preview's Print: its copies box, not the copies column).
        public DataRecord WithCopies(int copies)
        {
            return new DataRecord { Row = Row, Line = Line, Values = Values, Copies = copies, First = First };
        }

        public string Value(string name)
        {
            string v;
            return Values.TryGetValue(name, out v) ? v : null;
        }

        // Search: any value contains the text (case-insensitive), or the row number is the text.
        public bool Matches(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            text = text.Trim();
            return Row.ToString(CultureInfo.InvariantCulture) == text ||
                   Values.Values.Any(v => v != null && v.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0);
        }

        public static List<DataRecord> FromTable(CsvTable table)
        {
            var list = new List<DataRecord>();
            for (int i = 0; i < table.Rows.Count; i++)
            {
                var row = table.Rows[i];
                var r = new DataRecord { Row = i + 1, Line = table.Lines[i] };
                for (int c = 0; c < table.Columns.Count; c++)
                {
                    string name = table.Columns[c];
                    if (name.Length == 0 || r.Values.ContainsKey(name)) continue;   // an unnamed or repeated column: the first one
                    r.Values[name] = c < row.Length ? row[c] : "";
                }
                r.First = table.Columns.Select((n, c) => c < row.Length && n.Length > 0 && !string.Equals(n, CopiesColumn, StringComparison.OrdinalIgnoreCase) ? row[c].Trim() : "")
                                       .FirstOrDefault(v => v.Length > 0) ?? "";
                string copies = r.Value(CopiesColumn);
                if (copies != null && copies.Trim().Length > 0)
                {
                    int n;
                    if (int.TryParse(copies.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out n) && n <= MaxCopies) r.Copies = n;
                    else r.CopiesError = copies.Trim();
                }
                list.Add(r);
            }
            return list;
        }
    }

    // A CSV file as read: column names (the header row, trimmed) and the data rows (the cells as they are).
    sealed class CsvTable
    {
        public List<string> Columns = new List<string>();
        public List<string[]> Rows = new List<string[]>();
        public List<int> Lines = new List<int>();   // the file line where each row starts
        public char Delimiter = ',';
        public Encoding Encoding;
        public bool Json;   // read from JSON (the dialog says "entries" instead of "rows")
    }

    // CSV as Excel and other programs write it: the first row names the columns; delimiter ; , or tab, found in the
    // header row (outside quotes); UTF-8 with or without BOM, UTF-16 with BOM, else Windows-1252; quotes as RFC 4180
    // ("" inside quotes, line breaks inside quotes); empty lines are skipped. Errors: InvalidDataException with the line.
    static class CsvReader
    {
        // Limits (security review 2026-11-09): what a label job can use, far above real lists; beyond them a clear error
        // instead of minutes of work or running out of memory.
        public const int MaxBytes = 20 * 1024 * 1024;
        public const int MaxRows = 50000, MaxColumns = 1000, MaxCellChars = 10000;

        public static CsvTable Load(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > MaxBytes) throw new InvalidDataException(Lang.F("The file is larger than {0} MB.", MaxBytes / (1024 * 1024)));
            return Read(File.ReadAllBytes(path));
        }

        public static CsvTable Read(byte[] bytes)
        {
            if (bytes.Length > MaxBytes) throw new InvalidDataException(Lang.F("The file is larger than {0} MB.", MaxBytes / (1024 * 1024)));
            Encoding enc;
            string text = Decode(bytes, out enc);
            // NUL characters: not a text file (or UTF-16 without its BOM); refused rather than read as rubbish
            if (text.IndexOf('\0') >= 0) throw new InvalidDataException(Lang.T("This is not a CSV text file (it contains NUL characters)."));
            var table = new CsvTable { Encoding = enc, Delimiter = DelimiterOf(text) };
            int line = 1;
            bool header = true;
            foreach (var row in Records(text, table.Delimiter))
            {
                int at = line;
                line += row.Value;
                var cells = row.Key;
                if (cells.Count == 1 && cells[0].Length == 0) continue;   // an empty line
                if (cells.Count > MaxColumns) throw new InvalidDataException(Lang.F("Line {0}: more than {1} columns.", at, MaxColumns));
                if (!header && table.Rows.Count >= MaxRows) throw new InvalidDataException(Lang.F("The file has more than {0} rows.", MaxRows));
                if (header)
                {
                    table.Columns = cells.Select(c => c.Trim()).ToList();
                    header = false;
                    continue;
                }
                table.Rows.Add(cells.ToArray());
                table.Lines.Add(at);
            }
            if (header) throw new InvalidDataException(Lang.T("The file is empty."));
            return table;
        }

        static string Decode(byte[] b, out Encoding enc)
        {
            if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) { enc = new UTF8Encoding(true); return Encoding.UTF8.GetString(b, 3, b.Length - 3); }
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { enc = Encoding.Unicode; return Encoding.Unicode.GetString(b, 2, b.Length - 2); }
            if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { enc = Encoding.BigEndianUnicode; return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); }
            try
            {
                var strict = new UTF8Encoding(false, true);
                string s = strict.GetString(b);
                enc = strict;
                return s;
            }
            catch (DecoderFallbackException)
            {
                enc = Encoding.GetEncoding(1252);
                return enc.GetString(b);
            }
        }

        // The delimiter that occurs most often in the header row outside quotes (; before , before tab on a tie); none: ,
        static char DelimiterOf(string text)
        {
            int semi = 0, comma = 0, tab = 0;
            bool quoted = false;
            foreach (char c in text)
            {
                if (c == '"') quoted = !quoted;
                else if (!quoted && (c == '\n' || c == '\r')) break;
                else if (!quoted && c == ';') semi++;
                else if (!quoted && c == ',') comma++;
                else if (!quoted && c == '\t') tab++;
            }
            if (semi == 0 && comma == 0 && tab == 0) return ',';
            return semi >= comma && semi >= tab ? ';' : comma >= tab ? ',' : '\t';
        }

        // The rows with the number of file lines each takes (quoted line breaks count).
        static IEnumerable<KeyValuePair<List<string>, int>> Records(string text, char delimiter)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            int i = 0, lines = 1, line = 1, quoteLine = 0;
            bool quoted = false, any = false, wasQuoted = false;
            while (i < text.Length)
            {
                char c = text[i];
                if (cell.Length > MaxCellChars) throw new InvalidDataException(Lang.F("Line {0}: a value is longer than {1} characters.", line, MaxCellChars));
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                        quoted = false; i++; continue;
                    }
                    if (c == '\n' || c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')) lines++;
                    cell.Append(c); i++; continue;
                }
                if (c == '"' && cell.Length == 0 && !wasQuoted) { quoted = wasQuoted = any = true; quoteLine = line + lines - 1; i++; continue; }
                if (c == delimiter) { cells.Add(cell.ToString()); cell.Clear(); wasQuoted = false; any = true; i++; continue; }
                if (c == '\r' || c == '\n')
                {
                    if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    i++;
                    cells.Add(cell.ToString());
                    yield return new KeyValuePair<List<string>, int>(cells, lines);
                    line += lines;
                    cells = new List<string>(); cell.Clear(); wasQuoted = false; any = false; lines = 1;
                    continue;
                }
                cell.Append(c); any = true; i++;
            }
            if (quoted) throw new InvalidDataException(Lang.F("Line {0}: a quoted value is not closed (a \" is missing).", quoteLine));
            if (any || cell.Length > 0 || cells.Count > 0)
            {
                cells.Add(cell.ToString());
                yield return new KeyValuePair<List<string>, int>(cells, lines);
            }
        }
    }
}
