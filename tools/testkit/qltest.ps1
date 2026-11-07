<#
.SYNOPSIS
  QL printer test kit: reads a Brother QL label printer's status over USB and prints the kit's test labels
  (streams made by the "QL Unidrv driver" project) directly on it, then writes a report to send back.

.DESCRIPTION
  No installation and no administrator rights: the script opens the printer's USB printer interface the same
  way Brother's status monitor does, next to whatever driver is installed (Brother's or none). Nothing on the
  PC is changed. Print nothing else on that printer while the script runs.

  Usage (in the kit folder):
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1              guided test (status, labels, report)
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -StatusOnly  only read the status and write the report
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -List        list connected QL printers
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -Print <file.prn> [-Force]   send one stream
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -Network <IP address or printer name> [-Watch 60]
        a printer on the network (Ethernet / Wi-Fi): its status over SNMP, as Brother's software reads it; -Watch
        records every change for that many seconds (open the cover, print...); then optionally the 62 mm test
        label over the network (port 9100). -StatusOnly: no label. -Community: if SNMP isn't "public".
    powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -Details     also show the technical lines (dimmed)

  The screen shows one line per step; the technical lines (status bytes, stream files, SNMP values) always go into
  the report, and with -Details onto the screen too. Each test's expected picture is shown in the kit's own window.
  The report (qltest-report-<model>-<time>.txt) is written next to this script. Run from the project's source
  folder (tools\testkit), it uses the built kit (out\testkit\kit, make-testkit.ps1) and writes the report to
  out\testkit\reports. With several QL printers connected, the list shows each one's port, Windows printer name,
  model and loaded roll.
#>
param(
    [switch]$StatusOnly,
    [switch]$List,
    [string]$Print = "",
    [switch]$Force,        # print even if the loaded roll doesn't match the stream
    [string]$Device = "",  # several QL printers: pick one by port (USB001), Windows printer name or device path part
    [string]$Network = "", # a printer on the network: its IP address / host name, or a Windows printer on a TCP/IP port
    [string]$Community = "public",  # -Network: SNMP community (Brother's software always uses "public")
    [int]$Watch = 0,       # -Network: also record every status change for this many seconds
    [switch]$Details       # also show the report's technical lines on the screen (dimmed)
)
$ErrorActionPreference = "Stop"
$kitDir = $PSScriptRoot

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

public static class QlUsb
{
    const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000, FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
    const uint OPEN_EXISTING = 3, FILE_FLAG_OVERLAPPED = 0x40000000;
    const int ERROR_IO_PENDING = 997;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, int count, out int read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(SafeFileHandle h, IntPtr buffer, int count, out int written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr overlapped, out int count, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle h, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEventW(IntPtr security, bool manualReset, bool initialState, IntPtr name);
    [DllImport("kernel32.dll")]
    static extern uint WaitForSingleObject(IntPtr h, uint ms);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    // Opens the device; returns null and the Win32 error if it fails.
    public static SafeFileHandle Open(string path, out int error)
    {
        SafeFileHandle h = CreateFileW(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                                       OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        error = h.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        if (h.IsInvalid) { h.Dispose(); return null; }
        return h;
    }

    // One overlapped read or write with a timeout: the byte count, or -1 (error or timeout).
    static int Transfer(SafeFileHandle h, byte[] data, int offset, int count, bool write, int timeoutMs)
    {
        IntPtr buf = Marshal.AllocHGlobal(count);
        IntPtr ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeOverlapped)));
        IntPtr ev = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
        try
        {
            NativeOverlapped o = new NativeOverlapped();
            o.EventHandle = ev;
            Marshal.StructureToPtr(o, ov, false);
            if (write) Marshal.Copy(data, offset, buf, count);
            int n;
            bool ok = write ? WriteFile(h, buf, count, out n, ov) : ReadFile(h, buf, count, out n, ov);
            if (!ok)
            {
                if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return -1;
                if (WaitForSingleObject(ev, (uint)timeoutMs) != 0)
                {
                    CancelIoEx(h, ov);
                    GetOverlappedResult(h, ov, out n, true);
                    return -1;
                }
                if (!GetOverlappedResult(h, ov, out n, false)) return -1;
            }
            if (!write && n > 0) Marshal.Copy(buf, data, offset, Math.Min(n, count));
            return n;
        }
        finally
        {
            CloseHandle(ev);
            Marshal.FreeHGlobal(ov);
            Marshal.FreeHGlobal(buf);
        }
    }

    // One 32-byte message from the printer, or null if it has nothing to send.
    public static byte[] ReadMessage(SafeFileHandle h, int timeoutMs)
    {
        byte[] b = new byte[32];
        int n = Transfer(h, b, 0, 32, false, timeoutMs);
        if (n < 32) return null;
        return b;
    }

    public static bool Write(SafeFileHandle h, byte[] data, int offset, int count, int timeoutMs)
    {
        return Transfer(h, data, offset, count, true, timeoutMs) == count;
    }
}

// Minimal SNMPv1 GET (BER) for -Network: the value bytes and their BER type, or null with an error.
public static class QlSnmp
{
    static int requestId = Environment.TickCount & 0x7FFFFFFF;

    public static byte[] Get(string host, string community, string oid, int timeoutMs, int tries, out int type, out string error)
    {
        type = 0;
        error = "no answer";
        System.Net.IPAddress address;
        try
        {
            if (!System.Net.IPAddress.TryParse(host, out address))
            {
                address = null;
                foreach (var a in System.Net.Dns.GetHostAddresses(host))
                    if (address == null || a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) address = a;
                if (address == null) { error = "address not found"; return null; }
            }
        }
        catch (Exception ex) { error = ex.Message; return null; }
        using (var udp = new System.Net.Sockets.UdpClient(address.AddressFamily))
        {
            udp.Client.ReceiveTimeout = timeoutMs;
            var target = new System.Net.IPEndPoint(address, 161);
            for (int t = 0; t < tries; t++)
            {
                int id = Interlocked.Increment(ref requestId) & 0x7FFFFFFF;
                byte[] req = BuildGet(community, oid, id);
                try
                {
                    udp.Send(req, req.Length, target);
                    int deadline = Environment.TickCount + timeoutMs;
                    while (Environment.TickCount - deadline < 0)
                    {
                        var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                        byte[] reply = udp.Receive(ref from);
                        int replyId;
                        byte[] value = Parse(reply, out replyId, out type, out error);
                        if (replyId == id) return value;
                    }
                }
                catch (System.Net.Sockets.SocketException ex)
                {
                    if (ex.SocketErrorCode != System.Net.Sockets.SocketError.TimedOut &&
                        ex.SocketErrorCode != System.Net.Sockets.SocketError.ConnectionReset) { error = ex.Message; return null; }
                }
            }
        }
        error = "no answer";
        return null;
    }

    static byte[] BuildGet(string community, string oid, int id)
    {
        var bind = Tlv(0x30, Cat(Tlv(0x06, Oid(oid)), new byte[] { 0x05, 0x00 }));
        var pdu = Tlv(0xA0, Cat(Cat(Int(id), Int(0)), Cat(Int(0), Tlv(0x30, bind))));
        return Tlv(0x30, Cat(Cat(Int(0), Tlv(0x04, System.Text.Encoding.ASCII.GetBytes(community))), pdu));
    }

    static byte[] Parse(byte[] d, out int id, out int type, out string error)
    {
        id = -1; type = 0; error = "unreadable answer";
        try
        {
            int p = 0;
            byte[] msg = Read(d, ref p, 0x30); p = 0;
            Read(msg, ref p, 0x02); Read(msg, ref p, 0x04);
            byte[] pdu = Read(msg, ref p, 0xA2); p = 0;
            id = ToInt(Read(pdu, ref p, 0x02));
            int status = ToInt(Read(pdu, ref p, 0x02));
            Read(pdu, ref p, 0x02);
            if (status != 0) { error = status == 2 ? "noSuchName (the printer doesn't have this value)" : "SNMP error " + status; return null; }
            byte[] list = Read(pdu, ref p, 0x30); p = 0;
            byte[] bind = Read(list, ref p, 0x30); p = 0;
            Read(bind, ref p, 0x06);
            type = bind[p];
            byte[] value = Read(bind, ref p, (byte)type);
            if (type == 0x80 || type == 0x81 || type == 0x82) { error = "no such value (type 0x" + type.ToString("X2") + ")"; return null; }
            error = null;
            return value;
        }
        catch (Exception) { return null; }
    }

