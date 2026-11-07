<#
.SYNOPSIS
  Read-only probe of a QL printer's status over a printer share (\\host\share Local Port), for testing the status between two PCs.
  Changes nothing, prints nothing, opens no window.

.DESCRIPTION
  Every -Every seconds, one block with a timestamp:
    card   what the installed programs' own read path makes of it: PublishedStatus.QueryShare (via reflection from
           QlPrinterStatus.exe) -> DeviceState, Error, Stale, PublishedAt, reply. The share check's 30 s cache is cleared
           before each call (-KeepCache: the program's real timing).
    net    NetShareGetInfo(host, share, 1): error and share type (1 = print queue)
    queue  OpenPrinter(\\host\share) + PRINTER_INFO_2: Attributes (SHARED 0x8), Status (PENDING_DELETION 0x4), pShareName
    pub    the published value QLUnidrv\Status through the share: header, poll time, newest entry
    local  the local queue: status, jobs
  Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\status-probe.ps1 [-Printer "Brother QL-800 (Unidrv)"]
         [-Every 5] [-Count 0 (= until Ctrl+C)] [-Log <file>] [-KeepCache]
#>
param(
    [string]$Printer = "Brother QL-800 (Unidrv)",
    [int]$Every = 5,
    [int]$Count = 0,
    [string]$Log,
    [switch]$KeepCache,
    [string]$Exe = "C:\Program Files\QLUnidrv\QlPrinterStatus.exe"
)
$ErrorActionPreference = "Continue"
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class QlProbe {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PD { public string dt; public IntPtr dm; public int access; }
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref PD d);
  [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetPrinterW(IntPtr h, int level, IntPtr buf, int size, out int needed);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode)] static extern int GetPrinterDataExW(IntPtr h, string key, string value, out int type, byte[] data, int size, out int needed);
  [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetShareGetInfo(string s, string n, int l, out IntPtr b);
  [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr b);
  public static string Net(string host, string share) {
    IntPtr b; int e = NetShareGetInfo(host, share, 1, out b); int type = -1;
    if (e == 0 && b != IntPtr.Zero) type = Marshal.ReadInt32(b, IntPtr.Size);
    if (b != IntPtr.Zero) NetApiBufferFree(b);
    return "error " + e + (e == 0 ? " type " + type + (type == 1 ? " (print queue)" : "") : "");
  }
  public static string Queue(string q, out string published) {
    published = null; IntPtr h; PD d = new PD(); d.access = 8;   // PRINTER_ACCESS_USE
    if (!OpenPrinterW(q, out h, ref d)) return "OpenPrinter error " + Marshal.GetLastWin32Error();
    try {
      string info; int need;
      GetPrinterW(h, 2, IntPtr.Zero, 0, out need);
      if (need <= 0) info = "GetPrinter error " + Marshal.GetLastWin32Error();
      else {
        IntPtr buf = Marshal.AllocHGlobal(need);
        try {
          if (!GetPrinterW(h, 2, buf, need, out need)) info = "GetPrinter error " + Marshal.GetLastWin32Error();
          else {
            int p = IntPtr.Size;
            string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, p)), share = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, 2 * p));
            string port = Marshal.PtrToStringUni(Marshal.ReadIntPtr(buf, 3 * p));
            int attr = Marshal.ReadInt32(buf, 13 * p), st = Marshal.ReadInt32(buf, 13 * p + 20), jobs = Marshal.ReadInt32(buf, 13 * p + 24);
            info = string.Format("open OK; '{0}' share '{1}' port {2} attributes 0x{3:X} (SHARED {4}) status 0x{5:X}{6} jobs {7}",
              name, share, port, attr, (attr & 8) != 0 ? "yes" : "NO", st, (st & 4) != 0 ? " PENDING_DELETION" : "", jobs);
          }
        } finally { Marshal.FreeHGlobal(buf); }
      }
      int type, n; GetPrinterDataExW(h, "QLUnidrv", "Status", out type, null, 0, out n);
      if (n > 0) {
        byte[] data = new byte[n]; int e = GetPrinterDataExW(h, "QLUnidrv", "Status", out type, data, n, out n);
        published = e == 0 ? System.Text.Encoding.Unicode.GetString(data, 0, Math.Max(0, n - 2)) : "read error " + e;
      } else published = "(no QLUnidrv\\Status value)";
      return info;
    } finally { ClosePrinter(h); }
  }
}
'@

