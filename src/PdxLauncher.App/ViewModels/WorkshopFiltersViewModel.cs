namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online;

/// <summary>一次目录应用的结果：是否重建、新增的兼容性标签、被移除的已选标签。</summary>
/// <param name="Changed">分组结构是否变化（没变时不会重建集合）。</param>
/// <param name="AddedCompatibilityTags">相比上一次新增的更新兼容性标签（如 1.14）。</param>
/// <param name="RemovedActiveTags">上一次已勾选、但新目录里已不存在的标签。</param>
public sealed record WorkshopFilterApplyResult(
    bool Changed,
    IReadOnlyList<string> AddedCompatibilityTags,
    IReadOnlyList<string> RemovedActiveTags);

/// <summary>
/// 创意工坊左侧筛选栏的状态：分组、包含/排除的标签、DLC、不兼容开关、特别筛选条件。
/// <para>
/// 分组数据由 <see cref="Apply"/> 注入（Steam 列表页 SSR 优先，静态目录兜底）；
/// 任何一个选项变化只抛一次 <see cref="Changed"/>，由 <see cref="WorkshopViewModel"/>
/// 去抖后重查 —— Steam 同主机最小间隔 1 秒，连点几个标签不能连发几个请求。
/// </para>
/// </summary>
public sealed partial class WorkshopFiltersViewModel : LocalizedObservableObject
{
    /// <summary>特别筛选条件与服务端参数的对应关系（实测：6 / 7）。</summary>
    private static readonly SteamWorkshopSpecialFilter[] SpecialFilters =
    [
        SteamWorkshopSpecialFilter.None,
        SteamWorkshopSpecialFilter.RequiredByOtherItems,
        SteamWorkshopSpecialFilter.InCollections,
    ];

    private static readonly string[] SpecialFilterKeys =
    [
        Keys.WorkshopFilterSpecialNone,
        Keys.WorkshopFilterSpecialRequiredByOthers,
        Keys.WorkshopFilterSpecialInCollections,
    ];

    private bool _suppressChanged;
    private IReadOnlyList<string> _specialFilterModes = [];
    private string _signature = string.Empty;

    /// <summary>创建空的筛选状态（构造后等 <see cref="Apply"/> 注入分组）。</summary>
    public WorkshopFiltersViewModel()
    {
        _specialFilterModes = BuildSpecialFilterModes();
    }

    /// <summary>分组（类别 / 更新兼容性 / 所需的 DLC）。</summary>
    public ObservableCollection<WorkshopFilterGroupViewModel> Groups { get; } = new();

    /// <summary>「特别筛选条件」下拉框的选项。</summary>
    public IReadOnlyList<string> SpecialFilterModes => _specialFilterModes;

    /// <summary>下拉框当前应显示的文字。</summary>
    public string CurrentSpecialFilterLabel => _specialFilterModes.Count == 0
        ? string.Empty
        : _specialFilterModes[Math.Clamp(SpecialFilterIndex, 0, _specialFilterModes.Count - 1)];

    /// <summary>当前特别筛选条件下标。</summary>
    [ObservableProperty]
    private int _specialFilterIndex;

    /// <summary>是否显示不兼容项目（<c>requiredflags[]=incompatible</c>）。</summary>
    [ObservableProperty]
    private bool _showIncompatible;

    /// <summary>是否有分组可显示（没有数据时整栏隐藏）。</summary>
    public bool HasGroups => Groups.Count > 0;

    /// <summary>已生效的筛选项数量（标签+DLC+不兼容+特别筛选，用于标题旁的数字）。</summary>
    public int AppliedCount =>
        Groups.Sum(static group => group.Options.Count(static option => option.IsActive))
        + (ShowIncompatible ? 1 : 0)
        + (SpecialFilterIndex == 0 ? 0 : 1);

    /// <summary>是否有任何筛选条件生效。</summary>
    public bool HasFilters => AppliedCount > 0;

    /// <summary>必须包含的标签（非 DLC 组）。</summary>
    public IReadOnlyList<string> RequiredTags => CollectValues(isRequired: true, dlc: false);

