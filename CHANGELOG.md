# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；版本号遵循
[docs/VERSIONING.md](docs/VERSIONING.md)。

## [Unreleased]

### Fixed

- 已经在创意工坊发布（或上传后被设成隐藏 / 私密）的 Mod，在「Mod 创建」页一直显示「未发布」、工坊页也打不开：现在可以「绑定已有条目…」手填工坊 id 或从自己的 Steam 条目里选；发布状态改为四态（未发布 / 发布未完成 / 已发布（未公开）/ 已发布）并显示 Steam 真实可见性。根因是对隐藏条目匿名 Web API 只回 `EResult 9`（FileNotFound），它不等于条目不存在。
- 创意工坊页连不上 Steam 时永远停在「正在加载第 1 页…」且列表空白（搜索、排序、时间范围全都「搜不出来」）：.NET 连不上时抛的是 `OperationCanceledException`（内含 `TimeoutException`），以前被当成「用户取消了操作」静默吞掉。现在连接超时会翻译成明确的「网络故障」，状态栏直接写「无法连接 Steam 创意工坊：steamcommunity.com」，并提示「检测到系统代理 X，可在设置里切过去再试」。真机实测本机 DNS 被污染（`steamcommunity.com` → 104.244.46.21），直连必超时，走代理则正常返回 11252 条。
- 「检查更新」在连不上时不再冒出未观察的 Task 异常。
- 发布被环境问题拦下（缺 steam_api64.dll / 缺宿主 / 无 appid / 预览图不合规等）时只在状态栏闪一句、日志里毫无痕迹：这些分支现在都写一条 Warn，便于事后定位。

### Added

- 设置页「代理」分区新增「检测到的代理」：自动读出系统 / 环境变量里可用的代理地址，一键「使用」切过去（直连遇到 DNS 被污染时不用再手填地址）。

- 宿主新增 `workshop-mine` / `workshop-mine-detail` 两个动词（回调 1312 / 1318），走本机 Steam 登录态读「我自己发布的条目」，包括隐藏条目的标题与真实可见性。

## [1.1.1] - 2026-11-03

### Fixed

- 设置页「检查更新」成功后「上次检查」不刷新、自动检查开关不同步：`UpdateService` 与 UI 现在共用同一个 `AppSettings` 实例，并增加一致性检查。
- 设置页 `ToggleSwitch` 因默认 `MinWidth` 和「开/关」文案偏左，统一收紧后与右侧操作按钮对齐。

### Added

- 本地安装版本校验工具与发版规则：发布前必须安装并校验已安装程序的 `FileVersion` 与 `version.props` 一致；CI 自检同时校验版本文件与 CHANGELOG 条目。

## [1.1.0] - 2026-11-03

### Added

- GitHub Actions 发布流程：推 `v*` tag 自动构建、自检、生成安装器并上传 Release。
- 版本规划文档与发版清单。
- 页面状态跨重启保留：Mod 库的搜索/排序/筛选/滚动位置，Mod 创建页的面板、滚动位置与表单草稿。
- 设置页新增「本地翻译模型常驻」开关，默认常驻；关掉后空闲 5 分钟释放约 1.2 GB 权重。
- Debug 构建记录内存与缓存快照，见 `docs/MEMORY-BASELINE.md`。
- 工坊封面目录 100 MB 磁盘配额，按最旧优先清理；封面下载即生成 96/256 两档小图。

### Changed

- 列表与详情图片按显示尺寸解码，BitmapLoader 改 LRU；首屏只预热 12 张。
- 轻量页（设置/账号/DLC）离开即释放；Mod 库与创建页离开即释放，返回时恢复状态。
- 日志改缓冲写入并按 5 MB × 3 轮转。
- 工坊条目/分页缓存改 SQLite 真表，自有库升级到 v2：自动导入旧缓存、迁移前留 `.bak`。
- 缓存上限收紧：工坊条目 2000 条、工坊分页 150 条 × 128 KB、译文 2000 条、工坊列表页 40 页。
- 缓存落盘改 500 ms 防抖（含译文缓存），关窗统一 Flush。
- 本地翻译上下文 4096 → 2048，长文按句读分块；修正 GPU 层数与实际加载参数不一致。

## [1.0.0] - 2026-11-03

首个公开版本。

### Added

- Windows 安装包：Inno Setup 7.1.1，双模式（每用户无 UAC / 每机器 UAC）。
- 自动更新：GitHub Releases 静态 `update.json`、24 小时节流、下载校验 SHA-256、
  按安装模式静默安装或提权安装。
- 设置页「关于与更新」：版本、安装模式、上次检查、自动检查开关、手动检查。
- 每机器安装的数据目录回退：`%LOCALAPPDATA%\PdxNext-Data`，不写 Program Files。
- 单实例/安装互斥：`AppMutex`、`SetupMutex`，更新前主动释放。
- 机翻引擎、翻译缓存与设置页翻译分区。
- Mod 自动排序预览（差异、理由、冲突/警告）、Mod 创建页。
- 工坊详情页、Steam 直连订阅实验开关、缓存与图片解码优化。

### Changed

- 启动器日志缓冲写入并轮转；轻量页离开即释放；图片按显示尺寸解码。
- 仓库整理：MIT LICENSE、README、移除本机绝对路径。

### Known issues

- 未做 Authenticode 代码签名，SmartScreen 可能提示“未知发布者”。
- 每机器安装的更新必须经过 UAC，不能完全静默。
