namespace PdxLauncher.App.ViewModels;

using System.Globalization;

/// <summary>分页器上的一格：页码或省略号。</summary>
/// <param name="PageIndex">从 0 开始的页码（省略号为 0）。</param>
/// <param name="IsEllipsis">是否是「…」占位。</param>
/// <param name="IsCurrent">是否是当前页。</param>
public sealed record WorkshopPageButton(int PageIndex, bool IsEllipsis, bool IsCurrent)
{
    /// <summary>展示文案（1 基页码或省略号）。</summary>
    public string Label => IsEllipsis
        ? "\u2026"
        : (PageIndex + 1).ToString(CultureInfo.CurrentCulture);

    /// <summary>是否是页码（不是省略号）。</summary>
    public bool IsNumber => !IsEllipsis;

    /// <summary>普通页码按钮（非当前页）。</summary>
    public bool ShowAsButton => !IsEllipsis && !IsCurrent;

    /// <summary>当前页按钮（强调色）。</summary>
    public bool ShowAsCurrentButton => IsCurrent;

    /// <summary>省略号占位。</summary>
    public bool ShowAsEllipsis => IsEllipsis;

    /// <summary>稳定的 AutomationId。</summary>
    public string AutomationId => IsEllipsis
        ? "txtWorkshopPageGap" + PageIndex.ToString(CultureInfo.InvariantCulture)
        : "btnWorkshopPage" + (PageIndex + 1).ToString(CultureInfo.InvariantCulture);
}
