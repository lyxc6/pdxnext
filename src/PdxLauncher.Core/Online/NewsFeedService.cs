namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Storage;

/// <summary>
/// <see cref="INewsFeedService"/> 实现：官方启动器的 Braze content cards 接口 + 本地 JSON 缓存 + 图片落盘。
/// <para>接口语义：<c>GET https://api.paradox-interactive.com/communication/braze/contentcards/&lt;gameId&gt;</c>，
/// 返回按键名分 slot 的对象：<c>{ "main": { "contentGroups": [...] }, "secondary-1": {...}, "secondary-2": {...} }</c>。</para>
/// <para>JSON 用 <see cref="JsonDocument"/> 手工遍历而不是 DTO：外部结构易变（slot 名带连字符），
/// 反序列化失败会连带丢掉整批卡片。</para>
/// <para>失败容忍（公告只是主页的装饰性内容，必须当成弱依赖）：接口 404 视为「该游戏没有公告」并负缓存 6 小时；
/// 其它失败保留上次内容并 30 分钟内不再重试。公告图域名（braze-images.com）在部分网络下直连不可达，
/// 所以「图没取到」不算本轮成功：不推进 6 小时 TTL，只记图片失败时间并 5 分钟后重试补图；
/// 图确实没下回来的卡是纯图卡（title/text 多数为空），由 UI 判空隐藏。</para>
/// <para>缓存文件：%LOCALAPPDATA%\PdxNext\news-cache\news-cards.json，图片在 news-cache\images\&lt;md5(url)&gt;.*。</para>
/// </summary>
public sealed class NewsFeedService : INewsFeedService
{
    private const int CacheVersion = 1;
    private const string CacheFileName = "news-cards.json";
    private const string ImagesFolderName = "images";
    private const string ContentCardsEndpointFormat = "https://api.paradox-interactive.com/communication/braze/contentcards/{0}";

    /// <summary>公告时效性强于主题（5 天）与 DLC 简介（7 天），只缓 6 小时。</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);

    /// <summary>单张公告图的下载上限。图片域不通时不能逐张干等 HttpClient 的 15 秒连接超时。</summary>
    private static readonly TimeSpan ImageDownloadTimeout = TimeSpan.FromSeconds(8);

    /// <summary>公告图没取到时的重试间隔：远短于 6 小时 TTL，网络恢复后能自动补图。</summary>
    private static readonly TimeSpan ImageRetryBackoff = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFileSystem _fileSystem;
    private readonly IHttpFetcher _http;
    private readonly IAssetDownloader _assets;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly string _cacheRoot;
    private readonly IDocumentFile _cacheDocument;
    private readonly SemaphoreSlim _batchLock = new(1, 1);

    private CacheDocument? _cache;

    /// <summary>创建官方公告服务。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="http">HTTP 抓取器（只应请求 <see cref="OnlineHosts"/> 白名单内的域名）。</param>
    /// <param name="assets">素材下载器（公告图复用主题素材的 .part 原子写与 magic 判型）。</param>
    /// <param name="clock">时钟（缓存过期与失败退避判断）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="cacheRoot">缓存目录（通常是 %LOCALAPPDATA%\PdxNext\news-cache）。</param>
    public NewsFeedService(
        IFileSystem fileSystem,
        IHttpFetcher http,
        IAssetDownloader assets,
        IClock clock,
        IAppLogger logger,
        string cacheRoot,
        IDocumentFile? cacheDocument = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);

