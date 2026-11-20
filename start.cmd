@echo off
rem ---------------------------------------------------------------------------
rem  PdxNext one-click launcher: incremental build + run.
rem
rem      start.cmd           Release: build, then launch
rem      start.cmd debug     Debug:   build, then launch
rem      start.cmd check     Release: build, run self checks, then launch
rem      start.cmd new       launch even if an instance is already running
rem
rem  ASCII only -- see build.cmd. Localized text lives in scripts\messages.txt.
rem ---------------------------------------------------------------------------
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%~dp0"

rem 1 = started from Explorer (cmd /c ...), keep the console open on failure.
set "INTERACTIVE=0"
if not "!cmdcmdline:/c=!"=="!cmdcmdline!" set "INTERACTIVE=1"

rem ---------------------------------------------------------------------------
rem  arguments
rem ---------------------------------------------------------------------------
set "CONFIG=Release"
set "FORCENEW=0"
set "RUNCHECKS=0"

:parseargs
if "%~1"=="" goto argsdone
if /i "%~1"=="debug"   set "CONFIG=Debug"
if /i "%~1"=="release" set "CONFIG=Release"
if /i "%~1"=="check"   set "RUNCHECKS=1"
if /i "%~1"=="new"     set "FORCENEW=1"
if /i "%~1"=="--new"   set "FORCENEW=1"
if /i "%~1"=="/?"      goto usage
if /i "%~1"=="-h"      goto usage
if /i "%~1"=="--help"  goto usage
shift
goto parseargs

:usage
call "%ROOT%scripts\msg.cmd" S_USAGE1
call "%ROOT%scripts\msg.cmd" S_USAGE2
call "%ROOT%scripts\msg.cmd" S_USAGE3
call "%ROOT%scripts\msg.cmd" S_USAGE4
exit /b 0

:argsdone
set "EXE=src\PdxLauncher.App\bin\x64\Release\net11.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe"
if /i "%CONFIG%"=="Debug" set "EXE=src\PdxLauncher.App\bin\x64\Debug\net11.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe"

echo ============================================
call "%ROOT%scripts\msg.cmd" S_TITLE
echo ============================================
echo.

rem ---------------------------------------------------------------------------
rem  Already running?  Only a process launched from THIS exe path counts: the app
rem  may also be installed elsewhere on the same machine, and that copy running
rem  must not block this one.  Skipped when "new" was passed, and when "check"
rem  asked for a self run.
rem ---------------------------------------------------------------------------
if "%FORCENEW%"=="1" goto build
if "%RUNCHECKS%"=="1" goto build
set "EXEABS=%ROOT%%EXE%"
set "LOCAL=0"
for /f "usebackq tokens=1,* delims==" %%a in (`powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%scripts\pdx-app-process.ps1" -Action status -Exe "%EXEABS%"`) do (
    if /i "%%a"=="LOCAL" set "LOCAL=%%b"
)
if not "%LOCAL%"=="1" goto build
call "%ROOT%scripts\msg.cmd" S_ALREADY
exit /b 0

rem ---------------------------------------------------------------------------
rem  incremental build (always, even when nothing changed)
rem ---------------------------------------------------------------------------
:build
where dotnet >nul 2>nul
if errorlevel 1 (
    call "%ROOT%scripts\msg.cmd" S_NODOTNET
    call :fail
    exit /b 1
)

call "%ROOT%scripts\msg.cmd" S_BUILDING
call "%ROOT%scripts\msg.cmd" S_CONFIG %CONFIG%
dotnet build "PdxLauncher.sln" -c %CONFIG% -m:1 --nologo -v:m
if errorlevel 1 (
    echo.
    call "%ROOT%scripts\msg.cmd" S_FAILBUILD
    call :fail
    exit /b 1
)

if "%RUNCHECKS%"=="1" (
    echo.
    call "%ROOT%scripts\msg.cmd" S_STEPCHECK
    dotnet "tests\PdxLauncher.Checks\bin\%CONFIG%\net11.0\PdxLauncher.Checks.dll"
    if errorlevel 1 (
        echo.
        call "%ROOT%scripts\msg.cmd" S_FAILCHECK
        call :fail
        exit /b 1
    )
    call "%ROOT%scripts\msg.cmd" S_CHECKOK
)

if not exist "%EXE%" (
    call "%ROOT%scripts\msg.cmd" S_NOEXE
    call "%ROOT%scripts\msg.cmd" S_EXPECT "%EXE%"
    call :fail
    exit /b 1
)

echo.
call "%ROOT%scripts\msg.cmd" S_STARTING
call "%ROOT%scripts\msg.cmd" S_EXE "%ROOT%%EXE%"
start "" "%ROOT%%EXE%"
exit /b 0

rem ---------------------------------------------------------------------------
rem  failure helper: park the window when started from Explorer
rem ---------------------------------------------------------------------------
:fail
call "%ROOT%scripts\msg.cmd" S_PRESSKEY
if "%INTERACTIVE%"=="1" pause >nul
exit /b 0
