namespace PdxLauncher.App;

using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.App.Views;
using PdxLauncher.Core.Contracts;

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
    /// 最小宽度按"播放集页工具条能在一行里排完"倒推：内容区左缘约 237 DIP（导航面板 216 + 页面内边距），
    /// 工具条最小宽度约 740 DIP（搜索框已经压到 MinWidth 96 时的实测值），加上右内边距 16 DIP 约为 993，
    /// 取 1040 留一点余量。再窄就会出现搜索框被压扁、「添加模组」被裁掉。
    /// </summary>
    private const double MinimumWindowWidth = 1040.0;
    private const double MinimumWindowHeight = 600.0;

    /// <summary>
    /// 整窗背景的解码宽度（物理像素）。1200 DIP 的窗口在 150% 缩放下约 1800 px，
    /// 取 2560 保证 4K 下也不糊；2560×1423 的位图约 14.6 MB，与旧的主页横幅同量级。
    /// </summary>
    private const int BackdropDecodeWidth = 2560;

    /// <summary>
    /// 「完整（模糊底）」档那层铺底的解码宽度（物理像素）。96 px 相当于把 2560 宽的图
    /// 降采样 26 倍，细节全部被平均掉，再拉满整窗就是一层柔和的同图底；越小越糊。
    /// </summary>
    private const int BackdropBlurDecodeWidth = 96;

    private readonly NavigationService _navigation;
    private readonly ShellViewModel _viewModel;
    private GameLibraryPage? _libraryPage;
    private BitmapLoader? _bitmapLoader;
    private CancellationTokenSource? _backdropLifetime;

    /// <summary>背景图请求序号：快速切换游戏时只应用最后一次加载结果。</summary>
    private int _backdropVersion;

    /// <summary>当前背景图的完整路径：切到「完整（模糊底）」档要用它补加载低分辨率铺底。</summary>
    private string? _backdropPath;

    private bool _isStarted;

    /// <summary>是否已挂上标题栏留白的重算回调（DPI / 尺寸变化时重算）。</summary>
    private bool _captionPaddingHooked;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    public MainWindow()
    {
        InitializeComponent();

        Title = Localizer.Instance.AppName;

        _navigation = new NavigationService(AppServices.Logger);
        _viewModel = new ShellViewModel(_navigation);
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.NavigationRequested += OnNavigationRequested;

        // 标准做法：把 TitleBar 控件交给系统当拖动区域，窗口按钮仍由系统绘制。
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        ApplyWindowSize();

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
        }

        RootGrid.Loaded += OnRootGridLoaded;
        ContentFrame.Navigated += OnContentFrameNavigated;
        AppServices.CurrentGameChanged += OnCurrentGameChanged;
        AppServices.BackgroundDisplayChanged += OnBackgroundDisplayChanged;
        AlwaysOnTopService.Changed += OnAlwaysOnTopChanged;
        Localizer.Instance.LanguageChanged += OnLanguageChanged;
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

            // 窗口先显示出来，这里才开始扫描 Steam 库（纯本地 IO，不联网）。
            await _viewModel.InitializeAsync(CancellationToken.None).ConfigureAwait(true);
            UpdateTitleBar();
            UpdateContentNavState();

            // 先把整窗背景画出来，再落到主页，避免首页出现时底色闪一下。
            await UpdateBackdropAsync().ConfigureAwait(true);

            // 主页是当前游戏页：没发现任何游戏时它没内容可看，退回游戏库引导用户。
            if (AppServices.CurrentGame is null)
            {
                NavigateTo(NavigationService.LibraryTag);
            }
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

        if (content is GameLibraryPage libraryPage)
        {
            _libraryPage = libraryPage;
            libraryPage.NavigationRequested += OnPageNavigationRequested;
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

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        try
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigationRequested -= OnNavigationRequested;
            AppServices.CurrentGameChanged -= OnCurrentGameChanged;
            AppServices.BackgroundDisplayChanged -= OnBackgroundDisplayChanged;
            AlwaysOnTopService.Changed -= OnAlwaysOnTopChanged;
            Localizer.Instance.LanguageChanged -= OnLanguageChanged;
            RootGrid.ActualThemeChanged -= OnRootGridActualThemeChanged;

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

            _backdropLifetime?.Cancel();
            _backdropLifetime?.Dispose();
            _backdropLifetime = null;
            _bitmapLoader = null;

            if (_libraryPage is not null)
            {
                _libraryPage.NavigationRequested -= OnPageNavigationRequested;
                _libraryPage = null;
            }

            if (!AppServices.IsInitialized)
            {
                return;
            }

            var scale = GetScaleFactor();
            var size = AppWindow.Size;
            AppServices.Settings.WindowWidth = size.Width / scale;
            AppServices.Settings.WindowHeight = size.Height / scale;
            AppServices.SaveSettings();
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("保存窗口尺寸失败: " + ex.Message);
            }
        }
    }

    /// <summary>
    /// 内容管理导航项（播放集 / Mod 库 / DLC）只在当前游戏能管理内容时才出现；
    /// 仅启动的游戏（如 Prison Architect）下直接隐藏，并把停在这些页上的用户送回主页。
    /// </summary>
    private void UpdateContentNavState()
    {
        try
        {
            var game = AppServices.IsInitialized ? AppServices.CurrentGame : null;
            var manageable = game is null || game.SupportsContentManagement;

            // 隐藏而不是置灰：置灰的三项看起来像坏了，而且禁用的 NavigationViewItem 不弹 ToolTip，
            // 只能在左栏底部塞一段解释文字（窄列折行更难看）。干脆不显示。
            var contentVisibility = manageable ? Visibility.Visible : Visibility.Collapsed;
            NavPlaySets.Visibility = contentVisibility;
            NavModLibrary.Visibility = contentVisibility;
            NavDlc.Visibility = contentVisibility;

            if (manageable)
            {
                return;
            }

            if (_navigation.CurrentTag is string tag
                && (string.Equals(tag, NavigationService.PlaySetsTag, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(tag, NavigationService.ModLibraryTag, StringComparison.OrdinalIgnoreCase)
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

        try
        {
            if (!_navigation.Navigate(tag))
            {
                if (AppServices.IsInitialized)
                {
                    AppServices.Logger.Warn("导航失败: " + tag);
                }

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
        }
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
    private void OnLanguageChanged(object? sender, EventArgs e) =>
        SyncAlwaysOnTopButton(AlwaysOnTopButton.IsChecked == true);

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
    private async Task UpdateBackdropAsync()
    {
        if (_bitmapLoader is null || _backdropLifetime is null)
        {
            return;
        }

        var version = ++_backdropVersion;

        ImageSource? background;
        ImageSource? blurred = null;
        try
        {
            ThemeSpec? theme = AppServices.IsInitialized ? AppServices.CurrentGame?.Theme : null;
            var spec = theme ?? ThemeApplier.CreatePlaceholderSpec();

            background = await _bitmapLoader
                .LoadAsync(spec.Background, BackdropDecodeWidth, _backdropLifetime.Token)
                .ConfigureAwait(true);
            _backdropPath = spec.Background;

            // 只有「完整（模糊底）」档才需要那层低分辨率铺底，别的档位省掉一次解码。
            if (background is not null
                && BackgroundDisplayModeCatalog.UsesBlurredBackdrop(CurrentBackgroundDisplayMode()))
            {
                blurred = await _bitmapLoader
                    .LoadAsync(spec.Background, BackdropBlurDecodeWidth, _backdropLifetime.Token)
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

        // 快速连续切换游戏时，旧请求的结果直接丢弃。
        if (version != _backdropVersion)
        {
            return;
        }

        ShellBackgroundBrush.ImageSource = background;
        ShellBackgroundBlurBrush.ImageSource = blurred;
        await ApplyBackgroundDisplayModeAsync().ConfigureAwait(true);
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

    private void UpdateTitleBar()
    {
        try
        {
            AppTitleBar.Title = _viewModel.HeaderText;
            AppTitleBar.Subtitle = _viewModel.SubtitleText;
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("更新标题栏失败: " + ex.Message);
            }
        }
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
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("设置窗口尺寸失败: " + ex.Message);
            }
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
