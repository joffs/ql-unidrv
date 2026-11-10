# Driver architecture

How the QL Unidrv driver is built, what runs where at print time, and which files it uses. For the models see [models.md](models.md).

## 1. Why a Unidrv driver

Brother's QL drivers are custom v3 GDI drivers for x86/x64 only. The print spooler on Windows on ARM64 is a native ARM64 process and cannot
load x64 DLLs, so Brother's binaries cannot be reused there. Instead the driver describes each printer to
Windows' own universal printer driver (Unidrv, `unidrv.dll` / `unidrvui.dll`, part of Windows on every
architecture) with a GPD file and adds a small render plug-in that produces Brother's raster stream. Unidrv
provides rendering, halftoning, the printing preferences dialog, paper handling and PrintTicket support.
The same sources build for ARM64 and x64.

## 2. Components

| Component | Files | Role |
|---|---|---|
| INF | `driver/inf/qlunidrv.inf` | One package for all models, sections `NTarm64` and `NTamd64`; uses Microsoft's Unidrv core driver (`CoreDriverSections`, `UNIDRV.OEM` from `ntprint.inf`) |
| GPD, one per model | `driver/gpd/QLUnidrv<model>.GPD` (+ `.INI` naming the plug-in) | Model name, resolutions, paper sizes, custom-size range, options. Generated from `template.GPD.in` |
| Render plug-in | `QLUnidrv.dll` (`driver/src/plugin.cpp`, `ql_raster.*`, `labels.*`, `usb_status.*`) | `IPrintOemUni` plug-in loaded by Unidrv in the spooler: collects the rendered page and writes the QL stream |
| Encoder | `driver/src/ql_raster.*` | Pure C++ (no Windows headers): media table, per-model traits, page geometry, raster lines, PackBits, job/page commands. Unit- and replay-tested |
| Generator | `driver/gen/media.py` | Brother's paper tables (`docs/data/*.json`) + `MODELS` → all GPDs, `src/media_table.inc`, `src/media_info.inc` (Brother's `ESC i U w` media blocks per model and paper, from the captures), `labeltool/src/MediaTable.g.cs` |
| Label tool | `labeltool/` → `QlLabelFormats.exe`, `QlLabelMaker.exe`, `QlPrinterStatus.exe`, `QlLabelPrint.exe` (one source folder, four programs) | `QlLabelFormats.exe`: user label formats, default format, Printer setup window (`--setup`, [setup.md](setup.md)); `QlLabelMaker.exe`: label editor, `.qllabel` files ([label-editor.md](label-editor.md)), the same Printer setup window in its *Printer* menu; `QlPrinterStatus.exe`: printer status window and tray monitor; `QlLabelPrint.exe`: console program, prints a `.qllabel` with CSV/JSON data ([print-command-line.md](print-command-line.md)) |
| Status service (optional) | `QlPrinterStatus.exe --service` (`labeltool/src/StatusService.cs`, `PublishedStatus.cs`) | Windows service "QL printer status": reads the USB printers, publishes their status in the print queues' printer data for every user and for PCs printing through a share ([status-service.md](status-service.md)) |
| Scripts | `driver/*.ps1`, `driver/build.cmd` | Build, package and sign, install, verify (§6) |

## 3. Print path

1. An application prints with the printer's DEVMODE. Unidrv reads the model's GPD; every option and paper
   selection only triggers a **command callback** to the plug-in (the GPD emits no printer bytes itself):

   | Callback ID | Meaning |
   |---|---|
   | 1 / 41–57 | StartDoc; tells the plug-in the model: QL-500 46, QL-550 45, QL-560 44, QL-570 43, QL-580N 53, QL-600 57, QL-650TD 47, QL-700 48, QL-710W 49, QL-720NW 50, QL-800 1, QL-810W 41, QL-820NWB 42, QL-1050 51, QL-1060N 52, QL-1100 54, QL-1110NWB 55, QL-1115NWB 56 (40 + `ql::Model`; the QL-800, model 0, has 1). The IDs are fixed in the order the models were added and never renumbered |
   | 2, 3, 4, 5, 9 | StartPage, EndPage, EndDoc, copies, no-op |
   | 10/11, 12/13 | auto cut on/off, cut at end on/off |
   | 14/15, 16/17 | 300 × 300 / 300 × 600, speed / quality priority |
   | 18/19, 20/21 | mirror off/on, one colour / black and red |
   | 30/31, 32/33 | trim trailing blank off/on, rotate 0/180 |
   | 199 | custom size (width, length) |
   | 200 + n | cut every n labels |
   | 10000 + feed × 100 + index | paper: index into the media table, feed in dots |

