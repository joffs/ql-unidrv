# One status window per user session (statuswindow.cs) through out\tools\uitest\deskrun.exe: a desktop of its own, the
# user's mouse and focus stay untouched; an event name of its own, so the user's monitor is never signalled. Changes
# nothing. Exit code = failures.
#   powershell -File labeltool\test\status-window.ps1
# Needs: out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), labeltool\build.cmd run once (Version.g.cs)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "status-window"   # QL_TEST_OUT: a folder per session
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "statuswindow.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.StatusWindowTest /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs test\statuswindow.cs
    if ($LASTEXITCODE) { throw "statuswindow build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
& $deskrun --lock instances $exe
exit $LASTEXITCODE
