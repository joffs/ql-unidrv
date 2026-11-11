// Barcode encoders for the label editor: Code 128 (sets A, B and C, FNC1 for GS1-128), EAN-13 and QR codes (byte mode, versions 1-40,
// error correction L/M/Q/H). Pure code, no drawing: each returns modules (true = dark); EditorRender draws them
// with whole printer dots per module. QR follows ISO/IEC 18004; checked by labeltool\test\editor_test.cs against the
// standard's examples.
//
// The QR code part (class QrCode) is ported from Project Nayuki's QR Code generator library:
// Copyright (c) Project Nayuki. (MIT License) https://www.nayuki.io/page/qr-code-generator-library
// Its full licence notice is in THIRD-PARTY-NOTICES.txt (at the top level of the public repository and the
// release zip).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QlLabels
{
    static class Code128
    {
        // Bar/space widths of values 0-106 (106 = stop, 7 elements).
        static readonly string[] Patterns =
        {
            "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
            "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
            "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
            "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
            "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
            "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
            "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
            "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
            "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
            "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
            "114131", "311141", "411131", "211412", "211214", "211232", "2331112",
        };
        const int Shift = 98, CodeC = 99, CodeB = 100, CodeA = 101, Fnc1Value = 102, StartA = 103, StartB = 104, StartC = 105, Stop = 106;

        // FNC1 in the text given to Values/Modules/Check (a private-use character, never typed): at the start it marks
        // GS1-128, later it separates a variable-length GS1 element from the next (Gs1 makes such texts).
        public const char Fnc1 = '';

        public static string PatternOf(int value) { return Patterns[value]; }

        // null if the text can be encoded (ASCII 0-127 and FNC1), else the reason.
        public static string Check(string text)
        {
            if (string.IsNullOrEmpty(text)) return Lang.T("Enter the barcode's content.");
            foreach (char c in text)
                if (c > 127 && c != Fnc1) return Lang.T("Code 128 takes letters, digits and ASCII symbols only (no umlauts).");
            return null;
        }

        // The sets: 0 = A (ASCII 0-95: control characters, upper case), 1 = B (ASCII 32-127: lower case), 2 = C (digit
        // pairs). FNC1 is in all three.
        static bool InA(char c) { return c < 96 || c == Fnc1; }
        static bool InB(char c) { return (c >= 32 && c < 128) || c == Fnc1; }
        static bool Digit(char c) { return c >= '0' && c <= '9'; }
        static int ValueA(char c) { return c == Fnc1 ? Fnc1Value : c < 32 ? c + 64 : c - 32; }
        static int ValueB(char c) { return c == Fnc1 ? Fnc1Value : c - 32; }

        // Symbol values incl. start, checksum and stop. The shortest encoding (ISO/IEC 15417 allows any; fewer symbol
        // characters = a narrower barcode): cost[i, s] = symbol characters for text[i..] when set s is active at i,
        // with Code A/B/C to switch, Shift for one character of the other of A/B, and set C for digit pairs and FNC1.
        // Equal costs keep the current set, then prefer B, C, A (so plain text stays in set B as before).
        public static List<int> Values(string text)
        {
            int n = text.Length;
            var cost = new int[n + 1, 3];
            var step = new int[n + 1, 3];   // the choice at (i, s): 0-2 = encode in that set (switching if it isn't s), 3 = Shift
            const int Inf = 1 << 20;
            int[] order = { 1, 2, 0 };
            for (int i = n - 1; i >= 0; i--)
                for (int s = 0; s < 3; s++)
                {
                    int best = Inf, choice = -1;
                    // the current set first, then B, C, A
                    foreach (int t in new[] { s }.Concat(order.Where(x => x != s)))
                    {
                        int c = Direct(text, i, t, cost);
                        if (c >= Inf) continue;
                        c += t == s ? 0 : 1;
                        if (c < best) { best = c; choice = t; }
                    }
                    // Shift: one character from the other of A/B, staying in s
                    if (s != 2 && i < n && (s == 0 ? InB(text[i]) && !InA(text[i]) : InA(text[i]) && !InB(text[i])) && 2 + cost[i + 1, s] < best)
                    { best = 2 + cost[i + 1, s]; choice = 3; }
                    cost[i, s] = best; step[i, s] = choice;
                }
            var v = new List<int>();
            // The start code: the set with the fewest symbol characters after it (B, C, A on a tie)
            int set = 1;
            if (n > 0) foreach (int t in order) if (cost[0, t] < cost[0, set]) set = t;
            v.Add(set == 0 ? StartA : set == 1 ? StartB : StartC);
            for (int i = 0; i < n;)
            {
                int ch = step[i, set];
                if (ch == 3)
                {
                    v.Add(Shift);
                    v.Add(set == 0 ? ValueB(text[i]) : ValueA(text[i]));
                    i++;
                    continue;
                }
                if (ch != set) { v.Add(ch == 0 ? CodeA : ch == 1 ? CodeB : CodeC); set = ch; }
                if (set == 2)
                {
                    if (text[i] == Fnc1) { v.Add(Fnc1Value); i++; }
                    else { v.Add((text[i] - '0') * 10 + text[i + 1] - '0'); i += 2; }
                }
                else { v.Add(set == 0 ? ValueA(text[i]) : ValueB(text[i])); i++; }
            }
            int sum = v[0];
            for (int k = 1; k < v.Count; k++) sum += v[k] * k;
            v.Add(sum % 103);
            v.Add(Stop);
            return v;
        }

        // Symbol characters to encode text[i] (or a digit pair) in set t plus the rest from there, when t is active.
        static int Direct(string text, int i, int t, int[,] cost)
        {
            const int Inf = 1 << 20;
            char c = text[i];
            if (t == 2)
            {
                if (c == Fnc1) return 1 + cost[i + 1, 2];
                if (i + 1 < text.Length && Digit(c) && Digit(text[i + 1])) return 1 + cost[i + 2, 2];
                return Inf;
            }
            if (t == 0 ? InA(c) : InB(c)) return 1 + cost[i + 1, t];
            return Inf;
        }

        // Modules (true = bar), without quiet zones (ISO/IEC 15417: at least 10 modules on each side; EditorRender
        // leaves 10).
        public static bool[] Modules(string text)
        {
            var m = new List<bool>();
            foreach (int value in Values(text))
            {
                bool bar = true;
                foreach (char w in Patterns[value]) { for (int k = 0; k < w - '0'; k++) m.Add(bar); bar = !bar; }
            }
            return m.ToArray();
        }

        // Control characters typed as tokens (the barcode field's notation, like its {date} fields): {tab} {cr} {lf}
        // {gs} {rs} {eot} {fnc1} and {xNN} (hex 00-7F), any case. Runs before Fields.Expand: "{{" and "}}" and unknown
        // tokens ({date:...}) are left for it, and {x7B}/{x7D} become "{{"/"}}" so that they end up as one brace.
        public static string Expand(string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text ?? "";
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '{' && i + 1 < text.Length && text[i + 1] == '{') { sb.Append("{{"); i++; continue; }
                int close = c == '{' ? text.IndexOf('}', i + 1) : -1;
                string token = close > i ? Token(text.Substring(i + 1, close - i - 1)) : null;
                if (token == null) { sb.Append(c); continue; }
                sb.Append(token);
                i = close;
            }
            return sb.ToString();
        }

        static string Token(string name)
        {
            switch (name.ToLowerInvariant())
            {
                case "tab": return "\t";
                case "cr": return "\r";
                case "lf": return "\n";
                case "gs": return "\u001d";
                case "rs": return "\u001e";
                case "eot": return "\u0004";
                case "fnc1": return Fnc1.ToString();
            }
            int hex;
            if (name.Length == 3 && (name[0] == 'x' || name[0] == 'X') &&
                int.TryParse(name.Substring(1), System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out hex) && hex < 128)
                return hex == '{' ? "{{" : hex == '}' ? "}}" : ((char)hex).ToString();
            return null;
        }

        // The text under the bars: without control characters and FNC1 (ISO/IEC 15417: they have no printed form).
        public static string HumanReadable(string text)
        {
            return new string((text ?? "").Where(c => c >= 32 && c != 127 && c != Fnc1).ToArray());
        }

        // GS1-128 from "(01)09501101530003(17)250101(10)AB-123": FNC1, then each element as AI + data; after an element
        // of variable length another FNC1 separates it from the next (GS1 General Specifications 5.4.4, 7.8.4: the
        // elements of predefined length are those whose AI starts with 00-04, 11-20, 31-36 or 41). Returns the text
        // for Values/Modules, or null with the reason.
        public static string Gs1(string input, out string error)
        {
            error = null;
            var sb = new StringBuilder();
            sb.Append(Fnc1);
            int i = 0;
            bool needSeparator = false;
            if (string.IsNullOrEmpty(input) || input[0] != '(') { error = Lang.T("GS1-128: start with an application identifier in brackets, e.g. (01)."); return null; }
            while (i < input.Length)
            {
                int close = input.IndexOf(')', i);
                string ai = close > i ? input.Substring(i + 1, close - i - 1) : "";
                if (input[i] != '(' || ai.Length < 2 || ai.Length > 4 || !ai.All(Digit))
                { error = Lang.F("GS1-128: \"{0}\" is not an application identifier (2-4 digits in brackets).", close > i ? input.Substring(i, close - i + 1) : input.Substring(i)); return null; }
                int next = input.IndexOf('(', close + 1);
                string data = next < 0 ? input.Substring(close + 1) : input.Substring(close + 1, next - close - 1);
                if (data.Length == 0) { error = Lang.F("GS1-128: ({0}) has no data.", ai); return null; }
                foreach (char c in data)
                    if (c < 33 || c > 126) { error = Lang.F("GS1-128: ({0}) may only contain letters, digits and symbols, no spaces.", ai); return null; }
                int fixedLength = PredefinedLength(ai);   // AI + data
                if (fixedLength > 0 && ai.Length + data.Length != fixedLength)
                { error = Lang.F("GS1-128: ({0}) needs {1} digits.", ai, fixedLength - ai.Length); return null; }
                if (fixedLength > 0 && !data.All(Digit)) { error = Lang.F("GS1-128: ({0}) takes digits only.", ai); return null; }
                if ((ai == "00" || ai == "01" || ai == "02") && !Mod10Ok(data))
                { error = Lang.F("GS1-128: the check digit of ({0}) is wrong.", ai); return null; }
                if (needSeparator) sb.Append(Fnc1);
                sb.Append(ai).Append(data);
                needSeparator = fixedLength == 0;
                i = next < 0 ? input.Length : next;
            }
            return sb.ToString();
        }

        // AI + data length of the elements of predefined length (GS1 General Specifications, figure 5.10.1-1), else 0.
        static int PredefinedLength(string ai)
        {
            switch (ai.Substring(0, 2))
            {
                case "00": return 20;
                case "01": case "02": case "03": return 16;
                case "04": return 18;
                case "11": case "12": case "13": case "14": case "15": case "16": case "17": case "18": case "19": return 8;
                case "20": return 4;
                case "31": case "32": case "33": case "34": case "35": case "36": return 10;
                case "41": return 16;
                default: return 0;
            }
        }

        // GS1 check digit (mod 10, weights 3/1 from the right) of a GTIN/SSCC including its last digit.
        static bool Mod10Ok(string digits)
        {
            int sum = 0;
            for (int k = digits.Length - 2, w = 3; k >= 0; k--, w = 4 - w) sum += (digits[k] - '0') * w;
            return (10 - sum % 10) % 10 == digits[digits.Length - 1] - '0';
        }
    }

    static class Ean13
    {
        static readonly string[] L = { "0001101", "0011001", "0010011", "0111101", "0100011", "0110001", "0101111", "0111011", "0110111", "0001011" };
        static readonly string[] Parity = { "LLLLLL", "LLGLGG", "LLGGLG", "LLGGGL", "LGLLGG", "LGGLLG", "LGGGLL", "LGLGLG", "LGLGGL", "LGGLGL" };

        public static int CheckDigit(string twelve)
        {
            int sum = 0;
            for (int i = 0; i < 12; i++) sum += (twelve[i] - '0') * (i % 2 == 0 ? 1 : 3);
            return (10 - sum % 10) % 10;
        }

        // 12 digits (check digit added) or 13 (check digit checked). null = OK, else the reason.
        public static string Check(string text)
        {
            if (text == null || (text.Length != 12 && text.Length != 13) || text.Any(ch => ch < '0' || ch > '9'))
                return Lang.T("EAN-13 needs 12 digits (the check digit is added) or 13 digits.");
            if (text.Length == 13 && CheckDigit(text) != text[12] - '0')
                return Lang.F("The check digit is wrong: the 13th digit must be {0}.", CheckDigit(text));
            return null;
        }

        public static string Full(string text) { return text.Length == 13 ? text : text + CheckDigit(text); }

        // 95 modules: guard, 6 left digits, centre guard, 6 right digits, guard. guards: modules of the guard bars.
        public static bool[] Modules(string text, out bool[] guards)
        {
            string d = Full(text);
            var sb = new StringBuilder("101");
            string parity = Parity[d[0] - '0'];
            for (int i = 1; i <= 6; i++)
            {
                string l = L[d[i] - '0'];
                sb.Append(parity[i - 1] == 'L' ? l : new string(Invert(l).Reverse().ToArray()));
            }
            sb.Append("01010");
            for (int i = 7; i <= 12; i++) sb.Append(Invert(L[d[i] - '0']));
            sb.Append("101");
            guards = new bool[95];
            foreach (int g in new[] { 0, 1, 2, 45, 46, 47, 48, 49, 92, 93, 94 }) guards[g] = true;
            return sb.ToString().Select(ch => ch == '1').ToArray();
        }

        static string Invert(string s) { return new string(s.Select(ch => ch == '1' ? '0' : '1').ToArray()); }
    }

    // QR code, byte mode (UTF-8).
    static class QrCode
    {
        // Index [level][version]; levels L, M, Q, H.
        static readonly int[][] EccPerBlock =
        {
            new[] { -1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28, 28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            new[] { -1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26, 26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28 },
            new[] { -1, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30, 28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
            new[] { -1, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28, 30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30 },
        };
        static readonly int[][] Blocks =
        {
            new[] { -1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8, 8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25 },
            new[] { -1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16, 17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49 },
            new[] { -1, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20, 23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68 },
            new[] { -1, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25, 25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81 },
        };
        static readonly int[] FormatLevelBits = { 1, 0, 3, 2 };   // L, M, Q, H

        public static int RawDataModules(int ver)
        {
            int r = (16 * ver + 128) * ver + 64;
            if (ver >= 2)
            {
                int align = ver / 7 + 2;
                r -= (25 * align - 10) * align - 55;
                if (ver >= 7) r -= 36;
            }
            return r;
        }

        public static int DataCodewords(int ver, int level) { return RawDataModules(ver) / 8 - EccPerBlock[level][ver] * Blocks[level][ver]; }

        // Bytes that fit (byte mode).
        public static int Capacity(int ver, int level) { return (DataCodewords(ver, level) * 8 - 4 - (ver < 10 ? 8 : 16)) / 8; }

        public static int VersionFor(int bytes, int level)
        {
            for (int v = 1; v <= 40; v++) if (bytes <= Capacity(v, level)) return v;
            return -1;
        }

        public static string Check(string text, int level)
        {
            if (string.IsNullOrEmpty(text)) return Lang.T("Enter the QR code's content.");
            int n = Encoding.UTF8.GetByteCount(text);
            if (VersionFor(n, level) < 0) return Lang.F("Too long for a QR code: {0} bytes, at most {1}.", n, Capacity(40, level));
            return null;
        }

        // ---------- Reed-Solomon over GF(256), polynomial 0x11D ----------
        static int Mul(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return z;
        }

        static byte[] Divisor(int degree)
        {
            var r = new byte[degree];
            r[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < r.Length; j++)
                {
                    r[j] = (byte)Mul(r[j], root);
                    if (j + 1 < r.Length) r[j] ^= r[j + 1];
                }
                root = Mul(root, 0x02);
            }
            return r;
        }

        public static byte[] Remainder(byte[] data, int degree)
        {
            var div = Divisor(degree);
            var r = new byte[degree];
            foreach (byte b in data)
            {
                int factor = b ^ r[0];
                Array.Copy(r, 1, r, 0, r.Length - 1);
                r[r.Length - 1] = 0;
                for (int i = 0; i < r.Length; i++) r[i] ^= (byte)Mul(div[i], factor);
            }
            return r;
        }

        // ---------- encoding ----------
        public static byte[] DataBytes(byte[] text, int ver, int level)
        {
            var bits = new List<bool>();
            Action<int, int> put = (val, len) => { for (int i = len - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
            put(4, 4);
            put(text.Length, ver < 10 ? 8 : 16);
            foreach (byte b in text) put(b, 8);
            int capacity = DataCodewords(ver, level) * 8;
            put(0, Math.Min(4, capacity - bits.Count));
            put(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) put(pad, 8);
            var bytes = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++) if (bits[i]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
            return bytes;
        }

        public static byte[] AddEccAndInterleave(byte[] data, int ver, int level)
        {
            int numBlocks = Blocks[level][ver], eccLen = EccPerBlock[level][ver];
            int raw = RawDataModules(ver) / 8;
            int numShort = numBlocks - raw % numBlocks, shortLen = raw / numBlocks;
            var blocks = new List<byte[]>();
            for (int i = 0, k = 0; i < numBlocks; i++)
            {
                int len = shortLen - eccLen + (i < numShort ? 0 : 1);
                var dat = new byte[len];
                Array.Copy(data, k, dat, 0, len);
                k += len;
                var ecc = Remainder(dat, eccLen);
                var block = new List<byte>(dat);
                if (i < numShort) block.Add(0);   // placeholder, skipped when interleaving
                block.AddRange(ecc);
                blocks.Add(block.ToArray());
            }
            var result = new List<byte>();
            for (int i = 0; i < blocks[0].Length; i++)
                for (int j = 0; j < blocks.Count; j++)
                    if (i != shortLen - eccLen || j >= numShort) result.Add(blocks[j][i]);
            return result.ToArray();
        }

        // The symbol without quiet zone: [y, x], true = dark. level 0-3 = L, M, Q, H. The same array for the same text and
        // level (callers only read it): drawing, the quiet zone and the checks each ask for it, and a long text takes about
        // 30 ms to encode (a label with dozens of codes opened in seconds).
        public static bool[,] Encode(string text, int level)
        {
            string key = level + "|" + text;
            lock (encoded)
            {
                bool[,] m;
                if (encoded.TryGetValue(key, out m)) return m;
            }
            var made = EncodeNew(text, level);
            lock (encoded)
            {
                if (encoded.Count >= 256) encoded.Clear();   // a few labels' worth; a print run with data changes them each time
                encoded[key] = made;
            }
            return made;
        }

        static readonly Dictionary<string, bool[,]> encoded = new Dictionary<string, bool[,]>();

        static bool[,] EncodeNew(string text, int level)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            int ver = VersionFor(bytes.Length, level);
            if (ver < 0) throw new ArgumentException("too long");
            var codewords = AddEccAndInterleave(DataBytes(bytes, ver, level), ver, level);
            var q = new Matrix(ver);
            q.DrawFunctionPatterns(level);
            q.DrawCodewords(codewords);
            int best = 0, bestPenalty = int.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                q.ApplyMask(mask);
                q.DrawFormatBits(level, mask);
                int p = q.Penalty();
                if (p < bestPenalty) { best = mask; bestPenalty = p; }
                q.ApplyMask(mask);   // undo (XOR)
            }
            q.ApplyMask(best);
            q.DrawFormatBits(level, best);
            return q.Modules;
        }

        public static int FormatBits(int level, int mask)
        {
            int data = FormatLevelBits[level] << 3 | mask, rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            return (data << 10 | rem) ^ 0x5412;
        }

        public static int VersionBits(int ver)
        {
            int rem = ver;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            return ver << 12 | rem;
        }

        public static int[] AlignmentPositions(int ver)
        {
            if (ver == 1) return new int[0];
            int num = ver / 7 + 2, size = ver * 4 + 17;
            int step = ver == 32 ? 26 : (ver * 4 + num * 2 + 1) / (num * 2 - 2) * 2;
            var r = new int[num];
            r[0] = 6;
            for (int i = num - 1, pos = size - 7; i >= 1; i--, pos -= step) r[i] = pos;
            return r;
        }

        sealed class Matrix
        {
            public readonly bool[,] Modules;
            readonly bool[,] function;
            readonly int size, ver;

            public Matrix(int version)
            {
                ver = version;
                size = ver * 4 + 17;
                Modules = new bool[size, size];
                function = new bool[size, size];
            }

            void Set(int x, int y, bool dark) { Modules[y, x] = dark; function[y, x] = true; }

            public void DrawFunctionPatterns(int level)
            {
                for (int i = 0; i < size; i++) { Set(6, i, i % 2 == 0); Set(i, 6, i % 2 == 0); }
                Finder(3, 3); Finder(size - 4, 3); Finder(3, size - 4);
                var pos = AlignmentPositions(ver);
                int n = pos.Length;
                for (int i = 0; i < n; i++)
                    for (int j = 0; j < n; j++)
                        if (!(i == 0 && j == 0 || i == 0 && j == n - 1 || i == n - 1 && j == 0))
                            for (int dy = -2; dy <= 2; dy++)
                                for (int dx = -2; dx <= 2; dx++)
                                    Set(pos[i] + dx, pos[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                DrawFormatBits(level, 0);   // reserves the format areas
                if (ver >= 7)
                {
                    int bits = VersionBits(ver);
                    for (int i = 0; i < 18; i++)
                    {
                        bool bit = ((bits >> i) & 1) != 0;
                        int a = size - 11 + i % 3, b = i / 3;
                        Set(a, b, bit);
                        Set(b, a, bit);
                    }
                }
            }

            void Finder(int cx, int cy)
            {
                for (int dy = -4; dy <= 4; dy++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = cx + dx, y = cy + dy, d = Math.Max(Math.Abs(dx), Math.Abs(dy));
                        if (x >= 0 && x < size && y >= 0 && y < size) Set(x, y, d != 2 && d != 4);
                    }
            }

            public void DrawFormatBits(int level, int mask)
            {
                int bits = FormatBits(level, mask);
                Func<int, bool> bit = i => ((bits >> i) & 1) != 0;
                for (int i = 0; i <= 5; i++) Set(8, i, bit(i));
                Set(8, 7, bit(6));
                Set(8, 8, bit(7));
                Set(7, 8, bit(8));
                for (int i = 9; i < 15; i++) Set(14 - i, 8, bit(i));
                for (int i = 0; i < 8; i++) Set(size - 1 - i, 8, bit(i));
                for (int i = 8; i < 15; i++) Set(8, size - 15 + i, bit(i));
                Set(8, size - 8, true);
            }

            public void DrawCodewords(byte[] data)
            {
                int i = 0;
                for (int right = size - 1; right >= 1; right -= 2)
                {
                    if (right == 6) right = 5;
                    for (int vert = 0; vert < size; vert++)
                        for (int j = 0; j < 2; j++)
                        {
                            int x = right - j;
                            bool upward = ((right + 1) & 2) == 0;
                            int y = upward ? size - 1 - vert : vert;
                            if (!function[y, x] && i < data.Length * 8)
                            {
                                Modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                                i++;
                            }
                        }
                }
            }

            public void ApplyMask(int mask)
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        if (function[y, x]) continue;
                        bool invert;
                        switch (mask)
                        {
                            case 0: invert = (x + y) % 2 == 0; break;
                            case 1: invert = y % 2 == 0; break;
                            case 2: invert = x % 3 == 0; break;
                            case 3: invert = (x + y) % 3 == 0; break;
                            case 4: invert = (x / 3 + y / 2) % 2 == 0; break;
                            case 5: invert = x * y % 2 + x * y % 3 == 0; break;
                            case 6: invert = (x * y % 2 + x * y % 3) % 2 == 0; break;
                            default: invert = ((x + y) % 2 + x * y % 3) % 2 == 0; break;
                        }
                        if (invert) Modules[y, x] = !Modules[y, x];
                    }
            }

            // ISO penalty rules (runs, 2x2 blocks, finder-like patterns, dark balance): used only to pick a mask.
            // Read from a flat copy (rows, and columns by stepping a row's length): the two-dimensional array's checked
            // reads made a long text's encoding take 30 ms.
            public int Penalty()
            {
                int p = 0, dark = 0, n = size;
                var m = new bool[n * n];
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) m[y * n + x] = Modules[y, x];
                for (int pass = 0; pass < 2; pass++)
                    for (int a = 0; a < n; a++)
                    {
                        // pass 0: row a, step 1; pass 1: column a, step n
                        int first = pass == 0 ? a * n : a, step = pass == 0 ? 1 : n;
                        int run = 1, window = 0;   // window: the last 11 modules as bits, the oldest highest
                        for (int b = 0; b < n; b++)
                        {
                            bool v = m[first + b * step];
                            if (b > 0)
                            {
                                if (v == m[first + (b - 1) * step]) run++;
                                else { if (run >= 5) p += run - 2; run = 1; }
                            }
                            window = (window << 1 | (v ? 1 : 0)) & 0x7FF;
                            if (b >= 10 && (window == FinderBefore || window == FinderAfter)) p += 40;
                        }
                        if (run >= 5) p += run - 2;
                    }
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        bool v = m[y * n + x];
                        if (v) dark++;
                        if (x + 1 < n && y + 1 < n && v == m[y * n + x + 1] && v == m[(y + 1) * n + x] && v == m[(y + 1) * n + x + 1]) p += 3;
                    }
                int total = n * n;
                p += Math.Abs(dark * 100 / total - 50) / 5 * 10;
                return p;
            }

            // the finder-like pattern 1011101 with four light modules after it (10111010000) or before it (00001011101)
            const int FinderBefore = 0x5D0, FinderAfter = 0x05D;
        }
    }
}
