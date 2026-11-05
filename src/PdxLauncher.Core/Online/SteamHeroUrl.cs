namespace PdxLauncher.Core.Online;

/// <summary>
/// 由 Steam 商店头图地址推导「官方库页大图」（<c>library_hero.jpg</c>）。
/// <para>
/// 为什么不能只换文件名：实测新布局把每张图放在内容 hash 目录里
/// （<c>.../steam/apps/3450310/&lt;hash&gt;/header_alt_assets_3.jpg</c>），
/// 同一 hash 目录下没有 <c>library_hero.jpg</c>（404）；库页大图在 appid 的上一级
/// （<c>.../steam/apps/3450310/library_hero.jpg</c>）。所以
/// <see cref="FromHeaderUrl"/> 保持「只换文件名」的严格语义供断言，
/// 生产路径用 <see cref="ForLibraryHero"/> 去掉 hash 段。
/// </para>
/// </summary>
public static class SteamHeroUrl
{
    /// <summary>商店头图文件名。</summary>
    public const string HeaderFileName = "header.jpg";

    /// <summary>Steam 库页大图文件名（官方标准 1920×620）。</summary>
    public const string HeroFileName = "library_hero.jpg";

    private const string AltHeaderPrefix = "header_alt_assets_";

    /// <summary>
    /// 严格推导：只把结尾的 <c>header.jpg</c> 换成 <c>library_hero.jpg</c>，
    /// 保留 scheme/host/全部路径段与查询串；不是 <c>header.jpg</c> 就返回 null。
    /// 大小写不敏感，输出文件名统一小写。
    /// </summary>
    public static string? FromHeaderUrl(string? headerUrl)
    {
        if (!TryParseHttpUri(headerUrl, out var uri))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        var slash = path.LastIndexOf('/');
        if (slash < 0
            || !string.Equals(path[(slash + 1)..], HeaderFileName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ReplacePath(uri, path[..(slash + 1)] + HeroFileName);
    }

    /// <summary>
    /// 生产推导：从 <c>.../steam/apps/&lt;appid&gt;/(&lt;hash&gt;/)?header[_alt_assets_N].jpg</c>
    /// 得到 <c>.../steam/apps/&lt;appid&gt;/library_hero.jpg</c>。
    /// 必须去掉 hash 段——同目录下没有库页大图。保留原 host、前缀（store_item_assets）和查询串；
    /// 文件名不是头图（含 alt 变体）时返回 null。
    /// </summary>
    public static string? ForLibraryHero(string? headerUrl)
    {
        if (!TryParseHttpUri(headerUrl, out var uri))
        {
            return null;
        }

        var segments = uri.AbsolutePath.Trim('/').Split('/');
        if (segments.Length < 4 || !IsHeaderAsset(segments[^1]))
        {
            return null;
        }

        // 从右往左找 .../steam/apps/<appid>；前缀原样保留，避免把 host/布局写死。
        var appsIndex = -1;
        for (var i = segments.Length - 2; i >= 1; i--)
        {
            if (string.Equals(segments[i], "apps", StringComparison.OrdinalIgnoreCase)
                && string.Equals(segments[i - 1], "steam", StringComparison.OrdinalIgnoreCase))
            {
                appsIndex = i;
                break;
            }
        }

        if (appsIndex < 0 || appsIndex + 1 >= segments.Length || !IsAppId(segments[appsIndex + 1]))
        {
            return null;
        }

        var prefix = string.Join('/', segments, 0, appsIndex + 2);
        return ReplacePath(uri, "/" + prefix + "/" + HeroFileName);
    }

    private static bool IsHeaderAsset(string fileName) =>
        string.Equals(fileName, HeaderFileName, StringComparison.OrdinalIgnoreCase)
        || (fileName.StartsWith(AltHeaderPrefix, StringComparison.OrdinalIgnoreCase)
            && fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            && IsAllDigits(fileName.AsSpan(AltHeaderPrefix.Length, fileName.Length - AltHeaderPrefix.Length - 4)));

    private static bool IsAppId(string value)
    {
        if (!IsAllDigits(value) || value.Length > 9)
        {
            return false;
        }

        return int.TryParse(value, out var appId) && appId > 0;
    }

    private static bool IsAllDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryParseHttpUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value)
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        uri = parsed;
        return true;
    }

    private static string ReplacePath(Uri uri, string path)
    {
        var builder = new UriBuilder(uri) { Path = path };
        return builder.Uri.AbsoluteUri;
    }
}
