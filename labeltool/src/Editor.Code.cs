// Label editor, one object kind: barcode and QR code: the object, encoding and drawing, quiet zone and hints, the settings rows and insert commands.
using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System;

namespace QlLabels
{
    enum Symbology { Code128, Ean13, Qr, Gs1_128 }   // the file's names: BarcodeObject.KindNames

    // A barcode: Code 128, GS1-128, EAN-13 or QR code. Data may contain fields ({date}, Fields); a Code 128 also
    // control characters as tokens ({tab}, {gs}, {fnc1}, Code128.Expand), a GS1-128 its elements as "(AI)data". The symbol is drawn as large
    // as fits the box in whole printer dots per module (EditorRender.BarcodeBitmap); the box is the code itself, the
    // quiet zone lies around it (EditorRender.QuietZone).
    sealed class BarcodeObject : LabelObject
    {
        public Symbology Kind = Symbology.Code128;
        public string Data = "ABC-123";
        public bool ShowText = true;    // Code 128 / EAN-13: the digits or text under the bars
        public int Level = 1;           // QR error correction 0-3 = L, M, Q, H
        // QR code: what it holds. Other than Text, Data is built from QrFields (QrContent.Build) and kept up to date
        // (SetQrField), so the file holds the code as printed. QrFields is replaced, never changed in place (Clone).
        public QrType QrType = QrType.Text;
        public Dictionary<string, string> QrFields = new Dictionary<string, string>();
        // QR code only: white modules on black, the quiet zone black too (scanners of linear codes read dark on light only).
        public bool Invert;

        static readonly string[] KindNames = { "code128", "ean13", "qr", "gs1-128" };

        public override string Type { get { return "barcode"; } }
        public override bool CanTilt { get { return false; } }
        protected override string[] Keys { get { return new[] { "symbology", "data", "showText", "level", "qrType", "qrFields", "invert" }; } }

        public bool TypedQr { get { return Kind == Symbology.Qr && QrType != QrType.Text; } }

        public void SetQrField(string key, string value)
        {
            QrFields = new Dictionary<string, string>(QrFields);
            QrFields[key] = value;
            if (TypedQr) Data = QrContent.Build(QrType, QrFields);
        }

        public void SetQrType(QrType type)
        {
            if (type == QrType) return;
            // A web address typed as text moves into the address field.
            if (type == QrType.Url && QrType == QrType.Text && Data.Trim().Length > 0 && !QrFields.ContainsKey("url"))
                QrFields = new Dictionary<string, string>(QrFields) { { "url", Data.Trim() } };
            QrType = type;
            if (TypedQr) Data = QrContent.Build(QrType, QrFields);
        }

        // The content now (fields filled in; a Code 128's control character tokens too).
        public string Content(DateTime now) { return Fields.Expand(Kind == Symbology.Code128 ? Code128.Expand(Data) : Data, now); }

        // What the bars of a Code 128 or GS1-128 encode: a GS1-128 as Code 128 with FNC1 (null when its elements are wrong).
        public string Encoded(string content)
        {
            string error;
            return Kind == Symbology.Gs1_128 ? Code128.Gs1(content, out error) : content;
        }

        // The text under the bars: a GS1-128 with its brackets, a Code 128 without control characters.
        public string Caption(string content) { return Kind == Symbology.Code128 ? Code128.HumanReadable(content) : content; }

        // null when it can be printed, else why not.
        public string Problem(DateTime now)
        {
            if (TypedQr) { string missing = QrContent.Missing(QrType, QrFields); if (missing != null) return missing; }
            string c = Content(now);
            switch (Kind)
            {
                case Symbology.Ean13: return Ean13.Check(c);
                case Symbology.Qr: return QrCode.Check(c, Level);
                case Symbology.Gs1_128:
                {
                    string error, raw = Code128.Gs1(c, out error);
                    return raw == null ? error : Code128.Check(raw);
                }
                default: return Code128.Check(c);
            }
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["symbology"] = KindNames[(int)Kind]; d["data"] = Data; d["showText"] = ShowText; d["level"] = "LMQH"[Level].ToString();
            if (Invert && Kind == Symbology.Qr) d["invert"] = true;
            if (TypedQr)
            {
                d["qrType"] = QrContent.Names[(int)QrType];
                d["qrFields"] = QrContent.FieldsOf(QrType).Where(k => QrFields.ContainsKey(k)).ToDictionary(k => k, k => (object)QrFields[k]);
            }
        }

