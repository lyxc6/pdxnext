namespace PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊的排序档位。界面只有三档（两平台共用一套文案），
/// 但 Steam 在带关键词时只认自己的 <c>textsearch</c>，所以多一档内部值。
/// </summary>
public enum SteamWorkshopSort
{
    /// <summary>最热门（Steam <c>mostpopular</c> / PDX <c>popularity</c>）。</summary>
    MostPopular,

    /// <summary>最近更新（Steam <c>mostrecent</c> / PDX <c>created</c>）。</summary>
    MostRecent,

    /// <summary>评分最高（Steam <c>toprated</c> / PDX <c>rating</c>）。</summary>
    TopRated,

    /// <summary>关键词搜索专用（Steam 把排序固定在 <c>textsearch</c>）。</summary>
    TextSearch,
}

/// <summary>创意工坊列表查询条件。页码从 0 开始，与服务端 <c>p</c>（从 1 开始）差一由客户端换算。</summary>
/// <param name="SearchText">关键词；为空表示纯浏览。</param>
/// <param name="Sort">排序档位。</param>
/// <param name="Page">页码，从 0 开始。</param>
/// <param name="PageSize">每页条数。</param>
public sealed record SteamWorkshopQuery(
    string? SearchText,
    SteamWorkshopSort Sort,
    int Page,
    int PageSize = SteamWorkshopClient.DefaultPageSize);

/// <summary>条目标签。</summary>
/// <param name="Tag">机器标签。</param>
/// <param name="DisplayName">展示名。</param>
public sealed record SteamWorkshopTag(string Tag, string DisplayName);

/// <summary>创意工坊列表里的一条。</summary>
/// <param name="PublishedFileId">Steam 已发布文件 id（字符串，避免前导零/精度问题）。</param>
/// <param name="Title">标题。</param>
/// <param name="ShortDescription">列表里的简介。</param>
/// <param name="PreviewUrl">缩略图地址（images.steamusercontent.com）。</param>
/// <param name="AuthorSteamId">作者 SteamID64。</param>
/// <param name="Subscriptions">当前订阅数。</param>
/// <param name="Favorites">收藏数。</param>
/// <param name="Views">浏览数。</param>
/// <param name="CreatedUtc">创建时间。</param>
/// <param name="UpdatedUtc">更新时间。</param>
/// <param name="FileSizeBytes">文件体积（服务端给的是字符串）。</param>
/// <param name="Tags">标签。</param>
/// <param name="StarRating">星级评分（0–5）。</param>
/// <param name="TotalVotes">评分人数。</param>
/// <param name="ConsumerAppId">所属游戏 appid。</param>
public sealed record SteamWorkshopItem(
    string PublishedFileId,
    string Title,
    string ShortDescription,
    string? PreviewUrl,
    string AuthorSteamId,
    long Subscriptions,
    long Favorites,
    long Views,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    long? FileSizeBytes,
    IReadOnlyList<SteamWorkshopTag> Tags,
    double StarRating,
    int TotalVotes,
    int ConsumerAppId);

/// <summary>一页创意工坊结果。</summary>
/// <param name="Items">条目。</param>
/// <param name="TotalCount">总条数。</param>
/// <param name="Page">页码（从 0 开始）。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="TotalPages">总页数。</param>
public sealed record SteamWorkshopPage(
    IReadOnlyList<SteamWorkshopItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages)
{
    /// <summary>空页（用于页码越界时兜底）。</summary>
    public static SteamWorkshopPage Empty(int page, int pageSize) =>
        new([], 0, page, pageSize, 0);
}

/// <summary>创意工坊条目详情（来自 keyless GetPublishedFileDetails）。</summary>
/// <param name="PublishedFileId">文件 id。</param>
/// <param name="Title">标题。</param>
/// <param name="Description">完整描述（含 BBCode 标记的纯文本）。</param>
/// <param name="PreviewUrl">缩略图地址。</param>
/// <param name="FileSizeBytes">体积。</param>
/// <param name="CreatedUtc">创建时间。</param>
/// <param name="UpdatedUtc">更新时间。</param>
/// <param name="Filename">原始文件名（可能是空串）。</param>
public sealed record SteamWorkshopItemDetail(
    string PublishedFileId,
    string Title,
    string Description,
    string? PreviewUrl,
    long FileSizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? Filename);
