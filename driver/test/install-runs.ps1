# Runs install.ps1 through every mode in a fixed order and records, per run, the exit code, the -ResultFile, the
# log (transcript body) and a snapshot of what is installed, normalised (no times, thumbprints, oemNN, GUIDs), so two
# runs (before / after a change to install.ps1 or setup.ps1) can be compared file by file with -Compare.
# CHANGES THE PC: installs the driver, printers, ports, the status service, shares, and uninstalls everything at the
# end. Meant ONLY for a throwaway Windows (Windows Sandbox), never a real PC. Run elevated, from the repository
# (driver\test is not in the release zip), against an unpacked release zip:
#   powershell -NoProfile -ExecutionPolicy Bypass -File driver\test\install-runs.ps1 -Zip <unpacked zip> -Out <folder>
# (-Zip may also be a folder that holds driver\install.ps1 and out\package directly). The expected exit codes are
# guesses until the first (baseline) run; the before/after proof is -Compare, not the codes.
# -Compare <folder A> -With <folder B>: only compares two earlier -Out folders (changes nothing; any PC).
# Exit code 0 = every run ended as expected (see the table at the top of the report); with -Compare 0 = identical.
param(
    [string]$Zip,
    [string]$Out = (Join-Path $env:TEMP "install-runs"),
    [string]$Compare,
    [string]$With
)
$ErrorActionPreference = "Stop"

# PS>TerminatingError(...) lines are left out: the transcript writes one whenever a throw leaves a function, so they
# change when code moves into functions without the script doing anything else (install.ps1's modes, 2026-11-08)
function Read-Norm([string]$file) {
    if (-not (Test-Path $file)) { return @("<missing>") }
    $c = @(Get-Content $file -Encoding UTF8 | Where-Object { $_ -notmatch '^PS>TerminatingError\(' })
    if ($c.Count -eq 0) { @("<empty>") } else { $c }
}

if ($Compare) {
    if (-not $With) { throw "-Compare <folder A> needs -With <folder B>." }
    $a = $Compare; $b = $With
    $names = @(Get-ChildItem $a, $b -Filter *.txt | ForEach-Object Name | Sort-Object -Unique)
    $diff = 0
    foreach ($n in $names) {
        $d = Compare-Object (Read-Norm (Join-Path $a $n)) (Read-Norm (Join-Path $b $n)) -SyncWindow 1000
        if ($d) { $diff++; "DIFF $n"; $d | ForEach-Object { "  $($_.SideIndicator) $($_.InputObject)" } } else { "same $n" }
    }
    "$diff of $($names.Count) files differ."
    exit [int]($diff -gt 0)
}

if (-not $Zip) { throw "-Zip <unpacked release zip folder> is required." }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this elevated, in Windows Sandbox only."
}
$adv = if (Test-Path (Join-Path $Zip "advanced\driver\install.ps1")) { Join-Path $Zip "advanced" } else { $Zip }
$install = Join-Path $adv "driver\install.ps1"
$package = Join-Path $adv "driver\package.ps1"
$installed = Join-Path $env:ProgramFiles "QLUnidrv\setup\install.ps1"
$shareTest = Join-Path $PSScriptRoot "share-test.ps1"
New-Item -ItemType Directory -Force $Out | Out-Null
$summary = New-Object System.Collections.Generic.List[string]
$failed = 0
$driver = "Brother QL-800 (Unidrv)"

# Times, thumbprints, oemNN.inf, GUIDs, temp names and durations out; the rest stays as it was written
function Normalize([string[]]$lines) {
    foreach ($l in $lines) {
        $l = $l -replace '\b(\d{4}-\d{2}-\d{2}|\d{1,2}[./]\d{1,2}[./]\d{4})\b', '<date>' -replace '\b\d{1,2}:\d{2}(:\d{2})?\b', '<time>'
        $l = $l -replace '\b[0-9A-Fa-f]{40}\b', '<thumb>' -replace '\boem\d+\.inf\b', 'oemN.inf'
        $l = $l -replace '\{?[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}?', '<guid>'
        $l = $l -replace '\b\d+([.,]\d+)? ?(s|ms|seconds?)\b', '<n> s' -replace '\\Temp\\[^\\" ]+', '\Temp\<tmp>'
        $l
    }
}

