# PdxNext —— Paradox 启动器的 WinUI 3 重写（自用）

一个用 **WinUI 3 / Windows App SDK** 重写的 Paradox 游戏启动器，目标是替代 P 社官方启动器
（Electron + 内嵌 sqlite 的那套），做到**秒开、可离线、不碰官方目录**。

> **交接与任务拆分见 [docs/HANDOFF.md](docs/HANDOFF.md)** —— 当前进度、环境限制、待办任务（T1–T8）都在里面。

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

```bat
build.cmd        :: Release 构建 + 跑自检（首次约 1 分钟，之后增量约 15 秒）
run.cmd          :: 启动 Release 版
```

调试用：

```bat
build-debug.cmd
run-debug.cmd
```

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
| 仅启动 | Prison Architect（老 schema，formatVersion 语义不同） | 只负责拉起 exe（--launched-from-launcher）；模组与 DLC 由游戏本体管理，启动器不读不写它的任何数据 |
| 未适配 | 未登记 gameId 的目录 | 只展示，卡片标「未适配」 |

### 加载文件（启动器唯一必须写对的东西）

按 `formatVersion` 分两套格式，都是**紧凑 JSON、UTF-8 无 BOM、结尾无换行**：

| formatVersion | 文件名 | 键 |
| --- | --- | --- |
| 0 | `dlc_load.json` | `disabled_dlcs`（相对 dlcPath 的 POSIX .dlc 路径）、`enabled_mods`（mods.gameRegistryId） |
| 1 / 1.1 | `content_load.json` | `disabledDLC`（`[{paradoxAppId}]`）、`enabledMods`（`[{path}]`）、`enabledUGC`（`[{path}]`） |

游戏自己读这个文件，启动器**不写** `playsets.json` / `game_data.json` / `continue_game.json`。

### 播放集 / Mod / DLC
直接读写各游戏游戏数据目录下的 `launcher-v2.sqlite`（knex schema：`mods`、`playsets`、
`playsets_mods`、`playsets_dlcs`、`key_value_pairs`），用 **P/Invoke `winsqlite3.dll`**，
不依赖任何 NuGet sqlite 包（本项目全离线构建）。

- 播放集页：**两种显示方式**，在「设置 → 界面 → 播放集页面显示方式」切换（存 `data\settings.json`（程序目录下）
  的 `playSetPageDisplayMode`，默认 `officialLauncher`）：
  - `officialLauncher`（原版启动器布局）：顶部**只有一行**工具条——播放集下拉（**只切换查看/编辑目标，不改变激活状态**，
    选中项显示集名与「● 激活」标记）+ 新建 / 重命名 / 删除 ｜ 分隔线 ｜「激活播放集」（把当前选中的播放集写成游戏激活集，
    激活后按钮变「已激活」并置灰）+「启用全部」「禁用全部」｜ 中＝搜索框，右＝「添加模组」多选对话框（候选来自 Mod 库）；
    原先第二行的「当前播放集名 / 计数 / 已激活：X」摘要已删除，这些信息改由下拉框选中项与状态栏承载；
    下方**只显示该播放集的 mod**：逐项启用禁用、上下移动排序（上移/下移一位）、**点行首序号直接输入目标位次**（回车生效，
    Esc 或点别处取消，其余行依次顺移而非互换；**输入的数字大于总行数时视为置底**，不会报错；
    搜索状态下位次编辑与上下移动一并置灰）、从播放集移除。
    所有改动**自动保存到 sqlite**（`playsets_mods` / `playsets_dlcs`，写完回读比对）；**只有当前播放集是激活集时才写游戏加载文件**，
    编辑未激活集时状态栏会提示「未激活，游戏加载文件未改动」。**没有**「保存」「保存并启动」按钮。
    删除：删非激活集不改变游戏配置；删激活集时先激活接替者、再删除并写入接替者的加载文件；只剩一个激活集时拒绝删除。
  - `placeholder`（留空）：整页只显示占位提示，回设置页切回即可恢复原版布局。
