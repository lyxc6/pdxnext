# 设置页迁移到官方 SettingsCard / SettingsExpander

> 状态：**可行性已验证，方案待评审**。本文只出方案，没有改动任何一张现有卡片。
> 验证日期：2026-11-06。分支：`feat/1.3`。HEAD：`ac7225e`。
> 官方指南：<https://learn.microsoft.com/en-us/windows/apps/design/app-settings/guidelines-for-app-settings>

## 0. 结论（TL;DR）

1. **包能装。** `CommunityToolkit.WinUI.Controls.SettingsControls 8.2.251219` 从 nuget.org 拉取、restore、build、`dotnet publish`、运行期加载全部实测通过；`WindowsAppSDKSelfContained=true` + 本项目手工 publish 流程能正确带上第三方 XAML 资源（`generic.xaml` 编译进 `PdxLauncher.App.pri`，`SettingsCard`/`SettingsExpander` 在 UIA 树里可见且可交互）。
2. **但不能"全局换皮肤"。** App/页面级 `SettingsCard*` 资源键覆盖、`Style BasedOn="{StaticResource DefaultSettingsCardStyle}"` 两种常规做法**都失效**（后者运行时直接让设置页构造失败）。唯一稳定可用的是**给每张卡直写本地属性**（`Background`/`BorderBrush`/`BorderThickness`/`CornerRadius`/`MinHeight`/`Padding`），文中称"六件套"，已实测生效。
3. **机械改造面很小。** `SettingsPage.xaml` 的 47 张卡**全部**用 `Header` 属性 + `<controls:SettingsCard.ActionContent>` 属性元素写动作内容，没有一张用 `ActionContent` 属性、没有一张用 `HeaderIcon`；`Description` 只有 4 张。所以属性几乎不需要改名字，真正要动的只有"去掉 `ActionContent` 包层 + 换命名空间 + 加六件套"三步，**可脚本化**。
4. 工作量：4 个阶段、约 **4.5 人日**（含验证与自检修正），最大的一次性风险集中在"外观口径不一致"与"自绘控件删除顺序"，不在功能。

## 1. 包可达性实测（可复现）

### 1.1 前提：本仓是"全离线构建"

`NuGet.config` 只有：

```xml
<packageSources>
  <clear />
</packageSources>
```

注释写明：清空所有包源，所有包都从本机 NuGet 包缓存（`%USERPROFILE%\.nuget\packages`）还原。
`NUGET_PACKAGES` 环境变量未设置。因此**本机第一次引入新包必须联网 warm-up 一次**，之后就能继续离线构建。CI 不受影响：`.github/workflows/ci.yml:31` 与 `release.yml:32` 已经在运行时把 nuget.org 写进 `NuGet.config`。

实测起点：`C:\Users\17147\.nuget\packages` 下**没有** `communitytoolkit.winui.controls.settingscontrols`，也**没有** `communitytoolkit.winui.triggers`（只有 `communitytoolkit.mvvm 8.4.2` 与 `communitytoolkit.highperformance`）。SDK 仅 `dotnet 10.0.402`。

### 1.2 版本选择

| 版本 | 结论 |
| --- | --- |
| 8.2.251219（最新稳定） | **选它**。依赖 `CommunityToolkit.WinUI.Triggers 8.2.251219` + `Microsoft.WindowsAppSDK >= 1.6.250108002`，本项目 2.5.1 直接满足。 |
| 8.3.260402-preview2 | 不选。改为依赖拆包后的 `Microsoft.WindowsAppSDK.WinUI 1.8.260204000`，会引入第二套 WinAppSDK 版本口径。 |
| 8.1.x / 8.0.x | 不选。`SettingsExpander` 的 `Items`/`IsExpanded` 行为与 8.2 有差异，无收益。 |

包内资产只有 `lib/net7.0-windows10.0.18362`、`net8.0-windows10.0.19041`、`net9.0-windows10.0.19041` 三档，**没有 net10**；`net10.0-windows10.0.26100.0` 会回退吃 `net9.0-windows10.0.19041`（实测 `project.assets.json` 确认）。传递依赖共 5 个包：SettingsControls → Triggers → Helpers → Extensions → `CommunityToolkit.Common 8.2.1`。

### 1.3 实测步骤与结果

```powershell
# 1) 临时加引用（src\PdxLauncher.App\PdxLauncher.App.csproj，CommunityToolkit.Mvvm 之后）
#    <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.2.251219" />

# 2) 联网还原（9.61s，成功）
dotnet restore src\PdxLauncher.App\PdxLauncher.App.csproj -s https://api.nuget.org/v3/index.json -p:Platform=x64

# 3) 构建（0 警告 0 错误，30s；改包前基线同为 0 错，48s）
dotnet build src\PdxLauncher.App\PdxLauncher.App.csproj -c Release -p:Platform=x64 -m:1

# 4) 发布（验证手工 publish 流程）
dotnet publish src\PdxLauncher.App\PdxLauncher.App.csproj -c Release -m:1 -p:Platform=x64
```

结果：

