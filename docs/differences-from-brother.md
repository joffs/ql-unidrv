# Differences from Brother's drivers: missing features and untested areas

This page lists what this Unidrv driver does differently from Brother's original drivers, what it leaves out,
and what has only been checked against Brother's recorded output (captures) rather than on a real printer.

How the driver is checked: Brother's drivers are installed on an x64 PC with file ports, and their output is
recorded (`reference/captures/`). Our encoder must reproduce every capture byte for byte (`replay_test`), and the
installed driver is compared with the same cases (`compare.ps1`). Real printers available for tests: QL-800 and
QL-570; a QL-720NW over the network (2026-11-05).

## Tested on a real printer

| Model | What |
|---|---|
| QL-570 | test labels over USB, printer status |
| QL-720NW | over the network: printer status (SNMP) and the test kit's 62 mm label (network form, port 9100); USB not tested |
| QL-800 | continuous and die-cut labels, custom lengths, label formats, 300 × 600, two-colour (black/red DK-22251), multi-up "62mm x 2", printer status |

Every other model (QL-500, QL-550, QL-560, QL-580N, QL-600, QL-650TD, QL-700, QL-710W, QL-810W, QL-820NWB,
QL-1050, QL-1060N, QL-1100, QL-1110NWB, QL-1115NWB) and the QL-720NW on USB are checked against Brother's captures only. Owners can close this gap with the
[test kit](../tools/testkit/README.md): it prints our driver's USB stream for their model directly on the printer
(no install) and reports the printer's status.

## Untested: no capture and no printer

These were neither recorded from Brother's driver nor printed on a real printer. The driver's behaviour follows
what Brother's drivers do in the recorded cases, not a recording of that case.

- **Multi-up wide papers (QL-1050 / QL-1060N / QL-1100 series):** 102 mm and 103 mm × 2 / 3 / 4 (Brother paper IDs
  280, 297, 286, 301, 292, 305). Brother's dialog offers them; the x64 capture runs did not cover them. Our driver sends each strip
  as its own label, as it does for every multi-up paper (confirmed for "62mm x 2" on the QL-800). Unknown: whether
  Brother's driver lays out the strips of these wide papers the same way, and how they print.
- **USB form of the network-capable models** (QL-580N, QL-710W, QL-720NW, QL-810W, QL-820NWB, QL-1050, QL-1060N,
  QL-1100 series; the QL-1100 is USB-only but its driver compresses on a file port like the others): on USB
  the driver sends uncompressed lines (`M 00`), as Brother's code does; only the compressed form (file and network
  ports) was recorded. The compressed network form of the QL-810W driver printed correctly on a QL-800 (same command
  set) through a network-printer emulator, and a real QL-720NW printed its network form correctly (test kit,
  2026-11-05); on the other network models it is untested.
- **QL-1060N on a network port:** Brother's driver writes the finished stream itself there (350 zero bytes first),
  and our driver does the same on every port. Not printed.
- **Media block `ESC i U w`:** for some papers Brother's driver sends a 127-byte media block before each page:
  QL-1115NWB direct-thermal papers (DT 90 mm, DT 102 mm, DT 102 × 51, DT 102 × 152), QL-700 29 × 42 mm,
  QL-700/710W/720NW 60 × 86 mm, QL-800/820NWB 62 × 60 and 62 × 75 mm. Our driver sends the recorded block of that
  model and paper unchanged. Whether the printer needs it, and what its undecoded fields mean, is unknown.
  **Driver 0.18 and older** send it only for the direct-thermal papers. The die-cut papers above were first
  recorded for the test kit (2026-11-04), and driver 0.19 sends their blocks too.
- **QL-600:** Brother's driver ends every job with `ESC i a FF`; our driver does the same. Its effect is unknown.
- **QL-1100c / QL-1110NWBc** (newer hardware revisions): same Brother driver; whether they report the same USB
  hardware ID is not known, so they may not be detected automatically.
- **QL-700:** the per-line density byte and `ESC i J 01` match every capture, but their effect on a printer is
  unknown.
- **QL-650TD compressed form** (Brother uses it on COM/LPT ports only): not recorded; our driver never compresses for
  this model.
