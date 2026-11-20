namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 「添加模组」子页的 ViewModel：列出 mod 库里「还没进目标播放集」的候选 mod，
/// 让用户多选，再把选择结果交回播放集页。
/// </summary>
/// <remarks>
/// 真正写库由播放集页完成：只有它才知道当前播放集的完整上下文与自动保存逻辑。
/// 本页因此不持有 ModsViewModel，只读取 mod 库与目标播放集已有的 mod id。
/// </remarks>
public sealed class AddModsViewModel : LocalizedObservableObject
{
    /// <summary>
    /// 排序方式的键（默认名称 A-Z）。下拉框里显示的是这些键的本地化文案，
    /// 选中项存的也是文案，所以切换语言时要按键把选中项换到新语言的文案上。
    /// </summary>
    private static readonly string[] SortKeys =
    [
        Keys.ModsAddPageSortByName,
        Keys.ModsAddPageSortBySize,
        Keys.ModsAddPageSortBySubscribed,
    ];

    /// <summary>
    /// 「来源」筛选的键：全部 / 本地 / 订阅。与排序一样，下拉框显示文案、选中项也存文案，
    /// 所以切语言时要按键把选中项换到新语言的文案上。
    /// </summary>
    private static readonly string[] SourceFilterKeys =
    [
        Keys.ModsAddPageSourceFilterAll,
        Keys.ModsAddPageSourceFilterLocal,
        Keys.ModsAddPageSourceFilterSubscribed,
    ];

    /// <summary>
    /// 「版本兼容性」筛选的键：全部 / 版本偏低 / 其余。版本判定只认
    /// <see cref="ModVersionSupport.Below"/>，其余三种结论（未声明 / 游戏版本未知 / 不低于）都归「其余」。
    /// </summary>
    private static readonly string[] VersionFilterKeys =
    [
        Keys.ModsAddPageVersionFilterAll,
        Keys.ModsAddPageVersionFilterBelow,
        Keys.ModsAddPageVersionFilterOther,
    ];

    private readonly IPlaySetRepository _playSets;
    private readonly ModLibraryService _modLibrary;
    private readonly IAppLogger _logger;

    /// <summary>
    /// 名称译名管道：与 Mod 库 / 播放集页共用同一张 <c>TranslationCache.Names</c> 表，
    /// 所以同一个 mod 在这三处显示的一定是同一句译文，且只译一次。
    /// </summary>
    private readonly ModNameTranslator _translator;

    /// <summary>名称翻译去抖时长：与 Mod 库、工坊列表一致。</summary>
    private static readonly TimeSpan NameTranslationDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>本轮名称翻译的代次；行被整批换掉时自增，迟到的旧批次不再回填。</summary>
    private int _translationVersion;

    /// <summary>名称翻译去抖令牌：新批次接管时取消上一批，避免批次越积越多。</summary>
    private CancellationTokenSource? _translationDebounce;

    /// <summary>名称翻译进度回调来自线程池；回填行属性前切回创建本 VM 的 UI 线程。</summary>
    private Microsoft.UI.Dispatching.DispatcherQueue? _dispatcher;

    /// <summary>上一次译名整批失败的原因（拼状态栏文案用）；成功后清空。</summary>
    private string _translationFailureReason = string.Empty;

    /// <summary>失败提示当前确实写进状态栏的那串文字（用来判断它有没有被更新的提示覆盖）。</summary>
    private string _translationFailureStatusText = string.Empty;

    /// <summary>行右键菜单里的「重试翻译」是否可用：本轮译名一条都没译出来时为 true。</summary>
    private bool _canRetryTranslation;

    /// <summary>全部候选行（不受搜索影响）；勾选状态保存在行上，过滤后仍然保留。</summary>
    private readonly List<AddModRowViewModel> _allRows = new();

