using System;
using System.Collections.Generic;
using System.Collections.Specialized;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

namespace PdxLauncher.App.Views;

/// <summary>
/// 创意工坊 mod 详情子页。行数据通过导航参数传进来 —— 兼容两种写法：
/// <see cref="WorkshopDetailNavigationRequest"/>（行 + 返回目标，创意工坊列表和 Mod 库都用它）
/// 或裸的 <see cref="WorkshopRowViewModel"/>（返回目标缺省为创意工坊列表）。
/// 详情正文、图片墙、依赖、评论由 <see cref="WorkshopDetailViewModel"/> 负责加载。
/// 页面本身不持有导航服务：返回 / 去别处一律抛 <see cref="NavigationRequested"/>，由 MainWindow 处理。
/// </summary>
public sealed partial class WorkshopDetailPage : Page
{
    private readonly WorkshopDetailViewModel _viewModel;
    private readonly SteamWorkshopHostBackend? _steamBackend;
    private readonly HashSet<MarkdownView> _commentMarkdownViews = [];

    /// <summary>查看器当前显示的一组图片：顶部截图墙或正文配图，二选一。</summary>
    private IReadOnlyList<WorkshopScreenshotViewModel> _viewerItems = [];

    private WorkshopRowViewModel? _row;

    /// <summary>上级合集（从合集内容点进来的子项）；返回时先回它，null 表示直接回列表页。</summary>
    private WorkshopRowViewModel? _parentRow;

    /// <summary>
    /// 「返回」要回到的页面 Tag；由发起方在导航参数里指定。
    /// 写死回创意工坊列表会让从 Mod 库进来的用户被甩到别的页面，所以缺值才回落 <see cref="NavigationService.WorkshopTag"/>。
    /// </summary>
    private string? _returnTag;

    private CancellationTokenSource? _lifetime;

    /// <summary>初始化详情页，并接好依赖。</summary>
    public WorkshopDetailPage()
    {
        InitializeComponent();

        _steamBackend = CreateSteamBackend();

        _viewModel = new WorkshopDetailViewModel(
            new WorkshopDetailSource(
                AppServices.SteamWorkshop,
                AppServices.PdxMods,
                AppServices.Account,
                AppServices.WorkshopInfo,
                AppServices.WorkshopItemPages,
                AppServices.PlaySets,
                AppServices.Logger),
            new WorkshopImageCache(
                AppServices.FileSystem,
                AppServices.Assets,
                AppServices.WorkshopInfo,
                AppServices.Covers,
                path => ThumbnailVariants.GenerateInBackground(AppServices.FileSystem, AppServices.Logger, path, CancellationToken.None)),
            new WorkshopSubscriptionService(
                AppServices.PdxMods,
                AppServices.Account,
                AppServices.FileSystem,
                AppServices.DataRoot,
                AppServices.Logger),
            AppServices.Logger,
            _steamBackend,
            new WorkshopSubscriptionReader(
                AppServices.FileSystem,
                new SteamLibraryLocator(AppServices.FileSystem, AppServices.Logger),
                AppServices.Logger),
            new WorkshopStateReader(
                SteamWorkshopBackends.CreateForState,
                AppServices.WorkshopStates,
                AppServices.Logger));

        // 正文里的行内图片与顶部图片墙共用同一份缓存文件：先把解析器接上，
        // 图片下载完成后再由 GalleryImageReady 原地刷新，不重建整页。
        // 译文由同一个 MarkdownView 按块交替渲染，不需要再单独接一份解析器。
        DescriptionView.ImageResolver = _viewModel.ResolveImagePath;
        ChangelogView.ImageResolver = _viewModel.ResolveImagePath;
        _viewModel.GalleryImageReady += OnGalleryImageReady;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnGalleryImageReady(object? sender, EventArgs e)
    {
        DescriptionView.RefreshImages();
        ChangelogView.RefreshImages();

        foreach (var view in _commentMarkdownViews)
        {
            view.RefreshImages();
        }
    }

    /// <summary>评论模板里的 MarkdownView 没有在构造期接解析器，加载后再接（含表情小图）。</summary>
    private void OnCommentMarkdownLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MarkdownView view)
        {
            return;
        }

