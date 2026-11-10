// Label editor window (EditorForm.cs), part: the text group of the Arrange toolbar. Bold, italic, underline and invert;
// left, centre, right and justify; line spacing and letter spacing. Greyed without a text (nothing jumps); for an
// artistic text the alignment and the line spacing are greyed. Bold, italic, underline, invert and the letter spacing
// act on the selection while the text is edited (with nothing selected on what is typed next), else on the whole text;
// a mixed one is hatched ("partly"), a click turns it on for all, the next off again (as in Word); a mixed letter
// spacing is blank. Alignment and line spacing always act on the whole text.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        ToolStripButton boldButton, italicButton, underlineButton, invertButton;
        ToolStripButton[] alignButtons;           // left, centre, right, justify (TextAlign order)
        ToolStripLabel lineSpacingIcon, letterSpacingIcon;
        ComboBox lineSpacingBox;                  // 1.0 / 1.15 / 1.5 / 2.0 or the user's own value
        NumericUpDown letterSpacingNum;           // pt
        ToolStripControlHost lineSpacingHost, letterSpacingHost;
        readonly List<ToolStripItem> textItems = new List<ToolStripItem>();
        readonly bool[] textMixed = new bool[4];  // bold, italic, underline, invert are mixed in what the buttons act on
        bool textBarFilling;
        readonly Font[] letterFonts = new Font[3];

        enum TextIcon { Invert, Left, Centre, Right, Justify, LineSpacing, LetterSpacing }

        static readonly double[] LineSpacings = { 1.0, 1.15, 1.5, 2.0 };

        // The group's items, added after the arrange buttons (BuildArrangeBar).
        IEnumerable<ToolStripItem> BuildTextBar()
        {
            Func<ToolStripButton> letter = () => new ToolStripButton { DisplayStyle = ToolStripItemDisplayStyle.Text, AutoSize = false };
            boldButton = letter();
            boldButton.Click += delegate { ToggleStyle(new object(), "bold", false); };
            italicButton = letter();
            italicButton.Click += delegate { ToggleStyle(new object(), "italic", false); };
            underlineButton = letter();
            underlineButton.Click += delegate { ToggleStyle(new object(), "underline", false); };
            invertButton = IconButton(TextIcon.Invert);
            invertButton.Click += delegate { ToggleStyle(new object(), "invert", false); };
            var styles = new[] { boldButton, italicButton, underlineButton, invertButton };
            for (int i = 0; i < 4; i++)
            {
                int k = i;
                styles[i].Paint += (s, e) => { if (textMixed[k]) Hatch(e.Graphics, (ToolStripItem)s); };
            }

            alignButtons = new ToolStripButton[4];
            for (int i = 0; i < 4; i++)
            {
                var a = (TextAlign)i;
                alignButtons[i] = IconButton((TextIcon)((int)TextIcon.Left + i));
                alignButtons[i].Click += delegate { SetTextAlign(a); };
            }

            lineSpacingIcon = new ToolStripLabel { DisplayStyle = ToolStripItemDisplayStyle.Image, Tag = TextIcon.LineSpacing, ImageScaling = ToolStripItemImageScaling.None };
            lineSpacingBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, IntegralHeight = false };
            foreach (var v in LineSpacings) lineSpacingBox.Items.Add(LineSpacingText(v));
            lineSpacingBox.SelectionChangeCommitted += delegate
            {
                if (lineSpacingBox.SelectedIndex >= 0) SetLineSpacing(LineSpacings[lineSpacingBox.SelectedIndex], true);
            };
            lineSpacingBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitLineSpacing(); BackToLabel(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; UpdateTextBar(); BackToLabel(); }
            };
            lineSpacingBox.Leave += delegate { CommitLineSpacing(); };
            lineSpacingHost = new ToolStripControlHost(lineSpacingBox) { AutoSize = false };
            lineSpacingBox.HandleCreated += delegate { lineSpacingBox.BeginInvoke((Action)NoBlueValue); };

            letterSpacingIcon = new ToolStripLabel { DisplayStyle = ToolStripItemDisplayStyle.Image, Tag = TextIcon.LetterSpacing, ImageScaling = ToolStripItemImageScaling.None };
            letterSpacingNum = Number((decimal)TextObject.MinSpacing, (decimal)TextObject.MaxSpacing, 0.5m, 1);
            letterSpacingNum.ValueChanged += delegate
            {
                if (textBarFilling) return;
                double v = (double)letterSpacingNum.Value;   // before Style: its undo step refreshes the bar
                Style(letterSpacingNum, r => r.With(spacing: v), false);
            };
            letterSpacingNum.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; BackToLabel(); }
            };
            letterSpacingHost = new ToolStripControlHost(letterSpacingNum) { AutoSize = false };

            textItems.Clear();
            textItems.AddRange(new ToolStripItem[]
            {
                new ToolStripSeparator(), boldButton, italicButton, underlineButton, invertButton, new ToolStripSeparator(),
                alignButtons[0], alignButtons[1], alignButtons[2], alignButtons[3], new ToolStripSeparator(),
                lineSpacingIcon, lineSpacingHost, letterSpacingIcon, letterSpacingHost,
            });
            return textItems;
        }

        ToolStripButton IconButton(TextIcon icon)
        {
            return new ToolStripButton { Tag = icon, DisplayStyle = ToolStripItemDisplayStyle.Image, ImageScaling = ToolStripItemImageScaling.None };
        }

        // Typing goes on in the text after a field of the group (Enter, Escape) when it is edited.
        void BackToLabel()
        {
            if (canvas.Editing != null || canvas.Selected != null) canvas.Focus();
        }

        // The icons and sizes at the present DPI (SizeArrangeBar).
        void SizeTextBar(int n)
        {
            if (boldButton == null) return;
            foreach (var item in textItems)
            {
                if (!(item.Tag is TextIcon)) continue;
                var bmp = new Bitmap(n, n);
                using (var g = Graphics.FromImage(bmp)) DrawTextIcon(g, (TextIcon)item.Tag, n, Theme.Of(Role.Text));
                var old = item.Image;
                item.Image = bmp;
                if (old != null) old.Dispose();
                item.Padding = item is ToolStripButton ? new Padding(S(3)) : new Padding(S(2), 0, S(1), 0);
            }
            // the letters: as large as the icon buttons, in the bar's font (bold, italic, underlined)
            var size = invertButton.GetPreferredSize(Size.Empty);
            var face = arrangeBar.Font;
            var fonts = new[] { FontStyle.Bold, FontStyle.Italic, FontStyle.Underline };
            var letters = new[] { boldButton, italicButton, underlineButton };
            for (int i = 0; i < 3; i++)
            {
                var old = letterFonts[i];
                letters[i].Font = letterFonts[i] = new Font(face.FontFamily, face.SizeInPoints * 1.15f, fonts[i], GraphicsUnit.Point);
                if (old != null) old.Dispose();
                letters[i].Size = size;
            }
            lineSpacingHost.Size = new Size(S(56), lineSpacingBox.PreferredHeight);
            NoBlueValue();   // also after a switch of the scheme (ThemeChanged)
            letterSpacingHost.Size = new Size(S(58), letterSpacingNum.PreferredHeight);
            lineSpacingHost.Margin = letterSpacingHost.Margin = new Padding(0, S(1), S(6), S(1));
        }

        // Enabled, checked, mixed and the tips for the selection (UpdateArrangeBar, after LockObjectItems: the group
        // stays usable while a text is edited).
        void UpdateTextBar()
        {
            if (boldButton == null) return;
            var t = canvas.Selection.Count == 1 ? canvas.Selected as TextObject : null;
            bool on = t != null, para = on && t.Kind == TextKind.Frame, editing = on && canvas.EditedText == t;
            var c = on ? Current(t) : null;
            var values = new bool?[] { on ? c.Bold : false, on ? c.Italic : false, on ? c.Underline : false, on ? c.Invert : false };
            string select = " – " + Lang.T("Select a text.");
            string paraOnly = " – " + Lang.T("paragraph text only");

            // bold, italic, underline: Ctrl+B/I/U while editing; selected only Ctrl+Shift+F (Ctrl+B adds a rectangle)
            var letters = new[] { boldButton, italicButton, underlineButton };
            string[] names = { Lang.T("Bold"), Lang.T("Italic"), Lang.T("Underline") };
            Keys[] keys = { editing ? Keys.Control | Keys.B : Keys.Control | Keys.Shift | Keys.F, Keys.Control | Keys.I, Keys.Control | Keys.U };
            bool repaint = false;
            for (int i = 0; i < 3; i++)
            {
                var b = letters[i];
                bool mixed = on && values[i] == null;
                if (textMixed[i] != mixed) { textMixed[i] = mixed; repaint = true; }
                b.Enabled = on;
                b.Checked = on && values[i] == true;
                string key = " (" + KeyText(keys[i]) + ")";
                b.ToolTipText = !on ? names[i] + select
                              : mixed ? names[i] + " – " + Lang.T("partly; click: all") + key
                              : names[i] + key;
                b.AccessibleName = names[i] + (mixed ? " – " + Lang.T("partly") : "");
                if (repaint) b.Invalidate();
            }
            letters[0].Text = Lang.T("B");   // German F, K, U (Word's)
            letters[1].Text = Lang.T("I");
            letters[2].Text = Lang.T("U");

            // invert: as bold (no key of its own)
            bool invertMixed = on && values[3] == null;
            if (textMixed[3] != invertMixed) { textMixed[3] = invertMixed; invertButton.Invalidate(); }
            invertButton.Enabled = on;
            invertButton.Checked = on && values[3] == true;
            invertButton.ToolTipText = !on ? Lang.T("Invert: white on black") + select
                                     : invertMixed ? Lang.T("Invert") + " – " + Lang.T("partly; click: all")
                                     : Lang.T("Invert: white on black");
            invertButton.AccessibleName = Lang.T("Invert") + (invertMixed ? " – " + Lang.T("partly") : "");

            // alignment: paragraph text only; Ctrl+L/E/R/J while editing (outside Ctrl+L and Ctrl+E add a line and an
            // ellipse, Ctrl+R turns), Ctrl+J also when the text is only selected
            string[] alignNames = { Lang.T("Align text left"), Lang.T("Centre text"), Lang.T("Align text right"), Lang.T("Justify") };
            Keys[] alignKeys = { Keys.L, Keys.E, Keys.R, Keys.J };
            for (int i = 0; i < 4; i++)
            {
                var b = alignButtons[i];
                b.Enabled = para;
                b.Checked = para && (int)t.Align == i;
                string key = editing || i == 3 ? " (" + KeyText(Keys.Control | alignKeys[i]) + ")"
                                               : " (" + Lang.F("{0} while editing", KeyText(Keys.Control | alignKeys[i])) + ")";
                b.ToolTipText = alignNames[i] + (!on ? select : !para ? paraOnly : key);
                b.AccessibleName = alignNames[i];
            }

            // the spacings: the field the user types in keeps its text
            textBarFilling = true;
            try
            {
                lineSpacingBox.Enabled = lineSpacingIcon.Enabled = para;
                string lineTip = Lang.T("Line spacing: 1.0 / 1.15 / 1.5 / 2.0 or your own value") + (!on ? select : !para ? paraOnly : "");
                lineSpacingIcon.ToolTipText = lineTip;
                lineSpacingBox.AccessibleName = Lang.T("Line spacing");
                tips.SetToolTip(lineSpacingBox, lineTip);
                if (!lineSpacingBox.Focused)
                {
                    string text = para ? LineSpacingText(t.LineHeight) : "";
                    if (lineSpacingBox.Text != text) lineSpacingBox.Text = text;
                    NoBlueValue();
                }

                letterSpacingNum.Enabled = letterSpacingIcon.Enabled = on;
                string letterTip = Lang.T("Letter spacing (pt): more or less room between the letters") + (on ? "" : select);
                letterSpacingIcon.ToolTipText = letterTip;
                letterSpacingNum.AccessibleName = Lang.T("Letter spacing (pt)");
                tips.SetToolTip(letterSpacingNum, letterTip);
                if (!letterSpacingNum.Focused)
                {
                    if (on && c.Spacing != null)
                    {
                        // the text too: after the blank (no text, or mixed) the same Value doesn't write it again
                        letterSpacingNum.Value = Clamp(letterSpacingNum, c.Spacing.Value);
                        string s = letterSpacingNum.Value.ToString("F1", CultureInfo.CurrentCulture);
                        if (letterSpacingNum.Text != s) letterSpacingNum.Text = s;
                    }
                    else letterSpacingNum.Text = "";
                }
            }
            finally { textBarFilling = false; }
        }

        // No blue (selected) value in the line spacing box while the user isn't in it (as the font box): Windows selects
        // it when the text is set and when the box is made again (a switch to dark or High Contrast).
        void NoBlueValue()
        {
            if (!lineSpacingBox.Focused && lineSpacingBox.IsHandleCreated && lineSpacingBox.SelectionLength > 0) lineSpacingBox.SelectionLength = 0;
        }

        // 1.0, 1.15, 1.5, 2.0 (German 1,0 ...): at least one decimal, as Word shows them.
        static string LineSpacingText(double v)
        {
            return Math.Round(v, 2).ToString(v == Math.Round(v) ? "0.0" : "0.0#", CultureInfo.CurrentCulture);
        }

        void CommitLineSpacing()
        {
            if (textBarFilling || !lineSpacingBox.Enabled) return;
            double v;
            string s = lineSpacingBox.Text.Trim().TrimEnd('×', 'x', ' ');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) &&
                !double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                UpdateTextBar();   // not a number: back to the text's value
                return;
            }
            SetLineSpacing(v, false);
        }

        void SetLineSpacing(double v, bool back)
        {
            var t = canvas.Selected as TextObject;
            if (t == null || t.Kind != TextKind.Frame) return;
            v = Math.Min(TextObject.MaxLineHeight, Math.Max(TextObject.MinLineHeight, Math.Round(v, 2)));
            if (Math.Abs(t.LineHeight - v) > 1e-9) Edit(lineSpacingBox, o => ((TextObject)o).LineHeight = v);
            lineSpacingBox.Text = LineSpacingText(t.LineHeight);
            if (back) BackToLabel();
        }

        void SetTextAlign(TextAlign a)
        {
            var t = canvas.Selected as TextObject;
            if (t == null || canvas.Selection.Count != 1 || t.Kind != TextKind.Frame || t.Align == a) return;
            Edit(new object(), o => ((TextObject)o).Align = a);
        }

        // The keys of the group (TextEditingKey). Editing: Ctrl+L/E/R/J align (Ctrl+B/I/U: the canvas). Only selected:
        // Ctrl+Shift+F bold, Ctrl+I (Ctrl+Shift+K) italic, Ctrl+U (Ctrl+Shift+U) underline, Ctrl+J justify.
        bool TextBarKey(Keys keyData)
        {
            var t = canvas.Selection.Count == 1 ? canvas.Selected as TextObject : null;
            if (t == null) return false;
            if (canvas.EditedText == t)
            {
                int a = keyData == (Keys.Control | Keys.L) ? 0 : keyData == (Keys.Control | Keys.E) ? 1 :
                        keyData == (Keys.Control | Keys.R) ? 2 : keyData == (Keys.Control | Keys.J) ? 3 : -1;
                if (a < 0) return false;
                SetTextAlign((TextAlign)a);   // artistic text: nothing (no line or ellipse either)
                return true;
            }
            if (canvas.Editing != null) return false;
            switch (keyData)
            {
                case Keys.Control | Keys.Shift | Keys.F: ToggleStyle(new object(), "bold", false); return true;
                case Keys.Control | Keys.I: case Keys.Control | Keys.Shift | Keys.K: ToggleStyle(new object(), "italic", false); return true;
                case Keys.Control | Keys.U: case Keys.Control | Keys.Shift | Keys.U: ToggleStyle(new object(), "underline", false); return true;
                case Keys.Control | Keys.J: SetTextAlign(TextAlign.Justify); return true;
            }
            return false;
        }

        // A mixed format: the button hatched in the accent colour.
        static void Hatch(Graphics g, ToolStripItem item)
        {
            if (!item.Enabled) return;
            var r = new Rectangle(1, 1, item.Width - 3, item.Height - 3);
            using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(Theme.HighContrast ? 255 : 130, Theme.Of(Role.Accent)), Color.Transparent))
                g.FillRectangle(hatch, r);
            using (var pen = new Pen(Theme.Of(Role.Accent))) g.DrawRectangle(pen, r);
        }

        // The group's icons on a 16-unit grid (as the mockup), scaled to n pixels.
        void DrawTextIcon(Graphics g, TextIcon icon, int n, Color ink)
        {
            float k = n / 16f;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(ink, Math.Max(1f, k)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
            using (var brush = new SolidBrush(ink))
            {
                Action<float, float, float, float> line = (x1, y1, x2, y2) => g.DrawLine(pen, x1 * k, y1 * k, x2 * k, y2 * k);
                switch (icon)
                {
                    case TextIcon.Invert:
                        using (var path = HeadTile.Rounded(new RectangleF(1.5f * k, 2 * k, 13 * k, 12 * k), 1.5f * k)) g.FillPath(brush, path);
                        using (var font = new Font("Segoe UI", 10 * k, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (var white = new SolidBrush(Theme.Of(Role.Content)))
                        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString("A", font, white, new RectangleF(1.5f * k, 2.5f * k, 13 * k, 12 * k), fmt);
                        break;
                    case TextIcon.Left: case TextIcon.Centre: case TextIcon.Right: case TextIcon.Justify:
                    {
                        g.SmoothingMode = SmoothingMode.None;
                        float shortFrom = icon == TextIcon.Left || icon == TextIcon.Justify ? 2.5f : icon == TextIcon.Centre ? 4.5f : 6.5f;
                        float shortEnd = shortFrom + 7;
                        line(2.5f, 3.5f, 13.5f, 3.5f);
                        if (icon == TextIcon.Justify) line(2.5f, 6.5f, 13.5f, 6.5f); else line(shortFrom, 6.5f, shortEnd, 6.5f);
                        line(2.5f, 9.5f, 13.5f, 9.5f);
                        line(shortFrom, 12.5f, shortEnd, 12.5f);
                        break;
                    }
                    case TextIcon.LineSpacing:
                        g.SmoothingMode = SmoothingMode.None;
                        foreach (var y in new[] { 3.5f, 6.5f, 9.5f, 12.5f }) line(7, y, 14, y);
                        line(3, 2.5f, 3, 13.5f);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawLines(pen, new[] { new PointF(1.5f * k, 4 * k), new PointF(3 * k, 2.5f * k), new PointF(4.5f * k, 4 * k) });
                        g.DrawLines(pen, new[] { new PointF(1.5f * k, 12 * k), new PointF(3 * k, 13.5f * k), new PointF(4.5f * k, 12 * k) });
                        break;
                    case TextIcon.LetterSpacing:
                        using (var font = new Font("Segoe UI", 8.5f * k, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        {
                            g.DrawString("A", font, brush, new RectangleF(0, 0, 8 * k, 11 * k), fmt);
                            g.DrawString("V", font, brush, new RectangleF(8 * k, 0, 8 * k, 11 * k), fmt);
                        }
                        line(2, 13, 14, 13);
                        g.DrawLines(pen, new[] { new PointF(3.5f * k, 11.5f * k), new PointF(2 * k, 13 * k), new PointF(3.5f * k, 14.5f * k) });
                        g.DrawLines(pen, new[] { new PointF(12.5f * k, 11.5f * k), new PointF(14 * k, 13 * k), new PointF(12.5f * k, 14.5f * k) });
                        break;
                }
            }
        }
    }
}
