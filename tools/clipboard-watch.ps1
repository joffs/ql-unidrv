# clipboard-watch.ps1 - saves every new copy from one program as a numbered clipboard dump (read-only).
#
# Start it, then copy in the program again and again (Ctrl+C); each copy is saved by clipboard-formats.ps1 -SaveData
# to <Folder>\<Prefix>01, <Prefix>02, ... (numbers already there are skipped). Stop with Ctrl+C in this window.
#   powershell -NoProfile -File <repo>\tools\clipboard-watch.ps1
#   powershell -NoProfile -File <repo>\tools\clipboard-watch.ps1 -Prefix corel-s -Program CorelDRW -Folder <folder>
# Changes nothing on the clipboard: it only reads its sequence number and owner, then lets clipboard-formats.ps1 read
# it. No admin. Copies by other programs are ignored (and said so).
param([string]$Prefix = 'corel-s', [string]$Program = 'CorelDRW', [string]$Folder)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Folder) { $Folder = Join-Path $repo 'out\clipboard' }
$formats = Join-Path $PSScriptRoot 'clipboard-formats.ps1'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClipWatch
{
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    public static int OwnerPid()
    {
        IntPtr h = GetClipboardOwner();
        if (h == IntPtr.Zero) return 0;
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return (int)pid;
    }
}
'@

function Owner {
    $id = [ClipWatch]::OwnerPid()
    if ($id -eq 0) { return '' }
    try { return (Get-Process -Id $id).ProcessName } catch { return '' }
}

function NextFolder {
    for ($i = 1; $i -lt 1000; $i++) {
        $f = Join-Path $Folder ('{0}{1:00}' -f $Prefix, $i)
        if (-not (Test-Path $f)) { return $f }
    }
    throw "no free number under $Folder"
}

New-Item -ItemType Directory -Force -Path $Folder | Out-Null
Write-Host "Waiting for copies from $Program (saved under $Folder as ${Prefix}01, ${Prefix}02, ...). Stop: Ctrl+C."
$last = [ClipWatch]::GetClipboardSequenceNumber()
while ($true) {
    Start-Sleep -Milliseconds 400
    $now = [ClipWatch]::GetClipboardSequenceNumber()
    if ($now -eq $last) { continue }
    $last = $now
    Start-Sleep -Milliseconds 600   # the program may still be adding formats
    $last = [ClipWatch]::GetClipboardSequenceNumber()
    $who = Owner
    if ($who -ne $Program) { Write-Host "  (a copy from '$who' ignored)"; continue }
    $target = NextFolder
    & powershell -NoProfile -File $formats -SaveData $target -Preview 60 | Out-Null
    Write-Host ("saved {0}  {1:HH:mm:ss}" -f (Split-Path -Leaf $target), (Get-Date))
}
