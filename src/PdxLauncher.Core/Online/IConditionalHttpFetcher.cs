namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;

/// <summary>
/// 带 HTTP 条件请求能力的下载出口。只由 <see cref="AssetDownloader"/> 探测，
/// 不并进 <see cref="PdxLauncher.Core.Abstractions.IHttpFetcher"/>：所有既有测试假实现
/// 都不必为了主题缓存补一套 ETag 逻辑，未实现时自动退回普通下载。
/// </summary>
public interface IConditionalHttpFetcher
{
    /// <summary>
    /// 发 GET 并在 304 时正常返回（不抛）。非 2xx/304 仍按既有口径抛
    /// <see cref="HttpRequestException"/> 或限流异常。调用方负责释放 Content。
    /// </summary>
    Task<ConditionalHttpResult> GetConditionalAsync(
        Uri uri,
        string? etag,
        string? lastModified,
        CancellationToken cancellationToken);
}

/// <summary>条件请求的响应。Content 仅在 2xx 时非空，304 时为 null。</summary>
public sealed class ConditionalHttpResult : IAsyncDisposable
{
    public ConditionalHttpResult(
        HttpStatusCode statusCode,
        Stream? content,
        string? etag,
        string? lastModified)
    {
        StatusCode = statusCode;
        Content = content;
        ETag = etag;
        LastModified = lastModified;
    }

    public HttpStatusCode StatusCode { get; }

    public Stream? Content { get; }

    /// <summary>响应里的 ETag（已含引号，可直接回填 If-None-Match）。</summary>
    public string? ETag { get; }

    /// <summary>响应里的 Last-Modified（RFC 1123，可直接回填 If-Modified-Since）。</summary>
    public string? LastModified { get; }

    public async ValueTask DisposeAsync()
    {
        if (Content is not null)
        {
            await Content.DisposeAsync().ConfigureAwait(false);
        }
    }
}
