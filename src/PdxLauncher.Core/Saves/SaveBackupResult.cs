namespace PdxLauncher.Core.Saves;

/// <summary>备份结果。失败时 <see cref="RemainingPaths"/> 列出清理后仍残留的文件（正常为空）。</summary>
/// <param name="Status">结果状态。</param>
/// <param name="Message">人类可读说明（中文）。</param>
/// <param name="Entry">成功时的备份条目。</param>
/// <param name="Usage">配额超限时的当前用量快照。</param>
public sealed record SaveBackupResult(
    SaveBackupStatus Status,
    string Message,
    SaveBackupEntry? Entry,
    SaveBackupUsage? Usage)
{
    /// <summary>失败后仍残留的文件绝对路径清单。</summary>
    public IReadOnlyList<string> RemainingPaths { get; init; } = Array.Empty<string>();

    /// <summary>是否成功。</summary>
    public bool IsSuccess => Status == SaveBackupStatus.Ok;

    internal static SaveBackupResult Success(SaveBackupEntry entry) =>
        new(SaveBackupStatus.Ok, "存档备份完成。", entry, null);

    internal static SaveBackupResult Invalid(string message) =>
        new(SaveBackupStatus.InvalidArgument, message, null, null);

    internal static SaveBackupResult SourceMissing(string message) =>
        new(SaveBackupStatus.SourceMissing, message, null, null);

    internal static SaveBackupResult Quota(string message, SaveBackupUsage usage) =>
        new(SaveBackupStatus.QuotaExceeded, message, null, usage);

    internal static SaveBackupResult Failed(string message, IReadOnlyList<string>? remaining = null) =>
        new(SaveBackupStatus.Failed, message, null, null)
        {
            RemainingPaths = remaining ?? Array.Empty<string>(),
        };
}

/// <summary>备份用量快照：只统计 <c>.bak</c> 正文字节，不含 sidecar 与回收站。</summary>
/// <param name="TotalBytes">所有游戏备份正文的总字节数。</param>
/// <param name="EntryCount">所有游戏的备份条目总数。</param>
public sealed record SaveBackupUsage(long TotalBytes, int EntryCount)
{
    /// <summary>空用量。</summary>
    public static SaveBackupUsage Empty { get; } = new(0, 0);
}
