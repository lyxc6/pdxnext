namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Mods.Sorting;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Share;

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
    private bool _isAutoSortRunning;
    private bool _isLoaded;
    private string _statusMessage = string.Empty;
    private bool _translateFailureReported;
    private string _searchText = string.Empty;
    private bool _bulkEnabledUpdate;

    /// <summary>双列布局左栏（可添加的 mod）专用搜索词。</summary>
    private string _candidateSearchText = string.Empty;
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

    /// <summary>
    /// 自动排序应用前的顺序快照（含位次，可直接还原），只保留在内存。
    /// 切换或重载播放集即清空；列表内容在排序后有任何变化都会让签名失配，撤销随之失效。
    /// </summary>
    private AutoSortUndoSnapshot? _autoSortUndo;

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

    /// <summary>
    /// 双列布局左栏的候选行：mod 库里还没加入当前播放集的 mod（已按左栏搜索词过滤）。
    /// 勾选即加入——加入后这一行会从左栏消失并出现在右栏末尾。
    /// </summary>
    public ObservableCollection<AddModRowViewModel> Candidates { get; } = new();

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

    /// <summary>当前显示方式是否为双列布局（左＝可添加，右＝已在播放集）。</summary>
    public bool IsTwoColumnLayout =>
        string.Equals(_displayMode, PlaySetPageDisplayModes.TwoColumn, StringComparison.Ordinal);

    /// <summary>
    /// 工具条与状态栏是否显示：原版启动器与双列两种布局共用同一套播放集操作，
    /// 留空布局什么都不显示。页面根层用它控制这两块，避免把工具条复制两份。
    /// </summary>
    public bool ShowsPlaysetChrome => IsOfficialLayout || IsTwoColumnLayout;

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

    /// <summary>
    /// 自动排序扫描是否进行中。取消按钮绑定这个属性：IsBusy 期间所有编辑入口都被禁用，
    /// 若取消按钮也绑 IsEditorEnabled，就会在最需要它的时刻点不动。
    /// </summary>
    public bool IsAutoSortRunning
    {
        get => _isAutoSortRunning;
        private set => SetProperty(ref _isAutoSortRunning, value);
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

    /// <summary>双列布局左栏的搜索词；只影响 <see cref="Candidates"/>，与右栏搜索互不干扰。</summary>
    public string CandidateSearchText
    {
        get => _candidateSearchText;
        set
        {
            if (SetProperty(ref _candidateSearchText, value ?? string.Empty))
            {
                RebuildCandidates();
            }
        }
    }

    /// <summary>左栏候选计数，例如「共 12 个可添加的 mod」。带占位符的文案只在视图模型里拼。</summary>
    public string CandidateCountText => T(Keys.ModsAddPageCandidateCount, Candidates.Count);

    /// <summary>左栏列表是否可见（双列布局且左栏搜索框没被别的状态挤掉时恒为真）。</summary>
    public bool ShowCandidateList => Candidates.Count > 0;

    /// <summary>没有播放集时左栏显示「先选一个播放集」。</summary>
    public bool ShowCandidateNoPlaySet => IsTwoColumnLayout && SelectedPlaySet is null;

    /// <summary>左栏空状态：有播放集、没有搜索词、也确实没有可添加的 mod。</summary>
    public bool ShowCandidateEmpty =>
        IsTwoColumnLayout
        && SelectedPlaySet is not null
        && Candidates.Count == 0
        && string.IsNullOrWhiteSpace(CandidateSearchText);

    /// <summary>左栏无匹配：有播放集、有搜索词，但过滤后一行都不剩。</summary>
    public bool ShowCandidateNoMatch =>
        IsTwoColumnLayout
        && SelectedPlaySet is not null
        && Candidates.Count == 0
        && !string.IsNullOrWhiteSpace(CandidateSearchText);

    /// <summary>右栏没有播放集时的提示：两栏都空着，得先选一个播放集才知道右栏该显示什么。</summary>
    public bool ShowSelectedNoPlaySet => IsTwoColumnLayout && SelectedPlaySet is null;

    /// <summary>右栏空状态：播放集里确实没有 mod，且没有搜索词。</summary>
    public bool ShowSelectedEmpty =>
        IsTwoColumnLayout
        && SelectedPlaySet is not null
        && Mods.Count == 0
        && string.IsNullOrWhiteSpace(SearchText);

    /// <summary>右栏无匹配：播放集里有 mod，但当前搜索词把它们全滤掉了。</summary>
    public bool ShowSelectedNoMatch =>
        IsTwoColumnLayout
        && SelectedPlaySet is not null
        && Mods.Count == 0
        && !string.IsNullOrWhiteSpace(SearchText);

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

    /// <summary>是否可以一键撤销上一次自动排序。</summary>
    public bool HasAutoSortUndo => _autoSortUndo is not null;

    /// <summary>当前播放集的原始行（顺序即旧位次），供自动排序预览对话框对照「谁移到哪」。</summary>
    public IReadOnlyList<PlaySetMod> CurrentPlaySetMods => _allMods;

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
        // 自动排序的撤销快照与旧播放集绑定，切换/重载即作废，避免一键撤销还原到别的播放集的顺序。
        _autoSortUndo = null;
        OnPropertyChanged(nameof(HasAutoSortUndo));
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
            RebuildCandidates();
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
            RebuildCandidates();
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
        OnPropertyChanged(nameof(IsTwoColumnLayout));
        OnPropertyChanged(nameof(ShowsPlaysetChrome));
        // 切到双列时左栏要立刻有内容；切走时把候选清空，避免下次进来先闪一帧旧数据。
        RebuildCandidates();
    }

    /// <summary>选中一个播放集并加载它已保存的 mod 行（不再合并全库）。</summary>
    public async Task SelectPlaySetAsync(PlaySet? playSet, CancellationToken cancellationToken)
    {
        // 集合元素是不可变 record，激活标记变化时会被替换成新实例；这里按 Id 归一化，
        // 保证 SelectedPlaySet 始终是 ItemsSource 中的对象，下拉控件才能匹配到选中项。
        if (playSet is not null)
        {
            playSet = PlaySets.FirstOrDefault(p => p.Id == playSet.Id) ?? playSet;
        }

        SelectedPlaySet = playSet;
        // PlaySet 是值相等的 record：LoadAsync 里 PlaySets.Clear() 会让下拉控件的 SelectedItem
        // 掉成 null，随后即使重新选中"同一个播放集的新实例"，SetProperty 也会因值相等而跳过通知，
        // 单向 x:Bind 永远不回灌，选择框就一直空白（页面之前只有摘要文字有值，正是这个症状）。
        // 因此这里无条件补一次 PropertyChanged，保证下拉框每次都能显示当前播放集名。
        OnPropertyChanged(nameof(SelectedPlaySet));
        // 撤销快照只对它产生的播放集有效，换集即清空。
        _autoSortUndo = null;
        OnPropertyChanged(nameof(HasAutoSortUndo));
        if (Game is null || playSet is null)
        {
            _allMods = Array.Empty<PlaySetMod>();
            _modsBaseline = null;
            RebuildVisibleMods(cancellationToken);
            RebuildCandidates();
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

            RebuildVisibleMods(cancellationToken);

            // mod 库只用于补全大小与「添加模组」候选；读失败不影响已保存的播放集内容。
            try
            {
                _librarySnapshot = await _modLibrary
                    .LoadAsync(Game, cancellationToken)
                    .ConfigureAwait(true);
                _entryByModId = BuildEntryMap(_librarySnapshot);
                RebuildVisibleMods(cancellationToken);
                RebuildCandidates();
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
            RebuildVisibleMods(cancellationToken);
            RebuildCandidates();
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
        var addedMods = new List<PlaySetMod>();
        foreach (var entry in entries)
        {
            if (entry is null || !existingIds.Add(entry.ModId))
            {
                continue;
            }

            var mod = new PlaySetMod(
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
                entry.SteamId);
            appended.Add(mod);
            addedMods.Add(mod);
        }

        if (addedMods.Count == 0)
        {
            StatusMessage = T(Keys.ModsStatusNoModsAdded);
            return;
        }

        _allMods = PlaySetEditor.Renumber(appended);

        // 增量更新：只把新增的行接到右栏末尾，再把对应候选行从左栏摘掉。
        // 之前是 Clear() + 全量重建，每加一个 mod 都要重造两栏所有行 —— 缩略图重新解码、
        // 滚动条跳回顶部、开关动画重放，看上去就是整页闪一下。
        var canMove = CanMoveMods;
        foreach (var mod in addedMods.Where(IsVisibleUnderSearch))
        {
            Mods.Add(CreateRow(mod, Mods.Count, canMove));
        }

        RemoveCandidateRows(addedMods.Select(static m => m.ModId).ToHashSet());
        RefreshRowPositions();
        RaiseCounts();
        RaiseCandidateState();
        StartNameTranslation(CancellationToken.None);
        _logger.Info("播放集添加 " + addedMods.Count.ToString(CultureInfo.InvariantCulture) + " 个 mod（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        StatusMessage = T(Keys.ModsStatusModsAdded, addedMods.Count);
        RequestAutoSave();
    }

    /// <summary>从当前播放集中移除一行（随后自动保存）。搜索过滤下也允许。</summary>
    public void RemoveMod(ModRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        FlushVisibleRows();

        var removed = _allMods.FirstOrDefault(m => m.ModId == row.ModId);
        var remaining = _allMods.Where(m => m.ModId != row.ModId).ToList();
        if (removed is null || remaining.Count == _allMods.Count)
        {
            return;
        }

        _allMods = PlaySetEditor.Renumber(remaining);

        // 增量更新：只摘掉这一行，再把它按 mod 库顺序插回左栏；不重建两侧列表。
        Mods.Remove(row);
        RefreshRowPositions();
        InsertCandidate(removed);
        RaiseCounts();
        RaiseCandidateState();
        _logger.Info("播放集移除 mod「" + row.Name + "」（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        StatusMessage = T(Keys.ModsStatusModRemoved, row.Name);
        RequestAutoSave();
    }

    // ------------------------------------------------------------ 编辑

    /// <summary>按搜索词重建可见行；行顺序即 _allMods 中 position 顺序。</summary>
    /// <param name="cancellationToken">页面生命周期取消令牌（供后台译名使用）。</param>
    private void RebuildVisibleMods(CancellationToken cancellationToken = default)
    {
        var filtered = PlaySetEditor.Filter(_allMods, SearchText);
        var canMove = CanMoveMods;
        Mods.Clear();
        foreach (var mod in filtered)
        {
            Mods.Add(CreateRow(mod, filtered.Count, canMove));
        }

        RaiseCounts();
        StartNameTranslation(cancellationToken);
    }

    /// <summary>
    /// 按一行 Core 契约造行视图模型：缩略图边长、来源徽标、加载警告、位次与总数都在这里补齐。
    /// 全量重建与增量增删共用它，避免两条路径各写一份「行长什么样」而慢慢走偏。
    /// </summary>
    private ModRowViewModel CreateRow(PlaySetMod mod, int totalCount, bool canMove)
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

        var sizeText = _entryByModId.TryGetValue(mod.ModId, out var entry)
            ? FormatSize(entry.SizeBytes)
            : null;
        row.SetDetails(
            sizeText,
            T(ModOrigins.DescribeKey(origin)),
            origin == ModOrigin.Local,
            warningKey is null ? null : T(warningKey));

        row.TotalCount = totalCount;
        row.CanMove = canMove;
        return row;
    }

    /// <summary>
    /// 增删 / 换位后就地刷新每行的位次与总行数。`_allMods` 是唯一真源：可见行按它在
    /// `_allMods` 里的下标写 Position —— 搜索过滤时可见下标 ≠ 真实下标，所以不能拿 `Mods.IndexOf`。
    /// </summary>
    private void RefreshRowPositions()
    {
        var indexById = new Dictionary<Guid, int>(_allMods.Count);
        for (var i = 0; i < _allMods.Count; i++)
        {
            indexById[_allMods[i].ModId] = i;
        }

        var total = Mods.Count;
        foreach (var row in Mods)
        {
            if (indexById.TryGetValue(row.ModId, out var position))
            {
                row.Position = position;
            }

            row.TotalCount = total;
        }
    }

    /// <summary>这一行在当前搜索词下是否该出现在右栏（与 <see cref="PlaySetEditor.Filter"/> 同口径）。</summary>
    private bool IsVisibleUnderSearch(PlaySetMod mod) =>
        string.IsNullOrWhiteSpace(SearchText)
        || PlaySetEditor.Filter(new[] { mod }, SearchText).Count > 0;

    /// <summary>从左栏摘掉这些 ModId 的候选行：它们刚被加进播放集，不该再出现在「可添加」里。</summary>
    private void RemoveCandidateRows(IReadOnlySet<Guid> modIds)
    {
        for (var i = Candidates.Count - 1; i >= 0; i--)
        {
            if (modIds.Contains(Candidates[i].ModId))
            {
                Candidates.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 把刚移出播放集的 mod 按 mod 库顺序插回左栏（字母序，不是塞到末尾）。
    /// 库里查不到条目就什么都不做：候选行必须由 <see cref="ModLibraryEntry"/> 构造，
    /// 库外条目（mod 目录已被删）本来就拼不出来。
    /// </summary>
    private void InsertCandidate(PlaySetMod mod)
    {
        if (!IsTwoColumnLayout
            || SelectedPlaySet is null
            || !_entryByModId.TryGetValue(mod.ModId, out var entry))
        {
            return;
        }

        var candidate = new AddModRowViewModel(
            entry,
            onSelectionChanged: null,
            automationPrefix: "modCandidate");
        var query = CandidateSearchText.Trim();
        if (query.Length > 0 && !candidate.Matches(query))
        {
            return;
        }

        // 插入点 = 快照里排在它前面的候选数：快照已按名称排序，下标可直接比大小。
        var order = new Dictionary<Guid, int>(_librarySnapshot.Mods.Count);
        for (var i = 0; i < _librarySnapshot.Mods.Count; i++)
        {
            order[_librarySnapshot.Mods[i].ModId] = i;
        }

        if (!order.TryGetValue(mod.ModId, out var target))
        {
            return;
        }

        var insertAt = Candidates.Count;
        for (var i = 0; i < Candidates.Count; i++)
        {
            if (!order.TryGetValue(Candidates[i].ModId, out var index) || index > target)
            {
                insertAt = i;
                break;
            }
        }

        Candidates.Insert(insertAt, candidate);
    }

    // ------------------------------------------------------------ 双列布局的左栏

    /// <summary>
    /// 重建双列布局左栏的候选行：mod 库快照减去当前播放集已有的 ModId，再按左栏搜索词过滤。
    /// </summary>
    /// <remarks>
    /// 只在「成员集合变化」的地方调用（选播放集 / 加入 / 移除 / 切换布局），不挂在
    /// <see cref="RebuildVisibleMods"/> 上：后者每敲一个字、每次自动保存都会跑，跟着重建会把
    /// 左栏滚动位置和「刚点下去那一行」的勾选态一起丢掉。
    /// </remarks>
    private void RebuildCandidates()
    {
        Candidates.Clear();
        if (!IsTwoColumnLayout || SelectedPlaySet is null || _librarySnapshot.Mods.Count == 0)
        {
            RaiseCandidateState();
            return;
        }

        // 与 AddMods 的去重口径一致：按 ModId 判断是否已在播放集里。
        var existingIds = _allMods.Select(static m => m.ModId).ToHashSet();
        var query = CandidateSearchText.Trim();
        foreach (var entry in _librarySnapshot.Mods)
        {
            if (existingIds.Contains(entry.ModId))
            {
                continue;
            }

            // 前缀 modCandidate：与「添加模组」子页的 addModCandidate 区分，UIA 脚本不会串。
            var row = new AddModRowViewModel(
                entry,
                onSelectionChanged: null,
                automationPrefix: "modCandidate");
            if (query.Length > 0 && !row.Matches(query))
            {
                continue;
            }

            Candidates.Add(row);
        }

        RaiseCandidateState();
    }

    /// <summary>通知左栏计数/空状态与右栏空/无匹配状态。</summary>
    private void RaiseCandidateState()
    {
        OnPropertyChanged(nameof(CandidateCountText));
        OnPropertyChanged(nameof(ShowCandidateList));
        OnPropertyChanged(nameof(ShowCandidateNoPlaySet));
        OnPropertyChanged(nameof(ShowCandidateEmpty));
        OnPropertyChanged(nameof(ShowCandidateNoMatch));
        OnPropertyChanged(nameof(ShowSelectedNoPlaySet));
        OnPropertyChanged(nameof(ShowSelectedEmpty));
        OnPropertyChanged(nameof(ShowSelectedNoMatch));
    }

    /// <summary>
    /// 后台给可见行补译名：翻译是网络操作，不能挡着列表显示。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    private void StartNameTranslation(CancellationToken cancellationToken)
    {
        if (!AppServices.Settings.TranslateNamesEnabled || !AppServices.Translation.IsEnabled)
        {
            return;
        }

        // 只翻还没有译名的行：增量加入时列表里绝大多数行已经翻过，重翻一遍既浪费额度，
        // 也会让已有译文先空一下再回填（视觉上又是一次闪动）。
        var pending = Mods
            .Where(static row => !string.IsNullOrWhiteSpace(row.Name) && string.IsNullOrWhiteSpace(row.TranslatedName))
            .ToList();
        if (pending.Count == 0)
        {
            return;
        }

        // 新一轮加载重新给一次提示机会。
        _translateFailureReported = false;
        _ = TranslateNamesAsync(pending, cancellationToken);
    }

    /// <summary>整页名称一次性批翻（引擎内部并发），译完统一回填。</summary>
    private async Task TranslateNamesAsync(
        IReadOnlyList<ModRowViewModel> rows,
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
                ReportTranslationFailure(rows.Count);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面已关闭或设置已变更：这批译文作废，不记错误。
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 名称翻译失败：" + ex.Message);
        }
    }

    /// <summary>整页一条译文都没拿到时，在底部状态条说明原因（同一次加载只提示一次）。</summary>
    private void ReportTranslationFailure(int count)
    {
        if (_translateFailureReported)
        {
            return;
        }

        _translateFailureReported = true;
        var reason = AppServices.Translation.LastFailure ?? T(Keys.CommonUnknown);
        _logger.Warn("Mod 名称全部未翻译（" + count + " 条）：" + reason);
        StatusMessage = T(Keys.ModsTranslateUnavailable, reason);
    }

    /// <summary>行内开关变化：同步集合顺序（不重排），刷新计数与状态，并自动保存。</summary>
    private void OnRowEnabledChanged(ModRowViewModel row)
    {
        if (_bulkEnabledUpdate)
        {
            return;
        }

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

    /// <summary>
    /// 只同步「启用/禁用」状态，保持 <see cref="_allMods"/> 的原始顺序不变。
    /// <para>
    /// 搜索过滤时 <see cref="Mods"/> 只是子集，不能像 <see cref="FlushVisibleRows"/> 那样
    /// 把可见行排到最前；否则开关一个 mod 就会悄悄改掉真实加载顺序并写进加载文件。
    /// </para>
    /// </summary>
    private void SyncVisibleEnabledStateIntoAllMods()
    {
        if (Mods.Count == 0 || _allMods.Count == 0)
        {
            return;
        }

        var visibleStates = new Dictionary<Guid, bool>(Mods.Count);
        foreach (var row in Mods)
        {
            visibleStates[row.ModId] = row.IsEnabled;
        }

        _allMods = _allMods
            .Select(item => visibleStates.TryGetValue(item.ModId, out var enabled) && item.Enabled != enabled
                ? item with { Enabled = enabled }
                : item)
            .ToList();
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

        // 只移动这一行，不重建列表（搜索过滤下禁止移动，所以可见下标就是真实下标）。
        Mods.Move(index, target);
        RefreshRowPositions();
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

        // 同 MoveMod：就地换位，不重建列表。
        Mods.Move(index, target);
        RefreshRowPositions();
        StatusMessage = T(Keys.ModsStatusOrderMoved, row.Name, index + 1, targetPosition);
        _logger.Info("播放集排序（jump）：「" + row.Name + "」第 " + (index + 1).ToString(CultureInfo.InvariantCulture)
            + " 位 → 第 " + targetPosition.ToString(CultureInfo.InvariantCulture) + " 位，共 "
            + count.ToString(CultureInfo.InvariantCulture) + " 行，激活集=" + (IsSelectedPlaySetActive ? "是" : "否")
            + "（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        RequestAutoSave();
        RowRevealRequested?.Invoke(row.ModId);
        return PositionEditResult.Applied;
    }

    // ------------------------------------------------------------ 行右键菜单的状态栏反馈

    /// <summary>右键菜单「打开本地文件夹」成功后写状态栏。</summary>
    public void ReportOpenedFolder(ModRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        StatusMessage = T(Keys.ModsStatusOpenedFolder, row.Name);
    }

    /// <summary>行既没有目录也没有压缩包时写状态栏；菜单项通常已被 HasLocalFiles 禁用，这里兜底。</summary>
    public void ReportNoLocalFiles() => StatusMessage = T(Keys.ModsStatusNoLocalFiles);

    /// <summary>行没有可用的创意工坊 id 时写状态栏（复用 Mod 库页的现成文案）。</summary>
    public void ReportNoWorkshopAddress() => StatusMessage = T(Keys.ModlibStatusNoWorkshopAddress);

    /// <summary>右键菜单「复制注册 ID」成功后写状态栏。</summary>
    public void ReportClipboardCopied(ModRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        StatusMessage = T(Keys.ModsStatusCopiedId, row.Name);
    }

    /// <summary>启用全部 / 禁用全部（作用于整个播放集，搜索过滤也不例外），随后自动保存。</summary>
    public void SetAllEnabled(bool enabled)
    {
        // 行模型回调会逐条触发自动保存；这里统一改完再保存一次。
        _bulkEnabledUpdate = true;
        try
        {
            _allMods = _allMods
                .Select(item => item.Enabled == enabled ? item : item with { Enabled = enabled })
                .ToList();

            foreach (var row in Mods)
            {
                row.IsEnabled = enabled;
            }
        }
        finally
        {
            _bulkEnabledUpdate = false;
        }

        RaiseCounts();
        StatusMessage = enabled
            ? T(Keys.ModsStatusAllEnabled)
            : T(Keys.ModsStatusAllDisabled);
        RequestAutoSave();
    }

    // ------------------------------------------------------------ 自动排序

    /// <summary>
    /// 计算自动排序预览：扫描各 mod 的实际文件改动并算出新顺序，只读不落库。
    /// 未选游戏/未选播放集时置状态栏并返回 null；扫描期间置忙，防止中途编辑；
    /// 用户取消时返回 null 并把状态栏改成「已取消」。
    /// </summary>
    /// <param name="cancellationToken">页面生命周期与取消按钮合并后的令牌。</param>
    /// <param name="progress">扫描进度（原版索引 / 逐 mod 扫描 / 建图），回调在 UI 线程。</param>
    public async Task<AutoSortPreview?> BuildAutoSortPreviewAsync(
        CancellationToken cancellationToken,
        IProgress<AutoSortProgress>? progress = null)
    {
        var game = Game;
        if (game is null)
        {
            StatusMessage = T(Keys.ModsStatusAutoSortNeedsGame);
            return null;
        }

        // 没有播放集时列表本来就是空的：此时点「自动排序」会得到「无需调整」，
        // 那是把「没东西可排」说成了「排过了没问题」。这里直接说清楚缺的是什么。
        var playSet = SelectedPlaySet;
        if (playSet is null)
        {
            StatusMessage = T(Keys.ModsStatusAutoSortNeedsPlaySet);
            return null;
        }

        IsAutoSortRunning = true;
        IsBusy = true;
        try
        {
            var signature = PlaySetAutoSortService.ComputeInputSignature(_allMods);
            var preview = await PlaySetAutoSortService
                .BuildPreviewAsync(AppServices.FileSystem, game, _allMods, _logger, cancellationToken, progress)
                .ConfigureAwait(true);

            // 预览与「应用」之间隔着一次对话框确认。把当时的播放集与输入指纹钉进预览，
            // 应用前重签比对：这期间列表若被自动保存/刷新改过，宁可作废也不覆盖。
            preview = preview with { PlaySetId = playSet.Id, InputSignature = signature };
            var moved = preview.Result.Entries.Count(e => e.OldIndex != e.NewIndex);
            _logger.Info("自动排序（preview）：共 " + _allMods.Count.ToString(CultureInfo.InvariantCulture)
                + " 个 mod，移动 " + moved.ToString(CultureInfo.InvariantCulture)
                + " 个，冲突 " + preview.Conflicts.Count.ToString(CultureInfo.InvariantCulture)
                + " 组，警告 " + preview.Result.Warnings.Count.ToString(CultureInfo.InvariantCulture)
                + " 条（playSetId=" + playSet.Id.ToString("N") + "）");
            return preview;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = T(Keys.ModsAutoSortCancelled);
            _logger.Info("自动排序（preview）：已取消（playSetId=" + playSet.Id.ToString("N") + "）");
            return null;
        }
        finally
        {
            IsBusy = false;
            IsAutoSortRunning = false;
        }
    }

    /// <summary>
    /// 把扫描进度写进状态栏。页面用 <see cref="CreateAutoSortProgress"/> 拿到回调对象，
    /// 所有文案格式都留在这一层，进度来源换实现时不用动视图。
    /// </summary>
    public void ReportAutoSortProgress(AutoSortProgress progress)
    {
        StatusMessage = progress.Phase switch
        {
            AutoSortPhase.VanillaIndex => T(Keys.ModsAutoSortProgressVanilla, progress.Completed),
            AutoSortPhase.ScanningMods => T(
                Keys.ModsAutoSortProgressScan,
                progress.Completed + 1,
                progress.Total,
                progress.CurrentModName ?? string.Empty),
            _ => T(Keys.ModsAutoSortProgressGraph),
        };
    }

    /// <summary>
    /// 生成进度回调。必须在 UI 线程上构造 <see cref="Progress{T}"/>，
    /// 否则回调会在线程池上改状态栏（绑定属性变更不会回到 UI 线程）。
    /// </summary>
    public IProgress<AutoSortProgress> CreateAutoSortProgress() =>
        new Progress<AutoSortProgress>(ReportAutoSortProgress);

    /// <summary>
    /// 应用预览给出的新顺序：先把当前顺序存成撤销快照，再整体替换并走自动保存通道。
    /// 只改顺序，不碰启用状态与其它行内容。
    /// </summary>
    /// <returns>预览已过期（播放集换了，或列表内容/启用态变了）时返回 false，不改任何行。</returns>
    public bool ApplyAutoSort(AutoSortPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);

        if (preview.PlaySetId != SelectedPlaySet?.Id || preview.IsStaleFor(_allMods))
        {
            StatusMessage = T(Keys.ModsAutoSortStale);
            _logger.Info("自动排序（apply）：预览已作废，拒绝应用（playSetId="
                + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
            return false;
        }

        // 快照记住「排序前的顺序」，签名记住「排序后（=当前）的列表」：
        // 撤销时只有列表仍停在这份排序结果上才允许还原。
        var sorted = PlaySetEditor.Renumber(preview.ReorderedRows);
        _autoSortUndo = new AutoSortUndoSnapshot(
            _allMods,
            PlaySetAutoSortService.ComputeInputSignature(sorted));
        _allMods = sorted;
        RebuildVisibleMods();

        var moved = preview.Result.Entries.Count(e => e.OldIndex != e.NewIndex);
        StatusMessage = T(Keys.ModsStatusAutoSortApplied, moved);
        OnPropertyChanged(nameof(HasAutoSortUndo));
        _logger.Info("自动排序（apply）：移动 " + moved.ToString(CultureInfo.InvariantCulture)
            + " 个 mod（playSetId=" + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        RequestAutoSave();
        return true;
    }

    /// <summary>一键撤销上一次自动排序（还原应用前的顺序）；没有快照或列表已变时返回 false。</summary>
    public bool UndoAutoSort()
    {
        if (_autoSortUndo is null)
        {
            return false;
        }

        // 排序之后若增删了 mod / 改过启用态，旧顺序已经不再描述「当前这份列表的本来面目」：
        // 强行还原会删掉新加的 mod、复活已删的 mod，并随自动保存落库。宁可作废。
        if (!_autoSortUndo.IsValidFor(_allMods))
        {
            _autoSortUndo = null;
            StatusMessage = T(Keys.ModsAutoSortUndoStale);
            OnPropertyChanged(nameof(HasAutoSortUndo));
            _logger.Info("自动排序（undo）：列表已在排序后发生变化，撤销快照作废");
            return false;
        }

        _allMods = PlaySetEditor.Renumber(_autoSortUndo.Rows);
        _autoSortUndo = null;
        RebuildVisibleMods();
        StatusMessage = T(Keys.ModsStatusAutoSortUndone);
        OnPropertyChanged(nameof(HasAutoSortUndo));
        _logger.Info("自动排序（undo）：已还原自动排序前的顺序（playSetId="
            + (SelectedPlaySet?.Id.ToString("N") ?? "-") + "）");
        RequestAutoSave();
        return true;
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

    // ------------------------------------------------------------ 导入 / 导出（官方分享格式）

    /// <summary>
    /// 只解析分享文件，不写库：先让页面判断本地是否已有同名播放集，再决定合并还是新建副本。
    /// </summary>
    public async Task<AppResult<PlaySetSharePeek>> PeekImportAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null)
        {
            return AppResult<PlaySetSharePeek>.Fail(ErrorKind.Invalid, "share-no-game");
        }

        return await AppServices.PlaySetShare
            .PeekAsync(game, sourcePath, cancellationToken)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// 导入官方分享文件。成功后重新加载播放集列表并选中目标播放集；
    /// <paramref name="mergeIntoPlaySetId"/> 非空时合并进已有播放集（补齐缺失条目）。
    /// </summary>
    public async Task<AppResult<PlaySetShareImportResult>> ImportPlaySetAsync(
        string sourcePath,
        Guid? mergeIntoPlaySetId,
        CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null)
        {
            return AppResult<PlaySetShareImportResult>.Fail(ErrorKind.Invalid, "share-no-game");
        }

        try
        {
            var result = await AppServices.PlaySetShare
                .ImportAsync(game, sourcePath, mergeIntoPlaySetId, cancellationToken)
                .ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                _logger.Warn("导入播放集失败（gameId=" + game.GameId + "）："
                    + (result.ErrorMessage ?? result.Kind.ToString()));
                return result;
            }

            if (result.Value is not { } imported)
            {
                return result;
            }

            await LoadAsync(game, cancellationToken).ConfigureAwait(true);
            var target = PlaySets.FirstOrDefault(candidate => candidate.Id == imported.PlaySetId);
            if (target is not null)
            {
                await SelectPlaySetAsync(target, cancellationToken).ConfigureAwait(true);

                // 合并进当前激活播放集时，库里的 mod 变了但游戏加载文件还是旧的；
                // 不补写的话用户导入后直接启动游戏，看不到新增 mod。
                if (imported.Merged && target.IsActive)
                {
                    var written = await ActivePlaySetLoadFileWriter
                        .WriteAsync(game, cancellationToken)
                        .ConfigureAwait(true);
                    if (!written.IsSuccess)
                    {
                        _logger.Warn("导入合并后写加载文件失败（gameId=" + game.GameId + "）："
                            + (written.ErrorMessage ?? "unknown"));
                        StatusMessage = T(Keys.ModsStatusSaveFailed);
                        return result;
                    }
                }
            }

            StatusMessage = imported.Merged
                ? T(Keys.ModsStatusImportMerged, imported.PlaySetName, imported.AddedMods)
                : T(Keys.ModsStatusImportDone, imported.PlaySetName, imported.ImportedMods);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("导入播放集失败（gameId=" + game.GameId + "）", ex);
            StatusMessage = T(Keys.ModsStatusImportFailed, ex.Message);
            return AppResult<PlaySetShareImportResult>.Fail(ErrorKind.Io, ex.Message);
        }
    }

    /// <summary>
    /// 导出当前选中的播放集。先等未完成的自动保存落盘，避免导出到旧的加载顺序。
    /// </summary>
    public async Task<AppResult<PlaySetShareExportResult>> ExportSelectedAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var game = Game;
        var playSet = SelectedPlaySet;
        if (game is null || playSet is null)
        {
            return AppResult<PlaySetShareExportResult>.Fail(ErrorKind.Invalid, "share-no-selection");
        }

        try
        {
            await FlushPendingAutoSaveAsync(cancellationToken).ConfigureAwait(true);

            var result = await AppServices.PlaySetShare
                .ExportAsync(game, playSet.Id, destinationPath, cancellationToken)
                .ConfigureAwait(true);
            if (!result.IsSuccess)
            {
                _logger.Warn("导出播放集失败（gameId=" + game.GameId + "）："
                    + (result.ErrorMessage ?? result.Kind.ToString()));
                return result;
            }

            if (result.Value is not { } exported)
            {
                return result;
            }

            StatusMessage = T(Keys.ModsStatusExportDone, playSet.Name, exported.ExportedMods);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("导出播放集失败（gameId=" + game.GameId + "）", ex);
            StatusMessage = T(Keys.ModsStatusExportFailed, ex.Message);
            return AppResult<PlaySetShareExportResult>.Fail(ErrorKind.Io, ex.Message);
        }
    }

    /// <summary>
    /// 一键订阅导入结果里缺失的 Steam 模组。订阅只是让 Steam 开始下载，
    /// 下载完成后用户需要再导入同一份文件，条目才会补齐。
    /// <para>Steam 写操作受设置开关控制；未开启或后端不可用时只更新状态栏，不抛异常。</para>
    /// </summary>
    /// <returns>成功提交订阅的条数。</returns>
    public async Task<int> SubscribeMissingSteamModsAsync(
        IReadOnlyList<PlaySetShareMissingMod> missing,
        CancellationToken cancellationToken)
    {
        var game = Game;
        if (game is null)
        {
            return 0;
        }

        if (!AppServices.Settings.SteamWorkshopSubscribeEnabled)
        {
            StatusMessage = T(Keys.ModsImportSubscribeNeedsSetting);
            return 0;
        }

        var backend = SteamWorkshopBackends.CreateForSubscribe(game);
        if (backend is null || !backend.IsAvailable)
        {
            StatusMessage = T(Keys.ModsImportSubscribeUnavailable, backend?.UnavailableReason ?? string.Empty);
            return 0;
        }

        var ids = missing
            .Where(static mod => PdxModIds.LooksNumeric(mod.SteamId))
            .Select(static mod => ulong.Parse(mod.SteamId!, CultureInfo.InvariantCulture))
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            StatusMessage = T(Keys.ModsImportNoSteamMods);
            return 0;
        }

        var succeeded = 0;
        var failed = 0;
        for (var index = 0; index < ids.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusMessage = T(Keys.ModsImportSubscribing, index + 1, ids.Count);

            var outcome = await backend
                .SetSubscribedAsync(ids[index], subscribe: true, cancellationToken)
                .ConfigureAwait(true);
            if (outcome.Ok)
            {
                succeeded++;
            }
            else
            {
                failed++;
                _logger.Warn("订阅缺失的工坊 mod 失败（id=" + ids[index] + "）：" + outcome.ErrorText);
            }
        }

        StatusMessage = failed == 0
            ? T(Keys.ModsImportSubscribeDone, succeeded)
            : T(Keys.ModsImportSubscribePartial, succeeded, failed);
        return succeeded;
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
    /// 集合元素会被替换；不同步会让下拉控件的 SelectedItem 指向不存在的旧对象，选择框显示空白。
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
        if (!ShowsPlaysetChrome || Game is not { SupportsContentManagement: true } || SelectedPlaySet is null)
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

            // 只同步开关状态，保持 _allMods 原始顺序；搜索过滤时不能把可见行排到最前。
            SyncVisibleEnabledStateIntoAllMods();

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
        // 右栏（双列）的空 / 无匹配状态跟着可见行数走；左栏计数也在这里顺手刷一遍。
        OnPropertyChanged(nameof(ShowSelectedNoPlaySet));
        OnPropertyChanged(nameof(ShowSelectedEmpty));
        OnPropertyChanged(nameof(ShowSelectedNoMatch));
        OnPropertyChanged(nameof(CandidateCountText));
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
