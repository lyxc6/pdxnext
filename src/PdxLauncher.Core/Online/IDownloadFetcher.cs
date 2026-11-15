namespace PdxLauncher.Core.Online;

/// <summary>
/// 下载专用抓取能力：比 <see cref="PdxLauncher.Core.Abstractions.IHttpFetcher"/> 多要两样东西——
/// 断点续传的起始偏移，以及长度 / 总长（进度条与续传判定都要用）。
/// <para>
/// 刻意不并进 <see cref="PdxLauncher.Core.Abstractions.IHttpFetcher"/>：那个接口有 6 个自检假实现，
/// 每加一个成员都要全部跟着改，而这些假实现根本不需要 Range。调用方用 <c>http as IDownloadFetcher</c>
/// 探测，拿不到就退回「无续传、但仍有逐字节进度」的普通下载。
/// </para>
/// </summary>
public interface IDownloadFetcher
{
    /// <summary>发起一次可续传的下载。</summary>
    /// <param name="uri">下载地址；必须命中 <see cref="OnlineHosts"/> 白名单。</param>
    /// <param name="startOffset">从原始文件的第几个字节开始；null / 0 表示从头发起。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>响应流与长度信息；服务端忽略 Range 时 <see cref="HttpDownload.RangeStart"/> 为 0。</returns>
    Task<HttpDownload> GetDownloadAsync(Uri uri, long? startOffset, CancellationToken cancellationToken);
}
