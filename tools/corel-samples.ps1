# corel-samples.ps1 - makes CorelDRAW text samples and saves each one's clipboard copy as a dump (for the CMX reader).
#
# Uses the RUNNING CorelDRAW (COM "CorelDRAW.Application", CorelDRAW 2024 = .25) and the Windows clipboard:
# it creates one NEW document, builds each sample there (paragraph text in a frame, artistic text, one setting changed
# per sample), copies it (this replaces what is on the clipboard), saves the copy with clipboard-formats.ps1 -SaveData
# to <Folder>\corel-s-<name> (plus sample.txt saying what it is), deletes the sample, and at the end closes its
# document WITHOUT saving. Other open documents and files are never touched. No admin.
# Nobody should use CorelDRAW or the clipboard while it runs (about 3 s per sample).
#   powershell -NoProfile -File <repo>\tools\corel-samples.ps1 -List                 (names only, no CorelDRAW)
#   powershell -NoProfile -File <repo>\tools\corel-samples.ps1 -Only base             (one sample: a first test)
#   powershell -NoProfile -File <repo>\tools\corel-samples.ps1                        (all samples)
#   powershell -NoProfile -File <repo>\tools\corel-samples.ps1 -Folder <folder> -Only base,align-center
# A dump folder of the same name is replaced only when it was made by this script (has sample.txt).
param([string]$Folder, [string[]]$Only, [switch]$List, [string]$ProgId = 'CorelDRAW.Application.25')

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Folder) { $Folder = Join-Path $repo 'out\clipboard' }
$formats = Join-Path $PSScriptRoot 'clipboard-formats.ps1'

# ---------- the samples ----------
# Base: a paragraph text frame 40 x 20 mm, "Abc" paragraph mark "Defgh", Arial 12 pt, left aligned.
# Each sample: name, what it shows, and a script block that gets the page's layer and returns nothing.
# Corel's enums (CorelDRAW VBA): cdrAlignment 1 left, 2 right, 3 centre (checked in the CMX and EMF); cdrVerticalAlignment 0 top, 1 centre, 2 bottom.

function Frame($layer, [string]$text = "Abc`rDefgh", [double]$w = 40, [double]$h = 20, [string]$font = 'Arial', [double]$size = 12) {
    $s = $layer.CreateParagraphText(20, 80, 20 + $w, 80 - $h, $text)
    $st = $s.Text.Story
    $st.Font = $font
    $st.Size = $size
    $st.Alignment = 1
    return $s
}

function Artistic($layer, [string]$text, [double]$x = 20, [double]$y = 40, [string]$font = 'Arial', [double]$size = 12) {
    $s = $layer.CreateArtisticText($x, $y, $text)
    $st = $s.Text.Story
    $st.Font = $font
    $st.Size = $size
    return $s
}

# Whole labels (drawing and text in one copy), 62 x 29 mm with its top left corner at (20, 69).
function Label62($layer) {
    $r = $layer.CreateRectangle(20, 69, 82, 40)                      # frame
    $r.Outline.Width = 0.3
    $ln = $layer.CreateLineSegment(22, 58, 70, 58)                  # line under the title
    $ln.Outline.Width = 0.5
    $c = $layer.CreateEllipse2(76, 63, 3.5, 3.5)                    # logo: a filled circle
    $c.Fill.ApplyUniformFill($app.CreateRGBColor(0, 0, 0))
    $t = $layer.CreatePolygon(72.5, 52, 79.5, 45, 3, 1, 1, $false)  # and a filled triangle
    $t.Fill.ApplyUniformFill($app.CreateRGBColor(0, 0, 0))
    Artistic $layer 'Relais Modul 5V' 22 60 'Arial' 14 | Out-Null
    $f = Frame $layer "mit Optokoppler`rActive high/low" 40 12 'Arial' 9
    $f.Move(2, -12)
    $v = Artistic $layer 'Lot 42' 74 42 'Arial' 7
    $v.Rotate(90)
}

function LabelLayers($layer) {
    $r = $layer.CreateRectangle(20, 69, 82, 56)                      # black bar ...
    $r.Fill.ApplyUniformFill($app.CreateRGBColor(0, 0, 0))
    $w = Artistic $layer 'WHITE ON BLACK' 23 60 'Arial' 14          # ... with white text on it
    $w.Fill.UniformColor.RGBAssign(255, 255, 255)
    Artistic $layer 'Under the circle' 23 46 'Arial' 12 | Out-Null
    $c = $layer.CreateEllipse2(40, 47, 4, 4)                        # a grey circle drawn after (above) that text
    $c.Fill.ApplyUniformFill($app.CreateRGBColor(160, 160, 160))
}