    static byte[] Read(byte[] d, ref int p, byte tag)
    {
        if (p >= d.Length || d[p] != tag) throw new FormatException();
        p++;
        int len = d[p++];
        if (len > 0x80) { int n = len & 0x7F; len = 0; for (int i = 0; i < n; i++) len = (len << 8) | d[p++]; }
        if (p + len > d.Length) throw new FormatException();
        byte[] c = new byte[len];
        Array.Copy(d, p, c, 0, len);
        p += len;
        return c;
    }

    static int ToInt(byte[] b) { int v = (b.Length > 0 && (b[0] & 0x80) != 0) ? -1 : 0; foreach (byte x in b) v = (v << 8) | x; return v; }

    static byte[] Int(int v)
    {
        var l = new System.Collections.Generic.List<byte>();
        do { l.Insert(0, (byte)(v & 0xFF)); v >>= 8; } while (v != 0 && v != -1);
        if ((l[0] & 0x80) != 0 && v == 0) l.Insert(0, 0);
        return Tlv(0x02, l.ToArray());
    }

    static byte[] Oid(string oid)
    {
        string[] s = oid.Split('.');
        var l = new System.Collections.Generic.List<byte>();
        l.Add((byte)(uint.Parse(s[0]) * 40 + uint.Parse(s[1])));
        for (int i = 2; i < s.Length; i++)
        {
            uint v = uint.Parse(s[i]);
            var c = new System.Collections.Generic.List<byte>();
            c.Insert(0, (byte)(v & 0x7F));
            while ((v >>= 7) != 0) c.Insert(0, (byte)(0x80 | (v & 0x7F)));
            l.AddRange(c);
        }
        return l.ToArray();
    }

    static byte[] Tlv(byte tag, byte[] c)
    {
        var l = new System.Collections.Generic.List<byte>();
        l.Add(tag);
        if (c.Length < 0x80) l.Add((byte)c.Length);
        else if (c.Length < 0x100) { l.Add(0x81); l.Add((byte)c.Length); }
        else { l.Add(0x82); l.Add((byte)(c.Length >> 8)); l.Add((byte)c.Length); }
        l.AddRange(c);
        return l.ToArray();
    }

    static byte[] Cat(byte[] a, byte[] b) { byte[] r = new byte[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
}
"@

# The expected picture of a test in the kit's own small window: no picture viewer app, which could stay in the
# background and keep the kit folder in use (Photos did, 2026-11-03). It runs on its own thread, doesn't take the
# keyboard focus, shows the next test's picture in place, and closes with the script. The file is read into memory.
try {
    Add-Type -ReferencedAssemblies System.Windows.Forms, System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

public class QlQuietForm : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }
}

public static class QlPicture
{
    static QlQuietForm form;
    static PictureBox box;
    static Label note;
    static bool placed;
    static readonly object gate = new object();

    // Shows the picture with a title and a line of text; false if it can't (no file, no desktop).
    public static bool Show(string path, string title, string text)
    {
        Image img;
        try
        {
            using (var ms = new MemoryStream(File.ReadAllBytes(path)))
            using (var i = Image.FromStream(ms)) img = new Bitmap(i);
        }
        catch (Exception) { return false; }
        lock (gate)
        {
            if (form == null) Start();
            var f = form;
            if (f == null) { img.Dispose(); return false; }
            try { f.BeginInvoke((MethodInvoker)delegate { Set(f, img, title, text); }); }
            catch (Exception) { img.Dispose(); return false; }
        }
        return true;
    }

    public static void Close()
    {
        lock (gate)
        {
            var f = form;
            form = null;
            if (f != null) try { f.BeginInvoke((MethodInvoker)delegate { f.Close(); }); } catch (Exception) { }
        }
    }

    static void Start()
    {
        var ready = new ManualResetEvent(false);
        var t = new Thread(() =>
        {
            QlQuietForm f = null;
            try
            {
                f = new QlQuietForm();
                f.Text = "Expected label";
                f.FormBorderStyle = FormBorderStyle.FixedSingle;
                f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.Manual;
                f.BackColor = Color.FromArgb(232, 232, 232);
                f.Padding = new Padding(12);
                f.Font = new Font("Segoe UI", 9.75f);
                box = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = f.BackColor };
                note = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(0, 8, 0, 0) };
                f.Controls.Add(box);
                f.Controls.Add(note);
                f.FormClosed += (s, e) => { lock (gate) { if (form == f) form = null; } Application.ExitThread(); };
                IntPtr h = f.Handle;   // the window exists from here on, so BeginInvoke works
                placed = false;
                form = f;
            }
            catch (Exception) { f = null; }
            finally { ready.Set(); }
            if (f != null) Application.Run();   // shown by the first Set, at its size and place
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        ready.WaitOne(10000);
    }

    static void Set(Form f, Image img, string title, string text)
    {
        var old = box.Image;
        box.Image = img;
        if (old != null) old.Dispose();
        f.Text = title;
        note.Text = text;
        // Fit into 460 x 400 (small pictures up to twice their size), keeping the aspect ratio.
        double s = Math.Min(2.0, Math.Min(460.0 / img.Width, 400.0 / img.Height));
        int width = Math.Max(320, (int)(img.Width * s));
        note.Height = TextRenderer.MeasureText(text, note.Font, new Size(width, 0), TextFormatFlags.WordBreak).Height + 12;
        var client = new Size(width + 24, (int)(img.Height * s) + 24 + note.Height);
        var area = Screen.PrimaryScreen.WorkingArea;
        int right = placed ? f.Right : area.Right - 24;
        f.ClientSize = client;
        f.Location = new Point(Math.Max(area.Left, right - f.Width), placed ? f.Top : area.Top + 24);
        placed = true;
        if (!f.Visible) f.Show();
    }
}
"@
    $pictureWindow = $true
} catch { $pictureWindow = $false }

$UsbPrintClass ="HKLM:\SYSTEM\CurrentControlSet\Control\DeviceClasses\{28d78fad-5a12-11d1-ae5b-0000f803a8c2}"
# Status reply bytes 3 (series) and 4 (model) -> model; QL-500 and QL-550 send the same codes.
$Models = [ordered]@{
    "30 4F" = "QL-500/550"; "34 31" = "QL-560"; "34 32" = "QL-570"; "34 33" = "QL-580N"; "34 47" = "QL-600";
    "30 51" = "QL-650TD"; "34 35" = "QL-700"; "34 36" = "QL-710W"; "34 37" = "QL-720NW"; "34 38" = "QL-800";
    "34 39" = "QL-810W"; "34 41" = "QL-820NWB"; "30 50" = "QL-1050"; "34 34" = "QL-1060N"; "34 43" = "QL-1100";
    "34 44" = "QL-1110NWB"; "34 45" = "QL-1115NWB"
}
$report = New-Object System.Collections.Generic.List[string]
# The report leaves out what identifies the tester or the printer (the page promises "no personal data"): the USB
# serial number in the device path, printer names the user chose, addresses, folders. -Screen: a fuller line for the
# screen only. The scrub is the safety net for texts from Windows (error messages with an address or a folder).
function Hide-Private([string]$text) {
    $text = $text -replace '(?<![\d.])(\d{1,3}\.){3}\d{1,3}(?![\d.])', '<IP address>'
    if ($env:USERPROFILE) { $text = $text -replace [regex]::Escape($env:USERPROFILE), '<user folder>' }
    # only as \\name: a computer may be called "WINDOWS", which the report says for other reasons
    if ($env:COMPUTERNAME) { $text = $text -replace ('\\\\' + [regex]::Escape($env:COMPUTERNAME) + '\b'), '\\<computer>' }
    return $text
}

