// ptdevmode - builds a DEVMODE for a printer from its default PrintTicket with selected options.
//
//   ptdevmode caps <printer> <out.xml>
//       Writes the printer's PrintCapabilities (feature and option names).
//   ptdevmode make <printer> <out.devmode> [Feature=Option ...]
//       Feature/Option are local names as listed in the capabilities, e.g.
//       PageMediaSize=P271 AutoCut=Off CutEvery=N2 PageResolution=R300x600 MirrorPrint=On
//
// Build: see driver/compare.ps1
using System;
using System.IO;
using System.Printing;
using System.Printing.Interop;
using System.Text;
using System.Xml;

static class PtDevmode
{
    const string Psf = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";

    static string Local(string qname) { int i = qname.IndexOf(':'); return i < 0 ? qname : qname.Substring(i + 1); }

    // Matches the local name exactly, or with the scope prefix Windows adds to GPD features
    // ("AutoCut" -> "DocumentAutoCut" / "JobAutoCut" / "PageAutoCut").
    static XmlElement FindByLocalName(XmlNode parent, string xpath, XmlNamespaceManager ns, string local)
    {
        foreach (string candidate in new[] { local, "Document" + local, "Job" + local, "Page" + local })
            foreach (XmlElement e in parent.SelectNodes(xpath, ns))
                if (Local(e.GetAttribute("name")) == candidate) return e;
        return null;
    }

    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: see source header"); return 2; }
        using (var server = new LocalPrintServer())
        using (var queue = server.GetPrintQueue(args[1]))
        using (var conv = new PrintTicketConverter(args[1], PrintTicketConverter.MaxPrintSchemaVersion))
        {
            var caps = new XmlDocument();
            using (var s = queue.GetPrintCapabilitiesAsXml()) caps.Load(s);
            if (args[0] == "caps") { caps.Save(args[2]); return 0; }

            var doc = new XmlDocument();
            doc.Load(queue.DefaultPrintTicket.GetXmlStream());
            var ns = new XmlNamespaceManager(doc.NameTable);
            ns.AddNamespace("psf", Psf);
            var cns = new XmlNamespaceManager(caps.NameTable);
            cns.AddNamespace("psf", Psf);

            for (int i = 3; i < args.Length; i++)
            {
                string[] kv = args[i].Split('=');
                XmlElement capFeature = FindByLocalName(caps, "//psf:Feature", cns, kv[0]);
                if (capFeature == null) throw new Exception("feature not in capabilities: " + kv[0]);
                XmlElement capOption = FindByLocalName(capFeature, "psf:Option", cns, kv[1]);
                if (capOption == null) throw new Exception("option not in capabilities: " + args[i]);

                string optName = capOption.GetAttribute("name");
                string prefix = optName.Split(':')[0];
                string uri = capOption.GetNamespaceOfPrefix(prefix);
                if (string.IsNullOrEmpty(doc.DocumentElement.GetNamespaceOfPrefix(prefix)))
                    doc.DocumentElement.SetAttribute("xmlns:" + prefix, uri);

                XmlElement feature = FindByLocalName(doc, "//psf:Feature", ns, kv[0]);
                if (feature == null)
                {
                    feature = doc.CreateElement("psf", "Feature", Psf);
                    feature.SetAttribute("name", capFeature.GetAttribute("name"));
                    string fp = capFeature.GetAttribute("name").Split(':')[0];
                    if (string.IsNullOrEmpty(doc.DocumentElement.GetNamespaceOfPrefix(fp)))
                        doc.DocumentElement.SetAttribute("xmlns:" + fp, capFeature.GetNamespaceOfPrefix(fp));
                    doc.DocumentElement.AppendChild(feature);
                }
                foreach (XmlNode old in feature.SelectNodes("psf:Option", ns)) feature.RemoveChild(old);
                // Keep ScoredProperties with values (media size etc.); drop capability-only parts
                // (display names, parameter references, attributes other than name).
                var option = (XmlElement)doc.ImportNode(capOption, true);
                for (int a = option.Attributes.Count - 1; a >= 0; a--)
                    if (option.Attributes[a].Name != "name") option.Attributes.RemoveAt(a);
                foreach (XmlNode child in option.SelectNodes("psf:Property", ns)) option.RemoveChild(child);
                foreach (XmlElement sp in option.SelectNodes("psf:ScoredProperty", ns))
                    if (sp.SelectSingleNode("psf:Value", ns) == null) option.RemoveChild(sp);
                feature.AppendChild(option);
            }
            var ticket = new PrintTicket(new MemoryStream(Encoding.UTF8.GetBytes(doc.OuterXml)));
            byte[] dm = conv.ConvertPrintTicketToDevMode(ticket, BaseDevModeType.PrinterDefault);
            File.WriteAllBytes(args[2], dm);
            Console.WriteLine("devmode {0} bytes, dmPaperSize={1}, form '{2}'", dm.Length, BitConverter.ToInt16(dm, 0x4E),
                Encoding.Unicode.GetString(dm, 0x66, 64).TrimEnd('\0'));
            return 0;
        }
    }
}
