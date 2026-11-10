; TrustTunnel for Windows (Sabi VPN) — Inno Setup 6 script.
; Build:  ISCC.exe /DPkgDir=<folder with the assembled package> /DAppVer=2.1.0 /O<outdir> installer.iss

#ifndef PkgDir
  #define PkgDir "..\pkg\TrustTunnel"
#endif
#ifndef AppVer
  #define AppVer "2.1.0"
#endif

[Setup]
AppId={{8B5E2A7C-3D41-4F0B-9C77-5A1E6D2F9B10}
AppName=TrustTunnel (Sabi VPN)
AppVersion={#AppVer}
AppVerName=TrustTunnel (Sabi VPN) {#AppVer}
AppPublisher=sabi-karami
AppPublisherURL=https://github.com/TrustTunnel/TrustTunnel
DefaultDirName={autopf}\TrustTunnel
DefaultGroupName=TrustTunnel
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputBaseFilename=TrustTunnel-Setup
SetupIconFile=app.ico
UninstallDisplayIcon={app}\TrustTunnel.exe
UninstallDisplayName=TrustTunnel (Sabi VPN)
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=no
UsedUserAreasWarning=no
ChangesAssociations=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PkgDir}\*"; DestDir: "{app}"; Excludes: "bypass.txt,trusttunnel_client.toml,gui.ini,servers\*,*.log"; Flags: ignoreversion recursesubdirs createallsubdirs
; the user's own list of direct sites survives upgrades
Source: "{#PkgDir}\bypass.txt"; DestDir: "{app}"; Flags: onlyifdoesntexist

[Icons]
Name: "{group}\TrustTunnel"; Filename: "{app}\TrustTunnel.exe"; WorkingDir: "{app}"
Name: "{group}\TrustTunnel — Guide (راهنما)"; Filename: "{app}\راهنما.txt"
Name: "{group}\Uninstall TrustTunnel"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TrustTunnel"; Filename: "{app}\TrustTunnel.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
; tt:// links open the app (it also re-registers itself per user on start)
Root: HKA; Subkey: "Software\Classes\tt"; ValueType: string; ValueName: ""; ValueData: "URL:TrustTunnel link"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\tt"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\tt\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\TrustTunnel.exe"",0"
Root: HKA; Subkey: "Software\Classes\tt\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\TrustTunnel.exe"" ""%1"""
Root: HKCU; Subkey: "Software\Classes\tt"; Flags: uninsdeletekey dontcreatekey

[Run]
Filename: "{app}\TrustTunnel.exe"; Description: "{cm:LaunchProgram,TrustTunnel}"; Flags: nowait postinstall skipifsilent shellexec

[Code]
function Running(const Image: String): Boolean;
var rc: Integer;
begin
  Result := Exec(ExpandConstant('{cmd}'), '/c tasklist /NH /FI "IMAGENAME eq ' + Image + '" | find /I "' + Image + '" >nul', '', SW_HIDE, ewWaitUntilTerminated, rc) and (rc = 0);
end;

procedure StopApp(const Dir: String);
var rc, i: Integer;
begin
  if Running('TrustTunnel.exe') and FileExists(Dir + '\TrustTunnel.exe') then
  begin
    { graceful: the GUI disconnects the tunnel (routes, DNS, kill switch) and exits }
    Exec(Dir + '\TrustTunnel.exe', '--quit', Dir, SW_HIDE, ewNoWait, rc);
    for i := 1 to 30 do
    begin
      Sleep(500);
      if not Running('TrustTunnel.exe') and not Running('trusttunnel_client.exe') then Break;
    end;
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM TrustTunnel.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM trusttunnel_client.exe', '', SW_HIDE, ewWaitUntilTerminated, rc);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopApp(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var rc: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopApp(ExpandConstant('{app}'));
    Exec(ExpandConstant('{sys}\schtasks.exe'), '/Delete /F /TN TrustTunnelGUI', '', SW_HIDE, ewWaitUntilTerminated, rc);
  end;
  if CurUninstallStep = usPostUninstall then
    { settings contain the user's password: remove everything }
    DelTree(ExpandConstant('{app}'), True, True, True);
end;
