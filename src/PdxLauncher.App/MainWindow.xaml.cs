namespace PdxLauncher.App;

using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.App.Views;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 主窗口：系统标题栏（TitleBar 控件）+ 左侧 NavigationView + 右侧 Frame。
/// 布局与主题全部使用 WinUI 3 标准控件与主题资源，不模仿官方 Paradox 启动器的自绘外壳。
/// 窗口尺寸按 AppSettings 里保存的逻辑像素值乘 DPI 换算成物理像素后交给 AppWindow。
/// </summary>
public sealed partial class MainWindow : Window
{
    private const double DefaultWindowWidth = 1200.0;
    private const double DefaultWindowHeight = 800.0;
    /// <summary>
    /// 最小宽度不再由"播放集页工具条单行排完"倒推：工具条左侧按钮簇已改成 <c>PdxWrapPanel</c>，
    /// 窗口变窄时它自己折行，搜索框固定 200 DIP、右侧「添加模组」常驻，不会再被挤没或裁掉。
    /// 保留 1040 作为整体可用宽度的下限（列表行、其他页面的工具条在更窄时才开始显得局促）。
    /// </summary>
    private const double MinimumWindowWidth = 1040.0;
    private const double MinimumWindowHeight = 600.0;

    /// <summary>
    /// 窗口尺寸变化后的去抖时长（毫秒）：用户拖动窗口边缘时会连续触发 SizeChanged，
    /// 等它停稳再判断要不要按更大宽度重解背景，避免拖一次解十次。
    /// </summary>
    /// <remarks>是否需要重解由 <see cref="Services.BackdropDecodeWidth.NeedsUpgrade"/> 判定（只放大不缩小）。</remarks>
    private const int BackdropUpgradeDelayMilliseconds = 300;

    /// <summary>
    /// 「完整（模糊底）」档那层铺底的解码宽度（物理像素）。96 px 相当于把 2560 宽的图
    /// 降采样 26 倍，细节全部被平均掉，再拉满整窗就是一层柔和的同图底；越小越糊。
    /// </summary>
    private const int BackdropBlurDecodeWidth = 96;

    private readonly NavigationService _navigation;
    private readonly ShellViewModel _viewModel;
    private GameLibraryPage? _libraryPage;

    private WorkshopPage? _workshopPage;

    /// <summary>Mod 库页；它也能打开工坊详情子页，所以要订阅它的详情请求。</summary>
    private ModLibraryPage? _modLibraryPage;

    /// <summary>工坊详情子页；离开它就 detach 事件，避免持有已丢弃页面的引用。</summary>
    private WorkshopDetailPage? _workshopDetailPage;

    /// <summary>播放集页；它会请求打开「添加模组」子页。</summary>
    private ModsPage? _modsPage;

    /// <summary>「添加模组」子页；离开它就 detach 事件，避免持有已丢弃页面的引用。</summary>
    private AddModsPage? _addModsPage;

    /// <summary>存档页；它会请求打开「备份」子页。</summary>
    private SavesPage? _savesPage;

    /// <summary>「备份」子页；离开它就 detach 事件，避免持有已丢弃页面的引用。</summary>
    private SaveBackupsPage? _saveBackupsPage;

    private BitmapLoader? _bitmapLoader;
    private CancellationTokenSource? _backdropLifetime;

    /// <summary>背景图请求序号：快速切换游戏时只应用最后一次加载结果。</summary>
    private int _backdropVersion;

    /// <summary>
    /// 当前这次背景加载请求自己的取消源：新请求进来时先取消旧的，让旧解码真正停下来，
    /// 而不是解码完再丢弃结果（只丢结果 = 白烧一次 2560px 全图解码，后面的请求排队等着）。
    /// </summary>
    private CancellationTokenSource? _backdropRequest;

    /// <summary>当前背景图的完整路径：切到「完整（模糊底）」档要用它补加载低分辨率铺底。</summary>
    private string? _backdropPath;

    /// <summary>当前已经上屏的背景（路径 + 解码宽度）：用来跳过对同一张图、同一宽度的重复解码。</summary>
    private (string? Path, int Width)? _appliedBackdrop;

    /// <summary>按上一次会话的快照提前起的解码任务；游戏确认是同一张图时直接复用，不再解第二遍。</summary>
    private Task<ImageSource?>? _preloadedBackdrop;

    private string? _preloadedBackdropPath;
    private int _preloadedBackdropWidth;
    private CancellationTokenSource? _preloadRequest;

    /// <summary>窗口变大后的背景重解码去抖定时器（只在确实需要更高分辨率时才重解）。</summary>
    private DispatcherQueueTimer? _backdropUpgradeTimer;

    private bool _isStarted;

    /// <summary>翻译提示的显示延时：命中缓存的批次几十毫秒就结束，不值得让标题栏闪一下。</summary>
    private static readonly TimeSpan TranslationIndicatorShowDelay = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// 翻译提示的隐藏延时：整份文档是逐段翻的，段与段之间会有短暂空隙，
    /// 直接按计数显隐会让标题栏忽隐忽现。
    /// </summary>
    private static readonly TimeSpan TranslationIndicatorHideDelay = TimeSpan.FromMilliseconds(400);

    private DispatcherQueueTimer? _translationIndicatorShowTimer;
    private DispatcherQueueTimer? _translationIndicatorHideTimer;

    /// <summary>是否已挂上标题栏留白的重算回调（DPI / 尺寸变化时重算）。</summary>
    private bool _captionPaddingHooked;

    /// <summary>是否已挂上 WM_GETMINMAXINFO 的最小尺寸兜底。</summary>
    private bool _minimumSizeHooked;

    /// <summary>子类化回调必须保存在字段里，否则委托会被 GC 回收。</summary>
    private SubclassProcDelegate? SubclassProc;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>
    /// 全局确认浮层：所有「确认 / 取消」对话框都走它，
    /// 这样「取消」在左、「确认（主色）」在右在所有页面保持一致。
    /// </summary>
    public ConfirmOverlay ConfirmOverlayHost { get; private set; }

    public MainWindow()
    {
        InitializeComponent();

        ConfirmOverlayHost = ConfirmOverlayLayer;
        ConfirmOverlay.Host = ConfirmOverlayHost;

        Title = Localizer.Instance.AppName;

        _navigation = new NavigationService(AppServices.Logger);
        _viewModel = new ShellViewModel(_navigation);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.NavigationRequested += OnNavigationRequested;

        // 标准做法：把 TitleBar 控件交给系统当拖动区域，窗口按钮仍由系统绘制。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyWindowSize();
        ApplyPresenterMinimumSize();
        HookMinimumWindowSize();

        ApplyWindowIcon();

        AlwaysOnTopService.Attach(AppWindow);

        // 系统 caption 按钮（─ □ ✕）的颜色默认取「浅色主题」值，深色标题栏上聚焦态
        // 会画成纯黑不可见，所以先按应用主题给一次；Loaded 后再按实际解析出的主题补一次。
        TitleBarTheme.Apply(
            AppWindow,
            Application.Current.RequestedTheme == ApplicationTheme.Dark ? ElementTheme.Dark : ElementTheme.Light);
        RootGrid.ActualThemeChanged += OnRootGridActualThemeChanged;

        if (AppServices.IsInitialized)
        {
            _bitmapLoader = new BitmapLoader(AppServices.FileSystem, AppServices.Logger, AppServices.ThemeImages);
            _backdropLifetime = new CancellationTokenSource();

            // 背景解码不必等游戏发现：先用上一次会话的快照把同一张图在后台解好，
            // 发现完成后再确认「还是这一张」，命中就直接上屏（解码与发现重叠）。
            StartSpeculativeBackdropPreload();
        }

        RootGrid.Loaded += OnRootGridLoaded;
        RootGrid.SizeChanged += OnRootGridSizeChanged;
        ContentFrame.Navigated += OnContentFrameNavigated;
        AppServices.CurrentGameChanged += OnCurrentGameChanged;
        AppServices.GameSessionMonitor.SessionEnded += OnGameSessionEnded;
        AppServices.GameSessionMonitor.SessionAlert += OnGameSessionAlert;
        AppServices.BackgroundDisplayChanged += OnBackgroundDisplayChanged;
        AppServices.ModEditorNavVisibilityChanged += OnModEditorNavVisibilityChanged;
        AlwaysOnTopService.Changed += OnAlwaysOnTopChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;

        // 全局忙态提示：计数点在 GlobalBusyMonitor / TranslationCoordinator，这里只负责显示 / 隐藏。
        if (AppServices.IsInitialized)
        {
            AppServices.GlobalBusy.Changed += OnGlobalBusyChanged;
        }

        CreateTranslationIndicatorTimers();

        Closed += OnWindowClosed;
    }

