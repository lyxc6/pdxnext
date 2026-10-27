namespace PdxLauncher.Core.Contracts;

/// <summary>
/// Mod 库（全部 mod）的一行：来自 mods 表的展示信息 + 该 mod 被哪些播放集引用。
/// <para>只读投影，不参与写入；播放集页仍然使用 <see cref="PlaySetMod"/>。</para>
/// </summary>
/// <param name="ModId">mods.id。</param>
/// <param name="Name">展示名（displayName → name → 目录名 → Unknown Mod）。</param>
/// <param name="GameRegistryId">游戏注册 ID，可能为空串。</param>
/// <param name="DirPath">mod 目录（archive 型 mod 为 null）。</param>
/// <param name="ArchivePath">zip 形式的 mod 包路径（目录型 mod 为 null）。</param>
/// <param name="ThumbnailPath">已验存在的缩略图路径，可能为 null。</param>
/// <param name="RequiredVersion">要求的游戏版本，可能为空串。</param>
/// <param name="Status">mods.status（例如 ready_to_play / missing）。</param>
/// <param name="Source">mods.source（steam / paradox / local）。</param>
/// <param name="SizeBytes">mods.size（字节），数据库没写时为 null。</param>
/// <param name="SubscribedOn">mods.subscribedDate（UTC），数据库没写时为 null。</param>
/// <param name="PlaySetIds">引用该 mod 的播放集 Id（按播放集创建顺序）。</param>
/// <param name="IsUnregistered">
/// 是否为磁盘扫描到、但 mods 表里还没有登记的本地 mod。
/// 这类行没有播放集归属，也无法写入 playsets_mods（外键指向 mods.id）。
/// </param>
/// <param name="SteamId">
/// mods.steamId：Steam 创意工坊物品 id，用来拼「在 Steam 中打开」/「在浏览器中打开」的地址。
/// 本地 mod 与部分 Paradox 渠道 mod 没有这个值，可能为 null。
/// </param>
/// <param name="ThumbnailUrl">
/// mods.thumbnailUrl：Steam 官方缩略图地址，用于本地找不到图时联网补全并缓存；可能为 null。
/// </param>
public sealed record ModLibraryEntry(
    Guid ModId,
    string Name,
    string GameRegistryId,
    string? DirPath,
    string? ArchivePath,
    string? ThumbnailPath,
    string RequiredVersion,
    string Status,
    string Source,
    long? SizeBytes,
    DateTimeOffset? SubscribedOn,
    IReadOnlyList<Guid> PlaySetIds,
    bool IsUnregistered = false,
    string? SteamId = null,
    string? ThumbnailUrl = null)
{
    /// <summary>该 mod 是否已经被至少一个播放集引用（对应官方启动器的「在 N 个播放集中」）。</summary>
    public bool IsInPlaySet => PlaySetIds.Count > 0;

    /// <summary>
    /// 是否为数据库里登记过的本地 mod（mods.source = local）。
    /// 这类 mod 仍在官方 mod 库内、要显示大小/订阅/播放集归属，也能加进播放集，
    /// 与 <see cref="IsUnregistered"/> 的「只有磁盘、库里没登记」是两回事。
    /// </summary>
    public bool IsLocalOnly => string.Equals(Source, "local", StringComparison.OrdinalIgnoreCase);
}
