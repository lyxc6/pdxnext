namespace PdxLauncher.App.Views;

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库页：列出当前游戏的全部 mod（mods 表 + 磁盘本地 mod），支持搜索与排序。
/// 页面只负责生命周期和控件事件回写，加载 / 过滤 / 排序都在 <see cref="ModLibraryViewModel"/> 里完成。
/// <para>
/// 页面另有「在线（PDX Mods）」来源：同一页第二块视图，调 PDX Mods 搜索接口，
/// 由 <see cref="OnlineModsViewModel"/> 驱动。两个来源共用页头，主体互斥显示。
/// </para>
/// </summary>
public sealed partial class ModLibraryPage : Page
{
    private readonly ModLibraryViewModel _viewModel;
    private readonly OnlineModsViewModel _onlineViewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>在线视图的请求协调器：连点翻页 / 连打搜索只应用最新那次。</summary>
    private readonly ReloadCoordinator _onlineReload = new();

    public ModLibraryPage()
    {
        InitializeComponent();

        _viewModel = new ModLibraryViewModel(
            new ModLibraryService(AppServices.PlaySets, AppServices.Mods, AppServices.Logger),
            AppServices.ModThumbnails,
            AppServices.Logger);

        _onlineViewModel = new OnlineModsViewModel(
            AppServices.PdxMods,
            AppServices.Account,
            AppServices.Assets,
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModLibraryViewModel ViewModel => _viewModel;

    /// <summary>在线（PDX Mods）视图的视图模型。</summary>
    public OnlineModsViewModel OnlineViewModel => _onlineViewModel;

    /// <summary>XAML 里 <c>{x:Bind L.SomeKey, Mode=OneWay}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>当前来源是否在线；页面重进时据此决定是否自动查一次。</summary>
    private bool _isOnlineSource;

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
            AppServices.Account.SessionChanged -= OnSessionChanged;
            AppServices.Account.SessionChanged += OnSessionChanged;

            // x:Bind 在 InitializeComponent 里按属性顺序赋值，ComboBox 的 SelectedItem 可能先于
            // ItemsSource 生效而被清空；这里兜底补一次，保证排序框初始显示当前排序方式。
            if (SortBox.SelectedItem is null)
            {
                SortBox.SelectedItem = _viewModel.SelectedSortMode;
            }

            if (OnlineSortBox.SelectedItem is null)
            {
                OnlineSortBox.SelectedItem = _onlineViewModel.SelectedSortMode;
            }

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);

            // 上次停留在在线视图时，回来顺手刷一次（登录状态可能已变）。
            if (_isOnlineSource)
            {
                await OnlineReloadAsync().ConfigureAwait(true);
            }
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
        AppServices.Account.SessionChanged -= OnSessionChanged;
        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
            {
                _ = ReloadAsync();
                if (_isOnlineSource)
                {
                    _ = OnlineReloadAsync();
                }
            }))
        {
            AppServices.Logger.Warn("ModLibraryPage 无法派发游戏切换事件");
        }
    }

    /// <summary>
    /// 登录状态变化：在线列表的作者名 / 详情里的「已安装」都会变，而且未登录时
    /// 服务端只返回公开内容，所以只要当前在在线视图就静默重查一次。
    /// </summary>
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        if (!_isOnlineSource)
        {
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => _ = OnlineReloadAsync()))
        {
            AppServices.Logger.Warn("ModLibraryPage 无法派发登录状态变化事件");
        }
    }

    private Task ReloadAsync() => _reload.RunAsync(
        token => _viewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("ModLibraryPage 加载失败", ex));

    private Task OnlineReloadAsync() => _onlineReload.RunAsync(
        token => _onlineViewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("ModLibraryPage 在线查询失败", ex));

    // ------------------------------------------------------------ 来源切换

    /// <summary>切到本地视图。</summary>
    private void OnModSourceLocalClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetSource(online: false);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 切换来源失败", ex);
        }
    }

    /// <summary>切到在线视图：首次进入时触发一次查询。</summary>
    private void OnModSourceOnlineClick(object sender, RoutedEventArgs e)
    {
        try
        {
            SetSource(online: true);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 切换来源失败", ex);
        }
    }

    /// <summary>
    /// 切换来源：只改两块视图的可见性与两只切换按钮的选中态。
    /// 按钮是 ToggleButton，必须手工互斥，否则能同时按下。
    /// </summary>
    /// <param name="online">true 显示在线视图。</param>
    private void SetSource(bool online)
    {
        _isOnlineSource = online;

        LocalModsView.Visibility = online ? Visibility.Collapsed : Visibility.Visible;
        OnlineModsView.Visibility = online ? Visibility.Visible : Visibility.Collapsed;

        ModSourceLocalButton.IsChecked = !online;
        ModSourceOnlineButton.IsChecked = online;

        if (online && !_onlineViewModel.IsLoaded)
        {
            _ = OnlineReloadAsync();
        }
    }

    // ------------------------------------------------------------ 本地视图控件事件

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 手动刷新必须绕过扫描缓存：用户刚在 Steam 里装了 mod，点了刷新就该看到，
            // 而不是等 60 秒 TTL 过期。
            if (AppServices.Mods is IScanCacheInvalidator invalidatable && AppServices.CurrentGame is { } game)
            {
                invalidatable.InvalidateCache(game);
            }

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
            AppServices.Logger.Error("ModLibraryPage 搜索过滤失败", ex);
        }
    }

    /// <summary>排序下拉框选择变化：写回 ViewModel 触发重新排序。</summary>
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
            AppServices.Logger.Error("ModLibraryPage 切换排序失败", ex);
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

    /// <summary>行尾「在浏览器中打开」：用默认浏览器打开该 mod 的创意工坊页面。</summary>
    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
    {
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

    // ------------------------------------------------------------ 在线视图控件事件

    /// <summary>在线搜索框输入：写回 ViewModel，由它防抖后重查。</summary>
    private void OnOnlineSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not TextBox box)
            {
                return;
            }

            var text = box.Text ?? string.Empty;
            if (string.Equals(text, _onlineViewModel.SearchText, StringComparison.Ordinal))
            {
                return;
            }

            _onlineViewModel.SearchText = text;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 在线搜索失败", ex);
        }
    }

    /// <summary>在线排序下拉框变化。</summary>
    private void OnOnlineSortSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (sender is not ComboBox box || box.SelectedItem is not string mode)
            {
                return;
            }

            if (string.Equals(mode, _onlineViewModel.SelectedSortMode, StringComparison.Ordinal))
            {
                return;
            }

            _onlineViewModel.SelectedSortMode = mode;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 在线切换排序失败", ex);
        }
    }

    /// <summary>上一页。</summary>
    private async void OnOnlinePrevPageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _onlineReload
                .RunAsync(
                    token => _onlineViewModel.TurnPageAsync(-1, token),
                    _lifetime.Token,
                    ex => AppServices.Logger.Error("ModLibraryPage 在线翻页失败", ex))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
    }

    /// <summary>下一页。</summary>
    private async void OnOnlineNextPageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _onlineReload
                .RunAsync(
                    token => _onlineViewModel.TurnPageAsync(1, token),
                    _lifetime.Token,
                    ex => AppServices.Logger.Error("ModLibraryPage 在线翻页失败", ex))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
    }

    /// <summary>在线刷新：绕过缓存，直接重查当前页。</summary>
    private async void OnOnlineRefreshClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _onlineReload
                .RunAsync(
                    token => _onlineViewModel.RefreshAsync(),
                    _lifetime.Token,
                    ex => AppServices.Logger.Error("ModLibraryPage 在线刷新失败", ex))
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
    }

    /// <summary>点在线列表的一行：取详情并弹对话框。</summary>
    private async void OnOnlineModItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is not OnlineModRowViewModel row)
            {
                return;
            }

            await ShowOnlineDetailAsync(row).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 打开在线详情失败", ex);
        }
    }

    /// <summary>行内「在浏览器中打开」：直接打开 PDX Mods 网页详情页。</summary>
    private void OnOnlineOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: OnlineModRowViewModel row })
            {
                TryStartShell(row.WebUrl, string.Empty);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("ModLibraryPage 打开 PDX Mods 网页失败", ex);
        }
    }

    /// <summary>
    /// 弹详情对话框：网络请求期间先显示「正在读取」，拿到结果后换成正文。
    /// 对话框本身不可取消（CloseButtonText 只有关闭），请求随页面生命周期取消。
    /// </summary>
    /// <param name="row">被点击的行。</param>
    private async Task ShowOnlineDetailAsync(OnlineModRowViewModel row)
    {
        var body = new TextBlock
        {
            Text = L.ModlibOnlineDetailLoading,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = row.Name,
            Content = new ScrollViewer
            {
                Content = body,
                MaxHeight = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            CloseButtonText = L.ModlibOnlineDetailClose,
            DefaultButton = ContentDialogButton.Close,
        };

        // 先弹出来再填内容：详情接口可能要一两秒，空白对话框比不弹更难受。
        var shown = dialog.ShowAsync();
        try
        {
            var result = await _onlineViewModel
                .GetDetailAsync(row.ModId, _lifetime.Token)
                .ConfigureAwait(true);

            body.Text = result is { IsSuccess: true, Value: { } detail }
                ? BuildDetailText(detail)
                : L.T(Keys.ModlibOnlineDetailFailed, result.ErrorMessage ?? "pdx-mods-detail");
        }
        catch (OperationCanceledException)
        {
            body.Text = L.T(Keys.ModlibOnlineDetailFailed, "cancelled");
        }

        await shown;
    }

    /// <summary>把详情拼成对话框正文：长描述 + 体积 / 已装状态 + 外链清单。</summary>
    /// <param name="detail">详情结果。</param>
    private string BuildDetailText(PdxModsDetail detail)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(detail.LongDescription))
        {
            lines.Add(ToPlainText(detail.LongDescription));
        }

        if (detail.SizeBytes is { } size and > 0)
        {
            lines.Add(L.T(Keys.ModlibOnlineDetailSize, FormatSize(size)));
        }

        if (detail.ScreenshotUrls.Count > 0)
        {
            lines.Add(L.T(Keys.ModlibOnlineScreenshots, detail.ScreenshotUrls.Count));
        }

        foreach (var link in detail.ExternalLinks)
        {
            lines.Add(string.IsNullOrWhiteSpace(link.DisplayName)
                ? link.Url
                : link.DisplayName + "：" + link.Url);
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>
    /// PDX Mods 的 mod 描述是作者在网页里写的 Markdown（部分人还会塞 HTML）。
    /// 这里只做最小清理：去掉标题井号与加粗/斜体星号、HTML 标签，把链接还原成「文字（地址）」，
    /// 保留列表符号与换行 —— 目的是让对话框里能读，不做完整 Markdown 渲染。
    /// </summary>
    /// <param name="source">原始描述。</param>
    private static string ToPlainText(string source)
    {
        var text = source;

        // 先处理 HTML 换行，再统一交给标签剥离。
        text = text
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        var stripped = new System.Text.StringBuilder(text.Length);
        var inTag = false;
        foreach (var ch in text)
        {
            if (ch == '<')
            {
                inTag = true;
            }
            else if (ch == '>')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                stripped.Append(ch);
            }
        }

        text = System.Net.WebUtility.HtmlDecode(stripped.ToString());

        // [文字](地址) → 文字（地址）；单独的裸地址保持原样。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([^\]]+)\]\((https?://[^)\s]+)\)",
            "$1（$2）");

        // 行首井号：### 标题 → 标题。只吃行内空格，绝不能跨行（否则会把标题后的空行吞掉）。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?m)^[ \t]{0,3}#{1,6}[ \t]*",
            string.Empty);

        // 加粗 / 斜体 / 行内代码的标记字符直接去掉，保留里面的字。
        text = text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        // 收掉三行以上的连续空行，避免长描述里留下大片空白。
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>字节数转「1.2 MB」这类可读文案。</summary>
    /// <param name="bytes">字节数。</param>
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "0" : "0.#") + " " + units[unit];
    }

    // ------------------------------------------------------------ 打开目标

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
    /// 交给 Windows shell 打开目标（资源管理器 / 协议地址 / 网页）。
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
