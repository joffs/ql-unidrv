<#
  snmp-walk.ps1 - reads what a network printer offers over SNMP (SNMPv1 GETNEXT walk), read only.
  Changes nothing on the printer or the PC; the printer's print port is not touched.

    powershell -ExecutionPolicy Bypass -File tools\snmp-walk.ps1 -Address 192.0.2.50
        [-Root 1.3.6.1.2.1.43] [-Root ...] [-Community public] [-Max 5000] [-Out <file>]

  Default roots: the Printer MIB (1.3.6.1.2.1.43: counters, supplies, input trays) and Brother's own subtree
  (1.3.6.1.4.1.2435). Writes one line per value (OID, type, value) to out\snmp\<address>-<time>.txt.
  The output can hold the printer's serial number and MAC address: it stays in out\ (not in git).
#>
param(
    [Parameter(Mandatory = $true)][string]$Address,
    [string[]]$Root = @("1.3.6.1.2.1.43", "1.3.6.1.4.1.2435"),
    [string]$Community = "public",
    [int]$Max = 5000,
    [string]$Out = ""
)
$ErrorActionPreference = "Stop"
$Root = @($Root | ForEach-Object { $_ -split "," } | Where-Object { $_ })  # -File passes "a,b" as one string

Add-Type -TypeDefinition @"
using System;
using System.Collections.Generic;
using System.Text;

public static class SnmpWalk
{
    static int requestId = Environment.TickCount & 0x7FFFFFFF;

    // One GETNEXT: the next OID after oid, its BER type and value bytes; null at the end of the MIB or on no answer.
    public static string Next(string host, string community, string oid, out int type, out byte[] value, out string error)
    {
        type = 0; value = null; error = "no answer";
        using (var udp = new System.Net.Sockets.UdpClient())
        {
            udp.Client.ReceiveTimeout = 2000;
            var target = new System.Net.IPEndPoint(System.Net.IPAddress.Parse(host), 161);
            for (int t = 0; t < 3; t++)
            {
                int id = ++requestId & 0x7FFFFFFF;
                var bind = Tlv(0x30, Cat(Tlv(0x06, Oid(oid)), new byte[] { 0x05, 0x00 }));
                var pdu = Tlv(0xA1, Cat(Cat(Int(id), Int(0)), Cat(Int(0), Tlv(0x30, bind))));
                var req = Tlv(0x30, Cat(Cat(Int(0), Tlv(0x04, Encoding.ASCII.GetBytes(community))), pdu));
                try
                {
                    udp.Send(req, req.Length, target);
                    var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                    byte[] d = udp.Receive(ref from);
                    int p = 0;
                    byte[] msg = Read(d, ref p, 0x30); p = 0;
                    Read(msg, ref p, 0x02); Read(msg, ref p, 0x04);
                    byte[] rp = Read(msg, ref p, 0xA2); p = 0;
                    int rid = ToInt(Read(rp, ref p, 0x02));
                    int status = ToInt(Read(rp, ref p, 0x02));
                    Read(rp, ref p, 0x02);
                    if (rid != id) continue;
                    if (status != 0) { error = "end (error status " + status + ")"; return null; }
                    byte[] list = Read(rp, ref p, 0x30); p = 0;
                    byte[] vb = Read(list, ref p, 0x30); p = 0;
                    string next = OidText(Read(vb, ref p, 0x06));
                    type = vb[p];
                    value = Read(vb, ref p, -1);
                    if (type == 0x82) { error = "end of MIB"; return null; }
                    return next;
                }
                catch (System.Net.Sockets.SocketException) { }
            }
        }
        return null;
    }

