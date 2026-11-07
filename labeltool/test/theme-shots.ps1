# Screenshots of every program window in light and dark, German and English (themeshots.cs, through
# out\tools\uitest\deskrun.exe on a desktop of its own: the user's mouse and focus stay untouched). Changes nothing:
# the scheme and language are forced inside the test process, nothing is saved, printed or installed.
#   theme-shots.ps1 [-App <QlLabelFormats.exe>] [-Out <folder>]
# -> <Out>\<light|dark>-<de|en>-<window>.png, plus "-live" shots after a live switch to the other scheme.
param(
    [string]$App = "$PSScriptRoot\..\..\out\tools\QlLabelFormats.exe",
    [string]$Out = "$PSScriptRoot\..\..\out\theme-shots")

$ErrorActionPreference = "Stop"
$Out = [IO.Path]::GetFullPath($Out)
$csc = "$env:WINDIR\Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$deskrun = "$PSScriptRoot\..\..\out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
New-Item -ItemType Directory -Force $Out | Out-Null
$exe = Join-Path $Out "themeshots.exe"
& $csc /nologo /target:exe /platform:anycpu /codepage:65001 "/out:$exe" "/win32manifest:$PSScriptRoot\..\app.manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\themeshots.cs"
if ($LASTEXITCODE -ne 0) { throw "themeshots.cs: build failed" }
Copy-Item "$PSScriptRoot\..\app.config" "$exe.config" -Force
$label = [IO.Path]::GetFullPath("$PSScriptRoot\..\help\img\shots\labels\sample\Regal 3.qllabel")
$failed = 0
foreach ($scheme in "light", "dark") {
    foreach ($lang in "de", "en") {
        $samples = [IO.Path]::GetFullPath("$PSScriptRoot\..\help\formats\img\shots\labels-$lang.json")
        & $deskrun $exe ([IO.Path]::GetFullPath($App)) $Out $lang $scheme $samples $label
        if ($LASTEXITCODE -ne 0) { Write-Host "themeshots $scheme $lang`: exit code $LASTEXITCODE"; $failed++ }
    }
}
Write-Host "Screenshots: $Out"
exit $failed
