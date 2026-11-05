# PdxNext 开发文档

面向贡献者与维护者的开发笔记：构建、离线依赖、安装包与自动更新、实现细节、目录结构、自检、
数据隔离、已知限制与排错。面向使用者的项目介绍见仓库根 [README.md](../README.md)。

一个用 **WinUI 3 / Windows App SDK** 重写的 Paradox 游戏启动器，目标是替代 P 社官方启动器
（Electron + 内嵌 sqlite 的那套），做到**秒开、可离线、不碰官方目录**。

> **交接与任务拆分见 [HANDOFF.md](HANDOFF.md)** —— 当前进度、环境限制、待办任务（T1–T8）都在里面。

> **⚠ 使用范围**：个人自用工具，不随包分发游戏本体或 DLC 内容，也不提供任何绕过购买的下载源。
> 仓库里的「补丁素材」（`src\PdxLauncher.Core\Patch\Assets\`）是第三方 Steamworks 代理 DLL
> （CreamAPI 4.x）与配套 ini，来源见该目录的 `README.md`，仅供本机离线研究与已安装内容的整理；
> 是否使用及由此产生的后果由使用者自行承担。自有代码按 MIT 许可（见 `LICENSE`），
> **第三方素材不在 MIT 覆盖范围内**，版权归原作者。

官方启动器路径（保持只读，绝不写入）：`%LOCALAPPDATA%\Programs\Paradox Interactive\launcher`。

---

## 1. 快速开始

### 前置条件

- **.NET SDK 10**（实测 10.0.402，`dotnet --version` 可查）。不需要 Visual Studio，不需要装
  Windows App SDK Runtime，也不需要联网——`NuGet.config` 里用 `<packageSources><clear /></packageSources>`，
  全部包走本机缓存 `%USERPROFILE%\.nuget\packages`（23 个包，含 `Microsoft.WindowsAppSDK 2.5.1` /
  `Microsoft.WindowsAppSDK.WinUI 2.3.9` / `CommunityToolkit.Mvvm 8.4.2`）。缓存里缺包时构建直接失败。
- **运行端什么都不用装**，见下面「自包含」。

#### 从仓库克隆后第一次构建

`NuGet.config` 里是 `<clear />`（一个包源都没有），所以包必须**事先躺在本机缓存里**。克隆后先确认：

```bat
dir "%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk"      :: 应有 2.5.1
dir "%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk.winui" :: 应有 2.3.9
dir "%USERPROFILE%\.nuget\packages\communitytoolkit.mvvm"         :: 应有 8.4.2
build.cmd
```

缺包时构建会直接失败（没有联网还原的退路）。临时要联网还原的话，把 `NuGet.config` 里的
`<clear />` 换成 `<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />`，
拿到包之后改回来——`<clear />` 是故意的，本项目构建期一次网络请求都不发。

包缓存默认在 `%USERPROFILE%\.nuget\packages`，设了环境变量 `NUGET_PACKAGES` 时以环境变量为准
（`tools\manual-build.py` 与 `tools\package-app.py` 读的是同一个变量，不写死路径）。

### 日常命令

日常只有一条命令 —— **双击仓库根目录的 `start.cmd`**，或者对仓库右键「在终端中打开」后敲 `start.cmd`：

```bat
start.cmd        :: 一键启动：Release 增量构建后直接运行（已有实例在跑就跳过启动）
start.cmd debug  :: 同上，但用 Debug 配置
start.cmd check  :: 构建 + 跑一遍自检，再启动（相当于原来的 build.cmd + run.cmd）
start.cmd new    :: 忽略「已在运行」判断，强制再开一个窗口
start.cmd /?     :: 打印用法
```

`start.cmd` 每次都跑增量构建（不猜时间戳、不做短路），改完代码直接双击就是最新版本；
它自己会 `cd` 到仓库根、自动找 `dotnet`、只在构建或自检失败时停住窗口，成功就悄悄把窗口关掉。
**它和 `scripts\messages.txt` 之外不写任何中文**（顶层 `.cmd` 必须纯 ASCII，见 §7 最后一条）。

单独构建 / 单独启动（脚本化、CI、或只想跑某一步时用）：

```bat
build.cmd        :: Release 构建 + 跑自检（首次约 1 分钟，之后增量约 15 秒）
run.cmd          :: 只启动 Release 版，不构建
```

调试用：

```bat
build-debug.cmd
run-debug.cmd
```

### 用 VS Code 打开工程

仓库自带 `.vscode\`，用 VS Code 打开这个目录（`code .`）即可，不用装 Visual Studio：

- `Ctrl+Shift+B` 直接跑默认任务 **「PdxNext: 一键启动（增量构建 + 运行）」**，等价于 `start.cmd`；
- `Ctrl+Shift+P` → `Tasks: Run Task` 还能选「构建 + 自检 (Release)」「构建 (Debug)」「启动 Debug」「自检」「发布到 dist」；
- 构建错误进「问题」面板（`$msbuild` matcher）；
- 仓库里的 `bin` / `obj` / `dist` / 各种临时目录默认在资源管理器与搜索里隐藏（`.vscode\settings.json`），
  文件树只剩源码。

`.vscode\` 里只提交 `tasks.json` / `settings.json` / `extensions.json` 三个文件，其余本机配置照旧被 `.gitignore` 忽略
（这三个文件**不含任何机器路径**，任务里一律用 `${workspaceFolder}` 相对引用）。
**故意不放 `launch.json`**：WinUI 3 自包含 exe 的 F5 调试不可靠，要断点请用「启动 Debug」任务跑起来后
`Ctrl+Shift+P` → `Debug: Attach to Process` 附加 `PdxLauncher.App.exe`。

发布给别人用：

```bat
publish.cmd                 :: 构建 + 自检 + 发布，输出到 dist\PdxNext\
publish.cmd D:\out\PdxNext  :: 输出到指定目录
```

### 产物路径

构建产物（直接双击可运行）：

```
src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe
```

发布产物（要拷给别人时用这个目录，**不要只拷 exe**）：

```
dist\PdxNext\PdxLauncher.App.exe
```

两个目录都是 **538 个文件（顶层 438 项：349 个文件 + 89 个本地化子目录）/ 约 232 MB**，
内容逐项一致（`diff` 无差异）。自包含部署
（`SelfContained=true` + `WindowsAppSDKSelfContained=true`），
**目标机不需要装 .NET 运行时，也不需要装 Windows App SDK Runtime**。

Debug 产物同结构，路径把 `Release` 换成 `Debug`。

### ⚠ 分发时必须整目录拷

WinUI 3 运行时需要读 XAML 编译产物（`App.xbf` / `MainWindow.xbf` / `Views\*.xbf`）、
资源索引（`PdxLauncher.App.pri`）和 `Assets\`，缺任一项都会**直接退出、无窗口、无日志**。

> 实测：删掉这 5 项后双击，进程立刻消失，退出码 `-1073741189`（即 `0xC000027B`
> `STATUS_STOWED_EXCEPTION` —— WinUI 加载 XAML 期间的崩溃），并且程序目录下的 `data\logs\`
> 连目录都不会建，所以**没有任何日志可看**。注意在 Git-Bash / MSYS 里直接跑这个 exe 会看到
> 退出码 127，那是 bash 自己的「无法执行 Windows exe」包装码，不是应用的真实退出码，排查时要看
> `cmd` 或 PowerShell 里拿到的值。

> **历史上 `dotnet publish` 的产物是坏的**：顶层只有 433 项，缺上述 5 项，双击立刻退出。
> 根因是这些 XAML 产物只由 WinUI 的 `AddProcessedXamlFilesToCopyLocal` 目标
> （`BeforeTargets="GetCopyToOutputDirectoryItems"`）复制进 `$(OutDir)`，而 publish 走的是
> `GetCopyToPublishDirectoryItems` 这条独立链路，完全看不到它们。
>
> **已修复**：`src\PdxLauncher.App\PdxLauncher.App.csproj` 末尾的 `AddWinUIXamlArtifactsToPublish`
> 目标在 `ComputeFilesToPublish` 之后把这 5 项补进 `ResolvedFileToPublish`。现在 publish 输出与 bin
> 目录逐文件一致，已实测可启动（Release / Debug 双配置）。若有人删掉该目标，`publish.cmd` 会在
> 拷目录前用 `if not exist` 兜底拦住并报错。
>
> 另注：**不要用 `dotnet publish -o <dir>`**。`-o` 会把平台子目录（`x64`）从 `$(OutDir)` 路径里
> 去掉，补全目标就取不到 `.xbf` / `.pri`，产物又会变回坏的。这也是 `publish.cmd` 先发布到默认
> `PublishDir`、再整目录 `xcopy` 到 `dist` 的原因。

### ⚠ 构建建议带 `-m:1`

`build.cmd` / `publish.cmd` 里已经写好了。手敲命令请用：

```bat
dotnet build "PdxLauncher.sln" -c Release -m:1
```

原因：`PdxLauncher.App` 声明了 `<Platforms>x64</Platforms>` + `RuntimeIdentifier win-x64` +
`SelfContained true`，而解决方案配置是 `Any CPU`。MSBuild **并行**构建时，App 对 Core 的项目引用
在 `_GetProjectReferenceTargetFrameworkProperties` 目标上会跨节点静默失败，输出只有：

```
生成失败。
    0 个警告
    0 个错误
