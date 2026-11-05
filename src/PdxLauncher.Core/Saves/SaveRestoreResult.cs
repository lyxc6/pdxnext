namespace PdxLauncher.Core.Saves;

/// <summary>恢复结果。失败时 <see cref="RemainingPaths"/> 列出仍残留的文件（正常情况下没有被写出的半截文件）。</summary>
/// <param name="Status">结果状态。</param>
/// <param name="Message">人类可读说明（中文）。</param>
/// <param name="RestoredPath">成功时写回的绝对路径。</param>
/// <param name="TrashPath">覆盖恢复时被移入回收站的原存档路径，未发生覆盖则为 <see langword="null"/>。</param>
public sealed record SaveRestoreResult(
    SaveRestoreStatus Status,
    string Message,
    string? RestoredPath,
    string? TrashPath)
{
    /// <summary>失败后仍残留的文件绝对路径清单。</summary>
    public IReadOnlyList<string> RemainingPaths { get; init; } = Array.Empty<string>();

    /// <summary>是否成功。</summary>
    public bool IsSuccess => Status == SaveRestoreStatus.Ok;

    internal static SaveRestoreResult Success(string restoredPath, string? trashPath) =>
        new(SaveRestoreStatus.Ok, "存档恢复完成：" + restoredPath, restoredPath, trashPath);

    internal static SaveRestoreResult Invalid(string message) =>
        new(SaveRestoreStatus.InvalidArgument, message, null, null);

    internal static SaveRestoreResult Failed(string message, IReadOnlyList<string>? remaining = null, string? trashPath = null) =>
        new(SaveRestoreStatus.Failed, message, null, trashPath)
        {
            RemainingPaths = remaining ?? Array.Empty<string>(),
        };
}
