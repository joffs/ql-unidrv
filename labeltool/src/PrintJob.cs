// Print from data, the job: a label (the template) plus records → filled,
// checked labels → one print job with one page per label. Used by QL Label Maker's "Print with data" dialog and by
// QlLabelPrint.exe; neither asks anything here: the roll check (PrintReadiness / PrinterCheck) is the caller's.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;

namespace QlLabels
{
    enum RecordProblemKind { MissingField, Copies, Barcode, TextOverflow }

    // Something wrong with one record. Blocks: the label can't be printed as meant (a missing value, a bad copies cell);
    // else a warning the user may print anyway (a barcode that may not scan, a text cut off at its frame), as in the
    // editor's own print (EditorForm.ConfirmProblems).
    sealed class RecordProblem
    {
        public int Row;
        public RecordProblemKind Kind;
        public string Field;     // the field, the copies cell, or the object's short name
        public string Name;      // the row's labelname (null or empty: none)
        public string Message;
        public bool Blocks { get { return Kind == RecordProblemKind.MissingField || Kind == RecordProblemKind.Copies; } }
        public override string ToString()
        {
            return string.IsNullOrEmpty(Name) ? Lang.F("Row {0}: {1}", Row, Message) : Lang.F("Row {0} ({1}): {2}", Row, Name, Message);
        }
    }

    sealed class LabelJob
    {
        public readonly LabelDoc Template;
        public readonly LabelLayout Layout;
        public readonly List<DataField> Fields;
        public List<DataRecord> Records = new List<DataRecord>();
        public bool MissingEmpty { get; set; }   // "Leave missing fields empty" / --missing-empty
        public int Copies = 1;             // per label; a record's copies column multiplies
        public DateTime Now = DateTime.Now;   // one time for the whole job: {date}/{time} the same on every label

        public LabelJob(LabelDoc template, LabelLayout layout)
        {
            Template = template;
            Layout = layout;
            Fields = DataFields.Of(template);
        }

        // The same job on another printer's layout (the dialog's printer changed): records, choices and time stay.
        public LabelJob On(LabelLayout layout)
        {
            return new LabelJob(Template, layout) { Records = Records, MissingEmpty = MissingEmpty, Copies = Copies, Now = Now };
        }

        public FieldMatch Match(IEnumerable<string> columns) { return FieldMatch.Of(Fields, columns); }

        // The label filled with one record's values.
        public LabelDoc Fill(DataRecord r) { return DataFields.Apply(Template, r.Value, MissingEmpty); }

        // How many labels of a record are printed (Copies kept within 1-999, so no overflow).
        public int CopiesOf(DataRecord r) { return r.CopiesError != null ? 0 : r.Copies * Math.Max(1, Math.Min(DataRecord.MaxCopies, Copies)); }

        // At most this many labels in one job (security review 2026-11-09: 50,000 rows × 999 copies would be billions of
        // pages); more is refused before anything is sent.
        public const int MaxPages = 10000;

        public long PageCount(IEnumerable<DataRecord> records) { return records.Sum(r => (long)CopiesOf(r)); }

        // The records to check before printing: those that print, and those with a bad copies cell (which must block).
        // A row with copies 0 is skipped, so its missing values or barcode warnings don't stop or question the job
        // (dialog review 2026-11-09, P2).
        public List<DataRecord> ToCheck(IEnumerable<DataRecord> records)
        {
            return records.Where(r => r.CopiesError != null || CopiesOf(r) > 0).ToList();
        }

        // What is wrong with one record (empty: nothing).
        public List<RecordProblem> Check(DataRecord r) { return Check(r, Fill(r)); }

