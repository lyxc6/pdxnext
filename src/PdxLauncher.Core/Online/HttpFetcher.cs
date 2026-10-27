namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 基于 SocketsHttpHandler 的 HTTP 实现，不引用任何 NuGet 包。
/// 下载用 ResponseHeadersRead 流式读取，所以慢速代理（实测 ~10 KB/s）不会因整体超时被截断。
/// </summary>
public sealed class HttpFetcher : IHttpFetcher, IDisposable
{
    /// <summary>
    /// 连接超时 30 秒。旧值 15 秒实测会在到 metadata-assets 的 TLS 握手上大面积超时
    /// （一次抖动 6 项主题素材全灭），配合 AssetDownloader 的 2 次重试一起兜住慢链路。
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>整请求默认超时 180 秒（文档 §7 实测要求）。</summary>
    public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(180);

    /// <summary>与官方启动器一致的 UA（服务端实测不校验，保持一致更稳）。</summary>
    public const string UserAgent = "ParadoxLauncher/1.0";

    /// <summary>与官方启动器一致的版本头名。</summary>
    public const string LauncherVersionHeaderName = "x-pdx-launcher-version";

    /// <summary>与官方启动器一致的版本头值。</summary>
    public const string LauncherVersionHeaderValue = "1.0";

    private readonly HttpClient _client;

    public HttpFetcher(IAppLogger logger, IWebProxy? proxy = null, TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        // 代理完全由调用方（NetworkComposition / ProxyResolver）决定：
        // 这里再隐式读环境变量会让「直接连接」模式名不副实。
        var effectiveProxy = proxy;
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            ConnectTimeout = ConnectTimeout,
            Proxy = effectiveProxy,
            UseProxy = effectiveProxy is not null,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            // 公司/校园网代理常见 407 NTLM 质询：用登录用户的默认凭据应答。
            // 直连时该属性不生效；代理地址里显式写了 user:pass 的仍以 WebProxy.Credentials 为准。
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = totalTimeout ?? DefaultTotalTimeout,
        };
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        _client.DefaultRequestHeaders.TryAddWithoutValidation(LauncherVersionHeaderName, LauncherVersionHeaderValue);
    }

    public async Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var response = await _client
            .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new HttpRequestException("HTTP " + status + " " + uri);
        }

        try
        {
            // 返回内容流即可：流的释放会把连接归还连接池。
            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(jsonBody);

        using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(uri, content, cancellationToken).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("HTTP " + (int)response.StatusCode + " " + uri);
        }

        return text;
    }

    public void Dispose() => _client.Dispose();
}
