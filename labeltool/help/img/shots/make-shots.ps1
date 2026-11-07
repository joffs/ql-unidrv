# Takes the help's window screenshots of the real QL Label Maker (helpshots.cs, through out\tools\uitest\deskrun.exe on a
# desktop of its own: the user's mouse and focus stay untouched) at 100 % and 150 %, German and English, and cuts the
# pictures named below into ..\de and ..\en.
#   make-shots.ps1 [-App <QlLabelMaker.exe>] [-Out <folder>] [-CropOnly]
# Needs a screen at 150 % for the 150 % pictures and the printer "Brother QL-800 (Unidrv)" (its loaded roll shows in
# the status bar; nothing is printed).
param(
    [string]$App = "$PSScriptRoot\..\..\..\..\out\tools\QlLabelMaker.exe",
    [string]$Out = "$PSScriptRoot\..\..\..\..\out\help-shots",
    [switch]$CropOnly)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$Out = [IO.Path]::GetFullPath($Out)

if (-not $CropOnly) {
    $csc = "$env:WINDIR\Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    $deskrun = "$PSScriptRoot\..\..\..\..\out\tools\uitest\deskrun.exe"
    if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $exe = Join-Path $Out "helpshots.exe"
    & $csc /nologo /target:exe /platform:anycpu /codepage:65001 "/out:$exe" "/win32manifest:$PSScriptRoot\..\..\..\app.manifest" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\helpshots.cs"
    if ($LASTEXITCODE -ne 0) { throw "helpshots.cs: build failed" }
    Copy-Item "$PSScriptRoot\..\..\..\app.config" "$exe.config" -Force
    foreach ($scale in 100, 150) {
        foreach ($lang in "de", "en") {
            & $deskrun $exe ([IO.Path]::GetFullPath($App)) "$PSScriptRoot\labels" $Out $lang $scale
            if ($LASTEXITCODE -ne 0) { throw "helpshots $scale $lang`: exit code $LASTEXITCODE" }
        }
    }
}

# name, screenshot (scale, shot), crop left, top, right, bottom; an "en" crop only where the English window differs.
$pictures = @(
    @{ Name = "overview-window";        Shot = "100", "window";             De = 0, 0, 1100, 680 },
    @{ Name = "first-label-top-row";    Shot = "150", "window";             De = 26, 42, 1160, 106 },
    @{ Name = "text-panel";             Shot = "150", "text";               De = 805, 150, 1150, 410 },
    @{ Name = "printing-copies";        Shot = "150", "window";             De = 935, 72, 1160, 104; En = 958, 72, 1160, 104 },
    @{ Name = "printing-roll";          Shot = "100", "window";             De = 605, 651, 802, 672; En = 659, 651, 842, 672 },
    @{ Name = "view-direction";         Shot = "100", "window";             De = 745, 200, 978, 225 },
    @{ Name = "view-zoom";              Shot = "100", "window";             De = 802, 651, 1080, 672; En = 834, 651, 1080, 672 },
    @{ Name = "pictures-panel";         Shot = "100", "picture";            De = 742, 140, 1090, 425 },
    @{ Name = "codes-qr-panel";         Shot = "100", "qr";                 De = 742, 140, 1085, 465 },
    @{ Name = "codes-quiet-zone";       Shot = "100", "qr-zone";            De = 265, 242, 530, 505 },
    @{ Name = "arrange-several";        Shot = "100", "three";              De = 130, 135, 1000, 440 },
    @{ Name = "length-label-box";       Shot = "100", "length";             De = 255, 61, 637, 87 },
    @{ Name = "length-canvas";          Shot = "100", "length";             De = 185, 150, 640, 640 },
    @{ Name = "length-panel";           Shot = "100", "length";             De = 742, 255, 1090, 334 },
    @{ Name = "length-auto";            Shot = "100", "length-auto";        De = 742, 255, 1090, 306 },
    @{ Name = "length-error";           Shot = "100", "length-error";       De = 742, 255, 1090, 346; En = 742, 255, 1090, 331 },
    @{ Name = "length-diecut";          Shot = "100", "length-diecut";      De = 742, 256, 1010, 281 },
    @{ Name = "invert-label";           Shot = "100", "invert-none";        De = 150, 155, 672, 636 },
    @{ Name = "invert-text-panel";      Shot = "100", "invert-text";        De = 742, 266, 1090, 398; En = 742, 252, 1090, 356 },
    @{ Name = "invert-qr-panel";        Shot = "100", "invert-qr";          De = 742, 140, 1000, 276 },
    @{ Name = "lines-selected";         Shot = "100", "line-selected";      De = 175, 255, 455, 435 },
    @{ Name = "lines-dragging";         Shot = "100", "line-dragging";      De = 175, 255, 455, 435 },
    @{ Name = "lines-panel";            Shot = "100", "line-selected";      De = 742, 140, 1015, 282 },
    @{ Name = "lines-outside";          Shot = "100", "lineout-selected";   De = 330, 285, 720, 465 },
    @{ Name = "codes-barcode-panel";    Shot = "100", "code-c128";          De = 742, 140, 1090, 490 },
    @{ Name = "codes-gs1-label";        Shot = "100", "code-gs1";           De = 185, 390, 625, 530 },
    @{ Name = "codes-errors-dialog";    Shot = "100", "errors-dialog";      De = 0, 0, 0, 0 },
    @{ Name = "codes-gs1-hint";         Shot = "100", "code-gs1-hint";      De = 742, 140, 1090, 330 },
    @{ Name = "codes-text-shrinks";     Shot = "100", "code-gs1-hint";      De = 175, 390, 420, 525 },
    @{ Name = "rotate-grip";            Shot = "100", "reset-before";       De = 400, 382, 680, 522 },
    @{ Name = "rotate-angle";           Shot = "150", "reset-before";       De = 812, 503, 1137, 533; En = 812, 463, 1137, 493 },
    @{ Name = "arrange-toolbar";        Shot = "150", "reset-before";       De = 95, 108, 566, 137 },
    @{ Name = "rotate-reset-menu";      Shot = "100", "reset-context-menu"; De = 0, 0, 0, 0 })

foreach ($lang in "de", "en") {
    $dest = Join-Path $PSScriptRoot "..\$lang"
    foreach ($p in $pictures) {
        $scale, $shot = $p.Shot
        $c = if ($lang -eq "en" -and $p.En) { $p.En } else { $p.De }
        $img = [Drawing.Image]::FromFile((Join-Path $Out "$scale-$lang\$scale-$lang-$shot.png"))
        if ($c[2] -eq 0) { $c = 0, 0, $img.Width, $img.Height }   # the whole screenshot (a menu)
        $w = $c[2] - $c[0]; $h = $c[3] - $c[1]
        $bmp = New-Object Drawing.Bitmap $w, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.DrawImage($img, (New-Object Drawing.Rectangle 0, 0, $w, $h), (New-Object Drawing.Rectangle $c[0], $c[1], $w, $h), 'Pixel')
        $bmp.Save([IO.Path]::GetFullPath((Join-Path $dest "$($p.Name).png")), [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $img.Dispose()
        "{0}\{1}.png  {2}x{3}" -f $lang, $p.Name, $w, $h
    }
}
