@echo off
rem ---------------------------------------------------------------------------
rem  PdxNext dev entry point for automated agents and other conversations.
rem
rem      dev.cmd            stop the old instance, build Release, self check, run
rem      dev.cmd build      stop the old instance, build + self check, do not run
rem      dev.cmd nobuild    skip build, stop the old instance and run the exe
rem      dev.cmd logs       run, then follow data\logs\pdxnext.log in this console
rem      dev.cmd /?         usage
rem
rem  ASCII only -- see build.cmd. Localized text lives in scripts\messages.txt.
rem  The last lines are machine readable DEV_* markers.
rem  Exit codes: 0 ok, 1 build failed, 2 checks failed, 3 no exe, 4 app died.
rem ---------------------------------------------------------------------------
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%~dp0"

set "CONFIG=Release"
set "MODE=run"
set "NOBUILD=0"
set "NOLAUNCH=0"
set "FOLLOW=0"
set "RC=1"

:parseargs
if "%~1"=="" goto argsdone
if /i "%~1"=="build" (
    set "MODE=build"
    set "NOBUILD=0"
    set "NOLAUNCH=1"
)
if /i "%~1"=="nobuild" (
    set "MODE=nobuild"
    set "NOBUILD=1"
    set "NOLAUNCH=0"
)
if /i "%~1"=="run" (
    set "MODE=run"
    set "NOBUILD=0"
    set "NOLAUNCH=0"
)
if /i "%~1"=="logs" (
    set "FOLLOW=1"
)
if /i "%~1"=="/?"     goto usage
if /i "%~1"=="-h"     goto usage
if /i "%~1"=="--help" goto usage
shift
goto parseargs

:usage
call "%ROOT%scripts\msg.cmd" V_USAGE1
call "%ROOT%scripts\msg.cmd" V_USAGE2
call "%ROOT%scripts\msg.cmd" V_USAGE3
call "%ROOT%scripts\msg.cmd" V_USAGE4
call "%ROOT%scripts\msg.cmd" V_USAGE5
call "%ROOT%scripts\msg.cmd" V_USAGE6
exit /b 0

:argsdone
rem logs means "run and follow": it wins over build's "do not launch" flag.
if "%FOLLOW%"=="1" set "NOLAUNCH=0"
set "EXE=src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe"
set "EXEDIR=%ROOT%src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64"
set "DATA=%EXEDIR%\data"
set "LOG=%DATA%\logs\pdxnext.log"

echo ============================================
call "%ROOT%scripts\msg.cmd" V_TITLE
echo ============================================
echo.

rem --- repo state (best effort, git is optional) ---
set "BRANCH=NA"
set "HEAD=NA"
set "DIRTY=NA"
where git >nul 2>nul
if errorlevel 1 goto gitdone
for /f "usebackq delims=" %%b in (`git rev-parse --abbrev-ref HEAD 2^>nul`) do set "BRANCH=%%b"
for /f "usebackq tokens=1 delims= " %%h in (`git log -1 --oneline 2^>nul`) do set "HEAD=%%h"
set /a DIRTY=0
for /f "usebackq delims=" %%L in (`git status --porcelain -uall 2^>nul`) do set /a DIRTY+=1
:gitdone
call "%ROOT%scripts\msg.cmd" V_BRANCH %BRANCH%
call "%ROOT%scripts\msg.cmd" V_HEAD %HEAD%
call "%ROOT%scripts\msg.cmd" V_DIRTY %DIRTY%
echo.

rem ---------------------------------------------------------------------------
rem  Stop the old instance first: a running app locks PdxLauncher.Core.dll, and
rem  a stale window would make UI checks measure the wrong build.
rem ---------------------------------------------------------------------------
call "%ROOT%scripts\msg.cmd" V_STOP
call :killapp
echo.

if "%NOBUILD%"=="1" goto launch

where dotnet >nul 2>nul
if errorlevel 1 (
    call "%ROOT%scripts\msg.cmd" V_NODOTNET
    echo DEV_RESULT=FAIL_BUILD
    set "RC=1"
    goto fail
)

call "%ROOT%scripts\msg.cmd" V_BUILDING
dotnet build "PdxLauncher.sln" -c %CONFIG% -m:1 --nologo -v:m
if errorlevel 1 (
    echo.
    call "%ROOT%scripts\msg.cmd" V_FAILBUILD
    echo DEV_RESULT=FAIL_BUILD
    set "RC=1"
    goto fail
)
echo.

