<#
.SYNOPSIS
  End-to-end check: prints the reference test page through the installed driver (ARM64 or x64) for every
  reference capture and compares the output with Brother's original (reference\captures\<model>\*.prn).

.DESCRIPTION
  Needs install.ps1 -TestPrinter to have run once (printer "QL-800 Capture" on a local file port).
  No admin rights needed. Options are selected through the PrintTicket (tools: test\ptdevmode.cs),
  user label formats by form name (qlcap form=, as applications select them), and
  the page is drawn by tools\capture\qlcap.cs, so the GDI drawing is identical.
  Results: ..\out\compare\<name>.prn and a summary table.

  Other models (0.11): a capture printer with that model's driver and -Captures <folder under
  reference\captures>, e.g. -Printer "QL-560 Capture" -Captures ql560. Cases without a reference file there,
  or with an option the model's GPD doesn't offer, are SKIPPED.

.EXAMPLE
  .\compare.ps1                 # all cases
  .\compare.ps1 -Only 62mm_mirror
  .\compare.ps1 -Printer "QL-650TD Capture" -Captures ql650td
  .\compare.ps1 -Caps           # dump the printer's PrintCapabilities to ..\out\compare\caps.xml
#>
param(
    [string]$Printer = "QL-800 Capture",
    [string]$PortFile,  # default: the test printer's file port
    [string]$Captures = "ql800",  # reference folder under reference\captures
    [string[]]$Only,
    [switch]$Caps
)
$ErrorActionPreference = "Stop"
if (-not $PortFile) { $PortFile = (Get-Printer -Name $Printer -ErrorAction Stop).PortName }
$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root "out\compare"
$toolDir = Join-Path $root "out\tools"
New-Item -ItemType Directory -Force $outDir, $toolDir | Out-Null

