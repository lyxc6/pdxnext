namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「添加模组」子页：列出 mod 库里还没进目标播放集的 mod，让用户多选。
/// </summary>
/// <remarks>
/// 页面自己不持导航职责：返回时抛 <see cref="NavigationRequested"/>（回到播放集页），
/// 确认时抛 <see cref="ModsSelected"/>（把选择结果交回播放集页，由它追加并自动保存）。
/// </remarks>
public sealed partial class AddModsPage : Page
{
    private readonly AddModsViewModel _viewModel;
    private CancellationTokenSource _lifetime = new();

    private AddModsNavigationRequest? _request;

    /// <summary>上一次给建议框算过候选的查询文本；相同文本不重复刷新。</summary>
    private string? _lastSuggestQuery;

    /// <summary>创建页面。</summary>
    public AddModsPage()
    {
        InitializeComponent();

        _viewModel = new AddModsViewModel(
            AppServices.PlaySets,
            new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger),
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>ViewModel。</summary>
    public AddModsViewModel ViewModel => _viewModel;

    /// <summary>XAML 的文案入口：<c>{x:Bind L.SomeKey, Mode=OneWay}</c>。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>需要返回上一层（播放集页）时触发。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>用户确认添加时触发，参数是勾选的 mod。</summary>
    public event EventHandler<AddModsNavigationResult>? ModsSelected;

    /// <summary>导航进入时取参数：目标播放集。</summary>
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _request = e.Parameter as AddModsNavigationRequest;
    }

