namespace PdxLauncher.Core.Online;

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="IDlcDescriptionService"/> 实现：Steam 商店的 appdetails 接口 + 本地 JSON 缓存。
/// <para>接口：<c>GET https://store.steampowered.com/api/appdetails?appids=&lt;steamId&gt;&amp;l=&lt;language&gt;</c>，
/// 返回 <c>{ "&lt;appid&gt;": { "success": true, "data": { "short_description": "..." } } }</c>。
/// 不要加 <c>filters=</c>：Steam 对该接口的 filters 支持不稳定（曾出现 HTTP 200 但 data 整个缺失）。</para>
/// <para>失败容忍（联网补全是弱依赖）：单个 appid 失败不影响其它；<c>success:false</c>（已下架/区域不可用）
/// 按「没有简介」负缓存 1 天；网络失败记录失败时间并 30 分钟内不再重试；首个网络失败即取消本批剩余请求；
/// 响应解析失败不写缓存（下次再试），且不牵连同批其它请求。</para>
/// <para>缓存按商店语言分桶，正缓存天数由调用方传入（设置项 <c>DlcDescriptionCacheDays</c>）。
/// 过期条目会先返回旧文本再后台刷新。</para>
/// </summary>
public sealed class DlcDescriptionService : IDlcDescriptionService
{
    private const int CacheVersion = 2;
    private const int MaxConcurrentRequests = 3;
    private const int MaxDescriptionLength = 400;
    private const string CacheFileName = "dlc-descriptions.json";

    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);
    /// <summary>「确实没有简介」（已下架/区域不可用）的负缓存时长，比正缓存短，重新上架后能自然恢复。</summary>
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IHttpFetcher _http;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _batchLock = new(1, 1);

    private CacheDocument? _cache;

    /// <summary>本次进程内是否刚从 v1 缓存迁移过（迁移后需要回写磁盘）。</summary>
    private bool _migratedCache;

    /// <summary>创建 DLC 简介服务。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="http">HTTP 抓取器（只应请求 <see cref="OnlineHosts"/> 白名单内的域名）。</param>
    /// <param name="clock">时钟（缓存过期判断）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="cacheRoot">缓存目录（通常是 %LOCALAPPDATA%\PdxNext）。</param>
    public DlcDescriptionService(IFileSystem fileSystem, IHttpFetcher http, IClock clock, IAppLogger logger, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        _fileSystem = fileSystem;
        _http = http;
        _clock = clock;
        _logger = logger;
        CachePath = Path.Combine(cacheRoot, CacheFileName);
    }

    /// <inheritdoc />
    public string CachePath { get; }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        IReadOnlyCollection<string> steamIds,
        string language,
        int cacheDays,
        bool allowOnline,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(steamIds);

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var requested = NormalizeIds(steamIds);
        if (requested.Count == 0)
        {
            return result;
        }

        var storeLanguage = SteamStoreLanguage.Normalize(language);
        var ttl = TimeSpan.FromDays(Math.Clamp(cacheDays, 1, 365));

        // 串行化整批查询：避免页面快速切换游戏时多个批次同时写缓存文件。
        await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = LoadCache();
            var now = _clock.UtcNow;
            var entries = cache.GetLanguageEntries(storeLanguage);
            var stale = new List<string>();
            var touched = _migratedCache;

            foreach (var steamId in requested)
            {
                if (entries.TryGetValue(steamId, out var entry))
                {
                    // 过期条目也先给旧文本，用户体验上「有简介总比没有好」；下面再刷新。
                    AddIfNotEmpty(result, steamId, entry.Text);
                    var entryTtl = string.IsNullOrWhiteSpace(entry.Text) ? NegativeTtl : ttl;
                    if (now - entry.FetchedUtc < entryTtl)
                    {
                        continue;
                    }
                }

                stale.Add(steamId);
            }

            if (!allowOnline || stale.Count == 0)
            {
                _logger.Info(
                    "DLC 简介：命中本地缓存 " + result.Count.ToString(CultureInfo.InvariantCulture) + "/"
                    + requested.Count.ToString(CultureInfo.InvariantCulture) + " 个（语言 "
                    + storeLanguage + "）"
                    + (allowOnline ? "，全部未过期。" : "，已关闭联网，不发起请求。"));
                if (touched)
                {
                    SaveCache(cache);
                }

                return result;
            }

            if (cache.LastFailureUtc is { } lastFailure && now - lastFailure < FailureBackoff)
            {
                _logger.Info(
                    "DLC 简介：上次联网失败距今不足 " + ((int)FailureBackoff.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                    + " 分钟，本轮跳过联网（缺失 " + stale.Count.ToString(CultureInfo.InvariantCulture) + " 个）。");
                return result;
            }

            var batch = await FetchAsync(stale, storeLanguage, cancellationToken).ConfigureAwait(false);
            var fetched = 0;
            var missing = 0;
            var failed = 0;
            foreach (var pair in batch.Outcomes)
            {
                if (pair.Value.Kind == FetchOutcomeKind.Failed)
                {
                    // 解析失败/被白名单拦下：本轮不写缓存，下次再试。
                    failed++;
                    continue;
                }

                entries[pair.Key] = new CacheEntry
                {
                    Text = pair.Value.Text,
                    FetchedUtc = now,
                };
                touched = true;

                if (pair.Value.Kind == FetchOutcomeKind.Text)
                {
                    fetched++;
                    AddIfNotEmpty(result, pair.Key, pair.Value.Text);
                }
                else
                {
                    missing++;
                }
            }

            if (batch.NetworkFailed)
            {
                cache.LastFailureUtc = _clock.UtcNow;
                touched = true;
                _logger.Warn("DLC 简介：联网获取失败，已记录失败时间（30 分钟内不再重试）。");
            }
            else if (cache.LastFailureUtc is not null)
            {
                cache.LastFailureUtc = null;
                touched = true;
            }

            if (touched)
            {
                SaveCache(cache);
            }

            _logger.Info(
                "DLC 简介：联网补齐 " + fetched.ToString(CultureInfo.InvariantCulture) + "/"
                + stale.Count.ToString(CultureInfo.InvariantCulture) + " 个（语言 " + storeLanguage
                + "，无简介 " + missing.ToString(CultureInfo.InvariantCulture)
                + "，解析失败 " + failed.ToString(CultureInfo.InvariantCulture)
                + "），现有简介 " + result.Count.ToString(CultureInfo.InvariantCulture) + " 个。");
            return result;
        }
        finally
        {
            _batchLock.Release();
        }
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache = null;
        _migratedCache = false;
        try
        {
            if (_fileSystem.FileExists(CachePath))
            {
                _fileSystem.Delete(CachePath);
            }
        }
        catch (IOException exception)
        {
            _logger.Warn("DLC 简介缓存删除失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("DLC 简介缓存无权限删除：" + exception.Message);
        }
    }

    private static List<string> NormalizeIds(IReadOnlyCollection<string> steamIds)
    {
        var ids = new List<string>(steamIds.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in steamIds)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var id = candidate.Trim();
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    /// <summary>
    /// 并发抓取一批 steamId（最多 <see cref="MaxConcurrentRequests"/> 个并发）。
    /// <para>网络错误会取消本批剩余请求；<c>success:false</c> 记为「无简介」，解析失败记为「失败」。</para>
    /// </summary>
    private async Task<FetchBatch> FetchAsync(
        IReadOnlyList<string> steamIds,
        string language,
        CancellationToken cancellationToken)
    {
        var outcomes = new ConcurrentDictionary<string, FetchOutcome>(StringComparer.Ordinal);
        var networkFailed = 0;

        using var abort = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var slots = new SemaphoreSlim(MaxConcurrentRequests, MaxConcurrentRequests);

        void MarkFailure()
        {
            if (Interlocked.Exchange(ref networkFailed, 1) == 0)
            {
                abort.Cancel();
            }
        }

        var tasks = new List<Task>(steamIds.Count);
        foreach (var steamId in steamIds)
        {
            tasks.Add(FetchOneAsync(steamId, language, slots, outcomes, abort, cancellationToken, MarkFailure));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        var ordered = new Dictionary<string, FetchOutcome>(StringComparer.Ordinal);
        foreach (var steamId in steamIds)
        {
            if (outcomes.TryGetValue(steamId, out var outcome))
            {
                ordered[steamId] = outcome;
            }
        }

        return new FetchBatch(ordered, Volatile.Read(ref networkFailed) != 0);
    }

    private async Task FetchOneAsync(
        string steamId,
        string language,
        SemaphoreSlim slots,
        ConcurrentDictionary<string, FetchOutcome> outcomes,
        CancellationTokenSource abort,
        CancellationToken callerToken,
        Action markFailure)
    {
        var entered = false;
        try
        {
            await slots.WaitAsync(abort.Token).ConfigureAwait(false);
            entered = true;
            outcomes[steamId] = await FetchOneCoreAsync(steamId, language, abort.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            if (abort.IsCancellationRequested)
            {
                // 同批其它请求失败后主动放弃，不是错误。
                return;
            }

            _logger.Warn("DLC 简介请求超时（steamId=" + steamId + "），本轮放弃剩余请求。");
            markFailure();
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException)
        {
            _logger.Warn("DLC 简介获取失败（steamId=" + steamId + "）：" + exception.Message);
            markFailure();
        }
        catch (JsonException exception)
        {
            // 单个 appid 的响应体异常不应牵连同批其它请求，也不写负缓存。
            _logger.Warn("DLC 简介解析失败（steamId=" + steamId + "）：" + exception.Message);
        }
        finally
        {
            if (entered)
            {
                slots.Release();
            }
        }
    }

    private async Task<FetchOutcome> FetchOneCoreAsync(string steamId, string language, CancellationToken cancellationToken)
    {
        var uri = BuildUri(steamId, language);
        if (!OnlineHosts.IsAllowed(uri))
        {
            _logger.Warn("DLC 简介接口不在白名单内，已跳过：" + uri);
            return FetchOutcome.Failed;
        }

        string json;
        using (var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        return TryParseDescription(json, steamId, out var text) ? FetchOutcome.FromText(text) : FetchOutcome.Failed;
    }

    private static Uri BuildUri(string steamId, string language) =>
        new(
            "https://" + OnlineHosts.SteamStoreHost + "/api/appdetails?appids=" + Uri.EscapeDataString(steamId)
            + "&l=" + Uri.EscapeDataString(language),
            UriKind.Absolute);

    /// <summary>
    /// 解析 appdetails 响应。Steam 对已下架/区域不可用的 appid 返回 <c>success:false</c>，此时视为「确实没有简介」（空文本）。
    /// 同时容忍 appid 被包成数字键（JSON 对象键本来就是字符串，这里按原样查找并回退到唯一一项）。
    /// </summary>
    /// <returns>响应结构可用时返回 true（<paramref name="description"/> 可能为空串）；结构不可用返回 false，调用方不写缓存。</returns>
    private static bool TryParseDescription(string json, string steamId, out string description)
    {
        description = string.Empty;

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty(steamId, out var app))
        {
            // Steam 会把 appid 原样作为键，但本地 steam_id 可能有空白/前导零，退化为「只看唯一一项」。
            app = default;
            foreach (var property in root.EnumerateObject())
            {
                app = property.Value;
                break;
            }

            if (app.ValueKind != JsonValueKind.Object)
            {
                return false;
            }
        }

        if (app.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!app.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            // success:false = 已下架/区域不可用，确实没有简介。
            return true;
        }

        if (!app.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        var text = ReadString(data, "short_description");
        if (string.IsNullOrWhiteSpace(text))
        {
            text = ReadString(data, "detailed_description");
        }

        var cleaned = CleanDescription(text);
        if (cleaned.Length > MaxDescriptionLength)
        {
            cleaned = cleaned.Substring(0, MaxDescriptionLength).TrimEnd() + "…";
        }

        description = cleaned;
        return true;
    }

    private static string ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>去掉 Steam 简介里的 HTML 标签与常见实体，并把换行/连续空白压成单个空格。</summary>
    private static string CleanDescription(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '<')
            {
                var close = text.IndexOf('>', index + 1);
                if (close < 0)
                {
                    break;
                }

                builder.Append(' ');
                index = close + 1;
                continue;
            }

            builder.Append(ch);
            index++;
        }

        var flattened = builder
            .Replace("&amp;", "&")
            .Replace("&lt;", "<")
            .Replace("&gt;", ">")
            .Replace("&quot;", "\"")
            .Replace("&#39;", "'")
            .Replace("&apos;", "'")
            .Replace("&nbsp;", " ")
            .ToString();

        var compact = new StringBuilder(flattened.Length);
        var lastWasSpace = true;
        foreach (var ch in flattened)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!lastWasSpace)
                {
                    compact.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            compact.Append(ch);
            lastWasSpace = false;
        }

        return compact.ToString().Trim();
    }

    /// <summary>读取缓存；版本不符或损坏时丢弃重建。v1（无语言分桶）会被就地迁移成简中桶。</summary>
    private CacheDocument LoadCache()
    {
        _migratedCache = false;
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            if (_fileSystem.FileExists(CachePath))
            {
                var json = _fileSystem.ReadAllText(CachePath);
                var document = JsonSerializer.Deserialize<CacheDocument>(json, JsonOptions);
                if (document is not null && document.Version is > 0 and <= CacheVersion)
                {
                    _migratedCache = document.MigrateLegacyEntries();
                    _cache = document;
                    return _cache;
                }

                _logger.Warn("DLC 简介缓存版本不匹配，已忽略并重建：" + CachePath);
            }
        }
        catch (JsonException exception)
        {
            _logger.Warn("DLC 简介缓存解析失败，已忽略并重建：" + exception.Message);
        }
        catch (IOException exception)
        {
            _logger.Warn("DLC 简介缓存读取失败，已忽略并重建：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("DLC 简介缓存无权限读取，已忽略并重建：" + exception.Message);
        }

        _cache = new CacheDocument();
        return _cache;
    }

    private void SaveCache(CacheDocument cache)
    {
        try
        {
            cache.Version = CacheVersion;
            _fileSystem.EnsureDirectory(Path.GetDirectoryName(CachePath) ?? ".");
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            _fileSystem.WriteAllTextAtomic(CachePath, json);
            _migratedCache = false;
        }
        catch (IOException exception)
        {
            _logger.Warn("DLC 简介缓存写入失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("DLC 简介缓存无权限写入：" + exception.Message);
        }
    }

    private static void AddIfNotEmpty(Dictionary<string, string> target, string steamId, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            target[steamId] = text.Trim();
        }
    }

    private sealed record FetchBatch(IReadOnlyDictionary<string, FetchOutcome> Outcomes, bool NetworkFailed);

    private enum FetchOutcomeKind
    {
        /// <summary>拿到了非空简介。</summary>
        Text,
        /// <summary>接口明确表示没有简介（已下架/区域不可用），写负缓存。</summary>
        Missing,
        /// <summary>请求或解析失败，本轮不写缓存。</summary>
        Failed,
    }

    private readonly record struct FetchOutcome(string Text, FetchOutcomeKind Kind)
    {
        public static FetchOutcome Failed => new(string.Empty, FetchOutcomeKind.Failed);

        public static FetchOutcome FromText(string text)
            => string.IsNullOrWhiteSpace(text)
                ? new FetchOutcome(string.Empty, FetchOutcomeKind.Missing)
                : new FetchOutcome(text, FetchOutcomeKind.Text);
    }

    private sealed class CacheDocument
    {
        /// <summary>v1 的扁平 entries；读到后会被迁移进 <see cref="Languages"/> 并置空（序列化时省略）。</summary>
        public Dictionary<string, CacheEntry>? Entries { get; set; }

        public int Version { get; set; } = CacheVersion;

        public DateTimeOffset? LastFailureUtc { get; set; }

        /// <summary>商店语言 → (steamId → 条目)。</summary>
        public Dictionary<string, Dictionary<string, CacheEntry>> Languages { get; set; } = new(StringComparer.Ordinal);

        public Dictionary<string, CacheEntry> GetLanguageEntries(string language)
        {
            if (!Languages.TryGetValue(language, out var entries))
            {
                entries = new Dictionary<string, CacheEntry>(StringComparer.Ordinal);
                Languages[language] = entries;
            }

            return entries;
        }

        /// <returns>是否发生了迁移（需要回写磁盘）。</returns>
        public bool MigrateLegacyEntries()
        {
            if (Entries is null || Entries.Count == 0)
            {
                Entries = null;
                return false;
            }

            var target = GetLanguageEntries(SteamStoreLanguage.SimplifiedChinese);
            foreach (var pair in Entries)
            {
                if (pair.Value is not null && !target.ContainsKey(pair.Key))
                {
                    target[pair.Key] = pair.Value;
                }
            }

            Entries = null;
            return true;
        }
    }

    private sealed class CacheEntry
    {
        public string Text { get; set; } = string.Empty;

        public DateTimeOffset FetchedUtc { get; set; }
    }
}
