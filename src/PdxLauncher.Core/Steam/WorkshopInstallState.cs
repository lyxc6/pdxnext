namespace PdxLauncher.Core.Steam;

/// <summary>
/// 某个 Steam 创意工坊条目在**本机**的订阅 / 安装状态。
/// <para>
/// 三个布尔值都可能是 <c>null</c>：<c>null</c> 表示"读不到、不确定"，
/// 与"确定没有"（<c>false</c>）是两件事。UI 必须区分，否则断网或没有 acf 时
/// 会把已订阅的 mod 显示成"未订阅"。
/// </para>
/// <para>
/// 后半段字段只有**经 Steamworks 读取**时才拿得到（宿主子进程里
/// <c>GetItemState</c> + <c>GetItemInstallInfo</c> + <c>GetItemDownloadInfo</c>）；
/// 走 acf 降级路径时它们保持 <c>null</c>，界面据此退化成只有「已安装 / 下载中」两档。
/// </para>
/// </summary>
/// <param name="IsSubscribed">是否已订阅；null=不确定。</param>
/// <param name="IsInstalled">内容是否已在本机；null=不确定。</param>
/// <param name="IsDownloading">是否正在下载（含排队）。</param>
/// <param name="AcfPath">状态来自哪个 acf；Steamworks 路径为 null。</param>
/// <param name="NeedsUpdate">已安装但服务端有新版本。</param>
/// <param name="IsDisabledLocally">在 Steam 里被本地禁用。</param>
/// <param name="IsDownloadPending">排队等待下载（尚未真正开始传数据）。</param>
/// <param name="SizeOnDisk">已安装内容的磁盘占用字节数。</param>
/// <param name="Folder">安装目录名（相对 <c>workshop/content/&lt;appid&gt;</c>）。</param>
/// <param name="BytesDownloaded">当前下载已完成的字节数。</param>
/// <param name="BytesTotal">当前下载的总字节数；0 表示未知。</param>
public sealed record WorkshopInstallState(
    bool? IsSubscribed,
    bool? IsInstalled,
    bool? IsDownloading,
    string? AcfPath,
    bool? NeedsUpdate = null,
    bool? IsDisabledLocally = null,
    bool? IsDownloadPending = null,
    ulong? SizeOnDisk = null,
    string? Folder = null,
    ulong? BytesDownloaded = null,
    ulong? BytesTotal = null)
{
    /// <summary>完全未知（没有 acf、解析失败、参数非法）。</summary>
    public static WorkshopInstallState Unknown { get; } = new(null, null, null, null);

    /// <summary>至少有一项是确定值。</summary>
    public bool IsKnown => IsSubscribed is not null || IsInstalled is not null;

    /// <summary>是否拿到了 Steamworks 侧的额外信息（位标记 / 体积 / 目录）。</summary>
    public bool HasNativeDetail =>
        NeedsUpdate is not null || IsDisabledLocally is not null || SizeOnDisk is not null || Folder is not null;

    /// <summary>
    /// 下载完成度 0-100；总字节未知时返回 null（界面应显示不确定进度或干脆不显示）。
    /// </summary>
    public double? DownloadPercent => BytesTotal is > 0 && BytesDownloaded is not null
        ? Math.Clamp(BytesDownloaded.Value * 100.0 / BytesTotal.Value, 0, 100)
        : null;
}
