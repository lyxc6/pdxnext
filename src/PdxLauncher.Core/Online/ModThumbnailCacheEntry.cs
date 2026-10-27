namespace PdxLauncher.Core.Online;

/// <summary>
/// mod 缩略图缓存索引里的一条：记录这张图来自哪个 URL、落盘文件名，以及成功/失败时间。
/// 失败时间用于退避，避免离线时对同一个 mod 反复发起请求。
/// </summary>
public sealed class ModThumbnailCacheEntry
{
    /// <summary>下载来源 URL（mods.thumbnailUrl）。</summary>
    public string? Url { get; set; }

    /// <summary>缓存目录下的文件名（不含目录），由下载器按真实图片类型推断扩展名。</summary>
    public string? FileName { get; set; }

    /// <summary>最近一次下载成功的时间（UTC）。</summary>
    public DateTimeOffset? FetchedUtc { get; set; }

    /// <summary>最近一次下载失败的时间（UTC），用于失败退避。</summary>
    public DateTimeOffset? FailedUtc { get; set; }
}
