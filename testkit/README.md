# Test kit – help test the driver on your printer

The driver supports 18 Brother QL models, but only the **QL-570** and the **QL-800** have been tested on real
printers, and the **QL-720NW** over the network. For the other 15 the driver's output matches Brother's own driver
byte for byte – but nobody has seen a label come out yet. If you own one of them, **10 minutes and a few centimetres of labels** answer that.

> [!TIP]
> **In short:** download the zip for your model (step 1), run one command (step 2), send the report file
> (step 3). Even one printed label is already a useful report.

**You need:** the printer on **USB** for the full test (a network model without USB: status and one label over
the network, step 4 below), any Windows 10/11 PC (x64 or ARM64; your Brother driver can stay installed), and
**one roll – any Brother roll your model takes**: the kit has a test label for every one of them (the list with
the DK numbers is in the kit's README) and offers those that fit the roll in the printer. The 62 mm continuous
roll (e.g. DK-22205, US: DK-2205) gets a few extra tests. **Nothing is installed or changed** on your PC and no
administrator rights are needed.

## 1. Download the kit for your model

| Model | Kit |
|---|---|
| QL-500 | [ql-testkit-QL-500.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-500.zip) |
| QL-550 | [ql-testkit-QL-550.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-550.zip) |
| QL-560 | [ql-testkit-QL-560.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-560.zip) |
| QL-570 | [ql-testkit-QL-570.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-570.zip) – tested, more reports welcome |
| QL-580N | [ql-testkit-QL-580N.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-580N.zip) |
| QL-600 | [ql-testkit-QL-600.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-600.zip) |
| QL-650TD | [ql-testkit-QL-650TD.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-650TD.zip) |
| QL-700 | [ql-testkit-QL-700.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-700.zip) |
| QL-710W | [ql-testkit-QL-710W.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-710W.zip) |
| QL-720NW | [ql-testkit-QL-720NW.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-720NW.zip) – tested over the network, a USB report welcome |
| QL-800 | [ql-testkit-QL-800.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-800.zip) – tested, more reports welcome |
| QL-810W | [ql-testkit-QL-810W.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-810W.zip) |
| QL-820NWB | [ql-testkit-QL-820NWB.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-820NWB.zip) |
| QL-1050 | [ql-testkit-QL-1050.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-1050.zip) |
| QL-1060N | [ql-testkit-QL-1060N.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-1060N.zip) |
| QL-1100 | [ql-testkit-QL-1100.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-1100.zip) |
| QL-1110NWB | [ql-testkit-QL-1110NWB.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-1110NWB.zip) |
| QL-1115NWB | [ql-testkit-QL-1115NWB.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-QL-1115NWB.zip) |
| several models | [ql-testkit-all.zip](https://github.com/joffs/ql-unidrv/raw/refs/heads/main/testkit/ql-testkit-all.zip) |

## 2. Run it

1. Right-click the zip → *Properties* → tick **Unblock** → OK (Windows blocks scripts from downloads), then unzip.
2. In the unzipped folder: *File* → *Open Windows PowerShell* (or Shift + right-click → *Open PowerShell window
   here*) and run:

   ```
   powershell -ExecutionPolicy Bypass -File .\qltest.ps1
   ```

3. The script shows your printer's model and roll and offers the test labels that fit the roll, each with a
   picture of how it should look: `Enter` prints it, `s` skips, `q` stops. After each label it asks whether it
   looks like the picture (`y` / `n`). Want to see every technical line too? Add `-Details`. Full instructions are in the `README.md` inside the zip. With QL Unidrv
   already installed, its status service (if on) is paused for the test and resumes by itself.
4. **Network models** (QL-580N, 710W, 720NW, 810W, 820NWB, 1060N, 1110NWB, 1115NWB) on your network: also run
   (or only this, if the printer isn't on USB), with your printer's IP address in place of `192.0.2.50`:

   ```
   powershell -ExecutionPolicy Bypass -File .\qltest.ps1 -Network 192.0.2.50 -Watch 60
   ```

   During the 60 seconds, open and close the roll cover once. It reads the printer's status over the network (as
   Brother's software does) and offers one test label over the network. If your model isn't on USB, the command
   from step 2 also asks for the IP address.

## 3. Send the result

**[Open a "Printer test report" issue](https://github.com/joffs/ql-unidrv/issues/new?template=printer-test-report.yml)**
and attach the file `qltest-report-<model>-<time>.txt` (and `...-network-...txt`) from the kit folder – plus a photo of the labels if
something looked wrong. No GitHub account? Mail it to **ql-unidrv@joffs.de**, and say if and how we may name you on the thanks page. Good results are just as useful as
bad ones. The report contains the kit's build date, the printer's status messages, the USB product ID, the driver
name and your answers – no personal data (no serial number, printer names, IP address, computer name or folders).

How the kit is made (for developers): [tools/testkit/README.md](../tools/testkit/README.md).