- **12 mm round labels (DK-11219, US: DK-1219):** identical to Brother's captures on every model; printed only on the QL-800 (correct,
  inside the circle). The other models use the same bytes except the QL-650TD and QL-1050/1060N (`ESC i d 00 00`
  there, as Brother's drivers send).
- **Status replies** from every model except the QL-800 and QL-570: the model codes come from Brother's files, not
  from a printer.
- **Status of network printers** (a queue on a Standard TCP/IP port): the label tool reads it with SNMP, the value
  Brother's distributed-printing tool reads. Never
  tried on a real printer: whether the answer equals the USB reply byte for byte, and whether the QL-580N/1060N
  answer at all (their Brother packages have no SNMP code). The test kit's `-Network` mode records it.
- **The status service through a printer share** ([status-service.md](status-service.md)): tested only between the
  project's own two PCs (QL-800 and QL-570 shared by an ARM64 PC, read on an x64 PC).

## Deliberately different from Brother

- **Feed on the QL-1050 / QL-1060N:** 3 mm (35 dots), as on every other model. A freshly installed Brother driver
  for these two models starts with 6.1 mm (taken from its default paper 102 × 152 mm) and keeps it when another
  tape is chosen.
- **Brother's alternative paper tables** (`_m` tables of the QL-710W/720NW/810W, 24-dot feed): not used. Brother
  uses them only when its own tools set a registry value.
- **Brother's "crop print" (QL-1100 series):** the 215.9 × 297 mm page on which Brother's driver finds barcodes and
  prints each as its own label is not offered.
- **"Shipping label" papers (QL-1100/1110NWB, 103 × 164 mm and 103 mm):** not offered separately: they send the
  same commands as the normal 103 × 164 mm and 103 mm papers, which are offered. Brother's versions give the
  application the whole label as its page and then cut off its bottom.
- **Job id block** (`ESC i U J`): left out. Brother's driver sends it; the printers print the same without it.
- **Custom continuous lengths:** the drawing can sit 1–2 dots higher or lower than Brother's, because Windows'
  custom page is 2 lines shorter than Brother's. Commands and label length are the same.
- **Continuous papers chosen directly** (e.g. "62mm", "DT 90mm") have a 150 mm page on every model. Brother's
  drivers use the model's default length instead (89.8 mm; QL-1050/1060N 152.8 mm, QL-1100/1110NWB 164.3 mm,
  QL-1115NWB 100 mm). The paper names are shared by all models, so one length is used. Other lengths: a label format
  or a custom size.
- **QL-1100 series:** 29 × 90 mm landscape and 102 mm continuous sit 1–2 dots off Brother's,
  whose QL-1100 driver draws them differently from its QL-1060N driver. Commands and label size are the same.
- **102 × 51 mm die-cut label (QL-1050/1060N, QL-1100 series):** the drawing sits 1 dot further right than Brother's. Commands and
  label size are the same.
- **A custom 62 × 100 mm size** is matched by Windows to the 62 × 100 mm die-cut paper; the driver keeps it
  continuous, but the drawing ends 3 lines earlier than Brother's (same on every model).
- **Printable width of custom sizes and continuous label formats on tapes narrower than the model's widest** (e.g.
  29 mm on the QL-800, 62 mm on the QL-1100): the page's printable area is the whole tape width (29 mm:
  342 dots instead of Brother's 306), with no side margins reported. Windows can only centre a custom page on the
  paper this way; the driver crops the tape's side margins (1.5 mm each, 2 mm on 54 mm, 2.5 mm on 103 mm), so the
  edges of a drawing that fills the whole page are lost, but content lands where the program put it. On the widest
  tape (62 mm on the QL-500 to QL-820NWB, 102 mm on the wide models) the area and the margins are Brother's.
- **Label formats:** stored as Windows forms plus our own list (`labels.tsv`), edited with our label tool instead of
  Brother's label format tool. The feed of a label format is used for continuous tape.
- **Languages:** our label tool, status window and monitor are in English and German only (Brother's tools come in
  many languages). The printing preferences dialog is Windows' own (Unidrv); the driver's own options there (cutting,
  print priority, ...) have English names.
- **QL-500:** the multi-up papers are offered although Brother's QL-500 dialog hides them.
- **QL-560 factory list:** Brother's 3.05 mm feed for some formats is not copied.

## Windows limitations

- **Paper number 256:** Windows numbers paper sizes by their position in the machine-wide forms list, and 256 also
  means "custom size". On a PC with many paper sizes (e.g. Brother's drivers and ours installed) the paper at
  position 256 can't be selected and custom sizes print on the wrong tape. The label tool keeps that position for a
  placeholder ("Reserved: paper number 256") and moves its own label formats if needed; their numbers change
  then (programs that store the number instead of the name may show another paper size). Only if it has no label
  format to move does a paper stay at 256: the install then shows a warning.

## Missing (Brother has it, our driver does not)

- **P-touch Editor:** Brother's label design program is not part of the driver package and was not reproduced.
  Our *QL Label Maker* ([label-editor.md](label-editor.md)) covers text (single lines and text frames that wrap), lines, boxes,
  pictures, barcodes (Code 128, EAN-13, QR code) and date/time fields in black, on any length of continuous tape
  (typed, dragged or following the content); P-touch Editor's other barcode
  types, serial numbers, database (CSV/Excel) printing, clip art, templates and red on two-colour tape are not
  there. Any program can still print on the label formats like on paper.

- Brother's status monitor and distributed printing (`*M.EXE`, `PTMDA*.DLL`); our label tool has its own printer
  status window and tray monitor, also for network printers and printers on a share (Brother's monitor shows
  nothing for a printer on a TCP/IP port: its language monitor refuses such ports).
- Brother's `0x1DB` / `0x1EC` DEVMODE switches (no dialog control in Brother's driver either).
