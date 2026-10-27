namespace PdxLauncher.Core.Online;

using System.Security.Cryptography;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 主题三层解析：联网缓存（层1） → 安装目录（层2，即 game.Theme） → 内置占位（层3）。
/// 硬规则：
/// 1) 颜色/字体/字号/字重/阴影永远取自层2，层1 只覆盖 6 个图片/字体路径；
/// 2) allowOnlineRefresh=false 时绝不发起任何网络请求，只读缓存文件与索引；
/// 3) 刷新失败静默保留旧图，绝不把异常抛给 UI。
/// </summary>
public sealed class ThemeResolver : IThemeResolver
{
    /// <summary>缓存 TTL：5 天（文档 §7 实测值）。</summary>
    public const long CacheTtlMilliseconds = 432_000_000;

    /// <summary>
    /// 刷新有失败时的短重试间隔：10 分钟。
    /// 实测到 metadata-assets 的 TLS 握手会阶段性超时（一次连不上 6 项素材全灭），
    /// 若这种失败也推进 5 天 TTL，该游戏主题会被锁在「安装目录那张 20% 不透明底图」上整整 5 天。
    /// </summary>
    public const long RefreshRetryDelayMilliseconds = 600_000;

    /// <summary>可被联网缓存覆盖的素材键（ThemeSpec 能表达的 6 个）。</summary>
    public static readonly string[] OverridableKeys =
    {
        AssetKeys.Background,
        AssetKeys.Logo,
        AssetKeys.AppIcon,
        AssetKeys.AppTaskbarIcon,
        AssetKeys.ButtonBackground,
        AssetKeys.ButtonFont,
    };

    private readonly IFileSystem _fs;
    private readonly IClock _clock;
    private readonly IAppLogger _logger;
    private readonly IMetadataClient _metadata;
    private readonly IAssetDownloader _assets;
    private readonly string _cacheRoot;

    public ThemeResolver(
        IFileSystem fs,
        IClock clock,
        IAppLogger logger,
        IMetadataClient metadata,
        IAssetDownloader assets,
        string? cacheRoot = null)
    {
        ArgumentNullException.ThrowIfNull(fs);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(assets);
        _fs = fs;
        _clock = clock;
        _logger = logger;
        _metadata = metadata;
        _assets = assets;
        _cacheRoot = string.IsNullOrWhiteSpace(cacheRoot) ? DefaultCacheRoot : cacheRoot;
    }

    /// <summary>默认缓存根目录：程序目录\data\theme-cache（绝不写 P 社目录，也不写 C 盘）。</summary>
    public static string DefaultCacheRoot => Path.Combine(PdxDataRoot.Directory, "theme-cache");

    public string CacheRoot => _cacheRoot;

    public async Task<ThemeSpec> ResolveAsync(
        GameInstallation game,
        bool allowOnlineRefresh,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        var layer2 = game.Theme ?? DefaultThemeSpec();
        var cache = ReadCache(game.GameId);

        // 关键：不联网时必须在此提前返回，绝不触碰 IMetadataClient / IAssetDownloader。
        if (!allowOnlineRefresh)
        {
            return Merge(layer2, cache);
        }

        if (!IsCacheStale(game.GameId))
        {
            return Merge(layer2, cache);
        }

        try
        {
            await RefreshAsync(game.GameId, cache, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 刷新失败必须静默降级：保留旧图，主题照常返回。
            _logger.Warn("主题联网刷新失败，保留本地素材: " + ex.Message);
        }

        return Merge(layer2, ReadCache(game.GameId));
    }

    /// <summary>只读层1：从索引里挑出真实存在的缓存文件，得到 键 → 绝对路径。</summary>
    public Dictionary<string, string> ReadCache(string gameId)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return result;
        }

        var index = AssetIndex.Load(_fs, _logger, IndexPath(gameId));
        foreach (var key in OverridableKeys)
        {
            var entry = index.Find(key);
            if (entry?.FileName is null)
            {
                continue;
            }

            var path = _fs.Combine(_cacheRoot, entry.FileName);
            if (_fs.FileExists(path))
            {
                result[key] = path;
            }
        }

