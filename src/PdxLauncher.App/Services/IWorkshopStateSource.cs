namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Steam;

/// <summary>
/// 「整库 Steam 工坊状态」的能力抽象：一次拿回订阅集合，按批触发下载 / 本地禁用。
/// <para>
/// 与 <see cref="IWorkshopSubscribeBackend"/>（单条目订阅 / 退订）分开：那个是详情页的写操作，
/// 这个是 Mod 库页的只读浏览 + 少量写操作。分开的好处是 Mod 库页可以不依赖订阅开关就显示状态，
/// 只有真正点「下载 / 禁用」时才受设置里的写开关约束。
/// </para>
/// </summary>
public interface IWorkshopStateSource
{
    /// <summary>当前是否可用（宿主在、能找到 dll、知道 appid）。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用原因（中文）；可用时为 null。</summary>
    string? UnavailableReason { get; }

    /// <summary>一次拉回当前游戏的全部订阅条目及其安装状态。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订阅列表结果；不可用时返回带原因的空结果。</returns>
    Task<SteamHostSubscribedResult> ReadSubscribedAsync(CancellationToken cancellationToken);

    /// <summary>触发一条工坊条目的下载（Steam 侧立即排队，高优先级）。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该条结果。</returns>
    Task<WorkshopBatchItem> TriggerDownloadAsync(ulong publishedFileId, CancellationToken cancellationToken);

    /// <summary>在 Steam 里把一条工坊条目设为本地禁用 / 解除禁用（订阅关系保留）。</summary>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="disabled">true=禁用，false=解除。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该条结果。</returns>
    Task<WorkshopBatchItem> SetDisabledLocallyAsync(ulong publishedFileId, bool disabled, CancellationToken cancellationToken);
}
