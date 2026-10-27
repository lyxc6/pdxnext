namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;

/// <summary>
/// Mod 库页「在线（PDX Mods）」视图的视图模型：调 <see cref="PdxModsClient"/> 搜索、
/// 排序、翻页，并把封面下载到本地缓存后补进行里。
/// <para>
/// 与本地视图（<see cref="ModLibraryViewModel"/>）互相独立：两者各持一份列表与状态栏，
/// 页面按用户选中的「来源」只显示其中一个。本类不引用任何 Microsoft.UI.Xaml 类型。
/// </para>
/// </summary>
public sealed partial class OnlineModsViewModel : LocalizedObservableObject
{
    /// <summary>每页条数。PDX Mods 允许到 200，但一屏 24 条才好翻。</summary>
    private const int PageSize = 24;

    /// <summary>
    /// 排序方式的键。下拉框显示本地化文案，选中项存的也是文案，
    /// 所以切语言时要按键把选中项换到新语言的文案上（与本地视图同一套做法）。
    /// </summary>
    private static readonly string[] SortKeys =
    [
        Keys.ModlibOnlineSortBest,
        Keys.ModlibOnlineSortPopularity,
        Keys.ModlibOnlineSortRating,
        Keys.ModlibOnlineSortUpdated,
        Keys.ModlibOnlineSortCreated,
        Keys.ModlibOnlineSortName,
    ];

    private readonly PdxModsClient _client;
    private readonly AccountService _account;
    private readonly IAssetDownloader _assets;
    private readonly IAppLogger _logger;

    /// <summary>当前结果的游戏注册 ID；null 表示还没选游戏。</summary>
    private string? _gameId;

    /// <summary>当前页码（0 基）。</summary>
    private int _page;

    /// <summary>总条数（服务端返回）。</summary>
    private int _totalCount;

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

    public OnlineModsViewModel(
        PdxModsClient client,
        AccountService account,
        IAssetDownloader assets,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _account = account;
        _assets = assets;
        _logger = logger;

        SortModes = BuildSortModes();
        _selectedSortMode = SortModes[0];
    }

    /// <summary>当前可见的在线 mod 行。</summary>
    public ObservableCollection<OnlineModRowViewModel> Mods { get; } = new();

    /// <summary>是否已选中游戏（XAML 据此显示「先选游戏」提示）。</summary>
    public bool HasGame => _gameId is not null;

    /// <summary>空结果时显示提示（已完成查询且没有任何行）。</summary>
    public bool ShowNoResultsHint => IsLoaded && HasGame && Mods.Count == 0;

    /// <summary>是否可翻到上一页。</summary>
    public bool HasPrevPage => _page > 0;

    /// <summary>是否还有下一页。</summary>
    public bool HasNextPage => (_page + 1) * PageSize < _totalCount;

    /// <summary>页码文案：第 1 / 42 页；无结果时为空串。</summary>
    public string PageText => _totalCount == 0
        ? string.Empty
        : T(Keys.ModlibOnlinePage, _page + 1, TotalPages);

    /// <summary>总页数。</summary>
    public int TotalPages => _totalCount == 0 ? 0 : ((_totalCount - 1) / PageSize) + 1;

    /// <summary>
    /// 换游戏 / 首次进入：重置搜索与页码，然后查一次。
    /// </summary>
    /// <param name="game">当前游戏；null 表示还没选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        _gameId = game?.GameId;
        _page = 0;
        _totalCount = 0;

        if (_gameId is null)
        {
            Mods.Clear();
            IsLoaded = true;
            StatusMessage = T(Keys.ModlibOnlineNoGameHint);
            RaiseDerived();
            return;
        }

        await QueryAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>刷新当前页。</summary>
    public Task RefreshAsync() => HasGame
        ? QueryAsync(CancellationToken.None)
        : LoadAsync(Game, CancellationToken.None);

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

    /// <summary>取某个 mod 的详情（页面用它弹详情对话框）。</summary>
    /// <param name="modId">PDX Mods 的数字 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<PdxModsDetail>> GetDetailAsync(string modId, CancellationToken cancellationToken) =>
        _client.GetDetailAsync(modId, _account.Session, cancellationToken);

    /// <summary>
    /// 执行一次搜索：先清空列表再查，成功后建行并异步补封面。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task QueryAsync(CancellationToken cancellationToken)
    {
        if (_gameId is null)
        {
            return;
        }

        IsBusy = true;
        Mods.Clear();
        RaiseDerived();

        try
        {
            var query = new PdxModsQuery(
                string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim(),
                ResolveSort(),
                Descending: true,
                Page: _page,
                PageSize: PageSize);

            var result = await _client
                .SearchAsync(_gameId, query, _account.Session, cancellationToken)
                .ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                IsLoaded = true;
                StatusMessage = T(Keys.ModlibOnlineStatusFailed, DescribeFailure(result));
                RaiseDerived();
                return;
            }

            var page = result.Value;
            _totalCount = page.TotalCount;

            var size = ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize);
            foreach (var item in page.Items)
            {
                Mods.Add(new OnlineModRowViewModel(item, size));
            }

            IsLoaded = true;
            StatusMessage = Mods.Count == 0
                ? T(Keys.ModlibOnlineStatusEmpty, SearchText.Trim())
                : T(Keys.ModlibOnlineStatusLoaded, page.TotalCount);
            RaiseDerived();

