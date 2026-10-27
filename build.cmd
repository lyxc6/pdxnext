@echo off
rem ---------------------------------------------------------------------------
rem  PdxNext build (Release) + self checks.
rem
rem  NOTE: every top-level .cmd in this repo must stay PURE ASCII.
rem  cmd.exe re-reads batch files byte by byte; non-ASCII bytes can tear a
rem  line apart under some console code pages and print bogus errors.
rem  All localized text lives in scripts\messages.txt and is printed by
rem  scripts\msg.cmd -- keep it that way.
rem ---------------------------------------------------------------------------
chcp 65001 >nul
setlocal
cd /d "%~dp0"

echo ============================================
call "%~dp0scripts\msg.cmd" B_TITLE
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    call "%~dp0scripts\msg.cmd" B_NODOTNET
    exit /b 1
)

rem -m:1 is required: PdxLauncher.App declares Platforms=x64 while the solution
rem configuration is Any CPU; a parallel MSBuild fails silently (0 errors) in
rem _GetProjectReferenceTargetFrameworkProperties. Single node build is fine.
rem See README section 1.
call "%~dp0scripts\msg.cmd" B_STEP1
dotnet build "PdxLauncher.sln" -c Release -m:1
if errorlevel 1 (
    echo.
    call "%~dp0scripts\msg.cmd" B_FAILBUILD
    exit /b 1
)

echo.
call "%~dp0scripts\msg.cmd" B_STEP2
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"
if errorlevel 1 (
    echo.
    call "%~dp0scripts\msg.cmd" B_FAILCHECK
    exit /b 1
)

echo.
call "%~dp0scripts\msg.cmd" B_STEP3
call "%~dp0scripts\msg.cmd" B_EXE src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe
exit /b 0
