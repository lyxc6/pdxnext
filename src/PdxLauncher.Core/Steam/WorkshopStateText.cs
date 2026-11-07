namespace PdxLauncher.Core.Steam;

/// <summary>Mod 库里「Steam 状态徽标」的档位。</summary>
public enum WorkshopStateBadge
{
    /// <summary>不显示徽标（无状态、或未知）。</summary>
    None,

    /// <summary>已安装到本机。</summary>
    Installed,

    /// <summary>已安装但服务端有新版本。</summary>
    UpdateNeeded,

    /// <summary>正在下载。</summary>
    Downloading,

    /// <summary>排队等待下载。</summary>
    DownloadPending,

    /// <summary>在 Steam 里被本地禁用。</summary>
    DisabledLocally,
}

/// <summary>
/// 一个徽标的判定结果：档位 + 文案键 + 可选的百分比参数。
/// </summary>
/// <param name="Badge">档位；<see cref="WorkshopStateBadge.None"/> 表示不显示。</param>
/// <param name="Key">文案资源名（与 <c>src/PdxLauncher.App/Strings/*/Resources.resw</c> 逐字一致）。</param>
/// <param name="Percent">下载完成度 0-100；非下载状态或无进度时为 null。</param>
public sealed record WorkshopStateBadgeInfo(WorkshopStateBadge Badge, string Key, int? Percent)
{
    /// <summary>是否需要显示徽标。</summary>
    public bool IsVisible => Badge != WorkshopStateBadge.None;
}

/// <summary>
/// 「Mod 库行 / 工坊详情页」上 Steam 状态徽标的唯一判定点。
/// <para>
/// 判定顺序固定为：本地禁用 → 排队中 → 下载中 → 需要更新 → 已安装。
/// 这个顺序是有意的：「被本地禁用」是用户主动干预的结果，比「还在下载」更值得先说明；
/// 「排队中」必须先于「下载中」，因为排队位的定义本身就排除了「真在下」；
/// 而「需要更新」必须在「已安装」之前，否则用户永远看不到有更新。
/// </para>
/// <para>
/// 文案键写成字符串常量（Core 不能引用 App 生成的 <c>Keys</c>），
/// 由自检 <c>WorkshopStateTextChecks</c> 保证键名与 i18n 源一致。
/// </para>
/// </summary>
public static class WorkshopStateText
{
    /// <summary>已安装。</summary>
    public const string KeyInstalled = "ModlibSteamStateInstalled";

    /// <summary>需要更新。</summary>
    public const string KeyUpdateNeeded = "ModlibSteamStateUpdateNeeded";

    /// <summary>下载中（带百分比参数）。</summary>
    public const string KeyDownloading = "ModlibSteamStateDownloading";

    /// <summary>下载中但拿不到百分比（Steam 还没给出总量）。</summary>
    public const string KeyDownloadingNoPercent = "ModlibSteamStateDownloadingNoPercent";

    /// <summary>排队等待下载。</summary>
    public const string KeyPending = "ModlibSteamStatePending";

    /// <summary>本地已禁用。</summary>
    public const string KeyDisabled = "ModlibSteamStateDisabled";

    /// <summary>徽标的无障碍名。</summary>
    public const string KeyName = "ModlibSteamStateName";

    /// <summary>
    /// 判定一行该显示哪个徽标。
    /// </summary>
    /// <param name="state">Steam 状态；null 或 <see cref="WorkshopInstallState.Unknown"/> 时不显示。</param>
    /// <returns>档位 + 文案键 + 百分比。</returns>
    public static WorkshopStateBadgeInfo Resolve(WorkshopInstallState? state)
    {
        if (state is null || !state.IsKnown)
        {
            return new WorkshopStateBadgeInfo(WorkshopStateBadge.None, string.Empty, null);
        }

        if (state.IsDisabledLocally == true)
        {
            return new WorkshopStateBadgeInfo(WorkshopStateBadge.DisabledLocally, KeyDisabled, null);
        }

        // 先判「排队」：IsDownloadPending 的定义本身就是「排队位开着、且没有真在下载」，
        // 所以它一旦为 true，就比 IsDownloading 更精确。
        if (state.IsDownloadPending == true)
        {
            return new WorkshopStateBadgeInfo(WorkshopStateBadge.DownloadPending, KeyPending, null);
        }

        if (state.IsDownloading == true)
        {
            var percent = state.DownloadPercent;
            return new WorkshopStateBadgeInfo(
                WorkshopStateBadge.Downloading,
                KeyDownloading,
                percent is null ? null : (int)Math.Round(percent.Value));
        }

        if (state.NeedsUpdate == true)
        {
            return new WorkshopStateBadgeInfo(WorkshopStateBadge.UpdateNeeded, KeyUpdateNeeded, null);
        }

        if (state.IsInstalled == true)
        {
            return new WorkshopStateBadgeInfo(WorkshopStateBadge.Installed, KeyInstalled, null);
        }

        // 只知道「订阅了但没装」（比如刚订阅、Steam 还没开始下载）—— 不显示徽标，
        // 免得把「正在排队等你确认下载」的常态渲染成一排提示。
        return new WorkshopStateBadgeInfo(WorkshopStateBadge.None, string.Empty, null);
    }

    /// <summary>
    /// 详情页用的「需要更新」补充说明：只有在已安装且需要更新时返回 true。
    /// </summary>
    /// <param name="state">Steam 状态。</param>
    /// <returns>是否提示「有更新可用」。</returns>
    public static bool ShowsUpdateHint(WorkshopInstallState? state) =>
        state is not null && state.NeedsUpdate == true && state.IsInstalled == true;
}
