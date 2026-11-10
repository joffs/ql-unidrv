using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QlLabels
{
    // QL Label Maker's own mouse pointers. PanMove: the four-way cross while the view is moved with the middle button
    // (EditorForm.Pan.cs), from the resource pan-move.cur (assets\make-cursors.ps1: 32/40/48/64 px for 100/125/150/200 %,
    // hotspot in the middle; build.cmd /resource). Windows does not scale a cursor a program sets, so the size follows the
    // window's DPI; each size is made once and kept for the life of the process. Without the resource (the UI test
    // builds): Windows' own move cursor.
    static class EditorCursors
    {
        static readonly Dictionary<int, Cursor> panMove = new Dictionary<int, Cursor>();

        public static Cursor PanMove(int dpi)
        {
            int want = Math.Max(32, (int)Math.Round(32 * dpi / 96.0));
            Cursor cursor;
            if (!panMove.TryGetValue(want, out cursor))
            {
                cursor = Load("pan-move.cur", want) ?? Cursors.SizeAll;
                panMove[want] = cursor;
            }
            return cursor;
        }

        // The image of a .cur resource that fits: the smallest at least `size` px wide, else the largest.
        static Cursor Load(string name, int size)
        {
            try
            {
                byte[] file;
                using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    using (var copy = new MemoryStream()) { stream.CopyTo(copy); file = copy.ToArray(); }
                }
                if (file.Length < 6 || BitConverter.ToUInt16(file, 2) != 2) return null;
                int count = BitConverter.ToUInt16(file, 4), best = -1, bestSize = 0;
                for (int i = 0; i < count && 6 + 16 * i + 16 <= file.Length; i++)
                {
                    int w = file[6 + 16 * i] == 0 ? 256 : file[6 + 16 * i];
                    bool better = best < 0 || (w >= size ? (bestSize < size || w < bestSize) : (bestSize < size && w > bestSize));
                    if (better) { best = i; bestSize = w; }
                }
                if (best < 0) return null;
                int entry = 6 + 16 * best, length = BitConverter.ToInt32(file, entry + 8), offset = BitConverter.ToInt32(file, entry + 12);
                if (length <= 0 || offset < 0 || offset + length > file.Length) return null;
                // CreateIconFromResourceEx wants a cursor's image with its hotspot (two WORDs) in front
                var bits = new byte[length + 4];
                Array.Copy(file, entry + 4, bits, 0, 4);
                Array.Copy(file, offset, bits, 4, length);
                IntPtr handle = CreateIconFromResourceEx(bits, bits.Length, false, 0x00030000, bestSize, bestSize, 0);
                return handle == IntPtr.Zero ? null : new Cursor(handle);
            }
            catch (Exception) { return null; }
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr CreateIconFromResourceEx(byte[] bits, int length, bool icon, int version, int width, int height, int flags);
    }
}
