// QL Label Maker's tool bar for drawing (0.23): the arrow at the top and checked by
// default; a mouse click on a tool switches it on (checked, the status bar names it and its keys) and inserts nothing;
// Enter/Space on a tool (a click without the mouse) inserts at a free place as before; the flyout picked with the mouse
// switches the tool on, opened with the Right arrow key it inserts; the arrow switches back; the hint fits the status bar
// at 1024 px; the palette stays one column at the window's normal height. Checks plus screenshots, at 96 and 144 dpi.
// Runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken). Changes nothing: the remembered group
// tools (HKCU ...\LabelMaker Tool.*) are read first and put back at the end; nothing saved or printed.
// Run by draw-ui.ps1:  draw_ui.exe <de|en> <light|dark> <out folder>  ->  CHECK/PASS/FAIL lines, <lang>-<scheme>-<dpi>-<shot>.png
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels.Tests
{
    static class DrawUi
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll", EntryPoint = "PostMessageW")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);
        struct RECT { public int L, T, R, B; }
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string Key = @"Software\QLUnidrv\LabelMaker";

        static EditorForm form;
        static string outDir, lang, scheme, dpiName;
        static int failures;

        [STAThread]
        static int Main(string[] args)
        {
            lang = args[0]; scheme = args[1]; outDir = args[2];
            Directory.CreateDirectory(outDir);
            Lang.Force(lang);
            Theme.Force(scheme == "dark" ? Scheme.Dark : Scheme.Light);
            Application.EnableVisualStyles();
            var saved = SaveTools();
            form = new EditorForm(null, "QL-800 Capture", null) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            var watchdog = new Thread(() => { Thread.Sleep(150000); Console.WriteLine("FAIL watchdog: still running after 150 s"); RestoreTools(saved); Environment.Exit(3); }) { IsBackground = true };
            watchdog.Start();
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Fail("exception " + e); }
                F<object>("dirty", false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            RestoreTools(saved);
            Console.WriteLine("DONE " + lang + " " + scheme + (failures > 0 ? " " + failures + " failed" : ""));
            return failures > 0 ? 1 : 0;
        }

        static void Run()
        {
            Pump(2000);
            foreach (int dpi in new[] { 96, 144 })
            {
                if (form.DeviceDpi != dpi) ToDpi(dpi);
                if (form.DeviceDpi != dpi) { Console.WriteLine("INFO no " + dpi + " dpi here"); continue; }
                dpiName = dpi == 96 ? "100" : "150";
                Steps();
            }
        }

        static void Steps()
        {
            string p = lang + "-" + scheme + "-" + dpiName + "-";
            var palette = F<ToolStrip>("palette");
            var arrow = F<ToolStripButton>("arrowButton");
            var groups = (List<ToolGroup>)Call("ToolGroups");
            var doc = F<LabelDoc>("doc");
            Func<ToolGroup, ToolStripButton> button = g => palette.Items.OfType<ToolStripButton>().First(b => b.Tag == g);
            Func<EditorTool> active = () => (EditorTool)typeof(EditorForm).GetProperty("ActiveTool", All).GetValue(form, null);
            Func<ToolStripStatusLabel> hint = () => F<ToolStripStatusLabel>("toolStatus");
            Action<ToolStripButton> mouseClick = b => { F<object>("mouseUpButton", b); b.PerformClick(); Pump(200); };
            Action<ToolStripButton> keyClick = b => { b.PerformClick(); Pump(300); };
            Call("SelectTool", new object[] { null }); Pump(200);

            // the start: the arrow first and checked, no hint
            Check(palette.Items[0] == arrow && arrow.Checked && active() == null, "the arrow at the top, checked");
            Check(palette.Items.OfType<ToolStripButton>().Count(b => b.Checked) == 1, "only the arrow checked");
            Check(hint() == null || !hint().Visible, "no tool hint with the arrow");
            Check(palette.Items.OfType<ToolStripButton>().All(b => b.Width == arrow.Width && b.Height == arrow.Height), "the arrow as large as the tools");
            int oneColumn = arrow.Width + palette.Padding.Horizontal + S(2);
            Check(palette.Width <= oneColumn, "one column at the window's normal height (" + palette.Width + " <= " + oneColumn + ")");
            Shot("start");

            // a mouse click on Line: switched on, nothing inserted
            var line = groups.First(g => g.Id == "line");
            int count = doc.Objects.Count;
            mouseClick(button(line));
            Check(active() == line.Current && button(line).Checked && !arrow.Checked, "click on Line: the tool on and checked, the arrow not");
            Check(doc.Objects.Count == count, "click on Line: nothing inserted");
            Check(hint() != null && hint().Visible && hint().Text.StartsWith(line.Current.Name + ":") && hint().Text.Contains("Esc"), "the status bar names the tool: " + (hint() == null ? "-" : hint().Text));
            Shot("line");

            // Enter/Space on Shapes (a click without the mouse): inserts as before, the active tool stays
            var shapes = groups.First(g => g.Id == "shapes");
            keyClick(button(shapes));
            Check(doc.Objects.Count == count + 1, "Enter on Shapes: one object inserted (" + (doc.Objects.Count - count) + ")");
            Check(active() == line.Current && button(line).Checked && !button(shapes).Checked, "Enter on Shapes: Line stays the tool");

            // the flyout with the mouse: the pick switches the tool on
            Call("ShowFlyout", button(shapes), false); Pump(400);
            var flyout = F<ContextMenuStrip>("flyout");
            Shot("flyout", flyout);
            var ellipse = shapes.Tools.First(t => t.Id == "ellipse");
            ((ToolStripMenuItem)flyout.Items[shapes.Tools.IndexOf(ellipse)]).PerformClick(); Pump(300);
            Check(active() == ellipse && shapes.Current == ellipse && button(shapes).Checked && !button(line).Checked, "flyout pick with the mouse: Ellipse on, Shapes checked");
            Check(doc.Objects.Count == count + 1, "flyout pick with the mouse: nothing inserted");
            Check(hint().Text.StartsWith(ellipse.Name + ":"), "the hint follows: " + hint().Text);
            Shot("ellipse");

            // the flyout with the Right arrow key: the pick inserts
            Call("ShowFlyout", button(shapes), true); Pump(400);
            flyout = F<ContextMenuStrip>("flyout");
            var star = shapes.Tools.First(t => t.Id == "star");
            ((ToolStripMenuItem)flyout.Items[shapes.Tools.IndexOf(star)]).PerformClick(); Pump(300);
            Check(doc.Objects.Count == count + 2 && active() == ellipse, "flyout by key: Star inserted, Ellipse stays the tool");

            // the longest hint fits the status bar at 1024 px
            var text = groups.First(g => g.Id == "text");
            Call("SelectTool", text.Tools.First(t => t.Id == "artistic")); Pump(200);
            var size = form.Size; var min = form.MinimumSize;
            form.MinimumSize = Size.Empty; form.Width = S(1024); Pump(500);
            var bar = F<StatusStrip>("statusBar");
            var h = hint();
            int need = TextRenderer.MeasureText(h.Text, h.Font).Width;
            Check(h.Visible && h.Bounds.Right <= bar.DisplayRectangle.Right && h.Width >= need, "the artistic text hint fits at 1024 px (" + h.Width + " for " + need + ", bar " + bar.Width + ")");
            Shot("hint-1024");
            form.Size = size; form.MinimumSize = min; Pump(400);

            // a real mouse click (the messages as Windows sends them, not PerformClick): the button's MouseUp comes before
            // its Click, so the click switches the tool on instead of inserting
            Call("SelectTool", new object[] { null }); Pump(200);
            int before = doc.Objects.Count;
            var lb = button(line).Bounds;
            var lp = (IntPtr)(((lb.Y + lb.Height / 2) << 16) | ((lb.X + lb.Width / 2) & 0xFFFF));
            PostMessage(palette.Handle, 0x0200, IntPtr.Zero, lp);   // WM_MOUSEMOVE: the button under the mouse
            PostMessage(palette.Handle, 0x0201, (IntPtr)1, lp);      // WM_LBUTTONDOWN
            PostMessage(palette.Handle, 0x0202, IntPtr.Zero, lp);    // WM_LBUTTONUP
            Pump(300);
            Check(active() == line.Current && doc.Objects.Count == before, "a real mouse click on Line: switched on, nothing inserted (" +
                  (active() != null ? active().Id : "arrow") + ", " + (doc.Objects.Count - before) + " inserted)");

            // the tooltips name the drawing and the key that inserts (texts 2026-11-10)
            Check(button(line).ToolTipText.StartsWith(Lang.T("Draw a line · Ctrl+L inserts it")), "Line's tooltip: " + button(line).ToolTipText.Split('\n')[0]);
            Check(arrow.ToolTipText.Contains(Lang.T("Select, move and change objects (Esc)")), "the arrow's tooltip unchanged");

            // the live hints while dragging (the form's Measure), in the culture's numbers
            var dm = new DrawMeasure { Tool = line.Current, Length = 42, Angle = 25.2 };
            string dh = (string)Call("DragHint", dm);
            Check(dh.StartsWith(line.Current.Name + ": ") && dh.Contains(42.0.ToString("0.0") + " mm, 25°") && dh.EndsWith(Lang.T("Esc = cancel")), "drag hint, line: " + dh);
            var rect = shapes.Tools.First(t => t.Id == "rectangle");
            dh = (string)Call("DragHint", new DrawMeasure { Tool = rect, W = 30, H = 12.5 });
            Check(dh.Contains(30.0.ToString("0.0") + " × " + 12.5.ToString("0.0") + " mm"), "drag hint, rectangle: " + dh);
            var artistic = text.Tools.First(t => t.Id == "artistic");
            dh = (string)Call("DragHint", new DrawMeasure { Tool = artistic, FontSize = 24.3 });
            Check(dh.Contains(24.5.ToString("0.#") + " pt"), "drag hint, artistic text (half points): " + dh);

            // typing a drawn text: "Text eingeben - Esc = fertig"; an untyped one goes again when the editing ends
            Call("SelectTool", artistic); Pump(200);
            int beforeText = doc.Objects.Count;
            Call("OnDrawn", new PointF(10, 10), new PointF(10, 10), true); Pump(400);
            Check(hint().Visible && hint().Text.Contains(Lang.T("type the text")) && hint().Text.Contains(Lang.T("Esc = done")), "typing a drawn text: " + hint().Text);
            Check(!F<ToolStripStatusLabel>("labelStatus").Text.Contains(Lang.T("Editing text – Esc or a click beside it ends.")), "no second editing message beside it");
            Shot("typing");
            var cv = F<EditorCanvas>("canvas");
            cv.GetType().GetMethods(All).First(m => m.Name == "EndEdit" && m.GetParameters().Length == 0).Invoke(cv, null); Pump(400);
            Check(doc.Objects.Count == beforeText && hint().Text.StartsWith(artistic.Name + ": ") && !hint().Text.Contains(Lang.T("type the text")), "editing ended untyped: the text gone, the tool hint again: " + hint().Text);

            // the arrow switches back
            mouseClick(arrow);
            Check(active() == null && arrow.Checked && palette.Items.OfType<ToolStripButton>().Count(b => b.Checked) == 1, "the arrow: back to select, only it checked");
            Check(!hint().Visible, "the arrow: the hint gone");

            // without names: the arrow too
            F<object>("showToolNames", false); Call("SizePalette"); form.PerformLayout(); Pump(400);
            Check(arrow.DisplayStyle == ToolStripItemDisplayStyle.Image && arrow.Width == button(line).Width, "without names: the arrow an icon like the others");
            Shot("no-names");
            F<object>("showToolNames", true); Call("SizePalette"); form.PerformLayout(); Pump(400);

            // tidy: the inserted objects out, the document unchanged
            while (doc.Objects.Count > count) doc.Objects.RemoveAt(doc.Objects.Count - 1);
            form.Invalidate(true); Pump(200);
        }

        static int S(int v) { return form.LogicalToDeviceUnits(v); }

        static void Check(bool ok, string what)
        {
            Console.WriteLine("CHECK " + lang + " " + scheme + " " + dpiName + "%: " + what + (ok ? " PASS" : " FAIL"));
            if (!ok) failures++;
        }

        static void Fail(string what) { Console.WriteLine("FAIL " + what); failures++; }

        static Dictionary<string, object> SaveTools()
        {
            var d = new Dictionary<string, object>();
            using (var k = Registry.CurrentUser.OpenSubKey(Key))
                if (k != null) foreach (var n in k.GetValueNames().Where(n => n.StartsWith("Tool."))) d[n] = k.GetValue(n);
            return d;
        }

        static void RestoreTools(Dictionary<string, object> saved)
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key, true))
                {
                    if (k == null) return;
                    foreach (var n in k.GetValueNames().Where(n => n.StartsWith("Tool.")))
                        if (!saved.ContainsKey(n)) k.DeleteValue(n, false);
                    foreach (var kv in saved) k.SetValue(kv.Key, kv.Value);
                }
            }
            catch (Exception e) { Console.WriteLine("FAIL restoring the remembered tools: " + e.Message); }
        }

        static void Shot(string name, Control extra = null)
        {
            var r = form.Bounds;
            if (extra != null && extra.Visible) r = Rectangle.Union(r, extra.Bounds);
            using (var b = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Gray);
                    foreach (var c in extra != null && extra.Visible ? new Control[] { form, extra } : new Control[] { form })
                        using (var part = new Bitmap(c.Width, c.Height))
                        {
                            using (var pg = Graphics.FromImage(part)) { var dc = pg.GetHdc(); PrintWindow(c.Handle, dc, 2); pg.ReleaseHdc(dc); }
                            g.DrawImage(part, c.Left - r.Left, c.Top - r.Top);
                        }
                }
                b.Save(Path.Combine(outDir, lang + "-" + scheme + "-" + dpiName + "-" + name + ".png"), ImageFormat.Png);
            }
        }

        static void ToDpi(int dpi)
        {
            var r = new RECT { L = form.Left, T = form.Top, R = form.Left + form.Width * dpi / form.DeviceDpi, B = form.Top + form.Height * dpi / form.DeviceDpi };
            SendMessage(form.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref r);
            Pump(1500);
        }

        static T F<T>(string field, object set = null)
        {
            var f = typeof(EditorForm).GetField(field, All);
            if (f == null) throw new MissingFieldException("EditorForm", field);
            if (set != null) f.SetValue(form, set);
            return (T)f.GetValue(form);
        }

        static object Call(string method, params object[] args)
        {
            var m = typeof(EditorForm).GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == args.Length);
            return m.Invoke(form, args);
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
