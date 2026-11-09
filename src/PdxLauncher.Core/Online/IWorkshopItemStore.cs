namespace PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊 mod 信息缓存的持久层：一行一个「来源 + id」，读写都不再整份序列化。
/// <para>
/// 这一层只认数据，不碰文件系统；封面文件是否存在由 <see cref="WorkshopInfoCache"/> 判断。
/// 实现必须自己吞掉数据库异常（记日志、按空结果处理），绝不把异常抛给界面。
/// </para>
/// </summary>
public interface IWorkshopItemStore
{
    /// <summary>库里现有的条目数。</summary>
    int Count { get; }

    /// <summary>按缓存键（<c>steam:123</c> / <c>pdx:456</c>）取一条；没有返回 <c>null</c>。</summary>
    /// <param name="key">缓存键。</param>
    WorkshopInfoCacheEntry? Find(string key);

    /// <summary>写入或合并一条列表元数据；字段为 null 时保留旧值。</summary>
    /// <param name="entry">要写入的条目。</param>
    void Upsert(WorkshopInfoCacheEntry entry);

    /// <summary>
    /// 批量写入/合并一组列表元数据：同一个事务、只做一次容量淘汰。
    /// <para>
    /// 列表页一次要并进 30–50 条；逐条 <see cref="Upsert"/> 每条都要开连接、<c>BEGIN IMMEDIATE</c>、
    /// 提交并执行一次 <c>COUNT(*)</c>，慢盘上是秒级开销。
    /// </para>
    /// </summary>
    /// <param name="entries">要写入的条目；空列表直接返回。</param>
    void UpsertMany(IReadOnlyList<WorkshopInfoCacheEntry> entries);

    /// <summary>记录封面下载结果；条目不存在时新建一行。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="fileName">封面相对缓存根的文件名。</param>
    /// <param name="url">封面来源地址；空白表示保留旧值。</param>
    /// <param name="fetchedUtc">下载时间。</param>
    void SetCover(string key, string fileName, string? url, DateTimeOffset fetchedUtc);

    /// <summary>记录详情抓取结果；条目不存在时新建一行。</summary>
    /// <param name="key">缓存键。</param>
    /// <param name="text">详情正文；null 表示这次没拿到。</param>
    /// <param name="screenshots">详情截图地址；空表示清空。</param>
    /// <param name="creatorSteamId">作者 SteamID64；空白表示保留旧值。</param>
    /// <param name="tags">标签；空表示保留旧值。</param>
    /// <param name="views">浏览数；0 表示保留旧值。</param>
    /// <param name="lifetimeSubscriptions">累计订阅数；0 表示保留旧值。</param>
    /// <param name="fetchedUtc">抓取时间。</param>
    void SetDetail(
        string key,
        string? text,
        IReadOnlyList<string>? screenshots,
        string? creatorSteamId,
        IReadOnlyList<string>? tags,
        long views,
        long lifetimeSubscriptions,
        DateTimeOffset fetchedUtc);

    /// <summary>按来源与游戏列出条目（断网回退用），按最近更新时间倒序。</summary>
    /// <param name="source">来源。</param>
    /// <param name="gameId">游戏注册 ID；空白表示不按游戏过滤。</param>
    /// <param name="limit">最多返回条数。</param>
    IReadOnlyList<WorkshopInfoCacheEntry> List(WorkshopSource source, string? gameId, int limit);

    /// <summary>清空全部条目。</summary>
    void Clear();

    /// <summary>条目超限时按抓取时间淘汰最旧的。</summary>
    /// <param name="maxEntries">保留上限。</param>
    void Trim(int maxEntries);
}
