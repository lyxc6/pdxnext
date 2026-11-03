namespace PdxLauncher.Core.Online;

using System.Net;
using System.Net.Http;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 基于 SocketsHttpHandler 的 HTTP 实现，不引用任何 NuGet 包。
/// 下载用 ResponseHeadersRead 流式读取，所以慢速代理（实测 ~10 KB/s）不会因整体超时被截断。
/// <para>
/// 代理可以在运行期原地更换（<see cref="UseProxy"/>）：只换底层 <see cref="HttpClient"/> 与连接池，
/// 本对象身份不变。这是故意的——页面在构造函数里拿到 <see cref="IHttpFetcher"/> 之后会长期持有，
/// 换对象等于把它们的引用变成指向已释放连接池的僵尸。
/// </para>
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

    /// <summary>
    /// 换代理时最多保留几个已退休的连接池。在途请求仍握着旧 client，
    /// 立刻 Dispose 会把这些请求打断（正是本轮要修的故障形态），所以延迟释放；
    /// 但不能无限堆积，超过上限就从最老的开始真释放。
    /// </summary>
    private const int MaxRetiredClients = 2;

    private readonly IAppLogger _logger;

    /// <summary>
    /// 出口闸门（按 host 限并发/间隔、429 冷却）。为 null 表示不设闸门——只有自检里的假连接池
    /// 需要这个形态；应用侧一律由 NetworkComposition 注入同一个实例。
    /// </summary>
    private readonly RequestThrottle? _throttle;

    private readonly TimeSpan _totalTimeout;

    /// <summary>已换下来、等下次换池或 Dispose 时释放的连接池。</summary>
    private readonly List<HttpClient> _retired = new();

    private readonly object _retireGate = new();

    /// <summary>当前连接池；由 <see cref="UseProxy"/> 原子替换，读侧一律 <c>Volatile.Read</c>。</summary>
    private HttpClient _client;

    /// <param name="logger">日志出口。</param>
    /// <param name="proxy">代理；null 表示直连。</param>
    /// <param name="totalTimeout">整请求超时；null 用 <see cref="DefaultTotalTimeout"/>。</param>
    /// <param name="throttle">
    /// 出口闸门；null 表示不限流。应用里由 NetworkComposition 传入共享实例，
    /// 参数放最后是为了不打断既有的 <c>new HttpFetcher(logger)</c> 调用点。
    /// </param>
    public HttpFetcher(
        IAppLogger logger,
        IWebProxy? proxy = null,
        TimeSpan? totalTimeout = null,
        RequestThrottle? throttle = null)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _totalTimeout = totalTimeout ?? DefaultTotalTimeout;
        _throttle = throttle;
        _client = BuildClient(proxy, _totalTimeout);
    }

    /// <summary>
    /// 按新代理重建连接池，本对象身份保持不变（页面里缓存的引用继续有效）。
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
            "HTTP 连接池已重建（"
            + (proxy is null ? "直连" : "经代理")
            + "，整请求超时 "
            + (int)timeout.TotalSeconds
            + " 秒）");
    }

    public async Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureAllowed(uri);

        var slot = await AcquireSlotAsync(uri, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            response = await Volatile.Read(ref _client)
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            slot?.Dispose();
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var failure = ClassifyFailure(uri, response);
            response.Dispose();
            slot?.Dispose();
            throw failure;
        }

        _throttle?.ReportSuccess(uri.Host);

        // 闸门只覆盖到「拿到响应头」：接下来的流式读取可能持续几十秒（慢速代理实测 ~10 KB/s），
        // 不应该继续占着并发名额。
        slot?.Dispose();

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

    /// <summary>
    /// 下载入口：与 <see cref="GetAsync"/> 相同，但额外返回 Content-Length，供进度条使用。
    /// 只给更新下载器用（不放进 <see cref="IHttpFetcher"/>，避免所有测试假实现都要跟着改）。
    /// </summary>
    public async Task<HttpDownload> GetDownloadAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        EnsureAllowed(uri);

        var slot = await AcquireSlotAsync(uri, cancellationToken).ConfigureAwait(false);
        HttpResponseMessage response;
        try
        {
            response = await Volatile.Read(ref _client)
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            slot?.Dispose();
            throw;
        }

        if (!response.IsSuccessStatusCode)
        {
            var failure = ClassifyFailure(uri, response);
            response.Dispose();
            slot?.Dispose();
            throw failure;
        }

        _throttle?.ReportSuccess(uri.Host);
        slot?.Dispose();

        try
        {
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            return new HttpDownload(stream, response.Content.Headers.ContentLength);
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
        EnsureAllowed(uri);

        var slot = await AcquireSlotAsync(uri, cancellationToken).ConfigureAwait(false);
        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var response = await Volatile.Read(ref _client)
                .PostAsync(uri, content, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw ClassifyFailure(uri, response);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _throttle?.ReportSuccess(uri.Host);
            return text;
        }
        finally
        {
            slot?.Dispose();
        }
    }

    public async Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(formBody);
        EnsureAllowed(uri);

        var slot = await AcquireSlotAsync(uri, cancellationToken).ConfigureAwait(false);
        try
        {
            using var content = new StringContent(formBody, Encoding.UTF8, "application/x-www-form-urlencoded");
            using var response = await Volatile.Read(ref _client)
                .PostAsync(uri, content, cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw ClassifyFailure(uri, response);
            }

            var text = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _throttle?.ReportSuccess(uri.Host);
            return text;
        }
        finally
        {
            slot?.Dispose();
        }
    }

    /// <summary>向闸门申请一个请求名额；没配闸门时返回 null。</summary>
    private async Task<IDisposable?> AcquireSlotAsync(Uri uri, CancellationToken cancellationToken) =>
        _throttle is null
            ? null
            : await _throttle.AcquireAsync(uri.Host, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// 把非 2xx 响应翻译成异常：被限流时登记冷却并抛 <see cref="SteamThrottleException"/>，
    /// 其余仍是原来的 <see cref="HttpRequestException"/>（调用方既有的错误处理不必改）。
    /// </summary>
    private HttpRequestException ClassifyFailure(Uri uri, HttpResponseMessage response)
    {
        var status = (int)response.StatusCode;
        if (_throttle is not null && HttpThrottleHeaders.IsThrottleStatus(status, response.Headers))
        {
            var retryAfter = HttpThrottleHeaders.ReadRetryAfter(response.Headers, _throttle.Clock);
            _throttle.ReportThrottled(uri.Host, retryAfter);
            return new SteamThrottleException(
                uri.Host,
                retryAfter,
                rejectedByCooldown: false,
                "HTTP " + status + "（被限流）" + uri);
        }

        return new HttpRequestException("HTTP " + status + " " + uri);
    }

    /// <summary>
    /// 出口强制白名单。这里而不是调用方做校验，是因为「调用方自觉」这个约定已经在实测中失效：
    /// 出站调用点里有若干处根本没调 <see cref="OnlineHosts.IsAllowed"/>，加了新集成也不会有人记得补。
    /// 校验放在唯一出口上，漏一个调用点也不可能把请求发到白名单外。
    /// </summary>
    /// <exception cref="HttpRequestException">域名不在白名单内。</exception>
    private static void EnsureAllowed(Uri uri)
    {
        if (!OnlineHosts.IsAllowed(uri))
        {
            throw new HttpRequestException("host-not-allowed:" + uri.Host);
        }
    }

    /// <summary>
    /// 按代理与超时装配一个连接池。构造与 <see cref="UseProxy"/> 共用，
    /// 保证换池前后行为一致（代理凭据、解压、重定向、连接寿命都一样）。
    /// </summary>
    private static HttpClient BuildClient(IWebProxy? proxy, TimeSpan totalTimeout)
    {
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

        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = totalTimeout,
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation(LauncherVersionHeaderName, LauncherVersionHeaderValue);
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
