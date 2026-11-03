# Changelog

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)；版本号遵循
[docs/VERSIONING.md](docs/VERSIONING.md)。

## [Unreleased]

### Added

- GitHub Actions 发布流程：推 `v*` tag 自动构建、自检、生成安装器并上传 Release。
- 版本规划文档与发版清单。

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
