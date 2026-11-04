namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>硬边类型：来自声明式依赖，或从文件足迹/名称推导的补丁关系。</summary>
public enum SortEdgeKind
{
    /// <summary>声明式依赖（descriptor dependencies / launcher-v2.sqlite mods_dependencies）。</summary>
    DeclaredDependency,

    /// <summary>衍生补丁：补丁/翻译变体必须排在其基底 mod 之后加载。</summary>
    DerivedPatch,

    /// <summary>
    /// 同层位内的新增路径包含关系：一方的全部新增文件是另一方的子集，
    /// 视前者为基底、后者为细化版本，基底先加载。跨层位一律不建这种边（层位裁决优先）。
    /// </summary>
    FootprintOverlap,
}

/// <summary>
/// 一条硬排序边：<paramref name="Before"/> 必须先于 <paramref name="After"/> 加载。
/// </summary>
/// <param name="Before">先加载的 mod。</param>
/// <param name="After">后加载的 mod（补丁/依赖方，冲突时获胜）。</param>
/// <param name="Kind">边的来源。</param>
/// <param name="Detail">可读的来源说明（如共享文件名、依赖声明），预览里解释「为什么这么排」。</param>
public sealed record SortEdge(Guid Before, Guid After, SortEdgeKind Kind, string? Detail);
