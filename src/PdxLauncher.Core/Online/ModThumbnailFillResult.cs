namespace PdxLauncher.Core.Online;

/// <summary>一次缩略图补全的统计结果。</summary>
/// <param name="Updates">补全成功的 mod（缓存命中 + 本次下载）。</param>
/// <param name="CacheHits">其中来自磁盘缓存的数量。</param>
/// <param name="Attempted">本次尝试联网下载的数量。</param>
/// <param name="Failed">本次下载失败的数量。</param>
/// <param name="Pruned">本次清理掉的过期/孤儿缓存文件数。</param>
public sealed record ModThumbnailFillResult(
    IReadOnlyList<ModThumbnailUpdate> Updates,
    int CacheHits,
    int Attempted,
    int Failed,
    int Pruned);
