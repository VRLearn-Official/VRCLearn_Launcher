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
function WaitForLauncherToClose(Silent: Boolean): Boolean;
begin
  Result := False;
  while CheckForMutexes('{#MyAppMutex}') do
  begin
    Log('VRCLearn Launcher is still running.');
    if Silent then
      Exit;
    if SuppressibleMsgBox(CustomMessage('CloseLauncher'), mbInformation, MB_OKCANCEL, IDCANCEL) <> IDOK then
      Exit;
  end;
  Result := True;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and not WizardSilent then
    Result := WaitForLauncherToClose(False);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not WaitForLauncherToClose(True) then
    Result := CustomMessage('CloseLauncher');
end;

function InitializeUninstall(): Boolean;
begin
  Result := WaitForLauncherToClose(UninstallSilent);
end;
