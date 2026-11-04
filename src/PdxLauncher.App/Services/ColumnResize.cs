namespace PdxLauncher.App.Services;

/// <summary>
/// 双列播放集页的分栏宽度计算。
/// </summary>
/// <remarks>
/// 只做纯计算、不碰任何 UI 类型，一是页面代码里那几处钳制需要同一份上下限，
/// 二是自检工程可以直接链接这个文件断言边界（拖到窗口边缘时右栏不能被挤没）。
/// 这里的上下限是唯一真源：XAML 的两个列定义刻意不写 MinWidth，否则硬顶住的列宽
/// 会让整张 Grid 超过可用宽度，右栏直接被窗口边缘裁掉。
/// </remarks>
public static class ColumnResize
{
    /// <summary>默认左栏宽度（DIP）：刚够显示常见 mod 名字，右栏还能放下开关与行尾按钮。</summary>
    public const double DefaultWidth = 400.0;

    /// <summary>左栏常规下限（DIP）：窗口还有余量时不低于它，再窄候选名就只剩几个字。</summary>
    public const double MinimumWidth = 280.0;

    /// <summary>左栏上限（DIP）：再宽右栏放不下开关与行尾按钮组。</summary>
    public const double MaximumWidth = 900.0;

    /// <summary>
    /// 右栏下限（DIP）。播放集一行里固定占位约 320 DIP——序号 40、缩略图 44、
    /// 启用开关与文字 70、行尾上移 / 下移 / 移出三个按钮 130，加间距与内边距约 40——
    /// 再留 80 给 mod 名字。低于这个宽度名字列会被压成 0，行内容挤出可见区。
    /// </summary>
    public const double MinimumRightWidth = 400.0;

    /// <summary>分隔条自己占的列宽（DIP）。</summary>
    public const double SplitterWidth = 12.0;

    /// <summary>
    /// 把左栏宽度钳进允许区间；空间不够时左栏先让位，绝不溢出。
    /// </summary>
    /// <param name="width">期望宽度（DIP）。</param>
    /// <param name="availableWidth">双列区域的总宽（DIP）。</param>
    /// <returns>
    /// 钳制后的宽度。常规情况下落在 [<see cref="MinimumWidth"/>, min(<see cref="MaximumWidth"/>,
    /// 总宽 - 分隔条 - <see cref="MinimumRightWidth"/>)]；可用宽度不足放下限时返回更小的值（甚至 0），
    /// 保证右栏下限不受侵犯。宽度或总宽还没测量（NaN / 无穷 / 非正）时返回 <see cref="DefaultWidth"/>。
    /// </returns>
    public static double Clamp(double width, double availableWidth)
    {
        if (!double.IsFinite(width) || !double.IsFinite(availableWidth) || availableWidth <= 0)
        {
            return DefaultWidth;
        }

        var upper = Math.Min(MaximumWidth, availableWidth - SplitterWidth - MinimumRightWidth);
        if (upper <= 0)
        {
            return 0;
        }

        var lower = Math.Min(MinimumWidth, upper);
        return Math.Clamp(width, lower, upper);
    }
}
