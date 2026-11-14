namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;

/// <summary>
/// 下载响应的长度 / 起点解析。抽成纯函数是因为它是续传正确性的根：
/// 把 206 的剩余长度当成总长，进度条会从 0 开始又冲到底；把被忽略的 Range 当成已续传，
/// 文件前段会被静默重复写一遍。两种情况都不会崩，只会安静地写坏文件，所以必须能被离线自检钉住。
/// </summary>
public static class DownloadRange
{
    /// <summary>
    /// 解析响应的起点与长度。未知的量一律返回 null，调用方据此降级（不猜）。
    /// </summary>
    /// <param name="response">下载响应；调用方已确认它 2xx。</param>
    /// <param name="requestedStart">本次请求的 Range 起点（用于服务端不回 Content-Range 时兜底）。</param>
    public static DownloadRangeInfo Read(HttpResponseMessage response, long requestedStart)
    {
        ArgumentNullException.ThrowIfNull(response);

        var contentLength = response.Content.Headers.ContentLength;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            var rangeStart = range?.From ?? requestedStart;

            // Content-Range 的 Length 才是「原始文件总长」；没有它就只能说不知道，
            // 绝不能拿 ContentLength（剩余长度）冒充，否则进度条会算成「刚下了一点就到头」。
            var totalLength = range?.Length;
            return new DownloadRangeInfo(rangeStart, totalLength, contentLength);
        }

        // 200：服务端忽略了 Range（或不支持），流就是完整内容。起点归零，总长即本次长度。
        return new DownloadRangeInfo(0, contentLength, contentLength);
    }
}
