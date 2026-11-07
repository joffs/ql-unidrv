// The language menu's switch (QL Label Formats; reusable by other windows): the question, and the setting stored only
// after it. Each program keeps the language it started with (Lang.Current); the printer monitor and the status window
// restart by themselves when the setting changes (Lang.WatchSetting), other open windows switch at their next start.
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    static class LanguageSwitch
    {
        // A choice: setting = "en", "de" or null (as Windows). When the shown language stays the same it is stored at once;
        // otherwise ask(unsaved) confirms, unsaved changes are saved first (save: false when that failed or was
        // cancelled), and only then is the setting stored. True: restart now.
        public static async Task<bool> Run(string setting, bool unsaved, Func<bool, bool> ask, Func<Task<bool>> save, Action<string> store)
        {
            if ((setting ?? Lang.SystemLanguage) == Lang.Current) { store(setting); return false; }
            if (!ask(unsaved)) return false;
            if (unsaved && !await save()) return false;
            store(setting);
            return true;
        }

        // Extras > "Language / Sprache": Automatic (as Windows), English, Deutsch, ticked from the stored setting; choose
        // gets "en", "de" or null. Named in both languages, so either reader finds it.
        public static ToolStripMenuItem Menu(Action<string> choose)
        {
            var language = new ToolStripMenuItem("Language / Sprache")
            {
                ToolTipText = Lang.NameOf(Lang.Current) + ": " +
                    Lang.T("the language of QL Label Formats, QL Label Maker, QL Printer Status and Printer setup."),
            };
            var auto = new ToolStripMenuItem(Lang.F("Automatic (as Windows: {0})", Lang.NameOf(Lang.SystemLanguage)), null, delegate { choose(null); });
            var en = new ToolStripMenuItem("English", null, delegate { choose("en"); });
            var de = new ToolStripMenuItem("Deutsch", null, delegate { choose("de"); });
            language.DropDownItems.AddRange(new ToolStripItem[] { auto, new ToolStripSeparator(), en, de });
            language.DropDownOpening += delegate
            {
                string setting = Lang.Setting;
                auto.Checked = setting == null;
                en.Checked = setting == "en";
                de.Checked = setting == "de";
            };
            return language;
        }

        // "Switch the language to English?" (or "Use the Windows language (German)?"), the programs it applies to, that
        // app restarts, the line for other open QL windows (othersOpen; null: looked up), unsavedLine (null: none), and
        // Restart now / Save and restart (OK) or Cancel. app: the program's name (dialog title, restart sentence).
        public static SetupResult Question(string app, string setting, string unsavedLine, bool? othersOpen = null)
        {
            string target = (setting ?? Lang.SystemLanguage) == "de" ? Lang.T("German") : Lang.T("English");
            string question = setting == null ? Lang.F("Use the Windows language ({0})?", target) : Lang.F("Switch the language to {0}?", target);
            string note = Lang.F("Applies to QL Label Formats, QL Label Maker, QL Printer Status and Printer setup. {0} restarts for it.", app);
            if (othersOpen ?? OtherProgramsOpen()) note += "\n" + Lang.T("Other open QL programs switch at their next start.");
            if (unsavedLine != null) note += "\n\n" + unsavedLine;
            var d = new SetupResult(app, ResultKind.Question, question, null, note, closeText: Lang.T("Cancel"));
            d.AddChoice(unsavedLine != null ? Lang.T("Save and restart") : Lang.T("Restart now"), DialogResult.OK);
            return d;
        }

        // Another window of QL Label Maker or QL Label Formats in this Windows session (not this process; the status
        // window and the monitor follow by themselves).
        public static bool OtherProgramsOpen()
        {
            int self = Process.GetCurrentProcess().Id, session = Process.GetCurrentProcess().SessionId;
            foreach (var name in new[] { "QlLabelMaker", "QlLabelFormats" })
                foreach (var p in Process.GetProcessesByName(name))
                    using (p)
                    {
                        try { if (p.Id != self && p.SessionId == session && p.MainWindowHandle != IntPtr.Zero) return true; }
                        catch (Exception) { }
                    }
            return false;
        }
    }
}