        protected override void Read(Dictionary<string, object> d)
        {
            int k = Array.IndexOf(KindNames, Str(d, "symbology", "code128"));
            Kind = k < 0 ? Symbology.Code128 : (Symbology)k;
            Data = Str(d, "data", "");
            ShowText = Bool(d, "showText", true);
            Invert = Kind == Symbology.Qr && Bool(d, "invert", false);
            int l = "LMQH".IndexOf(Str(d, "level", "M"));
            Level = l < 0 ? 1 : l;
            int t = Array.IndexOf(QrContent.Names, Str(d, "qrType", "text"));
            QrType = t < 0 ? QrType.Text : (QrType)t;
            object fields;
            QrFields = new Dictionary<string, string>();
            if (d.TryGetValue("qrFields", out fields) && fields is Dictionary<string, object>)
                foreach (var kv in (Dictionary<string, object>)fields)
                    if (kv.Value is string) QrFields[kv.Key] = (string)kv.Value; else if (kv.Value is bool) QrFields[kv.Key] = (bool)kv.Value ? "true" : "false";
            // Data as saved: the code prints the same even if a newer editor built it differently.
        }
    }

    static partial class EditorRender
    {
        // colours: fixed begin - the label as it prints (white paper, black print), as in EditorRender.cs
        // ---------- barcodes ----------
        // The box is the code itself; the quiet zone scanners need lies around it (upright, in modules): QR code 4 on
        // every side, Code 128 10 left and right, EAN-13 11 left and 7 right. It isn't printed: other objects may reach
        // into it (QuietZoneIntruders finds them, the editor warns).
        static void QuietModules(BarcodeObject b, out int left, out int top, out int right, out int bottom)
        {
            if (b.Kind == Symbology.Qr) { left = top = right = bottom = 4; return; }
            top = bottom = 0;
            left = b.Kind == Symbology.Ean13 ? 11 : 10;
            right = b.Kind == Symbology.Ean13 ? 7 : 10;
        }

        // Dots per module (across, along the bars) for the upright code in a w x h dot box, and its modules across.
        static bool Modules(BarcodeObject b, int w, int h, int dpiW, int dpiH, DateTime now, out int mx, out int my, out int n)
        {
            mx = my = n = 0;
            if (w < 1 || h < 1 || b.Problem(now) != null) return false;
            string content = b.Content(now);
            if (b.Kind == Symbology.Qr)
            {
                n = QrCode.Encode(content, b.Level).GetLength(0);
                QrModule(n, w, h, dpiW, dpiH, out mx, out my);
                return true;
            }
            bool[] guards;
            n = b.Kind == Symbology.Ean13 ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
            mx = Math.Max(1, w / n);
            return true;
        }

        // As large as the box allows in whole dots; square modules also at 300 x 600 dpi.
        static void QrModule(int total, int w, int h, int dpiW, int dpiH, out int mx, out int my)
        {
            double moduleMm = Math.Min(w * 25.4 / dpiW, h * 25.4 / dpiH) / total;
            mx = Math.Max(1, (int)(moduleMm * dpiW / 25.4 + 1e-6));
            my = Math.Max(1, (int)Math.Round(mx * (double)dpiH / dpiW));
            if (my * total > h) { my = Math.Max(1, h / total); mx = Math.Max(1, (int)Math.Round(my * (double)dpiW / dpiH)); }
        }

        // The code with its quiet zone, in the printable area's dots (the box when the content can't be encoded).
        public static Rectangle QuietDots(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            int mx, my, n, l, t, rt, bt;
            if (!Modules(b, turned ? r.Height : r.Width, turned ? r.Width : r.Height, turned ? layout.DpiY : layout.DpiX, turned ? layout.DpiX : layout.DpiY,
                         now, out mx, out my, out n)) return r;
            QuietModules(b, out l, out t, out rt, out bt);
            // upright left, top, right, bottom in dots, then turned with the code (clockwise)
            var q = new[] { l * mx, t * my, rt * mx, bt * my };
            int shift = b.Rotation / 90;
            var s = new int[4];
            for (int i = 0; i < 4; i++) s[(i + shift) % 4] = q[i];
            return Rectangle.FromLTRB(r.Left - s[0], r.Top - s[1], r.Right + s[2], r.Bottom + s[3]);
        }

