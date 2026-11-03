namespace PdxLauncher.Core.ModStudio;

/// <summary>「新建本地 mod」表单的输入。所有字符串都由调用方先 Trim。</summary>
/// <param name="Name">展示名（写进描述符的 name）。</param>
/// <param name="FolderName">磁盘目录名，也是 legacy 的 <c>&lt;folder&gt;.mod</c> 文件名。</param>
/// <param name="Version">mod 自己的版本号。</param>
/// <param name="SupportedVersion">要求的游戏版本（可空）。</param>
/// <param name="Tags">标签（可空）。</param>
/// <param name="Description">简介，只有 V3/EU5 的 metadata 会写。</param>
/// <param name="PreviewSourcePath">用户选的封面图；为空时写内置占位图。</param>
public sealed record ModCreateRequest(
    string Name,
    string FolderName,
    string Version,
    string SupportedVersion,
    IReadOnlyList<string> Tags,
    string? Description,
    string? PreviewSourcePath);
