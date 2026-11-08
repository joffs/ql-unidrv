// Screenshots of the label editor's tool groups and shapes (editor-next step 3): the palette, the Shapes and Text
// flyouts, the panel of each shape kind, the palette without tool names, the column wrap in a short window, all at 96
// and 144 dpi. Runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken); changes nothing: no
// registry values (the remembered tools and "Show tool names" are set in memory only), nothing saved or printed.
// Run by shapes-ui.ps1:  shapes_ui.exe <de|en> <out folder>  ->  <out folder>\<lang>-<dpi>-<shot>.png
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

namespace QlLabels.Tests
{
    static class ShapesUi
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);
        struct RECT { public int L, T, R, B; }
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static string outDir, lang;

        [STAThread]
        static int Main(string[] args)
        {
            lang = args[0]; outDir = args[1];
            Directory.CreateDirectory(outDir);
            Lang.Force(lang);
            Application.EnableVisualStyles();
            form = new EditorForm(null, "QL-800 Capture", null) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
            bool done = false;
            form.Shown += delegate
            {
                try { Run(); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); }
                F<object>("dirty", false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
            Console.WriteLine("DONE " + lang);
            return 0;
        }

        static void Run()
        {
            Pump(2000);
            var canvas = F<EditorCanvas>("canvas");
            var doc = F<LabelDoc>("doc");
            foreach (ShapeKind k in Enum.GetValues(typeof(ShapeKind))) Call("AddShape", k);
            var shapes = doc.Objects.OfType<ShapeObject>().ToList();
            shapes[0].Radius = 2;
            shapes[4].Filled = true;
            // remembered tools in memory only: the Shapes group shows the star (as in the mockup)
            var groups = (List<ToolGroup>)Call("ToolGroups");
            groups[1].Current = groups[1].Tools.Last();
            Call("UpdatePaletteButton", groups[1]);
            foreach (int dpi in new[] { 96, 144 })
            {
                if (form.DeviceDpi != dpi) ToDpi(dpi);
                string p = lang + "-" + dpi + "-";
                canvas.Select(new LabelObject[0]); Pump(400);
                Shot(form, p + "window");
                foreach (var s in shapes)
                {
                    canvas.Select(new LabelObject[] { s }); Pump(400);
                    Shot(form, p + "panel-" + s.Kind.ToString().ToLowerInvariant());
                }
                canvas.Select(new LabelObject[0]);
                foreach (int g in new[] { 0, 1, 4 })
                {
                    var palette = F<ToolStrip>("palette");
                    var button = (ToolStripButton)palette.Items[g];
                    Call("ShowFlyout", button, false); Pump(500);
                    var flyout = F<ContextMenuStrip>("flyout");
                    Shot(form, p + "flyout-" + groups[g].Id, flyout);
                    flyout.Close(); Pump(200);
                }
                // without names (in memory: SetToolNames would write the registry)
                F<object>("showToolNames", false); Call("SizePalette"); form.PerformLayout(); Pump(400);
                Shot(form, p + "no-names");
                F<object>("showToolNames", true); Call("SizePalette"); form.PerformLayout(); Pump(400);
                // a window shorter than the minimum: the palette wraps into columns
                var size = form.Size; var min = form.MinimumSize;
                form.MinimumSize = Size.Empty; form.Height = form.LogicalToDeviceUnits(330); Pump(600);
                Shot(form, p + "wrap");
                form.Size = size; form.MinimumSize = min; Pump(400);
                Console.WriteLine("INFO " + dpi + " dpi: palette " + F<ToolStrip>("palette").Width + " px wide, window " + form.Size);
            }
        }

        static void Shot(Form f, string name, Control extra = null)
        {
            var r = f.Bounds;
            if (extra != null && extra.Visible) r = Rectangle.Union(r, extra.Bounds);
            using (var b = new Bitmap(r.Width, r.Height))
            {
                using (var g = Graphics.FromImage(b))
                {
                    g.Clear(Color.Gray);
                    foreach (var c in extra != null && extra.Visible ? new Control[] { f, extra } : new Control[] { f })
                        using (var part = new Bitmap(c.Width, c.Height))
                        {
                            using (var pg = Graphics.FromImage(part)) { var dc = pg.GetHdc(); PrintWindow(c.Handle, dc, 2); pg.ReleaseHdc(dc); }
                            g.DrawImage(part, c.Left - r.Left, c.Top - r.Top);
                        }
                }
                b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
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
