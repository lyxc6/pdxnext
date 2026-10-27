namespace PdxLauncher.App.Views;

using System.Diagnostics;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 当前游戏主页，布局对齐官方启动器：居中字标页头 + 继续/开始按钮 + 播放集选择器 + 官方公告卡。
/// 整窗背景图由 MainWindow 统一加载铺满，本页负责字标、按钮外观与公告区交互；
/// 配色方案由 <see cref="ThemeApplier"/> 从安装目录主题计算。
/// </summary>
public sealed partial class HomePage : Page
{
    private const int LogoDecodeWidth = 480;

    private readonly HomeViewModel _viewModel;
    private readonly ThemeApplier? _themeApplier;
    private readonly BitmapLoader? _bitmapLoader;
    /// <summary>
    /// Button 模板引用的资源键：不逐个覆盖的话，指针悬停 / 按下 / 禁用会被系统灰、系统蓝重新刷屏
    /// （AccentButtonStyle 走的是 Accent* 那一组键）。这里把「键组 → 用哪支画笔」直接绑成表，
    /// 避免散落多个 foreach 时漏掉 Disabled / Border 分支。
    /// </summary>
    private static readonly (string[] Keys, Func<ThemeVisuals, Brush> Pick)[] ThemedBrushBindings =
    {
        (new[] { "ButtonBackground", "AccentButtonBackground" }, visuals => visuals.ButtonBackground),
        (
            new[]
            {
                "ButtonBackgroundPointerOver",
                "ButtonBackgroundPressed",
                "AccentButtonBackgroundPointerOver",
                "AccentButtonBackgroundPressed",
            },
            visuals => visuals.ButtonHoverBackground),
        (
            new[] { "ButtonBackgroundDisabled", "AccentButtonBackgroundDisabled" },
            visuals => visuals.ButtonBackground),
        (
            new[]
            {
                "ButtonBorderBrush",
                "ButtonBorderBrushPointerOver",
                "ButtonBorderBrushPressed",
                "ButtonBorderBrushDisabled",
                "AccentButtonBorderBrush",
                "AccentButtonBorderBrushPointerOver",
                "AccentButtonBorderBrushPressed",
                "AccentButtonBorderBrushDisabled",
            },
            visuals => visuals.ButtonBorderBrush),
        (new[] { "ButtonForeground", "AccentButtonForeground" }, visuals => visuals.ButtonForeground),
        (
            new[]
            {
                "ButtonForegroundPointerOver",
                "ButtonForegroundPressed",
                "AccentButtonForegroundPointerOver",
                "AccentButtonForegroundPressed",
            },
            visuals => visuals.ButtonHoverForeground),
        (
            new[] { "ButtonForegroundDisabled", "AccentButtonForegroundDisabled" },
            visuals => visuals.ButtonDisabledForeground),
    };

    private CancellationTokenSource _lifetime = new();

    /// <summary>两个主题按钮的皮肤对象：悬停提亮 / 按下缩放 / 禁用置灰都要记住主题色。</summary>
    private readonly Dictionary<Button, ThemedButtonSkin> _themedSkins = new();

    /// <summary>最近一次启动结果：切语言时按新语言重写启动结果 InfoBar。</summary>
    private ProcessStartResult? _lastLaunchResult;

    /// <summary>正在展示「内部链接」公告提示的链接；null 表示当前没有这类提示需要随语言重算。</summary>
    private string? _newsInfoLink;

