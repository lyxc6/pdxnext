namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 单个 host 的请求策略。
/// </summary>
/// <param name="MaxConcurrent">同时最多几个在途请求（从发起到拿到响应头）。</param>
/// <param name="MinInterval">两次请求发起之间的最小间隔；<see cref="TimeSpan.Zero"/> 表示不限。</param>
/// <param name="CooldownWhenNoHeader">被限流但响应没给 <c>Retry-After</c> 时，默认停发多久。</param>
/// <param name="MaxCooldown">冷却时间的硬上限（连续被限流会翻倍，但不能无限涨）。</param>
public sealed record HostPolicy(
    int MaxConcurrent,
    TimeSpan MinInterval,
    TimeSpan CooldownWhenNoHeader,
    TimeSpan MaxCooldown);

/// <summary>
/// 被服务端限流（429，或带 Retry-After 的 503）时抛出的异常。
/// <para>
/// 两种情况都归到这里：真正收到 429 的一次（<see cref="RejectedByCooldown"/> 为 false），
/// 以及冷却期内被本机闸门直接拒发的一次（为 true，请求根本没出去）。
/// 调用方只需要「现在别发、过一会儿再试」这一个语义，不需要区分是哪种。
/// </para>
/// </summary>
public sealed class SteamThrottleException : HttpRequestException
{
    public SteamThrottleException(
        string host,
        TimeSpan? retryAfter,
        bool rejectedByCooldown,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Host = host;
        RetryAfter = retryAfter;
        RejectedByCooldown = rejectedByCooldown;
    }

    /// <summary>被限流的 host。</summary>
    public string Host { get; }

    /// <summary>服务端建议的等待时长；没给 <c>Retry-After</c> 时为 null。</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>true = 冷却期内被本机闸门拒发（请求没出网）。</summary>
    public bool RejectedByCooldown { get; }
}

/// <summary>
/// 429 / <c>Retry-After</c> 的解析。全是纯函数，便于离线自检。
/// </summary>
public static class HttpThrottleHeaders
{
    /// <summary>单个 <c>Retry-After</c> 的解析上限：服务端给个离谱值（比如一天）时不要把闸门焊死。</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(1);

    /// <summary>
    /// 解析 <c>Retry-After</c>：既支持秒数（<c>120</c>），也支持 HTTP-date。
    /// 头缺失、格式不认识时返回 null；值 ≤0 返回 <see cref="TimeSpan.Zero"/>。
    /// </summary>
    /// <param name="headers">响应头；null 直接返回 null。</param>
    /// <param name="clock">HTTP-date 的相对时间基准；null 用系统时间。</param>
    public static TimeSpan? ReadRetryAfter(HttpResponseHeaders? headers, IClock? clock = null)
    {
        if (headers is null || !headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var now = clock?.UtcNow ?? DateTimeOffset.UtcNow;
        foreach (var value in values)
        {
            var raw = value?.Trim();
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            {
                if (double.IsNaN(seconds))
                {
                    continue;
                }

                return Clamp(seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds));
            }

            if (DateTimeOffset.TryParse(
                    raw,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var when))
            {
                var delta = when - now;
                return Clamp(delta <= TimeSpan.Zero ? TimeSpan.Zero : delta);
            }
        }

        return null;
    }

    /// <summary>
    /// 这个状态码是不是「被限流」：429 恒是；503 只有带 <c>Retry-After</c> 才算
    /// （没有 Retry-After 的 503 通常是上游故障，重试也没用，不该触发长冷却）。
    /// </summary>
    public static bool IsThrottleStatus(int statusCode, HttpResponseHeaders? headers) =>
        statusCode == 429 || (statusCode == 503 && ReadRetryAfter(headers) is not null);

    private static TimeSpan Clamp(TimeSpan value) => value > MaxRetryAfter ? MaxRetryAfter : value;
}

/// <summary>某个 host 的实时统计（日志与界面提示用）。</summary>
public sealed record HostStats(
    int MaxConcurrent,
    bool InCooldown,
    TimeSpan CooldownRemaining,
    long Total,
    long Throttled,
    long Rejected,
    DateTimeOffset LastRequestUtc);