```

没有一行 error，看着像玄学。加上 `-m:1`（单进程 MSBuild）就正常。

> **2026-10-26 实测补记**：本机 SDK 10.0.402 环境下，**不带 `-m:1` 也能构建成功**
> （`dotnet build PdxLauncher.sln -c Release` 跑了两遍，均 0 警告 0 错误）。所以这是**环境相关**的
> 坑，不一定必然复现；保留 `-m:1` 只为稳妥，代价是慢几秒。

### 安装包与自动更新

安装器用 **Inno Setup 7.1.1+**，默认每用户安装（无 UAC），安装模式页可切换为每机器
（Program Files + UAC）。每机器安装的数据回退到 `%LOCALAPPDATA%\PdxNext-Data`，不写
Program Files。

```bat
winget install --id JRSoftware.InnoSetup.7 -e -s winget
publish.cmd
python tools\build-installer.py
:: 产物：dist\installer\PdxNext-Setup-<version>.exe + update.json
```

自动更新清单默认是
`https://github.com/lyxc6/pdxnext/releases/latest/download/update.json`，仓库必须公开；
发布走 GitHub Actions（推 `v*` tag 自动构建并上传），本地也可用 `tools\publish-release.ps1`。启动器检查 24 小时节流、下载时边写边算
SHA-256、校验长度/摘要后按安装模式静默安装或提权安装。

版本号唯一来源是 `version.props`；安装器排除 `data\`、`data-root.txt`、`*.log` 和
`update-cache\`，升级不覆盖用户数据。完整流程、MSIX 取舍和实机验证清单见
[INSTALLER.md](INSTALLER.md)；版本规则见 [VERSIONING.md](VERSIONING.md)。

---

## 2. 已经做到什么

### 游戏发现
两个来源，且不需要"已知 gameId 白名单"——目录里有可解析的 `launcher-settings.json` 就收录：

1. **Steam 库**：注册表 `HKCU/HKLM\SOFTWARE\Valve\Steam\SteamPath` + `libraryfolders.vdf` 找到全部库，
   再扫每个 `steamapps\common\*`。
2. **官方 Paradox 启动器的独立游戏库**（不经 Steam 安装的游戏在这里）：读
   `%APPDATA%\Paradox Interactive\launcher-v2\userSettings.json` 的 `gameLibraryPaths`
   ——字符串条目是库根（相对路径按官方启动器目录 `%LOCALAPPDATA%\Programs\Paradox Interactive\launcher`
   解析，官方默认值是 `../games`），对象条目取 `installationPath`；再兜底扫默认目录
   `%LOCALAPPDATA%\Programs\Paradox Interactive\games`。库根本身和它的一级子目录都会检查。

`launcher-settings.json` 的三种约定位置（大小写不敏感）：

- `<root>\launcher-settings.json`（Stellaris 就是这种）
- `<root>\launcher\launcher-settings.json`（CK3、Victoria 3）
- `<root>\Launcher\launcher-settings.json`（大写 L，Prison Architect 的旧 schema）

gameId 一律取自 JSON 里的 `gameId` 字段，**不看文件夹名**；同一 gameId 在 Steam 与独立库里都出现时，
保留先扫到的 Steam 那份（有 appmanifest，能拿到 SteamAppId）。官方 `userSettings.json` **只读，绝不写入**。

识别后按 **支持等级** 分档（卡片右上角角标 / 左侧导航可用性都由它决定）：

| 等级 | 本机实测游戏 | 启动器负责 |
| --- | --- | --- |
| 完整适配 | Crusader Kings III、Victoria 3、Stellaris | 启动 + 播放集 / Mod 库 / DLC + 加载文件 |
| 仅启动 | Prison Architect（老 schema，formatVersion 语义不同）、Cities: Skylines（本体不读加载文件） | 只负责拉起 exe（--launched-from-launcher）；模组与 DLC 由游戏本体管理，启动器不读不写它的任何数据 |
| 未适配 | 未登记 gameId 的目录 | 只展示，卡片标「未适配」 |

「仅启动」这两款各有各的理由：Prison Architect 的 `formatVersion` 语义和别的游戏不同（老 schema），
Cities: Skylines 则是**本体根本不读任何加载文件**——它的 mod 由游戏内 Colossal Mods Domain 枚举
`Addons\Mods` 和 Steam 工坊目录加载，开关状态存在自己的 `.cgs` 设置里；DLC 归属纯靠 Steam 授权，
安装目录里没有 `.dlc` 文件。所以对它们只做「启动」：不读 sqlite、不扫 DLC 目录、不写加载文件。

官方启动器给 Cities: Skylines 写的 `gameId` 是 **`cities_v2`**，而 Paradox 元数据 API、Steam appId
映射表、创意工坊表只认 **`cities_skylines`**。启动器在**发现阶段**就把 `cities_v2` 归一到
`cities_skylines`（`KnownGames.CanonicalizeGameId`），支持等级、appId 映射、工坊来源、远程主题
因此都能命中；只有**公告接口**反过来只认原始 id，由 `KnownGames.GetNewsFeedGameId` 单独兜住。

### 加载文件（启动器唯一必须写对的东西）

按 `formatVersion` 分两套格式，都是**紧凑 JSON、UTF-8 无 BOM、结尾无换行**：

| formatVersion | 文件名 | 键 |
| --- | --- | --- |
| 0 | `dlc_load.json` | `disabled_dlcs`（相对 dlcPath 的 POSIX .dlc 路径）、`enabled_mods`（mods.gameRegistryId） |
| 1 / 1.1 | `content_load.json` | `disabledDLC`（`[{paradoxAppId}]`）、`enabledMods`（`[{path}]`）、`enabledUGC`（`[{path}]`） |

游戏自己读这个文件，启动器**不写** `playsets.json` / `game_data.json` / `continue_game.json`。

### 播放集 / Mod / DLC
**事实源是本启动器自己的 sqlite**（程序目录下 `data\pdxnext.sqlite`，表结构见
`docs\DATA-FORMATS.md` 第 20 节）；官方 `launcher-v2.sqlite`（knex schema：`mods`、`playsets`、
`playsets_mods`、`playsets_dlcs`、`key_value_pairs`）只在**存在时**当读源与回写目标。
两者之间靠 `LauncherDbProjector` 双向同步：官方库在 ⇒ 读前导入、写后回写；官方库不在 ⇒
照常读写自有库，**绝不创建 `launcher-v2.sqlite`**，等它下次出现再补投射。
底层同样用 **P/Invoke `winsqlite3.dll`**，不依赖任何 NuGet sqlite 包（本项目全离线构建）。

- 播放集页：**两种显示方式**，在「设置 → 界面 → 播放集页面显示方式」切换（存 `data\settings.json`（程序目录下）
  的 `playSetPageDisplayMode`，默认 `officialLauncher`）：
  - `officialLauncher`（原版启动器布局）：顶部一条工具条（左侧按钮簇放不下时**自动折行**，搜索框与「添加模组」始终完整可见）——播放集下拉（**只切换查看/编辑目标，不改变激活状态**，
    选中项显示集名与「● 激活」标记）+ 新建 / 重命名 / 删除 ｜ 分隔线 ｜「激活播放集」（把当前选中的播放集写成游戏激活集，
    激活后按钮变「已激活」并置灰）+「启用全部」「禁用全部」｜ 中＝搜索框（`AutoSuggestBox`，`QueryIcon="Find"` 带放大镜与清除按钮；
    打字过程只更新下拉里的「最近搜索」建议，**按回车 / 点放大镜 / 点右侧刷新按钮才真正查询**），右＝「添加模组」（打开独立子页，候选来自 Mod 库）；
    原先第二行的「当前播放集名 / 计数 / 已激活：X」摘要已删除，这些信息改由下拉框选中项与状态栏承载；
    下方**只显示该播放集的 mod**：逐项启用禁用、上下移动排序（上移/下移一位）、**点行首序号直接输入目标位次**（回车生效，
    Esc 或点别处取消，其余行依次顺移而非互换；**输入的数字大于总行数时视为置底**，不会报错；
    搜索状态下位次编辑与上下移动一并置灰）、从播放集移除。
    「添加模组」现在是一个**独立子页**（左栏没有它的菜单项，选中态停在「播放集」）：
    顶部是返回按钮 + 标题 + 目标播放集名，下面是搜索框与候选计数，再下面是可勾选的候选列表
    （候选来自 Mod 库，已在该播放集里的不会出现；每行带缩略图、名称、来源徽标与大小/注册 ID），
    底部是「已选 N 个 mod」与「取消 / 添加」。勾选后点「添加」会回到播放集页并把 mod 追加进去
    （默认启用、排在末尾，随后自动保存）；点「取消」或返回则什么都不改。
    每行名称后带**来源徽标**（本地 / 订阅），进游戏时会被跳过的行另有一个警告图标，悬停看原因
    （缺路径 / 缺注册名 / 缺压缩包 / 路径不存在 / 路径含非 ASCII）。工具条上的「修复 mod 路径」
    会扫出可修的项：缺路径的按工坊编号补上、本地 mod 目录名含非 ASCII 且该游戏不支持时改成
    `mod_<id 前 8 位>`（撞名自动 `_2`/`_3`），改名成功才写库，写库失败会把目录移回原处。
    所有改动**自动保存到 sqlite**（`playsets_mods` / `playsets_dlcs`，写完回读比对）；**只有当前播放集是激活集时才写游戏加载文件**，
    编辑未激活集时状态栏会提示「未激活，游戏加载文件未改动」。**没有**「保存」「保存并启动」按钮。
    删除：删非激活集不改变游戏配置；删激活集时先激活接替者、再删除并写入接替者的加载文件；只剩一个激活集时拒绝删除。
  - `placeholder`（留空）：整页只显示占位提示，回设置页切回即可恢复原版布局。
- Mod 库页：只读展示 mod 全量（自有库的 `mods` 表，官方库在时由官方库导入），每行显示缩略图、名称、
  **来源徽标**（本地 / 订阅）、大小、订阅日期、mod **自己声明的支持版本**（`requiredVersion`，即 .mod 的 `supported_version` /
  metadata.json 的 `supported_game_version`，原样显示不做换算）、以及「在 N 个播放集中」（不在任何播放集时不占位）；
  本地 mod、官方库里状态不是 `ready_to_play` 的 mod、以及**声明的支持版本低于当前游戏版本**的 mod
  带三角提示标记，提示只讲「这个 mod 声明支持哪个版本、和当前游戏版本对不对得上」（登记情况与库里的状态作为补充说明）；
  订阅来源的行还会显示**真实 Steam 状态徽标**（已在 Steam 安装 / Steam 有更新 / Steam 下载中 N% / Steam 排队中 /
  Steam 本地已禁用，取自 Steamworks `GetItemState` 等接口，见 `docs/DATA-FORMATS.md` §18.5），
  状态未知（子进程刚 Init 还没同步、或本来就查不到）时不显示，绝不当成「没订阅」；
  行尾**四个图标按钮**分别是打开工坊详情页（复用创意工坊那个详情子页）、
  打开本地文件夹、在 Steam 中打开、在浏览器中打开（拿不到目录或工坊编号时对应按钮置灰），
  后面还有两个按需出现的按钮：**让 Steam 下载**（`DownloadItem`，没装或需要更新时可用）与
  **在 Steam 中禁用 / 启用**（`SetItemsDisabledLocally`，工坊层的「订阅但本地禁用」，和退订是两件事，
  要先用设置里打开「Steam 工坊直连订阅」）；支持搜索（名称 / 注册 ID / 目录）、
  **按来源筛选（全部 / 本地 / 订阅）**、按名称 / 大小 / 订阅时间排序、刷新；数量徽标 = 当前筛选下的行数。
  行操作的地址由 `SteamWorkshop` 拼装：创意工坊 id 优先取 `mods.steamId`，为空时回退从
  `mod/ugc_<id>.mod` 解析；打开动作是 `Process.Start` + `UseShellExecute`（资源管理器 / `steam://` / https），
  不经 `IFileSystem`，也不受 `OnlineHosts` 白名单约束（那是本进程 HTTP 的白名单）。
  「打开详情页」这一路先把行翻译成 `ModWorkshopRef`（平台 + 编号：Steam 用上面那套判定，
  CS2 的 `pdx:<sourceId>` 取数字部分），再交给 `WorkshopRowViewModel.FromLibrary` 造一条详情行导航进
  `WorkshopDetailPage`；编号解析不出来的行按钮置灰，提示文案直接说明为什么（本地 mod / 没有工坊编号）。
  详情子页的返回按钮回到**发起方那一页**（`WorkshopDetailNavigationRequest.ReturnTag`），
  左栏选中项也停在发起方。
