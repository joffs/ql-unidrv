// Editing a text on the label in QL Label Maker: double-click starts editing at the clicked
// place, typing as Windows sends it (WM_CHAR: a dead key's ê, AltGr's @, an emoji's surrogate pair), Ctrl+B on a word,
// Esc ends, one undo step per session, F2 / Enter / typing on a selected text start editing, a new text is only
// selected (an arrow key moves it, typing replaces its placeholder), an emptied text goes, fields show as codes while editing, 90° and 180° text (hit test and
// keys follow the text), overflow shown grey below the frame or scrolled inside it (View option), copy and paste of
// formatted characters, the menus while editing, the panel within 690 px, the time per key. Prints CHECK lines;
// changes nothing (nothing saved or printed). Runs only through deskrun.exe (a desktop of its own: no mouse, no focus
// taken), with --lock clipboard (it copies and pastes). A watchdog ends it after 240 s.
// Run by inline-text-ui.ps1:  inline_text_ui.exe <de|en>  ->  CHECK lines
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class InlineTextUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const int WM_KEYDOWN = 0x0100, WM_CHAR = 0x0102;

        static EditorForm form;
        static EditorCanvas canvas;
        static int failures;

        [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        static int Main(string[] args)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(240000);
                Console.WriteLine("CHECK watchdog: the test ended within 240 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args[0]);
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", "62mm") { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                done = true;
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);   // closes without asking to save
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine(failures == 0 ? "DONE " + args[0] : "DONE " + args[0] + " " + failures + " failed");
            return failures == 0 ? 0 : 1;
        }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + what + " " + (ok ? "PASS" : "FAIL"));
            if (!ok) failures++;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }
        static object Call(string method, params object[] a)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            return m.Invoke(form, a);
        }
        static LabelDoc Doc() { return F<LabelDoc>("doc"); }
        static IEnumerable<Control> Under(Control c) { return c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Under(x))); }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        // A key as the window gets it (its ProcessCmdKey first, as for a real key press). True: handled there.
        [DllImport("user32.dll")] static extern IntPtr GetFocus();

        // A key as Windows sends it to the field that has the focus (the message loop: PreProcessMessage, then the field).
        static void FieldKey(Control field, Keys key)
        {
            if (!field.ContainsFocus) field.Focus();   // not again: the number field would move it off its edit box
            Pump(30);
            IntPtr h = GetFocus();
            PostMessage(h, WM_KEYDOWN, (IntPtr)(int)key, IntPtr.Zero);
            PostMessage(h, WM_CHAR, (IntPtr)(key == Keys.Enter ? 13 : 27), IntPtr.Zero);
            Pump(120);
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        // Typed into a field as Windows sends it (all of its text selected first, then the characters).
        static void TypeInto(Control field, string text)
        {
            if (!field.ContainsFocus) field.Focus();   // not again: the number field would move it off its edit box
            Pump(30);
            IntPtr h = GetFocus();
            SendMessage(h, 0x00B1, IntPtr.Zero, (IntPtr)(-1));   // EM_SETSEL: all
            foreach (char c in text) PostMessage(h, WM_CHAR, (IntPtr)c, IntPtr.Zero);
            Pump(60);
        }

        static void FieldKeys(TextObject p)
        {
            canvas.Select(new LabelObject[] { p });
            Pump(60);
            var size = F<StepCombo>("sizeBox");
            var lineBox = F<ComboBox>("lineSpacingBox");
            var letters = F<StepUpDown>("letterSpacingNum");
            var stack = (System.Collections.IList)typeof(UndoStack).GetField("undo", All).GetValue(F<UndoStack>("undo"));
            Func<string> sizes = () => string.Join("|", p.FullRuns().Select(r => (r.Size ?? p.Size).ToString()));
            Func<string> spacings = () => string.Join("|", p.FullRuns().Select(r => (r.Spacing ?? p.Spacing).ToString()));
            string size0 = sizes(), sizeText0 = size.Text, spacing0 = spacings(), letterText0 = letters.Text;
            double line0 = p.LineSpacing;
            var runs0 = p.FullRuns().ToArray();

            TypeInto(size, "33");
            FieldKey(size, Keys.Escape);
            Check(sizes() == size0 && size.Text == sizeText0, "size box: typed 33 + Escape changes nothing (" + sizes() + ", shows " + size.Text + ")");
            TypeInto(lineBox, "180");
            FieldKey(lineBox, Keys.Escape);
            Check(Math.Abs(p.LineSpacing - line0) < 1e-9 && lineBox.Text == LineText(line0), "line spacing: typed 180 + Escape changes nothing (" + p.LineSpacing + ", shows " + lineBox.Text + ")");
            TypeInto(letters, "150");
            FieldKey(letters, Keys.Escape);
            Check(spacings() == spacing0 && letters.Text == letterText0, "letter spacing: typed 150 + Escape changes nothing (" + spacings() + ", shows " + letters.Text + ")");

            int steps = stack.Count;
            TypeInto(size, "13");
            FieldKey(size, Keys.Enter);
            Check(p.FullRuns().All(r => (r.Size ?? p.Size) == 13) && stack.Count == steps + 1 && !size.Focused,
                  "size box: typed 13 + Enter sets 13 pt, one undo step (" + sizes() + ", steps +" + (stack.Count - steps) + ")");
            Check(size.Text == size.FormatValue(13), "size box shows " + size.Text);
            steps = stack.Count;
            TypeInto(lineBox, "130");
            FieldKey(lineBox, Keys.Enter);
            Check(Math.Abs(p.LineSpacing - 130) < 1e-9 && stack.Count == steps + 1, "line spacing: typed 130 + Enter, one undo step (steps +" + (stack.Count - steps) + ")");
            steps = stack.Count;
            TypeInto(letters, "110");
            FieldKey(letters, Keys.Enter);
            Check(p.FullRuns().All(r => (r.Spacing ?? p.Spacing) == 110) && stack.Count == steps + 1, "letter spacing: typed 110 + Enter, one undo step (" + spacings() + ", steps +" + (stack.Count - steps) + ")");
            // back as before (no undo: it puts copies in place, and the test goes on with p)
            p.SetRuns(runs0);
            Call("SetLineSpacing", line0, false);
            Call("UpdateTextBar");
            Pump(60);
        }

        static bool Key(Keys keys)
        {
            var m = Message.Create(canvas.Handle, WM_KEYDOWN, (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero);
            var args = new object[] { m, keys };
            bool handled = (bool)typeof(EditorForm).GetMethod("ProcessCmdKey", All).Invoke(form, args);
            Pump(30);
            return handled;
        }

        // Characters as Windows sends them after TranslateMessage (a dead key and the next letter arrive as one ê; AltGr+Q
        // as @; an emoji as its two halves).
        static void Chars(string s)
        {
            foreach (char c in s) PostMessage(canvas.Handle, WM_CHAR, (IntPtr)c, IntPtr.Zero);
            Pump(60);
        }

        static void Mouse(string what, Point at, int clicks)
        {
            var e = new MouseEventArgs(MouseButtons.Left, clicks, at.X, at.Y, 0);
            typeof(EditorCanvas).GetMethod(what, All).Invoke(canvas, new object[] { e });
        }

        static void DoubleClick(Point at)
        {
            Mouse("OnMouseDown", at, 1); Mouse("OnMouseUp", at, 1);
            Mouse("OnMouseDown", at, 2); Mouse("OnMouseUp", at, 2);
            Pump(60);
        }

        // Where a character boundary of a text is on the canvas (between characters k-1 and k; on its line's middle),
        // through the text's turn.
        static Point At(TextObject t, int index)
        {
            var map = EditorRender.MapText(t);
            var l = map.Lines[map.LineOf(index)];
            float x = map.XOf(index), y = l.Top + l.Height / 2;
            var c = new PointF((float)(t.X + t.W / 2), (float)(t.Y + t.H / 2));
            var mm = LabelObject.Turn(new PointF(c.X + x, c.Y + y), c, t.Angle);
            var p = canvas.MmToClient(mm);
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }

        // The middle of character k (between boundaries k and k+1).
        static Point Mid(TextObject t, int k)
        {
            var map = EditorRender.MapText(t);
            var l = map.Lines[map.LineOf(k)];
            float x = (map.XOf(k) + map.XOf(k + 1)) / 2, y = l.Top + l.Height / 2;
            var c = new PointF((float)(t.X + t.W / 2), (float)(t.Y + t.H / 2));
            var mm = LabelObject.Turn(new PointF(c.X + x, c.Y + y), c, t.Angle);
            var p = canvas.MmToClient(mm);
            return new Point((int)Math.Round(p.X), (int)Math.Round(p.Y));
        }

        static TextObject AddText(string text, TextKind kind, double x, double y, double w = 30, double h = 12, int rotation = 0)
        {
            var t = new TextObject { X = x, Y = y, W = w, H = h, Text = text, Size = 12, Kind = kind, Rotation = rotation };
            EditorRender.FitText(t);
            Call("Add", t);
            Pump(100);
            return t;
        }

        static int UndoCount() { return ((System.Collections.ICollection)typeof(UndoStack).GetField("undo", All).GetValue(F<UndoStack>("undo"))).Count; }

        static void Run()
        {
            Pump(1500);
            canvas = F<EditorCanvas>("canvas");
            form.ActiveControl = canvas;   // the keys go to the canvas (a test desktop has no foreground)
            Call("SetZoom", 300.0);
            Pump(300);

            // ---------- double-click, type, Esc, one undo step ----------
            var t = AddText("Hallo Welt", TextKind.Line, 4, 4);
            int steps = UndoCount();
            DoubleClick(Mid(t, 6));   // in the "W"'s right half... its middle: the caret before or after it
            Check(canvas.EditedText == t, "double-click starts editing");
            Check(canvas.Editing != null && (canvas.Editing.Caret == 6 || canvas.Editing.Caret == 7), "caret at the clicked place: " + (canvas.Editing != null ? canvas.Editing.Caret : -1));
            Check(F<ToolStripStatusLabel>("labelStatus").Text == Lang.T("Editing text – Esc or a click beside it ends."), "status bar while editing");
            Key(Keys.End);
            Chars("!");
            Check(t.Text == "Hallo Welt!", "typed at the end: " + t.Text);
            Check(UndoCount() == steps + 1, "one undo step taken at the first change: " + (UndoCount() - steps));
            Chars("?");
            Key(Keys.Back);
            Key(Keys.Left);
            Key(Keys.Shift | Keys.Home);
            Check(canvas.Editing.SelectionLength == 10, "Shift+Home selects to the line start: " + canvas.Editing.SelectionLength);
            Key(Keys.Escape);
            Check(canvas.EditedText == null && canvas.Selected == t, "Esc ends editing, the text stays selected");
            Check(UndoCount() == steps + 1, "still one undo step for the session");
            Call("Undo", false);
            Pump(100);
            var back = Doc().Objects.OfType<TextObject>().LastOrDefault();
            Check(back != null && back.Text == "Hallo Welt", "undo: the text as before editing: " + (back != null ? back.Text : "-"));
            Call("Undo", true);
            Pump(100);
            t = Doc().Objects.OfType<TextObject>().Last();
            Check(t.Text == "Hallo Welt!", "redo");

            // ---------- the keys Windows sends: a dead key's ê, AltGr's @, an emoji; AltGr isn't a shortcut ----------
            canvas.Selected = t;
            Check(Key(Keys.F2) && canvas.EditedText == t && canvas.Editing.Caret == t.Text.Length, "F2 starts editing, caret at the end");
            Check(!Key(Keys.Control | Keys.Alt | Keys.Q), "AltGr+Q is left to Windows (not a shortcut)");
            Chars(" ê@€😀");
            Check(t.Text == "Hallo Welt! ê@€\U0001F600", "dead key, AltGr and emoji typed: " + t.Text);
            Key(Keys.Back);
            Check(t.Text == "Hallo Welt! ê@€", "Backspace removes the emoji whole");
            Key(Keys.Escape);

            // ---------- typing on a selected text starts editing at the end; Enter too ----------
            canvas.Selected = t;
            Chars("x");
            Check(canvas.EditedText == t && t.Text.EndsWith("€x"), "typing on a selected text: editing at the end: " + t.Text);
            Key(Keys.Enter);
            Check(canvas.EditedText == null && t.Text.EndsWith("€x"), "Enter ends an artistic text's editing (one line)");
            canvas.Selected = t;
            Check(Key(Keys.Enter) && canvas.EditedText == t, "Enter on a selected text starts editing");
            Key(Keys.Escape);

            // ---------- a paragraph text: bold a word, line keys, panel rows on the selection ----------
            var p = AddText("eins zwei drei vier fünf sechs sieben", TextKind.Frame, 4, 14, 26, 14);
            DoubleClick(Mid(p, 6));
            Check(canvas.EditedText == p, "double-click on a paragraph text");
            var fontBox = F<ComboBox>("fontBox");
            Check(form.ActiveControl == canvas, "editing: the keys stay in the canvas: " + (form.ActiveControl != null ? form.ActiveControl.Name : "-"));
            Check(fontBox.SelectionLength == 0, "editing: no selected (blue) name in the font box: " + fontBox.SelectionLength);
            DoubleClick(Mid(p, 6));   // inside editing: the word
            Check(canvas.Editing.SelectedText == "zwei", "double-click in editing selects the word: " + canvas.Editing.SelectedText);
            Key(Keys.Control | Keys.B);
            Check(p.FormatRuns != null && p.FullRuns().Any(r => r.Text == "zwei" && r.Bold == true) && p.FullRuns().Where(r => r.Text != "zwei").All(r => r.Bold == false),
                  "Ctrl+B bolds the word only");
            Check(F<ToolStripButton>("boldButton").Checked, "toolbar: Bold checked for the selection");
            var scope = F<Label>("scopeHint");
            Check(scope.Visible && scope.Text.Contains("zwei"), "panel: applies to the selection: " + scope.Text);
            F<ToolStripButton>("italicButton").PerformClick();
            Pump(60);
            Check(p.FullRuns().Any(r => r.Text == "zwei" && r.Italic == true && r.Bold == true) && p.FullRuns().Where(r => r.Text != "zwei").All(r => r.Italic == false),
                  "toolbar: Italic on the selection");
            Check(fontBox.SelectionLength == 0 && canvas.EditedText == p, "after a toolbar button: still editing, no selected name in the font box");
            form.ActiveControl = canvas;
            Key(Keys.Control | Keys.End);
            Key(Keys.Up);
            Check(canvas.Editing.Caret < p.Text.Length, "Up moves to the line above");
            Key(Keys.Control | Keys.Home);
            Key(Keys.Control | Keys.Shift | Keys.Right);
            Check(canvas.Editing.SelectedText == "eins ", "Ctrl+Shift+Right selects a word: " + canvas.Editing.SelectedText);
            Key(Keys.Escape);

            // ---------- the panel while not editing: hint and Edit text (F2); within 690 px ----------
            canvas.Selected = p;
            Pump(100);
            var props = F<TableLayoutPanel>("props");
            var editButton = Under(props).OfType<Button>().FirstOrDefault(x => x.Text == Lang.T("Edit text (F2)"));
            var hint = Under(props).OfType<Label>().FirstOrDefault(x => x.Text == Lang.T("Double-click the text on the label to edit it."));
            Check(editButton != null && editButton.Visible && hint != null && hint.Visible, "panel: hint and Edit text (F2)");
            Check(!Under(props).OfType<RichTextBox>().Any(), "panel: no text box any more");
            var side = F<Panel>("side");
            Check(form.Height <= 690 * form.DeviceDpi / 96, "window height " + form.Height + " px at " + form.DeviceDpi + " dpi");
            Console.WriteLine("INFO text panel " + props.Height + " px high, " + side.ClientSize.Height + " px shown (it scrolls as before)");
            if (editButton != null) { editButton.PerformClick(); Pump(60); }
            Check(canvas.EditedText == p && canvas.Editing.Caret == p.Text.Length, "Edit text (F2) button: caret at the end");
            // while editing: hint and button go, the object commands are greyed and their keys do nothing
            Pump(60);
            Check(editButton != null && !editButton.Visible, "panel: no Edit button while editing");
            int count = Doc().Objects.Count;
            Key(Keys.Control | Keys.D);
            Check(Doc().Objects.Count == count && canvas.EditedText == p, "Ctrl+D does nothing while editing");
            var arrangeBar = F<ToolStrip>("arrangeBar");
            var textItems = F<List<ToolStripItem>>("textItems");
            Check(arrangeBar.Items.Cast<ToolStripItem>().Where(i => !(i is ToolStripSeparator) && !textItems.Contains(i)).All(i => !i.Enabled), "arrange toolbar greyed while editing");
            Check(F<ToolStripButton>("boldButton").Enabled && F<ToolStripButton[]>("alignButtons").All(a => a.Enabled), "... but not its text group");

            // ---------- fields: codes while editing, values after; Insert field at the caret ----------
            Key(Keys.Control | Keys.End);
            Call("InsertField", "{date}");
            Check(p.Text.EndsWith("{date}") && EditorRender.LinesOf(p).Any(l => l.Contains("{date}")), "field inserted at the caret, shown as its code");
            Key(Keys.Escape);
            Check(!EditorRender.LinesOf(p).Any(l => l.Contains("{date}")), "field shows its value after editing");

            // ---------- overflow: grey below the frame (option on), scrolled inside it (off) ----------
            var o = AddText("eins zwei drei vier fünf sechs sieben acht neun zehn elf zwölf dreizehn vierzehn", TextKind.Frame, 34, 4, 20, 8);
            canvas.BeginEdit(o);
            Pump(100);
            Check(EditorRender.Overflows(o), "overflowing paragraph text");
            Check(canvas.ShowOverflow, "View > Show overflow while editing is on by default");
            using (var shot = Shot())
            {
                // light grey ink in the 5 mm below the frame (a band, not a few rows: at a high zoom those can fall between lines)
                var below = canvas.MmToClient(new PointF((float)(o.X + 2), (float)(o.Y + o.H + 0.3)));
                var r = canvas.RectangleToScreen(new Rectangle((int)below.X, (int)below.Y, (int)(16 * canvas.PixelsPerMm), (int)(5 * canvas.PixelsPerMm)));
                r = form.RectangleToClient(r);
                int grey = 0;
                for (int x = r.Left; x < r.Right; x++)
                    for (int y = r.Top; y < r.Bottom; y++)
                    {
                        if (x < 0 || y < 0 || x >= shot.Width || y >= shot.Height) continue;
                        var c = shot.GetPixel(x, y);
                        if (c.R < 235 && c.R > 80 && Math.Abs(c.R - c.G) < 20 && Math.Abs(c.G - c.B) < 20) grey++;
                    }
                Check(grey > 10, "grey overflow lines below the frame: " + grey + " grey pixels");
                SaveShot(shot, "overflow");
            }
            Call("OverflowItem");   // a fresh item is fine: clicking it toggles the option
            var item = F<ToolStripMenuItem>("overflowItem");
            item.PerformClick();
            Pump(100);
            Key(Keys.Control | Keys.End);
            Check(!canvas.ShowOverflow && EditorRender.CodesScroll > 0, "option off: the text scrolls inside the frame to the caret: " + EditorRender.CodesScroll.ToString("0.0") + " mm");
            using (var shot = Shot()) SaveShot(shot, "scrolled");
            Key(Keys.Control | Keys.Home);
            Check(EditorRender.CodesScroll == 0, "back at the start: not scrolled");
            Key(Keys.Escape);
            Check(EditorRender.CodesScroll == 0, "editing ended: not scrolled");
            item.PerformClick();   // back on (the setting is the user's)
            Pump(60);

            // ---------- turned text: hit test and keys follow the text ----------
            foreach (int rot in new[] { 90, 180 })
            {
                var r = AddText("ABCDEF", TextKind.Line, 60, 20, 30, 12, rot);
                canvas.Selected = null;
                DoubleClick(Mid(r, 2));   // "C"
                int caret = canvas.Editing != null ? canvas.Editing.Caret : -1;
                Check(canvas.EditedText == r && (caret == 2 || caret == 3), rot + "°: double-click places the caret at the clicked letter: " + caret);
                Key(Keys.Home);
                Key(Keys.Right);
                Check(canvas.Editing.Caret == 1, rot + "°: Right moves to the next letter of the text");
                Chars("x");
                Check(r.Text == "AxBCDEF", rot + "°: typed there: " + r.Text);
                using (var shot = Shot()) SaveShot(shot, "turned-" + rot);
                Key(Keys.Escape);
            }

            // ---------- copy and paste formatted characters (clipboard locked by deskrun) ----------
            canvas.BeginEdit(p);
            canvas.EditChange(e => e.SelectWord(p.Text.IndexOf("zwei") + 1));
            Call("Copy");
            Pump(100);
            Key(Keys.Control | Keys.End);
            Call("Paste", false, null);
            Pump(100);
            Check(p.Text.EndsWith("zwei") && p.FullRuns().Last().Bold == true, "copy and paste inside the text keeps bold: " + p.Text);
            string rtf = null;
            try { rtf = Clipboard.GetData(DataFormats.Rtf) as string; } catch (Exception) { }
            Check(rtf != null && rtf.Contains(@"\b") && Clipboard.GetText() == "zwei", "clipboard: text and RTF of the selection");
            Key(Keys.Escape);

            // ---------- a new text: only selected (it can be moved first), typing replaces its placeholder ----------
            Call("AddText");
            Pump(100);
            var n = canvas.Selected as TextObject;
            Check(n != null && n.Text == Lang.T("Text") && canvas.Editing == null, "new text: selected, not editing");
            double nx = n != null ? n.X : 0;
            Key(Keys.Right);   // not a text key: on to the canvas (the window moves the selection on its KeyDown)
            typeof(Control).GetMethod("OnKeyDown", All).Invoke(canvas, new object[] { new KeyEventArgs(Keys.Right) });
            Pump(30);
            Check(n != null && n.X > nx && canvas.Editing == null, "new text: an arrow key moves it");
            Chars("Neu");
            Check(n != null && n.Text == "Neu" && canvas.EditedText == n, "typing on a new text replaces the placeholder: " + (n != null ? n.Text : "-"));
            // an emptied text goes when editing ends; undo brings it back
            Key(Keys.Control | Keys.A);
            Check(canvas.Editing.SelectionLength == 3, "Ctrl+A selects the whole text (not all objects)");
            Key(Keys.Back);
            Key(Keys.Escape);
            Check(!Doc().Objects.Contains(n), "an emptied text is removed when editing ends");
            Call("Undo", false);
            Pump(100);
            Check(Doc().Objects.OfType<TextObject>().Any(x => x.Text == Lang.T("Text")), "undo: the text as before the editing session (its placeholder)");

            // a new paragraph text: F2 edits as on every text (the cursor at the end); once edited, typing goes to the end
            Call("AddTextFrame");
            Pump(100);
            var nf = canvas.Selected as TextObject;
            Check(nf != null && nf.Kind == TextKind.Frame && canvas.Editing == null, "new paragraph text: selected, not editing");
            Key(Keys.F2);
            Check(canvas.EditedText == nf && canvas.Editing.Caret == Lang.T("Text").Length && !canvas.Editing.HasSelection, "new paragraph text: F2, the cursor at the end");
            Key(Keys.Escape);
            Chars("X");
            Check(nf != null && nf.Text == Lang.T("Text") + "X", "a new text edited once: typing goes to the end: " + (nf != null ? nf.Text : "-"));
            Key(Keys.Escape);
            // a new text, a field inserted before any typing: the field replaces the placeholder
            Call("AddText");
            Pump(100);
            var nq = canvas.Selected as TextObject;
            Call("InsertField", "{date}");
            Check(nq != null && nq.Text == "{date}", "a field on a new text replaces the placeholder: " + (nq != null ? nq.Text : "-"));
            Key(Keys.Escape);

            // ---------- Save while an emptied text is edited: editing ends first (as Esc), the file has no empty text ----------
            var gone = AddText("Weg", TextKind.Line, 4, 40);
            canvas.BeginEdit(gone);
            Pump(60);
            Key(Keys.Control | Keys.A);
            Key(Keys.Back);
            int before = Doc().Objects.Count;
            string file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ql-inline-save-" + Lang.Current + ".qllabel");
            var pathField = typeof(EditorForm).GetField("path", All);
            object oldPath = pathField.GetValue(form);
            pathField.SetValue(form, file);
            try
            {
                bool saved = (bool)Call("Save", false);
                var reread = LabelDoc.Load(file);
                Check(saved && canvas.EditedText == null && !Doc().Objects.Contains(gone) && Doc().Objects.Count == before - 1,
                      "Save while an emptied text is edited: editing ends first, the text is removed");
                Check(reread.Objects.Count == before - 1 && !reread.Objects.OfType<TextObject>().Any(x => x.Text.Length == 0),
                      "the saved file has no empty text object");
                Call("Undo", false);
                Pump(100);
                Check(Doc().Objects.OfType<TextObject>().Any(x => x.Text == "Weg"), "undo after that Save: the text as before editing (one step)");
            }
            finally
            {
                pathField.SetValue(form, oldPath);
                try { System.IO.File.Delete(file); } catch (Exception) { }
            }

            // ---------- a click beside the text ends editing ----------
            canvas.BeginEdit(t);
            var empty = canvas.MmToClient(new PointF(50, 55));
            Mouse("OnMouseDown", new Point((int)empty.X, (int)empty.Y), 1);
            Mouse("OnMouseUp", new Point((int)empty.X, (int)empty.Y), 1);
            Pump(60);
            Check(canvas.EditedText == null, "a click beside the text ends editing");

            // ---------- time per key: a 10-line paragraph ----------
            var ten = AddText(string.Join(" ", Enumerable.Repeat("Ein Satz mit einigen Wörtern,", 12)), TextKind.Frame, 4, 30, 40, 26);
            canvas.BeginEdit(ten);
            Pump(100);
            var watch = Stopwatch.StartNew();
            const int keys = 20;
            for (int i = 0; i < keys; i++)
            {
                PostMessage(canvas.Handle, WM_CHAR, (IntPtr)'x', IntPtr.Zero);
                Application.DoEvents();
                canvas.Update();
            }
            double ms = watch.Elapsed.TotalMilliseconds / keys;
            Console.WriteLine("INFO " + EditorRender.LinesOf(ten).Count + " lines, " + ms.ToString("0.0") + " ms per key (typed, rendered, painted)");
            Check(ten.Text.EndsWith(new string('x', keys)) && ms < 150, "per key " + ms.ToString("0.0") + " ms");
            Key(Keys.Escape);

            TextBar();
        }

        // ---------- the toolbar's text group (EditorForm.TextBar.cs) ----------
        static void TextBar()
        {
            var bar = F<ToolStrip>("arrangeBar");
            var bold = F<ToolStripButton>("boldButton");
            var italic = F<ToolStripButton>("italicButton");
            var underline = F<ToolStripButton>("underlineButton");
            var invert = F<ToolStripButton>("invertButton");
            var aligns = F<ToolStripButton[]>("alignButtons");
            var lineBox = F<ComboBox>("lineSpacingBox");
            var letters = F<StepUpDown>("letterSpacingNum");
            var mixed = F<bool[]>("textMixed");
            var all = new ToolStripItem[] { bold, italic, underline, invert }.Concat(aligns).ToArray();
            bool de = Lang.Current == "de";
            Check(bold.Text == (de ? "F" : "B") && italic.Text == (de ? "K" : "I") && underline.Text == "U", "text group letters: " + bold.Text + italic.Text + underline.Text);
            Check(invert.Image != null && aligns.All(a => a.Image != null), "text group: every icon drawn");

            // nothing or a shape selected: all greyed, nothing hidden
            canvas.Select(new LabelObject[0]);
            Pump(60);
            Check(all.All(i => !i.Enabled && i.Visible) && !lineBox.Enabled && !letters.Enabled && lineBox.Visible,
                  "nothing selected: the text group greyed, not hidden");
            Check(bold.ToolTipText.Contains(de ? "Markieren Sie einen Text" : "Select a text"), "greyed tip says why: " + bold.ToolTipText);
            var shape = new ShapeObject { X = 40, Y = 4, W = 10, H = 6 };
            Call("Add", shape);
            Pump(60);
            Check(all.All(i => !i.Enabled) && !letters.Enabled, "a shape selected: the text group greyed");

            // artistic text: letters, invert and letter spacing; no alignment or line spacing
            var art = AddText("Grafik", TextKind.Line, 4, 40);
            Check(bold.Enabled && italic.Enabled && underline.Enabled && invert.Enabled && letters.Enabled, "artistic text: B I U, invert, letter spacing usable");
            Check(aligns.All(a => !a.Enabled) && !lineBox.Enabled && lineBox.Text == "", "artistic text: alignment and line spacing greyed");
            Check(aligns[0].ToolTipText.Contains(de ? "nur bei Mengentext" : "paragraph text only"), "artistic text: the alignment tip says why: " + aligns[0].ToolTipText);

            // selected only: the whole text; Ctrl+Shift+F bold, Ctrl+I italic, Ctrl+U underline
            int objects = Doc().Objects.Count;
            Check(Key(Keys.Control | Keys.Shift | Keys.F) && art.Bold && bold.Checked, "selected: Ctrl+Shift+F bolds the whole text");
            Check(Key(Keys.Control | Keys.I) && art.Italic && Doc().Objects.Count == objects, "selected: Ctrl+I italic (no picture dialog)");
            Check(Key(Keys.Control | Keys.U) && art.Underline, "selected: Ctrl+U underline");
            bold.PerformClick(); italic.PerformClick(); underline.PerformClick();
            Pump(60);
            Check(!art.Bold && !art.Italic && !art.Underline && !bold.Checked, "selected: the buttons switch the whole text off again");
            invert.PerformClick();
            Pump(60);
            Check(art.Invert && invert.Checked, "Invert: on for the whole text");
            invert.PerformClick();
            Pump(60);
            Check(!art.Invert, "Invert: off again");
            Check(letters.Text == "100 %", "letter spacing shows the text's 100 %: " + letters.Text);
            for (int i = 0; i < 4; i++) letters.UpButton();   // 4 × 1 %, as the arrows and the wheel (the first one pushes the undo step)
            Pump(60);
            Check(Math.Abs(art.Spacing - 104) < 0.001 && letters.Text == "104 %", "letter spacing in the toolbar: " + art.Spacing + " (" + letters.Text + ")");
            StepUpDown.Modifiers = () => Keys.Shift;
            letters.UpButton();
            StepUpDown.Modifiers = () => Keys.Control;
            letters.DownButton();
            StepUpDown.Modifiers = () => Keys.None;
            Pump(60);
            Check(Math.Abs(art.Spacing - 113.9) < 0.001 && letters.Text == (113.9).ToString("0.#") + " %", "Shift: 10 %, Ctrl: 0.1 %: " + art.Spacing + " (" + letters.Text + ")");
            letters.Value = 100;
            Pump(60);
            Check(art.Spacing == 100, "letter spacing back to 100 %: " + art.Spacing);
            Check(bold.ToolTipText.Contains(KeyText(Keys.Control | Keys.Shift | Keys.F)) && italic.ToolTipText.Contains(KeyText(Keys.Control | Keys.I)),
                  "selected: the tips name the keys: " + bold.ToolTipText);

            // a paragraph text, edited: a mixed selection is hatched; click 1 all on, click 2 all off (as in Word)
            var p = AddText("verzinkt 100 Stück im Karton, lose", TextKind.Frame, 4, 50, 34, 14);
            objects = Doc().Objects.Count;
            Check(aligns.All(a => a.Enabled) && aligns[0].Checked && lineBox.Enabled && lineBox.Text == LineText(100), "paragraph text: alignment Left, line spacing " + lineBox.Text);
            canvas.BeginEdit(p);
            Pump(60);
            canvas.EditChange(e => e.Select(0, 8));   // "verzinkt"
            Key(Keys.Control | Keys.B);
            canvas.EditChange(e => e.Select(0, 12));  // "verzinkt 100": partly bold
            Pump(60);
            Check(mixed[0] && !bold.Checked && bold.Enabled, "mixed: Bold hatched (partly)");
            Check(bold.ToolTipText.Contains(de ? "teilweise" : "partly"), "mixed: the tip says partly: " + bold.ToolTipText);
            Check(bold.ToolTipText.Contains(KeyText(Keys.Control | Keys.B)), "editing: the tip names Ctrl+B: " + bold.ToolTipText);
            SaveShot(BarShot(bar), "textbar-mixed");
            bold.PerformClick();
            Pump(60);
            Check(Bolds(p, 0, 12) == "1" && bold.Checked && !mixed[0], "mixed: the first click bolds all of the selection");
            bold.PerformClick();
            Pump(60);
            Check(Bolds(p, 0, p.Text.Length) == "0" && !bold.Checked, "the second click: bold off everywhere in it");
            Check(canvas.EditedText == p && canvas.Editing.SelectionLength == 12, "editing goes on, the selection kept");

            // invert and letter spacing on the selection (2026-11-09: as in Word), the rest of the text as it was
            canvas.EditChange(e => e.Select(9, 12));   // "100"
            Pump(60);
            invert.PerformClick();
            Pump(60);
            Check(p.FullRuns().Where(r => r.Invert == true).Select(r => r.Text).SequenceEqual(new[] { "100" }) && !p.Invert && invert.Checked,
                  "paragraph: Invert on the selected word only");
            Check(invert.ToolTipText == (de ? "Invertieren: weiß auf Schwarz" : "Invert: white on black"), "Invert's tip: " + invert.ToolTipText);
            letters.Value = 140;
            Pump(60);
            Check(p.FullRuns().Where(r => r.Spacing == 140).Select(r => r.Text).SequenceEqual(new[] { "100" }) && p.Spacing == 100, "paragraph: letter spacing on the selected word only");
            Check(canvas.EditedText == p && canvas.Editing.SelectionLength == 3, "after the toolbar: still editing, the selection kept");
            canvas.EditChange(e => e.Select(0, 12));
            Pump(60);
            Check(mixed[3] && letters.Text == "", "a selection partly inverted and spaced: Invert hatched, letter spacing blank");
            SaveShot(BarShot(bar), "textbar-invert-mixed");
            canvas.EditChange(e => e.Select(9, 12));
            Pump(60);
            Check(letters.Text == "140 %" && invert.Checked && !mixed[3], "the word's own values shown again: " + letters.Text);
            SaveShot(Shot(), "invert-word");
            invert.PerformClick();
            letters.Value = 100;
            Pump(60);
            Check(p.FullRuns().All(r => r.Invert == false && r.Spacing == 100), "invert and spacing off again");

            // alignment while editing: Ctrl+L/E/R/J and the buttons, always the whole text
            Check(Key(Keys.Control | Keys.E) && p.Align == TextAlign.Center && aligns[1].Checked, "editing: Ctrl+E centres");
            Check(Key(Keys.Control | Keys.R) && p.Align == TextAlign.Right && p.Rotation == 0, "editing: Ctrl+R right (no turn)");
            Check(Key(Keys.Control | Keys.J) && p.Align == TextAlign.Justify && aligns[3].Checked, "editing: Ctrl+J justify");
            Check(Key(Keys.Control | Keys.L) && p.Align == TextAlign.Left && Doc().Objects.Count == objects, "editing: Ctrl+L left (no line added)");
            aligns[3].PerformClick();
            Pump(60);
            Check(p.Align == TextAlign.Justify && canvas.EditedText == p, "the Justify button while editing");
            Check(EditorRender.MapText(p).Lines.Count >= 2, "the justified paragraph wraps");

            // line spacing in %: a choice from the list, a value of one's own (also below 100 %), an old factor
            Call("SetLineSpacing", 150.0, false);
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 150) < 0.001 && lineBox.Text == LineText(150), "line spacing 150 %: " + lineBox.Text);
            lineBox.Text = "87";
            Call("CommitLineSpacing");
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 87) < 0.001 && lineBox.Text == LineText(87), "line spacing of one's own, below 100 %: " + lineBox.Text);
            lineBox.Text = "30 %";
            Call("CommitLineSpacing");
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 30) < 0.001, "line spacing 30 %: not raised to a minimum of 50 % (" + p.LineSpacing + ")");
            lineBox.Text = de ? "1,3" : "1.3";
            Call("CommitLineSpacing");
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 130) < 0.001, "a factor below 10 counts as before: 1.3 = 130 %: " + p.LineSpacing);
            lineBox.Text = "abc";
            Call("CommitLineSpacing");
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 130) < 0.001 && lineBox.Text == LineText(130), "line spacing: not a number keeps the value: " + lineBox.Text);
            lineBox.Text = "900";
            Call("CommitLineSpacing");
            Check(Math.Abs(p.LineSpacing - TextObject.MaxLineSpacing) < 0.001, "line spacing: limited to " + TextObject.MaxLineSpacing);
            Call("SetLineSpacing", 87.0, false);
            Call("StepLineSpacing", 1);
            StepUpDown.Modifiers = () => Keys.Shift;
            Call("StepLineSpacing", 1);
            StepUpDown.Modifiers = () => Keys.Control;
            Call("StepLineSpacing", -1);
            StepUpDown.Modifiers = () => Keys.None;
            Pump(60);
            Check(Math.Abs(p.LineSpacing - 97.9) < 0.001 && lineBox.Text == LineText(97.9), "line spacing steps 1 %, Shift 10 %, Ctrl 0.1 %: " + lineBox.Text);
            Call("SetLineSpacing", 100.0, false);
            Key(Keys.Escape);
            Pump(60);

            // selected, not edited, letter spacing mixed: the field blank; a typed value + Enter sets all of it

            p.SetRuns(new[] { new TextRun("verzinkt ", spacing: 130), new TextRun(p.Text.Substring(9), spacing: 100) });
            Call("UpdateTextBar");
            Pump(60);
            Check(canvas.EditedText == null && letters.Text == "", "selected, letter spacing mixed: the field blank (" + letters.Text + "; " + string.Join("|", p.FullRuns().Select(r => r.Text + ":" + r.Spacing)) +
                  ", focused " + letters.Focused + ", selected " + (canvas.Selected == p) + ")");
            letters.Focus();
            letters.Text = "120";
            typeof(StepUpDown).GetMethod("OnKeyDown", All).Invoke(letters, new object[] { new KeyEventArgs(Keys.Enter) });
            Pump(60);
            Check(p.FullRuns().All(r => r.Spacing == 120) && p.FormatRuns == null, "typed 120 + Enter: all of the frame 120 % (" +
                  string.Join("|", p.FullRuns().Select(r => r.Text + ":" + r.Spacing)) + ")");
            Check(letters.Text == "120 %", "the field shows the value as typed: " + letters.Text);
            letters.Value = 100;
            Pump(60);

            // Escape in a field of the group: back to the text's value, nothing applied (also not when the focus goes back
            // to the label); Enter applies once (one undo step). Real key messages to the focused field.
            FieldKeys(p);

            // selected only: Ctrl+J justify; Left's tip names Ctrl+L as an editing key (outside it adds a line)
            Call("SetTextAlign", TextAlign.Left);
            Check(Key(Keys.Control | Keys.J) && p.Align == TextAlign.Justify, "selected: Ctrl+J justify");
            Check(aligns[0].ToolTipText.Contains(de ? "beim Bearbeiten" : "while editing"), "selected: Left's tip: " + aligns[0].ToolTipText);

            // the drawing keys outside text (and with a text only selected): Ctrl+B rectangle, Ctrl+E ellipse, Ctrl+L line,
            // Ctrl+R turns; the text group takes only Ctrl+Shift+F, Ctrl+I, Ctrl+U and Ctrl+J then
            foreach (bool textSelected in new[] { false, true })
            {
                string what = textSelected ? "text selected" : "nothing selected";
                Func<int> count = () => Doc().Objects.Count;
                // the added object deleted again (an undo would put copies of all objects in place: p would be stale)
                Action drop = () => { canvas.Select(new[] { Doc().Objects.Last() }); Call("Delete"); Pump(60); };
                canvas.Select(textSelected ? new LabelObject[] { p } : new LabelObject[0]);
                Pump(60);
                int n0 = count();
                Check(Key(Keys.Control | Keys.B) && count() == n0 + 1 && Doc().Objects.Last() is ShapeObject && ((ShapeObject)Doc().Objects.Last()).Kind == ShapeKind.Rectangle,
                      what + ": Ctrl+B adds a rectangle");
                drop();
                canvas.Select(textSelected ? new LabelObject[] { p } : new LabelObject[0]);
                Check(Key(Keys.Control | Keys.E) && count() == n0 + 1 && Doc().Objects.Last() is ShapeObject && ((ShapeObject)Doc().Objects.Last()).Kind == ShapeKind.Ellipse,
                      what + ": Ctrl+E adds an ellipse");
                drop();
                canvas.Select(textSelected ? new LabelObject[] { p } : new LabelObject[0]);
                Check(Key(Keys.Control | Keys.L) && count() == n0 + 1 && Doc().Objects.Last() is LineObject, what + ": Ctrl+L adds a line");
                drop();
                canvas.Select(textSelected ? new LabelObject[] { p } : new LabelObject[0]);
                Pump(60);
                if (textSelected)
                {
                    int rot = p.Rotation;
                    bool handled = Key(Keys.Control | Keys.R);
                    Check(handled && p.Rotation == (rot + 90) % 360 && p.Align != TextAlign.Right,
                          what + ": Ctrl+R turns the text (no alignment): handled " + handled + ", rotation " + rot + " → " + p.Rotation + ", " + p.Align + ", selected " + canvas.Selection.Count);
                    for (int k = 0; k < 3; k++) Key(Keys.Control | Keys.R);
                    Check(p.Rotation == rot, what + ": turned back with three more");
                }
                else
                {
                    var sh = new ShapeObject { X = 70, Y = 4, W = 10, H = 6 };
                    Call("Add", sh);
                    Pump(60);
                    Check(Key(Keys.Control | Keys.R) && sh.Rotation == 90, "a shape selected: Ctrl+R turns it");
                    drop();
                }
                Check(count() == n0, what + ": undone, nothing left over");
            }
            canvas.Select(new LabelObject[] { p });
            Pump(60);

            // undo: an alignment click is a step of its own
            int steps = UndoCount();
            aligns[1].PerformClick(); aligns[2].PerformClick();
            Pump(60);
            Check(UndoCount() == steps + 2, "each alignment click one undo step (" + (UndoCount() - steps) + ")");

            // the panel: font, size, bold/italic/underline/invert/alignment/spacings all in the toolbar (CorelDRAW's
            // property bar); the "doesn't fit" hint the panel's last row (it moves nothing below it)
            Check(typeof(EditorForm).GetField("boldCheck", All) == null && typeof(EditorForm).GetField("alignBox", All) == null, "the panel's moved rows are gone");
            var props = F<TableLayoutPanel>("props");
            Check(!props.Contains(F<ComboBox>("fontBox")) && !props.Contains(F<StepCombo>("sizeBox")) && F<ComboBox>("fontBox").Parent is ToolStrip &&
                  F<StepCombo>("sizeBox").Parent is ToolStrip, "font and size in the toolbar, not in the panel");
            string fitText = Lang.T("The text doesn't fit the box and is cut off: make the box larger or the text smaller.");
            var lastRow = props.Controls.Cast<Control>().OrderByDescending(c => props.GetRow(c)).First();
            Check(lastRow is Label && lastRow.Text == fitText, "the \u201cdoesn't fit\u201d hint is the panel's last row: " + lastRow.Text);

            // a paragraph text's frame options in the panel (CorelDRAW's Text docker): vertical alignment, space before /
            // after paragraphs; not for artistic text; each click or value one undo step
            var vbuttons = F<CheckBox[]>("verticalButtons");
            var before = F<StepUpDown>("spaceBeforeNum");
            var after = F<StepUpDown>("spaceAfterNum");
            Check(p.Kind == TextKind.Frame && vbuttons.All(b => b.Visible) && before.Visible && after.Visible && vbuttons[0].Checked,
                  "paragraph text: Vertical, Before and After paragraph in the panel, top pressed");
            Action<CheckBox> press = b => typeof(CheckBox).GetMethod("OnClick", All).Invoke(b, new object[] { EventArgs.Empty });
            steps = UndoCount();
            press(vbuttons[1]); Pump(60);
            Check(p.VerticalAlign == VerticalAlign.Middle && vbuttons[1].Checked && !vbuttons[0].Checked && UndoCount() == steps + 1, "Middle: pressed, one undo step");
            before.Value = 25; Pump(30); after.Value = 40; Pump(60);
            Check(p.SpaceBefore == 25 && p.SpaceAfter == 40 && UndoCount() == steps + 3, "Before / After paragraph set (" + p.SpaceBefore + ", " + p.SpaceAfter + ")");
            StepUpDown.Modifiers = () => Keys.Shift;
            typeof(StepUpDown).GetMethod("OnMouseWheel", All).Invoke(after, new object[] { new MouseEventArgs(MouseButtons.None, 0, 0, 0, 120) });
            StepUpDown.Modifiers = () => Control.ModifierKeys;
            Pump(60);
            Check(p.SpaceAfter == 50, "Shift + wheel: 10 % more (" + p.SpaceAfter + ")");
            int panelBottom = Under(props).Where(c => c.Visible).Max(c => form.PointToClient(c.PointToScreen(new Point(0, c.Height))).Y);
            Console.WriteLine("INFO paragraph text panel ends at " + panelBottom + " px in the window (" + form.DeviceDpi + " dpi)");
            Check(panelBottom < form.LogicalToDeviceUnits(640), "paragraph text panel within a 690 px window (ends at " + panelBottom + ")");
            SaveShot(Shot(), "frame-options");
            press(vbuttons[0]); before.Value = 0; after.Value = 0; Pump(60);
            Check(p.VerticalAlign == VerticalAlign.Top && p.SpaceBefore == 0 && p.SpaceAfter == 0 && vbuttons[0].Checked, "back to top, no spacing");
            var items = bar.Items.Cast<ToolStripItem>().ToList();
            int fontAt = items.FindIndex(i => i is ToolStripControlHost && ((ToolStripControlHost)i).Control == F<ComboBox>("fontBox"));
            int rotateAt = items.FindIndex(i => i.Tag is ArrangeIcon && (ArrangeIcon)i.Tag == ArrangeIcon.RotateLeft);
            Check(fontAt == 3 && items[0].Tag is ArrangeIcon && (ArrangeIcon)items[0].Tag == ArrangeIcon.Duplicate && rotateAt > items.IndexOf(F<ToolStripControlHost>("letterSpacingHost")),
                  "toolbar order: Duplicate, Delete | font, size, … spacings | rotate … (font at " + fontAt + ", rotate at " + rotateAt + ")");

            Justified();

            // 1366 px and the default 1180 px window: the whole bar fits; 1024 and the minimum 780 px: the text group whole, the object
            // functions at the end behind »
            var size = form.Size;
            var state = form.WindowState;
            form.WindowState = FormWindowState.Normal;
            var texts = F<List<ToolStripItem>>("textItems").Where(i => !(i is ToolStripSeparator)).ToList();
            foreach (int w in new[] { 1366, 1180, 1024, 780 })
            {
                form.Size = new Size(form.LogicalToDeviceUnits(w), size.Height);
                Pump(150);
                int hidden = bar.Items.Cast<ToolStripItem>().Count(i => !(i is ToolStripSeparator) && i.Placement != ToolStripItemPlacement.Main);
                bool textWhole = texts.All(i => i.Placement == ToolStripItemPlacement.Main) && items[0].Placement == ToolStripItemPlacement.Main;
                Check(w >= 1180 ? hidden == 0 && !bar.OverflowButton.Visible : textWhole && (hidden == 0 || bar.OverflowButton.Visible),
                      w + " px: " + (w >= 1180 ? "the bar fits without »" : "Duplicate, Delete and the text group whole, the rest behind »") + " (" + hidden + " hidden, needs " +
                      bar.PreferredSize.Width + " of " + bar.Width + " px, window " + form.Width + ")");
                SaveShot(BarShot(bar), "textbar-" + w);
            }
            Console.WriteLine("INFO toolbar needs " + bar.PreferredSize.Width + " px at " + form.DeviceDpi + " dpi");
            form.Size = size;
            form.WindowState = state;

            // dark and High Contrast: pictures of the bar with the mixed selection
            canvas.BeginEdit(p);
            Pump(60);
            canvas.EditChange(e => e.Select(0, 8));
            Key(Keys.Control | Keys.B);
            canvas.EditChange(e => e.Select(0, 12));
            Pump(60);
            foreach (var s in new[] { Scheme.Dark, Scheme.HighContrast })
            {
                Theme.Force(s);
                Theme.Refresh();
                Pump(100);
                SaveShot(BarShot(bar), "textbar-mixed-" + s.ToString().ToLowerInvariant());
                Check(mixed[0] && bold.Enabled && lineBox.SelectionLength == 0, "mixed in " + s + ", no blue value in the line spacing box");
            }
            Theme.Force(null);
            Theme.Refresh();
            Key(Keys.Escape);
        }

        // Justify as the user does it (2026-11-09): a paragraph text
        // typed on the label (three lines and more), Justify clicked while editing and with the text only selected; the
        // label as the canvas shows it (canvas.Rendered): each wrapped line reaches the frame's right side, the last one
        // stays left; with Left the wrapped lines end short of it.
        static void Justified()
        {
            var aligns = F<ToolStripButton[]>("alignButtons");
            var j = AddText("x", TextKind.Frame, 100, 4, 36, 52);
            canvas.BeginEdit(j);
            Pump(60);
            canvas.EditChange(e => e.SelectAll());
            Chars("Ein Absatz im Blocksatz, der in seinem Rahmen mehrmals umbricht und weiter bis ans Ende läuft.");
            Check(EditorRender.LinesOf(j).Count >= 3, "justify: the typed paragraph wraps over " + EditorRender.LinesOf(j).Count + " lines");
            var left = RightEdges(j);
            aligns[3].PerformClick();
            Pump(100);
            var edited = RightEdges(j);
            Key(Keys.Escape);
            Pump(100);
            var shown = RightEdges(j);
            var lines = EditorRender.LinesOf(j);
            float frame = (float)(j.X + j.W);
            Console.WriteLine("INFO justify right edges left " + string.Join(" ", left.Select(v => v.ToString("0.00"))) + " / justified editing " +
                              string.Join(" ", edited.Select(v => v.ToString("0.00"))) + " / not editing " + string.Join(" ", shown.Select(v => v.ToString("0.00"))) + " frame " + frame.ToString("0.00") + " lines: " + string.Join(" | ", lines));
            for (int i = 0; i < lines.Count - 1; i++)
                if (lines[i].Trim().Contains(" "))
                    Check(shown[i] > frame - 0.8f && edited[i] > frame - 0.8f && left[i] < shown[i] - 0.1f,
                          "justify on the canvas: line " + i + " reaches the frame (" + shown[i].ToString("0.00") + " of " + frame.ToString("0.00") + ", left " + left[i].ToString("0.00") + ")");
            Check(shown[lines.Count - 1] < frame - 3, "justify on the canvas: the last line stays left");
            SaveShot(Shot(), "justify");

            // the paragraph only selected, Invert (2026-11-09): black bands behind its lines, the empty frame below
            // them white (no black box over the whole frame); the button checked; a second click takes it off
            var invert = F<ToolStripButton>("invertButton");
            canvas.Select(new LabelObject[] { j });
            Pump(60);
            invert.PerformClick();
            Pump(100);
            var jm = EditorRender.MapText(j);
            var last = jm.Lines[jm.Lines.Count - 1];
            float cx = (float)(j.X + j.W / 2), cy = (float)(j.Y + j.H / 2);
            double first = BlackShare((float)j.X + 0.5f, cy + jm.Lines[0].Top + 0.3f, (float)j.X + 6, cy + jm.Lines[0].Top + jm.Lines[0].Height - 0.3f);
            double below = BlackShare((float)j.X + 0.5f, cy + last.Top + last.Height + 0.6f, (float)(j.X + j.W) - 0.5f, (float)(j.Y + j.H) - 0.5f);
            double outside = BlackShare((float)j.X - 0.9f, (float)j.Y + 1, (float)j.X - 0.2f, (float)(j.Y + j.H) - 1);
            Check(j.Invert && invert.Checked && first > 0.6 && below < 0.01 && outside < 0.01,
                  "paragraph inverted: bands behind the lines (" + first.ToString("0.00") + " black), the frame below them white (" + below.ToString("0.00") +
                  "), nothing beyond the frame (" + outside.ToString("0.00") + ")");
            SaveShot(Shot(), "paragraph-inverted");
            invert.PerformClick();
            Pump(100);
            Check(!j.Invert && !invert.Checked && BlackShare((float)j.X + 0.5f, cy + jm.Lines[0].Top + 0.3f, (float)j.X + 6, cy + jm.Lines[0].Top + jm.Lines[0].Height - 0.3f) < 0.5,
                  "paragraph: the second click takes the inversion off");
            canvas.Select(new LabelObject[] { j });
            Call("Delete");
            Pump(60);
        }

        // The share of black dots on the canvas's rendered label between two points (mm on the label).
        static double BlackShare(float x0, float y0, float x1, float y1)
        {
            var layout = F<LabelLayout>("layout");
            var bmp = canvas.Rendered;
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            int black = 0, n = 0;
            for (int y = (int)Math.Ceiling((y0 - layout.Printable.Y) * ky); y <= (int)Math.Floor((y1 - layout.Printable.Y) * ky); y++)
                for (int x = (int)Math.Ceiling((x0 - layout.Printable.X) * kx); x <= (int)Math.Floor((x1 - layout.Printable.X) * kx); x++)
                {
                    if (x < 0 || y < 0 || x >= bmp.Width || y >= bmp.Height) continue;
                    n++;
                    if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0) black++;
                }
            return n == 0 ? 0 : (double)black / n;
        }

        // Per laid-out line of a text: the rightmost black dot on the canvas's rendered label (mm on the label).
        static List<float> RightEdges(TextObject t)
        {
            var layout = F<LabelLayout>("layout");
            var map = EditorRender.MapText(t);
            var bmp = canvas.Rendered;
            float kx = (float)(layout.DpiX / 25.4), ky = (float)(layout.DpiY / 25.4);
            float cy = (float)(t.Y + t.H / 2);
            var r = new List<float>();
            foreach (var l in map.Lines)
            {
                int y0 = Math.Max(0, (int)Math.Floor((cy + l.Top - layout.Printable.Y) * ky)), y1 = Math.Min(bmp.Height - 1, (int)Math.Ceiling((cy + l.Top + l.Height - layout.Printable.Y) * ky));
                int x0 = Math.Max(0, (int)((t.X - layout.Printable.X) * kx)), x1 = Math.Min(bmp.Width - 1, (int)((t.X + t.W - layout.Printable.X) * kx));
                int hi = -1;
                for (int y = y0; y <= y1; y++)
                    for (int x = x1; x > Math.Max(x0, hi); x--)
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) == 0) { hi = x; break; }
                r.Add(hi < 0 ? float.NaN : (hi + 0.5f) / kx + layout.Printable.X);
            }
            return r;
        }

        // "1": all characters from..to bold, "0": none, "mixed"
        static string Bolds(TextObject t, int from, int to)
        {
            var flags = new List<bool>();
            foreach (var r in t.FullRuns()) foreach (char c in r.Text) flags.Add(r.Bold == true);
            var part = flags.Skip(from).Take(to - from).ToList();
            return part.All(x => x) ? "1" : part.All(x => !x) ? "0" : "mixed";
        }

        static string LineText(double v) { return v.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture) + " %"; }
        static string KeyText(Keys k) { return KeyNames.Text(k); }   // the program's own key names (not Windows' language)

        static Bitmap BarShot(ToolStrip bar)
        {
            Pump(60);
            var b = new Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(b, new Rectangle(Point.Empty, bar.Size));
            return b;
        }

        static Bitmap Shot()
        {
            Pump(60);
            var b = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(b, new Rectangle(Point.Empty, form.Size));
            return b;
        }

        static void SaveShot(Bitmap b, string name)
        {
            string dir = Environment.GetEnvironmentVariable("QL_INLINE_SHOT");
            if (dir != null) b.Save(System.IO.Path.Combine(dir, name + "-" + Lang.Current + ".png"));
        }
    }
}
