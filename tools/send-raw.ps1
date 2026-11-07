<#
.SYNOPSIS
  Sends a .prn file unchanged (RAW datatype) to a printer queue, e.g. an encoder-made stream for a
  test on the real printer before the driver supports it. No administrator rights needed.
.EXAMPLE
  tools\send-raw.ps1 "Brother QL-800 (Unidrv)" reference\captures\ql800\<case>.prn
#>
param(
    [Parameter(Mandatory)][string]$Printer,
    [Parameter(Mandatory)][string]$File,
    [string]$DocName = "raw test"
)
$ErrorActionPreference = "Stop"
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class RawPrint {
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public class DOC_INFO_1 { public string pDocName; public string pOutputFile; public string pDatatype; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr h, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int StartDocPrinter(IntPtr h, int level, DOC_INFO_1 di);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndDocPrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool StartPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)] static extern bool EndPagePrinter(IntPtr h);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool WritePrinter(IntPtr h, byte[] buf, int count, out int written);
    public static int Send(string printer, string doc, byte[] data) {
        IntPtr h;
        if (!OpenPrinter(printer, out h, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception();
        try {
            int job = StartDocPrinter(h, 1, new DOC_INFO_1 { pDocName = doc, pDatatype = "RAW" });
            if (job == 0) throw new System.ComponentModel.Win32Exception();
            StartPagePrinter(h);
            int written;
            if (!WritePrinter(h, data, data.Length, out written)) throw new System.ComponentModel.Win32Exception();
            EndPagePrinter(h);
            EndDocPrinter(h);
            return written;
        } finally { ClosePrinter(h); }
    }
}
"@
$data = [IO.File]::ReadAllBytes((Resolve-Path $File))
$n = [RawPrint]::Send($Printer, $DocName, $data)
"Sent $n of $($data.Length) bytes to '$Printer'."