- Mod 创建页：列出本地 mod、按游戏格式新建描述符、把 mod 上传到 Steam 创意工坊（走 `--steam-host workshop-publish`
  子进程，进程内不调 Steamworks）。上传表单对齐 Steam 客户端能改的面：
  - **可见性四档**：公开 / 仅限好友 / 隐藏 / 非公开（`SetItemVisibility`，对应 `ERemoteStoragePublishedFileVisibility` 的 0/1/2/3）；
  - **内容描述词五项**：裸露画面、频繁暴力血腥、仅限成人色情、频繁裸露、常见成人内容（`AddContentDescriptor` / `RemoveContentDescriptor`，
    每次提交先全删 1..5 再按勾选加，取消勾选才能真正生效）；
  - **预览**：主封面之外还能加最多 8 张附加图（`.png/.jpg/.jpeg/.bmp/.gif/.webp`，**单张必须 < 1 MB**，
    这是 Steam 的硬限制，启动器在提交前就按文件名拦掉），以及 YouTube 视频预览（贴 watch / youtu.be / shorts 链接均可，
    `AddItemPreviewVideo`）；每次提交先清空已有附加预览再从 0 顺序追加，所以顺序由列表的上移 / 下移决定；
  - **多语言标题与描述**（`SetItemUpdateLanguage`）：照 Steam 客户端的「编辑标题与描述」对话框，上传表单里从上到下是
    **语言下拉（默认 english）→ 标题 → 描述**。切语言时把当前文本存进该语言的槽位、再载入目标语言已有的文本，
    所以界面上永远只有一份标题/描述框，不会铺成一堆语言行；非 english 的文本只影响对应语言的用户看到什么。
    之所以仍然逐语言提交：`SetItemUpdateLanguage` 的语言绑定在 update handle 上，**一个 handle 只能承载一种语言**，
    N 种语言必然是 N 次「起 update → 设语言 → 设标题/描述 → 提交」，这是 API 强制的顺序。
    每次只改标题与描述、不重传内容也不重设可见性；部分语言失败只会带回一段「有 N 种语言未写入」的说明，不会把主上传报成失败。
    这条是实验性的 —— Steam 没把「同一 handle 多次设语言」的行为写清楚，被拒时就是这个降级表现；
  - **封面**：新建 mod 默认写入项目自带的品牌封面（内嵌在 `PdxLauncher.Core` 的 `default-cover.png`，
    取自 App 的 `Square150x150Logo.scale-200.png`），不再是原来的 1×1 透明占位图；上传页还有「使用内置封面」一键填回。
  发布记录存在自有库里（`ModPublication`），再次打开上传页会回填上次的可见性 / 描述词 / 附加图 / 视频 / 语言；
  老记录只有 `public: true|false`，读取时按「true→公开、false→隐藏」兼容。
  - **列表徽标只说状态**：已发布的 mod 右侧挂一个「已发布」短徽标（`PublicationBadgeText`），工坊 id 由副标题
    与详情面板的「发布状态」行显示；带 `{0}` 的文案不能直接绑到 XAML（`Localizer.Format` 无参时原样返回模板），
    否则界面上会出现字面的 `{0}`，`LocalizationBindingChecks` 专门守这条；
  - **上传面板有出口**：上传成功、取消上传、或在上传页点左侧另一个 mod 都会回到详情面板（失败时故意留在上传页，
    保留表单与错误信息好重试）；取消正在进行的上传时状态栏先显示「正在取消上传…」。
  - **详情面板能直接打开工坊页**：「在 Steam 中打开」与 Mod 库行尾那个按钮同一个地址
    （`steam://url/CommunityFilePage/<id>`，`SteamWorkshop.GetSteamAddress`），没有工坊编号时置灰；
    浏览器入口仍保留，两条路互不影响。
  - **详情面板能改描述符**：「编辑描述符」只改名称 / 版本 / 支持的游戏版本 / 标签 / 简介（V3 的 `short_description`），
    其余内容原样保留：legacy 的 `.mod` 是行级替换，注释、未知键与 `path` 不动，外层 `<folder>.mod` 与
    `<folder>/descriptor.mod` 一起写；V3 的 `metadata.json` 用 `JsonNode` 改属性，`id`、`relationships`、
    `game_custom_data` 等键不动。写盘走临时文件 + rename，双文件时第二个失败会把第一个回滚。
  - **描述是 BBCode，不是纯文本**：Steam 工坊描述走社区方言 BBCode（`[h1]`–`[h3]`、`[b]`、`[i]`、`[u]`、`[strike]`、
    `[list]`/`[olist]`、`[hr]`、`[url=地址]文字[/url]`、`[img]`、`[code]`、`[quote]`、`[spoiler]`，
    以及**裸链接自动生成小部件**）。上传页描述框上方有一条 11 个按钮的工具条（B I U S H2 • 1. — Link Img Code），
    按光标位置插入或包裹标记，焦点自动回到描述框；下方是「预览（读者看到的样子）」，用 `SteamBbCodeParser`
    把 BBCode 解析成与详情页同一棵 `MarkdownDocument` 再用 `MarkdownView` 渲染，描述为空时整块不显示。
  - **标题与描述按字节限长**：Steam 是定长字符缓冲（标题 128、描述 8000、更新说明 8000），启动器按 **UTF-8 字节**
    判（一个汉字算 3 字节），上传页实时显示「已用 / 上限」并在超限时把提示置红，提交前在视图模型与 Steamworks
    宿主子进程里各拦一道；`ModPublishLimits` 的错误文案会告诉你实际字节数。
- DLC 页：纵向列表，**左侧固定 208×117 缩略图，右侧标题 + 两行简介**（文字再长也不会把图片压小，超出两行省略、鼠标悬停看全文），逐项启用禁用，写 `playsets_dlcs` 并同步加载文件。简介优先用本地描述（Victoria 3 的 `.dlc.json` 自带 `description`），CK3 / Stellaris 的 `.dlc` 没有简介字段，联网时按 Steam appid 查商店简介补全并缓存 7 天（设置页可关）

播放集 / Mod 库 / DLC 三页**只对「完整适配」的游戏生效**；选中「仅启动」的游戏时，左侧这三项置灰、左栏底部常驻一行说明（禁用的导航项收不到指针事件，所以不用悬停提示），主页的播放集下拉隐藏、改显示一行「由游戏本体管理」的说明，不会去读它的 sqlite，也不会扫 DLC 目录。

