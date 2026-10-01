; Inno Setup script for PrintBridge.
; Build (from repo root, after dotnet publish):
;   iscc installer\PrintBridge.iss /DAppVersion=1.0.0 /DPublishDir=..\publish

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{4E1B9D7A-8C36-4F52-B0A9-6D3E5C7F81B4}
AppName=PrintBridge
AppVersion={#AppVersion}
AppPublisher=PrintBridge contributors
AppPublisherURL=https://github.com/gcgov/printbridge
DefaultDirName={localappdata}\Programs\PrintBridge
DisableProgramGroupPage=yes
; Per-user install: no admin rights required.
PrivilegesRequired=lowest
OutputBaseFilename=PrintBridge-{#AppVersion}-setup
SetupIconFile=..\src\PrintBridge\Resources\printbridge.ico
UninstallDisplayIcon={app}\PrintBridge.exe
Compression=lzma2
SolidCompression=yes
; Ask a running PrintBridge to close before replacing files.
CloseApplications=yes
WizardStyle=modern

[Tasks]
Name: "autostart"; Description: "Start PrintBridge when I sign in to Windows"; GroupDescription: "Startup:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{userprograms}\PrintBridge"; Filename: "{app}\PrintBridge.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "PrintBridge"; ValueData: """{app}\PrintBridge.exe"" --minimized"; \
  Flags: uninsdeletevalue; Tasks: autostart

[Run]
Filename: "{app}\PrintBridge.exe"; Description: "Launch PrintBridge now"; \
  Flags: nowait postinstall skipifsilent
; A silent install (Intune) closes a running PrintBridge before it replaces files.
; Start it again. On a first install, it opens the settings window, as an interactive install does.
Filename: "{app}\PrintBridge.exe"; Flags: nowait; Check: WizardSilent

[UninstallRun]
Filename: "taskkill"; Parameters: "/im PrintBridge.exe /f"; Flags: runhidden; RunOnceId: "KillPrintBridge"