# Vector samples: a rectangle with an outline of w mm and no fill; a fill in RGB without outline.
function VRect($layer, [double]$w) {
    $s = $layer.CreateRectangle2(20, 45, 30, 15)
    $s.Outline.Width = $w
    $s.Outline.Color.RGBAssign(0, 0, 0)
    $s.Fill.ApplyNoFill()
    return $s
}

function VFill($shape, [int]$r, [int]$g, [int]$b) {
    $shape.Outline.SetNoOutline()
    $shape.Fill.ApplyUniformFill($shape.Application.CreateRGBColor($r, $g, $b))
}

$samples = @(
    @{ n = 'base';            d = 'paragraph frame 40x20 mm, "Abc" CR "Defgh", Arial 12 pt, left';  b = { param($l) Frame $l | Out-Null } }
    @{ n = 'align-center';    d = 'base, alignment centre';                     b = { param($l) (Frame $l).Text.Story.Alignment = 3 } }
    @{ n = 'align-right';     d = 'base, alignment right';                      b = { param($l) (Frame $l).Text.Story.Alignment = 2 } }
    @{ n = 'linespacing150';  d = 'base, line spacing 150 % (of character height)'; b = { param($l) (Frame $l).Text.Story.LineSpacing = 150 } }
    @{ n = 'linespacing80';   d = 'base, line spacing 80 %';                    b = { param($l) (Frame $l).Text.Story.LineSpacing = 80 } }
    @{ n = 'charspacing20';   d = 'base, character spacing 20 %';               b = { param($l) (Frame $l).Text.Story.CharSpacing = 20 } }
    @{ n = 'charspacing-10';  d = 'base, character spacing -10 %';              b = { param($l) (Frame $l).Text.Story.CharSpacing = -10 } }
    @{ n = 'charspacing50';   d = 'base, character spacing 50 %';               b = { param($l) (Frame $l).Text.Story.CharSpacing = 50 } }
    @{ n = 'wordspacing200';  d = 'base, word spacing 200 %, text "Abc de" CR "Defgh hi"'; b = { param($l) (Frame $l "Abc de`rDefgh hi").Text.Story.WordSpacing = 200 } }
    @{ n = 'afterpara200';    d = 'base, spacing after paragraph 200 %';        b = { param($l) (Frame $l).Text.Story.ParaSpacingAfter = 200 } }
    @{ n = 'beforepara150';   d = 'base, spacing before paragraph 150 %';       b = { param($l) (Frame $l).Text.Story.ParaSpacingBefore = 150 } }
    @{ n = 'size20';          d = 'base, 20 pt';                                b = { param($l) (Frame $l).Text.Story.Size = 20 } }
    @{ n = 'bold-second';     d = 'base, only "Defgh" bold';                    b = { param($l) (Frame $l).Text.Story.Paragraphs.Item(2).Bold = $true } }
    @{ n = 'italic-first';    d = 'base, only "Abc" italic';                    b = { param($l) (Frame $l).Text.Story.Paragraphs.Item(1).Italic = $true } }
    @{ n = 'underline';       d = 'base, all underlined (single thin)';         b = { param($l) (Frame $l).Text.Story.Underline = 1 } }
    @{ n = 'underline-second'; d = 'base, only "Defgh" underlined (single thin)'; b = { param($l) (Frame $l).Text.Story.Paragraphs.Item(2).Underline = 1 } }
    @{ n = 'label-underline'; d = 'whole label: frame 62x29 mm, underlined title Arial 14, a second text with a 0.3 mm line 2 mm below it (stays a line)'; b = {
            param($l) $r = $l.CreateRectangle(20, 69, 82, 40); $r.Outline.Width = 0.3
            (Artistic $l 'Relais Modul' 23 60 'Arial' 14).Text.Story.Underline = 1
            Artistic $l 'Lot 42' 23 48 'Arial' 10 | Out-Null
            $ln = $l.CreateLineSegment(23, 46, 50, 46); $ln.Outline.Width = 0.3 } }
    @{ n = 'sizes-mixed';     d = 'base, "Defgh" 18 pt';                        b = { param($l) (Frame $l).Text.Story.Paragraphs.Item(2).Size = 18 } }
    @{ n = 'fonts-mixed';     d = 'base, "Defgh" Times New Roman';              b = { param($l) (Frame $l).Text.Story.Paragraphs.Item(2).Font = 'Times New Roman' } }
    @{ n = 'frame60x30';      d = 'paragraph frame 60x30 mm, text as base';     b = { param($l) Frame $l "Abc`rDefgh" 60 30 | Out-Null } }
    @{ n = 'wrap';            d = 'frame 40x20 mm, "Abc Defgh Ijklm Nopqr Stuvw" without CR (wraps)'; b = { param($l) Frame $l 'Abc Defgh Ijklm Nopqr Stuvw' | Out-Null } }
    @{ n = 'moved10';         d = 'base moved 10 mm to the right';              b = { param($l) (Frame $l).Move(10, 0) } }
    @{ n = 'rotated90';       d = 'base rotated 90 degrees';                    b = { param($l) (Frame $l).Rotate(90) } }
    @{ n = 'rotated30';       d = 'base rotated 30 degrees';                    b = { param($l) (Frame $l).Rotate(30) } }
    @{ n = 'rotated180';      d = 'base rotated 180 degrees';                   b = { param($l) (Frame $l).Rotate(180) } }
    @{ n = 'rotated270';      d = 'base rotated 270 degrees';                   b = { param($l) (Frame $l).Rotate(270) } }
    @{ n = 'artistic-rotated270'; d = 'artistic "Abc" CR "Defgh" CR "Ijk", centred, rotated 270 degrees (CorelDRAW has no paragraph spacing for artistic text)'; b = {
            param($l) $a = Artistic $l "Abc`rDefgh`rIjk"; $a.Text.Story.Alignment = 3; $a.Rotate(270) } }
    @{ n = 'valign-middle';   d = 'base, vertical alignment centre';            b = { param($l) (Frame $l).Text.Frame.VerticalAlignment = 1 } }
    @{ n = 'valign-bottom';   d = 'base, vertical alignment bottom';            b = { param($l) (Frame $l).Text.Frame.VerticalAlignment = 2 } }
    @{ n = 'artistic';        d = 'artistic text "Abc" CR "Defgh", Arial 12 pt'; b = { param($l) Artistic $l "Abc`rDefgh" | Out-Null } }
    @{ n = 'artistic-line';   d = 'artistic text "Abc Defgh", Arial 12 pt';     b = { param($l) Artistic $l 'Abc Defgh' | Out-Null } }
    @{ n = 'artistic-center'; d = 'artistic text "Abc" CR "Defgh", centred';     b = { param($l) (Artistic $l "Abc`rDefgh").Text.Story.Alignment = 3 } }
    @{ n = 'artistic-ls150';  d = 'artistic text "Abc" CR "Defgh", line spacing 150 %'; b = { param($l) (Artistic $l "Abc`rDefgh").Text.Story.LineSpacing = 150 } }
    @{ n = 'frame-plus-artistic'; d = 'base frame plus artistic "Xyz" to its right, both copied'; b = { param($l) Frame $l | Out-Null; Artistic $l 'Xyz' 70 70 | Out-Null } }
    @{ n = 'kerning';         d = 'artistic "AVATAR Toyota Wave", Arial 12 pt (kerning pairs)'; b = { param($l) Artistic $l 'AVATAR Toyota Wave' | Out-Null } }
    @{ n = 'kerning-franklin'; d = 'artistic "AVATAR Toyota Wave", ITC Franklin Gothic Std Bk Cp 13.21 pt'; b = { param($l) Artistic $l 'AVATAR Toyota Wave' 20 40 'ITC Franklin Gothic Std Bk Cp' 13.21 | Out-Null } }
    @{ n = 'franklin';        d = 'frame "LED MIX SET" CR "3mm, 120 STUECK", ITC Franklin Gothic Std Bk Cp 13.21 pt bold, centred, line spacing 87 %'; b = {
            param($l)
            $st = (Frame $l "LED MIX SET`r3mm, 120 ST$([char]0xDC)CK" 30 12 'ITC Franklin Gothic Std Bk Cp' 13.21).Text.Story
            $st.Bold = $true; $st.Alignment = 3; $st.LineSpacing = 87 } }
    @{ n = 'label-62x29';     d = 'whole label 62x29 mm: frame, line, logo-like circle and triangle, title, paragraph text, text turned 90'; b = { param($l) Label62 $l } }
    @{ n = 'label-text-on-shape'; d = 'filled black rectangle with white text on it, a grey circle on top of a second text'; b = { param($l) LabelLayers $l } }
    @{ n = 'umlauts';         d = 'base text "AEOEUE aeoeuess" in real umlauts, Arial 12 pt'; b = { param($l) Frame $l ("$([char]0xC4)$([char]0xD6)$([char]0xDC)`r$([char]0xE4)$([char]0xF6)$([char]0xFC)$([char]0xDF)") | Out-Null } }
    # Vector shapes (the picture path: outline widths, dashes, holes, fills, text as curves, groups, turned and mirrored).
    # A 30 x 15 mm rectangle unless said otherwise; outlines black, fills none unless said otherwise.
    @{ n = 'v-outline1';      d = 'rectangle 30x15 mm, outline 1 mm, no fill';  b = { param($l) VRect $l 1 | Out-Null } }
    @{ n = 'v-outline-scaled'; d = 'v-outline1 with "scale with object" on, then resized to 15x7.5 mm (expect 0.5 mm)'; b = {
            param($l) $s = VRect $l 1; $s.Outline.ScaleWithShape = $true; $s.SetSize(15, 7.5) } }
    @{ n = 'v-outline-fixed'; d = 'v-outline1 with "scale with object" off, then resized to 15x7.5 mm (expect 1 mm)'; b = {
            param($l) $s = VRect $l 1; $s.Outline.ScaleWithShape = $false; $s.SetSize(15, 7.5) } }
    @{ n = 'v-dashes';        d = 'v-outline1 with 0.5 mm dashed outline (CorelDRAW outline style 3)'; b = {
            param($l) $s = VRect $l 0.5; $s.Outline.Style = $l.Application.OutlineStyles.Item(3) } }
    @{ n = 'v-round';         d = 'rectangle 30x15 mm, corners 3 mm round, black fill, no outline'; b = {
            param($l) $s = $l.CreateRectangle2(20, 45, 30, 15, 3, 3, 3, 3); VFill $s 0 0 0 } }
    @{ n = 'v-hole';          d = 'black rectangle 30x15 mm with a round hole 8 mm (combined), no outline'; b = {
            param($l) $r = $l.CreateRectangle2(20, 45, 30, 15); $e = $l.CreateEllipse2(35, 52.5, 4, 4)
            $sr = $l.Application.CreateShapeRange(); $sr.Add($r); $sr.Add($e); VFill ($sr.Combine()) 0 0 0 } }
    @{ n = 'v-grey';          d = 'rectangle filled 50 % grey (RGB 128), no outline'; b = { param($l) VFill ($l.CreateRectangle2(20, 45, 30, 15)) 128 128 128 } }
    @{ n = 'v-gradient';      d = 'rectangle with a linear fountain fill black to white, no outline'; b = {
            param($l) $s = $l.CreateRectangle2(20, 45, 30, 15); $s.Outline.SetNoOutline()
            $a = $l.Application; $s.Fill.ApplyFountainFill($a.CreateRGBColor(0, 0, 0), $a.CreateRGBColor(255, 255, 255)) | Out-Null } }
    @{ n = 'v-knockout';      d = 'black rectangle 30x15 mm, a white 10x5 mm rectangle on top, no outlines'; b = {
            param($l) VFill ($l.CreateRectangle2(20, 45, 30, 15)) 0 0 0; VFill ($l.CreateRectangle2(30, 50, 10, 5)) 255 255 255 } }
    @{ n = 'v-white-outline'; d = 'black rectangle with a white 0.5 mm line across (as the logo swoosh)'; b = {
            param($l) VFill ($l.CreateRectangle2(20, 45, 30, 15)) 0 0 0; $c = $l.CreateLineSegment(22, 47, 48, 58)
            $c.Outline.Width = 0.5; $c.Outline.Color.RGBAssign(255, 255, 255) } }
    @{ n = 'v-text-curves';   d = 'artistic "Abc" Arial 24 pt converted to curves'; b = { param($l) (Artistic $l 'Abc' 20 45 'Arial' 24).ConvertToCurves() } }
    @{ n = 'v-group';         d = 'v-outline1 and a black circle 10 mm grouped'; b = {
            param($l) $sr = $l.Application.CreateShapeRange(); $sr.Add((VRect $l 1)); $e = $l.CreateEllipse2(65, 52.5, 5, 5); VFill $e 0 0 0; $sr.Add($e); $sr.Group() | Out-Null } }
    @{ n = 'v-mirrored';      d = '"Abc" as curves, flipped horizontally'; b = { param($l) $s = (Artistic $l 'Abc' 20 45 'Arial' 24); $s.ConvertToCurves(); $s.Flip(1) } }
    @{ n = 'v-rotated';       d = 'v-outline1 rotated 30 degrees'; b = { param($l) (VRect $l 1).Rotate(30) } }
    @{ n = 'v-small-text';    d = 'black rectangle and artistic "FUN ELECTRONIC COMPONENTS" Arial 3 pt below (text in a picture)'; b = {
            param($l) VFill ($l.CreateRectangle2(20, 50, 10, 10)) 0 0 0; Artistic $l 'FUN ELECTRONIC COMPONENTS' 20 46 'Arial' 3 | Out-Null } }
)

