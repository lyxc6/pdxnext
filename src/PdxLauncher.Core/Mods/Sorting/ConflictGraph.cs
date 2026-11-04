namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>冲突告警类别：无法自动决定先后、需要用户人工确认的重叠。</summary>
public enum AutoSortConflictKind
{
    /// <summary>两个同层位 mod 覆盖了同一个原版文件，谁赢都合理，按稳定顺序保持现状。</summary>
    VanillaOverrideSameTier,

    /// <summary>
    /// 两个 mod 共带同一新增文件，但层位不同：先后由层位决定（小改动后加载、覆盖该文件），
    /// 不用文件体量去反向裁决。只提示，不建边。
    /// </summary>
    SharedAddedFileTierDecided,

    /// <summary>两个同层位 mod 共带同一新增文件，也判不出基底：无顺序证据，保持现状。</summary>
    SharedAddedFileAmbiguous,
}

/// <summary>一对 mod 之间的重叠告警（只告警、不产生排序边）。</summary>
/// <param name="ModIdA">mod A。</param>
/// <param name="ModIdB">mod B。</param>
/// <param name="Kind">冲突类别。</param>
/// <param name="SharedPathCount">共享路径条数。</param>
/// <param name="SamplePaths">至多 3 条样本路径，预览里展示。</param>
public sealed record AutoSortConflict(
    Guid ModIdA,
    Guid ModIdB,
    AutoSortConflictKind Kind,
    int SharedPathCount,
    IReadOnlyList<string> SamplePaths);

/// <summary>冲突图构建结果：衍生补丁硬边 + 无法判定的告警清单。</summary>
/// <param name="DerivedPatchEdges">从文件足迹/名称推导的补丁关系边。</param>
/// <param name="Conflicts">无法自动判定的重叠告警。</param>
public sealed record ConflictGraph(
    IReadOnlyList<SortEdge> DerivedPatchEdges,
    IReadOnlyList<AutoSortConflict> Conflicts);
