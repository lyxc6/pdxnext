namespace PdxLauncher.App.Views;

using System.Diagnostics;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdxLauncher.App.Controls;
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

    /// <summary>重载协调器：连点游戏时取消旧重载、只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>两个主题按钮的皮肤对象：悬停提亮 / 按下缩放 / 禁用置灰都要记住主题色。</summary>
    private readonly Dictionary<Button, ThemedButtonSkin> _themedSkins = new();

    /// <summary>
    /// 当前走 WinUI 原生样式的按钮：原生样式只改模板里的 ContentPresenter 前景，
    /// 内容层是 Grid + TextBlock（带本地前景），不手动跟随就会「底灰了字还亮」。
    /// 记下按钮用的是 Default 还是 Accent，禁用时才能取到对应那组文字色。
    /// </summary>
    private readonly Dictionary<Button, NativeButtonLabel> _nativeButtonLabels = new();

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
        // 原生按钮的禁用态文字色来自主题资源，系统主题切换后要重新取一次；
        // 订阅放在构造里只建一次，避免每次 ApplyNativeButton 重复挂接。
        ContinueButton.ActualThemeChanged += OnThemedButtonActualThemeChanged;
        StartButton.ActualThemeChanged += OnThemedButtonActualThemeChanged;
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

    /// <summary>
    /// 重新加载主页数据并应用主题。可安全脱离 await 使用。
    /// <para>连点游戏时由 <see cref="ReloadCoordinator"/> 取消旧重载，只有最后一次的结果会落到界面上。</para>
    /// </summary>
    private Task ReloadAsync() => _reload.RunAsync(
        async token =>
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            var game = AppServices.CurrentGame;
            await _viewModel.LoadAsync(game, token).ConfigureAwait(true);
            await ApplyThemeAsync(game, token).ConfigureAwait(true);
        },
        _lifetime.Token,
        ex =>
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("HomePage 加载失败", ex);
            }
        });

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
            // 只有真正带主题美术的来源才保留这套皮肤；内置占位、随包自带素材（EU5）与
            // Steam 库页背景没有官方按钮素材，继续画幽灵皮肤反而与系统其它按钮不是一套，
            // 改用 WinUI 原生样式。来源 → 外观的决策集中在 HomeButtonStylePolicy，页面只做接线。
            var continueKind = HomeButtonStylePolicy.ResolveStyle(spec.Source, isPrimary: false);
            var startKind = HomeButtonStylePolicy.ResolveStyle(spec.Source, isPrimary: true);
            if (continueKind == HomeButtonStyleKind.Themed)
            {
                ShowThemedLayers(ContinueSurface, ContinueGlow, ContinueInnerRing, ContinueBottomLine);
                ShowThemedLayers(StartSurface, StartGlow, StartInnerRing, StartBottomLine);
                ApplyThemedButton(ContinueButton, ContinueSurface, ContinueGlow, ContinueLabel, visuals);
                ApplyThemedButton(StartButton, StartSurface, StartGlow, StartLabel, visuals);
            }
            else
            {
                ApplyNativeButton(
                    ContinueButton,
                    ContinueSurface,
                    ContinueGlow,
                    ContinueInnerRing,
                    ContinueBottomLine,
                    ContinueLabel,
                    continueKind);
                ApplyNativeButton(
                    StartButton,
                    StartSurface,
                    StartGlow,
                    StartInnerRing,
                    StartBottomLine,
                    StartLabel,
                    startKind);
            }

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

    /// <summary>播放集下拉选择变化：写回视图模型。</summary>
    private void OnPlaySetSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is PlaySet playSet)
        {
            _viewModel.SelectedPlaySet = playSet;
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
        // 上一次可能是原生外观，ApplyNativeButton 在这里挂过 Accent/Default 样式。
        // 切回皮肤必须把样式摘掉：主题外观完全由本地属性和按钮资源表达，不依赖任何 Style；
        // 用 ClearValue 而不是按来源显式重设，是因为无论之前挂的是哪支原生样式都能回到
        // XAML 里的默认样式，不必在皮肤分支里再知道原生样式键。
        button.ClearValue(FrameworkElement.StyleProperty);
        _nativeButtonLabels.Remove(button);

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

    /// <summary>
    /// 没有主题按钮美术时把按钮还原成 WinUI 原生样式：拆掉幽灵皮肤与资源覆盖、清掉主题写进去的
    /// 本地属性、挂回 Accent/Default 样式，并折叠三层自绘内容（高光 / 内描边 / 底线）。
    /// <para>
    /// 同一页面切换游戏时会在「原生 ↔ 主题皮肤」之间来回切，所以这里必须幂等：
    /// 先摘皮肤，再清本地值——本地值优先级高于 Style，不清掉会盖住原生背景 / 前景。
    /// </para>
    /// </summary>
    private void ApplyNativeButton(
        Button button,
        Grid surface,
        Border glow,
        Border innerRing,
        Border bottomLine,
        TextBlock label,
        HomeButtonStyleKind styleKind)
    {
        _themedSkins.Remove(button);

        var styleKey = HomeButtonStylePolicy.StyleKey(styleKind);
        foreach (var (keys, _) in ThemedBrushBindings)
        {
            foreach (var key in keys)
            {
                button.Resources.Remove(key);
            }
        }

        button.Style = Application.Current.Resources.TryGetValue(styleKey, out var style)
            ? style as Style
            : null;

        button.ClearValue(Control.BackgroundProperty);
        button.ClearValue(Control.BorderBrushProperty);
        button.ClearValue(Control.BorderThicknessProperty);
        button.ClearValue(Control.ForegroundProperty);
        button.ClearValue(Control.FontFamilyProperty);
        button.ClearValue(Control.FontSizeProperty);
        button.ClearValue(Control.FontWeightProperty);
        button.ClearValue(Control.PaddingProperty);
        button.ClearValue(Control.CornerRadiusProperty);

        glow.Visibility = Visibility.Collapsed;
        innerRing.Visibility = Visibility.Collapsed;
        bottomLine.Visibility = Visibility.Collapsed;
        surface.Opacity = 1.0;

        label.ClearValue(TextBlock.FontFamilyProperty);
        label.ClearValue(TextBlock.FontSizeProperty);
        label.ClearValue(TextBlock.FontWeightProperty);

        // 内容是 Grid + TextBlock，不经过 ContentPresenter 的文本着色：原生按钮的前景色
        // （Accent 白字 / Default 随主题）要手动接到文字上，否则 ClearValue 会退回主题默认字色，
        // 浅色主题下落在 Accent 蓝底上不可读。
        // 这里只登记「该按钮现在是原生外观」，初始色与后续 IsEnabled / 主题变化都走
        // RefreshNativeButtonForeground，避免只做一次性快照（禁用时底灰了字还亮）。
        _nativeButtonLabels[button] = new NativeButtonLabel(label, styleKind == HomeButtonStyleKind.NativeAccent);
        RefreshNativeButtonForeground(button);

        if (button.RenderTransform is CompositeTransform transform)
        {
            transform.ScaleX = 1.0;
            transform.ScaleY = 1.0;
        }
    }

    /// <summary>切回主题皮肤时恢复三层自绘内容（原生分支里被折叠掉了）。</summary>
    private static void ShowThemedLayers(Grid surface, Border glow, Border innerRing, Border bottomLine)
    {
        surface.Opacity = 1.0;
        glow.Visibility = Visibility.Visible;
        innerRing.Visibility = Visibility.Visible;
        bottomLine.Visibility = Visibility.Visible;
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
        if (sender is not Button button)
        {
            return;
        }

        if (_themedSkins.TryGetValue(button, out var skin))
        {
            skin.SetEnabled(button.IsEnabled);
        }

        RefreshNativeButtonForeground(button);
    }

    private void OnThemedButtonActualThemeChanged(FrameworkElement sender, object args)
    {
        if (sender is Button button)
        {
            RefreshNativeButtonForeground(button);
        }
    }

    /// <summary>
    /// 原生按钮的文字色跟随启用态：启用时读 <see cref="Control.Foreground"/>（样式解析出的
    /// Accent 白字 / Default 主题字色），禁用时读 XAML 里的两枚取色探针
    /// （ButtonForegroundDisabled / TextOnAccentFillColorDisabledBrush）。探针本身是 ThemeResource，
    /// 系统主题切换后会自动更新，这里重读即可，不需要自己缓存颜色。
    /// </summary>
    private void RefreshNativeButtonForeground(Button button)
    {
        if (!_nativeButtonLabels.TryGetValue(button, out var native))
        {
            return;
        }

        native.Label.Foreground = button.IsEnabled
            ? button.Foreground
            : native.Accent
                ? AccentButtonDisabledForegroundProbe.BorderBrush
                : DefaultButtonDisabledForegroundProbe.BorderBrush;
    }

    /// <summary>原生按钮的文字层，以及它用的是 Accent 还是 Default 那组禁用色。</summary>
    private sealed record NativeButtonLabel(TextBlock Label, bool Accent);

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
