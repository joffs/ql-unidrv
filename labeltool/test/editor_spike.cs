// Label editor spike: prints a test bitmap through LabelPrinter on a capture
// printer, so tools\prn_raster.py can check that the print is the bitmap dot for dot. Run by editor_spike.ps1.
//   editor_spike <printer> <paper name> <300|600> <portrait|landscape> <expected.pbm>
// The pattern is drawn as the editor will show a label: length left to right. Portrait: the editor turns it 90°
// clockwise itself; landscape: the page is sent as it is and the plug-in turns it. expected.pbm = the label as it
// must come out (portrait, feed downwards), for both.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;

namespace QlLabels.Tests
{
    static class EditorSpike
    {
        static int Main(string[] args)
        {
            if (args.Length != 5) { Console.WriteLine("usage: editor_spike <printer> <paper> <300|600> <portrait|landscape> <expected.pbm>"); return 2; }
            bool landscape = args[3] == "landscape";
            var page = LabelPrinter.Settings(args[0], args[1], int.Parse(args[2]), landscape);
            var geo = LabelPrinter.Measure(page);
            Console.WriteLine("measured:  " + geo);
            // The editor's canvas: length (feed) horizontal, tape width vertical.
            int length = landscape ? geo.Width : geo.Height, across = landscape ? geo.Height : geo.Width;
            using (var canvas = Pattern(length, across, landscape ? geo.DpiX : geo.DpiY, landscape ? geo.DpiY : geo.DpiX))
            using (var portrait = (Bitmap)canvas.Clone())
            {
                portrait.RotateFlip(RotateFlipType.Rotate90FlipNone);
                WritePbm(args[4], portrait);
                LabelPrinter.Print(page, landscape ? canvas : portrait, "QL label editor spike", 1,
                                   g => Console.WriteLine("in job:    " + g));
            }
            return 0;
        }

        // Frame on the outermost dots, 1-dot grid every 50 dots, a 1-dot checkerboard, solid block, text and an
        // arrow pointing along the feed (shows mirroring and rotation). dpiLen/dpiAcross: dots per inch along each axis.
        static Bitmap Pattern(int length, int across, int dpiLen, int dpiAcross)
        {
            var bmp = new Bitmap(length, across);
            bmp.SetResolution(dpiLen, dpiAcross);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                for (int x = 50; x < length; x += 50) for (int y = 0; y < across; y += 2) bmp.SetPixel(x, y, Color.Black);
                for (int y = 50; y < across; y += 50) for (int x = 0; x < length; x += 2) bmp.SetPixel(x, y, Color.Black);
                for (int x = 0; x < length; x++) { bmp.SetPixel(x, 0, Color.Black); bmp.SetPixel(x, across - 1, Color.Black); }
                for (int y = 0; y < across; y++) { bmp.SetPixel(0, y, Color.Black); bmp.SetPixel(length - 1, y, Color.Black); }
                for (int y = 10; y < 50 && y < across - 1; y++)
                    for (int x = 10; x < 50 && x < length - 1; x++)
                        if (((x + y) & 1) == 0) bmp.SetPixel(x, y, Color.Black);
                g.FillRectangle(Brushes.Black, 60, 10, 20, 30);
                // Arrow along the feed, near the start, upper half only (asymmetric in both axes).
                g.FillPolygon(Brushes.Black, new[] { new Point(90, 15), new Point(130, 25), new Point(90, 35) });
                // Font size in pixels: the spike checks dots, not text size (300 x 600 needs its own scaling in the editor).
                using (var f = new Font("Segoe UI", 24f, FontStyle.Bold, GraphicsUnit.Pixel))
                    g.DrawString("QL editor spike ABC 123", f, Brushes.Black, 140, 12);
            }
            return bmp;
        }

        static void WritePbm(string path, Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height, stride = (w + 7) / 8;
            using (var fs = File.Create(path))
            {
                var head = System.Text.Encoding.ASCII.GetBytes("P4\n" + w + " " + h + "\n");
                fs.Write(head, 0, head.Length);
                var row = new byte[stride];
                for (int y = 0; y < h; y++)
                {
                    Array.Clear(row, 0, stride);
                    for (int x = 0; x < w; x++)
                        if ((bmp.GetPixel(x, y).ToArgb() & 0xFFFFFF) != 0xFFFFFF) row[x >> 3] |= (byte)(0x80 >> (x & 7));
                    fs.Write(row, 0, stride);
                }
            }
        }
    }
}
