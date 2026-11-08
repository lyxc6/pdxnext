namespace PdxLauncher.Core.Steam;

/// <summary>
/// <c>InstalledDepots</c> 里的一个 depot。带 <see cref="DlcAppId"/> 的是 DLC depot，
/// 不带的是游戏本体 depot（本体 depot 不参与「DLC 内容是否由 Steam 安装」的判定）。
/// </summary>
/// <param name="DepotId">depot id。</param>
/// <param name="DlcAppId"><c>dlcappid</c>；本体 depot 为 null。</param>
/// <param name="Size">清单登记的大小（字节）。</param>
public sealed record AppDepot(int DepotId, int? DlcAppId, long Size);

/// <summary>
/// <c>steamapps\\appmanifest_&lt;appid&gt;.acf</c> 的关键字段。
/// <see cref="InstallPath"/> = &lt;library&gt;\\steamapps\\common\\&lt;InstallDir&gt;。
/// </summary>
public sealed record AppManifest(
    int AppId,
    string Name,
    string InstallDir,
    string InstallPath,
    int StateFlags,
    long SizeOnDisk,
    string? LastOwner = null,
    IReadOnlyList<string>? DisabledDlc = null,
    IReadOnlyList<AppDepot>? InstalledDepots = null,
    string? BuildId = null)
{
    private static readonly IReadOnlySet<string> EmptySet = new HashSet<string>(StringComparer.Ordinal);
    private static readonly IReadOnlySet<int> EmptyAppIdSet = new HashSet<int>();

    /// <summary>StateFlags 的 bit 2（值 4）表示"已完整安装"。Steam 用 4 表示 Installed。</summary>
    public bool IsInstalled => (StateFlags & 4) == 4;

    /// <summary>
    /// 清单里 <c>DisabledDLC</c> 的 id 集合。官方启动器读它来判断「Steam 停用了、
    /// 但账号确实买过」的 DLC，所以仍算已拥有。
    /// </summary>
    public IReadOnlySet<string> DisabledDlcSet
    {
        get
        {
            var items = DisabledDlc;
            if (items is null || items.Count == 0)
            {
                return EmptySet;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                if (!string.IsNullOrWhiteSpace(item))
                {
                    set.Add(item.Trim());
                }
            }

            return set;
        }
    }

    /// <summary>
    /// <see cref="InstalledDepots"/> 里登记为 DLC 的 appid 集合。Steam 只会把**确实安装过**的
    /// DLC depot 写进这里，所以它是「DLC 内容由 Steam 安装」唯一的权威依据；
    /// 手工拷进去的目录不会出现在这里。
    /// </summary>
    public IReadOnlySet<int> InstalledDlcAppIds
    {
        get
        {
            var depots = InstalledDepots;
            if (depots is null || depots.Count == 0)
            {
                return EmptyAppIdSet;
            }

            var set = new HashSet<int>();
            foreach (var depot in depots)
            {
                if (depot.DlcAppId is int appId && appId > 0)
                {
                    set.Add(appId);
                }
            }

            return set;
        }
    }

    /// <summary>
    /// 清单里到底有没有 <c>InstalledDepots</c> 段。为 false 时不能断言任何 DLC「Steam 已装」——
    /// 读不到清单与「Steam 没装」是两件事，UI 必须区分。
    /// </summary>
    public bool KnowsDlcDepots => InstalledDepots is not null;
}
