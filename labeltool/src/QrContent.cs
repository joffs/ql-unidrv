// QR code content types: a web address, contact, Wi-Fi network, e-mail, phone number or SMS from its fields, as
// the text phone cameras understand (the common formats: vCard 3.0, WIFI:, mailto:, tel:, SMSTO:). Date and time
// fields ({date}, Fields) stay usable in every field: escaping leaves them alone, they are filled in when printed.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QlLabels
{
    enum QrType { Text, Url, Contact, Wifi, Email, Phone, Sms }

    static class QrContent
    {
        // As written to .qllabel files ("qrType").
        public static readonly string[] Names = { "text", "url", "contact", "wifi", "email", "phone", "sms" };

        // The fields of each type, in panel order (keys in the file's "qrFields").
        public static string[] FieldsOf(QrType type)
        {
            switch (type)
            {
                case QrType.Url: return new[] { "url" };
                case QrType.Contact: return new[] { "firstName", "lastName", "company", "phone", "email", "street", "city", "website" };
                case QrType.Wifi: return new[] { "ssid", "password", "security", "hidden" };
                case QrType.Email: return new[] { "to", "subject", "body" };
                case QrType.Phone: return new[] { "number" };
                case QrType.Sms: return new[] { "number", "message" };
                default: return new string[0];
            }
        }

        // Wi-Fi "security": WPA (also WPA2/WPA3), WEP or nopass (an open network).
        public static readonly string[] Security = { "WPA", "WEP", "nopass" };

        static string Get(IDictionary<string, string> f, string key)
        {
            string v;
            return f != null && f.TryGetValue(key, out v) && v != null ? v : "";
        }

        // The text the QR code holds (fields not yet filled in).
        public static string Build(QrType type, IDictionary<string, string> f)
        {
            switch (type)
            {
                case QrType.Url:
                {
                    string url = Get(f, "url").Trim();
                    return url.Length == 0 || url.Contains("://") || url.StartsWith("{", StringComparison.Ordinal) ? url : "https://" + url;
                }
                case QrType.Contact:
                {
                    string first = Get(f, "firstName").Trim(), last = Get(f, "lastName").Trim();
                    var v = new StringBuilder("BEGIN:VCARD\r\nVERSION:3.0\r\n");
                    v.Append("N:").Append(VCard(last)).Append(';').Append(VCard(first)).Append(";;;\r\n");
                    v.Append("FN:").Append(VCard((first + " " + last).Trim())).Append("\r\n");
                    Action<string, string> line = (name, value) => { if (value.Trim().Length > 0) v.Append(name).Append(':').Append(VCard(value.Trim())).Append("\r\n"); };
                    line("ORG", Get(f, "company"));
                    line("TEL;TYPE=CELL", Get(f, "phone"));
                    line("EMAIL", Get(f, "email"));
                    string street = Get(f, "street").Trim(), city = Get(f, "city").Trim();
                    if (street.Length > 0 || city.Length > 0) v.Append("ADR:;;").Append(VCard(street)).Append(';').Append(VCard(city)).Append(";;;\r\n");
                    line("URL", Get(f, "website"));
                    return v.Append("END:VCARD").ToString();
                }
                case QrType.Wifi:
                {
                    string security = Get(f, "security");
                    if (!Security.Contains(security)) security = "WPA";
                    var w = new StringBuilder("WIFI:T:").Append(security).Append(";S:").Append(Wifi(Get(f, "ssid"))).Append(';');
                    if (security != "nopass") w.Append("P:").Append(Wifi(Get(f, "password"))).Append(';');
                    if (Get(f, "hidden") == "true") w.Append("H:true;");
                    return w.Append(';').ToString();
                }
                case QrType.Email:
                {
                    var query = new List<string>();
                    if (Get(f, "subject").Length > 0) query.Add("subject=" + Percent(Get(f, "subject")));
                    if (Get(f, "body").Length > 0) query.Add("body=" + Percent(Get(f, "body")));
                    return "mailto:" + Get(f, "to").Trim() + (query.Count > 0 ? "?" + string.Join("&", query) : "");
                }
                case QrType.Phone: return "tel:" + Number(Get(f, "number"));
                case QrType.Sms: return "SMSTO:" + Number(Get(f, "number")) + ":" + Get(f, "message");
                default: return "";
            }
        }

        // null when the fields are enough for the type, else what is missing.
        public static string Missing(QrType type, IDictionary<string, string> f)
        {
            Func<string, bool> empty = key => Get(f, key).Trim().Length == 0;
            switch (type)
            {
                case QrType.Url: return empty("url") ? Lang.T("Enter the web address.") : null;
                case QrType.Contact: return FieldsOf(type).All(empty) ? Lang.T("Enter at least a name.") : null;
                case QrType.Wifi:
                    if (empty("ssid")) return Lang.T("Enter the network name.");
                    return Get(f, "security") != "nopass" && empty("password") ? Lang.T("Enter the password (or choose an open network).") : null;
                case QrType.Email: return empty("to") ? Lang.T("Enter the e-mail address.") : null;
                case QrType.Phone: case QrType.Sms: return Number(Get(f, "number")).Length == 0 ? Lang.T("Enter the phone number.") : null;
                default: return null;
            }
        }

        // vCard 3.0 text values: backslash, comma, semicolon and line breaks escaped.
        static string VCard(string s)
        {
            return Fields.MapText(s, t => t.Replace("\\", "\\\\").Replace(",", "\\,").Replace(";", "\\;").Replace("\r\n", "\\n").Replace("\n", "\\n"));
        }

        // Wi-Fi network name and password: backslash, semicolon, comma, colon and quote escaped.
        static string Wifi(string s)
        {
            return Fields.MapText(s, t =>
            {
                var b = new StringBuilder();
                foreach (char c in t) { if ("\\;,:\"".IndexOf(c) >= 0) b.Append('\\'); b.Append(c); }
                return b.ToString();
            });
        }

        // mailto: subject and body percent-encoded (UTF-8), line breaks as CR LF.
        static string Percent(string s)
        {
            return Fields.MapText(s.Replace("\r\n", "\n").Replace("\n", "\r\n"), t => Uri.EscapeDataString(t));
        }

        // A phone number without spaces and separators: digits, a leading +, and fields.
        static string Number(string s)
        {
            return Fields.MapText(s.Trim(), t => new string(t.Where(c => char.IsDigit(c) || c == '+' || c == '*' || c == '#').ToArray()));
        }
    }
}
