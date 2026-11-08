# Help pictures per release: takes all screenshots of the two help manuals again (help\img\shots\make-shots.ps1 for
# QL Label Maker, help\formats\img\shots\make-shots.ps1 for QL Label Formats; deskrun, a desktop of its own: the user's
# mouse and focus stay untouched; light, German and English; nothing is printed or saved) into
# out\help-shots\<name>\, and compares every picture with the one in the help.
# Result: out\help-shots\<name>\report.txt and index.html (old | new | difference for every changed picture).
# Changes nothing in the help unless -Apply is given: then changed and new pictures are copied over the help's
# (never deleted; commit them yourself, with a pathspec).
#   powershell -File labeltool\test\help-shots.ps1 [-Name <0.20.0>] [-Scales 100,150] [-Printer <name>] [-Tools <folder>] [-CropOnly] [-Apply]
# -Tools: the folder with QlLabelMaker.exe and QlLabelFormats.exe (default out\tools; e.g. a build of HEAD made with
# QL_TOOLS_OUT, when out\tools is older than the programs).
# -CropOnly: cut and compare again from the screenshots of an earlier run with the same -Name (after a crop change).
# Output under $env:QL_TEST_OUT when set (one folder per session), else under out\.
# Needs: the built programs (labeltool\build.cmd), out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), an installed
# QL printer ("Brother QL-800 (Unidrv)" or -Printer; only named, its roll is always "62 mm continuous"), and for the
# 150 % pictures a screen at 150 % (without one they are left out and reported as such).
# Exit code: 0 when every picture is unchanged, 1 when pictures changed or are missing, 2 when a step failed.
param(
    [string]$Name = (Get-Date -Format "yyyyMMdd-HHmm"),
    [string[]]$Scales = @("100", "150"),
    [string]$Printer = "Brother QL-800 (Unidrv)",
    [string]$Tools = "",
    [switch]$CropOnly,
    [switch]$Apply)

$ErrorActionPreference = "Stop"
# "100,150" arrives as one string through powershell -File: split it.
[int[]]$Scales = @($Scales | ForEach-Object { "$_" -split "[, ]+" } | Where-Object { $_ } | ForEach-Object { [int]$_ })
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$help = Join-Path $root "labeltool\help"
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "help-shots\$Name"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\shots.log" -ErrorAction SilentlyContinue
Add-Type -AssemblyName System.Drawing

# Pictures made by hand (their make-shots.ps1 doesn't take them): listed as "manual", never compared or replaced.
$manual = @("formats\setup-sections.png")

# The two manuals: where make-shots.ps1 is, where its pictures live, where the new ones go.
$sets = @(
    @{ Id = "maker";   Script = "$help\img\shots\make-shots.ps1";         Pictures = "$help\img" },
    @{ Id = "formats"; Script = "$help\formats\img\shots\make-shots.ps1"; Pictures = "$help\formats\img" })

$failed = $false
foreach ($s in $sets) {
    $shotArgs = @{ Out = "$out\$($s.Id)-raw"; Dest = "$out\$($s.Id)"; Printer = $Printer }
    if ($s.Id -eq "maker") { $shotArgs.Scales = $Scales }
    if ($Tools) { $shotArgs.App = Join-Path $Tools $(if ($s.Id -eq "maker") { "QlLabelMaker.exe" } else { "QlLabelFormats.exe" }) }
    if ($CropOnly) { $shotArgs.CropOnly = $true }
    Remove-Item -Recurse -Force "$out\$($s.Id)", "$out\$($s.Id)-diff" -ErrorAction SilentlyContinue
    "== $($s.Id)"
    try { & $s.Script @shotArgs 2>&1 | ForEach-Object { "$_"; Add-Content -Encoding UTF8 "$out\shots.log" "$_" } }
    catch { "FAILED: $($s.Id): $_"; Add-Content -Encoding UTF8 "$out\shots.log" "FAILED: $($s.Id): $_"; $failed = $true }
}

