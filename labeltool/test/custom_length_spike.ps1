# Custom length spike (QL Label Maker phase 2): prints test bitmaps with page sizes of their own (no label format,
# no Windows form) on the capture printers (no real printer, no administrator), then checks with tools\prn_raster.py
# that each print is the bitmap dot for dot and with tools\compare_prn.py that it equals the same length printed
# through a label format. Shows the job header (ESC i z: media, width, length, raster lines; ESC i d: feed) and
# the plug-in's log lines of each job.
# Usage: powershell -File labeltool\test\custom_length_spike.ps1 [-Only case,case]
param([string]$Only)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "custom-length-spike"   # QL_TEST_OUT: a folder per session
New-Item -ItemType Directory -Force $out | Out-Null

$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "custom_length_spike.exe"
& $csc /nologo /target:exe /platform:anycpu /warnaserror+ /codepage:65001 "/out:$exe" /r:System.Drawing.dll `
    (Join-Path $root "labeltool\test\custom_length_spike.cs")
if ($LASTEXITCODE) { throw "build failed" }

# ESC i z / ESC i d of page 1
$header = Join-Path $out "header.py"
@'
import sys
b = open(sys.argv[1], 'rb').read()
i = b.find(b'\x1biz')
z = b[i + 3:i + 13]
d = b.find(b'\x1bid')
feed = b[d + 3] | b[d + 4] << 8 if d >= 0 else None
kind = {0x0A: 'continuous', 0x0B: 'die-cut'}.get(z[1], hex(z[1]))
print('ESC i z: %s, width %d mm, length %d mm, %d lines; ESC i d feed %s' % (kind, z[2], z[3], int.from_bytes(z[4:8], 'little'), feed))
'@ | Set-Content -Encoding ascii $header

$plog = "C:\ProgramData\QLUnidrv\log\plugin.log"
$Q8 = "QL-800 Capture"; $Q11 = "QL-1115NWB Capture"
# name, printer, route, width 0.1 mm, length 0.1 mm, lines/in, oracle case (same stream expected)
$cases = @(
    @("800-form-40", $Q8, "form:Label 040", 620, 400, 300, ""),
    @("800-dm256-40", $Q8, "dm256", 620, 400, 300, "800-form-40"),
    @("800-dm0-40", $Q8, "dm0", 620, 400, 300, "800-form-40"),
    @("800-net-40", $Q8, "net", 620, 400, 300, "800-form-40"),
    @("800-form-17", $Q8, "form:Label 017", 620, 170, 300, ""),
    @("800-dm256-17", $Q8, "dm256", 620, 170, 300, "800-form-17"),
    @("800-dm256-37.5", $Q8, "dm256", 620, 375, 300, ""),
    @("800-net-37.5", $Q8, "net", 620, 375, 300, "800-dm256-37.5"),
    @("800-dm0-37.5", $Q8, "dm0", 620, 375, 300, "800-dm256-37.5"),
    @("800-dm256-300", $Q8, "dm256", 620, 3000, 300, ""),
    @("800-dm0-300", $Q8, "dm0", 620, 3000, 300, "800-dm256-300"),
    @("800-net-300", $Q8, "net", 620, 3000, 300, "800-dm0-300"),
    @("800-dm0-100", $Q8, "dm0", 620, 1000, 300, "800-dm256-100"),
    @("800-net-100", $Q8, "net", 620, 1000, 300, "800-dm256-100"),
    @("800-dm256-29x90", $Q8, "dm256", 290, 900, 300, ""),
    # Real die-cut choices (must stay die-cut): by form name, by paper number, through .NET's paper list
    @("800-dmform-62x100", $Q8, "dmform:62mm x 100mm", 620, 1000, 300, ""),
    @("800-dmpaper-62x100", $Q8, "dmpaper:202", 620, 1000, 300, "800-dmform-62x100"),
    @("800-form-62x100", $Q8, "form:62mm x 100mm", 620, 1000, 300, "800-dmform-62x100"),
    @("800-form-29x90", $Q8, "form:29mm x 90mm", 290, 900, 300, ""),
    @("800-dm0-11", $Q8, "dm0", 620, 110, 300, ""),
    @("800-dm0-1001", $Q8, "dm0", 620, 10010, 300, ""),
    @("800-form-40-600", $Q8, "form:Label 040", 620, 400, 600, ""),
    @("800-dm256-40-600", $Q8, "dm256", 620, 400, 600, "800-form-40-600"),
    @("800-dm256-37.5-600", $Q8, "dm256", 620, 375, 600, ""),
    @("800-dm256-100", $Q8, "dm256", 620, 1000, 300, ""),
    @("800-dm256-29x50", $Q8, "dm256", 290, 500, 300, ""),
    @("800-dm256-12", $Q8, "dm256", 620, 120, 300, ""),
    @("800-dm256-11", $Q8, "dm256", 620, 110, 300, ""),
    @("800-dm256-5", $Q8, "dm256", 620, 50, 300, ""),
    @("800-dm256-1000", $Q8, "dm256", 620, 10000, 300, ""),
    @("800-dm256-1001", $Q8, "dm256", 620, 10010, 300, ""),
    @("800-dm256-2000", $Q8, "dm256", 620, 20000, 300, ""),
    @("1115-form-40", $Q11, "form:Label 040", 620, 400, 300, ""),
    @("1115-dm256-40", $Q11, "dm256", 620, 400, 300, "1115-form-40"),
    @("1115-dm256-37.5", $Q11, "dm256", 620, 375, 300, ""),
    @("1115-dm256-300", $Q11, "dm256", 620, 3000, 300, ""),
    @("1115-dm256-102x50", $Q11, "dm256", 1020, 500, 300, ""),
    @("1115-dm256-3000", $Q11, "dm256", 620, 30000, 300, ""),
    @("1115-dm256-3001", $Q11, "dm256", 620, 30010, 300, "")
)
$results = foreach ($c in $cases) {
    $name, $printer, $route, $w, $l, $dpi, $oracle = $c
    if ($Only -and ($Only -split "," | ForEach-Object { $_.Trim() }) -notcontains $name) { continue }
    $portFile = (Get-Printer $printer).PortName
    $pbm = Join-Path $out "$name-sent.pbm"
    if (Test-Path $portFile) { [IO.File]::Delete($portFile) }
    $logStart = (Get-Item $plog).Length
    $log = & $exe $printer $route $w $l $dpi $pbm 2>&1
    if ($LASTEXITCODE) { [pscustomobject]@{ Case = $name; Result = "FAILED"; Header = ""; Oracle = ""; Details = ($log -join "`n") }; continue }
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-PrintJob -PrinterName $printer -ErrorAction SilentlyContinue) -or -not (Test-Path $portFile)) {
        if ((Get-Date) -gt $deadline) { throw "timeout waiting for output of $name" }
        Start-Sleep -Milliseconds 300
    }
    Start-Sleep -Milliseconds 300
    $prn = Join-Path $out "$name.prn"
    Copy-Item $portFile $prn -Force
    $report = python -I (Join-Path $root "tools\prn_raster.py") $prn (Join-Path $out $name) --expect $pbm
    $head = python -I $header $prn
    $cmp = ""
    if ($oracle) {
        $o = Join-Path $out "$oracle.prn"
        $r = python -I (Join-Path $root "tools\compare_prn.py") $o $prn
        $cmp = if ($LASTEXITCODE -eq 0) { "= $oracle" } else { "differs from ${oracle}: " + (($r | Select-Object -Skip 1) -join " / ") }
    }
    $fs = [IO.File]::Open($plog, "Open", "Read", "ReadWrite")
    try { $fs.Position = [Math]::Min($logStart, $fs.Length); $plugin = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Close() }
    $plugin = ($plugin -split "`r?`n" | Where-Object { $_ -match "custom|page start|CommandCallback (199|1\d\d\d\d) " }) -join "`n"
    [pscustomobject]@{ Case = $name; Result = ($report | Select-Object -Skip 1 -First 1); Header = $head; Oracle = $cmp
                       Details = (@($log) + ($report | Select-Object -Skip 2) + $plugin) -join "`n" }
}
$results | Format-Table Case, Result, Oracle -AutoSize | Out-String -Width 250
$results | ForEach-Object { "--- $($_.Case)"; $_.Header; $_.Details }
