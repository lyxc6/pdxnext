namespace PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊 mod 信息缓存里的一条：按「来源 + id」记录列表元数据与封面/详情抓取状态。
/// <para>
/// 只存原始值（评分是 double、时间是 UTC 时间戳），不存已本地化的文案 ——
/// 界面切语言时由行视图模型重新取当前语言的文案，缓存因此不会失效。
/// </para>
/// </summary>
public sealed class WorkshopInfoCacheEntry
{
    /// <summary>来源（<c>steam</c> / <c>pdx</c>）；用于按来源过滤。</summary>
    public string? Source { get; set; }

    /// <summary>来源侧 id：Steam 是 publishedfileid，PDX 是 modId。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>抓取时所属游戏的注册 ID（例如 <c>ck3</c>），断网回退时按它过滤。</summary>
    public string? GameId { get; set; }

    /// <summary>标题。</summary>
    public string? Name { get; set; }

    /// <summary>作者：Steam 是作者 SteamID64，PDX 是用户名。</summary>
    public string? Author { get; set; }

    /// <summary>评分（Steam 是 0–5 星，PDX 是 0–5 分）。</summary>
    public double? Rating { get; set; }

    /// <summary>评分人数。</summary>
    public int? RatingsTotal { get; set; }

    /// <summary>订阅数（Steam subscriptions / PDX installedCount）。</summary>
    public long? Subscriptions { get; set; }

    /// <summary>最近更新时间（UTC）。</summary>
    public DateTimeOffset? UpdatedUtc { get; set; }

    /// <summary>文件体积（字节）；未知为 null。</summary>
    public long? SizeBytes { get; set; }

    /// <summary>封面来源 URL；作者换图后 URL 会变，用来判断缓存是否过期。</summary>
    public string? CoverUrl { get; set; }

    /// <summary>已下载封面在缓存根下 <c>{source}/</c> 子目录里的文件名。</summary>
    public string? CoverFileName { get; set; }

    /// <summary>封面下载成功时间（UTC）。</summary>
    public DateTimeOffset? CoverFetchedUtc { get; set; }

    /// <summary>详情正文（Steam description / PDX 长描述）。</summary>
    public string? DetailText { get; set; }

    /// <summary>详情里的截图地址（PDX 有，Steam 通常为空）。</summary>
    public List<string>? DetailScreenshots { get; set; }

    /// <summary>详情抓取时间（UTC）；与 <see cref="FetchedUtc"/> 分开，详情 TTL 比列表长。</summary>
    public DateTimeOffset? DetailFetchedUtc { get; set; }

    /// <summary>列表元数据的抓取时间（UTC），用作 TTL 判定。</summary>
    public DateTimeOffset? FetchedUtc { get; set; }

    /// <summary>
    /// 详情里的作者 SteamID64（Steam 详情才有）。评论翻页要用它当 owner id。
    /// <para>
    /// 不复用 <see cref="Author"/>：那个字段是给列表副标题用的，PDX 行放的是用户名，语义不纯。
    /// </para>
    /// </summary>
    public string? CreatorSteamId { get; set; }

    /// <summary>详情里的标签（Steam 详情才有）。</summary>
    public List<string>? Tags { get; set; }

    /// <summary>详情里的浏览数（Steam 详情才有）。</summary>
    public long? Views { get; set; }

    /// <summary>详情里的累计订阅数 lifetime_subscriptions（Steam 详情才有）。</summary>
    public long? LifetimeSubscriptions { get; set; }
}