    private string _searchText = string.Empty;
    private string _playSetName = string.Empty;
    private string _statusMessage = string.Empty;
    private string _selectedSortMode = string.Empty;
    private IReadOnlyList<string> _sortModes = Array.Empty<string>();
    private string _selectedSourceFilter = string.Empty;
    private string _sourceFilterKey = Keys.ModsAddPageSourceFilterAll;
    private IReadOnlyList<string> _sourceFilterModes = Array.Empty<string>();
    private string _selectedVersionFilter = string.Empty;
    private string _versionFilterKey = Keys.ModsAddPageVersionFilterAll;
    private IReadOnlyList<string> _versionFilterModes = Array.Empty<string>();
    private bool _isRelocalizing;
    private bool _isBusy;
    private int _selectedCount;
    private IReadOnlyList<AddModRowViewModel> _rows = Array.Empty<AddModRowViewModel>();

    /// <summary>创建 ViewModel。</summary>
    /// <param name="playSets">播放集仓储（用来读目标播放集已有的 mod）。</param>
    /// <param name="modLibrary">mod 库服务（用来读候选）。</param>
    /// <param name="logger">日志。</param>
    public AddModsViewModel(IPlaySetRepository playSets, ModLibraryService modLibrary, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(modLibrary);
        ArgumentNullException.ThrowIfNull(logger);

        _playSets = playSets;
        _modLibrary = modLibrary;
        _logger = logger;

        _sortModes = BuildSortModes();
        _selectedSortMode = _sortModes[0];
        _sourceFilterModes = BuildSourceFilterModes();
        _selectedSourceFilter = _sourceFilterModes[0];
        _versionFilterModes = BuildVersionFilterModes();
        _selectedVersionFilter = _versionFilterModes[0];

        _translator = new ModNameTranslator(
            ResolveNameTranslationTargetLanguage,
            load: (language, token) => AppServices.Translation.LoadNameTranslationsAsync(language, token),
            save: (language, updates, token) =>
                AppServices.Translation.SaveNameTranslationsAsync(language, updates, token));
    }

    /// <summary>
    /// 行右键菜单里的「重试翻译」是否可用：本轮译名一条都没译出来时为 true。
    /// 重新加载或真的译出结果都会复位。
    /// </summary>
    public bool CanRetryTranslation
    {
        get => _canRetryTranslation;
        private set
        {
            if (_canRetryTranslation == value)
            {
                return;
            }

            _canRetryTranslation = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 当前可见的候选行（受搜索、来源、版本三道筛选，再按排序方式排列）。整表一次性替换：
    /// 每次筛选只发一次 PropertyChanged，ListView 收到一次新的 ItemsSource 后自己回收容器，
    /// 不再「Clear() 再逐条 Add」抖动。
    /// </summary>
    public IReadOnlyList<AddModRowViewModel> Rows
    {
        get => _rows;
        private set
        {
            _rows = value;
            OnPropertyChanged();
        }
    }

    /// <summary>页面标题。</summary>
    public string Title => T(Keys.ModsAddPageTitle);

    /// <summary>副标题：加入播放集「xxx」；没有播放集名时为空串。</summary>
    public string TargetText => string.IsNullOrWhiteSpace(_playSetName)
        ? string.Empty
        : T(Keys.ModsAddPageTarget, _playSetName);

    /// <summary>搜索框占位文案。</summary>
    public string SearchPlaceholder => T(Keys.ModsAddPageSearchPlaceholder);

    /// <summary>搜索框的无障碍名称。</summary>
    public string SearchName => T(Keys.ModsAddPageSearchName);

    /// <summary>候选列表的无障碍名称。</summary>
    public string ListName => T(Keys.ModsAddPageListName);

    /// <summary>一个候选都没有时的提示。</summary>
    public string EmptyText => T(Keys.ModsAddPageEmpty);

    /// <summary>有候选但全被搜索过滤掉时的提示。</summary>
    public string NoMatchText => T(Keys.ModsAddPageNoMatch);

    /// <summary>底部说明文案。</summary>
    public string HintText => T(Keys.ModsAddPageHint);

    /// <summary>「添加」按钮文案。</summary>
    public string AddText => T(Keys.ModsAddPageAdd);

    /// <summary>「返回」按钮文案。</summary>
    public string BackText => T(Keys.ModsAddPageBack);

    /// <summary>已选数量文案。</summary>
    public string SelectedCountText => T(Keys.ModsAddPageSelectedCount, _selectedCount);

    /// <summary>候选总数文案。</summary>
    public string CandidateCountText => T(Keys.ModsAddPageCandidateCount, _allRows.Count);

    /// <summary>是否勾选了至少一个 mod。</summary>
    public bool HasSelection => _selectedCount > 0;

    /// <summary>当前是否有可见的候选行。</summary>
    public bool HasRows => Rows.Count > 0;

    /// <summary>是否一个候选都没有（该显示空状态提示）。</summary>
    public bool ShowEmptyState => _allRows.Count == 0;

    /// <summary>有候选但被搜索 / 来源 / 版本筛选光了（该显示「无匹配」提示）。</summary>
    public bool ShowNoMatch => _allRows.Count > 0 && Rows.Count == 0;

    /// <summary>是否正在读取候选。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
        }
    }

