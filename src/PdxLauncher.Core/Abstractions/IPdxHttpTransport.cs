namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// PDX 平台（api.paradox-interactive.com）专用的 HTTP 传输：需要自定义方法（PUT/DELETE）、
/// 自定义请求头（<c>Authorization</c> 里放的是 JSON，不是 Bearer）以及「读状态码而不是抛异常」。
/// <para>
/// 为什么不在 <see cref="IHttpFetcher"/> 上加方法：那个接口被 6 个自检 fake 实现，
/// 改签名会把无关的检查全带崩；两者共用同一份代理与超时配置即可。
/// </para>
/// </summary>
public interface IPdxHttpTransport
{
    /// <summary>
    /// 发一个请求并返回状态码与响应正文；<b>非 2xx 不抛异常</b>，
    /// 由调用方按状态码映射 <c>ErrorKind</c>。只有网络层故障（DNS/TLS/超时）才抛。
    /// </summary>
    /// <param name="method">HTTP 方法（GET/POST/PUT/DELETE）。</param>
    /// <param name="uri">绝对地址，必须在 <c>OnlineHosts</c> 白名单内。</param>
    /// <param name="authorizationJson">
    /// <c>Authorization</c> 头的值（形如 <c>{"hawk":{...}}</c> / <c>{"session":{"token":"..."}}</c>）；
    /// 为 null 时不发这个头。
    /// </param>
    /// <param name="jsonBody">请求正文；为 null 时不发正文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="headers">
    /// 额外请求头（可为 null）。订阅接口要求 <c>X-Accept-Version: 2</c>，缺了会被服务端按旧版本处理。
    /// </param>
    Task<PdxHttpResponse> SendAsync(
        string method,
        Uri uri,
        string? authorizationJson,
        string? jsonBody,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null);
}

/// <summary>一次 PDX 请求的结果（不含异常语义）。</summary>
/// <param name="StatusCode">HTTP 状态码。</param>
/// <param name="Body">响应正文（已按 UTF-8 解码）。</param>
public readonly record struct PdxHttpResponse(int StatusCode, string Body)
{
    /// <summary>2xx 视为成功。</summary>
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}
