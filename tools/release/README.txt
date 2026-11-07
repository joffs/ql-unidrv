QL Unidrv driver for Brother QL label printers (unofficial, not made by Brother)
==============================================================================

Version: programs {VERSION}, driver {DRIVER}   Contains the driver for: {ARCHS}
Project, documentation and help: https://github.com/joffs/ql-unidrv

What is in here
---------------
The compiled printer driver for 18 Brother QL models (QL-500 ... QL-1115NWB), three programs
(QL Label Formats: label sizes; QL Label Maker: design labels; QL Printer Status: printer status
and error notifications) and the setup (Setup.exe). The driver is not signed yet: the setup signs
it on your PC with your own certificate. The folder "advanced" holds what Setup.exe uses (driver
package, programs, scripts); you don't need to open it.

Install
-------
1. Unzip the whole zip (right-click -> Extract All; German: Alle extrahieren).
2. Connect and switch on the printer. QL-700/800/810W/820NWB: the "Editor Lite" LED must be off.
3. Double-click Setup.exe and follow the window (English or German, as Windows).
   If Windows says "Windows protected your PC" (the program isn't signed by a paid certificate):
   "More info" -> "Run anyway" (German: "Weitere Informationen" -> "Trotzdem ausfuehren").
   The window checks this PC, finds QL printers on USB, signs and installs the driver and the
   programs (one administrator prompt), turns on printer notifications if you like and prints a
   test label.
   ARM64 PCs: the window prepares one restart with key 7 ("Prepare and restart"; German:
   "Vorbereiten und neu starten"). Press 7 on the blue screen "Startup Settings" (German:
   "Starteinstellungen"); after you sign in the window opens again and installs. Windows then asks
   about the driver's publisher: "Install this driver software anyway" (German: "Diese
   Treibersoftware trotzdem installieren").
Afterwards the Start menu has QL Label Formats, QL Label Maker and QL Printer Status.

Day to day: "QL Label Formats" > menu Printer > Printer setup (German: Drucker >
Druckereinrichtung): add a network printer or a printer shared by another PC, the printer status
service, the driver log, uninstall. Or Windows Settings > Apps > Installed apps (German: Installierte
Apps) > "QL Unidrv printer driver" > Modify / Uninstall.
The programs speak English or German, as Windows does; to switch: QL Label Formats >
menu Tools > Language / Sprache.
Updating: unzip the new version and run its Setup.exe (printers and settings stay). When the driver
in it is the installed one, only the programs are updated: no signing, no restart.
Removing: Installed apps, the Printer setup window, or Setup.exe of this zip. It removes the driver,
the printers, the programs, the certificates and the program data (C:\ProgramData\QLUnidrv: logs,
label list); you can keep your label formats for a later install.

A QL printer on the network, added later (or a second one)
---------------------------------------------------------
In "QL Label Formats": menu Printer > Printer setup > "Add a QL printer on the network" (German:
Drucker > Druckereinrichtung > "QL-Drucker im Netzwerk hinzufuegen"). The printer is asked which
model it is. No restart with
key 7 needed, also on ARM64.

Printing to a QL printer shared by another PC
---------------------------------------------
On the PC the printer is plugged into, share the printer "Brother QL-xxx (Unidrv)" (Printer
properties -> Sharing; German: Druckereigenschaften -> Freigabe). On this PC:
1. Open \\host in Explorer (host = that PC's name or address), sign in as an account of that PC
   and tick "Remember my credentials".
2. Run Setup.exe as above (the driver must be installed here too).
3. Open the Printer setup window in QL Label Formats and give the share (\\host\QL-xxx) and the model.
   It adds a printer that sends to the share; no restart with key 7 needed, also on ARM64. A printer
   of the same name on USB stays; the new one is then called "Brother QL-xxx (Unidrv) on host".
Printer status on this PC too: on the PC the printer is plugged into, switch on "Printer status
service for shared printers" (German: "Druckerstatus-Dienst fuer freigegebene Drucker") in Printer
setup (docs/status-service.md on the project page). Details and limits: docs/sharing.md on the
project page.

Troubleshooting: the driver log
-------------------------------
The driver keeps no log unless you switch it on (for a bug report). In "QL Label Formats" open
menu Printer > Printer setup (German: Drucker > Druckereinrichtung) and tick
"Driver log for troubleshooting" (German: "Treiberprotokoll zur Fehlersuche"; one administrator
prompt). Print again; the log is C:\ProgramData\QLUnidrv\log\plugin.log (at most about 2 MB with
plugin.old.log). Attach it to your report together with C:\ProgramData\QLUnidrv\log\printer-status.log
(the printer's messages as the driver and the status service read them) and
C:\ProgramData\QLUnidrv\status\printer-status.log (as the programs read them; both always kept, small).
These two are a help, not a complete record: messages during a print may be missing.
Untick it afterwards. No restart with key 7 needed, also on ARM64.

Advanced: scripts (administrators)
----------------------------------
Everything Setup.exe does can be run by script, for unattended installs. Start PowerShell as
administrator and run (one line each; <zip folder> = this folder):
  Sign the package (once per zip, before an install):
    powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\package.ps1" -SignOnly -OneTimeKeys
  Install or update (ARM64: only in the start with key 7):
    powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" [-Update]
  Programs only (no driver change, no key 7):
    powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" -AppOnly
  Settings of an installed setup: -Settings with -NetworkPrinter 192.0.2.50, -SharePort \\PC\QL-800
  (with -DriverName "Brother QL-800 (Unidrv)"), -StatusService on|off, -Logging on|off.
  Uninstall: -Uninstall (add -KeepLabels to keep the label list).
The parameters are described at the top of install.ps1. advanced\driver\setup.ps1 is the same guided
setup as Setup.exe in a console window (English).

Why your own certificate?
-------------------------
Windows only installs signed drivers. Instead of asking you to trust the publisher's certificate
(which would let whoever holds its key sign any software your PC then trusts), each PC signs the
driver with its own certificate. Its private key is deleted right after signing, so nothing can
sign with it later; an update creates a new one.

Licence: MIT (LICENSE). The QR code encoder is ported from Project Nayuki's QR Code generator library (MIT,
THIRD-PARTY-NOTICES.txt). Brother and QL are trademarks of Brother Industries, Ltd.
