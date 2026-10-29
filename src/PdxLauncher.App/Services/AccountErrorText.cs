namespace PdxLauncher.App.Services;

/// <summary>登录失败的归类：界面据此选一句人话，而不是把内部错误码怼到用户脸上。</summary>
public enum AccountErrorCategory
{
    /// <summary>凭据不对（邮箱不存在 / 密码错 / 会话失效）。</summary>
    BadCredentials,

    /// <summary>连不上（DNS / TLS / 超时 / 代理）。</summary>
    Network,

    /// <summary>其它：没有专门文案，退回显示错误码本身。</summary>
    Other
}

/// <summary>
/// 把 <c>Core</c> 的错误码归类。纯 C#，不引用任何 UI 类型，可脱离 UI 自检。
/// </summary>
/// <remarks>
/// 之所以单独抽出来：这里曾经漏判过一次。客户端发的是
/// <c>pdx-login-unauthorized</c> / <c>pdx-refresh-unauthorized</c>（带 <c>login</c> 段），
/// 而归类写的是 <c>pdx-unauthorized</c>，于是「密码错了」被当成未知错误，
/// 界面直接显示 <c>登录失败：pdx-login-unauthorized</c> —— 用户完全看不出是自己密码打错了。
/// 这种「码对不上文案」的错不会崩、不会报错，只会在真机上表现为「登录坏了」，
/// 所以归类的口径要能被自检钉住。
/// </remarks>
public static class AccountErrorText
{
    /// <summary>按错误码判定该给用户看哪一类文案。</summary>
    /// <param name="errorCode">Core 返回的错误码，如 <c>pdx-login-unauthorized</c>、<c>pdx-login-failed:500</c>。</param>
    public static AccountErrorCategory Describe(string? errorCode)
    {
        var code = errorCode ?? string.Empty;

        // 用 Contains("unauthorized") 而不是精确串：登录与续期两个前缀不同，
        // 将来再加 endpoint 也不该漏掉这一族。
        if (code.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
            || code.Contains("401", StringComparison.Ordinal)
            || code.Contains("403", StringComparison.Ordinal))
        {
            return AccountErrorCategory.BadCredentials;
        }

        // 同理用 Contains("network")：客户端给的是 pdx-login-network（带 login 段），
        // 精确匹配 pdx-network 会漏掉它，网络故障就会被当成未知错误露出原始码。
        if (code.Contains("network", StringComparison.OrdinalIgnoreCase)
            || code.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return AccountErrorCategory.Network;
        }

        return AccountErrorCategory.Other;
    }
}
