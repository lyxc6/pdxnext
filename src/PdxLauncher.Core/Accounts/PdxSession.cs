namespace PdxLauncher.Core.Accounts;

/// <summary>
/// 一次成功的 Paradox 登录会话。
/// <para>
/// <see cref="SessionToken"/> 是后续所有 PDX 请求的凭据；<see cref="RefreshToken"/> 只在
/// 用户勾了「记住我」时才有（官方启动器同样如此），没有它就过期后必须重新输密码。
/// </para>
/// </summary>
/// <param name="SessionToken">会话 token（放进 <c>{"session":{"token":"…"}}</c>）。</param>
/// <param name="RefreshToken">续期 token；可能为 null。</param>
/// <param name="UserId">Paradox 账号 id（官方叫 <c>userid</c>）。</param>
/// <param name="Email">登录邮箱。</param>
/// <param name="DisplayName">显示名；服务端没给时回落为邮箱。</param>
/// <param name="ObtainedAt">拿到会话的时间（本地时钟），用于判断是否该续期。</param>
public sealed record PdxSession(
    string SessionToken,
    string? RefreshToken,
    string UserId,
    string Email,
    string DisplayName,
    DateTimeOffset ObtainedAt)
{
    /// <summary>
    /// 会话的保守有效期。官方启动器没有下发明确的过期时刻，只在内存里持有到进程结束；
    /// 我们落盘，所以按 24 小时保守处理，超过就先续期（有 refresh token 时）。
    /// </summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    /// <summary>按本地时钟判断是否已经到该续期的时间。</summary>
    public bool NeedsRefresh(DateTimeOffset now) => now - ObtainedAt >= Lifetime;

    /// <summary>能否在过期后自动续期（没有 refresh token 就只能重新登录）。</summary>
    public bool CanRefresh => !string.IsNullOrWhiteSpace(RefreshToken);
}
