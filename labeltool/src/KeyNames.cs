// Key names for menus and tooltips in the program's language, as Office shows them ("Ctrl+Shift+V", German
// "Strg+Umschalt+V"). Not Windows' KeysConverter: that follows the language of Windows, so an English program on a German
// Windows showed "Strg+Z" (2026-11-11). Menu items with ShortcutKeys get ShortcutKeyDisplayString from here for the same
// reason.
using System.Windows.Forms;

namespace QlLabels
{
    static class KeyNames
    {
        public static string Text(Keys keys)
        {
            string s = "";
            if ((keys & Keys.Control) != 0) s += Lang.T("Ctrl") + "+";
            if ((keys & Keys.Shift) != 0) s += Lang.T("Shift") + "+";
            if ((keys & Keys.Alt) != 0) s += "Alt+";
            return s + Key(keys & Keys.KeyCode);
        }

        static string Key(Keys k)
        {
            if (k >= Keys.A && k <= Keys.Z) return ((char)('A' + (k - Keys.A))).ToString();
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            if (k >= Keys.NumPad0 && k <= Keys.NumPad9) return ((char)('0' + (k - Keys.NumPad0))).ToString();
            if (k >= Keys.F1 && k <= Keys.F24) return "F" + (1 + (k - Keys.F1));
            switch (k)
            {
                case Keys.Delete: return Lang.T("Del");
                case Keys.Insert: return Lang.T("Ins");
                case Keys.Enter: return Lang.T("Enter");
                case Keys.Escape: return "Esc";
                case Keys.Tab: return "Tab";
                case Keys.Space: return Lang.T("Space");
                case Keys.Back: return Lang.T("Backspace");
                case Keys.PageUp: return Lang.T("PgUp");
                case Keys.PageDown: return Lang.T("PgDn");
                case Keys.Home: return Lang.T("Home");
                case Keys.End: return Lang.T("End");
                case Keys.Up: return "↑";
                case Keys.Down: return "↓";
                case Keys.Left: return "←";
                case Keys.Right: return "→";
                case Keys.Oemplus: case Keys.Add: return "Plus";
                case Keys.OemMinus: case Keys.Subtract: return "Minus";
                default: return k.ToString();
            }
        }
    }
}
