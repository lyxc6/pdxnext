namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>自动排序扫描所处的相位，用于进度文案。</summary>
public enum AutoSortPhase
{
    /// <summary>构建原版文件清单（只报已枚举的文件数，Total 为 0）。</summary>
    VanillaIndex,

    /// <summary>逐个 mod 统计足迹。</summary>
    ScanningMods,

    /// <summary>冲突建图与拓扑排序（短促相位，通常只报一次）。</summary>
    Graph,
}

/// <summary>
/// 自动排序的扫描进度快照。
/// </summary>
/// <param name="Phase">当前相位。</param>
/// <param name="Completed">本相位已完成的数量。</param>
/// <param name="Total">本相位的总量（原版索引相位未知时为 0）。</param>
/// <param name="CurrentModName">正在扫描的 mod 名（仅 <see cref="AutoSortPhase.ScanningMods"/> 有值）。</param>
public readonly record struct AutoSortProgress(
    AutoSortPhase Phase,
    int Completed,
    int Total,
    string? CurrentModName = null);
