<#
.SYNOPSIS
  Saves a picture of an open window (e.g. "QL Printer Status") without moving the mouse, taking focus or changing
  anything: finds visible top-level windows whose title contains -Title and renders each with PrintWindow.

.DESCRIPTION
  Read only. Works for windows on the caller's desktop (same user session), also when they are covered by others.
  A minimised window can't be rendered (it is skipped with a note).
  Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\window-shot.ps1 -Title "QL Printer Status"
         [-Out out\window-shots] [-Process QlPrinterStatus]
  Prints the saved file names.
#>
param(
    [Parameter(Mandatory = $true)][string]$Title,
    [string]$Out,
    [string]$Process
)
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "..\out\window-shots" }

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class WinShot {
    public delegate bool EnumProc(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc f, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    public static List<IntPtr> Find(string part) {
        var list = new List<IntPtr>();
        EnumWindows((h, p) => {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(512);
            GetWindowTextW(h, sb, 512);
            if (sb.ToString().IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static string TitleOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowTextW(h, sb, 512); return sb.ToString(); }
}
"@

New-Item -ItemType Directory -Force -Path $Out | Out-Null
$found = [WinShot]::Find($Title)
if ($found.Count -eq 0) { Write-Output "No visible window with '$Title' in its title."; exit 1 }
$i = 0
foreach ($h in $found) {
    $procId = [uint32]0
    [void][WinShot]::GetWindowThreadProcessId($h, [ref]$procId)
    $pname = (Get-Process -Id $procId -ErrorAction SilentlyContinue).ProcessName
    if ($Process -and $pname -ne $Process) { continue }
    if ([WinShot]::IsIconic($h)) { Write-Output "Minimised, skipped: $([WinShot]::TitleOf($h)) ($pname)"; continue }
    $r = New-Object WinShot+RECT
    # The visible frame (without the invisible resize border); falls back to the window rectangle.
    if ([WinShot]::DwmGetWindowAttribute($h, 9, [ref]$r, 16) -ne 0) { [void][WinShot]::GetWindowRect($h, [ref]$r) }
    $w = $r.R - $r.L; $hgt = $r.B - $r.T
    if ($w -le 0 -or $hgt -le 0) { continue }
    $full = New-Object WinShot+RECT
    [void][WinShot]::GetWindowRect($h, [ref]$full)
    $bmp = New-Object System.Drawing.Bitmap ($full.R - $full.L), ($full.B - $full.T)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][WinShot]::PrintWindow($h, $hdc, 2)   # PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    $crop = $bmp.Clone((New-Object System.Drawing.Rectangle ($r.L - $full.L), ($r.T - $full.T), $w, $hgt), $bmp.PixelFormat)
    $bmp.Dispose()
    $i++
    $file = Join-Path $Out ("{0}-{1}.png" -f (Get-Date -Format "yyyyMMdd-HHmmss"), $i)
    $crop.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $crop.Dispose()
    Write-Output "$file  ($([WinShot]::TitleOf($h)), $pname)"
}