            _ = FillCoversAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            IsLoaded = true;
            Mods.Clear();
            StatusMessage = T(Keys.ModlibOnlineStatusFailed, ex.Message);
            _logger.Error("PDX Mods 搜索失败（gameId=" + _gameId + "）", ex);
            RaiseDerived();
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// 封面补全：把 <c>displayImagePath</c> 下载到本地缓存再绑路径。
    /// 纯增强，任何失败只记日志；下载到一张就刷新一行。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task FillCoversAsync(CancellationToken cancellationToken)
    {
        var pending = Mods.Where(static row => !string.IsNullOrWhiteSpace(row.Summary.CoverUrl)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var root = AppServices.OnlineModCoverRoot;
        try
        {
            AppServices.FileSystem.EnsureDirectory(root);

            foreach (var row in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = AppServices.FileSystem.Combine(root, row.ModId);
                var local = await _assets
                    .DownloadAsync(row.Summary.CoverUrl!, target, cancellationToken)
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
            _logger.Warn("PDX Mods 封面下载失败：" + ex.Message);
        }
    }

    /// <summary>把失败结果翻译成状态栏里显示的短文案（错误码原样带上便于排障）。</summary>
    /// <param name="result">失败结果。</param>
    private static string DescribeFailure<TValue>(AppResult<TValue> result) => result.Kind switch
    {
        ErrorKind.Unauthorized => T(Keys.ModlibOnlineStatusNeedSignIn),
        _ => result.ErrorMessage ?? "pdx-mods",
    };

    /// <summary>搜索词变化：回到第 1 页重查。</summary>
    partial void OnSearchTextChanged(string value)
    {
        _page = 0;
        if (IsLoaded || HasGame)
        {
            _ = QueryAsync(CancellationToken.None);
        }
    }

    /// <summary>排序方式变化：回到第 1 页重查。</summary>
    partial void OnSelectedSortModeChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        _page = 0;
        if (IsLoaded || HasGame)
        {
            _ = QueryAsync(CancellationToken.None);
        }
    }

    /// <summary>语言切换：重建排序选项并保持当前排序方式。</summary>
    protected override void Relocalize()
    {
        var sort = ResolveSort();
        SortModes = BuildSortModes();

        _isRelocalizing = true;
        try
        {
            SelectedSortMode = T(SortKeyOf(sort));
        }
        finally
        {
            _isRelocalizing = false;
        }

        OnPropertyChanged(nameof(SelectedSortMode));
        StatusMessage = BuildCurrentStatusMessage();
    }

    /// <summary>按当前语言生成排序下拉框的选项。</summary>
    private static IReadOnlyList<string> BuildSortModes() => SortKeys.Select(static key => T(key)).ToArray();

    /// <summary>当前选中项对应的排序枚举。</summary>
    private PdxModsSort ResolveSort()
    {
        foreach (var key in SortKeys)
        {
            if (string.Equals(T(key), SelectedSortMode, StringComparison.Ordinal))
            {
                return SortOf(key);
            }
        }

        return PdxModsSort.Best;
    }

    /// <summary>排序枚举 → 文案键。</summary>
    /// <param name="sort">排序方式。</param>
    private static string SortKeyOf(PdxModsSort sort) => sort switch
    {
        PdxModsSort.Popularity => Keys.ModlibOnlineSortPopularity,
        PdxModsSort.Rating => Keys.ModlibOnlineSortRating,
        PdxModsSort.Updated => Keys.ModlibOnlineSortUpdated,
        PdxModsSort.Created => Keys.ModlibOnlineSortCreated,
        PdxModsSort.DisplayName => Keys.ModlibOnlineSortName,
        _ => Keys.ModlibOnlineSortBest,
    };

    /// <summary>文案键 → 排序枚举。</summary>
    /// <param name="key">文案键。</param>
    private static PdxModsSort SortOf(string key) => key switch
    {
        Keys.ModlibOnlineSortPopularity => PdxModsSort.Popularity,
        Keys.ModlibOnlineSortRating => PdxModsSort.Rating,
        Keys.ModlibOnlineSortUpdated => PdxModsSort.Updated,
        Keys.ModlibOnlineSortCreated => PdxModsSort.Created,
        Keys.ModlibOnlineSortName => PdxModsSort.DisplayName,
        _ => PdxModsSort.Best,
    };

    /// <summary>语言切换后按当前状态重算状态栏（不重发请求，避免切语言就联网）。</summary>
    private string BuildCurrentStatusMessage()
    {
        if (!HasGame)
        {
            return T(Keys.ModlibOnlineNoGameHint);
        }

        if (!IsLoaded)
        {
            return string.Empty;
        }

        if (Mods.Count == 0)
        {
            return T(Keys.ModlibOnlineStatusEmpty, SearchText.Trim());
        }

        return T(Keys.ModlibOnlineStatusLoaded, _totalCount);
    }

    /// <summary>派生属性没有 setter，统一在数据变化后手工通知。</summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(ShowNoResultsHint));
        OnPropertyChanged(nameof(HasPrevPage));
        OnPropertyChanged(nameof(HasNextPage));
        OnPropertyChanged(nameof(PageText));
        OnPropertyChanged(nameof(TotalPages));
    }
}
