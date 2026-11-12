# Agent 入口（PdxNext）

本文件给下个对话 / 自动化 agent 用：先照这里开工，细节再读 `docs/AGENT-WORKFLOW.md`。
仓库根：`G:\Work\paradox启动器`；活跃版本分支记在仓库根 `integration-branch`（多行清单，每行一个
`ver/x.y.z`；已发布分支冻结并从清单移除）。
默认用中文汇报。

## 1. 对话生命周期：一个对话一个分支

### 1.1 开工

1. 读 `docs/HANDOFF.md` 顶部「一句话现状」与最近 3 个 `## 0.NN` 小节，再动代码。
2. `git status` 看清当前分支和未提交改动。
3. 开本对话的分支：

   ```bat
   pwsh -File scripts/session.ps1 open <slug>
   ```

   `slug` 用英文 kebab-case，例如 `fix-dlc-artwork`、`docs-session-rules`。
   没有 `pwsh` 时用 `powershell -File`，参数相同。

   `open` 会校验你**必须站在 `integration-branch` 清单里的某个活跃版本分支上**（通常形如
   `ver/x.y.z`）；不在就先 `git switch <活跃分支>`。清单为空、或从 `main` / 已冻结分支起时脚本
   会直接拒绝；`-Force` 只用于引导、发版或从 tag 起紧急修复，并在汇报里写明原因。

- 分支名 `sess/YYYYMMDD-<slug>`，基线是**对话开始时所在的当前活跃版本分支**（通常是 `ver/x.y.z`）。
- 开工时已有的未提交改动会跟着带到新分支，并由脚本提交成 `wip(session): ...` 基线提交；
  它只是检查点，不是交付内容。
- 本对话的所有提交都留在 sess 分支；**不往起始分支写任何东西**。
- 子 agent / subagent **不要自己建分支或切分支**。并行写任务用 worktree（`docs/AGENT-WORKFLOW.md` §4），
  但基线是本对话的 sess 分支，合回也是 sess 分支。
- 提交信息：`type(scope): 中文摘要`（`feat` / `fix` / `perf` / `test` / `docs` / `chore`），正文写根因与行为变化。

### 1.2 收尾

1. 验收：`cmd /c dev.cmd build`，判定 0 警告 0 错误且输出 `失败: 0`（见 §2）。纯 `*.md` 改动可跳过。
2. 合回并清理：

   ```bat
   pwsh -File scripts/session.ps1 close -Message "中文说明"
   ```

   脚本做四件事：校验基线仍是活跃版本分支 → 切回该分支 → `git merge --no-ff` → 删除 sess 分支
   并留在该分支。基线已冻结或不在 `integration-branch` 清单里时，`close` 会硬失败（不再只警告），
   提示用 `-Into <活跃分支>` 重定向，或从活跃分支重开对话。
   纯 `*.md` 改动自动跳过构建；`-SkipVerify` 可强制跳过。合并冲突时脚本会中止合并并停在
   sess 分支，先 `git merge <目标分支>` 解决冲突、提交，再重跑 `close`。
3. 汇报四行：**做了什么 / 证据（`dev.cmd build` 输出 + 实测）/ 没做什么及原因 / 下一步建议**，
   并写明本对话分支去向：`sess/… → 已合回 <分支>（commit）`。

**没做完就不要 `close`**：分支留着，在汇报里写清停在哪、下个对话从哪接。

### 1.3 发版后：冻结旧分支，再开新版本分支

- **发布即冻结**：版本分支合进 `main` 并打 tag 后，从 `integration-branch` 移除；之后不再接受任何
  提交。分支引用保留作为历史，不删除、不 force push、不改写已推送历史。
- **补丁版**（如 1.3.1）：从 `main`（最新发布点）开 `ver/1.3.1`，登记、推送，在该分支修 bug，
  再走同一套发版流程。
- **新版本**（如 1.4.0）：从 `main` 开 `ver/1.4.0`，`CHANGELOG.md` 补 `## [Unreleased]`；
  `version.props` 保持最近一次已发布版本，发版时才 bump。
- **多分支并存**：补丁分支和新版本分支可以同时活跃；`integration-branch` 每行一个 `ver/x.y.z`，
  会话只从清单里的分支开。冻结分支绝不再作为任何会话的基线。
