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

    /// <summary>PDX Mods 内容 CDN（mod 封面、repository 文件与安装包都从这里取）。</summary>
    public const string ModsContentHost = "modscontent.paradox-interactive.com";

    /// <summary>PDX Mods 网页端（mod 详情页跳转用）。</summary>
    public const string ModsWebHost = "mods.paradoxplaza.com";

    /// <summary>Steam 创意工坊网页端（列表页解析 SSR 数据）。</summary>
    public const string SteamCommunityHost = "steamcommunity.com";

    /// <summary>Steam Web API（keyless 的 published file 详情接口）。</summary>
    public const string SteamApiHost = "api.steampowered.com";

    /// <summary>GetMetadata 的 GraphQL 端点（实测匿名可用）。</summary>
    public static Uri MetadataEndpoint { get; } = new("https://api.paradox-interactive.com/metadata/graphql");

    /// <summary>PDX Mods 列表/搜索端点（<c>POST /mods</c>，匿名可读）。</summary>
    public static Uri ModsSearchEndpoint { get; } = new("https://api.paradox-interactive.com/mods");

    /// <summary>Steam 创意工坊列表页（HTML + <c>valve-ssr-data</c>）。</summary>
    public static Uri SteamWorkshopBrowseEndpoint { get; } = new("https://steamcommunity.com/workshop/browse/");

    /// <summary>Steam 创意工坊物品页（作者主页、描述配图与评论区都在这份 HTML 里）。</summary>
    public static Uri SteamCommunityItemEndpoint { get; } = new("https://steamcommunity.com/sharedfiles/filedetails/");

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
        || string.Equals(host, ModsContentHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, ModsWebHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamCommunityHost, StringComparison.OrdinalIgnoreCase)
        || string.Equals(host, SteamApiHost, StringComparison.OrdinalIgnoreCase);

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
