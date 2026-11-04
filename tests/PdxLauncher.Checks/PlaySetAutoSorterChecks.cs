namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 自动排序器自检：<see cref="PlaySetAutoSorter.Sort"/> 的层位出序、硬边压层级、
/// 稳定 tie-break、幂等确定性与成环兜底。
/// <para>纯内存用例。排序契约：大修在上、兼容补丁与小修改在下（后加载者在同文件冲突中获胜）、
/// 翻译最后生效。</para>
/// </summary>
public static class PlaySetAutoSorterChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckTierOrder();
        CheckDerivedPatchAfterBase();
        CheckHardEdgeOverridesTier();
        CheckPureAdditiveStable();
        CheckIdempotentAndDeterministic();
        CheckPermutation();
        CheckCycleWarning();
        CheckEmptyInput();
        CheckLargePlaySetScale();
        return ChecksContext.Failures - before;
    }

    private static void CheckTierOrder()
    {
        var items = new List<AutoSortInput>
        {
            Input("Tweak", SmallTweak(), 0),
            Input("翻译", Localization(), 1),
            Input("大修", Overhaul(), 2),
            Input("纯新增", PureAdditive(), 3),
        };

        var result = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());

        ChecksContext.Equal("层位出序：大修→纯新增→微调→翻译", "大修,纯新增,Tweak,翻译", Names(result, items));
    }

    private static void CheckDerivedPatchAfterBase()
    {
        var items = new List<AutoSortInput>
        {
            Input("X Compatch", PureAdditive(), 0),
            Input("Base Mod", PureAdditive(), 1),
        };

        var result = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());

        ChecksContext.Equal("名字带补丁标记的排在基底之后", "Base Mod,X Compatch", Names(result, items));
    }

    private static void CheckHardEdgeOverridesTier()
    {
        var translation = Input("翻译补丁", Localization(), 0);
        var overhaul = Input("大修", Overhaul(), 1);
        var items = new List<AutoSortInput> { translation, overhaul };
        var edges = new List<SortEdge>
        {
            new(translation.Footprint.ModId, overhaul.Footprint.ModId, SortEdgeKind.DeclaredDependency, "descriptor"),
        };

        var result = PlaySetAutoSorter.Sort(items, edges);

        ChecksContext.Equal("硬边压过层位（声明依赖先行）", "翻译补丁,大修", Names(result, items));
    }

    private static void CheckPureAdditiveStable()
    {
        var items = new List<AutoSortInput>
        {
            Input("A", PureAdditive(), 0),
            Input("B", PureAdditive(), 1),
            Input("C", PureAdditive(), 2),
        };

        var result = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());

        ChecksContext.Equal("同层位保持原相对顺序", "A,B,C", Names(result, items));
    }

    private static void CheckIdempotentAndDeterministic()
    {
        var items = new List<AutoSortInput>
        {
            Input("Tweak", SmallTweak(), 0),
            Input("大修", Overhaul(), 1),
            Input("翻译", Localization(), 2),
            Input("补丁", SmallTweak(), 3),
        };

        var first = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());
        var second = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());
        ChecksContext.Equal("同一输入两次结果一致", Names(first, items), Names(second, items));

        var reordered = first.Entries
            .Select((entry, newIndex) =>
                new AutoSortInput(items.First(i => i.Footprint.ModId == entry.ModId).Footprint, newIndex))
            .ToList();
        var again = PlaySetAutoSorter.Sort(reordered, Array.Empty<SortEdge>());
        ChecksContext.Equal("对已排序结果再排序不变化（幂等）", Names(first, items), Names(again, items));
    }

    private static void CheckPermutation()
    {
        var items = new List<AutoSortInput>
        {
            Input("Tweak", SmallTweak(), 7),
            Input("大修", Overhaul(), 3),
            Input("翻译", Localization(), 5),
        };

        var result = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());

        ChecksContext.Equal("结果条数与输入一致", items.Count, result.Entries.Count);
        ChecksContext.Equal(
            "ModId 全排列（集合不变）",
            string.Join(",", items.Select(i => i.Footprint.ModId).OrderBy(id => id)),
            string.Join(",", result.Entries.Select(e => e.ModId).OrderBy(id => id)));
        ChecksContext.Equal(
            "NewIndex 连续 0..n-1",
            "0,1,2",
            string.Join(",", result.Entries.Select(e => e.NewIndex)));
        ChecksContext.Equal(
            "OldIndex 保留原位置",
            "3,7,5",
            string.Join(",", result.Entries.Select(e => e.OldIndex)));
    }

    private static void CheckCycleWarning()
    {
        var a = Input("A", PureAdditive(), 0);
        var b = Input("B", PureAdditive(), 1);
        var items = new List<AutoSortInput> { a, b };
        var edges = new List<SortEdge>
        {
            new(a.Footprint.ModId, b.Footprint.ModId, SortEdgeKind.DerivedPatch, "x"),
            new(b.Footprint.ModId, a.Footprint.ModId, SortEdgeKind.DerivedPatch, "y"),
        };

        var result = PlaySetAutoSorter.Sort(items, edges);

        ChecksContext.Equal("成环仍产出完整顺序", 2, result.Entries.Count);
        ChecksContext.Check("成环输出警告", result.Warnings.Any(w => w.Kind == AutoSortWarningKind.DependencyCycleBroken));

        // 只报「哪几个 mod 成环」不足以让人决定拆哪条：被违背的边要一起带出来。
        var warning = result.Warnings.First(w => w.Kind == AutoSortWarningKind.DependencyCycleBroken);
        var violated = warning.Edges ?? Array.Empty<SortEdge>();
        ChecksContext.Equal("成环记录被违背的边数", 1, violated.Count);
        ChecksContext.Equal(
            "被违背的边是「B 应先于 A」",
            b.Footprint.ModId + "->" + a.Footprint.ModId,
            violated.Count == 0 ? "(无)" : violated[0].Before + "->" + violated[0].After);
    }

    private static void CheckEmptyInput()
    {
        var result = PlaySetAutoSorter.Sort(Array.Empty<AutoSortInput>(), Array.Empty<SortEdge>());
        ChecksContext.Equal("空输入 → 空结果", 0, result.Entries.Count);
        ChecksContext.Equal("空输入无警告", 0, result.Warnings.Count);
    }

    /// <summary>数百 mod 同享一条覆盖路径：输出完整排列、无警告、两次运行结果一致。</summary>
    private static void CheckLargePlaySetScale()
    {
        const int count = 300;
        const string shared = "common/x.txt";
        var overridden = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { shared };
        var items = new List<AutoSortInput>(count);
        for (var i = 0; i < count; i++)
        {
            var footprint = new ModFootprint(
                Guid.NewGuid(),
                "M" + i,
                100 + (i % 50),
                1 + (i % 20),
                0,
                0,
                0,
                overridden,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            items.Add(new AutoSortInput(footprint, i));
        }

        var first = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());
        var second = PlaySetAutoSorter.Sort(items, Array.Empty<SortEdge>());

        ChecksContext.Equal("300 mod 全部出序", count, first.Entries.Count);
        ChecksContext.Check(
            "300 mod 的 NewIndex 连续 0..299",
            first.Entries.Select(e => e.NewIndex).SequenceEqual(Enumerable.Range(0, count)));
        ChecksContext.Equal("300 mod 无警告", 0, first.Warnings.Count);
        ChecksContext.Check(
            "两次排序结果一致",
            first.Entries.Select(e => e.ModId).SequenceEqual(second.Entries.Select(e => e.ModId)));
    }

    private static AutoSortInput Input(string name, ModFootprint footprint, int currentIndex) =>
        new(footprint with { Name = name }, currentIndex);

    private static ModFootprint Overhaul() => Foot(2000, 200);
    private static ModFootprint SmallTweak() => Foot(50, 3);
    private static ModFootprint PureAdditive() => Foot(50, 0);

    private static ModFootprint Localization() =>
        new(Guid.NewGuid(), "L", 100, 0, 80, 0, 0, new HashSet<string>(), new HashSet<string>());

    private static ModFootprint Foot(int totalFiles, int gameplayOverrides) =>
        new(Guid.NewGuid(), "M", totalFiles, gameplayOverrides, 0, 0, 0, new HashSet<string>(), new HashSet<string>());

    private static string Names(AutoSortResult result, IReadOnlyList<AutoSortInput> items) =>
        string.Join(",", result.Entries.Select(e => items.First(i => i.Footprint.ModId == e.ModId).Footprint.Name));
}
