namespace PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊的排序档位，对齐 Steam 创意工坊网页自己的「排序依据」下拉框：
/// 最热门（<c>trend</c>）、最受好评（<c>toprated</c>）、最近发行（<c>mostrecent</c>）、
/// 最新更新（<c>lastupdated</c>）、不重复订阅者总计（<c>totaluniquesubscribers</c>）。
/// <para>
/// PDX Mods 只有「最热门 / 最新更新 / 评分最高」三档，映射关系见 <c>WorkshopViewModel</c>。
/// <c>TextSearch</c> 是 Steam 自己的「搜索相关度」这一档（<c>textsearch</c>）；
/// 有关键词时搜索与排序可以并存，服务端照样按 <c>browsesort</c> 排序。
/// </para>
/// </summary>
public enum SteamWorkshopSort
{
    /// <summary>最热门（Steam <c>trend</c> / PDX <c>popularity</c>）。</summary>
    Trend,

    /// <summary>最受好评（Steam <c>toprated</c> / PDX <c>rating</c>）。</summary>
    TopRated,

    /// <summary>最近发行（Steam <c>mostrecent</c>）。</summary>
    MostRecent,

    /// <summary>最新更新（Steam <c>lastupdated</c> / PDX <c>updated</c>）。</summary>
    MostUpdated,

    /// <summary>不重复订阅者总计（Steam <c>totaluniquesubscribers</c>）。</summary>
    MostSubscribed,

    /// <summary>搜索相关度（Steam <c>textsearch</c>）。</summary>
    TextSearch,
}

/// <summary>
/// 创意工坊左侧筛选栏的「特别筛选条件」。
/// <para>
/// 取值来自网页实测（2026-11）：<c>special_filter=6</c> 表示「其他项目需要」，
/// <c>special_filter=7</c> 表示「在合集中」。选中的同时服务端还会把排序换成
/// <c>num_parent_items</c> / <c>num_parent_collections</c>，见 <see cref="SteamWorkshopClient.BuildBrowseUri"/>。
/// </para>
/// </summary>
public enum SteamWorkshopSpecialFilter
{
    /// <summary>无（不传参数）。</summary>
    None = 0,

    /// <summary>其他项目需要（<c>special_filter=6</c>）。</summary>
    RequiredByOtherItems = 6,

    /// <summary>在合集中（<c>special_filter=7</c>）。</summary>
    InCollections = 7,
}

/// <summary>创意工坊列表查询条件。页码从 0 开始，与服务端 <c>p</c>（从 1 开始）差一由客户端换算。</summary>
/// <param name="SearchText">关键词；为空表示纯浏览。</param>
/// <param name="Sort">排序档位。</param>
/// <param name="Page">页码，从 0 开始。</param>
/// <param name="PageSize">每页条数。</param>
/// <param name="Days">
/// 时间范围（Steam 的「时间范围」下拉框，服务端参数名 <c>days</c>）：
/// 1 = 今天、7 = 1 周、30 = 30 天、90 = 3 个月、180 = 6 个月、365 = 1 年、-1 = 发布至今。
/// 只对最热门/最受好评有意义；null 表示不传（服务端默认 7 天）。
/// </param>
/// <param name="RequiredTags">必须包含的标签（<c>requiredtags[]</c>，值是标签名，不是 id）。</param>
/// <param name="ExcludedTags">必须排除的标签（<c>excludedtags[]</c>）。</param>
/// <param name="RequiredAppIds">必须依赖的 DLC appid（<c>appids_required_for_use[]</c>）。</param>
/// <param name="ExcludedAppIds">必须排除的 DLC appid（<c>excluded_appids_required_for_use[]</c>）。</param>
/// <param name="ShowIncompatible">是否显示不兼容项目（<c>requiredflags[]=incompatible</c>）。</param>
/// <param name="SpecialFilter">特别筛选条件。</param>
public sealed record SteamWorkshopQuery(
    string? SearchText,
    SteamWorkshopSort Sort,
    int Page,
    int PageSize = SteamWorkshopClient.DefaultPageSize,
    int? Days = null,
    IReadOnlyList<string>? RequiredTags = null,
    IReadOnlyList<string>? ExcludedTags = null,
    IReadOnlyList<int>? RequiredAppIds = null,
    IReadOnlyList<int>? ExcludedAppIds = null,
    bool ShowIncompatible = false,
    SteamWorkshopSpecialFilter SpecialFilter = SteamWorkshopSpecialFilter.None);

/// <summary>条目标签。</summary>
/// <param name="Tag">机器标签。</param>
/// <param name="DisplayName">展示名。</param>
public sealed record SteamWorkshopTag(string Tag, string DisplayName);

