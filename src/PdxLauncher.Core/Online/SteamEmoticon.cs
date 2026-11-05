namespace PdxLauncher.Core.Online;

/// <summary>
/// Steam 创意工坊评论里的表情：<c>community.fastly.steamstatic.com/economy/emoticon/&lt;name&gt;</c>。
/// <para>
/// 这类地址没有扩展名，而且和头像/徽章共用同一个官方 CDN，所以「能不能当表情」不能只看主机；
/// 这里把协议、精确主机、路径前缀与名称合法性一次判完，并给出可缓存的规范化 URL
/// （去掉 query/fragment，避免同一张表情因为参数不同被存成多份）。
/// </para>
/// <para>
/// Steam 会把同一份表情轮换到 <c>community.akamai.steamstatic.com</c>（实测与 fastly 字节一致）。
/// 下载白名单仍然只放行 fastly，所以这里把 akamai 别名规范化成 fastly，不扩大网络放行面。
/// </para>
/// </summary>
public static class SteamEmoticon
{
    /// <summary>表情路径前缀。</summary>
    public const string PathPrefix = "/economy/emoticon/";

    /// <summary>Steam 轮换使用的官方 CDN 别名；只用于解析，不加入下载白名单。</summary>
    private const string AlternateAssetsHost = "community.akamai.steamstatic.com";

    /// <summary>URL 是否是官方表情（<paramref name="alt"/> 只用来优先取短码）。</summary>
    /// <param name="url">图片地址。</param>
    /// <param name="alt">图片 alt（Steam 给 <c>:name:</c>）。</param>
    public static bool IsEmoticonImage(string? url, string? alt) =>
        TryCreate(url, alt, out _, out _);

    /// <summary>
    /// 解析官方表情地址：成功时给出规范化 URL 与短码。
    /// 失败（http、未知主机、路径不对、名称畸形）时只返回 false，调用方按普通文本降级。
    /// </summary>
    /// <param name="url">图片地址。</param>
    /// <param name="alt">图片 alt。</param>
    /// <param name="normalizedUrl">去掉 query/fragment、主机统一为 fastly 的 https 地址。</param>
    /// <param name="shortCode">形如 <c>:highrise:</c> 的短码。</param>
    public static bool TryCreate(
        string? url,
        string? alt,
        out string normalizedUrl,
        out string shortCode)
    {
        normalizedUrl = string.Empty;
        shortCode = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.AbsolutePath.StartsWith(PathPrefix, StringComparison.Ordinal)
            || !IsOfficialHost(uri.Host))
        {
            return false;
        }

        var name = uri.AbsolutePath[PathPrefix.Length..];
        if (!IsValidName(name))
        {
            return false;
        }

        normalizedUrl = "https://" + OnlineHosts.SteamCommunityAssetsHost + PathPrefix + name;
        shortCode = TryGetShortCode(alt, out var fromAlt) ? fromAlt : ":" + name + ":";
        return true;
    }

    /// <summary>alt 是否就是 Steam 的 <c>:name:</c> 短码。</summary>
    /// <param name="alt">图片 alt。</param>
    /// <param name="shortCode">去空白后的短码。</param>
    public static bool TryGetShortCode(string? alt, out string shortCode)
    {
        shortCode = string.Empty;
        if (alt is null)
        {
            return false;
        }

        var trimmed = alt.Trim();
        if (trimmed.Length < 3 || trimmed[0] != ':' || trimmed[^1] != ':')
        {
            return false;
        }

        for (var index = 1; index < trimmed.Length - 1; index++)
        {
            var ch = trimmed[index];
            if (!char.IsLetterOrDigit(ch) && ch is not ('_' or '-' or '+'))
            {
                return false;
            }
        }

        shortCode = trimmed;
        return true;
    }

    /// <summary>只认官方 fastly 主机及其 akamai 轮换别名；子域一律拒绝。</summary>
    private static bool IsOfficialHost(string host) =>
        string.Equals(host, OnlineHosts.SteamCommunityAssetsHost, StringComparison.Ordinal)
        || string.Equals(host, AlternateAssetsHost, StringComparison.Ordinal);

    /// <summary>表情名称：非空、不含路径分隔符/空白、不是 <c>.</c> 或 <c>..</c>。</summary>
    private static bool IsValidName(string name)
    {
        if (name.Length == 0 || name is "." or "..")
        {
            return false;
        }

        foreach (var ch in name)
        {
            if (ch is '/' or '\\' || char.IsWhiteSpace(ch))
            {
                return false;
            }
        }

        return true;
    }
}
