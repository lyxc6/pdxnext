namespace PdxLauncher.Core.Contracts;

/// <summary>Mod 库页一次加载的完整快照：全部 mod + 全部播放集。</summary>
/// <param name="Mods">全部 mod（mods 表 + 磁盘本地 mod），已按名称排序。</param>
/// <param name="PlaySets">当前游戏的全部播放集（按创建时间升序）。</param>
public sealed record ModLibrarySnapshot(
    IReadOnlyList<ModLibraryEntry> Mods,
    IReadOnlyList<PlaySet> PlaySets)
{
    /// <summary>空快照（未选中游戏或数据库不可读时使用）。</summary>
    public static ModLibrarySnapshot Empty { get; } = new(
        Array.Empty<ModLibraryEntry>(),
        Array.Empty<PlaySet>());
}