    /// <summary>
    /// 给窗口套上应用图标：任务栏、Alt-Tab、标题栏左上的小图标都取这一份。
    /// exe 文件自身的图标由 csproj 的 ApplicationIcon 决定，两者都指向
    /// Assets\AppIcon.ico（由 tools/gen-app-icon.py 生成），避免运行期与文件图标走样。
    /// </summary>
    private void ApplyWindowIcon()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");

        try
        {
            if (!File.Exists(path))
            {
                AppServices.Logger.Warn("窗口图标缺失: " + path);
                return;
            }

            AppWindow.SetIcon(path);
        }
        catch (Exception ex)
        {
            // 图标只是装饰，取不到也要让窗口照常打开。
            AppServices.Logger.Warn("设置窗口图标失败: " + ex.Message);
        }
    }

    private async void OnRootGridLoaded(object sender, RoutedEventArgs e)
    {
        if (_isStarted)
        {
            return;
        }

        _isStarted = true;
        ApplyPresenterMinimumSize();
        EnforceMinimumWindowSize();
        StartupTimeline.Mark(StartupTimeline.ShellReady);

        ApplyAlwaysOnTopPreference();

        try
        {
            RootGrid.Loaded -= OnRootGridLoaded;
            FixTitleBarCaptionPadding();
            HookCaptionPaddingUpdates();

            // 控件模板此时才应用完，框架可能刚写过自己的 caption 颜色，这里覆盖成主题配色。
            TitleBarTheme.Apply(AppWindow, RootGrid.ActualTheme);

            _navigation.Frame = ContentFrame;
            NavigateTo(NavigationService.HomeTag);
            UpdateTitleBar();
            AppServices.Breadcrumb("启动 主窗导航完成");
            var startupWatch = System.Diagnostics.Stopwatch.StartNew();

            // 窗口先显示出来，这里才开始扫描 Steam 库（纯本地 IO，不联网）。
            AppServices.Breadcrumb("启动 游戏发现开始");
            await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            StartupTimeline.MarkInternal(StartupTimeline.DiscoveryReady);
            AppServices.Breadcrumb("启动 游戏发现完成（" + startupWatch.ElapsedMilliseconds + " ms）");
            UpdateTitleBar();
            UpdateContentNavState();

            // 先把整窗背景画出来，再落到主页，避免首页出现时底色闪一下。
            AppServices.Breadcrumb("启动 背景开始");
            await UpdateBackdropAsync().ConfigureAwait(true);
            AppServices.Breadcrumb("启动 背景完成（" + startupWatch.ElapsedMilliseconds + " ms）");
            AppServices.Logger.Info("启动首帧阶段耗时: 游戏发现+背景 " + startupWatch.ElapsedMilliseconds + " ms");

            // 主页是当前游戏页：没发现任何游戏时它没内容可看，退回游戏库引导用户。
            if (AppServices.CurrentGame is null)
            {
                NavigateTo(NavigationService.LibraryTag);
            }

            StartupTimeline.Mark(StartupTimeline.HomeLoaded);

            // 自动检查更新放在首屏完成之后；fire-and-forget，绝不让网络拖慢冷启动。
            _ = UpdateFlow.CheckOnStartupAsync();
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("MainWindow 初始化失败", ex);
            }
        }
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        try
        {
            if (args.InvokedItemContainer is NavigationViewItem { Tag: string tag })
            {
                NavigateTo(tag);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("导航项点击处理失败: " + ex.Message);
            }
        }
    }

    private void OnContentFrameNavigated(object sender, NavigationEventArgs e)
    {
        try
        {
            AttachPageEvents(e.Content);

            var tag = NavigationService.TagForPageType(e.SourcePageType);
            if (tag is not null)
            {
                SyncSelectedNavItem(tag);
            }

            UpdateTitleBar();
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("导航完成回调失败: " + ex.Message);
            }
        }
    }

    /// <summary>页面自己无法持有 NavigationService，靠页面事件把导航意图转给窗口。</summary>
    private void AttachPageEvents(object? content)
    {
        if (_libraryPage is not null)
        {
            _libraryPage.NavigationRequested -= OnPageNavigationRequested;
            _libraryPage = null;
        }

        if (_workshopPage is not null)
        {
            _workshopPage.AccountRequested -= OnWorkshopAccountRequested;
            _workshopPage.DetailRequested -= OnWorkshopDetailRequested;
            _workshopPage = null;
        }

        if (_modLibraryPage is not null)
        {
            _modLibraryPage.DetailRequested -= OnWorkshopDetailRequested;
            _modLibraryPage = null;
        }

        if (_workshopDetailPage is not null)
        {
            _workshopDetailPage.NavigationRequested -= OnPageNavigationRequested;
            _workshopDetailPage.DetailRequested -= OnWorkshopDetailRequested;
            _workshopDetailPage = null;
        }

        if (_modsPage is not null)
        {
            _modsPage.AddModsRequested -= OnAddModsRequested;
            _modsPage = null;
        }

        if (_addModsPage is not null)
        {
            _addModsPage.NavigationRequested -= OnPageNavigationRequested;
            _addModsPage.ModsSelected -= OnAddModsSelected;
            _addModsPage = null;
        }

        if (_savesPage is not null)
        {
            _savesPage.BackupsRequested -= OnSaveBackupsRequested;
            _savesPage = null;
        }

        if (_saveBackupsPage is not null)
        {
            _saveBackupsPage.NavigationRequested -= OnPageNavigationRequested;
            _saveBackupsPage = null;
        }

        if (content is GameLibraryPage libraryPage)
        {
            _libraryPage = libraryPage;
            libraryPage.NavigationRequested += OnPageNavigationRequested;
        }

        if (content is WorkshopPage workshopPage)
        {
            _workshopPage = workshopPage;
            workshopPage.AccountRequested += OnWorkshopAccountRequested;
            workshopPage.DetailRequested += OnWorkshopDetailRequested;
        }

        if (content is ModLibraryPage modLibraryPage)
        {
            _modLibraryPage = modLibraryPage;
            modLibraryPage.DetailRequested += OnWorkshopDetailRequested;
        }

        if (content is WorkshopDetailPage workshopDetailPage)
        {
            _workshopDetailPage = workshopDetailPage;
            workshopDetailPage.NavigationRequested += OnPageNavigationRequested;
            workshopDetailPage.DetailRequested += OnWorkshopDetailRequested;
        }

        if (content is ModsPage modsPage)
        {
            _modsPage = modsPage;
            modsPage.AddModsRequested += OnAddModsRequested;
        }

        if (content is AddModsPage addModsPage)
        {
            _addModsPage = addModsPage;
            addModsPage.NavigationRequested += OnPageNavigationRequested;
            addModsPage.ModsSelected += OnAddModsSelected;
        }

        if (content is SavesPage savesPage)
        {
            _savesPage = savesPage;
            savesPage.BackupsRequested += OnSaveBackupsRequested;
        }

        if (content is SaveBackupsPage saveBackupsPage)
        {
            _saveBackupsPage = saveBackupsPage;
            saveBackupsPage.NavigationRequested += OnPageNavigationRequested;
        }

        if (content is McpKeyEditorPage mcpKeyEditorPage)
        {
            mcpKeyEditorPage.NavigationRequested += OnMcpKeyEditorRequested;
        }

        if (content is SettingsPage settingsPage)
        {
            settingsPage.McpKeyEditorRequested += OnMcpKeyEditorOpenRequested;
        }
    }

    /// <summary>设置页要求打开 MCP 密钥子页；左栏选中态故意停在「设置」。</summary>
    private void OnMcpKeyEditorOpenRequested(object? sender, McpKeyEditorNavigationRequest request)
    {
        try
        {
            if (!_navigation.Navigate(NavigationService.McpKeyEditorTag, request))
            {
                AppServices.Logger.Warn("导航到 MCP 密钥子页失败");
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("导航到 MCP 密钥子页异常", ex);
        }
    }

    /// <summary>
    /// MCP 密钥子页返回设置页：把「是否保存 / 一次性明文」带回去，
    /// 左栏选中态同步成「设置」，让用户回到原来的 MCP 分节。
    /// </summary>
    private void OnMcpKeyEditorRequested(object? sender, McpKeyEditorNavigationResult result)
    {
        try
        {
            if (!_navigation.Navigate(NavigationService.SettingsTag, result))
            {
                AppServices.Logger.Warn("带着 MCP 密钥结果回到设置页失败");
                return;
            }

            SyncSelectedNavItem(NavigationService.SettingsTag);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("带着 MCP 密钥结果回到设置页异常", ex);
        }
    }

    /// <summary>
    /// 存档页要求打开「备份」子页（页面自己不持导航职责）。
    /// 故意不同步左栏选中项：子页没有菜单项，选中态应停在「存档」。
    /// </summary>
    private void OnSaveBackupsRequested(object? sender, SaveBackupsNavigationRequest request)
    {
        try
        {
            if (!_navigation.Navigate(NavigationService.SaveBackupsTag, request))
            {
                if (AppServices.IsInitialized)
                {
                    AppServices.Logger.Warn("导航到备份子页失败: " + request.Game.GameId);
                }
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("导航到备份子页异常: " + request.Game.GameId, ex);
            }
        }
    }

    private void OnWorkshopAccountRequested(object? sender, EventArgs e) =>
        NavigateTo(NavigationService.AccountTag);

    /// <summary>
    /// 创意工坊列表页 / Mod 库页要求打开某条 mod 的详情子页（页面自己不持导航职责）。
    /// 两个页面用同一种请求，返回目标在请求里。
    /// </summary>
    private void OnWorkshopDetailRequested(object? sender, WorkshopDetailNavigationRequest request) =>
        NavigateToWorkshopDetail(request);

    private void NavigateToWorkshopDetail(WorkshopDetailNavigationRequest request)
    {
        try
        {
            var row = request.Row;
            if (!_navigation.Navigate(NavigationService.WorkshopDetailTag, request))
            {
                if (AppServices.IsInitialized)
                {
                    AppServices.Logger.Warn("导航到工坊详情失败: " + row.Id);
                }

                return;
            }

            // 故意不同步左栏选中项：详情子页没有对应菜单项，选中态应停在发起方那一页（创意工坊或 Mod 库）。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("导航到工坊详情异常: " + request.Row.Id, ex);
            }
        }
    }

    /// <summary>
    /// 播放集页要求打开「添加模组」子页（页面自己不持导航职责）。
    /// </summary>
    private void OnAddModsRequested(object? sender, AddModsNavigationRequest request) => NavigateToAddMods(request);

    /// <summary>
    /// 「添加模组」子页确认了选择：带着结果回到播放集页，由它追加并自动保存。
    /// </summary>
    private void OnAddModsSelected(object? sender, AddModsNavigationResult result)
    {
        try
        {
            // 必须带参数回去：播放集页靠它拿到勾选的 mod。
            // 左栏选中态同步成「播放集」（本页正是播放集页）。
            if (!_navigation.Navigate(NavigationService.PlaySetsTag, result))
            {
                AppServices.Logger.Warn("带着添加结果回到播放集页失败");
                return;
            }

            SyncSelectedNavItem(NavigationService.PlaySetsTag);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("带着添加结果回到播放集页异常", ex);
        }
    }

    /// <summary>
    /// 打开「添加模组」子页；故意不同步左栏选中项（该子页没有对应菜单项，选中态应停在「播放集」）。
    /// </summary>
    private void NavigateToAddMods(AddModsNavigationRequest request)
    {
        try
        {
            if (!_navigation.Navigate(NavigationService.AddModsTag, request))
            {
                AppServices.Logger.Warn("导航到添加模组页失败: " + request.PlaySetId.ToString("N"));
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("导航到添加模组页异常: " + request.PlaySetId.ToString("N"), ex);
        }
    }

    private void OnPageNavigationRequested(object? sender, string tag) => NavigateTo(tag);

    private void OnNavigationRequested(object? sender, string tag) => NavigateTo(tag);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateTitleBar();

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _viewModel.CurrentGame = AppServices.CurrentGame;
                UpdateTitleBar();
                UpdateContentNavState();
                _ = UpdateBackdropAsync();
            }))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("MainWindow 无法派发游戏切换事件");
            }
        }
    }

    private void OnGameSessionEnded(object? sender, PdxLauncher.Core.CrashAnalysis.GameSessionSnapshot session)
    {
        // 监听在后台定时器上触发，必须 marshal 到 UI 线程再弹提示。
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var localizer = Localizer.Instance;
                    await Controls.ConfirmOverlay.ShowMessage(
                        localizer.CrashSessionEndedTitle,
                        localizer.T("CrashSessionEndedMessage", session.ErrorBlocks),
                        localizer.CommonClose);
                }
                catch (Exception exception)
                {
                    AppServices.Logger.Warn("显示本次游戏报错提示失败：" + exception.Message);
                }
            }))
        {
            AppServices.Logger.Warn("MainWindow 无法派发本次游戏结束事件");
        }
    }

    private void OnGameSessionAlert(object? sender, PdxLauncher.Core.CrashAnalysis.GameSessionSnapshot session)
    {
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var localizer = Localizer.Instance;
                    var category = session.FatalInfo?.Category switch
                    {
                        "platform-service" => localizer.CrashSessionFatalPlatformService,
                        "asset-database" => localizer.CrashSessionFatalAssetDatabase,
                        _ => localizer.CrashSessionFatalUnknown,
                    };
                    await Controls.ConfirmOverlay.ShowMessage(
                        localizer.CrashSessionAlertTitle,
                        localizer.T(Keys.CrashSessionAlertMessage,
                            string.IsNullOrWhiteSpace(session.GameName) ? session.GameId : session.GameName,
                            category),
                        localizer.CommonClose);
                }
                catch (Exception exception)
                {
                    AppServices.Logger.Warn("显示游戏初始化失败提示失败：" + exception.Message);
                }
            }))
        {
            AppServices.Logger.Warn("MainWindow 无法派发游戏初始化失败提示");
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigationRequested -= OnNavigationRequested;
            AppServices.CurrentGameChanged -= OnCurrentGameChanged;
            AppServices.GameSessionMonitor.SessionEnded -= OnGameSessionEnded;
            AppServices.GameSessionMonitor.SessionAlert -= OnGameSessionAlert;
            AppServices.BackgroundDisplayChanged -= OnBackgroundDisplayChanged;
            AppServices.ModEditorNavVisibilityChanged -= OnModEditorNavVisibilityChanged;
            AlwaysOnTopService.Changed -= OnAlwaysOnTopChanged;
            Localizer.Instance.LanguageChanged -= OnLanguageChanged;
            RootGrid.ActualThemeChanged -= OnRootGridActualThemeChanged;

            if (AppServices.IsInitialized)
            {
            AppServices.GlobalBusy.Changed -= OnGlobalBusyChanged;
            }

            StopTranslationIndicatorTimers();

            if (_captionPaddingHooked)
            {
                _captionPaddingHooked = false;
                var xamlRoot = RootGrid.XamlRoot;
                if (xamlRoot is not null)
                {
                    xamlRoot.Changed -= OnXamlRootChangedForCaptionPadding;
                }

                AppTitleBar.SizeChanged -= OnTitleBarSizeChangedForCaptionPadding;
            }

            _backdropRequest?.Cancel();
            _backdropRequest?.Dispose();
            _backdropRequest = null;

            RootGrid.SizeChanged -= OnRootGridSizeChanged;
            if (_backdropUpgradeTimer is not null)
            {
                _backdropUpgradeTimer.Tick -= OnBackdropUpgradeTimerTick;
                _backdropUpgradeTimer.Stop();
                _backdropUpgradeTimer = null;
            }

            _preloadRequest?.Cancel();
            _preloadRequest?.Dispose();
            _preloadRequest = null;
            _preloadedBackdrop = null;
            _appliedBackdrop = null;

            _backdropLifetime?.Cancel();
            _backdropLifetime?.Dispose();
            _backdropLifetime = null;
            _bitmapLoader = null;

            if (_libraryPage is not null)
            {
                _libraryPage.NavigationRequested -= OnPageNavigationRequested;
                _libraryPage = null;
            }

            if (_workshopPage is not null)
            {
                _workshopPage.AccountRequested -= OnWorkshopAccountRequested;
                _workshopPage.DetailRequested -= OnWorkshopDetailRequested;
                _workshopPage = null;
            }

            if (_modLibraryPage is not null)
            {
                _modLibraryPage.DetailRequested -= OnWorkshopDetailRequested;
                _modLibraryPage = null;
            }

            if (_workshopDetailPage is not null)
            {
                _workshopDetailPage.NavigationRequested -= OnPageNavigationRequested;
                _workshopDetailPage.DetailRequested -= OnWorkshopDetailRequested;
                _workshopDetailPage = null;
            }

            if (_modsPage is not null)
            {
                _modsPage.AddModsRequested -= OnAddModsRequested;
                _modsPage = null;
            }

            if (_addModsPage is not null)
            {
                _addModsPage.NavigationRequested -= OnPageNavigationRequested;
                _addModsPage.ModsSelected -= OnAddModsSelected;
                _addModsPage = null;
            }

            if (_savesPage is not null)
            {
                _savesPage.BackupsRequested -= OnSaveBackupsRequested;
                _savesPage = null;
            }

            if (_saveBackupsPage is not null)
            {
                _saveBackupsPage.NavigationRequested -= OnPageNavigationRequested;
                _saveBackupsPage = null;
            }

            if (!AppServices.IsInitialized)
            {
                return;
            }

            var scale = GetScaleFactor();
            var size = AppWindow.Size;
            AppServices.Settings.WindowWidth = size.Width / scale;
            AppServices.Settings.WindowHeight = size.Height / scale;

            // 位置按物理像素存：恢复时直接交给 AppWindow.Move，省一次坐标换算。
            var position = AppWindow.Position;
            AppServices.Settings.WindowX = position.X;
            AppServices.Settings.WindowY = position.Y;

            // 关闭窗口是最后一次落盘机会：先把页面状态记下来，
            // 再把各缓存攒着的防抖写入落盘，再把设置页的延迟写合并进来，最后立即写一次窗口尺寸。
            PageStateStore.Persist();
            AppServices.FlushCaches();
            AppServices.FlushSettings();
            // 关窗时留一行内存快照，配合启动那行可以对比一次会话涨了多少。
            if (AppServices.IsInitialized)
            {
                MemoryDiagnostics.Log(
                    AppServices.Logger,
                    "窗口关闭",
                    () => (AppServices.WorkshopInfo.Count, AppServices.WorkshopItemPages.Count, AppServices.ModThumbnails.Cache.Count));
            }

            // 看门狗汇总：这次会话里 UI 线程最长被占住多久。排查「整窗卡死」先看这一行。
            if (AppServices.Watchdog is { } watchdog)
            {
                AppServices.Logger.Info(
                    "UI 线程停顿统计：" + watchdog.Recorder.StallCount
                    + " 次，最长 " + watchdog.Recorder.MaxStallMs + " ms");
                watchdog.Dispose();
                AppServices.AttachWatchdog(null);
            }

            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存窗口尺寸失败: " + saved.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("保存窗口尺寸失败: " + ex.Message);
            }
        }
    }

    /// <summary>建立翻译提示的两只一次性定时器：一只管显示去抖，一只管隐藏去抖。</summary>
    private void CreateTranslationIndicatorTimers()
    {
        var queue = DispatcherQueue;
        if (queue is null)
        {
            return;
        }

        _translationIndicatorShowTimer = queue.CreateTimer();
        _translationIndicatorShowTimer.Interval = TranslationIndicatorShowDelay;
        _translationIndicatorShowTimer.IsRepeating = false;
        _translationIndicatorShowTimer.Tick += OnTranslationIndicatorShowTimerTick;

        _translationIndicatorHideTimer = queue.CreateTimer();
        _translationIndicatorHideTimer.Interval = TranslationIndicatorHideDelay;
        _translationIndicatorHideTimer.IsRepeating = false;
        _translationIndicatorHideTimer.Tick += OnTranslationIndicatorHideTimerTick;
    }

    /// <summary>停掉并解绑两只定时器（窗口关闭时调用）。</summary>
    private void StopTranslationIndicatorTimers()
    {
        if (_translationIndicatorShowTimer is not null)
        {
            _translationIndicatorShowTimer.Tick -= OnTranslationIndicatorShowTimerTick;
            _translationIndicatorShowTimer.Stop();
            _translationIndicatorShowTimer = null;
        }

        if (_translationIndicatorHideTimer is not null)
        {
            _translationIndicatorHideTimer.Tick -= OnTranslationIndicatorHideTimerTick;
            _translationIndicatorHideTimer.Stop();
            _translationIndicatorHideTimer = null;
        }
    }

    /// <summary>翻译在途计数变了：切回 UI 线程再动控件（计数可能来自翻译线程）。</summary>
    private void OnGlobalBusyChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue is null)
        {
            return;
        }

        DispatcherQueue.TryEnqueue(ApplyGlobalBusyIndicator);
    }

    /// <summary>按当前在途状态安排显示 / 隐藏（都带延时，避免缓存命中和分段翻译时闪烁）。</summary>
    private void ApplyGlobalBusyIndicator()
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        if (AppServices.GlobalBusy.Current is not null)
        {
            _translationIndicatorHideTimer?.Stop();

            if (TranslationIndicator.Visibility != Visibility.Visible)
            {
                _translationIndicatorShowTimer?.Start();
            }

            return;
        }

        _translationIndicatorShowTimer?.Stop();

        if (TranslationIndicator.Visibility == Visibility.Visible)
        {
            _translationIndicatorHideTimer?.Start();
        }
    }

    private void OnTranslationIndicatorShowTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        ShowTranslationIndicator();
    }

    private void OnTranslationIndicatorHideTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        HideTranslationIndicator();
    }

    /// <summary>显示标题栏的「正在翻译…」：环形进度在动，文字是说明。</summary>
    private void ShowTranslationIndicator()
    {
        // 显示延时到点时这一批可能已经结束，别再补一次提示。
        if (!AppServices.IsInitialized || AppServices.GlobalBusy.Current is null)
        {
            return;
        }

        RefreshGlobalBusyText();
        ringTranslating.IsActive = true;
        TranslationIndicator.Visibility = Visibility.Visible;
    }

    private void HideTranslationIndicator()
    {
        ringTranslating.IsActive = false;
        TranslationIndicator.Visibility = Visibility.Collapsed;
    }

    /// <summary>切语言或忙态来源变化时刷新标题栏文案。</summary>
    private void RefreshGlobalBusyText()
    {
        var current = AppServices.GlobalBusy.Current;
        var text = current is null ? string.Empty : Localizer.Instance.T(current.LabelKey);
        AutomationProperties.SetName(ringTranslating, text);
        AutomationProperties.SetName(txtTranslating, text);
    }

    /// <summary>
    /// 内容管理导航项：播放集 / Mod 库 / Mod 创建 / DLC 只在当前游戏能管理内容时才出现；
    /// 创意工坊另算——仅启动的游戏（Cities: Skylines 1 / Prison Architect）也能浏览与订阅 Steam 工坊，
    /// 那里只碰 Steam、不碰游戏目录，所以按内置表单独放行。
    /// 落在被隐藏页面上的用户会被送回主页。
    /// </summary>
    private void UpdateContentNavState()
    {
        try
        {
            var game = AppServices.IsInitialized ? AppServices.CurrentGame : null;
            var manageable = game is null || game.SupportsContentManagement;
            var workshopAvailable = manageable
                || (game is not null && SteamWorkshopGames.HasWorkshop(game.GameId));

            // 隐藏而不是置灰：置灰的项看起来像坏了，而且禁用的 NavigationViewItem 不弹 ToolTip，
            // 只能在左栏底部塞一段解释文字（窄列折行更难看）。干脆不显示。
            var contentVisibility = manageable ? Visibility.Visible : Visibility.Collapsed;
            NavPlaySets.Visibility = contentVisibility;
            NavModLibrary.Visibility = contentVisibility;
            NavModStudio.Visibility = contentVisibility;
            NavDlc.Visibility = contentVisibility;
            NavWorkshop.Visibility = workshopAvailable ? Visibility.Visible : Visibility.Collapsed;

            // 「Mod 编辑」入口还受设置开关控制（高级分区）：默认开，关掉后整个隐藏。
            var modEditorVisible = manageable && AppServices.Settings.ShowModEditorNav;
            NavModEditor.Visibility = modEditorVisible ? Visibility.Visible : Visibility.Collapsed;

            if (manageable)
            {
                // 入口被关且正停在该页：送回主页（可管理游戏也要处理）。
                if (!AppServices.Settings.ShowModEditorNav
                    && _navigation.CurrentTag is string currentTag
                    && string.Equals(currentTag, NavigationService.ModEditorTag, StringComparison.OrdinalIgnoreCase))
                {
                    NavigateTo(NavigationService.HomeTag);
                }

                return;
            }

            if (_navigation.CurrentTag is string tag
                && (string.Equals(tag, NavigationService.PlaySetsTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, NavigationService.ModLibraryTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, NavigationService.ModStudioTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, NavigationService.ModEditorTag, StringComparison.OrdinalIgnoreCase)
                    || (!workshopAvailable
                        && (string.Equals(tag, NavigationService.WorkshopTag, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(tag, NavigationService.WorkshopDetailTag, StringComparison.OrdinalIgnoreCase)))
                    || string.Equals(tag, NavigationService.AddModsTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, NavigationService.DlcTag, StringComparison.OrdinalIgnoreCase)))
            {
                NavigateTo(NavigationService.HomeTag);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("更新内容管理导航状态失败: " + ex.Message);
            }
        }
    }

    private void NavigateTo(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        // 排队而不是就地切页：用户点导航时，上一个页面可能正处在秒级的加载里
        // （工坊首屏、mod 扫描）。High 优先级让这次点击在加载的下一个空隙就插进去，
        // 而不是等整段加载跑完才「补」一次跳转。
        if (!DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () => NavigateCore(tag)))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("无法派发导航: " + tag);
            }
        }
    }

    /// <summary>
    /// 真正切页。失败或异常除了写日志，还要给用户一条可见提示：
    /// 只写日志的话，界面表现就是「点了没反应」，和线程卡死没法区分。
    /// </summary>
    private void NavigateCore(string tag)
    {
        try
        {
            if (!_navigation.Navigate(tag))
            {
                ReportNavigationFailure(tag);
                return;
            }

            SyncSelectedNavItem(tag);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("导航异常: " + tag, ex);
            }

            ReportNavigationFailure(tag);
        }
    }

    /// <summary>导航失败提示：复用窗口上已有的 <see cref="ConfirmOverlay"/>，不另造弹窗机制。</summary>
    private void ReportNavigationFailure(string tag)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Warn("导航失败: " + tag);
        }

        if (ConfirmOverlay.Host is null)
        {
            // 窗口还没建好（启动早期的导航）：没有宿主就只剩日志，此时用户也还没在点。
            return;
        }

        // 浮层只有一层：连点失败时后到的会按「取消」立刻返回，不会叠出多个框。
        _ = ConfirmOverlay.ShowMessage(
            Localizer.Instance.T(Keys.ShellNavFailedTitle),
            Localizer.Instance.T(Keys.ShellNavFailedBody),
            Localizer.Instance.T(Keys.CommonClose));
    }

    private void SyncSelectedNavItem(string tag)
    {
        var item = FindNavigationItem(MainNavigation.MenuItems, tag)
            ?? FindNavigationItem(MainNavigation.FooterMenuItems, tag);

        if (item is not null && !ReferenceEquals(MainNavigation.SelectedItem, item))
        {
            MainNavigation.SelectedItem = item;
        }
    }

    private static NavigationViewItem? FindNavigationItem(IList<object> items, string tag)
    {
        foreach (var entry in items)
        {
            if (entry is NavigationViewItem item
                && item.Tag is string itemTag
                && string.Equals(itemTag, tag, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    // ------------------------------------------------------------ 窗口置顶

    /// <summary>启动时把 settings.json 里的置顶偏好应用到窗口；默认关，不会残留上一轮遗留的置顶。</summary>
    private void ApplyAlwaysOnTopPreference()
    {
        var desired = AppServices.IsInitialized && AppServices.Settings.AlwaysOnTop;

        if (!AlwaysOnTopService.Apply(desired))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("应用窗口置顶偏好失败：当前窗口不是 Overlapped 呈现方式");
            }
        }

        SyncAlwaysOnTopButton(desired);
    }

    private void OnAlwaysOnTopClick(object sender, RoutedEventArgs e)
    {
        var requested = AlwaysOnTopButton.IsChecked == true;
        if (!AlwaysOnTopService.Set(requested, out var error))
        {
            // 应用失败就把图钉退回原状态，避免 UI 与实际窗口状态不一致。
            AlwaysOnTopButton.IsChecked = !requested;
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn(error ?? "切换窗口置顶失败");
            }

            return;
        }

        SyncAlwaysOnTopButton(requested);
    }

    private void OnAlwaysOnTopChanged(object? sender, bool alwaysOnTop) => SyncAlwaysOnTopButton(alwaysOnTop);

    /// <summary>切语言后重写置顶按钮的名称与提示：两个变体都是缓存下来的字符串，不重算会停在旧语言。</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        SyncAlwaysOnTopButton(AlwaysOnTopButton.IsChecked == true);
        RefreshGlobalBusyText();
    }

    private void SyncAlwaysOnTopButton(bool alwaysOnTop)
    {
        AlwaysOnTopButton.IsChecked = alwaysOnTop;
        AutomationProperties.SetName(
            AlwaysOnTopButton,
            alwaysOnTop ? Localizer.Instance.ShellAlwaysOnTopDisable : Localizer.Instance.ShellAlwaysOnTopEnable);
        ToolTipService.SetToolTip(
            AlwaysOnTopButton,
            alwaysOnTop
                ? Localizer.Instance.ShellAlwaysOnTopDisableTooltip
                : Localizer.Instance.ShellAlwaysOnTopEnableTooltip);
    }

    /// <summary>
    /// 整窗背景：取当前游戏主题的背景图铺满窗口（含标题栏与左侧导航栏），并显示遮罩。
    /// 无游戏 / 主题无背景图 / 解码失败时清空图片并隐藏遮罩，窗口退回 PdxWindowBackgroundBrush 纯色底，
    /// 视觉与没有整窗背景的版本一致。
    /// </summary>
    /// <remarks>
    /// 并发语义：每次进入都先取消上一次请求（<see cref="_backdropRequest"/>），再订阅整窗生命周期。
    /// 只靠 <see cref="_backdropVersion"/> 丢结果是「解码完再扔」，连点五款游戏就是五次大图
    /// 解码排队在 UI 线程，实测能把后一次背景拖到 50 秒后才显示 —— 必须真正取消旧工作。
    /// <para>
    /// 三段省时间的手段（首帧性能）：
    /// 1) 解码宽度按窗口现算（<see cref="Services.BackdropDecodeWidth"/>），不再无条件 2560；
    /// 2) 启动时先按上一次会话的快照在后台解码（<see cref="StartSpeculativeBackdropPreload"/>），
    ///    这里若发现还是同一张图就直接复用，解码与游戏发现重叠；
    /// 3) 同一张图、同一宽度已经上屏时直接返回，不再解第二遍（<c>CurrentGameChanged</c> 与启动初始化各会触发一次）。
    /// </para>
    /// </remarks>
    private async Task UpdateBackdropAsync()
    {
        if (_bitmapLoader is null || _backdropLifetime is null)
        {
            return;
        }

        ThemeSpec? theme = AppServices.IsInitialized ? AppServices.CurrentGame?.Theme : null;
        var spec = theme ?? ThemeApplier.CreatePlaceholderSpec();
        var decodeWidth = ResolveBackdropDecodeWidth();

        // 同一张图、且屏上那张的分辨率已经不低于这次要的：不再解码、不再重设画笔。
        if (_appliedBackdrop is { } applied
            && applied.Width >= decodeWidth
            && string.Equals(applied.Path ?? string.Empty, spec.Background ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            && ShellBackgroundBrush.ImageSource is not null)
        {
            return;
        }

        var version = ++_backdropVersion;

        // 取消上一次请求：旧解码会尽快抛 OperationCanceledException 退出，不再占用线程。
        var previousRequest = _backdropRequest;
        var request = CancellationTokenSource.CreateLinkedTokenSource(_backdropLifetime.Token);
        _backdropRequest = request;
        previousRequest?.Cancel();
        previousRequest?.Dispose();

        var token = request.Token;

        ImageSource? background;
        ImageSource? blurred = null;
        var appliedWidth = decodeWidth;
        var usedPreload = false;
        try
        {
            // 命中启动快照的预解码就复用，否则照常解码这一次。
            var preloaded = await TakePreloadedBackdropAsync(spec.Background).ConfigureAwait(true);
            if (preloaded.Image is not null)
            {
                background = preloaded.Image;
                appliedWidth = preloaded.Width;
                usedPreload = true;
            }
            else
            {
                StartupTimeline.MarkInternal(StartupTimeline.BackdropDecodeStart);
                background = await _bitmapLoader
                    .LoadAsync(spec.Background, decodeWidth, token)
                    .ConfigureAwait(true);
                StartupTimeline.MarkInternal(StartupTimeline.BackdropDecoded);
            }

            _backdropPath = spec.Background;

            // 只有「完整（模糊底）」档才需要那层低分辨率铺底，别的档位省掉一次解码。
            if (background is not null
                && BackgroundDisplayModeCatalog.UsesBlurredBackdrop(CurrentBackgroundDisplayMode()))
            {
                blurred = await _bitmapLoader
                    .LoadAsync(spec.Background, BackdropBlurDecodeWidth, token)
                    .ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("加载整窗背景失败: " + ex.Message);
            }

            background = null;
            _backdropPath = null;
        }
        finally
        {
            // 只有自己仍是「最新请求」时才清字段，避免把后来者的源误删。
            if (ReferenceEquals(_backdropRequest, request))
            {
                _backdropRequest = null;
                request.Dispose();
            }
        }

        // 快速连续切换游戏时，旧请求的结果直接丢弃。
        if (version != _backdropVersion)
        {
            return;
        }

        ShellBackgroundBrush.ImageSource = background;
        ShellBackgroundBlurBrush.ImageSource = blurred;
        StartupTimeline.MarkInternal(StartupTimeline.BackdropBrushAssigned);
        await ApplyBackgroundDisplayModeAsync().ConfigureAwait(true);
        StartupTimeline.MarkInternal(StartupTimeline.BackdropModeApplied);

        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Info(
                "背景上屏: " + (background is null ? "无图" : appliedWidth + " px")
                + "（" + (usedPreload ? "复用启动预解码" : "本次解码") + "）");
        }

        _appliedBackdrop = (spec.Background, appliedWidth);

        // 首帧性能埋点：背景真正交给画笔（并完成显示方式应用）之后。
        // 这里再打一行明细，覆盖「主页首帧早于背景」的启动顺序：日志里最后一次出现的
        // [首帧明细] 就是本次启动的完整读数。
        StartupTimeline.MarkInternal(StartupTimeline.BackdropPainted);
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Info("[首帧明细] " + StartupTimeline.DescribeAll());
        }

        // 快照落盘放在打点之后、线程池上：写文件在这台机器上实测要 60 ms 量级（杀软把关），
        // 摆在背景上屏的关键路径上等于自己给自己加一段首帧延迟。
        SaveBackdropSnapshotInBackground(spec.Background, background);
    }

    /// <summary>按窗口物理尺寸算这次该解多宽；取不到窗口尺寸时退回原来的 2560（宁可清晰，不要糊）。</summary>
    private int ResolveBackdropDecodeWidth()
    {
        try
        {
            var size = AppWindow.Size;
            return Services.BackdropDecodeWidth.Resolve(size.Width, size.Height);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("读取窗口尺寸失败，背景按最大宽度解码: " + ex.Message);
            }

            return Services.BackdropDecodeWidth.Max;
        }
    }

    /// <summary>
    /// 预解码用的宽度：按「设置里保存的逻辑像素 × 当前 DPI」算，与 <see cref="ApplyWindowSize"/> 同一套算式。
    /// <para>
    /// 不用 <c>AppWindow.Size</c>：窗口构造期系统还没把 <c>Resize</c> 的结果反映到 Size 上，
    /// 拿到的可能是旧值/默认值，导致预解码与真正上屏那次宽度不一致 —— 上一轮实测就是这样
    /// 白解一遍又重新解一遍（多花 140 ms）。用保存值算出来的宽度与后面稳定状态下的一致。
    /// </para>
    /// </summary>
    private int ResolvePreloadDecodeWidth()
    {
        try
        {
            var scale = GetScaleFactor();
            var widthDip = AppServices.IsInitialized && AppServices.Settings.WindowWidth > 0
                ? Math.Max(AppServices.Settings.WindowWidth, MinimumWindowWidth)
                : DefaultWindowWidth;
            var heightDip = AppServices.IsInitialized && AppServices.Settings.WindowHeight > 0
                ? Math.Max(AppServices.Settings.WindowHeight, MinimumWindowHeight)
                : DefaultWindowHeight;

            return Services.BackdropDecodeWidth.Resolve(widthDip * scale, heightDip * scale);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("计算预解码宽度失败，按最大宽度处理: " + ex.Message);
            }

            return Services.BackdropDecodeWidth.Max;
        }
    }

    /// <summary>
    /// 启动时按上一次会话的快照把背景先解好（只解不上屏）。
    /// 快照缺失 / 文件已不在 / 应用服务未就绪时什么都不做 —— 这条路只是加速，
    /// 失败了照旧等游戏发现完成再解码。
    /// </summary>
    private void StartSpeculativeBackdropPreload()
    {
        if (_bitmapLoader is null || _backdropLifetime is null || !AppServices.IsInitialized)
        {
            return;
        }

        var snapshot = FrameSnapshotStore.Load(
            AppServices.FileSystem,
            AppServices.Logger,
            FrameSnapshotStore.PathIn(AppServices.DataRoot));
        if (snapshot?.BackgroundPath is not { Length: > 0 } path)
        {
            return;
        }

        if (!AppServices.FileSystem.FileExists(path))
        {
            return;
        }

        var loader = _bitmapLoader;
        var decodeWidth = ResolvePreloadDecodeWidth();
        var request = CancellationTokenSource.CreateLinkedTokenSource(_backdropLifetime.Token);
        _preloadRequest = request;
        _preloadedBackdropPath = path;
        _preloadedBackdropWidth = decodeWidth;

        StartupTimeline.MarkInternal(StartupTimeline.BackdropPreloadStart);
        _preloadedBackdrop = PreloadAsync(loader, path, decodeWidth, request.Token);

        static async Task<ImageSource?> PreloadAsync(
            BitmapLoader loader,
            string path,
            int decodeWidth,
            CancellationToken token)
        {
            var image = await loader.LoadAsync(path, decodeWidth, token).ConfigureAwait(true);
            StartupTimeline.MarkInternal(StartupTimeline.BackdropPreloadReady);
            return image;
        }
    }

    /// <summary>
    /// 取启动时预解码的结果：路径对得上才用（宽度只作记录 —— 窗口尺寸在构造期与布局完成后可能差一档，
    /// 因此按路径命中、按实际宽度记账，需要更高分辨率时由 <see cref="OnBackdropUpgradeTimerTick"/> 再补）。
    /// 对不上就立刻取消这次预解码，调用方照常解码本次要的图。预解码任务只被取用一次。
    /// </summary>
    private async Task<(ImageSource? Image, int Width)> TakePreloadedBackdropAsync(string? path)
    {
        var task = _preloadedBackdrop;
        if (task is null)
        {
            return (null, 0);
        }

        var request = _preloadRequest;
        var width = _preloadedBackdropWidth;
        var matches = string.Equals(
            _preloadedBackdropPath ?? string.Empty,
            path ?? string.Empty,
            StringComparison.OrdinalIgnoreCase);

        _preloadedBackdrop = null;
        _preloadRequest = null;
        _preloadedBackdropPath = null;

        if (!matches)
        {
            // 上一次会话的背景不是这次要的那张：立即停掉，别白解。
            request?.Cancel();
            request?.Dispose();
            return (null, 0);
        }

        try
        {
            return (await task.ConfigureAwait(true), width);
        }
        finally
        {
            request?.Dispose();
        }
    }

    /// <summary>
    /// 把这次上屏的背景记进快照，供下次冷启动提前解码；没有背景时不动旧快照。
    /// 落盘交给线程池：几十毫秒的写文件不值得压在背景上屏的关键路径上。
    /// </summary>
    private static void SaveBackdropSnapshotInBackground(string? backgroundPath, ImageSource? background)
    {
        if (!AppServices.IsInitialized
            || background is null
            || string.IsNullOrWhiteSpace(backgroundPath))
        {
            return;
        }

        var fileSystem = AppServices.FileSystem;
        var logger = AppServices.Logger;
        var target = FrameSnapshotStore.PathIn(AppServices.DataRoot);
        var snapshot = new FrameSnapshot(
            FrameSnapshotStore.CurrentVersion,
            AppServices.CurrentGame?.GameId,
            backgroundPath,
            DateTimeOffset.UtcNow);

        _ = Task.Run(() => FrameSnapshotStore.Save(fileSystem, logger, snapshot, target));
    }

    /// <summary>窗口尺寸变了：去抖后只在确实需要更高分辨率时才按更大宽度重解背景（只放大不缩小）。</summary>
    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_appliedBackdrop is null)
        {
            return;
        }

        var timer = _backdropUpgradeTimer;
        if (timer is null)
        {
            timer = DispatcherQueue?.CreateTimer();
            if (timer is null)
            {
                return;
            }

            timer.Interval = TimeSpan.FromMilliseconds(BackdropUpgradeDelayMilliseconds);
            timer.IsRepeating = false;
            timer.Tick += OnBackdropUpgradeTimerTick;
            _backdropUpgradeTimer = timer;
        }

        timer.Stop();
        timer.Start();
    }

    private void OnBackdropUpgradeTimerTick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();

        if (_appliedBackdrop is not { } applied)
        {
            return;
        }

        try
        {
            var size = AppWindow.Size;
            if (!Services.BackdropDecodeWidth.NeedsUpgrade(applied.Width, size.Width, size.Height))
            {
                return;
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("重算背景解码宽度失败: " + ex.Message);
            }

            return;
        }

        _ = UpdateBackdropAsync();
    }

    /// <summary>当前设置里的背景显示方式；应用还没初始化时按默认「覆盖」处理。</summary>
    private static string? CurrentBackgroundDisplayMode() =>
        AppServices.IsInitialized ? AppServices.Settings.BackgroundDisplayMode : null;

    /// <summary>
    /// 按设置里的「背景显示方式」重画整窗背景：把档位换算成 ImageBrush 的 Stretch 与对齐，
    /// 决定遮罩、模糊铺底是否可见；切到「完整（模糊底）」而铺底还没加载时现场补一次。
    /// </summary>
    private async Task ApplyBackgroundDisplayModeAsync()
    {
        var mode = CurrentBackgroundDisplayMode();
        BackgroundDisplayModeCatalog.ApplyTo(ShellBackgroundBrush, mode);

        var hasBackground = ShellBackgroundBrush.ImageSource is not null;
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Info(
                "背景显示方式: " + mode
                + "（Stretch=" + ShellBackgroundBrush.Stretch
                + ", AlignmentX=" + ShellBackgroundBrush.AlignmentX
                + ", 有图=" + hasBackground + "）");
        }
        ShellScrim.Visibility = hasBackground ? Visibility.Visible : Visibility.Collapsed;

        var wantBlur = hasBackground && BackgroundDisplayModeCatalog.UsesBlurredBackdrop(mode);
        if (wantBlur && ShellBackgroundBlurBrush.ImageSource is null)
        {
            await LoadBlurredBackdropAsync().ConfigureAwait(true);
        }

        ShellBackgroundBlurRect.Visibility =
            wantBlur && ShellBackgroundBlurBrush.ImageSource is not null
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    /// <summary>按 96 像素宽解码当前背景图做模糊铺底；失败只记日志（退化成纯色边，不抛）。</summary>
    private async Task LoadBlurredBackdropAsync()
    {
        if (_backdropPath is null || _bitmapLoader is null || _backdropLifetime is null)
        {
            return;
        }

        try
        {
            ShellBackgroundBlurBrush.ImageSource = await _bitmapLoader
                .LoadAsync(_backdropPath, BackdropBlurDecodeWidth, _backdropLifetime.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("加载背景模糊层失败: " + ex.Message);
            }
        }
    }

    /// <summary>设置页改了背景显示方式：派发到 UI 线程后按新档位重画（不需要重新解码大图）。</summary>
    private void OnBackgroundDisplayChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(() => _ = ApplyBackgroundDisplayModeAsync()))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("MainWindow 无法派发背景显示方式变更");
            }
        }
    }

    /// <summary>设置页改了「Mod 编辑」菜单入口显隐：派发到 UI 线程后重算导航项可见性。</summary>
    private void OnModEditorNavVisibilityChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(UpdateContentNavState))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("MainWindow 无法派发 Mod 编辑菜单显隐变更");
            }
        }
    }

    private void UpdateTitleBar()
    {
        try
        {
            AppTitleBar.Title = _viewModel.HeaderText;
            AppTitleBar.Subtitle = _viewModel.SubtitleText;
            ApplyWindowTitle();
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("更新标题栏失败: " + ex.Message);
            }
        }
    }

    private void ApplyWindowTitle()
    {
        if (_viewModel is null)
        {
            return;
        }

        var title = _viewModel.HeaderText.TrimEnd(' ', '·');
        var version = _viewModel.SubtitleText;
        Title = string.IsNullOrEmpty(version) ? title : title + " · " + version;
    }

    private void ApplyWindowSize()
    {
        try
        {
            var scale = GetScaleFactor();
            var widthDip = AppServices.IsInitialized && AppServices.Settings.WindowWidth > 0
                ? AppServices.Settings.WindowWidth
                : DefaultWindowWidth;
            var heightDip = AppServices.IsInitialized && AppServices.Settings.WindowHeight > 0
                ? AppServices.Settings.WindowHeight
                : DefaultWindowHeight;

            var width = (int)Math.Round(Math.Max(widthDip, MinimumWindowWidth) * scale);
            var height = (int)Math.Round(Math.Max(heightDip, MinimumWindowHeight) * scale);
            AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
            ApplyWindowPosition(width, height);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("设置窗口尺寸失败: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// Win32 兜底：<c>OverlappedPresenter.PreferredMinimumWidth/Height</c> 在 2.7 上只是提示，
    /// 实测仍能拖到 700×500 物理像素。这里用 WM_GETMINMAXINFO 强制最小尺寸，保证主页
    /// 永远有 1040×600 DIP 的可用空间。
    /// <para>
    /// <c>MINMAXINFO.MinTrackSize</c> 是**物理像素**，所以这里乘当前 DPI 缩放；
    /// 反过来把它当 DIP 写死会让 150% 缩放下只锁到 693×400 DIP。
    /// </para>
    /// </summary>
    private void HookMinimumWindowSize()
    {
        if (_minimumSizeHooked)
        {
            return;
        }

        try
        {
            // WinUI 的 WindowNative 句柄是 content child；WM_GETMINMAXINFO/WM_SIZING 只发给顶层窗口。
            var hwnd = GetAncestor(WinRT.Interop.WindowNative.GetWindowHandle(this), 2);
            if (hwnd == IntPtr.Zero)
            {
                throw new InvalidOperationException("GetAncestor(GA_ROOT) 返回空");
            }
            SubclassProc = OnMinimumWindowSizeMessage;
            var ok = SetWindowSubclass(hwnd, SubclassProc, (UIntPtr)1, IntPtr.Zero);
            if (!ok)
            {
                throw new InvalidOperationException("SetWindowSubclass 返回 false");
            }

            _minimumSizeHooked = true;
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("挂载最小窗口尺寸钩子失败: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 先用 OverlappedPresenter 设定最小尺寸（它按 DIP 生效，实测传 1040/600 即可），
    /// 再由 WM_GETMINMAXINFO 按物理像素兜底。两条路径都保留：Presenter 在 WinUI 内部
    /// 生效，钩子处理旧式拖拽边界，任意一条被框架跳过时另一条仍能拦住。
    /// </summary>
    private void ApplyPresenterMinimumSize()
    {
        try
        {
            if (AppWindow.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            {
                return;
            }

            // 实测该属性按**物理像素**生效：150% 下传 1040/600 会锁到 693×400 DIP，
            // 因此必须乘当前 DPI 缩放，才能得到 1040×600 DIP。
            var scale = ResolveUiScale();
            presenter.PreferredMinimumWidth = (int)Math.Round(MinimumWindowWidth * scale);
            presenter.PreferredMinimumHeight = (int)Math.Round(MinimumWindowHeight * scale);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("设置 Presenter 最小尺寸失败: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// UI 实际缩放：优先 <c>XamlRoot.RasterizationScale</c>（窗口未显示时也可靠），
    /// 兜底再用 <c>GetDpiForWindow</c>。构造期 GetDpiForWindow 可能仍返回 96。
    /// </summary>
    private double ResolveUiScale()
    {
        try
        {
            if (RootGrid.XamlRoot is { RasterizationScale: > 0 } root)
            {
                return root.RasterizationScale;
            }
        }
        catch (Exception)
        {
            // 未进可视化树时读 XamlRoot 会抛；退回 DPI 路径。
        }

        return GetScaleFactor();
    }

    /// <summary>WM_GETMINMAXINFO：按当前 DPI 把 track 最小尺寸换成 1040×600 DIP。</summary>
    private IntPtr OnMinimumWindowSizeMessage(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        IntPtr dwRefData)
    {
        // WM_GETMINMAXINFO=0x24 定最小尺寸，WM_SIZING=0x214 拖拽中纠正，WM_WINDOWPOSCHANGING=0x46 兜底。
        if (message != 0x0024 && message != 0x0214 && message != 0x0046)
        {
            return DefSubclassProc(hwnd, message, wParam, lParam);
        }

        try
        {
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
            var minWidth = (int)Math.Round(MinimumWindowWidth * scale);
            var minHeight = (int)Math.Round(MinimumWindowHeight * scale);
            if (message == 0x0214)
            {
                // WM_SIZING：系统已给出拖拽矩形，把小于最小尺寸的边直接推回。
                var dragRect = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeRect>(lParam);
                if (dragRect.Right - dragRect.Left < minWidth)
                {
                    dragRect.Right = dragRect.Left + minWidth;
                }

                if (dragRect.Bottom - dragRect.Top < minHeight)
                {
                    dragRect.Bottom = dragRect.Top + minHeight;
                }

                System.Runtime.InteropServices.Marshal.StructureToPtr(dragRect, lParam, false);
            }
            else if (message == 0x0046)
            {
                // WM_WINDOWPOSCHANGING：直接夹住 cx/cy，系统不再按旧的 1040×600 px 下限截断。
                var pos = System.Runtime.InteropServices.Marshal.PtrToStructure<WindowPos>(lParam);
                if ((pos.Flags & 0x0001) == 0)
                {
                    if (pos.Cx < minWidth)
                    {
                        pos.Cx = minWidth;
                    }

                    if (pos.Cy < minHeight)
                    {
                        pos.Cy = minHeight;
                    }

                    System.Runtime.InteropServices.Marshal.StructureToPtr(pos, lParam, false);
                }
            }
            else if (message == 0x0024)
            {
                var info = System.Runtime.InteropServices.Marshal.PtrToStructure<MinMaxInfo>(lParam);
                // Win32 的 WM_GETMINMAXINFO 以物理像素为单位：DIP * DPI 缩放才是正确值。
                info.MinTrackSize.X = minWidth;
                info.MinTrackSize.Y = minHeight;
                System.Runtime.InteropServices.Marshal.StructureToPtr(info, lParam, false);
            }
            else
            {
                // WM_SIZING：系统已给出矩形，这里把低于最小尺寸的边推回。
                var rect = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeRect>(lParam);
                if (rect.Right - rect.Left < minWidth)
                {
                    rect.Right = rect.Left + minWidth;
                }

                if (rect.Bottom - rect.Top < minHeight)
                {
                    rect.Bottom = rect.Top + minHeight;
                }

                System.Runtime.InteropServices.Marshal.StructureToPtr(rect, lParam, false);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("修正最小窗口尺寸失败: " + ex.Message);
            }
        }

        return IntPtr.Zero;
    }

    /// <summary>窗口被系统/框架压到小于目标尺寸后，主动拉回最小尺寸。</summary>
    private void EnforceMinimumWindowSize()
    {
        try
        {
            var hwnd = GetAncestor(WinRT.Interop.WindowNative.GetWindowHandle(this), 2);
            var dpi = GetDpiForWindow(hwnd);
            var scale = dpi <= 0 ? 1.0 : dpi / 96.0;
            var minWidth = (int)Math.Round(MinimumWindowWidth * scale);
            var minHeight = (int)Math.Round(MinimumWindowHeight * scale);
            var rect = new NativeRect();
            if (!GetWindowRect(hwnd, ref rect))
            {
                return;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;
            if (width >= minWidth && height >= minHeight)
            {
                return;
            }

            SetWindowPos(hwnd, IntPtr.Zero, rect.Left, rect.Top,
                Math.Max(width, minWidth), Math.Max(height, minHeight), 0x0004);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("强制最小窗口尺寸失败: " + ex.Message);
            }
        }
    }

    private delegate IntPtr SubclassProcDelegate(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        IntPtr dwRefData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(
        IntPtr hwnd,
        SubclassProcDelegate proc,
        UIntPtr uIdSubclass,
        IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct WindowPos
    {
        public IntPtr Hwnd;
        public IntPtr HwndInsertAfter;
        public int X;
        public int Y;
        public int Cx;
        public int Cy;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, ref NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr hwndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    /// <summary>
    /// 恢复上次的窗口位置：只有整个窗口都落在某块显示器的工作区内才用；
    /// 拔掉副屏或改过分辨率时保持系统默认位置，避免窗口跑到看不见的地方。
    /// </summary>
    private void ApplyWindowPosition(int width, int height)
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        var x = AppServices.Settings.WindowX;
        var y = AppServices.Settings.WindowY;
        if (x < 0 || y < 0)
        {
            return;
        }

        try
        {
            var area = Microsoft.UI.Windowing.DisplayArea
                .GetFromWindowId(AppWindow.Id, Microsoft.UI.Windowing.DisplayAreaFallback.Nearest)
                .WorkArea;
            if (x < area.X || y < area.Y || x + width > area.X + area.Width || y + height > area.Y + area.Height)
            {
                return;
            }

            AppWindow.Move(new Windows.Graphics.PointInt32(x, y));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("恢复窗口位置失败: " + ex.Message);
        }
    }

    private double GetScaleFactor()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var dpi = GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// 修正标题栏左右留白的宽度。
    /// WinUI 的 TitleBar 在 UpdatePadding() 里把 AppWindow.TitleBar.LeftInset / RightInset
    /// （物理像素）直接当成逻辑像素赋给两端的留白列；高分屏下会多留出
    /// (inset - inset / 缩放) 的空白，把 RightHeader 推离系统 caption buttons。
    /// 这是 microsoft-ui-xaml issue #10344，截至 Windows App SDK 2.3 仍未修复，
    /// 因此这里按当前缩放比例把两侧列宽改回逻辑像素值。
    /// </summary>
    private void FixTitleBarCaptionPadding()
    {
        try
        {
            // 模板根 Grid：第 0 列是左留白，第 11 列是右留白（中间 1-10 列放按钮与标题）。
            if (VisualTreeHelper.GetChild(AppTitleBar, 0) is not Grid root
                || root.ColumnDefinitions.Count < 12)
            {
                return;
            }

            var scale = RootGrid.XamlRoot?.RasterizationScale ?? 1.0;
            if (scale <= 0)
            {
                scale = 1.0;
            }

            var titleBar = AppWindow.TitleBar;
            var isLeftToRight = AppTitleBar.FlowDirection == FlowDirection.LeftToRight;
            var leftInset = isLeftToRight ? titleBar.LeftInset : titleBar.RightInset;
            var rightInset = isLeftToRight ? titleBar.RightInset : titleBar.LeftInset;

            root.ColumnDefinitions[0].Width = new GridLength(leftInset / scale, GridUnitType.Pixel);
            root.ColumnDefinitions[11].Width = new GridLength(rightInset / scale, GridUnitType.Pixel);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("修正标题栏留白失败: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 框架的 UpdatePadding() 只在模板应用与 FlowDirection 变化时执行，
    /// DPI 迁移或窗口尺寸变化后需要自己再修一次，这里挂上两个最省的触发点。
    /// </summary>
    private void HookCaptionPaddingUpdates()
    {
        if (_captionPaddingHooked)
        {
            return;
        }

        var xamlRoot = RootGrid.XamlRoot;
        if (xamlRoot is null)
        {
            return;
        }

        xamlRoot.Changed += OnXamlRootChangedForCaptionPadding;
        AppTitleBar.SizeChanged += OnTitleBarSizeChangedForCaptionPadding;
        _captionPaddingHooked = true;
    }

    private void OnXamlRootChangedForCaptionPadding(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        FixTitleBarCaptionPadding();
    }

    private void OnTitleBarSizeChangedForCaptionPadding(object sender, SizeChangedEventArgs args)
    {
        FixTitleBarCaptionPadding();
    }

    /// <summary>
    /// 系统深浅主题切换后重新给 caption 按钮上色：DWM 只在窗口创建时读一次这些颜色，
    /// 不重新 Apply 的话按钮会一直停在旧主题的前景色上。
    /// </summary>
    private void OnRootGridActualThemeChanged(FrameworkElement sender, object args)
    {
        TitleBarTheme.Apply(AppWindow, RootGrid.ActualTheme);
    }
}
