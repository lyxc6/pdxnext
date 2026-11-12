namespace PdxLauncher.Checks;

using System.Net.Http;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 连通性自检的纯逻辑用例（不联网）：目标表本身是否自洽，以及
/// 「超时 / HTTP 状态码 / 白名单拦截 / 其它失败」四种异常是否被分到正确的结果类型。
/// </summary>
public static class NetworkProbeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        CheckTargets();
        CheckOk();
        CheckPostUsesFormBody();
        CheckTimeout();
        CheckHttpStatus();
        CheckHostNotAllowed();
        CheckOtherFailure();
        CheckCancellationPropagates();
        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 目标表

    private static void CheckTargets()
    {
        var targets = NetworkProbe.SteamTargets;
        ChecksContext.Equal("连通性自检: 目标数", 3, targets.Count);
        ChecksContext.Check(
            "连通性自检: 三台主机都在白名单内",
            targets.All(target => OnlineHosts.IsAllowed(target.Uri)));

        ChecksContext.Equal(
            "连通性自检: 第一台是 steamcommunity",
            OnlineHosts.SteamCommunityHost,
            targets[0].Uri.Host);
        ChecksContext.Equal(
            "连通性自检: 第二台是 api.steampowered",
            OnlineHosts.SteamApiHost,
            targets[1].Uri.Host);
        ChecksContext.Equal(
            "连通性自检: 第三台是 store.steampowered",
            OnlineHosts.SteamStoreHost,
            targets[2].Uri.Host);

        ChecksContext.Check(
            "连通性自检: 只有详情接口用 POST 表单",
            targets[0].PostFormBody is null
                && targets[1].PostFormBody is not null
                && targets[2].PostFormBody is null);
        ChecksContext.Equal(
            "连通性自检: key 不重复",
            targets.Count,
            targets.Select(target => target.Key).Distinct(StringComparer.Ordinal).Count());
    }

    // ------------------------------------------------------------ 结果分类

    private static void CheckOk()
    {
        var result = RunProbe(new StubHttpFetcher(() => Task.FromResult<Stream>(Stream.Null)));
        ChecksContext.Equal("连通性自检: 成功 → Ok", NetworkProbeStatus.Ok, result.Status);
        ChecksContext.Equal("连通性自检: 成功不带状态码", null, result.HttpStatus);
        ChecksContext.Check("连通性自检: 计时非负", result.Elapsed >= TimeSpan.Zero);
        ChecksContext.Check("连通性自检: IsOk", result.IsOk);
    }

    private static void CheckPostUsesFormBody()
    {
        var fetcher = new StubHttpFetcher(() => Task.FromResult<Stream>(Stream.Null));
        var target = NetworkProbe.SteamTargets.Single(item => item.PostFormBody is not null);
        var result = NetworkProbe.ExecuteAsync(fetcher, target).GetAwaiter().GetResult();

        ChecksContext.Equal("连通性自检: POST 目标走表单", 1, fetcher.PostFormCalls);
        ChecksContext.Equal("连通性自检: POST 目标不走 GET", 0, fetcher.GetCalls);
        ChecksContext.Equal("连通性自检: POST 表单体原样透传", target.PostFormBody, fetcher.LastFormBody);
        ChecksContext.Equal("连通性自检: POST 成功 → Ok", NetworkProbeStatus.Ok, result.Status);
    }

    private static void CheckTimeout()
    {
        var result = RunProbe(new StubHttpFetcher(
            () => throw new NetworkTimeoutException("steamcommunity.com", "连接超时（30 秒）：steamcommunity.com")));
        ChecksContext.Equal("连通性自检: 超时 → Timeout", NetworkProbeStatus.Timeout, result.Status);
        ChecksContext.Check("连通性自检: 超时保留细节", result.Detail?.Contains("连接超时", StringComparison.Ordinal) == true);
    }

    private static void CheckHttpStatus()
    {
        var result = RunProbe(new StubHttpFetcher(
            () => throw new HttpRequestException("HTTP 429 https://steamcommunity.com/workshop/browse/?appid=1158310")));
        ChecksContext.Equal("连通性自检: 429 → HttpStatus", NetworkProbeStatus.HttpStatus, result.Status);
        ChecksContext.Equal("连通性自检: 429 状态码", 429, result.HttpStatus ?? 0);
    }

    private static void CheckHostNotAllowed()
    {
        var result = RunProbe(new StubHttpFetcher(
            () => throw new HttpRequestException("host-not-allowed:evil.example")));
        ChecksContext.Equal(
            "连通性自检: 白名单外 → HostNotAllowed",
            NetworkProbeStatus.HostNotAllowed,
            result.Status);
    }

    private static void CheckOtherFailure()
    {
        var result = RunProbe(new StubHttpFetcher(
            () => throw new HttpRequestException("TLS 握手失败")));
        ChecksContext.Equal("连通性自检: 其它异常 → Failed", NetworkProbeStatus.Failed, result.Status);
    }

    private static void CheckCancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var fetcher = new StubHttpFetcher(() => Task.FromCanceled<Stream>(cancellation.Token));

        var threw = false;
        try
        {
            NetworkProbe.ExecuteAsync(fetcher, NetworkProbe.SteamTargets[0], cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        ChecksContext.Check("连通性自检: 用户取消原样抛", threw);
    }

    private static NetworkProbeResult RunProbe(StubHttpFetcher fetcher) =>
        NetworkProbe.ExecuteAsync(fetcher, NetworkProbe.SteamTargets[0]).GetAwaiter().GetResult();

    /// <summary>最小假出口：GET 走一个委托，POST 记录参数后返回空串。</summary>
    private sealed class StubHttpFetcher : IHttpFetcher
    {
        private readonly Func<Task<Stream>> _get;

        public StubHttpFetcher(Func<Task<Stream>> get) => _get = get;

        public int GetCalls { get; private set; }

        public int PostFormCalls { get; private set; }

        public string? LastFormBody { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCalls++;
            return _get();
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException("自检不使用 JSON POST");

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken)
        {
            PostFormCalls++;
            LastFormBody = formBody;
            return Task.FromResult("{}");
        }
    }
}
