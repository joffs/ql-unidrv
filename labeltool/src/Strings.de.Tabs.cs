// German texts of QL Label Maker's tabs (0.23): the tab strip (DocTabStrip.cs) and the close question with its list
// (SaveChangesDialog.cs). Same rules as Strings.de.cs (terms, placeholders; lang_test.cs checks them). Wording of the
// approved mockup (out/mockups/label-tabs).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> TabsTexts()
        {
            return new Dictionary<string, string>
            {
                { "Open labels", "Geöffnete Etiketten" },
                { "Close (Ctrl+W or middle mouse button)", "Schließen (Strg+W oder mittlere Maustaste)" },
                { "New label (Ctrl+N)", "Neues Etikett (Strg+N)" },
                { "All open labels ({0})", "Alle geöffneten Etiketten ({0})" },
                { "not in the strip", "nicht in der Leiste" },
                { "(unsaved)", "(ungespeichert)" },
                { "Switch", "Wechseln" },
                { "Press", "Drücken" },
                { "Save the changes to {0} labels?", "Änderungen an {0} Etiketten speichern?" },
                { "Save the changes to “{0}”?", "Änderungen an „{0}“ speichern?" },
                { "Do&n't save", "&Nicht speichern" },
            };
        }
    }
}
