<#
.SYNOPSIS
  Builds the QL printer test kit for owners of models we have no printer for (tools/testkit/README.md):
  out\testkit\kit\ (qltest.ps1, README.md, rolls.tsv, streams\<model>\*.prn + expected pictures + cases.tsv) and zips
  (all models, and one per model).

.DESCRIPTION
  The test streams are our encoder's output: replay_test (driver\test, built by driver\build.cmd) decodes
  Brother's pages recorded for the kit (reference\captures\testkit, neutral text "QL test") and re-encodes them exactly as our driver does - in the USB form (no
  compression) and, for the models that compress on network ports, also the compressed form. The pictures
  are the captures' PNG previews (what Brother's driver printed). No admin rights needed.

  Usage: tools\testkit\make-testkit.ps1 [-ReplayTest <path to replay_test.exe>] [-Out <folder>]
#>
param([string]$ReplayTest = "", [string]$Out = "")
$ErrorActionPreference = "Stop"
$repo = Split-Path (Split-Path $PSScriptRoot)
if (-not $Out) { $Out = Join-Path $repo "out\testkit" }
if (-not $ReplayTest) {
    $native = if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64") { "out\obj\replay_test.exe" } else { "out\obj\x64\replay_test.exe" }
    $ReplayTest = Join-Path $repo $native
}
if (-not (Test-Path $ReplayTest)) { throw "replay_test not found ($ReplayTest): run build.cmd first or pass -ReplayTest." }

# Kit model name, capture folder, replay_test model argument, 300 x 600, compresses on network ports.
$models = @(
    @{ Name = "QL-500"; Dir = "ql500"; Arg = "500"; HighRes = $false; Pack = $false },
    @{ Name = "QL-550"; Dir = "ql550"; Arg = "550"; HighRes = $false; Pack = $false },
    @{ Name = "QL-560"; Dir = "ql560"; Arg = "560"; HighRes = $false; Pack = $false },
    @{ Name = "QL-570"; Dir = "ql570"; Arg = "570"; HighRes = $true; Pack = $false },
    @{ Name = "QL-580N"; Dir = "ql580n"; Arg = "580"; HighRes = $true; Pack = $true },
    @{ Name = "QL-600"; Dir = "ql600"; Arg = "600"; HighRes = $true; Pack = $false },
    @{ Name = "QL-650TD"; Dir = "ql650td"; Arg = "650"; HighRes = $false; Pack = $false },
    @{ Name = "QL-700"; Dir = "ql700"; Arg = "700"; HighRes = $true; Pack = $false },
    @{ Name = "QL-710W"; Dir = "ql710w"; Arg = "710"; HighRes = $true; Pack = $true },
    @{ Name = "QL-720NW"; Dir = "ql720nw"; Arg = "720"; HighRes = $true; Pack = $true },
    @{ Name = "QL-800"; Dir = "ql800"; Arg = "800"; HighRes = $true; Pack = $false },
    @{ Name = "QL-810W"; Dir = "ql810w"; Arg = "810"; HighRes = $true; Pack = $true },
    @{ Name = "QL-820NWB"; Dir = "ql820nwb"; Arg = "820"; HighRes = $true; Pack = $true },
    @{ Name = "QL-1050"; Dir = "ql1050"; Arg = "1050"; HighRes = $false; Pack = $true },
    @{ Name = "QL-1060N"; Dir = "ql1060n"; Arg = "1060"; HighRes = $false; Pack = $true },
    @{ Name = "QL-1100"; Dir = "ql1100"; Arg = "1100"; HighRes = $false; Pack = $true },
    @{ Name = "QL-1110NWB"; Dir = "ql1110nwb"; Arg = "1110"; HighRes = $false; Pack = $true },
    @{ Name = "QL-1115NWB"; Dir = "ql1115nwb"; Arg = "1115"; HighRes = $false; Pack = $true }
)
# The DK roll numbers (Europe and US) per paper: rolls.tsv, written by rolls.py (here, not in driver\gen: the
# driver's version hash covers that folder); also copied into the kit (qltest.ps1 names the loaded roll with them).
python -I (Join-Path $PSScriptRoot "rolls.py")
if ($LASTEXITCODE) { throw "rolls.py failed." }
$rollsFile = Join-Path $PSScriptRoot "rolls.tsv"
$rolls = @{}
Get-Content $rollsFile | Where-Object { $_ -and -not $_.StartsWith("#") } | ForEach-Object { $f = $_ -split "`t"; $rolls[$f[1]] = $f }
# "62 mm continuous (e.g. DK-22205, US: DK-2205)": a roll with its DK numbers (paper: the name in rolls.tsv);
# just the text for a paper without a known DK roll (54 x 29 mm, 62 x 60 mm, the direct-thermal papers).
function Roll([string]$paper, [string]$text) {
    $r = $rolls[$paper]
    if (-not $r) { return $text }
    $dk = $(if ($r[4] -eq "1") { "e.g. " } else { "" }) + $r[2] + $(if ($r[3]) { ", US: " + $r[3] } else { "" })
    return "$text ($dk)"
}

