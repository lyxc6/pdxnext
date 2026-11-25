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
/// <see cref="INewsFeedService"/> 实现：官方启动器的 Braze content cards 接口 + 本地缓存 + 图片落盘。
/// <para>兼容两代数据：旧 slot/contentGroups/contentItems/content 结构按固定权重取第一条；
/// 新扁平 Braze 卡片按 <c>extras.game</c> / 平台 / section / DLC / 启动次数过滤后按权重随机选组。</para>
/// <para>缓存键含平台，TTL 15 分钟；失败退避 30 分钟；图片失败 5 分钟后补图，手动强刷可越过 TTL 与退避。</para>
/// </summary>
public sealed class NewsFeedService : INewsFeedService
{
    private const int CacheVersion = 3;
    private const string CacheFileName = "news-cards.json";
    private const string ImagesFolderName = "images";
    private const string ContentCardsEndpointFormat =
        "https://api.paradox-interactive.com/communication/braze/contentcards/{0}?distributionPlatform={1}";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ImageDownloadTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ImageRetryBackoff = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
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
    private readonly Func<double> _random;
    private readonly SemaphoreSlim _batchLock = new(1, 1);

    private CacheDocument? _cache;

    public NewsFeedService(
        IFileSystem fileSystem,
        IHttpFetcher http,
        IAssetDownloader assets,
        IClock clock,
        IAppLogger logger,
        string cacheRoot,
        IDocumentFile? cacheDocument = null,
        Func<double>? random = null)
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
        _random = random ?? Random.Shared.NextDouble;
        ImagesDirectory = Path.Combine(cacheRoot, ImagesFolderName);
    }

    public string CachePath => Path.Combine(_cacheRoot, CacheFileName);

    public string CacheLocation => _cacheDocument.DisplayPath;

    private string ImagesDirectory { get; }

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
    public Task<NewsFeed> GetAsync(GameInstallation game, bool allowOnline, CancellationToken cancellationToken) =>
        GetAsync(NewsFeedRequest.FromGame(game, allowOnline), cancellationToken);

    /// <summary>按完整上下文读取公告；强制刷新越过 TTL 与失败退避，但仍复用已下载图片。</summary>
    public async Task<NewsFeed> GetAsync(NewsFeedRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var game = request.Game;
        if (string.IsNullOrWhiteSpace(game.GameId))
        {
            return NewsFeed.Empty;
        }

        await _batchLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cache = LoadCache();
            var now = _clock.UtcNow;
            var cacheKey = CacheKey(game, request.DistributionPlatform);
            cache.Entries.TryGetValue(cacheKey, out var entry);
            var cached = BuildFeed(entry);

            if (!request.AllowOnline)
            {
                _logger.Info("官方公告：已关闭联网，不发起请求（gameId=" + game.GameId + "，平台="
                    + request.DistributionPlatform + "，卡片 " + cached.Items.Count.ToString(CultureInfo.InvariantCulture) + " 张）。");
                return cached;
            }

            if (!request.ForceRefresh)
            {
                if (entry is { } fresh && now - fresh.FetchedUtc < CacheTtl)
                {
                    _logger.Info("官方公告：命中本地缓存（gameId=" + game.GameId + "，平台="
                        + request.DistributionPlatform + "，卡片 " + cached.Items.Count.ToString(CultureInfo.InvariantCulture) + " 张）。");
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
            }

            try
            {
                var fetched = await FetchAsync(game, request, now, cancellationToken).ConfigureAwait(false);
                if (fetched.ImagesIncomplete && !request.ForceRefresh)
                {
                    cache.Entries[cacheKey] = new CacheEntry
                    {
                        FetchedUtc = entry?.FetchedUtc ?? default,
                        LastImageFailureUtc = now,
                        Items = fetched.Items,
                    };
                    SaveCache(cache);
                    _logger.Info("官方公告：图片未取到，"
                        + ((int)ImageRetryBackoff.TotalMinutes).ToString(CultureInfo.InvariantCulture)
                        + " 分钟后重试（gameId=" + game.GameId + "）。");
                    return BuildFeed(cache.Entries[cacheKey]);
                }

                cache.Entries[cacheKey] = new CacheEntry { FetchedUtc = now, Items = fetched.Items };
                SaveCache(cache);
                return BuildFeed(cache.Entries[cacheKey]);
            }
            catch (HttpRequestException exception) when (IsNotFound(exception))
            {
                _logger.Info("官方公告：该游戏没有官方公告（HTTP 404，gameId=" + game.GameId + "）。");
                cache.Entries[cacheKey] = new CacheEntry { FetchedUtc = now, Items = new List<CacheItem>() };
                SaveCache(cache);
                return NewsFeed.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException or JsonException
                or UnauthorizedAccessException or OperationCanceledException)
            {
                _logger.Warn("官方公告：联网获取失败，沿用上次内容（gameId=" + game.GameId + "）：" + exception.Message);
                cache.Entries[cacheKey] = new CacheEntry
                {
                    FetchedUtc = entry?.FetchedUtc ?? default,
                    LastFailureUtc = now,
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

    private async Task<FetchResult> FetchAsync(
        GameInstallation game,
        NewsFeedRequest request,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(string.Format(
            CultureInfo.InvariantCulture,
            ContentCardsEndpointFormat,
            NewsGameId(game),
            Uri.EscapeDataString(string.IsNullOrWhiteSpace(request.DistributionPlatform) ? "pdx" : request.DistributionPlatform)),
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

        var items = ParseItems(json, request, now, _random);
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
                continue;
            }

            imagesIncomplete = true;
            _logger.Info("官方公告：公告图下载超时，本轮不再尝试剩余图片（gameId=" + game.GameId + "）。");
            break;
        }

        _logger.Info("官方公告：联网获取到 " + items.Count.ToString(CultureInfo.InvariantCulture)
            + " 张卡片（gameId=" + game.GameId + "，平台=" + request.DistributionPlatform + "）。");
        return new FetchResult(items, imagesIncomplete);
    }

    /// <summary>自动识别新旧 JSON 格式；每个 slot 输出全部可见卡片（单卡时即一张）。</summary>
    private static List<CacheItem> ParseItems(string json, NewsFeedRequest request, DateTimeOffset now, Func<double> random)
    {
        var items = new List<CacheItem>();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return items;
        }

        var modern = NewsFeedSlots.Order.Any(slot =>
            root.TryGetProperty(slot, out var value)
            && value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("contentCards", out _));

        if (!modern)
        {
            return ParseLegacyItems(root, now);
        }

        foreach (var slot in NewsFeedSlots.Order)
        {
            if (!root.TryGetProperty(slot, out var slotElement) || slotElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var candidate in ReadCandidateCards(slotElement, now, modern, slot))
            {
                if (modern && !MatchesModernFilters(candidate, request, slot))
                {
                    continue;
                }

                var item = modern ? ReadModernItem(slot, candidate) : ReadItem(slot, candidate);
                if (item is not null)
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    private static List<CacheItem> ParseLegacyItems(JsonElement root, DateTimeOffset now)
    {
        var items = new List<CacheItem>();
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

            foreach (var group in groups.EnumerateArray())
            {
                if (!IsGroupVisible(group, now))
                {
                    continue;
                }

                if (!group.TryGetProperty("contentItems", out var contentItems))
                {
                    continue;
                }

                foreach (var entry in EnumerateContentItems(contentItems))
                {
                    if (ReadItem(slot, entry) is { } item)
                    {
                        items.Add(item);
                    }
                }
            }
        }

        return items;
    }

    private static List<JsonElement> ReadCandidateCards(JsonElement slotElement, DateTimeOffset now, bool modern, string slot)
    {
        var result = new List<JsonElement>();
        if (slotElement.TryGetProperty("contentCards", out var cards))
        {
            foreach (var card in EnumerateValueElements(cards))
            {
                if (card.ValueKind == JsonValueKind.Object
                    && IsModernVisible(card, now)
                    && MatchesSection(card, slot, modern))
                {
                    result.Add(card);
                }
            }

            return result;
        }

        if (!slotElement.TryGetProperty("contentGroups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (IsGroupVisible(group, now) && group.TryGetProperty("contentItems", out var contentItems))
            {
                foreach (var itemElement in EnumerateContentItems(contentItems))
                {
                    if (MatchesSection(group, slot, modern))
                    {
                        result.Add(itemElement);
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateValueElements(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Value;
            }
        }
    }

    /// <summary>
    /// 旧格式 <c>contentItems</c> 可能是数组，也可能是对象表；每个元素才是真实卡片。
    /// </summary>
    private static IEnumerable<JsonElement> EnumerateContentItems(JsonElement contentItems) =>
        EnumerateValueElements(contentItems);

    private static JsonElement? SelectCard(
        List<JsonElement> cards,
        NewsFeedRequest request,
        bool modern,
        Func<double> random,
        string slot)
    {
        JsonElement? chosen = null;
        var chosenWeight = modern ? 0 : double.NegativeInfinity;
        foreach (var card in cards)
        {
            if (modern && !MatchesModernFilters(card, request, slot))
            {
                continue;
            }

            var weight = modern ? ReadDouble(card, "weight") ?? 1 : ReadGroupWeight(card);
            if (!modern)
            {
                if (weight > chosenWeight)
                {
                    chosenWeight = weight;
                    chosen = card;
                }

                continue;
            }

            chosenWeight += weight;
            if (random() * chosenWeight < weight)
            {
                chosen = card;
            }
        }

        return chosen;
    }

    private static bool MatchesModernFilters(JsonElement card, NewsFeedRequest request, string slot)
    {
        if (!card.TryGetProperty("extras", out var extras) || extras.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        if (!MatchesCsv(extras, "game", request.Game.GameId)
            || !MatchesCsv(extras, "distributionPlatforms", request.DistributionPlatform)
            || !MatchesSection(card, slot, modern: true))
        {
            return false;
        }

        if (extras.TryGetProperty("numberOfLauncherStarts", out var exact)
            && exact.ValueKind == JsonValueKind.Number
            && exact.TryGetInt32(out var target)
            && target != request.LaunchCount)
        {
            return false;
        }

        if (TryReadInt(extras, "minNumberOfLauncherStarts", out var min) && request.LaunchCount < min)
        {
            return false;
        }

        if (TryReadInt(extras, "maxNumberOfLauncherStarts", out var max) && request.LaunchCount > max)
        {
            return false;
        }

        return !card.TryGetProperty("filter", out var filter)
            || filter.ValueKind != JsonValueKind.Object
            || MatchesOwnership(filter, request.OwnedDlcIds);
    }

    private static bool MatchesSection(JsonElement card, string slot, bool modern)
    {
        if (!modern)
        {
            return true;
        }

        if (!card.TryGetProperty("extras", out var extras) || extras.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        return MatchesCsv(extras, "sections", slot);
    }

    private static bool MatchesCsv(JsonElement extras, string name, string expected) =>
        !extras.TryGetProperty(name, out var value)
        || value.ValueKind != JsonValueKind.String
        || string.IsNullOrWhiteSpace(value.GetString())
        || value.GetString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => string.Equals(item, expected, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesCsv(JsonElement extras, string name, IEnumerable<string> expected) =>
        expected.Any(value => MatchesCsv(extras, name, value));

    private static bool MatchesOwnership(JsonElement filter, IReadOnlySet<string> owned)
    {
        if (filter.TryGetProperty("owns", out var owns))
        {
            return Owns(owns, owned);
        }

        if (filter.TryGetProperty("and", out var and) && and.ValueKind == JsonValueKind.Array)
        {
            return and.EnumerateArray().All(item => MatchesOwnership(item, owned));
        }

        if (filter.TryGetProperty("or", out var or) && or.ValueKind == JsonValueKind.Array)
        {
            return or.EnumerateArray().Any(item => MatchesOwnership(item, owned));
        }

        return true;
    }

    private static bool Owns(JsonElement value, IReadOnlySet<string> owned) => value.ValueKind switch
    {
        JsonValueKind.String => owned.Contains(value.GetString() ?? string.Empty),
        JsonValueKind.Array => value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String
            && owned.Contains(item.GetString() ?? string.Empty)),
        _ => true,
    };

    private static bool IsModernVisible(JsonElement card, DateTimeOffset now) =>
        !(TryReadTimestamp(card, "from", out var from) && now < from)
        && !(TryReadTimestamp(card, "until", out var until) && now > until);

    private static bool IsGroupVisible(JsonElement group, DateTimeOffset now)
    {
        if (group.ValueKind != JsonValueKind.Object
            || !group.TryGetProperty("settings", out var settings)
            || settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty("visible", out var visible)
            || visible.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        return !(TryReadTimestamp(visible, "from", out var from) && now < from)
            && !(TryReadTimestamp(visible, "until", out var until) && now > until);
    }

    private static bool TryReadTimestamp(JsonElement owner, string propertyName, out DateTimeOffset value)
    {
        value = default;
        var text = ReadString(owner, propertyName);
        return text is not null && DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }

    private static double? ReadDouble(JsonElement owner, string name) =>
        owner.ValueKind == JsonValueKind.Object
        && owner.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var result)
            ? result
            : null;

    private static bool TryReadInt(JsonElement owner, string name, out int value)
    {
        value = 0;
        return owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static int ReadGroupWeight(JsonElement group) =>
        group.ValueKind == JsonValueKind.Object
        && group.TryGetProperty("settings", out var settings)
        && TryReadInt(settings, "weight", out var value)
            ? value
            : 1;

    private static CacheItem? ReadModernItem(string slot, JsonElement card)
    {
        var title = ReadString(card, "title");
        var text = ReadString(card, "description");
        var imageUrl = ReadString(card, "imageUrl");
        var link = ReadString(card, "url");
        if (card.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
        {
            link = ReadString(extras, "launcherUrl") ?? link;
        }

        return title is null && text is null && imageUrl is null && link is null
            ? null
            : new CacheItem { Slot = slot, Title = title, Text = text, ImageUrl = imageUrl, Link = link };
    }

    private static CacheItem? ReadItem(string slot, JsonElement entry)
    {
        if (entry.ValueKind != JsonValueKind.Object
            || !entry.TryGetProperty("content", out var content)
            || content.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = ReadLocalizedText(content, "title") ?? ReadString(entry, "localizedTitle");
        var text = ReadLocalizedText(content, "text");
        var imageUrl = ReadLegacyImageUrl(content);
        var link = ReadLegacyLink(content);
        return title is null && text is null && imageUrl is null && link is null
            ? null
            : new CacheItem { Slot = slot, Title = title, Text = text, ImageUrl = imageUrl, Link = link };
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

        return ReadString(map, "en") ?? map.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .Select(property => NullIfEmpty(property.Value.GetString()))
            .FirstOrDefault(value => value is not null);
    }

    private static string? ReadLegacyImageUrl(JsonElement content)
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

        return ReadString(image, "src") ?? ReadString(image, "cacheSrc")
            ?? (ReadString(image, "backgroundImage") is { } css ? ExtractCssUrl(css) : null);
    }

    private static string? ReadLegacyLink(JsonElement content)
    {
        if (!content.TryGetProperty("link", out var link))
        {
            return null;
        }

        return link.ValueKind == JsonValueKind.String
            ? NullIfEmpty(link.GetString())
            : link.ValueKind == JsonValueKind.Object ? ReadString(link, "href") : null;
    }

    private static string? ReadString(JsonElement owner, string propertyName)
    {
        if (owner.ValueKind != JsonValueKind.Object
            || !owner.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.String)
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
        return close <= start ? null : NullIfEmpty(css[start..close].Trim('"', '\''));
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NewsGameId(GameInstallation game) => KnownGames.GetNewsFeedGameId(game.GameId);

    private static string CacheKey(GameInstallation game, string platform) =>
        NewsGameId(game) + ":" + (string.IsNullOrWhiteSpace(platform) ? "pdx" : platform.ToLowerInvariant());

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

        var hash = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
        try
        {
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
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ImageDownloadTimeout);
            string? downloaded;
            try
            {
                downloaded = await _assets.DownloadAsync(uri.AbsoluteUri, targetWithoutExtension, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.Warn("官方公告图下载超时，卡片将不显示图片：" + url);
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

    private NewsFeed BuildFeed(CacheEntry? entry)
    {
        if (entry?.Items is null || entry.Items.Count == 0)
        {
            return NewsFeed.Empty;
        }

        var items = new List<NewsFeedItem>(entry.Items.Count);
        foreach (var cached in entry.Items)
        {
            if (!string.IsNullOrWhiteSpace(cached.Slot))
            {
                items.Add(new NewsFeedItem(
                    cached.Slot,
                    cached.Title,
                    cached.Text,
                    ResolveImagePath(cached.ImageFileName),
                    cached.ImageUrl,
                    cached.Link));
            }
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
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("官方公告缓存读取失败，已忽略并重建：" + exception.Message);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("官方公告缓存写入失败：" + exception.Message);
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

    private readonly record struct FetchResult(List<CacheItem> Items, bool ImagesIncomplete);

    private readonly record struct ImageDownload(string? Path, bool TimedOut);
}
