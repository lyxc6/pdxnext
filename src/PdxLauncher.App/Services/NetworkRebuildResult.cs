namespace PdxLauncher.App.Services;

/// <summary>
/// 按新代理设置重建联网服务的结果。失败时只带「原因码 + 细节」，不生成成品文案：
/// 文案由调用方按当前界面语言取，否则英文界面里会冒出中文。
/// </summary>
/// <param name="Success">是否重建成功。</param>
/// <param name="NotInitialized">失败原因是「启动尚未初始化完成」（此时 <paramref name="Detail"/> 为 null）。</param>
/// <param name="Detail">其它失败原因的原始异常消息（框架文本，不做本地化）。</param>
public readonly record struct NetworkRebuildResult(bool Success, bool NotInitialized, string? Detail)
{
    /// <summary>重建成功。</summary>
    public static NetworkRebuildResult Ok { get; } = new(true, false, null);

    /// <summary>服务还没装配完，这次改动只能等下次启动生效。</summary>
    public static NetworkRebuildResult NotReady { get; } = new(false, true, null);

    /// <summary>重建过程抛了异常，旧链路继续沿用。</summary>
    public static NetworkRebuildResult Failed(string detail) => new(false, false, detail);
}
