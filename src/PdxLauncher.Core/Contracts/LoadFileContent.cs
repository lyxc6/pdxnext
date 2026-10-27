namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 要写入加载文件的全部内容。不同格式只取其中一部分字段：
/// V0 用 DisabledDlcFilePaths + EnabledModRegistryIds；
/// V1 用 DisabledDlcPopsIds + EnabledModArchivePaths；
/// V1_1 用 DisabledDlcPopsIds + EnabledModDirPaths + EnabledModArchivePaths。
/// </summary>
public sealed record LoadFileContent(
    IReadOnlyList<string> DisabledDlcFilePaths,
    IReadOnlyList<string> DisabledDlcPopsIds,
    IReadOnlyList<string> EnabledModRegistryIds,
    IReadOnlyList<string> EnabledModDirPaths,
    IReadOnlyList<string> EnabledModArchivePaths,
    IReadOnlyList<string> EnabledUgcPaths)
{
    /// <summary>空内容（全部 DLC 启用、无 mod）。</summary>
    public static LoadFileContent Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
}
