// Brother's label format files bsq80V.DAT / .ORG: "2.00", count, record
// size 0x8E, then records of a 130-byte Windows-1252 name, paper id, length and feed (0.01 mm).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace QlLabels
{
    static class BrotherDat
    {
        public const int MaxFormats = 20;  // Brother's driver reads at most 20
        const int RecordSize = 0x8E, NameLength = 0x82;
        static readonly Encoding Ansi = Encoding.GetEncoding(1252);

        public static List<LabelFormat> Read(string path)
        {
            using (var s = File.OpenRead(path)) return Read(s);
        }

        public static List<LabelFormat> Read(Stream stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] b = ms.ToArray();
            if (b.Length < 10 || Encoding.ASCII.GetString(b, 0, 2) != "2.") throw new InvalidDataException(Lang.T("Not a Brother label format file."));
            int count = BitConverter.ToUInt16(b, 4), size = BitConverter.ToUInt16(b, 6);
            if (size != RecordSize || b.Length < 10 + count * size) throw new InvalidDataException(Lang.T("Unexpected record size."));
            var list = new List<LabelFormat>();
            for (int i = 0; i < count; i++)
            {
                int o = 10 + i * size;
                int n = Array.IndexOf(b, (byte)0, o, NameLength);
                list.Add(new LabelFormat
                {
                    Name = Ansi.GetString(b, o, (n < 0 ? o + NameLength : n) - o),
                    PaperId = (int)BitConverter.ToUInt32(b, o + NameLength),
                    LengthMm = BitConverter.ToUInt32(b, o + NameLength + 4) / 100.0,
                    FeedMm = BitConverter.ToUInt32(b, o + NameLength + 8) / 100.0,
                    Key = i + 1,
                });
            }
            return list;
        }

        // Names Windows-1252 cannot represent come out with '?'; callers warn first (CanEncode).
        public static void Write(string path, IList<LabelFormat> formats)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(Encoding.ASCII.GetBytes("2.00"));
                w.Write((ushort)formats.Count);
                w.Write((ushort)RecordSize);
                w.Write((ushort)0);
                foreach (var f in formats)
                {
                    byte[] name = new byte[NameLength];
                    byte[] src = Ansi.GetBytes(f.Name);
                    Array.Copy(src, name, Math.Min(src.Length, NameLength - 1));
                    w.Write(name);
                    w.Write((uint)f.PaperId);
                    w.Write((uint)Math.Round(f.LengthMm * 100));
                    w.Write((uint)Math.Round(f.FeedMm * 100));
                }
                File.WriteAllBytes(path, ms.ToArray());
            }
        }

        public static bool CanEncode(string name)
        {
            try
            {
                Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetBytes(name);
                return true;
            }
            catch (EncoderFallbackException) { return false; }
        }
    }
}
