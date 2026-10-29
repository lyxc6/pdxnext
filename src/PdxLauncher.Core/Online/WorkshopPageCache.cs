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
    /// <summary>
    /// 默认新鲜期；过期后条目仍可立刻显示，只是会触发后台静默刷新。
    /// <para>
    /// 原来只有 5 分钟：用户翻两页再翻回来就会又抓一次 SSR 页（每页 1–3 MB），
    /// 是列表侧最容易堆请求的地方。15 分钟足够覆盖一次「浏览一会儿」的会话。
    /// </para>
    /// </summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(15);

    /// <summary>保留期：超过这个时间的页直接丢弃（过期页仍可先显示，但不能无限堆在内存里）。</summary>
    public static readonly TimeSpan Retention = TimeSpan.FromHours(2);

    /// <summary>最多保留的页数；超出后按抓取时间淘汰最旧的。</summary>
    public const int MaxPages = 120;

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
    /// <param name="LastAttemptUtc">
    /// 最近一次「试着取这页」的时刻（含失败的静默刷新）。静默刷新按它节流，
    /// 避免过期页在每次进入页面时都重抓一遍。
    /// </param>
    public sealed record Page(
        IReadOnlyList<WorkshopInfoCacheEntry> Items,
        int TotalCount,
        int TotalPages,
        DateTimeOffset FetchedUtc,
        DateTimeOffset LastAttemptUtc = default);

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
            if (!_pages.TryGetValue(key, out var found))
            {
                return false;
            }

            // 超过保留期就当作没有：过期页只是「先显示、后台刷新」，不是永久缓存。
            if (_clock.UtcNow - found.FetchedUtc > Retention)
            {
                _pages.Remove(key);
                return false;
            }

            page = found;
            return true;
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

        var now = _clock.UtcNow;
        lock (_gate)
        {
            _pages[key] = new Page([.. items], totalCount, totalPages, now, now);
            EvictOverflowLocked();
        }
    }

    /// <summary>记一次「已尝试刷新这页」，用于静默刷新的节流判断。</summary>
    /// <param name="key">页键。</param>
    public void MarkAttempt(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_gate)
        {
            if (_pages.TryGetValue(key, out var page))
            {
                _pages[key] = page with { LastAttemptUtc = _clock.UtcNow };
            }
        }
    }

    /// <summary>距上次尝试是否已经超过 <paramref name="minInterval"/>（静默刷新据此节流）。</summary>
    /// <param name="page">页快照。</param>
    /// <param name="minInterval">两次尝试之间的最小间隔。</param>
    public bool CanReattempt(Page page, TimeSpan minInterval)
    {
        ArgumentNullException.ThrowIfNull(page);
        return page.LastAttemptUtc == default
               || _clock.UtcNow - page.LastAttemptUtc >= minInterval;
    }

    /// <summary>超出页数上限时淘汰最旧的页。</summary>
    private void EvictOverflowLocked()
    {
        if (_pages.Count <= MaxPages)
        {
            return;
        }

        var overflow = _pages.Count - MaxPages;
        foreach (var key in _pages
                     .OrderBy(pair => pair.Value.FetchedUtc)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _pages.Remove(key);
        }
    }

    /// <summary>
    /// 清空。页键里已经带了来源/游戏/筛选条件，正常情况下不需要手动清；
    /// 保留它是给「清缓存」这类显式动作用的。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _pages.Clear();
        }
    }
}
