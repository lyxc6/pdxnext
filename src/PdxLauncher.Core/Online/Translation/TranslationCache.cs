namespace PdxLauncher.Core.Online.Translation;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 译文缓存：按「引擎 + 目标语言 + 原文」存译文，命中就不再请求引擎。
/// <para>
/// 落点是一份 JSON 文档（键 <see cref="JsonDocumentKeys.Translations"/>），
/// 条目上限 <see cref="MaxEntries"/>，超出后按写入时间淘汰最旧的。
/// 写盘失败只记日志，绝不把异常抛给 UI——翻译是弱依赖，缓存坏了顶多多花一次请求。
/// </para>
/// <para>
/// 另有一份名称译文（<see cref="NameTranslation"/>）按「mod 主键 + 目标语言」单独存放：
/// 名称跟引擎与缓存天数无关，换引擎/模型或重进 Mod 库时仍能直接显示旧译名。
/// </para>
/// </summary>
public sealed class TranslationCache
{
    /// <summary>
    /// 缓存条目上限；超出后淘汰最旧的条目。
    /// 5000 条对译文缓存没有实际收益（真实库只有几百条），却让整份序列化变大，
    /// 所以收到 2000——仍然远超日常使用量，最坏内存占用却小一半以上。
    /// </summary>
    public const int MaxEntries = 2000;

    /// <summary>名称译文条目上限；名称与引擎/TTL 解耦，独立计数并按更新时间淘汰最旧的。</summary>
    public const int MaxNameEntries = 4000;

    /// <summary>
    /// 缓存文档结构版本；破坏性结构变化才整份作废（老结构解析不出来自然回落）。
    /// 新增可选字段（例如名称译名 Names）保持版本不变，老文档照常可读。
    /// </summary>
    public const int CacheVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IJsonDocumentStore _documents;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CacheDocument? _document;
    private bool _loaded;

