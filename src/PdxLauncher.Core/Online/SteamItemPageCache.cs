namespace PdxLauncher.Core.Online;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 创意工坊条目页（HTML）解析结果的持久缓存：索引 <c>workshop-pages.json</c>，按 publishedfileid 存
/// 作者、描述、内嵌配图、画廊截图与首屏评论。
/// <para>
/// 为什么值得单独存：条目页是整个流程里最大的一个响应（1–3 MB HTML），而解析出来的东西只有几十 KB。
/// 每次打开详情都重抓一遍既慢又最容易被 Steam 限流；存下解析结果后，同一个 mod 在 TTL 内重开
/// （包括断网时）不再发请求，详情页立刻可用。
/// </para>
/// <para>
/// 容错策略与 <see cref="WorkshopInfoCache"/> 一致：索引缺失或损坏一律视为无缓存，绝不把异常抛给调用方。
/// </para>
/// </summary>
public sealed class SteamItemPageCache
{
    /// <summary>索引文件名（位于缓存根目录下）。</summary>
    public const string IndexFileName = "workshop-pages.json";

    /// <summary>条目页的默认新鲜期；超过后下次打开详情会重新抓取。</summary>
    public static readonly TimeSpan DefaultPageTtl = TimeSpan.FromHours(12);

    /// <summary>
    /// 最多保留的条目数；超出后按抓取时间淘汰最旧的。
    /// 条目页是整页正文，最坏情况按单条上限算：150 × 128KB ≈ 19MB，
    /// 比原来的 400 × 512KB（最坏 200MB）小一个数量级。
    /// </summary>
    public const int MaxEntries = 150;

    /// <summary>单条序列化后超过这个字节数就不落盘（描述里塞满 base64 图片的极端条目）。</summary>
    public const int MaxEntryBytes = 128 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _cacheRoot;
    private readonly IDocumentFile _indexDocument;
    private readonly object _gate = new();
    private readonly Dictionary<string, CachedSteamItemPage> _entries;

    /// <summary>内存里有未落盘的修改；由 <see cref="Flush"/> 统一写入。</summary>
    private bool _dirty;

    /// <summary>构造并载入索引；索引损坏时按空缓存处理。</summary>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时钟（TTL 判定与新鲜度记账）。</param>
    /// <param name="cacheRoot">缓存根目录（与创意工坊信息缓存同根，清缓存时一起删掉）。</param>
    public SteamItemPageCache(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        string cacheRoot,
        IDocumentFile? indexDocument = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _fileSystem = fileSystem;
        _logger = logger;
        _clock = clock;
        _cacheRoot = cacheRoot;
        _indexDocument = indexDocument ?? new FileDocumentFile(fileSystem, IndexPath);
        _entries = Load(_indexDocument, logger);
    }

    /// <summary>缓存根目录。</summary>
    public string CacheRoot => _cacheRoot;

    /// <summary>索引的旧文件路径（索引已搬进自有库，图片/封面仍在缓存根下）。</summary>
    public string IndexPath => Path.Combine(_cacheRoot, IndexFileName);

    /// <summary>索引落点（库键或旧文件）。</summary>
    public string IndexLocation => _indexDocument.DisplayPath;

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

