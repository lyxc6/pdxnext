@echo off
rem ---------------------------------------------------------------------------
rem  PdxNext working tree status for humans.
rem
rem      status.cmd           show the status, then wait for a key
rem      status.cmd nopause   show the status and exit
rem
rem  Read only. ASCII only -- see build.cmd. Localized text lives in
rem  scripts\messages.txt and is printed by scripts\msg.cmd.
rem ---------------------------------------------------------------------------
chcp 65001 >nul
setlocal EnableExtensions EnableDelayedExpansion
cd /d "%~dp0"
set "ROOT=%~dp0"

set "PAUSE=1"
if /i "%~1"=="nopause" set "PAUSE=0"
if /i "%~1"=="/?" goto usage
if /i "%~1"=="-h" goto usage
if /i "%~1"=="--help" goto usage

set "EXE=src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe"

echo ============================================
call "%ROOT%scripts\msg.cmd" G_TITLE
echo ============================================
echo.

where git >nul 2>nul
if errorlevel 1 (
    call "%ROOT%scripts\msg.cmd" G_NOGIT
    goto end
)

set "BRANCH=?"
for /f "usebackq delims=" %%b in (`git rev-parse --abbrev-ref HEAD 2^>nul`) do set "BRANCH=%%b"
set "HEAD=?"
for /f "usebackq delims=" %%h in (`git log -1 --oneline 2^>nul`) do set "HEAD=%%h"

set /a STAGED=0
for /f "usebackq delims=" %%L in (`git diff --cached --name-only 2^>nul`) do set /a STAGED+=1
set /a UNSTAGED=0
for /f "usebackq delims=" %%L in (`git diff --name-only 2^>nul`) do set /a UNSTAGED+=1
set /a UNTRACKED=0
for /f "usebackq delims=" %%L in (`git ls-files --others --exclude-standard 2^>nul`) do set /a UNTRACKED+=1
set /a TOTAL=0
for /f "usebackq delims=" %%L in (`git status --porcelain -uall 2^>nul`) do set /a TOTAL+=1

call "%ROOT%scripts\msg.cmd" G_BRANCH %BRANCH%
call "%ROOT%scripts\msg.cmd" G_HEAD %HEAD%
echo.
call "%ROOT%scripts\msg.cmd" G_TOTAL %TOTAL%
call "%ROOT%scripts\msg.cmd" G_STAGED %STAGED%
call "%ROOT%scripts\msg.cmd" G_UNSTAGED %UNSTAGED%
call "%ROOT%scripts\msg.cmd" G_UNTRACKED %UNTRACKED%
echo.
call "%ROOT%scripts\msg.cmd" G_FILES

set /a SHOWN=0
set /a LIMIT=60
for /f "usebackq delims=" %%L in (`git -c "core.quotepath=false" status --short -uall 2^>nul`) do (
    set /a SHOWN+=1
    if !SHOWN! leq %LIMIT% echo %%L
)
if !SHOWN! gtr %LIMIT% (
    set /a REST=SHOWN-LIMIT
    call "%ROOT%scripts\msg.cmd" G_MORE !REST!
)
if !SHOWN!==0 call "%ROOT%scripts\msg.cmd" G_DIFF_NONE

echo.
call "%ROOT%scripts\msg.cmd" G_DIFF
set "SHORTSTAT="
for /f "usebackq delims=" %%s in (`git diff HEAD --shortstat 2^>nul`) do set "SHORTSTAT=%%s"
if defined SHORTSTAT (
    echo   !SHORTSTAT!
) else (
    call "%ROOT%scripts\msg.cmd" G_DIFF_NONE
)

echo.
call "%ROOT%scripts\msg.cmd" G_COMMITS
git log -5 --oneline 2>nul

echo.
set "APPID="
for /f "usebackq tokens=2 delims=," %%p in (`tasklist /fi "imagename eq PdxLauncher.App.exe" /fo csv /nh 2^>nul`) do set "APPID=%%~p"
if defined APPID (
    call "%ROOT%scripts\msg.cmd" G_RUNNING %APPID%
) else (
    call "%ROOT%scripts\msg.cmd" G_NOTRUNNING
)

if exist "%ROOT%%EXE%" (
    for %%F in ("%ROOT%%EXE%") do set "EXETIME=%%~tF"
    call "%ROOT%scripts\msg.cmd" G_EXETIME "!EXETIME!"
    call "%ROOT%scripts\msg.cmd" G_EXE "%EXE%"
) else (
    call "%ROOT%scripts\msg.cmd" G_NOEXE
)

:end
echo.
call "%ROOT%scripts\msg.cmd" G_DONE
if "%PAUSE%"=="1" (
    call "%ROOT%scripts\msg.cmd" G_PRESSKEY
    pause >nul
)
exit /b 0

:usage
call "%ROOT%scripts\msg.cmd" G_USAGE1
call "%ROOT%scripts\msg.cmd" G_USAGE2
call "%ROOT%scripts\msg.cmd" G_USAGE3
exit /b 0
