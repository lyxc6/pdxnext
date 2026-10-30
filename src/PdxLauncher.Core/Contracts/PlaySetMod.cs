namespace PdxLauncher.Core.Contracts;

/// <summary>playsets_mods 与 mods 表连接后的一行（含排序位置与启用状态）。</summary>
/// <param name="SteamId">
/// mods.steamId（Steam 创意工坊物品 id）；目录扫描与旧版数据库都可能拿不到，为 null。
/// 不落库：写播放集时只存 playsetId / modId / enabled / position。
/// </param>
/// <param name="Source">
/// 来源（mods.source：local / steam / paradox / pdx_mods），磁盘扫描时按「有没有工坊 id」给。
/// 展示层用 <see cref="PdxLauncher.Core.Playsets.ModOrigins.Normalize"/> 归一成「本地 / 订阅」。
/// </param>
public sealed record PlaySetMod(
    Guid ModId,
    string Name,
    string DirPath,
    string? ArchivePath,
    string GameRegistryId,
    string? ThumbnailPath,
    string RequiredVersion,
    string Status,
    bool Enabled,
    int Position,
    string? SteamId = null,
    string? Source = null);
