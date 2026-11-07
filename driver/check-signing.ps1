<#
.SYNOPSIS
  Read-only diagnostics of the driver's signing state; run after every maintenance window
  (docs/architecture.md, section 6).

.DESCRIPTION
  Run as administrator (ARM64 or x64 machine). Changes nothing; writes
  ..\out\check-signing\check-<timestamp>.txt with: boot/code integrity state, Smart App
  Control, Windows Protected Print policy, the certificates that sign the installed package and where they are trusted,
  driver store packages and their signatures, every model's printer driver and its printers, the last setupapi install
  section for our package (qlunidrv.inf), recent CodeIntegrity / PrintService events and the plug-in log tail.
#>
# -DriverName: only that driver (e.g. "Brother QL-570 (Unidrv)"); without it every model's driver from the package
param([string]$DriverName)
$ErrorActionPreference = "Continue"

# DriverVersion as Windows packs it (16 bits per part), e.g. 0.18.304.1114
function Get-DriverVersion($d) {
    $v = [uint64]$d.DriverVersion
    "{0}.{1}.{2}.{3}" -f ($v -shr 48), (($v -shr 32) -band 0xFFFF), (($v -shr 16) -band 0xFFFF), ($v -band 0xFFFF)
}

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script as administrator."
}
$repo = Split-Path $PSScriptRoot -Parent
$pkg = Join-Path $repo "out\package"
$outDir = Join-Path $repo "out\check-signing"
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir ("check-{0:yyyyMMdd-HHmm}.txt" -f (Get-Date))
Start-Transcript -Path $out -Force | Out-Null

function Section($title) { ""; "=== $title ===" }
$since = (Get-Date).AddDays(-1)

