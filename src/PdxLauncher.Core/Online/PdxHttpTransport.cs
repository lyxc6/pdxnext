namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;
using System.Text;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// <see cref="IPdxHttpTransport"/> 的真实实现。与 <see cref="HttpFetcher"/> 共用同一份
/// 代理/超时策略（构造时传入同一个 <see cref="IWebProxy"/>，由 <c>NetworkComposition</c> 统一决定）。
/// <para>
/// 与 <see cref="HttpFetcher"/> 一样支持运行期原地换连接池（<see cref="UseProxy"/>）：
/// 页面在构造函数里拿到本对象后会长期持有，换对象会让它们的引用指向已释放的 HttpClient。
/// </para>
/// </summary>
public sealed class PdxHttpTransport : IPdxHttpTransport, IDisposable
{
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
    /// <param name="totalTimeout">整请求超时；null 用 <see cref="HttpFetcher.DefaultTotalTimeout"/>。</param>
    public PdxHttpTransport(IAppLogger logger, IWebProxy? proxy = null, TimeSpan? totalTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _totalTimeout = totalTimeout ?? HttpFetcher.DefaultTotalTimeout;
        _client = BuildClient(proxy, _totalTimeout);
    }

    /// <summary>
    /// 按新代理重建连接池，本对象身份保持不变（账号/Mods 客户端里缓存的引用继续有效）。
    /// 旧连接池进退休队列延迟释放，避免打断正在跑的请求。
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
            "PDX 连接池已重建（"
            + (proxy is null ? "直连" : "经代理")
            + "，整请求超时 "
            + (int)timeout.TotalSeconds
            + " 秒）");
    }

    /// <inheritdoc />
    public async Task<PdxHttpResponse> SendAsync(
        string method,
        Uri uri,
        string? authorizationJson,
        string? jsonBody,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
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
            // 只记状态码与路径，绝不把 Authorization（含 token/密码哈希）写进日志。
            _logger.Warn("PDX 请求失败：" + method + " " + uri.AbsolutePath + " → HTTP " + status);
        }

        return new PdxHttpResponse(status, body);
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

        // 与官方启动器一致的 UA / 版本头：服务端目前不校验，但保持一致更稳。
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
