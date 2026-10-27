# 设置页改版说明（2025-10-27）

## 目标与结果

把原来「一整列 10 张卡片、每张卡里 160/`*` 左标签右控件」的设置页，改成 Windows 11 设置风格：

- 左侧 216px 子分类导航（外观 / 启动 / 路径 / 联网 / 缓存 / 高级），右侧只显示当前分类。
- 每行一个 `SettingsCard`（标题 + 可选说明 + 右侧控件），行卡统一描边、圆角、56px 最小高度。
- 状态栏移出滚动区、固定在页面底部；无文案时自动收起。
- 页头（标题 + 当前游戏名）与导航同列显示。

## 结构

```
Page
└─ Grid Padding=20 RowSpacing=12（两行，* / Auto）
   ├─ Row0 Grid ColumnSpacing=16（216 / *）
   │  ├─ Col0 NavigationView x:Name=navSettingsSections（AutomationId=lstSettingsSections，
   │  │   PaneDisplayMode=Left、OpenPaneLength=216、IsPaneToggleButtonVisible=False、
   │  │   IsSettingsVisible=False、Background=Transparent）
   │  │   ├─ PaneHeader：页头（标题 + 当前游戏名）
   │  │   └─ MenuItems：六个 NavigationViewItem（Tag=0..5，各带 SymbolIcon），
   │  │       SelectionChanged=OnSectionChanged，AutomationId 见下
   │  └─ Col1 ScrollViewer（VerticalScrollBarVisibility=Visible 常显、
   │        HorizontalScrollBarVisibility=Disabled、HorizontalContentAlignment=Stretch）
   │     └─ StackPanel Spacing=12 Margin=0,0,14,0（右侧 14 DIP 是滚动条通道）
   │        ├─ StackPanel x:Name=secAppearance
   │        ├─ StackPanel x:Name=secLaunch   Visibility=Collapsed
   │        ├─ StackPanel x:Name=secPaths    Visibility=Collapsed
   │        ├─ StackPanel x:Name=secOnline   Visibility=Collapsed
   │        ├─ StackPanel x:Name=secCache    Visibility=Collapsed
   │        └─ StackPanel x:Name=secAdvanced Visibility=Collapsed
   └─ Row1 Border（状态栏，txtSettingsStatus，同样 14 DIP 右侧通道以对齐右缘）
```

- 自绘控件：`src/PdxLauncher.App/Controls/SettingsCard.xaml(.cs)`，三个依赖属性
  `Header`（string）、`Description`（string，空则隐藏）、`ActionContent`（object，右侧控件）。
  边框用 `PdxCardBackgroundBrush` / `PdxCardStrokeBrush`，与主题（浅色/深色/高对比）联动。
- **滚动条通道**：WinUI 3 的 Fluent 滚动条是浮层指示器，贴在 `ScrollViewer` 右边缘绘制、
  不占内容宽度。初版内容一路撑到右边缘，轨道和滑块就压在卡片的 1 px 描边与 8 px 圆角上
  （悬停/滚动时表现为「内容和滚动条重叠」）。修法是滚动内容与状态栏各留 14 DIP 右侧通道
  （12 DIP 滚动条通道 + 2 DIP 余量），滚动条改画在通道里。注意只能给**内容**留白：
  给 `ScrollViewer` 本身加 Margin 只会把轨道跟着卡片一起往里挪，问题不变。
- **分节导航控件**：左侧六分类用官方 `NavigationView`（与主窗口左栏同一控件、同 216 宽），
  不是自绘。选中指示条（3 DIP）左侧按 WinUI 规范留的是**图标位**：条目带 `SymbolIcon`
  后依次是「条 → 图标 → 文字」。初版用纯文字 `ListView`，文字直接落在条目内边距 8 DIP 处，
  紧贴指示条（视觉上「条和字太近」），换控件 + 加图标后条右缘到文字 ≈ 41 DIP。
  用 `ListView` 也能通过加大 `Padding` 拉开，但那样条与字之间仍是空的，不符合导航规范。
- 分节切换在 `SettingsPage.xaml.cs`：
  `OnSectionChanged(NavigationView, NavigationViewSelectionChangedEventArgs)` →
  `CurrentSectionIndex()`（解析 `NavigationViewItem.Tag`，"0"–"5"）→ `ShowSection(index, refreshCache)`；
  切到缓存节时后台刷新缓存体积（`CacheSectionIndex = 4`）。
