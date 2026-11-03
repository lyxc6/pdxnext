# mod 自动排序说明（2026-11-03）

按「实际文件改动足迹」为数百 mod 的播放集自动排加载顺序：扫描每个 mod 的文件清单，
对照原版内容分类（纯新增 / 小改 / 大改 / 大型重构 / 仅美化 / 本地化），结合声明依赖与
补丁命名推导约束边，再用 Kahn 拓扑 + 层位优先队列出序。应用前有差异预览，应用后可一键撤销。

排序语义与社区规则一致：**后加载者胜**（同文件被多个 mod 提供时播放集更靠下者生效），
**大改在上、小改与补丁在下、翻译最后**（参考 CK3 社区惯例与官方 wiki 的 load order 说明）。

## 命令入口

播放集页工具条按钮（`src/PdxLauncher.App/Views/ModsPage.xaml`）：

- **自动排序**（`btnAutoSort`）：内存计算 → 差异预览对话框 → 用户确认后应用。
  顺序没有变化时只弹「无需调整」，不进预览。
- **取消排序**（`btnAutoSortCancel`）：扫描期间才出现，取消后状态栏写「已取消自动排序」。
  它与页面生命周期共用令牌：离开页面同样会停下扫描。
- **撤销排序**（`btnUndoAutoSort`）：应用后出现，一键还原排序前顺序。
  快照只在内存，切换/重载播放集即作废（避免还原到别的播放集的顺序）。

扫描进度写在状态栏（建立原版索引 → 逐个 mod → 计算冲突与顺序），可见的等待不再是无反馈的卡住。
未选播放集时直接提示「未选择播放集」，而不是拿空列表算出「无需调整」。

预览会记下生成时的播放集 ID 与输入指纹（`PlaySetAutoSortService.ComputeInputSignature`：
ModId + 位次 + 启用态）。确认对话框开着时列表若被自动保存/刷新改过，应用会被拒绝并提示重新排序，
避免把过期的重排结果整表覆盖到新列表上。

应用与撤销都走 `PlaySetEditor.Renumber` + `RequestAutoSave` 自动保存通道，与其他行序改动一致。

## 排序规则

### 1. 层位（软排序）

`FootprintClassifier.Classify`（`src/PdxLauncher.Core/Mods/Sorting/FootprintClassifier.cs`）按足迹定层，
`TierRank` 给出先后（数字小的先加载）：

| 层位 | 判定 | TierRank |
| --- | --- | --- |
| 大型重构 Overhaul | 覆盖 ≥200 个原版玩法文件，或 ≥1000 文件且覆盖 ≥50 | 0（不因名称降级） |
| 大量修改 LargeChange | 覆盖 51–200 | 1 |
| 中量修改 MediumChange | 覆盖 11–50 | 2 |
| 仅美化/界面 GfxOnly | gfx 占比 ≥80% 且 0 覆盖 | 3 |
| 纯新增 PureAdditive | 0 覆盖 | 4 |
| 小幅调整 SmallTweak | 覆盖 1–10 | 5 |
| 补丁/兼容（名称标记） | 名称含 Compatch/Compatibility/Patch/Fix/Translation/Localization 或 兼容/补丁/汉化/翻译/中文化 | 6 |
| 本地化 Localization | localization 占比 ≥80% | 7 |

补丁标记只在**非 Overhaul** 上生效：一个覆盖两百多个玩法文件的大修不会因为名字里带
「Patch」就整包沉到微调后面。中文标记按子串匹配（中文没有词边界），拉丁标记按整词匹配——
`Suffix`、`Prefix` 里的 `fix` 不算补丁，`Fix Pack` 才算。

玩法目录 = `common/events/history/gui/map_data/data_binding`；原版文件清单 = `<安装根>/game` 全部相对路径。

### 2. 硬约束（SortEdge，压过层位）

- **声明依赖**（`SortEdgeKind.DeclaredDependency`）：依赖方先加载。
  来源二：launcher-v2.sqlite 的 `mods_dependencies` 表（`PlaySetRepository.ReadModDependencyEdges`，
  只读打开、缺表/坏行降级为空）；descriptor.mod 的 `dependencies = { workshop_item_id = ... }`
  （`DescriptorDependencyParser.ParseSteamIds`，按 SteamId 匹配已装 mod）。