# Screen and report are written separately: the screen has one plain line per step (approved
# 2026-11-03), the report every technical line. Log = report line, on the
# screen only with -Details (dimmed). Colours: only White, Green, Yellow, Red, Cyan and DarkGray - DarkYellow and
# DarkMagenta are the blue PowerShell window's own text and background colours.
function Log([string]$text) {
    $report.Add((Hide-Private $text))
    if ($Details) { Write-Host "         $text" -ForegroundColor DarkGray }
}
# A step's result: "  OK     text" with the word in colour, optionally a dimmed note after the text.
function Mark([string]$word, [string]$color, [string]$text, [string]$note = "") {
    Write-Host ("  {0,-7}" -f $word) -ForegroundColor $color -NoNewline
    if ($note) { Write-Host $text -NoNewline; Write-Host "  $note" -ForegroundColor DarkGray } else { Write-Host $text }
}
function Ok([string]$text, [string]$note = "") { Mark "OK" Green $text $note }
function Warn([string]$text) { Mark "WARN" Yellow $text }
function Fail([string]$text) { Mark "ERROR" Red $text }
function Hint([string]$text) { Write-Host "         $text" }
function Dim([string]$text) { Write-Host $text -ForegroundColor DarkGray }
# A section heading, with an optional text at the right end of a 72-column line.
function Head([string]$title, [string]$right = "", [string]$rightColor = "DarkGray") {
    Write-Host ""
    $h = "== $title =="
    Write-Host $h -ForegroundColor Cyan -NoNewline
    if ($right) { Write-Host ((" " * [Math]::Max(2, 72 - $h.Length - $right.Length)) + $right) -ForegroundColor $rightColor } else { Write-Host "" }
}
# A choice: pairs of key and meaning, e.g. "Enter", "print", "S", "skip". Returns "" for Enter, else the key in
# lower case. Ctrl+C stops the script as always.
function Read-Choice([string]$question, [string[]]$keys) {
    while ($true) {
        if ($question) { Write-Host "$question  " -NoNewline }
        for ($i = 0; $i -lt $keys.Count; $i += 2) {
            Write-Host $keys[$i] -ForegroundColor White -NoNewline
            Write-Host (" " + $keys[$i + 1] + $(if ($i + 2 -lt $keys.Count) { "   " } else { ": " })) -NoNewline
        }
        $a = ([string](Read-Host)).Trim().ToLower()
        for ($i = 0; $i -lt $keys.Count; $i += 2) {
            if ($keys[$i] -eq "Enter" -and $a -eq "") { return "" }
            if ($keys[$i] -ne "Enter" -and $a -eq $keys[$i].ToLower()) { return $a }
        }
    }
}
function Read-YesNo([string]$question, [string]$yes = "yes", [string]$no = "no") {
    if ($question) { Write-Host $question }
    return (Read-Choice "" @("Y", $yes, "N", $no)) -eq "y"
}
function Hex([byte[]]$b) { ($b | ForEach-Object { $_.ToString("X2") }) -join " " }

# Connected QL printers: usbprint interfaces of Brother devices (vendor ID 04F9) that can be opened.
function Get-QlDevices {
    $found = @()
    if (-not (Test-Path $UsbPrintClass)) { return $found }
    foreach ($k in Get-ChildItem $UsbPrintClass) {
        if ($k.PSChildName -notmatch "VID_04F9&PID_([0-9A-F]{4})") { continue }
        $pid4 = $Matches[1]
        $hash = Join-Path $k.PSPath "#"
        $link = (Get-ItemProperty $hash -ErrorAction SilentlyContinue).SymbolicLink
        if (-not $link) { $link = "\\?\" + $k.PSChildName.Substring(4) }
        $params = Get-ItemProperty (Join-Path $hash "Device Parameters") -ErrorAction SilentlyContinue
        $port = if ($params -and $params."Port Number" -ne $null) { "{0}{1:000}" -f $params."Base Name", [int]$params."Port Number" } else { "" }
        $err = 0
        $h = [QlUsb]::Open($link, [ref]$err)
        if (-not $h) { continue }
        $h.Dispose()
        # Windows printers on that USB port (names the user knows) and whether they are printing.
        $queues = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $port -and $_.PortName -eq $port })
        $busy = @($queues | Where-Object { @(Get-PrintJob -PrinterName $_.Name -ErrorAction SilentlyContinue).Count }).Count -gt 0
        $found += [pscustomobject]@{ Path = $link; Pid = $pid4; Port = $port; Queues = @($queues | ForEach-Object Name);
                                     Busy = $busy; Model = ""; Roll = "" }
    }
    return $found
}

# Model and loaded roll of a device, read quietly for the selection list (not while its queue is printing).
function Add-DeviceInfo($d) {
    if ($d.Busy) { $d.Roll = "busy printing"; return }
    $err = 0
    $h = [QlUsb]::Open($d.Path, [ref]$err)
    if (-not $h) { return }
    try {
        $s = Get-Status $h -Quiet -Tag "[$($d.Port) while listing] "
        if ($s) {
            $d.Model = $Models[("{0:X2} {1:X2}" -f $s[3], $s[4])]
            $d.Roll = "roll " + (Get-Roll $s)
        }
    } finally { $h.Dispose() }
}

# One line per device for the choice: model, port and roll from the printer itself, then the Windows printer
# name(s) dimmed (screen only).
function Show-Device([string]$prefix, $d) {
    $model = if ($d.Model) { $d.Model } else { "QL printer (model unknown)" }
    $port = if ($d.Port) { $d.Port } else { "USB" }
    $roll = if ($d.Roll) { ", $($d.Roll)" } else { "" }
    $names = if ($d.Queues.Count) { $d.Queues -join ", " } else { "no Windows printer on this port" }
    if ($d.NoTests) {
        Write-Host "$prefix $model on $port$roll  (no tests in this kit)" -ForegroundColor DarkGray
        return
    }
    Write-Host $prefix -ForegroundColor White -NoNewline
    Write-Host " $model on $port$roll" -NoNewline
    Write-Host "  $names" -ForegroundColor DarkGray
}

# The models this kit has test labels for (streams\<model>): a one-model kit tests only that model; a printer of
# another model is greyed in the list, and with none on USB a network model's kit offers its network test.
function Test-KitModel([string]$model) {
    if (-not $kitModels.Count -or -not $model) { return $true }
    if ($model -eq "QL-500/550") { return ($kitModels -contains "QL-500") -or ($kitModels -contains "QL-550") }
    return $kitModels -contains $model
}

# The DK roll numbers per paper (rolls.tsv next to this script: paper id, name, Europe, US, 1 = e.g.).
$RollNumbers = @{}
$rollsFile = Join-Path $kitDir "rolls.tsv"
if (Test-Path $rollsFile) {
    Get-Content $rollsFile | Where-Object { $_ -and -not $_.StartsWith("#") } | ForEach-Object { $f = $_ -split "`t"; $RollNumbers[$f[1]] = $f }
}

# "62 mm continuous (e.g. DK-22205, US: DK-2205)": the loaded roll as the printer reports it (width, type, label
# length), with its DK numbers where the size has one.
function Get-Roll([byte[]]$s) {
    $type = switch ($s[11]) { 0x0A { "continuous" } 0x0B { "die-cut" } 0 { "none" } default { "type 0x{0:X2}" -f $s[11] } }
    if ($s[11] -eq 0) { return "none" }
    # A round label reports as a square die-cut one (12 x 12 mm): named round where Brother has a round paper of
    # that size and no square one (23 x 23 mm stays die-cut).
    $round = $s[11] -eq 0x0B -and $s[10] -eq $s[17] -and $RollNumbers[("{0}mm Dia" -f $s[10])] -and -not $RollNumbers[("{0}mm x {0}mm" -f $s[10])]
    $roll = if ($round) { "{0} mm round" -f $s[10] }
            elseif ($s[11] -eq 0x0B) { "{0} x {1} mm {2}" -f $s[10], $s[17], $type } else { "{0} mm {1}" -f $s[10], $type }
    $keys = if ($s[25] -eq 0x81) { @("62mm black/red") }
            elseif ($s[11] -eq 0x0A) { @("{0}mm" -f $s[10]) }
            elseif ($s[11] -eq 0x0B) { @(("{0}mm x {1}mm" -f $s[10], $s[17])) + $(if ($s[10] -eq $s[17]) { @("{0}mm Dia" -f $s[10]) } else { @() }) }
            else { @() }
    if ($s[25] -eq 0x81) { $roll += " black/red" }
    $dk = $keys | ForEach-Object { Get-RollNumbers $_ } | Where-Object { $_ } | Select-Object -First 1
    if ($dk) { $roll += " $dk" }
    return $roll
}

# "(e.g. DK-22205, US: DK-2205)" for a paper name of rolls.tsv ("62mm"); "" when it has none.
function Get-RollNumbers([string]$paper) {
    $r = $RollNumbers[$paper]
    if (-not $r) { return "" }
    return "(" + $(if ($r[4] -eq "1") { "e.g. " } else { "" }) + $r[2] + $(if ($r[3]) { ", US: " + $r[3] } else { "" }) + ")"
}

