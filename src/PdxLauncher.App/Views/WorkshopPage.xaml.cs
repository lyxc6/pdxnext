namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊页：按当前游戏显示 Steam 创意工坊或 Paradox Mods（二选一，没有来源切换页签）。
/// 页面只负责生命周期、控件事件与「请求打开某一行的详情」；来源判定与查询都在 <see cref="WorkshopViewModel"/> 里，
/// 详情正文则在独立的 <see cref="WorkshopDetailPage"/> 子页展示（原先是一个 MaxHeight=560 的弹窗，装不下长描述）。
/// </summary>
public sealed partial class WorkshopPage : Page
{
    private readonly WorkshopViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    private bool _isReloading;

    /// <summary>列表是否已经加载过（用于「从详情子页返回」时不重拉，保住当前页与搜索条件）。</summary>
    private bool _isListLoaded;

    /// <summary>上次加载列表时所属的游戏 id。</summary>
    private string? _loadedGameId;

    public WorkshopPage()
    {
        InitializeComponent();

        _viewModel = new WorkshopViewModel(
            AppServices.SteamWorkshop,
            AppServices.PdxMods,
            AppServices.Account,
            AppServices.Assets,
            AppServices.WorkshopInfo,
            AppServices.WorkshopPages,
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>用户点了「去登录」：交给外壳导航到账号页（页面自己不持有导航职责）。</summary>
    public event EventHandler? AccountRequested;

    /// <summary>用户点了某一行：交给外壳导航到 <see cref="WorkshopDetailPage"/>（参数就是这一行）。</summary>
    public event EventHandler<WorkshopRowViewModel>? DetailRequested;

    public WorkshopViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

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

            // x:Bind 在 InitializeComponent 里按属性顺序赋值，ComboBox 的 SelectedItem 可能先于
            // ItemsSource 生效而被清空；这里兜底补一次，保证排序框初始显示当前排序方式。
            if (SortBox.SelectedItem is null)
            {
                SortBox.SelectedItem = _viewModel.SelectedSortMode;
            }

            if (TimeRangeBox.SelectedItem is null)
            {
                TimeRangeBox.SelectedItem = _viewModel.SelectedTimeRange;
            }

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
                return;
            }

            // 从详情子页返回时 Loaded 会再触发一次；此时沿用已加载的列表，
            // 否则会把用户翻到的页码和搜索条件一起冲掉（弹窗时代不会发生）。
            if (_isListLoaded &&
                string.Equals(_loadedGameId, AppServices.CurrentGame?.GameId, StringComparison.Ordinal))
            {
                return;
            }

            await ReloadAsync().ConfigureAwait(true);
            _isListLoaded = true;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await ReloadAsync().ConfigureAwait(true);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
                {
                    // 页面已关闭导致的取消，忽略。
                }
                catch (Exception ex)
                {
                    AppServices.Logger.Error("WorkshopPage 切换游戏后刷新失败", ex);
                }
            }))
        {
            AppServices.Logger.Warn("WorkshopPage 无法派发游戏切换事件");
        }
    }

    private async Task ReloadAsync()
    {
        if (_isReloading)
        {
            return;
        }

        _isReloading = true;
        try
        {
            await _viewModel.LoadAsync(AppServices.CurrentGame, _lifetime.Token).ConfigureAwait(true);
            _loadedGameId = AppServices.CurrentGame?.GameId;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 加载失败", ex);
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ------------------------------------------------------------ 控件事件

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 刷新失败", ex);
        }
    }

    /// <summary>
    /// 搜索框输入。x:Bind 不支持 UpdateSourceTrigger，所以 Text 只做 OneWay 绑定，
    /// 由这里把用户输入写回 ViewModel（写回会触发回到第一页重查）。
    /// </summary>
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not TextBox box)
            {
                return;
            }

            var text = box.Text ?? string.Empty;
            if (string.Equals(text, _viewModel.SearchText, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SearchText = text;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 搜索失败", ex);
        }
    }

    /// <summary>排序下拉框选择变化：写回 ViewModel 触发重查。</summary>
    private void OnSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox box || box.SelectedItem is not string mode)
            {
                return;
            }

            if (string.Equals(mode, _viewModel.SelectedSortMode, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SelectedSortMode = mode;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换排序失败", ex);
        }
    }

    /// <summary>时间范围下拉框选择变化：写回 ViewModel 触发重查（仅 Steam）。</summary>
    private void OnTimeRangeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox box || box.SelectedItem is not string range)
            {
                return;
            }

            if (string.Equals(range, _viewModel.SelectedTimeRange, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SelectedTimeRange = range;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换时间范围失败", ex);
        }
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.TurnPageAsync(-1, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 上一页失败", ex);
        }
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.TurnPageAsync(1, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 下一页失败", ex);
        }
    }

    /// <summary>未登录提示条上的「去登录」：抛给外壳去导航，页面不直接翻 Frame。</summary>
    private void OnSignInClick(object sender, RoutedEventArgs e) =>
        AccountRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>页头「在浏览器中打开」：打开当前来源的网站首页。</summary>
    private void OnOpenWebClick(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.Source switch
        {
            WorkshopSource.Steam when _viewModel.Game?.SteamAppId is { } appId
                => "https://steamcommunity.com/app/" + appId + "/workshop/",
            WorkshopSource.Pdx => "https://mods.paradoxplaza.com/",
            _ => null,
        };

        if (url is null)
        {
            _viewModel.SetStatus(L.WorkshopNoGameHint);
            return;
        }

        SetStatusAfterOpen(ShellLauncher.TryOpenUrl(url, "创意工坊页"), L.WorkshopOpenBrowser);
    }

    /// <summary>行内「在浏览器中打开」。</summary>
    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkshopRowViewModel row })
        {
            SetStatusAfterOpen(ShellLauncher.TryOpenUrl(row.WebUrl, "创意工坊页"), L.WorkshopOpenBrowser);
        }
    }

    /// <summary>行内「在 Steam 中打开」。</summary>
    private void OnOpenSteamClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkshopRowViewModel row }
            && !string.IsNullOrWhiteSpace(row.SteamUrl))
        {
            ShellLauncher.TryOpenSteam(row.SteamUrl, "创意工坊页");
        }
    }

    /// <summary>打开外链后，成功就把提示条刷成对应文案，失败则留给日志。</summary>
    /// <param name="opened">是否成功交给 shell。</param>
    /// <param name="message">成功时显示的提示。</param>
    private void SetStatusAfterOpen(bool opened, string message)
    {
        if (opened)
        {
            _viewModel.SetStatus(message);
        }
    }

    /// <summary>点一行：把这一行交给外壳，导航到详情子页。</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WorkshopRowViewModel row)
        {
            DetailRequested?.Invoke(this, row);
        }
    }
}
