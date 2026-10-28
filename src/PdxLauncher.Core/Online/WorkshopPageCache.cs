namespace PdxLauncher.Core.Online;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 创意工坊列表页的内存缓存：按「来源 + 游戏 + 搜索词 + 排序 + 时间范围 + 页码」记住整页条目。
/// <para>
/// 目的：翻回看过的页不再重抓 Steam 那份 1–3 MB 的 SSR 页面（实测要 2–8 秒），
/// 也避免重建 30 行时的空白期。只在内存里，重启即失效 —— 磁盘上仍然只有
/// <see cref="WorkshopInfoCache"/> 的条目元数据与封面文件。
/// </para>
/// </summary>
public sealed class WorkshopPageCache
{
    /// <summary>默认新鲜期；过期后条目仍可立刻显示，只是会触发后台静默刷新。</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);

    private readonly IClock _clock;
    private readonly object _gate = new();
    private readonly Dictionary<string, Page> _pages = new(StringComparer.Ordinal);

    public WorkshopPageCache(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
    }

    /// <summary>一页的结果快照。</summary>
    /// <param name="Items">该页条目（与磁盘缓存同一模型，可直接建行）。</param>
    /// <param name="TotalCount">该筛选下的总条数。</param>
    /// <param name="TotalPages">总页数。</param>
    /// <param name="FetchedUtc">抓取时刻。</param>
    public sealed record Page(
        IReadOnlyList<WorkshopInfoCacheEntry> Items,
        int TotalCount,
        int TotalPages,
        DateTimeOffset FetchedUtc);

    /// <summary>缓存里的页数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _pages.Count;
            }
        }
    }

    /// <summary>
    /// 生成页键。六个维度都参与，避免切排序/时间范围/关键词后拿到别的页。
    /// </summary>
    /// <param name="source">来源。</param>
    /// <param name="gameId">游戏 ID。</param>
    /// <param name="search">搜索词（空表示浏览全部）。</param>
    /// <param name="sort">排序参数原值（例如 trend / mostupdated）。</param>
    /// <param name="days">Steam 时间范围天数；非 Steam 传 null。</param>
    /// <param name="page">从 0 开始的页码。</param>
    /// <returns>稳定可比较的字符串键。</returns>
    public static string Key(
        WorkshopSource source,
        string? gameId,
        string? search,
        string sort,
        int? days,
        int page)
    {
        var tag = WorkshopInfoCache.SourceTag(source);
        return string.Join(
            '|',
            tag,
            gameId ?? string.Empty,
            search ?? string.Empty,
            sort ?? string.Empty,
            days?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-",
            page.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>取一页；TTL 内才算新鲜命中（过期也返回 true，用 <see cref="IsFresh"/> 判断）。</summary>
    /// <param name="key">页键。</param>
    /// <param name="page">命中的页。</param>
    /// <returns>是否存在。</returns>
    public bool TryGet(string key, out Page page)
    {
        page = new Page([], 0, 0, DateTimeOffset.MinValue);
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            return _pages.TryGetValue(key, out page!);
        }
    }

    /// <summary>该页是否仍在 TTL 内。</summary>
    /// <param name="page">页快照。</param>
    /// <param name="ttl">新鲜期；默认 <see cref="DefaultTtl"/>。</param>
    /// <returns>是否新鲜。</returns>
    public bool IsFresh(Page page, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var elapsed = _clock.UtcNow - page.FetchedUtc;
        return elapsed >= TimeSpan.Zero && elapsed < (ttl ?? DefaultTtl);
    }

    /// <summary>写入（或覆盖）一页。</summary>
    /// <param name="key">页键。</param>
    /// <param name="items">条目快照。</param>
    /// <param name="totalCount">总条数。</param>
    /// <param name="totalPages">总页数。</param>
    public void Set(string key, IReadOnlyList<WorkshopInfoCacheEntry> items, int totalCount, int totalPages)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(items);

        lock (_gate)
        {
            _pages[key] = new Page([.. items], totalCount, totalPages, _clock.UtcNow);
        }
    }

    /// <summary>清空（换游戏或切换来源时调用，避免旧游戏的页被复用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
        }
    }
}
