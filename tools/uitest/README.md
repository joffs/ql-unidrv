# UI tests without taking the user's focus

UI tests run while someone works on the same PC, so they must never move the mouse, take the keyboard focus or bring
a window to the front, on any monitor. `WS_EX_NOACTIVATE`, `SW_SHOWNOACTIVATE` and a CBT hook inside the test were not
enough. They cover only one thread and one process: message boxes, other threads and child processes slipped through.

**Rule:** start every program that opens windows for a test through `deskrun.exe`. That covers our exes, test harnesses and PowerShell scripts that show forms.

```
tools\uitest\build.cmd
out\tools\uitest\deskrun.exe <program.exe> [arguments...]
```

- The program runs on a Windows desktop of its own (`QlUiTest`). Its windows, and those of every process it starts, are invisible to the user and can't take the focus.
- Its output is passed through.
- Meanwhile deskrun checks the user's foreground window every 25 ms and logs each change with its process.
  - Changes to the user's own programs are just logged, because the user's own clicks count too.
  - If the foreground goes to a process of the test, deskrun stops the test at once (exit code 99).
- Exit code: the program's own; 98 means deskrun could not start the program.
- Last line: `... to a test process 0 time(s) - focus-free`.

Inside the test:
- Screenshots: `PrintWindow(hwnd, dc, 2)` works there. Screen captures (`CopyFromScreen`, `BitBlt` from the screen) do not.
- Clicks and typing: messages to the control's own window (`PostMessage`, `accDoDefaultAction`). Never `SetCursorPos`, `SendInput`, `SendKeys` or `SetForegroundWindow`.
- Monitors and DPI are the same as on the normal desktop. Windows placed on the 150 % monitor get 150 %.
- A harness can refuse to run on the normal desktop: if the name from `GetUserObjectInformation(GetThreadDesktop(GetCurrentThreadId()), UOI_NAME = 2)` is `Default`, it stops.
- PowerShell: `deskrun.exe C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -File <full path>\script.ps1`
