// Where a paste or a drop comes from: the program that put it there, so that quirks of one program are handled for
// that program only (Corel/CorelEmf.cs). Signs, strongest first: the clipboard's owner process (a paste only), program-
// only clipboard formats, and the OLE Object Descriptor's type name ("CorelDRAW 2024 Graphic"). The Object Descriptor
// also names the source file (its path): that part is never read, kept or shown.
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace QlLabels
{
    enum SourceProgram { Unknown, CorelDraw, Affinity }

    sealed class ImportSource
    {
        public SourceProgram Program;
        public string Version = "";      // from the type name ("2024"); "" when unknown

        public bool IsCorelDraw { get { return Program == SourceProgram.CorelDraw; } }
        public bool IsAffinity { get { return Program == SourceProgram.Affinity; } }

        public static readonly ImportSource Unknown = new ImportSource();

        // CorelDRAW's own formats (registered names on the clipboard).
        public const string Cmx32Format = "Corel 32-bit Presentation Exchange Data", Cmx64Format = "Corel 64-bit Presentation Exchange Data";
        public const string ObjectDescriptorFormat = "Object Descriptor";

        // owner: the clipboard owner's process name (ClipboardOwner), null for a drop or a test.
        public static ImportSource Detect(IClipSource s, string owner)
        {
            var r = new ImportSource();
            string type = TypeName(s);
            bool corelType = type != null && type.StartsWith("CorelDRAW", StringComparison.OrdinalIgnoreCase);
            bool corelOwner = owner != null && owner.StartsWith("CorelDRW", StringComparison.OrdinalIgnoreCase);
            bool corelFormats = Has(s, Cmx32Format) || Has(s, Cmx64Format);
            if (corelOwner || corelType || corelFormats)
            {
                r.Program = SourceProgram.CorelDraw;
                if (corelType)
                {
                    var year = type.Split(' ').FirstOrDefault(w => w.Length == 4 && w.All(char.IsDigit));
                    if (year != null) r.Version = year;
                }
            }
            // Affinity (Import/Affinity/AffinityClip.cs): its process, or its own node formats (Designer: "Serif Persona")
            else if ((owner != null && owner.StartsWith("Affinity", StringComparison.OrdinalIgnoreCase))
                     || Has(s, AffinityClip.NodesFormat) || Has(s, AffinityClip.DesignerNodesFormat))
                r.Program = SourceProgram.Affinity;
            return r;
        }

        static bool Has(IClipSource s, string format)
        {
            try { return s.Has(format); } catch (Exception) { return false; }
        }

        // OBJECTDESCRIPTOR: cbSize, CLSID, dwDrawAspect, SIZEL, POINTL, dwStatus, dwFullUserTypeName, dwSrcOfCopy; the
        // type name is a UTF-16 string at its offset. Only the type name is read (dwSrcOfCopy is the source's path).
        internal static string TypeName(IClipSource s)
        {
            if (!Has(s, ObjectDescriptorFormat)) return null;
            byte[] d;
            try { d = s.Get(ObjectDescriptorFormat) as byte[]; } catch (Exception) { return null; }
            if (d == null || d.Length < 52) return null;
            int at = BitConverter.ToInt32(d, 44);
            if (at < 52 || at >= d.Length - 1) return null;
            var sb = new StringBuilder();
            for (int i = at; i + 1 < d.Length && sb.Length < 200; i += 2)
            {
                char c = (char)BitConverter.ToUInt16(d, i);
                if (c == '\0') break;
                sb.Append(c);
            }
            return sb.ToString();
        }

        [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

        // The process that put the clipboard's data there ("CorelDRW"); null when unknown.
        public static string ClipboardOwner()
        {
            try
            {
                var h = GetClipboardOwner();
                if (h == IntPtr.Zero) return null;
                uint pid;
                GetWindowThreadProcessId(h, out pid);
                if (pid == 0) return null;
                using (var p = Process.GetProcessById((int)pid)) return p.ProcessName;
            }
            catch (Exception) { return null; }
        }
    }
}