    /// <summary>必须排除的标签（非 DLC 组）。</summary>
    public IReadOnlyList<string> ExcludedTags => CollectValues(isRequired: false, dlc: false);

    /// <summary>必须依赖的 DLC appid。</summary>
    public IReadOnlyList<int> RequiredAppIds => CollectAppIds(isRequired: true);

    /// <summary>必须排除的 DLC appid。</summary>
    public IReadOnlyList<int> ExcludedAppIds => CollectAppIds(isRequired: false);

    /// <summary>当前特别筛选条件。</summary>
    public SteamWorkshopSpecialFilter SpecialFilter =>
        SpecialFilters[Math.Clamp(SpecialFilterIndex, 0, SpecialFilters.Length - 1)];

    /// <summary>筛选项变化（已由调用方去抖）。</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// 用新的分组数据重建侧栏。已勾选的标签/DLC 按值保留（SSR 每次刷新都会重建分组对象）。
    /// </summary>
    /// <param name="groups">分组数据。</param>
    public WorkshopFilterApplyResult Apply(IReadOnlyList<SteamWorkshopFilterGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);

        // 每次翻页都会带着同一份分组回来：结构没变就别重建集合，
        // 否则 DLC 的「按名称筛选」输入会在翻页后被清掉。
        var signature = WorkshopFilterCatalogPolicy.Signature(groups);
        if (string.Equals(signature, _signature, StringComparison.Ordinal))
        {
            return new WorkshopFilterApplyResult(false, [], []);
        }

