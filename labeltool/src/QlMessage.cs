// Our message box in place of Windows' MessageBox (which stays light in dark mode): the result dialog's look
// (SetupResult: a round state mark, a headline, the text, the buttons at the right), in the window's scheme (Theme),
// and MessageBox's behaviour:
// - Enter = the default button; Esc and the window's x = Cancel, else No (Esc means No on Yes/No), else OK;
// - J/N (Y/N) answer without Alt, as MessageBox's underlined letters; Tab and the arrows move between the buttons;
// - Ctrl+C copies the message as text in MessageBox's layout (title, message, buttons);
// - Windows' sounds per kind (Information, Warning, Error, Question); none while QL_TEST_SILENT=1 (UI tests on deskrun's
//   desktop: sound isn't separated by desktops, the user hears it);
// - details (e.g. Windows' error text) behind "Show details" / "Copy details";
// - at most ~690 px high at any scaling: a long text scrolls.
//
// QlMessage.Show takes MessageBox.Show's arguments and splits the text itself (Split): a question in it becomes the
// headline, else its first sentence; the first paragraph of the rest in the text colour, the others dimmed; a headline
// that ends in ":" (error + Windows' message) puts the rest behind Show details for errors. QlMessage.Ask takes the
// headline, text and details as they are.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace QlLabels
{
    static class QlMessage
    {
        // No sounds (tests): set here, or QL_TEST_SILENT=1 in the environment (deskrun sets it for every test it runs)
        public static bool Silent = Environment.GetEnvironmentVariable("QL_TEST_SILENT") == "1";

        // As MessageBox.Show(owner, text, caption, buttons, icon, defaultButton)
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK,
                                        MessageBoxIcon icon = MessageBoxIcon.None, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
        {
            var parts = Split(text, icon == MessageBoxIcon.Error);
            return Ask(owner, title, parts.Headline, parts.Text, buttons, icon, defaultButton, parts.Details);
        }

        // Without an owner window (the tray monitor): as MessageBox.Show(text, caption, buttons, icon)
        public static DialogResult Show(string text, string title, MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None)
        {
            return Show(null, text, title, buttons, icon);
        }

        // headline: one sentence (the question, or what happened); text: paragraphs split by a blank line, null for none;
        // details: shown with Show details, null for none.
        public static DialogResult Ask(IWin32Window owner, string title, string headline, string text, MessageBoxButtons buttons,
                                       MessageBoxIcon icon, MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1, string details = null)
        {
            using (var box = new MessageForm(title, headline, text, buttons, icon, defaultButton, details))
            {
                if (owner == null) box.StartPosition = FormStartPosition.CenterScreen;
                return box.ShowDialog(owner);
            }
        }

        // ---- the text ----

        public sealed class Parts { public string Headline, Text, Details; }

        // "e.g. DK-..." isn't the end of a sentence
        static readonly string[] Abbreviations = { "e.g.", "i.e.", "etc.", "z. B.", "z.B.", "d. h.", "usw.", "ca.", "Nr.", "bzw." };

        // The sentences of a paragraph: after . ? ! or : followed by a space or a line break; a line break ends one too
        internal static List<string> Sentences(string paragraph)
        {
            var list = new List<string>();
            int start = 0;
            for (int i = 0; i < paragraph.Length; i++)
            {
                char c = paragraph[i];
                bool lineEnd = c == '\n';
                bool end = lineEnd || ((c == '.' || c == '?' || c == '!' || c == ':') && (i + 1 == paragraph.Length || paragraph[i + 1] == ' ' || paragraph[i + 1] == '\n'));
                if (end && !lineEnd && c == '.' && Abbreviations.Any(a => i + 1 >= a.Length && string.CompareOrdinal(paragraph, i + 1 - a.Length, a, 0, a.Length) == 0))
                    end = false;
                if (!end) continue;
                string s = paragraph.Substring(start, (lineEnd ? i : i + 1) - start).Trim();
                if (s.Length > 0) list.Add(s);
                start = i + 1;
            }
            string rest = paragraph.Substring(start).Trim();
            if (rest.Length > 0) list.Add(rest);
            return list;
        }

        // A MessageBox text as headline, text and details (see the top of this file)
        public static Parts Split(string message, bool error)
        {
            var paragraphs = Regex.Split((message ?? "").Replace("\r\n", "\n").Trim(), @"\n\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var parts = new Parts { Headline = "" };
            if (paragraphs.Count == 0) return parts;
            // The question, if there is one: the headline; the rest of its paragraph stays where it was
            for (int p = 0; p < paragraphs.Count; p++)
            {
                var sentences = Sentences(paragraphs[p]);
                int q = sentences.FindIndex(s => s.EndsWith("?"));
                if (q < 0) continue;
                parts.Headline = sentences[q];
                sentences.RemoveAt(q);
                if (sentences.Count > 0) paragraphs[p] = Join(sentences); else paragraphs.RemoveAt(p);
                parts.Text = paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : null;
                return parts;
            }
            // Else the first sentence; one ending in ":" introduces the rest (for an error: Windows' text, the details)
            var first = Sentences(paragraphs[0]);
            parts.Headline = first[0];
            first.RemoveAt(0);
            if (first.Count > 0) paragraphs[0] = Join(first); else paragraphs.RemoveAt(0);
            string rest = paragraphs.Count > 0 ? string.Join("\n\n", paragraphs) : null;
            if (parts.Headline.EndsWith(":"))
            {
                parts.Headline = parts.Headline.Substring(0, parts.Headline.Length - 1).TrimEnd() + ".";
                if (error && rest != null) { parts.Details = rest; rest = null; }
            }
            parts.Text = rest;
            return parts;
        }

        // Sentences of one paragraph again; one that was on a line of its own (ends without a stop) keeps its line
        static string Join(List<string> sentences)
        {
            var b = new StringBuilder();
            foreach (var s in sentences)
            {
                if (b.Length > 0) b.Append(".?!:".IndexOf(b[b.Length - 1]) >= 0 ? " " : "\n");
                b.Append(s);
            }
            return b.ToString();
        }

        // ---- the window ----

        internal sealed class MessageForm : ScaledForm
        {
            readonly Label head;
            readonly Panel scroller = new Panel { AutoScroll = true, Margin = Padding.Empty };
            readonly FlowLayoutPanel body = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            readonly FlowLayoutPanel buttonRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right };
            readonly List<Button> buttons = new List<Button>();
            readonly Label feedback = Theme.Fore(new Label { AutoSize = true, Visible = false }, Role.Dim);
            readonly MessageBoxIcon icon;
            readonly string headline, text, details;
            readonly int em, wide;
            internal readonly DialogResult EscResult;
            internal Button DefaultButton { get; private set; }

            static Font HeadFont(Font font) { return new Font(font.FontFamily, font.Size * 1.25f, FontStyle.Bold); }

            public MessageForm(string title, string headline, string text, MessageBoxButtons kind, MessageBoxIcon icon,
                               MessageBoxDefaultButton defaultButton, string details)
            {
                this.icon = icon;
                this.headline = headline ?? "";
                this.text = string.IsNullOrEmpty(text) ? null : text.Replace("\r\n", "\n");
                this.details = string.IsNullOrEmpty(details) ? null : details;
                Text = title;
                Theme.Use(this);
                Font = SystemFonts.MessageBoxFont;
                AutoScaleMode = AutoScaleMode.Dpi;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                ShowInTaskbar = false;
                ShowIcon = false;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                StartPosition = FormStartPosition.CenterParent;
                KeyPreview = true;
                em = Font.Height;
                wide = em * 26;

                var root = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(em, em, em, em * 3 / 4) };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                if (icon != MessageBoxIcon.None)
                {
                    var glyph = SetupResult.Glyph(KindOf(icon), em * 2);
                    glyph.Margin = new Padding(0, 0, em, 0);
                    root.Controls.Add(glyph, 0, 0);
                    root.SetRowSpan(glyph, 2);
                }
                head = new Label { Text = this.headline, AutoSize = true, MaximumSize = new Size(wide, 0), Font = HeadFont(Font), UseMnemonic = false,
                                   Margin = new Padding(0, em / 4, 0, this.text != null || this.details != null ? em / 2 : 0) };
                root.Controls.Add(head, 1, 0);

                if (this.text != null)
                {
                    bool firstParagraph = true;
                    foreach (var paragraph in Regex.Split(this.text, @"\n\s*\n"))
                    {
                        var label = new Label { Text = paragraph.Trim(), AutoSize = true, MaximumSize = new Size(wide, 0), UseMnemonic = false, Margin = new Padding(0, 0, 0, em / 2) };
                        body.Controls.Add(firstParagraph ? label : Theme.Fore(label, Role.Dim));
                        firstParagraph = false;
                    }
                }
                if (this.details != null) AddDetails();
                body.Controls.Add(feedback);
                scroller.Controls.Add(body);
                root.Controls.Add(scroller, 1, 1);

                AddButtons(kind, defaultButton);
                EscResult = buttons.Any(b => b.DialogResult == DialogResult.Cancel) ? DialogResult.Cancel
                          : buttons.Any(b => b.DialogResult == DialogResult.No) ? DialogResult.No : buttons[0].DialogResult;
                CancelButton = buttons.First(b => b.DialogResult == EscResult);
                buttonRow.Margin = new Padding(0, em / 2, 0, 0);
                root.Controls.Add(buttonRow, 0, 2);
                root.SetColumnSpan(buttonRow, 2);
                Controls.Add(root);
                FitText();
                AccessibleName = title;
                AccessibleDescription = (this.headline + "\n" + (this.text ?? "")).Trim();
                FormClosing += delegate { if (DialogResult == DialogResult.Cancel && EscResult != DialogResult.Cancel) DialogResult = EscResult; };
                Shown += delegate { if (DefaultButton != null) DefaultButton.Focus(); Sound(); };
            }

            static ResultKind KindOf(MessageBoxIcon icon)
            {
                switch (icon)
                {
                    case MessageBoxIcon.Error: return ResultKind.Error;          // = Hand, Stop
                    case MessageBoxIcon.Warning: return ResultKind.Warning;      // = Exclamation
                    case MessageBoxIcon.Question: return ResultKind.Question;
                    default: return ResultKind.Info;                             // Information = Asterisk
                }
            }

            void Sound()
            {
                if (Silent) return;
                switch (icon)
                {
                    case MessageBoxIcon.Error: SystemSounds.Hand.Play(); break;
                    case MessageBoxIcon.Warning: SystemSounds.Exclamation.Play(); break;
                    case MessageBoxIcon.Question: SystemSounds.Question.Play(); break;
                    case MessageBoxIcon.Information: SystemSounds.Asterisk.Play(); break;
                }
            }

            void AddButtons(MessageBoxButtons kind, MessageBoxDefaultButton defaultButton)
            {
                var list = new List<KeyValuePair<string, DialogResult>>();
                switch (kind)
                {
                    case MessageBoxButtons.OK: list.Add(Pair("OK", DialogResult.OK)); break;
                    case MessageBoxButtons.OKCancel: list.Add(Pair("OK", DialogResult.OK)); list.Add(Pair(Lang.T("Cancel"), DialogResult.Cancel)); break;
                    case MessageBoxButtons.YesNo: list.Add(Pair(Lang.T("&Yes"), DialogResult.Yes)); list.Add(Pair(Lang.T("&No"), DialogResult.No)); break;
                    case MessageBoxButtons.YesNoCancel:
                        list.Add(Pair(Lang.T("&Yes"), DialogResult.Yes)); list.Add(Pair(Lang.T("&No"), DialogResult.No)); list.Add(Pair(Lang.T("Cancel"), DialogResult.Cancel)); break;
                    case MessageBoxButtons.RetryCancel: list.Add(Pair(Lang.T("Try again"), DialogResult.Retry)); list.Add(Pair(Lang.T("Cancel"), DialogResult.Cancel)); break;
                    default: throw new NotSupportedException("QlMessage: " + kind);
                }
                int index = defaultButton == MessageBoxDefaultButton.Button3 ? 2 : defaultButton == MessageBoxDefaultButton.Button2 ? 1 : 0;
                index = Math.Min(index, list.Count - 1);
                for (int i = 0; i < list.Count; i++)
                {
                    var b = new Button { Text = list[i].Key, DialogResult = list[i].Value, AutoSize = true, MinimumSize = new Size(em * 6, em * 2), Margin = new Padding(em / 2, 0, 0, 0) };
                    buttons.Add(b);
                    buttonRow.Controls.Add(b);
                    if (i != index) { b.FlatStyle = FlatStyle.System; continue; }
                    DefaultButton = b;
                    AcceptButton = b;
                    // The default in the brand colour, unless it is the safe way out (No, Cancel): then no button is
                    // filled, as no answer is recommended (mockup: "Print anyway?" with No preselected)
                    if (b.DialogResult == DialogResult.No || b.DialogResult == DialogResult.Cancel) b.FlatStyle = FlatStyle.System;
                    else { b.Font = new Font(Font, FontStyle.Bold); Theme.Primary(b); }
                }
            }

            static KeyValuePair<string, DialogResult> Pair(string text, DialogResult result) { return new KeyValuePair<string, DialogResult>(text, result); }

            void AddDetails()
            {
                var box = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Visible = false,
                                        Font = new Font(FontFamily.GenericMonospace, Font.Size * 0.9f), Text = details.Replace("\r\n", "\n").Replace("\n", "\r\n"),
                                        Size = new Size(wide, em * 8), Margin = new Padding(0, 0, 0, em / 2) };
                var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                var show = Link(Lang.T("Show details"));
                show.LinkClicked += delegate
                {
                    box.Visible = !box.Visible;
                    show.Text = box.Visible ? Lang.T("Hide details") : Lang.T("Show details");
                    FitText();
                };
                var copy = Link(Lang.T("Copy details"));
                copy.LinkClicked += delegate
                {
                    try { Clipboard.SetText(details); feedback.Text = Lang.T("Details copied."); feedback.Visible = true; FitText(); } catch (Exception) { }
                };
                links.Controls.Add(show);
                links.Controls.Add(copy);
                body.Controls.Add(links);
                body.Controls.Add(box);
            }

            LinkLabel Link(string text)
            {
                var link = new LinkLabel { Text = text, AutoSize = true, LinkBehavior = LinkBehavior.HoverUnderline, Margin = new Padding(0, 0, em, em / 3) };
                Theme.StyleLink(link);
                return link;
            }

            // The text area at its size, at most ~690 px for the whole window (logical, so at any scaling): beyond that
            // it scrolls. After the window's scaling (OnLoad) and on every change of the text area.
            void FitText()
            {
                if (!IsHandleCreated) return;
                int fontHeight = Font.Height;
                body.PerformLayout();
                var size = body.PreferredSize;
                int max = 690 * DeviceDpi / 96 - fontHeight * 12 - head.Height;
                max = Math.Max(fontHeight * 4, Math.Min(max, Screen.FromControl(this).WorkingArea.Height - fontHeight * 14));
                bool scroll = size.Height > max;
                scroller.AutoScroll = false;
                scroller.Size = new Size(size.Width + (scroll ? SystemInformation.VerticalScrollBarWidth + 2 : 0), scroll ? max : size.Height);
                if (!scroll) return;
                // only up and down: the lines wrap at the window's width
                scroller.HorizontalScroll.Maximum = 0;
                scroller.HorizontalScroll.Visible = false;
                scroller.VerticalScroll.Visible = true;
                scroller.AutoScroll = true;
            }

            // Opened on a second screen with other scaling: ScaledForm checks the screen before Load and lets bold fonts
            // follow a DPI change.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                FitText();
            }

            protected override void OnDpiChanged(DpiChangedEventArgs e)
            {
                base.OnDpiChanged(e);
                head.Font = HeadFont(Font);
                FitText();
            }

            // J/N (Y/N) without Alt, as MessageBox's underlined letters (not while typing in the details box)
            protected override bool ProcessDialogChar(char charCode)
            {
                if ((ModifierKeys & (Keys.Control | Keys.Alt)) == 0 && !(ActiveControl is TextBox))
                    foreach (var b in buttons)
                        if (IsMnemonic(charCode, b.Text)) { b.PerformClick(); return true; }
                return base.ProcessDialogChar(charCode);
            }

            // Ctrl+C: the message as text (the details box copies its own selection)
            protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
            {
                if ((keyData == (Keys.Control | Keys.C) || keyData == (Keys.Control | Keys.Insert)) && !(ActiveControl is TextBox && ((TextBox)ActiveControl).SelectionLength > 0))
                {
                    try { Clipboard.SetText(CopyText()); } catch (Exception) { }
                    return true;
                }
                return base.ProcessCmdKey(ref msg, keyData);
            }

            // As MessageBox copies itself: title, message, buttons between dashed lines
            internal string CopyText()
            {
                const string line = "---------------------------";
                var b = new StringBuilder();
                b.Append(line).Append("\r\n").Append(Text).Append("\r\n").Append(line).Append("\r\n");
                b.Append(headline);
                if (text != null) b.Append("\r\n\r\n").Append(text.Replace("\n", "\r\n"));
                if (details != null) b.Append("\r\n\r\n").Append(details.Replace("\r\n", "\n").Replace("\n", "\r\n"));
                b.Append("\r\n").Append(line).Append("\r\n");
                foreach (var button in buttons) b.Append(button.Text.Replace("&", "")).Append("   ");
                b.Append("\r\n").Append(line).Append("\r\n");
                return b.ToString();
            }
        }
    }
}
