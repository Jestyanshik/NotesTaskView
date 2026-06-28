#define MyAppName "NotesTaskView"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "NotesTaskView contributors"
#define MyAppExeName "NotesTaskView.exe"

[Setup]
AppId={{A4F60D20-56E4-4E12-A9C3-1D99B4F9B732}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\NotesTaskView
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\installer-output
OutputBaseFilename=NotesTaskViewSetup
SetupIconFile=..\Assets\app-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Files]
Source: "..\publish\win-x64-self-contained\NotesTaskView.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\publish\win-x64-self-contained\appsettings.json"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "..\Assets\app-icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "autostart"; Description: "Запускать NotesTaskView вместе с Windows"; GroupDescription: "Автозагрузка:"; Flags: checkedonce

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app-icon.ico"
Name: "{userstartup}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\app-icon.ico"; Tasks: autostart

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: files; Name: "{userstartup}\{#MyAppName}.lnk"
