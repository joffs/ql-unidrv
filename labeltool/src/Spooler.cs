// Print queue access for the current user (no administrator rights): the jobs in a queue, the user's
// default paper size (label format) of a printer, and Windows' "Use Printer Offline" (clearing it needs them).
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace QlLabels
{
    sealed class JobInfo
    {
        public int Id;
        public string Document, StatusText, User;
        public int Status, PagesPrinted, TotalPages;
        public DateTime Submitted;  // local time

        public bool Finished { get { return (Status & (Spooler.JOB_STATUS_PRINTED | Spooler.JOB_STATUS_DELETED | Spooler.JOB_STATUS_COMPLETE)) != 0; } }
    }

    static class Spooler
    {
        public const int JOB_STATUS_PAUSED = 0x1, JOB_STATUS_ERROR = 0x2, JOB_STATUS_DELETING = 0x4, JOB_STATUS_SPOOLING = 0x8,
            JOB_STATUS_PRINTING = 0x10, JOB_STATUS_OFFLINE = 0x20, JOB_STATUS_PRINTED = 0x80, JOB_STATUS_DELETED = 0x100,
            JOB_STATUS_BLOCKED_DEVQ = 0x200, JOB_STATUS_COMPLETE = 0x1000;
        const int DM_OUT_BUFFER = 2, DM_IN_BUFFER = 8;
        const int DM_PAPERSIZE = 0x2, DM_PAPERLENGTH = 0x4, DM_PAPERWIDTH = 0x8, DM_FORMNAME = 0x10000;
        const int OffFields = 72, OffPaperSize = 78, OffFormName = 102, FormNameChars = 32;
        const int DC_PAPERS = 2, DC_PAPERNAMES = 16;

        [StructLayout(LayoutKind.Sequential)]
        struct SYSTEMTIME { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

        [StructLayout(LayoutKind.Sequential)]
        struct JOB_INFO_1
        {
            public int JobId;
            public IntPtr pPrinterName, pMachineName, pUserName, pDocument, pDatatype, pStatus;
            public int Status, Priority, Position, TotalPages, PagesPrinted;
            public SYSTEMTIME Submitted;
        }

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterW(string name, out IntPtr handle, IntPtr defaults);
        [DllImport("winspool.drv", SetLastError = true)]
        static extern bool ClosePrinter(IntPtr handle);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EnumJobsW(IntPtr h, int first, int count, int level, IntPtr buf, int size, out int needed, out int returned);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool GetPrinterW(IntPtr h, int level, IntPtr buf, int size, out int needed);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetPrinterW(IntPtr h, int level, IntPtr info, int command);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int DocumentPropertiesW(IntPtr hwnd, IntPtr h, string name, IntPtr outDm, IntPtr inDm, int mode);
        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern int DeviceCapabilitiesW(string device, string port, int capability, IntPtr output, IntPtr devMode);

        static IntPtr Open(string printer)
        {
            IntPtr h;
            if (!OpenPrinterW(printer, out h, IntPtr.Zero)) throw new Win32Exception();
            return h;
        }

        // ---------- "Use Printer Offline" ----------
        // Windows' switch in the print queue window (menu Printer): jobs then wait in this PC's queue, without an
        // error, and nothing reaches the printer. Windows may set it while a USB printer is missing, and it stays
        // when the queue is moved to another port (a share).
        const int PRINTER_ATTRIBUTE_WORK_OFFLINE = 0x400, PRINTER_ALL_ACCESS = 0xF000C, ERROR_ACCESS_DENIED = 5;

        [StructLayout(LayoutKind.Sequential)]
        struct PRINTER_DEFAULTS { public IntPtr pDatatype, pDevMode; public int DesiredAccess; }

        [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool OpenPrinterAccess(string name, out IntPtr handle, ref PRINTER_DEFAULTS defaults);

        public static bool WorkOffline(string printer)
        {
            IntPtr h = Open(printer);
            try
            {
                int needed;
                GetPrinterW(h, 4, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new Win32Exception();
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetPrinterW(h, 4, buf, needed, out needed)) throw new Win32Exception();
                    return (Marshal.ReadInt32(buf, 2 * IntPtr.Size) & PRINTER_ATTRIBUTE_WORK_OFFLINE) != 0;  // PRINTER_INFO_4.Attributes
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { ClosePrinter(h); }
        }

        // Clears "Use Printer Offline" (PRINTER_INFO_2 without the security descriptor, so it stays as it is). Needs
        // the right to manage the printer (administrators); false when Windows denies it.
        public static bool UseOnline(string printer)
        {
            var defaults = new PRINTER_DEFAULTS { DesiredAccess = PRINTER_ALL_ACCESS };
            IntPtr h;
            if (!OpenPrinterAccess(printer, out h, ref defaults))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == ERROR_ACCESS_DENIED) return false;
                throw new Win32Exception(error);
            }
            try
            {
                int needed;
                GetPrinterW(h, 2, IntPtr.Zero, 0, out needed);
                if (needed <= 0) throw new Win32Exception();
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!GetPrinterW(h, 2, buf, needed, out needed)) throw new Win32Exception();
                    int offAttributes = 13 * IntPtr.Size, attributes = Marshal.ReadInt32(buf, offAttributes);
                    if ((attributes & PRINTER_ATTRIBUTE_WORK_OFFLINE) == 0) return true;
                    Marshal.WriteInt32(buf, offAttributes, attributes & ~PRINTER_ATTRIBUTE_WORK_OFFLINE);
                    Marshal.WriteIntPtr(buf, 12 * IntPtr.Size, IntPtr.Zero);  // pSecurityDescriptor: unchanged
                    if (SetPrinterW(h, 2, buf, 0)) return true;
                    int error = Marshal.GetLastWin32Error();
                    if (error == ERROR_ACCESS_DENIED) return false;
                    throw new Win32Exception(error);
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { ClosePrinter(h); }
        }

        public static List<JobInfo> Jobs(string printer)
        {
            var jobs = new List<JobInfo>();
            IntPtr h = Open(printer);
            try
            {
                int needed, count;
                EnumJobsW(h, 0, 999, 1, IntPtr.Zero, 0, out needed, out count);
                if (needed <= 0) return jobs;
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!EnumJobsW(h, 0, 999, 1, buf, needed, out needed, out count)) throw new Win32Exception();
                    int size = Marshal.SizeOf(typeof(JOB_INFO_1));
                    for (int i = 0; i < count; i++)
                    {
                        var j = (JOB_INFO_1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * size), typeof(JOB_INFO_1));
                        var s = j.Submitted;
                        DateTime submitted;
                        try { submitted = new DateTime(s.Year, s.Month, s.Day, s.Hour, s.Minute, s.Second, DateTimeKind.Utc).ToLocalTime(); }
                        catch (ArgumentException) { submitted = DateTime.Now; }
                        jobs.Add(new JobInfo
                        {
                            Id = j.JobId, Status = j.Status, PagesPrinted = j.PagesPrinted, TotalPages = j.TotalPages, Submitted = submitted,
                            Document = Marshal.PtrToStringUni(j.pDocument) ?? "", User = Marshal.PtrToStringUni(j.pUserName) ?? "",
                            StatusText = Marshal.PtrToStringUni(j.pStatus),
                        });
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { ClosePrinter(h); }
            return jobs;
        }

        // ---------- the printers Windows lists ----------
        // A printer removed while something still holds it (the status service, a job) stays in the registry as
        // "<name>,0" until the spooler restarts, pending deletion (USB remove test 2026-11-06). Windows' own lists
        // (EnumPrinters, Get-Printer) leave it out; opening it fails with "invalid printer name". The local printers
        // that are not pending deletion, or null when Windows can't be asked.
        const int PRINTER_ENUM_LOCAL = 2, PRINTER_STATUS_PENDING_DELETION = 4;

        [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool EnumPrintersW(int flags, string name, int level, IntPtr buf, int size, out int needed, out int returned);

        public static HashSet<string> ListedPrinters()
        {
            try
            {
                int needed, count;
                EnumPrintersW(PRINTER_ENUM_LOCAL, null, 2, IntPtr.Zero, 0, out needed, out count);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (needed <= 0) return Marshal.GetLastWin32Error() == 0 ? names : null;
                IntPtr buf = Marshal.AllocHGlobal(needed);
                try
                {
                    if (!EnumPrintersW(PRINTER_ENUM_LOCAL, null, 2, buf, needed, out needed, out count)) return null;
                    int size = 13 * IntPtr.Size + 8 * 4;   // PRINTER_INFO_2: 13 pointers, 8 DWORDs
                    for (int i = 0; i < count; i++)
                    {
                        IntPtr p = new IntPtr(buf.ToInt64() + (long)i * size);
                        string name = Marshal.PtrToStringUni(Marshal.ReadIntPtr(p, IntPtr.Size));   // pPrinterName
                        int status = Marshal.ReadInt32(p, 13 * IntPtr.Size + 5 * 4);               // Status
                        if (name != null && (status & PRINTER_STATUS_PENDING_DELETION) == 0) names.Add(name);
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
                return names;
            }
            catch (Exception) { return null; }
        }

        // ---------- the user's default label format ----------
        // The per-user default lives in PRINTER_INFO_9 (the global one in PRINTER_INFO_2/8). The printing
        // preferences dialog stores dmPaperSize plus dmFormName with DM_FORMNAME; DocumentProperties' output
        // drops DM_FORMNAME and may carry a stale name, so the name is read from the stored DEVMODE.

        // The paper (form) name programs start with on this printer for the current user, or null.
        public static string DefaultFormName(string printer)
        {
            IntPtr h = Open(printer);
            IntPtr dm = IntPtr.Zero;
            try
            {
                dm = StoredDefault(h);
                if (dm == IntPtr.Zero) return null;
                int fields = Marshal.ReadInt32(dm, OffFields);
                string name = Marshal.PtrToStringUni(new IntPtr(dm.ToInt64() + OffFormName));
                if ((fields & DM_FORMNAME) != 0 && !string.IsNullOrEmpty(name)) return name;
                if ((fields & DM_PAPERSIZE) != 0)
                {
                    short paper = Marshal.ReadInt16(dm, OffPaperSize);
                    foreach (var p in Papers(printer)) if (p.Value == paper) return p.Key;
                }
                return null;
            }
            finally
            {
                if (dm != IntPtr.Zero) Marshal.FreeHGlobal(dm);
                ClosePrinter(h);
            }
        }

        // Makes a form the printer's default paper size for the current user (PRINTER_INFO_9, no
        // administrator rights), as the printing preferences dialog does. Programs then start with it.
        public static void SetDefaultForm(string printer, string form)
        {
            short paper = 0;
            foreach (var p in Papers(printer))
                if (string.Equals(p.Key, form, StringComparison.OrdinalIgnoreCase)) { paper = p.Value; form = p.Key; break; }
            if (paper == 0) throw new InvalidOperationException(printer + " doesn't offer the paper size '" + form + "'.");
            IntPtr h = Open(printer);
            IntPtr dm = IntPtr.Zero, valid = IntPtr.Zero, info = IntPtr.Zero;
            try
            {
                dm = StoredDefault(h);
                if (dm == IntPtr.Zero) throw new InvalidOperationException(Lang.T("The printer's default settings can't be read."));
                SelectForm(dm, paper, form);
                int size = DocumentPropertiesW(IntPtr.Zero, h, printer, IntPtr.Zero, IntPtr.Zero, 0);
                if (size <= 0) throw new Win32Exception();
                valid = Marshal.AllocHGlobal(size);
                if (DocumentPropertiesW(IntPtr.Zero, h, printer, valid, dm, DM_IN_BUFFER | DM_OUT_BUFFER) < 0) throw new Win32Exception();
                SelectForm(valid, paper, form);  // the validated copy loses DM_FORMNAME
                info = Marshal.AllocHGlobal(IntPtr.Size);
                Marshal.WriteIntPtr(info, valid);
                if (!SetPrinterW(h, 9, info, 0)) throw new Win32Exception();
            }
            finally
            {
                if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
                if (valid != IntPtr.Zero) Marshal.FreeHGlobal(valid);
                if (dm != IntPtr.Zero) Marshal.FreeHGlobal(dm);
                ClosePrinter(h);
            }
        }

        static void SelectForm(IntPtr dm, short paper, string form)
        {
            int fields = Marshal.ReadInt32(dm, OffFields);
            Marshal.WriteInt32(dm, OffFields, (fields | DM_PAPERSIZE | DM_FORMNAME) & ~(DM_PAPERLENGTH | DM_PAPERWIDTH));
            Marshal.WriteInt16(dm, OffPaperSize, paper);
            var name = new char[FormNameChars];
            form.CopyTo(0, name, 0, Math.Min(form.Length, FormNameChars - 1));
            Marshal.Copy(name, 0, new IntPtr(dm.ToInt64() + OffFormName), FormNameChars);
        }

        // A copy of the user's default DEVMODE (PRINTER_INFO_9), else the printer's (PRINTER_INFO_2); caller frees it.
        static IntPtr StoredDefault(IntPtr h)
        {
            IntPtr copy = CopyDevMode(h, 9, 0);
            return copy != IntPtr.Zero ? copy : CopyDevMode(h, 2, 7 * IntPtr.Size);
        }

        static IntPtr CopyDevMode(IntPtr h, int level, int offset)
        {
            int needed;
            GetPrinterW(h, level, IntPtr.Zero, 0, out needed);
            if (needed <= 0) return IntPtr.Zero;
            IntPtr buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (!GetPrinterW(h, level, buf, needed, out needed)) return IntPtr.Zero;
                IntPtr dm = Marshal.ReadIntPtr(buf, offset);
                if (dm == IntPtr.Zero) return IntPtr.Zero;
                int size = (ushort)Marshal.ReadInt16(dm, 68) + (ushort)Marshal.ReadInt16(dm, 70);
                var bytes = new byte[size];
                Marshal.Copy(dm, bytes, 0, size);
                IntPtr copy = Marshal.AllocHGlobal(size);
                Marshal.Copy(bytes, 0, copy, size);
                return copy;
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        // The printer's paper names and their dmPaperSize values (DeviceCapabilities, as programs see them).
        static List<KeyValuePair<string, short>> Papers(string printer)
        {
            var list = new List<KeyValuePair<string, short>>();
            int n = DeviceCapabilitiesW(printer, null, DC_PAPERNAMES, IntPtr.Zero, IntPtr.Zero);
            if (n <= 0) return list;
            IntPtr names = Marshal.AllocHGlobal(n * 64 * 2), ids = Marshal.AllocHGlobal(n * 2);
            try
            {
                DeviceCapabilitiesW(printer, null, DC_PAPERNAMES, names, IntPtr.Zero);
                DeviceCapabilitiesW(printer, null, DC_PAPERS, ids, IntPtr.Zero);
                for (int i = 0; i < n; i++)
                {
                    var chars = new char[64];
                    Marshal.Copy(new IntPtr(names.ToInt64() + i * 128), chars, 0, 64);
                    string name = new string(chars);
                    int nul = name.IndexOf('\0');
                    if (nul >= 0) name = name.Substring(0, nul);
                    list.Add(new KeyValuePair<string, short>(name, Marshal.ReadInt16(ids, i * 2)));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(names);
                Marshal.FreeHGlobal(ids);
            }
            return list;
        }
    }
}