call "%ROOT%scripts\msg.cmd" V_STEPCHECK
dotnet "tests\PdxLauncher.Checks\bin\%CONFIG%\net10.0\PdxLauncher.Checks.dll"
if errorlevel 1 (
    echo.
    call "%ROOT%scripts\msg.cmd" V_FAILCHECK
    echo DEV_RESULT=FAIL_CHECK
    set "RC=2"
    goto fail
)
call "%ROOT%scripts\msg.cmd" V_CHECKOK
echo.

if "%NOLAUNCH%"=="1" goto report_build

:launch
if not exist "%ROOT%%EXE%" (
    call "%ROOT%scripts\msg.cmd" V_NOEXE
    call "%ROOT%scripts\msg.cmd" V_EXPECT "%EXE%"
    echo DEV_RESULT=FAIL_NOEXE
    set "RC=3"
    goto fail
)

call "%ROOT%scripts\msg.cmd" V_START
call "%ROOT%scripts\msg.cmd" V_EXE "%EXE%"
powershell -NoProfile -Command "Start-Process -FilePath '%ROOT%%EXE%'" >nul 2>nul
if errorlevel 1 start "" "%ROOT%%EXE%"
call "%ROOT%scripts\msg.cmd" V_WAIT

set "PID="
set /a WAIT=0
:waitloop
ping -n 2 127.0.0.1 >nul
set "PID="
for /f "usebackq tokens=2 delims=," %%p in (`tasklist /fi "imagename eq PdxLauncher.App.exe" /fo csv /nh 2^>nul`) do set "PID=%%~p"
if defined PID goto launched
set /a WAIT+=1
if !WAIT! lss 10 goto waitloop
call "%ROOT%scripts\msg.cmd" V_FAILSTART "%LOG%"
echo DEV_RESULT=FAIL_LAUNCH
set "RC=4"
goto fail

:launched
call "%ROOT%scripts\msg.cmd" V_PID %PID%
echo.
call "%ROOT%scripts\msg.cmd" V_OKRUN
if "%FOLLOW%"=="0" call "%ROOT%scripts\msg.cmd" V_LOGHINT
echo.
echo DEV_RESULT=PASS
echo DEV_MODE=%MODE%
echo DEV_CONFIG=%CONFIG%
echo DEV_BRANCH=%BRANCH%
echo DEV_HEAD=%HEAD%
echo DEV_DIRTY=%DIRTY%
echo DEV_EXE=%ROOT%%EXE%
echo DEV_DATA=%DATA%
echo DEV_LOG=%LOG%
echo DEV_PID=%PID%
if "%FOLLOW%"=="1" call :followlog
exit /b 0

rem ---------------------------------------------------------------------------
rem  Follow the log file in this console. The app keeps running when the user
rem  stops the tail (Ctrl+C); only this console stops reading the file.
rem ---------------------------------------------------------------------------
:followlog
echo.
call "%ROOT%scripts\msg.cmd" V_FOLLOW
powershell -NoProfile -Command "$p='%LOG%'; for ($i=0; $i -lt 20 -and -not (Test-Path -LiteralPath $p); $i++) { Start-Sleep -Milliseconds 500 }; if (Test-Path -LiteralPath $p) { Get-Content -LiteralPath $p -Wait -Tail 20 -Encoding UTF8 } else { Write-Host '[log] pdxnext.log not found: %LOG%' }"
echo.
call "%ROOT%scripts\msg.cmd" V_FOLLOWSTOP
exit /b 0

:report_build
echo.
call "%ROOT%scripts\msg.cmd" V_OKBUILD
echo.
echo DEV_RESULT=PASS
echo DEV_MODE=%MODE%
echo DEV_CONFIG=%CONFIG%
echo DEV_BRANCH=%BRANCH%
echo DEV_HEAD=%HEAD%
echo DEV_DIRTY=%DIRTY%
echo DEV_EXE=%ROOT%%EXE%
echo DEV_DATA=%DATA%
echo DEV_LOG=%LOG%
echo DEV_PID=0
exit /b 0

:fail
echo.
echo DEV_MODE=%MODE%
echo DEV_CONFIG=%CONFIG%
echo DEV_BRANCH=%BRANCH%
echo DEV_HEAD=%HEAD%
echo DEV_DIRTY=%DIRTY%
echo DEV_PID=0
exit /b %RC%

:killapp
tasklist /fi "imagename eq PdxLauncher.App.exe" /nh 2>nul | findstr /i /c:"PdxLauncher.App.exe" >nul
if errorlevel 1 (
    call "%ROOT%scripts\msg.cmd" V_NOSTOP
    exit /b 0
)
call "%ROOT%scripts\msg.cmd" V_KILL
taskkill /im PdxLauncher.App.exe /f >nul 2>nul
ping -n 3 127.0.0.1 >nul
call "%ROOT%scripts\msg.cmd" V_KILLED
exit /b 0
