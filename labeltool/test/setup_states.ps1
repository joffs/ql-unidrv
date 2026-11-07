# The setup window's finish states (setup_states.cs) in German and English through deskrun.exe (a desktop of its own:
# the user's mouse and focus stay untouched). Preview mode: nothing on the PC changes. PNGs in out\setup-states;
# exit 1 when a state isn't as expected.
# Usage: powershell -File labeltool\test\setup_states.ps1 [-Lang de,en]
# Needs: out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), labeltool\build.cmd run once (Version.g.cs)
param([string]$Lang = "de,en")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $root "out\setup-states"
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }

$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "setup_states.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.SetupStates /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs test\setup_states.cs
    if ($LASTEXITCODE) { throw "setup_states build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }

[Console]::OutputEncoding = New-Object Text.UTF8Encoding $false
$failed = $false
$ErrorActionPreference = "Continue"   # the harness's stderr (an exception) is output here, not a script error
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    Write-Host "== $l"
    & $deskrun $exe $out $l 2>&1 | ForEach-Object { Write-Host "$_" }
    if ($LASTEXITCODE) { $failed = $true }
}
if ($failed) { exit 1 }
