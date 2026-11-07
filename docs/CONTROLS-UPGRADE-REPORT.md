# WinUI 控件补全可行性报告

> 对象：8 个候选控件（MenuFlyout / TableView / Expander / SelectorBar / TreeView / NumberBox / InfoBadge / AnnotatedScrollBar）
> 基线：`feat/1.3` @ `ac7225e`（2026-11-06）
> 性质：只做版本核查、约束核对与落点评估，**本报告不含任何代码改动**。

---

## 0. 结论（先看这一段）

**8 个不能全部补上。** 卡点只有一个控件，但它是硬卡点：

| 控件 | 首版可用的 WinAppSDK | 本项目 2.5.1 是否可用 | 判定 |
| --- | --- | --- | --- |
| MenuFlyout | 1.0（WinUI 2.0 时代起随框架提供） | ✅ | **可做**，仅限右键菜单 |
| TableView | **2.5.4-experimental** | ❌ 完全不存在 | **当前不能做**（需升 SDK + 走实验通道） |
| Expander | 1.0（WinUI 2.6 引入） | ✅ | 能做，**建议不做**（项目两次移除） |
| SelectorBar | **1.5** | ✅ | 可做，但只值得用在一处 |
| TreeView | 1.0（WinUI 2.2 引入） | ✅ | 可做，**需先补数据模型** |
| NumberBox | 1.0（WinUI 2.3 引入） | ✅ | 可做，只有 1 个合理落点 |
| InfoBadge | 1.0（WinUI 2.7 引入） | ✅ | 可做，最小一件 |
| AnnotatedScrollBar | **1.4**（不是 1.6） | ✅ | 可做，但**要换滚动容器**，改动面最大 |

三句话总结：

1. **版本门槛只有 TableView 一处**，且不在稳定通道上（`2.5.4-experimental`，官方原文「不要在正式版应用里用」）。要补它，必须把项目从 2.5.1 稳定版整体抬到实验版——这比「加一个控件」大得多，且与项目「构建 0 警告」的硬约束直接冲突（实验 API 会产出 `CS8305` / `WMC1501`）。
2. **用户给的两个版本前提需要修正**：`SelectorBar 1.5` 正确；`AnnotatedScrollBar 1.6` 错误——它是 **WinAppSDK 1.4** 就有的控件，2.5.1 里早已可用（本地 winmd 实测 19 处命中 + `generic.xaml:22791` 有完整样式）。
3. **真正的成本不在版本，在数据面和存量工程约束**：TableView 想要的「时间/级别/来源/正文」四列里，「来源」这一列在日志里根本不存在；TreeView 想要的父子依赖，Core 只给平铺 ID 列表；AnnotatedScrollBar 想要的滚动接线，现有 8 个页面全是 `ListView` 内建 `ScrollViewer`，没有可接的 `IScrollController`。

**建议：做 6 个（InfoBadge、NumberBox、MenuFlyout、SelectorBar、TreeView、AnnotatedScrollBar），缓 1 个（TableView，等稳定版），否 1 个（Expander）。**

---

## 1. 前置条件 A：项目实际用的 Windows App SDK

### 1.1 版本实况（证据链）

版本只由一处决定——**仓库里没有中央包管理，也没有锁文件**：

- `Directory.Packages.props`：**不存在**
- `global.json`：**不存在**
- `packages.lock.json`：**不存在**（上述三项已用递归 glob 与实际 `ls` 双重确认）

因此 `src/PdxLauncher.App/PdxLauncher.App.csproj` 是唯一事实源：

| 行 | 内容 | 含义 |
| --- | --- | --- |
| `:4` | `<TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>` | .NET 10 + Windows SDK 26100 |
| `:5` | `<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>` | 最低 Win10 1809 |
| `:9` | `<RuntimeIdentifier>win-x64</RuntimeIdentifier>` | 仅 x64 |
| `:10` | `<UseWinUI>true</UseWinUI>` | WinUI 3 |
| `:11` | `<WinUISDKReferences>false</WinUISDKReferences>` | 走 NuGet 引用，非 SDK 内联引用 |
| `:13` | `<WindowsPackageType>None</WindowsPackageType>` | 非打包（unpackaged） |
| `:14` | `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` | WinAppSDK 自包含 |
| `:15` | `<SelfContained>true</SelfContained>` | .NET 自包含 |
| `:23` | `DefineConstants` 含 `DISABLE_XAML_GENERATED_MAIN` | 自写 `Program.Main`（非模板入口） |
| `:26` | `Microsoft.Windows.SDK.BuildTools` `10.0.28000.2705` | — |
| `:27` | `<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.5.1" />` | **现行版本 = 2.5.1** |
| `:28` | `CommunityToolkit.Mvvm` `8.4.2` | — |

还原后的真实依赖（`obj/project.assets.json`）：`Microsoft.WindowsAppSDK/2.5.1`，其子包为
`Base 2.0.4` / `Foundation 2.3.12` / `InteractiveExperiences 2.1.9` / **`WinUI 2.3.9`** / `DWrite 2.1.0` / `Widgets 2.0.5` / `AI 2.5.5` / `ML 2.1.94` / `Search 2.5.5` / `Runtime [2.5.1]`。

> 关键推论：**控件可用性看的是 `Microsoft.WindowsAppSDK.WinUI` 子包（2.3.9），不是外层的 2.5.1。** 本报告所有「本地实测」都直接扫这个子包的 `Microsoft.UI.Xaml.winmd` 与 `Themes/generic.xaml`。

### 1.2 一个容易被忽略的约束：**全离线还原**

`NuGet.config` 把 `packageSources` 直接 `<clear />`，本机缓存里只有 `microsoft.windowsappsdk 2.5.1` 一条主线（其余均为其子包）。

这意味着：

- 现行构建**完全可离线复现**，版本想漂也漂不了；
- **升级 WinAppSDK 不是改一行版本号**——要新增包源、重新 restore、把 `Base/Foundation/InteractiveExperiences/WinUI` 全套新版本灌进缓存，并同步 `WindowsAppSDKSelfContained` 的运行时载荷；
- 任何一个「顺手升级」都会把「离线可构建」这个隐形资产打掉。

### 1.3 本地实测：8 个控件在 2.5.1 里的真实存在性

扫描方式（可复现）：

```powershell
$pkg = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk.winui\2.3.9"
# 1) 类型是否存在
Select-String -Path "$pkg\metadata\Microsoft.UI.Xaml.winmd" -Pattern 'TableView','AnnotatedScrollBar','SelectorBar','InfoBadge' -AllMatches
# 2) 是否有默认样式（没有样式的控件等于不可用）
Select-String -Path "$pkg\lib\net6.0-windows10.0.17763.0\Microsoft.WinUI\Themes\generic.xaml" -Pattern 'TargetType="controls:TableView"'
```

