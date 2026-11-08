# Takes the help's window screenshots of the real QL Label Maker (helpshots.cs, through out\tools\uitest\deskrun.exe on a
# desktop of its own: the user's mouse and focus stay untouched) at 100 % and 150 %, German and English, and cuts the
# pictures named below into ..\de and ..\en.
#   make-shots.ps1 [-App <QlLabelMaker.exe>] [-Out <folder>] [-Dest <folder>] [-Scales 100,150] [-Printer <name>] [-CropOnly]
# -Dest: where the pictures go (<Dest>\de, <Dest>\en; default: the help's img folder). -Scales: the scalings to shoot;
# pictures of a scaling not shot are left out. Needs a screen at 150 % for the 150 % pictures (without one they are
# left out with a warning) and an installed QL printer, "Brother QL-800 (Unidrv)" or -Printer (only named: the roll in
# the status bar is always "62 mm continuous", helpshots.cs FixedRoll; nothing is printed). Run by
# labeltool\test\help-shots.ps1, which compares the result with the help's pictures.
param(
    [string]$App = "$PSScriptRoot\..\..\..\..\out\tools\QlLabelMaker.exe",
    [string]$Out = "$PSScriptRoot\..\..\..\..\out\help-shots",
    [string]$Dest = "$PSScriptRoot\..",
    [string[]]$Scales = @("100", "150"),
    [string]$Printer = "Brother QL-800 (Unidrv)",
    [switch]$CropOnly)

$ErrorActionPreference = "Stop"
# "100,150" arrives as one string through powershell -File: split it.
[int[]]$Scales = @($Scales | ForEach-Object { "$_" -split "[, ]+" } | Where-Object { $_ } | ForEach-Object { [int]$_ })
Add-Type -AssemblyName System.Drawing
$Out = [IO.Path]::GetFullPath($Out)

if (-not $CropOnly) {
    $csc = "$env:WINDIR\Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    # A program older than the last commit to labeltool\src may not read the labels here (a hang on its "newer
    # version" message, 2026-11-08): stop with a hint instead.
    $srcTime = try { git -C "$PSScriptRoot\..\..\.." log -1 --format=%ct -- src 2>$null } catch { $null }
    if ($srcTime -and (Test-Path $App) -and
        (Get-Item $App).LastWriteTimeUtc -lt [DateTimeOffset]::FromUnixTimeSeconds([long]$srcTime).UtcDateTime) {
        throw "$App is older than the last commit to labeltool\src: run labeltool\build.cmd, or pass a build of HEAD (help-shots.ps1 -Tools)"
    }
    $deskrun = "$PSScriptRoot\..\..\..\..\out\tools\uitest\deskrun.exe"
    if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
    New-Item -ItemType Directory -Force $Out | Out-Null
    $exe = Join-Path $Out "helpshots.exe"
    & $csc /nologo /target:exe /platform:anycpu /codepage:65001 "/out:$exe" "/win32manifest:$PSScriptRoot\..\..\..\app.manifest" `
        /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\helpshots.cs"
    if ($LASTEXITCODE -ne 0) { throw "helpshots.cs: build failed" }
    Copy-Item "$PSScriptRoot\..\..\..\app.config" "$exe.config" -Force
    foreach ($scale in $Scales) {
        foreach ($lang in "de", "en") {
            $log = & $deskrun $exe ([IO.Path]::GetFullPath($App)) "$PSScriptRoot\labels" $Out $lang $scale $Printer
            if ($LASTEXITCODE -ne 0) {
                # exit code 3: a step hung (helpshots.cs watchdog); its log names the step and the open windows' texts
                $log | Where-Object { $_ -match "FAILED|^  " } | ForEach-Object { "helpshots $scale $lang`: $_" }
                throw "helpshots $scale $lang`: exit code $LASTEXITCODE"
            }
            $log | Where-Object { $_ -match "FAILED|^screen " } | ForEach-Object { "helpshots $scale $lang`: $_" }
            # no screen at this scaling: the shots were taken at another one and their crops would be wrong
            $screen = @($log | Where-Object { $_ -match "^screen .* dpi \d+$" })[0]
            if ($screen -and $screen -match "dpi (\d+)$" -and [int]$Matches[1] -ne $scale * 96 / 100) {
                Write-Warning "no screen at $scale %: the $scale % pictures are left out"
                Remove-Item -Recurse -Force (Join-Path $Out "$scale-$lang")
            }
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
    @{ Name = "pictures-panel";         Shot = "100", "picture";            De = 742, 140, 1090, 460 },
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
    @{ Name = "rotate-reset-menu";      Shot = "100", "reset-context-menu"; De = 0, 0, 0, 0 },
    @{ Name = "view-tools";             Shot = "100", "tools";              De = 8, 98, 82, 392 },
    @{ Name = "view-tools-flyout";      Shot = "100", "tools-flyout";       De = 8, 128, 272, 300 },
    @{ Name = "shapes-label";           Shot = "100", "shape-star";         De = 178, 262, 616, 392 },
    @{ Name = "shapes-panel";           Shot = "100", "shape-star";         De = 742, 140, 1000, 388 },
    @{ Name = "text-runs-panel";        Shot = "100", "runs";               De = 742, 135, 1070, 455; En = 742, 135, 1070, 430 },
    @{ Name = "edit-menu";              Shot = "100", "edit-menu";          De = 0, 0, 0, 0 },
    @{ Name = "edit-canvas-menu";       Shot = "100", "canvas-menu";        De = 0, 0, 0, 0 })

foreach ($lang in "de", "en") {
    $langDir = Join-Path $Dest $lang
    New-Item -ItemType Directory -Force $langDir | Out-Null
    foreach ($p in $pictures) {
        $scale, $shot = $p.Shot
        if (-not (Test-Path (Join-Path $Out "$scale-$lang\$scale-$lang-$shot.png"))) { "{0}\{1}.png  left out (no {2} % shot)" -f $lang, $p.Name, $scale; continue }
        $c = if ($lang -eq "en" -and $p.En) { $p.En } else { $p.De }
        $img = [Drawing.Image]::FromFile((Join-Path $Out "$scale-$lang\$scale-$lang-$shot.png"))
        if ($c[2] -eq 0) { $c = 0, 0, $img.Width, $img.Height }   # the whole screenshot (a menu)
        $w = $c[2] - $c[0]; $h = $c[3] - $c[1]
        $bmp = New-Object Drawing.Bitmap $w, $h
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.DrawImage($img, (New-Object Drawing.Rectangle 0, 0, $w, $h), (New-Object Drawing.Rectangle $c[0], $c[1], $w, $h), 'Pixel')
        $bmp.Save([IO.Path]::GetFullPath((Join-Path $langDir "$($p.Name).png")), [Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose(); $img.Dispose()
        "{0}\{1}.png  {2}x{3}" -f $lang, $p.Name, $w, $h
    }
}
