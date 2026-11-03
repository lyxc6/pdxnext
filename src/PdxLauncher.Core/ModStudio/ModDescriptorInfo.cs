namespace PdxLauncher.Core.ModStudio;

/// <summary>从一个本地 mod 目录读出的描述符信息（只读，不参与写入）。</summary>
/// <param name="Family">描述符家族。</param>
/// <param name="DescriptorPath">描述符文件全路径；archive 型 mod 可能为 null。</param>
/// <param name="FolderName">mod 目录名。</param>
/// <param name="Name">展示名。</param>
/// <param name="Version">mod 版本。</param>
/// <param name="SupportedVersion">要求的游戏版本。</param>
/// <param name="Tags">标签。</param>
/// <param name="Description">简介（只有 metadata 家族有）。</param>
/// <param name="PicturePath">已验存在的封面路径；没有时为 null。</param>
public sealed record ModDescriptorInfo(
    ModDescriptorFamily Family,
    string? DescriptorPath,
    string FolderName,
    string Name,
    string Version,
    string SupportedVersion,
    IReadOnlyList<string> Tags,
    string? Description,
    string? PicturePath)
{
    /// <summary>是否成功读到描述符（<see cref="ModDescriptorFamily.None"/> 视为没读到）。</summary>
    public bool IsReadable => Family != ModDescriptorFamily.None;
}
