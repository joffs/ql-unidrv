# Label editor spike: prints a test bitmap per case on a capture printer
# (no real printer, no administrator) and checks with tools\prn_raster.py that the print is the bitmap dot for dot.
# Usage: powershell -File labeltool\test\editor_spike.ps1 [-Printer "QL-800 Capture"] [-Only case,case]
param(
    [string]$Printer = "QL-800 Capture",
    [string]$Only
)
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "editor-spike"   # QL_TEST_OUT: a folder per session
New-Item -ItemType Directory -Force $out | Out-Null

$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "editor_spike.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:exe /platform:anycpu /warnaserror+ /codepage:65001 "/out:$exe" /main:QlLabels.Tests.EditorSpike `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        /recurse:src\*.cs test\editor_spike.cs
    if ($LASTEXITCODE) { throw "build failed" }
} finally { Pop-Location }

$portFile = (Get-Printer $Printer).PortName
$cases = @(
    @("continuous-62x40", "Label 040", 300, "portrait"),
    @("continuous-62x40-landscape", "Label 040", 300, "landscape"),
    @("continuous-62x40-300x600", "Label 040", 600, "portrait"),
    @("diecut-29x90", "29mm x 90mm", 300, "portrait"),
    @("round-12mm", "12mm Dia", 300, "portrait")
)
$results = foreach ($c in $cases) {
    $name, $paper, $dpi, $orient = $c
    if ($Only -and ($Only -split "," | ForEach-Object { $_.Trim() }) -notcontains $name) { continue }
    $pbm = Join-Path $out "$name-sent.pbm"
    if (Test-Path $portFile) { [IO.File]::Delete($portFile) }
    $log = & $exe $Printer $paper $dpi $orient $pbm
    if ($LASTEXITCODE) { throw "editor_spike failed for $name" }
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-PrintJob -PrinterName $Printer -ErrorAction SilentlyContinue) -or -not (Test-Path $portFile)) {
        if ((Get-Date) -gt $deadline) { throw "timeout waiting for output of $name" }
        Start-Sleep -Milliseconds 300
    }
    $prn = Join-Path $out "$name.prn"
    Copy-Item $portFile $prn -Force
    $report = python -I (Join-Path $root "tools\prn_raster.py") $prn (Join-Path $out $name) --png --expect $pbm
    [pscustomobject]@{ Case = $name; Result = ($report | Select-Object -Skip 1 -First 1); Details = (@($log) + ($report | Select-Object -Skip 2)) -join "`n" }
}
$results | Format-Table Case, Result -AutoSize
$results | ForEach-Object { "--- $($_.Case)"; $_.Details }
