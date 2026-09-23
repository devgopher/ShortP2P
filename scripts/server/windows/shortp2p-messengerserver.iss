; Inno Setup 6 script for ShortP2P Messenger Server (win-x64).
; Compile after publish.ps1 -Rid win-x64:
;   ISCC /DMyAppVersion=0.1.0 scripts\server\windows\shortp2p-messengerserver.iss
; Or:  powershell -File scripts\server\windows\build-installer.ps1

#ifndef MyAppVersion
  #define MyAppVersion "0.1.0"
#endif

#define MyAppName "ShortP2P Messenger Server"
#define MyAppPublisher "ShortP2P"
#define MyServiceName "ShortP2PMessengerServer"
#define MyAppExeName "ShortP2P.MessengerServer.Api.exe"
#define MyDataDir "{commonappdata}\ShortP2P\MessengerServer"

[Setup]
AppId={{A8E3C4F1-9B2D-4E6A-8C1F-7D3A5B9E2F10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\ShortP2P\MessengerServer
DefaultGroupName=ShortP2P
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
OutputDir=..\out\installer
OutputBaseFilename=shortp2p-messengerserver-{#MyAppVersion}-win-x64
UninstallDisplayName={#MyAppName}
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Dirs]
Name: "{#MyDataDir}"; Permissions: admins-full
Name: "{#MyDataDir}\data"; Permissions: admins-full; Flags: uninsneveruninstall
Name: "{#MyDataDir}\certs"; Permissions: admins-full; Flags: uninsneveruninstall

[Files]
; Binaries. Upgrade overwrites bin; never ship a PFX or LiteDB.
Source: "..\out\win-x64\*"; DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs; \
  Excludes: "*.litedb,*.pfx,appsettings.Production.json"
; Base appsettings.json into the data/config dir (content root). Safe to refresh on upgrade.
Source: "..\out\win-x64\appsettings.json"; DestDir: "{#MyDataDir}"; Flags: ignoreversion
; Admin Production overlay — create once, never overwrite, keep on uninstall.
Source: "appsettings.Production.json.example"; DestDir: "{#MyDataDir}"; \
  DestName: "appsettings.Production.json"; Flags: onlyifdoesntexist uninsneveruninstall

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "StopMessengerServer"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden waituntilterminated; RunOnceId: "DeleteMessengerServer"

[Code]
const
  ServiceName = '{#MyServiceName}';

function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result :=
    Exec(ExpandConstant('{sys}\sc.exe'), 'query ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
end;

procedure StopExistingService();
var
  ResultCode: Integer;
begin
  if ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(2000);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopExistingService();
  Result := '';
end;

procedure RegisterService();
var
  ResultCode: Integer;
  BinPath: String;
  CreateArgs: String;
  ConfigArgs: String;
begin
  BinPath :=
    '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" --contentRoot "' +
    ExpandConstant('{#MyDataDir}') + '" --environment Production';

  if ServiceExists() then
  begin
    ConfigArgs := 'config ' + ServiceName + ' binPath= "' + BinPath + '" start= auto';
    Exec(ExpandConstant('{sys}\sc.exe'), ConfigArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end
  else
  begin
    CreateArgs :=
      'create ' + ServiceName +
      ' binPath= "' + BinPath + '"' +
      ' start= auto DisplayName= "ShortP2P Messenger Server"';
    Exec(ExpandConstant('{sys}\sc.exe'), CreateArgs, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  { Written after sc create so we do not invent a service key before the SCM entry exists. }
  RegWriteMultiStringValue(HKEY_LOCAL_MACHINE,
    'SYSTEM\CurrentControlSet\Services\' + ServiceName,
    'Environment',
    'ASPNETCORE_ENVIRONMENT=Production');

  { Start is best-effort: the host does not yet call UseWindowsService(), so SCM may report 1053. }
  Exec(ExpandConstant('{sys}\sc.exe'), 'start ' + ServiceName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterService();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopExistingService();
end;
