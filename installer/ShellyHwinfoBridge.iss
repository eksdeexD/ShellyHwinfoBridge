; Shelly HWiNFO Bridge installer (Inno Setup 6.3 or newer).
;
; Build (from the repository root, after the self-contained `dotnet publish ... -o publish`; the exe carries the .NET
; runtime, so the installer needs no runtime check):
;   ISCC.exe /DAppVersion=1.0.0 installer\ShellyHwinfoBridge.iss
; or use installer\Build-Installer.ps1, which the release and CI workflows call.
;
; Defines that can be passed with /D:
;   AppVersion  the product version without a leading "v" (the release workflow passes the tag, which must match
;               <Version> in Directory.Build.props). Without it the script builds a "0.0.0-dev" installer.
;   PublishDir  folder that holds the published ShellyHwinfoBridge.exe, psu-database.json and PSU-DATA-NOTICE.md
;               (default ..\publish).
;   OutputDir   folder the setup.exe is written to (default ..\artifacts\installer).
;
; Silent install / uninstall (used by winget): /VERYSILENT /NORESTART. A silent uninstall answers No to both
; "remove ..." questions, so it never deletes sensors, settings or data.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

#define AppName "Shelly HWiNFO Bridge"
#define AppExeName "ShellyHwinfoBridge.exe"
#define AppPublisher "Zapador"
#define AppUrl "https://github.com/eksdeexD/ShellyHwinfoBridge"

; VersionInfoVersion only takes numbers: drop a pre-release suffix such as "-beta.6".
#define DashPos Pos("-", AppVersion)
#if DashPos > 0
  #define NumericVersion Copy(AppVersion, 1, DashPos - 1)
#else
  #define NumericVersion AppVersion
#endif

