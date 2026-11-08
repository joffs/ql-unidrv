// Window screenshots of the real QL Label Formats for its manual, taken by make-shots.ps1. The program runs in this
// process (its window classes through reflection) on a screen with the given scaling. It shows the sample label
// formats of labels-<lang>.json in place of this PC's, and this PC's "Brother QL-800 (Unidrv)" (or the printer given) as the only printer in the
// status bar; nothing is saved or printed. Runs only on deskrun's own desktop (tools\uitest): it never takes the user's
// mouse or focus. Screenshots with PrintWindow.
// The loaded roll is always the same (FixedCheck: ready, 62 mm continuous), whatever the printer has loaded, so the
// pictures can be compared from release to release.
//   formatshots.exe <QlLabelFormats.exe> <out folder> <de|en> <100|150> <shots folder> [printer]
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

static class FormatShots
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
    static string outDir, prefix, samples, printer = "Brother QL-800 (Unidrv)";
    static Screen target;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new System.Text.StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop"); return 2; }
        asm = Assembly.LoadFrom(a[0]);
        string lang = a[2]; int scale = int.Parse(a[3]);
        prefix = scale + "-" + lang + "-";
        outDir = System.IO.Path.Combine(a[1], scale + "-" + lang);
        System.IO.Directory.CreateDirectory(outDir);
        samples = System.IO.Path.Combine(a[4], "labels-" + lang + ".json");
        if (a.Length > 5) printer = a[5];
        asm.GetType("QlLabels.Lang").GetMethod("Force", All).Invoke(null, new object[] { lang });
        // the manual shows the light scheme, whatever this PC's Appearance setting is
        asm.GetType("QlLabels.Theme").GetMethod("Force", All).Invoke(null, new[] { Enum.Parse(asm.GetType("QlLabels.Scheme"), "Light") });
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        target = Screen.AllScreens.FirstOrDefault(s => Dpi(s) == scale * 96 / 100) ?? Screen.PrimaryScreen;
        Console.WriteLine("screen " + target.DeviceName + " dpi " + Dpi(target));

        form = (Form)Activator.CreateInstance(asm.GetType("QlLabels.MainForm"));
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(target.WorkingArea.Left + 40, target.WorkingArea.Top + 40);
        bool done = false;
        form.Shown += delegate
        {
            Pump(5000);   // layout, the printer list and the roll check
            try { Work(); } catch (Exception e) { Console.WriteLine("FAILED: " + e); }
            done = true;
            form.GetType().GetField("dirty", All).SetValue(form, false);
            form.Close();
        };
        form.Show();
        while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
        return 0;
    }

    static void Work()
    {
        // the QL-800 on USB as the test printer (its roll is read; a shared printer would show the other PC's address)
        var box = (ToolStripComboBox)Field("printerBox");
        for (int i = 0; i < box.Items.Count; i++)
            if (box.Items[i].ToString() == printer) { box.SelectedIndex = i; break; }
        Pump(4000);
        // the fixed roll in place of the printer's, and no further checks
        ((System.Windows.Forms.Timer)Field("printerTimer")).Stop();
        form.GetType().GetField("checking", All).SetValue(form, true);
        form.GetType().GetField("printerCheck", All).SetValue(form, FixedCheck(printer));
        form.GetType().GetMethod("ShowPrinterCheck", All).Invoke(form, null);
        Pump(400);
        Samples();
        var list = (ListView)Field("list");
        Console.WriteLine("formats: " + string.Join(" | ", list.Items.Cast<ListViewItem>().Select(i => i.Text + (i.ForeColor == Dim() ? " (grey)" : ""))));
        SelectItem(list, 0);
        Shot(form, "window");
        foreach (ToolStripMenuItem m in ((MenuStrip)Field("menu")).Items)
        {
            m.ShowDropDown(); Pump(500);
            ShotOf(m.DropDown, "menu-" + m.Text.Replace("&", "").ToLowerInvariant());
            m.HideDropDown(); Pump(300);
        }
        var cm = list.ContextMenuStrip;
        var at = list.Items[0].Bounds.Location;
        cm.Show(list, new Point(at.X + 40, at.Y + 8)); Pump(500); ShotOf(cm, "context-menu"); cm.Close(); Pump(300);
        // a greyed format: one that doesn't fit the loaded roll
        var grey = list.Items.Cast<ListViewItem>().FirstOrDefault(i => i.ForeColor == Dim());
        if (grey != null) { SelectItem(list, grey.Index); Shot(form, "greyed"); }
        // a new format (not saved)
        form.GetType().GetMethod("NewFormat", All, null, Type.EmptyTypes, null).Invoke(form, null); Pump(800);
        Shot(form, "new");
        // the printer status window
        var status = (Form)Activator.CreateInstance(asm.GetType("QlLabels.StatusForm"), new object[] { new List<string> { printer }, null });
        Place(status); status.Show(form); Pump(5000);
        ((System.Windows.Forms.Timer)status.GetType().GetField("timer", All).GetValue(status)).Stop();
        status.GetType().GetField("running", All).SetValue(status, true);
        foreach (var card in (IEnumerable)status.GetType().GetField("cards", All).GetValue(status))
            card.GetType().GetMethod("Display", All).Invoke(card, new[] { FixedCheck(printer) });
        Pump(800); Shot(status, "status"); status.Close(); Pump(300);
        // the printer setup window
        var setup = (Form)Activator.CreateInstance(asm.GetType("QlLabels.SetupForm"));
        Place(setup); setup.Show(form); Pump(4000); Shot(setup, "setup"); setup.Close(); Pump(300);
    }

    // The sample formats in place of this PC's (in memory only), shown as saved and applied
    static void Samples()
    {
        var store = asm.GetType("QlLabels.LabelStore");
        var set = store.GetMethod("Load", All).Invoke(null, new object[] { samples });
        var formats = (IList)set.GetType().GetField("Formats", All).GetValue(set);
        var saved = (IList)Activator.CreateInstance(formats.GetType());
        foreach (var f in formats)
        {
            store.GetMethod("Normalize", All).Invoke(null, new[] { f });
            saved.Add(f.GetType().GetMethod("Clone", All).Invoke(f, null));
        }
        form.GetType().GetField("formats", All).SetValue(form, formats);
        form.GetType().GetField("saved", All).SetValue(form, saved);
        form.GetType().GetMethod("FillList", All).Invoke(form, new[] { formats[0] });
        form.GetType().GetMethod("SetDirty", All).Invoke(form, new object[] { false });
        Pump(800);
    }

    // The status bar as on a PC with one QL printer, its label formats applied
    static void StatusBar()
    {
        var lang = asm.GetType("QlLabels.Lang");
        string version = (string)asm.GetType("QlLabels.AboutForm").GetMethod("DriverVersion", All).Invoke(null, null);
        ((ToolStripItem)Field("printerStatus")).Text = (string)lang.GetMethod("F", All).Invoke(null,
            new object[] { "QL Unidrv {0} · {1}", new object[] { version, printer } });
        ((ToolStripItem)Field("syncStatus")).Text = (string)lang.GetMethod("T", All).Invoke(null, new object[] { "Windows paper sizes in sync" });
        Pump(300);
    }

    static void Place(Form f)
    {
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(target.WorkingArea.Left + 80, target.WorkingArea.Top + 60);
    }

    static void SelectItem(ListView list, int i)
    {
        list.SelectedIndices.Clear();
        list.Items[i].Selected = true; list.Items[i].Focused = true;
        Pump(800);
    }

    static int Dpi(Screen s)
    {
        uint x, y;
        var m = MonitorFromPoint(new Point(s.Bounds.Left + 10, s.Bounds.Top + 10), 2);
        return GetDpiForMonitor(m, 0, out x, out y) == 0 ? (int)x : 96;
    }

    // Ready, 62 mm continuous loaded (a QL-800's status reply), as the test printer's check.
    const string Ready62 = "80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00";
    static object FixedCheck(string name)
    {
        var device = Activator.CreateInstance(asm.GetType("QlLabels.DeviceResult"));
        Action<string, object> set = (n, v) => device.GetType().GetField(n, All).SetValue(device, v);
        set("Printer", name);
        set("Port", "USB001");
        set("State", Enum.Parse(asm.GetType("QlLabels.DeviceState"), "Open"));
        set("Reply", asm.GetType("QlLabels.QlStatus").GetMethod("FromHex", All).Invoke(null, new object[] { Ready62, DateTime.Now }));
        var jobs = Activator.CreateInstance(typeof(List<>).MakeGenericType(asm.GetType("QlLabels.JobInfo")));
        return asm.GetType("QlLabels.PrinterCheck").GetMethod("From", All).Invoke(null, new object[] { name, device, jobs, null });
    }

    static object Field(string n) { return form.GetType().GetField(n, All).GetValue(form); }

    // The colour of a greyed format (a format the loaded roll or the model can't print)
    static Color Dim()
    {
        var role = Enum.Parse(asm.GetType("QlLabels.Role"), "Dim");
        return (Color)asm.GetType("QlLabels.Theme").GetMethod("Of", All).Invoke(null, new[] { role });
    }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
    }

    static void Shot(Control c, string n) { if (c == form) StatusBar(); ShotOf(c, n); }

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
