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

    /// <summary>播放集（activation set）端点前缀。</summary>
    public static Uri SetsEndpoint { get; } = new("https://api.paradox-interactive.com/mods/sets");

    /// <summary>订阅（已安装 mod）端点：POST 订阅、DELETE 退订，都按 <c>activationSetId</c> 记。</summary>
    public static Uri InstalledEndpoint { get; } = new("https://api.paradox-interactive.com/mods/installed");

    /// <summary>订阅写接口要求的版本头；缺了会被服务端按旧协议处理。</summary>
    public static IReadOnlyDictionary<string, string> AcceptVersionHeaders { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Accept-Version"] = "2",
        };

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

            var requirements = ParseRequirements(detail);

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
                ExternalLinkTypes: linkTypes,
                Requirements: requirements));
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX Mods 详情响应解析失败: " + ex.Message);
            return AppResult<PdxModsDetail>.Fail(ErrorKind.Io, "pdx-mods-detail:bad-json");
        }
    }

    /// <summary>解析 <c>modDetail.requirements[]</c>（前置依赖）；缺 id 的项直接跳过。</summary>
    private static IReadOnlyList<PdxModRequirement> ParseRequirements(JsonObject detail)
    {
        if (detail["requirements"] is not JsonArray array)
        {
            return [];
        }

        var list = new List<PdxModRequirement>(array.Count);
        foreach (var entry in array)
        {
            if (entry is not JsonObject requirement)
            {
                continue;
            }

            var id = GetString(requirement, "id");
            if (id.Length == 0)
            {
                continue;
            }

            int? version = int.TryParse(
                GetString(requirement, "version"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsedVersion)
                ? parsedVersion
                : null;

            list.Add(new PdxModRequirement(
                ProductType: GetString(requirement, "productType"),
                Id: id,
                Name: GetString(requirement, "name"),
                Version: version,
                DependencyVersion: NullIfEmpty(GetString(requirement, "dependencyVersion"))));
        }

        return list;
    }

    /// <summary>发请求；网络层故障统一折算成 0（调用方据此给 <see cref="ErrorKind.Io"/>）。</summary>
    private async Task<PdxHttpResponse> SendAsync(
        string method,
        Uri uri,
        string? jsonBody,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        var authorization = session is null || string.IsNullOrWhiteSpace(session.SessionToken)
            ? null
            : new JsonObject { ["session"] = new JsonObject { ["token"] = session.SessionToken } }.ToJsonString();

        try
        {
            return await _http.SendAsync(method, uri, authorization, jsonBody, cancellationToken, headers)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("PDX Mods 请求失败（网络）: " + method + " " + uri.AbsolutePath + " — " + ex.Message);
            return new PdxHttpResponse(0, string.Empty);
        }
    }

    /// <summary>
    /// 取某款游戏在 PDX 侧的播放集（activation set）列表。
    /// <para>
    /// 订阅按播放集登记，所以要先拿到 <c>activationSetId</c>；没有播放集时先
    /// <see cref="CreatePlaySetAsync"/> 建一个。
    /// </para>
    /// </summary>
    /// <param name="gameName">平台游戏 id（形如 <c>cities_skylines_2</c>）。</param>
    /// <param name="session">登录会话（必填，否则 401）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<IReadOnlyList<PdxPlaySet>>> GetPlaySetsAsync(
        string gameName,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);

        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return AppResult<IReadOnlyList<PdxPlaySet>>.Fail(ErrorKind.Unauthorized, "pdx-playsets:sign-in-required");
        }

        var uri = new Uri(
            SetsEndpoint.AbsoluteUri + "/list/" + Uri.EscapeDataString(gameName) + "?includeShared=false");

        var response = await SendAsync("GET", uri, null, session, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            _logger.Warn("PDX 播放集列表请求失败: game=" + gameName + " status=" + response.StatusCode);
            return AppResult<IReadOnlyList<PdxPlaySet>>.Fail(kind, "pdx-playsets:" + kind.ToString().ToLowerInvariant());
        }

        try
        {
            var root = JsonNode.Parse(response.Body);
            var list = new List<PdxPlaySet>();
            foreach (var node in EnumerateItems(root, "sets", "playSets", "items"))
            {
                if (node is not JsonObject item)
                {
                    continue;
                }

                var id = GetLong(item, "id");
                if (id <= 0)
                {
                    continue;
                }

                list.Add(new PdxPlaySet(
                    Id: id,
                    Name: FirstNonEmpty(GetString(item, "name"), GetString(item, "displayName")),
                    GameName: NullIfEmpty(FirstNonEmpty(GetString(item, "game"), GetString(item, "gameName"))),
                    ModCount: GetNullableInt(item, "modsCount")));
            }

            return AppResult<IReadOnlyList<PdxPlaySet>>.Ok(list);
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX 播放集列表解析失败: " + ex.Message);
            return AppResult<IReadOnlyList<PdxPlaySet>>.Fail(ErrorKind.Io, "pdx-playsets:bad-json");
        }
    }

    /// <summary>
    /// 在 PDX 侧建一个播放集。
    /// <para>注意：这会在用户账号里留下一个与官方启动器并列的播放集，命名要克制。</para>
    /// </summary>
    /// <param name="gameName">平台游戏 id。</param>
    /// <param name="name">播放集显示名。</param>
    /// <param name="session">登录会话（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<PdxPlaySet>> CreatePlaySetAsync(
        string gameName,
        string name,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return AppResult<PdxPlaySet>.Fail(ErrorKind.Unauthorized, "pdx-playset-create:sign-in-required");
        }

        var body = new JsonObject
        {
            ["game"] = gameName,
            ["name"] = name,
        };

        var response = await SendAsync("POST", SetsEndpoint, body.ToJsonString(), session, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            _logger.Warn("PDX 播放集创建失败: game=" + gameName + " status=" + response.StatusCode);
            return AppResult<PdxPlaySet>.Fail(kind, "pdx-playset-create:" + kind.ToString().ToLowerInvariant());
        }

        try
        {
            var root = JsonNode.Parse(response.Body);
            var node = root as JsonObject;
            if (node?["set"] is JsonObject nested)
            {
                node = nested;
            }

            var id = node is null ? 0 : GetLong(node, "id");
            if (id <= 0)
            {
                return AppResult<PdxPlaySet>.Fail(ErrorKind.Io, "pdx-playset-create:bad-json");
            }

            return AppResult<PdxPlaySet>.Ok(new PdxPlaySet(
                Id: id,
                Name: FirstNonEmpty(GetString(node!, "name"), name),
                GameName: gameName,
                ModCount: 0));
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX 播放集创建响应解析失败: " + ex.Message);
            return AppResult<PdxPlaySet>.Fail(ErrorKind.Io, "pdx-playset-create:bad-json");
        }
    }

    /// <summary>取一个播放集里的 mod（用来判断当前 mod 是否已订阅）。</summary>
    /// <param name="activationSetId">播放集 id。</param>
    /// <param name="session">登录会话（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<IReadOnlyList<PdxPlaySetMod>>> GetPlaySetModsAsync(
        long activationSetId,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        if (activationSetId <= 0)
        {
            return AppResult<IReadOnlyList<PdxPlaySetMod>>.Fail(ErrorKind.Invalid, "pdx-playset-mods:bad-set");
        }

        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return AppResult<IReadOnlyList<PdxPlaySetMod>>.Fail(ErrorKind.Unauthorized, "pdx-playset-mods:sign-in-required");
        }

        var uri = new Uri(
            SetsEndpoint.AbsoluteUri + "/" +
            activationSetId.ToString(CultureInfo.InvariantCulture) + "/mods");

        var response = await SendAsync("GET", uri, null, session, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            _logger.Warn("PDX 播放集 mod 列表请求失败: set=" + activationSetId + " status=" + response.StatusCode);
            return AppResult<IReadOnlyList<PdxPlaySetMod>>.Fail(kind, "pdx-playset-mods:" + kind.ToString().ToLowerInvariant());
        }

        try
        {
            var root = JsonNode.Parse(response.Body);
            var list = new List<PdxPlaySetMod>();
            foreach (var node in EnumerateItems(root, "mods", "items", "results"))
            {
                if (node is not JsonObject item)
                {
                    continue;
                }

                var modId = FirstNonEmpty(GetString(item, "modId"), GetString(item, "id"));
                if (modId.Length == 0)
                {
                    continue;
                }

                list.Add(new PdxPlaySetMod(
                    ModId: modId,
                    Name: NullIfEmpty(FirstNonEmpty(GetString(item, "displayName"), GetString(item, "name"))),
                    Version: GetNullableInt(item, "version"),
                    Enabled: GetBool(item, "enabled", true)));
            }

            return AppResult<IReadOnlyList<PdxPlaySetMod>>.Ok(list);
        }
        catch (JsonException ex)
        {
            _logger.Warn("PDX 播放集 mod 列表解析失败: " + ex.Message);
            return AppResult<IReadOnlyList<PdxPlaySetMod>>.Fail(ErrorKind.Io, "pdx-playset-mods:bad-json");
        }
    }

    /// <summary>
    /// 订阅一个 mod：<c>POST /mods/installed</c>，body 里的 <c>activationSetId</c> 决定登记到哪个播放集。
    /// </summary>
    /// <param name="activationSetId">播放集 id。</param>
    /// <param name="modId">平台 mod id。</param>
    /// <param name="version">要固定的版本号；null 表示跟随最新。</param>
    /// <param name="session">登录会话（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<bool>> SubscribeAsync(
        long activationSetId,
        string modId,
        int? version,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        if (activationSetId <= 0)
        {
            return AppResult<bool>.Fail(ErrorKind.Invalid, "pdx-subscribe:bad-set");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return AppResult<bool>.Fail(ErrorKind.Unauthorized, "pdx-subscribe:sign-in-required");
        }

        var entry = new JsonObject();
        if (long.TryParse(modId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericId))
        {
            entry["id"] = numericId;
        }
        else
        {
            entry["id"] = modId;
        }

        if (version is > 0)
        {
            entry["version"] = version.Value;
        }

        var body = new JsonObject
        {
            ["activationSetId"] = activationSetId,
            ["mods"] = new JsonArray(entry),
        };

        var response = await SendAsync(
            "POST",
            InstalledEndpoint,
            body.ToJsonString(),
            session,
            cancellationToken,
            AcceptVersionHeaders).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            _logger.Warn("PDX 订阅失败: mod=" + modId + " set=" + activationSetId + " status=" + response.StatusCode);
            return AppResult<bool>.Fail(kind, "pdx-subscribe:" + kind.ToString().ToLowerInvariant());
        }

        return AppResult<bool>.Ok(true);
    }

    /// <summary>取消订阅：<c>DELETE /mods/installed?activationSetId=…&amp;modId=…</c>。</summary>
    /// <param name="activationSetId">播放集 id。</param>
    /// <param name="modId">平台 mod id。</param>
    /// <param name="session">登录会话（必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<bool>> UnsubscribeAsync(
        long activationSetId,
        string modId,
        PdxLauncher.Core.Accounts.PdxSession? session,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        if (session is null || string.IsNullOrWhiteSpace(session.SessionToken))
        {
            return AppResult<bool>.Fail(ErrorKind.Unauthorized, "pdx-unsubscribe:sign-in-required");
        }

        var uri = new Uri(
            InstalledEndpoint.AbsoluteUri +
            "?activationSetId=" + activationSetId.ToString(CultureInfo.InvariantCulture) +
            "&modId=" + Uri.EscapeDataString(modId));

        var response = await SendAsync(
            "DELETE",
            uri,
            null,
            session,
            cancellationToken,
            AcceptVersionHeaders).ConfigureAwait(false);

        if (!response.IsSuccess)
        {
            var kind = MapFailure(response.StatusCode);
            _logger.Warn("PDX 退订失败: mod=" + modId + " set=" + activationSetId + " status=" + response.StatusCode);
            return AppResult<bool>.Fail(kind, "pdx-unsubscribe:" + kind.ToString().ToLowerInvariant());
        }

        return AppResult<bool>.Ok(true);
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

    /// <summary>
    /// 从响应里取条目集合：顶层是数组就直接用，是对象则依次试给定字段名。
    /// <para>播放集相关端点的外层包装不稳定（有时裸数组，有时 <c>{sets:[…]}</c>），所以做宽容解析。</para>
    /// </summary>
    private static IEnumerable<JsonNode?> EnumerateItems(JsonNode? root, params string[] keys)
    {
        if (root is JsonArray direct)
        {
            return direct;
        }

        if (root is not JsonObject node)
        {
            return [];
        }

        foreach (var key in keys)
        {
            if (node[key] is JsonArray array)
            {
                return array;
            }
        }

        return [];
    }

    /// <summary>取可空整数（服务端可能给 null 或缺字段）。</summary>
    private static int? GetNullableInt(JsonObject node, string key)
    {
        if (node[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        // 有的端点是数字字符串。
        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    /// <summary>取布尔（缺字段时返回 <paramref name="fallback"/>）。</summary>
    private static bool GetBool(JsonObject node, string key, bool fallback)
    {
        if (node[key] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        return value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed) ? parsed : fallback;
    }
}