- 新增 AutomationId：`lstSettingsSections`（沿用旧名，挂在 `NavigationView` 上，未改成 navSettingsSections）、
  `navSettingsAppearance`、`navSettingsLaunch`、
  `navSettingsPaths`、`navSettingsOnline`、`navSettingsCache`、`navSettingsAdvanced`。
  原有 39 个 AutomationId 全部保留原值。

## 三个「设置一变就跳」的问题及修法

1. **改完选项整页跳一下 / 滚动条闪现**
   - 滚动条改为常显（`VerticalScrollBarVisibility="Visible"`）：浮层滚动条本身不占布局宽度，
     这条并不能消除宽度抖动，只是不让滚动条随内容高度闪现；真正起作用的是下面两条。
     顺带：常显也意味着必须给它留通道，否则会压住卡片（见上文「滚动条通道」）。
   - 状态栏从滚动区挪到页面底部固定行。
   - 代理地址输入框不再随「手动代理」折叠，改为 `IsEnabled` 绑定（行高恒定）。
   - 数值型长文本统一 `TextTrimming="CharacterEllipsis"` + `TextWrapping="NoWrap"` + tooltip 看全文。

2. **下拉里选项文字会变**
   - 取消两行 `ItemTemplate`（标题 + 会换行的描述），改为 `DisplayMemberPath="Title"` 单行；
     描述改挂在下拉框的 `ToolTipService.ToolTip` 上，随选中项变化。
   - 下拉框固定 `MinHeight="32"`，弹层与关闭态文字行数一致。

3. **改一项之后其它值被重置**
   - 根因：主题联网检查后 `ThemeUpdateService` 会 `AppServices.SetGames(...)`，
     `ReconcileCurrentGame` 产生新的 `GameInstallation` 实例，`ReferenceEquals` 判定必然为 false，
     于是每次都广播 `CurrentGameChanged`；设置页收到后无条件 `ReloadAsync()`，
     而 `LoadAsync` 会清空签名、把「其它可执行文件」重置成第一项、并从 settings.json 重读代理。
   - 修法：`SettingsViewModel.LoadAsync` 先做 `IsSameGame`（GameId + InstallRoot）判断，
     同一游戏只刷新展示字段、不覆盖用户输入；新增 `RefreshAfterGameRefresh(game)`，
     页面侧 `HandleCurrentGameChanged` 命中同游戏时走轻量刷新，只有真正换了游戏才整页重载。

## 文案与本地化

- 新增 6 个导航文案键：`settings.nav.{appearance,launch,paths,online,cache,advanced}`；
  危险区新增 `settings.danger.regenerateTitle`。`tools/i18n/{zh-CN,en-US}/settings.json` 同步维护，
  `python tools/gen-i18n.py` 生成 `Localization/Strings.g.cs`（当前 499 个键，`--check` 通过）。
- 9 段长说明（主题 / 代理 / 界面 / DLC / 公告 / 程序数据 / 缩略图 / 缓存 / 危险）各压成一句话。
  改版前的完整长文案留在 `backup-settings-0350/settings.zh-CN.json`、`settings.en-US.json`（仓库根目录）。

## 验证记录

- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误。
- `bash .scratch/rework/build-checks.sh`：15 个检查类、断言失败 0。
- `python tools/gen-i18n.py --check`：499 个键合法。
- UI 自动化（`pwsh .scratch/rework/verify-settings2.ps1`）：六个分类逐个切换，
  各节期望的 AutomationId 全部命中（appearance 42 / launch 35 / paths 42 / online 41 / cache 34 / advanced 34），
  截图见 `.scratch/rework/settings-{appearance,launch,paths,online,cache,advanced}.png`。
- 症状回归（`.scratch/rework/verify-settings3.ps1`）：切换「背景显示方式」与开关「窗口置顶」后，
  其它下拉与开关的值保持不变；被操作控件的位置与尺寸在前后完全一致（`1323,940,492x60`），
  即不再出现整页跳动。
- 语言切换（`.scratch/rework/verify-lang2.ps1`）：简中 ↔ English 双向切换，导航与卡片文案、状态栏提示同步更新。

### 滚动条通道（2026-10-27 14:50）

用户反馈「设置页面的内容和滚动条重叠了」。WinUI 3 的 Fluent 滚动条是浮层指示器，贴在
`ScrollViewer` 右边缘绘制、不占内容宽度；改版初版内容一路撑到右边缘，悬停/滚动时轨道直接画在卡片的
1 px 描边和 8 px 圆角上。现在给滚动内容与状态栏各留 14 DIP 右侧通道，滚动条落在通道里。

