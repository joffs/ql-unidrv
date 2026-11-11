// Unit test for the UI languages (Lang.cs, Strings.de.cs): every English text the sources pass to Lang.T / Lang.F has
// a German entry with the same {0} placeholders, and no entry is left over. Reads labeltool\src (build.cmd runs the
// tests from labeltool\). Called by StatusTest.Main.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Keys = System.Windows.Forms.Keys;

namespace QlLabels.Tests
{
    static class LangTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        // Lang.T("...") / Lang.F("..." + "...", ...): the English text, joined and unescaped as the compiler does.
        const string Literal = "\"((?:[^\"\\\\]|\\\\.)*)\"";
        static readonly Regex Call = new Regex(@"Lang\.[TF]\(\s*" + Literal + @"((?:\s*\+\s*" + Literal + ")*)");
        static readonly Regex Part = new Regex(Literal);
        static readonly Regex Placeholder = new Regex(@"\{(\d+)\}");

        public static int Run()
        {
            var used = new Dictionary<string, string>();  // text -> first file
            foreach (var file in Directory.GetFiles("src", "*.cs", SearchOption.AllDirectories))
            {
                string source = File.ReadAllText(file, Encoding.UTF8);
                foreach (Match m in Call.Matches(source))
                {
                    string text = Unescape(m.Groups[1].Value) +
                                  string.Concat(Part.Matches(m.Groups[2].Value).Cast<Match>().Select(p => Unescape(p.Groups[1].Value)));
                    if (!used.ContainsKey(text)) used[text] = Path.GetFileName(file);
                }
            }
            Check(used.Count > 200, "lang: texts found in src (" + used.Count + ")");

            Dictionary<string, string> de;
            try { de = Lang.GermanTexts.ToDictionary(e => e.Key, e => e.Value); }
            catch (Exception ex) { Check(false, "lang: German table: " + ex.GetBaseException().Message); return failures; }

            foreach (var u in used)
            {
                string german;
                if (!de.TryGetValue(u.Key, out german)) { Check(false, "lang: no German text for \"" + u.Key + "\" (" + u.Value + ")"); continue; }
                Check(Placeholders(german).SetEquals(Placeholders(u.Key)), "lang: placeholders differ: \"" + u.Key + "\"");
                // A menu or button text ("Save && apply"): a single '&' would underline the next letter instead.
                Check(!u.Key.Contains("&&") || !german.Replace("&&", "").Contains("&"), "lang: single '&' in a menu text: \"" + u.Key + "\"");
                Check(Count(german, "|") == Count(u.Key, "|"), "lang: file filter parts differ: \"" + u.Key + "\"");
                Check(german.Trim().Length > 0, "lang: empty German text: \"" + u.Key + "\"");
            }
            foreach (var key in de.Keys.Where(k => !used.ContainsKey(k)))
                Check(false, "lang: German text no longer used: \"" + key + "\"");

            // The switch itself.
            Lang.Force("de");
            Check(Lang.T("Test print") == "Testdruck" && Lang.F("{0} QL printers", 2) == "2 QL-Drucker", "lang: German");
            Check(Lang.T("not in the table") == "not in the table", "lang: missing text stays English");
            Check(Lang.InEnglish(() => Lang.T("Test print")) == "Test print" && Lang.German, "lang: InEnglish only inside");
            // key names in the program's language, whatever Windows' language is
            Check(KeyNames.Text(Keys.Control | Keys.Shift | Keys.V) == "Strg+Umschalt+V" && KeyNames.Text(Keys.Delete) == "Entf" &&
                  KeyNames.Text(Keys.Control | Keys.PageUp) == "Strg+Bild↑", "lang: German key names: " + KeyNames.Text(Keys.Control | Keys.Shift | Keys.V));
            Lang.Force("en");
            Check(Lang.T("Test print") == "Test print" && !Lang.German, "lang: English");
            Check(KeyNames.Text(Keys.Control | Keys.Shift | Keys.V) == "Ctrl+Shift+V" && KeyNames.Text(Keys.Control | Keys.Z) == "Ctrl+Z" &&
                  KeyNames.Text(Keys.Alt | Keys.F4) == "Alt+F4" && KeyNames.Text(Keys.Control | Keys.D1) == "Ctrl+1", "lang: English key names: " + KeyNames.Text(Keys.Control | Keys.Shift | Keys.V));
            Check(Lang.NameOf("de") == "Deutsch" && Lang.NameOf("en") == "English", "lang: language names");
            Switch();
            return failures;
        }

