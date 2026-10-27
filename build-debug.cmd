@echo off
rem  PdxNext build (Debug) + self checks.  ASCII only -- see build.cmd.
chcp 65001 >nul
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    call "%~dp0scripts\msg.cmd" D_NODOTNET
    exit /b 1
)

call "%~dp0scripts\msg.cmd" D_TITLE1
dotnet build "PdxLauncher.sln" -c Debug -m:1
if errorlevel 1 exit /b 1

echo.
call "%~dp0scripts\msg.cmd" D_TITLE2
dotnet "tests\PdxLauncher.Checks\bin\Debug\net10.0\PdxLauncher.Checks.dll"
if errorlevel 1 exit /b 1

echo.
call "%~dp0scripts\msg.cmd" D_EXE src\PdxLauncher.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe
exit /b 0
