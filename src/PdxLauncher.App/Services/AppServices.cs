namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation.Local;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Playsets.Share;
using PdxLauncher.Core.Saves;
using PdxLauncher.Core.Storage;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Updates;
/// <summary>
/// 应用级服务定位器。WinUI 的 Frame.Navigate 只支持无参构造，所以页面统一从这里取依赖。
/// 由 App.xaml.cs 在启动时 <see cref="Initialize"/> 一次性装配；之后只读。
/// </summary>
public static class AppServices
{
    /// <summary>卡片封面预摊平的并发上限（见 <see cref="PrepareCardCoversAsync"/>）。</summary>
    private const int CardCoverConcurrency = 4;

    public static IFileSystem FileSystem { get; private set; } = null!;

    public static IAppLogger Logger { get; private set; } = null!;

    public static IClock Clock { get; private set; } = null!;

    public static IGameCatalog GameCatalog { get; private set; } = null!;

    public static IThemeResolver ThemeResolver { get; private set; } = null!;

    public static IPlaySetRepository PlaySets { get; private set; } = null!;

    /// <summary>
    /// 播放集分享（官方「分享 / 导入」JSON）。播放集页与导入结果对话框共用同一个实例。
    /// 在 <see cref="Initialize"/> 里由 <see cref="PlaySets"/> + 文件系统装配，不单独加入参数列表。
    /// </summary>
    public static PlaySetShareService PlaySetShare { get; private set; } = null!;

    public static IModCatalog Mods { get; private set; } = null!;

    public static IDlcCatalog Dlcs { get; private set; } = null!;

    public static ILoadFileWriter LoadFiles { get; private set; } = null!;

    public static IGameLauncher Launcher { get; private set; } = null!;

    public static IContinueGameReader ContinueGame { get; private set; } = null!;

    /// <summary>
    /// 存档库：只读磁盘，按需扫描。启动路径<b>不</b>调用它——存档页打开或点刷新时才扫描，
    /// 避免把「用户 Documents 下有多少文件」变成冷启动耗时。
    /// </summary>
    public static ISaveGameLibrary SaveGames { get; private set; } = null!;

    /// <summary>
    /// 存档页的两段式数据源（先枚举文件、再逐条解析）；与 <see cref="SaveGames"/> 是同一个实例，
    /// 只是暴露更窄的接口，避免页面绕过「先粗略后细节」的顺序。
    /// </summary>
    public static ISaveGameFileIndex SaveGameFiles { get; private set; } = null!;

    /// <summary>
    /// 存档备份：备份 / 恢复 / 删除（含真实存档移入回收站）。写文件全部在这层完成，
    /// 页面只负责确认与刷新；配额由服务判定（2 GiB / 每款游戏 200 条），服务不会自动清理。
    /// </summary>
    public static ISaveGameBackupService SaveBackups { get; private set; } = null!;

    public static IDlcSignatureService DlcSignatures { get; private set; } = null!;

    /// <summary>DLC 简介（磁盘缓存 + 可选的 Steam 商店补全）。</summary>
    public static IDlcDescriptionService DlcDescriptions { get; private set; } = null!;

    /// <summary>DLC 头图（Steam 商店 header_image；磁盘 .dlc 没有图片）。</summary>
    public static IDlcArtworkService DlcArtwork { get; private set; } = null!;

    /// <summary>官方启动器公告卡（Braze content cards；磁盘缓存 + 可选联网）。</summary>
    public static INewsFeedService NewsFeed { get; private set; } = null!;

    public static IMetadataClient Metadata { get; private set; } = null!;

    public static IAssetDownloader Assets { get; private set; } = null!;

    /// <summary>mod 缩略图补全（磁盘缓存 + 可选联网）。</summary>
    public static IModThumbnailService ModThumbnails { get; private set; } = null!;

    /// <summary>Paradox 账号会话（登录 / 续期 / 登出）。</summary>
    public static AccountService Account { get; private set; } = null!;

    /// <summary>PDX Mods 平台客户端（搜索 / 详情，匿名可读）。</summary>
    public static PdxModsClient PdxMods { get; private set; } = null!;

    /// <summary>Steam 创意工坊客户端（列表 / 详情 / 能力探测，匿名 keyless）。</summary>
    public static SteamWorkshopClient SteamWorkshop { get; private set; } = null!;

