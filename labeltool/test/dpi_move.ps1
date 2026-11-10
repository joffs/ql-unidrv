# Real mixed-DPI check (dpi_move.cs) through deskrun.exe: the install and setup windows, the setup dialogs, QL Label
# Formats, the status window, About and QL Label Maker, each moved between the 100 % screen and a screen with other scaling (both ways,
# and opened directly on each). Needs that second screen attached; refuses without it. Changes nothing on the PC.
# Lines "STATE window step dpi size client font" in <out>\states.txt, a picture per step.
# Usage: powershell -File labeltool\test\dpi_move.ps1 [-Lang de,en] [-Src <labeltool folder to build, e.g. a worktree's>]
#        [-Out <folder>] [-Mode same|close]   (two runs, e.g. before and after a refactor: compare states.txt and the
#        pictures). -Mode same: the same steps on one screen (tests the harness without the second screen); -Mode close:
#        QL Label Formats closed with WM_CLOSE only. A step that hangs ends the run with "FAIL <window> <step>".
# Needs: out\tools\uitest\deskrun.exe (tools\uitest\build.cmd)
param([string]$Lang = "de,en", [string]$Src = "", [string]$Out = "", [string]$Mode = "")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Src) { $Src = Join-Path $root "labeltool" }
$Src = (Resolve-Path $Src).Path
if (-not $Out) { $Out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "dpi-move" }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path   # absolute: the build runs inside -Src
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $Out "dpi_move.exe"
Push-Location $Src
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.DpiMove /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        src\*.cs (Join-Path $PSScriptRoot "dpi_move.cs")
    if ($LASTEXITCODE) { throw "dpi_move build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$states = Join-Path $Out "states.txt"
[IO.File]::WriteAllText($states, "")
$failed = 0
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    $dir = Join-Path $Out $l
    $runArgs = @($exe, $dir, $l) + $(if ($Mode) { @($Mode) } else { @() })
    $lines = & $deskrun @runArgs 2>&1 | ForEach-Object { "$_" } | ForEach-Object {
        if ($_ -match "^(FAIL|INFO|DONE|refused)|to a test process") { Write-Host $_ }   # live: a hang shows where it stops
        $_ }
    if ($LASTEXITCODE) { $failed++ }
    [IO.File]::AppendAllLines($states, [string[]]@($lines | Where-Object { $_ -match "^STATE" } | ForEach-Object { "$l $_" }))   # @(): none when refused
}
Write-Host "states: $states"
exit $failed
