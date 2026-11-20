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
    /// 本页用的名称译名管道：与播放集页、添加模组页共用同一个实现，也就共用同一张
    /// <c>TranslationCache.Names</c> 表——三处对同一个 mod 显示的一定是同一句译文。
    /// </summary>
    private readonly ModNameTranslator _nameTranslator;

    /// <summary>
    /// 名称翻译批次的代次：每次起新批次（重建行 / 重试 / 强制刷新）都自增，
    /// 被取代的旧批次的回填与收尾守卫都按它判定过期。
    /// </summary>
    private int _translationVersion;

    /// <summary>
    /// 「刷新译文」的强制批次是否在途：期间重建行（搜索 / 排序 / 换筛选都会整批换新行实例）
    /// 也按强制重翻，不让旧缓存把刚要求重翻的旧译文原样填回新行。
    /// </summary>
    private bool _nameRefreshInFlight;

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

    /// <summary>工坊转本地是否在途；期间右键菜单可据此防重入。</summary>
    [ObservableProperty]
    private bool _isConvertToLocalBusy;

    /// <summary>是否已完成过一次加载（未完成时不弹空状态，避免闪一下提示）。</summary>
    [ObservableProperty]
    private bool _isLoaded;

    /// <summary>状态栏文案。</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 行右键菜单里的「重试翻译」是否可用：本轮译名一条都没译出来时为 true。
    /// 重建行（搜索 / 排序 / 换筛选）不会清掉它，只有真的译出结果或重新加载才会。
    /// </summary>
    [ObservableProperty]
    private bool _canRetryTranslation;

    /// <summary>上一次译名整批失败的原因（拼状态栏文案用）；成功后清空。</summary>
    private string _translationFailureReason = string.Empty;

    /// <summary>失败提示当前确实写进状态栏的那串文字（用来判断它有没有被更新的提示覆盖）。</summary>
    private string _translationFailureStatusText = string.Empty;

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

        _nameTranslator = new ModNameTranslator(
            GetNameTranslationTargetLanguage,
            load: (language, token) => AppServices.Translation.LoadNameTranslationsAsync(language, token),
            save: (language, updates, token) =>
                AppServices.Translation.SaveNameTranslationsAsync(language, updates, token));
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

        // 新一轮加载重新给一次机会：上一轮的重试提示作废。
        CanRetryTranslation = false;
        _translationFailureReason = string.Empty;

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
    /// 右键菜单「转为本地 mod」：把订阅且有本地内容的 mod 复制成同内容的本地 mod。
    /// 成功只在状态栏反馈（静默），失败写入状态栏并把异常抛给页面弹 ShowMessage；
    /// 在途期间置 <see cref="IsConvertToLocalBusy"/> 防重入。
    /// </summary>
    /// <param name="row">被转换的 Mod 库行。</param>
    /// <param name="cancellationToken">本次转换的取消令牌（页面用它链接生命周期令牌并暴露取消按钮）。</param>
    public async Task ConvertToLocalAsync(ModLibraryRowViewModel row, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(row);
        var game = Game;
        if (game is null)
        {
            throw new InvalidOperationException(T(Keys.ModsGameNone));
        }

        IsConvertToLocalBusy = true;
        try
        {
            var result = await WorkshopToLocalInvoker.ConvertAsync(
                game, row.ModId, row.Name, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                SetStatus(T(Keys.ConvertToLocalFailedDetail, result.ErrorMessage ?? string.Empty));
                throw new InvalidOperationException(result.ErrorMessage ?? "convert-to-local-failed");
            }

            // 转换改写了来源与路径，重载一次让徽标 / 来源筛选跟上，随后写成功文案（重载会覆盖状态栏）。
            await LoadAsync(game, _pageToken).ConfigureAwait(true);
            SetStatus(T(Keys.ConvertToLocalStatusDone, row.Name));
        }
        finally
        {
            IsConvertToLocalBusy = false;
        }
    }

    /// <summary>
    /// 覆盖状态栏文案。页面上的行操作（打开文件夹 / 打开创意工坊 / 转为本地 mod）不在本类里执行，
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

        // 译名整批失败时，状态栏留的是那条原因（重试入口在行右键菜单里）；切语言后要按新语言重拼，
        // 不能退回「已加载 N 个 mod」把失败提示悄悄抹掉。
        if (CanRetryTranslation && !string.IsNullOrEmpty(_translationFailureReason))
        {
            _translationFailureStatusText = T(Keys.ModsTranslateUnavailable, _translationFailureReason);
            return _translationFailureStatusText;
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
            if (_nameTranslator.TryBackfill(entry.ModId, entry.Name, targetLanguage, out var translated))
            {
                row.TranslatedName = translated;
            }

            Mods.Add(row);
        }

        StatusMessage = CanRetryTranslation && !string.IsNullOrEmpty(_translationFailureStatusText)
            ? _translationFailureStatusText
            : BuildStatusMessage(query);
        RaiseDerived();

        StartNameTranslation();
    }

    /// <summary>
    /// 用户点了 Mod 库行右键菜单里的「重试翻译」：把这一页还没译名的行强制重翻一遍。
    /// <para>
    /// 重试前先收起提示：否则重试期间状态栏还挂着上一次的原因，看不出这次在动。
    /// 带 <c>forceRefresh</c>：失败后旧缓存条目还在，不带 force 会命中旧缓存把旧译文原样填回行
    /// 并按成功路径清掉失败提示，引擎根本不会被问到。
    /// </para>
    /// </summary>
    public void RetryNameTranslation()
    {
        CanRetryTranslation = false;
        _translationFailureReason = string.Empty;
        _translationFailureStatusText = string.Empty;
        StatusMessage = BuildStatusMessage(SearchText.Trim());
        AppServices.Translation.ResetRetryableFailures();
        StartNameTranslation(forceRefresh: true);
    }

    /// <summary>
    /// 用户点了行右键菜单里的「刷新译文」：忽略缓存，当前列表全部名称强制重翻一遍。
    /// <para>
    /// 除了像重试那样收起提示，还要清掉内存译名表与行上的旧译文：
    /// 不清的话「只翻没译名的行」的过滤与重建行时的回填会把旧译文原样填回来。
    /// 强制批次在途期间（<see cref="_nameRefreshInFlight"/>），重建行会以 force 重启批次，
    /// 保证最终显示的是新译文而不是旧缓存。
    /// </para>
    /// </summary>
    public void RefreshNameTranslation()
    {
        CanRetryTranslation = false;
        _translationFailureReason = string.Empty;
        _translationFailureStatusText = string.Empty;
        StatusMessage = BuildStatusMessage(SearchText.Trim());
        AppServices.Translation.ResetRetryableFailures();

        _nameTranslator.Clear();
        foreach (var row in Mods)
        {
            row.TranslatedName = null;
        }

        StartNameTranslation(forceRefresh: true);
    }

    /// <summary>
    /// 后台给可见行补译名：翻译是网络操作，不能挡着列表显示。
    /// 只翻译还没有译名的行；连续重建时先取消上一批，250ms 内的多次变化合并成一批；
    /// 每起新批次都推进 <see cref="_translationVersion"/>，重试 / 强制刷新也不例外。
    /// <para>
    /// 强制刷新在途时（<see cref="_nameRefreshInFlight"/>），这里重启的批次同样强制绕开旧缓存——
    /// 否则旧缓存把刚要求重翻的旧译文原样填回重建出来的新行，强制批次的结果全落在
    /// 已被换掉的旧行上，刷新等于被静默吞掉。
    /// </para>
    /// </summary>
    /// <param name="forceRefresh">为 true 时跳过缓存直接请求引擎（「刷新译文」用），结果照常写回缓存覆盖旧值。</param>
    private void StartNameTranslation(bool forceRefresh = false)
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

        // 强制刷新在途期间重建出来的行同样绕开旧缓存。
        var force = forceRefresh || _nameRefreshInFlight;

        CancelPendingNameTranslation();
        _dispatcher ??= Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

        // 每次起新批次都推进代次（与 Mod 页 / 工坊页一致）：只在重建行时自增的话，
        // 「重试 / 刷新译文」起的新批次与被它取消的旧批次同版，旧批次收尾时会在下面的
        // finally 里把强制批次还挂在途的 _nameRefreshInFlight 抢着复位。
        var version = ++_translationVersion;
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(_pageToken);
        _translationDebounce = debounce;
        if (force)
        {
            _nameRefreshInFlight = true;
        }

        _ = TranslateNamesAfterDebounceAsync(pending, targetLanguage, version, debounce, force);
    }

    /// <summary>去抖到点后翻这一批；令牌被新批次取消时什么都不做。</summary>
    private async Task TranslateNamesAfterDebounceAsync(
        IReadOnlyList<ModLibraryRowViewModel> rows,
        string targetLanguage,
        int version,
        CancellationTokenSource debounce,
        bool forceRefresh = false)
    {
        try
        {
            await Task.Delay(NameTranslationDebounce, debounce.Token).ConfigureAwait(true);
            await TranslateNamesAsync(rows, targetLanguage, version, debounce.Token, forceRefresh)
                .ConfigureAwait(true);
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
        CancellationToken cancellationToken,
        bool forceRefresh = false)
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
                        version),
                    forceRefresh: forceRefresh)
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
                // 这一页原来没有提示位，用户只能盯着没变化的标题怀疑功能坏了；
                // 现在复用「Mod 页」那条文案（不新增 .resw 键）并在旁边给一个重试按钮。
                var reason = AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown);
                _logger.Warn("Mod 库名称全部未翻译（" + rows.Count + " 条）：" + reason);
                _translationFailureReason = reason;
                CanRetryTranslation = true;
                _translationFailureStatusText = T(Keys.ModsTranslateUnavailable, reason);
                StatusMessage = _translationFailureStatusText;
            }
            else
            {
                _translationFailureReason = string.Empty;
                CanRetryTranslation = false;

                // 只在状态栏仍然是那条失败提示时才还回计数文案：否则会盖掉用户刚看到的其它反馈
                // （打开文件夹、Steam 操作结果等）。
                if (!string.IsNullOrEmpty(_translationFailureStatusText)
                    && string.Equals(StatusMessage, _translationFailureStatusText, StringComparison.Ordinal))
                {
                    StatusMessage = BuildStatusMessage(SearchText.Trim());
                }

                _translationFailureStatusText = string.Empty;
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
            // 只有最新一代批次（版本仍是当前值）收尾才算这一轮强制刷新结束：
            // 每次起新批次都推进版本，被重试 / 刷新 / 重建行取代的旧批次在这里对不上号，
            // 不会把接班的（也是强制的）批次还挂在途的标志提前复位。
            if (version == _translationVersion)
            {
                _nameRefreshInFlight = false;
            }

            // 页面关闭或批次被新搜索取代时，已经回填到行的译名仍然有效：
            // 落盘后下次进入 Mod 库可以直接显示，不必重翻这一批。
            await _nameTranslator.SaveAsync(targetLanguage, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 读回持久化的名称译名（按当前目标语言过滤）；读不到不影响后续正常翻译。
    /// 磁盘读取丢线程池，避免首次进入 Mod 库时在 UI 线程上同步读缓存文档。
    /// </summary>
    private async Task LoadCachedNameTranslationsAsync(CancellationToken cancellationToken)
    {
        var targetLanguage = GetNameTranslationTargetLanguage();
        if (targetLanguage is null)
        {
            _nameTranslator.Clear();
            return;
        }

        try
        {
            // 用 Func<Task<…>> 明确选中 Task.Run 的异步重载，别把内层 Task 当成结果。
            Func<Task> load = () => _nameTranslator.LoadAsync(targetLanguage, cancellationToken);
            await Task.Run(load, cancellationToken).ConfigureAwait(true);
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
    private static string? GetNameTranslationTargetLanguage() => ModNameTranslator.ResolveTargetLanguage(
        AppServices.Settings.TranslateNamesEnabled,
        AppServices.Translation.CurrentOptions);

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
        _nameTranslator.Remember(row.ModId, row.Name, targetLanguage, translatedName);
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
