# 设置页改版说明（2026-10-27）

> **2026-10-28 追加**：Paradox 账号分节（曾经的 `navSettingsAccount`，`Tag=4`）已从设置页移出，改成左栏底部独立「账号」页（`src/PdxLauncher.App/Views/AccountPage.xaml`）。设置页当天回到 **6 个分节**（`Tag="0".."5"`），`CacheSectionIndex` 回到 `4`；**此后 2026-10-30 新增「翻译」（`Tag=6`，`navSettingsTranslation`）、2026-11-03 新增「关于与更新」（`Tag=7`，`navSettingsAbout`），现为 8 个分节（`Tag="0".."7"`），见文末「2026-11-06 勘误」。** 文案键 `settings.nav.account` / `settings.section.account` / 全部 `settings.account.*` 已删，迁到 `src/PdxLauncher.App/Strings/*/Resources.resw`（`account.*`）。详见 `docs/HANDOFF.md` §0.26。

## 目标与结果

把原来「一整列 10 张卡片、每张卡里 160/`*` 左标签右控件」的设置页，改成 Windows 11 设置风格：

- 左侧 216 DIP 子分类导航（外观 / 启动 / 路径 / 联网 / 缓存 / 高级 / 翻译 / 关于与更新），右侧只显示当前分类。
- 每行一个 `SettingsCard`（标题 + 可选说明 + 右侧控件），行卡统一描边、圆角、56px 最小高度。
- 状态栏移出滚动区、固定在页面底部；无文案时自动收起。
- 页头（标题 + 当前游戏名）与导航同列显示。

## 结构

```
Page
└─ Grid Padding=20 RowSpacing=12 ColumnSpacing=16（两行 * / Auto、两列 216 / *）
   ├─ 列布局：Col0 216（分节导航）/ Col1 *（滚动内容），列间距 16 DIP
   │  ├─ Col0 NavigationView x:Name=navSettingsSections（AutomationId=lstSettingsSections，
   │  │   PaneDisplayMode=Left、OpenPaneLength=216、IsPaneToggleButtonVisible=False、
   │  │   IsSettingsVisible=False、Background=Transparent）
   │  │   ├─ PaneHeader：页头（标题 + 当前游戏名）
   │  │   └─ MenuItems：八个 NavigationViewItem（Tag=0..7，各带 SymbolIcon），
   │  │       SelectionChanged=OnSectionChanged，AutomationId 见下
   │  └─ Col1 ScrollViewer（VerticalScrollBarVisibility=Visible 常显、
   │        HorizontalScrollBarVisibility=Disabled、HorizontalContentAlignment=Stretch）
   │     └─ StackPanel Spacing=12 Margin=0,0,16,0（右侧 16 DIP 是滚动条通道）
   │        ├─ StackPanel x:Name=secAppearance
   │        ├─ StackPanel x:Name=secLaunch      Visibility=Collapsed
   │        ├─ StackPanel x:Name=secPaths       Visibility=Collapsed
   │        ├─ StackPanel x:Name=secOnline      Visibility=Collapsed
   │        ├─ StackPanel x:Name=secCache       Visibility=Collapsed
   │        ├─ StackPanel x:Name=secAdvanced    Visibility=Collapsed
   │        ├─ StackPanel x:Name=secTranslation Visibility=Collapsed
   │        └─ StackPanel x:Name=secAbout       Visibility=Collapsed
   └─ Row1 Border（状态栏，txtSettingsStatus，同样 16 DIP 右侧通道以对齐右缘）
```

- 自绘控件：`src/PdxLauncher.App/Controls/SettingsCard.xaml(.cs)`，三个依赖属性
  `Header`（string）、`Description`（string，空则隐藏）、`ActionContent`（object，右侧控件）。
  边框用 `PdxCardBackgroundBrush` / `PdxCardStrokeBrush`，与主题（浅色/深色/高对比）联动。
