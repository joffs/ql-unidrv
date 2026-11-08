// Setup window (InstallForm.cs), part: the step list's controls: one step (Step), its state mark (StepMark) and the
// done view's box (DoneBox).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    partial class InstallForm
    {
        // A step: one line of what to do now (Detail); the why and the details behind the (i) next to its title (Tip);
        // Links = the step's own links (picture, report). F7 test 2026-10-30: less text, one line per step.
        sealed class Step
        {
            public readonly StepMark Mark = new StepMark();
            public readonly Label Title = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 0, 0, 0) };
            public readonly Label Detail = Theme.Fore(new Label { AutoSize = true }, Role.Dim);
            public readonly FlowLayoutPanel Head = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            public readonly FlowLayoutPanel Links = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Visible = false };
            // The explanation: the app's own tip at the (i) (InfoTip; user's choice 2026-10-30: no Windows ToolTip,
            // and no text opening inline in the window)
            public Control Info;
            public string Tip;
            public bool Shown = true;  // the step is part of this window (special: ARM64 only); Visible reads false before the window shows
            public string Text = "";    // the detail without the elapsed time
            public DateTime? BusySince;  // the window waits for work outside it (a script, Windows' prompt): ring turns, time counts
        }

        // A step's marker (colours from Brand): done = petrol circle with a white tick, current = petrol ring with an
        // orange dot, busy = a petrol arc turning on a light ring, waiting = grey ring, problem = red circle with "!",
        // skipped = grey ring with a dash. Drawn, so it scales with the font and doesn't depend on a symbol font.
        sealed class StepMark : Control
        {
            StepState state;
            Timer spin;
            int angle;

            public StepMark()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                         ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.Transparent;
            }

            public StepState State { get { return state; } set { state = value; Invalidate(); } }

            // The setup waits for work outside the window (variant A of the progress mockup, 0504d06, user's choice
            // 2026-10-30: the install step looked frozen for 30-40 s)
            public bool Spinning
            {
                get { return spin != null; }
                set
                {
                    if (value == Spinning) return;
                    if (value)
                    {
                        spin = new Timer { Interval = 40 };
                        spin.Tick += delegate { angle = (angle + 12) % 360; Invalidate(); };
                        spin.Start();
                    }
                    else { spin.Dispose(); spin = null; }
                    Invalidate();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && spin != null) { spin.Dispose(); spin = null; }
                base.Dispose(disposing);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float d = Math.Min(Width, Height) - 2, pen = Math.Max(1.5f, d / 10), x = (Width - d) / 2, y = 1;
                var ring = new RectangleF(x + pen / 2, y + pen / 2, d - pen, d - pen);
                if (spin != null)
                {
                    float wide = pen * 1.3f;
                    var r = new RectangleF(x + wide / 2, y + wide / 2, d - wide, d - wide);
                    using (var light = new Pen(Theme.Of(Role.AccentSoft), wide)) g.DrawEllipse(light, r);
                    using (var arc = new Pen(Theme.Of(Role.Accent), wide) { StartCap = LineCap.Round, EndCap = LineCap.Round }) g.DrawArc(arc, r, angle - 90, 100);
                    return;
                }
                switch (state)
                {
                    case StepState.Done:
                        using (var fill = new SolidBrush(Theme.Fixed.StateOk)) g.FillEllipse(fill, x, y, d, d);
                        using (var tick = new Pen(Theme.Fixed.StateSymbol, pen * 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                            g.DrawLines(tick, new[] { new PointF(x + d * 0.28f, y + d * 0.52f), new PointF(x + d * 0.44f, y + d * 0.67f), new PointF(x + d * 0.72f, y + d * 0.36f) });
                        break;
                    case StepState.Working:
                        using (var p = new Pen(Theme.Of(Role.Accent), pen)) g.DrawEllipse(p, ring);
                        using (var dot = new SolidBrush(Theme.Fixed.Orange)) g.FillEllipse(dot, x + d * 0.3f, y + d * 0.3f, d * 0.4f, d * 0.4f);
                        break;
                    case StepState.Problem:
                        using (var fill = new SolidBrush(Theme.Fixed.StateError)) g.FillEllipse(fill, x, y, d, d);
                        using (var mark = new Pen(Theme.Fixed.StateSymbol, pen * 1.2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        {
                            g.DrawLine(mark, x + d / 2, y + d * 0.25f, x + d / 2, y + d * 0.56f);
                            g.DrawLine(mark, x + d / 2, y + d * 0.74f, x + d / 2, y + d * 0.75f);
                        }
                        break;
                    default:
                        using (var p = new Pen(Theme.Fixed.StateUnknown, pen)) g.DrawEllipse(p, ring);
                        if (state == StepState.Skipped)
                            using (var dash = new Pen(Theme.Fixed.StateUnknown, pen) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                                g.DrawLine(dash, x + d * 0.32f, y + d / 2, x + d * 0.68f, y + d / 2);
                        break;
                }
            }
        }

        // The box at the top of the after-restart view: a large done mark, "Done – ..." in bold, a grey line under it; white
        // with a hairline and a petrol bar on the left (F7 test 2026-10-30: that view didn't read as finished).
        sealed class DoneBox : TableLayoutPanel
        {
            public readonly Label Line = Theme.Fore(new Label { AutoSize = true, Margin = new Padding(0, 2, 0, 0) }, Role.Dim);
            readonly Label title = new Label { AutoSize = true, Margin = Padding.Empty };

            // A little larger than the steps' titles; semibold (not Bold), so Brand.FollowFont leaves it alone and it
            // follows the window's font here when the scaling changes
            void TitleFont(Font font)
            {
                title.Font = new Font("Segoe UI Semibold", font.SizeInPoints * 1.15f, FontStyle.Regular, GraphicsUnit.Point);
            }

            protected override void OnFontChanged(EventArgs e)
            {
                base.OnFontChanged(e);
                TitleFont(Font);
            }

            public DoneBox(Font font, int em, int width)
            {
                AutoSize = true;
                ColumnCount = 2;
                Theme.Back(this, Role.Content);
                Padding = new Padding(em * 3 / 4 + Bar(em), em * 3 / 4, em, em * 3 / 4);
                Margin = new Padding(0, 0, 0, em * 5 / 4);
                MinimumSize = new Size(width, 0);
                ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                var mark = new StepMark { State = StepState.Done, Size = new Size(em * 2, em * 2), Margin = new Padding(0, 0, em * 3 / 4, 0), Anchor = AnchorStyles.Left };
                var text = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Left };
                Title = Lang.T("Done – the driver is installed.");
                TitleFont(font);
                int textWidth = width - Padding.Horizontal - mark.Width - mark.Margin.Horizontal;
                title.MaximumSize = Line.MaximumSize = new Size(textWidth, 0);
                text.Controls.Add(title);
                text.Controls.Add(Line);
                Controls.Add(mark, 0, 0);
                Controls.Add(text, 1, 0);
                SetStyle(ControlStyles.ResizeRedraw, true);
            }

            public string Title { set { title.Text = value; } }

            static int Bar(int em) { return Math.Max(3, em / 4); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var line = new Pen(Theme.Of(Role.SurfaceLine))) e.Graphics.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
                using (var bar = new SolidBrush(Theme.Of(Role.Accent))) e.Graphics.FillRectangle(bar, 0, 0, Bar(Font.Height), Height);
            }
        }
    }
}
