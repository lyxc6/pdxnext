namespace PdxLauncher.Core.Online;

/// <summary>一条条目页缓存：解析结果的序列化形式（字段名即 JSON 名）。</summary>
public sealed class CachedSteamItemPage
{
    /// <summary>抓取时间（UTC），用于 TTL 判定与容量淘汰。</summary>
    public DateTimeOffset FetchedUtc { get; set; }

    /// <summary>作者显示名。</summary>
    public string? AuthorName { get; set; }

    /// <summary>作者主页。</summary>
    public string? AuthorProfileUrl { get; set; }

    /// <summary>描述原文（HTML）。</summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>描述里内嵌的图片。</summary>
    public List<string> ImageUrls { get; set; } = [];

    /// <summary>官方画廊截图。</summary>
    public List<string> ScreenshotUrls { get; set; } = [];

    /// <summary>讨论总数。</summary>
    public int TotalComments { get; set; }

    /// <summary>首屏评论。</summary>
    public List<SteamWorkshopComment> Comments { get; set; } = [];
}
