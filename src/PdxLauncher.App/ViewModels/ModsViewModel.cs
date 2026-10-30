namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 「播放集」页的视图模型：播放集列表 + 当前播放集已保存的 mod 行 + 搜索 + 启用/禁用/排序/增删。
/// 原版启动器显示方式下，切换播放集只切换当前查看/编辑的播放集；必须点「激活播放集」才会
/// 更新 sqlite 的 isActive 并写入游戏加载文件。编辑自动保存：sqlite 始终写，加载文件只跟随激活集。
/// 不提供「保存」与「保存并启动」按钮。
/// 只做状态与命令，不引用任何 Microsoft.UI.Xaml 类型；对话框与提示由页面负责。
/// </summary>
public sealed class ModsViewModel : LocalizedObservableObject
{
    private readonly PlaySetEditor _editor;
    private readonly ModLibraryService _modLibrary;
    private readonly ModPathRepairer _pathRepairer;
    private readonly IAppLogger _logger;

    /// <summary>自动保存串行门：一次只允许一个保存流程写 sqlite / 加载文件。</summary>
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private GameInstallation? _game;
    private bool _isBusy;
    private bool _isLoaded;
    private string _statusMessage = string.Empty;
    private string _searchText = string.Empty;
    private PlaySet? _selectedPlaySet;
    private IReadOnlyList<PlaySetMod> _allMods = Array.Empty<PlaySetMod>();

    /// <summary>
    /// 当前播放集「加载时」的库内指纹（ModId + 启用态 + 顺序）。保存前与库内现状比对，
    /// 用来发现官方启动器是否在这期间改写过同一个播放集——写库是「DELETE 全表 + 逐行 INSERT」，
    /// 直接用旧内存落盘会把官方的改动整份盖掉。null 表示还没有基线（首次加载 / 读取失败），此时不拦截。
    /// </summary>
    private string? _modsBaseline;
    private ModLibrarySnapshot _librarySnapshot = ModLibrarySnapshot.Empty;
    private Dictionary<Guid, ModLibraryEntry> _entryByModId = new();

    /// <summary>播放集页面显示方式（原版启动器 / 留空），来自 AppSettings，页面加载时刷新。</summary>
    private string _displayMode = PlaySetPageDisplayModes.OfficialLauncher;

    /// <summary>是否有待执行的自动保存；连续快速改动会被合并成一次保存。</summary>
    private bool _autoSavePending;

    /// <summary>正在运行的自动保存循环；为 null 或已完成时才允许启动新循环。</summary>
    private Task? _autoSaveTask;

    public ModsViewModel(
        PlaySetEditor editor,
        ModLibraryService modLibrary,
        ModPathRepairer pathRepairer,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(modLibrary);
        ArgumentNullException.ThrowIfNull(pathRepairer);
        ArgumentNullException.ThrowIfNull(logger);

        _editor = editor;
        _modLibrary = modLibrary;
        _pathRepairer = pathRepairer;
        _logger = logger;
    }

    /// <summary>
    /// 请求把某个 mod 的行滚到可见区域内。排序成功后触发，由页面订阅后调 ListView.ScrollIntoView。
    /// 视图模型不引用 ListView（保持无 UI 依赖），所以用事件把「想让它出现」这件事交出去。
    /// 参数是 ModId 而不是行对象：重排会重建所有行，旧行对象此时已经不在集合里。
    /// </summary>
    public event Action<Guid>? RowRevealRequested;

    /// <summary>播放集列表；IsActive 供模板显示"● 激活"标记。</summary>
    public ObservableCollection<PlaySet> PlaySets { get; } = new();

    /// <summary>当前播放集的 mod 行（已按搜索词过滤、按 position 排序）。</summary>
    public ObservableCollection<ModRowViewModel> Mods { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
                OnPropertyChanged(nameof(CanActivateSelected));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.ModsGameNone);

    /// <summary>当前显示方式是否为原版启动器布局。</summary>
    public bool IsOfficialLayout =>
        string.Equals(_displayMode, PlaySetPageDisplayModes.OfficialLauncher, StringComparison.Ordinal);

