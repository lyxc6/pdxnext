# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；版本号遵循
[docs/VERSIONING.md](docs/VERSIONING.md)。

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
