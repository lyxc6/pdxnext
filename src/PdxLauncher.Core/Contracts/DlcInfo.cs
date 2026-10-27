namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一个 DLC 的元数据。FilePathRelativeToDlcPath 是相对 dlcPath 的 POSIX 路径，
/// RawBytes 是原始文件字节（dlc_signature 计算需要）。
/// <para>Description 是描述文件自带的简介（V3 的 .dlc.json 有 description.en；
/// CK3/Stellaris 的 .dlc 没有这个键，此时为空串，由 UI 层联网补全）。</para>
/// </summary>
public sealed record DlcInfo(
    string DisplayName,
    string PopsId,
    string SteamId,
    string FilePathRelativeToDlcPath,
    string ThumbnailPath,
    string Description,
    byte[] RawBytes);
