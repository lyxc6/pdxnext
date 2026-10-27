namespace PdxLauncher.Core.Online;

using PdxLauncher.Core.Contracts;

/// <summary>
/// mod 缩略图的联网补全入口：先读磁盘缓存，未命中且允许联网时才下载。
/// 只在 <c>allowOnline=false</c> 时保证零请求（设置页关掉联网后完全离线）。
/// </summary>
public interface IModThumbnailService
{
    /// <summary>磁盘缓存（装配解析器时共享同一个实例，避免重复读索引）。</summary>
    ModThumbnailCache Cache { get; }

    /// <summary>
    /// 为给定 mod 补全缩略图：缓存命中的直接返回，未命中的按 <paramref name="allowOnline"/> 决定是否下载。
    /// 单次调用串行执行，避免与用户连续切页时并发写同一份索引。
    /// </summary>
    Task<ModThumbnailFillResult> FillAsync(
        IReadOnlyList<ModLibraryEntry> entries,
        bool allowOnline,
        int retentionDays,
        CancellationToken cancellationToken);
}
