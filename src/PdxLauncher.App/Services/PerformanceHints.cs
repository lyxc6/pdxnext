namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Infrastructure;

/// <summary>一条可优化项的类型。页面按它映射标题与说明文案（中英文各一份）。</summary>
public enum DiagnosticHintCode
{
    /// <summary>工作集超过工坊详情页预算（550 MB）。</summary>
    MemoryAboveDetailBudget,

    /// <summary>工作集超过空闲目标（250 MB）。</summary>
    MemoryHigh,

    /// <summary>有域名出现「拒发」，多半是代理/DNS 连不上。</summary>
    ThrottleRejected,

    /// <summary>冷启动到首页可交互超过 2.5 秒。</summary>
    StartupSlow,

    /// <summary>缓存合计超过 500 MB。</summary>
    CacheLarge,

    /// <summary>工坊缓存条目（条目 + 分页）达到 2000。</summary>
    WorkshopCacheLarge,

    /// <summary>日志接近 5 MB 轮转阈值。</summary>
    LogNearRotation,

    /// <summary>历史日志已有 3 份，下一次轮转会丢弃最老的一份。</summary>
    LogArchives,

    /// <summary>翻译开启且模型常驻（约 1.3 GB）。</summary>
    TranslationModelResident,
}

/// <summary>
/// 一条优化建议。<see cref="Value"/> 是触发阈值时的实测值（字节 / 毫秒 / 个数，按 <see cref="Code"/> 解释），
/// <see cref="Subject"/> 目前只用于 <see cref="DiagnosticHintCode.ThrottleRejected"/> 的域名。
/// </summary>
public sealed record DiagnosticHint(DiagnosticHintCode Code, long Value, string? Subject = null);

/// <summary>规则引擎输入。全部字段都由页面在统计完成后填好，规则本身不读任何全局状态。</summary>
public sealed record PerformanceHintInput(
    long WorkingSetBytes,
    long CacheBytes,
    int WorkshopCacheEntries,
    long LogBytes,
    int LogArchiveCount,
    bool TranslationEnabled,
    bool KeepTranslationModelLoaded,
    int ThrottleRejectedHosts,
    string? BusiestRejectedHost,
    long BusiestRejectedCount,
    double? StartupElapsedMilliseconds);

/// <summary>
/// 「性能与日志」页面的优化建议规则。阈值与 <c>docs/MEMORY-BASELINE.md</c> 的验收目标一致：
/// 空闲 250 MB、工坊详情 550 MB、冷启动 2.5 秒；缓存 500 MB、工坊条目 2000、日志 80% 轮转阈值是本页新增的观察线。
/// 纯函数、无 IO、无 UI，方便自检把每个边界都钉住。
/// </summary>
public static class PerformanceHints
{
    /// <summary>工坊详情页的内存预算（MEMORY-BASELINE 的 550 MB）。</summary>
    public const long DetailBudgetBytes = 550L * 1024 * 1024;

    /// <summary>空闲态内存目标（MEMORY-BASELINE 的 250 MB）。</summary>
    public const long IdleBudgetBytes = 250L * 1024 * 1024;

    /// <summary>缓存合计的观察线。</summary>
    public const long CacheBudgetBytes = 500L * 1024 * 1024;

    /// <summary>日志轮转阈值的 80%，越过就提示「即将轮转」。</summary>
    public const long LogNearRotationBytes = FileLogger.MaxBytes * 8 / 10;

    /// <summary>工坊缓存条目（条目 + 分页）的观察线。</summary>
    public const int WorkshopEntryBudget = 2000;

    /// <summary>冷启动到首页可交互的目标（MEMORY-BASELINE 的 2.5 秒）。</summary>
    public const double StartupBudgetMilliseconds = 2500;

    /// <summary>按严重度从高到低评估；同一类内存问题只出一条（详情预算优先于空闲目标）。</summary>
    public static IReadOnlyList<DiagnosticHint> Evaluate(PerformanceHintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var hints = new List<DiagnosticHint>(9);

        if (input.WorkingSetBytes >= DetailBudgetBytes)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.MemoryAboveDetailBudget, input.WorkingSetBytes));
        }
        else if (input.WorkingSetBytes >= IdleBudgetBytes)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.MemoryHigh, input.WorkingSetBytes));
        }

        if (input.ThrottleRejectedHosts > 0)
        {
            hints.Add(new DiagnosticHint(
                DiagnosticHintCode.ThrottleRejected,
                input.BusiestRejectedCount,
                input.BusiestRejectedHost));
        }

        if (input.StartupElapsedMilliseconds is { } elapsed && elapsed >= StartupBudgetMilliseconds)
        {
            hints.Add(new DiagnosticHint(
                DiagnosticHintCode.StartupSlow,
                (long)Math.Round(elapsed, MidpointRounding.AwayFromZero)));
        }

        if (input.CacheBytes >= CacheBudgetBytes)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.CacheLarge, input.CacheBytes));
        }

        if (input.WorkshopCacheEntries >= WorkshopEntryBudget)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.WorkshopCacheLarge, input.WorkshopCacheEntries));
        }

        if (input.LogBytes >= LogNearRotationBytes)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.LogNearRotation, input.LogBytes));
        }

        if (input.LogArchiveCount >= FileLogger.MaxArchives)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.LogArchives, input.LogArchiveCount));
        }

        if (input.TranslationEnabled && input.KeepTranslationModelLoaded)
        {
            hints.Add(new DiagnosticHint(DiagnosticHintCode.TranslationModelResident, 0));
        }

        return hints;
    }
}
