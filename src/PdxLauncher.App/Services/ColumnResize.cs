namespace PdxLauncher.App.Services;

/// <summary>
/// 双列播放集页的分栏宽度计算。
/// </summary>
/// <remarks>
/// 只做纯计算、不碰任何 UI 类型，一是页面代码里那几处钳制需要同一份上下限，
/// 二是自检工程可以直接链接这个文件断言边界（拖到窗口边缘时右栏不能被挤没）。
/// </remarks>
public static class ColumnResize
{
    /// <summary>默认左栏宽度（DIP）：刚够显示常见 mod 名字，右栏还能放下开关与行尾按钮。</summary>
    public const double DefaultWidth = 400.0;

    /// <summary>左栏下限：再窄候选名就只剩几个字，拖动的意义没了。</summary>
    public const double MinimumWidth = 280.0;

    /// <summary>左栏上限：再宽右栏放不下开关与行尾按钮组。</summary>
    public const double MaximumWidth = 900.0;

    /// <summary>右栏下限：启用开关 + 行尾三个按钮的实测占位，再挤就会横向滚动。</summary>
    public const double MinimumRightWidth = 320.0;

    /// <summary>分隔条自己占的列宽（DIP）。</summary>
    public const double SplitterWidth = 12.0;

    /// <summary>
    /// 把左栏宽度钳进允许区间。
    /// </summary>
    /// <param name="width">期望宽度（DIP）。</param>
    /// <param name="availableWidth">双列区域的总宽（DIP）。</param>
    /// <returns>钳制后的宽度；总宽还不够放下限时返回下限。</returns>
    public static double Clamp(double width, double availableWidth)
    {
        if (double.IsNaN(width) || double.IsInfinity(width) || availableWidth <= 0)
        {
            return DefaultWidth;
        }

        var max = Math.Min(MaximumWidth, availableWidth - SplitterWidth - MinimumRightWidth);
        return Math.Clamp(width, MinimumWidth, Math.Max(MinimumWidth, max));
    }
}
