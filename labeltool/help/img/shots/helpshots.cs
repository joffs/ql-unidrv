// Window screenshots of the real QL Label Maker for the help, taken by make-shots.ps1. The editor runs in this process
// (its window class through reflection), on a screen with the given scaling, with the labels in labels\. Runs only on
// deskrun's own desktop (tools\uitest): it never takes the user's mouse or focus. Screenshots with PrintWindow.
// The loaded roll is always the same (FixedRoll: ready, 62 mm continuous), whatever the printer has loaded, so the
// pictures can be compared from release to release; the printer is only named, never printed to.
//   helpshots.exe <QlLabelMaker.exe> <labels folder> <out folder> <de|en> <100|150> [printer]
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
    static string outDir, prefix, printer = "Brother QL-800 (Unidrv)";
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new System.Text.StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop"); return 2; }
        asm = Assembly.LoadFrom(a[0]);
        string dir = a[1]; string lang = a[3]; int scale = int.Parse(a[4]);
        if (a.Length > 5) printer = a[5];
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
        Size size = scale == 100 ? new Size(1180, 680) : new Size(1170, 780);

        Step(dir + "\\sample\\Regal 3.qllabel", target, size, () =>
        {
            Shot("window");
            Select(o => Text(o) == "Regal 3"); NoFontHighlight(); Shot("text");
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
            Select(o => Text(o) == "Regal 3"); NoFontHighlight(); Shot("invert-text");
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
            Pump(500); var cm = (ContextMenuStrip)Field("objectMenu"); FixPaste(cm); ShotOf(cm, "reset-context-menu"); cm.Close(); Pump(300);
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
        Step(dir + "\\shapes\\Regal 3.qllabel", target, size, () =>
        {
            // the palette as on a fresh PC: tool names shown, every group on its first tool (in memory only: the
            // remembered tools and the names setting live in the registry)
            form.GetType().GetField("showToolNames", All).SetValue(form, true);
            form.GetType().GetMethod("SizePalette", All).Invoke(form, null);
            var update = form.GetType().GetMethod("UpdatePaletteButton", All);
            foreach (var g in (IEnumerable)form.GetType().GetMethod("ToolGroups", All).Invoke(form, null))
            {
                var tools = ((IEnumerable)Member(g, "Tools")).Cast<object>().First();
                g.GetType().GetField("Current", All).SetValue(g, tools);
                update.Invoke(form, new[] { g });
            }
            form.PerformLayout(); Pump(600);
            Shot("tools");
            // the Shapes group's flyout, opened as by its corner triangle
            var palette = (ToolStrip)Field("palette");
            var shapes = palette.Items.Cast<ToolStripItem>().First(i => i.Tag != null && (string)Member(i.Tag, "Id") == "shapes");
            form.GetType().GetMethod("ShowFlyout", All).Invoke(form, new object[] { shapes, false }); Pump(600);
            var flyout = (Control)Field("flyout");
            ShotWith(flyout, "tools-flyout");
            ((ToolStripDropDown)flyout).Close(); Pump(300);
            Select(o => o.GetType().Name == "ShapeObject" && Convert.ToString(Member(o, "Kind")) == "Star"); Shot("shape-star");
            // the Edit menu with the star selected
            var edit = (ToolStripMenuItem)((MenuStrip)Field("menuBar")).Items[1];
            edit.ShowDropDown(); Pump(500); FixPaste(edit.DropDown); ShotOf(edit.DropDown, "edit-menu"); edit.HideDropDown(); Pump(300);
            // a right click on an empty spot of the label
            Select(o => false);
            var canvas = (Control)Field("canvas");
            form.GetType().GetMethod("ShowCanvasMenu", All).Invoke(form, new object[] { (Point?)new Point(canvas.Width / 2, canvas.Height * 3 / 4) });
            Pump(500); var cm = (ToolStripDropDown)Field("canvasMenu"); FixPaste(cm); ShotOf(cm, "canvas-menu"); cm.Close(); Pump(300);
        });
        Step(dir + "\\runs\\Regal 3.qllabel", target, size, () =>
        {
            // a paragraph text with words in their own format, edited on the label: the caret after "verzinkt"
            var canvas = (Control)Field("canvas");
            var runs = Objects().First(o => Text(o) != null && Text(o).StartsWith("Schrauben"));
            Edit(runs, Text(runs).IndexOf("verzinkt") + "verzinkt".Length);
            Shot("edit-canvas");
            // a paragraph text whose second line doesn't fit: shown light grey below its box while editing
            var over = Objects().First(o => Text(o) != null && Text(o).StartsWith("Trocken"));
            Edit(over, Text(over).Length);
            Shot("edit-overflow");
            canvas.GetType().GetMethod("EndEdit", All).Invoke(canvas, null); Pump(400);
        });
        Step(dir + "\\printdata\\Regal 3.qllabel", target, size, () =>
        {
            // File > Print with data on a 20-row CSV without a "Lagerort" column: "Leave missing fields empty" on, one card
            // selected. The dialog is modal: a timer takes the shot inside its loop, then cancels it.
            System.Windows.Forms.Form shown = null;
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = Field("printDataDialog") as System.Windows.Forms.Form;
                if (d == null || !d.Visible) return;
                timer.Stop();
                shown = d;
                try
                {
                    d.Size = d.MinimumSize;   // not the size this PC remembers
                    Pump(800);
                    ((CheckBox)d.GetType().GetField("missingEmpty", All).GetValue(d)).Checked = true;
                    Pump(300);
                    var grid = d.GetType().GetField("grid", All).GetValue(d);
                    grid.GetType().GetMethod("ClickCard", All).Invoke(grid, new object[] { 3, Keys.None });
                    Pump(2500);   // the previews are drawn in the background
                    ShotOf(d, "print-data");
                }
                catch (Exception e) { Console.WriteLine("FAILED: " + e); }
                d.DialogResult = DialogResult.Cancel;
            };
            timer.Start();
            form.GetType().GetMethod("PrintWithData", All).Invoke(form, new object[] { dir + "\\printdata\\teile.csv" });
            timer.Dispose();
            if (shown == null) Console.WriteLine("FAILED: the Print with data dialog didn't open");
            Pump(300);
        });
        Step(dir + "\\printdata-linked\\Regal 3.qllabel", target, size, () =>
        {
            // the same label saved with a remembered data file ("printData", next to it): Print with data loads it at
            // once and the data tile says "remembered"; only the tiles are cut out (make-shots.ps1)
            System.Windows.Forms.Form shown = null;
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = Field("printDataDialog") as System.Windows.Forms.Form;
                if (d == null || !d.Visible) return;
                timer.Stop();
                shown = d;
                try
                {
                    d.Size = d.MinimumSize;
                    Pump(800);
                    // as in the picture above, so the missing "Lagerort" doesn't tint the data tile amber
                    ((CheckBox)d.GetType().GetField("missingEmpty", All).GetValue(d)).Checked = true;
                    Pump(2500);   // the file is looked for in the background, then the previews are drawn
                    ShotOf(d, "print-data-linked");
                }
                catch (Exception e) { Console.WriteLine("FAILED: " + e); }
                d.DialogResult = DialogResult.Cancel;
            };
            timer.Start();
            form.GetType().GetMethod("PrintWithData", All).Invoke(form, new object[] { null });
            timer.Dispose();
            if (shown == null) Console.WriteLine("FAILED: the Print with data dialog didn't open (remembered file)");
            Pump(300);
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

    // A step that doesn't finish within 3 minutes (a message box the editor opened, e.g. "objects from a newer version"
    // when out\tools is older than the labels here) ends the run with exit code 3: the windows still open and their
    // texts are logged and the topmost one saved as <prefix>HUNG.png, instead of waiting for ever (2026-11-08, 1.5 h).
    [DllImport("user32.dll")] static extern bool EnumThreadWindows(int id, EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc f, IntPtr p);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder b, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, System.Text.StringBuilder l, int flags, int ms, out IntPtr result);
    delegate bool EnumProc(IntPtr h, IntPtr p);
    static int uiThread;

    static string WindowText(IntPtr h)
    {
        var b = new System.Text.StringBuilder(1024); IntPtr r;
        SendMessageTimeout(h, 0x000D /* WM_GETTEXT */, (IntPtr)1024, b, 2 /* SMTO_ABORTIFHUNG */, 2000, out r);
        return b.ToString();
    }

    static void Hung(string file)
    {
        Console.WriteLine("FAILED: step " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file)) + " hung (no end after 180 s); open windows:");
        IntPtr first = IntPtr.Zero;
        EnumThreadWindows(uiThread, (h, p) =>
        {
            if (!IsWindowVisible(h)) return true;
            if (first == IntPtr.Zero) first = h;
            var cls = new System.Text.StringBuilder(256); GetClassName(h, cls, 256);
            var texts = new List<string>();
            EnumChildWindows(h, (c, q) => { var s = WindowText(c); if (s.Trim().Length > 0 && texts.Count < 12) texts.Add(s.Replace("\r", " ").Replace("\n", " ")); return true; }, IntPtr.Zero);
            Console.WriteLine("  [" + cls + "] \"" + WindowText(h) + "\": " + string.Join(" | ", texts));
            return true;
        }, IntPtr.Zero);
        if (first != IntPtr.Zero)
            try
            {
                RECT r; GetWindowRect(first, out r);
                using (var bmp = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
                {
                    using (var g = Graphics.FromImage(bmp)) { var dc = g.GetHdc(); PrintWindow(first, dc, 2); g.ReleaseHdc(dc); }
                    bmp.Save(System.IO.Path.Combine(outDir, prefix + "HUNG.png"));
                }
            }
            catch (Exception e) { Console.WriteLine("  no picture: " + e.Message); }
        Console.Out.Flush();
        Environment.Exit(3);
    }

    static void Step(string file, Screen target, Size size, Action work)
    {
        if (uiThread == 0) uiThread = GetCurrentThreadId();
        Console.WriteLine("step " + System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file)));
        var watchdog = new System.Threading.Timer(_ => Hung(file), null, 180000, Timeout.Infinite);
        var t = asm.GetType("QlLabels.EditorForm");
        form = (Form)Activator.CreateInstance(t, new object[] { file, printer, null });
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(target.WorkingArea.Left + 40, target.WorkingArea.Top + 40);
        bool done = false;
        form.Shown += delegate
        {
            Pump(800);
            form.Bounds = new Rectangle(target.WorkingArea.Left + 40, target.WorkingArea.Top + 40, size.Width, size.Height);
            Pump(3500);   // layout and the roll check
            FixedRoll();
            try { work(); } catch (Exception e) { Console.WriteLine("FAILED: " + e); }
            done = true;
            Set("dirty", false);
            form.Close();
        };
        form.Show();
        while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
        watchdog.Dispose();
        form.Dispose();
    }

    // Ready, 62 mm continuous loaded (a QL-800's status reply), as the editor's roll; no further roll checks.
    const string Ready62 = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
    static void FixedRoll()
    {
        form.GetType().GetField("roll", All).SetValue(form, FixedCheck(asm, printer));
        form.GetType().GetField("lastRollCheck", All).SetValue(form, DateTime.MaxValue);
        form.GetType().GetMethod("ShowRoll", All).Invoke(form, null);
        Pump(400);
    }

    internal static object FixedCheck(Assembly asm, string printer)
    {
        var device = Activator.CreateInstance(asm.GetType("QlLabels.DeviceResult"));
        Action<string, object> set = (n, v) => device.GetType().GetField(n, All).SetValue(device, v);
        set("Printer", printer);
        set("Port", "USB001");
        set("State", Enum.Parse(asm.GetType("QlLabels.DeviceState"), "Open"));
        set("Reply", asm.GetType("QlLabels.QlStatus").GetMethod("FromHex", All).Invoke(null, new object[] { Ready62, DateTime.Now }));
        var jobs = Activator.CreateInstance(typeof(List<>).MakeGenericType(asm.GetType("QlLabels.JobInfo")));
        return asm.GetType("QlLabels.PrinterCheck").GetMethod("From", All).Invoke(null, new object[] { printer, device, jobs, null });
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

    static List<object> Objects()
    {
        var doc = Field("doc");
        var f = doc.GetType().GetField("Objects", All);
        return ((IEnumerable)(f != null ? f.GetValue(doc) : doc.GetType().GetProperty("Objects", All).GetValue(doc, null))).Cast<object>().ToList();
    }

    // Edits the text on the label with the caret at index caret, the caret shown (not blinking) and the canvas focused.
    static void Edit(object text, int caret)
    {
        var canvas = (Control)Field("canvas");
        canvas.GetType().GetMethod("BeginEdit", All).Invoke(canvas, new object[] { text, null, false });
        Pump(300);
        var editing = canvas.GetType().GetField("editing", All).GetValue(canvas);
        editing.GetType().GetMethod("SetCaret", All).Invoke(editing, new object[] { caret, false });
        form.GetType().GetMethod("EditCaretMoved", All).Invoke(form, null);   // the panel shows the format at the caret
        form.ActiveControl = canvas;
        ((System.Windows.Forms.Timer)canvas.GetType().GetField("blink", All).GetValue(canvas)).Enabled = false;
        canvas.GetType().GetField("caretOn", All).SetValue(canvas, true);
        canvas.Invalidate();
        NoFontHighlight();
        form.ActiveControl = canvas;
        Pump(600);
    }

    static void Select(Func<object, bool> which)
    {
        var objs = Objects();
        var pick = objs.Where(which).ToList();
        var canvas = Field("canvas");
        var lt = asm.GetType("QlLabels.LabelObject");
        var arr = Array.CreateInstance(lt, pick.Count);
        for (int i = 0; i < pick.Count; i++) arr.SetValue(pick[i], i);
        canvas.GetType().GetMethod("Select", All, null, new[] { typeof(IEnumerable<>).MakeGenericType(lt) }, null).Invoke(canvas, new object[] { arr });
        Pump(800);
        Console.WriteLine("selected " + pick.Count);
    }

    // The text panel's font box shows its name selected (blue) after a selection: focus on the canvas, nothing selected.
    static void NoFontHighlight()
    {
        form.ActiveControl = (Control)Field("canvas");
        var box = (ComboBox)Field("fontBox");
        box.SelectionStart = 0; box.SelectionLength = 0;
        Pump(400);
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

    // Paste items as after copying an object in the editor, whatever the user's clipboard holds (deskrun's desktop
    // shares it): Paste on, Paste as plain text off.
    static void FixPaste(ToolStripDropDown menu)
    {
        var t = asm.GetType("QlLabels.Lang").GetMethod("T", All);
        string paste = (string)t.Invoke(null, new object[] { "&Paste" }), plain = (string)t.Invoke(null, new object[] { "Paste as plain te&xt" });
        foreach (ToolStripItem i in menu.Items)
        {
            if (i.Text == paste) i.Enabled = true;
            if (i.Text == plain) i.Enabled = false;
        }
        menu.Refresh(); Pump(200);
    }

    // The window with a drop-down (a flyout) drawn over it where it is on the screen; the picture keeps the window's
    // size and origin, so its crops use the same coordinates as Shot's.
    static void ShotWith(Control extra, string n)
    {
        RECT r; GetWindowRect(form.Handle, out r);
        using (var bmp = new Bitmap(r.R - r.L, r.B - r.T))
        {
            using (var g = Graphics.FromImage(bmp))
                foreach (var c in new[] { (Control)form, extra })
                {
                    RECT cr; GetWindowRect(c.Handle, out cr);
                    using (var part = new Bitmap(cr.R - cr.L, cr.B - cr.T))
                    {
                        using (var pg = Graphics.FromImage(part)) { var dc = pg.GetHdc(); PrintWindow(c.Handle, dc, 2); pg.ReleaseHdc(dc); }
                        g.DrawImage(part, cr.L - r.L, cr.T - r.T);
                    }
                }
            bmp.Save(System.IO.Path.Combine(outDir, prefix + n + ".png"), System.Drawing.Imaging.ImageFormat.Png);
        }
        Console.WriteLine("shot " + prefix + n + " (with " + extra.GetType().Name + ")");
    }
}
