namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
/// <summary>
/// 应用级服务定位器。WinUI 的 Frame.Navigate 只支持无参构造，所以页面统一从这里取依赖。
/// 由 App.xaml.cs 在启动时 <see cref="Initialize"/> 一次性装配；之后只读。
/// </summary>
public static class AppServices
{
    public static IFileSystem FileSystem { get; private set; } = null!;

    public static IAppLogger Logger { get; private set; } = null!;

    public static IClock Clock { get; private set; } = null!;

    public static IGameCatalog GameCatalog { get; private set; } = null!;

    public static IThemeResolver ThemeResolver { get; private set; } = null!;

    public static IPlaySetRepository PlaySets { get; private set; } = null!;

    public static IModCatalog Mods { get; private set; } = null!;

    public static IDlcCatalog Dlcs { get; private set; } = null!;

    public static ILoadFileWriter LoadFiles { get; private set; } = null!;

    public static IGameLauncher Launcher { get; private set; } = null!;

    public static IContinueGameReader ContinueGame { get; private set; } = null!;

    public static IDlcSignatureService DlcSignatures { get; private set; } = null!;

    /// <summary>DLC 简介（磁盘缓存 + 可选的 Steam 商店补全）。</summary>
    public static IDlcDescriptionService DlcDescriptions { get; private set; } = null!;

    /// <summary>官方启动器公告卡（Braze content cards；磁盘缓存 + 可选联网）。</summary>
    public static INewsFeedService NewsFeed { get; private set; } = null!;

    public static IMetadataClient Metadata { get; private set; } = null!;

    public static IAssetDownloader Assets { get; private set; } = null!;

    /// <summary>mod 缩略图补全（磁盘缓存 + 可选联网）。</summary>
    public static IModThumbnailService ModThumbnails { get; private set; } = null!;

    public static AppSettingsStore SettingsStore { get; private set; } = null!;

    /// <summary>用户设置的内存副本；改动后调用 <see cref="SaveSettings"/> 落盘。</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>当前选中的游戏；由游戏库页/外壳页设置，其它页面只读。</summary>
    public static GameInstallation? CurrentGame { get; private set; }

    /// <summary>当前游戏变化时触发（参数为新的游戏，可能为 null）。</summary>
    public static event EventHandler<GameInstallation?>? CurrentGameChanged;

    /// <summary>
    /// 某款游戏的主题素材被替换（联网刷新完成）时触发。参数为受影响的 gameId。
    /// 订阅方必须自己 marshal 到 UI 线程（页面统一用 DispatcherQueue.TryEnqueue）。
    /// </summary>
    public static event EventHandler<string>? ThemesChanged;

    /// <summary>通知订阅方：这些游戏的主题已经换过，需要重画。</summary>
    public static void NotifyThemesChanged(string gameId) =>
        ThemesChanged?.Invoke(null, gameId);

    /// <summary>
    /// 设置页改了整窗背景的显示方式时触发（sender 与参数都无意义）。
    /// 订阅方必须自己 marshal 到 UI 线程，主窗口用 DispatcherQueue.TryEnqueue 重画背景。
    /// </summary>
    public static event EventHandler? BackgroundDisplayChanged;

    /// <summary>通知订阅方：背景显示方式变了，需要按新档位重画。</summary>
    public static void NotifyBackgroundDisplayChanged() =>
        BackgroundDisplayChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>冷启动阶段已发现的所有游戏。</summary>
    public static IReadOnlyList<GameInstallation> Games { get; private set; } = Array.Empty<GameInstallation>();

    public static bool IsInitialized { get; private set; }

    /// <summary>当前联网服务里 HttpFetcher 的属主（代理重建时换掉并 Dispose）。</summary>
    private static IDisposable? _networkLifetime;

