// Print from data, the data file: CSV (PrintData.cs, CsvReader) or JSON, both read
// into the same CsvTable, so the dialog, QlLabelPrint.exe and later the web port and MCP treat them alike.
// JSON, kept simple on purpose: one object {"Name":"Value", "copies":2} or a list of them; each value a text, a number,
// true/false or null (empty); no nested objects or lists. A name is a column (first seen first); a name missing in a row
// is an empty cell, as a short CSV row. The CSV limits apply (size, rows, columns, characters per value).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace QlLabels
{
    static class DataFile
    {
        // A file chosen in the dialog or given with --data: JSON by its extension .json, or by its content ({ or [ first)
        // unless it is a .csv; else CSV.
        public static CsvTable Load(string path)
        {
            var info = new FileInfo(path);
            if (info.Length > CsvReader.MaxBytes) throw new InvalidDataException(Lang.F("The file is larger than {0} MB.", CsvReader.MaxBytes / (1024 * 1024)));
            return Read(File.ReadAllBytes(path), path);
        }

        // name: the file's name, or null/"-" for stdin (then the content decides)
        public static CsvTable Read(byte[] bytes, string name)
        {
            if (!IsJson(bytes, name)) return CsvReader.Read(bytes);
            var t = ReadJson(bytes);
            t.Json = true;
            return t;
        }

        public static bool IsJson(byte[] bytes, string name)
        {
            string ext = name == null || name == "-" ? "" : Path.GetExtension(name);
            if (string.Equals(ext, ".json", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(ext, ".csv", StringComparison.OrdinalIgnoreCase)) return false;
            return LooksLikeJson(bytes);
        }

        static bool LooksLikeJson(byte[] bytes)
        {
            int n = Math.Min(bytes.Length, 4096);
            string s = Encoding.UTF8.GetString(bytes, 0, n).TrimStart('﻿', ' ', '\t', '\r', '\n');
            return s.StartsWith("{") || s.StartsWith("[");
        }

        public static CsvTable ReadJson(byte[] bytes)
        {
            if (bytes.Length > CsvReader.MaxBytes) throw new InvalidDataException(Lang.F("The file is larger than {0} MB.", CsvReader.MaxBytes / (1024 * 1024)));
            object root;
            try { root = new JavaScriptSerializer { MaxJsonLength = CsvReader.MaxBytes }.DeserializeObject(Encoding.UTF8.GetString(bytes).TrimStart('﻿')); }
            catch (ArgumentException e) { throw new InvalidDataException(Lang.F("The JSON data can't be read: {0}", e.Message)); }
            catch (InvalidOperationException e) { throw new InvalidDataException(Lang.F("The JSON data can't be read: {0}", e.Message)); }
            catch (FormatException e) { throw new InvalidDataException(Lang.F("The JSON data can't be read: {0}", e.Message)); }
            var items = root is Dictionary<string, object> ? new object[] { root } : root as object[];
            if (items == null) throw new InvalidDataException(Lang.T("The JSON data must be an object or a list of objects."));
            if (items.Length > CsvReader.MaxRows) throw new InvalidDataException(Lang.F("The file has more than {0} rows.", CsvReader.MaxRows));

            var table = new CsvTable { Encoding = Encoding.UTF8 };
            var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // the names over all rows, like CSV columns
            var rows = new List<Dictionary<int, string>>();
            for (int i = 0; i < items.Length; i++)
            {
                var obj = items[i] as Dictionary<string, object>;
                if (obj == null) throw new InvalidDataException(Lang.F("Row {0}: {1}", i + 1, Lang.T("not an object")));
                if (obj.Count > CsvReader.MaxColumns) throw new InvalidDataException(Lang.F("Line {0}: more than {1} columns.", i + 1, CsvReader.MaxColumns));
                var cells = new Dictionary<int, string>();
                foreach (var kv in obj)
                {
                    if (kv.Value is Dictionary<string, object> || kv.Value is object[])
                        throw new InvalidDataException(Lang.F("Row {0}: {1}", i + 1, Lang.F("the value of {0} is not a text or number.", kv.Key)));
                    string v = kv.Value == null ? "" : kv.Value is bool ? ((bool)kv.Value ? "true" : "false") : Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    if (v.Length > CsvReader.MaxCellChars || v.IndexOf('\0') >= 0)
                        throw new InvalidDataException(Lang.F("Row {0}: {1}", i + 1, Lang.F("the value of {0} is not a text or number.", kv.Key)));
                    int c;
                    if (!index.TryGetValue(kv.Key, out c))
                    {
                        if (index.Count >= CsvReader.MaxColumns)
                            throw new InvalidDataException(Lang.F("The JSON data has more than {0} different names.", CsvReader.MaxColumns));
                        c = index.Count;
                        index[kv.Key] = c;
                        table.Columns.Add(kv.Key);
                    }
                    if (!cells.ContainsKey(c)) cells[c] = v;   // "Name" and "name" in one object: the first
                }
                rows.Add(cells);
            }
            for (int i = 0; i < rows.Count; i++)
            {
                int length = 0;   // only up to the row's last name: 50,000 empty objects after one with 1,000 names stay small
                foreach (int c in rows[i].Keys) length = Math.Max(length, c + 1);
                var row = new string[length];
                for (int c = 0; c < row.Length; c++)
                {
                    string v;
                    row[c] = rows[i].TryGetValue(c, out v) ? v : "";
                }
                table.Rows.Add(row);
                table.Lines.Add(i + 1);   // for JSON: the object's number in the list
            }
            return table;
        }
    }
}