- `OutDir` 与 `publish` 目录都出现 `CommunityToolkit.WinUI.Controls.SettingsControls.dll`、`...Triggers.dll`、`...Helpers.dll`、`...Extensions.dll`、`CommunityToolkit.Common.dll`；**没有独立的 toolkit `.pri`**。
- `PdxLauncher.App.pri` 从 **2 290 728 → 2 333 144 字节（+42 416）**，UTF-16 扫描含 `SettingsCard` / `SettingsExpander` / `SettingsControls` 字符串 → 第三方 XAML 资源确实被合并进主 PRI。`publish.cmd:72-76` 校验的 5 个文件（`App.xbf`、`PdxLauncher.App.pri`、`Views\HomePage.xbf`、`Controls\ConfirmOverlay.xbf`、`Assets\AppIcon.ico`）全部齐全；`Views\SettingsPage.xbf` 与 `Controls\SettingsCard.xbf` 也在产物里。
- 运行期：在 `SettingsPage.xaml` 临时插一张 `ct:SettingsCard`（含 `ToggleSwitch`）与一个 `ct:SettingsExpander`（`Items` 里再套一张卡），**构建产物 exe 与 publish 产物 exe 都能正常渲染**，UIA 探针输出：

```
FOUND probeToolkitCard class='SettingsCard' rect=1051x105
FOUND probeToolkitExpander class='SettingsExpander' rect=1051x106
FOUND probeToolkitToggle class='ToggleSwitch' rect=99x30
elementcount=89 types=Button=10,ComboBox=4,Custom=2,Group=7,List=4,ListItem=20,Pane=9,ScrollBar=1,Text=29,TitleBar=1,Window=2
```

> 探针脚本在 `%TEMP%\pdx-nuget-probe\probe-uia.ps1`（未入库）：启动 exe → 等窗口 → 点 `navSettings` → 点 `navSettingsAppearance` → 按 AutomationId 查元素 → 截图 → 结束进程。截图 `shot-build2.png` / `shot-publish.png` 同目录。

**结论：包能装，资源能进 PRI（构建与 publish 两条路径都验过），可以进入方案阶段。**

### 1.4 外观差异（必须先解决）

官方卡默认 `CardBackgroundFillColorDefaultBrush`（半透明灰）、`MinHeight=68`、`Padding=16,16,16,16`，实测高 **105 px**；自绘 `PdxCard` 是 `#FF27272C` 深底、`MinHeight=56`、`Padding=16,12`、`CornerRadius=8`，高约 76 px。**直接替换会有明显的「变高、变亮」观感差异**，这是本次迁移最容易被用户看见的风险。

## 2. 外观口径：唯一可用的覆盖方式

三次实验（每次 `dotnet build` + UIA 探针 + 截图）：

| 实验 | 做法 | 结果 |
| --- | --- | --- |
| A | 页资源写 `<Style x:Key="PdxSettingsCardStyle" TargetType="ct:SettingsCard" BasedOn="{StaticResource DefaultSettingsCardStyle}">` | 编译通过，**运行期设置页构造抛异常**：导航静默回退主页（UIA 打 `NO_APPEARANCE_NAV`，所有探针元素 MISSING，进程不崩、事件日志无记录）。根因：`DefaultSettingsCardStyle` 在 toolkit 的 `Themes/Generic.xaml` 里，App/页面资源字典取不到。**禁用。** |
| B | App.xaml 三套 `ThemeDictionaries` 里加 `<StaticResource x:Key="SettingsCardBackground" ResourceKey="PdxCardBackgroundBrush" />` 等 5 个别名 | 放在 `PdxCardStrokeBrush` 之后（早于 `PdxCardHoverTintBrush` 定义）→ 与 A 同样的**运行期崩溃**（同字典内 `StaticResource` 前向引用非法）。挪到三套字典末尾 → 页面正常加载，**但卡高仍是 105**，视觉口径没变。 |
| C | 卡上直写本地属性 | **生效**：`Background="#FF5B1B4B"` → 取样 `cardFill=#5B1B4B`；`BorderBrush="#FF00FF88"` → 取样 `cardBorderTL=#00FF88`；`MinHeight="56" Padding="16,12"` → 卡高 105 → **94**。 |

结论与落地口径：

- **不要**写 `SettingsCard*` 资源键别名，**不要** `BasedOn` 官方样式。这两种写法要么崩、要么不生效，属于"看起来能行、跑起来不行"的高危陷阱。
- **每张卡直写"六件套"**（本地值压过 Style setter，模板根 `PART_RootGrid` 全部 `TemplateBinding`，所以必然生效）：

```xml
<ct:SettingsCard
    Header="…"
    Background="{ThemeResource PdxCardBackgroundBrush}"
    BorderBrush="{ThemeResource PdxCardStrokeBrush}"
    BorderThickness="1"
    CornerRadius="8"
    MinHeight="56"
    Padding="16,12"
    IsClickEnabled="False">
```

- 别名请继续引用 App.xaml 里已有的 `PdxCardBackgroundBrush` / `PdxCardStrokeBrush`（三套主题都有定义），这样明暗/高对比仍然跟随主题，而不是把颜色写死。
- `IsClickEnabled` 官方默认 `true`（`ButtonBase` 子类，整卡可点、进点击态；默认值取自 Toolkit 源码的 `PropertyMetadata(true)`，本仓没有单独测"默认真"下的点击行为）。我们的卡都没有 `Click` 处理器，统一显式 `False`：避免卡片吞掉内部控件的点击，也避免卡片进 Tab 序列（保持现有键盘路径不变）。**注意**：`False` 时卡片不再有 hover/pressed 视觉，与现在的静态 `PdxCard` 一致——Stage 1 试点时顺手确认一次"点卡体不吞事件、键盘仍能 Tab 到开关"。
- 4px 网格自检（`tests/PdxLauncher.Checks/SpacingChecks.cs`）只扫 `Padding`/`Margin`/`Spacing`/`RowSpacing`/`ColumnSpacing` 与 `CornerRadius`（只允许 0/4/8），**不扫 `MinHeight`**。六件套里的 `16,12`、`8` 都合法，写 `Padding="16,16,16,16"` 或 `CornerRadius="12"` 会被判红。
- 官方默认样式会给卡内的 `ToggleSwitch` 套 `RightAlignedCompactToggleSwitchStyle`、给 `Slider`/`ComboBox`/`TextBox` 设 `MinWidth=SettingsCardContentMinWidth(120)`；`PasswordBox` 与自绘 `PdxDropDown` **不在**这条规则里，试点时若宽度塌陷需显式 `MinWidth`。

