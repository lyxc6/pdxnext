namespace PdxLauncher.Checks;

using System.Net.Http;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;

/// <summary>
/// <see cref="PdxModsClient"/> 自检（不真联网）：请求体形状、响应解析、字段回退、
/// 状态码映射、os/arch 必填、鉴权头三种形态。全部通过 fake <see cref="IPdxHttpTransport"/> 注入。
/// <para>
/// 报文样例取自 2026-10-27 本机对 <c>api.paradox-interactive.com/mods</c> 的真实响应，
/// 只保留断言用到的字段 —— 字段改名（例如列表里叫 <c>installedCount</c>）时这里会先炸。
/// </para>
/// </summary>
public static class ModsApiChecks
{
    private const string GameId = "cities_skylines_2";

    /// <summary>一份最小可用的搜索响应（字段名与真实报文一致）。</summary>
    private const string SearchBody = """
    {
      "totalCount": 27364,
      "mods": [
        {
          "modId": "163638",
          "displayName": "Placemaker",
          "author": "ockidj",
          "shortDescription": "Your plot's edges connect to each other.",
          "requiredVersion": "1.6.*",
          "userModVersion": "0.3.6",
          "modVersion": "10",
          "rating": 5,
          "ratingsTotal": 1194,
          "installedCount": 46600,
          "updated": "2026-10-27T00:55:39.000Z",
          "creationDate": "2024-04-01T10:00:00.000Z",
          "displayImagePath": "https://modscontent.paradox-interactive.com/cities_skylines_2/e2e8c331/content/cover.jpg",
          "tags": ["Code Mod"]
        },
        {
          "name": "ef1b8d5e-92c1-45db-8c85-51f778a9aaaa",
          "displayName": "",
          "modId": "161156",
          "author": "Torbi",
          "subscriptions": 7,
          "latestVersion": "14"
        }
      ]
    }
    """;

    private const string DetailBody = """
    {
      "result": "ok",
      "modDetail": {
        "modId": "163638",
        "displayName": "Placemaker",
        "author": "ockidj",
        "longDescription": "Long text.",
        "changelog": "0.3.6 fixes.",
        "requiredVersion": "1.6.*",
        "subscriptions": 46632,
        "enabled": true,
        "forumLink": "https://forum.paradoxplaza.com/threads/1/",
        "screenshots": [
          { "image": "https://modscontent.paradox-interactive.com/a/screenshot_01.png", "thumbnail": "https://x/t.png" },
          { "image": "" }
        ],
        "externalLinks": [
          { "url": "https://github.com/jackwrichards/placemaker", "type": "github", "displayName": null },
          { "url": "", "type": "youtube", "displayName": "no url" }
        ],
        "metadata": { "size_in_memory": "36977023" }
      }
    }
    """;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckSearchRequestShape();
        CheckSearchParsing();
        CheckDetailRequestShape();
        CheckDetailParsing();
        CheckStatusMapping();
        CheckBadJsonAndNetwork();
        CheckSessionHeader();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- 搜索请求

