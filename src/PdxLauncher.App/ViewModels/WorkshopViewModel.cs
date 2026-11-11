namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.ComponentModel;

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
    /// <summary>默认每页条数（与 Steam 网页一致）。</summary>
    private const int DefaultPageSize = 30;

    /// <summary>「每页」下拉框的档位（与 Steam 网页一致：10 / 15 / 30 / 50）。</summary>
    private static readonly int[] PageSizeChoices = [10, 15, 30, 50];

    /// <summary>创意工坊所在的 host：限流冷却按 host 记账，提示文案要查它。</summary>
    private const string SteamCommunityHost = "steamcommunity.com";

    /// <summary>
    /// 筛选变化到发请求之间的去抖：一口连点几个「+/-」只查一次。
    /// Steam 对同一主机的最小请求间隔是 1 秒，700 ms 去抖后仍会被限流层兜住。
    /// </summary>
    private static readonly TimeSpan FilterDebounce = TimeSpan.FromMilliseconds(700);

    /// <summary>Steam 的排序档位（与网页一致，含「搜索相关度」）。</summary>
    private static readonly string[] SteamSortKeys =
    [
        Keys.WorkshopSortTrend,
        Keys.WorkshopSortTopRated,
        Keys.WorkshopSortMostRecent,
        Keys.WorkshopSortMostUpdated,
        Keys.WorkshopSortRelevance,
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

    /// <summary>Steam 浏览区段的文案键（顺序与 <see cref="SteamWorkshopSection"/> 一致）。</summary>
    private static readonly string[] SectionKeys =
    [
        Keys.WorkshopSectionItems,
        Keys.WorkshopSectionCollections,
    ];

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

    /// <summary>当前每页条数（Steam / PDX 共用）。</summary>
    private int _pageSize = DefaultPageSize;

    /// <summary>筛选去抖令牌；每次筛选变化都取消上一轮。</summary>
    private CancellationTokenSource? _filterDebounce;

    /// <summary>上一次生成的页码序列；相同就不重建集合，避免 x:Bind 抖动。</summary>
    private string _pageButtonsSignature = string.Empty;

    /// <summary>按 appId 记住上次解析到的筛选组：切回看过的游戏时，命中页缓存也能显示侧栏。</summary>
    private readonly Dictionary<int, IReadOnlyList<SteamWorkshopFilterGroup>> _filterGroupsByApp = new();

    /// <summary>筛选目录的落盘缓存（最近一次抓到的分组 + 抓取时刻 + 本地游戏版本）。</summary>
    private readonly WorkshopFilterCatalogCache _catalogCache;

    /// <summary>当前 app 的目录快照；null 表示还没抓到过（此时用静态目录兜底）。</summary>
    private WorkshopFilterCatalogSnapshot? _catalogSnapshot;

    /// <summary>目录刷新失败的时刻（内存退避，成功后清空）。</summary>
    private DateTimeOffset _lastCatalogFailureUtc = DateTimeOffset.MinValue;

    /// <summary>目录刷新是否在途（单飞，避免进页面时连发请求）。</summary>
    private bool _catalogRefreshing;

    /// <summary>在途的目录刷新令牌；换游戏 / 页面卸载时取消。</summary>
    private CancellationTokenSource? _catalogCts;

    /// <summary>本轮查询里因为标签消失而被取消的筛选，需要在查询结束后重查一次。</summary>
    private bool _pendingFilterRequery;

    /// <summary>目录快照最小写盘间隔（结构没变时最多每小时写一次）。</summary>
    private static readonly TimeSpan CatalogSaveMinInterval = TimeSpan.FromHours(1);

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

    /// <summary>标题翻译的去抖时长：翻页/换筛选很密时把几批合成一批，别反复唤醒本地模型。</summary>
    private static readonly TimeSpan NameTranslationDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>本轮待翻译批次的去抖令牌；新批次到来时取消上一批。</summary>
    private CancellationTokenSource? _translationDebounce;

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

    /// <summary>Steam 侧状态的整库读取（60 秒缓存 → 子进程 → 失败退旧值）。</summary>
    private readonly WorkshopStateReader _steamStates;

    /// <summary>状态填充代号：切页 / 换游戏后旧结果不许回写。</summary>
    private int _steamStateFillVersion;

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

    /// <summary>浏览区段（项目 / 合集），取值来自 <see cref="SectionModes"/>（仅 Steam）。</summary>
    [ObservableProperty]
    private string _selectedSection = string.Empty;

    /// <summary>浏览区段下拉框的选项（按当前语言生成）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sectionModes = [];

    /// <summary>当前显示的来源。</summary>
    [ObservableProperty]
    private WorkshopSource _source;

    /// <summary>是否正在补封面。</summary>
    [ObservableProperty]
    private bool _isFillingCovers;

    /// <summary>
    /// 最近一次查询是否以失败 / 断网兜底结束。
    /// 用来把「加载失败」和「查询成功但确实没有内容」分开：空列表提示只在后者显示，
    /// 否则网络断掉时页面会写着「这个游戏还没有创意工坊内容」，用户以为刷新坏了。
    /// </summary>
    [ObservableProperty]
    private bool _hasLoadFailure;

    /// <summary>当前选中的每页条数（分页器下拉框）。</summary>
    [ObservableProperty]
    private int _selectedPageSize = DefaultPageSize;

    /// <summary>是否展开左侧筛选栏（工具条上的「筛选」按钮切换）。</summary>
    [ObservableProperty]
    private bool _showFilterPanel = true;

    public WorkshopViewModel(
        SteamWorkshopClient steam,
        PdxModsClient pdx,
        AccountService account,
        IAssetDownloader assets,
        WorkshopInfoCache info,
        WorkshopPageCache pages,
        WorkshopFilterCatalogCache catalogCache,
        IAppLogger logger,
        WorkshopStateReader steamStates)
    {
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(pdx);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(catalogCache);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(steamStates);

        _steam = steam;
        _pdx = pdx;
        _account = account;
        _assets = assets;
        _info = info;
        _pages = pages;
        _catalogCache = catalogCache;
        _logger = logger;
        _steamStates = steamStates;

        SortModes = BuildSortModes(WorkshopSource.None);
        _selectedSortMode = SortModes[0];
        TimeRangeModes = BuildTimeRangeModes();
        _selectedTimeRange = TimeRangeModes[DefaultTimeRangeIndex];
        SectionModes = BuildSectionModes();
        _selectedSection = SectionModes[(int)SteamWorkshopSection.ReadyToUseItems];

        Filters = new WorkshopFiltersViewModel();
        Filters.Changed += OnFiltersChanged;
        Filters.PropertyChanged += OnFiltersPropertyChanged;
    }

    /// <summary>当前可见的创意工坊行。</summary>
    public BulkObservableCollection<WorkshopRowViewModel> Rows { get; } = new();

    /// <summary>左侧筛选栏的状态（分组、标签、DLC、不兼容、特别筛选）。</summary>
    public WorkshopFiltersViewModel Filters { get; }

    /// <summary>「每页」下拉框的档位。</summary>
    public IReadOnlyList<int> PageSizeOptions => PageSizeChoices;

    /// <summary>分页器上的页码/省略号。</summary>
    public ObservableCollection<WorkshopPageButton> PageButtons { get; } = new();

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

    /// <summary>空结果时显示提示（查询成功、没有失败、没有在加载、且确实一行都没有）。</summary>
    public bool ShowEmptyState =>
        IsLoaded && !IsBusy && HasGame && Rows.Count == 0 && !HasLoadFailure;

    /// <summary>加载失败且没有可显示的旧行时，在列表区中间显示失败原因 + 重试。</summary>
    public bool ShowLoadFailureState =>
        HasLoadFailure && !IsBusy && HasGame && Rows.Count == 0;

    /// <summary>PDX 来源且未登录时显示「去登录」提示条。</summary>
    public bool ShowSignInHint => Source == WorkshopSource.Pdx && !_account.IsSignedIn;

    /// <summary>时间范围只对 Steam 的「最热门 / 最受好评」有意义（Steam 网页也只在这两个排序下给时间范围），
    /// 其他排序或 PDX 来源都隐藏这个下拉。
    /// </summary>
    public bool ShowTimeRange => Source == WorkshopSource.Steam && IsTimeRangeSort();

    /// <summary>浏览区段切换只对 Steam 有意义（PDX Mods 没有合集）。</summary>
    public bool ShowSectionPicker => Source == WorkshopSource.Steam;

    /// <summary>当前是否在浏览合集。</summary>
    public bool IsCollectionsSection => ResolveSection() == SteamWorkshopSection.Collections;

    /// <summary>只有 Steam 来源有筛选栏（PDX Mods 没有标签/DLC 维度）。</summary>
    public bool ShowFilters => Source == WorkshopSource.Steam;

    /// <summary>工具条上的「筛选」按钮是否可见：来源支持且确实拿到了分组数据。</summary>
    public bool CanShowFilters => ShowFilters && Filters.HasGroups;

    /// <summary>实际是否显示筛选栏：来源支持、用户没收起、且确实拿到了分组数据。</summary>
    public bool ShowFilterSidebar => ShowFilterPanel && ShowFilters && Filters.HasGroups;

    /// <summary>
    /// 页面从导航缓存重新激活时，把查询令牌换成新的页面生命周期令牌。
    /// <para>
    /// 页面卸载会取消旧令牌，但工坊页 <c>NavigationCacheMode=Required</c>，从详情返回时
    /// 走的是「沿用已加载列表」的早退分支，不会重新 LoadAsync；不换令牌的话刷新/搜索/
    /// 筛选都从已取消的旧令牌派生，点了等于没点。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">页面新的生命周期令牌。</param>
    public void RebindPageToken(CancellationToken cancellationToken)
    {
        if (!cancellationToken.IsCancellationRequested)
        {
            _pageToken = cancellationToken;
        }
    }

    /// <summary>
    /// 换游戏 / 首次进入：重置页码与列表，判定来源后查一次。
    /// </summary>
    /// <param name="game">当前游戏；null 表示还没选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        _gameId = game?.GameId;
        HasLoadFailure = false;

        // 换游戏 / 重新加载：上一轮查询作废；已提交的搜索词跟随输入框当前内容。
        CancelPendingQuery();
        CancelPendingFilterQuery();
        CancelCatalogRefresh();
        _pendingFilterRequery = false;
        ApplyPersistedPageSize();

        // 每个游戏从「无筛选」开始；分组数据会在来源判定后按 appId 重新装载。
        Filters.Reset();
        _pageToken = cancellationToken;
        _appliedSearch = SearchText.Trim();

        // 独立安装读不到 appmanifest 时 SteamAppId 是 null，这里用映射表兜底，
        // 否则 EU4 / CS1 / 监狱建筑师这些明明有 Steam 工坊的游戏会被误判成没有。
        _steamAppId = WorkshopSourceChooser.ResolveAppId(game?.SteamAppId, game?.GameId);

        // 先把上次落盘的筛选目录读出来：重启/离线时侧栏直接显示已知最新版本，
        // 不必等首屏网络请求，也不会退回写死 1.13 的静态目录。
        _catalogSnapshot = null;
        if (_steamAppId is { } catalogAppId)
        {
            var snapshot = await Task
                .Run(() => _catalogCache.TryLoad(catalogAppId), cancellationToken)
                .ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested)
            {
                _catalogSnapshot = snapshot;
            }
        }

        _page = 0;
        _totalCount = 0;
        _totalPages = 0;
        CancelCoverFill();
        CancelPendingNameTranslation();
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

        // 阶段耗时同时进日志和看门狗面包屑：卡死时看门狗那一行会带出「最后走到哪一步」。
        AppServices.Breadcrumb("工坊 开始加载（game=" + (game.GameId) + "）");
        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Source = await ResolveSourceAsync(cancellationToken).ConfigureAwait(true);
            AppServices.Breadcrumb("工坊 来源=" + Source + "（" + watch.ElapsedMilliseconds + " ms）");
            RaiseDerived();
            await RunQueryAsync(resetPage: false, force: false).ConfigureAwait(true);

            // 列表本身可能完全来自页缓存（或刚刚查完），但筛选目录有自己的 24h TTL /
            // 本地版本判断；不新鲜时在后台补一次，不挡列表显示。
            _ = RefreshCatalogIfStaleAsync(cancellationToken);
            _logger.Info("创意工坊加载完成（game=" + game.GameId + "）：" + watch.ElapsedMilliseconds + " ms");
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
    /// 连不上 Steam 时补一句代理说明：
    /// 当前已经在走代理 → 说「代理已生效但仍连不上，检查代理本身」；
    /// 当前是直连、系统里有代理 → 报出地址，提示去设置里切过去。
    /// </summary>
    private string DescribeDetectedProxyHint()
    {
        try
        {
            // 只读探测：不改任何设置，也不改当前生效的代理。
            var current = ProxyResolver.Describe(AppServices.Settings);
            if (current.Url is not null)
            {
                return T(Keys.WorkshopStatusProxyActiveFailed, current.Url);
            }

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

    /// <summary>跳到指定页（0 基）；越界或与当前页相同都不动。</summary>
    /// <param name="pageIndex">目标页码，从 0 开始。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task GoToPageAsync(int pageIndex, CancellationToken cancellationToken)
    {
        if (pageIndex < 0 || (_totalPages > 0 && pageIndex >= _totalPages) || pageIndex == _page)
        {
            return;
        }

        _page = pageIndex;
        _pageToken = cancellationToken;
        await RunQueryAsync(resetPage: false, force: false).ConfigureAwait(true);
    }

    /// <summary>切换每页条数：回到第 1 页并跳过缓存（页缓存键里已经带了页大小）。</summary>
    /// <param name="pageSize">新的每页条数（不在档位里时回落到默认 30）。</param>
    public Task ChangePageSizeAsync(int pageSize)
    {
        var normalized = NormalizePageSize(pageSize);
        if (normalized == _pageSize)
        {
            return Task.CompletedTask;
        }

        _pageSize = normalized;
        SelectedPageSize = normalized;
        AppServices.Settings.WorkshopPageSize = normalized;
        AppServices.SaveSettingsDeferred();
        return RunQueryAsync(resetPage: true, force: true);
    }

    /// <summary>筛选变化：去抖后回到第 1 页重查。</summary>
    private void OnFiltersChanged(object? sender, EventArgs e)
    {
        if (!HasGame || Source != WorkshopSource.Steam || _pageToken.IsCancellationRequested)
        {
            return;
        }

        CancelPendingFilterQuery();
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(_pageToken);
        _filterDebounce = debounce;
        _ = ApplyFiltersAfterDebounceAsync(debounce);
    }

    /// <summary>去抖到点后按新筛选查第一页；令牌被取代时什么都不做。</summary>
    private async Task ApplyFiltersAfterDebounceAsync(CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(FilterDebounce, debounce.Token).ConfigureAwait(true);
            _queryTask = RunQueryAsync(resetPage: true, force: true);
            await _queryTask.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被下一次筛选 / 换游戏 / 页面卸载取代：正常路径。
        }
        catch (Exception ex)
        {
            _logger.Error("创意工坊筛选查询失败（game=" + _gameId + "）", ex);
        }
        finally
        {
            if (ReferenceEquals(_filterDebounce, debounce))
            {
                _filterDebounce = null;
            }

            debounce.Dispose();
        }
    }

    /// <summary>取消尚未触发的筛选去抖（换游戏 / 页面卸载时用）。</summary>
    public void CancelPendingFilterQuery()
    {
        var pending = _filterDebounce;
        _filterDebounce = null;
        if (pending is null)
        {
            return;
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

    /// <summary>从设置里恢复每页条数（不在档位里就回到默认 30）。</summary>
    private void ApplyPersistedPageSize()
    {
        var stored = NormalizePageSize(AppServices.Settings.WorkshopPageSize);
        _pageSize = stored;
        SelectedPageSize = stored;
    }

    private static int NormalizePageSize(int value)
    {
        foreach (var choice in PageSizeChoices)
        {
            if (choice == value)
            {
                return choice;
            }
        }

        return DefaultPageSize;
    }

    /// <summary>筛选栏的派生状态（是否显示）变化时通知绑定。</summary>
    private void OnFiltersPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkshopFiltersViewModel.HasGroups))
        {
            OnPropertyChanged(nameof(ShowFilterSidebar));
            OnPropertyChanged(nameof(CanShowFilters));
        }
    }

    partial void OnShowFilterPanelChanged(bool value) => OnPropertyChanged(nameof(ShowFilterSidebar));

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
        AppServices.Breadcrumb("工坊 查询（source=" + Source + "，page=" + (_page + 1) + "）");

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
            HasLoadFailure = true;
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

            await ApplyCachedPageAsync(cached, cancellationToken).ConfigureAwait(true);
            if (version != _queryVersion || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            HasLoadFailure = false;
            StatusMessage = T(Keys.WorkshopStatusLoadedCached);
            RaiseDerived();

            // 过期的页仍然先显示，再悄悄刷新一次；用户不必盯着转圈。
            // 同一页两分钟内只补一次，否则来回切页会重复打同一个地址。
            var refreshingPage = false;
            if (!_pages.IsFresh(cached) && _pages.CanReattempt(cached, SilentRefreshMinInterval))
            {
                _pages.MarkAttempt(key);
                refreshingPage = true;
                _queryTask = RefreshSilentlyAsync(key, version, cancellationToken);
            }

            // 页缓存新鲜时不会发列表请求，筛选目录可能已经过期（兼容性版本更新）；
            // 静默刷新本来就会带回目录，所以只在没刷新页时补一次。
            if (!refreshingPage)
            {
                _ = RefreshCatalogIfStaleAsync(cancellationToken);
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
                await ShowFailureOrCacheAsync(Source, "throttled", cooldown, cancellationToken).ConfigureAwait(true);
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
        HasLoadFailure = false;
        StatusMessage = _appliedSearch.Length > 0
            ? T(Keys.WorkshopStatusSearching)
            : T(Keys.WorkshopStatusLoadingPage, _page + 1);
        RaiseDerived();

        try
        {
            if (Source == WorkshopSource.Steam)
            {
                await QuerySteamAsync(version, cancellationToken, key, force).ConfigureAwait(true);
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
                HasLoadFailure = true;
                await ShowFailureOrCacheAsync(
                    Source,
                    ex.Message,
                    ex is SteamThrottleException ? DescribeFailure(ex) : null,
                    cancellationToken).ConfigureAwait(true);
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

                // 目录刷新发现已选标签消失：本页是按失效标签查的，收尾后重新查第 1 页。
                if (_pendingFilterRequery)
                {
                    _pendingFilterRequery = false;
                    _queryTask = RunQueryAsync(resetPage: true, force: true);
                }
            }
        }
    }

    /// <summary>生成页缓存的键：来源、游戏、已提交的关键词、排序、时间范围、页码、页大小、筛选栏都参与。</summary>
    private string BuildPageKey()
    {
        var search = _appliedSearch.Length == 0 ? null : _appliedSearch;
        if (Source == WorkshopSource.Steam)
        {
            var sort = ResolveSteamSort();
            var days = sort is SteamWorkshopSort.Trend or SteamWorkshopSort.TopRated ? ResolveDays() : (int?)null;
            return WorkshopPageCache.Key(
                WorkshopSource.Steam,
                _gameId,
                search,
                sort.ToString(),
                days,
                _page,
                _pageSize,
                Filters.RequiredTags,
                Filters.ExcludedTags,
                Filters.RequiredAppIds,
                Filters.ExcludedAppIds,
                Filters.ShowIncompatible,
                Filters.SpecialFilter,
                ResolveSection());
        }

        return WorkshopPageCache.Key(
            WorkshopSource.Pdx,
            _gameId,
            search,
            ResolvePdxSort().ToString(),
            null,
            _page,
            _pageSize);
    }

    /// <summary>
    /// 把缓存的一页直接铺到列表上（封面只认磁盘命中，不联网）。
    /// <para>
    /// 建行原先每行都按 key 再查一次 SQLite（开连接 + 三条 pragma），30–50 行压在 UI 线程上会拖慢切页；
    /// 页快照本身已经带封面文件名，这里只判文件是否存在，并把整段建行丢到线程池。
    /// </para>
    /// </summary>
    /// <param name="page">页快照。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task ApplyCachedPageAsync(WorkshopPageCache.Page page, CancellationToken cancellationToken)
    {
        _totalCount = page.TotalCount;
        _totalPages = page.TotalPages;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var rows = await Task
            .Run(() => BuildCachedRows(page.Items, size, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        ReplaceRows(rows);
        StartNameTranslation(rows, cancellationToken);
    }

    /// <summary>线程池上把页快照建成行模型；封面只看条目里的文件名，不再回查 SQLite。</summary>
    /// <param name="items">页快照条目。</param>
    /// <param name="thumbnailSize">缩略图边长（像素）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>行模型。</returns>
    private List<WorkshopRowViewModel> BuildCachedRows(
        IReadOnlyList<WorkshopInfoCacheEntry> items,
        int thumbnailSize,
        CancellationToken cancellationToken)
    {
        var rows = new List<WorkshopRowViewModel>(items.Count);
        foreach (var entry in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 合集标记以缓存条目自身为准：页缓存键已经按区段隔离，但普通条目的旧缓存不能误标。
            var row = WorkshopRowViewModel.FromCache(entry, thumbnailSize, entry.IsCollection);
            if (_info.TryGetCachedCover(entry, out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// 给一行行模型补译名（后台跑，译好一行刷一行）。
    /// <para>翻译是网络操作，不能挡着列表显示：这里只起一个后台任务，行模型自己接住结果。</para>
    /// <para>
    /// 250 ms 去抖：翻页、换筛选、返回缓存页都会各来一批，立即开工只会让本地模型
    /// 反复「唤醒—推理—被取消」。等手停下来再翻，一批就够。
    /// </para>
    /// </summary>
    /// <param name="rows">要翻译的行。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartNameTranslation(IEnumerable<WorkshopRowViewModel> rows, CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateNamesEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        var pending = rows
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrEmpty(row.TranslatedName))
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        CancelPendingNameTranslation();

        var debounce = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _translationDebounce = debounce;
        _ = TranslateNamesAfterDebounceAsync(pending, debounce);
    }

    /// <summary>去抖到点后翻这一批；令牌被新批次取消时什么都不做。</summary>
    private async Task TranslateNamesAfterDebounceAsync(
        IReadOnlyList<WorkshopRowViewModel> rows,
        CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(NameTranslationDebounce, debounce.Token).ConfigureAwait(true);
            await TranslateNamesAsync(rows, debounce.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // 被新批次取代：这一批作废。
        }
        finally
        {
            if (ReferenceEquals(_translationDebounce, debounce))
            {
                _translationDebounce = null;
            }

            debounce.Dispose();
        }
    }

    /// <summary>取消还没到点/没跑完的标题翻译批次。</summary>
    private void CancelPendingNameTranslation()
    {
        var previous = _translationDebounce;
        _translationDebounce = null;
        if (previous is null)
        {
            return;
        }

        try
        {
            previous.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 上一批刚好自己收尾释放了令牌，不用再取消。
        }
    }

    /// <summary>整页标题一次性批翻（引擎内部并发），译完统一回填。</summary>
    private async Task TranslateNamesAsync(
        IReadOnlyList<WorkshopRowViewModel> rows,
        CancellationToken cancellationToken)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            AppServices.Breadcrumb("工坊 翻译标题 " + rows.Count + " 条");
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
                ReportTranslationFailure(rows.Count, watch.ElapsedMilliseconds);
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
    /// <param name="count">本批条数（写日志用）。</param>
    /// <param name="elapsedMs">本批耗时（写日志用）。</param>
    private void ReportTranslationFailure(int count, long elapsedMs)
    {
        var key = BuildPageKey();
        if (string.Equals(_translateFailureKey, key, StringComparison.Ordinal))
        {
            return;
        }

        _translateFailureKey = key;
        var failure = AppServices.Translation.LastFailure;
        _logger.Warn(
            "创意工坊标题全部未翻译（" + key + "）：" + (failure ?? T(Keys.CommonUnknown))
            + "；本批 " + count + " 条 / " + elapsedMs + " ms"
            + (failure is null ? "，引擎未给出失败原因" : string.Empty));
        SetStatus(T(Keys.WorkshopTranslateUnavailable, failure ?? T(Keys.CommonUnknown)));
    }

    /// <summary>整体替换列表行；先取消上一轮封面下载，避免旧页任务继续占用带宽。</summary>
    /// <param name="rows">新行。</param>
    private void ReplaceRows(IEnumerable<WorkshopRowViewModel> rows)
    {
        CancelCoverFill();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Rows.ReplaceAll(rows);
        watch.Stop();
        if (watch.ElapsedMilliseconds >= 30)
        {
            _logger.Info("创意工坊铺行耗时 " + watch.ElapsedMilliseconds + " ms（" + Rows.Count + " 行）");
        }

        // 列表先铺出来再补状态：整库状态可能要起一次子进程（约 1 秒），不能挡着这一屏。
        StartSteamStateFill();
    }

    /// <summary>
    /// 后台给当前这一页的 Steam 行补上安装 / 下载状态（走 60 秒缓存，通常不新起子进程）。
    /// 从详情页返回时也用它：那边改完订阅会失效缓存，这里就能读到新值。
    /// </summary>
    public void RefreshSteamStates() => StartSteamStateFill();

    /// <summary>起一轮状态填充；后起的覆盖先起的，切页 / 换游戏后的旧结果不回写。</summary>
    private void StartSteamStateFill()
    {
        if (Source != WorkshopSource.Steam || Game is null || Rows.Count == 0)
        {
            return;
        }

        var version = ++_steamStateFillVersion;
        _ = FillSteamStatesAsync(version, _pageToken);
    }

    /// <summary>读整库状态并写到各行；没有状态的行走「不显示徽标」。</summary>
    /// <param name="version">发起时的填充代号。</param>
    /// <param name="cancellationToken">页面生命周期令牌。</param>
    private async Task FillSteamStatesAsync(int version, CancellationToken cancellationToken)
    {
        try
        {
            var states = await _steamStates.ReadAllAsync(Game, cancellationToken).ConfigureAwait(true);
            if (cancellationToken.IsCancellationRequested || version != _steamStateFillVersion)
            {
                return;
            }

            var applied = 0;
            foreach (var row in Rows)
            {
                if (row.Source != WorkshopSource.Steam)
                {
                    continue;
                }

                if (ulong.TryParse(row.Id, out var fileId) && states.TryGetValue(fileId, out var state))
                {
                    row.SteamState = state;
                    applied++;
                }
                else
                {
                    row.SteamState = null;
                }
            }

            if (applied > 0)
            {
                _logger.Info("创意工坊：已补 " + applied + " 条 Steam 状态（本页 " + Rows.Count + " 行）");
            }
        }
        catch (OperationCanceledException)
        {
            // 页面卸载 / 又翻了一页：放弃这一批。
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _logger.Warn("创意工坊：读取 Steam 状态失败：" + ex.Message);
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
                await QuerySteamAsync(version, cancellationToken, key, silent: true).ConfigureAwait(true);
            }
            else
            {
                await QueryPdxAsync(version, cancellationToken, key, silent: true).ConfigureAwait(true);
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
        finally
        {
            // 静默刷新不走 RunQueryAsync，失效标签的重查要在这里收口；
            // 否则标记会留到下一条无关查询，造成一次莫名其妙的跳页。
            if (_pendingFilterRequery && !cancellationToken.IsCancellationRequested)
            {
                _pendingFilterRequery = false;
                _queryTask = RunQueryAsync(resetPage: true, force: true);
            }
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

        // 这里**不** Dispose：封面任务现在跑在线程池上，池线程可能正拿着这个令牌
        // 等信号量或注册取消回调；UI 线程此时 Dispose 会让它们抛 ObjectDisposedException。
        // 令牌的所有权归 FillCoversAsync，它跑完自己的 finally 再释放（见 FillCoversAsync 末尾）。
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
    /// <param name="force">true = 强制走网络（刷新 / 回车搜索 / 换筛选），不复用旧的在途请求。</param>
    private async Task QuerySteamAsync(
        int version,
        CancellationToken cancellationToken,
        string pageKey,
        bool force = false,
        bool silent = false)
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
            _pageSize,
            days,
            Filters.RequiredTags,
            Filters.ExcludedTags,
            Filters.RequiredAppIds,
            Filters.ExcludedAppIds,
            Filters.ShowIncompatible,
            Filters.SpecialFilter,
            ResolveSection());

        var result = await _steam
            .BrowseAsync(appId, query, cancellationToken, bypassCoalescing: force)
            .ConfigureAwait(true);

        // 已经被新一代查询或页面卸载取代：这一次的结果直接丢掉，别覆盖新列表。
        if (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        IsLoaded = true;

        if (!result.IsSuccess || result.Value is null)
        {
            if (silent)
            {
                // 静默刷新只是后台补新，失败不能把用户正在看的页替换成离线全量缓存，
                // 也不能改页码/总数；保留当前列表，原因写日志即可。
                HasLoadFailure = false;
                _logger.Warn("创意工坊静默刷新失败，保留当前页（"
                    + (result.ErrorMessage ?? "unknown") + "）");
                return;
            }

            HasLoadFailure = true;
            var errorCode = result.Kind == ErrorKind.Unsupported
                ? "steam-workshop-parse"
                : result.ErrorMessage ?? "steam-workshop";
            await ShowFailureOrCacheAsync(
                WorkshopSource.Steam,
                errorCode,
                DescribeWorkshopError(errorCode, result.Kind),
                cancellationToken).ConfigureAwait(true);
            RaiseDerived();
            return;
        }

        HasLoadFailure = false;
        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalPages;
        AppServices.Breadcrumb("工坊 结果已回（" + page.Items.Count + " 条）");

        // 筛选组以页面数据为准；页面改版/解析不到时退回持久化快照 / 内置目录（Victoria 3）。
        var applyResult = ApplyFilterGroups(page.Groups);

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);

        // 建行时每条都要读写一次工坊信息缓存，而那是同步 SQLite（每条一次开连接 + 提交），
        // 30 条实测 5 秒多，全压在 UI 线程上就是切页面卡死。整段丢线程池，
        // 回 UI 线程只做 ReplaceRows 与绑定（行模型本身不是 UI 对象）。
        var collectionsSection = IsCollectionsSection;
        var gameId = _gameId;
        var (rows, entries) = await Task
            .Run(
                () => BuildSteamRows(page.Items, size, collectionsSection, gameId, cancellationToken),
                cancellationToken)
            .ConfigureAwait(true);

        AppServices.Breadcrumb("工坊 建行完成 " + rows.Count + " 行");
        ReplaceRows(rows);
        AppServices.Breadcrumb("工坊 列表已铺 " + rows.Count + " 行");
        StartNameTranslation(rows, cancellationToken);
        _pages.Set(pageKey, entries, _totalCount, _totalPages);
        AppServices.Breadcrumb("工坊 页缓存已写");

        // 30 条累加后只落盘一次（逐条 Save 会卡在同步 IO 上），并放到后台线程，不占 UI 线程。
        await FlushInfoCacheAsync().ConfigureAwait(true);
        AppServices.Breadcrumb("工坊 信息已落盘");
        StatusMessage = rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, _appliedSearch)
            : IsRelevanceWithoutKeyword()
                ? T(Keys.WorkshopSortRelevanceNeedsKeyword)
                : T(Keys.WorkshopStatusLoaded);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);

        if (applyResult.RemovedActiveTags.Count > 0)
        {
            // Steam 移除了已勾选的标签：Apply 已经静默取消选择，但这一页仍按旧标签查过，
            // 等本轮回合收尾后回第 1 页重查，避免列表停在失效筛选上。
            _logger.Info("创意工坊筛选条件已失效，准备重查：" + string.Join(", ", applyResult.RemovedActiveTags));
            _pendingFilterRequery = true;
        }
    }

    /// <summary>
    /// 在线程池上建好 Steam 列表页的行模型：每条都要读写一次工坊信息缓存（同步 SQLite），
    /// 放在 UI 线程上 30 条就是几秒的停顿。
    /// <para>
    /// 元数据先攒成一批、一个事务写完（逐条 Upsert 每条都要开连接、<c>BEGIN IMMEDIATE</c>、提交，
    /// 再执行一次 <c>COUNT(*)</c>）；随后整页只读一次库拿回封面文件名与页缓存条目。
    /// </para>
    /// </summary>
    /// <param name="items">列表页条目。</param>
    /// <param name="thumbnailSize">缩略图边长（像素）。</param>
    /// <param name="collectionsSection">是否合集区段。</param>
    /// <param name="gameId">抓取时所属游戏（在 UI 线程上先取好，避免建行途中换游戏）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>行模型与对应的缓存条目快照。</returns>
    private (List<WorkshopRowViewModel> Rows, List<WorkshopInfoCacheEntry> Entries) BuildSteamRows(
        IReadOnlyList<SteamWorkshopItem> items,
        int thumbnailSize,
        bool collectionsSection,
        string? gameId,
        CancellationToken cancellationToken)
    {
        var pending = new List<WorkshopInfoCacheEntry>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending.Add(CreateSteamEntry(item, gameId, collectionsSection));
        }

        if (pending.Count > 0)
        {
            _info.UpsertMany(pending);
        }

        var rows = new List<WorkshopRowViewModel>(items.Count);
        var entries = new List<WorkshopInfoCacheEntry>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheKey = WorkshopInfoCache.Key(WorkshopSource.Steam, item.PublishedFileId);
            var row = WorkshopRowViewModel.FromSteam(item, thumbnailSize, collectionsSection);
            if (_info.TryGet(cacheKey, out var entry))
            {
                if (_info.TryGetCachedCover(entry, out var cover))
                {
                    row.CoverPath = cover;
                }

                entries.Add(entry);
            }

            rows.Add(row);
        }

        return (rows, entries);
    }

    /// <summary>PDX Mods 查询。</summary>
    /// <param name="version">查询代号；结果回来时不是最新一代就丢弃。</param>
    /// <param name="cancellationToken">本轮查询的取消令牌。</param>
    /// <param name="pageKey">页缓存键（成功后把这一页写进去）。</param>
    private async Task QueryPdxAsync(
        int version,
        CancellationToken cancellationToken,
        string pageKey,
        bool silent = false)
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
            PageSize: _pageSize);

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
            if (silent)
            {
                HasLoadFailure = false;
                _logger.Warn("创意工坊 PDX 静默刷新失败，保留当前页（"
                    + (result.ErrorMessage ?? "unknown") + "）");
                return;
            }

            HasLoadFailure = true;
            await ShowFailureOrCacheAsync(
                WorkshopSource.Pdx,
                result.ErrorMessage ?? "pdx-mods",
                cancellationToken: cancellationToken).ConfigureAwait(true);
            RaiseDerived();
            return;
        }

        HasLoadFailure = false;
        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalCount == 0 ? 0 : ((page.TotalCount - 1) / _pageSize) + 1;
        AppServices.Breadcrumb("工坊 PDX 结果已回（" + page.Items.Count + " 条）");

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);

        // 与 Steam 分支同一条规矩：建行要逐条读写工坊信息缓存（同步 SQLite），
        // 30 条压在 UI 线程上就是切页面卡死。整段丢线程池，回 UI 线程只做铺行与绑定。
        var (rows, entries) = await Task
            .Run(() => BuildPdxRows(page.Items, size, gameId, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        // 建行期间可能又起了新查询（翻页 / 换筛选）：迟到的结果直接丢弃。
        if (version != _queryVersion || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        AppServices.Breadcrumb("工坊 PDX 建行完成 " + rows.Count + " 行");
        ReplaceRows(rows);
        AppServices.Breadcrumb("工坊 列表已铺 " + rows.Count + " 行");
        StartNameTranslation(rows, cancellationToken);
        _pages.Set(pageKey, entries, _totalCount, _totalPages);
        await FlushInfoCacheAsync().ConfigureAwait(true);
        StatusMessage = rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, _appliedSearch)
            : T(Keys.WorkshopStatusLoaded);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);
    }

    /// <summary>
    /// 在线程池上建好 PDX 列表页的行模型：与 <see cref="BuildSteamRows"/> 同样的原因，
    /// 逐条 SQLite 读写不能压在 UI 线程上；元数据同样一个事务批量写入。
    /// </summary>
    /// <param name="items">PDX 条目。</param>
    /// <param name="thumbnailSize">缩略图边长（像素）。</param>
    /// <param name="gameId">抓取时所属游戏（在 UI 线程上先取好，避免建行途中换游戏）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>行模型与对应的缓存条目快照。</returns>
    private (List<WorkshopRowViewModel> Rows, List<WorkshopInfoCacheEntry> Entries) BuildPdxRows(
        IReadOnlyList<PdxModsSummary> items,
        int thumbnailSize,
        string? gameId,
        CancellationToken cancellationToken)
    {
        var pending = new List<WorkshopInfoCacheEntry>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            pending.Add(CreatePdxEntry(item, gameId));
        }

        if (pending.Count > 0)
        {
            _info.UpsertMany(pending);
        }

        var rows = new List<WorkshopRowViewModel>(items.Count);
        var entries = new List<WorkshopInfoCacheEntry>(items.Count);
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cacheKey = WorkshopInfoCache.Key(WorkshopSource.Pdx, item.ModId);
            var row = WorkshopRowViewModel.FromPdx(item, thumbnailSize);
            if (_info.TryGet(cacheKey, out var entry))
            {
                if (_info.TryGetCachedCover(entry, out var cover))
                {
                    row.CoverPath = cover;
                }

                entries.Add(entry);
            }

            rows.Add(row);
        }

        return (rows, entries);
    }

    /// <summary>
    /// 查询失败：先用这个游戏之前缓存过的条目把列表填上，
    /// 一条都没有才显示失败原因。断网时页面仍然是可用的。
    /// <para>
    /// 快照读取与建行都放线程池：缓存条目最多 2000 条，原先在 UI 线程上逐条按 key 回查 SQLite，
    /// 连不上 PDX / Steam 时这一下能把界面按住好几秒。快照条目自带封面文件名，这里不再回查。
    /// </para>
    /// </summary>
    /// <param name="source">当前来源。</param>
    /// <param name="reason">失败原因（用于日志/兜底文案）。</param>
    /// <param name="statusOverride">限流等特殊情况下的状态栏文案；null 表示用通用失败文案。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task ShowFailureOrCacheAsync(
        WorkshopSource source,
        string reason,
        string? statusOverride = null,
        CancellationToken cancellationToken = default)
    {
        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        var gameId = _gameId;
        var collectionFilter = source == WorkshopSource.Steam ? IsCollectionsSection : (bool?)null;
        var rows = await Task
            .Run(() => BuildFallbackRows(source, size, gameId, collectionFilter, cancellationToken), cancellationToken)
            .ConfigureAwait(true);

        if (cancellationToken.IsCancellationRequested)
        {
            return;
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
        HasLoadFailure = true;
    }

    /// <summary>线程池上从离线快照建行；封面只判条目里的文件名，不回查 SQLite。</summary>
    /// <param name="source">来源。</param>
    /// <param name="thumbnailSize">缩略图边长（像素）。</param>
    /// <param name="gameId">抓取时所属游戏（在 UI 线程上先取好，避免建行途中换游戏）。</param>
    /// <param name="collectionFilter">Steam 区段过滤：true=只合集、false=只普通项、null=不过滤。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>行模型。</returns>
    private List<WorkshopRowViewModel> BuildFallbackRows(
        WorkshopSource source,
        int thumbnailSize,
        string? gameId,
        bool? collectionFilter,
        CancellationToken cancellationToken)
    {
        var entries = _info.Snapshot(source, gameId);
        if (collectionFilter is { } isCollection)
        {
            entries = entries.Where(entry => entry.IsCollection == isCollection).ToList();
        }

        var rows = new List<WorkshopRowViewModel>(entries.Count);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var row = WorkshopRowViewModel.FromCache(entry, thumbnailSize, entry.IsCollection);
            if (_info.TryGetCachedCover(entry, out var cover))
            {
                row.CoverPath = cover;
            }

            rows.Add(row);
        }

        return rows;
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
        AppServices.Breadcrumb("工坊 补封面 " + pending.Count + " 张");

        var root = _info.CoverRoot;

        // 整段封面流水线留在 UI 线程会形成「下载 → 回 UI → 同步写 SQLite → 每张一次」
        // 的串行链：30 张图就是 30 次 UI 线程上的同步写盘。这里改由线程池跑，
        // 只有行模型的 CoverPath（绑定属性）与进度计数回 UI 线程。
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        using var gate = new SemaphoreSlim(MaxConcurrentCovers);
        try
        {
            // 目录准备是纯文件 IO：丢线程池，别让 UI 线程在这里等磁盘（后面每张封面都要用到这三个目录）。
            await Task.Run(
                    () =>
                    {
                        AppServices.FileSystem.EnsureDirectory(root);
                        AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "steam"));
                        AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "pdx"));
                    },
                    token)
                .ConfigureAwait(true);

            await Task.WhenAll(pending.Select(row =>
                    Task.Run(
                        () => FillOneCoverAsync(row, gate, root, token, dispatcher, lifetime),
                        token)))
                .ConfigureAwait(true);
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

    /// <summary>下载并登记一张封面；在线程池上跑，只有行属性与进度回 UI 线程。</summary>
    private async Task FillOneCoverAsync(
        WorkshopRowViewModel row,
        SemaphoreSlim gate,
        string root,
        CancellationToken token,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher,
        CancellationTokenSource lifetime)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var target = AppServices.FileSystem.Combine(root, row.CoverCacheKey);
            var local = await _assets.DownloadAsync(row.CoverUrl!, target, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(local))
            {
                return;
            }

            // 记下文件名（含来源子目录），下次这条 id 再出现直接绑本地文件。
            // 这一步是同步 SQLite 写，必须留在当前线程，别带回 UI。
            _info.SetCover(
                WorkshopInfoCache.Key(row.Source, row.Id),
                row.CoverCacheKey + Path.GetExtension(local),
                row.CoverUrl);

            SetCoverPath(row, local, dispatcher);
        }
        finally
        {
            gate.Release();
            ReportCoverDone(dispatcher, lifetime);
        }
    }

    /// <summary>把本地封面路径写回行模型（区间在 UI 线程）。</summary>
    private static void SetCoverPath(
        WorkshopRowViewModel row,
        string local,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
    {
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            row.CoverPath = local;
            return;
        }

        dispatcher.TryEnqueue(() => row.CoverPath = local);
    }

    /// <summary>进度 +1 并刷新文案；只改自己这一轮的计数。</summary>
    private void ReportCoverDone(
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher,
        CancellationTokenSource lifetime)
    {
        if (dispatcher is not null && !dispatcher.HasThreadAccess)
        {
            if (!dispatcher.TryEnqueue(() => ReportCoverDone(null, lifetime)))
            {
                return;
            }

            return;
        }

        // 新一轮封面任务已经接管：这一轮的迟到计数不写，免得把新页的进度顶乱。
        if (!ReferenceEquals(_coverLifetime, lifetime))
        {
            return;
        }

        _coverDone++;
        RaiseCoverProgress();
    }

    /// <summary>把 Steam 列表条目转成待写入缓存的行（不覆盖已抓到的封面文件名与详情）。</summary>
    /// <param name="item">Steam 条目。</param>
    /// <param name="gameId">所属游戏（建行前在 UI 线程取好的快照）。</param>
    /// <param name="collectionsSection">当前是否合集区段（SSR 缺 file_type 时靠区段兜底）。</param>
    private static WorkshopInfoCacheEntry CreateSteamEntry(
        SteamWorkshopItem item,
        string? gameId,
        bool collectionsSection) => new()
    {
        Source = "steam",
        Id = item.PublishedFileId,
        GameId = gameId,
        Name = item.Title,
        Author = item.AuthorSteamId,
        Rating = item.StarRating,
        RatingsTotal = item.TotalVotes,
        Subscriptions = item.Subscriptions,
        UpdatedUtc = item.UpdatedUtc,
        SizeBytes = item.FileSizeBytes,
        CoverUrl = item.PreviewUrl,
        IsCollection = item.IsCollection || collectionsSection,
    };

    /// <summary>把 PDX 列表条目转成待写入缓存的行。</summary>
    /// <param name="summary">PDX 条目。</param>
    /// <param name="gameId">所属游戏（建行前在 UI 线程取好的快照）。</param>
    private static WorkshopInfoCacheEntry CreatePdxEntry(PdxModsSummary summary, string? gameId) => new()
    {
        Source = "pdx",
        Id = summary.ModId,
        GameId = gameId,
        Name = summary.DisplayName,
        Author = summary.Author,
        Rating = summary.Rating,
        RatingsTotal = summary.RatingsTotal,
        Subscriptions = summary.Subscriptions,
        UpdatedUtc = summary.UpdatedOn,
        CoverUrl = summary.CoverUrl,
    };

    /// <summary>当前来源可用的排序文案键。</summary>
    private static string[] SortKeysFor(WorkshopSource source) =>
        source == WorkshopSource.Steam ? SteamSortKeys : PdxSortKeys;

    /// <summary>把当前选中文案反查成 Steam 的排序枚举。</summary>
    private SteamWorkshopSort ResolveSteamSort() => ResolveSortKey() switch
    {
        var key when key == Keys.WorkshopSortTopRated => SteamWorkshopSort.TopRated,
        var key when key == Keys.WorkshopSortMostRecent => SteamWorkshopSort.MostRecent,
        var key when key == Keys.WorkshopSortMostUpdated => SteamWorkshopSort.MostUpdated,
        var key when key == Keys.WorkshopSortRelevance => SteamWorkshopSort.TextSearch,
        var key when key == Keys.WorkshopSortMostSubscribed => SteamWorkshopSort.MostSubscribed,
        _ => SteamWorkshopSort.Trend,
    };

    /// <summary>
    /// 选了「搜索相关度」却没有生效的关键词。
    /// <para>
    /// 服务端对没有 <c>searchtext</c> 的 <c>browsesort=textsearch</c> 不做相关度排序
    /// （<see cref="SteamWorkshopClient.BuildBrowseUri"/> 会回退成 trend），
    /// 所以这里要能识别出来，给状态栏一句「需要先输入关键词」，而不是让人以为选了没反应。
    /// </para>
    /// </summary>
    private bool IsRelevanceWithoutKeyword() =>
        ResolveSteamSort() == SteamWorkshopSort.TextSearch && _appliedSearch.Length == 0;

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

    /// <summary>浏览区段下拉框的选项。</summary>
    private static string[] BuildSectionModes() => SectionKeys.Select(static key => T(key)).ToArray();

    /// <summary>当前区段下拉框的序号；找不到时按「项目」处理。</summary>
    private int CurrentSectionIndex()
    {
        var index = Array.FindIndex(
            SectionModes.ToArray(),
            item => string.Equals(item, SelectedSection, StringComparison.Ordinal));
        return index < 0 ? 0 : index;
    }

    /// <summary>把当前选中的区段文案反查成枚举。</summary>
    private SteamWorkshopSection ResolveSection() =>
        CurrentSectionIndex() == (int)SteamWorkshopSection.Collections
            ? SteamWorkshopSection.Collections
            : SteamWorkshopSection.ReadyToUseItems;

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

    partial void OnSelectedSectionChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        OnPropertyChanged(nameof(IsCollectionsSection));
        _queryTask = RunQueryAsync(resetPage: true, force: true);
    }

    partial void OnHasLoadFailureChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowLoadFailureState));
    }

    partial void OnSourceChanged(WorkshopSource value)
    {
        // 来源换了，排序档位跟着换（Steam 6 档 / PDX 3 档）；重置到第一档避免选中项越界。
        RebuildSortModes(value);

        // 先按游戏装载筛选组（Victoria 3 有内置目录），首屏就能看到侧栏；
        // 等服务端结果回来再以 SSR 数据为准。
        ApplyFilterGroups([]);
        OnPropertyChanged(nameof(ShowTimeRange));
        OnPropertyChanged(nameof(ShowSectionPicker));
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(CanShowFilters));
        OnPropertyChanged(nameof(ShowFilterSidebar));
        RaiseDerived();
    }

    /// <summary>
    /// 把筛选组套到侧栏：服务端数据优先，其次本会话缓存、持久化快照，最后才是内置目录。
    /// 服务端数据同时更新内存与落盘快照，下次启动/离线仍能看到最新兼容性版本。
    /// </summary>
    /// <param name="serverGroups">列表页 SSR 解析出的分组；为空表示这次没有新数据。</param>
    private WorkshopFilterApplyResult ApplyFilterGroups(IReadOnlyList<SteamWorkshopFilterGroup> serverGroups)
    {
        if (Source != WorkshopSource.Steam || _steamAppId is not { } appId)
        {
            CancelCatalogRefresh();
            var cleared = Filters.Apply([]);
            OnPropertyChanged(nameof(ShowFilterSidebar));
            return cleared;
        }

        if (serverGroups.Count > 0)
        {
            _filterGroupsByApp[appId] = serverGroups;
            UpdateCatalogSnapshot(appId, serverGroups);
        }

        var groups = _filterGroupsByApp.TryGetValue(appId, out var session) && session.Count > 0
            ? session
            : _catalogSnapshot is { Groups.Count: > 0 } snapshot && snapshot.AppId == appId
                ? snapshot.Groups
                : WorkshopFilterCatalog.ForApp(appId);

        var result = Filters.Apply(groups);
        OnPropertyChanged(nameof(ShowFilterSidebar));
        return result;
    }

    /// <summary>
    /// 用这次 SSR 的分组更新目录快照；结构变了或距上次写盘超过 1 小时才落盘，
    /// 避免翻页时反复写同一个文件。
    /// </summary>
    private void UpdateCatalogSnapshot(int appId, IReadOnlyList<SteamWorkshopFilterGroup> groups)
    {
        var now = DateTimeOffset.UtcNow;
        var previous = _catalogSnapshot;
        var version = Game?.Version ?? string.Empty;
        var changed = previous is null
                      || previous.AppId != appId
                      || !string.Equals(
                          WorkshopFilterCatalogPolicy.Signature(previous.Groups),
                          WorkshopFilterCatalogPolicy.Signature(groups),
                          StringComparison.Ordinal);

        // 本地版本变化也要落盘：否则下次启动读到旧版本，会白白多抓一次目录。
        var versionChanged = previous is not null
                             && !string.Equals(previous.GameVersion, version, StringComparison.OrdinalIgnoreCase);

        _catalogSnapshot = new WorkshopFilterCatalogSnapshot(appId, version, now, groups);

        if (!changed && !versionChanged && previous is not null && now - previous.FetchedAtUtc < CatalogSaveMinInterval)
        {
            return;
        }

        var snapshot = _catalogSnapshot;
        _ = Task.Run(() =>
        {
            if (!_catalogCache.TrySave(snapshot))
            {
                _logger.Warn("创意工坊筛选目录落盘失败（appId=" + appId + "）");
            }
        });
    }

    /// <summary>
    /// 目录过期（24h / 本地版本变化）时后台补一次。
    /// <para>
    /// 跳过条件：非 Steam、没有 appId、列表查询进行中、Steam 冷却期、已在刷新、刚失败过 30 分钟。
    /// 这个方法自己收口所有异常，调用方可以安全地 fire-and-forget。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task RefreshCatalogIfStaleAsync(CancellationToken cancellationToken)
    {
        if (Source != WorkshopSource.Steam || _steamAppId is not { } appId || !HasGame)
        {
            return;
        }

        if (_catalogRefreshing || IsBusy || _queryTask is { IsCompleted: false } || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (TryGetCooldown(out _))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!WorkshopFilterCatalogPolicy.NeedsRefresh(_catalogSnapshot, Game?.Version, now))
        {
            return;
        }

        if (_lastCatalogFailureUtc != default
            && now - _lastCatalogFailureUtc < WorkshopFilterCatalogPolicy.FailureBackoff)
        {
            return;
        }

        _catalogRefreshing = true;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _catalogCts = cts;
        try
        {
            var result = await _steam.GetFilterGroupsAsync(appId, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested
                || Source != WorkshopSource.Steam
                || _steamAppId != appId)
            {
                return;
            }

            if (!result.IsSuccess || result.Value is not { Count: > 0 })
            {
                _lastCatalogFailureUtc = DateTimeOffset.UtcNow;
                _logger.Warn("创意工坊筛选目录刷新失败（appId=" + appId + "）：" + (result.ErrorMessage ?? "empty"));
                return;
            }

            _lastCatalogFailureUtc = default;
            var applyResult = ApplyFilterGroups(result.Value);
            if (applyResult.RemovedActiveTags.Count > 0)
            {
                _logger.Info("创意工坊筛选条件已失效，准备重查：" + string.Join(", ", applyResult.RemovedActiveTags));
                _queryTask = RunQueryAsync(resetPage: true, force: true);
            }
        }
        catch (OperationCanceledException)
        {
            // 换游戏 / 页面卸载 / 冷却切换：正常路径。
        }
        catch (Exception ex)
        {
            _lastCatalogFailureUtc = DateTimeOffset.UtcNow;
            _logger.Warn("创意工坊筛选目录刷新失败（appId=" + appId + "）：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_catalogCts, cts))
            {
                _catalogCts = null;
            }

            cts.Dispose();
            _catalogRefreshing = false;
        }
    }

    /// <summary>取消在途的目录刷新（换游戏 / 页面卸载时用）。</summary>
    public void CancelCatalogRefresh()
    {
        var pending = _catalogCts;
        _catalogCts = null;
        if (pending is null)
        {
            return;
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
        var sectionIndex = CurrentSectionIndex();

        _isRelocalizing = true;
        try
        {
            SortModes = BuildSortModes(Source);
            SelectedSortMode = SortModes[Math.Clamp(sortIndex, 0, SortModes.Count - 1)];
            TimeRangeModes = BuildTimeRangeModes();
            SelectedTimeRange = TimeRangeModes[Math.Clamp(timeIndex, 0, TimeRangeModes.Count - 1)];
            SectionModes = BuildSectionModes();
            SelectedSection = SectionModes[Math.Clamp(sectionIndex, 0, SectionModes.Count - 1)];
        }
        finally
        {
            _isRelocalizing = false;
        }

        // 新旧文案可能相同（例如中英文都是「最热门」），显式补一次通知让 x:Bind 刷新。
        OnPropertyChanged(nameof(SelectedSortMode));
        OnPropertyChanged(nameof(SelectedTimeRange));
        OnPropertyChanged(nameof(SelectedSection));
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
        OnPropertyChanged(nameof(ShowLoadFailureState));
        OnPropertyChanged(nameof(ShowSignInHint));
        OnPropertyChanged(nameof(ShowTimeRange));
        OnPropertyChanged(nameof(ShowFilters));
        OnPropertyChanged(nameof(CanShowFilters));
        OnPropertyChanged(nameof(ShowFilterSidebar));
        OnPropertyChanged(nameof(ShowCoverProgress));
        OnPropertyChanged(nameof(CoverProgressText));
        RebuildPageButtons();
    }

    /// <summary>
    /// 重建分页器上的页码格子：始终给首页、末页与当前页左右一页，
    /// 中间用省略号连接；总数不足时全部显示。页码序列没变就不动集合。
    /// </summary>
    private void RebuildPageButtons()
    {
        var buttons = BuildPageButtons(_page, _totalPages);
        var signature = string.Join(
            "|",
            buttons.Select(static button => button.IsEllipsis
                ? "*"
                : (button.PageIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                  + (button.IsCurrent ? "!" : string.Empty)));
        if (string.Equals(signature, _pageButtonsSignature, StringComparison.Ordinal))
        {
            return;
        }

        _pageButtonsSignature = signature;
        PageButtons.Clear();
        foreach (var button in buttons)
        {
            PageButtons.Add(button);
        }
    }

    private static List<WorkshopPageButton> BuildPageButtons(int page, int totalPages)
    {
        var result = new List<WorkshopPageButton>();
        if (totalPages <= 0)
        {
            return result;
        }

        var pages = new SortedSet<int> { 0, totalPages - 1 };
        for (var index = Math.Max(0, page - 1); index <= Math.Min(totalPages - 1, page + 1); index++)
        {
            pages.Add(index);
        }

        // 靠前时补足前三页（1 2 3 … N），靠后时补足最后三页（1 … N-2 N-1 N）。
        if (page <= 1)
        {
            for (var index = 0; index < Math.Min(3, totalPages); index++)
            {
                pages.Add(index);
            }
        }

        if (page >= totalPages - 2)
        {
            for (var index = Math.Max(0, totalPages - 3); index < totalPages; index++)
            {
                pages.Add(index);
            }
        }

        var previous = -2;
        foreach (var index in pages)
        {
            if (index - previous > 1)
            {
                result.Add(new WorkshopPageButton(0, IsEllipsis: true, IsCurrent: false));
            }

            result.Add(new WorkshopPageButton(index, IsEllipsis: false, IsCurrent: index == page));
            previous = index;
        }

        return result;
    }
}
