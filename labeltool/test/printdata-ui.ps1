# QL Label Maker's "Print with data" dialog: CHECK lines in German and
# English. Builds printdata_ui.cs and runs it through deskrun.exe (a desktop of its own: the user's mouse and focus stay
# untouched). Prints only to the capture printer (C:\ProgramData\QLUnidrv\capture\capture.prn, no paper): two jobs
# through the dialog, checked page by page against the engine's pages (printdata_prn_check.py, Python). Saves nothing
# else; the dialog's remembered size is put back; its files go to <QL_TEST_OUT or out>\printdata-ui. Each run is
# stopped after 300 s (the test's own watchdog: 240 s).
# Needs deskrun.exe (tools\uitest\build.cmd; also taken from the main checkout when this is a worktree) and the
# printer "QL-800 Capture". QL_PRINTDATA_SHOT=<folder>: dialog pictures for a look.
# Usage: powershell -File labeltool\test\printdata-ui.ps1 [-Lang de,en] [-Src <source folder, default labeltool\src>]
param([string]$Lang = "de,en", [string]$Src = "src", [int]$TimeoutSeconds = 300)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "printdata-ui"
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
$exe = Join-Path $out "printdata_ui.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.PrintDataUi /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "/recurse:$Src\*.cs" test\printdata_ui.cs
    if ($LASTEXITCODE) { throw "printdata_ui build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    $log = Join-Path $out "run-$l.txt"
    $p = Start-Process -FilePath $deskrun -ArgumentList @("`"$exe`"", $l, "`"$out`"") -NoNewWindow -PassThru -RedirectStandardOutput $log
    $null = $p.Handle   # without it ExitCode stays empty after WaitForExit
    if (-not $p.WaitForExit($TimeoutSeconds * 1000)) {
        # deskrun and the test (its child) stopped: no hang
        & taskkill /T /F /PID $p.Id 2>&1 | Out-Null
        Get-Content $log -Encoding UTF8
        "CHECK $l run ended within $TimeoutSeconds s FAIL"
        $failed = $true
        continue
    }
    Get-Content $log -Encoding UTF8
    if ($p.ExitCode -or (Select-String -Path $log -Pattern " FAIL$" -Quiet)) { $failed = $true }
    # the captures of the dialog's prints, page by page against the engine's pages
    $prints = @(Get-ChildItem -Directory $out -Filter "print-*" | ForEach-Object { $_.FullName })
    if ($prints.Count -gt 0) {
        & python (Join-Path $PSScriptRoot "printdata_prn_check.py") @prints
        if ($LASTEXITCODE) { $failed = $true }
    }
}
if ($failed) { exit 1 }
