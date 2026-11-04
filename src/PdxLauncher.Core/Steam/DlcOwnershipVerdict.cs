namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Patch;

/// <summary>「补丁到底用上没有」的判定结论。</summary>
public enum DlcOwnershipVerdictKind
{
    /// <summary>探测不可用（Steam 没开、缺 appid、32 位游戏…），只能说「授权未知」。</summary>
    Unknown,

    /// <summary>已打补丁（含他人补丁），且探测结果里磁盘上的 DLC 全部已授权 —— 补丁生效。</summary>
    PatchedEffective,

    /// <summary>已打补丁（含他人补丁），但探测结果仍未授权其中一部分（通常是 <c>cream_api.ini</c> 少了对应 id）。</summary>
    PatchedIncomplete,

    /// <summary>没打补丁，且有 DLC 未授权。</summary>
    VanillaPartial,

    /// <summary>没打补丁，但账号本来就全都有，看不出差别。</summary>
    VanillaAllOwned,
}

/// <summary>
/// 把「补丁状态 + 授权探测 + 磁盘上的 DLC」折算成一句结论。
/// 纯函数，界面与自检共用。
/// </summary>
/// <param name="Kind">结论类别。</param>
/// <param name="DiskDlcCount">参与对账的磁盘 DLC 数（SteamId 为空的条目不参与）。</param>
/// <param name="OwnedDiskDlcCount">其中已授权的数量。</param>
/// <param name="ProbeDlcCount">探测到的 DLC 总数（loader 在岗时是加载器的表，不一定等于 Steam 目录）。</param>
/// <param name="ProbeOwnedCount">探测结果里已授权的总数。</param>
/// <param name="UnownedNames">未授权的 DLC 名称（界面提示用，已截断到前若干条）。</param>
/// <param name="IsLoaderActive">探测时补丁 loader 是否在岗（此时 <paramref name="ProbeDlcCount"/> 不是 Steam 数字）。</param>
/// <param name="AccountDlcCount">用原版备份探到的账号 DLC 数；null 表示无法判定。</param>
/// <param name="AccountOwnedCount">账号视图里已授权的总数。</param>
/// <param name="AccountOwnedDiskDlcCount">磁盘 DLC 中账号确实拥有的数量。</param>
/// <param name="AccountErrorKey">账号视图失败原因（<see cref="SteamProbeError"/> 常量）。</param>
/// <param name="ForeignDlcCount">探测表里不属于本游戏 Steam 目录的条数（仅账号视图可信时统计）。</param>
public sealed record DlcOwnershipVerdict(
    DlcOwnershipVerdictKind Kind,
    int DiskDlcCount,
    int OwnedDiskDlcCount,
    int ProbeDlcCount,
    int ProbeOwnedCount,
    IReadOnlyList<string> UnownedNames,
    bool IsLoaderActive = false,
    int? AccountDlcCount = null,
    int? AccountOwnedCount = null,
    int? AccountOwnedDiskDlcCount = null,
    string? AccountErrorKey = null,
    int ForeignDlcCount = 0)
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
        // PatchedOther 也是「loader 在岗」——只是不是本工具打的，不能因此说成「未打补丁」。
        var loaderActive = patchStatus is GamePatchStatus.Patched or GamePatchStatus.PatchedOther;
        if (report is null || !report.HasOwnership)
        {
            return new DlcOwnershipVerdict(
                DlcOwnershipVerdictKind.Unknown,
                0,
                0,
                report?.Dlcs.Count ?? 0,
                report is null ? 0 : CountOwned(report.Dlcs),
                Array.Empty<string>(),
                loaderActive,
                null,
                null,
                null,
                AccountError(report),
                0);
        }

        var accountView = report.Account is { HasOwnership: true } account ? account : null;
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
        int? accountOwnedDisk = null;
        if (accountView is not null && candidates.Count > 0)
        {
            accountOwnedDisk = CountOwnedCandidates(accountView, candidates);
        }

        if (diskCount == 0)
        {
            // 没有任何 .dlc 描述文件（例如 CS2）：只能拿探测清单对账。
            // loader 在岗时主清单是 cream_api.ini 的大表（490 条），拿它说「全都有」是假象，
            // 所以优先用原版备份探到的账号清单。
            var source = accountView is not null ? accountView.Dlcs : report.Dlcs;
            var sourceOwned = accountView is not null ? accountView.OwnedAppIds : report.OwnedAppIds;
            diskCount = source.Count;
            foreach (var dlc in source)
            {
                if (!sourceOwned.Contains(dlc.AppId))
                {
                    unowned.Add(dlc.Name);
                }
            }

            owned = diskCount - unowned.Count;
        }

        var unownedCount = diskCount - owned;
        var allOwned = unownedCount <= 0;
        var kind = loaderActive
            ? (allOwned ? DlcOwnershipVerdictKind.PatchedEffective : DlcOwnershipVerdictKind.PatchedIncomplete)
            : (allOwned ? DlcOwnershipVerdictKind.VanillaAllOwned : DlcOwnershipVerdictKind.VanillaPartial);

        return new DlcOwnershipVerdict(
            kind,
            diskCount,
            owned,
            report.Dlcs.Count,
            CountOwned(report.Dlcs),
            Trim(unowned),
            loaderActive,
            accountView is null ? null : accountView.Dlcs.Count,
            accountView is null ? null : CountOwned(accountView.Dlcs),
            accountOwnedDisk,
            accountView is not null ? null : AccountError(report),
            accountView is null ? 0 : Math.Max(0, report.Dlcs.Count - accountView.Dlcs.Count));
    }

    private static int CountOwnedCandidates(DlcAccountOwnership account, IReadOnlyList<DlcOwnershipCandidate> candidates)
    {
        var count = 0;
        foreach (var candidate in candidates)
        {
            if (candidate is null || string.IsNullOrWhiteSpace(candidate.SteamId))
            {
                continue;
            }

            if (account.OwnedAppIds.Contains(candidate.SteamId.Trim()))
            {
                count++;
            }
        }

        return count;
    }

    private static int CountOwned(IReadOnlyList<SteamDlcOwnership> dlcs)
    {
        var count = 0;
        foreach (var dlc in dlcs)
        {
            if (dlc.IsOwned)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>主报告失败时带原因；成功但账号视图不可用时也带上账号视图的原因。</summary>
    private static string? AccountError(SteamOwnershipReport? report)
    {
        if (report is null)
        {
            return null;
        }

        if (!report.HasOwnership)
        {
            return report.ErrorKey;
        }

        return report.Account is { HasOwnership: false } account ? account.ErrorKey : null;
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