## 3. 与官方指南逐条对照

| 指南条款 | 现状 | 落地做法 | 阶段 |
| --- | --- | --- | --- |
| 设置入口在 `NavigationView` 最后一个、固定底部 | **已满足**：`MainWindow.xaml:267-292` 用 `<NavigationView.FooterMenuItems>`，顺序是 `navDiagnostics`（性能与日志）→ `navSettings`（设置），且 `IsSettingsVisible="False"`（不用内置项，自己在 footer 放）。`DiagnosticsPageChecks` 断言"性能与日志排在设置之前" | 不改。若哪天想用内置项，改成 `IsSettingsVisible="True"` 即可，但会丢掉自定义图标与 Tag 路由，收益为负 | — |
| 设置页整页打开、可滚动 | **已满足**：`SettingsPage` 是独立页，右侧 `ScrollViewer`（`VerticalScrollBarVisibility=Visible`，水平全 `Disabled`，内容 `Margin="0,0,16,0"` 给浮层滚动条留 16 DIP 通道） | 不改 | — |
| 内容列 1000–1100 px 约束 | **未满足**：内容列自适应铺满，实测卡宽 1051（只是恰好落区间） | 在 `ScrollViewer` 内套一层容器：`<Grid MaxWidth="1040" HorizontalAlignment="Center">`（或 `StackPanel` 同参）。1040 取 4 的倍数，落在 1000–1100 中段；窗口再宽也不会把卡片拉成"超长条" | 4 |
| 分节标题用 `BodyStrong` | **未满足**：9 处 `SubtitleTextBlockStyle`（`SettingsPage.xaml:138,253,279,359,443,550,601,632,939`），`BodyStrong` 0 处 | 节标题统一换 `{StaticResource BodyStrongTextBlockStyle}`（仓库里其它页已在用这个键）；已有 `SettingsSubsectionTitleStyle`（8 处）保留为二级分组标题，BasedOn 改 `BodyStrongTextBlockStyle` + 上间距 16 | 4 |
| 单项 `SettingsCard`、折叠子项 `SettingsExpander` | 全部自绘 `SettingsCard`，无折叠 | 47 张卡 → `ct:SettingsCard`；About 段（第 8 个导航项 `Tag=7`）→ 一个 `ct:SettingsExpander`（`Items` 里放 8 张卡，见 §5.4） | 2–4 |
| **不引入通用 `Expander` 控件**（本项目既有决定） | 已决定不引入 | 本方案的 `SettingsExpander` 来自设置包自带的 `CommunityToolkit.WinUI.Controls.SettingsExpander`，与"不引通用 Expander"不冲突。**它是 `Expander` 的专用派生，只用于设置项分组**；通用折叠区仍不用 `Expander` | — |
| About 段放在页面最底部、用 `SettingsExpander` | About 是第 8 个导航项（`navSettingsAbout`，`Tag=7`），内容是 8 张卡平铺 | 保留导航项（本页是"分节切换"结构，没有单一长页），但把它做成**该节内唯一的 `SettingsExpander`**：折叠头 = 应用名 + 图标 + 版本号，展开区 = 版本与安装模式 5 张卡 + 更新 3 张卡。位置天然是页底 | 4 |
| 每节设置项 4–5 个 | 现在 3–18 个（见下表） | 用已有 `settings.section.*` 键做二级分组，把每节拆成 2–4 组，每组 3–5 项 | 4 |
| 布尔用 `ToggleSwitch` | 14 张卡是 `ToggleSwitch` | 保留 | — |
| 小集合用 `RadioButton` | 现在没有 `RadioButton`（全是 `PdxDropDown` 或 `ToggleSwitch`） | 二选一场景（如主题来源）可改 `RadioButton`，但不强制；`.ActionContent` 换成 `StackPanel` + 两个 `RadioButton` 即可 | 可选 |
| 多项用 `ComboBox` | 10 张卡用自绘 `PdxDropDown`（`UserControl`，非 `ComboBox` 派生） | **本次不动 `PdxDropDown`**：它是独立控件改造任务（换成官方 `ComboBox` 会牵动主题与 UIA 脚本）。作为 `Content` 放进官方卡里照常工作 | 另立 |
| 立即生效、不要确认按钮 | 已满足（无"保存"按钮，开关即时写盘） | 不改 | — |

每节现状（`SettingsPage.xaml`，卡片数含 value 卡）：

| 节 | 起始行 | 卡片数 | 需要拆分组 |
| --- | --- | --- | --- |
| `secAppearance` 外观 | 137 | 11 | 3 组：主题 / 界面 / 窗口 |
| `secLaunch` 启动 | 252 | 3 | 否 |
| `secPaths` 路径 | 278 | 7 | 2 组：游戏路径 / 数据目录 |
| `secOnline` 联网 | 358 | 7 | 2 组：代理 / Steam |
| `secCache` 缓存 | 442 | 8 | 2 组：缓存 / 在线素材 |
| `secAdvanced` 高级 | 547 | 5 | 否 |
| `secTranslation` 翻译 | 631 | 18 | 4 组：引擎 / 密钥 / 范围 / 缓存与测试 |
| `secAbout` 关于与更新 | 937 | 8 | 折叠为 1 个 `SettingsExpander` |

