<#
.SYNOPSIS
  Installs this driver package on a QL printer's USB device (-HardwareId and -Model, e.g. USBPRINT\BrotherQL-80053B7 and "Brother QL-800 (Unidrv)"),
  whatever Windows would rank higher.
  Run as administrator, with driver signature enforcement off (F7 boot), after the package is in
  the driver store (install.ps1 calls it).

.DESCRIPTION
  Packages installed with signature enforcement off rank as "not digitally signed", below
  Windows' generic driver (prnms013.inf). Without this step a clean install leaves the USB device
  on the generic driver: no USB port, no printer. UpdateDriverForPlugAndPlayDevices cannot be used:
  it also targets the printer queues' software devices (same hardware ID) and fails on them.
  This script selects the package's driver for exactly the USB device (SetupDi + DiInstallDevice).
#>
param(
    [Parameter(Mandatory)] [string]$Inf,              # the package's INF in the driver store
    [Parameter(Mandatory)] [string]$HardwareId,       # the model's USB hardware ID (install.ps1's model list)
    [Parameter(Mandatory)] [string]$Model             # its driver name
)
$ErrorActionPreference = "Stop"
if (-not ("QlUnidrv.DevBind" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
namespace QlUnidrv {
public static class DevBind {
    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVINFO_DATA { public int cbSize; public Guid ClassGuid; public int DevInst; public IntPtr Reserved; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SP_DRVINFO_DATA_V2 {
        public int cbSize; public int DriverType; public IntPtr Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string MfgName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string ProviderName;
        public long DriverDate; public ulong DriverVersion;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SP_DEVINSTALL_PARAMS {
        public int cbSize; public int Flags; public int FlagsEx; public IntPtr hwndParent;
        public IntPtr InstallMsgHandler; public IntPtr InstallMsgHandlerContext; public IntPtr FileQueue;
        public UIntPtr ClassInstallReserved; public int Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
    }
    const int DI_ENUMSINGLEINF = 0x00010000, SPDIT_COMPATDRIVER = 2, ERROR_NO_MORE_ITEMS = 259;
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr hwnd);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instanceId, IntPtr hwnd, int flags, ref SP_DEVINFO_DATA data);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiGetDeviceInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_DEVINSTALL_PARAMS p);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiSetDeviceInstallParamsW(IntPtr set, ref SP_DEVINFO_DATA data, ref SP_DEVINSTALL_PARAMS p);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiBuildDriverInfoList(IntPtr set, ref SP_DEVINFO_DATA data, int type);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool SetupDiEnumDriverInfoW(IntPtr set, ref SP_DEVINFO_DATA data, int type, int index, ref SP_DRVINFO_DATA_V2 drv);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("newdev.dll", SetLastError = true)]
    static extern bool DiInstallDevice(IntPtr hwnd, IntPtr set, ref SP_DEVINFO_DATA data, ref SP_DRVINFO_DATA_V2 drv, int flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);

    public static string Install(string instanceId, string inf, string model) {
        IntPtr set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try {
            var dev = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
            if (!SetupDiOpenDeviceInfoW(set, instanceId, IntPtr.Zero, 0, ref dev)) throw new Win32Exception();
            var p = new SP_DEVINSTALL_PARAMS { cbSize = Marshal.SizeOf(typeof(SP_DEVINSTALL_PARAMS)) };
            if (!SetupDiGetDeviceInstallParamsW(set, ref dev, ref p)) throw new Win32Exception();
            p.Flags |= DI_ENUMSINGLEINF;
            p.DriverPath = inf;
            if (!SetupDiSetDeviceInstallParamsW(set, ref dev, ref p)) throw new Win32Exception();
            if (!SetupDiBuildDriverInfoList(set, ref dev, SPDIT_COMPATDRIVER)) throw new Win32Exception();
            var drv = new SP_DRVINFO_DATA_V2 { cbSize = Marshal.SizeOf(typeof(SP_DRVINFO_DATA_V2)) };
            string seen = "";
            for (int i = 0; ; i++) {
                if (!SetupDiEnumDriverInfoW(set, ref dev, SPDIT_COMPATDRIVER, i, ref drv)) {
                    int e = Marshal.GetLastWin32Error();
                    if (e == ERROR_NO_MORE_ITEMS) throw new InvalidOperationException("No driver '" + model + "' for " + instanceId + " in " + inf + " (found: " + seen + ")");
                    throw new Win32Exception(e);
                }
                seen += drv.Description + "; ";
                if (drv.Description == model) break;
            }
            bool reboot;
            if (!DiInstallDevice(IntPtr.Zero, set, ref dev, ref drv, 0, out reboot)) throw new Win32Exception();
            return reboot ? "installed (restart needed)" : "installed";
        } finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
}
'@
}
$devices = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like "USBPRINT\*" -and $_.HardwareID -contains $HardwareId -and $_.Present })
if (-not $devices) { "No connected $Model ($HardwareId)."; return }
foreach ($d in $devices) {
    $result = [QlUnidrv.DevBind]::Install($d.InstanceId, $Inf, $Model)
    "Device $($d.InstanceId): $Model $result."
}
