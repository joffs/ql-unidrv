// Status of a QL printer on the network (Standard TCP/IP port), the way Brother's own software reads it: one SNMPv1 GET of 1.3.6.1.4.1.2435.3.3.9.1.6.1.0 on UDP 161, whose value is
// the same 32-byte status the printer sends over USB in reply to ESC i S. Nothing is ever sent to the print
// port (TCP 9100), so a job from another PC can't be disturbed, and any number of PCs may ask.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Win32;

namespace QlLabels
{
    static class NetworkStatus
    {
        public const string StatusOid = "1.3.6.1.4.1.2435.3.3.9.1.6.1.0";
        const string PortsKey = @"SYSTEM\CurrentControlSet\Control\Print\Monitors\Standard TCP/IP Port\Ports\";
        const int TimeoutMs = 2000, Tries = 2;  // as Brother's

        // A Standard TCP/IP port: the printer's address and the SNMP community set on the port (else "public").
        public static bool TryPort(string port, out string host, out string community)
        {
            host = community = null;
            if (string.IsNullOrEmpty(port) || port.StartsWith(@"\\")) return false;
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(PortsKey + port))
                {
                    if (k == null) return false;
                    host = Convert.ToString(k.GetValue("IPAddress"));
                    if (string.IsNullOrEmpty(host)) host = Convert.ToString(k.GetValue("HostName"));
                    community = Convert.ToString(k.GetValue("SNMP Community"));
                }
            }
            catch (Exception) { return false; }
            if (string.IsNullOrEmpty(community)) community = "public";
            return !string.IsNullOrEmpty(host);
        }

        public static bool IsNetworkPort(string port)
        {
            string host, community;
            return TryPort(port, out host, out community);
        }

        // Which QL model answers at this address (install.ps1 -NetworkPrinter): driver name, model, loaded roll.
        // Throws with an explanation if it doesn't answer as a QL printer.
        public static List<string> Identify(string host)
        {
            string error;
            var value = Snmp.Get(host, "public", StatusOid, TimeoutMs, Tries, out error);
            if (value == null)
                throw new InvalidOperationException(error == Snmp.NoAnswer
                    ? Lang.F("No QL printer answers at {0} (SNMP, UDP port 161): is it switched on, is the address right, is SNMP enabled on it?", host)
                    : error);
            var s = QlStatus.TryParse(value, value.Length, DateTime.Now);
            if (s == null) throw new InvalidOperationException(Lang.F("The device at {0} is not a QL printer (its answer has {1} bytes).", host, value.Length));
            var model = QlModel.ForStatusCode(s.Model);
            if (model == null) throw new InvalidOperationException(Lang.F("The QL printer at {0} reports {1}, which this driver doesn't support.", host, s.ModelName));
            return new List<string> { model.DriverName, model.Name, s.LoadedText };
        }

        // Asks the printer; the result looks like a direct USB read with a reply (or none).
        public static DeviceResult Query(string printer, string port)
        {
            var r = new DeviceResult { Printer = printer, Port = port };
            string host, community;
            if (!TryPort(port, out host, out community)) { r.State = DeviceState.NotUsb; return r; }
            r.Network = host;
            // The port's community first (a user may have changed it on the printer), then Brother's "public".
            string error = null;
            byte[] value = null;
            foreach (var c in new[] { community, "public" }.Distinct())
            {
                value = Snmp.Get(host, c, StatusOid, TimeoutMs, Tries, out error);
                if (value != null || error != Snmp.NoAnswer) break;
            }
            r.Error = error;
            r.RequestSent = true;
            if (value == null)
            {
                r.State = r.Error == Snmp.NoAnswer ? DeviceState.NotConnected : DeviceState.Failed;
                return r;
            }
            r.State = DeviceState.Open;
            r.Reply = QlStatus.TryParse(value, value.Length, DateTime.Now);
            if (r.Reply == null)
            {
                r.State = DeviceState.Failed;
                r.Error = "The printer's answer is not a QL status (" + value.Length + " bytes).";
            }
            return r;
        }

        // "Printer details" in QL Printer Status: counters and device facts of a network QL printer (network-status.md
        // §9: standard Printer/Host MIB values plus Brother's own). Read only, one GET per value (SNMPv1 fails a whole
        // request when one OID is missing). The serial number identifies the printer: it is shown, never logged.
        public const string PagesOid = "1.3.6.1.2.1.43.10.2.1.4.1.1";                 // prtMarkerLifeCount
        public const string FirmwareOid = "1.3.6.1.2.1.43.15.1.1.6.1.1";              // prtFirmwareVersion
        public const string MemoryOid = "1.3.6.1.2.1.25.2.2.0";                       // hrMemorySize, KB
        public const string CountersOid = "1.3.6.1.4.1.2435.2.3.9.4.2.1.5.5.10.0";    // Brother: cuts, pages, metres
        public const string SerialOid = "1.3.6.1.4.1.2435.2.3.9.4.2.1.5.5.1.0";
        public const string NetworkFirmwareOid = "1.3.6.1.4.1.2435.2.4.3.1240.1.4.0"; // print server NC-17002h (QL-720NW); other servers: not shown
        public const int CutsId = 0xD001, PagesId = 0x0001, MetresId = 0x6001;

        public static NetworkDetails ReadDetails(string port)
        {
            string host, community;
            var d = new NetworkDetails();
            if (!TryPort(port, out host, out community)) return d;
            d.Host = host;
            byte tag = 0;
            string error = null;
            byte[] pages = null;
            foreach (var c in new[] { community, "public" }.Distinct())
            {
                community = c;
                pages = Snmp.GetAny(host, c, PagesOid, TimeoutMs, Tries, out tag, out error);
                if (pages != null || error != Snmp.NoAnswer) break;
            }
            if (error == Snmp.NoAnswer) return d;
            d.Answered = true;
            Func<string, Tuple<byte, byte[]>> get = oid =>
            {
                byte t;
                string e;
                var v = Snmp.GetAny(host, community, oid, TimeoutMs, 1, out t, out e);
                return v == null ? null : Tuple.Create(t, v);
            };
            var counters = get(CountersOid);
            var block = counters != null && counters.Item1 == 0x04 ? ParseCounters(counters.Item2) : new Dictionary<int, long>();
            long n;
            if (pages != null) d.Pages = Snmp.ToNumber(pages, tag);
            if (d.Pages == null && block.TryGetValue(PagesId, out n)) d.Pages = n;
            if (block.TryGetValue(CutsId, out n)) d.Cuts = n;
            if (block.TryGetValue(MetresId, out n)) d.Metres = n;
            var mem = get(MemoryOid);
            if (mem != null) d.MemoryKb = Snmp.ToNumber(mem.Item2, mem.Item1);
            d.Firmware = Text(get(FirmwareOid));
            d.NetworkFirmware = Text(get(NetworkFirmwareOid));
            d.Serial = Text(get(SerialOid));
            return d;
        }

        static string Text(Tuple<byte, byte[]> v)
        {
            if (v == null || v.Item1 != 0x04) return null;
            string s = System.Text.Encoding.ASCII.GetString(v.Item2).Trim('\0', ' ');
            return s.Length > 0 && s.All(ch => ch >= ' ' && ch < 0x7F) ? s : null;
        }

        // Brother's counter block: records of a 2-byte id, a length byte and a big-endian value; FF ends it.
        public static Dictionary<int, long> ParseCounters(byte[] b)
        {
            var r = new Dictionary<int, long>();
            int i = 0;
            while (i + 3 <= b.Length && b[i] != 0xFF)
            {
                int id = b[i] << 8 | b[i + 1], len = b[i + 2];
                if (len > 8 || i + 3 + len > b.Length) break;
                long v = 0;
                for (int k = 0; k < len; k++) v = v << 8 | b[i + 3 + k];
                r[id] = v;
                i += 3 + len;
            }
            return r;
        }
    }

    // What "Printer details" shows of a network printer; null fields: the printer doesn't report them.
    sealed class NetworkDetails
    {
        public string Host;
        public bool Answered;
        public long? Pages, Cuts, Metres, MemoryKb;
        public string Firmware, NetworkFirmware, Serial;
    }

    // Minimal SNMPv1 GET (BER), enough for one OID with an OCTET STRING value (GetAny: a value of any type).
    static class Snmp
    {
        public const string NoAnswer = "No answer.";
        static readonly System.Security.Cryptography.RandomNumberGenerator Random = System.Security.Cryptography.RandomNumberGenerator.Create();

        // A request id nobody on the LAN can guess (security audit 2026-10-24 L2).
        static int NewRequestId()
        {
            var b = new byte[4];
            lock (Random) Random.GetBytes(b);
            return BitConverter.ToInt32(b, 0) & 0x7FFFFFFF;
        }

        // The value of oid, or null with error set (NoAnswer: no reply in time).
        public static byte[] Get(string host, string community, string oid, int timeoutMs, int tries, out string error)
        {
            byte tag;
            return Request(host, community, oid, timeoutMs, tries, false, out tag, out error);
        }

        // Any value type: tag 0x04 OCTET STRING, 0x02 INTEGER, 0x41 Counter32, 0x42 Gauge32, ...
        public static byte[] GetAny(string host, string community, string oid, int timeoutMs, int tries, out byte tag, out string error)
        {
            return Request(host, community, oid, timeoutMs, tries, true, out tag, out error);
        }

        static byte[] Request(string host, string community, string oid, int timeoutMs, int tries, bool anyType, out byte tag, out string error)
        {
            error = null;
            tag = 0;
            IPAddress address;
            try
            {
                if (!IPAddress.TryParse(host, out address))
                    address = Dns.GetHostAddresses(host).OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1).First();
            }
            catch (Exception ex) { error = "The address '" + host + "' can't be resolved: " + ex.Message; return null; }
            using (var udp = new UdpClient(address.AddressFamily))
            {
                udp.Client.ReceiveTimeout = timeoutMs;
                var target = new IPEndPoint(address, 161);
                for (int i = 0; i < tries; i++)
                {
                    int id = NewRequestId();
                    var request = BuildGet(community, oid, id);
                    try
                    {
                        udp.Send(request, request.Length, target);
                        var deadline = Environment.TickCount + timeoutMs;
                        while (Environment.TickCount - deadline < 0)
                        {
                            var from = new IPEndPoint(IPAddress.Any, 0);
                            var reply = udp.Receive(ref from);
                            if (!from.Address.Equals(address) || from.Port != 161) continue;  // only the printer answers
                            byte[] value;
                            int replyId;
                            if (ParseAny(reply, anyType, out replyId, out tag, out value, out error) && replyId == id) return value;
                            if (replyId == id) return null;  // our answer, but an error (error set)
                        }
                    }
                    catch (SocketException ex)
                    {
                        if (ex.SocketErrorCode != SocketError.TimedOut && ex.SocketErrorCode != SocketError.ConnectionReset)
                        {
                            error = ex.Message;
                            return null;
                        }
                    }
                }
            }
            error = NoAnswer;
            return null;
        }

        public static byte[] BuildGet(string community, string oid, int id)
        {
            var varbind = Tlv(0x30, Concat(Tlv(0x06, EncodeOid(oid)), new byte[] { 0x05, 0x00 }));
            var pdu = Tlv(0xA0, Concat(Integer(id), Integer(0), Integer(0), Tlv(0x30, varbind)));
            return Tlv(0x30, Concat(Integer(0), Tlv(0x04, System.Text.Encoding.ASCII.GetBytes(community)), pdu));
        }

        // A GetResponse with one varbind: its request-id and OCTET STRING value.
        public static bool ParseResponse(byte[] data, out int id, out byte[] value, out string error)
        {
            byte tag;
            return ParseAny(data, false, out id, out tag, out value, out error);
        }

        public static bool ParseAny(byte[] data, bool anyType, out int id, out byte tag, out byte[] value, out string error)
        {
            id = -1;
            tag = 0;
            value = null;
            error = Lang.T("The printer's SNMP answer can't be read.");
            try
            {
                int pos = 0;
                var msg = Read(data, ref pos, 0x30);
                int p = 0;
                Read(msg, ref p, 0x02);                // version
                Read(msg, ref p, 0x04);                // community
                var pdu = Read(msg, ref p, 0xA2);      // GetResponse
                int q = 0;
                id = ToInt(Read(pdu, ref q, 0x02));
                int status = ToInt(Read(pdu, ref q, 0x02));
                Read(pdu, ref q, 0x02);                // error-index
                if (status != 0)
                {
                    error = status == 2 ? Lang.T("The printer doesn't report a QL status over SNMP (noSuchName).") : Lang.F("SNMP error {0}.", status);
                    return false;
                }
                var list = Read(pdu, ref q, 0x30);
                int v = 0;
                var bind = Read(list, ref v, 0x30);
                int b = 0;
                Read(bind, ref b, 0x06);
                if (b >= bind.Length) return false;
                tag = bind[b];
                var content = Read(bind, ref b, tag);
                if (tag != 0x04 && !anyType)
                {
                    error = Lang.F("The printer doesn't report a QL status over SNMP (value type 0x{0}).", tag.ToString("X2"));
                    return false;
                }
                value = content;
                error = null;
                return true;
            }
            catch (Exception) { return false; }
        }

        static byte[] Read(byte[] data, ref int pos, byte tag)
        {
            if (pos >= data.Length || data[pos] != tag) throw new FormatException("unexpected tag");
            pos++;
            int len = data[pos++];
            if (len > 0x80)
            {
                int n = len & 0x7F;
                if (n > 3) throw new FormatException("length");
                len = 0;
                for (int i = 0; i < n; i++) len = (len << 8) | data[pos++];
            }
            if (len < 0 || pos + len > data.Length) throw new FormatException("length");
            var content = new byte[len];
            Array.Copy(data, pos, content, 0, len);
            pos += len;
            return content;
        }

        // INTEGER (signed), Counter32/Gauge32/TimeTicks (unsigned); null for other types.
        public static long? ToNumber(byte[] b, byte tag)
        {
            if (b.Length == 0 || b.Length > 5) return null;
            if (tag == 0x02) return b.Length <= 4 ? ToInt(b) : (long?)null;
            if (tag < 0x41 || tag > 0x43) return null;
            long v = 0;
            foreach (var x in b) v = v << 8 | x;
            return v;
        }

        static int ToInt(byte[] b)
        {
            if (b.Length == 0 || b.Length > 4) throw new FormatException("integer");
            int v = (b[0] & 0x80) != 0 ? -1 : 0;
            foreach (var x in b) v = (v << 8) | x;
            return v;
        }

        static byte[] Integer(int value)
        {
            var bytes = new List<byte>();
            do { bytes.Insert(0, (byte)(value & 0xFF)); value >>= 8; } while (value != 0 && value != -1);
            if ((bytes[0] & 0x80) != 0 && value == 0) bytes.Insert(0, 0);
            return Tlv(0x02, bytes.ToArray());
        }

        static byte[] EncodeOid(string oid)
        {
            var parts = oid.Split('.').Select(uint.Parse).ToArray();
            var bytes = new List<byte> { (byte)(parts[0] * 40 + parts[1]) };
            foreach (var part in parts.Skip(2))
            {
                var chunk = new List<byte>();
                uint v = part;
                chunk.Insert(0, (byte)(v & 0x7F));
                while ((v >>= 7) != 0) chunk.Insert(0, (byte)(0x80 | (v & 0x7F)));
                bytes.AddRange(chunk);
            }
            return bytes.ToArray();
        }

        static byte[] Tlv(byte tag, byte[] content)
        {
            var bytes = new List<byte> { tag };
            int len = content.Length;
            if (len < 0x80) bytes.Add((byte)len);
            else if (len < 0x100) { bytes.Add(0x81); bytes.Add((byte)len); }
            else { bytes.Add(0x82); bytes.Add((byte)(len >> 8)); bytes.Add((byte)len); }
            bytes.AddRange(content);
            return bytes.ToArray();
        }

        static byte[] Concat(params byte[][] parts) { return parts.SelectMany(p => p).ToArray(); }
    }
}