- 发版顺序见 `docs/VERSIONING.md` §4；冻结、登记与向其它活跃分支传播见 §5/§5.1。
  不要把版本分支名写死在其它文档或脚本里。
- 发版前先 `pwsh -File scripts/session.ps1 list`，确认没有未收尾的 `sess/*` 分支。

## 2. 常用命令

在仓库根用 `cmd /c` 执行：

| 目的 | 命令 | 说明 |
| --- | --- | --- |
| 验收 | `dev.cmd build` | 结束旧实例 → Release 构建（`-m:1`）→ 自检，不启动 |
| 跑起来 | `dev.cmd` | 结束旧实例 → 构建 + 自检 → 启动，并确认进程存活 |
| 看实时日志 | `dev.cmd logs` | 同 `dev.cmd`，启动后在本终端跟随 `data\logs\pdxnext.log`；Ctrl+C 只停跟随，程序继续跑 |
| 免编译跑 | `dev.cmd nobuild` | 不构建，直接结束旧实例并启动已有产物；加 `logs` 也跟随日志 |
| 看状态 | `status.cmd nopause` | 分支、未提交改动、运行态、构建时间（只读） |

- `dev.cmd` 末尾固定输出 `DEV_RESULT=` / `DEV_EXE=` / `DEV_DATA=` / `DEV_LOG=` / `DEV_PID=` 等行，直接 grep 判断。
- 退出码：`0` 通过、`1` 构建失败、`2` 自检失败、`3` 没有可执行文件、`4` 启动后退出。
- `dev.cmd` 会**结束正在运行的 `PdxLauncher.App.exe`**（运行中的 exe 会锁 `PdxLauncher.Core.dll`）。
  同一工作树同一时刻只允许一个 `dev.cmd` / `dotnet build`。
- `dev.cmd logs` / `dev.cmd nobuild logs` 会一直阻塞到 Ctrl+C（或关掉终端）；自动化验收仍用
  `dev.cmd build`，不要用 `logs`。
- 如果 `dev.cmd build` 只挂在真机数据检查上（例如本机没装某个游戏 / 缺 `dlc_metadata`），
  可以先用 `dev.cmd nobuild` 或 `dev.cmd nobuild logs` 看界面；但这不算验收通过，汇报里要写明「自检未过」的原因。

## 3. 仓库事实

- WinUI 3（Windows App SDK）+ .NET 10，自包含 x64；**不是** WPF。
- Release 产物：`src\PdxLauncher.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\PdxLauncher.App.exe`
- 数据目录：`<exe 目录>\data`；日志：`<exe 目录>\data\logs\pdxnext.log`
- 构建必须 `-m:1`，否则 App 项目会 0 错 0 警告地静默失败。

## 4. 硬约束

1. 顶层 `.cmd` 纯 ASCII；中文只放 `scripts\messages.txt`（由 `scripts\msg.cmd` 打印）和
   `Strings\{zh-CN,en-US}\Resources.resw`。
2. `Padding` / `Margin` / `Spacing` / `CornerRadius` 用 4 的倍数，圆角只用 0/4/8。
3. 不写用户已安装目录：`D:\app\PdxNext`、`%LOCALAPPDATA%\Programs\PdxNext`、`Program Files` 下的 PdxNext。
4. 不改 `version.props` / `CHANGELOG.md` / `docs/HANDOFF.md`（协调者专属）。
5. 不提交 `bin/`、`obj/`、`.scratch/`、`data/`。
6. 文案改动两种语言的 `.resw` 必须同时补齐（编译期生成器校验键集与占位符）。
7. 不 `git push`、不 force push、不改写已推送历史（除非用户明确要求）。

## 5. 交付格式

每轮结束给四行：**做了什么 / 证据（`dev.cmd build` 输出 + 实测）/ 没做什么及原因 / 下一步建议**。

## 6. 深入

- `docs/AGENT-WORKFLOW.md`：角色、分支、派活模板、并发隔离、验收七条、§10 本地开发入口
- `docs/DEVELOPMENT.md`：构建、目录结构、自检、数据隔离、排错
- `docs/VERSIONING.md`：版本号、发版流程、分支与热修
- `docs/HANDOFF.md`：逐轮交接记录