        // The same in mm on the label.
        public static RectangleF QuietZone(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            var q = QuietDots(b, layout, now);
            float kx = (float)(25.4 / layout.DpiX), ky = (float)(25.4 / layout.DpiY);
            return new RectangleF(layout.Printable.X + q.X * kx, layout.Printable.Y + q.Y * ky, q.Width * kx, q.Height * ky);
        }

        // Codes with something printed in their quiet zone, and the topmost object there: each code's zone is checked on
        // the label drawn without that code.
        public static List<KeyValuePair<BarcodeObject, LabelObject>> QuietZoneIntruders(LabelDoc doc, LabelLayout layout, DateTime now)
        {
            var found = new List<KeyValuePair<BarcodeObject, LabelObject>>();
            foreach (var code in doc.Objects.OfType<BarcodeObject>().ToList())
            {
                var zone = QuietDots(code, layout, now);
                var box = DotRect(code, layout);
                if (zone == box) continue;
                var others = doc.Objects.Where(o => o != code).ToList();
                if (!InkIn(others, doc.Across, layout, zone, box, now)) continue;
                // which one: the topmost whose own dots are there
                var mm = QuietZone(code, layout, now);
                var who = others.Where(o => o.Bounds.IntersectsWith(mm)).Reverse()
                                .FirstOrDefault(o => InkIn(new[] { o }, doc.Across, layout, zone, box, now));
                found.Add(new KeyValuePair<BarcodeObject, LabelObject>(code, who));
            }
            return found;
        }

