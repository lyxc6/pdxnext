namespace PdxLauncher.Core.Online;

/// <summary>带长度信息的下载响应（进度显示与断点续传都用它）。</summary>
/// <param name="Content">响应正文流；释放它会归还连接。</param>
/// <param name="ContentLength">本次响应体声明的长度；chunked 响应为 null。</param>
/// <param name="RangeStart">流从原始文件的第几个字节开始；服务端忽略 Range（回 200）时是 0。</param>
/// <param name="TotalLength">原始文件的总长度（<c>Content-Range</c> 总量或 <c>Content-Length</c>）；未知为 null。</param>
public sealed record HttpDownload(
    Stream Content,
    long? ContentLength,
    long RangeStart = 0,
    long? TotalLength = null);