结果：

| 控件 | winmd 命中 | generic.xaml 样式 | 结论 |
| --- | --- | --- | --- |
| MenuFlyout | 97 | 有 | 可用 |
| TableView | **0** | **0** | **完全不存在** |
| Expander | 21 | 有 | 可用 |
| SelectorBar / SelectorBarItem | 有 | 有 | 可用 |
| TreeView | 81 | 有 | 可用 |
| NumberBox | 18 | 有 | 可用 |
| InfoBadge | 23 | 有（`NavigationViewItem.InfoBadge` 属性见 `Microsoft.WinUI.xml:21490`） | 可用 |
| AnnotatedScrollBar | **19** | 有（`generic.xaml:22791`） | **可用** |

**TableView 的 0 命中是决定性的**：它不是「用法不对」，是编译期就不存在。

### 1.4 逐控件版本门槛与来源

#### ① TableView —— 唯一的硬门槛

- **门槛：Windows App SDK `2.5.4-experimental`**。
- 官方原文（Microsoft Learn《Table view》，页面更新于 2026-10-14）：

  > "The `TableView` control is available starting in Windows App SDK 2.5.4-experimental. The APIs are marked experimental and might change or be removed in a future release. **Don't use them in production apps.**"

- 来源：<https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/table-view>
- 使用它还附赠一串额外成本：
  - 需要 `xmlns:tabular="using:Microsoft.UI.Xaml.Controls.Tabular"`；
  - 必须在 `App.xaml` 的 `XamlControlsResources` **之后**并入 `<TabularControlsResources xmlns="using:Microsoft.UI.Xaml.Controls.Tabular" />`，否则首次布局直接抛 XAML parse exception；
  - 实验 API 会产出 **`CS8305`（C#）与 `WMC1501`（XAML）警告**——项目要求 `0 警告`，等于开箱即违约；
  - 列的 `Binding` **只能用 `{Binding}`，不能用 `{x:Bind}`**——与项目「x:Bind + Mode=OneWay」的既有习惯相反，失去编译期检查；
  - 能力边界：只能单选行、单列排序（表头还不支持键盘排序）、单层分组、不能重排列、无行头、只能冻结前导列、**列不虚拟化（官方给的舒适区约 5–50 列）**、明确「不是 DataGrid 的替代品」。
- 佐证：WinAppSDK 1.5 的 release notes 在「previously planned features」里还写着「Investigations into the table view and ink controls」，说明它直到 1.5 仍在调研阶段。
- **本地实测：2.5.1 中不存在（winmd 与 generic.xaml 双 0）。**

#### ② SelectorBar —— 用户说的 1.5 正确

- **门槛：Windows App SDK 1.5（稳定版）**。
- 1.5 release notes 有独立条目标题「New SelectorBar control」，并注明它「previously known as "SegmentedControl" on our 1.5 roadmap」；1.5.0-preview1 的新 API 清单包含 `SelectorBar`、`SelectorBarItem`、`SelectorBarSelectionChangedEventArgs`，1.5-experimental2 另有 `SelectorBarItemAutomationPeer`。
- 来源：<https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-5>
- 本地：2.5.1 已含类型与样式，**可用**。
- 控制面限制（Learn《Selector bar》，2026-02-28）：只有 `Items` 集合（**没有 `ItemsSource`**）、只有 `SelectedItem`（**没有 `SelectedIndex`**）、有 `SelectionChanged`；官方定位是「轻量、选项少、不为窄窗口重排」。替代方案对照：顶层导航用 NavigationView、可关闭视图用 TabView、分页用 PipsPager、无默认选中用 RadioButtons。

#### ③ AnnotatedScrollBar —— **用户说的 1.6 不成立，实际是 1.4**

- **门槛：Windows App SDK 1.4**。
- 1.4 release notes 把 `AnnotatedScrollBar` 作为新控件介绍：「extends a regular scrollbar's functionality ... a clickable rail with labels that act as markers ... a tooltip when hovering over the clickable rail」；1.4.0-preview2 的新 API 清单里就是 `AnnotatedScrollBar`、`AnnotatedScrollBarLabel`、`AnnotatedScrollBarDetailLabelRequestedEventArgs`、`AnnotatedScrollBarScrollingEventArgs`、`AnnotatedScrollBarScrollingEventKind`。
- 后续两条 bugfix 反向证明它早就存在：1.4.7「Fixed a potential crash on shutdown if an `AnnotatedScrollBar` label update was pending」；1.5「Fixed an issue where `AnnotatedScrollbar` could sometimes crash when quickly scrolling」。
- 来源：<https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4>
- 本地：2.5.1 winmd 19 处命中，`generic.xaml:22791` 有 `<Style TargetType="controls:AnnotatedScrollBar">`；公开成员齐全（`Labels`、`ScrollController`、`SmallChange`、`DetailLabelRequested`、`Scrolling`、`LabelTemplate`、`DetailLabelTemplate`）。
- **结论：版本完全不是问题，问题是接线（见 §3 C8）。**

#### ④ Expander —— WinUI 2.6 引入，1.0 起随框架

- **门槛：Windows App SDK 1.0**（前身是 WinUI 2.6，2021-06）。
- 来源：<https://learn.microsoft.com/en-us/windows/uwp/get-started/winui2/release-notes/winui-2.6>
- 本地：2.5.1 有类型与样式，**可用**。
- 但——**技术可用与项目该用是两回事**，见 §3 C4。

#### ⑤ TreeView / NumberBox / InfoBadge / MenuFlyout —— 门槛都是 1.0，远低于现状

这四个控件都是 UWP/WinUI 2.x 时代的产物（TreeView ≈ WinUI 2.2、NumberBox ≈ WinUI 2.3、InfoBadge ≈ WinUI 2.7、MenuFlyout 更早，属基础 XAML），WinUI 3 / WinAppSDK 1.0 起随框架提供。对 2.5.1 而言**版本门槛为零**。

本报告不对它们的 WinUI 2.x 具体引入版本做过度断言（Learn 的 WinUI 2 release notes 索引：<https://learn.microsoft.com/en-us/windows/apps/winui/winui2/release-notes/>），因为**判定依据是本地实测**：2.5.1 的 winmd 与 `generic.xaml` 里四个控件都有类型与默认样式（命中数见 §1.3），本地能编译就是可用。

