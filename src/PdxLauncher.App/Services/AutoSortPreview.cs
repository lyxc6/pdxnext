namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 自动排序的预览产物：足迹、硬边（声明依赖+派生补丁）、冲突列表、排序结果与重排后的行。
/// 应用前由调用方确认；撤销用调用方保留的旧顺序快照。
/// <para>
/// <see cref="Footprints"/> 只含启用的 mod（禁用的行不参与扫描，保持原位）。
/// </para>
/// <para>
/// 预览还带着生成时的播放集与输入签名（见 <see cref="PlaySetAutoSortService.ComputeInputSignature"/>）：
/// 预览到确认之间列表若被刷新，应用方必须拒绝而不是整表覆盖。
/// </para>
/// </summary>
public sealed record AutoSortPreview(
    IReadOnlyList<ModFootprint> Footprints,
    IReadOnlyList<SortEdge> Edges,
    IReadOnlyList<AutoSortConflict> Conflicts,
    AutoSortResult Result,
    IReadOnlyList<PlaySetMod> ReorderedRows,
    Guid? PlaySetId = null,
    string? InputSignature = null)
{
    /// <summary>当前列表是否已经偏离生成预览时的输入（无签名的旧预览一律视为过期）。</summary>
    public bool IsStaleFor(IReadOnlyList<PlaySetMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        return PlaySetId is null
            || InputSignature is null
            || InputSignature != PlaySetAutoSortService.ComputeInputSignature(mods);
    }
}
