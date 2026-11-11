# QL Label Maker's tool bar for drawing (0.23): the arrow, a click switches a tool on, Enter inserts, the flyout, the
# status bar hint; German and English, light and dark, at 96 and 144 dpi: builds draw_ui.cs and runs it through
# deskrun.exe (a desktop of its own: the user's mouse and focus stay untouched). Changes nothing (the remembered tools
# in HKCU are put back, nothing saved or printed). Needs out\tools\uitest\deskrun.exe (tools\uitest\build.cmd) and the
# printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\draw-ui.ps1 [-Lang de,en] [-Scheme light,dark]  ->  <QL_TEST_OUT or out>\draw-ui\*.png
# Exit code 1 when a check failed.
param([string]$Lang = "de,en", [string]$Scheme = "light,dark")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "draw-ui"
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
$exe = Join-Path $out "draw_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.DrawUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:src\*.cs test\draw_ui.cs
    if ($LASTEXITCODE) { throw "draw_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
$failed = 0
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    foreach ($s in ($Scheme -split "," | ForEach-Object { $_.Trim() })) {
        $lines = & $deskrun $exe $l $s $out 2>&1 | ForEach-Object { "$_" }
        $lines
        if (-not ($lines | Where-Object { $_ -like "DONE *" })) { "FAIL $l $s`: no DONE line"; $failed++ }
        $failed += @($lines | Where-Object { $_ -match "FAIL" }).Count
    }
}
"Screenshots: $out"
if ($failed) { "draw-ui: $failed FAIL"; exit 1 }
"draw-ui: all PASS"