/// <summary>
/// 出口闸门：按 host 限制并发与发起间隔，并在被限流后进入冷却（冷却期内本地快速失败，请求不出网）。
/// <para>
/// 装在 <see cref="HttpFetcher"/> 这个唯一出口上，而不是各个调用点：调用点太多、加新集成也不会
/// 有人记得补护栏（白名单当初就是因此挪到出口的）。闸门只约束「请求发起到拿到响应头」这一段，
/// 不约束响应体流式下载——图片仍可能长时间占用连接，但不会再被无节制地并发发起。
/// </para>
/// <para>
/// 冷却状态只在进程内保存：重启后从零开始。Steam 的限流窗口比进程寿命长得多，但重启本身就
/// 意味着用户已经等了一段时间；把熔断持久化反而会让「误判一次就锁死到明天」。
/// </para>
/// </summary>
public sealed class RequestThrottle
{
    /// <summary>单次连续被限流时冷却翻倍的次数上限（2^4 = 16 倍）。</summary>
    private const int MaxDoublings = 4;

    /// <summary>各 host 的默认策略；构造时可整体替换。</summary>
    public static IReadOnlyDictionary<string, HostPolicy> DefaultPolicies { get; } =
        new Dictionary<string, HostPolicy>(StringComparer.OrdinalIgnoreCase)
        {
            // Steam 社区页是最贵的：一页 browse 1–3 MB，且限流是按这个域名判的。
            ["steamcommunity.com"] = new(
                MaxConcurrent: 2,
                MinInterval: TimeSpan.FromSeconds(1),
                CooldownWhenNoHeader: TimeSpan.FromMinutes(15),
                MaxCooldown: TimeSpan.FromMinutes(60)),

            // 图片 CDN 便宜但数量大（一页 30 张封面 + 画廊），并发放宽、间隔只做微节流。
            ["images.steamusercontent.com"] = new(
                MaxConcurrent: 4,
                MinInterval: TimeSpan.FromMilliseconds(100),
                CooldownWhenNoHeader: TimeSpan.FromMinutes(5),
                MaxCooldown: TimeSpan.FromMinutes(30)),

            ["api.steampowered.com"] = new(
                MaxConcurrent: 2,
                MinInterval: TimeSpan.FromMilliseconds(250),
                CooldownWhenNoHeader: TimeSpan.FromMinutes(5),
                MaxCooldown: TimeSpan.FromMinutes(30)),

            ["store.steampowered.com"] = new(
                MaxConcurrent: 2,
                MinInterval: TimeSpan.FromMilliseconds(250),
                CooldownWhenNoHeader: TimeSpan.FromMinutes(5),
                MaxCooldown: TimeSpan.FromMinutes(30)),
        };

    /// <summary>没单独配置的 host（PDX 平台、Braze 等）用这条：并发有上限、不做节奏限制。</summary>
    public static HostPolicy DefaultPolicy { get; } = new(
        MaxConcurrent: 4,
        MinInterval: TimeSpan.Zero,
        CooldownWhenNoHeader: TimeSpan.FromMinutes(5),
        MaxCooldown: TimeSpan.FromMinutes(30));

    private readonly IAppLogger _logger;
    private readonly IReadOnlyDictionary<string, HostPolicy> _policies;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Dictionary<string, HostState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _statesGate = new();

    /// <param name="logger">日志出口。</param>
    /// <param name="clock">时间基准；自检注入 FixedClock。</param>
    /// <param name="policies">host 策略表；null 用 <see cref="DefaultPolicies"/>。</param>
    /// <param name="delay">等待实现；null 用 <see cref="Task.Delay(TimeSpan, CancellationToken)"/>。自检注入以免真等。</param>
    public RequestThrottle(
        IAppLogger logger,
        IClock clock,
        IReadOnlyDictionary<string, HostPolicy>? policies = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _logger = logger;
        Clock = clock;
        _policies = policies ?? DefaultPolicies;
        _delay = delay ?? ((span, ct) => Task.Delay(span, ct));
    }