**补丁（DLC 解锁）不受支持等级限制**：它只依赖「安装根能找到 `steam_api64.dll` / `steam_api.dll`」和「能解析出 Steam appId」两件事，
所以「仅启动」的游戏照样能打——Cities: Skylines 的 `steam_api64.dll` 就在安装根，`Cities.exe` 没有 SteamStub 之类的壳，
`ColossalNative.dll` 是运行时 `LoadLibrary("steam_api64.dll")` 动态加载（不在导入表里），所以把 CreamAPI 的 loader + `cream_api.ini` 放进去即可生效。
独立安装（没有 `appmanifest`）时 appId 从 `GameSteamAppIds` 映射表拿，`cities_skylines` 登记的是 255710。

### 启动游戏
`Process.Start` 直接拉 `exePath`，**cwd = 游戏 exe 所在目录**，`UseShellExecute=false`，
`CreateNoWindow=true`，stdout/stdin 不重定向（stderr 重定向到日志）。

参数只传 `launcher-settings.json` 里的 `exeArgs`（CK3 / Victoria 3 / Stellaris 是 `-gdpr-compliant`，Prison Architect 是 `--launched-from-launcher`）；
勾了"继续游戏"再加 `--continuelastsave`；设置页里手填的额外参数追加在最后。

**不传 session token**——那是官方启动器跟 Paradox 服务端通信用的，离线复刻不需要。

### 补丁（DLC 解锁 / 一键移除）
设置 → 高级里有「一键打补丁」和「移除补丁」：给**当前选中的游戏**换上 CreamAPI loader（`steam_api64.dll` /
`steam_api.dll`）并生成配套 `cream_api.ini`，让已装但未购买的 DLC 能在离线 / 正版联机下被游戏识别。
**不碰 DLC 内容文件**（`game\dlc\*` 一个字节都不写）——解锁只发生在 loader 与 ini 这一层。

- **备份与还原**：打补丁前把**目标游戏自己**的 `steam_api*.dll` 原子备份成同目录的 `<名字>_o.dll`，
  再写入 loader；「移除补丁」= 把 `_o` 移回原名 + 删掉带 `; PdxNext 自动生成` 标记的 `cream_api.ini`。
  整合包里自带的 `steam_api64_o.dll` **不会**被拷给别的游戏（各游戏原版 dll 版本不同，直接拷会把游戏弄坏）。
- **状态判定**（设置页实时显示，`Inspect` 只读）：原版 / 已打补丁 / 被别的工具打过 / **被 Steam 更新覆盖**
  （文件回到原版但 `_o` 还在，可一键重打）/ 未找到 api 文件 / 缺少 appId 无法打补丁。
- **目标目录**：CS2 在 `Cities2_Data\Plugins\x86_64\`；其余游戏按「exe 所在目录 → 安装根目录 →
  `binaries\`」找第一个含 `steam_api64.dll` / `steam_api.dll` 的目录（两个都在就都处理）；
  CK3 / EU5 / Imperator / Victoria 3 实际落在 `binaries\`，CS1 / CK2 / EU4 / HOI4 / Stellaris 落在根目录。
- **appId** 取自 `launcher-settings.json` / `appmanifest` 的 Steam appId，独立安装时回退到
  `src\PdxLauncher.Core\Online\GameSteamAppIds.cs` 的内置表（名单见该文件）。
- **补丁素材**：`steam_api.dll`、`steam_api64.dll`、`cream_api.ini` 内嵌在 `PdxLauncher.Core.dll` 里
  （`src\PdxLauncher.Core\Patch\Assets\`，来源见该目录 README，仅个人离线研究用）；
  程序目录下的 `data\patch\assets\` 可整体覆盖同名文件，整合包更新只要丢文件进去、不用重编译。
  打过的游戏记在 `data\patch\state.json`，素材目录用「打开素材目录」按钮直达。
- **边界**：只改 `steam_api*` 与 `cream_api.ini`；游戏运行中 / 目录只读时操作失败并给原因，不动已备份的原文件；
  不改官方 Paradox 启动器目录；不联网；不做「全部游戏一起打」的批量按钮。

### 主题
读游戏安装目录里的 `theme-settings.json`（`relativePath` / `color` / `font` 等键），
在本地渲染成 WinUI `SolidColorBrush` / `FontFamily` 应用到整个窗口。
联网刷新是**可选功能**（设置页可关），关掉后完全离线、只用安装目录里的素材。

**背景图是按版本走的，不是安装目录里那张**。和官方 Paradox Launcher v2 一样，主题素材由 Paradox 元数据服务
按 `gameId` 下发一组 URL（路径末尾带内容 hash），下载到
`data\theme-cache\<gameId>-<assetKey>-<urlHash8>.<ext>`（程序目录下），TTL 24 小时、过期先返回旧图再后台刷新；
游戏发新版本 ⇒ 服务端换图 ⇒ URL 变 ⇒ 本地文件名跟着变（顺带让 WinUI 的图片缓存自动失效）。
实测 CK3 拿到的背景图与官方启动器 `cache\ck3-background` **sha256 完全一致**，
而它与安装目录 `launcher\assets\app-background.png` 是两张完全不同的图。
主页的「继续」「开始游戏」两个按钮按官方「主题按钮」的语义渲染：**主题背景图铺满 232×66 的按钮**（官方 14.5rem × 4.1667rem），
**主题 `button.color` 是文字色、不是底色**，`buttonHover.color` 是悬停文字色，字体 / 字号 / 字重同样取自主题。
悬停叠 8% 白并换成悬停文字色、按下缩到 0.85、禁用降到 55% 透明；没有主题图的游戏退回「幽灵按钮」（白 5% 底 + 官方式 2px 内嵌白高光，不额外描外边框）。
CK3 是深炭纹理 + 米色字、Victoria 3 是紫红大理石 + 金珠边框 + 近白字、Stellaris 是暗青纯色 + 白字，切游戏即随之切换；其余控件保持系统 Fluent 样式。

### 界面
`TitleBar` + `NavigationView` + `Frame`，左栏页面：游戏库 / 当前游戏 / 播放集 / Mod库 / Mod创建 / 创意工坊 / DLC / 账号 / 设置。
另外有两个不带左栏菜单项的子页：创意工坊详情页、**「添加模组」子页**（从播放集页进入，左栏选中态停在「播放集」）。
全中文，跟随系统亮/暗主题，卡片式游戏库。

当前游戏页的布局对齐官方 Paradox 启动器：居中字标 → 「继续」（左）+「开始游戏」（右）→ 播放集选择器 →
版本号（公告栏正上方、右对齐、裸版本串）→ 底部三张官方公告卡（左大 451×230、右二各 246×230）。
官方主页右上角的「刷新」按钮按用户意见去掉了（页面加载与切换游戏时自动刷新）；版本号照官方 Victoria 3
放到广告卡正上方（官方在右下角，这里与广告行同宽、右对齐），且不带「版本」前缀。播放集选择器用
`DropDownButton + Flyout`（`BottomEdgeAlignedLeft`）而不是 ComboBox：WinUI 的 ComboBox 下拉总把选中项
垂直居中到控件中心，选中最后一项时必然向上展开，`MaxDropDownHeight` 改不了这个定位模型。
播放集按钮与弹层的边框也做了收边：按钮 `BorderThickness=0`、圆角 4，弹层 `CornerRadius=8`、去掉 1px 硬边并内缩 4 DIP，同时关掉选中项的系统焦点白框（`UseSystemFocusVisuals=False`），只保留选中底色。
**不再有统计卡、最近存档条或外层卡片** ——
官方主页上这些控件都直接浮在背景图上，所以我们也去掉外框；最近存档改由「继续」按钮的悬停提示承载。
公告卡同样照官方实现：纯图块（无内框、无内边距）、圆角 4、外 1px 黑 40% + 内 1px 白 10% 描边、
悬停压 50% `#131724` 遮罩；只有该公告自带标题时才在左下角叠 24px 白字 + 底部渐变（官方也不渲染正文）。
公告来自官方的 Braze content cards（可选联网，设置页可关）；没公告的游戏整块隐藏；
`pdxlaun://` 这类启动器内部深链当前不跳转，点了会在公告区下方给一行提示，而不是把卡片灰掉。
公告卡是纯图卡（标题/正文通常为空），所以**图没下载回来就等于没内容、不会显示**；这类失败不占用 6 小时缓存，
单张图 8 秒超时后本轮不再逐张干等，5 分钟后自动重试补图。

当前游戏的主题背景图铺满**整个窗口**（含标题栏条与左侧导航面板），页面以半透明纱与玻璃卡片浮在图上；
在游戏库里点另一张卡片即换图（例如 CK3 深红肖像 ↔ Victoria 3 亮色插图）。没有选中游戏时退回纯色底，
视觉与普通 WinUI 应用一致。

### 应用图标
石墨炭圆角方底 + 金色「P」，P 的碗（计白）里放一枚同色系金色播放三角 —— P 是 Pdx，播放三角是「启动器 / Next」。
底色 `#2E2E36 → #1B1B1F → #0E0E12` 纵向渐变 + 轻微暗角，金色字标 `#FFE3A3 → #F2C14E → #C08A2B`；
≥32px 加 1px 白 13% 描边（浅色任务栏上也有边界）、≥48px 加柔和投影，16/20/24px 光学加粗且不画投影，保证 16px 仍读得出「P + 三角」。