[Setup]
; Never change the AppId: it is how a newer installer finds and upgrades an existing install, and winget's
; ProductCode is "{AppId}_is1". The first brace is doubled because Inno treats "{" as the start of a constant.
AppId={{DEF17674-1535-48EB-A2A8-299718E399EB}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
AppCopyright=Copyright (c) 2026 {#AppPublisher}
VersionInfoVersion={#NumericVersion}
VersionInfoTextVersion={#AppVersion}
VersionInfoProductName={#AppName}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
SetupIconFile=..\src\ShellyHwinfoBridge\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
LicenseFile=..\LICENSE
WizardStyle=modern
Compression=lzma2
SolidCompression=yes
OutputDir={#OutputDir}
; Same name as the release asset: ShellyHwinfoBridge-<tag>-setup.exe, <tag> = "v" + AppVersion (like the zip).
OutputBaseFilename=ShellyHwinfoBridge-v{#AppVersion}-setup
; Only one setup at a time. The [Code] section asks the running app to exit first; the Restart Manager
; (CloseApplications) is the fallback for anything that still holds the files. Never restart the app from the
; elevated installer: it must run as the user (HKCU sensors, per-user settings).
SetupMutex=ShellyHwinfoBridgeSetup,Global\ShellyHwinfoBridgeSetup
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\psu-database.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\PSU-DATA-NOTICE.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
; runasoriginaluser: start the tray app as the user who ran setup, never elevated.
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
const
  AppProcessName = '{#AppExeName}';
  { Must match AppPaths.ExitEventName and AppPaths.SingleInstanceMutexName in ShellyHwinfoBridge.Core. }
  AppExitEventName = 'Local\ShellyHwinfoBridge.Exit';
  AppMutexName = 'Local\ShellyHwinfoBridge.SingleInstance';
  SensorsKey = 'Software\HWiNFO64\Sensors\Custom\Shelly Power';
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
  RunValueName = 'ShellyHwinfoBridge';
  EVENT_MODIFY_STATE = $0002;

function OpenEvent(dwDesiredAccess: DWORD; bInheritHandle: BOOL; lpName: string): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(hEvent: THandle): BOOL;
  external 'SetEvent@kernel32.dll stdcall';
function CloseHandle(hObject: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';

{ True while any ShellyHwinfoBridge.exe process runs (any user/session; WMI), or, if WMI is unavailable, while
  the app's single-instance mutex exists in this session. }
function IsAppRunning: Boolean;
var
  Locator, Service, Processes: Variant;
begin
  try
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('.', 'root\CIMV2');
    Processes := Service.ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''' + AppProcessName + '''');
    Result := Processes.Count > 0;
  except
    Result := CheckForMutexes(AppMutexName);
  end;
end;

{ Polls IsAppRunning for up to TimeoutMs; True once the app is gone. }
function WaitForAppExit(TimeoutMs: Integer): Boolean;
var
  Waited: Integer;
begin
  Waited := 0;
  Result := not IsAppRunning;
  while (not Result) and (Waited < TimeoutMs) do
  begin
    Sleep(250);
    Waited := Waited + 250;
    Result := not IsAppRunning;
  end;
end;

{ Sets the app's exit event, the same signal "ShellyHwinfoBridge.exe --exit" sends. The running app then runs
  its normal clean exit (sensors offline, energy log flushed). Versions before 1.0 have no exit event. }
function SignalAppExit: Boolean;
var
  Handle: THandle;
begin
  Result := False;
  Handle := OpenEvent(EVENT_MODIFY_STATE, False, AppExitEventName);
  if Handle <> 0 then
  begin
    Result := SetEvent(Handle);
    CloseHandle(Handle);
  end;
end;

procedure RunTaskkill(const Params: string);
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

{ Stops a running app before files are replaced or removed: the app's own exit signal first, then a polite
  taskkill (WM_CLOSE), then a forced one. Anything left over is handled by CloseApplications. }
procedure StopRunningApp;
begin
  if not IsAppRunning then
    exit;

  Log('Shelly HWiNFO Bridge is running; asking it to exit.');
  if SignalAppExit and WaitForAppExit(10000) then
    exit;

  Log('The app did not exit on its exit signal; trying taskkill.');
  RunTaskkill('/IM ' + AppProcessName);
  if WaitForAppExit(5000) then
    exit;

  Log('The app is still running; forcing it to close.');
  RunTaskkill('/F /IM ' + AppProcessName);
  WaitForAppExit(5000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunningApp;
  Result := '';
end;

{ A Yes/No question whose default (and silent-mode answer) is No. }
function AskDefaultNo(const Question: string): Boolean;
begin
  Result := SuppressibleMsgBox(Question, mbConfirmation, MB_YESNO or MB_DEFBUTTON2, IDNO) = IDYES;
end;

// User-context note: the uninstaller runs elevated (PrivilegesRequired=admin), so HKCU and {userappdata} below are
// the hive and profile of the account the uninstaller is elevated as. For the usual case (an administrator who
// confirms the UAC prompt) that is the user who uses the app. When a standard user elevates with another
// administrator's credentials ("over the shoulder"), they point at that administrator instead and the user's own
// sensors and settings stay. ExecAsOriginalUser cannot be used during uninstall, so there is no reliable way to
// reach the original user from here; such a user removes the sensors from the app first (Settings > Maintenance
// > "Remove HWiNFO sensors", or "ShellyHwinfoBridge.exe --remove-sensors") and deletes %APPDATA%\ShellyHwinfoBridge
// by hand. Other users of the PC keep their own sensors and settings in any case.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usUninstall then
    exit;

  { Stop the app before anything is removed; its clean exit writes the sensors one last time. }
  StopRunningApp;

  if AskDefaultNo('Remove the Shelly HWiNFO Bridge sensors from HWiNFO?' + #13#10#13#10 +
    'This deletes the "Shelly Power" sensor group (HKCU\' + SensorsKey + ') for the current user. ' +
    'Choose No to keep them, for example when you plan to reinstall.') then
  begin
    if RegKeyExists(HKCU, SensorsKey) then
    begin
      if RegDeleteKeyIncludingSubkeys(HKCU, SensorsKey) then
        Log('Removed HKCU\' + SensorsKey + '.')
      else
        Log('Could not remove HKCU\' + SensorsKey + '.');
    end;
  end;

  if AskDefaultNo('Remove the Shelly HWiNFO Bridge settings and data?' + #13#10#13#10 +
    'This deletes ' + ExpandConstant('{userappdata}\ShellyHwinfoBridge') +
    ' (settings, energy log and logs) and the "Start with Windows" entry for the current user. ' +
    'Choose No to keep them for a later reinstall.') then
  begin
    if DirExists(ExpandConstant('{userappdata}\ShellyHwinfoBridge')) then
    begin
      if DelTree(ExpandConstant('{userappdata}\ShellyHwinfoBridge'), True, True, True) then
        Log('Removed the settings and data folder.')
      else
        Log('Could not remove the settings and data folder completely.');
    end;
    if RegValueExists(HKCU, RunKey, RunValueName) then
      RegDeleteValue(HKCU, RunKey, RunValueName);
  end;
end;
