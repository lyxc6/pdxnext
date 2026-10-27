namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>游戏库页：GridView 卡片列表，点卡片进当前游戏页，点"启动"直接拉起游戏。</summary>
public sealed partial class GameLibraryPage : Page
{
    private readonly LibraryViewModel _viewModel;
    private CancellationTokenSource _lifetime = new();
    private ProcessStartResult? _lastLaunchResult;

    public GameLibraryPage()
    {
        InitializeComponent();

        _viewModel = new LibraryViewModel();
        _viewModel.GameActivated += OnGameActivated;
        _viewModel.LaunchCompleted += OnLaunchCompleted;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>页面把导航意图抛给主窗口，自己无法持有 NavigationService。</summary>
    public event EventHandler<string>? NavigationRequested;

    public LibraryViewModel ViewModel => _viewModel;

    public Localizer L => Localizer.Instance;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            AppServices.ThemesChanged -= OnThemesChanged;
            AppServices.ThemesChanged += OnThemesChanged;

            Localizer.Instance.LanguageChanged -= OnLanguageChanged;
            Localizer.Instance.LanguageChanged += OnLanguageChanged;

            if (!AppServices.IsInitialized)
            {
                _viewModel.SetStatus(Keys.LibraryStatusServicesNotReady);
                return;
            }

            await _viewModel.LoadAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("GameLibraryPage 初始化失败", ex);
            }
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime.Cancel();
        AppServices.ThemesChanged -= OnThemesChanged;
        Localizer.Instance.LanguageChanged -= OnLanguageChanged;
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
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("切换语言后刷新游戏库提示失败: " + ex.Message);
            }
        }
    }

    /// <summary>后台主题刷新换了一张封面 → 重建卡片，让新图立刻显示。</summary>
    private void OnThemesChanged(object? sender, string gameId)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                if (!AppServices.IsInitialized)
                {
                    return;
                }

                try
                {
                    _viewModel.ReloadFromCurrentGames();
                }
                catch (Exception ex)
                {
                    AppServices.Logger.Warn("游戏库刷新主题素材失败（gameId=" + gameId + "）: " + ex.Message);
                }
            }))
        {
            AppServices.Logger.Warn("GameLibraryPage 无法派发主题更新事件");
        }
    }

    private void OnGameCardClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is GameCardItem card)
            {
                _viewModel.Activate(card);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Warn("选择游戏失败: " + ex.Message);
            }
        }
    }

    private void OnGameActivated(object? sender, GameCardItem card)
    {
        NavigationRequested?.Invoke(this, NavigationService.HomeTag);
    }

    private async void OnCardLaunchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: GameCardItem card })
            {
                await _viewModel.LaunchCommand.ExecuteAsync(card).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("启动游戏失败", ex);
            }
        }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e) => SetCardHovered(sender, hovered: true);

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e) => SetCardHovered(sender, hovered: false);

    /// <summary>
    /// 容器的 pointer-over 底色与描边已置透明，容器被回收复用时也要把卡片归位，
    /// 否则上一次被悬停过的卡片样式会跟着容器带到新条目上。
    /// </summary>
    private void OnGameContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Phase != 0 || args.ItemContainer is not ContentControl { ContentTemplateRoot: Border card })
        {
            return;
        }

        SetCardHovered(card, hovered: false);
    }

    /// <summary>
    /// 悬停反馈由卡片里叠加的 <c>CardHoverRing</c> 表达：只切 Visibility，不改卡片自身的主题画笔。
    /// ThemeResource 画笔在代码里按主题查不到（ThemeDictionaries 不走 ResourceDictionary.TryGetValue），
    /// 交给 XAML 解析才稳定生效，也就不会出现"查表失败、静默没有 hover"。
    /// </summary>
    private static void SetCardHovered(object sender, bool hovered)
    {
        if (sender is Border card && FindHoverRing(card) is { } ring)
        {
            ring.Visibility = hovered ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>在卡片可视树里找叠加层：DataTemplate 的 namescope 不能保证可 FindName，按 Name 逐层找。</summary>
    private static Border? FindHoverRing(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Border { Name: "CardHoverRing" } ring)
            {
                return ring;
            }

            if (FindHoverRing(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        try
        {
            await _viewModel.RefreshAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            if (AppServices.IsInitialized)
            {
                AppServices.Logger.Error("刷新游戏库失败", ex);
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

    /// <summary>把启动结果写进 InfoBar；结果留一份，切语言时用新语言重写同一内容。</summary>
    private void ShowLaunchResult(ProcessStartResult result)
    {
        _lastLaunchResult = result;

        if (result.Started)
        {
            LibraryInfoBar.Severity = InfoBarSeverity.Success;
            LibraryInfoBar.Title = Localizer.Instance.LibraryLaunchedTitle;
            LibraryInfoBar.Message = result.ProcessId is int pid
                ? Localizer.Instance.T(Keys.LibraryProcessCreatedWithPid, pid)
                : Localizer.Instance.LibraryProcessCreated;
        }
        else
        {
            LibraryInfoBar.Severity = InfoBarSeverity.Error;
            LibraryInfoBar.Title = Localizer.Instance.LibraryLaunchFailedTitle;
            LibraryInfoBar.Message = result.ErrorMessage ?? Localizer.Instance.LibraryUnknownReason;
        }

        LibraryInfoBar.IsOpen = true;
    }
}