    private static void CheckSearchRequestShape()
    {
        var transport = new FakeTransport(200, SearchBody);
        var client = new PdxModsClient(transport, new NullLogger());
        var query = new PdxModsQuery("traffic", PdxModsSort.Popularity, Descending: false, Page: 3, PageSize: 50);

        var result = client.SearchAsync(GameId, query, session: null, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check("搜索: 成功", result.IsSuccess);
        ChecksContext.Equal("搜索: 只发一次", 1, transport.Calls.Count);

        var call = transport.Calls[0];
        ChecksContext.Equal("搜索: 方法是 POST", "POST", call.Method);
        ChecksContext.Equal("搜索: 端点路径", "/mods", call.Uri.AbsolutePath);
        ChecksContext.Equal("搜索: 无会话时不发鉴权头", null, call.AuthorizationJson);

        var body = Json.Parse(call.JsonBody);
        ChecksContext.Equal("搜索: gameNames 单元素", GameId, body.Path("gameNames[0]"));
        ChecksContext.Equal("搜索: sortBy 走白名单字符串", "popularity", body.Path("sortBy"));
        ChecksContext.Equal("搜索: Descending=false → asc", "asc", body.Path("orderBy"));
        ChecksContext.Equal("搜索: page 透传", "3", body.Path("page"));
        ChecksContext.Equal("搜索: pageSize 透传", "50", body.Path("pageSize"));
        ChecksContext.Equal("搜索: states 只要 published", "published", body.Path("states[0]"));
        ChecksContext.Equal("搜索: 关键词进 searchQuery", "traffic", body.Path("searchQuery"));

        // 空关键词不该发 searchQuery（官方启动器也不发；多发会影响排序权重）
        var blank = new FakeTransport(200, SearchBody);
        new PdxModsClient(blank, new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery("   "), null, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("搜索: 空白关键词不发 searchQuery", "null", Json.Parse(blank.Calls[0].JsonBody).Path("searchQuery"));

        // pageSize 防御性 clamp
        var clamped = new FakeTransport(200, SearchBody);
        new PdxModsClient(clamped, new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null, PageSize: 9999), null, CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal(
            "搜索: pageSize 超上限被 clamp 到 200",
            PdxModsClient.MaxPageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Json.Parse(clamped.Calls[0].JsonBody).Path("pageSize"));
    }

    private static void CheckSearchParsing()
    {
        var transport = new FakeTransport(200, SearchBody);
        var page = new PdxModsClient(transport, new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None)
            .GetAwaiter().GetResult().Value;

        ChecksContext.Check("解析: 有结果", page is not null);
        if (page is null)
        {
            return;
        }

        ChecksContext.Equal("解析: 总数", 27364, page.TotalCount);
        ChecksContext.Equal("解析: 条目数", 2, page.Items.Count);
        ChecksContext.Check("解析: HasMore（0 页 * 24 < 27364）", page.HasMore);

        var first = page.Items[0];
        ChecksContext.Equal("解析: modId", "163638", first.ModId);
        ChecksContext.Equal("解析: displayName", "Placemaker", first.DisplayName);
        ChecksContext.Equal("解析: author", "ockidj", first.Author);
        ChecksContext.Equal("解析: requiredVersion → GameVersion", "1.6.*", first.GameVersion);
        ChecksContext.Equal("解析: 评分数", 1194, first.RatingsTotal);
        ChecksContext.Equal("解析: installedCount → Subscriptions", 46600L, first.Subscriptions);
        ChecksContext.Equal("解析: modVersion → Version", "10", first.Version);
        ChecksContext.Equal("解析: 标签", "Code Mod", first.Tags.FirstOrDefault());
        ChecksContext.Check("解析: 封面是完整 URL", first.CoverUrl?.StartsWith("https://modscontent", StringComparison.Ordinal) == true);
        ChecksContext.Check("解析: UpdatedOn 有值", first.UpdatedOn is not null);
        ChecksContext.Check("解析: CreatedOn 有值", first.CreatedOn is not null);

        // 第二条：displayName 空 → 回落 name；只有 subscriptions 没有 installedCount
        var second = page.Items[1];
        ChecksContext.Equal("解析: displayName 空时回落 name", "ef1b8d5e-92c1-45db-8c85-51f778a9aaaa", second.DisplayName);
        ChecksContext.Equal("解析: 只有 subscriptions 时也读得到", 7L, second.Subscriptions);
        ChecksContext.Equal("解析: latestVersion → Version", "14", second.Version);
        ChecksContext.Equal("解析: 缺 CoverUrl → null", null, second.CoverUrl);
        ChecksContext.Equal("解析: 缺字段不抛，取默认 0", 0, second.RatingsTotal);
    }

    // ---------------------------------------------------------------- 详情请求

    private static void CheckDetailRequestShape()
    {
        var transport = new FakeTransport(200, DetailBody);
        new PdxModsClient(transport, new NullLogger())
            .GetDetailAsync("163638", null, CancellationToken.None)
            .GetAwaiter().GetResult();

        var call = transport.Calls[0];
        var query = call.Uri.Query;
        ChecksContext.Equal("详情: 方法 GET", "GET", call.Method);
        ChecksContext.Equal("详情: 路径 /mods", "/mods", call.Uri.AbsolutePath);
        ChecksContext.Check("详情: 带 modId", query.Contains("modId=163638", StringComparison.Ordinal), query);
        ChecksContext.Check("详情: os=windows 必填", query.Contains("os=windows", StringComparison.Ordinal), query);
        ChecksContext.Check("详情: arch=x64 必填", query.Contains("arch=x64", StringComparison.Ordinal), query);
        ChecksContext.Equal("详情: 不带正文", null, call.JsonBody);
    }

    private static void CheckDetailParsing()
    {
        var detail = new PdxModsClient(new FakeTransport(200, DetailBody), new NullLogger())
            .GetDetailAsync("163638", null, CancellationToken.None)
            .GetAwaiter().GetResult().Value;

        ChecksContext.Check("详情解析: 有结果", detail is not null);
        if (detail is null)
        {
            return;
        }

        ChecksContext.Equal("详情解析: 长描述", "Long text.", detail.LongDescription);
        ChecksContext.Equal("详情解析: 更新日志", "0.3.6 fixes.", detail.Changelog);
        ChecksContext.Equal("详情解析: subscriptions 也算订阅数", 46632L, detail.Summary.Subscriptions);
        ChecksContext.Equal("详情解析: enabled=true → IsInstalled", true, detail.IsInstalled);
        ChecksContext.Equal("详情解析: forumLink", "https://forum.paradoxplaza.com/threads/1/", detail.ForumUrl);
        ChecksContext.Equal("详情解析: 截图只留非空 image", 1, detail.ScreenshotUrls.Count);
        ChecksContext.Equal("详情解析: 外链只留非空 url", 1, detail.ExternalLinks.Count);
        ChecksContext.Equal("详情解析: 外链 type", "github", detail.ExternalLinks[0].Type);
        ChecksContext.Equal("详情解析: size_in_memory 转 long", 36977023L, detail.SizeBytes);
    }

    // ---------------------------------------------------------------- 失败路径

    private static void CheckStatusMapping()
    {
        // 404 → NotFound（详情有专用文案）
        var notFound = new PdxModsClient(new FakeTransport(404, "{}"), new NullLogger())
            .GetDetailAsync("1", null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("映射: 404 → NotFound", ErrorKind.NotFound, notFound.Kind);
        ChecksContext.Equal("映射: 404 详情文案", "pdx-mods-detail:not-found", notFound.ErrorMessage);
        ChecksContext.Equal("映射: 失败时 Value 为 null", null, notFound.Value);

        // 401/403 → Unauthorized
        foreach (var status in new[] { 401, 403 })
        {
            var denied = new PdxModsClient(new FakeTransport(status, "{}"), new NullLogger())
                .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("映射: " + status + " → Unauthorized", ErrorKind.Unauthorized, denied.Kind);
        }

        // 400 → Invalid（os/arch 缺失就是这条）
        var bad = new PdxModsClient(new FakeTransport(400, "{\"message\":\"Mod is corrupted\"}"), new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("映射: 400 → Invalid", ErrorKind.Invalid, bad.Kind);

        // 500 → Io
        var boom = new PdxModsClient(new FakeTransport(500, ""), new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("映射: 500 → Io", ErrorKind.Io, boom.Kind);
    }

    private static void CheckBadJsonAndNetwork()
    {
        var garbled = new PdxModsClient(new FakeTransport(200, "not json at all"), new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("坏 JSON: 不抛，返回 Io", ErrorKind.Io, garbled.Kind);
        ChecksContext.Equal("坏 JSON: 错误码", "pdx-mods-search:bad-json", garbled.ErrorMessage);

        // 顶层不是对象 / 没有 modDetail
        var noDetail = new PdxModsClient(new FakeTransport(200, "{\"result\":\"ok\"}"), new NullLogger())
            .GetDetailAsync("1", null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("缺 modDetail: NotFound", ErrorKind.NotFound, noDetail.Kind);

        // 网络故障（HttpRequestException）→ 状态码折 0 → Io，且不冒泡
        var down = new PdxModsClient(new FakeTransport(new HttpRequestException("dns")), new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("网络故障: 折成 Io 不抛", ErrorKind.Io, down.Kind);
    }

    private static void CheckSessionHeader()
    {
        var session = new PdxSession("TOKEN-123", null, "user-1", "a@b.c", "Tester", DateTimeOffset.UtcNow);
        var transport = new FakeTransport(200, SearchBody);
        new PdxModsClient(transport, new NullLogger())
            .SearchAsync(GameId, new PdxModsQuery(null), session, CancellationToken.None)
            .GetAwaiter().GetResult();

        var header = transport.Calls[0].AuthorizationJson;
        ChecksContext.Check("鉴权: 头不是 Bearer", header?.StartsWith("Bearer", StringComparison.OrdinalIgnoreCase) != true, header);
        var json = Json.Parse(header ?? "{}");
        ChecksContext.Equal("鉴权: token 在 session.token 里", "TOKEN-123", json.Path("session.token"));

        ChecksContext.Check("鉴权: IsSignedIn(null) = false", !PdxModsClient.IsSignedIn(null));
        ChecksContext.Check("鉴权: IsSignedIn(空 token) = false", !PdxModsClient.IsSignedIn(new PdxSession("  ", null, "u", "a@b.c", "T", DateTimeOffset.UtcNow)));
        ChecksContext.Check("鉴权: IsSignedIn(有 token) = true", PdxModsClient.IsSignedIn(session));
    }

    // ---------------------------------------------------------------- 假件

    /// <summary>记录调用并按预设回应的传输。</summary>
    private sealed class FakeTransport : IPdxHttpTransport
    {
        private readonly int _status;
        private readonly string _body;
        private readonly Exception? _throw;

        public FakeTransport(int status, string body)
        {
            _status = status;
            _body = body;
        }

        public FakeTransport(Exception toThrow)
        {
            _throw = toThrow;
            _body = string.Empty;
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
            if (_throw is not null)
            {
                throw _throw;
            }

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

/// <summary>极简 JSON 读取器：只为断言路径，不引第三方库。</summary>
internal static class Json
{
    public static JsonNode? Parse(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text!);

    /// <summary>取路径（形如 <c>a.b[0].c</c>）；缺失返回字符串 "null" 便于 Equal 比较。</summary>
    public static string Path(string? text, string path) => ReadPath(Parse(text ?? "{}"), path);

    /// <summary>取路径（已经解析好的节点）。</summary>
    public static string Read(JsonNode? node, string path) => ReadPath(node, path);

    /// <summary>取路径，便于链式调用（<c>Json.Parse(x).Path("a.b")</c>）。</summary>
    public static string Path(this JsonNode? node, string path) => ReadPath(node, path);

    private static string ReadPath(JsonNode? node, string path)
    {
        foreach (var rawSegment in path.Split('.'))
        {
            var segment = rawSegment;
            var index = -1;
            var bracket = segment.IndexOf('[', StringComparison.Ordinal);
            if (bracket >= 0)
            {
                index = int.Parse(
                    segment[(bracket + 1)..^1],
                    System.Globalization.CultureInfo.InvariantCulture);
                segment = segment[..bracket];
            }

            if (node is not JsonObject obj)
            {
                return "null";
            }

            node = obj[segment];

            if (index >= 0)
            {
                node = node is JsonArray array && index < array.Count ? array[index] : null;
            }

            if (node is null)
            {
                return "null";
            }
        }

        return node is null ? "null" : node.ToJsonString().Trim('"');
    }
}
