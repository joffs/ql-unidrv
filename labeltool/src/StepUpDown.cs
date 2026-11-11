// A number field with CorelDRAW's steps: the mouse wheel, the arrow keys and the field's own arrows change the value by
// Step, with Shift by 10 × Step, with Ctrl by Step / 10 (nudge, super nudge, micro nudge; Shift = × 10 in Adobe,
// Affinity, Figma). A unit after the number ("%") is shown and read back. A blank field (a mixed value) stays blank
// until the user changes it; Committed reports a typed value + Enter even when it equals the Value already set (a
// mixed text: the value applies to all of it); a new value comes through ValueChanged as usual.
using System;
using System.Globalization;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class StepUpDown : NumericUpDown
    {
        public decimal Step = 1;   // the plain step (Increment follows the keys held)
        public string Unit = "";   // shown after the number, e.g. " %"
        public event EventHandler Committed;   // Enter with a number typed (Value already set)
        int wheel;

        // The modifier keys held now (a test sets its own: no real Shift or Ctrl in a UI test).
        internal static Func<Keys> Modifiers = () => ModifierKeys;

        public StepUpDown() { TextAlign = HorizontalAlignment.Right; }

        // Escape reaches the field (KeyDown) with the typed text replaced by the value shown before: in a toolbar the
        // toolbar would take it and send the focus back, and leaving the field applied the typed text.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Revert();
                OnKeyDown(new KeyEventArgs(Keys.Escape));
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // The value set before (or blank) back in the field, the typed text dropped.
        public void Revert()
        {
            UserEdit = false;   // first: Value's getter would apply the typed text
            UpdateEditText();
            UserEdit = false;
        }

        // The step for the keys held now.
        decimal StepNow
        {
            get
            {
                var keys = Modifiers();
                return (keys & Keys.Control) != 0 ? Step / 10 : (keys & Keys.Shift) != 0 ? Step * 10 : Step;
            }
        }

        public override void UpButton()
        {
            Increment = StepNow;
            if (Blank) { Blank = false; ValidateEditText(); }
            base.UpButton();
        }

        public override void DownButton()
        {
            Increment = StepNow;
            if (Blank) { Blank = false; ValidateEditText(); }
            base.DownButton();
        }

        // One step per notch of the wheel (Windows' default would scroll by several lines); Ctrl+wheel too (over the
        // label it zooms: EditorForm.PreFilterMessage).
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (!Enabled) return;
            wheel += e.Delta;
            while (Math.Abs(wheel) >= 120)
            {
                if (wheel > 0) { UpButton(); wheel -= 120; }
                else { DownButton(); wheel += 120; }
            }
        }

        // The field shows nothing (a mixed value) until a step or a typed number.
        public bool Blank
        {
            get { return blank; }
            set { blank = value; if (value) Text = ""; else UpdateEditText(); }
        }
        bool blank;

        // A new value (a step, typed, or set by the program) ends the blank.
        protected override void OnValueChanged(EventArgs e)
        {
            blank = false;
            base.OnValueChanged(e);
        }

        protected override void UpdateEditText()
        {
            if (blank) { Text = ""; return; }
            Text = Value.ToString("0." + new string('#', Math.Max(1, DecimalPlaces)), CultureInfo.CurrentCulture) + Unit;
        }

        // The typed text without its unit; "1,5" and "1.5" both read.
        public bool TryRead(out decimal v)
        {
            string s = Text.Trim();
            if (Unit.Trim().Length > 0) s = s.Replace(Unit.Trim(), "").Trim();
            return decimal.TryParse(s, NumberStyles.Number, CultureInfo.CurrentCulture, out v) ||
                   decimal.TryParse(s.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out v);
        }

        protected override void ValidateEditText()
        {
            decimal v;
            UserEdit = false;   // read once: Value's getter validates again while UserEdit is set
            if (Text.Trim().Length == 0 && blank) return;   // still mixed
            if (TryRead(out v))
            {
                blank = false;
                Value = Math.Min(Maximum, Math.Max(Minimum, Math.Round(v, DecimalPlaces)));
            }
            UpdateEditText();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                decimal v;
                bool typed = TryRead(out v);
                decimal before = Value;
                ValidateEditText();
                // ValueChanged reported a new value already; the same value (a blank, mixed field) only here
                if (typed && Value == before && Committed != null) Committed(this, EventArgs.Empty);
            }
            base.OnKeyDown(e);
        }
    }
}
