@echo off
rem Builds everything into out\: the driver (driver\build.cmd, including its tests) and the label
rem tool (labeltool\build.cmd). Next steps: driver\package.ps1, then driver\install.ps1 (see README.md).
rem Usage: build.cmd [this | x64 | arm64 | both]
rem   this (default): the driver for this PC's architecture; both: one package for x64 and ARM64 PCs.
rem   Started by double-click (no argument): asks, offers to make the package and waits at the end.
setlocal
set "HOSTARCH=x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "HOSTARCH=arm64"
if /i "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "HOSTARCH=arm64"
set "OTHER=arm64"
if "%HOSTARCH%"=="arm64" set "OTHER=x64"
set "HOSTNAME=x64"
set "OTHERNAME=ARM64"
if "%HOSTARCH%"=="arm64" (set "HOSTNAME=ARM64" & set "OTHERNAME=x64")

set "GUIDED="
if "%~1"=="" echo %CMDCMDLINE% | findstr /c:"\"\"" >nul && set "GUIDED=1"
set "TARGET=%~1"
if defined GUIDED goto :ask
goto :build

:ask
echo.
echo  QL Unidrv driver - build
echo  ========================
echo  This PC is %HOSTNAME%. Which driver do you want to build?
echo.
echo    1  For this PC only (%HOSTNAME%)                     - recommended
echo    2  For both x64 and ARM64 PCs (one package for both)
echo    3  Only for %OTHERNAME% PCs (another PC)
echo.
choice /c 123 /n /t 300 /d 1 /m "Your choice [1, 2 or 3]: "
if errorlevel 3 (set "TARGET=%OTHER%") else if errorlevel 2 (set "TARGET=both") else (set "TARGET=this")
echo.

:build
call "%~dp0driver\build.cmd" %TARGET% || goto :failed
call "%~dp0labeltool\build.cmd" || goto :failed
if not defined GUIDED exit /b 0

echo.
echo  Build finished. Next step: the signed driver package (driver\package.ps1). On the first run it
echo  creates your own signing certificate in your user certificate store. No administrator rights needed.
choice /c YN /n /m "Create the package now? [Y/N]: "
if errorlevel 2 goto :done
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0driver\package.ps1" || goto :failed
echo.
echo  Package ready in out\package. To install it, follow "Install" in README.md
echo  (an administrator PowerShell runs driver\install.ps1; ARM64 PCs need the one-time F7 boot first).
:done
echo.
pause
exit /b 0

:failed
if defined GUIDED (echo. & echo  Something went wrong, see the messages above. & pause)
exit /b 1
