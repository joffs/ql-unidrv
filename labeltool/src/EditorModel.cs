// Label editor: the document (a label with text, lines, boxes and images), its .qllabel file (JSON) and undo.
// Coordinates are millimetres on the label as the editor shows it: x along the label (feed direction, left = the
// end that leaves the printer first), y across the tape. X/Y/W/H is an object's box as shown (after rotation).
// Unknown JSON fields are kept, so a file from a newer version survives being opened and saved here.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace QlLabels
{
    abstract class LabelObject
    {
        public double X, Y, W, H;    // box on the label (mm)
        public int Rotation;         // 0, 90, 180, 270 (clockwise): quarter turns, the box swaps width and height
        public double Tilt;          // free rotation on top, -45 to 45 degrees (clockwise) about the box centre
        protected Dictionary<string, object> extra = new Dictionary<string, object>();

        public abstract string Type { get; }
        public virtual bool Resizable { get { return true; } }
        // Free rotation (any angle): text, boxes, pictures. Barcodes and QR codes only in 90° steps (scanners);
        // lines have their two ends.
        public virtual bool CanTilt { get { return true; } }

        public RectangleF Box { get { return new RectangleF((float)X, (float)Y, (float)W, (float)H); } }

        // The angle as one number, -180 to 180 (clockwise).
        public double Angle
        {
            get { double a = Rotation + Tilt; return a > 180 ? a - 360 : a; }
        }

        // Sets the angle: quarter turns plus the tilt (the box centre stays).
        public void SetAngle(double angle)
        {
            angle = ((angle % 360) + 360) % 360;
            int quarter = NormalizeRotation((int)(Math.Round(angle / 90) * 90));
            double tilt = angle - quarter;
            if (tilt > 180) tilt -= 360;
            if (tilt < -180) tilt += 360;
            if (!CanTilt) tilt = 0;
            for (int i = 0; i < 4 && Rotation != quarter; i++) Rotate90();
            Tilt = Math.Round(tilt, 2);
        }

        // The box's corners on the label, turned by the tilt (clockwise from top left).
        public PointF[] Corners
        {
            get
            {
                var b = Box;
                var pts = new[] { new PointF(b.Left, b.Top), new PointF(b.Right, b.Top), new PointF(b.Right, b.Bottom), new PointF(b.Left, b.Bottom) };
                if (Tilt == 0) return pts;
                var c = new PointF(b.X + b.Width / 2, b.Y + b.Height / 2);
                return pts.Select(p => Turn(p, c, Tilt)).ToArray();
            }
        }

        // The space the object takes on the label: its box, or around the tilted box.
        public RectangleF Bounds
        {
            get
            {
                if (Tilt == 0) return Box;
                var p = Corners;
                float l = p.Min(q => q.X), t = p.Min(q => q.Y);
                return new RectangleF(l, t, p.Max(q => q.X) - l, p.Max(q => q.Y) - t);
            }
        }

        // p turned about c by degrees (clockwise on the screen, y down).
        public static PointF Turn(PointF p, PointF c, double degrees)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a), dx = p.X - c.X, dy = p.Y - c.Y;
            return new PointF((float)(c.X + dx * cos - dy * sin), (float)(c.Y + dx * sin + dy * cos));
        }

        public LabelObject Clone()
        {
            var c = (LabelObject)MemberwiseClone();
            c.extra = new Dictionary<string, object>(extra);
            return c;
        }

        public Dictionary<string, object> ToJson()
        {
            var d = new Dictionary<string, object>(extra);
            d["type"] = Type;
            d["x"] = R(X); d["y"] = R(Y); d["w"] = R(W); d["h"] = R(H);
            d["rot"] = Rotation;
            if (Tilt != 0) d["tilt"] = R(Tilt); else d.Remove("tilt");
            Write(d);
            return d;
        }

        protected abstract void Write(Dictionary<string, object> d);
        protected abstract void Read(Dictionary<string, object> d);

        public static LabelObject FromJson(Dictionary<string, object> d)
        {
            LabelObject o;
            switch (Str(d, "type", ""))
            {
                case "text": o = new TextObject(); break;
                case "line": o = new LineObject(); break;
                case "box": o = new BoxObject(); break;
                case "image": o = new ImageObject(); break;
                case "barcode": o = new BarcodeObject(); break;
                default: return null;   // a newer object type: skipped (the document says so when loading)
            }
            o.extra = new Dictionary<string, object>(d);
            foreach (var k in new[] { "type", "x", "y", "w", "h", "rot", "tilt" }) o.extra.Remove(k);
            // Within the properties panel's ranges: a damaged file can't hand the drawing code sizes it can't handle.
            o.X = Limit(Num(d, "x", 0), MinPos, MaxMm); o.Y = Limit(Num(d, "y", 0), MinPos, MaxMm);
            o.W = Limit(Num(d, "w", 10), 0, MaxMm); o.H = Limit(Num(d, "h", 5), 0, MaxMm);
            o.Rotation = NormalizeRotation((int)Num(d, "rot", 0));
            o.Read(d);
            double tilt = Num(d, "tilt", 0);
            if (tilt != 0 && !double.IsNaN(tilt) && !double.IsInfinity(tilt) && o.CanTilt) o.SetAngle(o.Rotation + tilt);
            foreach (var k in o.Keys) o.extra.Remove(k);
            return o;
        }

        protected abstract string[] Keys { get; }

        public const double MinPos = -500, MaxMm = 3000;   // the properties panel's X/Y and W/H ranges (mm)
        static double Limit(double v, double min, double max) { return double.IsNaN(v) ? min : Math.Min(max, Math.Max(min, v)); }

        public static int NormalizeRotation(int r) { r = ((r % 360) + 360) % 360; return r - r % 90; }

        // Rotates by 90° clockwise about the box centre (the box turns too).
        public void Rotate90()
        {
            double cx = X + W / 2, cy = Y + H / 2;
            double w = W; W = H; H = w;
            X = cx - W / 2; Y = cy - H / 2;
            Rotation = NormalizeRotation(Rotation + 90);
        }

        internal static double R(double v) { return Math.Round(v, 2); }
        internal static double Num(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            if (!d.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); }
            catch (Exception) { return fallback; }
        }
        internal static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is string ? (string)v : fallback;
        }
        internal static bool Bool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }
    }

    enum TextAlign { Left, Center, Right }

    // A single-line text: the box always fits the text, the handles make the font larger or smaller. A text frame:
    // the box is set with the handles, the text wraps inside it at its font size (what doesn't fit is cut off).
    enum TextKind { Line, Frame }

    sealed class TextObject : LabelObject
    {
        public string Text = "";
        public string Font = "Segoe UI";
        public double Size = 12;     // points
        public bool Bold, Italic, Underline;
        public TextAlign Align = TextAlign.Left;
        public TextKind Kind = TextKind.Line;
        public double LineHeight = 1;    // frame text: line pitch as a multiple of the font's (0.5-3)
        public double Spacing;           // extra space between characters, points (-5 to +20)
        public bool Invert;              // white on a black box (the box + EditorRender.InvertPad on each side)

        public const double MinLineHeight = 0.5, MaxLineHeight = 3, MinSpacing = -5, MaxSpacing = 20;

        public override string Type { get { return "text"; } }
        protected override string[] Keys { get { return new[] { "text", "font", "size", "bold", "italic", "underline", "align", "kind", "lineHeight", "spacing", "invert" }; } }

        public FontStyle Style
        {
            get { return (Bold ? FontStyle.Bold : 0) | (Italic ? FontStyle.Italic : 0) | (Underline ? FontStyle.Underline : 0); }
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["text"] = Text; d["font"] = Font; d["size"] = R(Size);
            d["bold"] = Bold; d["italic"] = Italic; d["underline"] = Underline;
            d["align"] = Align == TextAlign.Center ? "center" : Align == TextAlign.Right ? "right" : "left";
            d["kind"] = Kind == TextKind.Frame ? "frame" : "line";
            d["lineHeight"] = R(LineHeight);
            d["spacing"] = R(Spacing);
            if (Invert) d["invert"] = true;
        }

        protected override void Read(Dictionary<string, object> d)
        {
            Text = Str(d, "text", ""); Font = Str(d, "font", Font);
            Size = Math.Min(400, Math.Max(2, Num(d, "size", Size)));
            Bold = Bool(d, "bold", false); Italic = Bool(d, "italic", false); Underline = Bool(d, "underline", false);
            string a = Str(d, "align", "left");
            Align = a == "center" ? TextAlign.Center : a == "right" ? TextAlign.Right : TextAlign.Left;
            LineHeight = Math.Min(MaxLineHeight, Math.Max(MinLineHeight, Num(d, "lineHeight", 1)));
            Spacing = Math.Min(MaxSpacing, Math.Max(MinSpacing, Num(d, "spacing", 0)));
            Invert = Bool(d, "invert", false);
            Kind = Str(d, "kind", "line") == "frame" ? TextKind.Frame : TextKind.Line;
        }
    }

    // A straight line from one corner of the box to the opposite one: horizontal (H = 0), vertical (W = 0) or
    // diagonal (Rising: bottom left to top right, else top left to bottom right).
    sealed class LineObject : LabelObject
    {
        public double Thickness = 0.5;  // mm
        public bool Rising;

        public override string Type { get { return "line"; } }
        public override bool CanTilt { get { return false; } }
        protected override string[] Keys { get { return new[] { "thickness", "rising" }; } }

        public PointF Start { get { return Rising ? new PointF((float)X, (float)(Y + H)) : new PointF((float)X, (float)Y); } }
        public PointF End { get { return Rising ? new PointF((float)(X + W), (float)Y) : new PointF((float)(X + W), (float)(Y + H)); } }

        // The panel's angle (0-180°, counter-clockwise from the horizontal; a line has no direction) and length (mm)
        public double LineAngle
        {
            get
            {
                double a = Math.Atan2(-(End.Y - Start.Y), End.X - Start.X) * 180 / Math.PI;
                a = Math.Round(((a % 180) + 180) % 180, 1);
                return a >= 180 ? 0 : a;
            }
        }
        public double Length { get { return Math.Round(Math.Sqrt(W * W + H * H), 2); } }

        // Angle and length typed in the panel: the left end stays where it is (a vertical line's lower end).
        public void SetAngleLength(double angle, double length)
        {
            var a = W == 0 ? new PointF((float)X, (float)(Y + H)) : Start;
            double t = (angle <= 90 ? angle : angle - 180) * Math.PI / 180;
            double bx = a.X + length * Math.Cos(t), by = a.Y - length * Math.Sin(t);
            X = Math.Round(Math.Min(a.X, bx), 2);
            Y = Math.Round(Math.Min(a.Y, by), 2);
            W = Math.Round(Math.Abs(bx - a.X), 2);
            H = Math.Round(Math.Abs(by - a.Y), 2);
            Rising = by < a.Y - 1e-9;   // up to the right
        }

        protected override void Write(Dictionary<string, object> d) { d["thickness"] = R(Thickness); d["rising"] = Rising; }
        protected override void Read(Dictionary<string, object> d)
        {
            Thickness = Math.Min(20, Math.Max(0.1, Num(d, "thickness", Thickness)));
            Rising = Bool(d, "rising", false);
        }
    }

    sealed class BoxObject : LabelObject
    {
        public double Thickness = 0.5;  // mm (outline)
        public bool Filled;
        public bool Ellipse;

        public override string Type { get { return "box"; } }
        protected override string[] Keys { get { return new[] { "thickness", "filled", "ellipse" }; } }

        protected override void Write(Dictionary<string, object> d) { d["thickness"] = R(Thickness); d["filled"] = Filled; d["ellipse"] = Ellipse; }
        protected override void Read(Dictionary<string, object> d)
        {
            Thickness = Math.Min(20, Math.Max(0.1, Num(d, "thickness", Thickness)));
            Filled = Bool(d, "filled", false); Ellipse = Bool(d, "ellipse", false);
        }
    }

    enum ImageMode { Threshold, Dither }

    sealed class ImageObject : LabelObject
    {
        byte[] png;                     // the original picture (as inserted, at most MaxPixels per side)
        public byte[] Svg;              // an SVG picture as a vector (SvgPicture, cleaned): drawn at the size printed; Png is its preview
        public ImageMode Mode = ImageMode.Dither;
        public int Threshold = 128;     // 1-254: darker than this prints black (Threshold mode)
        public bool Invert;

        public const int MaxPixels = 2000;
        Bitmap picture;                 // decoded Png (cache)
        bool unreadable;                // Png is no picture (a damaged file): drawn as nothing

        // A copy for another thread: it decodes the picture itself (a Bitmap must not be used by two threads at once).
        public ImageObject Detached()
        {
            var c = (ImageObject)Clone();
            c.picture = null;
            return c;
        }

        // A new picture (Replace picture) is decoded again; the old one stayed in the cache.
        public byte[] Png
        {
            get { return png; }
            set { if (value != png) { png = value; picture = null; unreadable = false; } }
        }

        public override string Type { get { return "image"; } }
        protected override string[] Keys { get { return new[] { "png", "svg", "mode", "threshold", "invert" }; } }

        public Bitmap Picture
        {
            get
            {
                if (picture == null && png != null && !unreadable)
                    try
                    {
                        using (var ms = new MemoryStream(png))
                        using (var decoded = new Bitmap(ms))
                            picture = new Bitmap(decoded);   // a copy: a Bitmap read from a stream needs the stream open
                    }
                    catch (ArgumentException) { unreadable = true; }
                return picture;
            }
        }

        // Reads a picture file (HEIC and a PDF's first page too: PictureFile); large pictures are scaled down (a label
        // needs at most ~1300 dots).
        public static ImageObject FromFile(string path)
        {
            byte[] svg = SvgPicture.IsSvg(path) ? SvgPicture.Read(path) : null;
            using (var src = svg != null ? SvgPreview(svg) : PictureFile.Load(path))
            {
                double k = Math.Min(1.0, (double)MaxPixels / Math.Max(src.Width, src.Height));
                int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
                using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.White);   // transparent parts print white
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.DrawImage(src, 0, 0, w, h);
                    }
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        return new ImageObject { Png = ms.ToArray(), Svg = svg, W = w, H = h };
                    }
                }
            }
        }

        // An SVG's preview, MaxPixels on its longer side (the editor's draft and the picture without Direct2D).
        static Bitmap SvgPreview(byte[] svg)
        {
            var size = SvgPicture.Size(svg);
            double k = MaxPixels / Math.Max(size.Width, size.Height);
            var bmp = SvgPicture.Render(svg, Math.Max(1, (int)Math.Round(size.Width * k)), Math.Max(1, (int)Math.Round(size.Height * k)));
            if (bmp == null) throw new InvalidDataException(Lang.T("Windows can't draw this SVG picture."));
            return bmp;
        }

        protected override void Write(Dictionary<string, object> d)
        {
            d["png"] = Png != null ? Convert.ToBase64String(Png) : "";
            if (Svg != null) d["svg"] = Encoding.UTF8.GetString(Svg);
            d["mode"] = Mode == ImageMode.Threshold ? "threshold" : "dither";
            d["threshold"] = Threshold; d["invert"] = Invert;
        }

        protected override void Read(Dictionary<string, object> d)
        {
            string png = Str(d, "png", "");
            try { Png = png.Length > 0 ? Convert.FromBase64String(png) : null; } catch (FormatException) { Png = null; }
            string svg = Str(d, "svg", "");
            try { Svg = svg.Length > 0 ? SvgPicture.Clean(Encoding.UTF8.GetBytes(svg)) : null; }   // cleaned again: the file may come from elsewhere
            catch (InvalidDataException) { Svg = null; }   // drawn from the png
            Mode = Str(d, "mode", "dither") == "threshold" ? ImageMode.Threshold : ImageMode.Dither;
            Threshold = (int)Math.Min(254, Math.Max(1, Num(d, "threshold", 128)));
            Invert = Bool(d, "invert", false);
        }
    }

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

    // Fields in texts and barcodes, filled in when the label is drawn or printed:
    //   {date} today, {date+30} / {date-1} days from today, {time} now; a format after a colon: {date:yyyy-MM-dd},
    //   {date+7:dd.MM.}, {time:HH:mm:ss} (.NET date formats). Without a format: the short date / time of the user's
    //   Windows settings. {{ and }} are literal braces. Anything else in braces stays as it is.
    static class Fields
    {
        static readonly System.Text.RegularExpressions.Regex Field =
            new System.Text.RegularExpressions.Regex(@"\{\{|\}\}|\{(date|time)([+-]\d{1,5})?(?::([^{}]+))?\}");

        public static string Expand(string text, DateTime now)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0 && text.IndexOf('}') < 0) return text ?? "";
            return Field.Replace(text, m =>
            {
                if (m.Value == "{{") return "{";
                if (m.Value == "}}") return "}";
                bool date = m.Groups[1].Value == "date";
                string format = m.Groups[3].Success ? m.Groups[3].Value : date ? "d" : "t";
                try
                {
                    var t = m.Groups[2].Success ? now.AddDays(int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : now;
                    return t.ToString(format, CultureInfo.CurrentCulture);
                }
                catch (FormatException) { return m.Value; }
                catch (ArgumentOutOfRangeException) { return m.Value; }
            });
        }

        public static bool HasFields(string text) { return text != null && Expand(text, new DateTime(2000, 1, 1)) != text; }

        // Applies f to the text between the fields (escaping a QR code's content must leave the fields as they are).
        public static string MapText(string text, Func<string, string> f)
        {
            var b = new StringBuilder();
            int at = 0;
            foreach (System.Text.RegularExpressions.Match m in Field.Matches(text ?? ""))
            {
                b.Append(f(text.Substring(at, m.Index - at))).Append(m.Value);
                at = m.Index + m.Length;
            }
            return b.Append(f((text ?? "").Substring(at))).ToString();
        }
    }

    // The layer commands (Arrange menu, context menu).
    enum Layer { ToFront, Forward, Backward, ToBack }

    sealed class LabelDoc
    {
        public const string Extension = ".qllabel";
        // The file format's version ("version"): 1 = the format of 0.18, the base. A later change that older files need
        // converting for raises it, and opening converts files with a lower version.
        public const int Version = 1;

        public string FormName = "";   // the label format or Brother paper the label was made for
        public int PaperId;            // its Brother paper (to find a replacement if the format is gone)
        public int Length01;           // its length (0.1 mm)
        public bool Across;            // shown and written across the tape (else along the label; LabelLayout)
        public string LengthMode = ""; // "own": a length of its own on the paper's tape (no form), "auto": from the content
        public List<LabelObject> Objects = new List<LabelObject>();
        public int Skipped;            // objects of unknown types not loaded (from a newer version)
        Dictionary<string, object> extra = new Dictionary<string, object>();

        // Stacking order = list order: a later object covers the earlier ones. The given objects move together and keep
        // their order among themselves; "one layer" passes the next object that isn't one of them.
        public bool CanRestack(ICollection<LabelObject> these, Layer how)
        {
            bool up = how == Layer.ToFront || how == Layer.Forward, seen = false;
            foreach (var o in up ? Objects : Enumerable.Reverse(Objects))
            {
                if (these.Contains(o)) seen = true;
                else if (seen) return true;
            }
            return false;
        }

        public bool Restack(ICollection<LabelObject> these, Layer how)
        {
            if (!CanRestack(these, how)) return false;
            var mine = Objects.Where(these.Contains).ToList();
            switch (how)
            {
                case Layer.ToFront: Objects.RemoveAll(these.Contains); Objects.AddRange(mine); break;
                case Layer.ToBack: Objects.RemoveAll(these.Contains); Objects.InsertRange(0, mine); break;
                case Layer.Forward:
                    for (int i = Objects.Count - 2; i >= 0; i--)
                        if (these.Contains(Objects[i]) && !these.Contains(Objects[i + 1])) Swap(i, i + 1);
                    break;
                case Layer.Backward:
                    for (int i = 1; i < Objects.Count; i++)
                        if (these.Contains(Objects[i]) && !these.Contains(Objects[i - 1])) Swap(i, i - 1);
                    break;
            }
            return true;
        }

        void Swap(int a, int b) { var o = Objects[a]; Objects[a] = Objects[b]; Objects[b] = o; }

        public LabelDoc Clone()
        {
            var c = (LabelDoc)MemberwiseClone();
            c.Objects = Objects.Select(o => o.Clone()).ToList();
            c.extra = new Dictionary<string, object>(extra);
            return c;
        }

        public string ToJson()
        {
            var d = new Dictionary<string, object>(extra);
            d["format"] = "QL label";
            d["version"] = Version;
            var label = new Dictionary<string, object> { { "form", FormName }, { "paperId", PaperId }, { "length01", Length01 }, { "view", Across ? "across" : "along" } };
            if (LengthMode.Length > 0) label["length"] = LengthMode;
            d["label"] = label;
            d["objects"] = Objects.Select(o => o.ToJson()).ToList();
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(d);
        }

        public static LabelDoc FromJson(string json)
        {
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || LabelObject.Str(d, "format", "") != "QL label") throw new InvalidDataException(Lang.T("Not a QL label file."));
            var doc = new LabelDoc();
            doc.extra = new Dictionary<string, object>(d);
            foreach (var k in new[] { "format", "version", "label", "objects" }) doc.extra.Remove(k);
            object v;
            var label = d.TryGetValue("label", out v) ? v as Dictionary<string, object> : null;
            if (label != null)
            {
                doc.FormName = LabelObject.Str(label, "form", "");
                doc.PaperId = (int)LabelObject.Num(label, "paperId", 0);
                doc.Length01 = (int)LabelObject.Num(label, "length01", 0);
                doc.Across = LabelObject.Str(label, "view", "along") == "across";
                doc.LengthMode = LabelObject.Str(label, "length", "");
                if (doc.LengthMode != "own" && doc.LengthMode != "auto") doc.LengthMode = "";
            }
            var list = d.TryGetValue("objects", out v) ? v as IEnumerable : null;
            if (list != null)
                foreach (var item in list)
                {
                    var od = item as Dictionary<string, object>;
                    var o = od != null ? LabelObject.FromJson(od) : null;
                    if (o != null) doc.Objects.Add(o); else doc.Skipped++;
                }
            return doc;
        }

        public static LabelDoc Load(string path) { return FromJson(File.ReadAllText(path, Encoding.UTF8)); }
        public void Save(string path) { LabelStore.WriteAtomic(path, ToJson()); }
    }

    // Undo/redo as document snapshots (JSON keeps it simple and exact; images are shared byte arrays in Clone).
    sealed class UndoStack
    {
        readonly List<LabelDoc> undo = new List<LabelDoc>(), redo = new List<LabelDoc>();
        const int Max = 100;

        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }

        // Call before a change, with the document as it is now.
        public void Push(LabelDoc before)
        {
            undo.Add(before.Clone());
            if (undo.Count > Max) undo.RemoveAt(0);
            redo.Clear();
        }

        public LabelDoc Undo(LabelDoc current)
        {
            if (!CanUndo) return current;
            redo.Add(current.Clone());
            var d = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            return d;
        }

        public LabelDoc Redo(LabelDoc current)
        {
            if (!CanRedo) return current;
            undo.Add(current.Clone());
            var d = redo[redo.Count - 1];
            redo.RemoveAt(redo.Count - 1);
            return d;
        }

        public void Clear() { undo.Clear(); redo.Clear(); }
    }
}
