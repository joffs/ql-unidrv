// UI sweep of QL Label Maker (and the label tool's windows), on deskrun's desktop only (never the user's):
//   ui_sweep clip  <de|en> <out>   every panel state of the editor and the other windows at 96/120/144/168 dpi
//                                   (WM_DPICHANGED, as a move to such a screen): texts cut off (preferred size larger
//                                   than the control, or the control cut by its container), toolbar buttons pushed
//                                   into the overflow, windows taller than 690 px (logical) -> FIND lines + PNGs;
//                                   also the status card of a printer on another PC's share in each state (shared,
//                                   not shared with its advice and link, not reachable, can't be checked, stale) and
//                                   link texts cut inside their LinkLabel. QL_SWEEP_ONLY=status: only those cards
//   ui_sweep drag  <de|en> <out>   every object type moved, resized and turned in steps; the canvas as Windows would
//                                   show it (only invalidated regions painted again) vs. a fresh EditorRender.Render:
//                                   differing dots = paint left behind (trails) -> FIND lines + PNGs
//   ui_sweep sizes <de|en> <out>   every barcode type at widths 8-60 mm: rendered label PNGs + sizes.tsv (problem and warning
//                                   lines, dots of the text under the bars) for ui_sweep.py to decode
// Lines "FIND <check> <where>: <what>" are findings; "INFO" lines are notes. Exit 0 also with findings (the
// report counts them); 2 = normal desktop, 3 = timeout.
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
    static class UiSweep
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr GetThreadDesktop(int id);
        [DllImport("kernel32.dll")] static extern int GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetUserObjectInformation(IntPtr h, int i, StringBuilder b, int n, out int need);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, ref RECT r);
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly int[] Dpis = { 96, 120, 144, 168 };
        static string outDir, lang;
        static int findings;
        static EditorForm form;
        static EditorCanvas canvas;

        [STAThread]
        static int Main(string[] args)
        {
            // UTF-8 to ui_sweep.ps1 (which reads it as such): the console's code page garbled · and × in the findings
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
            var name = new StringBuilder(256); int need;
            GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), 2, name, 256, out need);
            if (name.ToString() == "Default") { Console.WriteLine("refused: run through deskrun.exe"); return 2; }
            string mode = args[0];
            lang = args[1];
            outDir = Path.Combine(args[2], mode + "-" + lang);
            Directory.CreateDirectory(outDir);
            Lang.Force(lang);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            var watchdog = new System.Threading.Timer(_ => { Console.WriteLine("timeout"); Environment.Exit(3); }, null, 900000, Timeout.Infinite);
            string file = Path.Combine(outDir, "sweep.qllabel");
            File.WriteAllText(file, SweepLabel(), new UTF8Encoding(false));
            if (mode == "clip") Clip(file);
            else if (mode == "drag") WithEditor(file, Drag);
            else if (mode == "sizes") WithEditor(file, Sizes);
            watchdog.Dispose();
            Console.WriteLine("DONE " + mode + " " + lang + ": " + findings + " finding(s)");
            return 0;
        }

        static void Find(string check, string where, string what)
        {
            findings++;
            Console.WriteLine("FIND " + check + " " + where + ": " + what);
        }

        // ---------- the sweep label: every object type, neutral contents ----------

        static string SweepLabel()
        {
            string png;
            using (var bmp = new Bitmap(120, 80))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 120, 80), Color.Black, Color.White, 30f)) g.FillEllipse(b, 10, 10, 100, 60);
                }
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); png = Convert.ToBase64String(ms.ToArray()); }
            }
            var o = new List<string>
            {
                "{\"type\":\"text\",\"kind\":\"line\",\"x\":3,\"y\":3,\"w\":30,\"h\":8,\"text\":\"Regal 3\",\"size\":20,\"bold\":true}",
                "{\"type\":\"text\",\"kind\":\"frame\",\"x\":3,\"y\":13,\"w\":34,\"h\":12,\"text\":\"Schrauben M4 x 20\\nverzinkt\",\"size\":9}",
                "{\"type\":\"line\",\"x\":3,\"y\":27,\"w\":30,\"h\":0,\"thickness\":0.4}",
                "{\"type\":\"line\",\"x\":36,\"y\":3,\"w\":0,\"h\":20,\"thickness\":0.4}",
                "{\"type\":\"line\",\"x\":3,\"y\":30,\"w\":20,\"h\":12,\"thickness\":0.6,\"rising\":true}",
                "{\"type\":\"box\",\"x\":26,\"y\":30,\"w\":14,\"h\":10,\"thickness\":0.5}",
                "{\"type\":\"box\",\"x\":42,\"y\":30,\"w\":14,\"h\":10,\"thickness\":0.5,\"ellipse\":true,\"filled\":true}",
                "{\"type\":\"image\",\"x\":40,\"y\":3,\"w\":18,\"h\":12,\"png\":\"" + png + "\",\"mode\":\"dither\",\"threshold\":128}",
                "{\"type\":\"barcode\",\"symbology\":\"ean13\",\"x\":60,\"y\":3,\"w\":30,\"h\":18,\"data\":\"2000000000008\"}",
                "{\"type\":\"barcode\",\"symbology\":\"code128\",\"x\":60,\"y\":24,\"w\":30,\"h\":14,\"data\":\"ABC-123\"}",
                "{\"type\":\"barcode\",\"symbology\":\"gs1-128\",\"x\":92,\"y\":3,\"w\":56,\"h\":16,\"data\":\"(01)02000000000008(10)ABC123\"}",
                "{\"type\":\"barcode\",\"symbology\":\"qr\",\"x\":92,\"y\":22,\"w\":16,\"h\":16,\"data\":\"QR TEST\"}",
                TypedQr(QrType.Url, 110, 22, "url", "example.org"),
                TypedQr(QrType.Contact, 128, 22, "firstName", "Erika", "lastName", "Muster"),
                TypedQr(QrType.Wifi, 150, 3, "ssid", "Test", "password", "secret", "security", QrContent.Security[0]),
                TypedQr(QrType.Email, 150, 22, "to", "test@example.org"),
                TypedQr(QrType.Phone, 168, 3, "number", "+49 30 1234567"),
                TypedQr(QrType.Sms, 168, 22, "number", "+49 30 1234567", "message", "Hallo"),
            };
            return "{\"format\":\"QL label\",\"version\":1,\"label\":{\"form\":\"62mm\",\"paperId\":259,\"view\":\"along\",\"length\":\"own\",\"length01\":1900},\"objects\":[" +
                   string.Join(",", o) + "]}";
        }

        // A QR code of a type with fields, its content built as the editor builds it (QrContent.Build).
        static string TypedQr(QrType type, double x, double y, params string[] kv)
        {
            var f = new Dictionary<string, string>();
            for (int i = 0; i + 1 < kv.Length; i += 2) f[kv[i]] = kv[i + 1];
            var js = new System.Web.Script.Serialization.JavaScriptSerializer();
            return "{\"type\":\"barcode\",\"symbology\":\"qr\",\"x\":" + x + ",\"y\":" + y + ",\"w\":16,\"h\":16,\"qrType\":\"" + QrContent.Names[(int)type] +
                   "\",\"qrFields\":" + js.Serialize(f) + ",\"data\":" + js.Serialize(QrContent.Build(type, f)) + "}";
        }

        static string Describe(LabelObject o)
        {
            var b = o as BarcodeObject;
            if (b != null) return b.Kind == Symbology.Qr ? "qr-" + QrContent.Names[(int)b.QrType] : b.Kind.ToString().ToLowerInvariant();
            var t = o as TextObject;
            if (t != null) return t.Kind == TextKind.Frame ? "text-frame" : "text-line";
            var l = o as LineObject;
            if (l != null) return l.H == 0 ? "line-level" : l.W == 0 ? "line-upright" : "line-diagonal";
            var x = o as BoxObject;
            if (x != null) return x.Ellipse ? "ellipse" : "box";
            return o.Type;
        }

        static void WithEditor(string file, Action work)
        {
            form = new EditorForm(file, "QL-800 Capture", null);
            form.StartPosition = FormStartPosition.Manual;
            form.Bounds = new Rectangle(30, 30, 1220, 680);
            bool done = false;
            form.Shown += delegate
            {
                Pump(2500);
                canvas = F<EditorCanvas>("canvas");
                try { work(); } catch (Exception e) { Find("harness", "editor", e.ToString()); }
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }
        }

        static T F<T>(string field) { return (T)typeof(EditorForm).GetField(field, All).GetValue(form); }

        // ---------- (a) clipped texts, (e) window height ----------

        static void Clip(string file)
        {
            if (Environment.GetEnvironmentVariable("QL_SWEEP_ONLY") == "status") { ShareCards(); return; }
            // The editor: every selection state at every DPI.
            form = new EditorForm(file, "QL-800 Capture", null);
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(30, 30);
            bool done = false;
            form.Shown += delegate
            {
                Pump(2500);
                canvas = F<EditorCanvas>("canvas");
                var start = form.Size;
                Console.WriteLine("INFO editor opens at " + start + ", minimum " + form.MinimumSize);
                foreach (int dpi in Dpis)
                {
                    if (dpi != form.DeviceDpi) ToDpi(form, dpi);
                    Height("editor (minimum size)", form.MinimumSize.Height, dpi);
                    var doc = F<LabelDoc>("doc");
                    var states = new List<KeyValuePair<string, LabelObject[]>> { new KeyValuePair<string, LabelObject[]>("nothing selected", new LabelObject[0]) };
                    foreach (var o in doc.Objects) states.Add(new KeyValuePair<string, LabelObject[]>(Describe(o), new[] { o }));
                    states.Add(new KeyValuePair<string, LabelObject[]>("text+box", new[] { doc.Objects[0], doc.Objects[5] }));
                    foreach (var s in states)
                    {
                        canvas.Select(s.Value);
                        Pump(400);
                        Inspect(form, "editor " + dpi + " dpi, " + s.Key);
                        if (Environment.GetEnvironmentVariable("QL_SWEEP_SHOTS") == "1") Shot(form, "all " + dpi + " " + s.Key, new List<Rectangle>());
                    }
                    canvas.Select(new LabelObject[0]);
                }
                typeof(EditorForm).GetField("dirty", All).SetValue(form, false);
                done = true;
                form.Close();
            };
            form.Show();
            while (!done || form.Visible) { Application.DoEvents(); Thread.Sleep(15); }

            // The other windows, at every DPI.
            var others = new List<KeyValuePair<string, Func<Form>>>
            {
                new KeyValuePair<string, Func<Form>>("about", () => (Form)Activator.CreateInstance(typeof(AboutForm), All, null, new object[] { "QL Label Maker" }, null)),
                new KeyValuePair<string, Func<Form>>("label formats", () => new MainForm()),
                new KeyValuePair<string, Func<Form>>("printer setup", () => new SetupForm()),
                new KeyValuePair<string, Func<Form>>("printer status", () => new StatusForm(new[] { "QL-800 Capture" })),
            };
            others.InsertRange(3, SetupCases());
            foreach (var kv in others)
            {
                Form f;
                try { f = kv.Value(); } catch (Exception e) { Console.WriteLine("INFO " + kv.Key + " not opened: " + e.GetBaseException().Message); continue; }
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(30, 30);
                f.Show();
                Pump(2500);
                foreach (int dpi in Dpis)
                {
                    if (dpi != f.DeviceDpi) ToDpi(f, dpi);
                    Height(kv.Key, f.Height, dpi);
                    Rows(f, kv.Key + " " + dpi + " dpi");
                    Inspect(f, kv.Key + " " + dpi + " dpi");
                }
                Console.WriteLine("INFO " + kv.Key + " checked at " + string.Join("/", Dpis) + " dpi (" + f.Width * 96 / f.DeviceDpi + " x " + f.Height * 96 / f.DeviceDpi + " logical)");
                f.Close();
                f.Dispose();
                Pump(300);
            }
            ShareCards();
        }

        // The status card of a printer on another PC's share (port \\PRINTHOST\QL-800, a simulated host) in each state,
        // its advice open (the "Remove the printer here" link when not shared), at every DPI. The printer name doesn't
        // exist on this PC: the window gets its card, the check comes from PublishedStatus with fake readers.
        static void ShareCards()
        {
            const string Printer = "Brother QL-800 (Unidrv) sweep", Port = @"\\PRINTHOST\QL-800";
            var now = new DateTime(2026, 11, 6, 10, 0, 30, DateTimeKind.Utc);
            Func<DateTime, string> published = poll => "QLUnidrv status 1\r\npoll\t" + poll.ToString("yyyy-MM-ddTHH:mm:ssZ") + "\tOpen\treply,request\t\r\n" +
                poll.AddSeconds(1).ToString("yyyy-MM-ddTHH:mm:ssZ") + "\t80 20 42 34 38 30 00 00 00 00 3E 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00\treply\r\n";
            var saved = new object[] { PublishedStatus.NetShare, PublishedStatus.Queue, PublishedStatus.ReadValue, PublishedStatus.UtcNow, PublishedStatus.HostAnswers };
            var states = new[] { "shared", "not shared", "not reachable", "can't be checked", "stale", "offline" };
            try
            {
                foreach (var state in states)
                {
                    PublishedStatus.ForgetShareChecks();
                    PublishedStatus.UtcNow = () => now;
                    PublishedStatus.HostAnswers = h => state != "not reachable";
                    PublishedStatus.NetShare = (h, n) => state == "not shared" ? new[] { 2310, 0 } : state == "not reachable" ? new[] { 53, 0 } : state == "can't be checked" ? new[] { 5, 0 } : new[] { 0, 1 };
                    PublishedStatus.Queue = q => state == "not shared" ? new PublishedStatus.QueueFacts { Error = 1801 } : new PublishedStatus.QueueFacts { Attributes = 0x48, ShareName = "QL-800" };
                    PublishedStatus.ReadValue = q => published(state == "stale" ? now.AddMinutes(-10) : now.AddSeconds(-3));
                    var check = PrinterCheck.From(Printer, PublishedStatus.QueryShare(Printer, Port, "PRINTHOST", "QL-800"), new List<JobInfo>(), null);
                    if (state == "offline") check.SetWorkOffline();   // this PC's queue set to "Use Printer Offline", the host answering
                    var f = new StatusForm(new[] { Printer }, () => new[] { Printer });
                    f.StartPosition = FormStartPosition.Manual;
                    f.Location = new Point(30, 30);
                    f.Show();
                    Pump(2500);
                    ((System.Windows.Forms.Timer)typeof(StatusForm).GetField("timer", All).GetValue(f)).Stop();
                    Pump(1500);   // a refresh still running
                    var card = ((System.Collections.IList)typeof(StatusForm).GetField("cards", All).GetValue(f))[0];
                    foreach (int dpi in Dpis)
                    {
                        if (dpi != f.DeviceDpi) ToDpi(f, dpi);
                        card.GetType().GetField("openFor", All).SetValue(card, check.Line);
                        card.GetType().GetMethod("Display", All).Invoke(card, new object[] { check });
                        Pump(600);
                        Height("printer status, share " + state, f.Height, dpi);
                        Inspect(f, "printer-status-share " + state + " " + dpi + " dpi");
                        if (Environment.GetEnvironmentVariable("QL_SWEEP_SHOTS") == "1") Shot(f, "status share " + state + " " + dpi, new List<Rectangle>());
                    }
                    Console.WriteLine("INFO printer status, share " + state + " (" + check.Line + ") checked at " + string.Join("/", Dpis) + " dpi");
                    f.Close();
                    f.Dispose();
                    Pump(300);
                }
            }
            finally
            {
                PublishedStatus.NetShare = (Func<string, string, int[]>)saved[0];
                PublishedStatus.Queue = (Func<string, PublishedStatus.QueueFacts>)saved[1];
                PublishedStatus.ReadValue = (Func<string, string>)saved[2];
                PublishedStatus.UtcNow = (Func<DateTime>)saved[3];
                PublishedStatus.HostAnswers = (Func<string, bool>)saved[4];
                PublishedStatus.ForgetShareChecks();
            }
        }

        // The tallest usual Printer setup: 8 printers in the list (its most rows), the "Add printer" line for 3 connected
        // USB printers (one with Brother's printer beside it), the last change with a printer still pending deletion, on
        // each tab, and the "Share a printer" tab in each state (a-f of the mockup): the 690 px rule with at least 5 list
        // rows (user's choice 2026-11-06, Rows below)
        static IEnumerable<KeyValuePair<string, Func<Form>>> SetupCases()
        {
            foreach (int tab in new[] { 0, 1, 3 })
            {
                int t = tab;
                yield return new KeyValuePair<string, Func<Form>>("printer setup, 8 printers + 3 USB + pending, tab " + t, () => SetupWithUsb(t, "a"));
            }
            foreach (string state in new[] { "a", "b", "c", "d", "e", "f" })
            {
                string st = state;
                yield return new KeyValuePair<string, Func<Form>>("printer setup, 8 printers + 3 USB + pending, share " + st, () => SetupWithUsb(2, st));
            }
        }

        static Form SetupWithUsb(int tab, string share)
        {
            SetupForm.NoSaveTab = true;
            var f = new SetupForm();
            const BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
            ((TabBar)typeof(SetupForm).GetField("tabs", any).GetValue(f)).Selected = tab;
            // a: one printer, not shared; b: shared; c: two; d: none; e: the name taken; f: Windows' sharing off
            Func<string, string, string, ShareablePrinter> printer = (n, port, name) => new ShareablePrinter { Name = n, Port = port, ShareName = name, Model = QlModel.ForDriver(n) };
            var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var printers = new List<ShareablePrinter>();
            var reach = SharingReach.Ok;
            if (share == "a" || share == "f") printers.Add(printer("Brother QL-800 (Unidrv)", "USB001", null));
            if (share == "b" || share == "c") { printers.Add(printer("Brother QL-800 (Unidrv)", "USB001", "QL-800")); taken["QL-800"] = "Brother QL-800 (Unidrv)"; }
            if (share == "c" || share == "e") printers.Add(printer("Brother QL-1110NWB (Unidrv)", "USB002", null));
            if (share == "e") taken["QL-1110NWB"] = "Brother QL-1110NWB (Brother)";
            if (share == "f") reach = SharingReach.FirewallOffPublic;
            f.ShowSharePage(printers, taken, reach);
            if (share == "e") ((TextBox)typeof(SetupForm).GetField("shareNameBox", any).GetValue(f)).Text = "QL-1110NWB";
            var list = (ListBox)typeof(SetupForm).GetField("printerList", any).GetValue(f);
            var listed = (List<string>)typeof(SetupForm).GetField("listed", any).GetValue(f);
            list.Items.Clear(); listed.Clear();
            for (int i = 0; i < 8; i++) { listed.Add("Brother QL-720NW (Unidrv) " + i); list.Items.Add("Brother QL-720NW (Unidrv) " + i + "   (IP_192.0.2." + (50 + i) + ")"); }
            list.Visible = true;
            ((Control)typeof(SetupForm).GetField("printerButtons", any).GetValue(f)).Visible = true;
            f.ShowUsbRows(new List<UsbCandidate>
            {
                new UsbCandidate { Model = MediaTable.Models.First(m => m.Name == "QL-800"), Port = "USB001" },
                new UsbCandidate { Model = MediaTable.Models.First(m => m.Name == "QL-570"), Port = "USB002", BrotherQueue = "Brother QL-570" },
                new UsbCandidate { Model = MediaTable.Models.First(m => m.Name == "QL-1110NWB"), Port = "USB003" },
            });
            typeof(SetupForm).GetMethod("Done", any).Invoke(f, new object[] { Lang.F("Printer '{0}' is being removed", "Brother QL-720NW (Unidrv) 5"),
                SetupForm.PendingNote(new Dictionary<string, string> { { "pending", "Brother QL-720NW (Unidrv) 5,0" }, { "pending-port", "IP_192.0.2.55" } }) });
            return f;
        }

        static void ToDpi(Form f, int dpi)
        {
            var r = new RECT { L = f.Left, T = f.Top, R = f.Left + f.Width * dpi / f.DeviceDpi, B = f.Top + f.Height * dpi / f.DeviceDpi };
            SendMessage(f.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref r);
            Pump(1200);
        }

        static void Height(string where, int pixels, int dpi)
        {
            int logical = pixels * 96 / dpi;
            if (logical > 690) Find("height", where + " " + dpi + " dpi", logical + " px logical (rule: 690)");
        }

        // Printer setup's list: at least 5 rows when it has that many printers (fewer make it worthless, 2026-11-06)
        static void Rows(Form f, string where)
        {
            var field = f.GetType().GetField("printerList", BindingFlags.Instance | BindingFlags.NonPublic);
            var list = field == null ? null : (ListBox)field.GetValue(f);
            if (list == null || !list.Visible || list.ItemHeight == 0) return;
            int rows = list.ClientSize.Height / list.ItemHeight;
            if (rows < Math.Min(5, list.Items.Count)) Find("rows", where, "printer list " + rows + " rows (rule: at least 5)");
        }

        static readonly HashSet<string> reported = new HashSet<string>();

        static void Inspect(Form f, string where)
        {
            var bad = new List<Rectangle>();
            foreach (var c in Walk(f))
            {
                var combo = c as ComboBox;
                if (combo != null && combo.Visible)
                {
                    string cw = ComboCut(combo);
                    if (cw == null) continue;
                    string ckey = where.Substring(0, where.IndexOf(' ')) + "|combo|" + combo.Name + "|" + combo.Items.Count + "|" + cw + "|" + f.DeviceDpi;
                    bad.Add(f.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)));
                    if (reported.Add(ckey)) Find("clip", where, cw);
                    continue;
                }
                if (!c.Visible || string.IsNullOrEmpty(c.Text)) continue;
                if (!(c is Label || c is ButtonBase)) continue;
                string what = null;
                // a LinkLabel sized by its window measures itself (its link layout); the pixel check below shows a cut link
                Size pref = c.AutoSize ? c.GetPreferredSize(c.MaximumSize.Width > 0 ? new Size(c.MaximumSize.Width, 0) : Size.Empty) :
                    c is LinkLabel ? c.GetPreferredSize(new Size(c.Width, 0)) : NeedOf(c);
                if (pref.Width > c.Width + 1 || pref.Height > c.Height + 1) what = "needs " + pref.Width + "x" + pref.Height + ", has " + c.Width + "x" + c.Height;
                // cut by a container: only what the text itself needs counts (a check box's trailing padding may go)
                var seen = Visible(c);
                var text = TextExtent(c);
                if (what == null && (seen.Width < text.Width - 1 || seen.Height < Math.Min(text.Height, c.Height) - 1))
                    what = "cut by its container to " + seen.Width + "x" + seen.Height + ", the text needs " + text.Width + "x" + text.Height + (cutBy != null ? " (by " + cutBy + ")" : "");
                // a LinkLabel paints its link part clipped to the link's own measure: cut inside the control
                var link = c as LinkLabel;
                if (what == null && link != null)
                {
                    var plain = PlainExtent(link);
                    if (text.Width < plain.Width - 2 || text.Height < plain.Height - 2)
                        what = "link text cut inside the control: drawn " + text.Width + "x" + text.Height + ", the same text as a label " + plain.Width + "x" + plain.Height;
                }
                if (what == null) continue;
                string key = where.Substring(0, where.IndexOf(' ')) + "|" + c.Text + "|" + f.DeviceDpi;
                bad.Add(f.RectangleToClient(c.RectangleToScreen(c.ClientRectangle)));
                if (reported.Add(key)) Find("clip", where, c.GetType().Name + " \"" + Short(c.Text) + "\" " + what);
            }
            // a link row: "|" only between two visible links (not doubled, not first or last)
            foreach (var row in Walk(f).OfType<FlowLayoutPanel>())
            {
                var shown = row.Controls.Cast<Control>().Where(x => x.Visible && !string.IsNullOrEmpty(x.Text)).Select(x => x.Text.Trim()).ToList();
                if (!shown.Contains("|")) continue;
                bool bad2 = shown[0] == "|" || shown[shown.Count - 1] == "|" || shown.Zip(shown.Skip(1), (x, y) => x == "|" && y == "|").Any(z => z);
                if (bad2 && reported.Add("separator|" + string.Join(" ", shown) + "|" + f.DeviceDpi))
                    Find("separator", where, "link row \"" + Short(string.Join(" ", shown)) + "\"");
            }
            foreach (var strip in Walk(f).OfType<ToolStrip>())
                foreach (ToolStripItem item in strip.Items)
                    if (item.Available && item.IsOnOverflow && reported.Add("overflow|" + item.Text + "|" + f.DeviceDpi))
                        Find("overflow", where, "toolbar item \"" + Short(item.Text ?? item.ToolTipText) + "\" pushed into the overflow");
            if (bad.Count > 0) Shot(f, where, bad);
        }

        // A drop-down list whose texts don't fit (null: they do): every item (and so the selected one) against the box's
        // text area left of the arrow button (lists of other objects: the selected text), the open list's width, and the
        // part its containers let show. Editable boxes (fonts) only by the open list: their own text scrolls.
        static string ComboCut(ComboBox c)
        {
            if (c.Items.Count == 0) return null;
            int arrow = SystemInformation.GetVerticalScrollBarWidthForDpi(c.DeviceDpi);
            int area = c.ClientSize.Width - arrow - 6;   // the arrow button and the text's inner margins
            int list = c.DropDownWidth - 6 - (c.Items.Count > c.MaxDropDownItems ? arrow : 0);
            string widest = null;
            int need = 0;
            foreach (var item in c.Items)
            {
                string t = c.GetItemText(item);
                int w = TextRenderer.MeasureText(t, c.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
                if (w > need) { need = w; widest = t; }
            }
            // every item against the box only for the program's own lists (strings): the toolbar's label formats are
            // the user's names, capped by design and shown whole in the open list
            bool own = c.Items.Cast<object>().All(i => i is string);
            if (c.DropDownStyle == ComboBoxStyle.DropDownList && (own ? need : TextRenderer.MeasureText(c.Text, c.Font, Size.Empty, TextFormatFlags.NoPadding).Width) > area)
                return "ComboBox item \"" + Short(widest) + "\" needs " + need + " px, the box shows " + area;
            if (need > list)
                return "ComboBox list: item \"" + Short(widest) + "\" needs " + need + " px, the open list shows " + list;
            var seen = Visible(c);
            if (seen.Width < c.Width - 1)
                return "ComboBox \"" + Short(c.Text) + "\" cut by its container to " + seen.Width + " of " + c.Width + " px" + (cutBy != null ? " (by " + cutBy + ")" : "");
            return null;
        }

        // The text's own size for a control that doesn't size itself.
        static Size NeedOf(Control c)
        {
            var flags = TextFormatFlags.WordBreak;
            int width = c.ClientSize.Width - (c is ButtonBase ? 8 : 0) - (c is CheckBox || c is RadioButton ? 20 : 0);
            var s = TextRenderer.MeasureText(c.Text, c.Font, new Size(Math.Max(1, width), int.MaxValue), flags);
            if (c is CheckBox || c is RadioButton) s.Width += 20;
            if (c is ButtonBase && !(c is CheckBox) && !(c is RadioButton)) s = new Size(Math.Min(s.Width, c.Width), s.Height);
            return new Size(Math.Min(s.Width, c.Width), s.Height);
        }

        // From the control's left/top: how far its drawn content (text, a check box's box) really reaches, found in
        // the control drawn on its own (DrawToBitmap): the last pixel that differs from its background.
        static Size TextExtent(Control c)
        {
            using (var b = new Bitmap(Math.Max(1, c.Width), Math.Max(1, c.Height)))
            {
                c.DrawToBitmap(b, new Rectangle(0, 0, b.Width, b.Height));
                var bg = b.GetPixel(b.Width - 1, 0);
                int right = 0, bottom = 0;
                for (int y = 0; y < b.Height; y++)
                    for (int x = 0; x < b.Width; x++)
                    {
                        var p = b.GetPixel(x, y);
                        if (Math.Abs(p.R - bg.R) + Math.Abs(p.G - bg.G) + Math.Abs(p.B - bg.B) < 60) continue;
                        if (x + 1 > right) right = x + 1;
                        if (y + 1 > bottom) bottom = y + 1;
                    }
                return new Size(right, bottom);
            }
        }

        // How far the link's text reaches drawn as a plain label of the same size, font and rendering.
        static Size PlainExtent(LinkLabel l)
        {
            using (var plain = new Label { AutoSize = false, Size = l.Size, Font = l.Font, Text = l.Text, Padding = l.Padding, TextAlign = l.TextAlign,
                UseCompatibleTextRendering = l.UseCompatibleTextRendering, BackColor = l.BackColor, ForeColor = l.BackColor.GetBrightness() > 0.5f ? Color.Black : Color.White })
                return TextExtent(plain);
        }

        // The part of the control its containers let show (screen coordinates intersected up to the form).
        static string cutBy;   // the container that cut it last (Visible), named in the finding

        static Rectangle Visible(Control c)
        {
            cutBy = null;
            var r = c.RectangleToScreen(c.ClientRectangle);
            bool vertical = false, horizontal = false;   // a scrolling panel on the way: cut in that direction on purpose
            for (var p = c.Parent; p != null; p = p.Parent)
            {
                var pr = p.RectangleToScreen(p.ClientRectangle);
                var sc = p as ScrollableControl;
                if (sc != null && sc.AutoScroll)
                {
                    vertical |= sc.VerticalScroll.Visible;
                    horizontal |= sc.HorizontalScroll.Visible;
                }
                if (vertical) { pr.Y = r.Y; pr.Height = r.Height; }
                if (horizontal) { pr.X = r.X; pr.Width = r.Width; }
                var before = r;
                r.Intersect(pr);
                if (r != before) cutBy = p.GetType().Name + (string.IsNullOrEmpty(p.Name) ? "" : " " + p.Name) + (sc != null && sc.AutoScroll ? " (scrolls, bar " + (sc.VerticalScroll.Visible ? "shown" : "hidden") + ")" : "");
            }
            return r;
        }

        static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control c in root.Controls)
            {
                yield return c;
                foreach (var d in Walk(c)) yield return d;
            }
        }

        static string Short(string s) { s = (s ?? "").Replace("\n", " ").Replace("\r", ""); return s.Length > 60 ? s.Substring(0, 57) + "..." : s; }

        static void Shot(Form f, string where, List<Rectangle> marks)
        {
            using (var b = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(b))
                {
                    var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc);
                    var off = f.PointToScreen(Point.Empty);
                    using (var pen = new Pen(Color.Red, 2))
                        foreach (var m in marks) g.DrawRectangle(pen, m.X + off.X - f.Left - 2, m.Y + off.Y - f.Top - 2, m.Width + 4, m.Height + 4);
                }
                string n = new string(where.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
                b.Save(Path.Combine(outDir, n + ".png"), ImageFormat.Png);
            }
        }

        // ---------- (b) drag consistency ----------

        [DllImport("user32.dll")] static extern int GetUpdateRgn(IntPtr h, IntPtr rgn, bool erase);
        [DllImport("user32.dll")] static extern bool ValidateRect(IntPtr h, IntPtr rect);
        [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int a, int b, int c, int d);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam, lParam; public int time, x, y; }
        [DllImport("user32.dll")] static extern bool PeekMessage(out MSG m, IntPtr h, int min, int max, int remove);
        [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG m);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        static Bitmap screen;

        static void Drag()
        {
            var doc = F<LabelDoc>("doc");
            // the self-test: a change shown without invalidating must be found
            canvas.Selected = doc.Objects[0]; Pump(400);
            StartScreen();
            ((List<LabelObject>)typeof(EditorCanvas).GetField("selection", All).GetValue(canvas)).Clear();
            if (Stale("selftest", false) == 0) Find("drag", "selftest", "the check found no difference where there is one");
            EndScreen();
            foreach (var o in doc.Objects.ToList())
            {
                string n = Describe(o);
                bool draft = o is ImageObject;   // drawn as a quick draft while dragging, exact on release
                // move
                canvas.Selected = o; Pump(400);
                StartScreen();
                var c = Centre(o);
                Mouse("OnMouseDown", c);
                Point at = c;
                for (int i = 1; i <= 10; i++) { at = new Point(c.X + i * 9, c.Y + (i % 3) * 5); Mouse("OnMouseMove", at); }
                Stale(n + " move (dragging)", draft);
                Mouse("OnMouseUp", at);
                Stale(n + " move (released)", false);
                // resize from a handle (lines: an end)
                var hs = Handles(o);
                if (hs.Count > 0)
                {
                    var h = Point.Round(new PointF(hs[hs.Count / 2].X + hs[hs.Count / 2].Width / 2, hs[hs.Count / 2].Y + hs[hs.Count / 2].Height / 2));
                    Mouse("OnMouseDown", h);
                    for (int i = 1; i <= 8; i++) { at = new Point(h.X + i * 6, h.Y + i * 3); Mouse("OnMouseMove", at); }
                    Stale(n + " resize (dragging)", draft);
                    Mouse("OnMouseUp", at);
                    Stale(n + " resize (released)", false);
                }
                // turn: the grip for text, boxes and pictures; 90° steps for the rest
                PointF stem, grip;
                var gripArgs = new object[] { o, PointF.Empty, PointF.Empty };
                bool hasGrip = (bool)typeof(EditorCanvas).GetMethod("Grip", All).Invoke(canvas, gripArgs);
                if (hasGrip)
                {
                    grip = (PointF)gripArgs[2];
                    var cc = Centre(o);
                    Mouse("OnMouseDown", Point.Round(grip));
                    for (int i = 1; i <= 8; i++)
                    {
                        double a = -Math.PI / 2 + i * 0.12;
                        double rr = Math.Sqrt((grip.X - cc.X) * (grip.X - cc.X) + (grip.Y - cc.Y) * (grip.Y - cc.Y));
                        at = new Point((int)(cc.X + rr * Math.Cos(a)), (int)(cc.Y + rr * Math.Sin(a)));
                        Mouse("OnMouseMove", at);
                    }
                    Stale(n + " turn (dragging)", draft);
                    Mouse("OnMouseUp", at);
                    Stale(n + " turn (released)", false);
                }
                else
                {
                    typeof(EditorForm).GetMethod("Rotate", All, null, Type.EmptyTypes, null).Invoke(form, null);
                    Settle();
                    Stale(n + " turn 90°", false);
                }
                EndScreen();
                // back to where it was, for the next object
                typeof(EditorForm).GetMethod("Undo", All, null, new[] { typeof(bool) }, null).Invoke(form, new object[] { false }); Pump(200);
                typeof(EditorForm).GetMethod("Undo", All, null, new[] { typeof(bool) }, null).Invoke(form, new object[] { false }); Pump(200);
                typeof(EditorForm).GetMethod("Undo", All, null, new[] { typeof(bool) }, null).Invoke(form, new object[] { false }); Pump(400);
                doc = F<LabelDoc>("doc");
            }
        }

        static Point Centre(LabelObject o)
        {
            var line = o as LineObject;
            PointF mm = line != null ? new PointF((line.Start.X + line.End.X) / 2, (line.Start.Y + line.End.Y) / 2)
                                     : new PointF((float)(o.X + o.W / 2), (float)(o.Y + o.H / 2));
            var p = (PointF)typeof(EditorCanvas).GetMethod("ToScreen", All, null, new[] { typeof(float), typeof(float) }, null).Invoke(canvas, new object[] { mm.X, mm.Y });
            return Point.Round(p);
        }

        static List<RectangleF> Handles(LabelObject o)
        {
            return (List<RectangleF>)typeof(EditorCanvas).GetMethod("Handles", All).Invoke(canvas, new object[] { o });
        }

        static void Mouse(string what, Point p)
        {
            typeof(EditorCanvas).GetMethod(what, All).Invoke(canvas, new object[] { new MouseEventArgs(MouseButtons.Left, 1, p.X, p.Y, 0) });
            if (screen != null) Settle(); else Pump(60);
        }

        static void StartScreen() { EndScreen(); Settle(); screen = Full(false); }
        static void EndScreen() { if (screen != null) { screen.Dispose(); screen = null; } }

        static void Paint(Graphics g, Rectangle clip)
        {
            typeof(Control).GetMethod("OnPaintBackground", All).Invoke(canvas, new object[] { new PaintEventArgs(g, clip) });
            typeof(Control).GetMethod("OnPaint", All).Invoke(canvas, new object[] { new PaintEventArgs(g, clip) });
        }

        // truth: the label rendered afresh instead of the canvas's kept bitmap (updated in parts while dragging).
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

        // Every message but WM_PAINT, then the invalidated region painted into the emulated screen.
        static void Settle()
        {
            for (int round = 0; round < 15; round++)
            {
                MSG m;
                for (int n = 0; n < 500; n++)
                {
                    if (!PeekMessage(out m, IntPtr.Zero, 0, 0x0E, 1) && !PeekMessage(out m, IntPtr.Zero, 0x10, 0xFFFF, 1)) break;
                    TranslateMessage(ref m);
                    DispatchMessage(ref m);
                }
                if (screen != null && screen.Size != canvas.Size)
                {
                    // the canvas grew or shrank (an object dragged beyond the label): what was shown stays at its place
                    var grown = new Bitmap(canvas.Width, canvas.Height);
                    using (var g = Graphics.FromImage(grown)) g.DrawImageUnscaled(screen, 0, 0);
                    screen.Dispose();
                    screen = grown;
                }
                if (screen != null)
                {
                    IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
                    try
                    {
                        if (GetUpdateRgn(canvas.Handle, rgn, false) > 1)
                            using (var region = Region.FromHrgn(rgn))
                            using (var g = Graphics.FromImage(screen))
                            {
                                g.Clip = region;
                                Paint(g, Rectangle.Round(region.GetBounds(g)));
                            }
                    }
                    finally { DeleteObject(rgn); }
                }
                ValidateRect(canvas.Handle, IntPtr.Zero);
                Thread.Sleep(15);
            }
        }

        // Differing dots between the emulated screen and the truth; a finding unless expected (a picture's draft).
        static int Stale(string name, bool expected)
        {
            Settle();
            int diff = 0;
            var box = Rectangle.Empty;
            using (var fresh = Full(true))
            {
                var a = screen.LockBits(new Rectangle(0, 0, screen.Width, screen.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var b = fresh.LockBits(new Rectangle(0, 0, fresh.Width, fresh.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var pa = new int[a.Width * a.Height]; var pb = new int[pa.Length];
                Marshal.Copy(a.Scan0, pa, 0, pa.Length); Marshal.Copy(b.Scan0, pb, 0, pb.Length);
                screen.UnlockBits(a); fresh.UnlockBits(b);
                // Paint left behind is a whole object's worth of strongly different pixels; scaling only a part of
                // the label again (at a zoom that isn't whole pixels per dot) changes edge smoothing a little, which
                // isn't. So: pixels that differ by more than 300 of 765 in R+G+B, with at least 3 such neighbours.
                int w = a.Width, h = a.Height;
                var strong = new bool[pa.Length];
                for (int i = 0; i < pa.Length; i++)
                {
                    int x = pa[i], y = pb[i];
                    strong[i] = Math.Abs((x >> 16 & 255) - (y >> 16 & 255)) + Math.Abs((x >> 8 & 255) - (y >> 8 & 255)) + Math.Abs((x & 255) - (y & 255)) > 300;
                }
                for (int yy = 1; yy < h - 1; yy++)
                    for (int xx = 1; xx < w - 1; xx++)
                    {
                        int i = yy * w + xx;
                        if (!strong[i]) continue;
                        int n = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if ((dx != 0 || dy != 0) && strong[i + dy * w + dx]) n++;
                        if (n < 3) continue;
                        diff++;
                        var r = new Rectangle(xx, yy, 1, 1);
                        box = box.IsEmpty ? r : Rectangle.Union(box, r);
                    }
                if (diff > 0 && name != "selftest")
                {
                    string n = new string(name.Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray());
                    screen.Save(Path.Combine(outDir, n + "-shown.png"), ImageFormat.Png);
                    fresh.Save(Path.Combine(outDir, n + "-truth.png"), ImageFormat.Png);
                    if (expected) Console.WriteLine("INFO drag " + name + ": " + diff + " dots differ (a picture is a draft while dragging)");
                    else Find("drag", name, diff + " dots differ from a fresh render in " + box);
                }
            }
            return diff;
        }

        // ---------- (c) barcode sizes ----------

        static void Sizes()
        {
            var doc = F<LabelDoc>("doc");
            var layout = F<LabelLayout>("layout");
            var kinds = doc.Objects.OfType<BarcodeObject>().Where(b => b.Kind != Symbology.Qr || b.QrType == QrType.Text).ToList();
            var tsv = new StringBuilder("kind\twidth_mm\theight_mm\tproblem\thint\ttext_dots\timage\n");
            foreach (var proto in kinds)
            {
                for (double w = 8; w <= 60; w += 2)
                {
                    var one = doc.Clone();
                    one.Objects.Clear();
                    var b = (BarcodeObject)LabelObject.FromJson(proto.ToJson());
                    b.X = 5; b.Y = 5; b.W = w; b.H = b.Kind == Symbology.Qr ? w : 16;
                    if (b.H > 50) break;
                    one.Objects.Add(b);
                    // the panel's lines: the problem that stops printing, else the warnings about a code that may not scan
                    var now = DateTime.Now;
                    string problem = EditorRender.Problem(b, layout, now) ?? "";
                    string hint = EditorRender.BarcodeHint(b, layout, now) ?? "";
                    int textDots = 0;
                    string png = Describe(b) + "-" + w.ToString("00") + "mm.pgm";
                    using (var with = EditorRender.Render(one, layout))
                    {
                        SavePgm(with, Path.Combine(outDir, png));
                        if (b.Kind != Symbology.Qr)
                        {
                            b.ShowText = false;
                            using (var without = EditorRender.Render(one, layout)) textDots = Differ(with, without);
                        }
                    }
                    tsv.Append(Describe(b) + "\t" + w + "\t" + b.H + "\t" + problem.Replace("\t", " ") + "\t" + hint.Replace("\t", " ").Replace("\n", " / ") + "\t" + textDots + "\t" + png + "\n");
                }
            }
            File.WriteAllText(Path.Combine(outDir, "sizes.tsv"), tsv.ToString(), new UTF8Encoding(false));
            Console.WriteLine("INFO sizes.tsv written");
        }

        // Grey PGM (P5) for the decoder: no image library needed on the Python side.
        static void SavePgm(Bitmap bmp, string path)
        {
            var data = new byte[bmp.Width * bmp.Height];
            using (var c = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(c)) { g.Clear(Color.White); g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height); }
                var d = c.LockBits(new Rectangle(0, 0, c.Width, c.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var px = new int[c.Width * c.Height];
                Marshal.Copy(d.Scan0, px, 0, px.Length);
                c.UnlockBits(d);
                for (int i = 0; i < px.Length; i++) data[i] = (byte)(((px[i] >> 16 & 255) + (px[i] >> 8 & 255) + (px[i] & 255)) / 3);
            }
            using (var f = File.Create(path))
            {
                var head = Encoding.ASCII.GetBytes("P5\n" + bmp.Width + " " + bmp.Height + "\n255\n");
                f.Write(head, 0, head.Length);
                f.Write(data, 0, data.Length);
            }
        }

        static int Differ(Bitmap a, Bitmap b)
        {
            int n = 0;
            for (int y = 0; y < Math.Min(a.Height, b.Height); y++)
                for (int x = 0; x < Math.Min(a.Width, b.Width); x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) n++;
            return n;
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