        // The language menu: the setting is stored only after the question, and after unsaved changes were saved.
        static void Switch()
        {
            Lang.Force("en");
            string stored = "-"; int asked = 0, saved = 0;
            Func<string, bool, bool, bool, bool> run = (setting, unsaved, answer, saveOk) =>
            {
                stored = "-"; asked = saved = 0;
                return LanguageSwitch.Run(setting, unsaved, u => { asked++; return answer; },
                                          () => { saved++; return System.Threading.Tasks.Task.FromResult(saveOk); }, s => stored = s ?? "auto").Result;
            };
            Check(!run("en", true, true, true) && stored == "en" && asked == 0 && saved == 0, "switch: same language stored without a question");
            Check(!run("de", false, false, true) && stored == "-" && asked == 1, "switch: Cancel stores nothing");
            Check(!run("de", true, false, true) && stored == "-" && saved == 0, "switch: Cancel with unsaved changes saves nothing");
            Check(!run("de", true, true, false) && stored == "-" && saved == 1, "switch: failed save, no switch");
            Check(run("de", true, true, true) && stored == "de" && saved == 1, "switch: saved first, then stored, restart");
            Check(run("de", false, true, true) && stored == "de" && saved == 0, "switch: no unsaved changes, no save");
            bool windowsEnglish = Lang.SystemLanguage == "en";
            Check(run(null, false, true, true) == !windowsEnglish && stored == "auto" && asked == (windowsEnglish ? 0 : 1), "switch: as Windows");

            Lang.Force("de");
            using (var d = LanguageSwitch.Question("QL Label Formats", "en", Lang.T("You have unsaved changes: they are saved and applied first."), true))
            {
                var texts = AllTexts(d).ToList();
                Check(texts.Contains("Sprache auf Englisch umstellen?") && texts.Any(t => t.Contains("QL Label Formats startet dafür neu.")) && texts.Any(t => t.Contains("vorher gespeichert und angewendet") && t.Contains("wechseln beim nächsten Start")) &&
                      ((System.Windows.Forms.Button)d.AcceptButton).Text == "Speichern und neu starten" && ((System.Windows.Forms.Button)d.CancelButton).Text == "Abbrechen",
                      "switch: German question with unsaved changes");
            }
            using (var d = LanguageSwitch.Question("QL Label Formats", null, null, false))
                Check(AllTexts(d).Any(t => t.StartsWith("Sprache wie Windows verwenden (")) && ((System.Windows.Forms.Button)d.AcceptButton).Text == "Jetzt neu starten" &&
                      !AllTexts(d).Any(t => t.Contains("vorher gespeichert") || t.Contains("nächsten Start")), "switch: German question as Windows");
            Lang.Force("en");
            Watch();
        }

        // The registry change notification the monitor and the status window restart on (a test key of its own).
        static void Watch()
        {
            string path = @"Software\QLUnidrv\test-" + Guid.NewGuid().ToString("N");
            var called = new System.Threading.ManualResetEvent(false);
            bool differs = false;
            Microsoft.Win32.RegistryKey key = null;
            try
            {
                key = Lang.WatchKey(path, () => differs, () => called.Set());
                Check(key != null, "watch: key opened");
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path)) k.SetValue("Other", "1");
                Check(!called.WaitOne(1500), "watch: a change that doesn't matter");
                differs = true;
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(path)) k.SetValue("Language", "de");
                Check(called.WaitOne(5000), "watch: a change that matters arrives (no polling)");
            }
            finally
            {
                if (key != null) key.Dispose();
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(path, false); } catch (Exception) { }
            }
        }

        static IEnumerable<string> AllTexts(System.Windows.Forms.Control c)
        {
            yield return c.Text;
            foreach (System.Windows.Forms.Control x in c.Controls) foreach (var t in AllTexts(x)) yield return t;
        }

        static string Unescape(string s) { return Regex.Unescape(s); }

        static HashSet<string> Placeholders(string s)
        {
            return new HashSet<string>(Placeholder.Matches(s).Cast<Match>().Select(m => m.Value));
        }

        static int Count(string s, string what)
        {
            int n = 0;
            for (int i = s.IndexOf(what, StringComparison.Ordinal); i >= 0; i = s.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
