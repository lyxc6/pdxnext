namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 自动排序差异模型的回归护栏：预览对话框要靠它展示「谁移到哪 + 理由」，
/// 这里锁住行序、旧位映射、Moved 标记、理由映射与冲突/警告透传，
/// 保证界面只是排版、不承担任何排序语义。
/// 纯内存用例，不读写磁盘。
/// </summary>
public static class AutoSortDiffChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckRowsFollowNewOrder();
        CheckNamesComeFromModId();
        CheckReasonsMapFromEdges();
        CheckFootprintOverlapReason();
        CheckConflictsSortedBySharedCount();
        CheckWarningsAndConflictsPassThrough();
        CheckEmptyEntries();

        return ChecksContext.Failures - before;
    }

    /// <summary>行按新顺序排列，姓名/旧位取自旧表，Moved 与 MovedCount 只认位置变化。</summary>
    private static void CheckRowsFollowNewOrder()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var oldRows = new[]
        {
            Mod(a, "甲", 0),
            Mod(b, "乙", 1),
            Mod(c, "丙", 2),
        };
        // 新顺序：甲不动，丙上到第 2 位，乙下到第 3 位。
        var entries = new[]
        {
            Entry(a, 0, 0),
            Entry(c, 2, 1),
            Entry(b, 1, 2),
        };
        var preview = Preview(
            Array.Empty<SortEdge>(),
            Array.Empty<AutoSortConflict>(),
            Result(entries, Array.Empty<AutoSortWarning>()));

        var diff = AutoSortDiff.Build(preview, oldRows);

        ChecksContext.Equal("行数", 3, diff.Rows.Count);
        ChecksContext.Equal("第 1 行姓名", "甲", diff.Rows[0].Name);
        ChecksContext.Equal("第 1 行旧位", 0, diff.Rows[0].OldIndex);
        ChecksContext.Equal("第 1 行新位", 0, diff.Rows[0].NewIndex);
        ChecksContext.Check("第 1 行未移动", !diff.Rows[0].Moved);
        ChecksContext.Equal("第 2 行姓名", "丙", diff.Rows[1].Name);
        ChecksContext.Equal("第 2 行旧位", 2, diff.Rows[1].OldIndex);
        ChecksContext.Equal("第 2 行新位", 1, diff.Rows[1].NewIndex);
        ChecksContext.Check("第 2 行已移动", diff.Rows[1].Moved);
        ChecksContext.Equal("第 3 行姓名", "乙", diff.Rows[2].Name);
        ChecksContext.Check("第 3 行已移动", diff.Rows[2].Moved);
        ChecksContext.Equal("移动数", 2, diff.MovedCount);
    }

    /// <summary>旧行顺序在预览后若已变（位置指向了别的 mod），名字仍按 ModId 解析，绝不按位次猜。</summary>
    private static void CheckNamesComeFromModId()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var oldRows = new[]
        {
            Mod(a, "甲", 0),
            Mod(b, "乙", 1),
        };
        var entries = new[]
        {
            Entry(a, 0, 1),
            Entry(b, 1, 0),
        };
        var preview = Preview(
            Array.Empty<SortEdge>(),
            Array.Empty<AutoSortConflict>(),
            Result(entries, Array.Empty<AutoSortWarning>()));

        // 模拟预览生成后列表被刷新：位次 0 现在指向乙、位次 1 指向甲。
        var drifted = new[]
        {
            Mod(b, "乙", 0),
            Mod(a, "甲", 1),
        };

        var diff = AutoSortDiff.Build(preview, drifted);

        ChecksContext.Equal("位次漂移后第 1 行仍是甲", "甲", diff.Rows[0].Name);
        ChecksContext.Equal("位次漂移后第 1 行旧位保持", 0, diff.Rows[0].OldIndex);
        ChecksContext.Equal("位次漂移后第 2 行仍是乙", "乙", diff.Rows[1].Name);
    }

    /// <summary>边映射成理由：补丁边/依赖边都指到被跟随的 mod 名，无边的行理由为空。</summary>
    private static void CheckReasonsMapFromEdges()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var oldRows = new[]
        {
            Mod(a, "基础 mod", 0),
            Mod(b, "XX 补丁", 1),
            Mod(c, "内容包", 2),
        };
        var entries = new[]
        {
            Entry(a, 0, 0),
            Entry(b, 1, 1),
            Entry(c, 2, 2),
        };
        var edges = new[]
        {
            new SortEdge(a, b, SortEdgeKind.DerivedPatch, "common/x.txt"),
            new SortEdge(a, c, SortEdgeKind.DeclaredDependency, "descriptor 依赖"),
        };
        var preview = Preview(edges, Array.Empty<AutoSortConflict>(), Result(entries, Array.Empty<AutoSortWarning>()));

        var diff = AutoSortDiff.Build(preview, oldRows);

        ChecksContext.Equal("基础 mod 无理由", 0, diff.Rows[0].Reasons.Count);
        ChecksContext.Equal("补丁行理由数", 1, diff.Rows[1].Reasons.Count);
        ChecksContext.Equal("补丁行理由类型", AutoSortDiffReasonKind.DerivedPatchAfter, diff.Rows[1].Reasons[0].Kind);
        ChecksContext.Equal("补丁行指向基础 mod 名", "基础 mod", diff.Rows[1].Reasons[0].RelatedModName);
        ChecksContext.Equal("依赖行理由类型", AutoSortDiffReasonKind.DeclaredDependencyAfter, diff.Rows[2].Reasons[0].Kind);
        ChecksContext.Equal("依赖行指向依赖方名", "基础 mod", diff.Rows[2].Reasons[0].RelatedModName);
    }

    /// <summary>足迹包含边映射为「新增文件被某 mod 包含」，不能说成「作为其补丁」。</summary>
    private static void CheckFootprintOverlapReason()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var oldRows = new[] { Mod(a, "基础包", 0), Mod(b, "扩展包", 1) };
        var entries = new[] { Entry(a, 0, 0), Entry(b, 1, 1) };
        var edges = new[] { new SortEdge(a, b, SortEdgeKind.FootprintOverlap, "common/x.txt") };
        var preview = Preview(edges, Array.Empty<AutoSortConflict>(), Result(entries, Array.Empty<AutoSortWarning>()));

        var diff = AutoSortDiff.Build(preview, oldRows);

        ChecksContext.Equal("包含边理由类型", AutoSortDiffReasonKind.FootprintOverlapAfter, diff.Rows[1].Reasons[0].Kind);
        ChecksContext.Equal("包含边指向基底名", "基础包", diff.Rows[1].Reasons[0].RelatedModName);
    }

    /// <summary>冲突按共带文件数降序：共享得最多的先给人看。</summary>
    private static void CheckConflictsSortedBySharedCount()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var oldRows = new[] { Mod(a, "甲", 0), Mod(b, "乙", 1), Mod(c, "丙", 2) };
        var entries = new[] { Entry(a, 0, 0), Entry(b, 1, 1), Entry(c, 2, 2) };
        var conflicts = new[]
        {
            new AutoSortConflict(a, b, AutoSortConflictKind.SharedAddedFileAmbiguous, 2, Array.Empty<string>()),
            new AutoSortConflict(a, c, AutoSortConflictKind.SharedAddedFileTierDecided, 9, Array.Empty<string>()),
        };
        var preview = Preview(Array.Empty<SortEdge>(), conflicts, Result(entries, Array.Empty<AutoSortWarning>()));

        var diff = AutoSortDiff.Build(preview, oldRows);

        ChecksContext.Equal("冲突按共带数降序", 9, diff.Conflicts[0].SharedPathCount);
        ChecksContext.Equal("次多者排第二", 2, diff.Conflicts[1].SharedPathCount);
    }

    /// <summary>警告与冲突原样透传，对话框只负责排版。</summary>
    private static void CheckWarningsAndConflictsPassThrough()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var oldRows = new[] { Mod(a, "甲", 0), Mod(b, "乙", 1) };
        var entries = new[] { Entry(a, 0, 0), Entry(b, 1, 1) };
        var warnings = new[] { new AutoSortWarning(AutoSortWarningKind.DependencyCycleBroken, new[] { a, b }) };
        var conflicts = new[]
        {
            new AutoSortConflict(a, b, AutoSortConflictKind.SharedAddedFileAmbiguous, 3, new[] { "common/x.txt" }),
        };
        var preview = Preview(
            Array.Empty<SortEdge>(),
            conflicts,
            Result(entries, warnings));

        var diff = AutoSortDiff.Build(preview, oldRows);

        ChecksContext.Equal("警告数", 1, diff.Warnings.Count);
        ChecksContext.Equal("警告类型", AutoSortWarningKind.DependencyCycleBroken, diff.Warnings[0].Kind);
        ChecksContext.Equal("冲突数", 1, diff.Conflicts.Count);
        ChecksContext.Equal("冲突共用数", 3, diff.Conflicts[0].SharedPathCount);
        ChecksContext.Equal("移动数为 0", 0, diff.MovedCount);
    }

    /// <summary>没有条目时产物为空，而不是抛异常。</summary>
    private static void CheckEmptyEntries()
    {
        var preview = Preview(
            Array.Empty<SortEdge>(),
            Array.Empty<AutoSortConflict>(),
            Result(Array.Empty<AutoSortEntry>(), Array.Empty<AutoSortWarning>()));

        var diff = AutoSortDiff.Build(preview, Array.Empty<PlaySetMod>());

        ChecksContext.Equal("空条目行数", 0, diff.Rows.Count);
        ChecksContext.Equal("空条目移动数", 0, diff.MovedCount);
        ChecksContext.Equal("空条目警告数", 0, diff.Warnings.Count);
        ChecksContext.Equal("空条目冲突数", 0, diff.Conflicts.Count);
    }

    // ------------------------------------------------------------ 用例数据

    private static PlaySetMod Mod(Guid id, string name, int position) => new(
        id, name, string.Empty, null, string.Empty, null, string.Empty, string.Empty, true, position);

    private static AutoSortEntry Entry(Guid id, int oldIndex, int newIndex) => new(
        id, oldIndex, newIndex, ModKind.SmallTweak, false, 0);

    private static AutoSortResult Result(IReadOnlyList<AutoSortEntry> entries, IReadOnlyList<AutoSortWarning> warnings) =>
        new(entries, warnings);

    private static AutoSortPreview Preview(
        IReadOnlyList<SortEdge> edges,
        IReadOnlyList<AutoSortConflict> conflicts,
        AutoSortResult result) =>
        new(Array.Empty<ModFootprint>(), edges, conflicts, result, Array.Empty<PlaySetMod>());
}