# Test cases: capture name, roll the tester needs, two-colour, what the label is. Cases without a capture for
# a model are left out for it.
$cases = @(
    @{ File = "62mm-continuous_default-length"; Roll = (Roll "62mm" "62 mm continuous"); Text = "one 62 mm label at the default length, cut after it" },
    @{ File = "62mm_cut-every-2"; Roll = (Roll "62mm" "62 mm continuous"); Text = "three labels in one job; cut after label 2 and at the end (where the model has these options)" },
    @{ File = "62mm_highres"; Roll = (Roll "62mm" "62 mm continuous"); Text = "one label in 300 x 600 dpi (high resolution)"; HighRes = $true },
    @{ File = "62mm_mirror"; Roll = (Roll "62mm" "62 mm continuous"); Text = "one label, mirrored (text reads backwards)" },
    @{ File = "62mm_2color"; Roll = (Roll "62mm black/red" "62 mm black/red"); Text = "black border, red text and bars"; TwoColor = $true },
    @{ File = "29x90-diecut"; Roll = (Roll "29mm x 90mm" "29 x 90 mm die-cut"); Text = "one 29 x 90 mm label" },
    @{ File = "12mm-dia_diecut"; Roll = (Roll "12mm Dia" "12 mm round"); Text = "one 12 mm round label" },
    @{ File = "102mm-continuous_default-length"; Roll = (Roll "102mm" "102 mm continuous"); Text = "one 102 mm label at the default length" },
    @{ File = "103mm-continuous"; Roll = (Roll "103mm" "103 mm continuous"); Text = "one 103 mm label at the default length" },
    @{ File = "102x51-diecut"; Roll = (Roll "102mm x 51mm" "102 x 51 mm die-cut"); Text = "one 102 x 51 mm label" },
    @{ File = "102x152-diecut"; Roll = (Roll "102mm x 152mm" "102 x 152 mm die-cut"); Text = "one 102 x 152 mm label" },
    @{ File = "103x164-diecut"; Roll = (Roll "103mm x 164mm" "103 x 164 mm die-cut"); Text = "one 103 x 164 mm label" }
)
# One label per further Brother paper of the model (reference\captures\testkit): roll and text from the
# capture name, "50mm-continuous_default-length", "54x29-diecut", "24mm-dia_diecut", "dt-" = direct-thermal.
function Paper-Case([string]$file) {
    $dt = $file.StartsWith("dt-"); $n = $file -replace "^dt-", ""
    $pre = if ($dt) { "DT " } else { "" }; $kind = if ($dt) { "direct-thermal " } else { "" }
    if ($n -match '^(\d+)mm-continuous_default-length$') {
        return @{ File = $file; Roll = (Roll "$pre$($Matches[1])mm" "$($Matches[1]) mm ${kind}continuous"); Text = "one $($Matches[1]) mm label at the default length" }
    }
    if ($n -match '^(\d+)x(\d+)-diecut$') {
        return @{ File = $file; Roll = (Roll "$pre$($Matches[1])mm x $($Matches[2])mm" "$($Matches[1]) x $($Matches[2]) mm ${kind}die-cut"); Text = "one $($Matches[1]) x $($Matches[2]) mm label" }
    }
    if ($n -match '^(\d+)mm-dia_diecut$') {
        return @{ File = $file; Roll = (Roll "$pre$($Matches[1])mm Dia" "$($Matches[1]) mm ${kind}round"); Text = "one $($Matches[1]) mm round label" }
    }
    return $null
}

