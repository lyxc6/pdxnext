namespace PdxLauncher.Core.Online;

/// <summary>
/// <see cref="AssetDownloader"/> 的条件下载结果。304 与「下载成功」必须能区分：
/// 304 只允许更新索引里的检查时间，绝不能重写缓存文件。
/// </summary>
public enum AssetDownloadStatus
{
    Downloaded,
    NotModified,
    Failed,
}

/// <summary>一次主题素材下载的结果（含服务端给的校验头）。</summary>
public sealed record AssetDownloadResult(
    AssetDownloadStatus Status,
    string? LocalPath,
    string? ETag,
    string? LastModified)
{
    public static AssetDownloadResult Failed { get; } = new(AssetDownloadStatus.Failed, null, null, null);

    public static AssetDownloadResult NotModified(string? etag, string? lastModified) =>
        new(AssetDownloadStatus.NotModified, null, etag, lastModified);

    public static AssetDownloadResult Downloaded(string path, string? etag, string? lastModified) =>
        new(AssetDownloadStatus.Downloaded, path, etag, lastModified);
}
