namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// DLC 头图（商店页 460×215 的 <c>header_image</c>）下载与本地缓存。
/// <para>磁盘上的 DLC 描述文件只带 pops_id / name，没有图片；未拥有的 DLC 更没有本地素材，
/// 因此这两类行都通过这里按 Steam appid 补一张头图。网络失败只影响图片，不抛给调用方。</para>
/// </summary>
public interface IDlcArtworkService
{
    /// <summary>
    /// 返回 <c>appid → 本地图片路径</c>。已缓存的直接命中，不联网；缺失的按需取 appdetails 再下载。
    /// <para>取消会原样抛 <see cref="OperationCanceledException"/>，其它异常一律吞掉并记日志。</para>
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetArtworkAsync(
        IReadOnlyCollection<string> steamIds,
        CancellationToken cancellationToken);
}
