# Driver and setup tests

The C++ tests (`encoder_test`, `labels_test`, `replay_test`) run in `driver\build.cmd`. The scripts below change the
PC they run on, so they run **in Windows Sandbox only** (the x64 session's harness starts them there):

| Command | What it does |
|---|---|
| `powershell -File driver\test\install-runs.ps1` | 19 `install.ps1` runs (settings, ports, shares, remove) with transcripts and state lists; `-Compare` checks two recordings file by file |
| `powershell -File driver\test\share-test.ps1` | Sharing and unsharing printers through `install.ps1 -Settings`, 17 checks |
| `powershell -File driver\test\cold-start-sandbox.ps1 -Out <folder> [-Purge] [-Wpr] [-FontSource <folder> -FontCount 1800]` | First start of QL Label Formats and QL Label Maker as after a reboot: `purge-standby.exe` (built from `purge-standby.cs` with the framework's csc) empties the file cache, optional wpr trace (CPU, disk, file I/O), optional many per-user fonts; times and `QL_STARTUP_TRACE` files per run in `<folder>` |
