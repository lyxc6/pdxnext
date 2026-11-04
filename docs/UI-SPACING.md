# UI 间距规范（WinUI 3 · 4px 网格）

> 适用：`src/PdxLauncher.App` 下的所有 XAML 与手写 UI 代码。
> 检查：`tests/PdxLauncher.Checks/SpacingChecks.cs`（每次自检都会跑，违规直接失败）。
> 规范地位：本文是项目规范的一部分（`docs/DEVELOPMENT.md` §8），新增 UI 必须遵守。
> 本文最后更新：2026-11-04（全量归一化，见 §5）。

## 1. 官方依据

- WinUI Gallery 的 **Spacing** 页：<https://github.com/microsoft/WinUI-Gallery/blob/main/WinUIGallery/Samples/Spacing/SpacingPage.xaml>
  （Gallery 里代码在 `WinUIGallery/Samples/Spacing/`；旧文档链接 `winui3gallery://item/Spacing` 指的就是它）
- 同一份表述另见 Microsoft Learn：<https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics>

原文规则（一句话）：

> A best practice in design is to use a 4px grid. This means that any spacing or sizing should be a multiple of 4.

`epx` 就是 WinUI 3 XAML 里的 **DIP**。换算到 150% 缩放：1 DIP = 1.5 物理像素，4 DIP = 6 px。

## 2. 官方常用值（Gallery 页面上的表）

| epx | 官方说明 | 对应到本启动器 |
| --- | --- | --- |
| `4epx` | Spacing used for compact sizing. | 紧凑堆叠（标题 + 副标题、行内小元素） |
| `8epx` | Spacing between UI controls, control + label. | 同一排按钮、控件与它的标签、按钮簇折行间距 |
| `12epx` | Spacing between control + header, surface and edge text, text sections. | 小标题与正文、面板内的文本段 |
| `16epx` | Padding used in list styles, cards. | 卡片内边距、列表行内边距 |
| `24epx` | Spacing between content sections. | 页面里大块内容之间 |
| `36epx` | Padding on pages. | 宽松页面的页边距（本项目不采用，见 §3） |
| `48epx` | Spacing between page sections with title. | 带标题的页面分区（本项目未用到） |

`4 / 8 / 12 / 16 / 24 / 36 / 48` 是**常用值**，不是白名单：`20 / 28 / 32 / 40` 等 4 的倍数同样合法。

## 3. 本项目规则

1. **间距必须是 4 的倍数**：`Padding`、`Margin`、`Spacing`、`RowSpacing`、`ColumnSpacing` 的**每个分量**分别判断
   （`Padding="8,4"` 合法；`Padding="6,1"` 不合法）。
2. **页面级内边距**沿用 `16 / 20 / 24` 三档（都是 4 的倍数）：列表型密集页面 16，设置 / 性能页 20，宽松页面 24。
   **不照搬官方 `36`**：桌面工具在 1040 宽的最小窗口下，36 的页边距会明显压缩列表与工具条，收益为负。
   想要更宽松的版本，只需改各页根 `Grid` 的 `Padding`，不是破坏规则。
3. **圆角**只允许 `0 / 4 / 8`（Gallery 页同一段里说「这些值对应我们的圆角逻辑」）：卡片 / 面板 8，小控件 4。
4. **1px 细线**（分隔线、下划线）用 `Width` / `Height` 表达，不写成 `1` 的间距；线宽同理。
5. **`BorderThickness` 不受此规则约束**：它是描边宽度（如 Markdown 引用块左侧 `3,0,0,0` 的竖线），不是间距。
6. **表达式型 `Thickness` 不强制**（如 `new Thickness(16 + (item.Level * 20), 0, 0, 0)`），新增时必须人工确认每个常量是 4 的倍数。

## 4. 检查器

`SpacingChecks` 会：

- 扫描 `src/PdxLauncher.App/**/*.xaml`（跳过 `obj/`、`bin/`，先剥掉 `<!-- -->` 注释）；
- 扫描 `src/PdxLauncher.App/**/*.cs`：`Spacing = 数字`、全数字字面量的 `new Thickness(...)`、`new CornerRadius(...)`；
- 失败信息格式：`第 N 行 Padding="6,1"`，直接指到行；
- 同时断言本文存在且含官方依据关键字，防止文档被误删。

本地运行：

```bat
dotnet build "PdxLauncher.sln" -c Release -m:1
dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll
```

输出里应有 `=== SpacingChecks ===`，且 `间距 XAML：非 4 倍数总数为 0`、`间距 C#：非 4 倍数总数为 0`。

## 5. 2026-11-04 全量归一化记录

审计结果：XAML 15 个文件 **140 处**非 4 倍数，C# **11 处**，`CornerRadius` **23 处**（`6`×22、`3`×1）。

| 属性 | 归一化（原值 → 新值） |
| --- | --- |
| Spacing | `2→4`、`6→8`、`10→12`、`21→24` |
| RowSpacing | `10→12` |
| ColumnSpacing | `10→8`、`6→8` |
| Padding | `6,1→8,4`、`10,6→12,8`、`4,6→4,8`、`8,3→8,4`、`10→12`、`6,4→8,4`、`16,10→16,12`、`10,4→12,4`、`11,0,8,0→12,0,8,0`、`10,8→12,8`、`12,10→12,12`、`6,2→8,4`、`12,8,12,10→12,8,12,12`、`2,0→4,0`、`0,6→0,8`、`18→16` |
| Margin | `0,0,14,0→0,0,16,0`、`0,6,0,0→0,8,0,0`、`0,2,0,2→0,4,0,4`、`0,10,10,0→0,12,12,0`、`6→8`、`0,0,0,2→0,0,0,4`、`2,0,2,1→0`、`2→0`、`2,0→0` |
| CornerRadius | `6→8`、`3→4` |

三处结构性处理（只是换数字不够）：

1. 播放集工具栏分隔线：`PdxWrapPanel` 不做交叉轴居中，原来的 `Height=20 + Margin="1,6"` 是「20 高竖线在 32 高行里居中」的补偿值。
   改成 `Width=1 Height=32 BorderThickness="1,0,0,0"` 的满行高竖线，间距归零。
2. 主页 1px 装饰下划线 `Margin="2,0,2,1"` → `0`（1 是 1px 线的光学补偿，不是间距）。
3. 播放集「位置」输入框 `Padding="2,0"` → `"4,0"`（`MaxWidth=40`，4 位数仍放得下）。

C# 侧：`MarkdownView` 的块间距 / 标题间距 / 段落 / 列表 / 代码块 / 引用块 / 分隔线共 9 处，
`UpdateFlow` 的进度面板 `Spacing=10→12`，`AutoSortPreviewDialog` 的 `Spacing=2→4`。

## 6. 新增 UI 的检查清单

- [ ] 新写的间距值都是 4 的倍数（优先用 4 / 8 / 12 / 16 / 24）
- [ ] 圆角只用 0 / 4 / 8
- [ ] 需要「看起来居中」的细线，用 `Height` + 父容器 `VerticalAlignment`，不要用 margin 补偿
- [ ] 跑一遍 `SpacingChecks`（构建后自检即是）
