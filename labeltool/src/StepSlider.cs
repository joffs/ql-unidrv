// A small slider in the look of the editor's zoom slider (ZoomSlider.cs), with a few fixed steps: a tick per step, the
// thumb snaps to them. Drag or click, or the mouse wheel over it (2026-11-09). Used for the card size in
// "Print with data" (PrintDataDialog.cs).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class StepSlider : Control
    {
        int steps = 5, value;
        bool dragging;

        public event EventHandler Scrolled;   // moved by the user (Value is the new step)

        public StepSlider()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            BackColor = Color.Transparent;
        }

        public int Steps { get { return steps; } set { steps = Math.Max(2, value); Value = this.value; } }

        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(steps - 1, value)); Invalidate(); }
        }

        float Inset { get { return LogicalToDeviceUnits(5); } }
        float X(int step) { return Inset + (Width - 2 * Inset) * step / (float)(steps - 1); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float cy = Height / 2f, tick = LogicalToDeviceUnits(3);
            using (var track = new Pen(Theme.Of(Role.SliderTrack), Math.Max(1, LogicalToDeviceUnits(1))))
                g.DrawLine(track, Inset, cy, Width - Inset, cy);
            using (var t = new Pen(Theme.Of(Role.SliderTick), Math.Max(1, LogicalToDeviceUnits(1))))
                for (int i = 0; i < steps; i++) g.DrawLine(t, X(i), cy - tick, X(i), cy + tick);
            float w = LogicalToDeviceUnits(4), h = Math.Min(Height - 2, LogicalToDeviceUnits(12));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var thumb = new SolidBrush(Enabled ? Theme.Of(Role.Accent) : Theme.Of(Role.Dim)))
                g.FillRectangle(thumb, X(value) - w / 2, cy - h / 2, w, h);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !Enabled) return;
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

        // The wheel over the slider: one step per notch (up = larger).
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Enabled && e.Delta != 0) StepBy(e.Delta > 0 ? 1 : -1);
        }

        public void StepBy(int by) { Set(value + by); }

        void Follow(int x)
        {
            int nearest = (int)Math.Round((x - Inset) / Math.Max(1f, Width - 2 * Inset) * (steps - 1));
            Set(nearest);
        }

        void Set(int v)
        {
            v = Math.Max(0, Math.Min(steps - 1, v));
            if (v == value) return;
            Value = v;
            if (Scrolled != null) Scrolled(this, EventArgs.Empty);
        }
    }
}
