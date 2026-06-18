; Inno Setup script for Snipper.
; Build with:  ISCC.exe packaging\Snipper.iss
; (or run packaging\build-installer.ps1, which publishes first then compiles this)
;
; Expects a self-contained publish in ..\publish (see build-installer.ps1).

#define MyAppName "Snipper"
#define MyAppVersion "0.1.1"
#define MyAppPublisher "Jeff Aigner"
#define MyAppExeName "Snipper.exe"
#define MyPublishDir "..\publish"

[Setup]
; Keep this AppId STABLE across releases so upgrades/uninstall work.
AppId={{B7E5B2B1-9C4A-4F3E-8E21-6D4A7C1F9A20}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install -> no admin / UAC prompt (good for an unsigned app).
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\
OutputBaseFilename=Snipper-{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The whole self-contained publish folder (Snipper.exe, .NET runtime, ffmpeg\).
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent
