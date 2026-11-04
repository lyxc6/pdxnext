namespace PdxLauncher.App.Views;

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库页：列出当前游戏的全部 mod（mods 表 + 磁盘本地 mod），支持搜索与排序。
/// 页面只负责生命周期和控件事件回写，加载 / 过滤 / 排序都在 <see cref="ModLibraryViewModel"/> 里完成。
/// </summary>
public sealed partial class ModLibraryPage : Page
{
    private readonly ModLibraryViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Disabled 会在每次进入时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    private bool _isReloading;

    /// <summary>从 <see cref="PageStateStore"/> 取回的滚动位置；加载完列表后消费一次。</summary>
    private double _pendingOffset;

    public ModLibraryPage()
    {
        InitializeComponent();

        _viewModel = new ModLibraryViewModel(
            new ModLibraryService(
                AppServices.PlaySets,
                AppServices.Mods,
                AppServices.Logger,
                SteamWorkshopBackends.CreateForState,
                AppServices.WorkshopStates),
            AppServices.ModThumbnails,
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModLibraryViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>
    /// 请求打开某一行的工坊详情子页；参数是详情行 + 返回目标（本页）。
    /// 页面本身不导航 —— 导航由外壳 <see cref="MainWindow"/> 统一负责，回到本页也由它决定。
    /// </summary>
    public event EventHandler<WorkshopDetailNavigationRequest>? DetailRequested;

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

            // 页面是重建的：把上次离开时的搜索词/排序/来源筛选先放回去，再加载列表。
            RestoreState();

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
                await RestoreScrollAsync().ConfigureAwait(true);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);
            await RestoreScrollAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 初始化失败", ex);
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
                    AppServices.Logger.Error("ModLibraryPage 切换游戏后刷新失败", ex);
                }
            }))
        {
            AppServices.Logger.Warn("ModLibraryPage 无法派发游戏切换事件");
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
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 加载失败", ex);
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ------------------------------------------------------------ 状态恢复

    /// <summary>把上次离开时的搜索词 / 排序 / 来源筛选套回 ViewModel 与控件；换了游戏就不套用。</summary>
    private void RestoreState()
    {
        var state = PageStateStore.ModLibrary;
        if (state is null || !IsSameGame(state.GameId))
        {
            return;
        }

        if (!string.IsNullOrEmpty(state.SearchText))
        {
            // 写 ViewModel 而不是 SearchBox.Text：Text 是 OneWay 绑定，写控件会被下次绑定刷掉，
            // 而且程序化改 Text 不再走过滤（TextChanged 只认 UserInput）。
            _viewModel.SearchText = state.SearchText;
        }

        if (!string.IsNullOrEmpty(state.SortMode))
        {
            _viewModel.SelectedSortMode = state.SortMode;
            SortBox.SelectedItem = state.SortMode;
        }

        if (!string.IsNullOrEmpty(state.SourceFilter))
        {
            _viewModel.SelectedSourceFilter = state.SourceFilter;
            SourceBox.SelectedItem = state.SourceFilter;
        }

        _pendingOffset = state.VerticalOffset;
    }

    private async Task RestoreScrollAsync()
    {
        var offset = _pendingOffset;
        _pendingOffset = 0;
        if (offset <= 0)
        {
            return;
        }

        try
        {
            await ListViewScrollState.RestoreAsync(ModList, offset).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModLibraryPage 恢复滚动位置失败：" + ex.Message);
        }
    }

    /// <summary>离开页面（导航去别处）时把状态记下来，页面本身随即销毁。</summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        try
        {
            PageStateStore.ModLibrary = new ModLibraryState(
                AppServices.IsInitialized ? AppServices.CurrentGame?.GameId : null,
                _viewModel.SearchText,
                _viewModel.SelectedSortMode,
                _viewModel.SelectedSourceFilter,
                ListViewScrollState.GetVerticalOffset(ModList));
            PageStateStore.Persist();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("ModLibraryPage 记录页面状态失败：" + ex.Message);
        }

        base.OnNavigatedFrom(e);
    }

    private static bool IsSameGame(string? gameId) =>
        string.Equals(gameId, AppServices.IsInitialized ? AppServices.CurrentGame?.GameId : null, StringComparison.Ordinal);

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
            AppServices.Logger.Error("ModLibraryPage 刷新失败", ex);
        }
    }

    /// <summary>
    /// 搜索框输入。x:Bind 不支持 UpdateSourceTrigger，所以 Text 只做 OneWay 绑定，
    /// 由这里把用户输入写回 ViewModel（写回会触发重新过滤）。
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

            SearchSuggestBox.Update(sender, _viewModel.Mods.Select(mod => mod.Name), text);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 搜索过滤失败", ex);
        }
    }

    /// <summary>回车 / 放大镜 / 选中建议：用当前文本过滤，并收起建议下拉。</summary>
    private void OnSearchQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        try
        {
            _viewModel.SearchText = (args.QueryText ?? string.Empty).Trim();
            sender.IsSuggestionListOpen = false;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 搜索过滤失败", ex);
        }
    }

    /// <summary>排序下拉框选择变化：写回 ViewModel 触发重新排序。</summary>
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
            AppServices.Logger.Error("ModLibraryPage 切换排序失败", ex);
        }
    }

    /// <summary>来源下拉框选择变化：写回 ViewModel 触发重新过滤。</summary>
    private void OnSourceFilterChanged(object sender, PdxDropDownSelectionChangedEventArgs e)
    {
        try
        {
            if (e.SelectedItem is not string mode)
            {
                return;
            }

            if (string.Equals(mode, _viewModel.SelectedSourceFilter, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.SelectedSourceFilter = mode;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 切换来源筛选失败", ex);
        }
    }

    /// <summary>行尾「打开本地文件夹」：目录型 mod 打开目录，压缩包型 mod 定位到压缩包。</summary>
    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                OpenModFolder(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 打开本地文件夹失败", ex);
        }
    }

    /// <summary>行尾「在 Steam 中打开」：用 steam:// 协议打开该 mod 的创意工坊页面。</summary>
    private void OnOpenSteamClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                OpenWorkshopInSteam(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 在 Steam 中打开失败", ex);
        }
    }

    /// <summary>行尾「让 Steam 下载」：触发一次工坊条目的高优先级下载。</summary>
    private async void OnSteamDownloadClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                await _viewModel.TriggerSteamDownloadAsync(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 触发 Steam 下载失败", ex);
        }
    }

    /// <summary>行尾「在 Steam 中禁用 / 启用」：切换工坊条目的本地禁用位。</summary>
    private async void OnSteamDisableClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                await _viewModel.SetSteamDisabledLocallyAsync(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 切换 Steam 本地禁用失败", ex);
        }
    }

    /// <summary>行尾「在浏览器中打开」：用默认浏览器打开该 mod 的创意工坊页面。</summary>
    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                OpenWorkshopInBrowser(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 在浏览器中打开失败", ex);
        }
    }

    /// <summary>
    /// 行尾「打开详情页」：把这一行交给工坊详情子页（复用创意工坊那个详情页）。
    /// 页面自己不导航，只把请求抛给外壳；编号解析不出来的行按钮本来就是灰的，
    /// 这里再兜一次底，把原因写进状态栏。
    /// </summary>
    private void OnOpenDetailClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: ModLibraryRowViewModel row })
            {
                OpenWorkshopDetail(row);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 打开详情页失败", ex);
        }
    }

    /// <summary>
    /// 用资源管理器打开 mod 所在位置：目录型 mod 打开目录本身，
    /// 压缩包型 mod 打开所在目录并选中压缩包。
    /// </summary>
    private void OpenModFolder(ModLibraryRowViewModel row)
    {
        var dirPath = row.Entry.DirPath;
        if (!string.IsNullOrWhiteSpace(dirPath))
        {
            if (TryStartShell("explorer.exe", "\"" + dirPath + "\""))
            {
                _viewModel.SetStatus(L.T(Keys.ModlibStatusOpenedFolder, row.Name));
            }

            return;
        }

        var archivePath = row.Entry.ArchivePath;
        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            // /select, 让资源管理器直接定位到压缩包，用户不用自己在一堆文件里找。
            if (TryStartShell("explorer.exe", "/select,\"" + archivePath + "\""))
            {
                _viewModel.SetStatus(L.T(Keys.ModlibStatusLocatedArchive, row.Name));
            }

            return;
        }

        _viewModel.SetStatus(L.T(Keys.ModlibStatusNoLocalFiles));
    }

    /// <summary>用 steam:// 协议在 Steam 客户端里打开该 mod 的创意工坊页面。</summary>
    private void OpenWorkshopInSteam(ModLibraryRowViewModel row)
    {
        var address = SteamWorkshop.GetSteamAddress(row.WorkshopId);
        if (address is null)
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusNoWorkshopAddress));
            return;
        }

        if (TryStartShell(address, string.Empty))
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusOpenedSteam, row.Name));
        }
    }

    /// <summary>用默认浏览器打开该 mod 的创意工坊页面。</summary>
    private void OpenWorkshopInBrowser(ModLibraryRowViewModel row)
    {
        var address = SteamWorkshop.GetBrowserAddress(row.WorkshopId);
        if (address is null)
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusNoWorkshopAddress));
            return;
        }

        if (TryStartShell(address, string.Empty))
        {
            _viewModel.SetStatus(L.T(Keys.ModlibStatusOpenedBrowser, row.Name));
        }
    }

    /// <summary>
    /// 把这一行翻译成工坊详情行并请求导航。返回目标写本页：详情子页的返回按钮按发起方回退，
    /// 否则从 Mod 库进来会被甩到创意工坊列表。
    /// </summary>
    private void OpenWorkshopDetail(ModLibraryRowViewModel row)
    {
        var detailRow = _viewModel.CreateDetailRow(row);
        if (detailRow is null)
        {
            // 编号解析不出来时按钮已经是灰的；真走到这里说明数据在渲染后又变了，把原因写进状态栏。
            _viewModel.SetStatus(row.WorkshopDetailText);
            return;
        }

        DetailRequested?.Invoke(this, new WorkshopDetailNavigationRequest(detailRow, NavigationService.ModLibraryTag));
    }

    /// <summary>
    /// 交给 Windows shell 打开目标（资源管理器 / 协议地址）。
    /// 打开文件夹、拉起 Steam 与浏览器都属于 shell 动作，不是文件内容读写，故不经 IFileSystem；
    /// 目标也不是本进程发起的 HTTP 请求，因此不受 OnlineHosts 白名单约束。
    /// </summary>
    private bool TryStartShell(string fileName, string arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                AppServices.Logger.Warn("ModLibraryPage 无法启动外部程序：" + fileName);
                _viewModel.SetStatus(L.T(Keys.ModlibStatusCannotOpen, fileName));
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 启动外部程序失败：" + fileName, ex);
            _viewModel.SetStatus(L.T(Keys.ModlibStatusOpenFailed, ex.Message));
            return false;
        }
    }
}
