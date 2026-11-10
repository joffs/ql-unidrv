// One open label in QL Label Maker: its document and everything that belongs to it, kept here
// while another tab is shown. EditorForm works on its own fields for the active tab; EditorForm.Tabs.cs stores them into
// the tab before another one is shown and takes them back when it is shown again (undo, file, printer and label format,
// resolution, length, zoom, scroll position, selection).
using System.Collections.Generic;
using System.Drawing;

namespace QlLabels
{
    sealed class LabelTab
    {
        public LabelDoc Doc = new LabelDoc();
        public UndoStack Undo = new UndoStack();
        public string Path;          // null: not saved yet
        public int Untitled;         // "Untitled n" while Path is null
        public bool Dirty;
        public bool KeepFileFormat;
        public object LastEdited;

        public string Printer;
        public QlModel Model;
        public List<LabelChoice> Choices = new List<LabelChoice>();
        public LabelChoice Label;
        public bool HighRes;
        public PrinterCheck Roll;
        public bool AutoLength;
        public string LengthError = "";

        public double Zoom;          // percent; 0 = fit to the window
        public Point Scroll;         // the canvas host's scroll position (positive)
        public List<LabelObject> Selection = new List<LabelObject>();
        public TextObject NewText;   // a text from the Text tool not edited yet

        // An empty label nobody has worked on: when it is the window's only tab, a file opened replaces it instead of
        // opening a tab of its own (EditorForm.OnlyStartTab).
        public bool Pristine { get { return Path == null && !Dirty && Doc.Objects.Count == 0 && !Undo.CanUndo; } }
    }
}
