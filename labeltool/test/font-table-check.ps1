# Do QL Label Formats', QL Label Maker's and QL Printer Status' windows load GDI+'s table of every installed font? (UiFont.cs: they
# shouldn't; that table costs seconds after a restart with many fonts.) Builds font_table_check.cs with the label tool's
# sources and runs it natively through deskrun (own desktop, no focus taken) per window. Changes nothing.
#   powershell -File labeltool\test\font-table-check.ps1 [-Machine arm64|amd64]
# Needs out\tools\uitest\deskrun.exe (tools\uitest\build.cmd) and labeltool\build.cmd run once (Version.g.cs).
param([string]$Machine = "arm64", [switch]$Shots)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "font-table-check"
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "font_table_check.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.FontTableCheck /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:src\*.cs test\font_table_check.cs
    if ($LASTEXITCODE) { throw "font_table_check build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
$failed = $false
foreach ($window in "formats", "maker", "status") {
    $lines = & $deskrun --machine $Machine --lock instances $exe $window 2>&1 | ForEach-Object { "$_" } | Where-Object { $_ -notmatch "^deskrun" }
    $lines | ForEach-Object { "$window  $_" }
    if (-not ($lines -match "did NOT load")) { $failed = $true }
}
if ($Shots) {
    Add-Type -AssemblyName System.Drawing
    foreach ($window in "formats", "maker", "status") {
        foreach ($mode in "new", "off") {
            $env:QL_FONTCHECK_SHOT = Join-Path $out "$window-$mode.png"
            $env:QL_UIFONT = if ($mode -eq "off") { "off" } else { "" }
            & $deskrun --machine $Machine --lock instances $exe $window 2>&1 | Out-Null
        }
        $env:QL_FONTCHECK_SHOT = ""; $env:QL_UIFONT = ""
        $a = [Drawing.Bitmap]::FromFile((Join-Path $out "$window-new.png")); $b = [Drawing.Bitmap]::FromFile((Join-Path $out "$window-off.png"))
        $diff = 0
        if ($a.Width -ne $b.Width -or $a.Height -ne $b.Height) { $diff = -1 }
        else { for ($y = 0; $y -lt $a.Height; $y += 1) { for ($x = 0; $x -lt $a.Width; $x += 1) { if ($a.GetPixel($x, $y) -ne $b.GetPixel($x, $y)) { $diff++ } } } }
        "$window  pictures: " + $(if ($diff -lt 0) { "different sizes {0}x{1} / {2}x{3}" -f $a.Width, $a.Height, $b.Width, $b.Height } else { "$diff pixels differ" })
        $a.Dispose(); $b.Dispose()
    }
}
if ($failed) { "FAIL: a window loaded the font table"; exit 1 }
"OK: no window loaded the font table"
