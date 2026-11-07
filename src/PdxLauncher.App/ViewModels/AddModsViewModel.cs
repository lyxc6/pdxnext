namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

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

    private readonly IPlaySetRepository _playSets;
    private readonly ModLibraryService _modLibrary;
    private readonly IAppLogger _logger;

    /// <summary>全部候选行（不受搜索影响）；勾选状态保存在行上，过滤后仍然保留。</summary>
    private readonly List<AddModRowViewModel> _allRows = new();

    private string _searchText = string.Empty;
    private string _playSetName = string.Empty;
    private string _statusMessage = string.Empty;
    private string _selectedSortMode = string.Empty;
    private IReadOnlyList<string> _sortModes = Array.Empty<string>();
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
    }

    /// <summary>
    /// 当前可见的候选行（受搜索过滤）。整表一次性替换：每次搜索只发一次 PropertyChanged，
    /// ListView 收到一次新的 ItemsSource 后自己回收容器，不再「Clear() 再逐条 Add」抖动。
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

    /// <summary>有候选但被搜索过滤光了（该显示「无匹配」提示）。</summary>
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
                _allRows.Add(new AddModRowViewModel(entry, OnRowSelectionChanged, AddModRowViewModel.DefaultAutomationPrefix, game.Version));
            }

            _selectedCount = 0;
            RebuildVisibleRows();
            OnPropertyChanged(nameof(SelectedCountText));
            OnPropertyChanged(nameof(HasSelection));
            StatusMessage = string.Empty;
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
    /// 语言切换：下拉框的显示文案同时就是选中项的值，要按新语言重建选项并把选中项换过去；
    /// 行自己会随属性通知刷新，这里不重排也不重建列表（保留滚动位置）。
    /// </summary>
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

        // 新旧语言的文案可能恰好相同，那样上面的赋值不会变更，仍需通知一次让下拉框重新选中。
        OnPropertyChanged(nameof(SelectedSortMode));
    }

    /// <summary>按搜索关键字重算可见行（行对象复用，勾选状态因此保留）。</summary>
    /// <remarks>
    /// 只换 <see cref="Rows"/> 的引用、不清空逐条加：搜索框每敲一个字都会走到这里，
    /// <c>ObservableCollection.Clear()</c> + 每行一次 CollectionChanged 会让 ListView 拆掉全部容器、
    /// 再一条条建回来，滚动条和行高都会跳一下。一次性赋一个新列表，界面只收到一次变更。
    /// 排序在过滤之后做，搜索与排序可以叠加。
    /// </remarks>
    private void RebuildVisibleRows()
    {
        var query = (_searchText ?? string.Empty).Trim();
        Rows = query.Length == 0
            ? ApplySort(_allRows).ToArray()
            : ApplySort(_allRows.Where(row => row.Matches(query))).ToArray();

        OnPropertyChanged(nameof(HasRows));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowNoMatch));
        OnPropertyChanged(nameof(CandidateCountText));
    }

    /// <summary>按当前语言生成排序下拉框的选项。</summary>
    private static IReadOnlyList<string> BuildSortModes() => SortKeys.Select(static key => T(key)).ToArray();

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
