namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;
using System.Linq;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>差异行里一条「为什么动」的理由。</summary>
/// <param name="Kind">理由类型。</param>
/// <param name="RelatedModName">被跟随的 mod 名（补丁的基础 mod / 声明依赖的依赖方）；查不到时为 null。</param>
public sealed record AutoSortDiffReason(AutoSortDiffReasonKind Kind, string? RelatedModName);

/// <summary>差异行理由的类型。</summary>
public enum AutoSortDiffReasonKind
{
    /// <summary>与另一 mod 共带文件，作为其补丁/衍生排在后面。</summary>
    DerivedPatchAfter,

    /// <summary>与另一 mod 同层位且新增路径互有包含，作为被包含的一方排在后面。</summary>
    FootprintOverlapAfter,

    /// <summary>声明依赖另一 mod（mods_dependencies 或 descriptor dependencies），排在其后。</summary>
    DeclaredDependencyAfter,
}

/// <summary>一行差异：某 mod 从旧位到新位，以及它为什么在那里。</summary>
/// <param name="ModId">mod 标识。</param>
/// <param name="Name">显示名（取自应用前的行）。</param>
/// <param name="OldIndex">应用前的位次（0 起）。</param>
/// <param name="NewIndex">应用后的位次（0 起）。</param>
/// <param name="Moved">位次是否变化。</param>
/// <param name="Kind">足迹分类层位。</param>
/// <param name="IsDerivedPatch">名称是否带补丁/兼容标记。</param>
/// <param name="VanillaOverrideCount">覆盖原版玩法文件数。</param>
/// <param name="Reasons">先后关系理由（补丁边 / 依赖边）。</param>
public sealed record AutoSortDiffRow(
    Guid ModId,
    string Name,
    int OldIndex,
    int NewIndex,
    bool Moved,
    ModKind Kind,
    bool IsDerivedPatch,
    int VanillaOverrideCount,
    IReadOnlyList<AutoSortDiffReason> Reasons);

/// <summary>
/// 自动排序差异模型：把预览结果摊成对话框可直接排版的行（谁移到哪 + 理由），
/// 并原样携带警告与冲突。排序语义全部在 Core，这里只做映射，不含任何决策。
/// </summary>
public sealed record AutoSortDiff(
    IReadOnlyList<AutoSortDiffRow> Rows,
    IReadOnlyList<AutoSortWarning> Warnings,
    IReadOnlyList<AutoSortConflict> Conflicts,
    int MovedCount)
{
    /// <summary>
    /// 从预览产物构建差异。<paramref name="oldRows"/> 是应用前的行（顺序即旧位次），
    /// 用于取显示名与「被跟随 mod」的名字。
    /// </summary>
    public static AutoSortDiff Build(AutoSortPreview preview, IReadOnlyList<PlaySetMod> oldRows)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(oldRows);

        var nameByModId = new Dictionary<Guid, string>();
        foreach (var row in oldRows)
        {
            if (!nameByModId.ContainsKey(row.ModId))
            {
                nameByModId[row.ModId] = row.Name;
            }
        }

        // 理由挂在「后加载」的一端：谁要排在谁后面，就给谁一条指向对方的说明。
        var reasonsByModId = new Dictionary<Guid, List<AutoSortDiffReason>>();
        foreach (var edge in preview.Edges)
        {
            if (edge.Before == edge.After)
            {
                continue;
            }

            var kind = edge.Kind switch
            {
                SortEdgeKind.DerivedPatch => AutoSortDiffReasonKind.DerivedPatchAfter,
                SortEdgeKind.FootprintOverlap => AutoSortDiffReasonKind.FootprintOverlapAfter,
                _ => AutoSortDiffReasonKind.DeclaredDependencyAfter,
            };
            nameByModId.TryGetValue(edge.Before, out var relatedName);
            if (!reasonsByModId.TryGetValue(edge.After, out var reasons))
            {
                reasons = new List<AutoSortDiffReason>();
                reasonsByModId[edge.After] = reasons;
            }

            reasons.Add(new AutoSortDiffReason(kind, relatedName));
        }

        var rows = new List<AutoSortDiffRow>(preview.Result.Entries.Count);
        var movedCount = 0;
        foreach (var entry in preview.Result.Entries)
        {
            // 名字先按 ModId 取，位次只在 ModId 对得上时兜底：旧行顺序在预览后可能已变，按位置取名会把 A 的名字挂到 B 上。
            var name = nameByModId.TryGetValue(entry.ModId, out var known)
                ? known
                : entry.OldIndex >= 0 && entry.OldIndex < oldRows.Count && oldRows[entry.OldIndex].ModId == entry.ModId
                    ? oldRows[entry.OldIndex].Name
                    : entry.ModId.ToString("N");
            var moved = entry.OldIndex != entry.NewIndex;
            if (moved)
            {
                movedCount++;
            }

            rows.Add(new AutoSortDiffRow(
                entry.ModId,
                name,
                entry.OldIndex,
                entry.NewIndex,
                moved,
                entry.Kind,
                entry.IsDerivedPatch,
                entry.VanillaOverrideCount,
                reasonsByModId.TryGetValue(entry.ModId, out var mine)
                    ? mine
                    : Array.Empty<AutoSortDiffReason>()));
        }

        // 冲突按共带文件数降序：共享得越多越可能是真正的覆盖关系，先给人看。
        var conflicts = preview.Conflicts
            .OrderByDescending(static c => c.SharedPathCount)
            .ToList();

        return new AutoSortDiff(rows, preview.Result.Warnings, conflicts, movedCount);
    }
}