# Pixel comparison: a pixel differs when a channel differs by more than 8 (anti-aliasing noise); a picture changed when
# its size differs or more than 0.05 % of its pixels differ. diff: the new picture, differing pixels in red.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class HelpShotDiff
{
    public static int Compare(string oldPath, string newPath, string diffPath, out int total)
    {
        using (var a = new Bitmap(oldPath))
        using (var b = new Bitmap(newPath))
        {
            total = b.Width * b.Height;
            if (a.Width != b.Width || a.Height != b.Height) return -1;
            int[] pa = Pixels(a), pb = Pixels(b);
            int differ = 0;
            var d = new int[pb.Length];
            for (int i = 0; i < pb.Length; i++)
            {
                int x = pa[i], y = pb[i];
                bool diff = Math.Abs((x & 255) - (y & 255)) > 8 || Math.Abs(((x >> 8) & 255) - ((y >> 8) & 255)) > 8 ||
                            Math.Abs(((x >> 16) & 255) - ((y >> 16) & 255)) > 8;
                if (diff) differ++;
                // the new picture faded, differing pixels in red
                d[i] = diff ? unchecked((int)0xFFE00000) : (unchecked((int)0xFF000000) | Fade(y, 0) | Fade(y, 8) | Fade(y, 16));
            }
            if (differ > 0)
                using (var bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
                {
                    var data = bmp.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(d, 0, data.Scan0, d.Length);
                    bmp.UnlockBits(data);
                    bmp.Save(diffPath, ImageFormat.Png);
                }
            return differ;
        }
    }

    static int Fade(int c, int shift) { int v = (c >> shift) & 255; return (255 - (255 - v) / 3) << shift; }

    static int[] Pixels(Bitmap src)
    {
        using (var bmp = src.Clone(new Rectangle(0, 0, src.Width, src.Height), PixelFormat.Format32bppArgb))
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var p = new int[bmp.Width * bmp.Height];
            for (int y = 0; y < bmp.Height; y++) Marshal.Copy(data.Scan0 + y * data.Stride, p, y * bmp.Width, bmp.Width);
            bmp.UnlockBits(data);
            return p;
        }
    }
}
"@

$rows = @()
foreach ($s in $sets) {
    foreach ($lang in "de", "en") {
        $oldDir = Join-Path $s.Pictures $lang
        $newDir = Join-Path $out "$($s.Id)\$lang"
        $names = @()
        if (Test-Path $oldDir) { $names += Get-ChildItem $oldDir -Filter *.png | ForEach-Object Name }
        if (Test-Path $newDir) { $names += Get-ChildItem $newDir -Filter *.png | ForEach-Object Name }
        foreach ($n in ($names | Sort-Object -Unique)) {
            $old = Join-Path $oldDir $n; $new = Join-Path $newDir $n
            $diff = Join-Path $out "$($s.Id)-diff\$lang\$n"
            $row = [ordered]@{ Set = $s.Id; Lang = $lang; Name = $n; State = ""; Detail = ""; Old = $old; New = $new; Diff = $null }
            if ($manual -contains "$($s.Id)\$n") { $row.State = "manual"; $row.Detail = "made by hand, not compared" }
            elseif (-not (Test-Path $new)) { $row.State = "missing"; $row.Detail = "not taken (see shots.log)" }
            elseif (-not (Test-Path $old)) { $row.State = "new"; $row.Detail = "not in the help yet" }
            else {
                New-Item -ItemType Directory -Force (Split-Path $diff) | Out-Null
                $total = 0
                $differ = [HelpShotDiff]::Compare($old, $new, $diff, [ref]$total)
                if ($differ -lt 0) {
                    $a = [Drawing.Image]::FromFile($old); $b = [Drawing.Image]::FromFile($new)
                    $row.State = "changed"; $row.Detail = "size {0}x{1} -> {2}x{3}" -f $a.Width, $a.Height, $b.Width, $b.Height
                    $a.Dispose(); $b.Dispose()
                }
                elseif ($differ -gt $total * 0.0005) { $row.State = "changed"; $row.Detail = "{0} of {1} pixels ({2:0.0} %)" -f $differ, $total, (100.0 * $differ / $total); $row.Diff = $diff }
                else { $row.State = "unchanged"; $row.Detail = if ($differ) { "$differ pixels (noise)" } else { "" } }
            }
            $rows += New-Object PSObject -Property $row
        }
    }
}

# report.txt
$order = @{ missing = 0; changed = 1; new = 2; manual = 3; unchanged = 4 }
$sorted = $rows | Sort-Object { $order[$_.State] }, Set, Lang, Name
$count = { param($st) @($rows | Where-Object State -eq $st).Count }
$summary = "help pictures {0}: {1} unchanged, {2} changed, {3} new, {4} missing, {5} made by hand" -f $Name, (& $count unchanged), (& $count changed), (& $count new), (& $count missing), (& $count manual)
$lines = @($summary, "") + ($sorted | ForEach-Object { "{0,-9} {1,-7} {2}\{3}  {4}" -f $_.State, $_.Set, $_.Lang, $_.Name, $_.Detail })
[IO.File]::WriteAllLines("$out\report.txt", $lines)

