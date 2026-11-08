// Formatted text from RTF (Word, WordPad, Outlook and others): the text with its font, size, bold, italic and
// underline as runs for a text object, without a RichTextBox. Read: the font table (names and character sets), \f,
// \fs, \b, \i, \ul (and its kinds), \plain, \v (hidden text left out), \par/\line (new lines), \tab, \cell/\row,
// \pard and \ql/\qc/\qr, \'hh in the font's or the document's code page, \uN with \uc fallback skipped, and the
// symbols Word writes as controls. Colours, spacing, lists, tables' shapes, pictures and objects are left out.
using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace QlLabels
{
    sealed class RtfDoc
    {
        public List<TextRun> Runs = new List<TextRun>();   // filled (font, size, bold, italic, underline); "\n" breaks
        public TextAlign Align = TextAlign.Left;          // when every paragraph has it, else Left
        public string Text { get { return string.Concat(Runs.Select(r => r.Text)); } }
    }

    static class RtfRuns
    {
        public const string DefaultFont = "Segoe UI";

        // Destinations whose text isn't part of the document.
        static readonly HashSet<string> Skipped = new HashSet<string>
        {
            "colortbl", "stylesheet", "info", "pict", "header", "footer", "headerl", "headerr", "headerf", "footerl",
            "footerr", "footerf", "listtable", "listoverridetable", "rsidtbl", "generator", "xmlnstbl", "themedata",
            "colorschememapping", "latentstyles", "datastore", "object", "fldinst", "filetbl", "revtbl", "mmathPr",
            "pgdsctbl", "nonshppict", "shp", "objdata", "footnote", "annotation", "bkmkstart", "bkmkend", "xe", "tc",
            "pntext", "pntxta", "pntxtb", "listtext",
        };

        // RTF that is only text (no pictures, objects, drawings or tables): it pastes as text even when the program
        // also offers a picture of it (Word's EMF).
        public static bool OnlyText(string rtf)
        {
            return rtf != null && !Regex.IsMatch(rtf, @"\\(pict|object|shp|shpinst|trowd)(?![a-z])");
        }

        sealed class Fmt
        {
            public int Font = -1;
            public double Size = 12;
            public bool Bold, Italic, Underline, Hidden;
            public Fmt Copy() { return (Fmt)MemberwiseClone(); }
        }

        sealed class Group
        {
            public Fmt Fmt;
            public bool Skip, FontTable, FontTableRoot;   // root: the onttbl group itself (its groups are entries)
            public int Uc;
        }

        sealed class FontEntry
        {
            public string Name = "";
            public int CodePage;   // 0: the document's
            public bool Done;      // the name's ';' was read
        }

        // The runs of an RTF document; missing: fonts not installed here (their runs get DefaultFont).
        public static RtfDoc Read(string rtf, out List<string> missing)
        {
            var doc = Parse(rtf);
            missing = new List<string>();
            var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var fonts = new InstalledFontCollection())
                foreach (var f in fonts.Families) installed.Add(f.Name);
            for (int i = 0; i < doc.Runs.Count; i++)
            {
                var r = doc.Runs[i];
                if (installed.Contains(r.Font)) continue;
                if (!missing.Contains(r.Font)) missing.Add(r.Font);
                doc.Runs[i] = new TextRun(r.Text, DefaultFont, r.Size, r.Bold, r.Italic, r.Underline);
            }
            return doc;
        }

        // The text only (the paragraphs as "\n").
        public static string PlainText(string rtf) { return Parse(rtf).Text; }

        // One text object: a text frame for several lines or words in another format, else a single-line text.
        public static TextObject Object(RtfDoc doc)
        {
            var t = new TextObject { Align = doc.Align };
            t.SetRuns(doc.Runs);
            t.Kind = t.Text.Contains("\n") || t.Runs != null ? TextKind.Frame : TextKind.Line;
            EditorRender.FitBox(t);
            return t;
        }

        static RtfDoc Parse(string rtf)
        {
            var doc = new RtfDoc();
            var fonts = new Dictionary<int, FontEntry>();
            int defaultFont = 0, docCodePage = 1252, ucSkip = 0;
            FontEntry entry = null;   // the font table entry being read
            var g = new Group { Fmt = new Fmt(), Uc = 1 };
            var stack = new Stack<Group>();
            var bytes = new List<byte>();
            var aligns = new HashSet<TextAlign>();
            TextAlign align = TextAlign.Left;
            bool paragraphHasText = false;

            Func<Fmt, string> fontName = f =>
            {
                FontEntry e;
                if (fonts.TryGetValue(f.Font >= 0 ? f.Font : defaultFont, out e) && e.Name.Length > 0) return e.Name;
                return DefaultFont;
            };
            // the last run's text grows in tail (one character at a time: a string would make long text quadratic)
            var tail = new StringBuilder();
            Action commit = () => { if (tail.Length > 0) doc.Runs[doc.Runs.Count - 1] = doc.Runs[doc.Runs.Count - 1].WithText(tail.ToString()); };
            Action<string> emit = s =>
            {
                if (s.Length == 0 || g.Skip) return;
                if (g.FontTable) { if (entry != null && !entry.Done && entry.Name.Length < 256) entry.Name += s; return; }
                if (g.Fmt.Hidden) return;
                if (s != "\n") paragraphHasText = true;
                var f = g.Fmt;
                var run = new TextRun(s, fontName(f), f.Size, f.Bold, f.Italic, f.Underline);
                var last = doc.Runs.Count > 0 ? doc.Runs[doc.Runs.Count - 1] : null;
                if (last != null && last.Font == run.Font && last.Size == run.Size && last.Bold == run.Bold && last.Italic == run.Italic && last.Underline == run.Underline)
                    tail.Append(s);
                else { commit(); doc.Runs.Add(run); tail.Clear().Append(s); }
            };
            Action flush = () =>
            {
                if (bytes.Count == 0) return;
                FontEntry e;
                int cp = fonts.TryGetValue(g.Fmt.Font >= 0 ? g.Fmt.Font : defaultFont, out e) && e.CodePage > 0 ? e.CodePage : docCodePage;
                Encoding enc;
                try { enc = Encoding.GetEncoding(cp); } catch (Exception) { enc = Encoding.GetEncoding(1252); }
                emit(enc.GetString(bytes.ToArray()));
                bytes.Clear();
            };
            Action endParagraph = () =>
            {
                if (paragraphHasText) aligns.Add(align);
                paragraphHasText = false;
            };

            int i = 0;
            while (i < rtf.Length)
            {
                char ch = rtf[i];
                if (ch == '{')
                {
                    flush();
                    stack.Push(g);
                    if (g.FontTableRoot) entry = null;   // a new entry
                    g = new Group { Fmt = g.Fmt.Copy(), Skip = g.Skip, FontTable = g.FontTable, Uc = g.Uc };
                    i++;
                    continue;
                }
                if (ch == '}')
                {
                    flush();
                    if (stack.Count > 0) g = stack.Pop();
                    i++;
                    continue;
                }
                if (ch == '\r' || ch == '\n') { i++; continue; }
                if (ch != '\\')
                {
                    if (ucSkip > 0) { ucSkip--; i++; continue; }
                    flush();
                    if (g.FontTable && ch == ';') { if (entry != null) entry.Done = true; i++; continue; }
                    emit(ch.ToString());
                    i++;
                    continue;
                }
                if (i + 1 >= rtf.Length) break;
                char next = rtf[i + 1];
                if (next == '\'')
                {
                    int v;
                    if (i + 4 <= rtf.Length && int.TryParse(rtf.Substring(i + 2, 2), System.Globalization.NumberStyles.HexNumber, null, out v))
                    {
                        if (ucSkip > 0) ucSkip--;
                        else bytes.Add((byte)v);
                    }
                    i += 4;
                    continue;
                }
                flush();
                if (next == '*') { g.Skip = true; i += 2; continue; }
                if (!char.IsLetter(next))
                {
                    if (ucSkip > 0) ucSkip--;
                    else if (next == '\\' || next == '{' || next == '}') emit(next.ToString());
                    else if (next == '~') emit(" ");
                    else if (next == '_') emit("‑");
                    else if (next == '\r' || next == '\n') { endParagraph(); emit("\n"); }
                    i += 2;
                    continue;
                }
                int j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                string word = rtf.Substring(i + 1, j - i - 1);
                int k = j;
                if (k < rtf.Length && (rtf[k] == '-' || char.IsDigit(rtf[k]))) { k++; while (k < rtf.Length && char.IsDigit(rtf[k])) k++; }
                int num;
                bool hasNum = int.TryParse(rtf.Substring(j, k - j), out num);
                if (k < rtf.Length && rtf[k] == ' ') k++;
                i = k;
                if (word == "fonttbl") { g.FontTable = g.FontTableRoot = true; entry = null; continue; }
                if (Skipped.Contains(word)) { g.Skip = true; continue; }
                if (g.FontTable)
                {
                    if (g.Skip) continue;
                    if (word == "f" && hasNum) { entry = new FontEntry(); fonts[num] = entry; }
                    else if (entry != null && word == "fcharset" && hasNum) entry.CodePage = CodePage(num, entry.CodePage);
                    else if (entry != null && word == "cpg" && hasNum) entry.CodePage = num;
                    else if (word == "u" && hasNum) { emit(((char)(num < 0 ? num + 65536 : num)).ToString()); ucSkip = g.Uc; }
                    continue;
                }
                if (g.Skip)
                {
                    if (word == "u" && hasNum) ucSkip = g.Uc;
                    continue;
                }
                bool on = !hasNum || num != 0;
                switch (word)
                {
                    case "ansicpg": if (hasNum) docCodePage = num; break;
                    case "deff": if (hasNum) defaultFont = num; break;
                    case "uc": if (hasNum) g.Uc = num; break;
                    case "u":
                        if (hasNum) { emit(((char)(num < 0 ? num + 65536 : num)).ToString()); ucSkip = g.Uc; }
                        break;
                    case "plain": g.Fmt = new Fmt(); break;
                    case "f": if (hasNum) g.Fmt.Font = num; break;
                    case "fs": if (hasNum && num > 0) g.Fmt.Size = Math.Min(400, Math.Max(2, num / 2.0)); break;
                    case "b": g.Fmt.Bold = on; break;
                    case "i": g.Fmt.Italic = on; break;
                    case "ulnone": g.Fmt.Underline = false; break;
                    case "ul": case "uld": case "uldash": case "uldashd": case "uldashdd": case "uldb": case "ulhwave":
                    case "ulldash": case "ulth": case "ulthd": case "ulthdash": case "ulthdashd": case "ulthdashdd":
                    case "ulthldash": case "ululdbwave": case "ulw": case "ulwave":
                        g.Fmt.Underline = on; break;
                    case "v": g.Fmt.Hidden = on; break;
                    case "pard": align = TextAlign.Left; break;
                    case "ql": case "qj": case "qd": align = TextAlign.Left; break;
                    case "qc": align = TextAlign.Center; break;
                    case "qr": align = TextAlign.Right; break;
                    case "par": case "sect": case "page": case "row": endParagraph(); emit("\n"); break;
                    case "line": emit("\n"); break;
                    case "tab": case "cell": emit("\t"); break;
                    case "emdash": emit("—"); break;
                    case "endash": emit("–"); break;
                    case "emspace": case "enspace": case "qmspace": emit(" "); break;
                    case "bullet": emit("•"); break;
                    case "lquote": emit("‘"); break;
                    case "rquote": emit("’"); break;
                    case "ldblquote": emit("“"); break;
                    case "rdblquote": emit("”"); break;
                }
            }
            flush();
            commit();
            endParagraph();
            // The last paragraph mark ends the document, it isn't an empty line; a cell's tab before a new line goes.
            Trim(doc.Runs);
            doc.Align = aligns.Count == 1 ? aligns.First() : TextAlign.Left;
            return doc;
        }

        static void Trim(List<TextRun> runs)
        {
            for (int i = 0; i < runs.Count; i++)
                if (runs[i].Text.Contains("\t\n")) runs[i] = runs[i].WithText(runs[i].Text.Replace("\t\n", "\n"));
            while (runs.Count > 0)
            {
                var r = runs[runs.Count - 1];
                string t = r.Text.TrimEnd('\n', '\t');
                if (t.Length > 0) { runs[runs.Count - 1] = r.WithText(t); break; }
                runs.RemoveAt(runs.Count - 1);
            }
        }

        // A font's character set as a code page (0: the document's).
        static int CodePage(int charset, int current)
        {
            switch (charset)
            {
                case 77: return 10000;
                case 128: return 932;
                case 129: return 949;
                case 134: return 936;
                case 136: return 950;
                case 161: return 1253;
                case 162: return 1254;
                case 163: return 1258;
                case 177: return 1255;
                case 178: return 1256;
                case 186: return 1257;
                case 204: return 1251;
                case 222: return 874;
                case 238: return 1250;
                default: return current;
            }
        }
    }
}
