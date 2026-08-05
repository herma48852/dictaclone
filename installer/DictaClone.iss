#ifndef MyAppVersion
  #error MyAppVersion must be supplied by Build-Installer.ps1
#endif
#ifndef MyVersionInfoVersion
  #error MyVersionInfoVersion must be supplied by Build-Installer.ps1
#endif
#ifndef MySourceDir
  #error MySourceDir must be supplied by Build-Installer.ps1
#endif
#ifndef MyOutputDir
  #error MyOutputDir must be supplied by Build-Installer.ps1
#endif
#ifndef MyOutputBaseFilename
  #error MyOutputBaseFilename must be supplied by Build-Installer.ps1
#endif

#define MyAppName "DictaClone"
#define MyAppExeName "DictaClone.App.exe"
#define MyAppPublisher "DictaClone contributors"
#define MyAppUrl "https://github.com/herma48852/dictaclone"

[Setup]
AppId={{A4A195C4-2270-4F25-9F56-4FA6F4721809}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppCopyright=Copyright (C) 2026 DictaClone contributors
DefaultDirName={localappdata}\Programs\DictaClone
DefaultGroupName=DictaClone
DisableProgramGroupPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
AppMutex=DictaClone.Desktop
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
OutputDir={#MyOutputDir}
OutputBaseFilename={#MyOutputBaseFilename}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} per-user installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoVersion={#MyVersionInfoVersion}

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DictaClone"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\Uninstall DictaClone"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "DictaClone"; Flags: dontcreatekey uninsdeletevalue

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch DictaClone"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  PurgeUserData: Boolean;

function HasCommandLineParameter(const Expected: String): Boolean;
var
  Index: Integer;
begin
  Result := False;
  for Index := 1 to ParamCount do
  begin
    if CompareText(ParamStr(Index), Expected) = 0 then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeUninstall: Boolean;
begin
  PurgeUserData := HasCommandLineParameter('/PURGEUSERDATA');
  if (not PurgeUserData) and (not UninstallSilent) then
  begin
    PurgeUserData := SuppressibleMsgBox(
      'Delete DictaClone settings, downloaded speech models, transcript history, and diagnostics for this Windows user?' + #13#10 + #13#10 +
      'Choose No to keep this data for a reinstall or rollback.',
      mbConfirmation,
      MB_YESNO or MB_DEFBUTTON2,
      IDNO) = IDYES;
  end;

  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(
      HKEY_CURRENT_USER,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'DictaClone');

    if PurgeUserData then
      DelTree(ExpandConstant('{localappdata}\DictaClone'), True, True, True);
  end;
end;
