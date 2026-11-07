<#
.SYNOPSIS
  Run elevated once (x64 PC; on ARM64 only inside an F7 window): updates our driver package with
  install.ps1 -Update (-SkipInstall: not, e.g. after an install with Setup.exe), then adds one capture printer per
  model on a file port, so compare.ps1 can check the real Unidrv output against Brother's captures:

    .\driver\compare.ps1 -Printer "QL-560 Capture" -Captures ql560     (no admin)

  Printers: "QL-500 Capture", "QL-550 Capture", "QL-560 Capture", "QL-570 Capture", "QL-580N Capture" (0.14), "QL-650TD Capture",
  "QL-700 Capture", "QL-710W Capture", "QL-720NW Capture" (0.12), "QL-1050 Capture", "QL-1060N Capture" (0.13),
  "QL-600 Capture", "QL-1100 Capture", "QL-1110NWB Capture", "QL-1115NWB Capture" (0.15) on
  C:\ProgramData\QLUnidrv\capture\<model>.prn, and "QL-800 Capture" on ...\capture\capture.prn (the same printer as
  install.ps1 -TestPrinter), all with our "(Unidrv)" drivers.
  -Models 800,1115NWB: only these models (default: all of them).
  The capture folder gets the rights install.ps1 -TestPrinter gives it: users may create and change files there (the
  spooler writes a file port as the user who printed), but not create folders.
  Transcript: out\capture-setup\models-<timestamp>.txt
#>
param([switch]$SkipInstall, [string[]]$Models)
$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot)
$folder = "C:\ProgramData\QLUnidrv\capture"
$log = Join-Path $repo ("out\capture-setup\models-{0:yyyyMMdd-HHmm}.txt" -f (Get-Date))
New-Item -ItemType Directory -Force (Split-Path $log) | Out-Null
Start-Transcript -Path $log -Force | Out-Null

# The capture folder as install.ps1's Set-UserFilesFolder makes it: no link, no subfolders, users (S-1-5-32-545) may
# create files and change any file below, but not create folders
function Set-CaptureFolder {
    $item = Get-Item -LiteralPath $folder -Force -ErrorAction SilentlyContinue
    if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "$folder is a link to another place: remove it (only the link), then run this again." }
    if (-not $item) { New-Item -ItemType Directory -Force $folder | Out-Null }
    if (Get-ChildItem -LiteralPath $folder -Force -Directory) { throw "$folder has subfolders, which nobody should have made there: remove them, then run this again." }
    icacls $folder /reset | Out-Null
    icacls $folder /grant "*S-1-5-32-545:(RX,WD)" "*S-1-5-32-545:(OI)(IO)(M)" | Out-Null
}

# Adds a file port (as install.ps1's Add-LocalPort): Windows checks the path when a file port is added, so the folder
# must exist (generic CIM error otherwise, x64 Sandbox 2026-11-01); a failure is tried twice more, then through the
# Local Port monitor's XcvData "AddPort" (the spooler's port dialog way).
function Add-FilePort([string]$port) {
    if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { return }
    if (-not (Test-Path -LiteralPath (Split-Path $port) -PathType Container)) { throw "Could not add the port ${port}: its folder does not exist." }
    $err = $null
    for ($i = 0; $i -lt 3; $i++) {
        if ($i) { Start-Sleep -Seconds 2 }
        try { Add-PrinterPort -Name $port -ErrorAction Stop; return } catch { $err = $_.Exception.Message }
        if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { return }
    }
    if (-not ("QlXcvAdd" -as [type])) {
        Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class QlXcvAdd {
    [StructLayout(LayoutKind.Sequential)] struct Defaults { public IntPtr DataType, DevMode; public int Access; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref Defaults d);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool XcvDataW(IntPtr h, string name, byte[] input, int cbInput, IntPtr output, int cbOutput, out int needed, out int status);
    public static int AddPort(string port) {
        var d = new Defaults { Access = 1 };   // SERVER_ACCESS_ADMINISTER
        IntPtr h;
        if (!OpenPrinterW(",XcvMonitor Local Port", out h, ref d)) return Marshal.GetLastWin32Error();
        try {
            byte[] input = Encoding.Unicode.GetBytes(port + "\0");
            int needed, status;
            if (!XcvDataW(h, "AddPort", input, input.Length, IntPtr.Zero, 0, out needed, out status)) return Marshal.GetLastWin32Error();
            return status;
        } finally { ClosePrinter(h); }
    }
}
"@
    }
    $status = [QlXcvAdd]::AddPort($port)
    if (($status -eq 0 -or $status -eq 183) -and (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {
        "Port $port added through the port monitor (Add-PrinterPort failed: $err)."
        return
    }
    throw "Could not add the port ${port}. Add-PrinterPort: $err Port monitor (XcvData AddPort): $((New-Object ComponentModel.Win32Exception $status).Message)"
}

try {
    if (-not $SkipInstall) {
        "=== install.ps1 -Update"
        & (Join-Path $repo "driver\install.ps1") -Update
    }
    $all = "500", "550", "560", "570", "580N", "600", "650TD", "700", "710W", "720NW", "800", "1050", "1060N", "1100", "1110NWB", "1115NWB"
    # powershell -File passes "-Models 800,1115NWB" as one string
    $want = if ($Models) { @($Models -split "," | ForEach-Object { $_.Trim() -replace "^QL-", "" }) } else { $all }
    $unknown = @($want | Where-Object { $all -notcontains $_ })
    if ($unknown) { throw "Unknown model(s): $($unknown -join ', '). One of: $($all -join ', ')." }
    Set-CaptureFolder
    foreach ($m in $all | Where-Object { $want -contains $_ }) {
        $driver = "Brother QL-$m (Unidrv)"; $printer = "QL-$m Capture"
        # The QL-800's port is install.ps1 -TestPrinter's (compare.ps1's default printer)
        $port = Join-Path $folder $(if ($m -eq "800") { "capture.prn" } else { "ql$($m.ToLower()).prn" })
        "=== $printer ($driver, $port)"
        if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) { Add-PrinterDriver -Name $driver }
        Add-FilePort $port
        if (-not (Get-Printer -Name $printer -ErrorAction SilentlyContinue)) { Add-Printer -Name $printer -DriverName $driver -PortName $port }
    }
    "=== result"
    Get-Printer | Where-Object Name -like "QL-* Capture" | Format-Table Name, DriverName, PortName -AutoSize | Out-String -Width 200
    Get-PrinterDriver | Where-Object Name -like "* (Unidrv)" | Format-Table Name, @{ n = "Version"; e = { $v = $_.DriverVersion; "{0}.{1}.{2}.{3}" -f ($v -shr 48), (($v -shr 32) -band 0xFFFF), (($v -shr 16) -band 0xFFFF), ($v -band 0xFFFF) } } -AutoSize | Out-String -Width 200
} catch {
    "FAILED: $_"
} finally {
    Stop-Transcript | Out-Null
    "Written to $log"
    Read-Host "Done - press Enter to close"
}
