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
                case "box": case "shape": o = new ShapeObject(); break;   // "shape": triangle, polygon, star (version 2)
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

        public const double MinPos = -10000, MaxMm = 10000;   // the properties panel's X/Y and W/H ranges (mm): room for a picture
        // pasted 1:1 (a 10000 px bitmap at 96 dpi is 2.6 m, centred on the label at about -1250 mm)
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

    // The layer commands (Arrange menu, context menu).
    enum Layer { ToFront, Forward, Backward, ToBack }

    sealed class LabelDoc
    {
        public const string Extension = ".qllabel";
        // The file format's version ("version"): 1 = the format of 0.18, the base. A later change that older files need
        // converting for raises it, and opening converts files with a lower version. 2 (0.21): new optional keys and
        // types ("box" + "radius", "type":"shape"); version 1 files open unchanged.
        public const int Version = 2;

        public string FormName = "";   // the label format or Brother paper the label was made for
        public int PaperId;            // its Brother paper (to find a replacement if the format is gone)
        public int Length01;           // its length (0.1 mm)
        public bool Across;            // shown and written across the tape (else along the label; LabelLayout)
        public string LengthMode = ""; // "own": a length of its own on the paper's tape (no form), "auto": from the content
        public List<LabelObject> Objects = new List<LabelObject>();
        public int Skipped;            // objects of unknown types not loaded (from a newer version)
        public int FileVersion;        // the opened file's "version" (0: none, a new label)
        // Objects of unknown types (from a newer version), kept as they were and written back on save (0.21 safeguard):
        // Key = how many known objects came before it in the file, so it keeps its place in the stacking order as far
        // as the known objects allow. Never shown, selected or printed.
        List<KeyValuePair<int, Dictionary<string, object>>> unknown = new List<KeyValuePair<int, Dictionary<string, object>>>();

        // Made with a newer QL Label Maker (a higher file version): parts may be missing or shown simplified.
        public bool Newer { get { return FileVersion > Version; } }
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
            c.unknown = new List<KeyValuePair<int, Dictionary<string, object>>>(unknown);
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
            var objects = Objects.Select(o => (object)o.ToJson()).ToList();
            // the unknown objects back at their places (from the end, so the earlier places stay right)
            foreach (var u in Enumerable.Reverse(unknown)) objects.Insert(Math.Min(u.Key, Objects.Count), u.Value);
            d["objects"] = objects;
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(d);
        }

        public static LabelDoc FromJson(string json)
        {
            var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>;
            if (d == null || LabelObject.Str(d, "format", "") != "QL label") throw new InvalidDataException(Lang.T("Not a QL label file."));
            var doc = new LabelDoc();
            doc.extra = new Dictionary<string, object>(d);
            foreach (var k in new[] { "format", "version", "label", "objects" }) doc.extra.Remove(k);
            double version = LabelObject.Num(d, "version", 0);
            doc.FileVersion = double.IsNaN(version) || version < 0 ? 0 : (int)Math.Min(version, int.MaxValue);
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
                    if (o != null) doc.Objects.Add(o);
                    else
                    {
                        doc.Skipped++;
                        if (od != null) doc.unknown.Add(new KeyValuePair<int, Dictionary<string, object>>(doc.Objects.Count, od));
                    }
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
