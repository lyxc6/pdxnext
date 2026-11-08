namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online;

/// <summary>
/// 筛选栏的一个分组（类别 / 更新兼容性 / 所需的 DLC）。
/// <para>
/// DLC 组通常有 20 项以上，额外支持一个「按名称筛选」输入框：它只过滤本地列表，
/// 不改服务端查询。分组本身只把选项变化聚合成 <see cref="Changed"/>，
/// 由 <see cref="WorkshopFiltersViewModel"/> 统一去抖。
/// </para>
/// </summary>
public sealed partial class WorkshopFilterGroupViewModel : LocalizedObservableObject
{
    private bool _suppressChanged;

    /// <summary>创建分组。</summary>
    /// <param name="key">稳定键（category / compatibility / dlc / other:…）。</param>
    /// <param name="title">服务端原始标题（可能为空）。</param>
    /// <param name="isDlc">是否是 DLC 组（选项值是 appid，且显示名称过滤框）。</param>
    /// <param name="tags">选项。</param>
    public WorkshopFilterGroupViewModel(
        string key,
        string title,
        bool isDlc,
        IEnumerable<SteamWorkshopTag> tags)
    {
        Key = key;
        RawTitle = title;
        _title = title;
        IsDlc = isDlc;

        // 类别组默认展开，更新兼容性 / DLC 默认折叠：后者动辄几十项，全展开会把整栏挤满。
        _isExpanded = string.Equals(key, "category", StringComparison.Ordinal);
        Options = new ObservableCollection<WorkshopTagOption>(
            tags.Select(tag => new WorkshopTagOption(tag.Tag, tag.DisplayName, localize: !isDlc)));
        VisibleOptions = new ObservableCollection<WorkshopTagOption>(Options);

        foreach (var option in Options)
        {
            option.PropertyChanged += OnOptionPropertyChanged;
        }
    }

    /// <summary>稳定键。</summary>
    public string Key { get; }

    /// <summary>是否 DLC 组。</summary>
    public bool IsDlc { get; }

    /// <summary>服务端原始标题（用于切语言时重新本地化；未知分组直接显示它）。</summary>
    public string RawTitle { get; }

    /// <summary>分组标题（随语言切换，由父级重设）。</summary>
    [ObservableProperty]
    private string _title;

    /// <summary>展开状态（标题按钮点击时由页面切换；类别组默认展开，其余默认收起）。</summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>标题右侧的箭头图标：展开朝上、收起朝下。</summary>
    public string HeaderGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    /// <summary>本组已生效的选项数（标题右侧的数字徽标）。</summary>
    public int ActiveCount => Options.Count(static option => option.IsActive);

    /// <summary>是否有选项生效（没有就不显示徽标）。</summary>
    public bool HasActive => ActiveCount > 0;

    /// <summary>徽标文字（折叠时也能看出这组选了东西）。</summary>
    public string ActiveCountText => ActiveCount.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>
    /// 分组标题的读屏文本：有选择时附上数量（折叠着也能知道这组选了几项）。
    /// </summary>
    public string HeaderText => HasActive ? $"{Title} · {ActiveCountText}" : Title;

    /// <summary>全部选项。</summary>
    public ObservableCollection<WorkshopTagOption> Options { get; }

    /// <summary>当前可见的选项（DLC 名称过滤后）。</summary>
    public ObservableCollection<WorkshopTagOption> VisibleOptions { get; }

    /// <summary>「按名称筛选」输入框的文案（仅 DLC 组显示）。</summary>
    [ObservableProperty]
    private string _nameFilterText = string.Empty;

    /// <summary>过滤后是否还有选项（DLC 组过滤到空时显示提示）。</summary>
    public bool HasVisibleOptions => VisibleOptions.Count > 0;

    /// <summary>是否显示「按名称筛选」输入框（只有 DLC 组显示）。</summary>
    public bool ShowNameFilter => IsDlc;

    /// <summary>DLC 组过滤后为空时显示提示。</summary>
    public bool ShowDlcEmptyHint => IsDlc && VisibleOptions.Count == 0;

    /// <summary>选项状态变化（已合并成一次语义事件）。</summary>
    public event EventHandler? Changed;

    /// <summary>把已有选择套到新加载的选项上（SSR 刷新时保留用户勾选）。</summary>
    /// <param name="required">必须包含的标签/值。</param>
    /// <param name="excluded">必须排除的标签/值。</param>
    public void RestoreSelection(IReadOnlySet<string> required, IReadOnlySet<string> excluded)
    {
        _suppressChanged = true;
        try
        {
            foreach (var option in Options)
            {
                option.IsRequired = required.Contains(option.Tag);
                option.IsExcluded = excluded.Contains(option.Tag);
            }
        }
        finally
        {
            _suppressChanged = false;
        }
    }

    /// <summary>清空本组的选择与名称过滤。</summary>
    public void Reset()
    {
        _suppressChanged = true;
        try
        {
            foreach (var option in Options)
            {
                option.Reset();
            }

            NameFilterText = string.Empty;
        }
        finally
        {
            _suppressChanged = false;
        }
    }

    partial void OnNameFilterTextChanged(string value) => ApplyNameFilter();

    /// <summary>切语言时组名会重设，标题（含数量后缀）跟着刷新。</summary>
    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(HeaderText));

    /// <summary>展开/收起时箭头描边跟着翻。</summary>
    partial void OnIsExpandedChanged(bool value) => OnPropertyChanged(nameof(HeaderGlyph));

    private void ApplyNameFilter()
    {
        var filter = NameFilterText.Trim();
        VisibleOptions.Clear();
        foreach (var option in Options)
        {
            if (filter.Length == 0
                || option.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                VisibleOptions.Add(option);
            }
        }

        OnPropertyChanged(nameof(HasVisibleOptions));
        OnPropertyChanged(nameof(ShowDlcEmptyHint));
    }

    private void OnOptionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(WorkshopTagOption.IsRequired) or nameof(WorkshopTagOption.IsExcluded)))
        {
            return;
        }

        // 徽标数字不受 _suppressChanged 影响：批量恢复选择时界面也要跟着更新，
        // 只是不需要再抛一次 Changed（那一次由调用方自己决定）。
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(HeaderText));

        if (_suppressChanged)
        {
            return;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
