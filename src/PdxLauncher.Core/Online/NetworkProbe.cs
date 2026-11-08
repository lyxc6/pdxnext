namespace PdxLauncher.Core.Online;

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using PdxLauncher.Core.Abstractions;

/// <summary>一次探测的结果分类。</summary>
public enum NetworkProbeStatus
{
    /// <summary>拿到 2xx 响应。</summary>
    Ok,

    /// <summary>连接或整请求超时（DNS 被污染 / 代理不可达 / 网络中断）。</summary>
    Timeout,

    /// <summary>连上了但服务端返回非 2xx。</summary>
    HttpStatus,

    /// <summary>主机不在 <see cref="OnlineHosts"/> 白名单里，请求根本没发出去。</summary>
    HostNotAllowed,

    /// <summary>其它失败（TLS 中断、连接被重置、解析异常等）。</summary>
    Failed,
}

/// <summary>
/// 一个探测目标。<paramref name="PostFormBody"/> 为 null 时用 GET，否则用 form-urlencoded POST。
/// </summary>
/// <param name="Key">稳定标识（界面按它取自动化 id 与顺序，不用于显示）。</param>
/// <param name="Uri">绝对地址，必须在白名单内。</param>
/// <param name="PostFormBody">POST 表单体；null 表示 GET。</param>
public sealed record NetworkProbeTarget(string Key, Uri Uri, string? PostFormBody = null);

/// <summary>单个目标的探测结果。</summary>
/// <param name="Key">对应的 <see cref="NetworkProbeTarget.Key"/>。</param>
/// <param name="Status">分类。</param>
/// <param name="HttpStatus">非 2xx 时的状态码；其余情况为 null。</param>
/// <param name="Elapsed">从发出到拿到响应头（或失败）的耗时。</param>
/// <param name="Detail">诊断细节（异常消息），界面一般不展示。</param>
public sealed record NetworkProbeResult(
    string Key,
    NetworkProbeStatus Status,
    int? HttpStatus,
    TimeSpan Elapsed,
    string? Detail)
{
    /// <summary>是否算「通」。</summary>
    public bool IsOk => Status == NetworkProbeStatus.Ok;
}

/// <summary>
/// 一次自检的完整结果。
/// <para>
/// <see cref="Direct"/> 为 null 表示当前就在直连，没有必要再测一遍直连做对比。
/// </para>
/// </summary>
/// <param name="Current">按当前代理模式测出的结果，顺序与 <see cref="NetworkProbe.SteamTargets"/> 一致。</param>
/// <param name="Direct">同一批目标忽略代理直连测出的结果；当前是直连时为 null。</param>
public sealed record NetworkProbeReport(
    IReadOnlyList<NetworkProbeResult> Current,
    IReadOnlyList<NetworkProbeResult>? Direct);

/// <summary>
/// 联网连通性自检：对创意工坊真正依赖的三台 Steam 主机各发一次真实请求。
/// <para>
/// 为什么需要它：创意工坊的列表/详情/评论是启动器**自己**发的 HTTP 请求，不走 Steam 客户端；
/// 而 UU / 雷神这类加速器的「Steam 加速」默认是进程模式，只接管 <c>steam.exe</c>。
/// 用户在进程模式下会看到工坊一直转圈，却没有任何办法判断「是加速器没覆盖到本程序，
/// 还是本机网络/代理坏了」。同一个目标再直连测一遍，两种原因就能分开。
/// </para>
/// <para>
/// 探测刻意**不接**共享的 <see cref="RequestThrottle"/>：自检不该往限流统计里灌数据，
/// 也不该因为工坊页面刚被 429 冷却过就必然失败。白名单仍由 <see cref="HttpFetcher"/> 强制。
/// </para>
/// </summary>
public static class NetworkProbe
{
    /// <summary>单次探测的整请求超时。连接超时是 <see cref="HttpFetcher.ConnectTimeout"/>（30 秒），这里必须更短。</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 探测用的 appid：CK3（1158310）。选一个内置创意工坊表里确认有工坊的游戏，
    /// 这样 browse 页与 appdetails 返回的都是真实内容，不会因为 appid 不存在而走奇怪的跳转。
    /// </summary>
    public const string ProbeAppId = "1158310";

    /// <summary>
    /// 探测用的工坊文件 id。实测这个 keyless 接口对 <c>0</c> 直接回 400，
    /// 而任意非零 id（哪怕不存在）都回 200 + <c>result=9</c>，所以取一个大到不会存在的值：
    /// 既证明接口可达，又不依赖某个 mod 还活着。
    /// </summary>
    public const string ProbePublishedFileId = "9999999999";

    /// <summary>创意工坊列表页（HTML）。</summary>
    public const string SteamCommunityKey = "steamcommunity";

    /// <summary>keyless 的已发布文件详情接口（POST form）。</summary>
    public const string SteamApiKey = "steamapi";

    /// <summary>商店 appdetails（工坊能力探测 / DLC 简介同源）。</summary>
    public const string SteamStoreKey = "steamstore";

    private const string NotAllowedPrefix = "host-not-allowed:";

    private const string HttpStatusPrefix = "HTTP ";