- **衍生补丁**（`SortEdgeKind.DerivedPatch`）：两 mod 共带新增文件、恰一方名称带补丁标记时，
  基础 mod 先加载（`ConflictGraphBuilder`）。

### 3. 冲突图（不产生边，只进预览警示）

- `SharedAddedFileAmbiguous`：共带新增文件但无法判定谁是基底（对等），保持现有相对顺序。
- `VanillaOverrideSameTier`：同层位两 mod 覆盖同一批原版文件，后加载者生效，预览提示。

### 4. 出序

`PlaySetAutoSorter.Sort`：Kahn 拓扑 + `PriorityQueue<(TierRank, 原下标)>`；
依赖成环时强制弹出剩余最优节点并记 `DependencyCycleBroken` 警告（警告带涉及的 ModId，
预览会拼成「涉及 N 个 mod：甲、乙…」，用户才能自己去拆环）。幂等：对结果再排序不变。

预览对话框对行数设上限（移动 60 行、冲突 30 条），超出折成「…另有 N 项未显示」——
数百 mod 全量塞进不可虚拟化的 `StackPanel` 会造出上千个控件。

## 性能

每次点击都即时计算（原版索引 + 全部 mod 扫描 + 冲突图），不缓存。`BuildPreview` 会按阶段
（原版索引 / 扫描 / 建图 / 排序）记 `Info` 耗时日志，换机器或 mod 数量变化后先看这行再决定要不要优化。
冲突图建图是两两交集（O(n²) 对）；三百量级的实测在可接受范围内，若日志显示建图占主导，
下一步是把「路径 → mod」倒排后再取交集。

## 实现结构

```
src/PdxLauncher.Core/Mods/Sorting/          纯排序引擎（不依赖 App/UI）
  ModFootprint.cs                            足迹（覆盖/新增/替换计数 + 路径集合）
  ModFootprintScanner.cs                     扫描目录/zip；归一化、replace/ 折叠、垃圾剔除、包装目录探测
  FootprintClassifier.cs / ModKind.cs        分层分类
  ConflictGraphBuilder.cs / ConflictGraph.cs 冲突图 + 衍生补丁边
  PlaySetAutoSorter.cs                       Kahn + 优先队列出序
  SortEdge.cs / DescriptorDependencyParser.cs
src/PdxLauncher.App/Services/
  PlaySetAutoSortService.cs                  编排：原版清单 + 扫描 + 依赖边 + 出序 + 重排行
  AutoSortPreview.cs / AutoSortDiff.cs       预览产物 / 差异行（移动原因映射）
  Views/AutoSortPreviewDialog.cs             预览对话框（ConfirmOverlay）
tests/PdxLauncher.Checks/
  FootprintClassifierChecks / DescriptorDependencyParserChecks /
  ModFootprintScannerChecks / SortConflictGraphChecks /
  PlaySetAutoSorterChecks / AutoSortDiffChecks / PlaySetAutoSortServiceChecks
```

## 已知局限

- 脚本内部引用（effect/事件互调）不解析，只看文件级足迹；层位与冲突提示供人工复核。
- 补丁识别依赖名称标记（Compatch/兼容/补丁 等），漏标时退化为层位/共享文件规则。
  拉丁标记按整词匹配，因此「Fixer」「Prefix」不会误判；代价是「X-Patch」这类连字符写法也命中不了。
- 压缩包 mod 只看 zip 条目名，无法看 replace/ 语义以外的内部差异。
- 原版清单取当前安装根；换了游戏版本需重新扫描（每次排序即时计算，无缓存）。
- `mods_dependencies` 当前各游戏多为空表，声明依赖主要来自 descriptor；随官方数据增长自动受益。
- 自我覆盖：一个 mod 若既声明依赖某 mod、又与它共享新增文件，两条边方向一致时无害；
  成环时排序仍会给出结果，但被断开的边是静默忽略的（预览的警告只列参与环的 mod，不列具体边）。
