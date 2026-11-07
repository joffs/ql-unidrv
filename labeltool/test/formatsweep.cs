// Theme sweep of QL Label Formats and QL Printer Status: every window and dialog in one scheme and language, then
// live switches through the other schemes and back. Covers what themeshots.cs doesn't: the tip bar's "Use Printer
// Offline" state, Printer setup's dialogs (question, result with actions, warning, error with details, progress, the
// language question), status cards (error with advice open, offline with "Use printer online", OK) and the tray
// menu. No message boxes: they play their sounds even on deskrun's desktop. Each shot gets a line in report.tsv:
// logical height (finding above 690 px), solid black and solid white 8x8 blocks (black controls; light parts left
// in dark; the window's transparent edge counts as black too). Nothing is saved, printed, elevated or installed; the scheme and language are forced
// in-process. Runs only on deskrun's own desktop. Run by formats-sweep.ps1:
//   formatsweep.exe <QlLabelFormats.exe> <out folder> <de|en> <light|dark|highcontrast> <samples .json>
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class FormatSweep
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
    [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern int GetDpiForWindow(IntPtr h);
    struct RECT { public int L, T, R, B; }

    static Assembly asm;
    static string outDir, prefix, samples, scheme;
    static StringBuilder report = new StringBuilder();
    const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    static int Main(string[] a)
    {
        var name = new StringBuilder(256); int need;
        GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
        if (name.ToString() == "Default") { Console.WriteLine("refused: normal desktop"); return 2; }
        asm = Assembly.LoadFrom(a[0]);
        outDir = a[1];
        string lang = a[2];
        scheme = a[3];
        samples = a[4];
        prefix = scheme + "-" + lang + "-";
        System.IO.Directory.CreateDirectory(outDir);
        T("Lang").GetMethod("Force", All).Invoke(null, new object[] { lang });
        ForceScheme(scheme);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        T("Theme").GetMethod("Start", All).Invoke(null, null);   // the menus' renderer, as the programs start
        int failures = 0;
        try { Work(); }
        catch (Exception e) { Console.WriteLine("FAILED: " + e); failures++; }
        System.IO.File.AppendAllText(System.IO.Path.Combine(outDir, "report.tsv"), report.ToString());
        return failures;
    }

    static Type T(string n) { return asm.GetType("QlLabels." + n); }

    static void ForceScheme(string s)
    {
        T("Theme").GetMethod("Force", All).Invoke(null, new[] { Enum.Parse(T("Scheme"), s, true) });
    }

    static void Switch(string s)
    {
        ForceScheme(s);
        T("Theme").GetMethod("Refresh", All).Invoke(null, null);
        Pump(1500);
    }

    static string L(string text) { return (string)T("Lang").GetMethod("T", All).Invoke(null, new object[] { text }); }
    static string F(string text, params object[] args) { return (string)T("Lang").GetMethod("F", All).Invoke(null, new object[] { text, args }); }

    static void Work()
    {
        var main = (Form)Activator.CreateInstance(T("MainForm"));
        Place(main, 20);
        main.Show();
        Pump(4000);
        Samples(main);
        Shot(main, "formats");
        Offline(main);
        Shot(main, "formats-offline");

        // Printer setup and its dialogs
        var setup = (Form)Activator.CreateInstance(T("SetupForm"));
        Place(setup, 60); setup.Show(main); Pump(4000); Shot(setup, "setup");
        foreach (var d in SetupDialogs()) { Place(d.Value, 120); d.Value.Show(setup); Pump(1200); Shot(d.Value, d.Key); d.Value.Close(); d.Value.Dispose(); Pump(200); }

        var status = (Form)Activator.CreateInstance(T("StatusForm"), new object[] { new List<string> { "Brother QL-800 (Unidrv)", "Brother QL-570 (Unidrv)" } });
        Place(status, 80); status.Show(main); Pump(4000);
        Cards(status);
        Shot(status, "status-cards");

        var aboutCtor = T("AboutForm").GetConstructors(All)[0];
        var about = (Form)aboutCtor.Invoke(new object[] { "QL Printer Status" });
        Place(about, 100); about.Show(main); Pump(1500); Shot(about, "about");

        TrayMenu();

        // Live: through every other scheme and back; the windows that stay open are shot at each step.
        var question = SetupDialogs().First(d => d.Key == "setup-error").Value;
        Place(question, 140); question.Show(setup); Pump(1000);
        var order = new List<string> { "light", "dark", "highcontrast" };
        order.Remove(scheme);
        order.Add(scheme);
        int step = 0;
        foreach (var s in order)
        {
            Switch(s);
            step++;
            string tag = "live" + step + "-" + s + "-";
            Shot(main, tag + "formats");
            Shot(setup, tag + "setup");
            Shot(question, tag + "setup-error");
            Shot(status, tag + "status");
            Shot(about, tag + "about");
        }
        TrayMenu("-back");
        question.Close(); about.Close(); status.Close(); setup.Close();
        main.GetType().GetField("dirty", All).SetValue(main, false);
        main.Close();
        Pump(500);
    }

    // The tip bar as UpdateTipBar shows it while a QL printer is set to "Use Printer Offline".
    static void Offline(Form main)
    {
        var printers = new List<string> { "Brother QL-800 (Unidrv)" };
        main.GetType().GetField("offlinePrinters", All).SetValue(main, printers);
        ((Label)Field(main, "offlineText")).Text = F("{0} is set to 'Use Printer Offline': print jobs wait on this PC and nothing reaches the printer.", printers[0]);
        foreach (Control c in (IList)Field(main, "monitorTip")) c.Visible = false;
        foreach (Control c in (IList)Field(main, "offlineTip")) c.Visible = true;
        ((Control)Field(main, "tipBar")).Visible = true;
        main.GetType().GetMethod("FitTipBar", All).Invoke(main, null);
        Pump(150);   // shot before the window's own check hides the bar again
    }

    static List<KeyValuePair<string, Form>> SetupDialogs()
    {
        var kind = T("ResultKind");
        Func<string, object> k = n => Enum.Parse(kind, n);
        Func<string[], object> fields = p =>
        {
            var list = new List<KeyValuePair<string, string>>();
            for (int i = 0; i + 1 < p.Length; i += 2) list.Add(new KeyValuePair<string, string>(p[i], p[i + 1]));
            return list;
        };
        Func<object, string, object, string, string, string, Form> make = (kd, head, f, note, details, close) =>
            (Form)Activator.CreateInstance(T("SetupResult"), new object[] { "Printer setup", kd, head, f, note, details, close });
        var r = new List<KeyValuePair<string, Form>>();

        var q = make(k("Question"), F("Found {0} at {1}", "QL-820NWB", "192.0.2.50"),
                     fields(new[] { L("Model"), "QL-820NWB " + L("(the printer told)"), L("Loaded roll"), "62 mm", L("Address"), F("{0}, print port 9100", "192.0.2.50") }),
                     L("Windows then asks for administrator permission."), null, L("Cancel"));
        q.GetType().GetMethod("AddChoice", All).Invoke(q, new object[] { L("Add printer"), DialogResult.OK, true });
        r.Add(new KeyValuePair<string, Form>("setup-question", q));

        var ok = make(k("Ok"), L("Printer added"),
                      fields(new[] { L("Name"), "Brother QL-820NWB (Unidrv)", L("Model"), "QL-820NWB", L("Address"), "192.0.2.50 " + L("(print port 9100)") }),
                      L("Programs can print to it now. The label formats are the same on every QL printer."), null, null);
        var add = ok.GetType().GetMethod("AddAction", All);
        add.Invoke(ok, new object[] { L("Print test label"), (Func<string>)(() => null), true, null });
        add.Invoke(ok, new object[] { L("Open print queue"), (Func<string>)(() => null), false, null });
        add.Invoke(ok, new object[] { L("Set as default printer"), (Func<string>)(() => null), false, null });
        r.Add(new KeyValuePair<string, Form>("setup-added", ok));

        r.Add(new KeyValuePair<string, Form>("setup-warning", make(k("Warning"), L("The printer doesn't answer"), fields(new[] { L("Address"), "192.0.2.50" }),
            "Timeout\n\n" + L("If the address is right, choose the model yourself and click Add again."), null, null)));

        var err = make(k("Error"), "Install failed", fields(new[] { L("Name"), "Brother QL-800 (Unidrv)" }), "install.ps1 ended with exit code 1.",
                       "line 1\r\nline 2: Access is denied.\r\nline 3", null);
        foreach (var box in All2(err).OfType<TextBox>()) box.Visible = true;   // "Show details" open
        r.Add(new KeyValuePair<string, Form>("setup-error", err));

        var lang = (Form)T("LanguageSwitch").GetMethod("Question", All).Invoke(null, new object[] { "QL Label Formats", "en", null, (bool?)true });
        r.Add(new KeyValuePair<string, Form>("setup-language", lang));

        var progress = (Form)T("SetupProgress").GetConstructors(All)[0].Invoke(new object[] { "Printer setup", L("Adding the printer..."), L("Which model is it, and which roll is loaded (SNMP)?") });
        r.Add(new KeyValuePair<string, Form>("setup-progress", progress));
        return r;
    }

    static IEnumerable<Control> All2(Control c)
    {
        foreach (Control x in c.Controls) { yield return x; foreach (var y in All2(x)) yield return y; }
    }

    // Card 1: wrong media, advice open, "Use Printer Offline" (the online link); card 2: ready.
    static void Cards(Form status)
    {
        var cards = (IList)Field(status, "cards");
        var pc = T("PrinterCheck");
        var health = T("Health");
        Func<string, object, string, string, string[], bool, object> check = (printer, h, head, cause, advice, offline) =>
        {
            var c = Activator.CreateInstance(pc, true);
            pc.GetField("Printer").SetValue(c, printer);
            pc.GetField("Health").SetValue(c, h);
            pc.GetField("Headline").SetValue(c, head);
            pc.GetField("Cause").SetValue(c, cause);
            pc.GetField("Detail").SetValue(c, cause == null ? null : "The loaded roll doesn't fit the print job.");
            pc.GetField("WorkOffline").SetValue(c, offline);
            var list = (List<string>)pc.GetField("Advice").GetValue(c);
            list.AddRange(advice);
            return c;
        };
        var bad = check("Brother QL-800 (Unidrv)", Enum.Parse(health, "Error"), "Wrong media", "the roll doesn't fit the job",
                        new[] { "Load the roll the job needs.", "Open and close the roll cover." }, true);
        var good = check("Brother QL-570 (Unidrv)", Enum.Parse(health, "Ok"), "Ready", null, new string[0], false);
        var display = cards[0].GetType().GetMethod("Display", All);
        display.Invoke(cards[0], new[] { bad });
        cards[0].GetType().GetField("openFor", All).SetValue(cards[0], pc.GetProperty("Line").GetValue(bad));
        display.Invoke(cards[0], new[] { bad });
        if (cards.Count > 1) display.Invoke(cards[1], new[] { good });
        Pump(150);   // shot before the window's next poll shows the real printers again
    }

    // The tray monitor's menu (its items as TrayMonitor builds them; the renderer is the programs' own).
    static void TrayMenu(string tag = "")
    {
        var menu = new ContextMenuStrip();
        var status = menu.Items.Add(L("Printer status"));
        status.Font = new Font(status.Font, FontStyle.Bold);
        menu.Items.Add("QL Label Formats");
        menu.Items.Add("QL Label Maker");
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(L("Turn off printer notifications"));
        menu.Items.Add(L("About QL Unidrv"));
        menu.Items.Add(L("Exit"));
        menu.Show(new Point(600, 400));
        Pump(600);
        Shot(menu, "tray-menu" + tag);
        menu.Close();
        menu.Dispose();
        Pump(200);
    }

    static void Samples(Form main)
    {
        var store = T("LabelStore");
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

    static void Shot(Control c, string n) { ShotHandle(c.Handle, n); }

    // The picture, and a report line: logical height and the solid black / white 8x8 blocks.
    static void ShotHandle(IntPtr h, string n)
    {
        RECT r; GetWindowRect(h, out r);
        int w = Math.Max(1, r.R - r.L), ht = Math.Max(1, r.B - r.T);
        int dpi = GetDpiForWindow(h);
        if (dpi <= 0) dpi = 96;
        using (var bmp = new Bitmap(w, ht, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bmp))
            {
                var dc = g.GetHdc();
                PrintWindow(h, dc, 2);
                g.ReleaseHdc(dc);
            }
            bmp.Save(System.IO.Path.Combine(outDir, prefix + n + ".png"), ImageFormat.Png);
            int black, white;
            Blocks(bmp, out black, out white);
            int logical = ht * 96 / dpi;
            report.Append(prefix + n).Append('\t').Append(logical).Append('\t').Append(black).Append('\t').Append(white)
                  .Append('\t').Append(logical > 690 ? "TALL" : "").Append("\r\n");
            Console.WriteLine("shot " + prefix + n + " " + w + "x" + ht + " logical height " + logical + " black " + black + " white " + white);
        }
    }

    static void Blocks(Bitmap bmp, out int black, out int white)
    {
        black = white = 0;
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[data.Stride / 4 * bmp.Height];
        Marshal.Copy(data.Scan0, px, 0, px.Length);
        bmp.UnlockBits(data);
        int stride = data.Stride / 4;
        for (int by = 0; by + 8 <= bmp.Height; by += 8)
            for (int bx = 0; bx + 8 <= bmp.Width; bx += 8)
            {
                bool allBlack = true, allWhite = true;
                for (int y = by; y < by + 8 && (allBlack || allWhite); y++)
                    for (int x = bx; x < bx + 8; x++)
                    {
                        int p = px[y * stride + x];
                        int rr = (p >> 16) & 255, gg = (p >> 8) & 255, bb = p & 255;
                        if (rr > 12 || gg > 12 || bb > 12) allBlack = false;
                        if (rr < 243 || gg < 243 || bb < 243) allWhite = false;
                    }
                if (allBlack) black++;
                if (allWhite) white++;
            }
    }
}
