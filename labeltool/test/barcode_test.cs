// Unit test for the Code 128 encoder (Barcodes.cs): sets A/B/C with Shift, FNC1 and GS1-128 element strings, the check
// character, the shortest encoding. Every encoding is decoded again from its bars with the standard's tables (ISO/IEC
// 15417) and must give the text back. Run by labeltool\build.cmd.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QlLabels.Tests
{
    static class BarcodeTest
    {
        static int failures;
        const char F = Code128.Fnc1;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        static string Vals(string text) { return string.Join(",", Code128.Values(text)); }

        static int Main()
        {
            Lang.Force("en");
            Known();
            Gs1();
            Tokens();
            RoundTrip();
            Shortest();
            if (failures > 0) { Console.WriteLine(failures + " barcode test(s) FAILED"); return 1; }
            Console.WriteLine("barcode_test: all OK");
            return 0;
        }

        // Codeword sequences worked out by hand from the standard's tables (check = start + sum(value * position) mod 103)
        static void Known()
        {
            // Wikipedia's example: set B only (the digit run 123 is too short for set C)
            Check(Vals("PJJ123C") == "104,48,42,42,17,18,19,35,55,106", "PJJ123C: " + Vals("PJJ123C"));
            Check(Vals("1234") == "105,12,34,82,106", "1234 in set C: " + Vals("1234"));
            // 5 digits: B + CC or CC + B are both 5 symbol characters; on a tie the start stays B (as before)
            Check(Vals("12345").StartsWith("104,17,99,23,45,"), "12345: " + Vals("12345"));
            // A tab needs set A: (103 + 33 + 73*2 + 34*3) mod 103 = 75
            Check(Vals("A\tB") == "103,33,73,34,75,106", "A<TAB>B in set A: " + Vals("A\tB"));
            // Lower case with one control character: set B and Shift (cheaper than switching): (104+65+98*2+73*3+66*4) mod 103 = 24
            Check(Vals("a\tb") == "104,65,98,73,66,24,106", "a<TAB>b with Shift: " + Vals("a\tb"));
            // Two control characters in lower case: switching to A and back costs 2, two Shifts cost 2 as well; either is 7 characters
            Check(Code128.Values("a\t\rb").Count == 2 + 6 + 1, "a<TAB><CR>b length: " + Vals("a\t\rb"));
            // GS (ASCII 29) in set A = 93
            Check(Vals("A\u001dB").Contains(",93,"), "GS in set A: " + Vals("A\u001dB"));
            // DEL (127) only in set B = 95
            Check(Vals("A\u007f") == "104,33,95," + (104 + 33 + 95 * 2) % 103 + ",106", "DEL in set B: " + Vals("A\u007f"));
            // FNC1 at the start in set C (GS1-128 with digits): Start C, FNC1 = 102
            string gs1 = F + "0109501101530003" + "17140704" + "10AB-123";
            var v = Code128.Values(gs1);
            string expect = "105,102,1,9,50,11,1,53,0,3,17,14,7,4,10,100,33,34,13,17,18,19";
            int sum = 105; var e = expect.Split(',').Select(int.Parse).ToList();
            for (int k = 1; k < e.Count; k++) sum += e[k] * k;
            Check(string.Join(",", v) == expect + "," + sum % 103 + ",106", "GS1 (01)(17)(10): " + string.Join(",", v));
            // module count: every symbol character 11 modules, stop 13
            Check(Code128.Modules("1234").Length == 11 * 4 + 13, "module count");
            Check(Code128.Check("Größe") != null && Code128.Check("ABC-123") == null && Code128.Check("A\tB") == null && Code128.Check(F + "01") == null,
                  "Check: ASCII 0-127 and FNC1 only");

            // a typed QR code saved without its data (written by a script, e.g. samples\print-data): built from its
            // fields on loading; an empty QR code says "QR code", not "barcode" (2026-11-09)
            var doc = LabelDoc.FromJson("{\"format\": \"QL label\", \"version\": 2, \"label\": {\"paperId\": 259, \"length01\": 500}, \"objects\": [" +
                "{\"type\": \"barcode\", \"symbology\": \"qr\", \"x\": 4, \"y\": 4, \"w\": 20, \"h\": 20, \"data\": \"\", \"qrType\": \"contact\", " +
                "\"qrFields\": {\"firstName\": \"{Vorname}\", \"lastName\": \"Muster\"}}]}");
            var qr = (BarcodeObject)doc.Objects[0];
            Check(qr.Data.Contains("BEGIN:VCARD") && qr.Data.Contains("{Vorname}") && qr.Problem(DateTime.Now) == null, "typed QR without data: built from its fields: " + qr.Problem(DateTime.Now));
            Check(QrCode.Check("", 1) == Lang.T("Enter the QR code's content."), "empty QR code: the QR text");
        }

        static void Gs1()
        {
            string err;
            Check(Code128.Gs1("(01)09501101530003(17)140704(10)AB-123", out err) == F + "0109501101530003" + "17140704" + "10AB-123" && err == null,
                  "GS1: fixed-length elements need no separator, the last one none either");
            Check(Code128.Gs1("(10)ABC(17)250101", out err) == F + "10ABC" + F + "17250101", "GS1: FNC1 after a variable-length element");
            Check(Code128.Gs1("(21)12345(10)X1", out err) == F + "2112345" + F + "10X1", "GS1: two variable-length elements");
            Check(Code128.Gs1("(3103)000189(10)A", out err) == F + "3103000189" + "10A", "GS1: 4-digit AI of predefined length");
            Check(Code128.Gs1("(01)09501101530004", out err) == null && err != null, "GS1: wrong GTIN check digit refused");
            Check(Code128.Gs1("(17)2501", out err) == null && err.Contains("6"), "GS1: (17) needs 6 digits: " + err);
            Check(Code128.Gs1("0109501101530003", out err) == null && err != null, "GS1: without brackets refused");
            Check(Code128.Gs1("(1)5", out err) == null && err != null, "GS1: 1-digit AI refused");
            Check(Code128.Gs1("(10)", out err) == null && err != null, "GS1: element without data refused");
            Check(Code128.Gs1("(10)A B", out err) == null && err != null, "GS1: space refused");
            Check(Code128.Gs1("(00)106141412345678908", out err) != null, "GS1: SSCC with a valid check digit");
        }

        // The field's notation for control characters (Code128.Expand, then Fields.Expand as the editor does)
        static void Tokens()
        {
            var day = new DateTime(2026, 11, 2);
            Func<string, string> content = s => Fields.Expand(Code128.Expand(s), day);
            Check(content("A{tab}B{cr}{lf}C") == "A\tB\r\nC", "tokens tab cr lf");
            Check(content("{GS}{rs}{Eot}") == "\u001d\u001e\u0004", "tokens gs rs eot, any case");
            Check(content("{fnc1}01") == F + "01", "token fnc1");
            Check(content("{x1D}{x00}{x7f}") == "\u001d\0\u007f", "hex tokens");
            Check(content("{x80}{x1}{xZZ}{foo}") == "{x80}{x1}{xZZ}{foo}", "unknown tokens stay as text");
            Check(content("{{tab}}") == "{tab}", "{{ keeps a literal brace: {{tab}} prints {tab}");
            Check(content("{x7B}tab{x7D}") == "{tab}", "{x7B}/{x7D} are braces, not a token");
            Check(content("LOT{date:yyMMdd}{gs}X") == "LOT261102\u001dX", "date field and token together");
            Check(content("plain") == "plain" && content("") == "" && Code128.Expand(null) == "", "no tokens");
            Check(Code128.HumanReadable("A\tB" + F + "C\u007f") == "ABC", "text under the bars without control characters");
        }

        // Decodes the bars with the patterns and the set rules; the text must come back
        static void RoundTrip()
        {
            var rnd = new Random(1);
            string alphabet = "0123456789ABCabcxyz-./ \t\r\n\u001d\u007f" + F;
            var samples = new List<string> { "PJJ123C", "1234", "12345", "123456789", "A\tB", "a\tb", "a\t\rb", "x1234567y", F + "0112345678901231" };
            for (int t = 0; t < 2000; t++)
            {
                int len = 1 + rnd.Next(20);
                var sb = new StringBuilder();
                for (int k = 0; k < len; k++) sb.Append(rnd.Next(3) == 0 ? (char)('0' + rnd.Next(10)) : alphabet[rnd.Next(alphabet.Length)]);
                samples.Add(sb.ToString());
            }
            foreach (string s in samples)
            {
                string back = Decode(Code128.Modules(s));
                Check(back == s, "round trip of " + Show(s) + ": got " + Show(back ?? "(not decodable)"));
            }
        }

        // The encoder's length never exceeds the old rule (set B, set C for digit runs of 4+ at an end or 6+ inside)
        // and matches a brute-force search over all set sequences for short texts
        static void Shortest()
        {
            var rnd = new Random(2);
            string alphabet = "0123456789Aa\t" + F;
            for (int t = 0; t < 400; t++)
            {
                int len = 1 + rnd.Next(7);
                var sb = new StringBuilder();
                for (int k = 0; k < len; k++) sb.Append(alphabet[rnd.Next(alphabet.Length)]);
                string s = sb.ToString();
                int got = Code128.Values(s).Count - 2;   // with the start code, without check and stop
                int best = Brute(s, 0, -1);
                Check(got == best, "shortest for " + Show(s) + ": " + got + " symbol characters, brute force " + best);
            }
        }

        // Fewest symbol characters for s[i..] with set cur active (-1 = before the start code, which counts as one)
        static int Brute(string s, int i, int cur)
        {
            if (i == s.Length) return cur < 0 ? 1 : 0;
            int best = int.MaxValue;
            // Shift (A <-> B) for one character of the other set
            if (cur == 0 || cur == 1)
            {
                char c0 = s[i];
                bool other = cur == 0 ? (c0 >= 96 && c0 < 128) : c0 < 32;
                if (other) best = 2 + Brute(s, i + 1, cur);
            }
            for (int set = 0; set < 3; set++)
            {
                int extra = cur < 0 ? 1 : cur == set ? 0 : 1;
                char c = s[i];
                if (set == 2)
                {
                    if (c == F) best = Math.Min(best, extra + 1 + Brute(s, i + 1, 2));
                    else if (i + 1 < s.Length && char.IsDigit(c) && char.IsDigit(s[i + 1]) && c < 128 && s[i + 1] < 128) best = Math.Min(best, extra + 1 + Brute(s, i + 2, 2));
                }
                else if (set == 0 ? (c < 96 || c == F) : (c >= 32 && c < 128 || c == F)) best = Math.Min(best, extra + 1 + Brute(s, i + 1, set));
            }
            return best;
        }

        static readonly string[] Patterns = Enumerable.Range(0, 107).Select(Code128.PatternOf).ToArray();

        static string Decode(bool[] m)
        {
            // widths of bars and spaces
            var w = new List<int>();
            for (int k = 0; k < m.Length;)
            {
                int j = k;
                while (j < m.Length && m[j] == m[k]) j++;
                w.Add(j - k);
                k = j;
            }
            var values = new List<int>();
            int p = 0;
            while (p + 6 <= w.Count)
            {
                bool stop = p + 7 == w.Count;
                string pat = string.Concat(w.Skip(p).Take(stop ? 7 : 6));
                int v = Array.IndexOf(Patterns, pat);
                if (v < 0) return null;
                values.Add(v);
                p += stop ? 7 : 6;
            }
            if (values.Count < 3 || values[values.Count - 1] != 106) return null;
            int sum = values[0];
            for (int k = 1; k < values.Count - 2; k++) sum += values[k] * k;
            if (sum % 103 != values[values.Count - 2]) return null;
            int set = values[0] - 103;
            if (set < 0 || set > 2) return null;
            var sb = new StringBuilder();
            bool shift = false;
            for (int k = 1; k < values.Count - 2; k++)
            {
                int v = values[k];
                int use = shift ? 1 - set : set;
                shift = false;
                if (v == 102) { sb.Append(F); continue; }
                if (use == 2)
                {
                    if (v < 100) sb.Append(v.ToString("00"));
                    else if (v == 100) set = 1;
                    else if (v == 101) set = 0;
                    else return null;
                    continue;
                }
                if (v < 96) sb.Append(use == 0 ? (char)(v < 64 ? v + 32 : v - 64) : (char)(v + 32));
                else if (v == 98) shift = true;
                else if (v == 99) set = 2;
                else if (v == 100) { if (use == 1) return null; set = 1; }
                else if (v == 101) { if (use == 0) return null; set = 0; }
                else return null;
            }
            return sb.ToString();
        }

        static string Show(string s)
        {
            return "\"" + string.Concat(s.Select(c => c == F ? "<FNC1>" : c < 32 || c == 127 ? "<" + (int)c + ">" : c.ToString())) + "\"";
        }
    }
}