    /// <summary>创建缓存。</summary>
    /// <param name="documents">文档存储。</param>
    /// <param name="clock">时钟（过期判断）。</param>
    /// <param name="logger">日志。</param>
    public TranslationCache(IJsonDocumentStore documents, IClock clock, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _documents = documents;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>缓存文档的键。</summary>
    public static string DocumentKey => JsonDocumentKeys.Translations;

    /// <summary>
    /// 取缓存里的译文；没有或已过期返回 null。
    /// </summary>
    /// <param name="engineId">引擎标识。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="text">原文。</param>
    /// <param name="cacheDays">缓存保留天数。</param>
    public async Task<string?> TryGetAsync(
        string engineId,
        string targetLanguage,
        string text,
        int cacheDays,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var key = BuildKey(engineId, targetLanguage, text);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = EnsureLoaded();
            if (!document.Entries.TryGetValue(key, out var entry) || entry is null)
            {
                return null;
            }

            var ttl = TimeSpan.FromDays(Math.Clamp(cacheDays, 1, 365));
            if (_clock.UtcNow - entry.FetchedUtc > ttl)
            {
                return null;
            }

            return entry.Text;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 写入一条译文；写盘失败只记日志。
    /// </summary>
    /// <param name="engineId">引擎标识。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="text">原文。</param>
    /// <param name="translation">译文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SetAsync(
        string engineId,
        string targetLanguage,
        string text,
        string translation,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(translation))
        {
            return;
        }

        var key = BuildKey(engineId, targetLanguage, text);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = EnsureLoaded();
            document.Entries[key] = new CacheEntry
            {
                Text = translation,
                FetchedUtc = _clock.UtcNow,
            };

            if (document.Entries.Count > MaxEntries)
            {
                EvictOldest(document);
            }

            Persist(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 读出指定目标语言的全部名称译文（按 mod 主键索引）。
    /// 文档读不出来时返回空表，调用方按「没有旧译名」继续正常翻译。
    /// </summary>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<IReadOnlyDictionary<Guid, NameTranslation>> GetNameTranslationsAsync(
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, NameTranslation>();
        if (string.IsNullOrEmpty(targetLanguage))
        {
            return result;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = EnsureLoaded();
            var names = document.Names!;
            foreach (var pair in names)
            {
                var entry = pair.Value;
                if (entry is null
                    || string.IsNullOrWhiteSpace(entry.SourceName)
                    || string.IsNullOrWhiteSpace(entry.Translation)
                    || !string.Equals(entry.TargetLanguage, targetLanguage, StringComparison.Ordinal))
                {
                    continue;
                }

                result[entry.ModId] = new NameTranslation(
                    entry.ModId,
                    entry.SourceName,
                    entry.TargetLanguage,
                    entry.Translation);
            }
        }
        finally
        {
            _gate.Release();
        }

        return result;
    }

    /// <summary>
    /// 批量写入名称译文；与普通译文缓存共用同一份文档，写盘失败只记日志。
    /// 原文名称或译文为空、以及目标语言为空时跳过，不影响其它条目。
    /// </summary>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="translations">名称译文列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task SetNameTranslationsAsync(
        string targetLanguage,
        IReadOnlyList<NameTranslation> translations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(translations);
        if (string.IsNullOrEmpty(targetLanguage) || translations.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var document = EnsureLoaded();
            var names = document.Names!;
            var changed = false;
            foreach (var translation in translations)
            {
                if (string.IsNullOrWhiteSpace(translation.SourceName)
                    || string.IsNullOrWhiteSpace(translation.Translation))
                {
                    continue;
                }

                names[BuildNameKey(translation.ModId, targetLanguage)] = new NameCacheEntry
                {
                    ModId = translation.ModId,
                    TargetLanguage = targetLanguage,
                    SourceName = translation.SourceName,
                    Translation = translation.Translation,
                    UpdatedUtc = _clock.UtcNow,
                };
                changed = true;
            }

            if (!changed)
            {
                return;
            }

            if (names.Count > MaxNameEntries)
            {
                EvictOldestNames(document);
            }

            Persist(document);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>清空缓存（设置页的「清理缓存」用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _document = new CacheDocument();
            _loaded = true;
            _documents.Delete(DocumentKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 同步清空缓存（设置页的「清理缓存」按钮用：那条路径整体跑在后台线程里）。
    /// </summary>
    public void Clear()
    {
        _gate.Wait();
        try
        {
            _document = new CacheDocument();
            _loaded = true;
            _documents.Delete(DocumentKey);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>缓存键：引擎 + 目标语言 + 原文的哈希。</summary>
    /// <param name="engineId">引擎标识。</param>
    /// <param name="targetLanguage">目标语言。</param>
    /// <param name="text">原文。</param>
    public static string BuildKey(string engineId, string targetLanguage, string text)
    {
        var payload = string.Concat(engineId, "\n", targetLanguage, "\n", text);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    /// <summary>
    /// 名称译文的键：mod 主键 + 目标语言。
    /// 不含引擎与原文，名称变化由条目里的 <see cref="NameCacheEntry.SourceName"/> 校验。
    /// </summary>
    private static string BuildNameKey(Guid modId, string targetLanguage)
        => modId.ToString("N") + "\n" + targetLanguage;

    /// <summary>读出内存里的缓存文档；没读过就读盘，读坏了就当空。</summary>
    private CacheDocument EnsureLoaded()
    {
        if (_loaded)
        {
            return _document!;
        }

        _loaded = true;
        if (_documents.TryRead(DocumentKey, out var json) && !string.IsNullOrWhiteSpace(json))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<CacheDocument>(json, JsonOptions);
                if (parsed is not null && parsed.Version == CacheVersion)
                {
                    parsed.Entries ??= new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
                    parsed.Names ??= new Dictionary<string, NameCacheEntry>(StringComparer.Ordinal);
                    _document = parsed;
                    return parsed;
                }
            }
            catch (JsonException)
            {
                _logger.Warn("译文缓存结构无法解析，已重新开始积累");
            }
        }

        _document = new CacheDocument();
        return _document;
    }

    /// <summary>条目超上限时按写入时间淘汰最旧的一批。</summary>
    private static void EvictOldest(CacheDocument document)
    {
        var excess = document.Entries.Count - MaxEntries;
        if (excess <= 0)
        {
            return;
        }

        var oldest = document.Entries
            .OrderBy(pair => pair.Value?.FetchedUtc ?? DateTimeOffset.MinValue)
            .Take(excess)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var key in oldest)
        {
            document.Entries.Remove(key);
        }
    }

    /// <summary>名称条目超上限时按更新时间淘汰最旧的一批。</summary>
    private static void EvictOldestNames(CacheDocument document)
    {
        var names = document.Names!;
        var excess = names.Count - MaxNameEntries;
        if (excess <= 0)
        {
            return;
        }

        var oldest = names
            .OrderBy(pair => pair.Value.UpdatedUtc)
            .Take(excess)
            .Select(pair => pair.Key)
            .ToList();
        foreach (var key in oldest)
        {
            names.Remove(key);
        }
    }

    /// <summary>落盘；失败只记日志。</summary>
    private void Persist(CacheDocument document)
    {
        try
        {
            var json = JsonSerializer.Serialize(document, JsonOptions);
            if (!_documents.Write(DocumentKey, json))
            {
                _logger.Warn("译文缓存写入失败");
            }
        }
        catch (JsonException ex)
        {
            _logger.Warn("译文缓存序列化失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 一条名称译文：按 mod 主键 + 目标语言存放，与引擎和缓存天数无关。
    /// 名称换了（<see cref="SourceName"/> 对不上）就不再复用，避免把旧名字的译文贴到新名字上。
    /// </summary>
    /// <param name="ModId">mod 主键。</param>
    /// <param name="SourceName">翻出这条译文时的原文名称。</param>
    /// <param name="TargetLanguage">目标语言。</param>
    /// <param name="Translation">译文。</param>
    public sealed record NameTranslation(
        Guid ModId,
        string SourceName,
        string TargetLanguage,
        string Translation);

    /// <summary>缓存文档结构。</summary>
    private sealed class CacheDocument
    {
        public int Version { get; set; } = CacheVersion;

        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);

        /// <summary>名称译文（mod 主键 + 目标语言）；与 Entries 的引擎/TTL 语义互不影响。</summary>
        public Dictionary<string, NameCacheEntry>? Names { get; set; } =
            new(StringComparer.Ordinal);
    }

    /// <summary>一条缓存。</summary>
    private sealed class CacheEntry
    {
        public string Text { get; set; } = string.Empty;

        public DateTimeOffset FetchedUtc { get; set; }
    }

    /// <summary>一条持久化的名称译文。</summary>
    private sealed class NameCacheEntry
    {
        /// <summary>反序列化用；显式 public 让 System.Text.Json 能构造私有嵌套类型。</summary>
        public NameCacheEntry()
        {
        }

        public Guid ModId { get; set; }

        public string TargetLanguage { get; set; } = string.Empty;

        public string SourceName { get; set; } = string.Empty;

        public string Translation { get; set; } = string.Empty;

        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
