// Fuzz test for the parsers that read data from other programs (clipboard, drag and drop, files): EmfPicture
// (Check, What, SizeMm, Render), EmfText (Read, Objects, Repair), RtfRuns (Read, Object, PlainText), SvgPicture (Clean,
// Size, Missing, Render), EditorClipboard (Read, Dib). Inputs: the synthetic fixtures of clipboard_test/editor_test
// (EMFs drawn with GDI, Word-like RTF, SVG with CSS and text, DIBs), mutated with a fixed seed: bits flipped, numbers
// at record and header offsets overwritten (0, -1, int.MaxValue, NaN, ...), cut off, RTF/SVG tokens inserted; plus a
// few hand-made worst cases (deep nesting, long text). Passes when no call throws anything but InvalidDataException
// (what the editor treats as "not readable") and none takes longer than 1 s. Changes nothing, no clipboard, no printer.
// Usage: labeltool\test\parser_fuzz.ps1 [-Count 3000] [-Seed 20261108]   (builds and runs it; exit 1 on a finding)
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
        const int LimitMs = 1000;
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
            int count = args.Length > 0 ? int.Parse(args[0]) : 3000;
            int seed = args.Length > 1 ? int.Parse(args[1]) : 20261108;
            string dump = args.Length > 2 ? args[2] : null;   // folder for the inputs of findings
            if (dump != null) Directory.CreateDirectory(dump);
            Lang.Force("en");
            var rnd = new Random(seed);
            var emfs = EmfFixtures();
            var rtfs = RtfFixtures();
            var svgs = SvgFixtures();
            var dibs = DibFixtures();
            Console.WriteLine("parser_fuzz: seed " + seed + ", " + count + " inputs per kind, fixtures " + emfs.Count + " EMF, " + rtfs.Count + " RTF, " + svgs.Count + " SVG, " + dibs.Count + " DIB");

            // warm-up, not timed: the first calls load fonts, GDI+ and Direct2D and compile the code (two seconds)
            warming = true;
            Emf(emfs[0], null, "warm-up"); Rtf(rtfs[0], null, "warm-up"); Svg(svgs[2], null, "warm-up"); Dib(dibs[0], null, "warm-up");
            warming = false;
            // the fixtures themselves must be fine
            foreach (var e in emfs) Emf(e, dump, "fixture");
            foreach (var r in rtfs) Rtf(r, dump, "fixture");
            foreach (var s in svgs) Svg(s, dump, "fixture");
            // worst cases made by hand (not in the quick run: some take a second by design)
            if (!quick)
            {
                int h = 0;
                foreach (var r in HardRtf()) Rtf(r, dump, "hand-rtf#" + h++);
                h = 0;
                foreach (var s in HardSvg()) Svg(s, dump, "hand-svg#" + h++);
                h = 0;
                foreach (var e in HardEmf(emfs[0])) Emf(e, dump, "hand-emf#" + h++);
            }

            for (int i = 0; i < count; i++)
            {
                Emf(MutateEmf(rnd, emfs[rnd.Next(emfs.Count)]), dump, "emf#" + i);
                Rtf(MutateText(rnd, rtfs[rnd.Next(rtfs.Count)], RtfTokens), dump, "rtf#" + i);
                Svg(Encoding.UTF8.GetBytes(MutateText(rnd, Encoding.UTF8.GetString(svgs[rnd.Next(svgs.Count)]), SvgTokens)), dump, "svg#" + i);
                if (i % 4 == 0) Svg(MutateBytes(rnd, svgs[rnd.Next(svgs.Count)]), dump, "svgbytes#" + i);
                Dib(MutateDib(rnd, dibs[rnd.Next(dibs.Count)]), dump, "dib#" + i);
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
                }, 64 * 1024 * 1024);
                t.IsBackground = true;
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
            public void Start(Action a) { job = a; start.Set(); }
            public bool Wait(int ms) { return finished.WaitOne(ms); }
        }
        static Worker worker = new Worker();

        // One call on the worker thread: InvalidDataException is fine, anything else and more than 1 s are findings.
        static void Call(string what, string name, byte[] input, string dump, string ext, Action a)
        {
            calls++;
            var sw = Stopwatch.StartNew();
            worker.Start(a);
            bool done = worker.Wait(LimitMs);
            if (!done) done = worker.Wait(10000);   // a hang still ends (or is left behind) after 11 s
            long ms = sw.ElapsedMilliseconds;
            Exception error = done ? worker.Error : null;
            if (!done) worker = new Worker();
            if (warming) return;
            long sum; totals.TryGetValue(what, out sum); totals[what] = sum + ms;
            if (ms > slowestMs) { slowestMs = ms; slowest = what + " " + name + " (" + input.Length + " bytes)"; }
            string finding = null;
            if (ms > LimitMs) finding = what + ": " + (done ? "took " + ms + " ms" : "HANG (> 11 s)");
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

        // ---------- mutations ----------

        static readonly int[] Interesting = { 0, 1, -1, 2, 4, 8, 7, 0x7FFFFFFF, unchecked((int)0x80000000), 0x7FFFFFFC, 0x40000000, 0xFFFF, 0x10000,
                                              unchecked((int)0xFFFFFFF8), 1000000, 0x7FC00000 /* NaN */, 0x7F800000 /* +inf */, unchecked((int)0xFF800000),
                                              0x7149F2CA /* 1e30f */, 0x00000001 /* tiny float */ };

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
