// Label editor, one object kind: line: the line object, its settings and the insert command (its width row is in Editor.Shape.cs, shared with boxes).
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

    sealed partial class EditorForm
    {
        void AddLine()
        {
            var at = FreeSpot();
            double len = layout != null ? Math.Min(30, layout.Printable.Width - 2) : 30;
            Add(new LineObject { X = at.X, Y = at.Y + 2, W = Math.Max(5, len), H = 0 });
        }
    }
}
