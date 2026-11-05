namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Linq;
using System.Security.Cryptography;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;
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
    /// <summary>
    /// 缓存 TTL：24 小时。条件请求（If-None-Match / If-Modified-Since）几乎零流量，
    /// 缩短到一天才能及时接到服务端「同一 URL 原地换图」；退避窗口内仍然一个请求都不发。
    /// </summary>
    public const long CacheTtlMilliseconds = ThemeRefreshDecision.DefaultTtlMilliseconds;

    /// <summary>
    /// 刷新有失败时的短重试间隔：10 分钟。
    /// 实测到 metadata-assets 的 TLS 握手会阶段性超时（一次连不上 6 项素材全灭），
    /// 若这种失败也推进 24 小时 TTL，该游戏主题会被锁在「安装目录那张 20% 不透明底图」上整整一天。
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

    /// <summary>Steam 官方库页大图（library_hero.jpg）失败后的重试间隔：10 分钟。</summary>
    public const long SteamHeroRetryMilliseconds = 600_000;

    /// <summary>索引来源标识：metadata 下发的素材。</summary>
    public const string MetadataSource = "metadata";

    /// <summary>索引来源标识：Steam 官方库页大图。</summary>
    public const string SteamHeroSource = "steam-hero";

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
    private readonly Func<string, IDocumentFile> _indexDocument;

    public ThemeResolver(
        IFileSystem fs,
        IClock clock,
        IAppLogger logger,
        IMetadataClient metadata,
        IAssetDownloader assets,
        string? cacheRoot = null,
        ISteamCoverUrlResolver? steamCovers = null,
        IBundledThemeSource? bundled = null,
        Func<string, IDocumentFile>? indexDocument = null)
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
        _indexDocument = indexDocument ?? (gameId => new FileDocumentFile(fs, IndexPath(gameId)));
    }

    /// <summary>默认缓存根目录：程序目录\data\theme-cache（绝不写 P 社目录，也不写 C 盘）。</summary>
    public static string DefaultCacheRoot => Path.Combine(PdxDataRoot.Directory, "theme-cache");

    public string CacheRoot => _cacheRoot;

    public async Task<ThemeSpec> ResolveAsync(
        GameInstallation game,
        bool allowOnlineRefresh,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        ArgumentNullException.ThrowIfNull(game);

        var layer2 = ResolveInstalledLayer(game);
        var cache = ReadCache(game.GameId);
        var index = AssetIndex.Load(_indexDocument(game.GameId), _logger);

        // 关键：不联网时必须在此提前返回，绝不触碰 IMetadataClient / IAssetDownloader。
        if (!allowOnlineRefresh)
        {
            _logger.Info(
                "主题决策: gameId=" + game.GameId + ", reason=离线, lastChecked="
                + FormatTime(index.LastCheckedUtc));
            var offline = ApplyBundled(
                game,
                TagSteamHeroSource(game, Merge(layer2, cache)),
                hasOnlineCache: cache.Count > 0);
            return WithSteamCapsule(game, offline);
        }

        if (index.Assets.Count == 0)
        {
            // 保留旧日志口径：自检与排障都靠这句确认「索引形状异常、需要重新联网」。
            _logger.Info("主题缓存索引没有素材条目，忽略上次检查时间重新联网: " + game.GameId);
        }

        var decision = EvaluateCacheDecision(index, forceRefresh);
        _logger.Info(
            "主题决策: gameId=" + game.GameId + ", reason=" + ReasonText(decision.Reason)
            + ", lastChecked=" + FormatTime(index.LastCheckedUtc)
            + ", retryAfter=" + FormatTime(index.RetryAfterUtc)
            + ", force=" + forceRefresh + ", cache=" + cache.Count + ", index=" + index.Assets.Count);

        if (!decision.ShouldRefresh)
        {
            LogSkip(game.GameId, index);
            var skipped = await WithSteamHeroAsync(
                    game,
                    TagSteamHeroSource(game, Merge(layer2, cache)),
                    forceRefresh,
                    cancellationToken)
                .ConfigureAwait(false);
            skipped = ApplyBundled(game, skipped, hasOnlineCache: cache.Count > 0);
            return await WithSteamCapsuleAsync(game, skipped, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            await RefreshAsync(game.GameId, index, cache, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 刷新失败必须静默降级：保留旧图，主题照常返回。
            _logger.Warn("主题联网刷新失败，保留本地素材: " + ex.Message);
            RecordFailure(game.GameId, "主题联网刷新异常: " + ex.Message);
        }

        var refreshedCache = ReadCache(game.GameId);
        var resolved = await WithSteamHeroAsync(
                game,
                TagSteamHeroSource(game, Merge(layer2, refreshedCache)),
                forceRefresh,
                cancellationToken)
            .ConfigureAwait(false);
        resolved = ApplyBundled(game, resolved, hasOnlineCache: refreshedCache.Count > 0);
        return await WithSteamCapsuleAsync(game, resolved, cancellationToken)
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
    /// 「Steam 官方库页大图」兜底：只在 metadata / 安装目录 / 在线缓存都给不出背景、
    /// 且该游戏有 Steam AppId 时尝试。EU5（3450310）在 metadata 里没有记录，靠这一层拿到 1920×620 官方图。
    /// 下载成功写进同一个 theme-cache（索引来源标 steam-hero）；失败按 10 分钟退避并回落到内置图，绝不空白。
    /// </summary>
    private async Task<ThemeSpec> WithSteamHeroAsync(
        GameInstallation game,
        ThemeSpec spec,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(spec.Background))
        {
            return spec;
        }

        if (game.SteamAppId is not int appId || appId <= 0)
        {
            return spec;
        }

        if (_steamCovers is null)
        {
            return spec;
        }

        if (!forceRefresh && IsSteamHeroBackoff(appId))
        {
            _logger.Info("steam-hero 退避中，跳过: appid=" + appId + ", gameId=" + game.GameId);
            return spec;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(SteamCapsuleTimeoutMilliseconds);
            var token = timeout.Token;

            var headerUrl = await _steamCovers
                .ResolveHeaderImageUrlAsync(appId, token)
                .ConfigureAwait(false);
            var heroUrl = SteamHeroUrl.ForLibraryHero(headerUrl);
            if (heroUrl is null)
            {
                _logger.Warn(
                    "steam-hero 不可用，回落内置图: appid=" + appId
                    + ", gameId=" + game.GameId + ", 原因=头图地址缺失或无法推导");
                MarkSteamHeroMiss(appId);
                return spec;
            }

            var target = _fs.Combine(
                _cacheRoot,
                game.GameId + "-" + AssetKeys.Background + "-" + ShortHash(heroUrl));
            var downloaded = await _assets
                .DownloadConditionalAsync(heroUrl, target, etag: null, lastModified: null, token)
                .ConfigureAwait(false);

            if (downloaded.Status != AssetDownloadStatus.Downloaded || downloaded.LocalPath is null)
            {
                _logger.Warn(
                    "steam-hero 不可用，回落内置图: appid=" + appId
                    + ", gameId=" + game.GameId + ", 原因=下载失败(" + downloaded.Status + ")");
                MarkSteamHeroMiss(appId);
                return spec;
            }

            var index = AssetIndex.Load(_indexDocument(game.GameId), _logger);
            var previousFileName = index.Find(AssetKeys.Background)?.FileName;
            var newFileName = _fs.GetFileName(downloaded.LocalPath);
            index.Set(
                AssetKeys.Background,
                heroUrl,
                newFileName,
                downloaded.ETag,
                downloaded.LastModified,
                SteamHeroSource);
            index.Save(_indexDocument(game.GameId), _logger);

            if (previousFileName is not null
                && !string.Equals(previousFileName, newFileName, StringComparison.OrdinalIgnoreCase))
            {
                TryDeleteCacheFile(_fs.Combine(_cacheRoot, previousFileName));
            }

            _logger.Info(
                "steam-hero: appid=" + appId + " 分辨率=" + DescribeImageSize(downloaded.LocalPath)
                + ", 字节=" + _fs.GetFileSize(downloaded.LocalPath)
                + ", gameId=" + game.GameId + " → " + downloaded.LocalPath);

            return spec with
            {
                Background = downloaded.LocalPath,
                Source = ThemeAssetSource.SteamHero,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.Warn(
                "steam-hero 不可用，回落内置图: appid=" + appId
                + ", gameId=" + game.GameId + ", 原因=超时(" + SteamCapsuleTimeoutMilliseconds + "ms)");
            MarkSteamHeroMiss(appId);
            return spec;
        }
        catch (Exception ex)
        {
            _logger.Warn(
                "steam-hero 不可用，回落内置图: appid=" + appId
                + ", gameId=" + game.GameId + ", 原因=" + ex.Message);
            MarkSteamHeroMiss(appId);
            return spec;
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

    /// <summary>
    /// 只看背景：有 logo 但缺背景仍然是「空白主页」，必须允许 Steam 封面兜底。
    /// 旧实现要求背景与 logo 同时为空，导致「有 logo、缺背景」的游戏永远停在纯色底。
    /// </summary>
    public static bool NeedsSteamCapsule(ThemeSpec spec) =>
        string.IsNullOrWhiteSpace(spec.Background);

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

    private string SteamHeroMissPath(int appId) =>
        _fs.Combine(_cacheRoot, "steam-" + appId + "-hero.miss");

    private bool IsSteamCapsuleBackoff(int appId) =>
        IsBackoff(SteamCapsuleMissPath(appId), SteamCapsuleRetryMilliseconds);

    private bool IsSteamHeroBackoff(int appId) =>
        IsBackoff(SteamHeroMissPath(appId), SteamHeroRetryMilliseconds);

    /// <summary>失败标记未过期即处于退避窗口，窗口内一个请求都不发。</summary>
    private bool IsBackoff(string path, long retryMilliseconds)
    {
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
            return _clock.UtcNow - missedAt < TimeSpan.FromMilliseconds(retryMilliseconds);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void MarkSteamHeroMiss(int appId)
    {
        try
        {
            _fs.EnsureDirectory(_cacheRoot);
            _fs.WriteAllTextAtomic(
                SteamHeroMissPath(appId),
                _clock.UtcNow.UtcTicks.ToString(CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("Steam 库页大图退避标记写入失败: " + ex.Message);
        }
    }

    /// <summary>
    /// 从下载到的字节里读图片像素尺寸，只用于诊断日志（读不出来就记「未知」，绝不影响解析）。
    /// 支持 PNG（IHDR）与 JPEG（SOF 段）。
    /// </summary>
    private string DescribeImageSize(string path)
    {
        try
        {
            var bytes = _fs.ReadAllBytes(path);
            if (bytes.Length >= 24
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            {
                var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                return width + "x" + height;
            }

            if (bytes.Length > 4 && bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                var i = 2;
                while (i + 9 < bytes.Length)
                {
                    if (bytes[i] != 0xFF)
                    {
                        i++;
                        continue;
                    }

                    var marker = bytes[i + 1];
                    if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
                    {
                        var height = (bytes[i + 5] << 8) | bytes[i + 6];
                        var width = (bytes[i + 7] << 8) | bytes[i + 8];
                        return width + "x" + height;
                    }

                    if (marker is 0xD8 or 0x01 or >= 0xD0 and <= 0xD7)
                    {
                        i += 2;
                        continue;
                    }

                    var length = (bytes[i + 2] << 8) | bytes[i + 3];
                    i += length >= 2 ? length : 2;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "未知";
        }

        return "未知";
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

        var index = AssetIndex.Load(_indexDocument(gameId), _logger);
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
    /// <summary>索引落点（库里是 <c>cache:theme-assets:&lt;gameId&gt;</c>，旧布局是文件）。</summary>
    public string IndexLocation(string gameId) => _indexDocument(gameId).DisplayPath;

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

        var index = AssetIndex.Load(_indexDocument(gameId), _logger);
        return index.LastCheckedUtc;
    }

    /// <summary>
    /// 把索引事实翻译成纯判定输入。URL 是否变化要等 metadata 才知道，
    /// 所以这里只判「索引/文件/时间」；单项 URL 变化在 RefreshOneAsync 里用同一套规则重判。
    /// </summary>
    private ThemeRefreshResult EvaluateCacheDecision(AssetIndex index, bool force)
    {
        var fileMissing = false;
        foreach (var entry in index.Assets.Values)
        {
            if (entry.FileName is null || !_fs.FileExists(_fs.Combine(_cacheRoot, entry.FileName)))
            {
                fileMissing = true;
                break;
            }
        }

        return ThemeRefreshDecision.Evaluate(
            new ThemeRefreshInput(
                _clock.UtcNow,
                index.LastCheckedUtc,
                index.RetryAfterUtc,
                IndexEmpty: index.Assets.Count == 0,
                // 这里恒为 false 是刻意的：缓存判定阶段还没拿到 metadata，无从比较 URL。
                // UrlChanged 分支保留给 RefreshOneAsync 的真实比较（sameUrl 路径），
                // 不要在决策入口把它当已判定事实。
                UrlChanged: false,
                FileMissing: fileMissing,
                Force: force));
    }

    /// <summary>跳过联网时的可 grep 证据：时间 + 每个素材的远端 URL。</summary>
    private void LogSkip(string gameId, AssetIndex index)
    {
        var urls = string.Join(
            " | ",
            index.Assets.Select(pair => pair.Key + "=" + (pair.Value.Url ?? "(无)")));
        _logger.Info(
            "主题跳过: gameId=" + gameId + ", lastChecked=" + FormatTime(index.LastCheckedUtc)
            + ", urls=" + urls);
    }

    private void RecordFailure(string gameId, string reason)
    {
        // 重新读取：异常可能发生在 RefreshAsync 已写入部分条目之后，
        // 用调用方手里的旧快照会把这些条目覆盖掉。
        var index = AssetIndex.Load(_indexDocument(gameId), _logger);
        index.RetryAfterUtc = _clock.UtcNow.AddMilliseconds(RefreshRetryDelayMilliseconds);
        index.LastError = reason;
        index.LastErrorUtc = _clock.UtcNow;
        index.Save(_indexDocument(gameId), _logger);
    }

    private static string ReasonText(ThemeRefreshReason reason) => reason switch
    {
        ThemeRefreshReason.Force => "force",
        ThemeRefreshReason.Backoff => "退避中",
        ThemeRefreshReason.IndexEmpty => "索引空",
        ThemeRefreshReason.UrlChanged => "URL 变",
        ThemeRefreshReason.FileMissing => "文件缺失",
        ThemeRefreshReason.RetryDue => "退避到点",
        ThemeRefreshReason.NeverChecked => "从未检查",
        ThemeRefreshReason.TtlExpired => "TTL 已过",
        _ => "TTL 未到",
    };

    private static string FormatTime(DateTimeOffset? value) =>
        value?.ToString("u", CultureInfo.InvariantCulture) ?? "null";

    /// <summary>索引里的失败/退避/元数据缺失；设置页的「上次失败原因」直接读这里。</summary>
    public ThemeCacheDiagnostics GetDiagnostics(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return new ThemeCacheDiagnostics(null, null, null, null, MetadataMissing: false);
        }

        var index = AssetIndex.Load(_indexDocument(gameId), _logger);
        return new ThemeCacheDiagnostics(
            index.LastCheckedUtc,
            index.RetryAfterUtc,
            index.LastError,
            index.LastErrorUtc,
            index.MetadataMissing);
    }

    /// <summary>
    /// 层2：安装目录主题（只取 game.Theme 本身）。
    /// 内置素材不再在这里补背景——它要排在 Steam 官方库页大图之后，
    /// 由 <see cref="ApplyBundled"/> 在最末兜底，否则 EU5 这类「有内置图、无 metadata」的游戏永远轮不到官方图。
    /// 另外，离线阶段 <c>ThemeUpdateService</c> 会把内置图经 <see cref="Themes.ThemePatch.Apply"/>
    /// 打进 <c>game.Theme</c>（来源 Bundled）。那不是真实安装层：若把它当背景返回，
    /// 联网解析会误判「已有背景」而永久跳过官方图，所以这里剥掉它的背景，交给联网层重新判定；
    /// 真失败时 <see cref="ApplyBundled"/> 会用同一张内置图补回来，离线显示不受影响。
    /// </summary>
    private static ThemeSpec ResolveInstalledLayer(GameInstallation game)
    {
        var installed = game.Theme;
        if (installed is null)
        {
            return DefaultThemeSpec();
        }

        if (installed.Source == ThemeAssetSource.Bundled)
        {
            return installed with { Background = string.Empty };
        }

        return installed;
    }

    /// <summary>
    /// 内置素材兜底：只有前面所有来源都没给出背景时才补，绝不覆盖在线/官方图。
    /// 有在线缓存时保留 Merge 推导出的来源，否则标成 Bundled。
    /// </summary>
    private ThemeSpec ApplyBundled(GameInstallation game, ThemeSpec spec, bool hasOnlineCache)
    {
        if (!string.IsNullOrWhiteSpace(spec.Background))
        {
            return spec;
        }

        var bundled = _bundled?.TryResolve(game);
        if (bundled?.Background is not { } background)
        {
            return spec;
        }

        return spec with
        {
            Background = background,
            Source = hasOnlineCache ? spec.Source : ThemeAssetSource.Bundled,
        };
    }

    /// <summary>
    /// 缓存里那张背景若来自 Steam 官方库页，把来源标成 <see cref="ThemeAssetSource.SteamHero"/>，
    /// 让设置页能说清「这张图来自 Steam 官方库页」。
    /// </summary>
    private ThemeSpec TagSteamHeroSource(GameInstallation game, ThemeSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Background))
        {
            return spec;
        }

        var index = AssetIndex.Load(_indexDocument(game.GameId), _logger);
        var entry = index.Find(AssetKeys.Background);
        if (entry is null
            || entry.FileName is not { } fileName
            || !string.Equals(entry.Source, SteamHeroSource, StringComparison.OrdinalIgnoreCase))
        {
            return spec;
        }

        var cachedPath = _fs.Combine(_cacheRoot, fileName);
        if (!string.Equals(spec.Background, cachedPath, StringComparison.OrdinalIgnoreCase))
        {
            return spec;
        }

        return _fs.FileExists(cachedPath)
            ? spec with { Source = ThemeAssetSource.SteamHero }
            : spec;
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

    private async Task RefreshAsync(
        string gameId,
        AssetIndex index,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var metadataList = await _metadata
            .GetMetadataAsync(new[] { gameId }, cancellationToken)
            .ConfigureAwait(false);

        if (metadataList.Count == 0)
        {
            MarkMetadataMissing(gameId, index, "元数据无该游戏（metadata 未返回记录）");
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

        if (dto is null)
        {
            MarkMetadataMissing(gameId, index, "元数据无该游戏");
            return;
        }

        if (dto.ThemeSettings is not { } settings)
        {
            // 没有 themeSettings 视为未成功检查，不推进 lastCheckedUtc。
            index.LastError = "元数据缺少 themeSettings";
            index.LastErrorUtc = _clock.UtcNow;
            index.Save(_indexDocument(gameId), _logger);
            _logger.Warn("元数据缺少 themeSettings，跳过主题刷新: " + gameId + "，使用内置素材");
            return;
        }

        var remote = BuildRemoteMap(settings);
        if (remote.Count == 0)
        {
            // 远端给了 themeSettings 但一项可覆盖素材都没有：不推进 lastCheckedUtc，
            // 以免写出「检查过却零素材」的索引形状（那会被当成旧 bug 的产物而反复重查）。
            index.LastError = "远端主题没有可用素材";
            index.LastErrorUtc = _clock.UtcNow;
            index.Save(_indexDocument(gameId), _logger);
            _logger.Warn("远端主题没有可用素材，跳过主题刷新: " + gameId + "，使用内置素材");
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
            // 改记 10 分钟后的短重试。否则一次网络抖动就能把该游戏主题锁死一整天。
            var failed = remote.Count - succeeded;
            index.RetryAfterUtc = _clock.UtcNow.AddMilliseconds(RefreshRetryDelayMilliseconds);
            index.LastError = "主题刷新部分失败: 成功 " + succeeded + "/" + remote.Count + " 项";
            index.LastErrorUtc = _clock.UtcNow;
            index.Save(_indexDocument(gameId), _logger);
            _logger.Warn(
                "主题刷新部分失败: " + gameId + "，成功 " + succeeded + "/" + remote.Count
                + " 项（失败 " + failed + " 项），"
                + (RefreshRetryDelayMilliseconds / 60_000) + " 分钟后重试");
            return;
        }

        index.LastCheckedUtc = _clock.UtcNow;
        index.RetryAfterUtc = null;
        index.MetadataMissing = false;
        index.LastError = null;
        index.LastErrorUtc = null;
        index.Save(_indexDocument(gameId), _logger);
        _logger.Info("主题缓存已更新: " + gameId + "，素材 " + remote.Count + " 项");
    }

    /// <summary>
    /// metadata 里没有该游戏（EU5 这类）：记录明确原因并保留内置素材，
    /// 不静默、不推进 lastCheckedUtc；设置页据此显示「元数据无该游戏，使用内置素材」。
    /// </summary>
    private void MarkMetadataMissing(string gameId, AssetIndex index, string reason)
    {
        index.MetadataMissing = true;
        index.RetryAfterUtc = null;
        index.LastError = reason;
        index.LastErrorUtc = _clock.UtcNow;
        index.Save(_indexDocument(gameId), _logger);
        _logger.Warn(reason + "，跳过主题刷新: " + gameId + "，使用内置素材");
    }

    /// <summary>
    /// 刷新单项素材。返回该项是否已就绪：
    /// URL 未变且文件在库 → 条件请求（304 只更新检查时间）；URL 变/文件缺失 → 无条件重下。
    /// </summary>
    private async Task<bool> RefreshOneAsync(
        string gameId,
        AssetIndex index,
        string key,
        string url,
        Dictionary<string, string> cache,
        CancellationToken cancellationToken)
    {
        var entry = index.Find(key);
        var previousFileName = entry?.FileName;
        var previousPath = previousFileName is null
            ? null
            : _fs.Combine(_cacheRoot, previousFileName);
        var fileExists = previousPath is not null && _fs.FileExists(previousPath);
        var sameUrl = entry is not null
            && string.Equals(entry.Url, url, StringComparison.Ordinal);

        // 文件名里带上 URL 的短 hash：URL（= 版本）一变文件名就变，
        // 于是 BitmapLoader 的 path 缓存键、WinUI 的图片 URI 缓存、FontLoader 的路径缓存
        // 全部自然失效，不需要在 UI 层再做一次 ClearCache。
        var targetWithoutExtension = _fs.Combine(_cacheRoot, gameId + "-" + key + "-" + ShortHash(url));

        // 只有「URL 未变 + 文件仍在」才带旧的校验头；文件缺失时发条件请求会拿到
        // 304 却仍然没有文件，属于必须避开的死局。
        var etag = sameUrl && fileExists ? entry!.ETag : null;
        var lastModified = sameUrl && fileExists ? entry!.LastModified : null;

        if (entry is not null && !sameUrl)
        {
            _logger.Info(
                "主题素材 URL 变，强制重新下载: gameId=" + gameId + ", key=" + key
                + ", old=" + (entry.Url ?? "(无)") + ", new=" + url);
        }
        else if (entry is not null && !fileExists)
        {
            _logger.Info(
                "主题素材文件缺失，重新下载: gameId=" + gameId + ", key=" + key
                + ", old=" + (previousPath ?? "(无)"));
        }

        var result = await _assets
            .DownloadConditionalAsync(url, targetWithoutExtension, etag, lastModified, cancellationToken)
            .ConfigureAwait(false);

        if (result.Status == AssetDownloadStatus.NotModified)
        {
            // 304：绝不重写文件、不改条目；调用方只推进 lastCheckedUtc。
            _logger.Info(
                "主题素材未变化（304）: gameId=" + gameId + ", key=" + key
                + ", file=" + (previousPath ?? "(无)"));
            return true;
        }

        if (result.Status != AssetDownloadStatus.Downloaded || result.LocalPath is null)
        {
            _logger.Warn(
                "主题素材下载失败，保留旧图: gameId=" + gameId + ", key=" + key
                + ", old=" + (previousPath ?? "(无)") + ", oldModified="
                + FormatFileTime(previousPath));
            return false;
        }

        var newFileName = _fs.GetFileName(result.LocalPath);
        index.Set(key, url, newFileName, result.ETag, result.LastModified, MetadataSource);
        cache[key] = result.LocalPath;

        // 新旧文件名不同就清掉旧文件，避免缓存目录按版本堆积。
        if (previousFileName is not null
            && !string.Equals(previousFileName, newFileName, StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteCacheFile(_fs.Combine(_cacheRoot, previousFileName));
        }

        return true;
    }

    private static string FormatFileTime(string? path)
    {
        if (path is null)
        {
            return "(无)";
        }

        try
        {
            return File.GetLastWriteTimeUtc(path).ToString("u", CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "(读取失败: " + ex.Message + ")";
        }
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
