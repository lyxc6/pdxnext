namespace PdxLauncher.Core.Contracts;

/// <summary>启动 PDS 游戏时传给游戏的 Paradox 启动会话凭据。</summary>
public sealed class LaunchAuth
{
    /// <summary>创建启动凭据。</summary>
    public LaunchAuth(string sessionToken, string paradoxUserId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(paradoxUserId);
        SessionToken = sessionToken;
        ParadoxUserId = paradoxUserId;
    }

    /// <summary>官方参数 <c>--pdx-launcher-session-token</c> 的值。</summary>
    public string SessionToken { get; }

    /// <summary>官方参数 <c>--paradox-account-userid</c> 的值。</summary>
    public string ParadoxUserId { get; }

    /// <summary>避免凭据被记录到普通对象描述中。</summary>
    public override string ToString() => "LaunchAuth(<redacted>)";
}
