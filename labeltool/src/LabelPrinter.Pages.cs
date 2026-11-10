// Several labels in one print job (print from data, PrintJob.cs): one page per label, each bitmap asked for only when
// its page is printed, sent as LabelPrinter.Print sends one (1 bpp, StretchDIBits 1:1). Copies are pages, device
// copies stay 1. Both kinds of page go through one DC with StartDoc, StartPage/EndPage per label and EndDoc: a
// PrintDocument (StandardPrintController) resets the DC for every page, and Unidrv then starts the document again
// (ESC @ and the invalidate bytes between the pages: a stream the printer and compare_prn don't read as one job).
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Runtime.InteropServices;

namespace QlLabels
{
    static partial class LabelPrinter
    {
        [DllImport("kernel32.dll")] static extern IntPtr GlobalLock(IntPtr h);
        [DllImport("kernel32.dll")] static extern bool GlobalUnlock(IntPtr h);
        [DllImport("kernel32.dll")] static extern IntPtr GlobalFree(IntPtr h);

        // pageAt(i): the i-th label as the driver's portrait page (EditorRender.ForPrinter); not disposed here.
        public static void PrintPages(PageSettings page, int count, Func<int, Bitmap> pageAt, string documentName)
        {
            if (count <= 0) return;
            string printer = page.PrinterSettings.PrinterName;
            // the page's DEVMODE (paper, resolution, orientation and the printer's own settings), copies 1
            IntPtr hdm = page.PrinterSettings.GetHdevmode(page);
            try
            {
                IntPtr dm = GlobalLock(hdm);
                if (dm == IntPtr.Zero) throw new InvalidOperationException("GlobalLock failed");
                try
                {
                    Marshal.WriteInt32(dm, OffFields, Marshal.ReadInt32(dm, OffFields) | DM_COPIES);
                    Marshal.WriteInt16(dm, OffCopies, 1);
                    IntPtr dc = CreateDC(null, printer, null, dm);
                    if (dc == IntPtr.Zero) throw new InvalidOperationException("CreateDC failed: " + printer);
                    try { Pages(dc, count, pageAt, 0, documentName); }
                    finally { DeleteDC(dc); }
                }
                finally { GlobalUnlock(hdm); }
            }
            finally { GlobalFree(hdm); }
        }

        // The same on a length of its own (as Print(CustomPage ...)).
        public static void PrintPages(CustomPage page, int count, Func<int, Bitmap> pageAt, int skipX, string documentName)
        {
            if (count <= 0) return;
            WithDc(page, 1, dc => Pages(dc, count, pageAt, skipX, documentName));
        }

        // One document on the DC, one page per label; AbortDoc when a page fails (nothing half printed stays queued).
        static void Pages(IntPtr dc, int count, Func<int, Bitmap> pageAt, int skipX, string documentName)
        {
            var di = new DOCINFO { cbSize = Marshal.SizeOf(typeof(DOCINFO)), lpszDocName = documentName };
            if (StartDoc(dc, ref di) <= 0) throw new InvalidOperationException("StartDoc failed");
            bool done = false;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    var label = pageAt(i);
                    byte[] info, bits;
                    ToDib(label, out info, out bits);
                    if (StartPage(dc) <= 0) throw new InvalidOperationException("StartPage failed");
                    if (StretchDIBits(dc, skipX, 0, label.Width, label.Height, 0, 0, label.Width, label.Height, bits, info, DIB_RGB_COLORS, SRCCOPY) == 0)
                        throw new InvalidOperationException("StretchDIBits failed");
                    if (EndPage(dc) <= 0) throw new InvalidOperationException("EndPage failed");
                }
                if (EndDoc(dc) <= 0) throw new InvalidOperationException("EndDoc failed");
                done = true;
            }
            finally { if (!done) AbortDoc(dc); }
        }
    }
}
