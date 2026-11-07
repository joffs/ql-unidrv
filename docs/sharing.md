# Printing from another PC through a shared printer

Works for every supported model. Below, `QL-xxx` stands for your model (e.g. `QL-800`; also used as the share
name, any name works) and `host` for the PC the printer is plugged into.

## How it works

The QL printer is plugged into the **host** (USB) and shared there. The data a client sends must already be the
printer's raster stream, so the share's queue passes it on unchanged (winprint, RAW). A client therefore renders
with a QL driver of its own and prints to a **Local Port named after the share** (`\\host\QL-xxx`):

```
Client: application ──► QL printer with a QL driver (this one or Brother's x64 driver)
                          Local Port \\host\QL-xxx
                               │  SMB (port 445), RAW data
Host:                          ▼
                          "Brother QL-xxx (Unidrv)", shared as QL-xxx   winprint RAW: passed on unchanged
                               │  USB
                               ▼
                             printer
```

"Add printer → shared printer" (Point and Print) would download the host's driver instead; that works only if the
host offers a driver for the client's architecture, which this package does not set up. Use the Local Port.

## Host (the PC the printer is plugged into)

With the driver installed and the printer on USB (elevated PowerShell):

```powershell
Set-Printer -Name "Brother QL-xxx (Unidrv)" -Shared $true -ShareName "QL-xxx"
Enable-NetFirewallRule -DisplayGroup "File and Printer Sharing"   # German Windows: "Datei- und Druckerfreigabe"
```

- The queue must print directly in RAW (`winprint`, the default), so jobs reach USB byte for byte.
- The host must be running; nobody needs to be signed in. A fixed address (or a name the clients can resolve)
  avoids broken ports later.
- Clients sign in with an account of the host. A VPN client on the host may block LAN access.
- The firewall rules apply to the network profile in use; on a trusted LAN, set the connection to "Private".

## Client

First make sure the share is reachable: open `\\host` in Explorer, sign in as an account of the host
(`host\user`) and tick **Remember my credentials**. Saved credentials belong to one Windows user: do this for
**every user** of the client who prints to the share (or give them an account on the host with the same name and
password). A user without access gets "No printer status from HOST ... user name or password is incorrect" in the
status window (whether that user's jobs still reach the host is not tested yet).

- **Client with this driver (x64 or ARM64):** install the driver (release zip: `Setup.exe`), then add the
  printer in the *Printer setup* window of QL Label Formats, *Add a QL printer shared by another PC* ([setup.md](setup.md));
  by script, in an elevated PowerShell (from the repository: `driver\install.ps1`)

  ```powershell
  powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" -AppOnly -SharePort \\host\QL-xxx -DriverName "Brother QL-xxx (Unidrv)"
  ```

  adds a printer with that driver on that port. `-AppOnly` leaves the
  installed driver alone, so on ARM64 no special start is needed; without it, the driver package is installed
  too. A printer of the same name on another port (e.g. the client's own USB printer) is kept, and the share's
  printer is called "Brother QL-xxx (Unidrv) on host".
- **Client with Brother's own x64 driver:** if a Brother printer for this model already exists there, add a Local
  Port named `\\host\QL-xxx` in its *Printer properties > Ports* and select it; its paper settings stay. Without
  such a printer, install this package on the client and add the share as above.

**Moving an existing printer to the share** (e.g. a USB printer whose printer went to another PC; elevated:
`Set-Printer -Name "<printer>" -PortName \\host\QL-xxx`): Windows may have set it to *Use Printer Offline* (German:
*Drucker offline verwenden*) while its USB printer was missing, and that stays after the port change. Jobs then
wait in the client's queue without an error. The programs show it (QL Printer Status, the monitor's
notification, the tip bar of QL Label Formats and its Test print) with a **Use printer online** link (German: *Drucker
online verwenden*), which clears it. Or clear it in the print queue window, menu *Printer* (German: *Drucker*), or
elevated:
`Get-CimInstance Win32_Printer -Filter "Name='<printer>'" | Set-CimInstance -Property @{WorkOffline=$false}`.

Optional check of the path without any driver: send a recorded job for your model as raw data, the one that
matches the loaded roll (a mismatched roll puts the printer into its error state):

```powershell
cmd /c copy /b reference\captures\<model>\62mm-continuous_default-length.prn \\host\QL-xxx
```

## Limits

- **Printer status and notifications on the clients need the optional service "QL printer status" on the
  host** ([status-service.md](status-service.md)): it publishes the printer's status in the shared queue, and the
  clients' status window and printer monitor show it ("Status from HOST"), with the jobs waiting on the host.
  Without it, only the host shows the status. The **two-colour roll check** works only on the host (the driver
  asks the printer over USB). Stuck jobs from a client are in the host's queue (*Print queue* in the status
  window; cancelling another user's job needs administrator rights).
- **Label formats** are per PC: each client has its own list (QL Label Formats, or Brother's tool with Brother's
  driver).
- **On the host, share a queue with this driver**, not one with Brother's driver: for several models (e.g. the
  QL-570) Brother's queue has a language monitor that expects Brother's own page headers and would change a
  finished stream. Don't share a "Generic / Text Only" queue
  either: it prints, but the service publishes the status only on queues with this driver, so the clients would
  show "No printer status from HOST".
