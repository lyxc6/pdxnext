namespace PdxLauncher.Core.ModStudio;

/// <summary>
/// 「把已有的创意工坊条目绑定到本地 mod」的结果。
/// <para>
/// 绑定本身与工坊查询是两件事：查询失败（Steam 没开、没登录、缺 dll、条目隐藏到读不到）
/// 不影响绑定的成败，只影响 <see cref="Verified"/>——见 <see cref="VerifyError"/>。
/// </para>
/// </summary>
/// <param name="Ok">绑定是否已写入。</param>
/// <param name="Publication">写入的那条记录；失败时为 null。</param>
/// <param name="Error">失败原因（阻断性）；成功时为 null。</param>
/// <param name="Verified">是否从 Steam 读回了详情（此时 <see cref="ModPublication.SteamVisibility"/> 有值）。</param>
/// <param name="VerifyError">没读到详情的原因；绑定仍可能成功。</param>
public sealed record ModBindResult(
    bool Ok,
    ModPublication? Publication,
    string? Error,
    bool Verified,
    string? VerifyError)
{
    /// <summary>绑定失败。</summary>
    public static ModBindResult Fail(string error) => new(false, null, error, false, null);

    /// <summary>绑定成功；<paramref name="verified"/> 为假时 <paramref name="verifyError"/> 说明原因。</summary>
    public static ModBindResult Bound(ModPublication publication, bool verified, string? verifyError)
    {
        ArgumentNullException.ThrowIfNull(publication);
        return new(true, publication, null, verified, verifyError);
    }
}
