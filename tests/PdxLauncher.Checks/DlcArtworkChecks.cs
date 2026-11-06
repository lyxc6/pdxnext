namespace PdxLauncher.Checks;

using System.Text;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// DLC 头图服务的自检：磁盘 .dlc 不带图，未拥有的 DLC 也没有本地素材，所以头图完全来自
/// Steam 商店的 header_image。这里用 fake IHttpFetcher + 真实临时目录钉住四件事：
/// 解析出带 hash 的 URL、下载一次后命中不联网、失败退避不反复打接口、白名单外的 URL 不下载。
/// </summary>
public static class DlcArtworkChecks
{
    private static readonly byte[] JpegBytes =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
        0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9,
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-dlcart-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckDownloadAndHit(fs, tempRoot);
            CheckFailureBackoff(fs, tempRoot);
            CheckWhitelist(fs, tempRoot);
            CheckUnavailable(fs, tempRoot);
            CheckCancellation(fs, tempRoot);
        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(tempRoot))
                {
                    fs.Delete(tempRoot);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 下载与命中

    private static void CheckDownloadAndHit(IFileSystem fs, string tempRoot)
    {
        var cache = Path.Combine(tempRoot, "hit");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        var http = new FakeHttpFetcher(uri =>
        {
            var text = uri.AbsoluteUri;
            if (text.Contains("appdetails", StringComparison.Ordinal))
            {
                return Json(
                    "{\"4232900\":{\"success\":true,\"data\":{\"header_image\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/4232900/abc/header.jpg?t=1\"}}}");
            }

            return JpegBytes;
        });
        var service = new DlcArtworkService(fs, http, clock, new NullLogger(), cache);

        var first = service.GetArtworkAsync(["4232900"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("DLC 头图: 解析出路径", first.ContainsKey("4232900"), string.Join(",", first.Keys));
        if (first.TryGetValue("4232900", out var path))
        {
            ChecksContext.Check("DLC 头图: 落盘且字节一致", fs.FileExists(path) && fs.ReadAllBytes(path).SequenceEqual(JpegBytes), path);
        }

        ChecksContext.Check("DLC 头图: 两次请求（接口 + 图片）", http.GetCallCount == 2, http.GetCallCount.ToString());

        var second = service.GetArtworkAsync(["4232900"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("DLC 头图: 二次命中不再联网", http.GetCallCount == 2 && second.ContainsKey("4232900"), http.GetCallCount.ToString());
    }

    // ------------------------------------------------------------ 失败退避

    private static void CheckFailureBackoff(IFileSystem fs, string tempRoot)
    {
        var cache = Path.Combine(tempRoot, "backoff");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        var http = new FakeHttpFetcher(uri =>
        {
            if (uri.AbsoluteUri.Contains("appdetails", StringComparison.Ordinal))
            {
                return Json(
                    "{\"111\":{\"success\":true,\"data\":{\"header_image\":\"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/111/x/header.jpg\"}}}");
            }

            throw new System.Net.Http.HttpRequestException("404");
        });
        var service = new DlcArtworkService(fs, http, clock, new NullLogger(), cache);

        var first = service.GetArtworkAsync(["111"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("DLC 头图: 下载失败不返回路径", first.Count == 0, first.Count.ToString());
        var callsAfterFailure = http.GetCallCount;

        var second = service.GetArtworkAsync(["111"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "DLC 头图: 退避期内不重试",
            http.GetCallCount == callsAfterFailure && second.Count == 0,
            http.GetCallCount + " vs " + callsAfterFailure);

        clock.Advance(TimeSpan.FromMinutes(31));
        service.GetArtworkAsync(["111"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "DLC 头图: 退避过期后重试",
            http.GetCallCount > callsAfterFailure,
            http.GetCallCount + " vs " + callsAfterFailure);

        ChecksContext.Check(
            "DLC 头图: 失败不落空文件",
            !fs.FileExists(Path.Combine(cache, "111.jpg")),
            "不应存在 111.jpg");
    }

    // ------------------------------------------------------------ 白名单

    private static void CheckWhitelist(IFileSystem fs, string tempRoot)
    {
        var cache = Path.Combine(tempRoot, "allow");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        var http = new FakeHttpFetcher(uri =>
        {
            if (uri.AbsoluteUri.Contains("appdetails", StringComparison.Ordinal))
            {
                return Json(
                    "{\"222\":{\"success\":true,\"data\":{\"header_image\":\"https://evil.example.com/store/apps/222/header.jpg\"}}}");
            }

            return JpegBytes;
        });
        var service = new DlcArtworkService(fs, http, clock, new NullLogger(), cache);

        var result = service.GetArtworkAsync(["222"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("DLC 头图: 白名单外的 URL 不下载", result.Count == 0, result.Count.ToString());
        ChecksContext.Check("DLC 头图: 白名单外不发图片请求", http.GetCallCount == 1, http.GetCallCount.ToString());
    }

    // ------------------------------------------------------------ 接口无图

    private static void CheckUnavailable(IFileSystem fs, string tempRoot)
    {
        var cache = Path.Combine(tempRoot, "missing");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        var http = new FakeHttpFetcher(_ => Json("{\"333\":{\"success\":false}}"));
        var service = new DlcArtworkService(fs, http, clock, new NullLogger(), cache);

        var result = service.GetArtworkAsync(["333"], CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("DLC 头图: success=false 不抛且无路径", result.Count == 0, result.Count.ToString());
    }

    // ------------------------------------------------------------ 取消

    private static void CheckCancellation(IFileSystem fs, string tempRoot)
    {
        var cache = Path.Combine(tempRoot, "cancel");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-11-06T00:00:00Z"));
        var http = new FakeHttpFetcher(_ => Json("{}"));
        var service = new DlcArtworkService(fs, http, clock, new NullLogger(), cache);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var threw = false;
        try
        {
            service.GetArtworkAsync(["444"], cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        ChecksContext.Check("DLC 头图: 取消原样抛出", threw, threw ? "OCE" : "未抛");
    }

    // ------------------------------------------------------------ 辅助

    private static Stream Json(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private sealed class FakeHttpFetcher : IHttpFetcher
    {
        private readonly Func<Uri, object> _handler;

        public FakeHttpFetcher(Func<Uri, object> handler) => _handler = handler;

        public int GetCallCount { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCallCount++;
            var value = _handler(uri);
            return Task.FromResult(value as Stream ?? new MemoryStream((byte[])value));
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult("{}");

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken) =>
            Task.FromResult("{}");
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow) => UtcNow = utcNow;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
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
