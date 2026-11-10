# Copy out of the INSTALLED QL Label Maker into Word (PC batch step 12a): a label with a text, a box and a QR code,
# select all, the Maker's own Copy, then Word via COM (invisible, own instance; stops if Word is already running):
# paste, report type and size, export the page as PDF; the PDF is rendered to PNG (600 dpi) and the QR code decoded.
# OVERWRITES THE USER'S CLIPBOARD (only with their OK). Saves nothing in Word; changes nothing else.
#   powershell -File labeltool\test\word-paste.ps1 [-Machine arm64|amd64] [-Maker "C:\Program Files\QLUnidrv\QlLabelMaker.exe"]
# Needs out\tools\uitest\deskrun.exe (tools\uitest\build.cmd), Word, python with numpy + zxing-cpp. Results in out\word-paste.
param([string]$Machine = "arm64", [string]$Maker = "C:\Program Files\QLUnidrv\QlLabelMaker.exe")
$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$out = Join-Path $(if ($env:QL_TEST_OUT) { $env:QL_TEST_OUT } else { Join-Path $root "out" }) "word-paste"
New-Item -ItemType Directory -Force $out | Out-Null
$deskrun = Join-Path $root "out\tools\uitest\deskrun.exe"
if (-not (Test-Path $deskrun)) {
    # a worktree under out\: the main checkout's
    $main = (& git -C $root rev-parse --path-format=absolute --git-common-dir) -replace '[\\/]\.git$', ''
    $deskrun = Join-Path $main "out\tools\uitest\deskrun.exe"
}
if (-not (Test-Path $deskrun)) { throw "deskrun.exe missing: run tools\uitest\build.cmd first" }
if (Get-Process WINWORD -ErrorAction SilentlyContinue) { Write-Output "STOP Word is already running: close it first (not attaching)"; exit 2 }
$csc = Join-Path $env:WINDIR "Microsoft.NET\FrameworkArm64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe" }
$exe = Join-Path $out "word_paste.exe"
$manifest = Join-Path $root "labeltool\app.manifest"; $source = Join-Path $root "labeltool\test\word_paste.cs"
& $csc /nologo /target:winexe /platform:anycpu "/out:$exe" "/win32manifest:$manifest" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:Microsoft.CSharp.dll /r:System.Core.dll $source
if ($LASTEXITCODE) { throw "word_paste build failed" }
Copy-Item (Join-Path $root "labeltool\app.config") "$exe.config" -Force

# The label: a text, a box and a QR code on 62 mm continuous tape
$label = Join-Path $out "copy-out.qllabel"
$json = '{"format": "QL label", "version": 1, "label": {"form": "", "paperId": 259, "length01": 600, "view": "along", "length": "own"}, "objects": [' +
        '{"type": "text", "kind": "line", "x": 4, "y": 4, "w": 34, "h": 8, "rot": 0, "text": "Regal 3", "font": "Segoe UI", "size": 20, "bold": true}, ' +
        '{"type": "box", "x": 4, "y": 16, "w": 18, "h": 12, "rot": 0, "thickness": 0.5, "filled": false, "ellipse": false}, ' +
        '{"type": "barcode", "symbology": "qr", "x": 28, "y": 14, "w": 20, "h": 20, "rot": 0, "data": "https://example.org", "qrType": "url", "qrFields": {"url": "example.org"}, "level": "M"}]}'
[IO.File]::WriteAllText($label, $json, (New-Object Text.UTF8Encoding $false))
$pdf = Join-Path $out "copy-out.pdf"
Remove-Item $pdf -ErrorAction SilentlyContinue

$env:QL_TEST_SILENT = "1"
& $deskrun --lock clipboard --machine $Machine $exe $label $pdf $Maker 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
if (-not (Test-Path $pdf)) { Write-Output "no PDF (exit $code)"; exit 1 }

# PDF page 1 -> PNG at 600 dpi (Windows.Data.Pdf), raw pixels for the check
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Data.Pdf.PdfDocument, Windows.Data.Pdf, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.InMemoryRandomAccessStream, Windows.Storage.Streams, ContentType = WindowsRuntime]
$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq "AsTask" -and $_.GetParameters().Count -eq 1 }
function Await($op, [type]$type) {
    $m = $asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } | Select-Object -First 1
    $t = $m.MakeGenericMethod($type).Invoke($null, @($op)); $t.Wait(); $t.Result
}
function AwaitAction($op) {
    $m = $asTask | Where-Object { $_.GetParameters()[0].ParameterType.Name -eq "IAsyncAction" } | Select-Object -First 1
    $m.Invoke($null, @($op)).Wait()
}
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($pdf)) ([Windows.Storage.StorageFile])
$doc = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
$page = $doc.GetPage(0)
$opt = New-Object Windows.Data.Pdf.PdfPageRenderOptions
$opt.DestinationWidth = [uint32]([Math]::Round($page.Size.Width * 600 / 96))
$opt.DestinationHeight = [uint32]([Math]::Round($page.Size.Height * 600 / 96))
$stream = New-Object Windows.Storage.Streams.InMemoryRandomAccessStream
AwaitAction ($page.RenderToStreamAsync($stream, $opt))
$stream.Seek(0)
$net = [System.IO.WindowsRuntimeStreamExtensions]::AsStreamForRead($stream)
Add-Type -AssemblyName System.Drawing
$bmp = New-Object Drawing.Bitmap $net
$raw = Join-Path $out "page.raw"
$rect = New-Object Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
$data = $bmp.LockBits($rect, "ReadOnly", "Format24bppRgb")
$bytes = New-Object byte[] ($data.Stride * $bmp.Height)
[Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
$bmp.UnlockBits($data)
[IO.File]::WriteAllBytes($raw, $bytes)

$check = & python (Join-Path $root "labeltool\test\word_paste_check.py") $raw $bmp.Width $bmp.Height $data.Stride $pdf
$check | ForEach-Object { "$_" }
# The pasted object cut out (with a margin) as the PNG to look at
$box = ($check | Where-Object { $_ -match "^bbox " }) -replace "^bbox ", ""
if ($box) {
    $v = $box -split " " | ForEach-Object { [int]$_ }
    $m = 60
    $r = New-Object Drawing.Rectangle ([Math]::Max(0, $v[0] - $m)), ([Math]::Max(0, $v[1] - $m)), ($v[2] - $v[0] + 2 * $m), ($v[3] - $v[1] + 2 * $m)
    $crop = $bmp.Clone($r, $bmp.PixelFormat)
    $png = Join-Path $out "copy-out-600dpi.png"
    $crop.Save($png, [Drawing.Imaging.ImageFormat]::Png); $crop.Dispose()
    Write-Output "png $png"
}
$bmp.Dispose(); $net.Dispose()
Remove-Item $raw
if ($code -or ($check -match "^FAIL")) { exit 1 }