    /// <summary>状态栏文案；失败时写失败原因。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (string.Equals(_statusMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    /// <summary>搜索关键字；写入即过滤可见行（勾选状态不受影响）。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            var text = value ?? string.Empty;
            if (string.Equals(_searchText, text, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = text;
            OnPropertyChanged();
            RebuildVisibleRows();
        }
    }

    /// <summary>排序下拉框的选项（按当前语言生成，语言切换时整体替换）。</summary>
    public IReadOnlyList<string> SortModes
    {
        get => _sortModes;
        private set
        {
            _sortModes = value;
            OnPropertyChanged();
        }
    }

    /// <summary>当前排序方式，取值来自 <see cref="SortModes"/>；变化后立即重排可见行。</summary>
    public string SelectedSortMode
    {
        get => _selectedSortMode;
        set
        {
            var mode = value ?? string.Empty;
            if (string.Equals(_selectedSortMode, mode, StringComparison.Ordinal))
            {
                return;
            }

            _selectedSortMode = mode;
            OnPropertyChanged();

            if (!_isRelocalizing)
            {
                RebuildVisibleRows();
            }
        }
    }

    /// <summary>排序下拉框的无障碍名称。</summary>
    public string SortName => T(Keys.ModsAddPageSortName);

    /// <summary>来源筛选下拉框的选项（全部 / 本地 / 订阅；切语言时整体替换）。</summary>
    public IReadOnlyList<string> SourceFilterModes
    {
        get => _sourceFilterModes;
        private set
        {
            _sourceFilterModes = value;
            OnPropertyChanged();
        }
    }

    /// <summary>当前来源筛选，取值来自 <see cref="SourceFilterModes"/>；变化后立即重建可见行。</summary>
    public string SelectedSourceFilter
    {
        get => _selectedSourceFilter;
        set
        {
            var mode = value ?? string.Empty;
            if (string.Equals(_selectedSourceFilter, mode, StringComparison.Ordinal))
            {
                return;
            }

            _selectedSourceFilter = mode;
            // 记住选中项的逻辑键：Localizer.Apply 先切资源上下文再回调 Relocalize，
            // 那时旧的显示文案已经对不上任何新语言选项，只能靠这里存的键还原。
            _sourceFilterKey = ResolveSourceFilterKey();
            OnPropertyChanged();

            if (!_isRelocalizing)
            {
                RebuildVisibleRows();
            }
        }
    }

    /// <summary>来源筛选下拉框的无障碍名称。</summary>
    public string SourceFilterName => T(Keys.ModsAddPageSourceFilterName);

    /// <summary>版本兼容性筛选下拉框的选项（全部 / 版本偏低 / 其余；切语言时整体替换）。</summary>
    public IReadOnlyList<string> VersionFilterModes
    {
        get => _versionFilterModes;
        private set
        {
            _versionFilterModes = value;
            OnPropertyChanged();
        }
    }

    /// <summary>当前版本兼容性筛选，取值来自 <see cref="VersionFilterModes"/>；变化后立即重建可见行。</summary>
    public string SelectedVersionFilter
    {
        get => _selectedVersionFilter;
        set
        {
            var mode = value ?? string.Empty;
            if (string.Equals(_selectedVersionFilter, mode, StringComparison.Ordinal))
            {
                return;
            }

            _selectedVersionFilter = mode;
            _versionFilterKey = ResolveVersionFilterKey();
            OnPropertyChanged();

            if (!_isRelocalizing)
            {
                RebuildVisibleRows();
            }
        }
    }

