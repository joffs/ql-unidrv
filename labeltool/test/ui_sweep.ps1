# UI sweep of QL Label Maker in one command: builds ui_sweep.cs and runs it through deskrun.exe (a desktop of its own:
# the user's mouse and focus stay untouched) in German and English, then ui_sweep.py (barcode sizes decoded with
# zxing-cpp, sample contents checked for real GTINs). Report: out\ui-sweep\report.txt; exit 1 when there are findings.
# Usage: powershell -File labeltool\test\ui_sweep.ps1 [-Checks clip,drag,sizes,contents] [-Lang de,en]
# Needs: out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), the printer "QL-800 Capture" (nothing is printed),
# python with zxing-cpp and numpy (pip install --user zxing-cpp) for the sizes check.
param(
    [string]$Checks = "clip,drag,sizes,contents",
    [string]$Lang = "de,en"
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "ui-sweep"   # QL_TEST_OUT: a folder per session
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }

$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "ui_sweep.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.UiSweep /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /recurse:src\*.cs test\ui_sweep.cs
    if ($LASTEXITCODE) { throw "ui_sweep build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }

$log = Join-Path $out "sweep.log"
[IO.File]::WriteAllText($log, "")
[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false   # ui_sweep.exe writes UTF-8 (dashes, middle dots in the findings)
$checkList = $Checks -split "," | ForEach-Object { $_.Trim() }
foreach ($mode in "clip", "drag", "sizes") {
    if ($checkList -notcontains $mode) { continue }
    foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
        if ($mode -ne "clip" -and $l -ne ($Lang -split ",")[0].Trim()) { continue }   # drag and sizes don't depend on the language
        Write-Host "== $mode $l"
        $lines = & $deskrun $exe $mode $l $out 2>&1 | ForEach-Object { "$_" }
        $lines | Where-Object { $_ -match "^(FIND|INFO|DONE|timeout|refused)|to a test process" } | ForEach-Object { Write-Host $_ }
        [IO.File]::AppendAllLines($log, [string[]]$lines)
    }
}
# not -I: zxing-cpp is installed in the user's site-packages
python (Join-Path $PSScriptRoot "ui_sweep.py") $out $root ($checkList -join ",")
exit $LASTEXITCODE