    /// <summary>创意工坊 mod 信息缓存（按「来源 + id」复用元数据 / 封面 / 详情）。</summary>
    public static WorkshopInfoCache WorkshopInfo { get; private set; } = null!;

    /// <summary>创意工坊列表页的内存缓存（翻页复用，不落盘）。</summary>
    public static WorkshopPageCache WorkshopPages { get; private set; } = null!;

    /// <summary>创意工坊条目页（HTML 解析结果）持久缓存：同一个 mod 在 TTL 内重开不再抓页面。</summary>
    public static SteamItemPageCache WorkshopItemPages { get; private set; } = null!;

    /// <summary>工坊封面/截图目录的磁盘配额（100 MB 上限，超出从最旧的删起）。</summary>
    public static CoverDiskQuota Covers { get; private set; } = null!;

    /// <summary>
    /// 「当前账号在这款游戏下订阅了什么、各自装到哪一步」的内存缓存（按 appid，TTL 1 分钟）。
    /// <para>
    /// 数据来自 <c>--steam-host workshop-subscribed</c> 子进程，代价约一次进程启动，
    /// 所以整库只查一次，翻页 / 排序 / 重画都复用这一份。
    /// </para>
    /// </summary>
    public static WorkshopStateCache WorkshopStates { get; } = new();

    /// <summary>
    /// 出口闸门（按 host 限并发/间隔 + 被 429 后冷却）。
    /// 界面靠它判断「现在是不是在冷却」，从而显示「已暂停 N 分钟」而不是反复撞墙。
    /// </summary>
    public static RequestThrottle HttpThrottle { get; private set; } = null!;

    /// <summary>
    /// 翻译协调器：把当前设置与界面语言翻译成一次翻译任务（引擎选择、目标语言、缓存都在这里收口）。
    /// </summary>
    public static TranslationCoordinator Translation { get; private set; } = null!;

    /// <summary>
    /// 本地离线翻译运行时：模型下载、术语词典与两个本地引擎的容器。
    /// <para>
    /// 没有它也能启动——只是设置页看不到「本地模型」入口，翻译全部走云端引擎。
    /// </para>
    /// </summary>
    public static LocalTranslationRuntime? LocalTranslation { get; private set; }

    /// <summary>游戏目录补丁（一键打补丁 / 移除补丁）。只在用户点按钮时读写游戏目录，不做任何自动变更。</summary>
    public static IGamePatchService GamePatch { get; private set; } = null!;

    /// <summary>
    /// DLC 授权判定：加载游戏目录里那份 steam_api DLL，问 Steam「这些 DLC 授权了吗」。
    /// 补丁生效时它报告的授权就是游戏实际看到的授权，据此判断补丁有没有用上。
    /// </summary>
    public static ISteamOwnershipService DlcOwnership { get; private set; } = null!;

    /// <summary>
    /// 成就数据源：走「Steam 宿主子进程」读真实成就（appid 未知 / Steam 不可用 / 没有成就
    /// 分别对应不同的空状态）。原生调用只在子进程里发生，主进程不碰 Steamworks。
    /// <para>
    /// 每次读取都是一次性的 <c>--steam-host achievements</c> 进程：读完即退，Steam 侧的
    /// 「游戏正在运行」登记随进程退出消失。这里刻意不保留常驻 worker、也不做启动 / 切游戏
    /// 预热 —— Steam 会把调过 <c>SteamAPI_Init</c> 的进程登记成该游戏的 game process，
    /// 常驻会话会让 Steam 在两次读取之间一直把游戏显示成「运行中」。
    /// </para>
    /// </summary>
    public static IAchievementSource Achievements { get; private set; } = new NullAchievementSource();

    public static AppSettingsStore SettingsStore { get; private set; } = null!;

