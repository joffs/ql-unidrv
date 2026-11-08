<#
.SYNOPSIS
  Guided install of a QL Unidrv release zip (advanced\driver\setup.ps1 in the zip; the console form of Setup.exe, for administrators).

.DESCRIPTION
  The zip holds the compiled, unsigned driver package (out\package), the label tool (out\tools) and the
  scripts. This script
  1. signs the package with the user's own certificate (driver\package.ps1 -SignOnly -OneTimeKeys: a new
     private root CA and signer for this package, whose private keys are deleted right after signing, so
     nothing on the PC can sign code it trusts later; needs nothing but Windows),
  2. on ARM64: makes sure the PC runs in the one-time "Disable driver signature enforcement" boot (F7),
  3. runs driver\install.ps1 as administrator (-Update when the driver is already installed; asks first about the
     optional service "QL printer status": -StatusService on|off),
  4. offers the printer status window (where printer notifications are turned on).
  When the driver is already installed, it shows a menu instead (docs/setup.md): update / repair from this zip,
  add a network printer or another PC's shared printer, the status service on/off, the driver log on/off,
  the printer status window, uninstall. The settings items run driver\install.ps1 -Settings (no driver
  install, no special start on ARM64).
  -Uninstall: runs driver\install.ps1 -Uninstall as administrator.
  -Pause: wait for Enter at the end (Windows' "Installed apps" starts it without a console of its own).
  Copied to advanced\driver\setup.ps1 in the zip by tools\make-release.ps1, and by install.ps1 to
  C:\Program Files\QLUnidrv\setup (with install.ps1), where "Installed apps" runs it: there it has no package,
  so the menu offers no update.
#>
param([switch]$Uninstall, [switch]$Pause)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$install = Join-Path $PSScriptRoot "install.ps1"
$log = Join-Path $env:TEMP "ql-unidrv-install.log"
# Removed after a run that finished, with the files earlier runs and the setup window left;
# kept after a failure, which names it.
# The same names as the setup window's (labeltool\src\ScriptRun.cs RunFileNames): change both.
function Remove-Log {
    foreach ($name in "ql-unidrv-install.log", "ql-unidrv-setup.txt", "ql-unidrv-settings.log", "ql-unidrv-settings.txt") {
        Remove-Item (Join-Path $env:TEMP $name) -Force -ErrorAction SilentlyContinue
    }
}

function Ask([string]$question) {
    # J/N too: German users type J
    do { $a = (Read-Host "$question [Y/N]").Trim().ToUpper() } while ($a -notin "Y", "N", "J")
    return $a -ne "N"
}

function Run-Elevated([string]$arguments) {
    if (Test-Path $log) { Remove-Item $log -Force }
    $argList = "-NoProfile -ExecutionPolicy Bypass -File `"$install`" $arguments -Log `"$log`""
    try {
        $p = Start-Process powershell.exe -Verb RunAs -ArgumentList $argList -Wait -PassThru
    } catch {
        Write-Host "Cancelled: administrator rights are needed for this step." -ForegroundColor Yellow
        return $false
    }
    $ok = ($p.ExitCode -eq 0) -and (Test-Path $log) -and (Select-String -Path $log -Pattern "^(Done\.|Uninstalled\.)" -Quiet)
    # Only on failure, and via Out-Host: lines left in the output would become part of the return value
    # (a non-empty array, which "if (-not ...)" counts as success).
    if (-not $ok -and (Test-Path $log)) {
        # Without the transcript's own header and footer (each between lines of asterisks, in Windows' language)
        $lines = @(Get-Content $log)
        $stars = @(for ($i = 0; $i -lt $lines.Count; $i++) { if ($lines[$i] -match "^\*{10,}$") { $i } })
        $from = if ($stars.Count -ge 2) { $stars[1] + 1 } else { 0 }
        $to = if ($stars.Count -ge 4) { $stars[-2] - 1 } else { $lines.Count - 1 }
        if ($to -ge $from) { $lines[$from..$to] | Where-Object { $_ -notmatch "^\*{10,}$" } | Out-Host }
    }
    return $ok
}

# The label tool's and the label editor's windows may hold unsaved changes: the user closes them (install.ps1
# closes the printer monitor and status windows itself).
function Wait-LabelToolClosed {
    $dir = Join-Path $env:ProgramFiles "QLUnidrv"
    while (@(Get-CimInstance Win32_Process -Filter "Name='QlLabelFormats.exe' OR Name='QlLabelMaker.exe'" |
            Where-Object { $_.ExecutablePath -like "$dir\*" }).Count) {
        Write-Host "QL Label Formats or QL Label Maker is open. Save your changes, close it, then press Enter." -ForegroundColor Yellow
        [void](Read-Host)
    }
}

# The printer status window
function Start-StatusWindow {
    $status = Join-Path $env:ProgramFiles "QLUnidrv\QlPrinterStatus.exe"
    if (Test-Path $status) { Start-Process $status }
}

# Asks for a network printer's address and its model (from the printer itself, else chosen); returns the
# arguments for install.ps1, or nothing when skipped.
function Read-NetworkPrinter {
    # The zip's label tool, else the installed one (installed copy of this script: no out\tools)
    $identify = Join-Path $root "out\tools\QlLabelFormats.exe"
    if (-not (Test-Path $identify)) { $identify = $appExe }
    while ($true) {
        $address = (Read-Host "The printer's IP address (or host name; empty = skip)").Trim()
        if (-not $address) { return @() }
        if ($address -notmatch "^[A-Za-z0-9][A-Za-z0-9.:-]*$") { Write-Host "That is not an address." -ForegroundColor Yellow; continue }
        # Ask the printer which model it is (SNMP, no administrator rights needed).
        $result = Join-Path $env:TEMP "qlunidrv-identify.txt"
        Remove-Item $result -Force -ErrorAction SilentlyContinue
        Start-Process $identify -ArgumentList "--identify", $address, "--result", "`"$result`"" -Wait -WindowStyle Hidden
        $lines = @(Get-Content $result -ErrorAction SilentlyContinue)
        Remove-Item $result -Force -ErrorAction SilentlyContinue
        if ($lines.Count -ge 3 -and $lines[0] -eq "OK" -and $lines[1] -match '^Brother QL-[0-9A-Z]+ \(Unidrv\)$') {
            Write-Host "Found: $($lines[2]) at $address ($($lines[3]) loaded)." -ForegroundColor Green
            # The model goes along, so the elevated install doesn't ask the printer again.
            return @("-NetworkPrinter $address", "-DriverName `"$($lines[1])`"")
        }
        Write-Host "No answer as a QL printer: $(($lines | Select-Object -Skip 1) -join ' ')" -ForegroundColor Yellow
        $models = "QL-580N", "QL-710W", "QL-720NW", "QL-810W", "QL-820NWB", "QL-1060N", "QL-1110NWB", "QL-1115NWB"
        Write-Host "If the address is right, choose the model yourself (the printer status won't work until it answers):"
        for ($i = 0; $i -lt $models.Count; $i++) { Write-Host ("  {0}. {1}" -f ($i + 1), $models[$i]) }
        $n = (Read-Host "Number (empty = enter the address again)").Trim()
        if ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le $models.Count) {
            return @("-NetworkPrinter $address", "-DriverName `"Brother $($models[[int]$n - 1]) (Unidrv)`"")
        }
    }
}

function Invoke-Uninstall {
    # Printers other PCs print through
    foreach ($p in @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Shared -and $_.DriverName -like "Brother QL-* (Unidrv)" })) {
        Write-Host "Shared with other PCs: '$($p.Name)' as '$($p.ShareName)' - they can't print through it after the uninstall." -ForegroundColor Yellow
    }
    if (-not (Ask "Remove the driver, its printers, the programs, the certificates and the logs from this PC?")) { return }
    Wait-LabelToolClosed
    $keep = $false
    if (Test-Path (Join-Path $env:ProgramData "QLUnidrv\labels.json")) {
        Write-Host "Your label formats (the list in 'QL Label Formats') can be kept for a later install."
        $keep = Ask "Keep your label formats?"
    }
    if (Run-Elevated $(if ($keep) { "-Uninstall -KeepLabels" } else { "-Uninstall" })) {
        # The copies of the per-PC root that signing left among this user's intermediate CAs
        Get-ChildItem Cert:\CurrentUser\CA -ErrorAction SilentlyContinue | Where-Object Subject -like "CN=QL* driver root CA*" | Remove-Item -ErrorAction SilentlyContinue
        # What Windows didn't let go (a port, a paper size): said, and the log kept
        $left = @(Select-String -Path $log -Pattern "^Could not (remove|delete)" | ForEach-Object Line)
        if ($left) { $left | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }; Write-Host "(log: $log)" }
        else { Remove-Log }
        Write-Host "`nRemoved$(if ($keep) { ' (your label formats were kept)' })." -ForegroundColor Green
    }
    else { Write-Host "`nUninstall did not finish, see the messages above (log: $log)." -ForegroundColor Red }
}

# A QL printer on USB whose port only another vendor's printer uses (its device on e.g. Brother's own driver, which
# install.ps1 never rebinds): our printer is added next to it on that port, as the setup
# window does. The port's description is the printer's USB name ("BrotherQL-800").
function Get-ForeignUsbArgs {
    # Every such port, as comma lists for install.ps1
    $printers = @(Get-Printer -ErrorAction SilentlyContinue)
    $ports = @(); $drivers = @()
    foreach ($port in @(Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "USB*" -and $_.Description -match "^Brother(QL-[0-9A-Z]+)$" })) {
        $driver = "Brother $($port.Description -replace '^Brother', '') (Unidrv)"
        $on = @($printers | Where-Object PortName -eq $port.Name)
        if (-not $on -or ($on | Where-Object DriverName -like "Brother QL-* (Unidrv)")) { continue }
        Write-Host "$($driver -replace ' \(Unidrv\)$', '') on $($port.Name) uses another driver ($(($on | ForEach-Object DriverName | Select-Object -Unique) -join ', ')); it stays, and '$driver' is added on the same port."
        $ports += $port.Name; $drivers += $driver
    }
    if (-not $ports) { return @() }
    @("-UsbPrinter", "-UsbDriverName `"$($drivers -join ',')`"", "-UsbPort $($ports -join ',')")
}

# The guided install / update from this zip.
function Invoke-Install {
    # 1. The right package for this PC
    $isArm = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64"   # the PC's, not the starting program's environment
    $pcName = if ($isArm) { "ARM64" } else { "x64" }
    $pkgArch = if ($isArm) { "arm64" } else { "amd64" }
    $pkg = Join-Path $root "out\package"
    if (-not (Test-Path (Join-Path $pkg "$pkgArch\QLUnidrv.dll"))) {
        Write-Host "This download has no driver for this PC ($pcName). Download the $pcName (or 'both') zip instead." -ForegroundColor Red
        return
    }
    Write-Host "This PC is $pcName. What happens next:"
    Write-Host "  1. The driver is signed with a certificate created for this PC (trusted only on this PC; its"
    Write-Host "     private key is deleted right after signing)."
    if ($isArm) {
        Write-Host "  2. ARM64: the install needs a special Windows start (key 7 after a restart). If this PC isn't in it"
        Write-Host "     yet, the setup prepares it and stops; you restart and run Setup.exe again."
    } else {
        Write-Host "  2. (Only on ARM64 PCs: a special Windows start. Not needed here.)"
    }
    Write-Host "  3. The driver and the programs are installed (one administrator prompt). An installed driver is"
    Write-Host "     updated; your printers and their settings stay."
    Write-Host ""
    if (-not (Ask "Start?")) { return }

    # 2. Sign with the user's own certificate
    Write-Host ""
    Write-Host "Step 1 of 3: sign the driver with your own certificate." -ForegroundColor Cyan
    Write-Host "Windows only installs signed drivers. This creates a private certificate for this PC and signs"
    Write-Host "the driver with it; then its private key is deleted, so nothing can sign with it afterwards."
    try {
        & (Join-Path $PSScriptRoot "package.ps1") -SignOnly -OneTimeKeys | Where-Object { $_ -is [string] -and $_ -match "Created|ready|deleted|Removed" } | ForEach-Object { "  $_" }
    } catch {
        Write-Host ""
        Write-Host "Signing failed: $($_.Exception.Message)" -ForegroundColor Red
        return
    }

    # 3. ARM64: the one-time F7 boot
    if ($isArm) {
        Write-Host ""
        Write-Host "Step 2 of 3: ARM64 needs a special start for the install." -ForegroundColor Cyan
        Write-Host "Windows on ARM64 installs self-signed drivers only in a start with 'Disable driver signature"
        Write-Host "enforcement' (option 7 in the Startup Settings). It applies to that one start only; the driver"
        Write-Host "keeps working afterwards with all protections on."
        if (-not (Ask "Is this PC running in that special start (you pressed 7 after the restart) right now?")) {
            Write-Host ""
            Write-Host "Then:" -ForegroundColor Yellow
            Write-Host "  1. Prepare it: this asks for administrator rights and runs"
            Write-Host "       bcdedit /set {current} onetimeadvancedoptions on"
            Write-Host "     (shows the Startup Settings once at the next restart)."
            Write-Host "  2. Restart. On the blue 'Startup Settings' screen press 7."
            Write-Host "  3. Sign in and run Setup.exe again."
            if (Ask "Prepare the special start now (step 1)?") {
                try {
                    $p = Start-Process cmd.exe -Verb RunAs -ArgumentList '/c bcdedit /set {current} onetimeadvancedoptions on' -Wait -PassThru
                    if ($p.ExitCode -eq 0) { Write-Host "Done. Restart the PC now, press 7, then run Setup.exe again." -ForegroundColor Green }
                    else { Write-Host "bcdedit failed (exit code $($p.ExitCode))." -ForegroundColor Red }
                } catch { Write-Host "Cancelled." -ForegroundColor Yellow }
            }
            return
        }
    } else {
        Write-Host ""
        Write-Host "Step 2 of 3: nothing to do on x64 (it accepts your own certificate)." -ForegroundColor Cyan
    }

    # 4. Install as administrator
    Write-Host ""
    Write-Host "Step 3 of 3: install (asks for administrator rights)." -ForegroundColor Cyan
    Wait-LabelToolClosed
    if ($isArm) { Write-Host "If Windows says it can't verify the publisher, choose 'Install this driver software anyway'." }
    $update = [bool](Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object Name -like "Brother QL-* (Unidrv)")
    if ($update) { Write-Host "The driver is already installed: updating it (printers and their settings stay)." }
    # A QL printer on the network (Ethernet / Wi-Fi): its address; the model is asked from the printer itself.
    $networkArgs = @()
    Write-Host ""
    Write-Host "QL printers connected by USB are found by themselves. Does this PC also print to a QL printer on the"
    Write-Host "network (Ethernet or Wi-Fi, e.g. QL-810W, QL-820NWB, QL-1110NWB)? You need its IP address (shown on its"
    Write-Host "network settings page or the configuration label it prints)."
    if (Ask "Set up a QL printer on the network?") { $networkArgs = @(Read-NetworkPrinter) }

    # The optional status service: only useful when other PCs print to this PC's QL printers through a share.
    $hasService = [bool](Get-Service -Name "QLStatus" -ErrorAction SilentlyContinue)
    Write-Host ""
    Write-Host "Optional: the service 'QL printer status'. Other PCs that print to this PC's QL printers through a"
    Write-Host "printer share can then show their status (roll, errors) too. It runs in the background (as SYSTEM)"
    Write-Host "and reads the printers on this PC's USB ports every 3 seconds. Not needed if only this PC prints."
    Write-Host "You can change this later by running Setup.exe again."
    if ($hasService) { Write-Host "It is installed now." }
    $service = if (Ask "Install the service 'QL printer status'?") { "on" } else { "off" }
    $arguments = @(if ($update) { "-Update" }) + "-StatusService $service" + $networkArgs + @(Get-ForeignUsbArgs)
    if (-not (Run-Elevated ($arguments -join " "))) {
        Write-Host ""
        Write-Host "The install did not finish, see the messages above (log: $log)." -ForegroundColor Red
        return
    }
    Write-Host ""
    $netLine = Select-String -Path $log -Pattern "^Printer '.*' ready on IP_" | Select-Object -First 1
    if ($netLine) { Write-Host ($netLine.Line -replace "^Printer '(.*)' ready on IP_.*$", "Network printer set up: '`$1'.") -ForegroundColor Green }
    $usbLine = Select-String -Path $log -Pattern "^Printer '.*' ready on USB" | Select-Object -First 1
    if ((Select-String -Path $log -Pattern "^No QL printer connected" -Quiet) -and -not $netLine -and -not $usbLine) {
        Write-Host "Installed, but no QL printer was found on USB, so none was set up (fine if this PC only prints to a" -ForegroundColor Yellow
        Write-Host "shared printer). Otherwise switch the printer on and connect it (QL-700/800 series: Editor Lite LED off),"
        Write-Host "then run Setup.exe again$(if ($isArm) { ' (in the special start)' })."
        Write-Host "Start menu: 'QL Label Formats' (label sizes), 'QL Label Maker' and 'QL Printer Status'."
    } else {
        Write-Host "Installed. Your QL printer appears as 'Brother QL-... (Unidrv)'." -ForegroundColor Green
        Write-Host "Start menu: 'QL Label Formats' (label sizes), 'QL Label Maker' and 'QL Printer Status'."
    }
    if ($isArm) { Write-Host "Restart normally when you're done; the driver keeps working." }
    Remove-Log

    # 5. Printer notifications (they must be visible and offered, not hidden)
    $app = Join-Path $env:ProgramFiles "QLUnidrv\QlLabelFormats.exe"
    if (Test-Path $app) {
        Write-Host ""
        Write-Host "The printer status window shows each QL printer's state (roll, errors). Tick 'Notify me about"
        Write-Host "printer errors' there to get a message when a printer needs attention (cover open, wrong roll)."
        if (Ask "Open the printer status window now?") { Start-StatusWindow }
    }
}

# The installed label tool
$appExe = Join-Path $env:ProgramFiles "QLUnidrv\QlLabelFormats.exe"
$hasPackage = Test-Path (Join-Path $root "out\package\qlunidrv.inf")
# The download mark of a zip unpacked with Explorer, as Setup.exe removes it: the label tool
# started from here (--identify) and the copies install.ps1 makes would carry it.
if ($hasPackage) { Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue }

# "0.16.296.925" from a driver's 64-bit version number
function Format-Version([uint64]$v) {
    "{0}.{1}.{2}.{3}" -f ($v -shr 48), (($v -shr 32) -band 0xFFFF), (($v -shr 16) -band 0xFFFF), ($v -band 0xFFFF)
}
function Get-InstalledDrivers { @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object Name -like "Brother QL-* (Unidrv)") }
# The releases, as the setup window compares them (labeltool\src\Versions.cs): a driver by "0.18" of its DriverVer
# (driver\VERSION), the programs by their product version "0.18.1 (build 305.700)" (a build from before the split,
# "0.18.304.1157", counts as 0.18.0). Get-Release and Get-AppRelease are the same as in install.ps1: each script is
# copied alone, so change both.
function Get-Release([string]$v) { if ($v -match "^(\d+)\.(\d+)") { "$($Matches[1]).$($Matches[2])" } }
function Get-AppOrder([string]$product) {
    if ($product -match "^(\d+)\.(\d+)\.(\d+) \(build (\d+)\.(\d+)\)$") { return [version]::new($Matches[1], $Matches[2], $Matches[3], [int]$Matches[4] * 1440 + [int]$Matches[5]) }
    if ($product -match "^(\d+)\.(\d+)\.") { return [version]::new($Matches[1], $Matches[2], 0, 0) }
}
function Get-AppRelease([string]$product) { if ($product -match "^(\d+\.\d+\.\d+) \(build \d+\.\d+\)$") { $Matches[1] } else { Get-Release $product } }
function Get-Product([string]$exe) { if (Test-Path $exe) { (Get-Item $exe).VersionInfo.ProductVersion } }

