namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 对话分支脚本自检。<c>scripts/session.ps1</c> 是「一个对话一个分支」的唯一执行者，
/// 它退化成「必须等别的对话收尾」时不会报任何错 —— 只会让并行工作卡住、让别人的半成品
/// 被卷进 wip 基线。这里把那几条行为钉成断言：
/// <list type="bullet">
/// <item>并行对话走 <c>open -Worktree</c>（不碰当前工作区），不是「等对方 close」；</item>
/// <item>脏工作区默认拒绝，只有显式 <c>-CarryDirty</c> 才带走改动；</item>
/// <item>close 在「检出基线的那个工作树」里合并，那棵树自己脏时拒绝（退出 6）；</item>
/// <item>链接工作树收尾后 detach，否则它会把基线锁给一棵马上要删掉的树；</item>
/// <item>文档不再把已冻结的 <c>feat/1.3</c> 当现行主干工作树，检查类计数与真实文件数一致。</item>
/// </list>
/// </summary>
public static class SessionScriptChecks
{
    /// <summary>「已经在对话分支上」时给出的两种出路：收尾这条，或另开一棵树并行。</summary>
    private static readonly Regex AlreadyOnSessionPattern = new(
        @"已经在对话分支[^\r\n]*\r?\n?[\s\S]{0,400}session\.ps1 close[\s\S]{0,400}open <slug> -Worktree",
        RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[SessionScriptChecks] 对话分支脚本与并行规则");

        var root = FindWorkspaceRoot();
        var scriptPath = Path.Combine(root, "scripts", "session.ps1");
        if (!ChecksContext.Check("scripts/session.ps1 存在", fs.FileExists(scriptPath), scriptPath))
        {
            return ChecksContext.Failures - before;
        }

        var text = fs.ReadAllText(scriptPath);
        CheckOpenSupportsWorktree(text);
        CheckDirtyGate(text);
        CheckAlreadyOnSessionMessage(text);
        CheckCloseMergesInBaseOwner(text);
        CheckCloseDetachesLinkedWorktree(text);
        CheckListShowsOwner(text);
        CheckWorkflowDoc(root);
        CheckCheckClassCounts(root, fs);

        return ChecksContext.Failures - before;
    }

    /// <summary>open 必须能另开一棵工作树：并行对话不用等别人。</summary>
    private static void CheckOpenSupportsWorktree(string text)
    {
        ChecksContext.Check("open 声明 -Worktree 开关", text.Contains("[switch]$Worktree", StringComparison.Ordinal));
        ChecksContext.Check("open 声明 -CarryDirty 开关", text.Contains("[switch]$CarryDirty", StringComparison.Ordinal));
        ChecksContext.Check(
            "open -Worktree 走 git worktree add（不是 switch 到当前树）",
            text.Contains("'worktree', 'add', '-b'", StringComparison.Ordinal));
        ChecksContext.Check(
            "open -Worktree 把工作树路径记进 pdxWorktree（close/list 才能找回它）",
            text.Contains("pdxWorktree", StringComparison.Ordinal));
        ChecksContext.Check(
            "open 输出 SESSION_WORKTREE= 供 agent 解析",
            text.Contains("SESSION_WORKTREE=", StringComparison.Ordinal));
        ChecksContext.Check(
            "open -Worktree 允许从 sess 分支直接开并行对话（取该分支的 pdxBase）",
            text.Contains("$baseFromSess", StringComparison.Ordinal));
    }

    /// <summary>脏工作区默认拒绝：把别人的半成品扫进 wip 提交是最难拆的坑。</summary>
    private static void CheckDirtyGate(string text)
    {
        ChecksContext.Check(
            "脏工作区不带 -CarryDirty 时退出 3（不再无条件 git add -A）",
            text.Contains("-not $CarryDirty", StringComparison.Ordinal) && text.Contains("exit 3", StringComparison.Ordinal));
        ChecksContext.Check(
            "wip 基线提交信息列出带走的文件（超过 10 项折叠）",
            text.Contains("$carried", StringComparison.Ordinal) && text.Contains("等 $($carried.Count) 项", StringComparison.Ordinal));
        ChecksContext.Check(
            "拒绝时给出三条出路：先提交 / -Worktree 另开 / -CarryDirty",
            text.Contains("加 -Worktree 另开一棵工作树", StringComparison.Ordinal)
                && text.Contains("加 -CarryDirty", StringComparison.Ordinal));
    }

    /// <summary>「已经在对话分支上」必须给并行出路，不能只说「先 close」。</summary>
    private static void CheckAlreadyOnSessionMessage(string text)
    {
        var match = AlreadyOnSessionPattern.Match(text);
        ChecksContext.Check(
            "已在对话分支时的提示同时给 close 与 -Worktree 两条路",
            match.Success,
            match.Success ? "ok" : "只提示了其中一条（并行对话会被迫等待）");
    }

