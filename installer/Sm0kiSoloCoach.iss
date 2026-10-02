#define MyAppName "Sm0ki Solo Coach"
#define MyAppVersion "3.3.1"
#define MyAppExeName "Sm0kiSoloCoach.exe"

[Setup]
AppId={{1F8477C5-ED2F-4D5E-8E28-7C52C419C2C7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\Sm0kiSoloCoach
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputDir=..\dist
OutputBaseFilename=Sm0kiSoloCoach-Setup
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\dist\Sm0kiSoloCoach.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autodesktop}\Sm0ki Solo Coach"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Sm0ki Solo Coach"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Sm0ki Solo Coach"; Flags: nowait postinstall skipifsilent
