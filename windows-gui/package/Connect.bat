@echo off
title SABI TUNNEL
cd /d "%~dp0"
set "TT_SELF=%~f0"

rem --- ask for administrator rights (needed for the virtual network adapter) ---
net session >nul 2>&1
if not errorlevel 1 goto run
powershell -NoProfile -Command "Start-Process -FilePath $env:TT_SELF -Verb RunAs"
exit /b

:run
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tt-run.ps1"
echo.
pause
