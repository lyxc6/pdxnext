namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>排序输入：一份足迹 + 它当前在播放集里的位置（稳定 tie-break 用）。</summary>
public sealed record AutoSortInput(ModFootprint Footprint, int CurrentIndex);

/// <summary>排序结果的一行（按新顺序排列，<see cref="NewIndex"/> 即列表下标）。</summary>
public sealed record AutoSortEntry(
    Guid ModId,
    int OldIndex,
    int NewIndex,
    ModKind Kind,
    bool IsDerivedPatch,
    int VanillaOverrideCount);

/// <summary>排序警告类别。</summary>
public enum AutoSortWarningKind
{
    /// <summary>硬边成环，已按层位强制打破（被弹出的 mod 记录在 <see cref="AutoSortWarning.ModIds"/>）。</summary>
    DependencyCycleBroken,

    /// <summary>没找到原版内容目录的文件索引：所有 mod 都只能按「纯新增」处理，层位失效。</summary>
    VanillaIndexMissing,

    /// <summary>有启用 mod 的内容文件数为 0（未安装/路径失效），它们只能落在「纯新增」。</summary>
    EmptyFootprint,
}

/// <summary>排序警告。</summary>
/// <param name="Kind">警告类别。</param>
/// <param name="ModIds">相关 mod（<see cref="AutoSortWarningKind.VanillaIndexMissing"/> 为空）。</param>
/// <param name="Edges">成环时被违背的硬边，供预览解释「哪条先于哪条被打断」。</param>
public sealed record AutoSortWarning(
    AutoSortWarningKind Kind,
    IReadOnlyList<Guid> ModIds,
    IReadOnlyList<SortEdge>? Edges = null);

/// <summary>排序结果：新顺序 + 警告。</summary>
public sealed record AutoSortResult(
    IReadOnlyList<AutoSortEntry> Entries,
    IReadOnlyList<AutoSortWarning> Warnings);

/// <summary>
/// 播放集自动排序器：Kahn 拓扑（硬边压层级）+ 优先队列按 (TierRank, CurrentIndex) 出序。
/// 纯函数、不碰 IO；同一输入必得同一结果（幂等，重复应用不产生新变化）。
/// </summary>
public static class PlaySetAutoSorter
{
    /// <summary>
    /// 计算新顺序。<paramref name="edges"/> 中 Before 先于 After；未被边约束的按
    /// 层位（大修→中量→美化→纯新增→微调→补丁/翻译→翻译）+ 当前位置稳定出序。
    /// 硬边成环时按层位强制打破并输出 <see cref="AutoSortWarningKind.DependencyCycleBroken"/>。
    /// </summary>
    public static AutoSortResult Sort(IReadOnlyList<AutoSortInput> items, IReadOnlyList<SortEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(edges);

        if (items.Count == 0)
        {
            return new AutoSortResult(Array.Empty<AutoSortEntry>(), Array.Empty<AutoSortWarning>());
        }

        var indexById = new Dictionary<Guid, int>(items.Count);
        var ranks = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var footprint = items[i].Footprint;
            indexById.TryAdd(footprint.ModId, i);
            ranks[i] = FootprintClassifier.TierRank(
                FootprintClassifier.Classify(footprint),
                FootprintClassifier.IsDerivedPatchName(footprint.Name));
        }

        var successors = new List<int>[items.Count];
        var indegree = new int[items.Count];
        var edgesByAfter = new List<SortEdge>[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            successors[i] = new List<int>();
            edgesByAfter[i] = new List<SortEdge>();
        }

        foreach (var edge in edges)
        {
            if (edge.Before == edge.After
                || !indexById.TryGetValue(edge.Before, out var before)
                || !indexById.TryGetValue(edge.After, out var after))
            {
                continue;
            }

            successors[before].Add(after);
            indegree[after]++;
            edgesByAfter[after].Add(edge);
        }

        var queue = new PriorityQueue<int, (int Rank, int CurrentIndex)>();
        var emitted = new bool[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            if (indegree[i] == 0)
            {
                queue.Enqueue(i, (ranks[i], items[i].CurrentIndex));
            }
        }

        var entries = new List<AutoSortEntry>(items.Count);
        var brokenCycle = new List<Guid>();
        var violatedEdges = new List<SortEdge>();
        while (entries.Count < items.Count)
        {
            int node;
            if (queue.Count > 0)
            {
                node = queue.Dequeue();
            }
            else
            {
                // 硬边成环：按层位+当前位置强制弹出最优节点，打破环继续。
                node = FindBestRemaining(items, ranks, emitted);
                brokenCycle.Add(items[node].Footprint.ModId);

                // 记下这一刻被违背的边（要求它在前，但它还没出序），供预览解释断环。
                foreach (var edge in edgesByAfter[node])
                {
                    if (indexById.TryGetValue(edge.Before, out var beforeIndex) && !emitted[beforeIndex])
                    {
                        violatedEdges.Add(edge);
                    }
                }
            }

            emitted[node] = true;
            var footprint = items[node].Footprint;
            entries.Add(new AutoSortEntry(
                footprint.ModId,
                items[node].CurrentIndex,
                entries.Count,
                FootprintClassifier.Classify(footprint),
                FootprintClassifier.IsDerivedPatchName(footprint.Name),
                footprint.GameplayOverrides));

            foreach (var successor in successors[node])
            {
                if (emitted[successor])
                {
                    continue;
                }

                indegree[successor]--;
                if (indegree[successor] == 0)
                {
                    queue.Enqueue(successor, (ranks[successor], items[successor].CurrentIndex));
                }
            }
        }

        var warnings = brokenCycle.Count > 0
            ? new List<AutoSortWarning>
            {
                new(AutoSortWarningKind.DependencyCycleBroken, brokenCycle, violatedEdges.Distinct().ToList()),
            }
            : new List<AutoSortWarning>();
        return new AutoSortResult(entries, warnings);
    }

    /// <summary>在未出序的节点里找 (层位, 当前位置) 最优者。</summary>
    private static int FindBestRemaining(IReadOnlyList<AutoSortInput> items, int[] ranks, bool[] emitted)
    {
        var best = -1;
        for (var i = 0; i < items.Count; i++)
        {
            if (emitted[i])
            {
                continue;
            }

            if (best < 0
                || ranks[i] < ranks[best]
                || (ranks[i] == ranks[best] && items[i].CurrentIndex < items[best].CurrentIndex))
            {
                best = i;
            }
        }

        return best;
    }
}
