@echo off
rem Delete saved settings; next start of Connect.bat will ask for a new tt:// link.
cd /d "%~dp0"
if exist trusttunnel_client.toml del /q trusttunnel_client.toml
echo Settings removed. Run Connect.bat and paste the new link.
pause
