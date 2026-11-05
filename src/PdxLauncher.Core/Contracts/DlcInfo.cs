namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一个 DLC 的元数据。FilePathRelativeToDlcPath 是相对 dlcPath 的 POSIX 路径，
/// RawBytes 是原始文件字节（dlc_signature 计算需要）。
/// <para>Description 是描述文件自带的简介（V3 的 .dlc.json 有 description.en；
/// CK3/Stellaris 的 .dlc 没有这个键，此时为空串，由 UI 层联网补全）。</para>
/// <para>由 <c>game/dlc_metadata/*.txt</c> 合成的条目（VIC3 1.15 起）没有磁盘描述文件：
/// <see cref="PopsId"/> 取 metadata 块名（如 <c>dlc019</c>）是<b>推断</b>，官方文档与磁盘都没有
/// pops_id 对照、未与官方 launcher 验证；RawBytes 为空，不参与 dlc_signature。</para>
/// </summary>
public sealed record DlcInfo(
    string DisplayName,
    string PopsId,
    string SteamId,
    string FilePathRelativeToDlcPath,
    string ThumbnailPath,
    string Description,
    byte[] RawBytes);
