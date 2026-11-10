# Architecture check on an ARM64 PC: the programs run natively as ARM64 when an ARM64 program starts them (manifest
# supportedArchitectures), as x64 when an x64 one does. This runs each architecture-dependent path both ways and
# compares: QL Label Formats' test print and a QL Label Maker print on a capture printer (the .prn files must be
# byte-identical), the printing preferences window (PNG each), and the printer status read. Everything runs on
# deskrun's desktop (no focus taken). Prints only on the capture printer; saves, applies and installs nothing.
#   powershell -File labeltool\test\arch-check.ps1 [-Capture "QL-800 Capture"] [-Printer "Brother QL-800 (Unidrv)"] [-Format <name>]
# Needs: out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), labeltool\build.cmd run once (Version.g.cs), python.
# Results in out\arch-check (QL_TEST_OUT: a folder per session). Exit code = failures.
param(
    [string]$Capture = "QL-800 Capture",
    [string]$Printer = "Brother QL-800 (Unidrv)",
    [string]$Format
)
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "arch-check"
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$refs = "/r:System.Web.Extensions.dll", "/r:System.ServiceProcess.dll", "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll"

# Both test exes with the programs' manifest (it decides the architecture)
$check = Join-Path $out "arch_check.exe"; $bprint = Join-Path $out "barcode_print.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$check" /main:QlLabels.Tests.ArchCheck /win32manifest:app.manifest @refs src\*.cs test\arch_check.cs
    if ($LASTEXITCODE) { throw "arch_check build failed" }
    & $csc /nologo /target:winexe /platform:anycpu /codepage:65001 "/out:$bprint" /main:QlLabels.Tests.BarcodePrint /win32manifest:app.manifest @refs src\*.cs test\barcode_print.cs
    if ($LASTEXITCODE) { throw "barcode_print build failed" }
    Copy-Item app.config "$check.config" -Force; Copy-Item app.config "$bprint.config" -Force
} finally { Pop-Location }

# One run through deskrun: x64 (deskrun's default for a .NET program) or ARM64 (deskrun --machine arm64). Output lines of the test exe come back through deskrun.
function Run([string]$arch, [string[]]$arguments) {
    $log = Join-Path $out "run.log"
    if ($arch -eq "ARM64") {
        # deskrun's own native launch (no "start /MACHINE arm64" outside it, rule since 2026-11-08)
        & $deskrun --machine arm64 @arguments > $log 2>&1
    } else {
        & $deskrun @arguments > $log 2>&1
    }
    $code = $LASTEXITCODE
    $text = (Get-Content $log -ErrorAction SilentlyContinue) -join "`n"
    return @{ Code = $code; Text = $text }
}

$failures = 0
function Report([string]$what, [bool]$ok, [string]$detail) {
    if (-not $ok) { $script:failures++ }
    "{0,-5} {1}: {2}" -f $(if ($ok) { "OK" } else { "FAIL" }), $what, $detail
}

$portFile = (Get-Printer $Capture).PortName
if (-not $Format) {
    $list = Get-Content "$env:ProgramData\QLUnidrv\labels.json" -Raw -Encoding UTF8 | ConvertFrom-Json
    $Format = $list.formats[0].name
}
python -I (Join-Path $PSScriptRoot "barcode_cases.py") $out | Out-Null
$case = (Get-Content (Join-Path $out "cases.json") -Raw -Encoding UTF8 | ConvertFrom-Json)[0]
$qllabel = Join-Path $out "$($case.name).qllabel"

foreach ($kind in "formats", "maker") {
    $prn = @{}
    foreach ($arch in "x64", "ARM64") {
        if (Test-Path $portFile) { [IO.File]::Delete($portFile) }
        if ($kind -eq "formats") { $r = Run $arch @("--lock", "capture", $check, "print", $Capture, $Format) }
        else { $r = Run $arch @("--lock", "capture", $bprint, $qllabel, $Capture, "$($case.dpi)") }
        $machine = if ($kind -eq "formats") { if ($r.Text -match "machine (\S+)") { $Matches[1] } else { "?" } } else { $arch + " (deskrun)" }
        $file = Join-Path $out "$kind-$arch.prn"
        if (Test-Path $portFile) { Copy-Item $portFile $file -Force; $prn[$arch] = $file }
        Report "$kind print as $arch" ($r.Code -eq 0 -and (Test-Path $file)) ("machine $machine, exit $($r.Code), " + (($r.Text -split "`n" | Where-Object { $_ -match "printed|error|paper" }) -join "; "))
    }
    if ($prn.Count -eq 2) {
        $same = (Get-FileHash $prn["x64"]).Hash -eq (Get-FileHash $prn["ARM64"]).Hash
        Report "$kind .prn x64 = ARM64" $same ("{0} / {1} bytes" -f (Get-Item $prn["x64"]).Length, (Get-Item $prn["ARM64"]).Length)
    }
}
foreach ($arch in "x64", "ARM64") {
    # Natively only: as x64 Windows shows the preferences through splwow64 on the user's own desktop (not deskrun's),
    # where it takes the focus (found 2026-11-08; the old x64 way of Printer setup's dialogs).
    if ($arch -eq "ARM64") {
        $png = Join-Path $out "prefs-$arch.png"
        $r = Run $arch @($check, "prefs", $Printer, $png)
        Report "printing preferences as $arch" ($r.Code -eq 0 -and (Test-Path $png)) (($r.Text -split "`n" | Where-Object { $_ }) -join "; ")
    }
    $r = Run $arch @($check, "status", $Printer)
    Report "status read as $arch" ($r.Code -eq 0) (($r.Text -split "`n" | Where-Object { $_ }) -join "; ")
}
exit $failures