    /// <summary>用户设置的内存副本；改动后调用 <see cref="SaveSettings"/> 落盘。</summary>
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>
    /// 按设置页里为该游戏选择的「其它可执行文件」构造启动请求；
    /// 没选或选的是标准 exe 时返回默认请求（让 <see cref="IGameLauncher"/> 读 launcher-settings 的参数）。
    /// </summary>
    /// <param name="game">要启动的游戏。</param>
    /// <param name="continueLastSave">是否追加 --continuelastsave。</param>
    public static LaunchRequest CreateLaunchRequest(GameInstallation game, bool continueLastSave)
    {
        ArgumentNullException.ThrowIfNull(game);

        string? customPath = null;
        if (Settings.PreferredLaunchExePaths.TryGetValue(game.GameId, out var preferred)
            && !string.IsNullOrWhiteSpace(preferred)
            && !string.Equals(preferred, game.ExePath, StringComparison.OrdinalIgnoreCase))
        {
            customPath = preferred;
        }

        IReadOnlyList<string>? customArgs = null;
        if (customPath is not null)
        {
            var alternative = game.Alternatives.FirstOrDefault(
                item => string.Equals(item.ExePath, customPath, StringComparison.OrdinalIgnoreCase));
            customArgs = alternative?.Args;
        }

        return new LaunchRequest(customPath, customArgs, continueLastSave);
    }

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

    /// <summary>本启动器自己的 SQLite 库（唯一事实源；官方库缺席时开发播放集与 Mod 库的唯一来源）。</summary>
    public static PdxDatabase Database { get; private set; } = null!;

    /// <summary>安装包更新：检查 GitHub Releases、下载并校验、按安装模式启动安装器。</summary>
    public static UpdateService Updates { get; private set; } = null!;

    /// <summary>整份 JSON 文档存储（设置、登录态、各缓存索引都落在 <see cref="Database"/> 里）。</summary>
    public static IJsonDocumentStore Documents { get; private set; } = null!;

    public static bool IsInitialized { get; private set; }

    /// <summary>
    /// UI 线程卡顿看门狗；窗口建好后由 <see cref="App"/> 启动，退出时为 null。
    /// 关闭窗口时汇总本次会话的停顿次数与最长停顿写进日志，业务代码用 <see cref="Breadcrumb"/> 留位置。
    /// </summary>
    public static UiThreadWatchdog? Watchdog { get; private set; }

    /// <summary>接上看门狗（启动时调用一次）；传 null 表示停用。</summary>
    /// <param name="watchdog">已启动的看门狗。</param>
    public static void AttachWatchdog(UiThreadWatchdog? watchdog) => Watchdog = watchdog;

    /// <summary>
    /// 记一条「现在走到哪一步」的面包屑；看门狗没启动时静默忽略。
    /// 只在可能出现长耗时的阶段边界调用，别放进循环内层。
    /// </summary>
    /// <param name="message">简短位置描述。</param>
    public static void Breadcrumb(string message) => Watchdog?.Breadcrumb(message);