图标由 `tools/gen-app-icon.py` **唯一生成**（Pillow + numpy 自绘，机器上没有 SVG 栅格化工具，所以没有 SVG 母版）：
`AppIcon.ico` 是 9 帧 16/20/24/32/40/48/64/128/256（<128 用 32 位 BMP 帧带 AND mask、≥128 用 PNG 帧），
`Assets\` 里 8 张 Microsoft 模板同名 PNG（88 / 300 / 50 / 48 / 24 / 48 / 1240×600 / 620×300）一并替换，文件名与尺寸都不变。

接到哪里：exe 资源走 csproj 的 `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`（双击 exe 后的任务栏 / 文件管理器 / Alt-Tab 图标），
运行中的窗口图标由 `MainWindow.ApplyWindowIcon()` 调 `AppWindow.SetIcon(…\Assets\AppIcon.ico)` 设上（任务栏、Alt-Tab、Win+Tab；标题栏是自定义的纯文字标题栏，不画小图标）。

改图标只改生成器里的常量，然后

```bat
python tools\gen-app-icon.py          :: 重写 Assets 里 9 个文件 + docs\app-icon.png 接触表
python tools\gen-app-icon.py --preview :: 只写 .tmp\icon-preview\，用来先看效果
```

`docs\app-icon.png` 是深浅两种底色下的接触表（16→256）。自检 `IconChecks` 会校验 ICO 容器结构、帧尺寸集合、
BMP/PNG 帧策略、32px 帧的金色占比与圆角透明、8 张 PNG 的魔数与 IHDR 尺寸、以及 csproj / MainWindow / publish.cmd 三处接入点。

### 网络代理
设置页有独立的「网络代理」卡片，三选一（存 `data\settings.json` 的 `proxyMode`），改完**立刻重建整条联网链路**，不用重启：

| 模式 | 取值 | 行为 |
| --- | --- | --- |
| 直接连接（默认） | `direct` | 不走代理；Windows 代理与 `http_proxy` 等环境变量都不看 |
| 跟随系统 | `system` | 读 Windows「Internet 选项」的代理设置（`ProxyEnable` / `ProxyServer` / `ProxyOverride`）；系统没开代理时再读 `http_proxy` / `https_proxy` / `all_proxy`（大小写都认） |
| 手动配置 | `manual` + `proxyUrl` | 只用填的地址；`127.0.0.1:7890` 这种缺 scheme 的自动补 `http://` |

卡片底部还会显示「当前生效」的来源与地址（手动配置 / 系统代理 / 环境变量代理 / 直连），
一眼能看出最终走的是哪条链路；代理地址非法、填了 SOCKS、系统配置了 PAC 脚本等情况不会静默失败，
而是在这一行和日志里说明「为什么现在直连」。地址框改完按回车或点别处即生效。
旧配置只有 `proxyUrl` 没有 `proxyMode` 时按「手动配置」处理，行为与上一版一致。

设置页「联网」分类只剩代理本身；DLC 简介、官方公告、Mod 缩略图这几类联网缓存的
「开关 / 保留天数 / 目录 / 占用 / 清除」统一收在「缓存」分类里，改哪类缓存都不必跳回联网页。

### 请求预算与 429 行为

创意工坊的请求都压在同一个出口上，Steam 对同一 IP 的密度有限制，所以出口有一道按 host 的闸门
（`src/PdxLauncher.Core/Online/RequestThrottle.cs`，挂在 `HttpFetcher` 上）：

| host | 并发 | 最小间隔 | 无 `Retry-After` 时冷却 | 冷却上限 |
| --- | --- | --- | --- | --- |
| `steamcommunity.com` | 2 | 1000 ms | 15 分钟 | 60 分钟 |
| `images.steamusercontent.com` | 4 | 100 ms | 5 分钟 | 30 分钟 |
| `api.steampowered.com` / `store.steampowered.com` | 2 | 250 ms | 5 分钟 | 30 分钟 |
| 其它（PDX 等） | 4 | 不限 | 5 分钟 | 30 分钟 |

- 收到 429（或带 `Retry-After` 的 503）时该 host 进入冷却，冷却期内**本地直接拒绝**、请求根本不发出去；
  连续被限流冷却翻倍，封顶上表的上限；成功一次即归零。冷却只在进程内，重启清零。
- 同一个地址在途时只发一次（`RequestCoalescer`），详情页连点、翻页取消后重建都不会翻倍。
- 物品页 HTML 的解析结果落盘 12 小时（`workshop-info/workshop-pages.json`，最多 400 条，单条超过 512 KB 只留内存），
  12 小时内重开同一个 mod 详情是 0 请求；联网失败时退回旧副本，画廊/作者/首屏评论仍然可见。
- 评论首屏直接用物品页内嵌的前 10 条，不再单独请求第 1 页；要继续翻页才走评论接口。
- 列表页缓存 15 分钟内算新鲜、2 小时内过期仍先显示；同一页 2 分钟内不重复静默刷新；换游戏不再清空其它游戏的页。
- 搜索只在**回车**或点刷新时发起：打字过程中不发请求（旧实现每敲一个字符就排一次 1–3 MB 的列表页请求，
  限流下页面看起来像卡死）；新查询会取消上一轮在途请求，迟到的结果按查询代号丢掉。
- 搜索框是 `AutoSuggestBox`（7 处搜索框统一，见 HANDOFF §0.85）：工坊页下拉给**最近提交词**
  （`workshopSearchHistory`，最多 8 条、忽略大小写去重、最新在前；只在本机提交成功后才写入），
  其余六处下拉给**当前已加载 mod 名字**里包含输入的那些（前缀匹配排前、包含匹配排后，最多 8 条）；
  打字、点清除按钮都不发请求，只有回车 / 点放大镜 / 点刷新才查。
- 被限流时状态栏/详情页会给「已暂停约 N 分钟」「请约 N 分钟后重试」，刷新按钮在冷却期不再发请求；
  图片下载遇到限流立即放弃（其它瞬时失败仍是 500 ms / 1500 ms 三次重试）。
- 「在浏览器打开创意工坊」走系统浏览器，不经过闸门，但仍然占同一 IP 的配额。

以上缓存都在 `data\workshop-info\`，设置 → 缓存 → 清除创意工坊缓存会一并删掉。

---

### 翻译（工坊与 Mod 名称 / 简介 / 更新日志 / 评论）

设置页新增独立的「翻译」分类（左栏最下面一项），默认**开启**，覆盖创意工坊列表、PDX Mods 列表、
Mod 库、播放集里的 mod 行、工坊详情页的简介与更新日志、以及工坊评论区的正文。

- **四个后端可切换**：
  - 「自填接口」——填 OpenAI 兼容 API 的地址（Base URL）、密钥与模型名，走 `POST {Base}/chat/completions`；
  - 「微软翻译」——填 Azure Translator 的**订阅密钥 + 区域**两个凭据，走
    `POST https://api.cognitiveservices.azure.com/translator/text/v3/translate?api-version=3.0&to=zh-Hans`
    （免费层每月 200 万字符；没有免密钥的公开端点，密钥与区域缺一不可）；
  - 「谷歌云翻译」——填 Google Cloud Translation 的 **API Key**，走
    `POST https://translation.googleapis.com/language/translate/v2`（免费层每月 50 万字符）；
  - 「谷歌免费翻译」——用谷歌公开网页接口兜底，**无需任何配置**（非官方公开端点，随时可能变，可用性不保证）；
    按 `translate.google.com` → `clients5.google.com` → `translate.googleapis.com` 顺序挑能连的主机
    （部分网络对 `translate.googleapis.com` 按主机名阻断 TLS，换成前两台就能用）；
    一段文本里的多段会**合成一个请求**（每次最多 8 段、URL 不超过 1500 字符），翻译评论时不至于一段一个请求把出口 IP 打到限流；
    连不上的主机进 5 分钟冷却、返回 HTTP 429 的主机进 60 秒冷却，冷却期间直接跳过，不让每段译文都白等一次超时；
  - 「自动」是默认档：按 **自填接口 → 微软 → 谷歌云 → 谷歌免费** 依次尝试，前一个失败或没配齐就回落下一个；
    「仅自填」「仅微软」「仅谷歌云」「仅谷歌免费」是专档位，**失败就是失败，不回落**。
- **目标语言默认跟随界面语言**（界面中文→译成中文，界面英文→译成英文），也可以在设置里固定成中文或英文。
- **原文与译文同时显示**：列表行在原标题下方多出一块**带描边框**的译文，详情页的简介 / 更新日志 / 评论在原文下方
  多出一块**带描边框**的译文正文——**不覆盖原文**，也**不加「译文」小标题**（框里就是译文本身）。框内是保留 Markdown 结构的渲染结果。
- 四个范围开关可以分别关掉名称 / 简介 / 更新日志 / 评论的翻译。
- **翻译在途有提示**：只要还有翻译请求没回来（含排队等并发的批次），窗口标题栏右侧显示一个小号环形进度 +
  「正在翻译…」，最后一批结束后约 0.4 秒消失；详情页在译文将来出现的位置先显示「正在翻译简介…／更新日志…／评论…」，
  译文到了原地替换。评论是逐条译的，一条译好就挂一条，整段译完提示才收。
- 译文有本地缓存（`cache:translations` 库键，保留天数可配，默认 30 天），命中缓存就不再发请求；
  设置页「缓存」分类里多一行「译文缓存」，可单独清空。
- 翻译是**弱依赖**：请求失败、限流、返回格式不对都只记日志并在界面上保留原文，绝不把内容清空；
  一条都没译出来时，页面顶部的提示会**逐条列出每个引擎各自的原因**（如「mymemory: 免费额度已用完，约 2 小时后恢复」），
  而不是只报第一个失败者。MyMemory 的免费额度用完（HTTP 429）后，本进程内**立刻停止再请求**并显示恢复时间，
  不会每段译文都白打一次；额度恢复后自动重新尝试。
