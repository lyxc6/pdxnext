namespace PdxLauncher.Core.Mods;

using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// PDX Mods 平台的只读客户端（不依赖闭源 <c>PDX.SDK.dll</c>）。
/// <para>
/// 协议来自官方 Electron 启动器（app.asar 的 <c>modsApi</c> 逐字核对）与本机实测：
/// </para>
/// <list type="bullet">
/// <item>搜索：<c>POST /mods</c>，body <c>{gameNames:[gameId], orderBy, sortBy, pageSize, page, states:["published"]}</c>。</item>
/// <item>关键词：body 加 <c>searchQuery</c>（实测有效：Traffic → 994 条；官方启动器不发这个字段）。</item>
/// <item>详情：<c>GET /mods?modId=…&amp;os=windows&amp;arch=x64</c>，<b>os/arch 必填</b>。</item>
/// </list>
/// <para>
/// 这两条接口<b>匿名即可用</b>（实测 2026-10，无 Authorization 头）。带会话时额外能拿到
/// 「是否已订阅」；订阅/下载/播放集同步另有端点，需要登录，见 <see cref="IsSignedIn"/> 相关注释。
/// </para>
/// <para>
/// 失败一律返回 <see cref="AppResult{T}"/> + <see cref="ErrorKind"/>，不把英文错误串抛给界面。
/// </para>
/// </summary>
public sealed class PdxModsClient
{
    /// <summary>列表 / 搜索端点。</summary>
    public static Uri SearchEndpoint => OnlineHosts.ModsSearchEndpoint;

    /// <summary>详情端点（不含查询串）。</summary>
    public static Uri DetailEndpoint { get; } = new("https://api.paradox-interactive.com/mods");

    /// <summary>服务端 <c>sortBy</c> 白名单（实测 400 报文里逐字列出）。</summary>
    public static IReadOnlyList<string> AllowedSortValues { get; } =
        ["activationOrder", "created", "displayName", "popularity", "rating", "updated", "best"];

    /// <summary>单页上限。服务端实测 200 仍可用；界面不依赖这个上限，只做防御。</summary>
    public const int MaxPageSize = 200;

    private readonly IPdxHttpTransport _http;
    private readonly IAppLogger _logger;

    /// <summary>创建客户端。</summary>
    /// <param name="http">PDX 传输（与账号客户端共用）。</param>
    /// <param name="logger">日志。</param>
    public PdxModsClient(IPdxHttpTransport http, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger;
    }

    /// <summary>
    /// 有没有可用的登录会话。为 false 时仍可搜索/看详情，只是拿不到「已订阅」标记、
    /// 也不能订阅或下载。
    /// </summary>
    public static bool IsSignedIn(PdxLauncher.Core.Accounts.PdxSession? session) =>
        session is not null && !string.IsNullOrWhiteSpace(session.SessionToken);

