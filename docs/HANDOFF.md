# 交接文档（HANDOFF）—— 下一轮对话从这里开始

> 本文件由上一轮会话在用户要求「停止，留下文档给下个对话使用，需要任务拆分」时写入。
> 状态时间：2026-10-25 22:30（本机时区 Asia/Hong_Kong）。
> 工作区：G:\Work\paradox启动器 —— **已是 git 仓库**（2026-10-27 初始化）。改代码前先 `git switch -c <分支>`，
> 每完成一步就 `git commit`；回滚用 `git restore` / `git revert`，不要再复制 backup 目录。
> 历史备份（.scratch\rework\backup*）只作为改造前后的存档，不再新增。

---

## 0. 一句话现状

启动器外壳与主页已从「照官方 Paradox 启动器截图逐像素自绘」**改成 WinUI 3 标准控件 UI**（用户明确要求），
Release **0 警告 0 错误**，自检 **16 个检查类 / 0 失败**（2026-10-27 14:40 实测；本轮新增 `IconChecks`。历史上并发会话加过好几个类，实际以运行时打印的 `检查类: N` 为准），运行时 UIA 树已确认是 TitleBar + NavigationView + Frame 结构。
**2026-10-26 已完成：窗口置顶开关（§0.2）+ 整窗背景「方案 B」（§0.3）**——游戏主题背景图现在连标题栏条和左侧
NavigationView 一起铺满整窗，六个页面全部浮在图上（五页统一纱 + 卡片/列表玻璃底衬），主页那条 260 高、被裁掉一半的
主题横幅已删除。剩下的活：Light / HighContrast 真机切主题跑一遍（§0.3 唯一视觉盲区）、真机复验 settings.json 与置顶持久化、
交互回归（启动 / 继续 / 播放集切换）、README 与截图同步（截图已换、README 计数已更）。
**2026-10-26 追加：标题栏文案已收敛为「PdxNext · 游戏名 · 版本 …」，不再显示「已发现 N 个游戏」（§0.4）。**
**2026-10-26 17:55 追加：窗口置顶入口已从「左栏底部图钉」移到标题栏右上角 `TitleBar.RightHeader`（紧贴系统按钮、随 DPI / 最大化自适应），并顺带修掉官方 RightInset DPI 缺陷 —— 详见 §0.8；§0.2 的「三个入口」与 §0.2.1 整节已作废。**
**2026-10-27 00:35 追加：聚焦态右上角 ─ □ ✕ 不可见已修 —— 根因是 `AppWindowTitleBar` 按钮颜色默认取浅色主题值
（聚焦纯黑、失焦 #606060），在深色标题栏上正好相反；现按主题显式上色（`TitleBarPalette` + `TitleBarTheme`），
`DWMWA_USE_IMMERSIVE_DARK_MODE` 这条路已实测无效，详见 §0.9。**
**2026-10-26 16:20 追加：五个顶层 `.cmd` 已全部改成纯 ASCII，中文提示集中到 `scripts\messages.txt`
+ `scripts\msg.cmd` 查表打印，彻底消除偶发的 `'行文件:' is not recognized` 假报错（§0.5）。
以后改脚本请继续保持 ASCII，别再往 `.cmd` 里写中文。**
**2026-10-26 17:30 追加：主页已按官方启动器骨架重排（居中字标 / 继续+开始 / 播放集 / 统计三卡 / 最近存档 / 底部公告卡），
两个主按钮按游戏主题染色，底部接入官方 Braze content cards（可选联网）。这部分代码已用离线手工链编译通过（Core/App 均 0 错 0 警告），但没跑过 `dotnet build` 与真机 UI，见 §0.6。**
**2026-10-27 00:30 追加：主页两个主按钮的「主题染色」之前是错的（把 `button.color` 当底色，导致深底深字）——现已按官方语义重写：按钮图当底、`color` 当文字色、232×66、悬停/按下/禁用三态，并新增素材提取工具 `tools/extract-launcher-assets.py`，见 §0.10。**
**2026-10-27 01:30 追加：主页按用户三条意见返修 —— 删掉「没用的」统计三卡与最近存档条、启动区去掉外层卡片框、公告卡改回官方实现（纯图无框、固定 451/246×230、悬停压暗、深链不再灰化），见 §0.11。**
**2026-10-27 02:35 追加：主页三处细节按用户意见修正 —— 播放集下拉强制朝下（`MaxDropDownHeight` 按窗口剩余高度动态收窄）、删掉右上「刷新」按钮、版本号从页头移到公告栏正上方左对齐，见 §0.12。**
**2026-10-27 03:20 追加：上一轮的 MaxDropDownHeight 修复无效 —— 已换成 `DropDownButton + Flyout（BottomEdgeAlignedLeft）` 保证朝下，版本号照官方 Victoria 3 移到广告卡正上方右对齐、去掉「版本」前缀；WinUI 源码证明 ComboBox 的 MaxDropDownHeight 改不了定位模型，见 §0.13。** 已用 UIA 冒烟实测：弹层在按钮下方 8 DIP 展开、版本号右边缘与公告行对齐。
**2026-10-27 04:10 追加：播放集按钮与下拉弹层做了视觉收边 —— 按钮去系统外框、圆角 4，弹层圆角 8 / 去 1px 硬边、列表内缩 4 DIP 并关掉选中项的系统焦点白框（`UseSystemFocusVisuals=False`），解决了「边框是直角、像没美化」的问题，见 §0.13.7。**
**2026-10-27 04:20 追加：播放集页工具条收成一行（删掉第二行「集名 / 计数 / 已激活：X」摘要，「激活播放集 / 启用全部 / 禁用全部」并入第一行，忙碌转圈移到状态栏右端），并修掉进页面 / 重载后下拉框空白；`MinimumWindowWidth` 900→1040，见 §0.14。**
**2026-10-27 02:30 追加：整窗背景新增「背景显示方式」五档（覆盖 / 填充 / 完整模糊底 / 完整纯色边 / 居中原始像素）、Mod 缩略图三档（32 / 48 / 72 DIP），导航顺序把「当前游戏」放到最前 —— 详见 §12 与 `docs/DATA-FORMATS.md` §13。**
**2026-10-27 14:40 追加：应用图标从零做起 —— 石墨圆角方 + 金「P」（碗里放播放三角），`tools/gen-app-icon.py` 是唯一生成源，接进 exe 资源（`<ApplicationIcon>`）与运行期窗口（`AppWindow.SetIcon`），并替换 `Assets\` 里 8 张模板占位 PNG；dist 仍 538 文件，见 §0.15 与 §14。**
**2026-10-27 14:50 追加：设置页「内容和滚动条重叠」已修 —— 根因是 WinUI 3 的滚动条是**浮层**，贴着 `ScrollViewer` 右边缘画在卡片描边/圆角上；现给滚动内容与状态栏各留 14 DIP 右侧通道，滚动条改落在通道里。真机三态截图 + 像素测量见 §0.16。**
**2026-10-27 15:10 追加：设置页左侧分节导航从「纯文字 ListView」换成官方 `NavigationView`（带 `SymbolIcon`）—— 选中指示条到文字的间距从 ≈8 DIP 变成 ≈41 DIP，见 §0.17。**

## 0.17 最新一轮（2026-10-27 15:10）：设置页分节导航 —— 指示条与文字太近

### 0.17.1 现象（用户原话 m00398，附左栏截图）

「这个选择状态的条似乎和字太近了，是不是自己写的ui，是否有官方ui库解决」——左栏「外观」被选中时，
3 DIP 的选中指示条紧贴文字，看起来像两个元素挤在一起。

### 0.17.2 结论（读代码 + 量像素，不是猜）

- 指示条**不是自绘**：它是 `ListViewItemPresenter` 自带的选中指示器（3 DIP，贴在条目左缘）。
  挤的原因是条目**只有文字**（`<ListViewItem Padding="8,10">` + 一个 `TextBlock`），文字落在 8 DIP 内边距处
  ⇒ 条右缘到文字只有 12 px（150% DPI，≈8 DIP）。
- WinUI 的导航规范里，指示条右侧留的是**图标位**（条 → 图标 → 文字）。官方控件就是 `NavigationView`，
  而且主窗口左栏 `MainNavigation` 早就在用；`CommunityToolkit.WinUI.Controls.SettingsControls` 只有
  `SettingsCard` / `SettingsExpander`，**没有导航控件**，所以那边帮不上。
- 给 `ListViewItem` 加大 `Padding` 也能拉开，但条和字之间会留一段空白，不符合导航规范 ⇒ 换控件。

### 0.17.3 改动（已落盘）

- `src/PdxLauncher.App/Views/SettingsPage.xaml`：Col0 的 `ListView` 换成 `NavigationView`
  （`AutomationId` 沿用 `lstSettingsSections`，`PaneDisplayMode=Left`、`OpenPaneLength=216`、
  `IsPaneToggleButtonVisible=False`、`IsSettingsVisible=False`、`IsPaneOpen=True`、`Background=Transparent`）；
  页头（标题 + 当前游戏名）移进 `NavigationView.PaneHeader`；六个 `NavigationViewItem` 用 `Tag="0".."5"`
  携带分类序号，图标依次 `Font / Play / Folder / Globe / Save / Setting`；外层 `Grid` 补上列定义 216/\*
  与行定义 \*/Auto，`ScrollViewer` 仍是 Row0/Col1 的**兄弟**节点（14 DIP 滚动条通道不变），
  状态栏 `Border` 放 Row1 并 `ColumnSpan="2"`。
- `src/PdxLauncher.App/Views/SettingsPage.xaml.cs`（已在 `1f5f23f`）：`OnSectionChanged(NavigationView,
  NavigationViewSelectionChangedEventArgs)` + `CurrentSectionIndex()` 解析 `Tag`，`ShowSection` 逻辑未变。
- 提醒：`PaneBackground` 在 WinUI 3 的 `NavigationView` 上**不存在**（XamlCompiler `WMC0011`），要透明只能设 `Background`。
- `docs/SETTINGS-REWORK.md`：结构图、新增「分节导航控件」条目、切分节说明、新的量测小节。

### 0.17.4 验证（Release，真机截图 + 像素测量 + UIA）

- `cmd /c build.cmd`：0 警告 0 错误；`检查类: 16，失败: 0，断言失败总数: 0`。
- `.scratch/rework/navgap-settings.png` + `navgap-probe2.py`（1800×1200，150% DPI，行带 y=190..246）：
  指示条 `x=374–378`（5 px ≈ 3 DIP）→ 图标块 `x=392–415`（24 px = 16 DIP，间隔 14 px ≈ 9 DIP）
  → 再空 24 px（16 DIP）→ 文字首列 `x=440`。**条右缘到文字 62 px（≈41 DIP）**；改前 12 px（≈8 DIP）。
- `.scratch/rework/verify-settings2.ps1`：appearance 46 / launch 39 / paths 46 / online 39 / cache 44 /
  advanced 38（比旧基线各 +4，来自 `NavigationView` 的 `PaneRoot` / `PaneTitleTextBlock` /
  `PART_TitleText` / `PART_SubtitleText` 节点）。

### 0.17.5 待办与盲区

- `verify-settings2.ps1` 的 `online` 期望表仍是迁移前的 IA：6 个「数据 / Mod 缩略图」控件已随 `18cab88`
  迁进「缓存」节，所以该节 `missing` 会报 6 个 id —— 是脚本过期，不是回归，需并发会话更新期望表。
- `1f5f23f` 提交里只有 `.cs` 是 `NavigationView` 版、XAML 还是 `ListView`：单独 checkout 该提交**编译不过**，
  本轮 XAML 提交补齐了这个缺口。
- 键盘 / 手柄导航（Tab 进左栏、方向键切分类、Enter 激活）只做了控件层面的对齐（`NavigationView` 自带），
  没有逐项实测。
- 同一工作区同时有另一个会话在改 `MainWindow.xaml.cs`、`Services/{AppServices,BitmapLoader,XamlImage}.cs`、
  `ViewModels/LibraryViewModel.cs`、`Views/HomePage.xaml.cs`、`Services/ThemeImageFlattener.cs`、
  `GameCatalog.cs`、`tests/PdxLauncher.Checks/GamesChecks.cs`、`InstallSignatureDetector.cs` —— 不属本轮，
  提交时按路径限定，别顺手 `git add -A`。

### 0.17.6 备份

- 仓库已 git 化，改前的 `ListView` 版 XAML 可从 git 历史取；本轮没有新增 backup 目录。

## 0.16 最新一轮（2026-10-27 14:50）：设置页「内容与滚动条重叠」—— 浮层滚动条压在卡片描边上

### 0.16.1 现象（用户原话 m00003，附右缘竖条截图）

设置页外观分类右侧有一条通栏亮竖线，正好贴在卡片右边框上；截图里「联网缓存 / 上次检查 / 立即检查 /
四个下拉 / 开关」的卡片右边缘全被这条线压着。

### 0.16.2 根因（像素取证，不是猜）

- WinUI 3 的 Fluent 滚动条是**浮层指示器**：它贴着 `ScrollViewer` 的右边缘绘制，**不占内容宽度**。
  所以 `VerticalScrollBarVisibility="Visible"` 既不会给内容留白，也不能阻止重叠 —— 改版时「常显可避免宽度抖动」
  的说法是错的（详见 `docs/SETTINGS-REWORK.md` 问题 #1 的修正）。
- 改版初版的内容 `StackPanel` 没有任何右侧留白，卡片一路撑到 `ScrollViewer` 右边缘 ⇒ 轨道画在卡片的
  1 px 描边与 8 px 圆角上，指针悬停 / 滚动时肉眼可见「内容与滚动条重叠」。
- 旧截图里看不到滚动条，是因为指针不在滚动区时它会自动淡出；这也解释了为什么「看静态截图没问题、一动鼠标就出来」。

### 0.16.3 改动（已落盘）

- `src/PdxLauncher.App/Views/SettingsPage.xaml`：滚动内容的 `<StackPanel Spacing="12">` 加 `Margin="0,0,14,0"`，
  页面状态栏 `Border Grid.Row="1"` 同样加 `Margin="0,0,14,0"`（保持整页右边缘对齐），两处各带说明注释。
  14 DIP = 12 DIP 滚动条通道 + 2 DIP 余量；**只给内容留白**，给 `ScrollViewer` 加 Margin 只会把轨道跟着卡片一起往里挪。
- `docs/SETTINGS-REWORK.md`：结构图补上通道、新增「滚动条通道」条目、修正问题 #1 的错误归因。

### 0.16.4 验证（Release，真机截图 + 像素测量）

- `cmd /c build.cmd`：16 个检查类、失败 0、断言失败 0；`python tools/gen-i18n.py --check`：499 个键合法。
- `.scratch/rework/verify-scrollgutter.ps1` + `.scratch/rework/gutter-probe.py`（1800×1200，150% DPI）：
  卡片描边 `x=1736–1737`、滚动条轨道 `x=1750–1753` ⇒ 留空 12 px；空闲 3.6 s 后同列无亮像素。
- `.scratch/rework/verify-narrow-gutter.ps1`（1000×700）：描边 `936–937`、滚动条 `951–952`，间隙 13 px；5.2 s 后消失。
- `.scratch/rework/verify-settings3.ps1`：被操作控件前后恒为 `1302,940,492x60`（旧基线 `1323,940,492x60` 左移 21 px）。
- 六分类 UIA 计数：42 / 35 / 42 / 41 / 34 / 34，`missing=[]`。

### 0.16.5 盲区

- 只修了设置页。主页 `HomeScrollViewer`（`VerticalScrollBarVisibility="Auto"`）与其它页的 `ScrollViewer` 同样是
  无留白浮层滚动条，但它们的内容是满宽卡片/列表，是否需要同样通道没和用户确认过，没动。
- 触摸 / 手柄下的滚动条宽度与 hover 一致（未单独实测）。

### 0.16.6 备份

- 改动前的三份文件在 `.scratch\rework\backup-20261027-scrollgutter\`（`SettingsPage.xaml`、`SETTINGS-REWORK.md`、`HANDOFF.md`）。
- 仓库已 git 化，本次代码与文档改动已提交；后续回滚走 `git restore` / `git revert`。

## 0.15 最新一轮（2026-10-27 14:40）：应用图标 —— 从零做一套，接进 exe / 窗口 / 任务栏

> 本节压过「图标以后再说」的旧结论。图标细节速查另见 §14；README 的「界面 / 应用图标」小节是给使用者的版本。

### 0.15.1 起点：整个软件其实没有图标

开工前实测（不是猜）：

- `src/PdxLauncher.App/Assets/AppIcon.ico` 是 **WinUI 模板占位图**（灰圆角方框 + 圆圈 + X，370,070 B，内含 16/32/48/64/128/256 六帧），MD5 `c2838761752b2161371390e41154ed92`。
- 同目录 8 张模板 PNG 也是同一张占位图（88 / 300 / 50 / 48 / 24 / 48 / 1240×600 / 620×300）。这些是 MSIX 模板遗留物，未打包应用运行时不读它们，只是被 csproj 的 `Assets\**\*.*` 顺手 publish 进产物。
- csproj **没有 `<ApplicationIcon>`**，源码里 **没有任何 `AppWindow.SetIcon` / `Window.Icon`**；从 `dist\PdxNext\PdxLauncher.App.exe` 用 `ExtractAssociatedIcon` 取出来的是 .NET 默认通用图标（灰窗口 + 蓝方块）⇒ 任务栏、Alt-Tab、文件管理器里全是「无图标」。
- `ThemeSpec` / `ThemeSettingsDto` 早就有 `AppIcon` / `AppTaskbarIcon`（官方主题的 `app-icon.png`，Victoria 3 是 512×512），`ThemeApplier` 也把它装进 `ThemeVisuals.AppIcon`，但**全仓无人消费**（死字段）。

环境限制：机器上没有任何 SVG 栅格化工具（无 cairosvg / skia / svglib / wand、无 magick / inkscape / rsvg-convert），所以没有 SVG 母版，图标直接用 Pillow + numpy 画。

### 0.15.2 设计（一句话：深色任务栏上要认得出，16px 也要）

- **石墨炭圆角方底 + 金色「P」，P 的碗（计白）里放一枚同色系金色播放三角**：P = Pdx，播放三角 = 启动器 / Next。
- 底色 `#2E2E36 → #1B1B1F → #0E0E12` 纵向渐变 + 轻微暗角；金色字标 `#FFE3A3 → #F2C14E → #C08A2B`。
- ≥32px 画 1px 白 13% 描边（浅色任务栏上也有边界），≥48px 加柔和投影；16/20/24px 光学加粗（`_weight` 1.08 / 1.04）且不画投影。
- 不用 Paradox 的任何商标或官方图标，避免和官方启动器混淆。
- 走过的弯路（别再走）：第一版是「只把播放三角从 P 上挖掉」——碗是实心金块，整体读不出 P；三角的圆角还因为按 `d = r / sin(θ/2)` 沿角平分线内缩而长出孤立圆洞。最终做法是 `mark - counter` 得到真正的 P 环、再把金色三角放进计白；圆角三角改成**精确圆弧采样**（`_rounded_polygon`，内缩顶点即圆心，取切点之间的短弧）。

### 0.15.3 落盘内容

