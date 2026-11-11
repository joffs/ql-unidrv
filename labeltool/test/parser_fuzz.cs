// Fuzz test for the parsers that read data from other programs (clipboard, drag and drop, files): EmfPicture
// (Check, What, SizeMm, Render), EmfText (Read, Objects, Repair), RtfRuns (Read, Object, PlainText), SvgPicture (Clean,
// Size, Missing, Render), EditorClipboard (Read, Dib), and label files (LabelDoc.FromJson, then saved, copied, drawn and
// checked; values in the JSON changed, 3 s per file). Inputs: the synthetic fixtures of clipboard_test/editor_test
// (EMFs drawn with GDI, Word-like RTF, SVG with CSS and text, DIBs), mutated with a fixed seed: bits flipped, numbers
// at record and header offsets overwritten (0, -1, int.MaxValue, NaN, ...), cut off, RTF/SVG tokens inserted; plus a
// few hand-made worst cases (deep nesting, long text). Passes when no call throws anything but InvalidDataException
// (what the editor treats as "not readable") and none takes longer than 1 s. Changes nothing, no clipboard, no printer.
// Usage: labeltool\test\parser_fuzz.ps1 [-Count 3000] [-Seed 20261108]   (builds and runs it; exit 1 on a finding)
//        labeltool\test\parser_fuzz.ps1 -Dumps <folder> [-Count 20]: the saved clipboard dumps (out\clipboard: one folder
//        per copy, formats.txt + one file per format) instead, each pasted as saved and then Count times with one of its
//        formats mutated (cut off, bits flipped, numbers at record and chunk offsets overwritten): the whole paste as
//        QL Label Maker makes it (read, text import, layers, pictures) and the objects drawn; 8 s per case
//        labeltool\build.cmd runs it quick: parser_fuzz.exe 200 20261108 --quick (no hand-made worst cases)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class ParserFuzz
    {
        const int LimitMs = 1000, LayersLimitMs = 4000;
        static int findings, calls;
        static long slowestMs; static string slowest = "";
        static bool warming;
        static readonly Dictionary<string, long> totals = new Dictionary<string, long>();   // ms per entry point
        static readonly Dictionary<string, int> seen = new Dictionary<string, int>();   // one line per kind of finding

        sealed class Source : IClipSource
        {
            readonly Dictionary<string, object> data = new Dictionary<string, object>();
            public Source Add(string format, object value) { data[format] = value; return this; }
            public bool Has(string format) { return data.ContainsKey(format); }
            public object Get(string format) { object o; return data.TryGetValue(format, out o) ? o : null; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            bool quick = args.Contains("--quick");   // build.cmd's run: mutated inputs only
            args = args.Where(a => a != "--quick").ToArray();
            int di = Array.IndexOf(args, "--dumps");
            string dumps = di >= 0 && di + 1 < args.Length ? args[di + 1] : null;
            if (di >= 0) args = args.Where((a, i) => i != di && i != di + 1).ToArray();
            int count = args.Length > 0 ? int.Parse(args[0]) : 3000;
            int seed = args.Length > 1 ? int.Parse(args[1]) : 20261108;
            string dump = args.Length > 2 ? args[2] : null;   // folder for the inputs of findings
            if (dump != null) Directory.CreateDirectory(dump);
            Lang.Force("en");
            var rnd = new Random(seed);
            if (dumps != null) return Dumps(dumps, count, seed, rnd, dump);
            var emfs = EmfFixtures();
            var rtfs = RtfFixtures();
            var svgs = SvgFixtures();
            var dibs = DibFixtures();
            var labels = LabelFixtures();
            var cmxs = CmxFixtures();
            Console.WriteLine("parser_fuzz: seed " + seed + ", " + count + " inputs per kind, fixtures " + emfs.Count + " EMF, " + rtfs.Count + " RTF, " + svgs.Count + " SVG, "
                              + dibs.Count + " DIB, " + labels.Count + " labels, " + cmxs.Count + " CMX");

            // warm-up, not timed: the first calls load fonts, GDI+ and Direct2D and compile the code (two seconds)
            warming = true;
            Emf(emfs[0], null, "warm-up"); Rtf(rtfs[0], null, "warm-up"); Svg(svgs[2], null, "warm-up"); Dib(dibs[0], null, "warm-up"); Label(labels[0], null, "warm-up");
            warming = false;
            // the fixtures themselves must be fine
            foreach (var e in emfs) Emf(e, dump, "fixture");
            foreach (var r in rtfs) Rtf(r, dump, "fixture");
            foreach (var s in svgs) Svg(s, dump, "fixture");
            foreach (var l in labels) Label(l, dump, "fixture");
            labelEmf = emfs[5];   // a rectangle with a text on it: a whole label
            foreach (var x in cmxs) Corel(x, emfs[0], dump, "fixture");
            // the label splits into the rectangle below and the text on it
            var layers = EmfLayers.Split(labelEmf);
            if (layers == null || layers.Pictures.Count != 1 || layers.Pictures[0].Level != 0 || layers.Texts.Count != 1 || layers.Texts[0].Key != 1)
            { findings++; Console.WriteLine("FINDING: the label fixture doesn't split into a picture and a text above it"); }
            // and the CMX fixtures must read as made, or the fuzz would only shake the RIFF header
            var first = Cmx.Read(cmxs[0]);
            if (first.Count != 1 || first[0].Text != "Hallo" || first[0].Font != "Arial" || first[0].LineSpacingPct != 87 || first[0].Glyphs.Count != 1
                || Cmx.Read(cmxs[1])[0].Text != "Ha\nllo" || Cmx.Read(cmxs[1])[0].FrameWidthMm < 26.35 || Cmx.Read(cmxs[2]).Count != 2
                || Cmx.Read(cmxs[4])[0].Text != "Hal\nlo" || Cmx.Read(cmxs[4])[0].Paragraphs.Count != 2 || Cmx.Read(cmxs[4])[0].Paragraphs[1].Align != 2
                || Cmx.Read(cmxs[4])[0].Paragraphs[1].CharSpacingPct != 20 || Cmx.Read(cmxs[4])[0].FrameHeightMm != 20 || !Cmx.Read(cmxs[4])[0].Frame)
            { findings++; Console.WriteLine("FINDING: the CMX fixtures don't read as made"); }
            // worst cases made by hand (not in the quick run: some take a second by design)
            if (!quick)
            {
                int h = 0;
                foreach (var r in HardRtf()) Rtf(r, dump, "hand-rtf#" + h++);
                h = 0;
                foreach (var s in HardSvg()) Svg(s, dump, "hand-svg#" + h++);
                h = 0;
                foreach (var e in HardEmf(emfs[0])) Emf(e, dump, "hand-emf#" + h++);
                h = 0;
                foreach (var l in HardLabels(labels[0])) Label(l, dump, "hand-label#" + h++);
            }

            for (int i = 0; i < count; i++)
            {
                Emf(MutateEmf(rnd, emfs[rnd.Next(emfs.Count)]), dump, "emf#" + i);
                Rtf(MutateText(rnd, rtfs[rnd.Next(rtfs.Count)], RtfTokens), dump, "rtf#" + i);
                Svg(Encoding.UTF8.GetBytes(MutateText(rnd, Encoding.UTF8.GetString(svgs[rnd.Next(svgs.Count)]), SvgTokens)), dump, "svg#" + i);
                if (i % 4 == 0) Svg(MutateBytes(rnd, svgs[rnd.Next(svgs.Count)]), dump, "svgbytes#" + i);
                Dib(MutateDib(rnd, dibs[rnd.Next(dibs.Count)]), dump, "dib#" + i);
                Label(MutateLabel(rnd, labels[rnd.Next(labels.Count)]), dump, "label#" + i);
                Corel(MutateCmx(rnd, cmxs[rnd.Next(cmxs.Count)]), emfs[0], dump, "cmx#" + i);
            }
            Console.WriteLine("parser_fuzz: " + calls + " calls, slowest " + slowestMs + " ms (" + slowest + "); ms per entry point: "
                              + string.Join(", ", totals.OrderByDescending(t => t.Value).Select(t => t.Key + " " + t.Value)));
            foreach (var kv in seen.OrderBy(k => k.Key)) Console.WriteLine("  " + kv.Value + "x " + kv.Key);
            Console.WriteLine(findings == 0 ? "parser_fuzz: all OK" : "parser_fuzz: " + findings + " finding(s)");
            return findings == 0 ? 0 : 1;
        }

        // ---------- entry points ----------

        static void Emf(byte[] b, string dump, string name)
        {
            Call("EmfPicture.What", name, b, dump, "emf", () => EmfPicture.What(b));
            Call("EmfPicture.SizeMm", name, b, dump, "emf", () => EmfPicture.SizeMm(b));
            Call("EmfText.Read+Objects", name, b, dump, "emf", () =>
            {
                List<string> missing;
                foreach (var p in EmfText.Read(b, out missing)) EmfText.Objects(p);
            });
            Call("EmfText.Repair", name, b, dump, "emf", () => { var r = EmfText.Repair(b); if (r != b) EmfPicture.Check(r); });
            Call("EditorClipboard.Read(EMF)", name, b, dump, "emf", () => EditorClipboard.Read(new Source().Add(EditorClipboard.EmfFormat, b)));
            // a whole label's layers: drawn item by item, so it has its own time budget (3 s) and limit
            Call("EmfLayers.Split", name, b, dump, "emf", () => EmfLayers.Split(b), LayersLimitMs);
        }

        // CorelDRAW's CMX: read alone, and laid over the EMF fixture "Hallo" as a paste from CorelDRAW does.
        static readonly ImportSource corel = new ImportSource { Program = SourceProgram.CorelDraw };
        static byte[] labelEmf;

        static void Corel(byte[] cmx, byte[] emf, string dump, string name)
        {
            Call("Cmx.Read", name, cmx, dump, "cmx", () => Cmx.Read(cmx));
            Call("TextImport.FromEmf(CMX)", name, cmx, dump, "cmx", () =>
            {
                List<string> missing; string how;
                TextImport.FromEmf(new ClipContent { Emf = emf, Cmx = cmx, Source = corel }, out missing, out how);
            });
            Call("TextImport.FromLabel(CMX)", name, cmx, dump, "cmx", () =>
            {
                List<string> missing; string how;
                TextImport.FromLabel(new ClipContent { Emf = labelEmf, Cmx = cmx, Source = corel }, out missing, out how);
            }, LayersLimitMs);
        }

        static void Rtf(string s, string dump, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            Call("RtfRuns.Read+Object", name, bytes, dump, "rtf", () =>
            {
                List<string> missing;
                RtfRuns.Object(RtfRuns.Read(s, out missing));
            });
            Call("RtfRuns.PlainText", name, bytes, dump, "rtf", () => RtfRuns.PlainText(s));
            Call("EditorClipboard.Read(RTF)", name, bytes, dump, "rtf", () => EditorClipboard.Read(new Source().Add(DataFormats.Rtf, s).Add(DataFormats.UnicodeText, "x")));
        }

        static void Svg(byte[] b, string dump, string name)
        {
            Call("SvgText.Read", name, b, dump, "svg", () =>
            {
                foreach (var q in new[] { SvgQuirks.None, AffinityClip.Quirks })
                {
                    var r = SvgText.Read(b, null, q);
                    if (r != null) InRange(r.Objects);
                }
            });
            Call("SvgPicture.Clean+Size+Missing", name, b, dump, "svg", () =>
            {
                var c = SvgPicture.Clean(b);
                SvgPicture.Size(c);
                SvgPicture.Missing(c);
                SvgPicture.HasUnappliedCss(c);
            });
            Call("EditorClipboard.Read(SVG)", name, b, dump, "svg", () => EditorClipboard.Read(new Source().Add("image/svg+xml", b)));
        }

        static void Dib(byte[] b, string dump, string name)
        {
            Call("EditorClipboard.Dib", name, b, dump, "dib", () => { using (EditorClipboard.Dib(b)) { } });
        }

        // A label file (.qllabel) as QL Label Maker and QlLabelPrint open it (security review 2026-11-09, L5): reading it
        // may fail only with InvalidDataException ("damaged"), and a file that opens must save, copy, draw and check
        // without an exception.
        static readonly LabelLayout labelLayout = LabelLayout.Estimate(new LabelChoice { Name = "Fuzz 62", Media = MediaTable.ById(259), LengthMm = 70, FeedMm = 3, IsFormat = true }, false, false);

        static void Label(string s, string dump, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            Call("LabelDoc.FromJson+Render", name, bytes, dump, "qllabel", () =>
            {
                var d = LabelDoc.FromJson(s);
                LabelDoc.FromJson(d.ToJson());
                d.Clone();
                var now = new DateTime(2026, 11, 9, 23, 0, 0);
                using (EditorRender.Render(d, labelLayout, now)) { }
                foreach (var o in d.Objects)
                {
                    var b = o as BarcodeObject;
                    if (b != null) EditorRender.Problem(b, labelLayout, now);
                    var t = o as TextObject;
                    if (t != null) EditorRender.Overflows(t);
                }
            }, 3000);   // opening a file happens once (a paste or a key press has 1 s): 2,000 objects take about 1.3 s
        }

        // The worker thread the calls run on (a new one after a hang; the hanging one is left behind).
        sealed class Worker
        {
            readonly AutoResetEvent start = new AutoResetEvent(false), finished = new AutoResetEvent(false);
            Action job;
            public Exception Error;
            public Worker()
            {
                var t = new Thread(() =>
                {
                    while (true)
                    {
                        start.WaitOne();
                        Error = null;
                        try { job(); }
                        catch (InvalidDataException) { }
                        catch (Exception ex) { Error = ex; }
                        finished.Set();
                    }
                }, 1024 * 1024);   // the UI thread's stack (the exe's default): what overflows here ends QL Label Maker
                t.IsBackground = true;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
            public void Start(Action a) { job = a; start.Set(); }
            public bool Wait(int ms) { return finished.WaitOne(ms); }
        }
        static Worker worker = new Worker();

        // One call on the worker thread: InvalidDataException is fine, anything else and more than 1 s (limit) are findings.
        static void Call(string what, string name, byte[] input, string dump, string ext, Action a, int limit = LimitMs)
        {
            calls++;
            var sw = Stopwatch.StartNew();
            worker.Start(a);
            bool done = worker.Wait(limit);
            if (!done) done = worker.Wait(10000);   // a hang still ends (or is left behind) after 11 s
            long ms = sw.ElapsedMilliseconds;
            Exception error = done ? worker.Error : null;
            if (!done) worker = new Worker();
            if (warming) return;
            long sum; totals.TryGetValue(what, out sum); totals[what] = sum + ms;
            if (ms > slowestMs) { slowestMs = ms; slowest = what + " " + name + " (" + input.Length + " bytes)"; }
            string finding = null;
            if (ms > limit) finding = what + ": " + (done ? "took " + ms + " ms" : "HANG (> 11 s)");
            else if (error != null) finding = what + ": " + error.GetType().Name + " " + Where(error);
            if (finding == null) return;
            findings++;
            string key = Kind(finding);
            int n;
            seen.TryGetValue(key, out n);
            seen[key] = n + 1;
            if (n < 3) Console.WriteLine("FINDING " + name + " (" + input.Length + " bytes): " + finding);
            if (dump != null && n < 3) File.WriteAllBytes(Path.Combine(dump, name.Replace('#', '-') + "." + ext), input);
        }

        static string Where(Exception ex)
        {
            var frame = new StackTrace(ex, true).GetFrames();
            var own = frame == null ? null : frame.FirstOrDefault(f => f.GetMethod() != null && f.GetMethod().DeclaringType != null
                                                                      && f.GetMethod().DeclaringType.Namespace == "QlLabels");
            return own == null ? "" : "at " + own.GetMethod().DeclaringType.Name + "." + own.GetMethod().Name + (own.GetFileLineNumber() > 0 ? ":" + own.GetFileLineNumber() : "");
        }

        // the finding without its numbers (ms), to count the same kind once
        static string Kind(string s) { return System.Text.RegularExpressions.Regex.Replace(s, @"took \d+ ms", "took > 1 s"); }

        // ---------- the saved clipboard dumps ----------

        const int DumpLimitMs = 8000;   // a whole label: read, split into layers, drawn (the largest dumps take about 2 s)

        static int Dumps(string folder, int perDump, int seed, Random rnd, string dump)
        {
            var dirs = Directory.GetDirectories(folder).Where(d => File.Exists(Path.Combine(d, "formats.txt"))).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList();
            Console.WriteLine("parser_fuzz: seed " + seed + ", " + dirs.Count + " dumps in " + folder + ", " + perDump + " mutations each");
            warming = true;
            if (dirs.Count > 0) PasteDump(DumpFormats(dirs[0]), null, "warm-up", "warm-up");
            warming = false;
            foreach (var d in dirs)
            {
                var formats = DumpFormats(d);
                string name = Path.GetFileName(d);
                PasteDump(formats, dump, name + "#as-saved", "as saved");
                // a format the paste reads; the largest (the EMF, CMX or SVG) every other time
                var parts = formats.Where(f => f.Data.Length > 0 && f.Wanted != null).ToList();
                if (parts.Count == 0) continue;
                var largest = parts.OrderByDescending(x => x.Data.Length).First();
                for (int i = 0; i < perDump; i++)
                {
                    var f = rnd.Next(2) == 0 ? largest : parts[rnd.Next(parts.Count)];
                    var mutated = formats.Select(x => x == f ? x.With(MutateFormat(rnd, x)) : x).ToList();
                    PasteDump(mutated, dump, name + "#" + i, f.Name);
                }
            }
            Console.WriteLine("parser_fuzz: " + calls + " calls, slowest " + slowestMs + " ms (" + slowest + ")");
            foreach (var kv in seen.OrderBy(k => k.Key)) Console.WriteLine("  " + kv.Value + "x " + kv.Key);
            Console.WriteLine(findings == 0 ? "parser_fuzz: all OK" : "parser_fuzz: " + findings + " finding(s)");
            return findings == 0 ? 0 : 1;
        }

        static byte[] MutateFormat(Random r, DumpFormat f)
        {
            if (f.Id == 14) return MutateEmf(r, f.Data);
            if (f.Name.IndexOf("Presentation Exchange", StringComparison.OrdinalIgnoreCase) >= 0) return MutateCmx(r, f.Data);
            if (f.Id == 8 || f.Id == 17) return MutateDib(r, f.Data);
            if (f.Text && r.Next(2) == 0)
            {
                string t = Encoding.UTF8.GetString(f.Data);
                return Encoding.UTF8.GetBytes(MutateText(r, t, t.TrimStart().StartsWith("{\\rtf") ? RtfTokens : SvgTokens));
            }
            return MutateBytes(r, f.Data);
        }

        // The paste as QL Label Maker makes it (EditorForm.Make): the same readers in the same order, then the objects drawn
        // on a label (what the canvas does next). Only InvalidDataException may leave a reader (the paste falls back).
        static void PasteDump(List<DumpFormat> formats, string dump, string name, string what)
        {
            var src = new Source();
            foreach (var f in formats) if (f.Wanted != null) src.Add(f.Wanted, f.Value());
            var big = formats.OrderByDescending(f => f.Data.Length).FirstOrDefault();
            Call("paste " + what, name, big != null ? big.Data : new byte[0], dump, "bin", () =>
            {
                var c = EditorClipboard.Read(src);
                var made = new List<LabelObject>();
                List<string> missing; string how;
                try
                {
                    switch (c.Kind)
                    {
                        case ClipKind.Svg:
                            var texts = SvgText.Read(c.SvgSource, c.SizeMm, SvgQuirks.For(c.Source));
                            if (texts != null && texts.Objects.Count > 0)
                            {
                                if (!texts.OnlyText && texts.Rest != null)
                                    try { made.Add(ImageObject.FromSvg(SvgPicture.Clean(texts.Rest))); } catch (InvalidDataException) { }
                                made.AddRange(texts.Objects);
                            }
                            made.Add(ImageObject.FromSvg(c.Svg));
                            break;
                        case ClipKind.Emf:
                            if (c.EmfKind == EmfPicture.Content.TextOnly)
                                try { made.AddRange(TextImport.FromEmf(c, out missing, out how)); } catch (InvalidDataException) { }
                            var parts = TextImport.FromLabel(c, out missing, out how);
                            if (parts != null)
                                foreach (var part in parts)
                                {
                                    var pic = part as EmfLayers.Picture;
                                    made.Add(pic != null ? ImageObject.FromEmf(pic.Emf, c.Cmx) : (LabelObject)part);
                                }
                            made.Add(ImageObject.FromEmf(c.Emf, c.Cmx));
                            break;
                        case ClipKind.Picture: made.Add(ImageObject.FromBitmap(c.Picture)); break;
                        case ClipKind.Rtf: made.Add(RtfRuns.Object(RtfRuns.Read(c.Rtf, out missing))); break;
                        case ClipKind.Objects: made.AddRange(EditorForm.ObjectsFrom(c.ObjectsJson)); break;
                    }
                }
                finally { if (c.Picture != null) c.Picture.Dispose(); }
                InRange(made);
                var doc = new LabelDoc();
                foreach (var o in made)
                {
                    o.X = 1; o.Y = 1;
                    o.W = Math.Max(0.1, Math.Min(o.W, 200)); o.H = Math.Max(0.1, Math.Min(o.H, 200));
                    doc.Objects.Add(o);
                }
                using (EditorRender.Render(doc, labelLayout, new DateTime(2026, 11, 12, 10, 0, 0))) { }
            }, DumpLimitMs);
        }

        // Pasted objects must be within the label's ranges (NaN or a 1e29 pt text broke the panel and the drawing later).
        static void InRange(IEnumerable<LabelObject> objects)
        {
            foreach (var o in objects)
            {
                var t = o as TextObject;
                bool ok = t != null ? TextImport.Sane(t)
                        : new[] { o.X, o.Y, o.W, o.H }.All(v => !double.IsNaN(v) && Math.Abs(v) <= LabelObject.MaxMm);
                if (!ok) throw new InvalidOperationException("object out of range: " + o.Type + " " + o.X + "," + o.Y + " " + o.W + "x" + o.H + (t != null ? " " + t.Size + " pt" : ""));
            }
        }

        sealed class DumpFormat
        {
            public int Id; public string Name, Wanted; public byte[] Data;
            public bool Text { get { return Id == 1 || Id == 13 || Wanted == DataFormats.Rtf || Wanted == DataFormats.Html || Name.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0; } }
            public DumpFormat With(byte[] data) { return new DumpFormat { Id = Id, Name = Name, Wanted = Wanted, Data = data }; }
            // as a source gives it: the text formats as strings, the rest as bytes
            public object Value()
            {
                if (Id == 13) return Encoding.Unicode.GetString(Data, 0, Data.Length & ~1).TrimEnd('\0');
                if (Id == 1 || Wanted == DataFormats.Rtf) return Encoding.Default.GetString(Data).TrimEnd('\0');
                return Data;
            }
        }

        // A dump folder (tools\clipboard-formats.ps1 -SaveData): formats.txt names the formats, <id>-<name>.<ext> holds each
        // one's data. Files (CF_HDROP) are left out: they would open files.
        static List<DumpFormat> DumpFormats(string d)
        {
            var names = new Dictionary<int, string>();
            foreach (var line in File.ReadAllLines(Path.Combine(d, "formats.txt")))
            {
                var m = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(\d+)\s+(.+?)\s{2,}\S");
                if (m.Success) names[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Trim();
            }
            var list = new List<DumpFormat>();
            foreach (var f in Directory.GetFiles(d))
            {
                var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), @"^(\d+)-");
                string n;
                if (!m.Success || !names.TryGetValue(int.Parse(m.Groups[1].Value), out n)) continue;
                int id = int.Parse(m.Groups[1].Value);
                string wanted;
                switch (id)
                {
                    case 1: wanted = DataFormats.Text; break;
                    case 8: wanted = DataFormats.Dib; break;
                    case 13: wanted = DataFormats.UnicodeText; break;
                    case 14: wanted = EditorClipboard.EmfFormat; break;
                    case 17: wanted = EditorClipboard.DibV5Format; break;
                    default: wanted = id < 0xC000 ? null : n; break;
                }
                list.Add(new DumpFormat { Id = id, Name = n, Wanted = wanted, Data = File.ReadAllBytes(f) });
            }
            return list;
        }

        // ---------- mutations ----------

        static readonly int[] Interesting = { 0, 1, -1, 2, 4, 8, 7, 0x7FFFFFFF, unchecked((int)0x80000000), 0x7FFFFFFC, 0x40000000, 0xFFFF, 0x10000,
                                              unchecked((int)0xFFFFFFF8), 1000000, 0x7FC00000 /* NaN */, 0x7F800000 /* +inf */, unchecked((int)0xFF800000),
                                              0x7149F2CA /* 1e30f */, 0x00000001 /* tiny float */ };

        // CMX: bit flips and cuts, or an interesting number on an instruction's or tag's size, or anywhere.
        static byte[] MutateCmx(Random r, byte[] src)
        {
            int k = r.Next(3);
            if (k == 0) return MutateBytes(r, src);
            var b = (byte[])src.Clone();
            int n = 1 + r.Next(3);
            for (int i = 0; i < n; i++)
            {
                int at = r.Next(b.Length);
                if (k == 1) { if (at + 2 <= b.Length) BitConverter.GetBytes((ushort)Interesting[r.Next(Interesting.Length)]).CopyTo(b, at); }
                else PutInt(b, at, Interesting[r.Next(Interesting.Length)]);
            }
            return b;
        }

        static byte[] MutateBytes(Random r, byte[] src)
        {
            var b = (byte[])src.Clone();
            int n = 1 + r.Next(8);
            for (int i = 0; i < n && b.Length > 0; i++) b[r.Next(b.Length)] ^= (byte)(1 << r.Next(8));
            if (r.Next(4) == 0 && b.Length > 1) Array.Resize(ref b, r.Next(b.Length));
            return b;
        }

        static void PutInt(byte[] b, int at, int v) { if (at >= 0 && at + 4 <= b.Length) BitConverter.GetBytes(v).CopyTo(b, at); }

        // The records' offsets (as long as the sizes make sense).
        static List<int> Records(byte[] b)
        {
            var list = new List<int>();
            int pos = 0;
            while (pos + 8 <= b.Length && list.Count < 100000)
            {
                list.Add(pos);
                int size = BitConverter.ToInt32(b, pos + 4);
                if (size < 8 || size > b.Length - pos) break;
                pos += size;
            }
            return list;
        }

        static byte[] MutateEmf(Random r, byte[] src)
        {
            var b = (byte[])src.Clone();
            var recs = Records(b);
            int steps = 1 + r.Next(3);
            for (int s = 0; s < steps; s++)
            {
                int rec = recs[r.Next(recs.Count)];
                int recSize = rec + 8 <= b.Length ? BitConverter.ToInt32(b, rec + 4) : 0;
                switch (r.Next(9))
                {
                    case 0: b = MutateBytes(r, b); break;
                    case 1: PutInt(b, rec + 4, Interesting[r.Next(Interesting.Length)]); break;                       // a record's size
                    case 2: PutInt(b, rec + 4, recSize + 4 * (r.Next(9) - 4)); break;                                  // a size a little off
                    case 3: PutInt(b, rec + 8 + 4 * r.Next(Math.Max(1, (recSize - 8) / 4)), Interesting[r.Next(Interesting.Length)]); break;   // a number in a record
                    case 4: PutInt(b, rec, r.Next(2) == 0 ? r.Next(123) : new[] { 35, 36, 84, 82, 9, 10, 11, 12, 13, 33, 34, 14 }[r.Next(12)]); break;   // its type
                    case 5: PutInt(b, new[] { 4, 48, 52, 56, 72, 76, 80, 84, 8, 24, 32 }[r.Next(11)], Interesting[r.Next(Interesting.Length)]); break; // header fields
                    case 6: if (b.Length > 8) Array.Resize(ref b, 8 + r.Next(b.Length - 8)); break;                                                     // cut off
                    case 7:   // a record repeated (many runs, many transforms)
                        if (recSize >= 8 && recSize <= b.Length - rec && rec > 0 && b.Length >= 56)
                        {
                            int times = 1 + r.Next(r.Next(10) == 0 ? 2000 : 20);
                            var list = new List<byte>(b.Take(rec + recSize));
                            for (int t = 0; t < times; t++) list.AddRange(b.Skip(rec).Take(recSize));
                            list.AddRange(b.Skip(rec + recSize));
                            b = list.ToArray();
                            PutInt(b, 48, BitConverter.ToInt32(b, 48) + times * recSize);   // nBytes
                            PutInt(b, 52, BitConverter.ToInt32(b, 52) + times);              // nRecords
                        }
                        break;
                    default:   // a float in an XFORM or text record: NaN, infinity, huge
                        int f = new[] { 0x7FC00000, 0x7F800000, unchecked((int)0xFF800000), 0x7149F2CA, 0, unchecked((int)0x80000000) }[r.Next(6)];
                        PutInt(b, rec + 8 + 4 * r.Next(Math.Max(1, Math.Min(12, (recSize - 8) / 4))), f);   // (PutInt skips what is outside)
                        break;
                }
                recs = Records(b);
                if (recs.Count == 0) break;
            }
            return b;
        }

        static readonly string[] RtfTokens =
        {
            "{", "}", "{{{{{{{{", "}}}}}}}}", "\\", "\\u", "\\u-99999999999?", "\\u99999 ", "\\u-1?", "\\uc0 ", "\\uc99999 ", "\\uc-5 ", "\\uc2147483647 ",
            "\\'", "\\'z", "\\'ff", "\\bin999999 ", "\\bin-1 ", "\\fs-99999 ", "\\fs0 ", "\\fs99999999999 ", "\\f99999 ", "\\f-1 ", "\\ansicpg99999 ",
            "\\ansicpg-1 ", "{\\fonttbl{\\f9\\fcharset222 X;}}", "{\\*\\x ", "{\\pict ", "\\par ", "\\line ", "\\tab ", "\\cell ", "\\row ", "\\qc ",
            "\\b ", "\\b0 ", "\\i ", "\\ul ", "\\ulnone ", "\\plain ", "\\pard ", "\\v ", "\\", "€", "\0", "\r\n", "\\'cf\\'cf\\'cf", "\\ql\\qr\\qj ",
            "\\fcharset-1 ", "{\\fonttbl;}", "{\\fonttbl{\\f0 ", "\\deff99 ", "\\upr{", "\\ud{", "\\*\\", "\\fs ", "\\f ", "\\abcdefghijklmnopqrstuvwxyz0123456789 ",
            "\\uc1\\u65\\u66\\u67 ", "\\ ", "\\~", "\\-", "\\_", "\\{", "\\}", "\\\\"
        };

        static readonly string[] SvgTokens =
        {
            "<", ">", "/>", "</svg>", "<svg>", "<g>", "</g>", "<style>", "</style>", "<style>.a{fill:red}</style>", "<style>{{{{</style>", "<style>@media{.a{</style>",
            "<style>/*", "*/", "<text>", "</text>", "<text x=\"1e309\" y=\"NaN\" font-size=\"1e30\">A</text>", "<text font-size=\"-5\">B</text>",
            "<text font-size=\"0\">C</text>", "<tspan dx=\"1 2 3\">D</tspan>", " class=\"a\"", " style=\"fill:\"", " style=\";;:;\"", " id=\"\"", " viewBox=\"0 0 0 0\"",
            " viewBox=\"0 0 -1 NaN\"", " viewBox=\"1e400 1 1 1\"", " width=\"1e309mm\"", " height=\"-1px\"", " width=\"%\"", " font-family=\"\"", " transform=\"rotate(NaN)\"",
            " letter-spacing=\"1e30\"", " href=\"#\"", " xlink:href=\"data:image/png;base64,!!\"", "&amp;", "&#0;", "&#x110000;", "<![CDATA[", "]]>", "<!--", "-->",
            "<?pi x?>", "<!DOCTYPE svg [<!ENTITY a \"aaaa\">]>", "\0", "￿", "<script>", "<foreignObject>", " xmlns=\"\"", " xmlns:a=\"b\" a:x=\"1\""
        };

        static string MutateText(Random r, string src, string[] tokens)
        {
            var sb = new StringBuilder(src);
            int steps = 1 + r.Next(4);
            for (int s = 0; s < steps; s++)
            {
                int at = sb.Length == 0 ? 0 : r.Next(sb.Length + 1);
                switch (r.Next(6))
                {
                    case 0: case 1: case 2: sb.Insert(at, tokens[r.Next(tokens.Length)]); break;
                    case 3: if (sb.Length > 0) sb.Remove(at == sb.Length ? at - 1 : at, Math.Min(1 + r.Next(20), sb.Length - (at == sb.Length ? at - 1 : at))); break;
                    case 4: if (sb.Length > 1) sb.Length = r.Next(sb.Length); break;
                    default: if (sb.Length > 0) { int p = Math.Min(at, sb.Length - 1); sb[p] = (char)(sb[p] ^ (1 << r.Next(7))); } break;
                }
            }
            return sb.ToString();
        }

        static byte[] MutateDib(Random r, byte[] src)
        {
            var b = (byte[])src.Clone();
            switch (r.Next(4))
            {
                case 0: PutInt(b, new[] { 0, 4, 8, 16, 20, 32 }[r.Next(6)], Interesting[r.Next(Interesting.Length)]); break;
                case 1: b[14] = (byte)new[] { 0, 1, 4, 8, 16, 24, 32, 64, 255 }[r.Next(9)]; b[15] = 0; break;
                case 2: Array.Resize(ref b, r.Next(b.Length)); break;
                default: b = MutateBytes(r, b); break;
            }
            return b;
        }

        // ---------- fixtures (as in clipboard_test.cs and editor_test.cs) ----------

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateEnhMetaFileW(IntPtr hdcRef, string file, ref RECT frame, string description);
        [DllImport("gdi32.dll")] static extern IntPtr CloseEnhMetaFile(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(IntPtr hemf);
        [DllImport("gdi32.dll")] static extern bool Rectangle(IntPtr hdc, int l, int t, int r, int b);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOutW(IntPtr hdc, int x, int y, string text, int n);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool ExtTextOutW(IntPtr hdc, int x, int y, uint options, IntPtr rect, string text, int n, int[] dx);
        [DllImport("gdi32.dll")] static extern int StretchDIBits(IntPtr hdc, int x, int y, int w, int h, int sx, int sy, int sw, int sh, byte[] bits, byte[] info, uint usage, uint rop);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike,
                                                                                          uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [DllImport("gdi32.dll")] static extern uint SetTextAlign(IntPtr dc, uint align);
        [DllImport("gdi32.dll")] static extern int SetMapMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern int SetGraphicsMode(IntPtr dc, int mode);
        [DllImport("gdi32.dll")] static extern bool SetWorldTransform(IntPtr dc, ref XFORM x);
        [DllImport("gdi32.dll")] static extern bool ModifyWorldTransform(IntPtr dc, ref XFORM x, uint mode);
        [DllImport("gdi32.dll")] static extern int SaveDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern bool RestoreDC(IntPtr dc, int n);
        [DllImport("gdi32.dll")] static extern bool SetWindowExtEx(IntPtr dc, int x, int y, IntPtr old);
        [DllImport("gdi32.dll")] static extern bool SetViewportExtEx(IntPtr dc, int x, int y, IntPtr old);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] struct XFORM { public float M11, M12, M21, M22, Dx, Dy; }

        static byte[] MakeEmf(Action<IntPtr> draw)
        {
            var frame = new RECT { L = 0, T = 0, R = 3000, B = 2000 };
            IntPtr dc = CreateEnhMetaFileW(IntPtr.Zero, null, ref frame, null);
            draw(dc);
            IntPtr h = CloseEnhMetaFile(dc);
            try { return EmfPicture.FromHandle(h); }
            finally { DeleteEnhMetaFile(h); }
        }

        static void Text(IntPtr dc, string face, int height, int weight, int escapement, int x, int y, string text)
        {
            IntPtr f = CreateFontW(height, 0, escapement, escapement, weight, 0, 0, 0, 1, 0, 0, 0, 0, face);
            IntPtr old = SelectObject(dc, f);
            TextOutW(dc, x, y, text, text.Length);
            SelectObject(dc, old);
            DeleteObject(f);
        }

        // ---------- CMX fixtures (made here, as CorelDRAW 2024 writes them; see Import/Corel/Cmx.cs) ----------

        static byte[] Cat(params byte[][] parts) { return parts.SelectMany(p => p).ToArray(); }
        static byte[] I32s(params int[] v) { return v.SelectMany(BitConverter.GetBytes).ToArray(); }
        static byte[] U16(int v) { return BitConverter.GetBytes((ushort)v); }
        static byte[] Utf16(string s) { return Encoding.Unicode.GetBytes(s); }

        static byte[] CmxTag(int tag, params byte[][] parts)
        {
            var data = Cat(parts);
            return Cat(new[] { (byte)tag }, U16(data.Length + 3), data);
        }

        // One instruction; tags: its tag list (the end mark is added), null: no tags; longForm: 0xFFFF, int32 size, code.
        static byte[] CmxInstruction(int code, byte[][] tags, bool longForm = false)
        {
            var body = tags == null ? new byte[0] : Cat(Cat(tags), new byte[] { 0xFF });
            return longForm ? Cat(U16(0xFFFF), I32s(body.Length + 8), U16(code), body) : Cat(U16(body.Length + 4), U16(code), body);
        }

        static byte[] Chunk(string id, byte[] data)
        {
            var c = Cat(Encoding.ASCII.GetBytes(id), I32s(data.Length), data);
            return data.Length % 2 == 1 ? Cat(c, new byte[1]) : c;
        }

        static byte[] CmxFile(string font, params byte[][] page)
        {
            var body = Cat(Encoding.ASCII.GetBytes("CMX1"), Chunk("cont", new byte[16]),
                           Chunk("rfnt", Cat(CmxTag(4, I32s(font.Length), Utf16(font)), CmxTag(1, U16(font.Length), Encoding.ASCII.GetBytes(font)), new byte[] { 0xFF })),
                           Chunk("page", Cat(page)));
            return Cat(Encoding.ASCII.GetBytes("RIFF"), I32s(body.Length), body);
        }

        static byte[] CmxTextStream(string text, bool frame, int size)
        {
            var parts = new List<byte[]> { CmxInstruction(20, null) };
            if (frame)
                parts.Add(CmxInstruction(98, new[] { CmxTag(1, U16(1), I32s(263571)),
                    CmxTag(2, I32s(-57514, 0), U16(2), Cat(new[] { 1.0, 0, 0, 1, -235000.5, -180000.25 }.Select(BitConverter.GetBytes).ToArray())) }));
            parts.Add(CmxInstruction(99, new[] { CmxTag(2, U16(1), I32s(size)), CmxTag(5, I32s(0, 0, 1000000, 870000, 0)) }));
            parts.Add(CmxInstruction(101, new[] { CmxTag(1, I32s(1, 2, 3)) }, true));   // CharInfo: skipped
            parts.Add(CmxInstruction(102, new[] { CmxTag(3, I32s(text.Length), Utf16(text)) }));
            parts.Add(CmxInstruction(21, null));
            return Cat(parts.ToArray());
        }

        // Paragraph text as CorelDRAW writes it: one BeginParagraph .. EndParagraph per paragraph (centred, 20 %
        // character spacing), no paragraph marks in the characters.
        static byte[] CmxParagraphs(params string[] paragraphs)
        {
            var parts = new List<byte[]> { CmxInstruction(20, new[] { CmxTag(1, U16(0)) }) };
            parts.Add(CmxInstruction(98, new[] { CmxTag(1, U16(1), I32s(400000)), CmxTag(2, I32s(-200000, 0), U16(2), Cat(new[] { 1.0, 0, 0, 1, 0, 0 }.Select(BitConverter.GetBytes).ToArray())), CmxTag(3, U16(0)) }));
            foreach (var p in paragraphs)
            {
                parts.Add(CmxInstruction(99, new[] { CmxTag(2, U16(1), I32s(42333)), CmxTag(4, new byte[] { 2 }), CmxTag(5, I32s(200000, 0, 1000000, 1000000, 1000000, 0)) }));
                parts.Add(CmxInstruction(102, new[] { CmxTag(3, I32s(p.Length), Utf16(p)) }));
                parts.Add(CmxInstruction(100, null));
            }
            parts.Add(CmxInstruction(21, null));
            return Cat(parts.ToArray());
        }

        static byte[] CmxLaidOut(string text)
        {
            var xy = new List<int>();
            for (int i = 0; i < text.Length; i++) { xy.Add(i * 25000); xy.Add(-40000); }
            return Cat(CmxInstruction(70, null), CmxInstruction(65, new[] { CmxTag(1, U16(text.Length), Utf16(text), I32s(xy.ToArray())) }), CmxInstruction(71, null));
        }

        static List<byte[]> CmxFixtures()
        {
            return new List<byte[]>
            {
                CmxFile("Arial", CmxTextStream("Hallo", false, 46609), CmxLaidOut("Hallo")),
                CmxFile("Arial", CmxTextStream("Ha\rllo\r", true, 46609), CmxLaidOut("Ha"), CmxLaidOut("llo")),
                CmxFile("Arial", CmxTextStream("Hallo", true, 46609), CmxTextStream("Zweite Zeile", false, 30000), CmxLaidOut("Hallo")),
                CmxFile("Arial", CmxInstruction(9, null), CmxTextStream("Hallo", false, 46609), CmxInstruction(10, null)),
                CmxFile("Arial", CmxParagraphs("Hal", "lo")),
            };
        }

        static List<byte[]> EmfFixtures()
        {
            var list = new List<byte[]>();
            list.Add(MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -40, 400, 0, 100, 120, "Hallo"); }));
            list.Add(MakeEmf(dc => { SetTextAlign(dc, 24); Text(dc, "Arial", -30, 400, 0, 50, 100, "Zeile 1"); Text(dc, "Arial", -30, 700, 0, 50, 136, "Zweite Zeile"); Text(dc, "Arial", -30, 400, 900, 200, 200, "Hoch"); }));
            list.Add(MakeEmf(dc => { SetMapMode(dc, 2); SetTextAlign(dc, 24); Text(dc, "Arial", -50, 400, 0, 100, -100, "Metrisch"); }));
            list.Add(MakeEmf(dc =>
            {
                SetGraphicsMode(dc, 2);
                float c = (float)Math.Cos(Math.PI / 6), s = (float)Math.Sin(Math.PI / 6);
                var x = new XFORM { M11 = c, M12 = s, M21 = -s, M22 = c, Dx = 100, Dy = 100 };
                SetWorldTransform(dc, ref x);
                var y = new XFORM { M11 = 2, M22 = 2 };
                ModifyWorldTransform(dc, ref y, 2);
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -30, 400, 0, 0, 0, "Schräg");
            }));
            list.Add(MakeEmf(dc =>   // CorelDRAW's shape (for Repair)
            {
                SetMapMode(dc, 8);
                SetWindowExtEx(dc, 100000, 100000, IntPtr.Zero);
                SetViewportExtEx(dc, 12000, 12000, IntPtr.Zero);
                SaveDC(dc);
                SetMapMode(dc, 1);
                SetTextAlign(dc, 24);
                Text(dc, "Arial", -60, 400, 0, 0, 120, "3mm");
                SetGraphicsMode(dc, 2);
                SetMapMode(dc, 8);
                SetWindowExtEx(dc, 1, 1, IntPtr.Zero);
                SetViewportExtEx(dc, 1, 1, IntPtr.Zero);
                var x = new XFORM { M11 = 1, M22 = 1, Dx = 1200, Dy = 1000 };
                SetWorldTransform(dc, ref x);
                Text(dc, "Arial", -500, 700, 0, 0, 0, " - 10");
                RestoreDC(dc, -1);
            }));
            list.Add(MakeEmf(dc =>   // text with character spacing (dx array) and a rectangle
            {
                Rectangle(dc, 10, 10, 200, 100);
                IntPtr f = CreateFontW(-30, 0, 0, 0, 400, 1, 1, 0, 1, 0, 0, 0, 0, "Courier New");
                IntPtr old = SelectObject(dc, f);
                ExtTextOutW(dc, 20, 50, 0, IntPtr.Zero, "Abstand", 7, new[] { 20, 20, 20, 20, 20, 20, 20 });
                SelectObject(dc, old);
                DeleteObject(f);
            }));
            list.Add(MakeEmf(dc =>   // a bitmap
            {
                var info = new byte[40];
                BitConverter.GetBytes(40).CopyTo(info, 0); BitConverter.GetBytes(4).CopyTo(info, 4); BitConverter.GetBytes(4).CopyTo(info, 8);
                info[12] = 1; info[14] = 32;
                StretchDIBits(dc, 0, 0, 40, 40, 0, 0, 4, 4, new byte[64], info, 0, 0x00CC0020);
            }));
            return list;
        }

        static List<string> RtfFixtures()
        {
            return new List<string>
            {
                @"{\rtf1\ansi\ansicpg1252\uc1\deff0{\fonttbl{\f0\fswiss\fcharset0 Arial;}" +
                @"{\flominor\f31506\fbidi \fswiss\fcharset0\fprq2{\*\panose 020b0004020202020204}Segoe UI;}{\f240\fswiss\fcharset0 Arial Black;}" +
                @"{\f5\fnil\fcharset204 Arial;}}{\colortbl;\red255\green0\blue0;}{\*\generator Test;}" +
                @"\pard\plain \ltrpar\qc \f31506\fs24\lang1031 {\ul Heute} {\b ist }ein sch\'f6ner {\i Tag}! {\f240\cf1 Satz}\par " +
                @"\pard\plain \qc \f31506\fs32 Gro\'df{\v versteckt}{\f5 \'cf}\par }",
                @"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Arial;}}{\colortbl;\red0\green0\blue0;}{\*\generator Test;}\pard\f0\fs20 Gr\'fc\'dfe\par Zeile 2\line x\tab y\par}",
                @"{\rtf1\uc1 荤?\u-3913? A}{\rtf1\uc2 荤\'80\'80 B}",
                @"{\rtf1 {\*\shppict{\pict\pngblip 89504e47}}{\nonshppict{\pict 00}}Text \trowd\cellx1000 A\cell B\cell\row}",
                @"{\rtf1\ansi\ansicpg1251 \'cf {\ulc1 a\ul b\ul0 c\ulnone}}"
            };
        }

        static IEnumerable<string> HardRtf()
        {
            yield return @"{\rtf1 " + new string('{', 100000) + "x" + new string('}', 100000) + "}";            // deep nesting
            yield return @"{\rtf1 " + new string('{', 100000) + "x";                                            // never closed
            yield return @"{\rtf1 " + new string('}', 100000) + "x";                                            // closed too often
            yield return @"{\rtf1 " + new string('a', 300000) + "}";                                            // long text in one run
            yield return @"{\rtf1 " + string.Concat(Enumerable.Repeat(@"{\b a}{\i b}", 50000)) + "}";          // many runs
            yield return @"{\rtf1 " + string.Concat(Enumerable.Repeat(@"\u65?", 100000)) + "}";               // many \u
            yield return @"{\rtf1 " + string.Concat(Enumerable.Repeat(@"\par ", 100000)) + "}";               // many paragraphs
            yield return @"{\rtf1{\fonttbl" + string.Concat(Enumerable.Range(0, 20000).Select(i => @"{\f" + i + " F" + i + ";}")) + "} x}";   // many fonts
            yield return @"{\rtf1 즙999999999 \u65 abc}";
            yield return @"{\rtf1 \u" + new string('9', 1000) + " x}";
            yield return @"{\rtf1 \" + new string('a', 100000) + " x}";                                         // a huge control word
        }

        static List<byte[]> SvgFixtures()
        {
            var enc = new UTF8Encoding(false);
            return new List<byte[]>
            {
                enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"20mm\" height=\"10mm\" viewBox=\"0 0 20 10\"><rect x=\"1\" y=\"1\" width=\"18\" height=\"8\" fill=\"black\"/></svg>"),
                enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>/* c */ @charset \"UTF-8\"; @font-face{font-family:x} rect{fill:red;stroke:blue} " +
                             ".a{fill:green} #b{fill:black} rect.a.c, circle{stroke-width:3!important;fill:url(https://example.com/x)} g rect{fill:pink} " +
                             ".d{fill:url(#grad)}</style><rect id=\"r1\" class=\"a\"/><rect id=\"b\" class=\"a\" fill=\"white\"/><rect id=\"r3\" class=\"a\" style=\"fill:gray\"/>" +
                             "<rect id=\"r4\" class=\"c a\"/><circle id=\"r5\"/><rect id=\"r6\" class=\"d\"/></svg>"),
                enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"0 0 200 50\">" +
                             "<g transform=\"translate(5 5)\"><text x=\"1\" y=\"20\" font-family=\"Arial\" font-size=\"12\" font-weight=\"bold\" text-anchor=\"middle\">Hallo " +
                             "<tspan x=\"10\" dy=\"14\" font-style=\"italic\">Welt</tspan></text></g>" +
                             "<path id=\"p\" d=\"M0 0 L90 9\"/><text><textPath href=\"#p\">A</textPath></text><use xlink:href=\"#p\"/></svg>"),
                enc.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\" width=\"40mm\" height=\"10mm\" onload=\"x()\">" +
                             "<script>x()</script><image href=\"https://example.com/a.png\"/><use href=\"#a\"/><rect id=\"a\" width=\"1\" height=\"1\"/></svg>")
            };
        }

        static IEnumerable<byte[]> HardSvg()
        {
            var enc = new UTF8Encoding(false);
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\">" + string.Concat(Enumerable.Repeat("<g>", 20000)) + string.Concat(Enumerable.Repeat("</g>", 20000)) + "</svg>");
            // the same with something drawn and a text inside (the text import walks it; security review 2026-11-11)
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\">" + string.Concat(Enumerable.Repeat("<g>", 20000))
                                      + "<rect width=\"5\" height=\"5\" stroke=\"black\"/><text x=\"1\" y=\"10\">A<tspan>B</tspan></text>" + string.Concat(Enumerable.Repeat("</g>", 20000)) + "</svg>");
            foreach (var t in new[] { "transform=\"scale(0)\" text-anchor=\"middle\"", "x=\"Infinity\"", "transform=\"translate(NaN)\"", "font-size=\"1e30\"",
                                      "font-size=\"Infinity\"", "transform=\"scale(1e300) scale(1e300)\"", "letter-spacing=\"NaN\"", "transform=\"rotate(NaN)\"" })
                yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100\" height=\"50\"><rect width=\"5\" height=\"5\" stroke=\"black\"/><text y=\"10\" " + (t.StartsWith("x=") ? t : t + " x=\"1\"")
                                          + ">Abc</text></svg>");
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>" + string.Concat(Enumerable.Range(0, 3000).Select(i => ".c" + i + "{fill:red}")) + "</style>" +
                                      string.Concat(Enumerable.Range(0, 3000).Select(i => "<rect class=\"c" + i + "\"/>")) + "</svg>");   // many rules x many elements
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\">" + string.Concat(Enumerable.Repeat("<text x=\"1\" y=\"1\">Text</text>", 2000)) + "</svg>");
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><text x=\"1\" y=\"1\">" + new string('W', 100000) + "</text></svg>");
            yield return enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><style>" + new string('{', 100000) + "</style></svg>");
            yield return enc.GetBytes("<!DOCTYPE svg [<!ENTITY a \"aaaaaaaaaa\"><!ENTITY b \"&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;\"><!ENTITY c \"&b;&b;&b;&b;&b;&b;&b;&b;&b;&b;\">" +
                                      "<!ENTITY d \"&c;&c;&c;&c;&c;&c;&c;&c;&c;&c;\"><!ENTITY e \"&d;&d;&d;&d;&d;&d;&d;&d;&d;&d;\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&e;&e;&e;</svg>");   // entity expansion
        }

        static IEnumerable<byte[]> HardEmf(byte[] text)
        {
            // many text records: the first text record repeated 5000 times
            var recs = Records(text);
            int at = recs.FirstOrDefault(p => BitConverter.ToInt32(text, p) == 84);
            if (at > 0)
            {
                int size = BitConverter.ToInt32(text, at + 4);
                var list = new List<byte>(text.Take(at + size));
                for (int i = 0; i < 5000; i++)
                {
                    var rec = text.Skip(at).Take(size).ToArray();
                    PutInt(rec, 36, BitConverter.ToInt32(rec, 36) + (i % 50) * 3);   // a little moved, not all on one spot
                    list.AddRange(rec);
                }
                list.AddRange(text.Skip(at + size));
                var b = list.ToArray();
                PutInt(b, 48, b.Length);
                PutInt(b, 52, BitConverter.ToInt32(b, 52) + 5000);
                yield return b;
            }
        }

        // ---------- label files ----------

        static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        // One label with every kind of object and most keys, plus the help's sample labels when the folder is there.
        static List<string> LabelFixtures()
        {
            string png;
            using (var bmp = new Bitmap(4, 4))
            using (var ms = new MemoryStream())
            {
                bmp.SetPixel(1, 1, Color.Black); bmp.SetPixel(2, 2, Color.Gray);
                bmp.Save(ms, ImageFormat.Png); png = Convert.ToBase64String(ms.ToArray());
            }
            var objects = new object[]
            {
                D("type", "text", "x", 2, "y", 2, "w", 40, "h", 10, "rot", 0, "text", "Hallo {date} Welt", "font", "Arial", "size", 12, "bold", false,
                  "italic", false, "underline", false, "align", "left", "kind", "frame", "lineHeight", 1, "spacing", 0,
                  "runs", new object[] { D("t", "Hallo ", "font", "Arial", "size", 12), D("t", "{date} Welt", "bold", true, "underline", true) }),
                D("type", "text", "x", 2, "y", 14, "w", 30, "h", 8, "rot", 90, "tilt", 15, "text", "Grafik", "font", "Segoe UI", "size", 20, "align", "center",
                  "kind", "line", "invert", true),
                D("type", "line", "x", 1, "y", 30, "w", 50, "h", 0, "thickness", 0.5, "rising", false),
                D("type", "box", "x", 1, "y", 32, "w", 20, "h", 10, "thickness", 0.3, "filled", false, "ellipse", false, "radius", 2),
                D("type", "shape", "x", 25, "y", 32, "w", 10, "h", 10, "thickness", 0.3, "filled", true, "shape", "star", "points", 5, "inner", 0.5),
                D("type", "shape", "x", 40, "y", 32, "w", 10, "h", 10, "shape", "polygon", "corners", 6),
                D("type", "barcode", "x", 2, "y", 44, "w", 40, "h", 10, "symbology", "code128", "data", "ABC-{gs}123", "showText", true, "level", "M"),
                D("type", "barcode", "x", 45, "y", 44, "w", 12, "h", 12, "symbology", "qr", "data", "", "qrType", "wifi",
                  "qrFields", D("ssid", "Netz", "password", "geheim"), "level", "Q", "invert", true),
                D("type", "barcode", "x", 2, "y", 58, "w", 30, "h", 10, "symbology", "ean13", "data", "200000000000", "showText", true),
                D("type", "image", "x", 35, "y", 58, "w", 10, "h", 10, "png", png, "mode", "threshold", "threshold", 128, "invert", false),
                D("type", "image", "x", 48, "y", 58, "w", 10, "h", 5, "png", png, "mode", "dither",
                  "svg", "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 20 10\"><rect x=\"1\" y=\"1\" width=\"18\" height=\"8\"/></svg>"),
                D("type", "future", "x", 1, "y", 1, "w", 1, "h", 1)
            };
            var doc = D("format", "QL label", "version", 2, "label", D("form", "", "paperId", 259, "length01", 700, "view", "along", "length", "own"), "objects", objects);
            var list = new List<string> { Json.Serialize(doc) };
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                string help = Path.Combine(dir.FullName, @"labeltool\help\img\shots\labels");
                if (!Directory.Exists(help)) continue;
                list.AddRange(Directory.GetFiles(help, "*.qllabel", SearchOption.AllDirectories).OrderBy(f => f).Select(f => File.ReadAllText(f, Encoding.UTF8)));
                break;
            }
            return list;
        }

        static readonly System.Web.Script.Serialization.JavaScriptSerializer Json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static readonly string[] LabelKeys =
        {
            "type", "x", "y", "w", "h", "rot", "tilt", "text", "font", "size", "bold", "italic", "underline", "align", "kind", "lineHeight", "spacing",
            "invert", "runs", "t", "thickness", "rising", "filled", "ellipse", "radius", "shape", "corners", "points", "inner", "symbology", "data",
            "showText", "level", "qrType", "qrFields", "png", "svg", "emf", "mode", "threshold", "form", "paperId", "length01", "view", "length", "version"
        };

        static object LabelValue(Random r)
        {
            switch (r.Next(24))
            {
                case 0: return 0;
                case 1: return -1;
                case 2: return 1e9;
                case 3: return -1e9;
                case 4: return 1e308;
                case 5: return int.MaxValue;
                case 6: return int.MinValue;
                case 7: return 0.0001;
                case 8: return "";
                case 9: return "x";
                case 10: return new string('W', 1 + r.Next(20000));
                case 11: return true;
                case 12: return null;
                case 13: return new object[0];
                case 14: return D();
                case 15: return new object[] { D("t", "a"), D("t", null), 5, D("t", "b", "size", -3) };
                case 16: return "{date}{time}{gs}{x00}{{";
                case 17: return "AAAA";   // base64 of three zero bytes: not a picture
                case 18: return "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"1e9\" height=\"1e9\"><text>A</text></svg>";
                case 19: return new[] { "text", "line", "box", "shape", "image", "barcode" }[r.Next(6)];
                case 20: return new[] { "code128", "ean13", "qr", "gs1-128", "gs1_128", "?" }[r.Next(6)];
                case 21: return new[] { "triangle", "polygon", "star", "?" }[r.Next(4)];
                case 22: return string.Concat(Enumerable.Repeat("́😀‏", 1 + r.Next(500)));
                default: return r.NextDouble() * 400 - 200;
            }
        }

        // Mostly values changed inside the JSON (keys of every kind set on any object), sometimes the text itself.
        static string MutateLabel(Random r, string src)
        {
            if (r.Next(5) == 0) return MutateText(r, src, LabelTokens);
            var d = Json.DeserializeObject(src) as Dictionary<string, object>;
            if (d == null) return src;
            var objects = (d.ContainsKey("objects") ? d["objects"] as object[] : null) ?? new object[0];
            int steps = 1 + r.Next(5);
            for (int s = 0; s < steps; s++)
            {
                Dictionary<string, object> target;
                int pick = r.Next(objects.Length + 2);
                if (pick < objects.Length) target = objects[pick] as Dictionary<string, object>;
                else if (pick == objects.Length) target = d.ContainsKey("label") ? d["label"] as Dictionary<string, object> : null;
                else target = d;
                if (target == null) continue;
                var runs = target.ContainsKey("runs") ? target["runs"] as object[] : null;
                if (runs != null && runs.Length > 0 && r.Next(3) == 0) target = runs[r.Next(runs.Length)] as Dictionary<string, object> ?? target;
                string key = LabelKeys[r.Next(LabelKeys.Length)];
                if (r.Next(8) == 0) target.Remove(key); else target[key] = LabelValue(r);
            }
            if (r.Next(6) == 0 && objects.Length > 0)   // many copies of one object
                d["objects"] = Enumerable.Repeat(objects[r.Next(objects.Length)], 1 + r.Next(300)).ToArray();
            return Json.Serialize(d);
        }

        static readonly string[] LabelTokens =
        {
            "{", "}", "[", "]", ",", ":", "\"", "null", "true", "1e400", "-0", "NaN", "\\u0000", "\\ud800", "\"type\":\"text\",", "\"objects\":[",
            "{\"type\":\"image\",\"png\":\"!!\"},", "[[[[[[[[[[[[[[[[[[[[", "]]]]]]]]]]]]]]]]]]]]"
        };

        static IEnumerable<string> HardLabels(string sample)
        {
            yield return "{\"format\":\"QL label\",\"objects\":" + new string('[', 5000) + new string(']', 5000) + "}";   // deep arrays
            var many = Json.DeserializeObject(sample) as Dictionary<string, object>;
            var objects = (object[])many["objects"];
            many["objects"] = Enumerable.Range(0, 2000).Select(i => objects[i % objects.Length]).ToArray();   // 2,000 objects
            yield return Json.Serialize(many);
            var text = D("type", "text", "x", 0, "y", 0, "w", 60, "h", 60, "kind", "frame", "text", new string('W', 100000), "size", 8);
            yield return Json.Serialize(D("format", "QL label", "objects", new object[] { text }));   // 100,000 characters in one frame
            var runs = Enumerable.Range(0, 20000).Select(i => (object)D("t", "a", "bold", i % 2 == 0)).ToArray();
            var runText = D("type", "text", "x", 0, "y", 0, "w", 60, "h", 60, "kind", "frame", "text", new string('a', 20000), "runs", runs);
            yield return Json.Serialize(D("format", "QL label", "objects", new object[] { runText }));   // 20,000 runs
            // 300 QR codes in boxes of 10000 mm (each made a bitmap of millions of dots: 5 s, seed 7 label#1444) and 100
            // different QR codes of 2,000 characters each (encoded several times per code before)
            var wide = Enumerable.Range(0, 300).Select(i => (object)D("type", "barcode", "symbology", "qr", "x", 20, "y", 16, "w", 10000, "h", 26,
                                                                         "data", "https://example.org/" + i % 3)).ToArray();
            yield return Json.Serialize(D("format", "QL label", "objects", wide));
            var full = Enumerable.Range(0, 100).Select(i => (object)D("type", "barcode", "symbology", "qr", "x", 5, "y", 5, "w", 50, "h", 50,
                                                                         "data", i + new string('x', 2000))).ToArray();
            yield return Json.Serialize(D("format", "QL label", "objects", full));
        }

        static List<byte[]> DibFixtures()
        {
            var list = new List<byte[]>();
            foreach (int bits in new[] { 32, 24, 8, 1 })
            {
                int w = 6, h = 4, stride = ((w * bits + 31) / 32) * 4, colours = bits <= 8 ? 1 << bits : 0;
                var d = new byte[40 + colours * 4 + stride * h];
                BitConverter.GetBytes(40).CopyTo(d, 0); BitConverter.GetBytes(w).CopyTo(d, 4); BitConverter.GetBytes(bits == 24 ? -h : h).CopyTo(d, 8);
                d[12] = 1; d[14] = (byte)bits;
                for (int i = 40; i < d.Length; i++) d[i] = (byte)(i * 37);
                list.Add(d);
            }
            return list;
        }
    }
}
