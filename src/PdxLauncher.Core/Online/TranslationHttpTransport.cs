namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;
using System.Text;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="ITranslationTransport"/> 的真实实现：自持一个 <see cref="HttpClient"/>，
/// 与 <see cref="HttpFetcher"/> 共用同一份代理与超时策略。
/// <para>
/// 与其它传输一样支持运行期原地换连接池（<see cref="UseProxy"/>），本对象身份保持不变。
/// </para>
/// </summary>
public sealed class TranslationHttpTransport : ITranslationTransport, IDisposable
{
    /// <summary>整请求默认超时 60 秒：翻译请求体小，不该占着连接池太久。</summary>
    public static readonly TimeSpan DefaultTotalTimeout = TimeSpan.FromSeconds(60);

    /// <summary>换代理时最多保留几个已退休的连接池；在途请求仍握着旧 client。</summary>
    private const int MaxRetiredClients = 2;

    private readonly IAppLogger _logger;

    private readonly TimeSpan _totalTimeout;

    private readonly List<HttpClient> _retired = new();

    private readonly object _retireGate = new();

    /// <summary>当前连接池；由 <see cref="UseProxy"/> 原子替换，读侧一律 <c>Volatile.Read</c>。</summary>
    private HttpClient _client;

    /// <summary>创建传输。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="proxy">代理；null 表示直连。</param>
    /// <param name="totalTimeout">整请求超时；null 用 <see cref="DefaultTotalTimeout"/>。</param>
    public TranslationHttpTransport(IAppLogger logger, IWebProxy? proxy = null, TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _totalTimeout = totalTimeout ?? DefaultTotalTimeout;
        _client = BuildClient(proxy, _totalTimeout);
    }

    /// <summary>
    /// 按新代理重建连接池，本对象身份保持不变。旧连接池进退休队列延迟释放。
    /// </summary>
    /// <param name="proxy">新代理；null 表示直连。</param>
    /// <param name="totalTimeout">整请求超时；null 沿用上一次的超时设置。</param>
    public void UseProxy(IWebProxy? proxy, TimeSpan? totalTimeout = null)
    {
        var timeout = totalTimeout ?? _totalTimeout;
        var next = BuildClient(proxy, timeout);
        var previous = Interlocked.Exchange(ref _client, next);
        if (previous is not null)
        {
            Retire(previous);
        }

        _logger.Info(
            "翻译连接池已重建（"
            + (proxy is null ? "直连" : "经代理")
            + "，整请求超时 "
            + (int)timeout.TotalSeconds
            + " 秒）");
    }

    /// <inheritdoc />
    public async Task<TranslationResponse> SendAsync(
        HttpMethod method,
        Uri uri,
        string? jsonBody,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(uri);

        // 与其它传输同一道安全门：不在白名单、也没被用户授权的域名一律不发请求。
        if (!OnlineHosts.IsAllowed(uri))
        {
            throw new HttpRequestException("host-not-allowed:" + (uri.Host ?? "<null>"));
        }

        using var request = new HttpRequestMessage(method, uri);
        if (headers is not null)
        {
            foreach (var pair in headers)
            {
                request.Headers.TryAddWithoutValidation(pair.Key, pair.Value);
            }
        }

        if (jsonBody is not null)
        {
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
        }

        using var response = await Volatile.Read(ref _client)
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var status = (int)response.StatusCode;
        if (status >= 400)
        {
            // 只记状态码与路径，绝不把 Authorization（含 API Key）写进日志。
            _logger.Warn("翻译请求失败：" + method.Method + " " + uri.AbsolutePath + " → HTTP " + status);
        }

        return new TranslationResponse(status is >= 200 and < 300, status, body, string.Empty);
    }

    /// <summary>按代理与超时装配一个连接池；构造与 <see cref="UseProxy"/> 共用。</summary>
    private static HttpClient BuildClient(IWebProxy? proxy, TimeSpan totalTimeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = HttpFetcher.ConnectTimeout,
            Proxy = proxy,
            UseProxy = proxy is not null,
            AllowAutoRedirect = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        };

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = totalTimeout,
        };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", HttpFetcher.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation(
            HttpFetcher.LauncherVersionHeaderName,
            HttpFetcher.LauncherVersionHeaderValue);
        return client;
    }

    /// <summary>把换下来的连接池放进退休队列；超上限时释放最老的那个。</summary>
    private void Retire(HttpClient client)
    {
        List<HttpClient> expired;
        lock (_retireGate)
        {
            _retired.Add(client);
            if (_retired.Count <= MaxRetiredClients)
            {
                return;
            }

            expired = _retired.GetRange(0, _retired.Count - MaxRetiredClients);
            _retired.RemoveRange(0, expired.Count);
        }

        foreach (var item in expired)
        {
            item.Dispose();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Volatile.Read(ref _client).Dispose();
        lock (_retireGate)
        {
            foreach (var item in _retired)
            {
                item.Dispose();
            }

            _retired.Clear();
        }
    }
}