    /// <summary>取条目页缓存；没有则返回 false。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="entry">命中的缓存条目（副本）。</param>
    public bool TryGet(string publishedFileId, out CachedSteamItemPage entry)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(publishedFileId, out var found))
            {
                entry = Clone(found);
                return true;
            }
        }

        entry = null!;
        return false;
    }

    /// <summary>条目是否还在新鲜期内（在期内直接用，不再发请求）。</summary>
    /// <param name="entry">缓存条目。</param>
    public bool IsFresh(CachedSteamItemPage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return _clock.UtcNow - entry.FetchedUtc <= DefaultPageTtl;
    }

    /// <summary>写入一条条目页解析结果并落盘；单条过大只留在内存、不落盘。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="page">条目页解析结果。</param>
    public void Set(string publishedFileId, SteamWorkshopItemPage page)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);
        ArgumentNullException.ThrowIfNull(page);

        var entry = new CachedSteamItemPage
        {
            FetchedUtc = _clock.UtcNow,
            AuthorName = page.AuthorName,
            AuthorProfileUrl = page.AuthorProfileUrl,
            DescriptionHtml = page.DescriptionHtml,
            ImageUrls = [.. page.ImageUrls],
            ScreenshotUrls = [.. page.ScreenshotUrls],
            TotalComments = page.TotalComments,
            Comments = [.. page.Comments],
        };

        if (EstimateBytes(entry) > MaxEntryBytes)
        {
            _logger.Info("创意工坊条目页过大（超过 " + (MaxEntryBytes / 1024) + " KB），只保留内存副本：" + publishedFileId);
            lock (_gate)
            {
                _entries[publishedFileId] = entry;
                EvictOverflowLocked();
            }

            return;
        }

        lock (_gate)
        {
            _entries[publishedFileId] = entry;
            EvictOverflowLocked();
            _dirty = true;
        }

        Save();
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
            _indexDocument.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("创意工坊条目页缓存清理失败：" + exception.Message);
        }
    }

    /// <summary>把内存里累积的修改一次性写盘。</summary>
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
                Entries = new Dictionary<string, CachedSteamItemPage>(_entries, StringComparer.OrdinalIgnoreCase),
            };
        }

        try
        {
            if (!_indexDocument.WriteText(JsonSerializer.Serialize(snapshot, Options)))
            {
                _logger.Warn("创意工坊条目页缓存索引写入失败：" + _indexDocument.DisplayPath);
            }
        }
        catch (Exception exception) when (exception is IOException
                                              or UnauthorizedAccessException
                                              or ArgumentException
                                              or NotSupportedException
                                              or PathTooLongException)
        {
            _logger.Warn("创意工坊条目页缓存索引写入失败：" + exception.Message);
        }
    }

    /// <summary>把缓存条目还原成解析结果（纯函数，供详情页直接使用）。</summary>
    /// <param name="entry">缓存条目。</param>
    public static SteamWorkshopItemPage Restore(CachedSteamItemPage entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new SteamWorkshopItemPage(
            entry.AuthorName,
            entry.AuthorProfileUrl,
            entry.DescriptionHtml,
            entry.ImageUrls,
            entry.ScreenshotUrls,
            entry.TotalComments,
            entry.Comments);
    }

    private static int EstimateBytes(CachedSteamItemPage entry) =>
        JsonSerializer.SerializeToUtf8Bytes(entry, Options).Length;

    /// <summary>超出上限时按抓取时间淘汰最旧的条目。</summary>
    private void EvictOverflowLocked()
    {
        if (_entries.Count <= MaxEntries)
        {
            return;
        }

        var overflow = _entries.Count - MaxEntries;
        foreach (var key in _entries
                     .OrderBy(pair => pair.Value.FetchedUtc)
                     .Take(overflow)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _entries.Remove(key);
        }
    }

    private static CachedSteamItemPage Clone(CachedSteamItemPage source) => new()
    {
        FetchedUtc = source.FetchedUtc,
        AuthorName = source.AuthorName,
        AuthorProfileUrl = source.AuthorProfileUrl,
        DescriptionHtml = source.DescriptionHtml,
        ImageUrls = [.. source.ImageUrls],
        ScreenshotUrls = [.. source.ScreenshotUrls],
        TotalComments = source.TotalComments,
        Comments = [.. source.Comments],
    };

    private static Dictionary<string, CachedSteamItemPage> Load(
        IDocumentFile document,
        IAppLogger logger)
    {
        var entries = new Dictionary<string, CachedSteamItemPage>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = document.ReadText();
            if (json is null)
            {
                return entries;
            }

            var file = JsonSerializer.Deserialize<IndexFile>(json, Options);
            if (file?.Entries is null)
            {
                return entries;
            }

            foreach (var (key, value) in file.Entries)
            {
                if (value is null || string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                Normalize(value);
                entries[key] = value;
            }
        }
        catch (Exception exception) when (exception is JsonException
                                              or IOException
                                              or UnauthorizedAccessException
                                              or NotSupportedException)
        {
            logger.Warn("创意工坊条目页缓存索引读取失败，按空缓存处理：" + exception.Message);
        }

        return entries;
    }

    /// <summary>旧版本/手工改过的索引里可能有 null 数组；补齐成空集合，避免界面绑定炸掉。</summary>
    private static void Normalize(CachedSteamItemPage entry)
    {
        entry.ImageUrls ??= [];
        entry.ScreenshotUrls ??= [];
        entry.Comments ??= [];
    }

    /// <summary>磁盘上的索引形状。</summary>
    private sealed class IndexFile
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, CachedSteamItemPage>? Entries { get; set; }
    }
}

/// <summary>一条条目页缓存：解析结果的序列化形式（字段名即 JSON 名）。</summary>
public sealed class CachedSteamItemPage
{
    /// <summary>抓取时间（UTC），用于 TTL 判定与容量淘汰。</summary>
    public DateTimeOffset FetchedUtc { get; set; }

    /// <summary>作者显示名。</summary>
    public string? AuthorName { get; set; }

    /// <summary>作者主页。</summary>
    public string? AuthorProfileUrl { get; set; }

    /// <summary>描述原文（HTML）。</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>描述里内嵌的图片。</summary>
    public List<string> ImageUrls { get; set; } = [];

    /// <summary>官方画廊截图。</summary>
    public List<string> ScreenshotUrls { get; set; } = [];

    /// <summary>讨论总数。</summary>
    public int TotalComments { get; set; }

    /// <summary>首屏评论。</summary>
    public List<SteamWorkshopComment> Comments { get; set; } = [];
}
