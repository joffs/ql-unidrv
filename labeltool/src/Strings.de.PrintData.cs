// German texts of print from data (PrintData.cs, PrintJob.cs, the "Print with data" dialog; QlLabelPrint.exe may add
// its own here). Same rules as Strings.de.cs (terms, placeholders; lang_test.cs checks them).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> PrintDataTexts()
        {
            return new Dictionary<string, string>
            {
                // ---------- engine: CSV, records, checks ----------
                { "The file is larger than {0} MB.", "Die Datei ist größer als {0} MB." },
                { "The file is empty.", "Die Datei ist leer." },
                { "Line {0}: a quoted value is not closed (a \" is missing).", "Zeile {0}: Ein Wert in Anführungszeichen ist nicht abgeschlossen (ein \" fehlt)." },
                { "This is not a CSV text file (it contains NUL characters).", "Das ist keine CSV-Textdatei (sie enthält NUL-Zeichen)." },
                { "Line {0}: more than {1} columns.", "Zeile {0}: mehr als {1} Spalten." },
                { "The file has more than {0} rows.", "Die Datei hat mehr als {0} Zeilen." },
                { "Line {0}: a value is longer than {1} characters.", "Zeile {0}: Ein Wert ist länger als {1} Zeichen." },
                { "More than {0} labels in one print job.", "Mehr als {0} Etiketten in einem Druckauftrag." },
                { "Row {0}: {1}", "Zeile {0}: {1}" },
                { "Row {0} ({1}): {2}", "Zeile {0} ({1}): {2}" },
                { "'{0}' is not a number of copies (0–{1}).", "„{0}“ ist keine Anzahl von Exemplaren (0–{1})." },
                { "The data has no value for {0}.", "In den Daten fehlt ein Wert für {0}." },
                { "The text “{0}” doesn't fit its box and is cut off.", "Der Text „{0}“ passt nicht in seinen Rahmen und wird abgeschnitten." },
                { "{0} has no paper for this label.", "{0} hat kein Papier für dieses Etikett." },
                { "{0} doesn't offer '{1}' yet. A new or changed label format must be saved first: QL Label Formats, then Save & apply.",
                  "{0} bietet „{1}“ noch nicht an. Ein neues oder geändertes Etikettenformat muss zuerst gespeichert werden: QL Label Formats, dann „Übernehmen“." },
                // printer choice, search ✕, large preview (2026-11-09)
                { "The printer the labels are printed on (QL Label Maker switches with it)", "Der Drucker, auf dem die Etiketten gedruckt werden (QL Label Maker wechselt mit)" },
                { "{0} can't print this label.", "{0} kann dieses Etikett nicht drucken." },
                { "{0} offers no label for this template.", "{0} bietet kein Etikett für diese Vorlage an." },
                { "Clear search (Esc)", "Suche leeren (Esc)" },
            };
        }
    }
}
