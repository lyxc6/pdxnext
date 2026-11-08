namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;

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

    /// <summary>名称翻译去抖时长：搜索词连续变化时合并成一批，避免每敲一个字都起一次引擎请求。</summary>
    private static readonly TimeSpan NameTranslationDebounce = TimeSpan.FromMilliseconds(250);

    private readonly ModLibraryService _service;
    private readonly IModThumbnailService _thumbnails;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 本页已知的名称译名：按 mod 主键存原文、目标语言与译文。
    /// 搜索 / 排序 / 来源筛选只重建行，译文直接从这份字典回填，不重复请求翻译。
    /// </summary>
    private readonly Dictionary<Guid, TranslationCache.NameTranslation> _nameTranslations = new();

    /// <summary>本轮名称翻译的代次；重建行时自增，迟到的旧批次不再回填。</summary>
    private int _translationVersion;

    /// <summary>名称翻译去抖令牌：新批次接管时取消上一批，避免批次越积越多。</summary>
    private CancellationTokenSource? _translationDebounce;

    /// <summary>名称翻译进度回调来自线程池；回填行属性前切回创建本 VM 的 UI 线程。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    /// <summary>页面生命周期令牌（由 <see cref="LoadAsync"/> 记录）；刷新与重建行都沿用它。</summary>
    private CancellationToken _pageToken = CancellationToken.None;

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

    /// <summary>是否正在跑一次 Steam 写操作（触发下载 / 本地禁用）；期间按钮置灰。</summary>
    [ObservableProperty]
    private bool _isSteamActionBusy;

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

    /// <summary>空状态：还没在游戏库里选游戏。</summary>
    public bool ShowNoGameHint => IsLoaded && !HasGame;

    /// <summary>空状态：已选游戏，但该游戏一个 mod 都没有（内容由游戏本体管理时不算）。</summary>
    public bool ShowNoModsHint => IsLoaded && HasGame && !_managedByGame && _snapshot.Mods.Count == 0;

    /// <summary>是否需要显示空状态面板。</summary>
    public bool ShowEmptyState => ShowNoGameHint || ShowNoModsHint;

    /// <summary>
    /// 当前游戏能不能读 Steam 工坊状态（决定是否显示「刷新 Steam 状态」与行内写操作按钮）。
    /// Steam 不在、没直连条件、或用户在设置里关掉了状态读取时为 false。
    /// </summary>
    public bool HasSteamStates => HasGame && _service.HasSteamStates(Game);

    /// <summary>行内 Steam 写操作是否可用（读得到状态 + 用户打开了写开关）。</summary>
    public bool CanWriteSteam =>
        HasSteamStates && AppServices.Settings.SteamWorkshopSubscribeEnabled && !IsSteamActionBusy;

    /// <summary>加载 Mod 库：读快照 → 应用搜索 / 排序 → 刷新计数与状态栏。</summary>
    /// <param name="game">当前游戏；null 表示还没在游戏库里选游戏。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task LoadAsync(
        GameInstallation? game,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        // 刷新、搜索与 Steam 写操作之后的重载都沿用这个令牌，页面关闭后不再继续翻译。
        _pageToken = cancellationToken;

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
            _snapshot = await _service.LoadAsync(game, cancellationToken, forceRefresh).ConfigureAwait(true);

            // 名称译名与引擎/缓存天数无关，先把持久化的旧译名读回来：
            // 重建行时直接显示，不用在每次进入 Mod 库时重新等一遍翻译。
            await LoadCachedNameTranslationsAsync(cancellationToken).ConfigureAwait(true);

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

    /// <summary>刷新：丢掉 mod 扫描缓存后按当前游戏重新加载一次（沿用页面生命周期令牌）。</summary>
    public Task RefreshAsync() => LoadAsync(Game, _pageToken, forceRefresh: true);

    /// <summary>
    /// 重新问一次 Steam 的工坊状态（丢掉缓存后重载）。
    /// 用户点「刷新 Steam 状态」或在 Steam 里改过订阅后调用。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task RefreshSteamStatesAsync()
    {
        if (Game is null)
        {
            return;
        }

        if (WorkshopSourceChooser.ResolveAppId(Game.SteamAppId, Game.GameId) is { } appId and > 0)
        {
            AppServices.WorkshopStates.Invalidate(appId);
        }

        await LoadAsync(Game, _pageToken).ConfigureAwait(true);
    }

    /// <summary>
    /// 让 Steam 立刻下载这一条。写操作受「Steam 工坊直连订阅」开关约束（默认关）。
    /// </summary>
    /// <param name="row">要下载的行。</param>
    /// <returns>异步任务。</returns>
    public async Task TriggerSteamDownloadAsync(ModLibraryRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!AppServices.Settings.SteamWorkshopSubscribeEnabled)
        {
            SetStatus(T(Keys.ModlibSteamWriteDisabled));
            return;
        }

        if (row.WorkshopId is not { Length: > 0 } workshopId || !ulong.TryParse(workshopId, out var fileId))
        {
            return;
        }

        await RunSteamActionAsync(
            fileId,
            static (source, id, ct) => source.TriggerDownloadAsync(id, ct),
            () => T(Keys.ModlibSteamDownloadRequested, row.Name)).ConfigureAwait(true);
    }

    /// <summary>
    /// 在 Steam 里把这一条设为本地禁用 / 解除禁用（订阅保留）。
    /// 写操作同样受「Steam 工坊直连订阅」开关约束。
    /// </summary>
    /// <param name="row">目标行。</param>
    /// <returns>异步任务。</returns>
    public async Task SetSteamDisabledLocallyAsync(ModLibraryRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!AppServices.Settings.SteamWorkshopSubscribeEnabled)
        {
            SetStatus(T(Keys.ModlibSteamWriteDisabled));
            return;
        }

        if (row.WorkshopId is not { Length: > 0 } workshopId || !ulong.TryParse(workshopId, out var fileId))
        {
            return;
        }

        var disable = !row.IsSteamDisabledLocally;
        await RunSteamActionAsync(
            fileId,
            (source, id, ct) => source.SetDisabledLocallyAsync(id, disable, ct),
            null).ConfigureAwait(true);
    }

    /// <summary>
    /// 跑一次 Steam 写操作：置忙 → 调后端 → 丢缓存 → 重载（徽标跟着变）。
    /// 失败只落状态栏，不抛给界面。
    /// </summary>
    private async Task RunSteamActionAsync(
        ulong publishedFileId,
        Func<IWorkshopStateSource, ulong, CancellationToken, Task<WorkshopBatchItem>> action,
        Func<string>? successText)
    {
        var source = _service.ResolveStateSource(Game);
        if (source is null)
        {
            SetStatus(T(Keys.ModlibSteamWriteDisabled));
            return;
        }

        IsSteamActionBusy = true;
        try
        {
            var item = await action(source, publishedFileId, CancellationToken.None).ConfigureAwait(true);
            if (!item.Ok)
            {
                SetStatus(T(Keys.ModlibSteamActionFailed, item.Error ?? T(Keys.CommonUnknown)));
                return;
            }

            if (WorkshopSourceChooser.ResolveAppId(Game?.SteamAppId, Game?.GameId) is { } appId and > 0)
            {
                AppServices.WorkshopStates.Invalidate(appId);
            }

            if (successText is not null)
            {
                SetStatus(successText());
            }

            await LoadAsync(Game, _pageToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库：Steam 写操作失败（id=" + publishedFileId + "）：" + ex.Message);
            SetStatus(T(Keys.ModlibSteamActionFailed, ex.Message));
        }
        finally
        {
            IsSteamActionBusy = false;
        }
    }

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
        // 新一批行取代旧行：先作废旧译名批次，避免标题栏一直被旧批次占着「正在翻译」。
        _translationVersion++;
        CancelPendingNameTranslation();

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
        var targetLanguage = GetNameTranslationTargetLanguage();
        Mods.Clear();
        foreach (var entry in ApplySort(filtered))
        {
            var row = new ModLibraryRowViewModel(
                entry,
                gameVersion,
                ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize));

            // 已经译过的名称直接回填：搜索 / 排序 / 筛选重建行时不再等一轮翻译。
            if (targetLanguage is not null
                && _nameTranslations.TryGetValue(entry.ModId, out var cached)
                && string.Equals(cached.TargetLanguage, targetLanguage, StringComparison.Ordinal)
                && string.Equals(cached.SourceName, entry.Name, StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(cached.Translation))
            {
                row.TranslatedName = cached.Translation;
            }

            Mods.Add(row);
        }

        StatusMessage = BuildStatusMessage(query);
        RaiseDerived();

        StartNameTranslation();
    }

    /// <summary>
    /// 后台给可见行补译名：翻译是网络操作，不能挡着列表显示。
    /// 只翻译还没有译名的行；连续重建时先取消上一批，250ms 内的多次变化合并成一批。
    /// </summary>
    private void StartNameTranslation()
    {
        var targetLanguage = GetNameTranslationTargetLanguage();
        if (targetLanguage is null)
        {
            return;
        }

        var pending = Mods
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name)
                && string.IsNullOrWhiteSpace(row.TranslatedName))
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        CancelPendingNameTranslation();
        _dispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        var version = _translationVersion;
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(_pageToken);
        _translationDebounce = debounce;
        _ = TranslateNamesAfterDebounceAsync(pending, targetLanguage, version, debounce);
    }

    /// <summary>去抖到点后翻这一批；令牌被新批次取消时什么都不做。</summary>
    private async Task TranslateNamesAfterDebounceAsync(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        string targetLanguage,
        int version,
        CancellationTokenSource debounce)
    {
        try
        {
            await Task.Delay(NameTranslationDebounce, debounce.Token).ConfigureAwait(true);
            await TranslateNamesAsync(rows, targetLanguage, version, debounce.Token).ConfigureAwait(true);
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

    /// <summary>取消还没到点/没跑完的名称翻译批次。</summary>
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

    /// <summary>
    /// 整页名称一次性批翻（引擎内部并发）；命中缓存或引擎出结果时立刻回填对应行，
    /// 不再让一页已缓存的名字等未命中的名字跑完整个引擎回落。
    /// </summary>
    private async Task TranslateNamesAsync(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        string targetLanguage,
        int version,
        CancellationToken cancellationToken)
    {
        try
        {
            var names = rows.Select(static row => row.Name).ToList();
            var translated = await AppServices.Translation
                .TranslateBatchAsync(
                    names,
                    cancellationToken,
                    (index, translatedName) => ApplyTranslatedName(
                        rows,
                        index,
                        translatedName,
                        targetLanguage,
                        version))
                .ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested || version != _translationVersion)
            {
                return;
            }

            for (var i = 0; i < rows.Count && i < translated.Count; i++)
            {
                var translatedName = translated[i];
                if (string.IsNullOrWhiteSpace(translatedName))
                {
                    continue;
                }

                // 进度回调通常已经回填；这里兜底处理回调被丢弃的情况。
                ApplyTranslatedName(rows, i, translatedName, targetLanguage, version);
            }

            // 以行上的实际结果为准：设置变更导致整批返回空时，缓存命中的那部分仍算已译出。
            var anyTranslated = rows.Any(static row => !string.IsNullOrWhiteSpace(row.TranslatedName));
            if (!anyTranslated)
            {
                // 这一页没有状态条，失败只落日志；原因带上引擎与错误，便于排查。
                _logger.Warn("Mod 库名称全部未翻译（" + rows.Count + " 条）："
                    + (AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown)));
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库名称翻译失败：" + ex.Message);
        }
        finally
        {
            // 页面关闭或批次被新搜索取代时，已经回填到行的译名仍然有效：
            // 落盘后下次进入 Mod 库可以直接显示，不必重翻这一批。
            await SaveNameTranslationsAsync(rows, targetLanguage).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 把这一批新拿到的译名按 ModId 持久化，下次进入 Mod 库或换引擎模式时直接复用。
    /// </summary>
    private async Task SaveNameTranslationsAsync(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        string targetLanguage)
    {
        var updates = new List<TranslationCache.NameTranslation>(rows.Count);
        foreach (var row in rows)
        {
            var translatedName = row.TranslatedName;
            if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(translatedName))
            {
                continue;
            }

            updates.Add(new TranslationCache.NameTranslation(
                row.ModId,
                row.Name,
                targetLanguage,
                translatedName));
        }

        if (updates.Count == 0)
        {
            return;
        }

        try
        {
            await AppServices.Translation
                .SaveNameTranslationsAsync(targetLanguage, updates, CancellationToken.None)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库名称译名持久化失败：" + ex.Message);
        }
    }

    /// <summary>
    /// 读回持久化的名称译名（按当前目标语言过滤）；读不到不影响后续正常翻译。
    /// 磁盘读取丢线程池，避免首次进入 Mod 库时在 UI 线程上同步读缓存文档。
    /// </summary>
    private async Task LoadCachedNameTranslationsAsync(CancellationToken cancellationToken)
    {
        _nameTranslations.Clear();
        var targetLanguage = GetNameTranslationTargetLanguage();
        if (targetLanguage is null)
        {
            return;
        }

        try
        {
            // 用 Func<Task<…>> 明确选中 Task.Run 的异步重载，别把内层 Task 当成结果。
            Func<Task<IReadOnlyDictionary<Guid, TranslationCache.NameTranslation>>> load =
                () => AppServices.Translation.LoadNameTranslationsAsync(targetLanguage, cancellationToken);
            var cached = await Task.Run(load, cancellationToken).ConfigureAwait(true);
            foreach (var pair in cached)
            {
                _nameTranslations[pair.Key] = pair.Value;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库名称译名缓存读取失败：" + ex.Message);
        }
    }

    /// <summary>当前该用的名称翻译目标语言；名称翻译或翻译总开关关闭时返回 null。</summary>
    private static string? GetNameTranslationTargetLanguage()
    {
        if (!AppServices.Settings.TranslateNamesEnabled)
        {
            return null;
        }

        var options = AppServices.Translation.CurrentOptions;
        return options.Enabled ? options.TargetLanguage : null;
    }

    /// <summary>
    /// 把一条译文回填到对应行；回调可能来自线程池，切回 UI 线程后再改行属性。
    /// 旧批次的迟到回调按代次丢弃，免得把已经换掉的行写乱。
    /// </summary>
    private void ApplyTranslatedName(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        int index,
        string? translatedName,
        string targetLanguage,
        int version)
    {
        if (string.IsNullOrWhiteSpace(translatedName) || version != _translationVersion)
        {
            return;
        }

        if (_dispatcher is null || _dispatcher.HasThreadAccess)
        {
            ApplyTranslatedNameOnUi(rows, index, translatedName, targetLanguage, version);
            return;
        }

        _dispatcher.TryEnqueue(
            () => ApplyTranslatedNameOnUi(rows, index, translatedName, targetLanguage, version));
    }

    /// <summary>在 UI 线程上写行与名称译名字典。</summary>
    private void ApplyTranslatedNameOnUi(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        int index,
        string translatedName,
        string targetLanguage,
        int version)
    {
        if (version != _translationVersion || index < 0 || index >= rows.Count)
        {
            return;
        }

        var row = rows[index];
        if (string.IsNullOrWhiteSpace(row.Name))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(row.TranslatedName))
        {
            row.TranslatedName = translatedName;
        }

        // 即使这一行已经被筛掉也记下来：下次搜索 / 排序重建时可以直接复用。
        _nameTranslations[row.ModId] = new TranslationCache.NameTranslation(
            row.ModId,
            row.Name,
            targetLanguage,
            translatedName);
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
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowNoGameHint));
        OnPropertyChanged(nameof(ShowNoModsHint));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(HasSteamStates));
        OnPropertyChanged(nameof(CanWriteSteam));
    }

    partial void OnIsSteamActionBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanWriteSteam));
    }
}
