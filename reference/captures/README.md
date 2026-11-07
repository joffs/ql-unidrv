# Reference captures – Brother's drivers, one folder per model

Brother's own Windows drivers printing the same test page (border, text, bar pattern) to a file port, one case per
driver setting. They are the test oracle: `replay_test` (run by `build.cmd`) re-encodes every capture with this
driver's encoder and requires byte-identical output, and `driver/compare.ps1` prints the same cases through the
installed driver and compares the result.

Per case: `<case>.prn` (the stream as it goes to the printer), `<case>.devmode` (the DEVMODE used), `<case>.png`
(the raster in print-head order, i.e. mirrored left-right compared with the printed label; 300 × 600 twice as
tall). Some models also have `<case>.spool`, the driver's own output before its port monitor.

| Folder | Model |
|---|---|
| `ql500/` | QL-500 |
| `ql550/` | QL-550 |
| `ql560/` | QL-560 |
| `ql570/` | QL-570 |
| `ql580n/` | QL-580N |
| `ql600/` | QL-600 |
| `ql650td/` | QL-650TD |
| `ql700/` | QL-700 |
| `ql710w/` | QL-710W |
| `ql720nw/` | QL-720NW |
| `ql800/` | QL-800 |
| `ql810w/` | QL-810W |
| `ql820nwb/` | QL-820NWB |
| `ql1050/` | QL-1050 |
| `ql1060n/` | QL-1060N |
| `ql1100/` | QL-1100 |
| `ql1110nwb/` | QL-1110NWB |
| `ql1115nwb/` | QL-1115NWB |
| `testkit/` | all 18: the test kit's pages (neutral text "QL test"), which `tools/testkit/make-testkit.ps1` re-encodes |

The case names say what was set (paper, length, cut options, resolution, mirror, copies, ...); `driver/compare.ps1`
lists the settings per case.
