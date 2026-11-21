namespace PdxLauncher.Core.Contracts;

/// <summary>Windows 事件日志里与崩溃取证相关的条目类别。</summary>
public enum WindowsEventKind
{
    /// <summary>应用程序错误（Event ID 1000）里记录的故障模块。</summary>
    ApplicationFaultModule,

    /// <summary>系统内存不足警告（Event ID 2004）。</summary>
    LowVirtualMemory,

    /// <summary>显卡驱动停止响应并恢复（TDR，System 4101/13/14/153）。</summary>
    GraphicsDriverReset,

    /// <summary>硬件错误（WHEA）。</summary>
    HardwareError,

    /// <summary>磁盘或文件系统错误（Disk/Ntfs/stornvme/storahci）。</summary>
    DiskError,
}

/// <summary>
/// 一条事件日志证据。只包含取证需要的字段；原文截断后作为展示证据。
/// </summary>
/// <param name="Kind">归类后的证据类别。</param>
/// <param name="EventId">原始事件 ID。</param>
/// <param name="Provider">事件提供程序名称。</param>
/// <param name="Time">事件时间（能解析时）。</param>
/// <param name="FaultingModule">Application 1000 里的故障模块名；其它类别为 null。</param>
/// <param name="Message">截断后的事件消息摘要。</param>
public sealed record WindowsEventEvidence(
    WindowsEventKind Kind,
    long EventId,
    string Provider,
    DateTimeOffset? Time,
    string? FaultingModule,
    string Message);

/// <summary>
/// 与崩溃时间无关、但能帮助判断「是不是电脑问题」的只读系统快照。
/// 字段取不到时为 null，对应检查直接跳过，绝不猜测。
/// </summary>
/// <param name="DataDriveFreeBytes">游戏数据目录所在盘的可用字节数。</param>
/// <param name="GameDriveFreeBytes">游戏安装盘（若与数据盘不同）的可用字节数。</param>
/// <param name="PageFileEnabled">Windows 页面文件是否启用。</param>
/// <param name="GraphicsDriverDate">显卡驱动日期（能读到时）。</param>
public sealed record PcSystemSnapshot(
    long? DataDriveFreeBytes,
    long? GameDriveFreeBytes,
    bool? PageFileEnabled,
    DateTimeOffset? GraphicsDriverDate)
{
    /// <summary>默认空快照：所有字段都未知。</summary>
    public static PcSystemSnapshot Unknown { get; } = new(null, null, null, null);
}
