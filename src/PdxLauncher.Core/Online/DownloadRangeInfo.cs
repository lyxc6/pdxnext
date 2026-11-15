namespace PdxLauncher.Core.Online;

/// <summary>
/// 从一个下载响应里解析出来的长度信息（断点续传判定 + 进度换算的唯一来源）。
/// </summary>
/// <param name="RangeStart">流在原始文件里的起点：206 取 <c>Content-Range.From</c>；200 一律是 0。</param>
/// <param name="TotalLength">原始文件总长：<c>Content-Range</c> 的总量，或 200 响应的 <c>Content-Length</c>。</param>
/// <param name="ContentLength">本次响应体自己的长度（206 时只是剩余部分）。</param>
public sealed record DownloadRangeInfo(long RangeStart, long? TotalLength, long? ContentLength);