# Only the programs (install.ps1 -AppOnly): the zip's driver is the installed one, so no signing and no special start.
function Invoke-AppUpdate {
    if (Run-Elevated "-AppOnly") {
        Write-Host "The programs are updated; the driver stays." -ForegroundColor Green
        Remove-Log
    } else { Write-Host "Did not finish, see the messages above (log: $log)." -ForegroundColor Red }
}

# A settings change (install.ps1 -Settings: no driver install), then what it did.
function Invoke-Settings([string]$arguments) {
    if (Run-Elevated "-Settings $arguments") {
        Get-Content $log | Where-Object { $_ -match "ready on|^Driver log|^Service|^Printer at" } | ForEach-Object { Write-Host "  $_" -ForegroundColor Green }
        Remove-Log
    } else { Write-Host "Did not finish, see the messages above (log: $log)." -ForegroundColor Red }
}

function Add-SharedPrinter {
    Write-Host ""
    Write-Host "A QL printer plugged into another PC and shared there (Printer properties > Sharing; German:"
    Write-Host "Druckereigenschaften > Freigabe). First open \\<that PC> in Explorer once, sign in with an account of"
    Write-Host "that PC and tick 'Remember my credentials'. Details: docs/sharing.md on the project page."
    $share = (Read-Host "The share, e.g. \\PC\QL-800 (empty = back)").Trim()
    if (-not $share) { return }
    # As Windows allows it (umlauts, "&", spaces in the share name)
    if ($share -notmatch '^\\\\[^\\/:*?"<>| ]+\\[^\\/\[\]:|<>+=;,?*"]+$') { Write-Host "That is not a share name." -ForegroundColor Yellow; return }
    $drivers = @(Get-InstalledDrivers | Sort-Object { [int]($_.Name -replace '^Brother QL-(\d+).*$', '$1') }, Name)
    Write-Host "Which model is it?"
    for ($i = 0; $i -lt $drivers.Count; $i++) { Write-Host ("  {0,2}. {1}" -f ($i + 1), $drivers[$i].Name) }
    $n = (Read-Host "Number (empty = back)").Trim()
    if (-not ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le $drivers.Count)) { return }
    Invoke-Settings "-SharePort `"$share`" -DriverName `"$($drivers[[int]$n - 1].Name)`""
}

