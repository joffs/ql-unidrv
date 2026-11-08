# The optional service "QL printer status"

The programs show each QL printer's state (loaded roll, errors, what to do) in the status window, the status bar of
QL Label Formats and the printer monitor in the notification area. A printer tells this only over **USB** and only to the PC it is
plugged into. The service lets other PCs see it too.

## Do I need it?

| Your setup | Service |
|---|---|
| One PC, its QL printer on USB | **Not needed.** The programs read the printer themselves. |
| A QL printer on USB, **shared** with other PCs (`docs/sharing.md`) | **Install it on the PC the printer is plugged into.** The other PCs then show the printer's status in their status window and monitor. |
| Several users signed in on the same PC | Optional: one reader for everybody instead of one per user. |

The other PCs don't need the service: they only read what the host publishes.

## What it does

- Windows service **"QL printer status"** (service name `QLStatus`), program
  `C:\Program Files\QLUnidrv\QlPrinterStatus.exe --service`, start type *Automatic (Delayed Start)*, runs as **Local System**.
- Every 3 seconds it reads every QL printer on this PC's USB ports that uses this driver, the same way the label
  tool does: it collects the messages the printer sent by itself (printing, finished, error) and, only while the
  printer's print queue is empty and the printer isn't printing, asks for its status (`ESC i S`). It never writes
  anything else to the printer.
- It publishes the result in the **print queue's own settings** (printer data, key `QLUnidrv`, value `Status`): the
  last poll and the printer's last 16 messages, only when something changed and otherwise once a minute. Every
  program allowed to print to the queue can read it, also another PC through the printer share (no new share, no
  new firewall rule: the same Windows sign-in as printing).
- While it runs, the programs (QL Label Formats, QL Label Maker, the status window) and the monitor of every user on this PC read the published
  status instead of opening the printer themselves, so one program reads each printer.
- The printer's messages it reads go to `C:\ProgramData\QLUnidrv\log\printer-status.log` (next to the driver's; users
  can only read that folder, and the service doesn't write through links), so a bug report has them. Problems (a
  printer that can't be read, ...) go to the Windows event log (*Application*, source `QLStatus`), each one at most
  once an hour.
- When it stops, it marks the status as "stopped", so other PCs say so instead of showing an old state.

Why Local System: storing data in a print queue needs the right to manage that printer, which normal users
don't have. The service does nothing else with these rights: it reads the USB printers, asks the network
printers with SNMP (the address and community set on their Standard TCP/IP port; only the printer's own answer
counts), and writes the queue's `QLUnidrv` data. It keeps only the privilege every program has (`sc privs`) and runs
with its own service SID.

What a PC reads from another PC's share is checked before it is shown: at most 16 KB, 16 messages and an error
text of 200 printable characters; a status dated in the future counts as outdated.

## The test kit

The test kit (`tools/testkit/qltest.ps1`) sends its test labels straight to the USB printer, past the print queue.
A read or status request of the service in the middle spoils them: the printer reports an error and takes no more
data (seen 2026-11-03). So the kit pauses the service's USB reading while it tests:

- It sends the service the custom control 128 (`(Get-Service QLStatus).ExecuteCommand(128)`); 129 resumes. Other
  codes do nothing. Windows' default service permissions let every signed-in user (`INTERACTIVE`) send custom
  controls (`sc sdshow QLStatus`: `CR` in the `IU` entry), so neither the kit nor the service needs administrator
  rights or changed permissions.
- The service waits until no USB read of its own runs any more, then stops reading USB printers for **10 minutes**
  and writes the pause's end into `C:\ProgramData\QLUnidrv\log\status-pause.txt` (users can only read that folder,
  so the kit can trust it). The kit starts only after that confirmation and renews the pause before each label.
- It resumes when the kit resumes it (also after Ctrl+C), else by itself after the 10 minutes (a closed window),
  and when it restarts. Network printers keep being read.
- What a user can do with it: pause the status of this PC's USB printers, 10 minutes at a time. Printing is not
  affected (jobs through the queue never needed the service), and the programs then call the status outdated
  ("No current printer status") after 3 minutes. That is no more than unplugging the printer's USB cable; the
  service accepts no data with the control, so there is nothing to misuse. A shared gate (a named mutex) was not
  used: in session 0 any user could create it first and lock the service out (security audit 2026-10-24, M2).

## Turn it on or off

- **Printer setup window:** *QL Label Formats* or *QL Label Maker* > *Printer* > *Printer setup* > tab *Share a
  printer* > **Printer status service for shared printers** (German: *Drucker* > *Druckereinrichtung* > *Drucker
  freigeben* > *Druckerstatus-Dienst für freigegebene Drucker*; one administrator prompt). `Setup.exe` offers it too when this PC shares a QL printer.
- **Script** (administrator PowerShell; from the repository: `driver\install.ps1`):

  ```
  powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" -AppOnly -StatusService on
  powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" -AppOnly -StatusService off
  ```

  `-AppOnly` touches only the programs and the service (no driver install, no special start on ARM64). Without
  `-StatusService` an install or update keeps the current setting; a fresh install has it off.
- **Uninstall** removes it together with everything else.
- Check it: *Services* (`services.msc`, German: *Dienste*) → "QL printer status", or
  `Get-Service QLStatus` in PowerShell.

## On the other PCs

A PC that prints through a share (a Local Port named `\\host\QL-xxx`, `docs/sharing.md`) shows, in the status window
and the monitor:

- the printer's state as on the host, with "Status from HOST (time)";
- the print jobs waiting on the host, from every PC;
- "No printer status from HOST" when the host can't be reached or doesn't run the service (it says how to turn it
  on), and "No current printer status from HOST" when the service there has stopped or its status is older than
  3 minutes.

Nothing has to be set up on these PCs beyond the shared printer itself.

## Network printers

QL printers on Ethernet or Wi-Fi (QL-580N, 710W, 720NW, 810W, 820NWB, 1060N, 1110NWB, 1115NWB), printed to through
a **Standard TCP/IP port**, don't need the service: the programs on every PC ask the printer themselves with SNMP
(UDP port 161, community from the port's settings, else `public`), the same value Brother's software reads; it
holds the same 32-byte status as over USB. Nothing is
sent to the printer's print port, so jobs of other PCs are never disturbed. SNMP must be enabled on the printer
(Brother's default). Where the service runs, it reads these printers too and publishes them like the USB ones
(useful when such a queue is shared). Confirmed on a real QL-720NW (2026-11-05): it answers with the same 32-byte
status as over USB, and QL Printer Status showed it; the test kit's `-Network` mode checks it on other models.
