# 版本号与发版规范

PdxNext 采用 [Semantic Versioning 2.0](https://semver.org/lang/zh-CN/)：`MAJOR.MINOR.PATCH`。

## 1. 唯一版本源

只改 `version.props`：

```xml
<Version>1.0.0</Version>
<FileVersion>1.0.0.0</FileVersion>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
```

`Directory.Build.props` 会把它导入所有项目。以下位置都从它派生，禁止手改：

- 程序集版本 / 文件版本；
- `PdxNext-Setup-<version>.exe` 文件名；
- `update.json` 的 `version`、`setupUrl`、`releaseNotesUrl`；
- GitHub Release 的 tag `v<version>`。

`FileVersion` 与 `AssemblyVersion` 始终是四段；`Version` 是三段。发版时两者一起改。

## 2. 什么时候加哪一位

| 变更 | 版本位 | 例子 |
| --- | --- | --- |
| 新页面、新功能、新安装模式、新游戏支持 | MINOR | `1.0.0 → 1.1.0` |
| 修 bug、性能优化、文案/样式调整、依赖小升级 | PATCH | `1.1.0 → 1.1.1` |
| 数据格式不兼容、设置项语义变化需要手动迁移、最低 Windows/.NET 提高、安装位置/更新协议不兼容 | MAJOR | `1.4.2 → 2.0.0` |
| 只改文档、注释、CI | 不改版本 | 跟随下一次功能/修复提交 |

判断口径：

- 用户不需要做任何额外操作就能升级 → 不是 MAJOR；
- 旧版本的 `data` 能被新版本读取，且新版本写出的数据旧版本不一定能读 → 通常仍是 MINOR/PATCH，但要写迁移说明；
- 需要用户手动改 `data-root.txt`、移动数据目录、重装才能继续 → MAJOR。

## 3. 预发布与测试版

- 稳定版：`1.2.3`，进 `update.json`，自动更新可见。
- 测试版：`1.3.0-beta.1` 只用于本地/手动测试，**不写入 `latest` 清单**，也不作为 GitHub Release 的 latest。
- 当前 `UpdateVersion` 只比较三段数字，会忽略 `-beta.1` 后缀；因此自动更新通道只允许稳定版。
- 如果以后要让 beta 也走自动更新，必须先实现真正的预发布排序（`1.3.0-beta.1 < 1.3.0`），再改清单协议。

## 4. 一次发版的完整顺序

1. 改 `version.props`（例如 `1.0.0 → 1.1.0`，同时 `1.1.0.0`）。
2. 更新 `CHANGELOG.md`，把 `[Unreleased]` 改成 `[1.1.0] - YYYY-MM-DD`。
3. 本地验证：

   ```bat
   publish.cmd
   python tools\build-installer.py
   ```

   然后安装刚生成的 Setup，并校验本机安装版本：

   ```powershell
   Start-Process "dist\installer\PdxNext-Setup-<version>.exe" `
     -ArgumentList "/VERYSILENT", "/SUPPRESSMSGBOXES", "/CURRENTUSER", "/NORESTART"
   powershell -ExecutionPolicy Bypass -File tools\verify-installed-version.ps1
   ```

   安装后如果启动器自动拉起，先退出再跑校验。**校验不通过不能提交发版。**

4. 提交并推送 `main`。
5. 打 tag 并推送：

   ```bat
   git tag v1.1.0
   git push origin v1.1.0
   ```

6. GitHub Actions `.github/workflows/release.yml` 自动：
   - 校验 tag 与 `version.props` 一致；
   - 构建、跑自检；
   - 生成 Setup + `update.json`；
   - 创建 Release 并上传两个资产。
7. 下载一次 `releases/latest/download/update.json`，确认可访问、`version` 正确。
8. 用旧版本安装一次，验证自动更新提示、下载、校验、安装、重启。

## 5. 分支与热修

- `main` 始终保持可发布；功能在 `feat/*`，修复在 `fix/*`。
- `sess/YYYYMMDD-*` 是**对话级临时分支**（`scripts/session.ps1 open/close` 管理）：从当前分支拉出、
  对话结束 `--no-ff` 合回后删除，**永不作为发布来源**，也不打 tag。
- `main` 只接受 `feat/1.3`（发版）或 `hotfix/*`（线上故障）合入。
- 线上故障：从对应 tag 拉 `hotfix/x.y.z`，只改最小范围，PATCH +1，走同一套 tag 流程。
- 不做“静默改版本号”的 force push；`version.props` 一旦进入 Release 就不再改，重新发版必须递增。

## 6. 与安装/更新的兼容约定

- 安装器 `AppId` 永远不变：`A2E3D5E1-5133-46BA-A846-1388A878E1A7`。变了会被当成另一个产品。
- 升级只替换程序文件，`data\`、`data-root.txt`、`install-mode.txt` 由安装器保留/重写。
- `schemaVersion=1` 的更新清单保持字段向后兼容；新增字段必须可选，旧客户端应能忽略。
- `OnlineHosts` 只放精确主机名，不加通配子域；更换发布域名要同步改清单地址与白名单。

## 7. 本地安装版本同步（硬规则）

每次发版必须同时满足下面三条，缺一条都视为发版未完成：

1. `version.props`、`CHANGELOG.md` 与实际代码一致；
2. 用新生成的 Setup 在本机完成一次真实安装/覆盖安装；
3. `tools/verify-installed-version.ps1` 退出码为 0，且安装目录里的
   `PdxLauncher.App.exe` 的 `FileVersion` 等于 `version.props` 的
   `FileVersion`。

原因：安装包和更新清单都由 `version.props` 派生，但**本机已安装副本不会自动更新**。
如果只改源码/打 tag、不安装新包，About 页和卸载项会继续显示旧版本，更新链路也会
拿旧程序去比较版本，表现为「发了版但客户端还是旧号」。

CI 的 `ReleaseVersionChecks` 只能校验版本文件与 CHANGELOG；本地安装副本必须在
发布机器上单独校验：

```powershell
powershell -ExecutionPolicy Bypass -File tools\verify-installed-version.ps1
```
