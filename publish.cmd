@echo off
rem ===========================================================================
rem  PdxNext publish (Release, self-contained -- just copy the folder around)
rem
rem  Usage:
rem      publish.cmd              output to dist\PdxNext\
rem      publish.cmd D:\out\Pdx   output to the given directory
rem
rem  Notes:
rem  - Do NOT use dotnet publish -o: -o drops the platform subfolder (x64) from
rem    $(OutDir), so the publish-completion target in the csproj cannot find the
rem    .xbf / .pri files there and the output is broken again. Use the default
rem    PublishDir and copy the whole directory to dist instead.
rem  - -m:1 same as build.cmd, avoids the App project failing silently across
rem    build nodes.
rem  - ASCII only -- see build.cmd.
rem ===========================================================================
chcp 65001 >nul
setlocal
cd /d "%~dp0"

set "TARGET=%~1"
if "%TARGET%"=="" set "TARGET=%CD%\dist\PdxNext"

where dotnet >nul 2>nul
if errorlevel 1 (
    call "%~dp0scripts\msg.cmd" P_NODOTNET
    exit /b 1
)

set "PUBDIR=src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\publish"

echo ============================================
call "%~dp0scripts\msg.cmd" P_TITLE
echo ============================================
echo.

call "%~dp0scripts\msg.cmd" P_STEP1
dotnet build "PdxLauncher.sln" -c Release -m:1 --nologo
if errorlevel 1 (
    echo.
    call "%~dp0scripts\msg.cmd" P_FAILBUILD
    exit /b 1
)

echo.
call "%~dp0scripts\msg.cmd" P_STEP2
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll" >nul 2>nul
if errorlevel 1 (
    call "%~dp0scripts\msg.cmd" P_FAILCHECK
    exit /b 1
)
call "%~dp0scripts\msg.cmd" P_CHECKOK

echo.
call "%~dp0scripts\msg.cmd" P_STEP3
rem Same for the publish staging dir: keep %PUBDIR%\data when someone runs the exe
rem straight from there.
set "PUBDATABACKUP=%PUBDIR%.__data__"
if exist "%PUBDATABACKUP%" rmdir /s /q "%PUBDATABACKUP%"
if exist "%PUBDIR%\data" move "%PUBDIR%\data" "%PUBDATABACKUP%" >nul
if exist "%PUBDIR%" rmdir /s /q "%PUBDIR%"
dotnet publish "src\PdxLauncher.App\PdxLauncher.App.csproj" -c Release -m:1 --nologo -p:Platform=x64
if errorlevel 1 (
    echo.
    call "%~dp0scripts\msg.cmd" P_FAILPUB
    exit /b 1
)

rem Fallback validation: publish must contain the XAML artifacts and the
rem resource index, otherwise the copied output is broken.
if not exist "%PUBDIR%\App.xbf"              goto :broken
if not exist "%PUBDIR%\PdxLauncher.App.pri"  goto :broken
if not exist "%PUBDIR%\Views\HomePage.xbf"   goto :broken
if not exist "%PUBDIR%\Controls\ConfirmOverlay.xbf" goto :broken
if not exist "%PUBDIR%\Assets\AppIcon.ico"   goto :broken
if exist "%PUBDATABACKUP%" move "%PUBDATABACKUP%" "%PUBDIR%\data" >nul

echo.
call "%~dp0scripts\msg.cmd" P_STEP4 "%TARGET%"
rem Keep the per-program data folder (%TARGET%\data: settings, logs, caches)
rem across republish; wiping it would erase the user settings and caches.
set "DATABACKUP=%TARGET%.__data__"
if exist "%DATABACKUP%" rmdir /s /q "%DATABACKUP%"
if exist "%TARGET%\data" move "%TARGET%\data" "%DATABACKUP%" >nul
if exist "%TARGET%" rmdir /s /q "%TARGET%"
mkdir "%TARGET%" 2>nul
xcopy "%PUBDIR%" "%TARGET%\" /E /I /Q /Y >nul
if errorlevel 1 (
    call "%~dp0scripts\msg.cmd" P_FAILDIR
    if exist "%DATABACKUP%" move "%DATABACKUP%" "%TARGET%\data" >nul
    exit /b 1
)

if exist "%DATABACKUP%" move "%DATABACKUP%" "%TARGET%\data" >nul
echo.
call "%~dp0scripts\msg.cmd" P_DONE
call "%~dp0scripts\msg.cmd" P_EXE %TARGET%\PdxLauncher.App.exe
call "%~dp0scripts\msg.cmd" P_SIZE
exit /b 0

:broken
if exist "%PUBDATABACKUP%" move "%PUBDATABACKUP%" "%PUBDIR%\data" >nul
echo.
call "%~dp0scripts\msg.cmd" P_BROKEN1
call "%~dp0scripts\msg.cmd" P_BROKEN2
call "%~dp0scripts\msg.cmd" P_BROKEN3
exit /b 1
