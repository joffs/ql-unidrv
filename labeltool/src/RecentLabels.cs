// QL Label Maker's recently opened labels (File > Recent labels, EditorForm.Recent.cs): up to 15 full paths, the newest
// first, per user in HKCU\Software\QLUnidrv\LabelMaker (value RecentLabels, a list of strings) beside the editor's other
// settings; no file of its own. Uninstalling removes HKCU\Software\QLUnidrv of every signed-in user (install.ps1), so
// the list goes with it. A label comes in when it is opened (dialog, Explorer, a drop) and when it is saved.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace QlLabels
{
    static class RecentLabels
    {
        public const int Max = 15;   // 2026-11-09 (was 10); the digits 1-9 for the first nine
        internal static string Key = Lang.SettingsKey + @"\LabelMaker";   // the tests use a key of their own
        const string Value = "RecentLabels";

        public static List<string> Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                {
                    var list = k == null ? null : k.GetValue(Value) as string[];
                    return Clean(list ?? new string[0]);
                }
            }
            catch (Exception) { return new List<string>(); }
        }

        static void Store(List<string> list)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(Value, list.ToArray(), RegistryValueKind.MultiString); }
            catch (Exception) { }
        }

        public static void Add(string path) { if (!string.IsNullOrEmpty(path)) Store(With(Load(), path)); }
        public static void Remove(string path) { Store(Load().Where(p => !Same(p, path)).ToList()); }
        public static void Clear() { Store(new List<string>()); }

        // path at the top (full path; once, whatever its case), at most Max.
        internal static List<string> With(List<string> list, string path)
        {
            try { path = Path.GetFullPath(path); } catch (Exception) { }
            var result = new List<string> { path };
            result.AddRange(list.Where(p => !Same(p, path)));
            return result.Take(Max).ToList();
        }

        // What the registry held, without empty or repeated entries (a hand-edited value), at most Max.
        internal static List<string> Clean(IEnumerable<string> list)
        {
            var result = new List<string>();
            foreach (var p in list)
                if (!string.IsNullOrWhiteSpace(p) && !result.Any(q => Same(q, p)) && result.Count < Max) result.Add(p);
            return result;
        }

        internal static bool Same(string a, string b) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }

        // A folder on another computer (\\server\share or a mapped drive): only looked at when the entry is clicked, so the
        // menu never waits for a sleeping server.
        internal static bool OnNetwork(string path)
        {
            if (path.StartsWith(@"\\")) return true;
            try { return new DriveInfo(Path.GetPathRoot(path)).DriveType == DriveType.Network; }
            catch (Exception) { return false; }
        }
    }
}
