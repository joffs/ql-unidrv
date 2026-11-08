<#
.SYNOPSIS
  Installs or updates the QL Unidrv driver (all 18 models, QL-500 ... QL-1115NWB; ARM64 or x64) from
  ..\out\package and the label tool from ..\out\tools. Run as administrator after package.ps1.

.DESCRIPTION
  Installing a driver package needs driver signature enforcement off: the package is signed by a
  private CA (package.ps1), which Windows on ARM64 only accepts in test mode or after a one-time
  "Disable driver signature enforcement" boot (see the README; x64 accepts the package with
  enforcement on once the CA is trusted). The installed
  driver keeps working afterwards with Secure Boot on. Label formats never need this script: the
  label tool changes them without touching the driver.

  Default: clean install - removes printers using the driver, the driver, all our packages and the QL printers' USB
  device nodes, then adds the package (pnputil) and registers the driver. Each connected QL printer (any of
  the 18 models) is then bound to the new package and gets a printer "Brother QL-xxx (Unidrv)"
  (printer settings start from the driver defaults; sharing has to be set again).

  -Update: in-place upgrade - adds the new package and re-registers the driver; existing printers
  and their settings are kept; unused older packages are removed. Connected QL printers of every model are bound
  to the new package.

  Both also:
  - replace older certificates of this driver in LocalMachine\Root / TrustedPublisher by the
    package's (code-signing-only root CA),
  - set up C:\ProgramData\QLUnidrv (owner Administrators; administrators and SYSTEM write, users read,
    also in log\: the driver writes there with the spooler's rights, so users must not be able to
    redirect it; status\ is the one folder users can write to: the label tool's printer-status log,
    never written by the driver),
  - install the programs and their help pages (help\) to C:\Program Files\QLUnidrv with the Start menu entries "QL Label
    Formats", "QL Printer Status" and "QL Label Maker" and create the Windows forms (QlLabelFormats.exe --sync; when
    C:\ProgramData\QLUnidrv\labels.json is missing it is seeded from Brother's factory list: labels\default-labels.json
    for a German display language, else labels\default-labels-en-us.json when Windows measures in US units, else
    labels\default-labels-en.json, the UK list).

  -TestPrinter (developers only, not for a normal install): adds the printer "QL-800 Capture" on a local file
  port (C:\ProgramData\QLUnidrv\capture\capture.prn) for the source tree's driver\compare.ps1; nothing is printed.
  -UsbPrinter: adds the printer -UsbDriverName (or -DriverName) on that
  model's USB port (or -UsbPort USB001), for the
  case that Windows did not create the printer itself (the install also does this on its own), or it was removed
  (the Printer setup window offers it for a connected QL printer without one; never shared, the USB device is not
  rebound, so this needs no maintenance window, also not on ARM64).
  -SharePort \\host\share: adds the printer -DriverName (required, e.g. "Brother QL-570 (Unidrv)") on a Local
  Port with that name (a QL printer shared by another PC, docs/sharing.md); an existing port is reused.
  A printer of that name on another port (e.g. its USB printer) is kept: the share's printer is then
  named "<driver name> on <host>". With -AppOnly the driver must already be installed (no driver
  install, so no maintenance window on ARM64).
  -NetworkPrinter <IP address or host name>: adds a QL printer on the network (Ethernet / Wi-Fi) on a
  Standard TCP/IP port "IP_<address>" (raw, port 9100). The model is asked from the printer (SNMP, as the
  printer status does; QlLabelFormats.exe --identify); -DriverName overrides it (needed when the printer doesn't
  answer SNMP). Named like its driver, or "<driver name> on <address>" if that name is taken by another port.
  -AppOnly as with -SharePort.
  -RemovePrinter <name>: removes one QL printer (our driver, not one redirected by Remote Desktop) with its waiting
  jobs, and its port when no other printer uses it (IP_<address>, \\host\share, a file; never a USB port). The
  driver package stays, also for the last printer of a model. A USB printer doesn't come back by itself (tests
  2026-11-06). The status service is stopped meanwhile and a shared printer is unshared first; a printer another
  program still holds stays pending deletion until the spooler restarts (result key "pending"). Usually with
  -Settings (the Printer setup window).
  -SharePrinter <name> -ShareName <share name>: shares one QL printer on a USB port (our driver) for other PCs under
  that name, or renames the share of one that is shared already. The name: 1-80 characters, none of \ / , and
  not taken by another share on this PC (printer or folder). Result keys shared, sharename (and renamed-from).
  Windows' file and printer sharing must be on for other PCs to reach it (not changed here).
  -UnsharePrinter <name>: stops sharing one QL printer (result key unshared). Both usually with -Settings.
  USB devices bound to another vendor's driver package (e.g. Brother's own driver) are
  never removed or rebound.
  -AppOnly: only installs/updates the programs and their help (and applies the label formats); the driver,
  its package and the certificates are not touched, so this needs no maintenance window.
  -Settings: only changes settings of an installed setup (-StatusService, -Logging, -NetworkPrinter,
  -SharePort, -UsbPrinter, -RemovePrinter, -SharePrinter, -UnsharePrinter): driver, label tool files and label formats stay as they are; works from the installed copy
  (C:\Program Files\QLUnidrv\setup\install.ps1) and while the label tool is open (its setup window uses it).
  Every install/update also writes the entry "QL Unidrv printer driver" in Windows' Installed apps
  (uninstall from there) and copies install.ps1 and setup.ps1 to C:\Program Files\QLUnidrv\setup; docs/setup.md.
  -Uninstall: removes printers, driver, packages, the label tool (and its printer monitor's
  autostart for the current user), its Windows forms, the certificates and C:\ProgramData\QLUnidrv
  (logs, capture file, label list).
  -KeepLabels (with -Uninstall): keep the label list (C:\ProgramData\QLUnidrv\labels.json) for a later install.
  -Logging on|off: the driver's diagnostic log C:\ProgramData\QLUnidrv\log\plugin.log (off after a fresh
  install; without -Logging the current setting stays). For troubleshooting: -AppOnly -Logging on (no
  maintenance window).
  -StatusService on|off: the optional Windows service "QL printer status" (QlPrinterStatus.exe --service, runs as
  SYSTEM): reads this PC's USB QL printers every 3 s and publishes their status in the print queue, so PCs
  printing through the printer's share (and every user here) see it; docs/status-service.md. Off after a fresh
  install; without -StatusService the current setting stays (also with -AppOnly). The uninstall removes it.
  -Log <file>: also write all output to this file (used when started elevated by another script).
  -ResultFile <file>: what was done as key=value lines (printer, driver, address, port, share, roll, removed, pending, pending-port, was-shared, usb,
  shared, sharename, renamed-from, unshared, service,
  logging, logfile, done=1); the label tool's Printer setup window shows it. While it runs: step=<id> lines
  (certificates, package, printers, app, labels; the setup window's progress); on failure error=<message> and
  left-port=<port> / left-paper=<form> (uninstall: what Windows didn't let go); code=<class> (not-admin, label-tool-open, busy, enforcement-on, pnputil, no-package, no-printer,
  port, rdp-printer, other). One run at a time: a second one (another setup window, the Printer setup) stops with code=busy.
#>
param(
    [switch]$Update,
    [switch]$TestPrinter,
    [switch]$UsbPrinter,
    [string]$UsbPort,
    [string]$UsbDriverName,
    [string]$SharePort,
    [string]$NetworkPrinter,
    [string]$RemovePrinter,
    [string]$SharePrinter,
    [string]$ShareName,
    [string]$UnsharePrinter,
    [switch]$Uninstall,
    [switch]$AppOnly,
    [switch]$Settings,
    [switch]$KeepLabels,
    [ValidateSet("on", "off")] [string]$Logging,
    [ValidateSet("on", "off")] [string]$StatusService,
    [string]$Log,
    [string]$ResultFile,
    [string]$DriverName
)
$ErrorActionPreference = "Stop"
# The script's own parameters: inside a function $PSBoundParameters holds that function's (Invoke-PrinterActions)
$scriptParameters = $PSBoundParameters
# Started as 32-bit PowerShell on a 64-bit Windows (from a 32-bit program): run again in the native PowerShell, so
# System32, the registry and the driver tools are the real ones. Only strings and switches here.
if ([IntPtr]::Size -eq 4 -and $env:PROCESSOR_ARCHITEW6432) {
    $native = Join-Path $env:WINDIR "Sysnative\WindowsPowerShell\v1.0\powershell.exe"
    $argv = foreach ($k in $PSBoundParameters.Keys) {
        $v = $PSBoundParameters[$k]
        if ($v -is [System.Management.Automation.SwitchParameter]) { if ($v) { "-$k" } } else { "-$k"; "$v" }
    }
    & $native -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath @argv
    exit $LASTEXITCODE
}
if ($Log) { Start-Transcript -Path $Log -Force | Out-Null }
# -ResultFile: what was done as key=value lines (UTF-8), for the label tool's Printer setup window; the console
# output stays for people.
if ($ResultFile) { Set-Content -LiteralPath $ResultFile -Value @() -Encoding UTF8 }
function Add-Result([string]$key, [string]$value) {
    if ($ResultFile) { Add-Content -LiteralPath $ResultFile -Value "$key=$value" -Encoding UTF8 }
}
# step=<id> while it runs (the setup window shows the current step): certificates, package, printers, app, labels
function Set-Step([string]$id) { Add-Result "step" $id }
# code=<class> of a failure, for the setup window's plain-language explanation
function Get-ErrorCode([string]$message) {
    switch -regex ($message) {
        "as administrator" { return "not-admin" }
        "QL Label (Formats|Maker)\b.* is open" { return "label-tool-open" }   # "QL Label Formats or QL Label Maker is open"
        "Another setup is running" { return "busy" }
        "^pnputil failed.*0x800B0109" { return "enforcement-on" }
        "^pnputil failed" { return "pnputil" }
        "has no driver for this PC" { return "no-package" }
        "No USB port of a" { return "no-printer" }
        "^Printers redirected by a Remote Desktop session" { return "rdp-printer" }
        "^Could not add the port" { return "port" }   # Add-LocalPort
        # -Settings (QL Printer setup)
        "^No printer can be reached as" { return "share-unreachable" }
        "^Driver '.*' is not installed here|^The driver and the programs are not installed here" { return "no-driver" }
        "is not a QL printer with this driver on this PC" { return "not-ours" }
        "^Only a printer on a USB port is shared here" { return "share-usb-only" }
        "^Could not remove the printer" { return "remove-failed" }
    }
    return "other"
}
# A printer share (\\host\share) answers: the spooler there opens it with this user's credentials.
function Test-PrinterShare([string]$share) {
    if (-not ("QlShare" -as [type])) {
        Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class QlShare {
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, IntPtr d);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
    public static bool Reachable(string name) { IntPtr h; if (!OpenPrinterW(name, out h, IntPtr.Zero)) return false; ClosePrinter(h); return true; }
}
"@
    }
    [QlShare]::Reachable($share)
}
# Clears "Use Printer Offline": a printer moved off a missing USB device keeps it, and its jobs then wait in the
# queue with no error. Set-Printer has no switch for it.
function Clear-WorkOffline([string]$name) {
    $printer = Get-CimInstance Win32_Printer -Filter "Name='$($name.Replace('\', '\\').Replace("'", "\'"))'" -ErrorAction SilentlyContinue
    if ($printer -and $printer.WorkOffline) {
        $printer | Set-CimInstance -Property @{ WorkOffline = $false } -ErrorAction Stop
        "Printer '$name' was set to 'Use Printer Offline'; switched back to online."
    }
}
# Thumbprints of the certificates our packages in Windows' driver store are signed with: each catalog's signer and
# the root of its chain (the catalog carries its chain, so an untrusted root is found too). Older certificates of
# this driver are removed only when no package there uses them: a package Windows knew already keeps its first
# catalog, whose CA a new signing run doesn't replace.
function Get-StoreSigningThumbprints([string]$repository = "$env:WINDIR\System32\DriverStore\FileRepository") {
    $prints = @()
    foreach ($d in @(Get-ChildItem $repository -Directory -ErrorAction SilentlyContinue | Where-Object Name -match "^qlunidrv\.inf_")) {
        $cat = Join-Path $d.FullName ($d.Name -replace "\.inf_.*", ".cat")
        if (-not (Test-Path $cat)) { continue }
        $signer = (Get-AuthenticodeSignature $cat).SignerCertificate
        if (-not $signer) { continue }
        $chain = New-Object Security.Cryptography.X509Certificates.X509Chain
        $chain.ChainPolicy.RevocationMode = "NoCheck"
        try { $chain.ChainPolicy.ExtraStore.Import($cat) } catch { }
        [void]$chain.Build($signer)
        $prints += $signer.Thumbprint
        $prints += $chain.ChainElements[$chain.ChainElements.Count - 1].Certificate.Thumbprint
    }
    @($prints | Select-Object -Unique)
}

