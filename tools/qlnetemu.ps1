<#
.SYNOPSIS
  Development tool: emulates a Brother QL network printer on this PC, backed by a real QL printer on USB.

.DESCRIPTION
  Answers what Brother's software (and our label tool) ask a network QL printer:
  - SNMP (UDP 161): GET of Brother's status OID 1.3.6.1.4.1.2435.3.3.9.1.6.1.0 returns the backing printer's live
    32-byte status, taken from the status the service "QL printer status" publishes for its queue (so nothing but
    the service reads the USB printer). -Model changes the model code in it (e.g. QL-810W). Also answers sysDescr
    and sysObjectID; everything else gets noSuchName. SNMPv1 and v2c, community -Community.
  - Print port (TCP 9100): every connection's data is printed as a RAW job on the backing printer's Windows queue,
    so the spooler sends it over USB and the service holds back its status requests meanwhile. -NoPrint: data is
    only counted and saved to out\qlnetemu\.
  Runs until Ctrl+C. Listening on 127.0.0.1 needs no firewall rule; another PC needs -Listen <this PC's address>
  and inbound rules for UDP 161 and TCP 9100 (administrator).

  -Setup (administrator): adds the Standard TCP/IP port "QLEMU_<address>" (raw 9100, Windows' own SNMP polling
  off) and the printer "<model> Network Emulator" with that model's driver; -Remove deletes both.

  Not a proof for real printers: whether a printer's firmware answers the OID and with which bytes is only shown
  by a real one (test kit: qltest.ps1 -Network).

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\qlnetemu.ps1 -Setup -Model QL-810W      (administrator, once)
  powershell -ExecutionPolicy Bypass -File tools\qlnetemu.ps1 -Model QL-810W             (runs until Ctrl+C)