## 4. 47 张卡的机械改造

### 4.1 现状盘点（精确）

`src/PdxLauncher.App/Views/SettingsPage.xaml`（1040 行）：

- `<controls:SettingsCard` 开标签 **47**、闭标签 **47**、`<controls:SettingsCard.ActionContent>` 属性元素 **47 对**。用户口中的"94 处"= `<controls:SettingsCard` 这个前缀出现 94 次（47 开标签 + 47 个 `.ActionContent`）。
- 属性分布（47 张）：`Header` 47、`Visibility` 8、`Description` 4、`Margin` 2、`ToolTip` 1。**没有** `Background`/`BorderBrush`/`MinHeight`/`Padding`/`Style`/`IsTabStop` 等外观属性 → 外观全部来自自绘控件模板，所以"六件套"要新加。
- 动作内容（`ActionContent` 里第一个子控件）：`ToggleSwitch` 14、`StackPanel`（多控件组合）11、`controls:PdxDropDown` 10、`TextBox` 5、`Button` 4、`PasswordBox` 3。
- 另外两处同类页面：`Views/DiagnosticsPage.xaml` 9 张卡（6 张有 `ActionContent`）+ 1 张 value 卡；`Views/AccountPage.xaml` 1 张卡。合计 **57 张 `SettingsCard` + 21 张 `SettingsValueCard`**。
- 数据模板里的卡：`SettingsPage.xaml:462`（`ItemsControl` + `DataTemplate x:DataType="vm:CacheRowViewModel"`）与 `:830`（`LocalModelRowViewModel`）、`:880`（`GlossaryEntryViewModel`）。其中 `:463`、`:831` 两张卡的 `Header="{x:Bind Title, Mode=OneWay}"`，带 `Margin`/`ToolTip` → 属于特例（见 4.3）。

### 4.2 属性映射表

| 自绘 | 官方 | 处理 |
| --- | --- | --- |
| `<controls:SettingsCard` | `<ct:SettingsCard` | 直接换命名空间 |
| `Header="…"` | `Header="…"` | **同名同义，零改动**（官方 `Header` 是 `object`，字符串照用；`Header` 非空时官方会把 `Content` 的 `AutomationProperties.Name` 自动设成 Header 文案，但我们内部控件自带 AutomationId，不冲突） |
| `Description="…"` | `Description="…"` | **同名同义，零改动**（官方用 12px 次级色，与现在的 `CaptionTextBlockStyle + TextFillColorSecondaryBrush` 一致） |
| `<controls:SettingsCard.ActionContent>` … `</controls:SettingsCard.ActionContent>` | `Content`（默认内容属性） | **删掉这两行**，里面的控件直接作为 `<ct:SettingsCard>` 的子节点。47 张卡全都是这种写法，没有一张用 `ActionContent="…"` 属性 → 规则单一，脚本安全 |
| （无） | `HeaderIcon` | 暂不用（本次不加图标；若要加，`SymbolIcon`/`BitmapIcon` 放 `HeaderIcon`） |
| （无） | `IsClickEnabled` | 统一 `False`（见 §2） |
| `Visibility` / `Margin` / `ToolTip` | 同名 | 原样保留 |
| （新增） | 六件套 | `Background="{ThemeResource PdxCardBackgroundBrush}" BorderBrush="{ThemeResource PdxCardStrokeBrush}" BorderThickness="1" CornerRadius="8" MinHeight="56" Padding="16,12"` |

外观一致性不需要 `Style`：47 张卡各写一次六件套，用脚本注入即可，且**每张卡都能被单独覆盖**（例如将来的危险操作卡可以只改 `BorderBrush`）。

### 4.3 特例清单（不能一刀切）

1. **数据模板内的卡**（`SettingsPage.xaml:463`、`:831`）：在 `DataTemplate` 里，`Header="{x:Bind Title, Mode=OneWay}"` 依赖 `x:DataType`；换标签不影响绑定，但脚本改写时**不要**动 `x:DataType` 行；这两张卡带 `Margin`（列表行间距）与 `ToolTip`，替换后间距可能变成"卡自带 12 + Margin"，需要目视确认一次。
2. **动作内容是 `StackPanel` 的 11 张**：官方模板右对齐（`HorizontalContentAlignment=Right`），`StackPanel` 里若有 `HorizontalAlignment="Right"`/`Spacing` 会叠加；试点节（外观/启动）里至少有一张属于此类，先验一次，其余按同样结论套用。
3. **`Visibility` 条件卡 8 张**（代理探测结果 1 张、翻译引擎/密钥相关 6 张、安装模式目录 1 张，行号 392 / 663 / 674 / 684 / 695 / 705 / 729 / 957）：`Visibility` 保持原绑定；注意官方卡的 `Visibility=Collapsed` 时 `AutomationId` 也消失，UIA 脚本是"按节检查存在性"，需确认脚本对隐藏卡没有硬断言（当前 `verify-settings-sections.ps1` 的清单里没有这些条件卡的 id，安全）。
4. **`PasswordBox` 3 张**：官方不注入 `MinWidth`，可能比 `TextBox` 窄；试点时给个 `MinWidth="240"`（4 的倍数）。
5. **`PdxDropDown` 10 张**：`UserControl`，宽度由自身决定；官方卡内容区右对齐，若原本靠 `HorizontalAlignment="Stretch"` 撑开的，替换后可能收缩 → 试点时逐张看。
6. **`Margin` 2 张 / `ToolTip` 1 张**：原样保留，但 `ToolTip` 挂在卡上时官方卡是 `ButtonBase`，`ToolTipService` 仍有效。
7. **`Header` 之外还有副标题卡 4 张**（`Description`）：官方 `Description` 的换行阈值是 `WrapThreshold=476`、无图标 `286`，比自绘卡的"左列拉伸"更早换行 → 长描述会被折成两行、卡变高。试点先看这 4 张。

