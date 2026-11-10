// Start-up probe: what the first font use costs in a fresh process (time, and the process's read operations and bytes
// from GetProcessIoCounters, which count file-cache hits too). Steps in the order given; each prints its delta.
//   font_probe <step> [<step> ...]     steps: styles, gdi (CreateFont + GetTextExtentPoint32, no GDI+),
//                                      gdiplus (GdiplusStartup only), family (new FontFamily("Segoe UI")),
//                                      sysfont (SystemFonts.MessageBoxFont), control (new Label(): Control.DefaultFont),
//                                      families (FontFamily.Families: every installed family)
// Opens no window. Run through deskrun (tools\uitest): deskrun --machine arm64 font_probe.exe gdi family control
using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels.Tests
{
    static class FontProbe
    {
        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process, out IO_COUNTERS counters);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight, uint italic, uint underline, uint strike, uint charset, uint outPrecision, uint clipPrecision, uint quality, uint pitch, string face);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool GetTextExtentPoint32W(IntPtr dc, string s, int n, out Size size);
        [DllImport("gdiplus.dll")] static extern int GdiplusStartup(out IntPtr token, ref StartupInput input, IntPtr output);
        [StructLayout(LayoutKind.Sequential)] struct StartupInput { public int Version; public IntPtr Callback; public bool NoThread; public bool NoCodecs; }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode)] static extern int RegCreateKeyExW(IntPtr key, string sub, int reserved, string cls, uint options, uint sam, IntPtr security, out IntPtr result, out uint disposition);
        [DllImport("advapi32.dll")] static extern int RegOverridePredefKey(IntPtr key, IntPtr newKey);
        static Font privateFont;

        [STAThread]
        static int Main(string[] args)
        {
            var clock = Stopwatch.StartNew();
            IO_COUNTERS last; GetProcessIoCounters(Process.GetCurrentProcess().Handle, out last);
            Console.WriteLine("start: {0} ms after process start", (DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds.ToString("0"));
            foreach (var step in args)
            {
                long t0 = clock.ElapsedMilliseconds;
                string note = "";
                switch (step)
                {
                    case "styles": Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false); break;
                    case "gdi":
                        {
                            IntPtr dc = CreateCompatibleDC(IntPtr.Zero), f = CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 0, 0, "Segoe UI");
                            SelectObject(dc, f); Size s; GetTextExtentPoint32W(dc, "Regal 3", 7, out s); note = "extent " + s.Width;
                            break;
                        }
                    case "gdiplus": { IntPtr token; var input = new StartupInput { Version = 1 }; note = "status " + GdiplusStartup(out token, ref input, IntPtr.Zero); break; }
                    case "family": using (var fam = new FontFamily("Segoe UI")) note = fam.Name; break;
                    case "sysfont": note = SystemFonts.MessageBoxFont.Name; break;
                    case "control": using (var l = new Label()) note = l.Font.Name; break;
                    case "families": note = FontFamily.Families.Length + " families"; break;
                    case "nouser":
                        {
                            // This process's HKEY_CURRENT_USER points at an empty volatile key: GDI+ sees no per-user fonts
                            IntPtr key; uint disp;
                            int rc = RegCreateKeyExW(new IntPtr(unchecked((int)0x80000001)), @"Software\QLUnidrv\FontProbeEmpty", 0, null, 1 /* REG_OPTION_VOLATILE */,
                                                     0xF003F, IntPtr.Zero, out key, out disp);
                            note = "create " + rc + ", override " + RegOverridePredefKey(new IntPtr(unchecked((int)0x80000001)), key);
                            break;
                        }
                    case "private":
                        {
                            // A font from a private collection (one file): does it load the installed table?
                            var pfc = new System.Drawing.Text.PrivateFontCollection();
                            pfc.AddFontFile(Environment.ExpandEnvironmentVariables(@"%WINDIR%\Fonts\segoeui.ttf"));
                            privateFont = new Font(pfc.Families[0], 9f);
                            note = privateFont.Name + " " + privateFont.GetHeight().ToString("0.0");
                            break;
                        }
                    case "defaultfont":
                        {
                            // WinForms' Control.DefaultFont set to the private font (reflection), then a control and a measure
                            typeof(Control).GetField("defaultFont", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, privateFont);
                            using (var l = new Label { Text = "Regal 3" })
                                note = l.Font.Name + " " + TextRenderer.MeasureText(l.Text, l.Font).Width + " px";
                            break;
                        }
                    default: Console.WriteLine("unknown step " + step); return 2;
                }
                IO_COUNTERS now; GetProcessIoCounters(Process.GetCurrentProcess().Handle, out now);
                Console.WriteLine("{0,-9} {1,6} ms  reads {2,6}  {3,8:0.0} MB  {4}", step, clock.ElapsedMilliseconds - t0,
                                  now.ReadOps - last.ReadOps, (now.ReadBytes - last.ReadBytes) / 1048576.0, note);
                last = now;
            }
            return 0;
        }
    }
}
