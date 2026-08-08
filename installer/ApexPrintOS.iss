; ============================================================================
;  Apex Print OS — Field-Trial Installer (Inno Setup 6)
;
;  Build with:   build-installer.ps1   (publishes the app, then compiles this)
;  Or manually:  ISCC.exe /DMyAppVersion=2.2.0 /DAppSrc="<published-app-folder>" installer\ApexPrintOS.iss
;
;  Produces a per-user setup that:
;    • installs without requiring admin/UAC (PrivilegesRequired=lowest)
;    • registers an uninstaller shown in Windows "Apps & features" / Control Panel
;    • shows the Arabic End-User License Agreement (Accept/Decline gate)
; ============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "2.2.0"
#endif

; Folder that holds the published application (ApexPrintOS.exe + dependencies).
; Overridden by build-installer.ps1 via /DAppSrc=...
#ifndef AppSrc
  #define AppSrc "stage\app"
#endif

#define MyAppName "Apex Print OS"
#define MyAppPublisher "Apex Printing Press"
#define MyAppURL "https://apexprint.me"
#define MyAppExeName "ApexPrintOS.exe"

[Setup]
; Unique application identity — keep this GUID stable across versions so upgrades
; replace the previous install and Control-Panel keeps a single entry.
AppId={{A1B2C3D4-E5F6-7890-1234-567890ABCDEF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
VersionInfoVersion={#MyAppVersion}

; Per-user install → no admin prompt on field machines. Still creates an
; uninstall entry (HKCU) visible under Settings ▸ Apps and Control Panel.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto

; ── End-User License Agreement (shown as an Accept/Decline page) ──
LicenseFile=LICENSE-ar.txt

; ── Uninstall presentation in Control Panel / Apps & features ──
UninstallDisplayName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}

SetupIconFile=..\Apex.PrintingSystem\Apex.UI\Assets\AppIcon.ico
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
ArchitecturesAllowed=x64compatible

OutputDir=output
OutputBaseFilename=ApexPrintOS-Setup-{#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#AppSrc}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppSrc}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Ship the license alongside the app for reference.
Source: "LICENSE-ar.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
