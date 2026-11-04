namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 优化建议规则的自检：每个阈值两侧各一条（不报 / 报），同一类内存问题只出一条，
/// 输出顺序固定。规则错了不会崩，只会在性能页上给出错误的「去优化什么」，
/// 所以边界值由这里逐条钉住。
/// </summary>
public static class PerformanceHintsChecks
{
    private const long Megabyte = 1024L * 1024L;

    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        CheckMemory();
        CheckThrottle();
        CheckStartup();
        CheckCacheAndWorkshop();
        CheckLog();
        CheckTranslation();
        CheckOrdering();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckMemory()
    {
        ChecksContext.Equal("性能建议：无输入时没有建议", 0, PerformanceHints.Evaluate(Defaults()).Count);
        ChecksContext.Equal("性能建议：249 MB 不报内存", 0, PerformanceHints.Evaluate(Defaults() with { WorkingSetBytes = 249 * Megabyte }).Count);
        ChecksContext.Equal("性能建议：250 MB 报空闲内存超标", 1, PerformanceHints.Evaluate(Defaults() with { WorkingSetBytes = 250 * Megabyte }).Count);
        ChecksContext.Equal(
            "性能建议：549 MB 仍报空闲内存超标",
            DiagnosticHintCode.MemoryHigh,
            PerformanceHints.Evaluate(Defaults() with { WorkingSetBytes = 549 * Megabyte })[0].Code);
        ChecksContext.Equal(
            "性能建议：550 MB 报详情预算超标",
            DiagnosticHintCode.MemoryAboveDetailBudget,
            PerformanceHints.Evaluate(Defaults() with { WorkingSetBytes = 550 * Megabyte })[0].Code);

        var both = PerformanceHints.Evaluate(Defaults() with { WorkingSetBytes = 900 * Megabyte });
        ChecksContext.Equal("性能建议：内存只出一条建议", 1, both.Count);
        ChecksContext.Equal("性能建议：建议携带实测字节", 900L * Megabyte, both[0].Value);
    }

    private static void CheckThrottle()
    {
        ChecksContext.Equal(
            "性能建议：没有拒发时不报网络",
            0,
            PerformanceHints.Evaluate(Defaults() with { ThrottleRejectedHosts = 0 }).Count);

        var hint = PerformanceHints.Evaluate(Defaults() with
        {
            ThrottleRejectedHosts = 2,
            BusiestRejectedHost = "steamcommunity.com",
            BusiestRejectedCount = 17,
        });

        ChecksContext.Equal("性能建议：有拒发时报网络建议", 1, hint.Count);
        ChecksContext.Equal("性能建议：网络建议带域名", "steamcommunity.com", hint[0].Subject);
        ChecksContext.Equal("性能建议：网络建议带次数", 17L, hint[0].Value);
    }

    private static void CheckStartup()
    {
        ChecksContext.Equal(
            "性能建议：没测到启动耗时不报启动慢",
            0,
            PerformanceHints.Evaluate(Defaults() with { StartupElapsedMilliseconds = null }).Count);
        ChecksContext.Equal(
            "性能建议：2499 ms 不报启动慢",
            0,
            PerformanceHints.Evaluate(Defaults() with { StartupElapsedMilliseconds = 2499 }).Count);

        var slow = PerformanceHints.Evaluate(Defaults() with { StartupElapsedMilliseconds = 2500 });
        ChecksContext.Equal("性能建议：2500 ms 报启动慢", 1, slow.Count);
        ChecksContext.Equal("性能建议：启动建议类型", DiagnosticHintCode.StartupSlow, slow[0].Code);
        ChecksContext.Equal("性能建议：启动建议取整毫秒", 2500L, slow[0].Value);
    }