- 取证脚本：`.scratch/rework/verify-scrollgutter.ps1`（启动 → 设置/外观 → 悬停 / 滚动 / 空闲三态截图），
  测量脚本 `.scratch/rework/gutter-probe.py`。
- 1800×1200 窗口（150% DPI）：卡片填充右缘 `x=1735`、卡片描边 `x=1736–1737`、滚动条轨道 `x=1750–1753`
  ⇒ 描边到轨道左缘留空 **12 px（≈8 DIP）**；指针移出 3.6 s 后同一列无亮像素（自动隐藏生效）。
- 窄窗口 `.scratch/rework/verify-narrow-gutter.ps1`（1000×700）：卡片填充右缘 `x=935`、描边 `936–937`、
  滚动条 `951–952`，间隙 13 px；移出 5.2 s 后滚动条消失。缩放窗口不改变通道宽度（14 DIP 是常数）。
- 布局回归（`.scratch/rework/verify-settings3.ps1`）：被操作的 `cmbBackgroundDisplayMode` 在切换「背景显示方式」、
  开关「窗口置顶」前后位置尺寸完全一致 = `1302,940,492x60`；比改版记录的 `1323,940,492x60` 左移 21 px
  （14 DIP × 150% DPI），宽度 492、高度 60、y=940 不变。
- 六分类 UIA 计数复核：appearance 42 / launch 35 / paths 42 / online 41 / cache 34 / advanced 34，`missing=[]`。
- `cmd /c build.cmd`：16 个检查类、失败 0、断言失败 0；`python tools/gen-i18n.py --check`：499 个键合法。

### 分节导航改用 NavigationView（2026-10-27 15:10）

用户反馈「选择状态的条和字太近」，并问是不是自绘 UI、有没有官方控件可用。

- 结论：条是 WinUI `ListViewItemPresenter` 自带的选中指示器（3 DIP），不是自绘；显得挤是因为
  条目是**纯文字**（`Padding="8,10"`），文字落在条目内边距 8 DIP 处，而 WinUI 规范里指示条左侧
  留的是图标位。官方做法就是带图标的导航条目。
- 改动：`src/PdxLauncher.App/Views/SettingsPage.xaml` 里 Col0 的 `ListView` 换成 `NavigationView`
  （`PaneDisplayMode=Left`、`OpenPaneLength=216`、`IsPaneToggleButtonVisible=False`、
  `IsSettingsVisible=False`、`Background=Transparent`，`AutomationId` 沿用 `lstSettingsSections`）；
  页头移进 `NavigationView.PaneHeader`；六个 `NavigationViewItem` 用 `Tag="0".."5"` 携带序号，
  图标依次 `Font / Play / Folder / Globe / Save / Setting`。
  code-behind 的 `OnSectionChanged` 签名改为 `(NavigationView, NavigationViewSelectionChangedEventArgs)`，
  新增 `CurrentSectionIndex()` 解析 `Tag`。
- 量测（1800×1200，150% DPI，`.scratch/rework/navgap-settings.png` / `navgap-probe2.py`）：
  指示条 `x=374–378`（5 px ≈ 3 DIP）→ 图标块 `x=392–415`（24 px = 16 DIP，间隔 14 px ≈ 9 DIP）
  → 再空 24 px（16 DIP）→ 文字首列 `x=440`。**条右缘到文字 62 px（≈41 DIP）**；
  改前（用户截图 358×474）条 `x=25–30`、文字 `x=43`，仅 12 px（≈8 DIP）。
- UIA 计数（`pwsh .scratch/rework/verify-settings2.ps1`）：appearance 46 / launch 39 / paths 46 /
  online 39 / cache 44 / advanced 38。比 §「验证记录」的旧基线各 +4，来自 `NavigationView` 新增的
  `PaneRoot` / `PaneTitleTextBlock` / `PART_TitleText` / `PART_SubtitleText` 节点；
  `online` 的期望表仍是迁移前的 IA（6 个「数据 / Mod 缩略图」控件已随 `18cab88` 迁入「缓存」节），
  脚本期望表需要并发会话同步。
- `cmd /c build.cmd`：0 警告 0 错误、16 个检查类、失败 0、断言失败 0。
- 备注：`1f5f23f` 提交的 `SettingsPage.xaml.cs` 已经是 `NavigationView` 版，而当时的 XAML 还是 `ListView`，
  两者不配对（HEAD 编译不过）；本次 XAML 提交补齐了这个缺口。

