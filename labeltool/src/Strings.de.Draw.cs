// German texts of QL Label Maker's drawing tools (0.23): the arrow button on the tool bar and the status bar's hint for
// the active tool (EditorForm.Palette.cs). Same rules as Strings.de.cs (terms, placeholders; lang_test.cs checks them).
// Wording approved 2026-11-10 (mockup out/mockups/draw-tools).
using System.Collections.Generic;

namespace QlLabels
{
    static partial class Lang
    {
        static Dictionary<string, string> DrawTexts()
        {
            return new Dictionary<string, string>
            {
                { "Select", "Auswahl" },
                { "Select, move and change objects (Esc)", "Objekte auswählen, verschieben und ändern (Esc)" },
                { "click and type, or drag for the font size", "klicken und schreiben oder für die Schriftgröße ziehen" },
                { "drag the frame, or click", "Rahmen aufziehen oder klicken" },
                { "drag to draw, or click", "zum Zeichnen ziehen oder klicken" },
                { "drag the area, or click", "Fläche aufziehen oder klicken" },
                { "Shift = square", "Umschalt = Quadrat" },
                { "Shift = circle", "Umschalt = Kreis" },
                { "Shift = as wide as high", "Umschalt = so breit wie hoch" },
                { "Shift = straight", "Umschalt = gerade" },
                { "Esc = select", "Esc = Auswahl" },
                { "Write artistic text · Ctrl+T inserts it", "Grafiktext schreiben · Strg+T fügt ein" },
                { "Draw a paragraph text frame · Ctrl+Shift+T inserts it", "Mengentext-Rahmen aufziehen · Strg+Umschalt+T fügt ein" },
                { "Draw a rectangle · Ctrl+B inserts it", "Rechteck zeichnen · Strg+B fügt ein" },
                { "Draw an ellipse · Ctrl+E inserts it", "Ellipse zeichnen · Strg+E fügt ein" },
                { "Draw a triangle", "Dreieck zeichnen" },
                { "Draw a polygon", "Polygon zeichnen" },
                { "Draw a star", "Stern zeichnen" },
                { "Draw a line · Ctrl+L inserts it", "Linie zeichnen · Strg+L fügt ein" },
                { "Place a picture: logo, photo, symbol · Ctrl+I inserts it", "Bild platzieren: Logo, Foto, Symbol · Strg+I fügt ein" },
                { "Draw a barcode · Ctrl+K inserts it", "Barcode zeichnen · Strg+K fügt ein" },
                { "Draw a QR code · Ctrl+Q inserts it", "QR-Code zeichnen · Strg+Q fügt ein" },
                { "Esc = cancel", "Esc = abbrechen" },
                { "type the text", "Text eingeben" },
                { "Esc = done", "Esc = fertig" },
            };
        }
    }
}