function Show-Menu {
    while ($true) {
        $drivers = Get-InstalledDrivers
        $installed = if ($drivers) { Format-Version $drivers[0].DriverVersion }
        $zip = if ($hasPackage) { ((Get-Content (Join-Path $root "out\package\qlunidrv.inf") | Select-String "^DriverVer=").Line -split ",")[1].Trim() }
        $apps = Get-Product $appExe
        $zipApps = Get-Product (Join-Path $root "out\tools\QlLabelFormats.exe")
        $service = [bool](Get-Service -Name "QLStatus" -ErrorAction SilentlyContinue)
        $logOn = Test-Path (Join-Path $env:ProgramData "QLUnidrv\log\plugin.log")
        Write-Host ""
        $now = if ($drivers) { "programs $(Get-AppRelease $apps), driver $(Get-Release $installed)" } else { "programs only" }
        Write-Host "Installed: $now$(if ($zip) { "   This zip: programs $(Get-AppRelease $zipApps), driver $(Get-Release $zip)" } else { '   (to install or update the driver: Setup.exe of a release zip)' })"
        Write-Host ""
        $items = [ordered]@{}
        # Update, programs only (same driver), repair (same version) or, from an older zip, downgrade - said so, and
        # asked again before it runs.
        $older = $false; $appOnly = $false
        if ($hasPackage) {
            $items["1"] = if (-not $drivers) { "Install the driver from this zip (now only the programs are installed)" }
                          else {
                              $cmp = ([version](Get-Release $zip)).CompareTo([version](Get-Release $installed))
                              $appCmp = if ($apps) { (Get-AppOrder $zipApps).CompareTo((Get-AppOrder $apps)) } else { 1 }
                              $older = $cmp -lt 0
                              $appOnly = $cmp -eq 0 -and $appCmp -ne 0
                              if ($cmp -gt 0) { "Update the driver and programs to $(Get-AppRelease $zipApps) (driver $(Get-Release $zip); printers, settings, label formats stay)" }
                              elseif ($appOnly) { "$(if ($appCmp -lt 0) { 'Go back to the OLDER programs' } else { 'Update the programs to' }) $(Get-AppRelease $zipApps) (the driver $(Get-Release $installed) stays: no signing, no restart)" }
                              elseif ($cmp -eq 0) { "Repair the driver and programs (this zip is the installed version)" }
                              else { "Go back to the OLDER driver $(Get-Release $zip) from this zip (installed $(Get-Release $installed) is newer)" }
                          }
        }
        $items["2"] = "Add a QL printer on the network (IP address)"
        $items["3"] = "Add a QL printer shared by another PC"
        $items["4"] = "Printer status service 'QL printer status': $(if ($service) { 'on -> turn off' } else { 'off -> turn on' }) (printer status for PCs that print through a share)"
        $items["5"] = "Driver log for troubleshooting: $(if ($logOn) { 'on -> turn off' } else { 'off -> turn on' })"
        $items["6"] = "Open the printer status window"
        $items["7"] = "Uninstall"
        $items["0"] = "Exit"
        foreach ($k in $items.Keys) { Write-Host "  $k. $($items[$k])" }
        $choice = (Read-Host "Choice").Trim()
        if (-not $items.Contains($choice)) { continue }
        switch ($choice) {
            "0" { return }
            "1" {
                if ($appOnly) { Invoke-AppUpdate; return }
                if ($older -and -not (Ask "This zip ($(Get-Release $zip)) is older than the installed driver ($(Get-Release $installed)). Install the older version anyway?")) { continue }
                Invoke-Install; return
            }
            "2" { $a = @(Read-NetworkPrinter); if ($a) { Invoke-Settings ($a -join " ") } }
            "3" { Add-SharedPrinter }
            "4" {
                if ($service) { Invoke-Settings "-StatusService off" }
                else {
                    Write-Host "The service reads this PC's QL printers every 3 seconds (as SYSTEM) and publishes their status,"
                    Write-Host "so PCs printing through a share see it too. Only useful when this PC shares its printers."
                    if (Ask "Turn it on?") { Invoke-Settings "-StatusService on" }
                }
            }
            "5" { Invoke-Settings "-Logging $(if ($logOn) { 'off' } else { 'on' })" }
            "6" { Start-StatusWindow }
            "7" { Invoke-Uninstall; return }
        }
    }
}

Write-Host ""
Write-Host " QL Unidrv driver - setup" -ForegroundColor Cyan
Write-Host " ========================"
Write-Host ""

if ($Uninstall) { Invoke-Uninstall }
elseif ((Get-InstalledDrivers) -or (Test-Path $appExe)) { Show-Menu }
elseif ($hasPackage) { Invoke-Install }
else { Write-Host "Nothing is installed, and this folder has no driver package: use Setup.exe from the release zip." -ForegroundColor Yellow }
if ($Pause) { Write-Host ""; [void](Read-Host "Press Enter to close") }
