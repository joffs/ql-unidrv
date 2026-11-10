// QL Label Maker, File > Print with data: the dialog on a label with three data
// fields and CSV files written here (20 rows, 600 rows, a copies column, header only). Checks the field line both ways,
// Print blocked by a missing field and freed by "Leave missing fields empty", all selected at the start, the search,
// Explorer's selection (click, Ctrl+click, Shift+click, Ctrl+A, None), the count in the Print button with copies,
// lazy previews (only cards in view, 600 rows open at once), the empty CSV text, the minimum size. Prints nothing,
// saves nothing; the dialog's remembered size is put back. Runs only through deskrun.exe (a desktop of its own: no
// mouse, no focus taken). A watchdog ends it after 240 s.
// Run by printdata-ui.ps1:  printdata_ui.exe <de|en> <work folder>  ->  CHECK lines
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class PrintDataUi
    {
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        static EditorForm form;
        static int failures;
        static string dir;

        [STAThread]
        static int Main(string[] args)
        {
            var watchdog = new Thread(() =>
            {
                Thread.Sleep(240000);
                Console.WriteLine("CHECK watchdog: the test ended within 240 s FAIL");
                Console.Out.Flush();
                Environment.Exit(3);
            }) { IsBackground = true };
            watchdog.Start();
            Lang.Force(args[0]);
            dir = args[1];
            Directory.CreateDirectory(dir);
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

        static object Field(object o, string name) { return o.GetType().GetField(name, All).GetValue(o); }
        static object Call(object o, string method, params object[] a)
        {
            var m = o.GetType().GetMethods(All).First(x => x.Name == method && x.GetParameters().Length == a.Length);
            return m.Invoke(o, a);
        }

        static void Pump(int ms)
        {
            var w = Stopwatch.StartNew();
            while (w.ElapsedMilliseconds < ms) { Application.DoEvents(); Thread.Sleep(10); }
        }

        static string Csv(string name, string text)
        {
            string p = Path.Combine(dir, name);
            File.WriteAllText(p, text, new UTF8Encoding(true));
            return p;
        }

        static TextObject AddText(string text, double x, double y)
        {
            var t = new TextObject { X = x, Y = y, W = 40, H = 7, Text = text, Size = 10, Kind = TextKind.Line };
            EditorRender.FitText(t);
            Call(form, "Add", t);
            Pump(50);
            return t;
        }

        // Opens the dialog on a CSV and runs the steps inside its modal loop; the dialog is closed with Cancel afterwards.
        static void WithDialog(string csv, Action<PrintDataDialog> steps)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 50 };
            timer.Tick += delegate
            {
                var d = Field(form, "printDataDialog") as PrintDataDialog;
                if (d == null || !d.Visible) return;
                timer.Stop();
                try { Pump(200); steps(d); }
                catch (Exception e) { Console.WriteLine("FAIL " + e); failures++; }
                if (d.Visible && d.DialogResult == DialogResult.None) d.DialogResult = DialogResult.Cancel;   // not after a print
            };
            timer.Start();
            Call(form, "PrintWithData", csv);
            timer.Dispose();
            Pump(100);
        }

        static PrintDataGrid Grid(PrintDataDialog d) { return (PrintDataGrid)Field(d, "grid"); }
        static Button PrintButton(PrintDataDialog d) { return (Button)Field(d, "print"); }
        static string MatchLine(PrintDataDialog d) { return Chips(d).Line; }
        static ChipRow Chips(PrintDataDialog d) { return (ChipRow)Field(d, "fieldChips"); }
        static HeadTile Tile(PrintDataDialog d, string name) { return (HeadTile)Field(d, name + "Tile"); }

        static void ClickCard(PrintDataGrid g, int index, Keys mods)
        {
            Call(g, "ClickCard", index, mods);
            Pump(30);
        }

        // A check of QL-800 Capture as the status window makes it: ready with a 62 or 29 mm roll, or (null) not connected.
        static PrinterCheck Status(string mm)
        {
            var device = new DeviceResult { Printer = "QL-800 Capture", Port = "USB001", State = mm == null ? DeviceState.NotConnected : DeviceState.Open };
            if (mm != null)
                device.Reply = QlStatus.FromHex("80 20 42 34 38 30 00 00 00 00 " + int.Parse(mm).ToString("X2") + " 0A 00 00 15 00 00 00 00 00 00 00 00 00 00 01 00 00 00 00 00 00", DateTime.Now);
            return PrinterCheck.From("QL-800 Capture", device, new List<JobInfo>(), null);
        }

        static void Roll(PrinterCheck c)
        {
            typeof(EditorForm).GetField("roll", All).SetValue(form, c);
            Call(form, "RollToDialog");
            Pump(50);
        }

        static int LogicalPx(Control c, int px) { return px * c.DeviceDpi / 96; }

        static int Rendered(PrintDataGrid g) { return ((System.Collections.ICollection)Field(g, "previews")).Count; }

        static void Run()
        {
            QlPrinters.LastKey = @"Software\QLUnidrv\Tests\PrintDataUi";   // the user's last printer stays
            Pump(1200);
            AddText("{Artikel}", 4, 3);
            AddText("Nr. {Nummer|–}", 4, 11);
            AddText("{Lagerort}", 4, 19);
            var rows = new StringBuilder("Artikel;Nummer;Preis\r\n");
            for (int i = 1; i <= 20; i++) rows.Append("Teil " + (char)('A' + i - 1) + ";V-" + i.ToString("000") + ";" + (100 + i) + ",00\r\n");   // only row 15 has "15" (115,00)
            string csv20 = Csv("teile.csv", rows.ToString());
            string remembered = ToolSetting("PrintDataSize") as string;
            object rememberedCards = ToolSetting("PrintDataCardSize");

            WithDialog(csv20, d =>
            {
                var g = Grid(d);
                string line = MatchLine(d);
                Console.WriteLine("INFO fields: " + line);
                Check(line.Contains("✓ Artikel") && line.Contains("✓ Nummer") && line.Contains("✗ " + Lang.F("{0} missing", "Lagerort")) && line.Contains("Preis"),
                      "field line: found, missing, not on the label");
                Check(line.StartsWith("✗ ", StringComparison.Ordinal) && Chips(d).Chips[0].Kind == ChipKind.Blocking, "the missing field comes first, as blocking");
                Check(line.IndexOf(Lang.T("not on the label:"), StringComparison.Ordinal) < line.IndexOf("Preis", StringComparison.Ordinal), "the unused column after its key");
                Check(!PrintButton(d).Enabled, "Print blocked by the missing field");
                // the tiles (mockup A, 2026-11-09)
                var pt = Tile(d, "printer"); var tt = Tile(d, "template"); var dt = Tile(d, "data");
                Console.WriteLine("INFO tiles: " + pt.Main + " / " + pt.Sub + " | " + tt.Main + " / " + tt.Sub + " | " + dt.Main + " / " + dt.Sub);
                Check(pt.Caption == Lang.T("Printer") && pt.Main == "QL-800 Capture" && pt.Chevron && pt.Clickable, "printer tile: QL-800 Capture, a list");
                Check(pt.Sub == Lang.T("Status unknown") + " · " + Lang.T("prints to a file") && pt.State == TileState.Unknown, "printer tile: a file port has no status");
                Check(tt.Caption == Lang.T("Template") && tt.Sub.EndsWith(" · " + Lang.F("{0} fields", 3), StringComparison.Ordinal) && !tt.Clickable, "template tile: 3 fields, not a button");
                Check(dt.Main == "teile.csv" && dt.Sub == "CSV · " + Lang.F("{0} rows", 20) && !dt.CallToAction, "data tile: teile.csv, CSV · 20 rows");
                var change = (Button)Field(d, "chooseButton");
                Check(dt.State == TileState.Warn && change.Visible && change.Text == Lang.T("Change..."), "data tile amber while a field is missing, with Change...");
                Check((bool)Field(d, "emptyHot") && ((Panel)Field(d, "emptyFrame")).Visible && !((Label)Field(d, "waitInfo")).Visible, "Leave missing fields empty framed red");
                var note = (Label)Field(d, "sentInfo");
                Check(note.Text.Contains("{Lagerort}") && note.ForeColor == Theme.Of(Role.ErrorText), "the footer says what is missing, in red: " + note.Text);
                Check(tt.Height <= (int)(60 * d.DeviceDpi / 96f) && tt.Height >= (int)(44 * d.DeviceDpi / 96f), "tiles about 52 px high: " + tt.Height);
                var searchBox = (TextBox)Field(d, "search");
                int head = d.PointToClient(searchBox.PointToScreen(Point.Empty)).Y;
                Console.WriteLine("INFO the search box starts at " + head + " px (dpi " + d.DeviceDpi + ")");
                Check(head <= (int)(115 * d.DeviceDpi / 96f), "the top is lower than before: search at " + head + " px");

                // the printer tile's states from the editor's check (a QL-800's replies, as helpshots.cs FixedRoll)
                Roll(Status("62"));
                Check(pt.State == TileState.Ok && pt.Dot == Health.Ok && pt.Sub.StartsWith(Lang.T("Ready") + " · 62", StringComparison.Ordinal), "printer ready: " + pt.Sub);
                Roll(Status("29"));
                Check(pt.State == TileState.Warn && pt.Dot == Health.Warning && pt.Sub.StartsWith(Lang.T("Wrong roll") + " · 29", StringComparison.Ordinal), "the roll doesn't fit: " + pt.Sub);
                SaveShot(d, "wrong-roll");
                Roll(Status(null));
                Check(pt.State == TileState.Warn && pt.Dot == Health.Warning && pt.Sub == Lang.T("Not connected") && char.IsUpper(pt.Sub[0]), "not connected: " + pt.Sub);
                Roll(null);
                Check(pt.State == TileState.Unknown, "and unknown again");
                Check(g.Shown.Count == 20 && g.SelectedShown.Count == 20, "all 20 rows shown and selected at the start");
                Check(Rendered(g) > 0 && Rendered(g) < 20 + 1, "previews drawn: " + Rendered(g));
                SaveShot(d, "blocked");

                ((CheckBox)Field(d, "missingEmpty")).Checked = true;
                Pump(100);
                Check(PrintButton(d).Enabled && PrintButton(d).Text == Lang.F("Print ({0} labels)", 20), "Leave missing fields empty: " + PrintButton(d).Text);
                Check(MatchLine(d).Contains("✗ Lagerort · " + Lang.T("stays empty")) && Chips(d).Chips[0].Kind == ChipKind.Missing, "field line: Lagerort stays empty");
                Check(Tile(d, "data").State == TileState.Ok && !(bool)Field(d, "emptyHot") && ((Label)Field(d, "sentInfo")).Text == "", "data tile petrol, no red frame, no footer note");

                ((TextBox)Field(d, "search")).Text = "15";
                Pump(100);
                Check(g.Shown.Count == 1 && g.Shown[0].Row == 15, "search 15: only row 15 shown (" + g.Shown.Count + ")");
                Check(PrintButton(d).Text == Lang.T("Print (1 label)"), "Print counts the selected cards in view: " + PrintButton(d).Text);
                ((TextBox)Field(d, "search")).Text = "";
                Pump(100);

                ClickCard(g, 14, Keys.None);
                Check(g.SelectedShown.Count == 1 && g.SelectedShown[0].Row == 15, "click: only this card");
                Check(PrintButton(d).Text == Lang.T("Print (1 label)"), "Print (1 label)");
                ClickCard(g, 2, Keys.Shift);
                Check(g.SelectedShown.Count == 13 && g.SelectedShown.First().Row == 3 && g.SelectedShown.Last().Row == 15, "Shift+click: rows 3 to 15 (" + g.SelectedShown.Count + ")");
                ClickCard(g, 9, Keys.Control);
                Check(g.SelectedShown.Count == 12 && !g.IsSelected(g.Shown[9]), "Ctrl+click removes one");
                ClickCard(g, 19, Keys.Control);
                Check(g.SelectedShown.Count == 13 && g.IsSelected(g.Shown[19]), "Ctrl+click adds one");
                Call(g, "OnKeyDown", new KeyEventArgs(Keys.Control | Keys.A));
                Pump(30);
                Check(g.SelectedShown.Count == 20, "Ctrl+A: all");
                g.SelectNone();
                Pump(30);
                Check(g.SelectedShown.Count == 0 && !PrintButton(d).Enabled, "None: nothing selected, Print disabled");
                g.SelectAll();
                ((NumericUpDown)Field(d, "copies")).Value = 3;
                Pump(50);
                Check(PrintButton(d).Text == Lang.F("Print ({0} labels)", 60), "copies per label 3: " + PrintButton(d).Text);

                // the window: not smaller than the mockup
                float s = d.DeviceDpi / 96f;
                d.Size = new Size(300, 300);
                Pump(50);
                Check(d.Width >= (int)(780 * s) - 1 && d.Height >= (int)(660 * s) - 1, "minimum size " + d.Size);
                d.Size = new Size((int)(1000 * s), (int)(700 * s));
                Pump(100);
                Check(Grid(d).Width > (int)(900 * s), "the grid grows with the window");
                SaveShot(d, "wide");
            });
            string saved = ToolSetting("PrintDataSize") as string;
            Check(saved == "1000,700", "size remembered: " + saved);

            // a copies column
            string copiesCsv = Csv("copies.csv", "Artikel;Nummer;Lagerort;copies\r\nSchraube;1;R1;2\r\nMutter;2;R2;0\r\nDübel;3;R3;x\r\n");
            WithDialog(copiesCsv, d =>
            {
                var g = Grid(d);
                Check(MatchLine(d).IndexOf(Lang.T("not on the label:"), StringComparison.Ordinal) < 0 && MatchLine(d).Contains(Lang.F("Copies: {0}", "copies")),
                      "the copies column isn't listed as unused, but as the special column");
                Check((string)Call(g, "CopiesBadge", g.Shown[0]) == "×2" && (string)Call(g, "CopiesBadge", g.Shown[1]) == "×0" && (string)Call(g, "CopiesBadge", g.Shown[2]) == "×?",
                      "copies badges ×2, ×0, ×?");
                ClickCard(g, 0, Keys.None);
                Check(PrintButton(d).Text == Lang.F("Print ({0} labels)", 2), "a row with 2 copies: " + PrintButton(d).Text);
                SaveShot(d, "copies");
            });

            // labelname (2026-11-09): the cards' titles, the search; a JSON file through the same dialog
            string namedCsv = Csv("named.csv", "Artikel;Nummer;Lagerort;labelname\r\nSchraube;1;R1;A1234\r\nMutter;2;R2;\r\nDübel;3;R3;B7\r\n");
            WithDialog(namedCsv, d =>
            {
                var g = Grid(d);
                Check(g.Shown[0].Title == "A1234" && g.Shown[1].Title == "" && g.Shown[2].Title == "B7", "labelname: card titles A1234, (none), B7");
                Check(MatchLine(d).IndexOf(Lang.T("not on the label:"), StringComparison.Ordinal) < 0 && MatchLine(d).Contains(Lang.F("Name: {0}", "labelname")),
                      "labelname isn't listed as unused, but as the special column");
                var search = (TextBox)Field(d, "search");
                search.Text = "b7";
                Pump(100);
                Check(g.Shown.Count == 1 && g.Shown[0].Name == "B7", "search finds the labelname");
                search.Text = "";
                Pump(100);
                SaveShot(d, "labelname");
                string json = Csv("named.json", "[{\"Artikel\":\"Schraube\",\"Nummer\":1,\"Lagerort\":\"R1\",\"labelname\":\"J1\"},{\"Artikel\":\"Mutter\",\"Nummer\":2,\"Lagerort\":\"R2\",\"copies\":3}]");
                Call(d, "Load", json);
                Pump(300);
                Check(g.Shown.Count == 2 && g.Shown[0].Title == "J1" && g.Shown[1].Copies == 3 && MatchLine(d).Contains("✓ Lagerort"), "JSON file: rows, labelname, copies, fields found");
                Check(Tile(d, "data").Main == "named.json" && Tile(d, "data").Sub == "JSON · " + Lang.F("{0} entries", 2), "data tile: JSON · 2 entries");
                SaveShot(d, "json");

                // the search's ✕ (2026-11-09): shown while the box holds text, one click empties it
                search.Text = "j1";
                Pump(100);
                var clear = search.Controls.OfType<SearchClear>().Single();
                Check(clear.Visible && g.Shown.Count == 1, "search ✕ shown while the box holds text");
                Call(clear, "OnMouseClick", new MouseEventArgs(MouseButtons.Left, 1, 3, 3, 0));
                Pump(100);
                Check(search.Text == "" && !clear.Visible && g.Shown.Count == 2, "✕ empties the search");

                // the large preview (2026-11-09): hatched where the printer can't print, its own Print with a
                // number of copies, only this label, on the capture printer; the dialog stays open
                long before = File.Exists(Capture) ? new FileInfo(Capture).LastWriteTime.Ticks : 0;
                bool seen = false;
                int ticks = 0;
                var t = new System.Windows.Forms.Timer { Interval = 200 };
                t.Tick += delegate
                {
                    var f = Application.OpenForms.Cast<Form>().FirstOrDefault(x => x.GetType().Name == "PreviewWindow");
                    if (f == null) return;
                    if (!seen)
                    {
                        seen = true;
                        SaveShot(f, "preview");
                        var pc = (Button)Field(f, "close"); var pp = (Button)Field(f, "print");
                        Check(pc.Size == pp.Size, "large preview: Close as large as Print: " + pc.Size + " / " + pp.Size);
                        ((NumericUpDown)Field(f, "copies")).Value = 2;
                        ((Button)Field(f, "print")).PerformClick();
                    }
                    if (++ticks > 150) f.Close();   // 30 s: not sent
                };
                t.Start();
                Call(d, "ShowLarge", g.Shown[0]);
                t.Dispose();
                string sent = ((Label)Field(d, "sentInfo")).Text;
                Check(seen && sent == Lang.F("{0} labels sent to {1}.", 2, "QL-800 Capture"), "large preview: Print sent 2 labels of this one (" + sent + ")");
                Check(d.Visible && d.DialogResult == DialogResult.None, "the dialog stays open after the preview's print");
                for (int i = 0; i < 75 && !(File.Exists(Capture) && new FileInfo(Capture).LastWriteTime.Ticks > before); i++) Pump(200);
                Check(File.Exists(Capture) && new FileInfo(Capture).LastWriteTime.Ticks > before, "the capture printer got the preview's job");

                // the printer at the top (2026-11-09): switching it switches the editor too; rows stay
                var list = (List<string>)Field(d, "printers");
                Call(d, "ShowPrinterList");
                Pump(200);
                var menu = (ContextMenuStrip)Field(d, "printerList");
                var ticked = menu.Items.OfType<ToolStripMenuItem>().Where(i => i.Checked).Select(i => i.Text).ToList();
                Check(list.Contains("QL-800 Capture") && menu.Visible && menu.Items.Count == list.Count && ticked.Count == 1 && ticked[0] == "QL-800 Capture",
                      "printer tile: the list opens with QL-800 Capture ticked");
                menu.Close();
                Pump(100);
                string other = list.FirstOrDefault(n => n == "QL-1115NWB Capture");
                if (other == null) Console.WriteLine("INFO no printer \"QL-1115NWB Capture\": the switch isn't tested");
                else
                {
                    Call(d, "ChoosePrinter", other);
                    Pump(500);
                    var job = (LabelJob)Field(d, "job");
                    Check(job.Layout.Printer == other && (string)Field(form, "printer") == other && job.Records.Count == 2 && g.Shown.Count == 2,
                          "printer switched: the job's layout and the editor follow, the rows stay");
                    Check(Tile(d, "printer").Main == other, "the printer tile names it");
                    Check(QlPrinters.LastChosen() == other, "the dialog's choice is remembered as the last printer");
                    SaveShot(d, "printer-switched");
                    int at = list.IndexOf(other);
                    Call(Tile(d, "printer"), "OnKeyDown", new KeyEventArgs(Keys.Up));   // Up/Down on the tile: the printer before/after
                    Pump(500);
                    Check(at == 0 || (string)Field(d, "printer") == list[at - 1], "Up on the printer tile: the printer before");
                    Call(d, "ChoosePrinter", "QL-800 Capture");
                    Pump(500);
                    Check(((LabelJob)Field(d, "job")).Layout.Printer == "QL-800 Capture" && (string)Field(form, "printer") == "QL-800 Capture", "and back to QL-800 Capture");
                }
            });

            // no data yet: the data tile is one large button, the fields wait
            WithDialog(null, d =>
            {
                var dt = Tile(d, "data");
                Check(dt.CallToAction && dt.Clickable && dt.Main == "+ " + Lang.T("Choose data file...") && !((Button)Field(d, "chooseButton")).Visible,
                      "no data: the data tile is the button");
                Check(Chips(d).Chips.All(c => c.Kind == ChipKind.Waiting) && Chips(d).Chips.Count == 3 && ((Label)Field(d, "waitInfo")).Visible && !((Panel)Field(d, "emptyFrame")).Visible,
                      "no data: the label's fields wait, no checkbox");
                Check(!PrintButton(d).Enabled, "no data: Print disabled");
                SaveShot(d, "nodata");
            });

            // many columns: one line of chips, the rest behind "+N more", all in its list
            var wide = new StringBuilder("Artikel;Nummer");
            for (int i = 1; i <= 24; i++) wide.Append(";Spalte" + i);
            WithDialog(Csv("wide.csv", wide + "\r\nA;1;" + string.Join(";", Enumerable.Range(1, 24)) + "\r\n"), d =>
            {
                var c = Chips(d);
                Check(c.Hidden > 0 && c.Line.Contains("Spalte24") && c.TabStop, "many columns: " + c.Hidden + " behind +N more, all in the line");
                Call(c, "ShowAll");
                Pump(200);
                var all = (ContextMenuStrip)Field(c, "list");
                Check(all.Visible && all.Items.OfType<ToolStripMenuItem>().Count() == c.Chips.Count, "+N more lists every field and column");
                all.Close();
                SaveShot(d, "many-columns");
            });

            // dark and High Contrast: the tiles in the scheme's colours (an amber face in dark, none in High Contrast)
            foreach (var scheme in new[] { Scheme.Dark, Scheme.HighContrast })
            {
                Theme.Force(scheme);
                WithDialog(csv20, d =>
                {
                    var dt = Tile(d, "data");
                    using (var b = new Bitmap(dt.Width, dt.Height))
                    {
                        dt.DrawToBitmap(b, new Rectangle(Point.Empty, dt.Size));
                        Color face = b.GetPixel(dt.Width - LogicalPx(d, 12), LogicalPx(d, 3));
                        Color want = scheme == Scheme.Dark ? Theme.Of(Role.NoteBack) : Theme.Of(Role.Window);
                        Check(face.ToArgb() == want.ToArgb(), scheme + ": the data tile's face " + face + " (" + want + ")");
                    }
                    SaveShot(d, scheme == Scheme.Dark ? "dark" : "highcontrast");
                });
            }
            Theme.Force(null);

            // header only
            WithDialog(Csv("empty.csv", "Artikel;Nummer\r\n"), d =>
            {
                Check(Grid(d).Shown.Count == 0 && (string)Field(Grid(d), "emptyText") == Lang.T("The data file has no data rows."), "header only: no data rows");
                Check(!PrintButton(d).Enabled, "header only: Print disabled");
            });

            // 600 rows open at once, previews only for the cards in view
            var many = new StringBuilder("Artikel;Nummer;Lagerort\r\n");
            for (int i = 1; i <= 600; i++) many.Append("Artikel " + i + ";" + i + ";R" + i + "\r\n");
            string csv600 = Csv("many.csv", many.ToString());
            var watch = Stopwatch.StartNew();
            WithDialog(csv600, d =>
            {
                double open = watch.Elapsed.TotalMilliseconds;
                var g = Grid(d);
                Pump(1500);
                Console.WriteLine("INFO 600 rows: dialog up after " + open.ToString("0") + " ms, " + Rendered(g) + " previews drawn");
                Check(open < 5000, "600 rows: the dialog opens at once (" + open.ToString("0") + " ms)");
                Check(Rendered(g) > 0 && Rendered(g) < 100, "600 rows: previews only for cards in view (" + Rendered(g) + ")");
                SaveShot(d, "many");

                // the card size (2026-11-09): the slider, the wheel over it, Ctrl+wheel in the grid; stored per user
                var slider = (StepSlider)Field(d, "sizeSlider");
                // named Zoom as in Word's status bar, the tooltip says what it does (2026-11-09)
                Check(((Label)Field(d, "sizeLabel")).Text == Lang.T("Zoom"), "slider label: " + ((Label)Field(d, "sizeLabel")).Text);
                // Close as large as Print, in its normal look (2026-11-09)
                var closeB = (Button)Field(d, "cancel"); var printB = (Button)Field(d, "print");
                Check(closeB.Size == printB.Size, "Close as large as Print: " + closeB.Size + " / " + printB.Size);
                int cols0 = (int)g.GetType().GetProperty("Columns", All).GetValue(g, null);
                slider.StepBy(3);
                Pump(300);
                int cols3 = (int)g.GetType().GetProperty("Columns", All).GetValue(g, null);
                Check(g.SizeStep == 3 && cols3 < cols0 && Convert.ToInt32(ToolSetting("PrintDataCardSize")) == 3, "size slider: step 3, fewer columns (" + cols0 + " -> " + cols3 + "), stored");
                Call(slider, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 5, 5, 120));
                Check(slider.Value == 4 && g.SizeStep == 4, "wheel over the slider: one step larger");
                Pump(300);
                SaveShot(d, "cards-large");
                Call(slider, "OnMouseWheel", new MouseEventArgs(MouseButtons.None, 0, 5, 5, -120));
                Check(g.SizeStep == 3, "wheel down over the slider: one step smaller");
                g.SizeStep = 0;
                slider.Value = 0;
            });

            // ---------- end to end on "QL-800 Capture" (a file, no paper): rows 1-3 with copies 1, 2, 1, Lagerort missing
            // and left empty -> 4 pages; then an automatic length -> one job, every page as long as the content ----------
            string printCsv = Csv("print.csv", "Artikel;Nummer;copies\r\nSchraube M4;V-001;1\r\nMutter M5;V-002;2\r\nDübel 6;V-003;1\r\nNagel;V-004;1\r\n");
            PrintThrough("copies", printCsv, 4);
            typeof(EditorForm).GetField("autoLength", All).SetValue(form, true);
            Call(form, "FollowContent");
            Pump(300);
            Check(Field(form, "layout") != null && ((LabelLayout)Field(form, "layout")).Custom != null, "automatic length: the label follows its content");
            PrintThrough("auto", printCsv, 4);

            if (remembered != null) ToolSetting("PrintDataSize", remembered);
            else DeleteSetting("PrintDataSize");
            if (rememberedCards != null) ToolSetting("PrintDataCardSize", rememberedCards);
            else DeleteSetting("PrintDataCardSize");
            try { Microsoft.Win32.Registry.CurrentUser.DeleteSubKeyTree(QlPrinters.LastKey, false); } catch (Exception) { }
        }

        const string Capture = @"C:\ProgramData\QLUnidrv\capture\capture.prn";

        // Prints rows 1-3 through the dialog ("Leave missing fields empty" on) and keeps, in <dir>\print-<name>: the
        // capture file and expected-pN.pbm = the engine's page for each label as the printer gets it (EditorRender.
        // ForPrinter). printdata_prn_check.py compares them dot for dot (printdata-ui.ps1).
        static void PrintThrough(string name, string csv, int expectPages)
        {
            string folder = Path.Combine(dir, "print-" + name);
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
            var started = DateTime.Now;
            long before = File.Exists(Capture) ? new FileInfo(Capture).LastWriteTime.Ticks : 0;
            int pages = 0;
            bool stays = false;
            WithDialog(csv, d =>
            {
                ((CheckBox)Field(d, "missingEmpty")).Checked = true;
                var g = Grid(d);
                ClickCard(g, 0, Keys.None);
                ClickCard(g, 2, Keys.Shift);
                Check(PrintButton(d).Enabled && PrintButton(d).Text == Lang.F("Print ({0} labels)", expectPages), name + ": " + PrintButton(d).Text);
                var job = (LabelJob)Field(d, "job");
                foreach (var r in g.SelectedShown)
                    for (int i = job.CopiesOf(r); i > 0; i--)
                        using (var view = job.Render(r))
                        using (var page = EditorRender.ForPrinter(view, job.Layout))
                            WritePbm(Path.Combine(folder, "expected-p" + (++pages) + ".pbm"), page);
                Call(d, "Print");
                var w = Stopwatch.StartNew();
                var sentInfo = (Label)Field(d, "sentInfo");
                while (sentInfo.Text.Length == 0 && w.ElapsedMilliseconds < 60000) Pump(50);
                // the dialog stays open for the next print (2026-11-09); Close then
                stays = d.Visible && d.DialogResult == DialogResult.None && sentInfo.Text == Lang.F("{0} labels sent to {1}.", expectPages, "QL-800 Capture");
                Check(((Button)Field(d, "cancel")).Text == Lang.T("Close"), name + ": the button says Close");
            });
            Check(stays, name + ": printed, the dialog stayed open and said so");
            Check(pages == expectPages, name + ": " + pages + " pages expected");
            string status = ((ToolStripStatusLabel)Field(form, "labelStatus")).Text;
            Check(status == Lang.F("{0} labels sent to {1}.", expectPages, "QL-800 Capture"), name + ": status bar " + status);
            // the spooler writes the file after the job: wait until it is new and no longer grows (at most 60 s)
            var wait = Stopwatch.StartNew();
            long size = -1;
            while (wait.ElapsedMilliseconds < 60000)
            {
                Pump(500);
                if (!File.Exists(Capture)) continue;
                var fi = new FileInfo(Capture);
                if (fi.LastWriteTime.Ticks <= before) continue;
                if (fi.Length > 0 && fi.Length == size) break;
                size = fi.Length;
            }
            bool written = File.Exists(Capture) && new FileInfo(Capture).LastWriteTime.Ticks > before;
            Check(written, name + ": capture file written (" + (DateTime.Now - started).TotalSeconds.ToString("0") + " s)");
            if (written) File.Copy(Capture, Path.Combine(folder, "capture.prn"), true);
        }

        static void WritePbm(string path, Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height, stride = (w + 7) / 8;
            using (var fs = File.Create(path))
            {
                var head = Encoding.ASCII.GetBytes("P4\n" + w + " " + h + "\n");
                fs.Write(head, 0, head.Length);
                var row = new byte[stride];
                for (int y = 0; y < h; y++)
                {
                    Array.Clear(row, 0, stride);
                    for (int x = 0; x < w; x++)
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0xFFFFFF) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    fs.Write(row, 0, stride);
                }
            }
        }

        const string Key = @"Software\QLUnidrv\LabelMaker";
        static object ToolSetting(string name, object value = null)
        {
            if (value == null)
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key)) return k == null ? null : k.GetValue(name);
            using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value);
            return null;
        }

        static void DeleteSetting(string name)
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(Key, true)) if (k != null) k.DeleteValue(name, false);
        }

        static void SaveShot(Form d, string name)
        {
            string shots = Environment.GetEnvironmentVariable("QL_PRINTDATA_SHOT");
            if (shots == null) return;
            Pump(300);
            using (var b = new Bitmap(d.Width, d.Height))
            {
                d.DrawToBitmap(b, new Rectangle(Point.Empty, d.Size));
                b.Save(Path.Combine(shots, name + "-" + Lang.Current + ".png"));
            }
        }
    }
}
