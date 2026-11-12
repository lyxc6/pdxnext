# 五个控件的改动规格（InfoBadge / NumberBox / MenuFlyout / SelectorBar / TreeView）

配合 `docs/CONTROLS-UPGRADE-REPORT.md` 使用：报告给判定，本文只给「具体改哪一行、加哪个属性」。行号取自 `feat/1.3` 当前工作树。

## 1. InfoBadge（左栏计数徽标）

**现状证据**：`src/PdxLauncher.App/MainWindow.xaml:174–303` 是 NavigationView；`navLibrary` 项 `:257–264`（AutomationId `:258`、Content 绑 `L.ShellNavLibrary` `:259`、`Tag="library"` `:260`），Footer 项 `:267–292`。全文无 InfoBadge。`MainWindow.xaml.cs` 只公开 `L`（`:99`）与 `ConfirmOverlayHost`（`:105`），`ShellViewModel` 是私有字段（`:49`），XAML 拿不到计数。`src/PdxLauncher.App/ViewModels/ShellViewModel.cs:44` 有 `Games`，但 `ReplaceGames`（`:115–127`）只通知 `HasNoGames/SubtitleText/CanOpenCurrentGame`，不通知数量。

**用户视角**：打开启动器，左栏「游戏库」右边常驻一个数字。Before：要点进去才知道装了 2 个游戏；After：一眼看到 2。

**改动清单**：
- `ShellViewModel.cs` 新增 `public int GamesCount => Games.Count;`，并在 `ReplaceGames` 里补 `OnPropertyChanged(nameof(GamesCount))`。
- `MainWindow.xaml.cs:105` 附近加 `public ShellViewModel ViewModel { get; }`（构造 `:117` 里赋值）。
- `MainWindow.xaml:257–264` 的 `NavigationViewItem` 里加 `<NavigationViewItem.InfoBadge>`，内含 `<InfoBadge Value="{x:Bind ViewModel.GamesCount, Mode=OneWay}" Visibility="{x:Bind ...}"/>`（`GamesCount==0` 时隐藏）。
- Core：无。

**i18n**：数字不需键；读屏要有名字 → 新增 `shell.badgeLibraryName`（`"游戏库，共 {0} 款游戏"`），在 VM 拼成 `LibraryBadgeName` 后绑 `AutomationProperties.Name`（带占位符不能裸绑，见 `tests/PdxLauncher.Checks/LocalizationBindingChecks.cs:55–59`）。

**自检面**：会碰到 `MainWindow.xaml` 的既有检查类有 4 个——`SavesPageChecks.cs:76/:389–417`、`AchievementsPageChecks.cs:47/:158–167`、`DiagnosticsPageChecks.cs:55–56`、`ConfirmOverlayChecks.cs:166–178`，它们只断言文件存在与对应 `Tag` 菜单项存在，加徽标不影响，但别动 `Tag`。新增 `ShellNavBadgeChecks`，断言同一 `NavigationViewItem` 段内出现 `InfoBadge` 与 `GamesCount`；另受 `LocalizationBindingChecks` 约束。SpacingChecks：InfoBadge 不设 Padding/Margin；若加必须 4 倍数（8/12）。AutomationId 建议 `badgeNavLibrary`，沿用 `navXxx` 命名（`:258`、`:269`）。

**风险**：徽标纯视觉，UI Automation 读不到数值，只能靠 Name；`ReplaceGames` 每次重扫都会连发通知，注意别在循环里逐个发。

## 2. NumberBox（行内序号步进）

**现状证据**：`src/PdxLauncher.App/Views/ModsPage.xaml:70–80` 是编辑态 `TextBox x:Name="PositionEditBox"`（`MaxWidth="40"`、`Padding="4,0"`、`MaxLength="4"`、`InputScope="Number"`、`KeyDown`、`LostFocus`），与 `:53–69` 的显示态按钮按 `IsEditingPosition` 互斥。文本解析在 `src/PdxLauncher.App/Services/PositionInput.cs:23`；提交在 `src/PdxLauncher.App/ViewModels/ModsViewModel.cs:984`。Code-behind 靠 `FindDescendant<TextBox>` 找控件（`ModsPage.xaml.cs:720`），焦点写入 `box.Text`（`:690`）。

**用户视角**：点序号出现带上下箭头的数字框，点一下步进 1 位。Before：手敲数字+回车；After：点箭头即换位。

**改动清单**：
- `ModsPage.xaml:70–80` 换成 `NumberBox`：`Minimum="1"`、`Maximum="{x:Bind TotalCount, Mode=OneWay}"`、`SmallChange="1"`、`LargeChange="10"`、`SpinButtonPlacementMode="Compact"`、`ValidationMode="InvalidInputOverwritten"`、`Width="64"`、`Padding="8,0"`，保留 `KeyDown/LostFocus` 与两个 AutomationId 绑定。
- `ModsPage.xaml.cs`：`FindPositionEditor`（`:720`）泛型改 `NumberBox`；`FocusPositionEditor`（`:690`）改写 `Value`；`OnPositionEditKeyDown`（`:746–787`）改读 `Value` 后 `ToString` 再传 `TryMoveModToPosition`，Invalid 仍回退。
- VM：`Position`（`ModRowViewModel.cs:185`）、`TotalCount`（`:202`）已有，无新增。Core：无；`PositionInput.TryParse` 保留作最后兜底。