        public List<RecordProblem> Check(DataRecord r, LabelDoc filled)
        {
            var list = new List<RecordProblem>();
            if (r.CopiesError != null)
                list.Add(new RecordProblem { Row = r.Row, Kind = RecordProblemKind.Copies, Field = DataRecord.CopiesColumn,
                                             Message = Lang.F("'{0}' is not a number of copies (0–{1}).", r.CopiesError, DataRecord.MaxCopies) });
            if (!MissingEmpty)
                foreach (var f in Fields.Where(f => f.Required && r.Value(f.Name) == null))
                    list.Add(new RecordProblem { Row = r.Row, Kind = RecordProblemKind.MissingField, Field = f.Name,
                                                 Message = Lang.F("The data has no value for {0}.", "{" + f.Name + "}") });
            foreach (var o in filled.Objects)
            {
                var b = o as BarcodeObject;
                if (b != null)
                {
                    string p = EditorRender.Problem(b, Layout, Now);
                    if (p != null) list.Add(new RecordProblem { Row = r.Row, Kind = RecordProblemKind.Barcode, Field = Short(b.Content(Now)), Message = p });
                    continue;
                }
                var t = o as TextObject;
                if (t != null && EditorRender.Overflows(t))
                    list.Add(new RecordProblem { Row = r.Row, Kind = RecordProblemKind.TextOverflow, Field = Short(t.Text),
                                                 Message = Lang.F("The text “{0}” doesn't fit its box and is cut off.", Short(Fields.Count > 0 ? DataFields.Fill(t.Text, n => null, null) : t.Text)) });
            }
            foreach (var p in list) p.Name = Short(r.Name);
            return list;
        }

        static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length > 30 ? s.Substring(0, 29) + "…" : s;
        }

        // The label as shown (EditorRender.Render: 1 bit, the layout's view); the caller disposes it.
        public Bitmap Render(DataRecord r) { return EditorRender.Render(Fill(r), Layout, Now); }

        // One job, one page per label (copies as pages, so a record's own copies work): all or nothing is the caller's
        // check before; this renders each page only when the driver asks for it. Throws when Windows refuses the job.
        public void Print(IList<DataRecord> records, string documentName, Action<int, int> progress = null)
        {
            if (PageCount(records) > MaxPages) throw new InvalidDataException(Lang.F("More than {0} labels in one print job.", MaxPages));
            if (Layout == null || Layout.Approximate) throw new InvalidOperationException("no printer layout");
            var pages = new List<DataRecord>();
            foreach (var r in records) for (int i = CopiesOf(r); i > 0; i--) pages.Add(r);
            if (pages.Count == 0) return;
            DataRecord lastRecord = null;
            Bitmap last = null;
            try
            {
                Func<int, Bitmap> page = i =>
                {
                    if (progress != null) progress(i + 1, pages.Count);
                    if (pages[i] == lastRecord) return last;   // copies of the same label: rendered once
                    if (last != null) last.Dispose();
                    using (var view = Render(pages[i])) last = EditorRender.ForPrinter(view, Layout);
                    lastRecord = pages[i];
                    return last;
                };
                if (Layout.Custom != null) LabelPrinter.PrintPages(Layout.Custom, pages.Count, page, Layout.SkipX, documentName);
                else LabelPrinter.PrintPages(Layout.Page, pages.Count, page, documentName);
            }
            finally { if (last != null) last.Dispose(); }
        }

        // The layout a label file prints with on a printer, as QL Label Maker opens it (LabelChoice.Find, own length on
        // the same tape), for callers without the editor (QlLabelPrint.exe). null with error: the printer has no such
        // paper, or the label format isn't on the printer yet (QL Label Formats > Save & apply).
        public static LabelLayout LayoutFor(LabelDoc doc, string printer, out string error)
        {
            error = null;
            var model = QlPrinters.ModelOf(printer);
            var choices = LabelChoice.All(model);
            var c = LabelChoice.Find(choices, doc.FormName, doc.PaperId, doc.Length01);
            var tape = doc.LengthMode.Length > 0 ? MediaTable.ById(doc.PaperId) : null;
            if (tape != null && tape.Continuous && doc.Length01 > 0 && (model == null || model.HasPaper(tape.Id))) c = LabelChoice.Own(tape, doc.Length01 / 10.0);
            if (c == null) { error = Lang.F("{0} has no paper for this label.", printer); return null; }
            bool highRes = false;
            try { highRes = new PrinterSettings { PrinterName = printer }.DefaultPageSettings.PrinterResolution.Y == 600; }
            catch (Exception) { }
            var layout = LabelLayout.For(printer, c, highRes, doc.Across);
            if (layout.Approximate) { error = Lang.F("{0} doesn't offer '{1}' yet. A new or changed label format must be saved first: QL Label Formats, then Save & apply.", printer, c.Name); return null; }
            return layout;
        }
    }
}