        var hadPreviousGroups = Groups.Count > 0;
        var previousRequired = new HashSet<string>(StringComparer.Ordinal);
        var previousExcluded = new HashSet<string>(StringComparer.Ordinal);
        var previousActive = new HashSet<string>(StringComparer.Ordinal);
        var previousCompatibility = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in Groups)
        {
            foreach (var option in group.Options)
            {
                if (option.IsRequired)
                {
                    previousRequired.Add(option.Tag);
                    previousActive.Add(option.Tag);
                }
                else if (option.IsExcluded)
                {
                    previousExcluded.Add(option.Tag);
                    previousActive.Add(option.Tag);
                }
            }

            if (string.Equals(group.Key, "compatibility", StringComparison.Ordinal))
            {
                foreach (var option in group.Options)
                {
                    previousCompatibility.Add(option.Tag);
                }
            }
        }

        _signature = signature;

        _suppressChanged = true;
        try
        {
            Groups.Clear();
            foreach (var group in groups)
            {
                var viewModel = new WorkshopFilterGroupViewModel(
                    group.Key,
                    group.Title,
                    group.IsDlc,
                    group.Tags)
                {
                    Title = LocalizeGroupTitle(group.Key, group.Title),
                };
                viewModel.RestoreSelection(previousRequired, previousExcluded);
                viewModel.Changed += OnGroupChanged;
                Groups.Add(viewModel);
            }
        }
        finally
        {
            _suppressChanged = false;
        }

        RaiseSummary();
        return BuildApplyResult(groups, hadPreviousGroups, previousActive, previousCompatibility);
    }

    /// <summary>清空全部选择（含不兼容开关与特别筛选）。</summary>
    public void Clear()
    {
        Reset();
        RaiseChanged();
    }

    /// <summary>静默清空（换游戏/换来源时用，随后会由调用方重新查询，不需要再抛 Changed）。</summary>
    public void Reset()
    {
        _suppressChanged = true;
        try
        {
            foreach (var group in Groups)
            {
                group.Changed -= OnGroupChanged;
            }

            Groups.Clear();

            // 分组已经清空，下一次 Apply 必须重建（即使数据与上一游戏相同）。
            _signature = string.Empty;
            ShowIncompatible = false;
            SpecialFilterIndex = 0;
        }
        finally
        {
            _suppressChanged = false;
        }

        RaiseSummary();
    }

    /// <summary>把分组标题换成当前语言（选项状态保持不变）。</summary>
    protected override void Relocalize()
    {
        _suppressChanged = true;
        try
        {
            _specialFilterModes = BuildSpecialFilterModes();
            OnPropertyChanged(nameof(SpecialFilterModes));
            OnPropertyChanged(nameof(CurrentSpecialFilterLabel));
            foreach (var group in Groups)
            {
                group.Title = LocalizeGroupTitle(group.Key, group.RawTitle);
            }
        }
        finally
        {
            _suppressChanged = false;
        }
    }

    partial void OnShowIncompatibleChanged(bool value)
    {
        RaiseSummary();
        RaiseChanged();
    }

    partial void OnSpecialFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentSpecialFilterLabel));
        RaiseSummary();
        RaiseChanged();
    }

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        RaiseSummary();
        RaiseChanged();
    }

    private void RaiseSummary()
    {
        OnPropertyChanged(nameof(AppliedCount));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(HasGroups));
    }

    private void RaiseChanged()
    {
        if (_suppressChanged)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<string> CollectValues(bool isRequired, bool dlc) =>
        Groups
            .Where(group => group.IsDlc == dlc)
            .SelectMany(static group => group.Options)
            .Where(option => isRequired ? option.IsRequired : option.IsExcluded)
            .Select(static option => option.Tag)
            .ToList();

    private IReadOnlyList<int> CollectAppIds(bool isRequired) =>
        Groups
            .Where(static group => group.IsDlc)
            .SelectMany(static group => group.Options)
            .Where(option => isRequired ? option.IsRequired : option.IsExcluded)
            .Select(static option => int.TryParse(option.Tag, out var id) ? id : 0)
            .Where(static id => id > 0)
            .ToList();

    private static string LocalizeGroupTitle(string key, string rawTitle) => key switch
    {
        "category" => T(Keys.WorkshopFilterGroupCategory),
        "compatibility" => T(Keys.WorkshopFilterGroupCompatibility),
        "dlc" => T(Keys.WorkshopFilterGroupDlc),
        _ => LocalizeOtherGroup(key, rawTitle),
    };

    /// <summary>
    /// SSR 里没归类的分组：Cities: Skylines 的 «Assets» / «Misc» 也要有中文标题；
    /// 其它游戏将来新增的分组保底显示原标题，不至于变成空白。
    /// </summary>
    private static string LocalizeOtherGroup(string key, string rawTitle)
    {
        var title = key.StartsWith("other:", StringComparison.Ordinal) ? key["other:".Length..] : rawTitle;

        // UniquifyKey 可能给重复分组加后缀（other:Assets2），所以用前缀判断而不是全等。
        if (title.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
        {
            return T(Keys.WorkshopFilterGroupAssets);
        }

        if (title.StartsWith("Misc", StringComparison.OrdinalIgnoreCase))
        {
            return T(Keys.WorkshopFilterGroupMisc);
        }

        return string.IsNullOrWhiteSpace(rawTitle) ? T(Keys.WorkshopFilterGroupCategory) : rawTitle;
    }

    private static WorkshopFilterApplyResult BuildApplyResult(
        IReadOnlyList<SteamWorkshopFilterGroup> sourceGroups,
        bool hadPreviousGroups,
        IReadOnlySet<string> previousActive,
        IReadOnlySet<string> previousCompatibility)
    {
        var currentTags = sourceGroups
            .SelectMany(static group => group.Tags)
            .Select(static tag => tag.Tag)
            .ToHashSet(StringComparer.Ordinal);

        var removed = previousActive
            .Where(tag => !currentTags.Contains(tag))
            .ToList();

        var added = new List<string>();
        if (hadPreviousGroups)
        {
            var compatibility = sourceGroups.FirstOrDefault(static group =>
                string.Equals(group.Key, "compatibility", StringComparison.Ordinal));
            if (compatibility is not null)
            {
                added.AddRange(compatibility.Tags
                    .Select(static tag => tag.Tag)
                    .Where(tag => !previousCompatibility.Contains(tag)));
            }
        }

        return new WorkshopFilterApplyResult(true, added, removed);
    }

    private static string[] BuildSpecialFilterModes() =>
        SpecialFilterKeys.Select(static key => T(key)).ToArray();
}
