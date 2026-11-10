# Setup: install, update, change, remove

There are three ways in. They all use the same script underneath (`driver\install.ps1`), so they behave the same.

| Where | What it is for |
|---|---|
| **`Setup.exe`** in the release zip (x64 and ARM64) | First install, update, repair in one window, with a test label |
| **Printer setup** window (menu *Printer* in QL Label Formats and QL Label Maker) | Everyday changes without the zip: add, share and remove printers, options, uninstall |
| Windows **Installed apps** (*Installierte Apps*) | "QL Unidrv printer driver": *Modify* opens the Printer setup window, *Uninstall* the uninstall window |

The zip's top level holds only `Setup.exe` (with its `.config`), `README.txt`, `LICENSE` and
`THIRD-PARTY-NOTICES.txt`. Everything else is in `advanced\`, in the repository's layout one level down:
`advanced\driver\` (`install.ps1`, `setup.ps1`, `package.ps1`, `bind-device.ps1`, `check-signing.ps1`),
`advanced\out\package`, `advanced\out\tools`, `advanced\labels`.
Administrators can run the scripts there directly (`install.ps1`'s help: `Get-Help <zip
folder>\advanced\driver\install.ps1 -Detailed`; the zip's `README.txt` lists the usual commands).

## Versions

The driver and the programs have separate versions. The **programs** (QL Label Formats, QL Label Maker, QL Printer
Status, `Setup.exe`) are `major.minor.patch`, e.g. 0.18.1; the zip is named after them
(`ql-unidrv-0.18.1-x64-arm64.zip`). The **driver** keeps the version of the release in which it last changed, shown
as "0.18". A release with only program changes raises the patch (0.18.2) and leaves the driver as it is, so updating
to it needs no signing and no restart. The zip's `README.txt` names both versions.

## Setup.exe (release zip)

Extract the whole zip (*Extract All*; German: *Alle extrahieren*), double-click `Setup.exe`. Windows may first say
"Windows protected your PC" (the exe has no paid signature): *More info* > *Run anyway* (German: *Weitere
Informationen* > *Trotzdem ausführen*).

One window with four steps, each with its state:

1. **Check this PC** (runs by itself): the PC's architecture, the installed driver and programs and this zip's
   (install, update of driver and programs, update of the programs only, repair or an older version), the QL printers on USB with their loaded roll (asked directly, before any
   driver is there). Opened from inside the zip it says to extract first; it removes the download mark from the
   extracted files itself (no *Unblock* needed).
2. **Sign the driver for this PC** (`package.ps1 -SignOnly -OneTimeKeys`, not elevated; Windows' own functions,
   nothing to install).
3. **Install the driver and the programs** (`install.ps1`, one administrator prompt; its steps are shown while
   it runs). Before it: *Notify me about printer errors* (ticked; the printer monitor for the signed-in user), the
   status service only when this PC shares a QL printer, and a link for a QL printer on the network (asked for its
   model by SNMP, or chosen).
4. **Print a test label** on a printer the install set up (the roll is read first, then asked).

**Programs only:** when this zip's driver is the installed one and only the programs differ, the window offers
*Update the programs to 0.18.2* (German: *Programme auf 0.18.2 aktualisieren*; older programs: *Go back to the older
programs*). It runs `install.ps1 -AppOnly`: no signing, no restart with key 7, no restart; the steps that don't apply
are hidden. Printers, settings and label formats stay.

If something fails, the step says why in plain words, with *Show details* and *Try again*. `QlLabelFormats.exe --install
[<release folder>] [--after-restart]` opens the same window.

- The check stops with a plain sentence when Windows would block the driver anyway: *Smart App Control* is on
  (*Intelligente App-Steuerung*; it blocks programs without a paid signature), or *Windows protected print mode* is
  on (only Windows' own printer drivers).
- Only one install runs at a time (`install.ps1` holds a system-wide lock): a second window says a setup is already
  running.
- Closing the window while it signs or waits for the administrator prompt stops the setup (it asks first). Once the
  install itself runs, the window only hides: the install finishes, and the window turns on the notifications and
  (ARM64) the check after the restart, then closes.
- A failed install takes back the certificate it added, unless a driver package Windows kept is signed with it.

**ARM64 PCs** get one more step, **Restart with key 7** (German: *Neustart mit Taste 7*): Windows on
ARM64 installs the self-signed driver only after a restart with *Disable driver signature enforcement* (key 7). The
window sees by itself whether this start is one (code integrity state and the boot's start options, read without
administrator rights). Each step shows one line of what to do now; the why and the details are behind the (i) next to
its title (tooltip, or click).

- **Normal start:** *Prepare and restart* (*Vorbereiten und neu starten*): one administrator prompt for
  `bcdedit /set {current} onetimeadvancedoptions on` (Windows shows the Startup Settings once at the next start), an
  entry under the user's `RunOnce` that reopens the window after sign-in, then *Restart now?*. On the blue screen
  *Startup Settings* (*Starteinstellungen*) press 7 (or F7); Enter starts Windows normally instead. The link *What the
  blue screen looks like (picture)* opens a drawn picture of it in a small window beside the setup; a second click
  (*Hide picture*) closes it. If the detection is wrong, the link *I pressed 7 already* goes on anyway; a refused
  package then leads back here.
  On a PC encrypted with BitLocker (device encryption) the step and the restart question add that Windows may ask for
  the recovery key after the restart, and where it is (aka.ms/myrecoverykey).
- **Reopened, but Windows started normally** (7 not pressed): "Key 7 wasn't pressed" with *Try again*.
- **After the restart with key 7:** the install as on x64. Before Windows' publisher dialog the step says to click
  *Install this driver software anyway* (*Diese Treibersoftware trotzdem installieren*), with a link to a picture of
  it. At the end an orange note asks to restart normally soon (in this start Windows doesn't check driver signatures)
  and the window sets itself to open once more. After the test label the main button is *Restart now* (it asks first;
  a normal restart, other programs are closed) and *Later* closes the window.
- **After that normal restart:** the check, clearly finished: a box *Done – the driver is installed*
  (*Fertig – der Treiber ist installiert*), signature checks on again, and the main button *Done*
  (*Fertig*), which closes the window. A test label printed before the restart shows as done (with *Print another*);
  otherwise the step offers only the link *Print test label*. The link *Check the driver's signatures (report)*
  runs `check-signing.ps1` (elevated, read-only; the report opens in Notepad).

Steps that need no driver package (an already installed version, a printer added with `-Settings`) need no special
start. `install.ps1 -ResultFile` writes `step=` lines while it runs and
`code=` on failure, which the window reads.

## Console setup for administrators (advanced\driver\setup.ps1)

The same guided setup as `Setup.exe`, in a console window (English). Unzip the release (first: right-click the zip >
*Properties* > tick *Unblock*; German: *Eigenschaften* > *Zulassen*), then in PowerShell:

```
powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\setup.ps1"
```

- **Nothing installed yet:** the guided install. It signs the driver with a certificate made for this PC, on ARM64 PCs guides you through the restart with key 7, asks about a network
  printer and the printer status service, and installs the driver and the programs (one administrator prompt).
- **Already installed:** a menu.

```
Installed: programs 0.18.1, driver 0.18   This zip: programs 0.18.2, driver 0.18

  1. Update the programs to 0.18.2 (the driver 0.18 stays: no signing, no restart)
  2. Add a QL printer on the network (IP address)
  3. Add a QL printer shared by another PC
  4. Printer status service 'QL printer status': off -> turn on (docs/status-service.md)
  5. Driver log for troubleshooting: off -> turn on
  6. Open the printer status window
  7. Uninstall
  0. Exit
