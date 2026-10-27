namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Infrastructure;

/// <summary>
/// <see cref="DiscoveryCache{T}"/> 的自检：TTL 命中 / 过期重扫 / 显式失效 / in-flight 去重 /
/// 单个调用方取消不打断共享扫描。
/// <para>
/// 这套缓存是「切换游戏很慢」修复的地基之一：DLC 与 mod 目录扫描原先没有缓存，
/// 一次切换会被主页、DLC 页、mod 页、mod 库页各触发一遍。
/// </para>
/// </summary>
public static class DiscoveryCacheChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckHitAndTtl();
        CheckInvalidateAndClear();
        CheckInFlightDeduplication();
        CheckCallerCancellationIsolation();
        CheckDefaultTtl();
        CheckArgumentValidation();

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------------ 命中与过期

    private static void CheckHitAndTtl()
    {
        Console.WriteLine("[DiscoveryCacheChecks] 命中与 TTL");

        var clock = new FakeClock();
        var cache = new DiscoveryCache<string>(clock, TimeSpan.FromSeconds(60));
        var calls = 0;

        Task<string> Factory()
        {
            calls++;
            return Task.FromResult("v" + calls);
        }

        ChecksContext.Equal("首次未命中，工厂调用 1 次", "v1", cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("工厂累计 1 次", 1, calls);
        ChecksContext.Equal("缓存条目数 1", 1, cache.Count);

        // TTL 内：直接命中，不再调工厂。
        clock.Advance(TimeSpan.FromSeconds(59));
        ChecksContext.Equal("TTL 内返回旧值", "v1", cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("TTL 内工厂仍只 1 次", 1, calls);

        // 跨过 TTL：重扫。
        clock.Advance(TimeSpan.FromSeconds(2));
        ChecksContext.Equal("TTL 过期后重扫", "v2", cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("过期后工厂 2 次", 2, calls);

        // 不同 key 各自独立。
        ChecksContext.Equal("不同 key 独立缓存", "v3", cache.GetOrCreateAsync("other", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("两个 key 共 3 次", 3, calls);
        ChecksContext.Equal("缓存条目数 2", 2, cache.Count);

        // key 大小写不敏感：调用方常用 UI 里的大写 gameId。
        ChecksContext.Equal("key 大小写不敏感", "v3", cache.GetOrCreateAsync("OTHER", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("大小写变体不增加工厂调用", 3, calls);
    }

    // ------------------------------------------------------------------ 失效与清空

    private static void CheckInvalidateAndClear()
    {
        Console.WriteLine("[DiscoveryCacheChecks] 失效与清空");

        var clock = new FakeClock();
        var cache = new DiscoveryCache<string>(clock);
        var calls = 0;

        Task<string> Factory()
        {
            calls++;
            return Task.FromResult("v" + calls);
        }

        cache.GetOrCreateAsync("a", Factory, CancellationToken.None).GetAwaiter().GetResult();
        cache.GetOrCreateAsync("b", Factory, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("前置：2 次工厂调用", 2, calls);

        ChecksContext.Check("Invalidate 命中并返回 true", cache.Invalidate("a"));
        ChecksContext.Check("Invalidate 未命中返回 false", !cache.Invalidate("missing"));
        ChecksContext.Equal("失效后条目数 1", 1, cache.Count);

        ChecksContext.Equal("失效后重扫 a", "v3", cache.GetOrCreateAsync("a", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("b 仍命中旧值", "v2", cache.GetOrCreateAsync("b", Factory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("累计 3 次", 3, calls);

        cache.Clear();
        ChecksContext.Equal("清空后条目数 0", 0, cache.Count);
        cache.GetOrCreateAsync("a", Factory, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("清空后重新扫描", 4, calls);
    }

    // ------------------------------------------------------------------ in-flight 去重

    private static void CheckInFlightDeduplication()
    {
        Console.WriteLine("[DiscoveryCacheChecks] in-flight 去重");

        var clock = new FakeClock();
        var cache = new DiscoveryCache<string>(clock);
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> SlowFactory()
        {
            Interlocked.Increment(ref calls);
            await release.Task.ConfigureAwait(false);
            return "shared";
        }

        // 三路并发：都应该拿到同一个任务，工厂只跑一次。
        var first = cache.GetOrCreateAsync("k", SlowFactory, CancellationToken.None);
        var second = cache.GetOrCreateAsync("k", SlowFactory, CancellationToken.None);
        var third = cache.GetOrCreateAsync("k", SlowFactory, CancellationToken.None);

        release.SetResult();

        var results = Task.WhenAll(first, second, third).GetAwaiter().GetResult();
        ChecksContext.Equal("并发三路工厂只跑 1 次", 1, calls);
        ChecksContext.Equal("并发三路拿到同一个值", 3, results.Count(r => r == "shared"));

        // 完成后 in-flight 已摘除，但结果已进缓存，仍不重扫。
        ChecksContext.Equal("完成后仍命中缓存", "shared", cache.GetOrCreateAsync("k", SlowFactory, CancellationToken.None).GetAwaiter().GetResult());
        ChecksContext.Equal("命中缓存后工厂仍 1 次", 1, calls);
    }

    // ------------------------------------------------------------------ 取消隔离

    private static void CheckCallerCancellationIsolation()
    {
        Console.WriteLine("[DiscoveryCacheChecks] 取消隔离");

        var clock = new FakeClock();
        var cache = new DiscoveryCache<string>(clock);
        var calls = 0;
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> SlowFactory()
        {
            Interlocked.Increment(ref calls);
            await release.Task.ConfigureAwait(false);
            return "ok";
        }

        using var cancelled = new CancellationTokenSource();
        var doomed = cache.GetOrCreateAsync("k", SlowFactory, cancelled.Token);
        var survivor = cache.GetOrCreateAsync("k", SlowFactory, CancellationToken.None);

        // 取消第一个调用方的等待：它应该抛 OCE，但不影响共享任务与第二个调用方。
        cancelled.Cancel();
        var doomedThrew = false;
        try
        {
            doomed.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            doomedThrew = true;
        }

        release.SetResult();

        ChecksContext.Check("被取消的调用方抛 OCE", doomedThrew);
        ChecksContext.Equal("另一个调用方仍拿到结果", "ok", survivor.GetAwaiter().GetResult());
        ChecksContext.Equal("共享扫描只跑 1 次", 1, calls);

        // 已经取消的令牌：直接拒绝，不进工厂。
        using var already = new CancellationTokenSource();
        already.Cancel();
        var rejected = false;
        try
        {
            cache.GetOrCreateAsync("fresh", SlowFactory, already.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            rejected = true;
        }

        ChecksContext.Check("已取消的令牌直接抛出，不进工厂", rejected);
        ChecksContext.Equal("被拒绝的请求没有新增扫描", 1, calls);
    }

    // ------------------------------------------------------------------ 默认值与入参校验

    private static void CheckDefaultTtl()
    {
        Console.WriteLine("[DiscoveryCacheChecks] 默认 TTL");

        ChecksContext.Equal("默认 TTL 60 秒", TimeSpan.FromSeconds(60), DiscoveryCache<string>.DefaultTtl);

        // 不传 ttl 时应等价于 DefaultTtl。
        var clock = new FakeClock();
        var cache = new DiscoveryCache<string>(clock);
        var calls = 0;

        Task<string> Factory()
        {
            calls++;
            return Task.FromResult("v" + calls);
        }

        cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult();
        clock.Advance(DiscoveryCache<string>.DefaultTtl - TimeSpan.FromSeconds(1));
        cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("默认 TTL 内命中", 1, calls);

        clock.Advance(TimeSpan.FromSeconds(2));
        cache.GetOrCreateAsync("k", Factory, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("默认 TTL 外重扫", 2, calls);
    }

    private static void CheckArgumentValidation()
    {
        Console.WriteLine("[DiscoveryCacheChecks] 入参校验");

        var clock = new FakeClock();

        var nullClock = false;
        try
        {
            _ = new DiscoveryCache<string>(null!);
        }
        catch (ArgumentNullException)
        {
            nullClock = true;
        }

        ChecksContext.Check("时钟为 null 抛 ArgumentNullException", nullClock);

        var cache = new DiscoveryCache<string>(clock);
        var blankKey = false;
        try
        {
            cache.GetOrCreateAsync("  ", () => Task.FromResult("x"), CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ArgumentException)
        {
            blankKey = true;
        }

        ChecksContext.Check("空白 key 抛 ArgumentException", blankKey);

        var nullFactory = false;
        try
        {
            cache.GetOrCreateAsync("k", null!, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (ArgumentNullException)
        {
            nullFactory = true;
        }

        ChecksContext.Check("工厂为 null 抛 ArgumentNullException", nullFactory);
    }

    // ------------------------------------------------------------------ 替身

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = new(2026, 10, 27, 0, 0, 0, TimeSpan.Zero);

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }
}
