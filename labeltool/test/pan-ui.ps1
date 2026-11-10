# Panning with the middle mouse button in QL Label Maker: CHECK lines in German and English. Builds pan_ui.cs and runs it through
# deskrun.exe (a desktop of its own: the user's mouse and focus stay untouched). Changes nothing (nothing saved or
# printed). Needs deskrun.exe (tools\uitest\build.cmd; also taken from the main checkout when this is a worktree) and the printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\pan-ui.ps1 [-Lang de,en] [-Src <source folder, default labeltool\src>]
param([string]$Lang = "de,en", [string]$Src = "src")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "pan-ui"
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
$exe = Join-Path $out "pan_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.PanUi /win32manifest:app.manifest "/resource:assets\pan-move.cur,pan-move.cur" `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$Src\*.cs" test\pan_ui.cs
    if ($LASTEXITCODE) { throw "pan_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    & $deskrun $exe $l 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE) { $failed = $true }
}
if ($failed) { exit 1 }
