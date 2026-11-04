namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Steam;

/// <summary>
/// CS2（<c>PlaysetConfigV1</c>）的 DLC 清单自检：Steam 报告 → <see cref="DlcInfo"/> 的翻译规则，
/// 以及这种「没有磁盘描述文件」的清单在授权对账里仍然算得出来。
/// 纯内存，不加载真实 steam_api64.dll、不联网、不碰用户目录。
/// </summary>
public static class SteamDlcListChecks
{
    private const string Cs2AppId = "949230";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckMapping();
        CheckDeduplication();
        CheckIdsAreWritableOnlyByKey();
        CheckNullInput();
        CheckBuildDlcIdValidation();
        CheckVerdictWithoutDiskManifest();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------ 映射

    private static void CheckMapping()
    {
        var list = SteamDlcListBuilder.FromOwnership(
        [
            new SteamDlcOwnership("1094000", "Cities: Skylines II - Beach Properties", true, true),
            new SteamDlcOwnership("1094001", "Cities: Skylines II - Urban Promenades", false, false),
        ]);

        ChecksContext.Equal("steam dlc: 两个 DLC 都建出来了", 2, list.Count);
        if (list.Count != 2)
        {
            return;
        }

        ChecksContext.Equal("steam dlc: 名字原样保留", "Cities: Skylines II - Beach Properties", list[0].DisplayName);
        ChecksContext.Equal("steam dlc: appid 原样保留", "1094000", list[0].SteamId);
        ChecksContext.Check("steam dlc: pops_id 必须为空（CS2 没有 pops_id）", list[0].PopsId.Length == 0);
        ChecksContext.Equal("steam dlc: 有效 id 用 steam: 前缀", "steam:1094000", list[0].FilePathRelativeToDlcPath);
        ChecksContext.Equal("steam dlc: 有效 id 就是 FilePathRelativeToDlcPath", "steam:1094000", DlcCatalog.GetEffectiveDlcId(list[0]));
        ChecksContext.Check("steam dlc: 没有磁盘缩略图", list[0].ThumbnailPath.Length == 0);
        ChecksContext.Check("steam dlc: 简介留空（由 VM 联网补）", list[0].Description.Length == 0);
        ChecksContext.Check("steam dlc: 不带原始字节（不参与 dlc_signature）", list[0].RawBytes.Length == 0);
    }

    private static void CheckDeduplication()
    {
        var list = SteamDlcListBuilder.FromOwnership(
        [
            new SteamDlcOwnership("1094000", "First", true, true),
            new SteamDlcOwnership("   ", "No appid", false, false),
            new SteamDlcOwnership("1094001", "   ", false, false),
            new SteamDlcOwnership("1094000", "Duplicate of first", false, false),
            new SteamDlcOwnership("1094002", "Third", false, false),
        ]);

        ChecksContext.Equal("steam dlc: 空名/空 appid/重复 appid 都被丢掉", 2, list.Count);
        if (list.Count != 2)
        {
            return;
        }

        ChecksContext.Equal("steam dlc: 重复 appid 先到先得", "First", list[0].DisplayName);
        ChecksContext.Equal("steam dlc: 保持 Steam 返回顺序", "Third", list[1].DisplayName);
    }

    /// <summary>
    /// CS2 的 DLC 没有 pops_id，因此不能被写进 content_load.json 的 disabledDLC：
    /// 有效 id 必须唯一（否则同名列会互相覆盖），并且 <c>CanBeDisabledInContentLoad</c> 必须是 false。
    /// </summary>
    private static void CheckIdsAreWritableOnlyByKey()
    {
        var list = SteamDlcListBuilder.FromOwnership(
        [
            new SteamDlcOwnership("1094000", "Same name", true, true),
            new SteamDlcOwnership("1094001", "Same name", true, true),
            new SteamDlcOwnership("1094002", "Same name", true, true),
        ]);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allLocked = true;
        foreach (var info in list)
        {
            ids.Add(DlcCatalog.GetEffectiveDlcId(info));
            allLocked &= !DlcCatalog.CanBeDisabledInContentLoad(info);
        }

        ChecksContext.Equal("steam dlc: 重名 DLC 的有效 id 仍然互不相同", list.Count, ids.Count);
        ChecksContext.Check("steam dlc: 所有条目都无法通过加载文件禁用（开关应隐藏）", allLocked);
    }