    /// <summary>close 在基线所在的工作树里合并；那棵树脏时拒绝，不把别人半成品卷进来。</summary>
    private static void CheckCloseMergesInBaseOwner(string text)
    {
        ChecksContext.Check(
            "close 定位检出基线的那个工作树",
            text.Contains("Get-WorktreeOwningBranch", StringComparison.Ordinal));
        ChecksContext.Check(
            "close 在别的目录里合并（Invoke-Git -In）",
            text.Contains("-In $mergeIn", StringComparison.Ordinal));
        ChecksContext.Check(
            "基线那棵树有未提交改动时退出 6（不替别人合并半成品）",
            text.Contains("exit 6", StringComparison.Ordinal));
        ChecksContext.Check(
            "退出 6 的提示指名占用路径与两条排查命令",
            text.Contains("[那棵树的改动]", StringComparison.Ordinal)
                && text.Contains("[那边在哪条分支上]", StringComparison.Ordinal));
        ChecksContext.Check(
            "切回基线失败时解析 'already used by worktree' 并指出占用树",
            text.Contains("Get-CheckoutConflictPath", StringComparison.Ordinal)
                && text.Contains("already used by worktree at", StringComparison.Ordinal));
        ChecksContext.Check(
            "冲突时中止合并并保留对话分支（退出 2）",
            text.Contains("'merge', '--abort'", StringComparison.Ordinal));
    }

    /// <summary>链接工作树收尾后必须 detach，否则它把基线锁给一棵待删除的树。</summary>
    private static void CheckCloseDetachesLinkedWorktree(string text)
    {
        ChecksContext.Check(
            "识别链接工作树（--git-dir 落在 /worktrees/ 下）",
            text.Contains("Test-LinkedWorktree", StringComparison.Ordinal)
                && text.Contains("/worktrees/", StringComparison.Ordinal));
        ChecksContext.Check(
            "链接工作树收尾后 switch --detach（不占着基线分支）",
            text.Contains("'switch', '--detach'", StringComparison.Ordinal));
    }

    /// <summary>list 要能看出谁占着基线、那棵树干净不干净 —— 这是「是不是要排队」的唯一信号。</summary>
    private static void CheckListShowsOwner(string text)
    {
        ChecksContext.Check(
            "list 打印并行写法提示",
            text.Contains("open <slug> -Worktree", StringComparison.Ordinal));
        ChecksContext.Check(
            "list 标注每棵工作树是否干净",
            text.Contains("$wtDirty", StringComparison.Ordinal));
        ChecksContext.Check(
            "list 打印基线所在的工作树",
            text.Contains("基线所在的工作树", StringComparison.Ordinal));
    }

    /// <summary>文档口径：feat/1.3 早已冻结，不能再写成现行主干工作树。</summary>
    private static void CheckWorkflowDoc(string root)
    {
        var workflow = Path.Combine(root, "docs", "AGENT-WORKFLOW.md");
        if (!ChecksContext.Check("docs/AGENT-WORKFLOW.md 存在", File.Exists(workflow), workflow))
        {
            return;
        }

        var text = File.ReadAllText(workflow);
        ChecksContext.Check(
            "§4 不再把 feat/1.3 当现行主干工作树",
            !text.Contains("在 `feat/1.3` 主干工作树上做", StringComparison.Ordinal));
        ChecksContext.Check(
            "§4 写明并行写用 -Worktree、合并串行",
            text.Contains("-Worktree", StringComparison.Ordinal)
                && text.Contains("串行", StringComparison.Ordinal));
        ChecksContext.Check(
            "§2 写明脏工作区默认拒绝（-CarryDirty 才带走）",
            text.Contains("-CarryDirty", StringComparison.Ordinal));
    }

    /// <summary>README 与 DEVELOPMENT 里的检查类计数必须和真实文件数一致（历史上已经漂过两次）。</summary>
    private static void CheckCheckClassCounts(string root, IFileSystem fs)
    {
        var checksDir = Path.Combine(root, "tests", "PdxLauncher.Checks");
        if (!Directory.Exists(checksDir))
        {
            ChecksContext.Check("tests/PdxLauncher.Checks 目录存在", false, checksDir);
            return;
        }

        var actual = Directory.GetFiles(checksDir, "*Checks.cs").Length;

        foreach (var (relative, label) in new[]
        {
            (Path.Combine("README.md"), "README.md"),
            (Path.Combine("docs", "DEVELOPMENT.md"), "docs/DEVELOPMENT.md"),
        })
        {
            var path = Path.Combine(root, relative);
            if (!ChecksContext.Check(label + " 存在", fs.FileExists(path), path))
            {
                continue;
            }

            var text = fs.ReadAllText(path);
            ChecksContext.Check(
                label + " 的检查类计数等于实际文件数（" + actual + "）",
                text.Contains(actual + " 个检查类", StringComparison.Ordinal),
                actual + " 个");
        }
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
