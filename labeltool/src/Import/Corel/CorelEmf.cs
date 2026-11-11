// CorelDRAW's EMF on the clipboard: how its text records differ from GDI's own (read by Import/EmfText.cs).
// - The last character of each text record has dx = 1 (logical unit): the run's width would end one character early,
//   so the gap to the next run looks like a word gap and the line widths for the alignment come out short.
// - A line can arrive as several records, its right part first (sorted along the baseline in EmfText.Lines, for every
//   source).
// - The face name carries the style in CorelDRAW's language ("Arial Fett", "Arial Kursiv").
// - Text sizes and places in the units of the first mapping although the page was reset to 1:1 before (EmfText.Repair,
//   for every source: harmless elsewhere).
namespace QlLabels
{
    static class CorelEmf
    {
        public static readonly EmfQuirks Quirks = new EmfQuirks { ShortLastAdvance = true, StyleInFaceName = true };
    }
}
