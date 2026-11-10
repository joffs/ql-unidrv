// QL Label Maker: "Insert field ▾" > "Custom field..." (EditorForm.Objects FieldButton): a small dialog for a data
// field of print with data. A field name (the CSV column) and what to do without a value (2026-11-09: an empty
// field must be choosable, not only by typing {Name|} by hand): report it ({Name}), leave it empty ({Name|}) or print a
// default ({Name|default}). It shows the field as it goes into the text, so the syntax can be learnt and edited by hand
// later. The name follows DataFields (PrintData.cs): letters, digits, _ and -, spaces only inside, at most 64
// characters, not one of the built-in fields (date, time, the barcode codes); a default has no braces.
using System;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class OwnFieldDialog : ScaledForm
    {
        // What the field prints when the data has no value for it.
        public enum Missing { Report, Empty, Default }

        // DataFields' name: a letter, digit or _ at both ends; inside also spaces and -; 64 characters at most.
        static readonly Regex NameChars = new Regex(@"^[\p{L}\p{N}_](?:[\p{L}\p{N}_ \-]*[\p{L}\p{N}_])?$");
        public const int MaxName = 64, MaxDefault = 1000;

        readonly TextBox nameBox = new TextBox { MaxLength = 200 };
        readonly RadioButton report = new RadioButton { AutoSize = true, Checked = true };
        readonly RadioButton empty = new RadioButton { AutoSize = true };
        readonly RadioButton withDefault = new RadioButton { AutoSize = true };
        readonly TextBox defaultBox = new TextBox { MaxLength = MaxDefault };
        readonly Label problem = Theme.Fore(new Label { AutoSize = true }, Role.ErrorText);
        readonly Label shown = new Label { AutoSize = true };
        readonly Button insert = new Button { AutoSize = true };

        // The field as it goes into the text (valid input).
        public string Field { get; private set; }

        Missing Mode { get { return report.Checked ? Missing.Report : empty.Checked ? Missing.Empty : Missing.Default; } }

        // {Name} reported when missing, {Name|} left empty, {Name|default} with a default (an empty default = empty).
        // The name without its outer spaces.
        public static string Token(string name, Missing mode, string def)
        {
            name = (name ?? "").Trim();
            if (mode == Missing.Report) return "{" + name + "}";
            return "{" + name + "|" + (mode == Missing.Default ? def ?? "" : "") + "}";
        }

        // Why the input can't be inserted, or null. An empty name: "" (nothing to say yet, Insert stays off).
        public static string Problem(string name, Missing mode, string def)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return "";
            if (name.Length > MaxName) return Lang.F("A field name has at most {0} characters.", MaxName);
            if (name.IndexOf('|') >= 0) return Lang.T("Enter a default value below at \"Default value\", without |.");
            if (name.IndexOf('{') >= 0 || name.IndexOf('}') >= 0) return Lang.T("Leave out { and }: they are added for you.");
            if (!NameChars.IsMatch(name)) return Lang.T("Use letters, digits, _ and - (spaces only inside the name).");
            if (DataFields.IsReserved(name)) return Lang.F("'{0}' is a built-in field (date, time, barcode codes). Please choose another name.", name);
            if (mode == Missing.Default && def != null && (def.IndexOf('{') >= 0 || def.IndexOf('}') >= 0)) return Lang.T("The default value can't contain { or }.");
            return null;
        }

        public OwnFieldDialog()
        {
            Text = Lang.T("Custom field");
            Theme.Use(this);
            HelpPages.F1(this);   // its owner's page
            Font = UiFont.MessageBox;
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            StartPosition = FormStartPosition.CenterParent;
            int em = Font.Height;

            var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(em) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Func<string, Label> caption = t => new Label { Text = t, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, em / 4, em / 2, em / 4) };

            var intro = Theme.Fore(new Label { Text = Lang.T("Filled in from the column of this name (CSV or JSON) when you print with data."), AutoSize = true,
                                               MaximumSize = new Size(em * 26, 0), Margin = new Padding(0, 0, 0, em * 2 / 3) }, Role.Dim);
            root.Controls.Add(intro, 0, 0);
            root.SetColumnSpan(intro, 2);

            nameBox.Width = em * 15;
            nameBox.AccessibleName = Lang.T("Field name:").TrimEnd(':');
            root.Controls.Add(caption(Lang.T("Field name:")), 0, 1);
            root.Controls.Add(nameBox, 1, 1);
            problem.MaximumSize = new Size(em * 16, 0);
            problem.Margin = new Padding(3, 0, 0, em / 4);
            root.Controls.Add(problem, 1, 2);

            // Without a value: report / leave empty / default [box]; the (i) after the first choice.
            report.Text = Lang.T("Report when printing");
            empty.Text = Lang.T("Leave empty");
            withDefault.Text = Lang.T("Default value:");
            defaultBox.Width = em * 9;
            defaultBox.AccessibleName = Lang.T("Default value:").TrimEnd(':');
            var info = StatusForm.InfoIcon(em);
            InfoTip.Attach(info, () => Lang.T("When the data has no value for this field (no such column, or the cell is empty). Report when printing: Print with data names the labels and prints them only once the value is there, or with \"Leave missing fields empty\" on. Leave empty: nothing is printed there. Default value: this text is printed instead. You can change the field in the text by hand later."));
            Func<Control[], FlowLayoutPanel> row = cs =>
            {
                var p = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                p.Controls.AddRange(cs);
                return p;
            };
            var choices = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            choices.Controls.Add(row(new Control[] { report, info }));
            choices.Controls.Add(empty);
            choices.Controls.Add(row(new Control[] { withDefault, defaultBox }));
            var without = caption(Lang.T("Without a value:"));
            without.Anchor = AnchorStyles.Left | AnchorStyles.Top;
            without.Margin = new Padding(0, em / 4 + 2, em / 2, em / 4);
            root.Controls.Add(without, 0, 3);
            root.Controls.Add(choices, 1, 3);

            shown.Font = UiFont.Monospace(Font.Size);
            shown.Margin = new Padding(3, em / 3, 0, em / 4);
            var insertedCaption = caption(Lang.T("Inserted:"));
            insertedCaption.Margin = new Padding(0, em / 3, em / 2, em / 4);
            root.Controls.Add(insertedCaption, 0, 4);
            root.Controls.Add(shown, 1, 4);

            insert.Text = Lang.T("Insert");
            Theme.Primary(insert);
            var cancel = new Button { Text = Lang.T("Cancel"), AutoSize = true, DialogResult = DialogResult.Cancel };
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(0, em * 2 / 3, 0, 0) };
            buttons.Controls.AddRange(new Control[] { insert, cancel });
            root.Controls.Add(buttons, 0, 5);
            root.SetColumnSpan(buttons, 2);
            Controls.Add(root);
            AcceptButton = insert;
            CancelButton = cancel;

            insert.Click += delegate
            {
                if (Problem(nameBox.Text, Mode, defaultBox.Text) != null) return;
                Field = Token(nameBox.Text, Mode, defaultBox.Text);
                DialogResult = DialogResult.OK;
            };
            nameBox.TextChanged += delegate { Changed(); };
            // Typing or clicking into the default box chooses it.
            defaultBox.TextChanged += delegate { if (defaultBox.Text.Length > 0) withDefault.Checked = true; Changed(); };
            defaultBox.Enter += delegate { withDefault.Checked = true; };
            // The three sit in different panels, so Windows doesn't group them: one checked unchecks the others.
            var all = new[] { report, empty, withDefault };
            foreach (var r in all)
            {
                var me = r;
                me.CheckedChanged += delegate
                {
                    if (me.Checked) foreach (var o in all) if (o != me) o.Checked = false;
                    Changed();
                };
            }
            Changed();
        }

        void Changed()
        {
            string p = Problem(nameBox.Text, Mode, defaultBox.Text);
            problem.Text = p ?? "";
            problem.Visible = !string.IsNullOrEmpty(p);
            string name = nameBox.Text.Trim();
            shown.Text = Token(name.Length > 0 ? name : Lang.T("Name"), Mode, defaultBox.Text);
            Theme.Fore(shown, name.Length > 0 && p == null ? Role.Accent : Role.Dim);
            insert.Enabled = p == null;
        }

        // Shows the dialog; the field to insert, or null (cancelled).
        public static string Ask(IWin32Window owner)
        {
            using (var d = new OwnFieldDialog())
                return d.ShowDialog(owner) == DialogResult.OK ? d.Field : null;
        }
    }
}