### 1.5 若为 TableView 升级 WinAppSDK，风险有多大

| 维度 | 评估 |
| --- | --- |
| 通道 | 2.5.4 只有 **experimental**，无稳定版；官方明示不适合生产 |
| 离线构建 | `NuGet.config` 清空了源，缓存只有 2.5.1 → 必须新增包源/联网还原，破坏「离线可复现」 |
| 编译约束 | 实验 API 产 `CS8305`/`WMC1501` → 撞「0 警告」硬约束；要么 `NoWarn` 掩盖，要么破线 |
| 资源合并 | 必须改 `App.xaml` 并保证 `TabularControlsResources` 在 `XamlControlsResources` 之后，否则运行时崩 |
| 回归面 | `Base/Foundation/InteractiveExperiences/WinUI` 全套换版，波及 4 个自绘控件（ConfirmOverlay / SettingsCard / PdxDropDown / MarkdownView）、110 处 `NavigationViewItem`、自写 `Program.Main` 与手工 XAML 编译路径 |
| 收益 | 只为一个诊断页的日志表格 |

**结论：不值得为 TableView 升级 SDK。** 建议等它进稳定通道（当前无官方时间表），届时单独开升级任务，与控件补齐解耦。

---

## 2. 前置条件 B：项目现有硬约束（以及每个控件会踩哪条）

> **先纠正一个前提：`AGENTS.md` 与 `.editorconfig` 在本仓库并不存在**（已递归 glob 确认）。项目的实际约束分散在 4 份文档 + 2 个自检类里，比单一 AGENTS.md 更硬——因为它们是**机器执行**的。

### 2.1 约束清单

| # | 约束 | 出处 | 执行方式（是否自动） | 8 控件里谁踩 |
| --- | --- | --- | --- | --- |
| B1 | **4px 网格**：Padding / Margin / Spacing / RowSpacing / ColumnSpacing 的**每个分量**都必须是 4 的倍数；CornerRadius 只允许 0/4/8；页面根 Padding 只允许 16/20/24；1px 线用 `Width`/`Height`；BorderThickness 豁免 | `docs/DEVELOPMENT.md` §8（`:796` 起）、`docs/UI-SPACING.md` §3 | **自动**：`tests/PdxLauncher.Checks/SpacingChecks.cs` 正则扫 `src/PdxLauncher.App/**/*.xaml`（先剥注释）与 `*.cs`（`Spacing = 数字`、全数字 `new Thickness`、`new CornerRadius`），并断言 `docs/UI-SPACING.md` 含 `content-basics`/`4epx`/`36epx` | **全部**。尤其：TableView 的列内边距、Expander Header 的 padding、InfoBadge 的 Margin、NumberBox 的 SpinButton 尺寸、AnnotatedScrollBar 自定义模板——写 6/10/2 直接红 |
| B2 | **i18n 走源生成器**：文案只在 `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`（资源名 = 点分键的 PascalCase 标识符），两语言资源名必须一致；`Localizer` 属性与 `Keys` 常量由 `src/PdxLauncher.Localization.Generator` 在编译期生成，**不落盘、禁止手改** | `src/PdxLauncher.App/Strings/README.md` | 自动：生成器在编译期校验键集/占位符；`tests/PdxLauncher.Checks/I18nChecks.cs` 钉「真实换行、无字面 `\n`」 | **全部**。8 个控件的列头/菜单项/分区名/徽章无障碍名，全部要新增键 |
| B3 | **XAML 不许硬编码中文**（含注释外的可见文本） | `docs/AGENT-WORKFLOW.md` 硬约束 + 各页 Checks（如 `DiagnosticsPageChecks` 断言 XAML 无中文） | 自动（页面级） | **全部**。MenuFlyoutItem、SelectorBarItem、Expander Header、TableView 列头最容易写死 |
| B4 | **绑定一律 `Mode=OneWay`**（`x:Bind` 默认 OneTime，切语言不刷新）；行模板 VM 必须继承 `LocalizedObservableObject` 才能 `{x:Bind L.Foo, Mode=OneWay}` | `src/PdxLauncher.App/Strings/README.md` | 人审 + 部分 Checks | InfoBadge 的计数绑定、SelectorBar 选中态、TreeView 节点文案；**TableView 反而强制 `{Binding}`（与 B4 相反），需在报告里显式豁免说明** |
| B5 | **AutomationId 规范**：`AutomationProperties.AutomationId` 用 ASCII、**不翻译**（UIA 测试依赖）；`AutomationProperties.Name` **要翻译** | `src/PdxLauncher.App/Strings/README.md`、`docs/DEVELOPMENT.md` | UIA 测试 + 页面 Checks | 全部；MenuFlyout 的每个 `MenuFlyoutItem`、TableView 本体与列、SelectorBar 每一项、Expander、NumberBox、InfoBadge 都要补 |
| B6 | **自检类数必须递增**：新增检查类后，`docs/DEVELOPMENT.md` 的「108 个检查类」（`:574`）、示例输出「`检查类: 108`」（`:608`）、以及「547 个 .cs + 18 个 .xaml」（`:598`）三处数字都要同步改 | `docs/DEVELOPMENT.md`、`tests/PdxLauncher.Checks/Program.cs`（反射发现 `public static int Run(IFileSystem)`） | **自动**（数字对不上或新类未计数会红） | 每个控件都要求配一个能真失败的检查类（`docs/AGENT-WORKFLOW.md` §6 验收第 5 条）→ 8 个控件 ≈ **4–6 个新检查类 + 三处数字改动** |
| B7 | **构建 0 警告 0 错误**、`-m:1` 单构建 | `docs/AGENT-WORKFLOW.md` §4、`bash tools/build-app.sh -v q` | 自动 | **只踩 TableView**：实验 API 的 `CS8305`/`WMC1501` 直接破线 |
| B8 | **提交/协作**：worktree 在 `.agents\worktrees\<task>`（从**本对话的 sess 分支**起，1.3 后半段已不再直接合 `feat/1.3`）；提交 `type(scope): 中文摘要`；合并 `git merge --no-ff`；不改 `version.props`/`CHANGELOG.md`/`docs/HANDOFF.md`；一波最多 2 个写任务 | `AGENTS.md`、`docs/AGENT-WORKFLOW.md` | 人审 | 影响**批次拆分**（§6）而非单个控件 |
| B9 | **MVVM 边界**：VM 里不出现 `Microsoft.UI.Xaml` 类型 | `docs/DEVELOPMENT.md` + `ShellViewModel` 注释（「只持有纯数据，不出现任何 Microsoft.UI.Xaml 类型」） | 人审 | TreeView（`TreeViewNode` 是 UI 类型 → 必须 `ItemsSource` + 节点 VM）、TableView（行 VM 同）、NumberBox（`double` 与 `int` 的转换放 VM） |
| B10 | `ImplicitUsings` 关闭，统一 `GlobalUsings.cs`；`[ObservableProperty]` 字段式（`NoWarn MVVMTK0045`，禁用 partial property） | `Directory.Build.props` | 自动 | 新增 VM 文件/属性时 |

