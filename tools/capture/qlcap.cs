// qlcap - capture helper for the Brother QL-800 reference captures (x64 machine).
//
//   qlcap dump  <printer> <devmode.bin>
//       Writes the printer's current default DEVMODE (public + private part).
//   qlcap print <printer> <devmode.bin|-> [pages=N] [paper=N] [yres=N] [orient=N]
//               [copies=N] [color=N] [len=N] [wid=N] [form=NAME] [set=OFF:HEXBYTES ...] [save=out.bin]
//               [red=1] [page=mark] [text=TEXT]
//       Prints the standard test page (border, text, bar pattern) N times.
//       Opt-in variants (the default page is unchanged):
//         red=1      text and bar pattern in pure red (RGB 255,0,0), border black (two-colour tests)
//         text=TEXT  page text instead of "QL-800 test" (page number appended)
//         page=mark  instead of the test page only one black bar, 4 lines high, 20 lines above the
//                    bottom edge: every other line is blank (shows which blank lines become Z)
//       form= selects a paper by name as applications do: dmPaperSize from DeviceCapabilities
//       (DC_PAPERS/DC_PAPERNAMES) plus dmFormName.
//       set= patches raw DEVMODE bytes (offset from DEVMODE start, hex) before
//       DocumentProperties validation. save= stores the DEVMODE actually used.
//
// Build: csc /platform:x64 /out:qlcap.exe qlcap.cs
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