if ($Only) {
    $want = $Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ }
    $unknown = $want | Where-Object { $n = $_; -not ($samples | Where-Object { $_.n -eq $n }) }
    if ($unknown) { throw "unknown sample(s): $($unknown -join ', ') (see -List)" }
    $samples = $samples | Where-Object { $want -contains $_.n }
}
if ($List) { $samples | ForEach-Object { '{0,-22} {1}' -f $_.n, $_.d }; return }

# ---------- CorelDRAW ----------
$before = @(Get-Process CorelDRW -ErrorAction SilentlyContinue | ForEach-Object { $_.Id })
if ($before.Count -eq 0) { throw 'CorelDRAW is not running: start it first.' }
try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject($ProgId) }
catch {
    # CorelDRAW 2024 may not put itself into the running object table; its class factory then hands out the running
    # instance. A second CorelDRAW must not be used: it is closed again and the script stops.
    $app = New-Object -ComObject $ProgId
    $new = @(Get-Process CorelDRW -ErrorAction SilentlyContinue | Where-Object { $before -notcontains $_.Id })
    if ($new.Count -gt 0) {
        try { $app.Quit() } catch { }
        throw "COM started a second CorelDRAW (process $($new[0].Id)) instead of using the running one; it was told to quit. Stopped."
    }
}

