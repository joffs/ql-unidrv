// Label editor window (EditorForm.cs), part: cut, copy, paste and drag and drop. What a paste or a drop offers is read by
// EditorClipboard (its order); this places it on the label. Copy writes our own objects (QLUnidrv.LabelObjects) plus
// a 300 dpi picture of them (PNG and DIB) for other programs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        const string ObjectsFormatName = "QL label objects";

        // ---------- commands ----------

        // The edit field with the focus (a text or number box of the panel): Ctrl+X/C/V work on its text there.
        TextBoxBase FocusedField()
        {
            var field = ActiveControl as TextBoxBase;
            if (field == null) { var up = ActiveControl as UpDownBase; if (up != null) field = up.Controls.OfType<TextBoxBase>().FirstOrDefault(); }
            return field;
        }

        void Cut()
        {
            var field = FocusedField();
            if (field != null) { field.Cut(); return; }
            if (canvas.Editing != null) { canvas.EditCopy(true); return; }
            if (CopyObjects()) Delete();
        }

        void Copy()
        {
            var field = FocusedField();
            if (field != null) { field.Copy(); return; }
            if (canvas.Editing != null) { canvas.EditCopy(false); return; }
            CopyObjects();
        }

        // at: where the pasted objects' top left corner goes (mm, the empty-spot menu); null: the usual place.
        void Paste(bool plainText, PointF? at)
        {
            var field = FocusedField();
            if (field != null && at == null)
            {
                if (plainText) { var t = EditorClipboard.ReadPlainText(EditorClipboard.FromClipboard() ?? Empty); if (t.Kind == ClipKind.Text) field.SelectedText = t.Text; }
                else field.Paste();
                return;
            }
            if (canvas.Editing != null && at == null) { canvas.EditPaste(plainText); return; }
            var source = EditorClipboard.FromClipboard();
            if (source == null) { labelStatus.Text = Lang.T("Another program is using the clipboard. Try again."); return; }
            ClipContent c;
            try { c = plainText ? EditorClipboard.ReadPlainText(source) : EditorClipboard.Read(source); }
            catch (Exception ex) { Failed(this, Lang.T("The clipboard's content can't be pasted."), ex); return; }
            Place(c, at);
        }

        static readonly IClipSource Empty = new NoSource();
        sealed class NoSource : IClipSource { public bool Has(string f) { return false; } public object Get(string f) { return null; } }

        bool CanPaste() { return EditorClipboard.MayPaste(EditorClipboard.FromClipboard()); }
        bool CanPastePlainText()
        {
            var source = EditorClipboard.FromClipboard();
            return source != null && EditorClipboard.ReadPlainText(source).Kind == ClipKind.Text;
        }

        // The four commands (Edit menu, both context menus): text, keys, action.
        ToolStripMenuItem CutItem() { return Command(Lang.T("Cu&t"), Keys.Control | Keys.X, Cut); }
        ToolStripMenuItem CopyItem() { return Command(Lang.T("&Copy"), Keys.Control | Keys.C, Copy); }
        ToolStripMenuItem PasteItem(PointF? at) { return Command(Lang.T("&Paste"), Keys.Control | Keys.V, () => Paste(false, at)); }
        ToolStripMenuItem PastePlainItem(PointF? at) { return Command(Lang.T("Paste as plain te&xt"), Keys.Control | Keys.Shift | Keys.V, () => Paste(true, at)); }

        // In a context menu the shortcut is shown only (the menu bar handles the keys).
        static ToolStripMenuItem Command(string text, Keys keys, Action click)
        {
            return new ToolStripMenuItem(text, null, delegate { click(); }) { ShortcutKeyDisplayString = KeyText(keys), Tag = keys };
        }

        // The Edit menu's items after Undo/Redo. Their state is set when the menu opens and all are enabled again when it
        // closes, so the shortcuts always work (a field may have the focus by then).
        void AddClipboardItems(ToolStripMenuItem edit)
        {
            var items = new[] { CutItem(), CopyItem(), PasteItem(null), PastePlainItem(null) };
            foreach (var i in items) { i.ShortcutKeys = (Keys)i.Tag; i.ShortcutKeyDisplayString = KeyText(i.ShortcutKeys); edit.DropDownItems.Add(i); }
            edit.DropDownOpening += delegate
            {
                bool field = FocusedField() != null, any = canvas.Selection.Count > 0;
                items[0].Enabled = items[1].Enabled = field || any;
                items[2].Enabled = field || CanPaste();
                items[3].Enabled = CanPastePlainText();
            };
            edit.DropDown.Closed += delegate { foreach (var i in items) i.Enabled = true; };
        }

        // The object menu's first items (right click on an object).
        IEnumerable<ToolStripItem> ObjectClipboardItems()
        {
            var paste = PasteItem(null); paste.Enabled = CanPaste();
            var plain = PastePlainItem(null); plain.Enabled = CanPastePlainText();
            return new ToolStripItem[] { CutItem(), CopyItem(), paste, plain, new ToolStripSeparator() };
        }

        ContextMenuStrip canvasMenu;

        // Right click on an empty spot (or the menu key with nothing selected): paste there, select all.
        void ShowCanvasMenu(Point? at)
        {
            if (layout == null) return;
            var point = at ?? ViewCentre();
            PointF? spot = at.HasValue ? canvas.ToMm(at.Value) : (PointF?)null;
            if (canvasMenu != null) canvasMenu.Dispose();
            var menu = canvasMenu = new ContextMenuStrip { Font = menuBar.Font };
            var paste = PasteItem(spot); paste.Enabled = CanPaste();
            var plain = PastePlainItem(spot); plain.Enabled = CanPastePlainText();
            menu.Items.Add(paste);
            menu.Items.Add(plain);
            menu.Items.Add(new ToolStripSeparator());
            var all = Command(Lang.T("Select &all"), Keys.Control | Keys.A, SelectAll);
            all.Enabled = doc.Objects.Count > 0;
            menu.Items.Add(all);
            menu.Show(canvas, point);
        }

        // The middle of the canvas part that is shown (canvas pixels).
        Point ViewCentre()
        {
            var r = host.ClientRectangle;
            return canvas.PointToClient(host.PointToScreen(new Point(r.Width / 2, r.Height / 2)));
        }

        // ---------- copy ----------

        bool CopyObjects()
        {
            var these = SelectedObjects;
            if (these.Count == 0) return false;
            byte[] own = System.Text.Encoding.UTF8.GetBytes(ObjectsJson(these)), dib = null;
            using (var picture = PictureOf(these))
                if (picture != null) dib = Dib24(picture);
            // The EMF first (drawing programs and Word: text stays text, shapes stay vectors), a DIB for Paint & co.
            if (EditorClipboard.SetCopy(Handle, EmfOf(these), own, dib)) return true;
            labelStatus.Text = Lang.T("Another program is using the clipboard. Try again.");
            return false;
        }

        // The objects as an EMF (EditorRender.RenderEmf), in the same box as PictureOf; null without a layout.
        byte[] EmfOf(List<LabelObject> these)
        {
            if (layout == null) return null;
            var box = RectangleF.Inflate(EditorCanvas.BoxOf(these), 0.5f, 0.5f);
            if (box.Width > 1000 || box.Height > 1000) return null;
            try { return EditorRender.RenderEmf(these, layout, box, DateTime.Now); }
            catch (Exception) { return null; }
        }

        // The objects as they print, at 300 dpi (white around them); null without a layout or when it would be huge.
        Bitmap PictureOf(List<LabelObject> these)
        {
            if (layout == null) return null;
            var box = RectangleF.Inflate(EditorCanvas.BoxOf(these), 0.5f, 0.5f);
            const int dpi = 300;
            int w = (int)Math.Ceiling(box.Width / 25.4 * dpi), h = (int)Math.Ceiling(box.Height / 25.4 * dpi);
            if (w < 1 || h < 1 || w > 10000 || h > 10000) return null;
            var l = layout.Copy();
            l.Printable = box; l.DpiX = l.DpiY = dpi; l.DotsX = w; l.DotsY = h;
            var part = new LabelDoc();
            part.Objects.AddRange(these);
            try { return EditorRender.Render(part, l); }
            catch (Exception) { return null; }
        }

        // Our own clipboard format: the objects as in a .qllabel file.
        internal static string ObjectsJson(IEnumerable<LabelObject> these)
        {
            var json = new Dictionary<string, object> { { "format", ObjectsFormatName }, { "version", LabelDoc.Version }, { "objects", these.Select(o => o.ToJson()).ToList() } };
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(json);
        }

        // ...and back (objects of unknown types and anything else left out; FromJson keeps sizes in range, cleans SVGs
        // and checks EMFs).
        internal static List<LabelObject> ObjectsFrom(string json)
        {
            var list = new List<LabelObject>();
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>; }
            catch (ArgumentException) { return list; }
            catch (InvalidOperationException) { return list; }
            object v;
            var objects = d != null && LabelObject.Str(d, "format", "") == ObjectsFormatName && d.TryGetValue("objects", out v) ? v as IEnumerable : null;
            if (objects != null)
                foreach (var item in objects)
                {
                    var od = item as Dictionary<string, object>;
                    var o = od != null ? LabelObject.FromJson(od) : null;
                    if (o != null) list.Add(o);
                }
            return list;
        }

        // A packed 24 bpp DIB (CF_DIB) of a picture, bottom-up.
        internal static byte[] Dib24(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height, stride = (w * 3 + 3) & ~3;
            var d = new byte[40 + stride * h];
            BitConverter.GetBytes(40).CopyTo(d, 0);
            BitConverter.GetBytes(w).CopyTo(d, 4);
            BitConverter.GetBytes(h).CopyTo(d, 8);
            BitConverter.GetBytes((short)1).CopyTo(d, 12);
            BitConverter.GetBytes((short)24).CopyTo(d, 14);
            BitConverter.GetBytes(stride * h).CopyTo(d, 20);
            using (var rgb = bmp.Clone(new Rectangle(0, 0, w, h), PixelFormat.Format24bppRgb))
            {
                var data = rgb.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                try
                {
                    for (int y = 0; y < h; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, d, 40 + (h - 1 - y) * stride, w * 3);
                }
                finally { rgb.UnlockBits(data); }
            }
            return d;
        }

        // ---------- paste and drop ----------

        // Puts what was read on the label: at (mm) the top left corner of the new objects; null: our own objects at their
        // place (2 mm on while the spot is taken), anything else in the middle of the label (of the view without one).
        // Pictures keep their size (1:1); one bigger than the label hangs over its edges.
        void Place(ClipContent c, PointF? at)
        {
            string note = null;
            List<LabelObject> made;
            try { made = Make(c, ref note); }
            catch (Exception ex) { if (c.Picture != null) c.Picture.Dispose(); Failed(this, Lang.T("The clipboard's content can't be pasted."), ex); return; }
            if (c.Picture != null) c.Picture.Dispose();
            if (made == null) return;   // done otherwise (a label file opened)
            if (made.Count == 0) { labelStatus.Text = note ?? Lang.T("Nothing on the clipboard can be pasted here."); return; }
            var box = EditorCanvas.BoxOf(made);
            float dx = 0, dy = 0;
            if (at.HasValue) { dx = at.Value.X - box.X; dy = at.Value.Y - box.Y; }
            else if (c.Kind == ClipKind.Objects)
            {
                for (int i = 0; i < 50 && made.Any(m => doc.Objects.Any(o => o.Type == m.Type && Math.Abs(o.X - (m.X + dx)) < 0.05 && Math.Abs(o.Y - (m.Y + dy)) < 0.05)); i++)
                { dx += 2; dy += 2; }
            }
            else
            {
                var centre = layout != null ? new PointF((float)layout.ViewWidthMm / 2, (float)layout.ViewHeightMm / 2) : canvas.ToMm(ViewCentre());
                dx = centre.X - (box.X + box.Width / 2); dy = centre.Y - (box.Y + box.Height / 2);
            }
            // kept on the label where they fit (what is bigger stays centred or at the point)
            if (layout != null && (c.Kind != ClipKind.Objects || at.HasValue))
            {
                float right = (float)layout.ViewWidthMm, bottom = (float)layout.ViewHeightMm;
                if (box.Width <= right) dx = Math.Max(-box.X, Math.Min(dx, right - box.Right));
                if (box.Height <= bottom) dy = Math.Max(-box.Y, Math.Min(dy, bottom - box.Bottom));
            }
            foreach (var o in made) { o.X = Math.Round(o.X + dx, 2); o.Y = Math.Round(o.Y + dy, 2); }
            PushUndo(null);
            doc.Objects.AddRange(made);
            canvas.Select(made);
            ChangedAll();
            canvas.Focus();
            if (note != null) labelStatus.Text = note;
            OfferFit(made);
        }

        // The objects for what was read (positions from 0 unless they are our own); null: nothing to place (a .qllabel
        // file was opened instead).
        List<LabelObject> Make(ClipContent c, ref string note)
        {
            var list = new List<LabelObject>();
            switch (c.Kind)
            {
                case ClipKind.Objects: list.AddRange(ObjectsFrom(c.ObjectsJson)); break;
                case ClipKind.Svg:
                    {
                        // its text (Affinity puts its copies here as SVG) as editable text objects at their places, not
                        // the curves the picture would make of them; the rest of a whole label as a picture on the same
                        // frame, under the text (Import/SvgText.cs)
                        var texts = SvgText.Read(c.SvgSource, c.SizeMm, SvgQuirks.For(c.Source));
                        if (texts != null && texts.Objects.Count > 0)
                        {
                            ImageObject rest = null;
                            if (!texts.OnlyText && texts.Rest != null)
                            {
                                // the text import's extra: whatever goes wrong there, the whole picture is pasted instead
                                try { rest = AsCopied(ImageObject.FromSvg(SvgPicture.Clean(texts.Rest)), c.SizeMm); }
                                catch (Exception) { rest = null; }
                            }
                            if (texts.OnlyText || rest != null)
                            {
                                if (rest != null) list.Add(rest);
                                list.AddRange(texts.Objects);
                                if (texts.Missing.Count > 0)
                                    note = Lang.F("This text prints with a substitute font: {0} isn't installed on this PC.", string.Join(", ", texts.Missing));
                                break;
                            }
                        }
                        list.Add(AsCopied(ImageObject.FromSvg(c.Svg), c.SizeMm));
                        break;
                    }
                case ClipKind.Emf:
                    if (c.EmfKind == EmfPicture.Content.TextOnly)
                    {
                        // text only (CorelDRAW's text): editable text objects with their fonts, at their places
                        // (CorelDRAW: its CMX laid over the EMF for the real paragraphs, Import/TextImport.cs)
                        List<string> missing;
                        List<TextObject> texts;
                        string how;
                        try { texts = TextImport.FromEmf(c, out missing, out how); }
                        catch (Exception) { texts = null; missing = null; }   // the picture then
                        if (texts != null && texts.Count > 0)
                        {
                            list.AddRange(texts);
                            if (missing.Count > 0)
                                note = Lang.F("This text prints with a substitute font: {0} isn't installed on this PC.", string.Join(", ", missing));
                            break;
                        }
                    }
                    {
                        // a whole label from CorelDRAW: its texts as text objects, its drawing as picture(s) below
                        // (and above) them in its own z-order (Import/EmfLayers.cs)
                        List<string> missing;
                        string how;
                        List<LabelObject> split = null;
                        try
                        {
                            var parts = TextImport.FromLabel(c, out missing, out how);
                            if (parts != null)
                                split = parts.Select(part =>
                                {
                                    var pic = part as EmfLayers.Picture;
                                    return pic != null ? AtPlace(ImageObject.FromEmf(pic.Emf, c.Cmx), pic.Mm) : (LabelObject)part;
                                }).ToList();
                        }
                        catch (Exception) { split = null; missing = null; }   // the whole picture then
                        if (split != null)
                        {
                            list.AddRange(split);
                            if (missing.Count > 0)
                                note = Lang.F("This text prints with a substitute font: {0} isn't installed on this PC.", string.Join(", ", missing));
                            break;
                        }
                    }
                    list.Add(AsCopied(ImageObject.FromEmf(c.Emf, c.Cmx), c.SizeMm));
                    if (c.AlsoText) note = Lang.T("Pasted as a picture. To edit the text: Edit > Paste as plain text (Ctrl+Shift+V).");
                    break;
                case ClipKind.Picture: list.Add(AsCopied(ImageObject.FromBitmap(c.Picture), c.SizeMm)); break;
                case ClipKind.Files:
                    var label = c.Files.FirstOrDefault(f => string.Equals(Path.GetExtension(f), LabelDoc.Extension, StringComparison.OrdinalIgnoreCase));
                    if (label != null) { if (!printing) OpenFile(label); return null; }
                    foreach (var f in c.Files)
                    {
                        var img = LoadPicture(this, f);
                        if (img != null) list.Add(AsCopied(img, img.OwnSizeMm));
                    }
                    SideBySide(list);
                    break;
                case ClipKind.Rtf:
                    // Word and other RTF: one text object with the words' fonts, sizes, bold, italic and underline
                    List<string> gone;
                    list.Add(RtfRuns.Object(RtfRuns.Read(c.Rtf, out gone)));
                    if (gone.Count > 0)
                        note = Lang.F("This text prints with a substitute font: {0} isn't installed on this PC.", string.Join(", ", gone));
                    break;
                case ClipKind.Text:
                    list.Add(TextFrom(c.Text));
                    break;
            }
            return list;
        }

        // A new picture's size, as on Insert > Picture (80 % of the printable area).
        ImageObject Sized(ImageObject img)
        {
            var size = PictureSize(img.W, img.H);
            img.X = 0; img.Y = 0; img.W = Math.Round(size.Width, 1); img.H = Math.Round(size.Height, 1);
            return img;
        }

        // A pasted or dropped picture at the size it was copied or saved at (mm), even when bigger than the label;
        // without one (an EMF with an empty frame) as on Insert > Picture.
        ImageObject AsCopied(ImageObject img, SizeF? mm)
        {
            if (mm == null) return Sized(img);
            double k = Math.Min(1, LabelObject.MaxMm / Math.Max(mm.Value.Width, mm.Value.Height));   // beyond 10 m: the panel's (and file's) most
            img.X = 0; img.Y = 0; img.W = Math.Round(mm.Value.Width * k, 1); img.H = Math.Round(mm.Value.Height * k, 1);
            if (img.W < 0.1) img.W = 0.1;
            if (img.H < 0.1) img.H = 0.1;
            return img;
        }

        // A part of a pasted label at its place on the copied frame (mm).
        static ImageObject AtPlace(ImageObject img, RectangleF mm)
        {
            img.X = Math.Round(mm.X, 2); img.Y = Math.Round(mm.Y, 2);
            img.W = Math.Max(0.1, Math.Round(mm.Width, 2)); img.H = Math.Max(0.1, Math.Round(mm.Height, 2));
            return img;
        }

        // Several dropped files: in a row, 2 mm apart, each at its own size.
        static void SideBySide(List<LabelObject> pictures)
        {
            double x = 0;
            foreach (var p in pictures) { p.X = x; x += p.W + 2; }
        }

        // Pasted text: one line (artistic text), or a text frame when it has line breaks.
        internal static TextObject TextFrom(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n');   // "\n" breaks, as the text box writes them
            if (text.Contains("\n"))
            {
                var t = new TextObject { Text = text, Size = 12, Kind = TextKind.Frame, W = 40, H = 12 };
                EditorRender.FitBox(t);
                return t;
            }
            var one = new TextObject { Text = text, Size = 14 };
            EditorRender.FitText(one);
            return one;
        }

        // Drops: files from Explorer, pictures and text from other programs (only the drop's own data).
        void SetUpDrop()
        {
            canvas.AllowDrop = true;
            DragEventHandler over = (s, e) =>
            {
                e.Effect = !printing && EditorClipboard.MayPaste(EditorClipboard.FromDrop(e.Data)) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            canvas.DragEnter += over;
            canvas.DragOver += over;
            canvas.DragDrop += (s, e) =>
            {
                ClipContent c;
                try { c = EditorClipboard.Read(EditorClipboard.FromDrop(e.Data) ?? Empty); }
                catch (Exception) { return; }
                var at = canvas.ToMm(canvas.PointToClient(new Point(e.X, e.Y)));
                // after the drop: the program the data came from isn't kept waiting (a message box, opening a file)
                BeginInvoke((MethodInvoker)delegate { Activate(); Place(c, at); });
            };
        }
    }
}
