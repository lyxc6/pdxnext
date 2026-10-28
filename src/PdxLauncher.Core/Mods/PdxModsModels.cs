namespace PdxLauncher.Core.Mods;

/// <summary>
/// PDX Mods 平台上的一个 mod（列表项）。字段与 <c>POST /mods</c> 返回的条目一一对应，
/// 名字取自服务端原字段（本机实测 2026-10 的真实响应），缺失一律留空而不抛。
/// </summary>
/// <param name="ModId">平台数字 id（CS2 播放集里 <c>sourceId</c> 用的就是它），如 "163638"。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="Author">作者名。</param>
/// <param name="ShortDescription">一句话简介。</param>
/// <param name="GameVersion">mod 自己声明的目标游戏版本，如 "1.6.*"。</param>
/// <param name="UserModVersion">作者自定的版本号，如 "0.3.6"（与 <paramref name="Version"/> 不同）。</param>
/// <param name="Version">平台分配的整数版本（播放集 <c>sourceVersion</c> 用它）。</param>
/// <param name="Rating">评分（0–5）。</param>
/// <param name="RatingsTotal">评分人数。</param>
/// <param name="Subscriptions">订阅数。</param>
/// <param name="UpdatedOn">最近更新时间（服务端为 UTC）。</param>
/// <param name="CreatedOn">首次发布时间；列表接口不返回，只有详情有。</param>
/// <param name="CoverUrl">封面图 URL（<c>modscontent.paradox-interactive.com</c>）。</param>
/// <param name="Tags">平台标签（如 "Code Mod"）。</param>
public sealed record PdxModsSummary(
    string ModId,
    string DisplayName,
    string Author,
    string ShortDescription,
    string GameVersion,
    string UserModVersion,
    string Version,
    double Rating,
    int RatingsTotal,
    long Subscriptions,
    DateTimeOffset? UpdatedOn,
    DateTimeOffset? CreatedOn,
    string? CoverUrl,
    IReadOnlyList<string> Tags);

/// <summary>
/// PDX Mods 平台上的一个 mod 的详情（<c>GET /mods?modId=…&amp;os=windows&amp;arch=x64</c> 的 <c>modDetail</c>）。
/// <para>
/// 注意：该端点<b>必须带 os/arch</b>，否则服务端回
/// <c>400 bad-input "Mod is corrupted or chosen operating system is not supported"</c>（本机实测）。
/// 响应顶层是 <c>{result, modDetail}</c>，不是数组。
/// </para>
/// </summary>
/// <param name="Summary">列表接口能提供的公共字段。</param>
/// <param name="LongDescription">Markdown 长描述。</param>
/// <param name="Changelog">更新日志（Markdown，可能为空串）。</param>
/// <param name="ScreenshotUrls">预览图 URL 列表。</param>
/// <param name="ExternalLinks">外部链接（YouTube / GitHub 等）。</param>
/// <param name="ForumUrl">官方论坛链接。</param>
/// <param name="SizeBytes">服务端登记的安装体积（详情里是字符串 "36977023"，解析失败为 null）。</param>
/// <param name="IsInstalled">当前账号是否已订阅（未登录时恒为 false）。</param>
/// <param name="ScreenshotThumbnailUrls">
/// 与 <paramref name="ScreenshotUrls"/> 一一对应的缩略图 URL；服务端没给缩略图的位置为空串。
/// 缩略图常用于详情的图片墙先显示低清版。
/// </param>
/// <param name="ExternalLinkTypes">
/// 与 <paramref name="ExternalLinks"/> 一一对应的类型标签（原样保留，空串表示缺失）。
/// 服务端的 <c>displayName</c> 经常为 null，界面要给链接起名时需要这个类型。
/// </param>
/// <param name="Requirements">
/// 前置依赖（服务端 <c>requirements[]</c>）。<c>productType</c> 为 <c>MOD</c> 时是另一个 mod，
/// 其它值（如 DLC）原样保留给界面显示。空列表表示无依赖。
/// </param>
public sealed record PdxModsDetail(
    PdxModsSummary Summary,
    string LongDescription,
    string Changelog,
    IReadOnlyList<string> ScreenshotUrls,
    IReadOnlyList<PdxModsLink> ExternalLinks,
    string? ForumUrl,
    long? SizeBytes,
    bool IsInstalled,
    IReadOnlyList<string> ScreenshotThumbnailUrls,
    IReadOnlyList<string> ExternalLinkTypes,
    IReadOnlyList<PdxModRequirement> Requirements);

/// <summary>mod 详情里的一条前置依赖（<c>modDetail.requirements[]</c>）。</summary>
/// <param name="ProductType">产物类型：<c>MOD</c> / DLC 等。</param>
/// <param name="Id">依赖目标的 id（MOD 时就是 modId）。</param>
/// <param name="Name">依赖目标显示名。</param>
/// <param name="Version">服务端记录的版本号（可能为 null）。</param>
/// <param name="DependencyVersion">要求依赖方的版本（可能为 null）。</param>
public sealed record PdxModRequirement(
    string ProductType,
    string Id,
    string Name,
    int? Version,
    string? DependencyVersion)
{
    /// <summary>是不是另一个 mod（而不是 DLC/工具）。</summary>
    public bool IsMod => string.Equals(ProductType, "MOD", StringComparison.OrdinalIgnoreCase);
}

/// <summary>mod 详情里的一个外部链接。</summary>
/// <param name="Url">绝对地址。</param>
/// <param name="Type">服务端给的类型标签（youtube / github / …），可能为空。</param>
/// <param name="DisplayName">显示名，可能为空。</param>
public sealed record PdxModsLink(string Url, string Type, string DisplayName);

/// <summary>一页搜索结果。</summary>
/// <param name="TotalCount">服务端报告的总条数（CS2 约 2.7 万，与分页无关）。</param>
/// <param name="Page">本页页号（回显入参）。</param>
/// <param name="PageSize">本页请求的页大小。</param>
/// <param name="Items">本页条目。</param>
public sealed record PdxModsPage(
    int TotalCount,
    int Page,
    int PageSize,
    IReadOnlyList<PdxModsSummary> Items)
{
    /// <summary>空页（失败或未搜索时用）。</summary>
    public static PdxModsPage Empty { get; } = new(0, 0, 0, Array.Empty<PdxModsSummary>());

    /// <summary>是否还有下一页。</summary>
    public bool HasMore => (Page + 1) * PageSize < TotalCount;
}

/// <summary>
/// 账号在 PDX 侧的播放集（API 里叫 activation set）。
/// <para>
/// 订阅登记在播放集上（<c>activationSetId</c>），所以「某个 mod 是否已订阅」要先确定用哪个播放集。
/// </para>
/// </summary>
/// <param name="Id">播放集 id（订阅接口的 <c>activationSetId</c>）。</param>
/// <param name="Name">显示名。</param>
/// <param name="GameName">所属游戏（平台 id，形如 <c>cities_skylines_2</c>）。</param>
/// <param name="ModCount">其中 mod 数（服务端有时不给，为 null）。</param>
public sealed record PdxPlaySet(
    long Id,
    string Name,
    string? GameName,
    int? ModCount);

/// <summary>播放集里的一条 mod（用于判断「已订阅/已启用」）。</summary>
/// <param name="ModId">平台 mod id（字符串形式，便于与搜索/详情对齐）。</param>
/// <param name="Name">显示名。</param>
/// <param name="Version">登记的版本号。</param>
/// <param name="Enabled">在播放集里是否启用。</param>
public sealed record PdxPlaySetMod(
    string ModId,
    string? Name,
    int? Version,
    bool Enabled);
