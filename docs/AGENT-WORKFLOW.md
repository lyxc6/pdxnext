# Agent 工作流（1.3 起）

从 1.3 起，本仓库的**实际编码由 subagent 完成**，主会话只做「理解需求 → 拆分 → 派活 → 验收 → 合并 → 写文档」。
这份文档是那套约定的落盘版本：新任务照着 §3 的模板派活，照着 §6 的清单验收。

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
- 单个功能仍开 `feat/<task>`，从 `feat/1.3` 起，合回 `feat/1.3`（`--no-ff`，合并信息 `merge: 中文说明`）。
- 1.3.0 发版时才 `feat/1.3` → `main`，并按 `docs/VERSIONING.md` §4 全流程走（含本机安装版本校验）。
- **1.3 期间不改 `version.props`**，版本号停在 1.2.2；改动累积在 `CHANGELOG.md` 的 `[Unreleased]` 下。

## 3. 派活模板（每个 subagent 的 brief 必须包含这七项）

```
目标：<一句话，说清用户能看到的行为变化>
验收：<可验证断言，例如「窄窗口下按钮不折行」「自检新增 XxxChecks 且失败 0」>
仓库：G:\Work\paradox启动器，分支 feat/<task>（worktree：.agents\worktrees\<task>）
必读：docs/DEVELOPMENT.md §4 自检、§8 界面规范；docs/HANDOFF.md 中相关的 §0.NN
写范围：<精确到文件>；范围外的文件一律不动，需要时先停下回报
硬约束：-m:1 构建；4px 间距；.cmd 纯 ASCII；文案改动跑 gen-i18n --check；
        不写官方启动器目录；构建前确认启动器没在运行
交付：commit + 实际命令输出（构建 0 警告 0 错误、检查类 N / 失败 0）+ 改动文件清单 + 没做到的部分
```

写范围要精确到文件，因为「同一工作树里有两个写入者」是本仓库历史上最常见的踩坑方式
（见 `docs/HANDOFF.md` §0.4x 的两次记录）。

## 4. 并发与隔离

- **只读任务**（调研、定位、日志分析、评审）：可并行 ≤4，直接在主干工作树跑。
- **写码任务**：默认串行 1 个，在 `feat/1.3` 主干工作树上做。
- 需要同时写代码时，每个任务一条 worktree：

  ```bat
  git worktree add .agents\worktrees\<task> -b feat/<task> origin/feat/1.3
  ```

  各自构建 + 自检，协调者按依赖顺序合并回 `feat/1.3` 后 `git worktree remove`。
- **同一工作树同一时刻只允许一次 `dotnet build`**：`bin/`、`obj/` 会互相覆盖，构建还会锁
  `PdxLauncher.Core.dll`。
- 一波最多 2 个写任务 + 1 个只读核对；写范围有重叠就判为拆分错误，改依赖串行。

## 5. 每个任务的固定检查

```bat
dotnet build "PdxLauncher.sln" -c Release -m:1
dotnet "tests\PdxLauncher.Checks\bin\Release\net10.0\PdxLauncher.Checks.dll"
python tools\gen-i18n.py --check        :: 只在动了文案时
```

- `-m:1` 是硬要求（并行节点会让 App 项目 0 错 0 警告地静默失败）。
- 判定通过：构建 0 警告 0 错误，且输出 `失败: 0`。
- 纯文档改动不需要构建。
- UI 改动要在真机/UIA 上看一眼，并把实测结论（窗口尺寸、DIP 数值、日志行）写进汇报：
  「编译过了」不等于「界面对了」。

## 6. 验收清单（验收 subagent 逐条回答）

1. diff 里有没有超出写范围的文件？
2. 新增/改动的文案，`gen-i18n --check` 是否通过，中英键是否成对？
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
- 合并：`git merge --no-ff feat/<task>`，合并信息 `merge: 中文说明（分支名）`。
- 不 force push，不改写 `main` 历史，不在 `version.props` 已经进 Release 之后回去改版本号。

## 9. 汇报格式

每轮结束给用户四行：**做了什么 / 证据（构建、自检、实测）/ 没做什么及原因 / 下一步建议**。
细节留在 `HANDOFF.md`，不在对话里复述实现流水账。
