# Takes the window screenshots for the QL Label Formats manual (formatshots.cs, through out\tools\uitest\deskrun.exe on a
# desktop of its own: the user's mouse and focus stay untouched), German and English at 100 %, and cuts the pictures
# named below into ..\de and ..\en.
#   make-shots.ps1 [-App <QlLabelFormats.exe>] [-Out <folder>] [-Dest <folder>] [-Printer <name>] [-CropOnly]
# Shows the sample label formats of labels-de.json / labels-en.json, with this PC's "Brother QL-800 (Unidrv)" (or
# -Printer) as the test printer. Its roll is always "62 mm continuous" (formatshots.cs FixedCheck), which decides which
# formats are grey. Nothing is saved or printed. -Dest: where the pictures go (<Dest>\de, <Dest>\en; default: the
# manual's img folder). Run by labeltool\test\help-shots.ps1, which compares the result with the manual's pictures.
param(
    [string]$App = "$PSScriptRoot\..\..\..\..\..\out\tools\QlLabelFormats.exe",
    [string]$Out = "$PSScriptRoot\..\..\..\..\..\out\help-formats-shots",
    [string]$Dest = "$PSScriptRoot\..",
    [string]$Printer = "Brother QL-800 (Unidrv)",
    [switch]$CropOnly)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$Out = [IO.Path]::GetFullPath($Out)
$labeltool = "$PSScriptRoot\..\..\..\.."

if (-not $CropOnly) {
    $csc = "$env:WINDIR\Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    $deskrun = "$labeltool\..\out\tools\uitest\deskrun.exe"
    if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $exe = Join-Path $Out "formatshots.exe"
    & $csc /nologo /target:exe /platform:anycpu /codepage:65001 "/out:$exe" "/win32manifest:$labeltool\app.manifest" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\formatshots.cs"
    if ($LASTEXITCODE -ne 0) { throw "formatshots.cs: build failed" }
    Copy-Item "$labeltool\app.config" "$exe.config" -Force
    foreach ($lang in "de", "en") {
        $log = & $deskrun $exe ([IO.Path]::GetFullPath($App)) $Out $lang 100 ([IO.Path]::GetFullPath($PSScriptRoot)) $Printer
        if ($LASTEXITCODE -ne 0) { throw "formatshots $lang`: exit code $LASTEXITCODE" }
        $log | Where-Object { $_ -match "FAILED|^formats: " } | ForEach-Object { "formatshots $lang`: $_" }
    }
}

# name, screenshot (German, English), crop left, top, right, bottom (0, 0, 0, 0: the whole screenshot); an "en" crop
# only where the English window differs.
$pictures = @(
    @{ Name = "overview-window"; Shot = "window", "window";             De = 0, 0, 0, 0 },
    @{ Name = "toolbar";         Shot = "window", "window";             De = 30, 61, 990, 87 },
    @{ Name = "edit-panel";      Shot = "window", "window";             De = 395, 100, 990, 284 },
    @{ Name = "new-format";      Shot = "new", "new";                   De = 30, 95, 990, 295 },
    @{ Name = "greyed-panel";    Shot = "greyed", "greyed";             De = 395, 100, 990, 318 },
    @{ Name = "status-bar";      Shot = "window", "window";             De = 0, 629, 1016, 655 },
    @{ Name = "menu-file";       Shot = "menu-datei", "menu-file";      De = 0, 0, 0, 0 },
    @{ Name = "menu-printer";    Shot = "menu-drucker", "menu-printer"; De = 0, 0, 0, 0 },
    @{ Name = "context-menu";    Shot = "context-menu", "context-menu"; De = 0, 0, 0, 0 },
    @{ Name = "status-window";   Shot = "status", "status";             De = 0, 0, 0, 0 })
# setup-sections.png is made by hand (the share tab with a sample PC name, no printers of this PC), not here.

foreach ($lang in "de", "en") {
    $langDir = Join-Path $Dest $lang
    New-Item -ItemType Directory -Force $langDir | Out-Null
    foreach ($p in $pictures) {
        $shot = if ($lang -eq "de") { $p.Shot[0] } else { $p.Shot[1] }
        $c = if ($lang -eq "en" -and $p.En) { $p.En } else { $p.De }
        $file = Join-Path $Out "100-$lang\100-$lang-$shot.png"
        if (-not (Test-Path $file)) { "{0}\{1}.png  left out (no shot '{2}')" -f $lang, $p.Name, $shot; continue }
        $img = [Drawing.Image]::FromFile($file)
        if ($c[2] -eq 0) { $c = 0, 0, $img.Width, $img.Height }
        $w = $c[2] - $c[0]; $h = $c[3] - $c[1]
        $bmp = New-Object Drawing.Bitmap $w, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.DrawImage($img, (New-Object Drawing.Rectangle 0, 0, $w, $h), (New-Object Drawing.Rectangle $c[0], $c[1], $w, $h), 'Pixel')
        $bmp.Save([IO.Path]::GetFullPath((Join-Path $langDir "$($p.Name).png")), [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $img.Dispose()
        "{0}\{1}.png  {2}x{3}" -f $lang, $p.Name, $w, $h
    }
}
