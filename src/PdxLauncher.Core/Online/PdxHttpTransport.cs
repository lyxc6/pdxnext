namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;
using System.Text;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="IPdxHttpTransport"/> 的真实实现。与 <see cref="HttpFetcher"/> 共用同一份
/// 代理/超时策略（构造时传入同一个 <see cref="IWebProxy"/>，由 <c>NetworkComposition</c> 统一决定）。
/// </summary>
public sealed class PdxHttpTransport : IPdxHttpTransport, IDisposable
{
    private readonly HttpClient _client;
    private readonly IAppLogger _logger;

    /// <summary>创建传输。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="proxy">代理；null 表示直连。</param>
    /// <param name="totalTimeout">整请求超时；null 用 <see cref="HttpFetcher.DefaultTotalTimeout"/>。</param>
    public PdxHttpTransport(IAppLogger logger, IWebProxy? proxy = null, TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = HttpFetcher.ConnectTimeout,
            Proxy = proxy,
            UseProxy = proxy is not null,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = totalTimeout ?? HttpFetcher.DefaultTotalTimeout,
        };

        // 与官方启动器一致的 UA / 版本头：服务端目前不校验，但保持一致更稳。
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HttpFetcher.UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation(
            HttpFetcher.LauncherVersionHeaderName,
            HttpFetcher.LauncherVersionHeaderValue);
    }

    /// <inheritdoc />
    public async Task<PdxHttpResponse> SendAsync(
        string method,
        Uri uri,
        string? authorizationJson,
        string? jsonBody,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        // 与 AssetDownloader 同一道安全门：不在白名单的域名一律不发请求。
        if (!OnlineHosts.IsAllowed(uri))
        {
            throw new HttpRequestException("host-not-allowed:" + (uri?.Host ?? "<null>"));
        }

        using var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (authorizationJson is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationJson);
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await _client
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status >= 400)
        {
            // 只记状态码与路径，绝不把 Authorization（含 token/密码哈希）写进日志。
            _logger.Warn("PDX 请求失败：" + method + " " + uri.AbsolutePath + " → HTTP " + status);
        }

        return new PdxHttpResponse(status, body);
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();
}
