namespace PdxLauncher.App.Services;

/// <summary>
/// 工坊详情页图片区的尺寸计算：内联大图高度、全屏查看器里单张图片可用的宽高，
/// 以及正文（描述 / 更新日志）配图的列宽。
/// <para>
/// 只做纯数学，不碰任何 XAML 类型 —— 这样检查工程能把它链接进去直接断言，
/// 界面那边每次 <c>SizeChanged</c> 只是把算好的数字塞给控件。
/// </para>
/// </summary>
public static class WorkshopGalleryLayout
{
    /// <summary>内联大图的最小高度（DIP）。</summary>
    public const double InlineMinHeight = 160.0;

    /// <summary>内联大图的最大高度（DIP）。</summary>
    public const double InlineMaxHeight = 420.0;

    /// <summary>内联大图占窗口高度的比例。</summary>
    public const double InlineRatio = 0.45;

    /// <summary>量不到窗口高度时用的默认高度（DIP），与 XAML 里的初值一致。</summary>
    public const double InlineFallbackHeight = 360.0;

    /// <summary>查看器里图片的最小可用边长（DIP）；再小就把图片压没了。</summary>
    public const double ViewerMinSide = 120.0;

    /// <summary>查看器四周留白（DIP）。</summary>
    public const double ViewerPadding = 16.0;

    /// <summary>正文（描述 / 更新日志）内容列的最大宽度（DIP），对齐 Steam 物品页约 950 px 的阅读宽度。</summary>
    public const double DescriptionContentMaxWidth = 960.0;

    /// <summary>正文卡片的宽度上限：内容 960 + 左右各 16 内边距。</summary>
    public const double DescriptionCardMaxWidth = DescriptionContentMaxWidth + 32.0;

    /// <summary>正文配图框的左右内边距合计（DIP）。</summary>
    public const double InlineImageInset = 16.0;

    /// <summary>正文配图的最小宽度（DIP）；窗口极窄时也不把图压成 0。</summary>
    public const double InlineImageMinWidth = 120.0;

    /// <summary>配图占位框的默认最小宽度（DIP）。</summary>
    public const double InlineImagePlaceholderWidth = 240.0;

    /// <summary>
    /// 按正文视图的实际宽度算单张配图的 MaxWidth：小图保持原尺寸不放大，大图缩到列宽
    /// （与 Steam 物品页的 <c>max-width: 100%</c> 一致）。
    /// </summary>
    /// <param name="viewWidth">正文视图的实际宽度（DIP）；0 / NaN 表示还没量到。</param>
    public static double DescriptionImageMaxWidth(double viewWidth)
    {
        var limit = DescriptionContentMaxWidth - InlineImageInset;
        if (!double.IsFinite(viewWidth) || viewWidth <= 0)
        {
            return limit;
        }

        return Math.Clamp(Math.Min(viewWidth, DescriptionContentMaxWidth) - InlineImageInset, InlineImageMinWidth, limit);
    }

    /// <summary>配图占位框的最小宽度：默认 240，但不大于图片实际可用宽度。</summary>
    /// <param name="imageWidth">图片的 MaxWidth（见 <see cref="DescriptionImageMaxWidth"/>）。</param>
    public static double PlaceholderMinWidth(double imageWidth) =>
        double.IsFinite(imageWidth) && imageWidth > 0
            ? Math.Min(InlineImagePlaceholderWidth, imageWidth)
            : InlineImagePlaceholderWidth;

    /// <summary>按页面高度算内联大图高度。</summary>
    /// <param name="pageHeight">页面可用高度（DIP）。</param>
    /// <returns>夹在 <see cref="InlineMinHeight"/> 与 <see cref="InlineMaxHeight"/> 之间的高度。</returns>
    public static double InlineGalleryHeight(double pageHeight)
    {
        if (!double.IsFinite(pageHeight) || pageHeight <= 0)
        {
            return InlineFallbackHeight;
        }

        return Math.Clamp(pageHeight * InlineRatio, InlineMinHeight, InlineMaxHeight);
    }

    /// <summary>
    /// 按浮层尺寸算查看器里单张图片可用的宽高（1x 就是「整张图刚好放进这个框」）。
    /// </summary>
    /// <param name="overlayWidth">浮层宽度（DIP）。</param>
    /// <param name="overlayHeight">浮层高度（DIP）。</param>
    /// <param name="headerHeight">浮层顶部计数/关闭栏的高度（DIP）。</param>
    /// <returns>图片的 MaxWidth / MaxHeight。</returns>
    public static (double Width, double Height) ViewerContent(
        double overlayWidth,
        double overlayHeight,
        double headerHeight)
    {
        var width = Shrink(overlayWidth, 0);
        var height = Shrink(overlayHeight, headerHeight);
        return (width, height);
    }

    private static double Shrink(double side, double headerHeight)
    {
        if (!double.IsFinite(side) || side <= 0)
        {
            return ViewerMinSide;
        }

        var usable = side - (ViewerPadding * 2) - (double.IsFinite(headerHeight) && headerHeight > 0 ? headerHeight : 0);
        return usable < ViewerMinSide ? ViewerMinSide : usable;
    }
}