#>
param(
    [string]$Listen = "127.0.0.1",
    [string]$Printer = "Brother QL-800 (Unidrv)",   # the real USB printer behind the emulator, or a share (\\host\queue)
    [ValidateSet("", "QL-580N", "QL-710W", "QL-720NW", "QL-800", "QL-810W", "QL-820NWB", "QL-1060N", "QL-1110NWB", "QL-1115NWB")]
    [string]$Model = "",                              # model code to report (default: the backing printer's)
    [string]$Community = "public",
    [switch]$NoPrint,
    [switch]$Setup,
    [switch]$Remove
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
# Status reply bytes 3 and 4 (series, model) per model (docs/models.md).
$codes = @{ "QL-580N" = 0x34, 0x33; "QL-710W" = 0x34, 0x36; "QL-720NW" = 0x34, 0x37; "QL-800" = 0x34, 0x38; "QL-810W" = 0x34, 0x39;
            "QL-820NWB" = 0x34, 0x41; "QL-1060N" = 0x34, 0x34; "QL-1110NWB" = 0x34, 0x44; "QL-1115NWB" = 0x34, 0x45 }
$portName = "QLEMU_$Listen"
$emuModel = if ($Model) { $Model } else { "QL-800" }
$emuPrinter = "$emuModel Network Emulator"

if ($Setup -or $Remove) {
    $admin = (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $admin) { throw "-Setup / -Remove need an administrator PowerShell." }
    if ($Remove) {
        Get-Printer | Where-Object PortName -eq $portName | ForEach-Object { Remove-Printer -Name $_.Name; "Removed printer '$($_.Name)'." }
        if (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue) { Remove-PrinterPort -Name $portName; "Removed port $portName." }
        return
    }
    if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
        Add-PrinterPort -Name $portName -PrinterHostAddress $Listen -PortNumber 9100
        "Added port $portName (raw, $Listen`:9100)."
    }
    # Windows' own SNMP status polling off: only the label tool asks the emulator.
    Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\$portName" -Name "SNMP Enabled" -Value 0
    $driver = "Brother $emuModel (Unidrv)"
    if (-not (Get-PrinterDriver -Name $driver -ErrorAction SilentlyContinue)) { throw "Driver '$driver' is not installed." }
    if (-not (Get-Printer -Name $emuPrinter -ErrorAction SilentlyContinue)) {
        Add-Printer -Name $emuPrinter -DriverName $driver -PortName $portName
        "Added printer '$emuPrinter' ($driver on $portName)."
    }
    Restart-Service Spooler   # the port monitor reads "SNMP Enabled" at start
    "Ready. Start the emulator (not elevated): powershell -ExecutionPolicy Bypass -File $PSCommandPath$(if ($Model) { " -Model $Model" })"
    return
}

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class QlEmu
{
    // ---------- printer data (the service's published status) and RAW jobs ----------
    [StructLayout(LayoutKind.Sequential)] struct PDEF { public IntPtr DataType, DevMode; public int Access; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DOCINFO { public string Name, Output, DataType; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref PDEF d);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, IntPtr d);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode)] static extern int GetPrinterDataExW(IntPtr h, string k, string v, out int t, byte[] d, int s, out int n);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern int StartDocPrinterW(IntPtr h, int level, ref DOCINFO di);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool StartPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool WritePrinter(IntPtr h, byte[] b, int n, out int written);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndDocPrinter(IntPtr h);

    public static string ReadPublished(string printer)
    {
        IntPtr h; var d = new PDEF { Access = 8 };
        if (!OpenPrinterW(printer, out h, ref d)) return null;
        try
        {
            foreach (var key in new[] { "QLUnidrv" })
            {
                int t, n;
                int e = GetPrinterDataExW(h, key, "Status", out t, null, 0, out n);
                if ((e != 234 && e != 0) || n <= 0) continue;
                var b = new byte[n];
                if (GetPrinterDataExW(h, key, "Status", out t, b, n, out n) != 0) continue;
                return Encoding.Unicode.GetString(b, 0, n).TrimEnd('\0');
            }
            return null;
        }
        finally { ClosePrinter(h); }
    }

    // Prints data as a RAW job; the job id, or throws.
    public static int PrintRaw(string printer, string name, byte[] data)
    {
        IntPtr h;
        if (!OpenPrinterW(printer, out h, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
        try
        {
            var di = new DOCINFO { Name = name, DataType = "RAW" };
            int job = StartDocPrinterW(h, 1, ref di);
            if (job == 0) throw new System.ComponentModel.Win32Exception();
            StartPagePrinter(h);
            int written;
            bool ok = WritePrinter(h, data, data.Length, out written);
            EndPagePrinter(h);
            EndDocPrinter(h);
            if (!ok || written != data.Length) throw new System.ComponentModel.Win32Exception();
            return job;
        }
        finally { ClosePrinter(h); }
    }

    // ---------- SNMP agent (v1/v2c GET) ----------
    public const string StatusOid = "1.3.6.1.4.1.2435.3.3.9.1.6.1.0";

    // The answer to a request, or null (not a GET, wrong community: no answer, like a real agent). values: OID ->
    // (BER type, bytes). log: one line about the request.
    public static byte[] Answer(byte[] req, string community, Dictionary<string, KeyValuePair<byte, byte[]>> values, out string log)
    {
        log = "unreadable request (" + req.Length + " bytes)";
        try
        {
            int p = 0;
            byte[] msg = Read(req, ref p, 0x30); p = 0;
            int version = ToInt(Read(msg, ref p, 0x02));
            string comm = Encoding.ASCII.GetString(Read(msg, ref p, 0x04));
            if (p >= msg.Length) return null;
            byte pduType = msg[p];
            byte[] pdu = Read(msg, ref p, pduType); p = 0;
            byte[] id = Read(pdu, ref p, 0x02);
            Read(pdu, ref p, 0x02); Read(pdu, ref p, 0x02);
            byte[] list = Read(pdu, ref p, 0x30); p = 0;
            byte[] bind = Read(list, ref p, 0x30); p = 0;
            string oid = OidText(Read(bind, ref p, 0x06));
            log = string.Format("v{0} '{1}' {2} {3}", version == 0 ? "1" : "2c", comm, pduType == 0xA0 ? "GET" : "PDU 0x" + pduType.ToString("X2"), oid);
            if (comm != community) { log += " -> ignored (community)"; return null; }
            if (pduType != 0xA0) { log += " -> ignored (only GET)"; return null; }
            KeyValuePair<byte, byte[]> v;
            int status = 0;
            byte[] valueTlv;
            if (values.TryGetValue(oid, out v)) { valueTlv = Tlv(v.Key, v.Value); log += " -> " + v.Value.Length + " bytes"; }
            else if (version == 0) { status = 2; valueTlv = new byte[] { 0x05, 0x00 }; log += " -> noSuchName"; }
            else { valueTlv = new byte[] { 0x80, 0x00 }; log += " -> noSuchObject"; }
            var outBind = Tlv(0x30, Cat(Tlv(0x06, Oid(oid)), valueTlv));
            var outPdu = Tlv(0xA2, Cat(Cat(Tlv(0x02, id), Int(status)), Cat(Int(status == 0 ? 0 : 1), Tlv(0x30, outBind))));
            return Tlv(0x30, Cat(Cat(Int(version), Tlv(0x04, Encoding.ASCII.GetBytes(comm))), outPdu));
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
        var c = new byte[len]; Array.Copy(d, p, c, 0, len); p += len; return c;
    }
    static int ToInt(byte[] b) { int v = (b.Length > 0 && (b[0] & 0x80) != 0) ? -1 : 0; foreach (byte x in b) v = (v << 8) | x; return v; }
    static byte[] Int(int v)
    {
        var l = new List<byte>();
        do { l.Insert(0, (byte)(v & 0xFF)); v >>= 8; } while (v != 0 && v != -1);
        if ((l[0] & 0x80) != 0 && v == 0) l.Insert(0, 0);
        return Tlv(0x02, l.ToArray());
    }
    public static byte[] Oid(string oid)
    {
        string[] s = oid.Split('.');
        var l = new List<byte> { (byte)(uint.Parse(s[0]) * 40 + uint.Parse(s[1])) };
        for (int i = 2; i < s.Length; i++)
        {
            uint v = uint.Parse(s[i]); var c = new List<byte> { (byte)(v & 0x7F) };
            while ((v >>= 7) != 0) c.Insert(0, (byte)(0x80 | (v & 0x7F)));
            l.AddRange(c);
        }
        return l.ToArray();
    }
    static string OidText(byte[] b)
    {
        var parts = new List<string> { (b[0] / 40).ToString(), (b[0] % 40).ToString() };
        uint v = 0;
        for (int i = 1; i < b.Length; i++) { v = (v << 7) | (uint)(b[i] & 0x7F); if ((b[i] & 0x80) == 0) { parts.Add(v.ToString()); v = 0; } }
        return string.Join(".", parts);
    }
    static byte[] Tlv(byte tag, byte[] c)
    {
        var l = new List<byte> { tag };
        if (c.Length < 0x80) l.Add((byte)c.Length);
        else if (c.Length < 0x100) { l.Add(0x81); l.Add((byte)c.Length); }
        else { l.Add(0x82); l.Add((byte)(c.Length >> 8)); l.Add((byte)c.Length); }
        l.AddRange(c); return l.ToArray();
    }
    static byte[] Cat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
}
"@

function Log([string]$text) { Write-Host ("{0:HH:mm:ss} {1}" -f (Get-Date), $text) }

# The backing printer's newest 32 bytes from the published status (PublishedStatus.cs format), model patched.
function Get-Status {
    $text = [QlEmu]::ReadPublished($Printer)
    if (-not $text) { return $null }
    $lines = $text -split "`r?`n" | Where-Object { $_ }
    if ($lines.Count -lt 2 -or $lines[0] -ne "QLUnidrv status 1") { return $null }
    $poll = $lines[1] -split "`t"
    if ($poll[2] -ne "Open") { return $null }           # printer off / not connected / service stopped: no answer
    $last = $lines | Select-Object -Skip 2 | Select-Object -Last 1
    if (-not $last) { return $null }
    $bytes = [byte[]](($last -split "`t")[1] -split " " | ForEach-Object { [Convert]::ToByte($_, 16) })
    if ($bytes.Length -ne 32) { return $null }
    if ($Model) { $bytes[3] = $codes[$Model][0]; $bytes[4] = $codes[$Model][1] }
    return , $bytes
}

# A share (\\host\queue): the printer and the service that publishes its status are on the other PC, which
# needs the service; jobs go through the share.
if ($Printer -match '^\\\\[^\\]+\\[^\\]+$') {
    if (-not [QlEmu]::ReadPublished($Printer)) {
        Log "No published status from $Printer yet: the PC sharing it needs the service 'QL printer status' (install.ps1 -StatusService on). SNMP stays unanswered until it publishes."
    }
} else {
    if (-not (Get-Printer -Name $Printer -ErrorAction SilentlyContinue)) { throw "Printer '$Printer' not found." }
    if ((Get-Service QLStatus -ErrorAction SilentlyContinue).Status -ne "Running") {
        throw "The service 'QL printer status' doesn't run here: it supplies the backing printer's status (install.ps1 -AppOnly -StatusService on)."
    }
}
$saveDir = Join-Path $root "out\qlnetemu"
New-Item -ItemType Directory -Force $saveDir | Out-Null

$udp = New-Object Net.Sockets.UdpClient (New-Object Net.IPEndPoint ([Net.IPAddress]::Parse($Listen)), 161)
$tcp = New-Object Net.Sockets.TcpListener ([Net.IPAddress]::Parse($Listen)), 9100
$tcp.Start()
if (-not [Net.IPAddress]::IsLoopback([Net.IPAddress]::Parse($Listen))) {
    Log "Listening on $Listen`: every device that reaches this address can print on '$Printer' and read its status (no password, like a real network printer)."
}
Log "QL network printer emulator on $Listen (SNMP UDP 161, print TCP 9100) as $emuModel, backed by '$Printer'$(if ($NoPrint) { ', not printing' })."
Log "Windows printer for it: '$emuPrinter' on port $portName (tools\qlnetemu.ps1 -Setup). Ctrl+C stops."
$udpTask = $udp.ReceiveAsync()
$tcpTask = $tcp.AcceptTcpClientAsync()
$jobs = 0
try {
    while ($true) {
        [void][Threading.Tasks.Task]::WaitAny(@($udpTask, $tcpTask), 500)
        if ($udpTask.IsCompleted) {
            $r = $udpTask.Result
            $status = Get-Status
            $values = New-Object 'System.Collections.Generic.Dictionary[string, System.Collections.Generic.KeyValuePair[byte, byte[]]]'
            $values["1.3.6.1.2.1.1.1.0"] = New-Object 'System.Collections.Generic.KeyValuePair[byte, byte[]]' 0x04, ([Text.Encoding]::ASCII.GetBytes("Brother $emuModel (emulated by qlnetemu, backed by $Printer)"))
            $values["1.3.6.1.2.1.1.2.0"] = New-Object 'System.Collections.Generic.KeyValuePair[byte, byte[]]' 0x06, ([QlEmu]::Oid("1.3.6.1.4.1.2435.2.3.9.1"))
            if ($status) { $values[[QlEmu]::StatusOid] = New-Object 'System.Collections.Generic.KeyValuePair[byte, byte[]]' 0x04, $status }
            $line = ""
            $answer = [QlEmu]::Answer($r.Buffer, $Community, $values, [ref]$line)
            if ($answer -and ($status -or $line -notmatch [regex]::Escape([QlEmu]::StatusOid))) {
                [void]$udp.Send($answer, $answer.Length, $r.RemoteEndPoint)
                $hex = if ($status -and $line -match [regex]::Escape([QlEmu]::StatusOid)) { "  " + (($status | ForEach-Object { $_.ToString("X2") }) -join " ") } else { "" }
                Log "SNMP from $($r.RemoteEndPoint): $line$hex"
            } else {
                Log "SNMP from $($r.RemoteEndPoint): $line$(if (-not $status) { ' (no current status: printer off or not connected - no answer, like a printer that is off)' })"
            }
            $udpTask = $udp.ReceiveAsync()
        }
        if ($tcpTask.IsCompleted) {
            $client = $tcpTask.Result
            $tcpTask = $tcp.AcceptTcpClientAsync()
            $from = $client.Client.RemoteEndPoint
            $ms = New-Object IO.MemoryStream
            $s = $client.GetStream()
            # Like a printer: no sign-in. Limits keep one sender from filling the disk or holding the loop: 16 MB
            # and 2 minutes per job (a real label job is well under 1 MB).
            $s.ReadTimeout = 30000
            $buf = New-Object byte[] 65536
            $deadline = (Get-Date).AddMinutes(2)
            try {
                while (($n = $s.Read($buf, 0, $buf.Length)) -gt 0) {
                    $ms.Write($buf, 0, $n)
                    if ($ms.Length -gt 16MB -or (Get-Date) -gt $deadline) { Log "print connection from ${from}: over 16 MB or 2 minutes, cut off"; break }
                }
            } catch { Log "print connection from ${from}: read stopped ($($_.Exception.Message))" }
            $client.Close()
            $data = $ms.ToArray()
            $jobs++
            $file = Join-Path $saveDir ("job-{0:yyyyMMdd-HHmmss}-{1}.prn" -f (Get-Date), $jobs)
            [IO.File]::WriteAllBytes($file, $data)
            if ($NoPrint) { Log "print job from ${from}: $($data.Length) bytes, saved to $file (not printed)" }
            else {
                try {
                    $id = [QlEmu]::PrintRaw($Printer, "qlnetemu job $jobs ($emuModel)", $data)
                    Log "print job from ${from}: $($data.Length) bytes -> RAW job $id on '$Printer' (copy: $file)"
                } catch { Log "print job from ${from}: $($data.Length) bytes, printing failed: $($_.Exception.Message) (copy: $file)" }
            }
        }
    }
} finally {
    $udp.Close()
    $tcp.Stop()
    Log "Stopped."
}