    /// <summary>
    /// 当前联网连接池的属主。<b>改代理时原地换池，不换服务对象</b>：
    /// 页面在构造函数里缓存了 <see cref="SteamWorkshop"/> / <see cref="PdxMods"/> 等实例，
    /// 换对象会让这些引用指向已释放的 HttpClient（实测 <c>ObjectDisposedException</c>）。
    /// </summary>
    private static NetworkLink? _networkLink;

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
        IDlcArtworkService dlcArtwork,
        INewsFeedService newsFeed,
        IMetadataClient metadata,
        IAssetDownloader assets,
        IModThumbnailService modThumbnails,
        AccountService account,
        PdxModsClient pdxMods,
        SteamWorkshopClient steamWorkshop,
        WorkshopInfoCache workshopInfo,
        WorkshopPageCache workshopPages,
        SteamItemPageCache workshopItemPages,
        RequestThrottle httpThrottle,
        TranslationCoordinator translation,
        IGamePatchService gamePatch,
        ISteamOwnershipService dlcOwnership,
        AppSettingsStore settingsStore,
        AppSettings settings,
        PdxDatabase database,
        IJsonDocumentStore documents,
        UpdateService updates,
        IReadOnlyList<GameInstallation> games,
        NetworkLink? networkLink = null,
        LocalTranslationRuntime? localTranslation = null)
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
        ArgumentNullException.ThrowIfNull(dlcArtwork);
        ArgumentNullException.ThrowIfNull(newsFeed);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(modThumbnails);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(pdxMods);
        ArgumentNullException.ThrowIfNull(steamWorkshop);
        ArgumentNullException.ThrowIfNull(workshopInfo);
        ArgumentNullException.ThrowIfNull(workshopPages);
        ArgumentNullException.ThrowIfNull(workshopItemPages);
        ArgumentNullException.ThrowIfNull(httpThrottle);
        ArgumentNullException.ThrowIfNull(translation);
        ArgumentNullException.ThrowIfNull(gamePatch);
        ArgumentNullException.ThrowIfNull(dlcOwnership);
        ArgumentNullException.ThrowIfNull(settingsStore);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(updates);
        ArgumentNullException.ThrowIfNull(games);

        FileSystem = fileSystem;
        Logger = logger;
        Clock = clock;
        GameCatalog = gameCatalog;
        ThemeResolver = themeResolver;
        PlaySets = playSets;
        PlaySetShare = new PlaySetShareService(playSets, fileSystem, logger);
        Mods = mods;
        Dlcs = dlcs;
        LoadFiles = loadFiles;
        Launcher = launcher;
        ContinueGame = continueGame;
        var saveGames = new SaveGameScanner(fileSystem, logger);
        SaveGames = saveGames;
        SaveGameFiles = saveGames;
        SaveBackups = new SaveBackupService(
            fileSystem,
            logger,
            maxTotalBytes: settings.SaveBackupMaxTotalBytes,
            autoPruneOldestWhenFull: true);
        DlcSignatures = dlcSignatures;
        DlcDescriptions = dlcDescriptions;
        DlcArtwork = dlcArtwork;
        NewsFeed = newsFeed;
        Metadata = metadata;
        Assets = assets;
        ModThumbnails = modThumbnails;
        Account = account;
        PdxMods = pdxMods;
        SteamWorkshop = steamWorkshop;
        WorkshopInfo = workshopInfo;
        WorkshopPages = workshopPages;
        WorkshopItemPages = workshopItemPages;
        Covers = new CoverDiskQuota(fileSystem, logger, clock, WorkshopCacheRoot);
        HttpThrottle = httpThrottle;
        Translation = translation;
        LocalTranslation = localTranslation;
        GamePatch = gamePatch;
        DlcOwnership = dlcOwnership;
        // 成就读取一律走一次性宿主：拉起 `--steam-host achievements`、读一次、进程退出，
        // Steam 侧的「游戏正在运行」登记随进程退出消失。这里刻意不保留常驻 worker、
        // 也不做启动 / 切游戏预热 —— 两者都会让 Steam 在用户没启动游戏时显示「运行中」。
        var steamHostClient = new SteamHostClient(logger);
        Achievements = new SteamAchievementSource(
            steamHostClient,
            new SteamApiLocator(fileSystem, logger),
            fileSystem,
            logger,
            () => Settings.SteamApiDllPath,
            () => CurrentGame,
            () => PdxLauncher.App.Localization.Localizer.Instance.Language);
        SettingsStore = settingsStore;
        Database = database;
        Documents = documents;
        Settings = settings;
        Updates = updates;
        if (!ReferenceEquals(updates.Settings, settings))
        {
            Logger.Error("UpdateService 与 AppServices.Settings 不是同一实例，更新检查时间与自动检查开关会不同步");
        }
        _networkLink = networkLink;
        Games = games;
        CurrentGame = SelectDefaultGame(games);
        IsInitialized = true;
    }

    /// <summary>游戏发现完成后回填目录（冷启动先显示窗口，再异步发现；刷新也走这里）。</summary>
    /// <remarks>
    /// 只有「当前游戏真的换了」才广播 <see cref="CurrentGameChanged"/>。
    /// 后台主题刷新每换一款素材都会调一次本方法，而 <c>ThemePatch.Apply</c> / 重排序会产生新的
    /// <see cref="GameInstallation"/> 实例 —— 之前这里无条件走 <see cref="SelectGame"/>，
    /// <c>ReferenceEquals</c> 判不出「其实是同一款」，于是没有用户操作也会触发六个页面全量重载。
    /// </remarks>
    public static void SetGames(IReadOnlyList<GameInstallation> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        Games = games;

        var next = ReconcileCurrentGame(games);
        var current = CurrentGame;
        if (current is not null
            && next is not null
            && string.Equals(current.GameId, next.GameId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(current.InstallRoot, next.InstallRoot, StringComparison.OrdinalIgnoreCase)
            && current.Theme == next.Theme)
        {
            // 同一款游戏、主题也没变：只是列表换了实例或顺序，静默换引用即可。
            CurrentGame = next;
            return;
        }

        SelectGame(next);
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
    /// 默认选中的游戏：优先「上次选中的那款」，认不出来才退回
    /// 「能管理内容的优先 → 能识别的 → 第一个」，都没有则 null。
    /// 这样库里有 CK3 时 Prison Architect 不会抢占默认项，重启也能回到用户上次的选择。
    /// </summary>
    private static GameInstallation? SelectDefaultGame(IReadOnlyList<GameInstallation> games) =>
        GameSelection.Select(games, Settings.LastGameId, Settings.LastGameInstallRoot);

    public static void SelectGame(GameInstallation? game)
    {
        if (ReferenceEquals(CurrentGame, game))
        {
            return;
        }

        CurrentGame = game;

        // 记住用户的选择：Initialize 之前 SettingsStore 还是 null!，此时不能写盘。
        // game 为 null（库被清空）时保留旧记忆，别把用户的偏好抹掉。
        if (game is not null && IsInitialized)
        {
            Settings.LastGameId = game.GameId;
            Settings.LastGameInstallRoot = game.InstallRoot;
            SaveSettingsDeferred();
        }

        CurrentGameChanged?.Invoke(null, game);

        // 刻意不在这里预热成就：预热会立刻拉起 Steam 宿主并调 SteamAPI_Init，
        // Steam 会把当前游戏登记成「正在运行」，而用户并没有启动游戏。
        // 成就只在用户打开成就页时按需读取，且每次都是一次性宿主，读完即退。
    }

    /// <summary>立即把设置写盘；失败返回 <see cref="AppResult{T}"/>，调用方据此提示，不再静默当成功。</summary>
    public static AppResult<bool> SaveSettings() => SettingsStore.Save(Settings);

    /// <summary>登记一次延迟写（设置页连续改动时合并成一次落盘），失败只能在延迟到期后记日志。</summary>
    public static void SaveSettingsDeferred() => SettingsStore.SaveDeferred(Settings);

    /// <summary>把尚未执行的延迟写立刻落盘；关闭窗口 / 退出前必须调用，否则最后一次改动会丢。</summary>
    public static AppResult<bool> FlushSettings() => SettingsStore.Flush();

    /// <summary>
    /// 把各缓存攒着的防抖写入立刻落盘；关闭窗口前调用，否则最后几百毫秒的缓存会丢。
    /// 缓存写失败不影响退出，所以这里不返回结果。
    /// </summary>
    public static void FlushCaches()
    {
        if (!IsInitialized)
        {
            return;
        }

        DebouncedDocumentFile.FlushAll();
        DebouncedJsonDocumentStore.FlushAll();
    }

    /// <summary>
    /// 按当前 <see cref="Settings"/> 里的代理设置重建联网连接池（不换服务对象）。
    /// <para>
    /// 只调用两条连接的 <c>UseProxy</c>：客户端实例保持不变，页面构造函数里缓存的引用继续有效；
    /// 旧连接池进退休队列延迟释放，在途请求不会被打断。
    /// </para>
    /// 失败只记日志、保留旧连接池：设置页要能把失败原因显示出来，不能让异常冒到 UI 线程。
    /// </summary>
    /// <returns>结果里只有原因码与细节，界面文案由调用方按语言取（见 <see cref="NetworkRebuildResult"/>）。</returns>
    public static NetworkRebuildResult TryRebuildNetworkServices()
    {
        if (!IsInitialized)
        {
            return NetworkRebuildResult.NotReady;
        }

        if (_networkLink is null)
        {
            // 装配时没交连接池属主（只有测试/特殊启动会这样），无从换起。
            Logger.Warn("联网服务尚未登记连接池属主，无法按代理设置重建");
            return NetworkRebuildResult.NotReady;
        }

        try
        {
            var proxy = ProxyResolver.Resolve(Settings, Logger);
            _networkLink.Http.UseProxy(proxy);
            _networkLink.PdxHttp.UseProxy(proxy);
            _networkLink.TranslationHttp.UseProxy(proxy);
            Logger.Info("已按新代理设置重建联网连接池");
            return NetworkRebuildResult.Ok;
        }
        catch (Exception ex)
        {
            // 换池失败时旧连接池仍在用，功能不受影响，只是代理改动没生效。
            Logger.Error("按新代理设置重建联网连接池失败，继续沿用旧连接池", ex);
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

    /// <summary>DDS 缩略图转好的 PNG 缓存目录（image-cache/）。</summary>
    public static string ImageCacheRoot => Path.Combine(DataRoot, "image-cache");

    /// <summary>
    /// 创意工坊 mod 信息缓存根目录（索引 workshop-info.json + 封面 {steam|pdx}/{id}.*）。
    /// 与本地 mod 缩略图缓存分开：那边的键是 <see cref="System.Guid"/>（mods.id），
    /// 创意工坊结果用的是 Steam publishedfileid / PDX Mods 的数字 id，两套 id 空间不能混在一个目录里。
    /// </summary>
    public static string WorkshopCacheRoot => Path.Combine(DataRoot, "workshop-info");

    /// <summary>
    /// 工坊筛选目录的落盘位置（<c>workshop-filters\{appid}.json</c>）。
    /// 目录随 Steam 列表页 SSR 更新，按 appid 存最近一次快照，重启/离线时先显示已知最新版本。
    /// </summary>
    public static string WorkshopFilterRoot => Path.Combine(DataRoot, "workshop-filters");

    /// <summary>本启动器自己的日志路径。</summary>
    public static string LogPath => Path.Combine(DataRoot, "logs", "pdxnext.log");

    private static GameCrashAnalyzer? _crashAnalysis;

    /// <summary>
    /// 游戏崩溃分析器：只读扫描游戏崩溃包/日志，不联网、不写游戏目录。
    /// 首次访问时创建；依赖 FileSystem / LoadFiles / Logger / Clock 都是已装配好的只读服务。
    /// </summary>
    public static GameCrashAnalyzer CrashAnalysis => _crashAnalysis ??= new GameCrashAnalyzer(
        FileSystem,
        LoadFiles,
        Logger,
        Clock);

    private static ThemeImageFlattener? _themeImages;

    /// <summary>
    /// 主题背景摊平服务：把「整幅 alpha 恒定的纱层」图（EU4 的 app-background.png 是 20% 不透明）
    /// 摊平成不透明副本，缓存到 <c>theme-cache\flat\</c>。首次访问时按当前数据根创建。
    /// </summary>
    public static ThemeImageFlattener ThemeImages => _themeImages ??= new ThemeImageFlattener(
        FileSystem,
        Logger,
        Path.Combine(ThemeCacheRoot, "flat"));

    private static ImageArtCache? _imageArt;

    /// <summary>
    /// DDS 缩略图显示缓存：把 <c>thumbnail.dds</c> 转成 PNG 存进 <c>image-cache\</c>，
    /// 因为 WinUI 的 BitmapImage 解不了 DDS。首次访问时按当前数据根创建。
    /// </summary>
    public static ImageArtCache ImageArt => _imageArt ??= new ImageArtCache(
        FileSystem,
        Logger,
        ImageCacheRoot);

    /// <summary>
    /// 卡片封面预摊平：卡片封面是 OneTime 绑定，所以必须在重建卡片**之前**把这些背景准备好。
    /// 只处理纱层图；其它图走磁盘否定标记，光速过一遍 9 个游戏也不会卡。
    /// </summary>
    /// <remarks>
    /// 并发摊平（上限 <see cref="CardCoverConcurrency"/>）：这些图从 1 MB 到 6 MB 不等，
    /// 串行时首屏要等最慢的那张；而摊平本身已经全在线程池上跑（见 <see cref="ThemeImageFlattener"/>），
    /// 并发度只受磁盘顺序读限制。解码内存峰值 = 并发数 × 单图 RGBA，4 张 2560×1340 约 55 MB，可接受。
    /// </remarks>
    public static async Task PrepareCardCoversAsync(
        IReadOnlyList<GameInstallation> games,
        CancellationToken cancellationToken)
    {
        if (!IsInitialized || games.Count == 0)
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var backgrounds = new List<string>();
        foreach (var game in games)
        {
            var background = game.Theme.Background;
            if (!string.IsNullOrWhiteSpace(background) && seen.Add(background))
            {
                backgrounds.Add(background);
            }
        }

        if (backgrounds.Count == 0)
        {
            return;
        }

        using var gate = new SemaphoreSlim(CardCoverConcurrency, CardCoverConcurrency);
        var work = new Task[backgrounds.Count];
        for (var i = 0; i < backgrounds.Count; i++)
        {
            var background = backgrounds[i];
            work[i] = PrepareOneAsync(background);
        }

        await Task.WhenAll(work).ConfigureAwait(true);
        return;

        async Task PrepareOneAsync(string background)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ThemeImages.PrepareAsync(background, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