    static byte[] Read(byte[] d, ref int p, int tag)
    {
        if (tag >= 0 && d[p] != tag) throw new Exception("unexpected tag");
        p++;
        int len = d[p++];
        if ((len & 0x80) != 0) { int n = len & 0x7F; len = 0; for (int i = 0; i < n; i++) len = (len << 8) | d[p++]; }
        var r = new byte[len];
        Array.Copy(d, p, r, 0, len);
        p += len;
        return r;
    }
    static int ToInt(byte[] b) { int v = (b.Length > 0 && (b[0] & 0x80) != 0) ? -1 : 0; foreach (var x in b) v = (v << 8) | x; return v; }
    static byte[] Cat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; a.CopyTo(r, 0); b.CopyTo(r, a.Length); return r; }
    static byte[] Int(int v)
    {
        var l = new List<byte>(BitConverter.GetBytes(v)); l.Reverse();
        while (l.Count > 1 && ((l[0] == 0 && (l[1] & 0x80) == 0) || (l[0] == 0xFF && (l[1] & 0x80) != 0))) l.RemoveAt(0);
        return Tlv(0x02, l.ToArray());
    }
    static byte[] Tlv(int tag, byte[] v)
    {
        var l = new List<byte> { (byte)tag };
        if (v.Length < 0x80) l.Add((byte)v.Length);
        else if (v.Length < 0x100) { l.Add(0x81); l.Add((byte)v.Length); }
        else { l.Add(0x82); l.Add((byte)(v.Length >> 8)); l.Add((byte)v.Length); }
        l.AddRange(v);
        return l.ToArray();
    }
    static byte[] Oid(string oid)
    {
        var parts = oid.Split('.');
        var l = new List<byte> { (byte)(int.Parse(parts[0]) * 40 + int.Parse(parts[1])) };
        for (int i = 2; i < parts.Length; i++)
        {
            long v = long.Parse(parts[i]);
            var tmp = new List<byte> { (byte)(v & 0x7F) };
            while ((v >>= 7) > 0) tmp.Insert(0, (byte)(0x80 | (v & 0x7F)));
            l.AddRange(tmp);
        }
        return l.ToArray();
    }
    static string OidText(byte[] b)
    {
        var sb = new StringBuilder();
        sb.Append(b[0] / 40).Append('.').Append(b[0] % 40);
        long v = 0;
        for (int i = 1; i < b.Length; i++) { v = (v << 7) | (uint)(b[i] & 0x7F); if ((b[i] & 0x80) == 0) { sb.Append('.').Append(v); v = 0; } }
        return sb.ToString();
    }
}
"@

function Format-Value([int]$type, [byte[]]$v) {
    switch ($type) {
        0x02 { $n = [long]0; if ($v.Length -and ($v[0] -band 0x80)) { $n = -1 }; foreach ($x in $v) { $n = ($n -shl 8) -bor $x }; return "integer $n" }
        { $_ -in 0x41, 0x42, 0x43, 0x46 } { $n = [uint64]0; foreach ($x in $v) { $n = ($n -shl 8) -bor $x }; return ("{0} {1}" -f @{0x41 = "counter"; 0x42 = "gauge"; 0x43 = "timeticks"; 0x46 = "counter64" }[$type], $n) }
        0x04 {
            $printable = @($v | Where-Object { $_ -lt 0x20 -and $_ -notin 9, 10, 13 -or $_ -gt 0x7E }).Count -eq 0
            if ($printable) { return 'string "' + [Text.Encoding]::ASCII.GetString($v) + '"' }
            return "octets[$($v.Length)] " + (($v | ForEach-Object { $_.ToString("X2") }) -join " ")
        }
        0x05 { return "null" }
        0x06 { return "oid" }
        0x40 { return "ip " + ($v -join ".") }
        default { return ("type 0x{0:X2} " -f $type) + (($v | ForEach-Object { $_.ToString("X2") }) -join " ") }
    }
}

if (-not $Out) {
    $dir = Join-Path (Split-Path $PSScriptRoot) "out\snmp"
    New-Item -ItemType Directory -Force $dir | Out-Null
    $Out = Join-Path $dir ("{0}-{1:yyyyMMdd-HHmm}.txt" -f ($Address -replace '[^\w.-]', '_'), (Get-Date))
}
$lines = New-Object System.Collections.Generic.List[string]
foreach ($r in $Root) {
    $oid = $r
    $count = 0
    while ($count -lt $Max) {
        $type = 0; $value = $null; $err = ""
        $next = [SnmpWalk]::Next($Address, $Community, $oid, [ref]$type, [ref]$value, [ref]$err)
        if (-not $next -or -not $next.StartsWith("$r.")) { break }
        $lines.Add("$next`t$(Format-Value $type $value)")
        $oid = $next
        $count++
    }
    Write-Host "$r : $count values"
}
[IO.File]::WriteAllLines($Out, $lines)
Write-Host "Written: $Out"
