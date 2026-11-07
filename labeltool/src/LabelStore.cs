// Label formats: model, files (labels.json / labels.tsv), geometry and validation.
//
// C:\ProgramData\QLUnidrv\labels.json  master list (written by the elevated helper only):
//   {"version": "2.00", "formats": [{key, name, paper_id, length_mm, feed_mm}], "owned_forms": [names]}
// C:\ProgramData\QLUnidrv\labels.tsv   the same for the render plug-in (driver/src/labels.h):
//   <form name> TAB <paper id> TAB <length 0.1 mm> TAB <feed dots>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace QlLabels
{
    sealed class LabelFormat
    {
        public int Key;          // stable id (informational since labels v2)
        public string Name = "";
        public int PaperId;
        public double LengthMm;  // continuous: label length; die-cut: the paper's length
        public double FeedMm;    // continuous: feed margin top and bottom; die-cut: the paper's offset

        public LabelFormat Clone() { return (LabelFormat)MemberwiseClone(); }

        public bool SameAs(LabelFormat o)
        {
            return o != null && Name == o.Name && PaperId == o.PaperId && Math.Abs(LengthMm - o.LengthMm) < 0.005 &&
                   Math.Abs(FeedMm - o.FeedMm) < 0.005;
        }
    }

    sealed class LabelSet
    {
        public List<LabelFormat> Formats = new List<LabelFormat>();
        public List<string> OwnedForms = new List<string>();  // forms this tool created (never touch others)
        public bool HasOwnedList;                             // false for the seed file (no owned_forms)
    }

    static class LabelStore
    {
        public const int MaxNameLength = 31;  // DEVMODE dmFormName: 32 characters including NUL
        public const double MaxFeedMm = 8.3;

        public static readonly string DataDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "QLUnidrv");
        public static string JsonPath { get { return Path.Combine(DataDir, "labels.json"); } }
        public static string TsvPath { get { return Path.Combine(DataDir, "labels.tsv"); } }

        // Brother's factory list as the seed for a new PC, next to the exe (development runs: the repository's labels\):
        // German (default-labels.json) for a German Windows display language, else English with Brother's US names in
        // inches (default-labels-en-us.json) when Windows measures in US units, otherwise the UK names in millimetres
        // (default-labels-en.json). Chosen once, when there is no labels.json yet (--sync); the names are the user's then.
        public static string SeedName(string language, bool metric)
        {
            return language == "de" ? "default-labels.json" : metric ? "default-labels-en.json" : "default-labels-en-us.json";
        }

        // The seed file for a language on this PC (Windows' measurement system), or null.
        public static string FindSeed(string language) { return FindSeedFile(SeedName(language, RegionInfo.CurrentRegion.IsMetric)); }

        public static string FindSeedFile(string name)
        {
            string dir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            for (int i = 0; i < 5 && dir != null; i++)
            {
                string p = Path.Combine(dir, "labels", name);
                if (File.Exists(p)) return p;
                dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
            }
            return null;
        }

        // ---------- files ----------
        public static LabelSet Load(string path)
        {
            var set = new LabelSet();
            if (!File.Exists(path)) return set;
            var doc = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8));
            int n = 0;
            object formats;
            if (doc.TryGetValue("formats", out formats) && formats is object[])
            {
                foreach (Dictionary<string, object> f in (object[])formats)
                {
                    n++;
                    set.Formats.Add(new LabelFormat
                    {
                        Key = f.ContainsKey("key") ? Convert.ToInt32(f["key"]) : n,
                        Name = Convert.ToString(f["name"]),
                        PaperId = Convert.ToInt32(f["paper_id"]),
                        LengthMm = Convert.ToDouble(f["length_mm"], CultureInfo.InvariantCulture),
                        FeedMm = f.ContainsKey("feed_mm") ? Convert.ToDouble(f["feed_mm"], CultureInfo.InvariantCulture) : 3.0,
                    });
                }
            }
            object owned;
            if (doc.TryGetValue("owned_forms", out owned) && owned is object[])
            {
                set.HasOwnedList = true;
                set.OwnedForms.AddRange(((object[])owned).Select(o => Convert.ToString(o)));
            }
            return set;
        }

        // A file that exists but can't be parsed (edited by hand, cut short): not an I/O problem.
        public static bool IsDamage(Exception ex) { return !(ex is IOException || ex is UnauthorizedAccessException); }

        public static string DamagedCopyPath { get { return JsonPath + ".bad"; } }

        // labels.json for an apply. When it is damaged (damaged: the reason), the forms this tool created are the names in
        // labels.tsv, which every apply writes with the list: the new list can still be applied without touching other forms.
        public static LabelSet LoadCurrent(out string damaged)
        {
            damaged = null;
            try { return Load(JsonPath); }
            catch (Exception ex)
            {
                if (!IsDamage(ex)) throw;
                damaged = ex.Message;
                return new LabelSet { OwnedForms = TsvNames(TsvPath), HasOwnedList = true };
            }
        }

        internal static List<string> TsvNames(string path)
        {
            var names = new List<string>();
            if (!File.Exists(path)) return names;
            foreach (var line in File.ReadAllLines(path, Encoding.UTF8))
            {
                int tab = line.IndexOf('\t');
                if (tab > 0 && !line.StartsWith("#")) names.Add(line.Substring(0, tab));
            }
            return names;
        }

        public static string ToJson(LabelSet set)
        {
            var js = new JavaScriptSerializer();
            var sb = new StringBuilder();
            sb.Append("{\n \"version\": \"2.00\",\n \"formats\": [\n");
            for (int i = 0; i < set.Formats.Count; i++)
            {
                var f = set.Formats[i];
                sb.AppendFormat(CultureInfo.InvariantCulture,
                    "  {{\"key\": {0}, \"name\": {1}, \"paper_id\": {2}, \"length_mm\": {3}, \"feed_mm\": {4}}}{5}\n",
                    f.Key, js.Serialize(f.Name), f.PaperId, f.LengthMm.ToString("0.##", CultureInfo.InvariantCulture),
                    f.FeedMm.ToString("0.##", CultureInfo.InvariantCulture), i + 1 < set.Formats.Count ? "," : "");
            }
            sb.Append(" ],\n \"owned_forms\": ");
            sb.Append(js.Serialize(set.OwnedForms));
            sb.Append("\n}\n");
            return sb.ToString();
        }

        public static string ToTsv(IEnumerable<LabelFormat> formats)
        {
            var sb = new StringBuilder("# QLUnidrv labels 1\n");
            foreach (var f in formats)
            {
                var m = MediaTable.ById(f.PaperId);
                if (m == null) continue;
                sb.Append(f.Name).Append('\t').Append(m.Id).Append('\t')
                  .Append(Length01(f, m).ToString(CultureInfo.InvariantCulture)).Append('\t')
                  .Append(FeedDots(f, m).ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
            return sb.ToString();
        }

        // Write to a temporary file next to the target, then swap it in, so readers (the plug-in,
        // a second instance) never see a half-written file.
        public static void WriteAtomic(string path, string text)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, text, new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        // ---------- geometry (must match driver/gen/media.py and driver/src/ql_raster.cpp) ----------
        static int Mm01ToDots(int v) { return (v * 300 * 10 / 254 + 5) / 10; }
        static int DotsToUm(int dots) { return (int)(((long)dots * 254000 + 1500) / 3000); }

        public static int Length01(LabelFormat f, Media m)
        {
            return m.Continuous ? (int)Math.Round(f.LengthMm * 10) : m.Length01;
        }

        public static int FeedDots(LabelFormat f, Media m)
        {
            return m.Continuous ? (int)Math.Round(f.FeedMm * 300 / 25.4) : m.TopDots;
        }

        // Size of the Windows form: the driver's page size (continuous: Brother's page is one dot longer than the
        // rounded length).
        public static int FormWidthUm(Media m) { return m.PageWidthUm; }

        public static int FormLengthUm(LabelFormat f, Media m)
        {
            return m.Continuous ? DotsToUm(Mm01ToDots(Length01(f, m)) + 1) : m.PageLengthUm;
        }

        // Die-cut labels have a fixed length. Their feed is not used (the driver takes the paper's
        // offset); it is kept only for Brother's file format.
        public static void Normalize(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            if (m != null && !m.Continuous) f.LengthMm = m.LengthMm;
        }

        // ---------- validation ----------
        public static string ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return Lang.T("The name is empty.");
            if (name != name.Trim()) return Lang.T("The name starts or ends with spaces.");
            if (name.Length > MaxNameLength) return Lang.F("The name is longer than {0} characters (Windows limit for paper names).", MaxNameLength);
            if (name.Any(char.IsControl)) return Lang.T("The name contains control characters.");
            if (MediaTable.IsMediaName(name)) return Lang.F("'{0}' is the name of a Brother paper.", name);
            return null;
        }

        public static string ValidateFormat(LabelFormat f)
        {
            string e = ValidateName(f.Name);
            if (e != null) return e;
            var m = MediaTable.ById(f.PaperId);
            if (m == null) return Lang.F("Brother media {0} is not supported.", f.PaperId);
            if (!m.Continuous) return null;
            if (f.LengthMm < m.MinLength01 / 10.0 || f.LengthMm > m.MaxLength01 / 10.0)
                return Lang.F("The length must be {0}–{1} mm.", Media.Mm(m.MinLength01 / 10.0), Media.Mm(m.MaxLength01 / 10.0));
            if (f.FeedMm < 0 || f.FeedMm > MaxFeedMm) return Lang.F("The feed must be 0–{0} mm.", Media.Mm(MaxFeedMm));
            if (f.LengthMm - 2 * f.FeedMm < 1) return Lang.T("The length must exceed twice the feed by at least 1 mm.");
            return null;
        }

        // All problems of a list, as "name: problem". forms: the current forms database (null = not checked).
        public static List<string> Validate(IList<LabelFormat> formats, IList<FormInfo> forms, ICollection<string> owned)
        {
            var errors = new List<string>();
            foreach (var f in formats)
            {
                string e = ValidateFormat(f);
                if (e != null) errors.Add(Label(f) + e);
            }
            foreach (var g in formats.GroupBy(f => f.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                errors.Add(Lang.F("'{0}': the name is used {1} times.", g.Key, g.Count()));
            if (forms != null)
            {
                foreach (var f in formats)
                {
                    var m = MediaTable.ById(f.PaperId);
                    if (m == null) continue;
                    var existing = forms.FirstOrDefault(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null || owned.Contains(existing.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    errors.Add(Label(f) + (existing.Flags == FormsDb.FORM_BUILTIN
                                           ? Lang.T("Windows already has a paper size with this name (built in).")
                                           : Lang.T("Windows already has a paper size with this name (not created by this tool).")));
                }
            }
            return errors;
        }

        static string Label(LabelFormat f) { return "'" + (f.Name.Length > 0 ? f.Name : Lang.T("(no name)")) + "': "; }

        public static string Describe(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            if (m == null) return Lang.T("unsupported media");
            if (m.Kind == MediaKind.Round) return Lang.F("\u00D8 {0} mm, round", Media.Mm(m.WidthMm));
            return SizeText(f) + ", " + m.KindText;
        }

        // The size alone (QL Label Formats' list: its groups already say continuous, die-cut or round).
        public static string SizeText(LabelFormat f)
        {
            var m = MediaTable.ById(f.PaperId);
            if (m == null) return Lang.T("unsupported media");
            if (m.Kind == MediaKind.Round) return "\u00D8 " + Media.Mm(m.WidthMm) + " mm";
            return Media.Mm(m.WidthMm) + " \u00D7 " + Media.Mm(m.Continuous ? f.LengthMm : m.LengthMm) + " mm";
        }

        public static int NextKey(IEnumerable<LabelFormat> formats)
        {
            return formats.Select(f => f.Key).DefaultIfEmpty(0).Max() + 1;
        }
    }
}
