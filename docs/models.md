# Supported models

The driver package supports 18 Brother QL label printers. Every model has its own driver name, GPD and
paper list; all share one render plug-in (`QLUnidrv.dll`) and one label tool. Models are listed by model
number throughout the documentation.

## 1. Overview

| Model | Driver name | USB hardware ID | Head | Resolutions | Captures |
|---|---|---|---|---|---|
| QL-500 | Brother QL-500 (Unidrv) | `USBPRINT\BrotherQL-5009026` | 720 dots | 300 × 300 | [ql500](../reference/captures/ql500/) |
| QL-550 | Brother QL-550 (Unidrv) | `USBPRINT\BrotherQL-550C025` | 720 | 300 × 300 | [ql550](../reference/captures/ql550/) |
| QL-560 | Brother QL-560 (Unidrv) | `USBPRINT\BrotherQL-5603025` | 720 | 300 × 300 | [ql560](../reference/captures/ql560/) |
| QL-570 | Brother QL-570 (Unidrv) | `USBPRINT\BrotherQL-570A024` | 720 | 300 × 300, 300 × 600 | [ql570](../reference/captures/ql570/) |
| QL-580N | Brother QL-580N (Unidrv) | `USBPRINT\BrotherQL-580N2C10` | 720 | 300 × 300, 300 × 600 | [ql580n](../reference/captures/ql580n/) |
| QL-600 | Brother QL-600 (Unidrv) | `USBPRINT\BrotherQL-60090D6` | 720 | 300 × 300, 300 × 600 | [ql600](../reference/captures/ql600/) |
| QL-650TD | Brother QL-650TD (Unidrv) | `USBPRINT\BrotherQL-650TD3360` | 720 | 300 × 300 | [ql650td](../reference/captures/ql650td/) |
| QL-700 | Brother QL-700 (Unidrv) | `USBPRINT\BrotherQL-7005085` | 720 | 300 × 300, 300 × 600 | [ql700](../reference/captures/ql700/) |
| QL-710W | Brother QL-710W (Unidrv) | `USBPRINT\BrotherQL-710W9D81` | 720 | 300 × 300, 300 × 600 | [ql710w](../reference/captures/ql710w/) |
| QL-720NW | Brother QL-720NW (Unidrv) | `USBPRINT\BrotherQL-720NW4A17` | 720 | 300 × 300, 300 × 600 | [ql720nw](../reference/captures/ql720nw/) |
| QL-800 | Brother QL-800 (Unidrv) | `USBPRINT\BrotherQL-80053B7` | 720 | 300 × 300, 300 × 600 | [ql800](../reference/captures/ql800/) |
| QL-810W | Brother QL-810W (Unidrv) | `USBPRINT\BrotherQL-810W4803` | 720 | 300 × 300, 300 × 600 | [ql810w](../reference/captures/ql810w/) |
| QL-820NWB | Brother QL-820NWB (Unidrv) | `USBPRINT\BrotherQL-820NWBC1AA` | 720 | 300 × 300, 300 × 600 | [ql820nwb](../reference/captures/ql820nwb/) |
| QL-1050 | Brother QL-1050 (Unidrv) | `USBPRINT\BrotherQL-1050AE13` | **1296 dots** | 300 × 300 | [ql1050](../reference/captures/ql1050/) |
| QL-1060N | Brother QL-1060N (Unidrv) | `USBPRINT\BrotherQL-1060NF99F` | **1296 dots** | 300 × 300 | [ql1060n](../reference/captures/ql1060n/) |
| QL-1100 | Brother QL-1100 (Unidrv) | `USBPRINT\BrotherQL-11003E41` | **1296 dots** | 300 × 300 | [ql1100](../reference/captures/ql1100/) |
| QL-1110NWB | Brother QL-1110NWB (Unidrv) | `USBPRINT\BrotherQL-1110NWBA2A3` | **1296 dots** | 300 × 300 | [ql1110nwb](../reference/captures/ql1110nwb/) |
| QL-1115NWB | Brother QL-1115NWB (Unidrv) | `USBPRINT\BrotherQL-1115NWB6EA3` | **1296 dots** | 300 × 300 | [ql1115nwb](../reference/captures/ql1115nwb/) |

Each model's captures are in `reference/captures/<model>/` ([index](../reference/captures/README.md)).

## 2. Options per model

What the driver's printing preferences offer. They follow what Brother's own driver dialog offers for the
model.

| Model | Auto cut | Cut every n | Cut at end | 300 × 600 | Black and red (DK-22251) | Papers (incl. multi-up) | Continuous length |
|---|---|---|---|---|---|---|---|
| QL-500 | – (manual cutter) | – | – | – | – | 36 | 25 mm – 1 m |
| QL-550 | ✓ | – | – | – | – | 36 | 25 mm – 1 m |
| QL-560 | ✓ | ✓ | ✓ | – | – | 36 | 25 mm – 1 m |
| QL-570 | ✓ | ✓ | ✓ | ✓ | – | 36 | 12.7 mm – 1 m |
| QL-580N | ✓ | ✓ | ✓ | ✓ | – | 36 | 12.7 mm – 1 m |
| QL-600 | ✓ | ✓ | ✓ | ✓ | – | 38 | 12.7 mm – 1 m |
| QL-650TD | ✓ | – | ✓ | – | – | 36 | 25 mm – 1 m |
| QL-700 | ✓ | ✓ | ✓ | ✓ | – | 38 | 12.7 mm – 1 m |
| QL-710W | ✓ | ✓ | ✓ | ✓ | – | 38 | 12.7 mm – 1 m |
| QL-720NW | ✓ | ✓ | ✓ | ✓ | – | 38 | 12.7 mm – 1 m |
| QL-800 | ✓ | ✓ | ✓ | ✓ | ✓ | 41 | 12.7 mm – 1 m |
| QL-810W | ✓ | ✓ | ✓ | ✓ | ✓ | 39 | 12.7 mm – 1 m |
| QL-820NWB | ✓ | ✓ | ✓ | ✓ | ✓ | 41 | 12.7 mm – 1 m |
| QL-1050 | ✓ | ✓ | ✓ | – | – | 47 | 25 mm – 3 m |
| QL-1060N | ✓ | ✓ | ✓ | – | – | 47 | 25 mm – 3 m |
| QL-1100 | ✓ | ✓ | ✓ | – | – | 49 | 25.4 mm – 3 m |
| QL-1110NWB | ✓ | ✓ | ✓ | – | – | 49 | 25.4 mm – 3 m |
| QL-1115NWB | ✓ | ✓ | ✓ | – | – | 48 | 25.4 mm – 3 m |

