// QL Label Formats' main window: the list commands (new, duplicate, delete, move, import, export) and drag and drop.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    sealed partial class MainForm
    {
        // ---------- commands ----------
        string UniqueName(string name)
        {
            var names = new HashSet<string>(formats.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            if (!names.Contains(name)) return name;
            for (int i = 2; ; i++)
            {
                string suffix = " (" + i + ")";
                string candidate = (name.Length + suffix.Length > LabelStore.MaxNameLength
                    ? name.Substring(0, LabelStore.MaxNameLength - suffix.Length) : name) + suffix;
                if (!names.Contains(candidate)) return candidate;
            }
        }

        // A key no format of this list or of the applied one has: a new format never looks like a rename of a deleted one
        // (Apply moves the default label format along with a rename, by key).
        int NewKey() { return LabelStore.NextKey(formats.Concat(saved)); }

        void Insert(LabelFormat f)
        {
            int at = Selected != null ? formats.IndexOf(Selected) + 1 : formats.Count;
            formats.Insert(at, f);
            FillList(f);
            SetDirty(true);
            nameBox.Focus();
            nameBox.SelectAll();
        }

        void NewFormat()
        {
            var m = MediaTable.ById(259);
            Insert(new LabelFormat { Key = NewKey(), Name = UniqueName(Lang.T("New label")), PaperId = m.Id, LengthMm = 50, FeedMm = 3 });
        }

        // A new format of this continuous paper and length, named after its size ("62 × 40 mm").
        public void NewFormat(int paperId, double lengthMm)
        {
            var m = MediaTable.ById(paperId);
            if (m == null || !m.Continuous) { NewFormat(); return; }
            Insert(new LabelFormat { Key = NewKey(), Name = UniqueName(Media.Mm(m.WidthMm) + " × " + Media.Mm(lengthMm) + " mm"), PaperId = m.Id, LengthMm = lengthMm, FeedMm = 3 });
        }

        void Duplicate()
        {
            var f = Selected;
            if (f == null) return;
            var copy = f.Clone();
            copy.Key = NewKey();
            copy.Name = UniqueName(f.Name);
            Insert(copy);
        }

        void DeleteSelected()
        {
            var f = Selected;
            if (f == null) return;
            string text = Lang.F("Delete the label format '{0}'?", f.Name);
            var defaultOn = saved.Any(s => string.Equals(s.Name, f.Name, StringComparison.OrdinalIgnoreCase)) ? DefaultOn(f.Name) : new List<string>();
            if (defaultOn.Count > 0)
                text += "\n\n" + Lang.F("It is your default label format on {0}. After Save & apply, programs there start with another paper size; choose a new default then.",
                                        string.Join(", ", defaultOn));
            if (QlMessage.Show(this, text, AppTitle, MessageBoxButtons.YesNo, defaultOn.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes) return;
            int i = formats.IndexOf(f);
            formats.Remove(f);
            FillList(formats.Count == 0 ? null : formats[Math.Min(i, formats.Count - 1)]);
            SetDirty(true);
        }

        // Moves within the format's group (the list shows the groups separately).
        void MoveSelected(int delta)
        {
            var f = Selected;
            if (f == null) return;
            string g = GroupKey(f);
            int i = formats.IndexOf(f), j = i + delta;
            while (j >= 0 && j < formats.Count && GroupKey(formats[j]) != g) j += delta;
            if (j < 0 || j >= formats.Count) return;
            formats[i] = formats[j];
            formats[j] = f;
            FillList(f);
            SetDirty(true);
        }

        void Revert()
        {
            if (dirty && QlMessage.Show(this, Lang.T("Discard all unsaved changes?"), AppTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            LoadFromDisk();
        }

        void ReplaceOrAdd(List<LabelFormat> imported, string what)
        {
            // Buttons that say what they do (not Yes/No); Add is the default: nothing is lost.
            DialogResult answer;
            using (var d = new SetupResult(AppTitle, ResultKind.Question, Lang.F("{0} label formats in {1}", imported.Count, what), null,
                                           Lang.T("Replace the current list, or add them to it?"), null, Lang.T("Cancel")))
            {
                HelpPages.SetPage(d, HelpPages.FormatsList);
                d.AddAction(Lang.T("Replace list"), () => { d.DialogResult = DialogResult.Yes; return null; });
                d.AddChoice(Lang.T("Add"), DialogResult.OK);
                answer = d.ShowDialog(this);
            }
            if (answer != DialogResult.Yes && answer != DialogResult.OK) return;
            if (answer == DialogResult.Yes) formats.Clear();
            int skipped = 0;
            foreach (var f in imported)
            {
                if (MediaTable.ById(f.PaperId) == null) { skipped++; continue; }
                f.Name = f.Name.Trim();
                if (f.Name.Length > LabelStore.MaxNameLength) f.Name = f.Name.Substring(0, LabelStore.MaxNameLength).TrimEnd();
                f.Name = UniqueName(f.Name);
                f.Key = NewKey();
                LabelStore.Normalize(f);
                formats.Add(f);
            }
            FillList(formats.FirstOrDefault());
            SetDirty(true);
            if (skipped > 0)
                QlMessage.Ask(this, AppTitle, Lang.F("{0} format(s) skipped: their paper (e.g. a multi-label paper such as \"62mm x 2\") can't be the base of a label format.", skipped),
                              null, MessageBoxButtons.OK, MessageBoxIcon.Information);   // one sentence (Show would split it at its colon)
        }

        void ImportFile(bool brother)
        {
            using (var dlg = new OpenFileDialog { Title = Lang.T("Import label formats") })
            {
                dlg.Filter = brother ? Lang.T("Brother label formats (*.dat;*.org)|*.dat;*.org|All files|*.*") : Lang.T("Label formats (*.json)|*.json|All files|*.*");
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var imported = brother ? BrotherDat.Read(dlg.FileName) : LabelStore.Load(dlg.FileName).Formats;
                    ReplaceOrAdd(imported, Path.GetFileName(dlg.FileName));
                }
                catch (Exception ex) { Failed(Lang.T("The file couldn't be imported."), ex); }
            }
        }

        // Brother's German file, embedded in development builds only; in English the UK or US seed list instead.
        static Stream FactoryStream() { return Lang.Current == "de" ? Assembly.GetExecutingAssembly().GetManifestResourceStream("bsq80V.ORG") : null; }

        void LoadFactory()
        {
            var factory = FactoryFormats();
            if (factory != null) ReplaceOrAdd(factory, Lang.T("Brother's factory list"));
        }

        // Brother's factory list, or null (with a message when the file is there but can't be read).
        List<LabelFormat> FactoryFormats()
        {
            using (var s = FactoryStream())
            {
                if (s != null) return BrotherDat.Read(s);
            }
            // Builds without Brother's file, and English: the same list as JSON (LabelStore.FindSeed, installed next to the exe)
            string seed = LabelStore.FindSeed(Lang.Current);
            if (seed == null) return null;
            try { return LabelStore.Load(seed).Formats; }
            catch (Exception ex)
            {
                QlMessage.Show(this, Lang.F("Cannot read {0}:", seed) + "\n" + ex.Message, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        void ExportFile(bool brother)
        {
            using (var dlg = new SaveFileDialog { Title = Lang.T("Export label formats") })
            {
                dlg.Filter = brother ? Lang.T("Brother label formats (*.dat)|*.dat|All files|*.*") : Lang.T("Label formats (*.json)|*.json|All files|*.*");
                dlg.FileName = brother ? "bsq80V.DAT" : "label-formats.json";
                if (brother)
                {
                    var lossy = formats.Where(f => !BrotherDat.CanEncode(f.Name)).Select(f => f.Name).ToList();
                    string warn = "";
                    if (formats.Count > BrotherDat.MaxFormats) warn += Lang.F("Brother's driver reads at most {0} formats; only the first {0} are exported.", BrotherDat.MaxFormats) + "\n";
                    if (lossy.Count > 0) warn += Lang.T("These names contain characters the Brother file cannot store:") + " " + string.Join(", ", lossy) + "\n";
                    if (warn.Length > 0 && QlMessage.Show(this, warn + "\n" + Lang.T("Export anyway?"), AppTitle, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
                }
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    if (brother) BrotherDat.Write(dlg.FileName, formats.Take(BrotherDat.MaxFormats).ToList());
                    else File.WriteAllText(dlg.FileName, LabelStore.ToJson(new LabelSet { Formats = formats }), new UTF8Encoding(false));
                    applyStatus.Text = Lang.F("Exported to {0}", dlg.FileName);
                }
                catch (Exception ex) { Failed(Lang.T("The file couldn't be exported."), ex); }
            }
        }


        // ---------- drag and drop (within a group) ----------
        void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            if (busy) return;
            dragItem = (ListViewItem)e.Item;
            try { list.DoDragDrop(e.Item, DragDropEffects.Move); }
            finally { dragItem = null; }
        }

        // Moves the dragged format while the mouse moves, so the list shows the new order at once.
        void OnDragOver(object sender, DragEventArgs e)
        {
            if (dragItem == null) { e.Effect = DragDropEffects.None; return; }
            e.Effect = DragDropEffects.Move;
            var pt = list.PointToClient(new Point(e.X, e.Y));
            var target = list.GetItemAt(pt.X, pt.Y);
            if (target == null || target == dragItem) return;
            var f = (LabelFormat)dragItem.Tag;
            var t = (LabelFormat)target.Tag;
            if (GroupKey(t) != GroupKey(f)) { e.Effect = DragDropEffects.None; return; }
            int to = formats.IndexOf(t);
            formats.Remove(f);
            formats.Insert(to, f);
            FillList(f);
            dragItem = list.Items.Cast<ListViewItem>().First(i => i.Tag == f);
            SetDirty(true);
        }
    }
}
