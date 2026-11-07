// One small test label on the roll the printer reports, after asking (Printer setup window, setup window).
// Without a known roll nothing is printed: a label of the wrong size stops the printer until the cover is opened.
using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    static class TestLabel
    {
        static string lastSent;

        // The explanation for the (i) next to the last Print result, or null (a share without a reported roll).
        public static string LastInfo { get; private set; }

        // Was this Print result "test label sent"?
        public static bool WasSent(string result) { return result != null && result == lastSent; }

        // What happened, for the caller's dialog; null when the user cancelled.
        public static string Print(IWin32Window owner, string title, string printer)
        {
            LastInfo = null;
            DeviceResult d;
            try
            {
                d = SetupProgress.Run(owner, title, Lang.F("Asking {0} for the loaded roll...", printer), null, () => QlDevice.Query(printer, true));
            }
            catch (Exception ex) { return Lang.T("Could not ask the printer:") + " " + ex.Message; }
            var roll = d.Reply;
            var media = roll != null ? roll.LoadedMedia : null;
            string host, share;
            if (media == null && (roll == null || !roll.NoMedia) && PublishedStatus.IsShare(QlDevice.PortOf(printer), out host, out share))
            {
                // Added through a share: only the host's status service can report the roll; without it the refusal looked like a failed setup.
                LastInfo = Lang.T("A test label first asks for the loaded roll, so a wrong size doesn't stop the printer. Through a share only the PC " +
                                  "the printer is plugged into can answer, and only with the printer status service.") + "\n\n" +
                           Lang.T("There: \"QL Label Formats\" > Printer > Printer setup > \"Share a printer\" > \"Printer status service for shared printers\". " +
                                  "Then click \"Print test label\" here again.") + "\n\n" +
                           Lang.T("If the service is already on: is the other PC switched on and the printer ready?");
                return Lang.T("The printer works. For a test label, the PC it is plugged into must report the roll.");
            }
            if (media == null)
                return roll != null && roll.NoMedia ? Lang.T("The printer reports no roll: load one and try again.")
                     : d.Error != null ? Lang.F("The printer didn't report its roll ({0}), so nothing was printed. Is it switched on and ready?", d.Error)
                     : Lang.T("The printer didn't report its roll, so nothing was printed. Is it switched on and ready?");
            try
            {
                var doc = new PrintDocument { DocumentName = Lang.T("QL printer setup: test label") };
                doc.PrinterSettings.PrinterName = printer;
                var size = doc.PrinterSettings.PaperSizes.Cast<PaperSize>()
                    .FirstOrDefault(p => string.Equals(p.PaperName, media.Name, StringComparison.OrdinalIgnoreCase));
                if (size == null) return Lang.F("{0} has no paper '{1}' (the loaded roll), so nothing was printed.", printer, media.Name);
                if (QlMessage.Show(owner, Lang.F("Print one test label on the loaded roll ({0}) on {1}?", roll.LoadedText, printer), title,
                                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return null;
                // Continuous tape: a 40 mm label (custom length) instead of the paper's long default.
                doc.DefaultPageSettings.PaperSize = media.Continuous ? new PaperSize("Custom", size.Width, (int)Math.Round(40 / 25.4 * 100)) : size;
                doc.DefaultPageSettings.Landscape = false;
                doc.PrintController = new StandardPrintController();
                string loaded = roll.LoadedText;
                // Drawn like QL Label Formats' test print (the old own drawing came out blank on 40 mm labels: PrintDocument's
                // 1-inch default margins, x64 QL-570 2026-10-28).
                var sample = new LabelFormat { Name = Lang.T("QL test label"), PaperId = media.Id, LengthMm = media.Continuous ? 40 : media.LengthMm };
                doc.PrintPage += (s, e) => DrawSample(e, sample);
                doc.Print();
                lastSent = Lang.F("Test label sent ({0}).", loaded);
                return lastSent;
            }
            catch (Exception ex) { return Lang.T("Printing failed:") + " " + ex.Message; }
        }

        // Border, name, size and a millimetre ruler measured from the label's top edge: QL Label Formats' test print and this
        // test label (one look; in millimetres from the printable area, so margins don't matter).
        internal static void DrawSample(PrintPageEventArgs e, LabelFormat f)
        {
            var g = e.Graphics;
            g.PageUnit = GraphicsUnit.Millimeter;
            RectangleF area = g.VisibleClipBounds;
            float top = e.PageSettings.HardMarginY * 0.254f;  // printable area starts this far from the label's edge
            using (var pen = new Pen(Color.Black, 0.3f))
            {
                g.DrawRectangle(pen, area.X + 0.3f, area.Y + 0.3f, area.Width - 0.6f, area.Height - 0.6f);
                for (int mm = (int)Math.Ceiling(top); mm <= top + area.Height; mm++)
                {
                    float y = area.Y + mm - top;
                    float len = mm % 10 == 0 ? 3f : mm % 5 == 0 ? 2f : 1f;
                    g.DrawLine(pen, area.X + 0.3f, y, area.X + 0.3f + len, y);
                }
            }
            float textX = area.X + 4.5f, textW = area.Width - 6;
            using (var small = new Font("Segoe UI", 5f, FontStyle.Regular, GraphicsUnit.Point))
            {
                for (int mm = 10 * (int)Math.Ceiling(top / 10); mm <= top + area.Height - 2; mm += 10)
                    g.DrawString(mm.ToString(), small, Brushes.Black, area.X + 3.3f, area.Y + mm - top - 1.2f);
            }
            float nameSize = Math.Max(5f, Math.Min(14f, Math.Min(area.Height / 4, textW / Math.Max(4, f.Name.Length) * 1.9f) * 2.83f));
            using (var big = new Font("Segoe UI", nameSize, FontStyle.Bold, GraphicsUnit.Point))
            using (var normal = new Font("Segoe UI", Math.Max(5f, nameSize * 0.6f), FontStyle.Regular, GraphicsUnit.Point))
            {
                var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                float y = area.Y + 1.5f;
                g.DrawString(f.Name, big, Brushes.Black, new RectangleF(textX, y, textW, big.GetHeight(g) * 2), fmt);
                y += g.MeasureString(f.Name, big, (int)textW).Height + 0.5f;
                g.DrawString(LabelStore.Describe(f), normal, Brushes.Black, new RectangleF(textX, y, textW, normal.GetHeight(g) * 2), fmt);
                y += normal.GetHeight(g) + 0.3f;
                g.DrawString(Lang.F("printed {0}", DateTime.Now.ToString("g")), normal, Brushes.Black, new RectangleF(textX, y, textW, normal.GetHeight(g) * 2), fmt);
            }
            e.HasMorePages = false;
        }
    }
}
