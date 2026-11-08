// Label editor window (EditorForm.cs), part: the menu bar, the command row (printer, label, copies, Print) and the status bar.
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
        // ---------- menu and toolbar ----------
        MenuStrip menuBar;
        StatusStrip statusBar;
        TableLayoutPanel commandRow;
        Label printerCaption;

        // One left line for the menu's first word, "Printer:" and the status bar's first text, at Brand.Gutter as in all
        // QL windows. The strips place each item's text themselves (centred, or left for the
        // status bar's label), so their paddings are measured.
        void AlignLeft()
        {
            int gutter = Brand.Gutter(Font.Height);
            var p = commandRow.Padding;
            commandRow.Padding = new Padding(Math.Max(0, gutter - printerCaption.Margin.Left), p.Top, p.Right, p.Bottom);
            foreach (ToolStrip strip in new ToolStrip[] { menuBar, statusBar })
            {
                if (strip.Items.Count == 0) continue;
                var item = strip.Items[0];
                bool left = item.TextAlign == ContentAlignment.MiddleLeft || item.TextAlign == ContentAlignment.TopLeft || item.TextAlign == ContentAlignment.BottomLeft;
                int text = left ? item.Bounds.X + item.Padding.Left
                                : item.Bounds.X + (item.Bounds.Width - TextRenderer.MeasureText(item.Text.Replace("&", ""), item.Font).Width) / 2;
                var q = strip.Padding;
                if (text != gutter) strip.Padding = new Padding(Math.Max(0, q.Left + gutter - text), q.Top, q.Right, q.Bottom);
            }
        }

        MenuStrip BuildMenu()
        {
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem(Lang.T("&File"));
            file.DropDownItems.Add(Item(Lang.T("&New"), Keys.Control | Keys.N, delegate { NewLabel(); }));
            file.DropDownItems.Add(Item(Lang.T("&Open..."), Keys.Control | Keys.O, delegate { OpenDialog(); }));
            file.DropDownItems.Add(Item(Lang.T("&Save"), Keys.Control | Keys.S, delegate { Save(false); }));
            file.DropDownItems.Add(Item(Lang.T("Save &as..."), Keys.Control | Keys.Shift | Keys.S, delegate { Save(true); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("&Print"), Keys.Control | Keys.P, delegate { Print(); }));
            file.DropDownItems.Add(new ToolStripSeparator());
            file.DropDownItems.Add(Item(Lang.T("&Close"), Keys.None, delegate { Close(); }));

            var edit = new ToolStripMenuItem(Lang.T("&Edit"));
            undoItem.Text = Lang.T("&Undo"); undoItem.ShortcutKeys = Keys.Control | Keys.Z; undoItem.Click += delegate { Undo(false); };
            redoItem.Text = Lang.T("&Redo"); redoItem.ShortcutKeys = Keys.Control | Keys.Y; redoItem.Click += delegate { Undo(true); };
            edit.DropDownItems.Add(undoItem);
            edit.DropDownItems.Add(redoItem);
            edit.DropDownItems.Add(new ToolStripSeparator());
            AddClipboardItems(edit);
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item(Lang.T("D&uplicate"), Keys.Control | Keys.D, delegate { Duplicate(); }));
            edit.DropDownItems.Add(new ToolStripMenuItem(Lang.T("&Delete"), null, delegate { Delete(); }) { ShortcutKeyDisplayString = Lang.T("Del") });
            edit.DropDownItems.Add(new ToolStripSeparator());
            edit.DropDownItems.Add(Item(Lang.T("Select &all"), Keys.Control | Keys.A, delegate { SelectAll(); }));

            var insert = new ToolStripMenuItem(Lang.T("&Insert"));
            foreach (var tool in InsertTools())
            {
                var t = tool;
                insert.DropDownItems.Add(Item(t.MenuText, t.Keys, delegate { t.Insert(); }));
            }

            var arrange = new ToolStripMenuItem(Lang.T("&Arrange"));
            arrange.DropDownItems.AddRange(AlignItems().ToArray());
            arrange.DropDownItems.Add(new ToolStripSeparator());
            arrange.DropDownItems.Add(Item(Lang.T("R&otate 90°"), Keys.Control | Keys.R, delegate { Rotate(); }));
            var resetItem = new ToolStripMenuItem(Lang.T("Re&set rotation"), null, delegate { ResetRotation(); });
            arrange.DropDownItems.Add(resetItem);
            arrange.DropDownItems.Add(new ToolStripSeparator());
            arrange.DropDownItems.AddRange(LayerItems().ToArray());
            arrange.DropDownOpening += delegate { UpdateAlignItems(arrange.DropDownItems); UpdateLayerItems(arrange.DropDown); resetItem.Enabled = Turned; };

            // The view: direction and zoom (the label settings in the panel and the zoom in the status bar do the same).
            var view = new ToolStripMenuItem(Lang.T("&View"));
            view.DropDownItems.Add(ToolNamesItem());
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.Add(Item(Lang.T("&Turn view"), Keys.Control | Keys.Shift | Keys.R, delegate { TurnView(); }));
            view.DropDownItems.Add(new ToolStripSeparator());
            view.DropDownItems.AddRange(ZoomItems(true).ToArray());
            view.DropDown.ShowItemToolTips = true;
            view.DropDownOpening += delegate { UpdateToolNamesItem((ToolStripMenuItem)view.DropDownItems[0]); UpdateZoomItems(view.DropDownItems); };

            var printerMenu = new ToolStripMenuItem(Lang.T("&Printer"));
            highResItem.Text = Lang.T("300 × 600 dpi (sharper, slower)");
            highResItem.Click += delegate { SetHighRes(!highRes); };
            printerMenu.DropDownItems.Add(highResItem);
            printerMenu.DropDownItems.Add(new ToolStripSeparator());
            rollItem.Text = Lang.T("Check the loaded &roll");
            rollItem.Click += delegate { CheckRoll(); };
            printerMenu.DropDownItems.Add(rollItem);
            printerMenu.DropDownItems.Add(Item(Lang.T("Printer status"), Keys.F6, delegate { ShowStatus(); }));
            // As in QL Label Formats: the printer monitor, then the Printer setup window.
            printerMenu.DropDownItems.Add(PrinterMenu.NotifyItem(printerMenu, this, AppTitle, null));
            printerMenu.DropDownItems.Add(new ToolStripSeparator());
            printerMenu.DropDownItems.Add(PrinterMenu.SetupItem(this, RefreshPrinters));
            printerMenu.DropDown.ShowItemToolTips = true;

            // As in QL Label Formats: the other program, then the language of all QL programs.
            var extras = new ToolStripMenuItem(Lang.T("&Tools"));
            extras.DropDownItems.Add(new ToolStripMenuItem(Lang.T("QL Label Formats"), null, delegate { OpenLabelFormats(); })
                { ToolTipText = Lang.T("Add, change and sort the label formats (QL Label Formats)") });
            extras.DropDownItems.Add(new ToolStripSeparator());
            extras.DropDownItems.Add(LanguageSwitch.Menu(SwitchLanguage));
            extras.DropDownItems.Add(Theme.Menu());

            var help = new ToolStripMenuItem(Lang.T("&Help"));
            // F1 opens the page of what is selected (ProcessCmdKey); the menu entry always opens the contents.
            help.DropDownItems.Add(new ToolStripMenuItem(Lang.T("QL Label Maker Help"), null, delegate { HelpPages.Open(this, HelpPages.LabelMaker); })
                { ShortcutKeyDisplayString = "F1" });
            help.DropDownItems.Add(new ToolStripSeparator());
            help.DropDownItems.Add(Item(Lang.T("About QL Unidrv"), Keys.None, delegate { AboutForm.ShowAbout(this, AppTitle); }));
            menu.Items.AddRange(new ToolStripItem[] { file, edit, view, insert, arrange, printerMenu, extras, help });
            menuBar = menu;
            return menu;
        }

        static ToolStripMenuItem Item(string text, Keys keys, EventHandler click)
        {
            var item = new ToolStripMenuItem(text, null, click);
            if (keys != Keys.None) item.ShortcutKeys = keys;
            return item;
        }
    }
}
