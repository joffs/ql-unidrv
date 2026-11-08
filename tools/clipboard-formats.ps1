# clipboard-formats.ps1 - lists the formats a program put on the Windows clipboard (read-only).
#
# Copy something in any program (a shape, a text, a picture), then run:
#   powershell -NoProfile -File <repo>\tools\clipboard-formats.ps1
#   powershell -NoProfile -File <repo>\tools\clipboard-formats.ps1 -SaveData <folder>
# Prints every format: number, name, size in bytes and, for text formats, the first characters.
# -SaveData also writes each format's raw bytes to <folder> (one file per format, EMF as .emf), so a reader
# for that format can be tested later without the program. The report goes to <folder>\formats.txt too.
# Changes nothing: the clipboard is opened for reading only and closed again. No admin, any PC (ARM64 and x64).
param([string]$SaveData, [int]$Preview = 300)

$ErrorActionPreference = 'Stop'
# The previews are Unicode: without this the console shows umlauts as "?" (formats.txt is UTF-8 either way).
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class ClipFormats
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] static extern bool CloseClipboard();
    [DllImport("user32.dll")] static extern uint EnumClipboardFormats(uint format);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClipboardFormatName(uint format, StringBuilder name, int max);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
    [DllImport("kernel32.dll")] static extern UIntPtr GlobalSize(IntPtr h);
    [DllImport("gdi32.dll")] static extern uint GetEnhMetaFileBits(IntPtr h, uint size, byte[] data);

    static readonly Dictionary<uint, string> Std = new Dictionary<uint, string> {
        {1,"CF_TEXT"},{2,"CF_BITMAP"},{3,"CF_METAFILEPICT"},{4,"CF_SYLK"},{5,"CF_DIF"},{6,"CF_TIFF"},{7,"CF_OEMTEXT"},
        {8,"CF_DIB"},{9,"CF_PALETTE"},{10,"CF_PENDATA"},{11,"CF_RIFF"},{12,"CF_WAVE"},{13,"CF_UNICODETEXT"},
        {14,"CF_ENHMETAFILE"},{15,"CF_HDROP"},{16,"CF_LOCALE"},{17,"CF_DIBV5"},{0x80,"CF_OWNERDISPLAY"},
        {0x81,"CF_DSPTEXT"},{0x82,"CF_DSPBITMAP"},{0x83,"CF_DSPMETAFILEPICT"},{0x8E,"CF_DSPENHMETAFILE"} };

    public class Entry { public uint Id; public string Name; public long Size = -1; public byte[] Data; public string Note; }

    public static uint OwnerPid()
    {
        uint pid = 0; var w = GetClipboardOwner();
        if (w != IntPtr.Zero) GetWindowThreadProcessId(w, out pid);
        return pid;
    }

    public static List<Entry> Read(bool withData)
    {
        var list = new List<Entry>();
        bool open = false;
        for (int i = 0; i < 20 && !(open = OpenClipboard(IntPtr.Zero)); i++) System.Threading.Thread.Sleep(50);
        if (!open) throw new InvalidOperationException("The clipboard is in use by another program; try again.");
        try
        {
            uint f = 0;
            while ((f = EnumClipboardFormats(f)) != 0)
            {
                var e = new Entry { Id = f };
                string n;
                if (Std.TryGetValue(f, out n)) e.Name = n;
                else { var sb = new StringBuilder(256); e.Name = GetClipboardFormatName(f, sb, 256) > 0 ? sb.ToString() : "?"; }
                list.Add(e);
                // Bitmap and palette handles are GDI objects, not memory: their size isn't read (CF_DIB has the same picture).
                if (f == 2 || f == 9 || f == 3 || f == 0x80) { e.Note = "GDI handle"; continue; }
                IntPtr h;
                try { h = GetClipboardData(f); } catch (Exception) { h = IntPtr.Zero; }
                if (h == IntPtr.Zero) { e.Note = "no data (delayed rendering failed)"; continue; }
                if (f == 14 || f == 0x8E)
                {
                    uint size = GetEnhMetaFileBits(h, 0, null);
                    e.Size = size;
                    if (withData && size > 0) { e.Data = new byte[size]; GetEnhMetaFileBits(h, size, e.Data); }
                    continue;
                }
                long len = (long)GlobalSize(h).ToUInt64();
                e.Size = len;
                if (len <= 0) continue;
                bool text = f == 1 || f == 7 || f == 13 || f == 15 || e.Name.IndexOf("svg", StringComparison.OrdinalIgnoreCase) >= 0
                         || e.Name.IndexOf("html", StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf("text", StringComparison.OrdinalIgnoreCase) >= 0
                         || e.Name.IndexOf("xml", StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf("rtf", StringComparison.OrdinalIgnoreCase) >= 0
                         || e.Name.IndexOf("url", StringComparison.OrdinalIgnoreCase) >= 0 || e.Name.IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!withData && !text) continue;
                IntPtr p = GlobalLock(h);
                if (p == IntPtr.Zero) continue;
                try { e.Data = new byte[len]; Marshal.Copy(p, e.Data, 0, (int)len); }
                finally { GlobalUnlock(h); }
            }
        }
        finally { CloseClipboard(); }
        return list;
    }
}
'@

