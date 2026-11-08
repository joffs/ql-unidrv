// Label editor window (EditorForm.cs), part: the settings panel on the right (the selected objects or the label).
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
        // ---------- properties ----------
        void BuildProperties()
        {
            // without a layout after every row (about 100 rows: seconds at the start)
            props.SuspendLayout();
            props.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            props.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            nothingSelected.Text = Lang.T("Nothing selected. Click an object on the label to change it.");
            nothingSelected.MaximumSize = new Size(S(260), 0);
            nothingSelected.Margin = new Padding(0, 0, 0, S(8));

            // Several objects selected: how many, and what works on all of them.
            Func<LabelObject, bool> several = o => canvas.Selection.Count > 1;
            severalSelected.Font = new Font(Font, FontStyle.Bold);
            severalSelected.Margin = new Padding(0, S(4), 0, S(4));
            SpanRow(severalSelected, several);
            var severalHint = Hint(Lang.T("Drag or use the arrow keys to move them together. Del deletes them. Click an object to edit just that one."));
            SpanRow(severalHint, several);

            // Label settings, shown while nothing is selected.
            Func<LabelObject, bool> none = o => canvas.Selection.Count == 0;
            var heading = Theme.Fore(new Label { Text = Lang.T("Label"), AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, S(4), 0, S(4)) }, Role.Heading);
            SpanRow(heading, none);
            SpanRow(nothingSelected, none);
            viewBox = Combo(Lang.T("Along the label"), Lang.T("Across the tape"));
            viewBox.SelectedIndexChanged += delegate { if (!loading && label != null && (viewBox.SelectedIndex == 1) != doc.Across) TurnView(); };
            Row(Lang.T("Direction:"), viewBox, none);
            viewBox.Width = Math.Max(S(150), viewBox.Items.Cast<string>().Max(t => TextRenderer.MeasureText(t, Font).Width) + S(30));
            tips.SetToolTip(viewBox, Lang.T("Across the tape: the label is shown turned by 90°, for text across a long label."));
            highResCheck = Check("300 × 600 dpi", (s, e) => { if (!loading) SetHighRes(highResCheck.Checked); });
            Row(Lang.T("Resolution:"), highResCheck, none);
            tips.SetToolTip(highResCheck, Lang.T("Twice the dots along the label: sharper text, slower printing."));
            // Length: on continuous tape a field (a length of its own), on die-cut and round labels the fixed length.
            Func<LabelObject, bool> tape = o => none(o) && label != null && label.Media.Continuous,
                fixedSize = o => none(o) && label != null && !label.Media.Continuous;
            lengthNum = Number(0, 9999, 1, 1);
            lengthNum.Width = S(80);
            lengthNum.ValueChanged += delegate { if (!loading) OwnLength((double)lengthNum.Value, false); };
            var lengthInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(lengthInfo, () => Lang.F("A length of its own on continuous tape, {0}–{1} mm on this printer. It prints exactly this length; no label format is needed.",
                                                     Media.Mm(MinLength), Media.Mm(MaxLength)));
            var lengthRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            lengthRow.Controls.AddRange(new Control[] { lengthNum, new Label { Text = "mm", AutoSize = true, Margin = new Padding(S(4), S(6), S(6), 0) }, lengthInfo });
            Row(Lang.T("Length:"), lengthRow, tape);
            lengthError = Theme.Fore(new Label { AutoSize = true, MaximumSize = new Size(S(230), 0) }, Role.ErrorText);
            Row("", lengthError, o => tape(o) && lengthError.Text.Length > 0);
            autoCheck = Check(Lang.T("Automatic from the content"), delegate
            {
                if (loading) return;
                autoLength = autoCheck.Checked;
                if (!FollowContent()) ShowLabelSettings();
                Touched();
            });
            Row("", autoCheck, tape);
            var saveFormat = new LinkLabel { Text = Lang.T("Save as label format …"), AutoSize = true, Margin = new Padding(0, S(4), S(6), S(4)) };
            saveFormat.LinkClicked += delegate { OpenLabelFormats(label != null && label.Media.Continuous ? label : null); };
            var saveInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(saveInfo, () => Lang.T("Then the length is also offered in other programs. Opens QL Label Formats with a new format of this length (Save & apply as usual)."));
            var saveRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, S(4)) };
            saveRow.Controls.AddRange(new Control[] { saveFormat, saveInfo });
            SpanRow(saveRow, tape);
            lengthFixed = new Label { AutoSize = true, Margin = new Padding(0, S(6), S(6), 0) };
            var fixedInfo = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(fixedInfo, () => Lang.T("The labels on the roll set the length. For a length of your own, choose continuous tape."));
            var fixedRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            fixedRow.Controls.AddRange(new Control[] { lengthFixed, fixedInfo });
            Row(Lang.T("Length:"), fixedRow, fixedSize);
            SpanRow(Hint(Lang.T("Arrow keys: 0.1 mm, with Shift 1 mm. Alt while dragging: no snapping.")), none);

            // Per kind (Editor.<Kind>.cs), in this order; then the rows every object has.
            BuildTextRows();
            BuildCodeRows();
            BuildShapeRows();
            BuildPictureRows();

            Func<LabelObject, bool> any = o => o != null, line = o => o is LineObject;
            Func<LabelObject, bool> oneLine = o => o is TextObject && ((TextObject)o).Kind == TextKind.Line;
            decimal low = (decimal)LabelObject.MinPos, high = (decimal)LabelObject.MaxMm;   // the file's ranges: a value shown is the real one
            numX = Number(low, high, 0.1m, 1); numY = Number(low, high, 0.1m, 1);
            numW = Number(0, high, 0.1m, 1); numH = Number(0, high, 0.1m, 1);
            numX.ValueChanged += (s, e) => Edit(s, o => o.X = (double)numX.Value);
            numY.ValueChanged += (s, e) => Edit(s, o => o.Y = (double)numY.Value);
            // a QR code stays square: width and height follow each other
            numW.ValueChanged += (s, e) => Edit(s, o => { o.W = (double)numW.Value; if (IsQr(o)) o.H = o.W; });
            numH.ValueChanged += (s, e) => Edit(s, o => { o.H = (double)numH.Value; if (IsQr(o)) o.W = o.H; });
            Row(Lang.T("Left (mm):"), numX, any);
            Row(Lang.T("Top (mm):"), numY, any);
            // A single-line text's box follows its text: no width and height to type. A line: its angle and length.
            Func<LabelObject, bool> sized = o => o != null && o.Resizable && !oneLine(o) && !(o is LineObject);
            Row(Lang.T("Width (mm):"), numW, sized);
            Row(Lang.T("Height (mm):"), numH, sized);
            lineAngleNum = Number(0, 180, 1, 1);
            lineLengthNum = Number(0, high, 0.1m, 1);
            lineAngleNum.ValueChanged += (s, e) => Edit(s, o => { var l = (LineObject)o; l.SetAngleLength((double)lineAngleNum.Value, l.Length); });
            lineLengthNum.ValueChanged += (s, e) => Edit(s, o => { var l = (LineObject)o; l.SetAngleLength(l.LineAngle, (double)lineLengthNum.Value); });
            tips.SetToolTip(lineAngleNum, Lang.T("0° level, 90° upright; the left end stays where it is."));
            tips.SetToolTip(lineLengthNum, Lang.T("The left end stays where it is."));
            Row(Lang.T("Angle (°):"), lineAngleNum, line);
            Row(Lang.T("Length (mm):"), lineLengthNum, line);
            // Text, boxes and pictures: any angle (also with the grip on the canvas); barcodes and QR codes in 90° steps.
            Func<LabelObject, bool> tilts = o => o != null && o.CanTilt && !(o is LineObject);
            Func<LabelObject, bool> quarters = o => o != null && !o.CanTilt && !(o is LineObject);
            angleNum = Number(-180, 180, 1, 1);
            angleNum.Width = S(70);
            angleNum.Margin = new Padding(0, S(3), 0, 0);
            angleNum.ValueChanged += (s, e) => Edit(s, o => o.SetAngle((double)angleNum.Value));
            Func<ArrangeIcon, string, int, Button> turn = (icon, tip, by) =>
            {
                int n = S(16);
                var pic = new Bitmap(n, n);
                using (var g = Graphics.FromImage(pic)) Brand.DrawArrangeIcon(g, icon, new RectangleF(0, 0, n, n), Theme.Of(Role.Text));
                var b = new Button { Image = pic, Size = new Size(S(30), S(26)), Margin = new Padding(S(6), S(1), 0, 0), UseVisualStyleBackColor = true };
                b.Click += (s, e) => Edit(s, o => o.SetAngle(by == 0 ? 0 : o.Angle + by));   // by 0: back to 0°
                turnButtons.Add(new KeyValuePair<Button, ArrangeIcon>(b, icon));
                tips.SetToolTip(b, tip);
                return b;
            };
            var angleRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            angleRow.Controls.AddRange(new Control[]
            {
                angleNum, new Label { Text = "°", AutoSize = true, Margin = new Padding(S(2), S(6), 0, 0) },
                turn(ArrangeIcon.RotateLeft, Lang.T("Turn 90° anticlockwise"), -90), resetButton = turn(ArrangeIcon.ResetRotation, Lang.T("Reset rotation (0°)"), 0),
                turn(ArrangeIcon.RotateRight, Lang.T("Turn 90° clockwise (Ctrl+R)"), 90),
            });
            Row(Lang.T("Rotation:"), angleRow, tilts);
            SpanRow(Hint(Lang.T("Any angle (−180° to 180°), also with the round grip above the object. The buttons turn by 90° (like Ctrl+R).")), tilts);
            rotBox = Combo("0°", "90°", "180°", "270°");
            rotBox.SelectedIndexChanged += (s, e) => Edit(s, o => { while (o.Rotation != rotBox.SelectedIndex * 90) o.Rotate90(); });
            Row(Lang.T("Rotation:"), rotBox, quarters);
            SpanRow(Hint(Lang.T("Barcodes and QR codes: only 0°, 90°, 180°, 270° (scanners read them best straight).")), quarters);
            props.ResumeLayout(false);
        }

        void Row(string caption, Control editor, Func<LabelObject, bool> visible)
        {
            int r = props.RowCount++;
            var l = new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(6), S(6), 0) };
            editor.Margin = new Padding(0, S(3), 0, S(3));
            if (!(editor is FlowLayoutPanel) && !(editor is CheckBox)) editor.Width = S(150);
            props.Controls.Add(l, 0, r);
            props.Controls.Add(editor, 1, r);
            rows.Add(new KeyValuePair<Control[], Func<LabelObject, bool>>(new Control[] { l, editor }, visible));
        }

        // The (i) of the SVG hint: what is missing, in plain words, the fonts replaced, and how to print it all.
        static string SvgTip(string[] missing)
        {
            var fonts = missing.Where(m => m.StartsWith("font:")).Select(m => m.Substring(5)).ToList();
            var words = missing.Where(m => !m.StartsWith("font:"))
                               .Select(m => m == "text" ? Lang.T("text") : m == "filter" ? Lang.T("effects such as shadows or blur")
                                          : m == "mask" ? Lang.T("fades and cut-outs") : Lang.T("some colours or lines")).ToList();
            var parts = new List<string>();
            if (words.Count > 0) parts.Add(Lang.F("Windows can't draw everything in this picture: {0}. The canvas shows what prints.", string.Join(", ", words)));
            if (fonts.Count > 0) parts.Add(string.Join("\n", fonts.Select(f => Lang.F("Font \"{0}\" is missing on this PC – a substitute font is used.", f))));
            parts.Add(Lang.T("To print it all: save the picture as a PNG in your drawing program and insert it here with \"Replace...\"."));
            if (fonts.Count > 0) parts.Add(Lang.T("Or install the font and insert the picture again with \"Replace...\"."));
            if (missing.Contains("text"))
                parts.Add(Lang.T("You can also convert the text to paths in your drawing program (Inkscape: Path › Object to Path); the picture then stays a drawing."));
            return string.Join("\n\n", parts);
        }

        void SpanRow(Control c, Func<LabelObject, bool> visible)
        {
            int r = props.RowCount++;
            props.Controls.Add(c, 0, r);
            props.SetColumnSpan(c, 2);
            rows.Add(new KeyValuePair<Control[], Func<LabelObject, bool>>(new[] { c }, visible));
        }

        Label Hint(string text)
        {
            return Theme.Fore(new Label { Text = text, AutoSize = true, MaximumSize = new Size(S(260), 0), Margin = new Padding(0, 0, 0, S(8)) }, Role.Dim);
        }

        NumericUpDown Number(decimal min, decimal max, decimal step, int decimals)
        {
            return new NumericUpDown { Minimum = min, Maximum = max, Increment = step, DecimalPlaces = decimals, TextAlign = HorizontalAlignment.Right };
        }

        static ComboBox Combo(params string[] items)
        {
            var c = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            c.Items.AddRange(items);
            return c;
        }

        static CheckBox Check(string text, EventHandler changed)
        {
            var c = new CheckBox { Text = text, AutoSize = true };
            c.CheckedChanged += changed;
            return c;
        }

        static decimal Clamp(NumericUpDown n, double v) { return Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)Math.Round(v, n.DecimalPlaces))); }

        // Shows the selected object's values (skip: the control the user is typing in).
        void FillProps(object skip)
        {
            var o = canvas.Selected;
            loading = true;
            props.SuspendLayout();
            try
            {
                if (canvas.Selection.Count > 1) severalSelected.Text = Lang.F("{0} objects selected", canvas.Selection.Count);
                foreach (var row in rows)
                {
                    bool v = row.Value(o);
                    foreach (var c in row.Key) c.Visible = v;
                }
                if (o == null) return;
                if (skip != numX) numX.Value = Clamp(numX, o.X);
                if (skip != numY) numY.Value = Clamp(numY, o.Y);
                if (skip != numW) numW.Value = Clamp(numW, o.W);
                if (skip != numH) numH.Value = Clamp(numH, o.H);
                if (skip != rotBox) rotBox.SelectedIndex = o.Rotation / 90;
                if (skip != angleNum) angleNum.Value = Clamp(angleNum, o.Angle);
                resetButton.Enabled = o.Angle != 0;
                var t = o as TextObject;
                if (t != null)
                {
                    FillText(t, skip);   // Editor.Text.cs
                }
                var l = o as LineObject;
                if (l != null)
                {
                    if (skip != thickNum) thickNum.Value = Clamp(thickNum, l.Thickness);
                    if (skip != lineAngleNum) lineAngleNum.Value = Clamp(lineAngleNum, l.LineAngle);
                    if (skip != lineLengthNum) lineLengthNum.Value = Clamp(lineLengthNum, l.Length);
                }
                var b = o as ShapeObject;
                if (b != null) FillShape(b, skip);
                var code = o as BarcodeObject;
                if (code != null)
                {
                    if (code.Kind != Symbology.Qr) kindBox.SelectedIndex = Array.IndexOf(LinearKinds, code.Kind);
                    notationLabel.Text = code.Kind == Symbology.Gs1_128 ? Lang.T("Elements: (AI)data, e.g. (01)…(17)…") : Lang.T("Control characters: {tab} {gs} {fnc1} …");
                    if (skip != dataBox && dataBox.Text != code.Data) dataBox.Text = code.Data;
                    qrTypeBox.SelectedIndex = (int)code.QrType;
                    foreach (var kv in qrEditors)
                    {
                        if (kv.Value == skip) continue;
                        string v;
                        if (!code.QrFields.TryGetValue(kv.Key, out v)) v = "";
                        var box = kv.Value as TextBox;
                        if (box != null && box.Text != v) box.Text = v;
                        var combo = kv.Value as ComboBox;
                        if (combo != null) combo.SelectedIndex = Math.Max(0, Array.IndexOf(QrContent.Security, v));
                        var check = kv.Value as CheckBox;
                        if (check != null) check.Checked = v == "true";
                    }
                    showTextCheck.Checked = code.ShowText;
                    levelBox.SelectedIndex = code.Level;
                    qrInvertCheck.Checked = code.Invert;
                    string problem = EditorRender.Problem(code, layout, DateTime.Now);
                    string hint = problem != null || layout == null ? null : EditorRender.BarcodeHint(code, layout, DateTime.Now);
                    Theme.Fore(problemLabel, problem != null ? Role.ErrorText : hint != null ? Role.WarningText : Role.Dim);
                    problemLabel.Text = problem ?? hint ?? (Fields.HasFields(code.Data) ? Lang.F("Prints now as: {0}", code.Caption(code.Content(DateTime.Now))) : "");
                    problemLabel.Visible = problemLabel.Text.Length > 0;
                    var hit = intruders.FirstOrDefault(p => p.Key == code);
                    Theme.Fore(quietLabel, hit.Key != null ? Role.WarningText : Role.Dim);
                    quietLabel.Text = hit.Key != null
                        ? Lang.F("{0} reaches into the quiet zone – scanners may not read the code well. Move the object away or make the code smaller.", NameOf(hit.Value))
                        : code.Invert && code.Kind == Symbology.Qr ? Lang.T("Quiet zone: printed black.")
                        : Lang.T("Light margin: quiet zone, stays clear, doesn't print.");
                }
                var img = o as ImageObject;
                if (img != null)
                {
                    modeBox.SelectedIndex = img.Mode == ImageMode.Dither ? 0 : 1;
                    if (skip != thresholdBar) thresholdBar.Value = img.Threshold;
                    invertCheck.Checked = img.Invert;
                    // only a font replaced: it all prints, in another font
                    svgLabel.Text = SvgPicture.Missing(img.Svg).All(m => m.StartsWith("font:"))
                        ? Lang.T("This picture prints with a substitute font.") : Lang.T("Parts of this picture can't be printed – tip: insert it as a PNG.");
                }
            }
            finally
            {
                props.ResumeLayout();
                // the side panel again: the docked table only takes its new height (rows shown or hidden, a hint that
                // wraps to another line) when its parent lays out, else its last rows were cut (German, 120/168 dpi)
                side.PerformLayout();
                loading = false;
            }
        }

        // While dragging: only the fields a drag changes (the whole panel again at the drop)
        void FillPlace()
        {
            var o = canvas.Selected;
            if (o == null) return;
            loading = true;
            try
            {
                numX.Value = Clamp(numX, o.X);
                numY.Value = Clamp(numY, o.Y);
                numW.Value = Clamp(numW, o.W);
                numH.Value = Clamp(numH, o.H);
                rotBox.SelectedIndex = o.Rotation / 90;
                angleNum.Value = Clamp(angleNum, o.Angle);
                resetButton.Enabled = o.Angle != 0;
                var t = o as TextObject;
                if (t != null) sizeNum.Value = Clamp(sizeNum, t.Size);
                var l = o as LineObject;
                if (l != null) { lineAngleNum.Value = Clamp(lineAngleNum, l.LineAngle); lineLengthNum.Value = Clamp(lineLengthNum, l.Length); }
            }
            finally { loading = false; }
        }

        // A property was changed in the panel: one undo step per control until another control or object is edited.
        void Edit(object sender, Action<LabelObject> change)
        {
            var o = canvas.Selected;
            if (loading || o == null) return;
            if (sender != lastEdited) { PushUndo(sender); }
            change(o);
            var t = o as TextObject;
            if (t != null) EditorRender.FitText(t);
            Touched();
            FillProps(sender);
        }
    }
}