try {
    Section "System"
    $os = Get-CimInstance Win32_OperatingSystem
    $cv = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion"
    "{0} {1} build {2}.{3} ({4})" -f $os.Caption, $cv.DisplayVersion, $cv.CurrentBuild, $cv.UBR, (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE

    Section "Boot state"
    try { "Secure Boot: $(Confirm-SecureBootUEFI)" } catch { "Secure Boot: unknown ($($_.Exception.Message))" }
    $bcd = bcdedit /enum "{current}"
    $flags = $bcd | Select-String -Pattern "testsigning|nointegritychecks|loadoptions"
    if ($flags) { $flags | ForEach-Object { $_.Line } } else { "testsigning / nointegritychecks: not set (off)" }

    Section "Code integrity"
    $sac = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy" -ErrorAction SilentlyContinue).VerifiedAndReputablePolicyState
    $sacText = @{ 0 = "off"; 1 = "ON (may block self-signed DLLs)"; 2 = "evaluation" }
    "Smart App Control: " + $(if ($null -eq $sac) { "not configured" } else { "$sac = $($sacText[[int]$sac])" })
    $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction SilentlyContinue
    if ($dg) {
        "VBS status: $($dg.VirtualizationBasedSecurityStatus) (0 off, 1 configured, 2 running)"
        "Security services running: $($dg.SecurityServicesRunning -join ',') (2 = Memory integrity/HVCI)"
        "App Control (WDAC) user-mode enforcement: $($dg.UsermodeCodeIntegrityPolicyEnforcementStatus) (0 off, 1 audit, 2 enforced)"
    }

    Section "Windows Protected Print / printer policies"
    foreach ($key in "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers\WPP",
                     "HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\Printers") {
        if (Test-Path $key) {
            $props = Get-ItemProperty $key
            $names = $props.PSObject.Properties | Where-Object { $_.Name -notlike "PS*" }
            if ($names) { "$key"; $names | ForEach-Object { "  $($_.Name) = $($_.Value)" } } else { "$key (no values)" }
        } else { "$key not present" }
    }

    $arch = if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "amd64" }   # not the environment (a starting program's)
    $store = Get-ChildItem "$env:WINDIR\System32\DriverStore\FileRepository" -Directory -Filter "*.inf_${arch}_*" | Where-Object Name -match "^qlunidrv\.inf_" |
        Sort-Object LastWriteTime -Descending

    # The certificates that sign the installed package: its catalog's signer and that signer's root. A release
    # install signs with a CA made on this PC (Setup.exe), a dev install with out\package's; both
    # are fine as long as the signer is in TrustedPublisher and the root in Root (comparing with out\package
    # alone would report "False" for a valid release install).
    Section "Certificates (signer of the installed package)"
    function Show-Cert($x, $storeName, $role) {
        $inStore = Test-Path ("Cert:\LocalMachine\{0}\{1}" -f $storeName, $x.Thumbprint)
        "{0}: {1}`n  thumbprint {2}, valid until {3:yyyy-MM-dd}, in LocalMachine\{4}: {5}" -f $role, $x.Subject, $x.Thumbprint, $x.NotAfter, $storeName, $inStore
        $eku = $x.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.37" }
        if ($eku) { "  EKU: " + (($eku.EnhancedKeyUsages | ForEach-Object { "$($_.FriendlyName) ($($_.Value))" }) -join ", ") }
        else { "  EKU: none (valid for all purposes)" }
    }
    $installedCat = if ($store) { Join-Path $store[0].FullName ($store[0].Name -replace "\.inf_.*", ".cat") }
    $signer = if ($installedCat -and (Test-Path $installedCat)) { (Get-AuthenticodeSignature $installedCat).SignerCertificate }
    $root = $null
    if ($signer) {
        "Catalog: $installedCat"
        $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
        $chain.ChainPolicy.RevocationMode = "NoCheck"
        [void]$chain.Build($signer)
        $root = $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate
        Show-Cert $signer "TrustedPublisher" "Signer"
        if ($root.Thumbprint -ne $signer.Thumbprint) { Show-Cert $root "Root" "Root CA" }
        else { "Root CA: the signer is self-signed (no separate CA)" }
    } else { "No signed catalog of an installed qlunidrv package found." }

    # out\package's certificates, for comparison: other ones are normal after a release install (signed on this PC)
    foreach ($c in @(@("qlunidrv-root.cer", "Root"), @("qlunidrv.cer", "TrustedPublisher"))) {
        $file = Join-Path $pkg $c[0]
        if (-not (Test-Path $file)) { continue }
        $x = New-Object Security.Cryptography.X509Certificates.X509Certificate2 $file
        $same = ($signer -and $x.Thumbprint -eq $signer.Thumbprint) -or ($root -and $x.Thumbprint -eq $root.Thumbprint)
        "out\package\{0}: {1}" -f $c[0], $(if ($same) { "the same certificate" } else { "a different certificate (not the one the installed package uses; fine)" })
    }

    Section "Driver store packages"
    $enum = pnputil /enum-drivers
    for ($i = 1; $i -lt $enum.Count; $i++) {
        if ($enum[$i] -match "qlunidrv\.inf") {
            $end = [Math]::Min($i + 7, $enum.Count - 1)
            $enum[($i - 1)..$end] | Where-Object { $_ -match "\S" }
            ""
        }
    }
    foreach ($s in $store) {
        "$($s.Name) ($($s.LastWriteTime))"
        # The catalog and the plug-in (in the package's arm64\ or amd64\ subfolder)
        foreach ($f in ($s.Name -replace "\.inf_.*", ".cat"), "$arch\QLUnidrv.dll") {
            $p = Join-Path $s.FullName $f
            if (-not (Test-Path $p)) { "  ${f}: missing"; continue }
            $sig = Get-AuthenticodeSignature $p
            "  {0}: {1} - {2}" -f $f, $sig.Status, $sig.StatusMessage
        }
    }
    if (-not $store) { "No qlunidrv package in the driver store." }

    # Every model's driver from our package (or only -DriverName), with the printers that use them
    Section "Printer drivers and printers"
    $drv = @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object {
        ($_.InfPath -match "\\qlunidrv\.inf_" -or $_.Name -like "Brother QL-* (Unidrv)") -and (-not $DriverName -or $_.Name -eq $DriverName) } |
        Sort-Object Name, PrinterEnvironment)
    if ($drv.Count) {
        $drv | ForEach-Object { "{0} [{1}] {2}`n  {3}" -f $_.Name, $_.PrinterEnvironment, (Get-DriverVersion $_), $_.InfPath }
        $names = $drv | ForEach-Object Name
        $printers = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $names -contains $_.DriverName })
        ""
        if ($printers.Count) { $printers | Format-Table Name, DriverName, PortName, PrinterStatus, Shared -AutoSize | Out-String -Width 200 }
        else { "No printer uses these drivers." }
    } elseif ($DriverName) { "Printer driver '$DriverName' not registered." }
    else { "No printer driver from the qlunidrv package registered." }

    Section "setupapi.dev.log - last install section mentioning qlunidrv (filtered)"
    $setupLog = "$env:WINDIR\INF\setupapi.dev.log"
    if (Test-Path $setupLog) { try {
        $lines = Get-Content $setupLog
        $starts = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i].StartsWith(">>>  [")) { $i } })
        $hit = $null
        for ($k = $starts.Count - 1; $k -ge 0 -and -not $hit; $k--) {
            $from = $starts[$k]
            $to = if ($k + 1 -lt $starts.Count) { $starts[$k + 1] - 1 } else { $lines.Count - 1 }
            if (($lines[$from..$to] -match "qlunidrv").Count) { $hit = $lines[$from..$to] }
        }
        if ($hit) {
            $hit | Where-Object {
                $_ -like ">>>*" -or $_ -like "<<<*" -or $_ -match "!!!|^\s*!\s" -or
                $_ -match "(?i)sign|trust|catalog|certif|authenticode|verif|exit status|error|fail"
            }
        } else { "No section mentioning qlunidrv." }
    } catch { "setupapi section failed: $_" } } else { "$setupLog not found." }

    Section "CodeIntegrity/Operational - last 24 h (print-related)"
    $ci = Get-WinEvent -LogName "Microsoft-Windows-CodeIntegrity/Operational" -MaxEvents 500 -ErrorAction SilentlyContinue |
        Where-Object { $_.TimeCreated -gt $since -and $_.Message -match "(?i)qlunidrv|spool|PrintIsolation|unidrv" } |
        Select-Object -First 20
    if ($ci) { $ci | Format-List TimeCreated, Id, LevelDisplayName, Message | Out-String } else { "none" }

    Section "PrintService/Admin - last 24 h, warnings and errors"
    $ps = Get-WinEvent -LogName "Microsoft-Windows-PrintService/Admin" -MaxEvents 200 -ErrorAction SilentlyContinue |
        Where-Object { $_.TimeCreated -gt $since -and $_.Level -le 3 } | Select-Object -First 15
    if ($ps) { $ps | Format-List TimeCreated, Id, LevelDisplayName, Message | Out-String } else { "none" }

    Section "Plug-in log tail"
    $plog = "C:\ProgramData\QLUnidrv\log\plugin.log"
    if (Test-Path $plog) { Get-Content $plog -Tail 15 } else { "$plog not found (driver log off)." }
} finally {
    Stop-Transcript | Out-Null
    "Results written to $out"
}
