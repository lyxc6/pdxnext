namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// Windows 事件日志只读接口，供崩溃取证使用。
/// <para>
/// 只查询崩溃时间附近的少量事件；实现必须吞掉所有异常并返回空集合，
/// 绝不因为事件日志权限不足或日志服务未启动而让崩溃分析页打不开。
/// </para>
/// </summary>
public interface IWindowsEventLogReader
{
    /// <summary>
    /// 读取崩溃时间前后最多 <paramref name="windowMinutes"/> 分钟内、与取证相关的事件。
    /// </summary>
    /// <param name="crashTime">崩溃时间。</param>
    /// <param name="windowMinutes">时间窗口（前后各多少分钟）。</param>
    /// <param name="maxEvents">最多返回的事件条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已归类、截断的事件证据；无法读取时返回空集合。</returns>
    IReadOnlyList<WindowsEventEvidence> ReadNear(
        DateTimeOffset crashTime,
        int windowMinutes,
        int maxEvents,
        CancellationToken cancellationToken = default);
}

/// <summary>PC 系统状态只读接口：页面文件、磁盘可用空间、显卡驱动日期。</summary>
public interface IPcSystemSnapshotReader
{
    /// <summary>读取系统快照；任何失败都返回字段未知的空快照，绝不抛异常。</summary>
    PcSystemSnapshot Read(string? dataDirectory, string? gameDirectory);
}

/// <summary>空实现：自检或不支持的系统使用。</summary>
public sealed class NullPcSystemSnapshotReader : IPcSystemSnapshotReader
{
    /// <summary>单例。</summary>
    public static NullPcSystemSnapshotReader Instance { get; } = new();

    /// <inheritdoc />
    public PcSystemSnapshot Read(string? dataDirectory, string? gameDirectory) => PcSystemSnapshot.Unknown;
}

/// <summary>空事件日志实现：自检或不支持事件日志的环境使用。</summary>
public sealed class NullWindowsEventLogReader : IWindowsEventLogReader
{    /// <summary>单例。</summary>
    public static NullWindowsEventLogReader Instance { get; } = new();

    /// <inheritdoc />
    public IReadOnlyList<WindowsEventEvidence> ReadNear(
        DateTimeOffset crashTime,
        int windowMinutes,
        int maxEvents,
        CancellationToken cancellationToken = default) => Array.Empty<WindowsEventEvidence>();
}