# The capture previews (tools/prn_dump.py --png) show the raster as the head receives it: each line right to
# left, one line per row, the whole head width. The printed label is that picture mirrored left-right; at
# 300 x 600 dpi a line is half as tall, so the picture is also halved in height. It is cut to the printed part
# (the test pages have a border), so a 29 mm label doesn't look like a 62 mm one.
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class InkColumns {
    // First and last column with a dark pixel (or 0 and width-1 if there is none).
    public static int[] Range(Bitmap b) {
        var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var px = new int[d.Stride / 4 * b.Height];
        Marshal.Copy(d.Scan0, px, 0, px.Length);
        b.UnlockBits(d);
        int first = b.Width, last = -1, w = d.Stride / 4;
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++) {
                int c = px[y * w + x];
                if (((c >> 16) & 0xFF) + ((c >> 8) & 0xFF) + (c & 0xFF) < 384) { if (x < first) first = x; if (x > last) last = x; }
            }
        return last < 0 ? new[] { 0, b.Width - 1 } : new[] { first, last };
    }
}
"@
function Save-PrintedView([string]$src, [string]$dst, [bool]$highRes) {
    $img = [System.Drawing.Image]::FromFile($src)
    try {
        $full = New-Object System.Drawing.Bitmap $img
        $ink = [InkColumns]::Range($full)
        $full.Dispose()
        $pad = 8
        $x0 = [Math]::Max(0, $ink[0] - $pad); $x1 = [Math]::Min($img.Width - 1, $ink[1] + $pad)
        $w = $x1 - $x0 + 1
        $h = if ($highRes) { [int][Math]::Ceiling($img.Height / 2) } else { $img.Height }
        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        try {
            $g.Clear([System.Drawing.Color]::White)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $w, $h), $x0, 0, $w, $img.Height, [System.Drawing.GraphicsUnit]::Pixel)
        } finally { $g.Dispose() }
        $bmp.RotateFlip([System.Drawing.RotateFlipType]::RotateNoneFlipX)
        $bmp.Save($dst, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
    } finally { $img.Dispose() }
}

$kit = Join-Path $Out "kit"
$tmp = Join-Path $Out "tmp"
# Empty the old kit: files must go; an empty folder that is still "in use" on the share (a window that was open
# there, released late by the file server) is simply reused.
foreach ($d in $kit, $tmp) {
    if (-not (Test-Path $d)) { continue }
    $open = @(Get-ChildItem $d -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        try { Remove-Item $_.FullName -Force -ErrorAction Stop } catch { $_.TargetObject } })
    if ($open) {
        throw "Cannot delete the old files in $d - one is still open (a picture viewer, or a PowerShell window or " +
              "Explorer in that folder, also on another PC through the share). Close it and run make-testkit.ps1 again."
    }
    Get-ChildItem $d -Recurse -Directory -Force -ErrorAction SilentlyContinue | Sort-Object { $_.FullName.Length } -Descending |
        ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }
}
New-Item -ItemType Directory -Force $kit, $tmp | Out-Null
Copy-Item (Join-Path $PSScriptRoot "qltest.ps1"), (Join-Path $PSScriptRoot "README.md"), $rollsFile $kit

