namespace PdxLauncher.App.Services;

/// <summary>迷你趋势线上的一个点（固定 120×24 画布坐标）。</summary>
public readonly record struct TrendPoint(double X, double Y);

/// <summary>
/// 性能仪表 60 秒迷你趋势线的纯数学：把一串百分比映射成固定画布上的折线点。
/// 只依赖 System.*，不碰 WinUI，可原样链接进 PdxLauncher.Checks，
/// 由 GaugeTrendChecks 钉住空数据 / 单点 / 全平 / 高低映射等边界。
/// </summary>
public static class GaugeTrendMath
{
    /// <summary>趋势窗口：保留最近 60 秒的采样。</summary>
    public const int WindowSeconds = 60;

    /// <summary>同一秒内多次刷新只保留最后一次，避免瞬时抖动塞满窗口。</summary>
    public const int DedupeMilliseconds = 800;

    /// <summary>迷你折线画布尺寸（与 PdxGaugeRing.xaml 的 Viewbox 一致）。</summary>
    public const double CanvasWidth = 120;

    public const double CanvasHeight = 24;

    /// <summary>上下留白，避免折线贴边被裁。</summary>
    private const double Padding = 4;

    /// <summary>
    /// 把值序列映射成折线点：X 从左到右均分，Y 按 [min,max] 反向映射（值越大越靠上）。
    /// 全平或只有一点时画在中线；输入为空返回空集合。
    /// </summary>
    public static IReadOnlyList<TrendPoint> BuildPoints(
        IReadOnlyList<double> values,
        double width = CanvasWidth,
        double height = CanvasHeight)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            return Array.Empty<TrendPoint>();
        }

        if (width <= 0 || height <= 0)
        {
            return Array.Empty<TrendPoint>();
        }

        double usableHeight = Math.Max(0, height - Padding * 2);
        double middle = height / 2;
        double min = values[0];
        double max = values[0];
        for (var i = 1; i < values.Count; i++)
        {
            min = Math.Min(min, values[i]);
            max = Math.Max(max, values[i]);
        }

        double span = max - min;
        double stepX = values.Count == 1 ? 0 : width / (values.Count - 1);
        var points = new TrendPoint[values.Count];
        for (var i = 0; i < values.Count; i++)
        {
            double y = span < 0.0001
                ? middle
                : Padding + usableHeight * (1 - (values[i] - min) / span);
            points[i] = new TrendPoint(i * stepX, y);
        }

        return points;
    }
}
