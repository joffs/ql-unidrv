// SVG text as text objects (Import/SvgText.cs), with hand-written SVGs in Affinity's style: px sizes, a group
// transform, <text> per line with a position per character, the font list starting with a PostScript name.
// Usage: svgtext_test.exe   (exit = failures)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace QlLabels.Tests
{
    static class SvgTextTest
    {
        static int failures, checks;

        static void Check(string what, bool ok, string detail = "")
        {
            checks++;
            if (!ok) failures++;
            Console.WriteLine("{0,-5} {1}{2}", ok ? "OK" : "FAIL", what, detail.Length > 0 ? " (" + detail + ")" : "");
        }

        static string Svg(string body, string size = "width=\"200px\" height=\"100px\"")
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"no\"?>\n"
                 + "<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">\n"
                 + "<svg " + size + " version=\"1.1\" xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" "
                 + "xml:space=\"preserve\" xmlns:affinity=\"https://www.affinity.studio/\" style=\"fill-rule:evenodd;clip-rule:evenodd;stroke-linejoin:round;stroke-miterlimit:2;\">\n"
                 + body + "\n</svg>";
        }

        // x positions for each character of text at the font's own advances plus extra (px), from x0
        static string Xs(string text, double x0, double sizePx, string face, bool bold, double extra = 0)
        {
            var parts = new List<string>();
            double x = x0;
            var style = bold ? System.Drawing.FontStyle.Bold : System.Drawing.FontStyle.Regular;
            foreach (char c in text)
            {
                parts.Add(x.ToString("0.###", CultureInfo.InvariantCulture) + "px");
                x += Advance(c, face, style, sizePx) + extra;
            }
            return string.Join(" ", parts);
        }

        static double Advance(char c, string face, System.Drawing.FontStyle style, double px)
        {
            using (var bmp = new System.Drawing.Bitmap(1, 1))
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            using (var f = new System.Drawing.Font(face, (float)px, style, System.Drawing.GraphicsUnit.Pixel))
            {
                g.PageUnit = System.Drawing.GraphicsUnit.Pixel;
                var fmt = System.Drawing.StringFormat.GenericTypographic;
                string one = c.ToString();
                return g.MeasureString(one + one, f, System.Drawing.PointF.Empty, fmt).Width - g.MeasureString(one, f, System.Drawing.PointF.Empty, fmt).Width;
            }
        }

        static string Describe(TextObject t)
        {
            var ci = CultureInfo.InvariantCulture;
            var runs = t.FullRuns();
            var bold = string.Join(",", runs.Where(r => r.Bold == true && r.Text.Trim().Length > 0).Select(r => r.Text.Trim()));
            var sp = runs.Select(r => r.Spacing ?? 100).Distinct().ToList();
            return t.Kind + " " + t.Align + " \"" + t.Text.Replace("\n", "|") + "\" " + t.Font + " " + t.Size.ToString("0.#", ci) + "pt"
                 + (bold.Length > 0 ? " bold:" + bold : "")
                 + (sp.Count == 1 && sp[0] != 100 ? " spacing " + Math.Round(sp[0]).ToString(ci) : "")
                 + (t.Invert ? " inverted" : "")
                 + string.Concat(runs.Where(r => !t.Invert && r.Invert == true && r.Text.Trim().Length > 0).Select(r => " inverted:" + r.Text.Trim()));
        }

        static SvgTextResult Read(string svg) { return SvgText.Read(Encoding.UTF8.GetBytes(svg)); }

        static void Expect(string name, string svg, params string[] want)
        {
            SvgTextResult r;
            try { r = Read(svg); }
            catch (Exception ex) { Check(name, false, ex.GetType().Name + ": " + ex.Message); return; }
            if (r == null) { Check(name, false, "nothing read"); return; }
            var got = r.Objects.OrderBy(o => o.Y).Select(Describe).ToList();
            Check(name, got.SequenceEqual(want), string.Join(" / ", got) + (got.SequenceEqual(want) ? "" : "; expected " + string.Join(" / ", want)));
        }

        [STAThread]
        static int Main()
        {
            Lang.Force("en");
            const string arial = "font-family:'ArialMT', 'Arial', sans-serif;font-size:16px;";
            const string arialBold = "font-family:'Arial-BoldMT', 'Arial', sans-serif;font-weight:700;font-size:16px;";

            // 01: artistic text, one line, 12 pt (16 px at 96 per inch)
            Expect("artistic", Svg("<g transform=\"matrix(1,0,0,1,-100.5,-50.25)\">\n"
                + "<text x=\"110.5px\" y=\"70.25px\" style=\"" + arial + "\">Abc</text>\n</g>"),
                "Line Left \"Abc\" Arial 12pt");

            // 02: two lines, left, per-character positions
            string l1 = "<text x=\"" + Xs("Abc", 10, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Abc</text>";
            string l2 = "<text x=\"" + Xs("Defgh", 10, 16, "Arial", false) + "\" y=\"38.4px\" style=\"" + arial + "\">Defgh</text>";
            Expect("frame left", Svg("<g transform=\"matrix(1,0,0,1,0,0)\">" + l1 + l2 + "</g>"), "Frame Left \"Abc|Defgh\" Arial 12pt");

            // 03: centred: each line's middle at x = 50
            double w1 = "Abc".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16));
            double w2 = "Defgh".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16));
            string c1 = "<text x=\"" + Xs("Abc", 50 - w1 / 2, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Abc</text>";
            string c2 = "<text x=\"" + Xs("Defgh", 50 - w2 / 2, 16, "Arial", false) + "\" y=\"38.4px\" style=\"" + arial + "\">Defgh</text>";
            Expect("frame centred", Svg(c1 + c2), "Frame Center \"Abc|Defgh\" Arial 12pt");

            // 05: tracking: 1.6 px more per character (Arial's space is 0.278 em = 4.45 px: about 136 %)
            string t1 = "<text x=\"" + Xs("Abc", 10, 16, "Arial", false, 1.6) + "\" y=\"20px\" style=\"" + arial + "\">Abc</text>";
            string t2 = "<text x=\"" + Xs("Defgh", 10, 16, "Arial", false, 1.6) + "\" y=\"38.4px\" style=\"" + arial + "\">Defgh</text>";
            Expect("tracking", Svg(t1 + t2), "Frame Left \"Abc|Defgh\" Arial 12pt spacing 136");

            // 06: "Defgh" bold, as a tspan
            string b2 = "<text x=\"10px\" y=\"38.4px\" style=\"" + arial + "\"><tspan style=\"" + arialBold + "\">Defgh</tspan></text>";
            Expect("bold line", Svg(l1 + b2), "Frame Left \"Abc|Defgh\" Arial 12pt bold:Defgh");

            // a font that isn't installed: the substitute, the family named as missing
            var miss = Read(Svg("<text x=\"10px\" y=\"20px\" style=\"font-family:'NoSuchFontPS', 'No Such Font', sans-serif;font-size:16px;\">Abc</text>"));
            Check("missing font", miss != null && miss.Missing.SequenceEqual(new[] { "No Such Font" }) && miss.Objects[0].Font == EmfText.Substitute,
                  miss == null ? "nothing read" : string.Join(",", miss.Missing) + " " + miss.Objects[0].Font);

            // text only, or text with drawing
            var only = Read(Svg(l1));
            var mixed = Read(Svg(l1 + "<rect x=\"0\" y=\"0\" width=\"5\" height=\"5\" style=\"fill:black;\"/>"));
            Check("only text", only != null && only.OnlyText);
            Check("text with a shape", mixed != null && !mixed.OnlyText && mixed.Objects.Count == 1);

            // no text: nothing
            Check("no text", Read(Svg("<rect x=\"0\" y=\"0\" width=\"5\" height=\"5\"/>")) == null);

            // place: viewBox scaled onto mm (200 px = 52.92 mm), the baseline at y = 20 px = 5.29 mm
            var placed = Read(Svg("<text x=\"10px\" y=\"20px\" style=\"" + arial + "\">Abc</text>", "width=\"100mm\" height=\"50mm\" viewBox=\"0 0 200 100\""));
            if (placed != null)
            {
                var t = placed.Objects[0];
                // 16 user units = 8 mm em = 22.68 pt
                Check("viewBox scale", Math.Abs(t.Size - 22.68) < 0.05, t.Size.ToString(CultureInfo.InvariantCulture));
                Check("viewBox place", t.X > 4 && t.X < 6 && t.Y > 2 && t.Y < 5, t.X + "," + t.Y);
            }
            else Check("viewBox", false, "nothing read");

            // white text (Affinity's fill:white): on a black rect: inverted, the rect stays a picture
            const string white = "font-family:'Arial-BoldMT', 'Arial', sans-serif;font-weight:700;font-size:16px;fill:white;";
            var sale = Read(Svg("<rect x=\"0\" y=\"0\" width=\"150\" height=\"40\"/><text x=\"40px\" y=\"28px\" style=\"" + white + "\">SALE</text>"));
            Check("white on black", sale != null && sale.Objects.Count == 1 && Describe(sale.Objects[0]) == "Line Left \"SALE\" Arial 12pt bold:SALE inverted"
                  && !sale.OnlyText && sale.Rest != null, sale == null ? "nothing read" : Describe(sale.Objects[0]));
            // white characters inside a black line, nothing behind them: left out, the rest where it was
            string wi = "<text x=\"" + Xs("Abc Xyz Def", 10, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Abc <tspan style=\"fill:#ffffff\">Xyz</tspan> Def</text>";
            Expect("white inside", Svg(wi), "Line Left \"Abc\" Arial 12pt", "Line Left \"Def\" Arial 12pt");
            // the same as Affinity writes it: one <text> per colour in the line's group
            double xa = 10 + "Abc ".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16)), xd = 10 + "Abc Xyz ".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16));
            string wg = "<g><text x=\"" + Xs("Abc ", 10, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Abc </text>"
                      + "<text x=\"" + Xs("Xyz ", xa, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "fill:white;\">Xyz </text>"
                      + "<text x=\"" + Xs("Def", xd, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Def</text></g>";
            var grouped = SvgText.Read(Encoding.UTF8.GetBytes(Svg(wg)), null, new SvgQuirks { TextGroups = true });
            var gd = grouped == null ? new List<string>() : grouped.Objects.OrderBy(o => o.X).Select(Describe).ToList();
            Check("white inside, group", gd.SequenceEqual(new[] { "Line Left \"Abc\" Arial 12pt", "Line Left \"Def\" Arial 12pt" }), string.Join(" / ", gd));
            // white characters on a black highlight inside a black line: inverted there, the highlight gone
            double ax = 10 + "Abc ".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16));
            double xw = "Xyz".Sum(c => Advance(c, "Arial", System.Drawing.FontStyle.Regular, 16));
            string w3 = "<rect x=\"" + ax.ToString("0.###", CultureInfo.InvariantCulture) + "\" y=\"5\" width=\"" + xw.ToString("0.###", CultureInfo.InvariantCulture) + "\" height=\"19\"/>"
                      + "<text x=\"" + Xs("Abc Xyz", 10, 16, "Arial", false) + "\" y=\"20px\" style=\"" + arial + "\">Abc <tspan style=\"fill:rgb(255,255,255)\">Xyz</tspan></text>";
            var hl = Read(Svg(w3));
            Check("white highlight", hl != null && hl.OnlyText && hl.Objects.Count == 1 && Describe(hl.Objects[0]) == "Line Left \"Abc Xyz\" Arial 12pt inverted:Xyz",
                  hl == null ? "nothing read" : Describe(hl.Objects[0]) + " only text " + hl.OnlyText);

            // turned 90 degrees
            var turned = Read(Svg("<g transform=\"rotate(90 50 50)\"><text x=\"10px\" y=\"20px\" style=\"" + arial + "\">Abc</text></g>"));
            Check("turned", turned != null && Math.Abs(turned.Objects[0].Angle - 90) < 0.5, turned == null ? "" : turned.Objects[0].Angle.ToString(CultureInfo.InvariantCulture));

            Console.WriteLine("svgtext_test: " + (failures == 0 ? "all " + checks + " OK" : failures + " of " + checks + " failed"));
            return failures;
        }
    }
}
