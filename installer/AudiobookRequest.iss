; Inno Setup script for Audiobook Request.
; Prereq: run scripts\publish.ps1 first to produce ..\publish with the self-contained exe,
; the built React client baked into wwwroot, and ffmpeg/ffprobe under tools\.

#define MyAppName "Audiobook Request"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "Audiobook Request"
#define MyAppExeName "AudiobookRequest.Api.exe"
#define MyServiceName "AudiobookRequest"
#define MyAppPort "5230"

[Setup]
AppId={{EE90DEE5-194B-43D6-BE86-639247D6C0B4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={commonappdata}\AudiobookRequest
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputBaseFilename=AudiobookRequestSetup
OutputDir=Output
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "AudiobookRequest.url"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\AudiobookRequest.url"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\AudiobookRequest.url"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Run]
; Register and start the Windows Service. LocalSystem (the sc.exe default) has full access to
; ProgramData, which is where this whole install lives, so no explicit account is configured.
Filename: "{sys}\sc.exe"; Parameters: "create ""{#MyServiceName}"" binPath= ""{app}\{#MyAppExeName}"" start= auto DisplayName= ""{#MyAppName}"""; Flags: runhidden; StatusMsg: "Registering the Windows Service..."
Filename: "{sys}\sc.exe"; Parameters: "description ""{#MyServiceName}"" ""Audiobook request/download automation service."""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""Audiobook Request"" dir=in action=allow protocol=TCP localport={#MyAppPort}"; Flags: runhidden; StatusMsg: "Opening firewall port {#MyAppPort}..."
Filename: "{sys}\sc.exe"; Parameters: "start ""{#MyServiceName}"""; Flags: runhidden; StatusMsg: "Starting the service..."
Filename: "{app}\AudiobookRequest.url"; Description: "Open Audiobook Request in your browser"; Flags: postinstall shellexec skipifsilent

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop ""{#MyServiceName}"""; Flags: runhidden; RunOnceId: "StopService"
Filename: "{sys}\sc.exe"; Parameters: "delete ""{#MyServiceName}"""; Flags: runhidden; RunOnceId: "DeleteService"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""Audiobook Request"""; Flags: runhidden; RunOnceId: "DeleteFirewallRule"

[UninstallDelete]
; Data (SQLite DB, logs, data-protection keys) is left behind on uninstall by default so the
; user doesn't lose their library/request history; they can delete {app}\data manually if wanted.
Type: filesandordirs; Name: "{app}\wwwroot"
