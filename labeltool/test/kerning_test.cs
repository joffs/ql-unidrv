// Unit test for pair kerning (Kerning.cs). Run by labeltool\build.cmd.
// 1. Fonts that ship with Windows: the old 'kern' table read by Kerning gives the same pairs as GDI's GetKerningPairs
//    (which reads that table), and GPOS kerning moves "AV" and "To" closer and leaves "oo" alone.
// 2. With a folder of clipboard dumps (argument; private, skipped when missing): every text record of a CorelDRAW EMF
//    whose font is installed here: CorelDRAW's dx for each character but the record's last (its last dx is 1) must be
//    the character's own advance (GDI, the record's font) plus the pair kerning, within one unit. Not compared: samples
//    made with other character or word spacing (sample.txt says "spacing"), and records whose pairs all lie the same
//    amount off (tracking: a logo set tighter).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace QlLabels.Tests
{
    static class KerningTest
    {
        static int failures, kernedPairs, kernTablePairs;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static int Main(string[] args)
        {
            Lang.Force("en");
            foreach (var family in new[] { "Arial", "Times New Roman", "Segoe UI", "Calibri", "Verdana" })
                if (Installed(family)) WindowsFont(family);
            Check(Kerning.Of("No such font family 0815", FontStyle.Regular).Pair('A', 'V') == 0, "unknown font kerns");
            Check(ReferenceEquals(Kerning.Of("Arial", FontStyle.Bold), Kerning.Of(new Font("Arial", 9, FontStyle.Bold | FontStyle.Underline))),
                  "the table isn't cached per family and style");
            if (Installed("Arial")) Layout();
            int records = 0;
            if (args.Length > 0 && Directory.Exists(args[0])) records = CorelDumps(args[0]);
            if (failures > 0) { Console.WriteLine(failures + " kerning test(s) FAILED"); return 1; }
            Console.WriteLine("kerning_test: all OK (" + kernTablePairs + " 'kern' pairs against GDI" +
                              (records > 0 ? ", " + records + " CorelDRAW text records with " + kernedPairs + " kerned pairs" : "") + ")");
            return 0;
        }

        static bool Installed(string family)
        {
            using (var f = new Font(family, 10)) return string.Equals(f.FontFamily.Name, family, StringComparison.OrdinalIgnoreCase);
        }

        static void WindowsFont(string family)
        {
            var k = Kerning.Of(family, FontStyle.Regular);
            Check(k.HasPairs, family + ": no kerning found");
            if (!k.HasPairs) return;
            Check(k.Pair('A', 'V') < 0, family + ": AV not kerned (" + k.Pair('A', 'V') + ")");
            Check(k.Pair('T', 'o') < 0, family + ": To not kerned (" + k.Pair('T', 'o') + ")");
            Check(k.Pair('o', 'o') == 0, family + ": oo kerned (" + k.Pair('o', 'o') + ")");
            Check(Math.Abs(k.Before(new[] { "A", "V", "A" }, 2) - (k.Pair('A', 'V') + k.Pair('V', 'A'))) < 1e-6,
                  family + ": Before isn't the sum of the pairs");

            // The old 'kern' table alone against GDI, which reads the same table
            byte[] head, cmap, kern;
            var gdi = GdiPairs(family, out head, out cmap, out kern);
            if (kern == null) return;
            var old = Kerning.FromTables(head, cmap, null, kern);
            int upem = old.UnitsPerEm, wrong = 0, compared = 0;
            foreach (var p in gdi)
            {
                if (p.first > 0xFFFF || p.second > 0xFFFF) continue;
                compared++;
                int ours = (int)Math.Round(old.Pair((char)p.first, (char)p.second) * upem);
                if (Math.Abs(ours - p.amount) > 1 && wrong++ < 5)
                    Console.WriteLine("  " + family + " '" + (char)p.first + (char)p.second + "': GDI " + p.amount + ", ours " + ours);
            }
            kernTablePairs += compared;
            Check(compared > 0, family + ": GDI lists no kerning pairs");
            Check(wrong == 0, family + ": " + wrong + " of " + compared + " 'kern' pairs differ from GDI");
        }

        // The editor's text layout kerns: in the caret map (measured as DrawText draws) "V" after "A" starts where "A" alone
        // ends plus the pair, and the stretch's end moves by it too; a frame text the same.
        static void Layout()
        {
            foreach (var kind in new[] { TextKind.Line, TextKind.Frame })
            {
                var a = new TextObject { X = 5, Y = 5, W = 60, H = 20, Font = "Arial", Size = 36, Text = "A", Kind = kind };
                var av = new TextObject { X = 5, Y = 5, W = 60, H = 20, Font = "Arial", Size = 36, Text = "AV", Kind = kind };
                var ma = EditorRender.MapText(a).Lines[0].X;
                var mav = EditorRender.MapText(av).Lines[0].X;
                float em = (float)(36 * 25.4 / 72);
                float pair = em * Kerning.Of("Arial", FontStyle.Regular).Pair('A', 'V');
                float startA = ma[0], startAV = mav[0];
                // left aligned: both start at the box's edge; a centred artistic text moves by half the difference
                float shift = startAV - startA;
                Check(Math.Abs(mav[1] - shift - (ma[1] + pair)) < 0.01f,
                      kind + ": V doesn't start at A's end plus the pair (" + (mav[1] - shift) + " vs " + (ma[1] + pair) + ")");
                Check(pair < -0.1f, kind + ": Arial AV at 36 pt kerns only " + pair + " mm");
            }
        }

        // ---- CorelDRAW's EMF text records ----

        static int CorelDumps(string root)
        {
            int records = 0;
            foreach (var dir in Directory.GetDirectories(root))
            {
                string formats = Path.Combine(dir, "formats.txt");
                if (!File.Exists(formats) || File.ReadAllText(formats).IndexOf("Corel", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string sample = Path.Combine(dir, "sample.txt");   // character or word spacing set: not kerning alone
                if (File.Exists(sample) && File.ReadAllText(sample).IndexOf("spacing", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var emf in Directory.GetFiles(dir, "*CF_ENHMETAFILE.emf")) records += CorelEmf(emf, Path.GetFileName(dir));
            }
            return records;
        }

        static int CorelEmf(string path, string name)
        {
            var d = File.ReadAllBytes(path);
            var fonts = new Dictionary<int, byte[]>();   // object handle -> LOGFONTW
            byte[] current = null;
            int records = 0;
            for (int o = 0; o + 8 <= d.Length;)
            {
                int type = BitConverter.ToInt32(d, o), size = BitConverter.ToInt32(d, o + 4);
                if (size < 8 || o + size > d.Length) break;
                if (type == 82 && size >= 12 + 92)   // EMR_EXTCREATEFONTINDIRECTW
                {
                    var lf = new byte[92];
                    Array.Copy(d, o + 12, lf, 0, 92);
                    fonts[BitConverter.ToInt32(d, o + 8)] = lf;
                }
                else if (type == 37)   // EMR_SELECTOBJECT
                {
                    byte[] lf;
                    if (fonts.TryGetValue(BitConverter.ToInt32(d, o + 8), out lf)) current = lf;
                }
                else if (type == 84 && current != null)   // EMR_EXTTEXTOUTW
                {
                    int emr = o + 36;
                    int n = BitConverter.ToInt32(d, emr + 8), offString = BitConverter.ToInt32(d, emr + 12), offDx = BitConverter.ToInt32(d, emr + 36);
                    if (n >= 2 && offString + 2 * n <= size && offDx + 4 * n <= size)
                    {
                        string text = Encoding.Unicode.GetString(d, o + offString, 2 * n);
                        var dx = new int[n];
                        for (int i = 0; i < n; i++) dx[i] = BitConverter.ToInt32(d, o + offDx + 4 * i);
                        if (CorelRecord(current, text, dx, name)) records++;
                    }
                }
                if (type == 14) break;   // EMR_EOF
                o += size;
            }
            return records;
        }

        // False when the record's font isn't installed here (nothing to compare).
        static bool CorelRecord(byte[] logfont, string text, int[] dx, string name)
        {
            int height = Math.Abs(BitConverter.ToInt32(logfont, 0)), weight = BitConverter.ToInt32(logfont, 16);
            bool italic = logfont[20] != 0;
            string face = Encoding.Unicode.GetString(logfont, 28, 64).Split('\0')[0];
            if (height == 0 || !Installed(face)) return false;
            // GDI+ takes 600 and up as bold, as the editor does for CorelDRAW's weights
            var k = Kerning.Of(face, (weight >= 600 ? FontStyle.Bold : 0) | (italic ? FontStyle.Italic : 0));
            var adv = Advances(logfont, text);
            var off = new List<int>();
            for (int i = 0; i < text.Length - 1; i++) off.Add(dx[i] - adv[i] - (int)Math.Round(k.Pair(text[i], text[i + 1]) * height));
            off.Sort();
            if (off.Count >= 2 && Math.Abs(off[off.Count / 2] + off[(off.Count - 1) / 2]) >= 4) return true;   // tracking (median 2 units or more)
            for (int i = 0; i < text.Length - 1; i++)
            {
                int kern = (int)Math.Round(k.Pair(text[i], text[i + 1]) * height);
                int want = dx[i] - adv[i];
                if (want != 0) kernedPairs++;
                Check(Math.Abs(kern - want) <= 1, name + " '" + text[i] + text[i + 1] + "': CorelDRAW " + want + ", ours " + kern +
                      " (dx " + dx[i] + ", advance " + adv[i] + ")");
            }
            return true;
        }

        static int[] Advances(byte[] logfont, string text)
        {
            var adv = new int[text.Length];
            IntPtr font = CreateFontIndirectW(logfont);
            IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
            IntPtr old = SelectObject(dc, font);
            try
            {
                for (int i = 0; i < text.Length; i++)
                {
                    SIZE s;
                    GetTextExtentPoint32W(dc, text[i].ToString(), 1, out s);
                    adv[i] = s.cx;
                }
            }
            finally
            {
                SelectObject(dc, old);
                DeleteDC(dc);
                DeleteObject(font);
            }
            return adv;
        }

        // ---- GDI's own pairs ('kern' table) at the em size, and the raw tables ----

        static List<KERNINGPAIR> GdiPairs(string family, out byte[] head, out byte[] cmap, out byte[] kern)
        {
            head = cmap = kern = null;
            var list = new List<KERNINGPAIR>();
            using (var probe = new Font(family, 100, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                IntPtr hf = probe.ToHfont();
                IntPtr dc = CreateCompatibleDC(IntPtr.Zero);
                IntPtr old = SelectObject(dc, hf);
                head = Table(dc, "head");
                cmap = Table(dc, "cmap");
                kern = Table(dc, "kern");
                SelectObject(dc, old);
                DeleteObject(hf);
                if (head == null) { DeleteDC(dc); return list; }
                int upem = head[18] << 8 | head[19];
                // the same face at an em of exactly unitsPerEm pixels: the pairs come back in font units
                var lf = new LOGFONTW { lfHeight = -upem, lfWeight = 400, lfCharSet = 1, lfFaceName = family };
                IntPtr big = CreateFontIndirectW(ref lf);
                old = SelectObject(dc, big);
                int n = GetKerningPairsW(dc, 0, null);
                if (n > 0)
                {
                    var pairs = new KERNINGPAIR[n];
                    n = GetKerningPairsW(dc, n, pairs);
                    list.AddRange(pairs.Take(n));
                }
                SelectObject(dc, old);
                DeleteObject(big);
                DeleteDC(dc);
            }
            return list;
        }

        static byte[] Table(IntPtr dc, string tag)
        {
            uint key = (uint)(tag[0] | tag[1] << 8 | tag[2] << 16 | tag[3] << 24);
            uint size = GetFontData(dc, key, 0, null, 0);
            if (size == 0xFFFFFFFF || size == 0) return null;
            var data = new byte[size];
            return GetFontData(dc, key, 0, data, size) == size ? data : null;
        }

        [StructLayout(LayoutKind.Sequential)] struct KERNINGPAIR { public ushort first, second; public int amount; }
        [StructLayout(LayoutKind.Sequential)] struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct LOGFONTW
        {
            public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
            public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
        }

        [DllImport("gdi32.dll")] static extern uint GetFontData(IntPtr hdc, uint table, uint offset, byte[] buffer, uint length);
        [DllImport("gdi32.dll")] static extern int GetKerningPairsW(IntPtr hdc, int n, [Out] KERNINGPAIR[] pairs);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontIndirectW(ref LOGFONTW lf);
        [DllImport("gdi32.dll", EntryPoint = "CreateFontIndirectW")] static extern IntPtr CreateFontIndirectW(byte[] lf);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr hdc, string s, int n, out SIZE size);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    }
}
