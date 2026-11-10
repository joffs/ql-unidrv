# Theme sweep of QL Label Formats and QL Printer Status (formatsweep.cs) in light, dark and High Contrast, German and
# English, each followed by live switches through the other schemes and back; through out\tools\uitest\deskrun.exe on
# a desktop of its own (the user's mouse and focus stay untouched). Changes nothing: schemes and language are forced
# inside the test process; nothing is saved, printed, elevated or installed.
#   formats-sweep.ps1 [-App <QlLabelFormats.exe>] [-Out <folder>]
# -> <Out>\<scheme>-<lang>-<window>.png and <Out>\report.tsv (shot, logical height, black blocks, white blocks, TALL).
param(
    [string]$App = "$PSScriptRoot\..\..\out\tools\QlLabelFormats.exe",
    [string]$Out = $(if ($env:QL_TEST_OUT) { Join-Path $env:QL_TEST_OUT "formats-sweep" } else { "$PSScriptRoot\..\..\out\formats-sweep" }))

$ErrorActionPreference = "Stop"
$Out = [IO.Path]::GetFullPath($Out)
$csc = "$env:WINDIR\Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$deskrun = "$PSScriptRoot\..\..\out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
New-Item -ItemType Directory -Force $Out | Out-Null
Remove-Item "$Out\*.png", "$Out\report.tsv" -ErrorAction SilentlyContinue
$exe = Join-Path $Out "formatsweep.exe"
& $csc /nologo /target:exe /platform:anycpu /codepage:65001 "/out:$exe" "/win32manifest:$PSScriptRoot\..\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\formatsweep.cs"
if ($LASTEXITCODE -ne 0) { throw "formatsweep.cs: build failed" }
Copy-Item "$PSScriptRoot\..\app.config" "$exe.config" -Force
$failed = 0
foreach ($scheme in "light", "dark", "highcontrast") {
    foreach ($lang in "de", "en") {
        $samples = [IO.Path]::GetFullPath("$PSScriptRoot\..\help\formats\img\shots\labels-$lang.json")
        & $deskrun $exe ([IO.Path]::GetFullPath($App)) $Out $lang $scheme $samples
        if ($LASTEXITCODE -ne 0) { Write-Host "formatsweep $scheme $lang`: exit code $LASTEXITCODE"; $failed++ }
    }
}
$tall = @(Get-Content "$Out\report.tsv" | Where-Object { $_ -match "TALL" })
Write-Host "Shots: $Out ($($tall.Count) taller than 690 px)"
$tall | ForEach-Object { Write-Host "  $_" }
exit $failed
