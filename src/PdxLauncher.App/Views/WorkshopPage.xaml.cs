namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Controls;

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
            new WorkshopFilterCatalogCache(AppServices.FileSystem, AppServices.WorkshopFilterRoot),
            AppServices.Logger,
            new WorkshopStateReader(
                SteamWorkshopBackends.CreateForState,
                AppServices.WorkshopStates,
                AppServices.Logger));

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>用户点了「去登录」：交给外壳导航到账号页（页面自己不持有导航职责）。</summary>
    public event EventHandler? AccountRequested;

    /// <summary>
    /// 用户点了某一行：交给外壳导航到 <see cref="WorkshopDetailPage"/>。
    /// 参数除了这一行还带上返回目标（本页），与 Mod 库页共用同一种请求。
    /// </summary>
    public event EventHandler<WorkshopDetailNavigationRequest>? DetailRequested;

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

            // x:Bind 在 InitializeComponent 里按属性顺序赋值，下拉控件的 SelectedItem 可能先于
            // ItemsSource 生效而被清空；这里兜底补一次，保证排序框初始显示当前排序方式。
            if (SortBox.SelectedItem is null)
            {
                SortBox.SelectedItem = _viewModel.SelectedSortMode;
            }

            if (TimeRangeBox.SelectedItem is null)
            {
                TimeRangeBox.SelectedItem = _viewModel.SelectedTimeRange;
            }

            if (PageSizeBox.SelectedItem is null)
            {
                PageSizeBox.SelectedItem = _viewModel.SelectedPageSize;
            }

            if (SpecialFilterBox.SelectedItem is null)
            {
                SpecialFilterBox.SelectedItem = _viewModel.Filters.CurrentSpecialFilterLabel;
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
                // 列表不重查，但筛选目录有 24h TTL / 本地版本判断；过期时后台补一次。
                _ = _viewModel.RefreshCatalogIfStaleAsync(_lifetime.Token);

                // 详情子页里可能改过订阅 / 触发过下载，那边已经失效了状态缓存；
                // 这里补一次，回来就能看到新的徽标（命中缓存时不新起子进程）。
                _viewModel.RefreshSteamStates();
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
        _viewModel.CancelPendingQuery();
        _viewModel.CancelPendingFilterQuery();
        _viewModel.CancelCatalogRefresh();
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
    /// 由这里把用户输入写回 ViewModel。写回只更新文本，**不发请求**：
    /// 真正提交的是回车/放大镜（<see cref="OnSearchQuerySubmitted"/>）或刷新按钮。
    /// 建议只跟"手打"走：程序化改 Text（绑定回写、恢复上次搜索）也会走 TextChanged，
    /// 那种时候弹下拉就是和用户抢焦点。
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        try
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            var text = sender.Text ?? string.Empty;
            if (!string.Equals(text, _viewModel.SearchText, StringComparison.Ordinal))
            {
                _viewModel.SearchText = text;
            }

            SearchSuggestBox.Update(sender, AppServices.Settings.WorkshopSearchHistory, text);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 搜索框回写失败", ex);
        }
    }

    /// <summary>
    /// 提交搜索：回车、点放大镜、或从下拉里选一条最近搜索，都到这里。
    /// 打字过程中不查询 —— 每个字符都会排一次 1–3 MB 的工坊页请求，页面看起来就像卡死。
    /// 提交成功后把关键词记进最近搜索；刷新按钮走 <see cref="OnRefreshClick"/>，不记历史。
    /// </summary>
    private async void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var text = (args.QueryText ?? string.Empty).Trim();
        // 选中建议时 AutoSuggestBox 会自己把该条填进框里，这里再显式写回一次：
        // 绑定是 OneWay，不写回的话 ViewModel 可能还停在旧词上。
        _viewModel.SearchText = text;
        sender.IsSuggestionListOpen = false;

        await CommitSearchWithHistoryAsync(text).ConfigureAwait(true);
    }

    /// <summary>提交一次搜索；成功且关键词非空时记进设置的工坊最近搜索。</summary>
    private async Task CommitSearchWithHistoryAsync(string text)
    {
        try
        {
            await _viewModel.CommitSearchAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
            return;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 搜索提交失败", ex);
            return;
        }

        if (text.Length == 0)
        {
            return;
        }

        AppServices.Settings.WorkshopSearchHistory = SearchSuggestions.Push(AppServices.Settings.WorkshopSearchHistory, text);
        AppServices.SaveSettingsDeferred();
    }

    /// <summary>排序下拉框选择变化：写回 ViewModel 触发重查。</summary>
    private void OnSortSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not string mode)
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

    /// <summary>浏览区段下拉框选择变化：项目 / 合集，写回 ViewModel 触发重查（仅 Steam）。</summary>
    private void OnSectionSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not string section)
            {
                return;
            }

            if (string.Equals(section, _viewModel.SelectedSection, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SelectedSection = section;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换浏览区段失败", ex);
        }
    }

    /// <summary>时间范围下拉框选择变化：写回 ViewModel 触发重查（仅 Steam）。</summary>
    private void OnTimeRangeSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not string range)
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

    /// <summary>点页码：直接跳到那一页（0 基，来自按钮的 Tag）。</summary>
    private async void OnPageNumberClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement element || element.Tag is not int pageIndex)
            {
                return;
            }

            await _viewModel.GoToPageAsync(pageIndex, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 跳页失败", ex);
        }
    }

    /// <summary>每页条数变化：写回 ViewModel（会回到第 1 页并跳过缓存）。</summary>
    private async void OnPageSizeSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not int pageSize)
            {
                return;
            }

            await _viewModel.ChangePageSizeAsync(pageSize).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换每页条数失败", ex);
        }
    }

    /// <summary>特别筛选条件下拉变化：写回下标，由 ViewModel 去抖后重查。</summary>
    private void OnSpecialFilterSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not string label)
            {
                return;
            }

            var modes = _viewModel.Filters.SpecialFilterModes;
            var index = -1;
            for (var i = 0; i < modes.Count; i++)
            {
                if (string.Equals(modes[i], label, StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0 || index == _viewModel.Filters.SpecialFilterIndex)
            {
                return;
            }

            _viewModel.Filters.SpecialFilterIndex = index;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换特别筛选失败", ex);
        }
    }

    /// <summary>
    /// 点一个筛选胶囊：未选 → 必须包含 → 必须排除 → 未选。
    /// 状态只改 ViewModel，胶囊本身只负责显示；去抖与重新查询由 Filters 统一负责。
    /// </summary>
    private void OnTagChipClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: WorkshopTagOption option })
            {
                option.Cycle();
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 切换筛选胶囊失败", ex);
        }
    }

    /// <summary>点分组标题：展开 / 收起这一组（标题按钮的 DataContext 就是分组视图模型）。</summary>
    private void OnFilterGroupHeaderClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkshopFilterGroupViewModel group })
        {
            group.IsExpanded = !group.IsExpanded;
        }
    }

    /// <summary>清空全部筛选条件（标签、DLC、不兼容、特别筛选）。</summary>
    private void OnClearFiltersClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.Filters.Clear();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 清除筛选失败", ex);
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

    /// <summary>点一行：把这一行交给外壳，导航到详情子页（返回目标写本页）。</summary>
    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is WorkshopRowViewModel row)
        {
            DetailRequested?.Invoke(this, new WorkshopDetailNavigationRequest(row, NavigationService.WorkshopTag));
        }
    }
}
