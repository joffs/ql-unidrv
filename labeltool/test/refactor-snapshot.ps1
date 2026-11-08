# Refactor snapshot (for code moves that must not change behaviour): builds labeltool\src with a fixed build stamp and saves what
# "before = after" is checked against, in one folder: the unit test output, the member list (every type and member
# with its signature and IL size), the German texts as Lang.T returns them, and the pictures and .prn files of the UI
# tests. Changes nothing on the PC: the UI tests run through deskrun.exe (a desktop of their own) in preview modes;
# barcode_print prints only to the "QL-800 Capture" file printer. About 25 minutes.
# Usage: powershell -File labeltool\test\refactor-snapshot.ps1 [-Out <folder>] [-Only build,members,strings,uisweep,formats,setup,status,barcode]
#   default -Out: out\refactor-baseline. Compare two snapshots with labeltool\test\refactor-compare.ps1.
param(
    [string]$Out,
    [string]$Only = "build,members,strings,uisweep,formats,setup,status,barcode")
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\.."))
if (-not $Out) { $Out = Join-Path $root "out\refactor-baseline" }
# A relative -Out from the PowerShell location (not .NET's current directory)
$Out = [IO.Path]::GetFullPath($ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out))
$parts = $Only.Split(",") | ForEach-Object { $_.Trim() }
New-Item -ItemType Directory -Force $Out | Out-Null
$env:QL_BUILD_TIME = "2026-11-08T12:00"   # the same About/version texts in every snapshot
$env:QL_TEST_SILENT = "1"
# A folder of its own for everything built and run (programs, test exes, UI test output): several sessions run
# snapshots at the same time.
$work = Join-Path $Out "work"
$env:QL_TOOLS_OUT = Join-Path $work "tools"
$env:QL_TEST_OUT = $work
$summary = New-Object System.Collections.Generic.List[string]
function Note($line) { Write-Host $line; $summary.Add($line) }

# Copies a test's output folder into the snapshot (pictures, reports, .prn; no exes).
function Keep($from, $name) {
    $dest = Join-Path $Out $name
    if (Test-Path $dest) { Remove-Item -Recurse -Force $dest }
    if (-not (Test-Path $from)) { Note "$name`: no output folder $from"; return }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-ChildItem $from -Recurse -File | Where-Object { $_.Extension -notin ".exe", ".pdb", ".config" } | ForEach-Object {
        $rel = $_.FullName.Substring($from.Length).TrimStart("\")
        $target = Join-Path $dest $rel
        New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
        Copy-Item $_.FullName $target
    }
}

function RunTest($name, [scriptblock]$run) {
    $log = Join-Path $Out "$name.log"
    $started = Get-Date
    & $run *>&1 | ForEach-Object { "$_" } | Set-Content -Encoding UTF8 $log
    Note ("{0}: exit {1} ({2:n0} s)" -f $name, $LASTEXITCODE, ((Get-Date) - $started).TotalSeconds)
}

if ($parts -contains "build") {
    RunTest "build" { cmd /c "`"$root\labeltool\build.cmd`"" }
    if ($LASTEXITCODE) { throw "labeltool\build.cmd failed, see $Out\build.log" }
}

$exe = Join-Path $env:QL_TOOLS_OUT "QlLabelFormats.exe"
if ($parts -contains "members" -or $parts -contains "strings") {
    # In a child process: the loaded exe stays locked only there.
    $script = {
        param($exe, $out, $members, $strings)
        $asm = [Reflection.Assembly]::LoadFile($exe)
        $all = [Reflection.BindingFlags]"Public,NonPublic,Instance,Static,DeclaredOnly"
        if ($members) {
            $lines = foreach ($t in $asm.GetTypes()) {
                if ($t.FullName -match "<|\+<>|PrivateImplementationDetails") { continue }  # compiler-made lambdas, iterators
                "TYPE $($t.FullName) : $($t.BaseType)"
                foreach ($m in $t.GetMembers($all)) {
                    if ($m.Name -match "<") { continue }
                    $il = ""
                    if ($m -is [Reflection.MethodBase]) { $b = $m.GetMethodBody(); if ($b) { $il = " IL=" + $b.GetILAsByteArray().Length } }
                    "  $($t.FullName) $($m.MemberType) $m$il"
                }
            }
            $lines | Sort-Object | Set-Content -Encoding UTF8 (Join-Path $out "members.txt")
        }
        if ($strings) {
            $lang = $asm.GetType("QlLabels.Lang")
            $keys = New-Object System.Collections.Generic.SortedSet[string] ([StringComparer]::Ordinal)
            foreach ($f in $lang.GetFields([Reflection.BindingFlags]"NonPublic,Public,Static")) {
                if ($f.FieldType -eq [System.Collections.Generic.Dictionary[string, string]]) { foreach ($k in $f.GetValue($null).Keys) { [void]$keys.Add($k) } }
            }
            $lang.GetMethod("Force").Invoke($null, @("de"))
            $t = $lang.GetMethod("T")
            $rows = foreach ($k in $keys) {
                [string]$key = $k
                [string]$de = $t.Invoke($null, [object[]]@($key))
                ($key -replace "`r", "\r" -replace "`n", "\n") + "`t" + ($de -replace "`r", "\r" -replace "`n", "\n")
            }
            $rows | Set-Content -Encoding UTF8 (Join-Path $out "strings-de.txt")
        }
    }
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes(
        "& {" + $script.ToString() + "} '$exe' '$Out' `$$($parts -contains 'members') `$$($parts -contains 'strings')"))
    powershell -NoProfile -EncodedCommand $encoded
    if ($LASTEXITCODE) { throw "members/strings failed" }
    Note ("members: {0} lines, strings: {1} texts" -f @(Get-Content "$Out\members.txt" -ErrorAction SilentlyContinue).Count, @(Get-Content "$Out\strings-de.txt" -ErrorAction SilentlyContinue).Count)
}

if ($parts -contains "uisweep") {
    RunTest "ui-sweep" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\ui_sweep.ps1" }
    Keep "$work\ui-sweep" "ui-sweep"
}
if ($parts -contains "formats") {
    RunTest "formats-sweep" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\formats-sweep.ps1" -Out "$work\formats-sweep" -App $exe }
    Keep "$work\formats-sweep" "formats-sweep"
}
if ($parts -contains "setup") {
    RunTest "setup-theme" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\setup_theme.ps1" }
    Keep "$work\setup-theme" "setup-theme"
    RunTest "setup-states" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\setup_states.ps1" }
    Keep "$work\setup-states" "setup-states"
    RunTest "setup-open" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\setup_open.ps1" }
    Keep "$work\setup-open" "setup-open"
}
if ($parts -contains "status") {
    RunTest "status-window" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\status-window.ps1" }
}
if ($parts -contains "barcode") {
    RunTest "barcode-print" { powershell -NoProfile -OutputFormat Text -ExecutionPolicy Bypass -File "$root\labeltool\test\barcode_print.ps1" }
    Keep "$work\barcode-test" "barcode-test"
}

$summary.Insert(0, "Snapshot of $(git -C $root rev-parse --short HEAD) $(if (git -C $root status --porcelain -- labeltool) { '(labeltool has uncommitted changes)' }), $(Get-Date -Format s)")
$summary | Set-Content -Encoding UTF8 (Join-Path $Out "summary.txt")
Write-Host "Snapshot: $Out"
