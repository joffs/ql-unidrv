<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="labeltool/assets/logo-dark.png">
    <img alt="QL Unidrv" src="labeltool/assets/logo-light.png" height="144">
  </picture>
</p>

# QL Unidrv driver – Brother QL label printers on Windows ARM64 and x64

An independent, open-source printer driver for Brother QL label printers on Windows 11 **ARM64** and **x64**.
Brother only ships x86/x64 drivers, which Windows on ARM64 cannot load. This driver uses Windows' own universal
printer driver (Unidrv) with one printer description (GPD) per model and a small render plug-in that writes
Brother's QL raster commands. **Not made, endorsed or supported by Brother.** Brother and QL are trademarks of
Brother Industries, Ltd.

> [!IMPORTANT]
> **Testers wanted.** Only the QL-570 and QL-800 have been tested on real printers, and the QL-720NW over the
> network. Do you own a QL-500, 550, 560, 580N, 600, 650TD, 700, 710W, 810W, 820NWB, 1050, 1060N, 1100, 1110NWB
> or 1115NWB, or a QL-720NW on USB? The
> [**test kit**](testkit/README.md) takes **10 minutes and a few centimetres of labels**: no installation, no
> administrator rights, your Brother driver stays. Even one printed label is already a useful report.

## Why this project exists

**Brother offers no Windows ARM64 drivers for its QL label printers** (as of November 2026). On an ARM64 PC – a
Snapdragon laptop, or Windows in a VM on an Apple Silicon Mac – a QL printer simply can't be used: on such a PC,
Windows' print spooler is a native ARM64 process and can't load Brother's x64 driver files.

The project started for exactly that: one QL-800 on a Windows ARM64 machine. Because the driver is built on
Windows' own Unidrv and Brother's printers share one command language, it grew from there – first more models,
then an x64 build of the same driver, so one package serves both architectures (and x64 PCs can print to a QL
printer shared by an ARM64 PC). **ARM64 support is still the main reason it exists**; on x64 you can use either
Brother's driver or this one.

## Status per model

| Model | Real printer | Checked against Brother's driver | Test kit |
|---|---|---|---|
| QL-500, QL-550, QL-560, QL-650TD | **needs a tester** | ✓ byte-identical | [download](testkit/README.md) |
| QL-570 | ✓ tested | ✓ byte-identical | [download](testkit/README.md) |
| QL-580N, QL-700, QL-710W | **needs a tester** | ✓ byte-identical | [download](testkit/README.md) |
| QL-720NW | ✓ tested over the network (printer status, test label); USB **needs a tester** | ✓ byte-identical | [download](testkit/README.md) |
| QL-600 | **needs a tester** | ✓ byte-identical | [download](testkit/README.md) |
| QL-800 | ✓ tested (all features, black/red, printer status) | ✓ byte-identical | [download](testkit/README.md) |
| QL-810W, QL-820NWB | **needs a tester** (incl. black/red) | ✓ byte-identical | [download](testkit/README.md) |
| QL-1050, QL-1060N | **needs a tester** (wide rolls) | ✓ byte-identical | [download](testkit/README.md) |
| QL-1100, QL-1110NWB, QL-1115NWB | **needs a tester** (wide rolls; QL-1115NWB: direct-thermal rolls) | ✓ byte-identical | [download](testkit/README.md) |

"Checked against Brother's driver": for every model, recordings of Brother's own driver are reproduced byte for
byte by this driver's encoder, and the installed driver was compared with them page by page. What differs from
Brother's drivers, what is missing and what is untested: [docs/differences-from-brother.md](docs/differences-from-brother.md).
Per model: [docs/models.md](docs/models.md).

## What you get

- **The printer driver** for all models: every Brother paper size of the model, custom label lengths, cut options
  (auto cut, cut every N labels, cut at end), 300 × 600 dpi, print priority, mirror, rotate 180°, trim trailing
  blank, two-colour on the models that have it.
- **QL Label Formats**, a replacement for Brother's label format tool: named label formats (paper + length) that
  every program offers as paper sizes, a default format per user, a test print, and a check that a format fits the
  roll in the printer. In English or German: it follows the Windows display language (German for de-DE/AT/CH),
  and the menu *Tools > Language / Sprache* (*English* / *Deutsch*) changes it.