    /// <summary>自检目标。顺序即界面显示顺序。</summary>
    public static IReadOnlyList<NetworkProbeTarget> SteamTargets { get; } = new NetworkProbeTarget[]
    {
        new(
            SteamCommunityKey,
            new Uri(OnlineHosts.SteamWorkshopBrowseEndpoint.AbsoluteUri + "?appid=" + ProbeAppId),
            null),
        new(
            SteamApiKey,
            OnlineHosts.SteamPublishedFileDetailsEndpoint,
            "itemcount=1&publishedfileids[0]=" + ProbePublishedFileId),
        new(
            SteamStoreKey,
            new Uri(OnlineHosts.SteamAppDetailsEndpoint.AbsoluteUri + "?appids=" + ProbeAppId + "&filters=categories"),
            null),
    };

    /// <summary>
    /// 按当前代理测一遍，当前不是直连时再直连测一遍做对比。两组并发跑，最慢约 <see cref="DefaultTimeout"/>。
    /// </summary>
    /// <param name="logger">日志出口。</param>
    /// <param name="currentProxy">当前生效的代理；null 表示直连（此时不再重复测直连）。</param>
    /// <param name="cancellationToken">取消标记（关闭页面 / 切语言时不留下在途请求）。</param>
    public static async Task<NetworkProbeReport> RunAsync(
        IAppLogger logger,
        IWebProxy? currentProxy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(logger);

        var currentTask = RunSetAsync(logger, currentProxy, cancellationToken);
        if (currentProxy is null)
        {
            return new NetworkProbeReport(await currentTask.ConfigureAwait(false), null);
        }

        var directTask = RunSetAsync(logger, null, cancellationToken);
        var current = await currentTask.ConfigureAwait(false);
        var direct = await directTask.ConfigureAwait(false);
        return new NetworkProbeReport(current, direct);
    }

    /// <summary>测一个目标，把异常翻译成结果分类，不向外抛（取消除外）。</summary>
    /// <param name="http">HTTP 出口；自检自己建一个短超时的连接池。</param>
    /// <param name="target">目标。</param>
    /// <param name="cancellationToken">取消标记。</param>
    public static async Task<NetworkProbeResult> ExecuteAsync(
        IHttpFetcher http,
        NetworkProbeTarget target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(target);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (target.PostFormBody is null)
            {
                // ResponseHeadersRead：拿到响应头就算通，不下载整页 HTML。
                using var stream = await http.GetAsync(target.Uri, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await http.PostFormAsync(target.Uri, target.PostFormBody, cancellationToken).ConfigureAwait(false);
            }

            stopwatch.Stop();
            return new NetworkProbeResult(target.Key, NetworkProbeStatus.Ok, null, stopwatch.Elapsed, null);
        }
        catch (NetworkTimeoutException ex)
        {
            // 必须排在 HttpRequestException 之前：它是后者的子类。
            stopwatch.Stop();
            return new NetworkProbeResult(target.Key, NetworkProbeStatus.Timeout, null, stopwatch.Elapsed, ex.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // PostFormAsync 不翻译超时（只有 GET 侧翻了），HttpClient.Timeout 触发时只能在这里接住。
            stopwatch.Stop();
            return new NetworkProbeResult(target.Key, NetworkProbeStatus.Timeout, null, stopwatch.Elapsed, ex.Message);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            return Classify(target.Key, stopwatch.Elapsed, ex);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stopwatch.Stop();
            return new NetworkProbeResult(target.Key, NetworkProbeStatus.Failed, null, stopwatch.Elapsed, ex.Message);
        }
    }

    private static async Task<IReadOnlyList<NetworkProbeResult>> RunSetAsync(
        IAppLogger logger,
        IWebProxy? proxy,
        CancellationToken cancellationToken)
    {
        using var http = new HttpFetcher(logger, proxy, DefaultTimeout, throttle: null);
        var tasks = new Task<NetworkProbeResult>[SteamTargets.Count];
        for (var index = 0; index < SteamTargets.Count; index++)
        {
            tasks[index] = ExecuteAsync(http, SteamTargets[index], cancellationToken);
        }

        return await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 按 <see cref="HttpFetcher"/> 的错误消息约定分类。
    /// <para>
    /// <c>host-not-allowed:</c> 与 <c>HTTP &lt;code&gt; &lt;uri&gt;</c> 这两个格式来自
    /// <c>HttpFetcher.EnsureAllowed</c> / <c>ClassifyFailure</c>；自检不接限流，所以这里拿到的是
    /// 普通 <see cref="HttpRequestException"/> 而不是 <see cref="SteamThrottleException"/>。
    /// </para>
    /// </summary>
    private static NetworkProbeResult Classify(string key, TimeSpan elapsed, HttpRequestException ex)
    {
        var message = ex.Message;
        if (message.StartsWith(NotAllowedPrefix, StringComparison.Ordinal))
        {
            return new NetworkProbeResult(key, NetworkProbeStatus.HostNotAllowed, null, elapsed, message);
        }

        if (message.StartsWith(HttpStatusPrefix, StringComparison.Ordinal))
        {
            var rest = message[HttpStatusPrefix.Length..];
            var end = rest.IndexOf(' ', StringComparison.Ordinal);
            var digits = end < 0 ? rest : rest[..end];
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var status))
            {
                return new NetworkProbeResult(key, NetworkProbeStatus.HttpStatus, status, elapsed, message);
            }
        }

        return new NetworkProbeResult(key, NetworkProbeStatus.Failed, null, elapsed, message);
    }
}
