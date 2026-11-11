// A number box with a list of usual values (the font size, as CorelDRAW's property bar): any value can be typed; the
// mouse wheel and the arrow keys change it by Step, with Shift by 10 × Step, with Ctrl by Step / 10 (StepUpDown's
// steps), from the value shown; the list opens with Alt+Down or its button. Committed reports a new value: picked
// from the list, typed + Enter (also the value shown already: a blank, mixed field applies to all), on leaving the
// box with a changed value, or a step. Escape asks for the value shown before (Reverted).
// FieldCombo: a combo box in a toolbar that gets Escape itself (the toolbar would take it and send the focus back,
// leaving the typed text in the box, applied when the box is left later).
using System;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    class FieldCombo : ComboBox
    {
        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Escape || base.IsInputKey(keyData);
        }
    }

    sealed class StepCombo : FieldCombo
    {
        public double Min, Max = double.MaxValue, Step = 1;
        public int Decimals = 1;
        public string Unit = "";               // shown after the number, e.g. " %"
        public event Action<double> Committed;
        public event EventHandler Reverted;
        bool filling;
        string shown = "";                     // the text Show put in (a different text: typed)
        int wheel;

        public StepCombo() { DropDownStyle = ComboBoxStyle.DropDown; IntegralHeight = false; }

        // The list's values (shown with the unit).
        public void SetItems(params double[] values)
        {
            Items.Clear();
            foreach (var v in values) Items.Add(FormatValue(v));
        }

        public string FormatValue(double v)
        {
            return Math.Round(v, Decimals).ToString("0." + new string('#', Math.Max(1, Decimals)), CultureInfo.CurrentCulture) + Unit;
        }

        // Shows a value (null: blank, mixed) without reporting it. Not while the user types in the box.
        public void Show(double? v)
        {
            if (Focused && Text != shown) return;
            filling = true;
            try
            {
                shown = v == null ? "" : FormatValue(v.Value);
                if (Text != shown) Text = shown;
                if (IsHandleCreated && !Focused && SelectionLength > 0) SelectionLength = 0;   // no blue value while the user isn't in it
            }
            finally { filling = false; }
        }

        // The number typed: "13,2", "13.2", "13,2 pt", "87 %".
        public bool TryRead(out double v)
        {
            string s = Text.Trim();
            if (Unit.Trim().Length > 0) s = s.Replace(Unit.Trim(), "");
            s = s.Trim();
            return double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v) ||
                   double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        void Commit(double v)
        {
            v = Math.Min(Max, Math.Max(Min, Math.Round(v, Decimals)));
            Put(FormatValue(v));   // also while typing: leaving the box then doesn't apply it again
            if (Committed != null) Committed(v);
        }

        // The value shown, put in the box (a typed text replaced).
        void Put(string text)
        {
            filling = true;
            try
            {
                shown = text;
                if (Text != text) Text = text;
            }
            finally { filling = false; }
        }

        void Revert()
        {
            Put(shown);
            if (Reverted != null) Reverted(this, EventArgs.Empty);
        }

        // From the value shown: the wheel or an arrow key (blank: nothing to step from).
        public void StepBy(int direction)
        {
            double now;
            if (!TryRead(out now)) return;
            var keys = StepUpDown.Modifiers();
            double step = (keys & Keys.Control) != 0 ? Step / 10 : (keys & Keys.Shift) != 0 ? Step * 10 : Step;
            Commit(now + direction * step);
            if (Focused) SelectionLength = 0;
        }

        protected override void OnSelectionChangeCommitted(EventArgs e)
        {
            base.OnSelectionChangeCommitted(e);
            double v;
            if (filling || SelectedIndex < 0) return;
            string text = (string)Items[SelectedIndex];
            string unit = Unit.Trim();
            if (double.TryParse(unit.Length > 0 ? text.Replace(unit, "").Trim() : text, NumberStyles.Float, CultureInfo.CurrentCulture, out v)) Commit(v);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                double v;
                if (TryRead(out v)) Commit(v); else Revert();
            }
            else if (e.KeyCode == Keys.Escape)
            {
                Revert();
            }
            else if ((e.KeyCode == Keys.Up || e.KeyCode == Keys.Down) && !e.Alt && !DroppedDown)
            {
                // a step from the value, not the next item of the list
                e.Handled = e.SuppressKeyPress = true;
                StepBy(e.KeyCode == Keys.Up ? 1 : -1);
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            double v;
            if (Text != shown && TryRead(out v)) Commit(v);
            else if (Text != shown) Revert();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (DroppedDown) { base.OnMouseWheel(e); return; }
            var handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            if (!Enabled) return;
            wheel += e.Delta;
            while (Math.Abs(wheel) >= 120)
            {
                int d = Math.Sign(wheel);
                wheel -= d * 120;
                StepBy(d);
            }
        }
    }
}