### 4.4 脚本思路（建议用一次性 PowerShell/Python，改动前先 `git switch -c`）

```python
# 1) 根标签换命名空间（只动标签名，不动 .ActionContent 之前的部分）
text = text.replace("<controls:SettingsCard", "<ct:SettingsCard")
text = text.replace("</controls:SettingsCard>", "</ct:SettingsCard>")
# 2) 删掉 ActionContent 属性元素包层（成对、整行）
text = re.sub(r"[ \t]*<controls:SettingsCard\.ActionContent>\s*\n", "", text)
text = re.sub(r"[ \t]*</controls:SettingsCard\.ActionContent>\s*\n", "", text)
# 3) 给每个开标签注入六件套（在 Header 之前，保持缩进对齐）
SIX = ('Background="{ThemeResource PdxCardBackgroundBrush}"\n'
       '          BorderBrush="{ThemeResource PdxCardStrokeBrush}"\n'
       '          BorderThickness="1" CornerRadius="8" MinHeight="56" Padding="16,12"\n'
       '          IsClickEnabled="False"')
# 4) 页面根加 xmlns:ct="using:CommunityToolkit.WinUI.Controls"
```

校验（脚本后立即跑）：

```powershell
# 计数应分别为 47/47/0/0
(Select-String -Path src\PdxLauncher.App\Views\SettingsPage.xaml -Pattern '<ct:SettingsCard\s').Count
(Select-String -Path src\PdxLauncher.App\Views\SettingsPage.xaml -Pattern '</ct:SettingsCard>').Count
(Select-String -Path src\PdxLauncher.App\Views\SettingsPage.xaml -Pattern 'controls:SettingsCard').Count
(Select-String -Path src\PdxLauncher.App\Views\SettingsPage.xaml -Pattern 'SettingsCard\.ActionContent').Count
dotnet build src\PdxLauncher.App\PdxLauncher.App.csproj -c Release -p:Platform=x64 -m:1
```

风险提示：`<controls:SettingsCard.Header>` 这类属性元素写法本仓**没有**（已 grep 确认），所以"属性元素转属性"的复杂情况不存在，脚本可以放心。

## 5. 自绘控件的处置

### 5.1 `Controls/SettingsCard.xaml` + `.xaml.cs`（DP：`Header`/`Description`/`ActionContent`）

处理顺序（**不能先删**）：

1. **Stage 1–2**：文件保留、不删。此时页面上不会再有 `controls:SettingsCard` 引用，但它仍是 `SettingsValueCard` 的宿主（见 5.2），所以**必须留到 5.2 完成**。
2. **Stage 3**：`SettingsValueCard` 改成直接包 `ct:SettingsCard` 后，`Controls/SettingsCard.xaml(.cs)` 变为零引用 → 删除两个文件。`PdxLauncher.App.csproj` 的 publish 目标是 glob（`Controls\*.xbf`），删文件后自动不再包含，**不需要改 csproj**；`publish.cmd:72-76` 的硬编码校验清单是 `App.xbf`、`PdxLauncher.App.pri`、`Views\HomePage.xbf`、`Controls\ConfirmOverlay.xbf`、`Assets\AppIcon.ico`——**不含** `SettingsCard.xbf`，所以也不必改。
3. 删完跑 `build.cmd` + `publish.cmd`，并确认产物里没有 `Controls\SettingsCard.xbf`。

### 5.2 `Controls/SettingsValueCard.xaml`（20 处调用）

20 张 value 卡的调用点与 id：

| 行 | Header 键 | ValueAutomationId |
| --- | --- | --- |
| 140 | `L.SettingsThemeSource` | `txtThemeSource` |
| 146 | `L.SettingsThemeLastCheck` | `txtThemeLastCheck` |
| 152 | `L.SettingsThemeNextCheck` | `txtThemeNextCheck` |
| 158 | `L.SettingsThemeBackgroundFile` | `txtThemeBackgroundFile` |
| 164 | `L.SettingsThemeLastFailure` | `txtThemeLastFailure` |
| 255 | `L.SettingsLaunchExe` | `txtExePath` |
| 260 | `L.SettingsLaunchArgs` | `txtExeArgs` |
| 297 | `L.SettingsDataDlcScan` | `txtDlcPath` |
| 302 | `L.SettingsDataModDir` | `txtModDir` |
| 307 | `L.SettingsDataFormatVersion` | `txtFormatVersion` |
| 313 | `L.SettingsDataLoadFile` | `txtLoadFilePath` |
| 387 | `L.SettingsProxyCurrent` | `txtProxyStatus` |
| 432 | `L.SettingsSteamApiHeader` | `txtSteamApiStatus` |
| 536 | `L.SettingsModThumbnailsCacheDir` | `txtModThumbnailCachePath` |
| 622 | `L.SettingsDangerSignatureName` | `txtLastSignature` |
| 941 | `L.SettingsAboutLauncherVersion` | `txtLauncherVersion` |
| 946 | `L.SettingsAboutInstallMode` | `txtInstallMode` |
| 951 | `L.SettingsAboutInstallModeVersionLabel` | `txtInstallModeVersion` |
| 980 | `L.SettingsAboutInstallModeDataDirectory` | `txtInstallModeDataDirectory` |
| 1008 | `L.SettingsAboutLastCheckLabel` | `txtLastUpdateCheck` |

推荐方案（最低风险，**保留 20 处调用点与 6 个 DP 语义**）：

