namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// 性能仪表环映射的自检：把预算边界（0 / 75% 告警线 / 100% 危险线 / 超额）逐条钉住。
/// 映射错了不会崩，只会让环显示为错误的颜色或满环过早/过晚，编译器看不见，
/// 所以由这里守住每个预算常量两侧的读数与状态。
/// </summary>
public static class PerformanceGaugeMathChecks
{
    private const double Megabyte = 1024d * 1024d;

    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        CheckClamp();
        CheckMemory();
        CheckCache();
        CheckLog();
        CheckStartup();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckClamp()
    {
        ChecksContext.Equal("仪表：负数夹到 0", 0, PerformanceGaugeMath.ClampPercent(-12));
        ChecksContext.Equal("仪表：0 保持 0", 0, PerformanceGaugeMath.ClampPercent(0));
        ChecksContext.Equal("仪表：100 保持 100", 100, PerformanceGaugeMath.ClampPercent(100));
        ChecksContext.Equal("仪表：超过 100 夹到 100", 100, PerformanceGaugeMath.ClampPercent(137.5));
        ChecksContext.Equal("仪表：NaN 按 0", 0, PerformanceGaugeMath.ClampPercent(double.NaN));
        ChecksContext.Equal("仪表：正无穷按 0", 0, PerformanceGaugeMath.ClampPercent(double.PositiveInfinity));
        ChecksContext.Equal("仪表：负无穷按 0", 0, PerformanceGaugeMath.ClampPercent(double.NegativeInfinity));
    }

    private static void CheckMemory()
    {
        ChecksContext.Equal("仪表内存：0 → 0%", 0, PerformanceGaugeMath.ComputeMemory(0).Percent);
        ChecksContext.Equal("仪表内存：空快照状态正常", GaugeStatus.Normal, PerformanceGaugeMath.ComputeMemory(0).Status);
        CheckApprox("仪表内存：半目标 → 50%", 50, PerformanceGaugeMath.ComputeMemory(PerformanceHints.IdleBudgetBytes / 2).Percent, 0.001);
        CheckApprox("仪表内存：空闲目标 → 100%", 100, PerformanceGaugeMath.ComputeMemory(PerformanceHints.IdleBudgetBytes).Percent, 0.001);
        ChecksContext.Equal("仪表内存：空闲目标状态危险", GaugeStatus.Critical, PerformanceGaugeMath.ComputeMemory(PerformanceHints.IdleBudgetBytes).Status);
        ChecksContext.Equal("仪表内存：超过空闲目标仍 100%", 100, PerformanceGaugeMath.ComputeMemory(PerformanceHints.IdleBudgetBytes * 3).Percent);
        ChecksContext.Equal("仪表内存：超过空闲目标状态危险", GaugeStatus.Critical, PerformanceGaugeMath.ComputeMemory(PerformanceHints.IdleBudgetBytes * 3).Status);

        // 75% 告警线 = 250 MB × 75% = 187.5 MB。
        double warningBytes = PerformanceHints.IdleBudgetBytes * 0.75;
        ChecksContext.Equal("仪表内存：75% 状态告警", GaugeStatus.Warning, PerformanceGaugeMath.ComputeMemory(warningBytes).Status);
        ChecksContext.Equal("仪表内存：略低于 75% 状态正常", GaugeStatus.Normal, PerformanceGaugeMath.ComputeMemory(warningBytes - 1).Status);
    }

    private static void CheckCache()
    {
        ChecksContext.Equal("仪表缓存：0 → 0%", 0, PerformanceGaugeMath.ComputeCache(0).Percent);
        CheckApprox("仪表缓存：半预算 → 50%", 50, PerformanceGaugeMath.ComputeCache(PerformanceHints.CacheBudgetBytes / 2).Percent, 0.001);
        ChecksContext.Equal("仪表缓存：预算值状态危险", GaugeStatus.Critical, PerformanceGaugeMath.ComputeCache(PerformanceHints.CacheBudgetBytes).Status);
        ChecksContext.Equal("仪表缓存：超预算仍 100%", 100, PerformanceGaugeMath.ComputeCache(PerformanceHints.CacheBudgetBytes + 1024).Percent);
    }

    private static void CheckLog()
    {
        ChecksContext.Equal("仪表日志：0 → 0%", 0, PerformanceGaugeMath.ComputeLog(0).Percent);
        CheckApprox("仪表日志：轮转阈值 → 100%", 100, PerformanceGaugeMath.ComputeLog(FileLogger.MaxBytes).Percent, 0.001);
        ChecksContext.Equal("仪表日志：轮转阈值状态危险", GaugeStatus.Critical, PerformanceGaugeMath.ComputeLog(FileLogger.MaxBytes).Status);
        ChecksContext.Equal("仪表日志：80% 阈值状态告警", GaugeStatus.Warning, PerformanceGaugeMath.ComputeLog(FileLogger.MaxBytes * 8 / 10).Status);
    }

    private static void CheckStartup()
    {
        ChecksContext.Equal("仪表启动：0 → 0%", 0, PerformanceGaugeMath.ComputeStartup(0).Percent);
        CheckApprox("仪表启动：目标 → 100%", 100, PerformanceGaugeMath.ComputeStartup(PerformanceHints.StartupBudgetMilliseconds).Percent, 0.001);
        ChecksContext.Equal("仪表启动：超时状态危险", GaugeStatus.Critical, PerformanceGaugeMath.ComputeStartup(PerformanceHints.StartupBudgetMilliseconds + 1).Status);
        ChecksContext.Equal("仪表启动：半目标状态正常", GaugeStatus.Normal, PerformanceGaugeMath.ComputeStartup(PerformanceHints.StartupBudgetMilliseconds / 2).Status);
    }

    /// <summary>ChecksContext 没有浮点容差断言，这里用 Check 自己包一层。</summary>
    private static void CheckApprox(string label, double expected, double actual, double tolerance) =>
        ChecksContext.Check(
            label,
            Math.Abs(expected - actual) <= tolerance,
            "期望 " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "±" + tolerance.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "，实际 " + actual.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
