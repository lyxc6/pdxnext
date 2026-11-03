namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 冲突图自检：<see cref="ConflictGraphBuilder.Build"/> 的补丁先后边推导（名字标记/新增超集/数量/总量）
/// 与歧义告警（同层原版覆盖、无法判定的共带新增）。
/// <para>纯内存用例。真实依据：CK3 订阅里 gui/window_character.gui 被 10 个 mod 共带、
/// 「OE + CFP Compatch」这类补丁名自带基底关系。</para>
/// </summary>
public static class SortConflictGraphChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckDerivedNameEdge();
        CheckSupersetBase();
        CheckCountAndTotalBase();
        CheckAmbiguous();
        CheckVanillaSameTierConflict();
        CheckVanillaDifferentTierNoConflict();
        CheckNoShareNoGraph();
        return ChecksContext.Failures - before;
    }

    private static void CheckDerivedNameEdge()
    {
        var a = Foot("Ethnicities Overhaul", 200, [], ["common/x.txt"]);
        var b = Foot("OE + CFP Compatch", 3, [], ["common/x.txt"]);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("补丁名 → 一条补丁边", 1, graph.DerivedPatchEdges.Count);
        ChecksContext.Equal("边方向：基底在前、补丁在后", a.ModId + "->" + b.ModId, Edge(graph));
        ChecksContext.Equal("补丁边类型", SortEdgeKind.DerivedPatch, graph.DerivedPatchEdges[0].Kind);
        ChecksContext.Equal("补丁名场景不记歧义告警", 0, graph.Conflicts.Count);
    }

    private static void CheckSupersetBase()
    {
        var a = Foot("Base Pack", 200, [], ["common/s.txt", "common/x1.txt", "common/x2.txt"]);
        var b = Foot("Addons", 50, [], ["common/s.txt"]);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("新增路径超集方为基底", a.ModId + "->" + b.ModId, Edge(graph));
    }

    private static void CheckCountAndTotalBase()
    {
        var big = Foot("Big Mod", 30, [], ["common/s.txt", "common/y1.txt", "common/y2.txt"]);
        var small = Foot("Small Mod", 10, [], ["common/s.txt", "common/z.txt"]);
        var graph = ConflictGraphBuilder.Build([big, small]);
        ChecksContext.Equal("新增多者为基底", big.ModId + "->" + small.ModId, Edge(graph));

        var fat = Foot("Fat Mod", 100, [], ["common/s.txt", "common/y.txt"]);
        var thin = Foot("Thin Mod", 5, [], ["common/s.txt", "common/z.txt"]);
        var graph2 = ConflictGraphBuilder.Build([fat, thin]);
        ChecksContext.Equal("新增持平时总文件多者为基底", fat.ModId + "->" + thin.ModId, Edge(graph2));
    }

    private static void CheckAmbiguous()
    {
        var a = Foot("Fork A", 50, [], ["common/s.txt"]);
        var b = Foot("Fork B", 50, [], ["common/s.txt"]);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("完全对等共带 → 无边", 0, graph.DerivedPatchEdges.Count);
        ChecksContext.Equal("记一条歧义告警", 1, graph.Conflicts.Count);
        ChecksContext.Equal("告警类别", AutoSortConflictKind.SharedAddedFileAmbiguous, graph.Conflicts[0].Kind);
    }

    private static void CheckVanillaSameTierConflict()
    {
        var shared = new[] { "common/c1.txt", "common/c2.txt", "common/c3.txt", "common/c4.txt", "common/c5.txt" };
        var a = Foot("Tweak A", 3, shared, []);
        var b = Foot("Tweak B", 4, shared, []);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("同层原版覆盖不产生边", 0, graph.DerivedPatchEdges.Count);
        ChecksContext.Equal("记一条同层原版告警", 1, graph.Conflicts.Count);
        ChecksContext.Equal("告警类别", AutoSortConflictKind.VanillaOverrideSameTier, graph.Conflicts[0].Kind);
        ChecksContext.Equal("共享路径计数", 5, graph.Conflicts[0].SharedPathCount);
        ChecksContext.Equal("样本路径至多 3 条", 3, graph.Conflicts[0].SamplePaths.Count);
    }

    private static void CheckVanillaDifferentTierNoConflict()
    {
        var a = Foot("Overhaul", 200, ["common/c.txt"], []);
        var b = Foot("Tweak", 3, ["common/c.txt"], []);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("异层原版覆盖由层位决定先后，不告警", 0, graph.Conflicts.Count);
        ChecksContext.Equal("异层不产生边", 0, graph.DerivedPatchEdges.Count);
    }

    private static void CheckNoShareNoGraph()
    {
        var a = Foot("A", 200, ["common/a.txt"], ["common/x.txt"]);
        var b = Foot("B", 3, ["common/b.txt"], ["common/y.txt"]);
        var graph = ConflictGraphBuilder.Build([a, b]);

        ChecksContext.Equal("无共享路径 → 空图", "0/0", graph.DerivedPatchEdges.Count + "/" + graph.Conflicts.Count);
    }

    private static ModFootprint Foot(
        string name,
        int gameplayOverrides,
        string[] overridden,
        string[] added)
    {
        return new ModFootprint(
            Guid.NewGuid(),
            name,
            gameplayOverrides + added.Length,
            gameplayOverrides,
            0,
            0,
            0,
            new HashSet<string>(overridden, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(added, StringComparer.OrdinalIgnoreCase));
    }

    private static string Edge(ConflictGraph graph) =>
        graph.DerivedPatchEdges.Count == 0
            ? "(无)"
            : graph.DerivedPatchEdges[0].Before + "->" + graph.DerivedPatchEdges[0].After;
}
