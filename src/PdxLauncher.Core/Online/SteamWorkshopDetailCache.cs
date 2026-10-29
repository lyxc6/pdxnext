namespace PdxLauncher.Core.Online;

/// <summary>
/// 把工坊信息缓存里的条目还原成 <see cref="SteamWorkshopItemDetail"/>。
/// <para>
/// 单独抽出来是有原因的：这段「缓存 → 详情」的映射以前写在取数层里，
/// 漏掉了 <c>CreatorSteamId</c> 等字段，导致命中缓存后评论翻页与作者链接一起失效。
/// 放在 Core 里纯函数实现，检查工程可以直接断言。
/// </para>
/// </summary>
public static class SteamWorkshopDetailCache
{
    /// <summary>还原详情。</summary>
    /// <param name="entry">缓存条目。</param>
    /// <param name="id">工坊条目 id（缓存条目本身不带来源前缀）。</param>
    /// <param name="fallbackName">缓存里没有标题时的兜底标题。</param>
    public static SteamWorkshopItemDetail Restore(WorkshopInfoCacheEntry entry, string id, string fallbackName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var now = DateTimeOffset.UtcNow;

        // 作者优先取详情时存下的 CreatorSteamId；旧缓存文件没有这个键，
        // 退回列表元数据的 Author —— Steam 行的 Author 存的正是作者 SteamID64。
        var creator = !string.IsNullOrWhiteSpace(entry.CreatorSteamId)
            ? entry.CreatorSteamId
            : entry.Author;

        return new SteamWorkshopItemDetail(
            id,
            string.IsNullOrWhiteSpace(entry.Name) ? fallbackName : entry.Name!,
            entry.DetailText ?? string.Empty,
            entry.CoverUrl,
            entry.SizeBytes ?? 0,
            entry.FetchedUtc ?? now,
            entry.UpdatedUtc ?? now,
            null,
            string.IsNullOrWhiteSpace(creator) ? null : creator,
            entry.Tags ?? [],
            entry.Views ?? 0,
            entry.LifetimeSubscriptions ?? 0);
    }
}