### 2.2 与「新增控件」直接相关、且容易被漏掉的三条

1. **i18n 是面积成本，不是装饰成本。** 每个可见字符串都要在 zh-CN 与 en-US 两份 JSON 里各写一条，再跑生成器。8 个控件合计约 **25–30 条新键**，分布在 `shell / mods / saves / achievements / diagnostics / settings / workshop / modstudio` 8 个 area 文件。
2. **AutomationId 是硬通货。** 现有诊断页 Checks 断言了「19 个 AutomationId」；任何新增可见控件不补 AutomationId，页面级断言要么失效要么补不齐。MenuFlyout 的菜单项是重灾区（弹出层里的元素没有 AutomationId 时，UIA 测试摸不到）。
3. **自检类不是「顺手加一个文件」。** `Program.cs` 靠反射发现 `public static int Run(IFileSystem)` 的 public static class，新增一个类就得改 `docs/DEVELOPMENT.md` 三处数字（`:574`、`:598`、`:608`），并且断言必须**真的能失败**（`docs/AGENT-WORKFLOW.md` §6）。

---

## 3. 前置条件 C：与自绘控件的冲突（是否重复建设）

### C1 TableView ↔ 自绘日志视图 —— **不是替换控件，是补数据**

现状（`Views/DiagnosticsPage.xaml`）：

- 日志区是独立 `<Grid x:Name="secLog" RowSpacing="8" Margin="0,0,16,0">`（`:328–432`），正文**只有一个只读 `TextBox x:Name="txtDiagLogTail"`**（`:419–431`），绑定 `ViewModel.LogTailText`，`FontFamily="Cascadia Mono, Consolas"`，`MinHeight="160"`，`Padding="12,8"`；
- 交互只有：三档过滤 `rdoDiagLogAll/Warn/Error`（`Tag=All/Warning/Error`）、换行开关 `tglDiagLogWrap`、自动刷新 `tglDiagAutoRefresh`、两个按钮 `btnDiagRefreshLog`/`btnDiagOpenLogFolder`；
- 数据面：`Services/LogTailReader.cs` 只有 `public static string ReadTail(string? path, int maxLines, int maxBytes)`，**返回原始尾部文本**（`DefaultMaxLines=200`、`DefaultMaxBytes=128*1024`），不做任何结构化；
- 日志格式：`src/PdxLauncher.Core/Infrastructure/FileLogger.cs:68-78` 的 `Info/Warn/Error` 最终走 `Write`，拼的是
  ```csharp
  $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}"
  ```
  ——**只有时间 + 级别 + 正文，没有「来源/模块」字段**。

**结论：用户设想的「四列（时间/级别/来源/正文）」里第四列没有数据可填。**
TableView 落地前必须先做：

1. 新增 `LogLineParser`（正则 `^(?<ts>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3}) \[(?<lvl>INFO|WARN|ERROR)\] (?<msg>.*)$`），配独立检查类（半行/残字节/多行异常/`[level]` 缺失等边界）；
2. 或改 `FileLogger` 输出加来源字段（要动 Core，且**必须兼容历史日志文件**，否则用户旧的 `pdxlauncher.log` 解析全空）；
3. 再把 `TextBox` 换成表格。

另外**必须保住**一个历史修复（`docs/HANDOFF.md:74`、§0.84）：日志框原来写死 `Height="340"` 且被塞进唯一的外层 `ScrollViewer`，后来拆成「滚动容器（概览/缓存/网络/启动）」与「独立 Grid（日志）」两个互斥元素，日志框落 `*` 行随窗口伸缩。**TableView 自带滚动，不能反过来再被包进外层 ScrollViewer**，否则这个修复回退。

### C2 SelectorBar ↔ 现有分区导航

- 设置页已经是「左 NavigationView（216 DIP）+ Tag 切换」的 8 分区结构（`Views/SettingsPage.xaml:65–115`，`navSettingsAppearance` 0 … `navSettingsAbout` 7）；
- 诊断页同样结构，5 分区（`Views/DiagnosticsPage.xaml:134/141/148/155/162`，`navDiagOverview` 0 … `navDiagStartup` 4），HANDOFF 记录「结构与设置页同构」；
- 工坊详情 `Views/WorkshopDetailPage.xaml` 是**单页 ScrollViewer 流**（`:100` 主滚动、`:202` 横向滚动、`:789` 结束、`:838` `ZoomMode=Enabled` 画廊），**没有任何分区**；
- Mod 创建页 `Views/ModStudioPage.xaml` 是 Grid 行列布局，只有 `RadioButton GroupName="ModStudioUploadTarget"`（`:421/:425`）。

**结论：SelectorBar 在工坊详情页是「新增信息架构」，不是替换；在 Mod 创建页只是把 2 个 RadioButton 换个皮，收益极低。** 全项目已有 13 个分区（设置 8 + 诊断 5）统一走「左 NavigationView + Tag 切换」，再叠一层 SelectorBar 就是第二套导航范式，一致性成本高于收益。**只建议在工坊详情用一处。**

### C3 NumberBox ↔ 现有 TextBox + 校验

- 全项目唯一的用户可编辑数字：`Views/ModsPage.xaml:70-76` 的 `PositionEditBox`（`MaxWidth="40"`、`InputScope="Number"`，播放集「位置」就地编辑）；
- 代理设置是**单个** `TextBox AutomationProperties.AutomationId="txtProxyUrl"`，内容是 `127.0.0.1:7890` 这种 `host:port` 复合串，配套 `TextChanged`/`LostFocus`/`KeyDown` 三个事件处理器（`OnProxyUrlChanged`/`OnProxyUrlLostFocus`/`OnProxyUrlKeyDown`）；
- `AppSettings`（`src/PdxLauncher.Core/Configuration/AppSettings.cs:75-82`）只有 `string? ProxyMode`、`string? ProxyUrl`，**没有任何数字型设置**；并发度是 `AppServices.cs` / `ImageArtCache.cs` 里的常量。

