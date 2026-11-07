# Building, testing and signing the driver yourself

You don't need to be a developer for this. You install three free programs, download this project, double-click
`build.cmd` and answer two questions. The first time takes about an hour, most of it downloading Visual Studio;
later builds take a few minutes. No Windows Driver Kit is needed: the driver is a Unidrv (v3) driver that uses
Windows' own `unidrv.dll`; only a small plug-in is compiled.

## 1. Which PC do you build for?

Look it up on the PC the printer will be used with: *Settings → System → About → System type*.

- **"x64-based processor"**: most Windows PCs with an Intel or AMD processor. Build **x64**.
- **"ARM-based processor"**: e.g. PCs with a Snapdragon processor, or Windows in a VM on an Apple silicon Mac.
  Build **ARM64**.

Usually you build on that same PC and pick *"For this PC only"*. You can also build on one PC for another one,
or make one package for both kinds of PC.

## 2. Install the tools (once)

| What | Where to get it | What to choose |
|---|---|---|
| **Visual Studio 2022 Community** (free) | [visualstudio.microsoft.com/vs/community](https://visualstudio.microsoft.com/vs/community/), or the Visual Studio 2022 installer directly: [aka.ms/vs/17/release/vs_community.exe](https://aka.ms/vs/17/release/vs_community.exe) | see below. Tested with 2022 (17.14); Visual Studio 2026 is not tested yet |
| **Python 3** (free) | [python.org/downloads/windows](https://www.python.org/downloads/windows/) | the latest "Windows installer" for your PC (64-bit or ARM64). In the first installer screen tick **"Add python.exe to PATH"** |
| PowerShell 5.1, .NET Framework 4.8 C# compiler | already part of Windows | nothing to do |

**Visual Studio: what to tick.** Start the installer and choose *Install* (or *Modify* if Visual Studio is
already installed):

1. Tab **Workloads**: tick **"Desktop development with C++"** ("Desktopentwicklung mit C++"). This includes the
   Windows SDK (10.0.22621 or later), which the compiler needs. Signing needs no SDK: `package.ps1` uses
   Windows' own functions.
2. Only if you want to build for **ARM64 on an x64 PC**, or for **both**: tab **Individual components**, type `ARM64`
   in the search box and tick **"MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools (Latest)"** (German:
   "MSVC v143 – VS 2022 C++-ARM64/ARM64EC-Buildtools (neueste Version)"). Not the entries with
   "Spectre-mitigated libs", ATL or MFC, and not the ones with a version number instead of "(Latest)".
3. Only on an **ARM64 PC** that builds for x64 too: check in the same way that
   **"MSVC v143 - VS 2022 C++ x64/x86 build tools (Latest)"** is ticked (the workload normally includes it).
4. Click *Install* / *Modify* and wait until it has finished.

If something is missing later, the build stops and says what to install.

## 3. Get the project

On the project's GitHub page click **Code → Download ZIP**. Before unzipping, right-click the zip →
*Properties* → tick **Unblock** → *OK* (otherwise Windows may block the scripts). Unzip it to a short path, for
example `C:\ql-unidrv`. (With git: `git clone https://github.com/joffs/ql-unidrv`.)

## 4. Build and package

Double-click **`build.cmd`** in that folder. It asks:

```
  1  For this PC only (x64)                       - recommended
  2  For both x64 and ARM64 PCs (one package for both)
  3  Only for ARM64 PCs (another PC)
```

It then builds the driver and the programs and runs all tests (a few minutes; the window shows the progress).
At the end it offers to **create the signed driver package**: answer `Y`. The package is in `out\package`.
Then install it: see *Installing* in the [README](README.md).

The first package run creates **your own** signing certificate (a root CA and a code-signing certificate in your
user certificate store, `Cert:\CurrentUser\My`). Both private keys are non-exportable and stay there for your next
builds; while they exist, anything running in your user account can sign code your PC trusts, so build on a PC you
use for that. Never publish them; everyone who passes a package on signs it with their own certificate. (Release
installs don't keep them: see below.)

### The same from a command prompt

```
build.cmd [this | x64 | arm64 | both]
powershell -ExecutionPolicy Bypass -File driver\package.ps1 [-Arch x64|arm64|both]
```

`build.cmd` without an argument builds for this PC (started by double-click it asks instead). `package.ps1`
packages what the last build built (`out\driver\architectures.txt`), or the architectures given with `-Arch`. A
package for one architecture lists only that one in its INF (`NTamd64` or `NTarm64`).

## What the build does

`build.cmd` runs `driver\build.cmd` and `labeltool\build.cmd`:

1. `gen\media.py` turns Brother's paper tables (`docs\data\*.json`) and the model list (`MODELS` in that file) into
   `driver\src\media_table.inc`, one GPD per model in `driver\gpd\` and `labeltool\src\MediaTable.g.cs`.
2. Unit tests: `encoder_test` (raster encoder), `labels_test` (label formats, page geometry).
3. **`replay_test`** decodes every recording of Brother's drivers in `reference\captures\` (all 18 models plus
   the test kit's pages), re-encodes it with our encoder and requires **byte-identical** output. Any change to the
   encoder that breaks this fails the build.
4. The plug-in (`out\x64`, `out\arm64`), the GPDs, INIs and INF (`out\driver`), the three programs
   (`out\tools\QlLabelFormats.exe`, `QlLabelMaker.exe`, `QlPrinterStatus.exe`) and their tests.

The tests always run on the PC's own architecture; the other architecture's plug-in comes from a cross compiler
(x64 PC: the ARM64 build tools; ARM64 PC: the x64 compiler, which runs under emulation). `package.ps1` stages
everything into `out\package` (plug-ins in `arm64\` and `amd64\`) and signs the plug-ins and the catalog;
`install.ps1` imports your public certificates into the machine's trusted stores.

## Versions

The driver and the programs have separate versions:

- **Programs** (the three programs and `Setup.exe`): `labeltool\version\VERSION`, `major.minor.patch`. A release
  with only program changes raises the patch (0.18.1, 0.18.2), a release with a driver change or a milestone the minor
  (0.19.0; 0.20.0 = the first public release).
- **Driver** (INF, plug-in, GPDs): the release in which it last changed, shown as "0.18". `driver\VERSION` records
  it with its `DriverVer` and a hash of the driver sources (`driver\build.cmd`, `gen`, `gpd`, `inf`, `src`);
  `driver\gen\version.py` writes it, and `build.cmd` stamps the INF and GPDs from it. While the sources differ from
  the recorded hash, a build is a development build (the build time as stamp, and a note).
- **A release with a driver change:** set `labeltool\version\VERSION` to x.y.0, run `python driver\gen\version.py set`
  (it refuses unless the patch is 0), commit `driver\VERSION` with the change, build again.

## Making a release zip

```
build.cmd both
powershell -ExecutionPolicy Bypass -File tools\make-release.ps1 [-Arch both|x64|arm64]
```

writes `out\release\ql-unidrv-<programs' version>-x64-arm64.zip` (e.g. `ql-unidrv-0.18.1-x64-arm64.zip`; its
README names both versions). It refuses while the driver sources differ from `driver\VERSION` or the built
`DriverVer` isn't the recorded one. Its top level holds only `Setup.exe` (the setup window, with
its `.config`), `README.txt` (from `tools\release\`), `LICENSE` and `THIRD-PARTY-NOTICES.txt`; everything else is in
`advanced\`, in the repository's layout one level down: the **unsigned** package (`advanced\out\package`,
`package.ps1 -Stage`), the three programs (`advanced\out\tools`), `advanced\labels`, and the install scripts
(`advanced\driver\`). Users need no Visual Studio: `Setup.exe` (or, for
administrators, `advanced\driver\setup.ps1`) signs the package on their PC
with their own certificate (`package.ps1 -SignOnly -OneTimeKeys`, nothing to install: a new root CA and signer per install, whose private keys are deleted right after signing), then runs
`install.ps1` elevated. So a release never ships a certificate anyone has to trust, and leaves no key behind. The programs in a release are
built with `QL_RELEASE=1`, which never embeds Brother's factory label list (`make-release.ps1` checks it).

## Install and verify

- `driver\install.ps1 [-Update] [-TestPrinter]` (elevated) – see the README for the ARM64 boot option. `-TestPrinter`
  adds "QL-800 Capture", a printer on a file port, for the comparison below.
- `driver\compare.ps1` prints the test page for every recorded case through the *installed* driver and compares
  the output with Brother's recordings (`tools\compare_prn.py`); other models: `-Printer "QL-570 Capture"
  -Captures ql570` with a capture printer for that model.
- `driver\check-labels.ps1` checks every label format end to end; `driver\check-signing.ps1` (elevated) checks
  certificates, catalog and driver state after a restart.
- Plug-in log: `C:\ProgramData\QLUnidrv\log\plugin.log`, written only while the file exists (`install.ps1 -Logging on`,
  also with `-AppOnly`; off after a fresh install; at 1 MB it moves to `plugin.old.log`); `check-labels.ps1` uses it.
  Network printers without one: `tools\qlnetemu.ps1` emulates a QL network printer (SNMP status, print port 9100)
  backed by a QL printer on USB (`-Setup` adds a printer on it; see the script's help).
  Printer messages: the programs' in `C:\ProgramData\QLUnidrv\status\printer-status.log`, the driver's (roll
  check) and the status service's in `C:\ProgramData\QLUnidrv\log\printer-status.log` (both capped). The `log` folder
  is writable only for administrators and SYSTEM, and the plug-in and the service refuse to write through links
  (see `docs/architecture.md` §7).

## Layout

| Folder | Content |
|---|---|
| `driver\` | `src\` plug-in and encoder, `gpd\`, `inf\`, `gen\media.py`, `test\`, and the scripts |
| `labeltool\` | one source folder, three programs: QL Label Formats and the setup window (`QlLabelFormats.exe`; `Setup.exe` in a release), QL Label Maker (`QlLabelMaker.exe`), QL Printer Status and the printer monitor (`QlPrinterStatus.exe`) |
| `labels\` | `default-labels.json` (German), `default-labels-en.json` (UK, mm) and `default-labels-en-us.json` (US, inches): the label formats a new installation starts with (Brother's factory list; German display language: German, else US units: US, else UK) |
| `docs\` | documentation, paper tables (`data\`) |
| `reference\captures\` | recordings of Brother's drivers (the test oracle) |
| `tools\` | analysis tools (`prn_dump.py`, `compare_prn.py`, ...), the test page program `capture\qlcap.cs`, the test kit |
| `out\` | build output (not in git) |
