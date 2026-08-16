; AC Mod Hub 2.0 — per-user Inno Setup installer.
; Version is single-sourced: build.ps1 / CI generate installer/version.iss from
; Directory.Build.props before compiling. Never edit the version here.
#define MyAppName "AC Mod Hub"
#define MyAppPublisher "AC Mod Hub"
#define MyAppExeName "ACModHub.exe"
#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif
#if FileExists("version.iss")
  #include "version.iss"
#endif
#ifndef MyAppVersion
  ; Fallback only when version.iss was not generated (manual compile).
  #define MyAppVersion GetFileVersion(AddBackslash(PublishDir) + "ACModHub.exe")
#endif

[Setup]
AppId={{8F3E6485-402B-4F4F-BBB0-52733095AC64}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\ACModHub
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Per-user install: no administrator rights, no silent elevation, no admin dialog.
PrivilegesRequired=lowest
OutputDir=..\artifacts\installer
OutputBaseFilename=AC-Mod-Hub-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
SetupLogging=yes
; In-place updates: wait for the running app (AppMutex) before replacing files.
AppMutex=ACModHub.SingleInstance
CloseApplications=yes
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Assetto Corsa launcher and mod manager
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Run]
; Normal interactive install: offer to launch. Silent update: relaunch only when the app
; requested a post-update restart (pending-update marker resolved by UpdateRequested()).
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait skipifnotsilent; Check: UpdateRequested

[Code]
const
  PendingMarker = 'pending-update.json';

function UpdateRequested(): Boolean;
var
  MarkerPath: String;
begin
  Result := False;
  MarkerPath := ExpandConstant('{userappdata}\ACModHub\' + PendingMarker);
  Result := FileExists(MarkerPath);
end;

function WebView2Present(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
         or RegKeyExists(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not WebView2Present() then
    begin
      // Never silently install or elevate: just inform the user with the official link.
      MsgBox('The store needs the Microsoft WebView2 Runtime. The app will show a simplified store until it is installed.' + #13#10 +
             'Download it from: https://developer.microsoft.com/microsoft-edge/webview2/', mbInformation, MB_OK);
    end;
  end;
end;
