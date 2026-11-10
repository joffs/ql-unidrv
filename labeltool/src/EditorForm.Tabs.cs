// Label editor window (EditorForm.cs), part: several labels as tabs (mockup variant A). The
// strip (DocTabStrip) sits right above the canvas, always shown; the window, canvas, toolbars and panel exist once and
// show the active tab. A tab keeps its label's state (LabelTab) while another one is shown: StoreTab before the switch,
// ShowTab after it. Ctrl+N / the + make a new "Untitled n" on the active tab's printer and label format; Ctrl+Tab,
// Ctrl+Shift+Tab switch; Ctrl+W, the tab's x or a middle click close it (the known question when it has changes).
// Closing the last tab leaves an empty "Untitled 1". A file opened always gets a new tab right of the active one; only
// a window whose one tab is the untouched "Untitled 1" lets the file take its place (Word, VS Code). A file already
// open only brings its tab forward (it blinks, the status bar says so): File > Open, Recent labels, a dropped file and
// a second start (Explorer).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        readonly DocTabStrip strip = new DocTabStrip();
        readonly List<LabelTab> tabs = new List<LabelTab>();
        LabelTab current;
        ContextMenuStrip tabMenu;

        void BuildTabs()
        {
            strip.Dock = DockStyle.Top;
            Shown += delegate { BeginInvoke((MethodInvoker)RunAfterShown); };   // height: the strip's own
            strip.TabSelected += i => SwitchTo(i);
            strip.TabCloseRequested += i => CloseTab(i);
            strip.NewTabRequested += () => NewTab();
            strip.TabContextMenu += ShowTabMenu;
            current = new LabelTab { Untitled = 1, Doc = doc, Undo = undo };
            tabs.Add(current);
            strip.Tabs.Add(new DocTabStrip.Tab { Tag = current });
            strip.Active = 0;
        }

        // The tab's name: its file's name, else "Untitled n".
        static string TabName(LabelTab t) { return t.Path != null ? Path.GetFileNameWithoutExtension(t.Path) : Lang.F("Untitled {0}", t.Untitled); }

        // The strip's entry for each tab: the name, unsaved, the tooltip with the place, the printer and the label format.
        void UpdateTabs()
        {
            if (current != null) { current.Path = path; current.Dirty = dirty; current.Printer = printer; current.Label = label; }
            for (int i = 0; i < tabs.Count; i++)
            {
                var t = tabs[i];
                var entry = strip.Tabs[i];
                entry.Title = TabName(t);
                entry.Dirty = t.Dirty;
                entry.Tag = t;
                entry.ToolTip = (t.Path ?? Lang.T("Not saved yet")) + "\n" + (t.Printer ?? MainForm.NoPrinterText) +
                                (t.Label != null ? " · " + t.Label.Name : "");
            }
            strip.Active = tabs.IndexOf(current);
            strip.Refresh();
        }

        // ---------- switching ----------
        // The active label's state into its tab (the editing session ends as with Esc: it stays selected).
        void StoreTab()
        {
            var t = current;
            if (t == null) return;
            canvas.CancelDraw();   // a drag of a drawing tool belongs to this label (the tool itself stays on)
            canvas.EndEdit();
            t.Doc = doc; t.Undo = undo; t.Path = path; t.Dirty = dirty; t.KeepFileFormat = keepFileFormat; t.LastEdited = lastEdited;
            t.Printer = printer; t.Model = model; t.Choices = choices; t.Label = label; t.HighRes = highRes; t.Roll = roll;
            t.AutoLength = autoLength; t.LengthError = lengthError != null ? lengthError.Text : "";
            t.Zoom = zoom;
            t.Scroll = new Point(-host.AutoScrollPosition.X, -host.AutoScrollPosition.Y);
            t.Selection = canvas.Selection.ToList();
            t.NewText = newText;
        }

        // A tab's state into the window: the boxes, the panel, the canvas, the title.
        void ShowTab(LabelTab t)
        {
            string printerBefore = printer;
            current = t;
            HideFitBar();
            dragBounds = null;
            wheel = 0;
            doc = t.Doc; undo = t.Undo; path = t.Path; dirty = t.Dirty; keepFileFormat = t.KeepFileFormat;
            printer = t.Printer; model = t.Model; choices = t.Choices; label = t.Label; highRes = t.HighRes;
            // the loaded roll belongs to the printer: the latest reading when the printer is the same
            if (printer != printerBefore) roll = t.Roll;
            autoLength = t.AutoLength;
            zoom = t.Zoom;
            newText = t.NewText;
            loading = true;
            try
            {
                printerBox.SelectedItem = printer;
                labelBox.Items.Clear();
                foreach (var ch in choices) labelBox.Items.Add(ch);
                if (label != null && label.Custom && !labelBox.Items.Contains(label)) labelBox.Items.Insert(0, label);
                labelBox.SelectedItem = label;
                int w = choices.Count == 0 ? S(160) : choices.Max(ch => TextRenderer.MeasureText(ch.ToString(), labelBox.Font).Width) + S(30);
                labelBox.DropDownWidth = Math.Max(w, S(160));
                if (lengthError != null) lengthError.Text = t.LengthError ?? "";
            }
            finally { loading = false; }
            canvas.Doc = doc;
            canvas.Select(t.Selection.Where(o => doc.Objects.Contains(o)));
            Relayout();   // layout, panel's label settings, zoom, render, status bar
            if (printer != null && !printers.Any(p => string.Equals(p, printer, StringComparison.OrdinalIgnoreCase))) PrinterGone();
            lastEdited = t.LastEdited;
            FillProps(null);
            UpdateCommands();
            UpdateTitle();
            var scroll = t.Scroll;
            if (zoom > 0) BeginInvoke((MethodInvoker)delegate { if (current == t && !IsDisposed) host.AutoScrollPosition = scroll; });
            if (printer != printerBefore) CheckRoll(); else ShowRoll();
        }

        // The tab's printer was removed while another tab was shown: as on start, the first QL printer still there (or
        // the last one chosen); the label format is checked as when a file is opened: kept for saving when that printer
        // doesn't offer it, the label then shown on another one.
        void PrinterGone()
        {
            string gone = printer;
            var was = label;
            string to = QlPrinters.Choose(printers, null);
            ChangePrinter(to);
            FillPrinterBox();
            if (was != null && !was.Custom && !keepFileFormat && (label == null || !string.Equals(label.Name, was.Name, StringComparison.OrdinalIgnoreCase)))
            {
                doc.FormName = was.Name;
                doc.PaperId = was.Media.Id;
                doc.Length01 = (int)Math.Round(was.LengthMm * 10);
                doc.LengthMode = "";
                keepFileFormat = true;
            }
            labelStatus.Text = to == null ? Lang.F("{0} is no longer there.", gone) : Lang.F("{0} is no longer there: the label is shown on {1}.", gone, to);
        }

        void SwitchTo(int index)
        {
            if (index < 0 || index >= tabs.Count) return;
            var t = tabs[index];
            if (t == current) { strip.Active = index; return; }
            if (printing) { strip.Active = tabs.IndexOf(current); return; }   // a print is checked and sent for this label
            StoreTab();
            ShowTab(t);
            if (ActiveForm == this) canvas.Focus();
        }

        // Ctrl+Tab, Ctrl+Shift+Tab: the next or previous tab, round.
        void SwitchBy(int step)
        {
            if (tabs.Count < 2) return;
            int i = tabs.IndexOf(current);
            SwitchTo(((i + step) % tabs.Count + tabs.Count) % tabs.Count);
        }

        // ---------- new and open ----------
        int FreeUntitled()
        {
            int n = 1;
            while (tabs.Any(t => t.Path == null && t.Untitled == n)) n++;
            return n;
        }

        // Ctrl+N, File > New, the +: an empty label in a new tab on the active tab's printer and label format.
        LabelTab NewTab()
        {
            if (printing) return null;
            StoreTab();
            var from = current;
            var t = new LabelTab
            {
                Untitled = FreeUntitled(),
                Printer = from.Printer, Model = from.Model, Choices = from.Choices, Label = from.Label, HighRes = from.HighRes, Roll = from.Roll,
                AutoLength = from.AutoLength && from.Label != null && from.Label.Custom,
                Zoom = from.Zoom,
            };
            t.Doc.Across = t.Label != null && LabelLayout.AcrossByDefault(t.Label);
            int at = tabs.IndexOf(from) + 1;
            tabs.Insert(at, t);
            strip.Tabs.Insert(at, new DocTabStrip.Tab { Tag = t });
            ShowTab(t);
            if (ActiveForm == this) canvas.Focus();
            return t;
        }

        // The tab with this file, or null.
        // The window's one tab is an empty label nobody has worked on (the "Untitled 1" of the start, or the one left
        // after closing the last tab): a file or a second start with --printer replaces it instead of adding a tab.
        bool OnlyStartTab()
        {
            return tabs.Count == 1 && current.Pristine && path == null && !dirty;
        }

        LabelTab TabOf(string file)
        {
            return tabs.FirstOrDefault(t => t.Path != null && SamePath(t.Path, file));
        }

        // File > Open, Recent labels, a dropped .qllabel, a second start: the file in a new tab right of the active one
        // (only the untouched "Untitled 1" a window starts with makes way for it); a file already open only brings its
        // tab forward.
        // printerName: the tab's printer (a restart in another language, a second start with --printer); null: the
        // active tab's.
        void OpenFile(string file, string printerName = null)
        {
            if (printing) return;
            var open = TabOf(file);
            if (open != null)
            {
                SwitchTo(tabs.IndexOf(open));
                strip.Blink(tabs.IndexOf(open));
                labelStatus.Text = Lang.F("“{0}” is already open.", TabName(open));
                RecentLabels.Add(file);   // opened again: to the top of File > Recent labels
                return;
            }
            var d = LoadLabel(file);
            if (d == null) return;
            if (!OnlyStartTab())
            {
                StoreTab();
                var from = current;
                var t = new LabelTab { Printer = from.Printer, Model = from.Model, Choices = from.Choices, Label = from.Label, HighRes = from.HighRes, Roll = from.Roll, Zoom = from.Zoom };
                int at = tabs.IndexOf(from) + 1;
                tabs.Insert(at, t);
                strip.Tabs.Insert(at, new DocTabStrip.Tab { Tag = t });
                ShowTab(t);
            }
            string p = printerName != null ? QlPrinters.Choose(printers, printerName) : null;
            if (p != null && p != printer) { SetPrinter(p, null); FillPrinterBox(); }
            ShowLabel(file, d);
            if (ActiveForm == this) canvas.Focus();
        }

        // A further file of the command line (a restart in another language), on its own printer.
        internal void OpenMore(string file, string printerName) { OpenFile(file, printerName); }

        // ---------- after the window is up ----------
        // Messages while the window is still being made (a start or restart with files) wait until it shows, with the
        // window as their owner.
        readonly List<Action> afterShown = new List<Action>();
        bool shownOnce;

        void WhenShown(Action a)
        {
            if (shownOnce) a(); else afterShown.Add(a);
        }

        void RunAfterShown()
        {
            shownOnce = true;
            var list = afterShown.ToList();
            afterShown.Clear();
            foreach (var a in list) a();
        }

        // ---------- closing ----------
        // Ctrl+W, the tab's x, a middle click, the tab's menu: the known question when it has changes. The last tab
        // leaves an empty "Untitled 1" on its printer and label format.
        bool CloseTab(int index)
        {
            if (index < 0 || index >= tabs.Count || printing) return false;
            var t = tabs[index];
            if (t == current) canvas.EndEdit();   // an emptied text goes before the question sees the label
            if (t == current ? dirty : t.Dirty)
            {
                if (t != current) SwitchTo(index);
                var answer = QlMessage.Show(this, Lang.F("Save the changes to “{0}”?", TabName(t)), AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return false;
                if (answer == DialogResult.Yes && !Save(false)) return false;
            }
            RemoveTab(t);
            return true;
        }

        void RemoveTab(LabelTab t)
        {
            int index = tabs.IndexOf(t);
            if (t == current) StoreTab();
            if (tabs.Count == 1)
            {
                // the window stays: an empty label on the same printer and label format
                var fresh = new LabelTab { Untitled = 1, Printer = t.Printer, Model = t.Model, Choices = t.Choices, Label = t.Label, HighRes = t.HighRes, Roll = t.Roll, Zoom = t.Zoom };
                fresh.Doc.Across = fresh.Label != null && LabelLayout.AcrossByDefault(fresh.Label);
                tabs[0] = fresh;
                strip.Tabs[0].Tag = fresh;
                ShowTab(fresh);
                return;
            }
            tabs.RemoveAt(index);
            strip.Tabs.RemoveAt(index);
            if (t == current) ShowTab(tabs[Math.Min(index, tabs.Count - 1)]);
            else UpdateTabs();
        }

        // Right click on a tab: Close, Close others, Save, Open folder.
        void ShowTabMenu(int index, Point screen)
        {
            if (index < 0 || index >= tabs.Count) return;
            var t = tabs[index];
            if (tabMenu != null) tabMenu.Dispose();
            tabMenu = new ContextMenuStrip { Font = menuBar.Font };
            tabMenu.Items.Add(Item(Lang.T("&Close"), Keys.Control | Keys.W, delegate { CloseTab(tabs.IndexOf(t)); }));
            tabMenu.Items.Add(new ToolStripMenuItem(Lang.T("Close &others"), null, delegate { CloseOthers(t); }) { Enabled = tabs.Count > 1 });
            tabMenu.Items.Add(new ToolStripSeparator());
            tabMenu.Items.Add(Item(Lang.T("&Save"), Keys.Control | Keys.S, delegate { SwitchTo(tabs.IndexOf(t)); Save(false); }));
            tabMenu.Items.Add(new ToolStripMenuItem(Lang.T("Open &folder"), null, delegate { OpenFolder(t.Path); }) { Enabled = t.Path != null });
            tabMenu.Show(screen);
        }

        // The others one after the other (each with its question); Cancel stops there.
        void CloseOthers(LabelTab keep)
        {
            foreach (var t in tabs.Where(x => x != keep).ToList())
                if (tabs.Contains(t) && !CloseTab(tabs.IndexOf(t))) break;
            if (tabs.Contains(keep)) SwitchTo(tabs.IndexOf(keep));
        }

        void OpenFolder(string file)
        {
            if (file == null) return;
            try { Process.Start("explorer.exe", "/select," + EditorProgram.Quote(file)); }
            catch (Exception ex) { Failed(this, Lang.T("The folder couldn't be opened."), ex); }
        }

        // Closing the window: the labels with changes. One: the known question; several: one question with the list
        // (all ticked). False: the window stays.
        bool ConfirmCloseAll()
        {
            StoreTab();
            var changed = tabs.Where(t => t.Dirty).ToList();
            if (changed.Count == 0) return true;
            if (changed.Count == 1)
            {
                var t = changed[0];
                if (t != current) ShowTab(t);   // StoreTab ran above
                var answer = QlMessage.Show(this, Lang.F("Save the changes to “{0}”?", TabName(t)), AppTitle, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel) return false;
                return answer == DialogResult.No || Save(false);
            }
            var items = changed.Select(t => new SaveChangesDialog.Item
            {
                Title = TabName(t),
                Detail = t.Path != null ? Path.GetDirectoryName(t.Path) : Lang.T("not saved yet"),
            }).ToList();
            DialogResult result;
            using (var d = new SaveChangesDialog(items))
            {
                var untitled = changed.FirstOrDefault(t => t.Path == null);
                if (untitled != null) d.Note = Lang.F("“{0}” asks for a folder and name afterwards.", TabName(untitled));
                result = d.ShowDialog(this);
            }
            if (result == DialogResult.Cancel) return false;
            if (result == DialogResult.No) return true;
            for (int i = 0; i < changed.Count; i++)
            {
                if (!items[i].Save) continue;
                if (changed[i] != current) { StoreTab(); ShowTab(changed[i]); }
                if (!Save(false)) return false;   // Save as cancelled or failed: the window stays, on that label
                StoreTab();
            }
            return true;
        }

        // ---------- keys ----------
        // Ctrl+Tab, Ctrl+Shift+Tab (also Ctrl+PgUp/PgDn are layer order here, so not those).
        bool TabKey(Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.Tab)) { SwitchBy(1); return true; }
            if (keyData == (Keys.Control | Keys.Shift | Keys.Tab)) { SwitchBy(-1); return true; }
            return false;
        }
    }
}
