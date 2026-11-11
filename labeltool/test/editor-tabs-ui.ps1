# QL Label Maker's tabs (editor_tabs_ui.cs, tabs 0.23; the strip and close dialog alone: tabs-ui.ps1): CHECK lines in German and English, light and dark (forced inside the
# test process): each tab keeps its undo, zoom, selection and label format; Ctrl+N, Ctrl+Tab, Ctrl+W; a changed tab's
# question; the window's one question for several changed labels; the overflow; a file already open (its tab comes
# forward); a second start's file in a new tab; the last tab closed leaves "Untitled 1". Builds editor_tabs_ui.cs and runs it
# through deskrun.exe (a desktop of its own: the user's mouse and focus stay untouched). Changes nothing of the user's:
# its own label files under %TEMP% and its own registry key, both removed afterwards. Each run is stopped after 150 s
# (the test's own watchdog: 120 s). Pictures: <QL_TEST_OUT or out>\editor-tabs-ui\editor-tabs-<lang>-<scheme>-<100|150>.png;
# the 150 % run needs a screen at 150 % (skipped without one).
# Needs deskrun.exe (tools\uitest\build.cmd; also taken from the main checkout when this is a worktree) and the
# printer "QL-800 Capture".
# Usage: powershell -File labeltool\test\editor-tabs-ui.ps1 [-Lang de,en] [-Scheme light,dark] [-Dpi 100,150] [-Src <source folder>]
param([string]$Lang = "de,en", [string]$Scheme = "light,dark", [string]$Dpi = "100,150", [string]$Src = "src", [int]$TimeoutSeconds = 150)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "editor-tabs-ui"
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
$exe = Join-Path $out "editor_tabs_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.EditorTabsUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/recurse:$Src\*.cs" test\editor_tabs_ui.cs
    if ($LASTEXITCODE) { throw "editor_tabs_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    foreach ($s in ($Scheme -split "," | ForEach-Object { $_.Trim() })) {
      foreach ($d in ($Dpi -split "," | ForEach-Object { $_.Trim() })) {
        $log = Join-Path $out "run-$l-$s-$d.txt"
        $p = Start-Process -FilePath $deskrun -ArgumentList @("`"$exe`"", $l, $s, "`"$out`"", $d) -NoNewWindow -PassThru -RedirectStandardOutput $log
        $null = $p.Handle   # without it ExitCode stays empty after WaitForExit
        if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
            # deskrun and the test (its child) stopped: no hang
            & taskkill /T /F /PID $p.Id 2>&1 | Out-Null
            Get-Content $log -Encoding UTF8
            "CHECK $l $s $d run ended within $TimeoutSeconds s FAIL"
            $failed = $true
            continue
        }
        Get-Content $log -Encoding UTF8
        if ($p.ExitCode -or (Select-String -Path $log -Pattern " FAIL$" -Quiet)) { $failed = $true }
      }
    }
}
if ($failed) { exit 1 }
