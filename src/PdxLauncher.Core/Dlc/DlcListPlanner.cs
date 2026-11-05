namespace PdxLauncher.Core.Dlc;

using PdxLauncher.Core.Steam;

/// <summary>DLC 列表里一条记录的来源。</summary>
public enum DlcListSection
{
    /// <summary>磁盘上的 <c>.dlc</c> 描述文件：列表主体，带启用开关。</summary>
    Disk,

    /// <summary>只出现在 Steam 清单里：磁盘上没有，因此是未拥有的 DLC。</summary>
    SteamOnly,
}

/// <summary>「未拥有」分组的可用状态（决定页面上是列条目还是给一句说明）。</summary>
public enum DlcUnownedListState
{
    /// <summary>报告可用且可信：未拥有清单可以展示（可能为空）。</summary>
    Available,

    /// <summary>没有可用的授权报告（Steam 没运行 / 探测失败 / 游戏没有 appid）。</summary>
    SteamUnavailable,

    /// <summary>报告本身像 loader 模板或残缺数据，不能拿它合成未拥有行。</summary>
    UntrustedCatalog,
}

/// <summary>
/// 「已拥有」段标题的口径。标题必须和行里的徽标说同一件事：
/// 只有每一行磁盘 DLC 都被报告判定为已拥有时才敢叫「已拥有」；
/// 有「授权未知」或「未拥有」的行时改叫「本地 DLC」，否则会出现
/// 「已拥有（31）」下面 31 条「授权未知」的自相矛盾。
/// </summary>
public enum DlcOwnedSectionLabel
{
    /// <summary>全部磁盘行都已拥有：标题用「已拥有 (N)」。</summary>
    Owned,

    /// <summary>存在未知/未拥有的行（含没有报告）：标题用「本地 DLC (N)」。</summary>
    Local,
}

/// <summary>规划 DLC 列表时的磁盘行输入（只保留规划要用的字段，不拖 <c>DlcInfo</c> 进来）。</summary>
/// <param name="SteamId">磁盘描述文件里写的 Steam AppID；可能为空。</param>
/// <param name="DisplayName">磁盘描述文件里的名字（优先于 Steam 的名字）。</param>
/// <param name="FilePath">相对 dlcPath 的路径，同时是列表行的唯一键。</param>
public sealed record DlcListDiskRow(string SteamId, string DisplayName, string FilePath);

/// <summary>规划结果里的一条 DLC。</summary>
/// <param name="SteamId">Steam AppID；空串表示这条 DLC 没有 appid。</param>
/// <param name="DisplayName">展示名（磁盘行用磁盘名，Steam 独占行用 Steam 名）。</param>
/// <param name="FilePath">列表行的唯一键；Steam 独占行用 <c>steam:&lt;appid&gt;</c>。</param>
/// <param name="Owned">是否已拥有；null 表示授权未知（没有报告、报告不可用或这行没有 appid）。</param>
/// <param name="Section">这一条来自磁盘还是只来自 Steam。</param>
public sealed record DlcListEntry(
    string SteamId,
    string DisplayName,
    string FilePath,
    bool? Owned,
    DlcListSection Section);