- **滚动条通道**：WinUI 3 的 Fluent 滚动条是浮层指示器，贴在 `ScrollViewer` 右边缘绘制、
  不占内容宽度。初版内容一路撑到右边缘，轨道和滑块就压在卡片的 1 px 描边与 8 px 圆角上
  （悬停/滚动时表现为「内容和滚动条重叠」）。修法是滚动内容与状态栏各留 16 DIP 右侧通道
  （原为 12 DIP 滚动条通道 + 2 DIP 余量；2026-11-04 对齐 4px 网格时由 14 DIP 调为 16，见文末勘误），
  滚动条改画在通道里。注意只能给**内容**留白：
  给 `ScrollViewer` 本身加 Margin 只会把轨道跟着卡片一起往里挪，问题不变。
- **分节导航控件**：左侧八分类用官方 `NavigationView`（与主窗口左栏同一控件、同 216 宽），
  不是自绘。选中指示条（3 DIP）左侧按 WinUI 规范留的是**图标位**：条目带 `SymbolIcon`
  后依次是「条 → 图标 → 文字」。初版用纯文字 `ListView`，文字直接落在条目内边距 8 DIP 处，
  紧贴指示条（视觉上「条和字太近」），换控件 + 加图标后条右缘到文字 ≈ 41 DIP。
  用 `ListView` 也能通过加大 `Padding` 拉开，但那样条与字之间仍是空的，不符合导航规范。
- 分节切换在 `SettingsPage.xaml.cs`：
  `OnSectionChanged(NavigationView, NavigationViewSelectionChangedEventArgs)` →
  `CurrentSectionIndex()`（解析 `NavigationViewItem.Tag`，"0"–"7"）→ `ShowSection(index, refreshCache)`；
  切到缓存节时后台刷新缓存体积（`CacheSectionIndex = 4`）。
- 新增 AutomationId：`lstSettingsSections`（沿用旧名，挂在 `NavigationView` 上，未改成 navSettingsSections）、
  `navSettingsAppearance`、`navSettingsLaunch`、
  `navSettingsPaths`、`navSettingsOnline`、`navSettingsCache`、`navSettingsAdvanced`；
  两个后加分节再补 `navSettingsTranslation`（Tag=6）与 `navSettingsAbout`（Tag=7）。
  原有 39 个 AutomationId 全部保留原值。

### 高级节：补丁区（2026-10-28）
`secAdvanced` 内部顺序改为：补丁标题 → 「补丁状态」卡（`txtPatchStatus` + `txtPatchDetail`）→
「一键打补丁」卡（`btnApplyPatch` / `btnRemovePatch` / `btnOpenPatchAssets` 横排）→ 补丁说明 →
危险区标题 → 原有危险区（签名开关 / 重新生成 / 最近签名）。4 个新 AutomationId 与原 34 个
advanced 节 AutomationId 并存；补丁区只在选中游戏后可操作（`CanApplyPatch` / `CanRemovePatch`
由 `GamePatchReport` 决定，`IsBusy` 期间全部置灰），未选游戏时状态文案为
`settings.patch.state.noGame`。状态文案键在 `ViewModel.RefreshPatchState()` 里按 `GamePatchStatus`
选 `settings.patch.state.*`，语言切换经 `Relocalize()` 重算。

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

- 导航文案键现为 8 个：`settings.nav.{appearance,launch,paths,online,cache,advanced}`
  （改版时的 6 个）+ `settings.nav.translation` / `settings.nav.about`（2026-10-30 / 2026-11-03 两个新分节）；
  危险区新增 `settings.danger.regenerateTitle`。`src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` 同步维护，
  源生成器在编译期读取 `Strings/*/Resources.resw` 生成 `Localizer` 属性（改版当时 499 个键；2026-11-06 已增至 1424 个）。
- 9 段长说明（主题 / 代理 / 界面 / DLC / 公告 / 程序数据 / 缩略图 / 缓存 / 危险）各压成一句话。
  改版前的完整长文案留在 `backup-settings-0350/settings.zh-CN.json`、`settings.en-US.json`（仓库根目录）。

## 验证记录

- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误。
- `bash .scratch/rework/build-checks.sh`：15 个检查类、断言失败 0。
- `python src/PdxLauncher.Localization.Generator --check`：499 个键合法。
- UI 自动化（`powershell -File scripts/verify-settings-sections.ps1`）：按当前脚本为八个分类逐个切换
  （2026-10-27 改版时只覆盖 6 个；`navSettingsTranslation` / `navSettingsAbout` 后加，脚本的 want 列表已同步），
  各节期望的 AutomationId 全部命中（每节 missing 全空；count 含公共导航与状态栏、且随缓存统计/进度环浮动，不作为断言），
  截图见 `.scratch/rework/settings-{appearance,launch,paths,online,cache,advanced}.png`（翻译 / 关于两节为后加，
  暂无同名截图），另附缓存节滚动到底的 `settings-cache-bottom.png`。
- 症状回归（`powershell -File scripts/verify-settings-regression.ps1`）：切换「背景显示方式」与开关「窗口置顶」后，
  其它下拉与开关的值保持不变；被操作控件的位置与尺寸在前后完全一致（`1323,940,492x60`），
  即不再出现整页跳动。

---

## 2026-10-27 追加：缓存接管「数据 / Mod 缩略图」，并做一轮去重

用户诉求原话：「联网页面的数据和缩略图应该在缓存里；重新审视下设置中的代码，降低代码重复度」。

- 信息架构：`联网` 分类只剩「网络代理」三卡；`数据`（DLC 简介开关 + 简介缓存天数 + 公告开关）与
  `Mod 缩略图`（联网开关 + 图片保留天数 + 缓存目录卡）整块移到 `缓存` 分类，顺序为
  缓存标题 → 合计卡（刷新大小 / 清除全部）→ 四行缓存清单 → 数据 → Mod 缩略图 → 缓存说明。
  AutomationId 全部保留，`scripts/verify-settings-sections.ps1` 的 want 列表随之调整。
- 代码去重（行为保持，逐步 commit + build）：
  - `SettingsViewModel`：新增 `ApplyFlag` / `ApplyOption`（`[CallerMemberName]`），6 个开关 + 6 个下拉的 setter 收敛；
    `PersistSettings()` 改为 `=> SaveSettings()`；四个 `Refresh*` 合并为 `RefreshOptionSelections()`；
    主题来源只留一处 `ThemeSourceKey`；`CacheRowViewModel` 构造时注入清除委托，删掉 `ClearCacheCore` 的 Key 分派。
  - 保留天数目录：`ModThumbnailRetentionCatalog` + `DlcDescriptionRetentionCatalog` 合并为 `RetentionDaysCatalog`
    （`ModThumbnails` 默认 30 天、`DlcDescriptions` 默认 7 天两个静态实例）。
  - XAML：只读值卡改由新控件 `Controls/SettingsValueCard` 承载（Header / Description / Value /
    ValueAutomationId / ValueAutomationName / MaxValueWidth / ShowToolTip），hint 与二级小节标题改走
    `SettingsHintTextStyle` / `SettingsSubsectionTitleStyle`。
  - `SettingsPage.xaml.cs`：`ConfirmAsync`（统一确认框）、`RunGuardedAsync`（事件护栏）、
    `OpenPath`（合并 OpenDirectory / OpenExplorer）三处样板收敛。
- 语言切换（`.scratch/rework/verify-lang2.ps1`）：简中 ↔ English 双向切换，导航与卡片文案、状态栏提示同步更新。

### 滚动条通道（2026-10-27 14:50）

> 注：本节记录的是 14 DIP 通道时代的取证。2026-11-04 对齐 WinUI 4px 网格时通道已改为 **16 DIP**（见文末勘误），
> 下面的像素读数与「14 DIP 是常数」等表述保留原值，不再代表现状。

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
- `cmd /c build.cmd`：16 个检查类、失败 0、断言失败 0；`python src/PdxLauncher.Localization.Generator --check`：499 个键合法。

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
- 顺手补回列间距：换控件时把原来的 `Row0 Grid ColumnSpacing="16"` 合并进外层 `Grid`，`ColumnSpacing`
  一度漏掉，导致导航面板与卡片只见一条描边、几乎贴在一起。现在外层 `Grid` 带 `ColumnSpacing="16"`，
  卡片整体右移 25 px（≈16 DIP）并收窄同等宽度：卡片填充左缘 `x=693 → 718`，右缘 `x=1735` 未变
  （滚动条通道量值不受影响：描边 `1736–1737`、滚动条 `1751–1752`，间隙仍是 12 px）。
  导航面板右缘到卡片左缘约 27 px（≈18 DIP）。`verify-settings3.ps1` 三次读数仍然完全一致
  （本次窗口位置下 `1492,1130,492x60`），说明「点按钮不跳动」这条没有回退。


