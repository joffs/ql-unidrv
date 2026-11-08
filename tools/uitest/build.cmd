@echo off
rem Builds deskrun.exe into out\tools\uitest (inbox C# compiler, no SDK).
setlocal
set "HERE=%~dp0"
set "OUT=%HERE%..\..\out\tools\uitest"
if not exist "%OUT%" mkdir "%OUT%"
rem deskrun.exe may be running (other sessions' tests): a running exe can be renamed, not overwritten. Old copies
rem that no longer run are deleted here.
del "%OUT%\deskrun.old-*.exe" 2>nul
if exist "%OUT%\deskrun.exe" move /y "%OUT%\deskrun.exe" "%OUT%\deskrun.old-%RANDOM%.exe" >nul
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -platform:anycpu -r:System.Management.dll -out:"%OUT%\deskrun.exe" "%HERE%deskrun.cs" || exit /b 1
echo Build OK: %OUT%\deskrun.exe
