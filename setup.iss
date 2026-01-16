; ==============================================================================
; Power Overlay Installer Setup Script (Inno Setup)
; Creates installer with autostart and proper uninstall support.
; ==============================================================================
;
; [CHANGE LOG]
; 2026-01-11 - AI - Created initial installer script
; ==============================================================================
;
; BUILD INSTRUCTIONS:
; 1. First build the app: dotnet publish -c Release
; 2. Open this file in Inno Setup Compiler
; 3. Press Ctrl+F9 or click "Compile" to build the installer
; 4. Output: publish\PowerOverlaySetup.exe
; ==============================================================================

#define MyAppName "Power Switch Overlay"
#define MyAppVersion "1.2"
#define MyAppPublisher "Replicrafts"
#define MyAppURL "https://ko-fi.com/replicrafts"
#define MyAppExeName "PowerSwitchOverlay.exe"
; Path to the published executable (relative to this .iss file)
#define MyAppSource "publish\PowerSwitchOverlay.exe"

[Setup]
; NOTE: AppId uniquely identifies this app - don't change after first release
AppId={{8F7E4D3C-2B1A-4E5F-9C8D-1A2B3C4D5E6F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; Don't allow changing install directory (cleaner experience)
DisableDirPage=yes
DisableProgramGroupPage=yes
; Output file location and name
OutputDir=publish
OutputBaseFilename=PowerSwitchOverlaySetup
; Use the app icon for installer
SetupIconFile=app.ico
; Compression settings
Compression=lzma2/ultra64
SolidCompression=yes
; Modern installer look
WizardStyle=modern
; Uninstaller settings
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
; No admin required - installs to user's Program Files
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
; Optional: let user choose whether to start with Windows
Name: "autostart"; Description: "Start Power Overlay when Windows starts"; Flags: checkedonce
Name: "desktopicon"; Description: "Create a &desktop shortcut"; Flags: unchecked

[Files]
; Main executable
Source: "{#MyAppSource}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
; Desktop shortcut (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Autostart registry entry - only if user selected the task
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueName: "PowerSwitchOverlay"; ValueType: string; \
    ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: autostart

[Run]
; Launch app after install (optional checkbox)
Filename: "{app}\{#MyAppExeName}"; \
    Description: "Launch {#MyAppName}"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Close the app before uninstalling (gracefully)
Filename: "taskkill"; Parameters: "/F /IM {#MyAppExeName}"; \
    Flags: runhidden; RunOnceId: "CloseApp"

[Code]
// Close app if running during install/uninstall
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Try to close app gracefully before install
  Exec('taskkill', '/F /IM ' + '{#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;