### 分节 / 窗口位置持久化（2026-10-28）

用户反馈「设置状态没保存」：每次进设置页都落回「外观」，窗口位置也不记。

- `AppSettings` 新增 `lastSettingsSection`（0 起，null/越界 → 0）、`windowX` / `windowY`（物理像素，-1 = 从没记过）。
- `SettingsPage.RestoreSection()` 恢复上次停留的分节，并把 `navSettingsSections.SelectedItem` 同步过去
  （避免「内容在缓存节、高亮在外观节」）；`ShowSection()` 末尾把当前节写回。
  **坑**：XAML 里 `navSettingsAppearance` 曾写死 `IsSelected="True"`，会在 `OnLoaded` 之前触发一次
  `SelectionChanged → ShowSection(0)`，把刚恢复的分节覆盖成 0（表现为永远停在外观节）。已删除。
- `MainWindow` 关闭时记录 `AppWindow.Position`；`ApplyWindowPosition()` 在 `ApplyWindowSize()` 之后按
  `DisplayArea.GetFromWindowId(...).WorkArea` 校验「窗口矩形完整落在工作区内」才 `Move`，否则保持默认居中。
  实测关一次后 `windowX/Y=380`，重启 `GetWindowRect` 回到 `380,380`。
- `SettingsViewModel.SaveSettings()` 由延迟写（300ms）改为立即写：设置文件只有几百字节，
  换确定性，避免强杀进程丢掉最后 300ms 的改动。
- 自检 `SettingsChecks`（round-trip 含新增字段、缺文件默认、坏 JSON 回退、延迟写 `Flush`）已加入，
  当前 27 个检查类 / 失败 0。
- 注意：`NavigationView` 当前是 8 个分类（Tag=0..7；账号已迁到左栏独立页，Tag=6「翻译」与 Tag=7「关于与更新」为 2026-10-30 / 2026-11-03 追加），`CacheSectionIndex = 4`（「缓存」仍是 Tag=4，未随新增分节改变）。

---

## 2026-11-06 勘误（文档与代码对齐）

以 `feat/1.3` HEAD `ac7225e` 为准复核，本轮只改文档，代码未动：

- **分节数 6 → 8**：`SettingsPage.xaml` 的 `NavigationView.MenuItems` 现有 8 个 `NavigationViewItem`，`Tag="0".."7"`，
  依次为 外观 / 启动 / 路径 / 联网 / 缓存 / 高级 / 翻译 / 关于与更新（第 65–120 行；`navSettingsTranslation` 在第 107 行、
  `navSettingsAbout` 在第 114 行）。`SettingsPage.xaml.cs` 的 `ShowSection` 数组也是 8 项（第 169 行），
  `CacheSectionIndex` 仍为 `4`。两个新分节的来源：翻译见 `docs/HANDOFF.md` §0.56（2026-10-30），
  关于与更新见 §0.74.3（2026-11-03）。
- **滚动条通道 14 DIP → 16 DIP**：滚动内容 `StackPanel` 的 `Margin="0,0,16,0"`（第 135 行）与状态栏 `Border`
  的 `Margin="0,0,16,0"`（第 1024 行）。改动来自 2026-11-04 的 `7e95e57`「间距全量对齐 WinUI 的 4px 网格」
  （见 `docs/HANDOFF.md` §0.82）：14 不是 4 的倍数，归一化为 16。
- **代码注释仍写 14 DIP**：`SettingsPage.xaml` 第 131 行与第 1021 行的注释未随数值更新；
  按本轮「只改 docs」的约束未动代码，待代码侧顺手修正。
- 本文历史小节（`2026-10-27 14:50` 的 14 DIP 量测、`2026-10-27 15:10` 的六个 `NavigationViewItem`、
  「验证记录」里的六分类截图）按写作时的事实保留，不篡改。