    private static void CheckCacheAndWorkshop()
    {
        ChecksContext.Equal(
            "性能建议：499 MB 缓存不报",
            0,
            PerformanceHints.Evaluate(Defaults() with { CacheBytes = 499 * Megabyte }).Count);
        ChecksContext.Equal(
            "性能建议：500 MB 缓存报超标",
            1,
            PerformanceHints.Evaluate(Defaults() with { CacheBytes = 500 * Megabyte }).Count);

        ChecksContext.Equal(
            "性能建议：1999 条工坊缓存不报",
            0,
            PerformanceHints.Evaluate(Defaults() with { WorkshopCacheEntries = 1999 }).Count);
        ChecksContext.Equal(
            "性能建议：2000 条工坊缓存报超标",
            1,
            PerformanceHints.Evaluate(Defaults() with { WorkshopCacheEntries = 2000 }).Count);
    }

    private static void CheckLog()
    {
        ChecksContext.Equal("性能建议：空日志不报轮转", 0, PerformanceHints.Evaluate(Defaults()).Count);
        ChecksContext.Equal(
            "性能建议：轮转阈值前 1 字节不报",
            0,
            PerformanceHints.Evaluate(Defaults() with { LogBytes = PerformanceHints.LogNearRotationBytes - 1 }).Count);

        var near = PerformanceHints.Evaluate(Defaults() with { LogBytes = PerformanceHints.LogNearRotationBytes });
        ChecksContext.Equal("性能建议：达到 80% 报接近轮转", 1, near.Count);
        ChecksContext.Equal("性能建议：轮转建议类型", DiagnosticHintCode.LogNearRotation, near[0].Code);

        ChecksContext.Equal(
            "性能建议：两份归档不报",
            0,
            PerformanceHints.Evaluate(Defaults() with { LogArchiveCount = 2 }).Count);
        ChecksContext.Equal(
            "性能建议：三份归档报「下次会丢最老」",
            1,
            PerformanceHints.Evaluate(Defaults() with { LogArchiveCount = 3 }).Count);
    }

    private static void CheckTranslation()
    {
        ChecksContext.Equal(
            "性能建议：翻译未开启不报常驻",
            0,
            PerformanceHints.Evaluate(Defaults() with { TranslationEnabled = false, KeepTranslationModelLoaded = true }).Count);
        ChecksContext.Equal(
            "性能建议：翻译开启但不常驻不报",
            0,
            PerformanceHints.Evaluate(Defaults() with { TranslationEnabled = true, KeepTranslationModelLoaded = false }).Count);
        ChecksContext.Equal(
            "性能建议：翻译开启且常驻报提示",
            1,
            PerformanceHints.Evaluate(Defaults() with { TranslationEnabled = true, KeepTranslationModelLoaded = true }).Count);
    }

    private static void CheckOrdering()
    {
        var all = PerformanceHints.Evaluate(new PerformanceHintInput(
            WorkingSetBytes: 600 * Megabyte,
            CacheBytes: 600 * Megabyte,
            WorkshopCacheEntries: 3000,
            LogBytes: PerformanceHints.LogNearRotationBytes,
            LogArchiveCount: 3,
            TranslationEnabled: true,
            KeepTranslationModelLoaded: true,
            ThrottleRejectedHosts: 1,
            BusiestRejectedHost: "api.steampowered.com",
            BusiestRejectedCount: 3,
            StartupElapsedMilliseconds: 3000));

        var codes = string.Join(",", all.Select(hint => hint.Code.ToString()));
        ChecksContext.Equal(
            "性能建议：全部触发时按严重度排序",
            "MemoryAboveDetailBudget,ThrottleRejected,StartupSlow,CacheLarge,WorkshopCacheLarge,LogNearRotation,LogArchives,TranslationModelResident",
            codes);
        ChecksContext.Equal("性能建议：全部触发共 8 条", 8, all.Count);
    }

    private static PerformanceHintInput Defaults() => new(
        WorkingSetBytes: 0,
        CacheBytes: 0,
        WorkshopCacheEntries: 0,
        LogBytes: 0,
        LogArchiveCount: 0,
        TranslationEnabled: false,
        KeepTranslationModelLoaded: false,
        ThrottleRejectedHosts: 0,
        BusiestRejectedHost: null,
        BusiestRejectedCount: 0,
        StartupElapsedMilliseconds: null);
}
