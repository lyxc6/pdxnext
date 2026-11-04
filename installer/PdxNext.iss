; PdxNext installer (Inno Setup 7.1.1+).
; Build with tools/build-installer.py, which passes Version, FileVersion,
; PayloadDir and OutputDir. The script can also be compiled directly:
;   ISCC.exe installer\PdxNext.iss /DVersion=1.0.0 /DFileVersion=1.0.0.0 ^
;     /DPayloadDir=dist\PdxNext /DOutputDir=dist\installer

#ifndef Version
  #define Version "1.0.0"
#endif

#ifndef FileVersion
  #define FileVersion "1.0.0.0"
#endif

#ifndef PayloadDir
  #define PayloadDir "..\dist\PdxNext"
#endif

#ifndef OutputDir
  #define OutputDir "..\dist\installer"
#endif

#define AppName "PdxNext"
#define AppPublisher "lyxc6"
#define AppUrl "https://github.com/lyxc6/pdxnext"
#define ReleasesUrl "https://github.com/lyxc6/pdxnext/releases"

[Setup]
AppId={{A2E3D5E1-5133-46BA-A846-1388A878E1A7}
AppName={#AppName}
AppVersion={#Version}
AppVerName={#AppName} {#Version}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}
AppUpdatesURL={#ReleasesUrl}
VersionInfoVersion={#FileVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} installer
VersionInfoProductName={#AppName}
VersionInfoProductVersion={#Version}
DefaultDirName={autopf}\PdxNext
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableWelcomePage=no
; Inno 的默认值是 auto：一旦检测到旧安装目录（UsePreviousAppDir 命中的卸载项），
; 「选择目标位置」整页会被隐藏，用户再也没法改安装位置。这里显式关掉该隐藏规则，
; 让交互式安装永远能看到并修改目录；升级时 UsePreviousAppDir 仍会把旧目录预填进去。
; 静默安装（启动器的自动更新走 /SILENT）本来就不显示任何页面，不受本项影响。
DisableDirPage=no
AllowNoIcons=yes
LicenseFile=..\LICENSE
OutputDir={#OutputDir}
OutputBaseFilename=PdxNext-Setup-{#Version}
SetupIconFile=..\src\PdxLauncher.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\PdxLauncher.App.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupArchitecture=x64
AppMutex=Local\PdxNext.App
SetupMutex=Local\PdxNext.Setup
CloseApplications=yes
; 应用更新前会先退出，安装完由 [Run] 重新启动；这里不启用 Restart Manager 重启，
; 否则“安装时程序还开着”的情况可能与 [Run] 各启动一次，出现双开。
RestartApplications=no
UsePreviousAppDir=yes
LanguageDetectionMethod=uilanguage

[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
zhcn.OtherModeInstalled=检测到另一种安装模式的 PdxNext。请先卸载旧版本（数据会保留），再安装当前模式。
en.OtherModeInstalled=Another PdxNext installation mode was detected. Uninstall the old version first (your data is kept), then install this mode.
zhcn.BadInstallDir=不能安装到这个位置。请选择盘符根、Windows 目录、用户目录根、Program Files 根以外的文件夹，例如 D:\Apps\PdxNext。
en.BadInstallDir=This location cannot be used. Choose a folder other than a drive root, the Windows folder, your user profile root or the Program Files root, for example D:\Apps\PdxNext.
zhcn.InstallDirNotWritable=当前用户没有写入该文件夹的权限。请换一个文件夹，或在安装开始时选择「为所有用户安装」（需要管理员授权）。
en.InstallDirNotWritable=The current user cannot write to this folder. Choose another folder, or select "Install for all users" at the start of Setup (requires administrator approval).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; data, data-root.txt, logs and the update cache are runtime state and must survive upgrades.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "data\*,data-root.txt,*.log,update-cache\*,install-mode.txt"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\PdxLauncher.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\PdxLauncher.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Silent update: relaunch after the installer finishes. In an elevated install
; this must run as the original (non-elevated) user so the launcher does not
; inherit an administrator token.
Filename: "{app}\PdxLauncher.App.exe"; Flags: nowait postinstall skipifsilent runasoriginaluser
Filename: "{app}\PdxLauncher.App.exe"; Flags: nowait skipifnotsilent runasoriginaluser

[UninstallDelete]
Type: files; Name: "{app}\install-mode.txt"

[Code]
const
  AppExeName = 'PdxLauncher.App.exe';
  { Inno 的卸载项：AppId 去掉一层转义的大括号，再拼 _is1。 }
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{A2E3D5E1-5133-46BA-A846-1388A878E1A7}_is1';

{ 另一种安装模式是否已经装过。先读它写在卸载项里的安装目录（用户自选目录也覆盖得到），
  读不到再退回默认路径探测；旧版本可能把程序装在别处，这里只用于阻止双模式并存。 }
function OtherModeInstalled: Boolean;
var
  OtherRoot: Integer;
  AppPath: String;
begin
  if IsAdminInstallMode then
    OtherRoot := HKEY_CURRENT_USER
  else
    OtherRoot := HKEY_LOCAL_MACHINE;

  if RegQueryStringValue(OtherRoot, UninstallKey, 'Inno Setup: App Path', AppPath) and (AppPath <> '') then
  begin
    Result := FileExists(AddBackslash(AppPath) + AppExeName);
    if Result then
      Exit;
  end;

  if IsAdminInstallMode then
    Result := FileExists(ExpandConstant('{localappdata}\Programs\PdxNext\' + AppExeName))
  else
    Result := FileExists(ExpandConstant('{commonpf}\PdxNext\' + AppExeName));
end;

{ 去掉尾部反斜杠并统一小写，便于做路径比较。 }
function NormalizeDir(Dir: String): String;
begin
  Result := Lowercase(RemoveBackslashUnlessRoot(Dir));
end;

{ 明显不能装的位置：盘符根、Windows 目录、用户目录根、Program Files 根、LocalAppData 根。 }
function IsForbiddenDir(Dir: String): Boolean;
var
  Normalized, DrivePart, SystemRoot: String;
begin
  Result := True;
  Normalized := NormalizeDir(Dir);
  if Normalized = '' then
    Exit;

  { 盘符根 / 共享根：C:、C:\、\\server\share、\\server\share\ 都不能直接装。 }
  DrivePart := NormalizeDir(ExtractFileDrive(Normalized));
  if (Normalized = DrivePart) or (Normalized = NormalizeDir(DrivePart + '\')) then
    Exit;

  SystemRoot := NormalizeDir(ExpandConstant('{win}'));
  if (Normalized = SystemRoot) or (Pos(SystemRoot + '\', Normalized) = 1) then
    Exit;

  Result := (Normalized = NormalizeDir(ExpandConstant('{%USERPROFILE}')))
    or (Normalized = NormalizeDir(ExpandConstant('{commonpf}')))
    or (Normalized = NormalizeDir(ExpandConstant('{commonpf32}')))
    or (Normalized = NormalizeDir(ExpandConstant('{localappdata}')))
    or (Normalized = NormalizeDir(ExpandConstant('{localappdata}\Programs')));
end;

{ 往上找第一个已存在的目录：目标目录可能还不存在，真正决定能否创建的是最近的已存在父级。
  这里不调用 ForceDirectories，取消安装后不会留下空目录。 }
function NearestExistingDir(Dir: String): String;
var
  Current: String;
begin
  Current := RemoveBackslashUnlessRoot(Dir);
  while (Current <> '') and (not DirExists(Current)) do
    Current := ExtractFileDir(Current);
  Result := Current;
end;

function IsDirWritable(Dir: String): Boolean;
var
  ProbeDir, ProbeFile: String;
begin
  ProbeDir := NearestExistingDir(Dir);
  if ProbeDir = '' then
  begin
    Result := False;
    Exit;
  end;

  ProbeFile := AddBackslash(ProbeDir) + '.pdxnext-write-test.tmp';
  Result := SaveStringToFile(ProbeFile, 'ok', False);
  if Result then
    DeleteFile(ProbeFile);
end;

{ 只在目录页做前置校验。静默安装（启动器自动更新）没有页面，NextButtonClick 也可能被调用，
  返回 False 会让 Setup 直接退出，所以必须先放行静默流程，绝不能让探针拦住无人值守更新。 }
function NextButtonClick(CurPageID: Integer): Boolean;
var
  Dir: String;
begin
  Result := True;
  if CurPageID <> wpSelectDir then
    Exit;
  if WizardSilent then
    Exit;

  Dir := WizardDirValue;

  if IsForbiddenDir(Dir) then
  begin
    MsgBox(ExpandConstant('{cm:BadInstallDir}'), mbError, MB_OK);
    Result := False;
    Exit;
  end;

  { 管理员模式会走 UAC 提升，目录权限由提升后的进程决定，这里不必预检。 }
  if (not IsAdminInstallMode) and (not IsDirWritable(Dir)) then
  begin
    MsgBox(ExpandConstant('{cm:InstallDirNotWritable}'), mbError, MB_OK);
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  if OtherModeInstalled then
    Result := ExpandConstant('{cm:OtherModeInstalled}')
  else
    Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Mode: String;
begin
  if CurStep = ssPostInstall then
  begin
    if IsAdminInstallMode then
      Mode := 'perMachine'
    else
      Mode := 'perUser';
    SaveStringToFile(ExpandConstant('{app}\install-mode.txt'), Mode, False);
  end;
end;
