// The version numbers the user sees (setup window, Help > Info). The programs: labeltool\version\VERSION is the release
// (major.minor.patch: 0.18.1; an app-only release raises the patch, a release with a driver change the minor), the
// build stamp is days since 2026 and minute of day (305.700); file properties show 0.18.1.305 (Windows allows 16 bits
// per part), the product version "0.18.1 (build 305.700)" (labeltool\version\make-version.ps1). The driver:
// driver\VERSION, the release in which it last changed (always x.y.0, shown as "0.18"), in its DriverVer with that
// change's stamp (0.18.304.1114, driver\gen\version.py). A driver is "the same" when the first two parts match; the
// stamp only orders builds of one version for Windows.
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace QlLabels
{
    static class Versions
    {
        // This exe's product version, "0.18.1 (build 305.700)"
        public static string Product
        {
            get
            {
                var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(typeof(Versions).Assembly, typeof(AssemblyInformationalVersionAttribute));
                return a != null ? a.InformationalVersion : null;
            }
        }

        // "0.18.1": the programs' release
        public static string App { get { return AppRelease(Product); } }

        // "305.700": the build stamp
        public static string Build
        {
            get
            {
                var m = Regex.Match(Product ?? "", @"\(build (\d+\.\d+)\)$");
                return m.Success ? m.Groups[1].Value : null;
            }
        }

        // The release in a program's product version: "0.18.1 (build 305.700)" -> "0.18.1"; a build from before the
        // split (2026-11-01, product version = file version "0.18.304.1157") -> "0.18". null for none.
        public static string AppRelease(string product)
        {
            if (product == null) return null;
            var m = Regex.Match(product, @"^(\d+\.\d+\.\d+) \(build \d+\.\d+\)$");
            return m.Success ? m.Groups[1].Value : Release(product);
        }

        // Programs ordered by release, then build; a build from before the split counts as x.y.0 without a stamp
        static Version AppOrder(string product)
        {
            if (product == null) return null;
            var m = Regex.Match(product, @"^(\d+)\.(\d+)\.(\d+) \(build (\d+)\.(\d+)\)$");
            if (m.Success)
                return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value),
                                   int.Parse(m.Groups[4].Value) * 1440 + int.Parse(m.Groups[5].Value));
            Version v;
            return Version.TryParse(product, out v) ? new Version(v.Major, v.Minor, 0, 0) : null;
        }

        // A driver's release: "0.18.304.1114" -> "0.18"; null for null or no version
        public static string Release(string version)
        {
            Version v;
            return version != null && Version.TryParse(version, out v) ? v.Major + "." + v.Minor : null;
        }

        // "0.18.304.1114" -> "304.1114"; null without the last two parts
        public static string Stamp(string version)
        {
            Version v;
            return version != null && Version.TryParse(version, out v) && v.Build >= 0 && v.Revision >= 0 ? v.Build + "." + v.Revision : null;
        }

        // Drivers compared by their release (a null one is older than any)
        public static int CompareRelease(string a, string b)
        {
            Version va, vb;
            bool ha = Version.TryParse(Release(a) ?? "", out va), hb = Version.TryParse(Release(b) ?? "", out vb);
            return ha && hb ? va.CompareTo(vb) : ha.CompareTo(hb);
        }

        // Programs compared by release and build (a null one is older than any)
        public static int CompareApps(string a, string b)
        {
            Version va = AppOrder(a), vb = AppOrder(b);
            return va != null && vb != null ? va.CompareTo(vb) : (va != null).CompareTo(vb != null);
        }

        // The installed driver's DriverVer ("0.18.304.1114"), as the spooler has it; null when none of our drivers is
        // installed.
        public static string InstalledDriver()
        {
            try
            {
                foreach (string env in new[] { "Windows ARM64", "Windows x64" })
                    foreach (string name in QlModel.DriverNames)
                        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Environments\" + env + @"\Drivers\Version-3\" + name))
                        {
                            if (k == null) continue;
                            object v = k.GetValue("DriverVersion");
                            if (v is long)
                            {
                                ulong q = (ulong)(long)v;
                                return string.Format("{0}.{1}.{2}.{3}", q >> 48, (q >> 32) & 0xFFFF, (q >> 16) & 0xFFFF, q & 0xFFFF);
                            }
                            return Convert.ToString(v);
                        }
            }
            catch (Exception) { }
            return null;
        }

        // The installed programs' product version (QL Label Formats in Program Files); null when they aren't installed
        public static string InstalledApps()
        {
            try
            {
                string exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"QLUnidrv\QlLabelFormats.exe");
                return File.Exists(exe) ? FileVersionInfo.GetVersionInfo(exe).ProductVersion : null;
            }
            catch (Exception) { return null; }
        }
    }
}
