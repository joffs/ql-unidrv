// Label editor: page geometry from the driver and printing of a finished 1-bit label bitmap, dot for dot.
// The bitmap is sent with StretchDIBits at 1:1 into the printable area (no GDI+ scaling, no grey for Unidrv to
// halftone). Checked dot for dot on a capture printer by labeltool\test\editor_spike.ps1.
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Linq;
using System.Runtime.InteropServices;

namespace QlLabels
{
    // The printable area of one paper on one printer, in device dots, as the driver reports it.
    sealed class LabelGeometry
    {
        public int DpiX, DpiY;                 // 300 x 300 or 300 x 600
        public int Width, Height;              // printable area (HORZRES / VERTRES)
        public int OffsetX, OffsetY;           // printable area's corner from the page's corner
        public int PageWidth, PageHeight;      // whole page
        public bool Landscape;

        public override string ToString()
        {
            return Width + " x " + Height + " dots at " + DpiX + " x " + DpiY + " dpi, offset " + OffsetX + "," + OffsetY +
                   ", page " + PageWidth + " x " + PageHeight + (Landscape ? ", landscape" : "");
        }
    }

    // A length of its own on continuous tape: no form, the driver gets its own DEVMODE with DMPAPER_USER and the size in
    // 0.1 mm (custom length spike, test/custom_length_spike.ps1: dot-exact; a size equal to a die-cut paper stays
    // continuous tape, as the plug-in keeps continuous tape for dmPaperSize 256).
    sealed class CustomPage
    {
        public string Printer;
        public int Width01, Length01;   // the tape's width, the label's length (0.1 mm)
        public int DpiY;                // 300 or 600 lines per inch
    }

    static class LabelPrinter
    {
        [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int index);
        [DllImport("gdi32.dll")]
        static extern int StretchDIBits(IntPtr dc, int x, int y, int w, int h, int sx, int sy, int sw, int sh,
                                        byte[] bits, byte[] info, uint usage, uint rop);

        const int HORZRES = 8, VERTRES = 10, LOGPIXELSX = 88, LOGPIXELSY = 90;
        const int PHYSICALWIDTH = 110, PHYSICALHEIGHT = 111, PHYSICALOFFSETX = 112, PHYSICALOFFSETY = 113;
        const uint DIB_RGB_COLORS = 0, SRCCOPY = 0x00CC0020;

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
        [DllImport("gdi32.dll")] static extern int AbortDoc(IntPtr dc);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DOCINFO { public int cbSize; public string lpszDocName, lpszOutput, lpszDatatype; public int fwType; }

        const int DM_OUT_BUFFER = 2, DM_IN_BUFFER = 8;
        const int DM_ORIENTATION = 1, DM_PAPERSIZE = 2, DM_PAPERLENGTH = 4, DM_PAPERWIDTH = 8, DM_COPIES = 0x100;
        const int DM_PRINTQUALITY = 0x400, DM_YRESOLUTION = 0x2000, DM_FORMNAME = 0x10000;
        const short DMPAPER_USER = 256;
        // DEVMODEW offsets
        const int OffFields = 72, OffOrientation = 76, OffPaperSize = 78, OffPaperLength = 80, OffPaperWidth = 82, OffCopies = 86;
        const int OffQuality = 90, OffYRes = 96, OffFormName = 102;

        // Page settings for a paper (label format or Brother paper, by its name) and resolution (300 or 600 lines/inch).
        public static PageSettings Settings(string printer, string paperName, int dpiY, bool landscape)
        {
            var ps = new PrinterSettings { PrinterName = printer };
            if (!ps.IsValid) throw new ArgumentException("printer not found: " + printer);
            var size = ps.PaperSizes.Cast<PaperSize>()
                .FirstOrDefault(p => string.Equals(p.PaperName, paperName, StringComparison.OrdinalIgnoreCase));
            if (size == null) throw new ArgumentException(printer + " does not offer '" + paperName + "'");
            var res = ps.PrinterResolutions.Cast<PrinterResolution>().FirstOrDefault(r => r.X == 300 && r.Y == dpiY);
            if (res == null) throw new ArgumentException(printer + " does not offer 300 x " + dpiY + " dpi");
            var page = new PageSettings(ps) { PaperSize = size, PrinterResolution = res, Landscape = landscape, Color = false };
            return page;
        }