2. Unidrv renders each page and hands the plug-in 1 bpp bands (24 bpp for black and red). The plug-in
   collects the page; landscape pages are rotated 90° clockwise as Brother does.
3. Page and document ends come from hooked `DrvSendPage` / `DrvEndDoc` (GPD end-page callbacks never
   arrive). A page is held back until the next page or the end of the job, because the last page ends with
   `1A` instead of `0C`.
4. The encoder writes the job: invalidate and `ESC @`, then per page the commands of the model's traits, the
   raster lines (compressed on non-USB ports for the models that do, judged from the queue's port name) and
   the page end. Multi-up papers are split into one label per strip.
5. Paper selection, in order: a user label format (the DEVMODE's form name found in `labels.tsv`) wins; else
   the GPD paper; a custom size is continuous tape of the nearest width (a same-size die-cut paper that Unidrv
   substitutes is turned back into continuous tape). A paper the model doesn't have prints as continuous tape
   of the same width and length.
6. Black and red on a USB printer: before any output the plug-in asks the printer for the loaded roll
   (`ESC i S`, once per job) and prints black if the roll is not the red/black DK-22251, because a two-colour
   job on a black roll stops the printer.

The QL-800 family, the QL-600, the QL-1100 series and the QL-1060N's network form are written exactly as Brother's driver writes them; for
the models whose Brother driver relies on a language monitor (QL-500/550/560/570/650TD/1050) our stream is
what that monitor sends to the printer.

## 4. Label formats

Brother's label format tool stored custom label lengths in its driver data file. Here a label format is
a **Windows form** (user form inside the GPD's custom-size range) plus one line in
`C:\ProgramData\QLUnidrv\labels.tsv`:

```
# QLUnidrv labels 1
<form name> TAB <Brother paper id> TAB <length in 0.1 mm> TAB <feed in dots>
```

The plug-in looks up the selected form there (`src/labels.*`) and takes paper, length and feed from it, so
label formats change without reinstalling the driver. The label tool keeps the master list in
`C:\ProgramData\QLUnidrv\labels.json` and applies it (forms + `labels.tsv`, one administrator prompt; `--apply`,
`--sync`, `--plan`, `--remove-forms`). It only changes forms it created.

**Paper number 256.** Windows numbers paper sizes by their position in the machine-wide forms list, and number 256
is also its code for "custom size" (`DMPAPER_USER`): the paper at position 256 can't be selected, and Unidrv
matches custom sizes to it. So whenever the label tool applies label formats (also `--sync` at every install) it
keeps position 256 for a placeholder form "Reserved: paper number 256" (5 × 5 m, larger than any QL printer
accepts): it adds it when the list reaches 255 forms, and if a real paper already sits at 256 it moves its own
label formats to the end of the list until the placeholder can take that place. If no label format can move, it
warns. `--remove-forms` removes the placeholder too.

## 5. Label tool and printer status

`QlLabelFormats.exe` (Start menu "QL Label Formats"; .NET Framework 4.8, AnyCPU):

All four exes (QL Label Formats, QL Label Maker, QL Printer Status with its tray monitor and service, Setup.exe) declare
`supportedArchitectures` "amd64 arm64" in `labeltool/app.manifest`. On ARM64 Windows 11 24H2 and later they then run
natively as ARM64 when an ARM64 program starts them (Explorer, the Start menu, the service manager); without it an
AnyCPU .NET Framework exe runs under x64 emulation there, with about twice the start time. Started from an x64
program they still run as x64. Prints are byte-identical either way, and the driver's printing preferences open in
the program's own process instead of through splwow64 (`labeltool/test/arch-check.ps1`).

Window fonts (`labeltool/src/UiFont.cs`): GDI+ builds a table of every installed font in each process on its first
font by name, and opens every per-user font file for it: seconds after a restart on a PC with many fonts. The windows'
fonts therefore come from a private font collection with only the files of the UI faces (Segoe UI, the message and
menu fonts), WinForms' own default fonts are set to them at start, and the font list of QL Label Maker is filled when
it is first used. Drawing is unchanged (TextRenderer picks the font by name). Labels still use the installed fonts.
`QL_UIFONT=off` restores the old way; `labeltool/test/font-table-check.ps1` checks that no window loads the table.

- edit, order and test-print label formats; formats a model or the loaded roll can't print are greyed and
  explained; "Use as default label format" sets the per-user default paper (no administrator rights);
- printer status (Start menu "QL Printer Status", `QlPrinterStatus.exe`, `PrinterStatusProgram.cs`; also F6 in the
  label tool; `QlLabelFormats.exe --status` forwards to it): opens the printer's usbprint interface
  directly, drains the printer's own 32-byte messages and sends `ESC i S` only while the print queue is empty;
  errors such as wrong media are explained with the fix. While the optional service "QL printer status" runs,
  it is the only reader and the tool reads its published status (printer data `QLUnidrv\Status` of the queue);
  printers on a share (`\host\share`) are read the same way from the host's queue
  ([status-service.md](status-service.md)); printers on a Standard TCP/IP port with one SNMPv1 GET of Brother's
  status OID, the same 32 bytes (`NetworkStatus.cs`);
- label editor "QL Label Maker" (`--editor`, `EditorForm.cs` and `Editor*.cs`, [label-editor.md](label-editor.md)):
  renders a label in black and white at the printer's dots (`LabelPrinter.Measure` asks the driver for the
  printable area of the chosen form) and prints that bitmap 1:1 with `StretchDIBits`, so the screen shows exactly
  what prints (checked dot for dot on a capture printer: `labeltool\test\editor_spike.ps1`, `tools\prn_raster.py`);
- optional tray monitor (`QlPrinterStatus.exe --monitor`, started with Windows via `HKCU\...\Run`) that shows these errors as notifications while
  other programs print;
- Windows' *Use Printer Offline* on a QL queue (`PRINTER_ATTRIBUTE_WORK_OFFLINE`: jobs wait in the local queue
  without an error, whatever the printer reports) replaces the diagnosis in the status window, the monitor, the
  tip bar and before a test print; *Use printer online* clears it (`SetPrinter` level 2 without the security
  descriptor; when Windows denies that, through an elevated `--online <printer>`);
- English or German (`Lang.cs`; the texts in the code are English, `Strings.de.cs` maps them, `lang_test.cs` checks
  that every text has a German entry): the menu *Tools > Language / Sprache* stores the choice in `HKCU\Software\QLUnidrv\Language`
  (`en` / `de`), else the Windows display language decides (German for `de-*`). The command-line modes that
  scripts read (`--sync`, `--plan`, `--identify`, `--remove-forms`, `--service`) always write English; Save & apply
  passes `--lang` to its elevated `--apply`.
- one look for all windows (`Brand.cs`): petrol accent instead of Windows blue (primary button, links, (i),
  selection), the petrol band with a peeling corner in page-style dialogs (`HeaderBand`), the About dialog
  (`AboutForm.cs`) and the tray state icons. The icons (`labeltool\assets\*.ico`, one per program, the `.qllabel`
  file icon, tray states) and the README logo are drawn by `labeltool\assets\make-icons.ps1`, QL Label Maker's own
  mouse pointers (`pan-move.cur`, the move cross while panning; `EditorCursors.cs` picks the size per DPI) by
  `labeltool\assets\make-cursors.ps1` (both checked in; the build does not run them).
- light or dark (`Theme.cs`: named colour roles for light, dark and High Contrast; `labeltool\test\check-colours.ps1`
  in the build allows no colour outside it): *Tools > Appearance* (*Like Windows* / *Light* / *Dark*) stores the
  choice in `HKCU\Software\QLUnidrv\Theme` and switches every open QL window at once; with High Contrast Windows'
  colours apply and the menu is greyed. The label in QL Label Maker stays white.
- one printer status window per user session: the process that shows it (the tray monitor, else
  `QlPrinterStatus.exe`) waits on the event `Local\QLUnidrv.StatusWindow.Show`; F6 in QL Label Formats, *Printer
  status* in QL Label Maker, the Start menu entry and a second start bring that window to the front.
  `HangLog.cs` watches its UI thread: after 15 s without a response it writes the thread's stack to
  `C:\ProgramData\QLUnidrv\status\hang.log` (once per stall, below 256 KB).

## 6. Build, package, install, verify

| Step | Command | Notes |
|---|---|---|
| Build | `build.cmd [this\|x64\|arm64\|both]` (= `driver\build.cmd` + `labeltool\build.cmd`; double-click: asks) | On an ARM64 or x64 PC; output `out\x64`, `out\arm64` (plug-ins), `out\driver` (GPDs, INIs, INF). VS 2022 with ARM64 and x64 C++ tools, Windows SDK (no WDK), Python 3. Runs the generator, `encoder_test`, `labels_test`, `replay_test` for all 18 models' captures, the label tool's `status_test`; output in `out\`. The driver's version comes from `driver\VERSION` (`driver\gen\version.py`: release, `DriverVer`, a hash of the driver sources), stamped into the INF and GPDs; changed sources give a development build with the build time as stamp. The programs' version comes from `labeltool\version\VERSION` |
| Package | `driver\package.ps1 [-Arch x64\|arm64\|both] [-Stage\|-SignOnly] [-OneTimeKeys] [-Out dir]` | Keeps the build's `DriverVer`, stages `out\package` (`arm64\` and/or `amd64\` plug-ins: what the last build built; a one-architecture INF lists only that one), creates the private root CA and signing certificate on first run (non-exportable keys), builds and signs the catalog (Windows' own functions: wintrust.dll's catalog functions, `Set-AuthenticodeSignature`; no SDK since 2026-10-28). `-OneTimeKeys`: a new pair for this package, private keys deleted after signing |
| Release | `tools\make-release.ps1` | `ql-unidrv-<programs' version>-x64-arm64.zip`; refuses while the driver sources differ from `driver\VERSION` (a driver change first needs `python driver\gen\version.py set`, see [setup.md](setup.md#versions)). Zip with `Setup.exe` at the top and the unsigned package, programs and scripts under `advanced\` (signs on the user's PC with their own one-time CA: `package.ps1 -SignOnly -OneTimeKeys`, nothing to install) |
| Install | `driver\install.ps1 [-Update] [-TestPrinter] [-UsbPrinter] [-SharePort \\host\share] [-AppOnly]` | Administrator. Certificates into `LocalMachine\Root` / `TrustedPublisher`, `pnputil`, all 18 drivers registered, USB devices bound (`bind-device.ps1`), label tool installed |
| Verify | `driver\compare.ps1 [-Printer … -Captures …]`, `driver\check-labels.ps1`, `driver\check-signing.ps1` | Prints the reference test page per case through the installed driver and compares with Brother's captures; checks every label format; checks signatures after a restart |

