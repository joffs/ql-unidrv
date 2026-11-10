# cold-start-sandbox.ps1 - measures the first start of the QL programs with a cold file cache, as after a reboot.
# Runs INSIDE Windows Sandbox only (purge-standby.exe empties the whole PC's file cache; never on a real PC). Changes
# the Sandbox only: optional per-user fonts (copies + HKCU Fonts entries) for the "many fonts" case.
# Must run as the logged-on Sandbox user, elevated (wpr and the purge need admin), the programs installed (Setup.exe).
#
#   powershell -ExecutionPolicy Bypass -File cold-start-sandbox.ps1 -Out C:\out\cold [-Runs 3] [-Purge] [-Wpr]
#       [-FontSource <folder> -FontCount 1800] [-ExeDir "C:\Program Files\QLUnidrv"] [-Probe <exe>]
#
# Per run: optional purge, optional wpr (CPU, DiskIO, FileIO), optional font probe exe (-Probe with
# -ProbeArgs; its first font step on a cold cache, then purged again), QlLabelFormats.exe then QlLabelMaker.exe with
# QL_STARTUP_TRACE=1, each closed once its window is up and the trace says "first idle" (60 s watchdog each).
# Output: <Out>\run-<n>\ (traces, wpr .etl, the probe's output) and <Out>\summary.txt (ms to window per program and run).
param(
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$Runs = 3,
    [switch]$Purge,
    [switch]$Wpr,
    [string]$FontSource,
    [int]$FontCount = 0,
    [string]$ExeDir = "C:\Program Files\QLUnidrv",
    [string]$Probe,
    [string[]]$ProbeArgs = @("styles", "gdi", "gdiplus", "family", "sysfont", "control"),
    [string]$PurgeExe = (Join-Path $PSScriptRoot "purge-standby.exe")
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $Out | Out-Null
$summary = Join-Path $Out "summary.txt"
function Log($text) { Add-Content -Path $summary -Value $text -Encoding UTF8; Write-Host $text }

Log ("cold-start-sandbox {0}  purge={1} wpr={2} fonts={3}" -f (Get-Date -Format s), $Purge, $Wpr, $FontCount)
Log ("CPU {0}, RAM {1:N1} GB" -f (Get-CimInstance Win32_Processor).Name, ((Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory / 1GB))

# --- optional: many per-user fonts (like a PC with Office, Adobe and font collections)
if ($FontSource -and $FontCount -gt 0) {
    $dir = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Fonts"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $key = "HKCU:\Software\Microsoft\Windows NT\CurrentVersion\Fonts"
    if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
    $files = @(Get-ChildItem $FontSource -File | Where-Object { $_.Extension -match '^\.(ttf|otf|ttc)$' })
    if ($files.Count -eq 0) { throw "no fonts in $FontSource" }
    for ($i = 0; $i -lt $FontCount; $i++) {
        $f = $files[$i % $files.Count]
        $name = "ql-cold-{0:D4}-{1}" -f $i, $f.Name     # copies under new names: Windows opens each file
        $target = Join-Path $dir $name
        if (-not (Test-Path $target)) { Copy-Item $f.FullName $target }
        Set-ItemProperty -Path $key -Name ("QL cold {0:D4} (TrueType)" -f $i) -Value $target
    }
    $mb = (Get-ChildItem $dir -File | Measure-Object Length -Sum).Sum / 1MB
    Log ("per-user fonts: {0} files, {1:N0} MB in {2}" -f $FontCount, $mb, $dir)
}

function Defender-Ms {   # Defender's CPU time so far (disk vs. scanning in the cold run)
    $d = Get-Process MsMpEng -ErrorAction SilentlyContinue
    if ($d) { try { return $d.TotalProcessorTime.TotalMilliseconds } catch { } }
    return -1
}

function Start-Traced($exe, $runDir) {
    $defender = Defender-Ms
    $name = [IO.Path]::GetFileNameWithoutExtension($exe)
    $trace = Join-Path $env:TEMP "ql-startup-$name.txt"
    Remove-Item $trace -ErrorAction SilentlyContinue
    $psi = New-Object Diagnostics.ProcessStartInfo $exe
    $psi.UseShellExecute = $false
    $psi.EnvironmentVariables["QL_STARTUP_TRACE"] = "1"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $p = [Diagnostics.Process]::Start($psi)
    $windowMs = -1
    while ($sw.ElapsedMilliseconds -lt 60000) {     # watchdog
        $p.Refresh()
        if ($windowMs -lt 0 -and $p.MainWindowHandle -ne [IntPtr]::Zero) { $windowMs = $sw.ElapsedMilliseconds }
        if ($windowMs -ge 0) { break }
        if ($p.HasExited) { break }
        Start-Sleep -Milliseconds 20
    }
    Start-Sleep -Seconds 2                           # let "first idle" be marked
    if (-not $p.HasExited) { [void]$p.CloseMainWindow(); if (-not $p.WaitForExit(15000)) { $p.Kill() } }
    if (Test-Path $trace) { Copy-Item $trace (Join-Path $runDir "ql-startup-$name.txt") }
    $defenderNow = Defender-Ms
    $defenderMs = if ($defender -ge 0 -and $defenderNow -ge 0) { "{0:N0} ms" -f ($defenderNow - $defender) } else { "n/a" }
    "{0,-16} window after {1,6} ms   cpu {2:N0} ms   MsMpEng cpu {3}" -f $name, $windowMs, $p.TotalProcessorTime.TotalMilliseconds, $defenderMs
}

for ($run = 1; $run -le $Runs; $run++) {
    $runDir = Join-Path $Out "run-$run"
    New-Item -ItemType Directory -Force $runDir | Out-Null
    if ($Purge) {
        & $PurgeExe | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "purge-standby.exe failed ($LASTEXITCODE)" }
        Start-Sleep -Seconds 3
    }
    if ($Wpr) { & wpr.exe -start CPU -start DiskIO -start FileIO -filemode | Out-Null }
    $lines = @()
    if ($Probe) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $probeOut = & $Probe @ProbeArgs 2>&1
        $probeOut | Set-Content (Join-Path $runDir "probe.txt") -Encoding UTF8
        $lines += "{0,-16} done after {1,6} ms" -f "probe", $sw.ElapsedMilliseconds
        if ($Purge) { & $PurgeExe | Out-Null; Start-Sleep -Seconds 3 }
    }
    $lines += Start-Traced (Join-Path $ExeDir "QlLabelFormats.exe") $runDir
    $lines += Start-Traced (Join-Path $ExeDir "QlLabelMaker.exe") $runDir
    if ($Wpr) { & wpr.exe -stop (Join-Path $runDir "cold-start.etl") | Out-Null }
    Log "run $run"
    $lines | ForEach-Object { Log "  $_" }
}
Log "done"