- **QL Label Maker:** design and print labels with text, lines, boxes, pictures (dithered or black and white),
  barcodes and QR codes, along the label or across the tape; the screen shows the label exactly as it prints, dot
  for dot ([docs/label-editor.md](docs/label-editor.md)). Tip: print labels with it. Before printing it asks the
  printer for the loaded roll and says so before it sends a label that doesn't fit, so the printer doesn't stop in
  its error state (which only opening and closing the roll cover clears). That's handy when the printer is on the
  network or at another PC. Word and other programs print too, but without this check.
- **QL Printer Status:** one window that shows what each connected QL printer is doing, the loaded roll, and what to do about an error
  (Windows itself shows nothing, or only "Error"). Optional notifications next to the clock when a printer stops
  (wrong roll, cover open, end of roll), also while other programs print. A printer left on Windows' *Use Printer
  Offline* (jobs then wait without an error) is shown too, with *Use printer online* to clear it. With the optional service
  "QL printer status" on the PC the printer is plugged into, PCs that print through its share see the status too
  ([docs/status-service.md](docs/status-service.md)).
- **Light or dark:** all three programs follow Windows' colour mode; *Tools > Appearance* (*Extras > Darstellung*)
  picks *Light* or *Dark* for all of them.

## Installing

Two ways, both end with the same driver. Either way the driver is signed **on your PC with your own
certificate**: you never have to trust a certificate of ours (see *Why your own certificate?* below).

