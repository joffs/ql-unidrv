// German texts of QL Label Maker's tabs (EditorForm.Tabs.cs, tabs 0.23); the strip and the close dialog have theirs in
// Strings.de.Tabs.cs. Same rules as Strings.de.cs (terms, placeholders; lang_test.cs checks them).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> EditorTabsTexts()
        {
            return new Dictionary<string, string>
            {
                { "Untitled {0}", "Unbenannt {0}" },
                { "Not saved yet", "Noch nicht gespeichert" },
                { "not saved yet", "noch nicht gespeichert" },
                { "“{0}” is already open.", "„{0}“ ist bereits geöffnet." },
                { "“{0}” asks for a folder and name afterwards.", "„{0}“ fragt danach nach Ordner und Namen." },
                { "Close &others", "&Andere schließen" },
                { "Open &folder", "&Ordner öffnen" },
                { "The folder couldn't be opened.", "Der Ordner konnte nicht geöffnet werden." },
                { "E&xit", "&Beenden" },
                { "&Close label", "Etikett s&chließen" },
                { "{0} is no longer there.", "{0} ist nicht mehr vorhanden." },
                { "{0} is no longer there: the label is shown on {1}.", "{0} ist nicht mehr vorhanden: Das Etikett wird auf {1} angezeigt." },
            };
        }
    }
}
