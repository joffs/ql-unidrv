# Tests install.ps1 -Settings -SharePrinter / -ShareName / -UnsharePrinter (Printer setup's "Share a printer" tab), and
# -RemovePrinter for a printer on a \\host\share port whose host doesn't answer or whose share is gone.
# CHANGES THE PC: adds two test printers on Local Ports named USB098/USB099 (shared printers need a USB port), a folder
# share QLTESTFOLDER, Local Ports \\192.0.2.1\QL-GONE and \\localhost\QL-NO-SUCH-SHARE; shares, renames, unshares and
# removes; everything it added is removed at the end. Meant for a throwaway
# Windows (Windows Sandbox) with the driver installed. Run elevated:
#   powershell -NoProfile -ExecutionPolicy Bypass -File share-test.ps1 [-Install <path to install.ps1>] [-Report <file>]
# Exit code 0 = all cases passed; the report lists every case (PASS/FAIL) with install.ps1's result lines.
param(
    [string]$Install = (Join-Path $PSScriptRoot "..\install.ps1"),
    [string]$Report = (Join-Path $env:TEMP "share-test-report.txt"),
    [string]$Driver = "Brother QL-800 (Unidrv)"
)
$ErrorActionPreference = "Stop"
$Install = (Resolve-Path $Install).Path
$lines = New-Object System.Collections.Generic.List[string]
$failed = 0
function Say([string]$s) { $lines.Add($s); Write-Output $s }

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this elevated (it adds printers and shares)."
}
if (-not (Get-PrinterDriver -Name $Driver -ErrorAction SilentlyContinue)) { throw "The driver '$Driver' is not installed here." }

$p1 = "QL share test"; $p2 = "QL share test 2"; $pFile = "QL share test file"; $pGone = "QL share test gone"
$filePort = Join-Path $env:TEMP "ql-share-test.prn"

function Run-Install([string[]]$arguments) {
    # Windows PowerShell 5.1 turns the child's expected error output (2>&1) into a terminating error under "Stop"
    $ErrorActionPreference = "Continue"
    $result = Join-Path $env:TEMP "share-test-result.txt"
    Remove-Item $result -ErrorAction SilentlyContinue
    $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $Install -Settings @arguments -ResultFile $result 2>&1 | Out-String
    $code = $LASTEXITCODE
    $r = @{}
    if (Test-Path $result) { foreach ($l in Get-Content $result -Encoding UTF8) { $i = $l.IndexOf("="); if ($i -gt 0) { $r[$l.Substring(0, $i)] = $l.Substring($i + 1) } } }
    [pscustomobject]@{ Code = $code; Result = $r; Output = $out.Trim() }
}