**i18n**：复用 `mods.row.positionEditName`（`:64`/`:80` 已绑），新增 0 键；不要给 NumberBox 加 `Header/Description`，否则要新键。

**自检面**：`PositionInputChecks` 仍测文本层，不破；`ModsPageLayoutChecks` 的 16 个官方 id（`:35–53`）不含此项，安全；建议在 `ModsPageLayoutChecks` 追加「`PlaysetModRowTemplate` 段内同时出现 `NumberBox` 与 `modPositionEdit_`」。SpacingChecks：`Padding="8,0"` 合法，`Width` 不在检查范围。

**风险**：① `FindDescendant<TextBox>` 会命中 NumberBox 模板内部的 TextBox，写 `Text` 不同步 `Value`，表现为「框架没反应」；② NumberBox 无 `MaxLength`，超长粘贴靠 ValidationMode 归零，而 `TryParse` 的溢出分支从此走不到；③ Enter 是否被 NumberBox 先吃掉（`e.Handled`）需实机确认——**待确认**；④ host:port 类复合串不适用（代理设置保持 TextBox）。

## 3. MenuFlyout（mod 行右键菜单）

**现状证据**：`Views/*.xaml` 全文无 `ContextFlyout|MenuFlyout|RightTapped`（0 命中）。`ModsPage.xaml:38–47` 行模板根 Grid 只挂按钮：开关 `:149–155`、上移/下移 `:158–171`、移除 `:174–182`，无右键入口。列表 `SelectionMode="None"`（`:431`、`:634`）。打开文件夹的现成实现只在 `ModLibraryPage.xaml.cs:443–469`（`/select,` 定位压缩包）与 `:525` 的 `TryStartShell`；`ViewModels` 目录 0 处调用 `ShellLauncher`。

**用户视角**：右键任意 mod 行，出现「打开本地文件夹 / 在 Steam 中打开 / 复制注册 ID」。Before：先去 Mod 库页找到同一个 mod 再点；After：原地右键。

**改动清单**：
- `ModsPage.xaml:39` 的根 Grid 加 `Grid.ContextFlyout`，MenuFlyout 直接写在模板里（不要放资源字典，否则 DataContext 断链）。菜单项 `IsEnabled` 绑 `HasLocalFiles`/`CanMove`。
- `ModsPage.xaml.cs` 新增 3 个 Click handler，explorer 调 `ShellLauncher.TryStart`（`Services/ShellLauncher.cs:47`），复制走 `Windows.ApplicationModel.DataTransfer.Clipboard`（现成写法见 `Services/CrashReporter.cs:158`）。
- `ModRowViewModel.cs` 新增 `HasLocalFiles`（由 `DirPath:76`/`ArchivePath:78` 判空）与 3 个 `L.*` 文案属性；`ModsViewModel` 新增 `public void ReportClipboardCopied(ModRowViewModel row)`——`StatusMessage` 是私有 setter（`ModsViewModel.cs:170–173`），code-behind 改不了。
- Core：无。

**i18n**：`mods.rowMenu.openFolder / locateArchive / openSteam / copyId`（每段小写开头，符合 `src/PdxLauncher.App/Strings/README.md:19`），状态 `mods.status.openedFolder / copiedId / noLocalFiles`；`common.copyPath` 已存在（资源名 `CommonCopyPath`）可复用。

**自检面**：现有 16+11 个 id 断言不受影响；新增 `MenuFlyoutChecks` 或并入 `ModsPageLayoutChecks`，断言 `:38` 模板段含 `ContextFlyout`。SpacingChecks：菜单项不设 Padding（若设必须 4 倍数）。AutomationId 沿用 `mod<动作>_<ModId:N>`（`ModRowViewModel.cs:158–173`），如 `modOpenFolder_`。

**风险**：① Placement 会被忽略（报告已判），只做 ContextFlyout；② `SelectionMode="None"` 使行不可聚焦，`Shift+F10` 键盘右键大概率不触发——**待确认**，需 UI 测试覆盖；③ 菜单带动 mod 名会溢出，名字只放状态栏；④ 移除是即时自动保存、无二次确认（`:173` 注释 + `:175` Command），菜单里不要放「移除」，否则误触即删。

## 4. SelectorBar（工坊详情页分区跳转）

**现状证据**：`Views/WorkshopDetailPage.xaml:17–22` 内层 Grid 只有两行（Auto 页头 / * 内容）；`:100` 的 ScrollViewer **没有 x:Name**，代码后置无法 `ChangeView`；内容 `:104` 是 `StackPanel Spacing="12"`，8 段靠注释标号（`:106`、`:254`、`:385`、`:439`、`:499`、`:557`、`:610`、`:647`），各段 Border 均无 x:Name。

**用户视角**：详情页顶部出现「图集/信息/状态/描述/依赖/更新日志/外链/讨论」横条，点哪段滚到哪。Before：一直往下滑找依赖；After：一次点击直达。

