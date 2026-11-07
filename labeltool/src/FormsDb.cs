// The machine-wide Windows forms database (EnumForms / AddForm / SetForm / DeleteForm).
// Reading works for everyone; changing needs administrator rights (server handle).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QlLabels
{
    sealed class FormInfo
    {
        public string Name;
        public uint Flags;   // FormsDb.FORM_USER / FORM_BUILTIN / FORM_PRINTER
        public int Cx, Cy;   // 0.001 mm
    }

    // What Sync needs to change the database (tests use a list in memory).
    interface IFormsWriter
    {
        List<FormInfo> Enumerate();
        void Add(string name, int cx, int cy);
        bool Delete(string name);
    }

    static class FormsDb
    {
        public const uint FORM_USER = 0, FORM_BUILTIN = 1, FORM_PRINTER = 2;
        const int ERROR_INVALID_FORM_NAME = 1902;
        const int SERVER_ACCESS_ADMINISTER = 0x1, SERVER_ACCESS_ENUMERATE = 0x2, STANDARD_RIGHTS_REQUIRED = 0xF0000;
        const int SERVER_READ = 0x20000 | SERVER_ACCESS_ENUMERATE;
        const int SERVER_ALL_ACCESS = STANDARD_RIGHTS_REQUIRED | SERVER_ACCESS_ADMINISTER | SERVER_ACCESS_ENUMERATE;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct FORM_INFO_1
        {
            public uint Flags;
            public IntPtr pName;
            public int cx, cy, left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct PRINTER_DEFAULTS { public IntPtr pDatatype; public IntPtr pDevMode; public int DesiredAccess; }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterW(string name, out IntPtr handle, ref PRINTER_DEFAULTS defaults);
        [DllImport("winspool.drv", SetLastError = true)]
        static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EnumFormsW(IntPtr handle, int level, IntPtr buffer, int size, out int needed, out int returned);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool AddFormW(IntPtr handle, int level, ref FORM_INFO_1 form);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetFormW(IntPtr handle, string name, int level, ref FORM_INFO_1 form);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool DeleteFormW(IntPtr handle, string name);

        static IntPtr OpenServer(int access)
        {
            var d = new PRINTER_DEFAULTS { DesiredAccess = access };
            IntPtr h;
            if (!OpenPrinterW(null, out h, ref d)) throw new Win32Exception();
            return h;
        }

        public static List<FormInfo> Enumerate()
        {
            IntPtr h = OpenServer(SERVER_READ);
            try { return Enumerate(h); }
            finally { ClosePrinter(h); }
        }

        static List<FormInfo> Enumerate(IntPtr h)
        {
            int needed, count;
            EnumFormsW(h, 1, IntPtr.Zero, 0, out needed, out count);
            var list = new List<FormInfo>();
            if (needed <= 0) return list;
            IntPtr buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (!EnumFormsW(h, 1, buf, needed, out needed, out count)) throw new Win32Exception();
                int size = Marshal.SizeOf(typeof(FORM_INFO_1));
                for (int i = 0; i < count; i++)
                {
                    var f = (FORM_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + (long)i * size), typeof(FORM_INFO_1));
                    list.Add(new FormInfo { Name = Marshal.PtrToStringUni(f.pName), Flags = f.Flags, Cx = f.cx, Cy = f.cy });
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
            return list;
        }

        // Changes need an administrator; one handle for a whole sync.
        public sealed class Writer : IDisposable, IFormsWriter
        {
            IntPtr h;
            public Writer() { h = OpenServer(SERVER_ALL_ACCESS); }
            public void Dispose() { if (h != IntPtr.Zero) { ClosePrinter(h); h = IntPtr.Zero; } }

            public List<FormInfo> Enumerate() { return FormsDb.Enumerate(h); }

            // The whole form is imageable; the driver applies its own margins.
            static FORM_INFO_1 Info(IntPtr name, int cx, int cy)
            {
                return new FORM_INFO_1 { Flags = FORM_USER, pName = name, cx = cx, cy = cy, right = cx, bottom = cy };
            }

            public void Add(string name, int cx, int cy) { Call(name, cx, cy, false); }
            public void Set(string name, int cx, int cy) { Call(name, cx, cy, true); }

            void Call(string name, int cx, int cy, bool set)
            {
                IntPtr p = Marshal.StringToHGlobalUni(name);
                try
                {
                    var f = Info(p, cx, cy);
                    bool ok = set ? SetFormW(h, name, 1, ref f) : AddFormW(h, 1, ref f);
                    if (!ok) throw new Win32Exception();
                }
                finally { Marshal.FreeHGlobal(p); }
            }

            // False if there was no such form.
            public bool Delete(string name)
            {
                if (DeleteFormW(h, name)) return true;
                int e = Marshal.GetLastWin32Error();
                if (e == ERROR_INVALID_FORM_NAME) return false;
                throw new Win32Exception(e);
            }
        }
    }
}
