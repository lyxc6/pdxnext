namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>冲突图构建器：从一组 <see cref="ModFootprint"/> 推导补丁先后边与歧义告警。纯函数、不碰 IO。</summary>
public static class ConflictGraphBuilder
{
    /// <summary>
    /// 对共享路径的每一对 mod 决定先后：
    /// 共带新增文件时——恰一方名字带补丁/翻译标记→它排后（名字是明确声明，跨层位也成立）；
    /// 层位不同→不建边，只记 <see cref="AutoSortConflictKind.SharedAddedFileTierDecided"/>（层位优先）；
    /// 同层位且一方新增路径严格超集→超集方排前（<see cref="SortEdgeKind.FootprintOverlap"/>）；
    /// 其余记 <see cref="AutoSortConflictKind.SharedAddedFileAmbiguous"/>。
    /// 同层位双方覆盖同一原版文件时记 <see cref="AutoSortConflictKind.VanillaOverrideSameTier"/>（无边）。
    /// </summary>
    public static ConflictGraph Build(IReadOnlyList<ModFootprint> footprints)
    {
        ArgumentNullException.ThrowIfNull(footprints);

        var edges = new List<SortEdge>();
        var conflicts = new List<AutoSortConflict>();
        var ranks = new int[footprints.Count];
        for (var i = 0; i < footprints.Count; i++)
        {
            ranks[i] = Rank(footprints[i]);
        }

        for (var i = 0; i < footprints.Count; i++)
        {
            for (var j = i + 1; j < footprints.Count; j++)
            {
                var a = footprints[i];
                var b = footprints[j];
                ResolveAddedOverlap(a, b, ranks[i], ranks[j], edges, conflicts);
                ReportVanillaOverlap(a, b, conflicts);
            }
        }

        return new ConflictGraph(edges, conflicts);
    }

    /// <summary>层位排名（大修 0 … 翻译 7），与排序器同一口径。</summary>
    private static int Rank(ModFootprint footprint) => FootprintClassifier.TierRank(
        FootprintClassifier.Classify(footprint),
        FootprintClassifier.IsDerivedPatchName(footprint.Name));

    /// <summary>共带新增文件：跨层位交给层位裁决，同层位只用「新增路径严格包含」判定基底。</summary>
    private static void ResolveAddedOverlap(
        ModFootprint a,
        ModFootprint b,
        int rankA,
        int rankB,
        List<SortEdge> edges,
        List<AutoSortConflict> conflicts)
    {
        var shared = Intersect(a.AddedPaths, b.AddedPaths);
        if (shared.Count == 0)
        {
            return;
        }

        var aIsPatch = FootprintClassifier.IsDerivedPatchName(a.Name);
        var bIsPatch = FootprintClassifier.IsDerivedPatchName(b.Name);
        if (aIsPatch != bIsPatch)
        {
            // 恰一方是补丁/翻译变体：它排后（补丁在基底之上加载才能生效）。
            var before = aIsPatch ? b : a;
            var after = aIsPatch ? a : b;
            edges.Add(new SortEdge(before.ModId, after.ModId, SortEdgeKind.DerivedPatch, shared[0]));
            return;
        }

        if (rankA != rankB)
        {
            // 层位不同：先后已由「谁覆盖的原版玩法内容更多」决定，不让文件体量反向裁决。
            conflicts.Add(new AutoSortConflict(
                a.ModId,
                b.ModId,
                AutoSortConflictKind.SharedAddedFileTierDecided,
                shared.Count,
                shared.Take(3).ToList()));
            return;
        }

        if (IsProperSuperset(a.AddedPaths, b.AddedPaths))
        {
            edges.Add(new SortEdge(a.ModId, b.ModId, SortEdgeKind.FootprintOverlap, shared[0]));
            return;
        }

        if (IsProperSuperset(b.AddedPaths, a.AddedPaths))
        {
            edges.Add(new SortEdge(b.ModId, a.ModId, SortEdgeKind.FootprintOverlap, shared[0]));
            return;
        }

        conflicts.Add(new AutoSortConflict(
            a.ModId,
            b.ModId,
            AutoSortConflictKind.SharedAddedFileAmbiguous,
            shared.Count,
            shared.Take(3).ToList()));
    }

    /// <summary>双方覆盖同一原版文件且层位相同：谁赢都合理，只告警（保持当前顺序）。</summary>
    private static void ReportVanillaOverlap(ModFootprint a, ModFootprint b, List<AutoSortConflict> conflicts)
    {
        var shared = Intersect(a.OverriddenPaths, b.OverriddenPaths);
        if (shared.Count == 0)
        {
            return;
        }

        var aRank = FootprintClassifier.TierRank(FootprintClassifier.Classify(a), FootprintClassifier.IsDerivedPatchName(a.Name));
        var bRank = FootprintClassifier.TierRank(FootprintClassifier.Classify(b), FootprintClassifier.IsDerivedPatchName(b.Name));
        if (aRank != bRank)
        {
            // 异层位由层序决定先后（后加载的补丁/微调获胜），无需告警。
            return;
        }

        conflicts.Add(new AutoSortConflict(
            a.ModId,
            b.ModId,
            AutoSortConflictKind.VanillaOverrideSameTier,
            shared.Count,
            shared.Take(3).ToList()));
    }

    /// <summary>两个集合的交集（升序，保证告警样本稳定）。</summary>
    private static List<string> Intersect(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        var shared = new List<string>();
        foreach (var path in a)
        {
            if (b.Contains(path))
            {
                shared.Add(path);
            }
        }

        shared.Sort(StringComparer.OrdinalIgnoreCase);
        return shared;
    }

    /// <summary>a 是否为 b 的严格超集（b 非空且 a 含 b 的全部元素并至少多一个）。</summary>
    private static bool IsProperSuperset(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (b.Count == 0 || a.Count <= b.Count)
        {
            return false;
        }

        foreach (var path in b)
        {
            if (!a.Contains(path))
            {
                return false;
            }
        }

        return true;
    }
}
