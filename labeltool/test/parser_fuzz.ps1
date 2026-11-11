# Builds and runs the parser fuzz test (parser_fuzz.cs): the clipboard/drop/file parsers fed with a few thousand mutated
# inputs from a fixed seed; no exception other than InvalidDataException, no call over 1 s. Changes nothing on the PC.
# Inputs of findings are written to <out>\inputs. Usage:
#   powershell -ExecutionPolicy Bypass -File labeltool\test\parser_fuzz.ps1 [-Count 3000] [-Seed 20261108] [-Out <folder>]
# The saved clipboard dumps instead (each pasted as saved and Count times with one format mutated, default 20):
#   powershell -ExecutionPolicy Bypass -File labeltool\test\parser_fuzz.ps1 -Dumps out\clipboard [-Count 20]
param([int]$Count = 0, [int]$Seed = 20261108, [string]$Out = "", [string]$Dumps = "")
if ($Count -le 0) { $Count = if ($Dumps) { 20 } else { 3000 } }
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Out) { $Out = Join-Path $root "out\test\parser-fuzz" }
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $Out "parser_fuzz.exe"
Push-Location (Join-Path $root "labeltool")
try {
    & $csc /nologo /target:exe /platform:anycpu /warnaserror+ /codepage:65001 "/out:$exe" /main:QlLabels.Tests.ParserFuzz `
        /r:System.Web.Extensions.dll /r:System.ServiceProcess.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll `
        /recurse:src\*.cs test\parser_fuzz.cs
    if ($LASTEXITCODE) { throw "parser_fuzz build failed" }
} finally { Pop-Location }
if ($Dumps) { & $exe $Count $Seed (Join-Path $Out "inputs") --dumps (Resolve-Path $Dumps).Path }
else { & $exe $Count $Seed (Join-Path $Out "inputs") }
exit $LASTEXITCODE
