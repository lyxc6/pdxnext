# 安装包与自动更新

本文记录 PdxNext 的 Windows 安装包、双安装模式和自动更新链路。实现代码在
`src/PdxLauncher.Core/Updates/`、`src/PdxLauncher.App/Services/UpdateFlow.cs`、`installer/PdxNext.iss`
和 `tools/build-installer.py`。

## 1. 为什么是 Inno Setup，而不是 MSIX

PdxNext 是**非打包、自包含**的 WinUI 3 应用：

- `WindowsPackageType=None`、`EnableMsixTooling=false`；
- 运行时和 Windows App SDK 都随 `dist\PdxNext` 一起分发；
- 数据必须留在程序目录（`PdxDataRoot`），不能回退到 `%LOCALAPPDATA%` 的 MSIX 虚拟化目录；
- `steam_api64.dll`、Steam 子进程和游戏目录写入都依赖普通 Win32 文件系统语义。

MSIX 不是不能做，而是会同时改变身份、安装位置、文件虚拟化和更新模型：

| 维度 | Inno（当前） | MSIX |
| --- | --- | --- |
| 安装位置 | `%LOCALAPPDATA%\Programs` 或 Program Files | `WindowsApps`（受保护） |
| 数据目录 | 程序目录 / `%LOCALAPPDATA%\PdxNext-Data` | 包虚拟化目录，路径语义变化 |
| 更新 | 下载 Setup + 校验 + 启动安装器 | App Installer / `.appinstaller` |
| 签名 | 可不签（有 SmartScreen 提示） | **必须**受信任证书 |
| Steam 子进程 | 普通进程 | 受包身份/别名影响 |
| 本机现状 | 可直接构建、安装 | 本机 MSIX 未注册，历史上 `Add-AppxPackage` 被拒 |

因此当前发布走 Inno。以后若要上 Microsoft Store 或做 MSIX 自动更新，需要单独做打包工程、
代码签名证书、包身份和 `PdxDataRoot` 的虚拟化适配，不能只改安装器。

## 2. 双安装模式

安装器默认**每用户**，在欢迎页/安装模式页可以切换到**每机器**。`/ALLUSERS` 和
`/CURRENTUSER` 也可在命令行强制指定。

| | 每用户（默认） | 每机器 |
| --- | --- | --- |
| UAC | 不需要 | 需要管理员授权 |
| 目录 | `%LOCALAPPDATA%\Programs\PdxNext` | `%ProgramFiles%\PdxNext` |
| 开始菜单 | 当前用户 | 所有用户 |
| 数据目录 | `<安装目录>\data` | `%LOCALAPPDATA%\PdxNext-Data`（每个用户独立） |
| 自动更新 | 静默安装，无 UAC | 下载后弹 UAC，取消则应用不退出 |
| 卸载 | HKCU 卸载项 | HKLM 卸载项 |

