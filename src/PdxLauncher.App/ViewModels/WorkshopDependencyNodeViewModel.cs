namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mods;

/// <summary>
/// 详情页依赖树的一行：顶层是「需处理 / 已就绪 / 未知」分组，叶子是原有依赖行。
/// <para>
/// 一期只做分组，不做递归：<see cref="WorkshopDependencyMatcher"/> 只返回平铺 ID，
/// 真递归要动 Core 的匹配模型并加循环防护，留到后续。
/// </para>
/// </summary>
public sealed class WorkshopDependencyNodeViewModel : LocalizedObservableObject
{
    private readonly WorkshopDependencyViewModel? _leaf;
    private readonly DependencyGroup _group;
    private readonly int _groupCount;

    /// <summary>叶子节点（包一条现成依赖行）。</summary>
    /// <param name="leaf">依赖行。</param>
    public WorkshopDependencyNodeViewModel(WorkshopDependencyViewModel leaf)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        _leaf = leaf;
        _group = DependencyGroup.Leaf;
        AutomationId = leaf.AutomationId;
    }

    /// <summary>分组节点；子项按传入顺序挂到 <see cref="Children"/>。</summary>
    /// <param name="group">分组类别。</param>
    /// <param name="children">组内叶子依赖行。</param>
    public WorkshopDependencyNodeViewModel(DependencyGroup group, IEnumerable<WorkshopDependencyViewModel> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        _group = group;
        var items = children.ToList();
        _groupCount = items.Count;
        AutomationId = group switch
        {
            DependencyGroup.Attention => "workshopDependencyGroup_attention",
            DependencyGroup.Ready => "workshopDependencyGroup_ready",
            DependencyGroup.Unknown => "workshopDependencyGroup_unknown",
            _ => "workshopDependencyGroup_unknown",
        };
        IsExpanded = group == DependencyGroup.Attention;

        foreach (var child in items)
        {
            Children.Add(new WorkshopDependencyNodeViewModel(child));
        }
    }

    /// <summary>叶子包装的是哪一行；分组节点为 null。</summary>
    public WorkshopDependencyViewModel? Leaf => _leaf;

    /// <summary>子节点；叶子为空集合。</summary>
    public ObservableCollection<WorkshopDependencyNodeViewModel> Children { get; } = [];

    /// <summary>是否分组节点。</summary>
    public bool IsGroup => _leaf is null;

    /// <summary>分组默认展开：需处理组展开，其余折叠。</summary>
    public bool IsExpanded { get; private set; }

    /// <summary>显示名：分组带计数，叶子用依赖名。</summary>
    public string Name => _leaf?.Name ?? _group switch
    {
        DependencyGroup.Attention => T(Keys.WorkshopDependenciesGroupAttention, _groupCount),
        DependencyGroup.Ready => T(Keys.WorkshopDependenciesGroupReady, _groupCount),
        _ => T(Keys.WorkshopDependenciesGroupUnknown, _groupCount),
    };

    /// <summary>副标题（要求版本或 id）；分组节点为空。</summary>
    public string DetailText => _leaf?.DetailText ?? string.Empty;

    /// <summary>右侧状态文案；分组节点为空。</summary>
    public string StateText => _leaf?.StateText ?? string.Empty;

    /// <summary>分组节点是否显示计数副标题。</summary>
    public bool IsGroupHeader => IsGroup;

    /// <summary>叶子是否需要提醒（缺依赖 / 装了没启用）。</summary>
    public bool NeedsAttention => _leaf?.NeedsAttention ?? false;

    /// <summary>分组节点显示目录图标。</summary>
    public bool ShowGroupIcon => IsGroup;

    /// <summary>叶子节点显示提醒图标。</summary>
    public bool ShowAttentionIcon => !IsGroup && NeedsAttention;

    /// <summary>叶子节点有副标题。</summary>
    public bool HasDetail => !IsGroup && DetailText.Length > 0;

    /// <summary>叶子节点状态徽标。</summary>
    public bool HasState => !IsGroup && StateText.Length > 0;

    /// <summary>无障碍/自动化 id；叶子沿用原依赖行 id。</summary>
    public string AutomationId { get; }

    /// <summary>切语言后只重算分组标题；叶子沿用依赖行的本地化结果。</summary>
    protected override void Relocalize() => OnPropertyChanged(nameof(Name));

    /// <summary>依赖树的一级分组。</summary>
    public enum DependencyGroup
    {
        Leaf,
        Attention,
        Ready,
        Unknown,
    }
}
