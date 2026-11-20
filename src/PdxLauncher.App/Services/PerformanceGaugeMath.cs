namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Infrastructure;

/// <summary>性能仪表环的健康程度；数值越高越接近或超过预算。</summary>
public enum GaugeStatus
{
    /// <summary>预算内（低于 75%）。</summary>
    Normal,

    /// <summary>接近预算（75% ~ 100%）。</summary>
    Warning,

    /// <summary>达到或超过预算。</summary>
    Critical,
}

/// <summary>一次仪表读数：占预算百分比（0 ~ 100）与健康状态。</summary>
public sealed record GaugeReading(double Percent, GaugeStatus Status);

/// <summary>
/// 「性能与日志」概览四个核心环的纯计算：把实测值映射到预算百分比并给出健康状态。
/// 只依赖预算常量与基础数学，不引用 WinUI / AppServices，
/// 因此可原样链接进 PdxLauncher.Checks，用 PerformanceGaugeMathChecks 钉住边界。
/// </summary>
public static class PerformanceGaugeMath
{
    /// <summary>状态阈值：达到 75% 视为接近预算。</summary>
    public const double WarningPercent = 75;

    /// <summary>内存映射：空闲目标（250 MB）对应 60%，详情预算（550 MB）对应 100%。</summary>
    public const double IdleMemoryPercent = 60;

    /// <summary>把任意数值夹到 0 ~ 100；NaN / 无穷按 0 处理。</summary>
    public static double ClampPercent(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 100);
    }

    /// <summary>
    /// 工作集映射：0 → 0%，空闲目标 → 60%，详情预算及以上 → 100%，中间线性插值。
    /// 空闲目标以下用更平缓的斜率，让用户从「绿变黄」之前就能看到环在涨。
    /// </summary>
    public static GaugeReading ComputeMemory(double workingSetBytes)
    {
        if (double.IsNaN(workingSetBytes) || workingSetBytes <= 0)
        {
            return new GaugeReading(0, GaugeStatus.Normal);
        }

        double percent = workingSetBytes <= PerformanceHints.IdleBudgetBytes
            ? workingSetBytes / PerformanceHints.IdleBudgetBytes * IdleMemoryPercent
            : IdleMemoryPercent
              + (workingSetBytes - PerformanceHints.IdleBudgetBytes)
                / (PerformanceHints.DetailBudgetBytes - PerformanceHints.IdleBudgetBytes)
                * (100 - IdleMemoryPercent);

        return ToReading(percent);
    }

    /// <summary>缓存合计按 500 MB 预算映射，超过按 100%。</summary>
    public static GaugeReading ComputeCache(double cacheBytes) =>
        ToReading(cacheBytes / PerformanceHints.CacheBudgetBytes * 100);

    /// <summary>日志主文件按 5 MB 轮转阈值映射，超过按 100%。</summary>
    public static GaugeReading ComputeLog(double logBytes) =>
        ToReading(logBytes / FileLogger.MaxBytes * 100);

    /// <summary>冷启动耗时按 2.5 秒目标映射，超过按 100%。</summary>
    public static GaugeReading ComputeStartup(double elapsedMilliseconds) =>
        ToReading(elapsedMilliseconds / PerformanceHints.StartupBudgetMilliseconds * 100);

    private static GaugeReading ToReading(double percent)
    {
        var clamped = ClampPercent(percent);
        var status = clamped >= 100
            ? GaugeStatus.Critical
            : clamped >= WarningPercent
                ? GaugeStatus.Warning
                : GaugeStatus.Normal;

        return new GaugeReading(clamped, status);
    }
}