    public HomePage()
    {
        InitializeComponent();

        _viewModel = new HomeViewModel();
        _viewModel.LaunchCompleted += OnLaunchCompleted;

        // 主题按钮的状态（悬停提亮 / 按下缩放 / 禁用置灰）。PointerPressed 等会被 Button 内部
        // 标记成 handled，必须 handledEventsToo:true 才收得到。
        ContinueButton.IsEnabledChanged += OnThemedButtonIsEnabledChanged;
        StartButton.IsEnabledChanged += OnThemedButtonIsEnabledChanged;
        foreach (var button in new[] { ContinueButton, StartButton })
        {
            button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnThemedButtonPointerPressed), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnThemedButtonPointerReleased), true);
            button.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnThemedButtonPointerCaptureLost), true);
        }

        // ThemeApplier / BitmapLoader 需要 IFileSystem/IAppLogger；服务未就绪时留空，页面退化为内置占位样式。
        _themeApplier = AppServices.IsInitialized
            ? new ThemeApplier(AppServices.FileSystem, AppServices.Logger)
            : null;
        _bitmapLoader = AppServices.IsInitialized
            ? new BitmapLoader(AppServices.FileSystem, AppServices.Logger, AppServices.ThemeImages)
            : null;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public HomeViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.HomeStart, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            AppServices.CurrentGameChanged -= OnCurrentGameChanged;
            AppServices.CurrentGameChanged += OnCurrentGameChanged;

            Localizer.Instance.LanguageChanged -= OnLanguageChanged;
            Localizer.Instance.LanguageChanged += OnLanguageChanged;

            if (!AppServices.IsInitialized)
            {
                _viewModel.SetStatus(Keys.HomeStatusServicesNotReady);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("HomePage 初始化失败", ex);
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
        _lifetime.Cancel();
    }

    /// <summary>切语言后重写 InfoBar 这类不参与 x:Bind 的文案（标题与正文在代码里拼）。</summary>
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        try
        {
            if (_lastLaunchResult is not null)
            {
                ShowLaunchResult(_lastLaunchResult);
            }

            if (_newsInfoLink is not null)
            {
                ShowNewsInfo(Localizer.Instance.T(Keys.HomeNewsInternalLink, _newsInfoLink));
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("切换语言后刷新主页提示失败: " + ex.Message);
            }
        }
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync()))
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("HomePage 无法派发游戏切换事件");
            }
        }
    }

    /// <summary>重新加载主页数据并应用主题；内部吞掉异常，可安全脱离 await 使用。</summary>
    private async Task ReloadAsync()
    {
        try
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            var game = AppServices.CurrentGame;
            await _viewModel.LoadAsync(game, _lifetime.Token).ConfigureAwait(true);
            await ApplyThemeAsync(game, _lifetime.Token).ConfigureAwait(true);
            SyncPlaySetSelection();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("HomePage 加载失败", ex);
            }
        }
    }

    /// <summary>
    /// 加载主题素材：先落地按钮字体（保证文字可读），再异步解码字标。
    /// 背景画不在这里加载——MainWindow 用同一份主题把图铺满整个窗口。
    /// 背景配色沿用 WinUI 主题色，不套用游戏主题的暗色底，避免与系统主题冲突。
    /// </summary>
    private async Task ApplyThemeAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        try
        {
            var spec = game?.Theme ?? ThemeApplier.CreatePlaceholderSpec();
            var visuals = _themeApplier?.Create(spec);
            if (visuals is null)
            {
                return;
            }

            // 「继续」「开始」两个按钮按官方语义渲染：button.backgroundImage 当底图、
            // button.color 当文字色（悬停取 buttonHover.color），不是「拿 color 当底色」。
            ApplyThemedButton(ContinueButton, ContinueSurface, ContinueGlow, ContinueLabel, visuals);
            ApplyThemedButton(StartButton, StartSurface, StartGlow, StartLabel, visuals);

            var loader = _bitmapLoader;
            if (loader is null)
            {
                return;
            }

            var logo = await loader
                .LoadAsync(spec.Logo, LogoDecodeWidth, cancellationToken)
                .ConfigureAwait(true);
            LogoImage.Source = logo;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("应用主题失败: " + ex.Message);
            }
        }
    }

    /// <summary>把视图模型里已经选好的播放集回填进列表，保证打开弹层时当前项高亮。</summary>
    private void SyncPlaySetSelection() => PlaySetList.SelectedItem = _viewModel.SelectedPlaySet;

    private void OnPlaySetFlyoutOpening(object sender, object e) => SyncPlaySetSelection();

    private void OnPlaySetItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is PlaySet playSet)
        {
            _viewModel.SelectedPlaySet = playSet;
        }

        if (PlaySetButton.Flyout is Flyout flyout)
        {
            flyout.Hide();
        }
    }

    private async void OnStartClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.LaunchCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("主页启动游戏失败", ex);
            }
        }
    }

    private async void OnContinueClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 继续游戏走 --continuelastsave，和"开始游戏"是两条不同的启动参数。
            await _viewModel.LaunchContinueCommand.ExecuteAsync(null).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("主页继续游戏失败", ex);
            }
        }
    }

    private void OnLaunchCompleted(object? sender, ProcessStartResult result)
    {
        try
        {
            ShowLaunchResult(result);
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("显示启动结果失败: " + ex.Message);
            }
        }
    }

    /// <summary>把启动结果写进 InfoBar；结果同时留一份，切语言时用新语言重写同一内容。</summary>
    private void ShowLaunchResult(ProcessStartResult result)
    {
        _lastLaunchResult = result;

        if (result.Started)
        {
            HomeInfoBar.Severity = InfoBarSeverity.Success;
            HomeInfoBar.Title = Localizer.Instance.HomeLaunchedTitle;
            HomeInfoBar.Message = result.ProcessId is int pid
                ? Localizer.Instance.T(Keys.HomeProcessCreatedWithPid, pid)
                : Localizer.Instance.HomeProcessCreated;
        }
        else
        {
            HomeInfoBar.Severity = InfoBarSeverity.Error;
            HomeInfoBar.Title = Localizer.Instance.HomeLaunchFailedTitle;
            HomeInfoBar.Message = result.ErrorMessage ?? Localizer.Instance.HomeUnknownReason;
        }

        HomeInfoBar.IsOpen = true;
    }

    /// <summary>
    /// 把主题的按钮外观套到标准 Button 上：底图 / 幽灵底、描边、文字色、字体、字号、字重。
    /// 只设属性不够 —— 默认模板的悬停 / 按下 / 禁用状态由资源键驱动，必须一并覆盖，
    /// 否则一悬停就变回系统灰。悬停提亮、按下缩放、禁用置灰交给 <see cref="ThemedButtonSkin"/>。
    /// </summary>
    private void ApplyThemedButton(Button button, Grid surface, Border glow, TextBlock label, ThemeVisuals visuals)
    {
        // 官方 .themedButton--pureCSS 的 border-radius 是 .25rem = 4，有底图时同样裁到 4。
        var corner = new CornerRadius(4);
        var fontSize = visuals.ButtonFontSize > 0 ? visuals.ButtonFontSize : 24;

        button.Background = visuals.ButtonBackground;
        button.BorderBrush = visuals.ButtonBorderBrush;
        button.BorderThickness = new Thickness(visuals.ButtonBorderThickness);
        button.CornerRadius = corner;
        button.Foreground = visuals.ButtonForeground;
        button.FontFamily = visuals.ButtonFontFamily;
        button.FontSize = fontSize;
        button.FontWeight = visuals.ButtonFontWeight;
        button.Padding = new Thickness(0);

        glow.CornerRadius = corner;

        label.Foreground = visuals.ButtonForeground;
        label.FontFamily = visuals.ButtonFontFamily;
        label.FontSize = fontSize;
        label.FontWeight = visuals.ButtonFontWeight;

        foreach (var (keys, pick) in ThemedBrushBindings)
        {
            var brush = pick(visuals);
            foreach (var key in keys)
            {
                button.Resources[key] = brush;
            }
        }

        var skin = new ThemedButtonSkin(button, surface, glow, label, visuals);
        _themedSkins[button] = skin;
        skin.SetEnabled(button.IsEnabled);
    }

    private void OnNewsMainClick(object sender, RoutedEventArgs e) =>
        OpenNewsLink(_viewModel.NewsMain);

    private void OnNewsSecondary1Click(object sender, RoutedEventArgs e) =>
        OpenNewsLink(_viewModel.NewsSecondary1);

    private void OnNewsSecondary2Click(object sender, RoutedEventArgs e) =>
        OpenNewsLink(_viewModel.NewsSecondary2);

    /// <summary>
    /// 打开公告外链。打开浏览器属于 shell 动作，不是文件内容读写，故不经 IFileSystem
    /// （与设置页「打开数据目录」同一写法）。
    /// pdxlaun:// 这类启动器内部深链本产品接不住：不灰化卡片、也不静默丢弃，
    /// 而是弹一条页内 InfoBar 说明原因（官方同位置的卡本来是可点的）。
    /// </summary>
    private void OpenNewsLink(NewsCardItem? card)
    {
        try
        {
            if (card is null || string.IsNullOrWhiteSpace(card.Link))
            {
                // 官方没有链接的卡就是纯图，点击没有反应，这里保持一致。
                return;
            }

            if (!card.TryGetLink(out var uri))
            {
                if (AppServices.IsInitialized)
                {
                    AppServices.Logger.Info("公告链接不是 http(s)，已改为页内提示: " + card.Link);
                }

                _newsInfoLink = card.Link;
                ShowNewsInfo(Localizer.Instance.T(Keys.HomeNewsInternalLink, card.Link));
                return;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = uri.AbsoluteUri,
                UseShellExecute = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null && AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("打开公告链接失败（进程未创建）: " + uri);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("打开公告链接失败", ex);
            }
        }
    }

    private void OnNewsCardPointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetNewsCardHovered(sender, true);

    private void OnNewsCardPointerExited(object sender, PointerRoutedEventArgs e) =>
        SetNewsCardHovered(sender, false);

    /// <summary>
    /// 悬停遮罩（官方 .news:hover:after 的 rgba(19, 23, 36, .5)）是 XAML 里命名的 Border，
    /// 直接改 Opacity 即可。不改卡片自身的 Background/BorderBrush —— ThemeResource 画笔
    /// 在代码里按主题查不到（ThemeDictionaries 不走 ResourceDictionary.TryGetValue），
    /// 查表失败会静默没有 hover；遮罩的画笔由 XAML 解析，一定生效。
    /// </summary>
    private void SetNewsCardHovered(object sender, bool hovered)
    {
        var veil = ReferenceEquals(sender, btnNewsMain)
            ? NewsHoverVeilMain
            : ReferenceEquals(sender, btnNewsSecondary1)
                ? NewsHoverVeilSecondary1
                : ReferenceEquals(sender, btnNewsSecondary2)
                    ? NewsHoverVeilSecondary2
                    : null;

        if (veil is not null)
        {
            veil.Opacity = hovered ? 1 : 0;
        }
    }

    /// <summary>公告链接无法跳转时的页内提示（放在公告区下方，避免用户往下滚时看不见）。</summary>
    private void ShowNewsInfo(string message)
    {
        try
        {
            NewsInfoBar.Message = message;
            NewsInfoBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("显示公告提示失败: " + ex.Message);
            }
        }
    }

    private void OnThemedButtonPointerEntered(object sender, PointerRoutedEventArgs e) =>
        SetThemedButtonHovered(sender, true);

    private void OnThemedButtonPointerExited(object sender, PointerRoutedEventArgs e) =>
        SetThemedButtonHovered(sender, false);

    private void OnThemedButtonPointerPressed(object sender, PointerRoutedEventArgs e) =>
        SetThemedButtonPressed(sender, true);

    private void OnThemedButtonPointerReleased(object sender, PointerRoutedEventArgs e) =>
        SetThemedButtonPressed(sender, false);

    private void OnThemedButtonPointerCaptureLost(object sender, PointerRoutedEventArgs e) =>
        SetThemedButtonPressed(sender, false);

    private void OnThemedButtonIsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is Button button && _themedSkins.TryGetValue(button, out var skin))
        {
            skin.SetEnabled(button.IsEnabled);
        }
    }

    private void SetThemedButtonHovered(object sender, bool hovered)
    {
        if (sender is Button button && _themedSkins.TryGetValue(button, out var skin))
        {
            skin.SetHovered(hovered);
        }
    }

    private void SetThemedButtonPressed(object sender, bool pressed)
    {
        if (sender is Button button && _themedSkins.TryGetValue(button, out var skin))
        {
            skin.SetPressed(pressed);
        }
    }

    /// <summary>
    /// 主题按钮的运行时状态：悬停提亮叠层、按下缩放、禁用置灰。
    /// 只改内容层与 RenderTransform，<b>绝不</b>动 Button.Background —— ThemeResource 画笔在代码里
    /// 按主题查不到（ThemeDictionaries 不走 ResourceDictionary.TryGetValue），改了会静默失效；
    /// 提亮层由 XAML 解析的纯色画笔实现（与游戏库 hover 环同一做法）。
    /// </summary>
    private sealed class ThemedButtonSkin
    {
        /// <summary>有底图时悬停提亮的白色叠层不透明度（官方是 CSS brightness(115%)，深色图上提亮空间很小）。</summary>
        private const double HoverGlowArt = 0.08;

        /// <summary>幽灵按钮（无底图）悬停提亮的叠层不透明度。</summary>
        private const double HoverGlowGhost = 0.18;

        /// <summary>按下时的缩放（官方 .themedButton__container--isPressed 的 scale(.85)）。</summary>
        private const double PressedScale = 0.85;

        /// <summary>禁用时的整体不透明度（官方是 opacity .8 + grayscale，这里做整数倍近似）。</summary>
        private const double DisabledOpacity = 0.55;

        private readonly Button _button;
        private readonly Grid _surface;
        private readonly Border _glow;
        private readonly TextBlock _label;
        private readonly ThemeVisuals _visuals;
        private readonly double _hoverGlow;
        private bool _hovered;
        private bool _pressed;
        private bool _enabled = true;

        public ThemedButtonSkin(Button button, Grid surface, Border glow, TextBlock label, ThemeVisuals visuals)
        {
            _button = button;
            _surface = surface;
            _glow = glow;
            _label = label;
            _visuals = visuals;
            _hoverGlow = visuals.ButtonUsesArt ? HoverGlowArt : HoverGlowGhost;
        }

        public void SetHovered(bool hovered)
        {
            _hovered = hovered;
            Apply();
        }

        public void SetPressed(bool pressed)
        {
            _pressed = pressed;
            Apply();
        }

        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            Apply();
        }

        private void Apply()
        {
            _glow.Opacity = _enabled && _hovered ? _hoverGlow : 0;
            _label.Foreground = !_enabled
                ? _visuals.ButtonDisabledForeground
                : _hovered
                    ? _visuals.ButtonHoverForeground
                    : _visuals.ButtonForeground;
            _surface.Opacity = _enabled ? 1.0 : DisabledOpacity;

            var scale = _pressed && _enabled ? PressedScale : 1.0;
            if (_button.RenderTransform is CompositeTransform transform)
            {
                transform.ScaleX = scale;
                transform.ScaleY = scale;
            }
        }
    }
}
