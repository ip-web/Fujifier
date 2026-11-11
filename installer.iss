; Script generated for Inno Setup 6
; Fujifier Installer Script
#define MyAppName "Fujifier"
#define MyAppVersion "2.2.0"
#define MyAppPublisher "Isidore Paulin"
#define MyAppURL "https://fujifier.cc"
#define MyAppExeName "Fujifier.exe"

#ifndef MyAppSourceDir
#define MyAppSourceDir "publish\win-x64"
#endif

#ifndef MyOutputBaseFilename
#define MyOutputBaseFilename "Fujifier-v2.2.0-Setup"
#endif

#ifndef MyArchAllowed
#define MyArchAllowed "x64compatible"
#endif

[Setup]
AppId={{D37F8E42-68B1-4A2E-9C81-79A44BD8E431}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL=https://github.com/ip-web/Fujify/issues
AppUpdatesURL=https://github.com/ip-web/Fujify/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
LicenseFile=LICENSE
OutputDir=release
OutputBaseFilename={#MyOutputBaseFilename}
SetupIconFile=Fujify\Assets\images\logo.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed={#MyArchAllowed}
ArchitecturesInstallIn64BitMode={#MyArchAllowed}
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
ChangesAssociations=yes
UsedUserAreasWarning=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MyAppSourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Register Fujifier in Applications for Windows "Open with"
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueData: "Fujifier"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "Fujifier"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".arw"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cr2"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".cr3"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".crw"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dng"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nef"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".nrw"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".orf"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".raf"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".rw2"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".pef"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".srw"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".srf"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".sr2"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".3fr"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".erf"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".kdc"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dcs"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".dcr"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".iiq"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mos"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mef"; ValueData: ""; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".mrw"; ValueData: ""; Flags: uninsdeletekey

; ProgID
Root: HKA; Subkey: "Software\Classes\Fujifier.RAW"; ValueType: string; ValueData: "Camera RAW & DNG Image"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Fujifier.RAW"; ValueType: string; ValueName: "FriendlyTypeName"; ValueData: "Camera RAW & DNG Image"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Fujifier.RAW\DefaultIcon"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"",0"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Fujifier.RAW\shell\open\command"; ValueType: string; ValueData: """{app}\{#MyAppExeName}"" ""%1"""; Flags: uninsdeletekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-context-menu"; Flags: runhidden; RunOnceId: "UnregContextMenu"

[Code]
procedure CleanRegistryKeysForRoot(RootKey: Integer; SubPrefix: String);
var
  Exts: array[0..22] of String;
  I: Integer;
  BasePfx: String;
begin
  if SubPrefix <> '' then
    BasePfx := SubPrefix + '\'
  else
    BasePfx := '';

  Exts[0] := '.arw';  Exts[1] := '.cr2';  Exts[2] := '.cr3';  Exts[3] := '.crw';
  Exts[4] := '.dng';  Exts[5] := '.nef';  Exts[6] := '.nrw';  Exts[7] := '.orf';
  Exts[8] := '.raf';  Exts[9] := '.rw2';  Exts[10] := '.pef'; Exts[11] := '.srw';
  Exts[12] := '.srf'; Exts[13] := '.sr2'; Exts[14] := '.3fr'; Exts[15] := '.erf';
  Exts[16] := '.kdc'; Exts[17] := '.dcs'; Exts[18] := '.dcr'; Exts[19] := '.iiq';
  Exts[20] := '.mos'; Exts[21] := '.mef'; Exts[22] := '.mrw';

  // Delete centralized menu, wildcard verbs, and application registrations
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Fujifier.Menu');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Fujify.Menu');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\*\shell\Fujifier');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\*\shell\Fujify');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Applications\Fujifier.exe');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Applications\Fujify.exe');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Fujifier.RAW');
  RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\Fujify.RAW');

  // Delete per-extension context menus and OpenWith associations
  for I := 0 to 22 do
  begin
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\shell\Fujifier');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\shell\Fujify');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\shell\Fujifier');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\shell\Fujify');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\OpenWithList\Fujifier.exe');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\OpenWithList\Fujify.exe');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\OpenWithList\Fujifier.exe');
    RegDeleteKeyIncludingSubkeys(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\OpenWithList\Fujify.exe');
    RegDeleteValue(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\OpenWithProgids', 'Fujifier.RAW');
    RegDeleteValue(RootKey, BasePfx + 'Software\Classes\' + Exts[I] + '\OpenWithProgids', 'Fujify.RAW');
    RegDeleteValue(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\OpenWithProgids', 'Fujifier.RAW');
    RegDeleteValue(RootKey, BasePfx + 'Software\Classes\SystemFileAssociations\' + Exts[I] + '\OpenWithProgids', 'Fujify.RAW');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssInstall) or (CurStep = ssPostInstall) then
  begin
    // Clean up any stale or ghost keys in HKLM from previous versions that could conflict with per-user context menu
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\Classes\*\shell\Fujifier');
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\Classes\*\shell\Fujify');
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\Classes\Fujifier.Menu');
    RegDeleteKeyIncludingSubkeys(HKLM, 'Software\Classes\Fujify.Menu');
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  UserSIDs: TArrayOfString;
  I: Integer;
begin
  if (CurUninstallStep = usUninstall) or (CurUninstallStep = usPostUninstall) then
  begin
    // 1. Clean HKLM
    CleanRegistryKeysForRoot(HKLM, '');

    // 2. Clean HKCU
    CleanRegistryKeysForRoot(HKCU, '');

    // 3. Clean all active user profiles under HKEY_USERS
    if RegGetSubkeyNames(HKEY_USERS, '', UserSIDs) then
    begin
      for I := 0 to GetArrayLength(UserSIDs) - 1 do
      begin
        if (Pos('_Classes', UserSIDs[I]) = 0) and (UserSIDs[I] <> '.DEFAULT') then
        begin
          CleanRegistryKeysForRoot(HKEY_USERS, UserSIDs[I]);
        end;
      end;
    end;
  end;
end;
