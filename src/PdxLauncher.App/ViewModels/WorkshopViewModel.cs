namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.Online;

/// <summary>
/// 「创意工坊」页的视图模型。这个页面**只有一个来源**：按当前游戏决定显示 Steam 创意工坊
/// 还是 Paradox Mods（游戏有 Steam 工坊就用 Steam，没有就用 PDX；独立版没有 appId，直接 PDX）。
/// <para>
/// 排序照 Steam 创意工坊自己的下拉框做：最热门 / 最受好评 / 最近发行 / 最新更新 / 不重复订阅者总计，
/// 最热门与最受好评还可以再选时间范围（今天 ~ 发布至今）。PDX Mods 只有三档，见
/// <see cref="ResolvePdxSort"/>。
/// </para>
/// <para>
/// 同一条 mod 的信息（名称/作者/评分/订阅/更新时间/体积/封面/详情）按「来源 + id」缓存在
/// <see cref="WorkshopInfoCache"/> 里：翻页回来、切游戏回来、断网时都直接复用，封面不重复下载，
/// 详情在 TTL 内不再请求。
/// </para>
/// 本类不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed partial class WorkshopViewModel : LocalizedObservableObject
{
    /// <summary>PDX Mods 每页条数。</summary>
    private const int PdxPageSize = 24;

    /// <summary>创意工坊所在的 host：限流冷却按 host 记账，提示文案要查它。</summary>
    private const string SteamCommunityHost = "steamcommunity.com";

    /// <summary>Steam 每页条数。</summary>
    private const int SteamPageSize = SteamWorkshopClient.DefaultPageSize;

    /// <summary>Steam 的排序档位（与网页一致）。</summary>
    private static readonly string[] SteamSortKeys =
    [
        Keys.WorkshopSortTrend,
        Keys.WorkshopSortTopRated,
        Keys.WorkshopSortMostRecent,
        Keys.WorkshopSortMostUpdated,
        Keys.WorkshopSortMostSubscribed,
    ];

    /// <summary>PDX Mods 的排序档位（只有最热门 / 最新更新 / 评分最高）。</summary>
    private static readonly string[] PdxSortKeys =
    [
        Keys.WorkshopSortTrend,
        Keys.WorkshopSortMostUpdated,
        Keys.WorkshopSortRating,
    ];

    /// <summary>Steam 的时间范围文案键（与 <see cref="TimeRangeDays"/> 一一对应）。</summary>
    private static readonly string[] TimeRangeKeys =
    [
        Keys.WorkshopTimeToday,
        Keys.WorkshopTimeWeek,
        Keys.WorkshopTimeMonth,
        Keys.WorkshopTimeQuarter,
        Keys.WorkshopTimeHalfYear,
        Keys.WorkshopTimeYear,
        Keys.WorkshopTimeAll,
    ];

    /// <summary>时间范围对应的服务端 <c>days</c>；-1 表示发布至今（Steam 自己的 All time）。</summary>
    private static readonly int[] TimeRangeDays = [1, 7, 30, 90, 180, 365, -1];

    /// <summary>默认时间范围的索引（30 天，与 Steam 网页默认一致）。</summary>
    private const int DefaultTimeRangeIndex = 2;

    /// <summary>
    /// 未登记游戏的来源探测预算。HttpFetcher 的连接超时是 30 秒、总超时 180 秒，
    /// 不设上限时页面会长时间停在「正在判断…」，所以只等 5 秒，超时按 Paradox Mods 处理。
    /// </summary>
    private static readonly TimeSpan WorkshopProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 过期页静默刷新的最小间隔：同一页两分钟内只补一次，来回切页不会把请求翻倍。
    /// </summary>
    private static readonly TimeSpan SilentRefreshMinInterval = TimeSpan.FromMinutes(2);

    private readonly SteamWorkshopClient _steam;
    private readonly PdxModsClient _pdx;
    private readonly AccountService _account;
    private readonly IAssetDownloader _assets;
    private readonly WorkshopInfoCache _info;
    private readonly WorkshopPageCache _pages;
    private readonly IAppLogger _logger;

    /// <summary>当前游戏的注册 ID（PDX 查询用）；null 表示还没选游戏。</summary>
    private string? _gameId;

    /// <summary>当前游戏的 Steam appId（Steam 查询与探测用）；独立版为 null。</summary>
    private int? _steamAppId;

    /// <summary>当前页码（0 基）。</summary>
    private int _page;

    /// <summary>服务端报告的总条数。</summary>
    private int _totalCount;

    /// <summary>服务端报告的总页数；0 表示未知。</summary>
    private int _totalPages;

    /// <summary>语言切换时重建下拉框：此时改选中项不算用户操作。</summary>
    private bool _isRelocalizing;

    /// <summary>当前这一轮封面下载的令牌；翻页时取消上一轮，避免下载队列越积越长。</summary>
    private CancellationTokenSource? _coverLifetime;

    /// <summary>封面进度：已完成张数。</summary>
    private int _coverDone;

    /// <summary>封面进度：本轮总张数。</summary>
    private int _coverTotal;

    /// <summary>同时下载的封面数上限。</summary>
    private const int MaxConcurrentCovers = 6;

    /// <summary>已经提交的搜索词：只有回车 / 刷新才更新，翻页与页缓存键都用它。</summary>
    private string _appliedSearch = string.Empty;

    /// <summary>页面生命周期令牌（由 <see cref="LoadAsync"/> 传入）；每一轮查询的令牌都从它派生。</summary>
    private CancellationToken _pageToken = CancellationToken.None;

    /// <summary>当前在途查询的令牌源；起新查询时取消并释放上一轮。</summary>
    private CancellationTokenSource? _queryCts;

    /// <summary>查询代号：只有最新一代允许写界面，迟到的结果直接丢弃。</summary>
    private int _queryVersion;

    /// <summary>最近一次后台查询（主查询或静默刷新）；内部已收口异常，这里只为卸载判断与诊断。</summary>
    private Task? _queryTask;

    /// <summary>当前游戏；null 表示还没在游戏库里选游戏。</summary>
    [ObservableProperty]
    private GameInstallation? _game;

    /// <summary>是否正在查询。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否已完成过一次查询。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>已经提示过「本页标题完全没翻出来」的页键（同一页只提示一次）。</summary>
    private string? _translateFailureKey;

    /// <summary>搜索词。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前排序方式，取值来自 <see cref="SortModes"/>。</summary>
    [ObservableProperty]
    private string _selectedSortMode = string.Empty;

    /// <summary>排序下拉框的选项（随来源与语言变化）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sortModes = [];

    /// <summary>当前时间范围，取值来自 <see cref="TimeRangeModes"/>（仅 Steam）。</summary>
    [ObservableProperty]
    private string _selectedTimeRange = string.Empty;

    /// <summary>时间范围下拉框的选项（按当前语言生成）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _timeRangeModes = [];

    /// <summary>当前显示的来源。</summary>
    [ObservableProperty]
    private WorkshopSource _source;

    /// <summary>是否正在补封面。</summary>
    [ObservableProperty]
    private bool _isFillingCovers;

    public WorkshopViewModel(
        SteamWorkshopClient steam,
        PdxModsClient pdx,
        AccountService account,
        IAssetDownloader assets,
        WorkshopInfoCache info,
        WorkshopPageCache pages,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(pdx);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(logger);

        _steam = steam;
        _pdx = pdx;
        _account = account;
        _assets = assets;
        _info = info;
        _pages = pages;
        _logger = logger;

        SortModes = BuildSortModes(WorkshopSource.None);
        _selectedSortMode = SortModes[0];
        TimeRangeModes = BuildTimeRangeModes();
        _selectedTimeRange = TimeRangeModes[DefaultTimeRangeIndex];
    }

    /// <summary>当前可见的创意工坊行。</summary>
    public ObservableCollection<WorkshopRowViewModel> Rows { get; } = new();

    /// <summary>游戏名（页头副标题）；没有游戏时为空串。</summary>
    public string GameTitle => Game?.DisplayName ?? string.Empty;

    /// <summary>是否已选中游戏。</summary>
    public bool HasGame => Game is not null;

    /// <summary>没选游戏时显示提示。</summary>
    public bool ShowNoGameHint => Game is null;

    /// <summary>来源徽标文案：Steam 创意工坊 / Paradox Mods / 判断中。</summary>
    public string SourceLabel => Source switch
    {
        WorkshopSource.Steam => T(Keys.WorkshopSourceSteam),
        WorkshopSource.Pdx => T(Keys.WorkshopSourcePdx),
        _ => T(Keys.WorkshopSourceDetecting),
    };

    /// <summary>结果条数文案；没有结果时为空串。</summary>
    public string CountText => _totalCount > 0
        ? T(Keys.WorkshopCountText, _totalCount.ToString("N0", System.Globalization.CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>是否可翻到上一页。</summary>
    public bool HasPrevPage => _page > 0;

    /// <summary>是否还有下一页。</summary>
    public bool HasNextPage => _totalPages > 0 && _page + 1 < _totalPages;

    /// <summary>封面进度文案「封面 12/30」；没有进行中的下载时为空串。</summary>
    public string CoverProgressText => _coverTotal > 0
        ? T(Keys.WorkshopCoverProgress, _coverDone, _coverTotal)
        : string.Empty;

    /// <summary>是否显示封面进度。</summary>
    public bool ShowCoverProgress => _coverTotal > 0;

    /// <summary>页码文案：第 1 / 42 页；没有结果时为空串。</summary>
    public string PageText => _totalPages > 0 ? T(Keys.WorkshopPage, _page + 1, _totalPages) : string.Empty;

    /// <summary>空结果时显示提示（已完成查询且没有任何行，且已选中游戏）。</summary>
    public bool ShowEmptyState => IsLoaded && HasGame && Rows.Count == 0;

    /// <summary>PDX 来源且未登录时显示「去登录」提示条。</summary>
    public bool ShowSignInHint => Source == WorkshopSource.Pdx && !_account.IsSignedIn;

    /// <summary>
    /// 时间范围只对 Steam 的「最热门 / 最受好评」有意义（Steam 网页也只在这两个排序下给时间范围），
    /// 其他排序或 PDX 来源都隐藏这个下拉。
    /// </summary>
    public bool ShowTimeRange => Source == WorkshopSource.Steam && IsTimeRangeSort();

    /// <summary>
    /// 换游戏 / 首次进入：重置页码与列表，判定来源后查一次。
    /// </summary>
    /// <param name="game">当前游戏；null 表示还没选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        _gameId = game?.GameId;

        // 换游戏 / 重新加载：上一轮查询作废；已提交的搜索词跟随输入框当前内容。
        CancelPendingQuery();
        _pageToken = cancellationToken;
        _appliedSearch = SearchText.Trim();

        // 独立安装读不到 appmanifest 时 SteamAppId 是 null，这里用映射表兜底，
        // 否则 EU4 / CS1 / 监狱建筑师这些明明有 Steam 工坊的游戏会被误判成没有。
        _steamAppId = WorkshopSourceChooser.ResolveAppId(game?.SteamAppId, game?.GameId);
        _page = 0;
        _totalCount = 0;
        _totalPages = 0;
        CancelCoverFill();
        Rows.Clear();

        // 不再清空整页缓存：页键里已经带了来源/游戏/筛选条件，换游戏换回来时
        // 上一轮的列表可以直接铺上，省掉一次 1–3 MB 的 SSR 页面请求。
        Source = WorkshopSource.None;

        if (game is null)
        {
            IsLoaded = true;
            StatusMessage = T(Keys.WorkshopNoGameHint);
            RaiseDerived();
            return;
        }

        // 「仅启动」支持的游戏（例如监狱建筑师）也有 Steam 工坊：只要能解析出 appId 就继续查；
        // 既没有 appId 又不支持内容管理时，才提示「内容由游戏本体管理」。
        if (_steamAppId is null && !game.SupportsContentManagement)
        {
            IsLoaded = true;
            StatusMessage = SupportMessages.ManagedByGame;
            RaiseDerived();
            return;
        }

        IsBusy = true;
        IsLoaded = false;
        StatusMessage = T(Keys.WorkshopStatusLoading);
        RaiseDerived();

        try
        {
            Source = await ResolveSourceAsync(cancellationToken).ConfigureAwait(true);
            RaiseDerived();
            await RunQueryAsync(resetPage: false, force: false).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            IsLoaded = true;
            StatusMessage = T(Keys.WorkshopStatusFailed, ex.Message);
            _logger.Error("创意工坊加载失败（game=" + game.GameId + "）", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// 刷新当前页，同时充当「提交搜索」：文本框里改了关键词但没回车时，按刷新就按新词查。
    /// Steam 侧处于限流冷却时直接给出提示，不再发请求——冷却期内点刷新只会让封禁更久。
    /// </summary>
    public async Task RefreshAsync()
    {
        if (!HasGame)
        {
            await LoadAsync(Game, _pageToken).ConfigureAwait(true);
            return;
        }

        if (Source == WorkshopSource.Steam && TryGetCooldown(out var remaining))
        {
            // 冷却期不提交搜索词：否则列表还停在旧关键词上，状态栏却说在搜新的。
            StatusMessage = DescribeCooldown(remaining);
            RaiseDerived();
            return;
        }

        // 关键词变了回第 1 页（旧页码对新词没有意义），没变就只刷新当前页。
        var text = SearchText.Trim();
        var searchChanged = !string.Equals(text, _appliedSearch, StringComparison.Ordinal);
        _appliedSearch = text;
        if (searchChanged)
        {
            _page = 0;
        }

        var before = AppServices.HttpThrottle.Snapshot();
        await RunQueryAsync(resetPage: false, force: true).ConfigureAwait(true);
        var after = AppServices.HttpThrottle.Snapshot();
        _logger.Info("本次刷新请求数：" + DescribeRequestDelta(before, after));
    }

    /// <summary>Steam 侧当前是否还在限流冷却中。</summary>
    private static bool TryGetCooldown(out TimeSpan remaining)
    {
        if (AppServices.HttpThrottle.TryGetCooldown(SteamCommunityHost, out var untilUtc))
        {
            remaining = untilUtc - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
            {
                return true;
            }
        }

        remaining = TimeSpan.Zero;
        return false;
    }

    /// <summary>把冷却剩余时间写成「已暂停 N 分钟」的提示。</summary>
    private string DescribeCooldown(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return T(Keys.WorkshopThrottlePaused, minutes);
    }

    /// <summary>把查询异常翻成状态栏文案：限流单独说清楚，其余沿用失败文案。</summary>
    private string DescribeFailure(Exception exception)
    {
        if (exception is SteamThrottleException throttle)
        {
            var remaining = throttle.RetryAfter ?? TimeSpan.Zero;
            if (remaining <= TimeSpan.Zero && TryGetCooldown(out var cooldown))
            {
                remaining = cooldown;
            }

            // 请求已经被 Steam 拒了，这里要说「等多久重试」，而不是「已暂停、自动继续」。
            return DescribeRetryAfter(remaining);
        }

        return T(Keys.WorkshopStatusFailed, exception.Message);
    }

    /// <summary>
    /// 把后端给的错误码翻成状态栏文案。
    /// <para>
    /// 后端只回错误码（见 <c>AppResult.Fail</c>），界面要自己说清楚是哪一类失败：
    /// 页面结构变了（Steam 改版）、连不上（DNS 被污染 / 代理没走）、还是别的请求错误。
    /// 三种混成一句「加载失败：steam-workshop-http」，用户根本不知道该做什么。
    /// </para>
    /// </summary>
    /// <param name="errorCode">后端错误码，例如 <c>steam-workshop-parse</c>。</param>
    /// <param name="kind">错误种类。</param>
    private string DescribeWorkshopError(string? errorCode, ErrorKind kind)
    {
        if (string.Equals(errorCode, "steam-workshop-parse", StringComparison.Ordinal)
            || kind == ErrorKind.Unsupported)
        {
            return T(Keys.WorkshopStatusParseFailed);
        }

        if (string.Equals(errorCode, "steam-workshop-network", StringComparison.Ordinal))
        {
            return T(Keys.WorkshopStatusNetworkFailed, SteamCommunityHost);
        }

        return T(Keys.WorkshopStatusFailed, errorCode ?? "steam-workshop");
    }

    /// <summary>
    /// 连不上 Steam 时，看看系统里是不是有代理却没在用；有就把地址报出来，
    /// 让用户知道「不是没网，而是没走代理」。
    /// </summary>
    private string DescribeDetectedProxyHint()
    {
        try
        {
            // 只读探测：不改任何设置，也不改当前生效的代理。
            var detection = ProxyResolver.Describe(new AppSettings { ProxyMode = ProxyModes.System });
            if (detection.Url is null)
            {
                return string.Empty;
            }

            return T(Keys.WorkshopStatusProxyHint, detection.Url);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Empty;
        }
    }

    /// <summary>把冷却剩余时间写成「请约 N 分钟后重试」的提示。</summary>
    private string DescribeRetryAfter(TimeSpan remaining)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
        return T(Keys.WorkshopThrottleRetryIn, minutes);
    }

    /// <summary>刷新前后按 host 比较请求数，写进日志作为请求预算的验收证据。</summary>
    private static string DescribeRequestDelta(
        IReadOnlyDictionary<string, HostStats> before,
        IReadOnlyDictionary<string, HostStats> after)
    {
        var parts = new List<string>();
        foreach (var (host, stat) in after.OrderByDescending(pair => pair.Value.Total))
        {
            before.TryGetValue(host, out var previous);
            var delta = stat.Total - (previous?.Total ?? 0);
            if (delta > 0)
            {
                parts.Add(host + "=" + delta);
            }
        }

        return parts.Count == 0 ? "0" : string.Join(" ", parts);
    }

    /// <summary>供页面写入一次性状态提示（例如「已在浏览器中打开」）。</summary>
    /// <param name="message">状态栏文案。</param>
    public void SetStatus(string message) => StatusMessage = message;

    /// <summary>翻页：delta 为 -1 / +1，越界时不动。</summary>
    /// <param name="delta">页码增量。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task TurnPageAsync(int delta, CancellationToken cancellationToken)
    {
        var target = _page + delta;
        if (target < 0 || (delta > 0 && !HasNextPage))
        {
            return;
        }

        _page = target;
        _pageToken = cancellationToken;
        await RunQueryAsync(resetPage: false, force: false).ConfigureAwait(true);
    }

    /// <summary>
    /// 提交搜索框里的关键词并重查第一页。只有回车（或刷新按钮）走到这里 ——
    /// 打字过程中不发请求，否则每敲一个字符就会排一次 1–3 MB 的工坊页请求。
    /// </summary>
    public Task CommitSearchAsync()
    {
        if (!HasGame)
        {
            return Task.CompletedTask;
        }

        _appliedSearch = SearchText.Trim();
        return RunQueryAsync(resetPage: true, force: true);
    }

    /// <summary>页面卸载时取消在途查询（页面令牌本身也会级联取消，这里让状态立刻停下）。</summary>
    public void CancelPendingQuery()
    {
        var pending = _queryCts;
        _queryCts = null;
        if (pending is null)
        {
            return;
        }

        if (_queryTask is { IsCompleted: false })
        {
            _logger.Info("创意工坊页卸载：取消在途查询");
        }

        try
        {
            pending.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经收尾，忽略。
        }

        pending.Dispose();
    }

    /// <summary>
    /// 起一轮新查询：取消上一轮、递增代号，再把本轮从头跑到尾。
    /// 取消是正常路径（翻页 / 换筛选 / 页面卸载），所有异常都在这里收口，调用方只需要 await 到结束。
    /// </summary>
    /// <param name="resetPage">是否回到第一页（回车搜索、换筛选条件为 true）。</param>
    /// <param name="force">是否跳过页缓存。</param>
    private async Task RunQueryAsync(bool resetPage, bool force)
    {
        if (!HasGame || Source == WorkshopSource.None)
        {
            return;
        }

        if (resetPage)
        {
            _page = 0;
        }

        CancelPendingQuery();
        _queryCts = CancellationTokenSource.CreateLinkedTokenSource(_pageToken);
        var version = ++_queryVersion;

        try
        {
            await QueryAsync(version, _queryCts.Token, force).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (version != _queryVersion || _pageToken.IsCancellationRequested)
        {
            // 被新查询取代，或页面卸载：正常路径，不提示。
        }
        catch (OperationCanceledException ex)
        {
            // 页面没卸载、查询也没被取代，却收到取消 = 连接层超时
            // （.NET 连不上时抛的是 OperationCanceledException，内含 TimeoutException）。
            // 以前这里一律当成「正常取消」静默吞掉，页面就永远停在「正在加载第 1 页…」、
            // 列表空白，日志里只剩一条「未观察的 Task 异常」。现在必须说清楚。
            _logger.Error("创意工坊查询被中断（可能是连接超时，game=" + _gameId + "）", ex);
            IsLoaded = true;
            IsBusy = false;
            StatusMessage = T(Keys.WorkshopStatusNetworkFailed, SteamCommunityHost);
            RaiseDerived();
        }
        catch (Exception ex)
        {
            _logger.Error("创意工坊查询流程失败（game=" + _gameId + "）", ex);
            if (version == _queryVersion)
            {
                IsLoaded = true;
                IsBusy = false;
                StatusMessage = DescribeFailure(ex);
                RaiseDerived();
            }
        }
    }

    /// <summary>
    /// 把工坊信息缓存落盘。序列化 + 同步写都在后台线程：
    /// 30 条累加后的这一下写盘原先跑在 UI 线程上，慢磁盘时整个页面会短暂失去响应。
    /// </summary>
    private async Task FlushInfoCacheAsync()
    {
        try
        {
            await Task.Run(_info.Flush).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warn("创意工坊信息缓存落盘失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 判定来源，按三步走：
    /// <list type="number">
    /// <item><description>内置表（<see cref="SteamWorkshopGames"/>）里有登记 → 直接定来源，不发任何请求；</description></item>
    /// <item><description>没有 Steam appId（独立版）→ PDX；</description></item>
    /// <item><description>未登记但拿得到 appId → 探测，最多等 <see cref="WorkshopProbeTimeout"/>，
    /// 超时或失败都退回 PDX 并写明原因。</description></item>
    /// </list>
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task<WorkshopSource> ResolveSourceAsync(CancellationToken cancellationToken)
    {
        // 表里登记过的游戏不做任何网络判定：以前这里要先打 appdetails 再抓工坊页，
        // 网络不通时页面会长时间停在「正在判断…」，而且结果对 CK2 / BattleTech 这类老游戏还是错的。
        if (SteamWorkshopGames.TryGet(_gameId) is { } known)
        {
            _logger.Info("创意工坊来源按内置表判定：game=" + _gameId + " source=" + (known ? "Steam" : "Pdx"));
            return known ? WorkshopSource.Steam : WorkshopSource.Pdx;
        }

        if (_steamAppId is not { } appId)
        {
            return WorkshopSource.Pdx;
        }

        // 未登记的游戏才探测，给 5 秒预算：HttpFetcher 的连接超时是 30 秒，不设限会把页面卡死。
        using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        probeCts.CancelAfter(WorkshopProbeTimeout);

        AppResult<bool> probe;
        try
        {
            probe = await _steam.HasWorkshopAsync(appId, probeCts.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            StatusMessage = T(Keys.WorkshopProbeTimeout);
            _logger.Warn("Steam 工坊探测超时（" + WorkshopProbeTimeout.TotalSeconds + "s），按 Paradox Mods 处理：game=" + _gameId);
            return WorkshopSource.Pdx;
        }

        if (!probe.IsSuccess)
        {
            StatusMessage = T(Keys.WorkshopProbeFailed);
            _logger.Warn("Steam 工坊探测失败（appId=" + appId + "）：" + (probe.ErrorMessage ?? "unknown"));
            return WorkshopSource.Pdx;
        }

        return WorkshopSourceChooser.Resolve(appId, probe.Value, _gameId);
    }

    /// <summary>
    /// 按当前来源执行一次查询。命中内存页缓存就立刻铺满列表（回看过的页不必再抓 1–3 MB 的 SSR 页面），
    /// 未命中才联网；联网期间保留旧行，不白屏。
    /// </summary>
    /// <param name="version">查询代号；只有最新一代允许写界面，迟到的结果直接丢弃。</param>
    /// <param name="cancellationToken">本轮查询的取消令牌（由页面令牌派生）。</param>
    /// <param name="force">true 表示跳过缓存读取（回车搜索、刷新按钮、换筛选条件时用）。</param>
    private async Task QueryAsync(int version, CancellationToken cancellationToken, bool force = false)
    {
        if (_gameId is null || Source == WorkshopSource.None)
        {
            return;
        }

        var key = BuildPageKey();
        if (!force && _pages.TryGet(key, out var cached))
        {
            if (version != _queryVersion)
            {
                return;
            }

            ApplyCachedPage(cached, cancellationToken);
            StatusMessage = T(Keys.WorkshopStatusLoadedCached, _totalCount);
            RaiseDerived();

            // 过期的页仍然先显示，再悄悄刷新一次；用户不必盯着转圈。
            // 同一页两分钟内只补一次，否则来回切页会重复打同一个地址。
            if (!_pages.IsFresh(cached) && _pages.CanReattempt(cached, SilentRefreshMinInterval))
            {
                _pages.MarkAttempt(key);
                _queryTask = RefreshSilentlyAsync(key, version, cancellationToken);
            }

            return;
        }

        // Steam 限流冷却期内不发请求：换了排序/时间范围却拿到旧列表，
        // 用户只会以为「排序没生效」。这里先拦住，并在状态栏说明原因。
        if (Source == WorkshopSource.Steam && TryGetCooldown(out var remaining))
        {
            var cooldown = DescribeCooldown(remaining);
            if (Rows.Count == 0)
            {
                // 一行都没有时才用离线缓存兜底，免得页面空着。
                ShowFailureOrCache(Source, "throttled", cooldown, cancellationToken);
                if (Rows.Count > 0)
                {
                    // 兜底成功：冷却原因仍然要说，否则用户只看到「显示缓存中的 N 个结果」。
                    StatusMessage = cooldown + " · " + StatusMessage;
                }
            }
            else
            {
                // 已有行保持不动（不改成与排序无关的旧列表），只说明为什么没刷新。
                StatusMessage = cooldown;
            }

            RaiseDerived();
            return;
        }

        IsBusy = true;
        StatusMessage = _appliedSearch.Length > 0
            ? T(Keys.WorkshopStatusSearching)
            : T(Keys.WorkshopStatusLoadingPage, _page + 1);
        RaiseDerived();

        try
        {
            if (Source == WorkshopSource.Steam)
            {
                await QuerySteamAsync(version, cancellationToken, key).ConfigureAwait(true);
            }
            else
            {
                await QueryPdxAsync(version, cancellationToken, key).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (version == _queryVersion)
            {
                IsLoaded = true;
                ShowFailureOrCache(
                    Source,
                    ex.Message,
                    ex is SteamThrottleException ? DescribeFailure(ex) : null,
                    cancellationToken);
                _logger.Error("创意工坊查询失败（game=" + _gameId + "）", ex);
                RaiseDerived();
            }
        }
        finally
        {
            // 旧一代查询退出时不能复位忙碌状态，否则会把新一代的转圈提前关掉。
            if (version == _queryVersion)
            {
                IsBusy = false;
                RaiseDerived();
            }
        }
    }

    /// <summary>生成页缓存的键：来源、游戏、已提交的关键词、排序、时间范围、页码都参与。</summary>
    private string BuildPageKey()
    {
        var search = _appliedSearch.Length == 0 ? null : _appliedSearch;
        if (Source == WorkshopSource.Steam)
        {
            var sort = ResolveSteamSort();
            var days = sort is SteamWorkshopSort.Trend or SteamWorkshopSort.TopRated ? ResolveDays() : (int?)null;
            return WorkshopPageCache.Key(WorkshopSource.Steam, _gameId, search, sort.ToString(), days, _page);
        }

        return WorkshopPageCache.Key(WorkshopSource.Pdx, _gameId, search, ResolvePdxSort().ToString(), null, _page);
    }

    /// <summary>把缓存的一页直接铺到列表上（封面只认磁盘命中，不联网）。</summary>
    /// <param name="page">页快照。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void ApplyCachedPage(WorkshopPageCache.Page page, CancellationToken cancellationToken)
    {
        _totalCount = page.TotalCount;
        _totalPages = page.TotalPages;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var rows = new List<WorkshopRowViewModel>(page.Items.Count);
        foreach (var entry in page.Items)
        {
            var row = WorkshopRowViewModel.FromCache(entry, size);
            if (_info.TryGetCachedCover(WorkshopInfoCache.Key(row.Source, row.Id), out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
        }

        ReplaceRows(rows);
        StartNameTranslation(rows, cancellationToken);
    }

    /// <summary>
    /// 给一行行模型补译名（后台跑，译好一行刷一行）。
    /// <para>翻译是网络操作，不能挡着列表显示：这里只起一个后台任务，行模型自己接住结果。</para>
    /// </summary>
    /// <param name="rows">要翻译的行。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartNameTranslation(IEnumerable<WorkshopRowViewModel> rows, CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateNamesEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        var pending = rows.Where(static row => !string.IsNullOrWhiteSpace(row.Name)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        _ = TranslateNamesAsync(pending, cancellationToken);
    }

    /// <summary>整页标题一次性批翻（引擎内部并发），译完统一回填。</summary>
    private async Task TranslateNamesAsync(
        IReadOnlyList<WorkshopRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        try
        {
            var names = rows.Select(static row => row.Name).ToList();
            var translated = await AppServices.Translation
                .TranslateBatchAsync(names, cancellationToken)
                .ConfigureAwait(true);

            var anyTranslated = false;
            for (var i = 0; i < rows.Count && i < translated.Count; i++)
            {
                var translatedName = translated[i];
                if (string.IsNullOrWhiteSpace(translatedName))
                {
                    continue;
                }

                rows[i].TranslatedName = translatedName;
                anyTranslated = true;
            }

            if (!anyTranslated)
            {
                ReportTranslationFailure();
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("创意工坊标题翻译失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 整页一条译文都没拿到时，在底部状态条说明原因（同一页只提示一次）。
    /// <para>
    /// 引擎连不通时原本没有任何反馈，用户会以为翻译功能压根没做。
    /// </para>
    /// </summary>
    private void ReportTranslationFailure()
    {
        var key = BuildPageKey();
        if (string.Equals(_translateFailureKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _translateFailureKey = key;
        var reason = AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown);
        _logger.Warn("创意工坊标题全部未翻译（" + key + "）：" + reason);
        SetStatus(T(Keys.WorkshopTranslateUnavailable, reason));
    }

    /// <summary>整体替换列表行；先取消上一轮封面下载，避免旧页任务继续占用带宽。</summary>
    /// <param name="rows">新行。</param>
    private void ReplaceRows(IEnumerable<WorkshopRowViewModel> rows)
    {
        CancelCoverFill();
        Rows.Clear();
        foreach (var row in rows)
        {
            Rows.Add(row);
        }
    }

    /// <summary>页缓存过期时的静默刷新：不置忙碌、失败只记 Warn。</summary>
    /// <param name="key">页缓存键。</param>
    /// <param name="version">发起静默刷新时的查询代号；期间有新查询就放弃写入。</param>
    /// <param name="cancellationToken">本轮查询的取消令牌。</param>
    private async Task RefreshSilentlyAsync(string key, int version, CancellationToken cancellationToken)
    {
        try
        {
            if (Source == WorkshopSource.Steam)
            {
                await QuerySteamAsync(version, cancellationToken, key).ConfigureAwait(true);
            }
            else
            {
                await QueryPdxAsync(version, cancellationToken, key).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面卸载：放弃刷新。
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warn("创意工坊静默刷新失败：" + ex.Message);
        }
    }

    /// <summary>取消当前这一轮封面下载。</summary>
    private void CancelCoverFill()
    {
        var lifetime = _coverLifetime;
        _coverLifetime = null;
        if (lifetime is null)
        {
            return;
        }

        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经收尾，忽略。
        }

        lifetime.Dispose();
    }

    /// <summary>封面进度变化时通知两个派生属性。</summary>
    private void RaiseCoverProgress()
    {
        OnPropertyChanged(nameof(ShowCoverProgress));
        OnPropertyChanged(nameof(CoverProgressText));
    }

    /// <summary>Steam 创意工坊查询。</summary>
    /// <param name="version">查询代号；结果回来时不是最新一代就丢弃。</param>
    /// <param name="cancellationToken">本轮查询的取消令牌。</param>
    /// <param name="pageKey">页缓存键（成功后把这一页写进去）。</param>
    private async Task QuerySteamAsync(int version, CancellationToken cancellationToken, string pageKey)
    {
        if (_steamAppId is not { } appId)
        {
            return;
        }

        var sort = ResolveSteamSort();
        var days = sort is SteamWorkshopSort.Trend or SteamWorkshopSort.TopRated ? ResolveDays() : (int?)null;
        var query = new SteamWorkshopQuery(
            _appliedSearch.Length == 0 ? null : _appliedSearch,
            sort,
            _page,
            SteamPageSize,
            days);

        var result = await _steam.BrowseAsync(appId, query, cancellationToken).ConfigureAwait(true);

        // 已经被新一代查询或页面卸载取代：这一次的结果直接丢掉，别覆盖新列表。
        if (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        IsLoaded = true;

        if (!result.IsSuccess || result.Value is null)
        {
            var errorCode = result.Kind == ErrorKind.Unsupported
                ? "steam-workshop-parse"
                : result.ErrorMessage ?? "steam-workshop";
            ShowFailureOrCache(
                WorkshopSource.Steam,
                errorCode,
                DescribeWorkshopError(errorCode, result.Kind),
                cancellationToken);
            RaiseDerived();
            return;
        }

        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalPages;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var rows = new List<WorkshopRowViewModel>(page.Items.Count);
        var entries = new List<WorkshopInfoCacheEntry>(page.Items.Count);
        foreach (var item in page.Items)
        {
            // 先把元数据并进缓存，再从缓存取封面文件名：同一个 id 第二次出现不必再下载图片。
            RecordSteamItem(item);
            var cacheKey = WorkshopInfoCache.Key(WorkshopSource.Steam, item.PublishedFileId);
            var row = WorkshopRowViewModel.FromSteam(item, size);
            if (_info.TryGetCachedCover(cacheKey, out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
            if (_info.TryGet(cacheKey, out var entry))
            {
                entries.Add(entry);
            }
        }

        ReplaceRows(rows);
        StartNameTranslation(rows, cancellationToken);
        _pages.Set(pageKey, entries, _totalCount, _totalPages);

        // 30 条累加后只落盘一次（逐条 Save 会卡在同步 IO 上），并放到后台线程，不占 UI 线程。
        await FlushInfoCacheAsync().ConfigureAwait(true);
        StatusMessage = rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, _appliedSearch)
            : T(Keys.WorkshopStatusLoaded, _totalCount);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);
    }

    /// <summary>PDX Mods 查询。</summary>
    /// <param name="version">查询代号；结果回来时不是最新一代就丢弃。</param>
    /// <param name="cancellationToken">本轮查询的取消令牌。</param>
    /// <param name="pageKey">页缓存键（成功后把这一页写进去）。</param>
    private async Task QueryPdxAsync(int version, CancellationToken cancellationToken, string pageKey)
    {
        if (_gameId is not { } gameId)
        {
            return;
        }

        var query = new PdxModsQuery(
            _appliedSearch.Length == 0 ? null : _appliedSearch,
            ResolvePdxSort(),
            Descending: true,
            Page: _page,
            PageSize: PdxPageSize);

        var result = await _pdx
            .SearchAsync(gameId, query, _account.Session, cancellationToken)
            .ConfigureAwait(true);

        // 已经被新一代查询或页面卸载取代：这一次的结果直接丢掉，别覆盖新列表。
        if (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        IsLoaded = true;

        if (!result.IsSuccess || result.Value is null)
        {
            ShowFailureOrCache(
                WorkshopSource.Pdx,
                result.ErrorMessage ?? "pdx-mods",
                cancellationToken: cancellationToken);
            RaiseDerived();
            return;
        }

        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalCount == 0 ? 0 : ((page.TotalCount - 1) / PdxPageSize) + 1;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var rows = new List<WorkshopRowViewModel>(page.Items.Count);
        var entries = new List<WorkshopInfoCacheEntry>(page.Items.Count);
        foreach (var item in page.Items)
        {
            RecordPdxItem(item);
            var cacheKey = WorkshopInfoCache.Key(WorkshopSource.Pdx, item.ModId);
            var row = WorkshopRowViewModel.FromPdx(item, size);
            if (_info.TryGetCachedCover(cacheKey, out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
            if (_info.TryGet(cacheKey, out var entry))
            {
                entries.Add(entry);
            }
        }

        ReplaceRows(rows);
        StartNameTranslation(rows, cancellationToken);
        _pages.Set(pageKey, entries, _totalCount, _totalPages);
        await FlushInfoCacheAsync().ConfigureAwait(true);
        StatusMessage = rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, _appliedSearch)
            : T(Keys.WorkshopStatusLoaded, _totalCount);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);
    }

    /// <summary>
    /// 查询失败：先用这个游戏之前缓存过的条目把列表填上，
    /// 一条都没有才显示失败原因。断网时页面仍然是可用的。
    /// </summary>
    /// <param name="source">当前来源。</param>
    /// <param name="reason">失败原因（用于日志/兜底文案）。</param>
    /// <param name="statusOverride">限流等特殊情况下的状态栏文案；null 表示用通用失败文案。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void ShowFailureOrCache(
        WorkshopSource source,
        string reason,
        string? statusOverride = null,
        CancellationToken cancellationToken = default)
    {
        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var entries = _info.Snapshot(source, _gameId);
        var rows = new List<WorkshopRowViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            var row = WorkshopRowViewModel.FromCache(entry, size);
            if (_info.TryGetCachedCover(WorkshopInfoCache.Key(source, entry.Id), out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
        }

        ReplaceRows(rows);
        StartNameTranslation(rows, cancellationToken);

        if (rows.Count > 0)
        {
            _totalCount = rows.Count;
            _totalPages = 1;
            StatusMessage = T(Keys.WorkshopStatusOffline, rows.Count);
            _logger.Warn("创意工坊查询失败，改用缓存（" + source + "，" + rows.Count + " 条）：" + reason);
            return;
        }

        var message = statusOverride ?? T(Keys.WorkshopStatusFailed, reason);

        // 连不上 Steam 时补一句「系统里有代理但没在用」：这是本机上最常见的失败原因，
        // 不说出来用户只会反复点刷新。
        if (string.Equals(reason, "steam-workshop-network", StringComparison.Ordinal))
        {
            var hint = DescribeDetectedProxyHint();
            if (hint.Length > 0)
            {
                message = message + " " + hint;
            }
        }

        StatusMessage = message;
    }

    /// <summary>
    /// 并发补封面（同时最多 6 张）：每张完成就刷新对应行，不阻塞列表显示。
    /// 翻页会取消上一轮任务，避免下载队列越积越长。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task FillCoversAsync(CancellationToken cancellationToken)
    {
        CancelCoverFill();
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _coverLifetime = lifetime;
        var token = lifetime.Token;

        var pending = Rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.CoverUrl) && !row.HasCover)
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        _coverTotal = pending.Count;
        _coverDone = 0;
        IsFillingCovers = true;
        RaiseCoverProgress();

        var root = _info.CoverRoot;
        using var gate = new SemaphoreSlim(MaxConcurrentCovers);
        try
        {
            AppServices.FileSystem.EnsureDirectory(root);
            AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "steam"));
            AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "pdx"));

            await Task.WhenAll(pending.Select(async row =>
            {
                await gate.WaitAsync(token).ConfigureAwait(true);
                try
                {
                    token.ThrowIfCancellationRequested();
                    var target = AppServices.FileSystem.Combine(root, row.CoverCacheKey);
                    var local = await _assets.DownloadAsync(row.CoverUrl!, target, token).ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(local))
                    {
                        return;
                    }

                    row.CoverPath = local;

                    // 记下文件名（含来源子目录），下次这条 id 再出现直接绑本地文件。
                    _info.SetCover(
                        WorkshopInfoCache.Key(row.Source, row.Id),
                        row.CoverCacheKey + Path.GetExtension(local),
                        row.CoverUrl);
                }
                finally
                {
                    gate.Release();
                    _coverDone++;
                    RaiseCoverProgress();
                }
            })).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            _logger.Info("取消上一页封面任务");
        }
        catch (Exception ex)
        {
            _logger.Warn("创意工坊封面下载失败：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_coverLifetime, lifetime))
            {
                _coverLifetime = null;
            }

            lifetime.Dispose();
            IsFillingCovers = false;
            _coverDone = 0;
            _coverTotal = 0;
            RaiseDerived();

            // 落盘放到线程池：封面写完再收尾，不占 UI 线程。
            _ = Task.Run(_info.Flush);
        }
    }

    /// <summary>把 Steam 列表条目并进缓存（不覆盖已抓到的封面文件名与详情）。</summary>
    /// <param name="item">Steam 条目。</param>
    private void RecordSteamItem(SteamWorkshopItem item)
    {
        _info.Upsert(new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = item.PublishedFileId,
            GameId = _gameId,
            Name = item.Title,
            Author = item.AuthorSteamId,
            Rating = item.StarRating,
            RatingsTotal = item.TotalVotes,
            Subscriptions = item.Subscriptions,
            UpdatedUtc = item.UpdatedUtc,
            SizeBytes = item.FileSizeBytes,
            CoverUrl = item.PreviewUrl,
        });
    }

    /// <summary>把 PDX 列表条目并进缓存。</summary>
    /// <param name="summary">PDX 条目。</param>
    private void RecordPdxItem(PdxModsSummary summary)
    {
        _info.Upsert(new WorkshopInfoCacheEntry
        {
            Source = "pdx",
            Id = summary.ModId,
            GameId = _gameId,
            Name = summary.DisplayName,
            Author = summary.Author,
            Rating = summary.Rating,
            RatingsTotal = summary.RatingsTotal,
            Subscriptions = summary.Subscriptions,
            UpdatedUtc = summary.UpdatedOn,
            CoverUrl = summary.CoverUrl,
        });
    }

    /// <summary>当前来源可用的排序文案键。</summary>
    private static string[] SortKeysFor(WorkshopSource source) =>
        source == WorkshopSource.Steam ? SteamSortKeys : PdxSortKeys;

    /// <summary>把当前选中文案反查成 Steam 的排序枚举。</summary>
    private SteamWorkshopSort ResolveSteamSort() => ResolveSortKey() switch
    {
        var key when key == Keys.WorkshopSortTopRated => SteamWorkshopSort.TopRated,
        var key when key == Keys.WorkshopSortMostRecent => SteamWorkshopSort.MostRecent,
        var key when key == Keys.WorkshopSortMostUpdated => SteamWorkshopSort.MostUpdated,
        var key when key == Keys.WorkshopSortMostSubscribed => SteamWorkshopSort.MostSubscribed,
        _ => SteamWorkshopSort.Trend,
    };

    /// <summary>把当前选中文案反查成 PDX Mods 的排序枚举。</summary>
    private PdxModsSort ResolvePdxSort() => ResolveSortKey() switch
    {
        var key when key == Keys.WorkshopSortMostUpdated => PdxModsSort.Updated,
        var key when key == Keys.WorkshopSortRating => PdxModsSort.Rating,
        _ => PdxModsSort.Popularity,
    };

    /// <summary>当前时间范围对应的 <c>days</c> 值。</summary>
    private int ResolveDays()
    {
        var index = Array.FindIndex(TimeRangeModes.ToArray(), item => string.Equals(item, SelectedTimeRange, StringComparison.Ordinal));
        return TimeRangeDays[Math.Clamp(index < 0 ? DefaultTimeRangeIndex : index, 0, TimeRangeDays.Length - 1)];
    }

    /// <summary>当前时间范围下标的序号（按 <see cref="TimeRangeModes"/> 的顺序，与文案无关）。</summary>
    private int CurrentTimeRangeIndex()
    {
        var index = Array.FindIndex(TimeRangeModes.ToArray(), item => string.Equals(item, SelectedTimeRange, StringComparison.Ordinal));
        return index < 0 ? DefaultTimeRangeIndex : index;
    }

    /// <summary>当前排序下标的序号（按 <see cref="SortModes"/> 的顺序，与文案无关）。</summary>
    private int CurrentSortIndex()
    {
        var index = Array.FindIndex(SortModes.ToArray(), item => string.Equals(item, SelectedSortMode, StringComparison.Ordinal));
        return index < 0 ? 0 : index;
    }

    /// <summary>把下拉框里的选项位置反查成文案键（切语言后仍然是同一档排序）。</summary>
    private string ResolveSortKey()
    {
        var keys = SortKeysFor(Source);
        var index = CurrentSortIndex();
        return index < keys.Length ? keys[index] : keys[0];
    }

    /// <summary>排序下拉框的选项（按来源与当前语言生成）。</summary>
    private static string[] BuildSortModes(WorkshopSource source) =>
        SortKeysFor(source).Select(static key => T(key)).ToArray();

    /// <summary>时间范围下拉框的选项。</summary>
    private static string[] BuildTimeRangeModes() => TimeRangeKeys.Select(static key => T(key)).ToArray();

    partial void OnSearchTextChanged(string value)
    {
        // 打字只更新绑定值：真正发请求的是回车 / 刷新走的提交路径。
        // 如果在这里查询，每敲一个字符都会排一次 1–3 MB 的工坊页请求（限流下 1 秒一次），页面就像卡死。
    }

    partial void OnSelectedSortModeChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        OnPropertyChanged(nameof(ShowTimeRange));

        _queryTask = RunQueryAsync(resetPage: true, force: true);
    }

    partial void OnSelectedTimeRangeChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        _queryTask = RunQueryAsync(resetPage: true, force: true);
    }

    partial void OnSourceChanged(WorkshopSource value)
    {
        // 来源换了，排序档位跟着换（Steam 5 档 / PDX 3 档）；重置到第一档避免选中项越界。
        RebuildSortModes(value);
        OnPropertyChanged(nameof(ShowTimeRange));
        RaiseDerived();
    }

    /// <summary>当前排序是否属于「带时间范围」的两档（最热门 / 最受好评）。</summary>
    private bool IsTimeRangeSort()
    {
        var key = ResolveSortKey();
        return key == Keys.WorkshopSortTrend || key == Keys.WorkshopSortTopRated;
    }

    /// <summary>按来源重建排序档位，并把选中项放到第一档。</summary>
    /// <param name="source">当前来源。</param>
    private void RebuildSortModes(WorkshopSource source)
    {
        var wasRelocalizing = _isRelocalizing;
        _isRelocalizing = true;
        try
        {
            SortModes = BuildSortModes(source);
            SelectedSortMode = SortModes[0];
        }
        finally
        {
            _isRelocalizing = wasRelocalizing;
        }

        OnPropertyChanged(nameof(SelectedSortMode));
    }

    /// <summary>切语言后重建排序与时间范围选项，并把选中项换到新语言的同一档上。</summary>
    protected override void Relocalize()
    {
        var sortIndex = CurrentSortIndex();
        var timeIndex = CurrentTimeRangeIndex();

        _isRelocalizing = true;
        try
        {
            SortModes = BuildSortModes(Source);
            SelectedSortMode = SortModes[Math.Clamp(sortIndex, 0, SortModes.Count - 1)];
            TimeRangeModes = BuildTimeRangeModes();
            SelectedTimeRange = TimeRangeModes[Math.Clamp(timeIndex, 0, TimeRangeModes.Count - 1)];
        }
        finally
        {
            _isRelocalizing = false;
        }

        // 新旧文案可能相同（例如中英文都是「最热门」），显式补一次通知让 x:Bind 刷新。
        OnPropertyChanged(nameof(SelectedSortMode));
        OnPropertyChanged(nameof(SelectedTimeRange));
        RaiseDerived();
    }

    /// <summary>派生属性一次性重发通知。</summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(GameTitle));
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(ShowNoGameHint));
        OnPropertyChanged(nameof(SourceLabel));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasPrevPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowSignInHint));
        OnPropertyChanged(nameof(ShowTimeRange));
        OnPropertyChanged(nameof(ShowCoverProgress));
        OnPropertyChanged(nameof(CoverProgressText));
    }
}