**结论：**
- 「端口」不是一个独立字段，是地址字符串的一部分，拆成 NumberBox 会同时破坏 placeholder 文案（`settings.proxy.addressPlaceholder` = `127.0.0.1:7890（留空为直连）`）、三个事件处理与整体校验逻辑——**不做**；
- 「并发/超时」当前根本不存在用户可配项，要做是**另一个任务**（Core 加设置字段 + 所有使用点改造 + 设置页 + 迁移），量级「大」；
- NumberBox 现在**只能替换 1 处**（`PositionEditBox`），收益小而干净。

### C4 Expander —— 项目两次引入、两次移除

- `Views/AccountPage.xaml:51` 注释：「不再用 Expander：登录是这一页唯一要做的事，不该再点一次才展开。」
- `docs/HANDOFF.md:3905`：AccountPage 曾把登录表单放进 `Expander AutomationId="expAccountSignIn"`，后由 `d372152 feat(account): 账号页改成单步登录卡，登录表单不再折叠`（3 文件 178+/105-）移除；
- `docs/HANDOFF.md:6347`、`:6471`：ModStudioPage 曾有 `<Expander AutomationId="expModStudioAdvanced">` 高级折叠区（多语言开关/语言行/警告/添加按钮），后整段删除，改成常显的「其他语言（可选）」小区。

**结论：技术零门槛，产品上已被否过两次。** 且设置页已把「高级」拆成独立导航分区，再加折叠等于**双重收纳**（先进分区、再展开一次）。**建议不做**；若一定要做，唯一合理落点是「高级」分区里的危险操作组（清缓存/打开日志目录），且需产品确认理由。

### C5 MenuFlyout —— 有历史坑，但只限「定位」

- `docs/HANDOFF.md:750`、`:2195`、`:2281`：项目曾想用 `DropDownButton + MenuFlyout(Placement=BottomEdgeAlignedLeft)` 做下拉，**MenuFlyout 会忽略 `Placement`**，最终改用普通 `Flyout`。
- `docs/HANDOFF.md:744/797/1985`：全仓 `ComboBox` 已换成自绘 `controls:PdxDropDown`，源码 ComboBox 清零——项目风格偏自绘/受控。

**结论：右键菜单（`ContextFlyout` + `MenuFlyout`）走系统定位，不受 `Placement` 坑影响，可以放心做；但不要把 MenuFlyout 当「带定位的弹出层」用，那条路已经死过。**

### C6 TreeView ↔ 平铺依赖数据

- `src/PdxLauncher.Core/Mods/Sorting/DescriptorDependencyParser.cs`：唯一 API `public static IReadOnlyList<string> ParseSteamIds(string? descriptorText)`，正则 `dependencies\s*=\s*\{([^}]*)\}` + `workshop_item_id\s*=\s*(\d+)`，返回**按序去重的平铺工坊 ID 列表**——没有反向依赖，没有层级；
- `src/PdxLauncher.Core/Mods/Sorting/ConflictGraph.cs` 是 `AutoSortConflict`/`ConflictGraph` 记录，表达冲突**图**，不是树；
- **「代理分组」这个落点不存在**：项目没有代理列表，只有 `ProxyMode` + `ProxyUrl` 两个标量。

**结论：TreeView 唯一有意义的落点是「Mod 依赖树」，且必须先补三件事**：节点模型（App 层 `ModDependencyNode`）、树构建（mod → 依赖 → 依赖的依赖，需处理缺失节点与环）、数据来源（工坊依赖 matcher 已有，但要在 UI 里按需拉取）。

### C7 InfoBadge ↔ 无计数源

- `MainWindow.xaml:174` 的 `NavigationView` 只有 `Content`/`Tag`/`SymbolIcon`，没有任何计数；
- `ViewModels/ShellViewModel.cs` 现有状态：`SelectedTag`、`CurrentGame`、`StatusMessage`、`IsDiscovering`、`Games`（`ObservableCollection<GameInstallation>`）——**没有「可更新 Mod 数」之类的计数**；
- 但 `NavigationViewItem.InfoBadge` 属性在 2.5.1 里确实存在（`Microsoft.WinUI.xml:21490`），所以接线零成本。

**结论：能从 `Games.Count` 起步（「游戏库」角标），但要做到「可更新 Mod 数」需要新计数源（属增量任务，不是控件问题）。**

### C8 AnnotatedScrollBar ↔ ListView 内建滚动

- 8 个页面用的都是 `ListView`（内建 `ScrollViewer`）：`AchievementsPage` 3 个、`AddModsPage` 2、`ModLibraryPage` 2、`ModsPage` 3、`ModStudioPage` 2（另 6 个 `ItemsRepeater` + 4 个 `ScrollViewer`）、`SavesPage` 6、`WorkshopPage` 2；
- AnnotatedScrollBar **不能直接挂在这些滚动面上**：它靠 `IScrollController` 接线。本地 2.5.1 的证据链：
  - `AnnotatedScrollBar.ScrollController` → 「Gets an interface that exposes IScrollController members」（`Microsoft.WinUI.xml:13286`）
  - `ScrollPresenter.VerticalScrollController` / `.HorizontalScrollController` → 「Gets or sets the IScrollController implementation that can drive … scrolling」（`:33609` / `:33529`）
  - `ScrollView.ScrollPresenter`（`:24928`）——`ScrollView` 是 1.4 新控件，**它可以接线，`ScrollViewer` 不行**
- 现有列表全是 `ListView`/`ScrollViewer`，**没有暴露 `IScrollController`**（ModStudio 的 `ItemsRepeater` 也是包在 `ScrollViewer` 里）。

**结论：要接 AnnotatedScrollBar，必须把目标列表迁到 `ScrollView` + `ItemsRepeater`（或自写 `IScrollController` 适配层）。** 这是 8 个控件里**唯一需要改列表架构**的一项，风险与工作量都最高。

---

## 4. 逐控件落地方案

### 4.1 MenuFlyout —— 三个列表的右键菜单

**落点**：`ModsPage`（播放集 Mod 列表）、`SavesPage`（存档列表）、`AchievementsPage`（成就列表）的 `ListView` → `ContextFlyout`。