# Local Ports the way the spooler's port dialog adds and deletes them: XcvData "AddPort" / "DeletePort" on the Local
# Port monitor
function Add-QlXcvType {
    if (-not ("QlXcv" -as [type])) {
        Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices; using System.Text;
public static class QlXcv {
    [StructLayout(LayoutKind.Sequential)] struct Defaults { public IntPtr DataType, DevMode; public int Access; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref Defaults d);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool XcvDataW(IntPtr h, string name, byte[] input, int cbInput, IntPtr output, int cbOutput, out int needed, out int status);
    public static int DeletePort(string port) { return Call("DeletePort", port); }
    public static int AddPort(string port) { return Call("AddPort", port); }
    static int Call(string what, string port) {
        var d = new Defaults { Access = 1 };   // SERVER_ACCESS_ADMINISTER
        IntPtr h;
        if (!OpenPrinterW(",XcvMonitor Local Port", out h, ref d)) return Marshal.GetLastWin32Error();
        try {
            byte[] input = Encoding.Unicode.GetBytes(port + "\0");
            int needed, status;
            if (!XcvDataW(h, what, input, input.Length, IntPtr.Zero, 0, out needed, out status)) return Marshal.GetLastWin32Error();
            return status;
        } finally { ClosePrinter(h); }
    }
}
"@
    }
}

# Adds a Local Port (a file path, or a share \\host\printer). Windows checks a file port's path when the port is added,
# so its folder must exist (Add-PrinterPort fails with a generic CIM error for a port in a folder not made yet). A failure is tried twice more (the spooler may be busy right after a driver
# change), then through XcvData "AddPort"; the message names the port and both errors.
function Add-LocalPort([string]$port) {
    if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { return }
    if ($port -match "^[A-Za-z]:\\") {
        $dir = Split-Path $port
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "Could not add the port ${port}: its folder $dir does not exist." }
    }
    $err = $null
    for ($i = 0; $i -lt 3; $i++) {
        if ($i) { Start-Sleep -Seconds 2 }
        try { Add-PrinterPort -Name $port -ErrorAction Stop; return } catch { $err = $_.Exception.Message }
        if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { return }
    }
    Add-QlXcvType
    $status = [QlXcv]::AddPort($port)
    if (($status -eq 0 -or $status -eq 183) -and (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) {   # 183: exists
        "Port $port added through the port monitor (Add-PrinterPort failed: $err)."
        return
    }
    throw "Could not add the port ${port}. Add-PrinterPort: $err Port monitor (XcvData AddPort): $((New-Object ComponentModel.Win32Exception $status).Message)"
}

