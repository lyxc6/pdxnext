namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Patch;

/// <summary>「补丁到底用上没有」的判定结论。</summary>
public enum DlcOwnershipVerdictKind
{
    /// <summary>探测不可用（Steam 没开、缺 appid、32 位游戏…），只能说「授权未知」。</summary>
    Unknown,

    /// <summary>已打补丁，且 Steam 报告磁盘上的 DLC 全部已授权 —— 补丁生效。</summary>
    PatchedEffective,

    /// <summary>已打补丁，但 Steam 仍未授权其中一部分（通常意味 <c>cream_api.ini</c> 少了对应 id）。</summary>
    PatchedIncomplete,

    /// <summary>没打补丁，且有 DLC 未授权。</summary>
    VanillaPartial,

    /// <summary>没打补丁，但账号本来就全都有，看不出差别。</summary>
    VanillaAllOwned,
}

/// <summary>
/// 把「补丁状态 + Steam 授权 + 磁盘上的 DLC」折算成一句结论。
/// 纯函数，界面与自检共用。
/// </summary>
/// <param name="Kind">结论类别。</param>
/// <param name="DiskDlcCount">参与对账的磁盘 DLC 数（SteamId 为空的条目不参与）。</param>
/// <param name="OwnedDiskDlcCount">其中 Steam 报告已授权的数量。</param>
/// <param name="SteamDlcCount">Steam 总共列出多少个 DLC。</param>
/// <param name="SteamOwnedCount">Steam 报告已授权的总数。</param>
/// <param name="UnownedNames">未授权的 DLC 名称（界面提示用，已截断到前若干条）。</param>
public sealed record DlcOwnershipVerdict(
    DlcOwnershipVerdictKind Kind,
    int DiskDlcCount,
    int OwnedDiskDlcCount,
    int SteamDlcCount,
    int SteamOwnedCount,
    IReadOnlyList<string> UnownedNames)
{
    /// <summary>未授权的磁盘 DLC 数。</summary>
    public int UnownedDiskDlcCount => Math.Max(0, DiskDlcCount - OwnedDiskDlcCount);

    /// <summary>是否已判定出「补丁生效」。</summary>
    public bool IsEffective => Kind == DlcOwnershipVerdictKind.PatchedEffective;

    /// <summary>
    /// 计算结论。
    /// </summary>
    /// <param name="patchStatus">补丁状态。</param>
    /// <param name="report">授权探测结果；null 表示没探测。</param>
    /// <param name="diskDlcs">磁盘上的 DLC（只应传入带 SteamId 的条目）。</param>
    public static DlcOwnershipVerdict Evaluate(
        GamePatchStatus patchStatus,
        SteamOwnershipReport? report,
        IReadOnlyList<DlcOwnershipCandidate>? diskDlcs)
    {
        var candidates = diskDlcs ?? Array.Empty<DlcOwnershipCandidate>();
        if (report is null || !report.HasOwnership)
        {
            return new DlcOwnershipVerdict(
                DlcOwnershipVerdictKind.Unknown,
                0,
                0,
                report?.Dlcs.Count ?? 0,
                report is null ? 0 : CountOwned(report),
                Array.Empty<string>());
        }

        var unowned = new List<string>();
        var owned = 0;
        foreach (var candidate in candidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.SteamId))
            {
                continue;
            }

            if (report.OwnedAppIds.Contains(candidate.SteamId.Trim()))
            {
                owned++;
            }
            else
            {
                unowned.Add(candidate.DisplayName);
            }
        }

        var diskCount = owned + unowned.Count;
        if (diskCount == 0)
        {
            // 没有任何 .dlc 描述文件（例如 CS2）：只能拿 Steam 自己的清单说话。
            owned = CountOwned(report);
            diskCount = report.Dlcs.Count;
            foreach (var dlc in report.Dlcs)
            {
                if (!dlc.IsOwned)
                {
                    unowned.Add(dlc.Name);
                }
            }

            owned = diskCount - unowned.Count;
        }

        var unownedCount = diskCount - owned;
        var allOwned = unownedCount <= 0;
        var kind = patchStatus == GamePatchStatus.Patched
            ? (allOwned ? DlcOwnershipVerdictKind.PatchedEffective : DlcOwnershipVerdictKind.PatchedIncomplete)
            : (allOwned ? DlcOwnershipVerdictKind.VanillaAllOwned : DlcOwnershipVerdictKind.VanillaPartial);

        return new DlcOwnershipVerdict(
            kind,
            diskCount,
            owned,
            report.Dlcs.Count,
            CountOwned(report),
            Trim(unowned));
    }

    private static int CountOwned(SteamOwnershipReport report)
    {
        var count = 0;
        foreach (var dlc in report.Dlcs)
        {
            if (dlc.IsOwned)
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyList<string> Trim(List<string> names)
    {
        if (names.Count == 0)
        {
            return Array.Empty<string>();
        }

        var take = Math.Min(names.Count, 6);
        var result = new List<string>(take);
        for (var i = 0; i < take; i++)
        {
            result.Add(names[i]);
        }

        return result;
    }
}
