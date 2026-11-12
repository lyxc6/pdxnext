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

   如果仓库路径含非 ASCII 字符（例如 `G:\Work\paradox启动器`），Inno Setup 7 的 Setup
   可能在初始化阶段直接退出（exit code 1，且不写 `/LOG`）。先把 Setup 复制到纯 ASCII 目录
   （如 `%TEMP%\pdxnext-setup-test\`）再运行；安装目标目录不受影响。

   安装后如果启动器自动拉起，先退出再跑校验。**校验不通过不能提交发版。**

4. 把**正在发布的版本分支**合进 `main`（`--no-ff`，保留一个发布点）：

   ```bat
   git switch main
   git pull --ff-only
   git merge --no-ff ver/<version> -m "merge: 发布 <version>（ver/<version>）"
   ```

   版本分支就是 `integration-branch` 清单里、`version.props` 等于本次版本的那一行。
   发版前先确认没有未收尾的对话分支：`pwsh -File scripts/session.ps1 list` 应为空。

5. 在 `main` 上冻结刚发布的版本分支（发布后立即从活跃清单移除）：

   ```bat
   rem 编辑 integration-branch，删掉 ver/<version> 这一行
   git add integration-branch
   git commit -m "chore(branch): 冻结 ver/<version>（发布 <version>）"
   git push origin main
   ```

   冻结提交必须在打 tag 之前完成，tag 才会指向 `main` 的最新提交；被冻结的分支保留引用，
   之后不再接受任何提交。补丁版（1.3.1）与次版本（1.4.0）都走同一套顺序。

6. 打 tag 并推送：

   ```bat
   git tag v1.1.0
   git push origin v1.1.0
   ```

7. GitHub Actions `.github/workflows/release.yml` 自动：
   - 校验 tag 与 `version.props` 一致，并要求被 tag 的提交在 `main` 历史里；
   - 构建、跑自检；
   - 生成 Setup + `update.json`；
   - 创建 Release 并上传两个资产。
8. 下载一次 `releases/latest/download/update.json`，确认可访问、`version` 正确。
9. 用旧版本安装一次，验证自动更新提示、下载、校验、安装、重启。

## 5. 分支模型：发布即冻结

- `main` 永远可发布，只接受两类提交：
  1. 活跃 `ver` 分支的 `--no-ff` 发版合并；
  2. 协调者对仓库根 `integration-branch` 的登记/冻结提交（`chore(branch): ...`）。
- `ver/x.y.z` 是**长期版本分支**：一个分支只对应一个未发布版本，从 `main` 创建、登记后接收
  `sess/YYYYMMDD-*` 的合入；发布后立即冻结。
- `sess/*` 是**对话级临时分支**（`scripts/session.ps1 open/close` 管理）：从某个活跃 `ver`
  分支拉出、对话结束 `--no-ff` 合回后删除，**永不作为发布来源**，也不打 tag。
- `feat/<task>` 只用于 worktree 内并行写任务，基线是本次对话的 sess 分支；
  不再用 `feat/1.x` 这类名字充当长期版本分支。
- **冻结规则**：版本分支合进 `main` 并打 tag 后，从 `integration-branch` 移除；此后不接受任何
  提交（包括文档、`CHANGELOG.md`、`docs/HANDOFF.md`）。需要修正只能从 `main` 开新的更高补丁版
  分支（如 1.3.1）。冻结分支保留引用，不删除、不 force push。
- **创建规则**：
  - 补丁版：从 `main`（最新发布点）开 `ver/x.y.(z+1)`，只含修复；
  - 新版本：从 `main` 开 `ver/x.(y+1).0`，`CHANGELOG.md` 补 `## [Unreleased]`；
  - 新分支先在 `main` 登记（`chore(branch): 激活 ver/...`），再从 `main` 创建并推送；
    这样每个工作树都能在 `main` 的清单里看到它。
- **多分支并存**：`integration-branch` 可以同时列多个 `ver` 分支（例如 `ver/1.3.1` 与 `ver/1.4.0`）。
  补丁发布后要把 `main` 合进其它未发布的活跃分支，避免下一个版本漏修；冻结分支不参与合并。
- 同一个 bug 影响多个未发布版本时，先在最低版本分支修并发布，再把 `main` 合进更高分支。
- 合并 `main` 时的冲突口径：`version.props` 取 `main` 的已发布版本；`CHANGELOG.md` 同时保留
  `main` 的新版本小节与该分支的 `[Unreleased]` 条目。
- **版本号**：活跃分支的 `version.props` 停在上一个已发布版本，发版准备时才 bump；
  `version.props` 一旦进入 Release 就不再改，重新发版必须递增。
- 不做“静默改版本号”的 force push；不改写已推送历史。
- `main` 与 `ver` 分支的推送只由协调者在发版/登记流程里做；对话分支不 push。
- 不再使用 `hotfix/*`：线上故障就是开一个补丁版分支，走同一套流程。
- `integration-branch` 每行一个分支（空行与 `#` 注释忽略）；**权威副本在 `main`**，
  `scripts/session.ps1` 读的是 `main:integration-branch`，各分支上的副本只是继承快照，不参与校验。

## 5.1 发布之后：冻结、补丁、下一个版本

以 1.3.0 发布完为例：

1. 收尾所有对话分支：`pwsh -File scripts/session.ps1 list` 为空。
2. 确认 1.3.0 已按 §4 合进 `main`、冻结提交已完成、tag `v1.3.0` 已推送。
3. 发布用的版本分支（1.3 时代是 `feat/1.3`；今后发布都用 `ver/x.y.z`）**立即冻结**：
   不再提交，保留引用作为历史。
4. 需要修 1.3.x 时，从 `main` 新开补丁版分支：

   ```bat
   git switch main
   rem 先编辑 integration-branch，加一行 ver/1.3.1
   git add integration-branch
   git commit -m "chore(branch): 激活 ver/1.3.1"
   git push origin main
   git switch -c ver/1.3.1 main
   git push -u origin ver/1.3.1
   ```

   在 `ver/1.3.1` 上用 `sess/*` 修 bug；发版时把 `version.props` bump 到 1.3.1，按 §4 合
   `main`、冻结 `ver/1.3.1`、打 tag `v1.3.1`。

5. 需要下一个版本（如 1.4.0）时，同样从 `main` 新开：
   ```bat
   git switch main
   rem integration-branch 再加一行 ver/1.4.0（可以与 ver/1.3.1 并存）
   git add integration-branch
   git commit -m "chore(branch): 激活 ver/1.4.0"
   git push origin main
   git switch -c ver/1.4.0 main
   git push -u origin ver/1.4.0
   ```

   在该分支补 `CHANGELOG.md` 的 `## [Unreleased]`；`version.props` 仍停 1.3.0
   （或最近一次已发布版本），等 1.4.0 发版时才 bump。

6. 多个 `ver` 分支同时活跃时：
   - 每次发布后把 `main` 合进其它**未发布**的活跃分支（`git switch ver/1.4.0`
     → `git merge main`），解决 `version.props`/`CHANGELOG.md` 冲突时按 §5 的口径；
   - 冻结分支不参与任何合并，也不再作为会话基线。
7. 永远不要“在已发布分支上再补一刀”；需要修正就开新的补丁分支（1.3.1、1.3.2 …）。

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