| 项 | 内容 |
| --- | --- |
| 改动文件 | `src/PdxLauncher.App/Views/ModsPage.xaml`(+`.cs`)、`Views/SavesPage.xaml`(+`.cs`)、`Views/AchievementsPage.xaml`(+`.cs`)、`src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`、`Localization/Localizer.cs` + 生成器、`tests/PdxLauncher.Checks/MenuFlyoutChecks.cs`（新）、`docs/DEVELOPMENT.md`（三处计数） |
| 改动量级 | **小**（XAML 60–90 行 + code-behind 60–90 行 + 检查类 60 行，合计 ~180–260 行） |
| i18n 新增键 | **6 条**，例：`mods.menu.revealInExplorer`、`mods.menu.copyWorkshopId`、`saves.menu.openFolder`、`saves.menu.duplicate`、`achievements.menu.copyId`、`common.menu.more` |
| 是否动 Core | 否 |
| 风险 | **低**（唯一注意：`MenuFlyoutItem` 必须带 AutomationId；右键菜单的键盘可达性用 `Alt`/`Shift+F10` 验） |
| 能否独立提交 | 能；三个页面可拆三次提交，也可合并一次（文件不重叠） |

**注意**：不要为「看依赖」提前塞菜单项——那是 TreeView 的事。

### 4.2 TableView —— 诊断页日志四列

**结论：当前做不了，不建议在本批开工。** 若强行做：

| 项 | 内容 |
| --- | --- |
| 改动文件 | `PdxLauncher.App.csproj`（版本 → 2.5.4-experimental）、`App.xaml`（加 `TabularControlsResources`）、`Views/DiagnosticsPage.xaml`、`ViewModels/DiagnosticsViewModel.cs`、新 `ViewModels/DiagnosticsLogRow.cs`、新 `Services/LogLineParser.cs`（或改 `LogTailReader`）、`tests/.../DiagnosticsLogTableChecks.cs`（新）、`tests/.../DiagnosticsPageChecks.cs`（19 个 AutomationId 断言要改）、`docs/DEVELOPMENT.md` |
| 改动量级 | **大**（9–12 文件，~450–700 行；另有 SDK 升级与全套回归） |
| i18n 新增键 | **8 条**：`diagnostics.log.columnTime` / `columnLevel` / `columnSource` / `columnMessage`、`diagnostics.log.empty`、`diagnostics.log.rows`、`diagnostics.log.density`、`diagnostics.log.copy` |
| 是否动 Core | **是**（若选择给日志加来源字段 → `Core/Infrastructure/FileLogger.cs`，且要兼容历史日志；若只在 App 层启发式解析则否，但「来源」列只能靠猜） |
| 风险 | **高**：SDK 不在稳定通道 + 实验警告撞 0 警告约束 + 四列无数据源 + 只能用 `{Binding}` 失去编译期检查 + 列不虚拟化 |
| 能否独立提交 | **不能**：依赖 SDK 升级（应作为独立提交并全面回归），且依赖日志解析模型 |

### 4.3 Expander —— 设置页折叠高级项

| 项 | 内容 |
| --- | --- |
| 落点 | 若做：`SettingsPage` 的「高级」分区内，把危险操作（清缓存/打开日志目录）折叠；不要放在 ModStudio（刚删过） |
| 改动文件 | `Views/SettingsPage.xaml`(+`.cs`)、`src/PdxLauncher.App/Strings/*/Resources.resw`、`tests/.../SettingsChecks.cs`（现有）或新检查类、`docs/DEVELOPMENT.md` |
| 改动量级 | **小**（1–2 文件，~80–120 行） |
| i18n 新增键 | **1–2 条**（如 `settings.advanced.dangerZoneHeader`） |
| 是否动 Core | 否 |
| 风险 | 技术**低** / 产品**中**：与 8 分区左导航构成双重收纳；项目两次引入两次移除 |
| 能否独立提交 | 能，但**建议不做** |

### 4.4 SelectorBar —— 工坊详情分区切换

| 项 | 内容 |
| --- | --- |
| 落点 | `Views/WorkshopDetailPage.xaml`：把单页流拆成「描述 / 依赖 / 画廊 / 信息」面板，SelectorBar 做切换（`Items` + `SelectedItem`，无 `ItemsSource`） |
| 改动文件 | `Views/WorkshopDetailPage.xaml`(+`.cs`)、`ViewModels/WorkshopDetailViewModel.cs`、`src/PdxLauncher.App/Strings/*/Resources.resw`、`tests/.../WorkshopPageChecks.cs`（现有断言会红）、新 `SelectorBarChecks.cs`、`docs/DEVELOPMENT.md` |
| 改动量级 | **中**（5–7 文件，~260–420 行；含内容重排与懒加载） |
| i18n 新增键 | **5 条**：`workshop.section.description` / `dependencies` / `gallery` / `info` / `updates` |
| 是否动 Core | 否 |
| 风险 | **中**：信息架构改动；现有 `WorkshopPageChecks` 断言需同步；画廊那段横向 `ScrollViewer` 与分区容器嵌套要重排 |
| 能否独立提交 | 能（与 ModStudio 的 SelectorBar 化**分开**，后者不建议做） |

### 4.5 TreeView —— Mod 依赖

| 项 | 内容 |
| --- | --- |
| 落点 | `WorkshopDetailPage` 的「依赖」分区（承接 4.4）；节点 VM 走 `ItemsSource`，不用 `TreeViewNode` |
| 改动文件 | `Views/WorkshopDetailPage.xaml`(+`.cs`)、新 `ViewModels/ModDependencyNode.cs`、`ViewModels/WorkshopDetailViewModel.cs`、可选 `src/PdxLauncher.Core/Mods/Sorting/ModDependencyGraph.cs`（树构建/环检测纯函数，便于单测）、`src/PdxLauncher.App/Strings/*/Resources.resw`、新 `tests/.../ModDependencyTreeChecks.cs`、`docs/DEVELOPMENT.md` |
| 改动量级 | **中**（6–8 文件，~320–520 行） |
| i18n 新增键 | **4 条**：`workshop.deps.missing` / `cycle` / `notInstalled` / `expandAll` |
| 是否动 Core | **建议是**（树构建与环检测放 Core 才能被检查类直接测；若纯 App 层则否） |
| 风险 | **中高**：依赖数据可能不完整（工坊页拿不到全部依赖元数据）、存在环、递归深度、空态/加载态 |
| 能否独立提交 | **半独立**：可与 4.4 同批，但应作为**独立提交**；依赖 4.4 的分区容器（若先不做 4.4，也可暂放现页面的「依赖」小节） |

### 4.6 NumberBox —— 播放集位置

