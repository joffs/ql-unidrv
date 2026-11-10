// Unit test for the label editor's model and rendering (EditorModel.cs, EditorRender.cs): .qllabel round trip,
// unknown fields kept, rotation, 1-bit rendering at the right dots, threshold/dither. No printer needed
// (the layout is estimated from the paper table). Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace QlLabels.Tests
{
    static partial class EditorTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static int Main()
        {
            Lang.Force("en");
            RoundTrip();
            Shapes();
            Rotation();
            Tilt();
            Render();
            HighRes();
            Across();
            Images();
            Svg();
            Check(EditorProgram.Quote(@"C:\a b\x.qllabel") == "\"C:\\a b\\x.qllabel\"" && EditorProgram.Quote(@"C:\dir\") == "\"C:\\dir\\\\\"" &&
                  EditorProgram.Quote("say \"hi\"") == "\"say \\\"hi\\\"\"", "command-line quoting");
            Barcodes();
            Qr();
            FieldsAndFit();
            TextKinds();
            QrTypes();
            Layers();
            Outside();
            Margin();
            QuietZones();
            CustomSizeMargins();
            OwnLength();
            Invert();
            ResetRotation();
            RenderPart();
            LineSnap();
            Gs1Ui();
            LineAngleLength();
            TextRuns();
            CaretMapTest();
            RunFormatsTest();
            EditingTest();
            SpacesTest();
            RecentTest();
            OddNumbersTest();
            SingleInstanceTest();
            Console.WriteLine(failures == 0 ? "editor_test: all OK" : "editor_test: " + failures + " failure(s)");
            return failures == 0 ? 0 : 1;
        }

        // A hand-edited label with "NaN" or "Infinity" as text for a number (review 0.22): the defaults, never NaN (it got
        // past every clamp: letter spacing, size and line height NaN), and the label draws.
        static void OddNumbersTest()
        {
            string json = "{\"format\":\"QL label\",\"version\":2,\"objects\":[{\"type\":\"text\",\"x\":2,\"y\":2,\"text\":\"ab\"," +
                          "\"size\":\"NaN\",\"spacing\":\"NaN\",\"lineHeight\":\"-Infinity\",\"runs\":[{\"t\":\"a\",\"spacing\":\"Infinity\"},{\"t\":\"b\",\"size\":\"NaN\"}]}]}";
            TextObject t = null;
            try { t = (TextObject)LabelDoc.FromJson(json).Objects[0]; } catch (Exception ex) { Check(false, "odd numbers: opened (" + ex.Message + ")"); return; }
            Check(!double.IsNaN(t.Size) && t.Size >= 2 && t.Size <= 400, "odd numbers: size " + t.Size);
            Check(t.Spacing == 0 && t.LineHeight == 1, "odd numbers: letter spacing " + t.Spacing + ", line height " + t.LineHeight);
            Check(t.Runs != null && t.Runs.All(r => r.Spacing == null && r.Size == null), "odd numbers: run values left to the text");
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            try { using (var bmp = EditorRender.Render(doc, LabelLayout.Estimate(Continuous62(50), false, false))) Check(bmp != null, "odd numbers: drawn"); }
            catch (Exception ex) { Check(false, "odd numbers: drawn (" + ex.Message + ")"); }
        }

        // One window per program (SingleInstance.cs, review 0.22): a second start counts as handed over only when the
        // running one answered; one that closes the pipe without an answer (its window just closed, or it refused the
        // arguments) is not, so the second start tries again and opens a window of its own. After Release (the window
        // closed) nobody takes anything.
        static void SingleInstanceTest()
        {
            string pipe = "QLUnidrv.UnitTest." + Guid.NewGuid().ToString("N");
            Func<bool, int> serve = answer =>
            {
                var server = new System.IO.Pipes.NamedPipeServerStream(pipe, System.IO.Pipes.PipeDirection.InOut, 1);
                var t = new Thread(() =>
                {
                    try
                    {
                        server.WaitForConnection();
                        var r = new System.IO.BinaryReader(server, System.Text.Encoding.UTF8);
                        int n = r.ReadInt32();
                        for (int i = 0; i < n; i++) r.ReadString();
                        if (answer) { server.WriteByte(1); server.Flush(); server.WaitForPipeDrain(); }
                    }
                    catch (Exception) { }
                    finally { server.Dispose(); }
                }) { IsBackground = true };
                t.Start();
                int result = SingleInstance.Send(pipe, new[] { "x.qllabel" }, 5000);
                t.Join(5000);
                return result;
            };
            Check(serve(true) == 1, "single instance: an answer is a hand-over");
            Check(serve(false) == 0, "single instance: closed without an answer is no hand-over");

            string old = SingleInstance.Suffix;
            SingleInstance.Suffix = ".UnitTest" + Guid.NewGuid().ToString("N");
            try
            {
                string app = "Test", name = SingleInstance.PipeName(app);
                Check(!SingleInstance.HandOff(app, new string[0]), "single instance: the first start is the running one");
                Check(SingleInstance.Send(name, new[] { "a" }, 5000) == 1, "single instance: the running one takes a second start");
                SingleInstance.Release();
                Check(SingleInstance.Send(name, new[] { "a" }, 500) == 0, "single instance: after its window closed nobody takes it");
                Check(!SingleInstance.HandOff(app, new string[0]), "single instance: the next start is the running one again");
                Check(SingleInstance.Send(name, new[] { "a" }, 5000) == 1, "single instance: and takes a later start");
                SingleInstance.Release();
            }
            finally { SingleInstance.Suffix = old; }
        }

        // File > Recent labels (RecentLabels.cs): the newest first, once each whatever the case, at most 15, a damaged
        // registry value cleaned; stored in HKCU, here under a key of its own (removed afterwards).
        static void RecentTest()
        {
            var list = new List<string>();
            for (int i = 1; i <= 17; i++) list = RecentLabels.With(list, @"C:\Etiketten\L" + i + ".qllabel");
            Check(list.Count == 15 && list[0] == @"C:\Etiketten\L17.qllabel" && list[14] == @"C:\Etiketten\L3.qllabel", "recent: newest first, at most 15");
            list = RecentLabels.With(list, @"c:\etiketten\l5.QLLABEL");
            Check(list.Count == 15 && list[0] == @"c:\etiketten\l5.QLLABEL" && list.Count(x => RecentLabels.Same(x, @"C:\Etiketten\L5.qllabel")) == 1, "recent: an entry again moves to the top, once");
            Check(RecentLabels.Clean(new[] { "a", "", "A", " ", "b" }).SequenceEqual(new[] { "a", "b" }), "recent: empty and repeated entries dropped");
            Check(RecentLabels.OnNetwork(@"\\server\share\x.qllabel") && !RecentLabels.OnNetwork(@"C:\x.qllabel"), "recent: a UNC path is on the network, C: isn't");

            string key = RecentLabels.Key;
            RecentLabels.Key = Lang.SettingsKey + @"\UnitTest-" + Guid.NewGuid().ToString("N");
            try
            {
                Check(RecentLabels.Load().Count == 0, "recent: nothing stored: empty");
                RecentLabels.Add(@"C:\Etiketten\Regal 3.qllabel");
                RecentLabels.Add(@"D:\Alt\Regal 3.qllabel");
                var back = RecentLabels.Load();
                Check(back.Count == 2 && back[0] == @"D:\Alt\Regal 3.qllabel" && back[1] == @"C:\Etiketten\Regal 3.qllabel", "recent: stored and read back, two of the same name");
                RecentLabels.Remove(@"c:\etiketten\regal 3.qllabel");
                Check(RecentLabels.Load().SequenceEqual(new[] { @"D:\Alt\Regal 3.qllabel" }), "recent: one removed");
                RecentLabels.Clear();
                Check(RecentLabels.Load().Count == 0, "recent: cleared");
            }
            finally
            {
                try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(RecentLabels.Key, false); } catch (Exception) { }
                RecentLabels.Key = key;
            }
        }

        // An SVG picture: cleaned (no DTD, scripts or outside references), kept in the file, drawn as a vector at the size
        // printed (sharp where its preview would be blurred), also turned freely.
        static void Svg()
        {
            var enc = new System.Text.UTF8Encoding(false);
            string clean = enc.GetString(SvgPicture.Clean(enc.GetBytes(
                "<?xml version=\"1.0\"?><!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd\">" +
                "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"40mm\" height=\"10mm\" onload=\"x()\">" +
                "<script>x()</script><image xlink:href=\"file:///C:/secret.png\" width=\"5\" height=\"5\"/><image href=\"https://example.com/a.png\"/>" +
                "<use href=\"#a\"/><rect id=\"a\" width=\"1\" height=\"1\"/></svg>")));
            Check(!clean.Contains("DOCTYPE") && !clean.Contains("script") && !clean.Contains("onload") && !clean.Contains("secret") &&
                  !clean.Contains("example.com") && clean.Contains("href=\"#a\""), "SVG cleaned: " + clean);
            Check(clean.Contains("viewBox=") && !clean.Contains("width=\"40mm\""), "SVG gets a viewBox, loses its size");
            var size = SvgPicture.Size(SvgPicture.Clean(enc.GetBytes(clean)));
            Check(Math.Abs(size.Width / size.Height - 4) < 0.001, "SVG size from mm: " + size);
            bool refused = false;
            try { SvgPicture.Clean(enc.GetBytes("<!DOCTYPE svg [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><svg xmlns=\"http://www.w3.org/2000/svg\">&x;</svg>")); }
            catch (System.IO.InvalidDataException) { refused = true; }
            Check(refused, "SVG with an outside entity refused");
            refused = false;
            try { SvgPicture.Clean(enc.GetBytes("<html/>")); } catch (System.IO.InvalidDataException) { refused = true; }
            Check(refused, "no SVG refused");

            // CSS in <style> (as Illustrator writes it) becomes attributes: Direct2D ignores <style>
            byte[] ai = SvgPicture.Clean(enc.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 40 10\"><defs><style>.cls-1{fill:#fff;}.cls-2{fill:#1d1d1b;}</style></defs>" +
                "<rect class=\"cls-2\" x=\"0\" y=\"0\" width=\"20\" height=\"10\"/><rect class=\"cls-1\" x=\"20\" y=\"0\" width=\"20\" height=\"10\"/></svg>"));
            Check(!SvgPicture.HasUnappliedCss(ai) && enc.GetString(ai).Contains("fill=\"#1d1d1b\""), "Illustrator CSS applied: " + enc.GetString(ai));
            using (var bmp = SvgPicture.Render(ai, 40, 10))
            {
                Color dark = bmp.GetPixel(10, 5), white = bmp.GetPixel(30, 5);
                Check(dark.A == 255 && dark.R < 60 && white.A == 255 && white.R > 240, "Illustrator SVG: dark left, white right: " + dark + " " + white);
            }
            string css = enc.GetString(SvgPicture.Clean(enc.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>/* c */ @charset \"UTF-8\"; @font-face{font-family:x} rect{fill:red;stroke:blue} " +
                ".a{fill:green} #b{fill:black} rect.a.c, circle{stroke-width:3!important;fill:url(https://example.com/x)} g rect{fill:pink} " +
                ".d{fill:url(#grad)}</style><rect id=\"r1\" class=\"a\"/><rect id=\"b\" class=\"a\" fill=\"white\"/><rect id=\"r3\" class=\"a\" style=\"fill:gray\"/>" +
                "<rect id=\"r4\" class=\"c a\"/><circle id=\"r5\"/><rect id=\"r6\" class=\"d\"/></svg>")));
            Func<string, string> el = id => Regex.Match(css, "<(rect|circle) id=\"" + id + "\"[^>]*>").Value;
            Check(el("r1").Contains("fill=\"green\"") && el("r1").Contains("stroke=\"blue\""), "CSS: class beats element: " + el("r1"));
            Check(el("b").Contains("fill=\"black\""), "CSS: id beats class and the attribute: " + el("b"));
            Check(el("r3").Contains("fill=\"gray\"") && !el("r3").Contains("style="), "CSS: style attribute wins: " + el("r3"));
            Check(el("r4").Contains("stroke-width=\"3\"") && el("r5").Contains("stroke-width=\"3\""), "CSS: list and compound selectors: " + el("r4") + el("r5"));
            Check(!css.Contains("example.com") && el("r6").Contains("fill=\"url(#grad)\""), "CSS: only #references: " + css);
            Check(SvgPicture.HasUnappliedCss(enc.GetBytes(css)) && css.Contains("g rect{fill:pink}") && !css.Contains("font-face") && !css.Contains("charset"),
                  "CSS: what can't be applied stays: " + css);
            // the editor's hint: what Direct2D leaves out
            Func<string, string> missing = x => string.Join(",", SvgPicture.Missing(SvgPicture.Clean(enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\">" + x + "</svg>"))));
            Check(SvgPicture.Missing(ai).Length == 0 && missing("<rect width=\"1\" height=\"1\"/>") == "", "SVG hint: none when it all prints");
            Check(missing("<text x=\"1\" y=\"1\">A</text>") == "", "SVG hint: plain text becomes paths");
            Check(missing("<path id=\"p\" d=\"M0 0 L9 9\"/><text><textPath href=\"#p\">A</textPath></text>") == "text", "SVG hint: text on a path stays text");
            Check(missing("<defs><filter id=\"f\"/><mask id=\"m\"/></defs><rect filter=\"url(#f)\" mask=\"url(#m)\"/><text x=\"1 2\">AB</text>") == "text,filter,mask",
                  "SVG hint: text, filter, mask in a fixed order");
            Check(missing("<style>g rect{fill:red}</style><g><rect/></g>") == "css", "SVG hint: CSS that can't be applied");
            Check(SvgPicture.Missing(null).Length == 0, "SVG hint: no SVG");
            SvgText(enc);

            // the language switch restarts with the file, else the printer and the label format
            string withFile = EditorForm.RestartArguments(@"C:\a b\x.qllabel", "QL 800", "62 mm");
            Check(withFile == "\"C:\\a b\\x.qllabel\" --printer \"QL 800\"", "restart: the file and the printer: " + withFile);
            Check(EditorForm.RestartArguments(null, "QL 800", "62 mm") == "--printer \"QL 800\" --format \"62 mm\"", "restart: a new label: printer and format");
            Check(EditorForm.RestartArguments(null, "QL 800", null) == "--printer \"QL 800\"", "restart: an own length: the printer only");
            Check(EditorForm.RestartArguments(null, null, null) == "", "restart: no printer");

            // tabs: every file with its own printer (a restart in another language, a second start with several files)
            var pairs = new[] { new KeyValuePair<string, string>(@"C:\a b\x.qllabel", "QL 800"), new KeyValuePair<string, string>(@"C:\y.qllabel", null),
                                new KeyValuePair<string, string>(@"C:\z.qllabel", "QL-570") };
            string several = EditorProgram.FileArguments(pairs);
            Check(several == "--printer \"QL 800\" \"C:\\a b\\x.qllabel\" \"C:\\y.qllabel\" --printer \"QL-570\" \"C:\\z.qllabel\"", "restart: each file after its printer: " + several);
            var parsed = EditorProgram.FilePrinters(new[] { "--printer", "QL 800", @"C:\a b\x.qllabel", @"C:\y.qllabel", "--printer", "QL-570", @"C:\z.qllabel", "--lang", "de" }, 0);
            Check(parsed.Count == 3 && parsed[0].Value == "QL 800" && parsed[1].Value == null && parsed[2].Value == "QL-570" && parsed[2].Key == @"C:\z.qllabel",
                  "FilePrinters: each file with the printer before it");
            var old = EditorProgram.FilePrinters(new[] { "x.qllabel", "--printer", "QL 800", "--format", "62 mm" }, 0);
            Check(old.Count == 1 && old[0].Key == "x.qllabel" && old[0].Value == "QL 800", "FilePrinters: \"file --printer name\" as before tabs");
            Check(EditorProgram.FilePrinters(new[] { "--printer", "QL 800", "--format", "62 mm" }, 0).Count == 0, "FilePrinters: no file");
            var running = EditorProgram.ForRunning(new[] { "a.qllabel", "b.qllabel", "--printer", "QL 800" });
            Check(running.Length == 6 && running[0] == "--printer" && running[2] == System.IO.Path.GetFullPath("a.qllabel") && running[5] == System.IO.Path.GetFullPath("b.qllabel"),
                  "second start with two files: both handed over, full paths, each with the printer: " + string.Join(" | ", running));

            // F1: the page of the selected kind of object, else the contents
            Func<LabelObject[], string> f1 = sel => EditorForm.HelpPage(sel);
            Check(f1(new LabelObject[0]) == "index.html", "F1: nothing selected: contents");
            Check(f1(new LabelObject[] { new TextObject(), new TextObject() }) == "text.html", "F1: texts");
            Check(f1(new LabelObject[] { new ImageObject() }) == "pictures.html", "F1: picture");
            Check(f1(new LabelObject[] { new BarcodeObject() }) == "codes.html", "F1: code");
            Check(f1(new LabelObject[] { new LineObject(), new ShapeObject() }) == "lines-boxes.html", "F1: line and box");
            Check(f1(new LabelObject[] { new TextObject(), new ShapeObject() }) == "index.html", "F1: several kinds: contents");

            // the printer preselected: Windows' default when it is a QL printer, else the first by name
            var qls = new[] { "QL-800 b", "Brother QL-570 (Unidrv)", "QL-800 Capture" };
            Check(string.Join("|", QlPrinters.InOrder(qls, "QL-800 Capture")) == "QL-800 Capture|Brother QL-570 (Unidrv)|QL-800 b",
                  "printers: Windows' default first: " + string.Join("|", QlPrinters.InOrder(qls, "QL-800 Capture")));
            Check(string.Join("|", QlPrinters.InOrder(qls, "Microsoft Print to PDF")) == "Brother QL-570 (Unidrv)|QL-800 b|QL-800 Capture",
                  "printers: another default: by name");
            Check(QlPrinters.InOrder(qls, null).First() == "Brother QL-570 (Unidrv)", "printers: no default: by name");

            // 40 stripes across 20 mm (0.5 mm each = 6 dots): the vector shows them; its preview here is a single grey pixel.
            var svg = new System.Text.StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 40 10\">");
            for (int i = 0; i < 40; i += 2) svg.Append("<rect x=\"" + i + "\" y=\"0\" width=\"1\" height=\"10\"/>");
            svg.Append("</svg>");
            byte[] grey;
            using (var pic = new Bitmap(1, 1))
            using (var ms = new System.IO.MemoryStream())
            {
                pic.SetPixel(0, 0, Color.FromArgb(200, 200, 200));
                pic.Save(ms, System.Drawing.Imaging.ImageFormat.Png); grey = ms.ToArray();
            }
            var img = new ImageObject { X = 10, Y = 10, W = 20, H = 5, Png = grey, Svg = SvgPicture.Clean(enc.GetBytes(svg.ToString())), Mode = ImageMode.Threshold };
            var back = (ImageObject)LabelDoc.FromJson(new LabelDoc { Objects = { img } }.ToJson()).Objects[0];
            Check(back.Svg != null && back.Svg.SequenceEqual(img.Svg), "SVG kept in the file");
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            Func<ImageObject, int> stripes = o =>
            {
                var doc = new LabelDoc();
                doc.Objects.Add(o);
                using (var bmp = EditorRender.Render(doc, layout))
                {
                    var r = EditorRender.DotRect(o, layout);
                    int changes = 0;
                    for (int x = r.X + 1; x < r.Right - 1; x++) if (Black(bmp, x, r.Y + r.Height / 2) != Black(bmp, x - 1, r.Y + r.Height / 2)) changes++;
                    return changes;
                }
            };
            int n = stripes(back);
            Check(n >= 37 && n <= 41, "SVG drawn as a vector: " + n + " edges");
            back.Svg = null; back.Png = (byte[])grey.Clone();   // as Replace picture does: a new picture, converted anew
            Check(stripes(back) == 0, "without the vector: the preview");
            var turned = (ImageObject)img.Clone();
            turned.Rotation = 90; turned.W = 5; turned.H = 20;
            var doc90 = new LabelDoc();
            doc90.Objects.Add(turned);
            using (var bmp = EditorRender.Render(doc90, layout))
            {
                var r = EditorRender.DotRect(turned, layout);
                int changes = 0;
                for (int y = r.Y + 1; y < r.Bottom - 1; y++) if (Black(bmp, r.X + r.Width / 2, y) != Black(bmp, r.X + r.Width / 2, y - 1)) changes++;
                Check(changes >= 37 && changes <= 41, "SVG turned 90: " + changes + " edges");
            }
            var giant = (ImageObject)img.Clone();
            giant.X = layout.Printable.X; giant.Y = layout.Printable.Y; giant.W = 3000; giant.H = 750;
            var docG = new LabelDoc();
            docG.Objects.Add(giant);
            using (var bmp = EditorRender.Render(docG, layout)) Check(bmp != null, "giant SVG drawn");
            var tiltedGiant = (ImageObject)giant.Clone();
            tiltedGiant.Tilt = 10;
            docG.Objects[0] = tiltedGiant;
            using (var bmp = EditorRender.Render(docG, layout)) Check(bmp != null, "giant tilted SVG drawn");
            var tilted = (ImageObject)img.Clone();
            tilted.Tilt = 30;
            var docT = new LabelDoc();
            docT.Objects.Add(tilted);
            using (var bmp = EditorRender.Render(docT, layout))
            {
                var r = EditorRender.DotRect(tilted, layout);
                int black = 0;
                for (int y = r.Y; y < r.Bottom; y++) for (int x = r.X; x < r.Right; x++) black += Black(bmp, x, y);
                Check(black > 0, "SVG tilted drawn");
            }
        }

        // A dragged line end: 45° steps within 5°, Shift every 15°, the distance along the step kept; the angle shown.
        // Step 4: words with their own format. Runs keep only what differs, join to the text, survive the file and Clone;
        // a text changed without them (an older program) drops them; SetRuns takes the most frequent format as the object's.
        static void TextRuns()
        {
            var t = new TextObject { Font = "Arial", Size = 10, Kind = TextKind.Frame };
            t.SetRuns(new[] { new TextRun("Schrauben "), new TextRun("M4", bold: true), new TextRun(" verzinkt\n"), new TextRun("100 Stück", size: 8) });
            Check(t.Text == "Schrauben M4 verzinkt\n100 Stück" && t.Font == "Arial" && t.Size == 10 && !t.Bold, "runs: text joined, object format kept: " + t.Size);
            Check(t.Runs != null && t.Runs.Count == 4 && t.Runs[1].Bold == true && t.Runs[1].Font == null && t.Runs[0].Plain && t.Runs[3].Size == 8,
                  "runs: only the differences: " + (t.Runs == null ? "null" : t.Runs.Count.ToString()));
            var doc = new LabelDoc();
            doc.Objects.Add(t);
            string json = doc.ToJson();
            Check(json.Contains("\"runs\":[{\"t\":\"Schrauben \"},{\"t\":\"M4\",\"bold\":true}"), "runs: JSON: " + json);
            var back = (TextObject)LabelDoc.FromJson(json).Objects[0];
            Check(back.Runs != null && back.Runs.Count == 4 && back.Text == t.Text && LabelDoc.FromJson(json).ToJson() == json, "runs: round trip");
            var copy = (TextObject)t.Clone();
            copy.SetRuns(new[] { new TextRun("other") });
            Check(t.Runs.Count == 4 && copy.Runs == null && copy.Text == "other", "runs: a clone changes its own runs only");

            // An older program changed the text and kept the runs: they no longer fit and are dropped.
            var stale = LabelDoc.FromJson(json.Replace("\"text\":\"Schrauben M4", "\"text\":\"Muttern M4"));
            Check(((TextObject)stale.Objects[0]).Runs == null, "runs: stale runs dropped on opening");
            t.Text = "typed over";
            Check(t.FormatRuns == null && !doc.ToJson().Contains("\"runs\""), "runs: a changed text writes no runs");

            // SetRuns with filled runs (EMF text): the most frequent format becomes the object's; one format, no runs.
            var e = new TextObject();
            e.SetRuns(new[] { new TextRun("Kopf ", "Arial", 14, true, false, false), new TextRun("langer Rest der Zeile", "Arial", 9, false, false, false) });
            Check(e.Font == "Arial" && e.Size == 9 && !e.Bold && e.Runs.Count == 2 && e.Runs[0].Size == 14 && e.Runs[0].Bold == true && e.Runs[1].Plain,
                  "runs: SetRuns picks the main format: " + e.Size);
            e.SetRuns(new[] { new TextRun("a", "Arial", 9, false, false, false), new TextRun("b", "Arial", 9, false, false, false), new TextRun("") });
            Check(e.Runs == null && e.Text == "ab", "runs: one format, no runs");
            var full = new TextObject { Font = "Arial", Size = 9 };
            full.SetRuns(new[] { new TextRun("x "), new TextRun("y", italic: true) });
            var f = full.FullRuns();
            Check(f.Count == 2 && f[1].Italic == true && f[1].Font == "Arial" && f[1].Size == 9 && f[0].Italic == false, "runs: FullRuns fills every field");
            Check(TextRunFromBadJson(), "runs: damaged runs dropped");

            // Editing as the text box does it: typing takes the format before it, a range gets a format, mixed is null.
            var ed = new TextObject { Font = "Arial", Size = 10, Kind = TextKind.Frame };
            ed.SetRuns(new[] { new TextRun("ab "), new TextRun("CD", bold: true), new TextRun(" ef") });
            var fr = ed.FullRuns();
            ed.SetRuns(RunEdit.Splice(fr, 5, 0, "X", RunEdit.Common(fr, 5, 0)));   // typed right after "CD"
            Check(ed.Text == "ab CDX ef" && ed.Runs.Count == 3 && ed.Runs[1].Text == "CDX", "runs edit: typing continues the bold word");
            fr = ed.FullRuns();
            ed.SetRuns(RunEdit.Splice(fr, 2, 4, "", null));   // " CDX" deleted
            Check(ed.Text == "ab ef" && ed.Runs == null, "runs edit: deleting the bold word leaves one format");
            fr = ed.FullRuns();
            ed.SetRuns(RunEdit.Restyle(fr, 3, 2, r => new TextRun(r.Text, r.Font, 14, r.Bold, r.Italic, r.Underline)));
            Check(ed.Runs != null && ed.Runs.Count == 2 && ed.Runs[1].Text == "ef" && ed.Runs[1].Size == 14 && ed.Size == 10, "runs edit: a range gets a size");
            var mixed = RunEdit.Common(ed.FullRuns(), 0, 5);
            Check(mixed.Size == null && mixed.Bold == false && mixed.Font == "Arial", "runs edit: mixed size is null, the shared values stay");
            var crlf = new TextObject { Text = "a\r\nb" };
            Check(string.Concat(RunEdit.Normalized(crlf.FullRuns()).Select(r => r.Text)) == "a\nb", "runs edit: an older text's CR LF as the text box's LF");
            Check(RunEdit.Common(new List<TextRun>(), 0, 0) == null && RunEdit.Common(ed.FullRuns(), 0, 0).Size == 10, "runs edit: at the start the first character's format");

            // Drawn: a bold word adds ink, a larger run makes a single line taller, wrapping keeps "M4," together.
            var layout = LabelLayout.Estimate(Continuous62(30), false, false);
            Func<TextObject, int> ink = o =>
            {
                var d = new LabelDoc();
                d.Objects.Add(o);
                int n = 0;
                using (var bmp = EditorRender.Render(d, layout))
                    for (int y = 0; y < bmp.Height; y++) for (int x = 0; x < bmp.Width; x++) n += Black(bmp, x, y);
                return n;
            };
            Func<IEnumerable<TextRun>, TextObject> make = r =>
            {
                var o = new TextObject { X = 2, Y = 2, Font = "Arial", Size = 12 };
                o.SetRuns(r);
                EditorRender.FitBox(o);
                return o;
            };
            var plainT = make(new[] { new TextRun("Schrauben M4 verzinkt") });
            var boldT = make(new[] { new TextRun("Schrauben "), new TextRun("M4", bold: true), new TextRun(" verzinkt") });
            var bigT = make(new[] { new TextRun("Schrauben "), new TextRun("M4", size: 24), new TextRun(" verzinkt") });
            Check(boldT.Runs != null && ink(boldT) > ink(plainT) && boldT.W >= plainT.W, "runs drawn: bold adds ink, not narrower: " + ink(plainT) + " " + ink(boldT));
            Check(bigT.H > plainT.H * 1.5 && bigT.W > plainT.W, "runs drawn: a larger run makes the line taller: " + plainT.H + " " + bigT.H);
            Check(!EditorRender.Overflows(new TextObject { Text = "x", Kind = TextKind.Frame, W = 20, H = 10 }), "runs: a plain frame still fits");
            var frameT = new TextObject { X = 2, Y = 2, W = 14, H = 30, Font = "Arial", Size = 10, Kind = TextKind.Frame };
            frameT.SetRuns(new[] { new TextRun("aaaa "), new TextRun("M4", bold: true), new TextRun(", bbbb cccc dddd") });
            var lines = EditorRender.LinesOf(frameT);
            Check(lines.Any(l => l.Contains("M4,")) && lines.All(l => !l.EndsWith("M4")), "runs: wrapping keeps a word across formats together: " + string.Join("|", lines));
        }

        static bool TextRunFromBadJson()
        {
            string bad = "{\"format\":\"QL label\",\"version\":2,\"objects\":[{\"type\":\"text\",\"text\":\"ab\",\"runs\":[{\"t\":\"a\"},5]}]}";
            try { return ((TextObject)LabelDoc.FromJson(bad).Objects[0]).Runs == null; }
            catch (Exception) { return false; }
        }

        static void LineSnap()
        {
            var o = new PointF(10, 20);
            Func<PointF?, float, float, bool> at = (p, x, y) => p.HasValue && Math.Abs(p.Value.X - x) < 0.01 && Math.Abs(p.Value.Y - y) < 0.01;
            double r = 43 * Math.PI / 180;   // 43° rising (y down on the label): onto 45°, same distance along it
            var near = EditorCanvas.SnapLineEnd(o, new PointF((float)(10 + 18 * Math.Cos(r)), (float)(20 - 18 * Math.Sin(r))), false);
            double d = 18 * Math.Cos(2 * Math.PI / 180);
            Check(at(near, (float)(10 + d * Math.Sqrt(0.5)), (float)(20 - d * Math.Sqrt(0.5))), "line snap: 43° -> 45°");
            Check(!EditorCanvas.SnapLineEnd(o, new PointF((float)(10 + 18 * Math.Cos(Math.PI / 6)), 11), false).HasValue, "line snap: 30° stays free");
            Check(at(EditorCanvas.SnapLineEnd(o, new PointF(30, 21.5f), false), 30, 20), "line snap: nearly level -> level, y exact");
            Check(at(EditorCanvas.SnapLineEnd(o, new PointF(9, 5), false), 10, 5), "line snap: nearly upright -> upright, x exact");
            var shift = EditorCanvas.SnapLineEnd(o, new PointF((float)(10 + 20 * Math.Cos(Math.PI / 6)), 10), true);
            Check(shift.HasValue && Math.Abs(EditorCanvas.LineAngle(o, shift.Value) - 30) < 0.05, "line snap: Shift 15° steps (30°)");
            Check(!EditorCanvas.SnapLineEnd(o, o, false).HasValue, "line snap: no length");
            Check(EditorCanvas.LineAngle(new PointF(0, 10), new PointF(10, 0)) == 45 && EditorCanvas.LineAngle(new PointF(0, 0), new PointF(10, 10)) == 135 &&
                  EditorCanvas.LineAngle(new PointF(10, 5), new PointF(0, 5)) == 0, "line angle 0-180°");
        }

        // A length of its own on continuous tape: saved and read back, rounded to 0.1 mm, the models' limits.
        static void OwnLength()
        {
            var doc = LabelDoc.FromJson(new LabelDoc { PaperId = 259, Length01 = 403, LengthMode = "auto" }.ToJson());
            Check(doc.LengthMode == "auto" && doc.Length01 == 403 && doc.PaperId == 259 && doc.FormName == "", "own length saved: " + doc.LengthMode + " " + doc.Length01);
            Check(!new LabelDoc().ToJson().Contains("\"length\""), "no length mode for a label format");
            Check(LabelDoc.FromJson(new LabelDoc { LengthMode = "own" }.ToJson().Replace("\"own\"", "\"wild\"")).LengthMode == "", "unknown length mode ignored");
            var m = MediaTable.ById(259);
            var own = LabelChoice.Own(m, 40.04);
            Check(own.Custom && own.LengthMm == 40.0 && own.Media == m && own.ToString().EndsWith("own length"), "own choice: " + own);
            var ql800 = MediaTable.Models.First(x => x.Name == "QL-800");
            var wide = MediaTable.Models.First(x => x.Name == "QL-1115NWB");
            Check(ql800.MinLength01 == 127 && ql800.MaxLength01 == 10000 && wide.MinLength01 == 254 && wide.MaxLength01 == 30000,
                  "length limits from the GPD: QL-800 " + ql800.MinLength01 + "-" + ql800.MaxLength01 + ", QL-1115NWB " + wide.MinLength01 + "-" + wide.MaxLength01);

            // the side margins on a custom page: driver 0.16 reports 36 / 0 dots, 0.17 the whole paper width (the plug-in
            // crops the band); both give the tape's band from the paper table (62 mm: 696 dots, 18 at each side)
            var g16 = new LabelGeometry { DpiX = 300, DpiY = 300, PageWidth = 732, Width = 696, OffsetX = 36, PageHeight = 472, Height = 436, OffsetY = 18 };
            var g17 = new LabelGeometry { DpiX = 300, DpiY = 300, PageWidth = 732, Width = 732, OffsetX = 0, PageHeight = 472, Height = 436, OffsetY = 18 };
            var l16 = LabelLayout.FromGeometry(own, g16, null, "x", false);
            var l17 = LabelLayout.FromGeometry(own, g17, null, "x", false);
            Check(l16.SkipX == 0 && l16.DotsY == 696 && Math.Abs(l16.Printable.Top - 1.524) < 0.01, "custom page, driver 0.16: skip " + l16.SkipX + ", band " + l16.DotsY + ", side " + l16.Printable.Top);
            Check(l17.SkipX == 18 && l17.DotsY == 696 && Math.Abs(l17.Printable.Top - 1.524) < 0.01 && l17.Printable == l16.Printable,
                  "custom page, driver 0.17: skip " + l17.SkipX + ", band " + l17.DotsY + ", side " + l17.Printable.Top);
            var lf = LabelLayout.FromGeometry(new LabelChoice { Name = "62mm", Media = m, LengthMm = 40 }, g17, null, "x", false);
            Check(lf.SkipX == 0 && lf.DotsY == 732, "a label format keeps what the driver reports");
        }

        // White on black: text (a black box 1 mm beyond its frame) and QR codes (the quiet zone black); not Code 128.
        static void Invert()
        {
            var doc = new LabelDoc();
            doc.Objects.Add(new TextObject { X = 10, Y = 10, W = 20, H = 8, Text = "AB", Size = 14, Invert = true });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Qr, X = 35, Y = 10, W = 15, H = 15, Data = "invert", Invert = true });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Code128, X = 10, Y = 30, W = 30, H = 10, Data = "AB12", Invert = true });
            var back = LabelDoc.FromJson(doc.ToJson());
            Check(((TextObject)back.Objects[0]).Invert && ((BarcodeObject)back.Objects[1]).Invert && !((BarcodeObject)back.Objects[2]).Invert,
                  "invert saved for text and QR, not for Code 128");
            Check(!new TextObject().ToJson().ContainsKey("invert"), "no invert key when off");
            var label = new LabelChoice { Name = "62mm", Media = MediaTable.ById(259), LengthMm = 70, FeedMm = 3 };
            var layout = LabelLayout.Estimate(label, false, false);
            using (var bmp = EditorRender.Render(back, layout))
            {
                Func<double, double, bool> black = (x, y) =>
                {
                    var c = bmp.GetPixel((int)((x - layout.Printable.X) * 300 / 25.4), (int)((y - layout.Printable.Y) * 300 / 25.4));
                    return c.A > 0 && c.R < 128;
                };
                Check(black(9.4, 9.4) && black(30.6, 18.6) && !black(8.6, 9.4) && !black(31.4, 18.6), "inverted text: black box 1 mm beyond its frame");
                int white = 0;
                for (double x = 11; x < 29; x += 0.1) for (double y = 11; y < 17; y += 0.1) if (!black(x, y)) white++;
                Check(white > 100, "inverted text: white letters in the box (" + white + " samples)");
                Check(black(34, 9) && black(51, 26), "inverted QR: quiet zone black");
                // in the code every module swapped: compare with the same code not inverted
                var plain = LabelDoc.FromJson(back.ToJson());
                ((BarcodeObject)plain.Objects[1]).Invert = false;
                using (var normal = EditorRender.Render(plain, layout))
                {
                    int same = 0, all = 0;
                    for (double x = 35.2; x < 49.8; x += 0.2)
                        for (double y = 10.2; y < 24.8; y += 0.2, all++)
                        {
                            int px = (int)((x - layout.Printable.X) * 300 / 25.4), py = (int)((y - layout.Printable.Y) * 300 / 25.4);
                            if ((bmp.GetPixel(px, py).R < 128) == (normal.GetPixel(px, py).R < 128)) same++;
                        }
                    Check(same * 50 < all, "inverted QR: the modules swapped (" + same + " of " + all + " samples the same)");
                }
                Check(!black(10, 29.5) && !black(9, 35), "Code 128 stays dark on light (its quiet zone clear)");
            }
        }

        // "Reset rotation": back to 0°, a free angle and quarter turns alike (SetAngle(0)).
        static void ResetRotation()
        {
            var t = new TextObject { X = 10, Y = 10, W = 20, H = 8, Text = "NEU" };
            t.SetAngle(90 - 15);
            var b = new BarcodeObject { Kind = Symbology.Qr, X = 30, Y = 10, W = 12, H = 12 };
            b.SetAngle(270);
            Check(t.Rotation == 90 && t.Tilt == -15 && b.Rotation == 270, "turned before: " + t.Rotation + "+" + t.Tilt + ", " + b.Rotation);
            t.SetAngle(0); b.SetAngle(0);
            Check(t.Rotation == 0 && t.Tilt == 0 && t.Angle == 0 && b.Rotation == 0 && b.Angle == 0, "reset: " + t.Rotation + "+" + t.Tilt + ", " + b.Rotation);
        }

        // While dragging only the part around the dragged objects is drawn again: the same dots as a full render.
        static void RenderPart()
        {
            var doc = new LabelDoc();
            doc.Objects.Add(new ShapeObject { X = 2, Y = 2, W = 60, H = 40, Thickness = 0.5 });
            doc.Objects.Add(new BarcodeObject { Kind = Symbology.Qr, X = 30, Y = 8, W = 15, H = 15, Data = "part" });
            doc.Objects.Add(new TextObject { X = 8, Y = 10, W = 20, H = 8, Text = "Regal 3", Size = 14, Bold = true });
            doc.Objects.Add(new LineObject { X = 5, Y = 30, W = 40, H = 0, Thickness = 0.5 });
            doc.Objects.Add(new TextObject { X = 40, Y = 30, W = 20, H = 8, Text = "NEU", Size = 12, Invert = true });
            var label = new LabelChoice { Name = "62mm", Media = MediaTable.ById(259), LengthMm = 70, FeedMm = 3 };
            var layout = LabelLayout.Estimate(label, false, false);
            var now = DateTime.Now;
            Func<Bitmap, int[]> dots = bmp =>
            {
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                var all = new int[bmp.Width * bmp.Height];
                for (int y = 0; y < bmp.Height; y++) System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, all, y * bmp.Width, bmp.Width);
                bmp.UnlockBits(data);
                return all;
            };
            using (var shown = EditorRender.Render(doc, layout, now))
            {
                // the text turned and moved over the QR code, the inverted one moved onto the line: step by step as in a drag
                var t = (TextObject)doc.Objects[2];
                var inv = (TextObject)doc.Objects[4];
                var before = EditorCanvas.BoxOf(new LabelObject[] { t, inv });
                for (int step = 1; step <= 5; step++)
                {
                    t.SetAngle(step * 13); EditorRender.FitText(t); t.X += 3; t.Y += 0.7;
                    inv.X -= 4; inv.Y -= 0.5;
                    var after = EditorCanvas.BoxOf(new LabelObject[] { t, inv });
                    EditorRender.RenderPart(shown, doc, layout, RectangleF.Union(before, after), now, null);
                    before = after;
                }
                using (var full = EditorRender.Render(doc, layout, now))
                {
                    int[] a = dots(shown), b = dots(full);
                    int differ = Enumerable.Range(0, a.Length).Count(i => a[i] != b[i]);
                    Check(differ == 0, "render part while dragging = full render (" + differ + " dots differ)");
                }
            }
            // a picture being dragged: its draft (converted once, then only turned) prints about as dark as the exact one
            var pic = new ImageObject { X = 10, Y = 8, W = 30, H = 20 };
            using (var src = new Bitmap(300, 200))
            {
                using (var g = Graphics.FromImage(src))
                using (var lg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 300, 200), Color.White, Color.Black, 0f))
                    g.FillRectangle(lg, 0, 0, 300, 200);
                using (var ms = new System.IO.MemoryStream()) { src.Save(ms, System.Drawing.Imaging.ImageFormat.Png); pic.Png = ms.ToArray(); }
            }
            pic.SetAngle(30);
            var one = new LabelDoc();
            one.Objects.Add(pic);
            using (var exact = EditorRender.Render(one, layout, now))
            using (var draft = EditorRender.Render(one, layout, now, new LabelObject[] { pic }))
            {
                int e = dots(exact).Count(v => v == unchecked((int)0xFF000000)), d = dots(draft).Count(v => v == unchecked((int)0xFF000000));
                Check(e > 1000 && Math.Abs(d - e) * 10 < e, "dragged picture: draft " + d + " black dots, exact " + e);
            }
            // the editor's render after a drop (EditorRender.Later): a picture not converted at its new size is drawn from
            // its draft at once and converted on a worker thread; then the same dots as a print render
            var context = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Forms.WindowsFormsSynchronizationContext());
            try
            {
                pic.W = 33.3;
                bool ready = false;
                Bitmap quick;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                using (EditorRender.Later(() => ready = true)) quick = EditorRender.Render(one, layout, now);
                Check(!ready, "after a drop: drawn at once, the picture converted later");
                while (!ready && sw.ElapsedMilliseconds < 10000) { System.Windows.Forms.Application.DoEvents(); Thread.Sleep(5); }
                using (quick)
                using (var later = EditorRender.Render(one, layout, now))
                using (var exact = EditorRender.Render(one, layout, now))
                    Check(ready && dots(later).SequenceEqual(dots(exact)) && !dots(quick).SequenceEqual(dots(exact)),
                          "after a drop: the converted picture arrives (" + sw.ElapsedMilliseconds + " ms) and matches a print render");
            }
            finally { SynchronizationContext.SetSynchronizationContext(context); }
        }

        // The barcode types in the panel: GS1-128 (elements in brackets) and Code 128 with control characters as tokens.
        static void Gs1Ui()
        {
            var now = new DateTime(2026, 11, 1);
            var g = new BarcodeObject { Kind = Symbology.Gs1_128, X = 5, Y = 5, W = 50, H = 15, Data = "(01)02000000000008(10)AB-12" };
            var back = (BarcodeObject)LabelDoc.FromJson(new LabelDoc { Objects = { g } }.ToJson()).Objects[0];
            Check(back.Kind == Symbology.Gs1_128 && back.ToJson()["symbology"].Equals("gs1-128"), "GS1-128 saved as \"gs1-128\"");
            Check(g.Problem(now) == null, "GS1-128 example prints: " + g.Problem(now));
            Check(g.Caption(g.Content(now)) == "(01)02000000000008(10)AB-12", "GS1-128 text under the bars with brackets");
            Check(g.Encoded(g.Content(now)) != null && g.Encoded(g.Content(now))[0] == Code128.Fnc1, "GS1-128 encoded with FNC1 first");
            g.Data = "(01)123";
            Check(g.Problem(now) != null && g.Problem(now).StartsWith("GS1-128"), "GS1-128 error in the problem line: " + g.Problem(now));
            g.Data = "(01)02000000000008(17){date:yyMMdd}";
            Check(g.Problem(now) == null && g.Content(now) == "(01)02000000000008(17)261101", "GS1-128 with a date field: " + g.Content(now));
            var c = new BarcodeObject { Kind = Symbology.Code128, X = 5, Y = 25, W = 50, H = 15, Data = "A{tab}B{gs}C" };
            Check(c.Content(now) == "A	BC" && c.Problem(now) == null, "Code 128 control character tokens");
            Check(c.Caption(c.Content(now)) == "ABC", "Code 128 text under the bars without control characters");
            var label = new LabelChoice { Name = "62mm", Media = MediaTable.ById(259), LengthMm = 70, FeedMm = 3 };
            var layout = LabelLayout.Estimate(label, false, false);
            g.Data = "(01)02000000000008(10)AB-12";
            var doc = new LabelDoc();
            doc.Objects.Add(g); doc.Objects.Add(c);
            using (var bmp = EditorRender.Render(doc, layout, now))
            {
                int black = 0;
                for (int x = 0; x < bmp.Width; x += 2) for (int y = 0; y < bmp.Height; y += 2) if (bmp.GetPixel(x, y).R < 128) black++;
                Check(black > 2000, "GS1-128 and Code 128 drawn (" + black + " black samples)");
            }
            Check(LabelDoc.FromJson("{\"format\":\"QL label\",\"version\":1,\"objects\":[{\"type\":\"barcode\",\"symbology\":\"code128\",\"data\":\"X\"}]}").Objects[0] is BarcodeObject,
                  "older files: code128 still read");
            // the text under the bars shrinks to the width it has (it was wrapped out of sight); too narrow: the panel's hint
            var narrow = new BarcodeObject { Kind = Symbology.Gs1_128, X = 5, Y = 5, W = 30, H = 15, Data = "(01)00000000000000(10)ABC123" };
            Check(EditorRender.CaptionFits(narrow, layout, now), "GS1-128 30 mm wide: the text fits (smaller)");
            var tiny = new BarcodeObject { Kind = Symbology.Ean13, X = 5, Y = 5, W = 9, H = 6, Data = "0000000000000" };
            Check(!EditorRender.CaptionFits(tiny, layout, now), "EAN-13 9 mm wide: the text doesn't fit");
            // the panel's warnings: bars narrower than 3 dots (EAN-13: below 80 % of 0.33 mm), partly outside the printable area
            var fine = new BarcodeObject { Kind = Symbology.Code128, X = 5, Y = 5, W = 50, H = 15, Data = "ABC-123" };
            Check(EditorRender.BarcodeHint(fine, layout, now) == null, "Code 128 50 mm wide: no hint (" + EditorRender.BarcodeHint(fine, layout, now) + ")");
            var thin = new BarcodeObject { Kind = Symbology.Code128, X = 5, Y = 5, W = 12, H = 15, Data = "ABC-123", ShowText = false };
            string thinHint = EditorRender.BarcodeHint(thin, layout, now);
            Check(thinHint != null && thinHint.Contains("narrow") && EditorRender.ModuleMm(thin, layout, now) < 0.25, "Code 128 12 mm wide: bars too narrow (" + thinHint + ")");
            var ean = new BarcodeObject { Kind = Symbology.Ean13, X = 5, Y = 5, W = 37.3, H = 20, Data = "0000000000000" };
            double eanMm = EditorRender.ModuleMm(ean, layout, now);
            Check((EditorRender.BarcodeHint(ean, layout, now) != null) == (eanMm < 0.264), "EAN-13 at 100 %: hint only below 80 % (" + eanMm + " mm)");
            // narrower than one dot per bar: drawn wider than its box and cut off, so it can't be printed
            var gs1 = new BarcodeObject { Kind = Symbology.Gs1_128, X = 5, Y = 5, W = 12, H = 15, Data = "(01)00000000000000(10)ABC123" };
            string tooNarrow = EditorRender.Problem(gs1, layout, now);
            Check(tooNarrow != null && tooNarrow.StartsWith("Too narrow: at least") && EditorRender.BarcodeHint(gs1, layout, now) == null, "GS1-128 12 mm wide: " + tooNarrow);
            gs1.W = 40;
            Check(EditorRender.Problem(gs1, layout, now) == null, "GS1-128 40 mm wide: printable");
            // narrower than its bars but within box + quiet zones: drawn whole over the quiet zones (measured on the printer),
            // so a hint, not a problem; one quiet zone narrower still: cut, a problem (printing then asks)
            var c8 = new BarcodeObject { Kind = Symbology.Code128, X = 5, Y = 5, W = 8, H = 14, Data = "ABC-123" };
            string c8Hint = EditorRender.BarcodeHint(c8, layout, now);
            Check(EditorRender.Problem(c8, layout, now) == null && c8Hint != null && c8Hint.StartsWith("The code is wider than its frame"),
                  "Code 128 8 mm (112 bars in 95 dots): hint " + c8Hint);
            c8.W = 6;
            Check(EditorRender.Problem(c8, layout, now) != null, "Code 128 6 mm (112 bars in 71 + 20 dots): too narrow");
            var qr = new BarcodeObject { Kind = Symbology.Qr, X = 5, Y = 5, W = 1.5, H = 1.5, Data = "R3-M4-20" };
            Check(EditorRender.Problem(qr, layout, now) != null && EditorRender.Problem(new BarcodeObject { Kind = Symbology.Qr, X = 5, Y = 5, W = 10, H = 10, Data = "R3-M4-20" }, layout, now) == null,
                  "QR code 1.5 mm: too small, 10 mm: fine");
            var cut = new BarcodeObject { Kind = Symbology.Code128, X = layout.Printable.X - 2, Y = 5, W = 50, H = 15, Data = "ABC-123" };
            string cutHint = EditorRender.BarcodeHint(cut, layout, now);
            Check(cutHint != null && cutHint.Contains("printable area"), "Code 128 reaching past the printable area: hint (" + cutHint + ")");
        }

        // A line's angle and length typed in the panel: the left end (a vertical line's lower end) stays.
        static void LineAngleLength()
        {
            var l = new LineObject { X = 10, Y = 20, W = 30, H = 0 };
            Check(l.LineAngle == 0 && l.Length == 30, "level line: 0°, 30 mm");
            l.SetAngleLength(45, 20);
            Check(l.Rising && Math.Abs(l.X - 10) < 0.01 && Math.Abs(l.Y + l.H - 20) < 0.01 && Math.Abs(l.W - 14.14) < 0.01 && l.LineAngle == 45, "45°: up to the right from the left end");
            l.SetAngleLength(135, 20);
            Check(!l.Rising && Math.Abs(l.X - 10) < 0.01 && Math.Abs(l.Y - 20) < 0.01 && l.LineAngle == 135 && Math.Abs(l.Length - 20) < 0.01, "135°: down to the right from the left end");
            l.SetAngleLength(90, 15);
            Check(l.W == 0 && Math.Abs(l.Y + l.H - 20) < 0.01 && l.LineAngle == 90 && Math.Abs(l.Length - 15) < 0.01, "90°: upright, the lower end stays");
            l.SetAngleLength(90, 25);
            Check(Math.Abs(l.Y + l.H - 20) < 0.01 && Math.Abs(l.H - 25) < 0.01, "upright line longer: the lower end stays");
            l.SetAngleLength(0, 25);
            Check(l.H == 0 && Math.Abs(l.X - 10) < 0.01 && Math.Abs(l.Y - 20) < 0.01 && l.W == 25, "back to level from the lower end");
        }

        // ---------- barcodes (examples from the standards / Wikipedia) ----------
        static void Barcodes()
        {
            for (int v = 0; v <= 106; v++)
            {
                string p = Code128.PatternOf(v);
                Check(p.Sum(c => c - '0') == (v == 106 ? 13 : 11) && p.Length == (v == 106 ? 7 : 6), "Code 128 pattern " + v);
            }
            // "PJJ123C": start B, P J J 1 2 3 C in set B (the digit run is too short for set C), checksum 879 mod 103 = 55.
            Check(string.Join(",", Code128.Values("PJJ123C")) == "104,48,42,42,17,18,19,35,55,106", "Code 128 PJJ123C: " + string.Join(",", Code128.Values("PJJ123C")));
            // Digits only: start C and pairs; (105 + 12 + 2*34) mod 103 = 82.
            Check(string.Join(",", Code128.Values("1234")) == "105,12,34,82,106", "Code 128 1234 in set C");
            // Odd digit run at the start: first digit in set B, then set C.
            Check(string.Join(",", Code128.Values("12345")).StartsWith("104,17,99,23,45,"), "Code 128 12345: " + string.Join(",", Code128.Values("12345")));
            Check(Code128.Modules("1234").Length == 11 * 4 + 13, "Code 128 module count (start, 12, 34, checksum, stop)");
            Check(Code128.Check("Größe") != null && Code128.Check("ABC-123") == null, "Code 128 accepts printable ASCII only");

            Check(Ean13.CheckDigit("200000000000") == 8, "EAN-13 check digit (2000000000008, an in-store number)");
            Check(Ean13.Check("2000000000008") == null && Ean13.Check("2000000000009") != null && Ean13.Check("20000000000") != null, "EAN-13 checks");
            bool[] guards;
            var m = Ean13.Modules("200000000000", out guards);
            Check(m.Length == 95 && m[0] && !m[1] && m[2] && !m[45] && m[46] && !m[47] && m[48] && !m[49] && m[92] && !m[93] && m[94], "EAN-13 guards");
            // First digit 2 = parity LLGLGG; second digit 0 in L = 0001101.
            Check(new string(m.Skip(3).Take(7).Select(b => b ? '1' : '0').ToArray()) == "0001101", "EAN-13 first left digit");
            // Fourth digit 0 in G (reversed complement of L): 0100111.
            Check(new string(m.Skip(17).Take(7).Select(b => b ? '1' : '0').ToArray()) == "0100111", "EAN-13 G parity");
            // The check digit 8 last: R(8) = 1001000.
            Check(new string(m.Skip(85).Take(7).Select(b => b ? '1' : '0').ToArray()) == "1001000", "EAN-13 R code of the check digit");

            // Drawn: whole dots per module, centred, pure black/white.
            var b1 = new BarcodeObject { Kind = Symbology.Ean13, Data = "200000000000" };
            using (var bmp = EditorRender.BarcodeBitmap(b1, 600, 200, 300, 300, DateTime.Now))
            {
                int mx = 600 / (11 + 95 + 7), x0 = (600 - mx * 113) / 2 + 11 * mx;
                Check(Black(bmp, x0, 5) == 1 && Black(bmp, x0 + mx, 5) == 0 && Black(bmp, x0 + 2 * mx, 5) == 1 && bmp.GetPixel(x0 - 1, 5).A == 0,
                      "EAN-13 drawn with " + mx + " dots per module, the quiet zone transparent");
            }
            Check(EditorRender.BarcodeBitmap(new BarcodeObject { Kind = Symbology.Ean13, Data = "123" }, 600, 200, 300, 300, DateTime.Now) == null,
                  "an invalid barcode draws nothing");
        }

        static void Qr()
        {
            // ISO/IEC 18004 example "HELLO WORLD" 1-M: data codewords -> error correction codewords.
            var data = new byte[] { 0x20, 0x5B, 0x0B, 0x78, 0xD1, 0x72, 0xDC, 0x4D, 0x43, 0x40, 0xEC, 0x11, 0xEC, 0x11, 0xEC, 0x11 };
            var ecc = QrCode.Remainder(data, 10);
            Check(BitConverter.ToString(ecc) == "C4-23-27-77-EB-D7-E7-E2-5D-17", "QR Reed-Solomon: " + BitConverter.ToString(ecc));
            Check(QrCode.FormatBits(1, 0) == Convert.ToInt32("101010000010010", 2), "QR format bits M/0");
            Check(QrCode.FormatBits(0, 0) == Convert.ToInt32("111011111000100", 2), "QR format bits L/0");
            Check(QrCode.VersionBits(7) == Convert.ToInt32("000111110010010100", 2), "QR version 7 bits");
            Check(string.Join(",", QrCode.AlignmentPositions(7)) == "6,22,38" && string.Join(",", QrCode.AlignmentPositions(32)) == "6,34,60,86,112,138" &&
                  string.Join(",", QrCode.AlignmentPositions(40)) == "6,30,58,86,114,142,170", "QR alignment positions");
            // Byte capacities from the standard's table (L, M, Q, H).
            var caps = new Dictionary<int, int[]>
            {
                { 1, new[] { 17, 14, 11, 7 } }, { 2, new[] { 32, 26, 20, 14 } }, { 5, new[] { 106, 84, 60, 44 } },
                { 7, new[] { 154, 122, 86, 64 } }, { 10, new[] { 271, 213, 151, 119 } }, { 20, new[] { 858, 666, 482, 382 } },
                { 40, new[] { 2953, 2331, 1663, 1273 } },
            };
            foreach (var c in caps)
                for (int l = 0; l < 4; l++)
                    Check(QrCode.Capacity(c.Key, l) == c.Value[l], "QR capacity version " + c.Key + " level " + "LMQH"[l] + ": " + QrCode.Capacity(c.Key, l));
            // Encode and read back (format bits, unmasking, codeword order, block interleaving, byte mode).
            foreach (var text in new[] { "HELLO WORLD", "https://example.org/label?id=12345", "Grüße – 62 mm", new string('x', 300) })
                for (int l = 0; l < 4; l++)
                {
                    var q = QrCode.Encode(text, l);
                    string back = QrRead(q, l);
                    Check(back == text, "QR read back level " + "LMQH"[l] + ", " + text.Length + " chars: " + (back ?? "(null)"));
                }
        }

        // A small reader for the symbols QrCode makes (the test's check that placement and masking fit together).
        static string QrRead(bool[,] m, int expectLevel)
        {
            int size = m.GetLength(0), ver = (size - 17) / 4;
            int fmt = 0;
            for (int i = 0; i <= 5; i++) if (m[i, 8]) fmt |= 1 << i;
            if (m[7, 8]) fmt |= 1 << 6;
            if (m[8, 8]) fmt |= 1 << 7;
            if (m[8, 7]) fmt |= 1 << 8;
            for (int i = 9; i < 15; i++) if (m[8, 14 - i]) fmt |= 1 << i;
            int mask = -1, level = -1;
            for (int l = 0; l < 4; l++) for (int k = 0; k < 8; k++) if (QrCode.FormatBits(l, k) == fmt) { level = l; mask = k; }
            if (level != expectLevel) return null;
            // Function modules: as the encoder marks them (a fresh symbol's layout), found by re-encoding is circular;
            // instead rebuild them here.
            var function = new bool[size, size];
            Action<int, int> f = (x, y) => { if (x >= 0 && x < size && y >= 0 && y < size) function[y, x] = true; };
            for (int i = 0; i < size; i++) { f(6, i); f(i, 6); }
            foreach (var c in new[] { new[] { 3, 3 }, new[] { size - 4, 3 }, new[] { 3, size - 4 } })
                for (int dy = -4; dy <= 4; dy++) for (int dx = -4; dx <= 4; dx++) f(c[0] + dx, c[1] + dy);
            var pos = QrCode.AlignmentPositions(ver);
            for (int i = 0; i < pos.Length; i++)
                for (int j = 0; j < pos.Length; j++)
                    if (!(i == 0 && j == 0 || i == 0 && j == pos.Length - 1 || i == pos.Length - 1 && j == 0))
                        for (int dy = -2; dy <= 2; dy++) for (int dx = -2; dx <= 2; dx++) f(pos[i] + dx, pos[j] + dy);
            for (int i = 0; i < 9; i++) { f(8, i); f(i, 8); }
            for (int i = 0; i < 8; i++) { f(size - 1 - i, 8); f(8, size - 1 - i); }
            if (ver >= 7) for (int i = 0; i < 18; i++) { f(size - 11 + i % 3, i / 3); f(i / 3, size - 11 + i % 3); }
            var bits = new List<bool>();
            for (int right = size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j, y = ((right + 1) & 2) == 0 ? size - 1 - vert : vert;
                        if (function[y, x]) continue;
                        bool v = m[y, x];
                        bool inv;
                        switch (mask)
                        {
                            case 0: inv = (x + y) % 2 == 0; break;
                            case 1: inv = y % 2 == 0; break;
                            case 2: inv = x % 3 == 0; break;
                            case 3: inv = (x + y) % 3 == 0; break;
                            case 4: inv = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: inv = x * y % 2 + x * y % 3 == 0; break;
                            case 6: inv = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            default: inv = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        }
                        bits.Add(v ^ inv);
                    }
            }
            var raw = new byte[QrCode.RawDataModules(ver) / 8];
            for (int i = 0; i < raw.Length * 8; i++) if (bits[i]) raw[i >> 3] |= (byte)(0x80 >> (i & 7));
            // De-interleave: compare with the encoder's own interleaving of the data we expect is circular, so rebuild
            // the data codewords from the block structure the standard defines through DataCodewords.
            int dataLen = QrCode.DataCodewords(ver, level);
            // Find the block layout by trying: blocks of equal ECC length; numBlocks from the total ECC codewords.
            for (int numBlocks = 1; numBlocks <= 81; numBlocks++)
            {
                int eccTotal = raw.Length - dataLen;
                if (eccTotal % numBlocks != 0) continue;
                int eccLen = eccTotal / numBlocks, numShort = numBlocks - raw.Length % numBlocks, shortLen = raw.Length / numBlocks;
                if (shortLen - eccLen <= 0) continue;
                var blocks = new List<byte>[numBlocks];
                for (int j = 0; j < numBlocks; j++) blocks[j] = new List<byte>();
                int k = 0;
                for (int i = 0; i < shortLen + 1 && k < raw.Length; i++)
                    for (int j = 0; j < numBlocks && k < raw.Length; j++)
                        if (i != shortLen - eccLen || j >= numShort) blocks[j].Add(raw[k++]); else blocks[j].Add(0);
                var dat = new List<byte>();
                bool ok = true;
                for (int j = 0; j < numBlocks; j++)
                {
                    int len = shortLen - eccLen + (j < numShort ? 0 : 1);
                    var d = blocks[j].Take(j < numShort ? shortLen - eccLen : len).ToArray();
                    var e = blocks[j].Skip(j < numShort ? shortLen - eccLen + 1 : len).Take(eccLen).ToArray();
                    if (!QrCode.Remainder(d, eccLen).SequenceEqual(e)) { ok = false; break; }
                    dat.AddRange(d);
                }
                if (!ok) continue;
                // Byte mode segment.
                var b = dat.ToArray();
                Func<int, int, int> read = (start, len) => { int v = 0; for (int i = 0; i < len; i++) v = v << 1 | ((b[(start + i) >> 3] >> (7 - ((start + i) & 7))) & 1); return v; };
                if (read(0, 4) != 4) return null;
                int cb = ver < 10 ? 8 : 16, n = read(4, cb);
                var text = new byte[n];
                for (int i = 0; i < n; i++) text[i] = (byte)read(4 + cb + 8 * i, 8);
                return System.Text.Encoding.UTF8.GetString(text);
            }
            return null;
        }

        static void FieldsAndFit()
        {
            var now = new DateTime(2026, 10, 29, 14, 5, 0);
            Check(Fields.Expand("{date:yyyy-MM-dd}", now) == "2026-10-29", "date field");
            Check(Fields.Expand("MHD {date+30:dd.MM.yyyy}", now) == "MHD 28.11.2026", "date + 30 days");
            Check(Fields.Expand("{date-1:ddd}", now) == now.AddDays(-1).ToString("ddd"), "date - 1 day");
            Check(Fields.Expand("{time:HH:mm}", now) == "14:05", "time field with format");
            Check(Fields.Expand("{{date}} {foo} {date:yyyy}", now) == "{date} {foo} 2026", "braces and unknown fields");
            Check(Fields.HasFields("Today {date}") && !Fields.HasFields("Hello {name}"), "HasFields");
            var b = new BarcodeObject { Kind = Symbology.Code128, Data = "LOT{date:yyMMdd}" };
            Check(b.Content(now) == "LOT261029" && b.Problem(now) == null, "fields in barcodes");

            var json = LabelDoc.FromJson(new LabelDoc { Objects = { b } }.ToJson());
            Check(((BarcodeObject)json.Objects[0]).Data == "LOT{date:yyMMdd}", "barcode with fields saved");
        }

        static Dictionary<string, string> F(params string[] kv)
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[kv[i]] = kv[i + 1];
            return d;
        }

        // QR content types: the text each builds, escaping (fields left alone), the file, read-back.
        static void QrTypes()
        {
            Check(QrContent.Build(QrType.Url, F("url", "example.org/a?b=1")) == "https://example.org/a?b=1", "URL gets https://");
            Check(QrContent.Build(QrType.Url, F("url", " http://x.de ")) == "http://x.de", "URL with a scheme stays");
            string vcard = QrContent.Build(QrType.Contact, F("firstName", "Anna", "lastName", "Muster; Söhne", "company", "A, B", "phone", "+49 30 123",
                                                             "email", "a@b.de", "street", "Hauptstr. 1", "city", "10115 Berlin", "website", "https://b.de"));
            Check(vcard == "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Muster\\; Söhne;Anna;;;\r\nFN:Anna Muster\\; Söhne\r\nORG:A\\, B\r\nTEL;TYPE=CELL:+49 30 123\r\n" +
                           "EMAIL:a@b.de\r\nADR:;;Hauptstr. 1;10115 Berlin;;;\r\nURL:https://b.de\r\nEND:VCARD", "vCard: " + vcard);
            Check(QrContent.Build(QrType.Wifi, F("ssid", "My;Net", "password", "p:a\\s\"s,", "security", "WPA")) == "WIFI:T:WPA;S:My\\;Net;P:p\\:a\\\\s\\\"s\\,;;",
                  "Wi-Fi escaping: " + QrContent.Build(QrType.Wifi, F("ssid", "My;Net", "password", "p:a\\s\"s,", "security", "WPA")));
            Check(QrContent.Build(QrType.Wifi, F("ssid", "Gast", "password", "x", "security", "nopass", "hidden", "true")) == "WIFI:T:nopass;S:Gast;H:true;;", "open hidden Wi-Fi");
            Check(QrContent.Build(QrType.Email, F("to", "a@b.de", "subject", "Grüße & mehr", "body", "Zeile 1\nZeile 2")) ==
                  "mailto:a@b.de?subject=Gr%C3%BC%C3%9Fe%20%26%20mehr&body=Zeile%201%0D%0AZeile%202", "mailto encoding");
            Check(QrContent.Build(QrType.Phone, F("number", "+49 (30) 123-45")) == "tel:+493012345", "tel: digits only");
            Check(QrContent.Build(QrType.Sms, F("number", "0170 1234", "message", "Abholung {date:dd.MM.}")) == "SMSTO:01701234:Abholung {date:dd.MM.}", "SMSTO with a field");
            Check(QrContent.Build(QrType.Wifi, F("ssid", "Net:{date:yyyy}", "password", "a")) == "WIFI:T:WPA;S:Net\\:{date:yyyy};P:a;;", "fields not escaped");
            Check(QrContent.Missing(QrType.Wifi, F("ssid", "x")) != null && QrContent.Missing(QrType.Wifi, F("ssid", "x", "security", "nopass")) == null &&
                  QrContent.Missing(QrType.Contact, F()) != null && QrContent.Missing(QrType.Phone, F("number", "-")) != null, "missing fields");

            var b = new BarcodeObject { Kind = Symbology.Qr, Data = "www.example.org" };
            b.SetQrType(QrType.Url);
            Check(b.Data == "https://www.example.org" && b.QrFields["url"] == "www.example.org", "text moves into the URL field");
            var before = (BarcodeObject)b.Clone();
            b.SetQrType(QrType.Wifi);
            b.SetQrField("ssid", "Lager");
            b.SetQrField("password", "geheim");
            Check(b.Data == "WIFI:T:WPA;S:Lager;P:geheim;;" && b.Problem(DateTime.Now) == null, "Wi-Fi built: " + b.Data);
            Check(before.QrType == QrType.Url && !before.QrFields.ContainsKey("ssid"), "a clone keeps its own fields (undo)");
            string json = new LabelDoc { Objects = { b } }.ToJson();
            Check(json.Contains("\"qrType\":\"wifi\"") && json.Contains("\"ssid\":\"Lager\"") && !json.Contains("\"url\""), "saved with type and its fields only: " + json);
            var back = (BarcodeObject)LabelDoc.FromJson(json).Objects[0];
            Check(back.QrType == QrType.Wifi && back.QrFields["password"] == "geheim" && back.Data == b.Data, "QR type round trip");
            var oldQr = (BarcodeObject)LabelDoc.FromJson(File("{\"type\":\"barcode\",\"symbology\":\"qr\",\"data\":\"hello\"}")).Objects[0];
            Check(oldQr.QrType == QrType.Text && !oldQr.TypedQr && !oldQr.ToJson().ContainsKey("qrType"), "a QR code without a type is text");
            var code = new BarcodeObject { Kind = Symbology.Qr };
            code.SetQrType(QrType.Contact);
            code.SetQrField("firstName", "Anna");
            code.SetQrField("lastName", "Grüner, Söhne");
            Check(QrRead(QrCode.Encode(code.Content(DateTime.Now), 1), 1) == code.Data, "vCard QR reads back");
        }

        static string File(string objects) { return "{\"format\":\"QL label\",\"version\":1,\"objects\":[" + objects + "]}"; }

        // Single-line texts and text frames: spacing, line height, wrapping and what doesn't fit.
        static void TextKinds()
        {
            // Both kinds saved and read back.
            var kinds = new LabelDoc();
            kinds.Objects.Add(new TextObject { Kind = TextKind.Line, Text = "One line", Size = 20 });
            kinds.Objects.Add(new TextObject { Kind = TextKind.Frame, Text = "Line 1\nLine 2", Size = 12, W = 40, H = 20 });
            string saved = kinds.ToJson();
            var again = LabelDoc.FromJson(saved);
            Check(saved.Contains("\"kind\":\"line\"") && saved.Contains("\"kind\":\"frame\"") && ((TextObject)again.Objects[1]).Kind == TextKind.Frame &&
                  again.ToJson() == saved, "kinds round trip");

            // Letter spacing widens a line by the spacing between each pair of characters.
            var t = new TextObject { Text = "ABCDE", Size = 12 };
            EditorRender.FitBox(t);
            double w0 = t.W;
            t.Spacing = 10;
            EditorRender.FitBox(t);
            double expected = 4 * 10 * 25.4 / 72 * 1.02;
            Check(Math.Abs(t.W - w0 - expected) < 0.25, "spacing 10 pt: " + (t.W - w0) + " mm wider, expected " + expected);
            t.Spacing = -2;
            EditorRender.FitBox(t);
            Check(t.W < w0, "negative spacing narrows the text");

            // Line height: only for frames, a multiple of the font's line pitch.
            var f = new TextObject { Text = "A\nB\nC", Size = 12, Kind = TextKind.Frame };
            EditorRender.FitBox(f);
            double h1 = f.H;
            f.LineHeight = 2;
            EditorRender.FitBox(f);
            double pitch = (h1 - 0.6) / 3;
            Check(Math.Abs(f.H - h1 - 2 * pitch) < 0.3, "line height 2: " + f.H + " vs " + h1);
            var line = new TextObject { Text = "A", Size = 12, LineHeight = 3 };
            EditorRender.FitBox(line);
            var plain = new TextObject { Text = "A", Size = 12 };
            EditorRender.FitBox(plain);
            Check(line.H == plain.H, "line height doesn't change a single line");

            // A frame wraps at its box; too small a box cuts the text off (marked).
            var frame = new TextObject { X = 0, Y = 0, W = 20, H = 60, Text = "one two three four five six seven", Size = 12, Kind = TextKind.Frame };
            Check(!EditorRender.Overflows(frame), "a tall narrow frame takes the wrapped text");
            frame.H = 6;
            Check(EditorRender.Overflows(frame), "a low frame doesn't");
            frame.H = 60;
            frame.Text = "Donaudampfschifffahrtsgesellschaft";
            Check(!EditorRender.Overflows(frame), "a word wider than the frame breaks at characters");
            EditorRender.FitText(frame);
            Check(frame.W == 20 && frame.H == 60, "a frame keeps its box after a change");

            // Rendering: spaced text, and a frame draws nothing outside its box.
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            var doc = new LabelDoc();
            var cut = new TextObject { X = 10, Y = 10, W = 15, H = 5, Text = "Wrapped text that is far too long for this box", Size = 14, Kind = TextKind.Frame, Spacing = 2, Underline = true };
            doc.Objects.Add(cut);
            using (var bmp = EditorRender.Render(doc, layout))
            {
                var r = EditorRender.DotRect(cut, layout);
                int inside = 0, outside = 0;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                        if (Black(bmp, x, y) == 1) { if (r.Contains(x, y)) inside++; else outside++; }
                Check(inside > 200 && outside == 0, "frame text clipped to its box: " + inside + " inside, " + outside + " outside");
            }
        }

        static LabelChoice Continuous62(double length)
        {
            return new LabelChoice { Name = "Test 62", Media = MediaTable.ById(259), LengthMm = length, FeedMm = 3, IsFormat = true };
        }

        static void RoundTrip()
        {
            var doc = new LabelDoc { FormName = "Test 62", PaperId = 259, Length01 = 500 };
            doc.Objects.Add(new TextObject { X = 5, Y = 4, W = 30, H = 8, Text = "Hallo\nÄÖÜ ß \"x\"", Size = 14, Bold = true, Align = TextAlign.Center, Rotation = 90 });
            doc.Objects.Add(new LineObject { X = 1, Y = 2, W = 40, H = 0, Thickness = 0.8, Rising = true });
            doc.Objects.Add(new ShapeObject { X = 3, Y = 3, W = 10, H = 10, Filled = true, Kind = ShapeKind.Ellipse });
            doc.Objects.Add(new ImageObject { X = 20, Y = 10, W = 5, H = 5, Png = new byte[] { 1, 2, 3 }, Mode = ImageMode.Threshold, Threshold = 90, Invert = true });
            string json = doc.ToJson();
            var back = LabelDoc.FromJson(json);
            Check(back.ToJson() == json, "round trip gives the same JSON");
            Check(back.Objects.Count == 4 && back.Objects[0] is TextObject && back.Objects[3] is ImageObject, "object types");
            var t = (TextObject)back.Objects[0];
            Check(t.Text == "Hallo\nÄÖÜ ß \"x\"" && t.Bold && !t.Italic && t.Align == TextAlign.Center && t.Rotation == 90 && t.Size == 14, "text fields");
            var img = (ImageObject)back.Objects[3];
            Check(img.Png.SequenceEqual(new byte[] { 1, 2, 3 }) && img.Mode == ImageMode.Threshold && img.Threshold == 90 && img.Invert, "image fields");
            // through a file: UTF-8 without BOM written, read with or without BOM
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ql-editor-test-" + Guid.NewGuid().ToString("N") + ".qllabel");
            try
            {
                var umlauts = new LabelDoc();
                umlauts.Objects.Add(new TextObject { X = 1, Y = 1, W = 30, H = 8, Text = "äöü ÄÖÜ ß € – 100 Stück" });
                umlauts.Save(file);
                var bytes = System.IO.File.ReadAllBytes(file);
                Check(bytes[0] == (byte)'{' && System.Text.Encoding.UTF8.GetString(bytes).Contains("Stück"), "saved as UTF-8 without BOM");
                Check(((TextObject)LabelDoc.Load(file).Objects[0]).Text == "äöü ÄÖÜ ß € – 100 Stück", "umlauts and € back from the file");
                System.IO.File.WriteAllText(file, umlauts.ToJson(), new System.Text.UTF8Encoding(true));
                Check(((TextObject)LabelDoc.Load(file).Objects[0]).Text == "äöü ÄÖÜ ß € – 100 Stück", "a file with BOM too");
            }
            finally { try { System.IO.File.Delete(file); } catch (Exception) { } }

            // A file from a newer version: unknown fields and object types.
            string newer = "{\"format\":\"QL label\",\"version\":2,\"colour\":\"red\",\"label\":{\"form\":\"X\",\"paperId\":271,\"length01\":898}," +
                           "\"objects\":[{\"type\":\"qr\",\"data\":\"x\"},{\"type\":\"box\",\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"rot\":450,\"glow\":7}]}";
            var n = LabelDoc.FromJson(newer);
            Check(n.Skipped == 1 && n.Objects.Count == 1 && n.Objects[0].Rotation == 90, "unknown object skipped, rotation normalised");
            string again = n.ToJson();
            Check(again.Contains("\"colour\":\"red\"") && again.Contains("\"glow\":7"), "unknown fields kept: " + again);
            Check(!n.Newer && !LabelDoc.FromJson("{\"format\":\"QL label\",\"objects\":[]}").Newer, "version 2 and none: not newer");

            // 0.21 safeguard: a version 3 file; its unknown objects are kept (not shown) and written back at their place
            string v3 = "{\"format\":\"QL label\",\"version\":3,\"label\":{\"form\":\"X\",\"paperId\":271,\"length01\":898},\"objects\":[" +
                        "{\"type\":\"line\",\"x\":1,\"y\":2,\"w\":10,\"h\":0,\"rot\":0,\"thickness\":0.5,\"rising\":false}," +
                        "{\"type\":\"curve\",\"points\":[[1,2],[3,4]],\"style\":{\"dash\":true}}," +
                        "{\"type\":\"box\",\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"rot\":0}," +
                        "{\"type\":\"hologram\",\"x\":5}]}";
            var three = LabelDoc.FromJson(v3);
            Check(three.Newer && three.FileVersion == 3 && three.Skipped == 2 && three.Objects.Count == 2, "version 3: newer, two unknown objects not shown");
            string saved = three.ToJson();
            var types = System.Text.RegularExpressions.Regex.Matches(saved, "\"type\":\"(\\w+)\"").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).ToList();
            Check(string.Join(",", types) == "line,curve,box,hologram", "version 3: unknown objects written back at their places: " + string.Join(",", types));
            Check(saved.Contains("\"points\":[[1,2],[3,4]]") && saved.Contains("\"style\":{\"dash\":true}"), "version 3: unknown objects unchanged: " + saved);
            var edited = three.Clone();
            edited.Objects.RemoveAt(0);   // the line deleted: the curve comes first now
            var after = System.Text.RegularExpressions.Regex.Matches(edited.ToJson(), "\"type\":\"(\\w+)\"").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value);
            Check(after.Count() == 3 && after.Contains("curve") && after.Contains("hologram"), "version 3: after an edit the unknown objects are still there: " + string.Join(",", after));
            Check(LabelDoc.FromJson(saved).ToJson() == saved, "version 3: a second round trip gives the same JSON");
            bool threw = false;
            try { LabelDoc.FromJson("{\"objects\":[]}"); } catch (Exception) { threw = true; }
            Check(threw, "a file without the format marker is refused");
        }

        // ---------- shapes (editor-next step 3): file keys, version 2, regular proportions, drawing ----------
        static void Shapes()
        {
            var doc = new LabelDoc();
            doc.Objects.Add(new ShapeObject { X = 1, Y = 1, W = 20, H = 10, Radius = 2 });
            doc.Objects.Add(new ShapeObject { X = 1, Y = 1, W = 20, H = 10, Kind = ShapeKind.Ellipse, Radius = 2 });
            doc.Objects.Add(new ShapeObject { X = 1, Y = 1, W = 20, H = 10, Kind = ShapeKind.Triangle, Filled = true });
            doc.Objects.Add(new ShapeObject { X = 1, Y = 1, W = 20, H = 10, Kind = ShapeKind.Polygon, Sides = 8 });
            doc.Objects.Add(new ShapeObject { X = 1, Y = 1, W = 20, H = 10, Kind = ShapeKind.Star, Points = 7, Inner = 30 });
            string json = doc.ToJson();
            Check(json.Contains("\"version\":2"), "shapes: the file is version 2");
            Check(json.Contains("\"type\":\"box\",\"x\":1,\"y\":1,\"w\":20,\"h\":10,\"rot\":0,\"thickness\":0.5,\"filled\":false,\"ellipse\":false,\"radius\":2"),
                  "shapes: a rectangle stays a box, + radius: " + json);
            Check(json.Contains("\"ellipse\":true}"), "shapes: an ellipse is a box without radius");
            Check(json.Contains("\"type\":\"shape\"") && json.Contains("\"shape\":\"triangle\"") && json.Contains("\"corners\":8") &&
                  json.Contains("\"points\":7,\"inner\":30"), "shapes: triangle, polygon, star as type shape");
            var back = LabelDoc.FromJson(json);
            Check(back.ToJson() == json, "shapes: round trip gives the same JSON");
            var k = back.Objects.Cast<ShapeObject>().ToList();
            Check(k[0].Kind == ShapeKind.Rectangle && k[0].Radius == 2 && k[1].Kind == ShapeKind.Ellipse && k[2].Kind == ShapeKind.Triangle && k[2].Filled &&
                  k[3].Kind == ShapeKind.Polygon && k[3].Sides == 8 && k[4].Kind == ShapeKind.Star && k[4].Points == 7 && k[4].Inner == 30, "shapes: fields back");
            // A version 1 box; a shape name of a newer version (its corners); values out of range
            var old = LabelDoc.FromJson("{\"format\":\"QL label\",\"version\":1,\"objects\":[{\"type\":\"box\",\"x\":1,\"y\":2,\"w\":3,\"h\":4,\"ellipse\":true}," +
                                        "{\"type\":\"shape\",\"shape\":\"heart\",\"corners\":99,\"x\":1,\"y\":2,\"w\":3,\"h\":4}," +
                                        "{\"type\":\"shape\",\"shape\":\"star\",\"points\":1,\"inner\":500,\"radius\":-3,\"x\":1,\"y\":2,\"w\":3,\"h\":4}]}");
            var o = old.Objects.Cast<ShapeObject>().ToList();
            Check(o.Count == 3 && o[0].Kind == ShapeKind.Ellipse && o[0].Radius == 0, "shapes: a version 1 ellipse");
            Check(o[1].Kind == ShapeKind.Polygon && o[1].Sides == 20, "shapes: an unknown shape is a polygon, corners at most 20");
            Check(o[2].Kind == ShapeKind.Star && o[2].Points == 3 && o[2].Inner == 90 && o[2].Radius == 0, "shapes: points, inner and radius limited");

            // Shift while dragging: these proportions are regular
            Func<ShapeKind, int, double> ratio = (kind, n) => new ShapeObject { Kind = kind, Sides = n, Points = n }.NaturalRatio;
            Check(ratio(ShapeKind.Rectangle, 0) == 1 && ratio(ShapeKind.Ellipse, 0) == 1, "shapes: square and circle");
            Check(Math.Abs(ratio(ShapeKind.Triangle, 0) - 2 / Math.Sqrt(3)) < 1e-6, "shapes: equilateral triangle " + ratio(ShapeKind.Triangle, 0));
            Check(Math.Abs(ratio(ShapeKind.Polygon, 4) - 1) < 1e-6 && Math.Abs(ratio(ShapeKind.Polygon, 6) - Math.Sqrt(3) / 2) < 1e-6, "shapes: square on its corner, hexagon point up");

            // Drawn: the outline stays inside the box, also at the triangle's point; a filled star is black in its middle only
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            Func<Bitmap, double, double, int> at = (bmp, x, y) =>
                Black(bmp, (int)Math.Round((x - layout.Printable.X) * layout.DpiX / 25.4), (int)Math.Round((y - layout.Printable.Y) * layout.DpiY / 25.4));
            var draw = new LabelDoc();
            double h = Math.Round(20 / (2 / Math.Sqrt(3)), 2);   // 17.32
            draw.Objects.Add(new ShapeObject { X = 10, Y = 10, W = 20, H = h, Kind = ShapeKind.Triangle, Thickness = 1 });
            draw.Objects.Add(new ShapeObject { X = 35, Y = 10, W = 20, H = 19, Kind = ShapeKind.Star, Filled = true });
            draw.Objects.Add(new ShapeObject { X = 10, Y = 32, W = 20, H = 10, Thickness = 1, Radius = 4 });
            using (var bmp = EditorRender.Render(draw, layout))
            {
                Check(at(bmp, 20, 10 + h - 0.4) == 1 && at(bmp, 20, 10 + h + 0.4) == 0 && at(bmp, 20, 20) == 0, "shapes: triangle outline inside, middle empty");
                Check(at(bmp, 20, 10.7) == 1 && at(bmp, 20, 9.6) == 0, "shapes: the triangle's point stays inside the box");
                Check(at(bmp, 45, 20) == 1 && at(bmp, 36, 11) == 0 && at(bmp, 45, 10.4) == 1, "shapes: filled star");
                Check(at(bmp, 20, 32.4) == 1 && at(bmp, 10.4, 32.4) == 0 && at(bmp, 10.4, 37) == 1, "shapes: round corners, square edges between");
            }
        }

        // ---------- custom sizes: Unidrv reports the side margin on the left only, the label prints centred ----------
        static void CustomSizeMargins()
        {
            // The driver's numbers for "Label 035" (62 mm tape, custom 35 mm): page 732 x 414, printable 696 x 344 at 36,35.
            var g = new LabelGeometry { DpiX = 300, DpiY = 300, PageWidth = 732, PageHeight = 414, Width = 696, Height = 344, OffsetX = 36, OffsetY = 35 };
            var label = Continuous62(35);
            double side = 18 * 25.4 / 300;
            var across = LabelLayout.FromGeometry(label, g, null, null, true);
            Check(Math.Abs(across.Printable.Left - side) < 0.01 && Math.Abs(across.ViewWidthMm - across.Printable.Right - side) < 0.05,
                  "custom size across: centred " + across.Printable);
            var along = LabelLayout.FromGeometry(label, g, null, null, false);
            Check(Math.Abs(along.Printable.Top - side) < 0.01 && Math.Abs(along.ViewHeightMm - along.Printable.Bottom - side) < 0.05,
                  "custom size along: centred " + along.Printable);
        }

        // ---------- printed dots in the unprintable (hatched) margin: the hint is about ink, not boxes ----------
        static void Margin()
        {
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);   // 50 x 62 mm, shown along
            var p = layout.Printable;
            Check(p.Left > 2.6 && p.Top > 1.3, "margin: the label has margins for the cases below");
            var now = DateTime.Now;
            // the strip's two lines: "reaches into the hatched margin" (only there) and "reaches beyond the label"
            Func<LabelObject, bool> hint = o => EditorRender.CutOff(o, layout, now) == EditorRender.Cut.Margin;
            Func<LabelObject, bool> beyondHint = o => (EditorRender.CutOff(o, layout, now) & EditorRender.Cut.Beyond) != 0;

            // a frame text whose box reaches 1.2 mm into the top margin, its first line empty: no ink there
            var empty = new TextObject { Kind = TextKind.Frame, Text = "\nText", Size = 12, X = p.Left + 5, Y = p.Top - 1.2, W = 30, H = 20 };
            Check(!hint(empty), "margin: a box over the margin without ink there gives no hint");
            // a text 2.5 mm into the left margin: its glyphs are cut
            var cut = new TextObject { Text = "Text", Size = 12, X = p.Left - 2.5, Y = p.Top + 10, W = 20, H = 6 };
            Check(hint(cut), "margin: cut glyphs give the hint");
            var inside = new TextObject { Text = "Text", Size = 12, X = p.Left + 0.5, Y = p.Top + 10, W = 20, H = 6 };
            Check(!hint(inside), "margin: a text inside the printable area gives no hint");
            // an inverted text: its box inside, its black margin (InvertPad) reaching 0.5 mm into the margin
            var inverted = new TextObject { Text = "Text", Size = 12, Invert = true, X = p.Left + EditorRender.InvertPad - 0.5, Y = p.Top + 10, W = 20, H = 6 };
            Check(hint(inverted), "margin: an inverted text's black box in the margin gives the hint");
            var invertedInside = new TextObject { Text = "Text", Size = 12, Invert = true, X = p.Left + EditorRender.InvertPad + 0.3, Y = p.Top + 10, W = 20, H = 6 };
            Check(!hint(invertedInside), "margin: an inverted text with its black box inside gives no hint");
            // a filled 10 mm box turned 45°: its corner reaches (10√2 - 10) / 2 = 2.07 mm above its box
            var turned = new ShapeObject { X = p.Left + 10, Y = p.Top + 1, W = 10, H = 10, Filled = true, Tilt = 45 };
            var straight = new ShapeObject { X = p.Left + 10, Y = p.Top + 1, W = 10, H = 10, Filled = true };
            var turnedInside = new ShapeObject { X = p.Left + 10, Y = p.Top + 2.5, W = 10, H = 10, Filled = true, Tilt = 45 };
            Check(hint(turned) && !hint(straight) && !hint(turnedInside), "margin: a turned box's corner in the margin gives the hint, inside none");
            // beyond the label counts there, not here
            var beyond = new ShapeObject { X = 45, Y = 20, W = 20, H = 10, Filled = true };
            Check(!hint(beyond) && beyondHint(beyond), "margin: an object beyond the label is counted there, not twice");
            // an inverted text whose black box (not its glyphs) crosses the label's left edge: beyond
            var invertedEdge = new TextObject { Text = "Text", Size = 12, Invert = true, X = EditorRender.InvertPad - 0.5, Y = p.Top + 10, W = 20, H = 6 };
            Check(beyondHint(invertedEdge) && !hint(invertedEdge), "beyond: an inverted text's black box across the label's edge counts as beyond");
            Check(!beyondHint(inverted), "beyond: the same black box only in the margin is not beyond");
            // a box reaching 1 mm above the label with nothing printed beyond or in the margin: no hint (the canvas still grows)
            var overhang = new TextObject { Kind = TextKind.Frame, Text = "\n\nText", Size = 12, X = p.Left + 5, Y = -1, W = 30, H = 20 };
            Check(EditorRender.Beyond(overhang, layout) && EditorRender.CutOff(overhang, layout, now) == EditorRender.Cut.None,
                  "beyond: an empty overhang gives no hint, the canvas still grows by the box");
        }

        // ---------- objects beyond the label (grey margin in the editor) ----------
        static void Outside()
        {
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);   // 50 x 62 mm, shown along
            var inside = new ShapeObject { X = 5, Y = 5, W = 20, H = 10, Filled = true };
            var edge = new ShapeObject { X = 40, Y = 5, W = 10, H = 10, Filled = true };              // right edge on the label's
            var partly = new ShapeObject { X = 45, Y = 20, W = 20, H = 10, Filled = true };           // 15 mm beyond
            var away = new ShapeObject { X = 60, Y = 70, W = 10, H = 10, Filled = true };             // all beyond
            Check(!EditorRender.Beyond(inside, layout) && !EditorRender.Beyond(edge, layout) && EditorRender.Beyond(partly, layout) &&
                  EditorRender.Beyond(away, layout), "beyond the label");
            var doc = new LabelDoc();
            doc.Objects.AddRange(new LabelObject[] { inside, edge });
            // the grey margin at 4 pixels per mm, 0,0 mm at the screen's 0,0: dark dots grey, the rest transparent
            var screen = new Rectangle(0, 0, 400, 400);
            Check(EditorRender.RenderOutsideOnScreen(doc, layout, screen, PointF.Empty, 4) == null, "nothing beyond: no bitmap");
            doc.Objects.AddRange(new LabelObject[] { partly, away });
            using (var b = EditorRender.RenderOutsideOnScreen(doc, layout, screen, PointF.Empty, 4))
            {
                Func<double, double, int> alpha = (x, y) => b.GetPixel((int)(x * 4), (int)(y * 4)).A;
                // the far box's centre (65, 75 mm) and the partly beyond one (50, 25 mm) grey; between them (55, 50 mm) and the
                // box on the label (10, 10 mm, not beyond) transparent
                Check(b != null && alpha(65, 75) == 255 && alpha(50, 25) == 255 && alpha(55, 50) == 0 && alpha(10, 10) == 0, "outside drawn");
            }
            using (var label = EditorRender.Render(doc, layout))
                Check(label.Width == layout.DotsX, "label bitmap unchanged");
        }

        // ---------- stacking order (layer commands) ----------
        static void Layers()
        {
            var o = Enumerable.Range(0, 5).Select(i => (LabelObject)new ShapeObject { X = i }).ToList();
            Func<LabelDoc, string> order = d => string.Concat(d.Objects.Select(x => (int)x.X));
            Func<LabelDoc> fresh = () => { var d = new LabelDoc(); d.Objects.AddRange(o); return d; };
            var doc = fresh();
            var one = new[] { o[1] };
            Check(doc.Restack(one, Layer.Forward) && order(doc) == "02134", "one forward: " + order(doc));
            doc = fresh();
            Check(doc.Restack(one, Layer.Backward) && order(doc) == "10234", "one backward: " + order(doc));
            doc = fresh();
            Check(doc.Restack(one, Layer.ToFront) && order(doc) == "02341", "to front: " + order(doc));
            doc = fresh();
            Check(doc.Restack(one, Layer.ToBack) && order(doc) == "10234", "to back: " + order(doc));
            doc = fresh();
            Check(!doc.CanRestack(new[] { o[4] }, Layer.Forward) && !doc.CanRestack(new[] { o[4] }, Layer.ToFront) &&
                  !doc.Restack(new[] { o[0] }, Layer.ToBack) && order(doc) == "01234", "nothing to pass: no change");
            // several: they move together and keep their order
            var two = new[] { o[3], o[1] };
            Check(doc.Restack(two, Layer.Forward) && order(doc) == "02143", "two forward: " + order(doc));
            doc = fresh();
            Check(doc.Restack(two, Layer.ToFront) && order(doc) == "02413", "two to front: " + order(doc));
            doc = fresh();
            Check(doc.Restack(two, Layer.Backward) && order(doc) == "10324", "two backward: " + order(doc));
            doc = fresh();
            Check(doc.Restack(two, Layer.ToBack) && order(doc) == "13024", "two to back: " + order(doc));
            var top = new[] { o[3], o[4] };
            doc = fresh();
            Check(!doc.CanRestack(top, Layer.Forward) && doc.CanRestack(top, Layer.Backward), "a block at the top: only backward");
        }

        static void Rotation()
        {
            var b = new ShapeObject { X = 10, Y = 5, W = 20, H = 6 };
            b.Rotate90();
            Check(b.Rotation == 90 && Math.Abs(b.W - 6) < 1e-9 && Math.Abs(b.H - 20) < 1e-9 && Math.Abs(b.X - 17) < 1e-9 && Math.Abs(b.Y + 2) < 1e-9, "rotate 90 about the centre");
            b.Rotate90(); b.Rotate90(); b.Rotate90();
            Check(b.Rotation == 0 && Math.Abs(b.X - 10) < 1e-9 && Math.Abs(b.Y - 5) < 1e-9 && Math.Abs(b.W - 20) < 1e-9, "four turns = start");
            var t = new TextObject { Text = "Test", Size = 10 };
            EditorRender.FitText(t);
            Check(t.W > 5 && t.W < 20 && t.H > 3 && t.H < 8, "text box fits 10 pt 'Test': " + t.W + " x " + t.H);
            double w = t.W, h = t.H;
            t.Rotate90();
            EditorRender.FitText(t);
            Check(Math.Abs(t.W - h) < 0.11 && Math.Abs(t.H - w) < 0.11, "turned text box");
        }

        // ---------- quiet zones (outside the box since 2026-11-01) ----------
        static void QuietZones()
        {
            // barcode_print's edge cases: a code at 0,0 reaches into the unprintable margin and is cut off - the panel says
            // so; at the printable area's corner (the margin cases) it doesn't
            {
                var l = LabelLayout.Estimate(Continuous62(60), false, false);
                string outside = Lang.T("The code lies partly outside the printable area – it gets cut off.");
                Func<string, double, double, double, double, bool> warns = (kind, x, y, w, h) =>
                {
                    var b = new BarcodeObject { Kind = kind == "qr" ? Symbology.Qr : Symbology.Code128, Data = kind == "qr" ? "EDGE QR" : "EDGE-1", X = x, Y = y, W = w, H = h };
                    string hint = EditorRender.BarcodeHint(b, l, DateTime.Now);
                    return hint != null && hint.Contains(outside);
                };
                Check(warns("code128", 0, 0, 30, 12) && warns("qr", 0, 0, 16, 16), "a code at 0,0 gets the cut-off hint");
                Check(!warns("code128", 3, 1.6, 30, 12) && !warns("qr", 3, 1.6, 16, 16), "a code at the printable corner gets no cut-off hint");
            }
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            var qr = new BarcodeObject { Kind = Symbology.Qr, Data = "https://example.org", X = 20, Y = 20, W = 15, H = 15 };
            var zone = EditorRender.QuietZone(qr, layout, DateTime.Now);
            int n = QrCode.Encode(qr.Content(DateTime.Now), qr.Level).GetLength(0);
            int mx = (int)(15 * 300 / 25.4 / n);
            Check(Math.Abs(zone.Width - (15 + 8 * mx * 25.4 / 300)) < 0.2 && Math.Abs(zone.X + zone.Width / 2 - 27.5) < 0.1,
                  "QR quiet zone: 4 modules around the box: " + zone + " (" + mx + " dots per module)");
            var code = new BarcodeObject { Kind = Symbology.Ean13, Data = "200000000000", X = 5, Y = 40, W = 30, H = 12 };
            var z2 = EditorRender.QuietZone(code, layout, DateTime.Now);
            Check(z2.Top == code.Box.Top || Math.Abs(z2.Top - code.Box.Top) < 0.1, "barcode: no quiet zone above");
            Check(z2.Right - code.Box.Right > 0 && code.Box.Left - z2.Left > z2.Right - code.Box.Right, "EAN-13: wider quiet zone on the left (11 / 7)");
            code.Rotation = 90;
            var z3 = EditorRender.QuietZone(code, layout, DateTime.Now);
            Check(code.Box.Top - z3.Top > z3.Bottom - code.Box.Bottom && Math.Abs(z3.Left - code.Box.Left) < 0.1, "turned 90°: the left zone goes to the top");
            code.Rotation = 0;

            // the code prints in its box: nothing drawn in the zone
            var doc = new LabelDoc();
            doc.Objects.Add(qr);
            using (var bmp = EditorRender.Render(doc, layout))
            {
                var box = EditorRender.DotRect(qr, layout);
                var q = EditorRender.QuietDots(qr, layout, DateTime.Now);
                int inBox = 0, outside = 0;
                for (int y = q.Top; y < q.Bottom; y++)
                    for (int x = q.Left; x < q.Right; x++)
                        if (Black(bmp, x, y) == 1) { if (box.Contains(x, y)) inBox++; else outside++; }
                Check(inBox > 0 && outside == 0, "QR drawn inside its box, its zone white: " + inBox + " / " + outside);
            }
            Check(EditorRender.QuietZoneIntruders(doc, layout, DateTime.Now).Count == 0, "nothing in the zone");
            var text = new TextObject { Text = "Regal 3", Size = 14, X = 10, Y = 22 };
            EditorRender.FitText(text);
            text.X = qr.X - text.W + 0.5;   // its end reaches into the zone
            doc.Objects.Add(text);
            var found = EditorRender.QuietZoneIntruders(doc, layout, DateTime.Now);
            Check(found.Count == 1 && found[0].Key == qr && found[0].Value == text, "text in the zone found");
            text.X = zone.Left - text.W - 1;
            Check(EditorRender.QuietZoneIntruders(doc, layout, DateTime.Now).Count == 0, "text moved away");


            // the text under a barcode ends above the code's bottom edge (its last dots were cut, 2026-11-01)
            foreach (var kind in new[] { Symbology.Ean13, Symbology.Code128 })
                foreach (int dpiH in new[] { 300, 600 })
                {
                    var bc = new BarcodeObject { Kind = kind, Data = kind == Symbology.Ean13 ? "200000000000" : "R3-M4-20" };
                    int bw = (int)Math.Round(32 * 300 / 25.4), bh = (int)Math.Round(8 * dpiH / 25.4);
                    using (var bmp = EditorRender.BarcodeBitmap(bc, bw, bh, 300, dpiH, DateTime.Now))
                    {
                        int last = 0;
                        for (int x = 0; x < bmp.Width; x++) { var c = bmp.GetPixel(x, bmp.Height - 1); if (c.A > 128 && c.R < 128) last++; }   // the quiet zone is transparent
                        Check(last == 0, kind + " text at 300 x " + dpiH + ": nothing in the last row (" + last + " dots)");
                    }
                }
        }

        // ---------- free rotation ----------
        static void Tilt()
        {
            var b = new ShapeObject { X = 10, Y = 5, W = 20, H = 6, Filled = true };
            b.SetAngle(30);
            Check(b.Rotation == 0 && b.Tilt == 30 && b.Angle == 30 && b.W == 20, "30°: no quarter turn, tilted");
            b.SetAngle(120);
            Check(b.Rotation == 90 && b.Tilt == 30 && Math.Abs(b.W - 6) < 1e-9 && Math.Abs(b.X + b.W / 2 - 20) < 1e-9 && Math.Abs(b.Y + b.H / 2 - 8) < 1e-9,
                  "120° = a quarter turn + 30°, the centre stays");
            b.SetAngle(-135);
            Check(Math.Abs(b.Angle + 135) < 1e-9 && Math.Abs(Math.Abs(b.Tilt) - 45) < 1e-9, "-135°: " + b.Rotation + " + " + b.Tilt);
            b.SetAngle(0);
            Check(b.Rotation == 0 && b.Tilt == 0 && Math.Abs(b.X - 10) < 1e-9 && Math.Abs(b.Y - 5) < 1e-9, "back to 0°");
            b.SetAngle(45);
            var bounds = b.Bounds;
            Check(Math.Abs(bounds.Width - 26 * Math.Sqrt(0.5)) < 0.01 && Math.Abs(bounds.Height - bounds.Width) < 0.01 &&
                  Math.Abs(bounds.X + bounds.Width / 2 - 20) < 0.01, "bounds of a box at 45°: " + bounds);
            var code = new BarcodeObject { X = 0, Y = 0, W = 30, H = 10, Data = "123" };
            code.SetAngle(30);
            Check(code.Rotation == 0 && code.Tilt == 0, "barcodes don't tilt");
            code.SetAngle(80);
            Check(code.Rotation == 90 && code.Tilt == 0, "barcodes: the nearest quarter turn");

            var doc = new LabelDoc();
            doc.Objects.Add(b);
            string json = doc.ToJson();
            var back = (ShapeObject)LabelDoc.FromJson(json).Objects[0];
            Check(json.Contains("\"tilt\":45") && back.Tilt == 45 && back.Rotation == 0 && back.Angle == 45, "tilt saved and read back");
            b.SetAngle(0);
            Check(!doc.ToJson().Contains("tilt"), "no tilt, no tilt in the file");

            // Rendered turned: a filled 20 x 6 mm box at 45° about (20, 8): along the diagonal black, across white.
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            b.X = 10; b.Y = 17; b.SetAngle(45);   // centre (20, 20)
            Func<Bitmap, double, double, int> at = (bmp, x, y) =>
                Black(bmp, (int)Math.Round((x - layout.Printable.X) * layout.DpiX / 25.4), (int)Math.Round((y - layout.Printable.Y) * layout.DpiY / 25.4));
            using (var bmp = EditorRender.Render(doc, layout))
                Check(at(bmp, 20, 20) == 1 && at(bmp, 26, 26) == 1 && at(bmp, 26, 14) == 0 && at(bmp, 11, 18) == 0, "box turned 45°");

            // A picture (left half black) turned 30°: turned first, then black and white; the rest of its bounds stays white.
            using (var pic = new Bitmap(40, 20))
            {
                using (var g = Graphics.FromImage(pic)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 0, 0, 20, 20); }
                byte[] png;
                using (var ms = new System.IO.MemoryStream()) { pic.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png = ms.ToArray(); }
                var img = new ImageObject { X = 10, Y = 15, W = 20, H = 10, Png = png, Mode = ImageMode.Threshold };
                img.SetAngle(30);
                var d2 = new LabelDoc();
                d2.Objects.Add(img);
                var c = new PointF(20, 20);
                var left = LabelObject.Turn(new PointF(14, 20), c, 30);
                var right = LabelObject.Turn(new PointF(26, 20), c, 30);
                var r = img.Bounds;
                using (var bmp = EditorRender.Render(d2, layout))
                    Check(at(bmp, left.X, left.Y) == 1 && at(bmp, right.X, right.Y) == 0 && at(bmp, r.Left + 0.3, r.Top + 0.3) == 0 &&
                          at(bmp, r.Left + 0.3, r.Bottom - 0.3) == 0, "picture turned 30°");
                img.Mode = ImageMode.Dither; img.Invert = true;
                using (var bmp = EditorRender.Render(d2, layout))
                    Check(at(bmp, right.X, right.Y) == 1 && at(bmp, r.Left + 0.3, r.Top + 0.3) == 0, "turned picture inverted: only inside it black");
            }
        }

        // SVG text becomes paths of the installed fonts (Direct2D draws no <text>): placed as GDI+ draws the same text,
        // turned, centred, a missing font replaced and named; the editor's preview and the print agree.
        static void SvgText(System.Text.Encoding enc)
        {
            Func<string, byte[]> clean = x => SvgPicture.Clean(enc.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 50\">" + x + "</svg>"));
            Func<byte[], bool[,]> ink = svg =>
            {
                var r = new bool[200, 50];
                using (var bmp = SvgPicture.Render(svg, 200, 50))
                    for (int y = 0; y < 50; y++) for (int x = 0; x < 200; x++) r[x, y] = bmp.GetPixel(x, y).A > 128;
                return r;
            };
            Func<bool[,], Rectangle> box = m =>
            {
                int l = 200, t = 50, rr = -1, b = -1;
                for (int y = 0; y < 50; y++) for (int x = 0; x < 200; x++) if (m[x, y]) { l = Math.Min(l, x); t = Math.Min(t, y); rr = Math.Max(rr, x); b = Math.Max(b, y); }
                return rr < 0 ? Rectangle.Empty : Rectangle.FromLTRB(l, t, rr + 1, b + 1);
            };

            // Arial 40 px at x 10, baseline 40: the same pixels as GDI+'s own drawing of the text
            var arial = clean("<text x=\"10\" y=\"40\" font-family=\"Arial\" font-size=\"40\" fill=\"#000\">HxgH</text>");
            string a = enc.GetString(arial);
            Check(!a.Contains("<text") && a.Contains("<path") && SvgPicture.Missing(arial).Length == 0, "SVG text: Arial becomes paths: " + a.Substring(0, Math.Min(200, a.Length)));
            var mine = ink(arial);
            int both = 0, any = 0;
            Rectangle gdi;
            using (var family = new FontFamily("Arial"))
            using (var font = new Font(family, 40, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bmp = new Bitmap(200, 50))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                float ascent = 40f * family.GetCellAscent(FontStyle.Regular) / family.GetEmHeight(FontStyle.Regular);
                g.DrawString("HxgH", font, Brushes.Black, new PointF(10, 40 - ascent), StringFormat.GenericTypographic);
                var theirs = new bool[200, 50];
                for (int y = 0; y < 50; y++)
                    for (int x = 0; x < 200; x++)
                    {
                        theirs[x, y] = bmp.GetPixel(x, y).A > 128;
                        if (theirs[x, y] && mine[x, y]) both++;
                        if (theirs[x, y] || mine[x, y]) any++;
                    }
                gdi = box(theirs);
            }
            // the same box to a pixel; the edges differ by anti-aliasing only
            var own = box(mine);
            Check(Math.Abs(own.Left - gdi.Left) <= 1 && Math.Abs(own.Top - gdi.Top) <= 1 && Math.Abs(own.Right - gdi.Right) <= 1 && Math.Abs(own.Bottom - gdi.Bottom) <= 1
                  && any > 300 && both >= any * 0.85, "SVG text: placed as GDI+ draws it: " + own + " / " + gdi + ", " + both + " of " + any + " pixels");

            // turned with the text's transform; centred with text-anchor; bold, a tspan with its own fill and dx
            var turned = box(ink(clean("<text x=\"100\" y=\"25\" font-family=\"Arial\" font-size=\"10\" text-anchor=\"middle\" transform=\"rotate(90 100 25)\">HHHHH</text>")));
            Check(turned.Height > turned.Width * 2, "SVG text: turned with its transform: " + turned);
            var middle = box(ink(clean("<text x=\"100\" y=\"40\" font-family=\"Arial\" font-size=\"30\" text-anchor=\"middle\">HHH</text>")));
            Check(Math.Abs(middle.Left - (200 - middle.Right)) <= 2, "SVG text: text-anchor middle: " + middle);
            var spans = enc.GetString(clean("<text x=\"10\" y=\"40\" font-family=\"Arial\" font-size=\"30\" font-weight=\"bold\">AB<tspan fill=\"red\" dx=\"5\">CD</tspan></text>"));
            Check(Regex.Matches(spans, "<path").Count == 2 && spans.Contains("fill=\"red\""), "SVG text: a tspan keeps its fill: " + spans.Substring(0, Math.Min(300, spans.Length)));

            // a font this PC lacks: Arial in its place, the name kept for the hint
            var lost = clean("<text x=\"10\" y=\"40\" font-family=\"'No Such Font 4711', sans-serif\" font-size=\"30\">HH</text>");
            string lostText = enc.GetString(lost);
            Check(lostText.Contains("missing-font=\"No Such Font 4711\"") && box(ink(lost)).Width > 20, "SVG text: missing font replaced and named: " + lostText.Substring(0, Math.Min(300, lostText.Length)));
            Check(string.Join(",", SvgPicture.Missing(lost)) == "font:No Such Font 4711", "SVG hint: the missing font named: " + string.Join(",", SvgPicture.Missing(lost)));
            var lostAndBlur = clean("<defs><filter id=\"f\"/></defs><rect filter=\"url(#f)\"/><text font-family=\"No Such Font 4711\">A</text><text font-family=\"No Such Font 4711\">B</text>");
            Check(string.Join(",", SvgPicture.Missing(lostAndBlur)) == "filter,font:No Such Font 4711", "SVG hint: a font named once, after the parts: " + string.Join(",", SvgPicture.Missing(lostAndBlur)));

            // the editor's preview (inserted from a file) and the print show the same text
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ql-svg-text-test.svg");
            System.IO.File.WriteAllText(file, "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 200 50\"><text x=\"10\" y=\"40\" font-family=\"Arial\" " +
                                              "font-size=\"40\" font-weight=\"bold\">MUSTER</text></svg>");
            ImageObject img;
            try { img = ImageObject.FromFile(file); } finally { System.IO.File.Delete(file); }
            Check(img.Mode == ImageMode.Dither, "SVG inserted with Dither, as every picture (grey and colours stay visible)");
            img.X = 5; img.Y = 5; img.W = 40; img.H = 10; img.Mode = ImageMode.Threshold;
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            var doc = new LabelDoc();
            doc.Objects.Add(img);
            using (var print = EditorRender.Render(doc, layout))
            using (var preview = new Bitmap(img.Picture))
            {
                var r = EditorRender.DotRect(img, layout);
                int same = 0, inked = 0;
                for (int y = 0; y < r.Height; y++)
                    for (int x = 0; x < r.Width; x++)
                    {
                        bool p = Black(print, r.X + x, r.Y + y) == 1;
                        var c = preview.GetPixel(x * preview.Width / r.Width, y * preview.Height / r.Height);
                        bool v = c.A > 128 && c.GetBrightness() < 0.5f;
                        if (p || v) { inked++; if (p == v) same++; }
                    }
                Check(inked > 500 && same >= inked * 0.9, "SVG text: preview and print agree: " + same + " of " + inked);
            }
        }

        static int Black(Bitmap b, int x, int y) { return (b.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0 ? 1 : 0; }

        static void Render()
        {
            var layout = LabelLayout.Estimate(Continuous62(50), false, false);
            Check(layout.DpiX == 300 && Math.Abs(layout.DotsX - 44 * 300 / 25.4) <= 2 && Math.Abs(layout.DotsY - 59 * 300 / 25.4) <= 2,
                  "estimated 62 x 50 layout: " + layout.DotsX + " x " + layout.DotsY);
            var doc = new LabelDoc();
            // A filled box 10 x 5 mm at 10 mm / 10 mm on the label.
            doc.Objects.Add(new ShapeObject { X = 10, Y = 10, W = 10, H = 5, Filled = true });
            using (var bmp = EditorRender.Render(doc, layout))
            {
                var r = EditorRender.DotRect(doc.Objects[0], layout);
                Check(r.X == (int)Math.Round((10 - layout.Printable.X) * 300 / 25.4) && r.Width == 118, "box dots " + r);
                Check(Black(bmp, r.X + 1, r.Y + 1) == 1 && Black(bmp, r.Right - 2, r.Bottom - 2) == 1, "box filled");
                Check(Black(bmp, r.X - 2, r.Y + 5) == 0 && Black(bmp, r.Right + 1, r.Y + 5) == 0 && Black(bmp, r.X + 5, r.Bottom + 1) == 0, "nothing outside the box");
                int black = 0, other = 0;
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int v = bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF;
                        if (v == 0) black++; else if (v != 0xFFFFFF) other++;
                    }
                Check(other == 0, "only black and white dots");
                Check(Math.Abs(black - r.Width * r.Height) <= r.Width + r.Height, "black dots = the box: " + black + " vs " + r.Width * r.Height);
                using (var p = EditorRender.ForPrinter(bmp, layout))
                {
                    Check(p.Width == bmp.Height && p.Height == bmp.Width, "portrait size");
                    // Clockwise: the editor's (x, y) lands at (H - 1 - y, x).
                    Check(Black(p, bmp.Height - 1 - (r.Y + 1), r.X + 1) == 1, "portrait turned clockwise");
                }
            }
            // Text and lines render without grey, also turned.
            doc.Objects.Clear();
            var t = new TextObject { X = 6, Y = 6, Text = "Label 123", Size = 16, Bold = true };
            EditorRender.FitText(t);
            t.Rotate90();
            doc.Objects.Add(t);
            doc.Objects.Add(new LineObject { X = 5, Y = 30, W = 30, H = 0, Thickness = 0.5 });
            using (var bmp = EditorRender.Render(doc, layout))
            {
                int black = 0;
                for (int y = 0; y < bmp.Height; y++) for (int x = 0; x < bmp.Width; x++) black += Black(bmp, x, y);
                Check(black > 500, "text and line drawn: " + black + " black dots");
            }
        }

        static void HighRes()
        {
            var layout = LabelLayout.Estimate(Continuous62(50), true, false);
            Check(layout.DpiX == 600 && layout.DpiY == 300 && Math.Abs(layout.DotsX - 44 * 600 / 25.4) <= 3, "300 x 600 layout");
            var doc = new LabelDoc();
            doc.Objects.Add(new ShapeObject { X = 10, Y = 10, W = 10, H = 5, Filled = true });
            using (var bmp = EditorRender.Render(doc, layout))
            {
                var r = EditorRender.DotRect(doc.Objects[0], layout);
                Check(Math.Abs(r.Width - 236) <= 1 && r.Height == 59, "box at 300 x 600: twice the dots along " + r);
                Check(Black(bmp, r.X + 1, r.Y + 1) == 1 && Black(bmp, r.Right - 2, r.Bottom - 2) == 1, "box filled at 300 x 600");
            }
        }

        // Across the tape: the view is the driver's portrait page itself (no turning to print).
        static void Across()
        {
            var label = new LabelChoice { Name = "62mm x 29mm", Media = MediaTable.ById(274) ?? MediaTable.All.First(m => m.Name == "62mm x 29mm"), LengthMm = 29 };
            Check(LabelLayout.AcrossByDefault(label) && !LabelLayout.AcrossByDefault(Continuous62(100)), "short wide labels default to across");
            label.LengthMm = label.Media.LengthMm;
            var g = new LabelGeometry { DpiX = 300, DpiY = 300, Width = 696, Height = 271, OffsetX = 12, OffsetY = 35, PageWidth = 732, PageHeight = 341 };
            var across = LabelLayout.FromGeometry(label, g, null, null, true);
            var along = LabelLayout.FromGeometry(label, g, null, null, false);
            Check(across.DotsX == 696 && across.DotsY == 271 && along.DotsX == 271 && along.DotsY == 696, "dots per orientation");
            // across the tape centred (the plug-in centres the band on the head), whatever offset the driver reports
            Check(Math.Abs(across.Printable.X - 18 * 25.4 / 300) < 1e-4 && Math.Abs(across.Printable.Y - 35 * 25.4 / 300) < 1e-4, "across: printable origin");
            Check(Math.Abs(along.Printable.X - 35 * 25.4 / 300) < 1e-4 && Math.Abs(along.Printable.Y - 18 * 25.4 / 300) < 1e-4, "along: printable origin");
            var doc = new LabelDoc();
            doc.Objects.Add(new ShapeObject { X = 5, Y = 5, W = 3, H = 3, Filled = true });
            using (var bmp = EditorRender.Render(doc, across))
            using (var p = EditorRender.ForPrinter(bmp, across))
            {
                Check(p.Width == 696 && p.Height == 271, "across prints as rendered");
                var r = EditorRender.DotRect(doc.Objects[0], across);
                Check(Black(p, r.X + 2, r.Y + 2) == 1, "across: same dots on the page");
            }
            doc.Across = true;
            Check(LabelDoc.FromJson(doc.ToJson()).Across, "view saved");
        }

        static void Images()
        {
            var grey = Enumerable.Repeat((byte)128, 100 * 100).ToArray();
            int dithered = EditorRender.Dither(grey, 100, 100).Count(b => b);
            Check(dithered > 4500 && dithered < 5500, "dithered mid-grey is about half black: " + dithered);
            Check(EditorRender.Dither(Enumerable.Repeat((byte)18, 10000).ToArray(), 100, 100).All(b => b), "dither: near-black (RGB 19,19,18) prints solid black");
            Check(!EditorRender.Dither(Enumerable.Repeat((byte)232, 10000).ToArray(), 100, 100).Any(b => b), "dither: near-white stays white");
            int g26 = EditorRender.Dither(Enumerable.Repeat((byte)26, 10000).ToArray(), 100, 100).Count(b => !b);
            Check(g26 > 0 && g26 < 1500, "dither: just under 90 % black still dithers: " + g26 + " white");
            Check(EditorRender.Threshold(grey, 129).All(b => b) && EditorRender.Threshold(grey, 128).All(b => !b), "threshold");

            // A picture: left half black, right half white; 20 x 10 mm.
            using (var pic = new Bitmap(40, 20))
            {
                using (var g = Graphics.FromImage(pic)) { g.Clear(Color.White); g.FillRectangle(Brushes.Black, 0, 0, 20, 20); }
                byte[] png;
                using (var ms = new System.IO.MemoryStream()) { pic.Save(ms, System.Drawing.Imaging.ImageFormat.Png); png = ms.ToArray(); }
                var img = new ImageObject { X = 10, Y = 10, W = 20, H = 10, Png = png, Mode = ImageMode.Threshold };
                var layout = LabelLayout.Estimate(Continuous62(50), false, false);
                var doc = new LabelDoc();
                doc.Objects.Add(img);
                using (var bmp = EditorRender.Render(doc, layout))
                {
                    var r = EditorRender.DotRect(img, layout);
                    Check(Black(bmp, r.X + 5, r.Y + 5) == 1 && Black(bmp, r.Right - 5, r.Y + 5) == 0, "picture: black left, white right");
                }
                img.Rotation = 180;
                using (var bmp = EditorRender.Render(doc, layout))
                {
                    var r = EditorRender.DotRect(img, layout);
                    Check(Black(bmp, r.X + 5, r.Y + 5) == 0 && Black(bmp, r.Right - 5, r.Y + 5) == 1, "picture turned 180");
                }
                img.Invert = true;
                using (var bmp = EditorRender.Render(doc, layout))
                {
                    var r = EditorRender.DotRect(img, layout);
                    Check(Black(bmp, r.X + 5, r.Y + 5) == 1, "picture inverted");
                }

                // Replace picture: the new picture is drawn, not the one decoded before.
                img.Invert = false; img.Rotation = 0;
                byte[] white;
                using (var w = new Bitmap(40, 20))
                using (var ms = new System.IO.MemoryStream())
                {
                    using (var g = Graphics.FromImage(w)) g.Clear(Color.White);
                    w.Save(ms, System.Drawing.Imaging.ImageFormat.Png); white = ms.ToArray();
                }
                img.Png = white;
                using (var bmp = EditorRender.Render(doc, layout))
                {
                    var r = EditorRender.DotRect(img, layout);
                    Check(Black(bmp, r.X + 5, r.Y + 5) == 0, "replaced picture drawn");
                }

                // A picture far larger than the label (3000 mm, the panel's limit) is drawn, not an out-of-memory error.
                var giant = new ImageObject { X = layout.Printable.X, Y = layout.Printable.Y, W = 3000, H = 3000, Png = png, Mode = ImageMode.Threshold };
                var giantDoc = new LabelDoc();
                giantDoc.Objects.Add(giant);
                using (var bmp = EditorRender.Render(giantDoc, layout))
                    Check(Black(bmp, 5, 5) == 1, "giant picture drawn");
            }

            // A damaged file: picture data that is no picture and a giant box load and are left out of the drawing;
            // the rest of the label is drawn. Sizes are limited to the panel's ranges.
            var damaged = LabelDoc.FromJson("{\"format\":\"QL label\",\"objects\":[" +
                "{\"type\":\"image\",\"x\":5,\"y\":5,\"w\":10,\"h\":10,\"png\":\"AAAA\"}," +
                "{\"type\":\"box\",\"x\":-1e12,\"y\":1,\"w\":1e13,\"h\":5}," +
                "{\"type\":\"box\",\"x\":10,\"y\":10,\"w\":10,\"h\":10,\"filled\":true}]}");
            Check(damaged.Objects.Count == 3 && damaged.Objects[1].X == LabelObject.MinPos && damaged.Objects[1].W == LabelObject.MaxMm, "damaged sizes limited");
            Check(((ImageObject)damaged.Objects[0]).Picture == null, "damaged picture data reads as no picture");
            var dl = LabelLayout.Estimate(Continuous62(50), false, false);
            using (var bmp = EditorRender.Render(damaged, dl))
            {
                var r = EditorRender.DotRect(damaged.Objects[2], dl);
                Check(Black(bmp, r.X + r.Width / 2, r.Y + r.Height / 2) == 1, "damaged file: the rest is drawn");
            }
        }
    }
}
