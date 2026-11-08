# Mouse-wheel zoom in QL Label Maker: CHECK lines in German and English. Builds wheel_ui.cs and runs it through
# deskrun.exe (a desktop of its own: the user's mouse and focus stay untouched). Changes nothing (nothing saved or
# printed). Needs out\tools\uitest\deskrun.exe (tools\uitest\build.cmd) and the printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\wheel-ui.ps1 [-Lang de,en] [-Src <source folder, default labeltool\src>]
param([string]$Lang = "de,en", [string]$Src = "src")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "wheel-ui"
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "wheel_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.WheelUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$Src\*.cs" test\wheel_ui.cs
    if ($LASTEXITCODE) { throw "wheel_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    & $deskrun $exe $l 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE) { $failed = $true }
}
if ($failed) { exit 1 }