- 把 `SettingsValueCard.xaml` 的内部宿主从 `controls:SettingsCard` 换成 `ct:SettingsCard`，其余（`Value`/`ValueAutomationId`/`ValueAutomationName`/`MaxValueWidth`/`ShowToolTip`/`ToolTipText`）全部不变：

```xml
<ct:SettingsCard Header="{x:Bind Header, Mode=OneWay}" Description="{x:Bind Description, Mode=OneWay}"
                 Background="{ThemeResource PdxCardBackgroundBrush}" BorderBrush="{ThemeResource PdxCardStrokeBrush}"
                 BorderThickness="1" CornerRadius="8" MinHeight="56" Padding="16,12" IsClickEnabled="False">
    <TextBlock Text="{x:Bind Value, Mode=OneWay}" MaxWidth="{x:Bind MaxValueWidth, Mode=OneWay}"
               TextWrapping="NoWrap" TextTrimming="CharacterEllipsis"
               ToolTipService.ToolTip="{x:Bind ToolTipText, Mode=OneWay}"
               AutomationProperties.AutomationId="{x:Bind ValueAutomationId, Mode=OneWay}"
               AutomationProperties.Name="{x:Bind ValueAutomationName, Mode=OneWay}" />
</ct:SettingsCard>
```

- 好处：`AutomationId` 全域不变、`InstallModeUiChecks` 里断言 `Value="{x:Bind ViewModel.InstallModeText, Mode=OneWay}"` 的 2 条**继续通过**（文本落在 wrapper 的 `Value` 属性上）、无需改 20 处调用点。
- 备选（Stage 3 之后可选）：全部内联成 `ct:SettingsCard` + `TextBlock`，把 `SettingsValueCard` 也删掉。代价：20 处调用点要改，`InstallModeUiChecks.cs:151,167` 两条断言要跟着改（`Value="{x:Bind …}"` 变成 `Text="{x:Bind …}"`）。**收益小、风险大，建议不做。**

### 5.3 主题画刷与 4px 网格

- 画刷：继续用 `App.xaml` 里现成的 `PdxCardBackgroundBrush` / `PdxCardStrokeBrush` / `PdxCardHoverTintBrush` / `PdxCardHoverStrokeBrush`（三套主题字典都有），通过 `{ThemeResource …}` 引到六件套里。**不要**在 App.xaml 里新增 `SettingsCard*` 键（§2 实验 B 已证伪）。
- 圆角：本仓从未覆盖 `ControlCornerRadius`（全仓 grep 无匹配），官方卡默认吃 4；六件套显式 `CornerRadius="8"` 覆盖它。
- 网格：`MinHeight=56`、`Padding=16,12`、`CornerRadius=8`、`BorderThickness=1` 全部是 4 的倍数/允许值，`SpacingChecks` 保持绿。

### 5.4 About 段的 `SettingsExpander`

```xml
<ct:SettingsExpander Header="{x:Bind L.SettingsAboutTitle, Mode=OneWay}"
                     Description="{x:Bind ViewModel.VersionText, Mode=OneWay}"
                     IsExpanded="False">
    <ct:SettingsExpander.Items>
        <!-- 版本与安装模式 5 张卡（含 4 张 value 卡）+ 更新 3 张卡 -->
    </ct:SettingsExpander.Items>
</ct:SettingsExpander>
```

- 折叠子项**只叠一层**（官方明确要求），子项里不要再放 `SettingsExpander`。
- `SettingsExpander` 的外观覆盖**本次未实测**（只验证了它能渲染、`rect=1051x106`）。它派生自 `Expander`，`Background`/`BorderBrush`/`BorderThickness`/`CornerRadius` 是否同样走 `TemplateBinding` 未逐项验证 → **Stage 4 开工前先做一次单控件实测**；万一不生效，退化为"About 段继续平铺 `SettingsCard`"，不阻塞其他阶段。

## 6. AutomationId 与 UIA 脚本

- **19+ 个 AutomationId 全部在卡的内部控件上**（`ToggleSwitch`/`ComboBox`/`TextBox`/`Button`/`TextBlock`），不在卡本身；替换卡外层不影响这些 id。探针已验证：插桩后 `txtThemeSource`、`tglAlwaysOnTop` 等照常 FOUND。
- `scripts/verify-settings-sections.ps1` 的 8 节 id 清单（appearance 10 个、launch 3、paths 10、online 3、cache 8、advanced 2、translation 16、about 6，合计 58 个）**不需要改**：全部按 `AutomationId` 查找，不按 `ClassName`/`ControlType`。
- 会变的只有 UIA 树形态：卡从 `UserControl`（ControlType=Custom）变成 `ButtonBase`（ControlType=Button）。实测 `types=Button=10→14`。当前三个脚本里按 `ControlType` 过滤的地方只用 `ListItem`（导航项）和 `ClassName='ScrollViewer'`（滚动条），**不受影响**。若将来写"按 ControlType=Custom 找卡"的断言会红——不要那样写。
- `verify-settings-regression.ps1` / `verify-playset-twocolumn.ps1` 只操作导航 `ListItem` 与 Playset 列表，与卡片无关。
- 建议在 Stage 2 完成后给 `verify-settings-sections.ps1` 增加一条"每节卡片数量"断言（可选），用来锁住"每节 4–5 项"的成果。

## 7. i18n

