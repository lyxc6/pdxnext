namespace PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊条目页缓存的持久层：一行一个 publishedfileid，正文以 JSON 存 <c>payload</c> 列。
/// <para>
/// 与 <see cref="IWorkshopItemStore"/> 同样的约定：异常吞在内部，读不到就当没有。
/// </para>
/// </summary>
public interface IWorkshopPageStore
{
    /// <summary>库里现有的条目数。</summary>
    int Count { get; }

    /// <summary>按 publishedfileid 取一条；没有返回 <c>null</c>。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    CachedSteamItemPage? Find(string publishedFileId);

    /// <summary>写入一条解析结果（整行覆盖）。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="entry">解析结果。</param>
    void Set(string publishedFileId, CachedSteamItemPage entry);

    /// <summary>删除一条；不存在时静默。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    void Remove(string publishedFileId);

    /// <summary>清空全部条目。</summary>
    void Clear();

    /// <summary>条目超限时按抓取时间淘汰最旧的。</summary>
    /// <param name="maxEntries">保留上限。</param>
    void Trim(int maxEntries);
}