/// <summary>
/// 一次 DLC 列表规划的结果：磁盘行（已拥有段）+ Steam 独占的未拥有行 + 未拥有段的可用状态。
/// <para>纯数据，不含 UI 类型；页面只做「按 FilePath 回填拥有态、按条目建行」的映射。</para>
/// </summary>
/// <param name="Entries">磁盘行（按名字排序），拥有态已按报告算好。</param>
/// <param name="Unowned">Steam 独占且未拥有的条目（按名字排序）；不可信/报告不可用时为空。</param>
/// <param name="UnownedState">未拥有段的可用状态。</param>
/// <param name="CatalogCount">报告里 <c>Dlcs</c> 的原始条数（含空名字的）。</param>
/// <param name="EmptyNameCount">报告里名字为空的条数（loader 模板的典型特征）。</param>
public sealed record DlcListPlan(
    IReadOnlyList<DlcListEntry> Entries,
    IReadOnlyList<DlcListEntry> Unowned,
    DlcUnownedListState UnownedState,
    int CatalogCount,
    int EmptyNameCount)
{
    /// <summary>磁盘行数（「已拥有」段的行数）。</summary>
    public int DiskCount => Entries.Count;

    /// <summary>磁盘行里判定为已拥有的条数。</summary>
    public int OwnedCount => Entries.Count(static entry => entry.Owned == true);

    /// <summary>
    /// 「已拥有」段标题的口径：只有每一行磁盘 DLC 都被判定为已拥有时才用「已拥有」。
    /// <para>
    /// 没有报告（Steam 没跑）时所有行都是「授权未知」，此时若还写「已拥有（磁盘行数）」，
    /// 标题就和行里的徽标打架——所以改口叫「本地 DLC」，数量用磁盘行数。
    /// </para>
    /// </summary>
    public DlcOwnedSectionLabel OwnedSectionLabel =>
        Entries.Count > 0 && Entries.All(static entry => entry.Owned == true)
            ? DlcOwnedSectionLabel.Owned
            : DlcOwnedSectionLabel.Local;

    /// <summary>未拥有条数。</summary>
    public int UnownedCount => Unowned.Count;

    /// <summary>未拥有段是否能列出条目（报告可信且有内容）。</summary>
    public bool HasUnowned => Unowned.Count > 0;
}

/// <summary>
/// DLC 列表的合并/求差/排序规则（纯函数，无 IO、无环境依赖，便于 Checks 覆盖）。
/// <para>规则：</para>
/// <list type="number">
/// <item>已拥有 = <c>OwnedAppIds ∪ TrustedDisabledDlc</c>；磁盘行按 <c>SteamId</c> 与报告对齐，名字由磁盘决定。</item>
/// <item>未拥有 = 报告 <c>Dlcs</c> 里「不在磁盘、也不在已拥有」的条目；名字取报告；空名字/空 appid 直接丢弃（绝不造空行）。</item>
/// <item>可靠性闸门：报告条数 &gt; 200 或空名字占比 &gt; 50% 视为 loader 模板/不可信，整段不合成。</item>
/// <item>报告为 null 或不是 <see cref="SteamOwnershipReport.HasOwnership"/> 时只保留磁盘行，拥有态记 null。</item>
/// <item>排序按名字（序数比较，不随系统语言变化），同名再按 appid、路径兜底，保证结果稳定。</item>
/// </list>
/// </summary>
public static class DlcListPlanner
{
    /// <summary>报告条数超过这个值就当作 loader 模板（真机 Stellaris 原版 53 条、loader 490 条）。</summary>
    public const int MaxCatalogEntries = 200;

    /// <summary>报告里空名字条数占比超过这个百分比就当作不可信数据。</summary>
    public const int MaxEmptyNamePercent = 50;

    /// <summary>
    /// 规划列表：合并磁盘行与 Steam 报告，算出拥有态、未拥有条目与未拥有段状态。
    /// </summary>
    /// <param name="diskRows">磁盘 DLC 行（顺序无所谓，输出会重新排序）。</param>
    /// <param name="report">最近一次授权探测的报告；null 表示没探测或 Steam 没运行。</param>
    public static DlcListPlan Create(IEnumerable<DlcListDiskRow>? diskRows, SteamOwnershipReport? report)
    {
        var catalog = report?.Dlcs ?? (IReadOnlyList<SteamDlcOwnership>)Array.Empty<SteamDlcOwnership>();
        var trusted = report is { HasOwnership: true };

        // 已拥有 = 订阅集合 ∪ 采信的 DisabledDLC；两边都可能有空白项，统一 trim 后再比。
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (trusted)
        {
            AddAll(owned, report!.OwnedAppIds);
            AddAll(owned, report.TrustedDisabledDlc);
        }

        var entries = new List<DlcListEntry>();
        var diskIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in diskRows ?? [])
        {
            if (row is null)
            {
                continue;
            }

            // 名字优先用磁盘的：.dlc 里的 DisplayName 比 Steam 目录更贴近游戏内显示。
            var steamId = row.SteamId?.Trim() ?? string.Empty;
            var filePath = row.FilePath ?? string.Empty;
            if (steamId.Length > 0)
            {
                diskIds.Add(steamId);
            }

            bool? isOwned = null;
            if (trusted && steamId.Length > 0)
            {
                isOwned = owned.Contains(steamId);
            }

            entries.Add(new DlcListEntry(
                steamId,
                row.DisplayName ?? string.Empty,
                filePath,
                isOwned,
                DlcListSection.Disk));
        }

