namespace PdxLauncher.Checks;

using System.Net;
using System.Net.Http;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 连接池热替换自检：<see cref="HttpFetcher"/> 换代理时必须「换池不换对象」。
/// <para>
/// 起因：设置页改代理后工坊报 <c>ObjectDisposedException: Cannot access a disposed object.
/// Object name: 'System.Net.Http.HttpClient'</c>。根因是重建时把整套服务对象换掉并 Dispose 了旧连接池，
/// 而页面构造函数里缓存的客户端仍指向旧对象。修法是把连接池做成可替换的：
/// <see cref="HttpFetcher.UseProxy"/> 只换底层 <see cref="System.Net.Http.HttpClient"/>，对象身份不变、
/// 旧池进退休队列延迟释放。
/// </para>
/// <para>
/// 用例全部走白名单校验失败路径（<c>host-not-allowed</c>），因此不会真的联网。
/// </para>
/// </summary>
public static class HttpFetcherChecks
{
    private static readonly IAppLogger Logger = new NullLogger();

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckIdentityStableAcrossSwaps();
        CheckStillEnforcesWhitelist();
        CheckSwapsWithProxyObjects();
        CheckDisposeIsIdempotent();
        CheckTimeoutExceptionContract();
        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 连接超时的异常契约。
    /// <para>
    /// .NET 连不上时抛的是 <see cref="OperationCanceledException"/>（内含 TimeoutException），
    /// 不是 <see cref="HttpRequestException"/>。只捕 <see cref="HttpRequestException"/> 的调用点
    /// 会把「连不上」当成「用户取消了操作」——创意工坊页就因此永远停在「正在加载…」且列表空白。
    /// 所以 <see cref="HttpFetcher"/> 把超时翻译成 <see cref="NetworkTimeoutException"/>，
    /// 而它必须仍然是 <see cref="HttpRequestException"/>，既有调用点才不用改也能接住。
    /// </para>
    /// </summary>
    private static void CheckTimeoutExceptionContract()
    {
        var inner = new TimeoutException("connect timeout");
        var ex = new NetworkTimeoutException("steamcommunity.com", "连接超时（30 秒）：steamcommunity.com", inner);

        ChecksContext.Check(
            "HttpFetcher.CheckTimeoutExceptionContract：超时也是 HttpRequestException",
            ex is HttpRequestException);
        ChecksContext.Equal("HttpFetcher.CheckTimeoutExceptionContract：保留目标主机", "steamcommunity.com", ex.Host);
        ChecksContext.Check(
            "HttpFetcher.CheckTimeoutExceptionContract：保留原始异常",
            ReferenceEquals(inner, ex.InnerException));
        ChecksContext.Check(
            "HttpFetcher.CheckTimeoutExceptionContract：消息带上主机名",
            ex.Message.Contains("steamcommunity.com", StringComparison.Ordinal));

        // 取超时必须翻译成 NetworkTimeoutException：源码里必须有「没被取消却收到取消」的分支。
        var source = ReadSource("src", "PdxLauncher.Core", "Online", "HttpFetcher.cs");
        if (source is null)
        {
            return;
        }

        ChecksContext.Check(
            "HttpFetcher.CheckTimeoutExceptionContract：超时被翻译成 NetworkTimeoutException",
            source.Contains("NetworkTimeoutException", StringComparison.Ordinal));
        ChecksContext.Check(
            "HttpFetcher.CheckTimeoutExceptionContract：只把「没被取消的取消」当成超时",
            source.Contains("when (!cancellationToken.IsCancellationRequested)", StringComparison.Ordinal));
    }

    /// <summary>读仓库里的一个源码文件；找不到返回 null（自检不因此失败）。</summary>
    private static string? ReadSource(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                var path = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }

            dir = dir.Parent;
        }

        return null;
    }

    /// <summary>换池前后是同一个对象（页面里缓存的引用继续有效）。</summary>
    private static void CheckIdentityStableAcrossSwaps()
    {
        using var fetcher = new HttpFetcher(Logger);
        var identity = fetcher;

        fetcher.UseProxy(null);
        fetcher.UseProxy(new WebProxy("http://127.0.0.1:7890"));
        fetcher.UseProxy(null);

        ChecksContext.Check(
            "HttpFetcher.CheckIdentityStableAcrossSwaps：换池后对象身份不变",
            ReferenceEquals(identity, fetcher));
    }

    /// <summary>换池后白名单仍然是唯一出口的硬约束（也证明新 client 已经装好、能走到请求前置校验）。</summary>
    private static void CheckStillEnforcesWhitelist()
    {
        using var fetcher = new HttpFetcher(Logger);
        fetcher.UseProxy(new WebProxy("http://127.0.0.1:7890"));

        var blocked = RunBlocked(fetcher, "http://evil.example.com/x");

        ChecksContext.Check(
            "HttpFetcher.CheckStillEnforcesWhitelist：换池后仍拒绝白名单外域名",
            blocked is not null && blocked.StartsWith("host-not-allowed:", StringComparison.Ordinal),
            blocked ?? "(没有抛异常)");
    }

    /// <summary>连续切换（含代理对象来回切换）不抛异常，也不影响下一次请求。</summary>
    private static void CheckSwapsWithProxyObjects()
    {
        using var fetcher = new HttpFetcher(Logger);
        var failures = 0;

        try
        {
            for (var i = 0; i < 5; i++)
            {
                fetcher.UseProxy(i % 2 == 0 ? null : new WebProxy("http://10.0.0.1:8080"));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            failures++;
            ChecksContext.Check("HttpFetcher.CheckSwapsWithProxyObjects：切换不应抛异常", false, ex.Message);
        }

        if (failures == 0)
        {
            var blocked = RunBlocked(fetcher, "http://evil.example.com/y");
            ChecksContext.Check(
                "HttpFetcher.CheckSwapsWithProxyObjects：反复切换后请求仍正常走到白名单校验",
                blocked is not null && blocked.StartsWith("host-not-allowed:", StringComparison.Ordinal),
                blocked ?? "(没有抛异常)");
        }
    }

    /// <summary>Dispose 幂等：退出路径可能被调用两次。</summary>
    private static void CheckDisposeIsIdempotent()
    {
        var fetcher = new HttpFetcher(Logger);
        fetcher.UseProxy(null);
        fetcher.UseProxy(null);

        var threw = false;
        try
        {
            fetcher.Dispose();
            fetcher.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            threw = true;
            ChecksContext.Check("HttpFetcher.CheckDisposeIsIdempotent：重复 Dispose 不应抛异常", false, ex.Message);
        }

        if (!threw)
        {
            ChecksContext.Check("HttpFetcher.CheckDisposeIsIdempotent：重复 Dispose 不抛异常", true);
        }
    }

    /// <summary>跑一次必然被白名单挡住的请求，返回异常消息（异常类型不对则返回 null）。</summary>
    private static string? RunBlocked(HttpFetcher fetcher, string url)
    {
        try
        {
            var stream = fetcher.GetAsync(new Uri(url), CancellationToken.None).GetAwaiter().GetResult();
            stream.Dispose();
            return null;
        }
        catch (HttpRequestException ex)
        {
            return ex.Message;
        }
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
