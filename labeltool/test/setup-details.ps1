# Screenshots of the Printer setup's failure result for an unreachable share ("The change did not finish", closed and with
# "Show details") in German and English at 96 and 144 dpi: builds setup_details.cs against labeltool\src (or, with -Ref,
# against that commit's labeltool\src, for before/after shots) and runs it through deskrun.exe (a desktop of its own: the
# user's mouse and focus stay untouched). Changes nothing. Needs out\tools\uitest\deskrun.exe (tools\uitest\build.cmd)
# and a kept settings transcript (install.ps1 -Log of a failed -SharePort run).
# Usage: powershell -File labeltool\test\setup-details.ps1 -Transcript <file> [-Ref <commit>] [-Tag after] [-Out <folder>]
#        ->  <Out, default out\mockups\setup-details>\<tag>-<lang>-<dpi>-<1-result|2-details>.png
param([Parameter(Mandatory = $true)][string]$Transcript, [string]$Ref, [string]$Tag = "after", [string]$Out, [string]$Lang = "de,en")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Out) { $Out = Join-Path $root "out\mockups\setup-details" }
New-Item -ItemType Directory -Force $Out | Out-Null
$Transcript = (Resolve-Path $Transcript).Path
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$work = Join-Path $Out "build-$Tag"
New-Item -ItemType Directory -Force $work | Out-Null
$src = Join-Path $root "labeltool\src"
if ($Ref) {
    # that commit's sources in a folder of their own
    $zip = Join-Path $work "src.zip"
    & git -C $root archive --format=zip -o $zip $Ref labeltool/src
    if ($LASTEXITCODE) { throw "git archive $Ref failed" }
    $refDir = Join-Path $work "ref"
    if (Test-Path $refDir) { Remove-Item -Recurse -Force $refDir }
    Expand-Archive $zip $refDir
    $src = Join-Path $refDir "labeltool\src"
}
$exe = Join-Path $work "setup_details.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$exe" /main:QlLabels.Tests.SetupDetailsShots /win32manifest:app.manifest `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll "$src\*.cs" test\setup_details.cs
    if ($LASTEXITCODE) { throw "setup_details build failed" }
    Copy-Item app.config "$exe.config" -Force
} finally { Pop-Location }
$env:QL_TEST_SILENT = "1"
foreach ($l in ($Lang -split "," | ForEach-Object { $_.Trim() })) {
    & $deskrun $exe $l $Out $Transcript $Tag 2>&1 | ForEach-Object { "$_" }
}
"Screenshots: $Out"
