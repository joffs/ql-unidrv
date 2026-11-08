// "Larger than the label" and "Fit to label" in QL Label Maker: a 10000 x 10000 px picture pasted at its size (96 dpi,
// 2.6 m: no hang, the bar shows), the bar's link fits it (centred, in proportion, one undo step), × and the next edit close
// the bar, a small picture brings no bar, the object menu and the picture panel offer "Fit to label" for pictures only,
// and the window with the bar and a picture's panel stays within 690 px. Prints CHECK lines; changes nothing (nothing
// saved or printed). Runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken).
// Run by fit-ui.ps1:  fit_ui.exe <de|en>  ->  CHECK lines
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class FitUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
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

        static Panel Bar() { return F<Panel>("fitBar"); }
        static LabelDoc Doc() { return F<LabelDoc>("doc"); }
        static RectangleF Printable() { return F<LabelLayout>("layout").Printable; }
        static string Mm(RectangleF r) { return r.Width.ToString("0.0") + " x " + r.Height.ToString("0.0") + " mm at " + r.X.ToString("0.0") + ", " + r.Y.ToString("0.0"); }

        static IEnumerable<Control> Under(Control c) { return c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Under(x))); }

        // A pasted picture of w x h pixels at 96 dpi (as the clipboard reader gives it: its size in mm), placed like a paste.
        static double Paste(int w, int h)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                using (var pen = new Pen(Color.Black, Math.Max(1, w / 40)))
                {
                    g.DrawEllipse(pen, w / 10, h / 10, w * 8 / 10, h * 8 / 10);
                    g.DrawLine(pen, 0, 0, w, h);
                }
            }
            var c = new ClipContent { Kind = ClipKind.Picture, Picture = bmp, SizeMm = new SizeF(w * 25.4f / 96, h * 25.4f / 96) };
            var watch = Stopwatch.StartNew();
            Call("Place", c, null);
            Pump(400);
            return watch.Elapsed.TotalSeconds;
        }

        static void Run()
        {
            Pump(1500);
            var canvas = F<EditorCanvas>("canvas");
            var p = Printable();

            // A huge picture: pasted at its size, no hang; the bar says so.
            double s = Paste(10000, 10000);
            var big = Doc().Objects.Last();
            Check(s < 20, "10000 x 10000 px pasted in " + s.ToString("0.0") + " s");
            Check(big.W > 2600 && big.H > 2600, "pasted at its size: " + Mm(big.Box));
            Check(Bar().Visible, "bar shown after an oversized paste");
            // The panel shows the real place and size (no -500 / 3000 mm stop), typing in one field moves nothing else,
            // and a saved label keeps them.
            var numX = F<NumericUpDown>("numX"); var numY = F<NumericUpDown>("numY"); var numW = F<NumericUpDown>("numW");
            Check(Math.Abs((double)numX.Value - big.X) < 0.06 && Math.Abs((double)numY.Value - big.Y) < 0.06 && Math.Abs((double)numW.Value - big.W) < 0.06,
                  "panel shows the real values: " + numX.Value + " / " + numY.Value + " / " + numW.Value);
            double x0 = big.X, w0 = big.W, y0 = big.Y;
            numY.Value = numY.Value + 1;
            Pump(200);
            Check(Math.Abs(big.Y - (y0 + 1)) < 0.06 && big.X == x0 && big.W == w0, "typing Top moves only Top: X " + big.X + ", Y " + y0 + " -> " + big.Y);
            var again = LabelDoc.FromJson(Doc().ToJson()).Objects.Last();
            Check(Math.Abs(again.X - big.X) < 0.01 && Math.Abs(again.W - big.W) < 0.01, "saved and opened again: X " + again.X + ", W " + again.W);
            Call("Undo", false);   // back to the paste (the bar went with the edit)
            Pump(200);
            big = Doc().Objects.Last();
            Call("OfferFit", Doc().Objects.ToList());
            Pump(200);
            if (Environment.GetEnvironmentVariable("QL_FIT_SHOT") != null)   // a picture of the window with the bar (for a look)
                using (var shot = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(shot, new Rectangle(Point.Empty, form.Size)); shot.Save(Environment.GetEnvironmentVariable("QL_FIT_SHOT") + "-" + Lang.Current + ".png"); }
            Check(Under(Bar()).OfType<Label>().Any(l => l.Text == Lang.T("Larger than the label.")) &&
                  Under(Bar()).OfType<LinkLabel>().Any(l => l.Text == Lang.T("Fit to label")), "bar text and link: " +
                  string.Join(" | ", Under(Bar()).OfType<Label>().Select(l => l.Text)));
            Check(Bar().Bottom <= F<Panel>("host").Parent.Parent.ClientSize.Height && Bar().Height < form.Font.Height * 3, "bar one line high: " + Bar().Height + " px");

            // The window with the bar and the picture's panel: within 690 px, the panel's buttons visible without scrolling.
            var side = F<Panel>("side");
            var fitButton = Under(F<TableLayoutPanel>("props")).OfType<Button>().FirstOrDefault(x => x.Text == Lang.T("Fit to label"));
            Check(form.Height <= 690 * form.DeviceDpi / 96, "window height " + form.Height + " px at " + form.DeviceDpi + " dpi");
            Check(fitButton != null && fitButton.Visible, "picture panel: Fit to label button");
            if (fitButton != null)
            {
                var r = side.RectangleToClient(fitButton.RectangleToScreen(fitButton.ClientRectangle));
                Check(r.Bottom <= side.ClientSize.Height, "panel button within the panel: bottom " + r.Bottom + " of " + side.ClientSize.Height);
            }

            // The link: fitted in proportion, centred on the printable area, the bar gone; one undo step back.
            int undoBefore = UndoCount();
            Under(Bar()).OfType<LinkLabel>().First(l => l.Text == Lang.T("Fit to label")).GetType()
                .GetMethod("OnLinkClicked", All).Invoke(Under(Bar()).OfType<LinkLabel>().First(l => l.Text == Lang.T("Fit to label")),
                    new object[] { new LinkLabelLinkClickedEventArgs(null) });
            Pump(400);
            var b = big.Box;
            Check(b.Left >= p.Left - 0.05 && b.Top >= p.Top - 0.05 && b.Right <= p.Right + 0.05 && b.Bottom <= p.Bottom + 0.05,
                  "fitted into the printable area " + Mm(p) + ": " + Mm(b));
            Check(Math.Abs((b.Left + b.Right) / 2 - (p.Left + p.Right) / 2) < 0.1 && Math.Abs((b.Top + b.Bottom) / 2 - (p.Top + p.Bottom) / 2) < 0.1, "centred");
            Check(Math.Abs(b.Width - b.Height) < 0.1, "in proportion (square stays square)");
            Check(Math.Abs(b.Height - p.Height) < 0.1 || Math.Abs(b.Width - p.Width) < 0.1, "as large as fits");
            Check(!Bar().Visible, "bar gone after fitting");
            Check(UndoCount() == undoBefore + 1, "one undo step");
            Call("Undo", false);
            Pump(300);
            Check(Doc().Objects.Last().W > 2600, "undo: back at its size");
            Check(!Bar().Visible, "undo doesn't bring the bar back");

            // × closes it.
            Doc().Objects.Clear(); Call("ChangedAll"); Pump(200);
            Paste(3000, 600);
            Check(Bar().Visible, "bar after a wide paste (3000 x 600 px)");
            var close = Under(Bar()).OfType<Label>().First(l => l.Text == "×");
            close.GetType().GetMethod("OnClick", All).Invoke(close, new object[] { EventArgs.Empty });
            Pump(200);
            Check(!Bar().Visible && Doc().Objects.Last().W > 700, "× closes the bar, the picture stays as pasted");

            // The next edit closes it.
            Paste(3000, 600);
            Check(Bar().Visible, "bar again");
            Call("Rotate");
            Pump(200);
            Check(!Bar().Visible, "an edit (rotate) closes the bar");

            // A small picture: no bar.
            Paste(200, 100);
            Check(!Bar().Visible, "small picture (200 x 100 px = " + Mm(Doc().Objects.Last().Box) + "): no bar");

            // The object menu: "Fit to label" for pictures, not for a text among them.
            var picture = Doc().Objects.Last();
            canvas.Select(new[] { picture });
            Check(MenuHas(canvas), "object menu: Fit to label for a picture");
            var text = new TextObject { Text = "Test", Size = 12, X = 2, Y = 2 };
            EditorRender.FitText(text);
            Doc().Objects.Add(text);
            canvas.Select(new LabelObject[] { picture, text });
            Check(!MenuHas(canvas), "object menu: no Fit to label with a text selected");

            // Two pictures fitted together: both inside, their arrangement kept.
            Doc().Objects.Remove(text);
            var pics = Doc().Objects.OfType<ImageObject>().Cast<LabelObject>().ToList();
            canvas.Select(pics);
            Call("FitToLabel", (IList<LabelObject>)pics);
            Pump(200);
            var box = EditorCanvas.BoxOf(pics);
            Check(box.Left >= p.Left - 0.05 && box.Right <= p.Right + 0.05 && box.Top >= p.Top - 0.05 && box.Bottom <= p.Bottom + 0.05,
                  "several pictures fitted together: " + Mm(box));
        }

        static int UndoCount()
        {
            var undo = typeof(EditorForm).GetField("undo", All).GetValue(form);
            foreach (var name in new[] { "Count", "UndoCount" })
            {
                var prop = undo.GetType().GetProperty(name, All);
                if (prop != null) return (int)prop.GetValue(undo, null);
            }
            var stack = undo.GetType().GetFields(All).Select(f => f.GetValue(undo)).OfType<System.Collections.ICollection>().First();
            return stack.Count;
        }

        static bool MenuHas(EditorCanvas canvas)
        {
            Call("ShowObjectMenu", new Point(10, 10));
            Pump(200);
            var menu = F<ContextMenuStrip>("objectMenu");
            bool has = menu.Items.OfType<ToolStripMenuItem>().Any(i => i.Text == Lang.T("Fit to &label"));
            menu.Close();
            Pump(100);
            return has;
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
