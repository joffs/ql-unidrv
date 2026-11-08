// Label editor window (EditorForm.cs), part: new, open, save, undo and the document state.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- document ----------
        void PushUndo(object editor)
        {
            HideFitBar();   // the next edit closes "Larger than the label"
            undo.Push(doc);
            lastEdited = editor;
            UpdateCommands();
        }

        // The document changed: render, mark unsaved.
        void Touched()
        {
            if (!dirty) { dirty = true; UpdateTitle(); }
            Render();
            UpdateCommands();
        }

        void Render()
        {
            if (layout == null) { CheckQuietZones(); ShowInfo(); return; }
            if (FollowContent()) return;   // laid out again at the new length (and rendered there)
            var shown = canvas.Rendered;
            if (canvas.Dragging && dragBounds.HasValue && shown != null && shown.Width == layout.DotsX && shown.Height == layout.DotsY)
            {
                // While dragging: only where the dragged objects were and are now (a full render costs most of a move)
                var now = SelectionBounds();
                var part = Union(dragBounds.Value, now);
                EditorRender.RenderPart(shown, doc, layout, part, DateTime.Now, canvas.Selection);
                dragBounds = now;
                canvas.RenderedPart(part);
            }
            else using (EditorRender.Later(PictureConverted)) canvas.Rendered = EditorRender.Render(doc, layout);
            canvas.OutsideChanged();
            // While dragging, the strip and the quiet-zone check wait for the drop (the strip would cover the canvas under
            // the mouse).
            if (!canvas.Dragging) { CheckQuietZones(); ShowInfo(); }
            if (zoom > 0) ApplyZoom();   // the area shown follows the objects (scrolling)
        }

        // A picture's exact black-and-white version is ready (converted after a drop on a worker thread): the label with it.
        void PictureConverted()
        {
            if (IsDisposed || canvas.Dragging || layout == null) return;
            Render();
        }

        void CheckQuietZones()
        {
            using (EditorRender.Later(PictureConverted))
                intruders = layout == null ? new List<KeyValuePair<BarcodeObject, LabelObject>>() : EditorRender.QuietZoneIntruders(doc, layout, DateTime.Now);
            canvas.Crowded = new HashSet<LabelObject>(intruders.Select(p => (LabelObject)p.Key));
            canvas.Invalidate();
        }

        // How the panel names an object reaching into a quiet zone.
        static string NameOf(LabelObject o)
        {
            var t = o as TextObject;
            if (t != null)
            {
                string s = t.Text.Replace("\r", " ").Replace("\n", " ").Trim();
                return Lang.F("“{0}”", s.Length > 24 ? s.Substring(0, 23) + "…" : s);
            }
            if (o is LineObject) return Lang.T("A line");
            var shape = o as ShapeObject;
            if (shape != null)
                return new[] { Lang.T("A rectangle"), Lang.T("An ellipse"), Lang.T("A triangle"), Lang.T("A polygon"), Lang.T("A star") }[(int)shape.Kind];
            if (o is ImageObject) return Lang.T("A picture");
            var b = o as BarcodeObject;
            if (b != null) return b.Kind == Symbology.Qr ? Lang.T("A QR code") : Lang.T("A barcode");
            return Lang.T("Another object");
        }

        // The strip over the top of the canvas: objects with printed dots beyond the label, objects with printed dots in the
        // unprintable margin only (EditorRender.CutOff: an empty part of a box gives nothing), codes with something in
        // their quiet zone (one line each).
        void ShowInfo()
        {
            var now = DateTime.Now;
            var cuts = layout == null ? new List<EditorRender.Cut>() : doc.Objects.Select(o => EditorRender.CutOff(o, layout, now)).ToList();
            int count = cuts.Count(c => (c & EditorRender.Cut.Beyond) != 0), margin = cuts.Count(c => c == EditorRender.Cut.Margin);
            int crowded = intruders.Count;
            var lines = new List<string>();
            if (count > 0) lines.Add(count == 1 ? Lang.T("1 object reaches beyond the label – what lies outside doesn't print.")
                                                : Lang.F("{0} objects reach beyond the label – what lies outside doesn't print.", count));
            if (margin > 0) lines.Add(margin == 1 ? Lang.T("1 object reaches into the hatched margin – that part doesn't print.")
                                                  : Lang.F("{0} objects reach into the hatched margin – those parts don't print.", margin));
            if (crowded > 0) lines.Add(crowded == 1 ? Lang.T("1 code has objects in its quiet zone – scanners may not read it well.")
                                                    : Lang.F("{0} codes have objects in their quiet zone – scanners may not read them well.", crowded));
            string text = string.Join("\n", lines);
            if (text != outsideText) { outsideText = text; FitOutsideBar(); outsideBar.Invalidate(); }
            outsideBar.Visible = lines.Count > 0;
        }

        // The text wraps when the canvas is narrow (150 %, German).
        int OutsideTextLeft { get { return S(10) + Font.Height + S(8); } }
        void FitOutsideBar()
        {
            int width = Math.Max(1, outsideBar.Width - OutsideTextLeft - S(6));
            int text = TextRenderer.MeasureText(outsideText, Font, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            int height = Math.Max(Font.Height, text) + S(12);
            if (outsideBar.Height != height) outsideBar.Height = height;
        }

        void PaintOutsideBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = outsideBar.ClientRectangle;
            using (var b = new SolidBrush(Theme.Of(Role.NoteBack))) g.FillRectangle(b, r);
            using (var p = new Pen(Theme.Of(Role.NoteLine))) g.DrawLine(p, 0, r.Bottom - 1, r.Right, r.Bottom - 1);
            // a warning circle with "!" (a bar and a dot), as in the printer status
            int d = Font.Height, x = S(10), y = (r.Height - d) / 2;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(Theme.Fixed.StateWarning)) g.FillEllipse(b, x, y, d, d);
            float cx = x + d / 2f, w = d * 0.13f;
            using (var white = new SolidBrush(Theme.Fixed.StateSymbol))
            {
                g.FillRectangle(white, cx - w / 2, y + d * 0.22f, w, d * 0.4f);
                g.FillEllipse(white, cx - w * 0.6f, y + d * 0.68f, w * 1.2f, w * 1.2f);
            }
            int tx = OutsideTextLeft;
            TextRenderer.DrawText(g, outsideText, Font, new Rectangle(tx, 0, Math.Max(1, r.Width - tx - S(6)), r.Height), Theme.Of(Role.NoteText),
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        void SetDoc(LabelDoc d, IEnumerable<LabelObject> select)
        {
            doc = d;
            canvas.Doc = d;
            HideFitBar();   // undo, redo, another label
            canvas.Select(select ?? new LabelObject[0]);
            Render();
            FillProps(null);
            UpdateCommands();
        }

        void Undo(bool redo)
        {
            if (redo ? !undo.CanRedo : !undo.CanUndo) return;
            var indexes = canvas.Selection.Select(o => doc.Objects.IndexOf(o)).ToList();
            var d = redo ? undo.Redo(doc) : undo.Undo(doc);
            lastEdited = null;
            bool turned = d.Across != doc.Across;
            SetDoc(d, indexes.Where(i => i >= 0 && i < d.Objects.Count).Select(i => d.Objects[i]).ToList());
            if (turned) Relayout();
            if (!dirty) { dirty = true; UpdateTitle(); }
        }

        void UpdateCommands()
        {
            undoItem.Enabled = undo.CanUndo;
            redoItem.Enabled = undo.CanRedo;
            printButton.Enabled = printer != null && !printing;
            // While a print is checked and sent, printer and label stay as they were checked.
            printerBox.Enabled = printers.Count > 0 && !printing;
            labelBox.Enabled = !printing;
            tips.SetToolTip(printButton, printer == null ? MainForm.NoPrinterText : Lang.F("Print the label on {0} (Ctrl+P)", printer));
            UpdateArrangeBar();
        }

        void UpdateTitle()
        {
            string name = path != null ? Path.GetFileNameWithoutExtension(path) : Lang.T("New label");
            Text = name + (dirty ? " *" : "") + " – " + AppTitle;
        }

        // The texts are set when the window is built, so QL Label Maker starts again in the new language: with the label's
        // file (saved first when changed), else a new label on the same printer and label format. The setting is stored
        // only after the question (LanguageSwitch).
        async void SwitchLanguage(string language)
        {
            if (printing) return;
            bool restart;
            try
            {
                restart = await LanguageSwitch.Run(language, dirty, unsaved => AskLanguage(language, unsaved) == DialogResult.OK,
                                                   () => Task.FromResult(Save(false)), setting => Lang.Setting = setting);
            }
            catch (Exception ex) { Failed(this, Lang.T("The language couldn't be switched."), ex); return; }
            if (!restart) return;
            try { Process.Start(Application.ExecutablePath, RestartArguments(path, printer, label != null && !label.Custom ? label.Name : null)); }
            catch (Exception ex) { Failed(this, Lang.T("QL Label Maker couldn't start again in the new language."), ex); return; }
            dirty = false;   // saved, or nothing changed
            Close();
        }

        DialogResult AskLanguage(string setting, bool unsaved)
        {
            using (var d = LanguageSwitch.Question(AppTitle, setting, unsaved ? Lang.T("The label has unsaved changes: it is saved first.") : null))
                return d.ShowDialog(this);
        }

        // The file opens with its own label; without one, the printer and the label format (an own length has none).
        internal static string RestartArguments(string file, string printer, string formName)
        {
            if (file != null) return EditorProgram.Quote(file) + (printer != null ? " --printer " + EditorProgram.Quote(printer) : "");
            string args = printer != null ? "--printer " + EditorProgram.Quote(printer) : "";
            if (printer != null && formName != null) args += " --format " + EditorProgram.Quote(formName);
            return args;
        }

        bool ConfirmDiscard()
        {
            if (!dirty) return true;
            var answer = QlMessage.Show(this, Lang.T("Save the changes to this label?"), AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return false;
            return answer == DialogResult.No || Save(false);
        }

        void NewLabel()
        {
            if (printing || !ConfirmDiscard()) return;
            path = null;
            dirty = false;
            keepFileFormat = false;
            undo.Clear();
            SetDoc(new LabelDoc { Across = label != null && LabelLayout.AcrossByDefault(label) }, null);
            Relayout();
            UpdateTitle();
        }

        void OpenDialog()
        {
            if (printing || !ConfirmDiscard()) return;
            using (var dlg = new OpenFileDialog { Filter = Lang.T("QL labels") + " (*.qllabel)|*.qllabel|" + Lang.T("All files") + " (*.*)|*.*" })
                if (dlg.ShowDialog(this) == DialogResult.OK) Open(dlg.FileName);
        }

        void Open(string file)
        {
            LabelDoc d;
            try { d = LabelDoc.Load(file); }
            catch (Exception ex)
            {
                // A damaged or foreign file: the reader's message (about JSON, in Windows' language) doesn't help. A
                // missing file or one without access keeps Windows' message.
                bool damaged = ex is InvalidDataException || ex is ArgumentException || ex is InvalidOperationException;
                string what = Lang.F("Cannot open {0}.", Path.GetFileName(file));
                if (damaged) QlMessage.Ask(this, AppTitle, what, Lang.T("The file is damaged or is not a QL label file.") + "\n\n" + file,
                                           MessageBoxButtons.OK, MessageBoxIcon.Error);
                else Failed(this, what, ex);
                return;
            }
            path = file;
            dirty = false;
            undo.Clear();
            var c = LabelChoice.Find(choices, d.FormName, d.PaperId, d.Length01);
            // A length of its own: on the same tape, if this printer has it.
            var tapeOf = d.LengthMode.Length > 0 ? MediaTable.ById(d.PaperId) : null;
            if (tapeOf != null && tapeOf.Continuous && d.Length01 > 0 && (model == null || model.HasPaper(tapeOf.Id))) c = LabelChoice.Own(tapeOf, d.Length01 / 10.0);
            autoLength = c != null && c.Custom && d.LengthMode == "auto";
            lengthError.Text = "";
            // Only a label of the same paper (another length) or none: shown there, saved as it was made.
            keepFileFormat = d.FormName.Length > 0 && !IsFileFormat(c, d);
            string note = null;
            if (c != null) SelectLabel(c);
            if (keepFileFormat)
                note = Lang.F("The label was made for '{0}', which {1} doesn't offer. It is shown on {2}.", d.FormName, printer ?? "?", label != null ? label.Name : "?");
            SetDoc(d, null);
            Relayout();
            UpdateTitle();
            // A file from a newer version (0.21 safeguard): what can't be shown stays in the file when it is saved.
            string newer = d.Skipped > 0 ? Lang.F("{0} object(s) from a newer QL Label Maker can't be shown here. They stay in the file when you save.", d.Skipped)
                         : d.Newer ? Lang.T("This label was made with a newer QL Label Maker: some parts may be missing or shown simplified.") : null;
            if (newer != null) note = (note != null ? note + "\n\n" : "") + newer;
            if (note != null) QlMessage.Show(this, note, AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        bool Save(bool saveAs)
        {
            if (label != null && !keepFileFormat)
            {
                doc.FormName = label.Custom ? "" : label.Name;   // an own length has no form
                doc.PaperId = label.Media.Id;
                doc.Length01 = (int)Math.Round(label.LengthMm * 10);
                doc.LengthMode = label.Custom ? (autoLength ? "auto" : "own") : "";
            }
            string target = path;
            if (saveAs || target == null)
            {
                using (var dlg = new SaveFileDialog { Filter = Lang.T("QL labels") + " (*.qllabel)|*.qllabel", DefaultExt = "qllabel", AddExtension = true })
                {
                    if (path != null) dlg.FileName = Path.GetFileName(path);
                    if (dlg.ShowDialog(this) != DialogResult.OK) return false;
                    target = dlg.FileName;
                }
            }
            try { doc.Save(target); }
            catch (Exception ex)
            {
                Failed(this, Lang.F("Cannot save {0}.", Path.GetFileName(target)), ex);
                return false;
            }
            path = target;
            dirty = false;
            UpdateTitle();
            return true;
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            // While a print is checked and sent: the window closes by itself when it is sent or a question stops it.
            if (printing) { closeWhenPrinted = true; e.Cancel = true; return; }
            if (!ConfirmDiscard()) e.Cancel = true;
        }

        bool closeWhenPrinted;
    }
}
