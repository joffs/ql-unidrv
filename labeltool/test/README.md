# Label tool tests

`labeltool\build.cmd` runs the unit tests (`status_test`, `format_test`, `lang_test`, `setup_test`, `editor_test`,
`barcode_test`) on every build. The scripts below open windows or print to a capture printer, so they run on their own:
always through `out\tools\uitest\deskrun.exe` (a desktop of its own; your mouse and focus stay untouched; build it
with `tools\uitest\build.cmd`).

| Command | What it checks |
|---|---|
| `powershell -File labeltool\test\ui_sweep.ps1` | UI sweep of QL Label Maker, below |
| `powershell -File labeltool\test\setup_states.ps1` | Setup.exe's finish views (ARM64 normal and key-7 start, app-only and driver update, after the restart, x64): restart note, key-7 step, buttons, the Uninstall link; nothing on the PC changes; pictures in `out\setup-states` |
| `powershell -File labeltool\test\setup_open.ps1` | Printer setup opened with `--select` on 18 fake printers: the printer chosen (buttons on), a gone share marked whether the check answers at once or late (net 0, queue not SHARED); nothing on the PC changes; pictures in `out\setup-open` |
| `powershell -File labeltool\test\status-window.ps1` | One status window per user session: F6 from two other programs brings one window to the front, F6 with no status program running starts QlPrinterStatus.exe, F6 puts the status window of another process above a maximised window; an event name of its own, nothing changes |
| `powershell -File labeltool\test\formats-sweep.ps1` | QL Label Formats and QL Printer Status in light, dark and High Contrast, German and English, with live switches through the other schemes and back: main window, Printer setup and its dialogs, status cards (error, offline, OK), About, tray menu; pictures and `report.tsv` (heights over 690 px, solid black/white blocks) in `out\formats-sweep` |
| `powershell -File labeltool\test\barcode_print.ps1` | Barcodes printed through the editor on "QL-800 Capture", decoded and measured; the barcode panel |
| `powershell -File labeltool\test\editor_spike.ps1` | The editor's print path dot for dot |
| `powershell -File labeltool\test\custom_length_spike.ps1` | Custom lengths through the driver |

Needs for the barcode checks: Python with `zxing-cpp` and `numpy` (`pip install --user zxing-cpp`), the printer
"QL-800 Capture" (`driver\install.ps1 -TestPrinter`; nothing comes out of a real printer).

## UI sweep (`ui_sweep.ps1`)

One command, about 10 minutes; report in `out\ui-sweep\report.txt`, pictures of every finding next to it; exit
code 1 when there are findings. `-Checks clip,drag,sizes,contents` and `-Lang de,en` pick parts.

- **clip** (German and English): every panel state of QL Label Maker (nothing selected, each object type, each QR
  type, two objects) and the windows QL Label Formats, Printer setup, Printer status and About, at 96, 120, 144
  and 168 dpi (a move to such a screen, emulated with WM_DPICHANGED); also the Printer status card of a printer on
  another PC's share in each state (shared, not shared with its advice and the remove link, not reachable, can't be
  checked, stale), and link texts cut inside their link (`QL_SWEEP_ONLY=status` sweeps only those cards). A finding when a text needs more room than
  its control has, when a container cuts what the text really draws (measured on the control drawn on its own;
  scrolling panels may cut in their scroll direction), or when a toolbar button is pushed into the overflow.
  Also **height**: a window taller than 690 px (logical) at any DPI.
- **drag**: every object type moved, resized and turned in steps. The canvas is painted as Windows would show it
  (only the regions the editor invalidates are painted again) and compared with a fresh `EditorRender.Render`
  while dragging and after releasing; a cluster of strongly different pixels is paint left behind (a trail). A
  picture is a draft while dragging (noted, not a finding). The check tests itself first; it found the line trail
  fixed in c15b7fb on the build before it.
- **sizes**: every barcode type (EAN-13, Code 128, GS1-128, QR) rendered at widths 8-60 mm and decoded with
  zxing-cpp. A finding when a size doesn't read but the editor shows no problem line, when it reads but the editor
  refuses it, or when the text under the bars is missing without a problem line saying so.
- **contents**: sample, help and test files must not carry real GTINs (a 13/14-digit number with a valid check
  digit must start with 0 or 2: all-zero samples, GTIN-14 indicator 0, restricted circulation 20-29).