New-Item -ItemType Directory -Force -Path $Folder | Out-Null
$doc = $app.CreateDocument()
$started = Get-Date
$ok = 0; $failed = @()
try {
    # millimetres: the cdrUnit value whose page width reads like a real page (A4 210, Letter 215.9, ...)
    $mm = $false
    foreach ($u in 3, 4, 2, 5, 6, 1, 7, 8) {
        $doc.Unit = $u
        $w = $doc.ActivePage.SizeWidth
        if ($w -gt 100 -and $w -lt 500 -and [math]::Abs($w - [math]::Round($w)) -lt 0.2 -or [math]::Abs($w - 215.9) -lt 0.1) { $mm = $true; break }
    }
    if (-not $mm) { throw 'could not set the document to millimetres' }
    $layer = $doc.ActiveLayer
    foreach ($s in $samples) {
        $target = Join-Path $Folder ('corel-s-' + $s.n)
        try {
            $doc.Activate()
            & $s.b $layer
            $all = $doc.ActivePage.Shapes.All()
            $all.CreateSelection()
            $all.Copy()
            Start-Sleep -Milliseconds 700
            if (Test-Path $target) {
                if (-not (Test-Path (Join-Path $target 'sample.txt'))) { throw "$target exists and was not made by this script" }
                Remove-Item -Recurse -Force $target
            }
            & powershell -NoProfile -File $formats -SaveData $target -Preview 60 | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "clipboard-formats.ps1 failed ($LASTEXITCODE)" }
            $info = @("sample: $($s.n)", "what: $($s.d)", "made: $(Get-Date -Format s) by tools/corel-samples.ps1 with $ProgId $($app.VersionMajor).$($app.VersionMinor)")
            [IO.File]::WriteAllLines((Join-Path $target 'sample.txt'), $info)
            Write-Host ("saved {0}" -f (Split-Path -Leaf $target))
            $ok++
        }
        catch {
            Write-Host ("FAILED {0}: {1}" -f $s.n, $_.Exception.Message)
            $failed += $s.n
        }
        finally {
            try { $doc.ActivePage.Shapes.All().Delete() } catch { }
        }
    }
}
finally {
    try { $doc.Dirty = $false } catch { }
    $doc.Close()
}
Write-Host ("{0} saved, {1} failed{2}, {3:0} s" -f $ok, $failed.Count, $(if ($failed) { ' (' + ($failed -join ', ') + ')' } else { '' }), ((Get-Date) - $started).TotalSeconds)
if ($failed) { exit 1 }
