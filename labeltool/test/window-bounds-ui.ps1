# QL Label Maker and QL Label Formats remember their window size and place (window_bounds_ui.cs): CHECK lines for both
# windows (first start, moved and resized, maximized, minimized, off-screen, larger than the screen, damaged value).
# Builds window_bounds_ui.cs and runs it through deskrun.exe (a desktop of its own: the user's mouse and focus stay
# untouched). Changes nothing of the user's: a registry key of its own, removed afterwards. Each run is stopped after
# 150 s (the test's own watchdog: 120 s).
# Needs deskrun.exe (tools\uitest\build.cmd; also taken from the main checkout when this is a worktree) and the
# printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\window-bounds-ui.ps1 [-Lang de,en] [-Src <source folder>]
param([string]$Lang = "de,en", [string]$Src = "src", [int]$TimeoutSeconds = 150)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "window-bounds-ui"
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
$exe = Join-Path $out "window_bounds_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.WindowBoundsUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/recurse:$Src\*.cs" test\window_bounds_ui.cs
    if ($LASTEXITCODE) { throw "window_bounds_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    $log = Join-Path $out "run-$l.txt"
    $p = Start-Process -FilePath $deskrun -ArgumentList @("`"$exe`"", $l) -NoNewWindow -PassThru -RedirectStandardOutput $log
    $null = $p.Handle   # without it ExitCode stays empty after WaitForExit
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        & taskkill /T /F /PID $p.Id 2>&1 | Out-Null
        Get-Content $log -Encoding UTF8
        "CHECK $l run ended within $TimeoutSeconds s FAIL"
        $failed = $true
        continue
    }
    Get-Content $log -Encoding UTF8
    if ($p.ExitCode -or (Select-String -Path $log -Pattern " FAIL$" -Quiet)) { $failed = $true }
}
if ($failed) { exit 1 }
