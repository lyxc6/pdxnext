namespace PdxLauncher.App.Services;

/// <summary>
/// 一行排不下就换行的排版计算：给定每项的「自然宽高」与可用宽度，算出每项的最终矩形。
/// <para>
/// 只做纯数学，不碰任何 XAML 类型 —— 这样检查工程能把它链接进去直接断言，
/// 界面那边（<c>PdxLauncher.App.Controls.PdxWrapPanel</c>）只负责把子元素的
/// <c>DesiredSize</c> 喂进来、再把结果塞回 <c>Arrange</c>。
/// </para>
/// <para>
/// 起因：详情面板那一排按钮在默认窗口宽下就超过可用宽度，而 WinUI 3 没有 WrapPanel，
/// 水平 StackPanel 从不换行，外层 ScrollViewer 又不允许横向滚动，于是右边的按钮被直接裁掉。
/// </para>
/// </summary>
public static class WrapLayout
{
    /// <summary>参与换行的一项的自然尺寸（DIP）。</summary>
    /// <param name="Width">自然宽度。</param>
    /// <param name="Height">自然高度。</param>
    public readonly record struct WrapItem(double Width, double Height);

    /// <summary>一项的最终摆放位置（DIP）。</summary>
    /// <param name="Index">在传入列表里的下标（被跳过的项不产生 <see cref="WrapPlacement"/>）。</param>
    /// <param name="X">左上角 X。</param>
    /// <param name="Y">左上角 Y。</param>
    /// <param name="Width">摆放宽度（等于自然宽度，不拉伸子元素）。</param>
    /// <param name="Height">摆放高度（等于自然高度，不拉伸子元素）。</param>
    public readonly record struct WrapPlacement(int Index, double X, double Y, double Width, double Height);

    /// <summary>一次换行排版的结果：每项位置 + 整块内容的宽高。</summary>
    /// <param name="Items">按原始下标升序排列的摆放位置。</param>
    /// <param name="Width">内容总宽（最长一行的右边界）。</param>
    /// <param name="Height">内容总高（各行高度之和 + 行间距）。</param>
    public sealed record WrapLayoutResult(IReadOnlyList<WrapPlacement> Items, double Width, double Height);

    /// <summary>
    /// 按可用宽度把各项排成若干行，行内与行间都用同一个 <paramref name="spacing"/>。
    /// </summary>
    /// <param name="items">各项的自然尺寸，顺序即排版顺序。</param>
    /// <param name="availableWidth">可用宽度（DIP）；非有限值或 ≤0 表示不限制，排成一行。</param>
    /// <param name="spacing">行内与行间距（DIP）；非有限值或 &lt;0 按 0 处理。</param>
    /// <returns>每项位置与内容整体宽高；空列表或全部被跳过时宽高都是 0。</returns>
    public static WrapLayoutResult Compute(
        IReadOnlyList<WrapItem> items,
        double availableWidth,
        double spacing)
    {
        ArgumentNullException.ThrowIfNull(items);

        var gap = double.IsFinite(spacing) && spacing > 0 ? spacing : 0;
        var limit = double.IsFinite(availableWidth) && availableWidth > 0
            ? availableWidth
            : double.PositiveInfinity;

        var placements = new List<WrapPlacement>(items.Count);
        var rightEdge = 0.0;     // 当前行最后一个元素的右边界
        var lineHeight = 0.0;    // 当前行最高的一项
        var lineItems = 0;       // 当前行有几项
        var top = 0.0;           // 当前行的 Y
        var maxLineWidth = 0.0;

        for (var i = 0; i < items.Count; i++)
        {
            var width = items[i].Width;
            var height = items[i].Height;

            // 尺寸不可用（NaN / 无穷 / 0）的项不占位、也不产生间距：折叠元素与尚未量出尺寸的元素都走这里。
            if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            {
                continue;
            }

            var x = lineItems == 0 ? 0 : rightEdge + gap;
            if (lineItems > 0 && x + width > limit)
            {
                // 放不下就整项换到下一行；行首不做这个判断，否则单项超宽会无限换行。
                maxLineWidth = Math.Max(maxLineWidth, rightEdge);
                top += lineHeight + gap;
                rightEdge = 0;
                lineHeight = 0;
                lineItems = 0;
                x = 0;
            }

            placements.Add(new WrapPlacement(i, x, top, width, height));
            rightEdge = x + width;
            lineHeight = Math.Max(lineHeight, height);
            lineItems++;
        }

        if (placements.Count == 0)
        {
            return new WrapLayoutResult(placements, 0, 0);
        }

        maxLineWidth = Math.Max(maxLineWidth, rightEdge);
        return new WrapLayoutResult(placements, maxLineWidth, top + lineHeight);
    }
}