        _fileSystem = fileSystem;
        _http = http;
        _assets = assets;
        _clock = clock;
        _logger = logger;
        _cacheRoot = cacheRoot;
        _cacheDocument = cacheDocument ?? new FileDocumentFile(fileSystem, Path.Combine(cacheRoot, CacheFileName));
        ImagesDirectory = Path.Combine(cacheRoot, ImagesFolderName);
    }

    /// <summary>缓存索引的旧文件路径（索引已搬进自有库，图片仍在这里）。</summary>
    public string CachePath => Path.Combine(_cacheRoot, CacheFileName);

    /// <summary>缓存索引落点（<c>db:cache:news-cards</c> 或旧文件），设置页展示用。</summary>
    public string CacheLocation => _cacheDocument.DisplayPath;

    /// <summary>公告图缓存目录。</summary>
    private string ImagesDirectory { get; }

    /// <inheritdoc />
    public void ClearCache()
    {
        _cache = null;
        try
        {
            _cacheDocument.Delete();
            CacheStorage.Clear(_fileSystem, ImagesDirectory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("公告缓存清理失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public async Task<NewsFeed> GetAsync(GameInstallation game, bool allowOnline, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (string.IsNullOrWhiteSpace(game.GameId))
        {
            return NewsFeed.Empty;
        }

        // 串行化整批：主页刷新连点时会并发进来，缓存文件只能有一个写入者。
        await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = LoadCache();
            var now = _clock.UtcNow;
            cache.Entries.TryGetValue(NewsGameId(game), out var entry);
            var cached = BuildFeed(entry);

            if (!allowOnline)
            {
                _logger.Info("官方公告：已关闭联网，不发起请求（gameId=" + game.GameId + "，卡片 "
                    + cached.Items.Count.ToString(CultureInfo.InvariantCulture) + " 张）。");
                return cached;
            }

            if (entry is { } fresh && now - fresh.FetchedUtc < CacheTtl)
            {
                _logger.Info("官方公告：命中本地缓存（gameId=" + game.GameId + "，卡片 "
                    + cached.Items.Count.ToString(CultureInfo.InvariantCulture) + " 张）。");
                return cached;
            }

            if (entry?.LastFailureUtc is { } lastFailure && now - lastFailure < FailureBackoff)
            {
                _logger.Info("官方公告：上次联网失败距今不足 "
                    + ((int)FailureBackoff.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                    + " 分钟，本轮跳过联网（gameId=" + game.GameId + "）。");
                return cached;
            }

            if (entry?.LastImageFailureUtc is { } lastImageFailure && now - lastImageFailure < ImageRetryBackoff)
            {
                _logger.Info("官方公告：公告图上次没取到，距今不足 "
                    + ((int)ImageRetryBackoff.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                    + " 分钟，本轮跳过联网（gameId=" + game.GameId + "）。");
                return cached;
            }

            try
            {
                var fetched = await FetchAsync(game, now, cancellationToken).ConfigureAwait(false);
                if (fetched.ImagesIncomplete)
                {
                    // 图片域不可达：卡片解析本身是成功的，但这些都是纯图卡，没图等于没内容。
                    // 不推进 FetchedUtc（否则 6 小时内不会再试），只记图片失败时间做 5 分钟退避。
                    var partial = new CacheEntry
                    {
                        FetchedUtc = entry?.FetchedUtc ?? default,
                        LastImageFailureUtc = now,
                        Items = fetched.Items,
                    };
                    cache.Entries[NewsGameId(game)] = partial;
                    SaveCache(cache);
                    _logger.Info("官方公告：图片未取到（" + fetched.Items.Count.ToString(CultureInfo.InvariantCulture)
                        + " 张卡里 " + CountMissingImages(fetched.Items).ToString(CultureInfo.InvariantCulture)
                        + " 张无图），" + ((int)ImageRetryBackoff.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                        + " 分钟后重试（gameId=" + game.GameId + "）。");
                    return BuildFeed(partial);
                }

                var updated = new CacheEntry
                {
                    FetchedUtc = now,
                    Items = fetched.Items,
                };
                cache.Entries[NewsGameId(game)] = updated;
                SaveCache(cache);
                return BuildFeed(updated);
            }
            catch (HttpRequestException exception) when (IsNotFound(exception))
            {
                // 404 = 该游戏确实没有投放公告（victoria3 / eu4 等实测如此）。按空结果负缓存，避免反复打接口。
                _logger.Info("官方公告：该游戏没有官方公告（HTTP 404，gameId=" + game.GameId + "）。");
                var empty = new CacheEntry
                {
                    FetchedUtc = now,
                    Items = new List<CacheItem>(),
                };
                cache.Entries[NewsGameId(game)] = empty;
                SaveCache(cache);
                return NewsFeed.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 页面卸载/切换游戏：不是错误，交给调用方。
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                or UnauthorizedAccessException or OperationCanceledException)
            {
                _logger.Warn("官方公告：联网获取失败，沿用上次内容（gameId=" + game.GameId + "）：" + exception.Message);
                cache.Entries[NewsGameId(game)] = new CacheEntry
                {
                    FetchedUtc = entry?.FetchedUtc ?? default,
                    LastFailureUtc = _clock.UtcNow,
                    Items = entry?.Items ?? new List<CacheItem>(),
                };
                SaveCache(cache);
                return cached;
            }
        }
        finally
        {
            _batchLock.Release();
        }
    }

    /// <summary>抓取并解析一门游戏的公告，同时把图片落到缓存目录。</summary>
    /// <returns>解析出的条目，以及「是否因图片域不可达而中途放弃下载」。</returns>
    private async Task<FetchResult> FetchAsync(GameInstallation game, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            string.Format(CultureInfo.InvariantCulture, ContentCardsEndpointFormat, NewsGameId(game)),
            UriKind.Absolute);
        if (!OnlineHosts.IsAllowed(uri))
        {
            _logger.Warn("官方公告：接口域名不在白名单内，已跳过：" + uri.Host);
            return new FetchResult(new List<CacheItem>(), ImagesIncomplete: false);
        }

        string json;
        using (var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }

        var items = ParseItems(json, now);
        var imagesIncomplete = false;
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ImageUrl))
            {
                continue;
            }

            var download = await DownloadImageAsync(item.ImageUrl, cancellationToken).ConfigureAwait(false);
            if (download.Path is { } downloaded)
            {
                item.ImageFileName = _fileSystem.GetFileName(downloaded);
                continue;
            }

            if (!download.TimedOut)
            {
                // 快速失败（404 / 白名单外 / magic 不识别）：只丢这一张图，继续下一张。
                continue;
            }

            // 超时通常意味着整个图片域不可达，剩下的图不再逐张等 8 秒。
            imagesIncomplete = true;
            _logger.Info("官方公告：公告图下载超时，本轮不再尝试剩余图片（gameId=" + game.GameId + "）。");
            break;
        }

        _logger.Info("官方公告：联网获取到 " + items.Count.ToString(CultureInfo.InvariantCulture)
            + " 张卡片（gameId=" + game.GameId + "）。");
        return new FetchResult(items, imagesIncomplete);
    }

    /// <summary>统计「有图片地址但没有本地文件」的条目数（纯诊断日志用）。</summary>
    private static int CountMissingImages(List<CacheItem> items)
    {
        var missing = 0;
        foreach (var item in items)
        {
            if (!string.IsNullOrWhiteSpace(item.ImageUrl) && string.IsNullOrWhiteSpace(item.ImageFileName))
            {
                missing++;
            }
        }

        return missing;
    }

    /// <summary>解析接口 JSON：每个 slot 取一张卡（时间窗内 weight 最大的组的第一条）。</summary>
    private static List<CacheItem> ParseItems(string json, DateTimeOffset now)
    {
        var items = new List<CacheItem>(NewsFeedSlots.Order.Count);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return items;
        }

        foreach (var slot in NewsFeedSlots.Order)
        {
            if (!root.TryGetProperty(slot, out var slotElement) || slotElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!slotElement.TryGetProperty("contentGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            JsonElement? chosen = null;
            var chosenWeight = int.MinValue;
            foreach (var group in groups.EnumerateArray())
            {
                if (!IsGroupVisible(group, now))
                {
                    continue;
                }

                // 同 weight 取先出现的那组：只有严格更大才替换。
                var weight = ReadGroupWeight(group);
                if (weight > chosenWeight)
                {
                    chosenWeight = weight;
                    chosen = group;
                }
            }

            if (chosen is not { } selected)
            {
                continue;
            }

            if (!selected.TryGetProperty("contentItems", out var contentItems) || contentItems.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in contentItems.EnumerateArray())
            {
                // 官方每个 slot 最多 8 条，启动器版式只放一张：取第一条可显示的。
                var item = ReadItem(slot, entry);
                if (item is not null)
                {
                    items.Add(item);
                    break;
                }
            }
        }

        return items;
    }

    private static bool IsGroupVisible(JsonElement group, DateTimeOffset now)
    {
        if (group.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!group.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!settings.TryGetProperty("visible", out var visible) || visible.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (TryReadTimestamp(visible, "from", out var from) && now < from)
        {
            return false;
        }

        if (TryReadTimestamp(visible, "until", out var until) && now > until)
        {
            return false;
        }

        return true;
    }

    private static bool TryReadTimestamp(JsonElement owner, string propertyName, out DateTimeOffset value)
    {
        value = default;
        var text = ReadString(owner, propertyName);
        if (text is null)
        {
            return false;
        }

        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static int ReadGroupWeight(JsonElement group)
    {
        if (group.TryGetProperty("settings", out var settings)
            && settings.ValueKind == JsonValueKind.Object
            && settings.TryGetProperty("weight", out var weight)
            && weight.ValueKind == JsonValueKind.Number
            && weight.TryGetInt32(out var value))
        {
            return value;
        }

        return 1;
    }

    private static CacheItem? ReadItem(string slot, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!entry.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ReadLocalizedText(content, "title") ?? ReadString(entry, "localizedTitle");
        var text = ReadLocalizedText(content, "text");
        var imageUrl = ReadImageUrl(content);
        var link = ReadLink(content);

        if (title is null && text is null && imageUrl is null && link is null)
        {
            // 三个字段全空 = 这张卡没有任何可显示内容，该 slot 视为没有公告。
            return null;
        }

        return new CacheItem
        {
            Slot = slot,
            Title = title,
            Text = text,
            ImageUrl = imageUrl,
            Link = link,
        };
    }

    private static string? ReadLocalizedText(JsonElement content, string propertyName)
    {
        if (!content.TryGetProperty(propertyName, out var map))
        {
            return null;
        }

        if (map.ValueKind == JsonValueKind.String)
        {
            return NullIfEmpty(map.GetString());
        }

        if (map.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (ReadString(map, "en") is { } english)
        {
            return english;
        }

        // 官方接口目前只给 en；这里兜底取任意一种语言，避免将来加语言后卡片直接消失。
        foreach (var pair in map.EnumerateObject())
        {
            if (pair.Value.ValueKind == JsonValueKind.String && NullIfEmpty(pair.Value.GetString()) is { } other)
            {
                return other;
            }
        }

        return null;
    }

    private static string? ReadImageUrl(JsonElement content)
    {
        if (!content.TryGetProperty("image", out var image))
        {
            return null;
        }

        if (image.ValueKind == JsonValueKind.String)
        {
            return NullIfEmpty(image.GetString());
        }

        if (image.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (ReadString(image, "src") is { } source)
        {
            return source;
        }

        if (ReadString(image, "cacheSrc") is { } cachedSource)
        {
            return cachedSource;
        }

        // 兜底：background_image 是 css 的 url(...) 写法。
        return ReadString(image, "backgroundImage") is { } css ? ExtractCssUrl(css) : null;
    }

    private static string? ReadLink(JsonElement content)
    {
        if (!content.TryGetProperty("link", out var link))
        {
            return null;
        }

        if (link.ValueKind == JsonValueKind.String)
        {
            return NullIfEmpty(link.GetString());
        }

        return link.ValueKind == JsonValueKind.Object ? ReadString(link, "href") : null;
    }

    private static string? ReadString(JsonElement owner, string propertyName)
    {
        if (!owner.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return NullIfEmpty(element.GetString());
    }

    private static string? ExtractCssUrl(string css)
    {
        var open = css.IndexOf("url(", StringComparison.OrdinalIgnoreCase);
        if (open < 0)
        {
            return null;
        }

        var start = open + 4;
        var close = css.IndexOf(')', start);
        if (close <= start)
        {
            return null;
        }

        return NullIfEmpty(css.Substring(start, close - start).Trim('"', '\''));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 公告接口与缓存键用的 gameId。CS1 是唯一需要反向归一的一款：
    /// 本启动器内部用正名 <c>cities_skylines</c>，而 Braze 只认官方启动器写的 <c>cities_v2</c>。
    /// </summary>
    private static string NewsGameId(GameInstallation game) => KnownGames.GetNewsFeedGameId(game.GameId);

    /// <summary>下载一张公告图；失败只丢图，不影响卡片。</summary>
    /// <returns>本地路径；以及是否因超时而失败（超时按「图片域不可达」处理）。</returns>
    private async Task<ImageDownload> DownloadImageAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return new ImageDownload(null, TimedOut: false);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !OnlineHosts.IsAllowed(uri))
        {
            _logger.Warn("官方公告图不在白名单内，已跳过：" + url);
            return new ImageDownload(null, TimedOut: false);
        }

        // 文件名用 URL 的 MD5：接口里的 hash 段会随内容变，用整条 URL 才能稳定复用已下载的图。
        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        try
        {
            // 扩展名由 AssetDownloader 按 magic 决定，所以只能按「hash.*」探测已有文件。
            if (_fileSystem.DirectoryExists(ImagesDirectory))
            {
                foreach (var existing in _fileSystem.EnumerateFiles(ImagesDirectory, hash + ".*", recursive: false))
                {
                    if (_fileSystem.FileExists(existing))
                    {
                        return new ImageDownload(existing, TimedOut: false);
                    }
                }
            }

            var targetWithoutExtension = Path.Combine(ImagesDirectory, hash);

            // 单张图限时：图片域不通时不能占着公告的批次锁干等 HttpClient 的连接超时。
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ImageDownloadTimeout);
            string? downloaded;
            try
            {
                downloaded = await _assets.DownloadAsync(uri.AbsoluteUri, targetWithoutExtension, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warn("官方公告图下载超时（" + ((int)ImageDownloadTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture)
                    + " 秒），卡片将不显示图片：" + url);
                return new ImageDownload(null, TimedOut: true);
            }

            if (downloaded is null)
            {
                _logger.Info("官方公告图下载失败，卡片将不显示图片：" + url);
                return new ImageDownload(null, TimedOut: false);
            }

            return new ImageDownload(downloaded, TimedOut: false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("官方公告图处理失败，卡片将不显示图片：" + exception.Message);
            return new ImageDownload(null, TimedOut: false);
        }
    }

    /// <summary>把缓存条目投影成契约对象（图片文件缺失时按「无图」处理）。</summary>
    private NewsFeed BuildFeed(CacheEntry? entry)
    {
        if (entry?.Items is null || entry.Items.Count == 0)
        {
            return NewsFeed.Empty;
        }

        var items = new List<NewsFeedItem>(entry.Items.Count);
        foreach (var cached in entry.Items)
        {
            if (string.IsNullOrWhiteSpace(cached.Slot))
            {
                continue;
            }

            items.Add(new NewsFeedItem(
                cached.Slot,
                cached.Title,
                cached.Text,
                ResolveImagePath(cached.ImageFileName),
                cached.ImageUrl,
                cached.Link));
        }

        return items.Count == 0 ? NewsFeed.Empty : new NewsFeed(items);
    }

    private string? ResolveImagePath(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var path = Path.Combine(ImagesDirectory, fileName);
        return _fileSystem.FileExists(path) ? path : null;
    }

    private static bool IsNotFound(HttpRequestException exception) =>
        exception.Message.StartsWith("HTTP 404", StringComparison.Ordinal);

    private CacheDocument LoadCache()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        try
        {
            var json = _cacheDocument.ReadText();
            if (json is not null)
            {
                var document = JsonSerializer.Deserialize<CacheDocument>(json, JsonOptions);
                if (document is { Version: CacheVersion, Entries: not null })
                {
                    _cache = document;
                    return _cache;
                }

                _logger.Warn("官方公告缓存版本不匹配，已忽略并重建：" + _cacheDocument.DisplayPath);
            }
        }
        catch (JsonException exception)
        {
            _logger.Warn("官方公告缓存解析失败，已忽略并重建：" + exception.Message);
        }
        catch (IOException exception)
        {
            _logger.Warn("官方公告缓存读取失败，已忽略并重建：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("官方公告缓存无权限读取，已忽略并重建：" + exception.Message);
        }

        _cache = new CacheDocument();
        return _cache;
    }

    private void SaveCache(CacheDocument cache)
    {
        try
        {
            var json = JsonSerializer.Serialize(cache, JsonOptions);
            if (!_cacheDocument.WriteText(json))
            {
                _logger.Warn("官方公告缓存写入失败：" + _cacheDocument.DisplayPath);
            }
        }
        catch (IOException exception)
        {
            _logger.Warn("官方公告缓存写入失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            _logger.Warn("官方公告缓存无权限写入：" + exception.Message);
        }
    }

    private sealed class CacheDocument
    {
        public int Version { get; set; } = CacheVersion;

        public Dictionary<string, CacheEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CacheEntry
    {
        public DateTimeOffset FetchedUtc { get; set; }

        public DateTimeOffset? LastFailureUtc { get; set; }

        /// <summary>上次「卡片拿到了、但图片没下载成功」的时间；只做 5 分钟重试退避，不推进 <see cref="FetchedUtc"/>。</summary>
        public DateTimeOffset? LastImageFailureUtc { get; set; }

        public List<CacheItem> Items { get; set; } = new();
    }

    private sealed class CacheItem
    {
        public string Slot { get; set; } = string.Empty;

        public string? Title { get; set; }

        public string? Text { get; set; }

        public string? ImageFileName { get; set; }

        public string? ImageUrl { get; set; }

        public string? Link { get; set; }
    }

    /// <summary>一次抓取的结果：解析出的条目 + 是否因为图片域不可达而放弃了剩余图片。</summary>
    private readonly record struct FetchResult(List<CacheItem> Items, bool ImagesIncomplete);

    /// <summary>单张图的下载结果：本地路径（null = 没取到）+ 是否因超时失败。</summary>
    private readonly record struct ImageDownload(string? Path, bool TimedOut);
}
