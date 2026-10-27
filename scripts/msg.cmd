@echo off
rem ---------------------------------------------------------------------------
rem Prints one localized line from messages.txt (UTF-8, no BOM).
rem Usage:  call msg.cmd KEY [extra]
rem This file must stay pure ASCII: cmd re-reads batch files byte by byte,
rem and non-ASCII bytes in a .cmd can tear the line under some code pages.
rem ---------------------------------------------------------------------------
if "%~1"=="" exit /b 0
for /f "usebackq tokens=1,* delims=:" %%a in ("%~dp0messages.txt") do (
  if /i "%%a"=="%~1" (
    if "%~2"=="" (echo %%b) else (echo %%b %~2)
  )
)
exit /b 0
