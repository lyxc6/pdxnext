namespace PdxLauncher.Core.Online;

/// <summary>
/// 一次主题检查/单项素材校验的输入。全部是值，不碰 IO，便于自检直接钉住判定顺序。
/// </summary>
public readonly record struct ThemeRefreshInput(
    DateTimeOffset Now,
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? RetryAfterUtc,
    bool IndexEmpty,
    bool UrlChanged,
    bool FileMissing,
    bool Force);

/// <summary>为什么这次要（或不要）联网检查主题。</summary>
public enum ThemeRefreshReason
{
    /// <summary>用户点了「立即检查」，绕过 TTL 与退避。</summary>
    Force,

    /// <summary>上次有素材失败，仍在 10 分钟退避窗口内：一个请求都不发。</summary>
    Backoff,

    /// <summary>索引里没有任何素材条目（冷启动/索引损坏/旧 bug 产物）。</summary>
    IndexEmpty,

    /// <summary>远端 URL 变了（URL 带内容 hash，等价于素材版本变了）。</summary>
    UrlChanged,

    /// <summary>索引有记录但缓存文件已丢失（被清理、被杀毒软件删掉等）。</summary>
    FileMissing,

    /// <summary>失败退避已到点，提前重试，不必等满 TTL。</summary>
    RetryDue,

    /// <summary>从未成功检查过。</summary>
    NeverChecked,

    /// <summary>上次成功检查已超过 TTL（24 小时）。</summary>
    TtlExpired,

    /// <summary>仍新鲜：直接读缓存，不发请求。</summary>
    Fresh,
}

/// <summary>决策结果：要不要联网 + 可 grep 的原因。</summary>
public readonly record struct ThemeRefreshResult(bool ShouldRefresh, ThemeRefreshReason Reason);

/// <summary>
/// 主题刷新判定。判定顺序是行为契约：
/// force → 退避窗口 → 索引为空 → URL 变 → 文件缺失 → 退避到点 → 从未检查 → TTL → 新鲜。
/// 旧实现把「退避未到点」漏成了继续走 TTL 判断，导致一次失败后每次启动都重撞网络；
/// 这里的 <see cref="ThemeRefreshReason.Backoff"/> 早退就是那条回归的唯一定义点。
/// </summary>
public static class ThemeRefreshDecision
{
    /// <summary>默认 TTL：24 小时。条件请求几乎零流量，缩短到一天能及时接到服务端原地换图。</summary>
    public const long DefaultTtlMilliseconds = 86_400_000;

    public static ThemeRefreshResult Evaluate(ThemeRefreshInput input, long ttlMilliseconds = DefaultTtlMilliseconds)
    {
        if (input.Force)
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.Force);
        }

        // 关键回归：退避窗口内必须立刻判定「不刷新」，绝不能继续落到 TTL 判断。
        if (input.RetryAfterUtc is { } retryAfter && input.Now < retryAfter)
        {
            return new ThemeRefreshResult(false, ThemeRefreshReason.Backoff);
        }

        if (input.IndexEmpty)
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.IndexEmpty);
        }

        if (input.UrlChanged)
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.UrlChanged);
        }

        if (input.FileMissing)
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.FileMissing);
        }

        if (input.RetryAfterUtc is not null)
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.RetryDue);
        }

        if (input.LastCheckedUtc is not { } lastChecked || lastChecked == default(DateTimeOffset))
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.NeverChecked);
        }

        if (input.Now - lastChecked >= TimeSpan.FromMilliseconds(ttlMilliseconds))
        {
            return new ThemeRefreshResult(true, ThemeRefreshReason.TtlExpired);
        }

        return new ThemeRefreshResult(false, ThemeRefreshReason.Fresh);
    }
}