$summary = @()
foreach ($m in $models) {
    $captures = Join-Path $repo ("reference\captures\testkit\" + $m.Dir)
    $usb = Join-Path $tmp ($m.Name + "-usb"); $net = Join-Path $tmp ($m.Name + "-net")
    New-Item -ItemType Directory -Force $usb, $net | Out-Null
    # Print priority Quality: at full speed fine bars come out ragged (seen on the QL-800, 2026-10-19).
    # A case the encoder doesn't reproduce byte for byte stays out of the kit (with a warning): the kit must only
    # send what Brother's driver sends.
    $replay = @(& $ReplayTest $captures $m.Arg $usb usb quality)
    if ($m.Pack) { $replay += & $ReplayTest $captures $m.Arg $net net quality }
    $failed = @($replay | Where-Object { $_ -match '^FAIL (\S+?)\.prn' } | ForEach-Object { $Matches[1] } | Select-Object -Unique)
    if ($LASTEXITCODE -and -not $failed) { throw "replay_test failed for $($m.Name): $($replay | Select-Object -Last 3)" }
    foreach ($f in $failed) { Write-Warning "$($m.Name): $f left out - replay_test doesn't reproduce Brother's capture." }

    $dest = Join-Path $kit ("streams\" + $m.Name)
    New-Item -ItemType Directory -Force $dest | Out-Null
    $rows = @("# file`troll`ttwo-colour`twhat it is (made by tools/testkit/make-testkit.ps1)")
    $known = @($cases | ForEach-Object { $_.File })
    $papers = @(Get-ChildItem $usb -Filter *.prn | ForEach-Object { $_.BaseName } | Where-Object { $known -notcontains $_ } |
        ForEach-Object { Paper-Case $_ } | Where-Object { $_ } |
        Sort-Object { $_.File.StartsWith("dt-") }, { if ($_.File -match "continuous") { 0 } elseif ($_.File -match "dia") { 2 } else { 1 } },
                    { [int]($_.File -replace "^(dt-)?(\d+).*", '$2') }, { [int]($_.File -replace "^(dt-)?\d+x?(\d*).*", '0$2') })
    foreach ($c in $cases + $papers) {
        $src = Join-Path $usb ($c.File + ".prn")
        if (-not (Test-Path $src) -or $failed -contains $c.File) { continue }
        if ($c.HighRes -and -not $m.HighRes) { continue }
        $two = if ($c.TwoColor) { "1" } else { "0" }
        Copy-Item $src (Join-Path $dest ($c.File + ".prn"))
        $png = Join-Path $captures ($c.File + ".png")
        if (Test-Path $png) { Save-PrintedView $png (Join-Path $dest ($c.File + ".png")) ([bool]$c.HighRes) }
        $rows += "$($c.File).prn`t$($c.Roll)`t$two`t$($c.Text) (USB form, as our driver sends it on USB; print priority Quality)"
        if ($m.Pack -and $c.File -eq "62mm-continuous_default-length") {
            Copy-Item (Join-Path $net ($c.File + ".prn")) (Join-Path $dest ($c.File + "_compressed.prn"))
            $rows += "$($c.File)_compressed.prn`t$($c.Roll)`t0`tthe same label compressed (as our driver sends it over the network)"
        }
    }
    $rows | Set-Content -Path (Join-Path $dest "cases.tsv") -Encoding UTF8
    $summary += "{0,-10} {1} test streams" -f $m.Name, ($rows.Count - 1)
}
Remove-Item $tmp -Recurse -Force
# The kit's build date, which qltest.ps1 puts into every report (the zip name carries the same stamp).
$stamp = Get-Date -Format yyyyMMdd
Set-Content -Path (Join-Path $kit "kit-version.txt") -Value $stamp -Encoding ASCII

# Zips: everything, and one per model (qltest.ps1 + README + that model's streams). The files get a fixed time, so
# a zip is byte-identical as long as its content is (the public repository carries the zips; no churn in git).
$fixedTime = [datetime]"2026-01-01T00:00:00"
function Set-FixedTime([string]$dir) {
    Get-ChildItem $dir -Recurse -Force | ForEach-Object { $_.LastWriteTime = $fixedTime }
}
Set-FixedTime $kit
Get-ChildItem $Out -Filter "ql-testkit-*.zip" | Remove-Item -Force
Compress-Archive -Path (Join-Path $kit "*") -DestinationPath (Join-Path $Out "ql-testkit-all-$stamp.zip")
foreach ($m in $models) {
    $one = Join-Path $Out "one"
    if (Test-Path $one) { Remove-Item $one -Recurse -Force }
    New-Item -ItemType Directory -Force (Join-Path $one "streams") | Out-Null
    Copy-Item (Join-Path $kit "qltest.ps1"), (Join-Path $kit "README.md"), (Join-Path $kit "rolls.tsv"), (Join-Path $kit "kit-version.txt") $one
    Copy-Item (Join-Path $kit ("streams\" + $m.Name)) (Join-Path $one "streams") -Recurse
    Set-FixedTime $one
    Compress-Archive -Path (Join-Path $one "*") -DestinationPath (Join-Path $Out "ql-testkit-$($m.Name)-$stamp.zip")
    Remove-Item $one -Recurse -Force
}
@"
QL printer test kit - what is in this folder (built $(Get-Date -Format 'yyyy-MM-dd HH:mm'), details: tools\testkit\README.md)

kit\                             the built kit. Try it yourself (printer on USB, roll loaded):
                                 powershell -ExecutionPolicy Bypass -File kit\qltest.ps1
ql-testkit-<model>-$stamp.zip    send this to an owner of that model; they unzip it and run qltest.ps1 (see its README.md)
ql-testkit-all-$stamp.zip        all $($models.Count) models in one zip
reports\                         reports of runs started from tools\testkit; put returned reports here too

After changing the encoder: run build.cmd, then tools\testkit\make-testkit.ps1 again (rebuilds this folder).
Record results: docs\models.md section 3/4 and docs\differences-from-brother.md.
"@ | Set-Content -Path (Join-Path $Out "HOW-TO.txt") -Encoding UTF8
$summary
Get-ChildItem $Out -Filter "ql-testkit-*.zip" | ForEach-Object { "{0,-36} {1,8:N0} KB" -f $_.Name, ($_.Length / 1KB) }
