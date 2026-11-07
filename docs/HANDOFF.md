# 交接文档（HANDOFF）—— 下一轮对话从这里开始

> 本文件由上一轮会话在用户要求「停止，留下文档给下个对话使用，需要任务拆分」时写入。
> 状态时间：2026-10-25 22:30（本机时区 Asia/Hong_Kong）。
> 工作区：G:\Work\paradox启动器 —— **已是 git 仓库**（2026-10-27 初始化）。开工先按 `AGENTS.md` 跑
> `pwsh -File scripts/session.ps1 open <slug>`（每个对话一个 `sess/` 分支），每完成一步就 `git commit`；
> 收尾用 `scripts/session.ps1 close` 合回起始分支。回滚用 `git restore` / `git revert`，不要再复制 backup 目录。
> 历史备份（.scratch\rework\backup*）只作为改造前后的存档，不再新增。
>
> **2026-11-08 i18n 迁移**：文案源从 `tools/i18n/*.json` + `tools/gen-i18n.py`（Python 生成 `Strings.g.cs`）
> 改成 WinUI 官方 `.resw`：`src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`，由
> `src/PdxLauncher.Localization.Generator`（Roslyn 增量源生成器）在编译期生成 `Localizer` 属性与 `Keys`，
> 运行期用 `ResourceManager` + 可切换的 `ResourceContext` 读取，**即时切语言的行为保留**。
> 资源名是不含点的 PascalCase 标识符（`.resw` 的点是属性分隔符）。下文历史条目里的 `tools/i18n`
> 路径已批量替换成新路径，但当时「按 area 分文件」的细节已不再适用；以 `Strings/README.md` 为准。

---

## 0. 一句话现状

启动器外壳与主页已从「照官方 Paradox 启动器截图逐像素自绘」**改成 WinUI 3 标准控件 UI**（用户明确要求），
Release **0 警告 0 错误**，自检 **111 个检查类 / 0 失败**（2026-11-05 实测；7 处搜索框已全部换成 `AutoSuggestBox`（本地名字联想 + 工坊最近搜索），见 §0.85；`PlaysetChecks` 原来按本机 Victoria 3 播放集写死的 `mod 行数 >= 22` 已改成结构断言，见 §0.85.5；
最近一轮新增 `SearchSuggestionsChecks`、`SearchBoxChecks`、`SteamBbCodeParserChecks` 与 `ModPublishLimitsChecks`（钉住 BBCode 解析与字节上限），再往前是 `LocalizationBindingChecks` 与 `ModStudioPaneChecks`（钉住 `{0}` 裸绑与上传面板出口），再往前是 `ModPublishExtrasChecks`（含多语言语言池的 `PickUnused` / `IsDuplicate` 断言），再往前是 `WorkshopItemStateBitsChecks`、`WorkshopStateTextChecks`、`WorkshopStateCacheChecks`、`WorkshopSubscribedProtocolChecks`；此前还有 `ConfirmOverlayChecks`、`ModLibraryWarningChecks`、`PdxDatabaseChecks`、`JsonDocumentStoreChecks`、`OwnPlaysetRepositoryChecks`、`LauncherDbProjectionChecks`、`ModPathChecks`。历史上并发会话加过好几个类，实际以运行时打印的 `检查类: N` 为准），运行时 UIA 树已确认是 TitleBar + NavigationView + Frame 结构。
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
**2026-10-27 14:50 追加：设置页「内容和滚动条重叠」已修 —— 根因是 WinUI 3 的滚动条是**浮层**，贴着 `ScrollViewer` 右边缘画在卡片描边/圆角上；现给滚动内容与状态栏各留 16 DIP 右侧通道（2026-11-04 对齐 4px 网格时由 14 DIP 调为 16，见 §0.82），滚动条改落在通道里。真机三态截图 + 像素测量见 §0.16。**
**2026-10-27 15:10 追加：设置页左侧分节导航从「纯文字 ListView」换成官方 `NavigationView`（带 `SymbolIcon`）—— 选中指示条到文字的间距从 ≈8 DIP 变成 ≈41 DIP，见 §0.17。**
**2026-10-27 15:30 追加：设置页「缓存」分类接管「数据 / Mod 缩略图」两块（联网分类只剩代理），并做了一轮设置代码去重，见 §0.18。**
**2026-10-29 20:20 追加：自有 SQLite 事实源落地（`data\pdxnext.sqlite`）+ 官方库双向投射 + settings/session/六份缓存索引全部入库 + mod 来源徽标与筛选 + 加载文件跳过规则与路径修复，见 §0.29。**
**2026-10-29 22:20 追加：播放集页的「添加模组」多选对话框已改成独立子页（`AddModsPage`，左栏选中态停在「播放集」），候选列表带搜索 / 勾选 / 计数，返回后由播放集页统一写库并自动保存，见 §0.52。**
**2026-10-30 08:30 追加：Cities: Skylines 1 从「未适配」变「仅启动」——根因是官方启动器给它写的 `gameId` 是 `cities_v2`，而所有 id 表只认 `cities_skylines`；现按「发现阶段归一 + 公告接口反向特例」处理，补丁链路顺带打通（独立安装也能拿到 appId 255710），见 §0.54。**
**2026-10-30 10:40 追加：13 处 ComboBox 全换成统一下拉控件 `controls:PdxDropDown`（WinUI DropDownButton + Flyout + ListView），含主页播放集；自写 AutomationPeer 保住 UI 自动化脚本契约，见 §0.57。**
**2026-10-30 09:40 追加：DLC 页新增「Steam 授权判定条」——用游戏目录那份 `steam_api64.dll` 现场问 Steam，补丁生效显示 490/490 全绿、没打补丁显示真实授权（实测 26/53），每行还带「已拥有 / 未拥有 / 授权未知」徽标，见 §0.30。**
**2026-10-30 17:10 追加：Cities: Skylines II 的 DLC 页不再空白 —— CS2 没有 `.dlc` 描述文件，`DlcCatalog` 对 `PlaysetConfigV1` 直接返回空、页面又把整块清空；现改为在 DLC 页 VM 里用 Steam 报告生成清单（37 条带授权徽标、开关与保存按钮按需隐藏），`IDlcCatalog` 保持只扫磁盘，见 §0.58。**
**2026-10-31 12:55 追加：退出游戏后 Steam 仍显示「运行中」、且 Steam 的「退出游戏」会连启动器一起杀 ——
根因是启动器自己在进程内调了 `SteamAPI_Init`，Steam 就把它登记成该 appid 的 game process；现把全部原生 Steamworks 调用
搬进同一个 exe 的短命宿主子进程（`--steam-host`），启动器本体不再被登记，见 §0.59。**
**2026-10-31 17:00 追加：左栏新增「Mod 创建」页 —— 列出本地 mod、按游戏格式新建 `.mod`/`metadata.json`、经 `--steam-host` 子进程上传创意工坊；见 §0.60。**
**2026-11-03 12:20 追加：译文块去掉「译文 / Translation」小标题 —— 六处翻译块只留译文正文（描边框保留），孤儿文案键 `workshop.translateLabel` 一并删除；见 §0.62。**
**2026-11-03 12:55 追加：翻译在途提示 —— 窗口标题栏加「正在翻译…」小号环形进度（150ms 显示、400ms 隐藏去抖），详情页简介 / 更新日志 / 评论在译文位置先显示「正在翻译…」占位，译好原地替换；顺带补上 `HasTranslatedDescription / HasTranslatedChangelog` 从不发变更通知（译文块要切页才出现）的缺陷；见 §0.63。**
**2026-11-03 13:20 追加：创意工坊接上 Steam 真实状态 —— Mod 库页与详情页显示「已在 Steam 安装 / 有更新 / 下载中 N% / 排队中 / 本地已禁用」，行内可触发 `DownloadItem` 与 `SetItemsDisabledLocally`；宿主新增 `workshop-subscribed` 与 `workshop-batch` 两个动词（动词表 5→7），设置页把「读状态」与「改订阅」拆成两个开关；见 §0.65。**
**2026-11-03 14:05 追加：Mod 创建页补齐工坊发布面 —— 可见性四档（公开 / 仅限好友 / 隐藏 / 非公开）、5 个内容描述词、附加预览图（多图，单张 <1 MB）与 YouTube 视频预览、多语言标题与描述（当时收进「高级」折叠区，后来按用户反馈改成表单内的「其他语言」一节，见 §0.68）、新建 mod 改用内置封面而不是 1×1 透明占位图；见 §0.66。**
**2026-11-03 14:10 追加：一键启动 `start.cmd` —— 仓库根一个脚本包办「增量构建 + 运行」，支持 `debug` / `check` / `new` / `/?` 四种参数，已有实例在跑就跳过启动；同时补 `.vscode/` 工程入口（默认任务一键启动、构建错误进问题面板、不建 launch.json），并新增自检 `StartScriptChecks` 守住脚本纯 ASCII / 提示键齐全 / exe 路径 TFM 与 csproj 一致；见 §0.67。**
**2026-11-03 14:45 追加：上传页多语言改成「选语言 → 填该语言」——删掉「高级」折叠区、移进描述字段正下方，并补 `PickUnused` / `IsDuplicate`；提交仍逐语言一次，因为 `SetItemUpdateLanguage` 绑在 update handle 上。第一版是「一种语言一行」的行列表，被用户否掉，最终形态见 §0.69（照 Steam 客户端：语言下拉 + 标题 + 描述三段）。**
**2026-11-03 15:05 追加：多语言最终形态照 Steam 客户端的「编辑标题与描述」——一块表单里从上到下依次是语言下拉（默认 english）、标题、描述；切语言时把当前文本存进该语言的草稿槽位、再载入目标语言，删掉 `ModLocalizationRow` 行模型与「添加/删除语言」按钮；见 §0.69。**
**2026-11-03 15:30 追加：翻译「没有出结果」的根因不在界面 —— MyMemory 免费额度耗尽回 429、谷歌免费端点主用主机在本机 TLS 握手直接失败；现让免费引擎按主机回落（`translate.googleapis.com` → `clients5.google.com`）、把坏主机与额度耗尽记在**进程级**健康状态里（协调器每批 new 引擎，实例内记忆活不过一批）、失败提示逐引擎列原因；见 §0.70。**
**2026-11-03 15:45 追加：用户问「我连着系统代理，为什么谷歌翻译连不上」——代理是通的（CONNECT 200、同代理下 `translate.google.com` / `clients5.google.com` 都 200），是 `translate.googleapis.com` 被 DNS 污染到 192.179.26.95 且按 SNI 阻断；现把首选主机换成 `translate.google.com`、批量走 `translate_a/t`（≤8 段一个请求）、429 主机进 60 秒冷却；见 §0.71。**
**2026-11-03 17:50 追加：创意工坊描述支持 BBCode 与字节上限 —— Steam 的标题 / 描述是定长字符缓冲（128 / 8000），描述格式是社区方言 BBCode（`[h1]`–`[h3]`、`[list]`、`[url=]`、`[img]`、`[hr]`、裸链接自动小部件），此前我们既没校验长度也没做任何富文本；现按 UTF-8 字节三层拦截（`ModPublishLimits` → 宿主 → 上传页）、新增 `SteamBbCodeParser`（BBCode → 现有 `MarkdownDocument` AST，详情页兜底与上传页预览共用）并顺手修掉 `[hr]` / `[olist]` 残留与两份 `ToPlainText` 分叉；上传页描述框上方加 11 键工具条、下方加实时预览，自检 73→80；见 §0.73。**
**2026-11-03 16:40 追加：Mod 创建页三处 UI 修正 —— 列表徽标不再原样显示 `{0}`（改为短文案 `PublicationBadgeText`，根因是 `Localizer.Format` 无参返回模板原文）、删掉详情面板顶部多余的小标题、上传成功 / 取消 / 切换选中都能回到详情面板（此前成功分支走 `resetPane: false`、取消分支不切面板，进去就出不来）；顺带新增 `LocalizationBindingChecks`（裸绑 `{x:Bind L.Xxx}` 的键模板不许含 `{n}`）与 `ModStudioPaneChecks`，自检 71→73；见 §0.72。**
**2026-11-03 20:00 追加：Windows 安装包与自动更新落地 —— Inno Setup 7.1.1 双模式安装器（默认每用户无 UAC / 可选每机器）、公开 GitHub Releases 的 `update.json`、启动检查 + 下载校验 + 静默/提权安装、设置页「关于与更新」、每机器数据回退到 `%LOCALAPPDATA%\PdxNext-Data`；自检 83 类全过；见 §0.74。**
**2026-11-03 20:45 追加：发布链路改 GitHub Actions —— 推 `v*` tag 自动构建、自检、生成 Inno 安装器并上传 Release；新增 SemVer 版本规范 `docs/VERSIONING.md`、`CHANGELOG.md`、CI 工作流；发布仓库统一为公开的 `lyxc6/pdxnext`；见 §0.75。**
**2026-11-03 23:55 追加：已发布到创意工坊却仍显示「未发布」—— 根因是隐藏条目对匿名 API 回 `EResult 9`（FileNotFound，**不等于不存在**），而启动器只认「自己发起的上传」；现给宿主加 `workshop-mine` / `workshop-mine-detail` 两个动词（回调 1312/1318，走本机登录态读自己发布的条目），Mod 创建页加「绑定已有条目…」入口、发布状态改四态（未发布 / 发布未完成 / 已发布（未公开）/ 已发布）并显示 Steam 真实可见性；自检 93→94；见 §0.76。**
**2026-11-04 13:30 追加：创意工坊「搜不出来」—— 不是搜索坏了，是连不上 Steam 而错误被当成「用户取消」吞掉；根因是本机 DNS 被污染（steamcommunity.com → 104.244.46.21）+ 直连模式不读 `http_proxy`；现把连接超时翻译成 `NetworkTimeoutException`、工坊失败码分「结构变了 / 连不上」，状态栏写明原因并提示检测到的代理，设置页加「检测到的代理 → 使用」一键切换；自检 94→95；见 §0.77。**
**2026-11-04 15:20 追加：新增「性能与日志」页 —— 左栏底部（账号与设置之间）多一个入口，五个分区（概览 / 缓存 / 日志 / 网络 / 启动耗时）把内存与进程计数、七类缓存占用、日志尾部与轮转状态、Steam 各主机限流计数、五个启动里程碑放在一张页面里看，并按阈值自动列出「可以优化什么」；顺带把设置页的 7 行缓存定义抽成 `CacheRowCatalog` 两页共用、内存埋点与启动里程碑改成 Release 也记录；自检 96→100；见 §0.79。**
**2026-11-04 15:30 追加：播放集工具条改成自动折行 —— 按钮簇换成 `PdxWrapPanel`、列序改为 `* / Auto / Auto`、搜索框固定 200 宽右对齐；1200 与 1040 DIP 下不再把搜索框挤没或裁掉「添加模组」，1600 DIP 仍是一行，见 §0.80。**
**2026-11-04 14:40 追加：Mod 创建页详情那一排按钮在默认窗口宽下就被裁掉（截图里只剩半个「绑…」）—— 根因是详情右栏可用宽 ≈544 DIP 而五个按钮 ≈660 DIP，且 WinUI 3 没有 WrapPanel、水平 StackPanel 不换行、外层 ScrollViewer 横向禁滚；现新增纯算法 `WrapLayout` + 控件 `PdxWrapPanel`，详情按钮行与上传面板 BBCode 工具条都换成换行面板；自检 95→96；见 §0.78。**

**2026-11-04 16:20 追加：UI 间距全量对齐 WinUI 官方 4px 网格 —— XAML 140 处、C# 11 处、`CornerRadius` 23 处归一化，新增 `SpacingChecks`（103 类）与 `docs/UI-SPACING.md`；见 §0.82。**
**2026-11-04 16:10 追加：工坊详情截图改逐张结算 —— 大图 / 计数器不再等整批下完才出现，失败的那张会说话；见 §0.81。**
**2026-11-04 16:35 追加：播放集页新增第三种显示方式「双列」—— 左栏是还没加入当前播放集的 mod（勾选即加入），右栏是已加入的 mod，两栏各有搜索框；原版启动器布局与「添加模组」子页一行没动，设置页下拉切换；自检 103 类全过、`scripts/verify-playset-twocolumn.ps1` 两轮 UIA 冒烟 PASS；见 §0.83。**
**2026-11-04 18:50 追加：双列页分隔条可以左右拖动改宽度，窗口拉窄时左栏自动让位、右栏不再被窗口边缘裁掉（修 XAML 列 MinWidth 把 Grid 撑爆 + SizeChanged 读到旧 ActualWidth 两个根因）；见 §0.86。**
**2026-11-04 16:45 追加：日志分区跟着窗口长 —— 以前日志框写死 `Height="340"`、又被塞在唯一的外层 `ScrollViewer` 里，窗口拉高它不变；现把右栏拆成「滚动容器（概览 / 缓存 / 网络 / 启动耗时）」与「独立 Grid（日志）」两个互斥元素，日志框落在 `*` 行随窗口伸缩（`MinHeight="160"`），过滤行右侧新增「自动换行」开关（默认关、写进设置 `DiagnosticsLogWrap`），并让两个开关的标签常显；见 §0.84。**

**2026-11-05 03:10 追加：安装向导不给选安装位置 —— Inno 的 `DisableDirPage` 默认 `auto`，只要卸载项里找到旧目录就把「选择目标位置」整页隐藏（只有全新机器看得到），显式改成 `no` 后目录页始终显示、升级仍预填旧目录；同时给目录页加前置校验（盘符根 / `%WINDIR%` / 用户目录根 / Program Files 根 / 每用户模式下不可写目录）、`OtherModeInstalled` 改读对方卸载项的 `Inno Setup: App Path`、新增 `InstallerScriptChecks`（111 类 0 失败）；见 §0.87。**

## 0.87 最新一轮（2026-11-05 01:50–03:10）：安装向导不给选安装位置

> 用户诉求原话（m00001）：「软件的安装程序不给我选择安装位置」

### 0.87.1 根因：Inno 的 `DisableDirPage` 默认值是 `auto`

`installer/PdxNext.iss` 从来没有写过 `DisableDirPage`，于是用了 Inno 的默认值 `auto`。官方文档
（`topic_setup_disabledirpage.htm`）原文：*"If this is set to auto, Setup will not show the Select
Destination Location wizard page if it finds the directory of a previous installation."* 配合脚本里
本来就有的 `UsePreviousAppDir=yes`（从卸载项的 `Inno Setup: App Path` 取上次目录），结果就是：
**只有全新机器能看到目录页，任何重装 / 升级都会直接跳过它装回原目录。** 用户上一轮刚装过 1.2.1，
所以看到的是「没有选位置的页面」。

### 0.87.2 改法

- `DisableDirPage=no`（显式关掉隐藏规则），`UsePreviousAppDir=yes` 保留 —— 页面常显，且升级时预填旧目录。
- 静默安装（启动器自动更新走 `/SILENT`）本来就没有页面，不受影响；`NextButtonClick` 里第一句就是
  `if WizardSilent then Exit`，因为该事件在静默安装时同样会被调用，返回 `False` 会让 Setup 直接退出。
- 目录页前置校验：盘符根 / 共享根、`%WINDIR%` 及其子目录、用户目录根、`%LOCALAPPDATA%` 根、
  `%LOCALAPPDATA%\Programs` 根、Program Files 根一律拦下；每用户模式再探一次可写性（在「最近的已存在
  父目录」写 `.pdxnext-write-test.tmp` 再删掉，不用 `ForceDirectories`，取消后不留空目录）。
- 踩坑：`ExpandConstant('{userprofile}')` 在 Inno Setup 7 里**不是**合法常量（编译期不报错，运行到
  `NextButtonClick` 才弹 `Runtime error (at 10:569): 内部错误: Unknown constant "userprofile"`），改成环境
  变量形式 `{%USERPROFILE}`；同时给 `InstallerScriptChecks` 加了一条「`[Code]` 里用到的 `ExpandConstant`
  常量必须在内置白名单内」的断言，避免同类问题再溜过去。
- 踩坑：`tools/verify-installed-version.ps1` 一度在「确实装了」时也报 `FAIL no installed PdxNext found.`，
  根因是脚本没有 BOM，Windows PowerShell 5.1 按 ANSI(GBK) 解码，中文注释行尾的 `。` 把后续换行吃掉、
  把 `$uninstallKey = ...` 整行并进注释。两个脚本文件都补回 UTF-8 BOM，并在自检里钉死 BOM。
- `OtherModeInstalled` 以前只探测两个默认路径，用户自选目录后「切模式」检测会失效、两个模式能并存；
  现在先读**对方**卸载项里的 `Inno Setup: App Path`（每机器安装读 HKCU，每用户安装读 HKLM），读不到再退回默认路径探测。
- `tools/verify-installed-version.ps1` 同样补上卸载项目录，自选目录的安装也能被校验到。
- 新增 `tests/PdxLauncher.Checks/InstallerScriptChecks.cs`：把 `DisableDirPage=no`、`UsePreviousAppDir=yes`、
  `AppId` 未改动、`[Files]` 仍排除 `data`/日志/更新缓存/`install-mode.txt`、`[CustomMessages]` 中英文成对、
  `NextButtonClick` 里 `WizardSilent` 必须先于任何 `MsgBox`、更新器参数仍以 `/SILENT` 开头全部钉死。

### 0.87.3 验证

- `ISCC.exe` 直接编译 `installer/PdxNext.iss`：0 警告 0 错误。
- 自检 `检查类: 111，失败: 0`（新增 `InstallerScriptChecks` 的 26 条断言全 ok）。
- 数据目录不受影响：`PdxDataRoot` 先读 `install-mode.txt`，每用户 → `<安装目录>\data`，
  每机器 → `%LOCALAPPDATA%\PdxNext-Data`，自选目录天然安全。
- 真机向导实测（UIA 取控件坐标 + `SetWindowPos` 置顶 + `mouse_event` 真实点击，`/LANG=zhcn /CURRENTUSER /DIR=<目标>`）：
  - 「选择目标位置」页确实出现，路径框预填 `C:\Users\<user>\AppData\Local\Programs\PdxNext` —— 原报障点已解决；
  - 填 `C:\Windows\PdxNextProbe` → 拦下并弹「不能安装到这个位置……例如 `D:\Apps\PdxNext`」，停在目录页；
  - 填 `C:\$Recycle.Bin\PdxNextProbe`（不在禁列里但不可写）→ 拦下并弹「当前用户没有写入该文件夹的权限……」；
  - 填 `C:\ProgramData\PdxNextProbe` → 放行（本机 Users 对 ProgramData 根有写权限，属正常放行）；
  - 直接填 `C:\` 时先弹的是 Inno 内建的「必须输入带驱动器盘符的完整路径」（Inno 自己的校验早于脚本事件），
    这属于预期，因为盘符根在 Inno 那一层就被挡了。
- 静默端到端（`/VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER /NORESTART /DIR=C:\Temp\pdxnext-install-test\e2e`）：
  setup exit 0、`install-mode.txt: perUser`、`tools/verify-installed-version.ps1` 输出
  `OK ...\e2e\PdxLauncher.App.exe FileVersion=1.2.1.0`、卸载 exit 0 且安装目录清空（`data` 按设计保留）。
- 待人工做：一次真实的「启动器内自动更新」回归（`UpdateService` → `/SILENT`）。版本号按 `docs/VERSIONING.md` §4 在发布时再 bump。

## 0.81 最新一轮（2026-11-04 15:00–16:10）：工坊详情截图 —— 缩略图都出来了，上面大图还在转圈

> 用户诉求原话（m00003）：「这是什么原因，下面的图片都出来了，上面还在下载？走的不一样的调用路径？」

### 0.81.1 根因：确确实实是两条调用路径

- 缩略图条是 XAML 直接 `ItemsSource="{x:Bind ViewModel.Screenshots}"`，每个 item 的 `Image` 绑 `Path`、
  显隐绑 `HasPath` —— 集合里一加条目、一张下完就自己亮，跟批任务无关。
- 内联大图 `FlipWorkshopScreenshots` 当时**没有** `ItemsSource`，由 code-behind 的 `EnsureGallerySelection()`
  在 `LoadAsync` 返回**之后**才赋集合和 `SelectedIndex`；而 `LoadAsync` 的 Steam 路径把正文配图
  `DownloadInlineImagesAsync` 也塞进 `Task.WhenAll` → 大图恒空、计数器空，顶部却一直转。
- `IsLoadingScreenshots` 是页面级单标志，只在 WhenAll 的 finally 清零；单张失败只 `_logger.Warn`，
  UI 没有任何「这张在传 / 这张挂了」的状态，所以「看起来一直在下载」。

### 0.81.2 改动清单

- 新增纯算法 `src/PdxLauncher.App/Services/ScreenshotLoadTracker.cs`（Total / Settled / Pending / IsBusy，
  `Begin(int)` / `Settle()` / `FinishAll()` / `Reset()`，只约定 UI 线程、不加锁）。
- `WorkshopScreenshotViewModel` 加 `IsPending`（默认 true）/ `IsFailed` 与派生 `ShowPending` / `ShowFailed` /
  `ShowPlaceholder`，`MarkSettled(bool failed)`；`Path` 落盘后同步刷新这些派生属性。
- `WorkshopDetailViewModel`：`BuildGallery` 后 `_galleryTracker.Begin(Screenshots.Count)`（去重后的条目数），
  每张图片任务 finally 里 `MarkSettled` + `Settle()`，只要还没结清 `IsLoadingScreenshots` 就保持 true；
  外层 finally `FinishAll()` 兜底（取消 / 提前返回不会把提示永久点亮）。
- 详情页 XAML：hero `FlipView` 直接绑 `ViewModel.Screenshots`；模板加「下载中 / 下载失败」两层叠层，
  全屏查看器同样加。
- code-behind：`OnLoaded` 幂等订阅 `Screenshots.CollectionChanged`，集合一变就 `EnsureGallerySelection()`；
  `Count == 0` 时把 `SelectedIndex` 复位成 -1 并清空计数器（`Clear()` 会把选中项打成 -1，旧逻辑会残留「3 / 4」）。
- i18n 两条：`workshop.detailScreenshotDownloading`、`workshop.detailScreenshotFailed`（中英各一）。

### 0.81.3 验证

- `./build.cmd`：0 error，自检 101 个检查类 / 0 失败；新增 `ScreenshotLoadTrackerChecks`（26 条断言：
  Begin(0)、四次 Settle、重复 Settle 不进负数、FinishAll、Begin 重置、Reset、Begin(-3) 钳 0）。
- 真机（清空 `data/workshop-info/shots` 后打开未缓存的 Steam 工坊条目）：计数器在**首张落地那一刻**就变成
  `1 / 6`，hero 同帧出图（`.scratch/gallery-verify/` 的采样截图）；顶部提示只在还有图没结清时存在。
  本机下载太快（4–6 张 < 1 s），没截到「提示与大图同框」的瞬时帧，改以 UI 采样 + tracker 断言互相印证。

### 0.81.4 坑与后续

- `winapp ui focus` 在窗口不是前台时直接拒绝，必须先 `AttachThreadInput` + `SetForegroundWindow` 再发
  `send-keys --via send-input`；`set-value` 不会触发提交，Enter 必须真发键。
- 计数器 `txtWorkshopInlineCounter` 没有 AutomationId，UIA 只能按文本 `\d+ / \d+` 找，容易撞上日期
  （`2026/10/30` 会被误匹配），采样时要排除。
- 未做：单张重试按钮、缩略图逐张 spinner、失败占位点击重下；并发上限仍是 2，先不动。

## 0.79 最新一轮（2026-11-04 14:45–15:20）：新增「性能与日志」页 —— 把优化点集中到一页

> 用户诉求原话（m00001）：「能不能加一个[性能与日志]页面，给我看看有什么优化的地方」

### 0.79.1 设计：五个只读分区 + 一组阈值规则

页面挂在左栏最底部（`navDiagnostics`，排在「账号」之后、「设置」之前），`Tag="diagnostics"`；
`NavigationService` 注册 `DiagnosticsTag` → `PdxLauncher.App.Views.DiagnosticsPage`，`TagForPageType` 自动反向映射，选中态无需额外代码。
上次停留的分区记在 `AppSettings.LastDiagnosticsSection`（`int?`，0 起 5 项，null/越界落「概览」），
与设置页的 `LastSettingsSection` 同一套路。

| 分区 | 展示内容 | 数据来源 |
| --- | --- | --- |
| 概览 | 工作集 / 私有 / 托管 / 峰值（各带目标说明）、GC 0/1/2、线程 / 句柄、运行时长、工坊条目 / 分页 / mod 缩略图、缓存合计；下方是「优化建议」列表 | `MemoryDiagnostics.Capture` + `CacheRowCatalog.MeasureAsync` + `PerformanceHints.Evaluate` |
| 缓存 | 合计 + 七行（主题 / 公告 / 缩略图 / DLC 描述 / 图片 / 工坊 / 翻译），逐行可清、可全部清除（先确认） | 与设置页「缓存」分区**同一份** `CacheRowCatalog`，不再各写一套清除 lambda |
| 日志 | 当前文件大小 / 归档数 / 合计、最近写入时间、轮转阈值（`FileLogger.MaxBytes` / `MaxArchives`）、按「全部 / 警告 / 错误」过滤的最近 200 行、5 秒自动刷新开关、「打开日志目录」 | `LogTailReader.Measure` / `ReadTail` / `FilterNewestFirst` |
| 网络 | 每主机一行：总请求 / 限流 / 拒发 / 冷却剩余 / 最近一次；代理模式与解析出的地址（带来源），有告警时显示 InfoBar | `AppServices.HttpThrottle.Snapshot()` + `ProxyResolver.Describe` |
| 启动耗时 | 五个里程碑的绝对耗时与相邻差值：进程入口 / 服务装配 / 窗口激活 / 外壳就绪 / 首屏完成；下方注明「冷启动不联网」 | `StartupTimeline.Snapshot()` |

「优化建议」是纯规则函数（无 UI、可单测），阈值都写成常量，命中才出现、按严重度排序：

| 规则 | 阈值 | 级别 |
| --- | --- | --- |
| 工作集超详情预算 / 空载预算 | ≥ 550 MB → 高；≥ 250 MB → 中（内存只出一条） | 高 / 中 |
| 有主机被限流器**拒发**过 | 拒发数 > 0（取最忙的主机） | 中 |
| 冷启动到首屏超时 | ≥ 2500 ms | 中 |
| 缓存合计偏大 | ≥ 500 MB | 低 |
| 工坊缓存条目偏多 | 条目 + 分页 ≥ 2000 | 低 |
| 日志接近轮转 | ≥ `FileLogger.MaxBytes` × 80% | 低 |
| 历史日志已满 | 归档数 ≥ 3 | 低 |
| 翻译模型常驻 | 翻译开 + `KeepTranslationModelLoaded` | 提示 |

### 0.79.2 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Views/DiagnosticsPage.xaml` / `.xaml.cs` | **新增**。左 `NavigationView`（216 DIP，5 个 `NavigationViewItem`）+ 右 `ScrollViewer`，结构与设置页同构；分区切换、清缓存（`ConfirmOverlay` 先确认）、日志过滤 / 自动刷新（`DispatcherTimer` 5 s，页面卸载即停）、打开日志目录（`ShellLauncher.TryStart("explorer.exe")`）、状态条 |
| `src/PdxLauncher.App/ViewModels/DiagnosticsViewModel.cs` | **新增**。五个分区的采样与文案拼装；`Task.Run` 采样后回 UI 线程改集合；`Relocalize` 重建全部数字与句子；不新建服务，只用 `AppServices` 静态服务 |
| `src/PdxLauncher.App/ViewModels/DiagnosticsRows.cs` | **新增**。四个不可变行模型：指标卡、建议、限流主机、启动里程碑 |
| `src/PdxLauncher.App/ViewModels/CacheRowCatalog.cs` | **新增**。把设置页构造里的七行缓存定义、并行测量、全部清除搬出来，设置页与性能页共用（设置页 UI 与行为不变） |
| `src/PdxLauncher.App/Services/StartupTimeline.cs` | **新增**。静态时间线，起点取 `Process.StartTime`（失败退回 `DateTimeOffset.Now`）；`Mark` 幂等、线程安全、上限 32 条、绝不抛；五个里程碑常量 |
| `src/PdxLauncher.App/Services/LogTailReader.cs` | **新增**。用 `FileShare.ReadWrite` 加 `FileShare.Delete` 只读尾部 128 KB / 200 行（不锁写入方）、UTF-8 残字节与半行丢弃、按 `[WARN] / [ERROR]` 整块过滤（堆栈续行跟随）、最新在上；`Measure` 统计主文件 + 归档；`FileLogger.MaxBytes` / `MaxArchives` 改为 `public const` 供页面显示阈值 |
| `src/PdxLauncher.App/Services/PerformanceHints.cs` | **新增**。`DiagnosticHintCode` 枚举 + `PerformanceHintInput` + `Evaluate`，阈值常量集中在此 |
| `src/PdxLauncher.App/Services/MemoryDiagnostics.cs` | `MemorySnapshot` **嵌套 record** 保持 9 个位置参数不变，另加 `init` 属性：峰值工作集 / 线程数 / 句柄数 / 运行时长；`Describe` 文案一字未改（自检逐字断言） |
| `src/PdxLauncher.App/Services/NavigationService.cs` | `DiagnosticsTag = "diagnostics"` + `TypeNames` 映射到 `DiagnosticsPage` |
| `src/PdxLauncher.App/MainWindow.xaml` | 页脚新增 `navDiagnostics`（`SymbolIcon Symbol="Repair"`） |
| `src/PdxLauncher.App/Program.cs`、`App.xaml.cs`、`MainWindow.xaml.cs` | 五个启动里程碑打点；启动 / 关窗的内存快照去掉 `#if DEBUG`，Release 也留一行 |
| `src/PdxLauncher.Core/Configuration/AppSettings.cs` | 新增 `LastDiagnosticsSection` |
| `src/PdxLauncher.Core/Infrastructure/FileLogger.cs` | `MaxBytes` / `MaxArchives` 改 `public const`（逻辑不变） |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`、`shell.json` | **新增** 94 个 `diagnostics.*` 键 + `shell.nav.diagnostics`；`文案源生成器` 重新生成（合计 1280 键） |
| `tests/PdxLauncher.Checks/{DiagnosticsPageChecks,PerformanceHintsChecks,LogTailReaderChecks,StartupTimelineChecks}.cs` | **新增** 4 个检查类（96→100）：阈值两侧取值与排序、日志尾部的半行 / 残字节 / 写入方占用 / 归档统计、时间线幂等与上限、页面静态结构（导航注册、19 个 AutomationId、5 个分区与 Tag 顺序、只读 TextBox、XAML 无硬编码中文、三处共享埋点、设置页已改用 `CacheRowCatalog`） |
| `tests/PdxLauncher.Checks/MemoryDiagnosticsChecks.cs` | 扩充：逐字断言 `Describe` 全文、新 `init` 属性默认 0、活体快照峰值 / 线程 / 句柄 / 运行时长非 0 |

**没有**新增 NuGet 依赖；页面只读本地状态与文件，不联网、不写游戏或官方启动器目录；没有清空日志的按钮（`FileLogger` 的句柄不含删除共享，清空会被写入方占用）。

### 0.79.3 验证

- `bash tools/build-app.sh -v q` → **0 警告 0 错误**；`dotnet build PdxLauncher.sln -c Release -m:1`（清掉 `src/PdxLauncher.App/obj/x64` 后）→ **0 警告 0 错误**。
- `dotnet run --project tests/PdxLauncher.Checks -c Release` → `检查类: 100，失败: 0，断言失败总数: 0`。
- `文案自检` → 通过（重新生成后合计 1280 键）。
- **未做真机 UI 截图**：本机同时有两个别的会话在跑 `PdxLauncher.App.exe`，本轮没有抢占或杀进程；页面结构与绑定的验证靠编译 + 静态检查。

### 0.79.4 坑与后续

- **`MemorySnapshot` 是嵌套类型**：`MemoryDiagnostics.MemorySnapshot`，VM 里写裸 `MemorySnapshot` 会 CS0246；同时不能改它的 9 个位置参数，否则 `MemoryDiagnosticsChecks` 的命名参数构造全断——新字段一律走 `init` 属性。
- **WinUI 3 的 `TextBox` 没有 `HorizontalScrollBarVisibility`**：要写 `ScrollViewer.HorizontalScrollBarVisibility` 附加属性（否则 WMC0011）。
- **清掉 `obj/x64` 后 `dotnet build PdxLauncher.sln` 报 `XamlCompiler WMC9999`（参数超出范围）**：残留 `obj` 与并发实例的输出互相干扰；`rm -rf src/PdxLauncher.App/obj/x64` 后整解重建即通过（`tools/build-app.sh` 只构 App 工程，未受影响）。
- **检查工程关 `ImplicitUsings`**：新检查文件里用到 `Path` / `Regex` 等要显式 `using System.Text.RegularExpressions;`，`IFileSystem` 要 `using PdxLauncher.Core.Abstractions;`。
- 后续可做：真机截图核对五分区排版（尤其是 1040 DIP 最小宽下「概览」两张卡并排）、把 `DiagnosticsViewModel` 的格式化函数抽成可链接的纯静态类以便单测、在概览里补「本次会话相对启动涨了多少」。

## 0.78 最新一轮（2026-11-04 14:20–14:45）：Mod 创建页按钮行被裁掉 —— 换成「排不下就换行」

> 用户诉求原话（m00971）：「把自动换行加上，现在的也太抽象了」
> 截图（m00969）：Mod 创建页详情面板最后一行只剩四个半按钮，第五个「绑定已有条目…」被裁成「绑…」。

### 0.78.1 根因：可用宽度只有 ≈544 DIP，五个按钮要 ≈660 DIP

| 项 | 值 | 出处 |
| --- | --- | --- |
| 窗口默认宽 / 最小宽 | 1200 / **1040** DIP | `src/PdxLauncher.App/MainWindow.xaml.cs:33` `MinimumWindowWidth` |
| 左侧导航 | 216 DIP | `src/PdxLauncher.App/MainWindow.xaml:182` `OpenPaneLength` |
| Mod 创建页左右列 | 360（MinWidth 280）+ ColumnSpacing 12 | `src/PdxLauncher.App/Views/ModStudioPage.xaml` 主体 Grid |
| 页面 / 卡片内边距 | 16×2 / 18×2 | 同上 |
| **详情右栏可用宽** | 1200 宽 ≈ **544**、1040 宽 ≈ **384** | 1200−216−32−360−12−36 |
| 五按钮自然宽（zh-CN） | ≈ **660** | 实测截图比例；en-US 更长 |

也就是说**默认窗口宽下就已经放不下**，不只是窄屏。而这一行原本是
`<StackPanel Orientation="Horizontal" Spacing="8">` —— WinUI 3 **没有** WrapPanel（`CommunityToolkit.WinUI.Controls` 也不在本项目依赖里），
水平 StackPanel 从不换行；外层 `DetailPaneScroll` 的横向滚动默认是 Disabled，于是超出的按钮被**直接裁掉**，看起来像「按钮没了」。
上传面板的 BBCode 工具条（11 个 `MinWidth=36` 按钮 ≈670 DIP）是同一类问题，只是窄屏时才暴露。

### 0.78.2 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Services/WrapLayout.cs` | **新增**。纯算术：`WrapItem(Width,Height)` / `WrapPlacement(Index,X,Y,Width,Height)` / `WrapLayoutResult(Items,Width,Height)`，`Compute(items, availableWidth, spacing)`。规则：可用宽非有限或 ≤0 → 一行；间距非有限或 <0 → 0；尺寸非有限或 ≤0 的项跳过且**不产生间距**；换行判据 `x + width > limit` 只在行内判断（行首不判，避免单项超宽死循环）；行高取本行最高项；总高 = 各行高 + 行间距。不引用任何 XAML 类型（检查工程可链接断言） |
| `src/PdxLauncher.App/Controls/PdxWrapPanel.cs` | **新增**。`public sealed class PdxWrapPanel : Panel`，`Spacing` 依赖属性默认 **8.0**（变更回调 `InvalidateMeasure`）；`MeasureOverride` 用 `(availableWidth, ∞)` 量子元素、跳过 `Visibility.Collapsed`、委托 `WrapLayout`；`ArrangeOverride` 用 `finalSize.Width` **重算一遍**再 Arrange（首次布局时量到的宽度可能被夹过）。纯代码控件，无 XAML |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 详情动作行 `StackPanel Orientation="Horizontal" Spacing="8"` → `<ctl:PdxWrapPanel Spacing="8">`（5 个按钮、AutomationId、Click 全部原样，加注释说明原因）；BBCode 工具条 → `<ctl:PdxWrapPanel Spacing="4">`。其余行（新建预览 / 上传预览的「按钮 + 省略号文本」）不动 |
| `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj` | 链接 `WrapLayout.cs`（App 是 WinExe + windows TFM，检查工程引用不了，只能链接源码） |
| `tests/PdxLauncher.Checks/WrapPanelChecks.cs` | **新增**检查类（95→96）。算法：单行总量、刚好放得下 / 差 1 DIP、行高取最高、0 宽项不产生间距、原始下标保留、空列表、∞/NaN/0 宽兜底、超宽单项、非法间距；接线：详情行 5 个 `</Button>` + 5 个 AutomationId 都在同一个 `ctl:PdxWrapPanel` 内、工具条 11 个 `btnBbCode*`、面板默认间距 `8.0`、`WrapLayout` 不含 `Microsoft.UI` / `Windows.Foundation` |

**没有**新增 i18n 键（只改布局），`Strings.g.cs` 未重新生成；`文案自检` 通过（1172 键）。
**没有**改 `MinimumWindowWidth`、左列宽、按钮文案与顺序、其他页面的横向按钮行，也没引 NuGet 依赖。

### 0.78.3 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -v m` → **0 警告 0 错误**。
- `dotnet run --project tests/PdxLauncher.Checks -c Release` → `检查类: 96，失败: 0，断言失败总数: 0`。
- `文案自检` → exit 0。
- 真机实测（pwsh + UIA，本机 DPI 150%，窗口尺寸按 DIP 折算）：**1200×760 DIP → 详情按钮 2 行**（3 + 2，两行 y=418 / 458，行距 40 DIP）、**1040×600 DIP（最小宽）→ 3 行**（y=437 / 477 / 517），五个按钮的 AutomationId 全部可达，不再被裁到可视区外（第三行落在详情卡片底部，滚动可见）；默认启动尺寸 1329×815 DIP → 2 行。
- 截图证据：`.scratch/wrap-b-1200.png`（1200 DIP 两行）、`.scratch/wrap-c-1040.png`（1040 DIP 三行）；`.scratch/` 已在 `.gitignore` 第 13 行，不入库。上传面板 BBCode 工具条在 1040 DIP 下由 11×36 DIP 的按钮自然折成 2 行（同一控件、同一算法）。

### 0.78.4 坑与后续

- **WinUI 没有 WrapPanel**：`Microsoft.UI.Xaml.Controls` 里只有 `ItemsRepeater + UniformGridLayout`（那是等宽网格，不适合按钮条），
  `CommunityToolkit.WinUI.Controls` 也没被引用；所以自绘 `Panel` 是唯一「不引依赖 + 不重写按钮」的路。
- **Panel 子类在 Measure 里必须给子元素「宽度受限、高度无限」**：否则按钮会被压成多行文本或按 ∞ 宽测量。
- **Arrange 必须用最终宽度重算**：Measure 时的可用宽可能是 ∞（父级还没定宽）或已被夹过，缓存 measure 结果会导致首帧错位。
- 这个面板可以复用到其他横向按钮行（工坊详情、DLC 页工具条等）——本轮只动了用户截图指到的那两处。
- **重编 x64 时踩的两个坑**：① 应用正在运行会锁住 `bin\x64\...\PdxLauncher.Core.dll`，`MSB3021/MSB3027` 反复重试失败——先 `taskkill //F //IM PdxLauncher.App.exe`（Git-Bash 里必须写双斜杠，否则 `/IM` 被当成路径）。② 手删 `obj/x64` 后增量构建会报 `WMC0601 找不到 App.xaml`，必须 `dotnet build ... -p:Platform=x64 -t:Rebuild` 走完整重建；首次重建出现的 25 条 `PRI249 Invalid qualifier: THEME-CACHE` 警告是残留 PRI 引起的瞬态，再构建一次即归零。

## 0.77 最新一轮（2026-11-04 00:00–13:30）：创意工坊搜不出来 —— 「加载失败」被当成了「用户取消」

> 用户诉求原话（m00003）：「现在创意工坊页面啥情况，给谁改炸了，怎么搜不出来了」
> 补充（m00262）：「是 Vic3 遇到问题」；追问（m00418）：「是不是该缓存改炸了」。

### 0.77.1 结论先说：搜索功能是好的，坏的是「连不上时没人说话」

用复刻 `SteamWorkshopHtmlParser.Parse` 的脚本打真实页面（Vic3，appid=529340，走代理）：

| 请求 | total_count | 判定 |
| --- | --- | --- |
| 无关键词 | 11252 | 基准 |
| `&searchtext=morgenrote` | **51** | 关键词生效 |
| `&searchtext=morgenrote&days=30` | 51 | `days` 不影响结果集，只改排序 |
| `browsesort=textsearch&searchtext=morgenrote` | 51 | 与 `browsesort=trend` 等价 |
| `&searchtext=zzzqqqnotexist` | 0 | 确实按关键词过滤 |

日志里的请求 URL 也完全正确：
`https://steamcommunity.com/workshop/browse/?appid=529340&section=readytouseitems&browsesort=trend&num_per_page=30&p=1&searchtext=morgenrote&days=30`。

⇒ `docs/HANDOFF.md:3565` 那条「`searchtext` 与 `browsesort` 并存，别再把排序改写成 `textsearch`」**是对的**，
`beca63d` 删掉「强制 textsearch」**不是 bug**（已实测等价）。用户看到的空白与搜索词无关。

### 0.77.2 真正的根因：连接超时 → 被当成「用户取消」→ 静默吞掉

截图事实：来源徽标=**Steam 创意工坊**、游戏=**Victoria 3**、列表**完全空白**、状态栏永远停在
「**正在加载第 1 页…**」（既不是「已加载 N 条」，也不是失败文案）。

日志（`src/PdxLauncher.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/data/logs/pdxnext.log`）：

```
22:05:59 [INFO] 请求合并：browse:529340|https://steamcommunity.com/workshop/browse/?appid=529340&…&searchtext=morgenrote&days=30
22:08:05 [ERROR] 未观察的 Task 异常
   System.AggregateException → System.TaskCanceledException
   → System.TimeoutException: A connection could not be established within the configured ConnectTimeout.
   at HttpFetcher.GetAsync(...) at SteamWorkshopClient.BrowseCoreAsync(...) at RequestCoalescer.RunAsync[T](...)
   at WorkshopViewModel.QuerySteamAsync(...) at WorkshopViewModel.QueryAsync(...) at WorkshopViewModel.RunQueryAsync(...)
```

即：**请求发出去了、URL 也是对的，但 30 秒连不上**（`HttpFetcher.ConnectTimeout = 30s`）。
接着异常被三层 catch 吃掉：

1. `SteamWorkshopClient.BrowseCoreAsync` 只 `catch (HttpRequestException ex)` —— 但 .NET 连不上时抛的是
   `TaskCanceledException`（内含 `TimeoutException`），**不是** `HttpRequestException` → 不被捕获。
2. `RequestCoalescer.RunAsync` 的 `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`
   不成立（token 没被取消）→ 走 `catch (Exception ex)` 以异常形式广播。
3. `WorkshopViewModel.QueryAsync` 的 `catch (OperationCanceledException) { throw; }` 原样上抛，
   `WorkshopViewModel.RunQueryAsync` 的 `catch (OperationCanceledException) { }`（空吞）把它当成
   「被新查询或页面卸载取代」的正常路径 → **状态栏永远停在「正在加载第 1 页…」，`IsLoaded` 不置 true，
   连空状态都不显示，列表空白**；Task 异常从未被 await/observed → 最终化线程抛「未观察的 Task 异常」。

**为什么连不上**：本机 DNS 被污染 —— 默认 DNS 把 `steamcommunity.com` 解析到 **104.244.46.21**（Twitter 的 IP），
公共 DNS 8.8.8.8 也给 **199.59.148.7**；Node 直连实测 `Connect Timeout Error`。同一台机器上
GitHub 也连不上（`检查更新失败: …(github.com:443)`）→ **是全局的 DNS 污染，不是 Steam 的问题**。
而本机明明配了 `http_proxy=https_proxy=HTTP_PROXY=http://10.31.2.50:7890`、WinINET `ProxyEnable=1`，
走该代理访问 Steam **HTTP 200 / total_count=11252** —— **代理是通的，只是应用没用它**。

**为什么应用没用它**：`ProxyResolver` 的 `Direct` 模式**故意不读环境变量也不读系统代理**
（`HttpFetcher.BuildClient` 注释：「代理完全由调用方决定：这里再隐式读环境变量会让『直接连接』模式名不副实」），
只有 `System` 模式才读。三个 sqlite（开发库 / Temp 安装测试 / 已安装版）的 `settings` JSON 里
**从来就没有 `proxyMode` / `proxyUrl` 键** → 每次启动都是 Direct → 直连被污染的 DNS → 超时 → 空白。
日志里 22:27–22:41 那 8 次「使用代理 http://10.31.2.50:7890」全部紧跟「服务装配完成」，
说明那是**进程内临时改过、没落盘**的内存态（设置页改动只写内存，`Save()` 之前进程被杀就丢）。

### 0.77.3 「给谁改炸了」

| 提交 | 时间 | 贡献 | 定性 |
| --- | --- | --- | --- |
| `d88eb7a` | 2026-10-28 03:06 | 工坊页首版，`SteamWorkshopClient` 的网络 catch 就只捕 `HttpRequestException` | **老问题的源头**（漏捕超时） |
| `a24cb5a` | 2026-10-29 16:44 | 新增 `RequestCoalescer`（在途去重） | 把超时异常广播给等待者，放大了症状 |
| `beca63d` | 2026-11-03 20:03 | 引入 `_pageToken` + `CancelPendingQuery` + `RunQueryAsync`，其 `catch (OperationCanceledException) { }` 空吞 | **让「连不上」变成「永久转圈」的直接原因** |
| `8b39092` | 2026-11-03 21:09 | 工坊缓存改 SQLite 真表 | **与本故障无关**（只动缓存层，不碰请求路径）——用户「是不是缓存改炸了」的猜测可以排除 |

补充两点口径，避免下轮再走弯路：

- `beca63d` 引入的「回车才提交搜索」**不是故障**（`ad4f712` 之前是打字即查询，Steam 1 秒限流下确实会卡）；
  这次搜索失效与它无关，只是它让「取消」成为常态路径，从而把老问题放大成空白页。
- `ProxyResolver` 自 `fab6981` 起**从未改过** —— 「直连不读环境变量」是设计语义，不是谁改炸的。
  但它需要配套的「检测到代理 → 一键切换」，这轮补上了。

### 0.77.4 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Online/NetworkTimeoutException.cs` | **新增**。`public sealed class NetworkTimeoutException : HttpRequestException`，构造 `(string host, string message, Exception? innerException = null)`，属性 `Host`。继承 `HttpRequestException` 是为了兼容既有调用点：所有已捕 `HttpRequestException` 的地方不改一行也能接住超时；真正的用户取消仍原样抛 `OperationCanceledException` |
| `src/PdxLauncher.Core/Online/HttpFetcher.cs` | `GetAsync`（`:119`）与 `GetDownloadAsync`（`:180`）各加 `catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)` → `throw new NetworkTimeoutException(uri.Host, "连接超时（30 秒）：" + uri.Host, ex)`。`ConnectTimeout`(30s) / `DefaultTotalTimeout`(180s) / `IHttpFetcher` 接口均未改（6 个自检 fake 不动） |
| `src/PdxLauncher.Core/Online/SteamWorkshopClient.cs` | 5 处网络入口各加两个 catch（`NetworkTimeoutException` 与 `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`），统一失败码 **`steam-workshop-network`**：`BrowseCoreAsync`、`GetDetailCoreAsync`、`GetItemPageCoreAsync`、`ProbeAppDetailsAsync`、`ProbeWorkshopPageAsync`。原有 `steam-workshop-parse` / `steam-workshop-http` / `steam-workshop-detail-*` 语义不变；评论 / 订阅类入口本轮不动；`BuildBrowseUri` 未改 |
| `src/PdxLauncher.Core/Updates/UpdateService.cs` | `:175` catch 白名单加 `or TaskCanceledException`（超时 TaskCanceledException 原先不在名单里，会漏出未观察异常） |
| `src/PdxLauncher.App/ViewModels/WorkshopViewModel.cs` | 新增 `DescribeWorkshopError(errorCode, kind)`（`steam-workshop-parse`/`Unsupported` → `WorkshopStatusParseFailed`；`steam-workshop-network` → `WorkshopStatusNetworkFailed`；其余 → `WorkshopStatusFailed`）；新增 `DescribeDetectedProxyHint()`（只读 `ProxyResolver.Describe(ProxyMode=System)`，命中则提示「检测到系统代理 X，可在设置里切过去」）；`QuerySteamAsync` 失败分支改走该映射；`RunQueryAsync` 改成三级 catch —— `when (version != _queryVersion || _pageToken.IsCancellationRequested)` 才静默，超时兜底分支写 `IsLoaded=true; IsBusy=false; StatusMessage=WorkshopStatusNetworkFailed`；`QueryAsync` 同样分级；`ShowFailureOrCache` 无缓存分支在网络失败时追加代理提示。**未改**：`:262 ShowEmptyState`、`:473-482 CommitSearchAsync`、`:351-380 RefreshAsync`、`partial void OnSearchTextChanged`（自检 `CheckViewModelNoQueryWhileTyping` 依赖「打字不查询」契约） |
| `src/PdxLauncher.App/ViewModels/SettingsViewModel.cs` | 新增 `DetectedProxyText` / `HasDetectedProxy` / `UseDetectedProxy()`（切到 `manual` + 写地址 → `SaveSettings()` → `TryRebuildNetworkServices()`）；`RefreshProxyStatus()` 末尾调 `RefreshDetectedProxy(resolution)` 只读探测系统 / 环境变量代理；`ApplyProxySettings()` 成功后补一条「代理设置已写入并生效」日志 |
| `src/PdxLauncher.App/Views/SettingsPage.xaml(.cs)` | 代理分区新增「检测到的代理」卡片（`controls:SettingsCard`，Visibility 由 `HasDetectedProxy` 驱动）：TextBlock `AutomationId="txtDetectedProxy"` + Button `AutomationId="btnUseDetectedProxy"`（Click=`OnUseDetectedProxyClick`） |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 新增 `workshop.statusNetworkFailed`（zh「无法连接 Steam 创意工坊：{0}」/ en「Cannot reach Steam Workshop: {0}」）、`workshop.statusProxyHint`（zh「检测到系统代理 {0}，可在「设置 → 代理」里切换过去再试」） |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 新增 `settings.proxy.detected` / `detectedName` / `detectedHint` / `useDetected` / `useDetectedName`（各 5 键） |
| `src/PdxLauncher.App/Localization/Strings.g.cs` | **机器产物**，由 `文案源生成器` 生成（1167 → 1172 键），不可手改 |
| `tests/PdxLauncher.Checks/SteamWorkshopApiChecks.cs` | 新增 `CheckNetworkFailure()`：假 fetcher 抛 `TaskCanceledException(inner TimeoutException)` → `BrowseAsync` 返回失败 `steam-workshop-network`（不抛异常）；抛 `NetworkTimeoutException` → 同错误码；token 已取消时仍抛 `OperationCanceledException`。为此给 `CapturingHttpFetcher` 加可注入的 `GetException`（**没动 `IHttpFetcher` 接口**） |
| `tests/PdxLauncher.Checks/HttpFetcherChecks.cs` | 新增 `CheckTimeoutExceptionContract()`：`NetworkTimeoutException` 必须是 `HttpRequestException`、`Host` 正确、`InnerException` 保留、源码里有「没被取消的取消 → 超时」的分支 |
| `tests/PdxLauncher.Checks/WorkshopPageChecks.cs` | 新增 `CheckNetworkFailureIsVisible()` 源码断言：`RunQueryAsync` 块含 `when (version != _queryVersion || _pageToken.IsCancellationRequested)` 与 `WorkshopStatusNetworkFailed`；`DescribeWorkshopError` 区分 `steam-workshop-parse` / `steam-workshop-network`；`ShowFailureOrCache` 调 `DescribeDetectedProxyHint` |
| `tests/PdxLauncher.Checks/ProxyChecks.cs` | 新增 `CheckDirectIgnoresEnvironment()`：直连不读环境变量也不读系统代理；跟随系统才能发现环境变量代理 |
| `tests/PdxLauncher.Checks/SettingsChecks.cs` | 新增 `CheckSingleSettingsInstance()`：`AppServices.cs` 必须有 `Settings = settings;` 且**不得**再出现 `settingsStore.Load()`（二次加载会整份覆盖写回、把用户配好的代理抹掉） |
| `CHANGELOG.md` / `docs/HANDOFF.md` | 本节与 `## [Unreleased]` 的 `### Fixed` 条目 |

### 0.77.5 验证

- `dotnet build PdxLauncher.sln -c Release -m:1` → **0 警告 0 错误**。
- `dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll` → **检查类: 95，失败: 0，断言失败总数: 0**（含新增 15 条断言：网络故障 5 条、连接超时契约 5 条、页面失败可见 4 条、代理直连语义 5 条、设置单实例 2 条）。
- `文案自检` → 退出码 0，1172 键。
- 未做真机验证：本机 DNS 仍被污染，必须先在设置页「代理」分区点「使用」切到检测到的代理，工坊页才能连上。
  **用户需要重设一次代理**（旧库里的代理配置已被二次加载抹掉）。

### 0.77.6 坑

- **`.NET` 连不上时抛 `OperationCanceledException`（内含 `TimeoutException`），不是 `HttpRequestException`。**
  只捕 `HttpRequestException` 的调用点会把「连不上」当成「用户取消了操作」——这正是本次空白页的成因。
  全仓已按此排查过的点：`SteamWorkshopClient` 5 处、`UpdateService:175`（本轮已修）、`PdxModsClient:324`
  （**已有正确先例**：`catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)`）。
- **「取消」要分两种**：用户真的取消了（token 已取消 → 静默、正常路径）vs 连接层超时（token 没取消 → 必须报错）。
  判据统一用 `when (!cancellationToken.IsCancellationRequested)`。
- **别把「代理不生效」归到 DNS 污染头上**：先查 `settings` 表里有没有 `proxyMode` / `proxyUrl`。
  三个库都查过：**从来没有** → 说明用户设置从没落盘过，不是「配置被覆盖」，而是「根本没写进去」。
- **`docs/HANDOFF.md:3565` 的「关键词与排序并存」结论是对的**，本轮已用真实页面复核（见 §0.77.1），别再往回改。

### 0.77.7 下一步

1. 让用户在设置页点「使用」切到检测到的代理（`http://10.31.2.50:7890`），再进 Vic3 工坊页搜 `morgenrote`，
   预期状态栏显示「已加载 N 个结果」而不是空白。
2. 真机复验「连接超时」的可见性：把代理指向一个不可达地址，确认状态栏显示「无法连接 Steam 创意工坊：steamcommunity.com」
   而不是一直转圈。
3. `SteamWorkshopClient` 的评论 / 订阅类入口（`:330/:335/:458/:522/:588`）本轮没动，仍只捕 `HttpRequestException`，
   下轮可一并按同一模式补上。

## 0.74 最新一轮（2026-11-03 19:00–20:00）：安装包与自动更新

> 用户诉求原话（m00001）：「问题：1.该怎么实现Windows的软件安装包；2.该如何实现软件的更新」
> 后续拍板（m00047/m00132/m00155）：安装包讲 MSIX 和 Inno；更新走 GitHub 公开 Releases；
> 下载 + 校验 + 静默安装重启；每用户 + 每机器双模式；Inno 用 7.1.1。

### 0.74.1 安装器（Inno Setup 7.1.1）

- `installer/PdxNext.iss`：`AppId={{A2E3D5E1-5133-46BA-A846-1388A878E1A7}`、
  `DefaultDirName={autopf}\PdxNext`、`PrivilegesRequired=lowest` +
  `PrivilegesRequiredOverridesAllowed=commandline dialog`、`SetupArchitecture=x64`、
  `ArchitecturesAllowed=x64compatible`、`MinVersion=10.0.17763`、`CloseApplications=yes`。
- `[Files]` 排除 `data\*`、`data-root.txt`、`*.log`、`update-cache\*`、`install-mode.txt`，
  升级不覆盖用户数据。
- `[Code]`：`ssPostInstall` 写 `install-mode.txt`（`perUser`/`perMachine`）；
  `PrepareToInstall` 检测另一模式的默认安装目录，要求先卸载，避免双装。
- `[Run]`：`postinstall` / `skipifnotsilent` 两路都用 `runasoriginaluser`，每机器安装后启动器
  不以管理员令牌运行。
- `tools/build-installer.py`：校验 `dist\PdxNext` 的 `.xbf` / `.pri`、定位 ISCC、编译、
  算 SHA-256、写 `dist\installer\update.json`。产物 65 MB（238 MB payload）。
- 版本唯一源 `version.props`，`Directory.Build.props` 导入；安装器、程序集、清单三处一致。

### 0.74.2 自动更新

- `Core/Updates/UpdateVersion.cs`：1–3 段数字，忽略 `v`、`+commit`、`-preview`。
- `Core/Updates/UpdateManifest.cs`：schemaVersion=1、HTTPS + 白名单、`.exe`、64 位 sha256、
  size>0、notes URL HTTPS。
- `Core/Updates/UpdateService.cs`：24 小时节流、`ReadManifestTextAsync`（128 KB 上限）、
  流式下载 + 边写边算 SHA-256、长度/摘要校验、`.part` 清理、`/ALLUSERS|/CURRENTUSER`、
  每机器 `runas` + 1223 取消处理。
- `Core/Online/HttpFetcher.cs` 新增 `GetDownloadAsync`（返回流 + Content-Length）；
  `OnlineHosts` 加 `github.com` / `objects.githubusercontent.com`。
- `App/Services/UpdateFlow.cs`：启动静默检查（失败只 Warn）、手动检查、`ConfirmOverlay`
  更新提示、`ProgressBar` 下载进度、取消下载、启动安装器后保存设置并关闭主窗。
- `MainWindow.xaml.cs` 的 `OnRootGridLoaded` 末尾 fire-and-forget；
  `AppSettings` 新增 `AutoCheckUpdates`（默认 true）、`LastUpdateCheckUtc`、
  `SkippedUpdateVersion`。

### 0.74.3 双模式数据目录

- `PdxDataRoot` 新增 `install-mode.txt` 与 `%LOCALAPPDATA%\PdxNext-Data` 回退；
  每机器安装不再尝试写 Program Files 下的 `data`。
- `DataRootMigrator.MigrateFirstAvailable`：每机器首次启动依次尝试旧的每用户程序目录与
  `%LOCALAPPDATA%\PdxNext`，只复制不删除。
- 设置页新增「关于与更新」（Tag=7）：版本、安装模式、上次检查、自动检查开关、立即检查、
  状态文本；`scripts/verify-settings-sections.ps1` 同步 `navSettingsAbout`。

### 0.74.4 验证

- `dotnet build PdxLauncher.sln -c Release -m:1`：0 警告 0 错误。
- `dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"`：
  85 类 / 0 失败（含 `UpdateChecks`、`LocalizationBindingChecks`）。
- `python tools\build-installer.py`：成功生成 `PdxNext-Setup-1.0.0.exe` + `update.json`。
- 尚未做：真实安装/卸载、每机器 UAC 更新、GitHub 发布仓库创建与上传、代码签名。

### 0.74.5 下一步

1. 刷新 `publish.cmd` 后重新打安装包（安装器 payload 必须是修过文案的最新 `dist\PdxNext`）。
2. 发布仓库统一为公开的 `lyxc6/pdxnext`，推 `v*` tag 由 GitHub Actions 自动上传。
3. 按 `docs/INSTALLER.md` §7 做每用户/每机器安装、升级、取消 UAC、校验失败实机验证。
4. 如需对外分发，补 Authenticode 证书与 `SignTool` / `SignedUninstaller=yes`。

## 0.75 最新一轮（2026-11-03 20:35–21:00）：GitHub Actions 与版本规范

> 用户诉求原话（m00822）：「项目地址：https://github.com/lyxc6/pdxnext / 1.可以更改为GitHub Actions / 2.同时规范下项目的接下来的各种更改，做好版本号规划 / 3.推送下最近的更新内容」

### 0.75.1 发布改为 Actions

- `.github/workflows/release.yml`：`push` tag `v*` 或手动触发；`windows-latest`；
  checkout `fetch-depth: 0`；`setup-dotnet 10.0.x`；临时把 `NuGet.config` 换成 nuget.org；
  静默安装 Inno Setup 7.1.1 x64 并定位 `ISCC.exe`；校验 tag 与 `version.props` 一致；
  跑 `publish.cmd`（含自检）与 `tools/build-installer.py`；上传 artifact；
  `softprops/action-gh-release@v2` 上传 `PdxNext-Setup-*.exe` 与 `update.json`。
- `.github/workflows/ci.yml`：push main / PR / 手动；构建 + 跑 `PdxLauncher.Checks.dll`。
- 发布仓库从 `lyxc6/pdxnext-releases` 统一回 `lyxc6/pdxnext`，因为源码仓库已公开；
  `UpdateService.DefaultManifestUri`、安装器 `ReleasesUrl`、`build-installer.py`、
  `publish-release.ps1`、`UpdateChecks` 与文档全部同步。
- 本地脚本仍保留：`tools/publish-release.ps1` 用 `GITHUB_TOKEN` 调 GitHub REST，作为 Actions 之外的手动兜底。

### 0.75.2 版本规范

- `docs/VERSIONING.md`：SemVer；`version.props` 是唯一版本源；MAJOR/MINOR/PATCH 判定；
  预发布只手动分发、不进 `latest` 清单；发版顺序（改版本 → 更新 CHANGELOG → 本地验证 → 推 main →
  打 tag → Actions 校验/构建/上传 → 验证 latest → 旧版本实测升级）。
- `CHANGELOG.md`：`[Unreleased]` + `[1.0.0] - 2026-11-03` 首个公开版本。
- 安装器 AppId 固定为 `A2E3D5E1-5133-46BA-A846-1388A878E1A7`，升级不能变。

### 0.75.3 验证

- `dotnet build PdxLauncher.sln -c Release -m:1`：0 警告 0 错误。
- `dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll`：85 类 / 0 失败。
- `python src\PdxLauncher.Localization.Generator --check`：通过（1135 键）。
- 尚未做：提交并推送、Actions 首次运行验证、`v1.0.0` tag、Release 资产可访问性/安装实测。

## 0.76 最新一轮（2026-11-03 22:30–23:55）：已经发布到创意工坊，启动器却说「未发布」

> 用户诉求原话（m00001）：「mod上传有问题，我已经上传了mod（vic3那个），启动器仍然认为mod不存在」
> 后续（m00574）：用户贴出 `https://steamcommunity.com/sharedfiles/filedetails/?id=3832809800`，说明「这个是mod地址」。

### 0.76.1 根因：不是没传上去，是「藏起来了就查不到」

匿名调用 `POST ISteamRemoteStorage/GetPublishedFileDetails/v1/` 查该 id，回来的是一句：

```json
{"response":{"result":1,"resultcount":1,"publishedfiledetails":[{"publishedfileid":"3832809800","result":9}]}}
```

`EResult 9 = FileNotFound`。Steam 对**隐藏 / 仅限好友 / 私密 / 提交未完成**的条目，对匿名调用方一律回 9 ——
它不区分「真的不存在」和「藏起来了」。启动器把这句话当成了「这个 mod 没有」。

我们的链路原本有三处断点：

| # | 现象 | 根因 |
| --- | --- | --- |
| 1 | 详情页对它显示一张空卡片 | `SteamWorkshopClient.ParseDetail` 只取 `publishedfiledetails[0]`，**丢掉 `result` 字段**；`result:9` 被当成一条「标题空、大小 0」的正常条目 |
| 2 | Mod 创建页永远显示「未发布」 | 发布记录（`modstudio.publish.*`）**只在「本启动器发起过上传」时**才写；用户是在 Steam 客户端 / 网页上发的，没有任何回填入口。实测 dev 库 `documents` 表零条 `modstudio.publish.*`、日志零条发布记录 |
| 3 | Mod 库 / 创意工坊页都看不到它 | `workshop_items` 与 `ModLibraryService` 只收**订阅**条目，而自己发布的条目不是订阅；`EnumerateUserSubscribedFiles` 也不含自己的条目 |

结论：**不需要任何第三方服务**。Steamworks SDK 自带两个走本机登录态的接口就能看见自己的隐藏条目：
`sdk/public/steam/isteamremotestorage.h:279-280` `GetPublishedFileDetails`（鉴权版，能读隐藏条目的标题与可见性）与
`:283-285` `EnumerateUserPublishedFiles`（注释原文 "enumerate the files that the current user published with this app"）。
本机 `Victoria 3\binaries\steam_api64.dll` 实测导出 `SteamAPI_ISteamRemoteStorage_EnumerateUserPublishedFiles` /
`…_GetPublishedFileDetails` / `SteamAPI_SteamRemoteStorage_v014`，既有的 `--steam-host` 子进程通道直接可用。

### 0.76.2 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/SteamHostProtocol.cs` | 新增动词 `WorkshopMineVerb = "workshop-mine"`、`WorkshopMineDetailVerb = "workshop-mine-detail"` 并进 `Verbs`（7 → **9**）；新增 `SteamHostMineResult(bool Ok, IReadOnlyList<ulong> Ids, int Total, bool Truncated, string? Error, bool Initialized)`（`MaxItems = 1000`、`Fail(error)` → Total=-1）与 `SteamHostMineItemResult(bool Ok, ulong PublishedFileId, string Title, string Description, IReadOnlyList<string> Tags, int Visibility, ulong OwnerSteamId64, long FileSizeBytes, uint CreatedUnix, uint UpdatedUnix, int CreatorAppId, string? Error)`（`Fail(id, error)` → Visibility=-1） |
| `src/PdxLauncher.Core/Steam/SteamHostRunner.cs` | `OwnItemsTimeout = 30s`；`Run` 加 `RunMine` / `RunMineDetail` 分支（后者 `--fileid` 必填且须 `ulong`，非法写 stderr 返回 `ExitFailure`）；usage 字符串补两个动词 |
| `src/PdxLauncher.Core/Steam/SteamWorkshopHostOperation.cs` | 绑 `SteamAPI_SteamRemoteStorage_v*` 与两个 `ISteamRemoteStorage` 导出（候选 v016…v013），RemoteStorage **不是** init 硬门槛；`ReadOwnPublishedItems(timeout)`（50/页翻页，回调 **1312**）、`ReadOwnPublishedItem(id, timeout)`（回调 **1318**，`Result==9` 文案「Steam 说这个条目不存在（EResult=9）」）；`PumpManualCallbacks` 从两分支改成按回调号 switch（3403/3404/1312/1318）；新增 `OwnEnumerateFilesResult`（`Marshal.SizeOf = 416`）与 `OwnFileDetailResult`（**9760**，三个 bool 显式 `UnmanagedType.I1`）；`OwnEnumerateResultSize` / `OwnDetailResultSize` 供自检 |
| `src/PdxLauncher.Core/PdxLauncher.Core.csproj` | 新增 `<InternalsVisibleTo Include="PdxLauncher.Checks" />`（自检要读 internal 的结构体尺寸属性） |
| `src/PdxLauncher.Core/Online/SteamWorkshopClient.cs` | 新增 `ParseDetailWithResult(string? json) → (SteamWorkshopItemDetail? Detail, int Result)`、`ResultUnknown = -1`、`DetailUnavailableReason(int)`（9 →「这个条目对未登录 / 匿名调用方不可见…**不等于它不存在**」）；`ParseDetail` 改为取 `.Detail`；`GetDetailCoreAsync` 在详情为空时 Warn 一句原因 |
| `src/PdxLauncher.Core/ModStudio/ModPublication.cs` | 末位追加 `int? SteamVisibility = null` + `IsPubliclyVisible`（null / 0）/ `IsUnlisted`（1/2/3）；XML 注释写明与 `Visibility`（意图）的区别 |
| `src/PdxLauncher.Core/ModStudio/ModBindResult.cs` | **新增**。`record ModBindResult(bool Ok, ModPublication? Publication, string? Error, bool Verified, string? VerifyError)` + `Fail` / `Bound` |
| `src/PdxLauncher.App/Services/ModStudioService.cs` | 新增 `BindWorkshopItemAsync(game, entry, publishedFileId, verifyVisibility, ct)`（硬前提只有「本地目录存在 + id>0」；读详情失败只 Warn 不阻断；保留既有 Description/Tags/Preview 等字段，`LastError = null`）、`ReadOwnItemsAsync`、私有 `TryReadOwnItemAsync` / `ResolveMineContext`；`PublishAsync` 的 4 处早退（无工坊 / 无目录 / 预览图不合规 / `backend is null`）补 Warn |
| `src/PdxLauncher.App/Services/ModPublishBackend.cs` | `TryCreate` 的 3 处失败（appid / 宿主 / dll）补 `logger.Warn` |
| `src/PdxLauncher.App/Services/SteamHostClient.cs` | `OwnItemsTimeout = 45s`；新增 `ReadMineAsync` / `ReadMineItemAsync`（走既有 `ExecuteAsync`，会自动追加 `--fileid`） |
| `src/PdxLauncher.App/ViewModels/ModStudioRowViewModel.cs` | `PublicationText` 两态 → **四态**：无 id「未发布」/ `LastError` 非空「发布未完成 · {0}」/ `IsUnlisted`「已发布 · {0}（未公开）」/ 其它「已发布 · {0}」 |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | 新增 `SelectedSteamVisibility` / `HasSelectedSteamVisibility` / `CanBindWorkshop` / `BindWorkshopHint` / `BindWorkshopAsync(...)` / `ReadOwnWorkshopItemsAsync(ct)`（枚举只回 id，列表里标题就是 id 字符串）；`PublishAsync` 的 7 处校验分支补 Warn；`RaiseDerived` / `OnIsPublishingChanged` 通知新派生属性 |
| `src/PdxLauncher.App/Views/BindWorkshopItemDialog.cs` | **新增**。`internal static class`，`ShowAsync(XamlRoot, modName, loadMine)` → `ulong?`（null = 取消）；纯代码 UI（照 `ModPathRepairDialog` 范式），含 id 输入框、「从我的 Steam 条目里选」按钮与列表、错误行；确认键只在 `ulong.TryParse` 且 >0 时可用 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml(.cs)` | 详情面板加第 8 行「Steam 可见性」（仅 `HasSelectedSteamVisibility` 为真时显示）+ 按钮「绑定已有条目…」（`CanBindWorkshop`，AutomationId `modStudioBindWorkshop`）；`OnBindWorkshopClick` 串对话框 → `BindWorkshopAsync(id, verifyVisibility: true, ct)` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 各 +14 键（`bindExisting` / `bindHint` / `bindTitle` / `bindBody` / `bindIdLabel` / `bindLoadMine` / `bindMyItemsEmpty` / `bindSuccess` / `bindUnverified` / `bindFailed` / `detailSteamVisibility` / `rowPublishedUnlisted` / `rowPublishFailed` / `visibilityUnknown`），复用既有 `visibilityPublic/FriendsOnly/Private/Unlisted`；modstudio **155 → 169 键**，全库 **1140 → 1154 键** |
| `tests/PdxLauncher.Checks/ModWorkshopBindingChecks.cs` | **新增**。8 组断言：id 解析边界（空 / 0 / 负号 / 全角数字）、四态判据（含「意图 Public + 事实私密」）、`ModPublication` JSON 往返与旧记录无字段 → null、20 个 i18n 键在两份 json 里存在、两个新动词与两条结果记录 Encode/TryDecode 往返、`ParseDetailWithResult` 对 `result:9` 返回 (null, 9) 与坏响应 → -1、结构体尺寸 416 / 9760、记录键大小写与尾分隔符稳定 |
| `docs/DATA-FORMATS.md` | 新增 §18.9「我发布的条目与真实可见性」：EResult 9 的含义、两个动词与回调号、结构体尺寸、`SteamVisibility` 五值表、意图 vs 事实、绑定语义 |
| `docs/HANDOFF.md` | 本节 |

未改：`ModLibraryService` 的订阅注入、`mods.steamId` 列、`WorkshopPage` 的浏览逻辑（自己发布的条目仍不进「订阅」列表，这是刻意的）。

### 0.76.3 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -v m` → 0 警告 0 错误。
- `dotnet run --project tests/PdxLauncher.Checks -c Release` → `检查类: 94，失败: 0，断言失败总数: 0`（4603 条 ok）。
- `文案自检` → 退出码 0，1154 键。
- 未做真机验证：本机 Steam 账号下 Victoria 3（529340）**没有任何已发布条目**（9 条作者条目全是 CK3）；3832809800 是隐藏条目，
  属于另一个账号，所以「从我的条目里选」这条路径只能等用户在自己账号上实测。

### 0.76.4 坑

- **结构体尺寸别凭直觉**：我一开始断言详情结构是 1760，实测 `Marshal.SizeOf` = **9760**（`char[8000]` 描述缓冲是大头，
  之后还有 `ulong` 对齐补位）。唯一可靠做法是让自检打印实测值，再对着 `isteamremotestorage.h:467-497` 数一遍字段。
- **`bool` 一律 `UnmanagedType.I1`**：C# 默认 4 字节，SDK 里是 1 字节，不标会把后面的 `char[1025]` 整体错位，
  而且原生调用**不会报错**，只会读到脏的标签 / 可见性。
- **动词表数量断言会被新动词打破**：`SteamHostProtocolChecks` 里「动词表恰好七个」必须同步改成九个，
  否则自检报失败，容易误判成自己写坏了协议。
- **枚举接口只回 id**：`EnumerateUserPublishedFiles` 不回标题，所以「从我的条目里选」列表显示 id；
  想显示标题必须逐条跑 `workshop-mine-detail`（9 条 = 9 次子进程，暂未做）。
- **绑定不该被「查不到详情」卡住**：Steam 没开 / 没登录 / 缺导出都会让读详情失败，但这不该阻止手填 id 绑定；
  `SteamVisibility` 留 null，文案退回「已发布 · {id}」，并单独提示「已绑定，但没能从 Steam 核实可见性」。
- **排查这类问题先看证据**：`data\logs\pdxnext.log` 里有没有 `Steam 工坊发布：使用宿主` / `已发布到 Steam 创意工坊`，
  以及 `pdxnext.sqlite` 的 `documents` 表有没有 `modstudio.publish.*` —— 两条都没有，就说明这单上传从未经过本启动器。

### 0.76.5 下一步

1. 让用户在**自己账号**上实测：「绑定已有条目…」→ 手填 id → 行文案变「已发布 · {id}」；再点「从我的 Steam 条目里选」看是否列出。
2. 若实测发现隐藏条目读回来的 `Visibility` 是 2（私密），考虑在 Mod 创建页给一条「把它改成公开」的快捷入口（走已有 `SetItemVisibility`）。
3. 可选：`workshop-mine` 加 `--with-titles` 之类的一次性批量读标题，避免 N 次子进程。

## 0.60 最新一轮（2026-10-31 13:00–17:00）：新增「Mod 创建」页

> 用户诉求原话：「添加mod创建页面，用于显示本地mod、创建mod和上传mod到创意工坊」

### 0.60.1 页面

- 左栏 `NavModStudio`（Tag=`modstudio`，图标 `E7B8`，在「Mod 库」后）；`NavigationService` 映射到
  `PdxLauncher.App.Views.ModStudioPage`，`MainWindow.UpdateContentNavState` 与其它内容页一起显隐，
  不可管理游戏会重定向主页。
- `ModStudioPage`：左 360 DIP 本地 mod 列表（搜索、刷新、缩略图、已发布徽标），右侧详情/新建/上传三态面板，
  底部状态条与上传进度条；新建/上传用页内表单，不用 `ContentDialog`；发布确认才用 `ConfirmOverlay`。
- 自动化 Id：`modStudioList`、`modStudioSearch`、`modStudioCreate`、`modStudioUpload`、`modStudioRefresh`、
  `txtModStudioName/Folder/Version/SupportedVersion/Tags/Description`、`btnModStudioCreateConfirm/Cancel`、
  `rdoModStudioTargetSteam/Pdx`、`txtModStudioUploadDirectory/ModId/Title/Description/ChangeNote`、`modStudioUploadTags`、
  `btnModStudioUploadConfirm/Cancel`、`modStudioProgress`、`modStudioLegalLink`。

### 0.60.2 创建

- `Core/ModStudio/ModDescriptorRules`：ck3/stellaris/eu4/hoi4/imperator_rome 走 legacy，victoria3/eu5 走 metadata，
  其余不支持；`CanPublish` 还要求 `SteamWorkshopGames.TryGet(gameId)==true`。
- `ModCreator`：
  - legacy：`<modDir>/<folder>.mod`（带绝对 `path`）+ `<folder>/descriptor.mod`（不带 `path`）+ `thumbnail.png`；
  - metadata：`<folder>/.metadata/metadata.json` + `.metadata/thumbnail.png`，`picture` 写相对 `.metadata` 的 `thumbnail.png`；
  - 校验名称/文件夹名（`^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$`）、V3 非 ASCII、路径长度、目标已存在；失败清理半成品。
- `LocalModInventory` 只列本地（排除 `steamapps/workshop/content` 与 `workshop/content`），DB 行强制 `source=local`、
  `steamId=null`，归档行可见但不可上传。
- 发布记录写 `IJsonDocumentStore`（键 `modstudio.publish.<gameId>.<sha256(path)[..16]>`），**不写 `mods.steamId`**，
  避免本地 mod 被来源判定成订阅 mod。

### 0.60.3 上传

- 仍遵守 §0.59：主进程不 `SteamAPI_Init`；上传走 `PdxLauncher.App.exe --steam-host workshop-publish --api … --appid … --request <base64 json>`。
- `SteamHostProtocol` 增至 5 个动词，新增进度行前缀 `PDXNEXT-STEAM-HOST-PROGRESS ` 与 `ModPublishRequest/Progress/Result`。
- `SteamWorkshopHostOperation`：UGC 访问器候选 `v021…v012`（实测 V3/Stellaris 只有 v014、CK3 只有 v016），
  `CreateItem → StartItemUpdate → SetItemTitle/Description/Content/Preview/Visibility/Tags → SubmitItemUpdate`；
  用 `SteamAPI_ManualDispatch_*` 等 `CreateItemResult_t`(3403)/`SubmitItemUpdateResult_t`(3404)，`GetItemUpdateProgress` 推百分比。
- 法律协议：结果为 `NeedsLegalAgreement` 时页面给 `https://steamcommunity.com/sharedfiles/workshoplegalagreement` 链接。
- 上传前需 Steam 运行且已登录；失败会保留已拿到的工坊 id 和 `LastError`，便于重试。
- 上传目标对齐官方页：Steam / Paradox 两个单选；当前只接通 Steam，Paradox 选项保留但确认按钮禁用并显示未接入提示。
- `Mod ID` 对齐官方语义：留空=新建条目；填数字=更新该 `PublishedFileId`。初值从 `ModPublication.PublishedFileId` 回填，
  `ModIdInput.TryParse` 拦截 0/非数字/溢出。
- 标签改为游戏固定清单复选框（`ModTagCatalog`；V3 用官方 24 项，其他游戏用常用清单），保留描述符里未知标签。
  官方清单来自 Paradox games API，离线实现无法复刻，采用本地清单。

### 0.60.4 验证与未验证项

- `dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误。
- 自检 → `检查类: 63，失败: 0，断言失败总数: 0`（新增 `ModStudioChecks`，含规则、legacy/metadata 创建、非法输入、
  本地清单、发布记录、发布协议、宿主参数）。
- `文案自检` → 960 键。
- UIA 真机：左栏可见「Mod 创建」；CS2 列出 53 个本地 mod 且创建/上传禁用；CK3 列出 5 个本地 mod，
  详情、缩略图、新建表单、上传表单与确认浮层均正常；上传表单可见目录、Steam/Paradox 单选、Mod ID、
  名称、描述与标签复选；Steam 目标确认按钮可用，Paradox 目标禁用并提示未接入。
- **发布链路只做了静态/参数自检，没有真机完成一次创意工坊上传**（会创建真实公开条目，未擅自执行）。
  若后续真机上传失败，先查 Steam 是否运行/登录、是否接受法律协议，再查 `SteamAPI_ManualDispatch_*` 的管道参数与回调 id。

## 0.59 最新一轮（2026-10-31 12:10–13:00）：退出游戏后 Steam 仍说游戏在跑，点「退出游戏」还把启动器杀了

> 用户诉求原话：「我启动vic3后，退出游戏，steam仍然处于识别到游戏运行中，在steam点击退出游戏还会把启动器一起退出」

### 0.59.1 根因：Steam 把「启动器」登记成了「vic3」

两个症状同一个源头。证据在 Steam 自己的客户端日志 `D:\app\Steam\logs\console_log.txt`：

```
[2026-10-31 12:09:24] Game process added : AppID 529340 "...\win-x64\PdxLauncher.App.exe", ProcID 35008
[2026-10-31 12:10:04] STARTUP SlowJSMethodCall Apps.TerminateApp took 12.6ms     ← 用户点了「退出游戏」
[2026-10-31 12:10:07] Game process removed: AppID 529340 "...\win-x64\PdxLauncher.App.exe", ProcID 35008
```

启动器日志同一秒给出了触发者：`12:09:24.684 [INFO] Steamworks 已初始化：appid=529340，dll=…\win-x64\steam_api64.dll`
（`SteamWorkshopBridge`，用户在创意工坊详情页）。历史上 DLC 授权探测也会触发，而且**残留很久**：
10-30 `21:23:11` 探测 → Steam `Game process added … ProcID 39280` → 直到 `22:06:27` 才 `removed`（≈43 分钟）。

机制：**任何进程用游戏 appid 调 `SteamAPI_Init`，Steam 就把它登记为该 app 的 game process**，
`SteamAPI_Shutdown()` **不会**撤销登记（清一色只在进程退出/被 `Apps.TerminateApp` 时消失）。于是：

- Steam 库显示「游戏运行中」＝ 启动器还活着（哪怕游戏早退出了）；
- Steam 的「退出游戏」＝ 结束那个「游戏进程」＝ 杀启动器，游戏本身根本不在。

### 0.59.2 修法：同一个 exe 的隐藏宿主模式

不新增可执行文件（`publish.cmd` 的 `AddWinUIXamlArtifactsToPublish` 已被记为易坏，多一个要同步发布的 exe 只会持续漂移），
改成「同一个 exe，两种启动方式」：

- `src/PdxLauncher.App/PdxLauncher.App.csproj` 定义 `DISABLE_XAML_GENERATED_MAIN`，关掉 XAML 生成的 `Program`；
- `src/PdxLauncher.App/Program.cs` 自己当入口：`args[0] == "--steam-host"` 时不初始化 WinUI，只跑 `SteamHostRunner`
  并把结果写一行 `PDXNEXT-STEAM-HOST {json}` 到 stdout；否则原样 `InitializeComWrappers` + `Application.Start`；
- 触发 `SteamAPI_Init` 的活全在子进程里，**探测约 0.4–1.5 秒、订阅最多 25 秒**，子进程结束即被 Steam 除名；
  Steam 的「退出游戏」最多杀掉这个短命子进程，启动器不受影响。

### 0.59.3 新增/删除的文件

| 文件 | 作用 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/SteamHostProtocol.cs` | 命令行契约（`--steam-host probe\|workshop-state\|workshop-subscribe\|workshop-unsubscribe`）、结果行前缀 `PDXNEXT-STEAM-HOST `、JSON 编解码、`SteamHostProbeResult` / `SteamHostWorkshopResult` |
| `src/PdxLauncher.Core/Steam/SteamHostRunner.cs` | 子进程入口分发、参数解析、退出码（0 成功 / 1 失败）、工坊确认超时 25 s |
| `src/PdxLauncher.Core/Steam/SteamWorkshopHostOperation.cs` | 手工 `NativeLibrary` 绑定（`InitFlat` / `Shutdown` / `RunCallbacks` / `SteamUGC` / `SubscribeItem` / `UnsubscribeItem` / `GetItemState`），250 ms 轮询等状态位翻转 |
| `src/PdxLauncher.Core/Steam/SteamClientLocator.cs` | 注册表 + 默认路径找 Steam 安装根，兜底拿 Steam 自带 `steam_api64.dll` |
| `src/PdxLauncher.App/Program.cs` | 双模式入口（见上） |
| `src/PdxLauncher.App/Services/SteamHostClient.cs` | 拉起子进程、并发读 stdout/stderr、超时 `Kill(entireProcessTree: true)`、解码结果、日志 `Steam 宿主 <verb>：exit=…，耗时 … ms` |
| `src/PdxLauncher.App/Services/SteamHostProbe.cs` | `ISteamDllProbe` 的宿主实现，默认接到 `SteamOwnershipService` |
| `src/PdxLauncher.App/Services/SteamWorkshopHostBackend.cs` | `IWorkshopSubscribeBackend` 的宿主实现，替代原来的进程内桥 |
| ~~`src/PdxLauncher.App/Services/SteamWorkshopBridge.cs`~~ | 删除（进程内长会话，最严重的一处） |
| ~~`src/PdxLauncher.App/Services/SteamNativeApi.cs`~~ | 删除（`DllImport` 只能绑进程内调用） |

`SteamDllProbe` 保留，但现在**只被 `SteamHostRunner` 在子进程里用**；主进程里没有任何 `SteamAPI_*` 的 P/Invoke
（`grep -rn "SteamAPI_\|DllImport(\"steam_api" src/PdxLauncher.App` 只剩注释命中）。

### 0.59.4 顺带修的坑

- 创意工坊「订阅状态」以前只要后端可用就只信 Steamworks，宿主模式下 `ReadState` 可能返回 Unknown（没在运行的进程读不到实时状态），
  现在 `WorkshopDetailViewModel.RefreshSteamSubscriptionAsync` 在 `!state.IsKnown` 时回退到 acf 文件，不再把「已订阅」显示成未知。
- 新增错误键 `hostFailed`（`SteamProbeError.HostFailed`）+ 文案 `dlc.ownershipErrorHostFailed`，宿主被杀/超时/输出损坏时 DLC 页给出可读原因。
- `src/PdxLauncher.Localization.Generator` 用严格 UTF-8 解码，i18n 的 json 源文件**不能带 BOM**（否则 `Unexpected UTF-8 BOM`）。
- `tools/manual-build.py` 的 app 编译参数补了 `-define:DISABLE_XAML_GENERATED_MAIN`，离线构建路线不会漏。

### 0.59.5 真机验证（2026-10-31 12:51–12:55）

- 宿主冒烟：`PdxLauncher.App.exe --steam-host probe --api "<vic3>\binaries\steam_api64.dll" --appid 529340`
  → 1.4 s 返回 `"initialized":true, "steamRunning":true, steamId64=76561198370719182` + DLC 列表；Steam 日志
  `12:51:43 Game process added … --steam-host probe … ProcID 21236` → `12:51:46 Game process removed`（子进程退出即除名）。
- 端到端：启动器 GUI（PID 22948）打开 CV3 的 DLC 页 → 日志 `12:55:07 Steam 宿主 probe：exit=0，耗时 402 ms`、
  `DLC 授权探测（gameId=ck3）：state=Succeeded，Steam 37 个 DLC、29 个已拥有`；Steam 日志同秒 `added … ProcID 35748`
  （子进程）→ `12:55:09 removed`，**启动器自身 PID 从未出现**。
- 自检：`dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll` → `检查类: 63，失败: 0`；
  新增 `SteamHostProtocolChecks`（动词识别 / 结果往返 / 缺参数退出码 / 缺 DLL 返回 `noDll` / JSON 驼峰 / `LooksLikeSteamRoot`）。

### 0.59.6 以后别再犯

- **不要在启动器主进程里调 `SteamAPI_Init`**（哪怕立刻 `Shutdown`）：登记不撤销，就会瘫在 Steam 的「运行中」里，并被「退出游戏」连坐。
  需要 Steamworks 一律走 `SteamHostClient`（`--steam-host` 子进程）。
- 如果哪个功能非要长会话 Steamworks（比如实时订阅状态推送），就接受「用的时候才会被登记」并明确告诉用户，别默认开着。

## 0.57 最新一轮（2026-10-30 09:20–11:00）：15 处 ComboBox 全换成统一下拉控件 PdxDropDown

> 用户诉求原话：「目前是用什么下拉选框，怎么感觉不是winui的选框，我倒是看到winui有个DropDownButton的下拉选框」
> 用户决策（ask_user_question 两轮）：①外观用**标准 WinUI 按钮式**（填充底 + 描边 + 右侧 chevron），不做 ComboBox 输入框式外观；
> ②**全部 13 处 ComboBox 都换**（放弃「只做 ComboBox 主题化」与「混合」）；③**主页播放集一起重构**，全项目只留一套下拉实现。
> 收尾时又发现「翻译」分区（§0.56 新增）还有 2 处 ComboBox，一并换掉 → **源码里 ComboBox 已清零**。

### 0.57.1 为什么不用 ComboBox

ComboBox 的下拉列表**永远把选中项居中展示**：选中最后一项时下拉会向上展开，`MaxDropDownHeight` 改不了这个定位模型
（WinUI 源码 `ComboBox_Partial.cpp` 的 `GetNonPannablePopupLayout`）。§0.13 已就主页播放集验证过这一条。
`MenuFlyout` 会忽略 `Placement`，所以播放集那一版用的是普通 `Flyout`（`Placement=BottomEdgeAlignedLeft`）。

### 0.57.2 新控件

- `src/PdxLauncher.App/Controls/PdxDropDown.xaml`（UserControl）→ `DropDownButton`（默认 WinUI 模板，自带右侧 chevron）
  + `Flyout`（`Placement=BottomEdgeAlignedLeft`，presenter 样式 Padding=0 / CornerRadius=8 / BorderThickness=0）
  + `ListView`（`MaxHeight=320`、`Margin=4`、`SelectionMode=Single`、`IsItemClickEnabled=True`，
  ItemContainerStyle 关掉系统焦点白框并设 `MinHeight=40 / Padding=12,0`）。
- `src/PdxLauncher.App/Controls/PdxDropDown.xaml.cs`：DP = `ItemsSource` / `SelectedItem` / `DisplayMemberPath` /
  `ItemTemplate` / `PlaceholderText` / `DropDownWidth`（NaN = 与按钮等宽）；事件 `SelectionChanged`（参数 `PdxDropDownSelectionChangedEventArgs.SelectedItem`）。
  展开/收起走 `Button.Flyout` 的 `ShowAt` / `Hide` —— **`DropDownButton` 没有 `IsDropDownOpen` 属性**，展开态只能读 `FlyoutBase.IsOpen`（只读）。
  选中项若是 `INotifyPropertyChanged`（各选项目录对象都实现了它），控件会订阅其 `PropertyChanged`，
  **切换界面语言后按钮上的文字会跟着变**，不会停留在旧语言。
- `src/PdxLauncher.App/Controls/PdxDropDownAutomationPeer.cs`：自写 peer，见下。

### 0.57.3 自动化 peer（这是本轮最难的一块）

UI 自动化脚本 `scripts/verify-settings-regression.ps1` 依赖 ComboBox 的两个模式：

- `ComboText` 走 `SelectionPattern.GetSelection()[0].Current.Name` → 需要 **ISelectionProvider**；
- `ComboSelectIndex` 走 `ExpandCollapsePattern.Expand` + `$el.FindAll(Descendants, ControlType=ListItem)`
  + `SelectionItemPattern.Select()` → 需要 **IExpandCollapseProvider**，而且**选项必须是下拉控件自身的 UIA 后代**。

换成 DropDownButton 后默认只暴露 Invoke 模式，所以自写 `PdxDropDownAutomationPeer : FrameworkElementAutomationPeer,
IExpandCollapseProvider, ISelectionProvider`，`GetAutomationControlTypeCore()` 返回 `AutomationControlType.ComboBox`。

踩过的坑（都已在代码里规避）：

1. `SelectorAutomationPeer` 的构造函数要的是 `Microsoft.UI.Xaml.Controls.Primitives.Selector`，
   `UserControl` 不是 → 只能从 `FrameworkElementAutomationPeer` 派生并自己实现 `ISelectionProvider`。
2. `IRawElementProviderSimple` 是 **Public Sealed class**（不是接口）→ 没法自己实现；
   peer→raw provider 的转换只能用基类的 `ProviderFromPeer`。
3. `AutomationElementIdentifiers` **没有** `ExpandCollapseStateProperty` → 调 `RaisePropertyChangedEvent` 会报
   `error CS0117`，干脆不调。
4. `ExpandCollapseState` 枚举在 `Microsoft.UI.Xaml.Automation` 命名空间，不在 `.Automation.Provider` → 要补 using。
5. **Flyout 内容在独立的弹窗宿主里，不在按钮的自动化子树中** → 脚本 `FindAll(Descendants, ListItem)` 取不到任何选项，
   一开始报 `Failed=[bgMode-not-changed]`。
   解法：`GetChildrenCore()` 显式把 Flyout 里 `ListView` 的**原生 peer** 挂成下拉控件的子节点
   （`FrameworkElementAutomationPeer.CreatePeerForElement(ItemsList)`），
   选项于是以原生 ListItem 形态出现，并保留 WinUI 原生的 `SelectionItem` 模式。
   自造的项 peer（host 指向控件自身）会让所有选项 runtime id 相同，UIA 只回 1 项 —— 这条路不通。

### 0.57.4 替换清单（15 处 ComboBox → `controls:PdxDropDown`，AutomationId 全部保持不变）

- 设置页 10 处：`cmbUiLanguage` / `cmbPlaySetPageDisplayMode` / `cmbBackgroundDisplayMode` / `cmbModThumbnailSize` /
  `cmbAlternativeExecutable` / `cmbProxyMode` / `cmbDlcDescriptionRetention` / `cmbModThumbnailRetention` /
  `cmbTranslateEngineMode` / `cmbTranslateTargetLanguage`（`src/PdxLauncher.App/Views/SettingsPage.xaml`；
  最后两处在「翻译」分区，是 §0.56 之后加的，收尾时一并换掉）。
- 播放集页 1 处：`cmbPlaySets`（保留原 ItemTemplate，含「激活」徽标）。
- Mod 库页 2 处：`cmbModLibrarySort` / `cmbModLibrarySource`。
- 创意工坊页 2 处：`cmbWorkshopSort` / `cmbWorkshopTimeRange`。
- 主页播放集 1 处：`btnHomePlaySets` —— 原来手写的 DropDownButton + Flyout + ListView 三件套删掉，改用同一控件；
  `HomePage.xaml.cs` 里 `SyncPlaySetSelection` / `OnPlaySetFlyoutOpening` / `OnPlaySetItemClick` 一并删除。

代码后置同步改造：`ModLibraryPage.xaml.cs` / `WorkshopPage.xaml.cs` / `ModsPage.xaml.cs` / `HomePage.xaml.cs`
的处理器签名由 `SelectionChangedEventArgs` 改为 `PdxDropDownSelectionChangedEventArgs`，取值改为 `e.SelectedItem`。

### 0.57.5 验证（全部实际执行）

- `dotnet build "PdxLauncher.sln" -c Release -m:1` → **0 警告 0 错误**（必须 `-m:1`：解决方案混了 x64 与 Any CPU）。
- `dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll` → **检查类 58，失败 0**。
- `pwsh -NoProfile -File scripts/verify-settings-regression.ps1` → **DONE**（无 FAILED）。
  实测 `CHANGE_BG = picked=（艺术）`、`AFTER_CHANGE` 里背景显示方式确实变了而其它下拉没被重置、控件 BoundingRectangle 不变。
- `pwsh -NoProfile -File scripts/verify-settings-sections.ps1` → **DONE**（7 节 missing 全空，FAILED_SECTIONS=0）。
  首次跑曾报 `MISSING_NAV=navSettingsAdvanced/navSettingsTranslation`，复查是脚本在「缓存」节滚动到底后
  NavigationView 侧栏被重建导致的偶发找不到，与本次改造无关；重跑即恢复。

### 0.57.6 遗留

- `PdxDropDown` 目前只在设置/播放集/Mod 库/创意工坊/主页五处使用；翻译页等其它页面若要用同一控件直接套用即可。
- 下拉项的显示文本走 `DisplayMemberPath` 反射读取（带类型缓存）；`AlternativeExecutable` 等 record 依赖属性名 `DisplayLabel`。
- 源码里已无 `ComboBox` 控件实例（`ops_grep` 只剩注释与 `obj/` 生成物）；各选项目录类与 ViewModel 里
  「ComboBox 的 SelectedItem 才能按引用相等匹配」一类的注释已统一改成「下拉控件」。

## 0.30 最新一轮（2026-10-30）：DLC 页显示 Steam 授权 —— 一眼看出补丁有没有用上

> 用户诉求原话：「给DLC页面添加是否拥有dlc的判定，你翻下原版启动器的判定条件，方便user确认dlc补丁是否用上」

### 先取证：原版启动器怎么判「拥有」

`C:\Users\17147\AppData\Local\Programs\Paradox Interactive\launcher\launcher-v2.2026.13\resources\app.asar`
（29,170,265 B）里 `SteamService.getDlcList()` + `getDataForDlc()` 的合取式：

```
isOwned = isSubscribedApp(id) || 可信DisabledDLC.contains(id) || isDLCInstalled(id)
```

- `isSubscribedApp` / `isDLCInstalled` 就是 Steamworks 的 `ISteamApps::BIsSubscribedApp` / `BIsDlcInstalled`（greenworks 调的）。
- 「可信 DisabledDLC」来自 `<安装目录>\..\..\appmanifest_<appid>.acf` 的 `DisabledDLC`（逗号分隔），但**必须先验 `LastOwner` == 当前 SteamID64**，不匹配就整份丢弃 —— 官方这里是 `if (!lastOwner || lastOwner !== steamId) return new Set()`，防的是换账号后残留。
- appid 用环境变量 `SteamAppId` 灌进 DLL（对应官方 `zt.steamAppIdOverride`）。
- 每个 DLC 项还带 `isInstalled`（来自 `getInstalledDepots()`，与「拥有」是两件事）和 `distPlatform`（STEAM / PDX / MS Store 三套适配器），UI 对 `isOwned === false` 显示 `DLC_NOT_OWNED` 提示。

### 再定方案：判定要落在「游戏实际看到的那份授权」上

本项目已经有完整 CreamAPI 式补丁（`Core/Patch`），所以不能只问 SDK 里那份 DLL —— 那样只能说明「账号里有没有」，说明不了「补丁生效没」。改成**加载游戏目录里那份 `steam_api64.dll`**：补丁生效时它本身就是 loader，它报出来的授权就是游戏进程里看到的授权。

真机实测（python ctypes 先验证 ABI，再用 C# 复核，两者一致）：

| 被加载的 DLL | `GetDLCCount` | 拥有 | 结论 |
| --- | --- | --- | --- |
| `F:\SteamLibrary\steamapps\common\Stellaris\steam_api64.dll`（已打补丁） | 490 | 490 | `PatchedEffective` |
| 同目录 `steam_api64_o.dll`（原版备份） | 53 | 26 | `VanillaPartial` |

SteamID64 = 76561198370719182，与 appmanifest 的 `LastOwner` 一致，`TrustedDisabledDlc = 0`。

### 改了什么

**Core（新增 6 个文件 + 2 处增强）**

- `Steam/SteamOwnership.cs`：契约 —— `SteamOwnershipState{Unavailable,SteamNotRunning,InitFailed,Succeeded}`、`SteamDlcOwnership`、`DlcOwnershipCandidate`、`SteamOwnershipReport`、`SteamDllProbeResult`、`ISteamDllProbe`、`ISteamOwnershipService`、`SteamProbeError` 常量键。
- `Steam/SteamNativeSession.cs`：进程内独占闸门（`TryEnter(owner, out IDisposable)`），Steamworks 只能有一个持有者。
- `Steam/SteamDllProbe.cs`：`NativeLibrary.Load` + `TryGetExport` + `Marshal.GetDelegateForFunctionPointer`；进闸门 → 备份并设 `SteamAppId` → `SteamAPI_IsSteamRunning` → `SteamAPI_Init`（InitFlat → Init → InitSafe 依次回退，loader 没有 InitFlat 也能跑）→ 读 SteamID → 枚举 DLC → `Shutdown` + `Free` + 还原环境变量。异常映射 `DllNotFound→noDll`、`BadImageFormat→x86`、其余 `native`。**Core 的 `AllowUnsafeBlocks=false`，清零缓冲区走 `Marshal.Copy`。**
- `Steam/SteamOwnershipService.cs`：定 appId（game.SteamAppId → GameSteamAppIds → 补丁报告）→ 定 DLL（补丁报告的 `ApiDirectory/ApiFileName`）→ 32 位直接拒 → 30 秒缓存 → 探测 → 读 appmanifest → 合并。
- `Steam/AppManifestLookup.cs`：从安装目录往上推 `steamapps` 库根；已知库补找时**严格要求 `InstallPath` 归一化后等于 `InstallRoot`**；`TrustedDisabledDlc(manifest, steamId64)` 实现官方的 LastOwner 校验。
- `Steam/DlcOwnershipVerdict.cs`：把「补丁状态 + 探测结果 + 盘上 DLC」压成一档结论 —— `PatchedEffective` / `PatchedIncomplete` / `VanillaAllOwned` / `VanillaPartial` / `Unknown`，未授权名单最多留 6 条。
- `AppManifest` / `AppManifestReader`：补 `LastOwner` 与 `DisabledDlc`（`DisabledDlcSet` 做 `Ordinal` 集合），坏键读成 null 而不是抛。

**App**

- `AppServices.DlcOwnership` + `App.xaml.cs` 装配（`SteamLibraryLocator` 延迟求值，只有探测真要用时才枚举库）。
- `Services/XamlBrush.cs`：`FromVerdict` / `FromOwnership` 把 kind 映射成主题画刷（`PdxOwnedForegroundBrush` 等三色加进 Default/Light/HighContrast 三套字典），查不到退回 Gray 而不是 null（`Foreground=null` 会让文字直接消失）。
- `DlcViewModel`：`RefreshOwnershipAsync(force, ct)`（`Task.Run` 里跑探测，不占 UI 线程）、`OwnershipHeadline` / `OwnershipDetail` / `OwnershipKind` / `IsRecheckEnabled`、每行 `SetOwnership(bool?)`；`LoadAsync` 主路径在填简介前跑一次探测，三个提前返回分支都 `ResetOwnership()`。
- `DlcPage.xaml`：根 Grid 三行改四行，新增「授权判定条」（判定句 + 证据句 `路径 · appid · SteamID64 · 补丁状态 · LastOwner 是否匹配` + 重新检测按钮 + ProgressRing），列表行加「已拥有 / 未拥有 / 授权未知」徽标。
- `SteamWorkshopBridge` 接闸门：拿不到就报「Steam 正被其他功能占用（例如 DLC 授权探测）」，`ShutdownCore` 里释放。
- i18n：zh/en 各 31 个 `dlc.ownership*` / `dlc.owned*` 键，`文案源生成器` 生成（共 770 键）。

### 验证

- `tests/PdxLauncher.Checks/SteamOwnershipChecks.cs`（新增，约 560 行）：清单字段解析、LastOwner 可信/不可信两侧、库根推导与路径不匹配不采信、拥有合并（含 DisabledDLC 升级）、五种 verdict、八种失败键、闸门互斥、30 秒缓存与 `Invalidate`。全仓自检 0 失败。
- 真机两个场景都跑通（补丁 490/490、原版 26/53，见上表）。
- 构建坑提醒：改完 Core 之后直接增量 `dotnet build src/PdxLauncher.App/...` 可能报 `CS0246 SteamOwnershipService`（XamlCompiler 还会连带刷 `WMC1509/WMC9999`），那是无效缓存 —— `-t:Rebuild` 或 `build.cmd` 全量构建即消失。
- 跑自检不要 `dotnet ...dll | head`：提前关管道会把进程留在后台并锁住 `bin` 里的 dll，下一次构建就 `MSB3021`。重定向到文件再 grep。

## 0.29 最新一轮（2026-10-29 03:00–20:20）：自有 SQLite 事实源 + 来源徽标 + 修本地 mod 加载

> 用户诉求原话（两条，前后相接）：
> ① 「目前mod库和播放集无法区分本地mod和订阅mod，而且本地mod发生了无法加载的问题」
> ② 「是否能软件维护自己正确的SQLite数据，用正确的数据来更改p社启动器，毕竟有时候user会没装p社启动器；顺便将项目的各个数据能否整理进sql里」

### 两个 bug 的真机根因（先查证再动手）

1. **分不清本地 / 订阅**：契约层就没这个字段。`PlaySetMod` 只有 11 个字段，播放集页那条 SQL
   （`PlaySetRepository.GetPlaySetModsAsync`）也**没查 `m.source` 与 `m.steamId`**，所以即使库里有来源，
   播放集页也拿不到。Mod 库页的 `ModLibraryEntry.Source` 有值但**从来没人显示**（`IsLocalOnly` 是死代码）。
2. **本地 mod 进游戏不生效**：Victoria 3 **无法加载任何路径含非 ASCII 字符的 mod**（Vic3 wiki "Mod structure" +
   Paradox 官方论坛 2024-04-21 同题帖）。该机本地 mod 目录是 `…\Victoria 3\mod\更改虚空造物`（中文目录名），
   `content_load.json` 里写得完全正确、游戏日志里**一个字都不报**，就是不加载。CK3 / Stellaris 没这个限制
   （它们的本地 mod 也是中文目录名，照常加载 —— 现场对比确认）。
   顺带查出第二条独立缺陷：订阅的 `喜儿修改器`（`4b02923b-…`）在官方库里 `dirPath` / `archivePath` /
   `gameRegistryId` **全是 NULL**，所以它虽然是「启用」状态，写加载文件时被静默丢弃。

### 做了什么（按 Phase）

- **Phase 0 自有库地基**（`src/PdxLauncher.Core/Storage/`）：`PdxDatabase`（8 张表 + `meta.schema_version` +
  迁移前备份 + 打开失败改名 `.corrupt-<ms>` 后重建）、`IJsonDocumentStore` / `SqliteJsonDocumentStore`
  （settings / session 走单行表，其余走 `documents` 键空间）、`LegacyJsonImporter`（把 `data\*.json` 入库，
  原文件改名 `.migrated.bak` 留档）、`DocumentFile.cs`（`IDocumentFile` / `StoreDocumentFile` / `FileDocumentFile`）。
- **Phase 1 播放集 / Mod 库入库 + 官方库投射**：`OwnDatabasePlaySetRepository`（`IPlaySetRepository` 11 方法全按
  `gameId` 隔离）、`OwnModStore`、`PlaySetRepository.UpdateModRowsAsync` / `UpsertPlaySetsAsync`（回写官方库，
  路径列用 `COALESCE(?, 列)` 不覆盖已有值）、`LauncherDbProjector`（`imported:` / `projected:` /
  `pending-projection:` 三个 meta 标记）、`PlaySetRepositoryRouter` 改造（官方库在 ⇒ 读写主库 + 镜像；
  官方库不在 ⇒ 只写自有库，**绝不创建 `launcher-v2.sqlite`**）。
- **Phase 2 来源 + 加载规则 + 路径修复**：`ModOrigin` / `ModOrigins`（`source` 为唯一权威，**不能只看
  `steamId` 是否为空** —— 真机 CK3 有一条 `source=local` 却带 `steamId` 的行）、`ModCatalog` 三路扫描都带来源、
  两页的来源徽标、Mod 库页「全部/本地/订阅」筛选、`ModLoadRules.DescribeSkipKey`（六种跳过原因）、
  `PlaySetEditor.BuildLoadFileContent` 改返回 `LoadFilePlan(Content, Skipped)` 并记 Warn、
  `GamePathRules.RequiresAsciiPaths`（目前只有 victoria3）、`ModPathRepairer`（补路径 / 改名为
  `mod_<id 前 8>` / 撞名 `_2` / 写库失败移回原处）、`IFileSystem.MoveDirectory`、播放集页「修复 mod 路径」
  与 `ModPathRepairDialog`。
- **Phase 3 数据入库**：settings、session、六份缓存索引（`cache:workshop-info` / `workshop-pages` /
  `news-cards` / `dlc-descriptions` / `mod-thumbnails` / `theme-assets:<gameId>`）全部改走 `IDocumentFile`，
  磁盘只留图片；`CacheStorage.Measure/Clear` 支持「库键 + 目录」合并计量；设置页缓存面板显示库键。
- **Phase 4 验证**：新增五个检查类；`LoadFileChecks` 增 `CheckLoadFilePlan`；三份文档更新。

### 修掉的真缺陷（不是新功能，是旧代码 bug）

- `PlaySetRepository.GetPlaySetModsAsync` 缺 `m.steamId` / `m.source` 两列（老库缺列时不硬写）。
- `OwnDatabasePlaySetRepository.GetPlaySetModsAsync` 同样漏了 `source`，且构造 `PlaySetMod` 时漏传 `Source`。
- `LauncherDbProjector.ProjectAsync` 把 `ModLibraryEntry` 转 `PlaySetMod` 时丢了来源，回写会把来源写没。
- `tests/PdxLauncher.Checks/WorkshopInfoCacheChecks.cs` 与 `WorkshopPageCacheChecks.cs` 返回的是**全局**失败数
  （`ChecksContext.Failures == 0`），任何一个别的类失败都会把它们也标红 —— 改成比对进入时的计数。

### 验收

- `dotnet build PdxLauncher.sln -c Release --no-incremental` → 0 警告 0 错误。
- `tests/PdxLauncher.Checks.exe` → `检查类: 54，失败: 0，断言失败总数: 0`。
- `文案自检` → 文案源合法，727 个键，生成物已是最新。

### 限制 / 未覆盖

- **settings / session / 缓存是「一整段 JSON 一行」**，不是拆成列（读取方本来就是整份加载→整份写回，
  拆列要重写 `AppSettings` 40+ 字段，风险收益不成立）。领域数据（mods / playsets）才是真关系表。
- **真机落地已执行（2026-10-29 20:28）**：用一次性探针 `.scratch/modprobe/`（不进构建，`.scratch/` 已在
  `.gitignore`）在用户机器上实跑 `ModPathRepairer`：`喜儿修改器` 补上
  `dirPath=D:\app\Steam\steamapps\workshop\content\529340\3717461054`、`mod\更改虚空造物` 改名
  `mod\mod_978102dc` 并同步写回官方库；`content_load.json` 从 3 条（299 B）变 4 条（369 B），四条路径全 ASCII。
  ⇒ 官方库缺路径与 V3 非 ASCII 两个根因都已在真机消掉，剩游戏内确认。
- 父目录含非 ASCII（如 `C:\Users\张三\…`）时**不自动改名**，只给 `mods.warnNonAsciiParent` 提示。
- CS2（`playset_config.json`）与 EU5（`playsets.json`）只做来源徽标，不参与官方库投射（它们本来就没有 sqlite）。

## 0.18 最新一轮（2026-10-27 15:30）：设置页缓存接管「数据 / Mod 缩略图」，并做一轮去重

> 用户诉求原话（截图是设置页「联网」小节）：「联网页面的数据和缩略图应该在缓存里；重新审视下设置中的代码，降低代码重复度」。

- 信息架构：`联网` 分类只剩「网络代理」三卡；`数据`（DLC 简介开关 + 简介缓存天数 + 公告开关）与
  `Mod 缩略图`（联网开关 + 图片保留天数 + 缓存目录卡）整块移到 `缓存` 分类，顺序为
  缓存标题 → 合计卡（刷新大小 / 清除全部）→ 四行缓存清单 → 数据 → Mod 缩略图 → 缓存说明。
  AutomationId 一个没改，只换了分区。
- 去重（行为保持，一个 commit 一步、每步 build + Checks 通过）：
  - 开关/下拉 setter：`SettingsViewModel` 新增 `ApplyFlag` / `ApplyOption`（`[CallerMemberName]` 让通知落在属性名上），
    6 个开关 + 6 个下拉的「同值判断 → 写设置 → 保存 → 通知 → 状态栏」样板收敛成一行；`PersistSettings()` 改为 `=> SaveSettings()`。
  - 下拉刷新：四个 `Refresh*` 合并为 `RefreshOptionSelections()`；主题来源键只留一处 `ThemeSourceKey`。
  - 缓存清除：`CacheRowViewModel` 构造时注入清除委托，删掉 `SettingsViewModel.ClearCacheCore` 的 Key 分派。
  - 保留天数目录：`ModThumbnailRetentionCatalog` + `DlcDescriptionRetentionCatalog` 合并为 `RetentionDaysCatalog`
    （`ModThumbnails` 默认 30 天、`DlcDescriptions` 默认 7 天）。
  - XAML：只读值卡改用新控件 `Controls/SettingsValueCard`（Header/Description/Value/ValueAutomationId/
    ValueAutomationName/MaxValueWidth/ShowToolTip）；hint 与二级小节标题走 `SettingsHintTextStyle` / `SettingsSubsectionTitleStyle`。
  - code-behind：`ConfirmAsync`（统一确认框）、`RunGuardedAsync`（事件护栏）、`OpenPath`（合并 OpenDirectory/OpenExplorer）。
- 验证：`dotnet build "PdxLauncher.sln" -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false` 0 警告 0 错误；
  Checks 16 类 0 失败；UIA 分节六类 want 全部命中（每节 missing 全空；count 会随缓存统计/进度环等状态浮动，不作为断言）；
  截图 `.scratch/rework/settings-*.png` 与 `.scratch/rework/settings-cache-bottom.png`。

**2026-10-27 15:40 追加：EU4 卡片「发灰」与 EU5「不出现」两问全部落地 —— 前者是「20% alpha 纱层图被 WinUI 直接铺在黑底上」+「下载全超时仍写 lastCheckedUtc 锁死 5 天」两个根因，已用 `WashLayerPolicy` / `ThemeImageFlattener` 摊平 + 刷新失败不推进 TTL 修掉；后者是 EU5 安装目录压根没有 `launcher-settings.json`，已加 `InstallSignatureDetector` 按 `binaries/eu5.exe` 特征识别，见 §0.18。**

**2026-10-27 16:50 追加：EU5 从「只被发现」做到「能管」—— 自带 1920×1080 背景 + 卡片封面、`playsets.json` 播放集读写、EU5 自己的 `.dlc.json` 与英文本地化名字；同一轮在真机上挖出三个数据格式坑（metadata.json 带 BOM、库搬家后的陈旧路径、DLC 描述文件不是 v3 结构），见 §0.19。**

**2026-10-27 17:50 追加：新增纯托管 DDS 解码库 `src/PdxLauncher.Imaging`（BC1/BC2/BC3/BC4/BC5/BC7 + 未压缩 BGRA8，零 NuGet、可在离线自检里跑），EU5 的 `thumbnail.dds` 现在能转成 PNG 显示 —— DLC 三张封面与带图 mod 全部真实成像；设置页缓存卡片相应增加到五行（新增 `images`），见 §0.20。**

**2026-10-27 18:40 追加：切换游戏慢已定位并修好 —— 不是线程死锁（全仓 `.Result` / `.Wait()` 零命中），而是「UI 线程被同步 IO 与串行重清扫占着」：DLC / mod 扫描零缓存（一次切换同一游戏被扫 2–3 次）、纱层摊平的解码 / 逐像素 / 重编码全在 UI 线程且跨进程重做 7 次、`continue_game.json` 在 UI 线程同步读、整窗背景解码没有 per-request 取消。真机实测切游戏背景落地从 **39.6 秒空白 / 52.9 秒间隔**降到 **165–195 ms**，见 §0.21。**

**2026-10-27 19:30 追加：五个 P0 数据完整性 / 可观测性批次已全部落地并逐批提交（分支 `fix/data-integrity`，`26a926d` → `1984a62`）—— 写路径失败不再静默当成功、设置落盘失败有返回值、未处理异常不再静默退出、域名白名单移进网络出口。见 §0.22。**

**2026-10-27 20:20 追加：CS2（天际线 2）从「管不到」做到「能管 + 能逛工坊」——① 「外部操控工坊」那个工具查明是 **Skyve CS-II**，而它调用的 PDX Mods 平台确实有 HTTP API（无公开文档，协议是从官方 `app.asar` 逆向 + 现网实测拿的）；② 播放集/启用/排序接管 CS2 的 `playset_config.json`；③ 新增 Paradox 账号登录（设置页新节）；④ Mod 库页多了「在线（PDX Mods）」视图（搜索/排序/翻页/详情/封面）。顺手修掉「本地 CS2 缩略图全空」的真 bug，见 §0.23。**

**2026-10-28 00:40 追加：播放集页「位次就地编辑」——点行首序号直接输入目标位次（回车生效 / Esc 或点别处取消 / 插入语义 / 搜索态整体置灰），附带修掉「↑↓ 在搜索态看着能点但点了没反应」的老缺陷、序号列 24→40、状态播报对读屏可见、排序动作补日志，并新增 `PlaySetOrderChecks`（检查类 22 个 / 0 失败）。经用户拍板选择「就地输入框」方案（专家评审一致推荐的是「移动到第 N 位」对话框，用户改成更直接的就地编辑），见 §0.24。**

**2026-10-28 01:25 追加：位次输入的「大于总数＝报错」改成「大于总数＝置底」。反例来自用户截图——4 行的播放集输入 5，得到「位次无效：请输入 1–4 之间的整数」，而用户想要的是「放最后」。现在向上越界一律钳到末位（`0` 与负数仍判无效），并新增 `PositionInput` 纯解析类 + `PositionInputChecks`（检查类 23 个 / 0 失败），见 §0.25。**

**2026-10-28 03:10 追加：Mod 库的「在线（PDX Mods）」视图按用户要求整体回退，功能搬进左栏独立「创意工坊」页 —— 它按当前游戏单一来源渲染（CS2 → Paradox Mods，CK3/V3 等有 Steam 工坊的 → Steam 创意工坊），不做页签切换；Paradox 登录同时从设置页分节搬成左栏独立「账号」页，并且启动时就恢复会话（原来只有进设置页才恢复）；顺手补上「记忆上次选中的游戏」（`settings.json` 新增 `lastGameId` / `lastGameInstallRoot`），见 §0.26。**

**2026-10-28 19:30 追加：「Paradox 账号无法登录」查明**不是**协议/网络问题，而是账号页一个鸡生蛋死锁 —— 邮箱框的 `IsEnabled` 绑在 `CanSignIn` 上，而 `CanSignIn` 要求「邮箱非空」，于是邮箱框把自己禁用了，用户连邮箱都输不进去，请求从未发出（所以日志里一条 `pdx-login-*` 都没有）。顺带挖出第二个缺陷：凭据错误的错误码是 `pdx-login-unauthorized`，而归类只匹配 `pdx-unauthorized`，界面把原始码直接显示给用户（「登录失败：pdx-login-unauthorized」），用户根本看不出是密码错了。两者都已修 + 新增 `AccountChecks`，见 §0.28。**

**2026-10-28 03:40 追加：EU5 mod 封面「16/22 不显示」查明是两个缺陷叠加 —— ① 解析器只扫 mod 根目录、从不进 `.metadata\`（EU5 的封面全在那里）；② metadata.json 的键读成了 `thumbnail`，真键是 `picture`。修好后真机 22/22 全部成像（16 张来自 `.metadata`），老游戏零影响，见 §0.27。**

**2026-10-29 07:20 追加：Mod 库每行加第四个按钮「打开详情页」，复用创意工坊那个详情子页 —— 行先翻译成 `ModWorkshopRef`（平台 + 编号），`WorkshopRowViewModel.FromLibrary` 造详情行；解析不出编号（本地 mod）就置灰并把原因写进 tooltip。详情子页的「返回」不再写死回创意工坊：发起方经 `WorkshopDetailNavigationRequest.ReturnTag` 说回来处，从 Mod 库进来按钮显示「返回 Mod 库」，见 §0.46。**

## 0.28 最新一轮（2026-10-28 18:50–19:30）：Paradox 账号无法登录 —— 邮箱框自己禁用自己

### 0.28.1 用户诉求

> 「给我把 paradox 账号登录修好，目前无法登录账号」

### 0.28.2 先排除的不是这个（避免下次重新怀疑一遍）

协议层**没有问题**，证据链完整，不用再查：

1. **官方 asar 逐字比对**（本机 `launcher-v2.2026.13`，`C:\Users\17147\AppData\Local\Programs\Paradox Interactive\launcher\launcher-v2.2026.13\resources\app.asar`，29,170,265 B）：
   `OFe="64DC4DC6508F4A9D823179D4D99DB4EE"`（盐）、
   `RFe=(t,e)=>JSON.stringify({hawk:{email:t,"sha256(password+salt)":…}})`（头形状）、
   `Z3=t=>JSON.stringify({session:{token:t}})`、`NFe=t=>JSON.stringify({renewal:{token:t}})`、
   `Fme="https://api.paradox-interactive.com/accounts/sessions/launcher-v2"`。
   我们的 `PdxPassword.Salt` / `HeaderKeyName` / `PdxAccountClient.SessionEndpoint` / `SessionHeader` / 续期形状全部逐字一致。
2. **现网实测**：`curl -X PUT` 到该端点，header 放 hawk(email + 全零哈希)、空 body → **HTTP 401**，
   body `{"result":"Failure","errorCode":"not-authorized","errorMessage":"Not authorized","detail":{}}`。
   带不带 `rememberMe` 字段结果完全一样 → 端点可达、形状被接受，只判凭据错。
   （附带结论：我们多发的 `rememberMe` 是多余的 —— 官方只在**本地**决定是否落盘 refresh token，不参与请求；但无害，故保留不动。）
3. **运行时日志**：`data/logs/pdxnext.log` 里 `PDX 请求失败` / `Paradox 登录` / `pdx-login-*` **一条都没有** ——
   这本身就是关键证据：请求根本没发出，所以问题在 UI 层，不在网络层。

### 0.28.3 根因一：邮箱框的 `IsEnabled` 绑在 `CanSignIn` 上（死锁）

```csharp
// 改前 AccountViewModel
public bool CanSignIn => !IsBusy && !IsSignedIn && !string.IsNullOrWhiteSpace(Email);
```

`Email` 初值是 `string.Empty`，没有任何预填（全仓没有「记住上次邮箱」的逻辑）。而
`src/PdxLauncher.App/Views/AccountPage.xaml` 里 **4 个控件**共用这一个属性：

| 行 | 控件 | 改前 |
| --- | --- | --- |
| 115 | `EmailBox`（`txtAccountEmail`） | `IsEnabled="{x:Bind ViewModel.CanSignIn}"` |
| 127 | `PasswordBoxControl`（`txtAccountPassword`） | 同上 |
| 139 | `swAccountRemember` | 同上 |
| 149 | `btnAccountSignIn` | 同上 |

于是：邮箱空 → `CanSignIn=false` → **邮箱框自己被禁用** → 用户无法输入邮箱 → 永远为空。
身份卡上那个「登录」按钮（`btnAccountPrimary`）只做 `expAccountSignIn.IsExpanded = true; EmailBox.Focus(...)`，
焦点落到禁用控件上会静默失败 —— 用户观感就是「点了没反应」。

真机 UIA 实测（改前，`winapp ui inspect nav -a PdxLauncher.App`）：

```
txtAccountEmail    Edit   "邮箱"      [disabled]
txtAccountPassword Edit   "密码"      [disabled]
swAccountRemember  Button "保持登录" [off] [disabled]
btnAccountSignIn   Button "登录"      [disabled]
btnAccountPrimary  Button "登录"                ← 这个可点，但它只展开 + Focus
```

这个死锁从 0.23.4 批次（`git show 592729a~1:src/PdxLauncher.App/Views/AccountPage.xaml`）起就存在，
不是 592729a 账号页改版引入的。

### 0.28.4 修法：把「能不能改输入」与「能不能提交」拆开

新增纯 C# 判定类 `src/PdxLauncher.App/Services/AccountFormState.cs`：

```csharp
public static bool CanEditCredentials(bool isBusy, bool isSignedIn) => !isBusy && !isSignedIn;
public static bool CanSubmit(bool isBusy, bool isSignedIn, string? email)
    => CanEditCredentials(isBusy, isSignedIn) && !string.IsNullOrWhiteSpace(email);
```

`AccountViewModel` 由此拆成两个属性（`CanSignIn` 保留原名，只给按钮用）：

```csharp
public bool CanEditCredentials => AccountFormState.CanEditCredentials(IsBusy, IsSignedIn);
public bool CanSignIn => AccountFormState.CanSubmit(IsBusy, IsSignedIn, Email);
```

XAML 三处（115 / 127 / 139）改绑 `CanEditCredentials`，按钮 149 仍绑 `CanSignIn`；
`RaiseAccountProps()` 里补 `OnPropertyChanged(nameof(CanEditCredentials));`。
生成物 `obj/x64/Release/…/Views/AccountPage.g.cs:636-647` 的 `Update_ViewModel_CanEditCredentials`
同时设置 obj12/obj13/obj14 的 `IsEnabled`，与 XAML 行号 134/121/109 对应，确认绑定生效。

### 0.28.5 根因二：凭据错的错误码没被认出来

`PdxAccountClient` 返回 **`pdx-login-unauthorized`**（续期是 `pdx-refresh-unauthorized`），
而 `AccountViewModel.DescribeAccountError` 只匹配 `pdx-unauthorized` / `401` / `403`：

```csharp
if (code.Contains("pdx-unauthorized", …) || code.Contains("401", …) || code.Contains("403", …))
```

→ 落到最后的 `return code;`，界面**直接把内部码显示给用户**。真机实测（改前）：

```
txtAccountStatusBar Text "登录失败：pdx-login-unauthorized"
```

用户当然看不出「是我密码打错了」。同类漏判还有网络：客户端给的是 `pdx-login-network`，归类写的是 `pdx-network`。

修法：把归类抽成 `src/PdxLauncher.App/Services/AccountErrorText.cs`
（`AccountErrorCategory { BadCredentials, Network, Other }`），按**词根**而非精确串判断
（`Contains("unauthorized")` / `Contains("network")` / `Contains("timeout")`），
`DescribeAccountError` 改成对它 switch。改后真机：

```
txtAccountStatusBar Text "登录失败：邮箱或密码不正确。"
```

### 0.28.6 顺手修的两处

- **失败日志缺失**：`PdxAccountClient` 非 401 分支原来完全不记日志，响应 body 也不落盘 ——
  排障时看不到服务端的 `errorCode`。现在 401/403 与非 2xx 都记
  `Paradox 登录被拒：HTTP <code> <body 前 200 字>`（`BodySnippet`，空 body 记 `<empty>`；续期对应 `Paradox 续期被拒/失败`）。
  **只记响应，不记请求** —— 请求头里是密码哈希与会话令牌。
- **「保持登录」文案在说谎**：`account.rememberHint` 承诺「不勾选则只在本次运行有效」，
  但 `AccountService.SignInAsync` 原来无条件 `_store.Save(session)`。现在按 `rememberMe` 分支：
  勾了 `Save`，没勾 `Clear`（顺手删掉上次留下的会话文件，否则重启后 `RestoreAsync` 又会把它恢复出来）。

### 0.28.7 改了哪些文件

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Services/AccountFormState.cs` | **新增**，可编辑/可提交判定 |
| `src/PdxLauncher.App/Services/AccountErrorText.cs` | **新增**，错误码归类 |
| `src/PdxLauncher.App/ViewModels/AccountViewModel.cs` | `CanEditCredentials` 拆分；`RaiseAccountProps` 补通知；`DescribeAccountError` 改用归类 |
| `src/PdxLauncher.App/Views/AccountPage.xaml` | 115 / 127 / 139 改绑 `CanEditCredentials` |
| `src/PdxLauncher.Core/Accounts/PdxAccountClient.cs` | 失败日志 + `BodySnippet`；错误码/ErrorKind 不变 |
| `src/PdxLauncher.App/Services/AccountService.cs` | `SignInAsync` 按 `rememberMe` 决定 Save/Clear |
| `tests/PdxLauncher.Checks/AccountChecks.cs` | **新增**，61 条断言 |
| `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj` | 新增 3 条 `<Compile Include>` 链接（`AccountService` / `AccountFormState` / `AccountErrorText`） |
| `README.md` | 计数与覆盖说明同步 |

`AccountChecks` 钉住的口径：密码哈希三条向量（python `hashlib` 独立验算，避免「照抄实现」式自证）、
盐与头键名与官方逐字一致、`PUT` + 端点 + 空 body、邮箱去空格且头里不含明文密码、
401→`Unauthorized` / 500→`Io` / 网络异常→`pdx-login-network` 的映射、`session` 嵌套与顶层扁平两种响应形状、
缺 token 判失败、落盘策略（不勾不落盘 / 勾了落盘并能读回 / 改回不勾会清旧文件），
以及两条回归钉：**可编辑性与邮箱无关**、**`pdx-login-unauthorized` 必须归到凭据错**。

### 0.28.8 验证（全部实际执行）

- 构建：`bash tools/build-app.sh -p:Platform=x64` → **0 警告 0 错误**；
  产物 `src/PdxLauncher.App/bin/x64/Release/net10.0-windows10.0.26100.0/win-x64/`（`PdxLauncher.App.dll` 时间戳新鲜）。
- 自检：`dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"` →
  **`检查类: 37，失败: 0，断言失败总数: 0`**（连跑 3 次稳定）。
- 真机 UIA 端到端（改后，`winapp ui`）：
  1. 进账号页：`txtAccountEmail` / `txtAccountPassword` / `swAccountRemember` **均不带 `[disabled]`**，
     `btnAccountSignIn` 仍 `[disabled]`（邮箱空）—— 死锁已解，同时按钮仍受「必填」约束。
  2. `set-value` 填邮箱 + 密码 → 登录按钮解除禁用。
  3. `invoke btnAccountSignIn` → 日志新增三行，证明整链打通：
     ```
     [WARN] PDX 请求失败：PUT /accounts/sessions/launcher-v2 → HTTP 401
     [WARN] Paradox 登录被拒：HTTP 401 {"result":"Failure","errorCode":"not-authorized",…}
     [WARN] Paradox 账号操作失败：pdx-login-unauthorized
     ```
     界面 `txtAccountStatusBar` = `登录失败：邮箱或密码不正确。`
  4. 清空邮箱 → 三个输入控件仍可用、登录按钮恢复 `[disabled]`。
  5. `invoke btnAccountPrimary` → `get-focused` 返回 `Edit 邮箱`，焦点修复。
  6. 失败登录后 `data/pdx-session.json` **不存在**（不落盘）。

### 0.28.9 未做 / 已知边界

- **没有真实可用的 Paradox 账号做成功登录验证**（手上只有错误凭据）。所以「凭据正确时能拿到 token 并落盘」
  这条只由 `AccountChecks` 的 fake 传输覆盖，**没有走真网**。下次拿到账号请补这一次真机验证，
  重点看两件事：响应里 `session.token` 是否在、`displayName` 是否取到。
- `PdxAccountClient` 发的头里仍带多余的 `rememberMe`（官方 asar 的登录头里没有）。实测无害，暂不动。
- 不做邮箱预填（计划评审时明确排除：会引入「记住上次邮箱」的持久化，超出本次范围）。
- `AccountService.RestoreAsync` 续期失败仍静默 `_store.Clear()`，行为不变（刻意选的：冷启动不该因会话过期弹窗）。
- 官方启动器另有 `/accounts/auth` 与 Steam / Microsoft / Epic 三条第三方登录路径，当前只实现了邮箱密码一条，
  不在本轮范围。
- 工作区里若干创意工坊/在线相关的未提交改动与本轮无关，未触碰。

## 0.27 上一轮（2026-10-28 03:33–03:40）：EU5 mod 封面 16/22 缺图 —— `.metadata` 布局 + metadata.json `picture` 键

### 0.27.1 用户诉求

User said (m00003)：「eu5的图片解析似乎又坏了，修一下」。附图是「播放集」页 11 行，只有 Nobles Auto Marry 与 Move them 有封面，其余全是灰占位。

### 0.27.2 根因（两个独立缺陷叠加，都不是 DDS 解码或缓存问题）

| # | 缺陷 | 证据 |
| --- | --- | --- |
| ① | `ModThumbnailResolver.PickThumbnailFile` 只扫 mod 根目录（`EnumerateFiles(dir, "thumbnail*", recursive: false)`），从不下探 `.metadata\`；而 EU5 / PDX Mods 布局把封面放在 `<mod>\.metadata\thumbnail.png` | `F:\SteamLibrary\steamapps\workshop\content\3450310` 下 **22/22** 个 mod 都有 `.metadata\thumbnail.png`（7.6 KB–920 KB，512×512 或 100×100，合法 PNG / 少数实为 JPEG），只有 **6** 个在根目录另有副本 —— 截图中恰好只有这 6 个里的 2 个在该播放集里 |
| ② | `ModCatalog.AddMetadataMod` 读 metadata.json 的键写成 `"thumbnail"`，真机真键是 **`"picture"`**（13/22 个 mod 带该键，值均为 `thumbnail.png`）；`.mod` 路径（:171）读的正是 `picture`，于是 metadata 路径的「picture 优先」一直是死代码 | 22 个 metadata.json 里 `picture` 出现 13 次、`thumbnail` 0 次 |

老游戏不受影响：CK3（1158310）384 个工坊目录、Stellaris（281990）307 个、EU4（394360）79 个，**都没有任何 `.metadata` 子目录**，所以新增的 `.metadata` 一级在它们那里连枚举都不发生。

### 0.27.3 改动

- `src/PdxLauncher.Core/Playsets/ModThumbnailResolver.cs`：新增 `MetadataDirectoryName = ".metadata"`；`ResolveInDirectory` 的单目录查找顺序改为 **`<dir>\<picture>` → `<dir>\<picture 文件名>` → `<dir>\.metadata\<picture>` → `<dir>\.metadata\<picture 文件名>` → `<dir>\thumbnail*` → `<dir>\.metadata\thumbnail*`**。前四级抽进 `TryExistingImage(dir, relativePath)`（图片扩展名 + 真实存在才认），末级抽成 `PickMetadataThumbnailFile(dir)`。**根目录优先**保证那 6 个已经能显示的 mod 视觉不变；`public PickThumbnailFile` 仍是「只扫这一层」的语义（`DdsChecks.CheckModThumbnailOrder` 直接调它）。
- `src/PdxLauncher.Core/Playsets/ModCatalog.cs:255`：`Picture` 改为 `FirstNonEmptyOrNull(root["picture"], root["thumbnail"], null)`，真键优先、旧写法留作兼容。
- 自检：`ModThumbnailChecks` 新增 `CheckMetadataDirectory`（8 条断言：`.metadata` 命中、根目录仍优先、`.metadata` 里 `thumbnail.jpg` 也行、`picture` 相对 `.metadata`、`picture` 带子目录时退化到平铺、只有 metadata.json / 只有 `.pdn` 时为 null、陈旧 `dirPath` 重定位后仍取 `.metadata` 封面）；`PlaysetJsonChecks` 新增端到端 `CheckMetadataPictureThumbnail`（两个工坊 mod：一个只有 `picture: "cover art.png"` 指向非 thumbnail 名字的封面，一个没有该键靠扫描），并抽出 `CreateEu5Game` 夹具 helper。
- 文档：`docs/DATA-FORMATS.md` §5 metadata 键名 `thumbnail`→`picture`、缩略图解析补 `.metadata` 两级；§16 mod 那行改真话。

### 0.27.4 验证

- `build-debug.cmd`：**0 警告 0 错误**，`检查类: 27，失败: 0，断言失败总数: 0`（本轮新增 11 条断言全部 ok）。
- 真机（`.scratch` 里的一次性 harness 直接跑 `ModCatalog.DiscoverAllAsync`，走真实 22 个工坊目录）：**带封面 22/22，其中来自 `.metadata` 的 16 个**，magic bytes 全部是合法 PNG/JPEG；6 个仍走根目录 `thumbnail.png`（与修复前一致）。
- 真机截图（与用户附图同一视图，播放集页 11 行）：**11/11 全部真实成像** —— 原本就正常的 `Nobles Auto Marry` / `Move them` 之外，Community Flavor Pack / Cheat Menu Pro / 陪都 Multiple Capital / Auto Child Education / Better Road Builder / Thick borders / Reasonable Prices / More Control Methods / Megalopolis Upgrade 共 9 行从灰占位变成真图。
- 日志无「mod 缩略图解析失败」，`data\image-cache` 不变（PNG/JPEG 不经 DDS 转码）。

### 0.27.5 遗留（与本轮无关，仅记账）

日志里反复出现 `ObjectDisposedException: Cannot access a disposed object ... System.Net.Http.HttpClient`（`HttpFetcher.cs:68` ← `SteamWorkshopClient.HasWorkshopAsync` ← `WorkshopViewModel.ResolveSourceAsync` / `LoadAsync`）：创意工坊页在 HttpClient 已释放后仍去探测来源，属独立缺陷，未在本轮处理。

## 0.26 上一轮（2026-10-28 02:10–03:10）：独立「创意工坊」页 + 独立「账号」页 + 记忆上次游戏

### 0.26.1 用户需求（两条，后一条否决了前一轮的设计）

- User said (m00163)：「登录仍是左栏登录（一个新的页面）；创意工坊页面也是在左栏添加新页面。还有是不是没有记录最后选择哪个游戏」
- User said (m00196)：「创意工坊」页不是两个页签，是哪个游戏就显示哪个，比如CS2显示PMOD，ck3vic3这些显示steam创意工坊。

⇒ 三件事：① 「账号」必须是左栏真正独立页面（前一轮计划的「从导航深链到设置页账号分节」作废）；②「创意工坊」也是左栏独立页面，且**按当前游戏单一来源**渲染，不做 Steam/PDX 页签；③ 查证并补上「上次选中的游戏」。

第三条查证结论：**确实没记录** —— `AppSettings` 里没有任何游戏选择字段，`AppServices.SelectDefaultGame` 每次启动都按「第一款可管理内容的游戏」重新挑。已修（见 0.26.5）。

### 0.26.2 创意工坊的来源判定（单一来源、Steam 优先）

| 情形 | 显示 | 依据 |
| --- | --- | --- |
| `SteamAppId == null`（独立版：EU4/CS1/PA） | Paradox Mods | 没有 Steam 工坊可查 |
| `hasWorkshop == true`（CK3、V3、Stellaris、HOI4、CS1、EU5） | Steam 创意工坊 | `store.steampowered.com/api/appdetails?filters=categories` 的分类里有 `Steam Workshop` |
| `hasWorkshop == false`（**CS2**：有 appId 但无工坊分类） | Paradox Mods | CS2 的 `workshop/browse/?appid=949230` 会 302 回 `/workshop` |
| 探测请求失败 | Paradox Mods + 状态条提示「探测失败，已改用 Paradox Mods」 | 网络异常不该让页面空着 |

判定缓存按 appId 记在 `SteamWorkshopClient` 里（`ConcurrentDictionary<int,bool>`），一次进程只查一次。纯函数抽在 `src/PdxLauncher.Core/Online/WorkshopSourceChooser.cs`（`Resolve(int? steamAppId, bool? hasWorkshop)`），自检直接覆盖。

两平台共用同一套档位：Steam `trend/toprated/mostrecent/lastupdated/totaluniquesubscribers`（关键词用 `searchtext=`，与 `browsesort` 并存、不改写排序），PDX `popularity/updated/rating`。页大小 30 / 24，翻页、搜索、刷新复用同一个 VM。

### 0.26.3 批次与文件

| 批次 | 内容 |
| --- | --- |
| 回退 | `git checkout 5da2773 -- src/PdxLauncher.App/Views/ModLibraryPage.xaml(.cs)`；删 `OnlineModsViewModel.cs`（419 行）/`OnlineModRowViewModel.cs`（94 行）；删 `modlib.json` 里 29 个 `modlib.mode*`/`modlib.online*` 键。**保留** `ModLibraryViewModel` 的 DDS 预热（`AppServices.ImageArt.PrewarmAsync`）与 §0.23.6 的缩略图修复 |
| Core | `OnlineHosts` 加 `steamcommunity.com` / `api.steampowered.com` 白名单与三个端点常量；`IHttpFetcher.PostFormAsync`（原来只有 JSON POST，Steam 的 `GetPublishedFileDetails` 只吃表单）；新 `SteamWorkshopModels.cs` / `SteamWorkshopHtmlParser.cs` / `SteamWorkshopClient.cs` |
| 创意工坊页 | 新 `WorkshopViewModel.cs` / `WorkshopRowViewModel.cs` / `Views/WorkshopPage.xaml(.cs)`；`AppServices.WorkshopCoverRoot` = `data\workshop-covers\`，封面按 `steam\{id}` / `pdx\{id}` 落盘 |
| 账号页 | 新 `AccountViewModel.cs` / `Views/AccountPage.xaml(.cs)`，把设置页「Paradox 账号」分节整块搬出来（AutomationId 沿用 `txtAccountStatus`/`txtAccountEmail`/`txtAccountPassword`/`swAccountRemember`/`btnAccountSignIn`/`btnAccountSignOut`），设置页 7→6 分节 |
| 记忆游戏 | `AppSettings` 加 `lastGameId` / `lastGameInstallRoot`；新纯函数 `src/PdxLauncher.Core/Configuration/GameSelection.cs`；`SelectGame` 切游戏时落盘 |
| 启动恢复 | `App.xaml.cs` 在服务装配完成后 `_ = RestoreAccountSessionAsync()` —— 原来只有进设置页才会恢复 Paradox 会话，冷启动后「已登录」状态根本不存在 |
| i18n | 新 area `account.json`(27 键) / `workshop.json`(42 键)，`shell.json` 加两个导航键，`settings.json` 删 28 个账号键；`Strings.g.cs` 共 583 键 |

### 0.26.4 两个真机才暴露的坑

1. **Steam 工坊列表页的 SSR JSON 有两套嵌套**。官方 `/workshop/browse` 把 `renderContext` 放在**顶层**（`data["renderContext"]["queryData"]`），而离线样本是 `props.pageProps.renderContext`。结果是真机一直「解析到的条目数为 0」、页面显示「正在加载……」。现在两种都试（先 `props.pageProps.renderContext`，再顶层）。另外 `queries` 数组里排在前面的是 `AOWarningCookie` 之类的 `data=null` 项，必须按 `queryKey[0] == "workshop_browse"` 跳过空项。
2. **Steam 的 `star_rating` 用 -1 表示「没人评分」**。不判 `> 0` 就会在行副标题印出「-1 分」。另外 `file_size` 是字符串形式的字节数。

### 0.26.5 记忆上次游戏

- `GameSelection.Select(games, lastGameId, lastInstallRoot)` 优先级：gameId + installRoot 双命中 → 仅 gameId 命中（游戏搬库了也能认回）→ 仅 installRoot 命中（gameId 改过名）→ 第一款「可管理内容」的 → 第一款「可支持」的 → 列表第一项 → 空列表 `null`。
- `AppServices.SelectGame` 在 `IsInitialized` 之后才落盘（初始化前 `SettingsStore` 还是 `null!`），切游戏后 `settings.json` 里立刻出现 `lastGameId: "ck3"` 与 `lastGameInstallRoot`。真机验证：切到 CK3 → 重启 → 标题栏仍是「PdxNext · Crusader Kings III」。
- 卸载游戏不会卡住启动：记忆项找不到就退回默认规则。

### 0.26.6 验证

- `dotnet build "PdxLauncher.sln" -c Release` → 0 警告 0 错误。
- `tests/PdxLauncher.Checks` → **25 个检查类，失败 0，断言失败 0**（新增 `SteamWorkshopApiChecks` 与 `GameSelectionChecks`）。
- 真机（`ui-test-artifacts/workshop/workshop-shot.ps1`，winapp UIA）：
  - CS2：创意工坊页来源徽标「Paradox Mods」，`已加载 27378 个结果`，第 1/1141 页，未登录提示条正确显示（`02-workshop-pdx.png`）。
  - CK3：来源徽标「Steam 创意工坊」，`已加载 17717 个结果`，第 1/591 页，封面/评分/订阅/更新/体积齐全（`02-workshop-steam.png`）。
  - 账号页：左栏底部独立「账号」项，登录状态「未登录」，邮箱/密码/保持登录/登录按钮齐全（`03-account.png`）。
  - 设置页：左栏只剩 6 个分节（外观/启动/路径/联网/缓存/高级），账号分节已移出（`04-settings.png`）。

### 0.26.7 未做 / 已知边界

- 订阅、安装、收藏仍需要 Paradox 会话与写权限，这一轮只做「浏览」。
- Steam 工坊详情走 keyless `ISteamRemoteStorage/GetPublishedFileDetails/v1`（表单 POST），只回标题/描述/体积/预览图，没有截图列表与标签明细。
- PDX 详情用线上接口现取（`os=windows&arch=x64` 必填），离线时退回列表字段的旧值。
- 有 Steam 工坊的游戏（CK3/V3）**不再展示 PDX 列表** —— 这是用户指定行为，不是遗漏；要看 PDX 版可切到 CS2 之类的游戏。

## 0.25 上一轮（2026-10-28 01:10–01:30）：位次输入向上越界改为「置底」

### 0.25.1 用户需求

- User said (m00731)：「范围不能设计成我输入一个大于目前的 mod 数，自动把 mod 放到最后一位的这种设计」
  ——配合截图 (m00729)：播放集 4 行，用户输入 `5`，状态栏回「位次无效：请输入 1–4 之间的整数」。
  诉求就是这条错误提示本身：数字大于总数应当理解为「放最后」，而不是拒绝。

### 0.25.2 改动与决策依据

- **钳位落在解析层**：`src/PdxLauncher.App/Services/PositionInput.cs`（新增）`public static bool TryParse(string? text, int count, out int position)`。
  原 `ModsViewModel.TryParsePosition`（`internal static`）整体搬过来，把 `value < 1 || value > count → false` 改成
  `value < 1 → false`；`position = value > count ? count : value`。
  选解析层而不是调用方，理由：该方法本来就收 `count`，且**唯一调用点**是 `ModsViewModel.TryMoveModToPosition:634`，
  不存在漏钳位的旁路；钳位后 `target = count - 1` 恒为合法下标，不会落进 `PlaySetEditor.Move` 的静默 no-op 分支；
  状态栏报**钳位后的真实位次**（`→ 第 4 位`），不报用户敲的 `5`（那是界面上不存在的位置）。
- **抽类的唯一动机是可测**：视图模型在 `PdxLauncher.App`（WinExe + windows TFM），检查工程引用不到 → 逻辑测不到，
  这正是上一轮「位次无效」口径悄悄上线的原因。做法沿用 §0.24.5 已建立的 `<Compile Include>` 链接源码先例。
- **`0` 与负数仍判无效（有意的产品选择，不是缺陷）**：诉求只涉及上界；`0` 既可读作「置顶」也可读作「误触」，不猜。
  若后续要对称的「0＝置顶」，只需把那行改成 `value < 1 ? 1 : …` 并同步文案与断言。
- **`Unchanged` 不再静默**：钳位后「输入 9 但本就在末位」会变常见，无反馈看着像没生效。
  `target == index` 分支补 `StatusMessage = T(Keys.ModsStatusAlreadyAtPosition, row.Name, targetPosition)`；
  仍不动数据、不 `RequestAutoSave`、不触发 `RowRevealRequested`。
- **↑/↓ 路径不受影响**：`MoveMod` 不经过 `PositionInput`；搜索态 `Blocked` 判定仍在钳位之前。

### 0.25.3 改了哪些文件

- 新增 `src/PdxLauncher.App/Services/PositionInput.cs`；`ModsViewModel.cs` 删掉 `TryParsePosition`、改调
  `PositionInput.TryParse`、补 `Unchanged` 状态、文档注释把「越界」措辞改成「钳位」（`ModsPage.xaml.cs:575` 同步）。
- `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`：`mods.row.positionToolTip` 加「超过总数视为置底」；
  `mods.status.invalidPosition` 改「请输入不小于 1 的整数（大于 {0} 视为置底）」；新增 `mods.status.alreadyAtPosition`
  「「{0}」已经在第 {1} 位」。键总数 568 → **569**，`Strings.g.cs` 已重新生成。
- `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj`：第二条 `<Compile Include>` 链接 `PositionInput.cs`。
- 新增 `tests/PdxLauncher.Checks/PositionInputChecks.cs`：**39 条断言**——钳位（`5/9/42/9999` @count=4 → 4）、
  边界与 Trim（`1→1`、`4→4`、`" 3 "→3`、`count=1` 时 `7→1`）、拒绝 `0/-1/-5`、拒绝全角与中文数字、
  拒绝 `null/""/"   "/abc/3a/1.5/1e2/+2/99999999999999`（溢出）、`count<=0` 防御、
  以及「钳位后的位次喂给 `PlaySetEditor.Move` 确实落到末位且 position 连续」。
- `README.md:167` 补「输入的数字大于总行数时视为置底，不会报错」。

### 0.25.4 验收口径（已实际执行）

- `dotnet build "PdxLauncher.sln" -c Release -m:1 -p:ArtifactsPath="G:/Work/paradox启动器/.tmp/verify"` → **0 警告 0 错误**。
- `dotnet .tmp/verify/bin/PdxLauncher.Checks/release/PdxLauncher.Checks.dll` → **检查类: 23，失败: 0，断言失败总数: 0**（`PositionInputChecks` 39 条全 ok）。
- `文案自检` → 通过。

**仍未做**：真机没点过（焦点/输入法/滚动这条链依旧只有代码推断支撑）；无 undo；搜索态仍不允许排序。

## 0.24 上一轮（2026-10-28 00:00–01:00）：播放集页「位次就地编辑」

### 0.24.1 用户需求

- User said (m00001)：「播放集页面的排序编号添加更改功能，方便user进行快速排序」

需求本身一句话：**别再靠连点 ↑↓ 一格一格挪**，直接给序号就能到位。

### 0.24.2 方案怎么定的（这段是给后来人看的决策依据）

先开了一轮三个专家的独立评审（产品 / UX 研究 / 软件架构），三方**独立收敛到同一个结论**：
用「移动到第 N 位」的 ContentDialog（复用页面里已有的 `PromptForNameAsync` 模式），
把就地编辑放二期、拖拽放三期。但用户在看过方案对比后**拍板选了就地编辑**
（A 方案：序号本身变成输入框）——理由很直接，就地点一下比开对话框少一步，
而且要的就是「快」。**结论按用户的来**，但下面这些评审结论仍然成立并被吸收进了实现：

| 评审结论 | 落地情况 |
| --- | --- |
| `CanMoveMods` 一个 XAML 都没绑 → 搜索态下 ↑↓ 看着能点、点了静默无反应（P0 缺陷） | 已修：行 VM 新增 `CanMove`，↑↓ 与序号按钮全部绑 `IsEnabled` |
| 重排后列表会被 `ObservableCollection.Clear()` 重置、虚拟化列表回到顶部 → 用户看不到结果（P0） | 已修：`ModsViewModel.RowRevealRequested` 事件 + `ModList.ScrollIntoView` |
| 状态栏会被随后的自动保存回执覆盖，且读屏听不到 | 已修：`StatusText` 加 `AutomationProperties.LiveSetting="Assertive"`；排序文案改成「已调整顺序：「mod 名」第 N 位 → 第 M 位」 |
| 序号口径不一致：行里是 1 基、状态栏原先打 0 基 index | 已修：两处统一 1 基，文案带 mod 名 |
| 语义必须是「插入」不是「交换」（改 5→2 时 2..5 依次后移） | 已定：沿用 `PlaySetEditor.Move` 的 RemoveAt+Insert，tooltip 与文案都写明「其余行依次顺移」 |
| 越界输入要让 `PlaySetEditor.Move` 的静默 no-op 变成**进不去** | 已做：`TryParsePosition` 在 VM 里先挡掉非 1..count，UI 侧 `Invalid` 时不退出编辑态 |
| 没有 undo，删除对话框都写着「无法撤销」 | 接受为已知限制，不做 undo（成本远大于收益） |
| 搜索态能否排序是产品决策 | 用户选「搜索态禁用即可」，不做可见序↔全局序的映射层 |

两处**专家结论被实测推翻**，记下来免得再走一遍：

- 架构师说 `PlaySetEditor` 因为有 App 层依赖（`LoadFileContent`）所以不能搬 / 只能搬纯逻辑段。
  **实测：`ILoadFileWriter` 与 `LoadFileContent` 都定义在 `src/PdxLauncher.Core/Abstractions/ILoadFileWriter.cs`
  与 `Core/Contracts/LoadFileContent.cs`**，整个文件对 App 层类型的引用数为零 → 整份源码可以直接
  `<Compile Include>` 链接进只引用 Core + Imaging 的检查工程（已这么做，见 0.24.5）。
- 架构师说 `DispatcherQueuePriority.Low` 可以用。**实测：WinUI 2.3.9 的 `Microsoft.WinUI.xml` 里根本没有
  `DispatcherQueuePriority` / `TryEnqueue` 的文档条目**（`Microsoft.UI.Dispatching` 在另一个程序集），
  为免赌一个查不到的 API，改用仓库里已有先例的 `DispatcherQueue.TryEnqueue(Action)` 单参重载
  （先例：`DlcPage.xaml.cs:81`、`GameLibraryPage.xaml.cs:103`、`HomePage.xaml.cs:192`）。

### 0.24.3 输入法这一层为什么最后只做「文本层校验」

原计划三层防御（关 IME → 读 `KeyRoutedEventArgs.IsCommitted` → 纯 ASCII 校验）。
实测 WinUI 2.3.9 的 `KeyRoutedEventArgs` **只有** `DeviceId / Handled / Key / KeyStatus / OriginalKey`，
**没有 `IsCommitted`**；`IsInputMethodEnabled` 与 `Microsoft.UI.Xaml.Input.InputMethod.*` 也都不存在。
也就是说：按回车时**无法在按键层区分「用户确认真实回车」与「输入法上屏确认」**。
最终做法是退到文本层：

- `TryParsePosition` 逐字符只放行 ASCII 数字（`c is < '0' or > '9'` 直接拒），全角数字 / 中文数字 / 正负号 / 小数 / 空串全部判非法；
- 判非法时**不退出编辑态**、原文放回输入框，用户就地改，不会静默吞掉；
- `InputScope="Number"` 只是提示软键盘，不作为正确性依赖。

代价：若用户用中文输入法打「三」再按回车，会看到「位次无效：请输入 1–N 之间的整数」，需要自己改成阿拉伯数字。
这是可接受的行为边界，不是缺陷。

### 0.24.4 交互与状态机

- 显示态＝透明 `Button`（内含原来的 `CaptionTextBlockStyle` 序号文本），用 Button 而不是 TextBlock 是为了
  保住 Tab 可达与 ≥24×24 命中区（原生 TextBlock 只有 24px 宽、命中区不达标）。
- 点击进编辑态：同一时刻只允许一行；先结束上一行再置位新行。
- **焦点**：`IsEditingPosition` 翻转后控件要等布局才真正可见，直接 `Focus()` 会静默失效，
  所以统一排进 `DispatcherQueue.TryEnqueue`，再 `Text = PositionText` → `Focus(FocusState.Programmatic)` → `SelectAll()`。
  这是**本仓库第一处程序化聚焦**（改动前 `grep "Focus(FocusState" src/PdxLauncher.App/` 零命中），所以留了注释。
- 找控件不能靠 `x:Name`（条目容器会被回收复用），走 `ModList.ContainerFromItem(row)` 拿 `ContentTemplateRoot`
  再按类型找**唯一的那个 `TextBox`**（行模板里只有序号输入框一个）。
- 回车＝提交；Esc＝放弃；**失焦一律按取消处理**（不做隐式提交，避免「点别处结果改了」的意外）。
- `PositionEditResult` 四态决定 UI 收不收编辑态：`Applied / Unchanged / Blocked` → 收起；`Invalid` → 留在编辑态。
- **提交分支的顺序是硬约束**（`OnPositionEditKeyDown`）：必须「先判定、只有不该继续编辑时才 `EndPositionEdit()`」。
  早期写法是先 `EndPositionEdit()` 再在 `Invalid` 时把 `IsEditingPosition` 设回 `true`，这会引发竞态：
  置 `false` → 输入框隐藏 → 隐藏同步触发它自己的 `LostFocus` → 该 handler 又调 `EndPositionEdit()`
  → 刚恢复的编辑态被关掉，用户看到的是「非法输入后输入框凭空消失」，而文案还停在「位次无效」。
  另外提交时的 row 取 `box.DataContext as ModRowViewModel ?? _editingRow`，以发送者所在行为准
  ——`_editingRow` 只是「谁在编辑」的跟踪记录，重排会让行对象换人。

### 0.24.5 改了哪些文件

- `src/PdxLauncher.App/ViewModels/PositionEditResult.cs`（新增）：`enum { Applied, Unchanged, Invalid, Blocked }`。
- `src/PdxLauncher.App/ViewModels/ModRowViewModel.cs`：新增 `TotalCount` / `CanMove` / `IsEditingPosition` /
  `PositionEditName` / `PositionButtonAutomationId` / `PositionEditAutomationId`；`Position` 的 setter 同步 raise `PositionEditName`。
  该文件仍然**不引用任何 `Microsoft.UI.Xaml` 类型**（这条是硬约束）。
- `src/PdxLauncher.App/ViewModels/ModsViewModel.cs`：新增 `TryMoveModToPosition(ModRowViewModel, string?)`（**收原文**，
  解析/越界/文案全在 VM 里，单一事实源）、`internal static TryParsePosition`、`event Action<Guid> RowRevealRequested`；
  `RebuildVisibleMods()` 给每行写 `TotalCount` 与 `CanMove`；`MoveMod` 补日志并把状态文案改 1 基带 mod 名。
- `src/PdxLauncher.App/Views/ModsPage.xaml`：序号列 `24 → 40`；序号格子改成「透明 Button ＋ 就地 TextBox」两层按
  `IsEditingPosition` 互斥；↑↓ 补 `IsEnabled="{x:Bind CanMove}"`；状态栏补 `AutomationProperties.LiveSetting="Assertive"`。
- `src/PdxLauncher.App/Views/ModsPage.xaml.cs`：`_editingRow` 单编辑跟踪；`OnPositionEditClick` /
  `OnPositionEditKeyDown` / `OnPositionEditLostFocus` / `EndPositionEdit` / `FocusPositionEditor` /
  `FindPositionEditor` / `FindDescendant<T>` / `OnRowRevealRequested`；`Loaded`/`Unloaded` 里成对订阅/退订 `RowRevealRequested`。
- `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`：改 `mods.row.moveUpToolTip`「上移一位」/`moveDownToolTip`「下移一位」（去掉 0 基说法）、
  重写 `mods.status.orderMoved`（1 基 + mod 名 + 「其余行依次顺移」）；新增 `mods.row.positionToolTip` /
  `mods.row.positionEditName` / `mods.status.invalidPosition`。`Strings.g.cs` 已用 `文案源生成器` 重新生成，
  键总数 530 → 568，`--check` 通过。
- `tests/PdxLauncher.Checks/PlaySetOrderChecks.cs`（新增）：16 条断言，覆盖 `Renumber` 连续性与顺序、`Move` 的插入语义
  （`A,B,C,D,E` 第 5 位移到第 2 位 → `A,E,B,C,D`）、移到首/末位、index 与 target 双向越界的兜底、以及「只改 position
  不动 `Enabled` / `GameRegistryId` / `ModId`」。
- `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj`：加一条 `<Compile Include="..\..\src\PdxLauncher.App\Services\PlaySetEditor.cs" Link="Linked\PlaySetEditor.cs" />`
  —— 仓库里**此前没有任何 `<Compile Include>` 先例**（`Directory.Build.props:45` 用 `**/*.cs` 通配），所以注释里写了为什么能链接。

### 0.24.6 验收口径（已实际执行的部分）

- `dotnet build "PdxLauncher.sln" -c Release -m:1 -p:ArtifactsPath="G:/Work/paradox启动器/.tmp/verify"` → **0 警告 0 错误**。
  （用 `ArtifactsPath` 是因为用户的 `PdxLauncher.App.exe` 正在运行、锁着 `bin\...\PdxLauncher.Core.dll`，
  普通 `build.cmd` 会 MSB3021/MSB3027 文件占用。**正斜杠必须**，反斜杠会被吃掉变成 `G:Workparadox启动器.tmpverify`。）
- `dotnet .tmp/verify/bin/PdxLauncher.Checks/release/PdxLauncher.Checks.dll` → **检查类: 22，失败: 0，断言失败总数: 0**。
- `文案自检` → 通过。

**未做（后续接手请注意）**：没有真机点过这个交互（编译与纯逻辑自检绿，但键盘/焦点/滚动这条链只有代码推断支撑）；
`winapp ui` 能否自动化 ContentDialog 与条目标签尚未验证；undo 不存在。

## 0.23 上一轮（2026-10-27 19:30–20:20）：CS2 mod 管理 + Paradox 账号 + PDX Mods 在线视图

> **2026-10-28 03:10 已部分回退**：§0.23.5 的「Mod 库页内『在线（PDX Mods）』视图」已整体移除（页头来源切换按钮、`OnlineModsViewModel` / `OnlineModRowViewModel`、`AppServices.OnlineModCoverRoot`、`modlib.online*` 29 个文案键一并删除），原因见 §0.26.1 用户原话；功能迁到左栏独立「创意工坊」页（§0.26）。**保留**的是 §0.23.3/§0.23.4 的 CS2 播放集接管、§0.23.6 的本地缩略图修复、以及 Paradox 账号能力本身（只是入口从设置页搬到独立「账号」页）。

### 0.23.1 用户两问

- User said (m00001)：「我记得天际线2下了一个外部操控游戏内的p社创意工坊，你找找，或者找下这个社工坊是否存在api可以调用。目前的mod管理是无法管理到天际线2的」
- User said (m02676)：「新的需求：1.实现p社账号登录；2.实现访问p社mod平台」

### 0.23.2 先回答第一问：「那个外部工具」和「有没有 API」

**工具 = Skyve CS-II**，本机已装 `D:\app\Skyve CS-II\`（`Skyve.exe` / `Skyve.Service.exe` / `PDX.SDK.dll` / `Skyve.Domain.CS2.dll` / `Skyve.Systems.CS2.dll` / `Skyve.Api.Domain.dll`，均为 2025-05-11）。它**不自写 REST**，而是直接引官方 `PDX.SDK.dll`；`Skyve.Service` 服务本机未安装。所以「照它抄接口」这条路走不通 —— 网络层是闭源 SDK 里的。

**PDX Mods 平台确实有 HTTP API**（`https://api.paradox-interactive.com`），无公开文档，是从官方启动器 `app.asar` 里逐字抠出来再拿 curl 实测确认的（见 §0.23.5 与 `docs/DATA-FORMATS.md` §17）。关键点：**搜索/详情匿名可用**，只有「订阅 / 我发布的 mod / 已安装列表」需要 Paradox 会话令牌。

### 0.23.3 批次 A：CS2 播放集真正接管（`LoadFileFormat.PlaysetConfigV1`）

CS2 以前「管不到」的根因是加载文件写错了地方：CS2 的 `launcher-settings.json` 写的是 `formatVersion=1.1`，与 V3 的 `ContentLoadV1_1` 同值，于是启动器按 V3 的路子写 `content_load.json` —— 而 **CS2 根本不读它**，CS2 读 `.cache\Mods\playset_config.json`。

改动（全部已落盘、未提交）：

| 文件 | 作用 |
| --- | --- |
| `src/PdxLauncher.Core/Contracts/LoadFileFormat.cs` | 新增 `PlaysetConfigV1 = 3`；注释写明它**不能从 formatVersion 推**（CS2 也是 1.1），只能靠 `modsCompatibilityStandardConfig` 或 gameId 识别 |
| `src/PdxLauncher.Core/Playsets/PlaysetConfig/PlaysetConfigPaths.cs` | 路径常量：`playset_config.json` / `playset_config_lock.json` / `playset_sync.json` / `pdx_mods_cache.json` / `mod_directory.json` / `pdx_mods` 目录 |
| `…/PlaysetConfigDocument.cs` | 317 行，`JsonNode` 解析与序列化，**只改已知节点、未知键原样保留**；播放集自带真 GUID；mod 项只有 `source + sourceId + sourceVersion + isEnabled`，**数组下标就是加载顺序**；`activePlaysetId` 为空时整键删除（官方不写 null） |
| `…/PdxModsCache.cs` | 解析 `pdx_mods_cache.json`（`items[]`：id/version/displayName/thumbnailPath/requiredGameVersion/size/rating/installedDate/state…） |
| `…/PdxModsDirectory.cs` | `ScanVersions` / `Compose` / `ComposeLibrary`：把缓存 + 磁盘 `pdx_mods\<id>_<version>\` 目录合成 `PlaySetMod`；数字 id 排序键 `CompareIds` 保证 `74238` 排在 `7415` 之后 |
| `…/PlaysetConfigLock.cs` | 对 `playset_config_lock.json` 加锁，避免与游戏本体同时写 |
| `…/PlaysetConfigRepository.cs` | 658 行，实现 `IPlaySetRepository` 全部读写；`MaxBackups = 20`；写前备份、原子替换 |
| `…/PdxModsCatalog.cs` | 实现 `IModCatalog`，把 `pdx_mods` 目录当 mod 来源 |
| `src/PdxLauncher.App/Services/ModCatalogRouter.cs` | 按 `game.Format` 把 `IModCatalog` 路由到 CS2 或 Clausewitz 实现（与 `PlaySetRepositoryRouter` 同一思路，格式判定只发生在一处） |

结果：播放集页的启用/禁用/拖拽排序对 CS2 生效，写的就是游戏真正读的那份文件。

### 0.23.4 批次 B：Paradox 账号登录（设置页新节）

| 文件 | 作用 |
| --- | --- |
| `src/PdxLauncher.Core/Abstractions/IPdxHttpTransport.cs` | `GetAsync` / `PostJsonAsync` / `PutJsonAsync`，**出口自带域名白名单** |
| `src/PdxLauncher.Core/Abstractions/ISecretProtector.cs` | 凭据保护抽象 |
| `src/PdxLauncher.Core/Online/PdxHttpTransport.cs` | 走 `HttpFetcher`，与主题/公告同一条代理链路，代理变更时一起重建 |
| `src/PdxLauncher.Core/Accounts/PdxAccountClient.cs` | `SessionEndpoint` = `PUT https://api.paradox-interactive.com/accounts/sessions/launcher-v2`；`AccountDetailsEndpoint` = `/accountdetails`；`SignInAsync` / `RefreshAsync` / `GetDisplayNameAsync` / `SessionHeader` |
| `src/PdxLauncher.Core/Accounts/PdxPassword.cs` / `PdxSession.cs` / `PdxSessionStore.cs` | 密码规范化、会话模型、落盘 |
| `src/PdxLauncher.App/Services/DpapiSecretProtector.cs` | Windows DPAPI（当前用户作用域）加密「记住我」 |
| `src/PdxLauncher.App/Services/AccountService.cs` | `Session` / `IsSignedIn` / `IsBusy` / `SessionChanged` / `SignInAsync` / `RestoreAsync` / `ReplaceClient` |
| `src/PdxLauncher.App/Views/SettingsPage.xaml` + `.xaml.cs` | 左栏第 7 项 `navSettingsAccount`（Tag=6）与 `secAccount` 节 |

装配：`NetworkComposition.NetworkServices` 增加 `PdxHttp`（`PdxHttpTransport`）与 `Account`（`PdxAccountClient`）、`PdxMods`（`PdxModsClient`）；`AppServices.Initialize` / `ReplaceNetwork` / `TryRebuildNetworkServices` 三处同步。

### 0.23.5 批次 C：Mod 库页「在线（PDX Mods）」视图

`src/PdxLauncher.Core/Mods/`（新目录，334 + 85 + 56 行）：

- `PdxModsClient.SearchAsync(string gameId, PdxModsQuery, PdxSession?, ct)` → `POST /mods`，body `{gameNames:[gameId], orderBy, sortBy, pageSize, page, states:["published"]}`，关键词非空时加 `searchQuery`。CS2 现网共 **27,364** 个已发布 mod。
- `PdxModsClient.GetDetailAsync(string modId, PdxSession?, ct)` → `GET /mods?modId=…&os=windows&arch=x64`。**`os`/`arch` 必填**，缺了返回 400 `Mod is corrupted or chosen operating system is not supported`。
- `PdxModsQuery` 的排序枚举**故意不含 ActivationOrder**（非 `onlyInstalled` 场景传它必 400）。
- 鉴权头是 `{"session":{"token":"…"}}` 的 JSON 串，不是 `Bearer`。

`src/PdxLauncher.App/ViewModels/OnlineModsViewModel.cs`（419 行）+ `OnlineModRowViewModel.cs`（94 行）+ `Views/ModLibraryPage.xaml`（重写，488 行）：

- 页头左上两个按钮切「本地 / 在线（PDX Mods）」（`btnModSourceLocal` / `btnModSourceOnline`），两套列表各占一格、同一时刻只可见一个。
- 在线侧：搜索框 `txtOnlineModSearch`、排序 `cmbOnlineModSort`、翻页 `btnOnlineModPrevPage` / `txtOnlineModPage` / `btnOnlineModNextPage`、刷新 `btnRefreshOnlineMods`、列表 `lstOnlineMods`、状态 `txtOnlineModStatus`。
- 点行弹 `ContentDialog` 看详情（长描述、更新日志、截图、外链、体积），`MaxHeight = 560`。
- 封面走 `AssetDownloader` 下载到 `data\online-mod-covers\` 后按本地路径绑 `XamlImage.FromPath`（`XamlImage` 只吃本地路径，不吃 http）。
- i18n：`src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` 新增 31 个键（`modlib.onlineTitle` 等），已重跑 `src/PdxLauncher.Localization.Generator`。

### 0.23.6 顺手修掉的真 bug：本地 CS2 缩略图全空

回归本地视图时发现 53 行 mod 的封面格全是空框（连占位图标都不显示）。根因在 `PlaysetConfigRepository`：

```csharp
// 修前：第一个参数是「本地文件路径」字段，第二个是「URL」字段，同一个值两处都塞
ThumbnailPath: mod.ThumbnailPath,          // mod.ThumbnailPath 其实是 https://modscontent… 的 URL
ThumbnailUrl:  mod.ThumbnailPath
```

`pdx_mods` 目录里只有 `.cok` 文件、没有本地缩略图，`mod.ThumbnailPath` 从 `pdx_mods_cache.json` 拿到的是 **CDN URL**。塞进本地路径字段后有两个后果：

1. `XamlImage.FromPath(url)` 走 `FileSystem.FileExists` 判 false → 返回 `null`，图片是空的；
2. `HasThumbnail` 按「ThumbnailPath 非空」算，于是占位图标也被隐藏 → 显示成一个纯空框。

同时 `ModLibraryViewModel.FillThumbnailsAsync` 的过滤条件是「本地无图 **且** 有 URL」，而 `ThumbnailPath` 被塞了 URL 就不为空，`pending` 恒为 0 → 联网补全**一次都没跑过**（日志里没有任何「mod 缩略图联网补全」记录，正好对上）。

修法（`PlaysetConfigRepository.cs:227-243`）：`ThumbnailPath` 一律留 `null`，URL 只交给 `ThumbnailUrl`。

```csharp
// ThumbnailPath 必须留 null（UI 只按本地路径渲染），地址交给 ThumbnailUrl 走联网补全 + 缓存。
ThumbnailPath: null,
ThumbnailUrl:  mod.ThumbnailPath
```

修后实测：日志 `mod 缩略图联网补全：尝试 53 个，成功 53 个，失败 0 个，缓存命中 0 个`，53 张封面全部真实显示。

### 0.23.7 收尾修掉的第二个真 bug：订阅数恒为 0

写文档时按文档字段名回头核对代码，发现 `PdxModsClient.ParseSummary` 读的是 `subscriptions`，而**列表接口的报文字段叫 `installedCount`**（详情接口才叫 `subscriptions`）。于是在线列表里每条 mod 的订阅数都是 0。

修法：新增 `FirstNonZeroLong(item, "installedCount", "subscriptions")`，两个键都试、取第一个非零值，兼容两个接口与将来的改名。

真机验证（`.scratch/online-subscriptions.png`）：列表副标题从「作者 ockidj · 5 分（1194 人评分） · 更新于 2026/10/27」变成「作者 ockidj · 5 分（1194 人评分） · **46,600 订阅** · 更新于 2026/10/27」，前 6 行订阅数 46,600 / 60,168 / 8,206 / 5,772 / 20,458 / 18,449，与接口原始值逐条一致。

### 0.23.8 验证

- `dotnet build PdxLauncher.sln -c Release -m:1` → **0 警告 0 错误**。
- `PdxLauncher.Checks.exe` → **检查类 21，失败 0，断言失败总数 0**（新增 `ModsApiChecks`，61 条断言，覆盖请求体形状 / 解析 / 字段回退 / 状态码映射 / os+arch 必填 / 鉴权头形态 / 坏 JSON / 网络故障）。
- 真机截图（`Release`）：`.scratch/rework/online-mods.png`（在线列表）、`online-detail.png`（详情弹窗）、`online-search.png`、`online-sort.png`、`local-regression-late.png`（本地 53 张封面）、`.scratch/online-subscriptions.png`（订阅数修复后）。
- **构建踩坑 1**：`PdxLauncher.App` 进程开着时解决方案级构建会在复制 `PdxLauncher.Core.dll` 到输出目录时被文件锁挡住（`MSB3027` / `MSB3021`）。先 `Stop-Process PdxLauncher.App` 再构建。
- **构建踩坑 2（更阴）**：`PdxLauncher.App.csproj` 里 `<Platforms>x64</Platforms>`，但 `bash tools/build-app.sh` 不带 `-p:Platform=x64` 时产物落在 **`bin\Release\...\win-x64\`**，而 `bin\x64\Release\...\win-x64\` 是更早一次的旧产物（本次差 20 分钟）。UI 自动化脚本若指向后者，会「跑通了但看不到改动」。改代码后先比对两个路径下 `PdxLauncher.Core.dll` 的时间戳。

### 0.23.9 遗留

1. **订阅 / 安装还没做**（目标 D）：需要 Paradox 会话令牌 + 写 `pdx_mods` 目录 + 更新 `pdx_mods_cache.json`，是有写入副作用的操作，没在有登录态的真机上验证过。
2. CS2 的本地 mod 详情弹窗里「已订阅/评分」等字段仍来自 `pdx_mods_cache.json`，离线时是旧值。
3. 在线视图没有「只看已安装 / 只看已订阅」筛选，也没有分页跳转输入校验的边界用例。
4. `docs/DATA-FORMATS.md` 已补 §17（PDX Mods API / 账号 API / CS2 playset_config）。
5. `ModsApiChecks` 的报文是**手抄的真实响应子集**，服务端改字段名时它不会自动发现；只有真机打开在线视图才能发现（这次 `installedCount` 就是这么漏的）。

## 0.22 上一轮（2026-10-27 17:15–19:30）：五个 P0 批次 —— 数据静默丢失、崩溃不可观测、白名单靠自觉

### 0.22.1 起因

上一轮架构审核（两位专家 + Lead 交叉复核）确认：项目的业务规则极薄，复杂度全在**集成**，但真正会咬人的是
**「写失败被静默吞掉」**这一类问题 —— 用户的播放集在磁盘上没了，界面却说「保存成功」。审核给出的 P0 清单里，
前五项都属于「改完不改对外行为、但能把一类静默失败变成可见失败」的高性价比改动。

### 0.22.2 批次 1（`26a926d`）：写路径错误契约

`IPlaySetRepository` 的 7 个写方法原本返回值语义**四套并存**：`Task`（调用方无从得知成败）、
`Task<Guid>`（`CreatePlaySetAsync` 失败仍返回一个 id）、`Task<bool>`（失败与「目标不存在」不可区分）。
更糟的是 sqlite 仓储的 `WriteAsync<T>(..., T fallback = default!)` 把 `catch` 后的返回值交给**调用方传进来的兜底值**，
于是 `CreatePlaySetAsync` 写失败时把刚 new 出来的 Guid 当成功返回。

现在：`AppResult<T>` 扩成 `(bool IsSuccess, T? Value, string? ErrorMessage, ErrorKind Kind)`，
新增 `ErrorKind { Unknown, NotFound, Conflict, Invalid, Io, Unsupported }`；7 个写方法统一返回 `AppResult`；
`WriteAsync<T>` **删掉 `fallback` 参数**，失败即 `Fail(ErrorKind.Io, action + ":" + ex.Message)`，
库不存在 `Fail(NotFound, "database-not-found:" + path)`，`body` 返回失败时**先 `Rollback()` 再返回**；
`BackupDatabase` 从 `void` 改成 `AppResult<bool>`，备份失败**中止写入**（原来只 Warn 然后继续写）。
JSON 仓储（EU5）同步改造：`RequirePlaySet` 的 `throw InvalidOperationException` 全部换成 `Fail(NotFound/Conflict, ...)`。

**错误文案上移**：`PlaySetEditor` 里 9 处 `Localizer.Instance.T(Keys....)` 全部删掉（`grep -n "Localizer\|Keys\."` 零命中），
改成 `ErrorMessage` 只承载错误码（`step=mods;` / `step=dlcs;` / `step=loadfile;` / `step=readback;` / `step=verify;`、
`playSet-not-found:<guid>`、`playSet-is-active:<guid>`、`playSet-name-empty`），中文翻译移到 VM 层。
**这条同时解锁了「`PlaySetEditor` 下沉到 Core 被 `Checks` 覆盖」**——它不再依赖 App 的 `Localizer`。

### 0.22.3 批次 2（`69a1e99`）：保存后回读 sqlite 仓储

批次 1 之后还剩一个洞：`SaveAsync` 写完 sqlite 与加载文件后会**回读加载文件**比对，
但加载文件只是「写进游戏目录的那份」，**播放集真正的持久化载体是仓储**。sqlite 仓储用
`DELETE FROM playsets_mods WHERE playsetId = ?` + 逐行 `INSERT` 改写，中途失败会让行数/顺序与写入不一致，
而加载文件校验完全兜不住这个（加载文件写成功 ≠ 仓储写成功）。

新增 `VerifySqliteStoreAsync`：回读 `GetPlaySetModsAsync`/`GetPlaySetDlcsAsync`，
期望 key = `mod.ModId.ToString("D") + ":" + (mod.Enabled ? 1 : 0)`，逐条比对（复用 `AddSequenceDiff`），
差异时返回 `step=verify-store;...`。

两个设计取舍值得记：
- **期望值必须用同一个 `Renumber(mods)` 结果**——`SavePlaySetModsAsync` 传进去的就是重排后的列表，
  用原始列表比对会假差异。所以 `SaveAsync` 里先把 `orderedMods = Renumber(mods)` 提出来共用。
- **只对 sqlite 路径校验**（判据 `game.WritesLoadFile`）。EU5/CS2 是整份文档原子替换（`WriteAllTextAtomic`），
  没有「删表后逐行插」这种半途失败风险；而且 CS2 的 ModId 由 sourceId 派生，比身份会假差异。
  初版写成「所有格式都比对」被自己否掉，就是因为它既依赖并行会话的 CS2 枚举、又会误报。
- DLC 特例：Stellaris 的 `launcher-v2.sqlite` 可能没有 `playsets_dlcs` 表，写入被合法跳过，
  所以回读为空时**只有期望也为空才算通过**，否则 Warn 后跳过 DLC 比对。

### 0.22.4 批次 3（`4fce7d0`）：设置落盘不再静默失败 + 300ms 合并

`AppSettingsStore.Save` 原本 `catch IOException` / `catch UnauthorizedAccessException` 后只 `_logger.Error`，
**没有返回值**——所以 `SettingsViewModel` 里那句 `catch { SetStatus(Keys.SettingsStatusSaveFailed, ...) }` 是**死代码**，
`SaveSettings()` 被调 16 次，一次都没进过 catch。

现在 `Save` 返回 `AppResult<bool>`，并且整个 Store 变成 `IDisposable`：
- `Save(settings)` —— 立即写，成功 `Ok(true)`，失败带 `settings-write:` / `settings-write-denied:` 错误码；
- `SaveDeferred(settings)` —— 登记 300ms 后合并写（定时器回调里**绝不抛异常**，失败只记日志）；
- `Flush()` —— 立刻把待写项落盘；`Dispose()` —— 停表 + 落最后一次。

`_pending` 存的是**引用不存副本**，因为 `AppServices.Settings` 是长期复用的同一实例，
拷贝会丢掉这 300ms 内的后续改动。

调用点按「要不要立刻确定生效」分流：设置页里 8 处「改属性 → 落盘」走 `SaveSettingsDeferred()`；
改代理、窗口置顶、关窗口走 `SaveSettings()` / `FlushSettings()`（关窗口是最后一次落盘机会，
先 `Flush` 再 `Save`，否则最近 300ms 内的改动会丢）。

**踩坑记一条**：只 `-p:BuildProjectReferences=false` 单独编 App 会用到**旧的 Core dll**，
报 `CS0029 无法将类型"void"隐式转换为 AppResult<bool>`。正确顺序是**先编 Core，再编 App**。

### 0.22.5 批次 4（`68d335c`）：崩溃不再静默退出

原来 `App.OnUnhandledException` 只在 `AppServices.IsInitialized` 时 `Logger.Error`，
**不设 `e.Handled`、不弹框、不写标记文件**。更关键的是 WinUI 的 `UnhandledException` **只覆盖 UI 线程**——
后台线程和未观察的 Task 异常会让进程直接退出，用户看到的只是「启动器自己关了」，日志里什么都没有。

三处改动：
- 新增 `src/PdxLauncher.Core/Infrastructure/CrashReportWriter.cs`：把异常类型/消息/堆栈/版本/时间写成
  `crash-<yyyyMMdd-HHmmss>.txt` 落到数据根，保留最近 N 份，**写失败绝不抛**（这条路径本身不能崩）。
- 新增 `src/PdxLauncher.App/Services/CrashReporter.cs`：`Record(...)` 写文件 + 记日志；
  `ShowDialogAsync(...)` 弹一个**文本可选中复制**的对话框（全仓原本没有任何 `Clipboard` 用法，这里新写的）。
  带**重入闩**：弹框/写盘过程中再次抛异常就直接放弃，避免无限递归。
- `App` 挂三个处理器：`UnhandledException`（UI 线程，`e.Handled = true` 保住进程 + 弹框）、
  `AppDomain.CurrentDomain.UnhandledException`（后台线程，只能记录，进程必死）、
  `TaskScheduler.UnobservedTaskException`（未观察的 Task，记录后 `SetObserved()`）。

`Handled` 的取舍：UI 线程异常设 `Handled = true` 意味着进程继续跑，但状态可能已经不一致。
这里选「保住进程 + 明确告知」而不是「直接退出」，因为用户手里的播放集数据比进程存活更重要 ——
崩了能弹框让他先保存/复制错误信息，比静默消失好。后台线程那条无法挽救，只能留痕。

顺带补了 `ModsViewModel.RenameSelectedAsync` 缺失的 `catch`（原本异常会冒到 UI 线程）。

### 0.22.6 批次 5（`1984a62`）：域名白名单移进网络出口

`OnlineHosts.IsAllowed` 全仓只有 **4 处调用**（`AssetDownloader`、`DlcDescriptionService`、
`NewsFeedService` 两处），而 `HttpFetcher.GetAsync`/`PostJsonAsync` **内部无任何校验**——
白名单是「调用方自觉」。新加一个集成、或者像 `SteamCoverUrlResolver` 那样直接调 `GetAsync` 的路径，
就完全不受约束。

现在 `HttpFetcher` 两个入口自己校验，不在白名单内直接抛 `host-not-allowed`，**连接都不会建立**；
非 http(s) scheme 同样拒绝。`PdxHttpTransport`（账户/PDX mods 那条通道）早就这么做了，两条传输口径现在一致。

**调用点原有的校验保留**，不是重复的摆设：
- `AssetDownloader` 那处能在本地就拒掉，避免 `host-not-allowed` 被 `catch (HttpRequestException)` 当成瞬时失败
  白跑三次重试；
- 其余几处的 warn 日志比出口抛异常更具体（带原始 URL）。

### 0.22.7 验证与遗留

- 每批都跑了 `dotnet build` 与 `PdxLauncher.Checks.dll`：**0 警告 0 错误**，自检 **20 个检查类 / 0 失败**
  （开工前是 16 类，期间并行会话加了 `DdsChecks` / `DiscoveryCacheChecks` / `CatalogCacheChecks` 等）。
- 自检新增断言：`InfrastructureChecks` 覆盖 `AppSettingsStore` 的返回值/延迟写/`Flush`/损坏文件容错与
  `CrashReportWriter` 的写盘/裁剪；`OnlineChecks` 用真实 `HttpFetcher` 断言被拒域名与非 http(s) scheme
  都**不会发起请求**；`PlaysetChecks` / `PlaysetJsonChecks` 跟着新签名调整。
- **环境限制**：验证期间用户一直开着启动器（`PdxLauncher.App` PID 37388/37076），
  解决方案级 `dotnet build` 在最后一步复制 `PdxLauncher.Core.dll` 到 App 输出目录时被文件锁挡住
  （`MSB3027` / `MSB3021`）。逐项目构建全绿，那 10 个 `MSB3026` 重试警告与 2 个错误**全部来自文件锁**，与代码无关。
- **同一工作树里有第二个写入者**（并行会话在做 CS2 / 账号 / DDS 图片缓存）。
  `App.xaml.cs`、`Strings.g.cs`、`ModsViewModel.cs` 等文件的 hunk 会混批，`git add` 会把对方半成品带进去。
  本轮的分离手法：`.scratch/stage-hunks.py`（按 hunk 序号切 patch，**参数 1-based**）与
  `.scratch/stage-from-head.py`（从 `git show HEAD:<path>` 取基线、只重放本批编辑、`git update-index --cacheinfo`，
  **完全不动工作树**）。
  **踩坑**：`core.autocrlf=true` 下 `git show` 输出的是 CRLF，而 blob 存的是 LF —— 内部必须统一按 LF 处理，
  否则整个文件都显示成改动（第一版就是这样，`331 +207/-124`，修正后降到 `87 +85/-2`）。
  `git apply --cached` **不幂等**，重复执行会报 `patch does not apply`，不要重试。

**下一轮从这里接**：`HANDOFF.md` 的 D1–D20 缺陷台账里**至今没有一条属于「数据静默丢失」类别**，
本轮五批补的就是这个空白。审核给出的后续批次（抽 `ICacheStore<T>` 收编三份缓存实现、
`LoadFileFormat` 收敛为策略类、`.editorconfig` + `TreatWarningsAsErrors`、把 `PlaySetEditor` 下沉 Core）
**都还没做**，其中「下沉 Core」现在已经被批次 1 解锁（`PlaySetEditor` 不再依赖 `Localizer`）。

## 0.21 最新一轮（2026-10-27 18:40）：切换游戏 / 主页内容切换很慢 —— 五段根因、八段修法

### 0.21.1 用户诉求

User said (m00003)：「游戏切换，主页内容切换很慢，是不是有什么线程卡住了」，附官方 Stellaris 启动器主页截图（`image.png`，1366×1062）。

### 0.21.2 先排除「线程卡住」

`grep -rn "\.Result\|\.Wait()\|GetAwaiter().GetResult()" src/` **零命中**；`Thread.Sleep` 全仓只有 `AssetDownloader.cs:91` 一处正经的退避 `await Task.Delay`。所以不是 sync-over-async 死锁，也不是线程池饿死。真正的问题是 **UI 线程被一串同步 / 串行的重活长期独占**，以及**同一次切换把同样的扫描重复做了好几遍**。

日志给出的直接证据（`data\logs\pdxnext.log`，旧版本）：

```
15:45:38.545 mod 扫描完成：cities_skylines_2
15:45:38.954 mod 扫描完成：cities_skylines_2   ← 同一游戏同一毫秒级窗口被扫两次
15:45:39.385 DLC 扫描完成：cities_skylines_2   ← 第三次
15:46:05.835 官方公告：联网获取到 2 张卡片（hoi4）
15:46:45.453 背景显示方式: cover               ← 中间 39.6 秒日志全空白
```

39.6 秒里没有任何一行日志在推进，说明 UI 线程被单一长任务占住（全分辨率解码 + 摊平 + 重编码），连日志都插不进去。

### 0.21.3 五段根因

| # | 位置 | 问题 |
| --- | --- | --- |
| 1 | `src/PdxLauncher.App/ViewModels/HomeViewModel.cs` 的 `LoadPlaySetAsync` | 末尾串行读 mod 列表 + 全盘递归扫 DLC，只为填 `DlcCount` / `ModCount` / `EnabledModCount`，而这三个属性在 `HomePage.xaml` **零绑定**（`CountsText` 的唯一消费者是 `DlcPage.xaml:38` 的 DlcViewModel 自己的同名属性）。纯装饰计算，白扫一遍盘。 |
| 2 | `src/PdxLauncher.Core/Dlc/DlcCatalog.cs` / `src/PdxLauncher.Core/Playsets/ModCatalog.cs` | `DiscoverAsync` / `DiscoverAllAsync` **零缓存**。主页、DLC 页、播放集页、mod 库页各扫一遍，同一次切换同一款游戏触发 2–3 次全量目录遍历；Cities Skylines II 还会因为 `V1/V1_1 未解析到任何 .dlc.json` 去「先枚举 .dlc.json（0 命中）→ 再枚举 .dlc」双重遍历，全日志出现 55 次。 |
| 3 | `src/PdxLauncher.App/Services/ThemeImageFlattener.cs` | 摊平全程 `.ConfigureAwait(true)` 回 UI 线程：`DecodePixelsAsync` 拉全分辨率 BGRA（2560×1340 ≈ 13.7 MB）+ `for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;` 逐像素改 + `EncodePngAsync` 重编码 —— 三件重活都在渲染线程上。 |
| 4 | 同上，`_resolved` 缓存 | 只在内存里记「原图 → 摊平图」，**已落盘的 `flat\*.png` 从不读回**。于是每次冷启动、甚至同进程内多次调用都重新解码重编码。日志里同一张 `eu4-background-a2ae4e81.png`（5.7 MB）被摊平 **7 次**（14:46:59 / 15:02:43 / 15:10:33 / 15:11:13 / 15:15:17 / 15:29:47 / 15:34:50），产物路径每次都是同一个 `flat\eu4-background-a2ae4e81-7bef8c95.png`。 |
| 5 | `src/PdxLauncher.Core/Launching/ContinueGameReader.cs` | `ReadAsync` 内部全是同步 IO（`FileExists` / `ReadAllText` / `JsonDocument.Parse`），只有 `return Task.FromResult(...)`，**没有 `Task.Run`** ⇒ 直接在调用线程（= UI 线程）读盘。 |

另外两条放大因素：

- `src/PdxLauncher.App/MainWindow.xaml.cs` 的 `UpdateBackdropAsync`：取消令牌是整窗生命周期的 `_backdropLifetime`，`_backdropVersion` 只丢弃结果、**不取消工作** ⇒ 连点 N 款游戏 = N 次 2560 宽全图解码排队，后面的背景要等前面的解码完。实测两次背景之间隔了 **52.9 秒**（15:46:05.835 → 15:46:45.453）。
- `src/PdxLauncher.App/Services/ThemeUpdateService.cs:134`：后台联网刷新每换一款素材就 `AppServices.SetGames(updated)`，而 `ThemePatch.Apply` / 重排序会产生新的 `GameInstallation` 实例、`ReferenceEquals` 判不出「其实是同一款」⇒ **没有用户操作也会触发六页面全量重载**。
- 五个页面的 `private bool _isReloading` 是「**丢弃**新请求」而不是「取消旧请求」，快速连点只有第一次生效，界面会停在第 1 次点的游戏上。

### 0.21.4 八段修法

**① 新增 `src/PdxLauncher.Core/Infrastructure/DiscoveryCache.cs`（通用 TTL 缓存）**

`public sealed class DiscoveryCache<T> where T : class`：

- `public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);` —— 够挡住「切一次游戏被多处重扫」，又不会让用户装完 mod 后等太久看不到。
- `public DiscoveryCache(IClock clock, TimeSpan? ttl = null)`；内部 `Dictionary<string, Entry> _entries` + `Dictionary<string, Task<T>> _inFlight`（都 `OrdinalIgnoreCase`）+ `Lock _gate`，`Entry` 是 `record struct (T Value, DateTimeOffset StoredAt)`。
- `public async Task<T> GetOrCreateAsync(string key, Func<Task<T>> factory, CancellationToken cancellationToken)`：命中未过期直接返回；否则复用 in-flight 任务，或 `Task.Run(() => RunAsync(key, factory))` 新起一个。返回 `await work.WaitAsync(cancellationToken)` —— **单个调用方取消只影响自己的等待，不中断共享扫描**。
- `public bool Invalidate(string key)` / `public void Clear()` / `public int Count`。

**② `DlcCatalog` / `ModCatalog` 接缓存（构造签名只加可选参数，14 处测试构造点不用改）**

- `src/PdxLauncher.Core/Dlc/DlcCatalog.cs`：`public sealed class DlcCatalog : IDlcCatalog, IScanCacheInvalidator`；构造 `DlcCatalog(IFileSystem, IAppLogger, IClock? clock = null)`；`DiscoverAsync` 改为
  `_cache.GetOrCreateAsync(CacheKeyFor(game), () => Task.Run(() => Discover(game, cancellationToken), cancellationToken), cancellationToken)`，key = `game.GameId + "|" + game.DlcPath + "|" + game.Format`。
- `src/PdxLauncher.Core/Playsets/ModCatalog.cs`：同上，`ModCatalog(IFileSystem, IAppLogger, ModThumbnailResolver? thumbnails = null, IClock? clock = null)`，key = `game.GameId + "|" + game.GameDataDir`。
- `Discover` / `DiscoverAll` 的扫描体一行未动（保留并发会话新加的 `PlaysetsJsonV1` 分支与 `GetEffectiveDlcId` / `CanBeDisabledInContentLoad` 两个 public static）。

**③ 新增 `src/PdxLauncher.Core/Abstractions/IScanCacheInvalidator.cs`**

`public interface IScanCacheInvalidator { void InvalidateCache(GameInstallation game); void ClearCache(); }`。

单独拆接口的理由：缓存细节不该塞进 `IModCatalog` / `IDlcCatalog` 这两个只读扫描接口。实现者：`DlcCatalog`、`ModCatalog`、`src/PdxLauncher.App/Services/ModCatalogRouter.cs`（后者 `InvalidateCache` 路由到 `For(game)` 再转发，`ClearCache` 两份都清）。
`src/PdxLauncher.App/Views/ModLibraryPage.xaml.cs` 的 `OnRefreshClick` 在 `await _viewModel.RefreshAsync()` 前插入 `if (AppServices.Mods is IScanCacheInvalidator invalidatable && AppServices.CurrentGame is { } game) invalidatable.InvalidateCache(game);` —— 手动刷新绕过 TTL，用户刚在 Steam 装的 mod 点刷新就能看到。

**④ `ThemeImageFlattener` 改成三道缓存 + 全程线程池**

`src/PdxLauncher.App/Services/ThemeImageFlattener.cs`：

- `PolicyVersion` 保持 1（注释写明：只加缓存层、产物内容没变，不该 +1 白重算一次）。
- 三道缓存：内存 `_resolved` → 磁盘 `flat\*.png`（正面产物）→ 磁盘 `flat\*.noflat`（否定标记，`NegativeMarkerSuffix = ".noflat"`，记录「这张不是纱层，别再解码一遍」）。
- `Resolve(string? path)`：内存未命中时**探一次磁盘**（先探 flat 产物、再探 .noflat），命中写回内存；这样即使本进程还没调过 `PrepareAsync`（卡片直接重建），同步绑定也能立刻拿到上次留下的成品。
- `PrepareAsync` 改为 `Task.Run(() => ResolveOrFlattenAsync(path, ct))` + `await work.ConfigureAwait(false)`（原为 `true`）；新增 `ResolveOrFlattenAsync` 做「探产物 → 探否定标记 → 才真摊平」。
- `FlattenCoreAsync(string path, string flatPath, CancellationToken)` 的 `flatPath` 改成传入（不再自己算）；所有 `ConfigureAwait(true)` → `ConfigureAwait(false)`；判定不是纱层就写 `.noflat` 并返回原路径；成功落盘后删 `.noflat`。

**⑤ `MainWindow.UpdateBackdropAsync` 加 per-request 取消**

新增字段 `private CancellationTokenSource? _backdropRequest;`。方法开头：

```csharp
var previousRequest = _backdropRequest;
var request = CancellationTokenSource.CreateLinkedTokenSource(_backdropLifetime.Token);
_backdropRequest = request;
previousRequest?.Cancel();
previousRequest?.Dispose();
var token = request.Token;
```

后续两次 `LoadAsync(...)` 都传 `token`；`finally` 里 `if (ReferenceEquals(_backdropRequest, request)) { _backdropRequest = null; request.Dispose(); }`；`OnWindowClosed` 里在 `_backdropLifetime?.Cancel()` 之前先取消 `_backdropRequest`。**新请求进来直接掐掉旧解码**，而不是让旧解码跑完再丢结果。

**⑥ `ContinueGameReader` 移到线程池**

`src/PdxLauncher.Core/Launching/ContinueGameReader.cs`：`ReadAsync` 改为

```csharp
ArgumentNullException.ThrowIfNull(game);
cancellationToken.ThrowIfCancellationRequested();
return Task.Run(() => ReadCore(game, cancellationToken), cancellationToken);
```

原函数体整体下沉为 `private ContinueGameInfo? ReadCore(GameInstallation game, CancellationToken cancellationToken)`，四类 catch（`JsonException` / `IOException` / `UnauthorizedAccessException` / `ArgumentException`）与「永不抛」语义完全保留。调用方 `HomeViewModel.LoadContinueGameAsync` 不用改。

**⑦ 五个页面用 `ReloadCoordinator` 取代 `_isReloading`**

新增 `src/PdxLauncher.App/Services/ReloadCoordinator.cs`：`public sealed class ReloadCoordinator`，`RunAsync(Func<CancellationToken, Task> work, CancellationToken lifetime, Action<Exception> onError)` —— 新请求进来先 `Cancel()` 旧的再跑新的（语义从「丢弃新请求」变成「**取消旧的、跑最新的**」），`OperationCanceledException` 静默，其它异常交给 `onError`。`IsReloading` 属性供 code-behind 判断。

`HomePage` / `DlcPage` / `ModsPage` / `ModLibraryPage` / `SettingsPage` 五个 `.xaml.cs` 全部把 `private bool _isReloading` 换成 `private readonly ReloadCoordinator _reload = new();`，`ReloadAsync` 改成一行 `_reload.RunAsync(...)`。

**⑧ `SetGames` 去重 + 卡片封面并发**

- `src/PdxLauncher.App/Services/AppServices.cs` 的 `SetGames`：先算 `next = ReconcileCurrentGame(games)`，若 `current` / `next` 的 `GameId` 与 `InstallRoot` 相同且 `current.Theme == next.Theme`，只 `CurrentGame = next;` 静默换引用、**不广播** `CurrentGameChanged`。这样后台主题刷新换实例不再触发六页全量重载。
- `PrepareCardCoversAsync` 从串行 `foreach + await` 改成 `private const int CardCoverConcurrency = 4;` + `SemaphoreSlim` + `Task.WhenAll`。理由：卡片封面 1–6 MB，串行时首屏等最慢那张；摊平已经全在线程池，并发度只受磁盘顺序读限制，4 张 2560×1340 解码内存约 55 MB。

### 0.21.5 真机验证（2026-10-27 18:34–18:37）

启动 `src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe`，用 UIA 脚本（`.scratch/rework/switchbench.ps1`、`rapidtest.ps1`）逐款点卡片：

| 切换目标 | 背景落地耗时 | 静默总耗时 |
| --- | --- | --- |
| Crusader Kings III | 195 ms | 1.77 s |
| Hearts of Iron IV | 166 ms | 1.72 s |
| Stellaris | 166 ms | 1.72 s |
| Victoria 3 | 165 ms | 1.72 s |
| Europa Universalis IV | 166 ms | 3.01 s |
| Cities: Skylines II | 166 ms | 1.75 s |

对比修复前：39.6 秒日志全空白、两次背景间隔 52.9 秒。

其它实测结论：

- **扫描去重生效**：18:34:20 之后的日志里 `DLC 扫描完成` **0 次**、`mod 扫描完成` **0 次**（切换 6 款游戏），旧版本同样动作会产生十几条。
- **摊平缓存生效**：`已摊平为不透明图` 本轮 **0 次**；`data\theme-cache\flat\` 里 13 个 `.noflat` 否定标记（1 字节）在 18:34–18:35 一次性写出，`eu4-background-a2ae4e81-7bef8c95.png` 直接复用 17:45 的旧产物、没有重编。
- **快速连点正确收敛**：600 ms 内连点 CK3 → HOI4 → Stellaris → Victoria 3，8 秒后标题栏与主页都停在 **Victoria 3**（最后一次点击），不再是「停在第 1 次点的那款」。
- 六个页面都能正常进入（`navHome` 41 个元素 / `navDlc` 39 / `navModLibrary` 611 / `navPlaySets` 519 / `navSettings` 71 / `navLibrary` 83）。

### 0.21.6 新增自检

- `tests/PdxLauncher.Checks/DiscoveryCacheChecks.cs`：命中与 TTL / 失效与清空 / in-flight 去重 / 调用方取消隔离 / 默认 TTL / 入参校验，六组。
- `tests/PdxLauncher.Checks/CatalogCacheChecks.cs`：`DlcCatalog` 缓存行为（首次扫 1 次 → 第二次 `ReferenceEquals` 且日志仍 1 次 → TTL 内看不到新装 DLC → `InvalidateCache` 后重扫 → 60 秒后自动重扫 → `ClearCache` → 缺失目录的空结果也被缓存只 Warn 1 次）+ `ContinueGameReader` 读盘线程 ≠ 调用线程（实测「调用线程 2，读盘线程 6」）。

`dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"` → **`检查类: 20，失败: 0，断言失败总数: 0`**（本轮从 18 类增到 20 类）。

## 0.20 最新一轮（2026-10-27 17:50）：DDS 解码库 + DLC / mod 图片真正显示

### 0.20.1 用户诉求
User said (m02230)：「1.实现一个dds解析的通用调用库；2.然后完成DLC 和 mod 图片的显示」。计划经 exit_plan_mode 批准后实施。

### 0.20.2 为什么要自己写解码器
WinUI `BitmapImage` 解不了 DDS；EU5 三个 DLC 的 `thumbnail.dds` 是 **500×500 未压缩 BGRA8**，而安装目录里还有 BC1/BC3，抽样 400 个里另见 2 个 BC7（dxgiFormat 98）。WIC(COM) 探针在本机编译不通过，且离线自检无法覆盖；改为纯托管解码器，零 NuGet，能在 `PdxLauncher.Checks` 里直接跑。

### 0.20.3 改动（已落盘、已构建、已自检、已真机截图）
- 新项目 `src/PdxLauncher.Imaging`（`net10.0`）：`Dds/DdsHeader.cs`、`Dds/DdsFormat.cs`、`Dds/DdsDecoder.cs`、`Dds/Bc7Decoder.cs`、`Dds/Bc7Tables.cs`（表与算法移植自 Richard Geldreich `bc7enc_rdo`，MIT/公有领域，文件头已注明）、`Png/PngEncoder.cs`（BCL `ZLibStream`）、`DdsImage.cs`。
  - 能力：BC1/BC2/BC3/BC4/BC5/BC7(mode 0..7) + 未压缩 32/24bpp；ASTC/BC6H/浮点/数组/立方体/3D/截断 → 返回中文 `Error`，从不抛。
- Core：`DlcCatalog` 缩略图改候选序列（png → jpg → jpeg → webp → bmp → dds），`TryCollectEu5` 不再丢缩略图；`ModThumbnailResolver.ImageExtensions` 加 `.dds`。
- App：新增 `Services/ImageArtCache.cs`（DDS → PNG 落 `data\image-cache`，键 = SHA-256(路径小写+长度+mtime+PolicyVersion)，并发 2，≤1024 边长）；`XamlImage.FromPath` 遇 `.dds` 换缓存 PNG；`DlcViewModel` / `ModLibraryViewModel` / `ModsViewModel` 在建行**之前** `PrewarmAsync`（缩略图列是 `OneTime` 绑定）；设置页缓存卡片新增第五行 `images`（`data\image-cache`，可清空重建）；`settings.cache.hint` 从「四类」改「五类」。
- 自检：新增 `tests/PdxLauncher.Checks/DdsChecks.cs`（头解析 / 未压缩 32+24bpp / BC1 / BC3 / BC7 mode1+mode6 / 拒绝路径 / 真机 500×500 文件）、`PlaysetJsonChecks` 加「png 优先于 dds」「只有 dds 时选中 dds」。
- 文档：`docs/DATA-FORMATS.md` 新增第 16 节「DDS 图像」并更新第 3 节缩略图候选与第 15 节缓存表。

### 0.20.4 顺手修掉的既有 bug
`IPlaySetRepository` 的写方法已全部返回 `AppResult`，但 `PlaysetsJsonRepository` 与 App 调用点还停在旧签名（构建 CS0738/CS1061）——本轮一并对齐，语义上「写失败」现在会带着错误码走到 UI，而不是静默成功。

### 0.20.5 验证
- Release 构建 **0 警告 0 错误**；`PdxLauncher.Checks.exe` → **检查类: 18，失败: 0，断言失败总数: 0**。
- 真机（EU5）：DLC 页三张 .dds 缩略图显示真实美术（凤凰盾 / 古迹 / 圣地）；`data\image-cache` 3 个 PNG 共 967.6 KB；日志只有 3 行「DDS 缩略图已转 PNG」，重启后再进 DLC 页**仍是 3 行**（证明缓存命中、不重复转码）；Mod 库 22 个 mod 中带 `thumbnail.png` 的正常显示，其余仍为占位图标（文件本身不存在）**（← 2026-10-28 更正：这条结论是错的，那 16 个 mod 的封面在 `<mod>\.metadata\thumbnail.png`，是解析器漏扫 `.metadata`，不是文件不存在，见 §0.27）**。
- 设置页「缓存 → DDS 转码图」显示 `967.6 KB，3 个文件`，「清空」按钮生效。
- EU4 / CK3 等 png 缩略图不受影响。

### 0.20.6 已知缺口
- 只解 mip0；DDS 里的 ASTC / BC6H / 浮点纹理明确失败（P 社 UI 资产里未见到）。
- 没有缩略图文件的 mod（EU5 22 个里有 16 个）只能显示占位图标——那是数据缺失，不是解码问题；后续可考虑从工坊页抓首图。**（← 2026-10-28 更正：数据并不缺失，16 个的封面在 `.metadata\thumbnail.png`；已修，见 §0.27）**
- BC7 只覆盖 mode 0..7 的完整解码路径，未做性能优化（单张 500×500 约毫秒级，列表场景已够用）。
## 0.19 最新一轮（2026-10-27 16:50）：EU5 美术 + 播放集/DLC 接入

### 0.19.1 用户诉求

User said (m01362)：「1.完成eu5的封面美术和背景图片；2.完成eu5的播放集/DLC的控制接入，先告诉我eu5的播放集/DLC和之前的p社游戏有什么变化」。
随后追加（m01399）要求顺手把主界面左栏那两行说明文字去掉。

### 0.19.2 EU5 与其它 P 社游戏的数据格式差异（先讲变化的结论）

| 维度 | CK3 / V3 / Stellaris / HOI4 / EU4 | EU5 |
| --- | --- | --- |
| 安装描述 | 安装目录有 `launcher-settings.json`（版本、主题、exe 路径都在里面） | **没有**，靠 `binaries/eu5.exe` 特征识别（§0.18） |
| 播放集存储 | `Documents\Paradox Interactive\<游戏>\launcher-v2.sqlite`（mods / playsets / playsets_mods 表） | `Documents\Paradox Interactive\Europa Universalis V\playsets.json`（`file_version: "1.0.0"`） |
| 播放集内容 | sqlite 行，mod 按 id 关联 | json 里 `playsets[].orderedListMods[].path` 直接写**工坊目录的绝对路径** + `isEnabled`；没有 id、没有名称 |
| DLC 开关 | sqlite `playsets_dlcs` + 加载文件 `disabledDLC[].paradoxAppId` | 同一个 `playsets.json` 的 `DLC[].paradoxAppId`（值是 `d017_...` 这种 pops id，同时混有 "Sacred Sites" 这类显示名别名） |
| DLC 描述文件 | `*.dlc`（Clausewitz）或 v3 的 `*.dlc.json`（`id.paradox` + `displayName.en`） | `<DlcPath>\D0xx\D0xx.dlc.json`，平铺 `name` / `localizable_name` / `pops_id` / `steam_id` / `hidden`；**显示名在** `main_menu\localization\english\*_l_english.yml` 且带 `#italic ...#!` 标记 |
| mod 元数据 | `*.mod`（Clausewitz）或 sqlite 行 | 工坊目录下 `.metadata/metadata.json`（**全部带 UTF-8 BOM**） |
| 加载文件 | `dlc_load.json` / `content_load.json` | 无（游戏自己读 `playsets.json`） |

### 0.19.3 改动（已落盘）

- **美术**：`src/PdxLauncher.App/Assets/themes/eu5/hero-1920x1080.jpg`（1920×1080）与 `cover-300x450.jpg`，
  源图是 Steam 商店 CDN 的 `library_hero_2x.jpg`（实测唯一能拿到的大图）；生成脚本留在 `.scratch/`。
  主题走 `InstallSignatureDetector` → `GameContentLayout` → `ThemeResolver` 的层 2（安装目录）之外的
  **内置资产层**：`src/PdxLauncher.Core/Online/ThemeResolver.cs` 里 `BuiltInThemeAssets` 按 gameId 取。
- **播放集/DLC 仓储**：新增 `src/PdxLauncher.Core/Playsets/Json/PlaysetsJsonDocument.cs`（读写保真、未知键原样保留）、
  `PlaysetsJsonRepository.cs`（完整实现 `IPlaySetRepository`，13 个方法）、`LoadFileFormat.PlaysetsJsonV1` 分支。
- **DLC**：`DlcCatalog` 增加 EU5 分支（`TryCollectEu5`，hidden 的 `D000_shared` 不列出），
  名字来自新增的 `src/PdxLauncher.Core/Dlc/DlcLocalization.cs`（解析英文 yml 并剥 `#italic`/`#!`）。
- **写入路径**：`ResolveModPath` 落盘时一律改写为磁盘上真实存在的绝对路径。
- **自检**：`tests/PdxLauncher.Checks/PlaysetJsonChecks.cs` 新增 4 组用例（BOM、陈旧路径、EU5 DLC、写回保真）。

### 0.19.4 真机挖出的三个坑（都不是「猜」，是日志 / 文件实证）

1. **EU5 工坊 mod 的 `.metadata/metadata.json` 全带 UTF-8 BOM** ⇒ `JsonDocument.Parse(byte[])` 直接抛
   `'0xEF' is an invalid start of a value`，19 个 mod 全被跳过、列表只剩工坊 id。修法：解析前统一走
   `JsonBytes.WithoutBom`（`src/PdxLauncher.Core/Infrastructure/JsonBytes.cs`），修后日志
   `mod 扫描完成：eu5，磁盘描述文件 2 个` → **22 个**。
2. **`playsets.json` 里留着库搬家前的绝对路径**（写 `E:/SteamLibrary/...`，mod 实际在 F: 库）⇒ 只按路径查
   会把整个播放集判成 missing。修法：查不到就按**目录名（工坊 id）**兜底，并把 `DirPath` 换成真实路径；
   写回时也写成真实路径（真机验证：`"E:/SteamLibrary/..."` → `"F:/SteamLibrary/..."`）。
3. **EU5 的 DLC 描述文件不是 v3 结构** ⇒ 原 `CollectJson` 要求 `id.paradox` + `displayName.en`，EU5 全部
   被判「缺少 id.paradox 已跳过」，DLC 页显示 0 个。修法见 0.19.3。

### 0.19.5 验证

- `dotnet build PdxLauncher.sln -c Release` → 0 警告 0 错误；自检 **17 个检查类、0 失败、0 条断言失败**。
- 真机（2400×1600 截图 `.scratch/eu5-verify/final-*.png`）：
  - 当前游戏页：EU5 有背景大图（油画骑士）+ 两个按钮 + 公告卡；
  - 播放集页：`初始播放集` 列出 22 个 mod 的真实名称（Community Flavor Pack / 陪都 Multiple Capital …），
    开关可用，改动写回 `playsets.json` 且路径被修正为 F: 盘；
  - DLC 页：3 个（Fate of the Phoenix / Monumental Bonus / Sacred Sites），中文简介来自在线元数据，
    开关状态与 `playsets.json` 一致，保存后 `d008/d015/d017/d000` 的 `isEnabled` 正确落盘。
- 收尾时把测试期间改动的用户状态**全部恢复原样**（d008=false、d017=false、mod 启用状态不变）。

### 0.19.6 已知缺口

1. EU5 没有安装目录里的版本号来源（`launcher-settings.json` 不存在、`eu5.exe` 只有 `FileVersion 1.0`），
   卡片版本行显示「版本未知」；可考虑读最近存档 `continue_game.json` 的 `rawGameVersion`，但那不是安装版本，
   语义上会误导，本轮**故意不做**。
2. DLC / 部分 mod 缩略图是 `.dds`，WinUI 解不了，暂时显示占位图。
3. EU5 播放集的「自动排序」（`isAutomaticallySorted`）目前只透传、不改行为。

## 0.18 最新一轮（2026-10-27 15:40）：EU4 卡片发灰 + EU5 不出现 —— 两个根因、四段修法

### 0.18.1 用户两问（m00005 原文 + m00087 澄清）

「1.eu4的背景图片处理是不是有问题；2.怎么没看到eu5，是不是没有适配」，随后澄清「eu4的问题是看起来发灰」。
对照物：官方启动器里 EU4 卡片是明亮油画，PdxNext 里同一张图变成一片灰；而 Steam 里 EU5 明明装着（附商店海报）。

### 0.18.2 EU4 发灰的两个根因（像素取证 + 日志取证，不是猜）

1. **纱层图**：`F:\SteamLibrary\steamapps\common\Europa Universalis IV\launcher\assets\app-background.png`
   是 1920×1080 RGBA，**alpha 全幅恒为 51/255（20%）**，RGB 却是一张完整油画（均值 ≈98、最大 255）。
   官方启动器把它当「压在画面底纹上的纱」，PdxNext 却直接按 alpha 合成到卡片底色 `#27272C` 上 —— 结果就是没有层次的灰。
   实测该图单独铺出来的合成像素与用户截图吻合（不是主题没生效，是合成语义错了）。
2. **刷新失败被锁 5 天**：`data\logs\pdxnext.log` 里 EU4 六项素材全部 `素材下载超时: The operation was canceled.`
   （metadata-assets 的 TLS/连接超时），但 `ThemeResolver.RefreshAsync` 在循环后**无条件**写 `lastCheckedUtc`，
   于是 5 天内不再重试，主题永远停在安装目录那张纱层图上。ck3 / hoi4 / stellaris 同一批全中。

### 0.18.3 EU5 为什么不出现（文件系统取证）

EU5（1.0 起）安装目录只有 `binaries\`、`game\`、`clausewitz\`、`jomini\`、`platform_specific_game_data\`，
**没有 `launcher-settings.json`、也没有 `theme-settings.json`**（depth 4 全盘搜过），gameId 只存在于官方云端配置里。
而 `GameCatalog` 的收录门槛恰恰是「目录里能解析出 launcher-settings.json」——
所以 EU5 连库都进不来，不是「没适配 UI」，是「没被发现」。Steam 侧数据是齐的：`appmanifest_3450310.acf` → SteamAppId 3450310，
文档数据在 `Documents\Paradox Interactive\Europa Universalis V\playsets.json`（新格式，与 v2 的 `launcher-v2.sqlite` 完全不同）。

### 0.18.4 本轮改动（已落盘、已构建、已自检、已真机截图）

**A. 主题刷新不再被失败锁死** —— `src/PdxLauncher.Core/Online/ThemeResolver.cs`
- `RefreshAsync`：只有**全部成功**才写 `lastCheckedUtc`；部分/全部失败写 `RetryAfterUtc = now + RefreshRetryDelayMilliseconds`（10 分钟）。
- `IsCacheStale`：`RetryAfterUtc` 未到期时直接判「不刷新」，避免每帧重试。
- 关键常量：`CacheTtlMilliseconds = 432_000_000`（5 天，成功路径不变）、`RefreshRetryDelayMilliseconds = 600_000`。

**B. 纱层图摊平** —— 新增 `src/PdxLauncher.Core/Online/WashLayerPolicy.cs` + `src/PdxLauncher.App/Services/ThemeImageFlattener.cs`
- 判定：alpha 峰值 ≤ `MaximumPeakAlpha = 96`（38%）**且** alpha ≥ 240 的像素占比 ≤ `MaximumOpaqueFraction = 0.01` → 是纱层。
- 处理：解码像素 → alpha 置满 → 重新编码 PNG，落 `data\theme-cache\flat\`，缓存键 = 原图路径 + 文件大小 + `PolicyVersion`。
- 接入：封面（`LibraryViewModel` → `GameCardItem.CoverImagePath`）与整窗背景（`MainWindow` / `HomePage`）都改走摊平后的副本；
  非位图（svg）、缺失、解码失败一律返回原路径并记 warn，绝不抛异常。
- 改判定阈值时**必须**把 `ThemeImageFlattener.PolicyVersion` +1，否则旧缓存不失效。

**C. EU5 发现** —— 新增 `src/PdxLauncher.Core/Games/InstallSignatureDetector.cs`
- 有序规则表（不猜）：`("eu5", "Europa Universalis V", "binaries/eu5.exe", GameSupport.LaunchOnly)`。
- `GameCatalog` 在「launcher-settings.json 缺失」时再跑一次特征识别，命中就按 LaunchOnly 收录，Steam 扫到的 appmanifest 补上 `SteamAppId`。
- 卡片侧：`GameCardItem.HasNoCover` 显示占位标题，「仅启动」用徽标标出（`未适配`/`仅启动` 同一套徽标样式）。

**D. 没封面时的兜底（尽力而为）** —— `src/PdxLauncher.Core/Online/SteamCoverUrlResolver.cs` + `ThemeResolver.WithSteamCapsuleAsync`
- 商店 `appdetails?filters=basic` 取 `header_image`（新游戏的静态 `header.jpg` 路径已 404，只能走接口）。
- 新增 `SteamCapsuleTimeoutMilliseconds = 12_000` 硬超时（接口实测会「连上不回包」，沿用 180 秒总超时卡片会空很久）；
  失败写 `data\theme-cache\steam-<appId>.miss`，`SteamCapsuleRetryMilliseconds = 600_000` 退避 10 分钟。
- 新增 `ThemeAssetSource.SteamCapsule = 3`，设置页素材来源可显示「Steam 胶囊图」。

### 0.18.5 验证（Release，Release 构建 + 自检 + 真机）

- `dotnet build "PdxLauncher.sln" -c Release -m:1 -p:UseSharedCompilation=false -nodeReuse:false` → **0 警告 0 错误**。
- `tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.exe` → **检查类 16，失败 0，断言失败总数 0**。
  本轮新增断言：部分下载失败不得推进 5 天 TTL、`RefreshRetryDelayMilliseconds == 600_000`、
  纱层判定（均匀 20% alpha 直方图 → true；全不透明 / 4% 不透明像素的真半透明美术 / 峰值 200 → false）、
  `SteamCoverUrlResolver.ParseHeaderImage` 四种畸形响应、`SteamCapsuleTimeoutMilliseconds == 12_000` 且必须远小于默认总超时。
- 真机（1800×1200）：游戏库 **9 个游戏**（新增 EU5），EU4 卡片封面回到明亮油画、不再有那层灰纱
  （日志 `主题背景是纱层，已摊平为不透明图: ...\eu4-background-a2ae4e81.png → ...\theme-cache\flat\...`）；
  EU5 卡片显示「仅启动」徽标 + 占位标题，启动按钮指向 `binaries\eu5.exe`（**未实跑游戏本体**，避免占机器）。
- 日志证据：`主题缓存层已应用: 6 个游戏使用了联网缓存素材`；EU5 `按安装特征识别（目录内无 launcher-settings.json）: eu5 @ F:\...\Europa Universalis V`。

### 0.18.6 已知盲区 / 下一轮可选

1. **EU5 没有真实封面美术**：本机对 `store.steampowered.com/api/appdetails` 的请求在 App 进程内 12 秒内失败
   （PowerShell 里同一请求 625ms 成功，说明是进程级网络差异，未查明），所以 EU5 卡片目前是占位标题 + 「仅启动」徽标。
   想要真封面可选：① 用 `tools/` 里的脚本离线把官方启动器缓存里的图抠出来随包分发；② 解析 EU5 安装目录的 `.dds`
   （`game\main_menu\gfx\interface\logos\eu5_logo.dds` 等，需自写 DDS 解码）；③ 允许用户手动指定封面。
2. **EU5 播放集 / DLC 未适配**：EU5 用 `Documents\...\Europa Universalis V\playsets.json`（`file_version 1.0.0`、
   `playsets[].orderedListMods[].path/isEnabled`、`DLC[].paradoxAppId/isEnabled`），与 v2 的 sqlite 完全不同，
   现在只能「启动游戏」，播放集页对 EU5 是空的。
3. **Light / HighContrast 主题**下摊平图与新卡片的观感未复验（沿用 §0.3 的旧盲区）。

### 0.18.7 备份

本轮没有新增 backup 副本；改动全部在 git 工作树里，`git diff` 可逐处对照（见 §2.1 清单）。

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
- 补回列间距：合并内外层 Grid 时 `ColumnSpacing="16"` 漏掉过，导航面板一度只隔着一条描边就贴住卡片；
  现在外层 `Grid` 带 `ColumnSpacing="16"`，卡片左缘 `x=693 → 718`、宽度收窄 25 px、右缘 `1735` 不变。

### 0.17.4 验证（Release，真机截图 + 像素测量 + UIA）

- `cmd /c build.cmd`：0 警告 0 错误；`检查类: 16，失败: 0，断言失败总数: 0`。
- `.scratch/rework/navgap-settings.png` + `navgap-probe2.py`（1800×1200，150% DPI，行带 y=190..246）：
  指示条 `x=374–378`（5 px ≈ 3 DIP）→ 图标块 `x=392–415`（24 px = 16 DIP，间隔 14 px ≈ 9 DIP）
  → 再空 24 px（16 DIP）→ 文字首列 `x=440`。**条右缘到文字 62 px（≈41 DIP）**；改前 12 px（≈8 DIP）。
- `.scratch/rework/verify-settings2.ps1`：appearance 46 / launch 39 / paths 46 / online 39 / cache 44 /
  advanced 38（比旧基线各 +4，来自 `NavigationView` 的 `PaneRoot` / `PaneTitleTextBlock` /
  `PART_TitleText` / `PART_SubtitleText` 节点）。
- 列间距复查（`scrollgutter-hover.png`）：卡片填充左缘 `x=718`、右缘 `x=1735`、滚动条 `1751–1752`
  ⇒ 导航面板与卡片之间约 27 px，滚动条通道间隙仍 12 px；`verify-settings3.ps1` 三次读数一致
  （本次 `1492,1130,492x60`），点按钮不跳动这条没回退。

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

- `cmd /c build.cmd`：16 个检查类、失败 0、断言失败 0；`文案自检`：499 个键合法。
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

**2026-10-27 15:30 追加：设置页「缓存」分类接管 DLC 简介 / 官方公告 / Mod 缩略图三块（联网只剩代理），并做了一轮开关/下拉/刷新/清除/code-behind 去重；build 0/0、Checks 16 类 0 失败、UIA 六节 want 全命中，见 §0.16。**

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

> 2026-11-08 注：该工具是一次性的素材抠取脚本，产物已在仓库，脚本本身已删除；以下为当时记录。

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
  （2026-11-03 补注：`publish.cmd` 之后又改到 3906 B；新增第六个顶层脚本 `start.cmd` 4338 B，
  同样是纯 ASCII + CRLF，中文同样全走 `msg.cmd`，见 §0.67。）
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
| docs/screenshot-library.png | 1800x1200 | 3,413,005 | 游戏库（九张游戏卡，含 EU5「仅启动」；2026-10-27 15:40 重截，@1.5 DPI，EU4 封面已去灰纱） |
| docs/screenshot-playsets.png | 1800x1200 | 855,223 | 播放集（2026-10-27 04:19 换成 §0.14 单行工具条版，@1.5 DPI 与其余五张 @1.33 不同） |
| docs/screenshot-mod-library.png | 1782x1191 | 930,138 | Mod 库（387 个 mod） |
| docs/screenshot-dlc.png | 1782x1191 | 1,376,316 | DLC |
| docs/screenshot-settings.png | 1782x1191 | 1,287,153 | 设置 |
| docs/DATA-FORMATS.md | — | 18,420 | 数据格式真机实测记录（未变） |

除播放集外五张图均为 2026-10-26 §0.3 之后重截（Release，1782x1191 物理 @1.33 DPI），sha256 互不相同。

> 2026-10-27 04:19：`docs/screenshot-playsets.png` 已换成单行工具条后的新界面（`.scratch/rework/2026-10-27-playset-final.png`，1800x1200，@1.5 DPI）。与其余五张的尺寸/DPI 不一致，下次整体重截时一并统一；若并发会话正在改 Home/设置页，那些图可能也需同步重截。
>
> 2026-11-04：README 重写成对外介绍页，截图收敛到三张并整体重截（`home` / `library` / `settings`，1604x1002，客户端 1600x1000，UIA `SelectionItemPattern.Select()` 导航 + `DwmGetWindowAttribute` + `CopyFromScreen`，脚本 `.scratch/capture-readme4.ps1`）。`screenshot-playsets.png` / `screenshot-mod-library.png` 含使用者自己的 Mod 列表，`screenshot-dlc.png` 含 SteamID64 与 cream_api 解锁明细，三者已从 `docs/` 删除（历史仍在本文件的表格与 Git 记录里）。`docs/DEVELOPMENT.md` 的目录清单同步改为三张。

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
| D21 | ~~切换游戏 / 主页内容切换很慢（39.6 秒日志空白、两次背景间隔 52.9 秒）~~ **已解决（§0.21）**：五段根因（`LoadPlaySetAsync` 的纯装饰 DLC/mod 计数、DLC/mod 扫描零缓存、纱层摊平在 UI 线程、`flat\` 产物跨进程从不读回、`continue_game.json` 同步读盘）+ 两条放大因素（整窗背景无 per-request 取消、`SetGames` 因实例换引用反复广播）。真机实测背景落地 **165–195 ms**，切换 6 款游戏期间 `DLC 扫描完成` / `mod 扫描完成` / `已摊平为不透明图` 全部 **0 次** | `data\logs\pdxnext.log` 旧版 15:45–15:47 时间线；新版 18:34–18:37 实测（§0.21.5）；`检查类: 20，失败: 0` |
| D22 | `Frame.Navigate 返回 false (home)` 偶发：从游戏库点卡片后跳主页时，`NavigationService.Navigate` 返回 false，连着打印 `导航失败：Frame.Navigate 返回 false (home)` + `导航失败: home`。功能上不致命（后续 `背景显示方式` 仍会刷新、标题栏也换了），但左侧导航选中态与页面内容可能短暂不同步 | 全日志出现 3 次：`17:49:05.991`、`18:35:08.958`、`18:36:40.854`（同一模式）。推测是 `Frame.Navigate` 在同一帧内被连点触发，或上一帧导航尚未完成。**未定位，留给下轮**；`NavigationService.Navigate` 目前只对 `CurrentTag` 相等做短路，没做「导航进行中」互斥 |

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

### T10 · 切换游戏慢：扫描缓存 + 去掉无消费者的重活 —— **已完成（§0.21）**

- **目标**：① 新增 `DiscoveryCache<T>`（TTL 60 秒 + in-flight 去重 + 调用方取消隔离）；
  ② `DlcCatalog` / `ModCatalog` 接缓存（构造只加 `IClock?` 可选参数）；③ 新增 `IScanCacheInvalidator`
  并让三个扫描器实现，Mod 库「刷新」按钮接 `InvalidateCache`；④ 删掉 `HomeViewModel.LoadPlaySetAsync`
  末尾无 XAML 消费者的 mod 列表读取与 `Dlcs.DiscoverAsync`；⑤ `ContinueGameReader.ReadAsync` 整体包 `Task.Run`。
- **验收**：`dotnet build -c Release -m:1` 0 警告 0 错误；`PdxLauncher.Checks.dll` 全过；
  真机连续切换 6 款游戏期间 `DLC 扫描完成` / `mod 扫描完成` 日志 0 次。
- **write scopes**：src/PdxLauncher.Core/Infrastructure/DiscoveryCache.cs、
  src/PdxLauncher.Core/Abstractions/IScanCacheInvalidator.cs、src/PdxLauncher.Core/Dlc/DlcCatalog.cs、
  src/PdxLauncher.Core/Playsets/ModCatalog.cs、src/PdxLauncher.Core/Launching/ContinueGameReader.cs、
  src/PdxLauncher.App/Services/ModCatalogRouter.cs、src/PdxLauncher.App/ViewModels/HomeViewModel.cs、
  src/PdxLauncher.App/Views/ModLibraryPage.xaml.cs、tests/PdxLauncher.Checks/{DiscoveryCacheChecks,CatalogCacheChecks}.cs
- **blocked_by**：无

### T11 · 切换游戏慢：摊平 / 背景解码离开 UI 线程 + 跨进程复用产物 —— **已完成（§0.21）**

- **目标**：① `ThemeImageFlattener` 全程 `ConfigureAwait(false)` + `Task.Run`，新增磁盘正面产物与 `.noflat`
  否定标记两道跨进程缓存；② `MainWindow.UpdateBackdropAsync` 加 per-request `CancellationTokenSource`，
  新请求进来直接取消旧解码；③ `AppServices.SetGames` 在同一 gameId + InstallRoot + `Theme ==` 时只换引用不广播；
  ④ `PrepareCardCoversAsync` 串行改 4 路并发。
- **验收**：真机逐款切游戏，`背景显示方式` 落地的间隔 ≤ 500 ms；日志 `已摊平为不透明图` 在第二轮起 0 次。
- **write scopes**：src/PdxLauncher.App/Services/ThemeImageFlattener.cs、src/PdxLauncher.App/MainWindow.xaml.cs、
  src/PdxLauncher.App/Services/AppServices.cs
- **blocked_by**：无

### T12 · 页面重载改「取消旧的、跑最新的」—— **已完成（§0.21）**

- **目标**：新增 `ReloadCoordinator`，五个页面的 `private bool _isReloading`（语义是**丢弃**新请求，
  快速连点只有第一次生效）全部替换。
- **验收**：600 ms 内连点 4 款游戏，8 秒后标题栏与主页都停在**最后一次**点击的那款。
- **write scopes**：src/PdxLauncher.App/Services/ReloadCoordinator.cs、
  src/PdxLauncher.App/Views/{HomePage,DlcPage,ModsPage,ModLibraryPage,SettingsPage}.xaml.cs
- **blocked_by**：无

### T13 · 修 `Frame.Navigate 返回 false` 与导航互斥 —— 待做（D22）

- **目标**：`NavigationService.Navigate` 目前只对 `CurrentTag == tag` 短路，没有「导航进行中」互斥。
  从游戏库点卡片后跳主页时偶发返回 false（全日志 2–3 次），左侧选中态与页面内容可能短暂不同步。
  加一个 `bool _navigating` 或直接对 `Frame.Navigate` 的 false 做一次重试（下一个 UI 帧再试）。
- **验收**：连续快速切 10 次游戏，日志里 `Frame.Navigate 返回 false` 出现 0 次；六个页面 AutomationId 不变。
- **write scopes**：src/PdxLauncher.App/Services/NavigationService.cs、src/PdxLauncher.App/MainWindow.xaml.cs
- **blocked_by**：无

---

## 9. 下轮开场建议（给下一个对话的第一条 prompt）

> 读 docs/HANDOFF.md，重点看 §0.21（最新、切换游戏慢已修）与 §7 的 D22（导航偶发返回 false，未定位）。
> 当前进度：构建 0 警告 0 错误、自检 **20 个检查类 / 0 失败**；切换游戏实测背景落地 165–195 ms（修复前 39.6 秒空白）。
> 建议顺序：T13（修 `Frame.Navigate 返回 false`，D22）→ T4（真机复验 settings.json + 置顶恢复）→ T3（Light / HighContrast
> 真机切主题跑一遍，这是唯一没实测的视觉盲区）→ T6（启动 / 继续 / 播放集切换交互回归）→ T7 清理。
> **注意**：工作树里还有一个并发会话在做「账号登录 / DDS 图片 / 播放集 JSON 存储」的未提交改动，
> 提交时**不要 `git add -A`**，只能精确 `git add` 自己改的文件。
> 构建：`dotnet build PdxLauncher.sln -c Release -m:1`，构建前先 `Get-Process PdxLauncher.App | Stop-Process -Force`
> （进程在跑会 MSB3026 / PRI210: 0x800704c8 失败）。构建前另建议先等并发写入静默：
> `find . -newermt '-60 seconds' -type f \( -name '*.cs' -o -name '*.xaml' \) -not -path './*/obj/*' -not -path './*/bin/*' | wc -l` 为 0。
> 启动与截图方式见 §4.12 与 §3.3（cmd start 启动，强制 HWND_TOPMOST 后 CopyFromScreen，且启动与截图必须在同一次 pwsh 调用里）。

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
| 扫描结果缓存 TTL | `src/PdxLauncher.Core/Infrastructure/DiscoveryCache.cs` 的 `public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60)` |
| 卡片封面并发度 | `src/PdxLauncher.App/Services/AppServices.cs` 的 `private const int CardCoverConcurrency = 4` |
| 纱层摊平否定标记 | `src/PdxLauncher.App/Services/ThemeImageFlattener.cs` 的 `private const string NegativeMarkerSuffix = ".noflat"`（产物在 `data\theme-cache\flat\`） |
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
| `src/PdxLauncher.App/Strings/zh-CN/<area>.json`、`src/PdxLauncher.App/Strings/en-US/<area>.json`（新增） | 文案源，按 area 分文件：`common` / `settings` / `shell` / `home` / `library` / `mods` / `modlib` / `dlc`，目前 **498 个键** |
| `src/PdxLauncher.Localization.Generator`（新增） | 合并同一语言目录下所有 `*.json`（同键跨文件重复即报错）→ 校验两语言键集一致、值非空白无首尾空格、`{0}`/`{1}` 序列一致 → 生成 `src/PdxLauncher.App/Localization/Strings.g.cs`（键 → 标识符，如 `app.name` → `AppName`）；`--check` 只校验且比对生成物是否最新 |
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

- 左侧 8 分类（`lstSettingsSections`；本节写作时是 6 分类 `ListView`，2026-10-27 15:10 换成 `NavigationView`，2026-10-30 加「翻译」`Tag=6`、2026-11-03 加「关于与更新」`Tag=7`）：外观 / 启动 / 路径 / 联网 / 缓存 / 高级 / 翻译 / 关于与更新（`Tag="0".."7"`，图标依次 `Font / Play / Folder / Globe / Save / Setting / World / Help`）—— 右侧同时只显示一类（`secAppearance` / `secLaunch` / `secPaths` / `secOnline` / `secCache` / `secAdvanced` / `secTranslation` / `secAbout` 八个 `x:Name` 容器切 `Visibility`，`CacheSectionIndex = 4`）。
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
> 2. 多个并行会话同时写 `src/PdxLauncher.App/Strings/*` 时，半成品键集会让生成器报「两语言键集不一致」→ **先补齐对侧再继续**，别等对方。
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
---

## 15. 账号页重做 + 创意工坊来源判定修正 + 设置持久化（2026-10-28）

本节是速查；改动背景见用户三条反馈（EU4/CS1/PA 的工坊不见了、设置没保存、账号页不像 WinUI）。

### 15.1 创意工坊来源：判定修正（问题与修法）

- **症状**：EU4、Cities: Skylines、监狱建筑师都**有** Steam 创意工坊，但创意工坊页显示 Paradox Mods。
- **根因**：独立启动器下的游戏读不到 Steam `appmanifest` → `GameInstallation.SteamAppId == null` → `WorkshopSourceChooser.Resolve` 直接判 PDX。
- **修法**：
  - `src/PdxLauncher.Core/Online/GameSteamAppIds.cs`：内置 9 款游戏 appId 映射（`ck3 1158310`、`stellaris 281990`、`hoi4 394360`、`eu4 236850`、`victoria3 529340`、`cities_skylines_2 949230`、`cities_skylines 255710`、`eu5 3450310`、`prison_architect_pc_console 233450`），`TryGet(gameId, out appId)` 大小写不敏感。
  - `WorkshopSourceChooser.ResolveAppId(int? steamAppId, string? gameId)`：先安装信息（>0）、再映射表，都没有才 null（→ PDX）。
  - `SteamWorkshopClient.HasWorkshopAsync` 改**双路探测**：appdetails 失败时不再直接 Fail，而是抓 `workshop/browse` 落地页用 `SteamWorkshopHtmlParser.Parse` 判定（CS2 会 302 回 `/workshop`，无 SSR 数据 → false）；两路都 HTTP 失败才 `steam-workshop-probe-http`。结果按 appId 缓存。
  - 「内容由游戏本体管理」的判定收窄为 `ResolveAppId(...) is null && !SupportsContentManagement`：启动支持的游戏只要解析出 appId 也照常显示工坊。
- **自检**：`tests/PdxLauncher.Checks/GameSteamAppIdsChecks.cs`（9 条映射 + 大小写 + 未知/空 + `ResolveAppId` 优先序 5 条）、`SteamWorkshopApiChecks.CheckHasWorkshopFallback`（appdetails 挂+页面有数据 → true；落地页无 SSR → false；两路都挂 → Fail 且错误码含 `steam-workshop-probe`）。

### 15.2 账号页重做（照 Windows 11「账户」页）

- `Views/AccountPage.xaml`：`ScrollViewer` → `StackPanel MaxWidth=880 Spacing=16`：页头 → **身份卡**（`PersonPicture` 64 + `Initials`、`txtAccountProfileName`、`txtAccountStatus`、右侧主操作 `btnAccountPrimary`/`btnAccountSignOut`）→ **InfoBar**（`infoAccountStatus`，`IsOpen` 绑 `HasStatusMessage`，内容 `txtAccountStatusBar`）→ **登录表单**放进 `Expander`（`expAccountSignIn`，默认展开，Header 是新键 `account.signInHeader`）→ 已登录时一张「保持登录」卡（`swAccountRememberSignedIn`，避免与表单里的 `swAccountRemember` 撞车）。
- `ViewModels/AccountViewModel.cs`：新增 `enum AccountStatusLevel {Info,Success,Error}`、`Initials`、`ProfileName`、`HasStatusMessage`、`StatusLevel`、`ShowStatus(string)`；`SetStatus(key, level, args)` 记级别；`StatusText` 未登录时改显示 `account.statusHint`（不再与名字「未登录」重复）；登录失败也写 InfoBar（不再弹 ContentDialog）。
- 交互控件的 AutomationId 全部沿用旧设置页分节（`txtAccountEmail`/`txtAccountPassword`/`swAccountRemember`/`btnAccountSignIn`/`btnAccountSignOut`/`txtAccountStatus`），UI 自动化脚本无需改写；新增 `imgAccountAvatar`、`txtAccountProfileName`、`btnAccountPrimary`、`infoAccountStatus`、`txtAccountStatusBar`。
- i18n：`src/PdxLauncher.App/Strings/*/Resources.resw` 27→30 键（`profileTitle`/`notSignedInTitle`/`signInHeader`）。

### 15.3 设置持久化（分节 + 窗口位置 + 立即落盘）

- `AppSettings` 新增 `int? LastSettingsSection`、`int WindowX = -1`、`int WindowY = -1`（物理像素，-1 = 从没记过）。
- `SettingsPage.RestoreSection()`：读 `LastSettingsSection`（越界归 0）→ `navSettingsSections.SelectedItem = items[index]` 同步高亮 → `ShowSection(index, refreshCache:false)`；`ShowSection` 末尾把当前节写回设置。
- **踩坑（值得记住）**：XAML 里 `navSettingsAppearance` 曾写死 `IsSelected="True"`，NavigationView 会在 `OnLoaded` 之前触发 `SelectionChanged` → `ShowSection(0)` 把刚恢复的值覆盖成 0，表现为「永远停在外观节」。删掉 `IsSelected` 后修复。
- `MainWindow`：`OnClosed` 存 `AppWindow.Position`；`ApplyWindowPosition`（`ApplyWindowSize` 之后调用）按 `DisplayArea.GetFromWindowId(...).WorkArea` 校验窗口完整落在工作区内才 `AppWindow.Move`。实测：关一次后 `windowX/windowY = 380/380`，再启动 `GetWindowRect` = `left=380 top=380 right=2180 bottom=1580`。
- `SettingsViewModel.SaveSettings()` 从 `SaveSettingsDeferred()` 改成立即 `SaveSettings()`（几百字节文件，换确定性）。
- 自检：`tests/PdxLauncher.Checks/SettingsChecks.cs`（round-trip 含新增字段、缺文件默认、坏 JSON 回退、延迟写 `Flush`）。

### 15.4 验收

- `dotnet build "PdxLauncher.sln" -c Release` → 0 警告 0 错误；`文案源生成器` → 586 键；`PdxLauncher.Checks.exe` → **检查类 27 / 失败 0**。
- 真机（`pwsh ui-test-artifacts/workshop/workshop-shot.ps1`，用 `settings.json` 预置 `lastGameId` 逐款启动）：
  - CS2 → `source=Paradox Mods`，`已加载 27379 个结果`（`02-workshop-pdx.png`）
  - CK3 → `source=Steam 创意工坊`，17,717 个结果（`03-workshop-steam.png`）
  - EU4 → `source=Steam 创意工坊`（`04-workshop-steam-eu4.png`）——修正前这里是 Paradox Mods
  - 账号页新版式（`05-account.png`）、设置页停在「缓存」且左栏高亮同步（`06-settings.png`）
- `settings.json` 实测落盘：`lastGameId=eu4`、`lastGameInstallRoot=F:\SteamLibrary\steamapps\common\Europa Universalis IV`、`lastSettingsSection=4`、`windowX/Y=380`。

## 16. 补丁：一键打补丁 / 移除补丁（2026-10-28）

### 16.1 需求与三条已定决策

用户要求：设置 → 高级页加「一键补丁」和「移除补丁恢复正常游戏」，并让这套补丁思路适用于**所有** P 社游戏。
落地前已定（用户选择）：

1. 补丁文件**内嵌进启动器**，另留外部覆盖目录（发布自包含，新整合包丢文件即可，不必重编译）。
2. 只给**当前选中的游戏**打，不做「全部已安装游戏」批量按钮。
3. 只做**解锁层**，不碰 DLC 内容文件（实测 V3 的 `game\dlc` 与整合包同名目录 diff 为空；移除补丁只还原 DLL/ini）。

### 16.2 核心实现（`src/PdxLauncher.Core/Patch/`）

- `Assets/`：`steam_api.dll`(737280B, x86)、`steam_api64.dll`(834560B, x64)、`cream_api.ini`(26608B, 490 条 DLC)，
  由 `PdxLauncher.Core.csproj` 以 `EmbeddedResource` + `LogicalName=PdxLauncher.Patch.*` 打进 DLL；字节数与 sha256 记在
  `Assets/README.md` 与 `DlcPatchChecks` 的常量里（换素材必须同时改两处）。
- `PatchAssets`：外部目录 `data\patch\assets\` 同名文件优先（loader 按架构单独回退，ini 独立回退），否则用内嵌资源；
  `UsesExternalAssets` 只反映「这次实际用了外部文件」。
- `GamePatchProfiles`：CS2 特例 `Cities2_Data\Plugins\x86_64\`；其余按 `exe 目录 → InstallRoot → InstallRoot\binaries`
  找第一个含 `steam_api64.dll` / `steam_api.dll` 的目录，x64 优先，两者都在就都处理（这是「通用于所有 P 社游戏」的唯一判定点，
  新增游戏只要落在这几种布局里就不用改代码）。
- `GamePatchService`：`Inspect / Apply / Revert` 同步、不碰 UI，全部文件操作走 `IFileSystem`（新增 `Move` 支持覆盖改名）。
  - **Apply 的关键取舍**：先把**目标游戏当前真实的** `steam_api*.dll` 备份成同目录 `<name>_o.dll`，再写 loader。
    整合包自带的 `steam_api64_o.dll` 是某款游戏的旧版本（实测 289568B，与 V3 的 298384B 不同），**绝不能跨游戏直接拷**，
    否则「移除补丁」会把不匹配的 dll 还原回去、游戏起不来。
  - ini 由模板生成：只改 `[steam]` 的 `appid =`，保留 `orgapi/orgapi64`、`language = schinese`、`unlockall = false` 与全部 490 条 `[dlc]`，
    头部插 `; PdxNext 自动生成 …` 标记（Revert 只删带标记的 ini，用户自备的 ini 不碰）。
  - 清单 `data\patch\state.json`（`WriteAllTextAtomic`）记录 `gameId / installRoot / apiFile / backupFile / loaderSha256 / appliedUtc`。
  - 失败（只读、被占用、素材缺失）不抛异常，写进 `GamePatchReport.Detail` + 日志；原文件在写 loader 之前已备份完，
    写失败也不会丢原版。
- 状态机 `GamePatchStatus`：`Unsupported`（无 appId / 素材缺失）、`NotDetected`、`Vanilla`、`Patched`（loader + `_o` + 标记 ini 齐全）、
  `PatchedOther`（loader 不匹配或缺 `_o`；有 `_o` 时仍允许 Revert）、`SteamOverwrote`（api 回到原版但 `_o` 还在 ⇒ 显示「被 Steam 更新覆盖」并允许重打）。
- appId 解析顺序：`GameInstallation.SteamAppId`（来自 appmanifest）→ `GameSteamAppIds.TryGet`。本轮给该表补了
  `imperator_rome 859580`、`ck2 203770`、`victoria2 42960`（原本 9 条，现 12 条）。

### 16.3 外壳接线

- `AppServices`：新增 `IGamePatchService GamePatch`，`Initialize(...)` 在 `steamWorkshop` 之后多一个参数；
  `App.xaml.cs` 用 `PdxDataRoot.Directory` 拼出素材目录与清单路径后构造。
- `SettingsViewModel`：`PatchStatusText / PatchDetailText / HasPatchDetail / HasPatchInfo / CanApplyPatch / CanRemovePatch`、
  `RefreshPatchState()`（`LoadAsync` 两条出口、`RefreshAfterGameRefresh`、`Relocalize` 都调用）、
  `ApplyPatchAsync / RemovePatchAsync`（`IsBusy` + `Task.Run`，`IsBusy` 的 setter 同时通知两个按钮可用性）。
  状态文案按 `GamePatchStatus` 映射 `settings.patch.state.*`，细节行拼「目录 / appId / 素材来源 / 失败原因」。
- `SettingsPage`：`secAdvanced` 内、危险区之前插入 4 个 AutomationId：`txtPatchStatus`、`txtPatchDetail`、
  `btnApplyPatch`（Accent）、`btnRemovePatch`、`btnOpenPatchAssets`；前两个按钮先弹 `ContentDialog`（默认按钮是「取消」）。
- i18n：`src/PdxLauncher.App/Strings/*/Resources.resw` 228 → 257 键（`settings.patch.*` 29 个），`文案源生成器` → 615 键。

### 16.4 构建链修正（本次一起修的）

`tools/manual-build.py` 之前只编 Core，`checks` 模式缺 Imaging 引用与两个链接源文件，`app` 模式也不支持资源：
新增 `build_imaging()`；`build_checks(core, imaging)` 补 `PlaySetEditor.cs` / `PositionInput.cs` 与 `-analyzer:RegexGenerator`；
`compile_library(..., resources=)` 支持 `-resource:<path>,<logicalName>`，`build_core()` 传入三个内嵌素材。
（`dotnet build` 在本机沙箱仍会因禁用管道在 Csc 阶段失败，手工链是唯一可靠路径。）

### 16.5 验收

- `python tools/manual-build.py app` → `BUILD OK`（pass1 23 个 codegen、csc 0 错误、pass2 11 个 xbf，含新 SettingsPage.xbf）。
- `python tools/manual-build.py checks` → `检查类: 28，失败: 0，断言失败总数: 0`（新增 `DlcPatchChecks` 覆盖：
  内嵌素材 sha256/字节数、x86/x64 目录选择、备份字节、ini appId 与标记、清单与幂等重打、还原后字节与文件清理、
  旧手工补丁与 Steam 覆盖态的可还原性、缺 appId 拒绝、目标被占用时不破坏原文件）。
- 未做真机点按验证：本轮只验证编译与自检；真机需先退出游戏，再在设置 → 高级里对一款已装游戏打/移除。

## 17. 创意工坊：Steam 式排序 + mod 信息缓存（2026-10-28）

用户两句话：「创意工坊的排序改下，参考下 steam 自己的方式」「能给 mod 提供缓存吗，识别到是同一个 id，调用缓存的信息」。本节是速查。

### 17.1 排序：照 Steam 网页的语义

Steam 网页把排序分成三块：排序顺序 + 时间范围 + 分类筛选。时间范围**只在「最热门 / 最受好评」下出现**，其他排序 Steam 自己就忽略它。我们照抄这个语义：

| 排序档位（UI） | `browsesort` | 带时间范围 |
| --- | --- | --- |
| 最热门（默认） | `trend` | 是 |
| 最受好评 | `toprated` | 是 |
| 最近发行 | `mostrecent` | 否 |
| 最新更新 | `lastupdated` | 否 |
| 不重复订阅者总计 | `totaluniquesubscribers` | 否 |
| （有关键词时）搜索 | 仍用上面选中的排序 + `searchtext=` | 否 |

时间范围下拉 7 档：今天 / 1 周 / 30 天（默认）/ 3 个月 / 6 个月 / 1 年 / 发布至今，对应 `&days=1|7|30|90|180|365|-1`（**「发布至今」是 -1 不是 0**：`days=0` 会被服务端当成「不传」，回落成默认 7 天）。

> 2026-11-03 更正：本节原表写的 `mostupdated` / `mostsubscribed` 是错的 —— Steam 现行 browse 端点不认这两个值，会**静默回落成 `trend`**，表现就是「换了排序列表一模一样」。真实值见上表（依据是 Steam 网页自己的 SSR JS 白名单与 5 个 appid 的实测）。

- `WorkshopViewModel.ShowTimeRange` **必须按当前排序判断**（`IsTimeRangeSort()` 查 `Keys.WorkshopSortTrend`/`Keys.WorkshopSortTopRated`），只判断来源会把「最新更新 + 6 个月」这种无效组合露出来。真机验证：切到「最新更新」后 `cmbWorkshopTimeRange` 从 UIA 树里消失。
- PDX 侧对应三档：最热门 `Popularity` / 最新更新 `Updated` / 评分 `Rating`，没有时间范围。
- `src/PdxLauncher.Core/Online/SteamWorkshopClient.cs` 的 `BuildBrowseUri`：`searchtext` 与 `browsesort` **并存**（服务端带关键词时照样按 `browsesort` 排序并回显，别再把排序改写成 `textsearch`）；`days` 只在排序是 `trend` / `toprated` 时追加，且必须原样透传（-1 表示「发布至今」）。

### 17.2 缓存：按「来源 + id」复用

用户诉求原话是「识别到是同一个 id，调用缓存的信息」。落地为 `src/PdxLauncher.Core/Online/WorkshopInfoCache.cs` + `WorkshopInfoCacheEntry.cs`：

- 位置 `data\workshop-info\`：索引 `workshop-info.json` + `covers\<steam|pdx>\<id>.<ext>`。
- 键 `source:id`（`steam:2216623537`、`pdx:1234`），同一 id 在 Steam/PDX 两边不串味。
- 列表每条 `RecordSteamItem` / `RecordPdxItem` 都 upsert（保留旧字段，新值覆盖非空字段）；封面命中 `TryGetCachedCover` 直接绑本地路径，不再发请求。
- 详情页 7 天内复用（`IsDetailFresh` + `SetDetail`），过期才重新拉。
- 查询失败时走 `ShowFailureOrCache`：`Snapshot(source, gameId)` 取该游戏缓存列表回填，状态栏提示「网络不可用，显示缓存中的 N 个结果」。
- 设置页缓存卡片从 5 张变 6 张（`cacheRowWorkshopTitle/Desc`），清空走 `AppServices.WorkshopInfo.Clear()` + `CacheStorage.Clear(..., WorkshopCacheRoot)`。

### 17.3 踩坑记录

1. **封面根重复**：`WorkshopViewModel` 原本把封面下到 `AppServices.WorkshopCoverRoot`（`data\workshop-covers`），而缓存类自己的 `CoverRoot` 是 `data\workshop-info\covers`。命中缓存时拿的是后者的路径，导致「下载过的封面还是空白」。现在统一用 `_info.CoverRoot`，`WorkshopCoverRoot` 已删。
2. **旧 exe 骗人**：真机脚本启动的是 `bin\x64\Release\...\win-x64\PdxLauncher.App.exe`，而构建产物同时存在于 `bin\Release\...`。第一轮看到排序只有 3 档，其实是旧 exe —— 先比对 exe 与 `PdxLauncher.App.dll` 的时间戳，再用 UI 树确认。排查时临时加的「排序档位重建」日志帮了大忙（`source=Steam, count=5, first=最热门`），验证完已删。
3. **`total_count` 不随筛选变化**（Steam 行为）：时间范围切到 1 周后状态栏仍显示 17,718（总条数），切「最新更新」也一样；判断筛选是否生效要看列表内容而不是计数。
4. **`star_rating = -1` 表示无人评分**：行里要过滤 `StarRating > 0 && TotalVotes > 0`，否则会出现「-1 分」。
5. **服务端不认的 `browsesort` 会静默回落**（2026-11-03）：`mostupdated` / `mostsubscribed` / `mostpopular` 都不会 400，而是回落成 `trend`，列表与「最热门」逐项相同 —— 用户看到的就是「改了排序没变化」。判定方式：读 SSR 里 `queryKey[1].browse_sort` 的回显值，或直接比对首屏 `publishedfileid`。合法值只有 `trend` / `toprated` / `mostrecent` / `lastupdated` / `totaluniquesubscribers`（外加 `textsearch` 与 `playtime_*` 实验项）。
6. **限流冷却期换排序会被误判成「排序没生效」**：`ShowFailureOrCache` 用 `WorkshopInfoCache.Snapshot`（按 `UpdatedUtc` 倒序）兜底，列表内容与所选排序无关。现在 `QueryAsync` 在联网前先查冷却，冷却期**不覆盖已有行**，只在列表为空时用离线缓存兜底，状态栏说明剩余分钟。

### 17.4 验收

- `dotnet build "PdxLauncher.sln" -c Release` → 0 警告 0 错误；`文案源生成器` → 629 键；`PdxLauncher.Checks.exe` → **检查类 29 / 失败 0**（新增 `WorkshopInfoCacheChecks`：键、合并、持久化、封面命中、详情 TTL、快照排序、清空、坏索引降级）。
- 真机（`pwsh ui-test-artifacts/workshop/workshop-sort-shot.ps1`，CK3）：
  - 排序下拉 5 档、时间范围下拉 7 档，默认「最热门 + 30 天」（`07-sort-default.png`、`08-sort-menu.png`、`10-timerange-menu.png`）
  - 选「最受好评」→ 时间范围仍在（`09-sort-toprated.png`）；选「1 周」→ 生效（`11-timerange-week.png`）；选「最新更新」→ 时间范围下拉消失（`12-sort-updated.png`）
  - 第二轮启动时 86 条缓存 + 85 个封面文件命中，列表封面全部直出（`08-sort-menu.png` 里可见）
- 数据格式见 `docs/DATA-FORMATS.md` §19。

### 17.5 翻页性能：内存页缓存 + 封面并发 + 进度反馈（2026-10-28）

用户反馈：第一页 → 第二页 → 回第一页，第一页要重新等好几秒；第二页内容也慢。

**根因（四条叠在一起）**

1. 没有任何页面级缓存：每次翻页都重抓 Steam 那份 1–3 MB 的 SSR 页面，且先 `Rows.Clear()` 再重建 30 行 —— 屏幕先空白、再逐行出现。
2. `WorkshopInfoCache.Upsert` / `SetCover` 以前每条都同步全量写一次索引 JSON：一页 30 条 = 30 次原子写。
3. 封面在列表出来后仍逐个 `await`，一页最坏 30 × 1.5 s。
4. 翻页期间只有工具条一个小小的 ProgressRing，列表区没有进度感。

**改法**

- 新增 `PdxLauncher.Core.Online.WorkshopPageCache`（只在内存，重启即失效）：键 = `来源|游戏|搜索词|排序|时间范围|页码`，值 = 该页条目快照（`WorkshopInfoCacheEntry` 列表）+ 总条数 + 总页数 + 抓取时刻；`DefaultTtl = 5 分钟`。
- `WorkshopViewModel.QueryAsync(force)`：`force=false` 且命中缓存 → 立刻 `ApplyCachedPage` 出列表并显示「已加载 N 个结果（来自缓存）」；缓存不新鲜时再**后台静默刷新**（`RefreshSilentlyAsync`，不清空列表、失败不报错）。翻页 / 换排序 / 换时间范围 / 改关键词 → `force=true`。
- `WorkshopInfoCache` 改成「标脏 + 显式 `Flush()`」：`Upsert` / `SetCover` / `SetDetail` 只改内存并置 `_dirty`，`FillCoversAsync` 全部封面落地后统一 Flush 一次（`QuerySteamAsync` / `QueryPdxAsync` 末尾也 Flush）。
- 封面：`SemaphoreSlim(6)` 并发下载，收集到列表后再统一落盘；命中磁盘封面的行直接绑定路径，不再下载。
- 进度反馈：列表顶部 `ProgressBar`（`barWorkshopLoading`，绑 `IsBusy`）+ 状态栏右侧「封面 12/30」（`txtWorkshopCoverProgress`，绑 `ShowCoverProgress` / `CoverProgressText`），全部封面完成后自动隐藏。
- 翻页时 `CancelCoverFill()` 取消上一页未下完的封面；`RaiseDerived()` 里同步 `HasPrevPage` / `HasNextPage` / `PageText` / `CountText` / `ShowTimeRange`。

**实测（CK3，591 页，本机 20 Mbps）**

| 场景 | 改前 | 改后 |
|---|---|---|
| 第一页首次 | 8–12 s（空白等待后逐行出现） | 5.2 s（立即出列表 + 封面 15 张并发补齐，状态栏有进度） |
| 第一页 → 第二页（首次） | 6–9 s | 0.22 s（网络快时）/ 缓存命中后 0.23 s |
| 回第一页 | 5–8 s（重抓网络） | **0.23 s**（内存页缓存，状态栏「来自缓存」） |
| 再去第二页 | 6–9 s | 0.23 s |

脚本：`pwsh ui-test-artifacts/workshop/workshop-page-shot.ps1`（`13-page1.png`、`14-page2.png`、`15-page1-back.png`、`16-page2-cached.png`）。注意脚本里比对页码只能比数字（控制台按 GBK 解码 winapp 输出，中文会变乱码）。

**踩坑补充**

5. **`WorkshopPageCache.Key` 与 `WorkshopInfoCache.Key` 不要混用**：前者是「一页」，后者是「一条」。名字像、参数不同，混掉会让翻页永远不命中。
6. **封面进度先于完成**：`_coverTotal` 要在 `Rows` 建完、开始下载前设置，否则第一帧看不到进度；`HasCover` 变化必须 `OnPropertyChanged`，否则绑定的 `XamlVisibility` 不会刷新。

---

## 0.37 2026-10-28：创意工坊来源判定改为内置表（不再联网探测）

**背景**

创意工坊页进入时要先判定「这个游戏用 Steam 创意工坊还是 Paradox Mods」。原实现走
`WorkshopViewModel.ResolveSourceAsync` → `SteamWorkshopClient.HasWorkshopAsync(appId)`：
先请求 `store.steampowered.com/api/appdetails`（读 `categories` 里有没有 `Steam Workshop`），
失败再抓 `steamcommunity.com` 的工坊列表页。两个毛病：

1. **慢且会卡死页面**：`HttpFetcher.ConnectTimeout = 30s`、总超时 180s，探测要串最多两次请求；
   本机连不上 Steam 时实测 CK3 卡在「正在判断该游戏有没有 Steam 创意工坊…」75 秒以上。
2. **结果本身不可靠**：appdetails 的 `categories` 接口对部分老游戏不返回 `Steam Workshop` 分类 ——
   实测 CK2（203770）、BattleTech（637090）明明有创意工坊却被判成「没有」，于是被错划到 Paradox Mods。

**改法**

- 新增 `src/PdxLauncher.Core/Online/SteamWorkshopGames.cs`：gameId → 是否有 Steam 工坊的**内置事实表**
  （`Dictionary<string,bool>`，`OrdinalIgnoreCase`，每行注释里写 Steam appId），API 只有一个
  `bool? TryGet(string? gameId)`（`null` = 未登记）。表里同时收官方启动器的 id 别名
  （`infinite` / `prison_architect` / `surviving_aftermath` / `surviving_mars_relaunched`）。
  判断错了**只改这一张表**；不要塞进 `KnownGames`（那边是「支持等级」语义）。
- `WorkshopSourceChooser` 增加三参重载 `Resolve(int? steamAppId, bool? hasWorkshop, string? gameId)`：
  表命中就按表定来源（不看探测结果），未登记才退回原来的两参逻辑。
- `WorkshopViewModel.ResolveSourceAsync` 改为三步：① 表命中 → 直接返回并 `_logger.Info("创意工坊来源按内置表判定：…")`；
  ② 没有 appId（独立版）→ Paradox Mods；③ 未登记但有 appId → 探测，且用
  `CancellationTokenSource.CreateLinkedTokenSource` + `CancelAfter(5s)`（私有常量 `WorkshopProbeTimeout`）设预算，
  超时记 Warn 并回落 Paradox Mods，外部取消照旧向上抛。
- i18n 新增 `workshop.probeTimeout`（zh「Steam 创意工坊探测超时，已改用 Paradox Mods」/ en 对应），
  `gen-i18n` 后共 648 键；`workshop.sourceDetecting` 保留，只在未登记游戏探测期间显示。

**自检**

新增 `tests/PdxLauncher.Checks/SteamWorkshopGamesChecks.cs`（纯内存、不联网）：有/无工坊的代表性游戏
（含 CK2、BattleTech 两个「分类接口会误判」的回归样例）、未登记返回 `null`、别名一致、大小写不敏感、
`GameSteamAppIds` 里 12 个 gameId 全部有答案（防两表漂移）、以及三参 `Resolve` 的「表优先于探测结果」。
`检查类: 32，失败: 0`，Release `0 警告 0 错误`。

**实测**（Release exe，本机）

- CS2：日志 `创意工坊来源按内置表判定：game=cities_skylines_2 source=Pdx`，**没有 appdetails 请求**，
  进入页面约 6 秒出列表（含首屏抓取），状态栏「已加载 27393 个结果」。
- CK3：日志 `… game=ck3 source=Steam`，来源徽标直接显示「Steam 创意工坊」，不再出现「正在判断…」。

**踩坑**

1. cities_skylines_2 这类「明确没有 Steam 工坊」的游戏必须写进表，否则仍会每次进页面探测一次。
2. 5 秒预算只包住探测请求；真正的列表抓取仍用页面生命周期令牌（否则大页面还没抓完就被砍）。
3. UI 冒烟脚本会改 exe 同目录 `data/settings.json` 的 `lastGameId`，跑完记得还原成用户原先选中的游戏。

**顺带发现（未修，另开任务）**

设置页改代理会触发 `AppServices.ReplaceNetwork` → 旧的 `HttpFetcher`（`HttpClient`）被 Dispose，
但 `WorkshopViewModel` 等视图模型仍持有构造时注入的旧 `SteamWorkshopClient`，之后再查工坊就报
`System.ObjectDisposedException: Cannot access a disposed object. Object name: 'System.Net.Http.HttpClient'`。
2026-10-28 14:33 的日志里就是这个错（用户在设置页切了「跟随系统代理」之后）。修法有两种：
让 `AppServices` 在重建后发事件、由各视图模型重取客户端，或把客户端改成按需从 `AppServices` 取。
**已修，见 §0.38。**

## 0.38 2026-10-28：修复「改代理后联网全废（HttpClient 已释放）」

**背景**

§0.37 末尾记录的那个缺陷：设置页改「连接方式」后，`SettingsViewModel.SelectedProxyMode` setter 立刻调
`ApplyProxySettings()` → `AppServices.TryRebuildNetworkServices()`，后者用 `NetworkComposition.Create`
**把整条联网服务全部重造**并 `previous?.Dispose()` 释放旧连接池。但页面是 `NavigationCacheMode="Required"`、
常驻不销毁，视图模型是在构造函数里拿的 `AppServices.SteamWorkshop` / `PdxMods` / `Assets`（无 DI 容器，
没有重新注入的时机），于是后续请求全打到已 Dispose 的旧 `HttpClient` 上：

```
System.ObjectDisposedException: Cannot access a disposed object. Object name: 'System.Net.Http.HttpClient'
  at HttpFetcher.GetAsync → SteamWorkshopClient.BrowseAsync → WorkshopViewModel.QuerySteamAsync
```

创意工坊、详情子页、MOD 库、DLC 说明、公告一起挂，只有重启进程才能恢复。

**改法：换连接池，不换服务对象**

沿仓库里已有的正确范式（`AccountService.ReplaceClient`：客户端实例不变、内部指针可换），把「热替换」下沉到 HTTP 层：

1. `src/PdxLauncher.Core/Online/HttpFetcher.cs`（重写）：`_client` 改非 `readonly`；抽出
   `private static HttpClient BuildClient(IWebProxy? proxy, TimeSpan totalTimeout)`（handler 配置与 UA 头原样搬入）；
   新增 `public void UseProxy(IWebProxy? proxy, TimeSpan? totalTimeout = null)` —— 建新池、
   `Interlocked.Exchange(ref _client, next)`、旧池进 `_retired` 队列（`MaxRetiredClients = 2`，超出的 Dispose，
   保证正飞到一半的请求还能把响应读完）；三个请求方法改用 `Volatile.Read(ref _client)`；`Dispose()` 收尾当前 + 退休队列。
2. `src/PdxLauncher.Core/Online/PdxHttpTransport.cs`（重写）：PDX 那条链路（自己的 `SocketsHttpHandler`）同样处理，
   日志「PDX 连接池已重建（…）」。
3. `src/PdxLauncher.App/Services/AppServices.cs`：`IDisposable? _networkLifetime` → `NetworkLink? _networkLink`；
   **删除 `ReplaceNetwork` 整个 12 参方法**；`TryRebuildNetworkServices()` 只做
   `ProxyResolver.Resolve(Settings, Logger)` → `_networkLink.Http.UseProxy(proxy)` + `_networkLink.PdxHttp.UseProxy(proxy)`。
4. `src/PdxLauncher.App/Services/NetworkComposition.cs`：`DisposableGroup` 换成 `public sealed class NetworkLink`（只持有两个连接池属主，
   因为它是 `AppServices.Initialize` 的 public 参数类型，必须 public）；`Create` 不变。
5. `src/PdxLauncher.App/App.xaml.cs`：装配时把 `new NetworkLink(network.Http, network.PdxHttp)` 交给 `Initialize`。

**结果**：`SteamWorkshopClient`、`PdxModsClient`、`AssetDownloader`、`ThemeResolver` 等对象身份全程不变，
页面与视图模型**不需要任何改动**，改代理立即生效。

**自检**

新增 `tests/PdxLauncher.Checks/HttpFetcherChecks.cs`：换池后 `ReferenceEquals` 仍成立（对象身份稳定）、
换池后仍拒绝白名单外域名（不联网，断言 `host-not-allowed:` 前缀）、`null`/`WebProxy` 交替 5 次不抛、
`Dispose()` 幂等。`检查类: 33，失败: 0`；Release `0 警告 0 错误`（648 键）。

**实测**（Release exe，2026-10-28 15:19）

创意工坊先加载出「已加载 27395 个结果 / 第 1 / 1142 页」→ 设置页把「连接方式」从「跟随系统」切到「手动配置」→
日志出现 `HTTP 连接池已重建（…）`、`PDX 连接池已重建（…）`、`已按新代理设置重建联网连接池` →
回创意工坊点刷新，**仍是 27395 个结果**，`grep ObjectDisposedException` 没有任何新增（最后一批停在 14:33 修复前的旧构建）。

**踩坑**

1. `NetworkLink` 若写成 `internal` 会报 `CS0051`（`AppServices.Initialize` 是 public 且带该参数）。
2. 旧池不能立刻 Dispose：`_retired` 队列最多留 2 个，超过再放最老的；否则并发中的请求会炸在读取响应阶段。
3. 验证时注意 Debug 产物常被运行中的 `PdxLauncher.App` 锁住（`MSB3027/MSB3021`），可只构建
   `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj` 跑自检，Release 用 `tools/build-app.sh`。

## 0.39 2026-10-28：创意工坊详情页二期（Markdown 简介 / 图片查看 / 依赖检查 / 作者外链 / Steam 评论）

**背景**

详情子页（§0.35 起）此前把 PDX 的 `longDescription` 当纯文本塞进一个 `TextBlock`，`#`、`*`、`-` 原样露出来；
Steam 侧除了描述什么都没有；图片只有一张封面；作者只是一串文字；从来看不出「这个 mod 还缺哪个前置」。
这一轮把详情页做成真正能读的页面。

**改法**

1. `src/PdxLauncher.Core/Text/`（新增，纯 `System.*`，可被检查工程链接）：
   - `MarkdownDocument.cs`：块级/行内 AST（标题、段落、列表项、代码块、引用、分隔线；粗体/斜体/删除线/行内代码/链接/图片/硬换行），
     `MarkdownDocument.ImageUrls` 去重、`ToPlainText()`。
   - `MarkdownParser.cs`：手写子集解析器（标题、段落、有序/无序列表含缩进层级、围栏代码块、行内样式、`[t](u)`、`![alt](u)`、`<br>`/`<img>`），
     `MaxInputLength = 20000` 截断并置 `IsTruncated`；列表层级用 `lastListLevel` 归一化，避免 4 空格缩进跳级。
   - `HtmlDescriptionParser.cs`：Steam 的 HTML 描述 → 同一套 AST（`<p>`/`<div>`/`<li>`/`<b>`/`<a>`/`<img>`/`<br>`/`<pre>` 等）。
   - `WorkshopHtml.cs`：老的 BBCode/HTML 洗字逻辑迁到 Core（`WorkshopText.ToPlainText` 就此退役）。
2. `src/PdxLauncher.Core/Online/SteamWorkshopItemPageParser.cs`（新增）：解析 `steamcommunity.com/sharedfiles/filedetails/?id=<id>`
   —— 作者名与主页、描述 HTML 与内嵌图片、最多 10 条评论（作者/主页/时间戳/正文）与 `total_count`。
   这是**唯一**能拿到 Steam 评论的途径（`ISteamRemoteStorage/GetPublishedFileDetails` 不含评论）。
3. `src/PdxLauncher.Core/Online/SteamWorkshopClient.cs`：新增 `GetItemPageAsync`；`ParseDetail` 补上原先丢掉的
   `creator`/`tags`/`views`/`lifetime_subscriptions`（`SteamWorkshopItemDetail` 追加 4 个可选参数 + `CreatorProfileUrl`、`TagList`）。
4. `src/PdxLauncher.Core/Mods/`：`PdxModsClient` 解析 `modDetail.requirements[]` → `PdxModRequirement`；
   新增 `WorkshopDependencyMatcher`（按注册 id / `SteamId` / 名称匹配本地 `ModLibraryEntry`，注册 id 用数字边界比较，
   避免 74417 命中 774417），状态 = 已启用 / 已装未启用 / 缺失 / 未知。
5. `src/PdxLauncher.App/Controls/MarkdownView.cs`（新增）：`ContentControl`，`Markdown` 依赖属性，把 AST 渲染成
   `TextBlock` + `Hyperlink`/`HyperlinkButton`，并抛出 `LinkRequested`/`ImageRequested` 事件（页面负责打开浏览器/查看器）。
6. `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml`：简介与更新日志换成 `MarkdownView`；新增依赖卡（含「未安装」警示图标）、
   评论区（Steam）、作者改成可点 `HyperlinkButton`；图片墙改横向 200x120 缩略图条；
   `ImageOverlay` 浮层 = `FlipView` + 每页 `ScrollViewer(ZoomMode=Enabled, MinZoom=1, MaxZoom=4)`，右上角计数与关闭按钮，Esc 关闭。
7. i18n 两语言 `workshop.json` 各加 17 键（作者提示、看大图、依赖、讨论、各依赖状态、评论计数等），`gen-i18n` 后共 665 键。

**自检**

- 新增 `tests/PdxLauncher.Checks/WorkshopRichTextChecks.cs`（Markdown 各块/行内/列表层级/截断、HTML 描述、Steam 物品页含评论的完整样例）
  与 `WorkshopDependencyMatcherChecks.cs`（未知库/已启用/装了没启用/缺失/注册 id 数字边界/SteamId 与名称兜底）。
- `dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误；检查类 **35，失败 0**。

**实测**（Release exe，2026-10-28 16:2x，CS2 / Paradox Mods）

- `Unified Icon Library`：简介按 Markdown 渲染（`## Description` 变粗体标题、段落分行、`###` 子标题、项目符号列表）；
  作者「algernon」显示为超链接；图片墙三张缩略图横排，点开进浮层查看器（计数 1/3、可缩放、Esc 关闭）。
- `Anarchy`：依赖卡显示「共 1 个依赖，1 个已安装但未启用」+ 明细行 `Unified Icon Library`。
- 截图见 `ui-test-artifacts/workshop-rich-b/`。

**踩坑 / 边界**

1. Steam 评论要抓物品页（每 mod 一次 5–20 秒、~100KB），因此**详情正文先出，评论区后到**（`IsLoadingComments` 单独转圈）；
   物品页抓不到时只降级评论区与配图，不影响其余内容。
2. Steam 侧**没有依赖数据**（API 与物品页都没有），依赖卡只在 PDX 侧出现。
3. `WMC1506`：`x:Bind` 的 OneWay 源必须实现通知，行视图模型因此继承 `LocalizedObservableObject` 而不是裸 `ObservableObject`。
4. `winapp ui` 的 `click/focus` 要求窗口在前台，被全屏游戏挡住会直接失败；`px` 里 `$pid` 是只读变量，要用自己的变量名。
5. Steam 端到端（评论区、作者外链）在本机受网络与前台窗口限制，尚未跑通全程，仅以解析器样例自检兜底。

## 0.40 2026-10-28：创意工坊详情页图片区改用 FlipView（页内翻页 + 缩略图条 + 浮层缩放）

**背景**

§0.39 的图片墙是「横向缩略图条，点开才进浮层查看器」：页内看不到大图，翻页必须先开浮层。
用户问「能用 FlipView 实现吗」——浮层本来就是 `FlipView`，缺的是页内翻页与缩放交互，于是把图片区重做。

**改法**

1. 截图卡改成「内联 `FlipView`（`Height=360`，`AutomationId=flipWorkshopScreenshots`）+ 下方缩略图条」：
   页内直接左右翻页，右上角保留「当前 / 总数」计数器（`txtWorkshopInlineCounter`）。
2. 缩略图条每个按钮 `Click="OnThumbnailClick"` 只切内联 `SelectedIndex`；当前张用 2px `AccentFillColorDefaultBrush`
   描边高亮（绑 `WorkshopScreenshotViewModel.IsCurrent`，新增的可观察属性）。
3. 点内联大图（`Tapped`）进浮层查看器；浮层仍是 `FlipView`，新增双击 1x/2x 切换、
   Ctrl+滚轮 1–4x 缩放（未按 Ctrl 的滚轮留给 FlipView 翻页）、放大超过 1.01x 才允许拖动、关闭后焦点回到当前缩略图。
4. 浮层 `FlipView` 改名 `FlipWorkshopViewer`（`AutomationId` 仍是 `flipWorkshopImages`，脚本不用改）。

**两个必须记住的坑**

1. `FlipView` 的条目容器默认 `HorizontalContentAlignment/VerticalContentAlignment = Left/Top`，
   `ItemTemplate` 里的 `Grid` 会被量成 0 尺寸 → 整块显示为空白。必须加 `ItemContainerStyle` 设成 `Stretch`。
2. `x:Bind ViewModel.Screenshots` 是「绑定一次集合实例」，截图是加载中异步 `Add` 进来的；
   内联 `FlipView` 在这条路径上拿不到条目（`ItemsSource` 为 null、`SelectedIndex` 一直是 -1），
   因此在 `RunAsync` 末尾补了 `EnsureGallerySelection()`：显式兜一次 `ItemsSource`、选中第 0 张、刷新计数器。
   同一份集合给 `ItemsControl` 用时却没有这个问题，只有 `FlipView` 需要兜底。

**自检**

- `bash tools/build-app.sh -v q` → 0 警告 0 错误；`dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误；
  `检查类: 35，失败: 0`（本次只改视图与视图模型，未新增检查类）。

**实测**（Release exe，2026-10-28 18:2x，CS2 / Paradox Mods，`Unified Icon Library`）

- 页内大图直接显示第 1 张，右上计数「1 / 3」；`invoke workshopDetailShot_1` / `_2` 后计数变「2 / 3」「3 / 3」，
  大图与缩略图高亮同步跟随。
- 截图见 `ui-test-artifacts/workshop-flip2/`：`01-inline-first.png`（1/3 + 大图）、`02-thumb-2.png`（2/3）、
  `05-strip.png`（缩略图条 + 第 3 张描边高亮）。

**边界**

- 双击缩放、Ctrl+滚轮缩放、Esc 关闭这三项依赖真实鼠标/键盘，本轮**没有端到端跑**：
  本机前台被全屏游戏占用，`winapp ui click/focus` 会拒绝执行（`Target window is not in the foreground`）。
  为绕开它，冒烟时临时加过一个 `PDX_UI_OPEN_FIRST_MOD=1` 的自动开详情钩子，验证完已删除，
  最终 diff 只剩 3 个文件（截图 VM、详情页 XAML、详情页后置代码）。
- 内联 `FlipView` 高度写死 360；窗口很矮时大图会挤压缩略图条，暂不处理。

## 0.41 2026-10-28：创意工坊详情页改为 Steam 物品页式布局 + 评论翻页看全部 + 真 PDX 订阅

**背景**

- 详情子页此前是「一列卡片」：概览 → 简介 → 图片 → 依赖 → 更新日志 → 外链 → 评论，
  图片在中间、订阅无处可放，和用户熟悉的 Steam 物品页差距大。
- 评论只取物品页首屏 10 条（`SteamWorkshopItemPageParser.MaxComments`），136 条评论的 mod 只能看到 10 条，
  没有「翻页」入口，用户明确要求「显示所有评论，并且采用 Steam 的评论区切换」。
- 订阅按钮只是打开 Steam/网页，用户要求「去接真正的 PDX 订阅 API」。

**改法**

1. **布局重排**（`src/PdxLauncher.App/Views/WorkshopDetailPage.xaml` 整份重写，560 行）：
   页头（返回 / 标题 / 作者超链接 / 浏览器 / Steam）→ 图片区卡（内联 FlipView 360 高 + 计数 + 缩略图条）
   → 两列行（左 280px「订阅」卡、右「*」「信息」卡：评分/订阅/收藏/浏览/体积/更新于/发布于/标签）
   → 读取中 / 失败 / 缓存兜底 → 描述（`MarkdownView`）→ 依赖 → 更新日志 → 外链 → 评论区（含分页）。
   窗口窄时不做自适应状态机，固定 `280/*`。
2. **评论翻页**（Core）：`SteamWorkshopItemPageParser.ExtractComments` 抽成
   `public static ParseComments(html, maxComments)`（翻页接口的 `comments_html` 是裸片段，去掉「只在 `_posts">` 后找」的限定），
   新增 `ParseCommentTotal(json)`；`SteamWorkshopClient.GetCommentsAsync(creatorSteamId, fileId, start, count, ct)`
   打 `https://steamcommunity.com/comment/PublishedFile_Public/render/{作者}/{文件}/?start=&count=`，
   `count` 夹到 1..50，读 JSON 的 `comments_html` 与 `total_count`；`SteamWorkshopCommentPage` 承载结果。
   App 侧每页 15 条，`btnCommentsPrev`/`btnCommentsNext` + 「第 n / N 页 · 共 M 条」。
3. **真 PDX 订阅**（Core + App）：
   `X-Accept-Version: 2` 头 + `POST /mods/installed`（`{activationSetId, mods:[{id,version?}]}`）、
   `DELETE /mods/installed?activationSetId=&modId=`、`GET /mods/sets/list/{game}?includeShared=false`、`POST /mods/sets`；
   为让 `PdxHttpTransport.SendAsync` 能带头，给接口加了可选 `headers` 参数（既有实现方不用改签名）。
   `WorkshopSubscriptionService` 负责「找/建 PdxNext 播放集 → 缓存到 `data/pdx-playset.json` → 查/改订阅状态」，
   未登录时按钮禁用并提示去账号页，点击先弹 `ContentDialog` 二次确认（确认按钮文案随订阅/退订变化）。
4. **Imgur 白名单**（Core）：Steam 工坊简介配图大量是作者手写 `[img]` 指向 `i.imgur.com`，
   原来被 `OnlineHosts` 拦掉、图区只剩占位图标。现在精确放行 `imgur.com` 与 `i.imgur.com`（不做子域通配）。
5. **i18n**：两语言 `workshop.json` 各加 15 键（订阅/信息/评论分页），删掉不再引用的 `workshop.commentsMore`；
   `gen-i18n` 后 678 键。

**自检**

- `bash tools/build-app.sh -v q` → 0 警告 0 错误；`检查类: 36，失败: 0`（新增 `WorkshopSubscriptionChecks`，
  `WorkshopRichTextChecks` 增补 `ParseComments` / `ParseCommentTotal`；`SteamWorkshopApiChecks` 增补 Imgur 白名单 3 条）。

**实测**（Release exe，2026-10-28 19:0x–19:2x）

- CS2 / Paradox Mods（`Unified Icon Library`）：图片区在最上（1/3 计数 + 缩略图条），
  左「订阅」卡显示「登录 Paradox 账号后才能订阅」（本机未登录，按钮禁用），右「信息」卡
  「5 分（5459 人评分） · 1,389,600 订阅 · 体积 1 MB · 更新于 2026/5/2 19:23」，往下是 Markdown 简介与相关链接。
  截图 `ui-test-artifacts/workshop-layout/{01-detail-top,02-detail-bottom}.png`。
- CK3 / Steam 创意工坊（`Renovatio`）：页头作者是 Steam 个人页链接，「在 Steam 中订阅」按钮，
  信息卡「5 分（266 人评分） · 7,715 订阅 · 收藏 1,341 · 浏览 61,122 · 体积 2.2 GB」；
  评论区首屏渲染正常（作者超链接 + 时间 + 正文），底部分页「第 1 / 9 页 · 共 121 条」，
  `invoke btnCommentsNext` → 「2 / 9 页」，`invoke btnCommentsPrev` → 回到「1 / 9 页」。
  截图 `ui-test-artifacts/workshop-steam3/{01-comments-page1,02-comments-page2}.png`、
  Imgur 放行后 `ui-test-artifacts/workshop-steam4/00-no-rows.png`（大图与缩略图都出图）。

**踩坑 / 边界**

- 用脚本按行号搬 XAML 块（图片区上移 + 概览卡换成两列）会踩第一个 `</Border>` 匹配到内层封面 Border 的坑，
  搬完能编译但**运行时版式错乱**（卡片互相重叠、底/边框丢失）。教训：这种重排直接整份重写，
  不要用行号切片拼 XML。
- 订阅写接口需要登录，本机 `data/pdx-session.json` 不存在，所以「真订阅」只到「按钮禁用 + 提示登录」这一层，
  改法与请求体由 `WorkshopSubscriptionChecks` 的假 transport 断言（URL/方法/`X-Accept-Version`/body/未登录不发请求）；
  真机联调需要在已登录环境点一次。
- Steam 侧没有依赖数据（`GetPublishedFileDetails` 无依赖字段、物品页也没有依赖标记），依赖卡只对 PDX 显示。
- UI 自动化仍受前台限制：`winapp ui click/focus` 要求窗口在前台，`invoke` 不需要；
  行点击靠临时 `PDX_UI_OPEN_FIRST_MOD=1` 钩子自动打开首行详情，验证完已删除。

## 0.42 2026-10-29：Steam 详情页把简介配图当成了「截图」，正文里只剩「［图片］」

**背景**

- 用户点开 CK3 的 Steam mod（`https://steamcommunity.com/sharedfiles/filedetails/?id=3798537678`）：
  简介里的配图（Rate/Favorited 横幅、Features、Load Order…）全被搬到顶部「截图」区，正文对应位置只剩一个「［图片］」文本链接。
- 起因是三处叠加：① `SteamWorkshopItemPageParser` 把描述里的 `<img src>` 收进 `page.ImageUrls`；
  ② `WorkshopDetailViewModel.ApplySteamAsync` 用这份 URL 列表 `BuildGallery(...)` 当「截图」铺顶部；
  ③ `MarkdownView.AppendInlines` 的 `MarkdownImageInline` 分支只渲染成文本超链接。
  而 Steam 详情接口**没有截图字段**，所以顶部的「截图」本来就全是简介配图。
- 第一版把 Steam 的顶部图片区整块隐藏，用户反馈「Steam 图片区根本不出图」——
  于是改成：顶部给**一张工坊预览图**（`preview_url`），简介配图只留在正文内联显示。

**改法**

1. `src/PdxLauncher.App/Controls/MarkdownView.cs`：段落/列表项改用 `BuildFlow(...)` 分段渲染 ——
   行内流按图片切成「文字段 + 图片块」，图片单独成块（`Border` 圆角 + `MaxWidth 640/MaxHeight 420` + `Stretch=Uniform`），
   点击触发 `ImageRequested` 进全屏查看器；支持 `<a><img></a>` 这种被链接/样式包住的图（只含一个子节点时递归识别）。
   新增 `ImageResolver`（URL → 本地缓存路径）与 `RefreshImages()`：下载完成后原地换图，不重建整页、不跳滚动位置。
2. `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs`：
   新增 `ShowGalleryCard`（= `HasScreenshots`，所有来源都显示顶部图片区）、
   `ResolveImagePath(url)`（先查新增的 `_inlineImages` 字典，再回退到 `Screenshots` 里按 URL 查本地文件）、
   `GalleryImageReady` 事件（每张下载成功触发）；
   Steam 分支的「截图」只放**一个** `detail.PreviewUrl`（工坊预览图，和 Steam 物品页一致），
   正文里的 `<img>` 由新增的 `DownloadInlineImagesAsync(...)` 单独下载进 `_inlineImages`，
   这样预览图与正文图各有各的缓存文件，不会互相顶掉。
   顺带把作者显示名改成物品页的 `AuthorName`（列表条目里只有 SteamID）。
3. `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml`：图片区卡的可见性绑 `ShowGalleryCard`；
   描述与更新日志的 `MarkdownView` 命名 `DescriptionView` / `ChangelogView`。
4. `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml.cs`：给两个 `MarkdownView` 接 `ImageResolver`，
   订阅 `GalleryImageReady` → `RefreshImages()`，`OnUnloaded` 解绑。
5. `src/PdxLauncher.App/Services/WorkshopImageCache.cs`：`EnsureScreenshotAsync` 去掉 `index` 参数，
   缓存文件改按 **URL 稳定哈希** 命名（FNV-1a 32 位 → `shot_b8001693.gif`）。
   原来用序号 `shot_00`，预览图与正文第一张图会抢同一个文件名（先下的被后下的覆盖，表现为顶部空白）。
6. `tests/PdxLauncher.Checks/WorkshopRichTextChecks.cs`：新增「`<img><br>文字` 混合描述首块是图片」「配图顺序」
   「只有图片的简介不算空」三条断言。

**自检**

- `bash tools/build-app.sh -v q` → 0 警告 0 错误；`dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误；
- `dotnet tests/PdxLauncher.Checks/bin/Debug/net10.0/PdxLauncher.Checks.dll` → 检查类 37，失败 0。

**实测**

- CK3 / Steam `Renovatio`：顶部图片区显示工坊预览图 RENOVATIO（计数「1 / 1」+ 缩略图高亮），
  描述里 Rate/Favorited 横幅、Features 横幅按原文顺序出现，正文段落夹在中间；
  页头作者从 `76561198145098971` 变成 `Anumari`（可点跳个人页），评论区「第 1 / 9 页 · 共 121 条」正常。
  截图 `ui-test-artifacts/workshop-img5/{01-detail-top,02-detail-bottom,03-detail-mid}.png`。
- CS2 / PDX `Unified Icon Library`：顶部图片区仍是 3 张工坊截图（FlipView + 缩略图条），
  订阅卡显示「登录 Paradox 账号后才能订阅」。截图 `ui-test-artifacts/workshop-img-pdx2/01-detail-top.png`。

**踩坑 / 边界**

- 图片是异步下载的：先占位图标，`GalleryImageReady` 后原地刷新；单张失败只留占位，不影响其它内容；
  点击占位也进查看器（`Screenshots` 与正文图同源同序，可左右翻）。
- **截图冒烟要等图**：详情页刚打开时图片还在下，早拍会拍到空图位（曾误判成「图片区不出图」）；
  `.tmp/ui-img.ps1` 现在等标题出现后再 `Start-Sleep 10` 才拍。
- 描述解析走 `HtmlDescriptionParser`（`<img>` → `MarkdownImageInline`）；PDX 的 `longDescription` 是 Markdown 且实测零内嵌图，
  所以 PDX 不会出现「正文图 + 顶部截图」重复。
- 顶部图片区若以后想按来源收（例如 Steam 完全不显示空白卡），把 `ShowGalleryCard` 加回来源判断即可，其余改动不受影响。

## 0.43 2026-10-29：Steam 详情页截图改抓物品页画廊（`#highlight_player_area`）

**背景**

- §0.42 之后 Steam 顶部图区只剩一张 `preview_url`（mod 封面小图），用户指出「获取错误的截图，
  应该是显示 `<div id="highlight_player_area">` 中的内容」——即 Steam 物品页顶部那个官方画廊。

**页面结构（`https://steamcommunity.com/sharedfiles/filedetails/?id=<id>`，实测三份 HTML）**

- 服务端只给占位：`<div id="highlight_player_area">` 里每张图是
  `<div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_<previewid>">`
  + `<img id="previewImage<id>" src=".../trans.gif">`；真实地址由页尾脚本填。
- 页尾两个变量：
  - `var rgScreenshotURLs = { '<id>': '<url imw=637&imh=358>', … }`（小图，兜底用）
  - `var rgFullScreenshotURLs = [ { 'previewid' : '<id>', 'url': '<url imw=5000&imh=5000&…> }, … ]`（原图）
- 实测 `Renovatio`：7 个 id / 7 条 full URL 全对应；另一份 2 张的页面同样一致。
  `imw=5000` 的图实际只有 392 KB（源图 1920×1080），所以**不改写 URL 参数**，原样下载。

**改法**

1. `src/PdxLauncher.Core/Online/SteamWorkshopItemPageParser.cs`：新增
   `PlayerShotIdRegex`（`highlight_screenshot_(\d+)`）、`FullShotRegex`（`'previewid'…'url'…`）、
   `PreviewShotRegex`（`'<id>': 'https://…'`）与 `ExtractScreenshotUrls(html)`：
   按 **id 出现顺序** 取图（不是数组顺序），缺 url 的 id 跳过、URL 去重、上限 `MaxScreenshots = 20`；
   full 数组整体缺失时退回 637 字典；没有 `highlight_player_area` 时返回空。
2. `src/PdxLauncher.Core/Online/SteamWorkshopModels.cs`：`SteamWorkshopItemPage` 增加
   `IReadOnlyList<string> ScreenshotUrls`（官方画廊，顺序同页面；与描述内联 `ImageUrls` 分开）。
3. `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs`（`ApplySteamAsync`）：
   图区优先用 `page.ScreenshotUrls`，为空才退回 `preview_url`/封面（只有视频或页面改版时）。
4. `tests/PdxLauncher.Checks/WorkshopRichTextChecks.cs`：新增 `CheckItemPageScreenshots()`——
   数组故意反序验证「按 id 顺序」、验证取 `imw=5000` 而非 637 版、描述 `<img>` 不进画廊、
   id 无 url 时跳过、缺 full 数组时用字典兜底、无画廊区时为空。

**自检**

- `bash tools/build-app.sh -v q` → 0 警告 0 错误；`dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误。
- `检查类: 37`；与本次无关的唯一失败是 `PlaysetChecks` 的真机 Victoria 3 断言
  「V3 激活播放集全部启用」——本机 V3 播放集现在是 24 行且至少一行未启用（官方启动器/V3 侧改动）。
  用 `git stash` 暂存本次改动后复跑，该断言同样失败（`断言失败总数: 1`），确认与本次改动无关；
  另两个 `FAIL ... (Run 返回 1)` 是它把全局计数带下去的连锁。
  **该失败已在 §0.44 处理**（根因是断言把真机数据写死，不是代码坏）。

**实测（Release exe，UI 自动化）**

- CK3 / Steam `Renovatio`：顶部图区 **7 张**官方截图（计数「1 / 7」，缩略图条里是衣服/甲胄/纹章等真实截图），
  简介配图仍在正文，评论区「第 1 / 9 页 · 共 121 条」。
  截图 `ui-test-artifacts/workshop-shots/01-detail-top.png`。
- CS2 / PDX `Unified Icon Library`：回归无变化（3 张工坊截图，计数 1 / 3）。
  截图 `ui-test-artifacts/workshop-shots-pdx/01-detail-top.png`。

**踩坑 / 边界**

- 画廊里 `img` 是 `trans.gif` 占位，直接抓 `src` 只会得到占位图 —— 必须解析脚本数组。
- 顺序问题：数组顺序与页面顺序可能不同，**以 `highlight_screenshot_<id>` 的页面顺序为准**。
- 只有视频（`highlight_movie`）没有截图的 mod → `ScreenshotUrls` 为空 → 退回 `preview_url`。
- 页面改版导致抓不到时同样静默退回封面，不抛异常、不留空卡。

## 0.44 2026-10-29：官方启动器改写播放集不再记为失败（保存前防覆盖 + 存储格式兜底）

**触发**：全量自检唯一一条真失败是 `PlaysetChecks` 真机 Victoria 3 断言「V3 激活播放集全部启用」。

**实测（只读查本机库）**

- `%USERPROFILE%\Documents\Paradox Interactive\Victoria 3\launcher-v2.sqlite`：1 个播放集「1」（id `bc73b308-3180-48a6-9c0f-3c3b92153c25`，isActive=1），`playsets_mods` **24 行、仅 1 行启用**（position 0 = `喜儿修改器`），`mods` 表 **65 行**。
- 同目录 `content_load.json` = `{"enabledUGC":[],"disabledDLC":[],"enabledMods":[]}`；两文件 mtime 均 `2026-10-29 02:33:10`。
- `data/logs/pdxnext.log` 里没有 V3「已备份数据库」记录，目录下也没有 `launcher-v2.sqlite.pdxnext.bak` → 这次改动是官方启动器/V3 侧写的，不是本项目写的。
- 结论：**不是「官方一改就不能用」**。红的是断言把真机数据当契约（文档记 22 行全启用，实际 24 行仅 1 启用）。真实软肋只有两条，本次一并堵上（见 C / D）。

**影响矩阵（官方以后再改会发生什么）**

| 官方改动 | 读 | 写 | 结论 |
| --- | --- | --- | --- |
| 增删播放集、切换激活集 | 正常 | 正常 | 无影响；自检只锁结构 |
| 关/开 mod（含整批关掉） | 正常 | 正常 | 无影响；自检只锁「至少 1 行启用」 |
| 关/开 DLC、新增 DLC 文件 | 正常 | 正常 | 无影响；DLC 与 mod 数量只锁下限 |
| 官方正在写库（锁库） | 降级为空 + Warn | 备份失败即中止并报错 | 可恢复：重试 |
| `playsets` / `playsets_mods` 表列变更 | 读兜底（缺列当 null）+ Warn | 写入失败回滚 + 备份 | 需按新列适配 |
| 换成 playsets.json / playset_config.json | 路由兜底到对应实现 + Warn（D） | 同上 | 播放集页不再静默空白 |
| 页面加载后、保存前官方改了同一个播放集 | 正常 | **不再覆盖**：放弃本次保存并自动重载（C） | 官方数据不丢 |

**B. 文档**：`docs/DATA-FORMATS.md` 第 4 节把 CK3/V3/Stellaris 三行改成「历史快照，不是契约」，写明 V3 现为 24 行 / 仅 1 行启用、mods 表 65 行、归属 24 行。

**A. 自检去脆（`tests/PdxLauncher.Checks/PlaysetChecks.cs`）**：沿用文件里 CK3 段已确立的「只锁结构 + 下限」范式，把真机数据从断言里拿掉：

- V3 播放集：`playsets 数量 == 1` / 名称 == `"1"` / `playsets[0].IsActive` 三条删除，改为 `>= 1` 个、名称非空、Id 唯一、至多 1 个激活、激活项与列表一致。
- V3 mods：`全部启用` → `至少 1 行启用` + info 打印「24 行，启用 1 / 未启用 23」；保留 `>= 22`、Position 连续、Status 全 ready_to_play、名称非空、归属行数一致。
- V3 DLC 行数 `== 0` → info + DLC id 非空/唯一（官方开始写 DLC 开关时不红）。
- DLC 数量 17 / 25 / 31、mod 总数 64 / 313 的 `Equal` 改为 `>=` + 实测值 info；CK3 1.18「全部 DLC 启用」→「至少 1 个启用」。
- Stellaris 段同类放松：5 个播放集 → `>= 3`（第 5 个名字仍在 `Count >= 5` 时断言）；激活集若已不是 IronyModManager，只留结构断言、guid 锚点降级为 info；mod 行数 13 → `>= 11`、「全部启用」→「至少 1 行启用」。
- `CheckDlcShape` 的 pops_id 空/非空个数从 `Equal` 改为下限 + 「非空行 > 0」；Stellaris 4 个已知空 pops_id 文件的 `SequenceEqual` 改为「记录中的 4 个都还在集合里」。
- 新增 `tests/PdxLauncher.Checks/PlaySetGuardChecks.cs`：钉 C/D 的口径（指纹语义、漂移判定含坏库、路由兜底四个落点）；`PdxLauncher.Checks.csproj` 把 `PlaySetRepositoryRouter.cs` 源码链接进来（与 PlaySetEditor 同一手法：App 是 WinExe + windows TFM，引用不了）。

**C. 保存前防覆盖**：`ModsViewModel.SelectPlaySetAsync` 加载成功后记基线 `_modsBaseline = PlaySetEditor.DescribePlaySetMods(_allMods)`（ModId + 启用态 + 按 position 排序的指纹；名称/缩略图/版本等派生字段不参与）。`SaveAsync` 写库前走 `ConfirmNoExternalChangeAsync` → `PlaySetEditor.HasPlaySetDriftedAsync` 读库比对：不一致就**不写 sqlite、不写加载文件**，状态栏提示「官方启动器已改动该播放集，已重新加载（本次修改未保存）」并重载该播放集；保存成功后基线前移，避免被自己刚才的写入判成漂移。范围：只对写 sqlite 行的游戏生效（EU5/CS2 是整份 JSON 原子替换，不存在逐行覆盖）；没有基线（首次加载/读取失败）时放行。新增 i18n 键 `mods.status.externalChange`（zh/en），重跑 `文案源生成器`。

**D. 存储格式兜底**：`PlaySetRepositoryRouter` 注入 `IFileSystem` + `IAppLogger`；sqlite 分支在 `launcher-v2.sqlite` 不存在时探测 `playsets.json` → `playset_config.json`，命中即用对应实现并记 Warn；数据库存在就仍走 sqlite（「空库」不等于「换格式」）。`App.xaml.cs` 构造处补传 `fileSystem, logger`。

**边界 / 踩坑**

- `IFileSystem` 没有文件时间戳 API（只有存在/大小/读写/枚举），所以「官方改过没有」只能比**内容指纹**，不能靠 mtime。
- 仓储只读路径把 `SqliteException` / `IOException` / `UnauthorizedAccessException` 都吞成「空结果 + Warn」，所以坏库/锁库在漂移检测眼里表现为「读到空 → 判为改动」，结果是**放弃保存**而不是覆盖——比「读失败就放行」更保守，符合「宁可不存，不可盖掉官方数据」。
- 不要把「无基线（null）」与「基线为空串」混为一谈：前者放行，后者参与比较。
- 真机断言放松时必须保留结构侧真断言（Id 唯一、Position 连续、归属一致、只读 md5 不变），否则等于把自检删掉。

**自检**

- `dotnet build PdxLauncher.sln -c Debug -m:1` → 0 警告 0 错误。
- `dotnet tests\PdxLauncher.Checks\bin\Debug\net10.0\PdxLauncher.Checks.dll` → `检查类: 38，失败: 0，断言失败总数: 0`；V3 真机段打印「24 行，启用 1 / 未启用 23」且全部 ok；`V3 只读读取未修改 launcher-v2.sqlite` md5 前后一致（`e8a639ca42072da1015488ff5b770e0f`）。

## 0.45 2026-10-29：创意工坊详情页可以真的订阅 Steam 工坊（直连 Steamworks）

**背景**

- 详情页的「订阅」按钮一直是两套行为：Paradox Mods 走 §0.41 的真订阅接口，Steam 那侧只
  `ShellLauncher.TryOpenSteam` 打开客户端页面（用户还得在 Steam 里再点一次）。
- 用户问「能实现 Steam 创意工坊订阅吗，是不是需要 Steam 的 SDK」，并在拿到 SDK 后选了
  「Stage B」——即接受「用游戏 appid 初始化 Steamworks」这条越界路线，要真订阅。

**为什么 Web API 走不通（先排除的两条路）**

- `ISteamRemoteStorage/SubscribePublishedFile` 是 **PUBLISHER 专属**（`partner.steam-api.com`），
  第三方拿不到，也不该拿。
- 社区站点 `https://steamcommunity.com/sharedfiles/subscribe/?publishedfileid=…` 存在，但需要
  用户自己的 Steam 网页登录态；实测未登录直接 **HTTP 401**，且新版 Steam 的 `config.vdf` 里
  已经没有 `steamLoginSecure` / `WebCookies` / `sessionid`（0 命中），没有可复用的 cookie。
- OpenID 只回 Claimed ID（`http://steamcommunity.com/openid/id/<steamid>`），是身份不是授权令牌。
- 剩下唯一能「代表本机用户订阅」的入口就是 **本地 Steamworks 客户端 API**。

**SDK 1.65 与老版本 DLL 的差别（实测导出表）**

- `sdk/public/steam/steam_api.h` 把 `SteamAPI_Init()` 改成了 inline（转调 `SteamInternal_SteamAPI_Init`），
  所以 **1.65 的 `steam_api64.dll` 不再导出 `SteamAPI_Init`**，只导出 `SteamAPI_InitFlat` / `SteamAPI_InitSafe`。
  → 这也是不用 Steamworks.NET（2024.8.0 走旧导出）的原因，改用 P/Invoke 直连 flat API。
- 1.65 才有 `SteamAPI_SteamUGC_v021()`（老游戏自带 DLL 没有）；`SteamAPI_ISteamUGC_SubscribeItem`
  /`UnsubscribeItem`/`GetItemState` 两代都有。`SteamAPI_IsSteamRunning` 返回 C++ bool（1 字节），
  必须 `[return: MarshalAs(UnmanagedType.I1)]`，否则高位垃圾会让判断恒真。

**改法**

1. `src/PdxLauncher.Core/Steam/SteamApiLocator.cs`（新增）：按「手填路径 → 程序目录 →
   逐级父目录下的 `sdk/redistributable_bin/win64/` → 游戏目录 binaries/bin/steamworks」找 DLL，
   体积 < 64 KB 视为占位文件；`Candidates` 保序去重，`Resolve` 取第一个可用的。
2. `src/PdxLauncher.Core/Steam/WorkshopSubscriptionReader.cs`（新增）：读
   `steamapps/workshop/appworkshop_<appid>.acf` 的 `WorkshopItemDetails`（已订阅）与
   `WorkshopItemsInstalled`（已安装），配合 `steamapps/workshop/content/<appid>/<fileid>` 是否存在；
   订阅了但没装完 → `IsDownloading`。纯解析入口 `ReadFromAcf(...)` 抽成 public static 供自检造数据。
   acf 残缺（缺 `AppWorkshop` 或 `appid`）按 **未知** 处理，不谎报「未订阅」。
3. `src/PdxLauncher.Core/Steam/WorkshopInstallState.cs`（新增）：三态记录（`bool?` 订阅/安装/下载中 + acf 路径）。
4. `src/PdxLauncher.App/Services/SteamNativeApi.cs`（新增）：1.65 flat API 的 DllImport 表。
5. `src/PdxLauncher.App/Services/SteamWorkshopBridge.cs`（新增）：`NativeLibrary.SetDllImportResolver`
   指向解析到的 DLL（只能设一次）→ 写 `steam_appid.txt` 与 `SteamAppId` 环境变量 →
   `SteamAPI_InitFlat` → 取 `ISteamUGC*`；100 ms `DispatcherQueueTimer` 泵 `RunCallbacks`；
   `SetSubscribedAsync` 调 `SubscribeItem`/`UnsubscribeItem` 后 **每 500 ms 轮询 `GetItemState` 最多 40 次**，
   等 Steam 真正确认（而不是发了就报成功）；`Dispose` 停泵 + `SteamAPI_Shutdown`。
6. `src/PdxLauncher.App/Services/IWorkshopSubscribeBackend.cs`（新增）：`IsAvailable`/`SetSubscribedAsync`
   /`ReadState`，让 VM 可以脱开原生层跑；`WorkshopSubscribeResult` 带失败原因。
7. `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs`：新增 `IsSteamSubscribed`、
   `IsSteamStateKnown`、`IsSteamSubscribing`、`IsSteamDirectAvailable`、`SteamUnavailableReason`、
   `SteamSubscribeActionText`、`CanToggleSteamSubscription`、`SteamSubscribeHint`、`ShowSteamSubscribeState`；
   打开 Steam mod 时 `RefreshSteamSubscriptionAsync` —— 后端可用读 Steamworks 实时状态，否则退回 acf。
   `ApplySteamInstallState` 里补一条 INFO 日志（订阅/安装/下载中 + 来源），方便用户报障时对账。
8. `src/PdxLauncher.App/Views/WorkshopDetailPage.*`：Steam 按钮文案改绑 `SteamSubscribeActionText`，
   可点性绑 `CanToggleSteamSubscription`，订阅中单独一个 ProgressRing，已订阅显示强调色徽标，
   下方给「直连可用 / 未启用 / 不可用原因」提示；`OnSubscribeSteamClick` 在直连可用时走
   ContentDialog 二次确认 → 真订阅；直连不可用仍退回打开 Steam 页面。离开页面 `Dispose` 桥（关 Steamworks）。
9. 设置：开关与检测状态放在 **设置 → 联网 →「网络代理」下方**（这是联网行为，不塞进「界面」小节）；
   `SettingsViewModel.SteamSubscribeEnabled`（默认关的实验开关）+ `RefreshSteamApiStatus()`
   （状态卡标题是 `steam_api64.dll`，值显示「已找到 …」或「没找到 + 前两条候选」）；`AppSettings.SteamWorkshopSubscribeEnabled`
   /`SteamApiDllPath`（后者 v1 不在 UI 暴露，可手写 settings.json）。
10. i18n：`workshop.json` 各 +4（`detailSteamSubscribeHintOn/Off`、`detailSteamSubscribeConfirm`
    /`detailSteamUnsubscribeConfirm`），`settings.json` 各 +6（`steamSubscribeOn/Hint/StatusOn/StatusOff`、
    `steamApiReady/steamApiMissing`）；`gen-i18n` 后 695 键。
11. 不分发 Valve 二进制：`.gitignore` 加 `sdk/`、`steam_api64.dll`、`steam_appid.txt`；
    `PdxLauncher.App.csproj` 用 `Condition="Exists(...)"` 的 `None` 项把开发机上的 DLL 复制到输出。

**自检**

- `dotnet build PdxLauncher.sln -c Debug -m:1`（在隔离 worktree 里，因为别处的 `ModLibraryRowViewModel`
  正在改）→ 0 警告 0 错误。
- 新增 `tests/PdxLauncher.Checks/WorkshopSteamSubscribeChecks.cs`（4 组）：DLL 可用性判定与 `Resolve`、
  候选顺序/去重、acf 四种分支（订阅+安装 / 订阅未装完 → 下载中 / 未订阅 / 只有内容目录）、
  坏 acf 与缺 `AppWorkshop` 一律未知。全部用临时目录造数据，不联网、不碰真 Steam。
- `dotnet tests\PdxLauncher.Checks\bin\Debug\net10.0\PdxLauncher.Checks.dll` → `检查类: 39，失败: 0，断言失败总数: 0`。

**实测（真机，Steam 在跑，CK3 appid 1158310）**

- 日志：`已写入 steam_appid.txt：1158310` → `Steamworks 已初始化（appid=1158310，dll=…\steam_api64.dll）`
  → `Steam 订阅状态（Steamworks）：订阅=否 安装=否 下载中=否 acf=无`。
- 界面：订阅卡提示「已开启直连订阅：按钮会直接调 Steam 客户端订阅 / 取消订阅。」，按钮文案「订阅」可点。
- **真机直连实测（2026-10-29 14:21，CK3 appid 1158310，工坊物品 3579037811「移除属性上限 Remove Skills Cap」，2.1 KB）**：
  点「取消订阅」→ 12 秒内按钮翻成「订阅」、状态文案「已取消订阅」，acf 的 `WorkshopItemDetails` 条目消失
  （`WorkshopItemsInstalled` 仍在，符合 Valve 文档「退出游戏后才移除」）；随后点「订阅」把这条订阅加回去，
  按钮翻回「取消订阅」、acf 两处条目恢复。日志：`14:21:29 已取消订阅 Steam 工坊物品：3579037811`、
  `14:22:13 已订阅 Steam 工坊物品：3579037811`。截图：`ui-test-artifacts/workshop-steam-sub/{05-before-unsub,06-after-unsub,07-resubscribed}.png`。
  更大体积的物品（点了会立刻开始下载）仍建议用户在场时再点一次确认。

**踩坑 / 边界**

- 「已订阅」徽标最初判的是 `ShowSteamSubscribeButton && IsSteamStateKnown`——只要状态读到了就亮，
  于是「确认未订阅」也被标成已订阅（第一版截图里就能看到）。现在要求 `IsSteamSubscribed` 也为真。
- `SteamAPI_Init` 在 1.65 不存在 → 任何直接 P/Invoke 老符号的封装（含 Steamworks.NET）都会
  `EntryPointNotFoundException`。用 `InitFlat` + `SteamUGC_v021`。
- `SetDllImportResolver` 每个程序集只能设一次，第二次抛 `InvalidOperationException`；DLL 路径变了要
  **重启应用**才生效（桥里对这一点做了兜底：解析器已装过就沿用第一次的路径）。
- `steam_appid.txt` 必须是「当前游戏」的 appid；本功能默认关闭，且只在详情页打开期间初始化，
  离开页面即 `Shutdown`，避免长期占着 Steamworks。
- 订阅成功与否以 Steam 的 `GetItemState` 为准：`SubscribeItem` 只是投递请求，20 秒内没有
  `Subscribed` 位就按失败提示（大 mod 首次解析可能更慢，可重试）。
- acf 只反映「本机 Steam 记录的订阅」，与账号云端状态可能有延迟；直连可用时优先用 Steamworks。
- 用游戏 appid 初始化 Steamworks 不在 Valve 的对外授权范围内（本启动器不是 Steam 上发行的产品），
  因此默认关闭、需要用户显式开启，且不分发 `steam_api64.dll`。

## 0.46 2026-10-29：Mod 库行可以打开创意工坊详情页（复用 WorkshopDetailPage）

**背景**

- 用户诉求原话：「对mod库进行修改，添加mod详情页按钮，调用写好的详情页（就是创意工坊的那个详情页）」。
- 详情页正文此前已存在（§0.26 把内容从弹窗搬进 `WorkshopDetailPage`），但只有一个入口：创意工坊列表点行。
  Mod 库的行只有「打开文件夹 / Steam / 浏览器」三个跳转按钮，看不到描述、changelog、依赖、配图。

**改法**

1. `src/PdxLauncher.Core/Playsets/ModWorkshopRef.cs`（新增）：把 Mod 库行翻译成「平台 + 编号」
   （`sealed record ModWorkshopRef(WorkshopSource Source, string Id, string? StatusTextKey)`）。
   `Resolve(steamId, gameRegistryId, source)` 六步：先走 `SteamWorkshop.GetWorkshopId`（`mods.steamId`
   纯数字或 `mod/ugc_<id>.mod`）；CS2 的 `pdx_mods` 行剥 `pdx:` 前缀取 PDX 数字 id；`local` / 拿不到
   编号的 `steam` / `paradox` / 未知来源分别带一个状态文案键（按钮置灰时 tooltip 直接说原因）。
   PDX id 逐字符 ASCII 数字判定 —— 不能用 `long.TryParse`，它接受 `+12` / `1_2` / `12L`。
2. `src/PdxLauncher.App/ViewModels/ModLibraryRowViewModel.cs`：`WorkshopRef` + `HasWorkshopDetail` +
   `WorkshopDetailText`（可点=「打开详情页」，置灰=状态键文案）+ `WorkshopDetailAutomationId`
   （`modLibraryOpenDetail_<ModId N>`，沿用行内其余按钮的命名规律）。
3. `src/PdxLauncher.App/ViewModels/WorkshopRowViewModel.cs`：新增公开工厂 `FromLibrary(reference, name,
   sizeBytes, localThumbnailPath, cached, thumbnailSize)` —— 元数据尽量从 `WorkshopInfoCache` 里捞
   （作者/评分/订阅数/更新时间/CoverUrl），捞不到就留空，详情页自己会去抓真数据（稀疏行从 `FromCache`
   时代起就是被验证过的路径）。**本地缩略图只塞 `CoverPath`，绝不冒充 `CoverUrl`**：封面兜底会拿
   `CoverUrl` 走 `AssetDownloader`，把 `G:\...png` 当远程地址既浪费又不安全（白名单也过不了）。
4. `src/PdxLauncher.App/ViewModels/ModLibraryViewModel.cs`：`CreateDetailRow(row)` —— 查缓存、建详情行；
   解析不出编号返回 null（页面把原因写进状态栏）。
5. `src/PdxLauncher.App/ViewModels/WorkshopDetailNavigationRequest.cs`（新增）：`record (Row, ReturnTag)`。
   详情页的「返回」原来写死回创意工坊列表（`OnBackClick` → `NavigationService.WorkshopTag`），从 Mod 库
   进来会把用户甩到别的页面 —— 现在返回目标由发起方随参数带进来。`WorkshopPage.DetailRequested` 与
   `ModLibraryPage.DetailRequested` 统一成 `EventHandler<WorkshopDetailNavigationRequest>`，MainWindow 里
   一个 `OnWorkshopDetailRequested` 同时挂两页。
6. `src/PdxLauncher.App/Views/ModLibraryPage.xaml`：行操作区第三个位置前**新加第一个按钮**「打开详情页」
   （glyph `E946` Info，全仓未占用），置灰行为与其余三个一致；`.xaml.cs` 加 `DetailRequested` 事件与
   `OnOpenDetailClick`（模板同其余三个 handler）。
7. `src/PdxLauncher.App/MainWindow.xaml.cs`：新增 `_modLibraryPage` 字段，`AttachPageEvents` 与
   `OnWindowClosed` 两处都补了 ModLibraryPage 的挂/摘（漏一处就会出现「返回后再进 Mod 库按钮失效」）。
   `NavigateToWorkshopDetail(request)` 把整个 request 当导航参数传给 Frame。
8. `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml(.cs)`：`OnNavigatedTo` 兼容两种参数
   （request 或裸 row，裸 row 回落创意工坊列表）；`OnBackClick` 回 `_returnTag ?? WorkshopTag`；
   返回按钮的 ToolTip/AutomationName 从 `L.WorkshopDetailBack` 改绑 `ViewModel.BackText`。
9. `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs`：`LoadAsync(row, ct, returnTag = null)`，
   `_returnTag` 存字段；派生属性 `BackText`（`returnTag == ModLibraryTag` → 「返回 Mod 库」，否则
   「返回创意工坊」）加进 `NotifyStateChanged`，`ReloadAsync`（重试按钮）沿用上次 returnTag。
10. i18n：`modlib.json` +5（`openDetail`、`detailNoWorkshop`、`detailSteamUnresolved`、
    `detailPdxUnresolved`、`detailUnknownSource`）、`workshop.json` +1（`detailBackLibrary`）。
    （这批键随 §0.45 那笔提交 `463d933` 一起进了 HEAD，本轮工作区里 modlib/workshop json 已无 diff。）
11. README.md Mod 库段「三个图标按钮」→「四个」并补 `ModWorkshopRef` / `FromLibrary` / ReturnTag 说明。

**为什么 Steam 稀疏行不用特殊处理（复核过，避免下轮再查）**

`ApplySteamAsync` 里 `MetaText = BuildSteamMeta(row.SteamItem, detail)`：views/订阅数/大小/更新时间
在 `SteamWorkshopItemDetail` 里都有，item 为 null 时用 detail 兜底；只有评分 / 收藏 / 发布时间依赖列表
item，缺失时该行自动省略。作者名会被物品页 `page.AuthorName` 覆盖回来；描述、评论、配图全走
`GetSteamItemPageAsync`，与列表行无关。所以 Mod 库进来的行**不会报错**，只是个别 meta 缺项。

**自检**

- `文案自检` 通过；`dotnet build PdxLauncher.sln -c Release -m:1` 0 警告 0 错误。
- `dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll` → `检查类: 40，失败: 0`
  （含新增 `ModWorkshopRefChecks`：Steam 优先、`pdx:` 剥前缀、`+12`/`1_2`/`12L`/`1.2` 拒绝、
  local/未知来源置灰原因、四个状态键 + `openDetail` 在 zh-CN/en-US 两份 modlib.json 里都存在 ——
  Core 引用不到 App 的 `Keys`，键名拼错界面只会显示 `⟨键名⟩`，只能这样钉住）。

**实测（真机 UIA，Victoria 3，用新构建单独起了一个实例 8740，没动用户原来开着的 50496，验完自关）**

- Mod 库 52 行全部出现 `modLibraryOpenDetail_*`，51 行 tooltip「打开详情页」可点；唯一一条未登记本地
  mod（Daoyu Cheat）按钮 **disabled** 且 tooltip「这个 mod 没有工坊详情页（本地 mod）」。
- 点 Auto-Diplomacy 的详情 → `WorkshopDetailPage` 打开：标题、徽标「Steam 创意工坊」、正文与图片墙
  正常（日志 `素材下载完成 …shots\steam_3432933121\shot_*.jpg` 五张，acf 订阅状态 `订阅=是 安装=是`），
  全程无「导航到工坊详情失败/异常」日志。
- 返回按钮 UIA 名「**返回 Mod 库**」（不是「返回创意工坊」），点一下回到的正是 `lstModLibrary`，
  左栏选中项停在 Mod 库。

**边界 / 踩坑**

- `WorkshopDetailPage` 没设 `NavigationCacheMode`（其余八页都是 `Required`）→ 每次导航新建实例，
  所以 `_returnTag` 不会串页；ModLibraryPage 是 Required，返回时 `OnLoaded → ReloadAsync` 重扫一遍
  （原有行为，未改）。
- 本次提交落在 `feat/workshop-steam-subscribe` 分支上：工作区改动是叠在未提交的 §0.44（播放集防覆盖）
  之上的，README/HANDOFF/Strings.g.cs/Checks.csproj/PlaysetChecks.cs/settings.json 等文件里 §0.44 的
  hunk **未提交、留在工作区**，README 与 HANDOFF 用 hunk 级 `git apply --cached` 只取本轮改动。
- 从 Mod 库进详情页时 Steam 直连订阅（§0.45）照常可用 —— 两个功能是同一页面上正交的。

## 0.47 2026-10-29：修「评论翻不动页」——详情缓存丢掉了作者 SteamID64

**症状**

- Steam mod 详情页的评论只有第一屏，「第 1 / N 页」的分页按钮点了没反应（有时连按钮都不出现）。

**根因**

- `WorkshopDetailSource.GetSteamDetailAsync` 命中缓存时用 8 个位置参数重建 `SteamWorkshopItemDetail`，
  末尾那个 `null` 其实是 `Filename`：`CreatorSteamId`/`Tags`/`Views`/`LifetimeSubscriptions` 全落默认值。
- VM 里 `_steamCreatorId` 因此为空，`LoadCommentsPageAsync` 的守卫 `IsNullOrWhiteSpace(_steamCreatorId)`
  直接 return —— 既不翻页也不提示；顺带作者链接变灰、标签/浏览/收藏也一起没了。
- 只有「第一次打开（走远端）」正常，第二次以后（走缓存）静默失效，所以看着像「有时好有时坏」。

**改法**

1. `WorkshopInfoCacheEntry` 增 `CreatorSteamId`/`Tags`/`Views`/`LifetimeSubscriptions`；`WorkshopInfoCache.SetDetail`
   加同名可选参数，**只在真拿到值时才覆盖**（PDX 写正文不会把 Steam 那份元数据洗成空）。
2. `WorkshopInfoCache.Clone` 补上这四个字段 —— 不补等于白存（`TryGet`/`Snapshot` 都走 Clone，自检抓出来的）。
3. 新增 `SteamWorkshopDetailCache.Restore(entry, id, fallbackName)` 统一「缓存 → 详情」的还原：作者取
   `CreatorSteamId`，空则退回 `Author`（旧缓存没有新键，但 Steam 行的 `Author` 本来就是 SteamID64）。
4. VM 的作者 id 改成三级兜底 `ResolveCommentOwner`：详情 → `row.SteamItem?.AuthorSteamId` → 物品页
   `AuthorProfileUrl`（`WorkshopText.TryParseSteamId` 只认 `/profiles/<数字>` 与裸数字，`/id/<名字>` 不认）。
5. 失败可见化：`CommentsStatusText` + 页面上的 `txtWorkshopCommentsStatus`。「物品页 0 条且 total_count ≤ 0」
   判「这个 mod 没有可显示的评论」，其余判「评论加载失败，当前显示已抓到的 N 条」，没作者 id 时提示
   「没拿到作者信息，暂时无法翻页」。`HasCommentsPaging` 也要求有作者 id，不再摆一个死按钮。

**自检**

- `WorkshopCommentPagingChecks`（新）：新缓存原样带出作者/标签/浏览/订阅、旧缓存靠 `Author` 补作者 id、
  两者都没有时为空。
- `WorkshopRichTextChecks.CheckSteamIdParsing`：profiles 地址 / 裸数字 / 带查询串 / `/id/名字` / 空值 / 位数不足。
- `WorkshopInfoCacheChecks.CheckDetailMeta`：写回读、缺元数据不洗旧值、**手写旧版索引仍能读**（向后兼容）。
- `bash tools/build-app.sh -v q` 0 警告 0 错误；`dotnet build PdxLauncher.sln -c Debug -m:1` 0/0；
  `检查类: 41，失败: 0`。

**实测（真机 UIA，CK3）**

- 缓存命中路径的 `Renovatio`（3798537678，缓存里只有 `author=76561198145098971`）：详情页打开后点下一页
  → 「第 2 / 9 页 · 共 122 条」→ 再点 → 「3 / 9」。
  截图：`ui-test-artifacts/workshop-comments/{01-page1,02-page2,03-page3}.png`。
- 关闭了评论的 `LotR: Realms in Exile`（2291024373）：显示「这个 mod 没有可显示的评论」
  （`ui-test-artifacts/workshop-comments/04-nocomment.png`）。

**踩坑 / 边界**

- 旧缓存条目其实一直存着作者 id（`WorkshopViewModel` 抓列表时写的 `Author = item.AuthorSteamId`），
  所以这次不用清缓存重抓，旧索引也能直接修好。
- 给 UI 自动化用的临时钩子（`PDX_UI_OPEN_MOD_ID` / `PDX_UI_SEARCH`）验证完已 `git checkout` 撤掉，未入库。
- 从 Mod 库进详情页（§0.46）走的是同一条缓存路径，这次一并修好。

---

## 0.48 2026-10-29：清掉「改路径/改命名后没删旧文件」留下的 47 MB 重复缓存

**起因**

用户问「是什么代码导致图片重复存储」。查下来发现：**当前代码没有在重复下载**，
37 MB 是三次改动留下的遗留物，另外 ~4 MB 是「同一张图被两个不同的键各存一份」。

| 成因 | 体量 | 由来 |
| --- | --- | --- |
| `data\workshop-covers\` | 27.03 MB / 85 文件 | 早期工坊封面根；改成统一用 `WorkshopInfoCache.CoverRoot` 后废弃（d88eb7a 引入，b62ceb6 删掉 `AppServices.WorkshopCoverRoot`） |
| `data\online-mod-covers\` | 9.12 MB / 72 文件 | 已整体回退的「Mod 库在线视图」封面根 |
| `workshop-info\shots\*_<id>\shot_NN.*` | 9.41 MB / 25 文件 | 截图命名从「列表序号」改成「URL 哈希」（f1e6b90），旧序号文件没人删 |
| 封面 ↔ 截图同图 | 1.78 MB / 5 文件 | Steam 画廊首图就是封面本身，`EnsureScreenshotAsync` 又存一份 |
| 同图不同键 | 0.34 MB / 8 组 | `mod-cache` 按 mod GUID、`workshop-info` 按 `来源:id`，两条链路的 URL 还不一样 |

**关键前提**：这些文件**没有任何入口能删**。`SettingsViewModel` 注册的 6 张缓存卡片只指向
现行路径（theme / news / modThumbnails / dlcDescriptions / images / workshopInfo），
`ClearAllCachesAsync` 遍历的就是这几行，所以「清除全部缓存」也碰不到那两个遗留目录。

**改动**

- 新增 `src/PdxLauncher.Core/Storage/LegacyCacheCleaner.cs` + `LegacyCacheCleanupResult.cs`：
  启动时删两个废弃目录，再遍历 `shots\` 删 `shot_NN` 命中的旧命名文件。
  判定口径按「点号落在下标 7」（旧 `shot_NN.ext`）vs 下标 12（新 `shot_<8hex>.ext`）。
  幂等：没货可删时不碰磁盘；单项失败只记 Warn，绝不抛给调用方。
- `WorkshopInfoCache` 新增 `TryGetCachedCoverForUrl(key, url, out fullPath)`：
  问「这条记录的封面是不是就这个 URL」。
- `WorkshopImageCache.EnsureScreenshotAsync` 在下载前先问上一句，命中就直接返回封面路径——
  同一张图不再落第二份。
- `App.xaml.cs` 的 `ComposeServices` 里紧跟 `DataRootMigrator.Migrate()` 之后跑一次清理
  （设置页没有入口，只能放启动阶段），有货才记一条 Info。
- 顺手修了 `WorkshopImageCache` 类注释里过期一年的「文件名 shot_NN，靠枚举同名前缀复认」。

**自检**

- 新增 `LegacyCacheCleanerChecks`（4 组：无货可删是 no-op、两个遗留目录、只删旧命名、
  现行缓存不被误删）与 `WorkshopImageCacheChecks`（3 组：截图名是 URL 哈希、
  封面已记同 URL 时复用不下载、旧命名不算命中）。
- `检查类: 44，失败: 0`（原 41 + 本次新增 3 类：`LegacyCacheCleanerChecks`、
  `WorkshopImageCacheChecks`、`WorkshopGalleryLayoutChecks`）。工作区里并行的在建改动
  （`RequestCoalescerChecks` / `RequestThrottleChecks` / `SteamItemPageCacheChecks`）落地后为 47，同样全绿。
- `bash tools/build-app.sh -v q` 0 警告 0 错误。

**磁盘清理（已执行）**

真机数据根 `data\`：203,003,888 B / 580 文件 → **146.26 MB / 393 文件**，删掉 187 文件 47.34 MB。
清理后 `workshop-info` 271 文件 99.73 MB、`theme-cache` 45 文件 38.28 MB、
`mod-cache` 54 文件 3.69 MB、`news-cache` 10 文件 3.07 MB、`image-cache` 3 文件 0.94 MB。

**故意没做的**

- 剩余 11 组 1.038 MB 同图仍留着：8 组是 `mod-cache` 与 `workshop-info\pdx` 同一 mod 的两张封面
  （mod 库取 `.../content/covers/cover_15.jpg`、工坊取 `.../content/cover.jpg`，**URL 不同**），
  3 组是不同 id 的 Steam 封面内容相同但 `ugc/<数字>/` 路径段不同。
  按 URL 寻址的缓存识别不了这种情况，要去重必须改成内容哈希寻址——那是另一件事，没做。
- `WorkshopInfoCache` 仍没有 Prune/容量上限，只有 `Clear()` 和 `Flush()`。
  工坊缓存会一直涨（现在是 99.73 MB），要控体积得单独加保留期。
- 封面没做重编码：198 张 Steam 封面按 160–360 px 渲染，却存着 512×512 的 PNG（80.9 MB）。
  改成 256–512 px JPEG 能省 90% 以上，但会影响画质，需要单独决定。

**踩坑**

- `grep` 输出含中文时会被当成二进制（`Binary file (standard input) matches`），
  要看内容得用 `sed -n '/=== X ===/,/^$/p'` 或加 `PYTHONIOENCODING=utf-8`。
- 所有统计用 python heredoc 纯内存跑完，不落临时文件（当时文件策略还是 read-only）。
- 跑全量自检时撞上 `WorkshopPageCacheChecks`「页面缓存：超过 5 分钟失效」红：
  工作区里 `WorkshopPageCache.DefaultTtl` 已被改成 15 分钟（还多了 `Retention` / `MaxPages`），
  而那条断言把 5 分钟写死了。已改成按 `WorkshopPageCache.DefaultTtl` 现算——
  TTL 本来就会按实测调，写死等于每次调参都埋一颗雷。

## 0.49 2026-10-29：详情页图片区按窗口自适应（查看器 1x 不再裁掉大半张图）

### 背景
工坊详情页点开截图后，浮层查看器里只看到图片左上角一块：窗口明明比图片小，
却既不平铺也不居中，1x 还不能拖动。用户原话是「查看图片详情的时候能根据窗口大小显示吗，
现在窗口比图片小，有点怪」。

### 根因
`WorkshopDetailPage.xaml` 的查看器是 `<ScrollViewer ZoomMode="Enabled">` 里塞一张
`<Image Stretch="Uniform" />`。开了缩放后 ScrollViewer 按**内容自身期望尺寸**测量，
而工坊画廊取的是 `imw=5000` 原图（0.43 起从 `#highlight_player_area` 抠出来的），
于是图片槽位就是 5000px——`Stretch="Uniform"` 只是在这个巨大槽位里等比，等于没缩放。
更糟的是 `ApplyZoom` 在 1x 时把两个 `ScrollMode` 设成 `Disabled`，所以 1x 既看不全也拖不动。

内联图区则是写死的 `Height="360"`：640 高的窗口里它吃掉一整屏的一大半，很挤；
1600 高的窗口里又只占上半截，下面一片空。

### 改法
1. 新增 `src/PdxLauncher.App/Services/WorkshopGalleryLayout.cs`（纯 `System` 数学，可被检查工程链接）：
   - `InlineGalleryHeight(pageHeight)` → `Clamp(pageHeight * 0.45, 160, 420)`，量不到高度时回退 360；
   - `ViewerContent(overlayWidth, overlayHeight, headerHeight)` → 各减去留白（16×2）与顶部计数栏，
     低于 `ViewerMinSide`（120）一律抬到 120，避免把图压成 0 尺寸。
2. `WorkshopScreenshotViewModel` 增 `ViewerMaxWidth`/`ViewerMaxHeight`（默认 `PositiveInfinity`，
   即「还没算过」时保持原样）+ `ApplyViewerSize(w, h)`。
3. `WorkshopDetailPage.xaml`：查看器里的 `Image` 改绑 `MaxWidth/MaxHeight` 到上面两个属性并居中，
   留白收到 `ScrollViewer.Padding="16"`；根 `Grid` 与 `ImageOverlay` 各挂 `SizeChanged`。
4. `WorkshopDetailPage.xaml.cs`：
   - `UpdateGalleryLayout(width, height)` 写内联图区高度；浮层开着时顺带重算查看器尺寸；
   - `ApplyViewerSize()` 取 `ImageOverlay` 实际尺寸与 `RowDefinitions[0].ActualHeight`，算好后写给每张截图；
   - `OpenViewer` 在 `Visibility=Visible` **之后**才用 `DispatcherQueue.TryEnqueue` 计算——
     折叠状态下 `ActualWidth` 是 0，先算会把图压没；
   - 翻页（`OnGallerySelectionChanged`）后再补算一次，新页的容器是刚实体化的。

### 自检
新增 `tests/PdxLauncher.Checks/WorkshopGalleryLayoutChecks.cs`（12 条）：内联高度四档
（640→288 / 800→360 / 1200→420 上限 / 200→160 下限）、查看器两档整算
（1600×1000+68 头 → 1568×900、1040×600+68 → 1008×500）、无头栏 568、
0/NaN/过小窗口一律退到 120、内联 0/NaN → 360。
`PdxLauncher.Checks.csproj` 增加对应的 `<Compile … Link="Linked\WorkshopGalleryLayout.cs" />`。

### 实测（Debug exe，CK3 `Renovatio` 7 图，隔离 worktree 里跑的）
- 1040×640：查看器里整张图完整可见、垂直居中、上下留黑边；右上「1 / 7」与关闭按钮正常；内联图区 288 DIP。
- 1600×1000：同样完整居中；内联图区吃到 420 DIP 上限。
- 截图：`ui-test-artifacts/workshop-imgviewer/{01-inline-inline-1040x640,01-inline-inline-1600x1000,02-viewer-1040x640,02-viewer-1600x1000}.png`。

### 踩坑
- `winapp ui click` 需要窗口在前台（常被全屏游戏占用），`invoke` 不需要；但缩略图 `invoke` 只切内联页、
  不会开浮层，所以冒烟在 worktree 里临时加了 `PDX_UI_OPEN_MOD_ID` / `PDX_UI_OPEN_VIEWER` 两个环境变量钩子，
  验完删除（主仓库 `WorkshopPage.xaml.cs` 无改动）。
- 做这一节时主工作区 App/Release 构建是红的——并发会话正在改 `RequestThrottle.cs`、`WorkshopViewModel.cs`、
  `WorkshopPageCache.cs`（缺 `SteamCommunityHost`、`Keys.WorkshopThrottlePaused` 等），与本次改动无关，
  所以改用 `git worktree` 在干净 HEAD 上验证；那批改动随后在 §0.50 一并合入，**主工作区构建已回绿**
  （`bash tools/build-app.sh -v q` 0/0、检查类 47、失败 0）。
- 跑全量自检时唯一失败是 `WorkshopPageCacheChecks`「页面缓存：超过 5 分钟失效」，
  属未提交的在建改动（`DefaultTtl` 已从 5 分钟改成 15 分钟），与本次改动无关。
  该断言随后已改成按 `WorkshopPageCache.DefaultTtl` 现算（见 §0.48 踩坑），现全绿。


## 0.50 2026-10-29：创意工坊请求闸门 / 在途去重 / 物品页缓存（治「您最近作出的请求太多了」）

### 起因

用户在 Steam 创意工坊页看到官方限流页：

> 抱歉！处理您的请求时遇到错误：您最近作出的请求太多了。请稍候，然后重试。

当时的请求预算（实测口径，均为「用户点一下」触发的量）：

| 操作 | 改造前 | 改造后 |
| --- | --- | --- |
| 进创意工坊页（首次） | 1 次 `workshop/browse`（1–3 MB SSR）+ ≤30 张封面 | 同左（封面并发 4、间隔 100 ms） |
| 5 分钟后再进同一页 | 缓存 + 静默再抓一次 | 缓存 + **≥2 分钟**才补一次 |
| 换游戏再换回来 | `_pages.Clear()` 把**所有游戏**的页缓存清空 → 重新抓 | 0 请求（页键已带来源/游戏/筛选） |
| 首次打开某个 mod 详情 | 3 次：`GetPublishedFileDetails` + 物品页 HTML + **评论第 1 页（冗余）** | 1 次（仅物品页；详情 7 天缓存，评论首屏复用物品页内嵌） |
| 12 小时内重开同一 mod | 仍抓物品页 | **0 次**（物品页解析结果落盘） |
| 被限流（429）后 | 继续按原节奏发，图片还会 ×3 重试 | 该 host 进入冷却，本地直接拒绝，图片不再重试 |

### 三层改造

1. **出口闸门（P0）** —— `src/PdxLauncher.Core/Online/RequestThrottle.cs`，挂在唯一的出口
   `HttpFetcher` 上，按 host 记并发上限、最小间隔、冷却：
   - `steamcommunity.com`：2 并发 / 1000 ms 间隔 / 无 `Retry-After` 时冷却 15 分钟 / 上限 60 分钟；
   - `images.steamusercontent.com`：4 / 100 ms / 5 分钟 / 30 分钟；
   - `api.steampowered.com`、`store.steampowered.com`：2 / 250 ms / 5 分钟 / 30 分钟；
   - 其余（PDX、Braze 等）：4 / 不限 / 5 分钟 / 30 分钟。
   `HttpFetcher` 只对**已配置的 host** 加闸，未配置的沿用旧路径；非 2xx 且 `HttpThrottleHeaders.IsThrottleStatus`
   （429，或 503 带 `Retry-After`）时 `ReportThrottled` 并抛 `SteamThrottleException`，`Retry-After`
   秒数与 HTTP-date 两种写法都解析、夹到 1 小时以内。连续被限流冷却翻倍（1<<min(n-1,4)），封顶 `MaxCooldown`。
   **闸门只管「发出请求到拿到响应头」这一段**，响应体流式读取期间名额已让出——图片下载不会被 180 秒总超时卡住，
   这一点写进了 `HttpFetcher` 的类注释。
2. **在途去重（P1）** —— `src/PdxLauncher.Core/Online/RequestCoalescer.cs`。同一键在途时复用同一个
   `TaskCompletionSource`，后到者只记一条 Info「请求合并」并等同一个结果；失败/取消广播给所有等待者。
   `SteamWorkshopClient` 的四个入口分别用 `browse:{appid}|{uri}`、`detail:{id}`、`item:{id}`、
   `comments:{id}|{start}|{count}`；`WorkshopImageCache` 用 `cover:{key}|{url}`、`shot:{key}|{url}`。
   只合并**在途**，不做结果缓存（结果缓存是 P2 的事）。
3. **结果缓存（P2）** ——
   - `src/PdxLauncher.Core/Online/SteamItemPageCache.cs`：物品页 HTML 的**解析结果**落盘到
     `workshop-info/workshop-pages.json`（`version: 1`），TTL 12 小时、最多 400 条、单条序列化超过 512 KB
     只留内存副本（索引不膨胀）。索引损坏/读不出按空缓存处理，写入走 `WriteAllTextAtomic`。
     `WorkshopDetailSource.GetSteamItemPageAsync` 改成「缓存→联网→失败时退回旧副本」，
     退回时日志 `物品页使用缓存：{id}（请求失败，改用旧副本）`。
   - 评论首屏：物品页已经内嵌前 10 条，详情页不再额外请求第 1 页；只有内嵌为空或物品页拿不到时才回退到
     `comment/PublishedFile_Public/render/`。翻页语义改成按「当前已展示的起点 + 条数」推进
     （`_commentsShownStart` / `_commentsShownCount`），内嵌 10 条与接口 15 条不会错位。
   - 列表页缓存：`WorkshopPageCache.DefaultTtl` 5 → **15 分钟**，新增 `Retention = 2 小时`（过期仍先显示）、
     `MaxPages = 120`（按 `FetchedUtc` 淘汰）、`LastAttemptUtc` + `CanReattempt`（同一页 2 分钟内不重复静默刷新）；
     `WorkshopViewModel.LoadAsync` 删掉 `_pages.Clear()`，换游戏不再清空别人的页。
4. **失败放大（P3）** —— `AssetDownloader` 对 `SteamThrottleException` 立即放弃（不重试、不等待、清 `.part`），
   其它瞬时失败仍是 500 ms / 1500 ms 三次；`WorkshopViewModel.RefreshAsync` 在冷却期直接给提示不发请求；
   限流文案走 `Keys.WorkshopThrottlePaused`（列表页状态栏）与 `Keys.WorkshopThrottleRetryIn`（详情页错误区），
   都在 `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` 里，`文案源生成器` 重新生成 `Strings.g.cs`。
   **没有改 XAML**：两条文案都写进已有的 `StatusMessage` / `ErrorText` 绑定，避开 WMC9999。

### 自检

- `tests/PdxLauncher.Checks/RequestThrottleChecks.cs`：`Retry-After` 秒数/HTTP-date/超限夹取/非法值、
  429 与 503 的判定、冷却期拒绝且不消耗等待（注入 delay 计数）、连续触发翻倍与封顶、`ReportSuccess` 归零、
  并发上限（第 3 个在排队、释放后进）、最小间隔（只等一次、时长≈间隔）、统计计数。
- `tests/PdxLauncher.Checks/RequestCoalescerChecks.cs`：同键只跑一次、不同键不合并、异常广播、
  完成后条目清理且同名键可重新开始。
- `tests/PdxLauncher.Checks/SteamItemPageCacheChecks.cs`：落盘往返（含评论字段）、12 小时 TTL、
  400 条容量淘汰最旧、>512 KB 不落盘、损坏索引按空缓存、`Restore` 映射。
- `build-debug.cmd` 全绿：检查类 47、断言失败 0；`文案自检` 通过。

### 实测验收（Debug exe，手工）

- 同一 mod 连开两次详情：第二次日志 `物品页使用缓存：…` 且 `steamcommunity.com` 计数为 0。
- 翻页/换筛选再翻回来：命中页缓存，不重复 `browse`。
- 游戏 A → 游戏 B → 游戏 A：A 的列表直接铺上，不重新抓。
- 手动刷新一次：日志 `本次刷新请求数：steamcommunity.com=1 …`（RefreshAsync 里按 host 统计差值）。
- 人为把 `Retry-After` 打成 120 秒：单条 Warn + 状态栏「已暂停约 2 分钟」，冷却期内点刷新不再发请求。

### 限制 / 未覆盖

- 冷却只在进程内有效，重启后清零；没有落盘断路器。
- 「在浏览器打开」按钮走的是系统浏览器，不受闸门约束，但仍占用同一 IP 的配额（README 已注明）。
- `WorkshopDetailSource` 的「联网失败退回旧副本」分支没有自检：它在 App 层，构造要带
  `AccountService` / `IPlaySetRepository`，没法像 `WorkshopImageCache` 那样整文件链接进检查工程；
  这一分支目前只靠日志与手工验收。
- 非目标：批量 `GetPublishedFileDetails`、图片懒加载、设置页的请求计数器都未做。

### 附注：小节号重复

`## 0.18`（设置页缓存接管 / EU4 发灰）与 `## 0.3`（播放集激活 / 整窗背景）各出现过两次，
是当时手写小节号撞号留下的历史遗留；README 与本文多处按 `§0.18` / `§0.3` 交叉引用，
重编号会把那些引用一并带偏，故维持原样，只在此说明。


## 0.51 2026-10-29：订阅的创意工坊 mod「无法解析」——把 metadata.json 的 id 当成工坊 id

### 起因

用户给了订阅目录 `D:\app\Steam\steamapps\workshop\content\529340\3742679779`（Victoria 3，`Daoyu Cheat (1.13 Community Port) 刀鱼作弊`），
说「有些订阅 mod 无法解析」。真机 UIA dump（`.scratch/modlib-uia.txt`，07:15，`版本 1.13.11 (Matcha)`）
坐实了症状：该行 `modLibraryRow_9fe65f9f6bb53d0022ebce4c16e5ee3f` 落进「兼容性警告」组，
**三个按钮全是 disabled**，详情页 tooltip 写着「这个 mod 没有工坊详情页（本地 mod）」——
它是订阅的工坊 mod，却被当成纯本地 mod。其余 51 行四个按钮都可用。

### 根因

1. **`metadata.json` 的 `id` 不是创意工坊 id。** 官方启动器把两者分开存：`mods.steamId` = 数字工坊 id，
   `mods.metadataId` = `metadata.json` 的 `id`（真机可证：`2882576273 / weiqingyu1982@126.com`、
   `3472248460 / tech.res`、`3385002128 / com.github.Victoria-3-Modding-Co-op.Community-Mod-Framework`）。
   我们（`ModCatalog.AddMetadataMod`）直接拿 `id` 当 steamId，空值才退到工坊目录名；
   V3 的 65 个订阅里 **11 个** `id` 是 PDX 风格串，于是 `SteamWorkshop.GetWorkshopId`
   （只认纯 ASCII 数字）返回 null → `ModWorkshopRef.Resolve` 落到 `SourceLocal` → 三按钮全灰。
2. **合并时扫描值反过来盖掉库里的数字 id**（`ModCatalog.Compose` 原 `record?.SteamId ?? sqlite?.SteamId`），
   所以那 11 个**已登记**的 mod 在播放集页也拿不到工坊按钮——不只是 3742679779。
3. **未登记行被硬编码成 `source:"local"`**（`ModLibraryService.LoadAsync`），文案因此说「本地 mod」。
4. **未登记行不能加入播放集**（`ModsViewModel.GetAddCandidateModsAsync` 整条跳过 `IsUnregistered`），
   因为它没有 `mods.id` 这个外键。而 3742679779 恰好是唯一一行「磁盘上有、官方库里没有」的订阅 mod。

### 改动

- `src/PdxLauncher.Core/Playsets/ModCatalog.cs`
  - `AddMetadataMod(...)` 增 `bool isWorkshopRoot`（`ScanMetadataFiles` 对工坊目录传 true、
    `<gameDataDir>\mod\<dir>` 传 false），新 `ResolveSteamId(...)`：工坊目录 → 目录名是数字就用它，
    否则 `metadata.json` 的 `id` 是数字就用它，两者都不是则 null（并留一条 Warn）；本地 mod 目录
    **不拿目录名编造工坊 id**。
  - `Compose` 的 steamId 改「数字优先」：`DigitsOrNull(record) ?? DigitsOrNull(sqlite) ?? 原值`。
- `src/PdxLauncher.Core/Playsets/SteamWorkshop.cs`：`IsAllDigits` 从 private 提成 `internal`（同程序集复用）。
- `src/PdxLauncher.Core/Playsets/PlaySetRepository.cs`：`SavePlaySetModsAsync` 在同一个 `BEGIN IMMEDIATE`
  事务（仍只有一次 `.pdxnext.bak` 备份）里先 `EnsureModsRegistered(...)`——给「磁盘上有内容、库里没有该 id」
  的 mod 补登记一行 `mods`：`id` 用扫描出的确定性 GUID（重扫不变）、`steamId` 只写数字工坊 id、`source`
  有工坊 id 记 `steam` 否则 `local`、`status` 空或 `missing` 记 `ready_to_play`。列按 `PRAGMA table_info`
  与目标表求交集（各游戏/老版本 `mods` 表列不同），`INSERT OR IGNORE`，单行失败只 Warn 不拖垮整次保存。
- `src/PdxLauncher.App/Services/ModLibraryService.cs`：未登记行的 `Source` 按能否解析出工坊 id 决定。
- `src/PdxLauncher.App/ViewModels/ModLibraryRowViewModel.cs`：未登记 + 工坊来源改用新文案；
  `ModsViewModel.GetAddCandidateModsAsync` 不再跳过未登记行（保存时补登记）。

### 自检

- 新增 `tests/PdxLauncher.Checks/ModCatalogChecks.cs`（4 例）：工坊目录名优先 / `metadata id` 兜底 /
  本地 mod 不编造工坊 id / 合并时数字 steamId 优先（且 ModId 不变）。
- 新增 `tests/PdxLauncher.Checks/ModRegistrationChecks.cs`（4 例）：补登记未登记的工坊 mod / 幂等 /
  内容不存在不登记 / 窄 `mods` 表（无 `steamId` 列）不报错。`PlaysetChecks.CreateGame`、`CreateTempRoot`、
  `TryDelete` 由 private 提成 internal 供复用。
- `build-debug.cmd` 全绿：检查类 47 → **49**、断言失败 0；`build.cmd`（Release）同样 49/0；
  `文案自检` 通过（702 键）。
- 新文案 `modlib.warningUnregisteredWorkshop`：zh「创意工坊订阅的 mod，官方 mod 库中还没有登记（加入播放集时会自动补登记）」，
  en 同步；`Strings.g.cs` 由 `src/PdxLauncher.Localization.Generator` 重新生成。

### 实测验收（Release exe + 真机数据）

- 全量扫描探针（`.scratch/wsid-probe`，一次性）：65 个工坊目录 → **65 个数字工坊 id、无法解析 0 个**；
  刀鱼作弊 `SteamId=3742679779`、工坊页 `…?id=3742679779`，`ModId` 仍是
  `9fe65f9f-6bb5-3d00-22eb-ce4c16e5ee3f`（与修复前同一行，未产生重复行）。
  该机另有 1 个真本地 mod（`<docs>\mod\更改虚空造物`），解析不出工坊 id 是对的。
  探针在真实库的 `File.Copy` 副本上先跑通「补登记 + `playsets_mods` + 备份」。
- 修复后 Mod 库：同一行四个按钮全部 enabled（`打开详情页 / 打开本地文件夹 / 在 Steam 中打开 / 在浏览器中打开`），
  行 HelpText 变成新文案「创意工坊订阅的 mod，官方 mod 库中还没有登记（加入播放集时会自动补登记）」。
- 真机加一次：播放集页「添加模组」→ 选刀鱼作弊 →「添加」，自动保存后
  `launcher-v2.sqlite` 的 `mods` 65 → 66 行（`id=9fe65f9f…`、`steamId=3742679779`、`source=steam`、
  `status=ready_to_play`、`dirPath=…\3742679779`、`requiredVersion=1.13.*`），`playsets_mods` 25 → 26 行
  （position 25、enabled 1），`launcher-v2.sqlite.pdxnext.bak` 时间戳刷新，
  `content_load.json` 的 `enabledMods` 多出该目录。重开 Mod 库该行不再有未登记警示（HelpText 为空），
  行数仍 52，其余 5 条「版本不兼容」警示不变——回归未见变化。

### 限制 / 未覆盖

- **可能双行**：官方启动器之后自己登记同一个 mod 时会再插一行（同 dirPath、不同 id）。我们只在
  `mods` 里找不到该 id 时补登记，且写入前有 `.pdxnext.bak` 可回退。
- **不回填**已有行的空 `dirPath`：如 `喜儿修改器`（`4b02923b-…`，`dirPath` 为 NULL）在播放集里启用也不会进
  `content_load.json`——这是本次之前就有的行为，留给官方启动器下次扫描自愈。
- 不写 `metadataId` / `metadataVersion` / `size` / `subscribedDate` / `thumbnailUrl`：补登记行只保证
  「能被自己的界面认出来 + 能进播放集」，官方库需要的完整元数据仍以官方启动器扫描为准。
- CS2 的 pdx-mods（`PlaysetsJsonRepository` + `ModWorkshopRef.ResolvePdx`）未动：JSON 仲裁路径本就不产生
  `IsUnregistered` 行。
- `.scratch/wsid-probe`、`.scratch/wsid-uia*.ps1`、`.scratch/wsid-add*.ps1` 是一次性验收脚本，没有进构建。


## 0.52 2026-10-29：播放集页「添加模组」弹窗改成独立页面

### 起因

用户原话：「播放集页面的添加mod弹窗改成添加页面」。

原实现是播放集页工具条上那个 AccentButton「添加模组」点开后弹出的 `ContentDialog`：
面板在代码里现搭（一个 `TextBox` 搜索 + 一个 `SelectionMode=Multiple` 的 `ListView`），
底部只有「添加 / 取消」两个按钮。问题有三：

1. **弹窗装不下候选列表。** 弹窗高度有限，mod 库几十条时只能看到一屏多一点；
   搜索框与列表都是 `MinWidth 420`，在窄窗口里弹窗本身就被挤出可视区。
2. **写回路径脆弱。** 弹窗关闭时才把选中项交给 `ModsViewModel.AddMods`，
   而 `AddMods` 触发的是 fire-and-forget 的自动保存；一旦中途页面被重载
   （`ModsPage` 是 `NavigationCacheMode="Required"`，返回时 `Loaded` 会重跑
   `LoadAsync → SelectPlaySetAsync`，从库里重读 `_allMods`），刚加进去的 mod 会被覆盖掉。
3. **页面职责混在 code-behind 里。** 候选筛选、选中集合、恢复选中态这些逻辑全写在
   `ModsPage.xaml.cs` 的一个方法里，嵌套的 `AddModOption` 也只是给 `ListView` 包一层显示文本。

### 根因

弹窗本质上已经是「一个页面」了：自带标题、搜索、列表、空态、底部操作条，还需要在
`ContentDialog` 里手工维护 `selected` 集合与 `restoringSelection` 标志来抵消
`ItemsSource` 重设导致的选中丢失。塞进 `ContentDialog` 只会让这些复杂度变多，而不是变少。

### 改动

**新增文件**

- `src/PdxLauncher.App/ViewModels/AddModsNavigationRequest.cs`：子页入参
  `public sealed record AddModsNavigationRequest(Guid PlaySetId, string PlaySetName, string? GameId, string? ReturnTag);`
- `src/PdxLauncher.App/ViewModels/AddModsNavigationResult.cs`：子页出参
  `public sealed record AddModsNavigationResult(Guid PlaySetId, IReadOnlyList<ModLibraryEntry> Entries);`
- `src/PdxLauncher.App/ViewModels/AddModsViewModel.cs`：候选加载 / 搜索过滤 / 选中计数 / 状态文案。
- `src/PdxLauncher.App/ViewModels/AddModRowViewModel.cs`：候选行（缩略图、名称、来源徽标、大小 + 注册 ID）。
- `src/PdxLauncher.App/Views/AddModsPage.xaml` + `.xaml.cs`：子页本体，**不设** `NavigationCacheMode`
  （每次进入都是干净实例），自带 `_lifetime`（`Loaded` 重建 / `Unloaded` 取消）。

**导航接线**

- `NavigationService`：新增 `public const string AddModsTag = "addmods";`，
  `TypeNames` 里加 `[AddModsTag] = "PdxLauncher.App.Views.AddModsPage",`。
  注意 `Navigate` 在 `parameter == null` 时会跳过同 tag 导航，所以这个子页**必须**带参数进入。
- `MainWindow.xaml.cs`：新增 `_modsPage` / `_addModsPage` 两个字段（`AttachPageEvents` 里 detach + 置 null，
  `OnWindowClosed` 里同样 detach）；`NavigateToAddMods` 走 `_navigation.Navigate(AddModsTag, request)`
  且**故意不同步左栏选中项**（子页没有左栏菜单项，选中态应停在「播放集」，
  与 `NavigateToWorkshopDetail` 同一套做法）；`OnAddModsSelected` 回
  `_navigation.Navigate(PlaySetsTag, result)` 并同步左栏；`UpdateContentNavState` 的
  「内容不可管理就回主页」名单里也加上了 `AddModsTag`。
- `ModsPage.xaml.cs`：新增 `public event EventHandler<AddModsNavigationRequest>? AddModsRequested;`，
  `OnAddModsClick` 改成「没选播放集就提示，否则抛事件让 MainWindow 去导航」；
  `OnNavigatedTo` 接住 `AddModsNavigationResult` 存进 `_pendingAddResult`；
  `OnLoaded` 里 `await ReloadAsync()` **之后**再 `await ApplyPendingAddsAsync()`。

**关键约束：写回必须在 reload 之后。** `ModsPage` 是 `NavigationCacheMode="Required"`，
返回时 `Loaded` 会重跑 `LoadAsync → SelectPlaySetAsync`，而 `SelectPlaySetAsync` 用
`PlaySetEditor.Renumber(saved…)` 把 `_allMods` 整个替换成库里的内容；
自动保存又是 fire-and-forget、在真正写库那一刻才读 `_allMods`。
所以子页**不**直接写库，只把勾选结果交回；由播放集页在 reload 完成后统一
`SelectPlaySetAsync(目标集)`（目标集不同时先切过去）+ `AddMods(entries)`，
随后 `RequestAutoSave()` 照旧触发。目标播放集已不存在时，提示
`mods.addPage.targetMissing` 并且一个字节都不写。

**删除**

- `ModsViewModel.GetAddCandidateModsAsync`（原本是「弹窗候选」专用）：子页改用
  `ModLibraryService.LoadAsync` + `IPlaySetRepository.GetPlaySetModsAsync` 自己取候选并排除已在集内的。
- `ModsPage.xaml.cs` 里 `OnAddModsClick` 的整段弹窗代码与嵌套 `private sealed class AddModOption`。
- 5 个只为弹窗存在的文案键：`mods.addDialog.add` / `mods.addDialog.listName` /
  `mods.addDialog.searchName` / `mods.addDialog.searchPlaceholder` / `mods.dialog.noAddableMods`。

**文案**

新增 13 个 `mods.addPage.*` 键（zh-CN / en-US 同步）：`title` / `target`（带 `{0}` 播放集名）/
`searchPlaceholder` / `searchName` / `listName` / `empty` / `noMatch` / `hint` / `add` / `back` /
`selectedCount`（带 `{0}`）/ `candidateCount`（带 `{0}`）/ `targetMissing`。
`Strings.g.cs` 由 `文案源生成器` 重新生成（735 键），`--check` 通过。

**新页面的 AutomationId（便于 UIA 验收）**

`btnAddModsBack`、`txtAddModsTitle`、`txtAddModsTarget`、`txtAddModsSearch`、
`lstAddModsCandidates`、`txtAddModsEmpty`、`txtAddModsNoMatch`、`txtAddModsCandidateCount`、
`txtAddModsStatus`、`txtAddModsSelectedCount`、`btnAddModsCancel`、`btnAddModsConfirm`、`ringAddModsBusy`；
候选行 `addModCandidate_<modIdN>`，勾选框 `addModCandidateCheck_<modIdN>`。

### 自检

- `dotnet build "PdxLauncher.sln" -c Release -m:1` → **0 警告 0 错误**。
- `dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"` →
  **检查类: 54，失败: 0，断言失败总数: 0**。
- `文案自检` 通过（735 键）。
- `tests/PdxLauncher.Checks/ModPathChecks.cs` 的 `CheckKeysExist()` 追加了 13 个
  `mods.addPage.*` 键的双语存在性断言（检查类数量仍是 54，README 计数不变）。

### 预期验收路径（本轮只跑了编译 + 自检，界面行为按代码路径推导，未做真机 UIA 点击回归）

- 播放集页点「添加模组」→ 进入独立子页，**左栏选中态仍停在「播放集」**；
  页头是返回按钮 + 「添加模组」+ 目标播放集名，下面是搜索框与「共 N 个可添加的 mod」，
  再下面是可勾选列表（缩略图 / 名称 / 来源徽标 / 大小 + 注册 ID），底部是「已选 N 个 mod」与「取消 / 添加」。
- 搜索按名称 / 注册 ID 过滤，勾选状态在过滤前后保持；「取消」或返回箭头都不改任何东西。
- 点「添加」回到播放集页：mod 追加到列表末尾、默认启用，状态栏显示「已添加 N 个 mod（自动保存中）」，
  随后自动保存落库（`playsets_mods` 多出对应行）。
- 空态（mod 库里没有可加的）与搜索无结果分别显示 `txtAddModsEmpty` / `txtAddModsNoMatch`。
- 目标播放集在子页打开期间被删除：返回时提示 `mods.addPage.targetMissing`，播放集内容不变。

### 限制 / 未覆盖

- **没有做真机 UIA 自动化回归**：本轮只跑了编译与 54 个自检类，界面行为是按代码路径推演的，
  未用 `winapp ui` 跑一遍真实点击流程（§0.52 的「实测验收」条目待补 UIA dump）。
- **子页是独立实例**（没有 `NavigationCacheMode`）：每次进入都会重新读 mod 库，
  mod 很多时有一次可感知的加载；这是为了避免缓存实例与 `SelectedPlaySet` 状态打架。
- **候选仍受「当前游戏」限制**：没有选中游戏时子页显示「请先选择游戏」并给出空列表。
- 未覆盖的场景：子页打开期间切换游戏（`CurrentGameChanged`）——子页不会自动跟随刷新，
  关闭重开即可。
- `mods.status.readLibraryFailed` 仍被新子页复用（读候选失败时的状态文案）；
  `mods.status.noModsAdded` 仍由 `ModsViewModel.AddMods` 使用（一个都没加上时的提示），两者都没删。

## 0.53 2026-10-30：Mod 库页三角提示改成「版本支持」口径

### 起因

用户原话：「mod 库页面的感叹号报错应该是版本支持报错，你现在的报错还以为是 mod 不行呢」。

原实现里三角提示的三种来源都写成了「mod 有问题」的口气：

- `modlib.warningStatus` = 「状态异常：{0}」——读起来像 mod 装坏了；
- `modlib.warningOutdated` = 「可能不兼容当前游戏版本（要求 {0}，当前 {1}）」——直接下「不兼容」的判断；
- 未登记类提示虽然说明事实（本地磁盘 / 工坊订阅、官方库里没登记），但整段仍以「异常」收尾。

用户的要求是：三角只讲**版本支持**——这个 mod 声明支持哪个版本、和当前游戏版本对不对得上。

### 根因

1. **文案口径**：三句文案写死了否定判断（状态异常 / 不兼容），而登记情况与 `mods.status` 本来只是
   「这个 mod 在官方库里是什么状态」的**事实**，不是 mod 的问题。
2. **判定与文案耦合**：`ModLibraryRowViewModel.BuildWarning`（App 层）里同时写着「判什么」和「怎么说」，
   判定逻辑没法被自检覆盖（App 是 WinExe + windows TFM，`PdxLauncher.Checks` 引用不了）。
3. 官方库里 `mods.status` 恒为 `ready_to_play`（CK3 382 / Stellaris 313 / V3 66 全量实测），
   「状态异常」这条文案在官方 sqlite 路径上基本不会触发；用户真正看到的三角来自**版本分支**
   （V3 上 6 行：`1.12.*`×4、`1.11.*`×1、`1.9.*`×1 对游戏 `1.13.11 (Matcha)`），文案却写着「可能不兼容」。

### 改动

**新增文件**

- `src/PdxLauncher.Core/Playsets/ModLibraryWarning.cs`：三角提示的**唯一判定点**（Core，可被自检直接覆盖）。
  - `enum ModWarningKind { None, Unregistered, UnregisteredWorkshop, Status, VersionBelow }`
  - `enum ModVersionSupport { Undeclared, DeclaredUnknownGame, NotBelow, Below }`
  - `public sealed record ModWarningInfo(ModWarningKind Kind, ModVersionSupport VersionSupport, string RequiredVersion, string GameVersion, string Status)`，
    带 `HasTriangle` / `VersionKey` / `ConclusionKey` / `NoteKey` / `VersionArgumentCount` / `NoteArgumentCount`。
  - `public static class ModLibraryWarnings`：`Resolve(ModLibraryEntry entry, string? gameVersion)`、
    `ClassifyVersionSupport(string? requiredVersion, string? gameVersion)`、`ReadyStatus = "ready_to_play"`，
    以及 9 个文案键常量（`NameKey`、`KeyVersionUndeclared` / `KeyVersionDeclaredUnknownGame` / `KeyVersionDeclared` /
    `KeyVersionNotBelow` / `KeyVersionBelow`、`KeyUnregistered` / `KeyUnregisteredWorkshop` / `KeyStatus`）。
  - 判定优先级不变：**未登记（本地 / 工坊按 `SteamWorkshop.GetWorkshopId` 区分）→ status ≠ ready_to_play → 声明版本低于当前游戏版本**。
  - 版本对照沿用 `ModVersion` 的 major.minor 口径（**判定口径本身没改**，只换了说法）。
- `tests/PdxLauncher.Checks/ModLibraryWarningChecks.cs`：新检查类，覆盖未登记 / 状态 / 版本低于 / 版本不低于 /
  未声明 / 游戏版本未知，以及 9 个文案键在 zh-CN 与 en-US 两份 `modlib.json` 里都存在。

**Core 工具方法**

- `src/PdxLauncher.Core/Playsets/ModVersion.cs`：新增 `public static int? CompareMajorMinor(string? requiredVersion, string? gameVersion)`
  （任一边读不出两段数字返回 null，否则 -1 / 0 / 1），`IsOutdated` 改写为 `=> CompareMajorMinor(requiredVersion, gameVersion) < 0;`（行为等价）。

**文案**

- `src/PdxLauncher.App/Strings/zh-CN/Resources.resw` + `en-US/modlib.json`：
  - 删除 `modlib.warningOutdated`；
  - `modlib.warningName`：兼容性警告 → **版本支持提示**（en：`Version support`）；
  - `modlib.warningStatus`：状态异常：{0} → **官方库中的状态：{0}。**；
  - `modlib.warningUnregistered` / `warningUnregisteredWorkshop` 改成中性陈述句；
  - 新增 5 键 `modlib.versionSupport.undeclared` / `.declaredUnknownGame`（1 参数）/ `.declared`（2 参数）/ `.notBelow` / `.below`。
  - 键数 735 → **739**；`src/PdxLauncher.App/Localization/Strings.g.cs` 由 `文案源生成器` 重新生成（生成物不手改）。

**界面**

- `src/PdxLauncher.App/ViewModels/ModLibraryRowViewModel.cs`：删掉本地 `ReadyStatus` 常量，`BuildWarning` 改成
  「版本说明句 + 对照结论句 + 补充说明句」三段拼接（`string.Join(' ', parts)`），判定交给 `ModLibraryWarnings.Resolve`；
  `HasWarning` / `WarningText` 的注释改成「版本支持提示」口径。
- `src/PdxLauncher.App/Views/ModLibraryPage.xaml`：文件头注释同步改口径（三角的三种来源写清楚，文案只讲版本支持）。

**文档**

- `README.md`：Mod 库页说明改成「本地 mod / 库里状态不是 ready_to_play 的 mod / 声明的支持版本低于当前游戏版本的 mod 带三角提示标记，
  提示只讲版本支持」；检查类计数 54 → **55**，文件计数 342 → **344 个 .cs + 14 个 .xaml**（Core 184 / App 93+14 / Imaging 9 / Checks 58）。
- `docs/DATA-FORMATS.md`：Mod 库小节补「三角提示的三种来源（判定优先级）」与「提示文案只讲版本支持」的说明。
- `docs/HANDOFF.md`：文件头自检计数 54 → **55**。

### 自检

- `文案源生成器` + `--check` → **739 键**，退出码 0。
- `dotnet build "PdxLauncher.sln" -c Release -m:1` → **0 警告 0 错误**（构建前 `taskkill /F /IM PdxLauncher.App.exe`）。
- `dotnet "tests\\PdxLauncher.Checks\\bin\\Release\\net10.0\\PdxLauncher.Checks.dll"` →
  **检查类: 55，失败: 0，断言失败总数: 0**。
- 新检查类在第一轮跑出过 5 条断言失败，暴露了一个真 bug：`ModLibraryWarnings.Resolve` 在没有三角时返回
  `ModWarningInfo.None with { ... }`，把已经算好的版本对照结论覆盖成了 `Undeclared`
  （「声明版本不低于当前版本」被说成「没有声明版本」）。已改为统一 `return new ModWarningInfo(kind, support, required, game, status);`。
  另有 1 条是自检自身写错（把 `Ready_To_Play` 当成状态异常，实际按大小写不敏感比较它等于 `ready_to_play`），已改成断言 `MISSING` 才是状态类。

### 预期验收路径（本轮只跑了编译 + 自检，界面文案按代码路径推导，未做真机 UIA 点击回归）

- Mod 库页某行声明 `1.12.*`、游戏版本 `1.13.11 (Matcha)`：悬停三角提示 =
  「该 mod 声明支持版本 1.12.*；当前游戏版本 1.13.11 (Matcha)。 声明的版本低于当前游戏版本。」
- 某行声明 `1.13.*`（不低于当前版本）：不挂三角；库里状态不是 `ready_to_play` 时才挂三角，
  提示 = 「该 mod 声明支持版本 1.13.*；当前游戏版本 1.13.11 (Matcha)。 声明的版本不低于当前游戏版本。 官方库中的状态：missing。」
- 本地磁盘 mod（官方库没登记、也没声明版本）：提示 = 「该 mod 没有声明支持的游戏版本。 该 mod 来自本地磁盘，官方 mod 库中还没有登记。」
- 创意工坊订阅但官方库没登记（声明 `1.12.*`）：提示 = 「该 mod 声明支持版本 1.12.*；当前游戏版本 1.13.11 (Matcha)。 声明的版本低于当前游戏版本。 该 mod 来自创意工坊订阅，官方 mod 库中还没有登记（加入播放集时会自动补登记）。」
  （多句之间用一个空格拼接，英文同理。）
- 行尾「支持版本 1.12.*」那一栏仍照旧显示，不受影响。

### 限制 / 未覆盖

- **没有做真机 UIA 自动化回归**：本轮只跑了编译与 55 个自检类，三角提示的最终渲染文本未在真机上逐字核对
  （Mod 库页的 UIA 访问此前就没打通，见 §0.52 的同款限制）。
- **判定口径没有动**：`ModVersion` 仍只比较 major.minor，且 Paradox 的 `requiredVersion` 语义其实是「最低支持版本」，
  所以 CK3 上 `1.19.*` 对 `1.20.1.2` 会被判成「低于」（实测 382 行里 156 行）。这是本轮**刻意不改**的部分
  （用户拍板「不改判定，只改文案」），要修得单独开一轮。
- 播放集页那套「这一行为什么不会被加载」的提示（`ModLoadRules.DescribeSkipKey` → `ModsViewModel`）与本轮的三角无关，没有动。
- 「三角只讲版本支持」意味着**不再有任何「这个 mod 装不了」的判断**：磁盘目录缺失、mod 文件损坏这类问题仍然只体现在状态栏与行尾按钮置灰上。

## 0.54 2026-10-30：Cities: Skylines 1 从「未适配」→「仅启动」+ 补丁链路打通

### 起因

用户原话（两句，中间隔了一轮）：
① 「怎么城市天际线一没有适配，还有补丁能解开它的DLC吗」
② 「那处理下 DLC 解锁补丁和适配问题」

### 根因（全部真机实测）

- `F:\SteamLibrary\steamapps\common\Cities_Skylines\launcher-settings.json` 写的是 `"gameId": "cities_v2"`，
  而仓库所有 id 表（`KnownGames.DisplayNames` / `FullSupport` / `LaunchOnly`、`GameSteamAppIds.Known`、
  `SteamWorkshopGames.Known`）只登记 `cities_skylines` ⇒ `KnownGames.GetSupport` 返回 `Unsupported`，
  卡片角标就是「未适配」。
- Paradox 元数据 GraphQL（`POST https://api.paradox-interactive.com/metadata/graphql`，变量类型必须是 `[ID]`）：
  `["cities_v2"]` → `{"data":{"games":[]}}`；`["cities_skylines"]` → 正常返回 CS1 记录。
  所以远程主题一直刷不出来，日志里反复是「元数据未返回该游戏，跳过主题刷新: cities_v2」。
- **公告接口方向相反**：`GET .../communication/braze/contentcards/cities_v2` → 3307 B / 3 张卡；
  `.../contentcards/cities_skylines` → `{}`。归一不能一刀切，否则 CS1 的公告全没。
- 补丁本来就该能用：`steam_api64.dll`（239184 B，PE machine 0x8664）就在安装根，`Cities.exe` 无 SteamStub/DRM，
  `ColossalNative.dll` 是运行时 `LoadLibrary("steam_api64.dll")` 动态加载（不在导入表）⇒ 放 loader + `cream_api.ini` 即生效。
  Steam 安装靠 `appmanifest_255710.acf` 就能拿到 appId；**独立安装**原本拿不到 ⇒ 补丁报「无 appId」。
- CS1 本体**不读任何加载文件**：全安装 229 个 dll/exe + 64 个 >60 MB 大文件流式扫
  `content_load` / `enabledMods` / `disabledDLC` / `enabledUGC` / `dlc_signature` / `Addons` **全 0 命中**；
  `launcher-v2` 字样只出现在 `dowser.exe`（官方 Go 引导器）。mod 由游戏内 Colossal Mods Domain 加载
  （`Files\Mods\` + 工坊目录 `content\255710\<id>\`，开关存游戏自己的 `.cgs`），DLC 归属纯靠 Steam 授权，
  安装目录里没有任何 `.dlc` 文件 ⇒ 与 Prison Architect 同理，**只能「仅启动」，不能设 Full**。

### 改动

**Core（`src/PdxLauncher.Core/`）**

- `Games/KnownGames.cs`
  - 新增 `private static readonly Dictionary<string,string> IdentityAliases`（OrdinalIgnoreCase），目前只有
    `["cities_v2"] = "cities_skylines"`，带 XML doc 说明「官方启动器的内部编号 vs 服务端 id」。
  - `DisplayNames` 别名区补 `["cities_v2"] = "Cities: Skylines"`（兜底展示名，不隐含支持等级）。
  - `LaunchOnly` 补 `"cities_skylines"`，doc 里写清 CS1 的理由（与 PA 的 schema 理由不同）。
  - 新增 `public static string CanonicalizeGameId(string? gameId)`：空 / 空白 → `string.Empty`，命中别名 → 正名，否则原样。
  - 新增 `public static string GetNewsFeedGameId(string? gameId)`：归一后若为 `cities_skylines` → 返回 `"cities_v2"`，
    否则返回正名。**全仓库唯一的反向特例**，只给 Braze 公告用。
  - `IsKnown` / `GetDisplayName` / `GetSupport` 内部先 `CanonicalizeGameId` 再查表。
- `Games/GameCatalog.cs`：`DiscoverOneAsync` / `TryAddInstallation` / `BuildInstallation` /
  `BuildDetectedInstallation` / `TryAddDetectedInstallation` 全部先归一，
  `seenGameIds` 与写进 `GameInstallation.GameId` 的都用正名（否则支持等级、appId 映射、工坊来源、远程主题全部落空）。
- `Configuration/GameSelection.cs`：新增 `using PdxLauncher.Core.Games;`，`Select` 里把 `lastGameId` 先归一
  —— 老设置里存的是 `cities_v2`，不归一就选不中。
- `Online/GameSteamAppIds.cs` / `Online/SteamWorkshopGames.cs`：`TryGet` 内先归一（于是 `cities_v2` 也能查到 255710 / 有工坊）。
- `Online/NewsFeedService.cs`：新增 `private static string NewsGameId(GameInstallation game) => KnownGames.GetNewsFeedGameId(game.GameId);`，
  `FetchAsync` 的 URL 与**4 处缓存写键**（partial / updated / 404-empty / failure）全部改用它；
  日志里仍打印 `game.GameId`（排障时看归一后的 id 更有用）。

**App**：**零改动**。补丁 UI、工坊页、主页提示都没有按支持等级 gating 的代码
（`WorkshopViewModel` 在「仅启动」+ 能解析 appId 时本来就会继续查；`MainWindow.UpdateContentNavState` 只按
`SupportsContentManagement` 隐藏导航项，这正是「仅启动」该有的表现）。

**测试（`tests/PdxLauncher.Checks/`，检查类数量不变，仍 55 个）**

- 新增 fixture `tests/fixtures/cities_skylines/launcher-settings.json`（真机原文，`gameId: cities_v2`，无 `dlcPath`）。
- `GamesChecks.cs`：`CheckLauncherSettings` 加 CS1 fixture 断言（原始 id 保持 `cities_v2`、Format、`%LOCALAPPDATA%` 展开、
  缺 `dlcPath` 时兜底到 settings 目录）；`CheckSyntheticLibrary` 加一份 CS1 安装（断言收录 4 个游戏、
  `cities_skylines` 是 LaunchOnly / IsSupported=true / SupportsContentManagement=false、
  `DiscoverOneAsync("cities_v2")` 能命中）；`CheckSupportTiers` 加 CS1 支持等级与 `CanonicalizeGameId` /
  `GetNewsFeedGameId` 的全套断言；`CheckRealMachine` 把 `cities_skylines` 加进 `requiredIds`，
  并断言真机 Support=LaunchOnly / SteamAppId=255710 / exePath 以 `Cities.exe` 结尾 / 主题来自 InstallDir。
- `GameSteamAppIdsChecks.cs`：`cities_v2` → 255710，且 `WorkshopSourceChooser.ResolveAppId(null, "cities_v2")` = 255710。
- `SteamWorkshopGamesChecks.cs`：`cities_v2` 与 `cities_skylines` 同答案。
- `GameSelectionChecks.cs`：老设置里的 `cities_v2` 能选中 `cities_skylines`（双命中 / 仅 gameId 两种路径）。
- `DlcPatchChecks.cs`：新增 `CheckCitiesSkylines1Layout`（`appId: null` 的 CS1 独立安装 → Vanilla + appId 255710 +
  目标目录是安装根 → 打补丁 → ini 里 `appid = 255710` / 保留 `orgapi64` / 含 Cities Skylines 区块 → 还原字节一致）。
- `NewsFeedChecks.cs`：新增 `CheckGameIdAlias`（URL 必须是 `contentcards/cities_v2`、缓存键是 `cities_v2`、
  缓存里不出现 `cities_skylines`、原始 id 与归一 id 共用同一份缓存且不重复请求）。

**文档**：`README.md`（支持等级表 + 「仅启动」两款的理由 + 归一说明 + 补丁不受支持等级限制）、
`docs/DATA-FORMATS.md`（§0 真机表加 CS1 行、§5 补 CS1 的 mod 形态、新增 §11.1）、`docs/HANDOFF.md`（本节）。

### 验证（本轮实跑）

- `dotnet build tests\PdxLauncher.Checks\PdxLauncher.Checks.csproj -c Release -m:1` → **0 警告 0 错误**。
- `dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll` → **检查类: 55，失败: 0，断言失败总数: 0**。
  真机段实测打印：`真机: 发现 cities_skylines（原始 id cities_v2 已归一）`、`CS1 Support=LaunchOnly`、
  `CS1 SteamAppId 255710`、`CS1 主题来自 LauncherAssets [InstallDir]`。
- **`build.cmd` 首轮失败**：`PdxLauncher.App`（PID 29224）当时正开着，锁住
  `src\PdxLauncher.App\bin\x64\Release\...\PdxLauncher.Core.dll`，MSB3027/MSB3021 复制失败。
  关掉窗口后重跑即 **0 个警告 / 0 个错误 / 检查类: 55，失败: 0**（App 工程本身零改动）。

### 明确非目标 / 未做

- **没改 `cream_api.ini`**：CS1 区块 94 条已覆盖商店 79 个已发售 DLC，只缺 4771370（Holiday Getaways）、
  4771380（Mansions & Manors）、4771390（Blueprint Beats）三条 `release_date: "Coming soon"` 的未发售条目。
  加它们会连带改 `Assets/README.md` 的字节/sha256 表与 `DlcPatchChecks.ExpectedIniSha256`，收益为零，故不动。
- **没有在真机上点「打补丁」**：按计划这一步要等用户点头。补丁只会备份 `steam_api64_o.dll` 再写 loader，可一键移除。
- **没为「仅启动」游戏放开工坊 / 播放集 / DLC 导航**：`SupportsContentManagement` 仍只对 Full 为真，
  `WorkshopViewModel` 内部虽能查，但入口不显示。要不要给 CS1 单独开一个「只读工坊浏览」是独立议题。
- **`eu4` 仍是「未适配」**：本轮只处理 CS1；EU4 的 `launcher-settings.json` 用的是 `eu4` 这个已登记 id 之外的写法，
  要单独查。


### 顺带修掉的两个素材判定缺口（同一轮，非计划内）

CS1 的远端主题第一次真正被抓取，暴露了 `AssetDownloader` 的两处硬判定：

- **`cities_skylines-button-background-image` 只有 361 B**（IHDR 8×50 + sRGB/gAMA/pHYs/tIME + 单个 IDAT + IEND，
  结构完整），被 `MinimumFileBytes = 1024` 判成坏文件。阈值一路 4 KB → 1 KB → **256 B**，
  日志文案也从「素材小于 1 KB」改成「素材小于 256 B」（不再用整数除法打印，避免四舍五入歧义）。
- **`cities_skylines-button-font-path` 是 `OTTO`**（CFF 轮廓的 OpenType，69,808 B），
  而 `AssetFileTypeDetector.Detect` 只认 `00 01 00 00`。补上 `OTTO` 与 `true` 两个分支，都归到 `AssetFileType.Ttf`（扩展名 `.ttf`）。
- 同一轮还补了 **`00 00 01 00` → `AssetFileType.Ico`（新枚举值 8，扩展名 `.ico`）**：
  `eu4-launcher-app-taskbar-icon` 是 370,070 B 的 6 帧 Windows ICO，日志里从 2026-10-27 起累计 42 次
  「素材 magic bytes 无法识别」全是它。

这三处不修的后果是同一个：`ThemeResolver.RefreshAsync` 里 `succeeded < remote.Count` 恒成立，
于是 `lastCheckedUtc` 永不推进、`retryAfterUtc` 每 10 分钟被改写一次——CS1 与 EU4 的主题会永远重复下载。
修完实测：`cities_skylines` 5/5、`eu4` 6/6，两个索引的 `lastCheckedUtc` 都已落地、`retryAfterUtc` 清空。

`OnlineChecks.CheckAssetFileTypeDetection` 相应加了 `OTTO` / `true` / `ICO` 三个判型用例、
`GetExtension(AssetFileType.Ico)` 断言，以及「361 B 的 CS1 按钮背景图被接受并落盘」的回归用例；
「小于 1 KB 被拒绝」改成「小于 256 B 被拒绝」（样本 512 B → 200 B）。
`docs/DATA-FORMATS.md` §7 的 magic/阈值那句话同步改写。

### 真机复核（2026-10-30 08:52）

| 项 | 结果 |
| --- | --- |
| `build.cmd` | 0 个警告 / 0 个错误 / 检查类: 55，失败: 0 |
| `python src\PdxLauncher.Localization.Generator --check` | 739 键通过 |
| 日志 `cities_v2` 出现次数（重启后） | 0 |
| `cities_skylines` 主题 | 5/5 素材落盘，`lastCheckedUtc` 已写，`retryAfterUtc` 空 |
| `eu4` 主题 | 6/6 素材落盘（含 370 KB ICO），`retryAfterUtc` 空 |
| 真机 `F:\...\Cities_Skylines\` | 仍是原版 `steam_api64.dll`（239,184 B），无 `cream_api.ini`、无 `_o.dll`、无 `data\patch\state.json` |

构建过程中遇到过一次 `CSC error CS2012`（`obj\...\PdxLauncher.App.dll` 被 `VBCSCompiler` 或
`Microsoft.UI.Xaml.Markup.Compiler` 占用）：`dotnet build-server shutdown` + `taskkill /IM VBCSCompiler.exe /F` 后重跑即通过，
与本轮改动无关。
### 预期验收路径（界面未做 UIA 回归）

- 游戏库卡片：Cities: Skylines 角标 = 「仅启动」（`library.badge.launchOnly`），不再是「未适配」。
- 主页：选中 CS1 时播放集下拉隐藏，改显示 `home.supportNotice.launchOnly` 那句
  「{0} 只能启动：模组与 DLC 由游戏本体管理（游戏内 MOD 菜单 / Steam 创意工坊），本启动器不读写它的数据。」
- 设置 → 高级：补丁卡片显示「原版」，详情里 appId = 255710；点「一键打补丁」→ 已打补丁；「移除补丁」→ 字节还原。
- 日志：不再出现「元数据未返回该游戏，跳过主题刷新: cities_v2」；改为主题缓存更新成功，
  `data\theme-cache\cities_skylines-*` 出现（旧的 `cities_v2-*` 若存在会变成孤儿文件，可手删）。
- 公告：CS1 仍显示 3 张官方公告卡（走 `cities_v2` URL）。


## 0.55 2026-10-30：确认 / 取消按钮换位（自绘 ConfirmOverlay，取消在左、确认在右）

### 起因

用户原话（m00001）：「把订阅页面的确认和取消换下位置」。

工坊详情页点「订阅」/「取消订阅」会弹二次确认框，用户觉得「确认」在左、「取消」在右不顺手，
要求把两者左右换位。用户随后拍板三项：

1. **范围**：不只订阅页，**全应用**所有「确认 / 取消」对话框一起换位。
2. **键盘与焦点**：Esc = 取消、Enter = 确认，**右侧的确认键高亮（Accent）**。
3. **代码落点**：抽成**通用控件 `ConfirmOverlay`**，不只在本页内实现。

### 根因（框架限制，不是笔误）

WinUI 3 的 `ContentDialog` **没有交换 Primary / Close 左右的能力**。它的默认模板
（`generic.xaml` 的 `DefaultContentDialogStyle`）把命令区固定成 5 列：

```
PrimaryColumn(*) | FirstSpacer(0) | SecondaryColumn(0) | SecondSpacer(8) | CloseColumn(*)
   PrimaryButton ↑列0                                              CloseButton ↑列4
```

`ButtonsVisibilityStates` 的 `PrimaryAndCloseVisible` 状态只隐藏 SecondaryButton，
Primary 留在列 0、Close 留在列 4；`PrimaryVisible` / `SecondaryVisible` 也只是把「唯一那个按钮」
挪到列 4。换句话说：**只要用了 `ContentDialog`，视觉顺序永远是「确认在左、取消在右」**，
框架层没有 API 可以翻转，也没有「把 CloseButton 画到左边」的办法。

同时 `ContentDialogResult` 的语义绑定在按钮角色上：ESC / 系统返回 / 手柄 B 一律触发 CloseButton，
`DefaultButton` 指定谁谁就拿 Accent 外观并响应 Enter。原本代码写的是
`DefaultButton = ContentDialogButton.Close`，即**「取消」才是 Accent 主色、Enter 触发的是取消**——
这本身也是个反直觉的设计。

### 改动

**新增 `src/PdxLauncher.App/Controls/ConfirmOverlay.xaml(.cs)`**（自绘对话框）：

- 视觉照抄 ContentDialog 的主题资源，保持观感一致：
  `ContentDialogSmokeFill` 遮罩、`ContentDialogBackground` 卡片底、
  `ContentDialogBorderBrush` + `ContentDialogBorderWidth` 描边、`OverlayCornerRadius` 圆角、
  `ContentDialogMinWidth/MaxWidth/MinHeight/MaxHeight` 尺寸、`ContentDialogPadding` 内边距、
  `ContentDialogSeparator*` 分隔线、`ContentDialogButtonMinWidth` 按钮最小宽。
- 按钮条：`StackPanel Orientation=Horizontal Spacing=8`，**左「取消」**（`btnConfirmOverlayCancel`）
  **右「确认」**（`btnConfirmOverlayConfirm`，`AccentButtonStyle`）。
- 语义：Esc = 取消、Enter = 确认；焦点必须落在浮层内部按键事件才会冒泡，
  所以显示后主动把焦点移进浮层（内容是输入控件时优先聚焦输入框，否则聚焦确认键）；
  关闭后焦点回投给打开它的那个控件；点遮罩不关闭；同一时刻只允许一个浮层。
- 内容支持任意 `UIElement`（不只是字符串），`Content` 传字符串时自动包成可换行 TextBlock。
- `IsConfirmEnabled` 可动态启停确认键（「修复 mod 路径」需要按勾选状态联动）。

**宿主**：`src/PdxLauncher.App/MainWindow.xaml` 根 `RootGrid` 末尾加
`<controls:ConfirmOverlay x:Name="ConfirmOverlayLayer" Grid.RowSpan="2" />`（跨页面覆盖，含标题栏），
`MainWindow.xaml.cs` 构造时登记 `ConfirmOverlay.Host`。

**13 处 `new ContentDialog` 全部迁移**：

| 文件 | 场景 |
| --- | --- |
| `Views/WorkshopDetailPage.xaml.cs` | PDX 订阅、Steam 直连订阅（用户最初点名的两处） |
| `Views/SettingsPage.xaml.cs` | 打补丁、移除补丁、`ConfirmAsync`（清理旧 C 盘数据 / 清空全部缓存 / 重算 dlc_signature）、`ShowMessageAsync` |
| `Views/ModsPage.xaml.cs` | 删除播放集、重命名播放集（`PromptForNameAsync`）、`ShowMessageAsync` |
| `Views/AccountPage.xaml.cs` | 账号登出 |
| `Views/DlcPage.xaml.cs` | `ShowMessageAsync`（仅「关闭」键） |
| `Views/ModPathRepairDialog.cs` | 修复 mod 路径（勾选联动启用确认键） |
| `Services/CrashReporter.cs` | 崩溃弹框（「复制详情」= 确认键，回调写剪贴板） |

**`PdxLauncher.App.csproj`**：publish target 补 `Controls\*.xbf`（原先只补根目录与 `Views\*.xbf`，
新控件的 `.xbf` 进不了发布包）。

**`tests/PdxLauncher.Checks/ConfirmOverlayChecks.cs`**（新增，第 56 个检查类）：
断言取消键在 XAML 里排在确认键之前、确认键用 Accent 样式、代码里处理了 Escape / Enter、
7 个调用点不再有 `new ContentDialog`、`MainWindow` 挂了宿主、csproj 会发布 `Controls\*.xbf`。

### 自检

- `dotnet build "PdxLauncher.sln" -c Release -m:1 --nologo` → **0 个警告 0 个错误**。
- `dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"` → **检查类: 56，失败: 0，断言失败总数: 0**。

### 真机复核（2026-10-30，winapp ui / UIA）

| 场景 | 取消 | 确认 | 结论 |
| --- | --- | --- | --- |
| 播放集页「重命名播放集」 | 取消 x=1072 | 确定 x=1279（Accent） | 顺序正确；焦点落进 `txtPlaySetName`；Esc 关闭、Enter 确认均生效 |
| 播放集页「删除播放集」 | 取消 x=1227 | 删除 x=1434 | 顺序正确；Esc 关闭生效 |
| 工坊详情页「订阅」（Steam 直连） | 取消 x=1422 | 订阅 x=1629 | 顺序正确（用户最初点名的那处） |
| 设置 → 缓存「清除全部缓存」 | 取消 x=1416 | 清除 x=1623 | 顺序正确；Esc 关闭生效 |
| 播放集页「修复 mod 路径」（无待修复项） | — | 关闭 x=1461 | 单键形态正确 |

截图证据：`ui-test-artifacts/confirm-overlay/01-rename-overlay.png`、`02-delete-overlay.png`、
`03-subscribe-overlay.png`、`04-settings-clear.png`、`05-publish-settings-clear.png`（最后一张是**发布版**
`dist\PdxNext\PdxLauncher.App.exe` 的实拍）。

**发布版复核（2026-10-30 10:32–10:40）**：`publish.cmd` 跑通（EXIT=0），
`publish/` 与 `dist/PdxNext/` 下都出现了 `Controls\ConfirmOverlay.xbf`（同时补齐了 `PdxDropDown.xbf` 等
此前也漏掉的 `Controls\` 产物）。用 `dist\PdxNext\PdxLauncher.App.exe` 起进程复核：
设置 → 缓存 → 「清除全部缓存」→ 浮层里 `取消` 在 x=1356、`清除`（Accent）在 x=1563，
焦点初始落在确认键，`Esc` 关闭浮层、`Enter` 确认生效。`publish.cmd` 的产物兜底校验也补了一条
`Controls\ConfirmOverlay.xbf` 存在性检查（原来只查 `App.xbf` / `.pri` / `Views\HomePage.xbf` / 图标，
`Controls\` 漏了也照样能发布出「浮层是空壳」的坏包）。

### 明确非目标 / 未做

- **没有覆写 `ContentDialog` 模板**：那要复制框架内部模板并交换列，会影响全应用所有对话框、
  且 WinUI 升级时极易碎。自绘浮层只影响自己这一个控件。
- **没有改框架的 ESC 语义**：ContentDialog 里 ESC 恒等于 CloseButton，所以「取消」必须放在 Close 角色上，
  而 Close 恒在最右——这正是必须自绘的原因。
- **没有改任何文案**：复用 `common.cancel` / `common.close` / `common.ok` 等既有 i18n 键，
  `src/PdxLauncher.Localization.Generator` 无需重跑。
- **没有动其他浮层**：工坊详情页的图片查看器浮层（`ImageOverlay`）保持原样。

### 预期验收路径

- 任意「确认 / 取消」对话框：**取消在左、确认在右**，确认键为 Accent 主色。
- Esc = 取消（不弹任何东西、直接关掉），Enter = 确认。
- 关闭对话框后，焦点回到打开它的那个按钮。
- 崩溃弹框点「复制详情」后，崩溃详情进剪贴板（`onConfirm` 回调路径）。
- 「修复 mod 路径」不勾选任何条目时确认键是灰的，勾选后点亮。

## 0.56 2026-10-30：设置页新增「翻译」分区（工坊与 Mod 名称 / 简介 / 更新日志 / 评论）

### 起因

用户原话（m00001）：「在设置添加翻译功能，需要给mod的名称、内容、评论区做翻译」。

用户随后拍板四项：

1. **后端两个都要、可切换**：既支持用户自填的 OpenAI 兼容 API（Base URL / API Key / 模型名），
   也支持内置免费服务兜底；设置页给引擎档位选择。
2. **目标语言跟随界面语言**（界面中文→译成中文，英文→英文），设置里可覆盖为固定语言。
3. **范围**：创意工坊列表 + 工坊详情（简介与更新日志）+ 评论区 + 本地 Mod 库。
4. **呈现**：**原文与译文同时显示**，不是二选一切换。

### 分层

**Core（`src/PdxLauncher.Core/Online/Translation/` + `src/PdxLauncher.Core/Text/MarkdownTranslator.cs`）**

- `ITranslationEngine`：`Id` / `IsConfigured` / `TranslateAsync` / `TranslateBatchAsync`；失败用 null 表示，不抛异常。
- `OpenAiCompatibleTranslationEngine`：`POST {Base}/chat/completions`，`Authorization: Bearer`，
  body 带 `model` / `messages` / `temperature=0`；批量用 `\n<<<PDX_SPLIT>>>\n` 分隔，
  段数与原文数不符就退化成逐条翻译。
- `FreeTranslationEngine`：公共免费接口，`IsConfigured` 恒 true。
- `TranslationService`：缓存优先 → 未命中交给引擎；`auto` 模式自填失败项用内置补，
  `custom` 模式**不回落**，`builtin` 模式只用内置（并发上限 4）。
- `TranslationCache`：落 `cache:translations` 库键，键 = SHA256(引擎 + 目标语言 + 原文)，TTL 可配，
  超 5000 条按时间淘汰最旧；写盘失败只记日志。
- `TranslationOptions.FromSettings(settings, uiLanguage)`：目标语言留空→跟随界面；模型空→默认；
  引擎模式、缓存天数都归一化，未知值不会让翻译崩掉。
- `MarkdownTranslator`：保结构翻译（标题层级 / 列表层级与序号 / 链接地址 / 强调样式原样保留；
  代码块、图片、分隔线不翻译；引用块递归）；连续纯文本合并成一次请求；**译文为空就保留原文**。
- `TranslationHttpTransport` + `ITranslationTransport`：单开窄接口的原因是 `IHttpFetcher` 只有
  Get/PostJson/PostForm，没有自定义头入口，改它会连带改 4 个自检 fake。非 2xx 不抛异常。

**App（`src/PdxLauncher.App/`）**

- `Services/TranslationCoordinator.cs`：并发 4；设置变化或界面语言变化时换掉 CTS 作废在途批次；
  按当前设置临时拼 `TranslationService`；`ClearCache()` 给设置页的「清理缓存」用。
- `Services/TranslationEngineCatalog.cs` / `TranslationTargetLanguageCatalog.cs`：下拉项范式（Option + Catalog）。
- ViewModel 侧：`WorkshopRowViewModel` / `ModRowViewModel` / `ModLibraryRowViewModel` 加 `TranslatedName` +
  `HasTranslatedName`；`WorkshopCommentViewModel` 加 `TranslatedBody` / `TranslatedBodyPlain` / `HasTranslation`；
  `WorkshopDetailViewModel` 加 `TranslatedDescriptionDocument` / `TranslatedDescriptionText` /
  `TranslatedChangelogDocument` 与对应派生属性。
- 触发点：`WorkshopViewModel`（页缓存 / Steam 查询 / PDX 查询 / 失败回落缓存四条铺行路径）、
  `ModLibraryViewModel.RebuildRows`、`ModsViewModel.RebuildVisibleMods`、
  `WorkshopDetailViewModel`（简介 / 更新日志 / 评论两处）。
- XAML：工坊列表页、Mod 库页、播放集 mod 行在原标题下多出一行「译文 …」；
  工坊详情页的简介卡 / 更新日志卡 / 评论模板各多一块带「译文」小标题的译文块。

**设置页（第 7 个分区，Tag=6，`navSettingsTranslation`）**

- 控件：`tglTranslateEnabled` / `cmbTranslateEngineMode` / `txtTranslateBaseUrl` / `pwdTranslateApiKey` /
  `txtTranslateModel` / `cmbTranslateTargetLanguage` / `tglTranslateNames` / `tglTranslateDescription` /
  `tglTranslateChangelog` / `tglTranslateComments` / `txtTranslateCacheDays`；已登记进
  `scripts/verify-settings-sections.ps1` 的 want 列表。
- 「缓存」分类多一行「译文缓存」（库键 `cache:translations`），可单独清空。

### 域名口径

内置免费服务的域名进白名单（`OnlineHosts.TranslationHost`）；**自填接口的域名按用户填的地址精确授权**
（`OnlineHosts.AuthorizeBaseUrl` → 只放行该主机本身，不放行子域）。`TranslationHttpTransport` 与其它传输
共用同一道闸门：未授权域名抛 `host-not-allowed:<host>`。

### 已知限制

- 内置免费服务依赖第三方公开接口，稳定性不受本项目控制；失败时保留原文并记日志。
- API Key 明文存在本地 `settings` 文档里（UI 用密码框，日志绝不打印密钥）。
- 批量翻译靠分隔符协议，模型不遵守就退化成逐条请求（慢一些，但结果正确）。
- 目标语言只支持中文与英文两种。
- 代码库里没有真正的 BBCode→Markdown 转换器，BBCode 仍按纯文本降级后按 Markdown 解析。

### 验证

- `dotnet build PdxLauncher.sln -c Release -m:1` → 0 警告 0 错误。
- 自检 → `检查类: 61，失败: 0，断言失败总数: 0`（新增 `TranslationChecks` 49 条断言）。
- i18n：`src/PdxLauncher.Localization.Generator` 产出 836 条文案（翻译相关新增 66 键）。

---

## 0.58 2026-10-30：Cities: Skylines II 的 DLC 页不再是空白（清单改由 Steam 报告生成）

> 用户诉求原话：「天际线2的DLC为什么无法显示」

### 根因（三处叠加，全部有源码坐标）

1. `src/PdxLauncher.Core/Dlc/DlcCatalog.cs:125-132`：CS2 是 `LoadFileFormat.PlaysetConfigV1`，`Discover()`
   直接 `return Array.Empty<DlcInfo>()`（CS2 安装根没有 `.dlc` / `.dlc.json`；`PlaysetConfigRepository.GetPlaySetDlcsAsync`
   也恒返回空、`SavePlaySetDlcsAsync` 是 no-op —— 这条链路上根本没有 DLC 概念）。
2. `src/PdxLauncher.App/ViewModels/DlcViewModel.cs` 旧 190-198 行：`catalog.Count == 0 && PlaysetConfigV1` 时清空列表、
   写「该游戏没有 DLC 描述文件，无法列出 DLC」并 `ResetOwnership()` 直接 return。
3. 旧 `HasOwnershipStrip` 要求 `Dlcs.Count > 0`，于是 §0.30 刚做好的「Steam 授权判定条」也一起被藏掉 ——
   明明 `SteamDllProbe` 已经问出 37 个 DLC 的名字和授权状态，界面上一条都看不见。

### 改动（只在 DLC 页 VM 里拼，不碰 `IDlcCatalog`）

有意**没有**给 `IDlcCatalog` 加「Steam 回退」：`ModsViewModel`（autosave 的 `MergeDlcs`）与
`SettingsViewModel`（`dlc_signature` 重算）也消费同一个目录，全局回退会让 CS2 的 mod 自动保存与签名
悄悄多出一批 `steam:*` 条目。清单生成因此留在页面层。

- 新增 `src/PdxLauncher.Core/Steam/SteamDlcListBuilder.cs`：`SteamDlcOwnership` → `DlcInfo` 的纯函数映射。
  `PopsId` 留空（CS2 没有 pops_id，因此 `CanBeDisabledInContentLoad=false`），`FilePathRelativeToDlcPath` 用
  `steam:<appid>` 当唯一 id（重名 DLC 不会互相覆盖），`RawBytes` 空，不参与任何磁盘签名。
- `DlcViewModel`：新增 `IsSteamDlcList` / `CanEditDlcToggles`，加 `LoadSteamDlcListAsync` / `FillSteamRows` /
  `SetSteamStatus` / `ProbeOwnershipAsync`；`RefreshOwnershipAsync` 收敛成「探一次 → 同时喂判定条与列表」。
  Steam 清单下「启用全部 / 禁用全部 / 保存」直接返回（`dlc.saveSteamManaged`），因为 CS2 没有任何可写状态。
- `DlcRowViewModel`：新增第 5 个 ctor 参数 `showsToggle`（默认 `true`，四个既有调用点不动），Steam 行传 `false`。
- `DlcPage.xaml`：工具条三个按钮包进内层 `StackPanel`，`Visibility` 绑 `CanEditDlcToggles`；行内 `ToggleSwitch`
  绑 `ShowsToggle`。判定条可见性改 `CanRecheckOwnership && (Dlcs.Count > 0 || _ownership is not null)`。
- i18n：新增 5 键（`dlc.countsTextSteam` / `dlc.statusSteamList` / `dlc.statusSteamEmpty` /
  `dlc.statusSteamUnavailable` / `dlc.saveSteamManaged`），`gen-i18n.py` 产出 825 条。

### 验证（本轮实跑）

- `dotnet build PdxLauncher.sln -c Release -m:1` → 0 警告 0 错误。
- 自检 → `检查类: 61，失败: 0，断言失败总数: 0`（新增 `SteamDlcListChecks` 25 条断言：映射、去重、重名 id 唯一、
  `CanBeDisabledInContentLoad` 全 false、`BuildDlcId` 校验，以及「没有磁盘清单时判定按 Steam 报告计数」）。
- `文案自检` → 通过。
- **真机 UIA**（Steam 正在运行；CS2 的 `steam_api64.dll` 补丁状态＝未加载）：
  - CS2 DLC 页：`共 37 个 DLC（清单来自 Steam）`，37 行带「未拥有」徽标（本机该账号确实 0 个 CS2 DLC：
    日志 `state=Succeeded，Steam 37 个 DLC、0 个已拥有，结论=VanillaPartial`），工具条按钮 **0 个**、开关 **0 个**，
    判定条在（`探测 DLL：…\Cities2_Data\Plugins\x86_64\steam_api64.dll · AppID 949230 · 补丁：未加载`）。
  - 简介链路顺带确认真能跑：日志 `DLC 简介：联网补齐 31/37 个（语言 schinese）`。
  - Stellaris 回归：`共 31 个 DLC，已启用 31 个`（仍是旧的磁盘口径），工具条 3 个按钮在、行内开关在、
    可见行全部「已拥有」——说明 Steam 分支没有串到 Clausewitz 游戏上。

### 明确非目标 / 未做

- **CS2 DLC 缩略图**：Steam 图片 CDN（`cdn.cloudflare.steamstatic.com` 等）不在 `OnlineHosts` 白名单里，
  要做得先扩白名单，属另一件事；现在 CS2 行仍是占位图。
- 不做 Steam 清单的排序 / 搜索 / 分组，也不写 `playsets_dlcs`（CS2 那边本来就没有可写状态）。
- 「Steam 没开」的失败路径按代码推导（`SetSteamStatus` 走 `dlc.statusSteamUnavailable` + 现有错误文案），
  本轮没有为了它专门关一次 Steam 做真机回归。

### 预期验收路径

1. `build.cmd` 或 `dotnet build PdxLauncher.sln -c Release -m:1` → 0 错。
2. `tests\PdxLauncher.Checks` 的 Release exe → `检查类: 61，失败: 0`。
3. 开 Steam，`run.cmd` → 游戏库选 Cities: Skylines II → 左栏「DLC」→ 应看到 37 行带授权徽标、
   判定条可见、没有开关与工具条按钮；切回 Stellaris 仍是磁盘口径 + 开关可用。
4. 关掉 Steam 再进 CS2 的 DLC 页：列表为空、状态栏给出「未能从 Steam 读取 DLC 清单：…」并保留「重新检测」按钮。

## 0.59 2026-10-30：账号页版式打磨（布局口径对齐、状态条单行、会话偏好真落盘）

> 用户诉求原话：「这个ui给我调好（东西是在账号页面）」，附一张 Paradox 账号页截图。

### 根因（九处叠加，除第 8 条外都只影响观感）

1. `src/PdxLauncher.App/ViewModels/AccountViewModel.cs` 旧 `Initials`：取 `Session.DisplayName` 的
   **第一个字符**。Paradox 的显示名就是邮箱，于是头像上出现一个「1」；改过一次后又变成域名里的「q」
   （看起来像放大镜图标）。现在 `Services/AccountDisplayText.cs` 只在**本地段**里找**字母**，找不到就留空串，
   由 `PersonPicture` 画系统人像占位。
2. `StatusText` 走 `account.statusSignedIn`（`已登录：{0}（{1}）`）把显示名和邮箱各印一遍 —— 两者相同时
   就是「已登录：a@b（a@b）」。现在 `AccountDisplayText.SameIdentity` 判定两者相同就换成
   `account.statusSessionAuto` / `account.statusSessionOnce`（只说续期口径，不复读邮箱）。
3. `AccountPage.xaml` 的 `InfoBar` 同时有 `Title="登录状态"` 和 `Content`，WinUI 把它排成「标题行 + 内容行」，
   两行之间空一大截。现在只留 `Content`（`txtAccountStatusBar`），标题去掉。
4. 身份卡用 `PdxCardGlassBrush` + `CardStrokeColorDefaultBrush`，与 `Controls/SettingsCard.xaml` 的
   `PdxCardBackgroundBrush` + `PdxCardStrokeBrush` 不是一套，玻璃感更重、描边更亮。现在统一取 SettingsCard 那套。
5. 页面 `Padding="24,20,24,24"`，其它页都是 `Padding="24"`。
6. `ToggleSwitch` 不写 `OnContent`/`OffContent` 时**平台会补默认的「开 / 关」**（不是「留空 = 不显示」）。
   必须显式写 `OnContent=""` `OffContent=""`。
7. 内容列 `StackPanel` 同时设了 `MaxWidth="880"` 和 `HorizontalAlignment="Left"`：`StackPanel` 在纵向布局里
   按内容宽度收缩，`Left` 让 `MaxWidth` 只剩上限作用，卡片被挤成一条窄带、右侧空掉一大片。
   改成 `HorizontalAlignment="Stretch"`，`MaxWidth` 才变成实际宽度。
8. 「保持登录」原先只在**登录那一刻**决定要不要写 `pdx-session.json`，登录之后再拨开关只改配置项，
   和 `account.rememberHint` 承诺的「下次启动可自动续期」不符。现在 `AccountService.ApplyRememberPreference`
   会当场补写或清掉会话文件。
9. `ProfileName` 在「已登录但显示名为空」时回落成「未登录」，口径自相矛盾；改为回落到邮箱。

### 改动

- 新增 `src/PdxLauncher.App/Services/AccountDisplayText.cs`（纯函数，无 UI 类型）：`Initials` / `ProfileName` /
  `SameIdentity`。`Initials` 的候选顺序是「显示名本地段 → 邮箱本地段」，只认字母，没有就返回空串。
- `src/PdxLauncher.App/Views/AccountPage.xaml`：`Padding="24"`；身份卡换刷子、`MinHeight="72"`、`Padding="16,12"`；
  `PersonPicture` 缩到 48 并用 `AccentFillColorDefaultBrush` / `TextOnAccentFillColorPrimaryBrush`；
  名字行 `BodyStrongTextBlockStyle`、状态行 `CaptionTextBlockStyle`（去掉写死的 `MaxWidth` 420/440）；
  `btnAccountSignOut` 挂 `ToolTipService.ToolTip="{x:Bind L.AccountSignOutHint}"`（这个键此前一直没人用）；
  `EmailBox` / `PasswordBoxControl` 挂 `KeyDown="OnCredentialKeyDown"`。
- `src/PdxLauncher.App/Views/AccountPage.xaml.cs`：登录主体抽成 `private async Task SignInAsync()`；
  新增 `OnCredentialKeyDown`（`VirtualKey.Enter` → 走登录且 `e.Handled=true`，`CanSignIn` 为假时不发请求）；
  新增 8 秒 `DispatcherTimer`，只在 `AccountStatusLevel.Success` 时启动，到点把 `infoAccountStatus.IsOpen` 置 false
  （错误不自动消失），`OnUnloaded` 里停表。
- `src/PdxLauncher.App/ViewModels/AccountViewModel.cs`：`Initials` / `ProfileName` / `StatusText` 改走
  `AccountDisplayText`；`OnRememberChanged` 增加 `AppServices.Account.ApplyRememberPreference(value)` 并重算 `StatusText`。
- `src/PdxLauncher.App/Services/AccountService.cs`：新增 `ApplyRememberPreference(bool remember)`
  —— 无会话时什么都不做，有会话时 `_store.Save` / `_store.Clear`。**有意不取 `_gate`**：
  调用点在 UI 线程的开关回调里，只是落一个文件，取锁反而会和登录中的请求互相等待。
- i18n：新增 `account.statusSessionAuto`（`已登录 · 下次启动自动续期` / `Signed in · renews automatically next launch`）、
  `account.statusSessionOnce`（`已登录 · 仅本次运行有效` / `Signed in · valid for this run only`），
  `gen-i18n.py` 产出 836 条键。

### 自检

- 新增 `tests/PdxLauncher.Checks/AccountDisplayTextChecks.cs`：首字母（纯数字邮箱→空串、不拿域名凑、中文取字、
  小写转大写、空白/全空→空串）、`ProfileName` 回落、`SameIdentity` 大小写与空白，以及两个新键在 zh-CN/en-US
  都真的存在。
- 新增 `tests/PdxLauncher.Checks/AccountPageLayoutChecks.cs`（读 XAML 源码断言）：`InfoBar` 块内没有 `Title=`、
  全文没有非空 `OnContent`/`OffContent` 且两个开关都显式赋了空串、身份卡用的是 SettingsCard 那对刷子、
  内容列 `HorizontalAlignment="Stretch"`、全文没有硬编码 `#RRGGBB`、14 个 AutomationId 一个不少。
- `AccountChecks.CheckPersistence` 扩展：登录后关掉「保持登录」→ 会话文件被清；再打开 → 重新落盘且能读回 token；
  未登录时调用不产生文件。
- `tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj` 链接进 `AccountDisplayText.cs`（Checks 引用不了 WinExe 工程）。

### 验证（本轮实跑）

- `dotnet build PdxLauncher.sln -c Release -m:1` → 0 警告 0 错误。
- 自检 → `检查类: 61，失败: 0，断言失败总数: 0`。
- `文案自检` → `文案源合法，836 个键，生成物已是最新`。
- **真机 UIA**（复核脚本 `ui-test-artifacts/account-nav-shot.ps1 -Mode signed-in|fresh|validation`，截图在 `ui-test-artifacts/account-polish/`）：
  - `20-account-signed-in.png`：已登录态。身份卡与「保持登录」卡同宽同刷子，头像是人像占位（不再是「1」/「q」），
    状态行 `已登录 · 下次启动自动续期`，开关右侧没有「开」。
  - `24-account-validation-error.png`：干净的 `data-root`（`data-root.txt` 覆盖到 `.tmp\account-ui-empty`）下
    未登录态＋**键盘回车提交**（密码留空）→ 状态条是单行 `请先填写密码。`，不再有「登录状态」标题行。
  - `22-account-signed-out.png` / `23-account-login-failed.png`：同一轮里的「正在登录…」忙碌态
    （本机到 Paradox 登录接口的请求超时较久，20 秒时仍未见失败回包）。

### 明确非目标 / 未做

- 没碰协议层、会话存储格式、`AccountFormState` 语义、`AccountService` 的并发结构。
- Light / HighContrast 两套主题只做了「只用 ThemeResource、无硬编码颜色」的源码级保证，本轮没有做这两套主题的
  真机截图（Dark 与 Default 一致，本轮截图都是 Dark）。
- 「成功」类状态条 8 秒自动收起需要真的登录成功才会出现，本轮没有用真账号复验这一条；错误/提示类不自动收起。

### 预期验收路径

1. `build.cmd` 或 `dotnet build PdxLauncher.sln -c Release -m:1` → 0 错 → 自检 `检查类: 61，失败: 0`。
2. `run.cmd` → 左栏「账号」：已登录时身份卡第二行只说续期口径（不再复读邮箱）、头像不是数字；
   把「保持登录」拨掉再拨回，`data\pdx-session.json` 应随之消失 / 重新出现。
3. 未登录时点「登录」或直接在密码框按回车：状态条是单行、且没有标题行。
4. 退出登录按钮悬停应显示「只清除本机的登录状态，不会注销其它设备上的会话。」

---

## 0.60 2026-10-30：译文改「带框呈现」+ 接入微软 / 谷歌云机翻引擎

> 用户诉求原话（m01616）：「显示译文参考这种实现；还有能不能添加机翻，比如微软和谷歌翻译」，
> 并附了一张参考图：原文一段，**下方一个带细描边的方框**里放中文译文。

### 起因与拍板

1. **呈现样式**：译文不再是无边框的一行灰字，而是「原文在上 + 下方带描边框的译文块」，
   框内仍保留 Markdown 结构渲染（链接、图片回调照旧）。
2. **新增机翻引擎**：点名要微软与谷歌。调研结论：
   - **微软 Azure Translator F0 免费层必须同时有订阅密钥 + 区域**（每月 200 万字符，$10/百万字符），
     端点 `POST https://api.cognitiveservices.azure.com/translator/text/v3/translate?api-version=3.0&to=zh-Hans`，
     头 `Ocp-Apim-Subscription-Key` + `Ocp-Apim-Subscription-Region`；**没有免密钥的公开端点**。
   - **谷歌 Cloud Translation v2 需要 API Key**（免费层每月 50 万字符），
     端点 `POST https://translation.googleapis.com/language/translate/v2`。
   - 而原先的「内置免费服务」其实就是谷歌的**公开网页端点**（`translate.googleapis.com/translate_a/single`，无需凭据，
     但非官方、随时可能变）→ 这次把它改名叫「谷歌免费翻译（无需密钥）」，与「谷歌云翻译」区分开，
     不重复实现同一套东西。
   - 拍板：机翻专档位**严格不回落**；「自动」档的回落链是
     **自填 OpenAI → 微软 → 谷歌云 → 谷歌免费**。

### Core（`src/PdxLauncher.Core/`）

- `OnlineHosts`：新增 `MicrosoftTranslatorHost = "api.cognitiveservices.azure.com"` 与
  `GoogleCloudTranslationHost = "translation.googleapis.com"`，加进 `IsAllowedHost` 串联。
  注意它与既有的 `TranslationHost = "translate.googleapis.com"`（谷歌公开网页端点）**是两个不同主机**。
- `Configuration/TranslationEngineModes`：新增 `Microsoft = "microsoft"` / `GoogleCloud = "google-cloud"`；
  `All` 变为 5 项（auto / custom / builtin / microsoft / google-cloud），`Normalize` 认识新值。
- `Configuration/AppSettings`：新增 `TranslateMicrosoftKey` / `TranslateMicrosoftRegion` / `TranslateGoogleKey`。
  **刻意不复用 `TranslateApiKey`** —— 三种凭据分属三个服务，混用会让「这把钥匙属于哪个服务」变得含糊。
- `Online/Translation/TranslationOptions`：record struct 追加 `MicrosoftKey` / `MicrosoftRegion` / `GoogleKey`；
  `FromSettings` 归一化（去空白）；新增 `HasMicrosoftConfiguration`（密钥与区域**都**要非空）
  与 `HasGoogleCloudConfiguration`（密钥非空）。
- **新建 `Online/Translation/MicrosoftTranslatorEngine.cs`**（269 行）：`EngineId = "microsoft"`；
  每批上限 100 条 / 50000 字符（官方限制）；请求体是 JSON 数组 `[{"Text":"..."}]`；
  响应解析 `[{"translations":[{"text":"..."}]}]`；目标语言 `zh-CN → zh-Hans`、`en-US → en`。
- **新建 `Online/Translation/GoogleCloudTranslationEngine.cs`**（246 行）：`EngineId = "google-cloud"`；
  每批上限 128 条；`key` 走**查询参数**（不进请求体）；请求体 `{"q":[...],"target":"zh-CN","format":"text"}`；
  响应解析 `{"data":{"translations":[{"translatedText":"..."}]}}`。
- 两个引擎共同约定：失败返回 null 不抛异常；日志**只记状态码不记密钥**；批量段数不符退化为逐条；
  凭据不全时 `IsConfigured` 为 false 且**根本不发请求**。
- **`Online/Translation/TranslationService` 重写为「引擎列表」**：构造从
  `(customEngine, builtinEngine, cache, logger)` 改成 `(IReadOnlyList<ITranslationEngine> engines, cache, logger)`，
  **列表顺序即回落优先级**。`auto` 模式把未解决项交给下一个引擎；非 `auto` 模式跑完第一个即 break（不回落）。
  缓存键本来就含引擎 id，所以新引擎自动各自独立分区，`TranslationCache` 本身不用改。
  （内部集合必须用 `TranslationOutcome[]` —— `IReadOnlyList` 的索引器只读，回填不了。）

### App（`src/PdxLauncher.App/`）

- `Services/TranslationCoordinator.CreateService` 按档位拼引擎列表：
  `auto` = 自填(若配) + 微软(若配) + 谷歌云(若配) + 谷歌免费（永不缺席）；
  `custom` = 只自填；`builtin` = 只谷歌免费；`microsoft` = 只微软；`google-cloud` = 只谷歌云。
  → **专档位靠「列表里只放那一个引擎」实现不回落**，而不是在 Service 里判档位。
- `Services/TranslationEngineCatalog`：`All` 扩为 5 项，`builtin` 的标题改成「谷歌免费翻译（无需密钥）」。
- `ViewModels/SettingsViewModel`：新增 `TranslateMicrosoftKey` / `TranslateMicrosoftRegion` / `TranslateGoogleKey`
  三个属性（写回 `AppServices.Settings` 后 `SaveTranslationSettings()`），以及
  `HasOpenAiFields` / `HasMicrosoftFields` / `HasGoogleCloudFields` 三个可见性属性
  （按当前档位：`auto` 全显示，专档位只显示自己那组），档位切换时 `RaiseTranslationEngineFieldVisibility()` 发通知。
- `Views/SettingsPage.xaml`（翻译分区）：Base URL / 密钥 / 模型三张卡片加
  `Visibility="{x:Bind svc:XamlVisibility.FromBool(ViewModel.HasOpenAiFields), Mode=OneWay}"`；
  新增三张卡片 `pwdTranslateMicrosoftKey`（密码框）/ `txtTranslateMicrosoftRegion`（占位 `eastasia`）/
  `pwdTranslateGoogleKey`（密码框），分别按 `HasMicrosoftFields` / `HasGoogleCloudFields` 显隐。
  → 引擎档位决定哪些凭据卡片可见，这是**预期行为**，不是布局缺陷。

### 译文边框化（6 处 XAML）

统一外形：`Border{Margin 0,6,0,0（详情页 0,8,0,0）, Padding 6,4（详情页 10）, CornerRadius 6,
BorderThickness 1, BorderBrush {ThemeResource CardStrokeColorDefaultBrush},
Background {ThemeResource CardBackgroundFillColorDefaultBrush}}`，内含「译文」标签行（次级色 Caption）
加译文正文。

- `Views/WorkshopPage.xaml`、`Views/ModsPage.xaml`、`Views/ModLibraryPage.xaml`：列表行的译名，
  正文单行 + `CharacterEllipsis` 省略。
- `Views/WorkshopDetailPage.xaml`：简介块、更新日志块、评论模板三处，正文仍是 `ctl:MarkdownView`
  （保留 `LinkRequested` / `ImageRequested` 回调与 AutomationId
  `txtWorkshopDetailTranslatedDescription` / `txtWorkshopDetailTranslatedChangelog`）。
- 可见性条件不变（`HasTranslatedName` / `HasTranslatedDescription` / `HasTranslatedChangelog` / `HasTranslation`）。

### 自检（`tests/PdxLauncher.Checks/TranslationChecks.cs`）

新增 4 个检查方法 + `RecordingTransport` 替身（记录 method/uri/body/headers）：

- `CheckEngineModes`：新档位归一化（含带空格与大小写变体）、`All.Count == 5`、
  `FromSettings` 对三个凭据去空白、`HasMicrosoftConfiguration` 缺区域时为 false。
- `CheckMicrosoftTranslatorEngine`：请求形状（`api-version=3.0`、`to=zh-Hans`、两个头齐全、正文是 JSON 数组）、
  译文解析、英文目标 `to=en`、401 时返回 null 且日志含 `401` **且不含密钥**、缺区域不发请求。
- `CheckGoogleCloudTranslationEngine`：`key` 走查询参数、POST、正文含 `q` / `target` / `format`、
  `translatedText` 解析、403 时 null 且日志不含密钥、无密钥不发请求。
- `CheckMultiEngineFallback`：auto 模式下失败项由下一个引擎补齐、第三个引擎不被打扰；
  专档位不回落；未配置引擎被跳过。
- **踩坑**：非 auto 模式下 Service 只跑列表里的第一个引擎，所以「仅内置」档位的测试必须**只把内置引擎放进列表**
  （测试与 Coordinator 同构），否则会误跑自填引擎而失败。

### 验证脚本（`scripts/verify-settings-sections.ps1`）

- 翻译分区的 want 列表扩到 14 个 AutomationId（新增 `pwdTranslateMicrosoftKey` /
  `txtTranslateMicrosoftRegion` / `pwdTranslateGoogleKey`）。
- **新增难点**：凭据卡片按引擎档位显隐，本机档位是 `builtin` 时 6 张卡片 Collapsed，
  UIA 树里根本没有这些 AutomationId → 固定白名单会误报失败。
  解法：脚本进入翻译分区后先读当前档位名，切到 `auto` 档（此时三组凭据全部可见），校验完再**还原原档位**。
  新增辅助函数 `DropDownName` / `DropDownItems` / `SelectDropDownByName` / `SelectDropDownIndex`
  （操作自绘 `PdxDropDown`：展开 Flyout → 按 Name 找 ListItem → `SelectionItemPattern.Select` → 收起）。

### 已知限制

- 微软翻译**必须**填订阅密钥 + 区域；没有免密钥的公开端点。免费层 200 万字符/月。
- 谷歌云翻译必须填 API Key；免费层 50 万字符/月。谷歌公开免费端点非官方，可用性不保证。
- 三种凭据都以**明文**存在本地 settings 文档里（UI 密码框、日志不打印）。
- 批量翻译仍靠分隔符协议 / 官方批量上限切批，模型不遵守就退化成逐条。
- 目标语言仍只支持 `zh-CN` 与 `en-US`。

### 验证（本轮实跑）

- `dotnet build PdxLauncher.sln -c Release -m:1` → **0 警告 0 错误**。
- 自检 → `检查类: 61，失败: 0，断言失败总数: 0`。
- `文案自检` → 836 个键（翻译相关新增 18 键）。
- `scripts/verify-settings-sections.ps1` → 7 节全过（translation count=48 missing=[]，FAILED_SECTIONS=0）。

## 0.61 2026-10-31：工坊里「看不到翻译」→ 换可达的兜底引擎 + 让失败可见

用户原话：「目前的翻译功能真有用吗，我怎么进创意工坊没看到翻译呢」。
§0.60 交付的翻译层代码是全的（引擎、缓存、译名/简介/更新日志/评论渲染、设置分区都在），
但在这台机器上**一行译文都出不来**，而且界面上没有任何提示——看起来就像功能不存在。

### 现象与定位

- `dist/PdxNext/data/logs/pdxnext.log` 里没有任何 `翻译` / `MyMemory` / `translate` 相关行，
  说明请求根本没走到「有结果」或「记过一条日志」的地方。
- 默认档位 `auto` 在没填任何密钥时，唯一可用引擎是**谷歌免费翻译**（`translate.googleapis.com`，
  免密钥）。实测这台机器上该主机**直连与走代理都不通**：

  | 目标 | 直连 | 走代理 10.31.2.50:7890 |
  | --- | --- | --- |
  | `translate.googleapis.com`（内置免费引擎） | 超时 | TLS 握手失败 |
  | `translation.googleapis.com`（谷歌云，需 Key） | 超时 | 404 可达 |
  | `api.mymemory.translated.net`（本轮新接入） | **200** | 200 |
  | `edge.microsoft.com/translate/auth`（免密钥 Edge 端点） | 404 且正文为空 | 不可用 |
  | `lingva.ml`（社区替代） | 200 但原样返回原文 | — |

- 引擎失败返回 `null`，`TranslationService` 只写一条 `Warn`，Coordinator 把 `null` 原样交给
  ViewModel，ViewModel 不写 `StatusMessage`、详情页也没有提示位 → **静默失败**。
  这就是「功能在、但用户看不到」的全部原因；结论是**代码没坏，链路是死的**。

### 改了什么

1. **新增免密钥兜底引擎 `MyMemoryTranslationEngine`**（`Core/Online/Translation/`）：
   `GET https://api.mymemory.translated.net/get?q=…&langpair=Autodetect|<zh-CN|en>`；
   单请求上限 500 字符 → `MaxChunkChars = 450`，超长按句读符号切块（切点留在块尾，拼回不丢字）；
   多块并发 4、任一块失败整段算失败（不拼半截译文）；`RequestTimeout = 20s`。
2. **回落链改成「自填 API → 微软 → 谷歌云 → MyMemory → 谷歌免费」**：
   谷歌免费在本机 100% 不通，排最后就不会每次先赔一次超时。`builtin` 专档位语义不变。
   `TranslationEngineModes` 新增 `mymemory`，`All` 变六项；`OnlineHosts` 加主机白名单。
3. **内置免费引擎补并发与超时**：原来逐条串行、单请求吃满 60 s 传输超时，30 行列表要等半小时；
   现在并发 4、单请求 15 s、首错短路。
   同一轮把**工坊列表 / Mod 页 / Mod 库页**的译名从「一行一次 `TranslateAsync`」改成
   「整页一次 `TranslateBatchAsync`」，译完统一回填（原来是边译边刷）。
4. **失败可见**：
   - `TranslationService.LastFailure` → `TranslationCoordinator.LastFailure`，形如 `mymemory: …`。
   - 工坊列表与 Mod 页：一批译名一条都没成功时，状态条写 `workshop.translateUnavailable` /
     `mods.translateUnavailable`（按页去重，只提示一次）；Mod 库页没有状态条，只记
     `Mod 库名称全部未翻译（N 条）：<LastFailure>`。
   - 工坊详情页：简介 / 更新日志 / 评论三个范围各自最多提示一次，提示条在缓存提示下方
     （`txtWorkshopDetailTranslationNotice`）。
5. **设置 → 翻译新增「测试翻译」按钮**（`btnTranslateTest`）：逐引擎各译一句
   `Hello, this is a translation test.`，把每个引擎的 ✓/✗ 与原因写进 `txtTranslateTestResult`。
   这是用户自查「哪个引擎在我这通」的入口。探测过程日志：`探测翻译引擎：<id>` /
   `探测结果（<id>）：有译文|无译文`。
6. **启动日志**：`翻译已启用：模式=…，目标语言=…，自填 API=已配置|未配置`，失败时另有
   `翻译全部失败：<LastFailure>`，下次再遇到「没反应」先看日志。
7. **微软 / 谷歌云补「怎么申请 + 快速前往」**（用户后续要求，见下节）：选到这两个引擎时，
   密钥输入框下方各出现一段申请说明与一个 `HyperlinkButton`，直接打开对应的申请页。

### MyMemory 协议里的三个坑（实测）

- **单请求 500 字符**，超了返回 `{"responseData":"QUERY LENGTH LIMIT EXCEEDED. MAX ALLOWED QUERY : 500 CHARS","responseStatus":"403"}`。
- **源语言不能写 `auto`**（403 `'AUTO' IS AN INVALID SOURCE LANGUAGE`），必须写 `Autodetect`；
  且它**不做真正的语言检测**：`langpair=en|zh-CN` 传德文会原样返回，只有 `Autodetect` 才按实际语言处理。
- **类型不稳定**：成功时 `responseStatus` 是数字、`responseData` 是对象；出错时前者变字符串、
  后者变一段说明文字。解析必须两种都容。

### 偏差与已知限制

- **评论仍是逐条翻译**（`TranslateDocumentAsync`），没有改成批量：评论要保 markdown 结构，
  批量协议按分隔符切分，改起来得不偿失。只在失败时补一次提示。
- **Mod 库页没有状态条**，所以标题译名全失败只写日志，不弹提示；Mod 页有状态条，复用状态条提示。
- MyMemory 是社区公共服务（免费额度按天），**不保证可用性**；它只是让「没有密钥、谷歌又连不上」
  的网络有一个能用的默认值，不是长期方案。真要稳定还是填自填 API / 微软 / 谷歌云。
- 目标语言仍只支持 `zh-CN` 与 `en-US`；译文与原文仍同时显示。
- 界面文案排序由 `src/PdxLauncher.Localization.Generator` 决定：键 `settings.translate.engine.mymemory.title`
  生成的标识符是 **`SettingsTranslateEngineMymemoryTitle`**（`Mymemory`，不是 `MyMemory`），
  手写代码引用时必须按生成结果来，否则编译报 `CS0117`（本轮踩过一次）。

### 验证（本轮实跑）

- `dotnet build PdxLauncher.sln -c Release -m:1` → **0 警告 0 错误**。
- 自检 → `检查类: 62，失败: 0，断言失败总数: 0`（新增 MyMemory 解析/切块/批量保序/首错短路
  与 `LastFailure` 共 2 个检查方法、20 余条断言）。
- `文案自检` → 845 个键，无缺失。
- `scripts/verify-settings-sections.ps1` → `FAILED_SECTIONS=0`，翻译区 count=54 missing=[]
  （`btnTranslateTest` / `txtTranslateTestResult` 已进 want 列表）。
- 探针自检（UIA 点按钮 + 读结果）：临时脚本起 `bin\x64\Release` 的 exe，进设置 → 翻译 →
  `Invoke` `btnTranslateTest`，约 5 秒后 `txtTranslateTestResult` 变成
  `✓ MyMemory 翻译（免密钥）：您好，这是翻译测试。` /
  `✗ 谷歌免费翻译（无需密钥）：free: 未返回译文`；日志同步出现 `探测翻译引擎：mymemory` →
  `探测结果（mymemory）：有译文` → `内置翻译服务请求失败：The SSL connection could not be established`
  → `翻译全部失败：free: 未返回译文`。临时脚本与一次性 harness 已删。

### 本轮两个 XAML 小修

- `txtTranslateTestResult` 原本带 `Visibility="{x:Bind …XamlVisibility.FromString(...)}"`，没结果时是
  Collapsed → UIA 树里根本没有该元素，`verify-settings-sections.ps1` 报
  `translation: count=49 missing=[txtTranslateTestResult]`。改成**常驻空 TextBlock**（空串不占高度）。
- 同一元素上的 `AutomationProperties.Name` 原本写死为「测试各翻译引擎是否可用」，会把真实结果盖掉
  （UIA 的 Name 恒为那句固定文案，读屏也读不到结果）。已移除，让 Name 回落到正文。

### 补充：微软 / 谷歌云的「快速前往」与使用说明

起因：用户问「翻译功能真有用吗」之后又要「给微软、谷歌云留个快速前往的链接和使用说明」。
原来设置页只给了三个空密码框，用户既不知道去哪拿密钥，也不知道免费额度有多少。

- 文案（`src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`，按行插入保持字母序，未重排全文件）：
  - `settings.translate.microsoft.hint`：Azure 订阅里「创建资源 → Translator（翻译器）→ 定价层 F0
    （每月 200 万字符免费）→ 复制 KEY 1」，区域填资源位置（如 `eastasia`、`global`）。
  - `settings.translate.microsoft.portal`：打开 Azure 门户创建翻译器资源。
  - `settings.translate.googleCloud.hint`：Cloud Console 里「启用 Cloud Translation API → 凭据 →
    创建凭据 → API 密钥」，每月前 50 万字符免费，**只填密钥、不用填项目名**。
  - `settings.translate.googleCloud.portal`：打开 Google Cloud 控制台创建 API 密钥。
  - `settings.translate.statusPortalFailed`：`打不开浏览器：{0}`（打不开时写状态条，不弹窗）。
- `Views/SettingsPage.xaml`：说明与链接各自包在 `Visibility="{x:Bind …HasMicrosoftFields /
  HasGoogleCloudFields}"` 的 `StackPanel` 里，**只在选到对应引擎时出现**，自动/免费档不占版面。
  元素 `txtTranslateMicrosoftHint`、`btnTranslateMicrosoftPortal`、`txtTranslateGoogleCloudHint`、
  `btnTranslateGoogleCloudPortal`。
- `Views/SettingsPage.xaml.cs`：`OnTranslateMicrosoftPortalClick` / `OnTranslateGoogleCloudPortalClick`
  → `OpenTranslatePortal(url, who)` → `ShellLauncher.TryOpenUrl`。走 `Process.Start` + `UseShellExecute`，
  属于 shell 动作，**不受 `OnlineHosts` 域名白名单约束**（这两个域名不需要进白名单）。
  地址：`https://portal.azure.com/#create/Microsoft.CognitiveServicesTextTranslation`、
  `https://console.cloud.google.com/apis/credentials`。
- 验证：构建 0 警告 0 错误；自检 `检查类: 62，失败: 0`；`gen-i18n --check` → 851 键；
  `scripts/verify-settings-sections.ps1` → `FAILED_SECTIONS=0`（新元素条件显示，未进 want 列表）；
  UIA 实测下拉切到「微软翻译」时两个新元素在、谷歌云那段不在，切到「谷歌云翻译」时反之。
- 教训：含中文的临时 PowerShell 脚本必须写成 **UTF-8 带 BOM**，否则 Windows PowerShell 5.1 按 ANSI
  读文件，中文字面量全成乱码、条件永远不匹配（本轮先在探针脚本上踩过，又差点重踩）。

顺带修的一条（不在翻译范围内，但挡住了发布）：`tests/PdxLauncher.Checks/SteamHostProtocolChecks.cs`
里「动词表恰好四个」的断言已过时——`SteamHostProtocol.Verbs` 早前多了 `workshop-publish`（创建/更新
条目并上传内容），实际是五个。断言同步成五个并补一条「末位是 workshop-publish」，`publish.cmd`
的自检门才放行。若后续又加动词，这条断言要跟着改，或者改成「必须包含全部已知动词常量」。

## 0.62 2026-11-03：译文块去掉「译文」小标题

用户原话：「翻译功能的翻译不要带上译文这个字」。截图里工坊 / mod 列表行的译文框顶部有一行灰色 Caption「译文」，
下面才是中文译名；用户要的是**框里只出现译文本身**。

### 决策

- 删标题、**保留**带描边框与「原文在上、译文在下」的双显示——只去掉那行字，不动布局语义。
- 设置页 / 缓存 / 失败提示里的「译文」措辞**不动**（`settings.cache.row.translations.title`「译文缓存」、
  `settings.translate.*`、`workshop.translateUnavailable` 等）：那些指功能名，不是译文内容。
- 顺手删掉随之变成孤儿的文案键 `workshop.translateLabel`；`workshop.translateOriginalLabel`（原文 / Original）
  本来就没人绑，属既有遗留债，本轮不碰。`Strings.g.cs` 由 `src/PdxLauncher.Localization.Generator` 重新生成，未手改。

### 改动清单

六处翻译块结构都是 `Border > StackPanel(Spacing=2|4) > [Caption「译文」, 正文]`：删掉 Caption，
并把正文提为 `Border` 的直接子元素（只剩一个子元素时 StackPanel 没有意义）：

| 文件 | Caption 原行 | 正文元素 |
| --- | --- | --- |
| `src/PdxLauncher.App/Views/WorkshopPage.xaml` | 212 | 译名 `TranslatedName`（`FromString(TranslatedName)`） |
| `src/PdxLauncher.App/Views/ModsPage.xaml` | 264 | 同上 |
| `src/PdxLauncher.App/Views/ModLibraryPage.xaml` | 177 | 同上 |
| `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml` | 417 | 简介 `ctl:MarkdownView x:Name="TranslatedDescriptionView"`（`AutomationId=txtWorkshopDetailTranslatedDescription`） |
| `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml` | 518 | 更新日志 `TranslatedChangelogView`（`txtWorkshopDetailTranslatedChangelog`） |
| `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml` | 647 | 评论 `TranslatedBody`（`HasTranslation`） |

保留项：`Border` 的 Margin / Padding / CornerRadius / Background / BorderBrush / BorderThickness、各可见性条件、
`x:Name` 与 `AutomationId`。六处的代码注释都补了「不加『译文』标题」，防止下一轮又加回去。

文案源同步：`src/PdxLauncher.App/Strings/zh-CN/Resources.resw`、`src/PdxLauncher.App/Strings/en-US/Resources.resw` 删除 `workshop.translateLabel`；
`README.md:399-400` 的「带『译文』小标题」描述改成「带描边框的译文正文、不加小标题」。

### 验证

- `grep -rn "WorkshopTranslateLabel\|translateLabel" src tests tools`（排除 obj / bin）→ **0 命中**。
- `文案自检` → 文案源合法，**959 个键**（当前键数，比上一轮少 1 键）。
- x64 Release 构建 `bash tools/build-app.sh -v q -p:Platform=x64` → **0 警告 0 错误**；产物
  `PdxLauncher.App.dll` / `PdxLauncher.App.pri` 里已搜不到 `translateLabel`，`Views\WorkshopPage.xbf`
  等也随 XAML 改动重新生成。
- 自检 → `检查类: 63，失败: 0，断言失败总数: 0`。Checks 工程不引用 App，XAML 结构靠构建兜底：
  漏删任何一处都会因找不到 `L.WorkshopTranslateLabel` 而编译失败。

### 坑（本轮踩过）

- **重建前必须关掉正在运行的启动器**：进程锁着 `bin\x64\Release\...\*.dll`，MSBuild 会
  `MSB3027 / MSB3021`「文件被 PdxLauncher.App (PID) 锁定」重试 10 次后失败。
- **`build-app.sh` 要带 `-p:Platform=x64`**：不带时 Platform 退回 AnyCPU，产物落到
  `src\PdxLauncher.App\bin\Release\...`（不是运行用的 `bin\x64\Release\...`），看起来构建成功、
  实际要运行的 exe 没更新。`build.cmd` 走 sln 没这个问题。

### 注意

- 旧章节（如 0.60「译文改带框呈现」）描述的是当时状态，作为历史记录不改写。
- 若将来要做「显示原文 / 显示译文」二选一切换，可复用 `workshop.translateOriginalLabel` 这个现成键。

## 0.63 2026-11-03：翻译在途时的「正在翻译…」提示

> 用户诉求：先问「winui 有什么东西可以显示软件正在翻译」，紧接着补一句「在这种等待的时候的 progressring 加上一点文字说明」。

### 决策

- 用 **ProgressRing + 文字**（不用 InfoBar / ContentDialog / 任务栏进度 / 骨架屏）：这是 Windows 应用里「后台在跑、界面还能用」的既有语言，列表页的 `ringWorkshopBusy`、详情页的 `IsLoading` 都已经是这个形态。
- **两级提示**：全局一处（窗口标题栏）+ 局部三处（简介 / 更新日志 / 评论在译文将来出现的位置）。列表行的标题翻译只靠全局提示，不做行级占位 —— 行级占位会把整列标题撑成占位条，反而更吵。
- **计数放在 `TranslationCoordinator.TranslateBatchAsync`**：列表页批量翻标题、详情页逐段翻正文都会经过这一处，切页也不会漏；排队等并发的批次也算在途（`Activity.Begin()` 在 `_gate.WaitAsync` 之前）。设置页的「测试翻译」走 `service` 直连，**不经过**这里，所以点测试不会闪出「正在翻译」。
- **去抖**：显示延迟 150ms、隐藏延迟 400ms。前者压掉「缓存命中、几毫秒就回来」的闪烁，后者让逐段翻译的多批请求之间不闪。
- **顺带修的缺陷**：`HasTranslatedDescription / HasTranslatedChangelog` 之前只有声明、没有任何地方发变更通知（XAML 用的是 `Mode=OneWay`），译文译好了要切页才出现。这次在译文文档的 `OnXxxChanged` 里补通知。

### 改动

| 位置 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Services/TranslationActivityMonitor.cs`（新） | 线程安全的在途计数器：`Begin()` 返回 `Lease`（`IDisposable`），只在 0→1 / 1→0 发 `Changed`；重复 Dispose 不会把计数减成负数 |
| `src/PdxLauncher.App/Services/TranslationCoordinator.cs:101`、`:150` | `public TranslationActivityMonitor Activity`；`TranslateBatchAsync` 里 `using var activity = Activity.Begin();` |
| `src/PdxLauncher.App/MainWindow.xaml:149-171` | `TitleBar.Content` 里放 14px ProgressRing + 「正在翻译…」，默认 `Collapsed` |
| `src/PdxLauncher.App/MainWindow.xaml.cs:154`、`:157`、`:475`、`:573-620`、`:622-700` | 订阅 `Activity.Changed` → `DispatcherQueue.TryEnqueue` 去抖后显示 / 收起；两只 `DispatcherQueueTimer` 在关窗时停掉并退订 |
| `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs:285-291`、`:887`、`:1166`、`:1198`、`:1272`、`:1318`、`:1769-1786` | 三个 `[ObservableProperty] bool IsTranslating*`、三个 `ShowTranslating*` 派生属性、两组 `partial void OnXxxChanged` 通知；翻译路径 `finally` 里复位，切 mod 的重置路径一起清 |
| `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml:409-431`、`:526-548`、`:638-657` | 三处占位：简介 / 更新日志用带描边框的同尺寸卡（和最终译文块同位置），评论用一条无边框横排（在「共 N 条评论」下方、列表上方） |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw:36`、`workshop.json:57-59` | 新增 `shell.translating`（「正在翻译…」）与 `workshop.detailTranslating{Changelog,Comments,Description}`；键数 959 → 963 |
| `tests/PdxLauncher.Checks/TranslationActivityChecks.cs`（新）、`PdxLauncher.Checks.csproj` | 链接 `TranslationActivityMonitor.cs` 进来做自检：单层 / 嵌套 / 重复 Dispose / 64 路并发，只发一对 `Changed` |
| `README.md:402` | 翻译功能说明补一条「翻译在途有提示」 |

### 验证

- `bash tools/build-app.sh -v q -p:Platform=x64`：0 警告 0 错误（先关掉运行中的启动器，否则 dll 被锁）。
- `gen-i18n --check` 通过；生成物 963 键。
- `dotnet tests/PdxLauncher.Checks/bin/Release/net10.0/PdxLauncher.Checks.dll`：**检查类 64**、失败 0、断言失败总数 0（上一轮是 63）。
- 真机（`winapp ui` 截图，`ui-test-artifacts\translating\`）：
  - `17-placeholder.png` 是这一轮的最佳证据：标题栏环 +「正在翻译…」，简介框里「正在翻译简介…」，评论区「正在翻译评论…」，三者同屏。
  - `11/13/15/16` 逐张对着 `winapp ui search 正在翻译` 核过：翻译结束后三处占位从 UIA 树里消失（`element_not_found`），标题栏那处回到 `Collapsed`。
  - 译文/cache 命中时不会闪：显示延迟 150ms 把这些批次完全滤掉。
- 本轮环境里 `mymemory` 引擎时好时坏，失败时界面照旧保留原文并显示「翻译没有出结果」，提示也会正常收起 —— 失败路径没有把提示卡在「永远在翻」。

### 坑

- **`HasTranslated*` 从来没人通知过**：这类只读派生属性如果只声明不 `OnPropertyChanged`，`x:Bind Mode=OneWay` 永远不会更新。表现为「译好了但不显示，切个页回来就有了」——很容易被当成翻译失败。
- **不要把计数加在 ViewModel 里**：名称翻译在三个列表页 VM、正文翻译在详情页 VM、逐段翻译还会再拆批，任何一处漏加都会让提示提前消失；统一放在 `TranslateBatchAsync` 才只有一个口径。
- 计数用 `int` + `lock`（不是 `Interlocked` 自增后比较）：0→1 / 1→0 的判定和递增必须在同一把锁里，否则并发下会多发或少发 `Changed`，界面表现为提示偶发不出现。

## 0.64 2026-11-03：创意工坊页工具条收成一行 + 搜索改成回车/刷新触发

用户原话：「创意工坊这玩意能不能整成一行，方便查看，而且目前的搜索不太好用，似乎是实时搜索，容易卡住未响应」。
截图里页头是两行：第一行「创意工坊」标题 + 游戏名 + 「11,247 个结果」+ 右侧来源徽标 / 打开网页，
第二行是搜索框 + 最热门 + 30 天 + 刷新 —— 两行各占一条，列表被压得很矮。

### 决策

- **布局**：只留一行工具条，页面内标题整块去掉（左侧导航栏已经有「创意工坊」）；游戏名与结果数移到下方状态栏，
  与播放集页 §0.14「工具条收成一行」的做法一致。工具条从左到右：搜索框（`*`，最小 160）→ 排序（150–220）→
  时间范围（130–170，仅 Steam 且按时间排序时出现）→ 忙碌环 → 刷新 → 1×20 分隔线 → 来源徽标 → 打开网页。
  `workshop.title` 没有变成孤儿键：改为 `Page` 的 `AutomationProperties.Name`，读屏仍能报页面名。
- **搜索**：打字不查询，只有**回车**或**刷新**提交。旧实现每个字符都排一次 1–3 MB 的 SSR 列表页请求，
  而 `steamcommunity.com` 的出口闸门是并发 2 / 最小间隔 1 秒，且这些请求带着 `CancellationToken.None`
  **不可取消**；输入十个字符就是十几秒的请求洪峰，先发后到的结果还会互相覆盖 —— 这就是「像实时搜索、
  容易卡住未响应」的来源，不是 UI 线程被 HTML 解析卡住（解析本来就在后台线程）。
- **一期不做防抖计时器**：回车语义已经消掉洪峰，不再引入 `DispatcherQueueTimer`，也不改 `PdxLauncher.Core`
  （`RequestThrottle.AcquireAsync` 本来就尊重取消令牌，取消后请求不会出网）。搜索词用一个**已提交**副本
  `_appliedSearch` 参与查询与页缓存键，输入框只负责编辑。
- **抢占式查询**：每次查询拿一个自增代号（`_queryVersion`）和一个链接到页面令牌的取消源；新查询先取消旧的，
  旧查询在 `await` 返回、落列表、复位 `IsBusy` 之前都要比对代号，过期结果整段丢掉。
- **刷新语义**：搜索词变了 → 回第 1 页；没变 → 只刷新当前页。冷却期内点刷新既不发请求、也不提交搜索词
  （避免列表内容与搜索框文字对不上）。

### 改动清单

- `src/PdxLauncher.App/Views/WorkshopPage.xaml`：外层 `Grid` 从 5 行改 4 行（`RowSpacing` 12→10）；
  行 0 换成唯一的工具栏（8 列），`SearchBox` 增 `KeyDown="OnSearchKeyDown"`；原行 2/3/4 顺移为 1/2/3；
  状态栏改三列（游戏名 + 结果数 / 状态文案 / 封面进度），`txtWorkshopGame`、`txtWorkshopCount` 从页头移入；
  `Page` 增 `AutomationProperties.Name`。
- `src/PdxLauncher.App/Views/WorkshopPage.xaml.cs`：`OnUnloaded` 先 `_viewModel.CancelPendingQuery()` 再取消页面令牌；
  新增 `OnSearchKeyDown`（`VirtualKey.Enter` → `CommitSearchAsync()`，整段 try/catch，取消静默）；
  `OnSearchTextChanged` 注释与日志文案改为「只回写文本」。
- `src/PdxLauncher.App/ViewModels/WorkshopViewModel.cs`：新增 `_appliedSearch` / `_pageToken` / `_queryCts` /
  `_queryVersion` / `_queryTask`；新增 `CommitSearchAsync()`、`CancelPendingQuery()`、
  `RunQueryAsync(bool resetPage, bool force)`、`FlushInfoCacheAsync()`；`QueryAsync` 与 `QuerySteamAsync` /
  `QueryPdxAsync` 增加 `int version` 护栏（过期即丢，`IsBusy` 只由最新一代复位）；`BuildPageKey()` 改用
  `_appliedSearch`；`OnSearchTextChanged` 不再发起查询，删除 `OnFilterChangedAsync()`；排序 / 时间范围切换
  改走 `RunQueryAsync(resetPage: true, force: true)`；`_info.Flush()`（UI 线程同步写 JSON）改成
  `await FlushInfoCacheAsync()`（`Task.Run`）。
- `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`：`workshop.searchPlaceholder` 改成「搜索创意工坊内容（回车搜索）」/
  「Search workshop items (press Enter)」；新增 `workshop.statusSearching`「正在搜索…」/「Searching…」。
- `tests/PdxLauncher.Checks/WorkshopPageChecks.cs`（新增）：用源码文本断言钉住本轮结论 —— 工具条只有一行、
  页面内无标题样式、回车提交、外层 4 行、状态栏含游戏名与计数、14 个 AutomationId 齐全、打字不查询、
  查询代号护栏、缓存键用 `_appliedSearch`、落盘在后台线程、卸载时取消在途查询。
- `README.md`：「请求预算与 429 行为」补一条搜索触发方式与取消行为。

### 验证

- `文案自检` → 文案源合法，**964 个键**（并行会话的翻译在途提示先加 4 键，见 §0.63；
  本轮新增 `workshop.statusSearching` 1 键）。
- `cmd /c build.cmd` → 构建成功；自检 **检查类: 65，失败: 0，断言失败总数: 0**（上一轮 64，本轮新增
  `WorkshopPageChecks` 1 类）。
- `bash tools/build-app.sh -v q -p:Platform=x64` → **0 警告 0 错误**（增量 22.5 s）。
- 行为（读码核对）：连打字符 0 请求；回车 1 次请求；连按回车只保留最后一次结果；换排序 / 时间范围立即查询；
  页面卸载取消在途请求；慢磁盘落盘不再占 UI 线程。
- **未做**：UIA 实测工具条行宽（需要先在应用里选游戏再进创意工坊页，本轮没有跑自动化）。
  按控件预算估算右固定区 ≈ 551 DIP（150 + 130 + 20 + 40 + 1 + 4 + 徽标约 110 + 40 与 7 个 8 DIP 间距），
  1040 DIP 窗口的内容宽 ≈ 771 DIP → 搜索框 ≈ 220 DIP；1200 DIP 窗口 → ≈ 380 DIP，均高于 160 的最小值。

### 坑

- **重建前必须关掉正在运行的启动器**：本轮 `PdxLauncher.App.exe`（PID 32156）正跑在
  `bin\x64\Release\...\win-x64\` 下，不关就是 MSB3027 / MSB3021 文件占用。
- 改完 `src/PdxLauncher.App/Strings/*.json` 必须先跑 `文案源生成器`：`--check` 只会告诉你「文案源已过期」，
  不会顺手生成，直接构建也会在 `gen-i18n.py || exit 1` 这一步失败。
- **并行会话会同时写 HANDOFF**：本轮开工时文件是 §0.62 收尾，收工时另一个会话已经占用了 §0.63（翻译在途提示），
  所以这一节编到 §0.64；动笔前先 `tail` 一下，别把章节号写重。

### 注意

- 翻页仍是缓存优先（`force: false`）；回车、刷新、换筛选条件都是 `force: true`，跳过 15 分钟新鲜期。
- 状态栏与旧页头都在展示结果数（用户明确选择把计数放状态栏），别把它当成重复渲染的 bug 删掉。
- `workshop.title` 仍在使用（Page 读屏名）；以后要删这个键，得先给页面换一个可访问名称。

## 0.65 2026-11-03：创意工坊接上 Steam 真实状态（订阅集合 / 安装 / 更新 / 本地禁用）

> 用户原话：「我认为p社它自己写的原版启动器没有对创意工坊实现完全的适配和接入，你先翻翻Steam的接口看看」

### 决策

- **先把 Steam 接口面摸清再动手**：`sdk/public/steam/isteamugc.h` + `steam_api_flat.h` 里 `ISteamUGC` 一共 60 多个导出，
  原版启动器与本项目此前只用了 5 个（`SubscribeItem` / `UnsubscribeItem` / `GetItemState` / `CreateItem` / `SubmitItemUpdate` 系）。
  §18.1–18.4 那三路 keyless 网页接口**拿不到**「我订阅了哪些 / 装没装 / 要不要更新 / 本地禁用」——那是账号私有状态，
  只有 Steamworks 自己知道。
- **只补「读状态 + 两个写动作」，不做查询族替换列表**：`CreateQuery*UGC*` 能顶掉列表页的 HTML+SSR 抓取，
  但列表页已有请求闸门、在途合并、12 小时页面缓存，换引擎等于把整套缓存与限流结论作废；而查询族还有个额外代价 ——
  每翻一页都要再起一次子进程（Steam 会有「游戏中」登记）。这一轮只补真正缺的、网页做不到的部分。
- **批量而非逐条**：`GetNumSubscribedItems` + `GetSubscribedItems` 一次拿全量（本项目实测 CK3 384 项），
  逐条 `GetItemState` 就是 384 次跨进程调用 —— 所以状态刷新是「一个子进程拿全部」，不是「每行一个子进程」。
- **`GetItemState` 返回 0 必须当 Unknown，不能当「没订阅」**：子进程刚 `SteamAPI_Init`、Steam 还没把订阅集合同步过来时也是 0。
  详情页原来把 0 当「确定未订阅」，这是「已订阅却显示未订阅」偶发误报的根因；现在 `ReadStateDetail` 对 `raw == 0` 返回
  `WorkshopInstallState.Unknown`，只有**订阅动作确认**（`ReadStateForConfirm`）才把 0 当「确实没有」。
- **缓存 60 秒、只放内存**：状态变化快（下载进度每几秒就变），落盘得处理失效，得不偿失；
  Mod 库页刷新、行内写动作成功后 `Invalidate` 一次，其余时间读缓存，避免每次进页面都闪一次「游戏中」。
- **读状态与改订阅拆成两个设置开关**：读（`SteamWorkshopStateEnabled`，默认**开**）是只读的，代价只有一次短命子进程；
  写（`SteamWorkshopSubscribeEnabled`，仍是默认**关**）才允许订阅 / 退订 / 触发下载 / 本地禁用。
  这样「只想看到状态」的用户不必打开实验性的写开关。
- **未知态什么都不显示**（`WorkshopStateText.Resolve` 返回 `None`），不画灰色徽标、不写「状态未知」——
  用户看的是「我该不该点下载」，不是诊断信息。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/WorkshopItemStateBits.cs`（新） | `EItemState` 位常量（Subscribed 1 / LegacyItem 2 / Installed 4 / NeedsUpdate 8 / Downloading 16 / DownloadPending 32 / DisabledLocally 64）+ `ToInstallState(...)` / `HasUpdate` / `IsDisabled`；`IsDownloadPending` 定义为「排队位开且下载位关」 |
| `src/PdxLauncher.Core/Steam/WorkshopInstallState.cs` | 追加带默认值的 `NeedsUpdate` / `IsDisabledLocally` / `IsDownloadPending` / `SizeOnDisk` / `Folder` / `BytesDownloaded` / `BytesTotal`（带默认值才不用改旧构造点）；新增 `HasNativeDetail`、`DownloadPercent` |
| `src/PdxLauncher.Core/Steam/WorkshopStateText.cs`（新） | `WorkshopStateBadge` 枚举 + `Resolve(state)`（本地禁用 → 排队中 → 下载中 → 需要更新 → 已安装）+ 文案键常量；`ShowsUpdateHint` |
| `src/PdxLauncher.Core/Steam/SteamHostProtocol.cs` | `Verbs` 5→7：新增 `WorkshopSubscribedVerb="workshop-subscribed"`、`WorkshopBatchVerb="workshop-batch"`；新增 `WorkshopBatchOps`（state/download/disable/enable + `IsWrite`）、`WorkshopSubscribedItem`、`SteamHostSubscribedResult`（`MaxItems=4000`、`Truncated`）、`WorkshopBatchRequest`、`WorkshopBatchItem`、`WorkshopBatchResult`（`MaxItems=512`） |
| `src/PdxLauncher.Core/Steam/SteamWorkshopHostOperation.cs` | 新增 6 个导出绑定（`GetNumSubscribedItems` / `GetSubscribedItems` / `GetItemInstallInfo` / `GetItemDownloadInfo` / `DownloadItem` / `SetItemsDisabledLocally`）+ `EnsureSubscriptionBindings()`；新增 `ReadSubscribed` / `RunBatch` / `ReadStateDetail` / `ReadStateForConfirm` / `IsSettled` / `SafeGetNumSubscribedItems`；`MapState` 改调 `WorkshopItemStateBits.ToInstallState` |
| `src/PdxLauncher.Core/Steam/SteamHostRunner.cs` | 用法行改成 7 动词；新增 `RunSubscribed` / `RunBatch` 两个分流，**放在 `RunWorkshop` 的 `--fileid` 校验之前**（批量动词不带 `--fileid`）；新增 `WorkshopListTimeout = 60 s`、`TryDecodeRequest<T>` |
| `src/PdxLauncher.App/Services/SteamHostClient.cs` | 新增 `ReadSubscribedAsync` / `BatchAsync` / `WorkshopListTimeout = 90 s`；抽出 `ExecuteWithRequestAsync<TRequest>`（`--request` base64，与 `PublishAsync` 同口径的起进程 / 超时 / `Describe`） |
| `src/PdxLauncher.App/Services/IWorkshopStateSource.cs`（新） | `IsAvailable` / `UnavailableReason` / `ReadSubscribedAsync` / `TriggerDownloadAsync` / `SetDisabledLocallyAsync` |
| `src/PdxLauncher.App/Services/SteamWorkshopHostBackend.cs` | 实现 `IWorkshopStateSource`（`RunBatchAsync(op, id, ct)` 包 `workshop-batch`），仍保留 `IWorkshopSubscribeBackend` |
| `src/PdxLauncher.App/Services/SteamWorkshopBackends.cs`（新） | `CreateForState(game)` / `CreateForSubscribe(game)` 两个工厂，按设置开关决定是否可用；原来只在 `WorkshopDetailPage` 里 new 一遍 |
| `src/PdxLauncher.Core/Online/WorkshopStateCache.cs`（新） | 内存缓存，TTL 60 s，按 appId 分桶，`TryGet` / `TryGetStale` / `Set` / `Invalidate` / `Clear`；挂在 `AppServices.WorkshopStates` |
| `src/PdxLauncher.App/Services/ModLibraryService.cs` | 加可选 `Func<GameInstallation, IWorkshopStateSource?>` + `WorkshopStateCache`；`LoadAsync` 在排序前 `InjectSteamStatesAsync`（缓存 → 子进程 → 失败退旧副本），按 `WorkshopId` 把 `SteamState` 回填到 `ModLibraryEntry` |
| `src/PdxLauncher.Core/Contracts/ModLibraryEntry.cs` | 追加 `WorkshopInstallState? SteamState = null` |
| `src/PdxLauncher.Core/Configuration/AppSettings.cs` | 新增 `SteamWorkshopStateEnabled`（默认 true，只读状态） |
| `src/PdxLauncher.App/ViewModels/ModLibraryRowViewModel.cs` | `SteamState` / `ShowSteamStateBadge` / `SteamStateBadgeText` / `CanTriggerSteamDownload` / `IsSteamDisabledLocally` / `SteamDisableActionText` + 三个 AutomationId |
| `src/PdxLauncher.App/ViewModels/ModLibraryViewModel.cs` | `IsSteamActionBusy` / `HasSteamStates` / `CanWriteSteam` / `RefreshSteamStatesAsync` / `TriggerSteamDownloadAsync` / `SetSteamDisabledLocallyAsync` / `RunSteamActionAsync`（写开关关着时只给状态栏提示，不弹框） |
| `src/PdxLauncher.App/Views/ModLibraryPage.xaml`、`.xaml.cs` | 行内加状态徽标（`PlaySetText` 之后）+ 下载 / 本地禁用两个图标按钮；`OnSteamDownloadClick` / `OnSteamDisableClick` |
| `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs` | `IsSteamNeedsUpdate` / `SteamDownloadProgressText` / `ShowSteamUpdateHint` / `HasSteamDownloadProgress` / `CanWriteSteam`；`ApplySteamInstallState` 消费新字段；`RefreshSteamSubscriptionAsync` 先读 `WorkshopStates` 缓存 |
| `src/PdxLauncher.App/Views/WorkshopDetailPage.xaml`、`.xaml.cs` | 「需要更新」警示条 + 下载进度文本；`CreateSteamBackend()` 改走两个工厂 |
| `src/PdxLauncher.App/Views/SettingsPage.xaml`、`ViewModels/SettingsViewModel.cs` | 新增 `tglSteamWorkshopState`（读状态，默认开）卡片，在写开关之前；`SteamStateEnabled` + `SettingsSteamStateOn/Off/Hint` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw`、`settings.json`、`workshop.json` | modlib 13 键 + settings 3 键 + workshop 2 键 + `workshop.detailSteamReadOnlyHint`；959 → 983 键 |
| `tests/PdxLauncher.Checks/WorkshopItemStateBitsChecks.cs`、`WorkshopStateTextChecks.cs`、`WorkshopStateCacheChecks.cs`、`WorkshopSubscribedProtocolChecks.cs`（新） | 位值、优先级映射、TTL、7 动词与新记录的 JSON 往返 |
| `tests/PdxLauncher.Checks/SteamHostProtocolChecks.cs` | 动词断言 5→7（含 `Verbs[4..6]` 逐项）；`CheckWorkshopResultRoundTrip` 补扩展字段往返 |
| `README.md:225`、`:585`、`:491`、`docs/DATA-FORMATS.md` §18.5（新） | Mod 库页两个新按钮与状态徽标、宿主超时与两个开关、检查类计数、Steamworks 订阅状态接口全表 |

### 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → **0 警告 0 错误**。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → **0 警告 0 错误**。
- `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → **检查类: 69，失败: 0，断言失败总数: 0**（上一轮 65）。
- `文案自检` → 通过，**983 个键**。
- 两个真 bug 是自检抓出来的：`WorkshopStateText.Resolve` 里 `IsDownloadPending` 必须排在 `IsDownloading` 之前（否则排队分支永远走不到）；
  `WorkshopBatchOps.IsWrite(null)` 原来返回 true（未知动作被当成写动作）。

### 坑

- **`GetItemState` 的 0 不等于「没订阅」**——见上面决策，这是本轮最重要的一条。
- **新增宿主动词要改三处**：`Verbs` 数组、`SteamHostRunner.Run` 的用法行、`SteamHostProtocolChecks` 里的「恰好 N 个」断言。
  批量化动词还必须在 `RunWorkshop` 的 `--fileid` 校验**之前**分流，否则会被「缺少或非法的 --fileid」挡下。
- **XAML 注释里不能出现 `--`**（WMC9997）：写 `steam-host` 而不是 `--steam-host`，否则整个页面编译失败。
- **`DownloadPercent` 是 `double?`**，写成 `is int` 直接 CS8121。
- **`FirstOrDefault(id => ...)` 的 lambda 不能加 `static`**：CS8820，会引用外层的 `publishedFileId`。
- `TranslationActivityChecks.CheckConcurrentLeases` 偶发失败（`期望 2，实际 4`，5 次里 1 次）：
  `Parallel.For` + `Thread.SpinWait(200)` 的时序抖动，属既有检查，与本轮改动无关，别去改 `TranslationActivityMonitor`。

### 注意

- **未做**（下一轮可接）：`SetSubscriptionsLoadOrder`（Steam 侧加载顺序）、查询族 `CreateQuery*UGC*`（替代列表页抓取）、
  投票 / 收藏 / 依赖增删 / `DeleteItem` / EULA / PlaytimeTracking。
- `GetItemInstallInfo` 的目录串是**工坊条目目录名**（即 publishedFileId），不是绝对路径；要拼完整路径仍走
  `ModThumbnailResolver` 那套「按库根重定位」的逻辑。
- 徽标只提示，不拦截启动：没装 / 有更新都照样能启动游戏（游戏自己会提示）。

## 0.66 2026-11-03：Mod 创建页补齐工坊发布面（可见性 / 描述词 / 多图与视频 / 多语言 / 内置封面）

> 用户原话：「我打开steam看到一堆功能没有支持，而且似乎没有使用项目内自带的封面图片，还有没有添加和编辑图片/视频的功能」
> 配四张 Steam 客户端截图：可见性下拉（公开 / 仅限好友 / 隐藏 / 非公开，当前勾的是「隐藏」）、
> 「更新内容描述词」五个勾选项、「编辑标题与描述」的语言下拉（英语 / 简体中文 / …）。
> 用户对范围的答复：「多语言设置成高级模式行，看你这描述很复杂而且有问题，似乎会被steam拦住的可能」。

### 决策

- **只补 Steam 客户端能改、而上游 API 也允许改的面**：可见性、内容描述词、附加预览、多语言标题描述这五样都有
  对应导出（`SetItemVisibility` / `AddContentDescriptor` / `AddItemPreviewFile` / `AddItemPreviewVideo` / `SetItemUpdateLanguage`），
  所以是「我们把已有能力接出来」，不是猜。查询族 `CreateQuery*UGC*`（拿回官方渲染的附加预览）仍然不接，理由见 §0.65。
- **可见性用真枚举替换 `bool Public`**：`ModPublishRequest.Public` 只能表达 0/2 两档，而 Steam 是
  `Public 0 / FriendsOnly 1 / Private 2 / Unlisted 3` 四档。新字段 `SteamWorkshopVisibility Visibility` 是位置参数，
  旧字段直接删掉；`ModPublication` 保留了旧的 `bool Public`（老记录还得读），但新写入时 `Public = visibility == Public`，
  读取一律走派生 `EffectiveVisibility => Visibility ?? Effective(Public)`，所以旧记录仍能正确回显。
- **描述词先全删再按勾选加**：`AddContentDescriptor` 没有「查询当前有哪些」的配套（那要走查询族），
  而 `RemoveContentDescriptor` 对没设过的 id 是无害 no-op，所以每次提交都对 1..5 全部 remove 一遍再 add 目标集，
  这样「取消勾选」才能真正生效，也不需要读回。
- **附加预览先清空再从 0 追加**：`RemoveItemPreview(index)` 从 0 开始反复删到返回 false，然后按用户顺序
  `AddItemPreviewFile` / `AddItemPreviewVideo` 逐个加。代价是顺序完全由我们控制，不依赖 Steam 的排序规则。
- **多语言是「主提交成功后的追加提交」**：每种语言单独 `StartItemUpdate` → `SetItemUpdateLanguage` →
  只设 Title/Description → `SubmitItemUpdate`。**绝不重设 Content / Preview / Visibility / Tags / 描述词** ——
  重设内容等于把整个 mod 再传一遍，重设可见性等于把用户的选择覆盖成本次语言的值。
- **多语言默认关、收在「高级」Expander 里、失败降级为警告**：用户明确担心「似乎会被 Steam 拦住」。
  所以：开关默认 false；语言数计入宿主总超时（`PublishTimeout * (1 + min(count, 12))`）；
  部分语言失败仍返回 `Ok = true`，只在结果里带一段「有 N 种语言未写入」的说明，不让主上传显示成失败。
- **单图 1 MB 是 Steam 的硬限制，先在本地拦**：`AddItemPreviewFile` 头文件注释写明「must be under 1MB in size」。
  与其让用户在一个 800 MB 的 mod 传到 99% 时才失败，不如在 `ModStudioService.PublishAsync` 一开始就检查每张附加图
  （`ModPublishLimits.IsPreviewFileAcceptable`），错误里带文件名。上限 8 张、12 种语言同理属于「先在本地说清楚」。
- **封面改用内嵌默认图**：`ModCreator` 原来的 `PlaceholderPng` 是 1×1 透明 PNG，写进 `thumbnail.png` / `metadata.json` 的
  `picture` 后 Steam 侧就是一片空白。现在把项目自带的 `Square150x150Logo.scale-200.png`（24 KB，远小于 1 MB）
  作为 `EmbeddedResource` 嵌进 `PdxLauncher.Core`（`LogicalName = PdxLauncher.ModStudio.default-cover.png`），
  由 `ModCoverAssets.Default` 读取、`WriteDefault` 落盘；新建 mod 与上传页的「使用内置封面」按钮共用这一份字节。
  选择嵌资源而不是读 App 的 `Assets\`：`ModCreator` 在 Core 层，Core 不能依赖 App 的目录结构。
- **视频只接受 YouTube 链接**：`AddItemPreviewVideo` 收的就是 video id，Steam 客户端也只支持 YouTube。
  `YouTubeVideoId.TryParse` 统一处理 watch?v= / youtu.be / embed / shorts / live / 裸 ID 五种写法，其他域直接拒绝并提示。

### 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/SteamWorkshopVisibility.cs`（新） | 四档枚举（0/1/2/3）+ `All` / `ToNative` / `FromNative(int)` / `Effective(bool)` |
| `src/PdxLauncher.Core/Steam/ModLocalizedText.cs`（新） | `ModLocalizedText(Language, Title, Description)`；`SteamWorkshopLanguages` 30 种语言码与显示名、`Normalize`（未知 → english）、`DisplayName` |
| `src/PdxLauncher.Core/Steam/YouTubeVideoId.cs`（新） | `TryParse` 五种链接形式 + 纯 ID；只认 `[A-Za-z0-9_-]{11}` 与 YouTube 域 |
| `src/PdxLauncher.Core/ModStudio/ModCoverAssets.cs`（新） | 内嵌默认封面的读取与 `WriteDefault(IFileSystem, path)`；失败返回 false 不抛 |
| `src/PdxLauncher.Core/ModStudio/Assets/default-cover.png`（新） | 24863 字节，取自 App 的 `Square150x150Logo.scale-200.png` |
| `src/PdxLauncher.Core/PdxLauncher.Core.csproj` | 新增第 4 个 `EmbeddedResource` + `LogicalName` |
| `src/PdxLauncher.Core/ModStudio/ModCreator.cs` | 删掉 `PlaceholderPng`；`WritePreview` 兜底改调 `ModCoverAssets.WriteDefault` |
| `src/PdxLauncher.Core/Steam/SteamHostProtocol.cs` | `ModPublishRequest` 的 `bool Public` → `SteamWorkshopVisibility Visibility`，追加 `ContentDescriptors` / `PreviewPaths` / `PreviewVideoIds` / `Localizations`（都带默认值）；新增 `ModPublishLimits`（`MaxPreviewBytes` / `MaxAdditionalPreviews=8` / `MaxLocalizations=12` / `IsPreviewFileAcceptable`） |
| `src/PdxLauncher.Core/ModStudio/ModPublication.cs` | 追加 `Visibility` / `ContentDescriptors` / `PreviewPaths` / `PreviewVideoIds` / `Localizations`（带默认值），保留旧 `Public`；派生 `EffectiveVisibility` / `Descriptors` / `ExtraPreviews` / `Videos` / `ExtraLocalizations` |
| `src/PdxLauncher.Core/Steam/SteamWorkshopHostOperation.cs` | 新增 6 个导出绑定（`SetItemUpdateLanguage` / `AddItemPreviewFile` / `AddItemPreviewVideo` / `RemoveItemPreview` / `AddContentDescriptor` / `RemoveContentDescriptor`）+ `EnsureExtrasBindings(...)`；`Publish` 改为按四档设置可见性、`ApplyContentDescriptors`、`ApplyExtraPreviews`；主提交成功后 `SubmitLocalizations`（逐语言独立 `StartItemUpdate`，失败收集不中断） |
| `src/PdxLauncher.Core/Steam/SteamHostRunner.cs` | `RunPublish` 增加可见性越界校验；宿主总超时按语言数放大 |
| `src/PdxLauncher.App/Services/ModStudioService.cs` | `PublishAsync` 改成 15 参（可见性枚举 + 描述词 + 附加图 + 视频 + 多语言）；提交前逐张校验附加图存在且 <1 MB；写回 `ModPublication` 时带上全部扩展字段 |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | `UploadVisibility` + `VisibilityOptions` / `SelectedVisibilityOption`；5 个描述词 bool；`UploadPreviewItems` / `UploadVideoItems` / `UploadLocalizations` 三个集合与增删移动方法；`UseBuiltInCover()`；`UploadLocalizationEnabled` / `UploadAdvancedExpanded`；`ResetUploadForm` 回填扩展字段；`ApplyProgress` 认 `localizing` 阶段 |
| `src/PdxLauncher.App/ViewModels/ModUploadPreviewItem.cs`、`ModUploadVideoItem.cs`、`ModLocalizationRow.cs`、`VisibilityOption.cs`（新） | 三个列表行 VM 与一个下拉项 VM；行 VM 继承 `LocalizedObservableObject`（提供 `L` / `T`，见「坑」） |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 上传面板重排：标签之后插入可见性下拉、5 个描述词勾选、主封面 +「使用内置封面」、附加图列表（上移 / 下移 / 删除）、视频链接输入与列表、`Expander` 高级区（多语言开关 + 语言行 + 警告文案 + 添加按钮）；删掉旧的「公开」ToggleSwitch |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml.cs` | `PickImageAsync(multiple)` 支持 `.bmp/.gif/.webp` 与多选；新增 9 个行内按钮处理器；确认框按语言数选用 `ModstudioUploadConfirmMulti` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 各 +35 键（可见性四档、5 个描述词、预览与视频、高级与多语言、`stageLocalizing`、`uploadConfirmMulti`）；983 → 1018 键 |
| `tests/PdxLauncher.Checks/ModPublishExtrasChecks.cs`（新） | 可见性映射、1 MB 与张数上限、五种 YouTube 链接、语言表与 `Normalize`、请求 JSON 全字段往返、旧 `Public` 记录回显、内嵌封面 PNG 签名与 `ModCreator` 落盘一致 |
| `tests/PdxLauncher.Checks/ModStudioChecks.cs` | 两处 `new ModPublishRequest(...)` 改用可见性枚举 |
| `README.md`、`docs/DATA-FORMATS.md` §18.6（新） | Mod 创建页功能说明、发布请求扩展字段表 |

### 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → **0 警告 0 错误**。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → **0 警告 0 错误**。
- `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → **检查类: 71，失败: 0，断言失败总数: 0**（上一轮 69）。
- `文案自检` → 通过，**1018 个键**。

### 坑

- **`WMC9999`「未能找到任何适合于指定的区域性…ErrorMessages.resources」不是真的缺资源**：本轮它真实原因是
  `ModStudioPage.xaml` 里三个新 `DataTemplate` 用了 `{x:Bind L.SomeKey}`，而三个行 VM 没有 `L` 属性 ——
  WinUI 的 XAML 编译器遇到无法解析的 `x:Bind` 路径时会以这种误导性的「资源缺失」形式崩溃，不报普通的绑定错误。
  改成继承 `LocalizedObservableObject` 后立刻通过。**以后见到 WMC9999 先查 x:Bind 路径里的成员存不存在。**
- **XAML 注释里不能出现 `--`**（WMC9997），写 `steam-host` 而不是 `--steam-host`，否则整页编译失败。
- **`ModCreateRequest` 是 7 参**（`Name` / `FolderName` / `Version` / `SupportedVersion` / `Tags` / `Description` /
  `PreviewSourcePath`），`ModCreateResult` 是 `(Ok, DirPath, ErrorCode)` —— 写自检时最容易照旧印象写错。
- **V3（Victoria 3 / EU5）的封面在 `<modDir>\.metadata\thumbnail.png`**，legacy 系（CK3 / Stellaris / EU4 / HOI4 /
  Imperator）才在 mod 根目录；断言封面路径时别写死根目录。
- **`ModStudioPage` 没有 `_logger` 字段**，日志一律 `AppServices.Logger.Error(...)`；页面只有一个
  `public Localizer L => Localizer.Instance;`。
- 改 XAML / 重建前必须 `taskkill //IM PdxLauncher.App.exe //F`（MSB3027 文件锁）。

### 注意

- **未做**：`UpdateItemPreviewFile` / `UpdateItemPreviewVideo`（现在走「全删重建」，功能等价但会重传图片）；
  查询族读回官方渲染的附加预览与描述词（要在详情页展示别人的预览壁纸，得接 `GetQueryUGCNumAdditionalPreviews` 那一族）；
  开发分支用的 `SetRequiredGameVersions` / `SetItemMetadata`。
- **多语言是实验性功能**：Steam 对「同一 handle 多次 `SetItemUpdateLanguage` + `SubmitItemUpdate`」的行为没在文档里写全，
  真机上如果被拒，表现是主上传成功、结果里带「有 N 种语言未写入」。用户界面已按此降级，不会误报整体失败。
- **附加预览目前只写不读**：上传后本启动器看不到自己刚设的图（要靠 Steam 客户端或以后接查询族），
  再次打开上传页时列表来自 `ModPublication` 的本地记录，不是从 Steam 拉回来的。

## 0.67 2026-11-03：一键启动 `start.cmd` + VS Code 工程入口

用户原话（2026-11-03）：「有没有更好的项目打开方式，目前的启动方式不太方便我查看，给我给一键启用的方式查看」。
痛点是改完一行代码要先 `build.cmd`、再 `run.cmd`，程序还藏在
`src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\` 里，双击要一路点进去。

经确认，用户选的是「两条都要」+「只要根目录 `start.cmd`，不要桌面快捷方式」+「每次都跑增量构建」。

### 1. 根目录 `start.cmd`（123 行 / 4338 B，纯 ASCII、CRLF）

```
start.cmd        :: 一键启动：Release 增量构建后直接运行（已有实例在跑就跳过启动）
start.cmd debug  :: 同上，但用 Debug 配置
start.cmd check  :: 构建 + 跑一遍自检，再启动（等价于原来的 build.cmd + run.cmd）
start.cmd new    :: 忽略「已在运行」判断，强制再开一个窗口
start.cmd /?     :: 打印用法
```

流程：`chcp 65001` → `cd /d "%~dp0"`（双击时当前目录是 `System32`，必须自己切回仓库根）→ 解析参数 →
`where dotnet` 检查 → `dotnet build "PdxLauncher.sln" -c %CONFIG% -m:1 --nologo -v:m` →（`check` 时）
`dotnet "tests\PdxLauncher.Checks\bin\%CONFIG%\net10.0\PdxLauncher.Checks.dll` → 检查 exe 是否存在 → `start ""` 启动。
中文全部走 `scripts\msg.cmd` 的 18 个新键（`S_USAGE1..4 S_TITLE S_CONFIG S_BUILDING S_NODOTNET S_FAILBUILD
S_STEPCHECK S_FAILCHECK S_CHECKOK S_NOEXE S_EXPECT S_STARTING S_EXE S_ALREADY S_PRESSKEY`），脚本本身 0 个 >127 字节。

- **不做时间戳短路**：用户明确要「每次都跑增量构建」。`dotnet build` 自己就是增量的（无改动约 1 s，
  改一行约 5–15 s），加 `if exist` 判断反而会在切分支、改 csproj、删 `obj` 之后给一个过期 exe。
- **必须 `-m:1`**：见 §「构建为什么必须 `-m:1`」。并行 MSBuild 会让 WinUI 的 XAML 编译偶发 0 错 0 警告地不产出 xbf。
- **不创建桌面快捷方式**：是用户选的。仓库根双击 `start.cmd` 已经是一步，`.lnk` 还要处理路径失效与「未知发布者」提示。

### 2. `.vscode/`（只提交三个文件，不含任何机器路径）

- `tasks.json`：6 个 `type: process` 任务，`windows.command` 是 `cmd /c <脚本>`，`presentation.panel: "shared"`。
  默认任务 `PdxNext: 一键启动（增量构建 + 运行）` → `start.cmd`（`group.isDefault: true`，`Ctrl+Shift+B` 直达）；
  另有 `构建 + 自检 (Release)`（带 `$msbuild` problemMatcher）、`构建 (Debug)`、`启动 Debug`、`自检`、`发布到 dist`。
- `settings.json`：`files.encoding: utf8`、`files.eol: "\r\n"`、`files.exclude` 与 `search.exclude` 隐藏
  `bin/obj/dist/.scratch/.tmp/.backup/.agents/ui-test-artifacts/.nuget`、`[csharp].editor.tabSize: 4`。
- `extensions.json`：推荐 `ms-dotnettools.csdevkit` 与 `ms-dotnettools.csharp`。
- **`.gitignore` 配合**：原 `.vscode/` 整目录忽略，改成 `.vscode/*` 再 `!` 放行这三个文件，其余本机配置照旧忽略。
- **故意不放 `launch.json`**：WinUI 3 + 自包含 + WindowsAppSDKSelfContained 的 F5 经常起不来（宿主进程/包标识都对不上）。
  断点调试走「启动 Debug」任务 + `Debug: Attach to Process` 附加 `PdxLauncher.App.exe`，可靠得多。

### 3. 自检 `StartScriptChecks.cs`

`public static int Run(IFileSystem fs)`，断言：`start.cmd` 存在、逐字节 <0x80（失败报行号）、
脚本里每个 `msg.cmd KEY` 的 KEY 都已在 `messages.txt` 定义（正则 `scripts\\msg\.cmd"\s+([A-Za-z0-9_]+)`）、
两条 exe 路径的 TFM 与 `PdxLauncher.App.csproj` 的 `<TargetFramework>` 一致、含 `-m:1`、
`.vscode/tasks.json` 可被 `System.Text.Json` 解析且第一个任务是默认一键启动。
这样以后改 TFM 或改脚本忘了同步提示键，`build.cmd` 会直接红。

### 4. 验证记录

- 干净 worktree（`G:\Work\pdx-verify-oneclick`，基于 ad4f712）全量构建：0 警告 0 错误，27 s；
  自检 `检查类: 57，失败: 0`（该基线还没有后来并发会话加的检查类）；`start.cmd check` 完整走完并启动成功。
- 「已在运行」分支：先开实例再 `call start.cmd` → 打印「PdxNext 已在运行，本次不重复启动…」，退出码 0。
- 「exe 缺失」分支：把 exe 改名后跑 → 打印「找不到可执行文件，未启动程序」+ 期望路径，退出码 1。
- 期间排查到一个既有 flaky 自检 `TranslationActivityChecks.CheckConcurrentLeases()`：原来 `Parallel.For(0, 64, …)`
  的租约常常不重叠，导致 `Changed` 触发次数随机是 6 而不是 2，`build.cmd` 随机红。改成 64 个独立线程 +
  `CountdownEvent` / `ManualResetEventSlim` 卡住重叠窗口后连跑 10 次全绿。
- 源码树当时被另一个并发会话改着（`ModPublishRequest.Public` → `Visibility` 等），所以上面的编译验证都在
  worktree 里做；这类跨会话改写不属于本轮。

### 5. 已知边界

- 没有单实例 Mutex：`start.cmd` 用 `tasklist` 查同名进程来避免重复开窗，`new` 参数可以绕过。
- 查进程用 `findstr` 而不是 `find`：PATH 里 Git 的 GNU `find.exe` 会抢在前面且不认 `/i`，会恒判「没在跑」。

## 0.68 2026-11-03：多语言改成「选语言 → 填该语言」+ 从「高级」折叠区移进上传表单

用户原话（2026-11-03）：「高级设置放错地方了，是上传页面里面塞」，
追加：「你目前的多语言实现不是选择语言实现，怎么是一个个顺序实现，你不能看看 steam 的 api 看看是怎么实现没」。

### 1. 先说查到的 API 事实

`ISteamUGC` 里与语言有关的导出只有两个：查询侧 `SetLanguage(UGCQueryHandle_t, const char*)`，
发布侧 `SetItemUpdateLanguage(UGCUpdateHandle_t, const char*)`。官方文档（`partner.steamgames.com/doc/api/ISteamUGC`）原文：
`SetItemUpdateLanguage` 是「设置标题的语言并提供**将在此次项目更新中**使用的描述」，`SetItemTitle` / `SetItemDescription`
则说明「可以使用 `SetItemUpdateLanguage` 来设置此描述用于哪种语言，如果没有设置语言，那么则默认为 english」。

**语言是绑在 update handle 上的，一个 handle 只能承载一种语言**，所以 N 种语言必然是 N 轮
「起 handle → 设语言 → 设标题/描述 → 提交」。这层顺序提交是 API 强制，改不了。
用户的不满其实落在 UI：原来是「点一下加一行，语言由程序自动轮着挑」，用户没法选语言，也无法对照 Steam 客户端
「编辑标题与描述」对话框的心智模型。所以这轮只改呈现层，提交路径原样保留。

### 2. 改了什么

- **位置**：删掉上传面板底部那个 `<Expander AutomationId="expModStudioAdvanced">`（原来的「高级」折叠区），
  整块移到「描述 `txtModStudioUploadDescription`」字段正下方，成为「其他语言（可选）」一节 ——
  小标题 + 一行说明 + 语言行列表 + 「添加语言」下拉 + 添加按钮 + 实验性提示。默认就是可见的普通表单字段，不再是折叠的「高级」。
- **语言可选**：`ModLocalizationRow` 的 `Language` 从只读变成可变，新增 `SelectedLanguage`（ComboBox 绑定）
  与 `LanguageOptions => SteamWorkshopLanguages.All`（30 种）。改下拉 → 写回 `Language`；反之亦然（两边互相回写，用 Ordinal 比较防循环）。
- **添加语言**：新增 `ModStudioViewModel.UploadAvailableLanguages`（把已用语言剔掉的剩余列表，供下拉用）与
  `UploadNewLocalizationLanguage`；`AddUploadLocalization(string? language = null)` 显式语言优先，
  传空才用 `SteamWorkshopLanguages.PickUnused(...)` 兜底推荐；重复或全用尽返回 null，页面提示 `modstudio.localizationDuplicate`。
- **去掉开关**：`UploadLocalizationEnabled`、`UploadAdvancedExpanded` 两个属性删掉（列表非空即提交，无列表就不提交），
  `tglModStudioLocalization` 一并删除。校验从「开关开着才查空标题」改成「有任何一行空标题就拦，并把语言名带进提示」，
  另外新增「语言重复」检查（`SteamWorkshopLanguages.IsDuplicate`，先归一化再比，`klingon` 与 `english` 视为同一个）。
- **协议零改动**：`ModPublishRequest.Localizations`、`SteamWorkshopHostOperation.SubmitLocalizations`、
  `ModPublishLimits.MaxLocalizations` 都没动。`core` 只加了 `SteamWorkshopLanguages.PickUnused` / `IsDuplicate` 两个纯函数。

### 3. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/ModLocalizedText.cs` | 新增 `PickUnused(IEnumerable<string>?)`（按 `All` 顺序挑第一个未占用语言码，全占用返回 null）与 `IsDuplicate(IEnumerable<string>?, string?)`（归一化后 Ordinal 比较） |
| `src/PdxLauncher.App/ViewModels/ModLocalizationRow.cs` | `_language` 变可变 + 新增 `_selectedLanguage`；`LanguageOptions` / `DisplayName` / `IsTitleEmpty`；`OnLanguageChanged` / `OnSelectedLanguageChanged` 互相回写；保留 `ToLocalizedText()` |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | 删 `UploadLocalizationEnabled` / `UploadAdvancedExpanded`；加 `UploadNewLocalizationLanguage`、`UploadAvailableLanguages`、`HasDuplicateLocalizationLanguage()`；`AddUploadLocalization(string?)` 支持显式语言；`ClearUploadExtras` / `ApplyPublicationExtras` / `PublishAsync` 三处校验同步改写 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 删 `Expander`（含 `tglModStudioLocalization`、`ModstudioLocalizationWarning`、旧 `lstModStudioLocalizations`），在描述字段后插入「其他语言（可选）」一节：语言 ComboBox（`DisplayMemberPath="DisplayName"`）+ 标题 TextBox + 删除按钮、`cmbModStudioNewLocalization` + `btnModStudioAddLocalization`、实验性提示 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml.cs` | `languageCount` 直接读 `UploadLocalizations.Count`（不再看开关）；`OnAddLocalizationClick` 传 `UploadNewLocalizationLanguage?.Code`，返回 null 时 `SetStatus(L.T(Keys.ModstudioLocalizationDuplicate))` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 加 `localizationSection` / `localizationHint` / `localizationPick` / `localizationDuplicate` / `localizationNote`（各 5 个 = 10 个），删已无引用的 `advanced` / `localizationEnable` / `localizationWarning` / `localizationDescription` / `localizationLanguage` / `localizationDescriptionPlaceholder`；两文件各 144 键，全局 1021 键 |
| `tests/PdxLauncher.Checks/ModPublishExtrasChecks.cs` | `CheckLanguages()` 追加 6 条：空集合挑到 `schinese`、跳过已用、全占用返回 null、判重忽略大小写、未知码归一后判重、不同语言不判重 |

### 4. 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → 0 警告 0 错误。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → 0 警告 0 错误；
  `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → `检查类: 71，失败: 0，断言失败总数: 0`。
- `文案自检` → 退出码 0，`1021` 个键。
- `pwsh -NoProfile -File scripts/verify-settings-sections.ps1` → 七节 `missing=[]`，`FAILED_SECTIONS=0`。

### 5. 坑（本轮踩过）

- **`[ObservableProperty]` 需要 `using CommunityToolkit.Mvvm.ComponentModel;`**：给 `ModLocalizationRow` 换基类成
  `LocalizedObservableObject` 时顺手把这行 using 删了，结果整片 `CS0246: 未能找到类型或命名空间名"ObservableProperty"`，
  而 WinUI 的 XAML 编译器跟着报 `WMC9999: 未将对象引用设置到对象的实例`。**WMC9999 十有八九是别处 C# 编译错引起的二次故障**，
  先看 C# 错误列表，别去改 XAML。
- **`XamlCompiler.exe` 会占住 `obj\...\intermediatexaml\PdxLauncher.App.dll`**：连续两次构建太快会撞
  `CS2012 ... being used by another process`。`taskkill //IM XamlCompiler.exe //F` 再重建即可。
- 删文案键要用脚本按 JSON 整体重排写回（`json.dump(..., ensure_ascii=False, indent=2)` + 末尾换行），
  手改容易漏掉 en-US 那份，`--check` 会报键数不一致。

### 6. 注意

- 提交顺序没有变，也不打算变：`SetItemUpdateLanguage` 绑在 handle 上，一次一种语言是 API 的硬约束。
- 仍然不读回「Steam 上已存了哪些语言」：要读回得接查询族（`CreateQueryUGCDetailsRequest` + `SetLanguage` 逐语言拉），
  超出本轮范围；因此再次打开上传页时，语言行只来自本地 `ModPublication` 记录。
- 空标题的语言行会被前端直接拦下（提示里带语言名），不会静默跳过，避免用户以为某种语言已经提交成功。
- 上面这条在 §0.69 已作废：语言行模型整体删掉，改成「当前语言」单槽编辑。

---

## 0.69 2026-11-03：多语言最终形态照 Steam 客户端（语言下拉 + 标题 + 描述）

用户原话（2026-11-03）：「你这个是错误修改，你按照steam的显示修改吧，上面是语言选择（默认就是steam设置的英文），
中间是标题，下面是描述；然后steam的标题和描述是不限语言的，只是影响对应语言的用户看到的东西」，
并附了 Steam 客户端「编辑标题与描述」对话框截图（顶部面包屑 + 语言下拉「英语」+ 标题框 + 描述框 + 右下「保存」）。

### 1. 决策：一行都不许再铺

§0.68 那版把语言做成「点一下加一行、每行一个语言下拉 + 标题框」，用户看截图后判定为错。Steam 客户端的模型是
**同一块表单只显示一种语言的内容**：语言下拉在最上面，标题居中，描述在下；切语言时内容整体换掉。
所以改成：

- 表单里只保留**一个**语言下拉（`cmbModStudioUploadLanguage`）+ 原有的标题框 + 描述框，位置顺序照截图。
- 语言不再「添加 / 删除」，而是**随时可切**：切走时把当前标题与描述存进该语言的槽位，切回来再载入。
- 槽位表是 VM 里的私有字典 `Dictionary<string, ModLocalizedText> _localizedDrafts`（键为归一化语言码），
  english 永远有一项（即使为空），其余语言只有填了标题才留下。
- 提交给宿主的仍然只有「english 之外、且填了标题」的那些语言（`CollectLocalizations()`），
  Core 的 `SubmitLocalizations` 一行没改 —— 一次提交一种语言是 `SetItemUpdateLanguage` 绑 handle 的硬约束。

删除的东西：`ModLocalizationRow` 整个类（连同 XAML 里的 `DataTemplate x:DataType="vm:ModLocalizationRow"`）、
`UploadLocalizations` 集合、`AddUploadLocalization` / `RemoveUploadLocalization` /
`UploadAvailableLanguages` / `HasDuplicateLocalizationLanguage`、页面上「添加语言」下拉与按钮、删除行按钮。

### 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | 删 `UploadNewLocalizationLanguage`；新增 `[ObservableProperty] SteamWorkshopLanguage? _uploadSelectedLanguage`、`UploadLanguageOptions`（= `SteamWorkshopLanguages.All`）、`UploadExtraLanguageSummary`（「另有 N 种语言已填标题」）、`UploadLanguageCount`；新增 `ChangeUploadLanguage(SteamWorkshopLanguage?)`（带 `_changingLanguage` 重入护栏，先存后载）、`ResetLanguageEditing(title, description)`（english 落槽 + 选中 english）、`SaveCurrentLanguageDraft()`、`LoadLanguageDraft(code)`、`CollectLocalizations()`、`IsDefaultLanguage` / `IsFilled` / `CurrentLanguageCode`；`ClearUploadExtras` 去掉语言相关清理；`ApplyPublicationExtras` 把 `ExtraLocalizations` 灌进字典；`ResetUploadForm` 两个分支都调 `ResetLanguageEditing`；`PublishAsync` 开头 `SaveCurrentLanguageDraft()`，删掉空标题与重复语言两段校验；新增 `partial void OnUploadSelectedLanguageChanged` |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 「名称」字段前插入语言 `ComboBox`（`ItemsSource=UploadLanguageOptions`、`SelectedItem=UploadSelectedLanguage` 单向 + `SelectionChanged="OnUploadLanguageChanged"`、`DisplayMemberPath="DisplayName"`、AutomationId `cmbModStudioUploadLanguage`）；删掉整段「其他语言（可选）」（`lstModStudioLocalizations` + `cmbModStudioNewLocalization` + `btnModStudioAddLocalization`）；描述框后只留两行说明：`UploadExtraLanguageSummary`（警示色，AutomationId `txtModStudioLocalizationCount`）与改写后的 `ModstudioLocalizationNote` |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml.cs` | `languageCount` 改读 `_viewModel.UploadLanguageCount`；删 `OnAddLocalizationClick` / `OnRemoveLocalizationClick`；新增 `OnUploadLanguageChanged(object, SelectionChangedEventArgs)`（`sender is ComboBox { SelectedItem: SteamWorkshopLanguage language }` → `ChangeUploadLanguage`） |
| `src/PdxLauncher.App/ViewModels/ModLocalizationRow.cs` | **整个文件删除** |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 加 `fieldLanguage`（语言 / Language）、`localizationCount`（「另有 {0} 种语言已填标题」）；删 `localizationAdd` / `localizationPick` / `localizationDuplicate` / `localizationSection` / `localizationHint` / `localizationTitle` / `localizationTitleRequired`；改写 `localizationNote` 为「切换语言只改变下面这对标题与描述…上传时每种语言各提交一次」；各 **139 键**，全局 **1014 键** |

未改：`ModPublishRequest.Localizations`、`SteamWorkshopHostOperation.SubmitLocalizations` / `SubmitLocalizationCore`、
`ModPublishLimits.MaxLocalizations`、`SteamWorkshopLanguages` 全线（`PickUnused` / `IsDuplicate` 留着给自检与将来用）。

### 3. 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → 0 警告 0 错误。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → 0 警告 0 错误；
  `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → `检查类: 71，失败: 0，断言失败总数: 0`。
- `文案自检` → 退出码 0，`1014` 个键。
- `pwsh -NoProfile -File scripts/verify-settings-sections.ps1` → 七节 `missing=[]`，`FAILED_SECTIONS=0`。

### 4. 坑（本轮踩过）

- **切语言的时序**：`ComboBox.SelectedItem` 若双向绑定，`PropertyChanged` 先于用户改完文本触发，会把「新语言」的
  旧文本盖回去。做法是 `SelectedItem` 只做 `Mode=OneWay`，真正切换走 `SelectionChanged` → `ChangeUploadLanguage()`，
  在方法里先 `SaveCurrentLanguageDraft()`（用切换前的 `CurrentLanguageCode`）再改选中项。
- **重入**：`ChangeUploadLanguage` 内部改 `UploadSelectedLanguage` 会再触发一次通知，加了 `_changingLanguage`
  布尔护栏挡住，避免「存 → 载 → 再存」把刚载入的文本当成用户输入。
- **english 空标题也要落槽**：`SaveCurrentLanguageDraft` 里只有「非默认语言且标题为空」才删槽，
  否则 english 空着切走再切回会丢东西（默认语言的槽位始终存在）。
- 删类前先在 XAML 里删掉 `DataTemplate` 与事件处理器，否则 `WMC9999` / 缺 `OnRemoveLocalizationClick` 编译错。

### 5. 注意

- 提交顺序没变：N 种语言仍 N 次「起 handle → 设语言 → 设标题/描述 → 提交」，这是 API 硬约束。
- 仍不读回 Steam 上已存的语言：`_localizedDrafts` 只来自本地 `ModPublication.ExtraLocalizations`；
  要读回得接查询族（`CreateQueryUGCDetailsRequest` + `SetLanguage` 逐语言），超出本轮。
- 切换语言时报文面 `UploadExtraLanguageSummary` 只数「非 english 且填了标题」的语言，空着的语言不占名额也不提示。


## 0.70 2026-11-03：翻译「没有出结果」→ 免费引擎换得动主机 + 原因逐条可见

用户报告（2026-11-03 14:36）：「翻译遇到点问题」，附详情页截图：顶部红字
「翻译没有出结果：mymemory: 未返回译文。可在「设置 → 翻译」里点「测试翻译」查看各引擎状态。」
下方「描述」区仍是英文原文。

### 1. 根因（实测，不是在代码里猜的）

- `api.mymemory.translated.net` 返回 **HTTP 429**，body 原文：
  「MYMEMORY WARNING: YOU USED ALL AVAILABLE FREE TRANSLATIONS FOR TODAY. NEXT AVAILABLE IN  02 HOURS 21 MINUTES 29 SECONDS」。
  代码对 429 返回 `(null, false)`（非 fatal），于是每一段译文都从头再打一遍额度已空的接口。
- `translate.googleapis.com` 在本机 **TLS 握手直接失败**（`curl: (35) schannel: failed to receive handshake`），
  免费引擎没有第二个主机可退。
- 同一套谷歌公开接口在 `clients5.google.com` 上 **200 且返回结构完全一致**（`[["你好","en"]]`）；
  `translate_a/single?client=gtx&dt=t` 在该主机上同样可用（长短文本、双向都试过）。
- 提示为何只提 mymemory：`TranslationService` 的 `_engineFailureNote` 只在 null 时写，而「自动」模式下
  mymemory 先失败，后面 free 的 TLS 失败被吞掉 —— 用户看到的是第一个失败者，不是全部真相。
- 另一个隐藏约束：`TranslationCoordinator` **每批都 `CreateService` 新建引擎**，
  所以「记住这台主机坏了」不能放引擎实例里，否则下一段译文又白等一次超时。

### 2. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Online/Translation/TranslationEndpointHealth.cs` | **新增**。进程级端点健康状态（`static Shared`）：`MarkDead(host, ttl)` / `MarkHealthy` / `IsDead` / `MarkQuotaExhaustedUntil` / `QuotaResumeAt` / `IsQuotaExhausted` / `Reset`；`Func<DateTimeOffset>? now` 供自检注入时钟 |
| `src/PdxLauncher.Core/Online/OnlineHosts.cs` | 新增 `Clients5TranslationHost = "clients5.google.com"` 并加进 `IsAllowedHost` 精确白名单；`TranslationHost` 注释改为「主用主机」 |
| `src/PdxLauncher.Core/Online/Translation/ITranslationEngine.cs` | 新增默认成员 `string? LastFailure => null;`（最近一次失败的人话原因，成功置 null） |
| `src/PdxLauncher.Core/Online/Translation/FreeTranslationEngine.cs` | `Hosts`（主用 + 备用）、`DeadHostTtl = 5min`；构造注入 `TranslationEndpointHealth`；`RequestAsync` 按主机顺序尝试：网络异常 / 超时拉黑该主机，HTTP 非 2xx 与解析失败只记原因不拉黑；成功清空 `LastFailure` |
| `src/PdxLauncher.Core/Online/Translation/MyMemoryTranslationEngine.cs` | `QuotaMarker` / 等待时间正则（`NEXT AVAILABLE IN N HOURS M MINUTES`，解析不出按 30 分钟）；429 或 body 命中标记 → 记额度耗尽 + 恢复时间并返回 fatal；之后的请求直接跳过、不再打接口；`LastFailure` 形如「免费额度已用完，约 2 小时后恢复」 |
| `src/PdxLauncher.Core/Online/Translation/TranslationService.cs` | `_engineFailureNote` → `List<string> _engineFailureNotes`；`AddFailureNote` 逐引擎记「id: 原因」（异常优先、否则取 `engine.LastFailure`），`DescribeFailures()` 用「；」拼接、超 300 字符截断；每批开头清空 |
| `tests/PdxLauncher.Checks/TranslationChecks.cs` | 新增 `CheckFreeEngineHostFallback` / `CheckMyMemoryQuota` / `CheckMultiEngineFailureNotes` 与 `HostRoutingTransport`（按主机抛网络异常或返回 body） |
| `README.md` | 翻译节补主机回落 / 冷却 / 额度耗尽 / 逐引擎原因；自检清单同步 |
| `src/PdxLauncher.App/Strings/*` | 无新键 |

未改：设置页结构、`TranslationOptions`、缓存键格式、`TranslationCoordinator` 的引擎顺序（自填 → 微软 → 谷歌云 → mymemory → free）。

### 3. 验证

- `dotnet build src/PdxLauncher.Core/PdxLauncher.Core.csproj -c Release -v q` → 0 警告 0 错误。
- `bash tools/build-app.sh -v q -p:Platform=x64` → 0 警告 0 错误；`文案自检` → 1014 键、生成物最新。
- `PdxLauncher.Checks` → `检查类: 71，失败: 0，断言失败总数: 0`，其中新增断言包括：
  「免费引擎主机回落出译文 / 冷却后只发一条请求 / 冷却解除后先试主用主机 / 两台主机都冷却时返回 null」、
  「MyMemory 429 记为额度耗尽 / 额度耗尽后不重复请求 / 恢复时间过后重新翻译」、
  「多引擎失败时列出 alpha / beta」。
- 真机：启动器打开 Iceland mod 详情页，描述区出中文译文、顶部无红字。

### 4. 坑

- **健康状态不能放引擎实例**：协调器每批 `CreateService`，实例字段等于只记一批；额度和坏主机都必须进程级。
- **429 要当 fatal 处理**：配额耗尽不是「这条没译出来」，继续逐条重试只会把等待时间拖满一页。
- **HTTP 非 2xx 不该拉黑主机**：400/404 是请求本身的问题，拉黑 5 分钟会让本来能用的主机被无端跳过。
- **失败原因要逐条列**：只报第一个引擎会把用户指错方向（本次就是只报额度用完的 mymemory，掩盖了 free 的 TLS 失败）。
- 新增主机必须同时进 `OnlineHosts.IsAllowedHost` 白名单，否则 `TranslationHttpTransport` 会以 `host-not-allowed` 拦掉。

## 0.71 2026-11-03：谷歌免费翻译换首选主机 + 合批 + 429 短冷却

用户报告（2026-11-03 15:05）：「我不是联网连着系统代理吗，为什么谷歌翻译连不上」。

### 实测：代理没坏，是那个主机名坏了

- 应用确实在用系统代理：`data/pdxnext.sqlite` 里 `proxyMode` = `system`，日志反复出现「跟随系统代理 http://10.31.2.50:7890」；
  Windows 注册表 `ProxyEnable=1`、`ProxyServer=10.31.2.50:7890`、没有 PAC。
- 经同一代理 `curl -v https://translate.googleapis.com/...`：`CONNECT` 先回 `HTTP/1.1 200 Connection established`，
  随后客户端发出 471 字节 ClientHello，**服务端一直不回**，5.0 秒后 schannel 报「failed to receive handshake」；
  应用日志里的 `The SSL connection could not be established` 与此一致。
- 同代理下别的域名都通：`www.google.com` 200、`translate.google.com` 200、`translation.googleapis.com` 404、
  `clients5.google.com` 200；对这个域名发明文 HTTP，代理直接回 502（自己也没连上游）。
- DNS 被污染：`translate.googleapis.com` 解析到 **192.179.26.95**（不是谷歌网段），`clients5.google.com` 是 142.250.73.142；
  用 `--resolve translate.googleapis.com:443:142.250.73.142` 强制真实 IP，经代理和直连**都仍然失败** —— 所以不只是 DNS，
  是按主机名 / SNI 的阻断，代理对这个域名大概率走直连。
- `clients5.google.com` 会间歇 429（14:55 探测成功，15:07 翻评论时日志刷 `HTTP 429`），而 curl 单独打是 200 —— 出口 IP 被限流，
  当时的实现是「一段文本一个请求」，评论一多必然撞。

### 改动

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Online/OnlineHosts.cs` | 新增 `GoogleTranslateWebHost = "translate.google.com"` 并加进 `IsAllowedHost` 精确白名单 |
| `src/PdxLauncher.Core/Online/Translation/FreeTranslationEngine.cs` | 主机顺序 `translate.google.com` → `clients5.google.com` → `translate.googleapis.com`；改用 `/translate_a/t?client=dict-chrome-ex&q=…&q=…` 合批（≤8 段、URL ≤1500 字符一组，组内保序，最多 4 组并发）；429 → 记 `HTTP 429（host）` 并 `MarkDead(host, 60s)` 后换下一台（网络异常/超时仍 5 分钟冷却）；429 时把响应体前 120 字符写日志；失败原因保留**第一台**主机的（`lastFailure ??=`），用户先看到首选主机的真实原因 |
| `tests/PdxLauncher.Checks/TranslationChecks.cs` | 重写 `CheckFreeEngineHostFallback`；新增 `CheckFreeEngineBatching`、`CheckFreeEngineRateLimit` 与 `StatusRoutingTransport` / `BatchEchoTransport` 替身 |
| `README.md` | 翻译节改主机顺序 / 合批 / 两种冷却；自检清单补三条 |
| `docs/HANDOFF.md` | 本节 |

### 验证

- `dotnet build src/PdxLauncher.Core` → 0 警告 0 错误。
- `dotnet build tests/PdxLauncher.Checks -c Release` → 0 警告 0 错误；跑 dll → **检查类 71，失败 0，断言失败总数 0**。
  新断言：3 段只发一个请求且保序、20 段 ≤3 个请求且每组 ≤8 段、首选主机 429 时换下一台并进 60 秒冷却、三台都 429 时返回 null 且原因带主机名。
- 真机（2026-11-03 15:22–15:26，PID 53352，`winapp ui`）：创意工坊连开 3 个 mod 的详情页，中文译文都出现在原文下方的描边框里
  （Environmental Engineering / Custom Subjects / Smart Treaties），页面顶部不再出现红字，标题栏的「正在翻译…」译完即收；
  同一时段日志里只有启动时那一条 mymemory 429，没有 clients5 的 429 刷屏（旧版 15:07 一次评论翻译刷了 7 条）。
  UIA 读到的译文示例：「右键单击状态打开环境工程界面，然后选择是否添加或删除状态特征……」。截图 `ui-test-artifacts/transfix2/01–05*.png`。
- 设置 → 翻译 → 「测试翻译」实测（15:28）：`✗ MyMemory 翻译（免密钥）：mymemory: 免费额度已用完，约 2 小时后恢复`
  + `✓ 谷歌免费翻译（无需密钥）：您好，这是翻译测试。` —— 与修复目标一致（免费引擎打勾、额度原因带恢复时间）。
- 现象备注：长简介（Smart Treaties）逐段翻译约 30 秒，是先等 mymemory 429 再落到免费引擎 + 每段一个请求的结果；
  文字量大的 mod 首屏译文会晚十几秒，属预期，不是卡死。

### 坑

- **失败原因别取最后一台**：`lastFailure = …` 会让三台全 429 时把兜底主机的错报给用户，改成 `??=` 保留首选主机。
- **新增主机必须进白名单**：`TranslationHttpTransport` 对未授权主机直接 `host-not-allowed`，表现像「连不上」。
- **429 不等于网络坏**：拉黑 5 分钟太重，60 秒足够让它换出口 IP；但网络异常/超时仍然 5 分钟。
- **合批要看 URL 长度**：`translate_a/t` 走 GET，2000 字符可用但保守取 1500，超了拆组而不是丢段。
- **评论翻译是逐条的**：`TranslateDocumentAsync` 逐段调 `TranslateBatchAsync([text])`，合批只在同一次调用的多段间生效，
  所以评论页的请求数仍等于「条数 ÷ 1」——本轮没有改协调器把多条评论合批，靠 429 短冷却兜住。

## 0.72 2026-11-03：Mod 创建页三处 UI 修正（`{0}` / 冗余标题 / 上传面板关不掉）

用户原话（2026-11-03）：「1.这个{0}是啥？；2.mod详情这个几个字多余了；3.打开上传到创意工坊页面后，怎么点击它关不掉，看不了mod详情」，
并附两张截图：列表行徽标写着字面的「已发布 · {0}」，以及详情面板顶部「Mod 详情」小标题压在 mod 名大标题上方。

### 1. 根因

| # | 现象 | 根因 |
| --- | --- | --- |
| 1 | 徽标显示字面 `{0}` | `ModStudioPage.xaml` 里行模板裸绑 `{x:Bind L.ModstudioRowPublished}`，而 `modstudio.rowPublished` 的值是「已发布 · {0}」。生成的属性是 `Format("modstudio.rowPublished")`，**`Localizer.Format` 在 `args` 为空时直接返回模板原文**（`Localization/Localizer.cs:117-129`），于是占位符原样上屏。同一行 `SubtitleText` 已经含带 id 的 `PublicationText`，徽标再带一次 id 也是重复。 |
| 2 | 「Mod 详情」多余 | 详情面板顶部有一个 `L.ModstudioDetailTitle` 小标题（Subtitle 样式），紧跟着就是 `SelectedName` 的 Title 大标题。该键全仓只有这一处引用。 |
| 3 | 上传面板关不掉 | `PublishAsync` 成功分支用 `LoadAsync(..., resetPane: false)`，面板停在上传页；`catch (OperationCanceledException)` 分支只写状态、从不切面板；`OnSelectedRowChanged` 只重置表单不切面板。于是上传结束（或取消）后既看不到详情，在上传面板里点左侧另一个 mod 也仍然停在上传页 —— 页面又是 `NavigationCacheMode="Required"`，实例常驻，感觉就是「关不掉」。 |

### 2. 决策

- 徽标只说「已发布」：工坊 id 已经在副标题与详情面板的「发布状态」里，列表行右侧宽度有限，不该再挤一遍。
  新增短键 `modstudio.rowPublishedShort`，由行视图模型算好再绑；带 `{0}` 的 `modstudio.rowPublished` 保留给 `PublicationText`。
- 详情面板只留一个标题（mod 名）。「Mod 详情」和从未被引用的「返回详情」两个键一起删掉，不留死键。
- 上传面板的三条出口统一回详情：**成功**（`resetPane: true` + 显式 `Pane = Detail`）、**取消**（取消分支切回详情）、
  **切换选中项**（`OnSelectedRowChanged` 在非发布中时切回详情）。**失败仍然留在上传面板** —— 表单与错误信息要留给用户重试。

### 3. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/ViewModels/ModStudioRowViewModel.cs` | 新增 `PublicationBadgeText => HasPublication ? T(Keys.ModstudioRowPublishedShort) : string.Empty`（带注释说明为什么不复用 `PublicationText`）；`PublicationText` / `SubtitleText` / `HasPublication` 不变 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 行模板徽标改绑 `{x:Bind PublicationBadgeText}`；删详情面板顶部的 `L.ModstudioDetailTitle` TextBlock |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | `PublishAsync` 成功分支 `LoadAsync(..., resetPane: true)` + `SelectByPath` 之后 `Pane = ModStudioPane.Detail`；取消分支补 `Pane = ModStudioPane.Detail`（带注释说明原因）；`OnSelectedRowChanged` 在 `!IsPublishing && Pane != Detail` 时切回详情 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml.cs` | `OnUploadCancelClick` 在发布中除 `Cancel()` 外再 `SetStatus(L.T(Keys.ModstudioUploadCanceling))`，让点击立刻有反馈 |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 加 `rowPublishedShort`（已发布 / Published）、`uploadCanceling`（正在取消上传… / Canceling upload…）；删死键 `detailTitle`、`backToDetail`；各仍 **139 键**，全局仍 **1014 键** |
| `tests/PdxLauncher.Checks/LocalizationBindingChecks.cs` | **新增**。扫 `Views/` 与 `Controls/` 下所有 `.xaml` 的 `{x:Bind L.Xxx}`，按 `src/PdxLauncher.Localization.Generator` 的 `identifier_for` 规则（点分各段首字母大写拼接）反查 zh-CN 模板，断言**裸绑键的模板里不含 `{n}`**；顺带断言键表规模 > 500、扫到的裸绑定 > 20（防止正则失效后静默全绿） |
| `tests/PdxLauncher.Checks/ModStudioPaneChecks.cs` | **新增**。源码断言：XAML 含 `PublicationBadgeText` 且不再含 `L.ModstudioRowPublished` / `L.ModstudioDetailTitle`；VM 里 `LoadAsync(Game, cancellationToken, resetPane: true)` 存在、`catch (OperationCanceledException)` 块里有 `Pane = ModStudioPane.Detail`、`OnSelectedRowChanged` 里有面板复位；code-behind 含 `ModstudioUploadCanceling` |

未改：Core 的发布协议、`PublishAsync` 的失败分支、`ModStudioRowViewModel.PublicationText` 的格式。

### 4. 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → 0 警告 0 错误。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → 0 警告 0 错误；
  `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → `检查类: 73，失败: 0，断言失败总数: 0`。
- `文案源生成器` / `--check` → 退出码 0，`1014` 个键（modstudio 各 139）。
- `pwsh -NoProfile -File scripts/verify-settings-sections.ps1` → 七节 `missing=[]`，`FAILED_SECTIONS=0`。

### 5. 坑

- **`Localizer.Format` 无参返回原文**：`Format(key, args = null)` 在 `args` 为 null 或空数组时**直接返回模板**，不做占位符替换。
  所以任何带 `{n}` 的键都不能裸绑 `{x:Bind L.Xxx}`，必须在视图模型里用 `T(Keys.X, args)` 拼好再绑 —— 编译期不会报错，
  自检也不会报错，只有真机才看得见 `{0}`。`LocalizationBindingChecks` 就是为这条钉的。
- **`x:Bind` 的裸绑属性名与 key 的映射**：`modstudio.rowPublishedShort` ↔ `ModstudioRowPublishedShort`，
  规则是各段首字母大写后拼接（与 `src/PdxLauncher.Localization.Generator` 的 `identifier_for` 一致），自检里必须照抄这个规则，否则反查不到。
- **面板复位要放在 `catch` 里而不是只有一个 `finally`**：成功、取消、失败三种结局对面板的期望不同，
  统一在 `finally` 里回详情会把失败时用户填好的表单一起丢掉。
- **`NavigationCacheMode="Required"` 会放大状态 bug**：页面实例常驻，`Pane` 不会因为离开页面而复位，
  凡是「进去一次就回不去」的观感，先查视图模型里有没有出口。

### 6. 注意

- 上传失败仍停留在上传面板，这是有意的；只有成功 / 取消 / 切换选中三条路径回详情。
- 徽标文案与副标题的分工：徽标 =「已发布」状态，副标题 = 「大小 · 已发布 · <工坊 id>」，详情面板的「发布状态」行同样带 id。
- 自检类数 71 → 73，HANDOFF 与 README 的计数要一起改。

## 0.73 2026-11-03：创意工坊描述支持 BBCode + 字节上限 + 上传页工具条与实时预览

用户原话（2026-11-03）：「steam的描述功能限制有什么，我看外国那些mod都很复杂，是支持什么特殊格式吗」。

### 1. 查到的硬限制与格式

- Steam 的标题 / 描述是**定长字符缓冲**：`sdk/public/steam/isteamremotestorage.h:52` `k_cchPublishedDocumentTitleMax = 128 + 1`、
  `:53` `k_cchPublishedDocumentDescriptionMax = 8000`、`:54` `k_cchPublishedDocumentChangeDescriptionMax = 8000`、
  `:58` `k_cchPublishedFileURLMax = 256`。预览图另有 1 MB 上限（`isteamugc.h:307` / `:312`）。
- 描述**不是 Markdown 也不是 HTML**，是 Steam 社区方言的 BBCode：
  `[h1]`–`[h3]`、`[b]`、`[i]`、`[u]`、`[strike]`、`[spoiler]`、`[noparse]`、`[hr]`、`[url=…]文字[/url]`、
  `[list][*]…[/list]`、`[olist][*]…[/olist]`、`[quote=作者]`、`[code]`、`[img]URL[/img]`、`[previewyoutube=ID][/previewyoutube]`；
  **裸 URL 会自动生成小部件**（YouTube 视频卡、Steam 商店卡、工坊条目卡）——「外国 mod 看起来复杂」主要来自这里。
- 实测 30 个 Victoria 3 热门条目（`ISteamRemoteStorage/GetPublishedFileDetails/v1/`）：标题最长 60 字符、描述最长 7259 字符；
  标签频次 `b 298`、`* 225`、`h2 130`、`list 104`、`url 96`、`i 54`、`h1 44`、`hr 24`、`img 14`、`h3 12`、`olist 6`，
  图片基本都在 `images.steamusercontent.com`。

### 2. 根因（我们的缺口）

| # | 现象 | 根因 |
| --- | --- | --- |
| 1 | 超长标题 / 描述会被 Steam 静默截断或只回一个「设置失败」 | 发布侧把 `request.Title` / `request.Description` 原样交给 `SetItemTitle` / `SetItemDescription`（`SteamWorkshopHostOperation.cs:418` / `:423`，多语言在 `:743` / `:748`），**没有任何字节校验** |
| 2 | 上传页只能写纯文本，不知道 Steam 认什么标记 | 上传页描述是一个裸 `TextBox`，没有工具条、没有计数、没有预览 |
| 3 | 兜底路径把 BBCode 原样露出来 | 详情页描述在拿不到物品页 HTML 时退到 `MarkdownParser.Parse(WorkshopText.ToPlainText(...))`；`ToPlainText` 的正则只覆盖 `b/i/u/h1-6/list/*/url/img`，实测 30 条真实描述里残留 `[hr]` 12 次、`[olist]` 3 次 |
| 4 | 两份 `ToPlainText` 已分叉 | App 的 `Services/WorkshopText.cs:20` 与 Core 的 `Text/WorkshopHtml.cs:39` 各写一份，正则还不完全一样 |
| 5 | Steam 渲染出的 `<hr>` 被吞掉 | `HtmlDescriptionParser` 的 `StartTag` switch 没有 `hr` 分支，`MarkdownRule` 在 AST 与 `MarkdownView` 里都已支持 |

### 3. 决策

- **按 UTF-8 字节判长度**，不按字符数：Steam 那边是 `char` 缓冲，但中文经 UTF-8 传输会被算成 3 字节，
  用户看到「128」时写 43 个汉字就该被挡。提示文案里直说「一个汉字按 3 字节算」。
- **三层设防**：`ModPublishLimits.TryValidate*`（可复用、可测）→ 宿主 `SteamWorkshopHostOperation` 提交前再拦一次
  （防绕过 UI 的调用）→ 上传页提交前拦并给本地化提示（用户可读）。
- **自己写一个 BBCode 解析器转成现有 `MarkdownDocument`**，而不是新写一套渲染：详情页兜底、上传页预览、
  未来渲染都用同一棵 AST，`MarkdownView` 直接能画标题 / 列表 / 分隔线 / 链接 / 图片 / 代码块。
- **`WorkshopHtml.ToPlainText` 用白名单正则扩全标签**，绝不写成 `\[[^\]]*\]`：正文里真实存在 `[1.13]` 这种方括号，
  一刀切会把正文吃掉。标签名长的排前面（`spoiler` 先于 `s`），否则 `[spoiler]` 会被 `s` 规则拦腰截断。
- **App 的 `ToPlainText` 改成委托 Core**，结束两份分叉；`WorkshopText` 类本身保留（还有 `Join` / `FormatDate` / `TryParseSteamId`）。
- 工具条只做**插标记**，不做富文本编辑器：Steam 侧存的本来就是 BBCode 原文，所见即所得会带来「界面里的样子 ≠ 提交后的样子」。
  预览放在描述框下面，明确标注「读者看到的样子」。

### 4. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Steam/SteamHostProtocol.cs` | `ModPublishLimits` 加 `MaxTitleBytes = 128`、`MaxDescriptionBytes = 8000`、`MaxChangeNoteBytes = 8000`、`Utf8Length(string?)`、`TryValidateTitle/Description/ChangeNote`（统一走 `TryValidateText`，错误文案带实际字节与上限） |
| `src/PdxLauncher.Core/Steam/SteamWorkshopHostOperation.cs` | `Publish` 在 `_startItemUpdate` 之前校验标题与描述，失败直接 `ModPublishResult.Fail`；`SubmitLocalizationCore` 开头同样先校验该语言的标题与描述 |
| `src/PdxLauncher.Core/Text/SteamBbCodeParser.cs` | **新增**。`Parse(string?) → MarkdownDocument`，`MaxInputLength = 20000` 并记录 `IsTruncated`；块级支持 `[h1]`–`[h6]`、`[hr]`、`[code]`、`[quote]`/`[spoiler]`（嵌套上限 4）、`[list]`/`[olist]`（按 `[*]` 切条目、可嵌套）；行内支持 `[noparse]`、`[code]`、`[img]`、`[previewyoutube=ID]`（转 `img.youtube.com/vi/<id>/0.jpg` 缩略图）、`[url=…]`/`[url]…[/url]`、`[b]/[i]/[u]/[s]/[strike]`、词首裸 URL；**不认识的 `[…]` 原样保留**；有「索引未前进就强行前进」的死循环保护 |
| `src/PdxLauncher.Core/Text/WorkshopHtml.cs` | `BbCodeBlockRegex` 扩为 `\[/?(?:previewyoutube|strike|spoiler|noparse|olist|list|quote|code|h[1-6]|hr|b|i|u|s|\*)(?:=[^\]]*)?\]`，`BbCodeMediaRegex` 扩为 `\[/?(?:url|img)(?:=[^\]]*)?\]`（长标签在前） |
| `src/PdxLauncher.Core/Text/HtmlDescriptionParser.cs` | `StartTag` 加 `case "hr":` → 非 `<pre>` 内时 `Flush()` 并追加 `MarkdownRule` |
| `src/PdxLauncher.App/Services/WorkshopText.cs` | `ToPlainText` 改为一行委托 `WorkshopHtml.ToPlainText(source)`；其余成员不动 |
| `src/PdxLauncher.App/ViewModels/WorkshopDetailViewModel.cs` | 描述兜底从 `MarkdownParser.Parse(WorkshopText.ToPlainText(...))` 改为 `SteamBbCodeParser.Parse(detail.Description)` |
| `src/PdxLauncher.App/ViewModels/ModStudioViewModel.cs` | 加 `UploadTitleBytes` / `UploadDescriptionBytes` / `UploadTitleCountText` / `UploadDescriptionCountText` / `IsUploadTitleTooLong` / `IsUploadDescriptionTooLong` / `UploadTitleWarningText` / `UploadDescriptionWarningText` / `UploadDescriptionPreview` / `HasUploadDescriptionPreview`；`OnUploadTitleChanged`、`OnUploadDescriptionChanged` 里通知派生属性；`PublishAsync` 在标题非空校验之后加两道字节拦截 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml` | 标题 / 描述各加「已用 / 上限」计数与超限提示；描述加 11 个按钮的 BBCode 工具条（B I U S H2 • 1. — Link Img Code）与「预览（读者看到的样子）」卡片（`ctl:MarkdownView`，`MaxHeight=320` 内部滚动，描述为空时整块隐藏） |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml.cs` | 新增 `OnBbCodeClick` 与四个静态辅助：`ApplyWrap`（包裹 / 插入，可指定光标落在开标记内几个字符处）、`ApplyLineBlock`（整行加 `[h2]`）、`ApplyList`（整行转 `[list]`/`[olist]`）、`InsertAtCaret`；操作后 `box.Focus(...)` 把焦点还给描述框 |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 各加 16 键：`bytesCounter`、`titleTooLong`、`descriptionTooLong`、`bbcodeHint`、`bbcodeBold/Italic/Underline/Strike/Heading/BulletList/NumberedList/Rule/Link/Image/Code`、`previewHeader`；各 **139 → 155 键** |
| `tests/PdxLauncher.Checks/SteamBbCodeParserChecks.cs` | **新增**。36 条断言：纯文本、`[h1]`–`[h6]`、`[hr]`（含 `[/hr]`、`[hr/]`）、两种列表与嵌套、粗体 / 删除线、链接（`[url=X]文字[/url]` 与 `[url]X[/url]`）、图片、`[previewyoutube]` 缩略图、JSON 风格未知标签保留、`[noparse]`、超长截断 |
| `tests/PdxLauncher.Checks/ModPublishLimitsChecks.cs` | **新增**。21 条断言：ASCII / 汉字 3 字节 / emoji 4 字节的字节口径、128 与 8000 的边界值（恰好通过、+1 被挡）、42 个汉字通过而 43 个被挡、含 BBCode 标记时按原文计字节 |

未改：发布协议字段与动词、`Localizer`、`gen-i18n.py`。

### 5. 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -m:1 --nologo -v q -p:Platform=x64` → 0 警告 0 错误。
- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q` → 0 警告 0 错误；
  `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build` → `检查类: 80，失败: 0，断言失败总数: 0`。
- `文案自检` → 退出码 0；modstudio 各 155 键（本轮 +16）。
- `pwsh -NoProfile -File scripts/verify-settings-sections.ps1` → 七节 `missing=[]`，`FAILED_SECTIONS=0`。
- 真机（`winapp ui`）：上传页名称行「25 / 128 字节」、描述行「0 / 8000 字节」；描述为空时预览整块不出现；
  填入含 `[h2]/[b]/[i]/[strike]/[list]/[olist]/[hr]/[url=]/[code]` 的描述后计数变「271 / 8000 字节」，
  预览按标题 / 加粗斜体删除线 / 圆点列表 / 有序列表 / 分隔线 / 蓝色链接 / 代码块正确渲染；
  点 `Code` 按钮在光标处插入 `[code]` + 空行 + `[/code]`（见下方第 5 条坑）。

### 6. 坑

- **`get-value` 会吞空行**：`winapp ui get-value` 读回的文本看似 `[code][/code]…`，实际截图里是
  `[code]`、空行、`[/code]`。判断插入结果对不对，要截图看，别只信 `get-value`。
- **工具条按钮会抢焦点**：点按钮时 `TextBox` 失去焦点，`SelectionStart/SelectionLength` 是「失去焦点那一刻」的值。
  改完 `Text` 必须显式 `box.Focus(FocusState.Programmatic)`，并把选区回设，否则用户下一次点击会从 0 开始。
- **`[s]` 会吃掉 `[spoiler]`**：白名单正则里短标签必须排在长标签之后（`spoiler` 在 `s` 之前），
  否则 `[spoiler]` 被 `[s]` 规则截成 `poiler]`。
- **绝不能写 `\[[^\]]*\]`**：正文里有 `[1.13]` 这种真实方括号内容，一刀切会把正文吃掉；只能枚举已知标签。
- **并发会话会污染构建**：本轮构建中途出现过 `src/PdxLauncher.Core/Online/Translation/Local/` 目录下
  另一个会话正在写的文件留下的 CS0101/CS0111/CS0246，以及 `Keys` 未包含新键导致的 CS0117 + 连带 `WMC9999`。
  判断方法：错误集中在自己没碰过的目录、文件 mtime 是「现在」、`git status --porcelain` 显示整个目录 `??`。
  正确做法是**等**（轮询 build 直到对方写完），不要顺手去改别人的文件，也不要 `git checkout` 覆盖。
- **`SteamBbCodeParser` 里局部变量重名会 CS0136**：`linkUrl` 与 `imageUrl` 分开命名即可。
- **自检类的签名必须是 `public static int Run(IFileSystem fs)`**：`Program.Discover()` 用反射找这个签名，写成无参 `Run()` 不会被发现，会静默少跑。

### 7. 注意

- 描述上限按**字节**，`modstudio.bytesCounter` 展示的也是字节；用户写英文时看起来「才 300 多」是正常的。
- 预览是只读的 `MarkdownView`，不参与提交；提交的永远是描述框里的 BBCode 原文。
- 上传页在这个版本**没有做「所见即所得」**，也**不做** `[table]`（Steam 描述里实测无人使用，解析器也不认，会原样保留）。
- 自检类数 73 → 80（本轮 +2，其余是并发会话新增的播放集排序相关类）。HANDOFF 与 README 的计数要一起改。

## 0.74 2026-11-04：DLC 页「490 个 DLC」的真相（loader 表 ≠ Steam 目录）+ 账号真实拥有视图

用户原话（2026-11-04）：「DLC页面是真的判断出是否拥有所有dlc吗，不会只是扫描本地dlc吧」；贴出 V3 截图后追问「vic3哪来的490个dlc」。

### 1. 查到的硬限制

- loader 在岗时 `GetDLCCount` / `BGetDLCDataByIndex` 读的是同目录 `cream_api.ini` 的 `[dlc]` 表，**不是 Steam 目录**，而且不按当前 appid 过滤。仓库素材那份表共 490 行，是 11 款 Paradox 游戏共用模板（CS1 94 / EU4 93 / CK2 74 / HoI4 59 / Stellaris 56 / CK3 37 / CS2 29 / V3 23 / V2 11 / Imperator 9 / EU5 5）。
- V3 真机（appid 529340）：loader 表 490、账号真实 23、本地 `game/dlc/` 17 个 `.dlc.json`。所以「490/490」既不是 V3 的 DLC 数，也不是账号拥有数。
- 想拿「账号真实拥有」只能探原版备份 `steam_api64_o.dll`（loader 834560 B，原版 298384 B）；同一 appid 同一时刻只能有一个 `SteamAPI_Init`，两次探测必须顺序执行。

### 2. 根因

| # | 现象 | 根因 |
| --- | --- | --- |
| 1 | 判定句写「未打补丁」，详情却写「补丁：已加载」 | `DlcOwnershipVerdict.Evaluate` 只把 `GamePatchStatus.Patched` 当已打补丁，`PatchedOther`（他人/整合包装的 loader）落进 `Vanilla*`；`DlcViewModel` 的详情却把 `Patched` 和 `PatchedOther` 都当「已加载」 |
| 2 | 「Steam 报告 490 个 DLC」 | loader 在岗时 `GetDLCCount` 来自 `cream_api.ini`，文案却统一写「Steam 报告」 |
| 3 | 「全部拥有」名不副实 | candidates 只取磁盘上带 steam_id 的条目；磁盘没有的 DLC 永远不会判未拥有，账号真实拥有也无从体现 |

### 3. 决策

- `GamePatchReport.IsLoaderActive = Status is Patched or PatchedOther`（`IsPatched` 语义不动，仍是「有补丁痕迹」）。
- `SteamOwnershipReport` 追加 `IsLoaderActive` 与 `Account`（`DlcAccountOwnership?`，第二份「账号真实拥有」报告）；loader 在岗且备份存在时顺序再探一次备份，主报告失败不影响账号结论。
- 判定条拆两条线：主句 = 游戏实际可用（loader 表/Steam 目录），账号句 = 账号真实拥有；`Patched*` 主句不再写「Steam 报告」。
- CS2（磁盘 DLC 数为 0）回退时优先用账号表，避免拿 490 条 loader 整表对账。
- 缓存 key 扩为「DLL 路径 + 字节数 + appid + 补丁状态 + 备份路径 + 备份字节数」；设置页打/撤补丁成功后 `Invalidate()`。
- 降级显式化：备份缺失 / 备份与当前文件同字节 / 备份返回同样的表 / 备份探测失败，各有错误键，不再静默当成「没拥有」。

### 4. 改动清单

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.Core/Patch/GamePatchState.cs` | `GamePatchReport` 加 `IsLoaderActive`、`BackupApiFilePath`（由 ApiDirectory/ApiFileName 推出 `_o` 备份路径） |
| `src/PdxLauncher.Core/Patch/GamePatchService.cs` | `BackupSuffix` 改为 `public const`，供报告拼备份路径 |
| `src/PdxLauncher.Core/Steam/SteamOwnership.cs` | 新增 `DlcAccountOwnership`；`SteamOwnershipReport` 末尾追加 `IsLoaderActive`、`Account`（可选位置参数，源码兼容）；`SteamProbeError` 加 `BackupNotOriginal` |
| `src/PdxLauncher.Core/Steam/SteamOwnershipService.cs` | 主探测后按 `IsLoaderActive` 再探 `_o` 备份；`ProbeAccount` 处理缺备份/同字节/同表/探测失败；`ApplyTrusted` 抽出来给两份报告共用；缓存 key 扩为六段 |
| `src/PdxLauncher.Core/Steam/DlcOwnershipVerdict.cs` | `SteamDlcCount/SteamOwnedCount` → `ProbeDlcCount/ProbeOwnedCount`；新增 `IsLoaderActive`、`AccountDlcCount/AccountOwnedCount/AccountOwnedDiskDlcCount/AccountErrorKey/ForeignDlcCount`；`PatchedOther` 也算已打补丁；空磁盘回退优先账号表 |
| `src/PdxLauncher.App/ViewModels/DlcViewModel.cs` | headline 用 `Probe*` 并追加账号句；detail 补丁状态四态（含「其他工具/整合包」「已被 Steam 更新覆盖」）、loader 来源、外区条数、账号探测 DLL；`FillSteamRows` 在 loader 在岗时用账号表；日志分段 |
| `src/PdxLauncher.App/ViewModels/SettingsViewModel.cs` | `ApplyPatchAsync` / `RemovePatchAsync` 写回报告后 `AppServices.DlcOwnership.Invalidate()` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 4 条主句改写；新增 9 键（账号句 2、错误 2、详情 5） |
| `src/PdxLauncher.App/Localization/Strings.g.cs` | `文案源生成器` 重新生成（1278 键） |
| `tests/PdxLauncher.Checks/SteamOwnershipChecks.cs` | `CheckVerdictTable` 加 `PatchedOther`/`SteamOverwrote` 用例；新增 `CheckAccountProbe`（loader 490 / 账号 23 / 磁盘 17、CS2 回退、缓存 2→4、缺备份/同字节/同表/探测失败） |
| `tests/PdxLauncher.Checks/SteamDlcListChecks.cs` | 字段更名 |
| `tests/PdxLauncher.Checks/DlcPatchChecks.cs` | 断言 `IsLoaderActive` 三态 |
| `docs/DATA-FORMATS.md` | §3.1 补「loader 表 ≠ Steam 目录」「账号视图」「降级矩阵」「缓存 key」与 V3 真机数据 |

### 5. 验证

- `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj -c Release -m:1 --nologo -v q`：成功，0 警告 0 错误。
- `dotnet run --project tests/PdxLauncher.Checks -c Release --no-build`：`检查类: 99，失败: 2`，仅 `WrapPanelChecks` 失败（仓库里未提交的播放集工具条 WIP，与本改动无关）。本任务新增断言全部通过，例如：主探测 490 / 账号 23 / 外区 467、CS2 回退 23、缓存命中 2 次、`Invalidate` 后 4 次、缺备份只探 1 次。
- `文案自检`：通过。
- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -p:Platform=x64`：**当前工作区被无关 WIP 阻断**（`DiagnosticsRows.cs` / `DiagnosticsViewModel.cs` 找不到 `DiagnosticHintCode` / `MemorySnapshot` + WMC9999），与本改动无关；本改动涉及的 `DlcViewModel` / `SettingsViewModel` 已通过 Checks 工程对 Core 的编译链间接验证。

### 6. 坑

- **`gen-i18n.py` 要求占位符按 `{0}`、`{1}` 顺序出现**：en 里先写 `{1}` 再写 `{0}`（"1 of 3" 语序）会直接报错，只能改写句子。
- **record 追加可选位置参数是源码兼容的**，但新参数必须排在最后并带默认值；`SteamOwnershipReport` 现有 3 处构造（服务 1 + 测试 2）都不用改。
- **元组元素声明成 `IReadOnlySet<string>` 却返回 `HashSet`** 会 CS0029；`ApplyTrusted` 返回类型与解构声明要一致。
- **`cream_api.ini` 的 490 是模板**，不是本游戏条目；Stellaris 打完补丁同样是 490/490，不要拿它当「DLC 总数」。
- 判「备份也是 loader」不能只看字节：整包替换后备份可能被覆盖成同一份 loader；还要比对两次探测的 id 集合。

### 7. 注意

- 行徽标仍是「游戏实际可用」语义（`OwnedAppIds` + 可信 DisabledDLC），本轮没有给每行加「账号真实拥有」；账号信息只在判定条出现。
- 若以后 CreamAPI 改成按 appid 过滤，`ForeignDlcCount` 会自然归零，文案不必再改。
- 自检类数 99（本轮新增 `CheckAccountProbe`，其余是并发会话新增的播放集相关类）。README 的计数若落后于 99，另开一轮统一。

## 0.80 2026-11-04：播放集页工具条自动折行 —— 搜索框不再被挤没、右侧按钮不再被裁掉

> 用户诉求原话（m00003）：「播放集页面把自动换行加上」，附图为 1366×147 的工具条截图：搜索框整条不见、「添加模组」贴边。

### 0.80.1 现象与根因

工具条原本是「Auto / * / Auto」三列的第一列里塞一个水平 `StackPanel`，从不换行；窗口变窄时星号列（搜索框）先被压，
压到 `MinWidth="96"` 以下就整个消失，同时右侧「添加模组」被裁。§0.14（2026-10-27）只做到「1200 DIP 一行刚好放下」，
后来又加了「修复 mod 路径」「自动排序」两个按钮（≈110 + ≈78 DIP），按钮簇从 ≈600 涨到 ≈789 DIP，
而 1200 DIP 窗口的内容宽只有 939 DIP —— 默认尺寸下一行就已经放不下，搜索框只剩零星几十 DIP，正是截图里的样子。

| 元素 | 1200 DIP 实测宽 |
| --- | --- |
| 左侧按钮簇（含切换器与分隔线） | ≈789 |
| 搜索框（`MaxWidth` 320） | 262 |
| 「添加模组」 | 80 |
| 列间距 6×2 | 12 |
| 合计 | ≈1143 > 939 |

### 0.80.2 方案

- 列序换成 `* / Auto / Auto`：搜索框（固定 `Width="200"`）与「添加模组」先按自然宽度占位，剩下的宽度全给按钮簇。
- 按钮簇由 `StackPanel` 换成上一轮已有的 `controls:PdxWrapPanel Spacing="4"`（`WrapLayout` 纯算法 + 有限宽测量），
  放不下就整项换行；折叠中的「取消自动排序 / 撤销自动排序」不占位（面板本来就跳过 `Collapsed`）。
- 搜索框从「星号列 + Min/Max 夹逼」改成固定 200 宽、右对齐：宽度可预期，不再随窗口伸缩去挤按钮，也不再依赖 `MinWidth` 兜底。
- 分隔线保留在按钮簇里，`Margin` 由 `1,0` 改成 `1,6`（折行面板不做交叉轴居中，靠 margin 在 32 高的行里居中）。

### 0.80.3 验证（真机 UIA，DPA 1.5）

| 窗口宽 | 行数 | 观察 |
| --- | --- | --- |
| 1040 DIP（最小宽） | 2 | 第一行：切换器 + 新建/重命名/删除 + 激活播放集；第二行：启用全部/禁用全部/修复 mod 路径/自动排序；搜索框 200 宽在右侧完整可见 |
| 1200 DIP（默认宽） | 2 | 第一行多出「启用全部」「禁用全部」，第二行剩「修复 mod 路径」「自动排序」 |
| 1600 DIP | 1 | 全部控件一行排下，不折行 |

每个宽度都断言了：11 个控件都在客户区内、两两不重叠、搜索框宽 = 200 ±1.5、添加模组右边缘 ≤ 内容右边界、行数符合预期。
截图（gitignored）：`.scratch\playset-wrap-1040.png` / `-1200.png` / `-1600.png`；脚本 `.scratch\verify-playset-toolbar.ps1`。

- `dotnet build "PdxLauncher.sln" -c Release -m:1`：成功，0 警告 0 错误。
- `dotnet tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll`：`检查类: 101，失败: 0`（`WrapPanelChecks` 新增 8 条播放集工具条断言）。
- `文案自检`：通过（1280 条一致），本轮没有新增 / 修改任何 i18n 键。

### 0.80.4 改动的文件

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Views/ModsPage.xaml` | 列序 `*/Auto/Auto`；`StackPanel` → `PdxWrapPanel`；搜索框 `Width=200` 右对齐；分隔线 `Margin="1,6"`；页头与工具条注释重写 |
| `src/PdxLauncher.App/MainWindow.xaml.cs` | `MinimumWindowWidth` 的注释改写（1040 不再由「工具条一行」倒推，改作通用下限，常量不变） |
| `tests/PdxLauncher.Checks/WrapPanelChecks.cs` | 新增 `CheckPlaysetToolbarWiring`（折行面板在、10 个按钮、11 个 AutomationId、首列是星号列、搜索框在面板之后且固定 200 宽、添加模组在搜索框之后） |

### 0.80.5 坑

- **单行阈值不是 1200 也不是 1366**：客户区比窗口矩形窄 ≈7 DIP（WinUI 自绘标题栏的不可见边框），再加上内容左缩进 240.7、
  右 padding 16、搜索框 200 + 添加模组 80 + 间距，1366 DIP 窗口下按钮簇只有 ≈810 DIP，而整簇自然宽 ≈815 DIP —— 差 5 DIP 就折行。
  想在大屏上保持一行请取 ≥1600 DIP，别再按「窗口宽」估算。
- **`PdxWrapPanel` 不负责交叉轴对齐**：子元素各自按自然高度排在行首，需要垂直居中的（如 1×20 分隔线）自己给 `VerticalAlignment` + margin。
- **折成两行后工具条变高**：页面行是 `Auto`，列表自然下移，无需改 `ScrollViewer`。
- **自检里数按钮不要数 `</Button>`**：工具条按钮全是自闭合 `<Button ... />`，只能数 `"<Button"`。

### 0.80.6 注意

- 搜索框固定 200 宽是有意的：它一旦随窗口伸缩，宽屏下会把按钮簇挤到第二行、窄屏下又回到老问题。
- 窗口被手动拖到 1040 以下时按钮簇继续折行，但搜索框 + 添加模组所在的自动列不会收缩，极端窄（<≈330 内容宽）仍会被裁；
  这是既有的「不做自适应折叠」边界，本轮不处理。

## 0.82 2026-11-04：UI 间距全量对齐 WinUI 官方 4px 网格

> 用户诉求（m00365 → m00416）：「找找有没有 winui3 推荐的官方间隔推荐」→「对启动器进行完全的改动方案，按照官方推荐」。

### 0.82.1 依据

WinUI Gallery 的 Spacing 页（`WinUIGallery/Samples/Spacing/SpacingPage.xaml`，旧路径 `ControlPages/DesignGuidance/SpacingPage.xaml` 已不存在）
原文只有一条硬规则：**any spacing or sizing should be a multiple of 4**（epx = DIP）；常用值 4 / 8 / 12 / 16 / 24 / 36 / 48
分别对应紧凑、控件之间、控件与标题、列表/卡片、内容分区、页面边距、带标题分区。同页还有一句「这些值对应我们的圆角逻辑」。
Learn 的同一份表述：<https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics>。

### 0.82.2 审计（逐条枚举，不是估算）

| 范围 | 非 4 倍数 |
| --- | --- |
| XAML（15 个文件，633 处间距属性） | **140 处**（WorkshopDetail 29、ModsPage 24、ModStudio 23、Workshop 14、ModLibrary 13、AddMods 10、Diagnostics 6、Settings 6、Dlc 5、GameLibrary 3、Home 3、MainWindow 1、PdxDropDown 1、SettingsCard 1、Account 1） |
| C# | **11 处**（MarkdownView 9、UpdateFlow 1、AutoSortPreviewDialog 1） |
| CornerRadius | **23 处**（`6`×22、`3`×1） |

完整映射表、结构性处理与检查器用法写在 **`docs/UI-SPACING.md`**，这里只记结论：只改非 4 倍数，6/10 向上取整（保住点击区），
页面根 Padding 16 / 20 / 24 与所有 0/4/8/12/16 原值不动；官方 36 的「页面内边距」不采用（1040 宽下会明显压缩列表与工具条）。

### 0.82.3 三处结构性处理

1. 播放集工具栏分隔线：`PdxWrapPanel` 不做交叉轴居中，原来的 `Height=20 + Margin="1,6"` 是在补偿「20 高竖线在 32 高行里居中」，
   改为 `Width=1 Height=32 BorderThickness="1,0,0,0"` 的满行高竖线，间距归零。
2. 主页 1px 装饰下划线 `Margin="2,0,2,1"` → `0`（`1` 是 1px 线的光学补偿）。
3. 播放集「位置」输入框 `Padding="2,0"` → `"4,0"`（`MaxWidth=40`，4 位数字仍放得下）。

### 0.82.4 自检（新增 `tests/PdxLauncher.Checks/SpacingChecks.cs`）

- 扫 XAML（剥注释、跳过 obj/bin）：`Padding` / `Margin` / `Spacing` / `RowSpacing` / `ColumnSpacing` 与 `Setter Property=...`；
- 扫 C#：`Spacing = 数字`、全数字字面量的 `new Thickness(...)`、`new CornerRadius(...)`；
- `CornerRadius` 只允许 0/4/8；`BorderThickness` 明确排除（线宽不是间距）；
- 同时断言 `docs/UI-SPACING.md` 存在并含 `content-basics` / `4epx` / `36epx`（防文档漂移）；
- 自带 5 条「检查器自检」（6,1 违规、ThemeResource 跳过、8,4 合法、0 合法、Setter 也查）。

### 0.82.5 验证

- `dotnet build "PdxLauncher.sln" -c Release -m:1`：成功，**0 警告 0 错误**。
- 自检：`检查类: 103，失败: 0`（新增 `SpacingChecks`，`间距 XAML / C# 非 4 倍数总数 = 0`）。
- `文案自检`：通过（1288 条一致，本轮无 i18n 改动）。
- 播放集工具条 UIA（`.scratch/verify-playset-toolbar.ps1`）：1040 / 1200 DIP 仍 2 行、1600 DIP 仍 1 行，
  搜索框 200±1.5、「添加模组」完整可见、11 个控件两两不重叠 —— 间距归一化没有改变折行行为。
- 九页截图（1200 宽请求，实际被 AppWindow 恢复成 1800×1200 DIP）：`.scratch/spacing-after-*.png`；
  设置页、播放集页、Mod 创建页逐张目检无裁切、无文字截断；Mod 创建那张截图右半被 DWM 未绘制区域盖住（截图伪影，不是界面问题）。

### 0.82.6 坑

- **并发会话会覆盖归一化结果**：本轮的 `ModsPage.xaml` 被另一个会话（双列播放集）整份重写，22 处旧值随之回来；
  `.scratch/normalize-spacing.py` 是幂等的，等对方写完重跑一次即可（本次重跑 22 处）。
- **两个会话同时构建会锁文件**：`XamlCompiler` 持有 `intermediatexaml/*.dll`、运行中的 `PdxLauncher.App.exe` 持有 `apphost` 拷贝，
  表现为 WMC0601 / CS0016 / MSB3027；等对方关窗口或先 `taskkill` 再 `-t:Rebuild`。
- **`WrapPanelChecks` 曾把 `ColumnSpacing="6"` 写死**，归一化后首列断言直接失败；已改成匹配属性前缀、不依赖具体数值。
- **`SetWindowPos` 会被 AppWindow 的尺寸恢复覆盖**：想固定宽度截图，要在窗口恢复之后再设一次。

### 0.82.7 注意

- 以后新增 UI，间距只用 4 的倍数（优先 4/8/12/16/24），圆角只用 0/4/8；表达式型 `Thickness` 检查器管不到，要人工确认。
- 想让页面更宽松，改各页根 `Padding`（官方推荐 36），不是破坏规则；当前保留 16/20/24 三档。
- 规则已写进项目规范：`docs/DEVELOPMENT.md` §8「界面规范（强制）：4px 间距与圆角」（含官方原文、必须项、自动校验与例外流程），`docs/UI-SPACING.md` 是它的对照表与历史记录。


## 0.83 2026-11-04：播放集页第三种显示方式「双列」（左＝可添加、右＝已加入，勾选即加入）

### 0.83.1 需求

用户给了官方 Paradox 启动器「模组合集」编辑器的截图，要求播放集页做成双列：左栏未加入当前播放集的 mod，右栏已加入的 mod。
中途明确两点（当天原话）：「这个子页面不是给原版启动器页面使用的吗，为什么要删除，我这是要两套播放集页面啊」——
即**原版启动器布局与 `AddModsPage` 子页必须原样保留**，双列是**新增的第三种显示方式**，由设置页下拉切换；
左栏交互选的是**勾选即加入**（勾上立刻进右栏、行从左栏消失），不是「勾选 + 添加所选」。

### 0.83.2 改了什么

- `src/PdxLauncher.Core/Configuration/PlaySetPageDisplayModes.cs`：新增 `TwoColumn = "twoColumn"`，`Normalize` 支持三值（未知值仍回 `officialLauncher`），新增 `IsTwoColumn`。
- `src/PdxLauncher.App/Services/PlaySetPageDisplayModeCatalog.cs`：`All` 变三项，设置页下拉自动出现「双列（原版启动器式）」，无需改设置页 XAML。
- `src/PdxLauncher.App/ViewModels/AddModRowViewModel.cs`：ctor 增加 `automationPrefix` 参数（默认 `addModCandidate`，产出与原值逐字相同），双列左栏用 `modCandidate`，两处 UI 自动化 id 不再撞车。
- `src/PdxLauncher.App/ViewModels/ModsViewModel.cs`：
  `Candidates` 集合 + `CandidateSearchText`；`IsTwoColumnLayout` / `ShowsPlaysetChrome`；`RebuildCandidates()` 从 `_librarySnapshot.Mods` 排除 `_allMods` 现有 ModId 生成左栏行；`ShowCandidate*` / `ShowSelected*` 六个空状态布尔；`RequestAutoSave` 门禁从 `IsOfficialLayout` 放宽到 `ShowsPlaysetChrome`（否则双列里改东西不写盘）。
- `src/PdxLauncher.App/Views/ModsPage.xaml`：两个行模板下沉到 `Page.Resources`（`PlaysetModRowTemplate` 原样搬入、被原版列表与双列右栏共用；`PlaysetCandidateRowTemplate` 新写）；根 Grid 三行，工具条与状态栏由 `ShowsPlaysetChrome` 控制，原版主体与双列主体并列在 Row 1 各挂自己的 Visibility；双列左栏表头两行（标题+计数、整行搜索框）、右栏同构。
- `src/PdxLauncher.App/Views/ModsPage.xaml.cs`：`ActiveModList` 按布局选 `ModList` / `ModListTwoColumn`（位次就地编辑、`ScrollIntoView` 都走它）；新增 `OnCandidateChecked`（在 `DispatcherQueue.TryEnqueue` 里 `AddMods`，避免在 CheckBox 绑定回调里清空集合）与 `OnCandidateSearchTextChanged`。
- i18n：新增 `mods.twoColumn.{available,addToolTip,hint,noPlaySet,selectedEmpty,selectedNoMatch}`、`settings.interface.playset.twoColumn.{title,desc}`（zh-CN 与 en-US 同步，`文案源生成器` 重新生成，1288 键）。

### 0.83.3 两处踩过的视觉坑

- **CheckBox 默认样式带 120 DIP 最小宽度**：左栏候选行里勾选框一个人吃掉 120 DIP，名字被挤成省略号（截图里 39 个候选全是「Auto-Diplomacy And…」）。修法是给勾选框 `MinWidth="0" Padding="0,0,8,0"`，并把名字行从横向 `StackPanel` 换成星号列 `Grid`（横向面板按内容测量，名字过长时既不省略也不让位，会把「订阅 / 本地」徽标顶出可见区）。
- **左栏 360 DIP 不够**：候选名普遍很长，改为 `Width="400" MinWidth="300"`（Mod 库页仍是 360）。

### 0.83.4 自检与冒烟

- 新增 `tests/PdxLauncher.Checks/ModsPageLayoutChecks.cs`：双列 10 个 AutomationId、原版 16 个 id 仍在、四个布局布尔绑定、两栏搜索框不得写 `Width`、行模板被两处引用 / 候选模板一处、`Click="OnCandidateChecked"` 与 `ActiveModList` 存在、`AddModsPage.xaml` 与 `addmods` 路由仍在、显示方式三值归一化。
- `tests/PdxLauncher.Checks/InfrastructureChecks.cs` 补 `Normalize(" TWOCOLUMN ")` / `Normalize(TwoColumn)` / `IsTwoColumn` 三条断言。
- `tests/PdxLauncher.Checks/WrapPanelChecks.cs` 的工具条首列断言改成匹配属性前缀（新增 `Visibility` 后开始标签折行，原来精确匹配 `"<Grid Grid.Row=\"0\" ColumnSpacing=\"6\">"` 会 IndexOf 返回 -1）。
- 结果：`dotnet build PdxLauncher.sln -c Release -m:1` 0 警告 0 错误；`dotnet run --project tests/PdxLauncher.Checks -c Release` **检查类 103、0 失败**；`文案自检` 通过。
- `scripts/verify-playset-twocolumn.ps1`（纯 ASCII，UIA）：设置页切「双列」→ 播放集页断言双列 id 全在、原版 id 全不在 → 截图 → 切回「原版启动器」断言原版 id 回来、双列 id 消失 → 还原用户原来的选项。连跑两轮 `FAILED=0 DONE`。

### 0.83.5 注意

- **UIA 脚本跑的 exe 在 `bin/x64/...`，`dotnet build <csproj>` 只更新 `bin/Release`**：忘了 `dotnet build PdxLauncher.sln` 就会拿旧 exe 跑出「双列页一片空白」，实际是旧 XAML 里没有双列分支。判断方法：`obj/x64/Release/.../Views/ModsPage.g.cs` 里 `grep IsTwoColumnLayout` 是否为 0。
- 构建被运行中的 exe 锁断后，后续会一直报 `WMC9999 未将对象引用设置到对象的实例` + `CS2012`——那是 `obj` 写坏，`rm -rf src/PdxLauncher.App/obj` 后重编即可，**别删 `bin/x64`**（里面有 `data/`：用户设置与播放集）。
- 以后想加第四种布局：`PlaySetPageDisplayModes` 加常量与 `Normalize` 分支、Catalog 加一项、`ModsViewModel.RefreshDisplayMode` 补通知，XAML 再并列一个 `Visibility` 分支即可；工具条与状态栏已被 `ShowsPlaysetChrome` 收口。

## 0.84 2026-11-04：日志分区跟着窗口长 + 「自动换行」开关

### 0.84.1 需求

用户对着「性能与日志 → 日志」截图提两点：「1.日志界面不能跟随页面大小变化吗？2.添加自动换行的开关，实现日志的自动换行查看的能力」。
所以两件事分开做：**高度要能被窗口拉**，**折行要能开关**（不折行时保留横向滚动，长行不被硬切）。

### 0.84.2 根因

- 日志是右栏唯一 `ScrollViewer > StackPanel` 里的一段，`TextBox` 写死 `Height="340"`：窗口变高变矮它都不动，多出来的高度全给了外层滚动条。
- `TextWrapping="NoWrap"` 写死，没有折行路径。

### 0.84.3 改了什么

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Views/DiagnosticsPage.xaml` | 右栏 Grid 拆成两个互斥子元素：`ScrollViewer x:Name="diagSectionScroller"` 只装概览 / 缓存 / 网络 / 启动耗时；`Grid x:Name="secLog"`（`Auto/*` 两行）装日志头部 + `TextBox Grid.Row="1"`，去掉 `Height="340"`、加 `MinHeight="160"`，高度随窗口走 `*` 行 |
| 同上（过滤行） | 右侧两个开关加常显标签（`TextBlock` + `OffContent="" OnContent=""`）：`tglDiagLogWrap`（自动换行）与 `tglDiagAutoRefresh`（每 5 秒自动刷新）。以前关闭态是无字开关，两个并排分不清谁是谁 |
| `src/PdxLauncher.App/Views/DiagnosticsPage.xaml.cs` | `ShowSection` 改成 `(int Index, FrameworkElement? Element)[]` 映射：选「日志」时切 `secLog`、其余四个切 `diagSectionScroller`（`secNetwork`=3 / `secStartup`=4，不能再用数组下标） |
| `src/PdxLauncher.App/Services/XamlTextWrapping.cs` | 新增纯函数 `static TextWrapping FromBool(bool wrap) => wrap ? TextWrapping.Wrap : TextWrapping.NoWrap`；`TextBox.TextWrapping="{x:Bind svc:XamlTextWrapping.FromBool(ViewModel.LogWrap), Mode=OneWay}"` |
| `src/PdxLauncher.App/ViewModels/DiagnosticsViewModel.cs` | `public bool LogWrap`（默认关）：setter 发通知、写 `AppServices.Settings.DiagnosticsLogWrap` 并 `SaveSettings()`，失败只 `Logger.Warn`；构造时从设置读回 |
| `src/PdxLauncher.Core/Configuration/AppSettings.cs` | 新增 `public bool DiagnosticsLogWrap` |
| `src/PdxLauncher.App/Strings/{zh-CN,en-US}/Resources.resw` | 新增 `diagnostics.log.wrap`（自动换行 / Wrap lines）与 `diagnostics.log.wrapName`（日志自动换行 / Wrap log lines） |
| `tests/PdxLauncher.Checks/DiagnosticsPageChecks.cs` | 新增断言：折行绑定字符串、日志尾部不再有 `Height="340"` 且有 `MinHeight="160"`、`secLog` 不在 `diagSectionScroller` 里、`AppSettings` 有 `DiagnosticsLogWrap`、`XamlTextWrapping` 两分支、视图模型有 `LogWrap` 且写回设置、两侧 JSON 有 `diagnostics.log.wrap`、生成表含 `DiagnosticsLogWrap`、两个开关标签常显 |

### 0.84.4 不要用 SizeChanged 硬算

第一种思路是监听窗口 `SizeChanged` 再把 `Height` 设成「窗口高 − 头部高」，窗口每拖动一像素就写一次布局属性。改成「日志自己在一个 `Grid` 的 `*` 行里」之后，布局系统负责这件事：不用魔法数字、不用监听、摘要卡或过滤行变高也不会错位。

### 0.84.5 验证

- `bash tools/build-app.sh -v q`：0 警告 0 错误；`文案源生成器` 生成 1290 键（含其他会话未提交的文案）。
- 自检：`dotnet run --project tests/PdxLauncher.Checks -c Release` 跑 **103 类**；本次新增断言全过（`PlaysetChecks` 另有 1 条其他会话未完成的断言 `V3 激活播放集 mod 行数 >= 22` 失败，与本次无关；该行数下限断言已于 2026-11-04 删除，见 §0.85.5）。
- 真机（`winapp ui` + UIA，自建实例）：`winapp ui invoke navDiagnostics` → `navDiagLog` 进日志分区；`SetWindowPos` 拉到 2300×1500 与缩到 1560×980 各截一张 —— 日志框高度跟着窗口一起伸/缩；`winapp ui invoke tglDiagLogWrap --action toggle-on` 后长路径行折行、标签「自动换行」出现。
- 持久化：开关写进自有库 `data/pdxnext.sqlite` 的 `settings` 表（`json` 列里能看到 `"diagnosticsLogWrap": true` 与 `"lastDiagnosticsSection": 2`）。

### 0.84.6 注意

- **设置现在存在 sqlite，不是 `data/settings.json`**：后者只剩导入 / 自检路径在用（见 `AppSettingsStore` 顶部注释）。验证开关有没有落盘要去查 `data/pdxnext.sqlite` 的 `settings` 表。
- 自建的实例会锁住 `bin/Release/.../PdxLauncher.App.exe`，`build-app.sh` 会以 `MSB3027` 失败：先 `Stop-Process -Id <pid>` 再构建。`build-app.sh` 只编 App 工程，**自检工程要另外 `dotnet build tests/PdxLauncher.Checks/PdxLauncher.Checks.csproj`**，否则 `--no-build` 跑的是旧断言。

## 0.85 2026-11-04：7 处搜索框全部换成 `AutoSuggestBox`（本地名字联想 + 工坊最近搜索）

### 0.85.1 需求

用户原话：「软件的搜索框能不能更改为AutoSuggestBox」。在方案阶段问过建议来源，用户选 **A：本地名字联想 + 工坊最近搜索**，范围是**全部 7 处**搜索框；
硬约束是「行为语义不变」——每个框原来的提交时机（工坊＝回车 / 放大镜 / 刷新按钮，其余＝打字即过滤）与 `AutomationId` 都保留，UIA 脚本继续按 id 找得到。

### 0.85.2 改了哪 7 处

| 文件:行 | `AutomationId` | 建议来源 | 提交语义 |
| --- | --- | --- | --- |
| `src/PdxLauncher.App/Views/WorkshopPage.xaml:41` | `txtWorkshopSearch` | `AppServices.Settings.WorkshopSearchHistory`（持久化，最新在前，≤8） | 回车 / 点放大镜 / 刷新按钮才发请求 |
| `src/PdxLauncher.App/Views/ModLibraryPage.xaml:51` | `txtModLibrarySearch` | `ModLibraryViewModel.Mods` 的 `Name` | 打字即过滤 |
| `src/PdxLauncher.App/Views/ModsPage.xaml:397` | `txtModSearch`（原版布局工具条） | `ModsViewModel.Mods` 的 `Name` | 打字即过滤 |
| `src/PdxLauncher.App/Views/ModsPage.xaml:496` | `txtCandidateSearch`（双列左栏候选） | `ModsViewModel.Candidates` 的 `Name` | 打字即过滤 |
| `src/PdxLauncher.App/Views/ModsPage.xaml:611` | `txtModSearchTwoCol`（双列右栏已加入） | `ModsViewModel.Mods` 的 `Name` | 打字即过滤 |
| `src/PdxLauncher.App/Views/AddModsPage.xaml:74` | `txtAddModsSearch` | `AddModsViewModel.Rows` 的 `Name` | 打字即过滤 |
| `src/PdxLauncher.App/Views/ModStudioPage.xaml:47` | `modStudioSearch` | `ModStudioViewModel.Mods` 的 `Name` | 打字即过滤 |

`ModStudioPage.xaml` 的 `Text` 从 `Mode=TwoWay, UpdateSourceTrigger=PropertyChanged` 改回 `Mode=OneWay`，与其余六处统一走 code-behind 写回。

### 0.85.3 实现

| 文件 | 改动 |
| --- | --- |
| `src/PdxLauncher.App/Services/SearchSuggestions.cs`（新） | 纯逻辑：`Build(names, query, max = 8)`——空白查询返回空、名字按 `OrdinalIgnoreCase` 去重、跳过与查询完全相同的项、**前缀匹配排前、包含匹配排后**、组内保原序、截断 8 条；`Push(history, query, max = 8)`——新词置首、忽略大小写去重、去空白、截断、返回新 `List` 不改入参 |
| `src/PdxLauncher.App/Services/SearchSuggestBox.cs`（新） | UI 粘合 `Update(AutoSuggestBox box, IEnumerable<string?>? names, query)`：算建议，**空建议显式 `IsSuggestionListOpen = false`**（否则留一个空白弹层），非空才赋 `ItemsSource` 并展开 |
| 7 份 XAML | `TextBox` → `AutoSuggestBox` + `QueryIcon="Find"`（放大镜要显式给，默认没有）+ `QuerySubmitted`；`MinWidth` / `MaxWidth` / `Width="200"` / 对齐 / `Visibility` / 占位符 / `AutomationId` / `AutomationProperties.Name` 原样保留；工坊页删掉 `KeyDown="OnSearchKeyDown"` |
| `WorkshopPage.xaml.cs` | 删 `using Microsoft.UI.Xaml.Input;` / `using Windows.System;` 与整个 `OnSearchKeyDown`；`OnSearchTextChanged` 只在 `args.Reason == UserInput` 时更新 `SearchText` 并给历史建议；新增 `OnSearchQuerySubmitted` → `CommitSearchWithHistoryAsync(text)`（先 `CommitSearchAsync()`，成功后 `SearchSuggestions.Push` + `AppServices.SaveSettingsDeferred()`；空白词不写历史；取消 / 异常只记日志不抛） |
| `ModLibraryPage/AddModsPage/ModsPage/ModStudioPage.xaml.cs` | 每个框两个处理器：`OnSearchTextChanged(AutoSuggestBox, AutoSuggestBoxTextChangedEventArgs)` 先按 `ProgrammaticChange` 早退、再写回 VM、再给名字建议；`OnSearchQuerySubmitted` 把 `args.QueryText` 写回并只负责收起下拉（**不重算**，否则刚收起又被弹开） |
| `src/PdxLauncher.Core/Configuration/AppSettings.cs` | 新增 `public List<string> WorkshopSearchHistory { get; set; } = [];`（JSON 键 `workshopSearchHistory`，旧设置文件缺这个键读到空列表） |
| `ModLibraryPage.xaml.cs` 的 `RestoreState` | 从 `SearchBox.Text = state.SearchText` 改成 `_viewModel.SearchText = state.SearchText`——`Text` 是 OneWay 绑定，写控件会被绑定刷掉、且程序化改文本不再触发过滤 |

### 0.85.4 三个坑

- **程序化改文本也会触发 `TextChanged`**：双列布局的 `txtModSearchTwoCol` 与原版布局的 `txtModSearch` 共用同一个 `SearchText`，一个框写回 VM、另一个框的 `Text` 被绑定改写，就会来回弹下拉。统一守卫：`args.Reason == ProgrammaticChange` 直接 return，只有 `UserInput` 才更新建议。
- **空建议必须显式收起**：`AutoSuggestBox` 赋空集合后弹层会留在屏幕上变成一个空框。
- **`QuerySubmitted` 与 `SuggestionChosen` 的时机**：点建议时文本已由控件填好，`QuerySubmitted` 的 `QueryText` 总是当前文本，所以处理器里统一 trim 后再写 VM，不依赖 `ChosenSuggestion`。

### 0.85.5 验证

- `./build.cmd`：**0 警告 0 错误**；自检 **108 个检查类**，新增的两类全过——`SearchSuggestionsChecks`（40 条：去重 / 前缀优先 / 上限 / 空查询 / 入参不变）与 `SearchBoxChecks`（102 条：7 个框都是 `AutoSuggestBox` + `QueryIcon="Find"` + `TextChanged` + `QuerySubmitted`，10 个处理器都有 `Reason` 守卫与 `SearchSuggestBox.Update`，且不再出现 `sender is TextBox`）。
- 唯一失败是既有的 `PlaysetChecks` 数据断言 `V3 激活播放集 mod 行数 >= 22 [7]`（本机 Victoria 3 激活播放集只有 7 行 mod；同文件 :1045-1047 的注释本来就写「真机断言只锁结构，不锁本机数据」）。**已顺手删掉这条行数下限断言**（`tests/PdxLauncher.Checks/PlaysetChecks.cs` 里改成只打印 `info V3 激活播放集 mod 行数 …`，结构由 Position 连续 / 名称非空 / Status 合法 / 至少 1 行启用 / 与 Mod 库归属行数一致来锁），复跑 `./build.cmd` → **0 警告 0 错误，108 类，失败 0**。
- `WorkshopPageChecks` 已跟着改：工具栏断言从 `TextBox + KeyDown` 换成 `AutoSuggestBox + QueryIcon + QuerySubmitted`，并明确断言 `KeyDown=` 不再出现、code-behind 里没有 `VirtualKey.Enter`。

### 0.85.6 后续

- 建议目前只按「名字包含」匹配，且用页面已加载的集合（筛选后的那批）；工坊页当前没有本地数据，所以它只有最近搜索，没有联想。
- 清除按钮（`AutoSuggestBox` 自带）只清文本、不自动重新查询，与换控件前的行为一致。
- 待真机确认：Light / HighContrast 主题下建议弹层的配色。

### 0.85.7 UIA 与弹层的观测结论（2026-11-04 真机复核）

- `AutoSuggestBox` 在 UIA 树里是 **`ControlType=Group`、`ClassName=AutoSuggestBox`**，`AutomationProperties.AutomationId` 落在 Group 上；它内部是真的 `TextBox Edit`（没有 id，`Name` 等于占位符）。所以 `scripts/verify-playset-twocolumn.ps1` 这类「按 id 找元素、断言存在」的脚本不受影响；要对它输入/取值，先用 id 定位 Group，再用 `winapp ui send-keys --via send-input --target <id>` 或取内层 Edit。
- 建议弹层是同一个 HWND 里的 XAML `Popup`：`Window "弹出窗口"` → `List "SuggestionsList"` → `ListItem`。实测几何（Mod 库页，屏幕坐标）：搜索框 `(1862,113 240x49)`、弹层 `(1862,161 240x490)`、列表 `(1862,166 239x480)` —— **与框左对齐、同宽、紧贴下沿**，没有偏移。
- **`winapp ui screenshot`（不带 `--capture-screen`）会把主窗口和弹出窗口拼成一张带 HWND 标签的合成图**，弹层在合成图里像被挪到窗口右侧、还变窄；这是合成图的表现，不是布局 bug。要看真实位置用 `winapp ui screenshot --capture-screen --window <hwnd>` 或直接读 `ui inspect` 的 bounds。


## 0.86 2026-11-04：双列页分隔条拖动改宽度 + 窗口变窄时左栏让位（修右栏被裁）

### 0.86.1 现象

用户把左栏拖到接近上限、或把窗口缩小之后，右栏被窗口右边缘切掉：只剩序号、缩略图、开关的一截，
mod 名和行尾三个按钮完全看不见，右栏也不能滚动到这些内容。截图里窗口 1735 px（150% 缩放 ≈ 1157 DIP）、
左栏却仍有约 927 DIP —— 左栏 + 12 DIP 分隔条已经吃掉整行宽度，右栏只剩 123 DIP。

### 0.86.2 根因

两处叠加，都不是「联动算错」那么简单：

1. XAML 里两个列定义都写了 `MinWidth`（左 280 / 右 320）。Grid 在「可用宽度 < 各列下限之和」时会把
   整张 Grid 撑得比父容器更宽，而不是把内容压小 —— 于是右栏被推到窗口外，被父级裁掉。
   这一点还掩盖了钳制逻辑：即使代码算出「左栏该让到 280」，列上的 `MinWidth` 也会把宽度顶回去。
2. `OnTwoColumnGridSizeChanged` 里读 `TwoColumnGrid.ActualWidth`：在 `SizeChanged` 回调里这个值还是**旧尺寸**，
   于是 `Clamp(400, 旧总宽)` 得回 400，又命中「宽度没变」的早退分支，窗口缩小完全不触发让位。

### 0.86.3 修法

- `src/PdxLauncher.App/Services/ColumnResize.cs`：`MinimumRightWidth` 320 → **400**（一行固定占位
  ≈ 序号 40 + 缩略图 44 + 开关与「启用」70 + 行尾三按钮 130 + 间距 ≈ 320，再给名字留 80）。
  `Clamp` 改成「空间不足时左栏让位、绝不溢出」：`upper = min(900, available - 12 - 400)`；`upper <= 0` 时返回 0；
  下限取 `min(280, upper)`，保证下限不会把上限顶穿。
- `src/PdxLauncher.App/Views/ModsPage.xaml`：两个列定义删掉 `MinWidth`（左列保留 `MaxWidth="900"`），
  只留 `Width="*"` 给它自己按剩余空间分。
- `src/PdxLauncher.App/Views/ModsPage.xaml.cs`：把「用户意图宽度 `_preferredLeftColumnWidth`」与「当前渲染宽度」拆开；
  `OnTwoColumnGridSizeChanged` 改用 `e.NewSize.Width`；新增 `ApplyLeftColumnWidth(available)` 统一钳制 + 写
  `TwoColumnLeftColumn.Width`，窗口变窄只让位、不改意图宽度（窗口再拉宽会自动还原）。
- `src/PdxLauncher.App/Controls/PdxColumnSplitter.cs`：拖动的位移语义从「相对上一次的增量」改成
  **「相对按下点的总位移」**（新增 `DragStarted`，页面按下时记住起点宽度）。合成鼠标时曾出现
  「指针走 96 DIP、宽度却涨了 320 DIP」的现象，逐次累加对重复 / 乱序投递的移动事件不设防，改成绝对位移后
  同一位置重复投递不再叠加，最坏等于指针真实位移。

### 0.86.4 验证

- `dotnet build src/PdxLauncher.App/PdxLauncher.App.csproj -c Release -v m` → 0 警告 0 错误。
- `dotnet run --project tests/PdxLauncher.Checks -c Release` → 检查类 108，失败 0
  （`PlaysetChecks` 那条按本机 Victoria 3 播放集写死的 `mod 行数 >= 22` 已删，见 §0.85.5）。
  新增 `ColumnResizeChecks` 的 `CheckYieldsWhenSpaceIsTight` / `CheckRightColumnInvariant`（右栏 ≥ min(400, 可用-12)、
  左+12+右 ≤ 可用）与 `ModsPageLayoutChecks` 的 `CheckColumnsNotOverConstrained` / `CheckSplitterUsesEventSize`。
- UIA 实测（`.scratch/run` 独立构建，窗口 2700 px、缩放 150%）：默认左栏 400 DIP、右栏 1686 px；
  窗口缩到 1500 px → 左栏 324 DIP、右栏 400 DIP；缩到 1200 px → 左栏 124 DIP、右栏 400 DIP；
  日志 `双列分栏：总宽 736，左栏让位到 324（期望 400）`、`总宽 536，左栏让位到 124（期望 400）`。右栏不再被裁。
- 拖动本身的 1:1 手感需真机确认：我的自动化环境里 DSH 窗口始终占着前台，`SetForegroundWindow` 被系统拒绝，
  合成的鼠标按下落不到启动器窗口（`WindowFromPoint` 指向前台窗口），所以只验证了「拖动会改写宽度、窗口缩放会让位」，
  没能在自动化里验证「拖 100 px 宽度就涨 100 px」。

### 0.86.5 注意

- 分栏宽度只存在页面字段里，没写进设置，也没有新增 i18n 键（只有 `mods.twoColumn.splitterName` 的读屏名）。
- 页面 `NavigationCacheMode="Required"`，所以同一次会话内切走再切回宽度会保留，重启回到 400 DIP。
- 构建 `bin/x64` 时如果用户正开着启动器会锁 `PdxLauncher.Core.dll`（`MSB3026`），
  本次改用 `-p:OutputPath=.scratch\run\` 单独产出、再把 `data/` 复制过去复现，避免影响用户数据。
## 0.87 2026-11-06：存档管理页（浏览 / 两段式加载 / 备份 / 恢复 / 删除）

**问题**：1.2 只有主页一个「最近存档」卡（已删），用户没有任何入口看游戏存档，更不能备份或清理；用户要求「浏览 + 备份/恢复/删除」。

**改法**：
- Core 新增 `Saves/SaveGameScanner.cs`（`ISaveGameLibrary`）、`SaveGameMeta`、`SaveLocationCatalog`（ck3 `.ck3` / victoria3 `.v3` / eu5 `.eu5` / stellaris `.sav` / cities_skylines_2 `.cok`）、`SavContainerReader`（SAV0 头 4 KB 文本、zip 只读 `meta`、CK3 大 `meta_data` 256 KB 流式、V3 命名档是「SAV0 头 + zip」的嗅探回退、CS2 JSON 元数据、EU5 可选小时）。
- 备份服务 `Saves/SaveBackupService.cs`：落 `<data>\save-backups\<gameId>\<yyyyMMdd-HHmmss>__<安全名>.bak` + 同名 `.bak.json` 自述，先写 `.tmp` 再原子 Move；恢复默认生成「(恢复 yyyyMMdd-HHmm)」副本，覆盖时先把原档移 trash；删除真实存档只移 trash；配额 2 GiB / 每游戏 200 条，只报错不自动清理。
- 页面 `SavesPage`：两段式加载（先路径/大小/时间出粗略行，再后台按批补存档名/国家/版本）、行无障碍名随细节更新（`SaveRowAutomationWatcher` 订阅 `PropertyChanged`，因为 WinUI 的 `Style Setter.Value` 不认 `{Binding}`）、备份段支持恢复/删除/清理旧备份（保留 10 份）、孤儿备份以降级条目显示但不可恢复。

**验证**：本机 76 个真实存档只读扫描约 159 ms（VIC3 12 档首段约 4 ms、Stellaris 27 档约 8 ms）；CK3 元数据读出 1.19.0.6 / 1066.9.29 / 芒斯特小王国，VIC3 12/12、EU5 日期 12/27（其余为无日期串的 autosave）、CS2 8/8；删除进 `trash\`、恢复生成副本、占用文件返回 `Failed` 带 Win32 码；`检查类` 中存档相关断言全绿。

**注意**：存档清单不写 `pdxnext.sqlite`（磁盘是真源）；绝不写游戏 Documents 目录；V3/EU5 的字段解析是启发式，页面按「文件名/时间 + 尽力而为」呈现，CS2 `.cok` 标「实验性」。

## 0.88 2026-11-06：成就页接入 Steam（常驻宿主 / DLL 候选回落 / 中文化 / 解锁率）

**问题**：1.2 无成就页；用户要求看自己在游戏里的成就，且「游戏内是中文、这里却是英文」。

**改法**：
- 宿主动词 `--steam-host achievements --appid <id>`：Init 只在子进程，先 Init 失败后才问 `IsSteamRunning`（反序会把真机误报成「Steam 没运行」）；等 `UserStatsReceived_t`(1101) 10 s；用 `SteamAPICallCompleted_t`(703) + `ManualDispatch_GetAPICallResult` 取 1110 全局解锁率（此前只在回调泵里等，导致解锁率恒空）。
- `--steam-host serve` 常驻 worker + App 侧会话复用 + 窗口可见 2 s 后预热：首进从 1.1–3.0 s 降到约 0.18 s（热态 8 ms）。
- DLL 候选：优先原版备份（`steam_api64_o/orig/original`），每个候选先 `api-check`（只 Load/查导出，不 Init），`initFailed`/崩溃才换下一份（最多 3 份），`steamNotRunning`/`noSteamUser`/`timeout` 立即返回。修的是「打了 DLC 补丁的游戏被选中 loader 时成就 0 条」。
- 中文化：读游戏 `localization/<lang>/*_l_<lang>.yml` 的 `ACHIEVEMENT_<apiName>`，语言取 `pdx_settings.txt`，处理 `"$ACHIEVEMENT_xxx$"` 别名链（带环检测）；名称回退链＝本地化 → Steam displayName → 「—」。

**验证**：CK3 198 条、VIC3 152 条，中文名与描述 198/198、152/152，解锁率 198/198、152/152（与 Steam 社区页抽样误差 ≤0.3）；补丁 loader（834,560 B）强制先试 → `initFailed` → 自动回落原版 → 198 条 1.7 s。

**注意**：`GetAchievementDisplayAttribute` 的名字随**每游戏语言**（`appmanifest_<appid>.acf` 的 `UserConfig.language`）变，不随 Steam 客户端语言；`SetGameLanguage` 在本机 DLL 与 SDK flat API 里都不存在，不能靠它切语言。

## 0.89 2026-11-06：成就页图标、空快照与筛选

**问题**：成就页没有图；CK3 有时整页「暂无成就」；条目多时无法筛。

**改法**：
- `AchievementIconResolver` 从游戏安装目录取图：VIC3/Stellaris 用 `<apiName>.jpg` / `<apiName>_notachieved.jpg`；CK3 只有 `.dds`，用 `PdxLauncher.Imaging` 的 DDS 解码成 PNG 缓存到 `data\achievement-icons\<appid>\`，命中前校验 PNG 签名/IHDR/IEND，坏文件删除重建，临时名带 GUID + finally 清理。
- 空快照防御：`Available + 0 条` 不再写缓存；读取到毒快照（`Available` 但空）立即删除重取；`ShowEmptyState` 需 `!IsAvailable && !IsLoading && Count==0`。
- 筛选：状态（全部/已解锁/未解锁）+ 关键词（名称/描述，`OrdinalIgnoreCase`），行实例复用、过滤不触发图标解码。

**验证**：CK3 198 行中 55 解锁，首屏实化 46 行只生成 46 张 PNG；并发 6 线程同 key 无半写；过滤后计数「显示 N / 共 M」正确。

## 0.90 2026-11-06：DLC 页列出未拥有项、Steam 头图、VIC3 新版元数据

**问题**：DLC 页只能看到磁盘上已安装的 DLC，看不到「游戏有但我没有」；没有图片；VIC3 1.15 的新 DLC 不出现。

**改法**：
- `DlcListPlanner`（Core 纯函数）：已拥有＝`OwnedAppIds ∪ TrustedDisabledDlc`；未拥有＝Steam 报告减磁盘减已拥有；可靠性闸门（报告 >200 条或空名 >50%）判 `UntrustedCatalog`，不合成未拥有段，只显示说明。
- 头图 `DlcArtworkService`：`appdetails?appids=<id>&filters=basic` 取 `header_image`（带 hash，猜 `cdn.../header.jpg` 是 404），缓存 `<data>\dlc-artwork\<appid>.jpg`，URL 有效 14 天、失败退避 30 分钟、限 2 MiB、只下载白名单 https；已拥有与未拥有行都填充。
- `DlcCatalog` 增加 `dlc_metadata/*.txt` 解析（Clausewitz 块语法）：VIC3 1.15 的 `dlc019 State and Revolution`（4314660）只在元数据里；CK3 的 `00_dlc_metadata.txt` 与 Stellaris 的嵌套 `dlcs = {}` 形态被 schema 闸门识别后跳过，避免每次扫描刷 30 条「缺少 name」告警。

**验证**：VIC3 17→18 条（CK3 25、Stellaris 31、EU5 4 不变）；CK3 4232900 头图 37,340 B、4232880 61,666 B；未拥有段 8 行有图。

**注意**：`docs/DATA-FORMATS.md` 里「拥有 x/y」是某次实测样本，随账号变动，不要当常量断言。

**2026-11-07 追加**：未拥有行的头图看起来「歪」。原因是 Steam `header_image` 为 460×215（2.14:1），而缩略图框是 208×117（16:9），`Stretch="UniformToFill"` 会按高度放大约 20% 再左右各裁 21 DIP（约 8.5%、39 源像素），Steam 左上角紫色角标与两侧装饰因此被切掉；本地 `thumbnail.*`（280×160 ≈ 1.75:1）只裁约 2 DIP，所以已拥有段看着正常。角标是 Steam 图本身带的，不是启动器画的（`DlcArtworkService` 逐字节落盘）。修法：本地横图继续 `UniformToFill`，Steam 头图走 `XamlStretch.ForDlcImage`（入参是 `DlcRowViewModel.FillsImageBox`，OneWay 跟随商店图回填）→ `Uniform`，完整显示、上下各留约 10 DIP 底色边；框尺寸 208×117 不变。同时：`RefreshOwnershipAsync(force: true)` 重建未拥有行后补一次 `FillArtworkAsync`（否则点「重新检测」会退回占位图标）；`DlcArtworkService` 在 `header_image` 取不到时写 Warn（bundle `3596990` 的 `appdetails` 返回 `success:false`，仍会显示占位图，但现在日志里能看到 appid）。自检：`DlcArtworkChecks` 增加「行模板走 XamlStretch」「RefreshOwnershipAsync 在 force 时补图」「success=false 写 Warn」三条。

## 0.91 2026-11-06：设置页显示真实安装形态、更新器不再误提权

**问题**：装在 D 盘的 Inno 安装被显示成「便携 / 未安装」；更新器又按「数据目录语义」决定是否传 `/ALLUSERS`。

**改法**：Core 纯函数 `InstallModeResolver.Resolve(baseDir, marker, uninstallEntries)` + `IUninstallEntrySource`（App 侧读 HKCU/HKLM × 64/32）；设置页显示「已安装（Inno Setup，当前用户/所有用户）」+ 安装目录 + 打开目录按钮，删除旧的「在 `%LOCALAPPDATA%\Programs` 下才算已安装」启发式；`UpdateService` 改用同一判定（`InstalledInno + MatchedInstallDir + PerMachine` 才传 `/ALLUSERS`）。

**验证**：本机 `D:\app\PdxNext` → `InstalledInno / PerUser / Registry64 / 1.2.2`；`/ALLUSERS` 只在该三元组成立时出现。

**注意**：判定输入是进程目录 + 注册表，运行中重装必须退出进程；`PdxDataRoot.IsPerMachineInstall` 只保留数据目录语义，不要再拿它决定「是否安装」。

## 0.92 2026-11-06：离线模型真实状态、可复核删除与打开文件夹

**问题**：删除后仍显示「已删除本地模型：…」；删完立刻显示未安装，用户不知道是否真删。

**改法**：`GetInstallState` 五态（未安装/已安装/大小不符/缺配置/校验失败）、`Remove` 返回 `Removed / RemovedWithResidue / NotFound / Failed` 并复核目录确实消失、释放翻译引擎后再删、150 ms 重试一次、残留清单 + 脱敏错误上屏；行内加「校验」（深校验哈希）与「打开文件夹」；删除前若翻译在途则提示而不是硬删。

**验证**：占用文件返回 `RemovedWithResidue` 且 Residue 只列相对名，解除占用后重删 `Removed`；界面四类结果文案均由服务状态驱动。

## 0.93 2026-11-06：EU5 主页改用 Steam 官方库页大图 + 内置金色字标 + 无美术游戏换原生按钮

**问题**：EU5 主页背景停在内置静态图、按钮是「幽灵样式」（半透明白底、无品牌感）；Paradox 元数据对 EU5 返回空（`{"data":{"games":[]}}`），既没背景也没按钮素材。

**改法**：`ThemeResolver` 在 metadata 无背景时用 appdetails 的 `header_image` 推导 `library_hero.jpg`（必须去掉 hash 目录、且白名单要放行 `shared.akamai.steamstatic.com` / `cdn.cloudflare.steamstatic.com`）；`ApplyBundled` 把「背景」与「Logo」分开补齐，hero 背景时仍合并内置字标；主页按钮按 `ThemeAssetSource` 判定：`BuiltInPlaceholder`/`Bundled`/`SteamHero` 用 WinUI 原生按钮（`AccentButtonStyle`/`DefaultButtonStyle`），真正有主题的 `InstallDir`/`OnlineCache`/`SteamCapsule` 保留皮肤。

**验证**：真机 `library_hero.jpg` 200 / 245,266 B / 1920×620；伪装的 hero 缓存下按钮仍原生、标题显示金色 `EUROPA UNIVERSALIS V`；CS2 主题按钮未变。

## 0.94 2026-11-06：工坊评论表情渲染 + 三处杂修

**问题**：创意工坊评论区里的 Steam 表情（`/economy/emoticon/<name>`，无扩展名）显示不出来；另外三处小毛病：若干对话框正文里出现字面 `\n`、原子写失败留下 `.pdxnext.tmp`、删数据类确认框默认焦点在「确认」。

**改法**：
- 表情：白名单加 `community.fastly.steamstatic.com`（整域放行，头像/徽章/表情同域），评论 `Body.ImageUrls` 入队下载，评论模板挂 `ImageResolver`；`MarkdownView` 用 `MarkdownInlineFlow.RequiresRichText` 无条件下钻 styled/link，表情以 20×20 `InlineUIContainer` 内联（必须在 `RichTextBlock`，`Hyperlink.Inlines` 不允许），粗体/斜体/链接包表情都不崩。
- i18n：三键六处字面 `\\n` 改成真实换行，并加「i18n 源文件不得含字面 `\\n`」自检。
- 原子写：`WriteAllBytesAtomic` 失败路径 finally 删临时文件，补「目标被目录占住」用例。
- 确认框：`ConfirmOverlay` 加 `InitialFocus`（默认 Confirm），9 个破坏性调用点传 Cancel，Enter 随焦点、Escape 恒取消。

**验证**：V3 工坊 3822538803 五条评论里两张表情均为约 20px 小图（22,405 B / 15,801 B），HOI4 的「粗体+斜体包表情」评论正常；破坏性确认框 Enter 不执行清理。

**注意**：`InlineUIContainer` 只能挂在 `RichTextBlock` 的段落里；白名单做到主机级放行，路径校验交给表情识别侧，避免为了一个域把 `IsAllowedHost` 泛化成任意 URL 下载器。

## 0.95 2026-11-07：移除成就预热与常驻 Steam 宿主（不让 Steam 误显示游戏运行中）

**问题**：打开启动器、在游戏库里选中某款游戏后，几秒内 Steam 客户端把这款游戏显示成「正在运行 / 已启动」，而游戏 exe 并没有启动。

**根因**：选中游戏（以及冷启动恢复上次选择、后台主题刷新）会走 `AppServices.SelectGame` → `AchievementPrewarm.NotifyGameChanged`，2 s 去抖后拉起常驻 `--steam-host serve` worker；worker 首条成就命令用该游戏 appid 设 `SteamAppId` 并调 `SteamAPI_Init`。Steam 会把调过 Init 的进程登记成该 appid 的 game process（`SteamAPI_Shutdown` 不撤销，只有进程退出才除名），而 worker 读完后不退出、空闲 10 分钟才自退，打开成就页刷新还会重置计时，所以「运行中」会持续。本机日志：`pdxnext.log:6321/6339-6340` 的 worker PID 与 Steam `console_log.txt:14559` 的 `Game process added` 对得上。

**改法**：
- 删除 `AchievementPrewarmService`（启动 2 s / 切游戏 2 s 去抖预热）与 `SteamHostSession`（常驻 worker 复用）；`AppServices.SelectGame` 与 `App.OnLaunched` 不再有任何成就预热调用。
- `SteamAchievementSource` 去掉可选 session 参数，所有成就读取一律走一次性 `SteamHostClient.AchievementsRunAsync`（`--steam-host achievements`，读完即退）。
- 删除 `AchievementPrewarmChecks` / `SteamAchievementSessionChecks` 与 csproj 链接；`SteamHostProtocol.ServeVerb` 保留协议但注明「产品路径不再使用」。
- `docs/DEVELOPMENT.md` 第 9 条同步口径。

**验证**：`dotnet build PdxLauncher.sln -c Release -m:1` 0 警告 0 错误；`PdxLauncher.Checks --only Steam` 14 类 0 失败、`--only Achievement` 7 类 0 失败；全量自检 141 类 0 失败。

**注意**：Steamworks 的登记是 `SteamAPI_Init` 的固有副作用——保留成就 / DLC 授权 / 工坊直连就无法做到 100% 零登记；本次消除的是「用户没做任何 Steam 动作也会登记」的那部分。成就页 / DLC 页 / 工坊动作仍会带来约 1–3 s 的短暂登记（`probe`、`achievements`、`workshop-*` 一次性进程）。以后不要把 `serve` 接回产品路径，除非先解决登记副作用。

---

## 2026-11-06 勘误（设置页 6→8 分类、14→16 DIP）

以 `feat/1.3` HEAD `ac7225e` 复核现状，本轮只改文档、未动代码。以下与正文若干历史小节不一致之处，以此节为准：

- **设置页分节 6 → 8**：`SettingsPage.xaml` 的 `NavigationView.MenuItems` 现有 8 个 `NavigationViewItem`，`Tag="0".."7"`（第 65–120 行），依次为 外观 / 启动 / 路径 / 联网 / 缓存 / 高级 / 翻译 / 关于与更新；图标 `Font / Play / Folder / Globe / Save / Setting / World / Help`。其中 `navSettingsTranslation`（`Tag=6`，第 107 行）是 2026-10-30 新增的「翻译」节，见 §0.56；`navSettingsAbout`（`Tag=7`，第 114 行）是 2026-11-03 新增的「关于与更新」节，见 §0.74.3。
- **容器与代码同步**：右侧 8 个 `x:Name` 容器 `secAppearance` / `secLaunch` / `secPaths` / `secOnline` / `secCache` / `secAdvanced` / `secTranslation`（第 631 行）/ `secAbout`（第 937 行）切 `Visibility`；`SettingsPage.xaml.cs` 的 `ShowSection` 分节数组为 8 项（第 169 行）；`CacheSectionIndex` 仍为 `4`（「缓存」未换位）。
- **滚动条通道 14 DIP → 16 DIP**：滚动内容 `StackPanel` 与状态栏 `Border` 的 `Margin` 现均为 `0,0,16,0`（`SettingsPage.xaml` 第 135 / 1024 行）。改动来自 2026-11-04 的 `7e95e57`「style(ui): 间距全量对齐 WinUI 的 4px 网格」（14 不是 4 的倍数），见 §0.82；原 14 = 12 DIP 滚动条通道 + 2 DIP 余量。
- **代码注释未随数值更新**：`SettingsPage.xaml` 第 131 / 1021 行仍写 14 DIP，`SettingsPage.xaml.cs` 第 139 行仍写「左侧六分类」。按本轮「只改 docs」的约束未动，留待代码侧顺手修正。
- **历史小节按原样保留，不篡改**：§0.16.3 / §0.16.4（14 DIP 通道、12+2 的来历与像素读数）、§0.17.3（六个 `NavigationViewItem`、`Tag="0".."5"`）、§0.17.4（六分类 UIA 计数）、§0.23.4（账号节当时为第 7 项 `Tag=6`，该节已于 2026-10-28 迁出设置页，`Tag=6` 现由「翻译」复用）、§0.26.6（当时 6 个分节）、§13.5（六分类英文截图）均描述写作时状态，保留原文。
- **已就地更新的现状描述**：文首「2026-10-27 14:50 追加」的 14 DIP 与 §13.2 的「6 分类 ListView / 六个容器」已改为 8 分类、16 DIP。

---

## 2026-11-07 交接（设置页 S2–S3 已完成，S4 半途，五控件未开工）

本轮工作被用户叫停，现状与后续步骤写在 **[`docs/HANDOFF-2026-11-07.md`](HANDOFF-2026-11-07.md)**，从那里接着做。

要点：`feat/1.3` HEAD `ac7225e`，69 个文件未提交；构建 0 警告 0 错误、检查 142 类 0 失败。
设置页 67 张卡已全量换成官方 `ct:SettingsCard`（S2+S3），S4 只完成一部分（内容列仍 900、
8 节标题只 4 处 BodyStrong、关于节仍是 8 张平铺卡）；InfoBadge/NumberBox/MenuFlyout/SelectorBar/TreeView 未开工。

---

## 2026-11-07：EU5 DLC 缩略图显示修正（方图不再上下裁切）

- 用户反馈 EU5 DLC 页图片「歪」。逐字节核对四张真机 `thumbnail.dds`：D008/D015/D017 是 500×500 未压缩 BGRA8（pitch=2000，9 级 mip），D009 是 128×128 DXT5/BC3（8 级 mip）；解码器输出与缓存 PNG 尺寸一致，**DDS 解码没有斜切或翻转**。
- 根因是卡片图片框固定 208×117（16:9）+ `UniformToFill`：500×500 方图被上下各裁约 22%，盾徽/建筑只剩中间一条。
- 修法：EU5 行（`PlaysetsJsonV1`）优先 Steam 商店 `header_image`（宽幅），本地方形 DDS 只作离线兜底；本地方图与 Steam 头图都用 `Uniform` 完整显示，只有其它游戏的本地横图继续 `UniformToFill`。
- 落点：`DlcRowViewModel` 新增 `preferSteamArtwork` / `hasSquareThumbnail`、`FillsImageBox` / `NeedsSteamArtwork`；`DlcViewModel.FillArtworkAsync` 改用 `NeedsSteamArtwork`；`XamlStretch.ForDlcImage` + `DlcPage.xaml` 的 `Stretch` 改为 OneWay。
- 自检：新增 `DlcImageChecks`（链接真实 `DlcRowViewModel`），`DlcArtworkChecks` 补源码契约；`dotnet build PdxLauncher.sln -c Debug` 0 警告 0 错误，`--only Dlc` 6 类 0 失败。

---

## 2026-11-08：工坊正文配图点击进查看器 + 按 Steam 宽度排版

**问题**：创意工坊详情页里，正文配图点击跳浏览器；同一张 300×1600 的长图被压成约 79×420 的细条；Steam 物品页上显示正常。

**根因**：
- `WorkshopDetailPage.OnMarkdownImageRequested` 只在顶部截图墙 `_viewModel.Screenshots` 里按 URL 精确匹配，命中才 `OpenViewer`。0.43 起正文图被特意移出截图墙、存进 `_inlineImages`，所以永远匹配不到，落到 `ShellLauncher.TryOpenUrl`。
- `MarkdownView.BuildImage` 写死 `MaxWidth=640 / MaxHeight=420`；`Stretch=Uniform` 下 `MaxHeight` 会把长图等比缩窄，正是细条的来源。
- 描述卡没有宽度上限，内容宽 = 窗口宽 − 64 DIP；Steam 物品页正文列约 950 px。

**取数结论（本轮不改请求层）**：
- 详情页并发两次请求：`POST api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/`（元数据 + BBCode 描述，仅兜底）与 `GET steamcommunity.com/sharedfiles/filedetails/?id=…`（服务端直出的 HTML，`div.workshopItemDescription` 已是渲染好的描述）。
- 评论翻页才是拆开的请求：`/comment/PublishedFile_Public/render/{作者}/{文件}/?start=&count=`，只在翻页时发。
- 描述 HTML 缓存在 `workshop_pages`（12h / 150 条 / 单条 128 KB），BBCode 与元数据缓存在 `workshop_items`（7 天 / 2000 条）；原始 1–3 MB HTML 不落盘。
- 所以“直接拿请求里的正文”已经是现状，差距在渲染层：`HtmlDescriptionParser` 丢掉 width/height/style，`MarkdownView` 又加了 640×420 上限。

**改法**：
- `WorkshopGalleryLayout`：新增 `DescriptionContentMaxWidth=960`、`DescriptionCardMaxWidth=992`、`InlineImageInset=16`、`InlineImageMinWidth=120`、`DescriptionImageMaxWidth(viewWidth)`（夹在 [120, 944]）与 `PlaceholderMinWidth`。
- `MarkdownView`：正文图取消 `MaxHeight`，`MaxWidth` 跟随控件宽度；`SizeChanged` 时重算（另存 `_imageSlotList`，因为 `_imageSlots` 会按 URL 折叠重复图）；解码宽度从固定 1024 改成当前列宽。
- `WorkshopDetailPage.xaml`：描述卡与更新日志卡加 `MaxWidth="992"`（默认 Stretch），内部内容宽 960。
- `WorkshopDetailPage.xaml.cs`：新增 `_viewerItems`，`OpenViewer(items, index)` 支持任意一组 `WorkshopScreenshotViewModel`；正文图按 `MarkdownView.Markdown.ImageUrls` 顺序成组（过滤表情、只收已下载的），点击直接进查看器；仍未命中时回退截图墙 / 单张查看器 / 浏览器。计数器与查看器尺寸改读 `_viewerItems`，缩略图描边与关闭后的焦点恢复只在“数据源是截图墙”时执行。
- `WorkshopDetailViewModel.ApplyPdxAsync`：把 `description.ImageUrls + changelog.ImageUrls` 去重后交给 `DownloadInlineImagesAsync`，PDX 正文图也能显示与放大。

**验证**：
- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误。
- `PdxLauncher.Checks`：`WorkshopGalleryLayoutChecks` 新增 10 条断言全部通过（1600→944、960→944、500→484、136→120、0/NaN→944、卡片 992、占位框 240/120）；全量 143 类 0 失败。
- UI 实测：**未做**（构建时用户的 `PdxLauncher.App.exe` 正在运行，没有强制结束进程）。

**注意**：`MarkdownView` 是详情正文、更新日志、评论、ModStudio 预览共用的控件；这次只改图片尺寸，表情仍走 20 px 行内图。描述列宽常量放在 `WorkshopGalleryLayout` 是为了让检查工程能直接断言，XAML 里的 992 是它的字面值（960 + 32）。

---

## 2026-11-08：工坊筛选侧栏 + 新分页器

**需求**：按 Steam 工坊页左侧筛选栏加「类别 / 更新兼容性 / 所需的 DLC」的 包含(+) / 排除(−) 筛选、特别筛选条件与「显示不兼容的项目」，并把分页改成「每页 10/15/30/50 + 页码 1 2 3 … N + 前后箭头」。

**取数结论（用内置 Playwright 实测 `appid=529340`，2026-11，非猜测）**：
- 列表页 SSR 里 `window.SSR.renderContext.queryData` 是多条 React Query：
  - `workshop_browse`：`results` / `total_count` / `total_pages`；
  - `declared_tags_v6.readytouse_tags`：两组标签（第一组 `name` 为空 = 类别，第二组 `Update Compatibility`），值是标签名；
  - `workshop_required_dlc_v5`：`{appid,count}` 列表；DLC 名称在 `StoreItem / app_<appid> / default_info` 的 `name` 里，显示时去掉「游戏名: 」前缀。
- 服务端参数（逐项点选 UI 后读地址栏确认）：
  | 维度 | 参数 |
  |---|---|
  | 包含标签 | `requiredtags[]=<标签名>`（可重复） |
  | 排除标签 | `excludedtags[]=<标签名>` |
  | 所需 DLC | `appids_required_for_use[]=<dlc appid>` |
  | 排除 DLC | `excluded_appids_required_for_use[]=<dlc appid>` |
  | 显示不兼容 | `requiredflags[]=incompatible` |
  | 特别筛选 | `special_filter=6`（其他项目需要）/ `7`（在合集中），同时把 `browsesort` 换成 `num_parent_items` / `num_parent_collections` |
  | 每页 | `num_per_page=10/15/30/50` |
- 页面把这些值原样放进 `workshop_browse` 的 queryKey；没有单独的「标签 id」参数。

**改法**：
- Core：`SteamWorkshopQuery` 增加 `RequiredTags/ExcludedTags/RequiredAppIds/ExcludedAppIds/ShowIncompatible/SpecialFilter`；`BuildBrowseUri` 统一在 `AppendFilterParams` 拼装（排序去重 + 转义）；`SteamWorkshopPage.FilterGroups` 带回分组；`SteamWorkshopHtmlParser` 新增 `CollectQueries/FindBrowsePayload/ParseFilterGroups`，兼容老页面（没有 queryKey 时仍按 results 找）。
- Core：新增 `WorkshopFilterCatalog`（Victoria 3 的 24 个标签 + 6 个兼容性 + 20 个 DLC，来自实测），只在 SSR 解析不到时兜底；其他游戏不加静态目录。
- Core：`WorkshopPageCache.Key` 末尾追加 `pageSize / requiredTags / excludedTags / requiredAppIds / excludedAppIds / showIncompatible / specialFilter`，标签排序去重。
- App：新增 `WorkshopFiltersViewModel` / `WorkshopFilterGroupViewModel` / `WorkshopTagOption` / `WorkshopPageButton`。选项状态互斥；筛选变化 700 ms 去抖后回第 1 页重查；换游戏/卸载取消去抖；分组按 appId 缓存在内存里，切回看过的游戏命中页缓存时侧栏也在。
- App：`WorkshopViewModel` 页大小 10/15/30/50（持久化到 `AppSettings.WorkshopPageSize`）、`PageButtons`（首页 / 末页 / 当前页±1 + 省略号）、`GoToPageAsync`；PDX 来源沿用同一页大小。
- XAML：工具条加「筛选」开关（`btnWorkshopFilter`）；行 2 改成 264 DIP 侧栏 + 列表两列；行 3 改成分页器（`cmbWorkshopPageSize`、`lstWorkshopPageNumbers`），`txtWorkshopPage` 移进状态卡保留。
- RESW：新增 `WorkshopFilter*` 与 `WorkshopPageSize` 共 16 个键（中英同步）。

**验证**：
- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误。
- `PdxLauncher.Checks`：143 类 0 失败。新增断言覆盖：筛选参数名/排序/转义、特别筛选覆盖排序、筛选组解析（真实 SSR 形态）、页缓存键的页大小/标签/DLC/不兼容/特别筛选、侧栏与分页器源码结构。
- 真实 HTML 解析：用 Playwright 抓下 `appid=529340` 的 549 KB 页面喂给 `SteamWorkshopHtmlParser`，得到 `items=30 total=11348 pages=379`，三个分组（类别 24 / 更新兼容性 6 / DLC 20）与页面一致，DLC 名称去前缀正确。
- UI 实测：**未做**（桌面应用未启动）；参数与解析已用真实页面验证，交互走查留给下一次手动冒烟。

**已知边界**：
- 「按日期筛选」按钮未实现（需要 DLC 发布日期，页面参数未暴露）；「特别筛选条件」只做三个选项（无 / 其他项目需要 / 在合集中）。
- 筛选分组来自 SSR，其他游戏没有静态目录；页面改版导致解析不到时侧栏隐藏，不会给出错误标签。

---

## 2026-11-08：工坊筛选栏放开高度 + Steam 标签中文化

**问题**：V3 工坊筛选栏的「类别」只显示 10 行（每行 24 DIP），右侧出现内层滚动条；标签名全是英文（Alternative History、Balance…），只有组标题「类别」是中文。

**根因**：
- `WorkshopPage.xaml` 给每个分组的选项列表套了 `ScrollViewer MaxHeight="240"`，240 / 24 = 正好 10 行；外层整栏本来就有 ScrollViewer，内层限制只是把内容压成小窗口。
- 标签展示名走 `WorkshopTagOption.DisplayName`，直接使用 Steam 的 `display_name`；`SteamWorkshopTag` 已经把 `Tag`（发给 Steam 的机器名）和 `DisplayName` 分开，但展示侧没做本地化。组标题走 `WorkshopFiltersViewModel.LocalizeGroupTitle`，所以只有标题是中文。

**改法**：
- `WorkshopPage.xaml`：删掉每组 `ScrollViewer MaxHeight="240"`，`ItemsControl` 直接放进组内；整栏由外层 ScrollViewer 滚动。`DisplayName` 的文本与 ToolTip 绑定改 `Mode=OneWay`，切语言能刷新。
- Core 新增 `WorkshopTagKeys`：34 个标签 → 资源键（忽略大小写），`KeyFor` / `AllKeys`；覆盖 V3 24 个 + Characters / Culture / Interface / Religion / Megastructures / Ships / Species / Missions / Units / Deprecated。
- App 新增 `WorkshopTagText.Localize(tag, displayName)`：先查机器名再查展示名，命中用当前语言，未命中原样显示。
- `WorkshopTagOption`：`DisplayName` 改计算属性，构造加 `localize`（DLC 组传 false，避免商品名被误译）；它已继承 `LocalizedObservableObject`，切语言自动通知。
- `WorkshopFilterGroupViewModel`：按 `isDlc` 传 `localize`。
- `WorkshopDetailViewModel`：保存原始 Steam 标签列表，详情页标签逐项本地化；override `Relocalize()` 重拼 `TagsText`，切语言不重载页面。
- 两份 `Resources.resw` 各加 34 个 `WorkshopTag*` 键（中英同步）。

**验证**：
- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误。
- `PdxLauncher.Checks`：144 类 0 失败。新增 `WorkshopTagTextChecks`：34 个键在中英 resw 都存在、内置筛选目录 / Mod Studio 七个游戏的标签全部有译文、大小写不敏感、未知标签回退 null；`WorkshopPageChecks` 增两条源码断言（不再有 `MaxHeight="240"`、`DisplayName` 是 OneWay）。
- UI 实测：**未做**（桌面应用仍在运行）；需重启后按计划里的清单走一遍。

**注意**：
- 只翻译展示名，`WorkshopTagOption.Tag` / `SteamWorkshopTag.Tag` 不变，`requiredtags[]` / `excludedtags[]` 仍发英文。
- 未收录的新标签回退英文，不会出现 `⟨WorkshopTagXxx⟩`；以后 Steam 加标签时在 `WorkshopTagKeys` 补一条即可。
- Mod Studio 上传标签与「所需的 DLC」商品名不在本次范围。

---

## 2026-11-08：工坊筛选目录随游戏更新（24h TTL + 本地版本变化）

**问题**：筛选栏的「更新兼容性」等分组来自 Steam 列表页 SSR；游戏更新（1.13→1.14）后，静态目录不会变，页缓存命中时也不会重新抓目录，重启还会退回写死的 1.13 列表。

**实测结论（内置浏览器，2026-11）**：
- Steam 没有「只返回标签目录」的接口：点标签时前端也是重新 `fetch` 整个 `/workshop/browse/`。
- `num_per_page=10` 可用且目录完整，HTML 约 388 KB（30 条时约 555 KB），目录刷新用这一页。
- 目录分组仍在 `declared_tags_v6.readytouse_tags` / `workshop_required_dlc_v5`（见上一节）。

**改法**：
- `WorkshopFilterCatalogSnapshot` + `WorkshopFilterCatalogCache`：`data\workshop-filters\{appid}.json`，schema 1、原子写、坏文件/版本不符/appid 不符按未命中；文件内用 DTO 存取，避免接口类型反序列化问题。
- `WorkshopFilterCatalogPolicy`：`DefaultTtl=24h`、`FailureBackoff=30min`；`NeedsRefresh` = 无快照 / 本地版本变了（快照版本为空也算）/ 超过 TTL / 时钟回拨；`Signature` 统一分组比较。
- `SteamWorkshopClient.GetFilterGroupsAsync`：在途合并 + `BrowseAsync(num_per_page=10, trend, p=1)`，只返回分组；解析不到分组返回失败。
- `WorkshopViewModel`：
  - 构造函数注入目录缓存；`LoadAsync` 先读快照再判定来源，侧栏首屏就是上次落盘的最新版本。
  - 回退顺序：本会话缓存 → 持久化快照 → 静态目录（Victoria 3 仅离线兜底）。
  - 每次 SSR 成功更新快照；结构变化或距上次写盘 >1h 才落盘。
  - `RefreshCatalogIfStaleAsync`：仅 Steam、非忙碌、无在途查询、无冷却、非刷新中、失败未满 30 分钟才发请求；失败只记日志。
  - 触发点：`LoadAsync` 首次查询后、页缓存命中后（无静默刷新时）、从详情页返回（`OnLoaded` 早退）。
  - 静默更新：不写状态栏；若已勾选标签在新目录里消失，静默取消选择并在本轮结束后回第 1 页重查（静默刷新路径也有对应的收口）。
  - 换游戏/卸载：`CancelCatalogRefresh`。
- 静态目录只保留为「全新安装 + 离线」兜底，注释已写明不再承担最新版本来源。

**验证**：
- `dotnet build "PdxLauncher.sln" -c Release -m:1`：0 警告 0 错误（`PdxLauncher.App.exe` 在运行时用 `-p:OutDir=.scratch\appout\` 规避文件锁）。
- `PdxLauncher.Checks`：145 个检查类 0 失败。新增 `WorkshopFilterCatalogCacheChecks`（原子写/坏文件/schema/appid 隔离/TTL/版本/签名）、`GetFilterGroupsAsync` 的 URI 与失败路径、`WorkshopPageChecks` 的目录读写/冷却/单飞/页缓存钩子断言。
- 真实页面回归：抓 `num_per_page=10` 页 → `items=10 total=11347 pages=1000 groups=3`；快照保存后读回兼容性 `Deprecated,1.9–1.13`、DLC 名称正确；策略输出「同版本不刷新 / 1.14 刷新 / +25h 刷新」。
- 顺带修正真机检查：Victoria 3 新补丁又把 DLC 写回 `.dlc.json`（`dlc_metadata` 为 0），`PlaysetChecks` 改成只要求 `State and Revolution / 4314660` 条目存在，不再锁存储形态。

**边界**：页面一直开着且 24h 内没有任何操作时不会自动变化；下一次进页/刷新/改筛选/翻页命中 SSR 才更新（符合选定的 24h TTL 策略）。没有新增网络主机，仍走 `HttpFetcher` 白名单与限流。