        // Black dots of these objects in the zone, outside the code's box. Only the zone is drawn, with the objects near it
        // (the whole label for each code made a drop with a big photo pause).
        static bool InkIn(IEnumerable<LabelObject> objects, bool across, LabelLayout layout, Rectangle zone, Rectangle box, DateTime now)
        {
            zone.Intersect(new Rectangle(0, 0, layout.DotsX, layout.DotsY));
            if (zone.Width <= 0 || zone.Height <= 0) return false;
            float kx = (float)(25.4 / layout.DpiX), ky = (float)(25.4 / layout.DpiY);
            var mm = new RectangleF(layout.Printable.X + zone.X * kx, layout.Printable.Y + zone.Y * ky, zone.Width * kx, zone.Height * ky);
            var near = objects.Where(o => RectangleF.Inflate(o.Bounds, Reach, Reach).IntersectsWith(mm)).ToList();
            if (near.Count == 0) return false;
            // the same dots as in the whole label: its layout moved by whole dots (DotsX/DotsY kept: Workable sizes as there)
            var l = layout.Copy();
            l.Printable = mm;
            using (var bmp = new Bitmap(zone.Width, zone.Height, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.SmoothingMode = SmoothingMode.None;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.ScaleTransform(1 / kx, 1 / ky);
                    g.TranslateTransform(-mm.X, -mm.Y);
                    foreach (var o in near)
                        try { Draw(g, o, l, now); }
                        catch (Exception) { }
                }
                Binarize(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var row = new int[bmp.Width];
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width);
                        for (int x = 0; x < bmp.Width; x++)
                            if ((row[x] & 0xFFFFFF) == 0 && !box.Contains(x + zone.X, y + zone.Y)) return true;
                    }
                }
                finally { bmp.UnlockBits(data); }
            }
            return false;
        }

        static void DrawBarcode(Graphics g, BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (b.W <= 0 || b.H <= 0) return;
            var r = QuietDots(b, layout, now);   // drawn with its quiet zone (transparent: it covers nothing)
            if (r.Width < 1 || r.Height < 1) return;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            // The symbol upright: its own width/height in dots and dots per inch (fewer for a giant box: Workable).
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            using (var bmp = BarcodeBitmap(b, w, h, dpiW, dpiH, now))
            {
                if (bmp == null) return;
                bmp.RotateFlip(b.Rotation == 90 ? RotateFlipType.Rotate90FlipNone : b.Rotation == 180 ? RotateFlipType.Rotate180FlipNone
                               : b.Rotation == 270 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone);
                var state = g.Save();
                try
                {
                    g.ResetTransform();
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(bmp, r, 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel);
                }
                finally { g.Restore(state); }
            }
        }

        // The symbol upright in a w x h dot box (the code and its quiet zones), centred, each module a whole number of
        // dots; the code white and black, the quiet zones transparent (an EAN-13's first digit stands in its left one).
        // null when the content can't be encoded (BarcodeObject.Problem says why).
        public static Bitmap BarcodeBitmap(BarcodeObject b, int w, int h, int dpiW, int dpiH, DateTime now)
        {
            if (b.Problem(now) != null) return null;
            string content = b.Content(now);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            bmp.SetResolution(dpiW, dpiH);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.None;
                g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;
                if (b.Kind == Symbology.Qr)
                {
                    if (b.Invert) g.Clear(Color.Black);   // the quiet zone too: the black box around the code
                    DrawQr(g, QrCode.Encode(content, b.Level), w, h, dpiW, dpiH, b.Invert);
                }
                else DrawLinear(g, b, content, w, h, dpiW, dpiH);
            }
            return bmp;
        }

        static void DrawQr(Graphics g, bool[,] m, int w, int h, int dpiW, int dpiH, bool invert)
        {
            int n = m.GetLength(0), total = n + 8;   // 4 modules quiet zone on each side
            int mx, my;
            QrModule(total, w, h, dpiW, dpiH, out mx, out my);
            int x0 = (w - mx * total) / 2 + 4 * mx, y0 = (h - my * total) / 2 + 4 * my;
            g.FillRectangle(invert ? Brushes.Black : Brushes.White, x0, y0, n * mx, n * my);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    if (m[y, x]) g.FillRectangle(invert ? Brushes.White : Brushes.Black, x0 + x * mx, y0 + y * my, mx, my);
        }

        static void DrawLinear(Graphics g, BarcodeObject b, string content, int w, int h, int dpiW, int dpiH)
        {
            bool ean = b.Kind == Symbology.Ean13;
            bool[] guards = null;
            var bars = ean ? Ean13.Modules(content, out guards) : Code128.Modules(b.Encoded(content));
            int left = ean ? 11 : 10, right = ean ? 7 : 10, total = left + bars.Length + right;
            int mx = Math.Max(1, w / total);
            int x0 = (w - mx * total) / 2 + left * mx;
            g.FillRectangle(Brushes.White, x0, 0, bars.Length * mx, h);
            // Text under the bars: about 1/5 of the height, 1.6-3.5 mm.
            double hMm = h * 25.4 / dpiH;
            double textMm = b.ShowText ? Math.Max(1.6, Math.Min(3.5, hMm * 0.2)) : 0;
            int textDots = (int)Math.Round(textMm * dpiH / 25.4);
            int barH = Math.Max(1, h - textDots - (b.ShowText ? (int)(0.3 * dpiH / 25.4) : 0));
            int guardH = ean && b.ShowText ? Math.Min(h, barH + textDots / 2) : barH;
            for (int i = 0; i < bars.Length; i++)
                if (bars[i]) g.FillRectangle(Brushes.Black, x0 + i * mx, 0, mx, guards != null && guards[i] ? guardH : barH);
            if (!b.ShowText) return;
            double sizeMm = CaptionMm(b, content, mx, bars.Length, textMm, dpiW);
            if (sizeMm <= 0) return;   // too narrow even at the smallest size: the panel says so (CaptionFits)
            // Text in mm units with the symbol's dots per inch (also right at 300 x 600).
            g.ScaleTransform((float)(dpiW / 25.4), (float)(dpiH / 25.4));
            float k = (float)(25.4 / dpiW);
            using (var font = new Font("Segoe UI", (float)sizeMm, FontStyle.Regular, GraphicsUnit.World))
            using (var fmt = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center })
            {
                // The baseline lies one ascent below the top: the digits (no descenders) end just above the bottom
                // (from the top of the text band they reached a little below the edge and lost their last dots).
                float ascent = font.Size * font.FontFamily.GetCellAscent(FontStyle.Regular) / font.FontFamily.GetEmHeight(FontStyle.Regular);
                float top = (float)(hMm - textMm * 0.06 - 1.5 * 25.4 / dpiH) - ascent;   // round digits reach a little below the baseline
                fmt.FormatFlags |= StringFormatFlags.NoClip;
                if (ean)
                {
                    string d = Ean13.Full(content);
                    var first = new RectangleF((x0 - left * mx) * k, top, left * mx * k, (float)textMm);
                    g.DrawString(d.Substring(0, 1), font, Brushes.Black, first, fmt);
                    g.DrawString(d.Substring(1, 6), font, Brushes.Black, new RectangleF((x0 + 3 * mx) * k, top, 42 * mx * k, (float)textMm), fmt);
                    g.DrawString(d.Substring(7, 6), font, Brushes.Black, new RectangleF((x0 + 50 * mx) * k, top, 42 * mx * k, (float)textMm), fmt);
                }
                else g.DrawString(b.Caption(content), font, Brushes.Black, new RectangleF(x0 * k, top, bars.Length * mx * k, (float)textMm), fmt);
            }
        }

        // The text under the bars: its size (mm) so that it fits the width it has, an EAN-13's six digits between the
        // guard bars, else the bars' width (wider text was wrapped out of its band and vanished); at most the band's
        // size, 0 when it doesn't fit even at MinCaptionMm.
        const double MinCaptionMm = 1.3;

        static double CaptionMm(BarcodeObject b, string content, int mx, int bars, double textMm, int dpiW)
        {
            bool ean = b.Kind == Symbology.Ean13;
            string text = ean ? "000000" : b.Caption(content);
            if (text.Length == 0) return textMm * 0.95;
            double perMm;   // the text's width per mm of font size
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new Font("Segoe UI", 100f, FontStyle.Regular, GraphicsUnit.World))
                perMm = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width / 100.0;
            double room = (ean ? 42 : bars) * mx * 25.4 / dpiW;
            double size = Math.Min(textMm * 0.95, room / perMm);
            return size < MinCaptionMm ? 0 : size;
        }

        // false when a code's text under the bars doesn't fit its box at all (the panel's hint): as DrawBarcode sizes it
        public static bool CaptionFits(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (!b.ShowText || b.Kind == Symbology.Qr || b.W <= 0 || b.H <= 0 || b.Problem(now) != null) return true;
            var r = QuietDots(b, layout, now);
            if (r.Width < 1 || r.Height < 1) return true;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            string content = b.Content(now);
            bool ean = b.Kind == Symbology.Ean13;
            bool[] guards;
            int bars = ean ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
            int mx = Math.Max(1, w / ((ean ? 18 : 20) + bars));
            double textMm = Math.Max(1.6, Math.Min(3.5, h * 25.4 / dpiH * 0.2));
            return CaptionMm(b, content, mx, bars, textMm, dpiW) > 0;
        }

        // What is wrong with a code (null: nothing): BarcodeObject.Problem (it can't be encoded: it prints blank), or a box
        // so small that the code is cut off: a QR code smaller than one dot per module, a linear code narrower than its
        // bars at one dot each even with both quiet zones (below that it is drawn at one dot, centred over its quiet zones,
        // and cut at their edge; measured on the printer 2026-11-01). Printing asks first (EditorForm.ConfirmProblems).
        public static string Problem(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            string problem = b.Problem(now);
            if (problem != null || layout == null || b.W <= 0 || b.H <= 0) return problem;
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            int w = turned ? r.Height : r.Width, h = turned ? r.Width : r.Height;
            int dpiW = turned ? layout.DpiY : layout.DpiX, dpiH = turned ? layout.DpiX : layout.DpiY;
            string content = b.Content(now);
            if (b.Kind == Symbology.Qr)
            {
                int n = QrCode.Encode(content, b.Level).GetLength(0);
                double needMm = Math.Max(n * 25.4 / dpiW, n * 25.4 / dpiH);
                return w * 25.4 / dpiW < needMm - 1e-6 || h * 25.4 / dpiH < needMm - 1e-6 ? Lang.F("Too small: at least {0} mm.", Media.Mm(Math.Ceiling(needMm * 10) / 10)) : null;
            }
            int bars, l, t, rt, bt;
            LinearWidth(b, layout, now, out w, out bars, out dpiW);
            QuietModules(b, out l, out t, out rt, out bt);
            return w + l + rt < bars ? Lang.F("Too narrow: at least {0} mm.", Media.Mm(Math.Ceiling(bars * 25.4 / dpiW * 10) / 10)) : null;
        }

        // A linear code's box width (upright, in dots), its bars (modules) and the dots per inch across it.
        static void LinearWidth(BarcodeObject b, LabelLayout layout, DateTime now, out int w, out int bars, out int dpiW)
        {
            var r = DotRect(b, layout);
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            w = turned ? r.Height : r.Width;
            dpiW = turned ? layout.DpiY : layout.DpiX;
            bool[] guards;
            string content = b.Content(now);
            bars = b.Kind == Symbology.Ean13 ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
        }

        // A code's narrowest bar (a QR code's module) as printed, mm, as DrawBarcode sizes it; 0 when it can't be encoded.
        public static double ModuleMm(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (b.W <= 0 || b.H <= 0 || b.Problem(now) != null) return 0;
            var r = QuietDots(b, layout, now);
            if (r.Width < 1 || r.Height < 1) return 0;
            bool turned = b.Rotation == 90 || b.Rotation == 270;
            var s = Workable(r, layout);
            double k = (double)s.Width / r.Width;
            int w = turned ? s.Height : s.Width, h = turned ? s.Width : s.Height;
            int dpiW = Math.Max(1, (int)Math.Round((turned ? layout.DpiY : layout.DpiX) * k)), dpiH = Math.Max(1, (int)Math.Round((turned ? layout.DpiX : layout.DpiY) * k));
            string content = b.Content(now);
            int mx, my;
            if (b.Kind == Symbology.Qr) QrModule(QrCode.Encode(content, b.Level).GetLength(0) + 8, w, h, dpiW, dpiH, out mx, out my);
            else
            {
                bool ean = b.Kind == Symbology.Ean13;
                bool[] guards;
                int bars = ean ? Ean13.Modules(content, out guards).Length : Code128.Modules(b.Encoded(content)).Length;
                mx = Math.Max(1, w / ((ean ? 18 : 20) + bars));
            }
            return mx * 25.4 / dpiW;
        }

        // The panel's warnings about a code that prints but may not scan (null: none): partly outside the printable
        // area, bars narrower than 3 dots (an EAN-13 below 80 % of its nominal 0.33 mm), the text under the bars too wide.
        public static string BarcodeHint(BarcodeObject b, LabelLayout layout, DateTime now)
        {
            if (Problem(b, layout, now) != null) return null;
            var lines = new List<string>();
            if (b.Kind != Symbology.Qr)
            {
                int w, bars, dpiW;
                LinearWidth(b, layout, now, out w, out bars, out dpiW);
                if (w < bars) lines.Add(Lang.F("The code is wider than its frame – better at least {0} mm.", Media.Mm(Math.Ceiling(bars * 25.4 / dpiW * 10) / 10)));
            }
            const float t = 0.01f;
            var bounds = b.Bounds;
            var p = layout.Printable;
            if (bounds.IntersectsWith(p) && (bounds.Left < p.Left - t || bounds.Top < p.Top - t || bounds.Right > p.Right + t || bounds.Bottom > p.Bottom + t))
                lines.Add(Lang.T("The code lies partly outside the printable area – it gets cut off."));
            double module = ModuleMm(b, layout, now);
            double least = b.Kind == Symbology.Ean13 ? 0.33 * 0.8 : 3 * 25.4 / Math.Max(layout.DpiX, layout.DpiY) - 1e-6;
            if (module > 0 && module < least)
            {
                string mm = module.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + " mm";   // kept on one line
                lines.Add(b.Kind == Symbology.Qr ? Lang.F("Dots very small ({0}) – make the code larger.", mm)
                                                 : Lang.F("Bars very narrow ({0}) – make the code wider.", mm));
            }
            if (!CaptionFits(b, layout, now)) lines.Add(Lang.T("The text under the bars doesn't fit at this size – make the code wider."));
            return lines.Count > 0 ? string.Join("\n", lines) : null;
        }
        // colours: fixed end
    }

    sealed partial class EditorForm
    {
        // The settings rows of a barcode or QR code (BuildProperties).
        void BuildCodeRows()
        {
            // Barcodes (Code 128, GS1-128, EAN-13) and QR codes are separate tools; the type switches between the barcodes only.
            kindBox = Combo("Code 128", "GS1-128", "EAN-13");
            kindBox.SelectedIndexChanged += (s, e) => Edit(s, o => SetKind((BarcodeObject)o, LinearKinds[kindBox.SelectedIndex]));
            Row(Lang.T("Type:"), kindBox, o => o is BarcodeObject && ((BarcodeObject)o).Kind != Symbology.Qr);
            Func<LabelObject, bool> qr = o => o is BarcodeObject && ((BarcodeObject)o).Kind == Symbology.Qr,
                plain = o => o is BarcodeObject && !((BarcodeObject)o).TypedQr;
            qrTypeBox = Combo(Lang.T("Text"), Lang.T("Web address"), Lang.T("Contact"), Lang.T("Wi-Fi"), Lang.T("E-mail"), Lang.T("Phone number"), "SMS");
            qrTypeBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrType((QrType)qrTypeBox.SelectedIndex));
            Row(Lang.T("Contains:"), qrTypeBox, qr);
            tips.SetToolTip(qrTypeBox, Lang.T("What a phone does with the code: open a web page, save a contact, join a Wi-Fi network,\n" +
                                              "write an e-mail, call or send an SMS. Date fields such as {date} work in every field."));
            dataBox = new TextBox { Dock = DockStyle.Fill };
            dataBox.TextChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).Data = dataBox.Text);
            Row(Lang.T("Content:"), dataBox, plain);
            Row("", FieldButton(dataBox), plain);
            // How to write control characters (Code 128) or the elements (GS1-128): one line, the rest behind the (i)
            notationLabel = Theme.Fore(new Label { AutoSize = true, Margin = Padding.Empty }, Role.Dim);
            var notationIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(notationIcon, () =>
            {
                var b = canvas.Selected as BarcodeObject;
                return b != null && b.Kind == Symbology.Gs1_128
                    ? Lang.T("Each element starts with its application identifier (AI) in brackets, e.g. (01) GTIN, 14 digits; (17) best before, YYMMDD; " +
                             "(10) batch; (21) serial number. The brackets are printed under the bars but not encoded; the FNC1 separators are added automatically.")
                    : Lang.T("Typed like the date fields: {tab} tab, {cr} carriage return, {lf} line feed, {gs} group separator, {rs} record separator, " +
                             "{eot} end of transmission, {fnc1} FNC1, {xNN} any ASCII character by its hex code (00-7F). They are encoded but not printed under the bars.");
            });
            var notationRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            notationRow.Controls.AddRange(new Control[] { notationLabel, notationIcon });
            Row("", notationRow, o => { var b = o as BarcodeObject; return b != null && (b.Kind == Symbology.Code128 || b.Kind == Symbology.Gs1_128); });
            Action<string, string, Control> field = (key, caption, editor) =>
            {
                qrEditors[key] = editor;
                Row(caption, editor, o => { var b = o as BarcodeObject; return b != null && b.TypedQr && QrContent.FieldsOf(b.QrType).Contains(key); });
            };
            Func<string, bool, TextBox> qrBox = (key, multi) =>
            {
                var t = new TextBox { Dock = DockStyle.Fill, Multiline = multi, AcceptsReturn = multi, ScrollBars = multi ? ScrollBars.Vertical : ScrollBars.None };
                if (multi) t.Height = S(48);
                t.TextChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField(key, t.Text));
                return t;
            };
            field("url", Lang.T("Web address:"), qrBox("url", false));
            field("firstName", Lang.T("First name:"), qrBox("firstName", false));
            field("lastName", Lang.T("Last name:"), qrBox("lastName", false));
            field("company", Lang.T("Company:"), qrBox("company", false));
            field("phone", Lang.T("Phone:"), qrBox("phone", false));
            field("email", Lang.T("E-mail:"), qrBox("email", false));
            field("street", Lang.T("Street:"), qrBox("street", false));
            field("city", Lang.T("Postcode, city:"), qrBox("city", false));
            field("website", Lang.T("Website:"), qrBox("website", false));
            field("ssid", Lang.T("Network name:"), qrBox("ssid", false));
            field("password", Lang.T("Password:"), qrBox("password", false));
            var security = Combo("WPA/WPA2/WPA3", "WEP", Lang.T("None (open network)"));
            security.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField("security", QrContent.Security[security.SelectedIndex]));
            field("security", Lang.T("Encryption:"), security);
            CheckBox hidden = null;
            hidden = Check(Lang.T("Hidden network"), (s, e) => Edit(s, o => ((BarcodeObject)o).SetQrField("hidden", hidden.Checked ? "true" : "false")));
            field("hidden", "", hidden);
            field("to", Lang.T("To:"), qrBox("to", false));
            field("subject", Lang.T("Subject:"), qrBox("subject", false));
            field("body", Lang.T("Message:"), qrBox("body", true));
            field("number", Lang.T("Number:"), qrBox("number", false));
            field("message", Lang.T("Message:"), qrBox("message", true));
            problemLabel = new Label { AutoSize = true, MaximumSize = new Size(S(160), 0) };
            // Only while there is something to say (an empty row would leave a gap).
            Row("", problemLabel, o =>
            {
                var b = o as BarcodeObject;
                return b != null && (EditorRender.Problem(b, layout, DateTime.Now) != null || Fields.HasFields(b.Data) || (layout != null && EditorRender.BarcodeHint(b, layout, DateTime.Now) != null));
            });
            // The light margin around a selected code, explained; a warning when something reaches into it.
            quietLabel = new Label { AutoSize = true, MaximumSize = new Size(S(230), 0), Margin = new Padding(0, 0, 0, 0) };
            var quietIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(quietIcon, () => Lang.T("Scanners need a clear margin around the code to find where it starts and ends (the quiet zone): " +
                                                   "4 modules all round for a QR code, 10 modules left and right for Code 128, 11 left and 7 right for EAN-13. " +
                                                   "It isn't printed; the canvas shows it only while the code is selected."));
            quietRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, S(2), 0, S(6)) };
            quietRow.Controls.AddRange(new Control[] { quietLabel, quietIcon });
            SpanRow(quietRow, o => { var b = o as BarcodeObject; return b != null && b.Problem(DateTime.Now) == null; });
            showTextCheck = Check(Lang.T("Text under the bars"), (s, e) => Edit(s, o => ((BarcodeObject)o).ShowText = showTextCheck.Checked));
            Row("", showTextCheck, o => o is BarcodeObject && ((BarcodeObject)o).Kind != Symbology.Qr);
            levelBox = Combo(Lang.T("L – 7 % (smallest)"), Lang.T("M – 15 % (usual)"), Lang.T("Q – 25 %"), Lang.T("H – 30 % (most robust)"));
            levelBox.DropDownWidth = S(200);
            levelBox.SelectedIndexChanged += (s, e) => Edit(s, o => ((BarcodeObject)o).Level = levelBox.SelectedIndex);
            Row(Lang.T("Error correction:"), levelBox, o => o is BarcodeObject && ((BarcodeObject)o).Kind == Symbology.Qr);
            // Inverted only for QR codes: the scanners of Code 128 and EAN-13 read dark on light only (no checkbox there).
            qrInvertCheck = Check(Lang.T("Invert"), (s, e) => Edit(s, o => ((BarcodeObject)o).Invert = qrInvertCheck.Checked));
            var qrInvertIcon = StatusForm.InfoIcon(Font.Height);
            InfoTip.Attach(qrInvertIcon, () => Lang.T("White on black: the standard allows it. Phone cameras usually read it, older or simple scanners often don't. Test it if in doubt."));
            var qrInvertRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            qrInvertRow.Controls.AddRange(new Control[] { qrInvertCheck, qrInvertIcon });
            Row("", qrInvertRow, qr);
        }

        void AddBarcode()
        {
            var at = FreeSpot();
            Add(new BarcodeObject { X = at.X, Y = at.Y, W = 32, H = 12 });   // with its quiet zone about 40 mm
            FocusField(dataBox);
        }

        // A QR code starts as a web address (the panel's "Contains" offers the other kinds).
        void AddQrCode()
        {
            var at = FreeSpot();
            // the code itself; its quiet zone (4 modules on each side) comes around it
            double side = layout != null ? Math.Max(8, Math.Min(16, (Math.Min(layout.Printable.Width, layout.Printable.Height) - 2) * 0.75)) : 16;
            var q = new BarcodeObject { X = at.X, Y = at.Y, W = side, H = side, Kind = Symbology.Qr };
            q.SetQrType(QrType.Url);
            q.SetQrField("url", "example.org");
            Add(q);
            FocusField((TextBox)qrEditors["url"]);
        }

        // Code 128 <-> GS1-128 <-> EAN-13: an EAN-13 needs digits and a GS1-128 elements, so a content that doesn't fit
        // becomes an example.
        static void SetKind(BarcodeObject b, Symbology kind)
        {
            if (b.Kind == kind) return;
            b.Kind = kind;
            string error;
            if (kind == Symbology.Ean13 && Ean13.Check(b.Data) != null) b.Data = "0000000000000";   // neutral (a real number would be someone's product)
            if (kind == Symbology.Gs1_128 && Code128.Gs1(b.Data, out error) == null) b.Data = "(01)00000000000000(10)ABC123";
        }
    }
}
