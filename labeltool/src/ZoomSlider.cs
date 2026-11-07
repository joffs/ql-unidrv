// Label editor: the zoom slider in the status bar (Office style). 25 % to 800 %, evenly spaced in ratio; ticks at
// 100 % (real size) and at the dot-for-dot stops (petrol), which the slider snaps to nearby.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class ZoomSlider : Control
    {
        public const double Min = 25, Max = 800;
        double value = 100;
        double[] stops = new double[0];
        bool dragging;

        public event EventHandler Scrolled;   // moved by the user (Value is the new zoom)

        public ZoomSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
        }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(Min, Math.Min(Max, value)); Invalidate(); }
        }

        // The dot-for-dot zooms (they depend on the screen's scaling).
        public double[] Stops
        {
            get { return stops; }
            set { stops = value ?? new double[0]; Invalidate(); }
        }

        float Inset { get { return LogicalToDeviceUnits(5); } }
        float X(double v) { return Inset + (float)((Width - 2 * Inset) * Math.Log(v / Min) / Math.Log(Max / Min)); }
        double V(float x)
        {
            double t = Math.Max(0, Math.Min(1, (x - Inset) / Math.Max(1, Width - 2 * Inset)));
            return Min * Math.Pow(Max / Min, t);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float cy = Height / 2f, tick = LogicalToDeviceUnits(4);
            using (var track = new Pen(Theme.Of(Role.SliderTrack), Math.Max(1, LogicalToDeviceUnits(1))))
                g.DrawLine(track, Inset, cy, Width - Inset, cy);
            using (var real = new Pen(Theme.Of(Role.SliderTick), Math.Max(1, LogicalToDeviceUnits(1))))
                g.DrawLine(real, X(100), cy - tick, X(100), cy + tick);
            using (var sharp = new Pen(Theme.Of(Role.Accent), Math.Max(1, LogicalToDeviceUnits(1))))
                foreach (var s in stops)
                    if (s >= Min && s <= Max) g.DrawLine(sharp, X(s), cy - tick, X(s), cy + tick);
            float w = LogicalToDeviceUnits(4), h = Math.Min(Height - 2, LogicalToDeviceUnits(12));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var thumb = new SolidBrush(Enabled ? Theme.Of(Role.Accent) : Theme.Of(Role.Dim)))
                g.FillRectangle(thumb, X(value) - w / 2, cy - h / 2, w, h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            Capture = true;
            Follow(e.X);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Follow(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            dragging = false;
            Capture = false;
        }

        // To the mouse; near 100 % or a dot-for-dot stop exactly there; else whole percent.
        void Follow(int x)
        {
            double v = Math.Round(V(x));
            float snap = LogicalToDeviceUnits(5);
            foreach (var s in new[] { 100.0 }.Concat(stops))
                if (Math.Abs(X(s) - x) <= snap) { v = s; break; }
            if (v == value) return;
            Value = v;
            if (Scrolled != null) Scrolled(this, EventArgs.Empty);
        }
    }
}
