# Screenshots of QL Label Maker's tool groups, flyouts and shape panels (editor-next step 3) in German and English at
# 96 and 144 dpi: builds shapes_ui.cs and runs it through deskrun.exe (a desktop of its own: the user's mouse and focus
# stay untouched). Changes nothing (no registry values, nothing saved or printed). Needs out\tools\uitest\deskrun.exe
# (tools\uitest\build.cmd) and the printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\shapes-ui.ps1 [-Lang de,en]   ->  <QL_TEST_OUT or out>\shapes-ui\*.png
param([string]$Lang = "de,en")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "shapes-ui"
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
$exe = Join-Path $out "shapes_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.ShapesUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs test\shapes_ui.cs
    if ($LASTEXITCODE) { throw "shapes_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    & $deskrun $exe $l $out 2>&1 | ForEach-Object { "$_" }
}
"Screenshots: $out"
