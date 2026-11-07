# Agent 工作流（1.3 起）

从 1.3 起，本仓库的**实际编码由 subagent 完成**，主会话只做「理解需求 → 拆分 → 派活 → 验收 → 合并 → 写文档」。
每个新对话的行为入口是根目录 `AGENTS.md`（开工 / 收尾三步）；这份文档是那套约定的详细版：
新任务照着 §3 的模板派活，照着 §6 的清单验收。

## 1. 角色

| 角色 | 谁 | 做什么 | 不做什么 |
| --- | --- | --- | --- |
| 协调者 | 主会话（唯一有用户上下文的一方） | 复述需求 / 定验收 / 切任务 / 划写范围 / 派活 / 合并 / 写 `HANDOFF`+`CHANGELOG` | 不亲自写业务代码（除一行级修正与冲突解决） |
| 实现者 | 一个 subagent = 一个任务 | 读代码、改代码、自己构建+自检、提交 | 不越界改文件；不碰 `version.props`/`CHANGELOG.md`/`docs/HANDOFF.md` |
| 验收者 | 另起的 subagent（全新上下文） | 只看 diff + 验收标准，自己再跑一遍构建与自检，逐条核对 | 不参与实现，不「顺手修一下」 |

为什么要一个全新上下文的验收者：实现者的汇报天然带着它自己的假设（「我改对了」）。
只读 diff 的评审者能发现越界改动、漏掉的空值路径和「自检没覆盖到」的错觉；这比协调者自己看 diff 可靠。

## 2. 分支

- `main` 永远可发布；1.3 期间的集成分支是 `feat/1.3`。
- **一个对话一个分支**：每轮新对话开工先跑

  ```bat
  pwsh -File scripts/session.ps1 open <slug>
  ```

  得到 `sess/YYYYMMDD-<slug>`，基线是**对话开始时的当前分支**（通常是 `feat/1.3`）。
  对话期间所有提交都留在它上面，不往起始分支写任何东西。
- 对话结束、验收通过后跑 `pwsh -File scripts/session.ps1 close -Message "中文说明"`：
  `--no-ff` 合回起始分支（合并信息 `merge: 中文说明（sess/…）`），删掉 sess 分支并切回起始分支。
  **一个对话只合并一次**；没做完就不合并，分支留着写进汇报。
- 并行写任务仍在 worktree 里开 `feat/<task>`，但基线是**本对话的 sess 分支**，合回也是 sess 分支，
  不再直接合 `feat/1.3`。
- 1.3.0 发版时才 `feat/1.3` → `main`，并按 `docs/VERSIONING.md` §4 全流程走（含本机安装版本校验）。
- **1.3 期间不改 `version.props`**，版本号停在 1.2.2；改动累积在 `CHANGELOG.md` 的 `[Unreleased]` 下。

## 3. 派活模板（每个 subagent 的 brief 必须包含这七项）

```
目标：<一句话，说清用户能看到的行为变化>
验收：<可验证断言，例如「窄窗口下按钮不折行」「自检新增 XxxChecks 且失败 0」>
仓库：G:\Work\paradox启动器，分支 <本对话的 sess 分支>（worktree：.agents\worktrees\<task>）
必读：AGENTS.md、docs/DEVELOPMENT.md §4 自检、§8 界面规范；docs/HANDOFF.md 中相关的 §0.NN
写范围：<精确到文件>；范围外的文件一律不动，需要时先停下回报
硬约束：-m:1 构建；4px 间距；.cmd 纯 ASCII；文案改动只改 Resources.resw（生成器编译期校验键集）；
        不写官方启动器目录；构建前确认启动器没在运行；**不得自行建分支或切分支**
交付：commit + `dev.cmd build` 的实际输出（DEV_RESULT=PASS、构建 0 警告 0 错误、检查类 N / 失败 0）
      + 改动文件清单 + 没做到的部分
```

写范围要精确到文件，因为「同一工作树里有两个写入者」是本仓库历史上最常见的踩坑方式
（见 `docs/HANDOFF.md` §0.4x 的两次记录）。

## 4. 并发与隔离

