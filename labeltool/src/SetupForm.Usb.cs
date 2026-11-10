// Printer setup window (SetupForm.cs), part: the lines for connected USB printers without a printer of ours, kept
// current by WM_DEVICECHANGE, and "Add printer" for them.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QlLabels
{
    partial class SetupForm
    {
        // The lines for connected USB printers without a printer of ours: printer symbol, text (Brother's own printer on
        // the same port in grey below), "Add printer" on the right
        internal void ShowUsbRows(List<UsbCandidate> candidates)
        {
            int em = Font.Height;
            usbRows.SuspendLayout();
            foreach (Control c in usbRows.Controls.Cast<Control>().ToList()) c.Dispose();
            usbRows.Controls.Clear();
            usbTexts.Clear();
            // One line for all of them (two lines made the window too tall at 150 %; user's choice 2026-11-06): symbol |
            // text + (i) | button on the list's right edge; with several printers the button opens a menu of them
            if (candidates.Count > 0)
            {
                var row = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, em / 4, 0, 0), Padding = Padding.Empty };
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                var icon = Theme.Fore(new Label { Text = "\uE749", AutoSize = true, Font = UiFont.Create("Segoe MDL2 Assets", Font.SizeInPoints * 1.15f), Anchor = AnchorStyles.Left,
                                                  Margin = new Padding(0, 2, em / 4, 0) }, Role.Text);
                var text = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, em / 4, 0) };
                var first = candidates[0];
                var label = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = Padding.Empty,
                                        Text = candidates.Count == 1 ? Lang.F("{0} on {1} is not set up here yet.", first.Model.Name, first.Port)
                                                                     : Lang.F("{0} QL printers on USB are not set up here yet.", candidates.Count) };
                var info = StatusForm.InfoIcon(em);
                info.Cursor = Cursors.Hand;
                info.Anchor = AnchorStyles.Left;
                string details = UsbDetails(candidates);
                InfoTip.Attach(info, () => details, hover: true);
                text.Controls.Add(label);
                text.Controls.Add(info);
                var add = Action(Lang.T("Add printer"), () => { });
                add.Anchor = AnchorStyles.Right;
                add.Margin = Padding.Empty;
                if (candidates.Count == 1) add.Click += delegate { AddUsbPrinter(first); };
                else
                {
                    var menu = new ContextMenuStrip();
                    foreach (var c in candidates) { var cand = c; menu.Items.Add(c.Model.Name + " (" + c.Port + ")", null, delegate { AddUsbPrinter(cand); }); }
                    add.Click += delegate { menu.Show(add, new Point(0, add.Height)); };
                    add.Disposed += delegate { menu.Dispose(); };
                }
                row.Controls.Add(icon, 0, 0);
                row.Controls.Add(text, 1, 0);
                row.Controls.Add(add, 2, 0);
                usbRows.Controls.Add(row);
                usbTexts.Add(new Control[] { text, icon, add });
            }
            usbRows.Visible = candidates.Count > 0;
            usbRows.ResumeLayout();
            FitUsbRows(printerList.Width);
        }

        // The text wraps so that the line is as wide as the list (the button on its right edge)
        void FitUsbRows(int width)
        {
            foreach (var t in usbTexts)
            {
                // the symbol follows the text's size (a font set on a control isn't scaled at a DPI change)
                float size = t[0].Controls[0].Font.SizeInPoints * 1.15f;
                if (Math.Abs(t[1].Font.SizeInPoints - size) > 0.1f) t[1].Font = UiFont.Create("Segoe MDL2 Assets", size);
                var info = t[0].Controls[1];
                int room = Math.Max(Font.Height * 10, width - t[1].PreferredSize.Width - t[1].Margin.Horizontal - t[2].PreferredSize.Width - t[0].Margin.Horizontal
                                                      - info.Width - info.Margin.Horizontal);
                t[0].Controls[0].MaximumSize = new Size(room, 0);
                t[0].Parent.MinimumSize = new Size(width, 0);
            }
        }

        // The USB line's (i): why the printer isn't there, what "Add printer" does, Brother's own printers beside them
        internal static string UsbDetails(List<UsbCandidate> candidates)
        {
            var lines = new List<string>();
            if (candidates.Count > 1) lines.Add(string.Join(", ", candidates.Select(c => c.Model.Name + " (" + c.Port + ")")));
            lines.Add(Lang.T("Windows doesn't set up a removed USB printer again by itself, not even after a restart. \"Add printer\" adds it with this driver, not shared."));
            foreach (var c in candidates.Where(c => c.BrotherQueue != null))
                lines.Add(Lang.F("Brother's own printer \"{0}\" stays alongside it.", c.BrotherQueue));
            return string.Join("\r\n\r\n", lines);
        }

        // A QL printer plugged in, unplugged or switched off while the window is open: the USB line follows (Windows'
        // WM_DEVICECHANGE / DBT_DEVNODES_CHANGED to every top-level window; one look a second after the last one)
        Timer usbLook;
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg != 0x0219 || (long)m.WParam != 0x0007 || loading) return;
            if (usbLook == null)
            {
                usbLook = new Timer { Interval = 1000 };
                usbLook.Tick += delegate { usbLook.Stop(); RefreshUsbRows(); };
                Disposed += delegate { usbLook.Dispose(); };
            }
            usbLook.Stop();
            usbLook.Start();
        }

        void RefreshUsbRows()
        {
            if (IsDisposed || Drivers().Length == 0) return;
            ShowUsbRows(UsbPrinters.Unset());
            FitHeight();
        }

        void AddUsbPrinter(UsbCandidate c)
        {
            // Unplugged or switched off since the line appeared: no printer for a device that isn't there
            if (!UsbPrinters.Unset().Any(u => u.Port == c.Port))
            {
                Say(Lang.F("{0} on {1} is no longer connected.", c.Model.Name, c.Port));
                RefreshUsbRows();
                return;
            }
            var r = RunSettings("-UsbPrinter -UsbPort " + c.Port + " -UsbDriverName \"" + c.Model.DriverName + "\"", Lang.T("Adding the printer..."));
            if (r != null) PrinterAdded(r);
        }
    }
}
