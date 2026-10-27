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
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;
using PdxLauncher.Core.Playsets.PlaysetConfig;
using PdxLauncher.Core.Signatures;

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

        // 界面语言必须在任何页面 XAML 加载前生效，否则首屏会闪一下默认语言。
        Localizer.Instance.Apply(AppServices.Settings.UiLanguage);
        AppServices.Logger.Info("PdxNext 启动，创建主窗口（冷启动不联网）");

        _window = new MainWindow();
        MainWindowInstance = _window;
        _window.Activate();
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
        var migration = new DataRootMigrator(fileSystem, logger, DataRootMigrator.LegacyRoot, AppServices.DataRoot).Migrate();
        if (migration.Migrated)
        {
            logger.Info("旧数据目录已迁移：" + migration.FilesCopied + " 个文件 / " + migration.BytesCopied + " B");
        }

        // 先读设置，代理模式与地址来自 settings.json；AppServices.Initialize 内部会再读一次（幂等）。
        var settingsStore = new AppSettingsStore(fileSystem, logger, AppSettingsStore.DefaultPath);
        var settings = settingsStore.Load();

        // 联网链路（HttpFetcher → 元数据 / 素材 / 主题 / DLC 简介）集中在 NetworkComposition，
        // 设置页改代理后可以整条重建（见 AppServices.TryRebuildNetworkServices）。
        var network = NetworkComposition.Create(fileSystem, logger, clock, settings);

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
        var sqlitePlaySets = new PlaySetRepository(fileSystem, logger, clock, modThumbnails);
        var jsonPlaySets = new PlaysetsJsonRepository(fileSystem, logger, clausewitzMods, dlcs);
        var playsetConfig = new PlaysetConfigRepository(fileSystem, logger, clock);
        var playSets = new PlaySetRepositoryRouter(sqlitePlaySets, jsonPlaySets, playsetConfig);
        var loadFiles = new LoadFileWriter(fileSystem, logger);
        var processLauncher = new ProcessLauncher(logger);
        var launcher = new GameLauncher(processLauncher, fileSystem, logger);
        var continueGame = new ContinueGameReader(fileSystem, logger);
        var dlcSignatures = new DlcSignatureService(fileSystem, logger);

        // Paradox 账号：会话落盘（DPAPI 保护）与登录客户端分开，代理重建时只换客户端。
        var secretProtector = new DpapiSecretProtector(logger);
        var sessionStore = new PdxSessionStore(fileSystem, logger, secretProtector);
        var account = new AccountService(logger, sessionStore, clock, network.Account);

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
            network.NewsFeed,
            network.Metadata,
            network.Assets,
            network.ModThumbnails,
            account,
            network.PdxMods,
            settingsStore,
            Array.Empty<GameInstallation>(),
            new DisposableGroup(network.Http, network.PdxHttp));

        logger.Info("服务装配完成");
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