**改动清单**：
- `:19–22` 加第三行 `Height="Auto"`；新插 `<SelectorBar x:Name="DetailSections" Grid.Row="1" SelectionChanged="OnSectionSelected" AutomationProperties.AutomationId="barWorkshopDetailSections">`，8 个 `SelectorBarItem Tag="gallery|info|status|description|dependencies|changelog|links|comments"`。
- `:100` ScrollViewer 改 `Grid.Row="2"` 并加 `x:Name="DetailScroll"`；`:104` StackPanel 加 `x:Name="DetailContent"`；八个分区 Border 加 `x:Name`（如依赖卡在 `:504`）。
- `WorkshopDetailPage.xaml.cs` 新增 `OnSectionSelected`：按 Tag 取控件，`DetailScroll.ChangeView(null, target.TransformToVisual(DetailContent).TransformPoint(default).Y, null)`。
- VM / Core：无（纯视图行为）。

**i18n**：分区标题可复用 `workshop.detailInfo / detailDescription / detailDependencies / detailChangelog / detailLinks / detailComments`，但 `workshop.detailScreenshots` 是「截图 {0} 张」带占位符、不能当标签 → 新增 `workshop.sections.gallery`（"图集"）与 `workshop.sections.status`（"状态"）。

**自检面**：工坊详情页没有页面级检查类（仅 `ConfirmOverlayChecks.cs:24`、`:57` 引用其 code-behind）→ 新增 `WorkshopDetailSectionsChecks`，断言 8 个 Tag 与 `x:Name` 齐全。SpacingChecks：SelectorBar 若设 Margin/Padding 取 4 倍数。AutomationId 用 `barWorkshopDetailSections`（沿用现有 `bar/lst/btn` 类型前缀 `:515`、`:707`）。

**风险**：① SelectorBar 必须在 ScrollViewer 之外（Row 1），否则随内容滚走；② 手动滚动不会回改选中项（需 scroll-spy + 节流），一期只做「点选跳转」；③ 目标分区不可见时（如依赖卡 `:504` 绑 `HasDependencies`）`ChangeView` 落点为 0，需顺延到下一可见分区；④ 8 项在 1040 宽下是否溢出，**待实测**。

## 5. TreeView（依赖树）

**现状证据**：`WorkshopDetailPage.xaml:515` 是平铺 `ItemsControl`，行模板 `:518–521`；`WorkshopDependencyViewModel.cs:9–65` 只有 `Name/DetailText/StateText/AutomationId`，无 Children；`src/PdxLauncher.Core/Mods/WorkshopDependencyMatcher.cs:25` 的 record 仅 `(Requirement, State, InstalledModId)`，`:49` 的 `Match` 只做一层匹配、无递归。唯一带层级感的现成数据是 `src/PdxLauncher.Core/Mods/Sorting/ConflictGraph.cs:25`（冲突对 + 样本路径）。

**用户视角**：依赖区变成可折叠的树：顶层分「需处理」「已就绪」，展开一条能看到冲突路径与要求版本。Before：一列平铺要自己数几个没装；After：先看分组计数再展开细项。

**改动清单**：
- `WorkshopDetailPage.xaml:515` 换 `<TreeView ItemsSource="{x:Bind ViewModel.DependencyTree, Mode=OneWay}" ...>`，**保留** `AutomationId="lstWorkshopDependencies"`（`:515`）以免断既有 UI 测试。
- VM：新增 `WorkshopDependencyNodeViewModel : LocalizedObservableObject`（`Name/StateText/Children/IsExpanded/AutomationId="workshopDependencyNode_"+path`），或在 `WorkshopDependencyViewModel` 上加 `Children`。
- Core：**要动**——加一层"分组"不需要 Core 改动；要真递归就得扩 `Match` 并做循环引用防护与缓存。建议一期只做分组，不做递归。

**i18n**：`workshop.dependencies.groupAttention`（"需处理 {0} 项"）、`groupReady`、`groupUnknown`；叶子沿用 `workshop.dependencyEnabled/Disabled/Missing/Unknown`。

**自检面**：无既有检查类 → 新增 `WorkshopDependenciesTreeChecks`，断言 `TreeView`、三个分组键、`workshopDependencyNode_` 前缀存在。SpacingChecks：`TreeViewItem` 若设 Padding 取 4 倍数（建议 `8,4`）。

**风险**：① 没有现成的层级数据源（报告已判），递归方案成本远超其余四项；② TreeView 的 `ItemTemplate` 根必须是 `TreeViewItem`，写错会静默空白；③ 折叠态下 UIA 看不到子项，冒烟测试要先展开；④ 新旧控件不能共用同一个 AutomationId。

## 收尾

新增检查类后同步 `docs/DEVELOPMENT.md:567`（App 155 个 .cs）、`:574`（108 个检查类）、`:598`、`:608` 四处数字；`tests/PdxLauncher.Checks/Program.cs` 靠反射发现，无需注册。全部改动遵守 4px 网格与 `x:Bind Mode=OneWay`。
