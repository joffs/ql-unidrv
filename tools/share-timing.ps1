<#
.SYNOPSIS
  Read-only timing probe for a printer share as the status programs reach it: how long each step to \\host\share takes.
  Changes nothing, prints nothing.

.DESCRIPTION
  Every -Every seconds one line: time, TCP connect to port 445 (ms or "fail"), NetShareGetInfo (ms, error),
  OpenPrinter + GetPrinter level 2 (ms, error), GetPrinterDataEx QLUnidrv\Status (ms, error). Each step runs on its own
  with no deadline, so slow answers show their real length. Lines where a step took over -Slow ms are marked "SLOW".
  Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\share-timing.ps1 -HostName OTHERPC -Share QL-570
         [-Every 3] [-Minutes 10] [-Slow 1500] [-Log out\status-share-matrix\timing.log]
#>
param(
    [Parameter(Mandatory = $true)][string]$HostName,
    [Parameter(Mandatory = $true)][string]$Share,
    [int]$Every = 3,
    [int]$Minutes = 10,
    [int]$Slow = 1500,
    [string]$Log
)
if (-not $Log) {
    $Log = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) ("..\out\status-share-matrix\timing-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))
}
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Log) | Out-Null

Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices;
public static class QlTiming {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct PD { public string dt; public IntPtr dm; public int access; }
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref PD d);
  [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool GetPrinterW(IntPtr h, int level, IntPtr buf, int size, out int needed);
  [DllImport("winspool.drv", CharSet = CharSet.Unicode)] static extern int GetPrinterDataExW(IntPtr h, string key, string value, out int type, byte[] data, int size, out int needed);
  [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] static extern int NetShareGetInfo(string s, string n, int l, out IntPtr b);
  [DllImport("netapi32.dll")] static extern int NetApiBufferFree(IntPtr b);
  public static int Net(string host, string share) {
    IntPtr b; int e = NetShareGetInfo(host, share, 1, out b);
    if (b != IntPtr.Zero) NetApiBufferFree(b);
    return e;
  }
  public static int Queue(string name) {
    var d = new PD { access = 8 }; IntPtr h;
    if (!OpenPrinterW(name, out h, ref d)) return Marshal.GetLastWin32Error();
    try {
      int need; GetPrinterW(h, 2, IntPtr.Zero, 0, out need);
      if (need <= 0) return Marshal.GetLastWin32Error();
      IntPtr buf = Marshal.AllocHGlobal(need);
      try { return GetPrinterW(h, 2, buf, need, out need) ? 0 : Marshal.GetLastWin32Error(); }
      finally { Marshal.FreeHGlobal(buf); }
    } finally { ClosePrinter(h); }
  }
  public static int Data(string name) {
    var d = new PD { access = 8 }; IntPtr h;
    if (!OpenPrinterW(name, out h, ref d)) return Marshal.GetLastWin32Error();
    try { int t, need; var data = new byte[65536]; return GetPrinterDataExW(h, "QLUnidrv", "Status", out t, data, data.Length, out need); }
    finally { ClosePrinter(h); }
  }
}
'@

$queue = "\\$HostName\$Share"
$end = (Get-Date).AddMinutes($Minutes)
"time`ttcp445`tnetshare`tqueue`tdata`tnote  ($queue, every $Every s)" | Out-File -FilePath $Log -Encoding utf8
while ((Get-Date) -lt $end) {
    $t0 = Get-Date
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $tcp = New-Object System.Net.Sockets.TcpClient
    try { $ok = $tcp.ConnectAsync($HostName, 445).Wait(10000) } catch { $ok = $false }
    $tcp.Close()
    $tTcp = $sw.ElapsedMilliseconds
    $sw.Restart(); $eNet = [QlTiming]::Net($HostName, $Share); $tNet = $sw.ElapsedMilliseconds
    $sw.Restart(); $eQ = [QlTiming]::Queue($queue); $tQ = $sw.ElapsedMilliseconds
    $sw.Restart(); $eD = [QlTiming]::Data($queue); $tD = $sw.ElapsedMilliseconds
    $mark = if (-not $ok -or ($tTcp, $tNet, $tQ, $tD | Where-Object { $_ -gt $Slow })) { "SLOW" } else { "" }
    $tcpText = if ($ok) { "$tTcp" } else { "fail $tTcp" }
    "{0}`t{1}`t{2} e{3}`t{4} e{5}`t{6} e{7}`t{8}" -f $t0.ToString("HH:mm:ss"), $tcpText, $tNet, $eNet, $tQ, $eQ, $tD, $eD, $mark |
        Out-File -FilePath $Log -Append -Encoding utf8
    $wait = $Every * 1000 - ((Get-Date) - $t0).TotalMilliseconds
    if ($wait -gt 0) { [Threading.Thread]::Sleep([int]$wait) }
}
"done" | Out-File -FilePath $Log -Append -Encoding utf8
Write-Output $Log