# The script's own output from a transcript (between the second and the second-to-last "****" ruler)
function Transcript-Body([string]$file) {
    if (-not (Test-Path $file)) { return @("<no log>") }
    $all = @(Get-Content $file)
    $r = @(for ($i = 0; $i -lt $all.Count; $i++) { if ($all[$i].StartsWith("*****")) { $i } })
    $from = if ($r.Count -ge 2) { $r[1] + 1 } else { 0 }
    $to = if ($r.Count -ge 4) { $r[$r.Count - 2] } else { $all.Count }
    @($all[$from..([Math]::Max($from, $to - 1))] | Where-Object { $_.Trim().Length -gt 1 -and -not $_.StartsWith("*****") })
}

function Snapshot {
    "== printers"
    Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.DriverName -like "Brother QL-* (Unidrv)" } | Sort-Object Name |
        ForEach-Object { "$($_.Name) | $($_.DriverName) | $($_.PortName) | shared=$($_.Shared) $($_.ShareName)" }
    "== ports (not USB/LPT/COM/default)"
    Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -notmatch '^(USB\d|LPT|COM|FILE:|PORTPROMPT:|nul:|SHRFAX:|PDF)' } | Sort-Object Name | ForEach-Object Name
    "== drivers"
    @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object Name -like "Brother QL-* (Unidrv)").Count
    "== packages"
    (pnputil /enum-drivers | Out-String) -split "`r?`n`r?`n" | Where-Object { $_ -match 'qlunidrv\.inf' } | ForEach-Object { ($_ -split "`r?`n" | Where-Object { $_ -match 'Original Name|Originalname|Driver Version|Treiberversion|Signer|Signierer' }) -join " ; " }
    "== certificates"
    foreach ($s in "Root", "TrustedPublisher") { "$s " + @(Get-ChildItem "Cert:\LocalMachine\$s" | Where-Object Subject -like "CN=QL Unidrv*").Count }
    "== service"
    $svc = Get-Service QLStatus -ErrorAction SilentlyContinue; if ($svc) { "QLStatus $($svc.Status) $($svc.StartType)" } else { "none" }
    "== program files"
    $pf = Join-Path $env:ProgramFiles "QLUnidrv"
    if (Test-Path $pf) { Get-ChildItem $pf -Recurse -File | ForEach-Object { $_.FullName.Substring($pf.Length) } | Sort-Object } else { "none" }
    "== program data"
    $pd = Join-Path $env:ProgramData "QLUnidrv"
    if (Test-Path $pd) {
        Get-ChildItem $pd -Recurse | ForEach-Object { $_.FullName.Substring($pd.Length) } | Sort-Object
        foreach ($d in $pd, "$pd\log", "$pd\status") { if (Test-Path $d) { (icacls $d | Out-String).Trim() } }
    } else { "none" }
    "== uninstall entry"
    $k = Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QLUnidrv" -ErrorAction SilentlyContinue
    if ($k) { "DisplayName=$($k.DisplayName) Publisher=$($k.Publisher) UninstallString=$($k.UninstallString) ModifyPath=$($k.ModifyPath)" } else { "none" }
    "== start menu, file type, monitor"
    Get-ChildItem "$env:ProgramData\Microsoft\Windows\Start Menu\Programs" -Filter "QL *.lnk" -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object Name
    ".qllabel=" + (Test-Path "HKLM:\SOFTWARE\Classes\.qllabel")
    "monitor=" + [bool](Get-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "QL printer monitor" -ErrorAction SilentlyContinue)
}

$n = 0
function Run([string]$name, [string]$script, [string[]]$arguments, [int]$expectCode = 0) {
    $script:n++
    $id = "{0:D2}-{1}" -f $script:n, $name
    $log = Join-Path $env:TEMP "install-runs.log"; $res = Join-Path $env:TEMP "install-runs.txt"
    Remove-Item $log, $res -ErrorAction SilentlyContinue
    $ErrorActionPreference = "Continue"
    $console = & powershell -NoProfile -ExecutionPolicy Bypass -File $script @arguments -Log $log -ResultFile $res 2>&1 | Out-String
    $code = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    $result = if (Test-Path $res) { @(Get-Content $res -Encoding UTF8) } else { @("<no result file>") }
    Set-Content (Join-Path $Out "$id.result.txt") (@("exit=$code") + (Normalize $result)) -Encoding UTF8
    Set-Content (Join-Path $Out "$id.log.txt") (Normalize (Transcript-Body $log)) -Encoding UTF8
    Set-Content (Join-Path $Out "$id.state.txt") (Normalize (Snapshot)) -Encoding UTF8
    Set-Content (Join-Path $Out "$id.console.raw") $console -Encoding UTF8   # .raw: not compared (times, progress)
    $ok = $code -eq $expectCode
    if (-not $ok) { $script:failed++ }
    $summary.Add(("{0} {1}  exit {2} (expected {3})  {4}" -f ($(if ($ok) { "OK  " } else { "FAIL" })), $id, $code, $expectCode,
                  (($result | Where-Object { $_ -notmatch '^step=' }) -join " ")))
}