    public static void Initialize(
        IFileSystem fileSystem,
        IAppLogger logger,
        IClock clock,
        IGameCatalog gameCatalog,
        IThemeResolver themeResolver,
        IPlaySetRepository playSets,
        IModCatalog mods,
        IDlcCatalog dlcs,
        ILoadFileWriter loadFiles,
        IGameLauncher launcher,
        IContinueGameReader continueGame,
        IDlcSignatureService dlcSignatures,
        IDlcDescriptionService dlcDescriptions,
        INewsFeedService newsFeed,
        IMetadataClient metadata,
        IAssetDownloader assets,
        IModThumbnailService modThumbnails,
        AppSettingsStore settingsStore,
        IReadOnlyList<GameInstallation> games,
        IDisposable? networkLifetime = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(gameCatalog);
        ArgumentNullException.ThrowIfNull(themeResolver);
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(dlcs);
        ArgumentNullException.ThrowIfNull(loadFiles);
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(continueGame);
        ArgumentNullException.ThrowIfNull(dlcSignatures);
        ArgumentNullException.ThrowIfNull(dlcDescriptions);
        ArgumentNullException.ThrowIfNull(newsFeed);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(modThumbnails);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(games);

        FileSystem = fileSystem;
        Logger = logger;
        Clock = clock;
        GameCatalog = gameCatalog;
        ThemeResolver = themeResolver;
        PlaySets = playSets;
        Mods = mods;
        Dlcs = dlcs;
        LoadFiles = loadFiles;
        Launcher = launcher;
        ContinueGame = continueGame;
        DlcSignatures = dlcSignatures;
        DlcDescriptions = dlcDescriptions;
        NewsFeed = newsFeed;
        Metadata = metadata;
        Assets = assets;
        ModThumbnails = modThumbnails;
        SettingsStore = settingsStore;
        _networkLifetime = networkLifetime;
        Settings = settingsStore.Load();
        Games = games;
        CurrentGame = SelectDefaultGame(games);
        IsInitialized = true;
    }

    /// <summary>游戏发现完成后回填目录（冷启动先显示窗口，再异步发现；刷新也走这里）。</summary>
    public static void SetGames(IReadOnlyList<GameInstallation> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        Games = games;
        SelectGame(ReconcileCurrentGame(games));
    }

    /// <summary>
    /// 刷新后找回当前游戏：先按对象同一性，再按 (gameId, 安装根)，再按 gameId，
    /// 最后退回默认项；列表为空时返回 null（由 <see cref="SelectGame"/> 清空并通知订阅者）。
    /// 否则 <see cref="CurrentGame"/> 会继续指向刷新前那个已经不在列表里的旧对象。
    /// </summary>
    private static GameInstallation? ReconcileCurrentGame(IReadOnlyList<GameInstallation> games)
    {
        var current = CurrentGame;
        if (current is null)
        {
            return SelectDefaultGame(games);
        }

        return games.FirstOrDefault(g => ReferenceEquals(g, current))
            ?? games.FirstOrDefault(g =>
                string.Equals(g.GameId, current.GameId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.InstallRoot, current.InstallRoot, StringComparison.OrdinalIgnoreCase))
            ?? games.FirstOrDefault(g => string.Equals(g.GameId, current.GameId, StringComparison.OrdinalIgnoreCase))
            ?? SelectDefaultGame(games)
            ?? games.FirstOrDefault();
    }

    /// <summary>
    /// 默认选中的游戏：优先完整适配（能管理内容），其次任何能识别的游戏（仅启动也能启动），
    /// 都没有则 null。这样库里有 CK3 时 Prison Architect 不会抢占默认项。
    /// </summary>
    private static GameInstallation? SelectDefaultGame(IReadOnlyList<GameInstallation> games) =>
        games.FirstOrDefault(static g => g.SupportsContentManagement)
        ?? games.FirstOrDefault(static g => g.IsSupported);

    public static void SelectGame(GameInstallation? game)
    {
        if (ReferenceEquals(CurrentGame, game))
        {
            return;
        }

        CurrentGame = game;
        CurrentGameChanged?.Invoke(null, game);
    }

    /// <summary>立即把设置写盘；失败返回 <see cref="AppResult{T}"/>，调用方据此提示，不再静默当成功。</summary>
    public static AppResult<bool> SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>登记一次延迟写（设置页连续改动时合并成一次落盘），失败只能在延迟到期后记日志。</summary>
    public static void SaveSettingsDeferred() => SettingsStore.SaveDeferred(Settings);

    /// <summary>把尚未执行的延迟写立刻落盘；关闭窗口 / 退出前必须调用，否则最后一次改动会丢。</summary>
    public static AppResult<bool> FlushSettings() => SettingsStore.Flush();