| 文件 | 内容 |
| --- | --- |
| `tools/gen-app-icon.py` **（新）** | 唯一生成源（约 517 行）。`UNIT=1024` 设计坐标 → 超采样（≤32px 用 S=16，其余 S=8）+ LANCZOS；numpy 做底色/字标两处渐变与暗角；`_rounded` / `_ellipse` / `_rounded_polygon` / `_rounded_triangle_mask` 造蒙版；手写 ICO 容器（`_bmp_frame` / `_png_frame` / `ico_bytes`）。`--preview` 只写 `.tmp\icon-preview\`，`--variant play|plain` 选「P 里带三角」还是「纯 P」 |
| `src/PdxLauncher.App/Assets/AppIcon.ico` | 替换。**73,005 B**，9 帧 16/20/24/32/40/48/64/128/256、全 32bpp；`<128` 是 32 位 BMP 帧（带 AND mask，老 shell 也认）、`>=128` 是 PNG 帧；首帧偏移 150、末帧正好收在文件末尾。新 MD5 `d94e4946cc7c9da1e8194f8ce038befa` |
| 同目录 8 张 PNG | 替换（文件名与尺寸都不动）：`Square44x44Logo.scale-200.png` 88²、`Square150x150Logo.scale-200.png` 300²、`StoreLogo.png` 50²、`LockScreenLogo.scale-200.png` 48²、`Square44x44Logo.targetsize-24_altform-unplated.png` 24²、`Square44x44Logo.targetsize-48_altform-lightunplated.png` 48²、`SplashScreen.scale-200.png` 1240×600、`Wide310x150Logo.scale-200.png` 620×300 |
| `docs/app-icon.png` **（新）** | 95,329 B 接触表：深底 / 浅底两行，16→256 各一列 |
| `src/PdxLauncher.App/PdxLauncher.App.csproj` | `<ApplicationManifest>` 之后加 `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`（现第 17 行）—— 这一行才是 exe 真带图标的原因 |
| `src/PdxLauncher.App/MainWindow.xaml.cs` | 构造里 `ApplyWindowSize();` 之后加 `ApplyWindowIcon();`；新增 `private void ApplyWindowIcon()`（现 109-133 行）：取 `AppContext.BaseDirectory\Assets\AppIcon.ico`，文件不在就 `Warn("窗口图标缺失: " + path)` 直接返回，`AppWindow.SetIcon(path)` 包 try/catch 记 `Warn("设置窗口图标失败: " + ex.Message)`（缺图标不许把启动搞挂） |
| `tests/PdxLauncher.Checks/IconChecks.cs` **（新）** | 约 380 行、16 个检查类里的第 16 个。断言：ICO 保留区/类型/帧数 9/MD5 不等于占位图/尺寸集合/全 32 位/偏移不越界不重叠且末帧收尾/`<128` BMP 且 `>=128` PNG/32px 帧金色占比 ∈ (8%,60%)（实测 13.6%）/32px 四角 alpha=0 且中心不透明；8 张 PNG 存在 + PNG 魔数 + IHDR 尺寸；csproj 含 `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`；MainWindow 含 `AppWindow.SetIcon(` 与 `"AppIcon.ico"`；publish.cmd 仍校验 `Assets\AppIcon.ico` |
| `README.md` / `docs/HANDOFF.md` | README 新增「### 应用图标」小节 + 目录树两条 + 自检覆盖与计数（16 类 / 183 个 .cs + 9 个 .xaml）；本文 §0.15 / §14 与 §2.3 计数 |

### 0.15.4 验证（每条都跑过）

- `cmd /c build.cmd`（先 `taskkill /IM PdxLauncher.App.exe /F`，`-m:1`）：**0 警告 0 错误**；自检 **检查类 16，失败 0，断言失败总数 0**。
- **exe 资源**：`PrivateExtractIconsW` 从构建产物与 `dist` 的 exe 各提 16/24/32/48/256，与 ICO 对应帧逐像素比对 **平均像素差 0.00**（脚本 `.tmp\verify\compare-icons.py`）。⚠️ 踩坑：`PrivateExtractIcons` 的 `nIconIndex` 必须给 **0**，给 -1 会返回 0 个图标（看起来像「资源没进去」）。
- **运行期窗口图标**：启动 exe 后 `SendMessage(hwnd, WM_GETICON, ICON_BIG/ICON_SMALL)` 都返回 48×48 的本图标（中心 `#3A3324`）；`GCLP_HICON` 为 0（WinUI 窗口类不挂图标，任务栏走窗口图标，正常）。
- **任务栏**：本机任务栏是**竖排、贴右边缘**（`Shell_TrayWnd` 3768,0-3840,2160），截图里 PdxNext 按钮是「深底 + 金 P」方块 + 运行中蓝条（`.tmp\verify\runtime\taskbar-button-big.png`）。
- **publish**：`cmd /c publish.cmd` 全程通过（含 4 个关键文件兜底校验），`dist\PdxNext` 仍 **538 文件（顶层 438 = 349 文件 + 89 本地化子目录）/ 231.3 MB**，`Assets\AppIcon.ico` MD5 与源一致，dist 的 exe 图标与构建产物一致。

### 0.15.5 没做 / 以后可选

- **标题栏不画小图标**：标题栏是自定义的纯文字栏（`PdxNext · 游戏名 · 版本`），加小图标要改 `TitleBar` 布局，本轮不做。
- **不随游戏切任务栏图标**：官方主题有 `app-icon` / `app-taskbar-icon`，`ThemeSpec.AppIcon` / `AppTaskbarIcon` 与 `ThemeVisuals.AppIcon` 已经在仓里但没人消费。想接的话：MainWindow 收到主题变更后用 `AppWindow.SetIcon(theme.AppIcon)` 覆盖，缺省再回落 `AppIcon.ico`；注意这是「进程级窗口图标」，多游戏切换会互相影响。
- **没有 SVG 母版**：机器上没栅格化工具。以后若要矢量版，得先装 cairosvg 或 inkscape，再让生成器读 SVG 栅格化。
- 未验证：Windows 浅色 / 高对比度任务栏下的实际观感（只看了接触表的浅底行）、Explorer 里「大图标」视图（128/256 PNG 帧）的实拍。

## 0.14 最新一轮（2026-10-27 04:20）：播放集页工具条收成一行（删摘要行 / 三按钮上移 / 下拉框空白）

> 本节压过 §0.2 与 §0.3 里「第二行＝当前播放集名 / 计数 / 已激活：X 摘要」的布局描述；「切换播放集不改变激活状态 + 激活按钮」的业务语义不变。

### 0.14.1 用户诉求（m00007，附三张截图）

原话：「这个播放集的UI很拥挤，而且有问题：进入播放集页面，播放集选择那里没有名称；不需要图二的内容；图三的按钮放第一行按钮去」

- 图一（1764×828）播放集页全貌：第一行 `[空白下拉框][新建][重命名][删除] …… [搜索播放集内的 mod][添加模组]`；第二行左侧就是图二。
- 图二（769×34）：`IronyModManager　共 5 个 mod，已启用 4 个　已激活：IronyModManager`（第二行左簇）。
- 图三（405×60）：「已激活 / 启用全部 / 禁用全部」三个按钮（第二行右簇）。
- 拆解：① 下拉框进页面要显示当前播放集名；② 删掉第二行那段摘要；③ 三个按钮并入第一行；④ 整体不要再拥挤。

### 0.14.2 本轮改动（已落盘）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.App/Views/ModsPage.xaml` | 删掉整个第二行（原 `Grid.Row="1"` 的头部 Grid），`RowDefinition` 4 个 → 3 个；工具条 `ColumnSpacing` 12→6、左簇 `StackPanel.Spacing` 6→4；ComboBox `MinWidth` 220→150、`MaxWidth` 340→240；新增分组分隔线（`Border` 1×20，`DividerStrokeColorDefaultBrush`）+「激活播放集 / 启用全部 / 禁用全部」三个按钮（沿用 `btnActivatePlaySet` / `btnEnableAllMods` / `btnDisableAllMods`，细分隔线右侧）；搜索框 `MinWidth` 180→96、`HorizontalAlignment` Right→Stretch（保留 `MaxWidth` 320）；忙碌 `ProgressRing`（`ringModsBusy`）从工具条搬到**状态栏右端**（状态栏 Border 内换成 `Grid ColumnSpacing="8"`，列 `*` / `Auto`）；文件头注释同步改写 |
| `src/PdxLauncher.App/ViewModels/ModsViewModel.cs` | `SelectPlaySetAsync`（约 L313）在 `SelectedPlaySet = playSet;` 之后**无条件**补 `OnPropertyChanged(nameof(SelectedPlaySet));` + 5 行注释 |
| `src/PdxLauncher.App/MainWindow.xaml.cs:26` | `MinimumWindowWidth` 900 → **1040**（带注释说明倒推过程） |
| `README.md:163-168` | 「播放集页 / officialLauncher」段落改为单行工具条描述，删掉「第二行＝集名 / 计数 / 已激活：X」的说法 |

**下拉框空白根因（本次终于覆盖到）**：`PlaySet` 是值相等 `sealed record`（`src/PdxLauncher.Core/Contracts/PlaySet.cs:4`）；`NavigationCacheMode="Required"` 的页面每次重新进入都会走 `Loaded` → `ReloadAsync` → `LoadAsync`，先 `PlaySets.Clear()` 让 ComboBox 内部 `SelectedItem` 掉成 `null`，随后重新选中的是**同值的新实例**，`SetProperty` 判等返回 `false` → 不发 `PropertyChanged(SelectedPlaySet)` → 单向 `x:Bind`（`ModsPage.xaml:49`）永不回灌 → 选择框一直空白。§0.3 只修了「旧实例不在集合里」那条路径，没覆盖 `Clear()` + reload。新建 / 重命名 / 删除后的重新加载走同一条路径。

### 0.14.3 验证（Release，`-m:1`）

- 构建 **0 警告 0 错误**；`tests/PdxLauncher.Checks`：**检查类 15，失败 0，断言失败总数 0**。
- UIA（`.scratch/rework/playset_final.ps1`，DPI 1.5，默认窗口 1200 DIP）：`txtSelectedPlaySetName` / `txtActivePlaySet` 均 **REMOVED**（第二行确已删除）；工具条全部控件同一行、垂直居中（T≈120 px）。实测 DIP：combo `L=236.7 W=158`、新建 `394.7/52.7`、重命名 `451.3/66.7`、删除 `522/52.7`、激活播放集 `586/66.7`、启用全部 `656.7/80`、禁用全部 `740.7/80.7`、搜索 `828/262`、添加模组 `1096/80`（右缘 1176，窗口 1200，右边距 24 DIP）⇒ **行宽 939 DIP**。
- 下拉框空白：展开读项 = `1.20<SEL>=True` / `1.18=False` / `IronyModManager=False`；**离开播放集页再回来**（复现 `Clear()` 路径）后截图仍显示 `1.20 ●激活`（`.scratch/rework/2026-10-27-playset-final-reload.png`）。⚠️ WinUI 的 `SelectionItemPattern.Current.IsSelected` 在展开态会对三项都返回 False（UIA 伪值），判定以截图为准。
- 窄窗实测（同样是 DPI 1.5 物理 px ÷ 1.5）：1080 DIP 时搜索框 213 DIP、全部可见；1000 DIP 时搜索框被压到 **62 DIP**（已低于 `MinWidth 96`）但仍不裁切 ⇒ 内容区最小宽度 ≈ **740 DIP**，倒推窗口 ≈ 993；**900 DIP 时 `txtModSearch` 矩形越界、`btnAddMods` 被裁到 W=64 px** ⇒ 最小窗口宽度提到 1040。`MinimumWindowWidth` 只在 `ApplyWindowSize()`（启动时）生效，运行时手动拖到 1040 以下仍会裁切（与改前同性质，本轮没有加自适应收折）。
- 截图：`.scratch/rework/2026-10-27-playset-final.png`（首次进入）与 `...-final-reload.png`（离开再回来）；`...-w1080.png` / `...-w1000.png` / `...-w900.png` 为宽度实测。

### 0.14.4 遗留 / 注意事项

- `ModsViewModel.SelectedPlaySetName` / `CountsText` / `ActivePlaySetSummary` 与本地化键 `mods.playSet.nameLabel` / `mods.header.activePlaySet` 现在**已无视图引用**（集名改由下拉框选中项 + 「● 激活」标记承载，计数与激活提示由状态栏承载）。本轮**保留未删**（改动小、便于回退）；以后清理时记得同步删 `RaiseSelectedPlaySetChanged()` / `RaiseActivationState()` 里对应的 `OnPropertyChanged`。
- 本轮期间**另一个并发会话在改同一棵树**（HomePage 下拉收边 §0.13.7、SettingsPage / SettingsCard 重写）。03:54–04:02 连续 3 次构建失败全是对方改到一半的状态（`XamlCompiler WMC9999` + `SettingsPage.xaml.cs` 找不到 `lstSettingsSections` / `SettingsCard.xaml.cs(110,31) CS1061 DependencyProperty.Name`），对方落盘后 `dotnet build` 自动恢复 0/0。**以后再遇到 `WMC9999`：先看 `src/PdxLauncher.App/obj/Release/<tfm>/win-x64/output.json` 里的 `"GeneratedCodeFiles":[]` 与 `input.json` 里的 `"LocalAssembly": null`，再确认有没有人正在并发改 XAML，别急着怀疑自己的改动。**
- 备份：`.backup/20261027-playset-single-row/`（`ModsPage.xaml` / `ModsViewModel.cs` / `MainWindow.xaml.cs` / `README.md` / `HANDOFF.md` 改前副本）。
- 旧的 UIA 脚本 `probe_combo.ps1` / `probe_activate.ps1` / `debug_probe.ps1` / `smoke_activation.ps1` / `smoke_displays.ps1` 里还读 `txtSelectedPlaySetName` / `txtActivePlaySet`（已删除的 AutomationId）；这些脚本只用于历史冒烟，需要时把它们改成读 `cmbPlaySets` / `txtModsStatus`。

---

## 0.13 最新一轮（2026-10-27 03:20）：下拉强制朝下（换掉 ComboBox）+ 版本号照官方右对齐

> 本节压过 §0.12 的「ComboBox + MaxDropDownHeight 可强制朝下」与「版本号左对齐」两条；布局骨架、去刷新按钮、公告卡均不变。

### 0.13.1 用户复检（m03036，附四张图）

「你这也没修好啊？还有你看看图四原版启动器的版本号放哪」

- m03028（1773×1128，真机主页）：版本号在播放集下方**居中**；`MaxDropDownHeight` 方案没生效。
- m03030（466×286）：点开播放集，列表仍**向上**展开（1.18 / 1.20 在控件上方，IronyModManager 与控件同高）。
- m03032（481×195）：播放集行 + 居中「版本 1.20.1.2 (Crozier)」放大图。
- m03034（1930×1177，官方 Victoria 3 启动器）：版本「1.13.11 (Matcha)」在**广告卡正上方、右对齐**（x≈1730，卡片行 y≈790），不是左对齐。
- 结论：版本号目标位置 = 广告卡上方、右对齐；下拉必须真正朝下。

### 0.13.2 为什么 MaxDropDownHeight 一定无效（WinUI 源码取证）

源码：`microsoft/microsoft-ui-xaml` main 分支 `dxaml/xcp/dxaml/lib/ComboBox_Partial.cpp`（约 226 KB）。

- `ComboBox::GetNonPannablePopupLayout(...)`（约 166700 行起）先算
  `calculatedLayoutLocationAbove = cbY + cbHeight/2 - currentItemHeight/2 - cbPopupContentMargin.Top`，
  即**把选中项垂直居中到组合框中心**，再以 `upperLimit = MAX(cbY + cbHeight/2 - popupMaxHeight/2, 0)` 为界向上铺、向下铺。
- 选中项越靠后，向上铺的项越多 ⇒ 选中最后一个播放集时必然朝上，**与下方剩余空间无关**；`popupMaxHeight`（= `MaxDropDownHeight`）
  只决定能铺多少项，不改变「选中项居中」的定位模型。压到约 134 DIP 以下虽然不向上铺，但 `popupY` 会落到 `cbY - 11`，弹层盖住控件本身，不可用。
- 只有 editable ComboBox 在 `GetEditableComboBoxPopupLayout` 里显式翻转（`if (popupY + popupMaxHeight > rootWindowSize.Height) ... m_openedUp = true`）；普通 ComboBox 走不到。
- 另：`MenuFlyout` 会忽略 `Placement`（§0.12.5 留的退路不能用），必须用普通 `Flyout`。

### 0.13.3 本轮改动（已落盘）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.App/Views/HomePage.xaml` | 播放集 `ComboBox` → `DropDownButton`（`PlaySetButton`，220 宽 / MinHeight 32、`Padding=11,0,8,0`、内容左对齐、AutomationId `btnHomePlaySets`）+ 普通 `Flyout`（`PlaySetFlyout`，`Placement="BottomEdgeAlignedLeft"`、`Opening="OnPlaySetFlyoutOpening"`，`FlyoutPresenterStyle` Padding 0 / MinWidth=MaxWidth=220）+ `ListView`（`PlaySetList`，MaxHeight 320、`DisplayMemberPath=Name`、`IsItemClickEnabled=True`、`ItemClick="OnPlaySetItemClick"`、单选）；版本号 Grid 改 `HorizontalAlignment="Stretch"` + `MaxWidth=986`，TextBlock `HorizontalAlignment="Right"`、文本 `RawVersionText`（裸版本串，无「版本」前缀）；根 ScrollViewer 去掉 `ViewChanged`；按钮内容绑 `ViewModel.SelectedPlaySet.Name` |
| `src/PdxLauncher.App/Views/HomePage.xaml.cs` | 删 `_isSyncingPlaySet`、`OnPlaySetSelectionChanged`、`UpdatePlaySetDropDownHeight()`、`OnHomePageSizeChanged`、`OnHomeScrollViewChanged` 与构造里的 `SizeChanged` 订阅、`ReloadAsync` 里的重算调用；`SyncPlaySetSelection()` 改为 `PlaySetList.SelectedItem = _viewModel.SelectedPlaySet`；新增 `OnPlaySetFlyoutOpening`（打开前回填选中项）与 `OnPlaySetItemClick`（写 `SelectedPlaySet` 后 `Flyout.Hide()`） |
| `README.md` | 主页布局顺序改为「播放集选择器 → 版本号（公告栏正上方、右对齐、裸版本串）」，补 `DropDownButton + Flyout` 的原因与「不带版本前缀」 |
| `docs/HANDOFF.md` | 本节 + 顶部摘要行 + §0.12 加「已被推翻」标注 + §3.1 快照说明改指本节 + 验证清单里的 AutomationId 改 `btnHomePlaySets` |

### 0.13.4 验证

- `python .scratch/rework/xamlpass1.py` → `pass1 rc = 0`，17 个生成文件。
- `python .scratch/rework/build-app.py` → `[core] rc=0 errors=0` / `[app] generated cs = 17` / `[app] rc=0 errors=0 warnings=0` / `APP_OK`。
  首次跑出现过 1 条 `Views\SettingsPage.xaml.cs(88,55): error CS0246: GameInstallation`，那是**并发会话当时正在改 SettingsPage**（几秒后补上 `using PdxLauncher.Core.Contracts;`），与本轮改动无关；复跑即 0 错 0 警告。
- `python .scratch/rework/build-checks.py` → `CORE_EXIT=0` / `CHECKS_EXIT=0` / `RUN_EXIT=0` / **`检查类: 15，失败: 0，断言失败总数: 0`**（15 类是并发会话继续加检查类后的数）。
- **真机 UIA 冒烟（本轮新增 `.scratch/rework/smoke_home_dropdown.ps1`）**：启动 Release 版，窗口 1800×1200（系统缩放 150%）——
  `btnHomePlaySets`（Name「播放集」）rect L=935 R=1265 T=600 B=648；UIA `ExpandCollapsePattern.Expand()` 后，
  ListView（Name「播放集」）rect L=938 R=1264 **T=656** B=836，即列表顶边在按钮底边下方 8 DIP ⇒ **弹层朝下**（脚本判定 `VERDICT BELOW`）。
  版本号 `txtHomeVersion` 文本 `1.20.1.2 (Crozier)`、rect L=1290 R=1423 T=696 B=720 ⇒ **右对齐生效**（公告未加载时 StackPanel 按最宽子项收缩，所以这组坐标只到文字列右缘；有公告时公告行 986 DIP 决定列宽，版本右缘会与广告卡右缘齐平）。
  截图：`%TEMP%\dsh-NxOPj9\pdx-smoke\home-before.png` / `home-flyout.png`（本次联网公告未加载，公告区为空白；版本号与广告卡的最终间距仍要看有公告时的真机图）。
- 并发会话 03:24:08 的 `dotnet` Release 构建产物 `obj\Release\...\win-x64\Views\HomePage.g.cs` 里已生成
  `PlaySetButton`（DropDownButton）、`PlaySetFlyout`（Flyout + `Opening += OnPlaySetFlyoutOpening`）、`PlaySetList`（ListView + `ItemClick += OnPlaySetItemClick`），说明标准构建链同样接受新 XAML。

### 0.13.5 盲区

- 已过离线手工链 + 生成物检查 + 一次 UIA 冒烟（见 0.13.4），但冒烟窗口是 1800×1200、公告未加载；**公告加载后版本号与广告卡的实际间距、小窗口（1020 高及以下）下弹层是否仍朝下**仍需真机复验。`Flyout` 的 `BottomEdgeAlignedLeft` 只保证「对齐按钮左下」，窗口下方空间不足时 WinUI 仍可能把弹层上移/翻面。
- `DropDownButton` 用系统 Button 皮肤，与之前 ComboBox 的幽灵外观不同：默认带右侧 chevron、悬停是系统 Button 底色。若用户嫌突兀，可只给 `PlaySetButton` 设 `ButtonBackground` 等资源键，不要改全局。
- 弹层宽度用 `FlyoutPresenterStyle` 锁 220，高 DPI 下若文字更大仍可能截断（`TextTrimming` 只设在按钮内容上，列表项没有）。列表项沿用 `ListViewItem` 默认高度与 hover 样式。
- 版本号 `RawVersionText` 在无游戏时是空串（`Grid` 高度塌陷为 0）；与之前 `VersionText` 的「版本未知」不同，这是有意的「官方裸串」语义。

### 0.13.6 备份

`.scratch/rework/2026-10-27-home-tweak2-HomePage.xaml.txt` / `...-HomePage.xaml.cs.txt`（改前 508 / 668 行原文，平铺）。

### 0.13.7 播放集控件收边（m03401，2026-10-27 04:10）

用户原话：「那你这个边框没有写美化啊，怎么是直角边」。指的是上一个 `home-flyout.png` 里播放集按钮与弹层那一圈**白色直角描边**。

- 根因：`DefaultListViewItemStyle` 的 `UseSystemFocusVisuals=True` 会给选中项画 2 px 系统焦点框（矩形、白色）；`DefaultDropDownButtonStyle` 与 `DefaultFlyoutPresenterStyle` 默认还带 `ButtonBorderThemeThickness` / `FlyoutBorderThemeThickness` 的硬边。
- 改动（`src/PdxLauncher.App/Views/HomePage.xaml`）：`DropDownButton` 加 `BorderThickness="0"`、`CornerRadius="4"`（不加 `Background`，避免悬停时 `ButtonBackgroundPointerOver` 整体替换掉玻璃底）；`FlyoutPresenterStyle` 加 `CornerRadius="8"`、`BorderThickness="0"`；`ListView` 加 `Margin="4"`、`Background="Transparent"`；`ItemContainerStyle` 设 `UseSystemFocusVisuals=False`、`MinHeight=40`、`Padding=12,0`（保留选中项的浅色底与蓝色选中态，只去掉白框）。
- 验证：`xamlpass1.py` 17 个生成文件；`build-app.py` `[app] rc=0 errors=0 warnings=0` / `APP_OK`；UIA 冒烟同 §0.13.4（`btnHomePlaySets` rect L=935 R=1265 T=600 B=648，ListView L=942 R=1260 T=660 B=840 ⇒ `VERDICT BELOW`；版本号 L=1290 R=1423 T=696 B=720）。截图 `%TEMP%\dsh-NxOPj9\pdx-smoke\home-flyout.png` 与裁剪 `crop-btn2.png` / `crop-fly2.png` 可见按钮圆角 4、弹层圆角 8、无白色描边、选中项无白框。
- `build-checks.py` 复跑（并发会话已把 `tests/PdxLauncher.Checks/PlaysetChecks.cs:941-942` 的硬编码 22 改成 `>=22` 且「全部启用」）为 `CORE_EXIT=0` / `CHECKS_EXIT=0` / `RUN_EXIT=0`、`检查类: 15，失败: 0，断言失败总数: 0`。04:10 那次 `失败: 2` 是因为本机 1.18 播放集已有 25 个 DLC（与官方启动器「DLC 25 个」一致）而旧断言写死 22，不是 UI 回归。
- 盲区：`UseSystemFocusVisuals=False` 连键盘焦点框一起去掉了，键盘用户只剩选中项底色（鼠标优先，可接受；若以后要补，需自绘圆角焦点框）；选中项没有官方 ComboBox 那样的左侧蓝色竖条，只有浅色底；小窗口下 `Flyout` 是否会被 WinUI 翻到上方仍未实测。

---

## 0.12 上一轮（2026-10-27 02:35）：主页三处细节 —— 下拉朝下、去刷新、版本号下移

> 本节压过 §0.11 里「页头含版本号」「右上角刷新按钮」的描述；布局骨架与公告卡不变。
> ⚠️ **0.12.2 的 `MaxDropDownHeight` 结论已被 §0.13 推翻**（源码证明它改变不了「选中项居中」的定位模型），下拉控件已换成 `DropDownButton + Flyout`；版本号也从左对齐改为右对齐。保留本节仅作过程记录。

### 0.12.1 用户诉求（m02851，附四张真机截图）

「1.把这个选择改朝下，怎么写朝上？；2.主页为什么要这个刷新按钮？；3.版本号跟原版一样放到左下角去，底部公共栏上面」

- m02843 = 真机主页（窗口 1599x1020）：字标 + 版本 + 继续/开始 + 播放集 + 三张真实公告卡，标题重复已消失（§0.11 的 `FromStringInverse` 生效）。
- m02845 = 播放集下拉**向上**展开（1.18 / 1.20 / IronyModManager）；m02847 = 右上「刷新」；m02849 = 页头「版本 1.20.1.2 (Crozier)」。
- 追问答复：刷新按钮 → **移除**；「底部公共栏」→ 用户自定义回答「就是那个p社的广告栏」⇒ 版本号放三张公告卡正上方、左对齐（不是窗口级底栏）。

### 0.12.2 下拉朝上的根因（不是猜）

`%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk.winui\2.3.9\lib\native\Microsoft.UI\Themes\generic.xaml` 的
`DefaultComboBoxStyle` 里 `MaxDropDownHeight = 504`。ComboBox 按这个上限预估下方空间：窗口 1020 高、下拉框底边约 590，
下方只剩约 430 < 504 ⇒ 弹出层被翻到上方。**把 `MaxDropDownHeight` 压到实际剩余高度就固定朝下**，视觉 / 键盘 / 无障碍都不变。

### 0.12.3 本轮改动（已落盘）

| 文件 | 内容 |
| --- | --- |
| ```src/PdxLauncher.App/Views/HomePage.xaml``` | 根 `ScrollViewer` 加 `x:Name="HomeScrollViewer"` + `ViewChanged="OnHomeScrollViewChanged"`；页头删掉右上「刷新」Button（保留 `ringHomeBusy`）与版本号 TextBlock；在公告区之前新增 `txtHomeVersion`（`HorizontalAlignment="Left"`、`CaptionTextBlockStyle`、次要前景色），公告隐藏时仍在状态文字上方；页头与顶部注释同步改写 |
| ```src/PdxLauncher.App/Views/HomePage.xaml.cs``` | 删 `OnRefreshClick`；构造里订阅 `SizeChanged`；新增 `UpdatePlaySetDropDownHeight()`（`ActualHeight - TransformToVisual(this).Y - ComboBox.ActualHeight - 16`，`Math.Clamp(_, 120, 420)` 赋给 `MaxDropDownHeight`，try/catch + Warn）、`OnHomePageSizeChanged`、`OnHomeScrollViewChanged`；`ReloadAsync` 末尾在 `SyncPlaySetSelection()` 后重算一次 |
| ```README.md``` | 主页布局顺序补「版本号（公告栏正上方、左对齐）」；说明官方刷新按钮已按用户意见去掉、页面加载/切游戏自动刷新 |
| ```docs/HANDOFF.md``` | 本节 + 顶部摘要行 + §3.1 旧 UI 快照加注 |

### 0.12.4 验证

- `python .scratch/rework/xamlpass1.py` → `pass1 rc = 0`，17 个生成文件。
- `python .scratch/rework/build-app.py` → `[core] rc=0 errors=0` / `[app] generated cs = 17` / `[app] rc=0 errors=0 warnings=0` / `APP_OK`。
- `python .scratch/rework/build-checks.py`（本轮新增：bash 在沙箱里建不出 pipe，改 Python 复刻 `build-checks.sh` 的 csc 流程）
  → `CORE_EXIT=0` / `CHECKS_EXIT=0` / `RUN_EXIT=0` / **`检查类: 13，失败: 0，断言失败总数: 0`**（13 类是并发会话又加了一类之后的数）。

### 0.12.5 盲区

- 仍只过**离线手工链**，真机 UI 未跑；`MaxDropDownHeight` 动态取值的强制朝下依赖 WinUI「按该上限预留空间」的行为（默认值 504 与截图完全吻合）。
  若真机仍朝上，退路是 `DropDownButton + MenuFlyout`（`Placement=BottomEdgeAlignedLeft`），视觉需另调。**（§0.13 已推翻：MenuFlyout 会忽略 Placement，实际改用普通 Flyout。）**
- 极端矮窗口（600 高）时下拉可能只显示约 2 项（`Clamp` 下限 120），其余靠下拉内部滚动 —— 这是有意的，优先保证不翻到上方。
- `btnHomeRefresh` / `OnRefreshClick` 的 AutomationId 与处理函数已删除；仓库内 tests/tools 原本零引用，无需跟进。

### 0.12.6 备份

`.scratch/rework/2026-10-27-home-tweak-*`（改前的 HomePage.xaml / HomePage.xaml.cs / README.md / HANDOFF.md，平铺）。

---

## 0.11 上一轮（2026-10-27 01:30）：主页三条返修 —— 去框、删统计卡、公告卡照官方实现

> 本节压过 §0.6.1 里「启动卡片 Border」「统计三卡」「最近存档条」与「公告卡 Button+Border+IsEnabled 灰化」的描述。

### 0.11.1 用户诉求（m02524，附四张图）

「1.图一： 这部分没有用；2.图二这部分也不行，和图三的官方启动器相比，不好看，为什么外部要套个框；3.图四这玩意不对吧，你看下原版启动器是如何实现的」

- 图一 = 统计三卡（播放集 / 模组与 DLC / 游戏版本）+ 最近存档条 → **没有用，删**。
- 图二 = 启动区外面那层 `PdxCardGlassBrush` 玻璃 Border → 官方主页没有任何外框，**去掉**。
- 图三 = 官方 CK3 主页：居中字标 → 继续/开始并排 → 播放集下拉 → 底部公告卡，全程无卡片容器。
- 图四 = 我们的公告卡：Button + Border 裹框、图片没铺满、第三张因 `pdxlaun://` 被 `IsEnabled=false` 整张灰掉；官方是纯图块。

### 0.11.2 官方实现（反编译取证，Electron 启动器 app.asar）

- 公告卡 `.news`：固定尺寸纯图块（main 451×230、secondary 各 246×230，gap 21），`background-size:cover`、圆角 4、
  `box-shadow:0 5px 4px #00000040,0 0 0 1px #0006` 外圈 + `inset 1px #ffffff1a` 内圈；**没有内层卡片、没有 padding**。
  `:hover` 叠 `#13172480`（50% 深色）；`.news--withTitle` 才加底部渐变，文字 `.news__text` 24px 白字垫在左下角，正文不渲染。
- 主题按钮：即使有主题底图也叠内嵌高光 `inset 0 0 0 2px #ffffff38` + 底边 `inset 0 -1px #ffffff54`（官方截图逐像素实测吻合）。
- 布局：`.playBtns` 两按钮间距 16 DIP；官方截图 logo→按钮 ≈30 DIP、按钮→播放集 ≈44 DIP。

### 0.11.3 本轮改动（已落盘）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.App/Views/HomePage.xaml`（整体重写） | 删统计三卡（txtHomePlaySet/txtHomeCounts/txtHomeGameVersion）与最近存档条（txtContinueTitle/txtContinueDate/btnContinueGame）；启动区改为页面直接子级、不再套 Border；两按钮 232×66 / 圆角 4 / 内嵌 `#38FFFFFF` 2px + 底边 `#54FFFFFF` 1px，「继续」文案（官方 CK3 截图是「继续游戏」，此处按 Victoria 3 官方的短文案「继续」）；播放集行 `Margin="0,40,0,0"`；公告区 `NewsRegion`（**Viewbox + 水平 StackPanel**，MaxWidth 986、Spacing 21；不用三列 Grid —— Grid 会给隐藏的卡留空列，官方 flex 是缺哪块就少一块、整行仍居中；Viewbox 只向下缩放，窗口窄于 985 时整行等比缩小）三块 `Width=451/246/246` `Height=230` 纯图 Button，无 IsEnabled 绑定，内容 = Image(UniformToFill) + 标题渐变 + 24px 白字 + `NewsHoverVeil*` 遮罩 + 内圈；新增 `NewsInfoBar`（infHomeNews）；相应删掉了 code-behind 的 `UpdateNewsLayout`/`OnViewModelPropertyChanged` |
| `src/PdxLauncher.App/Views/HomePage.xaml.cs` | `ApplyThemedButton` 圆角改常量 4；`OpenNewsLink` 纯图卡直接 return、深链改 `ShowNewsInfo` 提示（不再灰化）；删 `FindHoverRing` 递归，hover 改为按 sender 取 `NewsHoverVeilMain/Secondary1/Secondary2` 改 `Opacity`；新增 `ShowNewsInfo` |
| `src/PdxLauncher.App/ViewModels/HomeViewModel.cs` | 新增 `ContinueToolTip`（存档名 + 时间进按钮提示），并在 RaiseDerived/OnGameChanged/OnContinueTitleChanged 通知、新增 `OnContinueDateChanged`；统计卡相关的 PlaySetText/CountsText 加「暂无绑定」注释保留 |
| `src/PdxLauncher.App/Services/XamlVisibility.cs` | 新增 `FromStringInverse`：有字标（`LogoPath` 非空）时 → `Collapsed`。CK3 的 `logo.svg` 本身就是带文字的大横幅，下面再排一行 `txtHomeGameTitle` 会和字标重复（官方页头只有横幅字标 + 版本号）；没有字标的游戏仍显示纯文字标题 |
| `src/PdxLauncher.App/Services/ThemeApplier.cs` | 幽灵按钮（游戏没有按钮底图时）不再画 1px 外描边：官方 pureCSS 按钮的 2px 内嵌高光现由 HomePage 内容层的 `*InnerRing` 统一绘制，两处都画会叠成双层边；`GhostButtonBorder` 常量删除，有图/无图两个分支都返回透明描边 + `Thickness=0` |
| `README.md` / `docs/DATA-FORMATS.md` | 主页布局与公告渲染语义改成「无统计卡 / 无存档条 / 无外框 / 纯图公告卡」；7.2 补渲染段与深链提示 |

### 0.11.4 验证

- `python .scratch/rework/xamlpass1.py` → `pass1 rc = 0`，17 个生成文件；
  **中途踩坑**：XAML 头注释里写 `.themedButton--pureCSS` 触发 `WMC9997 XML 注释中不能包含 --`，pass1 只生成 16 个文件
  并连带 App 报 14 个 CS0103（ModThumbnailSizes / ShellBackground* / NewsHoverVeil* / NewsInfoBar 全未生成），去掉双连字符后全部消失。
- `python .scratch/rework/build-app.py` → `[core] rc=0 errors=0` / `[app] rc=0 errors=0 warnings=0` / `APP_OK`
  （公告行从 Grid 改成 Viewbox+StackPanel、并删掉 `UpdateNewsLayout` 后又跑了一遍：`pass1 rc=0`、`17` 个生成文件、`APP_OK`；
  删掉幽灵按钮外描边后再跑第三遍，同为 `pass1 rc=0` / `17` 个文件 / `rc=0 errors=0 warnings=0` / `APP_OK`）。
- 最新一次 `bash .scratch/rework/build-checks.sh` → **`检查类: 12，失败: 0，断言失败总数: 0`**（12 类是并发会话又加了两类之后的数）。
  下面这几条是更早一次跑的**环境性失败**记录，留档说明当时为什么红；现在已被各自负责人修掉：
- 更早那次 `bash .scratch/rework/build-checks.sh` → `检查类: 10，失败: 6，断言失败总数: 4`，4 条全是**环境/并发改动造成，与主页无关**：
  ① `真机: 发现游戏数量` 期望 4 实际 5（本机新装了 HOI4，已识别为 hoi4）；
  ② `CK3 合并后 mod 总数` 期望 387 实际 382（磁盘 .mod 与 sqlite 都变成 382，用户改过播放集）；
  ③ `默认缓存目录` 期望 `%LOCALAPPDATA%\PdxNext\theme-cache` 实际 `<程序目录>\data\theme-cache`
  （并发会话把数据根目录迁到 `PdxDataRoot`（程序目录 data）并保留旧目录迁移，OnlineChecks 的断言还没跟上）。
  ⇒ 主页相关断言（HomePage/公告）无失败；这 4 条需要各自的负责人更新断言，本轮未动。

### 0.11.5 盲区

- 只过了**离线手工链**（XamlCompiler pass1 + csc），`dotnet build` 仍被沙箱挡（MSB3883）；pass1 的 `.g.cs` 为 0 字节，
  **x:Bind 绑定表达式没有 codegen 验证**，真机 UI 未跑。
- 公告卡与官方仍有差异：官方 main 图是 GIF 动图（我们只显示首帧）、官方每条 slot 是 slick 轮播（我们只显示第一条）、
  官方 hover 时文字上浮 10.7 DIP（我们不渲染正文，只有压暗）。
- 按钮内嵌高光用两层 Border 近似，未在 125%/150% DPI 下核对 1px/2px 的实际像素宽度。
- 公告卡的圆角靠 `Border.CornerRadius`；WinUI 的 Border **不保证裁剪子元素**（官方是 `overflow:hidden`），真机上 4px 圆角处的图片可能露直角。
  4px 很小、视觉上难察觉；若在意，再在 `SizeChanged` 里给 Image 套 `RectangleGeometry` 裁剪。
- 主页字标：`LogoImage` 用 `SvgImageSource` 直接载 `.svg`，已改为「有字标就不再显示纯文字标题」；字标与版本号之间的 Spacing=8 只是近似官方排版，未逐像素核对。

### 0.11.6 备份

`.scratch/rework/2026-10-27-home-tiles-*`（改前的 HomePage.xaml / HomePage.xaml.cs / HomeViewModel.cs / README.md / DATA-FORMATS.md / HANDOFF.md，ops_copy 平铺）。

---

## 0.9 最新一轮（2026-10-27 00:35）：聚焦态 ─ □ ✕ 不可见 —— 根因与修复

> 本节只讲系统 caption 按钮的配色，不涉及标题栏文案（文案见 §0.4）与置顶按钮布局（见 §0.8）。

### 0.9.1 现象

窗口**聚焦**时右上角三个系统按钮（最小化 / 最大化 / 关闭）看不见，**失焦**时反而能看见一点灰。
用户原话：「聚焦状态看不清系统的三个按钮，和左边的标题栏怎么反过来了？」——左边标题文字聚焦时是亮的、
失焦才变暗，右边按钮却是聚焦最黑、失焦才有点灰，两边明暗节奏正好相反。

### 0.9.2 根因（有实测，不是猜）

`ExtendsContentIntoTitleBar = true` 之后，标题栏区域画的是应用自己的背景图 + 压暗层，但三个按钮仍由 DWM 绘制，
颜色取自 `AppWindowTitleBar` 的按钮颜色属性。这些属性的**默认值就是浅色主题值**：

- `ButtonForegroundColor`（聚焦）= `#000000` —— 画在近黑的背景图上就是看不见；
- `ButtonInactiveForegroundColor`（失焦）= `#606060` —— 反而还剩一点灰能看见。

所以「聚焦最黑、失焦发灰」不是反转，是默认配色恰好只对浅色标题栏成立。**不是**主题解析错、也不是 padding 错。

### 0.9.3 走过的一条死路（别重复）

先试过 `DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE(=20), 1)`，并加 `SetWindowPos(SWP_FRAMECHANGED)`、
`RedrawWindow(INVALIDATE|FRAME|UPDATENOW)`、桌面↔窗口激活切换强制重绘：读回属性确实是 1，但 caption 按钮像素
**一个都没变**（glyph 峰值亮度仍是旧的）。结论：该 DWM 属性对「内容扩展到标题栏」模式下的 caption 按钮不起作用，
改按钮颜色只能走 `AppWindowTitleBar` 的颜色 API。

### 0.9.4 修复（已落盘、已构建、已真机截图验证）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.Core/Themes/TitleBarPalette.cs`（新增） | `record TitleBarPalette(Foreground, ForegroundInactive, ForegroundHover, ForegroundPressed, BackgroundHover, BackgroundPressed)`，`Dark` / `Light` 两套 + `For(bool isDarkTheme)` |
| `src/PdxLauncher.App/Services/TitleBarTheme.cs`（新增） | `Apply(AppWindow?, ElementTheme)`：`IsCustomizationSupported()==false`（Win10）直接返回 false 不抛；背景色置透明保留背景图；写前景 4 态 + 悬停/按下底色 |
| `src/PdxLauncher.App/MainWindow.xaml.cs` | 三处挂载：ctor 里按 `Application.Current.RequestedTheme` 先刷一次；`OnRootGridLoaded` 里按 `RootGrid.ActualTheme` 再刷（控件模板应用完，框架可能刚写过自己的值）；`RootGrid.ActualThemeChanged` → `OnRootGridActualThemeChanged` 重刷（DWM 只在窗口创建时读一次颜色，切主题不重写就会停在旧主题） |

深色配色：前景 `#FFFFFF`、失焦 `#8C8C8C`、按下 `#B3B3B3`、悬停底 6% 白、按下底 9% 白；浅色对称（黑 + 6%/9% 黑）。

**已知代价**：`AppWindowTitleBar` 没有「只给关闭键上色」的入口，因此关闭键失去原生红色悬停，三键统一淡白/淡黑高亮
（Win11 原生 caption 按钮本来也是这种统一高亮，视觉上不违和）。

### 0.9.5 自检与真机证据

- `tests/PdxLauncher.Checks` 新增 `TitleBarPaletteChecks` 17 条断言（含 WCAG 对比度：深色 20.06:1、失焦 5.97:1；
  浅色 18.93:1、失焦 5.67:1），`dotnet build` **0 警告 0 错误**，Checks **10 个类 / 0 失败**。
- 150% DPI 真机截图（`.scratch/pa-ui/verify4`、`verify5`、`verify6`、`release-check`，脚本同名 `.ps1`）：
  聚焦 glyph 峰值 255、纯白像素 92；悬停中间格区域均值 16.52 → 30.69（+14.2，与「6% 白叠在 #2A2A2A」的理论值 14.28 吻合）；
  失焦 glyph 峰值 140（= 我们给的 `#8C8C8C`）且清晰可见；最大化后「还原」图标白色可见。左侧标题文字与右侧按钮同明暗节奏。
- 高对比度：`TitleBarTheme.IsHighContrast()`（`SystemParametersInfo(SPI_GETHIGHCONTRAST)`）为真时把全部颜色写回 `null`，
  交还系统按无障碍主题绘制，避免写死的黑/白在 HC 的纯黑或纯白底上消失。
- 复现脚本注意点：WMI / `Start-Process` 拉起的窗口抢不到前台（Windows 前台锁），要 `AttachThreadInput` 再
  `SetForegroundWindow` 才能真正测到聚焦态；测失焦时记事本要 `SetWindowPos` 挪开（或直接点桌面空白处），
  否则白色窗口会盖住截图区域；窗口被最小化时 `GetWindowRect` 返回 `-32000` 坐标，量尺寸前先判 `IsIconic`。

### 0.9.6 盲区

Light / HighContrast 只有断言层面的底色对比度，没有真机切系统主题跑一遍。浅色下标题栏底色来自
`PdxTopScrimBrush` 的白色渐变，配色仍是「浅底黑字」，方向正确，但悬停高亮的实际观感未真机确认。

### 0.9.7 备份

`.scratch/rework/backup-20261027-000735/`（改前的 `MainWindow.xaml.cs`、`Services/ThemeApplier.cs`）。

---

## 0.10 最新一轮（2026-10-27 00:30）：主页两个主按钮按「官方主题按钮」语义重写

> 本节压过 §0.6.3 关于「两个主按钮染色」的描述（那版把主题色当底色用，是错的）。

### 0.10.1 现象（用户原话 m02026）

「当前的继续游戏按钮和开始游戏是错的，你似乎没有正常获取到原版启动器的正确美术材料」—— 四款游戏实测都是**深底深字**：

| 游戏 | 旧观感 |
| --- | --- |
| CK3 | 近黑底 + 近黑字，完全读不出 |
| Victoria 3 | 红底花纹 + 深色字，字几乎看不见 |
| Stellaris | 暗青底 + 更暗的字 |
| Prison Architect | 白大理石底 + 偏暗的字（勉强能看） |

### 0.10.2 根因（两处语义错误，已对照官方 schema 与 CSS 核实）

- 官方 `theme-settings.json` 的 `button.color` 是**文字色**（schema 原文 `Color of font on element`），`button.backgroundImage` 才是底色。
- 旧 `ThemeApplier.Create()` 把 `button.color` 当**底色**，再用 `ContrastFor(底色)` 反推文字色：米色 `hsla(53,31%,85%,.9)` 被算成深色字，而底色又被深色按钮图覆盖 ⇒ 黑底黑字。
- 另有尺寸与状态缺失：按钮走系统尺寸（不是官方 232×66），没有悬停 / 按下 / 禁用反馈，图用 `UniformToFill` 会把 V3 的金珠边框裁掉。

### 0.10.3 修复（已落盘）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.Core/Contracts/ThemeSpec.cs` | `ButtonColor`→`ButtonTextColor`、`ButtonHoverColor`→`ButtonHoverTextColor`，注释写明「官方 button.color 是文字色」 |
| `src/PdxLauncher.Core/Contracts/ThemeSettingsDto.cs` | 同名字段改名（JSON 键 `color` 不变） |
| `src/PdxLauncher.Core/Themes/ThemeReader.cs` | 默认常量与命名参数改名（`DefaultButtonTextColor` 等） |
| `src/PdxLauncher.Core/Online/ThemeResolver.cs` | Merge 与兜底常量改名，注释改「按钮**文字**色兜底」 |
| `src/PdxLauncher.App/Services/ThemeVisuals.cs` | 新签名：`ButtonUsesArt` / `ButtonHoverBackground` / `ButtonBorderBrush` / `ButtonBorderThickness` / `ButtonHoverForeground` / `ButtonDisabledForeground` |
| `src/PdxLauncher.App/Services/ThemeApplier.cs` | 主题色只当文字色；有图时底/悬停都是同一图刷、边框透明，无图时用 ghost 常量；`Stretch` 改 `Fill`；删掉 `ContrastFor` / `Lighten` |
| `src/PdxLauncher.App/Views/HomePage.xaml` | 两个按钮 232×66、`Padding=0`、内含 `Grid`+`Border`(悬停叠加)+`TextBlock`(文字)，去掉 `AccentButtonStyle` |
| `src/PdxLauncher.App/Views/HomePage.xaml.cs` | `ThemedBrushBindings` 7 组资源键覆盖 + `ThemedButtonSkin`（悬停 / 按下 / 禁用态） |

渲染规格：232×66（官方 14.5rem × 4.1667rem）、图 `Fill` 拉伸不裁金边、7 组资源键覆盖（背景正常/hover/禁用、边框、前景正常/hover/禁用）、
悬停 8% 白色叠加 + 悬停文字色、按下 `CompositeTransform` 缩 0.85、禁用 55% 透明 + 40% 文字色、无主题图时 ghost（白 5% 底 / 12% 悬停 / 22% 边框）。
**绝不改 `Button.Background` 的 ThemeResource 画笔**，仍沿用「叠加层」范式（见 §0.6.3 那一行注释里的原因）。

官方语义里未实现的部分：`localizedImages`（play / resume / getStarted 三态整图按钮 —— **实测 4 个已装游戏都没用**）、
`buttonDisabled` / `buttonText` 素材、`height`、`effect3dDisabled`、`hoverAnimationDisabled`。

### 0.10.4 验证（实跑）

- 离线链：`python .scratch/rework/xamlpass1.py` → `pass1 rc = 0`、17 个生成文件；`python .scratch/rework/build-app.py` → `[core] rc=0 errors=0` / `[app] rc=0 errors=0 warnings=0` / `APP_OK`；`bash .scratch/rework/build-checks.sh` → **`检查类: 10，失败: 0，断言失败总数: 0`**。
- 素材对照图（PIL 按主题字段生成，不是目测）：CK3 深炭纹理 + 米色字、V3 紫红大理石 + 金珠边 + 近白字、Stellaris 暗青纯色 + 白字、PA 白大理石 + 深灰字。
- **仍未验证**：真机 UI（沙箱内起不了窗口）、`x:Bind` codegen（pass1 的 `.g.cs` 是 0 字节）、`dotnet build`（MSB3883）。

### 0.10.5 新增工具 `tools/extract-launcher-assets.py`

解 `app.asar` 导出官方启动器自带素材（14 个：SourceSansPro 6 个字重、默认背景、图标、6 个 sidebar-icon），
并按各游戏 `theme-settings.json` 导出主题素材 + 生成 `REPORT.md`。纯标准库、无 Pillow 依赖（手写 PNG/GIF/JPEG/WEBP 头解析）。
**asar 数据区偏移 = `8 + headerSize`**，写成 `16 + jsonLength` 会偏 2 字节读出垃圾 —— 这是解包最容易踩的坑。

### 0.10.6 备份

`.scratch/rework/2026-10-26-button-skin-*`（12 个文件平铺；沙箱禁止 bash 建子目录，只能用 `ops_copy` 平铺）。

---

## 0.4 最新一轮（2026-10-26）：标题栏文案收敛为「启动器名称 · 游戏名 · 版本号」

> 本节优先级最高（仅就标题栏文案而言），压过 §2.1 / §3.1 里关于 `AppTitleBar.Title/Subtitle` 的旧描述。

**用户诉求（m00003）**：「顶部信息改下，改为启动器名称·游戏名·版本号，后面那个发现游戏不需要」。
用户截图里的原文是 `PdxNext · Victoria 3  版本 1.13.11 (Matcha) · 已发现 4 个游戏`。

**目标文案**：主文案 `PdxNext · Victoria 3 ·`（主色），副标题 `版本 1.13.11 (Matcha)`（次要色），**不出现「已发现 N 个游戏」**。

**改动**

- `src/PdxLauncher.App/ViewModels/ShellViewModel.cs`（126 行）：
  - `private const string LauncherName = "PdxNext";`（类首）
  - `public string HeaderText => CurrentGame is null ? LauncherName : LauncherName + " · " + CurrentGame.DisplayName + " ·";`（:45）
  - `public string SubtitleText => CurrentGame is null || string.IsNullOrWhiteSpace(CurrentGame.Version) ? string.Empty : "版本 " + CurrentGame.Version;`（:50）
  - `CurrentGame` 变化时两个属性各发一次 `OnPropertyChanged`（:109 / :115 / :116）。
- `src/PdxLauncher.App/MainWindow.xaml.cs`（510 行）`UpdateTitleBar()`（:460-471）：
  `AppTitleBar.Title = _viewModel.HeaderText; AppTitleBar.Subtitle = _viewModel.SubtitleText;`，
  不再自己读 `AppServices.CurrentGame` 拼串；外层 try/catch 保留，两处调用点（OnRootGridLoaded 前后、OnCurrentGameChanged）不变。
- **分隔点放在主文案末尾，不放在副标题前**：WinUI 模板 `TitleBarTitleMargin` = 8px 且 `PART_SubtitleText` 左外边距为 0；
  若写成 `SubtitleText = "· 版本 …"`，点左侧缝隙约 12px、右侧只有 4px，视觉上「挂在版本上」。放主文案末尾后与标题内已有的
  `·` 同为主色、间距对称。
- `ShellViewModel.StatusMessage` 的「已发现 N 个游戏」（:71）与 `LibraryViewModel.cs:63` 游戏库页的同一句**保留**
  （已无界面绑定到前者，属内部状态文案）。

**验证（Release，0 警告 0 错误）**

- CK3：`TitleBar|barAppTitle|PdxNext · Crusader Kings III ·`、`PART_SubtitleText|版本 1.20.1.2 (Crozier)`；barAppTitle 子树内已无「已发现」。
- Victoria 3：`PdxNext · Victoria 3 ·` + `版本 1.13.11 (Matcha)`。
- Stellaris（切游戏联动）：`PdxNext · Stellaris ·` + `版本 Cygnus v4.5.2 (9776)`。
- 3 倍放大截图：`.scratch\rework\final_titlebar_v3.png`（1680x144，取自 1778x48 顶栏条）；原尺寸 `.scratch\rework\crop_titlebar-01.png`。
- 无游戏空态只做代码级确认（`HeaderText` = `PdxNext`、`SubtitleText` = 空串 → 模板自动隐藏副标题段），**未做真机复现**（需改用户游戏配置）。

**并发会话提醒**：本轮 `dotnet checks` 中途变成「检查类: 6，失败: 2，断言失败总数: 1」，失败项是新增的 `SteamWorkshopChecks`
（`创意工坊 id：steamId 前后空白被裁掉 [期望 "123"，实际 null]`）；同一时段另一会话在改 `ModLibraryViewModel.cs` / `ModLibraryPage.xaml.cs`
（15:29 曾报 CS1061 SetStatus）。**与标题栏改动无关**（见 §4.13）。15:40 对方补齐后复跑：
构建 `0 个警告 0 个错误`、`检查类: 6，失败: 0，断言失败总数: 0`、exit 0。
另：构建前若 `bin` 里的 dll 被一个常驻 `PdxLauncher.App.exe` 锁住会报 MSB3027/MSB3021（重试 10 次失败），
按 §4.13 只 `Stop-Process -Id <自己的 pid>` 再构建即可。

**附：本轮新增的可用脚本**（旧 `cap3.ps1` / `cardshot.ps1` 在本机 `$bmp.Save()` 抛 GDI+ `ExternalException`，已不可用）

- `.scratch\rework\capbar.ps1`：UIA 取 `barAppTitle` 矩形 → ShowWindow/SetForegroundWindow + HWND_TOPMOST 置前（**必须先置前，否则截到别的前台窗口**）
  → CopyFromScreen → 存 `crop_titlebar-01.png` → 收尾回 HWND_NOTOPMOST。
- `.scratch\rework\clickcard.ps1 -CardName 'Victoria 3'`：选中 `navLibrary` → 按 Name 找 `GridViewItem` → SelectionItemPattern.Select。

---

## 0.8 最新一轮（2026-10-26 17:55）：窗口置顶入口移到标题栏右上角（TitleBar.RightHeader）

> **本节是「窗口置顶入口在哪」的当前真相**，压过 §0.2 的「三个入口 = 左栏底部图钉」与 §0.2.1 整节（左栏底部方案已作废）。
> 置顶逻辑本身（AlwaysOnTopService / 设置页 ToggleSwitch / Ctrl+Shift+T / settings.json 持久化）一行未改，仍是 §0.2 描述的语义。

**用户诉求演变**

- 起点（m00005）：「这玩意的大小怎么不一样大？」——左栏 PaneFooter 里自绘的图钉与「设置」导航项在尺寸 / 内边距 / 图标轴上都不一致。
- 定调（m00252）：「那正常的置顶方案是不是放在右上角那三个的旁边，我对方案b的手工对齐不满意，无法自适应各种情况」
  ⇒ 必须贴系统最小化 / 最大化 / 关闭按钮、必须自适应 DPI / 最大化 / 主题，**不许手写像素补偿**。
- 执行（m00541「执行」/ m00848「继续」）。

**落地方案：官方槽位 + 两个 ThemeResource 覆盖，零手写像素**

- `TitleBar` 模板（generic.xaml `DefaultTitleBarStyle`）是 12 列：
  0 `LeftPaddingColumn` / 1 Back / 2 PaneToggle / 3 LeftHeaderPresenter / 4 LeftHeaderPaddingColumn / 5 Icon /
  6 Title / 7 Subtitle / 8 ContentPresenter(星号) / **9 RightHeaderPresenter** / 10 `MinDragRegion` / 11 `RightPaddingColumn`。
  第 11 列由框架按 `AppWindowTitleBar.RightInset` 自动算，所以 RightHeader 天生就落在系统按钮左侧，且随 DPI / RTL / 最大化自适应。
- `<TitleBar.Resources>` 覆盖两个键（都在 `src/PdxLauncher.App/MainWindow.xaml`）：
  - `TitleBarExpandedHeight` = **32**：RightHeader 一旦非空，`UpdateHeight()` 会把标题栏从 Compact(32) 切到 Expanded(48)；覆盖回 32 后与系统 Standard caption 按钮等高，视觉改动最小。
  - `TitleBarMinDragRegionWidth` = **8**：框架默认 48，会在图钉与三个系统按钮之间留一大截空白（实测 75 物理像素）；标题文字 / Content 区本身仍可拖动窗口，收敛到 8 后间隙 = 8 + ToggleButton.Margin(2) = **10 逻辑像素**。
- `<TitleBar.RightHeader>` 里放 `ToggleButton x:Name="AlwaysOnTopButton"`：`Width="{ThemeResource TitleBarPaneToggleButtonWidth}"`(40)、`Height=28`、`Margin=2`、`Padding=0`、`CornerRadius="{ThemeResource ControlCornerRadius}"`、透明底 + 无边框，内容只有 `<FontIcon FontSize="16" Glyph="&#xE718;"/>`，`AutomationProperties.AutomationId="btnAlwaysOnTop"`、`Click="OnAlwaysOnTopClick"`、`KeyboardAccelerator Ctrl+Shift+T`。
  `ToggleButton.Resources` 覆盖 9 个画笔键（PointerOver→SubtleFillColorSecondary、Pressed→Tertiary、Checked→Secondary、CheckedPointerOver/CheckedPressed→Tertiary、ForegroundChecked→TextFillColorPrimary、三个 BorderBrushChecked*→Transparent），避免默认 Accent 白砖。
- `NavigationView.PaneFooter` 精简为只剩 `StackPanel x:Name="PinFooterPanel" Margin="8,4,8,8"` + `NavContentHint`（AutoId txtNavContentHint）；`PinDivider` / 左栏图钉 / `PinContent` / `PinLabel` / `Spacing` 全删。
- `MainWindow.xaml.cs` 同步删掉 `_paneStateToken`、ctor 里的 `RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, …)`、`OnWindowClosed` 里的注销、`OnRootGridLoaded` 里的 `UpdatePinCompactLayout()` 调用，以及 `OnNavigationPaneOpenChanged` / `UpdatePinCompactLayout()` 两个方法本体（**单入口，不再有紧凑模式重排**）。
- `Views/SettingsPage.xaml:157` 说明文字改为「…点标题栏右上角的图钉或按 Ctrl+Shift+T 可随时取消。」

**必须自己写 FixTitleBarCaptionPadding（官方缺陷 #10344，至今 Open）**

- 报的是 microsoft-ui-xaml issue #10344「TitleBar control calculation of padding is incorrect」：TitleBar.cpp 里
  `rightColumn.Width(winrt::GridLengthHelper::FromPixels(rightColumnInset))` 把 `AppWindowTitleBar.RightInset`（**物理像素**）
  直接当 DIP 用，没除以 RasterizationScale。
- 150% DPI 实测症状：`RightInset=207`、`RasterizationScale=1.5`、模板第 11 列实际宽 207.3 逻辑像素
  ⇒ 图钉被推到离三个系统按钮 **103 物理像素**（多出约 69 逻辑像素）之外，视觉上像「飘在中间」。
- 修复（`src/PdxLauncher.App/MainWindow.xaml.cs`）：
  - `FixTitleBarCaptionPadding()`：`VisualTreeHelper.GetChild(AppTitleBar, 0) as Grid` 拿模板根 Grid，把第 0 列与第 11 列宽度设成
    `inset / RootGrid.XamlRoot.RasterizationScale`（LTR 时左列用 LeftInset、右列用 RightInset；RTL 交换）。
  - `HookCaptionPaddingUpdates()`：挂 `RootGrid.XamlRoot.Changed` + `AppTitleBar.SizeChanged` 重算；字段 `_captionPaddingHooked` 防重复挂；
    `OnWindowClosed` 里解绑。**必须自己挂**，因为框架的 `UpdatePadding()` 只在 OnApplyTemplate 与 OnFlowDirectionChanged 时跑
    （源码里就是 `// TODO 50724421: Bind to appTitleBar Left and Right inset changed event.`），DPI 迁移 / 最大化后不会自动重算。
  - 时机：`OnRootGridLoaded` 里紧跟 `RootGrid.Loaded -= OnRootGridLoaded;` 之后调用。框架源码 `TitleBar.cpp` 的 `OnApplyTemplate` 顺序是
    `UpdateHeight(); UpdatePadding(); …`，所以 ctor 里 `InitializeComponent()` 后立刻 `ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar);`
    这个现有顺序不能动（否则 RightInset 为 0，右留白列算成 0，图钉会盖到系统按钮上）。

**实测（Release 构建 0 警告 0 错误；窗口 1800x1200 物理 @150% DPI）**

| 场景 | btnAlwaysOnTop UIA | Minimize UIA | 间隙 |
| --- | --- | --- | --- |
| 修复前（RightInset 当 DIP） | (1457,118 60x42) | (1620,38 69x48) | 103 物理 ≈ 69 逻辑 |
| 修复后（÷ RasterizationScale） | (1485,42 60x42) | (1620,38 69x48) | 75 物理 = 50 逻辑（48 + 2） |
| 再把 MinDragRegion 收到 8 | (1925,422 59x42) | (2000,418 69x48) | 16 物理 ≈ 10.7 逻辑（8 + 2） |
| 最大化（同进程再 inspect） | (3486,3 60x42) | (3561,0 69x48) | 15 物理 ≈ 10 逻辑 ⇒ **自适应成立** |

- 标题栏总高仍是 32 逻辑像素（48 物理），图钉 28 高垂直居中。
- `winapp ui invoke btnAlwaysOnTop -a <pid> --action toggle-on` 后 UIA 变 `btnAlwaysOnTop Button "取消窗口置顶" [on]`，状态同步链路照旧可用。
- 截图：`.scratch\pa-ui\shot3.png`（GDI 全窗 1800x1200）、`.scratch\pa-ui\crop-tb-final5.png`（3x 放大，可见图钉紧贴系统按钮区左侧）。
  **踩坑**：`winapp ui screenshot` 只截客户区（1782x1191）不含 caption buttons；`PrintWindow(hwnd,hdc,2)` 在 WinUI 3 上出陈旧帧 / 黑块；
  PowerShell 不 DPI-aware 时 `GetWindowRect` 返回虚拟化坐标（1200x800），必须先 `SetProcessDPIAware()` 再 `CopyFromScreen`。

**顺带修掉的既有编译错误**：`src/PdxLauncher.App/Services/ThemeUpdateService.cs:196`
`private static Task RunOnUiAsync(Func<bool> action)` → `private static Task<bool> RunOnUiAsync(Func<bool> action)`
（L126 `var applied = await RunOnUiAsync(...)` 报 CS0815「无法将 void 赋予隐式类型化的变量」）。与本次改动无关，但不修构建不过。

**构建 / 自检**：`dotnet build "PdxLauncher.sln" -c Release -m:1 --no-restore` → 0 警告 0 错误（构建前必须先 `taskkill /F /IM PdxLauncher.App.exe`，
否则 MSB3027/MSB3021 文件占用）；`dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll` → 7 个检查类 / 0 失败。
备份：`.scratch\rework\backup\2026-10-26-titlebar-rightheader\`（MainWindow.xaml 190 行 / MainWindow.xaml.cs 510 行 / SettingsPage.xaml 371 行 / HANDOFF.md 771 行）。

---

## 0.7 最新一轮（2026-10-26 17:40）：联网主题真正接进主窗口 —— 「每个版本都换的新背景」生效了

> **本节是主题子系统的当前真相**，压过 §0.3 / §2.1 里「主窗口只读安装目录主题」的旧描述。
> 与 §0.6 是两件互不相干的事（§0.6 改的是主页布局与公告卡，本节改的是主题素材来源与刷新时机）。

**用户诉求（m00001 原话）**：「我记得游戏每个版本都有新的背景，你似乎是获取默认的启动器背景，你找下原版启动器是怎么实现的」。
（这里的「原版」= 官方 Paradox Launcher v2 = `paradox-launcher-v2` Electron 应用。）

### 0.7.1 官方是怎么做到的（反编译结论，全部有本机实据）

1. **素材不是安装目录里那张图**。官方 GraphQL `GetMetadata`（`https://api.paradox-interactive.com/metadata/graphql`）
   返回的 `themeSettings.*` 字段是**服务端下发的 URL 字符串**，形如
   `https://metadata-assets.paradox-interactive.com/victoria3/launcher-background-image-f31101b1346f541572e1`
   （无扩展名，路径末尾 20 位是内容 hash ⇒ **游戏一发新版本，服务端换图、URL 跟着变**，所以每版都有新背景）。
2. **裸 GET，无签名无鉴权**：`getMetadataImage(url)` 内部就是 `http.get(url, {responseType:'stream'}, {useAuth:false})`。
   所以本项目用现有的 `IAssetDownloader` 直接下即可，不需要抄它的鉴权。
3. **下载后回填本地绝对路径**：`getAssets(game)` 产出 **12 个素材描述符**
   （`appIcon / appTaskbarIcon / background / logo / buttonBackgroundImage / buttonFont /
   buttonHoverBackgroundImage / buttonHoverFont / buttonDisabledBackgroundImage / buttonDisabledFont /
   buttonTextBackgroundImage / buttonTextFont`），每个带 `fieldPath: "themeSettings.xxx"`；
   `storeAsset` 下到 `cache/<gameId>-<assetName>` 后，把那个 fieldPath **改写成磁盘绝对路径**再写回
   `game-metadata` 缓存。渲染层只认本地路径。
4. **TTL 5 天 + stale-while-revalidate**：`pve = 1e3*60*60*24*5`；过期时**先返回旧缓存**、后台再刷。

**本机对照（决定性证据）**：官方 cache `%APPDATA%\Paradox Interactive\launcher-v2\cache\ck3-background`
（1,079,537 B，2560x1340，木偶戏／国王与主教）与安装目录
`E:\SteamLibrary\steamapps\common\Crusader Kings III\launcher\assets\app-background.png`
（3,342,628 B，2408x1340，红幕前的国王半身像）**是两张完全不同的图**。Victoria 3 同理。用户记的没错。

### 0.7.2 之前为什么不生效（4 条根因）

1. `ThemeResolver.ResolveAsync` 全仓只有设置页在调，**主窗口 / 主页 / 游戏库全都直接读 `game.Theme`**（= 安装目录层），
   所谓「三层合并」的联网层从来没进过渲染路径。
2. 代理只在 `App.xaml.cs` 里解析一次，设置页改完 `ProxyUrl` 后 `HttpFetcher` 不重建（`SocketsHttpHandler` 构造时就固定代理）。
3. `%LOCALAPPDATA%\PdxNext\theme-cache` 目录因此**从未被生成过**。
4. 缓存文件名是 `<gameId>-<key>` 这种稳定名 ⇒ 就算换了图，`BitmapLoader` / WinUI / `FontLoader` 的按路径缓存也不会失效。

### 0.7.3 这轮改了什么

**Core（纯逻辑，可被 Checks 覆盖）**

- `ThemeResolver` 缓存文件名改为 `<gameId>-<assetKey>-<urlHash8>.<ext>`：`<urlHash8>` = URL 的 SHA-256 前 4 字节
  小写十六进制（`Convert.ToHexStringLower(digest.AsSpan(0, 4))`，见 `:362 ShortHash`）。
  **URL 变 ⇒ 文件名变 ⇒ 所有按路径缓存自动失效**，不用手工 `ClearCache()`。
- `IThemeResolver` 新增 `DateTimeOffset? GetLastCheckedUtc(string gameId);`。
  主题上次检查时间只存在 `<gameId>.assets.json` 里，**不再写 settings.json**
  （`AppSettings.LastThemeCheckUtc` / `GetLastThemeCheck` / `SetLastThemeCheck` 已删）。
- 新增 `src/PdxLauncher.Core/Themes/ThemePatch.cs`（53 行）：
  `Apply(IReadOnlyList<GameInstallation> games, string gameId, ThemeSpec theme)` —— 主题值相等或 gameId 未命中时
  **原样返回同一个 list 实例**（调用方靠 `ReferenceEquals` 判断有没有变化），命中则只替换那一项。
- `AssetFileTypeDetector.MinimumFileBytes` **4 KB → 1 KB**（1024）。原因见 §0.7.6 的 D10。

**App（接线）**

- 新增 `src/PdxLauncher.App/Services/ThemeUpdateService.cs`（222 行，`internal static`）：
  - `ApplyCachedLayerAsync(games, ct)`：**零网络**，逐款 `ResolveAsync(allowOnlineRefresh: false)` 把缓存层贴进
    `game.Theme`，失败只 `Warn` 不影响启动。
  - `StartBackgroundRefresh()`：进程内只启动一次（`Interlocked.Exchange(ref _backgroundStarted, 1)`），拿不到锁就安静退出。
  - `RefreshAsync(only, ct)`：当前游戏排最前，**每完成一款就立刻回写 +`NotifyThemesChanged`**，不等其它游戏；
    `RunOnUiAsync` 保证回写与事件都在 UI 线程（`DispatcherQueue.TryEnqueue` + `TaskCompletionSource`）。
- `GameDiscovery.LoadAsync`（:42-50）在 `DiscoverAsync` 之后插两步：
  `ApplyCachedLayerAsync` → `SetGames(themed)`（**一次性**，避免连发两次 `CurrentGameChanged`）→
  `_completed = true` → `StartBackgroundRefresh()`。冷启动仍然零网络（`App.xaml.cs` 类注释的硬约束没破）。
- 新增 `src/PdxLauncher.App/Services/NetworkComposition.cs`：把 proxy→HttpFetcher→Metadata/Assets/Theme/DlcDescription/NewsFeed
  这条链收成 `NetworkServices` 记录，`App.xaml.cs` 只调一次 `NetworkComposition.Create(...)`。
- `AppServices` 新增 `event EventHandler<string>? ThemesChanged` + `NotifyThemesChanged`、
  `ReplaceNetwork(...)`、`RebuildNetworkServices()`（重建整条联网链并 Dispose 旧的），
  `Initialize` 尾部加第 18 个可选参数 `IDisposable? networkLifetime`。
- 设置页改代理地址（`SettingsPage.OnProxyUrlLostFocus`）改为调 `SettingsViewModel.ApplyProxySettings()`
  = 落盘 + `RebuildNetworkServices()` ⇒ **新代理对下一次联网立即生效**，不用重启。
- 设置页「立即检查主题」改为走 `ThemeUpdateService.RefreshAsync(Game, ct)`（原来是自己调 Resolver、还写 settings）；
  上次检查时间改从 `GetLastCheckedUtc` 读。
- `GameLibraryPage` 订阅 `ThemesChanged` → `LibraryViewModel.ReloadFromCurrentGames()`（只重建卡片，不重扫 Steam 库、不联网）。
- 设置页 `LoadAsync` 不再发网络请求（原来打开设置页就会解析一次主题）。

### 0.7.4 主窗口/主页为什么不用改

`ThemeUpdateService` 回写用 `SetGames(themePatch 出的新 list)`，而 `SetGames` 内部 `ReconcileCurrentGame` 按
`(GameId, InstallRoot)` 重新定位当前游戏；`ThemePatch` 会把该项换成**新 record 实例**，
于是 `SelectGame` 的 `ReferenceEquals` 早退失效 ⇒ **`CurrentGameChanged` 照常触发** ⇒
`MainWindow.OnCurrentGameChanged` → `UpdateBackdropAsync()`、`HomePage` / 设置页等各自重载。
这就是「后台刷新完，整窗背景自己就换了」的机制，不需要给主窗口再加监听。

### 0.7.5 真机验证证据（2026-10-26 16:42 / 17:01，Release）

日志 `%LOCALAPPDATA%\PdxNext\logs\pdxnext.log`：

```
16:42:35.605 [INFO] 素材下载完成: ...\theme-cache\ck3-background-bf4c6129.jpg (1079537 B, Jpeg)
16:42:38.659 [INFO] 主题缓存已更新: ck3，素材 5 项
16:42:38.669 [INFO] 已换用新的主题素材: ck3（来源=OnlineCache）
16:44:36.587 [INFO] 已换用新的主题素材: victoria3（来源=OnlineCache）
16:44:36.587 [INFO] 后台主题刷新完成: 更新 3 个游戏
17:01:49.633 [INFO] 素材下载完成: ...\theme-cache\ck3-button-dca8c880.png (3818 B, Png)
```

`%LOCALAPPDATA%\PdxNext\theme-cache` 首次真正落盘（此前目录不存在）：
`ck3-background-bf4c6129.jpg` 1079537 / `ck3-logo-3969bc62.svg` 16602 / `ck3-appIcon-f8a49238.png` 26708 /
`ck3-buttonFont-7e53a34e.ttf` 653252 / `ck3-button-dca8c880.png` 3818 /
`stellaris-background-20275cd8.jpg` 1427781 / `stellaris-logo-be77becd.png` 39732 /
`victoria3-background-01f1dd9c.jpg` 1851934 / `victoria3-logo-779eafd5.png` 161431 / 各 `<gameId>.assets.json`。

**sha256 对照**：我方 `ck3-background-bf4c6129.jpg` = `45C3C6975E7B440905B92061D93D6A2ABB1E6C3D8EC25810899EFE222A961B63`，
官方 `cache\ck3-background` 同大小同哈希 ⇒ **拿到手的图和官方启动器一模一样**。
截图 `.scratch\theme-after.png`（1782x1191）：标题栏 `PdxNext · Crusader Kings III · 版本 1.20.1.2 (Crozier)`，
整窗背景已是木偶戏新图 + CK3 字标。

冷启动复验：`主题缓存层已应用: 3 个游戏使用了联网缓存素材` + `后台主题刷新完成: 更新 0 个游戏`（TTL 内零请求）。

**构建与自检（2026-10-26 17:47 实测）**：`dotnet build PdxLauncher.sln -c Release` → **0 警告 0 错误**
（把 §0.6 那批「没编译过」的主页/公告改动也一起编过了）；`PdxLauncher.Checks.dll` →
`检查类: 9，失败: 0，断言失败总数: 0`（`ThemePatchChecks` 的 10 条断言全绿）。
注意：首次构建曾一次性报 59 条 CS0103（`MainNavigation` / `AppTitleBar` / `ShellBackgroundImage` 等 XAML 生成字段全部找不到），
再跑一次即 0 错 —— 是并发会话正在改 XAML 时生成器状态不一致，**遇到这类「全是 XAML 字段名找不到」的批量错误，先重跑一次再判断**。

### 0.7.6 已知缺陷（新增，接 §7）

- **D12 · 1 KB 阈值（已修，留作记录）**：官方 `ck3-buttonBackgroundImage` 只有 3,818 B、
  `stellaris-buttonBackgroundImage` 4,495 B，都是**合法 PNG**。原来的 4 KB 下限把它们判成坏文件
  （日志 `素材小于 4 KB，视为坏文件: 3818 B …`），CK3 的按钮背景图一直退回安装目录那张。
  阈值已降到 1 KB，`OnlineChecks` 加了 3,818 B 的回归用例。
- **D13 · 只实现 6 / 12 个素材**：官方 `getAssets` 有 12 个描述符，本项目 `OverridableKeys` 只有
  `appIcon / appTaskbarIcon / background / logo / button(buttonBackgroundImage) / buttonFont(button.fontPath)` 六个；
  缺 `buttonHover* / buttonDisabled* / buttonText*` 三组。悬停/按下/文字按钮仍用安装目录或系统配色。
- **D14 · 「立即检查」会顺带刷全部游戏**：`ThemeUpdateService.OrderGames(only)` 只把指定那款**排最前**，
  不清空其余，所以设置页点一次检查 = 串行刷新所有已安装游戏。16:42 那次实测跑了 2 分 8 秒
  （`16:42:28`→`16:44:36`）。网络慢的用户会觉得按钮卡住。
- **D15 · 进程内只自动刷新一次**：`StartBackgroundRefresh` 有 `_backgroundStarted` 一次性闩，
  所以游戏库里手动「刷新」（走 `GameDiscovery.RefreshAsync`）**不会**重新检查主题，
  同一进程内跨过 5 天 TTL 也不会自动再刷。要等重启或手动点设置页的检查。

### 0.7.7 文档

`docs/DATA-FORMATS.md` §6/§7 已同步：联网覆盖键由「9 个」改回**实测的 6 个**并写明官方 12 个描述符的差异；
缓存路径改为 `<gameId>-<assetKey>-<urlHash8>.<ext>` 并解释了为什么带 hash；
补充 `<gameId>.assets.json` 记录 `url / fileName / lastCheckedUtc`、「主题检查时间不写 settings.json」；
magic 阈值改为「拒绝 <1 KB 或 >40 MB」并写明 3,818 B 的实测依据。

---

## 0.6 最新一轮（2026-10-26 17:30）：主页对齐官方启动器布局 + 底部三张官方公告卡

> ⚠ **`dotnet build` 到收尾仍未跑成**（沙箱禁命名管道 → MSB3883，见 §4）。改用离线手工链验证：
> `csc` 编 Core **0 错 0 警告** → `XamlCompiler.exe` pass1 编 8 个 xaml（17 个生成文件，含新的 `Views/HomePage.g.i.cs`，
> 里面有 NewsRegion / btnNewsMain / NewsHoverRingMain 等新控件字段）→ `csc` 编 App **0 错 0 警告**；
> `PdxLauncher.Checks` 编译并运行：**`检查类: 9，失败: 0，断言失败总数: 0`**（公告自检 81 条断言全绿）。
> 第一次跑时 `GamesChecks` 有一条失败（stellaris 缺 dlcPath 的路径比较），排查后确认是**离线跑的假失败**：
> `ChecksContext.FixturePath` 从 `AppContext.BaseDirectory` 往上找 `tests/fixtures`，而手工链把 dll 放在临时目录，
> 找不到就退化成相对路径，与绝对路径的 `DlcPath` 比较必然不等。把 `tests/fixtures` 拷到 dll 旁边后 9 类全绿。
> 真机 `dotnet build` 时 dll 在 `tests/PdxLauncher.Checks/bin/...`，祖先链里有仓库根，不会触发。
> ⚠ 仍未验证的是**运行期**：pass1 跳过 x:Bind 代码生成（生成的 `.g.cs` 为 0 字节），绑定表达式只做了逐条人工核对；
> XBF 与真机 UI 都没跑。**下轮第一件事仍是 `build.cmd` + `run.cmd` 做 §0.6.4 的真机验证。**
> 另外改动前没能建备份目录（沙箱限制，见 §6），回滚只能手工反改。
>
> **本轮踩到的坑（写下来省下一轮）**：`x:Bind` 对引用类型路径**不生成空值检查**（生成的代码里一个 `?.` 都没有），
> 所以 `{x:Bind ViewModel.NewsMain.Title}` 这类写法一旦 `NewsMain` 为 null 就会在绑定刷新时抛 NullReferenceException。
> 故三张公告卡改用「永不为 null 的空卡占位」：`NewsCardItem.Empty(slot)` + `IsEmpty`，VM 里三个属性默认值和清空都换成空卡。

**用户诉求（m00005）**：「启动器的的主页应该参考原版启动器的布局，中间依次排列游戏图标、继续（左）、开始游戏（右）播放集选择；
继续和开始游戏采用每个游戏自己的特殊美化；底下反编译原版启动器的内容，获取它们的信息，也一块放底下」。
用户附了两张官方启动器截图：CK3（米色「继续游戏」+ 深灰「开始游戏」+ 三张统计卡 + 最近存档条）与 Victoria 3
（红底花纹的「继续」/「开始游戏」+ 播放集下拉 + 右下角版本号 + 底部三张宣传卡）。

### 0.6.1 做了什么

1. **主页重排成官方骨架**（`Views/HomePage.xaml` 整体重写）：居中字标 → 「继续」(左) + 「开始游戏」(右) 居中并排 →
   播放集下拉 → 三张统计卡 → 最近存档条 → **底部三张公告卡**（左大 2*/右二各 1*）→ 状态文案。「刷新」移到右上角。
   控件仍是标准 `Button`/`ComboBox`/`Border`，不自绘。
2. **两个按钮按当前游戏主题美化**：`HomePage.xaml.cs` 新增 `ApplyThemedButton(Button, ThemeVisuals)`，把
   `visuals.ButtonBackground` / `ButtonForeground` / `ButtonFontFamily`/`ButtonFontSize`/`ButtonFontWeight` 应用到
   `ContinueButton` 与 `StartButton`，**并覆盖三组资源键**（`ButtonBackground`/`AccentButtonBackground`、
   `ButtonBackgroundPointerOver`/`ButtonBackgroundPressed`/各种 `ButtonBorderBrush`、
   `ButtonForegroundPointerOver`/`AccentButtonForeground*`）—— 不覆盖的话一悬停就变回系统灰。
3. **底部公告卡接官方 Braze content cards**：新服务 `NewsFeedService` 拉
   `https://api.paradox-interactive.com/communication/braze/contentcards/<gameId>`，解析 `main` / `secondary-1` /
   `secondary-2` 三个 slot（时间窗 + weight 选组），图片经 `IAssetDownloader` 落盘到
   `%LOCALAPPDATA%\PdxNext\news-cache\images\<md5(url)>`，6 小时 TTL、404 负缓存、失败 30 分钟退避，
   设置页可一键关闭联网（关闭后绝不发请求）。实测 ck3/stellaris/eu5/hoi4/imperator_rome/aow4 有公告，
   victoria3/eu4/millenia/cities_skylines_2 返回 404（主页公告区整块隐藏）。
   数据格式与实测记录见 `docs/DATA-FORMATS.md` §7.2。

### 0.6.2 本轮改/新增的文件

| 文件 | 改动 |
| --- | --- |
| src/PdxLauncher.Core/Contracts/NewsFeed.cs **（新）** | `NewsFeedItem(Slot, Title, Text, ImagePath, ImageUrl, Link)` + HasImage/HasTitle/HasText/IsOpenable（只认绝对 http/https URI）；`NewsFeed(IReadOnlyList<NewsFeedItem> Items)` + `Empty` + `IsEmpty` + `Find(slot)`；`NewsFeedSlots { Main="main"; Secondary1="secondary-1"; Secondary2="secondary-2"; Order }` |
| src/PdxLauncher.Core/Abstractions/INewsFeedService.cs **（新）** | `string CachePath { get; }` + `Task<NewsFeed> GetAsync(GameInstallation game, bool allowOnline, CancellationToken ct)` |
| src/PdxLauncher.Core/Online/NewsFeedService.cs **（新，约 660 行）** | 照 `DlcDescriptionService` 的写法：`_batchLock` 串行整批 → `LoadCache` → 关联网/命中 TTL/失败退避三条短路 → `FetchAsync`；`JsonDocument` 手工解析（title/text 兼容字符串与多语言对象，优先 en）；图片走 `IAssetDownloader`，文件名 = 整条 URL 的 MD5（接口里那个 hash 段会随内容变）；缓存 `news-cards.json`（`{version, entries:{gameId:{fetchedUtc,lastFailureUtc,items[]}}}`） |
| src/PdxLauncher.Core/Online/OnlineHosts.cs | 白名单加 `BrazeImagesHost = "braze-images.com"`（精确匹配，子域仍拒绝） |
| src/PdxLauncher.Core/Online/AssetFileTypeDetector.cs | 加 `Gif` 判型（"GIF8"）与 `.gif` 扩展名 —— 公告 main 图是 .gif |
| src/PdxLauncher.Core/Configuration/AppSettings.cs | 加 `public bool NewsFeedOnlineEnabled { get; set; } = true;`（紧邻 `OnlineDlcDescriptionEnabled`）；**没有**加 LastNewsCheckUtc 字典（TTL/退避都在公告缓存文档里） |
| src/PdxLauncher.App/Services/NetworkComposition.cs | `NetworkServices` record 加 `NewsFeedService NewsFeed`；`Create` 里 `new NewsFeedService(fileSystem, http, assets, clock, logger, AppServices.NewsCacheRoot)` |
| src/PdxLauncher.App/Services/AppServices.cs | 加 `public static INewsFeedService NewsFeed`、`public static string NewsCacheRoot => Path.Combine(DataRoot, "news-cache")`；`Initialize` 变 18 参；`ReplaceNetwork` / `RebuildNetworkServices` 同步加参数与实参 |
| src/PdxLauncher.App/App.xaml.cs | `AppServices.Initialize(...)` 里补 `network.NewsFeed` |
| src/PdxLauncher.App/Services/NewsCardItem.cs **（新）** | 纯数据投影（仿 `GameCardItem`）：`NewsCardItem(Slot, Title, Text, ImagePath, Link)` + HasImage/HasTitle/HasText/IsOpenable、`AutomationIdCover => "imgNews_" + Slot`、`static FromFeedItem(NewsFeedItem?)`、`TryGetLink(out Uri)`、`ToString()` 给无障碍名 |
| src/PdxLauncher.App/ViewModels/HomeViewModel.cs | 加 `NewsMain`/`NewsSecondary1`/`NewsSecondary2`（字段式 `[ObservableProperty]`，**不能用 partial property**：CommunityToolkit.Mvvm 8.4.2 + `NoWarn=MVVMTK0045`）+ 派生 `HasNews`/`HasNewsMain`/…；`LoadAsync` 末尾 `_ = LoadNewsAsync(...)` **fire-and-forget**（公告是装饰性弱依赖，绝不让主页等网络）；`ClearState` 清空三字段 |
| src/PdxLauncher.App/Views/HomePage.xaml | 整体重写：页头居中 + 右上刷新、启动卡片两行居中（删掉「启动游戏」小标题）、统计三卡、最近存档条、公告 Grid(2*/1*/1*)、状态文案。公告卡 = `Button`（`Padding=0`、`Stretch` 对齐、`Resources` 里 8 个 Button* 画笔键全设 Transparent 压掉系统底色）套 `Border`（`MinHeight=230`）+ 图/文两行 + `NewsHoverRing*` 叠加环；既有 AutomationId 全部保留 |
| src/PdxLauncher.App/Views/HomePage.xaml.cs | `ApplyThemedButton` + 三组资源键数组；公告点击 `OpenNewsLink`（只放行 http/https，`pdxlaun://` 只记 Info 不执行；`ProcessStartInfo` + `UseShellExecute=true`，同 `SettingsPage` 打开资源管理器的写法）；hover 用 `FindHoverRing` 按 `"NewsHoverRing"` 前缀找叠加环（**不改卡片 Background** —— ThemeResource 画笔在代码里查不到）；`UpdateNewsLayout()` 在只有 main 时把它跨三列 |
| src/PdxLauncher.App/ViewModels/SettingsViewModel.cs | 加 `NewsFeedOnlineEnabled`（set 里写 settings + `SaveSettings` + StatusMessage），并进 `RaiseThemeProps` |
| src/PdxLauncher.App/Views/SettingsPage.xaml | DLC 简介说明之后加 `ToggleSwitch`（AutoId `tglOnlineNewsFeed`，OnContent「联网获取官方公告」/ OffContent「仅本地缓存公告」）+ 一行 Caption 说明 |
| tests/PdxLauncher.Checks/NewsFeedChecks.cs **（新，648 行）** | 9 组检查：解析 / 时间窗与 weight / 缺 slot 容错 / 404 负缓存 / 关闭联网 / 6 小时 TTL / 30 分钟失败退避 / 图片落盘与复用 / 缓存往返（含坏 JSON）。自带 `RecordingLogger`/`FakeClock`/`FakeHttpFetcher`/`FakeAssetDownloader`；由 `Program.cs` 反射自动发现，**无需改 Program.cs** |
| tests/PdxLauncher.Checks/OnlineChecks.cs | magic bytes 加 GIF（`GIF89a`）+ `GetExtension(Gif)==".gif"`；白名单放行 `https://braze-images.com/…/original.gif?1`，负例加 `https://cdn.braze-images.com/x.gif`（子域不放行） |
| tests/fixtures/online/contentcards-*.json **（新 4 个）** | `ck3`（真实抓取样本）、`expired`（时间窗全过期）、`minimal`（只有 main 且是纯文字）、`broken`（截断 JSON） |
| docs/DATA-FORMATS.md | 新增 §7.2 官方公告（端点 / slot / 时间窗与 weight / 内容字段兼容 / 404 与实测清单 / 缓存与图片命名 / 6 小时 TTL / 30 分钟退避 / 关闭联网 / 深链 / GIF 限制）；§7 白名单补 `braze-images.com`，magic 补 `GIF8` |
| README.md | 主题节补「主页继续/开始按钮按主题渲染」；界面节补官方骨架与公告卡；目录计数改为 Core 91 / App 38+8 / Checks 8；自检节补联网子系统的覆盖项 |
| docs/HANDOFF.md | 本节 |

### 0.6.3 设计决定（写下来免得下轮推翻）

- **沿用 `DlcDescriptionService` 的生命周期，不引入定时器**：公告只在「主页加载」与「点刷新」时取一次，TTL 6 小时挡重复请求。
  主题子系统也是这样（页面加载时按需解析），整个应用没有后台轮询循环。
- **公告是弱依赖**：`HomeViewModel.LoadAsync` 里 fire-and-forget，公告失败只 `Warn`，不弹 InfoBar、不影响主页其余部分
  （措辞与 `DlcViewModel.FillDescriptionsAsync` 一致：「主页公告获取失败（只影响公告区）」）。
- **卡片 hover 一律用叠加环**：`ThemeResource` 画笔在 code-behind 里按主题查不到（ThemeDictionaries 不走
  `ResourceDictionary.TryGetValue`），查表失败会静默没有 hover —— 这是 `GameLibraryPage` 踩过的坑。
- **`pdxlaun://` 深链不交给浏览器**：`Process.Start` 一个自定义 scheme 会弹系统「找不到应用」对话框，直接禁用按钮 + 记 Info。
- **图片用整条 URL 的 MD5 命名**，不用接口返回的 hash 段（那个会随内容变），命中同名文件就复用不重下。

### 0.6.4 下轮必须做的验证（本会话做不了）

1. `build.cmd` → 0 错 0 警告；`PdxLauncher.Checks.dll` → `检查类: 9，失败: 0`（离线链现已 9 类全绿，见 §0.6 的 ⚠ 块；注意手工链要先把 `tests/fixtures` 拷到 dll 旁边，否则路径比较类断言会假失败）。
2. `run.cmd` 选 CK3：字标居中、两个按钮是**深炭纹理底 + 米色字**（§0.10 已按官方语义重写；悬停变亮、按下缩到 0.85，**不得变回系统灰或蓝**）、底部 3 张公告卡
   （main 大图 + 右二）、点 main 卡开浏览器、点 secondary-2 无反应但日志有「公告链接不是 http(s)」。
3. 切 Victoria 3：两个按钮变红底花纹、公告区整块消失、日志有「该游戏没有官方公告（HTTP 404…）」。
4. 切 Stellaris：公告出现（不同图）。
5. 设置页关掉「联网获取官方公告」→ 抓包/日志确认不再请求 `contentcards`；重开恢复。
6. 截图存 `.scratch\rework\`（`caps_all.ps1`，须窗口最前，`CopyFromScreen`）。

盲区：`ApplyThemedButton` 覆盖资源键这一招**没在真机上验证过**。若按钮悬停仍有系统底色，退路是给两个按钮各写一个约 15 行的
`ControlTemplate`（`Border` 套 `ContentPresenter`，只留 PointerOver/Pressed 两个 VisualState 改 `Border.Opacity`）——
实现前先加载 `winui-design` skill 核对 API。

---

## 0.5 最新一轮（2026-10-26 16:20）：五个 `.cmd` 全部改成纯 ASCII + `scripts\messages.txt` 消息表

**现象**：跑 `build.cmd` 时偶发（约 0.7%，且**第一次运行最容易中**）冒出
`'行文件:' is not recognized as an internal or external command` / `'句号' ...` 之类的假报错。
构建本身仍然 `exit 0`、自检仍然 `0 失败`，纯粹是显示层的噪声，但观感很差。

**根因（本轮定量定位，决定性结论）**：cmd.exe 执行批处理时**按当前代码页逐字节重读文件**。
顶层 `.cmd` 里只要含非 ASCII 字节，在「控制台起始代码页不是 UTF-8（如 `chcp 936` 的窗口）」
+「文件刚被写入就立刻执行（写后竞态）」叠加时，中文多字节序列会被拆开，字节组合吞掉行边界，
把后面的 `echo` / `rem` 行撕成碎片 → 一堆「不是内部或外部命令」。
**不是编码声明问题，不是 BOM 问题，也不是 `chcp 65001` 缺失问题**。

实验证据（实验目录 `.scratch\_cmd_enc_lab\` 已在收尾时删除，结论保留如下）：
- 老版 UTF-8 + `chcp 65001` 的 build.cmd：300 次里 299 对；200 次对比里 199 对；dry 副本 150 次里 149 对。
- 纯 ASCII 脚本（`type` 外部消息文件版 / 纯 ASCII 版）：200/200 全对，任意起始代码页都 0 撕裂。
- GBK 存盘 + `chcp 936`：同样 200/200，但**否决**——文件编码会被编辑器 / git 改回 UTF-8，不稳定。
- UTF-8 带 BOM：无改善（14/15）。
- 「ASCII stub 设 `chcp` 后 call 子目录里的中文 inner」：100/100，但把同样的设计挪到 `scripts\` 子目录再测
  就退化成 199/200 且撕裂 ⇒ **wrapper 方案不可靠，不可用**。

**已落盘（新架构）**

- `scripts\msg.cmd`（618 B，**纯 ASCII**）：`call msg.cmd KEY [extra]`，
  `for /f "usebackq tokens=1,* delims=:"` 读 `%~dp0messages.txt`，
  命中则 `echo %%b`；带第二参数时输出 `%%b %~2`。
- `scripts\messages.txt`（1781 B / 41 行，**UTF-8 无 BOM**，`KEY:文本` 格式，`#` 开头为注释）。
  现有键：`B_TITLE B_NODOTNET B_STEP1 B_FAILBUILD B_STEP2 B_FAILCHECK B_STEP3 B_EXE` /
  `D_TITLE1 D_NODOTNET D_TITLE2 D_EXE` / `R_NOEXE R_EXPECT` / `RD_NOEXE RD_EXPECT` /
  `P_TITLE P_NODOTNET P_STEP1 P_FAILBUILD P_STEP2 P_FAILCHECK P_CHECKOK P_STEP3 P_FAILPUB
  P_STEP4 P_FAILDIR P_DONE P_EXE P_SIZE P_BROKEN1 P_BROKEN2 P_BROKEN3`（共 33 个在用键，0 缺失）。
- 五个顶层脚本全部改写为**纯 ASCII（0 个 >127 字节）、CRLF**：
  `build.cmd` 1699 B / `build-debug.cmd` 642 B / `run.cmd` 382 B /
  `run-debug.cmd` 380 B / `publish.cmd` 2975 B。
  所有中文改成 `call "%~dp0scripts\msg.cmd" KEY`，`rem` 注释全部改英文。
- 备份：`.scratch\rework\backup\2026-10-26-ascii-msgs\`（五个改前原件：1366 / 545 / 373 / 325 / 3011 B）。

**回归（全部实测通过，起始 cp936 的窗口里跑）**

| 脚本 | 结果 |
| --- | --- |
| `build.cmd` | `BUILD_EXIT=0`，撕裂 0 处，`检查类: 6，失败: 0，断言失败总数: 0` |
| `build-debug.cmd` | `EXIT=0`，撕裂 0 处，Debug 自检 0 失败 |
| `run.cmd` | `RUN_EXIT=0`，`PdxLauncher.App.exe` PID 25856 起来 |
| `run-debug.cmd` | `RUNDBG_EXIT=0`，PID 47912 起来 |
| `publish.cmd .scratch\_publish_out` | `PUB_EXIT=0`，产物 538 个文件，`App.xbf` / `PdxLauncher.App.pri` / `Views\HomePage.xbf` / `Assets\AppIcon.ico` 全在 |
| 缺 exe 的失败分支 | `run.cmd` / `run-debug.cmd` 均 `EXIT=1` + 正确中文提示 |
| `msg.cmd` 连跑 20 次 | 撕裂 0/20 |

**今后铁律**：顶层 `.cmd` 里**不许出现任何非 ASCII 字节**，中文一律走
`scripts\messages.txt` + `scripts\msg.cmd`；`rem` 行里也别写 `<` `>` `|` 这类元字符。
新增提示就往 `messages.txt` 加一行，然后 `call "%~dp0scripts\msg.cmd" KEY`。

**文档同步**：README 第 3 节目录结构加了 `scripts\` 两条 + 「顶层 .cmd 必须纯 ASCII」说明；
第 4 节自检计数 5 → 6；第 7 节排错新增一条「跑 .cmd 时冒出 '行文件:' 一类假报错」（含根因、实测数据、本项目的做法）。

---

## 0.0 最新一轮（2026-10-26 14:50）：工作区被设成 Low IL 导致 .cmd 弹「安全警告」—— 已修复

> 本节独立于代码进度（只讲目录/环境）。本节优先级让给 §0.4。用户上传了双击 `run.cmd` 时 Windows 弹出的
> 「打开文件 - 安全警告 / 未知发布者 / Windows 命令脚本」截图，问怎么解决。**根因不在代码，在目录的完整性级别。**

**根因**：`G:\Work\paradox启动器` 整个目录树带 **Low 完整性标签（Low IL）**，
而父目录 `G:\Work` 是默认 Medium。Explorer 用 `ShellExecute` 启动脚本时会校验发布者，
Low IL 路径下的脚本一律被判为「未知发布者」→ 弹安全警告。**改回 Medium 后弹窗消失。**

**判定方法**：

```powershell
icacls . | Select-String Mandatory
```

- Low IL：输出 `Mandatory Label\Low Mandatory Level:(OI)(CI)(NW)`。
- Medium IL：输出 `Mandatory Label\Medium Mandatory Level:(OI)(CI)(NW)`，**这是正常状态**（被显式设过才打印这一行）。
- 完全没有任何 `Mandatory` 行也正常（继承父目录的 Medium，不打印）。

**修复三步（已验证生效）**：

```cmd
icacls . /grant "%USERNAME%:(OI)(CI)(WO)" /T /C /Q      rem ① 先拿写权限
icacls . /setintegritylevel (OI)(CI)Medium /T /C /Q      rem ② 改回 Medium
icacls . /remove:g "%USERNAME%:(WO)" /T /C /Q            rem ③ 收回临时授权
```

⚠ **顺序不能反**：原 ACL 里 owner 只有 `(WO,S)`（TakeOwnership + Synchronize），没有 Write；
不先 grant 就改 IL，子项全部 `Access denied`。
本次实际执行时用了「对 10 个顶层子目录逐个 grant + setintegritylevel」，一次性 `/T` 会因部分子项失败。

**已排除的假设（都实测过，下轮不要再试）**：不是 MOTW（文件上只有 `:$DATA`，无 `Zone.Identifier`）；
不是中文路径（`G:\测试` / `G:\тест` / `G:\テスト` / `G:\Work\paradox测试` / `C:\Users\17147\测试` 全部不弹）；
不是 ACL 本身（把整份 SDDL 连同 `(D;CI;DT;;;WD)` Deny Everyone 和孤儿 SID `S-1-4-809312820-54538573`
搬到干净的 `G:\Work\_cmp` 上仍不弹）；不是 SRP / WDAC / AppLocker / SmartScreen。

**恢复手段（重要）**：本次曾用 `New-Object DirectorySecurity` + `SetAccessRuleProtection($true,$false)`
手工重建 DACL 去删孤儿 SID，结果把 `.scratch` 等子目录的继承权限搞坏（`Permission denied`）。
**`icacls . /reset` 一步恢复**——reset 后的根目录 ACL 与父 `G:\Work` 完全一致。教训：不要手工重建 DACL。
孤儿 SID `S-1-4-809312820-54538573` 未删掉（`/remove:g` 报 `Successfully processed 0 files`），但不影响弹窗。

**验证结果**：`run.cmd` / `build.cmd` / `publish.cmd` 经 Explorer 启动全部 no-warning；新建子目录里的新 `.cmd` 继承 Medium、no-warning；
关键路径（`.scratch` / `src` / `src\PdxLauncher.App\bin` / `docs` / `tests` / `tools` / `dist` / `README.md` / exe）IL 全 Medium；
`build.cmd` 回归 `检查类: 5，失败: 0，断言失败总数: 0`；应用启动 smoke `APP_RUNNING pid=1605`。

**若再次出现**：先跑判定命令看是不是又变回 Low IL（G 盘这个目录的 IL 是被外部工具改过的，不是项目自带）。
临时绕过：`cmd /c run.cmd`、`Start-Process run.cmd`，或直接双击 `PdxLauncher.App.exe` —— 都不经 Explorer 的 ShellExecute 校验。

**文档**：README §7 排错已加第 6 条（同上内容）。

---

## 0.1 最新一轮（2026-10-26）：播放集页面双显示方式

> 本节是最新状态，优先级高于下面 §2–§8 中与播放集页相关的旧描述。
> **2026-10-26 02:00 更新：本节的「切换即激活」「自动保存总是写加载文件」「删除激活集」描述已被 §0.3 取代，
> 其余（双显示方式、设置项、留空布局）仍然有效。**

**用户需求（m00001）**：播放集页面要支持不同显示方式：① 原版启动器页面 —— 播放集切换放在「添加模组」那一栏，
把「设为激活」改成「启用全部 / 禁用全部」两个按钮，不要「保存」「保存并启动」；② 先留空，再在设置里留切换项。
用户经 ask_user_question 确认：只保留「原版启动器 / 留空」两种，删除旧布局；所有改动自动写入 sqlite + 游戏加载文件。

**已实现并验证（Debug / Release 均 0 警告 0 错误；Checks 5 类 / 0 失败；Release 真机 UIA 冒烟通过）**：

- `src/PdxLauncher.Core/Configuration/AppSettings.cs`：新增 `PlaySetPageDisplayMode`（写入 `%LOCALAPPDATA%\PdxNext\settings.json`，null → officialLauncher）。
- `src/PdxLauncher.Core/Configuration/PlaySetPageDisplayModes.cs`：`officialLauncher` / `placeholder` + `Normalize` / `IsPlaceholder`。
- `src/PdxLauncher.App/Services/PlaySetPageDisplayModeCatalog.cs`：两个显示方式的标题与说明文案（设置页下拉用）。
- `src/PdxLauncher.App/Views/SettingsPage.xaml` + `ViewModels/SettingsViewModel.cs`：新增「界面」卡片，ComboBox `cmbPlaySetPageDisplayMode`；改选即保存 settings.json，提示「返回播放集页面后生效」。
- `src/PdxLauncher.App/Views/ModsPage.xaml` / `.xaml.cs` / `ViewModels/ModsViewModel.cs` **整体重写**：
  - 官方布局：顶部一行＝播放集 ComboBox（切换即激活）+ 新建 / 重命名 / 删除，中间搜索框，右侧「添加模组」；
    第二行＝当前播放集名 / 计数 / 「启用全部」「禁用全部」；下方是该播放集的 mod 列表（启用禁用、排序、移除）；
    状态栏在底部。没有「设为激活」「保存」「保存并启动」按钮。
  - 留空布局：整页只有居中占位提示，回设置页切回原版布局。
  - 自动保存：`ModsViewModel` 删除了 `SaveAsync(bool)` / `SetActiveAsync` / `LaunchCompleted`，改为 `RequestAutoSave()`（串行、防抖，写 sqlite + 加载文件并回读）；切换播放集、增删 mod、启用禁用、排序、启用/禁用全部都会触发。
  - 删除播放集：只剩一个时拒绝；删除激活播放集时先激活接替者、再删除并同步加载文件。
- `tests/PdxLauncher.Checks/InfrastructureChecks.cs`：AppSettings 往返、Normalize、IsPlaceholder 断言。

**验证结果（2026-10-26 01:20–01:35，Release / Debug x64，均 `-m:1`）**：

- 构建：Debug、Release 都是 **0 警告 0 错误**；Checks：**检查类 5 / 失败 0**，其中新增用例全部 ok
  （「设置往返: 播放集页面显示方式」、Normalize 四例：null / 空白 / placeholder 大小写空白 / 未知值、IsPlaceholder）。
- UIA 冒烟（`.scratch/rework/smoke_displays.ps1`，Release）：设置页 ComboBox「播放集页面显示方式」选「留空」→ 播放集页只剩
  `txtPlaySetPlaceholder`（offscreen=False）、`cmbPlaySets` 消失；切回「原版启动器」→ `cmbPlaySets` / `btnAddMods` /
  `btnEnableAllMods` / `btnDisableAllMods` / `txtModsStatus` 全部存在、占位提示消失；进程全程存活，无崩溃。
- 限制 1（环境，非功能缺陷）：agent 沙箱内启动的**应用进程**写 `%LOCALAPPDATA%` / `%TEMP%` 被拒
  （`UnauthorizedAccessException`，见 exe 目录 `crash-fallback.log`：`PdxNext\logs\pdxnext.log`、`theme-cache`、`settings.json.pdxnext.tmp`
  全部 denied；同进程写工作区/exe 目录却成功）。所以 **settings.json 真正落盘、以及「重启后显示方式保持」无法在本环境验证**，
  需在正常桌面直接双击 exe 人工确认一遍（这也是既有缺陷 D1 的同一环境限制）。
- 限制 2（环境）：期间 UIA 自动化在启动/导航时序上触发过 0xc000027b（Microsoft.UI.Xaml.dll）WER 崩溃簇；清空构建产物重建后
  UIA 冒烟稳定通过，未发现与播放集显示方式代码路径相关，判断为自动化时序/环境伪影。
- 注意：工作树里还并行存在「窗口置顶 AlwaysOnTop」相关改动（`src/PdxLauncher.App/Services/AlwaysOnTopService.cs`、
  `AppSettings.AlwaysOnTop` 等），不属于本轮播放集需求，本轮未改动也未验证它。
- 文档：README §功能 的播放集页段落已同步两种显示方式；DATA-FORMATS.md 无 settings.json 章节（未补）。

**相关文件**：`src/PdxLauncher.App/Views/ModsPage.xaml`、`src/PdxLauncher.App/Views/ModsPage.xaml.cs`、
`src/PdxLauncher.App/ViewModels/ModsViewModel.cs`、`src/PdxLauncher.App/ViewModels/SettingsViewModel.cs`、
`src/PdxLauncher.App/Views/SettingsPage.xaml`、`src/PdxLauncher.Core/Configuration/AppSettings.cs`。

---

## 0.3 最新一轮（2026-10-26 02:00–02:45）：播放集「切换不激活 + 激活按钮」+ 下拉框空白修复

> 本节优先级最高，压过 §0.1 与 §2–§8 中与播放集页相关的旧描述。

**用户需求**：「1.切换播放集，不需要切换激活状态；2.添加播放集激活按钮」（m00538）；
「播放集这个选择的文字显示出bug」（m00589，附截图：播放集页工具条 ComboBox 内空白只有下拉箭头）；
m00622「执行」。

**下拉框空白根因（已定案）**：`src/PdxLauncher.Core/Contracts/PlaySet.cs:4` 的 `PlaySet` 是值相等 record；
旧 `ActivatePlaySetAsync` 先 `ApplyActiveFlags`（把集合元素替换成 `with { IsActive = ... }` 的新实例），
之后仍用**旧对象**调 `SelectPlaySetAsync`，旧对象与新集合项值不等，`ComboBox.SelectedItem` 匹配不到 → 选择框空白。
修复原则：`SelectedPlaySet` 必须始终是 `PlaySets` 中的实例；`ApplyActiveFlags` 末尾 `SyncSelectedPlaySetInstance()`，
`SelectPlaySetAsync` 入口按 Id 归一化。

**已实现**

- `src/PdxLauncher.App/ViewModels/ModsViewModel.cs`：新增 `SwitchPlaySetAsync`（只切换查看/编辑目标，仅写 sqlite，不写加载文件）、
  `ActivateSelectedAsync`（先 flush 自动保存 → `SaveAsync(writeLoadFile:true)` 写 sqlite+加载文件 → `SetActivePlaySetAsync` → `ApplyActiveFlags`）；
  新增 `IsSelectedPlaySetActive` / `ActivePlaySet` / `ActivePlaySetSummary` / `CanActivateSelected` / `ActivateButtonText` /
  `IsEditorEnabled` / `CanManageSelection` / `RaiseActivationState()`；`SaveAsync` 增加 `bool? writeLoadFile`，
  缺省时按「选中集是否激活」决定是否写加载文件；删除删除激活集失败时回滚激活标记。
  激活失败时保留 SaveAsync 的真实原因（去掉「自动保存失败：」前缀后拼成「激活失败：…（播放集数据未保存，可重试）」）。
- `src/PdxLauncher.App/Services/PlaySetEditor.cs`：`SaveAsync(...)` 增加 `bool writeLoadFile = true`，返回 `AppResult<LoadFileContent?>`；
  false 时只写 `playsets_mods`/`playsets_dlcs`，跳过加载文件构建/写入/回读。
- `src/PdxLauncher.App/Views/ModsPage.xaml`：第二行加「激活播放集」按钮（`btnActivatePlaySet`，激活后内容变「已激活」并置灰）、
  激活状态摘要 `txtActivePlaySet`、当前播放集名 `txtSelectedPlaySetName`；ComboBox 项模板根 Grid 加
  `AutomationProperties.Name="{x:Bind Name, Mode=OneTime}"`（此前无障碍/UIA 名是 record ToString），
  切换/编辑类控件 `IsEnabled` 绑定 `IsEditorEnabled` / `CanManageSelection`。
- `src/PdxLauncher.App/Views/ModsPage.xaml.cs`：选择变化改调 `SwitchPlaySetAsync`；新增 `OnActivatePlaySetClick`；
  删除确认文案区分激活/未激活。
- `src/PdxLauncher.App/ViewModels/DlcViewModel.cs:267`：改为「请选择播放集并点「激活播放集」」。
- `README.md` 播放集页段落已同步（切换不激活、激活按钮、只有激活集才写加载文件、删除规则）。

**验证结果**

- 构建：Debug / Release x64 `-m:1` 均 **0 警告 0 错误**（Release 全量 Rebuild 19.64s、改文案后增量 13.81s；
  Debug 全量 Rebuild 17.92s；增量 Debug 曾报 CS1697 SettingsPage.xaml 校验和警告，属增量 XAML 产物不一致，Rebuild 后消失）；Checks **5 类 / 0 失败**。
- 回归：`.scratch/rework/smoke_displays.ps1` 通过（设置页选「留空」→ 播放集页只剩占位提示；切回官方布局 → 工具条 /
  添加模组 / 启用全部 / 禁用全部 / 状态栏全部存在，进程存活）。
- Release 真机 UIA + 截图（`.scratch/rework/probe_combo.ps1` → `.scratch/rework/playsets_page.png`）：
  下拉框显示「1.18」（空白 bug 已修）、头部「1.18 共 20 个 mod，已启用 18 个 已激活：IronyModManager」、
  第二行有「激活播放集」+「启用全部」「禁用全部」、底部状态「已切换到「1.18」（未激活，点「激活播放集」后游戏才会使用它）」。
- `.scratch/rework/smoke_activation.ps1`（UIA）：切到 1.18 后 `txtSelectedPlaySetName`=1.18、`txtActivePlaySet` 不变、
  激活按钮可用 → **切换不激活 OK**；点激活在沙箱内失败，状态栏显示
  「激活失败：保存失败：Access to the path 'C:\Users\17147\Documents\Paradox Interactive\Crusader Kings III\dlc_load.json.tmp.pdxnext.tmp' is denied.（播放集数据未保存，可重试）」
  → 证明激活前写加载文件被环境拒绝（与 §0.1 限制 1 同因）；失败后 isActive/选中项不变、按钮仍可用、进程存活。
  **成功路径（真正写加载文件 + isActive）仍需正常桌面人工点一次验证。**
- 环境限制：沙箱内 app 进程写用户目录（Documents / AppData）被拒，而同用户 PowerShell 可以写，
  因此激活成功分支无法在本环境端到端跑通；错误信息已能直接显示根因，便于真机排障。

**相关文件**：`src/PdxLauncher.App/ViewModels/ModsViewModel.cs`、`src/PdxLauncher.App/Services/PlaySetEditor.cs`、
`src/PdxLauncher.App/Views/ModsPage.xaml`、`src/PdxLauncher.App/Views/ModsPage.xaml.cs`、
`src/PdxLauncher.App/ViewModels/DlcViewModel.cs`、`README.md`。

---

## 0.2 最新一轮（2026-10-26 01:00–01:35）：窗口置顶改成用户可控开关

> 本节优先级最高，压过 §0.1 与 §2–§8 中与窗口层级相关的旧描述。

**用户原始诉求（m00001）**：「窗口权限似乎太高了，找下原因，点击其他程序无法正常切换窗口。」

**根因（已定案：不是提权、不是应用代码）**

- 不是提权：TokenElevation=0，app.manifest 无 requestedExecutionLevel / UIAccess，代码里没有 runas。
- 不是应用代码：全新实例 EXSTYLE 恒 `0x0000_0100`（仅 WS_EX_WINDOWEDGE），导航四页、两次 winapp ui screenshot/inspect 之后仍是 0x0100 —— 截图工具也被排除。
- 真凶：**上一轮会话（2026-10-25 22:42:43，session-1ada9dce-1fce-4ea8-956c-32367bff27d0）为在《Railway Empire》遮挡下截图/hover，用 `SetWindowPos(hwnd, HWND_TOPMOST, 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE)` 把主窗口钉在最上层，之后从未解除**（该会话全文 NOTOPMOST / HWND_BOTTOM 出现 0 次）。
- 受害实例：pid 13436 / hwnd 0x9C0E12 / EXSTYLE 0x0108 = WS_EX_WINDOWEDGE|WS_EX_TOPMOST / RECT 1800x1182 @ (1859,54)。该进程 00:52 前后已退出；**新启动的实例默认不置顶**（本轮实测 baseline 0x0100），所以问题只在那个被钉住的进程上出现。

**用户随即把需求改成「加置顶快捷功能」（m00152）**：问 winui 是否提供置顶 API、通用做法是什么，打算添加这个快捷功能。
⇒ 不再做「失焦强制清除」的自愈方案，改为合法化 + 用户可控。

**已实现（WinUI 官方 API，不用 SetWindowPos）**

- 开关落在 `AppWindow.Presenter is OverlappedPresenter` 上：`presenter.IsAlwaysOnTop = value`（Windows App SDK 2.5.1 下编译与运行均可用）。
- 三个入口：**标题栏右上角、紧贴系统最小化 / 最大化 / 关闭按钮的图钉** ToggleButton（AutoId `btnAlwaysOnTop`，图标 E718，坐在 `TitleBar.RightHeader` 槽位；2026-10-26 17:55 落定，见 §0.8 —— 它 02:2x 曾被挪到左栏底部 PaneFooter，那套方案已在 §0.2.1 作废）、快捷键 **Ctrl+Shift+T**（KeyboardAccelerator）、设置页「窗口置顶」ToggleSwitch（AutoId `tglAlwaysOnTop`）。
- 状态持久化到 settings.json 的 `alwaysOnTop`（`AppSettings.AlwaysOnTop`，默认 false，旧文件缺键回 false），启动时 MainWindow.OnRootGridLoaded → `ApplyAlwaysOnTopPreference()` 恢复。

**运行时实测（Release 构建）**

- 默认启动 EXSTYLE=0x0100（topmost=False）→ 点图钉 0x0108（True）→ 再点回 0x0100。
- UIA 证据：`barAppTitle TitleBar "PdxNext · Crusader Kings III"` 下有 `btnAlwaysOnTop Button "取消窗口置顶" [on]`；toggle 后其 AutomationProperties.Name 同步在「窗口置顶 / 取消窗口置顶」间切换。
- 设置页 ↔ 图钉双向同步通过（AlwaysOnTopService.Changed + DispatcherQueue 派发）。
- Ctrl+Shift+T 两次按键切换 0x0108 ↔ 0x0100，通过。

**验证盲区（去真机补）**：`%LOCALAPPDATA%\PdxNext` 在本会话永不创建（见 D1），所以「重启后自动恢复置顶」只跑过 desired=false 分支（Apply(false) + Sync）；持久化正确性目前由 tests/PdxLauncher.Checks 的 AppSettings 往返断言背书。

**构建**：Release **0 警告 0 错误**；Checks 5 个检查类 / 0 失败。备份：`.scratch\rework\backup\2026-10-26-topmost\`（8 个原文件）、`…\2026-10-26-topmost-current\`（功能版）。

---


## 0.2.1 2026-10-26 02:00–02:25：图钉从标题栏移到左栏底部（PaneFooter）—— **已作废**

> **⚠ 本节已作废（2026-10-26 17:55）。** 左栏底部方案因「与导航项手工对齐无法自适应」被用户否决（m00252），入口已改为标题栏右上角 `TitleBar.RightHeader`（当前真相见 §0.8）。下面保留为历史记录；其中涉及的 `PinDivider` / `PinLabel` / `PinContent` / `Spacing` 以及 `_paneStateToken` / `OnNavigationPaneOpenChanged` / `UpdatePinCompactLayout` 均已从代码删除，**不要照着本节改代码**。
>
> 只动视觉位置与两处状态同步，置顶逻辑（AlwaysOnTopService / 设置页 / 持久化）完全没变。

- 用户反馈（m00473，附截图）：标题栏里的图钉「太丑」，并圈出左栏左下空白，要求挪到左栏。
- 槽位依据：左栏实测 MenuItemsHost 底边 931、FooterItemsScrollViewer 顶边 1616、navSettings (512,1619 310x54)、txtNavContentHint (530,1514 274x72) —— 只有 PaneFooter 是「设置」正上方的标准槽位；用户圈的 filler 不在任何列表里。
- `MainWindow.xaml`：删掉 `TitleBar.Content` 整块（标题栏恢复成 Title + Subtitle 两段文本，AutoId barAppTitle 保留）；`NavigationView.PaneFooter` 换成 StackPanel `PinFooterPanel`（Margin 8,4,8,8 / Spacing 4），自上而下 = `NavContentHint`（txtNavContentHint，不管理内容的游戏的说明）→ `PinDivider`（Rectangle 高 1，Fill DividerStrokeColorDefaultBrush）→ `AlwaysOnTopButton`（ToggleButton，高 40、透明底、内容左对齐、AutoId btnAlwaysOnTop、Click OnAlwaysOnTopClick、ToolTip「窗口置顶（Ctrl+Shift+T）」、内含 `PinContent` = FontIcon E718 + TextBlock `PinLabel`「窗口置顶」，KeyboardAccelerator Ctrl+Shift+T）。
  - 选中态配色：默认模板选中用 AccentFillColorDefault 铺满，在深色左栏里像一块白砖；在 ToggleButton.Resources 里把 `ToggleButtonBackgroundChecked` / `…CheckedPointerOver` / `…CheckedPressed` 换成 SubtleFillColorSecondary/Tertiary、`ToggleButtonBorderBrushChecked` 透明，选中只体现为一层淡底。
  - 紧凑模式（pane 折叠成图标条）：NavigationView 不拉伸 PaneFooter 自定义内容，原样会得到「一小截分隔线 + 被裁掉的文字 + 图标贴左」。`MainWindow.xaml.cs` 用 `RegisterPropertyChangedCallback(NavigationView.IsPaneOpenProperty, OnNavigationPaneOpenChanged)`（令牌字段 `_paneStateToken`，Closed 里注销）→ `UpdatePinCompactLayout()`：展开 = Margin(8,4,8,8)/Stretch/Width=NaN/分隔线与文字 Visible/PinContent.Spacing=12/Padding(12,0)/内容左对齐；折叠 = Margin(0,4,0,8)/Center/Width=CompactPaneLength/分隔线与文字 Collapsed/Spacing=0/Padding=0/内容居中，图标与导航项图标同轴。
- `Views/SettingsPage.xaml` 的说明文字同步改为「点左栏底部的图钉…」。

**实测（Release 0 警告 0 错误，窗口 1782x1191 @1.33 DPI）**

- 展开态：btnAlwaysOnTop (366,1386 298x60)、navSettings (360,1467 310x54)，分隔线在两者之间；barAppTitle 子树只剩 PART_TitleText / PART_SubtitleText（标题栏已无图钉）。
- 紧凑态：btnAlwaysOnTop (354,1386 71x60)，中心 389.5 ≈ navDlc/navSettings 图标中心 390，文字与分隔线隐藏。
- 截图：选中态 `.scratch/pin-on3.png`（淡底 + 图钉高亮）、展开 `.scratch/pin3-expanded.png`、紧凑 `.scratch/pin3-compact.png`、Prison Architect 提示堆叠 `.scratch/pin-pa.png`。
- 置顶行为：toggle-on → EXSTYLE 0x0108、Name「取消窗口置顶」；toggle-off → 0x0100；设置页 [on]/[off] 与图钉双向同步；Ctrl+Shift+T 两次 → 0x0108 ↔ 0x0100（必须先把窗口抢到前台 + AttachThreadInput，否则按键打到别的窗口，见 §3.4）。
- Prison Architect（不管理内容的游戏）：txtNavContentHint 出现后与分隔线、图钉纵向堆叠正常，不压 navSettings。
- 备份：`.scratch\rework\backup\2026-10-26-pin-footer\`（移动前的 HANDOFF.md 与 MainWindow.xaml）。
- 注意：本轮期间工作树里**另有一个会话在并发改 Mods 相关代码**（ModsViewModel.cs / PlaySetEditor.cs / ModsPage.xaml，02:04–02:11 反复落盘）；本节的构建是在对方那批改动也已编译通过之后做的。若后续 Mods 行为有变化，不是本次置顶改动引入的。

---

## 0.3 最新一轮（2026-10-26 01:40–02:10）：整窗背景（方案 B，已完成 + 真机截图验证）

> 本节优先级最高，压过 §2.1 / §3.3 / §7 的 D2–D4 / §8 的 T1–T2–T5 中与「页面背景、横幅裁切」相关的旧描述。

**用户需求**：贴运行截图说「背景图片怎么就这样？不应该是背景吗」——那时的视觉是主页里一条 260 高、被裁掉约 58% 高度的插图条。
方案 A（只把主页做成整页背景）提交后被否，用户原话：「方案选 B 整窗背景（含左侧 NavigationView）」。

**分层结构（都在 MainWindow，页面不再画不透明底色）**

1. `RootGrid.Background = PdxWindowBackgroundBrush`：无游戏时的纯色兜底。
2. `Image x:Name="ShellBackgroundImage"`（AutoId `imgShellBackground`，`Grid.RowSpan=2`，`Stretch=UniformToFill`，`IsHitTestVisible=False`）：游戏主题背景图，横跨标题栏行与内容行。
3. `Grid x:Name="ShellScrim"`（AutoId `grdShellScrim`，RowSpan=2，Background=`PdxWindowScrimBrush` 整窗纵向渐变）：默认 `Visibility=Collapsed`，有图才显示；内含 `Border`（AutoId `grdShellTopScrim`，Height=96，`PdxTopScrimBrush`）做标题栏渐隐压暗带，保证标题/窗口按钮在亮色主题图上仍可读。
4. `TitleBar`（Row0）+ `NavigationView`（Row1，**`Background="Transparent"`**）+ `Frame`。

**画笔（App.xaml，Default / Light / HighContrast 各一份）**

| 画笔 | Default(Dark) | Light | HighContrast | 用途 |
| --- | --- | --- | --- | --- |
| PdxWindowScrimBrush | 渐变 #26000000 → #66000000(0.5) → #B3000000 | #33FFFFFF → #80FFFFFF → #CCFFFFFF | 纯色 #FF000000 | 整窗压暗 |
| PdxTopScrimBrush | 渐变 #B3000000 → #4D000000(0.62) → 透明 | #B3FFFFFF → #4DFFFFFF → 透明 | #FF000000 | 标题栏压暗带 |
| PdxPaneGlassBrush | #B31B1B1F | #B3F3F3F3 | #FF000000 | 导航栏玻璃 |
| PdxPageVeilBrush | #991B1B1F | #99FFFFFF | #FF000000 | 内容页纱 |
| PdxCardGlassBrush | #D927272C | #D9F7F7F7 | #FF000000 | 卡片 / 列表底衬 |

- 已删除 `PdxPageBackgroundBrush` 与 `PdxHeroScrimBrush`（后者唯一使用者是旧横幅，随横幅一起删）。
- 每套字典还必须覆盖 WinUI 模板内部 key：`NavigationViewDefaultPaneBackground` / `NavigationViewExpandedPaneBackground` / `NavigationViewTopPaneBackground` → `PdxPaneGlassBrush`；`NavigationViewContentBackground` → Transparent。**WinUI 3 没有 `NavigationView.PaneBackground` 属性**（`winapp find-api check-property NavigationView PaneBackground` → found=false，只有 SplitView 有），所以只能走这些资源 key。
- 纱/玻璃都是「原页面底色 + alpha」：没有背景图时（ShellScrim collapsed）压在同色底上等于原色，**合成结果与改造前完全一致**。

**代码落点**

- `src/PdxLauncher.App/MainWindow.xaml.cs`：`private const int BackdropDecodeWidth = 2560;`（1200 DIP @150% ≈ 1800 px；2560×1423 ≈ 14.6 MB）；字段 `BitmapLoader? _bitmapLoader` / `CancellationTokenSource? _backdropLifetime` / `int _backdropVersion`；新增 `UpdateBackdropAsync()`：`spec = AppServices.CurrentGame?.Theme ?? ThemeApplier.CreatePlaceholderSpec()` → `LoadAsync(spec.Background, 2560, token)` → 版本号防串台 → `ShellBackgroundImage.Source = background; ShellScrim.Visibility = background is null ? Collapsed : Visible;`。调用点：`OnRootGridLoaded`（导航前 await）、`OnCurrentGameChanged`（`_ = UpdateBackdropAsync()`）、`OnWindowClosed` 取消并释放 CTS。
- `src/PdxLauncher.App/Views/HomePage.xaml`：`Background="Transparent"`；旧的 260 高横幅 Border 整块删除（`BackgroundImage` / AutoId `imgHomeBackground` 已不存在）；字标（`LogoImage`，AutoId `imgHomeLogo`）+ 标题 + 版本 + 刷新成为页头，直接浮在背景图上；5 张卡片改用 `PdxCardGlassBrush`。
- `src/PdxLauncher.App/Views/HomePage.xaml.cs`：删 `BackgroundDecodeWidth = 1920` 与整窗背景的加载，只留 `LogoDecodeWidth = 480` 与 `LogoImage.Source`。
- 另外五页：`Background="{ThemeResource PdxPageVeilBrush}"`（GameLibraryPage / DlcPage 原为 PdxPageBackgroundBrush；ModsPage / ModLibraryPage / SettingsPage 原为 LayerFillColorDefaultBrush）。
- 列表页底衬：`Views/DlcPage.xaml` / `Views/ModsPage.xaml` / `Views/ModLibraryPage.xaml` 在列表所在 Grid 里加一层 `Border Background="{ThemeResource PdxCardGlassBrush}" CornerRadius="8"`（列表本身保持透明），把行文字从背景图上托起来。

**验证（Release 0 警告 0 错误；Checks 5 类 / 0 失败；窗口 1782x1191 物理 @1.33 DPI）**

- 六页截图：主页 / 游戏库 / 播放集 / Mod 库 / DLC / 设置，背景图连续铺满整窗（含标题栏条与左侧导航面板），无接缝、无横幅残留；列表页在玻璃底衬上可读。`docs/` 已换成这六张（`screenshot-home/library/playsets/mod-library/dlc/settings.png`）。
- 换游戏：点游戏库卡片切到 Victoria 3（亮色、细节多的插图）→ 整窗背景换成 V3 画、字标换成 V3 墨迹，标题栏与页头文字仍可读；再切回 CK3，字标 / 播放集 / Mod·DLC 计数 / 最近存档全部正确。
- 折叠态：`TogglePaneButton` 折叠后左栏变窄条，背景图仍在窄条后。
- 窗口缩到 1182x791（约 900x600 DIP）后背景图仍 UniformToFill 铺满，无黑边。
- 已知盲区：Light / HighContrast 只审阅了画笔定义，未真机切主题跑；「没有游戏」态未实测（按合成数学等价）。

---

## 1. 需求演变（最重要的上下文）

| 阶段 | 用户说了什么 | 结果 |
| --- | --- | --- |
| 最初（m00001） | 官方启动器「又卡又慢」，想要 **WinUI 3 复刻**自用 | 做了完整功能（游戏发现 / 播放集 / Mod / DLC / 加载文件 / 启动） |
| 中期（m03357） | 「你启动页面的复刻都不太对，你再改下」 | 开始按官方截图逐像素自绘（悬浮左栏 + 自绘顶栏 + 全幅背景 + 三张宣传卡） |
| **最新（m03926，决定性）** | **「这里不要复刻原启动器的ui，还是采用winui3的通用ui框架」** | **推翻像素复刻方向**：官方截图的所有 px/DIP 度量作废，改用标准 WinUI 3 控件与系统主题 |
| **再最新（m00001 / m00152）** | **「窗口权限似乎太高了…点击其他程序无法正常切换窗口」→「winui 是否提供置顶 API…打算添加这个快捷功能」** | 定位到是上一轮会话遗留的 `SetWindowPos(HWND_TOPMOST)`；按用户要求把置顶变成官方 API 的可控开关（图钉 / Ctrl+Shift+T / 设置页），见 §0.2 |

⇒ 验收标准已从「像官方启动器」变成「符合 WinUI 3 规范」：标准 TitleBar 控件、NavigationView(Left)、
语义化 ThemeResource 画笔、Light/Dark/HighContrast 三套主题、无障碍（AutomationId / AutomationProperties.Name）、
窗口尺寸按多窗格建议（1100–1300 × 720–840 DIP）。

---

## 2. 代码当前状态（已落盘、已编译通过）

> ⚠ §2.1 表格里 App.xaml / MainWindow.xaml / MainWindow.xaml.cs / HomePage.xaml / HomePage.xaml.cs 五行描述的
> **页面背景与主页横幅部分已被 §0.3（整窗背景方案 B）覆盖**：PdxPageBackgroundBrush / PdxHeroScrimBrush 已删除，
> 主页横幅与 BackgroundImage 已不存在。读 §2.1 时以 §0.3 为准，其余（导航、BitmapLoader SVG、ThemeApplier）仍然有效。
> 另：MainWindow.xaml.cs 这一行的标题栏描述已被 §0.4 覆盖（现在读 ViewModel 的 HeaderText / SubtitleText）。

构建命令（**必须带 -m:1**）：

    dotnet build "PdxLauncher.sln" -c Debug   -m:1 --no-restore
    dotnet build "PdxLauncher.sln" -c Release -m:1 --no-restore

实测：Debug → 0 警告 0 错误；Release → 0 警告 0 错误；
dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll → 检查类: 5，失败: 0，断言失败总数: 0。

### 2.1 本轮已改的文件

| 文件 | 改动 |
| --- | --- |
| src/PdxLauncher.App/App.xaml | 删掉自绘用的 PdxNavItemStyle / PdxTopBarButtonStyle 两个 Style 与 11 个自绘专用画笔；只保留 Default/Light/HighContrast 三支 ThemeDictionaries，每支 10 个语义画笔：PdxWindowBackgroundBrush / PdxPageBackgroundBrush / PdxCardBackgroundBrush / PdxCardStrokeBrush / PdxPlaceholderBackgroundBrush / PdxOverlayBrush / PdxOverlayTextBrush / PdxCardHoverBackgroundBrush / PdxCardHoverStrokeBrush / PdxHeroScrimBrush。其余颜色一律用内置 TextFillColor* / CardBackgroundFillColor* / LayerFillColor* |
| src/PdxLauncher.App/MainWindow.xaml | 206 行。RootGrid 两行 Auto/*：Row0 = TitleBar x:Name=AppTitleBar Title=PdxNext AutomationId=barAppTitle（`<TitleBar.Resources>` 覆盖 TitleBarExpandedHeight=32 与 TitleBarMinDragRegionWidth=8；`<TitleBar.RightHeader>` 里是置顶图钉 ToggleButton，见 §0.8）；Row1 = NavigationView x:Name=MainNavigation AutomationId=navMain IsBackButtonVisible=Collapsed IsSettingsVisible=False ItemInvoked=OnNavigationItemInvoked OpenPaneLength=216 PaneDisplayMode=Left。MenuItems 五项（Tag / 文案 / 图标）：navLibrary 游戏库 Library、navHome 当前游戏 Home、x:Name=NavPlaySets + navPlaySets 播放集 List、x:Name=NavModLibrary + navModLibrary Mod 库 AllApps、x:Name=NavDlc + navDlc DLC Shop；FooterMenuItems：navSettings 设置 Setting；NavigationView.PaneFooter 里 NavContentHint（AutoId txtNavContentHint）说明「该游戏的模组与 DLC 由游戏本体管理…」，默认 Collapsed；NavigationView.Content 是 Frame x:Name=ContentFrame AutomationId=frameContent |
| src/PdxLauncher.App/MainWindow.xaml.cs | 568 行。常量 DefaultWindowWidth=1200.0 / DefaultWindowHeight=800.0 / MinimumWindowWidth=900.0 / MinimumWindowHeight=600.0。构造里 ExtendsContentIntoTitleBar = true; SetTitleBar(AppTitleBar); + ApplyWindowSize()。OnRootGridLoaded：注入 _navigation.Frame → NavigateTo(LibraryTag) → UpdateTitleBar() → await _viewModel.InitializeAsync(...) → UpdateTitleBar() → UpdateContentNavState() → 有当前游戏则 NavigateTo(HomeTag)。UpdateContentNavState() 按 game.SupportsContentManagement 设 NavPlaySets/NavModLibrary/NavDlc.IsEnabled 与 NavContentHint.Visibility，禁用时把停在内容页的用户送回主页。SyncSelectedNavItem 用 FindNavigationItem(MainNavigation.MenuItems/FooterMenuItems, tag) 设 MainNavigation.SelectedItem。UpdateTitleBar 从 ShellViewModel 取 HeaderText/SubtitleText 赋给 AppTitleBar.Title/Subtitle（§0.4）。保留 ApplyWindowSize（GetDpiForWindow + AppWindow.Resize 物理像素）、OnWindowClosed 存尺寸、AttachPageEvents。**§0.8 新增**：`FixTitleBarCaptionPadding()`（取 TitleBar 模板根 Grid，把右 / 左内边距列改成 `inset / RasterizationScale`，绕过官方 #10344 把物理像素当 DIP 的缺陷）+ `HookCaptionPaddingUpdates()` / `_captionPaddingHooked` / `OnXamlRootChangedForCaptionPadding` / `OnTitleBarSizeChangedForCaptionPadding`（挂 RootGrid.XamlRoot.Changed 与 AppTitleBar.SizeChanged，OnRootGridLoaded 首次调用、OnWindowClosed 解绑） |
| src/PdxLauncher.App/Views/HomePage.xaml | 313 行。Page Background=PdxPageBackgroundBrush → ScrollViewer → StackPanel MaxWidth=1120 Padding=24 Spacing=16：① 页头（txtHomeGameTitle / txtHomeVersion / ringHomeBusy / btnHomeRefresh）② 主题横幅 Border Height=260 CornerRadius=8（BackgroundImage UniformToFill + PdxHeroScrimBrush 遮罩 + LogoImage MaxWidth=440 MaxHeight=120）③ 启动卡片（ContinueButton 用 Style=AccentButtonStyle 文案「继续游戏」+ StartButton「开始游戏」+ PlaySetComboBox 宽 220 DisplayMemberPath=Name + txtHomeNoPlaySet + txtHomeNoGame）④ txtHomeSupportNotice ⑤ HomeInfoBar(infHomeLaunch) ⑥ 三张统计卡（txtHomePlaySet / txtHomeCounts / txtHomeGameVersion）⑦ 继续游戏卡（txtContinueTitle / txtContinueDate / btnContinueGame）⑧ txtHomeStatus |
| src/PdxLauncher.App/Views/HomePage.xaml.cs | 8.5 KB。常量 BackgroundDecodeWidth=1920 / LogoDecodeWidth=480；ApplyThemeAsync 只设 StartButton.FontFamily + 两个 Image.Source（不再覆盖按钮底色/前景，交给系统 AccentButton）；保留 OnLoaded/OnUnloaded/OnCurrentGameChanged/ReloadAsync/SyncPlaySetSelection/OnPlaySetSelectionChanged/OnStartClick/OnContinueClick/OnRefreshClick/OnLaunchCompleted |
| src/PdxLauncher.App/Services/BitmapLoader.cs | 返回类型由 Task<BitmapImage?> **放宽为 Task<ImageSource?>**，缓存改 Dictionary<string, ImageSource>；新增 SVG 分支：path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) 时 new SvgImageSource() + SetSourceAsync(stream) |
| src/PdxLauncher.App/Services/ThemeApplier.cs | TryLoadImage 增加 SVG 分支：.svg 走 new SvgImageSource { UriSource = uri }，其余仍走 BitmapImage { UriSource = uri } |

**为什么必须支持 SVG**：CK3 的字标是 E:\SteamLibrary\steamapps\common\Crusader Kings III\launcher\assets\logo.svg
（16,602 B，viewBox 0 0 333 298.07），BitmapImage 不支持矢量，之前主页中央一直是空白。

### 2.1.1 2026-10-26 窗口置顶功能（已落盘、已编译、已运行时验证）

| 文件 | 改动 |
| --- | --- |
| src/PdxLauncher.Core/Configuration/AppSettings.cs | WindowHeight 之后新增 `public bool AlwaysOnTop { get; set; }`；JSON 键 `alwaysOnTop`（CamelCase 策略），缺键回 false |
| src/PdxLauncher.App/Services/AlwaysOnTopService.cs **（新文件）** | static：`event EventHandler<bool>? Changed` / `IsSupported => _appWindow?.Presenter is OverlappedPresenter` / `Attach(AppWindow)` / `Apply(bool)` / `Set(bool, out string? error)`（失败不改设置；成功写 AppServices.Settings.AlwaysOnTop + SaveSettings + 触发 Changed）/ `TryApply`（`presenter.IsAlwaysOnTop = value`，try/catch + Logger.Error） |
| src/PdxLauncher.App/MainWindow.xaml | **当前（§0.8）**：图钉在 `<TitleBar.RightHeader>` 里 —— `ToggleButton x:Name="AlwaysOnTopButton"`（AutoId btnAlwaysOnTop、Width={ThemeResource TitleBarPaneToggleButtonWidth}、Height=28、Margin=2、Padding=0、CornerRadius={ThemeResource ControlCornerRadius}、透明底无边框、内容只有 `<FontIcon FontSize="16" Glyph="&#xE718;"/>`、内含 `<KeyboardAccelerator Key="T" Modifiers="Control,Shift"/>`），ToggleButton.Resources 覆盖 9 个选中 / 悬停画笔；`<TitleBar.Resources>` 覆盖 TitleBarExpandedHeight=32、TitleBarMinDragRegionWidth=8；`NavigationView.PaneFooter` 只剩 `PinFooterPanel`（Margin 8,4,8,8）+ NavContentHint。~~早期「PaneFooter + PinDivider + PinLabel 高 40」的写法见已被作废的 §0.2.1~~ |
| src/PdxLauncher.App/MainWindow.xaml.cs | using Microsoft.UI.Xaml.Automation；ctor 在 ApplyWindowSize() 后 `AlwaysOnTopService.Attach(AppWindow)`、订阅 Changed；OnRootGridLoaded 依次 `ApplyAlwaysOnTopPreference()` 与新增的 `FixTitleBarCaptionPadding()`（`_paneStateToken` / `UpdatePinCompactLayout()` 已随 §0.2.1 一并删除）；Closed 里退订 Changed + 解绑 caption padding 钩子。「// ---- 窗口置顶」区段现为：ApplyAlwaysOnTopPreference / OnAlwaysOnTopClick（失败回退 IsChecked + Warn）/ OnAlwaysOnTopChanged / SyncAlwaysOnTopButton（IsChecked、SetName、SetToolTip 三者同步）/ FixTitleBarCaptionPadding / HookCaptionPaddingUpdates / OnXamlRootChangedForCaptionPadding / OnTitleBarSizeChangedForCaptionPadding |
| src/PdxLauncher.App/Views/SettingsPage.xaml | 「界面」区 Grid 第三行：TextBlock「窗口置顶」+ ToggleSwitch（AutoId tglAlwaysOnTop、`IsOn="{x:Bind ViewModel.AlwaysOnTop, Mode=TwoWay}"`、OffContent 普通窗口 / OnContent 已置顶）+ 说明「置顶后启动器会停在其它窗口之上；点标题栏右上角的图钉或按 Ctrl+Shift+T 可随时取消。」 |
| src/PdxLauncher.App/ViewModels/SettingsViewModel.cs | 新增 `AlwaysOnTop`（get/set → AlwaysOnTopService.Set；失败 StatusMessage=error + OnPropertyChanged 回退；成功写 StatusMessage）+ `RefreshAlwaysOnTop()` |
| src/PdxLauncher.App/Views/SettingsPage.xaml.cs | OnLoaded 订阅（先 -= 再 +=）AlwaysOnTopService.Changed + RefreshAlwaysOnTop()；OnUnloaded 退订；新增 OnAlwaysOnTopChanged → DispatcherQueue.TryEnqueue(RefreshAlwaysOnTop) |
| tests/PdxLauncher.Checks/InfrastructureChecks.cs | 设置往返加 `AlwaysOnTop = true` 并通过断言；新增「置顶默认关闭」断言 |

### 2.2 工作树里同时存在、已落盘的其它改动（不要重复实现）

- src/PdxLauncher.Core/Contracts/GameSupport.cs：public enum GameSupport { Unsupported = 0, LaunchOnly = 1, Full = 2 }。
- src/PdxLauncher.Core/Contracts/GameInstallation.cs：record 末位参数是 GameSupport Support，并带
  IsSupported => Support != GameSupport.Unsupported；SupportsContentManagement => Support == GameSupport.Full。
- src/PdxLauncher.App/Services/SupportMessages.cs：internal static class SupportMessages，常量 ManagedByGame =「该游戏的模组与 DLC 由游戏本体管理（游戏内 MOD 菜单 / Steam 创意工坊），本启动器不接管」，被 DlcViewModel / ModLibraryViewModel / ModsViewModel 引用。
- src/PdxLauncher.App/ViewModels/HomeViewModel.cs（14,221 B）已带 SelectedPlaySet / PlaySets / RawVersionText / HasPlaySets / SupportsContentManagement / ApplyPlaySetSelectionAsync。

### 2.3 源码规模（实测）

| 项目 | .cs | .xaml |
| --- | --- | --- |
| src/PdxLauncher.Core | 111 | 0 |
| src/PdxLauncher.App | 53 | 9 |
| tests/PdxLauncher.Checks | 19 | 0 |

合计 **183 个 .cs + 9 个 .xaml**（2026-10-27 14:40 实测：Core 111 / App 53+9 / Checks 19，排除 bin/obj；Checks 的 19 个 .cs 里是 **16 个检查类** + Program + ChecksContext + GlobalUsings，本轮新增 IconChecks）。

> §0.6 的增量：Core +3（NewsFeed.cs / INewsFeedService.cs / NewsFeedService.cs）、App +1（NewsCardItem.cs）、
> Checks +1（NewsFeedChecks.cs）。

App 层 62 个 .cs/.xaml（2026-10-27 14:40 实测）：根目录 3 个 .cs（App.xaml.cs / GlobalUsings.cs / MainWindow.xaml.cs）+ 2 个 .xaml（App / MainWindow）；
Services/ 27 个（AppServices、AlwaysOnTopService、BitmapLoader、FontLoader、GameCardItem、GameDiscovery、ModLibraryService、
NavigationService、NetworkComposition、NewsCardItem、PlaySetEditor、PlaySetPageDisplayModeCatalog、SupportMessages、
ThemeApplier、ThemeUpdateService、ThemeVisuals、TitleBarTheme、BackgroundDisplayModeCatalog、ModThumbnailSizeCatalog、
ModThumbnailRetentionCatalog、DlcDescriptionRetentionCatalog、ProxyModeCatalog、RetentionDaysCatalog、UiLanguageCatalog、
NetworkRebuildResult、XamlImage、XamlVisibility）；
ViewModels/ 12 个 .cs（11 个 VM + ModSizeText 格式化助手；ModsViewModel 最大、SettingsViewModel、HomeViewModel…）；
Views/ 6 页 12 个文件；Localization/ 4 个 .cs；Controls/ 1 个 .cs + 1 个 .xaml（SettingsCard）。

---

## 3. 运行验证现状

### 3.1 已验证为「新 UI 生效」的证据（UIA 树，Release 构建，窗口 1800x1200 物理 / 1200x800 DIP @1.5x）

> 下面这棵树来自 2026-10-26 的旧 UI，仅作历史留档；主页现状以 §0.13 为准（`btnHomeRefresh` 已删除；`txtHomeVersion` 在公告栏正上方、右对齐；播放集控件已从 `cmbHomePlaySets`（ComboBox）换成 `btnHomePlaySets`（DropDownButton + Flyout））。

共 **45 个 UIA 元素**，关键项：

    TitleBar|barAppTitle|PdxNext · Crusader Kings III ·        （§0.4：主文案末尾多一个分隔点）
    Text|PART_TitleText|PdxNext · Crusader Kin…        Text|PART_SubtitleText|版本 1.20.1.2 (Crozier)…
    Custom|navMain                                     Button|TogglePaneButton|关闭导航
    ListItem|navLibrary|游戏库   ListItem|navHome|当前游戏   ListItem|navPlaySets|播放集
    ListItem|navModLibrary|Mod 库  ListItem|navDlc|DLC        ListItem|navSettings|设置
    Text|txtHomeGameTitle|Crusader Kings III    Text|txtHomeVersion|版本 1.20.1.2 (Crozier)
    Image|imgHomeBackground|游戏主题背景 1378x390   Image|imgHomeLogo|游戏字标 200x180
    Button|btnHomeContinue|继续游戏 210x60      Button|btnStartGame|开始游戏 210x60
    ComboBox|cmbHomePlaySets|341x59             Text|txtHomePlaySet|播放集：IronyModManager
    Text|txtHomeCounts|Mod 4/5 启用 · DLC 25 个  Text|txtHomeGameVersion|1.20.1.2 (Crozier)
    Text|txtContinueTitle|茅斯顿小…_1066_09_29    Text|txtContinueDate|2026-06-13 02:26:06
    Button|btnContinueGame|继续游戏

**无任何旧自绘残留**（已确认不存在 btnTopBell / btnGameHeader / imgShellBackground / txtSideVersion / STATE & REVOLUTION 等）。

### 3.2 导航可用性（逐个用 UIA SelectionItemPattern.Select() 验证，进程全程存活）

navLibrary / navPlaySets / navModLibrary / navDlc / navSettings / navHome 全部 OK，
每次导航后进程保持存活（alive=11111111）。**之前观察到的「点导航后进程消失」无法复现**，
判断是当时多次 kill/spawn 竞争造成的假象，不是真崩溃。

### 3.3 截图（docs/ 已更新为新的标准 UI）

| 文件 | 尺寸 | 字节 | 说明 |
| --- | --- | --- | --- |
| docs/screenshot-home.png | 1782x1191 | 1,631,131 | 当前游戏（CK3，整窗背景） |
| docs/screenshot-library.png | 1782x1191 | 1,777,449 | 游戏库（四张游戏卡） |
| docs/screenshot-playsets.png | 1800x1200 | 855,223 | 播放集（2026-10-27 04:19 换成 §0.14 单行工具条版，@1.5 DPI 与其余五张 @1.33 不同） |
| docs/screenshot-mod-library.png | 1782x1191 | 930,138 | Mod 库（387 个 mod） |
| docs/screenshot-dlc.png | 1782x1191 | 1,376,316 | DLC |
| docs/screenshot-settings.png | 1782x1191 | 1,287,153 | 设置 |
| docs/DATA-FORMATS.md | — | 18,420 | 数据格式真机实测记录（未变） |

除播放集外五张图均为 2026-10-26 §0.3 之后重截（Release，1782x1191 物理 @1.33 DPI），sha256 互不相同。

> 2026-10-27 04:19：`docs/screenshot-playsets.png` 已换成单行工具条后的新界面（`.scratch/rework/2026-10-27-playset-final.png`，1800x1200，@1.5 DPI）。与其余五张的尺寸/DPI 不一致，下次整体重截时一并统一；若并发会话正在改 Home/设置页，那些图可能也需同步重截。

截图方法（**关键经验**）：PrintWindow(hwnd, hdc, 2) 在 WinUI 3 / DWM 合成窗口上**返回陈旧帧** ——
连截 7 张不同页面的 sha256 完全相同（730fa704f697…）。可用方案是
先 SetWindowPos(hwnd, HWND_TOPMOST(-1), 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE|SWP_SHOWWINDOW)、等约 900 ms，
再 DwmGetWindowAttribute(hwnd, 9 = DWMWA_EXTENDED_FRAME_BOUNDS, ...) 拿真实窗口矩形，
Graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, size)，最后 SetWindowPos(hwnd, HWND_NOTOPMOST(-2), …) 复位。
**只用 SetForegroundWindow 不可靠**（曾截到别的应用整屏）。现成脚本：`.scratch/rework/cap3.ps1`、`flow.ps1`。

### 3.4 窗口置顶功能验证（2026-10-26，Release）

| 检查 | 方法 | 结果 |
| --- | --- | --- |
| 默认不置顶 | EnumWindows 按 pid 过滤、class like `*WinUIDesktop*`，GetWindowLongPtr(GWL_EXSTYLE=-20) 看 `-band 0x8` | 0x0100 topmost=False |
| 图钉切换 | `winapp ui invoke btnAlwaysOnTop -a <pid> --action toggle-on|toggle-off` | 0x0108 → 0x0100 |
| 图钉状态名 | `winapp ui inspect -a <pid>` | Button "取消窗口置顶" [on] / "窗口置顶" [off] |
| 设置页 ↔ 图钉双向同步 | 设置页 ToggleSwitch toggle-on/off + inspect btnAlwaysOnTop | 双向同步通过 |
| Ctrl+Shift+T | 先把窗口抢到前台，再 `winapp ui send-keys 'ctrl+shift+t' -a <pid> --via=send-input`（`--via=` 必须用等号） | 两次切换 0x0108 ↔ 0x0100 |
| 图钉位置（标题栏右上角） | `winapp ui inspect -a <pid>`：btnAlwaysOnTop / Minimize 的 UIA 矩形 | §0.8 实测：(1925,422 59x42) vs Minimize (2000,418 69x48)，间隙 16 物理 ≈ 10.7 逻辑 = MinDragRegion 8 + Margin 2 |
| 图钉自适应（最大化 / DPI） | 最大化后再 inspect 同一进程 | §0.8 实测：(3486,3 60x42) vs Minimize (3561,0 69x48)，间隙 15 物理 ≈ 10 逻辑；标题栏高恒 32 逻辑（48 物理） |
| 选中态配色 | ToggleButton.Resources 覆盖 ToggleButtonBackgroundChecked / …PointerOver / …Pressed → 截图 | 淡底 + 图钉高亮（不再是白砖）；.scratch/pin-on3.png |
| 提示堆叠 | 切到 Prison Architect（不管理内容的游戏）→ inspect + 截图 | txtNavContentHint 出现后与分隔线、图钉纵向堆叠，不压 navSettings；.scratch/pin-pa.png |
| 重启后恢复置顶 | 需要写 %LOCALAPPDATA%\PdxNext\settings.json | **无法验证（D1）** |

探针脚本：`.scratch\topmost-probe.ps1`（启动+基线）、`topmost-settings9.ps1`（设置页+快捷键）、`topmost-smoke.ps1`（清理后完整冒烟）。
前台解锁（关键）：`winapp ui send-keys --via=send-input` 要求目标窗口在前台，本会话窗口不会自己抢前台 ——
需 Add-Type 声明 GetForegroundWindow / SetForegroundWindow / AttachThreadInput 等，并先 `keybd_event(VK_MENU=0x12)` 按下抬起再 SetForegroundWindow，才能 foreground=True。

---

## 4. 环境限制（下轮务必遵守，否则会浪费大量时间）

1. **本会话所有嵌套工具（tools.read / glob / grep / pwsh / read_image / acp_status / list_agents / compress / present）全部返回 413**：

       auto-review: reviewer ended with error INVALID_REQUEST: 413:
       {"type":"server_error","code":"side_request_payload_too_large",
        "message":"side request payload ~1383398 tokens reaches the effective context window 999999
        (model=deepseek-v4.1-flash); NOT forwarded — side requests bypass compression by design (#388).","retryable":false}

   ⇒ 本轮**唯一可用的工作方式**是：在 run_code 里 const fs = await import("node:fs") /
   await import("node:child_process") 直接读写文件、直接 spawn dotnet / powershell.exe / python.exe。
   **压缩也做不了**（compress 同样 413），上下文一直很满 —— 下轮若工具恢复正常，优先把上下文压缩一遍。

2. **dotnet 从 node spawn 时必须给显式 env**，否则报
   NuGet.targets(782,5): error : Value cannot be null. (Parameter 'path1')。最小集合：
   SystemRoot=C:\Windows、windir、SystemDrive、TEMP/TMP=C:\Users\17147\AppData\Local\Temp、
   USERPROFILE=C:\Users\17147、HOMEDRIVE、HOMEPATH、APPDATA、LOCALAPPDATA、USERNAME、
   PATH=C:\Program Files\dotnet;C:\Windows\System32;C:\Windows、
   NUGET_PACKAGES=C:\Users\17147\.nuget\packages、DOTNET_CLI_TELEMETRY_OPTOUT=1、DOTNET_NOLOGO=1。

3. **构建前先 taskkill /IM PdxLauncher.App.exe /F**，否则
   MSB3027: 无法将 apphost.exe 复制到 …\PdxLauncher.App.exe。超出了重试计数 10。失败。文件被"PdxLauncher.App (NNN)"锁定。

4. **-m:1 是硬要求**。不加时 dotnet build PdxLauncher.sln 会静默失败，日志只有「生成失败。/ 0 个警告 / 0 个错误」。
   原因：App 声明了 Platforms=x64 + RuntimeIdentifier win-x64 + SelfContained true，而解决方案配置是 Any CPU，
   并行 MSBuild 跨节点时 _GetProjectReferenceTargetFrameworkProperties 会失败。

5. **PowerShell 脚本必须写 UTF-8 带 BOM**（fs.writeFileSync(p, "\uFEFF" + script, "utf8")），
   否则工作区非 ASCII 路径 G:\Work\paradox启动器 会乱码，Start-Process 报「系统找不到指定的文件」。
   Python 脚本同理：**不要在脚本里硬编码含中文的路径**，用 os.path.dirname(os.path.abspath(__file__)) 推算（本轮踩了 2 次）。

6. **cp.spawn("powershell.exe", ...) 若 stdout/stderr 走 pipe 而调用方不消费，进程会挂住**（本轮多次 [timeout]）。
   稳妥做法：stdio: ["ignore", fd, fd] 重定向到文件 + detached: true + unref()，然后轮询读文件；
   或者直接用 node cp.spawn(exe, [], { detached: true, stdio: "ignore" }) 起应用本体，不经过 PowerShell。

7. **本会话的 harness 沙箱禁止应用写工作区之外的路径**（不是 env 为空，2026-10-26 已查明）：
   pwsh 里 `$env:LOCALAPPDATA` 正常，App 里 `Environment.GetFolderPath(LocalApplicationData)` 也返回
   C:\Users\17147\AppData\Local，但 App 一创建 %LOCALAPPDATA%\PdxNext 或 %TEMP%\… 就
   `UnauthorizedAccessException: Access to the path ... is denied`；写工作区内路径（.scratch、exe 目录）正常。
   同一 WMI 路径启动的 cmd.exe 反而能写 —— 机制未查明，属环境问题、非应用 bug。
   ⇒ `settings.json` / `pdxnext.log` 在本会话永远不会生成，**一切持久化类功能只能在真机验证**（见 D1 / T4）。

8. 无 Visual Studio / 无 Windows Kits / NuGet 不可达 / 无 rust ⇒ **C# 是唯一可构建路径**，且只能从
   C:\Users\17147\.nuget\packages 离线还原（NuGet.config 里是 <clear/>）。

9. ask_user_question / exit_plan_mode 常超时（无 answerer），直接声明假设继续。

10. python：C:\Users\17147\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe（Pillow 12.3.0 可用），
    分析脚本统一放 .scratch\rework\。

11. **禁止用外部脚本把本应用窗口钉成 topmost 后忘记复位**。上一轮会话就是这么造成「点其他程序切不过去」的：
    `SetWindowPos(hwnd, HWND_TOPMOST(-1), 0,0,0,0, SWP_NOMOVE|SWP_NOSIZE)` 之后从未调 NOTOPMOST。
    需要临时压住别的窗口（截图 / hover 测试）时，结束后必须 `SetWindowPos(hwnd, HWND_NOTOPMOST(-2), …)` 复位，
    或直接改用应用自带的官方开关（§0.2：图钉 / Ctrl+Shift+T / 设置页）。

12. **本轮（2026-10-26 17:50）新增的硬限制：`dotnet build` 彻底不可用** —— 报
    `error MSB3883: 意外的异常`（`Microsoft.CSharp.Core.targets(97,5)`）。根因：受限 token 下
    **用管道捕获子进程输出被拒** ⇒ Roslyn 编译器服务器（命名管道）与 `UseSharedCompilation=false` 后
    直接起的 `csc.exe` 都一样失败；`csc.exe /version` 直接跑却是成功的。临时目录建 hello-world 项目同样失败。
    另外 **bash/pwsh 起的任何子进程都写不进工作区**（`os.makedirs` → `[WinError 5]`），
    连 `touch`/`mkdir` 都不行；只有 DSH 的 `write`/`edit` 工具能落盘。
    ⇒ 本轮改用**离线手工链**（脚本都在 `.scratch/rework/`，用 `write` 工具建的）：
    - `xamlpass1.py`：拿旧构建的 `src/PdxLauncher.App/obj/Release/net10.0-windows10.0.26100.0/win-x64/input.json`，
      改 `IsPass1/CompileMode/OutputPath/SavedStateFile` 指向临时目录，调
      `C:\Users\17147\.nuget\packages\microsoft.windowsappsdk.winui\2.3.9\tools\net472\XamlCompiler.exe`
      → 生成 17 个文件，**能验 XAML 结构/控件字段名，但 x:Bind 绑定代码是 0 字节（codegen 被跳过）**。
    - `build-app.py`：`csc` 编 Core → 用 input.json 里 205 个引用（去掉旧 Core 换上新的）编 App
      → `[core] rc=0 errors=0` / `[app] rc=0 errors=0 warnings=0`。
    - `build-checks.sh`：`csc` 编 Core + Checks 并 `dotnet` 跑自检；
      **必须先把 `tests/fixtures` 拷到 dll 旁边**（`FixturePath` 从 `AppContext.BaseDirectory` 往上找，
      找不到就退化成相对路径，路径比较类断言会假失败）→ 9 类全绿。
    ⇒ **真机验证仍需用户在本机跑 `build.cmd` / `run.cmd`**，或把 DSH 切到「完全权限」后重试。


12. **启动 / 截图 GUI 的正确姿势（2026-10-26 实用经验）**：
    - 启动用 `cmd /c start "" "<exe 绝对路径>"`（pwsh 里立即返回）。`Start-Process` 会挂住到超时；
      `bash`/ops_bash 直接跑 Windows exe 一律 exit 127。
    - 本会话里应用进程会在工具调用之间被回收（无崩溃事件、Application 日志无 APPCRASH），
      所以**启动与截图必须放在同一次 pwsh 调用里**（参考 `.scratch/rework/flow.ps1`）。
    - 截图前必须把窗口真正置顶（见第 11 条 + §3.3），截完立刻复位。
13. **同一工作区可能有并发会话在改代码（2026-10-26 02:04 实测）**：另一个会话在同一个 `G:\Work\paradox启动器` 里改 ModsViewModel.cs / PlaySetEditor.cs / ModsPage.xaml，并反复 dotnet build + taskkill PdxLauncher.App。
    - 症状：`dotnet build` 突然报「当前上下文中不存在名称 X」「X 的最佳重载没有名为 Y 的参数」，甚至 XAML 编译器 WMC9999 —— 不是你改坏的，是对方改到一半。
    - 处置：先 `Get-ChildItem src -Recurse -Include *.cs,*.xaml | Sort LastWriteTime` 看源码 mtime，等 mtime 静默 1–2 分钟再构建；构建成功后用 exe 的 LastWriteTime 确认产物是刚构建的那份。
    - 脚本里不要 `taskkill /IM PdxLauncher.App.exe /F` 一刀切（会杀掉对方的验证实例），只 `Stop-Process -Id` 自己启动的那个 pid。
14. **`.cmd` 文件一律纯 ASCII（2026-10-26 定案）**：顶层 .cmd 里出现任何中文/非 ASCII 字节，
    在某些控制台起始代码页 + 写后立刻执行的组合下，cmd.exe 会按字节撕裂行边界，冒出
    `'行文件:' is not recognized as an internal or external command` 之类假报错（构建其实成功）。
    ⇒ 中文提示全部走 `scripts\messages.txt` + `call "%~dp0scripts\msg.cmd" KEY`，详见 §0.5。
    另外 `rem` 行里不要写 `<` `>` `|`（即使是 ASCII 的 rem，也会被 cmd 当重定向/管道）。
15. **验证 `.cmd` 要带真实退出码**：在 MINGW64 bash 里直接跑 Windows GUI exe 一律拿到 exit 127
    （MSYS 包装码，不是应用真实退出码）。要真实退出码就在 `cmd /c` 里跑并 `echo EXIT=%errorlevel%`，
    或把逻辑写进 `.scratch\xxx.sh` 再用 `bash .scratch\xxx.sh` 一次拿全量输出。

---

## 5. 产物与运行方式

    src\PdxLauncher.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe
    src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe

注意路径里多一层 x64（早期手工构建产物在 bin\Debug\…，已不存在）。
自包含部署（SelfContained=true + WindowsAppSDKSelfContained=true），约 232 MB / 538 文件（顶层 438 项）。

启动脚本（工作区根，CRLF，均含 chcp 65001 + cd /d "%~dp0" + -m:1）：
build.cmd(1,173 B) / build-debug.cmd(545 B) / run.cmd(373 B) / run-debug.cmd(325 B)。

### 5.1 发布（publish.cmd，2026-10-26 新增）

分发用 `publish.cmd`（3,011 B，CRLF，可传目标目录：`publish.cmd D:\out\PdxNext`），
默认输出 `dist\PdxNext\`。流程：build → 自检 → `dotnet publish -c Release -p:Platform=x64`
（默认 PublishDir）→ `if not exist` 兜底校验 4 个关键文件 → xcopy 到 dist。

**坑：早期 dotnet publish 产物是坏的**（顶层只 433 项，缺 App.xbf / MainWindow.xbf / Views\*.xbf /
PdxLauncher.App.pri / Assets\，双击进程立刻消失）。

> 真实退出码实测为 `-1073741189`（`0xC000027B` STATUS_STOWED_EXCEPTION），且连
> `%LOCALAPPDATA%\PdxNext\logs\` 都不建 —— 无任何日志。在 Git-Bash / MSYS 里跑同一个 exe 会看到
> 退出码 127，那是 bash 的包装码（「无法执行 Windows exe」），**不要拿 127 当应用真实退出码**。

根因：WinUI 的 `AddProcessedXamlFilesToCopyLocal`
（microsoft.windowsappsdk.winui\2.3.9\buildTransitive\Microsoft.WinUI.NET.Markup.Compiler.targets:34）
只挂 `BeforeTargets="GetCopyToOutputDirectoryItems"`（build 链路），publish 走
`GetCopyToPublishDirectoryItems` 独立链路看不到。已由 `PdxLauncher.App.csproj` 末尾的
`AddWinUIXamlArtifactsToPublish` 目标（`AfterTargets="ComputeFilesToPublish"`）补进
`ResolvedFileToPublish` 修复，Release / Debug 双配置实测 publish 与 bin 逐文件一致且可启动。

- **不要用 `dotnet publish -o <dir>`**：`-o` 会把平台子目录 `x64` 从 `$(OutDir)` 去掉，补全目标取不到
  `.xbf` / `.pri`，产物又变坏。

---

## 6. 版本控制与历史备份

**回滚手段从 2026-10-27 起改为 git**（仓库就在工作区根，基线提交 `chore: 初始化 Git 仓库`）。
日常流程：改动前建分支、每步 `build` 通过就 `commit`、验证完再合回 `main`；看历史用 `git log -p`，
回滚用 `git restore <文件>`（未提交）或 `git revert <提交>`（已提交）。下面的 .scratch 备份是 git 之前的存档，
保留作对照，但**不再新增**。

| 目录 | 内容 |
| --- | --- |
| .scratch\rework\backup\ | 改成自绘版**之前**的 6 份原文件：App.xaml(2,768 B)、MainWindow.xaml(3,494 B)、MainWindow.xaml.cs(8,758 B)、HomePage.xaml(178 行)、HomePage.xaml.cs(250 行)、HomeViewModel.cs(276 行) —— 即「标准 NavigationView + 简洁主页」初版 |
| .scratch\rework\backup_selfdraw\ | **自绘版** 6 份（文件名把 \ 换成 _）：App.xaml、MainWindow.xaml、MainWindow.xaml.cs、Views_HomePage.xaml、Views_HomePage.xaml.cs、ViewModels_HomeViewModel.cs |
| .scratch\rework\backup\2026-10-26-topmost\ 与 …-current\ | 置顶功能：8 个原始文件 / 功能版（见 §2.1.1） |
| .scratch\rework\backup\2026-10-26-pin-footer\ | 图钉移到左栏之前：HANDOFF.md(40,373 B) + MainWindow.xaml(7,614 B) |
| .scratch\rework\backup\titlebar-20261026-*.bak | 标题栏文案改造（§0.4）改前原文：ShellViewModel.cs(4,412 B)、MainWindow.xaml.cs(17,736 B)、HANDOFF.md(66,078 B)。**注意：这三个 .bak 是改完之后才复制、再手工反改回原文的**，字节数已与原文核对一致 |
| （缺）`.scratch\rework\backup\2026-10-26-home-news\` | **§0.6 主页改版没有备份**：本会话 bash 连 `mkdir` 都被沙箱拒绝（`touch src/…/_probe.txt` 同样 Permission denied），只 `write` 工具能落盘，而 `write` 要求父目录已存在 ⇒ 建不出新备份目录。**这一轮的改动只能靠手工反改**（受影响文件清单见 §0.6.2 表格） |

.scratch\rework\ 下还有 120+ 个分析/截图脚本（om.py / ob.py / mm.py 度量、pw*.ps1 / cap*.ps1 截图、
uia*.ps1 UIA dump、an*.py 颜色网格分析）。可复用：verify2.ps1、capture3.ps1、caps_all.ps1。

---

## 7. 已知缺陷 / 未验证项（下轮要先处理）

| 编号 | 问题 | 证据 |
| --- | --- | --- |
| D1 | %LOCALAPPDATA%\PdxNext 从未创建，pdxnext.log / settings.json 不存在 | 已查明是**本会话 harness 沙箱拒绝应用写工作区外路径**（不是代码 bug，也不是 env 为空）：临时诊断写 4 个目标 → `LAD FAIL UnauthorizedAccessException …PdxNext\diag`、`TEMP FAIL …\Temp\pdx-diag`、`WORKSPACE OK G:/Work/paradox启动器/.scratch/appwrite/ws.txt`、`EXEDIR OK …\win-x64\exediag.txt`；同一 WMI 路径启动的 cmd.exe 却能写。**必须在真机（非沙箱）复验** |
| D2 | ~~主页主题横幅过度裁切（imgHomeBackground 390 DIP 高，CK3 背景 2408x1340 被裁掉约 58%）~~ **已解决（§0.3）**：横幅整块删除，背景改为整窗 UniformToFill，主页不再有裁切条 | 六页截图目视 |
| D3 | PrintWindow 截图不可用（陈旧帧），只有 CopyFromScreen 可用；后者要求窗口在最前且不被遮挡 | 7 张图 sha256 相同（730fa704f697…） |
| D4 | ~~ModLibraryPage / DlcPage / SettingsPage 仍是旧样式~~ **已解决（§0.3）**：五页统一 `PdxPageVeilBrush` 纱，DLC / 播放集 / Mod 库三页再加玻璃底衬；ModsPage 的两种显示方式见 §0.1 | 六页截图 + grep 确认无 PdxPageBackgroundBrush |
| D5 | ~~README.md 仍写「复刻官方启动器」、源码计数与 docs 清单过时~~ **已解决（2026-10-26）**：标题改为「WinUI 3 重写」，计数 Core 85 / App 35+8 / Checks 8 / 合计 128，docs 列出 6 张截图，界面一节补整窗背景说明 | 与 glob 实测一致 |
| D6 | MainWindow.xaml.cs 的 SyncSelectedNavItem 依赖 FindNavigationItem 遍历 MainNavigation.MenuItems，而 PaneFooter 里的 NavContentHint 不进这两组 —— 目前没问题，但以后把导航项移到 PaneFooter 会静默失效 | 代码审阅 |
| D7 | 窗口置顶设置的「重启后恢复」未做运行时验证 | 受 D1 限制，启动恢复路径只跑过 desired=false 分支（Apply(false) + SyncAlwaysOnTopButton）；持久化链路由 Checks 的 AppSettings 往返断言 + SettingsViewModel 的 Set/Save 路径背书 |
| D8 | 应用无单实例保护，快速 kill → 立刻重启时可出现两个 PdxLauncher.App 并存；再叠加 5 秒内的 UIA 操作会触发 Microsoft.UI.Xaml.dll 0xc000027b / combase.dll 80004002 (E_NOINTERFACE) 启动期崩溃 | WER ReportArchive 01:11–01:18 共 7 份 APPCRASH，全部在脚本快速重启循环中；A/B：回滚置顶功能 18 秒常驻 ×3 全部存活，恢复功能同样 ×3 全部存活 ⇒ 与置顶功能无关 |

| D9 | 官方公告 main 图是 GIF，`BitmapImage` 只显示**首帧**，不会动 | `docs/DATA-FORMATS.md` §7.2 已记录；实测 ck3 的 main 图 URL 以 `original.gif` 结尾 |
| D10 | 公告里的 `pdxlaun://accountonboarding` 深链不跳转（卡片按钮直接禁用，只记 Info 日志）—— 官方启动器点它会跑账号引导 | 有意为之：`Process.Start` 自定义 scheme 会弹系统「找不到应用」框 |
| D11 | §0.6 的主页改版**编译已过（离线 csc + XamlCompiler pass1），但没跑过 `dotnet build`、没跑过真机 UI**；x:Bind 代码生成被 pass1 跳过，绑定只人工核对；`ApplyThemedButton` 覆盖 `ButtonBackground*` 等资源键这招没在真机上跑过，按钮悬停/按下可能仍露系统底色 | §0.6.4 的退路：改用 `ControlTemplate` |

| D12 | ~~官方 `ck3-buttonBackgroundImage` 3,818 B 被 4 KB 下限判成坏文件~~ **已修**：`AssetFileTypeDetector.MinimumFileBytes` 4 KB → **1 KB**，`OnlineChecks` 加 3,818 B 回归用例（§0.7.6） | 真机日志 `素材下载完成: ck3-button-dca8c880.png (3818 B, Png)` + `已换用新的主题素材` |
| D13 | 官方 `getAssets` 有 12 个素材描述符，本项目只实现 6 个（`appIcon / appTaskbarIcon / background / logo / button / buttonFont`），缺 `buttonHover* / buttonDisabled* / buttonText*` 三组；悬停/按下/文字按钮仍用安装目录或系统配色 | `docs/DATA-FORMATS.md` §6 与 `ThemeResolver.OverridableKeys` 对照 |
| D14 | 设置页「立即检查主题」实际会**串行刷新全部已安装游戏**（`OrderGames(only)` 只把指定款排最前，不清空其余），16:42 实测耗时 2 分 8 秒，按钮看起来像卡死 | `ThemeUpdateService.OrderGames` 代码 + 真机日志时间戳 |
| D15 | `StartBackgroundRefresh` 是进程级一次性闩（`_backgroundStarted`），所以游戏库手动「刷新」不会重查主题，同一进程内跨过 5 天 TTL 也不会自动再刷 | `ThemeUpdateService.StartBackgroundRefresh` 代码 |
| D16 | ~~上一代游戏（Stellaris）Mod 库 313 行缩略图**全灰**~~ **已解决（2026-10-26）**：根因是 `mods.dirPath` 停在旧盘（313 行里只有 2 行目录存在，mod 实际在 `F:\SteamLibrary`）+ 代码只认 `thumbnail.png` + `.mod` 的 `picture` 键从未被读取。新增 `PdxLauncher.Core/Playsets/ModThumbnailResolver.cs`（Steam 库重定位 / `picture` 优先 / `thumbnail*` 多扩展名 / `.launcher-cache` 兜底），`ModCatalog` 与 `PlaySetRepository` 共用同一实例。真机 **Stellaris 271/313（86.6%）**、Stellaris 激活播放集 **12/13**；CK3 373、V3 62 未回归；三个 `launcher-v2.sqlite` 的 MD5 前后一致。**剩余 42 个 mod 是上游工坊目录本身没有图**（其中 22 个只有 `thumbnailUrl`，要联网才拿得到 = 二期，`images.steamusercontent.com` 还不在 `OnlineHosts` 白名单）。`dirPath` 仍写旧盘、未重写（改了会影响 V1_1 加载文件与 `dir:` 匹配键，所以只让缩略图解析走重定位） | `dotnet build` 0 警告 0 错误；Checks `检查类: 9，失败: 0`，新增 `ModThumbnailChecks`（11 组用例）+ `PlaysetChecks` 里 `Stellaris 缩略图可解析数 >= 250` / `>= 11` 两条阈值断言；备份 `.scratch\rework\backup-20261026-1710\` |
| D17 | 主题按钮皮肤按官方语义重写后，**只做了离线编译 + PIL 素材对照，没跑真机 UI**：悬停 8% 白叠加、按下 0.85 缩放、禁用 55% 透明、`Fill` 拉伸（V3 金珠边是否真的不裁）、以及 7 组资源键在 WinUI 模板里是否压得住系统 hover 底色，全部未经真机确认 | 离线链 `[app] 0 errors 0 warnings` + `检查类: 10，失败: 0`；`.g.cs` 0 字节 ⇒ x:Bind 未 codegen；见 §0.10.4 |
| D18 | 官方主题里还有 `localizedImages`（play/resume/getStarted 三态整图按钮）、`buttonDisabled` / `buttonText` 素材、`height`、`effect3dDisabled`、`hoverAnimationDisabled` 未实现 —— 实测 4 个已装游戏都没用，但只要哪款游戏开始用，按钮就会退回纯文字版 | 本地 4 个 `theme-settings.json` 全量扫描（CK3 / V3 / Stellaris / PA 均无这些字段）；官方 schema 见 `docs/DATA-FORMATS.md` §6 |
| D19 | ~~全部软件数据写在 `%LOCALAPPDATA%\PdxNext`（D1 的直接诱因）~~ **已解决（2026-10-27）**：新增 `PdxDataRoot`（候选 `<exe>\data-root.txt` → `<exe>\data` → `<exe>\..\PdxNext-data`，**绝不回退 C 盘**）+ `DataRootMigrator`（旧目录只读复制、幂等 marker、不搬 logs、永不删源）；`AppServices.DataRoot` / `AppSettingsStore.DefaultPath` / `ThemeResolver.DefaultCacheRoot` / `NewsCacheRoot` / 新增 `ModThumbnailCacheRoot` 全部改到程序目录；设置页新增「程序数据目录」卡片（显示路径 + 打开目录 + 清理旧 C 盘数据）；`publish.cmd` 覆盖 `dist\PdxNext` 与暂存目录前把 `data` 搬到 `.__data__`、发布后搬回 | 自检 `检查类: 12，失败: 0`；新增 `DataRootChecks`（候选顺序 / data 被文件占位回退 / 迁移幂等 / 不搬 logs / 不删源）；`publish.cmd` 语义用同构 cmd 脚本在临时目录验证（DATA_OK / OLD_CLEAN / 备份目录不残留）；GUI 实测（2026-10-27 02:1x）：设置页「程序数据目录」「Mod 缩略图」两张卡片渲染正确，数据目录显示在程序目录下、旧 C 盘目录与清理按钮均可用（`.scratch\rework\shot-settings-bottom.png`） |
| D20 | Mod 缩略图**联网补图 + 长缓存**：本地解析不到的 42 行里 22 行只有 `thumbnailUrl`（`images.steamusercontent.com`）| 新增 `ModThumbnailCache`（索引 `mod-cache\mod-thumbnails.json`、文件 `mod-cache\mod-thumbnails\<modId N>.<ext>`、失败 6 小时退避、`Prune` 清过期与孤儿、URL 变名删旧）+ `ModThumbnailService`（`allowOnline=false` 零请求、串行、连续 2 次失败放弃本批）+ 设置页「联网图片」卡片（开关 + 保留天数 7/30/90/365，默认 30，`AppSettings.ModThumbnailCacheDays` 钳 1..3650）；Mod 库行改成 `ObservableObject` + `OneWay` 绑定，后台补图就地刷新。自检新增 `ModThumbnailCacheChecks`（roundtrip / 退避 / Prune / 索引乱码 / 零请求）；GUI 实测：Stellaris Mod 库 313 行缩略图全部正常渲染（修复前全灰），联网补图日志 `mod 缩略图联网补全：尝试 7 个，成功 5 个，失败 2 个`，`data\mod-cache\mod-thumbnails\` 落盘 5 个文件 + 索引（`.scratch\rework\shot-modlib-stellaris.png`）；CK3 382 行未回归（`shot-modlib-ck3.png`） |

---

## 8. 任务拆分（下一轮按此执行）

> 一个任务 = 一次编译 + 一次验证；依赖关系已标 blocked_by；write scopes 已给，避免并行时改同一文件。

### T1 · 统一另外四页的视觉风格（最高优先）—— **已由 §0.3 完成**

> 2026-10-26：五页已统一为 `PdxPageVeilBrush` 纱 + 卡片玻璃底衬，本任务关闭。

> 注：ModsPage 已在 2026-10-26 按「播放集页面双显示方式」需求重写（见 §0.1），本任务只剩 ModLibraryPage / DlcPage / SettingsPage。
- **目标**：Views/ModsPage.xaml（播放集）/ Views/ModLibraryPage.xaml / Views/DlcPage.xaml / Views/SettingsPage.xaml 与 HomePage 用同一套骨架：Page Background={ThemeResource PdxPageBackgroundBrush}（设置页可用 LayerFillColorDefaultBrush）+ ScrollViewer + 内容 MaxWidth=1120~1200 居中 + Border Background=PdxCardBackgroundBrush BorderBrush=PdxCardStrokeBrush BorderThickness=1 CornerRadius=8 卡片 + 标题用 TitleTextBlockStyle/SubtitleTextBlockStyle，次要文字用 CaptionTextBlockStyle + TextFillColorSecondaryBrush。
- **禁止**：新增硬编码颜色字面量（#RRGGBB / White）；新增 Pdx* 画笔前必须同时补 Default/Light/HighContrast 三处。
- **保留**：所有 AutomationProperties.AutomationId 原值不变（自动化脚本依赖它们）；ModsPage 的 PlaySetEditor 行为与 ModsPage.xaml.cs（18,551 B）的交互逻辑不动。
- **验收**：dotnet build PdxLauncher.sln -c Debug -m:1 --no-restore → 0 警告 0 错误；UIA dump 四页各有 ≥15 个元素且 AutomationId 与改前一致。
- **write scopes**：src/PdxLauncher.App/Views/ModsPage.xaml、ModLibraryPage.xaml、DlcPage.xaml、SettingsPage.xaml、src/PdxLauncher.App/App.xaml

### T2 · 主页横幅裁切与自适应 —— **已被方案 B 取代并完成（见 §0.3）**

> 横幅已删除，主页只保留页头（字标 + 标题 + 版本 + 刷新）+ 卡片，不再有"横幅"这个概念。
- **目标**：HomePage 的 Border Height=260 改成自适应（MinHeight=220 + 最大化窗口时不主导版面），BackgroundImage 保持 Stretch=UniformToFill 但加 MinHeight；LogoImage 用 MaxWidth 而非固定 200x180，让 CK3 的 SVG（viewBox 333x298.07，约 1.12:1）与 V3 的 PNG（墨迹 696x230，约 3.03:1）都能合理显示。
- **验收**：1200x800 与 1400x900 两种窗口尺寸下主页无横向滚动条、无内容被裁；UIA 里 imgHomeLogo 宽高比与源图接近。
- **write scopes**：src/PdxLauncher.App/Views/HomePage.xaml

### T3 · 补齐主题切换验证（Light / Dark / HighContrast）
- **目标**：App.xaml 已有三支 ThemeDictionaries；写验证脚本切换系统主题，逐页截图，确认没有「找不到资源」异常（缺画笔会抛 XamlParseException / 资源查找失败）。
- **验收**：三套主题下六个页面各截 1 张，程序不崩、日志无 Cannot find a Resource 类错误。
- **write scopes**：.scratch/rework/（只写脚本），必要时 src/PdxLauncher.App/App.xaml

### T4 · 复验 D1（数据目录 / 日志不落盘）+ 置顶持久化
- **结论更新（2026-10-26）**：代码没有 bug —— `AppServices.DataRoot` / `AppSettingsStore.DefaultPath` 都正确解析到
  `C:\Users\17147\AppData\Local\PdxNext`，失败原因只是**本会话沙箱拒绝应用写工作区外路径**（§4.7、D1 证据）。
  所以本任务不再是「修 bug」，而是**去真机复验**；代码兜底只剩可选加固。
- **可选加固**：AppServices.DataRoot 若 `GetFolderPath(LocalApplicationData)` 返回空串，依次退 `%LOCALAPPDATA%` → `Path.GetTempPath()`；FileLogger 建目录失败时把 `_path` 置 null 并写 `Debug.WriteLine`（现在只有 `catch` 静默）。
- **验收（真机）**：启动一次后 `%LOCALAPPDATA%\PdxNext\logs\pdxnext.log` 存在且含「PdxNext 启动」「服务装配完成」两行；`settings.json` 含 `"alwaysOnTop": true`；**杀掉进程重启后窗口 EXSTYLE 仍为 0x0108（置顶恢复生效）**，再用图钉取消后重启为 0x0100。
- **write scopes**：src/PdxLauncher.App/Services/AppServices.cs、src/PdxLauncher.Core/Infrastructure/FileLogger.cs
- **blocked_by**：无（可与 T1 并行，改的文件不重叠）

### T5 · 截图与 README 同步 —— **截图已完成，README 待收尾**

> docs/ 六张图已换（§3.3）。README 的源码计数与 docs 清单本轮修订；「UI 结构」一节仍可选。
- **目标**：重新截六页（home / library / playsets / modlibrary / dlc / settings）到 docs/；更新 README.md：删掉「复刻官方启动器」措辞改成「WinUI 3 原生风格外壳」，修正源码计数（Core 84 .cs、App 33 .cs + 8 .xaml、Checks 8 .cs），docs/ 清单补全，新增「UI 结构」一节说明 TitleBar + NavigationView + Frame 与主题画笔约定。
- **验收**：README.md 里所有数字与实际 ls 一致；docs/ 下 6 张 png 尺寸相同，sha256 互不相同。
- **write scopes**：README.md、docs/

### T6 · 交互回归（启动 / 继续 / 播放集切换）
- **目标**：btnStartGame / btnHomeContinue / btnHomePlaySets 三项实测：点开始 → PdxLauncher.App 之外出现 ck3.exe 进程且 dlc_load.json 未被破坏；切换播放集 → launcher-v2.sqlite 的 playsets 表 isActive 变化 + 主页 txtHomeCounts 同步刷新。
- **验收**：InfoBar 显示 PID；sqlite 前后 hash 对比只变 isActive；检查类仍 0 失败。
- **write scopes**：.scratch/rework/（脚本）；blocked_by：T1

### T7 · 代码清理与规范复核
- **目标**：MainWindow.xaml.cs 349 行里有 6 处 catch { Logger.Warn(...) } 兜底 —— 复核每处是否真需要（csharp-style 规范要求异常必须落到 UI 提示或日志，现在都落日志了，属合规）。另检查 GlobalUsings.cs（只有 6 个 global using，每个 .cs 需显式 using）、Directory.Build.props 的 NoWarn（CS1591;MVVMTK0045）。
- **验收**：dotnet build 0 警告；每处 catch 都有日志或 UI 反应。
- **write scopes**：src/PdxLauncher.App/MainWindow.xaml.cs、src/PdxLauncher.App/Services/

### T8 · （可选）窗口尺寸与导航折叠态
- **目标**：WinUI 3 多窗格建议 1100–1300 × 720–840 DIP，当前默认 1200x800 合规；但 MinimumWindowWidth=900 时 NavigationView 会自动折叠成 LeftCompact，需确认折叠态下 SyncSelectedNavItem 仍能选中。
- **验收**：窗口拉到 900x600，UIA 里 navLibrary 等仍存在且点击有效。
- **write scopes**：src/PdxLauncher.App/MainWindow.xaml（如需调 OpenPaneLength/CompactPaneLength）

### T9 · 补齐官方剩余 6 个主题素材 + 收敛刷新入口
- **目标**：① `ThemeSettingsDto` / `ThemeResolver` 扩到官方 12 个描述符里的另一组三对
  （`buttonHoverBackgroundImage` / `buttonHoverFont`、`buttonDisabledBackgroundImage` / `buttonDisabledFont`、
  `buttonTextBackgroundImage` / `buttonTextFont`），`Merge` 与 `BuildRemoteMap` 同步扩键，
  `ThemeSpec` 按需加字段（加法即可，别改既有语义）；② `ThemeUpdateService.OrderGames(only)` 在设置页
  「立即检查」场景下只刷那一款（给 `RefreshAsync` 加 `bool onlyThatGame` 或直接传单元素集合），消掉 D14；
  ③ 决定 D15：要么把一次性闩改成「按 gameId + 距上次检查时间」判断，要么在手动刷新游戏库时显式重置闩。
- **验收**：`dotnet build` 0 警告 0 错误；`PdxLauncher.Checks.dll` 检查类全过（现在 9 类）；
  真机选 CK3 后在 `theme-cache` 里看到 `ck3-buttonHover-<hash>.*` 等新文件；设置页点「立即检查」在 30 秒内返回。
- **write scopes**：src/PdxLauncher.Core/Online/ThemeResolver.cs、src/PdxLauncher.Core/Contracts/ThemeSettingsDto.cs、
  src/PdxLauncher.Core/Online/AssetKeys.cs、src/PdxLauncher.Core/Models/ThemeSpec.cs（按实际路径）、
  src/PdxLauncher.App/Services/ThemeUpdateService.cs、tests/PdxLauncher.Checks/OnlineChecks.cs

---

## 9. 下轮开场建议（给下一个对话的第一条 prompt）

> 读 docs/HANDOFF.md，重点看 §0.3（最新、优先级最高）。
> 当前进度：①「窗口置顶」已完成并通过运行时验证（§0.2 / §3.4），只差真机复验持久化；②「整窗背景（方案 B）」已完成并通过
> 六页真机截图验证（§0.3）；③ 五页视觉统一随之完成，主页横幅已删除；④ 构建 0 警告 0 错误、Checks 9 类 / 0 失败。
> 建议顺序：T4（真机复验 settings.json + 置顶恢复，受沙箱限制只能真机做）→ T3（Light / HighContrast 真机切主题跑一遍，
> 这是 §0.3 唯一没实测的视觉盲区）→ T6（启动 / 继续 / 播放集切换交互回归）→ T7 清理。
> 构建：`dotnet build PdxLauncher.sln -c Release -m:1`，构建前先 `Get-Process PdxLauncher.App | Stop-Process -Force`
> （进程在跑会 MSB3026 / PRI210: 0x800704c8 失败）。启动与截图方式见 §4.12 与 §3.3（cmd start 启动，
> 强制 HWND_TOPMOST 后 CopyFromScreen，且启动与截图必须在同一次 pwsh 调用里）。

---

## 10. 关键常量与素材位置速查

| 项 | 值 |
| --- | --- |
| 官方启动器目录（只读） | C:\Users\17147\AppData\Local\Programs\Paradox Interactive\launcher |
| CK3 安装 | E:\SteamLibrary\steamapps\common\Crusader Kings III（launcher/assets 下：app-background.png 2408x1340、button.png 175x50、logo.svg、mods-placeholder-background.png、news-placeholder-background.png、theme-settings.json、fonts/CormorantGaramond-Regular.ttf） |
| Victoria 3 安装 | D:\app\Steam\steamapps\common\Victoria 3（launcher/assets 下：launcher-background.jpg、launcher-button.jpg 175x50、launcher-logo.png 700x300 墨迹 696x230、app-icon.png 512x512、theme-settings.json、fonts/…） |
| Stellaris 安装 | F:\SteamLibrary\steamapps\common\Stellaris（settings 在安装根目录，无 launcher/ 子目录） |
| 游戏数据目录 | %USERPROFILE%\Documents\Paradox Interactive\<游戏>\launcher-v2.sqlite |
| 本启动器数据 | %LOCALAPPDATA%\PdxNext\（**当前未创建，见 D1**） |
| 游戏版本实测 | CK3 1.20.1.2 (Crozier)、V3 1.13.11 (Matcha) |
| -m:1 必需 | 是（见 §4.4） |
| 整窗背景解码宽度 | `MainWindow.xaml.cs` 的 `private const int BackdropDecodeWidth = 2560`（2560×1423 ≈ 14.6 MB 位图） |
| 主页字标解码宽度 | `HomePage.xaml.cs` 的 `private const int LogoDecodeWidth = 480` |
| 截图 / 启动脚本 | `.scratch/rework/flow.ps1`（杀进程 → cmd start 启动 → 六页截图）、`cap3.ps1 -ProcId <pid> -Tag <名> [-Nav <AutomationId>]`、`cardshot.ps1`（点卡片切游戏）、`paneshot.ps1`（折叠 / 小窗） |

---

## 11. 网络代理（三模式）

> 2026-10-27 完成。设置页原来只有一个「代理地址」输入框，且地址为空时 `HttpFetcher` 构造里还有一层隐式兜底（直接读环境变量），
> 用户看不出到底走没走代理、走的是哪一条。现在改成显式三选一，来源可见、可解释、可测。

### 11.1 行为

| 模式 | `proxyMode` | 解析顺序 |
| --- | --- | --- |
| 直接连接（默认） | `direct` | 不构造代理；Windows 代理与 `http_proxy` 等环境变量都不看 |
| 跟随系统 | `system` | WinINET（`ProxyEnable` / `ProxyServer` / `ProxyOverride` / `AutoConfigURL`）→ 未启用或未配置时回落 `http_proxy` / `https_proxy` / `all_proxy`（含全大写） |
| 手动配置 | `manual` + `proxyUrl` | 只用填的地址；缺 scheme 补 `http://`，两端引号去掉 |

- 旧配置迁移：`proxyMode` 为空时按 `proxyUrl` 是否有值推断（有→`manual`、无→`direct`），上一版用户行为不变。
- `ProxyServer` 支持 `http=a:1;https=b:2` 分号写法：取 `http` 那份，两侧不同会在状态行/日志提示。
- `ProxyOverride` 的 `<local>` → `WebProxy.BypassProxyOnLocal`，其余项转锚定正则（`*`→`.*`，其余字符 `Regex.Escape`）交给 `WebProxy.BypassList`。
- 不支持的情况**不静默**：SOCKS（`.NET` 的 `SocketsHttpHandler` 只能 http/https 代理）、非 http(s) scheme、系统配了 PAC（不执行脚本）、地址非法，一律直连并在设置页「当前生效」与日志里说明原因。
- 改完立即生效：`ApplyProxySettings()` 落盘 → `AppServices.TryRebuildNetworkServices(out error)` 整条重建（HttpFetcher / Metadata / Assets / Theme / DLC 简介 / 公告 / 缩略图），失败只记日志并沿用旧链路。
- 设置页「当前生效」显示来源 + 地址：`手动配置 / 系统代理 / 环境变量代理 / 直连`。

### 11.2 代码落点

- Core 新增：`src/PdxLauncher.Core/Configuration/ProxyModes.cs`（三种 key + `Normalize` + `IsManual`）、`src/PdxLauncher.Core/Online/ProxyResolution.cs`（`ProxyResolution` record + `ProxySource` 枚举）、`src/PdxLauncher.Core/Online/SystemProxySettings.cs`（注册表读取，`OperatingSystem.IsWindows()` 守卫）、`src/PdxLauncher.Core/Online/ProxyBypassPatterns.cs`（ProxyOverride → 正则）。
- Core 改写：`src/PdxLauncher.Core/Online/ProxyResolver.cs` 变成 `Describe(...)` / `ResolveUrl(...)` / `Resolve(...)`，`environment` 与 `systemProxy` 都可注入（测试用）；`src/PdxLauncher.Core/Configuration/AppSettings.cs` 加 `ProxyMode`；`src/PdxLauncher.Core/Online/HttpFetcher.cs` **删掉隐式兜底**（代理只由调用方决定），并加 `DefaultProxyCredentials = CredentialCache.DefaultCredentials` 应对 407。
- App 新增：`src/PdxLauncher.App/Services/ProxyModeCatalog.cs`（`ProxyModeOption` + 三项 `All` + `Resolve`，返回同一实例以配合 ComboBox 引用相等）。
- App 改写：`src/PdxLauncher.App/Services/AppServices.cs` 的 `RebuildNetworkServices()` → `public static bool TryRebuildNetworkServices(out string? error)`（未初始化时给出「下次启动生效」；内部 try/catch）；`src/PdxLauncher.App/ViewModels/SettingsViewModel.cs` 加 `ProxyModeOptions` / `SelectedProxyMode` / `IsManualProxy` / `ProxyStatusText` + `RefreshProxyStatus()`，`ApplyProxySettings()` 改成返回 bool；`src/PdxLauncher.App/Views/SettingsPage.xaml` 新增「网络代理」卡片（`cmbProxyMode` / `txtProxyUrl` / `txtProxyStatus`，地址行只在手动模式可见），地址框加 `KeyDown`（回车即应用）、`LostFocus`、`TextChanged`；`src/PdxLauncher.App/Views/SettingsPage.xaml.cs` 加 `OnProxyUrlKeyDown`。
- 测试：新增 `tests/PdxLauncher.Checks/ProxyChecks.cs`（第 13 个检查类），覆盖模式归一化/旧配置迁移、手动地址补 scheme 与去引号、拒绝 SOCKS 与 ftp、`user:pass@` → `NetworkCredential`、系统代理优先与 `ProxyEnable=0` 回落、`http=a;https=b`、PAC 不回退环境变量、`ProxyOverride` 正则语义、日志文案；`tests/PdxLauncher.Checks/OnlineChecks.cs:420` 的 `CheckProxyResolver` 同步改成显式模式。

### 11.3 验证方式（重要，沙箱限制）

本会话沙箱里 **MSBuild 无法编译**：Csc 任务用管道捕获子进程输出被拒（`MSB3883 意外的异常: 拒绝访问`），XamlCompiler 写 `obj\...\input.json` 也被拒；
沙箱只允许 DSH 的文件工具创建目录/文件，bash 在 workspace 里连 `mkdir` 都被拒。

因此 Core + Checks 用**手工 csc** 验证（脚本：`.scratch/manual-check.sh`，单次 bash 调用内完成）：

```bash
bash .scratch/manual-check.sh
```

- 把 `Microsoft.NETCore.App.Ref/10.0.13/ref/net10.0/*.dll` 与源文件写进 csc response file（路径必须带引号，否则空格会把 `-r:` 拆开）；
- 编出 `PdxLauncher.Core.dll` + `PdxLauncher.Checks.dll`，手写 `runtimeconfig.json` 后 `dotnet exec` 跑；
- **工作目录必须是仓库根**，否则 `ChecksContext.FixturePath` 找不到 `tests/fixtures`（`AppContext.BaseDirectory` 在临时目录里，向上找不到仓库）。

结果（2026-10-27）：Core / Checks 均 0 错误；`检查类: 13，失败: 1`，唯一失败是既有用例
`stellaris 缺 dlcPath → 默认 ./ 相对 settings 目录`（与本改动无关，见于手工 csc 环境）。

**App 层（XAML / WinUI）只能真机验证**：

```bat
build.cmd
```

手动验收三档：①「直接连接」关掉系统代理 → 设置页显示「直连」、日志「未配置代理，直连」；②「跟随系统」开系统代理 127.0.0.1:7890 → 显示「系统代理 http://127.0.0.1:7890」、日志「跟随系统代理」；③「手动配置」填 127.0.0.1:7890 → 显示「手动配置」、日志「使用代理」。

### 11.4 后续可选

- 现在每次切换模式都会整条重建联网链路（正在下载会失败一次，重试即可）；若嫌吵，可以只在 `proxyUrl` / 模式实际变化时重建。
- `SettingsPage.xaml` 的「代理地址」行现在只在手动模式可见，若手动模式下填错后切到别的模式，旧地址仍留在配置里（切回即恢复，这是有意的）。

---

## 12. 整窗背景五档 + Mod 缩略图三档 + 导航顺序（2026-10-27 02:30）

> 用户诉求：整窗背景不能只有「铺满」一种，要能选覆盖 / 填充 / 完整模糊底 / 完整纯色边 / 居中原始像素；Mod 库缩略图要能调大中小；导航顺序把「当前游戏」放最前。
> 现状：代码已落盘，`dotnet build PdxLauncher.sln -c Release` **0 警告 0 错误**，自检 **13 个检查类 / 0 失败**，并已逐档真机截图取证。

### 12.1 改动清单（已落盘）

| 文件 | 内容 |
| --- | --- |
| `src/PdxLauncher.Core/Configuration/BackgroundDisplayModes.cs`（新增） | 五个 key 常量 `cover` / `fill` / `fullBlur` / `fullPlain` / `center`；`Normalize`（未知/空 → `cover`）；`UsesBlurredBackdrop`（只有 `fullBlur` 需要模糊铺底） |
| `src/PdxLauncher.Core/Configuration/ModThumbnailSizes.cs`（新增） | `small` / `medium` / `large` = `SmallPixels` 32 / `MediumPixels` 48 / `LargePixels` 72 DIP；`Normalize`（未知 → `medium`）；`Pixels` |
| `src/PdxLauncher.App/Services/BackgroundDisplayModeCatalog.cs`（新增） | `BackgroundDisplayModeOption(Key, Title, Description)`；`All` 五项给 ComboBox；`Resolve(key)` 返回 `All` 里的**同一实例**（ComboBox 的 SelectedItem 按引用匹配）；`ApplyTo(ImageBrush, key)` 把档位换算成 Stretch / 对齐 |
| `src/PdxLauncher.App/MainWindow.xaml` | 结构没动，直接用既有四层：`ShellBackgroundLayer`（:25）/ `ShellBackgroundBlurRect` + `ShellBackgroundBlurBrush`（:34 / :40）/ `ShellBackgroundRect` + `ShellBackgroundBrush`（:49 / :53）/ `ShellScrim`（:66）；导航顺序已是 `navHome`（:157）→ `navPlaySets`（:166）→ `navModLibrary`（:175）→ `navDlc`（:184）→ `navLibrary`（:192），设置固定最下（`navSettings` :203） |
| `src/PdxLauncher.App/MainWindow.xaml.cs` | `UpdateBackdropAsync()`（:424）末尾调 `ApplyBackgroundDisplayModeAsync()`（:488）：按档位设 Stretch / 对齐、联动 `ShellScrim.Visibility`；`fullBlur` 需要铺底时懒加载 96 px 副本（`LoadBlurredBackdropAsync` :517）；`OnBackgroundDisplayChanged`（:543）把设置页的改档派发到 UI 线程重画 |
| `src/PdxLauncher.App/ViewModels/SettingsViewModel.cs` | 新增 `BackgroundDisplayModeOptions` / `SelectedBackgroundDisplayMode`（:37-40 起）与 `ModThumbnailSizeOptions` / `SelectedModThumbnailSize`，选中即落盘 |
| `src/PdxLauncher.App/Views/SettingsPage.xaml` | 两个 ComboBox + 档位说明：`cmbBackgroundDisplayMode`（:225）、`cmbModThumbnailSize`（:249） |
| `src/PdxLauncher.App/ViewModels/ModLibraryViewModel.cs` / `ModsViewModel.cs` | `:259` / `:496` 用 `ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize)` 撑缩略图边长 |

### 12.2 五档定义（用户可见文案 → 渲染）

| 档 | 名称 | Stretch / AlignmentX | 空白怎么补 |
| --- | --- | --- | --- |
| `cover` | 覆盖（默认） | UniformToFill / Left | 无空白（左边超出部分裁掉） |
| `fill` | 填充 | Fill / Center | 无空白（比例被拉伸） |
| `fullBlur` | 完整（模糊底） | Uniform / Center | 同一张图 96 px 宽解码后拉伸铺底 |
| `fullPlain` | 完整（纯色边） | Uniform / Center | 露出 `PdxWindowBackgroundBrush` 底色 |
| `center` | 居中（原始像素） | None / Center | 四周露出底色，超出窗口的部分裁掉 |

### 12.3 真机取证（2026-10-27 02:0x）

- 逐档日志（`data\logs\pdxnext.log`）与截图完全对得上：
  - `背景显示方式: cover（Stretch=UniformToFill, AlignmentX=Left, 有图=True）`
  - `背景显示方式: fill（Stretch=Fill, AlignmentX=Center, 有图=True）`
  - `背景显示方式: fullBlur（Stretch=Uniform, AlignmentX=Center, 有图=True）`
  - `背景显示方式: fullPlain（Stretch=Uniform, AlignmentX=Center, 有图=True）`
  - `背景显示方式: center（Stretch=None, AlignmentX=Center, 有图=True）`
- 五张截图均 1782×1191、SHA256 互不相同：`.scratch/bg-cover.png`（437F5831…）、`.scratch/bg-fill.png`（9E3AAEAF…）、`.scratch/bg-fullBlur.png`（DCBF7703…）、`.scratch/bg-fullPlain.png`（E003B6C5…）、`.scratch/bg-center.png`（BDFA40E9…）。`fullPlain` 上下露出底色、`center` 只显示中间区，与 `cover` 目视差异明显。
- Mod 缩略图用 UIA 量 `modLibraryRow_*` 的 BoundingRectangle 行高：small **55 px** / medium **72 px** / large **108 px**（150% DPI ⇒ 32 / 48 / 72 DIP）；截图 `.scratch/mods-small.png`、`.scratch/mods-large.png`。
- 可复用脚本：`.scratch/bg_modes.ps1`（逐档写 settings.json → 启动 → `cap_modes.ps1` 截图 → 还原备份）、`.scratch/mods_sizes.ps1`、`.scratch/mods_measure.ps1`。

- **改档即时生效**也验过（不重启）：同一进程里用 UIA 把下拉框从「覆盖」切到「居中（原始尺寸）」，日志立刻出现 `背景显示方式: center（Stretch=None, AlignmentX=Center, 有图=True）`，`data\settings.json` 当场写成 `"backgroundDisplayMode": "center"`，截图 `.scratch/live-center.png` 里背景已是 1:1 原始像素居中（比 cover 明显放大）。脚本：`.scratch/live_switch.ps1`。

> ⚠ 两个坑（本轮都踩了）：
> 1. `settings.json` 与日志在**程序目录**的 `data\`（`PdxDataRoot`，见 `docs/DATA-FORMATS.md` §12），**不是** `%LOCALAPPDATA%\PdxNext`；截图脚本指错路径会白跑一整轮。
> 2. 改 settings.json 别用 `ConvertFrom-Json | ConvertTo-Json` 往返：没显式加进去的键会被丢掉，`windowWidth/windowHeight` 还会变成 158/26。备份在 `.scratch/settings-repaired.json`。

### 12.4 两处现象（不属于本节改动，未处理）

- 用 UIA 点「Mod 库」导航时偶尔会跳到主页 / DLC 页，并把 CurrentGame 从 ck3 翻成 stellaris（`SelectGame` 的调用点只有 `AppServices.cs:153/162/195`、`LibraryViewModel.cs:95`、`ShellViewModel.cs:80-83`，导航本身不该触发）——疑似并发会话正在改的标题栏 / 公告线，先记录。
- 日志里 `[WARN] 修正标题栏留白失败: Invalid argument. (Parameter 'value')` 同样属标题栏那条线。

---

## 13. 界面语言（中/英）+ DLC 缓存 v2 + 统一缓存卡片（2026-10-27 04:35）

> 用户诉求：设置里能选界面语言（全 UI 多语言，不只设置页），并重新设计缓存（DLC 简介缓存 + 一个统一的缓存管理卡片）。
> 现状：代码已落盘，`bash tools/build-app.sh -v q` **0 警告 0 错误**，自检 `检查类 15 / 失败 0`，中英两套界面 + 运行时热切换已真机截图取证。

### 13.1 多语言文案链路（新增）

| 文件 | 内容 |
| --- | --- |
| `tools/i18n/zh-CN/<area>.json`、`tools/i18n/en-US/<area>.json`（新增） | 文案源，按 area 分文件：`common` / `settings` / `shell` / `home` / `library` / `mods` / `modlib` / `dlc`，目前 **498 个键** |
| `tools/gen-i18n.py`（新增） | 合并同一语言目录下所有 `*.json`（同键跨文件重复即报错）→ 校验两语言键集一致、值非空白无首尾空格、`{0}`/`{1}` 序列一致 → 生成 `src/PdxLauncher.App/Localization/Strings.g.cs`（键 → 标识符，如 `app.name` → `AppName`）；`--check` 只校验且比对生成物是否最新 |
| `src/PdxLauncher.App/Localization/Strings.g.cs`（机器生成，勿手改） | `partial class Localizer`：`TableZhCN` / `TableEnUS` 两个字典 + `AllKeys` + 每键一个 `public string Xxx => Format("xxx.yyy");`，另有 `internal static class Keys` 常量 |
| `src/PdxLauncher.App/Localization/Localizer.cs`（新增，手写 partial） | `Localizer.Instance`；`Language`；`Apply(string? languageKey)`（`UiLanguages.Normalize` → 选表 → 设 `CultureInfo.DefaultThreadCurrentUICulture` → 逐键发 PropertyChanged）；`T(key, args)`；查不到时返回 `⟨key⟩` 并 `Debug.Fail`；**订阅用 `List<WeakReference<LocalizedObservableObject>>`，不再有事件泄漏** |
| `src/PdxLauncher.App/Localization/LocalizedObservableObject.cs`（新增） | 页面 VM / 行 VM 的基类：构造时 `Localizer.Instance.RegisterLocalized(this)`，切语言时对该类型所有 public 属性发 PropertyChanged 再调 `Relocalize()` |
| `src/PdxLauncher.Core/Configuration/UiLanguages.cs`（新增） | 只两档：`zh-CN` / `en-US`；`IsFollowSystem`（空白）/ `Normalize` / `ResolveFromCulture`（zh* → 简中，其余 → 英文） |
| `src/PdxLauncher.Core/Online/SteamStoreLanguage.cs`（新增） | UI 语言 → 商店 `l=`：`zh-CN → schinese`、`en-US → english` |
| `AppSettings.UiLanguage`（新增，`string?`） | null / 缺失 = 跟随系统 |
| `App.xaml.cs` | `ComposeServices()` 之后立刻 `Localizer.Instance.Apply(AppServices.Settings.UiLanguage)`，必须早于任何页面 XAML 加载 |

规则：

- **XAML 里一律 `{x:Bind L.Xxx, Mode=OneWay}`**（`OneTime` 不会随切语言刷新）；页面后置加 `public Localizer L => Localizer.Instance;`。
- **代码里拼文案不许直接拼字符串**：状态栏用 `SetStatus(key, args...)` 记住键与参数，`Relocalize()` 里重放；只接收外部成品文本的地方用 `SetStatusText(text)`。
- 下拉选项类（`ProxyModeOption` / `PlaySetPageDisplayModeOption` / `BackgroundDisplayModeOption` / `ModThumbnailSizeOption` / `RetentionDaysOption` / `UiLanguageOption`）都继承 `LocalizedObservableObject`，由 i18n 键驱动 `Title` / `Description`，并保留静态 `All` / `Resolve`（ComboBox 的 SelectedItem 按引用相等匹配）；`ToString()` 返回 `Title`。
- 日志、`Debug.Fail`、内部异常消息**保持中文**，不进 i18n；`AutomationId` 保持 ASCII 且不变（真机脚本依赖）。

### 13.2 设置页信息架构（重写）

- 左侧 6 分类 ListView（`lstSettingsSections`）：外观 / 启动 / 路径 / 联网 / 缓存 / 高级 —— 右侧同时只显示一类（`secAppearance`…`secAdvanced` 六个 `x:Name` 容器切 `Visibility`，`CacheSectionIndex = 4`）。
- 切分类前先 `PersistSettings()`（把正在输入的代理地址落盘）；每个分类的 `ScrollViewer` 用 `VerticalScrollBarVisibility="Visible"` 预留滚动条槽位，避免「内容一高就抖」。
- 外观卡里的「界面语言」下拉 = `跟随系统 / 简体中文 / English`；选中立即 `Localizer.Instance.Apply`，状态栏提示「界面语言已切换为「…」，DLC 简介在下次加载时按新语言获取」。

### 13.3 DLC 简介缓存 v2（`DlcDescriptionService`）

- 缓存文件 `data/dlc-descriptions.json` 升到 `version: 2`：

```json
{ "version": 2, "lastFailureUtc": "…",
  "languages": { "schinese": { "<steamId>": { "text": "…", "fetchedUtc": "…" } },
                 "english":  { } } }
```

- v1 的扁平 `entries` 读到时迁移进 `schinese` 桶（`MigrateLegacyEntries`），不会丢已有简介。
- 正缓存天数由设置项 `dlcDescriptionCacheDays`（1..365，默认 7）**调用时传入**，不写进缓存文件；过期但可用的条目先返回旧文本再后台刷新。
- 并发 3；首批网络错误即 `abort.Cancel()` 整批并写 `lastFailureUtc`（30 分钟退避）；`success:false` 与解析失败都写空串做负缓存；正文剥 HTML/实体、压空白、截断 400 字。
- 接口已是 `GetDescriptionsAsync(steamIds, storeLanguage, cacheDays, allowOnline, ct)` + `ClearCache()`。
- 商店接口约束（实测）：**一个 appid 一次请求**（`appids=A,B` 返回 400），**不要加 `filters=`**（`filters=short_description` 会 200 但 `data` 整个缺失，等于把简介写成负缓存）。Steamworks SDK 没有商店文案；`IStoreBrowseService/GetDLCForApps` 只有 appid/价格/日期。

### 13.4 统一缓存卡片

- `ViewModels/CacheRowViewModel.cs`（新增）：四行 `theme` / `news` / `modThumbnails` / `dlcDescriptions`，各带标题、说明、路径、占用与「清除」按钮。
- `ViewModels/SettingsViewModel.cs`：`CacheRows` / `CacheTotalText`（`缓存合计 {0}`）/ `RefreshCacheStatsAsync`（`Task.Run` 里 `CacheStorage.Measure`）/ `ClearCacheAsync` / `ClearAllCachesAsync`；按 key 分派到 `CacheStorage.Clear`、`NewsFeed.ClearCache()`、`ModThumbnails.Cache.Clear()`、`DlcDescriptions.ClearCache()`。
- `Core/Storage/CacheStorage.cs`（新增）：`Measure(fs, path)` → `CacheUsage(Bytes, Files)`；`Clear(fs, path)` 文件删文件、目录递归删，失败只 Warn。
- 「清除全部」有 ContentDialog 二次确认，并明说只动缓存、不动 `settings.json` / 日志 / 播放集数据库。

### 13.5 验收取证

- `bash tools/build-app.sh -v q` → 0 警告 0 错误；`tests/PdxLauncher.Checks` → 检查类 15 / 失败 0。
- 英文界面：左栏 `Current Game / Playsets / Mod Library / DLC / Game Library / Settings`，设置页六分类 `Appearance / Launch / Paths / Online / Cache / Advanced`，缓存卡 `Cache total 1.8 KB` + 四行。
- 运行时热切换（UIA 驱动下拉 + 截图）：英文状态下把「Interface language」切到「简体中文」，**不重启**整页立即变中文（导航、卡片、状态栏「界面语言已切换为「简体中文」…」全部刷新）。
- 中文缓存卡：`缓存合计 2.8 MB`、`主题素材 2.8 MB, 7 个文件`、`官方公告 1.6 KB, 1 个文件`、`Mod 缩略图 暂无缓存`、`DLC 简介 暂无缓存`。
- 下拉项的 UI 自动化名称：`AutomationProperties.Name="{x:Bind Title, Mode=OneWay}"` 必须写在 `ItemTemplate` 根元素上，否则 UIA 读到的是类型全名（`PdxLauncher.App.Services.UiLanguageOption`）。

> ⚠ 本轮踩到的坑：
> 1. **XAML 里绑了不存在的 `L.Xxx` 标识符 → XamlCompiler 报 `WMC9999` 且没有行号**（真实原因是取本地化错误消息时炸掉）。改 XAML 前先跑 `python .tmp/checkbind.py`（从 `Strings.g.cs` 抽标识符集合，扫 `Views/**/*.xaml` 的 `L.<Ident>`，打印 MISSING）。
> 2. 多个并行会话同时写 `tools/i18n/*` 时，半成品键集会让生成器报「两语言键集不一致」→ **先补齐对侧再继续**，别等对方。
> 3. 裸 `AutomationId=` 在 `Border` 等非 FrameworkElement 上没有该属性，必须写 `AutomationProperties.AutomationId=`（本轮批量修了 39 处）。
> 4. WinUI 窗口截图只能靠 `CopyFromScreen`；UIA 点击要 `SelectionItemPattern.Select()`，`mouse_event` 对 WinUI 无效。

---

## 14. 应用图标（2026-10-27 14:40）

> 本节是速查 + 复现步骤；改动背景与验证过程见 §0.15。设计：石墨炭圆角方底 + 金色「P」，P 的碗（计白）里放一枚金色播放三角。

### 14.1 一眼速查

| 项 | 值 |
| --- | --- |
| 唯一生成源 | `tools/gen-app-icon.py`（Pillow + numpy 自绘；机器上无 SVG 栅格化工具，**没有 SVG 母版**） |
| 生成产物 | `src/PdxLauncher.App/Assets/AppIcon.ico`（9 帧 / 73,005 B / MD5 `d94e4946cc7c9da1e8194f8ce038befa`）+ 同目录 8 张模板同名 PNG + `docs/app-icon.png`（接触表） |
| exe 图标 | csproj `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>`（现第 17 行） |
| 窗口 / 任务栏图标 | `MainWindow.ApplyWindowIcon()`（`MainWindow.xaml.cs:109-133`）→ `AppWindow.SetIcon(<exe 目录>\Assets\AppIcon.ico)`，在构造里紧跟 `ApplyWindowSize()` 调用 |
| 帧策略 | 16/20/24/32/40/48/64/128/256 全 32bpp；`<128` 用 BMP 帧（带 AND mask）、`>=128` 用 PNG 帧；首帧偏移 150、末帧正好收在文件末尾 |
| 设计坐标 | `UNIT=1024`、`TILE_INSET=40`、`TILE_RADIUS=210`、字标 `CAP_TOP/BOTTOM=232/792`、`STEM_X0=258`、`STEM_WIDTH=122`、`BOWL_RIGHT=766`、`BOWL_RING=82`、`PLAY_TRI=((410,352),(410,492),(600,422))`、`PLAY_CORNER=20`、小尺寸光学加粗 `_weight()` 16-24px=1.08 / 32-40px=1.04 |
| 配色 | 底 `#2E2E36 → #1B1B1F → #0E0E12`（纵向渐变 + 暗角）；金 `#FFE3A3 → #F2C14E → #C08A2B`；描边白 13%（≥32px）、投影（≥48px） |
| 自检 | `tests/PdxLauncher.Checks/IconChecks.cs`（ICO 容器/帧集合/偏移/BMP-PNG 策略/金色占比/圆角透明/8 张 PNG/三处接入点） |
| 未做 | 标题栏小图标；随游戏切任务栏图标（`ThemeSpec.AppIcon` / `AppTaskbarIcon` → `ThemeVisuals.AppIcon` 仍是死字段）；SVG 母版 |

### 14.2 重新生成 / 先看效果

```bat
python tools\gen-app-icon.py --preview        :: 只写 .tmp\icon-preview\（接触表 .tmp\icon-preview\app-icon-play.png）
python tools\gen-app-icon.py                  :: 正式写 Assets 里 9 个文件 + docs\app-icon.png
python tools\gen-app-icon.py --variant plain  :: 纯 P 环、不放播放三角
```

改形状 / 配色就改生成器顶部的常量，别手改 PNG 与 ICO。跑完必须 `build.cmd`（`IconChecks` 会拦住手误：帧数、尺寸集合、偏移收尾、BMP/PNG 分界、金色占比、8 张 PNG 的 IHDR 尺寸、三处接入点字符串）。

### 14.3 怎么确认 exe / 窗口真的带上了图标

```powershell
# exe 资源（nIconIndex 必须给 0，给 -1 会返回 0 个图标）
$h = New-Object IntPtr[] 1; $ids = New-Object uint32[] 1
[Native.Icons]::PrivateExtractIcons($exe, 0, 48, 48, $h, $ids, 1, 0)

# 运行中的窗口
[Native.Win]::SendMessage($hwnd, 0x007F, [IntPtr]1, [IntPtr]::Zero)   # WM_GETICON / ICON_BIG
```

- 现成脚本：`.tmp\verify\exe-icons.ps1`（提 16/24/32/48/256 并存 PNG）、`.tmp\verify\compare-icons.py`（与 ICO 帧逐像素比对，现测得 **0.00**）、`.tmp\verify\window-icon.ps1`（启动 → `WM_GETICON` / `GCLP_HICON` → 截图 → 关掉进程）、`.tmp\verify\taskbar-icon.ps1`（任务栏截图；本机任务栏是**竖排贴右边缘**，别照底部任务栏的老经验找）。
- 这些脚本在 `.tmp\` 下，属于临时产物，随时可删；要重看图标效果，直接跑生成器的 `--preview` 更省事。

### 14.4 已知边界

- Windows 浅色 / 高对比度任务栏下的实际观感只核过接触表的浅底行，没在真机浅色任务栏上截图。
- Explorer 的「大图标 / 超大图标」视图会用 128 / 256 的 PNG 帧，没实拍。
- 图标是进程级窗口图标：以后若接入官方主题的 `app-icon`，切游戏会整体换掉任务栏图标，需要做成「主题有就用、没有回落 AppIcon.ico」。