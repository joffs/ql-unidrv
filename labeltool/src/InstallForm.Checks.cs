// Setup window (InstallForm.cs), part: what it asks the PC: the release folder and zip view, the architecture, the
// special start (code integrity), a running setup, Smart App Control / protected print mode, BitLocker, open QL
// windows, shares and the status service. No UI; the views decide what to do with the answers.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    partial class InstallForm
    {
        // ---------- the release folder ----------

        // The folder with out\package and driver\: given, the exe's own (Setup.exe in the zip's root) or two up
        // (out\tools\QlLabelFormats.exe in the zip).
        static string FindRoot(string given)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            // the release zip: Setup.exe at the top, the rest in advanced\; the source tree; out\tools\ in either
            foreach (var dir in new[] { given, Path.Combine(exeDir, "advanced"), exeDir, Path.GetFullPath(Path.Combine(exeDir, @"..\..")) })
                if (dir != null && File.Exists(Path.Combine(dir, @"out\package\qlunidrv.inf")) && File.Exists(Path.Combine(dir, @"driver\install.ps1")))
                    return Path.GetFullPath(dir);
            return null;
        }

        // Windows' zip view runs a program from a temporary copy (Windows 11 24H2: of the whole zip) under %TEMP%:
        // ...\Temp\Temp1_name.zip\Setup.exe, or ...\Temp\<GUID>_name.zip.ba6\name\Setup.exe (x64 Sandbox 2026-10-27).
        static bool StartedFromZip() { return IsZipView(LongPath(Path.GetDirectoryName(Application.ExecutablePath)), LongPath(Path.GetTempPath())); }

        internal static bool IsZipView(string dir, string temp)
        {
            return dir.StartsWith(temp.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(dir, @"\.zip(\.[0-9a-z]{3})?(\\|$)", RegexOptions.IgnoreCase);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int GetLongPathNameW(string shortPath, StringBuilder longPath, int size);

        // %TEMP% can be an 8.3 path (C:\Users\WDAGUT~1\...) while the exe's path is the long one.
        static string LongPath(string path)
        {
            var sb = new StringBuilder(1024);
            int n = GetLongPathNameW(path, sb, sb.Capacity);
            return n > 0 && n < sb.Capacity ? sb.ToString() : path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFileW(string path);

        // Removes the download mark (Mark of the Web) from the extracted files, as Properties > Unblock would.
        static void Unblock(string dir)
        {
            if (Preview) return;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) DeleteFileW(f + ":Zone.Identifier");
            }
            catch (Exception) { }
        }

        internal static string ZipVersion(string dir)
        {
            try
            {
                var m = File.ReadAllLines(Path.Combine(dir, @"out\package\qlunidrv.inf")).Select(l => Regex.Match(l, @"^DriverVer=[^,]+,\s*([\d.]+)")).FirstOrDefault(x => x.Success);
                return m != null ? m.Groups[1].Value : null;
            }
            catch (Exception) { return null; }
        }

        // The PC's own architecture, not this process's (NativeTools)
        static bool IsArm64() { return NativeTools.IsArm64(); }

        // ---------- ARM64: the special start ----------

        [StructLayout(LayoutKind.Sequential)]
        struct CodeIntegrityInformation { public uint Length, Options; }

        [DllImport("ntdll.dll")]
        static extern int NtQuerySystemInformation(int infoClass, ref CodeIntegrityInformation info, int length, out int returned);

        // Does this start let self-signed drivers in? Asked without admin rights: the code integrity options
        // (SystemCodeIntegrityInformation = 103; normal start on the ARM64 dev PC 2026-10-30: 0xC001) and the start
        // options Windows records for this boot (Control\SystemStartOptions).
        static bool SignatureChecksOff()
        {
            uint options = 1;
            try
            {
                var info = new CodeIntegrityInformation { Length = 8 };
                int returned;
                if (NtQuerySystemInformation(103, ref info, 8, out returned) == 0) options = info.Options;
            }
            catch (Exception) { }
            string start = null;
            try { using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control")) start = k == null ? null : k.GetValue("SystemStartOptions") as string; }
            catch (Exception) { }
            return SignatureChecksOff(options, start);
        }

        // Off when code integrity is off (CODEINTEGRITY_OPTION_ENABLED 0x1 clear) or in test mode (TESTSIGN 0x2), or the
        // boot loader passed DISABLE_INTEGRITY_CHECKS (key 7) or TESTSIGNING. Which of these the special start shows
        // on ARM64 is not measured yet: any of them counts.
        internal static bool SignatureChecksOff(uint options, string startOptions)
        {
            if ((options & 0x1) == 0 || (options & 0x2) != 0) return true;
            return startOptions != null && Regex.IsMatch(startOptions, @"\b(DISABLE_INTEGRITY_CHECKS|TESTSIGNING)\b", RegexOptions.IgnoreCase);
        }

        // install.ps1 holds this while it runs (one run at a time, debug pass 2026-10-31 I4). Made by an elevated
        // process, it may refuse this one access: that also means it exists.
        static bool SetupRunning()
        {
            try
            {
                System.Threading.Mutex m;
                if (!System.Threading.Mutex.TryOpenExisting(@"Global\QLUnidrvInstall", out m)) return false;
                m.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException) { return true; }
            catch (Exception) { return false; }
        }

        // Windows settings that keep this driver out (debug pass 2026-10-31 I11): Smart App Control on (blocks programs
        // without a paid signature, the label tool and probably the driver's plug-in) and Windows protected print mode
        // (only Windows' own printer drivers). Not measured on a PC with either on: the documented registry values.
        static string BlockingPolicy()
        {
            object sac = null, wppPolicy = null, wppMode = null;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\CI\Policy")) if (k != null) sac = k.GetValue("VerifiedAndReputablePolicyState");
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows NT\Printers\WPP"))
                    if (k != null) { wppPolicy = k.GetValue("WindowsProtectedPrintGroupPolicyState"); wppMode = k.GetValue("WindowsProtectedPrintMode"); }
            }
            catch (Exception) { }
            return BlockingPolicy(sac, wppPolicy, wppMode);
        }

        internal static string BlockingPolicy(object sac, object wppPolicy, object wppMode)
        {
            Func<object, int> num = v => v is int ? (int)v : -1;
            if (num(sac) == 1)
                return Lang.T("Smart App Control is on. It blocks programs without a paid signature, so this driver and its programs can't run here. " +
                              "(Windows Security > App & browser control > Smart App Control; once off, Windows can turn it on again only with a reset.)");
            if (num(wppPolicy) == 1 || num(wppMode) == 1)
                return Lang.T("Windows protected print mode is on. It allows only Windows' own printer drivers, so this driver can't be installed here. " +
                              "Your organisation or you can turn it off in Settings > Bluetooth & devices > Printers & scanners.");
            return null;
        }

        // BitLocker (or device encryption) on the system drive: the restart with key 7 may ask for its recovery key.
        // Shell property System.Volume.BitLockerProtection, readable without admin rights: 1 = on, 3 = encrypting.
        static bool SystemDriveEncrypted()
        {
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application");
                object shell = Activator.CreateInstance(type);
                object computer = type.InvokeMember("NameSpace", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { 17 });  // This PC
                string drive = Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\');
                object item = computer.GetType().InvokeMember("ParseName", System.Reflection.BindingFlags.InvokeMethod, null, computer, new object[] { drive });
                object v = item.GetType().InvokeMember("ExtendedProperty", System.Reflection.BindingFlags.InvokeMethod, null, item, new object[] { "System.Volume.BitLockerProtection" });
                int state = v == null ? 0 : Convert.ToInt32(v);
                return state == 1 || state == 3;
            }
            catch (Exception) { return false; }
        }

        // The open window that may hold unsaved changes: the label tool's ("QL Label Formats") or the label editor's
        // ("<label> – QL Label Maker", QlLabelMaker.exe); null when none. The monitor and status windows close themselves.
        static string LabelToolOpen()
        {
            int me = Process.GetCurrentProcess().Id;
            foreach (var p in new[] { "QlLabelFormats", "QlLabelMaker" }.SelectMany(Process.GetProcessesByName))
            {
                try
                {
                    if (p.Id == me) continue;
                    string title = p.MainWindowTitle;
                    if (title.StartsWith(MainForm.AppTitle, StringComparison.Ordinal)) return MainForm.AppTitle;
                    if (title.EndsWith(EditorForm.AppTitle, StringComparison.Ordinal)) return EditorForm.AppTitle;
                }
                catch (Exception) { }
            }
            return null;
        }

        internal static bool IsShared(string printer)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + printer))
                    return k != null && (Convert.ToInt32(k.GetValue("Attributes", 0)) & 0x8) != 0;  // PRINTER_ATTRIBUTE_SHARED
            }
            catch (Exception) { return false; }
        }

        static bool ServiceInstalled()
        {
            try { using (var sc = new ServiceController(PublishedStatus.ServiceName)) return sc.Status >= 0; }  // throws when not installed
            catch (Exception) { return false; }
        }
    }
}
