namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Steam;

/// <summary>
/// "订阅 / 取消订阅某个 Steam 创意工坊条目"的能力抽象。
/// <para>
/// 实现只有两种：<see cref="SteamWorkshopBridge"/>（真的调 Steamworks 原生 API）
/// 与检查工程里的 Fake（用来测试视图模型状态机）。视图模型只依赖本接口，
/// 这样 UI 逻辑可以在没有 Steam、没有原生库的机器上自检。
/// </para>
/// </summary>
public interface IWorkshopSubscribeBackend
{
    /// <summary>当前是否可用（原生库已加载且 Steamworks 已初始化）。</summary>
    bool IsAvailable { get; }

    /// <summary>不可用原因（中文，直接显示给用户）；可用时为 null。</summary>
    string? UnavailableReason { get; }

    /// <summary>
    /// 订阅或取消订阅。成功表示"状态已生效"，失败给出可直接展示的原因。
    /// </summary>
    Task<WorkshopSubscribeResult> SetSubscribedAsync(ulong publishedFileId, bool subscribe, CancellationToken cancellationToken);

    /// <summary>
    /// 读实时状态（来自 <c>GetItemState</c>）；不可用时返回
    /// <see cref="WorkshopInstallState.Unknown"/>。
    /// </summary>
    WorkshopInstallState ReadState(ulong publishedFileId);
}

/// <summary>一次订阅 / 退订操作的结果。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="ErrorText">失败原因（成功时为 null）。</param>
public sealed record WorkshopSubscribeResult(bool Ok, string? ErrorText)
{
    /// <summary>成功。</summary>
    public static WorkshopSubscribeResult Success { get; } = new(true, null);

    /// <summary>失败。</summary>
    public static WorkshopSubscribeResult Fail(string errorText) => new(false, errorText);
}