# Deletes a Local Port whose name is a file path. Remove-PrinterPort rejects "\" in names (0x80070709, with -Name
# and by object), so it goes the way the spooler's port dialog does: XcvData "DeletePort".
function Remove-FilePort([string]$port) {
    Add-QlXcvType
    if (-not (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue)) { return }
    $status = [QlXcv]::DeletePort($port)
    if ($status) { throw (New-Object ComponentModel.Win32Exception $status).Message }
    "Removed the port $port."
}
$script:addedCerts = @()
try {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this script as administrator."
    }
    # One run at a time: a setup window opened again while the first one's install still runs (its window closed) would
    # start a second install next to it. The setup window checks the same name.
    $script:runMutex = New-Object Threading.Mutex($false, "Global\QLUnidrvInstall")
    try { $owned = $script:runMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned = $true }   # a run that crashed
    if (-not $owned) { $script:runMutex.Dispose(); $script:runMutex = $null; throw "Another setup is running on this PC: wait until it has finished, then try again." }
    $repo = Split-Path $PSScriptRoot -Parent
    $pkg = Join-Path $repo "out\package"
    $data = Join-Path $env:ProgramData "QLUnidrv"
    $appDir = Join-Path $env:ProgramFiles "QLUnidrv"
    $app = Join-Path $appDir "QlLabelFormats.exe"
    # Three programs: the label tool, the label editor and the printer status (window, monitor, service).
    $editorApp = Join-Path $appDir "QlLabelMaker.exe"
    $statusApp = Join-Path $appDir "QlPrinterStatus.exe"
    $appNames = @("QlLabelFormats.exe", "QlLabelMaker.exe", "QlPrinterStatus.exe")
    # Icons as files (csc embeds one icon per exe): the base mark for Installed apps, the .qllabel file icon.
    # Without them the exes' icons are used.
    $appIcons = @("qlunidrv.ico", "qllabel.ico")
    $shortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\QL Label Formats.lnk"
    $statusShortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\QL Printer Status.lnk"
    $editorShortcut = Join-Path $env:ProgramData "Microsoft\Windows\Start Menu\Programs\QL Label Maker.lnk"
    $capturePort = Join-Path $data "capture\capture.prn"
    $rootPattern = "CN=QL Unidrv driver root CA*"
    $signerSubject = "CN=QL Unidrv driver"
    # The package's models by model number: driver name and USB hardware ID. -DriverName picks the model for
    # -UsbPrinter / -SharePort ($mainModel).
    $models = @(
        @{ Name = "Brother QL-500 (Unidrv)"; Id = "USBPRINT\BrotherQL-5009026" },
        @{ Name = "Brother QL-550 (Unidrv)"; Id = "USBPRINT\BrotherQL-550C025" },
        @{ Name = "Brother QL-560 (Unidrv)"; Id = "USBPRINT\BrotherQL-5603025" },
        @{ Name = "Brother QL-570 (Unidrv)"; Id = "USBPRINT\BrotherQL-570A024" },
        @{ Name = "Brother QL-580N (Unidrv)"; Id = "USBPRINT\BrotherQL-580N2C10" },
        @{ Name = "Brother QL-600 (Unidrv)"; Id = "USBPRINT\BrotherQL-60090D6" },
        @{ Name = "Brother QL-650TD (Unidrv)"; Id = "USBPRINT\BrotherQL-650TD3360" },
        @{ Name = "Brother QL-700 (Unidrv)"; Id = "USBPRINT\BrotherQL-7005085" },
        @{ Name = "Brother QL-710W (Unidrv)"; Id = "USBPRINT\BrotherQL-710W9D81" },
        @{ Name = "Brother QL-720NW (Unidrv)"; Id = "USBPRINT\BrotherQL-720NW4A17" },
        @{ Name = "Brother QL-800 (Unidrv)"; Id = "USBPRINT\BrotherQL-80053B7" },
        @{ Name = "Brother QL-810W (Unidrv)"; Id = "USBPRINT\BrotherQL-810W4803" },
        @{ Name = "Brother QL-820NWB (Unidrv)"; Id = "USBPRINT\BrotherQL-820NWBC1AA" },
        @{ Name = "Brother QL-1050 (Unidrv)"; Id = "USBPRINT\BrotherQL-1050AE13" },
        @{ Name = "Brother QL-1060N (Unidrv)"; Id = "USBPRINT\BrotherQL-1060NF99F" },
        @{ Name = "Brother QL-1100 (Unidrv)"; Id = "USBPRINT\BrotherQL-11003E41" },
        @{ Name = "Brother QL-1110NWB (Unidrv)"; Id = "USBPRINT\BrotherQL-1110NWBA2A3" },
        @{ Name = "Brother QL-1115NWB (Unidrv)"; Id = "USBPRINT\BrotherQL-1115NWB6EA3" }
    )
    $mainModel = if ($DriverName) { $models | Where-Object { $_.Name -eq $DriverName } | Select-Object -First 1 }
    if ($DriverName -and -not $mainModel) { throw "Unknown -DriverName '$DriverName'. One of: $(($models | ForEach-Object Name) -join ', ')." }
    $allDrivers = @($models | ForEach-Object Name)
    $captureName = "QL-800 Capture"
    # The package INF; driver store folder qlunidrv.inf_arm64_* or qlunidrv.inf_amd64_*.
    $infName = "qlunidrv.inf"
    $ourInf = "qlunidrv\.inf"
    # The PC's architecture from the registry: the environment is the starting program's, and a .NET AnyCPU program on
    # ARM64 has PROCESSOR_ARCHITECTURE=AMD64 there.
    $storeArch = if ((Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Environment').PROCESSOR_ARCHITECTURE -eq "ARM64") { "arm64" } else { "amd64" }
    # The printer monitor's autostart (per user, HKCU\...\Run)
    $monitorRunName = "QL printer monitor"
    # The user hives loaded now (signed-in users): HKCU here is the account that confirmed the administrator prompt, which
    # is not the signed-in user when a standard user typed an administrator's password.
    function Get-UserHives {
        @([Microsoft.Win32.Registry]::Users.GetSubKeyNames() | Where-Object { $_ -match "^S-1-5-21-[\d-]+$" } |
            ForEach-Object { "Registry::HKEY_USERS\$_" })
    }
    # Is this script running as the user signed in to this session (whose Explorer runs here)?
    function Test-SignedInUser {
        $me = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        $session = (Get-Process -Id $PID).SessionId
        foreach ($p in @(Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" -ErrorAction SilentlyContinue | Where-Object SessionId -eq $session)) {
            $owner = Invoke-CimMethod -InputObject $p -MethodName GetOwnerSid -ErrorAction SilentlyContinue
            if ($owner -and $owner.Sid -eq $me) { return $true }
        }
        $false
    }

    # A running program locks its exe. The label tool's and the label editor's windows may have unsaved changes, so
    # the user has to close them (checked before anything is changed). The printer monitor (--monitor, which may
    # show the status window) and status windows hold nothing to save: they are asked to close and stopped if they
    # don't. The monitor starts again afterwards.
    function Get-LabelTools {   # not the service (--service): Stop-StatusService
        $filter = ($appNames | ForEach-Object { "Name='$_'" }) -join " OR "
        @(Get-CimInstance Win32_Process -Filter $filter |
            Where-Object { $_.ExecutablePath -like "$appDir\*" -and $_.CommandLine -notmatch "--service" })
    }
    # The printer status program (window, monitor): nothing to save there
    function Test-StatusProcess($p) { $p.Name -eq "QlPrinterStatus.exe" }
    function Assert-LabelToolClosed {
        $open = @(Get-LabelTools | Where-Object { -not (Test-StatusProcess $_) })
        if ($open) { throw "QL Label Formats or QL Label Maker is open (pid $(($open.ProcessId) -join ', ')): save your changes, close it, then run this again." }
    }
    function Stop-LabelTool {
        Assert-LabelToolClosed
        $procs = Get-LabelTools
        if (-not $procs) { return }
        if ($procs | Where-Object { $_.CommandLine -match "--monitor" }) { $script:monitorStopped = $true }
        # The monitor exits at its next poll (3 s) when this event is set, once its status window is closed.
        try {
            $ev = [Threading.EventWaitHandle]::OpenExisting("Local\QLUnidrv.PrinterMonitor.Exit")
            [void]$ev.Set(); $ev.Dispose()
        } catch { }
        foreach ($p in $procs) {
            $proc = Get-Process -Id $p.ProcessId -ErrorAction SilentlyContinue
            if (-not $proc) { continue }
            [void]$proc.CloseMainWindow()   # status window (none: no effect)
            if ($proc.WaitForExit(10000)) { "Closed the printer monitor / status window (pid $($p.ProcessId))."; continue }
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            [void]$proc.WaitForExit(5000)
            "Stopped the printer monitor / status window (pid $($p.ProcessId))."
        }
    }

    # The optional service "QL printer status" (QlPrinterStatus.exe --service; docs/status-service.md).
    $serviceName = "QLStatus"
    function Get-ServiceCommand { "`"$statusApp`" --service" }
    # A service that fails to stop (or hangs while stopping) must not stop an update or uninstall: after 60 s its
    # process is ended, so its exe is free (2026-11-06: a removed printer made its stop fail).
    function Stop-StatusService {
        $s = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
        if (-not $s -or $s.Status -eq "Stopped") { return }
        try {
            if ($s.Status -ne "StopPending") { $s.Stop() }
            $s.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(60))
            "Stopped the service 'QL printer status'."
            return
        } catch { }
        $svcPid = (Get-CimInstance Win32_Service -Filter "Name='$serviceName'").ProcessId
        if ($svcPid) {
            Stop-Process -Id $svcPid -Force -ErrorAction SilentlyContinue
            Wait-Process -Id $svcPid -Timeout 10 -ErrorAction SilentlyContinue
        }
        try { $s.WaitForStatus("Stopped", [TimeSpan]::FromSeconds(10)) } catch { }
        $s.Refresh()
        if ($s.Status -ne "Stopped") { throw "The service 'QL printer status' could not be stopped (state $($s.Status))." }
        "The service 'QL printer status' didn't stop by itself; its process was ended."
    }
    function Install-StatusService {
        if (-not (Get-Service -Name $serviceName -ErrorAction SilentlyContinue)) {
            New-Service -Name $serviceName -BinaryPathName (Get-ServiceCommand) -DisplayName "QL printer status" -StartupType Automatic `
                -Description "Reads the Brother QL printers of this PC (USB, and network printers with SNMP) and publishes their status (roll, errors) in their print queues, also for PCs that print through their shares. Installed by the QL Unidrv driver (install.ps1 -StatusService on|off)." | Out-Null
            "Service 'QL printer status' installed."
        } else {
            # An existing service with another command (e.g. a moved folder) is pointed here through the service
            # manager (it reads the registry only at startup); its privileges and SID type below stay as set.
            $svc = Get-CimInstance Win32_Service -Filter "Name='$serviceName'"
            $command = Get-ServiceCommand
            if ($svc.PathName -ne $command) {
                $r = Invoke-CimMethod -InputObject $svc -MethodName Change -Arguments @{ PathName = $command }
                if ($r.ReturnValue) { throw "Could not point the service 'QL printer status' to $command (error $($r.ReturnValue))." }
                "Service 'QL printer status' now runs $command."
            }
        }
        sc.exe config $serviceName start= delayed-auto | Out-Null
        sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000// | Out-Null
        # Only the privileges it needs, and its own service SID: it reads devices,
        # the registry and the network and writes printer data, none of which needs a privilege.
        sc.exe privs $serviceName SeChangeNotifyPrivilege | Out-Null
        sc.exe sidtype $serviceName unrestricted | Out-Null
        Start-Service -Name $serviceName
        "Service 'QL printer status' running."
    }
    # The service's event log source (written by .NET's ServiceBase at its first start) goes with it
    $eventSourceKey = "HKLM:\SYSTEM\CurrentControlSet\Services\EventLog\Application\$serviceName"
    function Remove-StatusService {
        if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
            Stop-StatusService
            sc.exe delete $serviceName | Out-Null
            "Service 'QL printer status' removed."
        }
        Remove-Item $eventSourceKey -Recurse -Force -ErrorAction SilentlyContinue
    }

    # Copies a file of the label tool into $appDir. An exe can stay locked for a moment after its process
    # ended (or while a virus scanner reads it): retry, then rename the locked file aside (Windows allows
    # that for an exe in use) and copy; the renamed file is removed at the next install.
    function Install-AppFile([string]$source) {
        $target = Join-Path $appDir (Split-Path $source -Leaf)
        for ($i = 0; $i -lt 10; $i++) {
            try { Copy-Item $source $target -Force; return } catch { Start-Sleep -Milliseconds 500 }
        }
        Rename-Item $target ("{0}.old-{1:yyyyMMddHHmmss}" -f (Split-Path $target -Leaf), (Get-Date))
        Copy-Item $source $target -Force
        "$(Split-Path $target -Leaf) was still in use: the old copy was renamed and is removed at the next install."
    }

    # Creates a folder of the data area, or checks an existing one: a junction or link there (made by
    # someone else before the folder was protected) is removed, never followed; only the link goes, not
    # what it points to.
    function New-SafeDirectory([string]$path) {
        $item = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            Write-Host "$path was a link to another place: removed (only the link)." -ForegroundColor Yellow
            [IO.Directory]::Delete($path, $false)
            $item = $null
        }
        if ($item -and -not $item.PSIsContainer) { throw "$path is a file, not a folder: remove it and run the install again." }
        if (-not $item) { New-Item -ItemType Directory -Force $path | Out-Null }
    }

    # status\ and capture\, the folders users write to (the label tool's status log; the spooler writes a file
    # port as the user who printed): users may create files there and change or delete any file, but not create
    # folders, so there is no folder a user could swap for a junction while an administrator deletes the data
    # area. Folders found there are removed first.
    function Set-UserFilesFolder([string]$dir) {
        New-SafeDirectory $dir
        Get-ChildItem -LiteralPath $dir -Force -Directory | ForEach-Object { Remove-DataItem $_ }
        icacls $dir /reset | Out-Null
        icacls $dir /grant "*S-1-5-32-545:(RX,WD)" "*S-1-5-32-545:(OI)(IO)(M)" | Out-Null
    }
    # Before deleting them: users lose their rights there first, so nothing changes while it is deleted.
    function Lock-UserFilesFolder([string]$dir) {
        $item = Get-Item -LiteralPath $dir -Force -ErrorAction SilentlyContinue
        if (-not $item -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) { return }
        icacls $dir /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" | Out-Null
    }

    # Deletes a file or folder of the data area; links inside are removed themselves, never followed
    # (Remove-Item -Recurse would delete what a junction points to).
    function Remove-DataItem($item) {
        try {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                if ($item.PSIsContainer) { [IO.Directory]::Delete($item.FullName, $false) } else { [IO.File]::Delete($item.FullName) }
                return
            }
            if ($item.PSIsContainer) { Get-ChildItem -LiteralPath $item.FullName -Force | ForEach-Object { Remove-DataItem $_ } }
            Remove-Item -LiteralPath $item.FullName -Force -ErrorAction SilentlyContinue
        } catch { }
    }

    # The entry in Windows' "Installed apps" (Settings > Apps; German: Installierte Apps): "QL Unidrv printer
    # driver" with Uninstall (and Modify: the label tool's setup window). The scripts it needs are copied to
    # C:\Program Files\QLUnidrv\setup (users read only), so it works without the zip; docs/setup.md.
    $uninstallKey = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QLUnidrv"
    $setupDir = Join-Path $appDir "setup"
    # The releases (labeltool\version\VERSION, driver\VERSION): a driver's "0.18.304.1114" -> "0.18" (the last two parts
    # are the build stamp), the programs' product version "0.18.1 (build 305.700)" -> "0.18.1" (a build from before the
    # split: "0.18.304.1157" -> "0.18"). Installed apps shows the programs' release (every run updates them, -AppOnly
    # too); the driver's is the value DriverVersion next to it.
    # The same two functions are in setup.ps1 (each script is copied alone): change both.
    function Get-Release([string]$version) {
        if ($version -match "^(\d+)\.(\d+)") { "$($Matches[1]).$($Matches[2])" }
    }
    function Get-AppRelease([string]$product) {
        if ($product -match "^(\d+\.\d+\.\d+) \(build \d+\.\d+\)$") { $Matches[1] } else { Get-Release $product }
    }
    function Register-InstalledApp {
        New-Item -ItemType Directory -Force $setupDir | Out-Null
        $setupScript = Join-Path $PSScriptRoot "setup.ps1"   # release zip: advanced\driver\setup.ps1
        if (-not (Test-Path $setupScript)) { $setupScript = Join-Path $repo "tools\release\setup.ps1" }   # source tree
        Copy-Item $PSCommandPath, $setupScript (Join-Path $setupDir "") -Force
        $driver = Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { @($models | ForEach-Object Name) -contains $_.Name } | Select-Object -First 1
        $driverVersion = if ($driver) {
            $v = [uint64]$driver.DriverVersion
            "{0}.{1}.{2}.{3}" -f ($v -shr 48), (($v -shr 32) -band 0xFFFF), (($v -shr 16) -band 0xFFFF), ($v -band 0xFFFF)
        }
        $version = Get-AppRelease (Get-Item $app).VersionInfo.ProductVersion
        New-Item -Path $uninstallKey -Force | Out-Null
        $values = @{
            DisplayName = "QL Unidrv printer driver"; DisplayVersion = $version; DriverVersion = [string](Get-Release $driverVersion)
            Publisher = "JOFFS"; DisplayIcon = (Get-IconLocation "qlunidrv.ico" $app); InstallLocation = $appDir
            URLInfoAbout = "https://github.com/joffs/ql-unidrv"; HelpLink = "https://github.com/joffs/ql-unidrv"
            # The uninstall in the setup window, in Windows' language
            UninstallString = "`"$app`" --uninstall"; ModifyPath = "`"$app`" --setup"
        }
        foreach ($k in $values.Keys) { New-ItemProperty -Path $uninstallKey -Name $k -Value $values[$k] -PropertyType String -Force | Out-Null }
        New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
        # The size Installed apps shows (KB): the programs; the driver package is Windows'
        $size = [int]((Get-ChildItem $appDir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1KB)
        New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value $size -PropertyType DWord -Force | Out-Null
        "Registered in Installed apps: QL Unidrv printer driver $version (driver $(Get-Release $driverVersion))."
    }

    # Starts the printer monitor again after an update, as the signed-in user (not elevated, like at logon):
    # Explorer opens a temporary shortcut, so the monitor runs in the user's own context. Only when this script runs as
    # that user: else Explorer would start as the administrator's account (the setup window starts the monitor itself).
    function Start-Monitor {
        if (-not (Test-SignedInUser)) { "The printer monitor starts again with QL Label Formats or at the next sign-in."; return }
        $tmp = Join-Path $env:TEMP "QL printer monitor.lnk"
        $lnk = (New-Object -ComObject WScript.Shell).CreateShortcut($tmp)
        $lnk.TargetPath = $statusApp
        $lnk.Arguments = "--monitor"
        $lnk.Save()
        Start-Process explorer.exe -ArgumentList "`"$tmp`""
        Start-Sleep -Seconds 3
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
        $running = @(Get-LabelTools | Where-Object { $_.CommandLine -match "--monitor" })
        if ($running) { "Printer monitor started again (pid $(($running.ProcessId) -join ', '))." }
        else { "The printer monitor didn't start; it starts with QL Label Formats or at the next sign-in." }
    }

    # .qllabel files open in the label editor (machine-wide; ProgID QLUnidrv.Label, the editor's own icon). Explorer is
    # told afterwards, so icons and "Open" change without a restart.
    $fileTypeKeys = @("HKLM:\SOFTWARE\Classes\.qllabel", "HKLM:\SOFTWARE\Classes\QLUnidrv.Label")
    function Update-Explorer {
        if (-not ("QlShell" -as [type])) {
            Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class QlShell {
    [DllImport("shell32.dll")] static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);
    public static void AssociationsChanged() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }  // SHCNE_ASSOCCHANGED
}
"@
        }
        [QlShell]::AssociationsChanged()
    }
    # An icon file in $appDir, else the exe's own icon
    function Get-IconLocation([string]$icon, [string]$exe) {
        $file = Join-Path $appDir $icon
        if (Test-Path $file) { "`"$file`"" } else { "`"$exe`",0" }
    }
    # A key's default value; the key is created when missing (New-Item -Force on an existing key deletes its subkeys).
    function Set-KeyDefault([string]$key, [string]$value) {
        if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
        Set-ItemProperty -Path $key -Name "(default)" -Value $value
    }
    function Register-LabelFiles {
        Set-KeyDefault $fileTypeKeys[0] "QLUnidrv.Label"
        Set-KeyDefault $fileTypeKeys[1] "QL Label"
        Set-KeyDefault "$($fileTypeKeys[1])\DefaultIcon" (Get-IconLocation "qllabel.ico" $editorApp)
        Set-KeyDefault "$($fileTypeKeys[1])\shell\open\command" "`"$editorApp`" `"%1`""
        Update-Explorer
    }
    function Unregister-LabelFiles {
        $found = $false
        foreach ($k in $fileTypeKeys) { if (Test-Path $k) { Remove-Item $k -Recurse -Force; $found = $true } }
        if ($found) { Update-Explorer; "Removed the .qllabel file type." }
    }

    # "Open with" entries Explorer writes for an exe chosen by hand (Applications\<exe>), machine-wide and per signed-in
    # user, and the users' Explorer choices for .qllabel (the uninstall)
    function Remove-OpenWith {
        foreach ($root in @("HKLM:\SOFTWARE\Classes") + @(Get-UserHives | ForEach-Object { "$_\Software\Classes" })) {
            foreach ($n in $appNames) { Remove-Item "$root\Applications\$n" -Recurse -Force -ErrorAction SilentlyContinue }
        }
        foreach ($hive in Get-UserHives) {
            Remove-Item "$hive\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.qllabel" -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    # Shortcuts the users made to one of our exes (not the Start menu entries, which this script makes): every
    # profile's taskbar pins and desktop, and the public desktop. Each as File, Link (WScript.Shell), Exe.
    function Get-UserShortcuts {
        $folders = @(Join-Path $env:PUBLIC "Desktop")
        foreach ($p in @(Get-ChildItem "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList" -ErrorAction SilentlyContinue |
                Where-Object PSChildName -match "^S-1-5-21-[\d-]+$")) {
            $profileDir = (Get-ItemProperty $p.PSPath).ProfileImagePath
            if (-not $profileDir) { continue }
            $folders += Join-Path $profileDir "AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar"
            $folders += Join-Path $profileDir "Desktop"
            $folders += Join-Path $profileDir "OneDrive\Desktop"
        }
        $ws = New-Object -ComObject WScript.Shell
        foreach ($f in @($folders | Where-Object { Test-Path $_ } | ForEach-Object { Get-ChildItem $_ -Filter "*.lnk" -File -ErrorAction SilentlyContinue })) {
            try { $lnk = $ws.CreateShortcut($f.FullName) } catch { continue }
            $exe = $appNames | Where-Object { $lnk.TargetPath -eq (Join-Path $appDir $_) } | Select-Object -First 1
            if ($exe) { [pscustomobject]@{ File = $f.FullName; Link = $lnk; Exe = $exe } }
        }
    }

    # Published names (oemNN.inf) of our packages currently in the driver store
    function Get-OurPackages {
        $enum = pnputil /enum-drivers
        for ($i = 1; $i -lt $enum.Count; $i++) {
            if ($enum[$i] -match $ourInf -and $enum[$i - 1] -match "(oem\d+\.inf)") { $Matches[1] }
        }
    }

    # Runs the label tool's command-line mode and prints its result file.
    function Invoke-LabelTool([string]$exe, [string]$mode) {
        $result = Join-Path $env:TEMP ("QlLabelFormats-{0}.txt" -f [guid]::NewGuid().ToString("N"))
        $p = Start-Process $exe -ArgumentList $mode, "--result", "`"$result`"" -Wait -PassThru
        if (Test-Path $result) {
            # WARNING lines (e.g. Windows paper number 256 taken by a paper, Sync.KeepNumber256Free) in yellow
            Get-Content $result -Encoding UTF8 | Select-Object -Skip 1 | ForEach-Object {
                if ($_ -like "WARNING*") { Write-Host "  $_" -ForegroundColor Yellow } else { "  $_" }
            }
            Remove-Item $result -Force
        }
        if ($p.ExitCode) { throw "$(Split-Path $exe -Leaf) $mode failed (exit code $($p.ExitCode))." }
    }

    # Printers a Remote Desktop session redirects here ("Brother QL-800 (Unidrv) (umgeleitet 8)" on port TS003, Dynamic
    # Print Monitor) carry our driver names, but belong to that session: even an administrator can't remove them
    # (access denied). They go with the session.
    function Test-RedirectedPrinter($p) { $p.PortName -match "^TS\d+$" -or $p.Name -match "\((umgeleitet|redirected) \d+\)$" }
    # Our printers on this PC, without the redirected ones
    function Get-OurPrinters {
        @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $allDrivers -contains $_.DriverName -and -not (Test-RedirectedPrinter $_) })
    }
    # Why a share name can't be used for the printer $printer, or $null (-SharePrinter; the Printer setup window checks
    # the same before, Sharing.NameProblem): 1-80 characters, none of " / \ [ ] : | < > + = ; , ? * and not another share
    # on this PC (share names compare without case)
    function Test-ShareName([string]$name, [string]$printer) {
        if (-not $name) { return "-SharePrinter needs -ShareName (the share's name, e.g. QL-800)." }
        if ($name.Length -gt 80 -or $name -match '["/\\\[\]:|<>+=;,?*]') { return "The share name '$name' is not valid: at most 80 characters, none of `" / \ [ ] : | < > + = ; , ? *." }
        $printerShare = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.Shared -and $_.ShareName -eq $name -and $_.Name -ne $printer }) | Select-Object -First 1
        if ($printerShare) { return "The share name '$name' is taken by the printer '$($printerShare.Name)'." }
        $own = @(Get-Printer -Name $printer -ErrorAction SilentlyContinue | Where-Object { $_.Shared -and $_.ShareName -eq $name })
        $folder = Get-SmbShare -Name $name -ErrorAction SilentlyContinue
        if ($folder -and -not $own -and "$($folder.ShareType)" -ne "PrintQueue") { return "The share name '$name' is taken by a shared folder ($($folder.Path))." }
        if ($folder -and -not $own) { return "The share name '$name' is taken by another shared printer." }
        return $null
    }
    # Before removing our drivers (uninstall, clean install): a redirected printer keeps a driver in use, so stop before
    # anything is changed and say what to do
    function Assert-NoRedirectedPrinters {
        $rdp = @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $allDrivers -contains $_.DriverName -and (Test-RedirectedPrinter $_) })
        if ($rdp) {
            throw ("Printers redirected by a Remote Desktop session use this driver: $(($rdp | ForEach-Object { "'$($_.Name)'" }) -join ', '). " +
                   "Close Remote Desktop sessions with printer redirection, or connect without it (Remote Desktop Connection > " +
                   "Local Resources > Printers off), then try again.")
        }
    }

    function Remove-Printers {
        foreach ($p in Get-OurPrinters) {
            try { Remove-Printer -Name $p.Name -ErrorAction Stop } catch { throw "Could not remove the printer '$($p.Name)': $($_.Exception.Message)" }
            "Removed printer '$($p.Name)'."
        }
    }

    # The spooler entries only; pnputil /delete-driver removes the package afterwards. Through winspool's
    # DeletePrinterDriverEx: Remove-PrinterDriver can fail for every driver (even an inbox one)
    # where DeletePrinterDriverEx works. A printer that was just deleted can keep its
    # driver busy for a moment (an open handle), so a failed removal is retried, then once more after a spooler
    # restart. Returns the drivers that are still registered.
    function Remove-Drivers {
        if (-not ("QlDriver" -as [type])) {
            Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class QlDriver {
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool DeletePrinterDriverExW(string server, string environment, string name, int flags, int version);
    public static int Delete(string environment, string name) {
        return DeletePrinterDriverExW(null, environment, name, 0, 3) ? 0 : Marshal.GetLastWin32Error();
    }
}
"@
        }
        for ($round = 1; $round -le 4; $round++) {
            $left = @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $allDrivers -contains $_.Name })
            if (-not $left) { return @() }
            if ($round -eq 4) {
                Write-Host "Restarting the print spooler to release the drivers ..."
                Restart-Service Spooler -Force
                Start-Sleep -Seconds 2
            } elseif ($round -gt 1) { Start-Sleep -Seconds 3 }
            foreach ($d in $left) {
                $status = [QlDriver]::Delete($d.PrinterEnvironment, $d.Name)
                # Write-Host: the function's output is the list of drivers left (retest 3: these lines were taken for it)
                if (-not $status) { Write-Host "Removed printer driver '$($d.Name)' ($($d.PrinterEnvironment))." }
                elseif ($round -ge 3) { Write-Host "Could not remove the printer driver '$($d.Name)' yet: $((New-Object ComponentModel.Win32Exception $status).Message)" -ForegroundColor Yellow }
            }
        }
        @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $allDrivers -contains $_.Name } | ForEach-Object Name | Select-Object -Unique)
    }

    # The paper names of our packages' GPDs (*Feature: PaperSize, each option's *Name), e.g. "12mm", "29mm x 2"
    function Get-OurPaperNames {
        $names = @{}
        foreach ($d in @(Get-ChildItem "$env:WINDIR\System32\DriverStore\FileRepository" -Directory -ErrorAction SilentlyContinue | Where-Object Name -match "^qlunidrv\.inf_")) {
            foreach ($gpd in @(Get-ChildItem $d.FullName -Filter *.GPD -File -ErrorAction SilentlyContinue)) {
                $inPaper = $false
                foreach ($line in [IO.File]::ReadAllLines($gpd.FullName)) {
                    if ($line -match "^\s*\*Feature:\s*(\S+)") { $inPaper = $Matches[1] -eq "PaperSize" }
                    elseif ($inPaper -and $line -match '^\s*\*Name:\s*"(.+)"') { $names[$Matches[1]] = $true }
                }
            }
        }
        @($names.Keys)
    }

    # The forms Windows made from our GPDs' papers (FORM_PRINTER, e.g. "12mm", "29mm x 2"; they stay after an
    # uninstall otherwise), once our drivers are gone. Kept: forms another printer's driver lists (DC_PAPERNAMES),
    # and all of them while another Brother QL driver is installed (its forms may share our names; it may have no
    # printer to ask). Unidrv-based drivers add their GPD forms again when a printer is set up.
    function Remove-PaperForms([string[]]$names) {
        if (-not $names) { return }
        if (Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $allDrivers -notcontains $_.Name -and $_.Name -match "QL-?\d" }) {
            "Paper sizes of the driver kept: another Brother QL driver is installed."
            return
        }
        if (-not ("QlForms" -as [type])) {
            Add-Type -TypeDefinition @"
using System; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class QlForms {
    [StructLayout(LayoutKind.Sequential)] struct Defaults { public IntPtr DataType, DevMode; public int Access; }
    [StructLayout(LayoutKind.Sequential)] struct FormInfo1 { public uint Flags; public IntPtr Name; public int Cx, Cy, Left, Top, Right, Bottom; }
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool OpenPrinterW(string n, out IntPtr h, ref Defaults d);
    [DllImport("winspool.drv")] static extern bool ClosePrinter(IntPtr h);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool EnumFormsW(IntPtr h, int level, IntPtr buf, int cb, out int needed, out int returned);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool DeleteFormW(IntPtr h, string name);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)] static extern int DeviceCapabilitiesW(string device, string port, short cap, IntPtr output, IntPtr devmode);
    static IntPtr Server(int access) { var d = new Defaults { Access = access }; IntPtr h; return OpenPrinterW(null, out h, ref d) ? h : IntPtr.Zero; }
    // Names of the FORM_PRINTER forms (made from drivers' paper lists)
    public static string[] PrinterForms() {
        var list = new List<string>();
        IntPtr h = Server(2); if (h == IntPtr.Zero) return list.ToArray();   // SERVER_ACCESS_ENUMERATE
        try {
            int needed, n; EnumFormsW(h, 1, IntPtr.Zero, 0, out needed, out n);
            IntPtr buf = Marshal.AllocHGlobal(needed);
            try {
                if (!EnumFormsW(h, 1, buf, needed, out needed, out n)) return list.ToArray();
                int size = Marshal.SizeOf(typeof(FormInfo1));
                for (int i = 0; i < n; i++) {
                    var f = (FormInfo1)Marshal.PtrToStructure(new IntPtr(buf.ToInt64() + i * size), typeof(FormInfo1));
                    if (f.Flags == 2) list.Add(Marshal.PtrToStringUni(f.Name));   // FORM_PRINTER
                }
            } finally { Marshal.FreeHGlobal(buf); }
        } finally { ClosePrinter(h); }
        return list.ToArray();
    }
    public static int Delete(string name) {
        IntPtr h = Server(1); if (h == IntPtr.Zero) return Marshal.GetLastWin32Error();   // SERVER_ACCESS_ADMINISTER
        try { return DeleteFormW(h, name) ? 0 : Marshal.GetLastWin32Error(); } finally { ClosePrinter(h); }
    }
    // The paper names a printer's driver lists (DC_PAPERNAMES: 64 characters each)
    public static string[] PaperNames(string printer, string port) {
        int n = DeviceCapabilitiesW(printer, port, 16, IntPtr.Zero, IntPtr.Zero);
        if (n <= 0) return new string[0];
        IntPtr buf = Marshal.AllocHGlobal(n * 128);
        try {
            n = DeviceCapabilitiesW(printer, port, 16, buf, IntPtr.Zero);
            var names = new string[Math.Max(n, 0)];
            for (int i = 0; i < names.Length; i++) names[i] = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + i * 128), 64).TrimEnd('\0');
            return names;
        } finally { Marshal.FreeHGlobal(buf); }
    }
}
"@
        }
        $others = @{}
        foreach ($p in @(Get-Printer -ErrorAction SilentlyContinue)) { foreach ($n in [QlForms]::PaperNames($p.Name, $p.PortName)) { $others[$n] = $true } }
        $removed = 0
        foreach ($form in [QlForms]::PrinterForms()) {
            if ($names -notcontains $form -or $others.ContainsKey($form)) { continue }
            $status = [QlForms]::Delete($form)
            if ($status) { Write-Host "Could not remove the paper size '$form': $((New-Object ComponentModel.Win32Exception $status).Message)" -ForegroundColor Yellow; Add-Result "left-paper" $form }
            else { $removed++ }
        }
        if ($removed) { "Removed $removed paper sizes of the driver." }
    }

    # USB device nodes of the QL models (connected or not) that this script may remove or bind: those on
    # one of our packages, on a Windows inbox driver (e.g. the generic prnms013.inf) or on none. A
    # device on another vendor's package is left
    # alone and reported. Each gets a Model property (its row in $models).
    function Get-QlDevices {
        $ours = @(Get-OurPackages)
        foreach ($m in $models) {
            Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like "USBPRINT\*" -and $_.HardwareID -contains $m.Id } |
                Where-Object {
                    $infPath = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data
                    $mine = -not $infPath -or $infPath -notmatch "^oem\d+\.inf$" -or $ours -contains $infPath
                    if (-not $mine) { Write-Host "USB device $($_.InstanceId) uses another driver package ($infPath): left alone." }
                    $mine
                } | Add-Member -NotePropertyName Model -NotePropertyValue $m -PassThru
        }
    }

    # Present USB device nodes of any QL model (whatever their driver).
    function Get-QlDeviceCount {
        $ids = @($models | ForEach-Object Id)
        @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
            $_.InstanceId -like "USBPRINT\*" -and @($_.HardwareID | Where-Object { $ids -contains $_ }).Count }).Count
    }

    # A device node that was removed (or whose package was deleted) comes back only when Windows detects
    # devices again, otherwise not before the printer is switched off and on: scan and
    # wait until the $count devices that were there are back (on Windows' generic driver). Also expects one
    # for every connected QL printer's USB device (it stays when the printer device node is gone); only QL printers:
    # their USB product name starts with "QL-".
    function Wait-QlDevices([int]$count) {
        pnputil /scan-devices | Out-Null
        $usb = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like "USB\VID_04F9&*" -and
            (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data -eq "usbprint.inf" -and
            (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_BusReportedDeviceDesc -ErrorAction SilentlyContinue).Data -like "QL-*" }).Count
        if ($usb -gt $count) { $count = $usb }
        if ($count -le 0) { return }
        $deadline = (Get-Date).AddSeconds(30)
        while (($n = Get-QlDeviceCount) -lt $count -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
        if ($n -lt $count) { Write-Host "Only $n of $count QL USB printer devices found: switch the missing printer off and on." -ForegroundColor Yellow }
    }

    # Adds the printer for a model on its USB port (the port's description is the printer's USB name,
    # e.g. "BrotherQL-800"), for when Windows did not create it after the bind.
    function Add-UsbPrinter($model, [string]$port) {
        if (-not $port) {
            $short = $model.Name -replace "^Brother ", "" -replace " \(Unidrv\)$", ""
            $port = Get-PrinterPort | Where-Object { $_.Name -like "USB*" -and $_.Description -like "*$short" } |
                Select-Object -First 1 -ExpandProperty Name
        }
        if (-not $port) {
            throw "No USB port of a $($model.Name) found. Connect the printer (QL-700/800 series: Editor Lite LED off) or pass -UsbPort USB001. " +
                  "Ports: " + ((Get-PrinterPort | Where-Object Name -like "USB*" | ForEach-Object { "$($_.Name) ($($_.Description))" }) -join ", ")
        }
        # A printer of that name elsewhere (another PC's share, the network) stays where it is: this one is named
        # "<driver name> on USB002" then, as with shares
        $name = $model.Name
        $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
        if ($existing -and $existing.PortName -notlike "USB*") {
            $name = "$($model.Name) on $port"
            $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
        }
        if (-not $existing) {
            Add-Printer -Name $name -DriverName $model.Name -PortName $port -ErrorAction Stop
        } else {
            Set-Printer -Name $name -DriverName $model.Name -PortName $port -ErrorAction Stop
        }
        "Printer '$name' ready on $port."
        $script:usbAdded = @{ Name = $name; Port = $port; Existed = [bool]($existing -and $existing.PortName -eq $port -and $existing.DriverName -eq $model.Name) }
    }

    # ---------- the modes: install / update, programs only (-AppOnly), settings (-Settings), uninstall ----------

    # -Uninstall: printers, ports, drivers, packages, programs, forms, certificates, the data folder
    function Invoke-Uninstall {
        Assert-LabelToolClosed
        Assert-NoRedirectedPrinters
        # The monitor and the status service keep printers open: stop them before the printers go.
        Remove-StatusService
        Stop-LabelTool
        $deviceCount = Get-QlDeviceCount
        $paperNames = @(Get-OurPaperNames)   # before the packages go
        $ourPrinters = Get-OurPrinters
        $removedPrinters = @($ourPrinters | ForEach-Object Name)
        Remove-Printers
        # The ports made for shares (\\host\share) and network printers (IP_<address>) of those printers, when no other
        # printer uses them
        $usedPorts = @(Get-Printer | ForEach-Object PortName)
        foreach ($port in @($ourPrinters | ForEach-Object PortName | Select-Object -Unique)) {
            if ($usedPorts -contains $port -or $port -notmatch "^(\\\\[^\\]+\\[^\\]+|IP_.+)$") { continue }
            # A Standard TCP/IP port: Remove-PrinterPort fails with 0x80070709 even without a printer on it;
            # its WMI object goes. Reported only once it is really gone; one that stays is a leftover.
            $why = $null
            try {
                if ($port -like "IP_*") {
                    Get-CimInstance Win32_TCPIPPrinterPort -Filter "Name='$($port.Replace("'", "\'"))'" | Remove-CimInstance -ErrorAction Stop
                } else { Remove-FilePort $port | Out-Null }
            } catch { $why = $_.Exception.Message }
            if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) {
                Write-Host "Could not remove the port ${port}$(if ($why) { ": $why" })" -ForegroundColor Yellow
                Add-Result "left-port" $port
            } else { "Removed the port $port." }
        }
        $driversLeft = Remove-Drivers
        if (-not $driversLeft) { Remove-PaperForms $paperNames }
        foreach ($o in @(Get-OurPackages)) { pnputil /delete-driver $o /force | Out-Null; "Removed package $o" }
        Wait-QlDevices $deviceCount
        # Every file port in our data folder: capture.prn and the per-model capture printers' ports (capture\ql500.prn
        # ...), whose folder goes below. A port that can't be removed doesn't stop
        # the rest of the uninstall.
        $dataPorts = @(Get-PrinterPort -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$data\*" } | ForEach-Object Name)
        foreach ($port in @($capturePort) + $dataPorts | Select-Object -Unique) {
            try { Remove-FilePort $port }
            catch { Write-Host "Could not remove the port ${port}: $($_.Exception.Message)" -ForegroundColor Yellow }
        }
        if (Test-Path $app) { "Removing the paper sizes of the label formats:"; Invoke-LabelTool $app "--remove-forms" }
        # Every signed-in user's monitor autostart, program settings and the per-user settings of the removed printers
        foreach ($hive in Get-UserHives) {
            Remove-ItemProperty -Path "$hive\Software\Microsoft\Windows\CurrentVersion\Run" -Name $monitorRunName -ErrorAction SilentlyContinue
            Remove-Item "$hive\Software\QLUnidrv" -Recurse -Force -ErrorAction SilentlyContinue
            foreach ($key in "Printers\DevModePerUser", "Printers\DevModes2") {
                foreach ($p in $removedPrinters) { Remove-ItemProperty -Path "$hive\$key" -Name $p -ErrorAction SilentlyContinue }
            }
        }
        Remove-Item $shortcut, $statusShortcut, $editorShortcut -Force -ErrorAction SilentlyContinue
        # The users' own shortcuts to our exes (desktops, taskbar pins) would point to nothing
        foreach ($s in Get-UserShortcuts) {
            try { Remove-Item -LiteralPath $s.File -Force; "Removed the shortcut $($s.File)." }
            catch { Write-Host "Could not remove the shortcut $($s.File): $($_.Exception.Message)" -ForegroundColor Yellow }
        }
        Unregister-LabelFiles
        Remove-OpenWith
        Remove-Item $appDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item $uninstallKey -Recurse -Force -ErrorAction SilentlyContinue
        Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like $rootPattern } | Remove-Item
        Get-ChildItem Cert:\LocalMachine\TrustedPublisher -ErrorAction SilentlyContinue | Where-Object { $_.Subject -eq $signerSubject } | Remove-Item
        # Copies of the per-PC root that signing leaves among the intermediate CAs; the signed-in user's own are
        # removed by setup.ps1
        Get-ChildItem Cert:\CurrentUser\CA -ErrorAction SilentlyContinue | Where-Object { $_.Subject -like $rootPattern } | Remove-Item -ErrorAction SilentlyContinue
        # The data folder: logs, capture file, labels.tsv (written again from labels.json by the next
        # install); the label list only on request.
        if (Test-Path $data) {
            foreach ($d in "status", "capture") { Lock-UserFilesFolder (Join-Path $data $d) }
            $keep = if ($KeepLabels) { @(Join-Path $data "labels.json") } else { @() }
            Get-ChildItem $data -Force | Where-Object { $keep -notcontains $_.FullName } | ForEach-Object { Remove-DataItem $_ }
            $left = @(Get-ChildItem $data -Recurse -Force -File | Where-Object { $keep -notcontains $_.FullName })
            if ($left) { Write-Host "Could not delete (in use; delete after a restart): $(($left | ForEach-Object FullName) -join ', ')" -ForegroundColor Yellow }
            elseif (-not $KeepLabels) { Remove-Item $data -Recurse -Force -ErrorAction SilentlyContinue }
        }
        # Everything else is gone by now; a left driver makes the uninstall fail (exit code 1, error= line).
        if ($driversLeft) { throw "Uninstalled except these printer drivers, which Windows didn't let go: $($driversLeft -join ', '). Restart the PC and run the uninstall again." }
        "Uninstalled. " + $(if ($KeepLabels) { "Your label list was kept ($data\labels.json)." } else { "$data was deleted." })
        Add-Result "done" "1"
    }

    # Data folder: administrators write, users read (labels.tsv steers what the driver prints); also
    # log\, which the plug-in writes with the spooler's rights (a folder users can change would let them
    # redirect those writes with a junction; docs/architecture.md section 7): /reset gives it and its files the data
    # folder's rights.
    function Initialize-DataFolder {
        New-SafeDirectory $data
        icacls $data /setowner "*S-1-5-32-544" /T /C /Q | Out-Null   # a folder a user made beforehand stays theirs otherwise
        icacls $data /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" "*S-1-5-32-545:(OI)(CI)RX" | Out-Null
        $logDir = Join-Path $data "log"
        New-SafeDirectory $logDir
        icacls $logDir /reset /T /C /Q | Out-Null
        # The driver's diagnostic log is off unless asked for: the plug-in writes only while plugin.log exists
        # (without -Logging an existing one stays). printer-status.log there holds the messages the driver
        # read from a printer (roll check); the label tool reads it next to its own in status\.
        $pluginLog = Join-Path $logDir "plugin.log"
        if ($Logging -eq "on" -and -not (Test-Path $pluginLog)) { New-Item -ItemType File $pluginLog | Out-Null }
        if ($Logging -eq "off") { Remove-Item $pluginLog, (Join-Path $logDir "plugin.old.log") -Force -ErrorAction SilentlyContinue }
        if ($Logging) { "Driver log ${Logging}: $pluginLog"; Add-Result "logging" $Logging; Add-Result "logfile" $pluginLog }
        # status\: the label tool's and the printer monitor's printer-status.log, written as the signed-in user.
        # The driver never writes there, so a link users might put there gains them nothing.
        Set-UserFilesFolder (Join-Path $data "status")
    }

    # The driver package: certificates, pnputil, the drivers, the USB devices; then older packages and certificates
    function Install-DriverPackage {
        if (-not (Test-Path (Join-Path $pkg "$storeArch\QLUnidrv.dll"))) {
            throw "out\package has no driver for this PC ($storeArch). Run build.cmd (this PC, or both) and driver\package.ps1 first."
        }
        if (-not $Update) { Assert-NoRedirectedPrinters }   # the clean install removes our drivers
        # Certificates: the package's root CA and signer; older ones of this driver are removed after the package step.
        Set-Step "certificates"
        $rootCer = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $pkg "qlunidrv-root.cer")
        $signerCer = New-Object Security.Cryptography.X509Certificates.X509Certificate2 (Join-Path $pkg "qlunidrv.cer")
        # X509Store creates a missing store: a clean Windows has no LocalMachine\TrustedPublisher, where
        # Import-Certificate fails with E_ACCESSDENIED.
        # The ones added now are noted: a failure before the package is in (key 7 not pressed) takes them out again,
        # so retries don't leave a trusted root each (catch below).
        foreach ($c in @(@($rootCer, "Root"), @($signerCer, "TrustedPublisher"))) {
            $store = New-Object Security.Cryptography.X509Certificates.X509Store $c[1], "LocalMachine"
            $store.Open("ReadWrite")
            try {
                if (-not $store.Certificates.Find("FindByThumbprint", $c[0].Thumbprint, $false).Count) { $script:addedCerts += @("$($c[1])\$($c[0].Thumbprint)") }
                $store.Add($c[0])
            } finally { $store.Close() }
        }

        Set-Step "package"
        $old = @(Get-OurPackages)
        $deviceCount = Get-QlDeviceCount
        if (-not $Update) {
            Remove-Printers
            $left = Remove-Drivers
            if ($left) { Write-Host "Old drivers still registered (the new package replaces them): $($left -join ', ')" -ForegroundColor Yellow }
            # The USB device node keeps its driver package; remove it so that it is re-detected and
            # bound to the new package (needs signature enforcement off, like the package install).
            foreach ($d in @(Get-QlDevices)) { pnputil /remove-device $d.InstanceId | Out-Null; "Removed device $($d.InstanceId)." }
            foreach ($o in @(Get-OurPackages)) { pnputil /delete-driver $o /force | Out-Null; "Removed old package $o" }
            $old = @()
        }

        # /install offers the package to every matching device node. Where another vendor's package
        # drives a QL device, add the package only; our devices are bound
        # explicitly below anyway.
        $ours = @(Get-OurPackages)
        $ids = @($models | ForEach-Object Id)
        $foreign = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
                $_.InstanceId -like "USBPRINT\*" -and @($_.HardwareID | Where-Object { $ids -contains $_ }).Count } |
            Where-Object {
                $infPath = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data
                $infPath -match "^oem\d+\.inf$" -and $ours -notcontains $infPath })
        if ($foreign) {
            "QL devices on another vendor's driver package ($(($foreign | ForEach-Object InstanceId) -join ', ')): adding the package without offering it to devices."
            $added = @(pnputil /add-driver (Join-Path $pkg $infName))
        } else {
            $added = @(pnputil /add-driver (Join-Path $pkg $infName) /install)
        }
        $added
        # The same package already in Windows' driver store (an earlier attempt added it): Windows doesn't ask about the
        # publisher then, and the setup window says so instead of showing that question
        $published = @($added | Select-String "oem\d+\.inf" | ForEach-Object { $_.Matches[0].Value }) | Select-Object -First 1
        if ($published -and $ours -contains $published) { Add-Result "package" "known" }
        # 3010 = reboot required, 259 = package added but no device needed it (printers are bound below)
        # A negative code is an HRESULT, shown in hex: 0x800B0109 = untrusted root, i.e. signature checks on (ARM64 outside the F7 start)
        if ($LASTEXITCODE -and $LASTEXITCODE -ne 3010 -and $LASTEXITCODE -ne 259) {
            throw "pnputil failed ($LASTEXITCODE$(if ($LASTEXITCODE -lt 0) { ', 0x{0:X8}' -f $LASTEXITCODE }))"
        }
        # The store folder of this package: the one whose INF has this package's DriverVer (each build has its own). The
        # newest folder was wrong for a package Windows had already, next to a newer one ("Install older version").
        $pkgDriverVer = (Get-Content (Join-Path $pkg $infName) | Select-String "^DriverVer=").Line
        $inf = Get-ChildItem "$env:WINDIR\System32\DriverStore\FileRepository" -Directory -Filter "$($infName)_$($storeArch)_*" |
            Sort-Object @{ Descending = $true; Expression = { (Get-Content (Join-Path $_.FullName $infName) -ErrorAction SilentlyContinue | Select-String "^DriverVer=").Line -eq $pkgDriverVer } },
                        @{ Descending = $true; Expression = { $_.LastWriteTime } } |
            Select-Object -First 1
        if (-not $inf) { throw "pnputil failed (the package is not in Windows' driver store)" }
        # All models of the package, so printers on network or shared ports can be added with them too.
        foreach ($m in $models) {
            Add-PrinterDriver -Name $m.Name -InfPath (Join-Path $inf.FullName $infName) -ErrorAction Stop
        }
        "Drivers registered from $($inf.Name): $(($models | ForEach-Object { $_.Name }) -join ', ')."

        # Bind the connected QL printers' USB devices to this package (every model; also those on an older
        # package of ours, so it can be removed). Windows ranks packages installed with signature
        # enforcement off as "not digitally signed", below its generic driver (prnms013.inf: no USB
        # port, no printer), so bind-device.ps1 installs ours on the device explicitly. Windows then
        # creates the printer.
        Set-Step "printers"
        Wait-QlDevices $deviceCount
        $pkgVersion = ((Get-Content (Join-Path $pkg $infName) | Select-String "^DriverVer=").Line -split ",")[1].Trim()
        $devices = @(Get-QlDevices | Where-Object Present)
        $connected = @($models | Where-Object { $m = $_; @($devices | Where-Object { $_.Model -eq $m }).Count })
        $rebound = @()
        foreach ($m in $connected) {
            $stale = @($devices | Where-Object { $_.Model -eq $m } | Where-Object {
                (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverVersion -ErrorAction SilentlyContinue).Data -ne $pkgVersion })
            # bind-device.ps1 binds every connected device of the model
            if ($stale) {
                & (Join-Path $PSScriptRoot "bind-device.ps1") -Inf (Join-Path $inf.FullName $infName) -HardwareId $m.Id -Model $m.Name
                $rebound += @($devices | Where-Object { $_.Model -eq $m })
            }
        }
        if ($devices) {
            foreach ($m in $connected) {
                $deadline = (Get-Date).AddSeconds(60)
                do {
                    $usb = @(Get-Printer | Where-Object { $_.DriverName -eq $m.Name -and $_.PortName -like "USB*" })
                    if (-not $usb) { Start-Sleep -Seconds 2 }
                } while (-not $usb -and (Get-Date) -lt $deadline)
                if ($usb) { $usb | ForEach-Object { "Printer '$($_.Name)' on $($_.PortName)." } }
                else { "Windows did not create the USB printer for $($m.Name); adding it."; Add-UsbPrinter $m }
            }
            # After a clean install (device node removed and detected again) the USB port stayed unusable
            # until the printer was switched off and on: the first job stopped with an error before the
            # driver saw it. Restarting the printer's USB device (the
            # parent of the USBPRINT node, which carries the port) does the same in software.
            foreach ($d in $rebound) {
                $parent = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName DEVPKEY_Device_Parent -ErrorAction SilentlyContinue).Data
                if ($parent -like "USB\*") {
                    pnputil /restart-device $parent | Out-Null
                    if ($LASTEXITCODE) { Write-Host "Could not restart $parent ($LASTEXITCODE): switch the printer off and on before the first print." -ForegroundColor Yellow }
                    else { "Restarted the USB device $parent." }
                }
            }
            foreach ($d in $devices) {
                $infPath = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data
                "Device $($d.InstanceId) uses $infPath."
            }
        } elseif ($foreign) {
            # The usual x64 case: the QL printer's device on Brother's own driver
            foreach ($f in $foreign) {
                $m = $models | Where-Object { $f.HardwareID -contains $_.Id } | Select-Object -First 1
                $short = $m.Name -replace "^Brother ", "" -replace " \(Unidrv\)$", ""
                $port = Get-PrinterPort | Where-Object { $_.Name -like "USB*" -and $_.Description -like "*$short" } | Select-Object -First 1 -ExpandProperty Name
                if ($UsbPrinter -and (-not $UsbPort -or @($UsbPort -split "," | ForEach-Object { $_.Trim() }) -contains $port)) { "$short on $port uses Brother's driver; it stays, and '$($m.Name)' is added on the same port." }
                else { "$short on $port uses Brother's driver; it stays. To add '$($m.Name)' on the same port: -UsbPrinter -UsbDriverName `"$($m.Name)`" -UsbPort $port." }
            }
        } elseif ($storeArch -eq "arm64") {
            "No QL printer connected by USB (QL-700/800 series: Editor Lite LED must be off): one connected later gets bound in a later F7 boot (install again with -Update), or use -UsbPrinter."
        } else {
            "No QL printer connected by USB (fine when this PC prints through a network share). One connected later: run the install again (-Update) to bind it."
        }

        if ($Update) {
            # Older packages no longer used can go. pnputil refuses a package the spooler used in this
            # boot ("in use") even when nothing references it any more, so one that no
            # present device and no registered printer driver uses is removed with /force.
            $new = @(Get-OurPackages) | Where-Object { $old -notcontains $_ }
            $enum = pnputil /enum-drivers
            $versions = @{}
            for ($i = 1; $i -lt $enum.Count; $i++) {
                if ($enum[$i] -match $ourInf -and $enum[$i - 1] -match "(oem\d+\.inf)") {
                    $o = $Matches[1]
                    for ($j = $i; $j -lt [Math]::Min($i + 8, $enum.Count); $j++) {
                        if ($enum[$j] -match "\d+/\d+/\d+\s+(\d+\.\d+\.\d+\.\d+)") { $versions[$o] = $Matches[1]; break }
                    }
                }
            }
            $usedVersions = @(Get-PrinterDriver -ErrorAction SilentlyContinue | Where-Object { $_.InfPath -match "\\qlunidrv\.inf_" } |
                ForEach-Object { ((Get-Content $_.InfPath | Select-String "^DriverVer=").Line -split ",")[1].Trim() })
            $usedInfs = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object InstanceId -like "USBPRINT\*" |
                ForEach-Object { (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data })
            # A QL USB device that is switched off keeps its package: without it, Windows would put the
            # device on its generic driver when it comes back.
            $absent = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -like "USBPRINT\BROTHERQL*" -and $_.Status -eq "Unknown" } |
                ForEach-Object { [pscustomobject]@{ Id = $_.InstanceId; Inf = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverInfPath -ErrorAction SilentlyContinue).Data } })
            foreach ($o in $old) {
                if ($new -contains $o) { continue }
                $off = @($absent | Where-Object Inf -eq $o)
                if ($off) {
                    "Kept package $o ($($versions[$o])): used by $(($off.Id) -join ', '), not connected. Switch it on and run install.ps1 -Update again in an F7 window."
                    continue
                }
                $unused = $versions[$o] -and $usedVersions -notcontains $versions[$o] -and $usedInfs -notcontains $o
                if ($unused) { pnputil /delete-driver $o /force | Out-Null } else { pnputil /delete-driver $o | Out-Null }
                if ($LASTEXITCODE -eq 0) { "Removed old package $o ($($versions[$o]))" } else { "Kept package $o ($($versions[$o])): still in use" }
            }
            Get-OurPrinters | ForEach-Object { "Printer '$($_.Name)' updated." }
        }

        # Certificates of this driver, now that old packages are gone: kept while a package in the driver store is signed
        # with them (Get-StoreSigningThumbprints). This package's own pair goes too when Windows kept the stored copy of
        # the same package (a repair through Setup.exe signs again with a new one-time
        # pair). If the store can't be read, this package's pair stays.
        $storePrints = @(Get-StoreSigningThumbprints)
        $keep = if ($storePrints.Count) { $storePrints } else { @($rootCer.Thumbprint, $signerCer.Thumbprint) }
        Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Subject -like $rootPattern -and $keep -notcontains $_.Thumbprint } |
            ForEach-Object { Remove-Item $_.PSPath; "Removed old root certificate $($_.Subject) ($($_.Thumbprint))." }
        Get-ChildItem Cert:\LocalMachine\TrustedPublisher | Where-Object { $_.Subject -eq $signerSubject -and $keep -notcontains $_.Thumbprint } |
            ForEach-Object { Remove-Item $_.PSPath; "Removed old signing certificate $($_.Subject) ($($_.Thumbprint))." }
    }

    # -Settings -StatusService on|off
    function Set-StatusServiceSetting {
        if ($StatusService -eq "on") { Install-StatusService } elseif ($StatusService -eq "off") { Remove-StatusService }
        if ($StatusService) { Add-Result "service" $StatusService }
    }

    # The programs, help, Start menu, file type, Installed apps entry and the label formats
    function Install-Programs {
        # Label tool and the label formats (Windows forms + C:\ProgramData\QLUnidrv\labels.tsv)
        Set-Step "app"
        New-Item -ItemType Directory -Force $appDir | Out-Null
        $serviceWanted = if ($StatusService) { $StatusService -eq "on" } else { [bool](Get-Service -Name $serviceName -ErrorAction SilentlyContinue) }
        Stop-StatusService
        Stop-LabelTool
        Get-ChildItem $appDir -Filter "*.old-*" -File | Remove-Item -Force -ErrorAction SilentlyContinue
        foreach ($name in $appNames) {
            Install-AppFile (Join-Path $repo "out\tools\$name")
            Install-AppFile (Join-Path $repo "out\tools\$name.config")
        }
        foreach ($name in $appIcons) {
            $icon = Join-Path $repo "out\tools\$name"
            if (Test-Path $icon) { Copy-Item $icon $appDir -Force }
        }
        # The help pages (the programs open <exe folder>\help\... in the browser): the folder is replaced, so removed
        # pages go
        $help = Join-Path $appDir "help"
        if (Test-Path $help) { Remove-Item $help -Recurse -Force }
        Copy-Item (Join-Path $repo "out\tools\help") $help -Recurse -Force
        # The factory lists as JSON next to the programs (German, UK, US): --sync seeds labels.json from the one for
        # the Windows display language and units, and "Import > Brother factory list" falls back to them when Brother's own
        # file isn't embedded (release and public builds).
        New-Item -ItemType Directory -Force (Join-Path $appDir "labels") | Out-Null
        foreach ($seed in "default-labels.json", "default-labels-en.json", "default-labels-en-us.json") {
            Copy-Item (Join-Path $repo "labels\$seed") (Join-Path $appDir "labels") -Force
        }
        $ws = New-Object -ComObject WScript.Shell
        $lnk = $ws.CreateShortcut($shortcut)
        $lnk.TargetPath = $app
        $lnk.IconLocation = "$app,0"
        $lnk.Description = "Custom label formats (names, lengths) for the Brother QL printers"
        $lnk.Save()
        $lnk = $ws.CreateShortcut($statusShortcut)
        $lnk.TargetPath = $statusApp
        $lnk.Arguments = ""
        $lnk.IconLocation = "$statusApp,0"
        $lnk.Description = "Brother QL printers: state, loaded roll, errors and what to do; printer notifications"
        $lnk.Save()
        $lnk = $ws.CreateShortcut($editorShortcut)
        $lnk.TargetPath = $editorApp
        $lnk.Arguments = ""
        $lnk.IconLocation = "$editorApp,0"
        $lnk.Description = "Design and print labels on the Brother QL printers: text, lines, boxes, pictures"
        $lnk.Save()
        Register-LabelFiles
        "Programs installed in $appDir (Start menu: QL Label Formats, QL Label Maker, QL Printer Status; .qllabel files open in QL Label Maker)."
        Register-InstalledApp
        # A missing labels.json is seeded by --sync itself, in the Windows display language
        "Applying the label formats:"
        Set-Step "labels"
        Invoke-LabelTool $app "--sync"
        if ($serviceWanted) { Install-StatusService } else { Remove-StatusService }
        if ($script:monitorStopped) { Start-Monitor }
    }

    # The printer queues asked for: share/unshare, remove, test printer, USB, another PC's share, network
    function Invoke-PrinterActions {
        # The printer queues asked for (test printer, USB, share, network) are their own step in the setup window, so a
        # failure here isn't reported as one of the steps before
        if ($TestPrinter -or $UsbPrinter -or $SharePort -or $NetworkPrinter -or $RemovePrinter -or $SharePrinter -or $UnsharePrinter) { Set-Step "printers" }

        if ($SharePrinter -or $UnsharePrinter) {
            # Share one of our printers on a USB port for other PCs, rename its share, or stop sharing it (the Printer setup
            # window's "Share a printer" tab, 2026-11-06). Other PCs add it with -SharePort \\<this PC>\<name>.
            $target = if ($SharePrinter) { $SharePrinter } else { $UnsharePrinter }
            $p = Get-OurPrinters | Where-Object { $_.Name -eq $target } | Select-Object -First 1
            if (-not $p) { throw "'$target' is not a QL printer with this driver on this PC." }
            if ($UnsharePrinter) {
                if ($p.Shared) { Set-Printer -Name $p.Name -Shared $false -ErrorAction Stop; "Stopped sharing '$($p.Name)' (was $($p.ShareName))." }
                else { "'$($p.Name)' was not shared." }
                Add-Result "unshared" $p.Name
            } else {
                if ($p.PortName -notmatch "^USB\d{3}$") {
                    throw "Only a printer on a USB port is shared here; '$($p.Name)' uses $($p.PortName) (other PCs add a network printer by its address)."
                }
                $name = "$ShareName".Trim()
                $why = Test-ShareName $name $p.Name
                if ($why) { throw $why }
                $old = if ($p.Shared) { $p.ShareName } else { $null }
                if ($old -eq $name) { "'$($p.Name)' is shared as $name already." }
                else {
                    Set-Printer -Name $p.Name -Shared $true -ShareName $name -ErrorAction Stop
                    if ($old) { "Renamed the share of '$($p.Name)' from $old to $name."; Add-Result "renamed-from" $old }
                    else { "Shared '$($p.Name)' as ${name}; other PCs reach it as \\$env:COMPUTERNAME\$name." }
                }
                Add-Result "shared" $p.Name; Add-Result "sharename" $name
            }
        }

        if ($RemovePrinter) {
            # One of our printers (our driver, not redirected by Remote Desktop) with its waiting jobs; its port too when
            # no other printer uses it and it is one we add (IP_..., \\host\share, a file). USB ports belong to Windows.
            # The driver package stays, also for the last printer of a model.
            $p = Get-OurPrinters | Where-Object { $_.Name -eq $RemovePrinter } | Select-Object -First 1
            if (-not $p) { throw "'$RemovePrinter' is not a QL printer with this driver on this PC." }
            $jobs = @(Get-PrintJob -PrinterName $p.Name -ErrorAction SilentlyContinue)
            foreach ($j in $jobs) { Remove-PrintJob -InputObject $j -ErrorAction SilentlyContinue }
            if ($jobs.Count) { "Deleted $($jobs.Count) waiting print job(s)." }
            # A printer that something still holds open when it is removed stays "pending deletion" (registry key
            # "<name>,N", hidden from Windows' lists) until the spooler restarts, with its port and share (USB remove
            # tests 2026-11-06, ARM and x64). So nothing of ours may hold it: the status service is stopped meanwhile,
            # and the share goes first, so other PCs (their status readers, their print jobs) can't open it again.
            $printersKey = "HKLM:\SYSTEM\CurrentControlSet\Control\Print\Printers"
            $pendingPattern = "^" + [regex]::Escape($p.Name) + ",\d+$"
            $pendingBefore = @(Get-ChildItem $printersKey -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName } | Where-Object { $_ -match $pendingPattern })
            $svc = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
            $restartService = $svc -and $svc.Status -eq "Running"
            if ($restartService) { Stop-StatusService }
            try {
                if ($p.Shared) {
                    try { Set-Printer -Name $p.Name -Shared $false -ErrorAction Stop; "Stopped sharing '$($p.Name)'." }
                    catch { "Could not stop sharing '$($p.Name)' first: $($_.Exception.Message)" }
                }
                try { Remove-Printer -Name $p.Name -ErrorAction Stop } catch { throw "Could not remove the printer '$($p.Name)': $($_.Exception.Message)" }
                "Removed printer '$($p.Name)'."
                Add-Result "removed" $p.Name
                # Gone for good, or still pending? Handles of other programs may close within seconds.
                $pending = $null
                for ($i = 0; $i -lt 10; $i++) {
                    $pending = @(Get-ChildItem $printersKey -ErrorAction SilentlyContinue | ForEach-Object { $_.PSChildName } |
                        Where-Object { $_ -match $pendingPattern -and $pendingBefore -notcontains $_ }) | Select-Object -First 1
                    if (-not $pending -and -not (Test-Path -LiteralPath "$printersKey\$($p.Name)")) { break }
                    Start-Sleep -Seconds 1
                }
                if ($pending) {
                    "Windows keeps the printer as '$pending' until the print spooler restarts (another program, or a PC printing to its share, still had it open); it no longer appears anywhere."
                    Add-Result "pending" $pending
                    # Shared: as a rule a PC printing to the share holds it (its spooler's remote handle cache, 2026-11-06)
                    if ($p.Shared) { Add-Result "was-shared" "1" }
                }
            } finally {
                if ($restartService) { try { Start-Service -Name $serviceName -ErrorAction Stop; "Service 'QL printer status' running again." } catch { "Could not start the service 'QL printer status' again: $($_.Exception.Message)" } }
            }
            $port = $p.PortName
            $ours = $port -match "^IP_" -or $port -match "^\\\\[^\\]+\\[^\\]+$" -or $port -match "^[A-Za-z]:\\"
            if ($ours -and $pending) {
                # The pending printer still uses it, so Windows refuses ("general failure"; x64 Sandbox 2026-11-06). It stays,
                # unused, also once the printer is gone; no failure.
                "The port $port stays: the printer Windows still keeps uses it. Unused afterwards, it does no harm."
                Add-Result "pending-port" $port
            } elseif ($ours -and -not @(Get-Printer -ErrorAction SilentlyContinue | Where-Object { $_.PortName -eq $port })) {
                # The spooler can hold the port for a moment after the printer went
                $err = $null
                for ($i = 0; $i -lt 3; $i++) {
                    if ($i) { Start-Sleep -Seconds 1 }
                    try {
                        # Remove-PrinterPort fails for Standard TCP/IP ports (0x80070709); WMI removes them, as the uninstall does
                        if ($port -match "^IP_") {
                            Get-CimInstance Win32_TCPIPPrinterPort -Filter "Name='$($port.Replace("'", "\'"))'" | Remove-CimInstance -ErrorAction Stop
                        } else { Remove-FilePort $port | Out-Null }
                        if (Get-PrinterPort -Name $port -ErrorAction SilentlyContinue) { throw "it is still there" }
                        $err = $null; break
                    } catch { $err = $_.Exception.Message }
                }
                if ($err) { "Could not remove the port ${port}: $err"; Add-Result "left-port" $port }
                else { "Removed the port $port."; Add-Result "port" $port }
            }
        }

        if ($TestPrinter) {
            # Local file port: every job overwrites this file (used by compare.ps1). The spooler opens a file
            # port as the user who printed (not as SYSTEM), so users need to create,
            # change and delete files here; a link in this folder gains them nothing. Its own folder keeps that
            # away from the rest of the data folder (log\ and labels.tsv stay read-only for users).
            $name = $captureName
            Set-UserFilesFolder (Split-Path $capturePort)
            Add-LocalPort $capturePort
            if (-not (Get-Printer -Name $name -ErrorAction SilentlyContinue)) {
                Add-Printer -Name $name -DriverName "Brother QL-800 (Unidrv)" -PortName $capturePort -ErrorAction Stop
            } else {
                Set-Printer -Name $name -DriverName "Brother QL-800 (Unidrv)" -PortName $capturePort -ErrorAction Stop
            }
            "Test printer '$name' ready (prints to $capturePort)."
        }

        if ($UsbPrinter) {
            # -UsbDriverName: the USB printer's model when -DriverName is the network printer's (both in one run). Several
            # printers: -UsbPort USB003,USB004 -UsbDriverName "Brother QL-700 (Unidrv),Brother QL-1100 (Unidrv)" (pairs in
            # order; commas, as powershell -File passes one string)
            $ports = @(if ($UsbPort) { $UsbPort -split "," | ForEach-Object { $_.Trim() } } else { "" })
            $drivers = @(if ($UsbDriverName) { $UsbDriverName -split "," | ForEach-Object { $_.Trim() } })
            for ($i = 0; $i -lt $ports.Count; $i++) {
                $d = if ($drivers.Count) { $drivers[[Math]::Min($i, $drivers.Count - 1)] } else { $null }
                if (-not $d -and -not $mainModel) { throw "-UsbPrinter needs -UsbDriverName (or -DriverName): the printer's model." }
                $usbModel = if ($d) { $models | Where-Object { $_.Name -eq $d } | Select-Object -First 1 } else { $mainModel }
                if (-not $usbModel) { throw "Unknown -UsbDriverName '$d'." }
                Add-UsbPrinter $usbModel $ports[$i]
                # The Printer setup window's "Add printer" for a connected USB printer without a queue of ours: not shared
                # (user's choice 2026-11-06; sharing goes through Printer properties)
                Add-Result "printer" $script:usbAdded.Name; Add-Result "driver" $usbModel.Name; Add-Result "usb" $script:usbAdded.Port
                if ($script:usbAdded.Existed) { Add-Result "existed" "1" }
            }
        }

        if ($SharePort) {
            # A QL printer shared by another PC (docs/sharing.md): a Local Port named after the share, which
            # passes our driver's output on RAW; Point and Print is not used. The port may already exist
            # (e.g. for Brother's own printer on the same share).
            if (-not (Test-PrinterShare $SharePort)) {
                throw ("No printer can be reached as $SharePort. Check the name, that the other PC is on and shares the printer, and " +
                       "open \\$($SharePort.Split('\')[2]) in Explorer once with 'Remember my credentials' (docs/sharing.md on the project page).")
            }
            Add-LocalPort $SharePort
            # A printer of that name on another port (its USB printer, or another host's share) stays where it is.
            $name = $DriverName
            $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
            if ($existing -and $existing.PortName -ne $SharePort) {
                $name = "$DriverName on $($SharePort.Split('\')[2])"
                $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
            }
            Add-Result "existed" $(if ($existing -and $existing.DriverName -eq $DriverName) { "1" } else { "0" })
            if (-not $existing) {
                Add-Printer -Name $name -DriverName $DriverName -PortName $SharePort -ErrorAction Stop
            } else {
                Set-Printer -Name $name -DriverName $DriverName -PortName $SharePort -ErrorAction Stop
            }
            Clear-WorkOffline $name
            "Printer '$name' ready on $SharePort."
            Add-Result "printer" $name; Add-Result "driver" $DriverName; Add-Result "share" $SharePort
        }

        if ($NetworkPrinter) {
            # A QL printer on the network: the model from the printer itself unless -DriverName was given.
            $netDriver = $DriverName
            if (-not $scriptParameters.ContainsKey("DriverName")) {
                $result = Join-Path $env:TEMP "qlunidrv-identify.txt"
                Remove-Item $result -Force -ErrorAction SilentlyContinue
                Start-Process $app -ArgumentList "--identify", $NetworkPrinter, "--result", "`"$result`"" -Wait -WindowStyle Hidden
                $lines = @(Get-Content $result -ErrorAction SilentlyContinue)
                Remove-Item $result -Force -ErrorAction SilentlyContinue
                if ($lines.Count -lt 3 -or $lines[0] -ne "OK") {
                    throw ("The printer at $NetworkPrinter could not be identified: $(($lines | Select-Object -Skip 1) -join ' ')" +
                           " Give the model with -DriverName `"Brother QL-xxx (Unidrv)`".")
                }
                $netDriver = $lines[1]
                "Printer at ${NetworkPrinter}: $($lines[2]), $($lines[3]) loaded."
                Add-Result "roll" $lines[3]
            }
            if (-not (Get-PrinterDriver -Name $netDriver -ErrorAction SilentlyContinue)) {
                throw "Driver '$netDriver' is not installed here: install the driver first (Setup.exe, or this script without -AppOnly)."
            }
            $netPort = "IP_$NetworkPrinter"
            if (-not (Get-PrinterPort -Name $netPort -ErrorAction SilentlyContinue)) {
                Add-PrinterPort -Name $netPort -PrinterHostAddress $NetworkPrinter -PortNumber 9100
            }
            $name = $netDriver
            $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
            if ($existing -and $existing.PortName -ne $netPort) {
                $name = "$netDriver on $NetworkPrinter"
                $existing = Get-Printer -Name $name -ErrorAction SilentlyContinue
            }
            Add-Result "existed" $(if ($existing -and $existing.DriverName -eq $netDriver) { "1" } else { "0" })
            if (-not $existing) {
                Add-Printer -Name $name -DriverName $netDriver -PortName $netPort -ErrorAction Stop
            } else {
                Set-Printer -Name $name -DriverName $netDriver -PortName $netPort -ErrorAction Stop
            }
            Clear-WorkOffline $name
            "Printer '$name' ready on $netPort ($NetworkPrinter, port 9100)."
            Add-Result "printer" $name; Add-Result "driver" $netDriver; Add-Result "address" $NetworkPrinter; Add-Result "port" $netPort
        }
    }

    if ($Uninstall) {
        Invoke-Uninstall
        return
    }

    # -Settings: only settings of an installed setup (service, log, added printers); driver, label tool files and
    # label formats stay as they are, so it runs from the installed copy (C:\Program Files\QLUnidrv\setup) and
    # while the label tool is open (its setup window starts it; docs/setup.md).
    if ($Settings) {
        if (-not (Test-Path $app)) { throw "The driver and the programs are not installed here: run Setup.exe from the release zip." }
        $AppOnly = $true
    } else {
        Assert-LabelToolClosed   # before the driver is touched, so a refusal changes nothing
    }
    if ($NetworkPrinter -and $NetworkPrinter -notmatch "^[A-Za-z0-9][A-Za-z0-9.:-]*$") {
        throw "-NetworkPrinter must be the printer's IP address or host name (e.g. 192.0.2.50)."
    }
    if ($SharePort) {
        if ($SharePort -notmatch "^\\\\[^\\]+\\[^\\]+$") { throw "-SharePort must look like \\host\share (e.g. \\PC\QL-800)." }
        if (-not $DriverName) { throw "-SharePort needs -DriverName (the shared printer's model, e.g. `"Brother QL-570 (Unidrv)`")." }
        if ($AppOnly -and -not (Get-PrinterDriver -Name $DriverName -ErrorAction SilentlyContinue)) {
            throw "Driver '$DriverName' is not installed here: install the driver first (Setup.exe, or this script without -AppOnly)."
        }
    }
    # The download mark (Mark of the Web) of a zip unpacked with Explorer stays on copies (Copy-Item keeps the stream):
    # removed from what is installed from here, as Setup.exe does
    if (-not $Settings) {
        foreach ($d in "out\tools", "out\package", "labels", "driver") {
            Get-ChildItem (Join-Path $repo $d) -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue
        }
    }
    Initialize-DataFolder
    if ($Settings) {
    } elseif ($AppOnly) {
        "Programs only: driver, package and certificates unchanged."
    } else {
        Install-DriverPackage
    }
    if ($Settings) {
        "Settings only: driver, programs and label formats unchanged."
        Set-StatusServiceSetting
    } else {
        Install-Programs
    }
    Invoke-PrinterActions
    "Done."
    Add-Result "done" "1"
} catch {
    # Certificates this run added and no package in the driver store is signed with (the package step failed): removed
    foreach ($c in $script:addedCerts) {
        try {
            if (@(Get-StoreSigningThumbprints) -notcontains ($c -split "\\")[1]) {
                Remove-Item "Cert:\LocalMachine\$c" -ErrorAction Stop
                "Removed the certificate added by this run again (LocalMachine\$c)."
            }
        } catch { }
    }
    Add-Result "error" ($_.Exception.Message -replace "[\r\n]+", " ")
    Add-Result "code" (Get-ErrorCode $_.Exception.Message)
    throw
} finally {
    if ($script:runMutex) { $script:runMutex.ReleaseMutex(); $script:runMutex.Dispose() }
    if ($Log) { Stop-Transcript | Out-Null }
}
