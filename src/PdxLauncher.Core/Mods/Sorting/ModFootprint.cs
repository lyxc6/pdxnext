namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 一个 mod 的文件改动足迹：把「这个 mod 实际改了哪些文件」摊平成排序需要的计数与路径集合。
/// </summary>
/// <param name="ModId">mods.id。</param>
/// <param name="Name">展示名（判定补丁/翻译关系用）。</param>
/// <param name="TotalFiles">内容文件总数（已剔除 .git、README 等非内容文件）。</param>
/// <param name="GameplayOverrides">玩法目录（common/events/history/gui/map_data/data_binding）里覆盖原版的文件数。</param>
/// <param name="LocFiles">localization/ 下的文件数。</param>
/// <param name="GfxFiles">gfx/ 下的文件数。</param>
/// <param name="ReplaceFiles">来自 replace/ 目录（整文件强覆盖）的文件数。</param>
/// <param name="OverriddenPaths">覆盖原版的相对路径集合（'/' 分隔、已折叠 replace/ 段）。</param>
/// <param name="AddedPaths">原版没有的新增相对路径集合（'/' 分隔、已折叠 replace/ 段）。</param>
/// <remarks>
/// 路径集合是冲突图的输入：同路径被多个 mod 提供时才有顺序约束，
/// 所以扫描只列路径、不算内容哈希——数百 mod、约十万条路径，秒级可完成。
/// </remarks>
public sealed record ModFootprint(
    Guid ModId,
    string Name,
    int TotalFiles,
    int GameplayOverrides,
    int LocFiles,
    int GfxFiles,
    int ReplaceFiles,
    IReadOnlySet<string> OverriddenPaths,
    IReadOnlySet<string> AddedPaths);
