namespace PdxLauncher.Core.CrashAnalysis;

/// <summary>本次游戏里一类错误的汇总（复用 <see cref="CrashLogParser.Signature"/> 作为稳定键）。</summary>
/// <param name="Signature">错误类别键。</param>
/// <param name="Count">去重后的出现次数。</param>
/// <param name="SourcePath">首个脚本来源路径。</param>
/// <param name="ModName">来源归属的 Mod 名（映射不到为 null）。</param>
/// <param name="Sample">样例日志行。</param>
public sealed record SessionErrorGroup(
    string Signature,
    int Count,
    string? SourcePath,
    string? ModName,
    string Sample);

/// <summary>一次游戏会话的只读报错快照，在游戏结束时固化。</summary>
/// <param name="GameId">游戏 ID。</param>
/// <param name="GameName">游戏显示名。</param>
/// <param name="ProcessId">游戏进程 ID（未知为 null）。</param>
/// <param name="StartTime">会话开始时间。</param>
/// <param name="EndTime">会话结束时间（仍在运行为 null）。</param>
/// <param name="ErrorBlocks">本次会话遇到的错误块数（已跨日志去重）。</param>
/// <param name="Groups">错误聚类，按次数倒序。</param>
/// <param name="LogStates">日志文件状态探测结果。</param>
/// <param name="Degraded">是否有读取失败（部分日志可能未计入）。</param>
public sealed record GameSessionSnapshot(
    string GameId,
    string GameName,
    int? ProcessId,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime,
    int ErrorBlocks,
    IReadOnlyList<SessionErrorGroup> Groups,
    IReadOnlyList<LogFileProbeResult> LogStates,
    bool Degraded)
{
    /// <summary>会话持续时长；未结束为 null。</summary>
    public TimeSpan? Duration => EndTime is { } end && end >= StartTime ? end - StartTime : null;

    /// <summary>是否统计到任何错误。</summary>
    public bool HasErrors => ErrorBlocks > 0;
}
