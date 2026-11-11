// Replay of saved clipboard dumps (out\clipboard\<name>\: one file per format "<id>-<name>.<ext>" and formats.txt, made
// read-only from the real clipboard) through QL Label Maker's paste decision (EditorClipboard.Read). Each dump is served
// twice: as a COM IDataObject (the EMF as a real metafile handle, TYMED_ENHMF; the others as HGLOBAL), read through
// EditorClipboard.FromDrop, i.e. the same ComSource code a paste and a drop use, and as plain bytes. The expected result
// comes from the folder's expected.txt (one line, e.g. "vector"), else from its name (a "-2" etc. at the end is a later
// dump of the same case). A dump's EMF with text must also render for printer dots without greys from its text (crisp
// text; a grey fill may stay grey). The dumps are private (not
// fixtures, not in git): without the folder the test is skipped. Never touches the clipboard.
// labeltool\build.cmd runs it as x64 and, on an ARM64 PC, natively as ARM64 (the exe has the programs' manifest).
// Usage: clipboard_replay.exe [<folder with the dumps>]   (default: out\clipboard next to labeltool); exit = failures
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace QlLabels.Tests
{
    static class ClipboardReplay
    {
        // What each case must become (the original round of dumps, 2026-11-08).
        static readonly Dictionary<string, string> Expected = new Dictionary<string, string>
        {
            { "corel-shape", "vector" }, { "corel-text", "text" }, { "corel-label", "vector with text" }, { "word-text", "runs" },
            { "affinity-shape", "vector" }, { "designer-shape", "vector" }, { "vectorcraft-shape", "vector" },
            { "edge-image", "picture" }, { "word-picture", "picture" }, { "word-svg", "vector" }, { "explorer-files", "files" },
        };

        [STAThread]
        static int Main(string[] args)
        {
            Lang.Force("en");
            if (args.Length == 3 && args[0] == "--serve") return Serve(args[1], args[2]);
            if (args.Length == 3 && args[0] == "--fetch") return Fetch(args[1], args[2]);
            if (args.Length == 1 && args[0] == "--live") return Live();
            if (args.Length == 3 && args[0] == "--render") return RenderDump(args[1], args[2]);
            string dir = args.Length > 0 ? args[0]
                       : Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(typeof(ClipboardReplay).Assembly.Location))), "clipboard");   // out\test\x.exe -> out\clipboard
            string arch = RuntimeInformation.ProcessArchitecture.ToString();
            if (!Directory.Exists(dir)) { Console.WriteLine("clipboard_replay (" + arch + "): no dumps in " + dir + ", skipped"); return 0; }
            int failures = 0, cases = 0;
            foreach (var d in Directory.GetDirectories(dir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                var formats = Formats(d);
                if (formats.Count == 0) continue;
                string name = Path.GetFileName(d), key = Regex.Replace(name, @"-\d+$", "");
                string want, own = Path.Combine(d, "expected.txt");
                if (File.Exists(own)) want = File.ReadAllText(own).Trim();
                else Expected.TryGetValue(key, out want);
                foreach (var how in new[] { "COM", "bytes" })
                {
                    string got;
                    try
                    {
                        var source = how == "COM" ? EditorClipboard.FromDrop(new DumpObject(formats)) : new ByteSource(formats);
                        var read = EditorClipboard.Read(source);
                        got = want != null && (want.StartsWith("text:") || want.StartsWith("label:")) ? TextDetail(read)
                            : want != null && want.StartsWith("picture:") ? PictureDetail(read)
                            : want != null && want.StartsWith("size:") ? "size: " + Describe(read) + " " + SizeText(read) : Describe(read);
                    }
                    catch (Exception ex) { got = "exception " + ex.GetType().Name + ": " + ex.Message; }
                    cases++;
                    bool ok = want == null || got == want;
                    if (!ok) failures++;
                    Console.WriteLine("{0,-5} {1,-20} {2,-6} {3}{4}", ok ? (want == null ? "INFO" : "OK") : "FAIL", name, how, got,
                                      ok ? "" : " (expected: " + want + ")");
                }
                // An EMF with text, rendered for printer dots (crisp, as after a paste: repaired): no greys for Dither to
                // scatter. CorelDRAW's white text on white was smoothed into a dotted patch.
                try
                {
                    bool same;
                    var both = EditorClipboard.Read(new ByteSource(formats));
                    // a picture: its EMF as pasted, CorelDRAW's small text set again from the CMX (Import/Corel/CorelPicture.cs)
                    if (both.Kind == ClipKind.Emf && both.EmfKind != EmfPicture.Content.TextOnly) both.Emf = CorelPicture.Fix(both.Emf, both.Cmx);
                    string cross = CrossCheck(both, out same);
                    if (cross != null)
                    {
                        cases++;
                        if (!same) failures++;
                        Console.WriteLine("{0,-5} {1,-20} {2,-6} {3}", same ? "OK" : "FAIL", name, "cmx", cross);
                    }
                }
                catch (Exception ex) { cases++; failures++; Console.WriteLine("FAIL  {0,-20} cmx    exception {1}: {2}", name, ex.GetType().Name, ex.Message); }
                foreach (var file in Directory.GetFiles(d, "*.emf"))
                {
                    var emf = EmfText.Repair(File.ReadAllBytes(file));
                    if (!EmfPicture.What(emf).HasText) continue;
                    var mm = EmfPicture.SizeMm(emf);
                    int w = (int)Math.Round(mm.Width / 25.4 * 300), h = (int)Math.Round(mm.Height / 25.4 * 300);
                    // greys the text adds: grey here, not grey without the text (a grey fill stays grey)
                    var all = Greys(EmfPicture.Render(emf, w, h, true));
                    var drawing = Greys(EmfPicture.Render(EmfLayers.WithoutText(emf), w, h, true));
                    int smooth = DitherRange(EmfPicture.Render(emf, w, h));
                    int crisp = all == null ? -1 : all.Where((g, i) => g && (drawing == null || i >= drawing.Length || !drawing[i])).Count();
                    cases++;
                    if (crisp != 0) failures++;
                    Console.WriteLine("{0,-5} {1,-20} {2,-6} crisp text: {3} grey pixels for Dither ({4} smoothed)", crisp == 0 ? "OK" : "FAIL", name, "print", crisp, smooth);
                }
            }
            Console.WriteLine("clipboard_replay (" + arch + "): " + (failures == 0 ? "all " + cases + " OK" : failures + " of " + cases + " failed"));
            return failures;
        }

        // Pixels of a render (on white) that Dither would scatter: greys between its snapped ends; -1: not drawn.
        static int DitherRange(System.Drawing.Bitmap bmp)
        {
            var g = Greys(bmp);
            return g == null ? -1 : g.Count(x => x);
        }

        // Per pixel (row by row): grey enough for Dither to scatter; null: no picture.
        static bool[] Greys(System.Drawing.Bitmap bmp)
        {
            if (bmp == null) return null;
            var on = new bool[bmp.Width * bmp.Height];
            using (bmp)
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        int l = (c.R * 299 + c.G * 587 + c.B * 114) / 1000, grey = 255 - (255 - l) * c.A / 255;
                        on[y * bmp.Width + x] = grey > EditorRender.SnapBlack && grey < EditorRender.SnapWhite;
                    }
            return on;
        }

        // ---------- pictures to compare (by eye) ----------

        // --render <dump folder> <out folder>: a dump's picture formats side by side, as PNGs at 300 dpi:
        //   windows.png  the EMF played by GDI (PlayEnhMetaFile), as Windows and most programs show it, on white
        //   ours.png     what QL Label Maker draws it from (EmfPicture.Render, crisp), on blue: blue = transparent
        //   print.png    the pasted object as it prints (the label's black and white dots, picture mode as pasted)
        //   dib.png      the source program's own bitmap of it (CF_DIBV5, else CF_DIB), when there is one
        // Nothing is compared automatically; the dumps are private, so are the pictures.
        static int RenderDump(string dump, string outDir)
        {
            Directory.CreateDirectory(outDir);
            var formats = Formats(dump);
            var read = EditorClipboard.Read(new ByteSource(formats));
            Console.WriteLine("read: " + read.Kind + (read.Kind == ClipKind.Emf ? " " + read.EmfKind : "") + " -> " + Describe(EditorClipboard.Read(new ByteSource(formats))));
            if (read.Kind == ClipKind.Emf) Console.WriteLine(PictureDetail(EditorClipboard.Read(new ByteSource(formats))));
            var emfFormat = formats.FirstOrDefault(f => f.Id == 14);
            if (emfFormat != null)
            {
                var emf = emfFormat.Data;
                var mm = EmfPicture.SizeMm(emf);
                int w = Math.Max(1, (int)Math.Round(mm.Width / 25.4 * 300)), h = Math.Max(1, (int)Math.Round(mm.Height / 25.4 * 300));
                Console.WriteLine("EMF frame " + mm.Width.ToString("0.0") + " x " + mm.Height.ToString("0.0") + " mm, " + w + " x " + h + " dots");
                using (var bmp = GdiPlay(emf, w, h)) Save(bmp, outDir, "windows.png");
                using (var ours = EmfPicture.Render(EmfText.Repair(CorelPicture.Fix(emf, read.Cmx)), w, h, true))
                using (var onBlue = new System.Drawing.Bitmap(w, h))
                {
                    using (var g = System.Drawing.Graphics.FromImage(onBlue))
                    {
                        g.Clear(System.Drawing.Color.FromArgb(0, 160, 214));
                        if (ours != null) g.DrawImage(ours, 0, 0, w, h);
                    }
                    Save(onBlue, outDir, "ours.png");
                }
                var img = ImageObject.FromEmf(emf, read.Cmx);
                img.X = 0; img.Y = 0; img.W = mm.Width; img.H = mm.Height;
                var doc = new LabelDoc();
                doc.Objects.Add(img);
                var layout = LabelLayout.Estimate(new LabelChoice { Name = "Render", Media = MediaTable.ById(259), LengthMm = 70, FeedMm = 3, IsFormat = true }, false, false).Copy();
                layout.Printable = new System.Drawing.RectangleF(0, 0, mm.Width, mm.Height);
                layout.DpiX = layout.DpiY = 300; layout.DotsX = w; layout.DotsY = h;
                using (var bmp = EditorRender.Render(doc, layout, DateTime.Now)) Save(bmp, outDir, "print.png");
                // a whole label from CorelDRAW: its parts as the paste places them (pictures and text objects)
                List<string> missing; string how;
                var parts = TextImport.FromLabel(read, out missing, out how);
                if (parts != null)
                {
                    var split = new LabelDoc();
                    foreach (var part in parts)
                    {
                        var pic = part as EmfLayers.Picture;
                        if (pic == null) { split.Objects.Add((TextObject)part); continue; }
                        var o = ImageObject.FromEmf(pic.Emf, read.Cmx);
                        o.X = pic.Mm.X; o.Y = pic.Mm.Y; o.W = pic.Mm.Width; o.H = pic.Mm.Height;
                        split.Objects.Add(o);
                    }
                    using (var bmp = EditorRender.Render(split, layout, DateTime.Now)) Save(bmp, outDir, "label.png");
                }
            }
            var dib = formats.FirstOrDefault(f => f.Id == 17) ?? formats.FirstOrDefault(f => f.Id == 8);
            if (dib != null)
                using (var bmp = EditorClipboard.Dib(dib.Data)) if (bmp != null) Save(bmp, outDir, "dib.png");
            return 0;
        }

        static void Save(System.Drawing.Bitmap bmp, string dir, string name)
        {
            if (bmp == null) { Console.WriteLine(name + ": not drawn"); return; }
            string path = Path.Combine(dir, name);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine(path);
        }

        // The EMF played by GDI onto white, stretched to w x h.
        static System.Drawing.Bitmap GdiPlay(byte[] emf, int w, int h)
        {
            var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.Clear(System.Drawing.Color.White);
                IntPtr hdc = g.GetHdc(), hemf = SetEnhMetaFileBits((uint)emf.Length, emf);
                try
                {
                    var r = new RECT { Right = w, Bottom = h };
                    PlayEnhMetaFile(hdc, hemf, ref r);
                }
                finally
                {
                    DeleteEnhMetaFile(hemf);
                    g.ReleaseHdc(hdc);
                }
            }
            return bmp;
        }

        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("gdi32.dll")] static extern bool PlayEnhMetaFile(IntPtr hdc, IntPtr hemf, ref RECT r);
        [DllImport("gdi32.dll")] static extern bool DeleteEnhMetaFile(IntPtr hemf);

        // ---------- across processes (another program's data object, as on a real paste) ----------

        // --serve <dump folder> <name>: the dump's data object in the running object table until <name>.stop appears next
        // to the dump folder (60 s at most). Started as x64 it stands for an x64 program such as CorelDRAW.
        static int Serve(string dump, string name)
        {
            var obj = new DumpObject(Formats(dump));
            ComTypes.IRunningObjectTable rot; ComTypes.IMoniker mk;
            GetRunningObjectTable(0, out rot);
            CreateItemMoniker("!", "QlClipReplay-" + name, out mk);
            int cookie = rot.Register(1, obj, mk);   // ROTFLAGS_REGISTRATIONKEEPSALIVE
            string stop = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dump)), name + ".stop");
            Console.WriteLine("serving " + name + " (" + RuntimeInformation.ProcessArchitecture + ")");
            var until = DateTime.Now.AddSeconds(60);
            while (DateTime.Now < until && !File.Exists(stop)) { Application.DoEvents(); System.Threading.Thread.Sleep(20); }
            rot.Revoke(cookie);
            return 0;
        }

        // --fetch <name> <expected>: the served object through EditorClipboard (COM proxy: the data crosses processes and,
        // on an ARM64 PC, architectures as from CorelDRAW to a native QL Label Maker).
        static int Fetch(string name, string want)
        {
            ComTypes.IRunningObjectTable rot; ComTypes.IMoniker mk; object o = null; string last = "";
            GetRunningObjectTable(0, out rot);
            CreateItemMoniker("!", "QlClipReplay-" + name, out mk);
            for (int i = 0; i < 100 && o == null; i++)
            {
                try { rot.GetObject(mk, out o); } catch (COMException ex) { last = ex.Message; System.Threading.Thread.Sleep(100); }
            }
            string arch = RuntimeInformation.ProcessArchitecture.ToString();
            if (o == null) { Console.WriteLine("FAIL  " + name + " cross-process (" + arch + "): not served (" + last + ")"); return 1; }
            string got;
            try { got = Describe(EditorClipboard.Read(EditorClipboard.FromDrop(new Remote((ComTypes.IDataObject)o)))); }
            catch (Exception ex) { got = "exception " + ex.GetType().Name + ": " + ex.Message; }
            bool ok = got == want;
            Console.WriteLine("{0,-5} {1,-20} {2} {3}{4}", ok ? "OK" : "FAIL", name, "cross-process " + arch, got, ok ? "" : " (expected: " + want + ")");
            return ok ? 0 : 1;
        }

        // A COM proxy handed to FromDrop (which takes WinForms' IDataObject): the COM calls go to the other process.
        sealed class Remote : ComTypes.IDataObject, IDataObject
        {
            readonly ComTypes.IDataObject d;
            public Remote(ComTypes.IDataObject d) { this.d = d; }
            public int QueryGetData(ref ComTypes.FORMATETC f) { return d.QueryGetData(ref f); }
            public void GetData(ref ComTypes.FORMATETC f, out ComTypes.STGMEDIUM m) { d.GetData(ref f, out m); }
            public void GetDataHere(ref ComTypes.FORMATETC f, ref ComTypes.STGMEDIUM m) { d.GetDataHere(ref f, ref m); }
            public int GetCanonicalFormatEtc(ref ComTypes.FORMATETC i, out ComTypes.FORMATETC o) { return d.GetCanonicalFormatEtc(ref i, out o); }
            public void SetData(ref ComTypes.FORMATETC f, ref ComTypes.STGMEDIUM m, bool r) { d.SetData(ref f, ref m, r); }
            public ComTypes.IEnumFORMATETC EnumFormatEtc(ComTypes.DATADIR dir) { return d.EnumFormatEtc(dir); }
            public int DAdvise(ref ComTypes.FORMATETC f, ComTypes.ADVF a, ComTypes.IAdviseSink s, out int c) { return d.DAdvise(ref f, a, s, out c); }
            public void DUnadvise(int c) { d.DUnadvise(c); }
            public int EnumDAdvise(out ComTypes.IEnumSTATDATA e) { return d.EnumDAdvise(out e); }
            object IDataObject.GetData(string format, bool autoConvert) { return null; }
            object IDataObject.GetData(string format) { return null; }
            object IDataObject.GetData(Type format) { return null; }
            void IDataObject.SetData(string format, bool autoConvert, object data) { }
            void IDataObject.SetData(string format, object data) { }
            void IDataObject.SetData(Type format, object data) { }
            void IDataObject.SetData(object data) { }
            bool IDataObject.GetDataPresent(string format, bool autoConvert) { return false; }
            bool IDataObject.GetDataPresent(string format) { return false; }
            bool IDataObject.GetDataPresent(Type format) { return false; }
            string[] IDataObject.GetFormats(bool autoConvert) { return new string[0]; }
            string[] IDataObject.GetFormats() { return new string[0]; }
        }

        // --live: READ-ONLY diagnosis of the clipboard as it is now (nothing is written or emptied): per picture format what
        // OLE's QueryGetData/GetData answer (HRESULTs, sizes) and what EditorClipboard.Read decides, as QL Label Maker would.
        static int Live()
        {
            Console.WriteLine("live clipboard read (" + RuntimeInformation.ProcessArchitecture + "), read-only");
            ComTypes.IDataObject d;
            int hr = OleGetClipboard(out d);
            Console.WriteLine("  OleGetClipboard: 0x" + hr.ToString("X8"));
            if (hr != 0 || d == null) return 1;
            foreach (var f in new[] { new { Name = "CF_ENHMETAFILE", Id = (short)14, Tymed = ComTypes.TYMED.TYMED_ENHMF },
                                      new { Name = "CF_DIBV5", Id = (short)17, Tymed = ComTypes.TYMED.TYMED_HGLOBAL },
                                      new { Name = "CF_DIB", Id = (short)8, Tymed = ComTypes.TYMED.TYMED_HGLOBAL },
                                      new { Name = "PNG", Id = unchecked((short)RegisterClipboardFormat("PNG")), Tymed = ComTypes.TYMED.TYMED_HGLOBAL | ComTypes.TYMED.TYMED_ISTREAM } })
            {
                var etc = new ComTypes.FORMATETC { cfFormat = f.Id, dwAspect = ComTypes.DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = f.Tymed };
                int q = d.QueryGetData(ref etc);
                string get;
                try
                {
                    ComTypes.STGMEDIUM m;
                    d.GetData(ref etc, out m);
                    get = "tymed " + m.tymed + (m.tymed == ComTypes.TYMED.TYMED_ENHMF ? ", " + (EmfPicture.FromHandle(m.unionmember) ?? new byte[0]).Length + " bytes" : "");
                    ReleaseStgMedium(ref m);
                }
                catch (Exception ex) { get = ex.GetType().Name + " 0x" + Marshal.GetHRForException(ex).ToString("X8"); }
                Console.WriteLine("  {0,-15} QueryGetData 0x{1:X8}, GetData: {2}", f.Name, q, get);
            }
            var c = EditorClipboard.Read(EditorClipboard.FromClipboard());
            Console.WriteLine("  EditorClipboard.Read: " + c.Kind + (c.Kind == ClipKind.Emf ? " " + c.EmfKind : "") + " -> " + Describe(c));
            return 0;
        }

        [DllImport("ole32.dll")] static extern int OleGetClipboard(out ComTypes.IDataObject data);
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref ComTypes.STGMEDIUM medium);
        [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out ComTypes.IRunningObjectTable rot);
        [DllImport("ole32.dll", CharSet = CharSet.Unicode)] static extern int CreateItemMoniker(string delim, string item, out ComTypes.IMoniker mk);

        // What the editor makes of it, in the words of Expected.
        static string Describe(ClipContent c)
        {
            switch (c.Kind)
            {
                case ClipKind.Svg:
                    {
                        // Import/SvgText.cs: text only becomes text objects; text with drawing stays a picture
                        var st = SvgText.Read(c.SvgSource, c.SizeMm, SvgQuirks.For(c.Source));
                        return st == null ? "vector" : st.OnlyText ? "text" : "text + picture";
                    }
                case ClipKind.Emf:
                    if (c.EmfKind == EmfPicture.Content.TextOnly)
                    {
                        List<string> missing;
                        try { return EmfText.Read(c.Emf, EmfQuirks.For(c.Source), out missing).Count > 0 ? "text" : "vector"; }
                        catch (InvalidDataException) { return "vector"; }
                    }
                    return EmfPicture.What(c.Emf).HasText ? "vector with text" : "vector";
                case ClipKind.Rtf:
                    List<string> gone;
                    return RtfRuns.Read(c.Rtf, out gone).Runs.Count > 1 ? "runs" : "text";
                case ClipKind.Picture: c.Picture.Dispose(); return "picture";
                case ClipKind.Files: return c.Files.Length > 0 ? "files" : "nothing";
                case ClipKind.Text: return "plain text";
                case ClipKind.Objects: return "objects";
                default: return "nothing";
            }
        }

        // A picture paste in detail (an expected.txt line starting "picture:"): the EMF's geometric pen widths as pasted
        // (logical units; CorelDRAW's outlines scaled by its CMX, Import/Corel/CorelPicture.cs) and the printed dots at
        // 300 dpi in its own size, black and clear (transparent: no white box), in whole percent:
        // picture: pens 24, black 39%, clear 49%
        static string PictureDetail(ClipContent c)
        {
            if (c.Kind != ClipKind.Emf) return Describe(c);
            var img = ImageObject.FromEmf(c.Emf, c.Cmx);
            var pens = new List<int>();
            var emf = img.Emf;
            for (int pos = BitConverter.ToInt32(emf, 4); pos + 8 <= emf.Length; )
            {
                int type = BitConverter.ToInt32(emf, pos), size = BitConverter.ToInt32(emf, pos + 4);
                if (type == 14 || size < 8) break;
                if (type == 95 && size >= 36 && (BitConverter.ToInt32(emf, pos + 28) & 0x10000) != 0) pens.Add(BitConverter.ToInt32(emf, pos + 32));
                pos += size;
            }
            var mm = EmfPicture.SizeMm(c.Emf);
            int w = Math.Max(1, (int)Math.Round(mm.Width / 25.4 * 300)), h = Math.Max(1, (int)Math.Round(mm.Height / 25.4 * 300));
            img.W = mm.Width; img.H = mm.Height;
            int black = 0, clear = 0;
            var bmp = EditorRender.OneBit(img, w, h);   // cached by the renderer: not disposed here
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = bmp.GetPixel(x, y);
                    if (p.A < 128) clear++;
                    else if (p.R < 128) black++;
                }
            return "picture: pens " + (pens.Count > 0 ? string.Join(" ", pens) : "none") + ", black " + Math.Round(100.0 * black / (w * h)) +
                   "%, clear " + Math.Round(100.0 * clear / (w * h)) + "%";
        }

        // The size it pastes at (an expected.txt line starting "size:"), mm.
        static string SizeText(ClipContent c)
        {
            if (c.SizeMm == null) return "no size";
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return c.SizeMm.Value.Width.ToString("0.0", ci) + "x" + c.SizeMm.Value.Height.ToString("0.0", ci) + "mm";
        }

        // A text paste in detail (an expected.txt line starting "text:"), one part per object:
        // text: Frame Center "LED MIX SET|3mm, 120 STÜCK" ITC Franklin Gothic Std Bk Cp 13.21pt bold, via cmx   ("|": a line
        // break; "via": emf, cmx or cmx partly (Import/TextImport.cs), svg or svg + picture (Import/SvgText.cs))
        static string TextDetail(ClipContent c)
        {
            List<string> missing;
            string how;
            List<TextObject> texts;
            if (c.Kind == ClipKind.Emf && c.EmfKind == EmfPicture.Content.Vector)
            {
                // a whole label from CorelDRAW: pictures and texts in z-order
                var label = TextImport.FromLabel(c, out missing, out how);
                if (label == null) return Describe(c);
                return "label: " + string.Join(" / ", label.Select(p => p is EmfLayers.Picture ? PictureLine((EmfLayers.Picture)p) : TextLine((TextObject)p)))
                       + ", via " + how + (missing.Count > 0 ? " (missing " + string.Join(", ", missing) + ")" : "");
            }
            if (c.Kind == ClipKind.Svg)
            {
                var st = SvgText.Read(c.SvgSource, c.SizeMm, SvgQuirks.For(c.Source));
                if (st == null) return Describe(c);
                texts = st.Objects; missing = st.Missing; how = st.OnlyText ? "svg" : "svg + picture";
            }
            else
            {
                if (c.Kind != ClipKind.Emf || c.EmfKind != EmfPicture.Content.TextOnly) return Describe(c);
                try { texts = TextImport.FromEmf(c, out missing, out how); }
                catch (InvalidDataException) { return "vector"; }
            }
            var parts = new List<string>();
            foreach (var t in texts) parts.Add(TextLine(t));
            return "text: " + string.Join(" / ", parts) + ", via " + how + (missing.Count > 0 ? " (missing " + string.Join(", ", missing) + ")" : "");
        }

        static string PictureLine(EmfLayers.Picture p)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return "picture " + p.Mm.Width.ToString("0.#", inv) + "x" + p.Mm.Height.ToString("0.#", inv) + " at "
                   + p.Mm.X.ToString("0.#", inv) + "," + p.Mm.Y.ToString("0.#", inv);
        }

        static string TextLine(TextObject t)
        {
            return t.Kind + " " + t.Align + " \"" + t.Text.Replace("\n", "|") + "\" " + t.Font + " "
                   + t.Size.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "pt" + (t.Bold ? " bold" : "") + (t.Italic ? " italic" : "") + (t.Underline ? " underline" : "")
                   + (t.Kind == TextKind.Frame ? " " + t.LineSpacing.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"
                                                 + " box " + t.W.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "x" + t.H.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "")
                   + string.Concat(t.FullRuns().Where(r => r.Spacing.HasValue && r.Spacing.Value != 100).Select(r => r.Spacing.Value).Distinct()
                                    .Select(v => " letters " + v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%"))
                   + (t.Angle != 0 ? " turned " + t.Angle.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) : "")
                   + (t.VerticalAlign != VerticalAlign.Top ? " " + t.VerticalAlign.ToString().ToLowerInvariant() : "")
                   + (t.SpaceBefore != 0 ? " before " + t.SpaceBefore.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%" : "")
                   + (t.SpaceAfter != 0 ? " after " + t.SpaceAfter.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%" : "")
                   + (t.Invert ? " inverted" : "")
                   + string.Concat(t.FullRuns().Where(r => !t.Invert && r.Invert == true && r.Text.Trim().Length > 0).Select(r => " inverted:" + r.Text.Trim()))
                   + string.Concat(t.FullRuns().Where(r => r.Bold.Value != t.Bold || r.Italic.Value != t.Italic || r.Underline.Value != t.Underline)
                                    .Select(r => " run \"" + r.Text.Replace("\n", "|") + "\"" + RunStyle(r)));
        }

        // A run's bold / italic / underline (a text line lists the runs whose differ from their object's)
        static string RunStyle(TextRun r)
        {
            string s = (r.Bold.Value ? " bold" : "") + (r.Italic.Value ? " italic" : "") + (r.Underline.Value ? " underline" : "");
            return s.Length > 0 ? s : " regular";
        }

        // CorelDRAW's CMX against its EMF: the same characters, the same sizes, and every laid-out run of the CMX where
        // the EMF has a run with that text, at the same offset from the first one (0.05 mm). Null: no CMX.
        static string CrossCheck(ClipContent c, out bool ok)
        {
            ok = true;
            if (c.Cmx == null || c.Emf == null) return null;
            List<CmxText> cmx;
            try { cmx = Cmx.Read(c.Cmx); }
            catch (InvalidDataException ex) { ok = false; return "CMX unreadable: " + ex.Message; }
            var runs = EmfText.Runs(c.Emf);
            Func<string, string> squeeze = s => new string(s.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            // the EMF writes a line's runs in its own order (CorelDRAW: the right part first), so compare the characters
            Func<string, string> sorted = s => new string(squeeze(s).OrderBy(ch => ch).ToArray());
            string a = sorted(string.Concat(cmx.Select(t => t.Text))), b = sorted(string.Concat(runs.Select(r => r.Text)));
            var notes = new List<string>();
            if (a != b) { ok = false; notes.Add("text differs: CMX \"" + a + "\", EMF \"" + b + "\""); }
            foreach (var t in cmx)
            {
                var emfRun = runs.FirstOrDefault(r => squeeze(r.Text).Length > 0 && squeeze(t.Text).StartsWith(squeeze(r.Text)));
                // the EMF's size goes through CorelDRAW's rounded mapping (up to 2 % off); the CMX has the exact one
                if (emfRun != null && Math.Abs(emfRun.SizePt - t.SizePt) > Math.Max(0.05, t.SizePt * 0.03)) { ok = false; notes.Add("size: CMX " + t.SizePt + " pt, EMF " + emfRun.SizePt + " pt"); }
            }
            int compared = 0;
            double worst = 0;
            foreach (var t in cmx)
            {
                if (t.Glyphs.Count < 2) continue;
                var pairs = t.Glyphs.Select(g => new { g, e = runs.FirstOrDefault(r => r.Text == g.Text) }).Where(p => p.e != null).ToList();
                if (pairs.Count < 2) continue;
                var g0 = pairs[0].g.Start; var e0 = pairs[0].e.Start;
                // the CMX places are the text's own (unturned): turned onto the page by its matrix (m11 m12 m21 m22)
                var m = t.Matrix ?? new double[] { 1, 0, 0, 1, 0, 0 };
                double len = Math.Sqrt(m[0] * m[0] + m[1] * m[1]);
                double c11 = len > 0 ? m[0] / len : 1, c12 = len > 0 ? m[1] / len : 0, c21 = -c12, c22 = c11;
                foreach (var p in pairs.Skip(1))
                {
                    double ox = p.g.Start.X - g0.X, oy = p.g.Start.Y - g0.Y;
                    double gx = ox * c11 + oy * c21, gy = ox * c12 + oy * c22;
                    double dx = gx - (p.e.Start.X - e0.X), dy = -gy - (p.e.Start.Y - e0.Y);   // CMX y up
                    // the same mapping scales the EMF's places too (up to 4 % seen): 5 % of the distance (at least 0.05 mm)
                    double allowed = Math.Max(0.05, 0.05 * Math.Sqrt(gx * gx + gy * gy));
                    worst = Math.Max(worst, Math.Sqrt(dx * dx + dy * dy) / allowed * 0.05);
                    compared++;
                }
            }
            if (worst > 0.05) { ok = false; notes.Add("run offsets differ by up to " + (worst / 0.05).ToString("0.0") + " times the tolerance"); }
            // line spacing: CorelDRAW's value against the one from the EMF's pitch (shown only; the pitch goes through
            // the installed font's metrics)
            string spacing = "";
            var cmxSpacing = cmx.Where(t => t.LineSpacingPct > 0 && t.Text.Contains("\n")).Select(t => t.LineSpacingPct).ToList();
            if (cmxSpacing.Count > 0)
            {
                List<string> missing;
                var frames = EmfText.Read(c.Emf, EmfQuirks.For(c.Source), out missing).SelectMany(EmfText.Objects).Where(o => o.Kind == TextKind.Frame).ToList();
                spacing = ", line spacing CMX " + cmxSpacing[0].ToString("0.#") + " %"
                          + (frames.Count > 0 ? ", EMF pitch " + frames[0].LineSpacing.ToString("0.#") + " %" : "");
            }
            return (ok ? "CMX agrees with EMF: text, size, " + compared + " run offsets (worst " + (worst / 0.05 * 100).ToString("0") + " % of the tolerance)" + spacing : string.Join("; ", notes));
        }

        // ---------- the dump ----------

        sealed class Format { public int Id; public string Name; public byte[] Data; }

        // formats.txt names the formats ("  49410  Rich Text Format   12.345 bytes"); the files hold their bytes.
        static List<Format> Formats(string d)
        {
            var list = new List<Format>();
            string txt = Path.Combine(d, "formats.txt");
            if (!File.Exists(txt)) return list;
            var names = new Dictionary<int, string>();
            foreach (var line in File.ReadAllLines(txt))
            {
                var m = Regex.Match(line, @"^\s*(\d+)\s+(.+?)\s{2,}\S");
                if (m.Success) names[int.Parse(m.Groups[1].Value)] = m.Groups[2].Value.Trim();
            }
            foreach (var f in Directory.GetFiles(d))
            {
                var m = Regex.Match(Path.GetFileName(f), @"^(\d+)-");
                if (!m.Success) continue;
                int id = int.Parse(m.Groups[1].Value);
                string n;
                if (!names.TryGetValue(id, out n)) continue;
                list.Add(new Format { Id = id, Name = n, Data = File.ReadAllBytes(f) });
            }
            return list;
        }

        // The format's name as EditorClipboard asks for it (the standard formats by their DataFormats name).
        static string Wanted(Format f)
        {
            switch (f.Id)
            {
                case 1: return DataFormats.Text;
                case 8: return DataFormats.Dib;
                case 13: return DataFormats.UnicodeText;
                case 14: return EditorClipboard.EmfFormat;
                case 15: return DataFormats.FileDrop;
                case 17: return EditorClipboard.DibV5Format;
                default: return f.Id < 0xC000 ? null : f.Name;
            }
        }

        // As bytes (what a test's source gives): strings for the text formats, files for CF_HDROP.
        sealed class ByteSource : IClipSource
        {
            readonly Dictionary<string, object> data = new Dictionary<string, object>();
            public ByteSource(List<Format> formats)
            {
                foreach (var f in formats)
                {
                    string w = Wanted(f);
                    if (w == null) continue;
                    if (f.Id == 13) data[w] = Encoding.Unicode.GetString(f.Data, 0, f.Data.Length & ~1).TrimEnd('\0');
                    else if (f.Id == 1) data[w] = Encoding.Default.GetString(f.Data).TrimEnd('\0');
                    else if (f.Id == 15) data[w] = DropFiles(f.Data);
                    else data[w] = f.Data;
                }
            }
            public bool Has(string format) { return data.ContainsKey(format); }
            public object Get(string format) { object o; return data.TryGetValue(format, out o) ? o : null; }
        }

        // DROPFILES: offset of the list, wide flag, then zero-terminated names.
        static string[] DropFiles(byte[] b)
        {
            int at = BitConverter.ToInt32(b, 0);
            bool wide = BitConverter.ToInt32(b, 16) != 0;
            string all = wide ? Encoding.Unicode.GetString(b, at, b.Length - at) : Encoding.Default.GetString(b, at, b.Length - at);
            return all.Split('\0').Where(s => s.Length > 0).ToArray();
        }

        // As a COM data object, served as the owning program would: CF_ENHMETAFILE as a metafile handle, the rest in
        // global memory. EditorClipboard reads it through its ComSource (FromDrop), as a paste or a drop does.
        sealed class DumpObject : ComTypes.IDataObject, IDataObject
        {
            readonly Dictionary<short, Format> byId = new Dictionary<short, Format>();
            public DumpObject(List<Format> formats)
            {
                foreach (var f in formats)
                {
                    short id = f.Id < 0xC000 ? (short)f.Id : unchecked((short)RegisterClipboardFormat(f.Name));
                    byId[id] = f;
                }
            }

            static ComTypes.TYMED Tymed(short id) { return id == 14 ? ComTypes.TYMED.TYMED_ENHMF : ComTypes.TYMED.TYMED_HGLOBAL; }

            public int QueryGetData(ref ComTypes.FORMATETC format)
            {
                Format f;
                if (!byId.TryGetValue(format.cfFormat, out f)) return DV_E_FORMATETC;
                return (format.tymed & Tymed(format.cfFormat)) != 0 ? 0 : DV_E_TYMED;
            }

            public void GetData(ref ComTypes.FORMATETC format, out ComTypes.STGMEDIUM medium)
            {
                medium = new ComTypes.STGMEDIUM();
                if (QueryGetData(ref format) != 0) Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
                var f = byId[format.cfFormat];
                if (format.cfFormat == 14)
                {
                    medium.tymed = ComTypes.TYMED.TYMED_ENHMF;
                    medium.unionmember = SetEnhMetaFileBits((uint)f.Data.Length, f.Data);
                    if (medium.unionmember == IntPtr.Zero) Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
                    return;
                }
                IntPtr h = GlobalAlloc(0x0042, new UIntPtr((uint)f.Data.Length));   // GMEM_MOVEABLE | GMEM_ZEROINIT
                IntPtr p = GlobalLock(h);
                Marshal.Copy(f.Data, 0, p, f.Data.Length);
                GlobalUnlock(h);
                medium.tymed = ComTypes.TYMED.TYMED_HGLOBAL;
                medium.unionmember = h;
            }

            public void GetDataHere(ref ComTypes.FORMATETC format, ref ComTypes.STGMEDIUM medium) { throw new NotSupportedException(); }
            public int GetCanonicalFormatEtc(ref ComTypes.FORMATETC formatIn, out ComTypes.FORMATETC formatOut) { formatOut = formatIn; return DATA_S_SAMEFORMATETC; }
            public void SetData(ref ComTypes.FORMATETC formatIn, ref ComTypes.STGMEDIUM medium, bool release) { throw new NotSupportedException(); }
            public ComTypes.IEnumFORMATETC EnumFormatEtc(ComTypes.DATADIR direction) { throw new NotSupportedException(); }
            public int DAdvise(ref ComTypes.FORMATETC pFormatetc, ComTypes.ADVF advf, ComTypes.IAdviseSink adviseSink, out int connection) { connection = 0; return OLE_E_ADVISENOTSUPPORTED; }
            public void DUnadvise(int connection) { }
            public int EnumDAdvise(out ComTypes.IEnumSTATDATA enumAdvise) { enumAdvise = null; return OLE_E_ADVISENOTSUPPORTED; }

            // WinForms' side (FromDrop takes it; EditorClipboard only uses the COM side)
            object IDataObject.GetData(string format, bool autoConvert) { return null; }
            object IDataObject.GetData(string format) { return null; }
            object IDataObject.GetData(Type format) { return null; }
            void IDataObject.SetData(string format, bool autoConvert, object data) { }
            void IDataObject.SetData(string format, object data) { }
            void IDataObject.SetData(Type format, object data) { }
            void IDataObject.SetData(object data) { }
            bool IDataObject.GetDataPresent(string format, bool autoConvert) { return false; }
            bool IDataObject.GetDataPresent(string format) { return false; }
            bool IDataObject.GetDataPresent(Type format) { return false; }
            string[] IDataObject.GetFormats(bool autoConvert) { return new string[0]; }
            string[] IDataObject.GetFormats() { return new string[0]; }

            const int DV_E_FORMATETC = unchecked((int)0x80040064), DV_E_TYMED = unchecked((int)0x80040069);
            const int DATA_S_SAMEFORMATETC = 0x00040130, OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterClipboardFormat(string name);
        [DllImport("gdi32.dll")] static extern IntPtr SetEnhMetaFileBits(uint size, byte[] data);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    }
}
