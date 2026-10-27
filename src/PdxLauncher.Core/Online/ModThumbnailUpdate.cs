namespace PdxLauncher.Core.Online;

/// <summary>某个 mod 的缩略图已可用（来自磁盘缓存或本次下载）。</summary>
/// <param name="ModId">mods.id。</param>
/// <param name="ThumbnailPath">磁盘上真实存在的图片绝对路径。</param>
public sealed record ModThumbnailUpdate(Guid ModId, string ThumbnailPath);
