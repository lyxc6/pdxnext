@echo off
rem  Launch the Debug build.  ASCII only -- see build.cmd.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "EXE=src\PdxLauncher.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe"
if not exist "%EXE%" (
    call "%~dp0scripts\msg.cmd" RD_NOEXE
    call "%~dp0scripts\msg.cmd" RD_EXPECT %EXE%
    exit /b 1
)

start "" "%EXE%"
exit /b 0
