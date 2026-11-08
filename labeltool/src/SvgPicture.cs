using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace QlLabels
{
    // SVG pictures through Windows' own Direct2D SVG renderer (ID2D1DeviceContext5, Windows 10 1703 and later; no DLL of
    // ours): kept as a vector and drawn at the size printed. Clean checks the file first: no DTD, no scripts, no
    // references to other files or the web (only "#id" and embedded "data:image/" pictures); the root gets a viewBox and
    // no size, so it fills any w x h; CSS in <style> becomes attributes (InlineStyles), text becomes paths (TextToPaths).
    // What Direct2D doesn't draw stays empty: text we can't convert, CSS rules we can't apply, filters, masks.
    static class SvgPicture
    {
        const int MaxBytes = 20 * 1024 * 1024;

        public static bool IsSvg(string path)
        {
            return Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
        }

        // The cleaned SVG of a file (InvalidDataException: no SVG).
        public static byte[] Read(string path)
        {
            if (new FileInfo(path).Length > MaxBytes) throw new InvalidDataException(Lang.T("The SVG file is too large."));
            return Clean(File.ReadAllBytes(path));
        }

        public static byte[] Clean(byte[] source)
        {
            var doc = new XmlDocument { XmlResolver = null, PreserveWhitespace = true };
            try
            {
                using (var ms = new MemoryStream(source))
                using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, MaxCharactersInDocument = MaxBytes }))
                    doc.Load(reader);
            }
            catch (XmlException ex) { throw new InvalidDataException(Lang.T("This is no SVG picture, or the file is damaged."), ex); }
            var root = doc.DocumentElement;
            if (root == null || root.LocalName != "svg") throw new InvalidDataException(Lang.T("This is no SVG picture, or the file is damaged."));
            foreach (var node in doc.ChildNodes.OfType<XmlNode>().Where(n => n.NodeType == XmlNodeType.DocumentType || n.NodeType == XmlNodeType.ProcessingInstruction).ToList())
                doc.RemoveChild(node);
            foreach (var el in root.SelectNodes("descendant-or-self::*").OfType<XmlElement>().ToList())
            {
                if (el.LocalName == "script" || el.LocalName == "foreignObject") { el.ParentNode.RemoveChild(el); continue; }
                foreach (var a in el.Attributes.OfType<XmlAttribute>().ToList())
                {
                    string v = a.Value.Trim();
                    if (a.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                        || (a.LocalName == "href" && !v.StartsWith("#") && !v.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)))
                        el.Attributes.Remove(a);
                }
            }
            InlineStyles(root);
            TextToPaths(root);
            // the picture's own size: its viewBox, else width and height (300 x 150 without them, as in browsers)
            double[] box = ViewBox(root.GetAttribute("viewBox"));
            if (box == null)
            {
                box = new[] { 0.0, 0.0, Length(root.GetAttribute("width"), 300), Length(root.GetAttribute("height"), 150) };
                root.SetAttribute("viewBox", string.Join(" ", box.Select(n => n.ToString("R", CultureInfo.InvariantCulture))));
            }
            root.SetAttribute("preserveAspectRatio", "none");
            root.RemoveAttribute("width");
            root.RemoveAttribute("height");
            using (var ms = new MemoryStream())
            {
                using (var w = XmlWriter.Create(ms, new XmlWriterSettings { Encoding = new UTF8Encoding(false), OmitXmlDeclaration = true })) doc.Save(w);
                return ms.ToArray();
            }
        }

        // ---------- CSS ----------

        // The SVG properties CSS may set as attributes (Direct2D reads these attributes, not CSS).
        static readonly HashSet<string> Properties = new HashSet<string>
        {
            "fill", "fill-opacity", "fill-rule", "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
            "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "opacity", "color", "display", "visibility", "overflow",
            "clip-path", "clip-rule", "mask", "filter", "stop-color", "stop-opacity", "font-family", "font-size", "font-weight",
            "font-style", "text-anchor", "letter-spacing"
        };

        sealed class Rule
        {
            public string Element, Id;
            public string[] Classes;
            public int Specificity, Order;
            public List<KeyValuePair<string, string>> Declarations;

            public bool Matches(XmlElement el)
            {
                if (Element != null && Element != "*" && Element != el.LocalName) return false;
                if (Id != null && Id != el.GetAttribute("id")) return false;
                if (Classes.Length == 0) return true;
                var own = el.GetAttribute("class").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                return Classes.All(c => own.Contains(c));
            }
        }

        // CSS in <style> (Illustrator and many other programs keep their colours there; Direct2D ignores it): the rules with
        // simple selectors (element, .class, #id, combined as in path.cls-1, and lists of those) become the elements'
        // attributes in CSS's order (specificity, then order; a style="..." attribute wins). Rules with other selectors and
        // @media stay in a <style> of their own (HasUnappliedCss); @font-face and @charset go.
        static void InlineStyles(XmlElement root)
        {
            var rules = new List<Rule>();
            var kept = new StringBuilder();
            foreach (var style in root.SelectNodes("descendant-or-self::*[local-name()='style']").OfType<XmlElement>().ToList())
            {
                ParseCss(style.InnerText, rules, kept);
                style.ParentNode.RemoveChild(style);
            }
            // Rules indexed by their id, else their first class, else their element (thousands of rules and elements
            // would otherwise be matched each against each); only the candidates are matched.
            var byId = new Dictionary<string, List<Rule>>();
            var byClass = new Dictionary<string, List<Rule>>();
            var byElement = new Dictionary<string, List<Rule>>();
            var everyElement = new List<Rule>();
            Action<Dictionary<string, List<Rule>>, string, Rule> add = (d, k, r) =>
            {
                List<Rule> l;
                if (!d.TryGetValue(k, out l)) d[k] = l = new List<Rule>();
                l.Add(r);
            };
            foreach (var r in rules)
                if (r.Id != null) add(byId, r.Id, r);
                else if (r.Classes.Length > 0) add(byClass, r.Classes[0], r);
                else if (r.Element != null && r.Element != "*") add(byElement, r.Element, r);
                else everyElement.Add(r);
            var none = new List<Rule>();
            Func<Dictionary<string, List<Rule>>, string, List<Rule>> get = (d, k) => { List<Rule> l; return d.TryGetValue(k, out l) ? l : none; };
            foreach (var el in root.SelectNodes("descendant-or-self::*").OfType<XmlElement>())
            {
                var set = new Dictionary<string, string>();
                var candidates = new HashSet<Rule>(everyElement);
                candidates.UnionWith(get(byElement, el.LocalName));
                if (el.HasAttribute("id")) candidates.UnionWith(get(byId, el.GetAttribute("id")));
                foreach (var c in el.GetAttribute("class").Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    candidates.UnionWith(get(byClass, c));
                foreach (var rule in candidates.Where(r => r.Matches(el)).OrderBy(r => r.Specificity).ThenBy(r => r.Order))
                    foreach (var d in rule.Declarations) set[d.Key] = d.Value;
                if (el.HasAttribute("style"))
                {
                    foreach (var d in Declarations(el.GetAttribute("style"))) set[d.Key] = d.Value;
                    el.RemoveAttribute("style");
                }
                foreach (var d in set) el.SetAttribute(d.Key, d.Value);
            }
            if (kept.Length > 0)
            {
                var style = root.OwnerDocument.CreateElement(root.Prefix, "style", root.NamespaceURI);
                style.InnerText = kept.ToString();
                root.PrependChild(style);
            }
        }

        // A cleaned picture still has CSS rules that couldn't become attributes.
        public static bool HasUnappliedCss(byte[] svg)
        {
            return Encoding.UTF8.GetString(svg).Contains("<style");
        }

        // What Direct2D leaves out of a cleaned picture (the editor's hint): "text", "filter", "mask", "css", then "font:<name>"
        // for each font replaced (TextToPaths); empty: it all prints as drawn. Kept per picture (the panel asks on every
        // layout).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], string[]> missing =
            new System.Runtime.CompilerServices.ConditionalWeakTable<byte[], string[]>();

        public static string[] Missing(byte[] svg)
        {
            if (svg == null) return new string[0];
            return missing.GetValue(svg, s =>
            {
                var doc = new XmlDocument { XmlResolver = null };
                try
                {
                    using (var ms = new MemoryStream(s))
                    using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                        doc.Load(reader);
                }
                catch (XmlException) { return new string[0]; }
                var found = new HashSet<string>();
                var fonts = new List<string>();
                foreach (XmlElement el in doc.SelectNodes("//*"))
                {
                    string font = el.GetAttribute("missing-font", QlNamespace);
                    if (font.Length > 0 && !fonts.Contains(font)) fonts.Add(font);
                    string name = el.LocalName;
                    if (name == "text") found.Add("text");
                    if (name == "filter" || el.HasAttribute("filter")) found.Add("filter");
                    if (name == "mask" || el.HasAttribute("mask")) found.Add("mask");
                    if (name == "style") found.Add("css");
                }
                return new[] { "text", "filter", "mask", "css" }.Where(found.Contains).Concat(fonts.Select(f => "font:" + f)).ToArray();
            });
        }

        static void ParseCss(string css, List<Rule> rules, StringBuilder kept)
        {
            css = Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
            int i = 0;
            while (i < css.Length)
            {
                int open = css.IndexOf('{', i);
                if (open < 0) break;
                string prelude = css.Substring(i, open - i).Trim();
                // at-rules without a block before it (@charset "UTF-8"; @import url(...);)
                for (int semi; prelude.StartsWith("@") && (semi = prelude.IndexOf(';')) >= 0; prelude = prelude.Substring(semi + 1).Trim())
                    if (!prelude.StartsWith("@charset", StringComparison.OrdinalIgnoreCase)) kept.Append(prelude.Substring(0, semi + 1));
                int depth = 1, end = open + 1;
                for (; end < css.Length && depth > 0; end++)
                    if (css[end] == '{') depth++; else if (css[end] == '}') depth--;
                string body = css.Substring(open + 1, Math.Max(0, end - open - 2));
                i = end;
                if (prelude.StartsWith("@"))
                {
                    if (!prelude.StartsWith("@font-face", StringComparison.OrdinalIgnoreCase)) kept.Append(prelude + "{" + body + "}");
                    continue;
                }
                var declarations = Declarations(body);
                var other = new List<string>();
                foreach (var selector in prelude.Split(',').Select(x => x.Trim()))
                {
                    var rule = Selector(selector);
                    if (rule == null) { other.Add(selector); continue; }
                    rule.Declarations = declarations;
                    rule.Order = rules.Count;
                    rules.Add(rule);
                }
                if (other.Count > 0) kept.Append(string.Join(",", other) + "{" + body + "}");
            }
        }

        // element, *, .class, #id and combinations without spaces (path.cls-1.cls-2); null: another selector
        static Rule Selector(string s)
        {
            var m = Regex.Match(s, @"^(\*|[A-Za-z][\w-]*)?((?:[.#][A-Za-z_-][\w-]*)*)$");
            if (s.Length == 0 || !m.Success) return null;
            var parts = Regex.Matches(m.Groups[2].Value, @"[.#][\w-]+").Cast<Match>().Select(x => x.Value).ToList();
            var ids = parts.Where(p => p[0] == '#').Select(p => p.Substring(1)).Distinct().ToList();
            if (ids.Count > 1) return null;
            var classes = parts.Where(p => p[0] == '.').Select(p => p.Substring(1)).ToArray();
            string element = m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : null;
            return new Rule
            {
                Element = element, Id = ids.FirstOrDefault(), Classes = classes,
                Specificity = ids.Count * 10000 + classes.Length * 100 + (element != null && element != "*" ? 1 : 0)
            };
        }

        // "fill:#c00; stroke-width: 2" -> the properties Direct2D knows; no references to other files or the web.
        static List<KeyValuePair<string, string>> Declarations(string body)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var part in body.Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon < 0) continue;
                string name = part.Substring(0, colon).Trim().ToLowerInvariant();
                string value = Regex.Replace(part.Substring(colon + 1), @"!\s*important", "", RegexOptions.IgnoreCase).Trim();
                if (!Properties.Contains(name) || value.Length == 0) continue;
                if (Regex.IsMatch(value, @"url\(", RegexOptions.IgnoreCase) && !Regex.IsMatch(value, @"^url\(\s*['""]?#", RegexOptions.IgnoreCase)) continue;
                list.Add(new KeyValuePair<string, string>(name, value));
            }
            return list;
        }

        // ---------- text as paths ----------

        // Direct2D draws no <text>. As a drawing program's "object to path" does, each text becomes the outlines of the
        // installed fonts (GDI+ GraphicsPath.AddString): a <g> in its place keeps its id, transform, fill and stroke, one
        // <path> per run (a <tspan> keeps its fill and stroke). Covered: x and y (and single dx, dy) on <text> and <tspan>,
        // font-family with its fallbacks, font-size, -weight, -style, text-anchor, letter-spacing, whitespace as browsers
        // treat it. Kept as text (Missing: "text"): textPath, writing modes, per-glyph positions (lists, rotate),
        // textLength. A font missing on this PC: a substitute, its name in the path's ql:missing-font.
        public const string QlNamespace = "urn:ql-unidrv:svg";
        const float Em = 1000;   // glyphs at 1000 px, then scaled: no hinting at small sizes
        static readonly string[] TextOnly = { "x", "y", "dx", "dy", "font-family", "font-size", "font-weight", "font-style", "text-anchor",
                                              "letter-spacing", "word-spacing", "rotate", "textLength", "lengthAdjust", "writing-mode", "space" };

        sealed class Run
        {
            public string Text;
            public List<XmlElement> Owners;   // the tspans from the outermost in
            public XmlElement Style;           // the innermost element: its fonts
            public double? X, Y;
            public double Dx, Dy;
        }

        // A piece of text as outlines at Em px, its top left at 0,0, and its advance (GDI+ is slow: a picture may have
        // thousands of texts with the same words).
        sealed class Outline
        {
            public PointF[] Points;   // null: no outline (spaces)
            public byte[] Types;
            public float Width;
            public FontStyle Style;   // the entries for a face (key starting with \0): the style found, its ascent in Width
        }

        static void TextToPaths(XmlElement root)
        {
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                // font-family lists already looked up (a FontFamily takes a while; a picture may have thousands of texts)
                var families = new Dictionary<string, KeyValuePair<FontFamily, string>>();
                var outlines = new Dictionary<string, Outline>();   // by font-family list, style and text
                g.PageUnit = GraphicsUnit.Pixel;
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                foreach (var text in root.SelectNodes("descendant::*[local-name()='text']").OfType<XmlElement>().ToList())
                {
                    if (text.ParentNode == null) continue;
                    XmlElement group;
                    try { group = TextGroup(text, g, format, families, outlines); }
                    catch (ArgumentException) { group = null; }
                    catch (ExternalException) { group = null; }   // GDI+
                    if (group != null) text.ParentNode.ReplaceChild(group, text);
                }
                foreach (var f in families.Values) f.Key.Dispose();
            }
        }

        // The text as a group of paths; null: something we don't convert.
        static XmlElement TextGroup(XmlElement text, Graphics g, StringFormat format, Dictionary<string, KeyValuePair<FontFamily, string>> families,
                                    Dictionary<string, Outline> outlines)
        {
            if (Inherited(text, "writing-mode") != null) return null;
            var runs = new List<Run> { new Run { Text = "", Owners = new List<XmlElement>(), Style = text } };   // the text's own position
            if (!Position(text, runs[0]) || !Collect(text, new List<XmlElement>(), text, runs)) return null;
            Spaces(text, runs);

            var doc = text.OwnerDocument;
            var group = doc.CreateElement(text.Prefix, "g", text.NamespaceURI);
            foreach (XmlAttribute a in text.Attributes)
                if (!TextOnly.Contains(a.LocalName)) group.SetAttributeNode((XmlAttribute)a.Clone());
            double x = 0, y = 0;
            var chunk = new List<KeyValuePair<XmlElement, List<PointF[]>>>();
            var chunkTypes = new List<List<byte[]>>();
            double chunkStart = 0;
            string chunkAnchor = null;
            Action endChunk = () =>
            {
                double shift = chunkAnchor == "middle" ? -(x - chunkStart) / 2 : chunkAnchor == "end" ? -(x - chunkStart) : 0;
                for (int c = 0; c < chunk.Count; c++)
                {
                    var d = new StringBuilder();
                    for (int k = 0; k < chunk[c].Value.Count; k++) PathData(d, chunk[c].Value[k], chunkTypes[c][k], shift);
                    if (d.Length > 0) chunk[c].Key.SetAttribute("d", d.ToString().Trim());
                    else chunk[c].Key.ParentNode.RemoveChild(chunk[c].Key);
                }
                chunk.Clear(); chunkTypes.Clear();
            };
            foreach (var run in runs)
            {
                if (run.X.HasValue || run.Y.HasValue || chunkAnchor == null)
                {
                    if (chunkAnchor != null) endChunk();
                    if (run.X.HasValue) x = run.X.Value;
                    if (run.Y.HasValue) y = run.Y.Value;
                    chunkStart = x + run.Dx;
                    chunkAnchor = Inherited(run.Style, "text-anchor") ?? "start";
                }
                x += run.Dx; y += run.Dy;
                if (run.Text.Length == 0) continue;

                double size = FontSize(run.Style);
                string list = Inherited(run.Style, "font-family") ?? "serif", missing;
                KeyValuePair<FontFamily, string> known;
                if (!families.TryGetValue(list, out known))
                {
                    var found = Family(list, out missing);
                    families[list] = known = new KeyValuePair<FontFamily, string>(found, missing);
                }
                var family = known.Key;
                missing = known.Value;
                {
                    // the style and its ascent per font-family list and weight/style (GDI+ calls; looked up once)
                    string weight = Inherited(run.Style, "font-weight"), slant = Inherited(run.Style, "font-style");
                    string faceKey = "\0" + list + "\0" + weight + "\0" + slant;
                    Outline face;
                    if (!outlines.TryGetValue(faceKey, out face))
                    {
                        var found = Style(family, weight, slant);
                        outlines[faceKey] = face = new Outline { Style = found, Width = (float)(family.GetCellAscent(found) * (double)Em / family.GetEmHeight(found)) };
                    }
                    var style = face.Style;
                    double k = size / Em, ascent = face.Width;
                    double spacing = Length(Inherited(run.Style, "letter-spacing") ?? "", 0);
                    var path = doc.CreateElement(text.Prefix, "path", text.NamespaceURI);
                    foreach (var owner in run.Owners)
                        foreach (XmlAttribute a in owner.Attributes)
                            if (!TextOnly.Contains(a.LocalName) && a.LocalName != "id") path.SetAttributeNode((XmlAttribute)a.Clone());
                    if (missing != null) path.SetAttributeNode(doc.CreateAttribute("ql", "missing-font", QlNamespace)).Value = missing;
                    group.AppendChild(path);
                    var points = new List<PointF[]>();
                    var types = new List<byte[]>();
                    // whole runs (GDI+ spacing), or glyph by glyph with letter-spacing
                    var pieces = new List<string>();
                    if (spacing == 0) pieces.Add(run.Text);
                    else { var e = StringInfo.GetTextElementEnumerator(run.Text); while (e.MoveNext()) pieces.Add((string)e.Current); }
                    Font font = null;
                    try
                    {
                        foreach (var piece in pieces)
                        {
                            string key = list + "\0" + (int)style + "\0" + piece;
                            Outline o;
                            if (!outlines.TryGetValue(key, out o))
                            {
                                o = new Outline();
                                if (font == null) font = new Font(family, Em, style, GraphicsUnit.Pixel);
                                using (var gp = new GraphicsPath(FillMode.Winding))
                                {
                                    gp.AddString(piece, family, (int)style, Em, PointF.Empty, format);
                                    if (gp.PointCount > 0) { o.Points = gp.PathPoints; o.Types = gp.PathTypes; }
                                }
                                o.Width = g.MeasureString(piece, font, PointF.Empty, format).Width;
                                outlines[key] = o;
                            }
                            if (o.Points != null)
                            {
                                var pts = new PointF[o.Points.Length];
                                for (int i = 0; i < pts.Length; i++)
                                    pts[i] = new PointF((float)(x + o.Points[i].X * k), (float)(y + (o.Points[i].Y - ascent) * k));
                                points.Add(pts);
                                types.Add(o.Types);
                            }
                            x += o.Width * k + spacing;
                        }
                    }
                    finally { if (font != null) font.Dispose(); }
                    chunk.Add(new KeyValuePair<XmlElement, List<PointF[]>>(path, points));
                    chunkTypes.Add(types);
                }
            }
            if (chunkAnchor != null) endChunk();
            return group;
        }

        // The text's runs in order; false: something we don't convert.
        static bool Collect(XmlElement el, List<XmlElement> owners, XmlElement style, List<Run> runs)
        {
            foreach (XmlNode n in el.ChildNodes)
            {
                if (n.NodeType == XmlNodeType.Text || n.NodeType == XmlNodeType.CDATA || n.NodeType == XmlNodeType.Whitespace
                    || n.NodeType == XmlNodeType.SignificantWhitespace)
                {
                    runs.Add(new Run { Text = n.Value, Owners = owners, Style = style });
                }
                else if (n.NodeType == XmlNodeType.Element)
                {
                    var child = (XmlElement)n;
                    if (child.LocalName == "title" || child.LocalName == "desc") continue;
                    if (child.LocalName != "tspan" && child.LocalName != "a") return false;
                    int start = runs.Count;
                    runs.Add(new Run { Text = "", Owners = owners, Style = style });   // carries the tspan's position
                    if (!Position(child, runs[start])) return false;
                    if (!Collect(child, owners.Concat(new[] { child }).ToList(), child, runs)) return false;
                }
            }
            return true;
        }

        // x, y, dx, dy of an element onto its first run: single numbers (px) only.
        static bool Position(XmlElement el, Run run)
        {
            if (el.HasAttribute("rotate") || el.HasAttribute("textLength")) return false;
            double v;
            foreach (var name in new[] { "x", "y", "dx", "dy" })
            {
                if (!el.HasAttribute(name)) continue;
                if (!Number(el.GetAttribute(name), out v)) return false;
                if (name == "x") run.X = v; else if (name == "y") run.Y = v; else if (name == "dx") run.Dx += v; else run.Dy += v;
            }
            return true;
        }

        static bool Number(string s, out double v)
        {
            s = s.Trim();
            if (s.EndsWith("px")) s = s.Substring(0, s.Length - 2);
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        // Whitespace as browsers treat it: line breaks go, tabs become spaces, runs of spaces one (xml:space="preserve": kept
        // apart from the line breaks); none at the start and end.
        static void Spaces(XmlElement text, List<Run> runs)
        {
            bool preserve = text.GetAttribute("xml:space") == "preserve";
            bool lastSpace = true;
            foreach (var run in runs)
            {
                string t = run.Text.Replace("\r", "").Replace("\n", preserve ? " " : "").Replace('\t', ' ');
                if (!preserve)
                {
                    var b = new StringBuilder();
                    foreach (char c in t)
                    {
                        if (c == ' ' && lastSpace) continue;
                        b.Append(c);
                        lastSpace = c == ' ';
                    }
                    t = b.ToString();
                }
                run.Text = t;
            }
            if (preserve) return;
            var last = runs.LastOrDefault(r => r.Text.Length > 0);
            if (last != null) last.Text = last.Text.TrimEnd(' ');
        }

        // A property from the element or its ancestors.
        static string Inherited(XmlElement el, string name)
        {
            for (var n = el; n != null; n = n.ParentNode as XmlElement)
                if (n.HasAttribute(name))
                {
                    string v = n.GetAttribute(name).Trim();
                    if (v != "inherit") return v;
                }
            return null;
        }

        // font-size in px: a number, px, pt, em or % of the parent's (16 px at the root, as in browsers)
        static double FontSize(XmlElement el)
        {
            if (el == null) return 16;
            double parent = el.ParentNode is XmlElement ? FontSize((XmlElement)el.ParentNode) : 16;
            string s = el.GetAttribute("font-size").Trim();
            if (s.Length == 0 || s == "inherit") return parent;
            double v;
            if (s.EndsWith("%") && double.TryParse(s.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return parent * v / 100;
            if (s.EndsWith("em") && !s.EndsWith("rem") && double.TryParse(s.Substring(0, s.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
                return parent * v;
            double px = Length(s, -1);
            return px > 0 ? px : parent;
        }

        // The first installed font of a font-family list (generic names: Windows' usual fonts); none installed: Arial.
        // missing: the first named font that isn't installed, when a substitute is used.
        static FontFamily Family(string list, out string missing)
        {
            missing = null;
            foreach (var entry in list.Split(','))
            {
                string name = entry.Trim().Trim('"', '\'').Trim();
                if (name.Length == 0) continue;
                string generic;
                bool isGeneric = Generic.TryGetValue(name.ToLowerInvariant(), out generic);
                try { return new FontFamily(isGeneric ? generic : name); }
                catch (ArgumentException) { if (missing == null && !isGeneric) missing = name; }
            }
            return new FontFamily("Arial");
        }

        static readonly Dictionary<string, string> Generic = new Dictionary<string, string>
        {
            { "serif", "Times New Roman" }, { "sans-serif", "Arial" }, { "monospace", "Courier New" }, { "cursive", "Comic Sans MS" },
            { "fantasy", "Impact" }, { "system-ui", "Segoe UI" }
        };

        static FontStyle Style(FontFamily family, string weight, string style)
        {
            int w;
            bool bold = weight == "bold" || weight == "bolder" || (int.TryParse(weight, out w) && w >= 600);
            bool italic = style == "italic" || style == "oblique";
            var s = (bold ? FontStyle.Bold : FontStyle.Regular) | (italic ? FontStyle.Italic : FontStyle.Regular);
            foreach (var t in new[] { s, s & ~FontStyle.Italic, s & ~FontStyle.Bold, FontStyle.Regular, FontStyle.Bold, FontStyle.Italic })
                if (family.IsStyleAvailable(t)) return t;
            return FontStyle.Regular;
        }

        // GDI+ path points (0 start, 1 line, 3 Bezier; 0x80 closes) as SVG path data, shifted by dx.
        static void PathData(StringBuilder d, PointF[] pts, byte[] types, double dx)
        {
            Action<PointF> p = q => { Coordinate(d, q.X + dx); d.Append(' '); Coordinate(d, q.Y); };
            for (int i = 0; i < pts.Length; i++)
            {
                int t = types[i] & 7;
                if (t == 0) { d.Append('M'); p(pts[i]); d.Append(' '); }
                else if (t == 3 && i + 2 < pts.Length) { d.Append('C'); p(pts[i]); d.Append(' '); p(pts[i + 1]); d.Append(' '); p(pts[i + 2]); d.Append(' '); i += 2; }
                else { d.Append('L'); p(pts[i]); d.Append(' '); }
                if ((types[i] & 0x80) != 0) d.Append("Z ");
            }
        }

        // A coordinate with up to two decimals, as Math.Round(v, 2).ToString("0.##") writes it, without its cost (a
        // picture with thousands of texts has a million of them).
        static void Coordinate(StringBuilder d, double v)
        {
            double r = Math.Round(v, 2);
            if (double.IsNaN(r) || double.IsInfinity(r) || Math.Abs(r) >= 1e13) { d.Append(r.ToString("0.##", CultureInfo.InvariantCulture)); return; }
            long n = (long)Math.Round(r * 100);
            if (n == 0) { d.Append('0'); return; }
            if (n < 0) { d.Append('-'); n = -n; }
            d.Append(n / 100);
            int f = (int)(n % 100);
            if (f == 0) return;
            d.Append('.').Append((char)('0' + f / 10));
            if (f % 10 != 0) d.Append((char)('0' + f % 10));
        }

        // The picture's own size in mm, read from the source before Clean: width and height as a browser takes them
        // (mm, cm, in, pt, pc; px or no unit = 1/96 inch), else the viewBox in px; null when that gives nothing or it
        // can't be read.
        public static SizeF? SizeMm(byte[] source)
        {
            if (source == null) return null;
            try
            {
                int start = 0;
                while (start < source.Length && (source[start] == ' ' || source[start] == '\t' || source[start] == '\r' || source[start] == '\n')) start++;
                using (var ms = new MemoryStream(source, start, source.Length - start))
                using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element || reader.LocalName != "svg") return null;
                    var box = ViewBox(reader.GetAttribute("viewBox") ?? "");
                    double w = Length(reader.GetAttribute("width") ?? "", box != null ? box[2] : 0);
                    double h = Length(reader.GetAttribute("height") ?? "", box != null ? box[3] : 0);
                    if (w <= 0 || h <= 0) return null;
                    return new SizeF((float)(w * 25.4 / 96), (float)(h * 25.4 / 96));
                }
            }
            catch (Exception) { return null; }
        }

        // Width and height of a cleaned picture (its viewBox), for its aspect ratio.
        public static SizeF Size(byte[] svg)
        {
            var doc = new XmlDocument { XmlResolver = null };
            using (var ms = new MemoryStream(svg))
            using (var reader = XmlReader.Create(ms, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
                doc.Load(reader);
            var box = ViewBox(doc.DocumentElement.GetAttribute("viewBox"));
            return box == null ? new SizeF(300, 150) : new SizeF((float)box[2], (float)box[3]);
        }

        static double[] ViewBox(string s)
        {
            var parts = s.Split(new[] { ' ', ',', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 4) return null;
            var n = new double[4];
            for (int i = 0; i < 4; i++)
                if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out n[i])) return null;
            return n[2] > 0 && n[3] > 0 ? n : null;
        }

        // An SVG length in px (96 per inch); a percentage or a missing value: fallback.
        static double Length(string s, double fallback)
        {
            s = s.Trim();
            if (s.Length == 0 || s.EndsWith("%")) return fallback;
            string[] units = { "px", "pt", "pc", "mm", "cm", "in", "em", "ex" };
            double[] px = { 1, 96 / 72.0, 16, 96 / 25.4, 96 / 2.54, 96, 16, 8 };
            double k = 1;
            for (int i = 0; i < units.Length; i++)
                if (s.EndsWith(units[i], StringComparison.OrdinalIgnoreCase)) { k = px[i]; s = s.Substring(0, s.Length - 2).Trim(); break; }
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 ? v * k : fallback;
        }

        // ---------- drawing through Direct2D ----------

        // The picture stretched to w x h pixels on a transparent background (null: Direct2D couldn't draw it). Any thread.
        public static Bitmap Render(byte[] svg, int w, int h)
        {
            if (svg == null || w < 1 || h < 1) return null;
            try { return Draw(svg, w, h); }
            catch (Exception) { return null; }   // also out of memory: the caller draws the preview
        }

        [DllImport("d2d1.dll")] static extern int D2D1CreateFactory(int type, ref Guid riid, IntPtr options, out IntPtr factory);
        [DllImport("ole32.dll")] static extern int CoCreateInstance(ref Guid clsid, IntPtr outer, int context, ref Guid iid, out IntPtr obj);
        [DllImport("shlwapi.dll")] static extern IntPtr SHCreateMemStream(byte[] data, int size);

        [StructLayout(LayoutKind.Sequential)] struct TargetProperties { public int Type, Format, Alpha; public float DpiX, DpiY; public int Usage, MinLevel; }
        [StructLayout(LayoutKind.Sequential)] struct SizeF2 { public float W, H; }
        [StructLayout(LayoutKind.Sequential)] struct ColorF { public float R, G, B, A; }

        delegate int QueryInterfaceCall(IntPtr self, ref Guid iid, out IntPtr obj);
        delegate int CreateWicTargetCall(IntPtr self, IntPtr wicBitmap, ref TargetProperties p, out IntPtr target);
        delegate int CreateWicBitmapCall(IntPtr self, int w, int h, ref Guid format, int option, out IntPtr bitmap);
        delegate void BeginDrawCall(IntPtr self);
        delegate void ClearCall(IntPtr self, ref ColorF color);
        delegate int EndDrawCall(IntPtr self, out long tag1, out long tag2);
        delegate int CreateSvgCall(IntPtr self, IntPtr stream, SizeF2 viewport, out IntPtr doc);
        delegate void DrawSvgCall(IntPtr self, IntPtr doc);
        delegate int CopyPixelsCall(IntPtr self, IntPtr rect, int stride, int size, IntPtr buffer);

        // vtable slots (Windows SDK d2d1.h, d2d1_1.h, d2d1_3.h, wincodec.h)
        const int FactoryCreateWicBitmapRenderTarget = 13, WicFactoryCreateBitmap = 17, WicBitmapCopyPixels = 7;
        const int TargetClear = 47, TargetBeginDraw = 48, TargetEndDraw = 49;
        const int Context5CreateSvgDocument = 115, Context5DrawSvgDocument = 116;

        static T Slot<T>(IntPtr obj, int index) where T : class
        {
            IntPtr table = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(table, index * IntPtr.Size), typeof(T)) as T;
        }

        static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }

        static Bitmap Draw(byte[] svg, int w, int h)
        {
            IntPtr factory = IntPtr.Zero, wic = IntPtr.Zero, wicBitmap = IntPtr.Zero, target = IntPtr.Zero, context = IntPtr.Zero,
                   stream = IntPtr.Zero, doc = IntPtr.Zero;
            try
            {
                var iidFactory = new Guid("06152247-6f50-465a-9245-118bfd3b6007");   // ID2D1Factory
                Check(D2D1CreateFactory(0 /* single threaded */, ref iidFactory, IntPtr.Zero, out factory));
                var clsidWic = new Guid("cacaf262-9370-4615-a13b-9f5539da4c0a");     // CLSID_WICImagingFactory
                var iidWic = new Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70");
                Check(CoCreateInstance(ref clsidWic, IntPtr.Zero, 1 /* in process */, ref iidWic, out wic));
                var pbgra = new Guid("6fddc324-4e03-4bfe-b185-3d77768dc910");        // GUID_WICPixelFormat32bppPBGRA
                Check(Slot<CreateWicBitmapCall>(wic, WicFactoryCreateBitmap)(wic, w, h, ref pbgra, 1 /* cache on load */, out wicBitmap));
                var p = new TargetProperties { Type = 1 /* software */, Format = 87 /* B8G8R8A8 */, Alpha = 1 /* premultiplied */, DpiX = 96, DpiY = 96 };
                Check(Slot<CreateWicTargetCall>(factory, FactoryCreateWicBitmapRenderTarget)(factory, wicBitmap, ref p, out target));
                var iidContext5 = new Guid("7836d248-68cc-4df6-b9e8-de991bf62eb7");   // ID2D1DeviceContext5
                Check(Slot<QueryInterfaceCall>(target, 0)(target, ref iidContext5, out context));
                stream = SHCreateMemStream(svg, svg.Length);
                if (stream == IntPtr.Zero) throw new OutOfMemoryException();
                Check(Slot<CreateSvgCall>(context, Context5CreateSvgDocument)(context, stream, new SizeF2 { W = w, H = h }, out doc));
                Slot<BeginDrawCall>(context, TargetBeginDraw)(context);
                var clear = new ColorF();
                Slot<ClearCall>(context, TargetClear)(context, ref clear);
                Slot<DrawSvgCall>(context, Context5DrawSvgDocument)(context, doc);
                long tag1, tag2;
                Check(Slot<EndDrawCall>(context, TargetEndDraw)(context, out tag1, out tag2));
                var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                int hr = Slot<CopyPixelsCall>(wicBitmap, WicBitmapCopyPixels)(wicBitmap, IntPtr.Zero, data.Stride, data.Stride * h, data.Scan0);
                bmp.UnlockBits(data);
                if (hr < 0) { bmp.Dispose(); Check(hr); }
                return bmp;
            }
            finally
            {
                foreach (var com in new[] { doc, stream, context, target, wicBitmap, wic, factory })
                    if (com != IntPtr.Zero) Marshal.Release(com);
            }
        }
    }
}
