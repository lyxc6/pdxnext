namespace PdxLauncher.Core.ModStudio;

/// <summary>描述符编辑结果。<see cref="ErrorCode"/> 用稳定短码，由 UI 映射成本地化文案。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="ErrorCode">失败原因短码，例如 <c>invalid-name</c>、<c>invalid-json</c>、<c>io:...</c>。</param>
/// <param name="WrittenPaths">实际写入的文件全路径（没有变化时可能为空）。</param>
public sealed record ModDescriptorEditResult(bool Ok, string? ErrorCode, IReadOnlyList<string> WrittenPaths)
{
    /// <summary>成功。</summary>
    public static ModDescriptorEditResult Success(IReadOnlyList<string> writtenPaths) =>
        new(true, null, writtenPaths);

    /// <summary>失败。</summary>
    public static ModDescriptorEditResult Fail(string errorCode) =>
        new(false, errorCode, Array.Empty<string>());
}