# Signing as Setup.exe does it (one-time keys), then every mode
$ErrorActionPreference = "Continue"
& powershell -NoProfile -ExecutionPolicy Bypass -File $package -SignOnly -OneTimeKeys 2>&1 | Out-Null
$ErrorActionPreference = "Stop"
$summary.Add("sign: package.ps1 -SignOnly -OneTimeKeys exit $LASTEXITCODE")

Run "fresh-install"      $install   @("-TestPrinter", "-StatusService", "off")
Run "update"             $install   @("-Update", "-TestPrinter")
Run "app-only"           $install   @("-AppOnly")
Run "log-on"             $installed @("-Settings", "-Logging", "on")
Run "log-off"            $installed @("-Settings", "-Logging", "off")
Run "service-on"         $installed @("-Settings", "-StatusService", "on")
Run "service-off"        $installed @("-Settings", "-StatusService", "off")
Run "network-printer"    $installed @("-Settings", "-NetworkPrinter", "192.0.2.10", "-DriverName", $driver)
Run "network-no-answer"  $installed @("-Settings", "-NetworkPrinter", "192.0.2.11") 1
Run "network-bad-name"   $installed @("-Settings", "-NetworkPrinter", "bad name!") 1
Run "share-port"         $installed @("-Settings", "-SharePort", "\\192.0.2.1\QL-800", "-DriverName", $driver)
Run "share-port-again"   $installed @("-Settings", "-SharePort", "\\192.0.2.1\QL-800", "-DriverName", $driver)
Run "remove-network"     $installed @("-Settings", "-RemovePrinter", $driver)
Run "remove-share-port"  $installed @("-Settings", "-RemovePrinter", "$driver on 192.0.2.1")
Run "remove-unknown"     $installed @("-Settings", "-RemovePrinter", "No such printer") 1
Run "unknown-driver"     $installed @("-Settings", "-SharePort", "\\192.0.2.1\QL-X", "-DriverName", "Brother QL-9999 (Unidrv)") 1

# Share / rename / unshare / remove cases (their own report; it adds and removes its own printers)
if (Test-Path $shareTest) {
    $ErrorActionPreference = "Continue"
    & powershell -NoProfile -ExecutionPolicy Bypass -File $shareTest -Install $installed -Report (Join-Path $env:TEMP "share-test-report.txt") 2>&1 | Out-Null
    $code = $LASTEXITCODE
    $ErrorActionPreference = "Stop"
    Set-Content (Join-Path $Out "share-test.txt") (Normalize (Get-Content (Join-Path $env:TEMP "share-test-report.txt") | Select-Object -Skip 1)) -Encoding UTF8
    $summary.Add("share-test.ps1 exit $code"); if ($code) { $failed++ }
}

Run "uninstall-keep"     $installed @("-Uninstall", "-KeepLabels")
# The uninstall removed the certificates: sign again, as Setup.exe does before every install
$ErrorActionPreference = "Continue"
& powershell -NoProfile -ExecutionPolicy Bypass -File $package -SignOnly -OneTimeKeys 2>&1 | Out-Null
$ErrorActionPreference = "Stop"
$summary.Add("sign again: exit $LASTEXITCODE")
Run "reinstall"          $install   @("-TestPrinter")
Run "uninstall"          $installed @("-Uninstall")

$head = @("install-runs: $install", "zip: $Zip", "") + $summary + @("", "$failed failed.")
Set-Content (Join-Path $Out "_summary.txt") $head -Encoding UTF8
$head
exit [int]($failed -gt 0)