        return result;
    }

    /// <summary>索引路径固定 &lt;cacheRoot&gt;\&lt;gameId&gt;.assets.json。</summary>
    public string IndexPath(string gameId) =>
        _fs.Combine(_cacheRoot, gameId + ".assets.json");

    /// <summary>
    /// 上次成功联网检查主题的时间；从未检查过或索引损坏（视为无缓存）时返回 null。
    /// 索引的 lastCheckedUtc 是 TTL 的唯一事实来源，设置页展示也读这里。
    /// </summary>
    public DateTimeOffset? GetLastCheckedUtc(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        var index = AssetIndex.Load(_fs, _logger, IndexPath(gameId));
        return index.LastCheckedUtc;
    }

    private static ThemeSpec Merge(ThemeSpec layer2, Dictionary<string, string> cache)
    {
        var background = cache.TryGetValue(AssetKeys.Background, out var bg) ? bg : layer2.Background;
        var logo = cache.TryGetValue(AssetKeys.Logo, out var lg) ? lg : layer2.Logo;
        var appIcon = cache.TryGetValue(AssetKeys.AppIcon, out var ai) ? ai : layer2.AppIcon;
        var taskbar = cache.TryGetValue(AssetKeys.AppTaskbarIcon, out var tb) ? tb : layer2.AppTaskbarIcon;
        var buttonBg = cache.TryGetValue(AssetKeys.ButtonBackground, out var bb) ? bb : layer2.ButtonBackground;
        var buttonFont = cache.TryGetValue(AssetKeys.ButtonFont, out var bf) ? bf : layer2.ButtonFont;

        var source = cache.Count > 0
            ? ThemeAssetSource.OnlineCache
            : HasAnyPath(layer2)
                ? ThemeAssetSource.InstallDir
                : ThemeAssetSource.BuiltInPlaceholder;

        return new ThemeSpec(
            background,
            logo,
            appIcon,
            taskbar,
            buttonBg,
            buttonFont,
            layer2.ButtonTextColor,
            layer2.ButtonHoverTextColor,
            layer2.FontSize,
            layer2.FontWeight,
            layer2.TextShadow,
            source);
    }

    private static bool HasAnyPath(ThemeSpec spec) =>
        spec.Background is not null
        || spec.Logo is not null
        || spec.AppIcon is not null
        || spec.AppTaskbarIcon is not null
        || spec.ButtonBackground is not null
        || spec.ButtonFont is not null;

    private static ThemeSpec DefaultThemeSpec() =>
        new(
            null,
            null,
            null,
            null,
            null,
            null,
            ButtonTextColorFallback,
            ButtonHoverTextColorFallback,
            FontSizeFallback,
            FontWeightFallback,
            null,
            ThemeAssetSource.BuiltInPlaceholder);

    /// <summary>层2 缺失时的按钮文字色兜底（P 社主题的浅米色；它是文字色，不是底色）。</summary>
    public const string ButtonTextColorFallback = "hsla(53, 31%, 85%, 0.9)";

    /// <summary>层2 缺失时的按钮悬停文字色兜底。</summary>
    public const string ButtonHoverTextColorFallback = "hsla(53, 36%, 95%, 1)";

    /// <summary>层2 缺失时的字号兜底（官方实测 24）。</summary>
    public const double FontSizeFallback = 24d;

    /// <summary>层2 缺失时的字重兜底（官方实测 200）。</summary>
    public const int FontWeightFallback = 200;

    /// <summary>
    /// 新鲜度判定：只看索引里的 lastCheckedUtc（上一次联网检查时间），不看缓存文件 mtime。
    /// 原因：URL 带内容 hash，URL 未变就跳过下载，文件 mtime 永不更新；若用 mtime 判新鲜度，
    /// 每次启动都会判定过期 → 每次都发一次 GraphQL 请求却永远不下载，白白浪费网络。
    /// 文件是否真实存在由 ReadCache 的 FileExists 过滤单独保证。
    /// </summary>
    private bool IsCacheStale(string gameId)
    {
        var index = AssetIndex.Load(_fs, _logger, IndexPath(gameId));
        var lastChecked = index.LastCheckedUtc;
        if (lastChecked is null || lastChecked.Value == default(DateTimeOffset))
        {
            // 从未成功联网检查过（冷启动、索引缺失或索引损坏）：需要联网。
            return true;
        }

        // 自愈：索引说「检查过了」，却一项素材都没落地。
        // 只有旧版 RefreshAsync 会写出这种形状（下载全失败也推进 lastCheckedUtc），
        // 新版在有失败时改写 RetryAfterUtc、成功时必然留下条目，所以命中即视为旧 bug 的产物。
        if (index.Assets.Count == 0)
        {
            _logger.Info("主题缓存索引没有素材条目，忽略上次检查时间重新联网: " + gameId);
            return true;
        }

        // 上次刷新有素材失败：到点就重试，不必再等满 5 天。
        if (index.RetryAfterUtc is { } retryAfter && _clock.UtcNow >= retryAfter)
        {
            return true;
        }

        return _clock.UtcNow - lastChecked.Value > TimeSpan.FromMilliseconds(CacheTtlMilliseconds);
    }

    private async Task RefreshAsync(
        string gameId,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var metadataList = await _metadata
            .GetMetadataAsync(new[] { gameId }, cancellationToken)
            .ConfigureAwait(false);
        var index = AssetIndex.Load(_fs, _logger, IndexPath(gameId));

        if (metadataList.Count == 0)
        {
            // 元数据为空多为网络失败（MetadataClient 容错后返回空列表），
            // 此时**不**更新 lastCheckedUtc，下次启动仍会重试，避免被 5 天 TTL 锁住。
            _logger.Info("元数据未返回该游戏，跳过主题刷新: " + gameId);
            return;
        }

        GameMetadataDto? dto = null;
        foreach (var candidate in metadataList)
        {
            if (string.Equals(candidate.Id, gameId, StringComparison.OrdinalIgnoreCase))
            {
                dto = candidate;
                break;
            }
        }

        if (dto?.ThemeSettings is not { } settings)
        {
            // 同上：没有 themeSettings 视为未成功检查，不推进 lastCheckedUtc。
            _logger.Info("元数据缺少 themeSettings，跳过主题刷新: " + gameId);
            return;
        }

        var remote = BuildRemoteMap(settings);
        if (remote.Count == 0)
        {
            // 远端给了 themeSettings 但一项可覆盖素材都没有：不推进 lastCheckedUtc，
            // 以免写出「检查过却零素材」的索引形状（那会被当成旧 bug 的产物而反复重查）。
            _logger.Info("远端主题没有可用素材，跳过主题刷新: " + gameId);
            return;
        }

        var succeeded = 0;
        foreach (var pair in remote)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await RefreshOneAsync(gameId, index, pair.Key, pair.Value, cache, cancellationToken)
                    .ConfigureAwait(false))
            {
                succeeded++;
            }
        }

        if (succeeded < remote.Count)
        {
            // 关键：有素材失败时**不**推进 lastCheckedUtc（它只代表「全部就绪」的那次检查），
            // 改记 10 分钟后的短重试。否则一次网络抖动就能把该游戏主题锁死 5 天。
            index.RetryAfterUtc = _clock.UtcNow.AddMilliseconds(RefreshRetryDelayMilliseconds);
            index.Save(_fs, _logger, IndexPath(gameId));
            _logger.Warn(
                "主题刷新部分失败: " + gameId + "，成功 " + succeeded + "/" + remote.Count
                + " 项，" + (RefreshRetryDelayMilliseconds / 60_000) + " 分钟后重试");
            return;
        }

        index.LastCheckedUtc = _clock.UtcNow;
        index.RetryAfterUtc = null;
        index.Save(_fs, _logger, IndexPath(gameId));
        _logger.Info("主题缓存已更新: " + gameId + "，素材 " + remote.Count + " 项");
    }

    /// <summary>刷新单项素材；返回该项是否已就绪（URL 未变且文件在库 / 本次下载成功）。</summary>
    private async Task<bool> RefreshOneAsync(
        string gameId,
        AssetIndex index,
        string key,
        string url,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var entry = index.Find(key);

        // URL 里的 hash 段即版本号：URL 未变且本地文件存在 → 跳过下载。
        if (entry is not null
            && string.Equals(entry.Url, url, StringComparison.Ordinal)
            && entry.FileName is not null
            && _fs.FileExists(_fs.Combine(_cacheRoot, entry.FileName)))
        {
            return true;
        }

        // 文件名里带上 URL 的短 hash：URL（= 版本）一变文件名就变，
        // 于是 BitmapLoader 的 path 缓存键、WinUI 的图片 URI 缓存、FontLoader 的路径缓存
        // 全部自然失效，不需要在 UI 层再做一次 ClearCache。
        var targetWithoutExtension = _fs.Combine(_cacheRoot, gameId + "-" + key + "-" + ShortHash(url));
        var downloaded = await _assets
            .DownloadAsync(url, targetWithoutExtension, cancellationToken)
            .ConfigureAwait(false);
        if (downloaded is null)
        {
            // 失败静默保留旧图，由调用方汇总成「本次刷新有失败」。
            return false;
        }

        var newFileName = _fs.GetFileName(downloaded);
        var previous = entry?.FileName;
        index.Set(key, url, newFileName);
        cache[key] = downloaded;

        // 新旧文件名不同就清掉旧文件，避免缓存目录按版本堆积。
        if (previous is not null && !string.Equals(previous, newFileName, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteCacheFile(_fs.Combine(_cacheRoot, previous));
        }

        return true;
    }

    private void TryDeleteCacheFile(string path)
    {
        try
        {
            if (_fs.FileExists(path))
            {
                _fs.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.Warn("清理旧主题缓存失败: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("清理旧主题缓存无权限: " + ex.Message);
        }
    }

    private static Dictionary<string, string> BuildRemoteMap(ThemeSettingsDto settings)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        AddIfPresent(map, AssetKeys.Background, settings.Background);
        AddIfPresent(map, AssetKeys.Logo, settings.Logo);
        AddIfPresent(map, AssetKeys.AppIcon, settings.AppIcon);
        AddIfPresent(map, AssetKeys.AppTaskbarIcon, settings.AppTaskbarIcon);
        AddIfPresent(map, AssetKeys.ButtonBackground, settings.ButtonBackgroundImage);
        AddIfPresent(map, AssetKeys.ButtonFont, settings.ButtonFontPath);
        return map;
    }

    /// <summary>URL → 8 位小写十六进制（SHA-256 前 4 字节），作为缓存文件名的版本段。</summary>
    private static string ShortHash(string url)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexStringLower(digest.AsSpan(0, 4));
    }

    private static void AddIfPresent(Dictionary<string, string> map, string key, string? url)
    {
        if (!string.IsNullOrWhiteSpace(url))
        {
            map[key] = url.Trim();
        }
    }
}
