namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mods;

/// <summary>依赖清单里的一行（mod 详情页「前置依赖」卡）。</summary>
public sealed class WorkshopDependencyViewModel : LocalizedObservableObject
{
    /// <summary>构造。</summary>
    /// <param name="index">行号（拼 AutomationId 用）。</param>
    /// <param name="dependency">判定结果。</param>
    public WorkshopDependencyViewModel(int index, WorkshopDependency dependency)
    {
        ArgumentNullException.ThrowIfNull(dependency);

        var requirement = dependency.Requirement;
        var localizer = Localizer.Instance;

        Name = string.IsNullOrWhiteSpace(requirement.Name) ? requirement.Id : requirement.Name.Trim();
        Index = index;
        State = dependency.State;
        AutomationId = "workshopDependency_" + index.ToString(CultureInfo.InvariantCulture);

        StateText = dependency.State switch
        {
            WorkshopDependencyState.Enabled => localizer.T(Keys.WorkshopDependencyEnabled),
            WorkshopDependencyState.InstalledDisabled => localizer.T(Keys.WorkshopDependencyDisabled),
            WorkshopDependencyState.Missing => localizer.T(Keys.WorkshopDependencyMissing),
            _ => localizer.T(Keys.WorkshopDependencyUnknown),
        };

        DetailText = requirement.Version is > 0
            ? localizer.T(Keys.WorkshopDependencyVersion, requirement.Version.Value.ToString(CultureInfo.CurrentCulture))
            : requirement.Id;
    }

    /// <summary>行号。</summary>
    public int Index { get; }

    /// <summary>依赖名（服务端没给名字时退到 id）。</summary>
    public string Name { get; }

    /// <summary>右侧状态文案。</summary>
    public string StateText { get; }

    /// <summary>副标题：要求版本或依赖 id。</summary>
    public string DetailText { get; }

    /// <summary>本机状态。</summary>
    public WorkshopDependencyState State { get; }

    /// <summary>需要提醒（装了但没启用、或者根本没装）。</summary>
    public bool NeedsAttention =>
        State is WorkshopDependencyState.InstalledDisabled or WorkshopDependencyState.Missing;

    /// <summary>完全没有（视觉上用错误色）。</summary>
    public bool IsMissing => State == WorkshopDependencyState.Missing;

    /// <summary>读不到 mod 库时的中性状态。</summary>
    public bool IsUnknown => State == WorkshopDependencyState.Unknown;

    /// <summary>无障碍/自动化 id。</summary>
    public string AutomationId { get; }
}