    /// <summary>搜索 / 浏览 mod 列表。</summary>
    /// <param name="gameId">游戏 id（CS2 = <c>cities_skylines_2</c>）。</param>
    /// <param name="query">搜索条件。</param>
    /// <param name="session">可选的登录会话；只为拿「已订阅」信息，不影响能否搜索。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxModsPage>> SearchAsync(
        string gameId,
        PdxModsQuery query,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 0);

        var body = new JsonObject
        {
            ["gameNames"] = new JsonArray(gameId),
            ["orderBy"] = query.Descending ? "desc" : "asc",
            ["sortBy"] = PdxModsQuery.SortToString(query.Sort),
            ["pageSize"] = pageSize,
            ["page"] = page,
            ["states"] = new JsonArray("published"),
        };

        var keyword = query.SearchQuery?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            body["searchQuery"] = keyword;
        }

        var response = await SendAsync("POST", SearchEndpoint, body.ToJsonString(), session, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            return AppResult<PdxModsPage>.Fail(kind, "pdx-mods-search:" + kind.ToString().ToLowerInvariant());
        }

        try
        {
            var root = JsonNode.Parse(response.Body) as JsonObject;
            if (root is null)
            {
                return AppResult<PdxModsPage>.Fail(ErrorKind.Io, "pdx-mods-search:bad-json");
            }

            var total = GetInt(root, "totalCount");
            var items = new List<PdxModsSummary>();
            if (root["mods"] is JsonArray array)
            {
                foreach (var entry in array)
                {
                    if (entry is JsonObject item)
                    {
                        items.Add(ParseSummary(item));
                    }
                }
            }

            return AppResult<PdxModsPage>.Ok(new PdxModsPage(total, page, pageSize, items));
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX Mods 搜索响应解析失败: " + ex.Message);
            return AppResult<PdxModsPage>.Fail(ErrorKind.Io, "pdx-mods-search:bad-json");
        }
    }

    /// <summary>取单个 mod 的详情。</summary>
    /// <param name="modId">平台数字 id。</param>
    /// <param name="session">可选登录会话（用于「已订阅」标记）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxModsDetail>> GetDetailAsync(
        string modId,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        // os/arch 必填，缺任一个服务端都回 400（"Mod is corrupted or chosen operating system is not supported"）。
        var uri = new Uri(
            DetailEndpoint.Scheme + "://" + DetailEndpoint.Authority + DetailEndpoint.AbsolutePath +
            "?modId=" + Uri.EscapeDataString(modId) + "&os=windows&arch=x64");

        var response = await SendAsync("GET", uri, jsonBody: null, session, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == 404)
        {
            return AppResult<PdxModsDetail>.Fail(ErrorKind.NotFound, "pdx-mods-detail:not-found");
        }

        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            return AppResult<PdxModsDetail>.Fail(kind, "pdx-mods-detail:" + kind.ToString().ToLowerInvariant());
        }

        try
        {
            var root = JsonNode.Parse(response.Body) as JsonObject;
            if (root?["modDetail"] is not JsonObject detail)
            {
                return AppResult<PdxModsDetail>.Fail(ErrorKind.NotFound, "pdx-mods-detail:not-found");
            }

            var summary = ParseSummary(detail);
            var screenshots = new List<string>();

            // 与 ScreenshotUrls 一一对应：没有缩略图的位置塞空串，保证索引能对上。
            var screenshotThumbnails = new List<string>();
            if (detail["screenshots"] is JsonArray shots)
            {
                foreach (var shot in shots)
                {
                    var shotObject = shot as JsonObject;
                    var url = shotObject is null ? string.Empty : GetString(shotObject, "image");
                    if (url.Length > 0)
                    {
                        screenshots.Add(url);
                        screenshotThumbnails.Add(shotObject is null ? string.Empty : GetString(shotObject, "thumbnail"));
                    }
                }
            }

            var links = new List<PdxModsLink>();

            // 与 ExternalLinks 一一对应；displayName 经常为 null，界面要靠类型起名。
            var linkTypes = new List<string>();
            if (detail["externalLinks"] is JsonArray external)
            {
                foreach (var link in external)
                {
                    if (link is not JsonObject linkObject)
                    {
                        continue;
                    }

                    var url = GetString(linkObject, "url");
                    if (url.Length > 0)
                    {
                        var type = GetString(linkObject, "type");
                        links.Add(new PdxModsLink(
                            url,
                            type,
                            GetString(linkObject, "displayName")));
                        linkTypes.Add(type);
                    }
                }
            }

            long? size = null;
            if (detail["metadata"] is JsonObject metadata)
            {
                var rawSize = GetString(metadata, "size_in_memory");
                if (long.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    size = parsed;
                }
            }

            var installed = detail["enabled"] is JsonValue enabledValue
                && enabledValue.TryGetValue<bool>(out var enabled) && enabled;

            return AppResult<PdxModsDetail>.Ok(new PdxModsDetail(
                summary,
                LongDescription: GetString(detail, "longDescription"),
                Changelog: GetString(detail, "changelog"),
                ScreenshotUrls: screenshots,
                ExternalLinks: links,
                ForumUrl: NullIfEmpty(GetString(detail, "forumLink")),
                SizeBytes: size,
                IsInstalled: installed,
                ScreenshotThumbnailUrls: screenshotThumbnails,
                ExternalLinkTypes: linkTypes));
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX Mods 详情响应解析失败: " + ex.Message);
            return AppResult<PdxModsDetail>.Fail(ErrorKind.Io, "pdx-mods-detail:bad-json");
        }
    }

    /// <summary>发请求；网络层故障统一折算成 0（调用方据此给 <see cref="ErrorKind.Io"/>）。</summary>
    private async Task<PdxHttpResponse> SendAsync(
        string method,
        Uri uri,
        string? jsonBody,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        var authorization = session is null || string.IsNullOrWhiteSpace(session.SessionToken)
            ? null
            : new JsonObject { ["session"] = new JsonObject { ["token"] = session.SessionToken } }.ToJsonString();

        try
        {
            return await _http.SendAsync(method, uri, authorization, jsonBody, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("PDX Mods 请求失败（网络）: " + method + " " + uri.AbsolutePath + " — " + ex.Message);
            return new PdxHttpResponse(0, string.Empty);
        }
    }

    private static ErrorKind MapFailure(int statusCode) => statusCode switch
    {
        0 => ErrorKind.Io,
        401 or 403 => ErrorKind.Unauthorized,
        404 => ErrorKind.NotFound,
        400 => ErrorKind.Invalid,
        _ => ErrorKind.Io,
    };

    private static PdxModsSummary ParseSummary(JsonObject item) => new(
        ModId: GetString(item, "modId"),
        DisplayName: FirstNonEmpty(GetString(item, "displayName"), GetString(item, "name")),
        Author: GetString(item, "author"),
        ShortDescription: GetString(item, "shortDescription"),
        GameVersion: GetString(item, "requiredVersion"),
        UserModVersion: GetString(item, "userModVersion"),
        Version: FirstNonEmpty(GetString(item, "latestVersion"), GetString(item, "modVersion")),
        Rating: GetDouble(item, "rating"),
        RatingsTotal: GetInt(item, "ratingsTotal"),
        // 服务端实际字段是 installedCount（订阅/安装数），报文里没有 subscriptions 这个键；
        // 早期实现只读 subscriptions，导致列表里「订阅」恒为 0。两个都试，兼容假想的旧报文。
        Subscriptions: FirstNonZeroLong(item, "installedCount", "subscriptions"),
        UpdatedOn: GetDate(item, "latestUpdate") ?? GetDate(item, "updated"),
        CreatedOn: GetDate(item, "creationDate"),
        CoverUrl: NullIfEmpty(GetString(item, "displayImagePath")),
        Tags: GetStrings(item, "tags"));

    private static string GetString(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text ?? string.Empty : string.Empty;

    private static int GetInt(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static long GetLong(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<long>(out var number) ? number : 0;

    /// <summary>按顺序取第一个非零的长整数（字段名发生过变化时用）。</summary>
    private static long FirstNonZeroLong(JsonObject node, params string[] keys)
    {
        foreach (var key in keys)
        {
            var number = GetLong(node, key);
            if (number > 0)
            {
                return number;
            }
        }

        return 0;
    }

    private static double GetDouble(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : 0;

    private static DateTimeOffset? GetDate(JsonObject node, string key)
    {
        var raw = GetString(node, key);
        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static List<string> GetStrings(JsonObject node, string key)
    {
        if (node[key] is not JsonArray array)
        {
            return [];
        }

        var result = new List<string>(array.Count);
        foreach (var entry in array)
        {
            if (entry is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text))
            {
                result.Add(text);
            }
        }

        return result;
    }

    private static string FirstNonEmpty(string first, string second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
