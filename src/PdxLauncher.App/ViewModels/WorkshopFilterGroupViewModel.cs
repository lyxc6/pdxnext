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
        if (_suppressChanged)
        {
            return;
        }

        if (e.PropertyName is nameof(WorkshopTagOption.IsRequired) or nameof(WorkshopTagOption.IsExcluded))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
