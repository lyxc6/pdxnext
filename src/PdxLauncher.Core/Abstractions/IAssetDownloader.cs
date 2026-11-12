namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Online;

/// <summary>主题素材下载器：流式写 .part 再原子改名。</summary>
public interface IAssetDownloader
{
    /// <summary>下载成功返回最终本地路径（含推断的扩展名），失败返回 null。</summary>
    Task<string?> DownloadAsync(
        string url,
        string targetPathWithoutExtension,
        CancellationToken cancellationToken);

    /// <summary>
    /// 带 ETag/Last-Modified 的下载。默认实现退回普通下载（无校验头），
    /// 因此既有假实现无需改动；真实 AssetDownloader 会覆写并处理 304。
    /// </summary>
    async Task<AssetDownloadResult> DownloadConditionalAsync(
        string url,
        string targetPathWithoutExtension,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken)
    {
        var path = await DownloadAsync(url, targetPathWithoutExtension, cancellationToken).ConfigureAwait(false);
        return path is null
            ? AssetDownloadResult.Failed
            : AssetDownloadResult.Downloaded(path, etag: null, lastModified: null);
    }
}
