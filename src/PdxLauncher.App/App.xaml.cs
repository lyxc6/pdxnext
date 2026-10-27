namespace PdxLauncher.App;

using Microsoft.UI.Xaml;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
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
using PdxLauncher.Core.Signatures;

/// <summary>
/// 应用入口。启动阶段只做三件事：手工装配服务、创建主窗口、激活窗口。
/// 冷启动路径上不允许 await 任何网络或大文件 IO —— 游戏发现由各页面在窗口显示后异步触发。
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ComposeServices();

        // 界面语言必须在任何页面 XAML 加载前生效，否则首屏会闪一下默认语言。
        Localizer.Instance.Apply(AppServices.Settings.UiLanguage);
        AppServices.Logger.Info("PdxNext 启动，创建主窗口（冷启动不联网）");

        _window = new MainWindow();
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
        var mods = new ModCatalog(fileSystem, logger, modThumbnails);
        var dlcs = new DlcCatalog(fileSystem, logger);

        // EU5 不写 launcher-v2.sqlite，只认文档目录下的 playsets.json；其余游戏仍用 sqlite。
        // 判定收敛在路由里（GameInstallation.Format），上层服务不需要知道格式差异。
        var sqlitePlaySets = new PlaySetRepository(fileSystem, logger, clock, modThumbnails);
        var jsonPlaySets = new PlaysetsJsonRepository(fileSystem, logger, mods, dlcs);
        var playSets = new PlaySetRepositoryRouter(sqlitePlaySets, jsonPlaySets);
        var loadFiles = new LoadFileWriter(fileSystem, logger);
        var processLauncher = new ProcessLauncher(logger);
        var launcher = new GameLauncher(processLauncher, fileSystem, logger);
        var continueGame = new ContinueGameReader(fileSystem, logger);
        var dlcSignatures = new DlcSignatureService(fileSystem, logger);

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
            settingsStore,
            Array.Empty<GameInstallation>(),
            network.Http);

        logger.Info("服务装配完成");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Error("未处理的异常: " + e.Message, e.Exception);
        }
    }
}