- Markdown 结构在翻译后保持不变：标题层级、列表序号、链接地址、强调样式原样保留，代码块 / 图片 / 分隔线不翻译。

域名口径：微软翻译、谷歌云翻译与谷歌免费翻译的域名都在白名单里；**自填接口的域名按用户填的地址精确授权**（只放行该主机本身，
不放行子域），授权随设置保存，撤销后立刻失效。

---

## 3. 目录结构

```
PdxLauncher.sln
start.cmd                      :: 一键启动（增量构建 + 运行），双击即可，见 §1「日常命令」
build.cmd / build-debug.cmd / run.cmd / run-debug.cmd
publish.cmd                    :: 构建 + 自检 + 发布到 dist\PdxNext\（可传目标目录）
scripts/
  msg.cmd                      :: 顶层 .cmd 取中文提示用的小助手（纯 ASCII）
  messages.txt                 :: 全部中文提示，UTF-8 无 BOM，KEY:文本 格式
.vscode/                       :: 只提交 tasks.json / settings.json / extensions.json
  tasks.json                   :: 默认任务＝一键启动 start.cmd，另有构建 / 自检 / 发布
  settings.json                :: 隐藏 bin/obj/dist 等产物目录，编码与换行约定
  extensions.json              :: 推荐 ms-dotnettools.csdevkit / .csharp
Directory.Build.props          :: 统一可空/语言版本/告警策略
version.props                  :: 版本号唯一来源（安装器/程序集/update.json）
NuGet.config                   :: <clear/>，全离线
installer/
  PdxNext.iss                  :: Inno Setup 7 双模式安装器脚本
src/
  PdxLauncher.Core/            :: 205 个 .cs，无任何 WinUI 依赖，可单测
    Abstractions/              :: 接口（IGameCatalog / IPlaySetRepository / IGameLauncher / INewsFeedService ...）
    Contracts/                 :: 不可变 record 数据模型
    Games/                     :: Steam 库探测、launcher-settings 解析、游戏目录展开、路径规则（V3 非 ASCII）
    Steam/                     :: registry + libraryfolders.vdf（Valve KeyValues 解析）
    Playsets/                  :: winsqlite3 P/Invoke + 官方库仓储 + 自有库仓储 + 双向投射 + mod 来源/路径修复
    Storage/                   :: 自有库 pdxnext.sqlite、JSON 文档存储、遗留 JSON 入库、缓存计量与清理
    LoadFiles/                 :: 两套加载文件格式的写入与回读比对 + mod 跳过规则
    Launching/                 :: 进程启动、continue_game.json 读取
    Signatures/                :: dlc_signature 重算（默认关闭）
    Themes/                    :: 主题 JSON 解析 + HSL/RGB 颜色
    Online/                    :: GraphQL 元数据、素材下载、代理（三模式解析 / 系统代理 / 环境变量）、DLC 简介、官方公告
    Online/Translation/        :: 翻译引擎（自填 OpenAI 兼容 API + 微软 Azure Translator + 谷歌 Cloud Translation + 谷歌公开免费接口）、译文缓存、引擎模式回落
    Updates/                   :: 更新清单解析、版本比较、下载校验、按安装模式启动安装器
    Parsing/                   :: Clausewitz 与 Valve KeyValues 解析器
    Patch/                     :: 补丁素材（内嵌 loader + cream_api.ini）、目标目录判定、打补丁 / 还原
    ModStudio/                 :: Mod 创建：描述符规则/读取/编辑、legacy 与 V3 metadata 生成、本地清单、发布记录、内嵌默认封面（Assets/）
  PdxLauncher.App/             :: 155 个 .cs + 18 个 .xaml，WinUI 3 层
    Assets/                    :: AppIcon.ico（9 帧 16→256）+ 8 张模板同名 PNG，全部由 tools/gen-app-icon.py 生成
    Views/                     :: GameLibraryPage / HomePage / ModsPage（播放集） / ModLibraryPage（Mod库） / ModStudioPage（Mod创建/上传） / DlcPage / SettingsPage / WorkshopDetailPage（工坊详情子页） / AddModsPage（添加模组子页）
    ViewModels/                :: VM（全部不含 Microsoft.UI.Xaml 类型）
    Controls/                  :: 自绘控件：ConfirmOverlay（全局「确认 / 取消」浮层，取消在左、确认在右）、SettingsCard、SettingsValueCard、MarkdownView、PdxWrapPanel（排不下就自动换行，WinUI 没有 WrapPanel）
    Services/                  :: AppServices 装配、ThemeApplier、BitmapLoader ...
tests/
  PdxLauncher.Checks/          :: 108 个检查类，纯 console 自检（无 xunit，因为离线）
  fixtures/                    :: 抓下来的真实数据样本
docs/
  HANDOFF.md                   :: 交接文档：已完成项 / 已知缺陷 / 下一步
  AGENT-WORKFLOW.md            :: 1.3 起的分工约定：派活模板 / 并发隔离 / 验收清单 / 文档归属
  DATA-FORMATS.md              :: 所有数据格式的真机实测记录（含自有库表字典，见第 20 节）
  INSTALLER.md                 :: 安装包、双安装模式、自动更新与发版流程
  app-icon.png                 :: 应用图标接触表（深/浅底，16→256，由生成器写出）
  screenshot-home.png          :: 当前游戏（整窗主题背景）
  screenshot-library.png       :: 游戏库
  screenshot-settings.png      :: 设置
tools/
  manual-build.py              :: 完全绕过 MSBuild 的 Roslyn + XamlCompiler 手工构建链
  package-app.py               :: 打包脚本
  build-installer.py           :: 用 Inno Setup 7 打安装包并生成 update.json
  publish-release.ps1          :: 上传 Setup 与 update.json 到公开 GitHub Releases
  gen-app-icon.py              :: 应用图标唯一生成源（Pillow + numpy 自绘 → ico / 8 张 PNG / 接触表）
```

> **顶层 `.cmd` 必须保持纯 ASCII**。中文提示一律写在 `scripts\messages.txt`，由
> `scripts\msg.cmd <KEY> [附加文本]` 打印。原因见本文第 7 节同名条目：cmd.exe 按当前代码页
> 逐字节重读批处理文件，非 ASCII 字节在 cp936 等代码页下会把行边界撕开，导致偶发的
> `'行文件:' is not recognized as an internal or external command` 之类假报错。

**547 个 .cs + 18 个 .xaml**（不含 `obj/` 生成物；2026-11-04 实测，含工作区未提交文件：Core 272 / App 155+18 / Imaging 9 / Checks 111 —— Checks 的 111 个文件是 108 个检查类 + Program + ChecksContext + GlobalUsings；运行时打印的「检查类」就是 108，另 3 个是自检框架本身）。

---

## 4. 自检

```bat
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"
```

