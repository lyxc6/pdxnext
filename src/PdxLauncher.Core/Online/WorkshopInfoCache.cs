namespace PdxLauncher.Core.Online;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 创意工坊 mod 信息缓存：按「来源 + id」存列表元数据、封面文件名与详情正文。
/// <para>
/// 目的：同一个 mod 在翻页、切游戏、重新进页面时不再重复抓取 —— 名称/作者/评分立刻可用，
/// 封面文件命中后不再下载，详情在 TTL 内直接复用。
/// </para>
/// <para>
/// v2 起数据落在自有库的 <c>workshop_items</c> 真表（<see cref="IWorkshopItemStore"/>），
/// 一行一个条目；本类只做键解析、TTL 判定与封面文件存在性检查，不再整份序列化。
/// 缺失或损坏一律视为无缓存，绝不把异常抛给调用方。
/// </para>
/// </summary>
public sealed class WorkshopInfoCache
{
    /// <summary>旧索引文件名（v1 遗留，仅用于识别与清理，不再写入）。</summary>
    public const string IndexFileName = "workshop-info.json";

    /// <summary>详情正文的默认新鲜期；超过后下次打开详情会重新抓取。</summary>
    public static readonly TimeSpan DefaultDetailTtl = TimeSpan.FromDays(7);

    /// <summary>
    /// 最多保留的条目数；超出后按抓取时间淘汰最旧的。
    /// 真实库里通常只有几百条，2000 条是给「换游戏 / 重装」留的余量，
    /// 同时把最坏占用钉住（单条正文另有 <see cref="MaxDetailChars"/> 上限）。
    /// </summary>
    public const int MaxEntries = 2000;

