using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed partial class EditorForm
    {
        // ---------- fit to label ----------

        // Pasted and dragged-in pictures keep their size (1:1). Bigger than the label's printable area, a message bar over
        // the canvas and the panel says so and offers "Fit to label" (as QL Label Formats' bar: what happened, a link, ×).
        // It goes on ×, the next edit, an undo, another label, or after fitting.
        readonly Panel fitBar = new Panel { Dock = DockStyle.Top, Visible = false };
        Label fitBarText;
        List<LabelObject> fitOffered;

        Panel BuildFitBar()
        {
            Theme.Back(fitBar, Role.InfoBack);
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            Theme.Back(row, Role.InfoBack);
            var icon = Theme.Fore(new Label { AutoSize = true, Text = "ⓘ", Margin = new Padding(0, 0, S(6), 0) }, Role.InfoText);
            fitBarText = Theme.Fore(new Label { AutoSize = true, Text = Lang.T("Larger than the label."), Margin = new Padding(0, 0, S(12), 0) }, Role.InfoText);
            var fit = new LinkLabel { Text = Lang.T("Fit to label"), AutoSize = true, Margin = new Padding(0, 0, S(12), 0) };
            Theme.StyleLink(fit);
            fit.LinkClicked += delegate { if (fitOffered != null) FitToLabel(fitOffered); };
            var close = new Label
            {
                Text = "×", AutoSize = false, Dock = DockStyle.Right, Width = S(28), TextAlign = ContentAlignment.MiddleCenter,
                Cursor = Cursors.Hand, AccessibleName = Lang.T("Close"),
            };
            Theme.Fore(close, Role.InfoText);
            tips.SetToolTip(close, Lang.T("Close"));
            close.Click += delegate { HideFitBar(); };
            row.Controls.AddRange(new Control[] { icon, fitBarText, fit });
            fitBar.Controls.Add(close);
            fitBar.Controls.Add(row);
            row.BringToFront();   // fills what the × on the right leaves
            return fitBar;
        }

        // After a paste or drop: the bar when the new objects (pictures only: what "Fit to label" scales) are bigger than
        // the printable area.
        void OfferFit(List<LabelObject> made)
        {
            if (layout == null || made.Count == 0 || !made.All(o => o is ImageObject)) return;
            var box = EditorCanvas.BoxOf(made);
            var p = layout.Printable;
            if (box.Width <= p.Width + 0.05f && box.Height <= p.Height + 0.05f) return;
            fitOffered = made;
            fitBar.Padding = new Padding(S(12), S(7), 0, S(7));
            fitBar.Height = fitBarText.PreferredHeight + fitBar.Padding.Vertical;
            fitBar.Visible = true;
        }

        void HideFitBar()
        {
            fitOffered = null;
            if (fitBar.Visible) fitBar.Visible = false;
        }

        // "Fit to label" in the panel and the object menu: for pictures only (texts, codes and lines don't scale as a group).
        bool CanFit(IList<LabelObject> objects) { return layout != null && objects.Count > 0 && objects.All(o => o is ImageObject); }

        // The objects scaled together, in proportion, to the printable area and centred on it; one undo step.
        void FitToLabel(IList<LabelObject> objects)
        {
            var list = objects.Where(o => doc.Objects.Contains(o)).ToList();
            if (layout == null || list.Count == 0) { HideFitBar(); return; }
            var box = EditorCanvas.BoxOf(list);
            var p = layout.Printable;
            if (box.Width <= 0 || box.Height <= 0) return;
            double k = Math.Min(p.Width / box.Width, p.Height / box.Height);
            double x = p.X + (p.Width - box.Width * k) / 2, y = p.Y + (p.Height - box.Height * k) / 2;
            PushUndo(null);   // also closes the bar
            foreach (var o in list)
            {
                o.X = Math.Round(x + (o.X - box.X) * k, 2); o.Y = Math.Round(y + (o.Y - box.Y) * k, 2);
                o.W = Math.Round(o.W * k, 2); o.H = Math.Round(o.H * k, 2);
            }
            canvas.Select(list);
            ChangedAll();
            canvas.Focus();
        }
    }
}
