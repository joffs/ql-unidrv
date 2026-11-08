// Screenshots of the Printer setup's failure result ("The change did not finish") for an unreachable share, closed and
// with "Show details", at 96 and 144 dpi: the box as SetupForm.RunSettings fills it (SetupForm.FailureDetails when the
// build has it, else the older inline text, for before/after shots). Reads a kept settings transcript (install.ps1 -Log);
// runs only through deskrun.exe (a desktop of its own: no mouse, no focus taken); changes nothing.
// Run by setup-details.ps1:  setup_details.exe <de|en> <out folder> <transcript> <tag>  ->  <out>\<tag>-<lang>-<dpi>-<shot>.png
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class SetupDetailsShots
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, ref RECT r);
        struct RECT { public int L, T, R, B; }
        const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        const string ShownLog = @"C:\Users\qladmin\AppData\Local\Temp\ql-unidrv-settings.log";   // the Sandbox user's path

        [STAThread]
        static int Main(string[] args)
        {
            string lang = args[0], outDir = args[1], transcript = args[2], tag = args[3];
            Directory.CreateDirectory(outDir);
            Lang.Force(lang);
            Application.EnableVisualStyles();
            var raw = ScriptRun.TranscriptBody(transcript);
            string error = raw.Select(l => Regex.Match(l, "^PS>TerminatingError\\(\\w*\\): \"(.*)\"$")).Where(m => m.Success)
                              .Select(m => m.Groups[1].Value).LastOrDefault();
            var reasonMethod = typeof(SetupForm).GetMethod("SettingsReason", All);
            var reasonArgs = new object[] { "share-unreachable", error, null };
            string reason = (string)reasonMethod.Invoke(null, reasonArgs), tip = (string)reasonArgs[2];
            string details;
            var helper = typeof(SetupForm).GetMethod("FailureDetails", All);
            if (helper != null)
                details = (string)helper.Invoke(null, new object[] { tip, error, ScriptRun.FailureLines(transcript), ShownLog });
            else  // as RunSettings built it before FailureDetails
                details = (tip != null ? tip + "\r\n\r\n" : "") + (error != null ? error + "\r\n\r\n" : "")
                          + string.Join("\r\n", raw.Skip(Math.Max(0, raw.Count - 30))) + "\r\n\r\nLog: " + ShownLog;
            foreach (int dpi in new[] { 96, 144 })
            {
                var fields = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>(Lang.T("Log"), ShownLog) };
                using (var d = new SetupResult(Lang.T("QL Printer Setup"), ResultKind.Error, Lang.T("The change did not finish"), fields, reason, details)
                               { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) })
                {
                    d.Show();
                    Pump(800);
                    if (d.DeviceDpi != dpi)
                    {
                        var r = new RECT { L = d.Left, T = d.Top, R = d.Left + d.Width * dpi / d.DeviceDpi, B = d.Top + d.Height * dpi / d.DeviceDpi };
                        SendMessage(d.Handle, 0x02E0, (IntPtr)(dpi | dpi << 16), ref r);
                        Pump(1200);
                    }
                    string p = tag + "-" + lang + "-" + dpi + "-";
                    Shot(d, outDir, p + "1-result");
                    var show = Descendants(d).OfType<LinkLabel>().First(l => l.Text == Lang.T("Show details"));
                    var box = Descendants(d).OfType<TextBox>().First();
                    box.Visible = true; show.Text = Lang.T("Hide details");
                    Pump(800);
                    Shot(d, outDir, p + "2-details");
                    Console.WriteLine("INFO " + p + "window " + d.Size + " (limit 690 px high at 100 %)");
                    d.Close();
                }
            }
            Console.WriteLine("DONE " + tag + " " + lang);
            return 0;
        }

        static IEnumerable<Control> Descendants(Control c)
        {
            foreach (Control x in c.Controls) { yield return x; foreach (var y in Descendants(x)) yield return y; }
        }

        static void Shot(Form f, string outDir, string name)
        {
            using (var b = new Bitmap(f.Width, f.Height))
            {
                using (var g = Graphics.FromImage(b)) { var dc = g.GetHdc(); PrintWindow(f.Handle, dc, 2); g.ReleaseHdc(dc); }
                b.Save(Path.Combine(outDir, name + ".png"), ImageFormat.Png);
            }
        }

        static void Pump(int ms)
        {
            var end = DateTime.Now.AddMilliseconds(ms);
            while (DateTime.Now < end) { Application.DoEvents(); Thread.Sleep(15); }
        }
    }
}