**Signing.** The package is signed by a private CA. **x64** accepts it with Secure Boot and signature
enforcement on once the CA is trusted (only the UAC prompt). **ARM64** refuses self-signed driver packages
while code integrity is enforced, so an install or update there needs a one-time "Disable driver signature
enforcement" boot (F7); the installed driver keeps working afterwards with Secure Boot on.
Install steps: the README.

## 7. Files on an installed machine

| Path | Content |
|---|---|
| Driver store `qlunidrv.inf_<arch>_*` | GPDs, INIs, `QLUnidrv.dll` |
| `C:\Program Files\QLUnidrv\QlLabelFormats.exe`, `QlLabelMaker.exe`, `QlPrinterStatus.exe`, `QlLabelPrint.exe` | Label tool, label editor, printer status (window, tray monitor, service), printing from the command line ([print-command-line.md](print-command-line.md)); `.qllabel` files open in `QlLabelMaker.exe` (`HKLM\Software\Classes\QLUnidrv.Label`) |
| `C:\Program Files\QLUnidrv\setup\` | Copies of `install.ps1` and `setup.ps1` for uninstall and the Printer setup window; Installed apps entry `HKLM\...\Uninstall\QLUnidrv` ([setup.md](setup.md)) |
| `C:\ProgramData\QLUnidrv\labels.json` | Master list of label formats (+ the forms the tool owns) |
| `C:\ProgramData\QLUnidrv\labels.tsv` | Label formats as read by the plug-in |
| `C:\ProgramData\QLUnidrv\log\plugin.log` | Plug-in log, only while the file exists (`install.ps1 -Logging on`; off by default; moved to `plugin.old.log` at 1 MB) |
| `C:\ProgramData\QLUnidrv\log\printer-status.log` | Messages the plug-in read from a printer (roll check before a black-and-red job; emptied at 256 KB) and those the status service read (it runs as SYSTEM; kept at 256 KB plus `printer-status.old.log`) |
| `C:\ProgramData\QLUnidrv\status\printer-status.log` | Messages the label tool and the printer monitor read from printers (the tool also reads the plug-in's); `status\` is the only folder users can write to |
| `C:\ProgramData\QLUnidrv\status\hang.log` | Only after QL Printer Status stopped responding for 15 s: its UI thread's stack, for a bug report |
| `C:\ProgramData\QLUnidrv\capture\*.prn` | Output of the file-port test printers ("QL-… Capture") used by `compare.ps1` |

Rights (since the security audit of 2026-10-23): `C:\ProgramData\QLUnidrv` and everything in it belong to the
Administrators; administrators and SYSTEM may write, users only read. That includes `log\`: the plug-in writes there
with the spooler's rights, and a folder users could change would let them redirect those writes with a junction. The plug-in runs in the spooler's
PrintIsolationHost (SYSTEM) but impersonates the job's user; it writes its logs on a short-lived thread of its
own, which has the process's rights (the job thread keeps its impersonation). It writes only into a `log\` that
is a real folder at exactly that path (`GetFinalPathNameByHandleW`), owned by SYSTEM or the Administrators, that
no one else may change (so a folder a user made before `install.ps1` ran, e.g. after a driver-only install, is
not used); it keeps the folder open (no delete sharing) and opens the files relative to that handle
(`NtCreateFile` with a root directory, `FILE_OPEN_REPARSE_POINT`), only plain files with one link; it rotates
`plugin.log` through that handle. Two folders stay writable for users, and the plug-in never writes into them:
`status\` (the programs' printer-status log and hang log) and `capture\` (test printers only; the spooler opens a file
port as the user who printed, not as SYSTEM). There users may create files and change or delete any file, but
not create folders (since the audit of 2026-10-24), so nothing in them can be swapped for a junction; before the
uninstall deletes them it takes the users' rights away. `install.ps1` removes links it finds in the data folder
instead of following them.

## 8. Lessons from Unidrv

- `*DevBPP` / `*DevNumOfPlanes` must be 0 when the plug-in writes the output itself (else Unidrv crashes).
- A one-line `*Command: CmdSelect { … }` is silently ignored: always write it on several lines.
- GPD end-page / end-document callbacks never arrive: hook the DDI functions.
- Landscape surfaces arrive unrotated.
- Unidrv replaces a custom size by a GPD paper of the same size, also a die-cut one; the plug-in reverts that.
- The INF must use `CoreDriverSections`, not `Include`/`Needs`, for Unidrv; `AddPrinterDriverEx` without the
  driver store fails (3019), so the install goes through `pnputil`.
