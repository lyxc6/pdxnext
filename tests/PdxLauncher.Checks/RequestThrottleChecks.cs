namespace PdxLauncher.Checks;

using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 出口闸门（<see cref="RequestThrottle"/>）与限流响应头解析的自检。
/// <para>
/// 起因：Steam 创意工坊对同一 IP 的请求密度有限制，原来的 <see cref="HttpFetcher"/> 完全不看 429，
/// 被限流后仍按原节奏继续发，<see cref="AssetDownloader"/> 还会把同一次失败重试 3 次。
/// 这组用例不联网：所有响应头都是本地构造的 <see cref="HttpResponseMessage"/>。
/// </para>
/// </summary>
public static class RequestThrottleChecks
{
    private const string Host = "steamcommunity.com";

    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        CheckRetryAfterParsing();
        CheckThrottleStatusDetection();
        CheckCooldownRejectsWithoutDelay();
        CheckConsecutiveThrottleDoubles();
        CheckCooldownCapped();
        CheckSuccessResetsBackoff();
        CheckConcurrencyCap();
        CheckMinIntervalSpacing();
        CheckSnapshotCounters();
        return ChecksContext.Failures - before;
    }

    /// <summary><c>Retry-After</c> 支持秒数与 HTTP-date 两种写法，并夹在 1 小时以内。</summary>
    private static void CheckRetryAfterParsing()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

        ChecksContext.Equal(
            "限流：Retry-After 秒数",
            TimeSpan.FromSeconds(120),
            HttpThrottleHeaders.ReadRetryAfter(Headers("Retry-After", "120"), clock));

        ChecksContext.Equal(
            "限流：Retry-After 超过上限被夹到 1 小时",
            HttpThrottleHeaders.MaxRetryAfter,
            HttpThrottleHeaders.ReadRetryAfter(Headers("Retry-After", "99999"), clock));

        var date = clock.UtcNow.AddSeconds(90);
        ChecksContext.Equal(
            "限流：Retry-After HTTP-date 换算成剩余时长",
            TimeSpan.FromSeconds(90),
            HttpThrottleHeaders.ReadRetryAfter(
                Headers("Retry-After", date.ToString("R")),
                clock));

        ChecksContext.Check(
            "限流：没有 Retry-After 时返回 null",
            HttpThrottleHeaders.ReadRetryAfter(Headers("X-Test", "1"), clock) is null);

        ChecksContext.Check(
            "限流：已经过期的 HTTP-date 返回零（不再等）",
            HttpThrottleHeaders.ReadRetryAfter(
                Headers("Retry-After", clock.UtcNow.AddMinutes(-1).ToString("R")),
                clock) is { } span && span <= TimeSpan.Zero);

        ChecksContext.Check(
            "限流：非法取值当作没有头",
            HttpThrottleHeaders.ReadRetryAfter(Headers("Retry-After", "not-a-date"), clock) is null);
    }

    /// <summary>429 一律算限流；503 只有带 <c>Retry-After</c> 才算。</summary>
    private static void CheckThrottleStatusDetection()
    {
        ChecksContext.Check(
            "限流：429 视为被限流",
            HttpThrottleHeaders.IsThrottleStatus(429, Headers("X-Test", "1")));

        ChecksContext.Check(
            "限流：503 带 Retry-After 视为被限流",
            HttpThrottleHeaders.IsThrottleStatus(503, Headers("Retry-After", "30")));

        ChecksContext.Check(
            "限流：503 不带 Retry-After 不算被限流",
            !HttpThrottleHeaders.IsThrottleStatus(503, Headers("X-Test", "1")));

        ChecksContext.Check(
            "限流：200 不算被限流",
            !HttpThrottleHeaders.IsThrottleStatus(200, null));
    }

    /// <summary>冷却期内申请槽位必须直接拒绝，且不消耗任何等待时间。</summary>
    private static void CheckCooldownRejectsWithoutDelay()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var delays = 0;
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))),
            (_, _) =>
            {
                delays++;
                return Task.CompletedTask;
            });

        throttle.ReportThrottled(Host, TimeSpan.FromMinutes(15));

        var rejected = RunAcquire(throttle, Host);
        ChecksContext.Check(
            "限流：冷却期内申请被拒绝",
            rejected is SteamThrottleException { RejectedByCooldown: true },
            rejected?.GetType().Name ?? "(没有抛异常)");

        ChecksContext.Check(
            "限流：被拒绝时也不等待",
            delays == 0,
            "delays=" + delays);

        ChecksContext.Check(
            "限流：冷却剩余时间可查",
            throttle.TryGetCooldown(Host, out var untilUtc)
                && untilUtc - clock.UtcNow == TimeSpan.FromMinutes(15),
            throttle.CooldownRemaining(Host).ToString());
    }

    /// <summary>连续被限流时冷却翻倍（15 分钟 → 30 分钟）。</summary>
    private static void CheckConsecutiveThrottleDoubles()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60))),
            (_, _) => Task.CompletedTask);

        throttle.ReportThrottled(Host, null);
        var first = throttle.CooldownRemaining(Host);
        throttle.ReportThrottled(Host, null);
        var second = throttle.CooldownRemaining(Host);

        ChecksContext.Equal("限流：第一次无头冷却用策略默认值", TimeSpan.FromMinutes(5), first);
        ChecksContext.Equal("限流：第二次冷却翻倍", TimeSpan.FromMinutes(10), second);

        throttle.ReportSuccess(Host);
        throttle.ReportThrottled(Host, null);
        ChecksContext.Equal("限流：成功一次后重新从默认值起算", TimeSpan.FromMinutes(5), throttle.CooldownRemaining(Host));
    }

    /// <summary>翻倍不能突破策略上限。</summary>
    private static void CheckCooldownCapped()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(60))),
            (_, _) => Task.CompletedTask);

        for (var i = 0; i < 6; i++)
        {
            throttle.ReportThrottled(Host, null);
        }

        ChecksContext.Equal(
            "限流：连续触发后冷却不超过上限",
            TimeSpan.FromMinutes(60),
            throttle.CooldownRemaining(Host));
    }

    /// <summary>长时间的 Retry-After 也按上限截断。</summary>
    private static void CheckSuccessResetsBackoff()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60))),
            (_, _) => Task.CompletedTask);

        throttle.ReportThrottled(Host, TimeSpan.FromHours(10));
        ChecksContext.Equal(
            "限流：Retry-After 超过上限时按上限冷却",
            TimeSpan.FromMinutes(60),
            throttle.CooldownRemaining(Host));

        clock.Advance(TimeSpan.FromMinutes(61));
        ChecksContext.Check("限流：冷却结束后可再申请", RunAcquire(throttle, Host) is null);
    }

    /// <summary>并发上限：第 3 个请求必须等前两个之一释放。</summary>
    private static void CheckConcurrencyCap()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))),
            (_, _) => Task.CompletedTask);

        var first = throttle.AcquireAsync(Host, CancellationToken.None).GetAwaiter().GetResult();
        var second = throttle.AcquireAsync(Host, CancellationToken.None).GetAwaiter().GetResult();
        var third = throttle.AcquireAsync(Host, CancellationToken.None);

        Thread.Sleep(80);
        ChecksContext.Check("限流：超过并发上限的请求在排队", !third.IsCompleted);

        first.Dispose();
        var admitted = third.Wait(TimeSpan.FromSeconds(2));
        ChecksContext.Check("限流：释放一个名额后排队的请求拿到槽位", admitted);

        if (admitted)
        {
            third.Result.Dispose();
        }

        second.Dispose();
    }

    /// <summary>最小间隔：第二个请求必须等够策略里的间隔。</summary>
    private static void CheckMinIntervalSpacing()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var waits = new List<TimeSpan>();
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((4, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))),
            (span, _) =>
            {
                waits.Add(span);
                return Task.CompletedTask;
            });

        throttle.AcquireAsync(Host, CancellationToken.None).GetAwaiter().GetResult().Dispose();
        throttle.AcquireAsync(Host, CancellationToken.None).GetAwaiter().GetResult().Dispose();

        ChecksContext.Equal("限流：间隔内只等一次", 1, waits.Count);
        ChecksContext.Check(
            "限流：等待时长约等于最小间隔",
            waits.Count == 1 && waits[0] >= TimeSpan.FromMilliseconds(500) && waits[0] <= TimeSpan.FromSeconds(1),
            waits.Count == 1 ? waits[0].ToString() : "(没有等待)");
    }

    /// <summary>统计：总请求数、被限流次数、冷却期拒绝次数分开记。</summary>
    private static void CheckSnapshotCounters()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
        var throttle = new RequestThrottle(
            new NullLogger(),
            clock,
            Policies((2, TimeSpan.Zero, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30))),
            (_, _) => Task.CompletedTask);

        throttle.AcquireAsync(Host, CancellationToken.None).GetAwaiter().GetResult().Dispose();
        throttle.ReportThrottled(Host, TimeSpan.FromMinutes(1));
        RunAcquire(throttle, Host);

        var stats = throttle.Snapshot();
        ChecksContext.Check("限流：统计里有该 host", stats.TryGetValue(Host, out var hostStats));
        if (stats.TryGetValue(Host, out var snapshot))
        {
            ChecksContext.Equal("限流：请求计数", 1L, snapshot.Total);
            ChecksContext.Equal("限流：被限流计数", 1L, snapshot.Throttled);
            ChecksContext.Equal("限流：冷却拒绝计数", 1L, snapshot.Rejected);
            ChecksContext.Check("限流：统计显示处于冷却中", snapshot.InCooldown);
        }
    }

    private static Exception? RunAcquire(RequestThrottle throttle, string host)
    {
        try
        {
            throttle.AcquireAsync(host, CancellationToken.None).GetAwaiter().GetResult().Dispose();
            return null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ex;
        }
    }

    private static HttpResponseHeaders Headers(string name, string value)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation(name, value);
        return response.Headers;
    }

    private static IReadOnlyDictionary<string, HostPolicy> Policies(
        (int MaxConcurrent, TimeSpan MinInterval, TimeSpan CooldownWhenNoHeader, TimeSpan MaxCooldown) values) =>
        new Dictionary<string, HostPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            [Host] = new(values.MaxConcurrent, values.MinInterval, values.CooldownWhenNoHeader, values.MaxCooldown),
        };

    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