        public static LabelGeometry Measure(PageSettings page)
        {
            using (var g = page.PrinterSettings.CreateMeasurementGraphics(page))
            {
                IntPtr dc = g.GetHdc();
                try { return Read(dc, page.Landscape); }
                finally { g.ReleaseHdc(dc); }
            }
        }

        // The printable area of a length of its own, as the driver reports it.
        public static LabelGeometry Measure(CustomPage page)
        {
            LabelGeometry g = null;
            WithDc(page, 1, dc => g = Read(dc, false));
            return g;
        }

        // The printer's own settings (cut, ...) with the size, resolution and copies set; checked by the driver; then a
        // DC on it. A form name would win over the size, so it is cleared.
        static void WithDc(CustomPage page, short copies, Action<IntPtr> use)
        {
            IntPtr h;
            if (!OpenPrinter(page.Printer, out h, IntPtr.Zero)) throw new ArgumentException("printer not found: " + page.Printer);
            try
            {
                int size = DocumentProperties(IntPtr.Zero, h, page.Printer, IntPtr.Zero, IntPtr.Zero, 0);
                if (size <= 0) throw new InvalidOperationException("DocumentProperties failed");
                IntPtr dm = Marshal.AllocHGlobal(size), ok = Marshal.AllocHGlobal(size);
                try
                {
                    if (DocumentProperties(IntPtr.Zero, h, page.Printer, dm, IntPtr.Zero, DM_OUT_BUFFER) < 0) throw new InvalidOperationException("DocumentProperties failed");
                    int fields = Marshal.ReadInt32(dm, OffFields);
                    fields = (fields | DM_ORIENTATION | DM_PAPERSIZE | DM_PAPERLENGTH | DM_PAPERWIDTH | DM_COPIES | DM_PRINTQUALITY | DM_YRESOLUTION) & ~DM_FORMNAME;
                    Marshal.WriteInt32(dm, OffFields, fields);
                    Marshal.WriteInt16(dm, OffOrientation, 1);
                    Marshal.WriteInt16(dm, OffPaperSize, DMPAPER_USER);
                    Marshal.WriteInt16(dm, OffPaperLength, (short)page.Length01);
                    Marshal.WriteInt16(dm, OffPaperWidth, (short)page.Width01);
                    Marshal.WriteInt16(dm, OffCopies, copies);
                    Marshal.WriteInt16(dm, OffQuality, 300);
                    Marshal.WriteInt16(dm, OffYRes, (short)page.DpiY);
                    for (int i = 0; i < 64; i += 2) Marshal.WriteInt16(dm, OffFormName + i, 0);
                    if (DocumentProperties(IntPtr.Zero, h, page.Printer, ok, dm, DM_IN_BUFFER | DM_OUT_BUFFER) < 0) throw new InvalidOperationException("DocumentProperties failed");
                    IntPtr dc = CreateDC(null, page.Printer, null, ok);
                    if (dc == IntPtr.Zero) throw new InvalidOperationException("CreateDC failed: " + page.Printer);
                    try { use(dc); }
                    finally { DeleteDC(dc); }
                }
                finally { Marshal.FreeHGlobal(dm); Marshal.FreeHGlobal(ok); }
            }
            finally { ClosePrinter(h); }
        }

