// Custom length spike (QL Label Maker phase 2): prints a test bitmap on a capture printer with a page size of its own
// (no label format, no Windows form), so custom_length_spike.ps1 can check the stream dot for dot and against the
// same length printed through a label format. Run by custom_length_spike.ps1.
//   custom_length_spike <printer> <route> <width 0.1 mm> <length 0.1 mm> <300|600> <expected.pbm>
// route: dm256  own DEVMODE: dmPaperSize DMPAPER_USER (256), dmPaperWidth/Length in 0.1 mm, no form name,
//               checked by the driver (DocumentProperties), then CreateDC/StartDoc/StretchDIBits
//        dm0    the same with dmPaperSize 0 (what .NET writes for a PaperSize with RawKind 0)
//        net    System.Drawing.Printing: new PaperSize("Custom", 1/100 in, 1/100 in) and PrintDocument
//        form:<name>  the paper of that name (label format / Brother paper) through PrintDocument (the oracle)
// The bitmap is the printable area as the job reports it (portrait: width = across the tape, height = feed),
// drawn bit by bit (no GDI+, so 1000 mm and more fit in memory).
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace QlLabels.Tests
{
    static class CustomLengthSpike
    {
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinter(string name, out IntPtr h, IntPtr defaults);
        [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int DocumentProperties(IntPtr hwnd, IntPtr h, string name, IntPtr outDm, IntPtr inDm, int mode);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr CreateDC(string driver, string device, string port, IntPtr dm);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int StartDoc(IntPtr dc, ref DOCINFO di);
        [DllImport("gdi32.dll")] static extern int StartPage(IntPtr dc);
        [DllImport("gdi32.dll")] static extern int EndPage(IntPtr dc);
        [DllImport("gdi32.dll")] static extern int EndDoc(IntPtr dc);
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll")]
        static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh,
                                        byte[] bits, byte[] info, uint usage, uint rop);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DOCINFO { public int cbSize; public string lpszDocName, lpszOutput, lpszDatatype; public int fwType; }

        const int DM_OUT_BUFFER = 2, DM_IN_BUFFER = 8;
        const int DM_ORIENTATION = 1, DM_PAPERSIZE = 2, DM_PAPERLENGTH = 4, DM_PAPERWIDTH = 8;
        const int DM_PRINTQUALITY = 0x400, DM_YRESOLUTION = 0x2000, DM_FORMNAME = 0x10000;
        // DEVMODEW offsets
        const int OffFields = 72, OffOrientation = 76, OffPaperSize = 78, OffPaperLength = 80, OffPaperWidth = 82;
        const int OffQuality = 90, OffYRes = 96, OffFormName = 102;
        const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90;
        const int PHYSICALWIDTH = 110, PHYSICALHEIGHT = 111, PHYSICALOFFSETX = 112, PHYSICALOFFSETY = 113;

        static int Main(string[] args)
        {
            if (args.Length != 6) { Console.WriteLine("usage: custom_length_spike <printer> <dm256|dm0|net|form:name> <width 0.1mm> <length 0.1mm> <300|600> <expected.pbm>"); return 2; }
            string printer = args[0], route = args[1], pbm = args[5];
            int width01 = int.Parse(args[2]), length01 = int.Parse(args[3]), dpiY = int.Parse(args[4]);
            if (route == "dm256" || route == "dm0") return ViaDevmode(printer, (short)(route == "dm256" ? 256 : 0), width01, length01, dpiY, pbm);
            if (route.StartsWith("dmpaper:")) return ViaDevmode(printer, short.Parse(route.Substring(8)), width01, length01, dpiY, pbm, null);
            if (route.StartsWith("dmform:")) return ViaDevmode(printer, 0, width01, length01, dpiY, pbm, route.Substring(7));
            return ViaPrintDocument(printer, route, width01, length01, dpiY, pbm);
        }

        // form: select that form by name (DM_FORMNAME, no size fields) instead of a size; paperSize other than 0 and
        // 256 selects that paper number (DM_PAPERSIZE only). The die-cut checks: what a real paper choice looks like.
        static int ViaDevmode(string printer, short paperSize, int width01, int length01, int dpiY, string pbm, string form = null)
        {
            bool bySize = form == null && (paperSize == 0 || paperSize == 256);
            IntPtr h;
            if (!OpenPrinter(printer, out h, IntPtr.Zero)) { Console.WriteLine("OpenPrinter failed"); return 1; }
            try
            {
                int size = DocumentProperties(IntPtr.Zero, h, printer, IntPtr.Zero, IntPtr.Zero, 0);
                IntPtr dm = Marshal.AllocHGlobal(size), checkedDm = Marshal.AllocHGlobal(size);
                try
                {
                    if (DocumentProperties(IntPtr.Zero, h, printer, dm, IntPtr.Zero, DM_OUT_BUFFER) < 0) { Console.WriteLine("DocumentProperties (read) failed"); return 1; }
                    int fields = Marshal.ReadInt32(dm, OffFields);
                    fields |= DM_ORIENTATION | DM_PRINTQUALITY | DM_YRESOLUTION;
                    if (bySize) fields = (fields | DM_PAPERSIZE | DM_PAPERLENGTH | DM_PAPERWIDTH) & ~DM_FORMNAME;
                    else if (form != null) fields = (fields | DM_FORMNAME) & ~(DM_PAPERSIZE | DM_PAPERLENGTH | DM_PAPERWIDTH);
                    else fields = (fields | DM_PAPERSIZE) & ~(DM_FORMNAME | DM_PAPERLENGTH | DM_PAPERWIDTH);
                    Marshal.WriteInt32(dm, OffFields, fields);
                    Marshal.WriteInt16(dm, OffOrientation, 1);
                    Marshal.WriteInt16(dm, OffPaperSize, paperSize);
                    Marshal.WriteInt16(dm, OffPaperLength, (short)(bySize ? length01 : 0));
                    Marshal.WriteInt16(dm, OffPaperWidth, (short)(bySize ? width01 : 0));
                    Marshal.WriteInt16(dm, OffQuality, 300);
                    Marshal.WriteInt16(dm, OffYRes, (short)dpiY);
                    for (int i = 0; i < 64; i += 2) Marshal.WriteInt16(dm, OffFormName + i, 0);
                    if (form != null)
                        for (int i = 0; i < form.Length && i < 31; i++) Marshal.WriteInt16(dm, OffFormName + 2 * i, form[i]);
                    Console.WriteLine("sent:      " + Describe(dm));
                    if (DocumentProperties(IntPtr.Zero, h, printer, checkedDm, dm, DM_IN_BUFFER | DM_OUT_BUFFER) < 0) { Console.WriteLine("DocumentProperties (check) failed"); return 1; }
                    Console.WriteLine("driver:    " + Describe(checkedDm));
                    IntPtr dc = CreateDC(null, printer, null, checkedDm);
                    if (dc == IntPtr.Zero) { Console.WriteLine("CreateDC failed"); return 1; }
                    try
                    {
                        var di = new DOCINFO { cbSize = Marshal.SizeOf(typeof(DOCINFO)), lpszDocName = "QL custom length spike" };
                        if (StartDoc(dc, ref di) <= 0 || StartPage(dc) <= 0) { Console.WriteLine("StartDoc/StartPage failed"); return 1; }
                        PrintPattern(dc, pbm);
                        EndPage(dc);
                        EndDoc(dc);
                    }
                    finally { DeleteDC(dc); }
                }
                finally { Marshal.FreeHGlobal(dm); Marshal.FreeHGlobal(checkedDm); }
            }
            finally { ClosePrinter(h); }
            return 0;
        }

        static int ViaPrintDocument(string printer, string route, int width01, int length01, int dpiY, string pbm)
        {
            var ps = new PrinterSettings { PrinterName = printer };
            if (!ps.IsValid) { Console.WriteLine("printer not found"); return 1; }
            PaperSize paper;
            if (route.StartsWith("form:"))
            {
                string name = route.Substring(5);
                paper = ps.PaperSizes.Cast<PaperSize>().FirstOrDefault(p => string.Equals(p.PaperName, name, StringComparison.OrdinalIgnoreCase));
                if (paper == null) { Console.WriteLine("no paper '" + name + "'"); return 1; }
            }
            else
                paper = new PaperSize("Custom", (int)Math.Round(width01 / 2.54), (int)Math.Round(length01 / 2.54));
            var res = ps.PrinterResolutions.Cast<PrinterResolution>().First(r => r.X == 300 && r.Y == dpiY);
            var page = new PageSettings(ps) { PaperSize = paper, PrinterResolution = res, Landscape = false, Color = false };
            Console.WriteLine("paper:     " + paper.PaperName + " RawKind " + paper.RawKind + ", " + paper.Width + " x " + paper.Height + " (1/100 in)");
            // What .NET hands the driver: the printer's DEVMODE with the page's settings copied in (unchecked).
            IntPtr hdm = ps.GetHdevmode();
            page.CopyToHdevmode(hdm);
            IntPtr dm = GlobalLock(hdm);
            Console.WriteLine("devmode:   " + Describe(dm));
            GlobalUnlock(hdm);
            GlobalFree(hdm);
            using (var doc = new PrintDocument { DocumentName = "QL custom length spike" })
            {
                doc.PrinterSettings = ps;
                doc.DefaultPageSettings = page;
                doc.PrintController = new StandardPrintController();
                doc.PrintPage += (s, e) =>
                {
                    IntPtr dc = e.Graphics.GetHdc();
                    try { PrintPattern(dc, pbm); }
                    finally { e.Graphics.ReleaseHdc(dc); }
                    e.HasMorePages = false;
                };
                doc.Print();
            }
            return 0;
        }

        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

        static string Describe(IntPtr dm)
        {
            int fields = Marshal.ReadInt32(dm, OffFields);
            string form = Marshal.PtrToStringUni(dm + OffFormName);
            if (form.Length > 32) form = form.Substring(0, 32);
            return "fields 0x" + fields.ToString("x") + " paperSize " + Marshal.ReadInt16(dm, OffPaperSize) +
                   " length " + Marshal.ReadInt16(dm, OffPaperLength) + " width " + Marshal.ReadInt16(dm, OffPaperWidth) +
                   " (0.1 mm) yres " + Marshal.ReadInt16(dm, OffYRes) + " form '" + form + "'";
        }

        // Reads the printable area in the job, draws the pattern over all of it at 1:1 and writes it as expected.pbm.
        static void PrintPattern(IntPtr dc, string pbm)
        {
            int w = GetDeviceCaps(dc, HORZRES), h = GetDeviceCaps(dc, VERTRES);
            Console.WriteLine("in job:    " + w + " x " + h + " dots at " + GetDeviceCaps(dc, LOGPIXELSX) + " x " + GetDeviceCaps(dc, LOGPIXELSY) +
                              " dpi, offset " + GetDeviceCaps(dc, PHYSICALOFFSETX) + "," + GetDeviceCaps(dc, PHYSICALOFFSETY) +
                              ", page " + GetDeviceCaps(dc, PHYSICALWIDTH) + " x " + GetDeviceCaps(dc, PHYSICALHEIGHT));
            int stride = ((w + 31) / 32) * 4, pstride = (w + 7) / 8;
            var bits = new byte[stride * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (Black(x, y, w, h)) bits[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
            var info = new byte[48];
            BitConverter.GetBytes(40).CopyTo(info, 0);
            BitConverter.GetBytes(w).CopyTo(info, 4);
            BitConverter.GetBytes(-h).CopyTo(info, 8);   // top-down
            BitConverter.GetBytes((short)1).CopyTo(info, 12);
            BitConverter.GetBytes((short)1).CopyTo(info, 14);
            BitConverter.GetBytes(2).CopyTo(info, 32);
            info[40] = info[41] = info[42] = 255;        // colour 0 white, colour 1 black
            if (StretchDIBits(dc, 0, 0, w, h, 0, 0, w, h, bits, info, 0, 0x00CC0020) == 0) throw new InvalidOperationException("StretchDIBits failed");
            using (var fs = File.Create(pbm))
            {
                var head = System.Text.Encoding.ASCII.GetBytes("P4\n" + w + " " + h + "\n");
                fs.Write(head, 0, head.Length);
                for (int y = 0; y < h; y++) fs.Write(bits, y * stride, pstride);
            }
        }

        // Frame on the outermost dots, dotted grid every 50 dots, an arrow along the feed and a block near the start
        // (asymmetric in both axes: shows mirroring and rotation), a solid bar every 100 mm of feed (at 300 lines/in).
        static bool Black(int x, int y, int w, int h)
        {
            if (x == 0 || y == 0 || x == w - 1 || y == h - 1) return true;
            if (x % 50 == 0 && y % 2 == 0) return true;
            if (y % 50 == 0 && x % 2 == 0) return true;
            if (y >= 10 && y < 50 && x >= 10 && x < 10 + (y - 10) / 2) return true;
            if (y >= 10 && y < 40 && x >= 60 && x < 80) return true;
            if (y > 0 && y % 1181 < 4 && x < w / 3) return true;
            return false;
        }
    }
}
