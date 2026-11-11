// CorelDRAW's EMF on the clipboard drawn as CorelDRAW shows it, with the help of its CMX (the same objects in its own
// format, Import/Corel/Cmx.cs). Two things its EMF gets wrong, both fixed only where the CMX says how:
// - Outlines that scale with the object: the EMF pen has the width as set before the object was resized (a logo made
//   small: a 1.5 mm outline where CorelDRAW draws 0.24 mm). The CMX pen carries that resize as a matrix; the EMF pen
//   whose width matches the CMX pen's gets its width (and dashes) scaled by it.
// - Small text: written in its reference device's pixels (about 0.23 mm each), so the size and the letter steps come
//   out rounded to whole pixels ("FUN ELEC TRONIC", 0.92 mm instead of 1.005 mm). Such a text record is written again
//   at 0.001 mm with the size and letter steps of the CMX's laid-out run of the same text; its start stays the EMF's.
// The same bytes when there is no CMX, it can't be read or nothing matches.
// CMX parts read here (CorelDRAW 2024): chunk "cont" at 64 the unit in metres (1e-7); chunk "rpen" = uint16 count, one
// tag list per pen, tag 1 = int32 width, uint16 aspect, int32 angle, uint16 matrix type (2: six doubles follow).
// Page instructions: 72 BeginTextGroup tag 2 = uint16 matrix type + six doubles (the text's own matrix); 85 font
// tag 2 = uint16 font, int32 size (em); 65 DrawChars tag 1 = uint16 count, UTF-16 characters, int32 x, y each.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QlLabels
{
    static class CorelPicture
    {
        const double MmPerUnit = 1e-4;   // CMX units (0.1 micrometre)

        sealed class Run
        {
            public string Text;
            public int[] X;          // per character, CMX units
            public double SizeMm;    // em, after the text's matrix
            public double Scale = 1;
            public bool Used;
        }

        public static byte[] Fix(byte[] emf, byte[] cmx)
        {
            if (emf == null || cmx == null) return emf;
            try
            {
                List<double[]> outlines;   // the CMX pens: width mm, scale
                List<Run> runs;
                if (!ReadCmx(cmx, out outlines, out runs)) return emf;
                return Rewrite(emf, outlines, runs) ?? emf;
            }
            // damaged: drawn as it is
            catch (InvalidDataException) { return emf; }
            catch (ArgumentException) { return emf; }
            catch (IndexOutOfRangeException) { return emf; }
            catch (OverflowException) { return emf; }
        }

        // ---------- CMX ----------

        static bool ReadCmx(byte[] d, out List<double[]> outlines, out List<Run> runs)
        {
            outlines = new List<double[]>();
            runs = new List<Run>();
            if (d.Length < 12 || Ascii(d, 0, 4) != "RIFF" || Ascii(d, 8, 4) != "CMX1") return false;
            int end = (int)Math.Min(d.Length, 8L + U32(d, 4));
            bool unitOk = false;
            for (int at = 12; at + 8 <= end; )
            {
                string id = Ascii(d, at, 4);
                long n = U32(d, at + 4);
                if (n < 0 || at + 8 + n > end) break;
                int body = at + 8, bodyEnd = at + 8 + (int)n;
                if (id == "cont" && n >= 72)
                {
                    double m = BitConverter.ToDouble(d, body + 64);
                    unitOk = Math.Abs(m / 1e-7 - 1) < 1e-6;
                }
                if (id == "rpen") Pens(d, body, bodyEnd, outlines);
                if (id == "page") Page(d, body, bodyEnd, runs);
                at += 8 + (int)n + (int)(n & 1);
            }
            return unitOk;
        }

        static void Pens(byte[] d, int at, int end, List<double[]> outlines)
        {
            int count = U16(d, at);
            at += 2;
            for (int i = 0; i < count && at < end && outlines.Count < 10000; i++)
            {
                int start = at;
                var tags = Tags(d, at, end, out at);
                if (at <= start) break;
                Tuple<int, int> g;
                if (!tags.TryGetValue(1, out g) || g.Item2 - g.Item1 < 12) { outlines.Add(null); continue; }
                long w = I32(d, g.Item1);
                double scale = PenScale(d, g.Item1 + 10, g.Item2);
                outlines.Add(w > 0 && scale > 0 ? new[] { w * MmPerUnit, scale } : null);
            }
        }

        // The uniform scale of a matrix (type 1: none; type 2: six doubles); 0: turned, skewed or damaged.
        static double MatrixScale(byte[] d, int at, int end)
        {
            int type = U16(d, at);
            if (type == 1) return 1;
            if (type != 2 || at + 2 + 48 > end) return 0;
            double a = BitConverter.ToDouble(d, at + 2), b = BitConverter.ToDouble(d, at + 10);
            double c = BitConverter.ToDouble(d, at + 18), e = BitConverter.ToDouble(d, at + 26);
            if (!(a > 0) || Math.Abs(b) > 1e-6 * a || Math.Abs(c) > 1e-6 * a || Math.Abs(e - a) > 1e-4 * a) return 0;
            return a;
        }

        // A pen only needs the length scale: a turned or mirrored matrix with the same scale in every direction
        // (an object rotated by 90 or 270 degrees, say) scales its outline like an upright one. 0: skewed or damaged.
        static double PenScale(byte[] d, int at, int end)
        {
            int type = U16(d, at);
            if (type == 1) return 1;
            if (type != 2 || at + 2 + 48 > end) return 0;
            double a = BitConverter.ToDouble(d, at + 2), b = BitConverter.ToDouble(d, at + 10);
            double c = BitConverter.ToDouble(d, at + 18), e = BitConverter.ToDouble(d, at + 26);
            double s = Math.Sqrt(a * a + b * b);
            if (!(s > 0) || double.IsInfinity(s)) return 0;
            bool turned = Math.Abs(a - e) <= 1e-4 * s && Math.Abs(b + c) <= 1e-4 * s;
            bool mirrored = Math.Abs(a + e) <= 1e-4 * s && Math.Abs(b - c) <= 1e-4 * s;
            return turned || mirrored ? s : 0;
        }

        static void Page(byte[] d, int at, int end, List<Run> runs)
        {
            double textScale = 1, sizeMm = 0;
            for (int count = 0; at + 4 <= end && count < 200000; count++)
            {
                int size = U16(d, at), code = (short)U16(d, at + 2), head = 4;
                if (size == 0xFFFF)
                {
                    if (at + 8 > end) break;
                    long big = U32(d, at + 2);
                    if (big > int.MaxValue) break;
                    size = (int)big; code = (short)U16(d, at + 6); head = 8;
                }
                if (size < head || at + size > end) break;
                int after;
                var tags = Tags(d, at + head, at + size, out after);
                Tuple<int, int> g;
                switch (code)
                {
                    case 70: textScale = 1; sizeMm = 0; break;                                   // BeginTextObject
                    case 72: if (tags.TryGetValue(2, out g)) textScale = MatrixScale(d, g.Item1, g.Item2); break;
                    case 85:
                        if (tags.TryGetValue(2, out g) && g.Item2 - g.Item1 >= 6)
                        {
                            long em = I32(d, g.Item1 + 2);
                            sizeMm = em > 0 && em < 50000000 ? em * MmPerUnit : 0;
                        }
                        break;
                    case 65:                                                                       // DrawChars
                        if (tags.TryGetValue(1, out g) && textScale > 0 && sizeMm > 0)
                        {
                            int n = U16(d, g.Item1);
                            if (n > 0 && n <= 10000 && g.Item1 + 2 + 10L * n <= g.Item2)
                            {
                                var run = new Run { Text = Encoding.Unicode.GetString(d, g.Item1 + 2, 2 * n), X = new int[n], SizeMm = sizeMm * textScale, Scale = textScale };
                                int p = g.Item1 + 2 + 2 * n;
                                bool level = true;
                                for (int i = 0; i < n; i++)
                                {
                                    run.X[i] = (int)I32(d, p + 8 * i);
                                    if (I32(d, p + 8 * i + 4) != I32(d, p + 4)) level = false;
                                }
                                if (level && runs.Count < 100000) runs.Add(run);
                            }
                        }
                        break;
                }
                at += size;
            }
        }

        // ---------- EMF ----------

        sealed class Dc
        {
            public int MapMode = 1, TextAlign;
            public int WinOrgX, WinOrgY, WinExtX = 1, WinExtY = 1, VpOrgX, VpOrgY, VpExtX = 1, VpExtY = 1;
            public bool World;   // a world transform other than identity
            public Dc Copy() { return (Dc)MemberwiseClone(); }
        }

        static byte[] Rewrite(byte[] emf, List<double[]> outlines, List<Run> runs)
        {
            EmfPicture.Check(emf);
            int total = Int(emf, 48);
            int devX = Int(emf, 72), devY = Int(emf, 76), mmX = Int(emf, 80), mmY = Int(emf, 84);
            if (devX <= 0 || devY <= 0 || mmX <= 0 || mmY <= 0) return null;
            double pxPerMmX = devX / (double)mmX, pxPerMmY = devY / (double)mmY;
            int handle = Int(emf, 56) & 0xFFFF;   // nHandles: the next free object index
            if (handle < 1 || handle >= 0xFFFF) return null;
            var fonts = new Dictionary<int, int>();   // object index -> record position
            int font = -1, added = 0;
            bool changed = false;
            var s = new Dc();
            var stack = new List<Dc>();
            var o = new MemoryStream(emf.Length + 1024);
            for (int pos = 0; pos + 8 <= total; )
            {
                int type = Int(emf, pos), size = Int(emf, pos + 4), a = pos + 8;
                byte[] rec = null;
                switch (type)
                {
                    case 9: s.WinExtX = Int(emf, a); s.WinExtY = Int(emf, a + 4); break;
                    case 10: s.WinOrgX = Int(emf, a); s.WinOrgY = Int(emf, a + 4); break;
                    case 11: s.VpExtX = Int(emf, a); s.VpExtY = Int(emf, a + 4); break;
                    case 12: s.VpOrgX = Int(emf, a); s.VpOrgY = Int(emf, a + 4); break;
                    case 17: s.MapMode = Int(emf, a); break;
                    case 22: s.TextAlign = Int(emf, a); break;
                    case 33: if (stack.Count < 10000) stack.Add(s.Copy()); break;
                    case 34:
                        {
                            int n = Int(emf, a), keep = n < 0 ? stack.Count + n : n - 1;
                            if (keep >= 0 && keep < stack.Count) { s = stack[keep]; stack.RemoveRange(keep, stack.Count - keep); }
                            break;
                        }
                    case 35: s.World = !Identity(emf, a); break;
                    case 36:
                        {
                            int mode = Int(emf, a + 24);
                            if (mode == 1) s.World = false;
                            else if (!Identity(emf, a)) s.World = true;
                            break;
                        }
                    case 37:
                        {
                            int h = Int(emf, a);
                            if (fonts.ContainsKey(h)) font = h;
                            else if ((h & 0x80000000) != 0 && (h & 0x7FFFFFFF) >= 10 && (h & 0x7FFFFFFF) <= 17) font = -1;
                            break;
                        }
                    case 40: fonts.Remove(Int(emf, a)); break;
                    case 82: if (size >= 12 + 92) fonts[Int(emf, a)] = pos; break;
                    case 95: rec = ScaledPen(emf, pos, size, s, pxPerMmX, outlines); break;
                    case 84:
                        if (s.MapMode == 1 && !s.World && (s.TextAlign & 1) == 0 && fonts.ContainsKey(font) && font != -1)
                        {
                            var fixedText = SharperText(emf, pos, size, s, fonts[font], handle, pxPerMmX, pxPerMmY, runs);
                            if (fixedText != null)
                            {
                                o.Write(fixedText.Item1, 0, fixedText.Item1.Length);
                                added += fixedText.Item2 - 1;
                                changed = true;
                                pos += size;
                                continue;
                            }
                        }
                        break;
                }
                if (type == 14) { o.Write(emf, pos, total - pos); break; }   // EMR_EOF (and anything after it)
                if (rec != null) { o.Write(rec, 0, rec.Length); changed = true; }
                else o.Write(emf, pos, size);
                pos += size;
            }
            if (!changed) return null;
            var b = o.ToArray();
            BitConverter.GetBytes(b.Length).CopyTo(b, 48);                     // nBytes
            BitConverter.GetBytes(Int(emf, 52) + added).CopyTo(b, 52);        // nRecords
            BitConverter.GetBytes((ushort)(handle + 1)).CopyTo(b, 56);        // nHandles: one more for the new font
            return b;
        }

        static bool Identity(byte[] d, int at)
        {
            float[] m = new float[6];
            for (int i = 0; i < 6; i++) m[i] = BitConverter.ToSingle(d, at + 4 * i);
            return m[0] == 1 && m[1] == 0 && m[2] == 0 && m[3] == 1 && m[4] == 0 && m[5] == 0;
        }

        // mm per logical unit across (x) in the mapping; 0: not known.
        static double UnitMm(Dc s, double pxPerMm)
        {
            switch (s.MapMode)
            {
                case 1: return 1 / pxPerMm;
                case 2: return 0.1;
                case 3: return 0.01;
                case 4: return 0.254;
                case 5: return 0.0254;
                case 6: return 25.4 / 1440;
                case 7:
                case 8: return s.WinExtX != 0 ? Math.Abs((double)s.VpExtX / s.WinExtX) / pxPerMm : 0;
                default: return 0;
            }
        }

        // EMR_EXTCREATEPEN: ihPen, offBmi, cbBmi, offBits, cbBits, style at 28, width at 32, brush style, colour, hatch,
        // number of dashes at 52, dashes from 56.
        static byte[] ScaledPen(byte[] emf, int pos, int size, Dc s, double pxPerMm, List<double[]> outlines)
        {
            if (size < 56 || s.World) return null;
            int style = Int(emf, pos + 28), w = Int(emf, pos + 32);
            if ((style & 0x10000) == 0 || w <= 0) return null;   // cosmetic: always one pixel
            double unit = UnitMm(s, pxPerMm);
            if (!(unit > 0)) return null;
            double mm = w * unit, scale = 0;
            foreach (var p in outlines)
            {
                if (p == null || Math.Abs(p[0] - mm) > 0.6 * unit + 1e-6) continue;
                if (scale == 0) scale = p[1];
                else if (Math.Abs(scale - p[1]) > 1e-3 * scale) return null;   // two outlines of this width, scaled differently
            }
            if (scale == 0 || Math.Abs(scale - 1) < 1e-3) return null;
            var rec = new byte[size];
            Array.Copy(emf, pos, rec, 0, size);
            BitConverter.GetBytes(Math.Max(1, (int)Math.Round(w * scale))).CopyTo(rec, 32);
            int dashes = Int(emf, pos + 52);
            if (dashes > 0 && dashes <= 16 && 56 + 4 * dashes <= size)
                for (int i = 0; i < dashes; i++)
                    BitConverter.GetBytes(Math.Max(1, (int)Math.Round(Int(emf, pos + 56 + 4 * i) * scale))).CopyTo(rec, 56 + 4 * i);
            return rec;
        }

        // A text record in device pixels (MM_TEXT) again at 0.001 mm with the size and steps of its CMX run: the records
        // (and how many) to write in its place; null: no run of this text, or a record this doesn't handle.
        static Tuple<byte[], int> SharperText(byte[] emf, int pos, int size, Dc s, int fontPos, int handle, double pxPerMmX, double pxPerMmY, List<Run> runs)
        {
            // EMR_EXTTEXTOUTW: bounds, graphics mode, scales, then EMRTEXT at 36: reference x, y, nChars, offString,
            // options, rectangle, offDx
            int n = Int(emf, pos + 44), offString = Int(emf, pos + 48), options = Int(emf, pos + 52), offDx = Int(emf, pos + 72);
            if (size < 76 || n <= 0 || n > 10000 || (options & 0x2006) != 0) return null;   // PDY, clipped, opaque: not here
            if (offString < 76 || offString + 2L * n > size || offDx < 76 || offDx + 4L * n > size) return null;
            string text = Encoding.Unicode.GetString(emf, pos + offString, 2 * n);
            Run run = null;
            foreach (var r in runs) if (!r.Used && r.Text == text) { run = r; break; }
            if (run == null) return null;
            int fontSize = Int(emf, fontPos + 4);
            int oldHeight = Int(emf, fontPos + 12);
            if (fontSize < 12 + 92 || oldHeight == 0) return null;

            // the new mapping: 1 unit = 0.001 mm (window 1 m, viewport the device pixels of 1 m)
            const int Window = 1000000;
            int vpX = (int)Math.Round(1000 * pxPerMmX), vpY = (int)Math.Round(1000 * pxPerMmY);
            if (vpX <= 0 || vpY <= 0) return null;
            double kx = vpX / (double)Window, ky = vpY / (double)Window;   // device pixels per unit
            double unitMm = kx / pxPerMmX;   // about 0.001
            int x = (int)Math.Round((Int(emf, pos + 36) - s.WinOrgX) / kx), y = (int)Math.Round((Int(emf, pos + 40) - s.WinOrgY) / ky);
            int height = -(int)Math.Round(run.SizeMm / unitMm);
            if (height == 0) return null;
            run.Used = true;

            var o = new MemoryStream();
            int records = 0;
            Action<int, int[]> put = (type, values) =>
            {
                Write(o, type); Write(o, 8 + 4 * values.Length);
                foreach (var v in values) Write(o, v);
                records++;
            };
            put(33, new int[0]);                                   // SAVEDC
            put(17, new[] { 8 });                                  // SETMAPMODE ANISOTROPIC
            put(10, new[] { 0, 0 });                               // SETWINDOWORGEX
            put(9, new[] { Window, Window });                      // SETWINDOWEXTEX
            put(12, new[] { s.VpOrgX, s.VpOrgY });                 // SETVIEWPORTORGEX
            put(11, new[] { vpX, vpY });                           // SETVIEWPORTEXTEX
            var f = new byte[fontSize];                            // the font at the CMX's size
            Array.Copy(emf, fontPos, f, 0, fontSize);
            BitConverter.GetBytes(handle).CopyTo(f, 8);
            BitConverter.GetBytes(height).CopyTo(f, 12);
            int oldWidth = Int(emf, fontPos + 16);
            if (oldWidth != 0) BitConverter.GetBytes((int)Math.Round(oldWidth * (double)height / oldHeight)).CopyTo(f, 16);
            o.Write(f, 0, f.Length); records++;
            put(37, new[] { handle });                             // SELECTOBJECT
            var t = new byte[size];
            Array.Copy(emf, pos, t, 0, size);
            BitConverter.GetBytes(x).CopyTo(t, 36);
            BitConverter.GetBytes(y).CopyTo(t, 40);
            for (int i = 0; i < n; i++)
            {
                double step = i + 1 < n ? (run.X[i + 1] - run.X[i]) * MmPerUnit * run.Scale / unitMm
                                        : Int(emf, pos + offDx + 4 * i) / kx;   // the last: as written (no next letter)
                BitConverter.GetBytes((int)Math.Round(step)).CopyTo(t, offDx + 4 * i);
            }
            o.Write(t, 0, t.Length); records++;
            put(34, new[] { -1 });                                 // RESTOREDC
            put(40, new[] { handle });                             // DELETEOBJECT
            return Tuple.Create(o.ToArray(), records);
        }

        static void Write(Stream o, int v) { o.Write(BitConverter.GetBytes(v), 0, 4); }

        // ---------- bytes ----------

        static Dictionary<int, Tuple<int, int>> Tags(byte[] d, int at, int end, out int after)
        {
            var tags = new Dictionary<int, Tuple<int, int>>();
            after = at;
            while (at < end)
            {
                int tag = d[at];
                if (tag == 0xFF) { after = at + 1; return tags; }
                if (at + 3 > end) break;
                int n = U16(d, at + 1);
                if (n < 3 || at + n > end) break;
                if (!tags.ContainsKey(tag)) tags[tag] = Tuple.Create(at + 3, at + n);
                at += n;
            }
            after = end;
            return tags;
        }

        static string Ascii(byte[] d, int at, int n) { return at >= 0 && at + n <= d.Length ? Encoding.ASCII.GetString(d, at, n) : ""; }
        static int U16(byte[] d, int at) { return at >= 0 && at + 2 <= d.Length ? BitConverter.ToUInt16(d, at) : 0; }
        static long U32(byte[] d, int at) { return at >= 0 && at + 4 <= d.Length ? BitConverter.ToUInt32(d, at) : 0; }
        static long I32(byte[] d, int at) { return at >= 0 && at + 4 <= d.Length ? BitConverter.ToInt32(d, at) : 0; }
        static int Int(byte[] d, int at) { return at >= 0 && at <= d.Length - 4 ? BitConverter.ToInt32(d, at) : 0; }
    }
}
