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
/// </summary>
public sealed class TranslationCache
{
    /// <summary>
    /// 缓存条目上限；超出后淘汰最旧的条目。
    /// 5000 条对译文缓存没有实际收益（真实库只有几百条），却让整份序列化变大，
    /// 所以收到 2000——仍然远超日常使用量，最坏内存占用却小一半以上。
    /// </summary>
    public const int MaxEntries = 2000;

    /// <summary>缓存文档结构版本；结构变了就整份作废（老结构解析不出来自然回落）。</summary>
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

    /// <summary>缓存文档结构。</summary>
    private sealed class CacheDocument
    {
        public int Version { get; set; } = CacheVersion;

        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>一条缓存。</summary>
    private sealed class CacheEntry
    {
        public string Text { get; set; } = string.Empty;

        public DateTimeOffset FetchedUtc { get; set; }
    }
}