    /// <summary>时间基准（<see cref="HttpFetcher"/> 解析 <c>Retry-After</c> 的 HTTP-date 时要用）。</summary>
    public IClock Clock { get; }

    /// <summary>某个 host 被限流并进入冷却时触发；参数为 host。订阅方要自己 marshal 到 UI 线程。</summary>
    public event EventHandler<string>? Throttled;

    /// <summary>
    /// 申请一个请求槽位。返回的对象 Dispose 表示「已拿到响应头」，让出并发名额。
    /// 冷却期内直接抛 <see cref="SteamThrottleException"/>，请求不会发出。
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var state = GetState(host);
        var now = Clock.UtcNow;
        lock (state.Sync)
        {
            if (state.CooldownUntilUtc > now)
            {
                state.Rejected++;
                var remaining = state.CooldownUntilUtc - now;
                throw new SteamThrottleException(
                    host,
                    remaining,
                    rejectedByCooldown: true,
                    "限流冷却中，暂停向 " + host + " 发请求（剩余约 " + FormatRemaining(remaining) + "）");
            }
        }

        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TimeSpan wait;
            lock (state.Sync)
            {
                var current = Clock.UtcNow;
                var interval = state.Policy.MinInterval;
                if (interval <= TimeSpan.Zero)
                {
                    wait = TimeSpan.Zero;
                    state.LastRequestUtc = current;
                }
                else
                {
                    var next = state.LastRequestUtc + interval;
                    wait = next > current ? next - current : TimeSpan.Zero;
                    // 预占到「下一次可发」的时间点：并发调用者按顺序排队，而不是都抢同一个 slot 然后同时发。
                    state.LastRequestUtc = next > current ? next : current;
                }

                state.Total++;
            }

            if (wait > TimeSpan.Zero)
            {
                await _delay(wait, cancellationToken).ConfigureAwait(false);
            }