    /// <summary>当前显示方式是否为留空布局。</summary>
    public bool IsPlaceholderLayout =>
        string.Equals(_displayMode, PlaySetPageDisplayModes.Placeholder, StringComparison.Ordinal);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsEditorEnabled));
                OnPropertyChanged(nameof(CanManageSelection));
                OnPropertyChanged(nameof(CanActivateSelected));
                RaiseActivationState();
            }
        }
    }

    /// <summary>首次加载完成（界面据此决定是否显示空状态提示）。</summary>
    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>状态栏文本（自动保存结果、错误、契约缺口等）。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>搜索框内容；变化时重建可见行。</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                OnPropertyChanged(nameof(CanMoveMods));
                // RebuildVisibleMods 重建每一行并写入 CanMove（行模板绑的是行上的 CanMove，
                // 不是 ViewModel.CanMoveMods —— 历史缺陷：搜索态按钮看着能点、点了只回一句状态栏）。
                RebuildVisibleMods();
            }
        }
    }

    /// <summary>搜索时禁用上移/下移：可见顺序与真实 position 顺序不一致，移动会打乱排序。</summary>
    public bool CanMoveMods => string.IsNullOrWhiteSpace(SearchText);

    public PlaySet? SelectedPlaySet
    {
        get => _selectedPlaySet;
        set
        {
            if (SetProperty(ref _selectedPlaySet, value))
            {
                RaiseSelectedPlaySetChanged();
            }
        }
    }

    private void RaiseSelectedPlaySetChanged()
    {
        OnPropertyChanged(nameof(SelectedPlaySetName));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsModListEmpty));
        OnPropertyChanged(nameof(HasActivePlaySet));
        OnPropertyChanged(nameof(DeleteFallbackPlaySet));
        OnPropertyChanged(nameof(CanManageSelection));
        RaiseActivationState();
    }

    public string SelectedPlaySetName => SelectedPlaySet?.Name ?? T(Keys.ModsPlaySetNone);

    public bool HasSelection => SelectedPlaySet is not null;

    /// <summary>选中项相关的「重命名 / 删除」按钮是否可用：有选中且不在忙碌中。</summary>
    public bool CanManageSelection => HasSelection && !IsBusy;

    /// <summary>当前选中的播放集是否就是激活集（以列表里的 IsActive 为准，避免 SelectedPlaySet 过期）。</summary>
    public bool IsSelectedPlaySetActive =>
        SelectedPlaySet is { } selected && PlaySets.Any(p => p.Id == selected.Id && p.IsActive);

    /// <summary>当前激活的播放集；没有则为 null。</summary>
    public PlaySet? ActivePlaySet => PlaySets.FirstOrDefault(static p => p.IsActive);

    /// <summary>头部摘要：游戏当前使用哪个播放集。</summary>
    public string ActivePlaySetSummary => ActivePlaySet is { } active
        ? T(Keys.ModsSummaryActive, active.Name)
        : T(Keys.ModsSummaryNone);

    /// <summary>「激活播放集」按钮是否可用：有选中、游戏支持内容管理、非忙碌、且选中的不是激活集。</summary>
    public bool CanActivateSelected =>
        Game is { SupportsContentManagement: true }
        && SelectedPlaySet is not null
        && !IsBusy
        && !IsSelectedPlaySetActive;

    /// <summary>「激活播放集」按钮文字：激活后显示「已激活」。</summary>
    public string ActivateButtonText => IsSelectedPlaySetActive
        ? T(Keys.ModsActivateButtonApplied)
        : T(Keys.ModsActivateButton);

    /// <summary>切换/激活/加载期间禁用编辑控件，避免并发修改落到错误的播放集。</summary>
    public bool IsEditorEnabled => !IsBusy;

    /// <summary>已选中播放集但（按当前搜索）没有任何 mod 时，界面显示空状态提示。</summary>
    public bool IsModListEmpty => SelectedPlaySet is not null && Mods.Count == 0;

    /// <summary>当前游戏是否有激活播放集。</summary>
    public bool HasActivePlaySet => PlaySets.Any(static p => p.IsActive);

    /// <summary>
    /// 删除激活播放集时的接替者：当前播放集是激活集时，取任意一个其它播放集；否则为 null。
    /// 页面据此决定删除确认文案、以及「只剩一个激活集」时拒绝删除。
    /// </summary>
    public PlaySet? DeleteFallbackPlaySet => IsSelectedPlaySetActive
        ? PlaySets.FirstOrDefault(p => SelectedPlaySet is not null && p.Id != SelectedPlaySet.Id)
        : null;

    public int VisibleModCount => Mods.Count;

    public int EnabledModCount => Mods.Count(static m => m.IsEnabled);

    public string CountsText =>
        T(Keys.ModsSummaryCounts, VisibleModCount, EnabledModCount);

    // ------------------------------------------------------------ 加载

    /// <summary>切换游戏或页面重新进入时刷新显示方式与全部数据。</summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        RefreshDisplayMode();
        Game = game;
        if (game is null)
        {
            PlaySets.Clear();
            Mods.Clear();
            _allMods = Array.Empty<PlaySetMod>();
            _modsBaseline = null;
            _librarySnapshot = ModLibrarySnapshot.Empty;
            _entryByModId = new Dictionary<Guid, ModLibraryEntry>();
            SelectedPlaySet = null;
            IsLoaded = false;
            StatusMessage = T(Keys.ModsStatusSelectGameFirst);
            RaiseCounts();
            return;
        }

        if (!game.SupportsContentManagement)
        {
            // 仅启动的游戏：播放集 / mod 全归游戏本体，清空状态并直接返回。
            PlaySets.Clear();
            Mods.Clear();
            _allMods = Array.Empty<PlaySetMod>();
            _modsBaseline = null;
            _librarySnapshot = ModLibrarySnapshot.Empty;
            _entryByModId = new Dictionary<Guid, ModLibraryEntry>();
            SelectedPlaySet = null;
            IsLoaded = true;
            StatusMessage = SupportMessages.ManagedByGame;
            RaiseCounts();
            return;
        }

        IsBusy = true;
        try
        {
            var sets = await AppServices.PlaySets
                .GetPlaySetsAsync(game, cancellationToken)
                .ConfigureAwait(true);
            var active = await AppServices.PlaySets
                .GetActivePlaySetAsync(game, cancellationToken)
                .ConfigureAwait(true);

            PlaySets.Clear();
            foreach (var set in sets.OrderByDescending(static s => s.IsActive).ThenBy(static s => s.Name))
            {
                PlaySets.Add(set);
            }

            var target = PlaySets.FirstOrDefault(p => active is not null && p.Id == active.Id)
                ?? PlaySets.FirstOrDefault(static p => p.IsActive)
                ?? PlaySets.FirstOrDefault();
            await SelectPlaySetAsync(target, cancellationToken).ConfigureAwait(true);

            IsLoaded = true;
            StatusMessage = PlaySets.Count == 0
                ? T(Keys.ModsStatusNoPlaySets)
                : T(Keys.ModsStatusLoadedPlaySets, PlaySets.Count);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("ModsPage 加载失败（gameId=" + game.GameId + "）", ex);
            StatusMessage = T(Keys.ModsStatusLoadFailed, ex.Message);
            IsLoaded = true;
        }
        finally
        {
            IsBusy = false;
            RaiseCounts();
        }
    }

    /// <summary>把设置里的显示方式读进来（未知值回退原版启动器），并通知界面切换布局。</summary>
    public void RefreshDisplayMode()
    {
        var mode = PlaySetPageDisplayModes.Normalize(AppServices.Settings.PlaySetPageDisplayMode);
        if (string.Equals(_displayMode, mode, StringComparison.Ordinal))
        {
            return;
        }

        _displayMode = mode;
        OnPropertyChanged(nameof(IsOfficialLayout));
        OnPropertyChanged(nameof(IsPlaceholderLayout));
    }

    /// <summary>选中一个播放集并加载它已保存的 mod 行（不再合并全库）。</summary>
    public async Task SelectPlaySetAsync(PlaySet? playSet, CancellationToken cancellationToken)
    {
        // 集合元素是不可变 record，激活标记变化时会被替换成新实例；这里按 Id 归一化，
        // 保证 SelectedPlaySet 始终是 ItemsSource 中的对象，ComboBox 才能匹配到选中项。
        if (playSet is not null)
        {
            playSet = PlaySets.FirstOrDefault(p => p.Id == playSet.Id) ?? playSet;
        }

        SelectedPlaySet = playSet;
        // PlaySet 是值相等的 record：LoadAsync 里 PlaySets.Clear() 会让 ComboBox 的 SelectedItem
        // 掉成 null，随后即使重新选中"同一个播放集的新实例"，SetProperty 也会因值相等而跳过通知，
        // 单向 x:Bind 永远不回灌，选择框就一直空白（页面之前只有摘要文字有值，正是这个症状）。
        // 因此这里无条件补一次 PropertyChanged，保证下拉框每次都能显示当前播放集名。
        OnPropertyChanged(nameof(SelectedPlaySet));
        if (Game is null || playSet is null)
        {
            _allMods = Array.Empty<PlaySetMod>();
            _modsBaseline = null;
            RebuildVisibleMods();
            return;
        }

        IsBusy = true;
        try
        {
            var saved = await AppServices.PlaySets
                .GetPlaySetModsAsync(Game, playSet.Id, cancellationToken)
                .ConfigureAwait(true);

            _allMods = PlaySetEditor.Renumber(
                saved.OrderBy(static m => m.Position).ToList());

            // 记下这一份「库内现状」，保存前用它确认官方启动器没在这期间动过同一个播放集。
            _modsBaseline = PlaySetEditor.DescribePlaySetMods(_allMods);

            // DDS 缩略图先转成 PNG，行里才是真实封面而不是占位图。
            await AppServices.ImageArt
                .PrewarmAsync(_allMods.Select(static m => m.ThumbnailPath), cancellationToken)
                .ConfigureAwait(true);

            RebuildVisibleMods();

            // mod 库只用于补全大小与「添加模组」候选；读失败不影响已保存的播放集内容。
            try
            {
                _librarySnapshot = await _modLibrary
                    .LoadAsync(Game, cancellationToken)
                    .ConfigureAwait(true);
                _entryByModId = BuildEntryMap(_librarySnapshot);
                RebuildVisibleMods();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _librarySnapshot = ModLibrarySnapshot.Empty;
                _entryByModId = new Dictionary<Guid, ModLibraryEntry>();
                _logger.Warn("读取 mod 库失败（gameId=" + Game.GameId + "）：" + ex.Message);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("读取播放集 mod 失败（playSetId=" + playSet.Id.ToString("N") + "）", ex);
            StatusMessage = T(Keys.ModsStatusReadModsFailed, ex.Message);
            _allMods = Array.Empty<PlaySetMod>();
            _modsBaseline = null;
            RebuildVisibleMods();
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------ 添加 / 移除

    /// <summary>把 mod 库里的条目追加到当前播放集（默认启用，排在末尾，随后自动保存）。</summary>
    public void AddMods(IEnumerable<ModLibraryEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        FlushVisibleRows();

        var existingIds = _allMods.Select(static m => m.ModId).ToHashSet();
        var appended = new List<PlaySetMod>(_allMods);
        var added = 0;
        foreach (var entry in entries)
        {
            if (entry is null || !existingIds.Add(entry.ModId))
            {
                continue;
            }

            appended.Add(new PlaySetMod(
                entry.ModId,
                entry.Name,
                entry.DirPath ?? string.Empty,
                entry.ArchivePath,
                entry.GameRegistryId,
                entry.ThumbnailPath,
                entry.RequiredVersion,
                entry.Status,
                true,
                appended.Count,
                entry.SteamId));
            added++;
        }

        if (added == 0)
        {
            StatusMessage = T(Keys.ModsStatusNoModsAdded);
            return;
        }

        _allMods = PlaySetEditor.Renumber(appended);
        RebuildVisibleMods();
        _logger.Info("播放集添加 " + added.ToString(CultureInfo.InvariantCulture) + " 个 mod（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        StatusMessage = T(Keys.ModsStatusModsAdded, added);
        RequestAutoSave();
    }

    /// <summary>从当前播放集中移除一行（随后自动保存）。搜索过滤下也允许。</summary>
    public void RemoveMod(ModRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        FlushVisibleRows();

        var remaining = _allMods.Where(m => m.ModId != row.ModId).ToList();
        if (remaining.Count == _allMods.Count)
        {
            return;
        }

        _allMods = PlaySetEditor.Renumber(remaining);
        RebuildVisibleMods();
        _logger.Info("播放集移除 mod「" + row.Name + "」（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        StatusMessage = T(Keys.ModsStatusModRemoved, row.Name);
        RequestAutoSave();
    }

    // ------------------------------------------------------------ 编辑

    /// <summary>按搜索词重建可见行；行顺序即 _allMods 中 position 顺序。</summary>
    private void RebuildVisibleMods()
    {
        var filtered = PlaySetEditor.Filter(_allMods, SearchText);
        var canMove = CanMoveMods;
        Mods.Clear();
        foreach (var mod in filtered)
        {
            var row = new ModRowViewModel(
                mod,
                OnRowEnabledChanged,
                OnRowMove,
                OnRowRemove,
                ModThumbnailSizes.Pixels(AppServices.Settings.ModThumbnailSize));
            var origin = ModOrigins.Normalize(
                mod.Source ?? (_entryByModId.TryGetValue(mod.ModId, out var known) ? known.Source : null),
                mod.SteamId);

            string? warningKey = null;
            if (_game is not null)
            {
                // 存在性检查在这里做（ModLoadRules 本身是纯函数）：路径写进加载文件但磁盘上没有，
                // 游戏同样会静默忽略，属于同一类「启用了却没生效」。
                warningKey = ModLoadRules.DescribeSkipKey(
                    _game,
                    mod,
                    AppServices.FileSystem.DirectoryExists,
                    AppServices.FileSystem.FileExists);
            }

            if (_entryByModId.TryGetValue(mod.ModId, out var entry))
            {
                row.SetDetails(
                    FormatSize(entry.SizeBytes),
                    T(ModOrigins.DescribeKey(origin)),
                    origin == ModOrigin.Local,
                    warningKey is null ? null : T(warningKey));
            }
            else
            {
                row.SetDetails(
                    null,
                    T(ModOrigins.DescribeKey(origin)),
                    origin == ModOrigin.Local,
                    warningKey is null ? null : T(warningKey));
            }

            row.TotalCount = filtered.Count;
            row.CanMove = canMove;
            Mods.Add(row);
        }

        RaiseCounts();
    }

    /// <summary>行内开关变化：同步集合顺序（不重排），刷新计数与状态，并自动保存。</summary>
    private void OnRowEnabledChanged(ModRowViewModel row)
    {
        RaiseCounts();
        StatusMessage = row.IsEnabled
            ? T(Keys.ModsStatusModEnabled, row.Name)
            : T(Keys.ModsStatusModDisabled, row.Name);
        RequestAutoSave();
    }

    /// <summary>行内上移/下移按钮：offset=-1 上移，+1 下移。</summary>
    private void OnRowMove(ModRowViewModel row, int offset)
    {
        MoveMod(row, offset);
    }

    /// <summary>行内移除按钮。</summary>
    private void OnRowRemove(ModRowViewModel row)
    {
        RemoveMod(row);
    }

    /// <summary>把可见行写回 _allMods：可见行按当前顺序在前，未匹配搜索的保留在后。</summary>
    private void FlushVisibleRows()
    {
        var visible = Mods.Select(static m => m.ToPlaySetMod()).ToList();
        var visibleIds = visible.Select(static m => m.ModId).ToHashSet();
        var rest = _allMods.Where(m => !visibleIds.Contains(m.ModId)).ToList();

        var merged = new List<PlaySetMod>(visible.Count + rest.Count);
        merged.AddRange(visible);
        merged.AddRange(rest);
        _allMods = PlaySetEditor.Renumber(merged);
    }

    /// <summary>上移（offset=-1）/下移（offset=+1）。搜索过滤时不可用。</summary>
    public void MoveMod(ModRowViewModel row, int offset)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!CanMoveMods)
        {
            StatusMessage = T(Keys.ModsStatusSearchBlocksMove);
            return;
        }

        var index = Mods.IndexOf(row);
        if (index < 0)
        {
            return;
        }

        var target = index + offset;
        if (target < 0 || target >= Mods.Count)
        {
            return;
        }

        FlushVisibleRows();
        _allMods = PlaySetEditor.Move(_allMods, index, offset);
        RebuildVisibleMods();
        StatusMessage = T(Keys.ModsStatusOrderMoved, row.Name, index + 1, target + 1);
        _logger.Info("播放集排序（step）：「" + row.Name + "」第 " + (index + 1).ToString(CultureInfo.InvariantCulture)
            + " 位 → 第 " + (target + 1).ToString(CultureInfo.InvariantCulture) + " 位，共 "
            + Mods.Count.ToString(CultureInfo.InvariantCulture) + " 行，激活集=" + (IsSelectedPlaySetActive ? "是" : "否")
            + "（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        RequestAutoSave();
        RowRevealRequested?.Invoke(row.ModId);
    }

    /// <summary>
    /// 把一行直接移到用户输入的位次（界面看到的 1 起数字）。
    /// 语义是「插入」：其余行依次顺移/前移，由 <see cref="PlaySetEditor.Move"/> 的 RemoveAt + Insert 保证。
    /// 输入大于总行数时由 <see cref="PositionInput.TryParse"/> 钳到末位（置底），不报错。
    /// 解析与范围校验都在那边做，界面只负责把输入框原文递进来 ——
    /// 这样文案、边界与状态栏反馈只有一处真源，也便于单独测。
    /// </summary>
    /// <param name="row">要移动的可见行。</param>
    /// <param name="text">输入框原文（可能含空白、全角数字、负数、小数等）。</param>
    /// <returns>
    /// <see cref="PositionEditResult.Applied"/> 已重排并请求自动保存，界面应退出编辑态；
    /// <see cref="PositionEditResult.Unchanged"/> 目标位就是当前位，未改数据，界面应退出编辑态；
    /// <see cref="PositionEditResult.Invalid"/> 输入非法（空、非纯数字、小于 1），状态栏已提示，界面应保持编辑态；
    /// <see cref="PositionEditResult.Blocked"/> 搜索过滤态不允许改顺序，状态栏已提示，界面应退出编辑态。
    /// </returns>
    public PositionEditResult TryMoveModToPosition(ModRowViewModel row, string? text)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (!CanMoveMods)
        {
            StatusMessage = T(Keys.ModsStatusSearchBlocksMove);
            return PositionEditResult.Blocked;
        }

        var index = Mods.IndexOf(row);
        if (index < 0)
        {
            return PositionEditResult.Invalid;
        }

        var count = Mods.Count;
        if (!PositionInput.TryParse(text, count, out var targetPosition))
        {
            StatusMessage = T(Keys.ModsStatusInvalidPosition, count);
            return PositionEditResult.Invalid;
        }

        var target = targetPosition - 1;
        if (target == index)
        {
            StatusMessage = T(Keys.ModsStatusAlreadyAtPosition, row.Name, targetPosition);
            return PositionEditResult.Unchanged;
        }

        FlushVisibleRows();
        _allMods = PlaySetEditor.Move(_allMods, index, target - index);
        RebuildVisibleMods();
        StatusMessage = T(Keys.ModsStatusOrderMoved, row.Name, index + 1, targetPosition);
        _logger.Info("播放集排序（jump）：「" + row.Name + "」第 " + (index + 1).ToString(CultureInfo.InvariantCulture)
            + " 位 → 第 " + targetPosition.ToString(CultureInfo.InvariantCulture) + " 位，共 "
            + count.ToString(CultureInfo.InvariantCulture) + " 行，激活集=" + (IsSelectedPlaySetActive ? "是" : "否")
            + "（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        RequestAutoSave();
        RowRevealRequested?.Invoke(row.ModId);
        return PositionEditResult.Applied;
    }

    /// <summary>启用全部 / 禁用全部（作用于可见行），随后自动保存。</summary>
    public void SetAllEnabled(bool enabled)
    {
        foreach (var row in Mods)
        {
            row.IsEnabled = enabled;
        }

        FlushVisibleRows();
        RaiseCounts();
        StatusMessage = enabled
            ? T(Keys.ModsStatusAllEnabled)
            : T(Keys.ModsStatusAllDisabled);
        RequestAutoSave();
    }

    /// <summary>把字节数格式化成 "1.2MB" 这样的短文本（与 Mod 库页同一格式）；未知返回 null。</summary>
    /// <param name="bytes">字节数，来自 mods.size。</param>
    public static string? FormatSize(long? bytes)
    {
        var text = ModSizeText.Format(bytes);
        return text.Length == 0 ? null : text;
    }

    private static Dictionary<Guid, ModLibraryEntry> BuildEntryMap(ModLibrarySnapshot snapshot)
    {
        var map = new Dictionary<Guid, ModLibraryEntry>(snapshot.Mods.Count);
        foreach (var entry in snapshot.Mods)
        {
            map[entry.ModId] = entry;
        }

        return map;
    }

    // ------------------------------------------------------------ 播放集管理

    /// <summary>新建播放集：创建后选中它（不自动激活），由用户点「激活播放集」决定是否应用。</summary>
    public async Task CreatePlaySetAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (Game is null)
        {
            StatusMessage = T(Keys.ModsStatusSelectGameFirst);
            return;
        }

        var trimmed = name.Trim();
        Guid id;
        try
        {
            IsBusy = true;
            var createdResult = await AppServices.PlaySets
                .CreatePlaySetAsync(Game, trimmed, cancellationToken)
                .ConfigureAwait(true);
            if (!createdResult.IsSuccess)
            {
                _logger.Warn("新建播放集被拒绝：" + createdResult.ErrorMessage);
                StatusMessage = T(Keys.ModsStatusCreateFailed, createdResult.ErrorMessage ?? string.Empty);
                return;
            }

            id = createdResult.Value;
            _logger.Info("已新建播放集「" + trimmed + "」（id=" + id.ToString("N") + "）");
            await LoadAsync(Game, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("新建播放集失败", ex);
            StatusMessage = T(Keys.ModsStatusCreateFailed, ex.Message);
            return;
        }
        finally
        {
            IsBusy = false;
        }

        var created = PlaySets.FirstOrDefault(p => p.Id == id);
        if (created is null)
        {
            return;
        }

        await SelectPlaySetAsync(created, cancellationToken).ConfigureAwait(true);
        if (SelectedPlaySet?.Id == id)
        {
            StatusMessage = T(Keys.ModsStatusCreated, trimmed);
        }
    }

    public async Task RenameSelectedAsync(string newName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        if (Game is null || SelectedPlaySet is null)
        {
            StatusMessage = T(Keys.ModsStatusSelectPlaySetFirst);
            return;
        }

        var playSetId = SelectedPlaySet.Id;
        IsBusy = true;
        try
        {
            var result = await _editor
                .RenamePlaySetAsync(Game, playSetId, newName, cancellationToken)
                .ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                StatusMessage = result.ErrorMessage ?? T(Keys.ModsStatusRenameFailed);
                return;
            }

            await LoadAsync(Game, cancellationToken).ConfigureAwait(true);
            var renamed = PlaySets.FirstOrDefault(p => p.Id == playSetId);
            if (renamed is not null)
            {
                await SelectPlaySetAsync(renamed, cancellationToken).ConfigureAwait(true);
            }

            StatusMessage = T(Keys.ModsStatusRenamed, newName.Trim());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 仓储已经改成返回 AppResult，正常失败不会走到这里；这里是兜底：
            // 旧版没有 catch，一次异常会直接冒到 async void 事件处理器，进程就没了。
            _logger.Error("重命名播放集失败（playSetId=" + playSetId.ToString("N") + "）", ex);
            StatusMessage = T(Keys.ModsStatusRenameFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 删除当前播放集（页面必须先做二次确认）。
    /// 删除非激活集不改变激活状态、不写加载文件；删除激活集时因仓库限制必须先激活一个接替者，
    /// 删除后把接替者的配置写入加载文件。只剩一个激活集时拒绝删除。
    /// </summary>
    public async Task DeleteSelectedAsync(CancellationToken cancellationToken)
    {
        if (Game is not { SupportsContentManagement: true } || SelectedPlaySet is null)
        {
            StatusMessage = T(Keys.ModsStatusSelectPlaySetFirst);
            return;
        }

        var playSet = SelectedPlaySet;
        var deletingActive = IsSelectedPlaySetActive;
        var fallback = deletingActive ? DeleteFallbackPlaySet : null;
        if (deletingActive && fallback is null)
        {
            StatusMessage = T(Keys.ModsStatusCannotDeleteLast);
            return;
        }

        IsBusy = true;
        try
        {
            await FlushPendingAutoSaveAsync(cancellationToken).ConfigureAwait(true);

            if (fallback is not null)
            {
                await AppServices.PlaySets
                    .SetActivePlaySetAsync(Game, fallback.Id, cancellationToken)
                    .ConfigureAwait(true);
            }

            var result = await _editor
                .DeletePlaySetAsync(Game, playSet.Id, cancellationToken)
                .ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                if (fallback is not null)
                {
                    // 删除失败时把激活标记还给原播放集，避免留下「已换激活集但没删成」的半状态。
                    try
                    {
                        await AppServices.PlaySets
                            .SetActivePlaySetAsync(Game, playSet.Id, cancellationToken)
                            .ConfigureAwait(true);
                    }
                    catch (Exception restoreEx) when (restoreEx is not OperationCanceledException)
                    {
                        _logger.Error(
                            "删除失败后恢复激活播放集失败（playSetId=" + playSet.Id.ToString("N") + "）",
                            restoreEx);
                    }
                }

                StatusMessage = result.ErrorMessage ?? T(Keys.ModsStatusDeleteFailed);
                return;
            }

            await LoadAsync(Game, cancellationToken).ConfigureAwait(true);

            var loadFileSaved = true;
            if (fallback is not null)
            {
                loadFileSaved = await SaveAsync(cancellationToken, writeLoadFile: true).ConfigureAwait(true);
            }

            StatusMessage = fallback is null
                ? T(Keys.ModsStatusDeletedInactive, playSet.Name)
                : loadFileSaved
                    ? T(Keys.ModsStatusDeletedActivated, playSet.Name, fallback.Name)
                    : T(Keys.ModsStatusDeletedActivatedNoLoadFile, playSet.Name, fallback.Name);
            _logger.Info("已删除播放集「" + playSet.Name + "」（id=" + playSet.Id.ToString("N") + "）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("删除播放集失败", ex);
            StatusMessage = T(Keys.ModsStatusDeleteError, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 切换当前查看/编辑的播放集：只加载选中集的 mod 行，不改变激活状态、不写 sqlite 或加载文件。
    /// 切换前先把待执行的自动保存落库，避免把旧集的编辑内容带到新集。
    /// </summary>
    public async Task SwitchPlaySetAsync(PlaySet playSet, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playSet);
        if (Game is not { SupportsContentManagement: true } || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await FlushPendingAutoSaveAsync(cancellationToken).ConfigureAwait(true);
            await SelectPlaySetAsync(playSet, cancellationToken).ConfigureAwait(true);
            // SelectPlaySetAsync 结束时会把 IsBusy 置回 false；整个切换流程结束前保持忙碌。
            IsBusy = true;

            StatusMessage = IsSelectedPlaySetActive
                ? T(Keys.ModsStatusSwitchedActive, playSet.Name)
                : T(Keys.ModsStatusSwitchedInactive, playSet.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("切换播放集失败（playSetId=" + playSet.Id.ToString("N") + "）", ex);
            StatusMessage = T(Keys.ModsStatusSwitchFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 把当前选中的播放集设为激活集：先保存它的 mod/dlc 行并写入游戏加载文件，再更新 sqlite 的 isActive。
    /// 先写加载文件后写激活标记，避免出现「数据库已激活、游戏配置却没写」的半状态。
    /// </summary>
    public async Task ActivateSelectedAsync(CancellationToken cancellationToken)
    {
        if (Game is not { SupportsContentManagement: true } || SelectedPlaySet is not { } playSet || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await FlushPendingAutoSaveAsync(cancellationToken).ConfigureAwait(true);

            var saved = await SaveAsync(cancellationToken, writeLoadFile: true).ConfigureAwait(true);
            if (!saved)
            {
                // 保留 SaveAsync 写入的具体原因（例如加载文件写入被拒绝），否则排障时只能看到笼统失败。
                // 前缀按当前语言现算，避免硬编码中文在切换语言后识别不到。
                var autoSavePrefix = T(Keys.ModsStatusAutoSaveFailed, string.Empty);
                var reason = StatusMessage is not null && StatusMessage.StartsWith(autoSavePrefix, StringComparison.Ordinal)
                    ? StatusMessage.Substring(autoSavePrefix.Length)
                    : StatusMessage;
                StatusMessage = string.IsNullOrWhiteSpace(reason)
                    ? T(Keys.ModsStatusActivateFailedNoSave)
                    : T(Keys.ModsStatusActivateFailedReason, reason);
                return;
            }

            await AppServices.PlaySets
                .SetActivePlaySetAsync(Game, playSet.Id, cancellationToken)
                .ConfigureAwait(true);
            ApplyActiveFlags(playSet.Id);
            StatusMessage = T(Keys.ModsStatusActivated, playSet.Name);
            _logger.Info("已激活播放集「" + playSet.Name + "」（id=" + playSet.Id.ToString("N") + "）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("激活播放集失败（playSetId=" + playSet.Id.ToString("N") + "）", ex);
            StatusMessage = T(Keys.ModsStatusActivateFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 扫一遍当前游戏的 Mod，找出能自动修的路径问题：订阅 mod 缺路径（按工坊目录补）、
    /// 本地 mod 路径含非 ASCII（游戏加载不了，改成 mod_&lt;id 前 8 位&gt;）。
    /// 只诊断不落盘 —— 由界面确认后再调 <see cref="ApplyPathRepairsAsync"/>。
    /// </summary>
    public async Task<IReadOnlyList<ModPathRepair>> FindPathRepairsAsync(CancellationToken cancellationToken)
    {
        if (Game is not { } game || IsBusy)
        {
            return Array.Empty<ModPathRepair>();
        }

        IsBusy = true;
        try
        {
            return await _pathRepairer.FindAsync(game, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("扫描 mod 路径问题失败（gameId=" + game.GameId + "）", ex);
            StatusMessage = T(Keys.ModsRepairFailed, ex.Message);
            return Array.Empty<ModPathRepair>();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>执行用户确认过的修复项：先改目录名再写库，然后刷新当前播放集并把加载文件重写一遍。</summary>
    public async Task ApplyPathRepairsAsync(IReadOnlyList<ModPathRepair> repairs, CancellationToken cancellationToken)
    {
        if (Game is not { } game || repairs.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await FlushPendingAutoSaveAsync(cancellationToken).ConfigureAwait(true);

            var applied = await _pathRepairer.ApplyAsync(game, repairs, cancellationToken).ConfigureAwait(true);
            if (!applied.IsSuccess)
            {
                StatusMessage = T(Keys.ModsRepairFailed, applied.ErrorMessage ?? T(Keys.ModsRepairNothing));
                return;
            }

            // 改名/补路径之后磁盘事实变了：重载当前播放集，再补一次保存好让加载文件跟上。
            var current = SelectedPlaySet;
            await SelectPlaySetAsync(current, cancellationToken).ConfigureAwait(true);
            await SaveAsync(cancellationToken, writeLoadFile: true).ConfigureAwait(true);
            StatusMessage = T(Keys.ModsRepairDone, applied.Value);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("修复 mod 路径失败（gameId=" + game.GameId + "）", ex);
            StatusMessage = T(Keys.ModsRepairFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>等待正在执行的自动保存完成，确保切换/激活/删除前旧集的编辑已落库。</summary>
    private async Task FlushPendingAutoSaveAsync(CancellationToken cancellationToken)
    {
        while (_autoSaveTask is { IsCompleted: false } task)
        {
            await task.WaitAsync(cancellationToken).ConfigureAwait(true);
        }
    }

    /// <summary>把 PlaySets 里的 IsActive 标记整体移到指定播放集（PlaySet 是不可变 record，需替换元素）。</summary>
    private void ApplyActiveFlags(Guid activeId)
    {
        for (var i = 0; i < PlaySets.Count; i++)
        {
            var set = PlaySets[i];
            var shouldBeActive = set.Id == activeId;
            if (set.IsActive != shouldBeActive)
            {
                PlaySets[i] = set with { IsActive = shouldBeActive };
            }
        }

        SyncSelectedPlaySetInstance();
        OnPropertyChanged(nameof(HasActivePlaySet));
        OnPropertyChanged(nameof(DeleteFallbackPlaySet));
        RaiseActivationState();
    }

    /// <summary>
    /// 把 SelectedPlaySet 同步到 PlaySets 中同 Id 的实例。PlaySet 是不可变 record，激活标记变化时
    /// 集合元素会被替换；不同步会让 ComboBox.SelectedItem 指向不存在的旧对象，选择框显示空白。
    /// </summary>
    private void SyncSelectedPlaySetInstance()
    {
        if (SelectedPlaySet is not { } selected)
        {
            return;
        }

        var current = PlaySets.FirstOrDefault(p => p.Id == selected.Id);
        if (current is null || ReferenceEquals(current, selected))
        {
            return;
        }

        // record 的值可能相等（只换了实例），SetProperty 会跳过；这里直接换字段并手动通知。
        _selectedPlaySet = current;
        OnPropertyChanged(nameof(SelectedPlaySet));
        RaiseSelectedPlaySetChanged();
    }

    // ------------------------------------------------------------ 自动保存

    /// <summary>
    /// 请求一次自动保存（只有原版启动器布局 + 支持内容管理的游戏 + 已选中播放集时才生效）。
    /// 多次连续改动只会排队合并，不会并发写盘。
    /// </summary>
    private void RequestAutoSave()
    {
        if (!IsOfficialLayout || Game is not { SupportsContentManagement: true } || SelectedPlaySet is null)
        {
            return;
        }

        _autoSavePending = true;
        if (_autoSaveTask is { IsCompleted: false })
        {
            return;
        }

        _autoSaveTask = RunAutoSaveLoopAsync();
    }

    /// <summary>自动保存循环：消耗 pending 标记，串行调用 <see cref="SaveAsync"/>。</summary>
    private async Task RunAutoSaveLoopAsync()
    {
        try
        {
            while (_autoSavePending)
            {
                _autoSavePending = false;
                await SaveAsync(CancellationToken.None).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("自动保存循环异常", ex);
            StatusMessage = T(Keys.ModsStatusAutoSaveFailed, ex.Message);
        }
    }

    /// <summary>
    /// 保存：写 sqlite（playsets_mods + playsets_dlcs）；只有选中集是激活集时才写加载文件并回读比对
    /// （<paramref name="writeLoadFile"/> 为空时按激活状态自动判断）。
    /// 由自动保存与「激活 / 删除播放集」调用；失败只更新状态栏，不抛给 UI，并通过返回值告知调用方。
    /// </summary>
    private async Task<bool> SaveAsync(CancellationToken cancellationToken, bool? writeLoadFile = null)
    {
        await _saveGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            var game = Game;
            var playSet = SelectedPlaySet;
            if (game is not { SupportsContentManagement: true } || playSet is null)
            {
                return true;
            }

            FlushVisibleRows();

            var shouldWriteLoadFile = writeLoadFile
                ?? PlaySets.Any(p => p.Id == playSet.Id && p.IsActive);

            // 官方启动器可能在我们加载之后改写过同一个播放集（关 mod、加 mod、换顺序）。
            // 写库是「DELETE 全表 + 逐行 INSERT」，直接用旧内存落盘会把官方改动整份盖掉；
            // 因此先比对库内现状：变了就放弃这次保存并重新加载，让用户在新的现状上再改一次。
            if (!await ConfirmNoExternalChangeAsync(game, playSet, cancellationToken).ConfigureAwait(true))
            {
                return false;
            }

            var savedDlcs = await AppServices.PlaySets
                .GetPlaySetDlcsAsync(game, playSet.Id, cancellationToken)
                .ConfigureAwait(true);
            var dlcCatalog = await AppServices.Dlcs
                .DiscoverAsync(game, cancellationToken)
                .ConfigureAwait(true);
            var dlcs = PlaySetEditor.MergeDlcs(savedDlcs, dlcCatalog);

            var result = await _editor
                .SaveAsync(game, playSet.Id, _allMods, dlcs, dlcCatalog, cancellationToken, shouldWriteLoadFile)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                StatusMessage = T(Keys.ModsStatusAutoSaveFailed, result.ErrorMessage ?? T(Keys.ModsStatusSaveFailed));
                _logger.Warn("ModsPage 自动保存失败（playSetId=" + playSet.Id.ToString("N") + "）：" + (result.ErrorMessage ?? "保存失败"));
                return false;
            }

            StatusMessage = shouldWriteLoadFile
                ? T(Keys.ModsStatusAutoSaveSaved)
                : T(Keys.ModsStatusAutoSaveSavedTo, playSet.Name);
            _logger.Info("ModsPage 自动保存成功（gameId=" + game.GameId + "，playSetId=" + playSet.Id.ToString("N")
                + "，写加载文件=" + shouldWriteLoadFile + "）");

            // 保存成功后库内就是这份内容，基线跟着前移，否则下一次保存会被自己刚才的写入判成「漂移」。
            _modsBaseline = PlaySetEditor.DescribePlaySetMods(_allMods);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("自动保存播放集失败（gameId=" + (Game?.GameId ?? "-") + "）", ex);
            StatusMessage = T(Keys.ModsStatusAutoSaveFailed, ex.Message);
            return false;
        }
        finally
        {
            _saveGate.Release();
        }
    }

    /// <summary>
    /// 保存前确认官方启动器没有在页面加载后改写同一个播放集。
    /// 返回 false 表示已放弃本次保存并重新加载了当前播放集。
    /// 只对「自己写 sqlite 行」的游戏生效（EU5 playsets.json / CS2 playset_config.json 是整份文档原子替换，
    /// 不存在逐行覆盖别人的问题）；没有基线（首次加载、读取失败）或比对读失败时一律放行，不因检测本身挡住用户。
    /// </summary>
    private async Task<bool> ConfirmNoExternalChangeAsync(
        GameInstallation game,
        PlaySet playSet,
        CancellationToken cancellationToken)
    {
        var baseline = _modsBaseline;
        if (baseline is null || !game.WritesLoadFile)
        {
            return true;
        }

        var drifted = await _editor
            .HasPlaySetDriftedAsync(game, playSet.Id, baseline, cancellationToken)
            .ConfigureAwait(true);
        if (!drifted)
        {
            return true;
        }

        _logger.Warn("检测到官方启动器改写了播放集，放弃本次保存并重新加载（gameId=" + game.GameId
            + "，playSetId=" + playSet.Id.ToString("N") + "）");
        await SelectPlaySetAsync(playSet, cancellationToken).ConfigureAwait(true);
        // 重载成功会把状态栏改成「已加载 N 个播放集」，所以提示放在重载之后。
        StatusMessage = T(Keys.ModsStatusExternalChange);
        return false;
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(VisibleModCount));
        OnPropertyChanged(nameof(EnabledModCount));
        OnPropertyChanged(nameof(CountsText));
        OnPropertyChanged(nameof(HasActivePlaySet));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsModListEmpty));
        OnPropertyChanged(nameof(DeleteFallbackPlaySet));
        RaiseActivationState();
    }

    /// <summary>通知与「当前激活播放集」相关的派生属性（激活按钮文字/可用性、头部激活摘要）。</summary>
    private void RaiseActivationState()
    {
        OnPropertyChanged(nameof(IsSelectedPlaySetActive));
        OnPropertyChanged(nameof(ActivePlaySet));
        OnPropertyChanged(nameof(ActivePlaySetSummary));
        OnPropertyChanged(nameof(CanActivateSelected));
        OnPropertyChanged(nameof(ActivateButtonText));
    }
}