# The printer's errors in plain words for the screen ("" = none), and what the tester can do about them.
function Get-Problem([byte[]]$s) {
    $p = @()
    if ($s[9] -band 0x10) { $p += "the roll cover is open" }
    if ($s[8] -band 0x01) { $p += "no roll is loaded" }
    if ($s[8] -band 0x02) { $p += "the roll is used up" }
    if ($s[8] -band 0x04) { $p += "the cutter is jammed" }
    if ($s[9] -band 0x01) { $p += "it needs a different roll" }
    if ($s[9] -band 0x40) { $p += "the label didn't feed" }
    if (-not $p -and (($s[8] -band 0xF8) -or ($s[9] -band 0xAE))) { $p += "an error (details in the report)" }
    return $p -join ", "
}
function Show-ProblemHelp([byte[]]$s) {
    if ($s[9] -band 0x10) { Hint "Close the roll cover." }
    if ($s[8] -band 0x03) { Hint "Load a roll (or a new one) and close the cover." }
    if ($s[8] -band 0x04) { Hint "Switch the printer off, clear the cutter, switch it on again." }
    if ($s[9] -band 0x01) { Hint "Load the roll this test needs, or skip the test." }
    Hint "If the light still blinks red, open and close the roll cover, or switch the"
    Hint "printer off and on again."
}

function Describe([byte[]]$s) {
    $model = $Models[("{0:X2} {1:X2}" -f $s[3], $s[4])]
    if (-not $model) { $model = "unknown model code {0:X2} {1:X2}" -f $s[3], $s[4] }
    $roll = Get-Roll $s
    $kind = switch ($s[18]) { 0 { "status reply" } 1 { "printing completed" } 2 { "ERROR" } 4 { "turned off" } 5 { "notification" } 6 { "phase change" } default { "type $($s[18])" } }
    $errors = @()
    if ($s[8] -band 0x01) { $errors += "no media" }
    if ($s[8] -band 0x02) { $errors += "end of media" }
    if ($s[8] -band 0x04) { $errors += "cutter jam" }
    if ($s[8] -band 0xF8) { $errors += ("error1 0x{0:X2}" -f $s[8]) }
    if ($s[9] -band 0x01) { $errors += "replace media (wrong roll)" }
    if ($s[9] -band 0x10) { $errors += "cover open" }
    if ($s[9] -band 0x40) { $errors += "feeding error" }
    if ($s[9] -band 0xAE) { $errors += ("error2 0x{0:X2}" -f $s[9]) }
    $e = if ($errors) { "; errors: " + ($errors -join ", ") } else { "" }
    return "$kind, $model, roll $roll, phase $($s[19])$e"
}

# Drains the printer's own messages, then asks for the status (ESC i S; if that gets no answer, the
# invalidate + ESC @ + ESC i S that Brother's language monitor sends).
# -Quiet (selection list): nothing on screen, the lines only go into the report, marked with -Tag.
function Get-Status($h, [switch]$Quiet, [string]$Tag = "") {
    function Note([string]$text) { if ($Quiet) { $report.Add("$Tag$text") } else { Log $text } }
    for ($i = 0; $i -lt 16; $i++) {
        $m = [QlUsb]::ReadMessage($h, 300)
        if (-not $m) { break }
        Note ("  message: {0}   ({1})" -f (Hex $m), (Describe $m))
    }
    $requests = @(, [byte[]](0x1B, 0x69, 0x53))
    $requests += , ([byte[]](@(0) * 200 + @(0x1B, 0x40, 0x1B, 0x69, 0x53)))
    foreach ($req in $requests) {
        if (-not [QlUsb]::Write($h, $req, 0, $req.Length, 2000)) { Note "  status request could not be written (printer busy?)"; return $null }
        $t = [Environment]::TickCount
        while ([Environment]::TickCount - $t -lt 2000) {
            $m = [QlUsb]::ReadMessage($h, 500)
            if (-not $m) { continue }
            Note ("  status:  {0}   ({1})" -f (Hex $m), (Describe $m))
            if ($m[18] -eq 0) { return $m }
        }
    }
    Note "  no status reply"
    return $null
}

# The page media of a stream: (width byte, kind, length byte) of its first ESC i z.
function Get-StreamMedia([byte[]]$b) {
    for ($i = 0; $i -lt [Math]::Min($b.Length - 13, 4096); $i++) {
        if ($b[$i] -eq 0x1B -and $b[$i + 1] -eq 0x69 -and $b[$i + 2] -eq 0x7A) {
            return [pscustomobject]@{ Width = $b[$i + 5]; Kind = $b[$i + 4]; Length = $b[$i + 6] }
        }
    }
    return $null
}

# Tape a stream uses: walks the commands (raster lines skipped by their length) and adds up the pages.
# Continuous: (lines + 2 x feed) dots at 300 dpi (600 in the 300 x 600 mode); die-cut: one label per page.
# Returns @{ Mm = tape in mm (continuous); Labels = die-cut labels; Pages = pages }.
function Get-TapeUse([byte[]]$b) {
    $r = @{ Mm = 0.0; Labels = 0; Pages = 0 }
    $kind = 0; $lines = 0; $feed = 0; $dpi = 300.0; $lengthMm = 0
    $i = 0
    while ($i -lt $b.Length) {
        $c = $b[$i]
        if ($c -eq 0x00) { $i++; continue }
        if ($c -eq 0x1B) {
            if ($b[$i + 1] -eq 0x40) { $i += 2; continue }
            if ($b[$i + 1] -ne 0x69) { $i += 2; continue }
            switch ($b[$i + 2]) {
                0x7A { $kind = $b[$i + 4]; $lengthMm = $b[$i + 6]; $lines = [BitConverter]::ToInt32($b, $i + 7); $i += 13 }
                0x64 { $feed = $b[$i + 3] + 256 * $b[$i + 4]; $i += 5 }
                0x4B { $dpi = if ($b[$i + 3] -band 0x40) { 600.0 } else { 300.0 }; $i += 4 }
                0x55 { $i += $(if ($b[$i + 3] -eq 0x4A) { 18 } else { 5 + 127 }) }
                0x53 { $i += 3 }
                default { $i += 4 }   # ESC i a / ! / M / A / J n
            }
            continue
        }
        switch ($c) {
            0x4D { $i += 2 }                                       # M n
            { $_ -eq 0x67 -or $_ -eq 0x77 } { $i += 3 + $b[$i + 2] }   # g 00 n / w p n + data
            0x47 { $i += 3 + $b[$i + 1] + 256 * $b[$i + 2] }       # G n0 n1 + data
            0x5A { $i++ }                                          # Z
            { $_ -eq 0x0C -or $_ -eq 0x1A } {                      # page end
                $r.Pages++
                if ($kind -eq 0x0B) { $r.Labels++ } else { $r.Mm += ($lines + 2 * $feed) * 25.4 / $dpi }
                $i++
            }
            default { $i++ }
        }
    }
    return $r
}

function Format-TapeUse($u) {
    $s = { param($n) if ($n -eq 1) { "" } else { "s" } }
    $continuous = $u.Pages - $u.Labels
    $parts = @()
    if ($continuous -gt 0) { $parts += "{0} label{1}, about {2} cm of tape" -f $continuous, (& $s $continuous), [Math]::Ceiling($u.Mm / 10) }
    if ($u.Labels -gt 0) { $parts += "{0} die-cut label{1}" -f $u.Labels, (& $s $u.Labels) }
    return $parts -join " and "
}
# The same for the screen's right column: "9 cm", "1 label", "2 labels, 9 cm".
function Format-TapeShort($u) {
    $parts = @()
    if ($u.Labels -gt 0) { $parts += "{0} label{1}" -f $u.Labels, $(if ($u.Labels -eq 1) { "" } else { "s" }) }
    if ($u.Pages - $u.Labels -gt 0) { $parts += "{0} cm" -f [Math]::Ceiling($u.Mm / 10) }
    return $parts -join ", "
}

function Test-RollFits($media, [byte[]]$status, [bool]$twoColor) {
    if (-not $media -or -not $status) { return $true }
    # 103 mm tape: Brother's width byte is 104; what the printer reports for it is not known (103 or 104).
    # Die-cut length: Brother rounds (102 x 152 mm is sent as 153), the printer may report 152.
    $widthOk = $media.Width -eq $status[10] -or ($media.Width -eq 104 -and $status[10] -eq 103)
    $kindOk = $media.Kind -eq $status[11]
    $lengthOk = $media.Kind -ne 0x0B -or [Math]::Abs([int]$media.Length - [int]$status[17]) -le 1
    $colourOk = -not $twoColor -or $status[25] -eq 0x81
    return ($widthOk -and $kindOk -and $lengthOk -and $colourOk)
}

