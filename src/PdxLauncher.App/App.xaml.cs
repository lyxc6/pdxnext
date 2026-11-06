namespace PdxLauncher.App;

using Microsoft.UI.Xaml;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Launching;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Online.Translation.Local;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;
using PdxLauncher.Core.Playsets.PlaysetConfig;
using PdxLauncher.Core.Signatures;
using PdxLauncher.Core.Storage;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Updates;

/// <summary>
/// 应用入口。启动阶段只做三件事：手工装配服务、创建主窗口、激活窗口。
/// 冷启动路径上不允许 await 任何网络或大文件 IO —— 游戏发现由各页面在窗口显示后异步触发。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    /// <summary>
    /// 当前主窗口。崩溃处理路径（<see cref="Services.CrashReporter"/>）需要它来拿 XamlRoot 弹框，
    /// 而那条路径拿不到注入的服务，所以这里只读暴露一个静态入口。
    /// </summary>
    internal static Window? MainWindowInstance { get; private set; }

    /// <summary>崩溃处理路径的重入闩：弹框/写盘过程中再次抛异常时直接放弃，避免无限递归。</summary>
    private static bool _handlingCrash;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        // WinUI 的 UnhandledException 只覆盖 UI 线程。后台线程与未观察的 Task 异常
        // 会让进程直接退出（用户看到的是「启动器自己关了」），所以这两处也要留痕。
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ComposeServices();
        StartupTimeline.Mark(StartupTimeline.ServicesReady);

        // 界面语言必须在任何页面 XAML 加载前生效，否则首屏会闪一下默认语言。
        Localizer.Instance.Apply(AppServices.Settings.UiLanguage);
        AppServices.Logger.Info("PdxNext 启动，创建主窗口（冷启动不联网）");

        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();

        // UI 线程看门狗：任何页面上的同步长活（大文件加载、同步 IO/解码）都会在这里留下
        // 「停顿 N ms + 最近位置」，比窗口卡死后什么都抓不到要强。
        AppServices.AttachWatchdog(UiThreadWatchdog.Start(AppServices.Logger, _window.DispatcherQueue));
        StartupTimeline.Mark(StartupTimeline.WindowCreated);

        // 窗口已经可见：把当前游戏的成就读取挪到后台空闲时做，用户点成就页时直接命中快照。
        // 只调度、不等待；失败只记日志（预热服务内部已兜住所有异常）。
        AppServices.StartAchievementPrewarm();
    }

    /// <summary>手工构造服务图并交给 <see cref="AppServices"/>，不使用任何 DI 容器。</summary>
    private static void ComposeServices()
    {
        if (AppServices.IsInitialized)
        {
            return;
        }

        IFileSystem fileSystem = new FileSystem();
        var logger = new FileLogger(AppServices.LogPath);
        IClock clock = SystemClock.Instance;

        // 数据根目录固定在程序目录下（见 PdxDataRoot）：首次启动把旧的 %LOCALAPPDATA%\PdxNext
        // 只读复制过来。迁移失败只记日志，绝不阻塞启动，也绝不删除源目录。
        // 注意：下面的 IsPerMachineInstall 只决定「数据目录选址与迁移来源」，属于数据语义，
        // 不是安装形态判定；更新时是否传 /ALLUSERS 统一走 InstallModeResolver
        // （见 UpdateService.ShouldUseAllUsers），两处口径不得互相借用。
        var migration = PdxDataRoot.IsPerMachineInstall(AppContext.BaseDirectory)
            ? DataRootMigrator.MigrateFirstAvailable(
                fileSystem,
                logger,
                AppServices.DataRoot,
                DataRootMigrator.PerUserInstallDataRoot,
                DataRootMigrator.LegacyRoot)
            : new DataRootMigrator(fileSystem, logger, DataRootMigrator.LegacyRoot, AppServices.DataRoot).Migrate();
        if (migration.Migrated)
        {
            logger.Info("旧数据目录已迁移：" + migration.FilesCopied + " 个文件 / " + migration.BytesCopied + " B");
        }

        // 清掉「改过路径 / 改过命名」之后留下的旧缓存：data\workshop-covers\、data\online-mod-covers\
        // 与工坊详情 shots\ 下 shot_NN 形式的旧截图。这三处当前代码已经完全不引用，但设置页的
        // 缓存卡片也只认现行路径，没有任何入口能删它们，所以放在启动阶段跑一遍（幂等，无货可删就什么都不做）。
        var legacyCleanup = new LegacyCacheCleaner(
            fileSystem,
            logger,
            AppServices.DataRoot,
            AppServices.WorkshopCacheRoot).Run();
        if (legacyCleanup.HasWork)
        {
            logger.Info(
                "已清理遗留缓存："
                + legacyCleanup.Directories + " 个目录 / "
                + legacyCleanup.Files + " 个文件 / "
                + legacyCleanup.Bytes + " B");
        }

        // 自有库先立起来：它是本启动器唯一的事实源（设置、登录态、缓存索引、mod 库与播放集），
        // 官方 launcher-v2.sqlite 缺席时更是唯一数据源。建库失败后续各服务各自降级，不阻断启动。
        var database = new PdxDatabase(fileSystem, logger, PdxDatabase.GetDefaultPath());
        var documents = new SqliteJsonDocumentStore(database, logger, clock);

        // 「数据全部入库」之前遗留的整份 JSON（settings.json / pdx-session.json / 6 份缓存索引）搬进库，
        // 原文件改名成 *.migrated.bak 留档；重复执行什么也不做。
        new LegacyJsonImporter(database, fileSystem, documents, logger).Import(AppServices.DataRoot);

        // 先读设置，代理模式与地址来自 settings.json；后续所有服务共用这一实例。
        var settingsStore = new AppSettingsStore(documents, logger);
        var settings = settingsStore.Load();

        // 联网链路（HttpFetcher → 元数据 / 素材 / 主题 / DLC 简介）集中在 NetworkComposition，
        // 设置页改代理后只换连接池、不换服务对象（见 AppServices.TryRebuildNetworkServices）。
        var network = NetworkComposition.Create(fileSystem, logger, clock, settings, documents, database);

        var gameCatalog = new GameCatalog(fileSystem, logger);

        // 缩略图解析器共享给两个仓储：Steam 库列表只在首次需要时探测一次；
        // 同时挂上网盘缓存，本地目录全 miss 时直接读已下载的图片（不需要再发请求）。
        var modThumbnails = new ModThumbnailResolver(fileSystem, logger, steamLibraries: null, cache: network.ModThumbnails.Cache);
        var clausewitzMods = new ModCatalog(fileSystem, logger, modThumbnails);
        var pdxMods = new PdxModsCatalog(fileSystem, logger);

        // CS2 的 mod 不在 *.mod / .metadata 体系里，而在 .cache\Mods\pdx_mods\<id>_<ver>\；
        // 路由与播放集仓储同一套思路，上层（ModLibraryService）不需要判格式。
        var mods = new ModCatalogRouter(clausewitzMods, pdxMods);
        var dlcs = new DlcCatalog(fileSystem, logger);

        // EU5 不写 launcher-v2.sqlite，只认文档目录下的 playsets.json；CS2 则是 .cache\Mods\playset_config.json；
        // 其余游戏仍用 sqlite。判定收敛在路由里（GameInstallation.Format），上层服务不需要知道格式差异。
        // 传 fileSystem 是为了兜底：官方哪天把某个 sqlite 游戏迁到 JSON 存储，数据库文件消失时按 JSON 读，
        // 而不是让播放集页静默空白。
        var sqlitePlaySets = new PlaySetRepository(fileSystem, logger, clock, modThumbnails);
        var jsonPlaySets = new PlaysetsJsonRepository(fileSystem, logger, clausewitzMods, dlcs);
        var playsetConfig = new PlaysetConfigRepository(fileSystem, logger, clock);

        // 自有库（data\pdxnext.sqlite）：官方 launcher-v2.sqlite 不在时接管 sqlite 格式的游戏，
        // 在的时候由 LauncherDbProjector 两边同步（读官方、写官方后镜像一份进自有库）。
        var ownPlaySets = new OwnDatabasePlaySetRepository(database, logger, clock);
        var projector = new LauncherDbProjector(fileSystem, sqlitePlaySets, ownPlaySets, database, logger);
        var playSets = new PlaySetRepositoryRouter(
            sqlitePlaySets,
            jsonPlaySets,
            playsetConfig,
            fileSystem,
            logger,
            ownPlaySets,
            projector);
        var loadFiles = new LoadFileWriter(fileSystem, logger);
        var processLauncher = new ProcessLauncher(logger);
        var launcher = new GameLauncher(processLauncher, fileSystem, logger);
        var continueGame = new ContinueGameReader(fileSystem, logger);
        var dlcSignatures = new DlcSignatureService(fileSystem, logger);

        // 补丁素材：内嵌进 Core 的资源是兜底，程序目录下的 patch\assets 可整体替换（换新整合包不用重编译）。
        var patchAssets = new PatchAssets(fileSystem, logger, Path.Combine(PdxDataRoot.Directory, "patch", "assets"));
        var gamePatch = new GamePatchService(
            fileSystem, logger, patchAssets, Path.Combine(PdxDataRoot.Directory, "patch", "state.json"));

        // DLC 授权判定：问 Steam「这些 DLC 授权了吗」。原生 Steamworks 调用放在短命子进程
        // （PdxLauncher.App.exe --steam-host probe）里做：只要本进程自己调 SteamAPI_Init，
        // Steam 就会把启动器登记成「游戏正在运行」，并在点「退出游戏」时把启动器杀掉。
        // 补丁生效时它报的就是游戏实际看到的授权，所以这是判断「补丁有没有用上」的直接证据。
        // 库目录（找 appmanifest 里的 DisabledDLC）按需读注册表，读一次就复用。
        IReadOnlyList<string>? steamLibraries = null;
        var dlcOwnership = new SteamOwnershipService(
            fileSystem,
            logger,
            gamePatch,
            () => steamLibraries ??= new SteamLibraryLocator(fileSystem, logger).FindLibraries(),
            new SteamHostProbe(logger));

        // 本地离线翻译：模型目录、术语词典与下载器都在这里；模型本身要用户点「下载」才拉，
        // 启动阶段不碰网络。翻译协调器拿到它之后，「本地离线翻译」模式才可用。
        var localTranslation = new LocalTranslationRuntime(fileSystem, logger, network.Http);

        // 翻译协调器：引擎随设置临时构建，这里只持传输、缓存与「设置变了就作废在途批次」的状态。
        var translation = new TranslationCoordinator(
            network.TranslationTransport,
            network.TranslationCache,
            logger,
            localTranslation);

        // Paradox 账号：会话落盘（DPAPI 保护）与登录客户端分开，代理重建时只换客户端。
        var secretProtector = new DpapiSecretProtector(logger);
        var sessionStore = new PdxSessionStore(documents, logger, secretProtector);
        var account = new AccountService(logger, sessionStore, clock, network.Account);

        // 更新检查复用同一条 HttpFetcher：换代理时连接池原地替换，更新服务不需要重建。
        // 每用户安装把安装包缓存在 data\update-cache；每机器安装时 DataRoot 已在
        // %LOCALAPPDATA%\PdxNext-Data，同样可写。
        var updates = new UpdateService(
            network.Http,
            logger,
            settings,
            Path.Combine(AppServices.DataRoot, "update-cache"),
            AppContext.BaseDirectory,
            UpdateService.ReadCurrentVersion(),
            settingsStore,
            uninstallEntries: new RegistryUninstallEntrySource(logger));

        // 最后一个参数先给空列表：窗口先出来，游戏发现由 GameDiscovery 在 UI 就绪后补。
        AppServices.Initialize(
            fileSystem,
            logger,
            clock,
            gameCatalog,
            network.Themes,
            playSets,
            mods,
            dlcs,
            loadFiles,
            launcher,
            continueGame,
            dlcSignatures,
            network.Descriptions,
            network.Artwork,
            network.NewsFeed,
            network.Metadata,
            network.Assets,
            network.ModThumbnails,
            account,
            network.PdxMods,
            network.SteamWorkshop,
            network.WorkshopInfo,
            network.WorkshopPages,
            network.WorkshopItemPages,
            network.Throttle,
            translation,
            gamePatch,
            dlcOwnership,
            settingsStore,
            settings,
            database,
            documents,
            updates,
            Array.Empty<GameInstallation>(),
            new NetworkLink(network.Http, network.PdxHttp, network.TranslationTransport),
            localTranslation);

        logger.Info("服务装配完成");

        // 页面状态（搜索词/筛选/滚动/表单草稿）跨重启保留：先读回上次的位置，页面创建时直接用。
        PageStateStore.Attach(documents, logger);

        // Release 也留一行内存快照：「性能与日志」页与基线文档都靠它判断是不是这次启动变胖了。
        MemoryDiagnostics.Log(
            logger,
            "启动完成",
            () => (AppServices.WorkshopInfo.Count, AppServices.WorkshopItemPages.Count, AppServices.ModThumbnails.Cache.Count));

        // 封面目录只增不减（实测能到 200 MB+），启动时在后台按 100 MB 配额扫一轮。
        AppServices.Covers.SweepInBackground();

        // 翻译是否开着一句话就能看出来：这个功能失败是静默的，日志里得留下判断依据。
        var translationOptions = TranslationOptions.FromSettings(settings, Localizer.Instance.Language);
        if (translationOptions.Enabled)
        {
            logger.Info(
                "翻译已启用：模式=" + translationOptions.EngineMode
                + "，目标语言=" + translationOptions.TargetLanguage
                + "，自填 API=" + (translationOptions.HasCustomConfiguration ? "已配置" : "未配置"));
        }
        else
        {
            logger.Info("翻译未启用");
        }

        // 会话恢复必须在启动阶段跑：账号页与创意工坊提示条都依赖 Account.IsSignedIn，
        // 而旧实现只在进设置页时才续期，冷启动会误报未登录。
        _ = RestoreAccountSessionAsync();
    }

    /// <summary>
    /// 冷启动恢复 Paradox 会话（记住我时用 refresh token 续期）。
    /// 失败只记日志：启动流程不能因为账号服务不可用而中断。
    /// </summary>
    private static async Task RestoreAccountSessionAsync()
    {
        try
        {
            await AppServices.Account.RestoreAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppServices.Logger.Warn("启动时恢复 Paradox 会话失败: " + ex.Message);
        }
    }

    /// <summary>
    /// UI 线程未处理异常。这里是「从零崩溃可观测性到能定位」的落点：
    /// 写崩溃记录 + 弹可复制错误框 + <c>Handled = true</c> 让进程活下来。
    /// <para>
    /// 置 <c>Handled</c> 的取舍：不置的话 WinUI 会直接终止进程，用户只看到窗口消失、
    /// 日志里可能什么都没有。置了之后界面可能处于半损坏状态，但至少用户能复制错误、
    /// 能看到崩溃记录路径，而不是无迹可寻。所以只在<b>已经成功写下崩溃记录</b>时才置。
    /// </para>
    /// </summary>
    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (_handlingCrash)
        {
            // 弹框 / 记录过程中再次抛异常：放弃处理，让系统走它自己的终止流程。
            return;
        }

        _handlingCrash = true;
        try
        {
            var context = AppServices.IsInitialized ? "game=" + (AppServices.CurrentGame?.GameId ?? "(none)") : "startup";
            var path = Services.CrashReporter.Record("未处理的异常: " + e.Message, e.Exception, context);
            if (path is not null)
            {
                e.Handled = true;
                Services.CrashReporter.ShowDialog("未处理的异常: " + e.Message, e.Exception, path);
            }
        }
        finally
        {
            _handlingCrash = false;
        }
    }

    /// <summary>
    /// 非 UI 线程的未处理异常。.NET 会在这之后直接终止进程，改不了这个结果，
    /// 唯一能做的是<b>在死之前把现场留下来</b>。
    /// </summary>
    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        if (_handlingCrash)
        {
            return;
        }

        _handlingCrash = true;
        try
        {
            Services.CrashReporter.Record(
                "后台线程未处理异常（进程即将终止）",
                e.ExceptionObject as Exception,
                "terminating=" + e.IsTerminating);
        }
        catch
        {
            // 进程已经在死，不能再抛。
        }
        finally
        {
            _handlingCrash = false;
        }
    }

    /// <summary>未观察的 Task 异常：默认会被 GC 静默吞掉，这里至少留一条记录。</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Services.CrashReporter.Record("未观察的 Task 异常", e.Exception, null);
        }
        catch
        {
            // 记录失败就算了，这条路径本来就不该影响运行。
        }
    }
}