| 项 | 内容 |
| --- | --- |
| 落点 | `Views/ModsPage.xaml:70-76` 的 `PositionEditBox` → `NumberBox`（`Minimum=1`、`SpinButtonPlacementMode=Compact`、`AcceptsExpression=false`） |
| 改动文件 | `Views/ModsPage.xaml`(+`.cs`)、`ViewModels/ModsViewModel.cs`、`src/PdxLauncher.App/Strings/*/Resources.resw`、`tests/.../ModsPageChecks.cs`（现有）或新检查类、`docs/DEVELOPMENT.md` |
| 改动量级 | **小**（2–4 文件，~70–110 行） |
| i18n 新增键 | **1–2 条**（如 `mods.position.invalid`） |
| 是否动 Core | 否 |
| 风险 | **低**：注意三点——① `ValueChanged` 在程序化赋值时也会触发（需防回环）；② `double` ↔ `int` 转换；③ 现有 `MaxWidth="40"` 放 spinner 会挤，需调宽并保 4 的倍数 |
| 能否独立提交 | 能（最小的一件） |

> 附：**端口/并发/超时**不在本控件范围内——它们现在不是可配项（`AppSettings` 只有 `ProxyMode`/`ProxyUrl`），要做是独立任务，量级「大」。

### 4.7 InfoBadge —— 左栏导航计数角标

| 项 | 内容 |
| --- | --- |
| 落点 | `MainWindow.xaml` 的 `NavigationViewItem` 加 `InfoBadge`；首期用 `ShellViewModel.Games.Count`（「游戏库」角标） |
| 改动文件 | `MainWindow.xaml`、`ViewModels/ShellViewModel.cs`、`src/PdxLauncher.App/Strings/*/Resources.resw`（`AutomationProperties.Name`）、新 `tests/.../ShellBadgeChecks.cs`、`docs/DEVELOPMENT.md` |
| 改动量级 | **小**（2–4 文件，~80–130 行） |
| i18n 新增键 | **1–2 条**（如 `shell.badge.countFormat` = `{0} 个`） |
| 是否动 Core | 否 |
| 风险 | **低**：计数为 0 时 `InfoBadge` 要 `Collapsed`（否则留白）；高对比度下颜色由资源管；`ShellViewModel` 是 `LocalizedObservableObject`，`{x:Bind ... Mode=OneWay}` 可直接用 |
| 能否独立提交 | 能 |

### 4.8 AnnotatedScrollBar —— 长列表分区跳转

| 项 | 内容 |
| --- | --- |
| 落点 | 先做 `WorkshopPage`（Mod 工坊长列表）单点试点：`ListView` → `ScrollView` + `ItemsRepeater`，接 `Labels` + `DetailLabelRequested` |
| 改动文件 | `Views/WorkshopPage.xaml`(+`.cs`)、`ViewModels/WorkshopViewModel.cs`、可选 `Controls/` 下封装适配器、`src/PdxLauncher.App/Strings/*/Resources.resw`、新 `tests/.../AnnotatedScrollBarChecks.cs`、`docs/DEVELOPMENT.md` |
| 改动量级 | **中偏大**（5–7 文件，~350–600 行；主要是列表迁移与行为回归） |
| i18n 新增键 | **1–3 条**（如 `workshop.jump.all` / `workshop.jump.installed`） |
| 是否动 Core | 否 |
| 风险 | **高**：列表容器换代 → 选中态、虚拟化、键盘导航、滚动条样式、滚动位置记忆（项目已有 `ListViewScrollState`）全部要回归；UIA 结构变化会动到现有检查类 |
| 能否独立提交 | **不能作为第一批**：建议先 30 分钟 spike 确认 `ScrollView.ScrollPresenter.VerticalScrollController` 接线方式，再单开一提交只做工坊一页 |

---

## 5. 总体可行性判断

**不能全补。** 分层结论：

| 层 | 控件 | 判断 |
| --- | --- | --- |
| 版本硬阻塞 | TableView | **不能**：2.5.1 无此控件；唯一可用形态是 `2.5.4-experimental`，官方禁止生产使用，且实验警告与「0 警告」硬约束冲突；升级本身还会破坏离线构建 |
| 数据面缺前置 | TreeView、AnnotatedScrollBar | **能，但要先补料**：TreeView 缺父子模型（只有平铺 ID），AnnotatedScrollBar 缺可接线的滚动容器（全是 ListView） |
| 产品/历史否决 | Expander | **能做但不该做**：两次引入两次移除，且与现有 8 分区导航重复收纳 |
| 低风险增量 | MenuFlyout、NumberBox、InfoBadge | **能，小件** |
| 中等架构增量 | SelectorBar | **能，但只用在一处** |

**为何 TableView 是唯一「不能」：** 判定不看难度，看**是否可交付**。在 2.5.1 上它连编译都过不去（winmd 0 命中）；要交付就必须把整个 SDK 抬到实验通道，而这一步的影响面（4 个自绘控件、110 个导航项、自写 `Program.Main`、离线构建链路）远大于「补一个表格」。

---

## 6. 推荐顺序（分 3 批，每批可独立合并）

### 批 1 —— 小件，零架构改动（建议先做）

1. **InfoBadge**（`MainWindow` + `ShellViewModel`）
2. **NumberBox**（`ModsPage.PositionEditBox`）
3. **MenuFlyout**（三个列表的右键菜单）

- **可合并性**：三次提交文件不重叠（`MainWindow`/`ShellViewModel`、`ModsPage`、`SavesPage`+`AchievementsPage`），互不阻塞。
- **并行注意**：`docs/AGENT-WORKFLOW.md` §4 规定「一波最多 2 个写任务」，`MenuFlyout` 与 `NumberBox` 都碰 `ModsPage.xaml`，**必须串行**；InfoBadge 可与之并行。
- 每批收尾三件事：新增检查类 → 改 `docs/DEVELOPMENT.md` 三处数字 → `build-app.sh -v q` 零警告（生成器编译期校验键集）。

### 批 2 —— 新信息架构（SelectorBar 先行，TreeView 承接）

1. **SelectorBar**（工坊详情分区化）
2. **TreeView**（依赖分区）

- **依赖关系**：TreeView 的落点由 SelectorBar 划分出来，建议**同批但分两次提交**（先分区，再树），方便回滚。
- 若不想动工坊详情结构，可把 TreeView 先放进现有页面的「依赖」小节，与 SelectorBar 解耦——但会多一次搬迁。

### 批 3 —— 大件 / 高风险（各自单独立项）

1. **AnnotatedScrollBar**（先工坊一页试点，30 分钟 spike 后再动手）
2. **TableView**（**条件项**：仅当决定升级到 `2.5.4-experimental`，且先完成 `LogLineParser` + 日志来源字段方案；否则挂起）

