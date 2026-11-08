// Replay of saved clipboard dumps (out\clipboard\<name>\: one file per format "<id>-<name>.<ext>" and formats.txt, made
// read-only from the real clipboard) through QL Label Maker's paste decision (EditorClipboard.Read). Each dump is served
// twice: as a COM IDataObject (the EMF as a real metafile handle, TYMED_ENHMF; the others as HGLOBAL), read through
// EditorClipboard.FromDrop, i.e. the same ComSource code a paste and a drop use, and as plain bytes. The expected result
// comes from the folder's expected.txt (one line, e.g. "vector"), else from its name (a "-2" etc. at the end is a later
// dump of the same case). A dump's EMF with text must also render for printer dots without greys (crisp text). The dumps are private (not
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
                        got = Describe(EditorClipboard.Read(source));
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
                foreach (var file in Directory.GetFiles(d, "*.emf"))
                {
                    var emf = EmfText.Repair(File.ReadAllBytes(file));
                    if (!EmfPicture.What(emf).HasText) continue;
                    var mm = EmfPicture.SizeMm(emf);
                    int w = (int)Math.Round(mm.Width / 25.4 * 300), h = (int)Math.Round(mm.Height / 25.4 * 300);
                    int smooth = DitherRange(EmfPicture.Render(emf, w, h)), crisp = DitherRange(EmfPicture.Render(emf, w, h, true));
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
            if (bmp == null) return -1;
            int n = 0;
            using (bmp)
                for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        var c = bmp.GetPixel(x, y);
                        int l = (c.R * 299 + c.G * 587 + c.B * 114) / 1000, grey = 255 - (255 - l) * c.A / 255;
                        if (grey > EditorRender.SnapBlack && grey < EditorRender.SnapWhite) n++;
                    }
            return n;
        }

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
                case ClipKind.Svg: return "vector";
                case ClipKind.Emf:
                    if (c.EmfKind == EmfPicture.Content.TextOnly)
                    {
                        List<string> missing;
                        try { return EmfText.Read(c.Emf, out missing).Count > 0 ? "text" : "vector"; }
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
