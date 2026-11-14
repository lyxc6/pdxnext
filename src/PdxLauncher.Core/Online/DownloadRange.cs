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
    /// <param name="requestedStart">
    /// 本次请求的 Range 起点。只用于日志/诊断语义：<b>不参与起点兜底</b>——
    /// 206 不带 Content-Range 时一律按从头开始，见方法体注释。
    /// </param>
    public static DownloadRangeInfo Read(HttpResponseMessage response, long requestedStart)
    {
        ArgumentNullException.ThrowIfNull(response);

        var contentLength = response.Content.Headers.ContentLength;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;

            // 206 却不带 Content-Range 属于不合规响应：此时无法知道这段流从哪个偏移开始。
            // 不能拿 requestedStart 兜底——那等于断言「服务端确实按我的 Range 给的」，
            // 猜错就会把剩余内容写到错误的偏移上，拼出一个长度对、内容错的文件。
            // 一律按「从头开始」处理：调用方会截断重写，尺寸校验随后拦下并整份重下。
            if (range?.From is not long rangeFrom)
            {
                return new DownloadRangeInfo(0, contentLength, contentLength);
            }

            // Content-Range 的 Length 才是「原始文件总长」；没有它就只能说不知道，
            // 绝不能拿 ContentLength（剩余长度）冒充，否则进度条会算成「刚下了一点就到头」。
            return new DownloadRangeInfo(rangeFrom, range.Length, contentLength);
        }

        // 200：服务端忽略了 Range（或不支持），流就是完整内容。起点归零，总长即本次长度。
        return new DownloadRangeInfo(0, contentLength, contentLength);
    }
}