        // Prints the bitmap on a length of its own (as Print below); copies through the driver. skipX: where the tape's
        // band starts in the printable area (LabelLayout.SkipX).
        public static void Print(CustomPage page, Bitmap label, int skipX, string documentName, short copies, Action<LabelGeometry> measured)
        {
            byte[] info, bits;
            ToDib(label, out info, out bits);
            WithDc(page, copies, dc =>
            {
                var di = new DOCINFO { cbSize = Marshal.SizeOf(typeof(DOCINFO)), lpszDocName = documentName };
                if (StartDoc(dc, ref di) <= 0) throw new InvalidOperationException("StartDoc failed");
                bool done = false;
                try
                {
                    if (StartPage(dc) <= 0) throw new InvalidOperationException("StartPage failed");
                    if (measured != null) measured(Read(dc, false));
                    if (StretchDIBits(dc, skipX, 0, label.Width, label.Height, 0, 0, label.Width, label.Height, bits, info, DIB_RGB_COLORS, SRCCOPY) == 0)
                        throw new InvalidOperationException("StretchDIBits failed");
                    if (EndPage(dc) <= 0 || EndDoc(dc) <= 0) throw new InvalidOperationException("EndPage/EndDoc failed");
                    done = true;
                }
                finally { if (!done) AbortDoc(dc); }
            });
        }

        static LabelGeometry Read(IntPtr dc, bool landscape)
        {
            return new LabelGeometry
            {
                DpiX = GetDeviceCaps(dc, LOGPIXELSX), DpiY = GetDeviceCaps(dc, LOGPIXELSY),
                Width = GetDeviceCaps(dc, HORZRES), Height = GetDeviceCaps(dc, VERTRES),
                OffsetX = GetDeviceCaps(dc, PHYSICALOFFSETX), OffsetY = GetDeviceCaps(dc, PHYSICALOFFSETY),
                PageWidth = GetDeviceCaps(dc, PHYSICALWIDTH), PageHeight = GetDeviceCaps(dc, PHYSICALHEIGHT),
                Landscape = landscape,
            };
        }

        // Prints the bitmap (1 bpp, the printable area's size in dots) once per page; copies through the driver.
        // measured: the geometry seen inside the print job (for checks; null if not wanted).
        public static void Print(PageSettings page, Bitmap label, string documentName, short copies, Action<LabelGeometry> measured)
        {
            byte[] info, bits;
            ToDib(label, out info, out bits);
            using (var doc = new PrintDocument { DocumentName = documentName })
            {
                doc.PrinterSettings = page.PrinterSettings;
                doc.PrinterSettings.Copies = copies;
                doc.DefaultPageSettings = page;
                doc.PrintController = new StandardPrintController();
                doc.PrintPage += (s, e) =>
                {
                    IntPtr dc = e.Graphics.GetHdc();
                    try
                    {
                        if (measured != null) measured(Read(dc, e.PageSettings.Landscape));
                        if (StretchDIBits(dc, 0, 0, label.Width, label.Height, 0, 0, label.Width, label.Height,
                                          bits, info, DIB_RGB_COLORS, SRCCOPY) == 0)
                            throw new InvalidOperationException("StretchDIBits failed");
                    }
                    finally { e.Graphics.ReleaseHdc(dc); }
                    e.HasMorePages = false;
                };
                doc.Print();
            }
        }

        // 1 bpp top-down DIB: colour 0 white, 1 black; rows padded to 4 bytes. Any non-white pixel of the bitmap is black.
        static void ToDib(Bitmap bmp, out byte[] info, out byte[] bits)
        {
            int w = bmp.Width, h = bmp.Height, stride = ((w + 31) / 32) * 4;
            info = new byte[40 + 8];
            BitConverter.GetBytes(40).CopyTo(info, 0);
            BitConverter.GetBytes(w).CopyTo(info, 4);
            BitConverter.GetBytes(-h).CopyTo(info, 8);   // top-down
            BitConverter.GetBytes((short)1).CopyTo(info, 12);
            BitConverter.GetBytes((short)1).CopyTo(info, 14);
            BitConverter.GetBytes(2).CopyTo(info, 32);   // colours used
            info[40] = info[41] = info[42] = 255;        // colour 0: white (B, G, R, 0); colour 1 stays black
            bits = new byte[stride * h];
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var row = new int[w];
                for (int y = 0; y < h; y++)
                {
                    Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, w);
                    for (int x = 0; x < w; x++)
                        if ((row[x] & 0xFFFFFF) != 0xFFFFFF) bits[y * stride + (x >> 3)] |= (byte)(0x80 >> (x & 7));
                }
            }
            finally { bmp.UnlockBits(data); }
        }
    }
}