    /// <summary>
    /// 用新构造的联网服务替换旧的（设置页改了代理地址后调用）。
    /// <paramref name="lifetime"> 是旧 HttpFetcher 的属主，换下来之后立即 Dispose（会关掉连接池）。
    /// </summary>
    public static void ReplaceNetwork(
        IMetadataClient metadata,
        IAssetDownloader assets,
        IModThumbnailService modThumbnails,
        IThemeResolver themeResolver,
        IDlcDescriptionService dlcDescriptions,
        INewsFeedService newsFeed,
        IDisposable? lifetime)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(modThumbnails);
        ArgumentNullException.ThrowIfNull(themeResolver);
        ArgumentNullException.ThrowIfNull(dlcDescriptions);
        ArgumentNullException.ThrowIfNull(newsFeed);

        Metadata = metadata;
        Assets = assets;
        ModThumbnails = modThumbnails;
        ThemeResolver = themeResolver;
        DlcDescriptions = dlcDescriptions;
        NewsFeed = newsFeed;

        var previous = Interlocked.Exchange(ref _networkLifetime, lifetime);
        previous?.Dispose();
    }

    /// <summary>
    /// 按当前 <see cref="Settings"/> 里的代理设置重建联网服务。
    /// 失败只记日志、保留旧实例：设置页要能把失败原因显示出来，不能让异常冒到 UI 线程。
    /// </summary>
    /// <returns>结果里只有原因码与细节，界面文案由调用方按语言取（见 <see cref="NetworkRebuildResult"/>）。</returns>
    public static NetworkRebuildResult TryRebuildNetworkServices()
    {
        if (!IsInitialized)
        {
            return NetworkRebuildResult.NotReady;
        }

        try
        {
            var services = NetworkComposition.Create(FileSystem, Logger, Clock, Settings);
            ReplaceNetwork(
                services.Metadata,
                services.Assets,
                services.ModThumbnails,
                services.Themes,
                services.Descriptions,
                services.NewsFeed,
                services.Http);
            Logger.Info("已按新代理设置重建联网服务");
            return NetworkRebuildResult.Ok;
        }
        catch (Exception ex)
        {
            // 重建中途抛异常时，已经构造出来的 HttpFetcher 归还不了（Create 内部的局部变量），
            // 旧实例仍然有效，只是会占着一个连接池，直到进程退出。
            Logger.Error("按新代理设置重建联网服务失败，继续沿用旧链路", ex);
            return NetworkRebuildResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// 本地数据根目录：程序目录\data（与 P 社启动器目录完全隔离，且不写 C 盘）。
    /// 解析规则见 <see cref="PdxDataRoot"/>；旧的 %LOCALAPPDATA%\PdxNext 只读一次用于迁移。
    /// </summary>
    public static string DataRoot => PdxDataRoot.Directory;

    /// <summary>主题素材缓存目录。</summary>
    public static string ThemeCacheRoot => Path.Combine(DataRoot, "theme-cache");

    /// <summary>官方公告缓存目录（news-cards.json 与 images/）。</summary>
    public static string NewsCacheRoot => Path.Combine(DataRoot, "news-cache");

    /// <summary>mod 缩略图缓存目录（mod-thumbnails.json 与 mod-thumbnails/）。</summary>
    public static string ModThumbnailCacheRoot => Path.Combine(DataRoot, "mod-cache");

    /// <summary>本启动器自己的日志路径。</summary>
    public static string LogPath => Path.Combine(DataRoot, "logs", "pdxnext.log");

    private static ThemeImageFlattener? _themeImages;

    /// <summary>
    /// 主题背景摊平服务：把「整幅 alpha 恒定的纱层」图（EU4 的 app-background.png 是 20% 不透明）
    /// 摊平成不透明副本，缓存到 <c>theme-cache\flat\</c>。首次访问时按当前数据根创建。
    /// </summary>
    public static ThemeImageFlattener ThemeImages => _themeImages ??= new ThemeImageFlattener(
        FileSystem,
        Logger,
        Path.Combine(ThemeCacheRoot, "flat"));

    /// <summary>
    /// 卡片封面预摊平：卡片封面是 OneTime 绑定，所以必须在重建卡片**之前**把这些背景准备好。
    /// 只处理纱层图；其它图立刻返回，光速过一遍 7 个游戏也不会卡。
    /// </summary>
    public static async Task PrepareCardCoversAsync(
        IReadOnlyList<GameInstallation> games,
        CancellationToken cancellationToken)
    {
        if (!IsInitialized || games.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var game in games)
        {
            var background = game.Theme.Background;
            if (string.IsNullOrWhiteSpace(background) || !seen.Add(background))
            {
                continue;
            }

            await ThemeImages.PrepareAsync(background, cancellationToken).ConfigureAwait(true);
        }
    }
}
