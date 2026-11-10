// Label editor window (EditorForm.cs), part: File > Recent labels (2026-11-09, mockup variant A,
// out/mockups/recent-files/menu.html). Up to 15 labels (RecentLabels.cs), the newest first, the digits 1-9 as access
// keys, the folder grey beside the name (labels of the same name differ there). An entry's x (on hover) or Del removes
// it and the menu stays open; "Clear list..." asks first. A missing local file is grey with "not found" when the menu
// opens; a network folder is looked at only when the entry is clicked (a sleeping server never holds up the menu), and
// "folder not reachable" offers to try again, it removes nothing by itself.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        ToolStripMenuItem recentMenu;
        bool keepMenuOpen;       // an x was clicked: the menus stay open (their Closing is cancelled once)
        bool probing;            // a clicked network entry is being looked at
        // folders a click couldn't reach (this window, until a click reaches them): shown "folder not reachable"
        readonly HashSet<string> unreachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The questions of this menu (headline, text, buttons); the UI test answers them here.
        internal Func<string, string, KeyValuePair<string, DialogResult>[], DialogResult> RecentAsk { get; set; }

        ToolStripMenuItem RecentMenu(ToolStripMenuItem file)
        {
            recentMenu = new ToolStripMenuItem(Lang.T("&Recent labels"));
            recentMenu.DropDown = new RecentDropDown(this) { ShowImageMargin = false, ShowCheckMargin = false, ShowItemToolTips = true };
            recentMenu.DropDownItems.Add(new ToolStripMenuItem(Lang.T("No labels opened yet")) { Enabled = false });   // the arrow
            recentMenu.DropDownOpening += delegate { FillRecent(); };
            KeepOpenAfterX(recentMenu.DropDown);
            KeepOpenAfterX(file.DropDown);
            return recentMenu;
        }

        void KeepOpenAfterX(ToolStripDropDown d)
        {
            d.Closing += (s, e) => { if (keepMenuOpen && e.CloseReason == ToolStripDropDownCloseReason.ItemClicked) e.Cancel = true; };
            d.Closed += delegate { keepMenuOpen = false; };
        }

        void FillRecent()
        {
            var d = recentMenu.DropDown;
            d.SuspendLayout();
            foreach (var old in d.Items.Cast<ToolStripItem>().ToList()) { d.Items.Remove(old); old.Dispose(); }
            var list = RecentLabels.Load();
            if (list.Count == 0) d.Items.Add(new ToolStripMenuItem(Lang.T("No labels opened yet")) { Enabled = false });
            for (int i = 0; i < list.Count; i++)
            {
                string p = list[i], mark = null;
                if (RecentLabels.OnNetwork(p)) { if (unreachable.Contains(Folder(p))) mark = Lang.T("folder not reachable"); }
                else if (!Exists(p)) mark = Lang.T("not found");
                var item = new RecentItem(p, i, mark, Lang.T("Remove from the list (Del)"));
                item.Click += delegate { if (!item.OverX) OpenRecent(item.Path); };
                item.XDown += delegate { keepMenuOpen = true; };
                item.XClick += delegate { BeginInvoke((MethodInvoker)delegate { RemoveRecent(item.Path, false); }); };
                d.Items.Add(item);
            }
            if (list.Count > 0)
            {
                d.Items.Add(new ToolStripSeparator());
                d.Items.Add(new ToolStripMenuItem(Lang.T("Clear &list..."), null, delegate { ClearRecent(); }));
            }
            foreach (var item in d.Items.OfType<RecentItem>()) item.FitMenu();
            d.ResumeLayout();
        }

        static string Folder(string path) { try { return Path.GetDirectoryName(path) ?? ""; } catch (Exception) { return ""; } }
        static bool Exists(string path) { try { return File.Exists(path); } catch (Exception) { return false; } }

        DialogResult AskRecent(string headline, string text, params KeyValuePair<string, DialogResult>[] choices)
        {
            if (RecentAsk != null) return RecentAsk(headline, text, choices);
            return QlMessage.Choose(this, AppTitle, headline, text, MessageBoxIcon.Question, choices);
        }

        // An entry removed (its x or Del): the menu stays open, the next entry is marked when it came from the keyboard.
        void RemoveRecent(string path, bool keyboard)
        {
            int at = recentMenu.DropDown.Items.OfType<RecentItem>().ToList().FindIndex(i => RecentLabels.Same(i.Path, path));
            RecentLabels.Remove(path);
            labelStatus.Text = Lang.F("“{0}” removed from the list.", Path.GetFileNameWithoutExtension(path));
            keepMenuOpen = false;
            if (!recentMenu.DropDown.Visible) return;
            FillRecent();
            var items = recentMenu.DropDown.Items.OfType<RecentItem>().ToList();
            if (keyboard && items.Count > 0) items[Math.Max(0, Math.Min(at, items.Count - 1))].Select();
        }

        void ClearRecent()
        {
            if (AskRecent(Lang.T("Clear the list of recently opened labels?"), Lang.T("The labels themselves are kept."),
                          QlMessage.Choice(Lang.T("Clear"), DialogResult.OK), QlMessage.Choice(Lang.T("Cancel"), DialogResult.Cancel)) != DialogResult.OK) return;
            RecentLabels.Clear();
            labelStatus.Text = Lang.T("List cleared.");
        }

        // 0: there; 1: not found (the folder or drive is there, or a local one is gone); 2: the network folder can't be reached.
        static int Probe(string file)
        {
            if (Exists(file)) return 0;
            if (!RecentLabels.OnNetwork(file)) return 1;
            try { return Directory.Exists(Path.GetPathRoot(file)) ? 1 : 2; } catch (Exception) { return 2; }
        }

        internal static int ProbeSeconds = 8;   // a server that doesn't answer within this counts as not reachable

        async void OpenRecent(string file)
        {
            if (printing || probing) return;
            string name = Path.GetFileNameWithoutExtension(file), folder = Folder(file);
            while (true)
            {
                int state;
                if (RecentLabels.OnNetwork(file))
                {
                    probing = true;
                    UseWaitCursor = true;
                    try
                    {
                        var probe = Task.Run(() => Probe(file));
                        state = await Task.WhenAny(probe, Task.Delay(ProbeSeconds * 1000)) == probe ? probe.Result : 2;
                    }
                    finally { probing = false; UseWaitCursor = false; }
                }
                else state = Probe(file);
                if (IsDisposed) return;
                if (state == 0)
                {
                    unreachable.Remove(folder);
                    if (!printing) OpenFile(file);   // a tab of its own, or its open tab
                    return;
                }
                if (state == 1)
                {
                    unreachable.Remove(folder);
                    if (AskRecent(Lang.F("“{0}” was not found.", name), Lang.T("It may have been moved or deleted.\n\nRemove it from the list?"),
                                  QlMessage.Choice(Lang.T("Remove"), DialogResult.OK), QlMessage.Choice(Lang.T("Cancel"), DialogResult.Cancel)) == DialogResult.OK)
                        RemoveRecent(file, false);
                    return;
                }
                unreachable.Add(folder);
                var answer = AskRecent(Lang.F("The folder {0} can't be reached right now.", folder), Lang.T("Check the network connection and try again."),
                                       QlMessage.Choice(Lang.T("Retry"), DialogResult.Retry), QlMessage.Choice(Lang.T("Remove"), DialogResult.OK),
                                       QlMessage.Choice(Lang.T("Cancel"), DialogResult.Cancel));
                if (answer == DialogResult.OK) RemoveRecent(file, false);
                if (answer != DialogResult.Retry) return;
            }
        }

        // The submenu: Del removes the marked entry (the menu stays open).
        sealed class RecentDropDown : ToolStripDropDownMenu
        {
            readonly EditorForm form;
            public RecentDropDown(EditorForm form) { this.form = form; }

            internal bool DeleteKey()
            {
                var item = Items.OfType<RecentItem>().FirstOrDefault(i => i.Selected);
                if (item == null) return false;
                form.RemoveRecent(item.Path, true);
                return true;
            }

            protected override bool ProcessDialogKey(Keys keyData)
            {
                if (keyData == Keys.Delete && DeleteKey()) return true;
                return base.ProcessDialogKey(keyData);
            }

            protected override bool ProcessCmdKey(ref Message m, Keys keyData)
            {
                if (keyData == Keys.Delete && DeleteKey()) return true;
                return base.ProcessCmdKey(ref m, keyData);
            }
        }

        // One entry: digit (underlined, the access key), name, the folder grey beside it, a grey "not found" / "folder not
        // reachable", and an x at the right while it is marked (mouse or keyboard).
        internal sealed class RecentItem : ToolStripMenuItem
        {
            public readonly string Path;
            public readonly string Mark;
            readonly string name, folder, number, pathTip, xTip;
            public bool OverX { get; private set; }
            public event EventHandler XDown, XClick;

            public RecentItem(string path, int index, string mark, string xTip)
            {
                Path = path;
                Mark = mark;
                this.xTip = xTip;
                name = System.IO.Path.GetFileNameWithoutExtension(path);
                folder = Folder(path);
                number = index < 9 ? (index + 1).ToString() : "";
                Text = (number.Length > 0 ? "&" + number + " " : "") + name.Replace("&", "&&");
                AccessibleName = name + ", " + folder + (mark != null ? ", " + mark : "");
                ToolTipText = pathTip = path;
            }

            int Em { get { return Font.Height; } }
            const TextFormatFlags Flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            int NameMax { get { return Em * 14; } }
            int FolderMax { get { return Em * 16; } }
            int Gap { get { return Em * 3 / 4; } }
            int NumberWidth { get { return TextRenderer.MeasureText("8", Font, Size.Empty, Flags).Width + Em / 2; } }
            int XWidth { get { return Em * 3 / 2; } }
            int W(string s, int max) { return string.IsNullOrEmpty(s) ? 0 : Math.Min(max, TextRenderer.MeasureText(s, Font, Size.Empty, Flags).Width); }

            int Drawn { get { return Em / 2 + NumberWidth + W(name, NameMax) + Gap + W(folder, FolderMax) + (Mark != null ? Gap + W(Mark, Em * 12) : 0) + Gap + XWidth; } }

            // A menu measures its entries by text and shortcut only, and makes every entry that wide: a shortcut text of
            // spaces (never drawn: OnPaint draws everything) makes room for the folder, the mark and the x. Once in the menu
            // (its font).
            public void FitMenu()
            {
                ShortcutKeyDisplayString = null;
                int text = TextRenderer.MeasureText(Text.Replace("&&", "\u0001").Replace("&", "").Replace("\u0001", "&"), Font).Width;
                int space = Math.Max(1, TextRenderer.MeasureText("\u2003\u2003\u2003\u2003", Font).Width / 4);
                int missing = Drawn - text - Em * 2;
                if (missing > 0) ShortcutKeyDisplayString = new string('\u2003', missing / space + 1);
            }

            Rectangle XRect { get { return new Rectangle(Width - XWidth - Em / 4, 0, XWidth, Height); } }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool over = XRect.Contains(e.Location);
                if (over != OverX) { OverX = over; ToolTipText = over ? xTip : pathTip; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e) { if (OverX) { OverX = false; ToolTipText = pathTip; Invalidate(); } base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                OverX = XRect.Contains(e.Location);
                if (OverX && XDown != null) XDown(this, EventArgs.Empty);
                base.OnMouseDown(e);
            }

            protected override void OnClick(EventArgs e)
            {
                if (OverX) { if (XClick != null) XClick(this, EventArgs.Empty); return; }
                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                if (Owner != null) Owner.Renderer.DrawMenuItemBackground(new ToolStripItemRenderEventArgs(g, this));
                bool onHighlight = Theme.HighContrast && Selected;
                Color text = Theme.Of(onHighlight ? Role.OnPrimary : Role.Text), dim = Theme.Of(onHighlight ? Role.OnPrimary : Role.Dim);
                if (Mark != null) text = dim;
                int x = Em / 2, h = Height;
                if (number.Length > 0)
                    using (var under = new Font(Font, Font.Style | FontStyle.Underline))
                        TextRenderer.DrawText(g, number, under, new Rectangle(x, 0, NumberWidth, h), text, Flags);
                x += NumberWidth;
                int nw = W(name, NameMax);
                TextRenderer.DrawText(g, name, Font, new Rectangle(x, 0, nw, h), text, Flags | TextFormatFlags.EndEllipsis);
                x += nw + Gap;
                int fw = W(folder, FolderMax);
                TextRenderer.DrawText(g, folder, Font, new Rectangle(x, 0, fw, h), dim, Flags | TextFormatFlags.PathEllipsis);
                x += fw;
                if (Mark != null)
                {
                    x += Gap;
                    using (var italic = new Font(Font, Font.Style | FontStyle.Italic))
                        TextRenderer.DrawText(g, Mark, italic, new Rectangle(x, 0, W(Mark, Em * 12) + Em, h), dim, Flags);
                }
                if (Selected)
                {
                    var r = XRect;
                    if (OverX)
                        using (var b = new SolidBrush(Theme.Of(onHighlight ? Role.OnPrimary : Role.Hover)))
                            g.FillRectangle(b, r.X + 2, r.Y + 2, r.Width - 4, r.Height - 4);
                    Color xc = OverX && onHighlight ? Theme.Of(Role.Selection) : onHighlight ? Theme.Of(Role.OnPrimary) : Theme.Of(Role.Text);
                    TextRenderer.DrawText(g, "✕", Font, r, xc, Flags | TextFormatFlags.HorizontalCenter);
                }
            }
        }
    }
}