    /// <summary>离开页面时记住排序方式，并立即落盘（下次进入或重启后恢复）。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        SaveSortState();
        PageStateStore.Persist();
        base.OnNavigatedFrom(e);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_lifetime.IsCancellationRequested)
        {
            _lifetime.Dispose();
            _lifetime = new CancellationTokenSource();
        }

        if (_request is null)
        {
            // 缺少参数就不猜：回到播放集页，让用户从工具条重新进入。
            AppServices.Logger.Warn("添加模组页缺少目标播放集参数");
            OnBackClick(sender, e);
            return;
        }

        _viewModel.SetPlaySet(_request.PlaySetName);

        // 页面是重建的：先把上次离开时的排序方式放回去，再加载候选。
        RestoreSortState();

        // x:Bind 在 InitializeComponent 里按属性顺序赋值，下拉控件的 SelectedItem 可能先于
        // ItemsSource 生效而被清空；这里兜底补一次，保证排序框初始显示当前排序方式。
        if (SortBox.SelectedItem is null)
        {
            SortBox.SelectedItem = _viewModel.SelectedSortMode;
        }

        // 同排序框：x:Bind 按属性顺序赋值，来源 / 版本下拉的 SelectedItem 也可能先于 ItemsSource 被清空。
        if (SourceFilterBox.SelectedItem is null)
        {
            SourceFilterBox.SelectedItem = _viewModel.SelectedSourceFilter;
        }

        if (VersionFilterBox.SelectedItem is null)
        {
            VersionFilterBox.SelectedItem = _viewModel.SelectedVersionFilter;
        }

        await LoadAsync().ConfigureAwait(true);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _lifetime.Cancel();
    }

    /// <summary>读取候选 mod。</summary>
    private async Task LoadAsync()
    {
        if (_request is null)
        {
            return;
        }

        try
        {
            await _viewModel
                .LoadAsync(AppServices.CurrentGame, _request.PlaySetId, _lifetime.Token)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已经离开：本次加载作废。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("添加模组页加载失败", ex);
        }
    }

    /// <summary>
    /// 搜索框实时过滤：Text 是 OneTime 绑定（只做初始赋值），输入由这里单向写回 ViewModel，
    /// 绑定不会再反写控件，所以不存在「控件 → VM → 控件」的来回回写。
    /// 建议只跟"手打"走：程序化改 Text（绑定回写）也会触发 TextChanged，那时弹下拉会抢焦点。
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
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

        // 文本真的变了才重建建议列表：同一个值重复触发时再设一遍 ItemsSource +
        // IsSuggestionListOpen，下拉会闪一下，也会把已经选中的候选重置掉。
        if (string.Equals(_lastSuggestQuery, text, StringComparison.Ordinal))
        {
            return;
        }

        _lastSuggestQuery = text;
        SearchSuggestBox.Update(sender, _viewModel.Rows.Select(row => row.Name), text);
    }

    /// <summary>回车 / 放大镜 / 选中建议：过滤用的就是当前文本，收起下拉即可。</summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _viewModel.SearchText = (args.QueryText ?? string.Empty).Trim();
        _lastSuggestQuery = null;
        sender.IsSuggestionListOpen = false;
    }

    /// <summary>排序下拉框选择变化：写回 ViewModel 触发重排，并同步页面状态。</summary>
    private void OnSortSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
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
        SaveSortState();
    }

    /// <summary>来源下拉选择变化：写回 ViewModel 触发重建（与搜索 / 版本 / 排序叠加）。</summary>
    private void OnSourceFilterSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not string mode
            || string.Equals(mode, _viewModel.SelectedSourceFilter, StringComparison.Ordinal))
        {
            return;
        }

        _viewModel.SelectedSourceFilter = mode;
    }

    /// <summary>版本兼容性下拉选择变化：写回 ViewModel 触发重建（与搜索 / 来源 / 排序叠加）。</summary>
    private void OnVersionFilterSelectionChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is not string mode
            || string.Equals(mode, _viewModel.SelectedVersionFilter, StringComparison.Ordinal))
        {
            return;
        }

        _viewModel.SelectedVersionFilter = mode;
    }

    /// <summary>把上次选的排序方式套回 ViewModel 与下拉框；旧语言的文案对不上选项时保持默认。</summary>
    private void RestoreSortState()
    {
        var state = PageStateStore.AddMods;
        if (state is null || string.IsNullOrEmpty(state.SortMode))
        {
            return;
        }

        if (!_viewModel.SortModes.Any(mode => string.Equals(mode, state.SortMode, StringComparison.Ordinal)))
        {
            return;
        }

        _viewModel.SelectedSortMode = state.SortMode;
        SortBox.SelectedItem = state.SortMode;
    }

    /// <summary>把当前排序方式写进页面状态（内存态）；落盘由离开页面或关窗时统一做。</summary>
    private void SaveSortState()
    {
        PageStateStore.AddMods = new AddModsState(_viewModel.SelectedSortMode);
    }

    /// <summary>返回播放集页（不添加任何 mod）。</summary>
    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        NavigationRequested?.Invoke(this, _request?.ReturnTag ?? NavigationService.PlaySetsTag);
    }

    /// <summary>取消：等价于返回。</summary>
    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        OnBackClick(sender, e);
    }

    /// <summary>
    /// 行右键菜单里的「重试翻译」：只重跑缺译名的那一批，不重读 mod 库、不打乱勾选与滚动位置。
    /// </summary>
    private void OnTranslationRetryClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RetryNameTranslation();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("添加模组页重试翻译失败：" + ex.Message);
        }
    }

    /// <summary>行右键菜单里的「刷新译文」：忽略缓存与已有译名，本页候选全部强制重翻。</summary>
    private void OnTranslationRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _viewModel.RefreshNameTranslation();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("添加模组页刷新译文失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 行右键菜单即将打开：翻译菜单项是页面级动作，挂载在行模板上绑不到页面 ViewModel，
    /// 就在这里按当前状态统一设 IsEnabled。
    /// </summary>
    private void OnTranslationMenuOpening(object sender, object e)
    {
        if (sender is not MenuFlyout flyout)
        {
            return;
        }

        var retryEnabled = _viewModel.CanRetryTranslation;
        var refreshEnabled = AppServices.Translation.IsEnabled;
        foreach (var item in flyout.Items)
        {
            if (item is not MenuFlyoutItem menuItem)
            {
                continue;
            }

            menuItem.IsEnabled = AutomationProperties.GetAutomationId(menuItem) switch
            {
                "menuAddModsTranslationRetry" => retryEnabled,
                "menuAddModsTranslationRefresh" => refreshEnabled,
                _ => menuItem.IsEnabled,
            };
        }
    }

    /// <summary>确认添加：把勾选的 mod 交回播放集页。</summary>
    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        if (_request is null)
        {
            return;
        }

        var entries = _viewModel.GetSelectedEntries();
        if (entries.Count == 0)
        {
            return;
        }

        ModsSelected?.Invoke(this, new AddModsNavigationResult(_request.PlaySetId, entries));
    }
}