上表的目录只是**默认值**：向导里「选择目标位置」一页始终可见，可以改成任意目录
（例如 `D:\Apps\PdxNext`），升级时预填上次的目录。数据目录规则随之自动适配，
详见 [自定义安装位置](#自定义安装位置)。

安装器在 `ssPostInstall` 写入 `<安装目录>\install-mode.txt`：

```
perUser
```

或

```
perMachine
```

`PdxDataRoot` 先读这个标记；没有标记时按“是否在 Program Files 下 / 程序目录是否可写”推断。
每机器安装不会把数据写进 Program Files，而是回退到 `%LOCALAPPDATA%\PdxNext-Data`，因此：

- 同一台机器的不同用户各自有设置、缓存和日志；
- 更新安装器只替换程序文件，不碰用户数据；
- 从旧的每用户安装切到每机器前，安装器会检测另一模式的安装并中止，要求先卸载
  （卸载不会删数据）；检测读的是对方的卸载项里的实际安装目录，所以自选目录也能找到。
  旧数据可用 `data-root.txt` 指过去，或手动复制。

### 自定义安装位置

向导的「选择目标位置」页始终显示，`DefaultDirName` 只是初值：

- 全新安装：默认 `%LOCALAPPDATA%\Programs\PdxNext`（每机器模式为 `%ProgramFiles%\PdxNext`），
  可以改成任意目录，例如 `D:\Apps\PdxNext`。
- 升级安装：预填上次的目录（`UsePreviousAppDir=yes` 从卸载项的 `Inno Setup: App Path` 读取），
  也可以改到新目录——旧目录的程序文件会被卸载项带走，用户数据按下面的规则处理。
- 自动更新：`/SILENT` 静默安装没有页面，直接装回原目录，不受这里影响。

`DisableDirPage=no` 是刻意写死的：Inno 的默认值 `auto` 在检测到旧安装目录时会把整页隐藏，
用户就再也选不了位置（旧版本正是这样，见 [排错](#8-排错)）。

自选目录下的数据落在哪里，由 `install-mode.txt` 决定：

- 每用户安装 → `<安装目录>\data`（程序目录可写，例如 `D:\Apps\PdxNext\data`）；
- 每机器安装 → `%LOCALAPPDATA%\PdxNext-Data`（不写进 Program Files，每个用户独立）。

点「下一步」时会做前置校验，明显不合适的目录会被拦下并提示（中英文随向导语言）：

| 拦截 | 原因 |
| --- | --- |
| 盘符根 / 共享根（`C:\`、`\\nas\share`） | 程序文件会散落到盘根 |
| `%WINDIR%` 及其子目录 | 系统目录，卸载和权限都不可控 |
| 用户目录根、`%LOCALAPPDATA%` 根、`%LOCALAPPDATA%\Programs` 根 | 会污染用户配置目录 |
| Program Files 根（32/64 位） | 会污染其他程序共用的目录 |
| 每用户模式下当前用户不可写的目录 | 复制阶段必然失败，提前给出明确提示 |

可写性探测是在「最近的已存在父目录」里写一个 `.pdxnext-write-test.tmp` 再删掉，
不会为了探测创建空目录；每机器模式不探测（权限由提升后的进程决定）。

## 3. 构建安装包

前置：

- .NET SDK 10.0.402（仓库要求）；
- Inno Setup **7.1.1 或更高**（64-bit edition 优先）：

```bat
winget install --id JRSoftware.InnoSetup.7 -e -s winget
```

`ISCC.exe` 的查找顺序：`INNO_SETUP_ISCC` → `%ProgramFiles%\Inno Setup 7` →
`%ProgramFiles(x86)%\Inno Setup 7` → `%LOCALAPPDATA%\Programs\Inno Setup 7`。

步骤：

```bat
publish.cmd
python tools\build-installer.py
```

产物：

```
dist\installer\PdxNext-Setup-<version>.exe
dist\installer\update.json
```

`publish.cmd` 负责生成 `dist\PdxNext`（整目录、含 `.xbf` / `.pri`）；安装器只读取这个目录，
不会自己重新发布。`build-installer.py` 会校验 `App.xbf`、`PdxLauncher.App.pri`、
`Views\HomePage.xbf`、`Views\SettingsPage.xbf`、`Controls\ConfirmOverlay.xbf` 和
`Assets\AppIcon.ico`，缺一个就拒绝打包。

安装器排除以下运行时状态，升级时不会覆盖：

- `data\`（设置、缓存、日志、播放集等）；
- `data-root.txt`；
- `*.log`；
- `update-cache\`；
- `install-mode.txt`（安装时重写）。

版本号只有 `version.props` 一个源：

```xml
<Version>1.0.0</Version>
<FileVersion>1.0.0.0</FileVersion>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
```

发版时改这里，重新 `publish.cmd`，再 `python tools\build-installer.py`。安装器文件名、
`update.json.version` 和程序集版本都从这里来。

## 4. 发布到 GitHub Releases

更新清单默认地址：

```
https://github.com/lyxc6/pdxnext/releases/latest/download/update.json
```

源码与安装包都放在公开仓库 `lyxc6/pdxnext`；仓库必须是公开的，否则启动器匿名下载会 404/401。
发布走 GitHub Actions（`.github/workflows/release.yml`），推 `v*` tag 时自动构建并上传资产。

```powershell
$env:GITHUB_TOKEN = "ghp_..."
.\tools\publish-release.ps1 -Version 1.0.0 -NotesFile .\docs\RELEASE-NOTES-1.0.0.md
```

脚本会：

1. 用 `version.props` 或 `-Version` 定位安装包；
2. 创建 `v<version>` release（已存在则复用）；
3. 删除同名旧资产；
4. 先上传 `PdxNext-Setup-<version>.exe`，最后上传 `update.json`。

`update.json` 的 schema：

```json
{
  "schemaVersion": 1,
  "version": "1.0.0",
  "setupUrl": "https://github.com/lyxc6/pdxnext/releases/download/v1.0.0/PdxNext-Setup-1.0.0.exe",
  "sha256": "64 位小写十六进制",
  "size": 67576019,
  "releaseNotesUrl": "https://github.com/lyxc6/pdxnext/releases/tag/v1.0.0",
  "releaseNotes": "PdxNext 1.0.0",
  "mandatory": false,
  "publishedUtc": "2026-11-03T11:37:53Z"
}
```

校验规则（`UpdateManifest.TryParse`）：

- `schemaVersion` 必须为 1；
- `version` 必须是 1–3 段数字；
- `setupUrl` 必须是 HTTPS、白名单主机、路径以 `.exe` 结尾；
- `sha256` 必须是 64 位十六进制，`size > 0`；
- `releaseNotesUrl` 若存在必须是 HTTPS。

`github.com` 和 `objects.githubusercontent.com` 已加入 `OnlineHosts` 白名单；下载重定向由
`HttpClient` 跟随（最多 5 次）。

## 5. 启动器内的更新流程

- 启动后由 `MainWindow.OnRootGridLoaded` 末尾触发一次
  `UpdateFlow.CheckOnStartupAsync()`，不阻塞首屏。
- 自动检查受 `AppSettings.AutoCheckUpdates`（默认开）和 24 小时节流约束；手动检查总是联网。
- 发现新版本：`ConfirmOverlay` 显示当前/新版本、发布说明和“下载并更新 / 稍后”。
- 下载：流式写入 `<数据目录>\update-cache\PdxNext-Setup-<version>.exe.part`，边下边算
  SHA-256；长度和摘要都匹配后才改名为正式文件，失败清理 `.part`。
- 安装：先保存设置，再启动安装器：
  - 每用户：`/SILENT /SUPPRESSMSGBOXES /CLOSEAPPLICATIONS /NORESTART /CURRENTUSER`；
  - 每机器：同上加 `/ALLUSERS`，并通过 ShellExecute `runas` 弹 UAC；
  - 安装器完成后用 `runasoriginaluser` 重新启动启动器（每机器安装不会把启动器提权）。
- UAC 取消（Win32 错误 1223）时应用**不退出**，只提示“已取消管理员授权”。
- 安装器写 `install-mode.txt`；应用下次启动按标记选择数据目录。

## 6. 安全边界

- 清单和安装包都只允许 HTTPS；清单主机白名单精确匹配，不做子域通配。
- 安装包必须同时匹配 `size` 和 SHA-256，防止半截下载或 CDN 缓存旧文件。
- 没有代码签名证书：Windows SmartScreen 可能提示“未知发布者”。这是当前自用阶段的已知
  取舍；对外分发前应购买/申请 Authenticode 证书，并在 Inno 里配置 `SignTool` 与
  `SignedUninstaller=yes`。
- 安装器不写服务、不写计划任务、不静默提权；每机器更新必须由用户确认 UAC。
- 更新清单没有独立签名；它和安装包都依赖 GitHub HTTPS 与 SHA-256。若以后要防“仓库被
  篡改”，应给 `update.json` 加 detached signature（如 minisign）并在客户端验签。

## 7. 实机验证清单

1. **每用户安装**：双击 Setup → 不弹 UAC → 目录在
   `%LOCALAPPDATA%\Programs\PdxNext`；启动后 `install-mode.txt` 为 `perUser`；数据在
   `<安装目录>\data`。
2. **自定义安装目录**：双击 Setup → 目录页可见 → 改成 `D:\Apps\PdxNext` → 安装完成；
   `install-mode.txt` 为 `perUser`，数据在 `D:\Apps\PdxNext\data`。再覆盖安装一次，
   目录页仍可见且预填该目录。顺便试一次 `C:\` 或 `C:\Windows`，应在「下一步」被拦下。
3. **每机器安装**：在安装模式页选“为所有用户安装” → 弹 UAC → 目录在
   `%ProgramFiles%\PdxNext`；每个用户首次启动后数据在
   `%LOCALAPPDATA%\PdxNext-Data`。
4. **升级保留数据**：在旧版本里改设置/加缓存，覆盖安装新版本；`data` 不被删除。
5. **每用户自动更新**：发布新 `update.json` 和 Setup → 启动器发现更新 → 下载 → 校验 →
   静默安装 → 自动重启，无 UAC。
6. **每机器自动更新**：同上，但在启动安装器时弹 UAC；取消后启动器仍在运行。
7. **校验失败**：临时改坏 `update.json.sha256` 或 Setup 文件 → 启动器提示校验失败，
   不启动安装器，`.part` 被清理。
8. **离线/404**：断网或清单 404 → 启动器只记日志，不弹错误（手动检查才提示）。
9. **模式切换**：已装每用户版本时尝试每机器安装 → 安装器提示先卸载；卸载后数据保留。
10. **卸载**：控制面板卸载后程序目录移除，`data` / `%LOCALAPPDATA%\PdxNext-Data` 保留。
11. **自检**：`dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"`
    必须 `检查类: 166，失败: 0`。

## 8. 排错

| 现象 | 原因 / 处理 |
| --- | --- |
| `ISCC.exe (Inno Setup 7) not found` | 未安装 Inno 7，或不在默认路径；设 `INNO_SETUP_ISCC` |
| `payload is missing XAML artifacts` | `dist\PdxNext` 不是完整发布产物；重跑 `publish.cmd` |
| 安装器 0xC000027B 退出 | 不是安装器问题；`dist\PdxNext` 缺 `.xbf`/`.pri`，重发布 |
| 更新检查 `host-not-allowed:github.com` | 旧版本没带 GitHub 白名单；升级到本版本 |
| `releases/latest/download/update.json` 404 | 发布仓库不是公开的，或 release 不是 latest（draft/prerelease） |
| 每机器更新不弹 UAC、直接失败 | 安装目录是 Program Files 但没有标记；确认 `install-mode.txt` 为 `perMachine` |
| 更新后设置丢了 | 检查是否误把 `data` 打进安装包；`[Files]` 必须排除 `data\*` |
| SmartScreen 拦截 | 无签名，属预期；选“更多信息 → 仍要运行”，或后续加 Authenticode |
| 向导里没有「选择目标位置」 | 旧版本安装器在检测到旧安装时隐藏了该页（Inno `DisableDirPage` 默认 `auto`）。升级到本版本后始终显示；临时可用 `PdxNext-Setup-x.y.z.exe /DIR="D:\Apps\PdxNext"` 指定目录 |

## 9. 版本与发布顺序

1. 改 `version.props`；
2. `publish.cmd`（构建 + 自检 + `dist\PdxNext`）；
3. `python tools\build-installer.py`（Setup + `update.json`）；
4. 跑 `dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"`；
5. `tools\publish-release.ps1` 上传 Setup 和 `update.json`；
6. 用旧版本启动器做一次真实升级验证；
7. 更新 `README.md`（版本徽章与截图）、`docs/DEVELOPMENT.md`、`docs/HANDOFF.md` 和发布说明。
