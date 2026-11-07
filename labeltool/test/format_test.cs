// Unit tests for the label formats in QL Label Formats: validation (LabelStore.ValidateFormat / Validate), labels.json
// round trip, labels.tsv lines (what the plug-in reads, driver/src/labels.*) and Brother's .dat files (BrotherDat).
// Called by StatusTest.Main, in English.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace QlLabels.Tests
{
    static class FormatTest
    {
        static int failures;

        static void Check(bool ok, string what)
        {
            if (ok) return;
            failures++;
            Console.WriteLine("FAIL: " + what);
        }

        const int Continuous62 = 259, DieCut29x90 = 271;

        static LabelFormat Format(string name, int paper, double length, double feed = 3, int key = 1)
        {
            var f = new LabelFormat { Key = key, Name = name, PaperId = paper, LengthMm = length, FeedMm = feed };
            LabelStore.Normalize(f);
            return f;
        }

        public static int Run()
        {
            Validation();
            Files();
            Brother();
            Seeds();
            return failures;
        }

        static void Validation()
        {
            var m62 = MediaTable.ById(Continuous62);
            var m29 = MediaTable.ById(DieCut29x90);
            Check(m62 != null && m62.Continuous && m62.Width01 == 620, "media 259 = 62 mm continuous");
            Check(m29 != null && !m29.Continuous && m29.Width01 == 290 && m29.Length01 == 898, "media 271 = 29 x 90 mm die-cut (89.8 mm)");

            Check(LabelStore.ValidateFormat(Format("Shipping 100", Continuous62, 100)) == null, "valid continuous format");
            Check(LabelStore.ValidateFormat(Format("", Continuous62, 100)) == "The name is empty.", "empty name");
            Check(LabelStore.ValidateFormat(Format(" x", Continuous62, 100)) == "The name starts or ends with spaces.", "leading space");
            Check(LabelStore.ValidateFormat(Format(new string('n', LabelStore.MaxNameLength), Continuous62, 100)) == null &&
                  LabelStore.ValidateFormat(Format(new string('n', LabelStore.MaxNameLength + 1), Continuous62, 100)).StartsWith("The name is longer than 31"),
                  "name length limit 31 (dmFormName)");
            Check(LabelStore.ValidateFormat(Format("a\tb", Continuous62, 100)) == "The name contains control characters.", "control character");
            Check(LabelStore.ValidateFormat(Format(m62.Name, Continuous62, 100)) == "'" + m62.Name + "' is the name of a Brother paper.", "Brother paper name");
            Check(LabelStore.ValidateFormat(Format("x", 99999, 100)) == "Brother media 99999 is not supported.", "unknown paper");
            Check(LabelStore.ValidateFormat(Format("x", Continuous62, m62.MinLength01 / 10.0 - 0.1)).StartsWith("The length must be"), "too short");
            Check(LabelStore.ValidateFormat(Format("x", Continuous62, m62.MaxLength01 / 10.0 + 1)).StartsWith("The length must be"), "too long");
            Check(LabelStore.ValidateFormat(Format("x", Continuous62, 100, LabelStore.MaxFeedMm + 0.1)).StartsWith("The feed must be"), "feed too large");
            Check(m62.MinLength01 <= 165, "62 mm continuous from 16.5 mm or shorter");
            Check(LabelStore.ValidateFormat(Format("x", Continuous62, 17, 8)) == null, "length 17 / feed 8: 1 mm left");
            Check(LabelStore.ValidateFormat(Format("x", Continuous62, 16.5, 8)) == "The length must exceed twice the feed by at least 1 mm.", "length vs. feed");
            var dieCut = Format("Address", DieCut29x90, 12345, 0);
            Check(dieCut.LengthMm == 89.8 && LabelStore.ValidateFormat(dieCut) == null, "die-cut: length from the paper");

            var list = new List<LabelFormat> { Format("A", Continuous62, 50), Format("a", Continuous62, 60, 3, 2), Format("", DieCut29x90, 90, 3, 3) };
            var errors = LabelStore.Validate(list, null, new List<string>());
            Check(errors.Contains("'(no name)': The name is empty.") && errors.Contains("'A': the name is used 2 times."), "list: empty and duplicate names");
            var forms = new List<FormInfo>
            {
                new FormInfo { Name = "Letter", Flags = FormsDb.FORM_BUILTIN },
                new FormInfo { Name = "Mine", Flags = FormsDb.FORM_USER },
                new FormInfo { Name = "Other", Flags = FormsDb.FORM_USER },
            };
            errors = LabelStore.Validate(new[] { Format("Letter", Continuous62, 50), Format("Mine", Continuous62, 50, 3, 2), Format("Other", Continuous62, 50, 3, 3) },
                                         forms, new List<string> { "Mine" });
            Check(errors.Count == 2 && errors[0] == "'Letter': Windows already has a paper size with this name (built in)." &&
                  errors[1] == "'Other': Windows already has a paper size with this name (not created by this tool).", "list: Windows forms (owned ones are fine)");

            Check(LabelStore.Describe(Format("x", Continuous62, 100)) == "62 × 100 mm, continuous" &&
                  LabelStore.Describe(dieCut) == "29 × " + Media.Mm(89.8) + " mm, die-cut", "describe");
            Check(LabelStore.SizeText(Format("x", Continuous62, 100)) == "62 × 100 mm" &&
                  LabelStore.SizeText(dieCut) == "29 × " + Media.Mm(89.8) + " mm", "size text (the list's Size column)");
        }

        static void Files()
        {
            var set = new LabelSet
            {
                Formats = { Format("Versand \"Express\" ü", Continuous62, 52.5, 2.5, 7), Format("Adresse", DieCut29x90, 90, 3, 8) },
                OwnedForms = { "Adresse", "Versand \"Express\" ü" },
            };
            string path = Path.Combine(Path.GetTempPath(), "qllabels-test-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(path, LabelStore.ToJson(set));
                var back = LabelStore.Load(path);
                Check(back.HasOwnedList && back.OwnedForms.SequenceEqual(set.OwnedForms), "json: owned forms");
                Check(back.Formats.Count == 2 && back.Formats.Zip(set.Formats, (a, b) => a.SameAs(b) && a.Key == b.Key).All(x => x), "json: formats round trip");
            }
            finally { try { File.Delete(path); } catch (Exception) { } }
            Check(!LabelStore.Load(path).HasOwnedList && LabelStore.Load(path).Formats.Count == 0, "json: missing file = empty list");

            // A damaged labels.json (cut short, edited by hand) counts as damaged, not as a read error (L1, 2026-10-31);
            // the forms this tool created are then the names in labels.tsv.
            foreach (var bad in new[] { "{\"formats\": [{\"name\": \"A\", \"paper_id\": 2", "[1, 2]", "hello",
                                        "{\"formats\": [{\"name\": \"A\", \"length_mm\": 50}]}", "{\"formats\": [\"A\"]}" })
            {
                Exception error = null;
                try
                {
                    File.WriteAllText(path, bad);
                    LabelStore.Load(path);
                }
                catch (Exception ex) { error = ex; }
                finally { try { File.Delete(path); } catch (Exception) { } }
                Check(error != null && LabelStore.IsDamage(error), "json: damaged file detected (" + bad + ")");
            }
            Check(LabelStore.IsDamage(new ArgumentException()) && !LabelStore.IsDamage(new IOException()) &&
                  !LabelStore.IsDamage(new UnauthorizedAccessException()), "json: damage vs. read error");
            try
            {
                File.WriteAllText(path, LabelStore.ToTsv(set.Formats));
                Check(LabelStore.TsvNames(path).SequenceEqual(new[] { "Versand \"Express\" ü", "Adresse" }), "tsv: names (owned forms of a damaged list)");
            }
            finally { try { File.Delete(path); } catch (Exception) { } }

            // labels.tsv: name, paper id, length 0.1 mm, feed in 300 dpi dots (continuous) / the paper's offset (die-cut).
            var lines = LabelStore.ToTsv(set.Formats.Concat(new[] { Format("unknown", 99999, 50) })).Split('\n');
            var m29 = MediaTable.ById(DieCut29x90);
            Check(lines[0] == "# QLUnidrv labels 1", "tsv: header");
            Check(lines[1] == "Versand \"Express\" ü\t259\t525\t30", "tsv: continuous 52.5 mm, feed 2.5 mm = 30 dots");
            Check(lines[2] == "Adresse\t271\t898\t" + m29.TopDots, "tsv: die-cut from the paper");
            Check(lines.Length == 4 && lines[3] == "", "tsv: unknown paper left out");
            Check(LabelStore.FeedDots(Format("x", Continuous62, 50, 3), MediaTable.ById(Continuous62)) == 35, "feed 3 mm = 35 dots");
        }

        // The seeds: Brother's factory list (16 formats) in German, UK English (mm) and US English (inches), the same papers,
        // lengths and feeds in the same order; only the names differ. German for a German Windows display language, else
        // US when Windows measures in US units, else UK.
        static void Seeds()
        {
            Check(LabelStore.SeedName("de", true) == "default-labels.json" && LabelStore.SeedName("de", false) == "default-labels.json" &&
                  LabelStore.SeedName("en", true) == "default-labels-en.json" && LabelStore.SeedName("en", false) == "default-labels-en-us.json",
                  "seed: file names (language, measurement system)");
            string de = LabelStore.FindSeedFile("default-labels.json");
            Check(de != null, "seed: German list found");
            if (de == null) return;
            var g = LabelStore.Load(de).Formats;
            Check(g.Count == 16 && g[0].Name == "Adress-Etikett (Standard)" && g[0].PaperId == 271, "seed: German = Brother's 16 factory formats");
            Check(!LabelStore.Load(de).HasOwnedList, "seed: no owned forms");

            // A reset (labels.json deleted, Windows' forms and labels.tsv still there): the forms labels.tsv names stay ours.
            string tsv = Path.Combine(Path.GetTempPath(), "qllabels-test-" + Guid.NewGuid().ToString("N") + ".tsv");
            try
            {
                var fresh = Sync.Seed(de, tsv);
                Check(fresh.HasOwnedList && fresh.OwnedForms.Count == 0 && fresh.Formats.Count == 16, "seed: new PC, nothing owned");
                File.WriteAllText(tsv, "﻿# QLUnidrv labels 1\nAdress-Etikett (Standard)\t271\t898\t35\nOld format\t259\t500\t35\n", new System.Text.UTF8Encoding(false));
                var reset = Sync.Seed(de, tsv);
                Check(reset.OwnedForms.SequenceEqual(new[] { "Adress-Etikett (Standard)", "Old format" }), "seed: reset keeps labels.tsv's forms as ours");
                var forms = new List<FormInfo> { new FormInfo { Name = "Adress-Etikett (Standard)", Flags = FormsDb.FORM_USER, Cx = 1, Cy = 1 },
                                                 new FormInfo { Name = "Old format", Flags = FormsDb.FORM_USER, Cx = 1, Cy = 1 } };
                Check(LabelStore.Validate(reset.Formats, forms, reset.OwnedForms).Count == 0 &&
                      LabelStore.Validate(fresh.Formats, forms, fresh.OwnedForms).Count == 1, "seed: reset applies; a foreign form of the same name still stops it");
                var plan = Sync.Plan(reset.Formats, forms, reset.OwnedForms);
                Check(plan.Any(a => a.Name == "Old format" && a.Step == Sync.Step.Remove) &&
                      plan.Any(a => a.Name == "Adress-Etikett (Standard)" && a.Step == Sync.Step.Update), "seed: reset updates and removes its old forms");
            }
            finally { try { File.Delete(tsv); } catch (Exception) { } }

            foreach (var kv in new[] { new KeyValuePair<string, string>("default-labels-en.json", "12mm Round Label"),
                                       new KeyValuePair<string, string>("default-labels-en-us.json", "0.47\" Round Label") })
            {
                string path = LabelStore.FindSeedFile(kv.Key);
                Check(path != null, "seed: " + kv.Key + " found");
                if (path == null) continue;
                var e = LabelStore.Load(path).Formats;
                Check(e.Count == g.Count && e.Select((f, i) => f.PaperId == g[i].PaperId && f.LengthMm == g[i].LengthMm && f.FeedMm == g[i].FeedMm).All(x => x),
                      "seed: " + kv.Key + " = German papers, lengths and feeds");
                Check(e.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == e.Count &&
                      e.All(f => LabelStore.ValidateFormat(f) == null && f.Name == f.Name.Trim()), "seed: " + kv.Key + " names unique, valid, trimmed");
                Check(e[0].Name == "Standard Address Label" && e[11].Name == kv.Value, "seed: " + kv.Key + " Brother's names");
                Check(!LabelStore.Load(path).HasOwnedList, "seed: " + kv.Key + " no owned forms");
            }
        }

        static void Brother()
        {
            var formats = new List<LabelFormat> { Format("Grüße 62", Continuous62, 123.45, 1.5), Format("Adresse", DieCut29x90, 90, 3) };
            string path = Path.Combine(Path.GetTempPath(), "qllabels-test-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                BrotherDat.Write(path, formats);
                Check(new FileInfo(path).Length == 10 + 2 * 0x8E, "dat: header + 2 records of 0x8E bytes");
                var back = BrotherDat.Read(path);
                Check(back.Count == 2 && back[0].Name == "Grüße 62" && back[0].PaperId == Continuous62 && back[0].LengthMm == 123.45 &&
                      back[0].FeedMm == 1.5 && back[1].Name == "Adresse" && back[1].LengthMm == 89.8 && back[1].Key == 2, "dat: round trip");
            }
            finally { try { File.Delete(path); } catch (Exception) { } }
            Check(BrotherDat.CanEncode("Grüße (A6)") && !BrotherDat.CanEncode("Label ✓"), "dat: Windows-1252 names");
            try
            {
                BrotherDat.Read(new MemoryStream(new byte[] { (byte)'x', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
                Check(false, "dat: not a Brother file");
            }
            catch (InvalidDataException ex) { Check(ex.Message == "Not a Brother label format file.", "dat: not a Brother file"); }
        }
    }
}