- 文案源在 `src/PdxLauncher.App/Strings/<lang>/Resources.resw`（`en-US` / `zh-CN`），编译期由 `PdxLauncher.Localization.Generator` 生成 `Localizer` 属性；`settings` 区域当前**两种语言各 461 个键**。
- 已有可复用键：`settings.nav.*` 8 个（导航项）、`settings.section.*` 11 个（`theme`/`interface`/`launch`/`data`/`appData`/`proxy`/`online`/`cache`/`modThumbnails`/`danger`/`translation`/`name`）。
- **Stage 0–3 不增删任何键**（只换控件）。
- **Stage 4 需要新增**：把 8 节拆成约 18 个分组标题，除现有 11 个 `settings.section.*` 外，预计新增 **6–8 个**（如 `settings.section.window`、`settings.section.steam`、`settings.section.keys`、`settings.section.scope`、`settings.section.test`、`settings.section.install`、`settings.section.update`），两种语言的 Resources.resw 各补一份，然后 `I18nChecks`（钉住两种语言资源名一致 + 含真实换行的键两语言都有）必须变绿。
- 键名规范沿用现有 `settings.<区>.<项>` 点分小驼峰。

## 8. 自检面（`tests/PdxLauncher.Checks`）

| 检查类 | 会不会红 | 说明 / 改法 |
| --- | --- | --- |
| `SpacingChecks` | **不会**（只要守 4 倍数） | 扫所有 XAML 的 `Padding`/`Margin`/`Spacing`/`CornerRadius`，六件套 `16,12`/`8` 合法；`MinHeight` 不在扫描范围。若有人写 `CornerRadius="6"` 或 `Padding="18,12"` 立刻红 |
| `I18nChecks` | **不会**（Stage 0–3）；Stage 4 加键后要同步补两种语言的 Resources.resw | 它钉住 resw 两种语言资源名一致、以及那些**含真实换行**的键在两种语言都存在。新增的分组标题是单行文案，天然不触发 |
| `InstallModeUiChecks` | **不会**（若按 §5.2 保留 wrapper）；**会红 2 条**（若内联 value 卡） | `:151` 断言 `Value="{x:Bind ViewModel.InstallModeText, Mode=OneWay}"`、`:167` 断言 `Value="{x:Bind ViewModel.InstallModeDataDirectory, Mode=OneWay}"`。保留 wrapper 即无需动 |
| `LocalModelUiChecks` | 不会 | 断言的是行内按钮的 `AutomationId` 与 `Click=` 处理器名，卡外层不参与 |
| `AccountPageLayoutChecks` | 不会 | 断言的是账号页内容列 `MaxWidth 880` 与 2 个开关的 `OnContent=""`，与设置页卡片无关 |
| `DiagnosticsPageChecks` | 不会 | 断言 `navDiagnostics` 在 `navSettings` 之前（`MainWindow.xaml` footer 顺序），Stage 0–4 都不动它 |
| `SettingsChecks` | 不会 | 纯 `AppSettings` 存取往返，与 UI 无关 |
| `ConfirmOverlayChecks` | 不会 | 只看 `SettingsPage.xaml.cs` 里的确认对话框调用点 |
| `SpacingChecks` 文档断言 | 不会 | 要求 `docs/UI-SPACING.md` 含 `content-basics`、`4epx`、`36epx` |
| **无检查类** | — | 现在**没有任何**检查断言 `controls:SettingsCard` 文本或卡片数量（已 grep 全仓确认）。所以"换控件"本身不会触发结构断言失败 |

> 结论：只要采用 §5.2 的 wrapper 方案，**现有自检一条都不用改**；这也是推荐该方案的主要理由。

## 9. 分阶段计划

每阶段都能独立构建、独立验证、独立合并（`git switch -c` 后每阶段一个 commit，出问题可直接 revert）。

### Stage 0：装包 + 资源合并验证（0.5 人日）

1. `csproj` 加 `<PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" Version="8.2.251219" />`。
2. 联网 warm-up 一次：`dotnet restore src\PdxLauncher.App\PdxLauncher.App.csproj -s https://api.nuget.org/v3/index.json -p:Platform=x64`。
3. 验证"之后仍能离线构建"：断网/不带 `-s` 再 `dotnet restore` 一次应成功（包已在全局缓存）；把这 5 个包的缓存目录记录到 `docs/DEVELOPMENT.md`（换机器要复制）。
4. 合并前**不要**写任何 XAML，只验证构建与 publish 带上资源。

验收命令：

```powershell
dotnet build PdxLauncher.sln -c Release -m:1
dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll
.\publish.cmd
# 断言：publish 目录有 5 个 CommunityToolkit.*.dll；PdxLauncher.App.pri 字节数 > 2 290 728
```

### Stage 1：单节试点（外观节）（1 人日）

1. 只改 `secAppearance`（`SettingsPage.xaml:137-250`，11 张卡 = 6 张 `SettingsCard` + 5 张 value 卡）。
2. 5 张 value 卡走 §5.2 的 wrapper 改法——注意这**一次改动会让全部 20 张 value 卡同时换成官方卡**（其余 15 张在其他节里也会变）。这是有意的：value 卡结构单一（Header + 单个 TextBlock），一次换完比分批更安全，且调用点零改动。6 张普通卡按 §4 手改，作为脚本的"黄金样本"。
3. 视觉验收：与现状截图逐项比对——卡高、圆角、描边、底色、`ToggleSwitch` 的 `开/关` 文案是否仍在、`PdxDropDown` 宽度是否塌陷、长 `Description` 换行。
4. UIA 验收：`scripts\verify-settings-sections.ps1`。

验收命令：

```powershell
dotnet build src\PdxLauncher.App\PdxLauncher.App.csproj -c Release -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-settings-sections.ps1
```

### Stage 2：其余分节（1.5 人日）

