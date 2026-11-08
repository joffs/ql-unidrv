// PublishedStatus, part 3: reading, writing and deleting the published status in a queue's printer data (winspool),
// and the native calls of all three parts.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace QlLabels
{
    static partial class PublishedStatus
    {
        // ---------- printer data ----------

        // Reads the published status of a queue (local name or \\host\share); throws on access errors.
        public static string Read(string queue)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ACCESS_USE };
            if (!OpenPrinterW(queue, out h, ref defaults)) throw new Win32Exception();
            try { return ReadKey(h, Key); }
            finally { ClosePrinter(h); }
        }

        static string ReadKey(IntPtr h, string key)
        {
            {
                int type, needed;
                int e = GetPrinterDataExW(h, key, ValueName, out type, null, 0, out needed);
                if (e == ERROR_FILE_NOT_FOUND) return null;
                if (e != ERROR_MORE_DATA && e != 0) throw new Win32Exception(e);
                if (needed <= 0 || type != REG_SZ || needed > MaxBytes) return null;
                var buf = new byte[needed];
                e = GetPrinterDataExW(h, key, ValueName, out type, buf, needed, out needed);
                if (e != 0) throw new Win32Exception(e);
                if (type != REG_SZ) return null;
                return Encoding.Unicode.GetString(buf, 0, needed).TrimEnd('\0');
            }
        }

        // Writes the published status of a local queue (the service; needs administer rights on the queue).
        public static void Write(string printer, string text)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
            if (!OpenPrinterW(printer, out h, ref defaults)) throw new Win32Exception();
            try
            {
                var data = Encoding.Unicode.GetBytes(text + "\0");
                int e = SetPrinterDataExW(h, Key, ValueName, REG_SZ, data, data.Length);
                if (e != 0) throw new Win32Exception(e);
            }
            finally { ClosePrinter(h); }
        }

        // Removes it (the service's queue no longer has a USB printer, or the service is removed).
        public static void Delete(string printer)
        {
            IntPtr h;
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
            if (!OpenPrinterW(printer, out h, ref defaults)) return;
            try { DeletePrinterKeyW(h, Key); }
            finally { ClosePrinter(h); }
        }

        const int PRINTER_ACCESS_USE = 0x8, PRINTER_ALL_ACCESS = 0xF000C, REG_SZ = 1;
        const int ERROR_FILE_NOT_FOUND = 2, ERROR_MORE_DATA = 234;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct PRINTER_DEFAULTS { public IntPtr pDatatype, pDevMode; public int DesiredAccess; }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterW(string name, out IntPtr handle, ref PRINTER_DEFAULTS defaults);
        [DllImport("winspool.drv", SetLastError = true)]
        static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetPrinterW(IntPtr h, int level, IntPtr buf, int size, out int needed);
        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        static extern int NetShareGetInfo(string server, string share, int level, out IntPtr buf);
        [DllImport("netapi32.dll")]
        static extern int NetApiBufferFree(IntPtr buf);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int GetPrinterDataExW(IntPtr h, string key, string value, out int type, byte[] data, int size, out int needed);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int SetPrinterDataExW(IntPtr h, string key, string value, int type, byte[] data, int size);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode)]
        static extern int DeletePrinterKeyW(IntPtr h, string key);
    }
}