            return new Slot(state);
        }
        catch
        {
            state.Gate.Release();
            throw;
        }
    }

    /// <summary>
    /// 记录一次「真的被限流了」：按 <paramref name="retryAfter"/>（或策略默认值）进入冷却；
    /// 连续被限流时冷却翻倍，但不超过 <see cref="HostPolicy.MaxCooldown"/>。
    /// </summary>
    public void ReportThrottled(string host, TimeSpan? retryAfter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var state = GetState(host);
        TimeSpan cooldown;
        int consecutive;
        lock (state.Sync)
        {
            state.Throttled++;
            state.Consecutive++;
            consecutive = state.Consecutive;

            var baseCooldown = retryAfter ?? state.Policy.CooldownWhenNoHeader;
            if (baseCooldown < TimeSpan.Zero)
            {
                baseCooldown = TimeSpan.Zero;
            }

            var factor = 1 << Math.Min(state.Consecutive - 1, MaxDoublings);
            cooldown = TimeSpan.FromTicks(baseCooldown.Ticks * factor);
            if (cooldown > state.Policy.MaxCooldown)
            {
                cooldown = state.Policy.MaxCooldown;
            }

            if (cooldown < TimeSpan.Zero)
            {
                cooldown = TimeSpan.Zero;
            }

            state.CooldownUntilUtc = Clock.UtcNow + cooldown;
        }

        _logger.Warn(
            "被服务端限流：host=" + host
            + "，暂停 " + FormatRemaining(cooldown)
            + "（连续第 " + consecutive + " 次）"
            + (retryAfter is null ? "，响应未给 Retry-After" : "，Retry-After=" + FormatRemaining(retryAfter.Value))
            + "。当前各 host 统计：" + DescribeSnapshot());

        Throttled?.Invoke(this, host);
    }

    /// <summary>记录一次成功响应：清零连续计数（冷却期本身不动，它只由时间或下一次成功决定）。</summary>
    public void ReportSuccess(string host)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        var state = GetState(host);
        lock (state.Sync)
        {
            state.Consecutive = 0;
        }
    }

    /// <summary>查询某 host 是否还在冷却，以及冷却到什么时候。</summary>
    public bool TryGetCooldown(string host, out DateTimeOffset untilUtc)
    {
        var state = GetState(host);
        lock (state.Sync)
        {
            untilUtc = state.CooldownUntilUtc;
            return untilUtc > Clock.UtcNow;
        }
    }

    /// <summary>当前冷却剩余时长；不在冷却返回 <see cref="TimeSpan.Zero"/>。</summary>
    public TimeSpan CooldownRemaining(string host) =>
        TryGetCooldown(host, out var until) ? until - Clock.UtcNow : TimeSpan.Zero;

    /// <summary>各 host 的累计统计快照（日志与本机自检用）。</summary>
    public IReadOnlyDictionary<string, HostStats> Snapshot()
    {
        var now = Clock.UtcNow;
        var result = new Dictionary<string, HostStats>(StringComparer.OrdinalIgnoreCase);
        lock (_statesGate)
        {
            foreach (var pair in _states)
            {
                var state = pair.Value;
                lock (state.Sync)
                {
                    var remaining = state.CooldownUntilUtc > now ? state.CooldownUntilUtc - now : TimeSpan.Zero;
                    result[pair.Key] = new HostStats(
                        state.Policy.MaxConcurrent,
                        remaining > TimeSpan.Zero,
                        remaining,
                        state.Total,
                        state.Throttled,
                        state.Rejected,
                        state.LastRequestUtc);
                }
            }
        }

        return result;
    }

    /// <summary>一行可读的统计，用于限流日志。</summary>
    public string DescribeSnapshot()
    {
        var parts = new List<string>();
        foreach (var pair in Snapshot())
        {
            var stats = pair.Value;
            parts.Add(
                pair.Key + "(总 " + stats.Total
                + " / 限流 " + stats.Throttled
                + " / 拒发 " + stats.Rejected
                + (stats.InCooldown ? " / 冷却中 " + FormatRemaining(stats.CooldownRemaining) : string.Empty)
                + ")");
        }

        return parts.Count == 0 ? "（无）" : string.Join("；", parts);
    }

    private HostState GetState(string host)
    {
        lock (_statesGate)
        {
            if (_states.TryGetValue(host, out var existing))
            {
                return existing;
            }

            var state = new HostState(ResolvePolicy(host));
            _states[host] = state;
            return state;
        }
    }

    /// <summary>精确匹配优先，其次按「域名后缀」匹配（www.steamcommunity.com 命中 steamcommunity.com）。</summary>
    private HostPolicy ResolvePolicy(string host)
    {
        if (_policies.TryGetValue(host, out var exact))
        {
            return exact;
        }

        foreach (var pair in _policies)
        {
            if (host.EndsWith("." + pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return DefaultPolicy;
    }

    internal static string FormatRemaining(TimeSpan span)
    {
        if (span <= TimeSpan.Zero)
        {
            return "0 秒";
        }

        if (span.TotalSeconds < 60)
        {
            return ((int)Math.Ceiling(span.TotalSeconds)) + " 秒";
        }

        return ((int)Math.Ceiling(span.TotalMinutes)) + " 分钟";
    }

    private sealed class Slot : IDisposable
    {
        private HostState? _state;

        public Slot(HostState state) => _state = state;

        public void Dispose() => Interlocked.Exchange(ref _state, null)?.Gate.Release();
    }
}

/// <summary>
/// 单个 host 的运行时状态：并发信号量 + 节流时间戳 + 冷却与统计。
/// </summary>
internal sealed class HostState
{
    public HostState(HostPolicy policy)
    {
        Policy = policy;
        Gate = new SemaphoreSlim(policy.MaxConcurrent, policy.MaxConcurrent);
    }

    public HostPolicy Policy { get; }

    public SemaphoreSlim Gate { get; }

    public object Sync { get; } = new();

    public DateTimeOffset LastRequestUtc { get; set; }

    public DateTimeOffset CooldownUntilUtc { get; set; }

    public int Consecutive { get; set; }

    public long Total { get; set; }

    public long Throttled { get; set; }

    public long Rejected { get; set; }
}