输出 `检查类: 108，失败: 0，断言失败总数: 0` 即通过（2026-11-04 实测 108 类；真机段只锁结构——行数、启用态这类本机数据只打印 `info` 不作断言，Victoria 3 播放集那条写死的 `mod 行数 >= 22` 已按这个原则删掉，见 HANDOFF §0.85.5）。覆盖：Steam 探测与
launcher-settings 解析、两套加载文件格式的写入/回读、播放集与 sqlite、Mod 库只读读取、
Mod 创建（legacy `.mod`/`descriptor.mod` 与 V3 `.metadata/metadata.json` 生成、非法输入拒绝、本地清单合并、发布记录与发布协议参数、
工坊发布面：可见性四档映射、1 MB 与张数上限、五种 YouTube 链接解析、30 种语言码与归一化、请求 JSON 全字段往返、旧 `public` 记录回显、内嵌默认封面的 PNG 签名与落盘一致、
描述 BBCode 解析（标题 / 列表 / 分隔线 / 链接 / 图片 / 视频缩略图 / `[noparse]` / 未知标签原样保留 / 超长截断）、标题与描述的 UTF-8 字节上限（汉字 3 字节、emoji 4 字节、128 与 8000 的边界）、绑定已有工坊条目（id 解析边界、四态发布文案判据、`workshop-mine` / `workshop-mine-detail` 结果往返、原生结果结构体 416 / 9760））
（mods 全量、size / subscribedDate 解析、播放集归属、requiredVersion 兼容判断）、创意工坊 id 解析、启动参数拼装、路径大小写不敏感处理；
自有库 `pdxnext.sqlite`（建表幂等 / schema 版本 / 事务回滚 / 损坏自愈 / `documents` 键空间与前缀统计）、
官方库双向投射（导入带来源与工坊 id、投射回写新行、**官方库缺席时绝不创建 `launcher-v2.sqlite`**、pending 补投）、
mod 来源归一（`source=local` 带工坊 id 仍算本地）、加载文件跳过规则（六种 key + 四套格式）、
路径修复（补路径 / 改名为 ASCII / 撞名 `_2` / 父目录非 ASCII 只提示 / 回滚）；
以及联网子系统：域名白名单与 magic bytes 判型（含 GIF）、素材下载与原子改名、主题补丁合并、
DLC 简介的 TTL / 负缓存 / 失败退避、官方公告的 JSON 解析（时间窗与 weight）、404 负缓存、离线不联网、6 小时 TTL、30 分钟失败退避、图片落盘与复用、缓存往返；
以及网络代理：三模式归一化与旧配置迁移、手动地址补 scheme / 去引号 / 拒绝 SOCKS 与非 http、
「跟随系统」的 ProxyEnable 关闭回落环境变量、`http=a;https=b` 分号写法、PAC 不执行且不回退、
`ProxyOverride` → 正则 → `WebProxy.IsBypassed`、带账号密码时解析成 `NetworkCredential`；
以及应用图标：ICO 容器（保留区 / 类型 / 9 帧 / 偏移不重叠且末帧正好收尾）、帧尺寸集合与 32 位色深、
<128 走 BMP 且 ≥128 走 PNG、32px 帧的金色占比与圆角透明、8 张模板同名 PNG 的魔数与 IHDR 尺寸、
csproj 的 `<ApplicationIcon>` / MainWindow 的 `AppWindow.SetIcon` / publish.cmd 的 `Assets\AppIcon.ico` 校验三处接入点；
以及补丁：内嵌 loader / ini 的字节数与 sha256、x86-only / x64-only 的目标目录选择、打补丁后的备份字节与 ini 内容
（appId 改写、`[dlc]` 保留、PdxNext 标记）、清单落盘与幂等重打、还原后原字节恢复且 `_o` 与 ini 消失、
旧手工补丁与 Steam 覆盖两种状态的可还原性判定、缺 appId 时拒绝、目标文件被占用时不破坏原文件；
以及 Paradox 账号登录：密码哈希向量（含空密码与特殊字符）、盐与头键名与官方启动器逐字一致、
`PUT /accounts/sessions/launcher-v2` 的头形状与空 body、邮箱去空格且头里不含明文密码、
401/500/网络异常到错误码的映射、`session` 嵌套与顶层扁平两种响应形状、缺 token 判失败、
「不勾保持登录不落盘 / 勾了才落盘并能读回 / 改回不勾会清掉旧文件」，以及登录表单
「可编辑性与邮箱无关、可提交性＝可编辑且邮箱非空」这条死锁回归钉子；
以及翻译：Markdown 结构保留（标题层级 / 列表序号不变、代码块不译、译文为空保留原文、连续纯文本合并成一次请求）、
目标语言与引擎模式的归一化、译文缓存的命中 / 过期 / 清空、自动模式自填失败回落内置、
仅自填模式不回落、仅内置模式不碰自填引擎，以及未授权域名被拦下（`host-not-allowed`）、
自填接口域名授权后放行且不放行子域、撤销授权后立刻失效；
以及机翻引擎：档位归一化（`microsoft` / `google-cloud`）、微软翻译的请求形状（`api-version=3.0`、`to=zh-Hans`、
订阅密钥与区域两个头齐全、正文是 JSON 数组）与译文解析、谷歌云翻译的请求形状（`key` 走查询参数、
正文含 `q` / `target` / `format`）与译文解析、两者的失败只记状态码且**密钥不写进日志**、
凭据不全时不发请求，以及自动模式下失败项由下一个引擎补齐而专档位不回落；
还有免费引擎的主机回落与合批：首选 `translate.google.com` 抛网络异常时换 `clients5.google.com` 拿到译文、
坏主机进冷却后只打备用主机、冷却解除后重新先试首选、全冷却时返回 null 且不发请求；
3 段文本只发一个请求且译文保序、20 段按每批 8 段切成不超过 3 个请求；
首选主机 429 时换下一台并进 60 秒冷却、三台都 429 时返回 null 且原因里带主机名；
以及 MyMemory 的 429：记额度耗尽与恢复时间、本进程内不再请求、恢复时间过后重新尝试，
和「多个引擎都失败时提示里逐条列名」。
以及界面规范：XAML / C# 里的 `Padding`、`Margin`、`Spacing`、`RowSpacing`、`ColumnSpacing` 每个分量都是 4 的倍数，`CornerRadius` 只用 0/4/8（`SpacingChecks`，规则见 §8）。

---

## 5. 隔离性（重要）

