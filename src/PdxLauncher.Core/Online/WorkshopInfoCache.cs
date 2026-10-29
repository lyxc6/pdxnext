namespace PdxLauncher.Core.Online;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 创意工坊 mod 信息缓存：索引 <c>workshop-info.json</c>，按「来源 + id」存列表元数据、封面文件名与详情正文。
/// <para>
/// 目的：同一个 mod 在翻页、切游戏、重新进页面时不再重复抓取 —— 名称/作者/评分立刻可用，
/// 封面文件命中后不再下载，详情在 TTL 内直接复用。索引与主题缓存同一套容错策略：
/// 缺失或损坏一律视为无缓存，绝不把异常抛给调用方。
/// </para>
/// </summary>
public sealed class WorkshopInfoCache
{
    /// <summary>索引文件名（位于缓存根目录下）。</summary>
    public const string IndexFileName = "workshop-info.json";

    /// <summary>详情正文的默认新鲜期；超过后下次打开详情会重新抓取。</summary>
    public static readonly TimeSpan DefaultDetailTtl = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _cacheRoot;
    private readonly object _gate = new();
    private readonly Dictionary<string, WorkshopInfoCacheEntry> _entries;

    /// <summary>内存里有未落盘的修改；由 <see cref="Flush"/> 统一写入。</summary>
    private bool _dirty;

    public WorkshopInfoCache(IFileSystem fileSystem, IAppLogger logger, IClock clock, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _cacheRoot = cacheRoot;
        _entries = Load(fileSystem, logger, IndexPath);
    }

    /// <summary>缓存根目录。</summary>
    public string CacheRoot => _cacheRoot;

    /// <summary>索引文件绝对路径。</summary>
    public string IndexPath => Path.Combine(_cacheRoot, IndexFileName);

    /// <summary>
    /// 封面根目录：与索引同根，清缓存时一起删掉。
    /// 条目里的 <see cref="WorkshopInfoCacheEntry.CoverFileName"/> 是相对这里的路径（含 steam/ 或 pdx/ 子目录）。
    /// </summary>
    public string CoverRoot => _cacheRoot;

    /// <summary>索引里的条目数。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>缓存键：<c>steam:{publishedfileid}</c> 或 <c>pdx:{modId}</c>。</summary>
    public static string Key(WorkshopSource source, string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return SourceTag(source) + ":" + id;
    }

    /// <summary>来源在缓存键与封面目录里使用的小写标记。</summary>
    public static string SourceTag(WorkshopSource source) => source == WorkshopSource.Steam ? "steam" : "pdx";