- Mod 库页：只读展示 `mods` 表全量（即官方启动器的「MOD 库」），每行显示缩略图、名称、大小、
  订阅日期、mod **自己声明的支持版本**（`requiredVersion`，即 .mod 的 `supported_version` /
  metadata.json 的 `supported_game_version`，原样显示不做换算）、以及「在 N 个播放集中」（不在任何播放集时不占位）；
  本地 mod / 状态异常有警告标记；行尾**四个图标按钮**分别是打开工坊详情页（复用创意工坊那个详情子页）、
  打开本地文件夹、在 Steam 中打开、在浏览器中打开（拿不到目录或工坊编号时对应按钮置灰）；支持搜索（名称 / 注册 ID / 目录）、
  按名称 / 大小 / 订阅时间排序、刷新；数量徽标 = `SELECT COUNT(*) FROM mods`。
  行操作的地址由 `SteamWorkshop` 拼装：创意工坊 id 优先取 `mods.steamId`，为空时回退从
  `mod/ugc_<id>.mod` 解析；打开动作是 `Process.Start` + `UseShellExecute`（资源管理器 / `steam://` / https），
  不经 `IFileSystem`，也不受 `OnlineHosts` 白名单约束（那是本进程 HTTP 的白名单）。
  「打开详情页」这一路先把行翻译成 `ModWorkshopRef`（平台 + 编号：Steam 用上面那套判定，
  CS2 的 `pdx:<sourceId>` 取数字部分），再交给 `WorkshopRowViewModel.FromLibrary` 造一条详情行导航进
  `WorkshopDetailPage`；编号解析不出来的行按钮置灰，提示文案直接说明为什么（本地 mod / 没有工坊编号）。
  详情子页的返回按钮回到**发起方那一页**（`WorkshopDetailNavigationRequest.ReturnTag`），
  左栏选中项也停在发起方。
- DLC 页：纵向列表，**左侧固定 208×117 缩略图，右侧标题 + 两行简介**（文字再长也不会把图片压小，超出两行省略、鼠标悬停看全文），逐项启用禁用，写 `playsets_dlcs` 并同步加载文件。简介优先用本地描述（Victoria 3 的 `.dlc.json` 自带 `description`），CK3 / Stellaris 的 `.dlc` 没有简介字段，联网时按 Steam appid 查商店简介补全并缓存 7 天（设置页可关）

播放集 / Mod 库 / DLC 三页**只对「完整适配」的游戏生效**；选中「仅启动」的游戏时，左侧这三项置灰、左栏底部常驻一行说明（禁用的导航项收不到指针事件，所以不用悬停提示），主页的播放集下拉隐藏、改显示一行「由游戏本体管理」的说明，不会去读它的 sqlite，也不会扫 DLC 目录。

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
`data\theme-cache\<gameId>-<assetKey>-<urlHash8>.<ext>`（程序目录下），TTL 5 天、过期先返回旧图再后台刷新；
游戏发新版本 ⇒ 服务端换图 ⇒ URL 变 ⇒ 本地文件名跟着变（顺带让 WinUI 的图片缓存自动失效）。
实测 CK3 拿到的背景图与官方启动器 `cache\ck3-background` **sha256 完全一致**，
而它与安装目录 `launcher\assets\app-background.png` 是两张完全不同的图。
主页的「继续」「开始游戏」两个按钮按官方「主题按钮」的语义渲染：**主题背景图铺满 232×66 的按钮**（官方 14.5rem × 4.1667rem），
**主题 `button.color` 是文字色、不是底色**，`buttonHover.color` 是悬停文字色，字体 / 字号 / 字重同样取自主题。
悬停叠 8% 白并换成悬停文字色、按下缩到 0.85、禁用降到 55% 透明；没有主题图的游戏退回「幽灵按钮」（白 5% 底 + 官方式 2px 内嵌白高光，不额外描外边框）。
CK3 是深炭纹理 + 米色字、Victoria 3 是紫红大理石 + 金珠边框 + 近白字、Stellaris 是暗青纯色 + 白字，切游戏即随之切换；其余控件保持系统 Fluent 样式。

