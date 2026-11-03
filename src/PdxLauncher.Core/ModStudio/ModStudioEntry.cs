namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Contracts;

/// <summary>「Mod 创建」页一行需要的全部信息。</summary>
/// <param name="Mod">本地 Mod 库行。</param>
/// <param name="Descriptor">从磁盘读到的描述符；归档 mod / 未读到时为 null。</param>
/// <param name="Publication">已保存的发布记录；从未上传过为 null。</param>
public sealed record ModStudioEntry(
    ModLibraryEntry Mod,
    ModDescriptorInfo? Descriptor,
    ModPublication? Publication);
