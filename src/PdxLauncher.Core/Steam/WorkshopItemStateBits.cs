namespace PdxLauncher.Core.Steam;

/// <summary>
/// <c>ISteamUGC::GetItemState</c> 的位定义与「位 + 磁盘信息 → <see cref="WorkshopInstallState"/>」的映射。
/// <para>
/// 抽成独立纯函数有两个理由：一是宿主里的 <see cref="SteamWorkshopHostOperation"/> 与不依赖 SDK 的
/// <see cref="WorkshopSubscriptionReader"/> 必须给出**同一种**口径，否则详情页会在两条路径之间跳变；
/// 二是位运算写错不会崩，只会让界面把「需要更新」显示成「已安装」，只能靠自检钉住。
/// </para>
/// <para>位值见 <c>sdk/public/steam/isteamugc.h</c> 的 <c>EItemState</c>。</para>
/// </summary>
public static class WorkshopItemStateBits
{
    /// <summary>已订阅。</summary>
    public const uint Subscribed = 1u << 0;

    /// <summary>旧式条目（Steam 内部标记，界面不展示）。</summary>
    public const uint LegacyItem = 1u << 1;

    /// <summary>内容已下载到本机。</summary>
    public const uint Installed = 1u << 2;

    /// <summary>已安装但服务端有新版本。</summary>
    public const uint NeedsUpdate = 1u << 3;

    /// <summary>正在下载。</summary>
    public const uint Downloading = 1u << 4;

    /// <summary>排队等待下载。</summary>
    public const uint DownloadPending = 1u << 5;

    /// <summary>在 Steam 里被本地禁用（订阅仍然有效）。</summary>
    public const uint DisabledLocally = 1u << 6;

    /// <summary>
    /// 把原始位、磁盘占用、安装目录与下载进度合成状态。
    /// </summary>
    /// <param name="raw"><c>GetItemState</c> 返回的位组合；<c>0</c> 表示 Steam 未认出该条目。</param>
    /// <param name="sizeOnDisk">已安装内容的磁盘占用；取不到传 null。</param>
    /// <param name="folder">安装目录（通常是相对 <c>workshop/content/&lt;appid&gt;</c> 的子目录名）。</param>
    /// <param name="bytesDownloaded">已下载字节；取不到传 null。</param>
    /// <param name="bytesTotal">总字节；0 或 null 表示未知。</param>
    /// <param name="acfPath">状态来源 acf（Steamworks 路径为 null）。</param>
    /// <returns>三态字段齐全的安装状态。</returns>
    public static WorkshopInstallState ToInstallState(
        uint raw,
        ulong? sizeOnDisk = null,
        string? folder = null,
        ulong? bytesDownloaded = null,
        ulong? bytesTotal = null,
        string? acfPath = null) => new(
        IsSubscribed: (raw & Subscribed) != 0,
        IsInstalled: (raw & Installed) != 0,
        IsDownloading: (raw & (Downloading | DownloadPending)) != 0,
        AcfPath: acfPath,
        NeedsUpdate: (raw & NeedsUpdate) != 0,
        IsDisabledLocally: (raw & DisabledLocally) != 0,
        IsDownloadPending: (raw & DownloadPending) != 0 && (raw & Downloading) == 0,
        SizeOnDisk: sizeOnDisk,
        Folder: folder,
        BytesDownloaded: bytesDownloaded,
        BytesTotal: bytesTotal);

    /// <summary>位组合里是否带 <see cref="NeedsUpdate"/>。</summary>
    /// <param name="raw">原始位。</param>
    /// <returns>需要更新时为 true。</returns>
    public static bool HasUpdate(uint raw) => (raw & NeedsUpdate) != 0;

    /// <summary>位组合里是否带 <see cref="DisabledLocally"/>。</summary>
    /// <param name="raw">原始位。</param>
    /// <returns>被本地禁用时为 true。</returns>
    public static bool IsDisabled(uint raw) => (raw & DisabledLocally) != 0;
}
