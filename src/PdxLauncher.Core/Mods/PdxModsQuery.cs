namespace PdxLauncher.Core.Mods;

/// <summary>
/// PDX Mods 平台的排序方式。取值必须与服务端白名单完全一致：
/// 传其它值会得到 <c>400 bad-input</c>（<c>Search must be one of: activationOrder, created,
/// displayName, popularity, rating, updated, best</c>，本机实测）。
/// <para>
/// <see cref="ActivationOrder"/> 只在 <c>onlyInstalled</c> 场景下可用，普通搜索传它会 400
/// （<c>Can not sort by 'activationOrder' if 'onlyInstalled' is not specified</c>），因此不提供给界面。
/// </para>
/// </summary>
public enum PdxModsSort
{
    /// <summary>官方默认排序（服务端相关度 + 时间衰减）。</summary>
    Best,

    /// <summary>人气（订阅数）。</summary>
    Popularity,

    /// <summary>评分。</summary>
    Rating,

    /// <summary>最近更新。</summary>
    Updated,

    /// <summary>最新发布。</summary>
    Created,

    /// <summary>名称。</summary>
    DisplayName,
}

/// <summary>一次 PDX Mods 搜索的入参。</summary>
/// <param name="SearchQuery">关键词；null/空表示不筛关键词（只按排序浏览）。</param>
/// <param name="Sort">排序方式。</param>
/// <param name="Descending">是否降序。</param>
/// <param name="Page">页号，从 0 开始。</param>
/// <param name="PageSize">每页条数（服务端实测 200 仍可用；界面用 24/48）。</param>
public sealed record PdxModsQuery(
    string? SearchQuery,
    PdxModsSort Sort = PdxModsSort.Best,
    bool Descending = true,
    int Page = 0,
    int PageSize = 24)
{
    /// <summary>排序枚举 → 服务端接受的字符串。</summary>
    public static string SortToString(PdxModsSort sort) => sort switch
    {
        PdxModsSort.Popularity => "popularity",
        PdxModsSort.Rating => "rating",
        PdxModsSort.Updated => "updated",
        PdxModsSort.Created => "created",
        PdxModsSort.DisplayName => "displayName",
        _ => "best",
    };
}