$local = Get-Printer -Name $Printer -ErrorAction Stop
$port = $local.PortName
if ($port -notmatch '^\\\\([^\\]+)\\([^\\]+)$') { throw "'$Printer' is on port '$port', not on a \\host\share port." }
$hostName = $Matches[1]; $shareName = $Matches[2]

# The program's own read path (internal classes, read only)
$ps = $null; $query = $null; $forget = $null
try {
    $asm = [Reflection.Assembly]::LoadFrom($Exe)
    $ps = $asm.GetTypes() | Where-Object { $_.Name -eq "PublishedStatus" } | Select-Object -First 1
    $flags = [Reflection.BindingFlags]"Static,Public,NonPublic"
    $query = $ps.GetMethod("QueryShare", $flags)
    $forget = $ps.GetMethod("ForgetShareChecks", $flags)
} catch { }
$version = (Get-Item $Exe -ErrorAction SilentlyContinue).VersionInfo.ProductVersion

function Card {
    if (-not $query) { return "card  (QueryShare not found in $Exe)" }
    try {
        if (-not $KeepCache -and $forget) { [void]$forget.Invoke($null, $null) }
        $r = $query.Invoke($null, @($Printer, $port, $hostName, $shareName))
        $reply = if ($r.Reply) { "reply yes" } else { "reply none" }
        "card  State {0}; Stale {1}; PublishedAt {2}; {3}; messages {4}; Error '{5}'" -f $r.State, $r.Stale, $(if ($r.PublishedAt) { ([datetime]$r.PublishedAt).ToString("HH:mm:ss") } else { "-" }), $reply, $r.Messages.Count, $r.Error
    } catch { "card  ERROR $($_.Exception.InnerException.Message) $($_.Exception.Message)" }
}

function Out-Line([string]$s) { $s; if ($Log) { Add-Content -LiteralPath $Log -Value $s -Encoding UTF8 } }
Out-Line "status-probe: '$Printer' on $port (host $hostName, share $shareName); programs $version; every $Every s; cache $(if ($KeepCache) { 'kept (30 s)' } else { 'cleared' })"
$i = 0
while ($true) {
    $i++
    $now = Get-Date
    Out-Line ("=== {0} (UTC {1})" -f $now.ToString("HH:mm:ss"), $now.ToUniversalTime().ToString("HH:mm:ss"))
    Out-Line ("  " + (Card))
    Out-Line ("  net   " + [QlProbe]::Net($hostName, $shareName))
    $pub = $null
    Out-Line ("  queue " + [QlProbe]::Queue("\\$hostName\$shareName", [ref]$pub))
    if ($pub) {
        $lines = @($pub -split "`r?`n" | Where-Object { $_ })
        $poll = $lines | Where-Object { $_ -match '^poll\t' } | Select-Object -First 1
        $last = $lines | Where-Object { $_ -match '^\d{4}-' } | Select-Object -Last 1
        Out-Line ("  pub   {0}; {1}; newest: {2}" -f $lines[0], $(if ($poll) { $poll -replace "`t", " " } else { "no poll line" }), $(if ($last) { $last -replace "`t", " " } else { "-" }))
    } else { Out-Line "  pub   (not read: queue not opened)" }
    $q = Get-Printer -Name $Printer -ErrorAction SilentlyContinue
    $jobs = @(Get-PrintJob -PrinterName $Printer -ErrorAction SilentlyContinue)
    Out-Line ("  local status {0}; jobs {1} {2}" -f $q.PrinterStatus, $jobs.Count, (($jobs | ForEach-Object { "#$($_.Id) $($_.JobStatus)" }) -join ", "))
    if ($Count -gt 0 -and $i -ge $Count) { break }
    Start-Sleep -Seconds $Every
}
