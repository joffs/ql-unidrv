// UI language: English or German. The texts in the code are English; Strings.de.cs maps them to German.
//
// Which language: "--lang en|de" on the command line (Save & apply passes it to its elevated --apply), English for
// the command-line modes that scripts read (--sync, --plan, --identify, --remove-forms, --service, --apply without
// --lang), else the user's choice in QL Label Formats (HKCU\Software\QLUnidrv, value Language = en / de), else the
// Windows display language: German for de-* (Germany, Austria, Switzerland, ...), English for all others.
// A process keeps the language it started with; the printer monitor and the status window restart when the setting
// changes (WatchSetting), other windows switch at their next start.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace QlLabels
{
    static partial class Lang
    {
        public const string SettingsKey = @"Software\QLUnidrv";
        const string SettingValue = "Language";
        // Modes with a window: they follow the user's language.
        static readonly string[] WindowModes = { "--printer", "--setup", "--install", "--uninstall" };

        static string current;  // read once: a process keeps the language it started with (LanguageSwitch)
        static string forced;   // from the command line, or set by the tests
        static bool resolved;
        [ThreadStatic] static int englishDepth;

        // "en" or "de".
        public static string Current
        {
            get
            {
                if (englishDepth > 0) return "en";
                if (!resolved) Resolve();
                if (forced != null) return forced;
                return current ?? (current = Setting ?? SystemLanguage);
            }
        }

        public static bool German { get { return Current == "de"; } }

        static void Resolve()
        {
            resolved = true;
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--lang");
            if (i > 0 && i + 1 < args.Length) forced = Normalize(args[i + 1]) ?? "en";
            else if (args.Length > 1 && !windowApp && !WindowModes.Contains(args[1])) forced = "en";
        }

        // A program whose every start is a window (QlLabelMaker.exe: its first argument may be a file): the user's language
        // also with arguments. Call before the first text.
        static bool windowApp;
        public static void WindowApp() { windowApp = true; }

        // For the tests: always this language ("en" / "de"), or null for the normal choice.
        public static void Force(string language) { resolved = true; forced = language; }

        // Runs work with the English texts on this thread (log files stay English).
        public static T InEnglish<T>(Func<T> work)
        {
            englishDepth++;
            try { return work(); }
            finally { englishDepth--; }
        }

        // The Windows display language: German for de-DE, de-AT, de-CH and the other German locales.
        public static string SystemLanguage
        {
            get { return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? "de" : "en"; }
        }

        // The user's choice, or null for "as Windows".
        public static string Setting
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(SettingsKey))
                        return k != null ? Normalize(Convert.ToString(k.GetValue(SettingValue))) : null;
                }
                catch (Exception) { return null; }
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(SettingsKey))
                {
                    if (value == null) k.DeleteValue(SettingValue, false);
                    else k.SetValue(SettingValue, value);
                }
            }
        }

        // Calls changed (on a thread-pool thread) once the language setting changes so that this process would show
        // another one: the printer monitor and the status window restart then. Waits for a registry change notification on
        // HKCU\Software\QLUnidrv, no polling.
        public static void WatchSetting(Action changed) { WatchKey(SettingsKey, () => (Setting ?? SystemLanguage) != Current, changed); }

        // Calls changed once differs() is true after a change under HKCU\path (tests: a key of their own).
        internal static RegistryKey WatchKey(string path, Func<bool> differs, Action changed)
        {
            RegistryKey key;
            try { key = Registry.CurrentUser.CreateSubKey(path); }
            catch (Exception) { return null; }
            var signal = new AutoResetEvent(false);
            WaitOrTimerCallback wait = null;
            Func<bool> arm = () => RegNotifyChangeKeyValue(key.Handle.DangerousGetHandle(), false, RegNotifyChangeLastSet | RegNotifyThreadAgnostic,
                                                           signal.SafeWaitHandle.DangerousGetHandle(), true) == 0;
            wait = delegate
            {
                if (differs()) { changed(); return; }   // watched no more
                if (arm()) ThreadPool.RegisterWaitForSingleObject(signal, wait, null, -1, true);
            };
            if (arm()) ThreadPool.RegisterWaitForSingleObject(signal, wait, null, -1, true);
            lock (watched) watched.Add(key);   // the notification lives as long as the key is open
            return key;
        }

        static readonly List<RegistryKey> watched = new List<RegistryKey>();
        const int RegNotifyChangeLastSet = 4, RegNotifyThreadAgnostic = 0x10000000;
        [DllImport("advapi32.dll")]
        static extern int RegNotifyChangeKeyValue(IntPtr key, bool subtree, int filter, IntPtr ev, bool async);

        static string Normalize(string language)
        {
            return language == "en" || language == "de" ? language : null;
        }

        // The language's own name, as the switch shows it.
        public static string NameOf(string language) { return language == "de" ? "Deutsch" : "English"; }

        // The text in the current language (the English text when no translation exists).
        public static string T(string english)
        {
            string text;
            return Current == "de" && De.TryGetValue(english, out text) ? text : english;
        }

        // T, then string.Format with the arguments ({0}, {1}, ...).
        public static string F(string english, params object[] args)
        {
            return string.Format(CultureInfo.CurrentCulture, T(english), args);
        }

        // For the tests.
        public static IEnumerable<KeyValuePair<string, string>> GermanTexts { get { return De; } }
    }
}
