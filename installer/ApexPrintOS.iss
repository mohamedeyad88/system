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
  #define MyAppVersion "2.9.0"
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

; Always install to the canonical location above. Earlier builds used a different
; folder/name (localappdata\Apex Printing System, exe Apex.UI.exe); without this,
; an upgrade could be dragged back into the old folder. The old install is removed
; by the [Code] below, not left beside the new one.
UsePreviousAppDir=no

; If a copy is running, tell the user to close it so files can be replaced instead
; of a silent half-upgrade. Must match the mutex the app creates at startup.
AppMutex=Local\ApexPrintOS_SingleInstance

; ── End-User License Agreement (shown as an Accept/Decline page) ──
LicenseFile=LICENSE-ar.txt
; ── What changed — shown before installing, because an upgrade can move numbers on paper ──
InfoBeforeFile=RELEASE-NOTES-ar.txt

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
Source: "RELEASE-NOTES-ar.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Remove leftovers from the pre-"Apex Print OS" era (different folder, name and exe),
; in case its own uninstaller is gone. New files go to DefaultDirName above.
Type: filesandordirs; Name: "{localappdata}\Apex Printing System"
Type: files; Name: "{autoprograms}\Apex Printing System.lnk"
Type: files; Name: "{autodesktop}\Apex Printing System.lnk"
; An older same-AppId exe name, if a prior build left it inside the new folder.
Type: files; Name: "{app}\Apex.UI.exe"

[Code]
{ Run any previously-installed version's uninstaller BEFORE installing, so the old
  copy is removed instead of sitting beside the new one. Every past installer shares
  this AppId, so Inno registered its uninstaller under "<AppId>_is1". }
const
  PrevUninstKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A1B2C3D4-E5F6-7890-1234-567890ABCDEF}_is1';

function ReadPrevUninstaller(RootKey: Integer; var ExePath: String): Boolean;
var
  raw: String;
begin
  Result := False;
  if RegQueryStringValue(RootKey, PrevUninstKey, 'UninstallString', raw) then
  begin
    { strip the surrounding quotes to get the bare exe path }
    if (Length(raw) >= 2) and (raw[1] = '"') then
      raw := Copy(raw, 2, Pos('"', Copy(raw, 2, Length(raw))) - 1);
    ExePath := raw;
    Result := FileExists(ExePath);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  exe: String;
  rc: Integer;
begin
  Result := '';
  if ReadPrevUninstaller(HKCU, exe) or ReadPrevUninstaller(HKLM, exe) then
    Exec(exe, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '',
         SW_HIDE, ewWaitUntilTerminated, rc);
end;
