# Compares two refactor snapshots (labeltool\test\refactor-snapshot.ps1), read only: members.txt and strings-de.txt
# line by line, every picture pixel by pixel (a .png whose bytes differ is decoded and compared), every .prn and other
# file byte by byte, and the test logs' result lines. Exit code = number of differences.
# Usage: powershell -File labeltool\test\refactor-compare.ps1 -After <folder> [-Before out\refactor-baseline] [-Members expected-moves.txt]
#   -Members: a file of "old<TAB>new" type-name pairs for moves that rename types on purpose (e.g. a nested class that
#   became a class of its own); those names are mapped before the member lists are compared.
param(
    [Parameter(Mandatory = $true)][string]$After,
    [string]$Before,
    [string]$Members)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
public static class RefactorCompare {
    public static int Differing(byte[] a, byte[] b) {
        int n = 0;
        for (int i = 0; i + 3 < a.Length; i += 4)
            if (a[i] != b[i] || a[i + 1] != b[i + 1] || a[i + 2] != b[i + 2] || a[i + 3] != b[i + 3]) n++;
        return n;
    }
}
"@
if (-not $Before) { $Before = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\..\out\refactor-baseline" }
# Relative paths from the PowerShell location (not .NET's current directory)
$Before = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Before))
$After = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($After))
$diffs = 0
function Report-Diff($what) { Write-Host "DIFF $what"; $script:diffs++ }

function CompareLines($name, [string[]]$a, [string[]]$b) {
    $a = @($a | Where-Object { $_ -ne $null }); $b = @($b | Where-Object { $_ -ne $null })
    if ($a.Count -eq 0 -and $b.Count -eq 0) { Write-Host "same $name (empty)"; return }
    if ($a.Count -eq 0 -or $b.Count -eq 0) {
        $d = @($a | ForEach-Object { [pscustomobject]@{ SideIndicator = "<="; InputObject = $_ } }) + @($b | ForEach-Object { [pscustomobject]@{ SideIndicator = "=>"; InputObject = $_ } })
    } else { $d = Compare-Object $a $b -CaseSensitive }
    if (-not $d) { Write-Host "same $name ($($a.Count) lines)"; return }
    Report-Diff "$name`: $(@($d).Count) lines"
    $d | Select-Object -First 30 | ForEach-Object { Write-Host ("   {0} {1}" -f $(if ($_.SideIndicator -eq "<=") { "before" } else { "after " }), $_.InputObject) }
}

# Member list: optional type renames
$map = @{}
if ($Members) { Get-Content $Members | Where-Object { $_ -match "\t" } | ForEach-Object { $p = $_.Split("`t"); $map[$p[0]] = $p[1] } }
foreach ($f in "members.txt", "strings-de.txt") {
    $pa = Join-Path $Before $f; $pb = Join-Path $After $f
    if (-not (Test-Path $pa) -or -not (Test-Path $pb)) { Write-Host "skip $f (not in both)"; continue }
    $a = Get-Content $pa -Encoding UTF8
    if ($f -eq "members.txt" -and $map.Count) {
        $a = $a | ForEach-Object { $l = $_; foreach ($k in $map.Keys) { $l = $l.Replace($k, $map[$k]) }; $l } | Sort-Object
    }
    CompareLines $f $a (Get-Content $pb -Encoding UTF8)
}

# Test results: the lines that say how a test went (timings and paths left out)
Get-ChildItem $Before -Filter *.log | ForEach-Object {
    $pb = Join-Path $After $_.Name
    if (-not (Test-Path $pb)) { Write-Host "skip $($_.Name) (not in after)"; return }
    $pick = { param($p) Get-Content $p -Encoding UTF8 | Where-Object { $_ -match "(?i)\b(ok|pass|passed|fail|failed|failures?|findings?|error)\b" } |
        ForEach-Object { $_ -replace "\d+(\.\d+)?\s*(ms|s)\b", "#" -replace "[A-Z]:\\[^\s""]*", "<path>" } }
    CompareLines "log $($_.Name)" (& $pick $_.FullName) (& $pick $pb)
}

function Pixels($path) {
    $bmp = New-Object System.Drawing.Bitmap $path
    try {
        $r = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
        $data = $bmp.LockBits($r, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
        [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        $bmp.UnlockBits($data)
        return @{ W = $bmp.Width; H = $bmp.Height; Bytes = $bytes }
    } finally { $bmp.Dispose() }
}

# Files of the UI tests: every file in a subfolder
$same = 0
Get-ChildItem $Before -Directory | Where-Object { $_.Name -ne "work" } | ForEach-Object {   # work\: the snapshot's build and run folder
    $dir = $_
    if (-not (Test-Path (Join-Path $After $dir.Name))) { Write-Host "skip $($dir.Name)\ (not in after)"; return }
    $files = Get-ChildItem $dir.FullName -Recurse -File
    foreach ($f in $files) {
        $rel = $f.FullName.Substring($Before.Length).TrimStart("\")
        $other = Join-Path $After $rel
        if (-not (Test-Path $other)) { Report-Diff "$rel missing in after"; continue }
        if ((Get-FileHash $f.FullName).Hash -eq (Get-FileHash $other).Hash) { $same++; continue }
        if ($f.Extension -eq ".png") {
            $a = Pixels $f.FullName; $b = Pixels $other
            if ($a.W -ne $b.W -or $a.H -ne $b.H) { Report-Diff "$rel size $($a.W)x$($a.H) -> $($b.W)x$($b.H)"; continue }
            $n = [RefactorCompare]::Differing($a.Bytes, $b.Bytes)
            if ($n) { Report-Diff "$rel $n pixels" } else { $same++ }
        } elseif ($f.Extension -in ".txt", ".tsv", ".log", ".json") {
            CompareLines $rel (Get-Content $f.FullName -Encoding UTF8) (Get-Content $other -Encoding UTF8)
        } else { Report-Diff "$rel bytes differ" }
    }
    Get-ChildItem (Join-Path $After $dir.Name) -Recurse -File -ErrorAction SilentlyContinue | ForEach-Object {
        $rel = $_.FullName.Substring($After.Length).TrimStart("\")
        if (-not (Test-Path (Join-Path $Before $rel))) { Report-Diff "$rel new in after" }
    }
}
Write-Host "$same files identical, $diffs differences"
exit $diffs
