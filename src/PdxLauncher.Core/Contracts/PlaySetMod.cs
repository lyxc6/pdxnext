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
/// <param name="ModVersion">
/// mod 作者自己写的版本（.mod 的 <c>version</c>、metadata.json 的 <c>version</c>、
/// 官方库的 <c>metadataVersion</c>），与 <paramref name="RequiredVersion"/>（支持的游戏版本）不是一回事；
/// 作者没写时为空串。
/// </param>
/// <param name="PdxId">
/// PDX Mods 平台的数字 id（官方库 <c>mods.pdxId</c>、CS2 的 <c>sourceId</c>）。
/// 官方「分享播放集」JSON 用 <c>pdxId</c> 标识这类 mod；Steam 系 mod 为 null。
/// </param>
/// <param name="PdxVersion">
/// PDX Mods 被<b>锁定</b>版本时的版本号（官方库 <c>keepLatest = 0</c> 时的 <c>mods.version</c>）；
/// 跟随最新版（默认）或 Steam mod 为 null。分享 JSON 的 <c>version</c> 字段只在这个条件下写。
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
    string? Source = null,
    string? ModVersion = null,
    string? PdxId = null,
    string? PdxVersion = null);