    private static void CheckNullInput()
    {
        ChecksContext.Equal("steam dlc: null 报告给出空清单", 0, SteamDlcListBuilder.FromOwnership(null).Count);
        ChecksContext.Equal("steam dlc: 空集合给出空清单", 0, SteamDlcListBuilder.FromOwnership([]).Count);
    }

    private static void CheckBuildDlcIdValidation()
    {
        var threw = false;
        try
        {
            _ = SteamDlcListBuilder.BuildDlcId("   ");
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        ChecksContext.Check("steam dlc: 空 appid 生成 id 会抛 ArgumentException", threw);
        ChecksContext.Equal("steam dlc: 生成 id 会去掉两端空白", "steam:949230", SteamDlcListBuilder.BuildDlcId(" 949230 "));
    }

    /// <summary>
    /// 没有磁盘清单时，判定的分母必须回落到 Steam 的报告（CS2 就是这样），
    /// 否则判定条会永远说"0 个 DLC，看起来一切正常"。
    /// </summary>
    private static void CheckVerdictWithoutDiskManifest()
    {
        var dlcs = new[]
        {
            new SteamDlcOwnership("1094000", "Owned DLC", true, true),
            new SteamDlcOwnership("1094001", "Unowned DLC", false, false),
        };
        var report = Report(GamePatchStatus.Vanilla, dlcs, ["1094000"]);
        var candidates = SteamDlcListBuilder
            .FromOwnership(dlcs)
            .Select(static info => new DlcOwnershipCandidate(info.SteamId, info.DisplayName))
            .ToList();

        var partial = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Vanilla, report, candidates);
        ChecksContext.Equal("steam dlc: 无磁盘清单时按 Steam 报告计数", 2, partial.DiskDlcCount);
        ChecksContext.Equal("steam dlc: 无磁盘清单时算出 1 个已授权", 1, partial.OwnedDiskDlcCount);
        ChecksContext.Equal("steam dlc: 未打补丁 + 有缺授权 → VanillaPartial", DlcOwnershipVerdictKind.VanillaPartial, partial.Kind);
        ChecksContext.Equal("steam dlc: 未授权名字进提示", 1, partial.UnownedNames.Count);

        var allOwned = Report(GamePatchStatus.Patched, dlcs, ["1094000", "1094001"]);
        var effective = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, allOwned, candidates);
        ChecksContext.Equal("steam dlc: 打补丁 + 全授权 → PatchedEffective", DlcOwnershipVerdictKind.PatchedEffective, effective.Kind);
        ChecksContext.Equal("steam dlc: 全授权时没有未授权名字", 0, effective.UnownedNames.Count);

        // 传 null 候选（界面在列表还没建好时就是这样调的）也必须走同一个 CS2 分支，不能抛。
        var withoutCandidates = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Vanilla, report, null);
        ChecksContext.Equal("steam dlc: 没有候选列表时仍然可用", 2, withoutCandidates.ProbeDlcCount);
    }

    private static SteamOwnershipReport Report(
        GamePatchStatus patchStatus,
        IReadOnlyList<SteamDlcOwnership> dlcs,
        IReadOnlyCollection<string> owned)
    {
        var ownedSet = new HashSet<string>(owned, StringComparer.OrdinalIgnoreCase);
        return new SteamOwnershipReport(
            State: SteamOwnershipState.Succeeded,
            PatchStatus: patchStatus,
            SteamAppId: int.Parse(Cs2AppId, System.Globalization.CultureInfo.InvariantCulture),
            ApiDirectory: @"X:\steamapps\common\Cities Skylines II\Cities2_Data\Plugins\x86_64",
            ApiFileName: "steam_api64.dll",
            ApiFilePath: @"X:\steamapps\common\Cities Skylines II\Cities2_Data\Plugins\x86_64\steam_api64.dll",
            SteamId64: 76561198370719182,
            ManifestLastOwner: "76561198370719182",
            ManifestOwnerMatches: true,
            Dlcs: dlcs,
            OwnedAppIds: ownedSet,
            TrustedDisabledDlc: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ErrorKey: null,
            Detail: null);
    }
}
