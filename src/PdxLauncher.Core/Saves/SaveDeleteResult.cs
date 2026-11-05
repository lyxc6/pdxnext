namespace PdxLauncher.Core.Saves;

/// <summary>
/// 删除结果。<see cref="SourceStillPresent"/> 对「删除备份」表示 <c>.bak</c> 是否仍在，
/// 对「删除真实存档」表示源存档是否仍在。
/// </summary>
/// <param name="Status">结果状态。</summary>
/// <param name="Message">人类可读说明（中文）。</param>
/// <param name="SourceStillPresent">源（<c>.bak</c> 或真实存档）是否仍在原位置。</param>
/// <param name="TrashedPath">删除真实存档时移入回收站后的绝对路径，删除备份时为 <see langword="null"/>。</param>
public sealed record SaveDeleteResult(
    SaveDeleteStatus Status,
    string Message,
    bool SourceStillPresent,
    string? TrashedPath)
{
    /// <summary>失败后仍残留的文件绝对路径清单。</summary>
    public IReadOnlyList<string> RemainingPaths { get; init; } = Array.Empty<string>();

    /// <summary>是否成功。</summary>
    public bool IsSuccess => Status == SaveDeleteStatus.Ok;

    internal static SaveDeleteResult Ok(string message, string? trashedPath = null) =>
        new(SaveDeleteStatus.Ok, message, false, trashedPath);

    internal static SaveDeleteResult Invalid(string message) =>
        new(SaveDeleteStatus.InvalidArgument, message, false, null);

    internal static SaveDeleteResult NotFound(string message) =>
        new(SaveDeleteStatus.NotFound, message, false, null);

    internal static SaveDeleteResult Failed(
        string message,
        bool sourceStillPresent,
        IReadOnlyList<string>? remaining = null,
        string? trashedPath = null) =>
        new(SaveDeleteStatus.Failed, message, sourceStillPresent, trashedPath)
        {
            RemainingPaths = remaining ?? Array.Empty<string>(),
        };
}
