# QL Unidrv driver – technical documentation

An independent printer driver for 18 Brother QL label printers (QL-500 to QL-1115NWB) on Windows 11 **ARM64**
and **x64**: Windows' universal printer driver (Unidrv) with one GPD per model, a small render plug-in that
writes Brother's QL raster protocol, and three programs (QL Label Formats, QL Label Maker, QL Printer Status). Not made or supported by Brother. Recordings of the output of Brother's drivers serve as the test
oracle.

Installation and quick start: [../README.md](../README.md).

## Driver

| Document | Content |
|---|---|
| [models.md](models.md) | The 18 supported models: driver names, USB IDs, options, paper counts, status codes, test status |
| [architecture.md](architecture.md) | How the driver works: GPD + plug-in, callbacks, print path, label formats, label tool, build/install/verify, signing, files on disk |
| [differences-from-brother.md](differences-from-brother.md) | What differs from Brother's drivers, what is missing, what is untested |
| [setup.md](setup.md) | Install, update, change, remove: `Setup.exe` (setup window; `advanced\driver\setup.ps1` = the console form for administrators), the Printer setup window, Installed apps |
| [sharing.md](sharing.md) | Printing from another PC through a shared printer |
| [status-service.md](status-service.md) | The optional service "QL printer status": printer status for PCs that print through a share |
| [label-editor.md](label-editor.md) | QL Label Maker: design and print labels (text, lines, boxes, pictures), `.qllabel` files |
| [print-command-line.md](print-command-line.md) | `QlLabelPrint.exe`: print a `.qllabel` with data from CSV, JSON or a script; options, exit codes, JSON answer |


## Verification data

| Where | Content |
|---|---|
| [../reference/captures/](../reference/captures/README.md) | Brother's driver output per case and model (`.prn`, DEVMODE, PNG preview). The PNG previews show the raster in print-head order, i.e. mirrored left-right compared with the printed label (300 × 600: twice as tall); the test kit's pictures are corrected |
| `../tools/` | `prn_dump.py` (decode/render a stream), `compare_prn.py` / `compare_models.py`, `capture/qlcap.cs` (the test page of `compare.ps1` and `check-labels.ps1`) |
| [../tools/testkit/](../tools/testkit/README.md) | Test kit for owners of models without a real-printer test: status read and our driver's test labels sent directly over USB, report file |
