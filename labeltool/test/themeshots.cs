// Screenshots of every program window in light and dark, for checking the colours (Theme.cs): QL Label Formats with
// the help's sample formats, its Extras menu with Appearance open, About, the status window, Printer setup, the setup
// window (Setup.exe, nothing is installed) and QL Label Maker with a label. Then it switches the scheme live (as the
// Appearance menu does) and takes the main windows again ("-live"). The programs run in this process (their window
// classes through reflection); the scheme and language are forced in-process, nothing is written to the registry and
// nothing is saved or printed. Runs only on deskrun's own desktop (tools\uitest): it never takes the user's mouse or
// focus. Screenshots with PrintWindow. Run by theme-shots.ps1:
//   themeshots.exe <QlLabelFormats.exe> <out folder> <de|en> <light|dark> <samples .json> <.qllabel>
// -> <out folder>\<scheme>-<lang>-<shot>.png
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

static class ThemeShots
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, System.Text.StringBuilder b, int n, out int need);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    struct RECT { public int L, T, R, B; }

    static Assembly asm;

    // Two "Add printer" lines in Printer setup: a QL-800 on USB001, a QL-570 on USB002 with Brother's own printer
    static void FakeUsb(Form setup)
    {
        var cand = asm.GetType("QlLabels.UsbCandidate");
        var models = (Array)asm.GetType("QlLabels.MediaTable").GetField("Models", All).GetValue(null);
        Func<string, object> model = n => models.Cast<object>().First(m => (string)m.GetType().GetField("Name").GetValue(m) == n);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(cand));
        foreach (var c in new[] { new[] { "QL-800", "USB001", null }, new[] { "QL-570", "USB002", "Brother QL-570" } })
        {
            var o = Activator.CreateInstance(cand);
            cand.GetField("Model").SetValue(o, model(c[0]));
            cand.GetField("Port").SetValue(o, c[1]);
            cand.GetField("BrotherQueue").SetValue(o, c[2]);
            list.Add(o);
        }
        setup.GetType().GetMethod("ShowUsbRows", All).Invoke(setup, new object[] { list });
        setup.GetType().GetMethod("FitHeight", All).Invoke(setup, null);
    }
    static string outDir, prefix, samples, labelFile;
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new System.Text.StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop"); return 2; }
        asm = Assembly.LoadFrom(a[0]);
        outDir = a[1];
        string lang = a[2], scheme = a[3];
        samples = a[4];
        labelFile = a[5];
        prefix = scheme + "-" + lang + "-";
        System.IO.Directory.CreateDirectory(outDir);
        asm.GetType("QlLabels.Lang").GetMethod("Force", All).Invoke(null, new object[] { lang });
        ForceScheme(scheme);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // as under Application.Run: awaits in the windows continue on this thread (without it on the thread pool,
        // where a window closed meanwhile crashed QL Label Maker's roll check)
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        int failures = 0;
        try { Work(scheme); }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); failures++; }
        return failures;
    }

    static void ForceScheme(string scheme)
    {
        var type = asm.GetType("QlLabels.Scheme");
        asm.GetType("QlLabels.Theme").GetMethod("Force", All).Invoke(null, new[] { Enum.Parse(type, scheme, true) });
    }

    static void Work(string scheme)
    {
        // QL Label Formats with the sample formats
        var main = (Form)Activator.CreateInstance(asm.GetType("QlLabels.MainForm"));
        Place(main, 20);
        main.Show();
        Pump(4000);
        Samples(main);
        var list = (ListView)Field(main, "list");
        if (list.Items.Count > 0) { list.Items[0].Selected = true; list.Items[0].Focused = true; }
        Pump(800);
        Shot(main, "formats");
        var menu = (MenuStrip)Field(main, "menu");
        var extras = (ToolStripMenuItem)menu.Items[menu.Items.Count - 2];
        extras.ShowDropDown(); Pump(400);
        var appearance = extras.DropDownItems.OfType<ToolStripMenuItem>().Last();
        appearance.ShowDropDown(); Pump(500);
        Shot(extras.DropDown, "menu-extras");
        Shot(appearance.DropDown, "menu-appearance");
        appearance.HideDropDown(); extras.HideDropDown(); Pump(300);

        var about = (Form)asm.GetType("QlLabels.AboutForm").GetConstructors(All)[0].Invoke(Args(asm.GetType("QlLabels.AboutForm").GetConstructors(All)[0], "QL Label Formats"));
        Place(about, 60); about.Show(main); Pump(1500); Shot(about, "about"); about.Close(); Pump(300);

        var status = (Form)Activator.CreateInstance(asm.GetType("QlLabels.StatusForm"), new object[] { new List<string> { "Brother QL-800 (Unidrv)" } });
        Place(status, 60); status.Show(main); Pump(5000); Shot(status, "status"); status.Close(); Pump(300);

        var setup = (Form)Activator.CreateInstance(asm.GetType("QlLabels.SetupForm"));
        Place(setup, 60); setup.Show(main); Pump(4000); Shot(setup, "setup");
        // with the lines for connected USB printers without a printer of ours (plain, and Brother's printer beside it)
        FakeUsb(setup); Pump(800); Shot(setup, "setup-usb"); setup.Close(); Pump(300);

        // the setup window (Setup.exe) on its first page: nothing runs until a button is clicked
        var install = (Form)Activator.CreateInstance(asm.GetType("QlLabels.InstallForm"), new object[] { System.IO.Path.GetDirectoryName(asm.Location), false });
        Place(install, 60); install.Show(main); Pump(5000); Shot(install, "install"); install.Close(); Pump(300);

        // QL Label Maker with a label
        var editor = (Form)Activator.CreateInstance(asm.GetType("QlLabels.EditorForm"), new object[] { labelFile, "Brother QL-800 (Unidrv)", null });
        Place(editor, 40);
        editor.Size = new Size(1100, 680);
        editor.Show();
        Pump(5000);
        Shot(editor, "maker");
        MakerStates(editor);

        // the other scheme, live (as the Appearance menu switches it)
        ForceScheme(scheme == "dark" ? "light" : "dark");
        asm.GetType("QlLabels.Theme").GetMethod("Refresh", All).Invoke(null, null);
        Pump(1500);
        Shot(main, "formats-live");
        Select(editor, 0);
        Shot(editor, "maker-live");

        // High Contrast and back, as Windows switches it (QL Printer Status kept black buttons after that in 0.18.18):
        // the status window and Printer setup through High Contrast, light, dark, light; each step is shot, every
        // window must come back as the plain one.
        var status2 = (Form)Activator.CreateInstance(asm.GetType("QlLabels.StatusForm"), new object[] { new List<string> { "Brother QL-800 (Unidrv)" } });
        Place(status2, 80); status2.Show(main);
        var setup2 = (Form)Activator.CreateInstance(asm.GetType("QlLabels.SetupForm"));
        Place(setup2, 100); setup2.Show(main);
        Pump(4000);
        FakeUsb(setup2);
        Pump(800);
        int step = 0;
        foreach (var s in new[] { "highcontrast", "light", "dark", "light" })
        {
            ForceScheme(s);
            asm.GetType("QlLabels.Theme").GetMethod("Refresh", All).Invoke(null, null);
            Pump(1500);
            step++;
            Shot(status2, "switch" + step + "-" + s + "-status");
            Shot(setup2, "switch" + step + "-" + s + "-setup");
            Shot(main, "switch" + step + "-" + s + "-formats");
        }
        setup2.Close(); status2.Close(); Pump(300);

        Clean(editor);
        editor.Close();
        main.GetType().GetField("dirty", All).SetValue(main, false);
        main.Close();
        Pump(500);
    }

    // QL Label Maker's states: each kind of object selected (its panel, handles), all selected, a label with a part beyond
    // its end (the outside bar) and a code that can't be printed (the problems dialog).
    static void MakerStates(Form editor)
    {
        var objects = (IList)Field(Field(editor, "doc"), "Objects");
        var names = new[] { "text", "frame", "line", "qr", "code128", "picture" };
        for (int i = 0; i < objects.Count && i < names.Length; i++) { Select(editor, i); Shot(editor, "maker-" + names[i]); }
        SelectObjects(editor, objects);
        Shot(editor, "maker-all");
        Select(editor, -1);

        // the picture beyond the label's end, the Code 128 as an EAN-13 with too few digits
        string text = System.IO.File.ReadAllText(labelFile)
            .Replace("\"x\": 66, \"y\": 3", "\"x\": 140, \"y\": 3").Replace("\"symbology\": \"code128\"", "\"symbology\": \"ean13\"").Replace("\"R3-M4-20\"", "\"123\"");
        string changed = System.IO.Path.Combine(outDir, "themeshots-states.qllabel");
        System.IO.File.WriteAllText(changed, text);
        var other = (Form)Activator.CreateInstance(asm.GetType("QlLabels.EditorForm"), new object[] { changed, "Brother QL-800 (Unidrv)", null });
        Place(other, 60);
        other.Size = new Size(1100, 680);
        other.Show();
        Pump(4000);
        Shot(other, "maker-outside");
        var problems = (Form)other.GetType().GetMethod("ProblemsDialog", All).Invoke(other, null);
        if (problems != null)
        {
            Place(problems, 120);
            problems.Show(other); Pump(1500); Shot(problems, "maker-problems"); problems.Close(); Pump(300);
        }
        Clean(other);
        other.Close();
        Pump(500);
        System.IO.File.Delete(changed);
    }

    // Selects the label's object number i (-1: nothing).
    static void Select(Form editor, int i)
    {
        var objects = (IList)Field(Field(editor, "doc"), "Objects");
        var list = (IList)Activator.CreateInstance(objects.GetType());
        if (i >= 0) list.Add(objects[i]);
        SelectObjects(editor, list);
    }

    static void SelectObjects(Form editor, IList objects)
    {
        var canvas = Field(editor, "canvas");
        var type = typeof(IEnumerable<>).MakeGenericType(objects.GetType().GetGenericArguments()[0]);
        canvas.GetType().GetMethod("Select", new[] { type }).Invoke(canvas, new object[] { objects });
        Pump(600);
    }

    // Defaults for a constructor's parameters: strings from the given texts in order, then null; bools false.
    static object[] Args(ConstructorInfo c, params string[] texts)
    {
        int t = 0;
        return c.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object)(t < texts.Length ? texts[t++] : null)
                                           : p.ParameterType == typeof(bool) ? (object)false
                                           : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
    }

    // The editor closes without asking to save.
    static void Clean(Form editor)
    {
        var f = editor.GetType().GetField("dirty", All);
        if (f != null && f.FieldType == typeof(bool)) f.SetValue(editor, false);
    }

    // The sample formats in place of this PC's (in memory only), shown as saved and applied (as formatshots.cs)
    static void Samples(Form main)
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
        main.GetType().GetField("formats", All).SetValue(main, formats);
        main.GetType().GetField("saved", All).SetValue(main, saved);
        main.GetType().GetMethod("FillList", All).Invoke(main, new[] { formats[0] });
        main.GetType().GetMethod("SetDirty", All).Invoke(main, new object[] { false });
        Pump(800);
    }

    static void Place(Form f, int offset)
    {
        var area = Screen.PrimaryScreen.WorkingArea;
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(area.Left + offset, area.Top + offset);
    }

    static object Field(object o, string n) { return o.GetType().GetField(n, All).GetValue(o); }

    static void Pump(int ms)
    {
        var end = DateTime.Now.AddMilliseconds(ms);
        while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
    }

    static void Shot(Control c, string n)
    {
        RECT r; GetWindowRect(c.Handle, out r);
        using (var bmp = new Bitmap(Math.Max(1, r.R - r.L), Math.Max(1, r.B - r.T)))
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