        _commentMarkdownViews.Add(view);
        view.ImageResolver = _viewModel.ResolveImagePath;
        view.RefreshImages();
    }

    private void OnCommentMarkdownUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MarkdownView view)
        {
            _commentMarkdownViews.Remove(view);
        }
    }

    /// <summary>
    /// 建 Steam 直连后端：读状态与写订阅两个开关分开管。
    /// 只开了「读取状态」（默认开）时后端仍然可用，只用来读订阅/安装/下载状态；
    /// 真正下发订阅动作用的是写开关，没开就在视图模型里拒绝。
    /// 返回 null 表示走「打开 Steam 客户端」的降级路径。
    /// <para>
    /// 真正的原生调用在短命子进程里做（<see cref="SteamHostClient"/>），本进程不初始化 Steamworks，
    /// 因此不会被 Steam 当成「游戏正在运行」。
    /// </para>
    /// </summary>
    private static SteamWorkshopHostBackend? CreateSteamBackend()
    {
        if (!AppServices.Settings.SteamWorkshopSubscribeEnabled
            && !AppServices.Settings.SteamWorkshopStateEnabled)
        {
            return null;
        }

        try
        {
            var backend = AppServices.Settings.SteamWorkshopSubscribeEnabled
                ? SteamWorkshopBackends.CreateForSubscribe(AppServices.CurrentGame)
                : SteamWorkshopBackends.CreateForState(AppServices.CurrentGame);

            if (backend is null)
            {
                AppServices.Logger.Warn("Steam 直连不可用，退回打开客户端");
            }

            return backend;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("初始化 Steam 直连失败，退回打开客户端", ex);
            return null;
        }
    }

    /// <summary>当前页面的取消令牌（跑在 LoadAsync / 翻页 / 订阅里）。</summary>
    private CancellationToken CurrentToken => _lifetime?.Token ?? CancellationToken.None;

    /// <summary>详情页视图模型。</summary>
    public WorkshopDetailViewModel ViewModel => _viewModel;

    /// <summary>本地化文案。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>需要导航到别处（例如返回创意工坊列表）时触发。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>从合集内容里点了一条子项：请外壳打开这条子项的详情。</summary>
    public event EventHandler<WorkshopDetailNavigationRequest>? DetailRequested;

    /// <inheritdoc />
    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // 参数可能是「行 + 返回目标」的请求，也可能只是那一行；缺失时页面只显示空壳并报错，不做静默兜底。
        _returnTag = null;
        _parentRow = null;
        if (e.Parameter is WorkshopDetailNavigationRequest request)
        {
            _row = request.Row;
            _returnTag = request.ReturnTag;
            _parentRow = request.Parent;
        }
        else
        {
            _row = e.Parameter as WorkshopRowViewModel;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_row is null)
        {
            _viewModel.HasError = true;
            _viewModel.ErrorText = L.T(Keys.WorkshopDetailFailed, L.T(Keys.WorkshopDetailErrorUnknown));
            return;
        }

        UpdateGalleryLayout(ActualWidth, ActualHeight);

        // 截图是在加载过程中逐条落进集合的：集合一变就选首张、补计数，
        // 不再等整页加载结束（那时缩略图可能早就出完了，大图却还是黑的）。
        _viewModel.Screenshots.CollectionChanged -= OnScreenshotsChanged;
        _viewModel.Screenshots.CollectionChanged += OnScreenshotsChanged;

        await RunAsync(_row);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.GalleryImageReady -= OnGalleryImageReady;
        _viewModel.Screenshots.CollectionChanged -= OnScreenshotsChanged;
        _commentMarkdownViews.Clear();
        _lifetime?.Cancel();

        // 原生 Steamworks 会话本来就在子进程里，随进程退出自动结束；
        // 这里没有要显式关掉的东西（后端是无状态的一次性调用封装）。
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.Row is null)
        {
            return;
        }

        await RunAsync(_viewModel.Row);
    }

    private async Task RunAsync(WorkshopRowViewModel row)
    {
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = new CancellationTokenSource();

        try
        {
            await _viewModel.LoadAsync(row, _lifetime.Token, _returnTag, _parentRow);
        }
        catch (OperationCanceledException)
        {
            // 页面已经离开（或换了行），本次加载作废。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("创意工坊详情页加载异常: " + row.Id, ex);
        }

        // 截图是加载过程中异步落到集合里的，FlipView 不会自己选中第一个；
        // 这里补一次，保证大图一打开就有内容、右上角计数也有值。
        EnsureGallerySelection();
    }

    /// <summary>集合变化（换 mod 清空、逐条落地）时维护选中项与右上角计数。</summary>
    private void OnScreenshotsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        EnsureGallerySelection();

    private void EnsureGallerySelection()
    {
        if (_viewModel.Screenshots.Count == 0)
        {
            // 换 mod / 重新加载会先清空集合：把上一份的选中与计数一起复位，
            // 否则计数器会停在旧值、FlipView 也会留着上一份的最后选中项。
            if (FlipWorkshopScreenshots.SelectedIndex >= 0)
            {
                FlipWorkshopScreenshots.SelectedIndex = -1;
            }

            txtWorkshopInlineCounter.Text = string.Empty;
            return;
        }

        // 正常路径下 ItemsSource 由 XAML 的 x:Bind 绑定；这里只在它没绑上时补一次，
        // 避免 FlipView 拿不到条目（会显示成一块空白）。
        if (FlipWorkshopScreenshots.ItemsSource is null)
        {
            FlipWorkshopScreenshots.ItemsSource = _viewModel.Screenshots;
        }

        // 只在「还没有选中项」时选第一张，用户已经翻到别的页时不要打断。
        if (FlipWorkshopScreenshots.SelectedIndex < 0)
        {
            FlipWorkshopScreenshots.SelectedIndex = 0;
        }

        SyncCurrentShot(FlipWorkshopScreenshots.SelectedIndex);
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        // 从合集内容点进来的子项：先回上级合集详情，再点一次才回列表页。
        if (_parentRow is not null)
        {
            DetailRequested?.Invoke(this, new WorkshopDetailNavigationRequest(_parentRow, _returnTag));
            return;
        }

        // 回到发起方那一页；没有带返回目标（裸行参数）时才回落创意工坊列表。
        NavigationRequested?.Invoke(this, _returnTag ?? NavigationService.WorkshopTag);
    }

    /// <summary>合集内容里点了一条：交给外壳打开它的详情（返回目标仍是本页的上层列表）。</summary>
    private void OnCollectionItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WorkshopRowViewModel row)
        {
            return;
        }

        // 当前条目就是子项的上级：子项详情的「返回」先回到这里。
        DetailRequested?.Invoke(
            this,
            new WorkshopDetailNavigationRequest(row, _returnTag, _viewModel.Row));
    }

    private void OnOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenUrl(_viewModel.WebUrl, "创意工坊详情");
    }

    private void OnOpenSteamClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenSteam(_viewModel.SteamUrl, "创意工坊详情");
    }

    private void OnAuthorClick(object sender, RoutedEventArgs e)
    {
        ShellLauncher.TryOpenUrl(_viewModel.AuthorProfileUrl, "创意工坊详情（作者）");
    }

    private void OnLinkClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkshopLinkViewModel link)
        {
            ShellLauncher.TryOpenUrl(link.Url, "创意工坊详情");
        }
    }

    private void OnCommentAuthorClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkshopCommentViewModel comment)
        {
            ShellLauncher.TryOpenUrl(comment.AuthorProfileUrl, "创意工坊评论作者");
        }
    }

    /// <summary>订阅 / 取消订阅（先弹二次确认，避免误点）。</summary>
    private async void OnSubscribeClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanToggleSubscription)
        {
            return;
        }

        var subscribe = !_viewModel.IsInstalled;
        var action = subscribe ? L.WorkshopDetailSubscribe : L.WorkshopDetailUnsubscribe;

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            action,
            L.WorkshopDetailSubscribePlaysetHint,
            action,
            L.CommonCancel);
        if (!confirmed)
        {
            return;
        }

        var result = await _viewModel.ToggleSubscriptionAsync(subscribe, CurrentToken);

        // 成功静默：按钮文案与状态已经变了，再写一句「已订阅」是重复；失败必须留下原因。
        _viewModel.StatusText = result.IsSuccess
            ? string.Empty
            : L.T(
                subscribe ? Keys.WorkshopDetailSubscribeFailed : Keys.WorkshopDetailUnsubscribeFailed,
                result.ErrorMessage ?? string.Empty);
    }

    /// <summary>Steam：订阅其实要在 Steam 客户端里完成，这里只负责把页面递过去。</summary>
    private async void OnSubscribeSteamClick(object sender, RoutedEventArgs e)
    {
        // 直连可用且允许写：先让用户确认再动 Steam；只读/后端不可用：退回「在 Steam 客户端里打开」。
        if (_viewModel.IsSteamDirectAvailable && _viewModel.ShowSteamSubscribeButton && _viewModel.CanWriteSteam)
        {
            var subscribe = !_viewModel.IsSteamSubscribed;
            var actionText = subscribe
                ? L.WorkshopDetailSubscribe
                : L.WorkshopDetailUnsubscribe;
            var message = subscribe
                ? L.WorkshopDetailSteamSubscribeConfirm
                : L.WorkshopDetailSteamUnsubscribeConfirm;

            var confirmed = await ConfirmOverlay.ConfirmAsync(
                actionText,
                message,
                actionText,
                L.CommonCancel);
            if (!confirmed)
            {
                return;
            }

            var result = await _viewModel.ToggleSteamSubscriptionAsync(subscribe, CurrentToken);

            // 成功静默，理由同 PDX 分支。
            _viewModel.StatusText = result.Ok
                ? string.Empty
                : L.T(
                    subscribe ? Keys.WorkshopDetailSubscribeFailed : Keys.WorkshopDetailUnsubscribeFailed,
                    result.ErrorText ?? string.Empty);
            return;
        }

        ShellLauncher.TryOpenSteam(_viewModel.SteamUrl, "创意工坊详情（订阅）");
    }

    /// <summary>合集级「订阅全部 / 取消订阅全部」：直连可写时确认后调 Steam；只读时打开 Steam 合集页。</summary>
    private async void OnCollectionSubscribeClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanActivateCollectionSubscription)
        {
            return;
        }

        if (!_viewModel.IsSteamDirectAvailable || !_viewModel.CanWriteSteam)
        {
            ShellLauncher.TryOpenSteam(_viewModel.SteamUrl, "创意工坊合集（订阅）");
            return;
        }

        var subscribe = !_viewModel.IsCollectionFullySubscribed;
        var count = _viewModel.CollectionItems.Count;
        var actionText = subscribe
            ? L.WorkshopDetailCollectionSubscribeAll
            : L.WorkshopDetailCollectionUnsubscribeAll;
        var message = L.T(
            subscribe
                ? Keys.WorkshopDetailCollectionSubscribeConfirm
                : Keys.WorkshopDetailCollectionUnsubscribeConfirm,
            count);

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            actionText,
            message,
            actionText,
            L.CommonCancel);
        if (!confirmed)
        {
            return;
        }

        var result = await _viewModel.ToggleCollectionSubscriptionAsync(subscribe, CurrentToken);

        // 成功静默：按钮文案与逐项状态已经变了；失败必须留下原因。
        _viewModel.StatusText = result.Ok
            ? string.Empty
            : L.T(
                subscribe ? Keys.WorkshopDetailSubscribeFailed : Keys.WorkshopDetailUnsubscribeFailed,
                result.ErrorText ?? string.Empty);
    }

    /// <summary>合集里某一项的订阅 / 取消订阅。</summary>
    private async void OnCollectionItemSubscribeClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkshopRowViewModel row ||
            !row.CanActivateSteamSubscription)
        {
            return;
        }

        // 只读 / 后端不可写：不假装直连，直接打开该 mod 的 Steam 页面。
        // 这里按实时设置判断，用户在当前页面打开设置开关后无需重新进页面。
        if (!_viewModel.CanWriteSteam || !_viewModel.IsSteamDirectAvailable)
        {
            ShellLauncher.TryOpenSteam(row.SteamUrl, "创意工坊详情（订阅）");
            return;
        }

        var subscribe = !row.IsSubscribed;
        var actionText = subscribe ? L.WorkshopDetailSubscribe : L.WorkshopDetailUnsubscribe;
        var message = subscribe
            ? L.WorkshopDetailSteamSubscribeConfirm
            : L.WorkshopDetailSteamUnsubscribeConfirm;

        var confirmed = await ConfirmOverlay.ConfirmAsync(
            actionText,
            message,
            actionText,
            L.CommonCancel);
        if (!confirmed)
        {
            return;
        }

        var result = await _viewModel.SetCollectionItemSubscribedAsync(row, subscribe, CurrentToken);
        _viewModel.StatusText = result.Ok
            ? string.Empty
            : L.T(
                subscribe ? Keys.WorkshopDetailSubscribeFailed : Keys.WorkshopDetailUnsubscribeFailed,
                result.ErrorText ?? string.Empty);
    }

    /// <summary>合集子项行内的「在浏览器中打开」。</summary>
    private void OnCollectionItemOpenBrowserClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkshopRowViewModel row)
        {
            ShellLauncher.TryOpenUrl(row.WebUrl, "创意工坊详情");
        }
    }

    /// <summary>合集子项行内的「在 Steam 中打开」。</summary>
    private void OnCollectionItemOpenSteamClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WorkshopRowViewModel row)
        {
            ShellLauncher.TryOpenSteam(row.SteamUrl, "创意工坊详情");
        }
    }

    /// <summary>评论翻上一页。</summary>
    private async void OnCommentsPrevClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanCommentsPrev)
        {
            return;
        }

        await _viewModel.LoadCommentsPageAsync(_viewModel.CommentsPage - 1, CurrentToken);
    }

    /// <summary>评论翻下一页。</summary>
    private async void OnCommentsNextClick(object sender, RoutedEventArgs e)
    {
        if (!_viewModel.CanCommentsNext)
        {
            return;
        }

        await _viewModel.LoadCommentsPageAsync(_viewModel.CommentsPage + 1, CurrentToken);
    }

    /// <summary>正文里的链接（Markdown / HTML 都有）。</summary>
    private void OnMarkdownLinkRequested(object? sender, string url)
    {
        ShellLauncher.TryOpenUrl(url, "创意工坊详情（正文链接）");
    }

    /// <summary>
    /// 正文里内嵌的图片：打开内置查看器，并在同一份文档的配图之间左右翻页。
    /// 只有本地文件确实取不到（例如仅剩占位图）时才退回浏览器。
    /// </summary>
    private void OnMarkdownImageRequested(object? sender, string url)
    {
        var targetUrl = SteamEmoticon.NormalizeUrl(url);

        // 同一份文档的配图按正文顺序成组；表情和未下载完的图不进查看器。
        var urls = (sender as MarkdownView)?.Markdown?.ImageUrls;
        if (urls is { Count: > 0 })
        {
            var items = new List<WorkshopScreenshotViewModel>(urls.Count);
            var target = -1;
            foreach (var candidate in urls)
            {
                if (SteamEmoticon.TryCreate(candidate, alt: null, out _, out _))
                {
                    continue;
                }

                if (_viewModel.ResolveImagePath(candidate) is not { Length: > 0 } path)
                {
                    continue;
                }

                if (string.Equals(candidate, targetUrl, StringComparison.Ordinal))
                {
                    target = items.Count;
                }

                items.Add(new WorkshopScreenshotViewModel(items.Count, candidate) { Path = path });
            }

            if (target >= 0)
            {
                OpenViewer(items, target);
                return;
            }
        }

        // 正文文档里没有（或本地文件还没下好）：先看它是不是顶部截图墙里的同一张。
        for (var position = 0; position < _viewModel.Screenshots.Count; position++)
        {
            if (string.Equals(_viewModel.Screenshots[position].Url, targetUrl, StringComparison.Ordinal))
            {
                OpenViewer(_viewModel.Screenshots, position);
                return;
            }
        }

        // 单张图也能看大图，不再把用户甩去浏览器。
        if (_viewModel.ResolveImagePath(targetUrl) is { Length: > 0 } singlePath)
        {
            OpenViewer([new WorkshopScreenshotViewModel(0, targetUrl) { Path = singlePath }], 0);
            return;
        }

        ShellLauncher.TryOpenUrl(url, "创意工坊详情（图片）");
    }

    /// <summary>点缩略图：把页面里的大图翻到那一张（不打开浮层，浮层要点大图才进）。</summary>
    private void OnThumbnailClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not WorkshopScreenshotViewModel shot)
        {
            return;
        }

        FlipWorkshopScreenshots.SelectedIndex = Math.Clamp(shot.Index, 0, Math.Max(0, _viewModel.Screenshots.Count - 1));
    }

    /// <summary>内联大图翻页：同步缩略图描边与两处计数器。</summary>
    private void OnInlineGallerySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SyncCurrentShot(FlipWorkshopScreenshots.SelectedIndex);
    }

    /// <summary>点内联大图：打开全屏查看器并停在同一张。</summary>
    private void OnInlineImageTapped(object sender, TappedRoutedEventArgs e)
    {
        OpenViewer(_viewModel.Screenshots, FlipWorkshopScreenshots.SelectedIndex);
    }

    /// <summary>打开全屏图片查看器（页面内浮层，不是 ContentDialog）。</summary>
    /// <param name="items">要翻的一组图：顶部截图墙或正文配图。</param>
    /// <param name="index">初始停在那一张。</param>
    private void OpenViewer(IReadOnlyList<WorkshopScreenshotViewModel> items, int index)
    {
        if (items.Count == 0)
        {
            return;
        }

        _viewerItems = items;
        var target = Math.Clamp(index, 0, items.Count - 1);
        var fromGallery = ReferenceEquals(items, _viewModel.Screenshots);

        if (fromGallery)
        {
            FlipWorkshopScreenshots.SelectedIndex = target;
        }

        FlipWorkshopViewer.ItemsSource = items;
        FlipWorkshopViewer.SelectedIndex = target;
        UpdateCounter(target);
        if (fromGallery)
        {
            SyncCurrentShot(target);
        }

        ImageOverlay.Visibility = Visibility.Visible;

        // 折叠状态下浮层没有实际尺寸，必须等这一帧布局跑完再算，否则会把图压成 0。
        DispatcherQueue.TryEnqueue(ApplyViewerSize);
        FlipWorkshopViewer.Focus(FocusState.Programmatic);
    }

    private void OnCloseImageViewer(object sender, RoutedEventArgs e)
    {
        var wasGallery = ReferenceEquals(_viewerItems, _viewModel.Screenshots);
        ImageOverlay.Visibility = Visibility.Collapsed;
        FlipWorkshopViewer.ItemsSource = null;
        _viewerItems = [];
        txtWorkshopImageCounter.Text = string.Empty;

        if (!wasGallery)
        {
            return;
        }

        // 关掉浮层后把焦点还给当前缩略图，键盘用户不会「丢焦点」。
        var index = Math.Clamp(FlipWorkshopScreenshots.SelectedIndex, 0, Math.Max(0, _viewModel.Screenshots.Count - 1));
        var shot = index >= 0 && index < _viewModel.Screenshots.Count ? _viewModel.Screenshots[index] : null;
        if (shot is not null && FindThumbnail(shot.AutomationId) is { } button)
        {
            button.Focus(FocusState.Programmatic);
        }
        else
        {
            FlipWorkshopScreenshots.Focus(FocusState.Programmatic);
        }
    }

    private void OnGallerySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = FlipWorkshopViewer.SelectedIndex;
        if (index < 0 || _viewerItems.Count == 0)
        {
            return;
        }

        UpdateCounter(index);
        if (ReferenceEquals(_viewerItems, _viewModel.Screenshots))
        {
            SyncCurrentShot(index);
        }

        // 翻到新的一页时容器是刚实体化的，Max 宽高要再写一次。
        DispatcherQueue.TryEnqueue(ApplyViewerSize);
    }

    /// <summary>浮层里双击：在 1x 与 2x 之间切换。</summary>
    private void OnViewerDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        var factor = viewer.ZoomFactor < 1.5f ? 2f : 1f;
        ApplyZoom(viewer, factor);
        e.Handled = true;
    }

    /// <summary>浮层里 Ctrl + 滚轮：连续缩放（1–4x）。不按 Ctrl 时把滚轮让给 FlipView 翻页。</summary>
    private void OnViewerPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not ScrollViewer viewer ||
            (e.KeyModifiers & Windows.System.VirtualKeyModifiers.Control) == 0)
        {
            return;
        }

        var delta = e.GetCurrentPoint(viewer).Properties.MouseWheelDelta;
        if (delta == 0)
        {
            return;
        }

        var factor = (float)Math.Clamp(viewer.ZoomFactor * (delta > 0 ? 1.2f : 1f / 1.2f), 1f, 4f);
        ApplyZoom(viewer, factor);
        e.Handled = true;
    }

    /// <summary>应用缩放；放大时允许拖动查看，缩回 1x 时把拖动让回给 FlipView 翻页。</summary>
    private static void ApplyZoom(ScrollViewer viewer, float factor)
    {
        var zoom = Math.Clamp(factor, 1f, 4f);
        var scrollable = zoom > 1.01f ? ScrollMode.Enabled : ScrollMode.Disabled;
        viewer.HorizontalScrollMode = scrollable;
        viewer.VerticalScrollMode = scrollable;
        viewer.ChangeView(null, null, zoom, disableAnimation: true);
    }

    /// <summary>页面尺寸变化：内联图区高度随窗口走，浮层开着时同时重算图片可用区域。</summary>
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        UpdateGalleryLayout(e.NewSize.Width, e.NewSize.Height);

    private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e) => ApplyViewerSize();

    private void UpdateGalleryLayout(double width, double height)
    {
        if (height > 0)
        {
            FlipWorkshopScreenshots.Height = WorkshopGalleryLayout.InlineGalleryHeight(height);
        }

        if (ImageOverlay.Visibility == Visibility.Visible)
        {
            ApplyViewerSize();
        }
    }

    /// <summary>把「1x 刚好放进浮层」的可用宽高写进每张截图，模板靠 x:Bind 拿到。</summary>
    private void ApplyViewerSize()
    {
        if (ImageOverlay.Visibility != Visibility.Visible || _viewerItems.Count == 0)
        {
            return;
        }

        var headerHeight = ImageOverlay.RowDefinitions.Count > 0
            ? ImageOverlay.RowDefinitions[0].ActualHeight
            : 0;

        var (width, height) = WorkshopGalleryLayout.ViewerContent(
            ImageOverlay.ActualWidth,
            ImageOverlay.ActualHeight,
            headerHeight);

        foreach (var shot in _viewerItems)
        {
            shot.ApplyViewerSize(width, height);
        }
    }

    /// <summary>把「当前张」标记写到截图行上（缩略图条靠它描边）。</summary>
    private void SyncCurrentShot(int index)
    {
        for (var i = 0; i < _viewModel.Screenshots.Count; i++)
        {
            _viewModel.Screenshots[i].IsCurrent = i == index;
        }

        var total = _viewModel.Screenshots.Count;
        txtWorkshopInlineCounter.Text = total <= 0
            ? string.Empty
            : (Math.Clamp(index, 0, total - 1) + 1) + " / " + total;
    }

    private Button? FindThumbnail(string automationId)
    {
        foreach (var shot in _viewModel.Screenshots)
        {
            if (!string.Equals(shot.AutomationId, automationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (FindByName(lstWorkshopScreenshots, automationId) is Button button)
            {
                return button;
            }
        }

        return null;
    }

    private static FrameworkElement? FindByName(DependencyObject root, string automationId)
    {
        var count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element &&
                string.Equals(Microsoft.UI.Xaml.Automation.AutomationProperties.GetAutomationId(element), automationId, StringComparison.Ordinal))
            {
                return element;
            }

            if (FindByName(child, automationId) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void UpdateCounter(int index)
    {
        var total = _viewerItems.Count;
        txtWorkshopImageCounter.Text = total <= 0
            ? string.Empty
            : (Math.Clamp(index, 0, total - 1) + 1) + " / " + total;
    }

    private void OnGalleryKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            OnCloseImageViewer(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }
}
