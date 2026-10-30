namespace PdxLauncher.Core.Online;

/// <summary>
/// 联网域名白名单。这是本产品相对官方启动器的安全加固：
/// 只有 P 社元数据/素材域名与 Steam 商店域名允许被请求，其它一律拒绝。
/// </summary>
public static class OnlineHosts
{
    /// <summary>主题素材 CDN（实测图片 URL 均在此域名下）。</summary>
    public const string MetadataAssetsHost = "metadata-assets.paradox-interactive.com";

    /// <summary>GraphQL 元数据接口域名。</summary>
    public const string ApiHost = "api.paradox-interactive.com";

    /// <summary>Steam 商店接口域名（未安装游戏封面用）。</summary>
    public const string SteamStoreHost = "store.steampowered.com";

    /// <summary>官方公告图 CDN（Braze content cards 的图片地址）。</summary>
    public const string BrazeImagesHost = "braze-images.com";

    /// <summary>Steam 用户内容 CDN（mods.thumbnailUrl 指向的 mod 缩略图）。</summary>
    public const string SteamUserContentHost = "images.steamusercontent.com";

    /// <summary>
    /// Imgur 图床：Steam 创意工坊的简介配图大量放在这里（作者手写 BBCode 的 <c>[img]</c>），
    /// 不通融就只剩占位图标。只放行这两个精确主机，不做子域通配。
    /// </summary>
    public const string ImgurHost = "imgur.com";

    /// <summary>Imgur 图片直链主机（实际图片都在 <c>i.imgur.com</c>）。</summary>
    public const string ImgurImagesHost = "i.imgur.com";

    /// <summary>PDX Mods 内容 CDN（mod 封面、repository 文件与安装包都从这里取）。</summary>
    public const string ModsContentHost = "modscontent.paradox-interactive.com";

    /// <summary>PDX Mods 网页端（mod 详情页跳转用）。</summary>
    public const string ModsWebHost = "mods.paradoxplaza.com";

    /// <summary>Steam 创意工坊网页端（列表页解析 SSR 数据）。</summary>
    public const string SteamCommunityHost = "steamcommunity.com";

    /// <summary>Steam Web API（keyless 的 published file 详情接口）。</summary>
    public const string SteamApiHost = "api.steampowered.com";

    /// <summary>
    /// 内置免费翻译服务（Google 翻译的公开 Web 接口）。
    /// 只放行这一个精确主机：用户自填的 API 走「已授权主机」机制，不在这里写死。
    /// </summary>
    public const string TranslationHost = "translate.googleapis.com";

    /// <summary>
    /// 用户自行授权的额外主机（自填 OpenAI 兼容 API 的域名）。
    /// 授权后按精确主机名放行，仍然不做子域通配。
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> AuthorizedHosts =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>GetMetadata 的 GraphQL 端点（实测匿名可用）。</summary>
    public static Uri MetadataEndpoint { get; } = new("https://api.paradox-interactive.com/metadata/graphql");

    /// <summary>PDX Mods 列表/搜索端点（<c>POST /mods</c>，匿名可读）。</summary>
    public static Uri ModsSearchEndpoint { get; } = new("https://api.paradox-interactive.com/mods");

    /// <summary>Steam 创意工坊列表页（HTML + <c>valve-ssr-data</c>）。</summary>
    public static Uri SteamWorkshopBrowseEndpoint { get; } = new("https://steamcommunity.com/workshop/browse/");

    /// <summary>Steam 创意工坊物品页（作者主页、描述配图与评论区都在这份 HTML 里）。</summary>
    public static Uri SteamCommunityItemEndpoint { get; } = new("https://steamcommunity.com/sharedfiles/filedetails/");

    /// <summary>
    /// Steam 创意工坊评论翻页接口的前缀：<c>…/comment/PublishedFile_Public/render/{作者}/{文件}/</c>。
    /// <para>物品页只内嵌首屏评论，翻页由页面脚本请求这个地址，返回 JSON 里的 <c>comments_html</c>。</para>
    /// </summary>
    public static Uri SteamCommunityCommentEndpoint { get; } =
        new("https://steamcommunity.com/comment/PublishedFile_Public/render/");


    /// <summary>Steam 商店 appdetails（<c>filters=categories</c> 用来判定有没有创意工坊）。</summary>
    public static Uri SteamAppDetailsEndpoint { get; } = new("https://store.steampowered.com/api/appdetails");

    /// <summary>Steam keyless 的已发布文件详情（只吃 form-urlencoded）。</summary>
    public static Uri SteamPublishedFileDetailsEndpoint { get; } =
        new("https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/");

    /// <summary>主机名是否在白名单内（大小写不敏感，只允许精确匹配，不允许子域）。</summary>
    public static bool IsAllowedHost(string? host) =>
        string.Equals(host, MetadataAssetsHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ApiHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamStoreHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, BrazeImagesHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamUserContentHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ImgurHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ImgurImagesHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ModsContentHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ModsWebHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamCommunityHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamApiHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, TranslationHost, StringComparison.OrdinalIgnoreCase)
        || IsAuthorizedHost(host);

    /// <summary>
    /// 授权一个额外主机（用户在设置页自填翻译 API 时调用）。
    /// 只放行精确主机名，不做子域通配；传 null/空白/非法主机一律忽略。
    /// </summary>
    /// <param name="host">主机名（不含协议、端口与路径）。</param>
    public static void AuthorizeHost(string? host)
    {
        var normalized = NormalizeHost(host);
        if (normalized is not null)
        {
            AuthorizedHosts[normalized] = byte.MaxValue;
        }
    }

    /// <summary>撤销之前授权的主机。</summary>
    /// <param name="host">主机名。</param>
    public static void RevokeHost(string? host)
    {
        var normalized = NormalizeHost(host);
        if (normalized is not null)
        {
            AuthorizedHosts.TryRemove(normalized, out _);
        }
    }

    /// <summary>主机是否已被用户授权。</summary>
    /// <param name="host">主机名。</param>
    public static bool IsAuthorizedHost(string? host)
    {
        var normalized = NormalizeHost(host);
        return normalized is not null && AuthorizedHosts.ContainsKey(normalized);
    }

    /// <summary>把用户填的主机名收拾成规范形式；非法输入返回 null。</summary>
    private static string? NormalizeHost(string? host)
    {
        var trimmed = host?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        // 允许用户直接贴完整地址；只取主机部分。
        if (trimmed.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var parsed))
            {
                return null;
            }

            trimmed = parsed.Host;
        }

        trimmed = trimmed.Trim().TrimEnd('.');
        if (trimmed.Length == 0 || trimmed.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed;
    }

    /// <summary>绝对 URI 是否可请求：必须是 http/https 且主机在白名单内。</summary>
    public static bool IsAllowed(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsAllowedHost(uri.Host);
    }
}