- 批 3 两项**不要互相依赖**，可任选其一先做。
- 若 TableView 的 SDK 升级被否，则批 3 只剩 AnnotatedScrollBar，整体工期不受影响。

---

## 7. 总工作量估算

> 估算基准：本项目 2026-09～11 的真实提交，**不报小时数**。
> 参照：`5200f89 feat(achievements 筛选搜索)` = 8 文件 / 823+；`6364839 feat(saves polish)` = 11 文件 / 355+；`efb6698 feat(achievements)` = 13 文件 / 1035+；`8a1c2ed feat(steam)` = 18 文件 / 2432+；`d372152 feat(account 单步登录)` = 3 文件 / 178+。
> 口径：**小 = 2–4 文件 / 80–200 行；中 = 5–8 文件 / 250–600 行；大 = 9–12 文件 / 450–700 行（不含 SDK 升级）。**

| 批次 | 控件 | 规模 | 文件数 | 关键参照 |
| --- | --- | --- | --- | --- |
| 批 1 | InfoBadge | 小 | 2–4 | `d372152`（3 文件 178+） |
| 批 1 | NumberBox | 小 | 2–4 | 同上量级 |
| 批 1 | MenuFlyout | 小（偏上） | 4–7 | `39af882`（5 文件 152+） |
| 批 1 小计 | — | **小** | **约 7–10（已去重共用文件）** | `5200f89` 量级 |
| 批 2 | SelectorBar | 中 | 5–7 | `6364839`（11 文件 355+） |
| 批 2 | TreeView | 中 | 6–8 | `efb6698`（13 文件 1035+）偏小 |
| 批 2 小计 | — | **中** | **约 10–13** | — |
| 批 3 | AnnotatedScrollBar | 中偏大 | 5–7 | `4ab758a`（17 文件 991+）偏小 |
| 批 3 | TableView（条件） | 大 + SDK 升级 | 9–12（+1 升级提交） | `8a1c2ed`（18 文件 2432+） |
| **合计（不含 TableView）** | 7 控件 | **中大** | **约 18–24**（含 4–6 个新检查类、8 个 i18n area 文件、3 处文档数字） | 与 `5200f89`+`6364839` 两笔之和相当 |
| **合计（含 TableView）** | 8 控件 | **大** | **约 30–36**（另加 SDK 升级与全量回归） | 接近 `8a1c2ed` |

**估算里最容易低估的三块**（都不在「写 XAML」里）：

1. **检查类**：每个控件要求「断言真的能失败」，4–6 个新类 ≈ 300–400 行，还要改 `docs/DEVELOPMENT.md` 三处数字；
2. **i18n**：每控件 1–8 条键，zh-CN/en-US 双份 + 生成器 + `--check`；
3. **回归**：AnnotatedScrollBar 换列表容器、TableView 换 SDK，都会动到现有页面级断言（`WorkshopPageChecks`、`DiagnosticsPageChecks` 的 19 个 AutomationId）。

---

## 8. 需要你拍板的三件事

1. **Expander 是否确认不做？**（推荐：不做。两次移除史 + 与 8 分区导航重复收纳）
2. **TableView 是否缓到 SDK 稳定通道？**（推荐：缓。唯一硬门槛，且升级会破「离线可构建 + 0 警告」两条）
3. **AnnotatedScrollBar 是否从工坊页单点试点？**（推荐：是。先 30 分钟 spike 验证 `ScrollView` 接线，再单独立项）

以上三项确认后，**批 1 的三个提交（InfoBadge / NumberBox / MenuFlyout）可立即开工**，互不阻塞。

---

## 附录 A：核实方法与可复现命令

```powershell
# 1) 确认没有中央包管理/锁文件（三者都应为空）
Get-ChildItem -Recurse -Include Directory.Packages.props,global.json,packages.lock.json

# 2) 确认实际版本与依赖树
Select-String -Path obj\project.assets.json -Pattern 'Microsoft.WindowsAppSDK/\d'

# 3) 控件是否存在（以 2.5.1 的 WinUI 子包为准）
$pkg = "$env:USERPROFILE\.nuget\packages\microsoft.windowsappsdk.winui\2.3.9"
Select-String -Path "$pkg\metadata\Microsoft.UI.Xaml.winmd" -Pattern 'TableView' -AllMatches
Select-String -Path "$pkg\lib\net6.0-windows10.0.17763.0\Microsoft.WinUI\Themes\generic.xaml" -Pattern 'AnnotatedScrollBar'

# 4) 文案自检
$env:PYTHONIOENCODING = 'utf-8'
python src\PdxLauncher.Localization.Generator --check

# 5) 构建（必须 0 警告 0 错误）
bash tools/build-app.sh -v q
```

## 附录 B：来源链接

| 主题 | 链接 | 页面更新 |
| --- | --- | --- |
| TableView（含 2.5.4-experimental 与限制） | <https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/table-view> | 2026-10-14 |
| SelectorBar 用法与限制 | <https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/selector-bar> | 2026-02-28 |
| WinAppSDK 1.5（SelectorBar 新增） | <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-5> | — |
| WinAppSDK 1.4（AnnotatedScrollBar 新增） | <https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-notes/windows-app-sdk-1-4> | 2025-12-20 |
| WinUI 2.6（Expander 引入） | <https://learn.microsoft.com/en-us/windows/uwp/get-started/winui2/release-notes/winui-2.6> | — |
| WinUI 2 release notes 索引 | <https://learn.microsoft.com/en-us/windows/apps/winui/winui2/release-notes/> | — |
| 本地 API 文档（2.5.1） | `%USERPROFILE%\.nuget\packages\microsoft.windowsappsdk.winui\2.3.9\lib\net6.0-windows10.0.17763.0\Microsoft.WinUI.xml` | — |

## 附录 C：本报告的已知边界

- TableView 的**稳定版时间表未知**：官方文档只写「2.5.4-experimental 起可用」，未给 GA 计划。
- AnnotatedScrollBar 的接线方式**尚未经代码验证**：API 层面（`ScrollPresenter.VerticalScrollController` + `AnnotatedScrollBar.ScrollController`）已确认存在，但「`ScrollView` 包裹 `ItemsRepeater` 后实际手感」需 spike 30 分钟。
- TreeView 的数据完整性**未实测**：工坊元数据是否总能拿到被依赖项的标题（而不是只有 ID），需在实现时验证；拿不到就只能显示 ID + 缺失态。
- 「代理分组」落点**不存在**：项目只有 `ProxyMode`/`ProxyUrl` 两个标量，没有代理列表，该用途需先有产品设计。
