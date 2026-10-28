namespace PdxLauncher.Checks;

using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;

/// <summary>
/// 创意工坊详情页「订阅」链路自检：PDX 订阅/退订接口的 URL、方法、请求头与请求体，
/// 以及 <see cref="WorkshopSubscriptionService"/> 的播放集解析与缓存复用。
/// <para>不联网：全部走假的传输实现。</para>
/// </summary>
public static class WorkshopSubscriptionChecks
{
    private static readonly PdxSession Session =
        new("TOKEN-1", null, "u-1", "a@b.c", "Tester", DateTimeOffset.UtcNow);

    /// <summary>跑全部断言。</summary>
    /// <param name="fs">文件系统（播放集缓存写临时目录）。</param>
    /// <returns>失败断言数。</returns>
    public static int Run(IFileSystem fs)
    {
        var before = ChecksContext.Failures;

        CheckSubscribe(fs);
        CheckUnsubscribe(fs);
        CheckPlaySets(fs);
        CheckSignInRequired(fs);

        return ChecksContext.Failures - before;
    }

    // ---------------------------------------------------------------- 订阅/退订

    private static void CheckSubscribe(IFileSystem fs)
    {
        var transport = new FakeTransport(200, "{\"ok\":true}");
        var client = new PdxModsClient(transport, new NullLogger());
        var result = client
            .SubscribeAsync(7, "74417", 37, Session, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("订阅: 调用成功", result.IsSuccess, result.ErrorMessage);
        ChecksContext.Equal("订阅: 只有一次请求", 1, transport.Calls.Count);

        var call = transport.Calls[0];
        ChecksContext.Equal("订阅: 方法 POST", "POST", call.Method);
        ChecksContext.Equal(
            "订阅: URL",
            "https://api.paradox-interactive.com/mods/installed",
            call.Uri.ToString());
        ChecksContext.Check(
            "订阅: 带 X-Accept-Version",
            call.Headers is not null && call.Headers.TryGetValue("X-Accept-Version", out var version) && version == "2");

        var body = JsonNode.Parse(call.JsonBody ?? "{}");
        ChecksContext.Equal("订阅: activationSetId 是数字", 7L, body?["activationSetId"]?.GetValue<long>());
        var mods = body?["mods"]?.AsArray();
        ChecksContext.Check("订阅: mods 有一项", mods is { Count: 1 });
        ChecksContext.Equal("订阅: mod id 是数字", 74417L, mods?[0]?["id"]?.GetValue<long>());
        ChecksContext.Equal("订阅: 带 version", 37, mods?[0]?["version"]?.GetValue<int>());
    }

    private static void CheckUnsubscribe(IFileSystem fs)
    {
        var transport = new FakeTransport(200, "{}");
        var client = new PdxModsClient(transport, new NullLogger());
        var result = client
            .UnsubscribeAsync(9, "74417", Session, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("退订: 调用成功", result.IsSuccess, result.ErrorMessage);
        var call = transport.Calls[0];
        ChecksContext.Equal("退订: 方法 DELETE", "DELETE", call.Method);
        ChecksContext.Equal(
            "退订: URL 带 activationSetId 与 modId",
            "https://api.paradox-interactive.com/mods/installed?activationSetId=9&modId=74417",
            call.Uri.ToString());
        ChecksContext.Check(
            "退订: 带 X-Accept-Version",
            call.Headers is not null && call.Headers.ContainsKey("X-Accept-Version"));
    }

    // ---------------------------------------------------------------- 播放集

    private static void CheckPlaySets(IFileSystem fs)
    {
        var listTransport = new FakeTransport(200, "{\"sets\":[{\"id\":11,\"name\":\"A\"},{\"id\":12,\"name\":\"B\"}]}");
        var listClient = new PdxModsClient(listTransport, new NullLogger());
        var list = listClient
            .GetPlaySetsAsync("cities_skylines_2", Session, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("播放集: 列表调用成功", list.IsSuccess, list.ErrorMessage);
        ChecksContext.Equal("播放集: 解析两条", 2, list.Value?.Count ?? -1);
        ChecksContext.Equal(
            "播放集: 列表 URL",
            "https://api.paradox-interactive.com/mods/sets/list/cities_skylines_2?includeShared=false",
            listTransport.Calls[0].Uri.ToString());

        var createTransport = new FakeTransport(200, "{\"set\":{\"id\":42}}");
        var createClient = new PdxModsClient(createTransport, new NullLogger());
        var created = createClient
            .CreatePlaySetAsync("cities_skylines_2", "PdxNext", Session, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("播放集: 新建成功", created.IsSuccess, created.ErrorMessage);
        ChecksContext.Equal("播放集: 新建拿到 id", 42L, created.Value?.Id ?? -1);
        ChecksContext.Equal(
            "播放集: 新建 URL",
            "https://api.paradox-interactive.com/mods/sets",
            createTransport.Calls[0].Uri.ToString());
        ChecksContext.Equal("播放集: 新建方法 POST", "POST", createTransport.Calls[0].Method);
        ChecksContext.Check(
            "播放集: 新建请求体带名字",
            (createTransport.Calls[0].JsonBody ?? string.Empty).Contains("PdxNext", StringComparison.Ordinal));
    }

    private static void CheckSignInRequired(IFileSystem fs)
    {
        var transport = new FakeTransport(200, "{}");
        var client = new PdxModsClient(transport, new NullLogger());
        var result = client
            .GetPlaySetsAsync("cities_skylines_2", null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("未登录: 播放集列表失败", !result.IsSuccess);
        ChecksContext.Equal("未登录: 错误码", "pdx-playsets:sign-in-required", result.ErrorMessage ?? string.Empty);
        ChecksContext.Equal("未登录: 不发请求", 0, transport.Calls.Count);
    }

    // ---------------------------------------------------------------- 假件

    private sealed class FakeTransport : IPdxHttpTransport
    {
        private readonly int _status;
        private readonly string _body;

        public FakeTransport(int status, string body)
        {
            _status = status;
            _body = body;
        }

        public List<Call> Calls { get; } = new();

        public Task<PdxHttpResponse> SendAsync(
            string method,
            Uri uri,
            string? authorizationJson,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Calls.Add(new Call(method, uri, authorizationJson, jsonBody, headers));
            return Task.FromResult(new PdxHttpResponse(_status, _body));
        }

        public sealed record Call(
            string Method,
            Uri Uri,
            string? AuthorizationJson,
            string? JsonBody,
            IReadOnlyDictionary<string, string>? Headers = null);
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