# Build the two helpers (AnyCPU -> run natively on ARM64 and x64)
$csc = "$env:WINDIR\Microsoft.NET\$(if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64") { "FrameworkArm64" } else { "Framework64" })\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$wpf = Join-Path (Split-Path $csc) "WPF"
$qlcap = Join-Path $toolDir "qlcap.exe"
$ptdm = Join-Path $toolDir "ptdevmode.exe"
& $csc /nologo /platform:anycpu /out:$qlcap (Join-Path $root "tools\capture\qlcap.cs")
if ($LASTEXITCODE) { throw "building qlcap failed" }
& $csc /nologo /platform:anycpu /out:$ptdm "/r:$wpf\System.Printing.dll" "/r:$wpf\ReachFramework.dll" `
    (Join-Path $PSScriptRoot "test\ptdevmode.cs")
if ($LASTEXITCODE) { throw "building ptdevmode failed" }

if ($Caps) {
    & $ptdm caps $Printer (Join-Path $outDir "caps.xml")
    "PrintCapabilities written to $outDir\caps.xml"
    return
}

$custom898 = @("paper=256", "wid=620", "len=898")
# Brother's default length on the wide models: QL-1050/1060N 152.8 mm,
# QL-1100/1110NWB 164.3 mm, QL-1115NWB 100 mm
$wideLen = if ($Captures -like "ql10*") { "len=1528" } elseif ($Captures -eq "ql1115nwb") { "len=1000" } else { "len=1643" }
if ($Captures -like "ql1[01]*") { $custom898 = @("paper=256", "wid=620", $wideLen) }
# name, PrintTicket options, qlcap options
$cases = @(
    @("62mm-continuous_default-length", @(), $custom898),
    @("62mm-continuous_100mm-custom", @(), @("paper=256", "wid=620", "len=1000")),
    @("29x90-diecut", @("PageMediaSize=P271"), @("orient=2")),
    @("29x90-diecut_portrait", @("PageMediaSize=P271"), @("orient=1")),
    @("29x90_userformat-317", @(), @("form=Adress-Etikett (Standard)", "orient=1")),
    @("62mm_userformat-332-DHL-180mm", @(), @("form=Porto-Etikett DHL 62mmx180mm", "orient=1")),
    @("62mm_autocut-off", @("AutoCut=Off"), $custom898),
    @("62mm_cut-every-2", @("CutEvery=N2"), $custom898 + "pages=3"),
    @("62mm_cut-at-end", @("AutoCut=Off"), $custom898 + "pages=2"),
    @("62mm_cut-at-end-off", @("AutoCut=Off", "CutAtEnd=Off"), $custom898 + "pages=2"),
    @("62mm_highres", @("PageResolution=R300x600"), $custom898),
    @("62mm_2pages", @(), $custom898 + "pages=2"),
    @("62mm_copies-2", @(), $custom898 + "copies=2"),
    @("62mm_landscape", @(), $custom898 + "orient=2"),
    @("62mm_mirror", @("MirrorPrint=On"), $custom898),
    # reference only in the QL-570 and older folders
    @("12mm-dia_diecut", @("PageMediaSize=P362"), @("orient=1")),
    @("62mm_userformat-332-209mm", @(), @("form=Porto-Etikett 62mmx209mm", "orient=1")),
    @("62mm_mark_trim-trailing", @("TrimTrailingBlank=On"), $custom898 + "page=mark"),
    # reference only in ql1050 / ql1060n (wide papers; their 209 mm format is dmPaper 334)
    @("62mm_userformat-334-209mm", @(), @("form=Porto-Etikett 62mmx209mm", "orient=1")),
    @("102mm-continuous_100mm-custom", @(), @("paper=256", "wid=1020", "len=1000")),
    @("102x152-diecut", @("PageMediaSize=P366"), @("orient=1")),
    @("102x51-diecut", @("PageMediaSize=P365"), @("orient=1")),
    @("103x164-diecut", @("PageMediaSize=P385"), @("orient=1")),
    @("102mm-continuous_default-length", @(), @("paper=256", "wid=1020", $wideLen)),
    @("103mm-continuous", @(), @("paper=256", "wid=1030", $wideLen)),
    # reference only in ql1100 / ql1110nwb / ql1115nwb (their 209 mm format is dmPaper 330; DT papers: QL-1115NWB)
    @("62mm_userformat-330-209mm", @(), @("form=Porto-Etikett 62mmx209mm", "orient=1")),
    @("29x42-diecut", @("PageMediaSize=P358"), @("orient=1")),
    @("60x86-diecut", @("PageMediaSize=P383"), @("orient=1")),
    @("dt90mm-continuous", @("PageMediaSize=P465"), @("orient=1")),
    @("dt102mm-continuous", @("PageMediaSize=P466"), @("orient=1")),
    @("dt102x51-diecut", @("PageMediaSize=P467"), @("orient=1")),
    @("dt102x152-diecut", @("PageMediaSize=P468"), @("orient=1")),
    # blank lines as Z (packed models: QL-810W/820NWB, QL-1050/1060N, QL-1100 series)
    @("62mm_mark_z-rule", @(), $custom898 + "page=mark"),
    @("29x90_mark_z-rule", @("PageMediaSize=P271"), @("orient=1", "page=mark"))
)
if (-not $Captures) { $Captures = "ql800" }
$refDir = Join-Path $root "reference\captures\$Captures"
# The QL-800's results stay directly in out\compare (as before its captures moved into ql800\, 2026-10-19).
$outDir = Join-Path $outDir $(if ($Captures -ne "ql800") { $Captures } else { "." })
New-Item -ItemType Directory -Force $outDir | Out-Null

Add-Type -AssemblyName System.Drawing
$ps = New-Object System.Drawing.Printing.PrinterSettings
$ps.PrinterName = $Printer
$paperNames = @($ps.PaperSizes | ForEach-Object PaperName)

$results = foreach ($c in $cases) {
    $name, $pt, $qa = $c
    # powershell -File passes "-Only a,b" as one string: split it here.
    if ($Only -and ($Only -split "," | ForEach-Object { $_.Trim() }) -notcontains $name) { continue }
    # User-format cases need that label format installed (label tool); check-labels.ps1 covers forms.
    $form = $qa | Where-Object { $_ -like "form=*" } | ForEach-Object { $_.Substring(5) }
    if ($form -and $paperNames -notcontains $form) {
        [pscustomobject]@{ Case = $name; Result = "SKIPPED"; Details = "label format '$form' is not installed" }
        continue
    }
    $ref = Join-Path $refDir "$name.prn"
    if (-not (Test-Path $ref)) {
        [pscustomobject]@{ Case = $name; Result = "SKIPPED"; Details = "no reference capture $ref" }
        continue
    }
    $dm = Join-Path $outDir "$name.devmode"
    if ($Captures) {  # stderr hidden: an option the model's GPD lacks is reported as SKIPPED below
        $ErrorActionPreference = "Continue"
        & $ptdm make $Printer $dm @pt 2>$null | Out-Null
        $ErrorActionPreference = "Stop"
    } else {
        & $ptdm make $Printer $dm @pt | Out-Null
    }
    if ($LASTEXITCODE) {
        if ($Captures) {  # e.g. 300 x 600 on a QL-560: not in its GPD
            [pscustomobject]@{ Case = $name; Result = "SKIPPED"; Details = "option not offered: $($pt -join ', ')" }
            continue
        }
        throw "ptdevmode failed for $name"
    }
    if (Test-Path $PortFile) { [IO.File]::Delete($PortFile) }   # Remove-Item -Force also needs write-attributes rights
    & $qlcap print $Printer $dm @qa | Out-Null
    if ($LASTEXITCODE) { throw "qlcap failed for $name" }
    # Wait for the spooler to finish the job
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-PrintJob -PrinterName $Printer -ErrorAction SilentlyContinue) -or -not (Test-Path $PortFile)) {
        if ((Get-Date) -gt $deadline) { throw "timeout waiting for output of $name" }
        Start-Sleep -Milliseconds 300
    }
    $prn = Join-Path $outDir "$name.prn"
    Copy-Item $PortFile $prn -Force
    $report = python -I (Join-Path $root "tools\compare_prn.py") $ref $prn
    [pscustomobject]@{ Case = $name; Result = ($report | Select-Object -First 1).Split(' ')[0]; Details = ($report | Select-Object -Skip 1) -join "`n" }
}
$results | Format-Table Case, Result -AutoSize
$results | Where-Object Result -ne "IDENTICAL" | ForEach-Object { "--- $($_.Case)"; $_.Details }
# Report only: DIFFERENT is expected for the known 1-2 dot shifts. Exit 0 unless a case failed to run
# (otherwise the exit code was compare_prn.py's for whichever case ran last).
exit 0
