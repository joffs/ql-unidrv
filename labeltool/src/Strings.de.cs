// German texts of the UI (Lang.cs): English text in the code -> German. A text missing here stays English;
// labeltool\test\lang_test.cs checks that every Lang.T/F text in the sources has an entry with the same {0} placeholders.
// Terms: label format = Etikettenformat, continuous = Endlosband, die-cut = Einzeletikett, roll = Rolle,
// feed = Vorschub, print queue = Druckwarteschlange, roll cover = Rollenfachabdeckung.
// Product names stay English (Start menu): QL Label Formats, QL Printer Status, "QL printer status" (service).
// Split by area (2026-11-08): Strings.de.cs (texts several programs use, and the merge), Strings.de.Formats.cs,
// Strings.de.Setup.cs, Strings.de.Editor.cs. Each area adds its texts in its own file. A text that two areas use
// stays in the file where it was first; a key may appear only once in all four (the merge throws, as before).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static readonly Dictionary<string, string> De = Merge(SharedTexts(), FormatsTexts(), SetupTexts(), EditorTexts(), PrintDataTexts(), PrintCliTexts(), RecentTexts(), TabsTexts(), EditorTabsTexts(), DrawTexts());
        static Dictionary<string, string> Merge(params Dictionary<string, string>[] parts)
        {
            var all = new Dictionary<string, string>();
            foreach (var part in parts) foreach (var e in part) all.Add(e.Key, e.Value);
            return all;
        }

        static Dictionary<string, string> SharedTexts()
        {
            return new Dictionary<string, string>
            {
                // ---------- About dialog (AboutForm) ----------
                { "About QL Unidrv", "Über QL Unidrv" },
                { "QL Label Maker Help", "Hilfe zu QL Label Maker" },
                { "The help isn't installed. Setup.exe repairs the installation.", "Die Hilfe ist nicht installiert. Setup.exe repariert die Installation." },
                { "The help page couldn't be opened.", "Die Hilfeseite konnte nicht geöffnet werden." },
                { "QL Label Formats Help", "Hilfe zu QL Label Formats" },
                { "QL Printer Status Help", "Hilfe zu QL Printer Status" },
                { "Driver {0}", "Treiber {0}" },
                { "No QL driver installed", "Kein QL-Treiber installiert" },
                { "Build {0}", "Build {0}" },
                { "Driver build {0}", "Treiber-Build {0}" },
                { "Copy", "Kopieren" },
                { "Copied", "Kopiert" },
                { "Copies the versions, e.g. for a support request", "Kopiert die Versionen, z. B. für eine Support-Anfrage" },
                { "Free software under the MIT License", "Freie Software unter der MIT-Lizenz" },
                { "An independent project, not made or endorsed by Brother. Brother and QL are trademarks of Brother Industries, Ltd.",
                  "Ein unabhängiges Projekt, nicht von Brother erstellt oder unterstützt. Brother und QL sind Marken der Brother Industries, Ltd." },

                // ---------- brand look in the windows of QL Label Formats and QL Printer Status (MainForm, StatusForm, SetupForm, TrayMonitor) ----------
                { "Set up QL printers", "QL-Drucker einrichten" },
                { "The driver is not installed on this PC", "Der Treiber ist auf diesem PC nicht installiert" },
                { "No QL printer on this PC yet", "Noch kein QL-Drucker auf diesem PC" },
                { "1 QL printer on this PC", "1 QL-Drucker auf diesem PC" },
                { "{0} QL printers on this PC", "{0} QL-Drucker auf diesem PC" },
            };
        }
    }
}
