// Barcode UI test of QL Label Maker on deskrun's desktop (never the user's): the barcode panel (type switch, content,
// problem line, notation and quiet-zone lines, text under the bars, (i) tips) and stale paint while moving and
// turning codes (the canvas as shown vs. after a full repaint, during and after the drag).
//   barcode_ui <file.qllabel> <out folder>      -> <out>\ui-*.png and lines "CHECK name PASS|FAIL detail"
// The file holds a Code 128 and a QR code.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class BarcodeUi
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
        const BindingFlags All = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static EditorForm form;
        static EditorCanvas canvas;
        static string outDir;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            var name = new StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            outDir = args[1];
            Lang.Force("de");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var watchdog = new System.Threading.Timer(_ => { Console.WriteLine("timeout"); Environment.Exit(3); }, null, 120000, Timeout.Infinite);
            form = new EditorForm(args[0], "QL-800 Capture", null);
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(30, 30, 1220, 680);
            bool done = false;
            form.Shown += delegate
            {
                Pump(2500);
                try { Work(); } catch (Exception e) { Console.WriteLine("CHECK harness FAIL " + e); failures++; }
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            watchdog.Dispose();
            return failures == 0 ? 0 : 1;
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }

        static void Check(string name, bool ok, string detail)
        {
            if (!ok) failures++;
            Console.WriteLine("CHECK " + name + " " + (ok ? "PASS" : "FAIL") + " " + detail);
        }

        static void Work()
        {
            canvas = F<EditorCanvas>("canvas");
            var doc = F<LabelDoc>("doc");
            var code = doc.Objects.OfType<BarcodeObject>().First(b => b.Kind != Symbology.Qr);
            var qrCode = doc.Objects.OfType<BarcodeObject>().First(b => b.Kind == Symbology.Qr);
            var kind = F<ComboBox>("kindBox");
            var data = F<TextBox>("dataBox");
            var problem = F<Label>("problemLabel");
            var notation = F<Label>("notationLabel");
            var showText = F<CheckBox>("showTextCheck");

            canvas.Selected = code; Pump(700);
            Check("code128-panel", kind.Visible && kind.SelectedIndex == 0 && notation.Visible && notation.Text.Contains("{tab}"),
                  "type '" + kind.Text + "', notation '" + notation.Text + "'");
            Shot("ui-code128");
            Tip(notation.Parent.Controls[1], "ui-tip-notation-code128");

            data.Text = "QL{tab}A{gs}B"; Pump(600);
            Check("code128-tokens", !problem.Visible || problem.Text.Length == 0, "problem line '" + (problem.Visible ? problem.Text : "") + "'");
            Shot("ui-code128-tokens");
            data.Text = "Grüße"; Pump(600);
            Check("code128-non-ascii", problem.Visible && problem.Text.Length > 0, "problem line '" + problem.Text + "'");
            Shot("ui-code128-umlaut");

            kind.SelectedIndex = 2; Pump(600);   // EAN-13
            data.Text = "2000000000009"; Pump(600);
            Check("ean13-check-digit", problem.Visible && problem.Text.Length > 0 && !notation.Visible, "problem line '" + problem.Text + "'");
            Shot("ui-ean13-bad");
            // Printing with a red problem asks first; Cancel is the default; a code that can't be encoded prints blank
            using (var d = (Form)typeof(EditorForm).GetMethod("ProblemsDialog", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null))
            {
                var labels = AllControls(d).OfType<Label>().Select(l => l.Text).ToList();
                Check("print-with-errors", d != null && d.AcceptButton == d.CancelButton && ((Button)d.CancelButton).Text == Lang.T("Cancel") &&
                      AllControls(d).OfType<Button>().Any(x => x.Text == Lang.T("Print anyway") && x.DialogResult == DialogResult.OK) &&
                      labels.Any(t => t.Contains(problem.Text)) && labels.Any(t => t.Contains(Lang.T("Codes with errors may not scan. A code that can't be made from its content stays blank."))),
                      "dialog: " + string.Join(" | ", labels));
            }
            data.Text = "200000000000"; Pump(600);
            // a valid code: no red problem line (a yellow hint such as "bars very narrow" may stand there)
            Check("ean13-12-digits", !problem.Visible || problem.ForeColor != Brand.Error, "12 digits: problem line '" + (problem.Visible ? problem.Text : "") +
                  "', prints as " + code.Content(DateTime.Now));
            data.Text = "2000000000008"; Pump(600);
            Check("ean13-ok", !problem.Visible || problem.ForeColor != Brand.Error, "problem line '" + (problem.Visible ? problem.Text : "") + "'");
            Shot("ui-ean13-ok");
            showText.Checked = false; Pump(600);
            Shot("ui-ean13-notext");
            Check("ean13-text-off", !code.ShowText, "ShowText " + code.ShowText);
            showText.Checked = true; Pump(400);

            kind.SelectedIndex = 1; Pump(600);   // GS1-128
            data.Text = "(01)0200000000000(17)2611"; Pump(600);
            Check("gs1-length", problem.Visible && problem.Text.Length > 0, "problem line '" + problem.Text + "'");
            Shot("ui-gs1-bad");
            data.Text = "(01)02000000000009"; Pump(600);
            Check("gs1-check-digit", problem.Visible && problem.Text.Length > 0, "problem line '" + problem.Text + "'");
            code.W = 56;   // wide enough for the text under the bars (narrower: a problem line asks for a wider code)
            data.Text = "(01)02000000000008(17){date:yyMMdd}(10)ABC123"; Pump(600);
            // the date field filled in; the panel shows "Prints now as …" unless a yellow hint takes the line
            Check("gs1-ok-date", (!problem.Visible || problem.ForeColor != Brand.Error) && code.Content(DateTime.Now).Contains(DateTime.Now.ToString("yyMMdd")) &&
                  (problem.Text.Contains(DateTime.Now.ToString("yyMMdd")) || problem.ForeColor == Brand.WarningText),
                  "line '" + problem.Text + "', notation '" + notation.Text + "'");
            Shot("ui-gs1-ok");
            Tip(notation.Parent.Controls[1], "ui-tip-notation-gs1");

            canvas.Selected = qrCode; Pump(700);
            Check("qr-panel", !kind.Visible && F<ComboBox>("levelBox").Visible && F<CheckBox>("qrInvertCheck").Visible, "type hidden, level and invert shown");
            Shot("ui-qr");
            Tip(F<CheckBox>("qrInvertCheck").Parent.Controls[1], "ui-tip-qr-invert");
            Tip(F<Label>("quietLabel").Parent.Controls[1], "ui-tip-quiet");

            // The stale-paint check itself: a change shown without invalidating must be found.
            StartScreen();
            ((List<LabelObject>)typeof(EditorCanvas).GetField("selection", All).GetValue(canvas)).Clear();   // no Invalidate
            Stale("selftest", true);
            screen.Dispose(); screen = null;
            canvas.Selected = null; canvas.Invalidate(); Pump(300);
            // Stale paint: drag each code across the label in steps, compare the canvas as shown with a full repaint.
            foreach (var o in new LabelObject[] { code, qrCode }.Concat(doc.Objects.OfType<LineObject>()))
            {
                canvas.Selected = o; Pump(500);
                StartScreen();
                var box = o.Box;
                var start = Screen(box.X + box.Width / 2, box.Y + box.Height / 2);
                Mouse("OnMouseDown", start);
                Point at = start;
                for (int i = 1; i <= 12; i++)
                {
                    at = new Point(start.X + i * 14, start.Y + (i % 4) * 6);
                    Mouse("OnMouseMove", at);
                    Settle();
                }
                Stale(Kind(o) + "-drag-during");
                Mouse("OnMouseUp", at);
                Settle();
                Stale(Kind(o) + "-drag-after");
                // turn by 90° steps (Arrange > Rotate right) twice
                typeof(EditorForm).GetMethod("Rotate", All, null, Type.EmptyTypes, null).Invoke(form, null); Settle();
                typeof(EditorForm).GetMethod("Rotate", All, null, Type.EmptyTypes, null).Invoke(form, null); Settle();
                Stale(Kind(o) + "-turn");
            }
        }

        static string Kind(LabelObject o)
        {
            var b = o as BarcodeObject;
            return b == null ? "line" : b.Kind == Symbology.Qr ? "qr" : "code";
        }

        static Point Screen(float x, float y)
        {
            var p = (PointF)typeof(EditorCanvas).GetMethod("ToScreen", All, null, new[] { typeof(float), typeof(float) }, null).Invoke(canvas, new object[] { x, y });
            return Point.Round(p);
        }

        static void Mouse(string what, Point p)
        {
            typeof(EditorCanvas).GetMethod(what, All).Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
            if (screen != null) Settle(); else Pump(60);
        }

        // An emulated screen of the canvas: like Windows, only the regions the canvas invalidated are painted again
        // (its OnPaint, clipped to the update region). Stale paint = this picture differs from one full repaint.
        [DllImport("user32.dll")] static extern int GetUpdateRgn(IntPtr h, IntPtr rgn, bool erase);
        [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h, IntPtr rect);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        static Bitmap screen;

        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time, x, y; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, int min, int max, int remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        const int WM_PAINT = 0x000F;

        // Every message but WM_PAINT (timers, posted work, the canvas's BeginInvoke): the canvas is never painted by
        // Windows here, so its update region stays for Settle to read.
        static void PumpNoPaint()
        {
            MSG m;
            for (int n = 0; n < 500; n++)
            {
                if (!PeekMessage(out m, IntPtr.Zero, 0, WM_PAINT - 1, 1) && !PeekMessage(out m, IntPtr.Zero, WM_PAINT + 1, 0xFFFF, 1)) break;
                TranslateMessage(ref m);
                DispatchMessage(ref m);
            }
        }

        static void StartScreen()
        {
            if (screen != null) screen.Dispose();
            screen = Full();
        }

        static Bitmap Full() { return Full(false); }

        // truth: the label rendered afresh (EditorRender.Render) instead of the canvas's own kept bitmap, which the
        // editor updates only in parts while dragging (where trails come from).
        static Bitmap Full(bool truth)
        {
            var b = new Bitmap(canvas.Width, canvas.Height);
            var fRendered = typeof(EditorCanvas).GetField("rendered", All);
            var fView = typeof(EditorCanvas).GetField("view", All);
            object keptRendered = fRendered.GetValue(canvas), keptView = fView.GetValue(canvas);
            Bitmap fresh = null;
            if (truth)
            {
                fresh = EditorRender.Render(F<LabelDoc>("doc"), F<LabelLayout>("layout"));
                fRendered.SetValue(canvas, fresh);
                fView.SetValue(canvas, null);
            }
            try { using (var g = Graphics.FromImage(b)) Paint(g, new Rectangle(0, 0, b.Width, b.Height)); }
            finally
            {
                if (truth)
                {
                    var made = fView.GetValue(canvas) as Bitmap;
                    if (made != null) made.Dispose();
                    fRendered.SetValue(canvas, keptRendered);
                    fView.SetValue(canvas, keptView);
                    fresh.Dispose();
                }
            }
            return b;
        }

        static void Paint(Graphics g, Rectangle clip)
        {
            typeof(Control).GetMethod("OnPaintBackground", All).Invoke(canvas, new object[] { new PaintEventArgs(g, clip) });
            typeof(Control).GetMethod("OnPaint", All).Invoke(canvas, new object[] { new PaintEventArgs(g, clip) });
        }

        // Paints what the canvas invalidated into the emulated screen, then lets the real window catch up.
        static void Settle()
        {
            for (int round = 0; round < 20; round++)
            {
                PumpNoPaint();   // timers and posted work, which may invalidate
                IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
                try
                {
                    if (GetUpdateRgn(canvas.Handle, rgn, false) > 1)   // SIMPLEREGION or COMPLEXREGION
                        using (var region = Region.FromHrgn(rgn))
                        using (var g = Graphics.FromImage(screen))
                        {
                            g.Clip = region;
                            Paint(g, Rectangle.Round(region.GetBounds(g)));
                        }
                }
                finally { DeleteObject(rgn); }
                ValidateRect(canvas.Handle, IntPtr.Zero);
                Thread.Sleep(20);
            }
        }

        static void Stale(string name, bool expectDiff = false)
        {
            Settle();
            using (var fresh = Full(true))
            {
                int diff = 0;
                var box = Rectangle.Empty;
                for (int y = 0; y < fresh.Height; y++)
                    for (int x = 0; x < fresh.Width; x++)
                        if (screen.GetPixel(x, y) != fresh.GetPixel(x, y))
                        {
                            diff++;
                            box = box.IsEmpty ? new Rectangle(x, y, 1, 1) : Rectangle.Union(box, new Rectangle(x, y, 1, 1));
                        }
                screen.Save(Path.Combine(outDir, "ui-" + name + "-shown.png"), ImageFormat.Png);
                if (diff > 0) fresh.Save(Path.Combine(outDir, "ui-" + name + "-fresh.png"), ImageFormat.Png);
                Check("stale-" + name, expectDiff ? diff > 0 : diff == 0, diff + " pixels differ" + (diff > 0 ? " in " + box : ""));
            }
        }

        static void Tip(Control icon, string shot)
        {
            typeof(Control).GetMethod("OnClick", All).Invoke(icon, new object[] { EventArgs.Empty });
            Pump(700);
            var tip = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.GetType().Name == "InfoTip");
            if (tip == null) { Check("tip-" + shot, false, "no tip opened"); return; }
            using (var b = Capture(tip)) b.Save(Path.Combine(outDir, shot + ".png"), ImageFormat.Png);
            string text = string.Join(" ", tip.Controls.Cast<Control>().Select(c => c.Text));
            Check("tip-" + shot, true, text.Length > 0 ? text : "(drawn text)");
            tip.Close(); Pump(300);
        }

        static Bitmap Capture(Control c)
        {
            var b = new Bitmap(c.Width, c.Height);
            using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(c.Handle, dc, 2); g.ReleaseHdc(dc); }
            return b;
        }

        static void Shot(string n) { using (var b = Capture(form)) b.Save(Path.Combine(outDir, n + ".png"), ImageFormat.Png); }

        static IEnumerable<Control> AllControls(Control c) { foreach (Control k in c.Controls) { yield return k; foreach (var d in AllControls(k)) yield return d; } }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
