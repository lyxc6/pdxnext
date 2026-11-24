namespace PdxLauncher.Core.Mcp;

/// <summary>页面快照的统一分页计算。</summary>
public static class McpPageSnapshotPaging
{
    /// <summary>默认每页条数。</summary>
    public const int DefaultLimit = 20;

    /// <summary>最大每页条数。</summary>
    public const int MaxLimit = 100;

    /// <summary>按 offset/limit 切片并补齐分页元数据。</summary>
    public static McpPageSnapshot Slice(
        string pageId,
        string displayName,
        string gameId,
        IReadOnlyList<McpPageRow> rows,
        int offset,
        int limit)
    {
        var safeOffset = Math.Max(0, offset);
        var safeLimit = Math.Clamp(limit > 0 ? limit : DefaultLimit, 1, MaxLimit);
        var page = rows.Skip(safeOffset).Take(safeLimit).ToArray();
        var next = safeOffset + page.Length;
        var hasMore = next < rows.Count;
        return new McpPageSnapshot(
            pageId,
            displayName,
            gameId,
            [],
            page,
            rows.Count,
            safeOffset,
            hasMore,
            hasMore ? next : null);
    }
}
