namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 库页的视图模型：把当前游戏的全部 mod（mods 表 + 磁盘本地 mod）整理成行，
/// 并负责搜索过滤、排序与状态栏文案。本类不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed partial class ModLibraryViewModel : LocalizedObservableObject
{
    /// <summary>
    /// 排序方式的键（默认名称 A-Z）。下拉框里显示的是这些键的本地化文案，
    /// 选中项存的也是文案，所以切换语言时要按键把选中项换到新语言的文案上。
    /// </summary>
    private static readonly string[] SortKeys =
    [
        Keys.ModlibSortByName,
        Keys.ModlibSortBySize,
        Keys.ModlibSortBySubscribed,
    ];

    /// <summary>
    /// 「来源」筛选的键：全部 / 本地 / 订阅。与排序一样，下拉框显示文案、选中项也存文案。
    /// </summary>
    private static readonly string[] SourceFilterKeys =
    [
        Keys.ModlibSourceFilterAll,
        Keys.ModlibSourceFilterLocal,
        Keys.ModlibSourceFilterSubscribed,
    ];

    private readonly ModLibraryService _service;
    private readonly IModThumbnailService _thumbnails;
    private readonly IAppLogger _logger;

    /// <summary>最近一次加载的快照；搜索与排序只裁剪展示，不动这份完整数据。</summary>
    private ModLibrarySnapshot _snapshot = ModLibrarySnapshot.Empty;

    /// <summary>当前游戏的内容由游戏本体管理（仅启动等级）：此时不显示"还没有 mod"空状态。</summary>
    private bool _managedByGame;

    /// <summary>语言切换时重建排序下拉框：此时改选中项不算用户操作，不重建列表。</summary>
    private bool _isRelocalizing;

    /// <summary>当前游戏；null 表示还没在游戏库里选游戏。</summary>
    [ObservableProperty]
    private GameInstallation? _game;

    /// <summary>是否正在加载。</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>是否已完成过一次加载（未完成时不弹空状态，避免闪一下提示）。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>搜索词：名称 / 注册 ID / 目录，输入即过滤。</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>当前排序方式，取值来自 <see cref="SortModes"/>。</summary>
    [ObservableProperty]
    private string _selectedSortMode = string.Empty;

    /// <summary>排序下拉框的选项（按当前语言生成，语言切换时整体替换）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sortModes = [];

    /// <summary>来源筛选的选项（全部 / 本地 / 订阅）。</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _sourceFilterModes = [];

    /// <summary>当前来源筛选，取值来自 <see cref="SourceFilterModes"/>。</summary>
    [ObservableProperty]
    private string _selectedSourceFilter = string.Empty;

    public ModLibraryViewModel(ModLibraryService service, IModThumbnailService thumbnails, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(thumbnails);
        ArgumentNullException.ThrowIfNull(logger);

        _service = service;
        _thumbnails = thumbnails;
        _logger = logger;

        SortModes = BuildSortModes();
        _selectedSortMode = SortModes[0];
        SourceFilterModes = BuildSourceFilterModes();
        _selectedSourceFilter = SourceFilterModes[0];
    }

    /// <summary>当前可见的 mod 行（已按搜索词过滤、按排序方式排序）。</summary>
    public ObservableCollection<ModLibraryRowViewModel> Mods { get; } = new();

    /// <summary>当前游戏名（未选中游戏时为占位文案）。</summary>
    public string GameTitle => Game?.DisplayName ?? T(Keys.ModlibNoGameTitle);

    /// <summary>是否已选中游戏。</summary>
    public bool HasGame => Game is not null;

    /// <summary>当前可见行是否为空（搜索无结果时也为 true）。</summary>
    public bool IsEmpty => Mods.Count == 0;

    /// <summary>工具条上的计数文案（未选中游戏时为空串，XAML 侧据此隐藏）。</summary>
    public string CountText => HasGame ? T(Keys.ModlibCountText, _snapshot.Mods.Count) : string.Empty;

    /// <summary>空状态：还没在游戏库里选游戏。</summary>
    public bool ShowNoGameHint => IsLoaded && !HasGame;

    /// <summary>空状态：已选游戏，但该游戏一个 mod 都没有（内容由游戏本体管理时不算）。</summary>
    public bool ShowNoModsHint => IsLoaded && HasGame && !_managedByGame && _snapshot.Mods.Count == 0;

    /// <summary>是否需要显示空状态面板。</summary>
    public bool ShowEmptyState => ShowNoGameHint || ShowNoModsHint;

    /// <summary>加载 Mod 库：读快照 → 应用搜索 / 排序 → 刷新计数与状态栏。</summary>
    /// <param name="game">当前游戏；null 表示还没在游戏库里选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        _snapshot = ModLibrarySnapshot.Empty;
        _managedByGame = game?.SupportsContentManagement == false;

        if (game is null)
        {
            Mods.Clear();
            IsLoaded = true;
            StatusMessage = T(Keys.ModlibNoGameHint);
            RaiseDerived();
            return;
        }

        if (_managedByGame)
        {
            // 仅启动的游戏：Mod 库归游戏本体，这里只显示边界说明。
            Mods.Clear();
            IsLoaded = true;
            StatusMessage = SupportMessages.ManagedByGame;
            RaiseDerived();
            return;
        }

        IsBusy = true;
        try
        {
            _snapshot = await _service.LoadAsync(game, cancellationToken).ConfigureAwait(true);

            // DDS 缩略图先转成 PNG，行里才是真实封面而不是占位图。
            await AppServices.ImageArt
                .PrewarmAsync(_snapshot.Mods.Select(static m => m.ThumbnailPath), cancellationToken)
                .ConfigureAwait(true);

            RebuildRows();
            IsLoaded = true;

            // 列表先按本地图片显示完整一屏，联网补图在后台继续（不占用 IsBusy）。
            _ = FillThumbnailsAsync(game, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _snapshot = ModLibrarySnapshot.Empty;
            Mods.Clear();
            IsLoaded = true;
            StatusMessage = T(Keys.ModlibLoadFailed, ex.Message);
            _logger.Error("Mod 库页加载失败（gameId=" + game.GameId + "）", ex);
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>
    /// 联网补全缩略图：只处理「本地解析不到图、但库里有 thumbnailUrl」的行。
    /// 纯增强，任何失败都只记日志，不影响列表本身；补到图后就地刷新对应行。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private async Task FillThumbnailsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        var pending = _snapshot.Mods
            .Where(static entry => string.IsNullOrWhiteSpace(entry.ThumbnailPath)
                && !string.IsNullOrWhiteSpace(entry.ThumbnailUrl))
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var settings = AppServices.Settings;
        try
        {
            var result = await _thumbnails.FillAsync(
                pending,
                settings.OnlineModThumbnailEnabled,
                settings.ModThumbnailCacheDays,
                cancellationToken).ConfigureAwait(true);

            if (result.Updates.Count == 0)
            {
                return;
            }

            var byModId = new Dictionary<Guid, string>(result.Updates.Count);
            foreach (var update in result.Updates)
            {
                byModId[update.ModId] = update.ThumbnailPath;
            }

            foreach (var row in Mods)
            {
                if (string.IsNullOrWhiteSpace(row.ThumbnailPath)
                    && byModId.TryGetValue(row.ModId, out var thumbnailPath))
                {
                    row.ThumbnailPath = thumbnailPath;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已卸载：补图任务随令牌作废，不需要提示。
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库缩略图联网补全失败（gameId=" + game.GameId + "）：" + ex.Message);
        }
    }

    /// <summary>刷新：按当前游戏重新加载一次。</summary>
    public Task RefreshAsync() => LoadAsync(Game, CancellationToken.None);

    /// <summary>
    /// 覆盖状态栏文案。页面上的行操作（打开文件夹 / 打开创意工坊）不在本类里执行，
    /// 但结果需要反馈到同一条状态栏，所以留一个入口给 code-behind。
    /// </summary>
    /// <param name="message">要显示的文案。</param>
    public void SetStatus(string message) => StatusMessage = message ?? string.Empty;

    /// <summary>
    /// 语言切换：下拉框的显示文案同时就是选中项的值，要按新语言重建选项并把选中项换过去；
    /// mod 行自己会随属性通知刷新，所以这里不重建 <see cref="Mods"/>（保留滚动位置）。
    /// </summary>
    protected override void Relocalize()
    {
        var sortKey = ResolveSortKey();
        var sourceFilterKey = ResolveSourceFilterKey();
        SortModes = BuildSortModes();
        SourceFilterModes = BuildSourceFilterModes();

        _isRelocalizing = true;
        try
        {
            SelectedSortMode = T(sortKey);
            SelectedSourceFilter = T(sourceFilterKey);
        }
        finally
        {
            _isRelocalizing = false;
        }

        // 新旧语言的文案可能恰好相同，那样上面的赋值不会变更，仍需通知一次让下拉框重新选中。
        OnPropertyChanged(nameof(SelectedSortMode));
        OnPropertyChanged(nameof(SelectedSourceFilter));

        // 状态栏平时存的是已拼好的文案（行操作还会临时覆盖成动作结果），切语言后按状态重算。
        StatusMessage = BuildCurrentStatusMessage();
    }

    /// <summary>搜索词变化：重新过滤（不重新读库，只按快照重建可见行）。</summary>
    partial void OnSearchTextChanged(string value)
    {
        RebuildRows();
    }

    /// <summary>排序方式变化：重新排序。</summary>
    partial void OnSelectedSortModeChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        RebuildRows();
    }

    /// <summary>来源筛选变化：只需重建行，不用重新加载。</summary>
    partial void OnSelectedSourceFilterChanged(string value)
    {
        if (_isRelocalizing)
        {
            return;
        }

        RebuildRows();
    }

    /// <summary>按当前语言生成排序下拉框的选项。</summary>
    private static IReadOnlyList<string> BuildSortModes() => SortKeys.Select(static key => T(key)).ToArray();

    /// <summary>按当前语言生成来源筛选的选项。</summary>
    private static IReadOnlyList<string> BuildSourceFilterModes() =>
        SourceFilterKeys.Select(static key => T(key)).ToArray();

    /// <summary>选中项对应的来源筛选键（对不上时回落到「全部」）。</summary>
    private string ResolveSourceFilterKey()
    {
        foreach (var key in SourceFilterKeys)
        {
            if (string.Equals(T(key), SelectedSourceFilter, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return SourceFilterKeys[0];
    }

    /// <summary>按来源筛选键过滤。</summary>
    private bool MatchesOrigin(ModLibraryEntry entry)
    {
        var key = ResolveSourceFilterKey();
        if (string.Equals(key, Keys.ModlibSourceFilterLocal, StringComparison.Ordinal))
        {
            return entry.Origin == ModOrigin.Local;
        }

        if (string.Equals(key, Keys.ModlibSourceFilterSubscribed, StringComparison.Ordinal))
        {
            return entry.Origin == ModOrigin.Subscribed;
        }

        return true;
    }

    /// <summary>
    /// 选中项对应的排序方式键。选中项可能是语言切换前的旧文案，
    /// 对不上任何键时回落到默认排序（名称 A-Z）。
    /// </summary>
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

    /// <summary>按当前状态重算状态栏文案（不含行操作的临时提示）。</summary>
    private string BuildCurrentStatusMessage()
    {
        if (!HasGame)
        {
            return T(Keys.ModlibNoGameHint);
        }

        if (_managedByGame)
        {
            return SupportMessages.ManagedByGame;
        }

        return BuildStatusMessage(SearchText.Trim());
    }

    /// <summary>按当前搜索词与排序方式重建可见行，并更新状态栏文案。</summary>
    private void RebuildRows()
    {
        if (!HasGame)
        {
            Mods.Clear();
            RaiseDerived();
            return;
        }

        var query = SearchText.Trim();
        IReadOnlyList<ModLibraryEntry> filtered = query.Length == 0
            ? _snapshot.Mods.Where(MatchesOrigin).ToList()
            : _snapshot.Mods.Where(entry => MatchesSearch(entry, query) && MatchesOrigin(entry)).ToList();

        // 行 VM 需要游戏版本判断 requiredVersion 是否过旧；Game 在此路径上必然非 null。
        var gameVersion = Game?.Version;
        Mods.Clear();
        foreach (var entry in ApplySort(filtered))
        {
            Mods.Add(new ModLibraryRowViewModel(
                entry,
                gameVersion,
                ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize)));
        }

        StatusMessage = BuildStatusMessage(query);
        RaiseDerived();
    }

    /// <summary>
    /// 为「打开详情页」按钮建一条工坊详情行：把这一行的 (平台, 编号) 交给
    /// <see cref="WorkshopRowViewModel.FromLibrary"/>，元数据尽量从工坊信息缓存里补。
    /// <para>
    /// 缓存命中时副标题就有作者/评分/订阅数；没命中也不影响 —— 详情页会自己做一次真实请求，
    /// 拿到正文、作者显示名和评分后再填。编号不可用时返回 null（按钮本身已置灰，这里是第二道闸）。
    /// </para>
    /// </summary>
    /// <param name="row">Mod 库里的某一行。</param>
    public WorkshopRowViewModel? CreateDetailRow(ModLibraryRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var reference = row.WorkshopRef;
        if (!reference.CanOpen)
        {
            return null;
        }

        WorkshopInfoCacheEntry? cached = null;
        if (AppServices.WorkshopInfo.TryGet(WorkshopInfoCache.Key(reference.Source, reference.Id), out var entry))
        {
            cached = entry;
        }

        return WorkshopRowViewModel.FromLibrary(
            reference,
            row.Name,
            row.Entry.SizeBytes,
            row.ThumbnailPath,
            cached,
            ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize));
    }

    /// <summary>搜索匹配：名称 / 游戏注册 ID / mod 目录，忽略大小写。</summary>
    private static bool MatchesSearch(ModLibraryEntry entry, string query) =>
        entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.GameRegistryId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || (entry.DirPath is not null
            && entry.DirPath.Contains(query, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 排序：名称 A-Z（默认，忽略大小写）；大小降序与订阅时间降序时未知值排最后，
    /// 名称始终作为次级键，保证顺序稳定。
    /// </summary>
    private IEnumerable<ModLibraryEntry> ApplySort(IReadOnlyList<ModLibraryEntry> rows) =>
        ResolveSortKey() switch
        {
            Keys.ModlibSortBySize => rows
                .OrderByDescending(static entry => entry.SizeBytes.HasValue)
                .ThenByDescending(static entry => entry.SizeBytes ?? long.MinValue)
                .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.ModId),
            Keys.ModlibSortBySubscribed => rows
                .OrderByDescending(static entry => entry.SubscribedOn.HasValue)
                .ThenByDescending(static entry => entry.SubscribedOn ?? DateTimeOffset.MinValue)
                .ThenBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.ModId),
            _ => rows
                .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static entry => entry.ModId),
        };

    /// <summary>状态栏文案：加载结果 + 搜索命中情况。</summary>
    private string BuildStatusMessage(string query)
    {
        var total = _snapshot.Mods.Count;
        if (query.Length == 0)
        {
            return T(Keys.ModlibStatusLoaded, total);
        }

        return Mods.Count == 0
            ? T(Keys.ModlibStatusNoMatch, query)
            : T(Keys.ModlibStatusFiltered, Mods.Count, total);
    }

    /// <summary>派生属性没有 setter，统一在数据变化后手工通知。</summary>
    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(GameTitle));
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowNoGameHint));
        OnPropertyChanged(nameof(ShowNoModsHint));
        OnPropertyChanged(nameof(ShowEmptyState));
    }
}