$entries = [ClipFormats]::Read([bool]$SaveData)
$lines = New-Object System.Collections.Generic.List[string]
$ownerPid = [ClipFormats]::OwnerPid()
$owner = '?'
if ($ownerPid -gt 0) { try { $owner = (Get-Process -Id $ownerPid).ProcessName } catch { } }
$lines.Add(("Clipboard {0:yyyy-MM-dd HH:mm:ss}, put there by: {1}, {2} format(s)" -f (Get-Date), $owner, $entries.Count))
$lines.Add('')

function Preview($e) {
    if ($null -eq $e.Data -or $Preview -le 0) { return $null }
    $id = $e.Id
    if ($id -eq 15) {
        # DROPFILES: offset (4 bytes) and a wide flag at byte 16, then the file names
        $off = [BitConverter]::ToInt32($e.Data, 0); $wide = [BitConverter]::ToInt32($e.Data, 16) -ne 0
        $s = if ($wide) { [Text.Encoding]::Unicode.GetString($e.Data, $off, $e.Data.Length - $off) } else { [Text.Encoding]::Default.GetString($e.Data, $off, $e.Data.Length - $off) }
        return (($s -split "`0") | Where-Object { $_ }) -join ' | '
    }
    if ($id -eq 13) { $s = [Text.Encoding]::Unicode.GetString($e.Data) }
    elseif ($id -eq 1) { $s = [Text.Encoding]::Default.GetString($e.Data) }   # ANSI code page
    elseif ($id -eq 7) { $s = [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage).GetString($e.Data) }
    elseif ($e.Name -match 'svg|html|xml|json|url|rtf') {
        if ($e.Data.Length -ge 2 -and $e.Data[1] -eq 0) { $s = [Text.Encoding]::Unicode.GetString($e.Data) } else { $s = [Text.Encoding]::UTF8.GetString($e.Data) }
    }
    else { return $null }
    $s = ($s -replace "`0.*$", '' -replace '\s+', ' ').Trim()
    if ($s.Length -gt $Preview) { $s = $s.Substring(0, $Preview) + '...' }
    return $s
}

foreach ($e in $entries) {
    $size = if ($e.Size -ge 0) { '{0,10:N0} bytes' -f $e.Size } else { '{0,16}' -f '' }
    $line = '{0,6}  {1,-34} {2}' -f $e.Id, $e.Name, $size
    if ($e.Note) { $line += "  ($($e.Note))" }
    $lines.Add($line)
    $p = Preview $e
    if ($p) { $lines.Add("        " + $p) }
}

$lines | ForEach-Object { Write-Output $_ }

if ($SaveData) {
    New-Item -ItemType Directory -Force -Path $SaveData | Out-Null
    foreach ($e in $entries) {
        if ($null -eq $e.Data) { continue }
        $safe = ($e.Name -replace '[^A-Za-z0-9._+-]', '_')
        $ext = if ($e.Id -eq 14) { '.emf' } elseif ($e.Name -match 'svg') { '.svg' } elseif ($e.Name -match '^PNG$|image/png') { '.png' } else { '.bin' }
        [IO.File]::WriteAllBytes((Join-Path $SaveData ('{0:D5}-{1}{2}' -f $e.Id, $safe, $ext)), $e.Data)
    }
    [IO.File]::WriteAllLines((Join-Path $SaveData 'formats.txt'), $lines)
    Write-Output ''
    Write-Output "Saved to $SaveData"
}