/// <summary>
/// 工坊左侧筛选栏的一个分组（类别 / 更新兼容性 / 所需的 DLC）。
/// <para>
/// 前两组来自 SSR 的 <c>declared_tags_v6.readytouse_tags</c>，<see cref="SteamWorkshopTag.Tag"/> 是标签名；
/// DLC 组来自 <c>workshop_required_dlc_v5</c>，<see cref="SteamWorkshopTag.Tag"/> 是 DLC 的 appid 字符串，
/// 因为服务端参数是 <c>appids_required_for_use[]</c> 而不是标签。
/// </para>
/// </summary>
/// <param name="Key">稳定键（<c>category</c> / <c>compatibility</c> / <c>dlc</c>，SSR 未知组用原标题）。</param>
/// <param name="Title">服务端给的原始标题（可能为空，界面按 <see cref="Key"/> 本地化）。</param>
/// <param name="Tags">分组里的选项。</param>
/// <param name="IsDlc">是否是「所需的 DLC」组（选项值是 appid）。</param>
public sealed record SteamWorkshopFilterGroup(
    string Key,
    string Title,
    IReadOnlyList<SteamWorkshopTag> Tags,
    bool IsDlc = false);

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
/// <param name="FilterGroups">左侧筛选栏的可用分组（缺失时为空）。</param>
public sealed record SteamWorkshopPage(
    IReadOnlyList<SteamWorkshopItem> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages,
    IReadOnlyList<SteamWorkshopFilterGroup>? FilterGroups = null)
{
    /// <summary>筛选组（非 null，便于界面直接绑定）。</summary>
    public IReadOnlyList<SteamWorkshopFilterGroup> Groups => FilterGroups ?? [];

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
/// <param name="CreatorSteamId">作者 SteamID64（详情接口本来就返回，之前丢掉了）。</param>
/// <param name="Tags">标签原文。</param>
/// <param name="Views">浏览数。</param>
/// <param name="LifetimeSubscriptions">历史订阅总数。</param>
public sealed record SteamWorkshopItemDetail(
    string PublishedFileId,
    string Title,
    string Description,
    string? PreviewUrl,
    long FileSizeBytes,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string? Filename,
    string? CreatorSteamId = null,
    IReadOnlyList<string>? Tags = null,
    long Views = 0,
    long LifetimeSubscriptions = 0)
{
    /// <summary>标签（非 null，便于界面直接绑定）。</summary>
    public IReadOnlyList<string> TagList => Tags ?? [];

    /// <summary>作者主页地址；没有 SteamID 时为 null。</summary>
    public string? CreatorProfileUrl => string.IsNullOrWhiteSpace(CreatorSteamId)
        ? null
        : "https://steamcommunity.com/profiles/" + CreatorSteamId;
}

/// <summary>创意工坊条目页（HTML）解析结果。</summary>
/// <param name="AuthorName">作者显示名。</param>
/// <param name="AuthorProfileUrl">作者主页（可能是自定义 /id/ 形式）。</param>
/// <param name="DescriptionHtml">描述原文（HTML）。</param>
/// <param name="ImageUrls">描述里内嵌的图片。</param>
/// <param name="ScreenshotUrls">官方画廊截图（来自 <c>#highlight_player_area</c>），顺序与页面一致。</param>
/// <param name="TotalComments">讨论总数（页面上报的 total_count）。</param>
/// <param name="Comments">首屏评论。</param>
public sealed record SteamWorkshopItemPage(
    string? AuthorName,
    string? AuthorProfileUrl,
    string? DescriptionHtml,
    IReadOnlyList<string> ImageUrls,
    IReadOnlyList<string> ScreenshotUrls,
    int TotalComments,
    IReadOnlyList<SteamWorkshopComment> Comments);

/// <summary>一条 Steam 创意工坊评论。</summary>
/// <param name="Id">评论 id（页面上的 <c>comment_&lt;id&gt;</c>）。</param>
/// <param name="AuthorName">作者显示名。</param>
/// <param name="AuthorProfileUrl">作者主页。</param>
/// <param name="PostedUtc">发表时间。</param>
/// <param name="TextHtml">正文 HTML。</param>
/// <param name="TextPlain">正文纯文本（兜底/无障碍）。</param>
public sealed record SteamWorkshopComment(
    string Id,
    string AuthorName,
    string? AuthorProfileUrl,
    DateTimeOffset PostedUtc,
    string TextHtml,
    string TextPlain);

/// <summary>一页创意工坊评论（来自 <c>/comment/PublishedFile_Public/render/…</c>）。</summary>
/// <param name="Comments">这一页的评论。</param>
/// <param name="TotalCount">评论总数（接口返回的 total_count）。</param>
/// <param name="Start">本页起始偏移（0 基）。</param>
/// <param name="PageSize">本页最多条数。</param>
public sealed record SteamWorkshopCommentPage(
    IReadOnlyList<SteamWorkshopComment> Comments,
    int TotalCount,
    int Start,
    int PageSize);
