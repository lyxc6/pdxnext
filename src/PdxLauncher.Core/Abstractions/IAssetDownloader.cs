namespace PdxLauncher.Core.Abstractions;

/// <summary>主题素材下载器：流式写 .part 再原子改名。</summary>
public interface IAssetDownloader
{
    /// <summary>下载成功返回最终本地路径（含推断的扩展名），失败返回 null。</summary>
    Task<string?> DownloadAsync(
        string url,
        string targetPathWithoutExtension,
        CancellationToken cancellationToken);
}