- **只读任务**（调研、定位、日志分析、评审）：可并行 ≤4，直接在本次对话的 sess 分支工作树上跑。
- **写码任务**：默认串行 1 个，在本次对话的 sess 分支主工作树上做；子 agent 不建分支、不切分支。
- 需要同时写代码时，每个任务一条 worktree，基线是**本对话的 sess 分支**：

  ```bat
  git worktree add .agents\worktrees\<task> -b feat/<task> <sess 分支>
  ```

  各自构建 + 自检，协调者按依赖顺序 `git merge --no-ff feat/<task>` 合回 sess 分支后 `git worktree remove`。
- **同一工作树同一时刻只允许一次 `dotnet build`**：`bin/`、`obj/` 会互相覆盖，构建还会锁
  `PdxLauncher.Core.dll`。
- 一波最多 2 个写任务 + 1 个只读核对；写范围有重叠就判为拆分错误，改依赖串行。

### 4.1 用户机器的只读边界（2026-11-05 事故后新增，硬规则）

- **禁止**向任何用户已安装的目录写入、覆盖或删除文件，包括 `D:\app\PdxNext`、
  `%LOCALAPPDATA%\Programs\PdxNext`、`Program Files` 下的 PdxNext，以及它们的 `data\`。
  安装/升级/卸载只允许由协调者在明确发版流程里执行。
- UI 验收需要「像已安装一样」时，只允许：把发布目录复制到 `%TEMP%\pdxnext-uitest-*`，
  或 `subst` 一个盘符指向临时目录，在副本上启动；绝不能把 dev build 铺到真实安装目录上去
  图「注册表里 App Path 对得上」。
- 验收者必须区分「代码对」和「环境对」：如果一条结论依赖真实安装态，而环境被改动过，
  必须写「环境不干净，待发版安装后复验」，不许当作已通过。
- 事故记录：`feat/install-mode-ui` 为验证「已安装」文案，把 build 输出覆盖进
  `D:\app\PdxNext` 并启动，污染了用户 1.2.2 安装与 `data\pdxnext.sqlite`；
  已用 `PdxNext-Setup-1.2.2.exe /VERYSILENT /DIR=D:\app\PdxNext` 恢复并用
  `tools\verify-installed-version.ps1` 校验通过。

## 5. 每个任务的固定检查

```bat
dotnet build "PdxLauncher.sln" -c Release -m:1
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"
:: 文案键集由 PdxLauncher.Localization.Generator 在编译期校验（缺键/占位符不一致直接报错）
```

- `-m:1` 是硬要求（并行节点会让 App 项目 0 错 0 警告地静默失败）。
- 判定通过：构建 0 警告 0 错误，且输出 `失败: 0`。
- 上面两条命令等价于 `dev.cmd build`：它先结束旧实例，再跑这两条，末尾输出 `DEV_RESULT=` 与退出码，适合自动化验收。
- 纯文档改动不需要构建。
- UI 改动要在真机/UIA 上看一眼，并把实测结论（窗口尺寸、DIP 数值、日志行）写进汇报：
  「编译过了」不等于「界面对了」。

## 6. 验收清单（验收 subagent 逐条回答）

1. diff 里有没有超出写范围的文件？
2. 新增/改动的文案，两种语言的 Resources.resw 是否同时补齐（生成器编译期校验键集与占位符）？
3. 新写的 `Padding`/`Margin`/`Spacing`/`CornerRadius` 是否都是 4 的倍数（`/*/0/4/8`）？
4. 异常路径：文件缺失、网络失败、空列表、超长字符串，有没有未观察的 `Task` 异常或空 catch？
5. 有没有配套自检类？断言是否真的能失败（不是永远为真）？
6. 提交里有没有带 `bin/`、`obj/`、`.scratch/`、`data/`？
7. 有没有偷偷改 `version.props` / `CHANGELOG.md` / `docs/HANDOFF.md`？

任何一条不过就退回重做，不允许「先合再修」。

## 7. 文档归属

| 文件 | 谁写 | 时机 |
| --- | --- | --- |
| `docs/HANDOFF.md` | 协调者 | 每个任务合并后追加 `## 0.NN 日期：标题`（问题 / 改法 / 验证 / 注意），编号取最大 +1 |
| `CHANGELOG.md` | 协调者 | 同上，追加到 `[Unreleased]` 的 Added / Changed / Fixed |
| `version.props` | 协调者 | 只在发版时 |
| `docs/*.md` 专项文档 | 实现者 | 任务本身就需要改时（写范围里写明） |

集中写的原因：`HANDOFF.md` 是单文件巨型文档，多会话各写各的必然冲突，而冲突恰恰会吃掉最贵的上下文。

## 8. 提交与合并

- 提交信息：`type(scope): 中文摘要`（`feat` / `fix` / `perf` / `test` / `docs` / `chore`），正文写根因与行为变化。
- 合并分两层：
  - worktree 的 `feat/<task>` → 本对话的 sess 分支：`git merge --no-ff feat/<task>`，合并信息 `merge: 中文说明（分支名）`。
  - sess 分支 → 起始分支：`pwsh -File scripts/session.ps1 close -Message "中文说明"`，
    脚本负责 `--no-ff`、删除分支、切回起始分支；纯 `*.md` 改动自动跳过构建。
- 合并后删掉任务分支与 worktree；`git worktree list` 里不应留下已合并的分支。
- 不 push，不 force push，不改写 `main` 历史，不在 `version.props` 已经进 Release 之后回去改版本号。
- 为什么合并交给脚本：历史上 31 次合并用了 git 默认信息，本节要求的 `merge: 中文说明` 只执行过 2 次。

## 9. 汇报格式

每轮结束给用户四行：**做了什么 / 证据（构建、自检、实测）/ 没做什么及原因 / 下一步建议**，
并写明本对话分支去向：`sess/… → 已合回 <起始分支>（commit）`。
细节留在 `HANDOFF.md`，不在对话里复述实现流水账。

## 10. 本地开发入口

### 10.1 命令

在仓库根用 `cmd /c` 执行：

| 命令 | 做什么 |
| --- | --- |
| `dev.cmd build` | 结束旧实例 → `dotnet build "PdxLauncher.sln" -c Release -m:1` → 自检；不启动 |
| `dev.cmd` | 同上，构建与自检通过后启动，并确认进程在 10 秒内没有退出 |
| `dev.cmd nobuild` | 不构建，结束旧实例后直接启动已有产物 |
| `status.cmd` | 只读显示分支、HEAD、已暂存 / 未暂存 / 未跟踪计数、改动文件、diff 统计、最近 5 次提交、运行态与构建时间；默认等待按键 |
| `status.cmd nopause` | 同上，不等待按键（VS Code 任务与自动化用） |

### 10.2 机器可读结果

`dev.cmd` 末尾固定输出：

```
DEV_RESULT=PASS|FAIL_BUILD|FAIL_CHECK|FAIL_NOEXE|FAIL_LAUNCH
DEV_MODE=run|build|nobuild
DEV_BRANCH=feat/1.3
DEV_HEAD=ac7225e
DEV_DIRTY=213
DEV_EXE=<绝对路径>
DEV_DATA=<exe目录>\data
DEV_LOG=<exe目录>\data\logs\pdxnext.log
DEV_PID=12345
```

退出码：`0` 通过、`1` 构建失败、`2` 自检失败、`3` 没有可执行文件、`4` 启动后 10 秒内退出。

### 10.3 注意

- `dev.cmd` 默认**结束正在运行的 `PdxLauncher.App.exe`**：运行中的 exe 会锁 `PdxLauncher.Core.dll`，
  旧窗口也会让 UI 检查量到旧构建。
- 同一工作树同一时刻只允许一个 `dev.cmd` / `dotnet build`；两个对话同时跑会互相覆盖 `bin/`、`obj/`。
- `status.cmd` 只读，不写任何文件。改动文件最多列 60 个，超出部分打印剩余项数；
  完整清单仍用 `git status`。
- 真机数据检查（如 V3 `dlc_metadata`）会随本机安装状态而红；这时 `dev.cmd` 以退出码 2
  拒绝启动。只想看界面可用 `dev.cmd nobuild`，但汇报里必须写清「自检未过」的原因。
- `DEV_DIRTY` 是 `git status --porcelain` 的条目数；无 git 时为 `NA`，不阻断构建。
