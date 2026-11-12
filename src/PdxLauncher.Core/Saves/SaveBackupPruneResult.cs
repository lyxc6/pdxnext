namespace PdxLauncher.Core.Saves;

/// <summary>显式清理（<c>ISaveGameBackupService.PruneAsync</c>）的结果。</summary>
/// <param name="Status">结果状态。</param>
/// <param name="Message">人类可读说明（中文）。</param>
/// <param name="DeletedEntries">实际删除的备份条目数。</param>
/// <param name="FailedDeletions">删除失败的备份条目数。</param>
/// <param name="Usage">清理后的用量快照。</param>
public sealed record SaveBackupPruneResult(
    SaveBackupStatus Status,
    string Message,
    int DeletedEntries,
    int FailedDeletions,
    SaveBackupUsage Usage)
{
    /// <summary>删除失败后仍残留的文件绝对路径清单。</summary>
    public IReadOnlyList<string> RemainingPaths { get; init; } = Array.Empty<string>();

    /// <summary>是否成功（全部目标都被删除）。</summary>
    public bool IsSuccess => Status == SaveBackupStatus.Ok;
}
