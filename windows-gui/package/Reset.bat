@echo off
rem Delete saved settings; next start will ask for a new tt:// link.
cd /d "%~dp0"
if exist trusttunnel_client.toml del /q trusttunnel_client.toml
if exist servers rmdir /s /q servers
if exist pending_link.txt del /q pending_link.txt
echo Settings removed. Start TrustTunnel.exe (or Connect.bat) and paste the new link.
pause
