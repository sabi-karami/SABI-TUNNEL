@echo off
rem Delete saved settings and servers; on the next start SABI TUNNEL downloads the default servers again.
cd /d "%~dp0"
if exist trusttunnel_client.toml del /q trusttunnel_client.toml
if exist servers rmdir /s /q servers
if exist pending_link.txt del /q pending_link.txt
echo Settings removed. Start SabiTunnel.exe: the default servers are downloaded again (or paste a tt:// link).
pause