1. 按 §4.4 脚本批量改剩余 41 张卡（`secLaunch`/`secPaths`/`secOnline`/`secCache`/`secAdvanced`/`secTranslation`/`secAbout`）。
2. 目视巡检：优先看 §4.3 的 7 类特例（模板内 2 张、`StackPanel` 11 张、`Visibility` 8 张、`PasswordBox` 3 张、`PdxDropDown` 10 张、`Margin/ToolTip` 3 张、`Description` 4 张）。
3. `DiagnosticsPage.xaml`（9+1 张）与 `AccountPage.xaml`（1 张）同法处理，或另开一个 commit（它们不属于"设置页"，但换掉才能删自绘控件）。

验收命令同 Stage 1 + `scripts\verify-settings-regression.ps1`。

### Stage 3：清理（0.5 人日）

1. 确认全仓无 `controls:SettingsCard` 引用：`Select-String -Path src\**\*.xaml -Pattern 'controls:Settings(Card|ValueCard)'`。
2. 删除 `Controls\SettingsCard.xaml(.cs)`；`publish.cmd:72-76` 的校验清单**不含** `Controls\SettingsCard.xbf`，无需改动（`PdxLauncher.App.csproj` 的 publish 目标是 `Controls\*.xbf` glob，删文件后自然不再打包）。
3. `Controls\SettingsValueCard.xaml` 保留（改成包 `ct:SettingsCard` 后它就是唯一薄壳）。若要连它也删，按 §5.2 备选方案处理 20 处调用点 + 2 条断言（不推荐）。

验收：`build.cmd` 全绿 + `publish.cmd` 全绿 + `dist` 冒烟（开设置页、翻 8 个分节）。

### Stage 4：对齐官方新要求（1 人日）

1. 内容列：`ScrollViewer` 内套 `Grid MaxWidth="1040" HorizontalAlignment="Center"`。
2. 节标题：9 处 `SubtitleTextBlockStyle` → `BodyStrongTextBlockStyle`（二级标题把 `SettingsSubsectionTitleStyle` 的 BasedOn 一并降级 + 上间距 16）。
3. 分组：按 §3 的表把 6 个大节拆成 2–4 组，每组 3–5 项；补 6–8 个 i18n 键并同步两份 Resources.resw。
4. About：`secAbout` 改为单个 `ct:SettingsExpander`（先做单控件外观实测，见 §5.4）。
5. 可选：主题来源 2 选 1 改 `RadioButton`。

验收命令：

```powershell
build.cmd
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\verify-settings-sections.ps1
# 目视：1440x900 与 2560x1440 两个窗口下，内容列宽度 1040 且居中；每节滚动一屏内能看到 4-5 项
```

## 10. 风险

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 外观口径（更深 / 更矮 / 8 圆角）只能靠每卡本地属性维持，任何"全局换肤"的直觉写法都会崩（`BasedOn` / 资源键别名） | 高：新同学很容易踩回实验 A/B 的坑，且崩法表现为"设置页打不开、静默回主页"，难排查 | 本文 §2 写死结论；在 `App.xaml` 顶部加注释警告；Stage 1 后把"设置页能打开 + 8 节 id 齐全"的探针加进本地验收清单（CI 无桌面会话，不进 CI） |
| `SettingsExpander` 的外观覆盖未实测 | 中：About 段可能深浅不一 | Stage 4 前单控件实测；失败则退化平铺，不阻塞 |
| 拆节（4–5 项）+ 内容列 1040 会改变视觉节奏，用户已熟悉的"一屏一节"变成"多组滚动" | 中：返工 | Stage 4 独立成阶段，可单独回退；拆分方案先出截图给用户确认 |
| 自绘控件删除顺序错了（先删 `SettingsCard` 再改 `SettingsValueCard`） | 中：编译直接红，不会悄悄坏 | Stage 3 才删；删前 grep 引用数为 0 |
| 离线构建承诺被打破（新包不在 5 台开发机的缓存里） | 中：同事拉分支后 restore 失败 | Stage 0 把"warm-up 一次 + 记录 5 个缓存目录"写进 `docs/DEVELOPMENT.md` |

## 11. 附：本次验证的临时物与清理

- 全部临时探针已回滚：`PdxLauncher.App.csproj` 的 `PackageReference`、`App.xaml` 的三套主题别名与根级 `SettingsCardMinHeight`/`SettingsCardPadding`/`SettingsCardBorderThickness`/洋红刷、`SettingsPage.xaml` 的 `xmlns:ct` 与两段 PROBE 块。
- 回滚后 `dotnet build src\PdxLauncher.App\PdxLauncher.App.csproj -c Release -p:Platform=x64 -m:1 -t:Rebuild` → 0 警告 0 错误。
- 探针脚本与截图留在 `%TEMP%\pdx-nuget-probe\`（不入库）：`probe-uia.ps1`、`probe-rect.ps1`、`shot-build2.png`、`shot-publish.png`、`shot-keys.png`、`shot-magenta.png`、`card-localprops.png`。
- `git status` 相对 HEAD 只多出本文件；其余是**先于本任务**的既有改动，未做任何回滚：`docs/HANDOFF.md`、`docs/SETTINGS-REWORK.md`（修改）、`docs/CONTROLS-UPGRADE-REPORT.md`、`docs/CONTROLS-CHANGE-SPEC.md`（未跟踪）、`Views/SettingsPage.xaml` 两行注释（滚动条通道 14→16 DIP）、`Views/SettingsPage.xaml.cs:139` 注释（六分类→八个分节）、`Core/Storage/LegacyCacheCleaner.cs:9` 注释（六张→七张缓存卡片）。这 3 个源文件共 4 行 `+/-`，全是注释。
