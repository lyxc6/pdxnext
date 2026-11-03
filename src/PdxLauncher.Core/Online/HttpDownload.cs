namespace PdxLauncher.Core.Online;

/// <summary>带 Content-Length 的下载响应（Content-Length 可能为 null）。</summary>
/// <param name="Content">响应正文流；释放它会归还连接。</param>
/// <param name="ContentLength">服务端声明的长度；chunked 响应为 null。</param>
public sealed record HttpDownload(Stream Content, long? ContentLength);
