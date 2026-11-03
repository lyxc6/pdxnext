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
function OtherModeInstalled: Boolean;
begin
  if IsAdminInstallMode then
    Result := FileExists(ExpandConstant('{localappdata}\Programs\PdxNext\PdxLauncher.App.exe'))
  else
    Result := FileExists(ExpandConstant('{commonpf}\PdxNext\PdxLauncher.App.exe'));
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
