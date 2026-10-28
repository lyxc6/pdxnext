namespace PdxLauncher.Core.Steam;

/// <summary>
/// 某个 Steam 创意工坊条目在**本机**的订阅 / 安装状态。
/// <para>
/// 三个布尔值都可能是 <c>null</c>：<c>null</c> 表示"读不到、不确定"，
/// 与"确定没有"（<c>false</c>）是两件事。UI 必须区分，否则断网或没有 acf 时
/// 会把已订阅的 mod 显示成"未订阅"。
/// </para>
/// </summary>
public sealed record WorkshopInstallState(
    bool? IsSubscribed,
    bool? IsInstalled,
    bool? IsDownloading,
    string? AcfPath)
{
    /// <summary>完全未知（没有 acf、解析失败、参数非法）。</summary>
    public static WorkshopInstallState Unknown { get; } = new(null, null, null, null);

    /// <summary>至少有一项是确定值。</summary>
    public bool IsKnown => IsSubscribed is not null || IsInstalled is not null;
}