function Case([string]$name, [bool]$ok, $run) {
    if ($ok) { Say "PASS $name" } else { Say "FAIL $name"; $script:failed++ }
    if ($run) {
        Say ("     exit " + $run.Code + "; " + (($run.Result.GetEnumerator() | Where-Object { $_.Key -ne "step" } | Sort-Object Key | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join " "))
        if (-not $ok) { Say ("     " + ($run.Output -replace "`r?`n", "`r`n     ")) }
    }
}

function Share-Of([string]$printer) { $p = Get-Printer -Name $printer -ErrorAction SilentlyContinue; if ($p -and $p.Shared) { $p.ShareName } else { $null } }

Say "install.ps1 -SharePrinter / -UnsharePrinter test, $(Get-Date -Format 'yyyy-MM-dd HH:mm'), $env:COMPUTERNAME"
Say "install.ps1: $Install"
Say "Server service: $((Get-Service LanmanServer).Status)"
Say ""
try {
    # Setup: two printers on USB-named Local Ports, one on a file port, a folder share
    foreach ($port in "USB098", "USB099", $filePort) { if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { Add-PrinterPort -Name $port } }
    Add-Printer -Name $p1 -DriverName $Driver -PortName "USB098"
    Add-Printer -Name $p2 -DriverName $Driver -PortName "USB099"
    Add-Printer -Name $pFile -DriverName $Driver -PortName $filePort
    Set-Printer -Name $p2 -Shared $true -ShareName "QLTAKEN"
    $folder = Join-Path $env:TEMP "qltestfolder"
    New-Item -ItemType Directory -Force $folder | Out-Null
    New-SmbShare -Name "QLTESTFOLDER" -Path $folder | Out-Null

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QL-T1")
    Case "a share: shared as QL-T1" ($r.Code -eq 0 -and $r.Result["shared"] -eq $p1 -and $r.Result["sharename"] -eq "QL-T1" -and -not $r.Result.ContainsKey("renamed-from") -and (Share-Of $p1) -eq "QL-T1") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QL-T2")
    Case "b rename: QL-T1 -> QL-T2" ($r.Code -eq 0 -and $r.Result["renamed-from"] -eq "QL-T1" -and $r.Result["sharename"] -eq "QL-T2" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QL-T2")
    Case "c same name again: nothing changes" ($r.Code -eq 0 -and $r.Result["sharename"] -eq "QL-T2" -and -not $r.Result.ContainsKey("renamed-from") -and $r.Output -match "already" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QLTAKEN")
    Case "d name of another printer's share: refused, unchanged" ($r.Code -ne 0 -and $r.Result["error"] -match "taken by the printer" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "qltaken")
    Case "e the same in other case: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "taken" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QLTESTFOLDER")
    Case "f name of a folder share: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "shared folder" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QL,800")
    Case "g invalid name (comma): refused" ($r.Code -ne 0 -and $r.Result["error"] -match "not valid" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", ("Q" * 81))
    Case "h 81 characters: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "not valid") $r

    $r = Run-Install @("-SharePrinter", $pFile, "-ShareName", "QL-FILE")
    Case "i printer not on USB: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "USB port" -and -not (Share-Of $pFile)) $r

    $r = Run-Install @("-SharePrinter", "No such printer", "-ShareName", "QL-X")
    Case "j unknown printer: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "not a QL printer") $r

    $r = Run-Install @("-SharePrinter", $p1)
    Case "k no -ShareName: refused" ($r.Code -ne 0 -and $r.Result["error"] -match "needs -ShareName" -and (Share-Of $p1) -eq "QL-T2") $r

    $r = Run-Install @("-UnsharePrinter", $p1)
    Case "l stop sharing" ($r.Code -eq 0 -and $r.Result["unshared"] -eq $p1 -and -not (Share-Of $p1)) $r

    $r = Run-Install @("-UnsharePrinter", $p1)
    Case "m stop sharing again: no failure" ($r.Code -eq 0 -and $r.Result["unshared"] -eq $p1 -and $r.Output -match "was not shared") $r

    $r = Run-Install @("-SharePrinter", $p1, "-ShareName", "QL-T1")
    Case "n share again with the first name (freed by the rename)" ($r.Code -eq 0 -and (Share-Of $p1) -eq "QL-T1") $r
    $smb = Get-SmbShare -Name "QL-T1" -ErrorAction SilentlyContinue
    Case "o other PCs see it: SMB share QL-T1 is a print queue" ($smb -and "$($smb.ShareType)" -eq "PrintQueue") $null

    # A client printer whose share is gone (status matrix case 5/7): Printer setup's remove must neither fail nor hang.
    # \\192.0.2.1 (TEST-NET-1) never answers; \\localhost\QL-NO-SUCH-SHARE answers "no such share". Windows refuses a
    # Local Port whose target doesn't exist (0x80070035 / 0x80070043; x64 Sandbox 2026-11-06), so the ports are made as
    # a client's would be: the share exists while the port is added and goes afterwards, the unreachable one is written
    # to the registry and the spooler restarted (fine in a throwaway Windows).
    foreach ($gone in @(@{ Case = "p"; Port = "\\192.0.2.1\QL-GONE"; What = "host not reachable" },
                        @{ Case = "q"; Port = "\\localhost\QL-NO-SUCH-SHARE"; What = "share gone" })) {
        if ($gone.Case -eq "q") {
            Set-Printer -Name $p2 -Shared $true -ShareName "QL-NO-SUCH-SHARE"
            Add-PrinterPort -Name $gone.Port
            Add-Printer -Name $pGone -DriverName $Driver -PortName $gone.Port
            Set-Printer -Name $p2 -Shared $false
        } else {
            Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Ports" -Name $gone.Port -Value "" -Type String
            Restart-Service Spooler -Force
            Start-Sleep -Seconds 3
            Add-Printer -Name $pGone -DriverName $Driver -PortName $gone.Port
        }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $r = Run-Install @("-RemovePrinter", $pGone)
        $s = [int]$watch.Elapsed.TotalSeconds
        $left = (Get-Printer -Name $pGone -ErrorAction SilentlyContinue) -or (Get-PrinterPort -Name $gone.Port -ErrorAction SilentlyContinue)
        Case "$($gone.Case) remove a printer on a share, $($gone.What): removed with its port in $s s" ($r.Code -eq 0 -and $r.Result["removed"] -eq $pGone -and -not $left -and $s -lt 30) $r
    }
} catch {
    Say "FAIL setup or harness: $($_.Exception.Message)"; $failed++
} finally {
    foreach ($n in $p1, $p2, $pFile, $pGone) { Remove-Printer -Name $n -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
    foreach ($port in "USB098", "USB099", $filePort, "\\192.0.2.1\QL-GONE", "\\localhost\QL-NO-SUCH-SHARE") { Remove-PrinterPort -Name $port -ErrorAction SilentlyContinue }
    Remove-SmbShare -Name "QLTESTFOLDER" -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $env:TEMP "qltestfolder"), $filePort -Recurse -Force -ErrorAction SilentlyContinue
    $left = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "QL share test*" }).Count
    Say ""
    Say "Cleanup: $left test printer(s) left"
    Say ($(if ($failed) { "RESULT: $failed case(s) FAILED" } else { "RESULT: all cases PASS" }))
    Set-Content -LiteralPath $Report -Value $lines -Encoding UTF8
}
exit $(if ($failed) { 1 } else { 0 })
