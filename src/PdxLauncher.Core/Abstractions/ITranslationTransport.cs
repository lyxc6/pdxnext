namespace PdxLauncher.Core.Abstractions;

using System.Net.Http;

/// <summary>
/// 翻译用的 HTTP 传输：需要自定义请求头（<c>Authorization: Bearer</c>）以及「读状态码而不是抛异常」。
/// <para>
/// 为什么不直接用 <see cref="IHttpFetcher"/>：那个接口只有 Get/PostJson/PostForm 三个方法，
/// 没有自定义头的入口；改它的签名会连带改掉 4 个自检 fake。翻译因此单开一个窄接口，
/// 与 <see cref="Online.PdxHttpTransport"/> 同样的取向：非 2xx 不抛异常，由调用方判定。
/// </para>
/// </summary>
public interface ITranslationTransport
{
    /// <summary>
    /// 发一个请求并返回状态码与响应正文；<b>非 2xx 不抛异常</b>。
    /// 只有网络层故障（DNS/TLS/超时）或域名未授权才抛异常。
    /// </summary>
    /// <param name="method">HTTP 方法（GET/POST）。</param>
    /// <param name="uri">绝对地址，主机必须在白名单或已授权主机内。</param>
    /// <param name="jsonBody">请求正文；为 null 时不发正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="headers">额外请求头（可为 null），例如 Authorization。</param>
    Task<TranslationResponse> SendAsync(
        HttpMethod method,
        Uri uri,
        string? jsonBody,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null);
}

/// <summary>一次翻译请求的结果（不含异常语义）。</summary>
/// <param name="Success">是否拿到可用的成功响应（2xx）。</param>
/// <param name="StatusCode">HTTP 状态码；未发出请求时为 0。</param>
/// <param name="Body">响应正文（已按 UTF-8 解码）；可能为空串。</param>
/// <param name="Error">失败原因（不含密钥等敏感信息）；成功时为空串。</param>
public readonly record struct TranslationResponse(bool Success, int StatusCode, string Body, string Error)
{
    /// <summary>构造一个失败结果。</summary>
    /// <param name="statusCode">HTTP 状态码；未发出请求时传 0。</param>
    /// <param name="error">失败原因。</param>
    public static TranslationResponse Failure(int statusCode, string error) =>
        new(false, statusCode, string.Empty, error);
}