# index.html: the changed, new and missing pictures, old | new | difference (a local file, opened in a browser)
function Rel($p) { if (-not $p -or -not (Test-Path $p)) { return $null }; return ([Uri]("file:///" + $p.Replace('\', '/'))).AbsoluteUri }
$enc = { param($t) [Net.WebUtility]::HtmlEncode($t) }
$html = New-Object Text.StringBuilder
[void]$html.Append(@"
<!doctype html><html><head><meta charset="utf-8"><title>Help pictures $Name</title><style>
body{font:13px/1.4 "Segoe UI",sans-serif;margin:20px;background:#f0f0f0;color:#000}h1{font-size:18px}
.row{background:#fff;border:1px solid #c9d3d3;padding:10px;margin:0 0 14px}.row h2{font-size:14px;margin:0 0 6px}
.cols{display:flex;gap:14px;flex-wrap:wrap;align-items:flex-start}.cols div{font-size:12px;color:#6d6d6d}
img{display:block;border:1px solid #c9d3d3;max-width:100%;image-rendering:pixelated}
.changed h2{color:#9d5d00}.missing h2{color:#d13438}.new h2{color:#0e7c84}
</style></head><body><h1>$(& $enc $summary)</h1>
"@)
foreach ($r in ($sorted | Where-Object { $_.State -notin "unchanged", "manual" })) {
    [void]$html.Append("<div class=""row $($r.State)""><h2>$(& $enc ("$($r.State): $($r.Set) $($r.Lang)\$($r.Name)  $($r.Detail)"))</h2><div class=""cols"">")
    foreach ($c in @(@("old (help)", $r.Old), @("new", $r.New), @("difference", $r.Diff))) {
        $u = Rel $c[1]
        if ($u) { [void]$html.Append("<div>$($c[0])<img src=""$u""></div>") }
    }
    [void]$html.Append("</div></div>")
}
[void]$html.Append("</body></html>")
[IO.File]::WriteAllText("$out\index.html", $html.ToString(), (New-Object Text.UTF8Encoding $false))

# contact.png: one picture of all changed and new pictures, old (help) | new, a title line each (to approve in one go)
$show = @($sorted | Where-Object { $_.State -in "changed", "new" })
if ($show.Count) {
    $font = New-Object Drawing.Font("Segoe UI", 10, [Drawing.FontStyle]::Bold)
    $items = foreach ($r in $show) {
        $o = if (Test-Path $r.Old) { [Drawing.Image]::FromFile($r.Old) } else { $null }
        $n = [Drawing.Image]::FromFile($r.New)
        New-Object PSObject -Property @{ Row = $r; Old = $o; New = $n; W = $(if ($o) { $o.Width } else { 0 }) + $n.Width + 30; H = [Math]::Max($(if ($o) { $o.Height } else { 0 }), $n.Height) + 34 }
    }
    $width = [Math]::Max(600, ($items | Measure-Object W -Maximum).Maximum + 20)
    $height = ($items | Measure-Object H -Sum).Sum + 20
    $sheet = New-Object Drawing.Bitmap $width, $height
    $g = [Drawing.Graphics]::FromImage($sheet)
    $g.Clear([Drawing.Color]::FromArgb(0xE4, 0xE8, 0xE8))
    $y = 10
    foreach ($i in $items) {
        $title = "{0}: {1} {2}\{3}  {4}" -f $i.Row.State, $i.Row.Set, $i.Row.Lang, $i.Row.Name, $i.Row.Detail
        $g.DrawString($title, $font, [Drawing.Brushes]::Black, 10, $y)
        $x = 10
        if ($i.Old) { $g.DrawImage($i.Old, $x, $y + 22, $i.Old.Width, $i.Old.Height); $x += $i.Old.Width + 20; $i.Old.Dispose() }
        $g.DrawImage($i.New, $x, $y + 22, $i.New.Width, $i.New.Height); $i.New.Dispose()
        $y += $i.H
    }
    $g.Dispose(); $sheet.Save("$out\contact.png", [Drawing.Imaging.ImageFormat]::Png); $sheet.Dispose()
    "contact sheet (old | new): $out\contact.png"
}

$summary
"report: $out\report.txt"
"pictures side by side: $out\index.html"

if ($Apply) {
    foreach ($r in ($rows | Where-Object { $_.State -in "changed", "new" })) {
        Copy-Item $r.New $r.Old -Force
        "applied: $($r.Set) $($r.Lang)\$($r.Name)"
    }
}

if ($failed) { exit 2 }
if (@($rows | Where-Object { $_.State -in "changed", "missing", "new" }).Count) { exit 1 }
exit 0
