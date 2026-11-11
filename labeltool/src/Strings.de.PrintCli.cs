// German texts of QlLabelPrint.exe (PrintProgram.cs). Same rules as Strings.de.cs (terms, placeholders; lang_test.cs
// checks them). The options (--print, --data, ...) stay as they are in every language.
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> PrintCliTexts()
        {
            return new Dictionary<string, string>
            {
                // ---------- the command line ----------
                { "{0} needs a value.", "{0} braucht einen Wert." },
                { "--set needs Name=Value, not '{0}'.", "--set braucht Name=Wert, nicht „{0}“." },
                { "--copies needs a number from 1 to {0}, not '{1}'.", "--copies braucht eine Zahl von 1 bis {0}, nicht „{1}“." },
                { "--rows needs a row or a range such as 2-10, not '{0}'.", "--rows braucht eine Zeile oder einen Bereich wie 2-10, nicht „{0}“." },
                { "Unknown option {0}.", "Unbekannte Option {0}." },
                { "Say what to do: --print, --fields or --printers.", "Was soll geschehen? --print, --fields oder --printers." },
                { "Only one of --print, --fields and --printers at a time.", "Nur eines von --print, --fields und --printers auf einmal." },
                { "--data, --set, --name, --preview and --dry-run only go with --print.", "--data, --set, --name, --preview und --dry-run gibt es nur mit --print." },
                { "No row has the labelname {0}.", "Keine Zeile hat den labelname {0}." },
                { "Exit codes: 0 done, 1 usage, 2 label file, 3 data, 4 printer, 5 printer not ready or roll unknown, 6 Windows refused the job.",
                  "Exit-Codes: 0 erledigt, 1 Aufruf, 2 Etikettendatei, 3 Daten, 4 Drucker, 5 Drucker nicht bereit oder Rolle unbekannt, 6 Windows hat den Druckauftrag abgelehnt." },
                // ---------- label file and data ----------
                { "The label file {0} doesn't exist.", "Die Etikettendatei {0} gibt es nicht." },
                { "The label file {0} is larger than {1} MB.", "Die Etikettendatei {0} ist größer als {1} MB." },
                { "The label file {0} can't be read: {1}", "Die Etikettendatei {0} kann nicht gelesen werden: {1}" },
                { "The data file {0} doesn't exist.", "Die Datendatei {0} gibt es nicht." },
                { "The folder of the label's data file isn't reachable: {0}", "Der Ordner der Datendatei des Etiketts ist nicht erreichbar: {0}" },
                { "The label's data file {0} doesn't exist (next to the label or at that place).", "Die Datendatei {0} des Etiketts gibt es nicht (weder neben dem Etikett noch an dieser Stelle)." },
                { "The JSON data can't be read: {0}", "Die JSON-Daten können nicht gelesen werden: {0}" },
                { "The JSON data must be an object or a list of objects.", "Die JSON-Daten müssen ein Objekt oder eine Liste von Objekten sein." },
                { "not an object", "kein Objekt" },
                { "the value of {0} is not a text or number.", "der Wert von {0} ist weder Text noch Zahl." },
                { "The JSON data has more than {0} different names.", "Die JSON-Daten haben mehr als {0} verschiedene Namen." },
                { "--preview makes at most {0} pictures; choose rows with --rows.", "--preview erstellt höchstens {0} Bilder; Zeilen mit --rows wählen." },
                { "The data has no rows to print (see --rows).", "Die Daten haben keine Zeilen zum Drucken (siehe --rows)." },
                { "The data has errors (see the list); nothing was printed.", "Die Daten haben Fehler (siehe Liste); es wurde nichts gedruckt." },
                // ---------- printers and roll ----------
                { "{0} is not a QL printer of this driver. QL printers: {1}", "{0} ist kein QL-Drucker dieses Treibers. QL-Drucker: {1}" },
                { "No QL printer has the paper for this label. QL printers: {0}", "Kein QL-Drucker hat das Papier für dieses Etikett. QL-Drucker: {0}" },
                { "Several printers fit: {0}. Choose one with --printer.", "Mehrere Drucker passen: {0}. Einen mit --printer wählen." },
                { "none", "keine" },
                { "No QL printers.", "Keine QL-Drucker." },
                { "roll unknown", "Rolle unbekannt" },
                { "default", "Standard" },
                { "{0} is set to 'Use Printer Offline' in Windows. Use it online first (QL Printer Status).",
                  "{0} ist in Windows auf „Drucker offline verwenden“ gestellt. Zuerst online verwenden (QL Printer Status)." },
                { "The loaded roll of {0} can't be read, so nothing was printed. On a shared printer, turn on the status service on the PC it is plugged into. To print without the check: --no-roll-check.",
                  "Die eingelegte Rolle von {0} kann nicht gelesen werden, deshalb wurde nichts gedruckt. Bei einem freigegebenen Drucker den Statusdienst auf dem PC einschalten, an dem er angeschlossen ist. Ohne diese Prüfung drucken: --no-roll-check." },
                { "{0} didn't answer, so its loaded roll is unknown and nothing was printed. To print without the check: --no-roll-check.",
                  "{0} hat nicht geantwortet, die eingelegte Rolle ist unbekannt und es wurde nichts gedruckt. Ohne diese Prüfung drucken: --no-roll-check." },
                { "The printer has {0} loaded, but '{1}' needs {2}. Load the matching roll first.",
                  "Im Drucker ist {0} eingelegt, aber „{1}“ braucht {2}. Zuerst die passende Rolle einlegen." },
                { "Windows didn't take the print job: {0}", "Windows hat den Druckauftrag nicht angenommen: {0}" },
                // ---------- results ----------
                { "{0} labels sent to {1}.", "{0} Etiketten an {1} gesendet." },
                { "Checked: {0} labels for {1}, nothing printed.", "Geprüft: {0} Etiketten für {1}, nichts gedruckt." },
                { "{0} pictures in {1}.", "{0} Bilder in {1}." },
                { "1 picture in {0}.", "1 Bild in {0}." },
                { "Checked: 1 label for {0}, nothing printed.", "Geprüft: 1 Etikett für {0}, nichts gedruckt." },
                // "1 label sent to {0}." is in Strings.de.Editor.cs (the Print with data dialog)
            };
        }
    }
}