Every model also has: print priority (speed / quality), mirror printing, trim trailing blank ("cut tape after
data"), rotate 180°, orientation, copies, custom sizes and the user label formats of the label tool.

Papers: each model gets exactly the papers of Brother's paper table for it (`docs/data/*.json`). The QL-1050/1060N add 102 mm and 103 mm tape and three wide die-cut labels
(102 × 51, 102 × 152, 103 × 164 mm). The QL-1100/1110NWB have these plus 29 × 42 and 60 × 86 mm, with the
103 mm papers 36 dots wider printable than on the QL-1050/1060N; the QL-1115NWB has no 103 mm papers but four
direct-thermal papers (DT 90 mm, DT 102 mm, DT 102 × 51, DT 102 × 152). Brother's "crop print" page
(215.9 × 297 mm) and the two "Shipping label" papers (the same as 103 × 164 mm and 103 mm tape) are left out.
Multi-up papers ("62mm x 2" etc.) are sent as one label per strip.

## 3. Printer-side facts

| Model | Status reply series / model code (bytes 3 / 4) | Invalidate before `ESC @` | Raster compression |
|---|---|---|---|
| QL-500 | `0x30` / `0x4F` (same as QL-550) | 200 × `00` | never |
| QL-550 | `0x30` / `0x4F` | 200 | never |
| QL-560 | `0x34` / `0x31` | 200 | never |
| QL-570 | `0x34` / `0x32` | 200 | never |
| QL-580N | `0x34` / `0x33` | 200 | PackBits + `Z` except on USB |
| QL-600 | `0x34` / `0x47` | 200 | never (`ESC i a FF` after the job) |
| QL-650TD | `0x30` / `0x51` | 200 | never (`M 00`) |
| QL-700 | `0x34` / `0x35` | 200 | never |
| QL-710W | `0x34` / `0x36` | 200 | PackBits + `Z` except on USB |
| QL-720NW | `0x34` / `0x37` | 200 | PackBits + `Z` except on USB |
| QL-800 | `0x34` / `0x38` | 400 | never (`M 00`) |
| QL-810W | `0x34` / `0x39` | 400 | PackBits + `Z` except on USB |
| QL-820NWB | `0x34` / `0x41` | 400 | PackBits + `Z` except on USB |
| QL-1050 | `0x30` / `0x50` | 200 | PackBits + `Z` except on USB |
| QL-1060N | `0x34` / `0x34` | 350 | PackBits + `Z` except on USB |
| QL-1100 | `0x34` / `0x43` | 350 | PackBits + `Z` except on USB (assumed: USB form not captured) |
| QL-1110NWB | `0x34` / `0x44` | 350 | same |
| QL-1115NWB | `0x34` / `0x45` | 350 | same; `ESC i U w` media block on direct-thermal papers |

Status codes: read from the QL-570 and QL-800; for the other models taken from Brother's language-monitor
`.ini` files.

## 4. Test status

| Model | Real printer | Brother captures reproduced by the encoder (`replay_test`) | Installed driver vs captures (`compare.ps1`) |
|---|---|---|---|
| QL-500 | – | 15 | ✓ |
| QL-550 | – | 16 | ✓ |
| QL-560 | – | 19 | ✓ |
| QL-570 | **✓** (USB, status; 62 mm and 38 mm continuous: DK-22205, DK-22225) | 20 | ✓ |
| QL-580N | – | 20 | ✓ |
| QL-600 | – | 20 | ✓ |
| QL-650TD | – | 18 | ✓ |
| QL-700 | – | 20 | ✓ |
| QL-710W | – | 20 | ✓ |
| QL-720NW | **✓** over the network (status over SNMP, test kit's 62 mm label: DK-22205; 2026-11-05); USB not tested | 20 | ✓ |
| QL-800 | **✓** (all features, two-colour, status; 62 mm, 29 × 90 die-cut, 12 mm round: DK-22205, DK-22251, DK-11201, DK-11219) | 15 | ✓ |
| QL-810W | – | 23 | – (no capture printer yet) |
| QL-820NWB | – | 23 | – (no capture printer yet) |
| QL-1050 | – | 27 | ✓ |
| QL-1060N | – | 27 | ✓ |
| QL-1100 | – | 32 | ✓ |
| QL-1110NWB | – | 32 | ✓ |
| QL-1115NWB | – | 31 | ✓ |

`replay_test` decodes every capture and re-encodes it: the output must be byte-identical (two-colour: same
planes). `compare.ps1` prints Brother's test page through the installed driver and compares the result with
the capture; the remaining differences are listed in [differences-from-brother.md](differences-from-brother.md).

Owners of the other models can help: the [test kit](../tools/testkit/README.md) reads the printer's status and
prints a few labels with exactly the data our driver sends, without installing anything, and writes a report.