### 界面
`TitleBar` + `NavigationView` + `Frame`，6 个页面：游戏库 / 当前游戏 / 播放集 / Mod库 / DLC / 设置。
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

---

## 3. 目录结构

```
PdxLauncher.sln
build.cmd / build-debug.cmd / run.cmd / run-debug.cmd
publish.cmd                    :: 构建 + 自检 + 发布到 dist\PdxNext\（可传目标目录）
scripts/
  msg.cmd                      :: 顶层 .cmd 取中文提示用的小助手（纯 ASCII）
  messages.txt                 :: 全部中文提示，UTF-8 无 BOM，KEY:文本 格式
Directory.Build.props          :: 统一可空/语言版本/告警策略
NuGet.config                   :: <clear/>，全离线
src/
  PdxLauncher.Core/            :: 165 个 .cs，无任何 WinUI 依赖，可单测
    Abstractions/              :: 22 个接口（IGameCatalog / IPlaySetRepository / IGameLauncher / INewsFeedService ...）
    Contracts/                 :: 不可变 record 数据模型
    Games/                     :: Steam 库探测、launcher-settings 解析、游戏目录展开
    Steam/                     :: registry + libraryfolders.vdf（Valve KeyValues 解析）
    Playsets/                  :: winsqlite3 P/Invoke + 播放集仓库
    LoadFiles/                 :: 两套加载文件格式的写入与回读比对
    Launching/                 :: 进程启动、continue_game.json 读取
    Signatures/                :: dlc_signature 重算（默认关闭）
    Themes/                    :: 主题 JSON 解析 + HSL/RGB 颜色
    Online/                    :: GraphQL 元数据、素材下载、代理（三模式解析 / 系统代理 / 环境变量）、DLC 简介、官方公告
    Parsing/                   :: Clausewitz 与 Valve KeyValues 解析器
    Patch/                     :: 补丁素材（内嵌 loader + cream_api.ini）、目标目录判定、打补丁 / 还原
  PdxLauncher.App/             :: 86 个 .cs + 13 个 .xaml，WinUI 3 层
    Assets/                    :: AppIcon.ico（9 帧 16→256）+ 8 张模板同名 PNG，全部由 tools/gen-app-icon.py 生成
    Views/                     :: GameLibraryPage / HomePage / ModsPage（播放集） / ModLibraryPage（Mod库） / DlcPage / SettingsPage
    ViewModels/                :: 16 个 VM（全部不含 Microsoft.UI.Xaml 类型）
    Services/                  :: AppServices 装配、ThemeApplier、BitmapLoader ...
tests/
  PdxLauncher.Checks/          :: 40 个检查类，纯 console 自检（无 xunit，因为离线）
  fixtures/                    :: 抓下来的真实数据样本
docs/
  HANDOFF.md                   :: 交接文档：已完成项 / 已知缺陷 / 下一步
  DATA-FORMATS.md              :: 所有数据格式的真机实测记录（262 行）
  app-icon.png                 :: 应用图标接触表（深/浅底，16→256，由生成器写出）
  screenshot-home.png          :: 当前游戏（整窗主题背景）
  screenshot-library.png       :: 游戏库
  screenshot-playsets.png      :: 播放集
  screenshot-mod-library.png   :: Mod 库
  screenshot-dlc.png           :: DLC
  screenshot-settings.png      :: 设置
tools/
  manual-build.py              :: 完全绕过 MSBuild 的 Roslyn + XamlCompiler 手工构建链
  package-app.py               :: 打包脚本
  gen-app-icon.py              :: 应用图标唯一生成源（Pillow + numpy 自绘 → ico / 8 张 PNG / 接触表）
```

