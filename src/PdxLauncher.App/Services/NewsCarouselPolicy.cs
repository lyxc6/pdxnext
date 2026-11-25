namespace PdxLauncher.App.Services;

/// <summary>
/// 公告轮播的显示规则（纯函数，不引用任何 XAML 类型）。
/// 官方 2026.14 的 .newsSlot：单卡时不显示箭头与进度点，多卡才轮播。
/// </summary>
internal static class NewsCarouselPolicy
{
    /// <summary>官方 slick 的自动切换间隔（2 秒）。</summary>
    public static readonly TimeSpan AutoAdvanceInterval = TimeSpan.FromSeconds(2);

    /// <summary>进度条总宽（官方 .slick-dots 是 10rem = 160px，这里按 4 DIP 网格取 160）。</summary>
    public const double DotsWidth = 160;

    /// <summary>单段进度条宽（4 段时 37 + 3×4 间距 = 160）。</summary>
    public const double DotWidth = 37;

    /// <summary>进度条高度（官方 .333rem ≈ 4）。</summary>
    public const double DotHeight = 4;

    /// <summary>是否显示自动轮播（多卡才启动计时器）。</summary>
    public static bool ShouldAutoAdvance(int itemCount) => itemCount > 1;

    /// <summary>是否显示左右箭头（多卡且指针悬停时）。</summary>
    public static bool ShouldShowArrows(int itemCount, bool hovered) => itemCount > 1 && hovered;

    /// <summary>是否显示底部进度点（多卡时）。</summary>
    public static bool ShouldShowDots(int itemCount) => itemCount > 1;

    /// <summary>把索引夹在有效范围内；空集合返回 -1。</summary>
    public static int ClampIndex(int index, int itemCount)
    {
        if (itemCount <= 0)
        {
            return -1;
        }

        return index < 0 ? itemCount - 1 : index >= itemCount ? 0 : index;
    }

    /// <summary>前进/后退后的索引，循环切换。</summary>
    public static int Advance(int index, int itemCount, int delta)
    {
        if (itemCount <= 0)
        {
            return -1;
        }

        var next = (index + delta) % itemCount;
        return next < 0 ? next + itemCount : next;
    }
}
