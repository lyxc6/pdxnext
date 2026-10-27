namespace PdxLauncher.App.Views;

using System.Diagnostics;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊页：按当前游戏显示 Steam 创意工坊或 Paradox Mods（二选一，没有来源切换页签）。
/// 页面只负责生命周期、控件事件与详情弹窗，来源判定与查询都在 <see cref="WorkshopViewModel"/> 里。
/// </summary>
public sealed partial class WorkshopPage : Page
{
    private readonly WorkshopViewModel _viewModel;

    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    private bool _isReloading;

    public WorkshopPage()
    {
        InitializeComponent();

        _viewModel = new WorkshopViewModel(
            AppServices.SteamWorkshop,
            AppServices.PdxMods,
            AppServices.Account,
            AppServices.Assets,
            AppServices.Logger);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>用户点了「去登录」：交给外壳导航到账号页（页面自己不持有导航职责）。</summary>
    public event EventHandler? AccountRequested;

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

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
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
        try
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

            if (TryStartShell(url, string.Empty))
            {
                _viewModel.SetStatus(L.WorkshopOpenBrowser);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 打开网页失败", ex);
        }
    }

    /// <summary>行内「在浏览器中打开」。</summary>
    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: WorkshopRowViewModel row })
            {
                TryStartShell(row.WebUrl, string.Empty);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 打开浏览器失败", ex);
        }
    }

    /// <summary>行内「在 Steam 中打开」。</summary>
    private void OnOpenSteamClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is FrameworkElement { DataContext: WorkshopRowViewModel row }
                && !string.IsNullOrWhiteSpace(row.SteamUrl))
            {
                TryStartShell(row.SteamUrl, string.Empty);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 在 Steam 中打开失败", ex);
        }
    }

    /// <summary>点一行：按来源取详情并弹对话框。</summary>
    private async void OnItemClick(object sender, ItemClickEventArgs e)
    {
        try
        {
            if (e.ClickedItem is not WorkshopRowViewModel row)
            {
                return;
            }

            if (row.Source == WorkshopSource.Steam)
            {
                await ShowSteamDetailAsync(row).ConfigureAwait(true);
            }
            else
            {
                await ShowPdxDetailAsync(row).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 打开详情失败", ex);
        }
    }

    // ------------------------------------------------------------ 详情弹窗

    /// <summary>
    /// Steam 详情：先弹出「正在读取」再填正文（详情接口要请求一次 remote storage）。
    /// </summary>
    /// <param name="row">被点击的行。</param>
    private async Task ShowSteamDetailAsync(WorkshopRowViewModel row)
    {
        var body = CreateDetailBody(L.WorkshopDetailLoading);
        var dialog = CreateDetailDialog(row.Name, body);
        var shown = dialog.ShowAsync();

        try
        {
            var result = await _viewModel.GetSteamDetailAsync(row, _lifetime.Token).ConfigureAwait(true);
            body.Text = result is { IsSuccess: true, Value: { } detail }
                ? BuildSteamDetailText(detail)
                : L.T(Keys.WorkshopDetailFailed, result.ErrorMessage ?? "steam-workshop-detail");
        }
        catch (OperationCanceledException)
        {
            body.Text = L.T(Keys.WorkshopDetailFailed, "cancelled");
        }

        await shown;
    }

    /// <summary>
    /// PDX Mods 详情：同样先弹出再填正文。
    /// </summary>
    /// <param name="row">被点击的行。</param>
    private async Task ShowPdxDetailAsync(WorkshopRowViewModel row)
    {
        var body = CreateDetailBody(L.WorkshopDetailLoading);
        var dialog = CreateDetailDialog(row.Name, body);
        var shown = dialog.ShowAsync();

        try
        {
            var result = await _viewModel.GetPdxDetailAsync(row, _lifetime.Token).ConfigureAwait(true);
            body.Text = result is { IsSuccess: true, Value: { } detail }
                ? BuildPdxDetailText(detail)
                : L.T(Keys.WorkshopDetailFailed, result.ErrorMessage ?? "pdx-mods-detail");
        }
        catch (OperationCanceledException)
        {
            body.Text = L.T(Keys.WorkshopDetailFailed, "cancelled");
        }

        await shown;
    }

    private TextBlock CreateDetailBody(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true,
    };

    private ContentDialog CreateDetailDialog(string title, TextBlock body) => new()
    {
        XamlRoot = XamlRoot,
        Title = title,
        Content = new ScrollViewer
        {
            Content = body,
            MaxHeight = 560,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        },
        CloseButtonText = L.WorkshopDetailClose,
        DefaultButton = ContentDialogButton.Close,
    };

    /// <summary>Steam 详情正文：描述（BBCode 粗略清理）+ 体积 / 时间。</summary>
    /// <param name="detail">详情结果。</param>
    private string BuildSteamDetailText(SteamWorkshopItemDetail detail)
    {
        var lines = new List<string>();

        var description = ToPlainText(detail.Description);
        if (!string.IsNullOrWhiteSpace(description))
        {
            lines.Add(description);
        }

        if (detail.FileSizeBytes > 0)
        {
            lines.Add(L.T(Keys.WorkshopSize, FormatSize(detail.FileSizeBytes)));
        }

        if (detail.UpdatedUtc > DateTimeOffset.UnixEpoch)
        {
            lines.Add(L.T(
                Keys.WorkshopUpdated,
                detail.UpdatedUtc.ToLocalTime().ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture)));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }

    /// <summary>PDX Mods 详情正文：长描述 + 体积 + 截图数 + 外链清单。</summary>
    /// <param name="detail">详情结果。</param>
    private string BuildPdxDetailText(PdxModsDetail detail)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(detail.LongDescription))
        {
            lines.Add(ToPlainText(detail.LongDescription));
        }

        if (detail.SizeBytes is { } size and > 0)
        {
            lines.Add(L.T(Keys.WorkshopSize, FormatSize(size)));
        }

        if (detail.ScreenshotUrls.Count > 0)
        {
            lines.Add(L.T(Keys.WorkshopDetailScreenshots, detail.ScreenshotUrls.Count));
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
    /// 把作者写的 Markdown / HTML / BBCode 粗略清成可读纯文本。
    /// 只做「去掉标记、保留文字」，不追求完整渲染。
    /// </summary>
    /// <param name="source">原始描述。</param>
    private static string ToPlainText(string source)
    {
        var text = source;

        // HTML 换行先转成真换行，再统一剥离标签。
        text = text
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Steam 描述是 BBCode：[b]x[/b]、[h1]x[/h1]、[url=…]x[/url]、[list][*]x[/list]。
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?(b|i|u|h[1-6]|list|\*)\]", string.Empty);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?(url|img)[^\]]*\]", string.Empty);

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

        // [文字](地址) → 文字（地址）；裸地址保持原样。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([^\]]+)\]\((https?://[^)\s]+)\)",
            "$1（$2）");

        // 行首井号：### 标题 → 标题。只吃行内空格，绝不能跨行。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?m)^[ \t]{0,3}#{1,6}[ \t]*",
            string.Empty);

        text = text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        // 收掉三行以上的连续空行。
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

    /// <summary>
    /// 交给 Windows shell 打开目标（浏览器 / Steam 协议地址）。
    /// 打开外部程序属于 shell 动作，不是文件内容读写，故不经 IFileSystem；
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
                AppServices.Logger.Warn("WorkshopPage 无法启动外部程序：" + fileName);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("WorkshopPage 启动外部程序失败：" + fileName, ex);
            _viewModel.SetStatus(L.T(Keys.WorkshopStatusFailed, ex.Message));
            return false;
        }
    }
}
