# Barcode print test of QL Label Maker: writes the cases (barcode_cases.py), prints each one with the editor window's
# own Print on a capture printer (barcode_print.cs through deskrun.exe: no focus taken, no paper), turns every .prn
# into a PNG (tools\prn_raster.py) and decodes and measures it (barcode_check.py; needs zxing-cpp). Then the barcode
# panel and stale paint while moving/turning codes (barcode_ui.cs, also through deskrun; out\barcode-test\ui-*.png).
# Usage: powershell -File labeltool\test\barcode_print.ps1 [-Printer "QL-800 Capture"] [-Only case,case] [-NoPrint]
param(
    [string]$Printer = "QL-800 Capture",
    [string]$Only,
    [switch]$NoPrint
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "barcode-test"   # QL_TEST_OUT: a folder per session
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }

python -I (Join-Path $PSScriptRoot "barcode_cases.py") $out
if ($LASTEXITCODE) { throw "barcode_cases.py failed" }
$cases = Get-Content (Join-Path $out "cases.json") -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not $NoPrint) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    $exe = Join-Path $out "barcode_print.exe"
    Push-Location (Join-Path $root "labeltool")
    try {
        & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.BarcodePrint `
            /win32manifest:app.manifest /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll `
            /r:System.Drawing.dll src\*.cs test\barcode_print.cs
        if ($LASTEXITCODE) { throw "build failed" }
        Copy-Item app.config "$exe.config" -Force
    } finally { Pop-Location }

    $portFile = (Get-Printer $Printer).PortName
    foreach ($c in $cases) {
        if ($Only -and ($Only -split "," | ForEach-Object { $_.Trim() }) -notcontains $c.name) { continue }
        if (Test-Path $portFile) { [IO.File]::Delete($portFile) }
        & $deskrun --lock capture $exe (Join-Path $out "$($c.name).qllabel") $Printer "$($c.dpi)" | Out-Host
        if ($LASTEXITCODE) { Write-Warning "$($c.name): exit code $LASTEXITCODE"; continue }
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-PrintJob -PrinterName $Printer -ErrorAction SilentlyContinue) -or -not (Test-Path $portFile)) {
            if ((Get-Date) -gt $deadline) { throw "timeout waiting for the print of $($c.name)" }
            Start-Sleep -Milliseconds 300
        }
        Start-Sleep -Milliseconds 300
        $prn = Join-Path $out "$($c.name).prn"
        Copy-Item $portFile $prn -Force
        python -I (Join-Path $root "tools\prn_raster.py") $prn (Join-Path $out $c.name) --png | Out-Null
        if ($LASTEXITCODE) { throw "prn_raster failed for $($c.name)" }
    }
}
# not -I: zxing-cpp is installed in the user's site-packages
python (Join-Path $PSScriptRoot "barcode_check.py") $out
$printFail = $LASTEXITCODE

# The barcode panel and stale paint (a Code 128, a QR code and a level line on one label)
$ui = Join-Path $out "ui.qllabel"
[IO.File]::WriteAllText($ui, '{"format": "QL label", "version": 1, "label": {"form": "62mm", "paperId": 259, "view": "along", "length": "own", "length01": 700}, ' +
    '"objects": [{"type": "barcode", "symbology": "code128", "x": 5, "y": 8, "w": 30, "h": 14, "data": "ABC-123"}, ' +
    '{"type": "barcode", "symbology": "qr", "x": 42, "y": 6, "w": 20, "h": 20, "data": "QR UI"}, {"type": "line", "x": 5, "y": 30, "w": 30, "h": 0, "thickness": 0.5}]}')
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$uiExe = Join-Path $out "barcode_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$uiExe" /main:QlLabels.Tests.BarcodeUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs test\barcode_ui.cs
    if ($LASTEXITCODE) { throw "barcode_ui build failed" }
    Copy-Item app.config "$uiExe.config" -Force
} finally { Pop-Location }
& $deskrun $uiExe $ui $out | Where-Object { $_ -match "^CHECK|focus" } | Out-Host
$uiFail = $LASTEXITCODE
exit [int]($printFail -ne 0 -or $uiFail -ne 0)