        entries.Sort(CompareEntries);

        var catalogCount = 0;
        var emptyNameCount = 0;
        foreach (var dlc in catalog)
        {
            if (dlc is null)
            {
                continue;
            }

            catalogCount++;
            if (string.IsNullOrWhiteSpace(dlc.Name))
            {
                emptyNameCount++;
            }
        }

        var unowned = new List<DlcListEntry>();
        DlcUnownedListState state;
        if (!trusted)
        {
            state = DlcUnownedListState.SteamUnavailable;
        }
        else if (IsUntrusted(catalogCount, emptyNameCount))
        {
            // 报告像 loader 的 490 行模板：宁可整段不显示，也不能往页面塞几百条假 DLC。
            state = DlcUnownedListState.UntrustedCatalog;
        }
        else
        {
            state = DlcUnownedListState.Available;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dlc in catalog)
            {
                var appId = dlc.AppId?.Trim() ?? string.Empty;
                var name = dlc.Name?.Trim() ?? string.Empty;
                if (appId.Length == 0 || name.Length == 0)
                {
                    // 空名字不显示：造出来的只会是"无名 DLC"空行，用户看不懂。
                    continue;
                }

                if (!seen.Add(appId) || diskIds.Contains(appId) || owned.Contains(appId))
                {
                    continue;
                }

                unowned.Add(new DlcListEntry(
                    appId,
                    name,
                    SteamDlcListBuilder.BuildDlcId(appId),
                    Owned: false,
                    DlcListSection.SteamOnly));
            }

            unowned.Sort(CompareEntries);
        }

        return new DlcListPlan(entries, unowned, state, catalogCount, emptyNameCount);
    }

    /// <summary>条目排序：名字 → appid → 路径（全序，保证同名条目顺序稳定）。</summary>
    private static int CompareEntries(DlcListEntry left, DlcListEntry right)
    {
        var byName = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
        if (byName != 0)
        {
            return byName;
        }

        var byAppId = StringComparer.OrdinalIgnoreCase.Compare(left.SteamId, right.SteamId);
        if (byAppId != 0)
        {
            return byAppId;
        }

        return StringComparer.Ordinal.Compare(left.FilePath, right.FilePath);
    }

    /// <summary>
    /// 可靠性闸门：条数过多，或超过一半没有名字，都说明这份清单不是本游戏的真实目录。
    /// <para>
    /// 空名占比这一条在生产路径上目前是防御性保留：<c>SteamDllProbe</c> 在读出条目时会先丢掉空名字
    /// （<c>SteamDllProbe.cs:229-233</c>），所以真实的 loader 模板只会撞到「条数 &gt; 200」那一半。
    /// 留着它是因为 loader 模板的形态会变（例如改成条数不多但没有名字），真变了这里要能兜住。
    /// </para>
    /// </summary>
    private static bool IsUntrusted(int catalogCount, int emptyNameCount) =>
        catalogCount > MaxCatalogEntries
        || (catalogCount > 0 && (long)emptyNameCount * 100 > (long)catalogCount * MaxEmptyNamePercent);

    private static void AddAll(HashSet<string> target, IReadOnlySet<string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (var id in source)
        {
            var trimmed = id?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                target.Add(trimmed);
            }
        }
    }
}