# Sends a stream, then follows the printer until it is idle again or reports an error. Returns the last status
# (or $null) and, if the sending itself went wrong, what happened in plain words.
function Send-Stream($h, [string]$file) {
    $b = [IO.File]::ReadAllBytes($file)
    Log ("  sending {0} ({1} bytes)" -f (Split-Path $file -Leaf), $b.Length)
    $trouble = ""
    $chunk = 16384
    for ($o = 0; $o -lt $b.Length; $o += $chunk) {
        $n = [Math]::Min($chunk, $b.Length - $o)
        if (-not [QlUsb]::Write($h, $b, $o, $n, 60000)) {
            Log "  write stopped at byte $o (printer not accepting data)"; $trouble = "The printer stopped taking the label's data."; break
        }
    }
    $t = [Environment]::TickCount
    $last = $null
    while ([Environment]::TickCount - $t -lt 90000) {
        $m = [QlUsb]::ReadMessage($h, 1000)
        if ($m) {
            Log ("  message: {0}   ({1})" -f (Hex $m), (Describe $m))
            if ($m[18] -eq 2) { return [pscustomobject]@{ Status = $m; Trouble = $trouble } }
            continue
        }
        # nothing pending: ask; idle (phase 0, no error) after a few seconds = done
        $req = [byte[]](0x1B, 0x69, 0x53)
        if ([QlUsb]::Write($h, $req, 0, 3, 2000)) {
            $r = [QlUsb]::ReadMessage($h, 1500)
            if ($r) {
                $last = $r
                if (($r[8] -ne 0 -or $r[9] -ne 0) -or ($r[19] -eq 0 -and [Environment]::TickCount - $t -gt 3000)) {
                    Log ("  status:  {0}   ({1})" -f (Hex $r), (Describe $r))
                    return [pscustomobject]@{ Status = $r; Trouble = $trouble }
                }
            }
        }
        Start-Sleep -Milliseconds 500
    }
    Log "  printer did not become idle within 90 s"
    if (-not $trouble) { $trouble = "The printer didn't finish within 90 seconds." }
    return [pscustomobject]@{ Status = $last; Trouble = $trouble }
}

# Shows a printed label's outcome: "Printed", or the printer's error with what to do, waiting until it's fixed.
# Returns $true if the printer reported an error.
function Show-PrintResult($r, [scriptblock]$recheck) {
    $s = $r.Status
    $problem = if ($s) { Get-Problem $s } else { "" }
    if ($problem -or ($s -and $s[18] -eq 2)) {
        if (-not $problem) { $problem = "an error (details in the report)" }
        Fail "The printer stopped: $problem."
        Show-ProblemHelp $s
        Write-Host "         Press Enter when the printer is ready: " -NoNewline
        $null = Read-Host
        $again = & $recheck
        if ($again -and -not (Get-Problem $again)) { Ok "Ready again" }
        elseif ($again) { Warn "The printer still reports: $(Get-Problem $again)." }
        else { Warn "The printer doesn't answer; the next test may fail." }
        return $true
    }
    if ($r.Trouble) { Warn $r.Trouble; return $false }
    Ok "Printed"
    return $false
}

