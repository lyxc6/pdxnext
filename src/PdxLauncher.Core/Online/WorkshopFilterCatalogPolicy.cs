namespace PdxLauncher.Core.Online;

/// <summary>
/// 筛选目录的更新策略：Steam 没有推送，只能按 TTL 和本地游戏版本判断「要不要再抓一次」。
/// <para>
/// 默认 24 小时：正常翻页/改筛选本来就会带着 SSR 回来，只有页面命中内存缓存
/// （或从详情页返回、页面一直没动）时才需要额外的目录请求。
/// 本地版本变化（例如 1.13 → 1.14）会绕过 TTL 提前刷新，避免新版本标签要等一天。
/// </para>
/// </summary>
public static class WorkshopFilterCatalogPolicy
{
    /// <summary>目录新鲜期：超过后下次进入工坊页/命中页缓存时补一次。</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    /// <summary>目录刷新失败后的退避时间（只记在内存里，重启即忘）。</summary>
    public static readonly TimeSpan FailureBackoff = TimeSpan.FromMinutes(30);

    /// <summary>是否需要刷新：没有快照、本地版本变了、或超过 TTL。</summary>
    /// <param name="snapshot">已持久化的快照；null 表示从没抓到过。</param>
    /// <param name="currentGameVersion">当前本地游戏版本；空串表示读不到（例如功能探测安装）。</param>
    /// <param name="nowUtc">当前 UTC 时刻。</param>
    /// <param name="ttl">新鲜期；默认 <see cref="DefaultTtl"/>。</param>
    public static bool NeedsRefresh(
        WorkshopFilterCatalogSnapshot? snapshot,
        string? currentGameVersion,
        DateTimeOffset nowUtc,
        TimeSpan? ttl = null)
    {
        if (snapshot is null || snapshot.Groups.Count == 0 || snapshot.FetchedAtUtc == default)
        {
            return true;
        }

        var current = currentGameVersion?.Trim() ?? string.Empty;
        var stored = snapshot.GameVersion?.Trim() ?? string.Empty;
        if (current.Length > 0 && !string.Equals(current, stored, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var age = nowUtc - snapshot.FetchedAtUtc;
        return age < TimeSpan.Zero || age >= (ttl ?? DefaultTtl);
    }

    /// <summary>
    /// 分组签名：键、标题、标签值都参与。目录没变就不用重建侧栏、也不用重复写盘。
    /// </summary>
    /// <param name="groups">分组。</param>
    public static string Signature(IReadOnlyList<SteamWorkshopFilterGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        if (groups.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            "||",
            groups.Select(static group =>
                group.Key + "|" + group.Title + "|"
                + string.Join(",", group.Tags.Select(static tag => tag.Tag))));
    }
}
