namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>冲突图构建器：从一组 <see cref="ModFootprint"/> 推导补丁先后边与歧义告警。纯函数、不碰 IO。</summary>
public static class ConflictGraphBuilder
{
    /// <summary>
    /// 对共享路径的每一对 mod 决定先后：
    /// 共带新增文件时——恰一方名字带补丁/翻译标记→它排后；否则新增路径严格超集方排前；
    /// 否则新增多者排前；否则总文件多者排前；再判不出就记歧义告警。
    /// 同层位双方覆盖同一原版文件时记 <see cref="AutoSortConflictKind.VanillaOverrideSameTier"/>（无边）。
    /// </summary>
    public static ConflictGraph Build(IReadOnlyList<ModFootprint> footprints)
    {
        ArgumentNullException.ThrowIfNull(footprints);

        var edges = new List<SortEdge>();
        var conflicts = new List<AutoSortConflict>();

        for (var i = 0; i < footprints.Count; i++)
        {
            for (var j = i + 1; j < footprints.Count; j++)
            {
                var a = footprints[i];
                var b = footprints[j];
                ResolveAddedOverlap(a, b, edges, conflicts);
                ReportVanillaOverlap(a, b, conflicts);
            }
        }

        return new ConflictGraph(edges, conflicts);
    }

    /// <summary>共带新增文件（同一个新文件两边都给）：判定基底与补丁，产生硬边或歧义告警。</summary>
    private static void ResolveAddedOverlap(
        ModFootprint a,
        ModFootprint b,
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

        if (IsProperSuperset(a.AddedPaths, b.AddedPaths))
        {
            edges.Add(new SortEdge(a.ModId, b.ModId, SortEdgeKind.DerivedPatch, shared[0]));
            return;
        }

        if (IsProperSuperset(b.AddedPaths, a.AddedPaths))
        {
            edges.Add(new SortEdge(b.ModId, a.ModId, SortEdgeKind.DerivedPatch, shared[0]));
            return;
        }

        if (a.AddedPaths.Count != b.AddedPaths.Count)
        {
            var before = a.AddedPaths.Count > b.AddedPaths.Count ? a : b;
            var after = a.AddedPaths.Count > b.AddedPaths.Count ? b : a;
            edges.Add(new SortEdge(before.ModId, after.ModId, SortEdgeKind.DerivedPatch, shared[0]));
            return;
        }

        if (a.TotalFiles != b.TotalFiles)
        {
            var before = a.TotalFiles > b.TotalFiles ? a : b;
            var after = a.TotalFiles > b.TotalFiles ? b : a;
            edges.Add(new SortEdge(before.ModId, after.ModId, SortEdgeKind.DerivedPatch, shared[0]));
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
