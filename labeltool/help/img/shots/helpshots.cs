// Window screenshots of the real QL Label Maker for the help, taken by make-shots.ps1. The editor runs in this process
// (its window class through reflection), on a screen with the given scaling, with the labels in labels\. Runs only on
// deskrun's own desktop (tools\uitest): it never takes the user's mouse or focus. Screenshots with PrintWindow.
//   helpshots.exe <QlLabelMaker.exe> <labels folder> <out folder> <de|en> <100|150>
// -> <out folder>\<scale>-<lang>\<scale>-<lang>-<shot>.png
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class HelpShots
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, System.Text.StringBuilder b, int n, out int need);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point p, uint f);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr m, int t, out uint x, out uint y);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    struct RECT { public int L, T, R, B; }

    static Assembly asm;
    static Form form;
    static string outDir, prefix;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new System.Text.StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop"); return 2; }
        asm = Assembly.LoadFrom(a[0]);
        string dir = a[1]; string lang = a[3]; int scale = int.Parse(a[4]);
        prefix = scale + "-" + lang + "-";
        outDir = System.IO.Path.Combine(a[2], scale + "-" + lang);
        System.IO.Directory.CreateDirectory(outDir);
        asm.GetType("QlLabels.Lang").GetMethod("Force", All).Invoke(null, new object[] { lang });
        // the manual shows the light scheme, whatever this PC's Appearance setting is
        asm.GetType("QlLabels.Theme").GetMethod("Force", All).Invoke(null, new[] { Enum.Parse(asm.GetType("QlLabels.Scheme"), "Light") });
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Screen target = Screen.AllScreens.FirstOrDefault(s => Dpi(s) == scale * 96 / 100) ?? Screen.PrimaryScreen;
        Console.WriteLine("screen " + target.DeviceName + " dpi " + Dpi(target));
        Size size = scale == 100 ? new Size(1100, 680) : new Size(1170, 780);

        Step(dir + "\\sample\\Regal 3.qllabel", target, size, () =>
        {
            Shot("window");
            Select(o => Text(o) == "Regal 3"); Shot("text");
            Select(o => o.GetType().Name == "ImageObject"); Shot("picture");
            Select(o => Kind(o) == "Qr"); Shot("qr");
            Select(o => Text(o) == "Regal 3" || Kind(o) == "Qr" || Kind(o) == "Code128"); Shot("three");
        });
        Step(dir + "\\length\\Regal 3.qllabel", target, size, () =>
        {
            Shot("length");
            Set("autoCheck", true); Shot("length-auto");
            Set("autoCheck", false);
            ((NumericUpDown)Field("lengthNum")).Value = 8; Pump(600); Shot("length-error");
            ((NumericUpDown)Field("lengthNum")).Value = 40; Pump(600);
            // a die-cut label: its fixed length
            var box = (ComboBox)Field("labelBox");
            for (int i = 0; i < box.Items.Count; i++)
                if (box.Items[i].ToString().StartsWith("62mm x 29mm")) { box.SelectedIndex = i; break; }
            Pump(1200); Shot("length-diecut");
        });
        Step(dir + "\\invert\\Regal 3.qllabel", target, size, () =>
        {
            Shot("invert-none");
            Select(o => Text(o) == "Regal 3"); Shot("invert-text");
            Select(o => Text(o) != null && Text(o).StartsWith("Schrauben")); Shot("invert-frame");
            Select(o => Kind(o) == "Qr"); Shot("invert-qr");
            Select(o => Kind(o) == "Code128"); Shot("invert-code128");
            Select(o => Text(o) == "NEU"); Shot("invert-neu");
        });
        Step(dir + "\\reset\\Regal 3.qllabel", target, size, () =>
        {
            Select(o => Text(o) == "NEU"); Shot("reset-before");
            var menuBar = (MenuStrip)Field("menuBar");
            var arrange = menuBar.Items.OfType<ToolStripMenuItem>().First(i => i.DropDownItems.OfType<ToolStripMenuItem>().Any(x => x.Text.Replace("&", "").Contains("90°")));
            arrange.ShowDropDown(); Pump(500); ShotOf(arrange.DropDown, "reset-arrange-menu"); arrange.HideDropDown(); Pump(300);
            var canvas = (Control)Field("canvas");
            form.GetType().GetMethod("ShowObjectMenu", All).Invoke(form, new object[] { new Point(canvas.Width / 2, canvas.Height / 2) });
            Pump(500); var cm = (ContextMenuStrip)Field("objectMenu"); ShotOf(cm, "reset-context-menu"); cm.Close(); Pump(300);
            form.GetType().GetMethod("ResetRotation", All).Invoke(form, null); Pump(600); Shot("reset-after");
        });
        Step(dir + "\\line\\Regal 3.qllabel", target, size, () =>
        {
            Select(o => o.GetType().Name == "LineObject" && (bool)Member(o, "Rising")); Shot("line-selected");
            // drag the upper end to 43° from the lower one: it snaps to 45° and shows angle and length
            var canvas = (Control)Field("canvas");
            var line = ((IEnumerable)canvas.GetType().GetProperty("Selection").GetValue(canvas, null)).Cast<object>().First();
            PointF from = (PointF)Member(line, "Start"), end = (PointF)Member(line, "End");
            double len = Math.Sqrt((end.X - from.X) * (end.X - from.X) + (end.Y - from.Y) * (end.Y - from.Y)), turn = 43 * Math.PI / 180;
            var toScreen = canvas.GetType().GetMethod("ToScreen", All, null, new[] { typeof(double), typeof(double) }, null);
            var p0 = (PointF)toScreen.Invoke(canvas, new object[] { (double)end.X, (double)end.Y });
            var p1 = (PointF)toScreen.Invoke(canvas, new object[] { from.X + len * Math.Cos(turn), from.Y - len * Math.Sin(turn) });
            Mouse(canvas, "OnMouseDown", p0); Mouse(canvas, "OnMouseMove", p0); Mouse(canvas, "OnMouseMove", p1);
            Pump(600); Shot("line-dragging");
            Mouse(canvas, "OnMouseUp", p1); Pump(400);
        });
        Step(dir + "\\lineout\\Regal 3.qllabel", target, size, () =>
        {
            Select(o => o.GetType().Name == "LineObject"); Shot("lineout-selected");
        });
        Step(dir + "\\errors\\Regal 3.qllabel", target, size, () =>
        {
            // the dialog Print shows before printing a label with errors, shown here without printing
            var d = (Form)form.GetType().GetMethod("ProblemsDialog", All).Invoke(form, null);
            d.StartPosition = FormStartPosition.Manual;
            d.Location = new Point(form.Left + 200, form.Top + 150);
            d.Show(form); Pump(1200); ShotOf(d, "errors-dialog"); d.Close(); d.Dispose(); Pump(300);
        });
        Step(dir + "\\qr\\Regal 3.qllabel", target, size, () => { Select(o => Kind(o) == "Qr"); Shot("qr-zone"); });
        Step(dir + "\\barcode\\Regal 3.qllabel", target, size, () =>
        {
            Select(o => Kind(o) == "Code128"); Shot("code-c128");
            Select(o => Kind(o) == "Gs1_128"); Shot("code-gs1");
            var w = (NumericUpDown)Field("numW");
            w.Value = 30; Pump(800); Shot("code-gs1-hint");
            w.Value = 12; Pump(800); Shot("code-gs1-narrow");
            w.Value = 60; Pump(600);
        });
        return 0;
    }

    // A mouse event straight into the canvas (no real mouse: it never moves the user's pointer).
    static void Mouse(Control c, string what, PointF at)
    {
        c.GetType().GetMethod(what, All).Invoke(c, new object[] { new MouseEventArgs(MouseButtons.Left, 1, (int)Math.Round(at.X), (int)Math.Round(at.Y), 0) });
        Pump(150);
    }

    static int Dpi(Screen s)
    {
        uint x, y;
        var m = MonitorFromPoint(new Point(s.Bounds.Left + 10, s.Bounds.Top + 10), 2);
        return GetDpiForMonitor(m, 0, out x, out y) == 0 ? (int)x : 96;
    }

    static void Step(string file, Screen target, Size size, Action work)
    {
        var t = asm.GetType("QlLabels.EditorForm");
        form = (Form)Activator.CreateInstance(t, new object[] { file, "Brother QL-800 (Unidrv)", null });
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(target.WorkingArea.Left + 40, target.WorkingArea.Top + 40);
        bool done = false;
        form.Shown += delegate
        {
            Pump(800);
            form.Bounds = new Rectangle(target.WorkingArea.Left + 40, target.WorkingArea.Top + 40, size.Width, size.Height);
            Pump(3500);   // layout and the roll check
            try { work(); } catch (Exception e) { Console.WriteLine("FAILED: " + e); }
            done = true;
            Set("dirty", false);
            form.Close();
        };
        form.Show();
        while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
        form.Dispose();
    }

    static object Field(string n) { return form.GetType().GetField(n, All).GetValue(form); }
    static void Set(string n, bool v)
    {
        var o = Field(n);
        if (o is CheckBox) ((CheckBox)o).Checked = v; else form.GetType().GetField(n, All).SetValue(form, v);
        Pump(800);
    }

    static object Member(object o, string n) { var f = o.GetType().GetField(n, All); if (f != null) return f.GetValue(o); var p = o.GetType().GetProperty(n, All); return p != null ? p.GetValue(o, null) : null; }
    static string Text(object o) { return o.GetType().Name == "TextObject" ? Member(o, "Text") as string : null; }
    static string Kind(object o) { return o.GetType().Name == "BarcodeObject" ? Convert.ToString(Member(o, "Kind")) : null; }

    static void Select(Func<object, bool> which)
    {
        var doc = Field("doc");
        var objs = ((IEnumerable)doc.GetType().GetField("Objects", All).GetValue(doc) ?? (IEnumerable)doc.GetType().GetProperty("Objects", All).GetValue(doc, null)).Cast<object>().ToList();
        var pick = objs.Where(which).ToList();
        var canvas = Field("canvas");
        var lt = asm.GetType("QlLabels.LabelObject");
        var arr = Array.CreateInstance(lt, pick.Count);
        for (int i = 0; i < pick.Count; i++) arr.SetValue(pick[i], i);
        canvas.GetType().GetMethod("Select", All, null, new[] { typeof(IEnumerable<>).MakeGenericType(lt) }, null).Invoke(canvas, new object[] { arr });
        Pump(800);
        Console.WriteLine("selected " + pick.Count);
    }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
    }

    static void Shot(string n) { ShotOf(form, n); }

    static void ShotOf(Control c, string n)
    {
        RECT r; GetWindowRect(c.Handle, out r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                var dc = g.GetHdc();
                PrintWindow(c.Handle, dc, 2);
                g.ReleaseHdc(dc);
            }
            bmp.Save(System.IO.Path.Combine(outDir, prefix + n + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("shot " + prefix + n + " " + (r.R - r.L) + "x" + (r.B - r.T));
    }
}
