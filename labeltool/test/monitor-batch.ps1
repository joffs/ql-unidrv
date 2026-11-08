# The checks that need the real 150 % screen, in one run (for a check at the PC while that screen is attached):
#  1. dpi_move.ps1 (every window moved 100 % <-> 150 % and opened directly on each) on the commit before the ScaledForm
#     window conversions and on HEAD, each built in a clean worktree under the output folder; states.txt compared.
#  2. help-shots.ps1 -Scales 100,150: the help pictures taken again and compared with the help (never -Apply).
# Then one summary. Changes nothing on the PC (deskrun desktops of their own, preview modes, nothing printed or saved);
# the worktrees are removed at the end. Needs: the 150 % screen attached, out\tools\uitest\deskrun.exe, the programs
# built (labeltool\build.cmd, for help-shots), the printer "Brother QL-800 (Unidrv)" (only named) or -Printer.
# Usage: powershell -ExecutionPolicy Bypass -File labeltool\test\monitor-batch.ps1
#        [-Base <commit>] [-Printer <name>] [-Lang de,en]
param([string]$Base = "7a30dc6^", [string]$Printer = "Brother QL-800 (Unidrv)", [string]$Lang = "de,en")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$stamp = Get-Date -Format "yyyyMMdd-HHmm"
$out = Join-Path $root "out\pc-batch\monitor-$stamp"
New-Item -ItemType Directory -Force $out | Out-Null
$summary = New-Object System.Collections.Generic.List[string]
function Say([string]$line) { Write-Host $line; $summary.Add($line) }

# 1. dpi_move, before and after
$dpi = @{}
foreach ($side in @(@{ Name = "before"; Rev = $Base }, @{ Name = "after"; Rev = "HEAD" })) {
    $wt = Join-Path $out "wt-$($side.Name)"
    git -C $root worktree add -q --detach $wt $side.Rev 2>&1 | Out-Null
    $rev = (git -C $wt log -1 --format=%h) -join ""
    Write-Host "== dpi_move $($side.Name): $rev"
    $env:QL_TEST_OUT = Join-Path $out "dpi-$($side.Name)"
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "dpi_move.ps1") -Lang $Lang -Src (Join-Path $wt "labeltool") -Out $env:QL_TEST_OUT
    $code = $LASTEXITCODE
    $states = Join-Path $env:QL_TEST_OUT "states.txt"
    $lines = @()
    if (Test-Path $states) { $lines = @(Get-Content $states) }
    $dpi[$side.Name] = $lines
    Say ("dpi_move {0} ({1}): exit {2}, {3} states" -f $side.Name, $rev, $code, $dpi[$side.Name].Count)
    git -C $root worktree remove --force $wt 2>&1 | Out-Null
}
$diff = @()
if ($dpi["before"].Count -and $dpi["after"].Count) { $diff = @(Compare-Object $dpi["before"] $dpi["after"]) }
if ($dpi["after"].Count -eq 0 -or $dpi["before"].Count -eq 0) { Say "dpi_move: NO RESULT (150 % screen attached? see the lines above)" }
elseif ($diff.Count -eq 0) { Say "dpi_move: before = after (all $($dpi['after'].Count) states identical)" }
else {
    Say "dpi_move: $($diff.Count) differing state lines (<= before, => after):"
    $diff | ForEach-Object { Say ("  {0} {1}" -f $_.SideIndicator, $_.InputObject) }
}
$tall = @($dpi["after"] | Where-Object { $_ -match " TALL$" })
if ($tall.Count) { Say "dpi_move: $($tall.Count) state(s) taller than 690 px (logical):"; $tall | ForEach-Object { Say "  $_" } }
$other = @($dpi["after"] | Where-Object { $_ -match " (on-other|open-other) dpi=96 " })
if ($other.Count) { Say "dpi_move: $($other.Count) window(s) did NOT take the 150 % scaling on the other screen:"; $other | ForEach-Object { Say "  $_" } }

# 2. help pictures at 100 and 150 %
Write-Host "== help-shots 100,150"
$env:QL_TEST_OUT = $out
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "help-shots.ps1") -Name "pc-batch" -Scales "100,150" -Printer $Printer
$code = $LASTEXITCODE
$report = Join-Path $out "help-shots\pc-batch\report.txt"
Say ("help-shots: exit {0} ({1}); report {2}" -f $code, @("all pictures unchanged", "pictures changed or missing", "a step failed")[[Math]::Min([Math]::Max($code, 0), 2)], $report)

$file = Join-Path $out "summary.txt"
[IO.File]::WriteAllLines($file, [string[]]$summary)
Write-Host ""
Write-Host "Summary: $file"
