// The help pages (labeltool\help, installed as <exe folder>\help) in the default browser: F1 and the Help menus of
// QL Label Formats, QL Label Maker and QL Printer Status; F1 in a dialog opens its owner's page. A page is named without
// its language: "index.html", "codes.html" (QL Label Maker), "formats/setup.html" (QL Label Formats); it opens
// in the program's language, else in English.
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace QlLabels
{
    static class HelpPages
    {
        // QL Label Formats' pages (QL Printer Status has none of its own: it opens Status).
        public const string Formats = "formats/index.html", FormatsList = "formats/formats.html", Setup = "formats/setup.html",
                            Install = "formats/install.html", Status = "formats/status.html";
        public const string LabelMaker = "index.html";

        // <exe folder>\help; Setup.exe at the top of a release zip: <zip>\advanced\out\tools\help.
        internal static string Folder(string exeFolder)
        {
            foreach (var dir in new[] { Path.Combine(exeFolder, "help"), Path.Combine(exeFolder, @"advanced\out\tools\help") })
                if (Directory.Exists(dir)) return dir;
            return null;
        }

        // The page's file in this language, else in English; null when the help (or the page) isn't there.
        internal static string Find(string folder, string page, string language)
        {
            if (folder == null) return null;
            string dir = Path.GetDirectoryName(page.Replace('/', '\\')), file = Path.GetFileName(page);
            foreach (var lang in language == "de" ? new[] { "de", "en" } : new[] { "en" })
            {
                string path = Path.Combine(folder, dir, lang, file);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        // Each window's page, for F1 in its dialogs (a dialog without a page of its own opens its owner's).
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Form, string> pages =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Form, string>();

        // The window's page, without handling F1 (a window that handles its keys itself).
        public static void SetPage(Form form, string page)
        {
            pages.Remove(form);
            pages.Add(form, page);
        }

        // F1 opens the page; page null: the page of the nearest owner that has one (nothing when none has).
        public static void F1(Form form, string page = null)
        {
            if (page != null) SetPage(form, page);
            form.KeyPreview = true;
            form.KeyDown += (s, e) =>
            {
                if (e.KeyCode != Keys.F1 || e.Modifiers != Keys.None) return;
                e.Handled = true;
                string shown = PageOf(form);
                if (shown != null) Open(form, shown);
            };
        }

        internal static string PageOf(Form form)
        {
            for (var f = form; f != null; f = OwnerOf(f))
            {
                string page;
                if (pages.TryGetValue(f, out page)) return page;
            }
            return null;
        }

        // ShowDialog(owner) sets only the window's owner, not Form.Owner.
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        static Form OwnerOf(Form f)
        {
            if (f.Owner != null) return f.Owner;
            if (!f.IsHandleCreated) return null;
            return Control.FromHandle(GetWindow(f.Handle, 4 /* GW_OWNER */)) as Form;
        }

        // The default browser (shell "open"); UI tests replace it.
        internal static Action<string> Launch = path => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        public static void Open(IWin32Window owner, string page)
        {
            string path = Find(Folder(Path.GetDirectoryName(Application.ExecutablePath)), page, Lang.Current == "de" ? "de" : "en");
            string title = owner is Form ? ((Form)owner).Text : "QL Unidrv";
            if (path == null)
            {
                QlMessage.Show(owner, Lang.T("The help isn't installed. Setup.exe repairs the installation."), title,
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            try { Launch(path); }
            catch (Exception ex) { QlMessage.Ask(owner, title, Lang.T("The help page couldn't be opened."), null, MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, ex.Message); }
        }
    }
}