    /// <summary>
    /// 写入或合并一条列表元数据。传 null 的字段保留旧值，
    /// 这样列表刷新不会把已抓到的封面文件名与详情正文抹掉。
    /// </summary>
    public void Upsert(WorkshopInfoCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        var source = string.Equals(entry.Source, "steam", StringComparison.OrdinalIgnoreCase)
            ? WorkshopSource.Steam
            : WorkshopSource.Pdx;
        var key = Key(source, entry.Id);

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var target))
            {
                target = new WorkshopInfoCacheEntry();
                _entries[key] = target;
            }

            target.Source = SourceTag(source);
            target.Id = entry.Id;
            target.GameId = entry.GameId ?? target.GameId;
            target.Name = entry.Name ?? target.Name;
            target.Author = entry.Author ?? target.Author;
            target.Rating = entry.Rating ?? target.Rating;
            target.RatingsTotal = entry.RatingsTotal ?? target.RatingsTotal;
            target.Subscriptions = entry.Subscriptions ?? target.Subscriptions;
            target.UpdatedUtc = entry.UpdatedUtc ?? target.UpdatedUtc;
            target.SizeBytes = entry.SizeBytes ?? target.SizeBytes;

            // 封面 URL 变了说明作者换了图：旧文件名留着，但下次下载会覆盖同一槽位。
            if (!string.IsNullOrWhiteSpace(entry.CoverUrl) &&
                !string.Equals(entry.CoverUrl, target.CoverUrl, StringComparison.Ordinal))
            {
                target.CoverUrl = entry.CoverUrl;
            }

            target.FetchedUtc = _clock.UtcNow;
            _dirty = true;
        }
    }

    /// <summary>取一条快照；没有返回 false。返回的是副本，调用方改动不会影响索引。</summary>
    public bool TryGet(string key, out WorkshopInfoCacheEntry entry)
    {
        entry = new WorkshopInfoCacheEntry();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var found))
            {
                return false;
            }

            entry = Clone(found);
            return true;
        }
    }

    /// <summary>封面文件是否还在缓存目录里；在就返回绝对路径，调用方不必再下载。</summary>
    public bool TryGetCachedCover(string key, out string fullPath)
    {
        fullPath = string.Empty;
        if (!TryGet(key, out var entry) || string.IsNullOrWhiteSpace(entry.CoverFileName))
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
    public void SetCover(string key, string fileName, string? url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var target))
            {
                target = new WorkshopInfoCacheEntry();
                _entries[key] = target;
            }

            target.CoverFileName = fileName;
            target.CoverFetchedUtc = _clock.UtcNow;
            if (!string.IsNullOrWhiteSpace(url))
            {
                target.CoverUrl = url;
            }

            _dirty = true;
        }
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

        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var target))
            {
                target = new WorkshopInfoCacheEntry();
                _entries[key] = target;
            }

            target.DetailText = text;
            target.DetailScreenshots = screenshots is { Count: > 0 } ? [.. screenshots] : null;

            // 追加的元数据只在这次真拿到时才覆盖：PDX 详情没有这些字段，
            // 传默认值会把 Steam 那条已经存下的作者/标签洗掉（同一 key 不会跨来源，但保持「缺就不写」更稳）。
            if (!string.IsNullOrWhiteSpace(creatorSteamId))
            {
                target.CreatorSteamId = creatorSteamId;
            }

            if (tags is { Count: > 0 })
            {
                target.Tags = [.. tags];
            }

            if (views > 0)
            {
                target.Views = views;
            }

            if (lifetimeSubscriptions > 0)
            {
                target.LifetimeSubscriptions = lifetimeSubscriptions;
            }

            target.DetailFetchedUtc = _clock.UtcNow;
        }

        Save();
    }

    /// <summary>按来源与游戏取条目快照（断网回退用）。</summary>
    public IReadOnlyList<WorkshopInfoCacheEntry> Snapshot(WorkshopSource source, string? gameId)
    {
        var tag = SourceTag(source);
        lock (_gate)
        {
            return _entries.Values
                .Where(entry => string.Equals(entry.Source, tag, StringComparison.OrdinalIgnoreCase))
                .Where(entry => string.IsNullOrWhiteSpace(gameId) ||
                                string.Equals(entry.GameId, gameId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(entry => entry.UpdatedUtc)
                .Select(Clone)
                .ToList();
        }
    }

    /// <summary>清空缓存：删索引文件并清空内存索引。删除失败只记 Warn。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _dirty = false;
        }

        try
        {
            if (_fileSystem.FileExists(IndexPath))
            {
                _fileSystem.Delete(IndexPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("创意工坊缓存索引清理失败：" + exception.Message);
        }
    }

    /// <summary>
    /// 把内存里累积的修改一次性写盘（列表 30 条与封面 30 张各只写一次，避免逐条同步 IO 卡 UI）。
    /// </summary>
    public void Flush()
    {
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            _dirty = false;
        }

        Save();
    }

    /// <summary>原子写索引；写入失败只记日志。</summary>
    public void Save()
    {
        IndexFile snapshot;
        lock (_gate)
        {
            snapshot = new IndexFile
            {
                Entries = new Dictionary<string, WorkshopInfoCacheEntry>(_entries, StringComparer.OrdinalIgnoreCase),
            };
        }

        try
        {
            _fileSystem.EnsureDirectory(_cacheRoot);
            _fileSystem.WriteAllTextAtomic(IndexPath, JsonSerializer.Serialize(snapshot, Options));
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
        {
            _logger.Warn("创意工坊缓存索引写入失败：" + exception.Message);
        }
    }

    private static WorkshopInfoCacheEntry Clone(WorkshopInfoCacheEntry source) => new()
    {
        Source = source.Source,
        Id = source.Id,
        GameId = source.GameId,
        Name = source.Name,
        Author = source.Author,
        Rating = source.Rating,
        RatingsTotal = source.RatingsTotal,
        Subscriptions = source.Subscriptions,
        UpdatedUtc = source.UpdatedUtc,
        SizeBytes = source.SizeBytes,
        CoverUrl = source.CoverUrl,
        CoverFileName = source.CoverFileName,
        CoverFetchedUtc = source.CoverFetchedUtc,
        DetailText = source.DetailText,
        DetailScreenshots = source.DetailScreenshots is null ? null : [.. source.DetailScreenshots],
        DetailFetchedUtc = source.DetailFetchedUtc,
        FetchedUtc = source.FetchedUtc,
        CreatorSteamId = source.CreatorSteamId,
        Tags = source.Tags is null ? null : [.. source.Tags],
        Views = source.Views,
        LifetimeSubscriptions = source.LifetimeSubscriptions,
    };

    private static Dictionary<string, WorkshopInfoCacheEntry> Load(
        IFileSystem fileSystem,
        IAppLogger logger,
        string path)
    {
        var entries = new Dictionary<string, WorkshopInfoCacheEntry>(StringComparer.OrdinalIgnoreCase);
        if (!fileSystem.FileExists(path))
        {
            return entries;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<IndexFile>(fileSystem.ReadAllText(path), Options);
            if (loaded?.Entries is null)
            {
                return entries;
            }

            foreach (var pair in loaded.Entries)
            {
                if (pair.Value is not null && !string.IsNullOrWhiteSpace(pair.Key))
                {
                    entries[pair.Key] = pair.Value;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException
                                              or IOException
                                              or UnauthorizedAccessException)
        {
            logger.Warn("创意工坊缓存索引损坏，视为无缓存：" + exception.Message);
        }

        return entries;
    }

    private sealed class IndexFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, WorkshopInfoCacheEntry>? Entries { get; set; }
    }
}
