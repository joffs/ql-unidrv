@echo off
rem Builds deskrun.exe into out\tools\uitest (inbox C# compiler, no SDK).
setlocal
set "HERE=%~dp0"
set "OUT=%HERE%..\..\out\tools\uitest"
if not exist "%OUT%" mkdir "%OUT%"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" -nologo -platform:anycpu -r:System.Management.dll -out:"%OUT%\deskrun.exe" "%HERE%deskrun.cs" || exit /b 1
echo Build OK: %OUT%\deskrun.exe
