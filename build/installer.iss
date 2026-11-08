#define MyAppName "VRCLearn Launcher"
#define MyAppExeName "VRCLearnLauncher.exe"
#define MyAppMutex "VRCLearnLauncher.SingleInstance"

#ifndef MyAppVersion
  #error MyAppVersion must be defined
#endif

[Setup]
AppId={{C3B4BABC-4F9B-4281-A58B-02AA08BC61B7}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisherURL=https://start.gamemode1.com/pages/vrclearnlauncher/
AppSupportURL=https://start.gamemode1.com/pages/vrclearnlauncher/
DefaultDirName={localappdata}\Programs\VRCLearnLauncher
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts\releases
OutputBaseFilename=VRCLearnLauncher-{#MyAppVersion}-win-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.CloseLauncher=VRCLearn Launcher is running. Close it before continuing.
chinesesimplified.CloseLauncher=VRCLearn Launcher 正在运行，请先关闭启动器再继续。

english.ConfirmCloseLauncher=VRCLearn Launcher is running. Close it and continue?
chinesesimplified.ConfirmCloseLauncher=VRCLearn Launcher 正在运行。要关闭启动器并继续吗？
english.CloseLauncherFailed=The launcher did not close. Close it manually before continuing.
chinesesimplified.CloseLauncherFailed=未能自动关闭启动器，请手动关闭后再继续。

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[InstallDelete]
Type: filesandordirs; Name: "{app}\Plugins"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]
const
  ProcessQueryLimitedInformation = $1000;
  ProcessSynchronize = $00100000;
  CloseWindowMessage = $0010;
  ProcessExited = 0;
  LauncherExitTimeoutMs = 5000;

function GetWindowProcessId(Window: HWND; var ProcessId: DWORD): DWORD;
  external 'GetWindowThreadProcessId@user32.dll stdcall';
function OpenLauncherProcess(Access: DWORD; InheritHandle: BOOL; ProcessId: DWORD): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function ReadProcessImagePath(Process: THandle; Flags: DWORD; ImagePath: String; var PathLength: DWORD): BOOL;
  external 'QueryFullProcessImageNameW@kernel32.dll stdcall';
function WaitForLauncherExit(Process: THandle; Timeout: DWORD): DWORD;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseProcessHandle(Process: THandle): BOOL;
  external 'CloseHandle@kernel32.dll stdcall';
function LongPathName(Path: String; LongPath: String; BufferLength: DWORD): DWORD;
  external 'GetLongPathNameW@kernel32.dll stdcall';

function QueryLongPath(Path: String): String;
var
  Size: DWORD;
begin
  Size := 32768;
  SetLength(Result, Size);
  Size := LongPathName(Path, Result, Size);
  if Size = 0 then
    Result := Path
  else
    SetLength(Result, Size);
end;

function RequestLauncherClose(): Boolean;
var
  Window: HWND;
  ProcessId, CurrentProcessId, PathLength: DWORD;
  Process: THandle;
  ImagePath: String;
begin
  Result := Length(CheckForMutexes('{#MyAppMutex}')) = 0;
  if Result then
    Exit;

  Window := FindWindowByWindowName('{#MyAppName}');
  if (Window = 0) or (GetWindowProcessId(Window, ProcessId) = 0) then
    Exit;

  Process := OpenLauncherProcess(ProcessQueryLimitedInformation or ProcessSynchronize, False, ProcessId);
  if Process = 0 then
    Exit;

  try
    PathLength := 32768;
    SetLength(ImagePath, PathLength);
    if not ReadProcessImagePath(Process, 0, ImagePath, PathLength) then
      Exit;
    SetLength(ImagePath, PathLength);

    { Verify the window belongs to the executable this installer will replace. }
    if CompareText(QueryLongPath(ImagePath), QueryLongPath(ExpandConstant('{app}\{#MyAppExeName}'))) <> 0 then
      Exit;
    if (GetWindowProcessId(Window, CurrentProcessId) = 0) or (CurrentProcessId <> ProcessId) then
      Exit;

    Log('Requesting graceful shutdown of VRCLearn Launcher.');
    if PostMessage(Window, CloseWindowMessage, 0, 0) then
      Result := WaitForLauncherExit(Process, LauncherExitTimeoutMs) = ProcessExited;
  finally
    CloseProcessHandle(Process);
  end;
end;

function ConfirmLauncherClose(): Boolean;
begin
  Result := Length(CheckForMutexes('{#MyAppMutex}')) = 0;
  if Result then
    Exit;

  if SuppressibleMsgBox(CustomMessage('ConfirmCloseLauncher'), mbConfirmation, MB_OKCANCEL, IDCANCEL) <> IDOK then
    Exit;

  Result := RequestLauncherClose() or (Length(CheckForMutexes('{#MyAppMutex}')) = 0);

  if not Result then
  begin
    Log('VRCLearn Launcher could not be closed automatically.');
    SuppressibleMsgBox(CustomMessage('CloseLauncherFailed'), mbError, MB_OK, IDOK);
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not WizardSilent then
    Result := ConfirmLauncherClose();
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if Length(CheckForMutexes('{#MyAppMutex}')) > 0 then
  begin
    Log('VRCLearn Launcher is still running.');
    Result := CustomMessage('CloseLauncher');
  end;
end;

function InitializeUninstall(): Boolean;
begin
  if UninstallSilent then
    Result := Length(CheckForMutexes('{#MyAppMutex}')) = 0
  else
    Result := ConfirmLauncherClose();
end;