> **顶层 `.cmd` 必须保持纯 ASCII**。中文提示一律写在 `scripts\messages.txt`，由
> `scripts\msg.cmd <KEY> [附加文本]` 打印。原因见第 7 节同名条目：cmd.exe 按当前代码页
> 逐字节重读批处理文件，非 ASCII 字节在 cp936 等代码页下会把行边界撕开，导致偶发的
> `'行文件:' is not recognized as an internal or external command` 之类假报错。

**303 个 .cs + 13 个 .xaml**（不含 `obj/` 生成物；2026-10-29 实测：Core 165 / App 86+13 / Imaging 9 / Checks 43 —— Checks 的 43 个文件是 40 个检查类 + Program + ChecksContext + GlobalUsings）。

---

## 4. 自检

```bat
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"
```

输出 `检查类: 40，失败: 0，断言失败总数: 0` 即通过。覆盖：Steam 探测与
launcher-settings 解析、两套加载文件格式的写入/回读、播放集与 sqlite、Mod 库只读读取
（mods 全量、size / subscribedDate 解析、播放集归属、requiredVersion 兼容判断）、创意工坊 id 解析、启动参数拼装、路径大小写不敏感处理；
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
「可编辑性与邮箱无关、可提交性＝可编辑且邮箱非空」这条死锁回归钉子。

---

## 5. 隔离性（重要）

| 东西 | 位置 | 权限 |
| --- | --- | --- |
| 官方启动器目录 | `%LOCALAPPDATA%\Programs\Paradox Interactive\launcher` | **只读，永不写入** |
| 官方 userSettings.json | 同上 | **只读** |
| 游戏数据目录 | `%USERPROFILE%\Documents\Paradox Interactive\<游戏>` | 读写（写 sqlite + 加载文件） |
| 本启动器自己的数据 | `<PdxNext 程序目录>\data\`（**不写 %LOCALAPPDATA%**） | 全部自己的 |
| 启动器设置 | `data\settings.json`（含 `playSetPageDisplayMode` / `modThumbnailSize` / `modThumbnailCacheDays` 等） | 同上 |
| DLC 简介缓存 | `data\dlc-descriptions.json` | 同上（TTL 7 天） |
| 主题素材缓存 | `data\theme-cache\` | 同上（TTL 5 天） |
| 官方公告缓存 | `data\news-cache\` | 同上（TTL 6 小时） |
| Mod 缩略图缓存 | `data\mod-cache\` | 同上（保留天数可配，默认 30 天） |
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
以 `.migrated-from-localappdata` 标记保证幂等）；设置页「数据目录」卡片可以打开该目录或
删掉旧的 C 盘副本。`publish.cmd` 覆盖 `dist\PdxNext` 前会把 `data` 搬到
`dist\PdxNext.__data__`、发布完搬回，所以重新发布不会吞掉设置与缓存。

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

---

## 7. 排错

**构建输出"生成失败 / 0 个错误"** → 忘了 `-m:1`，见第 1 节。

**启动后白窗口 / 闪退** → 看 `%LOCALAPPDATA%\PdxNext\logs\pdxnext.log`。
如果日志目录根本没建出来，说明权限不足（例如在低完整性级别进程里跑）。

**找不到游戏** → 确认游戏装在某个 Steam 库里，或在官方启动器的独立库路径里，且安装目录下真有
`launcher-settings.json`。改完路径点右上角「刷新」；日志里会打印扫描结果（`游戏发现完成，共 N 个`）。

**Mod 列表为空** → 确认游戏数据目录下有 `launcher-v2.sqlite`（至少用官方启动器成功启动过一次游戏）。
若是 Prison Architect，这是预期行为：它是「仅启动」等级，左侧播放集 / Mod 库 / DLC 本来就置灰。

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
