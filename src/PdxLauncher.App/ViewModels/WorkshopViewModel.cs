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
/// 判定顺序：<see cref="WorkshopSourceChooser.Resolve"/> 先用 <c>SteamAppId</c> 排除独立版，
/// 再等 <see cref="SteamWorkshopClient.HasWorkshopAsync"/> 的探测结果；探测请求本身失败时
/// 退回 PDX Mods 并在状态栏说明原因，而不是把页面留在空状态。
/// </para>
/// 本类不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed partial class WorkshopViewModel : LocalizedObservableObject
{
    /// <summary>PDX Mods 每页条数。</summary>
    private const int PdxPageSize = 24;

    /// <summary>Steam 每页条数。</summary>
    private const int SteamPageSize = SteamWorkshopClient.DefaultPageSize;

    /// <summary>三档排序的文案键：两个平台共用一套顺序（最热门 / 最近更新 / 评分最高）。</summary>
    private static readonly string[] SortKeys =
    [
        Keys.WorkshopSortPopular,
        Keys.WorkshopSortRecent,
        Keys.WorkshopSortRating,
    ];

    private readonly SteamWorkshopClient _steam;
    private readonly PdxModsClient _pdx;
    private readonly AccountService _account;
    private readonly IAssetDownloader _assets;
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

    /// <summary>语言切换时重建排序下拉框：此时改选中项不算用户操作。</summary>
    private bool _isRelocalizing;

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

    /// <summary>搜索词。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前排序方式，取值来自 <see cref="SortModes"/>。</summary>
    [ObservableProperty]
    private string _selectedSortMode = string.Empty;

    /// <summary>排序下拉框的选项（按当前语言生成）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sortModes = [];

    /// <summary>当前显示的来源。</summary>
    [ObservableProperty]
    private WorkshopSource _source;

    public WorkshopViewModel(
        SteamWorkshopClient steam,
        PdxModsClient pdx,
        AccountService account,
        IAssetDownloader assets,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(pdx);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);

        _steam = steam;
        _pdx = pdx;
        _account = account;
        _assets = assets;
        _logger = logger;

        SortModes = BuildSortModes();
        _selectedSortMode = SortModes[0];
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

    /// <summary>页码文案：第 1 / 42 页；没有结果时为空串。</summary>
    public string PageText => _totalPages > 0 ? T(Keys.WorkshopPage, _page + 1, _totalPages) : string.Empty;

    /// <summary>空结果时显示提示（已完成查询且没有任何行，且已选中游戏）。</summary>
    public bool ShowEmptyState => IsLoaded && HasGame && Rows.Count == 0;

    /// <summary>PDX 来源且未登录时显示「去登录」提示条。</summary>
    public bool ShowSignInHint => Source == WorkshopSource.Pdx && !_account.IsSignedIn;

    /// <summary>
    /// 换游戏 / 首次进入：重置页码与列表，判定来源后查一次。
    /// </summary>
    /// <param name="game">当前游戏；null 表示还没选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        _gameId = game?.GameId;
        _steamAppId = game?.SteamAppId;
        _page = 0;
        _totalCount = 0;
        _totalPages = 0;
        Rows.Clear();
        Source = WorkshopSource.None;

        if (game is null)
        {
            IsLoaded = true;
            StatusMessage = T(Keys.WorkshopNoGameHint);
            RaiseDerived();
            return;
        }

        if (!game.SupportsContentManagement)
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
            Source = await ResolveSourceAsync(game, cancellationToken).ConfigureAwait(true);
            RaiseDerived();
            await QueryAsync(cancellationToken).ConfigureAwait(true);
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

    /// <summary>刷新当前页。</summary>
    public Task RefreshAsync() => HasGame
        ? QueryAsync(CancellationToken.None)
        : LoadAsync(Game, CancellationToken.None);

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
        await QueryAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>取 Steam 工坊条目的详情（页面用它弹详情对话框）。</summary>
    /// <param name="row">Steam 来源的行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopItemDetail>> GetSteamDetailAsync(
        WorkshopRowViewModel row,
        CancellationToken cancellationToken) =>
        _steam.GetDetailAsync(row.Id, cancellationToken);

    /// <summary>取 PDX Mods 条目的详情（页面用它弹详情对话框）。</summary>
    /// <param name="row">PDX 来源的行。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<PdxModsDetail>> GetPdxDetailAsync(
        WorkshopRowViewModel row,
        CancellationToken cancellationToken) =>
        _pdx.GetDetailAsync(row.Id, _account.Session, cancellationToken);

    /// <summary>
    /// 判定来源：没有 Steam appId（独立版）直接 PDX；否则探测该游戏有没有 Steam 工坊。
    /// 探测请求失败时退回 PDX，并把原因写进状态栏。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task<WorkshopSource> ResolveSourceAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        if (game.SteamAppId is not { } appId)
        {
            return WorkshopSource.Pdx;
        }

        var probe = await _steam.HasWorkshopAsync(appId, cancellationToken).ConfigureAwait(true);
        if (!probe.IsSuccess)
        {
            StatusMessage = T(Keys.WorkshopProbeFailed);
            _logger.Warn("Steam 工坊探测失败（appId=" + appId + "）：" + (probe.ErrorMessage ?? "unknown"));
            return WorkshopSource.Pdx;
        }

        return WorkshopSourceChooser.Resolve(appId, probe.Value);
    }

    /// <summary>
    /// 按当前来源执行一次查询：先清空列表再查，成功后建行并异步补封面。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task QueryAsync(CancellationToken cancellationToken)
    {
        if (_gameId is null || Source == WorkshopSource.None)
        {
            return;
        }

        IsBusy = true;
        Rows.Clear();
        RaiseDerived();

        try
        {
            if (Source == WorkshopSource.Steam)
            {
                await QuerySteamAsync(cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await QueryPdxAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            IsLoaded = true;
            Rows.Clear();
            StatusMessage = T(Keys.WorkshopStatusFailed, ex.Message);
            _logger.Error("创意工坊查询失败（game=" + _gameId + "）", ex);
            RaiseDerived();
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>Steam 创意工坊查询。</summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task QuerySteamAsync(CancellationToken cancellationToken)
    {
        if (_steamAppId is not { } appId)
        {
            return;
        }

        var query = new SteamWorkshopQuery(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            ResolveSteamSort(),
            _page,
            SteamPageSize);

        var result = await _steam.BrowseAsync(appId, query, cancellationToken).ConfigureAwait(true);
        IsLoaded = true;

        if (!result.IsSuccess || result.Value is null)
        {
            StatusMessage = result.Kind == ErrorKind.Unsupported
                ? T(Keys.WorkshopStatusParseFailed)
                : T(Keys.WorkshopStatusFailed, result.ErrorMessage ?? "steam-workshop");
            RaiseDerived();
            return;
        }

        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalPages;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        foreach (var item in page.Items)
        {
            Rows.Add(WorkshopRowViewModel.FromSteam(item, size));
        }

        StatusMessage = Rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, SearchText.Trim())
            : T(Keys.WorkshopStatusLoaded, _totalCount);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);
    }

    /// <summary>PDX Mods 查询。</summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task QueryPdxAsync(CancellationToken cancellationToken)
    {
        if (_gameId is not { } gameId)
        {
            return;
        }

        var query = new PdxModsQuery(
            string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
            ResolvePdxSort(),
            Descending: true,
            Page: _page,
            PageSize: PdxPageSize);

        var result = await _pdx
            .SearchAsync(gameId, query, _account.Session, cancellationToken)
            .ConfigureAwait(true);

        IsLoaded = true;

        if (!result.IsSuccess || result.Value is null)
        {
            StatusMessage = T(Keys.WorkshopStatusFailed, result.ErrorMessage ?? "pdx-mods");
            RaiseDerived();
            return;
        }

        var page = result.Value;
        _totalCount = page.TotalCount;
        _totalPages = page.TotalCount == 0 ? 0 : ((page.TotalCount - 1) / PdxPageSize) + 1;

        var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
        foreach (var item in page.Items)
        {
            Rows.Add(WorkshopRowViewModel.FromPdx(item, size));
        }

        StatusMessage = Rows.Count == 0
            ? T(Keys.WorkshopStatusEmpty, SearchText.Trim())
            : T(Keys.WorkshopStatusLoaded, _totalCount);
        RaiseDerived();

        _ = FillCoversAsync(cancellationToken);
    }

    /// <summary>
    /// 封面补全：把封面下载到 <c>data\workshop-covers\{steam|pdx}\{id}</c> 再绑路径。
    /// 纯增强，任何失败只记日志；下载到一张就刷新一行。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task FillCoversAsync(CancellationToken cancellationToken)
    {
        var pending = Rows.Where(static row => !string.IsNullOrWhiteSpace(row.CoverUrl)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var root = AppServices.WorkshopCoverRoot;
        try
        {
            AppServices.FileSystem.EnsureDirectory(root);
            AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "steam"));
            AppServices.FileSystem.EnsureDirectory(AppServices.FileSystem.Combine(root, "pdx"));

            foreach (var row in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = AppServices.FileSystem.Combine(root, row.CoverCacheKey);
                var local = await _assets
                    .DownloadAsync(row.CoverUrl!, target, cancellationToken)
                    .ConfigureAwait(true);

                if (!string.IsNullOrWhiteSpace(local))
                {
                    row.CoverPath = local;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已卸载：补图任务随令牌作废，不需要提示。
        }
        catch (Exception ex)
        {
            _logger.Warn("创意工坊封面下载失败：" + ex.Message);
        }
    }

    /// <summary>把当前选中文案反查成 Steam 的排序枚举。</summary>
    private SteamWorkshopSort ResolveSteamSort() => ResolveSortKey() switch
    {
        var key when key == Keys.WorkshopSortRecent => SteamWorkshopSort.MostRecent,
        var key when key == Keys.WorkshopSortRating => SteamWorkshopSort.TopRated,
        _ => SteamWorkshopSort.MostPopular,
    };

    /// <summary>把当前选中文案反查成 PDX Mods 的排序枚举。</summary>
    private PdxModsSort ResolvePdxSort() => ResolveSortKey() switch
    {
        var key when key == Keys.WorkshopSortRecent => PdxModsSort.Updated,
        var key when key == Keys.WorkshopSortRating => PdxModsSort.Rating,
        _ => PdxModsSort.Popularity,
    };

    /// <summary>把下拉框里的文案反查成文案键（切语言后仍然是同一档排序）。</summary>
    private string ResolveSortKey()
    {
        foreach (var key in SortKeys)
        {
            if (string.Equals(T(key), SelectedSortMode, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return SortKeys[0];
    }

    /// <summary>排序下拉框的选项（按当前语言生成）。</summary>
    private string[] BuildSortModes() => SortKeys.Select(static key => T(key)).ToArray();

    partial void OnSearchTextChanged(string value) => _ = OnFilterChangedAsync();

    partial void OnSelectedSortModeChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        _ = OnFilterChangedAsync();
    }

    partial void OnSourceChanged(WorkshopSource value) => RaiseDerived();

    /// <summary>搜索词或排序变了：回到第一页重查。</summary>
    private async Task OnFilterChangedAsync()
    {
        if (!HasGame || _isRelocalizing)
        {
            return;
        }

        _page = 0;
        await QueryAsync(CancellationToken.None).ConfigureAwait(true);
    }

    /// <summary>切语言后重建排序选项，并把选中项换到新语言的同一档上。</summary>
    protected override void Relocalize()
    {
        var sortKey = ResolveSortKey();
        SortModes = BuildSortModes();

        _isRelocalizing = true;
        try
        {
            SelectedSortMode = T(sortKey);
        }
        finally
        {
            _isRelocalizing = false;
        }

        // 新旧文案可能相同（例如中英文都是「Paradox Mods」），显式补一次通知让 x:Bind 刷新。
        OnPropertyChanged(nameof(SelectedSortMode));
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
    }
}