| 东西 | 位置 | 权限 |
| --- | --- | --- |
| 官方启动器目录 | `%LOCALAPPDATA%\Programs\Paradox Interactive\launcher` | **只读，永不写入** |
| 官方 userSettings.json | 同上 | **只读** |
| 游戏数据目录 | `%USERPROFILE%\Documents\Paradox Interactive\<游戏>` | 官方库在时读写它 + 加载文件；**不会创建 `launcher-v2.sqlite`** |
| 本启动器自己的数据 | `<PdxNext 程序目录>\data\`（**不写 %LOCALAPPDATA%**） | 全部自己的 |
| 自有数据库 | `data\pdxnext.sqlite`（播放集 / mod / settings / session / 六份缓存索引） | 同上 |
| 启动器设置 | 库内 `settings` 键（原 `data\settings.json`，已入库） | 同上 |
| DLC 简介缓存 | 库内 `cache:dlc-descriptions` 键 | 同上（TTL 7 天） |
| 主题素材缓存 | 库内 `cache:theme-assets:<gameId>` + 图片 `data\theme-cache\` | 同上（TTL 24 小时） |
| 官方公告缓存 | 库内 `cache:news-cards` + 图片 `data\news-cache\` | 同上（TTL 6 小时） |
| Mod 缩略图缓存 | 库内 `cache:mod-thumbnails` + 图片 `data\mod-cache\` | 同上（保留天数可配，默认 30 天） |
| 译文缓存 | 库内 `cache:translations` 键 | 同上（保留天数可配，默认 30 天） |
| 补丁素材（外部覆盖） | `data\patch\assets\` | 同上（可选；同名文件优先于内嵌素材） |
| 补丁清单 | `data\patch\state.json` | 同上（记录打过补丁的游戏与备份路径） |
| 游戏安装目录的 `steam_api*.dll` / `cream_api.ini` | 各游戏安装目录 | **只在用户点「一键打补丁 / 移除补丁」时写**，原文件备份为同目录 `*_o.dll` |
| 本启动器日志 | `data\logs\pdxnext.log` | 同上 |

本启动器和官方启动器可以共存，随便来回切，互不影响。
数据根目录由 `PdxDataRoot` 决定（`src/PdxLauncher.Core/Configuration/PdxDataRoot.cs`）：
优先 `<exe 目录>\data-root.txt`（里面写一行绝对路径）→ `<exe 目录>\data` →
`<exe 目录>\..\PdxNext-data`（程序目录不可写时），**任何情况下都不回退到 C 盘**。
首次启动会把旧的 `%LOCALAPPDATA%\PdxNext` 内容**复制**过来（`DataRootMigrator`：
settings.json / dlc-descriptions.json / theme-cache / news-cache / mod-cache，不搬 logs、永不删源、
以 `.migrated-from-localappdata` 标记保证幂等）；随后 `LegacyJsonImporter` 把这些 JSON 写进
`pdxnext.sqlite` 并把原文件改名成 `<名>.migrated.bak` 留档。设置页「数据目录」卡片可以打开该目录或
删掉旧的 C 盘副本。`publish.cmd` 覆盖 `dist\PdxNext` 前会把 `data` 搬到
`dist\PdxNext.__data__`、发布完搬回，所以重新发布不会吞掉设置与缓存。
`pdxnext.sqlite` 打不开或建表失败时会被改名成 `pdxnext.sqlite.corrupt-<毫秒>` 留档并重建空库，
宁可丢缓存也不让程序起不来。

---

## 6. 已知限制

1. **不联网登录**：不做 Paradox 账号登录，Mod 订阅列表只从本地 sqlite 读。
   联网只用于可选的元数据/主题素材刷新、DLC 简介补全与主页官方公告。
2. **`dlc_signature` 默认不写**：算法（md5 over MachineGuid + 各 .dlc 内容 + 固定 salt）是逆向出来的，
   但无法保证与官方逐字节一致，写了反而可能被判篡改。设置页里有开关，默认关。
3. **Prison Architect 只支持启动（不支持内容管理）**：它用的是旧 schema，本体不读
   `content_load.json`、没有 `launcher-v2.sqlite`、mod 走 `gameDataDir\mods\<name>\manifest.txt`、
   DLC 只由 Steam 授权控制。所以启动器只负责拉起 exe，播放集 / Mod / DLC 一律交给游戏本体
   （游戏内 MOD 菜单 / Steam 创意工坊），绝不写它的数据。
4. **EU5 未安装**：只在本机 Documents 残留里看到过，安装目录不存在，所以没做真机验证。
5. **不扫 Epic / GOG 等其他平台的库**：官方启动器自己也不扫，本启动器只跟随 Steam 与官方独立库路径。
   别的平台装的游戏，只要目录里有 `launcher-settings.json`，把目录加进官方启动器的游戏库路径即可被发现。
6. 游戏本体启动依赖 Steam 运行中（Steam DRM）；这不是启动器能绕过的。
7. **代理只支持 HTTP / HTTPS**：.NET 的 `SocketsHttpHandler` 不能走 SOCKS，所以系统里配了 SOCKS 代理时会直连并提示原因；
   PAC 自动代理脚本也不执行（检测到 `AutoConfigURL` 就按直连处理，不会去抓脚本）。需要代理的场合请填 HTTP 代理地址。
8. **Victoria 3 不能加载路径含非 ASCII 的 mod**：这是游戏本体的限制（Vic3 wiki "Mod structure" + Paradox 官方论坛同题帖），
   `content_load.json` 里写得再对也不生效，且游戏日志不报任何错。启动器会在播放集页给出警告，
   并可用「修复 mod 路径」把本地 mod 目录改成 `mod_<id 前 8 位>`。父目录含非 ASCII（如 `C:\Users\张三\…`）时
   不自动改名，只提示——那种情况得改 `launcher-settings.json` 的 `gameDataPath`。CK3 / Stellaris / EU4 无此限制。
9. **所有原生 Steamworks 调用都在子进程里跑**：DLC 授权探测、创意工坊直连订阅都需要以游戏 appId 调
   `SteamAPI_Init`，而 Steam 会把「调过它的进程」登记成该游戏的 game process（`SteamAPI_Shutdown` 不撤销）。
   2026-10-31 之前启动器自己调，导致游戏退出后 Steam 仍显示「运行中」，点 Steam 的「退出游戏」还会把启动器杀掉；
   现在改成同一个 exe 的隐藏宿主模式（`PdxLauncher.App.exe --steam-host probe|workshop-…`），探测约 1 s、订阅最多 25 s、
   订阅集合（`workshop-subscribed`）最多 60 s、批量写（`workshop-batch`：下载 / 本地禁用 / 启用）最多 45 s，
   子进程一退 Steam 立刻除名。上传工坊条目最多 20 分钟，开了多语言时按语言数放大到 `20 分钟 ×（1 + 语言数）`。
   所以偶尔在 Steam 库里闪一下「运行中」是子进程，属预期；主窗口不会被登记，也不会被连坐。
   设置页把「读状态」和「改订阅」拆成两个开关：读状态（默认开）每次只起一个只读子进程；
   写动作（默认关）才允许订阅 / 退订 / 触发下载 / 本地禁用。

---

## 7. 排错

**构建输出"生成失败 / 0 个错误"** → 忘了 `-m:1`，见本文第 1 节。

**启动后白窗口 / 闪退** → 看 `%LOCALAPPDATA%\PdxNext\logs\pdxnext.log`。
如果日志目录根本没建出来，说明权限不足（例如在低完整性级别进程里跑）。

**游戏退了 Steam 还显示运行中 / Steam 点「退出游戏」把启动器关了** → 2026-10-31 已修（见 HANDOFF §0.59）：
根因是启动器进程内调了 `SteamAPI_Init` 被 Steam 登记成游戏进程。确认两点：①用的是 2026-10-31 之后构建的 exe；
②日志里应有 `Steam 宿主 probe：exit=0，耗时 … ms` 这样的行（说明探测走的是子进程）。
若仍复现，先在 `D:\app\Steam\logs\console_log.txt` 里 grep `Game process added`，看被登记的 PID 是不是主窗口那个。

**找不到游戏** → 确认游戏装在某个 Steam 库里，或在官方启动器的独立库路径里，且安装目录下真有
`launcher-settings.json`。改完路径点右上角「刷新」；日志里会打印扫描结果（`游戏发现完成，共 N 个`）。

**Mod 列表为空** → 现在的读源是 `data\pdxnext.sqlite`：首次启动会从游戏数据目录下的
`launcher-v2.sqlite` 导入（至少用任一启动器成功启动过一次游戏才会有它）；没有官方库也能用自有库，
但两者都没数据就是空的。若是 Prison Architect，这是预期行为：它是「仅启动」等级，
左侧播放集 / Mod 库 / DLC 本来就置灰。

**某个 mod 勾了但进游戏没生效** → 播放集页那一行的警告图标会说明原因（缺路径 / 缺注册名 /
路径不存在 / 路径含非 ASCII）。Victoria 3 的非 ASCII 路径限制见本文第 6 节第 8 条，点工具条上的
「修复 mod 路径」可以自动改目录名与补路径。

**写完加载文件游戏没生效** → 游戏启动时会重写这个文件；确认启动前 launcher 已完全退出
（本启动器是保存即写、写完只回读，不会在游戏启动后继续持有）。

**双击 `run.cmd` / `build.cmd` 弹出「打开文件 - 安全警告」（发布者：未知发布者）** →
这是 Windows 对「低完整性级别（Low IL）目录里的脚本」做的发布者校验，不是病毒，也和
「文件从网上下载被锁定」（`Zone.Identifier` 数据流）无关 —— 本项目的脚本上并没有这个数据流。

判定（PowerShell）：

```powershell
icacls . | Select-String Mandatory
```

若输出里出现 `Mandatory Label\Low Mandatory Level`，就是它。
（显示 `Medium` 是正常的：继承来的完整性级别不显示这一行，只有被显式设过才打印出来。）

修复（改回 Medium；**必须带 `/T` 递归，且要先给自己 WO 权限**，否则子项会 `Access denied`）：

```cmd
icacls . /grant "%USERNAME%:(OI)(CI)(WO)" /T /C /Q
icacls . /setintegritylevel (OI)(CI)Medium /T /C /Q
icacls . /remove:g "%USERNAME%:(WO)" /T /C /Q
```

改完 `icacls .` 应显示 `Mandatory Label\Medium Mandatory Level`，弹窗消失。
临时绕过：用 `cmd /c run.cmd` 或 `Start-Process run.cmd` 启动（走 CreateProcess，不经 Explorer 的 ShellExecute 校验），
或直接双击 `PdxLauncher.App.exe`。

**跑 `.cmd` 时冒出 `'行文件:' is not recognized as an internal or external command` 一类假报错**
→ 这是 cmd.exe 的**批处理文件字节撕裂**，不是脚本写错了，也不影响构建结果（构建照样
`exit 0`，自检照样通过），只是观感很差。触发条件是「批处理文件里含非 ASCII 字节」+
「控制台起始代码页不是 UTF-8（例如从 `chcp 936` 的窗口里跑）」+「文件刚被写入就立刻执行」
三者叠加。cmd.exe 会按当前代码页逐字节重读批处理文件，中文多字节序列被拆开时字节组合会
吞掉行边界，把下一行 echo / rem 撕成碎片，于是冒出一堆莫名其妙的「不是内部或外部命令」。

> 实测：UTF-8 + `chcp 65001` 的老版 `build.cmd` 在大样本里约 0.7% 失败，且**第一次运行
> 最容易中**；从 `chcp 936` 的控制台起跑时概率显著升高。纯 ASCII 脚本在任何代码页下
> 200/200 全对。GBK 存盘也能做到 200/200，但文件编码会被编辑器 / git 改回 UTF-8，
> 不稳定，因此不采用。

**本项目的做法**：所有顶层 `.cmd`（`build.cmd` / `build-debug.cmd` / `run.cmd` /
`run-debug.cmd` / `publish.cmd`）**保持纯 ASCII**，中文提示全部集中在
`scripts\messages.txt`（UTF-8 无 BOM，格式 `KEY:文本`），由 `scripts\msg.cmd` 查表打印：

```bat
call "%~dp0scripts\msg.cmd" B_STEP1
call "%~dp0scripts\msg.cmd" P_EXE %TARGET%\PdxLauncher.App.exe
```

**改脚本时请遵守这条**：不要在顶层 `.cmd` 里直接写中文，也不要在 `rem` 行里写
`<` `>` `|` 这类 cmd 元字符（即使是 ASCII 的 rem，中文行里出现它们也会加剧撕裂）。
需要新提示就往 `messages.txt` 里加一个 `KEY:文本` 行。

## 8. 界面规范（强制）：4px 间距与圆角

> 来源：WinUI Gallery 的 **Spacing** 页（`WinUIGallery/Samples/Spacing/SpacingPage.xaml`）与 Microsoft Learn 的同一份表述
> （<https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics>）：
> **A best practice in design is to use a 4px grid. This means that any spacing or sizing should be a multiple of 4.**
> 完整对照表、映射历史与新增 UI 检查清单见 [UI-SPACING.md](UI-SPACING.md)。

### 8.1 必须

1. `Padding` / `Margin` / `Spacing` / `RowSpacing` / `ColumnSpacing` 的**每一个分量**都必须是 **4 的倍数**（`8,4` 合法，`6,1` 不合法）。
2. 常用值优先取 `4 / 8 / 12 / 16 / 24`（官方语义：4 紧凑排布、8 控件之间、12 控件与标题、16 列表与卡片、24 内容分区）。
3. 页面根 `Padding` 用 `16 / 20 / 24` 三档；**不采用官方 36**（1040 宽的最小窗口下会压缩列表与工具条，理由见 UI-SPACING §3）。
4. `CornerRadius` 只允许 `0 / 4 / 8`：卡片 / 面板 8，小控件 4。
5. 1px 细线用 `Width` / `Height` 表达，不要写成间距 `1` 去凑居中；`BorderThickness` 是描边宽度，不参与本规则。

### 8.2 自动校验

- `tests/PdxLauncher.Checks/SpacingChecks.cs` 扫描全部 XAML（先剥掉 `<!-- -->` 注释）与手写 UI 代码，
  任何非 4 倍数直接失败，失败信息带文件名与行号。
- 例外只能加在检查器白名单里，并在 [UI-SPACING.md](UI-SPACING.md) §3 写明理由（目前只有 Markdown 引用块的
  `BorderThickness="3,0,0,0"` 这一条，且它本就不算间距）。
- 表达式型 `Thickness`（如 `16 + item.Level * 20`）无法静态判断，新增时必须人工确认每个常量是 4 的倍数。

### 8.3 为什么是 4

- 4 的倍数在 100% / 125% / 150% / 200% 缩放下都落在整数物理像素上（4 DIP = 4 / 5 / 6 / 8 px），不会出现半像素模糊边。
- 间距按语义取值（4/8/12/16/24）后，同类元素在不同页面自动一致，review 时不用逐页比像素。
