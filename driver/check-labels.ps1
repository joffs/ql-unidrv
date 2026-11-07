<#
.SYNOPSIS
  Checks the label formats end to end (labels v2): every format in C:\ProgramData\QLUnidrv\labels.json
  must be offered as a paper size, and a print through the capture printer must arrive with
  Brother's media, line count and feed margin.

.DESCRIPTION
  No admin rights needed; needs the driver (0.8+), the label tool's --sync (install.ps1) and the
  capture printer (install.ps1 -TestPrinter). Prints one page per format, selecting it by name like
  applications do (qlcap form=), and reads ESC i z / ESC i d from the captured stream:
    continuous: lines = round(L * 300 / 254) + 1 - 2 * feed, ESC i d = feed (dots)
    die-cut:    lines = the paper's printable lines (docs/data/bsq80AD.json), ESC i d = 0
                (12 mm round: 35, QL-800/570 family)
  Results: ..\out\check-labels\<n>.prn and a table.

.EXAMPLE
  .\check-labels.ps1
  .\check-labels.ps1 -Only "Standard-Etikett 62mmx35mm"
#>
param(
    [string]$Printer = "QL-800 Capture",
    [string]$MainPrinter = "Brother QL-800 (Unidrv)",
    [string]$Labels = (Join-Path $env:ProgramData "QLUnidrv\labels.json"),
    [string[]]$Only
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "out\check-labels"
New-Item -ItemType Directory -Force $outDir | Out-Null
$portFile = (Get-Printer -Name $Printer).PortName
$plog = Join-Path $env:ProgramData "QLUnidrv\log\plugin.log"
if (-not (Test-Path $plog)) { Write-Host "Driver log off: the Log column stays empty (install.ps1 -AppOnly -Logging on)." -ForegroundColor Yellow }

$qlcap = Join-Path $root "out\tools\qlcap.exe"
if (-not (Test-Path $qlcap)) {
    $csc = "$env:WINDIR\Microsoft.NET\$(if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64") { "FrameworkArm64" } else { "Framework64" })\v4.0.30319\csc.exe"
    if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
    & $csc /nologo /platform:anycpu /out:$qlcap (Join-Path $root "tools\capture\qlcap.cs")
    if ($LASTEXITCODE) { throw "building qlcap failed" }
}

Add-Type -AssemblyName System.Drawing
function PaperNames($name) {
    $ps = New-Object System.Drawing.Printing.PrinterSettings
    $ps.PrinterName = $name
    if (-not $ps.IsValid) { return @() }
    return @($ps.PaperSizes | ForEach-Object PaperName)
}

$formats = (Get-Content $Labels -Raw -Encoding UTF8 | ConvertFrom-Json).formats
$papers = @{}
foreach ($p in (Get-Content (Join-Path $root "docs\data\bsq80AD.json") -Raw | ConvertFrom-Json).papers) { $papers[[int]$p.id] = $p }
$mainNames = PaperNames $MainPrinter
$capNames = PaperNames $Printer

$n = 0
$results = foreach ($f in $formats) {
    $n++
    if ($Only -and $Only -notcontains $f.name) { continue }
    $p = $papers[[int]$f.paper_id]
    $cont = $p.kind -eq 10
    $feed = [int][math]::Round($f.feed_mm * 300 / 25.4)
    if ($cont) {
        $l01 = [int][math]::Round($f.length_mm * 10)
        $expLines = [math]::Floor(([math]::Floor($l01 * 3000 / 254) + 5) / 10) + 1 - 2 * $feed
        $expFeed = $feed
    } else {
        $expLines = [int]$p.printable_length_dots
        $expFeed = if ([int]$f.paper_id -eq 362) { 35 } else { 0 }  # 12 mm round: 3 mm top margin (0.11, as Brother)
    }
    $row = [ordered]@{ Format = $f.name; Listed = (($mainNames -contains $f.name) -and ($capNames -contains $f.name)); Lines = ""; Feed = ""; Media = ""; Log = ""; Result = "FAIL" }
    if ($capNames -contains $f.name) {
        $logStart = if (Test-Path $plog) { (Get-Item $plog).Length } else { 0 }
        if (Test-Path $portFile) { Remove-Item $portFile -Force }
        & $qlcap print $Printer - "form=$($f.name)" | Out-Null
        if ($LASTEXITCODE) { throw "qlcap failed for $($f.name)" }
        $deadline = (Get-Date).AddSeconds(60)
        while ((Get-PrintJob -PrinterName $Printer -ErrorAction SilentlyContinue) -or -not (Test-Path $portFile)) {
            if ((Get-Date) -gt $deadline) { throw "timeout waiting for output of $($f.name)" }
            Start-Sleep -Milliseconds 300
        }
        Start-Sleep -Milliseconds 300
        $prn = Join-Path $outDir ("{0:00}.prn" -f $n)
        Copy-Item $portFile $prn -Force
        $b = [IO.File]::ReadAllBytes($prn)
        $z = -1; $d = -1
        for ($i = 0; $i -lt $b.Length - 13; $i++) {
            if ($b[$i] -eq 0x1B -and $b[$i + 1] -eq 0x69) {
                if ($z -lt 0 -and $b[$i + 2] -eq 0x7A) { $z = $i }
                if ($d -lt 0 -and $b[$i + 2] -eq 0x64) { $d = $i }
                if ($z -ge 0 -and $d -ge 0) { break }
            }
        }
        if ($z -ge 0 -and $d -ge 0) {
            $lines = [BitConverter]::ToUInt32($b, $z + 7)
            $kind = $b[$z + 4]; $width = $b[$z + 5]
            $gotFeed = [BitConverter]::ToUInt16($b, $d + 3)
            $row.Lines = "$lines/$expLines"
            $row.Feed = "$gotFeed/$expFeed"
            $row.Media = "{0} {1}mm" -f $(if ($kind -eq 0x0A) { "cont" } else { "die" }), $width
            $mediaOk = ($kind -eq [int]$p.kind) -and
                (($width -eq [math]::Floor(([int]$p.width_0_1mm + 5) / 10)) -or ($width -eq [math]::Floor([int]$p.width_0_1mm / 10)))
            if (Test-Path $plog) {
                $fs = [IO.File]::Open($plog, "Open", "Read", "ReadWrite")
                # shorter than before: the driver started a new log (plugin.old.log) in between
                try { [void]$fs.Seek($(if ($fs.Length -lt $logStart) { 0 } else { $logStart }), "Begin"); $new = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
                $row.Log = if ($new -match "label format '") { "hit" } else { "no hit" }
            }
            if ($row.Listed -and $lines -eq $expLines -and $gotFeed -eq $expFeed -and $mediaOk -and $row.Log -ne "no hit") { $row.Result = "OK" }
        }
    }
    [pscustomobject]$row
}
$results | Format-Table -AutoSize | Out-String -Width 200
$bad = @($results | Where-Object Result -ne "OK")
if ($bad.Count) { "$($bad.Count) of $(@($results).Count) formats FAILED"; exit 1 }
"All $(@($results).Count) label formats OK."
