namespace PdxLauncher.Core.ModStudio;

/// <summary>一次描述符编辑要写入的字段；空值由 <see cref="ModDescriptorEditor"/> 统一归一化。</summary>
/// <param name="Name">展示名（必填）。</param>
/// <param name="Version">mod 版本；留空按 <c>0.1.0</c> 写。</param>
/// <param name="SupportedVersion">要求的游戏版本；允许空串。</param>
/// <param name="Tags">标签；调用方负责切分，编辑器仍会去空、去重并截到 16 条。</param>
/// <param name="Description">简介；只有 <see cref="ModDescriptorFamily.Metadata"/> 会写。</param>
public sealed record ModDescriptorEdit(
    string Name,
    string Version,
    string SupportedVersion,
    IReadOnlyList<string> Tags,
    string? Description);
