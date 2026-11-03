namespace PdxLauncher.Core.Online.Translation;

/// <summary>
/// 翻译端点的健康状态：记住「这台主机连不上」与「这个免密钥接口的额度几时恢复」。
/// <para>
/// 为什么必须是进程级：翻译协调器每批都 <c>new</c> 一套引擎（设置可能随时变化），
/// 实例字段活不过一个批次。若把状态放在实例里，同一段网络故障会被每一段简介、
/// 每一条评论重新付一次超时——实测在谷歌端点被墙的网络里，一页要多等十几秒。
/// </para>
/// <para>
/// 时间通过构造函数注入，测试可以自己拨表；默认取 <see cref="DateTimeOffset.UtcNow"/>。
/// </para>
/// </summary>
public sealed class TranslationEndpointHealth
{
    /// <summary>进程级共享实例，正式代码用这个。</summary>
    public static TranslationEndpointHealth Shared { get; } = new();

    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _deadUntil = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<DateTimeOffset> _now;

    /// <summary>创建健康状态。</summary>
    /// <param name="now">取当前时间；null 表示用系统时间（UTC）。</param>
    public TranslationEndpointHealth(Func<DateTimeOffset>? now = null) =>
        _now = now ?? (static () => DateTimeOffset.UtcNow);

    /// <summary>当前时间（测试注入的表）。</summary>
    public DateTimeOffset Now => _now();

    /// <summary>免密钥额度最近一次被用尽时的恢复时间；null 表示没记录。</summary>
    public DateTimeOffset? QuotaResumeAt { get; private set; }

    /// <summary>额度是否仍在冷却期内。</summary>
    public bool IsQuotaExhausted => QuotaResumeAt is { } until && until > Now;

    /// <summary>取这台主机的冷却截止时间；没被标记过返回 null。</summary>
    /// <param name="host">主机名。</param>
    public DateTimeOffset? GetDeadUntil(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_gate)
        {
            return _deadUntil.TryGetValue(host, out var until) ? until : null;
        }
    }

    /// <summary>这台主机现在是否还在冷却期。</summary>
    /// <param name="host">主机名。</param>
    public bool IsDead(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return GetDeadUntil(host) is { } until && until > Now;
    }

    /// <summary>把一台主机标记为「暂时连不上」。</summary>
    /// <param name="host">主机名。</param>
    /// <param name="ttl">冷却多久；小于等于 0 时按 5 分钟处理。</param>
    public void MarkDead(string host, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(host);
        var until = Now + (ttl > TimeSpan.Zero ? ttl : TimeSpan.FromMinutes(5));
        lock (_gate)
        {
            _deadUntil[host] = until;
        }
    }

    /// <summary>这台主机又通了：清掉冷却。</summary>
    /// <param name="host">主机名。</param>
    public void MarkHealthy(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        lock (_gate)
        {
            _deadUntil.Remove(host);
        }
    }

    /// <summary>记录额度用尽的恢复时间。</summary>
    /// <param name="until">恢复时间。</param>
    public void MarkQuotaExhaustedUntil(DateTimeOffset until)
    {
        lock (_gate)
        {
            QuotaResumeAt = until;
        }
    }

    /// <summary>清空全部状态（自检用；正式代码不需要）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _deadUntil.Clear();
            QuotaResumeAt = null;
        }
    }
}
