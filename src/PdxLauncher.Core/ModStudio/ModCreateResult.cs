namespace PdxLauncher.Core.ModStudio;

/// <summary>新建本地 mod 的结果。<see cref="ErrorCode"/> 用稳定短码，由 UI 映射成本地化文案。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="DirPath">成功时的 mod 目录全路径。</param>
/// <param name="ErrorCode">失败原因短码，例如 <c>invalid-name</c>、<c>folder-exists</c>。</param>
public sealed record ModCreateResult(bool Ok, string? DirPath, string? ErrorCode)
{
    /// <summary>成功。</summary>
    public static ModCreateResult Success(string dirPath) => new(true, dirPath, null);

    /// <summary>失败。</summary>
    public static ModCreateResult Fail(string errorCode) => new(false, null, errorCode);
}
