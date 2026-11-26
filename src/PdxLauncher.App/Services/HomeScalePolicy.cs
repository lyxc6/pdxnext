namespace PdxLauncher.App.Services;

/// <summary>
/// 主页整体缩放策略（纯函数，不引用任何 XAML 类型）。
/// <para>
/// 基准是主页内容的自然宽度：内容列 <c>MaxWidth=1120</c>（含左右 24 内边距），
/// 三张公告卡总宽约 991，放在 1120 列里刚好不溢出。左侧 NavigationView 固定 216 DIP，
/// 不参与缩放。窗口比 1120 + 216 窄时整体等比缩小，最低 0.8；
/// 默认窗口 1200×800 时内容区约 984、比例 0.879，正好接近官方在 1200 宽下的观感。
/// </para>
/// </summary>
public static class HomeScalePolicy
{
    /// <summary>最小窗口（DIP）。与 MainWindow 的 MinimumWindowWidth 保持一致。</summary>
    public const double MinimumWindowWidth = 1040;

    /// <summary>左侧导航固定宽（DIP），与 NavigationView.OpenPaneLength 一致。</summary>
    public const double NavigationPaneWidth = 216;

    /// <summary>主页内容列的自然宽度（XAML 的 MaxWidth=1120，含左右各 24 内边距）。</summary>
    public const double DesignContentWidth = 1120;

    /// <summary>主页内容列的自然高度不参与比例计算：高度不足时由 ScrollViewer 兜底。</summary>
    public const double DesignContentHeight = double.PositiveInfinity;

    /// <summary>
    /// 缩放下限：0.75。最小窗口下内容区约 808 DIP，三卡行需要 ≤0.767 才不溢出；
    /// 0.75 同时保证按钮文字仍可读。
    /// </summary>
    public const double MinimumScale = 0.75;

    /// <summary>按可用内容区宽度算缩放系数；非法尺寸返回 1.0（不缩放）。</summary>
    public static double Resolve(
        double availableWidth,
        double availableHeight = double.PositiveInfinity,
        double designWidth = DesignContentWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth <= 0
            || !double.IsFinite(designWidth) || designWidth <= 0)
        {
            return 1.0;
        }

        var widthRatio = availableWidth / designWidth;
        var raw = widthRatio;
        return Math.Clamp(raw, MinimumScale, 1.0);
    }
}