    /// <summary>详情正文最多保留的字符数；正文已清洗成纯文本，截断只影响极长帖子的尾巴。</summary>
    public const int MaxDetailChars = 16 * 1024;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _cacheRoot;
    private readonly IWorkshopItemStore _store;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统抽象（判断封面文件是否存在）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时钟（TTL 判定与新鲜度记账）。</param>
    /// <param name="cacheRoot">缓存根目录（封面文件仍落在这里）。</param>
    /// <param name="store">条目持久层。</param>
    public WorkshopInfoCache(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string cacheRoot,
        IWorkshopItemStore store)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        ArgumentNullException.ThrowIfNull(store);
        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _cacheRoot = cacheRoot;
        _store = store;
    }

    /// <summary>缓存根目录。</summary>
    public string CacheRoot => _cacheRoot;

    /// <summary>
    /// 封面根目录：与旧索引同根，清缓存时一起删掉。
    /// 条目里的 <see cref="WorkshopInfoCacheEntry.CoverFileName"/> 是相对这里的路径（含 steam/ 或 pdx/ 子目录）。
    /// </summary>
    public string CoverRoot => _cacheRoot;

    /// <summary>缓存里的条目数。</summary>
    public int Count => _store.Count;

    /// <summary>缓存键：<c>steam:{publishedfileid}</c> 或 <c>pdx:{modId}</c>。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">来源侧 id。</param>
    public static string Key(WorkshopSource source, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SourceTag(source) + ":" + id;
    }

    /// <summary>来源在缓存键与封面目录里使用的小写标记。</summary>
    /// <param name="source">来源。</param>
    public static string SourceTag(WorkshopSource source) => source == WorkshopSource.Steam ? "steam" : "pdx";

    /// <summary>
    /// 写入或合并一条列表元数据。传 null 的字段保留旧值，
    /// 这样列表刷新不会把已抓到的封面文件名与详情正文抹掉。
    /// </summary>
    /// <param name="entry">要写入的条目。</param>
    public void Upsert(WorkshopInfoCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        _store.Upsert(entry);
    }

    /// <summary>
    /// 批量写入或合并一组列表元数据；一个事务落库，只做一次容量淘汰。
    /// 列表页一次并进 30–50 条，逐条写会把「开连接 + 事务 + COUNT(*)」重复一遍。
    /// </summary>
    /// <param name="entries">要写入的条目。</param>
    public void UpsertMany(IReadOnlyList<WorkshopInfoCacheEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _store.UpsertMany(entries);
    }

    /// <summary>取一条快照；没有返回 false。返回的是副本，调用方改动不会影响缓存。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="entry">命中的条目副本。</param>
    public bool TryGet(string key, out WorkshopInfoCacheEntry entry)
    {
        entry = new WorkshopInfoCacheEntry();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var found = _store.Find(key);
        if (found is null)
        {
            return false;
        }

        entry = found;
        return true;
    }

    /// <summary>封面文件是否还在缓存目录里；在就返回绝对路径，调用方不必再下载。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="fullPath">封面文件的绝对路径。</param>
    public bool TryGetCachedCover(string key, out string fullPath)
    {
        fullPath = string.Empty;
        return TryGet(key, out var entry) && TryGetCachedCover(entry, out fullPath);
    }

    /// <summary>
    /// 已经拿到条目时直接判定封面文件是否存在（不查 SQLite）。
    /// <para>
    /// 页快照与离线快照里的条目本来就带 <see cref="WorkshopInfoCacheEntry.CoverFileName"/>：
    /// 列表页逐行再按 key 回查一次库是 N+1，30–2000 条时会把界面按住。
    /// </para>
    /// </summary>
    /// <param name="entry">条目快照。</param>
    /// <param name="fullPath">封面文件的绝对路径。</param>
    public bool TryGetCachedCover(WorkshopInfoCacheEntry entry, out string fullPath)
    {
        ArgumentNullException.ThrowIfNull(entry);
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(entry.CoverFileName))
        {
            return false;
        }

        var candidate = Path.Combine(_cacheRoot, entry.CoverFileName);
        if (!_fileSystem.FileExists(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>
    /// 该条目已缓存的封面<b>就是</b>给定 URL 时，返回封面绝对路径；否则返回 false。
    /// <para>
    /// 用在「同一张图不该存两份」的判定上：Steam 详情页画廊首图常常就是封面本身，
    /// 命中这里就不必再往截图目录里下载一遍。
    /// </para>
    /// </summary>
    /// <param name="key">缓存键（<see cref="Key"/>）。</param>
    /// <param name="url">待判定的图片地址。</param>
    /// <param name="fullPath">封面文件的绝对路径。</param>
    public bool TryGetCachedCoverForUrl(string key, string url, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!TryGet(key, out var entry) || string.IsNullOrWhiteSpace(entry.CoverFileName))
        {
            return false;
        }

        if (!string.Equals(entry.CoverUrl, url, StringComparison.Ordinal))
        {
            return false;
        }

        var candidate = Path.Combine(_cacheRoot, entry.CoverFileName);
        if (!_fileSystem.FileExists(candidate))
        {
            return false;
        }

        fullPath = candidate;
        return true;
    }

    /// <summary>该条目的列表元数据是否在 TTL 内。</summary>
    /// <param name="entry">条目。</param>
    /// <param name="ttl">新鲜期。</param>
    public bool IsFresh(WorkshopInfoCacheEntry entry, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.FetchedUtc is not { } fetched)
        {
            return false;
        }

        var elapsed = _clock.UtcNow - fetched;
        return elapsed >= TimeSpan.Zero && elapsed < ttl;
    }

    /// <summary>详情正文是否在 TTL 内（且确实抓取过）。</summary>
    /// <param name="entry">条目。</param>
    /// <param name="ttl">新鲜期；null 用 <see cref="DefaultDetailTtl"/>。</param>
    public bool IsDetailFresh(WorkshopInfoCacheEntry entry, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.DetailFetchedUtc is not { } fetched || string.IsNullOrWhiteSpace(entry.DetailText))
        {
            return false;
        }

        var elapsed = _clock.UtcNow - fetched;
        return elapsed >= TimeSpan.Zero && elapsed < (ttl ?? DefaultDetailTtl);
    }

    /// <summary>记录封面下载结果。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="fileName">封面相对缓存根的文件名。</param>
    /// <param name="url">封面来源地址。</param>
    public void SetCover(string key, string fileName, string? url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        _store.SetCover(key, fileName, url, _clock.UtcNow);
    }

    /// <summary>记录详情抓取结果。</summary>
    /// <param name="key">缓存键（<see cref="Key"/>）。</param>
    /// <param name="text">详情正文；null 表示这次没拿到正文。</param>
    /// <param name="screenshots">详情里的截图地址（PDX 有，Steam 通常为空）。</param>
    /// <param name="creatorSteamId">作者 SteamID64（Steam 详情才有）；空白表示这次没拿到，保留旧值。</param>
    /// <param name="tags">标签（Steam 详情才有）；空表示保留旧值。</param>
    /// <param name="views">浏览数（Steam 详情才有）；0 表示保留旧值。</param>
    /// <param name="lifetimeSubscriptions">累计订阅数（Steam 详情才有）；0 表示保留旧值。</param>
    public void SetDetail(
        string key,
        string? text,
        IReadOnlyList<string>? screenshots,
        string? creatorSteamId = null,
        IReadOnlyList<string>? tags = null,
        long views = 0,
        long lifetimeSubscriptions = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        _store.SetDetail(
            key,
            ClampDetail(text),
            screenshots,
            creatorSteamId,
            tags,
            views,
            lifetimeSubscriptions,
            _clock.UtcNow);
    }

    /// <summary>按来源与游戏取条目快照（断网回退用）。</summary>
    /// <param name="source">来源。</param>
    /// <param name="gameId">游戏注册 ID；空白表示不按游戏过滤。</param>
    public IReadOnlyList<WorkshopInfoCacheEntry> Snapshot(WorkshopSource source, string? gameId) =>
        _store.List(source, gameId, MaxEntries);

    /// <summary>清空缓存。删除失败只记 Warn。</summary>
    public void Clear() => _store.Clear();

    /// <summary>兼容旧调用：v2 起每条写入即落库，这里无事可做。</summary>
    public void Flush()
    {
        // 真表存储下没有内存脏页，保留空实现避免调用点大改。
    }

    /// <summary>截断超长正文，且不把代理对切成半个字符。</summary>
    private static string? ClampDetail(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= MaxDetailChars)
        {
            return text;
        }

        int length = MaxDetailChars;
        if (char.IsHighSurrogate(text[length - 1]))
        {
            length--;
        }

        return text[..length];
    }
}