    /// <summary>版本兼容性筛选下拉框的无障碍名称。</summary>
    public string VersionFilterName => T(Keys.ModsAddPageVersionFilterName);

    /// <summary>设置目标播放集名称（只用于副标题展示）。</summary>
    public void SetPlaySet(string? playSetName)
    {
        var name = playSetName ?? string.Empty;
        if (string.Equals(_playSetName, name, StringComparison.Ordinal))
        {
            return;
        }

        _playSetName = name;
        OnPropertyChanged(nameof(TargetText));
    }

    /// <summary>
    /// 读取候选：mod 库里、但还没进目标播放集的 mod。
    /// </summary>
    /// <param name="game">当前游戏；为空时没有候选。</param>
    /// <param name="playSetId">目标播放集 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task LoadAsync(GameInstallation? game, Guid playSetId, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            StatusMessage = T(Keys.ModsStatusSelectGameFirst);
            return;
        }

        IsBusy = true;
        try
        {
            // 译名先读盘：名字跟引擎/缓存天数无关，上次译过的这次直接显示，不用重翻。
            // 读盘丢线程池，避免首次进入本页时在 UI 线程上同步读缓存文档。
            var targetLanguage = _translator.TargetLanguage;
            if (targetLanguage is not null)
            {
                Func<Task> load = () => _translator.LoadAsync(targetLanguage, cancellationToken);
                await Task.Run(load, cancellationToken).ConfigureAwait(true);
            }

            var snapshot = await _modLibrary.LoadAsync(game, cancellationToken).ConfigureAwait(true);
            var existing = await _playSets
                .GetPlaySetModsAsync(game, playSetId, cancellationToken)
                .ConfigureAwait(true);
            var existingIds = existing.Select(static mod => mod.ModId).ToHashSet();

            _allRows.Clear();
            foreach (var entry in snapshot.Mods)
            {
                if (existingIds.Contains(entry.ModId))
                {
                    continue;
                }

                // 带上当前游戏版本：版本列要判断「声明支持的版本是否偏低」，判定交给 ModLibraryWarnings。
                var row = new AddModRowViewModel(
                    entry,
                    OnRowSelectionChanged,
                    AddModRowViewModel.DefaultAutomationPrefix,
                    game.Version);

                // 已经译过的名称直接回填：搜索 / 排序重建行时不再等一轮翻译。
                if (_translator.TryBackfill(entry.ModId, entry.Name, targetLanguage, out var translated))
                {
                    row.TranslatedName = translated;
                }

                _allRows.Add(row);
            }

            _selectedCount = 0;
            _translationVersion++;
            CanRetryTranslation = false;
            _translationFailureReason = string.Empty;
            _translationFailureStatusText = string.Empty;
            RebuildVisibleRows();
            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(HasSelection));
            StatusMessage = string.Empty;
            StartNameTranslation(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("添加模组页读取候选失败", ex);
            StatusMessage = T(Keys.ModsStatusReadLibraryFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>当前勾选的 mod 条目（顺序与候选一致）。</summary>
    public IReadOnlyList<ModLibraryEntry> GetSelectedEntries()
    {
        return _allRows
            .Where(static row => row.IsSelected)
            .Select(static row => row.Entry)
            .ToList();
    }

    /// <summary>
    /// 用户点了添加模组行右键菜单里的「重试翻译」：把本页还没译名的行强制重翻一遍。
    /// 与 Mod 库 / 播放集页同口径——先收起提示，再清掉翻译主机的临时冷却，否则重试会立刻拿到
    /// 「暂时跳过」，用户以为按钮没反应。
    /// <para>
    /// 带 <c>forceRefresh</c>：失败后旧缓存条目还在，不带 force 会命中旧缓存把旧译文原样填回行
    /// 并按成功路径清掉失败提示，引擎根本不会被问到。
    /// </para>
    /// </summary>
    public void RetryNameTranslation()
    {
        _translationFailureReason = string.Empty;
        _translationFailureStatusText = string.Empty;
        CanRetryTranslation = false;
        StatusMessage = string.Empty;
        AppServices.Translation.ResetRetryableFailures();
        StartNameTranslation(CancellationToken.None, forceRefresh: true);
    }

    /// <summary>
    /// 用户点了行右键菜单里的「刷新译文」：忽略缓存，本页候选全部名称强制重翻一遍。
    /// <para>
    /// 除了像重试那样收起提示，还要清掉内存译名表与行上的旧译文：
    /// 不清的话「只翻没译名的行」的过滤会把旧译文原样留着，引擎根本不会被问到。
    /// </para>
    /// </summary>
    public void RefreshNameTranslation()
    {
        _translationFailureReason = string.Empty;
        _translationFailureStatusText = string.Empty;
        CanRetryTranslation = false;
        StatusMessage = string.Empty;
        AppServices.Translation.ResetRetryableFailures();

        _translator.Clear();
        foreach (var row in _allRows)
        {
            row.TranslatedName = null;
        }

        StartNameTranslation(CancellationToken.None, forceRefresh: true);
    }

    /// <summary>当前该用的名称翻译目标语言；名称翻译或翻译总开关关闭时为 null。</summary>
    private static string? ResolveNameTranslationTargetLanguage() => ModNameTranslator.ResolveTargetLanguage(
        AppServices.Settings.TranslateNamesEnabled,
        AppServices.Translation.CurrentOptions);

    /// <summary>
    /// 后台给本页候选补译名：翻译是网络操作，不能挡着列表显示。
    /// 只翻还没有译名的行；连续重建时先取消上一批，250ms 内的多次变化合并成一批。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    /// <param name="forceRefresh">为 true 时跳过缓存直接请求引擎（「刷新译文」用），结果照常写回缓存覆盖旧值。</param>
    private void StartNameTranslation(CancellationToken cancellationToken, bool forceRefresh = false)
    {
        var targetLanguage = _translator.TargetLanguage;
        if (targetLanguage is null)
        {
            return;
        }

        var pending = _allRows
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
        var pageToken = cancellationToken.CanBeCanceled ? cancellationToken : CancellationToken.None;
        var debounce = CancellationTokenSource.CreateLinkedTokenSource(pageToken);
        _translationDebounce = debounce;
        _ = TranslateNamesAfterDebounceAsync(pending, targetLanguage, version, debounce, forceRefresh);
    }

    /// <summary>去抖到点后翻这一批；令牌被新批次取消时什么都不做。</summary>
    private async Task TranslateNamesAfterDebounceAsync(
        IReadOnlyList<AddModRowViewModel> rows,
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

    /// <summary>取消还没到点 / 没跑完的名称翻译批次。</summary>
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
        IReadOnlyList<AddModRowViewModel> rows,
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
                // 进度回调通常已经回填；这里兜底处理回调被丢弃的情况。
                ApplyTranslatedName(rows, i, translated[i], targetLanguage, version);
            }

            // 以行上的实际结果为准：设置变更导致整批返回空时，缓存命中的那部分仍算已译出。
            var anyTranslated = rows.Any(static row => !string.IsNullOrWhiteSpace(row.TranslatedName));
            if (!anyTranslated)
            {
                var reason = AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown);
                _logger.Warn("添加模组页名称全部未翻译（" + rows.Count + " 条）：" + reason);
                _translationFailureReason = reason;
                _translationFailureStatusText = T(Keys.ModsTranslateUnavailable, reason);
                StatusMessage = _translationFailureStatusText;
                CanRetryTranslation = true;
            }
            else
            {
                _translationFailureReason = string.Empty;
                CanRetryTranslation = false;

                // 只在状态栏仍然是那条失败提示时才清空：否则会盖掉用户刚看到的其它反馈。
                if (!string.IsNullOrEmpty(_translationFailureStatusText)
                    && string.Equals(StatusMessage, _translationFailureStatusText, StringComparison.Ordinal))
                {
                    StatusMessage = string.Empty;
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
            _logger.Warn("添加模组页名称翻译失败：" + ex.Message);
        }
        finally
        {
            // 批次被新搜索取代时，已经回填到行的译名仍然有效：落盘后其他页面直接复用。
            await _translator.SaveAsync(targetLanguage, CancellationToken.None).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 把一条译文回填到对应行；回调可能来自线程池，切回 UI 线程后再改行属性。
    /// 旧批次的迟到回调按代次丢弃，免得把已经换掉的行写乱。
    /// </summary>
    private void ApplyTranslatedName(
        IReadOnlyList<AddModRowViewModel> rows,
        int index,
        string? translatedName,
        string targetLanguage,
        int version)
    {
        if (string.IsNullOrWhiteSpace(translatedName)
            || version != _translationVersion
            || index < 0
            || index >= rows.Count)
        {
            return;
        }

        var row = rows[index];

        // 即使这一行已经被筛掉也记下来：下次搜索 / 排序重建时可以直接复用。
        _translator.Remember(row.ModId, row.Name, targetLanguage, translatedName);

        void Apply()
        {
            if (version == _translationVersion)
            {
                row.TranslatedName = translatedName;
            }
        }

        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            Apply();
            return;
        }

        dispatcher.TryEnqueue(Apply);
    }

    /// <summary>
    /// 语言切换：下拉框的显示文案同时就是选中项的值，要按新语言重建选项并把选中项换过去；
    /// 行自己会随属性通知刷新，这里不重排也不重建列表（保留滚动位置）。
    /// </summary>
    protected override void Relocalize()
    {
        var sortKey = ResolveSortKey();
        var sourceFilterKey = ResolveSourceFilterKey();
        var versionFilterKey = ResolveVersionFilterKey();
        SortModes = BuildSortModes();
        SourceFilterModes = BuildSourceFilterModes();
        VersionFilterModes = BuildVersionFilterModes();

        // 缓存当前语言下的逻辑键，下一步切上下文后 Resolve 仍按新语言的文案找回同一个选项。
        _sourceFilterKey = sourceFilterKey;
        _versionFilterKey = versionFilterKey;

        _isRelocalizing = true;
        try
        {
            SelectedSortMode = T(sortKey);
            SelectedSourceFilter = T(sourceFilterKey);
            SelectedVersionFilter = T(versionFilterKey);
        }
        finally
        {
            _isRelocalizing = false;
        }

        // 新旧语言的文案可能恰好相同，那样上面的赋值不会变更，仍需通知一次让下拉框重新选中。
        OnPropertyChanged(nameof(SelectedSortMode));
        OnPropertyChanged(nameof(SelectedSourceFilter));
        OnPropertyChanged(nameof(SelectedVersionFilter));
    }

    /// <summary>按搜索词、来源、版本三道条件重算可见行（行对象复用，勾选状态因此保留）。</summary>
    /// <remarks>
    /// 只换 <see cref="Rows"/> 的引用、不清空逐条加：搜索框每敲一个字、每次换筛选都会走到这里，
    /// <c>ObservableCollection.Clear()</c> + 每行一次 CollectionChanged 会让 ListView 拆掉全部容器、
    /// 再一条条建回来，滚动条和行高都会跳一下。一次性赋一个新列表，界面只收到一次变更。
    /// 三道过滤全部做完后再排序，搜索 / 来源 / 版本与排序可以叠加。
    /// </remarks>
    private void RebuildVisibleRows()
    {
        var query = (_searchText ?? string.Empty).Trim();
        var matches = _allRows.Where(row =>
            (query.Length == 0 || row.Matches(query))
            && MatchesSourceFilter(row)
            && MatchesVersionFilter(row));
        Rows = ApplySort(matches).ToArray();

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(CandidateCountText));
    }

    /// <summary>按当前语言生成排序下拉框的选项。</summary>
    private static IReadOnlyList<string> BuildSortModes() => SortKeys.Select(static key => T(key)).ToArray();

    /// <summary>按当前语言生成来源筛选的选项。</summary>
    private static IReadOnlyList<string> BuildSourceFilterModes() =>
        SourceFilterKeys.Select(static key => T(key)).ToArray();

    /// <summary>按当前语言生成版本兼容性筛选的选项。</summary>
    private static IReadOnlyList<string> BuildVersionFilterModes() =>
        VersionFilterKeys.Select(static key => T(key)).ToArray();

    /// <summary>选中项对应的来源筛选键；先按当前语言的文案匹配，对不上时回落到上次记住的键，
    /// 再对不上（外部塞了乱码）才回落到「全部」。</summary>
    private string ResolveSourceFilterKey()
    {
        foreach (var key in SourceFilterKeys)
        {
            if (string.Equals(T(key), SelectedSourceFilter, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return SourceFilterKeys.Contains(_sourceFilterKey, StringComparer.Ordinal)
            ? _sourceFilterKey
            : SourceFilterKeys[0];
    }

    /// <summary>选中项对应的版本筛选键；匹配口径同 <see cref="ResolveSourceFilterKey"/>。</summary>
    private string ResolveVersionFilterKey()
    {
        foreach (var key in VersionFilterKeys)
        {
            if (string.Equals(T(key), SelectedVersionFilter, StringComparison.Ordinal))
            {
                return key;
            }
        }

        return VersionFilterKeys.Contains(_versionFilterKey, StringComparer.Ordinal)
            ? _versionFilterKey
            : VersionFilterKeys[0];
    }

    /// <summary>按来源筛选键过滤候选行（全部不过滤）。</summary>
    private bool MatchesSourceFilter(AddModRowViewModel row)
    {
        var key = ResolveSourceFilterKey();
        if (string.Equals(key, Keys.ModsAddPageSourceFilterLocal, StringComparison.Ordinal))
        {
            return row.Entry.Origin == ModOrigin.Local;
        }

        if (string.Equals(key, Keys.ModsAddPageSourceFilterSubscribed, StringComparison.Ordinal))
        {
            return row.Entry.Origin == ModOrigin.Subscribed;
        }

        return true;
    }

    /// <summary>
    /// 按版本兼容性筛选键过滤候选行：只有 <see cref="ModVersionSupport.Below"/> 算「版本偏低」，
    /// 未声明 / 游戏版本未知 / 不低于都归「其余」。判定取自行上的 VersionSupport（同一份
    /// ModLibraryWarnings 结论），这里不再解析版本号。
    /// </summary>
    private bool MatchesVersionFilter(AddModRowViewModel row)
    {
        var key = ResolveVersionFilterKey();
        if (string.Equals(key, Keys.ModsAddPageVersionFilterBelow, StringComparison.Ordinal))
        {
            return row.VersionSupport == ModVersionSupport.Below;
        }

        if (string.Equals(key, Keys.ModsAddPageVersionFilterOther, StringComparison.Ordinal))
        {
            return row.VersionSupport != ModVersionSupport.Below;
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

    /// <summary>
    /// 排序：名称 A-Z（默认，忽略大小写）；大小降序与订阅时间降序时未知值排最后，
    /// 名称始终作为次级键，保证顺序稳定。
    /// </summary>
    private IEnumerable<AddModRowViewModel> ApplySort(IEnumerable<AddModRowViewModel> rows) =>
        ResolveSortKey() switch
        {
            Keys.ModsAddPageSortBySize => rows
                .OrderByDescending(static row => row.Entry.SizeBytes.HasValue)
                .ThenByDescending(static row => row.Entry.SizeBytes ?? long.MinValue)
                .ThenBy(static row => row.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Entry.ModId),
            Keys.ModsAddPageSortBySubscribed => rows
                .OrderByDescending(static row => row.Entry.SubscribedOn.HasValue)
                .ThenByDescending(static row => row.Entry.SubscribedOn ?? DateTimeOffset.MinValue)
                .ThenBy(static row => row.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Entry.ModId),
            _ => rows
                .OrderBy(static row => row.Entry.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static row => row.Entry.ModId),
        };

    /// <summary>行的勾选状态变化：只更新计数，勾选本身由行自己持有。</summary>
    private void OnRowSelectionChanged(AddModRowViewModel row, bool isSelected)
    {
        _selectedCount = isSelected
            ? _selectedCount + 1
            : Math.Max(0, _selectedCount - 1);

        OnPropertyChanged(nameof(SelectedCountText));
        OnPropertyChanged(nameof(HasSelection));
    }
}