```

  1. depends on the zip: with the installed driver and other programs it updates only the programs
     (`install.ps1 -AppOnly`); with a newer driver it reads *Update the driver and programs to ...* and runs the full
     guided install (also when only the programs are there; on ARM64 it needs the restart with key 7, as a first
     install does). The same zip again = repair; an older driver is said so and asked again.
  2. asks for the address; the printer is asked which model it is (SNMP); if it doesn't answer you choose the model.
  3. asks for the share (`\\PC\share`) and the model. Before that, open `\\PC` in Explorer once and sign in with
     *Remember my credentials* ([sharing.md](sharing.md)).
  4. / 5. switch the option; the menu shows the current state.
  7. the guided uninstall (asks whether to keep your label formats).

  Items 2-5 change settings only (`install.ps1 -Settings`): no driver install, so no restart with key 7 on ARM64.

`setup.ps1 -Uninstall` goes straight to the uninstall.

## Printer setup window

*QL Label Formats* or *QL Label Maker* > menu *Printer* > **Printer setup** (German: *Drucker* >
*Druckereinrichtung*), or *Installed apps* > *QL Unidrv printer driver* > *Modify*, or `QlLabelFormats.exe --setup [--select "<printer>"]` (`--select`: that printer chosen in the list; the status
window's card for a printer on a gone share opens it so, for *Remove printer*).

- **QL printers on this PC:** the list (up to 8 rows, more scroll; a shared printer shows ", shared as <name>"; a printer on `\\host\share` shows
  "– no longer shared" (*nicht mehr freigegeben*) or "– not reachable" (*nicht erreichbar*), from a background check
  after every reload with `PublishedStatus.CheckShare`, the same check as the status window's card (NetShareGetInfo
  level 1 and the queue's SHARED flag, cached 30 s), at most 5 s per share (no answer = not reachable), so the window
  never waits for an offline host; *Remove printer* for such a printer doesn't contact the host (all local; tested in
  the x64 Sandbox by `driver/test/share-test.ps1` cases p/q); at least 4 also with the lines below it, 690 px;
  no line above it while it shows printers, the band counts them). For the selected printer, below it and in its
  context menu: *Remove printer* (*Drucker entfernen*; also Del) asks first (*No* preselected, the text says what is
  removed for its port), then runs `install.ps1 -Settings -RemovePrinter <name>` elevated: the printer, its waiting
  jobs and its `IP_...`, `\\PC\share` or file port when no other printer uses it. The driver package stays. The status service
  is stopped meanwhile and a shared printer is unshared first (the question says, for a shared one, that PCs printing
  to it may keep it open). One still held stays hidden, pending deletion, until it is released or the print spooler
  restarts: as a rule another PC that prints to the share or reads its status (its spooler's handle cache), which this
  PC can't release. A message then names the remedy (on that PC restart the service *Print Spooler*, or restart one of
  the two PCs; result key `was-shared`), the bottom line says "is being removed" with the details in its (i); its own
  port stays, unused. No button restarts this PC's spooler (on ARM64 that dropped the USB device to Windows' generic
  driver). A USB printer doesn't come back by itself. *Printer properties* (*Druckereigenschaften*;
  also double-click) opens Windows' properties dialog of that printer (`printui.dll,PrintUIEntry /p`), with the
  user's rights; the list is read again when it closes.
- **A QL printer connected by USB without a printer of ours** (after *Remove printer*, or Windows gave the device its
  generic or Brother's driver): one line below the list for all of them, "QL-800 on USB001 is not set up here yet." or
  "3 QL printers on USB are not set up here yet.", with *Add printer* (*Drucker hinzufügen*; with several a menu of
  them) and an (i) (why, what it does, Brother's own printer on the same port: it stays). The line follows devices
  plugged in, unplugged or switched off while the window is open (WM_DEVICECHANGE), and a click checks again.
  It runs `install.ps1 -Settings -UsbPrinter -UsbPort USB001 -UsbDriverName <driver>` elevated: our driver on that port,
  not shared, the USB device is not rebound (no maintenance window, also on ARM64). Found without administrator rights:
  present devices under `Enum\USBPRINT` (instance `...&USB001` = the port, the key names the model), no printer with
  one of our drivers on the port (`UsbPrinters.cs`).
- **Tabs** below the list (owner-drawn `TabBar`: Left/Right/Home/End on the bar, Ctrl+Tab / Ctrl+Shift+Tab in the
  window; all pages share one host as tall as the tallest page, so switching doesn't change the window height; the last
  tab is remembered per user, `HKCU\Software\QLUnidrv` value `SetupTab`):
  - **Network printer** (*Netzwerkdrucker*): IP address, model *(ask the printer)* or chosen, *Add*.
  - **Another PC's printer** (*Drucker eines anderen PCs*): share and model, *Add*. No model is chosen in advance; a
    share named after its model (`\\PC\QL-800`) picks it.
  - **Share a printer** (*Drucker freigeben*): one of our printers on a USB port (`USB\d{3}`) for other PCs. *Printer*
    (all of them, shared ones with their share name), *Share name* (suggested: the model, then "QL-800 (2)" ...;
    a shared printer shows its name), *Share printer* (*Freigeben*) or, when shared, *Rename* (*Namen ändern*) and
    *Stop sharing* (*Freigabe beenden*), each asks first (*No* preselected: PCs printing through the old name lose
    it). The name is checked while typing (`Sharing.NameProblem`, the same rules as `install.ps1`): 1-80 characters,
    none of `" / \ [ ] : | < > + = ; , ? *`, not used by another printer share or a folder share on this PC (case
    doesn't matter; read without administrator rights from the `Printers` and `LanmanServer\Shares` registry keys).
    A line above says when other PCs can't reach the share: the *Server* service stopped, the firewall group *File and
    Printer Sharing* off for the current profile, or the network set to public (`HNetCfg.FwPolicy2`), with *Open
    settings*. Without a USB printer of ours the tab says so (network printers are added by address on every PC). The
    printer status service ([status-service.md](status-service.md)) is switched here too. After *Add printer* for a USB
    printer the result offers *Share printer*, which opens this tab with that printer.
    Runs `install.ps1 -Settings -SharePrinter <name> -ShareName <share>` (shares, or renames the share; result keys
    `shared`, `sharename`, `renamed-from`) and `-UnsharePrinter <name>` (result key `unshared`). Tested in the x64
    Windows Sandbox by `driver/test/share-test.ps1` (17 cases: share, rename, same name again, names taken by a
    printer in other case or a folder, invalid names, a printer not on USB, unknown printer, unshare twice, and removing a printer
    on a share whose host doesn't answer or whose share is gone: 3 s each).
  - **Options** (*Optionen*): the driver log (`C:\ProgramData\QLUnidrv\log\plugin.log`, for troubleshooting).
- **Bottom row:** *Uninstall* opens the uninstall window (below); *New version: download page* opens the release
  page (an update needs the new driver package); its (i) says how to repair: start `Setup.exe` from the unzipped
  download again (printers, settings and label formats stay).
- **Size:** it fits a working area of 690 px at 100 % (1080p at 150 %); on a screen too small for it the middle part
  scrolls and the band and the bottom row stay.

Adding a printer first shows what will be added (for a network printer: the model and loaded roll the printer
reported) with *Add printer* / *Cancel*. Each change then asks for administrator rights once (a progress dialog
shows what it waits for) and ends with a result dialog: what was done, and the next steps - after adding a
printer *Print test label* (asks the printer for the loaded roll and prints one small label on it, after a
question; nothing is printed when the roll is unknown), *Open print queue*, *Set as default printer*; after
turning the driver log on *Open log folder*. A printer that was already set up the same way says *Already set
up*. A share is checked before it is added. If something fails, the
dialog says why, with *Show details* / *Copy details* (the script's output). The list of QL printers at the top
scrolls after eight. The bottom of the window keeps
a one-line note of the last change. The window may stay open while you use QL Label Formats.

`install.ps1 -Settings` writes what it did to `-ResultFile` (key=value lines), which the window reads. With
`QL_TEST_SILENT=1` (UI tests) the window doesn't start it: it logs the arguments to `%TEMP%\ql-unidrv-test-run.txt`.

## Uninstall window

*Installed apps* > *QL Unidrv printer driver* > *Uninstall* (*Deinstallieren*), *Uninstall* in the Printer setup
window, or the link *Uninstall* at the bottom left of the setup window (when something is installed) open the
uninstall in the setup window, in Windows' language (`QlLabelFormats.exe --uninstall`). The installed exe starts a copy of
itself in `%TEMP%` (it can't remove its own folder while it runs); the copy deletes itself when it closes.

- **Before:** what will be removed – the QL printers by name (a shared one in orange: other PCs can't print through
  it any more), the programs, the driver, its certificate and the logs – and *Keep my label formats* (ticked; the
  list stays in `C:\ProgramData\QLUnidrv` for a later install). *Uninstall* / *Cancel* (the default).
- **While it runs:** *Close the programs* (an open QL Label Formats or QL Label Maker is asked to close first), then *Remove
  printers, driver and programs* with the turning ring; one administrator prompt (`install.ps1 -Uninstall
  [-KeepLabels]`).
- **Done:** *Done – everything is removed.*, with the kept label formats named. A failure says so, with *Show details*
  and *Try again*.

`advanced\driver\setup.ps1 -Uninstall` in the zip keeps the console way (it also accepts J for yes).

## Installed apps entry

Every install or update writes `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\QLUnidrv` (name,
version, publisher, project link) and copies `install.ps1` and `setup.ps1` to `C:\Program Files\QLUnidrv\setup`
(administrators write, users read), so uninstall and the setup window work without the zip; the entry shows the
programs' size and version (`DisplayVersion`, e.g. 0.18.1), and the value `DriverVersion` holds the driver's (0.18). The Start menu gets *QL Label Formats*, *QL Label Maker* (the label editor) and *QL Printer Status*; each exe is named like its entry (`QlLabelFormats.exe`,
`QlLabelMaker.exe`, `QlPrinterStatus.exe`); `QlLabelPrint.exe` (printing from the command line,
[print-command-line.md](print-command-line.md)) has no Start menu entry. The uninstall removes the entry, the folder and these Start menu entries, and also: for every signed-in user the printer
monitor's autostart, the programs' settings (`HKCU\Software\QLUnidrv`), "Open with" entries and the removed printers'
per-user settings; shortcuts users made to the programs (desktops, taskbar pins; only those that start one of them); the ports it made for shared and network printers (when no other printer uses them); the status service's event
log source; the copies of this PC's signing root among the user's intermediate certificates. The logs in `%TEMP%`
are deleted after every run that finished, also those earlier runs left (kept after a failure, which names them). Nothing is left behind.
Started from the Printer setup window inside QL Label Formats, *Uninstall* first closes QL Label Formats (it asks
about unsaved changes). Started from QL Label Maker, or with either program open elsewhere, the uninstall window asks
to save and close it first ("QL Label Maker" is open. Save your changes and close it, then click OK.).

## How it fits together

| Piece | Role |
|---|---|
| `driver\install.ps1` | Does all the work, as administrator. `-Settings`: only settings of an installed setup (service, log, added printers) - no driver, no program files, no label formats; runs from the installed copy and while the label tool is open |
| `tools\release\setup.ps1` (zip: `advanced\driver\setup.ps1`; installed: `C:\Program Files\QLUnidrv\setup\setup.ps1`) | The guided install and the menu; starts `install.ps1` elevated and shows its result. Without a package (installed copy) the menu has no update item |
| `labeltool\src\InstallForm.cs` (zip: `Setup.exe`, the programs' sources built with the setup icon; it opens the setup window because of its name) | The setup window; signs unelevated, runs `install.ps1` elevated |
| `labeltool\src\SetupForm.cs` | The Printer setup window; runs `C:\Program Files\QLUnidrv\setup\install.ps1 -Settings ...` elevated |
