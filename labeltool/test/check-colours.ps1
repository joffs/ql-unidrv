# Checks that the programs' windows hold no colours of their own: every colour comes from src\Theme.cs (roles per
# scheme, light / dark / High Contrast), so a colour change is one place and the dark mode can't miss a window.
# Fails (exit 1) on, outside Theme.cs:
#   - SystemColors / SystemBrushes / SystemPens, named colours (Color.White, Brushes.Black, Pens.Gray ...),
#     Color.FromArgb with r, g, b (Color.FromArgb(alpha, Theme...) - a role or fixed colour with transparency - is fine);
#   - a control colour set directly (ForeColor = ..., BackColor = ..., LinkColor = ...): use Theme.Fore / Theme.Back /
#     Theme.Link, so a switch can recolour it (BackColor = Color.Transparent is fine).
# Exceptions: the files that render the label as it prints (white paper, black print) and lines marked
# "// colours: fixed" (or a block between "// colours: fixed begin" and "// colours: fixed end"), e.g. pictures of
# Windows' own screens. Changes nothing. Run by labeltool\build.cmd; by hand:
#   powershell -ExecutionPolicy Bypass -File labeltool\test\check-colours.ps1
param([string]$Src = (Join-Path $PSScriptRoot '..\src'))

$allowedFiles = @(
    'Theme.cs',         # the colours themselves
    'EditorRender.cs',  # the label's bitmap as it prints: white paper, black print
    'EditorModel.cs',   # pictures flattened onto white paper for printing
    'TestLabel.cs',     # the test label's print
    'Strings.de.cs'     # texts only
)
$patterns = @(
    @{ re = '\bSystem(Colors|Brushes|Pens)\.'; why = 'SystemColors / SystemBrushes / SystemPens (use a Theme role)' },
    @{ re = '\b(Color|Brushes|Pens)\.(?!FromArgb\b|Empty\b|Transparent\b)[A-Z][A-Za-z]+\b'; why = 'named colour (use a Theme role or Theme.Fixed)' },
    @{ re = 'Color\.FromArgb\(\s*[^,()]+,\s*[^,()]+,'; why = 'Color.FromArgb with r, g, b (use a Theme role or Theme.Fixed)' },
    @{ re = '\b(ForeColor|BackColor|LinkColor|ActiveLinkColor|VisitedLinkColor)\s*=(?!=)(?!\s*Color\.Transparent\b)'; why = 'control colour set directly (use Theme.Fore / Theme.Back / Theme.Link)' }
)

$failures = 0
foreach ($file in Get-ChildItem -Path $Src -Filter *.cs) {
    if ($allowedFiles -contains $file.Name) { continue }
    $inFixed = $false
    $n = 0
    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $n++
        if ($line -match '//\s*colours: fixed begin') { $inFixed = $true; continue }
        if ($line -match '//\s*colours: fixed end') { $inFixed = $false; continue }
        if ($inFixed -or $line -match '//\s*colours: fixed\b') { continue }
        $code = ($line -replace '//.*$', '')   # comments may name colours
        foreach ($p in $patterns) {
            if ($code -match $p.re) {
                Write-Host ("FAIL: colours: {0}:{1}: {2}: {3}" -f $file.Name, $n, $p.why, $line.Trim())
                $failures++
            }
        }
    }
}
if ($failures -gt 0) { Write-Host "colours: $failures place(s) with a colour outside Theme.cs"; exit 1 }
Write-Host 'colours: OK (all window colours from Theme.cs)'
exit 0
