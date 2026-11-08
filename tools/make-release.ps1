<#
.SYNOPSIS
  Makes a release zip: the compiled, UNSIGNED driver package, the label tool and the guided install
  (Setup.exe). The user signs the package on their own PC with their own certificate (with
  Windows' own functions, nothing to install), so nobody has to trust the publisher's certificate.

.DESCRIPTION
  Run build.cmd first (build.cmd both for a zip with both architectures). Output:
  out\release\ql-unidrv-<programs' version>-<x64|arm64|x64-arm64>.zip (labeltool\version\VERSION, e.g. 0.18.1; the
  driver keeps the version of the release in which it last changed, driver\VERSION) with
    Setup.exe (+ .config; the setup window: the label tool's sources built with the setup icon), README.txt,
    LICENSE, THIRD-PARTY-NOTICES.txt - all a user sees at the top (users take the setup window)
    advanced\driver\   setup.ps1 (tools\release), install.ps1, bind-device.ps1, package.ps1, check-signing.ps1
    advanced\out\package\   the staged, unsigned package (package.ps1 -Stage)
    advanced\out\tools\     QlLabelFormats.exe, QlLabelMaker.exe, QlPrinterStatus.exe (+ .config each), qlunidrv.ico,
                            qllabel.ico, built with QL_RELEASE=1 (Brother's factory list never embedded),
                            help\ (the help pages; Setup.exe opens them from here too)
    advanced\labels\default-labels.json, default-labels-en.json, default-labels-en-us.json (the seeds: German, UK, US)
  advanced\ keeps the repository layout, so install.ps1 and package.ps1 run unchanged; Setup.exe finds it there.
  Nothing here needs administrator rights; nothing is signed or installed.
  -HelpCheck: first takes the help pictures again (labeltool\test\help-shots.ps1 -Name <version> -Scales 100,150,
  a deskrun desktop of its own, nothing printed or saved) and only warns when pictures changed or are missing (exit 1)
  or a step failed (exit 2); the zip is made either way. Without a 150 % screen help-shots leaves those pictures out
  with a warning: that warning says the monitor for them is missing. Needs the printer "Brother QL-800 (Unidrv)"
  installed, else -HelpPrinter <an installed QL printer>; report in out\help-shots\<version>\ (or under QL_TEST_OUT).
#>
param([ValidateSet("both", "x64", "arm64")][string]$Arch = "both", [switch]$HelpCheck, [string]$HelpPrinter)
$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
# A driver change needs its version recorded first (driver\gen\version.py set, with the next minor release)
& python -I (Join-Path $repo "driver\gen\version.py") check
if ($LASTEXITCODE) { throw "Driver sources changed: see the line above." }
# Every model of driver\gen\media.py in the INF and in install.ps1's $models (a new model is easy to forget)
& python -I (Join-Path $repo "driver\test\check_models.py")
if ($LASTEXITCODE) { throw "Model lists differ: see the lines above." }
$recorded = (& python -I (Join-Path $repo "driver\gen\version.py") show) -split " "
$version = (Get-Content (Join-Path $repo "labeltool\version\VERSION") -TotalCount 1).Trim()
# The help pictures against the programs as built (only a warning: the zip is made anyway)
if ($HelpCheck) {
    # -Command, not -File: -File would hand "100,150" over as one string, not as the two scales
    $quote = { param($s) "'" + $s.Replace("'", "''") + "'" }
    $call = "& " + (& $quote (Join-Path $repo "labeltool\test\help-shots.ps1")) + " -Name " + (& $quote $version) + " -Scales 100,150"
    if ($HelpPrinter) { $call += " -Printer " + (& $quote $HelpPrinter) }
    & powershell -NoProfile -ExecutionPolicy Bypass -Command "$call; exit `$LASTEXITCODE" | ForEach-Object { "  $_" }
    switch ($LASTEXITCODE) {
        0 { "Help pictures: unchanged." }
        1 { Write-Warning "Help pictures changed or missing: see the help-shots report for $version (help-shots.ps1 -Apply takes them over)." }
        default { Write-Warning "The help picture check did not finish (help-shots.ps1 exit $LASTEXITCODE); the zip is made anyway." }
    }
}
$releaseDir = Join-Path $repo "out\release"
$stageRoot = Join-Path $releaseDir "stage"
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
$stage = Join-Path $stageRoot "ql-unidrv"
$adv = Join-Path $stage "advanced"   # everything but the setup window and the texts
New-Item -ItemType Directory -Force (Join-Path $adv "driver"), (Join-Path $adv "labels") | Out-Null

# Unsigned driver package
$out = & (Join-Path $repo "driver\package.ps1") -Stage -Arch $Arch -Out (Join-Path $adv "out\package")
$out | ForEach-Object { "  $_" }
$inf = Get-Content (Join-Path $adv "out\package\qlunidrv.inf") -Raw
if ($inf -notmatch "DriverVer=[^,]+,([\d.]+)") { throw "DriverVer not found in the staged INF." }
if ($Matches[1] -ne $recorded[1]) { throw "The built driver is $($Matches[1]), driver\VERSION says $($recorded[1]): run build.cmd again." }
$driver = $recorded[0]
$archs = @(Get-ChildItem (Join-Path $adv "out\package") -Directory | ForEach-Object { @{ amd64 = "x64"; arm64 = "arm64" }[$_.Name] } | Sort-Object { $_ -ne "x64" })

# Label tool without Brother's factory list
$tools = Join-Path $adv "out\tools"
$env:QL_RELEASE = "1"; $env:QL_TOOLS_OUT = $tools
try {
    cmd /c "`"$(Join-Path $repo 'labeltool\build.cmd')`"" | ForEach-Object { "  $_" }
    if ($LASTEXITCODE) { throw "labeltool\build.cmd failed." }
} finally { Remove-Item Env:QL_RELEASE, Env:QL_TOOLS_OUT -ErrorAction SilentlyContinue }
# The programs are built from the same sources, so each one is checked.
foreach ($name in "QlLabelFormats.exe", "QlLabelMaker.exe", "QlPrinterStatus.exe", "Setup.exe") {
    $exe = [IO.File]::ReadAllBytes((Join-Path $tools $name))
    if ([Text.Encoding]::ASCII.GetString($exe).Contains("bsq80V.ORG")) { throw "$name contains Brother's factory list; not releasable." }
    $product = (Get-Item (Join-Path $tools $name)).VersionInfo.ProductVersion
    if ($product -notlike "$version (build *") { throw "$name is $product, not $version." }
}
# The help pages (build.cmd copies them next to the programs; install.ps1 installs them from there)
foreach ($page in "help\help.css", "help\de\index.html", "help\en\index.html", "help\formats\de\index.html", "help\formats\en\index.html") {
    if (-not (Test-Path (Join-Path $tools $page))) { throw "$page is missing in the built programs." }
}
if (Get-ChildItem (Join-Path $tools "help") -Recurse -Directory -Filter shots) { throw "The help's screenshot sources (shots\) got into the zip." }

# Scripts, docs, seed
foreach ($f in "install.ps1", "bind-device.ps1", "package.ps1", "check-signing.ps1") {
    Copy-Item (Join-Path $repo "driver\$f") (Join-Path $adv "driver")
}
Copy-Item (Join-Path $PSScriptRoot "release\setup.ps1") (Join-Path $adv "driver")
# The setup window (docs/setup.md): the label tool's build with the setup icon, which opens it when named Setup.exe;
# moved to the zip's root (install.ps1 doesn't install it)
Move-Item (Join-Path $tools "Setup.exe"), (Join-Path $tools "Setup.exe.config") $stage
Copy-Item (Join-Path $PSScriptRoot "publish\public\LICENSE"), (Join-Path $PSScriptRoot "publish\public\THIRD-PARTY-NOTICES.txt") $stage
Copy-Item (Join-Path $repo "labels\default-labels.json"), (Join-Path $repo "labels\default-labels-en.json"),
          (Join-Path $repo "labels\default-labels-en-us.json") (Join-Path $adv "labels")
$readme = (Get-Content (Join-Path $PSScriptRoot "release\README.txt") -Raw).Replace("{VERSION}", $version).Replace("{DRIVER}", $driver).Replace("{ARCHS}", (($archs | ForEach-Object { if ($_ -eq "x64") { "x64 PCs" } else { "ARM64 PCs" } }) -join " and "))
[IO.File]::WriteAllText((Join-Path $stage "README.txt"), $readme.Replace("`r`n", "`n").Replace("`n", "`r`n"), [Text.Encoding]::ASCII)
Get-ChildItem $stage -Recurse -Filter *.cdf | Remove-Item
Get-ChildItem (Join-Path $adv "out") -File | Remove-Item   # package.ps1 leftovers next to package\
# The top level: only the setup window and the texts
$top = @(Get-ChildItem $stage | Where-Object Name -notin "advanced", "Setup.exe", "Setup.exe.config", "README.txt", "LICENSE", "THIRD-PARTY-NOTICES.txt")
if ($top) { throw "Unexpected at the zip's top level: $(($top | ForEach-Object Name) -join ', ')" }

# Zip with one top folder named like the zip
$name = "ql-unidrv-$version-$($archs -join '-')"
Rename-Item $stage $name
$zip = Join-Path $releaseDir "$name.zip"
Get-ChildItem $releaseDir -Filter "ql-unidrv-*.zip" | Remove-Item   # older releases
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Entry names with "/" (ZipFile.CreateFromDirectory on .NET Framework writes "\", which other unzip tools reject)
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in Get-ChildItem (Join-Path $stageRoot $name) -Recurse -File) {
        $entry = $f.FullName.Substring($stageRoot.Length + 1).Replace("\", "/")
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }
Remove-Item $stageRoot -Recurse -Force
"Release: $zip ($([math]::Round((Get-Item $zip).Length / 1KB)) KB): programs $version, driver $driver ($($recorded[1]))"