static class QlCap
{
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr h, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int DocumentProperties(IntPtr hwnd, IntPtr hPrinter, string name, IntPtr outDm, IntPtr inDm, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateDC(string driver, string device, string port, IntPtr dm);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc, int idx);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int StartDoc(IntPtr dc, ref DOCINFO di);
    [DllImport("gdi32.dll")] static extern int StartPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int EndPage(IntPtr dc);
    [DllImport("gdi32.dll")] static extern int EndDoc(IntPtr dc);
    [DllImport("gdi32.dll")] static extern bool Rectangle(IntPtr dc, int l, int t, int r, int b);
    [DllImport("gdi32.dll")] static extern bool PatBlt(IntPtr dc, int x, int y, int w, int h, uint rop);
    [DllImport("gdi32.dll")] static extern IntPtr GetStockObject(int i);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr o);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern bool TextOut(IntPtr dc, int x, int y, string s, int n);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateFont(int h, int w, int esc, int orient, int weight, uint italic, uint underline,
        uint strike, uint charset, uint outPrec, uint clipPrec, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll")] static extern IntPtr CreatePen(int style, int width, uint color);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr o);
    [DllImport("gdi32.dll")] static extern IntPtr CreateSolidBrush(uint color);
    [DllImport("gdi32.dll")] static extern uint SetTextColor(IntPtr dc, uint color);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int DeviceCapabilities(string device, string port, int capability, IntPtr output, IntPtr dm);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DOCINFO { public int cbSize; public string lpszDocName; public string lpszOutput; public string lpszDatatype; public int fwType; }

    const int DM_OUT_BUFFER = 2, DM_IN_BUFFER = 8;
    const uint BLACKNESS = 0x00000042, PATCOPY = 0x00F00021, RED = 0x000000FF;

    static byte[] GetDefault(IntPtr hp, string name)
    {
        int size = DocumentProperties(IntPtr.Zero, hp, name, IntPtr.Zero, IntPtr.Zero, 0);
        IntPtr buf = Marshal.AllocHGlobal(size);
        DocumentProperties(IntPtr.Zero, hp, name, buf, IntPtr.Zero, DM_OUT_BUFFER);
        byte[] dm = new byte[size];
        Marshal.Copy(buf, dm, 0, size);
        Marshal.FreeHGlobal(buf);
        return dm;
    }

    static byte[] Merge(IntPtr hp, string name, byte[] dm)
    {
        int size = DocumentProperties(IntPtr.Zero, hp, name, IntPtr.Zero, IntPtr.Zero, 0);
        IntPtr inb = Marshal.AllocHGlobal(Math.Max(size, dm.Length));
        IntPtr outb = Marshal.AllocHGlobal(size);
        Marshal.Copy(dm, 0, inb, dm.Length);
        int r = DocumentProperties(IntPtr.Zero, hp, name, outb, inb, DM_IN_BUFFER | DM_OUT_BUFFER);
        if (r < 0) throw new Exception("DocumentProperties merge failed " + Marshal.GetLastWin32Error());
        byte[] res = new byte[size];
        Marshal.Copy(outb, res, 0, size);
        Marshal.FreeHGlobal(inb); Marshal.FreeHGlobal(outb);
        return res;
    }

    static void Put16(byte[] dm, int off, int v) { dm[off] = (byte)v; dm[off + 1] = (byte)(v >> 8); }
    static void Fields(byte[] dm, uint f) { uint cur = BitConverter.ToUInt32(dm, 0x48); cur |= f; BitConverter.GetBytes(cur).CopyTo(dm, 0x48); }

    // Paper by name: dmPaperSize (DC_PAPERS index entry) + dmFormName, no explicit length/width.
    static void SelectForm(string printer, byte[] dm, string form)
    {
        const int DC_PAPERS = 2, DC_PAPERNAMES = 16;
        int n = DeviceCapabilities(printer, null, DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) throw new Exception("DeviceCapabilities failed");
        IntPtr names = Marshal.AllocHGlobal(n * 64 * 2), ids = Marshal.AllocHGlobal(n * 2);
        try
        {
            DeviceCapabilities(printer, null, DC_PAPERNAMES, names, IntPtr.Zero);
            DeviceCapabilities(printer, null, DC_PAPERS, ids, IntPtr.Zero);
            for (int i = 0; i < n; i++)
            {
                string s = Marshal.PtrToStringUni(new IntPtr(names.ToInt64() + i * 128), 64).TrimEnd('\0');
                int z = s.IndexOf('\0');
                if (z >= 0) s = s.Substring(0, z);
                if (!string.Equals(s, form, StringComparison.OrdinalIgnoreCase)) continue;
                Put16(dm, 0x4e, Marshal.ReadInt16(ids, i * 2));
                byte[] fn = new byte[64];
                byte[] src = Encoding.Unicode.GetBytes(s);
                Array.Copy(src, fn, Math.Min(src.Length, 62));
                fn.CopyTo(dm, 0x66);
                uint f = BitConverter.ToUInt32(dm, 0x48);
                f = (f | 0x2 | 0x10000) & ~0xCu;  // DM_PAPERSIZE | DM_FORMNAME, no DM_PAPERLENGTH/WIDTH
                BitConverter.GetBytes(f).CopyTo(dm, 0x48);
                return;
            }
        }
        finally { Marshal.FreeHGlobal(names); Marshal.FreeHGlobal(ids); }
        throw new Exception("paper '" + form + "' not offered by " + printer);
    }

    static void Describe(byte[] dm)
    {
        Console.WriteLine("dmSize={0} dmDriverExtra={1} dmFields=0x{2:X8}", BitConverter.ToUInt16(dm, 0x44), BitConverter.ToUInt16(dm, 0x46), BitConverter.ToUInt32(dm, 0x48));
        Console.WriteLine("orient={0} paper={1} plen={2} pwid={3} scale={4} copies={5} quality={6} color={7} yres={8} form='{9}'",
            BitConverter.ToInt16(dm, 0x4c), BitConverter.ToInt16(dm, 0x4e), BitConverter.ToInt16(dm, 0x50), BitConverter.ToInt16(dm, 0x52),
            BitConverter.ToInt16(dm, 0x54), BitConverter.ToInt16(dm, 0x56), BitConverter.ToInt16(dm, 0x5a), BitConverter.ToInt16(dm, 0x5c),
            BitConverter.ToInt16(dm, 0x60), Encoding.Unicode.GetString(dm, 0x66, 64).TrimEnd('\0'));
    }

    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: see source header"); return 2; }
        string cmd = args[0], name = args[1];
        IntPtr hp;
        if (!OpenPrinter(name, out hp, IntPtr.Zero)) { Console.Error.WriteLine("OpenPrinter failed"); return 1; }
        try
        {
            if (cmd == "dump")
            {
                byte[] dm = GetDefault(hp, name);
                File.WriteAllBytes(args[2], dm);
                Describe(dm);
                return 0;
            }
            byte[] d = args[2] == "-" ? GetDefault(hp, name) : File.ReadAllBytes(args[2]);
            int pages = 1; string save = null, page = null, text = "QL-800 test"; bool red = false;
            for (int i = 3; i < args.Length; i++)
            {
                string[] kv = args[i].Split(new[] { '=' }, 2);
                string k = kv[0], v = kv[1];
                switch (k)
                {
                    case "pages": pages = int.Parse(v); break;
                    case "save": save = v; break;
                    case "red": red = v == "1"; break;
                    case "page": page = v; break;
                    case "text": text = v; break;
                    case "orient": Put16(d, 0x4c, int.Parse(v)); Fields(d, 0x1); break;
                    case "paper": Put16(d, 0x4e, int.Parse(v)); Fields(d, 0x2); break;
                    case "len": Put16(d, 0x50, int.Parse(v)); Fields(d, 0x4); break;
                    case "wid": Put16(d, 0x52, int.Parse(v)); Fields(d, 0x8); break;
                    case "copies": Put16(d, 0x56, int.Parse(v)); Fields(d, 0x100); break;
                    case "color": Put16(d, 0x5c, int.Parse(v)); Fields(d, 0x800); break;
                    case "yres": Put16(d, 0x60, int.Parse(v)); Fields(d, 0x2000); break;
                    case "form": SelectForm(name, d, v); break;
                    case "set":
                        string[] ov = v.Split(':');
                        int off = Convert.ToInt32(ov[0], 16);
                        for (int j = 0; j < ov[1].Length / 2; j++) d[off + j] = Convert.ToByte(ov[1].Substring(j * 2, 2), 16);
                        break;
                    default: throw new Exception("unknown option " + k);
                }
            }
            d = Merge(hp, name, d);
            Describe(d);
            if (save != null) File.WriteAllBytes(save, d);

            IntPtr pdm = Marshal.AllocHGlobal(d.Length);
            Marshal.Copy(d, 0, pdm, d.Length);
            IntPtr dc = CreateDC(null, name, null, pdm);
            if (dc == IntPtr.Zero) throw new Exception("CreateDC failed");
            int w = GetDeviceCaps(dc, 8 /*HORZRES*/), h = GetDeviceCaps(dc, 10 /*VERTRES*/);
            int dx = GetDeviceCaps(dc, 88 /*LOGPIXELSX*/), dy = GetDeviceCaps(dc, 90 /*LOGPIXELSY*/);
            Console.WriteLine("HORZRES={0} VERTRES={1} DPI={2}x{3} PHYS={4}x{5} OFF={6},{7}", w, h, dx, dy,
                GetDeviceCaps(dc, 110), GetDeviceCaps(dc, 111), GetDeviceCaps(dc, 112), GetDeviceCaps(dc, 113));
            var di = new DOCINFO { cbSize = Marshal.SizeOf(typeof(DOCINFO)), lpszDocName = "qlcap test" };
            StartDoc(dc, ref di);
            for (int p = 1; p <= pages; p++)
            {
                StartPage(dc);
                if (page == "mark")
                {
                    PatBlt(dc, w / 20, h - 20, w - w / 10, 4, BLACKNESS);
                    EndPage(dc);
                    continue;
                }
                IntPtr pen = CreatePen(0, Math.Max(1, dx / 150), 0);
                IntPtr oldPen = SelectObject(dc, pen);
                IntPtr oldBr = SelectObject(dc, GetStockObject(5 /*NULL_BRUSH*/));
                Rectangle(dc, 2, 2, w - 2, h - 2);
                SelectObject(dc, oldBr);
                SelectObject(dc, oldPen); DeleteObject(pen);
                int fh = Math.Min(h / 6, dy / 4);
                IntPtr font = CreateFont(-fh, 0, 0, 0, 700, 0, 0, 0, 0, 0, 0, 3 /*NONANTIALIASED*/, 0, "Arial");
                IntPtr oldF = SelectObject(dc, font);
                if (red) SetTextColor(dc, RED);
                string s = text + " p" + p;
                TextOut(dc, w / 20, h / 10, s, s.Length);
                SelectObject(dc, oldF); DeleteObject(font);
                // barcode-like pattern: bars of 1..4 dots wide with growing gaps
                IntPtr brush = red ? CreateSolidBrush(RED) : IntPtr.Zero;
                IntPtr oldB = red ? SelectObject(dc, brush) : IntPtr.Zero;
                int x = w / 20, y = h / 2, bh = h / 3, k2 = 0;
                while (x < w - w / 20) { int bw = 1 + (k2 % 4); PatBlt(dc, x, y, bw, bh, red ? PATCOPY : BLACKNESS); x += bw + 1 + (k2 % 3); k2++; }
                if (red) { SelectObject(dc, oldB); DeleteObject(brush); }
                EndPage(dc);
            }
            EndDoc(dc);
            DeleteDC(dc);
            Marshal.FreeHGlobal(pdm);
            return 0;
        }
        finally { ClosePrinter(hp); }
    }
}
