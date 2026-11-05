namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Steam;

/// <summary>
/// DLC 列表规划器的自检：已拥有/未拥有的求差、磁盘名字优先、空名字跳过、
/// loader 模板的可靠性闸门、报告缺失与排序稳定性。
/// 纯内存，不加载 DLL、不联网、不碰用户目录。
/// </summary>
public static class DlcListPlannerChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckDifference();
        CheckDiskNameWins();
        CheckBlankNamesSkipped();
        CheckCatalogTooLarge();
        CheckUntrustedKeepsDiskRows();
        CheckOwnedSectionLabel();
        CheckCatalogAtLimit();
        CheckEmptyNameRatio();
        CheckEmptyNameRatioAtLimit();
        CheckNullReport();
        CheckUnavailableReport();
        CheckOwnedNeverInUnowned();
        CheckDiskRowsMatchReport();
        CheckStableOrder();
        CheckCatalogDeduplication();
        CheckTrustedDisabledCountsAsOwned();
        CheckEmptyDiskRows();
        CheckEmptyInputs();
        CheckSteamOnlyIds();
        CheckCounts();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- 求差

    private static void CheckDifference()
    {
        var plan = DlcListPlanner.Create(
            [
                Disk("10", "Base Game DLC"),
                Disk("11", "Second DLC"),
            ],
            Report(
                [
                    Dlc("10", "Base Game DLC"),
                    Dlc("11", "Second DLC"),
                    Dlc("20", "Missing One"),
                    Dlc("21", "Missing Two"),
                ],
                owned: ["10", "11"]));

        ChecksContext.Equal("planner: 磁盘两行都进条目", 2, plan.Entries.Count);
        ChecksContext.Equal("planner: 未拥有 = 报告减磁盘减已拥有", 2, plan.Unowned.Count);
        ChecksContext.Equal("planner: 未拥有段状态可展示", DlcUnownedListState.Available, plan.UnownedState);
        ChecksContext.Equal("planner: 未拥有第一个名字", "Missing One", plan.Unowned[0].DisplayName);
        ChecksContext.Equal("planner: 未拥有第二个名字", "Missing Two", plan.Unowned[1].DisplayName);
        ChecksContext.Check("planner: 未拥有条目全部标记为未拥有", plan.Unowned.All(static e => e.Owned == false));
        ChecksContext.Check("planner: 未拥有条目来自 Steam 段", plan.Unowned.All(static e => e.Section == DlcListSection.SteamOnly));
    }

    private static void CheckDiskNameWins()
    {
        var plan = DlcListPlanner.Create(
            [Disk("10", "磁盘上的名字")],
            Report([Dlc("10", "Steam 的名字")], owned: ["10"]));

        ChecksContext.Equal("planner: 磁盘名字优先", "磁盘上的名字", plan.Entries[0].DisplayName);
        ChecksContext.Check("planner: 磁盘行已拥有", plan.Entries[0].Owned == true);
    }

    private static void CheckBlankNamesSkipped()
    {
        var plan = DlcListPlanner.Create(
            [],
            Report(
                [
                    Dlc("20", "   "),
                    Dlc("21", ""),
                    Dlc("  ", "No appid"),
                    Dlc("22", "  Real DLC  "),
                ]));

        ChecksContext.Equal("planner: 空名字/空 appid 不造行", 1, plan.Unowned.Count);
        ChecksContext.Equal("planner: 剩下的真实条目名字被 trim", "Real DLC", plan.Unowned[0].DisplayName);
        ChecksContext.Equal("planner: 空名字条数如实统计", 2, plan.EmptyNameCount);
        ChecksContext.Equal("planner: 清单条数含空名字条目", 4, plan.CatalogCount);
    }

    // ---------------------------------------------------------------- 闸门

    private static void CheckCatalogTooLarge()
    {
        // loader 的 cream_api.ini 模板是 490 行（11 款游戏共用），绝不能整段塞进页面。
        var dlcs = Enumerable
            .Range(0, DlcListPlanner.MaxCatalogEntries + 1)
            .Select(i => Dlc((1000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "DLC " + i))
            .ToList();

        var plan = DlcListPlanner.Create([], Report(dlcs));

        ChecksContext.Equal("planner: >200 条视为不可信", DlcUnownedListState.UntrustedCatalog, plan.UnownedState);
        ChecksContext.Equal("planner: 不可信时不合成未拥有行", 0, plan.Unowned.Count);
        ChecksContext.Equal("planner: 不可信时仍报告原始条数", 201, plan.CatalogCount);
    }

    private static void CheckCatalogAtLimit()
    {
        var dlcs = Enumerable
            .Range(0, DlcListPlanner.MaxCatalogEntries)
            .Select(i => Dlc((2000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "DLC " + i))
            .ToList();

        var plan = DlcListPlanner.Create([], Report(dlcs));

        ChecksContext.Equal("planner: 恰好 200 条仍然放行", DlcUnownedListState.Available, plan.UnownedState);
        ChecksContext.Equal("planner: 200 条全部列为未拥有", 200, plan.Unowned.Count);
    }

    private static void CheckEmptyNameRatio()
    {
        var plan = DlcListPlanner.Create(
            [],
            Report([Dlc("30", "Named"), Dlc("31", " "), Dlc("32", "")]));

        ChecksContext.Equal("planner: 空名字占比 >50% 视为不可信", DlcUnownedListState.UntrustedCatalog, plan.UnownedState);
        ChecksContext.Equal("planner: 空名字过多时不合成未拥有行", 0, plan.Unowned.Count);
    }

    private static void CheckEmptyNameRatioAtLimit()
    {
        var plan = DlcListPlanner.Create(
            [],
            Report([Dlc("40", "Named A"), Dlc("41", "Named B"), Dlc("42", " "), Dlc("43", "")]));

        ChecksContext.Equal("planner: 空名字正好 50% 仍然放行", DlcUnownedListState.Available, plan.UnownedState);
        ChecksContext.Equal("planner: 50% 空名字时有名字的两条仍可列出", 2, plan.Unowned.Count);
    }

    // ------------------------------------------------------------ 报告不可用

    private static void CheckUntrustedKeepsDiskRows()
    {
        // 闸门只该拦「未拥有」段的合成，绝不能把磁盘行连同算好的拥有态一起清掉
        // （曾经把 entries.Clear() 写进不可信分支，Checks 全绿却把已拥有段清空）。
        var oversized = Enumerable
            .Range(0, DlcListPlanner.MaxCatalogEntries + 1)
            .Select(i => Dlc((3000 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), "Loader Row " + i))
            .ToList();

        var plan = DlcListPlanner.Create(
            [Disk("10", "On Disk Owned"), Disk("11", "On Disk Unowned")],
            Report(oversized, owned: ["10"]));

        ChecksContext.Equal("planner: 不可信报告时磁盘行仍保留", 2, plan.Entries.Count);
        ChecksContext.Check(
            "planner: 不可信报告不清空已拥有行的拥有态",
            plan.Entries.Single(static e => e.SteamId == "10").Owned == true);
        ChecksContext.Check(
            "planner: 不可信报告下未拥有的磁盘行仍是 false",
            plan.Entries.Single(static e => e.SteamId == "11").Owned == false);
        ChecksContext.Equal("planner: 不可信报告时已拥有计数保留", 1, plan.OwnedCount);
        ChecksContext.Equal("planner: 不可信报告不合成未拥有行", 0, plan.Unowned.Count);
        ChecksContext.Equal("planner: 不可信报告状态不变", DlcUnownedListState.UntrustedCatalog, plan.UnownedState);
    }

    private static void CheckOwnedSectionLabel()
    {
        var fullyOwned = DlcListPlanner.Create(
            [Disk("10", "A"), Disk("11", "B")],
            Report([Dlc("10", "A"), Dlc("11", "B")], owned: ["10", "11"]));

        ChecksContext.Equal("planner: 全部已拥有 → 标题用「已拥有」", DlcOwnedSectionLabel.Owned, fullyOwned.OwnedSectionLabel);
        ChecksContext.Equal("planner: 全部已拥有时计数等于已拥有数", 2, fullyOwned.OwnedCount);
        ChecksContext.Equal("planner: 全部已拥有时计数也等于磁盘行数", fullyOwned.DiskCount, fullyOwned.OwnedCount);

        var partial = DlcListPlanner.Create(
            [Disk("10", "A"), Disk("11", "B")],
            Report([Dlc("10", "A"), Dlc("11", "B")], owned: ["10"]));

        ChecksContext.Equal("planner: 有未拥有行 → 标题改叫「本地 DLC」", DlcOwnedSectionLabel.Local, partial.OwnedSectionLabel);

        // Steam 没跑：磁盘 2 行、已拥有 0；标题若拿磁盘行数冒充已拥有数就会出现「已拥有（2）」+ 两条「授权未知」。
        var unknown = DlcListPlanner.Create(
            [Disk("10", "A"), Disk("11", "B")],
            Report([], state: SteamOwnershipState.SteamNotRunning));

        ChecksContext.Equal("planner: Steam 没跑 → 标题用「本地 DLC」", DlcOwnedSectionLabel.Local, unknown.OwnedSectionLabel);
        ChecksContext.Equal("planner: Steam 没跑时已拥有数为 0", 0, unknown.OwnedCount);
        ChecksContext.Equal("planner: Steam 没跑时磁盘行数仍是 2", 2, unknown.DiskCount);
    }

    private static void CheckNullReport()
    {
        var plan = DlcListPlanner.Create([Disk("10", "Only Disk")], Report([], state: SteamOwnershipState.SteamNotRunning) with { Dlcs = null! });

        ChecksContext.Equal("planner: null 报告 → 未拥有段不可用", DlcUnownedListState.SteamUnavailable, plan.UnownedState);
        ChecksContext.Equal("planner: null 报告仍保留磁盘行", 1, plan.Entries.Count);
        ChecksContext.Check("planner: null 报告时磁盘行拥有态未知", plan.Entries[0].Owned is null);
        ChecksContext.Equal("planner: null 报告不合成未拥有行", 0, plan.Unowned.Count);
    }

    private static void CheckUnavailableReport()
    {
        var plan = DlcListPlanner.Create(
            [Disk("10", "Only Disk")],
            Report([Dlc("20", "Steam Only")], state: SteamOwnershipState.SteamNotRunning));

        ChecksContext.Equal("planner: 未成功的报告 → 未拥有段不可用", DlcUnownedListState.SteamUnavailable, plan.UnownedState);
        ChecksContext.Check("planner: 未成功的报告不判定拥有态", plan.Entries[0].Owned is null);
        ChecksContext.Equal("planner: 未成功的报告不合成未拥有行", 0, plan.Unowned.Count);
    }

    private static void CheckOwnedNeverInUnowned()
    {
        var plan = DlcListPlanner.Create(
            [Disk("10", "On Disk")],
            Report(
                [Dlc("10", "On Disk"), Dlc("11", "Owned Not On Disk"), Dlc("12", "Unowned")],
                owned: ["10", "11"]));

        ChecksContext.Equal("planner: 未拥有只剩一条", 1, plan.Unowned.Count);
        ChecksContext.Equal("planner: 已拥有但不在磁盘的条目也不列进未拥有", "Unowned", plan.Unowned[0].DisplayName);
    }

    private static void CheckDiskRowsMatchReport()
    {
        var plan = DlcListPlanner.Create(
            [
                Disk("99", "Not In Report"),
                Disk("  ", "No AppID"),
            ],
            Report([Dlc("10", "Other")], owned: ["10"]));

        // 排序按名字："No AppID"（空格 < 't'）在前，"Not In Report" 在后。
        ChecksContext.Check("planner: 没有 appid 的磁盘行拥有态未知", plan.Entries[0].Owned is null);
        ChecksContext.Check("planner: 报告里没有的磁盘 appid 判为未拥有", plan.Entries[1].Owned == false);
        ChecksContext.Equal("planner: 没有 appid 的磁盘行名字保留", "No AppID", plan.Entries[0].DisplayName);
    }

    // ---------------------------------------------------------------- 排序

    private static void CheckStableOrder()
    {
        var plan = DlcListPlanner.Create(
            [Disk("3", "B"), Disk("2", "A"), Disk("1", "A")],
            Report([], state: SteamOwnershipState.SteamNotRunning));

        ChecksContext.Equal("planner: 排序按名字", "A", plan.Entries[0].DisplayName);
        ChecksContext.Equal("planner: 同名按 appid 兜底", "1", plan.Entries[0].SteamId);
        ChecksContext.Equal("planner: 同名第二个仍是 A", "A", plan.Entries[1].DisplayName);
        ChecksContext.Equal("planner: 同名第二个 appid 稳定", "2", plan.Entries[1].SteamId);
        ChecksContext.Equal("planner: 最后一个是 B", "B", plan.Entries[2].DisplayName);
    }

    private static void CheckCatalogDeduplication()
    {
        var plan = DlcListPlanner.Create(
            [],
            Report([Dlc("50", "First"), Dlc("50", "Duplicate"), Dlc("51", "Second")]));

        ChecksContext.Equal("planner: 重复 appid 只保留一条", 2, plan.Unowned.Count);
        ChecksContext.Equal("planner: 重复 appid 先到先得", "First", plan.Unowned[0].DisplayName);
    }

    private static void CheckTrustedDisabledCountsAsOwned()
    {
        var plan = DlcListPlanner.Create(
            [Disk("60", "Disk Row")],
            Report([Dlc("60", "Disk Row"), Dlc("61", "Trusted Only")], trusted: ["60", "61"]));

        ChecksContext.Check("planner: 可信 DisabledDLC 也算磁盘行已拥有", plan.Entries[0].Owned == true);
        ChecksContext.Equal("planner: 可信 DisabledDLC 的条目不算未拥有", 0, plan.Unowned.Count);
    }

    // ---------------------------------------------------------------- 边界

    private static void CheckEmptyDiskRows()
    {
        var plan = DlcListPlanner.Create(null, Report([Dlc("70", "Only Steam")]));

        ChecksContext.Equal("planner: 空磁盘行时条目为空", 0, plan.Entries.Count);
        ChecksContext.Equal("planner: 空磁盘行时未拥有照列", 1, plan.Unowned.Count);
    }

    private static void CheckEmptyInputs()
    {
        var plan = DlcListPlanner.Create([], Report([]));

        ChecksContext.Equal("planner: 空报告 + 空磁盘 → 可展示但为空", DlcUnownedListState.Available, plan.UnownedState);
        ChecksContext.Equal("planner: 空报告没有未拥有行", 0, plan.UnownedCount);
        ChecksContext.Check("planner: 空报告 HasUnowned 为 false", !plan.HasUnowned);
    }

    private static void CheckSteamOnlyIds()
    {
        var plan = DlcListPlanner.Create([], Report([Dlc("80", "Steam Only")]));

        ChecksContext.Equal("planner: 未拥有行的有效 id 用 steam: 前缀", "steam:80", plan.Unowned[0].FilePath);
        ChecksContext.Equal("planner: 未拥有行保留 appid", "80", plan.Unowned[0].SteamId);
    }

    private static void CheckCounts()
    {
        var plan = DlcListPlanner.Create(
            [Disk("90", "Owned"), Disk("91", "Free")],
            Report([Dlc("90", "Owned"), Dlc("91", "Free"), Dlc("92", "Missing")], owned: ["90"]));

        ChecksContext.Equal("planner: 磁盘行计数", 2, plan.DiskCount);
        ChecksContext.Equal("planner: 已拥有计数", 1, plan.OwnedCount);
        ChecksContext.Equal("planner: 未拥有计数", 1, plan.UnownedCount);
    }

    // ------------------------------------------------------------ 辅助构造

    private static DlcListDiskRow Disk(string steamId, string name) => new(steamId, name, "dlc/" + name + ".dlc");

    private static SteamDlcOwnership Dlc(string appId, string name) => new(appId, name, false, false);

    private static SteamOwnershipReport Report(
        IReadOnlyList<SteamDlcOwnership> dlcs,
        IEnumerable<string>? owned = null,
        IEnumerable<string>? trusted = null,
        SteamOwnershipState state = SteamOwnershipState.Succeeded) =>
        new(
            State: state,
            PatchStatus: GamePatchStatus.Vanilla,
            SteamAppId: 281990,
            ApiDirectory: @"X:\steamapps\common\Stellaris",
            ApiFileName: "steam_api64.dll",
            ApiFilePath: @"X:\steamapps\common\Stellaris\steam_api64.dll",
            SteamId64: 76561198370719182,
            ManifestLastOwner: "76561198370719182",
            ManifestOwnerMatches: true,
            Dlcs: dlcs,
            OwnedAppIds: new HashSet<string>(owned ?? [], StringComparer.OrdinalIgnoreCase),
            TrustedDisabledDlc: new HashSet<string>(trusted ?? [], StringComparer.OrdinalIgnoreCase),
            ErrorKey: null,
            Detail: null);
}
