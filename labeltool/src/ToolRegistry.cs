// Label editor: the insert tools in one list, in groups. The palette on the left (one button per group, a flyout with
// the group's tools; EditorForm.Palette.cs) and the Insert menu (every tool, in this order) are built from it, so a new
// tool is one entry here plus its insert command. A tool's Insert remembers it as its group's tool (the palette's
// icon), also from the menu or its key (editor-next step 3, 2026-11-08).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace QlLabels
{
    sealed class EditorTool
    {
        public readonly string Id;          // remembered per user as the group's tool
        public readonly ToolIcon Icon;
        public readonly string Name;        // the flyout's entry and, when it is the group's only tool, the palette's name
        public readonly string Add;         // the palette's tooltip, first line ("Add a star", with the key)
        public readonly string Explain;     // the flyout entry's tooltip, second line (null: none)
        public readonly string More;        // the palette's tooltip, second line, when this tool is the group's other one
        public readonly string MenuText;    // the Insert menu's entry (with its & key)
        public readonly Keys Keys;
        public Action Insert;               // inserts the tool's object and remembers the tool (set by InsertTools)
        public ToolGroup Group;

        public EditorTool(string id, ToolIcon icon, string name, string add, string explain, string more, string menuText, Keys keys)
        {
            Id = id; Icon = icon; Name = name; Add = add; Explain = explain; More = more; MenuText = menuText; Keys = keys;
        }
    }

    sealed class ToolGroup
    {
        public readonly string Id, Name;
        public readonly string More;        // the palette's tooltip, second line, for a group of three or more
        public readonly List<EditorTool> Tools = new List<EditorTool>();
        public EditorTool Current;          // the palette's icon and what a click inserts: the tool used last

        public ToolGroup(string id, string name, string more) { Id = id; Name = name; More = more; }

        public bool HasFlyout { get { return Tools.Count > 1; } }

        // The palette button's tooltip: the current tool, then how to reach the others.
        public string Tip
        {
            get
            {
                if (!HasFlyout) return Current.Add;
                string more = Tools.Count == 2 ? Tools.First(t => t != Current).More : More;
                return Current.Add + "\n" + more;
            }
        }
    }

    sealed partial class EditorForm
    {
        List<ToolGroup> toolGroups;

        // The groups in the palette's order, each tool with its insert command.
        List<ToolGroup> ToolGroups()
        {
            if (toolGroups != null) return toolGroups;
            var groups = new List<ToolGroup>();
            Action<ToolGroup, EditorTool, Action> add = (g, t, insert) =>
            {
                t.Group = g;
                t.Insert = delegate { UseTool(t); insert(); };
                g.Tools.Add(t);
            };
            var text = new ToolGroup("text", Lang.T("Text"), null);
            add(text, new EditorTool("artistic", ToolIcon.ArtisticText, Lang.T("Artistic text"), Lang.T("Write artistic text · Ctrl+T inserts it"),
                                     Lang.T("One line; the handles change the font size"), Lang.T("Click the triangle or press and hold: artistic text"),
                                     Lang.T("&Artistic text"), Keys.Control | Keys.T), AddText);
            add(text, new EditorTool("paragraph", ToolIcon.ParagraphText, Lang.T("Paragraph text"), Lang.T("Draw a paragraph text frame · Ctrl+Shift+T inserts it"),
                                     Lang.T("Wraps in its box"), Lang.T("Click the triangle or press and hold: paragraph text"),
                                     Lang.T("&Paragraph text"), Keys.Control | Keys.Shift | Keys.T), AddTextFrame);
            groups.Add(text);
            var shapes = new ToolGroup("shapes", Lang.T("Shapes"), Lang.T("Click the triangle or press and hold: more shapes"));
            add(shapes, new EditorTool("rectangle", ToolIcon.Rectangle, Lang.T("Rectangle"), Lang.T("Draw a rectangle · Ctrl+B inserts it"),
                                       Lang.T("Also with round corners"), null, Lang.T("&Rectangle"), Keys.Control | Keys.B), () => AddShape(ShapeKind.Rectangle));
            add(shapes, new EditorTool("ellipse", ToolIcon.Ellipse, Lang.T("Ellipse"), Lang.T("Draw an ellipse · Ctrl+E inserts it"),
                                       Lang.T("Shift while dragging a corner: a circle"), null, Lang.T("&Ellipse"), Keys.Control | Keys.E), () => AddShape(ShapeKind.Ellipse));
            add(shapes, new EditorTool("triangle", ToolIcon.Triangle, Lang.T("Triangle"), Lang.T("Draw a triangle"),
                                       Lang.T("Shift while dragging a corner: equilateral"), null, Lang.T("Tr&iangle"), Keys.None), () => AddShape(ShapeKind.Triangle));
            add(shapes, new EditorTool("polygon", ToolIcon.Polygon, Lang.T("Polygon"), Lang.T("Draw a polygon"),
                                       Lang.T("3 to 20 corners"), null, Lang.T("P&olygon"), Keys.None), () => AddShape(ShapeKind.Polygon));
            add(shapes, new EditorTool("star", ToolIcon.Star, Lang.T("Star"), Lang.T("Draw a star"),
                                       Lang.T("3 to 20 points, sharp or blunt"), null, Lang.T("&Star"), Keys.None), () => AddShape(ShapeKind.Star));
            groups.Add(shapes);
            var line = new ToolGroup("line", Lang.T("Line"), null);
            add(line, new EditorTool("line", ToolIcon.Line, Lang.T("Line"), Lang.T("Draw a line · Ctrl+L inserts it"), null, null, Lang.T("&Line"), Keys.Control | Keys.L), AddLine);
            groups.Add(line);
            var picture = new ToolGroup("picture", Lang.T("Picture"), null);
            add(picture, new EditorTool("picture", ToolIcon.Picture, Lang.T("Picture"), Lang.T("Place a picture: logo, photo, symbol · Ctrl+I inserts it"), null, null,
                                        Lang.T("Pict&ure..."), Keys.Control | Keys.I), AddPicture);
            groups.Add(picture);
            var code = new ToolGroup("code", Lang.T("Code"), null);
            add(code, new EditorTool("barcode", ToolIcon.Barcode, Lang.T("Barcode"), Lang.T("Draw a barcode · Ctrl+K inserts it"),
                                     Lang.T("Code 128 or EAN-13"), Lang.T("Click the triangle or press and hold: barcode"), Lang.T("&Barcode"), Keys.Control | Keys.K), AddBarcode);
            add(code, new EditorTool("qr", ToolIcon.QrCode, Lang.T("QR code"), Lang.T("Draw a QR code · Ctrl+Q inserts it"),
                                     Lang.T("Text, web address, contact, Wi-Fi..."), Lang.T("Click the triangle or press and hold: QR code"), Lang.T("&QR code"), Keys.Control | Keys.Q), AddQrCode);
            groups.Add(code);
            foreach (var g in groups)
            {
                string id = ToolSetting("Tool." + g.Id) as string;
                g.Current = g.Tools.FirstOrDefault(t => t.Id == id) ?? g.Tools[0];
            }
            return toolGroups = groups;
        }

        // Every tool, in the Insert menu's order.
        List<EditorTool> InsertTools() { return ToolGroups().SelectMany(g => g.Tools).ToList(); }

        // A tool was used (palette, flyout, Insert menu, its key): its group shows it from now on, also next time.
        void UseTool(EditorTool t)
        {
            if (t.Group.Current == t) return;
            t.Group.Current = t;
            ToolSetting("Tool." + t.Group.Id, t.Id);
            UpdatePaletteButton(t.Group);
        }

        // The editor's per-user settings: HKCU\Software\QLUnidrv\LabelMaker (value null: read).
        const string EditorKey = Lang.SettingsKey + @"\LabelMaker";

        static object ToolSetting(string name, object value = null)
        {
            try
            {
                if (value == null)
                    using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(EditorKey)) return k == null ? null : k.GetValue(name);
                using (var k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(EditorKey)) k.SetValue(name, value);
            }
            catch (Exception) { }
            return null;
        }
    }
}
