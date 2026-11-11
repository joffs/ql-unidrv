// Affinity's clipboard (Affinity 3, and Designer as "Serif Persona Nodes"): what differs from other programs' SVG.
// - The SVG counts px at the document's resolution (300 per inch in a print document), not 96 per inch: read as an SVG
//   alone, a copy comes out 3.125 times too big. The PDF it puts beside it has the true size (its MediaBox, in points),
//   so the size comes from there when the PDF and the SVG have the same aspect.
// - Text is <text> per laid-out line with the font list starting with its PostScript name ('Arial-BoldMT', 'Arial');
//   tracking and kerning are letter-spacing per character; an underline is a filled <rect> under the line; a frame copied
//   alone gives the frame's size; a paragraph's space after is part of the line pitch (Import/SvgText.cs reads all that).
// - No EMF; the PDF holds glyph numbers only (no text, no paragraphs), so it adds nothing beyond the size.
using System;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace QlLabels
{
    static class AffinityClip
    {
        public const string PdfFormat = "Portable Document Format";
        public const string NodesFormat = "Affinity Nodes", DesignerNodesFormat = "Serif Persona Nodes";

        // Document resolutions: the SVG's px for its text are taken at one of these when the size it was copied at gives
        // one within 1 % (the SVG's size is rounded to whole px, the PDF's is exact: 473 px over 40 mm is 300.4 per inch,
        // and 12 pt would come out as 11.97 pt). Before Quirks: static fields start in this order.
        static readonly int[] Resolutions = { 72, 96, 144, 150, 200, 240, 300, 360, 600, 1200 };

        public static readonly SvgQuirks Quirks = new SvgQuirks { TextGroups = true, Resolutions = Resolutions };

        // The PDF's first MediaBox in mm; null when there is none (or it can't be read).
        public static SizeF? PdfSizeMm(byte[] pdf)
        {
            if (pdf == null || pdf.Length < 20) return null;
            // the header and the page objects are ASCII; Latin-1 keeps every byte one character
            string s = Encoding.GetEncoding(28591).GetString(pdf, 0, Math.Min(pdf.Length, 4 * 1024 * 1024));
            if (!s.StartsWith("%PDF", StringComparison.Ordinal)) return null;
            var m = Regex.Match(s, @"/MediaBox\s*\[\s*([-0-9.]+)\s+([-0-9.]+)\s+([-0-9.]+)\s+([-0-9.]+)\s*\]");
            if (!m.Success) return null;
            var v = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(m.Groups[i + 1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return null;
            double w = Math.Abs(v[2] - v[0]) * 25.4 / 72, h = Math.Abs(v[3] - v[1]) * 25.4 / 72;
            // within what a picture may be (any program can put a PDF beside an "Affinity Nodes" format)
            return w > 0 && h > 0 && w <= LabelObject.MaxMm && h <= LabelObject.MaxMm ? new SizeF((float)w, (float)h) : (SizeF?)null;
        }

        // The size the copy was made at: the PDF's when it has the SVG's aspect (within 2 %, or the SVG's whole-px
        // rounding of a small copy), else the SVG's own (svgMm: read at 96 px per inch).
        public static SizeF? SizeMm(SizeF? svgMm, byte[] pdf)
        {
            var p = PdfSizeMm(pdf);
            if (p == null) return svgMm;
            if (svgMm == null) return p;
            double a = svgMm.Value.Width / svgMm.Value.Height, b = p.Value.Width / p.Value.Height;
            double wpx = svgMm.Value.Width * 96 / 25.4, hpx = svgMm.Value.Height * 96 / 25.4;
            return Math.Abs(a - b) <= b * Math.Max(0.02, 1.5 * (1 / wpx + 1 / hpx)) ? p : svgMm;
        }
    }
}
