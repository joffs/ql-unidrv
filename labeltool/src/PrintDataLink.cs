// A label's data file for Print with data (2026-11-11): stored in the .qllabel as "printData": {"file": the path relative
// to the label's folder (when there is one: same drive or share), "absolute": the full path}. Opening Print with data
// loads it at once (PrintDataDialog), QlLabelPrint uses it without --data. Looked for relative first (the label and its
// data moved together), then at the full path. Immutable: a change is a new link, which marks the label changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QlLabels
{
    enum LinkState { Found, NotFound, FolderOffline, Unreadable }

    sealed class PrintDataLink
    {
        public readonly string File;       // relative to the label's folder, or null
        public readonly string Absolute;   // the full path
        public readonly bool Saved;        // as in the label's file (else: written with the next Save)

        PrintDataLink(string file, string absolute, bool saved) { File = file; Absolute = absolute; Saved = saved; }

        // A data file chosen for a label (labelPath null: not saved yet, the relative path follows on Save).
        public static PrintDataLink For(string dataPath, string labelPath, bool saved = false)
        {
            string full = Path.GetFullPath(dataPath);
            return new PrintDataLink(labelPath != null ? Relative(Path.GetDirectoryName(Path.GetFullPath(labelPath)), full) : null, full, saved);
        }

        // As written to a label saved at labelPath: the relative path for that folder (Save as elsewhere: a new one).
        public PrintDataLink SavedAt(string labelPath, string found = null) { return For(found ?? Absolute, labelPath, true); }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>();
            if (File != null) d["file"] = File;
            d["absolute"] = Absolute;
            return d;
        }

        // From the file's "printData"; null when it has no usable path.
        public static PrintDataLink FromJson(object value)
        {
            var d = value as Dictionary<string, object>;
            if (d == null) return null;
            object v;
            string file = d.TryGetValue("file", out v) ? v as string : null;
            string absolute = d.TryGetValue("absolute", out v) ? v as string : null;
            if (string.IsNullOrWhiteSpace(file) || !Usable(file) || Path.IsPathRooted(file)) file = null;
            if (string.IsNullOrWhiteSpace(absolute) || !Usable(absolute) || !Path.IsPathRooted(absolute)) absolute = null;
            if (absolute == null && file == null) return null;
            return new PrintDataLink(file, absolute ?? file, true);
        }

        static bool Usable(string p) { return p.IndexOfAny(Path.GetInvalidPathChars()) < 0 && p.IndexOf('*') < 0 && p.IndexOf('?') < 0; }

        // The places to look, in order: next to the label (relative), then the full path.
        public List<string> Candidates(string labelPath)
        {
            var list = new List<string>();
            try
            {
                if (File != null && labelPath != null) list.Add(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(labelPath)), File)));
            }
            catch (Exception) { }
            try { if (Path.IsPathRooted(Absolute)) list.Add(Path.GetFullPath(Absolute)); } catch (Exception) { }
            return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // The file to use, or null with why (a network folder may take a while: call it off the window's thread).
        public string Resolve(string labelPath, out LinkState state) { return Resolve(labelPath, out state, System.IO.File.Exists, Directory.Exists); }

        internal string Resolve(string labelPath, out LinkState state, Func<string, bool> fileExists, Func<string, bool> folderExists)
        {
            var places = Candidates(labelPath);
            foreach (var p in places)
                if (fileExists(p)) { state = LinkState.Found; return p; }
            // not there: is the full path's drive or share gone (offline, not connected), or only the file?
            state = LinkState.NotFound;
            string full = places.Count > 0 ? places[places.Count - 1] : null;
            try
            {
                string root = full != null ? Path.GetPathRoot(full) : null;
                if (!string.IsNullOrEmpty(root) && !folderExists(root)) state = LinkState.FolderOffline;
            }
            catch (Exception) { }
            return null;
        }

        // The path of 'full' from 'folder' ("Daten\teile.csv", "..\Daten\teile.csv"); null on another drive or share.
        internal static string Relative(string folder, string full)
        {
            if (folder == null) return null;
            string root = Path.GetPathRoot(folder), other = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || !string.Equals(root.TrimEnd('\\', '/'), other.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return null;
            var a = folder.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            var b = full.Substring(other.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int same = 0;
            while (same < a.Length && same < b.Length - 1 && string.Equals(a[same], b[same], StringComparison.OrdinalIgnoreCase)) same++;
            var parts = Enumerable.Repeat("..", a.Length - same).Concat(b.Skip(same));
            return string.Join("\\", parts);
        }

        public bool SameFile(string path)
        {
            try { return path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(Absolute), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        // ---------- the file's "printData" alone (2026-11-11: remembered without the asterisk) ----------

        // The label file's text with "printData" set to this link (null: removed), everything else byte for byte as it
        // was; null when the text isn't one JSON object.
        public static string WithLink(string json, PrintDataLink link, string labelPath)
        {
            int open, close, start, end;
            if (!TopLevel(json, out open, out close)) return null;
            string value = link == null ? null
                : new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(link.SavedAt(labelPath).ToJson());
            if (FindKey(json, open, close, Key, out start, out end))
            {
                if (value != null) return json.Substring(0, start) + "\"" + Key + "\":" + value + json.Substring(end);
                // removed with its comma (the one before it, else the one after it)
                int a = start, b = end;
                int before = start - 1;
                while (before > open && char.IsWhiteSpace(json[before])) before--;
                if (json[before] == ',') a = before;
                else
                {
                    int after = end;
                    while (after < close && char.IsWhiteSpace(json[after])) after++;
                    if (json[after] == ',') b = after + 1;
                }
                return json.Substring(0, a) + json.Substring(b);
            }
            if (value == null) return json;
            bool empty = json.Substring(open + 1, close - open - 1).Trim().Length == 0;
            return json.Substring(0, close) + (empty ? "" : ",") + "\"" + Key + "\":" + value + json.Substring(close);
        }

        const string Key = "printData";

        // The text without "printData": two label files that differ only there are the same label.
        public static string WithoutLink(string json) { return WithLink(json, null, null); }

        // The outer object's braces.
        static bool TopLevel(string s, out int open, out int close)
        {
            open = 0;
            close = s.Length - 1;
            while (open < s.Length && char.IsWhiteSpace(s[open])) open++;
            while (close > open && char.IsWhiteSpace(s[close])) close--;
            return open < close && s[open] == '{' && s[close] == '}' && SkipValue(s, open) == close + 1;
        }

        // A key of the outer object: from its opening quote to the end of its value.
        static bool FindKey(string s, int open, int close, string key, out int start, out int end)
        {
            start = end = -1;
            int i = open + 1;
            while (true)
            {
                while (i < close && char.IsWhiteSpace(s[i])) i++;
                if (i >= close || s[i] != '"') return false;
                int k = i, afterKey = SkipValue(s, i);
                if (afterKey < 0) return false;
                string name = s.Substring(k + 1, afterKey - k - 2);
                i = afterKey;
                while (i < close && char.IsWhiteSpace(s[i])) i++;
                if (i >= close || s[i] != ':') return false;
                i++;
                while (i < close && char.IsWhiteSpace(s[i])) i++;
                int valueEnd = SkipValue(s, i);
                if (valueEnd < 0) return false;
                if (name == key) { start = k; end = valueEnd; return true; }
                i = valueEnd;
                while (i < close && char.IsWhiteSpace(s[i])) i++;
                if (i < close && s[i] == ',') { i++; continue; }
                return false;
            }
        }

        // The index after the JSON value at i (a string, an object or list, or a literal); -1 when it is damaged.
        static int SkipValue(string s, int i)
        {
            if (i >= s.Length) return -1;
            if (s[i] == '"')
            {
                for (int j = i + 1; j < s.Length; j++)
                {
                    if (s[j] == '\\') j++;
                    else if (s[j] == '"') return j + 1;
                }
                return -1;
            }
            if (s[i] == '{' || s[i] == '[')
            {
                int depth = 0;
                for (int j = i; j < s.Length; j++)
                {
                    char c = s[j];
                    if (c == '"') { j = SkipValue(s, j) - 1; if (j < 0) return -1; }
                    else if (c == '{' || c == '[') depth++;
                    else if (c == '}' || c == ']') { if (--depth == 0) return j + 1; }
                }
                return -1;
            }
            int e = i;
            while (e < s.Length && s[e] != ',' && s[e] != '}' && s[e] != ']' && !char.IsWhiteSpace(s[e])) e++;
            return e > i ? e : -1;
        }
    }
}
