// Applies a label list to the machine (runs elevated): labels.json + labels.tsv in
// C:\ProgramData\QLUnidrv, and one Windows form per label format. Only forms this tool created
// (LabelSet.OwnedForms) are ever changed or deleted.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace QlLabels
{
    static class Sync
    {
        public enum Step { Add, Update, Remove, Unchanged }

        public sealed class Action
        {
            public Step Step;
            public string Name;   // for Update/Remove: the form's current name
            public int Cx, Cy;

            public override string ToString()
            {
                return string.Format("{0,-9} {1}{2}", Step.ToString().ToLowerInvariant(), Name,
                    Step == Step.Remove ? "" : string.Format(" ({0:0.000} x {1:0.000} mm)", Cx / 1000.0, Cy / 1000.0));
            }
        }

        // What applying `formats` would do to the forms database. Throws if the list is invalid.
        public static List<Action> Plan(IList<LabelFormat> formats, IList<FormInfo> forms, ICollection<string> ownedNames)
        {
            var owned = new HashSet<string>(ownedNames, StringComparer.OrdinalIgnoreCase);
            var errors = LabelStore.Validate(formats, forms, owned);
            if (errors.Count > 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
            var plan = new List<Action>();
            foreach (var f in formats)
            {
                var m = MediaTable.ById(f.PaperId);
                int cx = LabelStore.FormWidthUm(m), cy = LabelStore.FormLengthUm(f, m);
                var existing = forms.FirstOrDefault(x => string.Equals(x.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                Step step;
                if (existing == null) step = Step.Add;
                else if (existing.Cx == cx && existing.Cy == cy && existing.Flags == FormsDb.FORM_USER) step = Step.Unchanged;
                else step = Step.Update;
                plan.Add(new Action { Step = step, Name = existing != null ? existing.Name : f.Name, Cx = cx, Cy = cy });
            }
            var wanted = new HashSet<string>(formats.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in owned.Where(n => !wanted.Contains(n)).OrderBy(n => n))
                if (forms.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                    plan.Add(new Action { Step = Step.Remove, Name = name });
            return plan;
        }

        static List<LabelFormat> Prepare(List<LabelFormat> formats)
        {
            foreach (var f in formats) { f.Name = f.Name.Trim(); LabelStore.Normalize(f); }
            return formats;
        }

        // First install (--sync without labels.json): the factory list in the Windows display language becomes the list.
        public static string SeedIfMissing()
        {
            if (File.Exists(LabelStore.JsonPath)) return null;
            string seed = LabelStore.FindSeed(Lang.SystemLanguage);
            if (seed == null) throw new FileNotFoundException("No label formats yet, and no " + LabelStore.SeedName(Lang.SystemLanguage, RegionInfo.CurrentRegion.IsMetric) + " next to the program.");
            Directory.CreateDirectory(LabelStore.DataDir);
            var set = Seed(seed, LabelStore.TsvPath);
            LabelStore.WriteAtomic(LabelStore.JsonPath, LabelStore.ToJson(set));
            return "Label formats seeded from " + seed + " (Brother's factory list, Windows display language)" +
                   (set.OwnedForms.Count > 0 ? "; the " + set.OwnedForms.Count + " paper sizes named in labels.tsv stay this tool's." : ".");
        }

        // The seed list as the new labels.json. labels.json deleted (a reset) while labels.tsv is still there: every apply
        // writes it with the list, so the forms it names are this tool's and the new list may change or remove them.
        internal static LabelSet Seed(string seedPath, string tsvPath)
        {
            var set = LabelStore.Load(seedPath);
            set.OwnedForms = LabelStore.TsvNames(tsvPath);
            set.HasOwnedList = true;
            return set;
        }

        // formats: the new list; null = re-apply the current labels.json (install, --sync).
        public static List<string> Apply(List<LabelFormat> formats)
        {
            var log = new List<string>();
            Directory.CreateDirectory(LabelStore.DataDir);
            string damaged;
            var current = LabelStore.LoadCurrent(out damaged);
            if (damaged != null)
            {
                // QL Label Formats shows a damaged list as empty (or Brother's factory list): its new list replaces it.
                if (formats == null) throw new InvalidDataException(Lang.F("The label list {0} is damaged: {1}", LabelStore.JsonPath, damaged));
                File.Copy(LabelStore.JsonPath, LabelStore.DamagedCopyPath, true);
                log.Add(Lang.F("The damaged label list was kept as {0}.", LabelStore.DamagedCopyPath));
            }
            var set = new LabelSet { Formats = Prepare(formats ?? current.Formats), OwnedForms = current.OwnedForms, HasOwnedList = true };

            using (var db = new FormsDb.Writer())
            {
                var plan = Plan(set.Formats, db.Enumerate(), current.OwnedForms);

                // Files first, with every form we are about to create already counted as ours: an
                // interrupted sync never leaves forms behind that the tool doesn't know about.
                var wanted = set.Formats.Select(f => f.Name).ToList();
                set.OwnedForms = current.OwnedForms.Union(wanted, StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
                LabelStore.WriteAtomic(LabelStore.TsvPath, LabelStore.ToTsv(set.Formats));
                LabelStore.WriteAtomic(LabelStore.JsonPath, LabelStore.ToJson(set));

                foreach (var a in plan)
                {
                    switch (a.Step)
                    {
                        case Step.Add: db.Add(a.Name, a.Cx, a.Cy); break;
                        case Step.Update: db.Set(a.Name, a.Cx, a.Cy); break;
                        case Step.Remove: db.Delete(a.Name); break;
                    }
                }
                set.OwnedForms = wanted.OrderBy(n => n).ToList();
                LabelStore.WriteAtomic(LabelStore.JsonPath, LabelStore.ToJson(set));
                log.AddRange(KeepNumber256Free(db, set.OwnedForms));
                log.Add(Lang.F("{0} label formats: {1} added, {2} updated, {3} unchanged, {4} removed.", set.Formats.Count,
                    plan.Count(a => a.Step == Step.Add), plan.Count(a => a.Step == Step.Update),
                    plan.Count(a => a.Step == Step.Unchanged), plan.Count(a => a.Step == Step.Remove)));
            }
            return log;
        }

        // Windows numbers paper sizes (dmPaperSize) by their position in the forms database, and number 256 is
        // also DMPAPER_USER ("custom size"). The paper at position 256 can't be selected, and Unidrv matches
        // custom sizes to it (seen 2026-10-21 on a PC with Brother's drivers: "DT 102mm x 152mm" was number 256,
        // a 102 mm custom size printed on 62 mm tape). So this tool keeps position 256 for a placeholder form,
        // too large for any QL printer: when the list reaches 255 forms it adds the placeholder, and if a real
        // paper already sits at 256 it moves its own label formats to the end of the list until the
        // placeholder can take that place. Label formats that move get new numbers (their names stay).
        public const string ReservedForm = "Reserved: paper number 256";
        const int ReservedUm = 5000000;  // 5 m x 5 m
        const int DmpaperUser = 256;

        public static List<string> KeepNumber256Free(IFormsWriter db, ICollection<string> ownedNames)
        {
            var log = new List<string>();
            var owned = new HashSet<string>(ownedNames, StringComparer.OrdinalIgnoreCase);
            Func<FormInfo, bool> isReserved = x => x.Flags == FormsDb.FORM_USER &&
                string.Equals(x.Name, ReservedForm, StringComparison.OrdinalIgnoreCase);
            Func<FormInfo, bool> isOwned = x => x.Flags == FormsDb.FORM_USER && owned.Contains(x.Name);
            var moved = new List<FormInfo>();
            var forms = db.Enumerate();
            int at = forms.FindIndex(x => isReserved(x));
            if (at >= 0 && at != DmpaperUser - 1)
            {
                db.Delete(forms[at].Name);
                forms = db.Enumerate();
            }
            string blocked = null;
            while (true)
            {
                if (forms.Count < DmpaperUser)
                {
                    if (forms.Count == DmpaperUser - 1) db.Add(ReservedForm, ReservedUm, ReservedUm);
                    break;
                }
                var f = forms[DmpaperUser - 1];
                if (isReserved(f)) break;
                // Our own label format at 256: move it to the end. A foreign paper: move one of ours from
                // before it, which moves that paper to 255.
                var mine = isOwned(f) ? f : forms.Take(DmpaperUser - 1).LastOrDefault(isOwned);
                if (mine == null) { blocked = f.Name; break; }
                db.Delete(mine.Name);
                moved.Add(mine);
                forms = db.Enumerate();
            }
            foreach (var m in moved) db.Add(m.Name, m.Cx, m.Cy);
            if (moved.Count > 0)
                log.Add(Lang.F("Paper number 256 kept free: {0} label format(s) moved to the end of the Windows list.", moved.Count));
            // install.ps1 colours lines starting with "WARNING" (its --sync runs in English).
            if (blocked != null)
                log.Add(Lang.F("WARNING: Windows paper number 256 is '{0}' (from another program or driver). Windows can't select it, and " +
                               "custom sizes may print on the wrong tape. Delete a paper size you don't need in Print server properties > Forms, " +
                               "then click Save & apply in QL Label Formats again.", blocked));
            return log;
        }

        // Dry run (no admin rights needed): validation, form actions and the labels.tsv content.
        public static List<string> Preview(List<LabelFormat> formats)
        {
            string damaged;
            var current = LabelStore.LoadCurrent(out damaged);
            if (damaged != null && formats == null) throw new InvalidDataException("The label list " + LabelStore.JsonPath + " is damaged: " + damaged);
            formats = Prepare(formats ?? current.Formats);
            var log = new List<string> { "Owned forms: " + current.OwnedForms.Count + (damaged != null ? " (from labels.tsv: labels.json is damaged)" : current.HasOwnedList ? "" : " (no list yet)") };
            log.AddRange(Plan(formats, FormsDb.Enumerate(), current.OwnedForms).Select(a => a.ToString()));
            log.Add("--- labels.tsv");
            log.AddRange(LabelStore.ToTsv(formats).TrimEnd('\n').Split('\n'));
            return log;
        }

        // Uninstall: delete the forms this tool created; labels.json keeps the formats.
        public static List<string> RemoveForms()
        {
            string damaged;
            var set = LabelStore.LoadCurrent(out damaged);  // damaged: the forms named in labels.tsv; the file stays as it is
            int removed = 0;
            using (var db = new FormsDb.Writer())
            {
                foreach (var name in set.OwnedForms)
                    if (db.Delete(name)) removed++;
                db.Delete(ReservedForm);
            }
            set.OwnedForms.Clear();
            set.HasOwnedList = true;
            if (File.Exists(LabelStore.JsonPath) && damaged == null) LabelStore.WriteAtomic(LabelStore.JsonPath, LabelStore.ToJson(set));
            return new List<string> { removed + " paper sizes removed." };
        }

        // Unelevated check for the status bar: does every format have its form with the right size?
        public static string Status(IList<LabelFormat> formats)
        {
            List<FormInfo> forms;
            try { forms = FormsDb.Enumerate(); }
            catch (Exception ex) { return Lang.T("Paper sizes:") + " " + ex.Message; }
            int missing = 0;
            foreach (var f in formats)
            {
                var m = MediaTable.ById(f.PaperId);
                if (m == null) { missing++; continue; }
                var x = forms.FirstOrDefault(k => string.Equals(k.Name, f.Name, StringComparison.OrdinalIgnoreCase));
                if (x == null || x.Flags != FormsDb.FORM_USER || x.Cx != LabelStore.FormWidthUm(m) || x.Cy != LabelStore.FormLengthUm(f, m))
                    missing++;
            }
            return missing == 0 ? Lang.T("Windows paper sizes in sync") : Lang.F("{0} format(s) not applied to Windows yet", missing);
        }
    }
}
