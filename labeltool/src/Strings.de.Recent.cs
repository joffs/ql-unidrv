// German texts of QL Label Maker's File > Recent labels (EditorForm.Recent.cs). Same rules as Strings.de.cs (terms,
// placeholders; lang_test.cs checks them). Wording of the approved mockup (out/mockups/recent-files, variant A).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> RecentTexts()
        {
            return new Dictionary<string, string>
            {
                { "&Recent labels", "&Zuletzt geöffnet" },
                { "No labels opened yet", "Noch keine Etiketten geöffnet" },
                { "Clear &list...", "Liste &leeren..." },
                { "not found", "nicht gefunden" },
                { "folder not reachable", "Ordner nicht erreichbar" },
                { "Remove from the list (Del)", "Aus der Liste entfernen (Entf)" },
                { "“{0}” removed from the list.", "„{0}“ aus der Liste entfernt." },
                { "Clear the list of recently opened labels?", "Die Liste der zuletzt geöffneten Etiketten leeren?" },
                { "The labels themselves are kept.", "Die Etiketten selbst bleiben erhalten." },
                { "Clear", "Leeren" },
                { "List cleared.", "Liste geleert." },
                { "“{0}” was not found.", "„{0}“ wurde nicht gefunden." },
                { "It may have been moved or deleted.\n\nRemove it from the list?", "Vielleicht wurde es verschoben oder gelöscht.\n\nAus der Liste entfernen?" },
                { "The folder {0} can't be reached right now.", "Der Ordner {0} ist gerade nicht erreichbar." },
                { "Check the network connection and try again.", "Prüfen Sie die Netzwerkverbindung und versuchen Sie es noch einmal." },
                { "Retry", "Noch einmal" },
            };
        }
    }
}
