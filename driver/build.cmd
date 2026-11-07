@echo off
rem Builds the render plug-in for ARM64 (..\out\arm64) and/or x64 (..\out\x64), puts the GPDs, INIs and
rem the INF into ..\out\driver and runs the unit tests and the replay of Brother's reference captures.
rem The label tool is built by ..\labeltool\build.cmd; ..\build.cmd builds both.
rem Works on an ARM64 or an x64 PC: the tests always run natively; the other architecture's plug-in is
rem built by the x64 compiler under emulation (ARM64 PC) or the ARM64 cross compiler (x64 PC; needs the
rem Visual Studio component "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools (Latest)").
rem Usage: build.cmd [this | x64 | arm64 | both]   (from any directory; default: this PC's architecture)
rem The architectures built are written to ..\out\driver\architectures.txt (read by package.ps1).
setlocal
pushd "%~dp0"
set "OUT=..\out"

set "HOSTARCH=x64"
if /i "%PROCESSOR_ARCHITECTURE%"=="ARM64" set "HOSTARCH=arm64"
if /i "%PROCESSOR_ARCHITEW6432%"=="ARM64" set "HOSTARCH=arm64"
set "OTHER=arm64"
if "%HOSTARCH%"=="arm64" set "OTHER=x64"
set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=this"
if /i "%TARGET%"=="this" set "TARGET=%HOSTARCH%"
set "BUILDHOST="
set "BUILDOTHER="
if /i "%TARGET%"=="both" (set "BUILDHOST=1" & set "BUILDOTHER=1")
if /i "%TARGET%"=="%HOSTARCH%" set "BUILDHOST=1"
if /i "%TARGET%"=="%OTHER%" set "BUILDOTHER=1"
if not defined BUILDHOST if not defined BUILDOTHER (echo Usage: build.cmd [this ^| x64 ^| arm64 ^| both] & goto :fail)

python --version >nul 2>&1 || (echo Python 3 not found. Install it from https://www.python.org/downloads/ and tick "Add python.exe to PATH", see BUILDING.md. & goto :fail)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%i in (`call "%%VSWHERE%%" -latest -products * -property installationPath`) do set "VSDIR=%%i"
if not defined VSDIR (echo Visual Studio not found. Install Visual Studio 2022 Community with "Desktop development with C++", see BUILDING.md. & goto :fail)
rem Each architecture gets its own compiler environment (setlocal ... endlocal).
setlocal
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" %HOSTARCH% >nul
call :checkcl %HOSTARCH% || goto :fail

python -I gen\media.py || goto :fail
rem The driver version: driver\VERSION while the sources are as recorded there (gen\version.py)
if not exist %OUT%\obj mkdir %OUT%\obj
python -I gen\version.py build %OUT%\obj || goto :fail

if not exist %OUT%\obj mkdir %OUT%\obj
set CFLAGS=/nologo /std:c++17 /O2 /W4 /WX /MT /EHsc /GS /guard:cf /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /Fo:%OUT%\obj\

rem Unit tests and replay of Brother's reference captures (run natively)
cl %CFLAGS% test\encoder_test.cpp src\ql_raster.cpp /Fe:%OUT%\obj\encoder_test.exe || goto :fail
cl %CFLAGS% test\labels_test.cpp src\labels.cpp src\ql_raster.cpp /Fe:%OUT%\obj\labels_test.exe || goto :fail
cl %CFLAGS% test\replay_test.cpp src\ql_raster.cpp /Fe:%OUT%\obj\replay_test.exe || goto :fail
%OUT%\obj\encoder_test.exe || goto :fail
%OUT%\obj\labels_test.exe || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql500 500 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql550 550 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql560 560 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql570 570 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql580n 580 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql600 600 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql650td 650 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql700 700 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql710w 710 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql720nw 720 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql800 800 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql810w 810 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql820nwb 820 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql1050 1050 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql1060n 1060 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql1100 1100 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql1110nwb 1110 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\ql1115nwb 1115 || goto :fail
rem The test kit's pages (neutral text "QL test")
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql500 500 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql550 550 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql560 560 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql570 570 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql580n 580 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql600 600 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql650td 650 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql700 700 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql710w 710 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql720nw 720 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql800 800 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql810w 810 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql820nwb 820 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql1050 1050 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql1060n 1060 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql1100 1100 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql1110nwb 1110 || goto :fail
%OUT%\obj\replay_test.exe ..\reference\captures\testkit\ql1115nwb 1115 || goto :fail

rem Render plug-in DLL for this PC's architecture
if defined BUILDHOST call :plugin %HOSTARCH% || goto :fail
endlocal

rem The other architecture's plug-in from the same sources. ARM64 PC: the x64-hosted compiler runs under
rem emulation (VS 17.14 has no ARM64-hosted x64 compiler), plus one replay run of the x64 encoder.
rem x64 PC: the ARM64 cross compiler (its output can't run here).
if not defined BUILDOTHER goto :files
setlocal
set "VCARGS=x64"
if "%OTHER%"=="arm64" set "VCARGS=x64_arm64"
call "%VSDIR%\VC\Auxiliary\Build\vcvarsall.bat" %VCARGS% >nul
call :checkcl %OTHER% || goto :fail
if not exist %OUT%\obj\%OTHER% mkdir %OUT%\obj\%OTHER%
set CFLAGS=/nologo /std:c++17 /O2 /W4 /WX /MT /EHsc /GS /guard:cf /DUNICODE /D_UNICODE /D_CRT_SECURE_NO_WARNINGS /Fo:%OUT%\obj\%OTHER%\
if "%OTHER%"=="x64" (
   cl %CFLAGS% test\replay_test.cpp src\ql_raster.cpp /Fe:%OUT%\obj\x64\replay_test.exe || goto :fail
   %OUT%\obj\x64\replay_test.exe ..\reference\captures\ql800 800 || goto :fail
)
call :plugin %OTHER% || goto :fail
endlocal

rem GPDs, INIs and INF for both architectures (package.ps1 stages them from there)
:files
if not exist %OUT%\driver mkdir %OUT%\driver
for %%m in (500 550 560 570 580 600 650 700 710 720 800 810 820 1050 1060 1100 1110 1115) do (
   copy /y gpd\QLUnidrv%%m.GPD %OUT%\driver\ >nul
   copy /y gpd\QLUnidrv%%m.INI %OUT%\driver\ >nul
)
copy /y inf\qlunidrv.inf %OUT%\driver\ >nul
python -I gen\version.py stamp %OUT%\obj %OUT%\driver || goto :fail
set "BUILT=%TARGET%"
if /i "%TARGET%"=="both" set "BUILT=arm64 x64"
>%OUT%\driver\architectures.txt echo %BUILT%

echo.
for %%a in (%BUILT%) do for %%i in (%OUT%\%%a) do echo Build OK: %%~fi
popd
exit /b 0

:checkcl
rem %1 = arm64 or x64: the compiler in the current environment must target it. vcvarsall reports
rem success even when the target's tools are missing (then the host's cl is found), so check the
rem folder of the cl.exe found first (...\bin\Host<host>\<target>\cl.exe; the banner is localized).
set "CLDIR="
for /f "delims=" %%c in ('where cl 2^>nul') do if not defined CLDIR set "CLDIR=%%~dpc"
if defined CLDIR for %%d in ("%CLDIR:~0,-1%") do if /i "%%~nxd"=="%1" exit /b 0
echo.
echo The C++ compiler for %1 is not installed. In the Visual Studio Installer: Modify, tab
echo "Individual components", type %1 in the search box and tick
if /i "%1"=="arm64" echo   "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools (Latest)"
if /i "%1"=="x64" echo   "MSVC v143 - VS 2022 C++ x64/x86 build tools (Latest)"
echo (not the "Spectre-mitigated libs", ATL or MFC entries), then Modify. See BUILDING.md.
exit /b 1

:plugin
rem %1 = arm64 or x64; uses the current compiler environment and CFLAGS.
rem KERNEL_MODE selects the render (not UI) plug-in interfaces in printoem.h/prcomoem.h.
if not exist %OUT%\%1 mkdir %OUT%\%1
cl %CFLAGS% /DKERNEL_MODE /LD src\plugin.cpp src\labels.cpp src\ql_raster.cpp src\usb_status.cpp /Fe:%OUT%\%1\QLUnidrv.dll ^
   /link /DEF:src\qlunidrv.def /GUARD:CF /DYNAMICBASE /NXCOMPAT kernel32.lib user32.lib ole32.lib uuid.lib advapi32.lib ^
   winspool.lib
exit /b %errorlevel%

:fail
echo BUILD FAILED
popd
exit /b 1
