; SABI TUNNEL for Windows - Inno Setup 6 script.
; Build:  ISCC.exe /DPkgDir=<folder with the assembled package> /DAppVer=3.1.0 /O<outdir> installer.iss
; The app updates itself: it downloads a newer SabiTunnel-Setup.exe from the panel feed into {app}\update, checks its
; SHA-256 and runs it with /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-, then starts SabiTunnel.exe --updated.

#ifndef PkgDir
  #define PkgDir "..\pkg\SabiTunnel"
#endif
#ifndef AppVer
  #define AppVer "3.1.0"
#endif

[Setup]
; the AppId of "TrustTunnel (Sabi VPN)" 2.x / 3.0.x: installs over it (keeping its folder and settings)
AppId={{8B5E2A7C-3D41-4F0B-9C77-5A1E6D2F9B10}
AppName=SABI TUNNEL
AppVersion={#AppVer}
AppVerName=SABI TUNNEL {#AppVer}
AppPublisher=sabi-karami
AppPublisherURL=https://github.com/TrustTunnel/TrustTunnel
DefaultDirName={autopf}\SabiTunnel
DefaultGroupName=SABI TUNNEL
UsePreviousGroup=no
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=SabiTunnel-Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\SabiTunnel.exe
UninstallDisplayName=SABI TUNNEL
VersionInfoProductName=SABI TUNNEL
VersionInfoDescription=SABI TUNNEL Setup
VersionInfoVersion={#AppVer}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
UsedUserAreasWarning=no
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
; 3.0.x and older were TrustTunnel.exe with "TrustTunnel" shortcuts; never touch {app}\update (Setup runs from there)
Type: files; Name: "{app}\TrustTunnel.exe"
Type: files; Name: "{app}\TrustTunnel.exe.config"
Type: filesandordirs; Name: "{commonprograms}\TrustTunnel"
Type: files; Name: "{autodesktop}\TrustTunnel.lnk"

[Files]
Source: "{#PkgDir}\*"; DestDir: "{app}"; Excludes: "bypass.txt,trusttunnel_client.toml,gui.ini,servers\*,update\*,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
; the user's own list of direct sites survives upgrades
Source: "{#PkgDir}\bypass.txt"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\SABI TUNNEL"; Filename: "{app}\SabiTunnel.exe"; WorkingDir: "{app}"
Name: "{group}\SABI TUNNEL - Guide (راهنما)"; Filename: "{app}\راهنما.txt"
Name: "{group}\Uninstall SABI TUNNEL"; Filename: "{uninstallexe}"
Name: "{autodesktop}\SABI TUNNEL"; Filename: "{app}\SabiTunnel.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; tt:// links open the app (it also re-registers itself per user on start)
Root: HKA; Subkey: "Software\Classes\tt"; ValueType: string; ValueName: ""; ValueData: "URL:SABI TUNNEL link"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\tt"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\tt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\SabiTunnel.exe"",0"
Root: HKA; Subkey: "Software\Classes\tt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\SabiTunnel.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\tt"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\SabiTunnel.exe"; Description: "{cm:LaunchProgram,SABI TUNNEL}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
function Running(const Image: String): Boolean;
var rc: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /NH /FI "IMAGENAME eq ' + Image + '" | find /I "' + Image + '" >nul', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
end;

{ graceful: the app disconnects the tunnel (routes, DNS, kill switch) and exits }
procedure QuitApp(const Dir, Exe: String);
var rc, i: Integer;
begin
  if Running(Exe) and FileExists(Dir + '\' + Exe) then
  begin
    Exec(Dir + '\' + Exe, '--quit', Dir, SW_HIDE, ewNoWait, rc);
    for i := 1 to 30 do
    begin
      Sleep(500);
      if not Running(Exe) and not Running('trusttunnel_client.exe') then Break;
    end;
  end;
end;

procedure StopApp(const Dir: String);
var rc: Integer;
begin
  QuitApp(Dir, 'SabiTunnel.exe');
  QuitApp(Dir, 'TrustTunnel.exe');
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM SabiTunnel.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TrustTunnel.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM trusttunnel_client.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var rc: Integer;
begin
  { "run at sign-in" of 3.0.x starts TrustTunnel.exe, which is gone now: move that task over to SabiTunnel.exe }
  if CurStep = ssPostInstall then
    Exec(ExpandConstant('{app}\SabiTunnel.exe'), '--migrate', ExpandConstant('{app}'), SW_HIDE, ewWaitUntilTerminated, rc);
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var rc: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopApp(ExpandConstant('{app}'));
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN SabiTunnel', '', SW_HIDE, ewWaitUntilTerminated, rc);
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN TrustTunnelGUI', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
  if CurUninstallStep = usPostUninstall then
    { settings contain the user's password: remove everything }
    DelTree(ExpandConstant('{app}'), True, True, True);
end;
