namespace PdxLauncher.Core.Online;

using System.Globalization;
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

    /// <summary>
    /// Steam 胶囊图兜底失败后的重试间隔：10 分钟。
    /// 与主题刷新同一个理由——断网/被墙时别每次解析都去撞一遍 CDN。
    /// </summary>
    public const long SteamCapsuleRetryMilliseconds = 600_000;

    /// <summary>
    /// 封面兜底（商店接口 + 图片下载）的整体超时。
    /// 实测该接口偶尔会「连上但不回包」，若沿用默认 180 秒总超时，卡片会空很久才回落到占位图；
    /// 这里卡死 12 秒，超时按一次失败处理并进入退避。
    /// </summary>
    public const int SteamCapsuleTimeoutMilliseconds = 12_000;

    /// <summary>
    /// Steam 官方商店头图的旧式静态地址（460×215，无鉴权、无防盗链）。
    /// 仅作为 <see cref="ISteamCoverUrlResolver"/> 缺席或解析失败时的兜底：
    /// Steam 已迁移到带内容 hash 的 store_item_assets 路径，新游戏（EU5 3450310）用这个地址会 404。
    /// </summary>
    public const string SteamCapsuleUrlTemplate =
        "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/header.jpg";

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
    private readonly ISteamCoverUrlResolver? _steamCovers;
    private readonly IBundledThemeSource? _bundled;

    public ThemeResolver(
        IFileSystem fs,
        IClock clock,
        IAppLogger logger,
        IMetadataClient metadata,
        IAssetDownloader assets,
        string? cacheRoot = null,
        ISteamCoverUrlResolver? steamCovers = null,
        IBundledThemeSource? bundled = null)
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
        _steamCovers = steamCovers;
        _bundled = bundled;
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

        var layer2 = ResolveLayer2(game);
        var cache = ReadCache(game.GameId);

        // 关键：不联网时必须在此提前返回，绝不触碰 IMetadataClient / IAssetDownloader。
        if (!allowOnlineRefresh)
        {
            return WithSteamCapsule(game, Merge(layer2, cache));
        }

        if (!IsCacheStale(game.GameId))
        {
            return await WithSteamCapsuleAsync(game, Merge(layer2, cache), cancellationToken)
                .ConfigureAwait(false);
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

        return await WithSteamCapsuleAsync(game, Merge(layer2, ReadCache(game.GameId)), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 离线快路径：只有本地已经缓存的 Steam 胶囊图才可用，绝不发请求。
    /// 只在层1/层2 都给不出背景时生效——有主题素材的游戏永远优先用官方图。
    /// </summary>
    private ThemeSpec WithSteamCapsule(GameInstallation game, ThemeSpec spec)
    {
        if (!NeedsSteamCapsule(spec))
        {
            return spec;
        }

        var cached = FindSteamCapsule(game.SteamAppId);
        return cached is null
            ? spec
            : spec with { Background = cached, Source = ThemeAssetSource.SteamCapsule };
    }

    /// <summary>联网路径：本地没有缓存时才尝试下「一次」，失败按 10 分钟退避。</summary>
    private async Task<ThemeSpec> WithSteamCapsuleAsync(
        GameInstallation game,
        ThemeSpec spec,
        CancellationToken cancellationToken)
    {
        var local = WithSteamCapsule(game, spec);
        if (!NeedsSteamCapsule(local))
        {
            return local;
        }

        if (game.SteamAppId is not int appId || appId <= 0)
        {
            _logger.Info("没有 Steam AppId 可查封面，跳过封面兜底: " + game.GameId);
            return local;
        }

        if (IsSteamCapsuleBackoff(appId))
        {
            return local;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SteamCapsuleTimeoutMilliseconds);
            var token = timeout.Token;

            var url = await ResolveCapsuleUrlAsync(appId, token).ConfigureAwait(false);
            if (url is null)
            {
                _logger.Warn("Steam 封面地址不可用: " + game.GameId + "，10 分钟后重试");
                MarkSteamCapsuleMiss(appId);
                return local;
            }

            var downloaded = await _assets
                .DownloadAsync(url, _fs.Combine(_cacheRoot, "steam-" + appId + "-header"), token)
                .ConfigureAwait(false);

            if (downloaded is null || !_fs.FileExists(downloaded))
            {
                _logger.Warn("Steam 封面下载失败: " + game.GameId + "，10 分钟后重试");
                MarkSteamCapsuleMiss(appId);
                return local;
            }

            _logger.Info("Steam 封面已缓存: " + game.GameId + " → " + downloaded);
            return local with { Background = downloaded, Source = ThemeAssetSource.SteamCapsule };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            // 只有兜底自己的 12 秒预算到期：按失败退避，绝不把占位图卡住。
            _logger.Warn("Steam 封面超时: " + game.GameId + "，10 分钟后重试");
            MarkSteamCapsuleMiss(appId);
            return local;
        }
        catch (Exception ex)
        {
            _logger.Warn("Steam 封面下载失败: " + game.GameId + "，10 分钟后重试: " + ex.Message);
            MarkSteamCapsuleMiss(appId);
            return local;
        }
    }

    /// <summary>
    /// 先问商店接口要带 hash 的真实头图地址；没有解析器或解析失败时退回旧式静态地址
    /// （老游戏仍可用，新游戏会 404 → 由调用方记一次退避）。
    /// </summary>
    private async Task<string?> ResolveCapsuleUrlAsync(int appId, CancellationToken cancellationToken)
    {
        if (_steamCovers is not null)
        {
            var resolved = await _steamCovers
                .ResolveHeaderImageUrlAsync(appId, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return string.Format(CultureInfo.InvariantCulture, SteamCapsuleUrlTemplate, appId);
    }

    /// <summary>背景与 logo 都空才算「没有官方主题素材」。</summary>
    private static bool NeedsSteamCapsule(ThemeSpec spec) =>
        string.IsNullOrWhiteSpace(spec.Background) && string.IsNullOrWhiteSpace(spec.Logo);

    /// <summary>缓存目录里已有的胶囊图（扩展名由下载器按 magic bytes 决定）。</summary>
    private string? FindSteamCapsule(int? steamAppId)
    {
        if (steamAppId is not int appId || appId <= 0)
        {
            return null;
        }

        foreach (var path in _fs.EnumerateFiles(_cacheRoot, "steam-" + appId + "-header.*", false))
        {
            if (_fs.FileExists(path))
            {
                return path;
            }
        }

        return null;
    }

    private string SteamCapsuleMissPath(int appId) =>
        _fs.Combine(_cacheRoot, "steam-" + appId + ".miss");

    private bool IsSteamCapsuleBackoff(int appId)
    {
        var path = SteamCapsuleMissPath(appId);
        if (!_fs.FileExists(path))
        {
            return false;
        }

        try
        {
            var text = _fs.ReadAllText(path).Trim();
            if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            {
                return false;
            }

            var missedAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return _clock.UtcNow - missedAt < TimeSpan.FromMilliseconds(SteamCapsuleRetryMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void MarkSteamCapsuleMiss(int appId)
    {
        try
        {
            _fs.EnsureDirectory(_cacheRoot);
            _fs.WriteAllTextAtomic(
                SteamCapsuleMissPath(appId),
                _clock.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("Steam 封面退避标记写入失败: " + ex.Message);
        }
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

    /// <summary>
    /// 层 2：安装目录主题；安装目录没有主题素材时用自带美术（层 2.5）补上，最后才是内置占位图。
    /// <para>
    /// 只替换「没有主题」的情况：安装目录里有 theme-settings.json 素材的游戏（EU4/CK3/…）
    /// 永远不会被自带美术覆盖。
    /// </para>
    /// </summary>
    private ThemeSpec ResolveLayer2(GameInstallation game)
    {
        var installed = game.Theme;
        if (installed is not null && HasAnyPath(installed))
        {
            return installed;
        }

        var bundled = _bundled?.TryResolve(game);
        return bundled ?? installed ?? DefaultThemeSpec();
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
            : layer2.Source == ThemeAssetSource.Bundled
                ? ThemeAssetSource.Bundled
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