# QL Unidrv's optional status service (QLStatus) reads every QL printer on USB every 3 s, past the lock below: its
# reads and status requests would land inside the test streams (2026-11-03). Asked by this control (any signed-in
# user may send it), it stops reading USB printers for 10 minutes, renewed by asking again; once no read of its own
# runs any more it writes the pause's end into a file of its log folder (docs/status-service.md "The test kit").
$statusService = Get-Service -Name QLStatus -ErrorAction SilentlyContinue
$pauseFile = Join-Path $env:ProgramData "QLUnidrv\log\status-pause.txt"
$servicePaused = $false
function Suspend-StatusService {
    if (-not $statusService) { return $true }
    $statusService.Refresh()
    if ($statusService.Status -ne "Running") { return $true }
    $asked = [DateTime]::UtcNow
    try { $statusService.ExecuteCommand(128) } catch { return $false }
    $t = [Environment]::TickCount
    while ([Environment]::TickCount - $t -lt 20000) {
        try {
            $until = [DateTime]::Parse((Get-Content $pauseFile -Raw -ErrorAction Stop).Trim(), $null, [Globalization.DateTimeStyles]::RoundtripKind)
            if ($until -gt $asked.AddMinutes(5)) { $script:servicePaused = $true; return $true }  # this request's pause, not an older one's
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    return $false
}
function Resume-StatusService {
    if (-not $script:servicePaused) { return }
    $script:servicePaused = $false
    try {
        $statusService.ExecuteCommand(129)
        $script:serviceNote = "QL Unidrv's status service is running again."; Log "QL Unidrv's status service: resumed."
    } catch {
        $script:serviceNote = "QL Unidrv's status service resumes by itself within 10 minutes."
        Log "QL Unidrv's status service: resumes by itself within 10 minutes."
    }
}
$serviceNote = ""

# The tests' outcomes for the end: looked right, different, skipped, printer errors; and whether the tester stopped.
$results = @{ Ok = 0; Diff = 0; Skipped = 0; Errors = 0; Stopped = "" }

# The tester's verdict on a printed label: one green line, or one follow-up question and a yellow line.
# The report line keeps its old form ("  RESULT: OK" / "  RESULT: PROBLEM - <note>").
function Read-Verdict([string]$tag = "") {
    if (Read-YesNo "Does the label look like the picture (text, border, bars, cut)?" "yes" "no, something is different") {
        Ok "Looks right"
        $results.Ok++
        Log "  RESULT${tag}: OK"
        return
    }
    Write-Host "What is different? A few words: " -NoNewline
    $note = ([string](Read-Host)).Trim()
    Mark "DIFF" Yellow $(if ($note) { $note } else { "different (no words given)" })
    Dim "         A photo of the labels helps; add it when you send the report."
    $results.Diff++
    Log ("  RESULT{0}: PROBLEM{1}" -f $tag, $(if ($note) { " - $note" } else { "" }))
}

function Write-Report([string]$model, [string]$kind = "") {
    $name = (@(($model -replace "[^A-Za-z0-9-]", ""), $kind.TrimStart("-")) | Where-Object { $_ }) -join "-"
    $file = Join-Path $reportDir ("qltest-report-{0}-{1:yyyyMMdd-HHmm}.txt" -f $name, (Get-Date))
    $report | Set-Content -Path $file -Encoding UTF8
    Head "Done"
    if ($results.Ok + $results.Diff + $results.Skipped + $results.Errors) {
        Write-Host "   " -NoNewline
        Write-Host "$($results.Ok) look$(if ($results.Ok -eq 1) { 's' }) right" -ForegroundColor Green -NoNewline
        Write-Host "   " -NoNewline
        Write-Host "$($results.Diff) different" -ForegroundColor Yellow -NoNewline
        Write-Host "   $($results.Skipped) skipped" -ForegroundColor DarkGray -NoNewline
        if ($results.Errors) { Write-Host "   $($results.Errors) printer error$(if ($results.Errors -ne 1) { 's' })" -ForegroundColor Red } else { Write-Host "" }
        if ($results.Stopped) { Dim "   $($results.Stopped)" }
    }
    Write-Host "  " -NoNewline; Write-Host ("{0,-7}" -f "OK") -ForegroundColor Green -NoNewline
    Write-Host "Report saved: " -NoNewline; Write-Host (Split-Path $file -Leaf) -ForegroundColor White
    # Bright, not dimmed: the tester needs the folder to attach the file (easy to miss in grey).
    Write-Host "         in " -NoNewline; Write-Host (Split-Path $file) -ForegroundColor White
    if ($serviceNote) { Dim "         $serviceNote" }
    Head "Please send the report"
    Write-Host "   GitHub: " -NoNewline; Write-Host "github.com/joffs/ql-unidrv/issues" -ForegroundColor White -NoNewline; Write-Host " > New issue >"
    Write-Host "           `"Printer test report`", and attach the file"
    Write-Host "   E-mail: " -NoNewline; Write-Host "ql-unidrv@joffs.de" -ForegroundColor White
    Write-Host "   Add photos of labels that looked different. Thank you!"
    Write-Host "   Want to be named on the thanks page? Say so in the form or in your mail."
    Write-Host ""
}

# ---------- network (-Network): status over SNMP, optional test label over the network ----------
# Brother's own software reads a network QL printer's status with one SNMPv1 GET of this OID, whose value is
# the same 32-byte status as the USB reply. Asking is harmless: nothing is
# sent to the print port. Also read: a few standard values, to see what the printer offers besides.
$BrotherStatusOid = "1.3.6.1.4.1.2435.3.3.9.1.6.1.0"
$StandardOids = [ordered]@{
    "sysDescr" = "1.3.6.1.2.1.1.1.0"; "sysObjectID" = "1.3.6.1.2.1.1.2.0"; "hrDeviceStatus" = "1.3.6.1.2.1.25.3.2.1.5.1";
    "hrPrinterStatus" = "1.3.6.1.2.1.25.3.5.1.1.1"; "hrPrinterDetectedErrorState" = "1.3.6.1.2.1.25.3.5.1.2.1"
}

function Get-SnmpValue([string]$address, [string]$oid) {
    $type = 0; $err = ""
    $v = [QlSnmp]::Get($address, $Community, $oid, 2000, 2, [ref]$type, [ref]$err)
    return [pscustomobject]@{ Value = $v; Type = $type; Error = $err }
}

function Format-SnmpValue($r) {
    if ($r.Value -eq $null) { return "no value ($($r.Error))" }
    switch ($r.Type) {
        0x04 {
            $printable = ($r.Value | Where-Object { $_ -lt 0x20 -or $_ -gt 0x7E }).Count -eq 0
            if ($printable -and $r.Value.Length) { return '"' + [Text.Encoding]::ASCII.GetString($r.Value) + '"' }
            return "octets[$($r.Value.Length)] $(Hex $r.Value)"
        }
        0x02 { $n = 0; foreach ($b in $r.Value) { $n = $n * 256 + $b }; return "integer $n" }
        0x06 { return "oid (BER) $(Hex $r.Value)" }
        default { return "type 0x{0:X2}: {1}" -f $r.Type, (Hex $r.Value) }
    }
}

# Brother's status OID once; the 32 bytes or $null. Written to the report with -Tag.
function Get-NetStatus([string]$address, [string]$tag = "") {
    $r = Get-SnmpValue $address $BrotherStatusOid
    if ($r.Value -ne $null -and $r.Type -eq 0x04 -and $r.Value.Length -eq 32) {
        Log ("  {0}status:  {1}   ({2})" -f $tag, (Hex $r.Value), (Describe $r.Value))
        return [byte[]]$r.Value
    }
    Log ("  {0}Brother status OID: {1}" -f $tag, (Format-SnmpValue $r))
    return $null
}

# A test's expected picture in the kit's own window (falls back to naming the file).
function Show-Expected([string]$png, [string]$title, [string]$text) {
    if (-not (Test-Path $png)) { return }
    if ($pictureWindow -and [QlPicture]::Show($png, $title, $text)) { Dim "The expected picture is in the `"Expected label`" window." }
    else { Dim "Expected picture: $png" }
}

# A test's description for the screen: without the technical part in brackets, starting with a capital.
function Get-Title([string]$text) {
    $t = ($text -replace "\s*\([^)]*\)\s*$", "").Trim()
    if ($t) { $t = $t.Substring(0, 1).ToUpper() + $t.Substring(1) }
    return $t
}

# The roll in plain words while watching: the problem, printing, or ready.
function Get-State([byte[]]$s) {
    $p = Get-Problem $s
    if ($p) { return $p.Substring(0, 1).ToUpper() + $p.Substring(1) }
    if ($s[19] -ne 0) { return "Printing" }
    return "Ready"
}

function Invoke-NetworkTest {
    # A Windows printer name: its Standard TCP/IP port's address (as Brother's software does), else an address.
    $address = $Network
    $printer = Get-Printer -Name $Network -ErrorAction SilentlyContinue
    if ($printer) {
        $key = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\$($printer.PortName)"
        $p = Get-ItemProperty $key -ErrorAction SilentlyContinue
        $address = if ($p -and $p.IPAddress) { $p.IPAddress } elseif ($p -and $p.HostName) { $p.HostName } else { $null }
        Log "Windows printer given by name (driver $($printer.DriverName))"
        if (-not $address) {
            if (-not $printerHead) { Head "Printer" }
            Fail "The Windows printer '$Network' isn't on a network (TCP/IP) port."
            Hint "Start the kit with the printer's IP address instead: -Network 192.0.2.50 (your printer's address)"
            exit 1
        }
    }
    $given = if ([Net.IPAddress]::TryParse($address, [ref]$null)) { "an IP address" } else { "a host name" }
    $comm = if ($Community -eq "public") { "'public'" } else { "not 'public'" }
    Log "Network printer: given as $given, SNMP community $comm"
    Log "Standard values:"
    foreach ($k in $StandardOids.Keys) { Log ("  {0} ({1}): {2}" -f $k, $StandardOids[$k], (Format-SnmpValue (Get-SnmpValue $address $StandardOids[$k]))) }
    Log "Brother's status ($BrotherStatusOid):"
    $status = Get-NetStatus $address
    if (-not $printerHead) { Head "Printer" }
    if (-not $status) {
        Log "No QL status over SNMP."
        Fail "No answer from $address."
        Hint "Check the printer is on and in the same network, SNMP is on in"
        Hint "its web page, and the firewall lets UDP port 161 through."
        Write-Report "" "-network"
        return
    }
    $model = $Models[("{0:X2} {1:X2}" -f $status[3], $status[4])]
    if (-not $model) { Write-Host "Which model is it (e.g. QL-810W)? " -NoNewline; $model = ([string](Read-Host)).Trim() }
    Log "Model: $model"
    Ok "Brother $model answers at $address"
    $problem = Get-Problem $status
    if ($problem) { Fail "The printer reports: $problem." } else { Ok "Ready. Roll: $(Get-Roll $status)" }

    if ($Watch -gt 0) {
        Head "Watching the printer" "$Watch s"
        Write-Host "Now open and close the roll cover, take the roll out and put it back,"
        Write-Host "or print something. Every change is noted."
        Log "Watch ($Watch s, every 2 s; only changes):"
        $last = Hex $status
        $lastState = Get-State $status
        $changes = 0
        $end = (Get-Date).AddSeconds($Watch)
        while ((Get-Date) -lt $end) {
            Start-Sleep -Seconds 2
            $r = Get-SnmpValue $address $BrotherStatusOid
            $valid = $r.Value -ne $null -and $r.Value.Length -eq 32
            $now = if ($valid) { Hex $r.Value } else { "(" + (Format-SnmpValue $r) + ")" }
            if ($now -eq $last) { continue }
            $text = if ($valid) { "$now   ($(Describe $r.Value))" } else { $now }
            Log ("  {0:HH:mm:ss} {1}" -f (Get-Date), $text)
            $last = $now
            # The screen: a line when the state in plain words changes (the report has every byte change).
            $state = if ($valid) { Get-State $r.Value } else { "No answer" }
            if ($valid -and $state -eq "Ready" -and (Get-Roll $r.Value) -ne (Get-Roll $status)) { $state = "Ready. Roll: $(Get-Roll $r.Value)"; $status = [byte[]]$r.Value }
            if ($state -ne $lastState) {
                Write-Host ("  {0:HH:mm:ss}  " -f (Get-Date)) -ForegroundColor DarkGray -NoNewline
                if ($state -like "Ready*" -or $state -eq "Printing") { Write-Host $state } else { Write-Host $state -ForegroundColor Yellow }
                $lastState = $state
                $changes++
            }
        }
        Ok "$changes change$(if ($changes -ne 1) { 's' }) noted"
    }
    # Optional: the kit's network-form label (as our driver sends it to a network port) over TCP 9100.
    $dir = Join-Path $streamsRoot $model
    $file = Join-Path $dir "62mm-continuous_default-length_compressed.prn"
    if ((Test-Path $file) -and -not $StatusOnly) {
        $b = [IO.File]::ReadAllBytes($file)
        $use = Get-TapeUse $b
        Head "Network test label" (Format-TapeShort $use) White
        Write-Host "One 62 mm label, sent over the network (port 9100)" -ForegroundColor White
        $media = Get-StreamMedia $b
        if (-not (Test-RollFits $media $status $false)) {
            Log "Network test label: needs the 62 mm continuous roll $(Get-RollNumbers "62mm") (loaded: $(Get-Roll $status)); skipped."
            Mark "-" DarkGray "Skipped: it needs the 62 mm continuous roll $(Get-RollNumbers "62mm") (loaded: $(Get-Roll $status))."
            $results.Skipped++
        } else {
            Show-Expected (Join-Path $dir "62mm-continuous_default-length.png") "Expected label - network test" "62 mm continuous: one 62 mm label at the default length, cut after it. This window closes when the test kit ends."
            if ((Read-Choice "Print it?" @("Enter", "print", "S", "skip")) -eq "s") {
                Log "Network test label: skipped"; Mark "-" DarkGray "Skipped"; $results.Skipped++
            } else {
                Log "  sending $(Split-Path $file -Leaf) ($($b.Length) bytes) to the printer, port 9100"
                Dim "         Printing ..."
                $sent = $true
                try {
                    $tcp = New-Object Net.Sockets.TcpClient
                    $tcp.Connect($address, 9100)
                    $s = $tcp.GetStream()
                    $s.Write($b, 0, $b.Length)
                    $s.Flush()
                    $tcp.Close()
                } catch {
                    $sent = $false
                    Log ("  sending failed: " + ($_.Exception.Message -replace [regex]::Escape($address), "<printer>"))
                    Fail "The label couldn't be sent: the printer didn't accept the connection (port 9100)."
                    $results.Errors++
                }
                if ($sent) {
                    # Follow it over SNMP: printing, then idle or an error.
                    $s2 = $null
                    $t = Get-Date
                    while (((Get-Date) - $t).TotalSeconds -lt 60) {
                        Start-Sleep -Seconds 2
                        $s2 = Get-NetStatus $address "after sending: "
                        if ($s2 -and ($s2[8] -ne 0 -or $s2[9] -ne 0 -or ($s2[19] -eq 0 -and ((Get-Date) - $t).TotalSeconds -gt 6))) { break }
                    }
                    $failed = Show-PrintResult ([pscustomobject]@{ Status = $s2; Trouble = $(if (-not $s2) { "The printer didn't answer after printing." } else { "" }) }) { Get-NetStatus $address "after the error: " }
                    if ($failed) { $results.Errors++ }
                    Read-Verdict " (network)"
                }
            }
        }
    }
    Write-Report $model "-network"
}

# ---------------------------------------------------------------------------------------------------
# Test labels: the kit's streams folder; run from the source folder (tools\testkit) the built kit in
# out\testkit\kit, and the report goes to out\testkit\reports instead of the source folder.
$streamsRoot = Join-Path $kitDir "streams"
$reportDir = $kitDir
$repoRoot = Split-Path (Split-Path $kitDir)
if (-not (Test-Path $streamsRoot) -and (Test-Path (Join-Path $repoRoot "driver\build.cmd"))) {
    $streamsRoot = Join-Path $repoRoot "out\testkit\kit\streams"
    $reportDir = Join-Path $repoRoot "out\testkit\reports"
    New-Item -ItemType Directory -Force $reportDir | Out-Null
    if (-not (Test-Path $streamsRoot)) { Write-Host "No test labels built yet: run tools\testkit\make-testkit.ps1 first." -ForegroundColor Yellow }
} elseif ((Split-Path $kitDir -Leaf) -eq "kit" -and (Test-Path (Join-Path (Split-Path $kitDir) "HOW-TO.txt"))) {
    # the built kit in out\testkit\kit: keep reports out of it (make-testkit.ps1 rebuilds that folder)
    $reportDir = Join-Path (Split-Path $kitDir) "reports"
    New-Item -ItemType Directory -Force $reportDir | Out-Null
}

# Which kit made the report: the build date make-testkit.ps1 writes next to the streams, and the script's own hash
# (the same date can have several builds, and a tester may have edited the script).
$versionFile = Join-Path (Split-Path $streamsRoot) "kit-version.txt"
$kitBuild = if (Test-Path $versionFile) { (Get-Content $versionFile -TotalCount 1).Trim() } else { "unknown (not a built kit)" }
$scriptHash = (Get-FileHash -Algorithm SHA256 $PSCommandPath).Hash.Substring(0, 8).ToLower()
Log "QL test kit, build $kitBuild (qltest.ps1 $scriptHash)"
Log "Run $(Get-Date -Format 'yyyy-MM-dd HH:mm'), Windows $([Environment]::OSVersion.Version) $((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE)"
Write-Host ""
$title = if ($Network) { "QL printer test kit - network printer" } else { "QL printer test kit" }
Write-Host $title -ForegroundColor White -NoNewline
Write-Host ((" " * [Math]::Max(2, 72 - $title.Length - "build $kitBuild".Length)) + "build $kitBuild") -ForegroundColor DarkGray
if ($List) { }
elseif ($Network -and $StatusOnly) { Write-Host "Reads the printer's status over the network and writes a report." }
elseif ($Network) { Write-Host "Reads the printer's status over the network and prints one test label." }
elseif ($StatusOnly) { Write-Host "Reads the printer's status and writes a report. Nothing on this PC changes." }
else { Write-Host "Prints a few test labels and writes a report. Nothing on this PC changes." }

# Everything below: the picture window closes with the script, also after an error or Ctrl+C.
try {
if ($Network) { Invoke-NetworkTest; exit 0 }

$devices = @(Get-QlDevices | Where-Object {
        -not $Device -or $_.Path -like "*$Device*" -or $_.Port -eq $Device -or @($_.Queues | Where-Object { $_ -like "*$Device*" }).Count })
Head "Printer"
$printerHead = $true
$kitModels = @(if (Test-Path $streamsRoot) { Get-ChildItem $streamsRoot -Directory | ForEach-Object Name })
# "the QL-720NW", "the QL-710W or QL-720NW"; "your printer" for the kit with all models.
$kitName = if ($kitModels.Count -ge 1 -and $kitModels.Count -le 3) { "the " + ($kitModels -join " or ") } else { "your printer" }
$kitNetwork = @($kitModels | Where-Object { $_ -match '^QL-\d+\D*[NW]' })
if ($devices.Count -gt 1 -or ($devices -and $kitModels.Count -and -not $Device)) {
    if ($devices.Count -gt 1) { Dim "Asking the connected QL printers for model and roll ..." }
    foreach ($d in $devices) { Add-DeviceInfo $d }
} elseif ($List) { foreach ($d in $devices) { Add-DeviceInfo $d } }
foreach ($d in $devices) { $d | Add-Member NoTests (-not $Device -and -not (Test-KitModel $d.Model)) }
$usable = @($devices | Where-Object { -not $_.NoTests })
if ($List -and $devices) {
    foreach ($d in $devices) { Show-Device " " $d }
    exit 0
}
if (-not $usable) {
    if ($devices) {
        $onUsb = ($devices | ForEach-Object { "$(if ($_.Model) { $_.Model } else { 'QL printer' }) ($($_.Port))" }) -join ", "
        Log "No printer on USB this kit has tests for; on USB: $onUsb"
        Warn "This kit is for $kitName. On USB: $onUsb."
    } else {
        Log "No Brother QL printer found on USB."
        Fail "No Brother QL printer found on USB."
    }
    if ($kitNetwork -and -not $List) {
        Hint "Is $kitName on the network? Enter its IP address"
        Write-Host "         (or press Enter to quit): " -NoNewline
        $ip = ([string](Read-Host)).Trim()
        if ($ip) {
            Log "Network test with the address entered"
            $Network = $ip
            Invoke-NetworkTest
            exit 0
        }
    }
    if ($devices) { Hint "Connect $kitName by USB and start the kit again." }
    else {
        Hint "Connect it and switch it on. QL-700 and QL-800 series: the"
        Hint "Editor Lite light must be off (a printer that shows up as a USB drive"
        Hint "is in its Editor Lite mode)."
    }
    exit 1
}
if ($usable.Count -gt 1) {
    Write-Host "Several QL printers are connected:"
    $i = 0
    foreach ($d in $devices) { if ($d.NoTests) { Show-Device "    -" $d } else { $i++; Show-Device ("  [{0}]" -f $i) $d } }
    do { Write-Host "Which printer (1-$($usable.Count)): " -NoNewline; $n = Read-Host } while (-not ($n -match '^\d+$' -and [int]$n -ge 1 -and [int]$n -le $usable.Count))
    $dev = $usable[[int]$n - 1]
} else {
    $dev = $usable[0]
    if ($devices.Count -gt 1) { Dim "Other QL printers on USB have no tests in this kit." }
}
# No device path: it carries the printer's USB serial number.
Log "Device: USB PID $($dev.Pid), port $($dev.Port)"

$queues = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $dev.Port -and $_.PortName -eq $dev.Port })
$queueNote = if ($queues) { "Windows printer: " + (@($queues | ForEach-Object Name) -join ", ") } else { "" }
foreach ($q in $queues) {
    Log "Windows printer on this port: driver $($q.DriverName)"
    if (@(Get-PrintJob -PrinterName $q.Name -ErrorAction SilentlyContinue).Count) {
        Ok "Brother QL printer on USB ($($dev.Port))" $queueNote
        Fail "This printer has print jobs waiting in Windows."
        Hint "Let them finish or cancel them, then start the test again."
        exit 1
    }
}

$err = 0
$h = [QlUsb]::Open($dev.Path, [ref]$err)
if (-not $h) {
    Log "Cannot open the printer (error $err)."
    Fail "The printer can't be opened (Windows error $err)."
    Hint "Is another program printing on it? Close it, or switch the printer off and on."
    exit 1
}
# With the QL Unidrv label tool installed: its printer monitor and status window ask the printer every few
# seconds; three bytes of theirs inside a test stream spoil the label (2026-10-19). They wait for this lock.
$gate = $null
if ($dev.Port) {
    $gate = New-Object Threading.Mutex($false, "Local\QLUnidrv.Usb.$($dev.Port)")
    try { [void]$gate.WaitOne(10000) } catch [Threading.AbandonedMutexException] { }
}
try {
    $pausedOk = Suspend-StatusService
    if ($pausedOk -and $servicePaused) { Log "QL Unidrv's status service: paused for the test (it resumes at the end, or by itself after 10 minutes)." }
    Log "Status:"
    $status = Get-Status $h
    $model = if ($status) { $Models[("{0:X2} {1:X2}" -f $status[3], $status[4])] } else { $null }
    if ($model -eq "QL-500/550") {
        Write-Host "Is this a QL-500 (no automatic cutter) or a QL-550?"
        $model = if ((Read-Choice "" @("500", "QL-500", "550", "QL-550")) -eq "500") { "QL-500" } else { "QL-550" }
    }
    if (-not $status) {
        Ok "Brother QL printer on USB ($($dev.Port))" $queueNote
        Warn "The printer doesn't answer the status request."
        Hint "Switch it off and on and start the kit again - or go on: the kit"
        Hint "then can't check the roll."
    }
    if (-not $model) { Write-Host "Which model is it (e.g. QL-710W)? " -NoNewline; $model = ([string](Read-Host)).Trim() }
    Log "Model: $model"
    if ($status) {
        Ok "Brother $model on USB ($($dev.Port))" $queueNote
        $problem = Get-Problem $status
        if ($problem) {
            Fail "The printer reports: $problem."
            Show-ProblemHelp $status
            Write-Host "         Press Enter when the printer is ready: " -NoNewline
            $null = Read-Host
            $again = Get-Status $h
            if ($again) { $status = $again }
            if ($again -and -not (Get-Problem $again)) { Ok "Ready again. Roll: $(Get-Roll $status)" }
            else { Warn "The printer still reports a problem; the labels may not print." }
        } else { Ok "Ready. Roll: $(Get-Roll $status)" }
    }
    if (-not $pausedOk) {
        Log "QL Unidrv's status service (QLStatus) did not pause."
        Warn "QL Unidrv's status service could not be paused."
        Hint "Its status requests can spoil a test label. Update QL Unidrv,"
        Hint "or stop it (administrator PowerShell: Stop-Service QLStatus)."
        if (-not (Read-YesNo "Continue anyway?" "yes" "stop")) { exit 1 }
    } elseif ($servicePaused) { Dim "         QL Unidrv's status service is paused while the test runs." }

    if ($Print) {
        Head "Sending $(Split-Path $Print -Leaf)"
        $media = Get-StreamMedia ([IO.File]::ReadAllBytes($Print))
        if (-not $Force -and -not (Test-RollFits $media $status $false)) {
            Log "The loaded roll doesn't match this stream."
            Fail "The loaded roll doesn't match this stream (-Force sends it anyway)."
            exit 1
        }
        $null = Suspend-StatusService
        Dim "         Printing ..."
        if (Show-PrintResult (Send-Stream $h $Print) { Get-Status $h }) { $results.Errors++ }
    } elseif (-not $StatusOnly) {
        $dir = Join-Path $streamsRoot $model
        $manifest = Join-Path $dir "cases.tsv"
        Head "Tests for this roll" "tape"
        if (-not (Test-Path $manifest)) {
            Log "This kit has no test labels for the $model."
            Warn "This kit has no test labels for the $model."
            Dim "         (looked in $dir)"
        } else {
            $cases = @(Get-Content $manifest -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith("#") } | ForEach-Object {
                $c = $_ -split "`t"; [pscustomobject]@{ File = $c[0]; Roll = $c[1]; TwoColor = $c[2] -eq "1"; Text = $c[3] } })
            $fit = @($cases | Where-Object { Test-RollFits (Get-StreamMedia ([IO.File]::ReadAllBytes((Join-Path $dir $_.File)))) $status $_.TwoColor })
            $otherRolls = @($cases | Where-Object { $fit -notcontains $_ } | ForEach-Object Roll) | Select-Object -Unique
            $others = $otherRolls -join ", "
            Log ("Test labels for the loaded roll: {0} of {1}{2}" -f $fit.Count, $cases.Count, $(if ($others) { " (the others need: $others)" } else { "" }))
            # What the tests will use, so the tester can check the roll has enough left.
            $total = @{ Mm = 0.0; Labels = 0; Pages = 0 }
            $n = 0
            foreach ($c in $fit) {
                $c | Add-Member -NotePropertyName Use -NotePropertyValue (Get-TapeUse ([IO.File]::ReadAllBytes((Join-Path $dir $c.File)))) -Force
                $total.Mm += $c.Use.Mm; $total.Labels += $c.Use.Labels; $total.Pages += $c.Use.Pages
                $n++
                Log ("  {0}. {1} - {2}" -f $n, $c.File, (Format-TapeUse $c.Use))
                $t = Get-Title $c.Text
                if ($t.Length -gt 56) { $t = $t.Substring(0, 53) + "..." }
                Write-Host ("   {0}  {1,-56} {2,9}" -f $n, $t, (Format-TapeShort $c.Use))
            }
            if ($fit.Count) {
                Log ("All {0} tests together: {1}. Make sure the roll has enough left; you can skip any test." -f $fit.Count, (Format-TapeUse $total))
                $labels = $total.Pages
                $cm = if ($total.Pages - $total.Labels -gt 0) { ", about {0} cm" -f [Math]::Ceiling($total.Mm / 10) } else { "" }
                Dim ("      Together {0} label{1}{2}. Check the roll has enough left." -f $labels, $(if ($labels -ne 1) { "s" }), $cm)
            } else {
                Warn "None of the kit's tests fits the loaded roll."
            }
            if ($otherRolls) {
                $more = $cases.Count - $fit.Count
                Dim ("      {0} more test{1} need{2} another roll:" -f $more, $(if ($more -ne 1) { "s" }), $(if ($more -eq 1) { "s" }))
                # One test per Brother roll of the model: the sizes only (the DK numbers are in the report), a few lines.
                $line = ""
                foreach ($o in @($otherRolls | ForEach-Object { $_ -replace " \(.*\)$", "" })) {
                    if ($line -and $line.Length + $o.Length -gt 62) { Dim "        $line,"; $line = "" }
                    $line = if ($line) { "$line, $o" } else { $o }
                }
                if ($line) { Dim "        $line" }
            }
            if ($fit.Count) {
                Write-Host ""
                Write-Host "Each test shows its expected picture in a small window. Compare the"
                Write-Host "label with it, then answer. You can skip any test."
            }
            $n = 0
            foreach ($c in $fit) {
                $n++
                Head "Test $n of $($fit.Count)" (Format-TapeShort $c.Use) White
                Write-Host (Get-Title $c.Text) -ForegroundColor White
                Log "Test $n of $($fit.Count): $($c.File) - $($c.Text) [$(Format-TapeUse $c.Use)]"
                $png = Join-Path $dir ([IO.Path]::ChangeExtension($c.File, ".png") -replace "_usb\.png$|_compressed\.png$", ".png")
                Show-Expected $png "Expected label - test $n of $($fit.Count)" "$($c.Roll): $(Get-Title $c.Text). This window closes when the test kit ends."
                $a = Read-Choice "Print it?" @("Enter", "print", "S", "skip", "Q", "stop testing")
                if ($a -eq "q") {
                    Log "  stopped by the tester"
                    $results.Stopped = "Stopped before test $n of $($fit.Count)."
                    break
                }
                if ($a -eq "s") { Log "  skipped"; Mark "-" DarkGray "Skipped"; $results.Skipped++; continue }
                if ($servicePaused) { $null = Suspend-StatusService }   # renews the 10 minutes (the tester may have taken longer)
                Dim "         Printing ..."
                $r = Send-Stream $h (Join-Path $dir $c.File)
                $recheck = { if ($servicePaused) { $null = Suspend-StatusService }; Get-Status $h }
                if (Show-PrintResult $r $recheck) { $results.Errors++ }
                Read-Verdict
            }
        }
    }
} finally {
    $h.Dispose()
    if ($gate) { try { $gate.ReleaseMutex() } catch { }; $gate.Dispose() }
    Resume-StatusService   # also after Ctrl+C; a closed window leaves it to the service's 10 minutes
}

Write-Report $model
} finally {
    if ($pictureWindow) { [QlPicture]::Close() }
}