**A. Ready-made download (recommended).** From the project's *Releases* page, download
`ql-unidrv-<version>-x64-arm64.zip` (both kinds of PC; the version is the programs', the zip's `README.txt` also
names the driver's). Then:

1. Unzip the whole zip (right-click → *Extract All*).
2. Connect and switch on the printer (QL-700/800/810W/820NWB: the "Editor Lite" LED must be off).
3. Double-click **`Setup.exe`** and follow the window (English or German, as Windows). If Windows says
   *"Windows protected your PC"*: *More info* → *Run anyway*. The window checks the PC, signs and installs the
   driver and the programs (one administrator prompt), guides ARM64 PCs through the special start (below),
   offers printer notifications and prints a test label. The zip's `advanced\` folder holds what it
   uses (driver package, programs, scripts); you don't need to open it.

Afterwards the Start menu has **QL Label Formats**, **QL Label Maker** and **QL Printer Status**. Already
installed? `Setup.exe` updates or repairs from the zip; when the zip has the installed driver and only newer
programs, it updates just the programs (no signing, no restart). Day to day the **Printer setup** window (menu
*Printer > Printer setup* in *QL Label Formats* and *QL Label Maker*) adds a network or shared printer, shares a
printer with other PCs, removes printers, switches the printer status service and the driver log, and uninstalls; Windows' *Installed apps* lists "QL Unidrv printer driver"
(*Modify*, *Uninstall*). Details: [docs/setup.md](docs/setup.md).

**Printers on the network** (Ethernet or Wi-Fi: QL-580N, 710W, 720NW, 810W, 820NWB, 1060N, 1110NWB, 1115NWB):
the Printer setup window asks for the printer's **IP address** (on its network settings page or the configuration label it
prints), asks the printer which model it is, and sets up "Brother QL-xxx (Unidrv)" on a standard TCP/IP port.
Later or for another printer: the Printer setup window (no special start needed, also on ARM64); by script:
`<zip folder>\advanced\driver\install.ps1 -Settings -NetworkPrinter 192.0.2.50` as administrator (add
`-DriverName "Brother QL-810W (Unidrv)"` if the printer doesn't answer). Its status (roll, errors) then shows
in *QL Printer Status* too.

**B. Build it yourself** from this repository: [BUILDING.md](BUILDING.md) (step by step, no developer knowledge
needed; Visual Studio Community and Python), then `driver\install.ps1` in an elevated PowerShell (`-Update` to
keep existing printers).

**ARM64 PCs: one special start per driver install or update.** Windows on ARM64 refuses self-signed driver
packages while driver signature enforcement is on, so the install runs in one start with it switched off.
`Setup.exe` prepares it with one click (*Prepare and restart*) and opens again after the restart; by hand:

1. Open **Command Prompt as administrator** (Start → type `cmd` → *Run as administrator*) – **not
   PowerShell**: PowerShell reads `{current}` as a script block and `bcdedit` fails. Run:

   ```
   bcdedit /set {current} onetimeadvancedoptions on
   ```

   (In PowerShell it only works with quotes: `bcdedit /set "{current}" onetimeadvancedoptions on`.)
2. Restart. On the blue *Startup Settings* screen press **7** ("Disable driver signature enforcement"). This
   applies to this one start only.
3. In that start run `Setup.exe` or `advanced\driver\install.ps1`. When Windows says it can't verify the publisher,
   choose **"Install this driver software anyway"**.
4. Restart normally. The driver keeps working with Secure Boot and enforcement on.

Updates of the programs alone (`Setup.exe` with a zip whose driver is the installed one, or `install.ps1 -AppOnly`)
never need it. **x64 PCs** need no special start: they
accept the package once your certificate is trusted (`install.ps1` does that).

**Why your own certificate?** Windows only installs signed drivers. A certificate that a publisher asks you to
trust would let whoever holds its private key sign *any* software your PC then accepts. So nothing here ships a
certificate: each install creates one for your PC (a root and a signer, restricted to code signing), signs the
driver with it, trusts only that one, and **deletes its private keys right after signing**, so nothing on your PC
can sign anything with it later. An update brings a new one and replaces the old.

- **QL-700, QL-800, QL-810W, QL-820NWB:** the "Editor Lite" LED must be off (hold the Editor Lite button until it goes out), otherwise the printer shows up as a USB drive.
  Any model that shows up as a drive instead of a printer: switch off its Editor Lite / mass storage mode.
- Printing from another PC through a shared printer: [docs/sharing.md](docs/sharing.md).
- **Driver log for a bug report** (off by default): in *QL Label Formats* open *Printer > Printer setup*
  (German: *Drucker > Druckereinrichtung*) and tick **Driver log for troubleshooting** (*Treiberprotokoll zur
  Fehlersuche*; one administrator prompt). Print again, then attach `C:\ProgramData\QLUnidrv\log\plugin.log` and
  `printer-status.log` from the same folder (the printer's messages as the driver and the status service read
  them), and `C:\ProgramData\QLUnidrv\status\printer-status.log` (the printer's messages as the programs read them;
  a help, not a complete record: messages during a print may be missing). Untick it afterwards. No special start
  needed, also on ARM64. Alternative in an administrator PowerShell:
  `powershell -ExecutionPolicy Bypass -File "<zip folder>\advanced\driver\install.ps1" -AppOnly -Logging on` (`-Logging off` to stop).
  If *QL Printer Status* stops responding, attach `C:\ProgramData\QLUnidrv\status\hang.log` too (written by itself after
  15 s, no setting needed).

## Test kit for owners of other models

[testkit/](testkit/README.md): a ready-made zip per model with one PowerShell script – no installation, no
administrator rights. It reads your printer's status, prints a few test labels with exactly the data this driver
sends, and writes a report. Please send it as a
[Printer test report](https://github.com/joffs/ql-unidrv/issues/new?template=printer-test-report.yml) issue, or by
mail to ql-unidrv@joffs.de; the status table above is updated from your reports.

## Documentation

- [docs/README.md](docs/README.md) – index: models, architecture, setup, sharing, the printer status service, QL Label
  Maker, and the differences from Brother's drivers.
- [reference/captures/](reference/captures/README.md) – recordings of Brother's drivers printing our test page,
  per case and model: the test oracle for the encoder (`replay_test`) and the installed driver (`compare.ps1`).
- [BUILDING.md](BUILDING.md) – toolchain, build, tests, signing your own package.

## Contact

Questions, problems and test reports: [GitHub issues](https://github.com/joffs/ql-unidrv/issues), or
ql-unidrv@joffs.de.

## Licence

MIT – see [LICENSE](LICENSE). QL Label Maker's QR code encoder is ported from Project Nayuki's MIT-licensed QR Code
generator library; its notice is in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). The paper tables (`docs/data/`) and the recordings describe Brother's printers
for interoperability; Brother's own driver files are not part of this repository.
