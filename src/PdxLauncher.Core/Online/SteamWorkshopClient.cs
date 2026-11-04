namespace PdxLauncher.Core.Online;

using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// Steam 创意工坊只读客户端（匿名、keyless）。
/// <para>
/// 三条端点的实测结论（2026-10）：
/// </para>
/// <list type="bullet">
/// <item>列表：<c>GET /workshop/browse/?appid=…&amp;section=readytouseitems&amp;browsesort=…&amp;p=…</c>，
/// 数据在 <c>valve-ssr-data</c> 里，解析见 <see cref="SteamWorkshopHtmlParser"/>。</item>
/// <item>详情：列表里的 SSR 数据够用，但完整描述只能走
/// <c>POST /ISteamRemoteStorage/GetPublishedFileDetails/v1/</c> —— <b>只吃 form-urlencoded</b>
/// （GET→405、JSON body→400），所以走 <see cref="IHttpFetcher.PostFormAsync"/>。</item>
/// <item>能力探测：<c>store.steampowered.com/api/appdetails?appids=…&amp;filters=categories</c>，
/// 分类里有 <see cref="WorkshopCategory"/> 才算有创意工坊（CS2 没有，CK3 有）。结果按 appId 缓存。</item>
/// </list>
/// </summary>
public sealed class SteamWorkshopClient
{
    /// <summary>默认每页条数（服务端 <c>num_per_page</c> 上限 50，超过会回落到 30）。</summary>
    public const int DefaultPageSize = 30;

    /// <summary><c>num_per_page</c> 的安全上限。</summary>
    public const int MaxPageSize = 50;

    /// <summary>appdetails 里代表创意工坊的分类名。</summary>
    public const string WorkshopCategory = "Steam Workshop";

    private readonly IHttpFetcher _http;
    private readonly IAppLogger _logger;

    /// <summary>appId → 有没有创意工坊。Steam 分类几乎不变，进程内缓存足够。</summary>
    private readonly ConcurrentDictionary<int, bool> _workshopProbes = new();

    /// <summary>
    /// 在途请求合并：同一个 URI 同时被两个调用点需要时（详情页与列表页、连点两次）只发一次。
    /// 只合并「进行中」的请求，不做结果缓存——缓存由上层各 cache 负责。
    /// </summary>
    private readonly RequestCoalescer _coalesce;

    /// <summary>创建客户端。</summary>
    /// <param name="http">HTTP 抽象（与其它联网服务共用同一个 HttpFetcher，代理重建时一起换）。</param>
    /// <param name="logger">日志。</param>
    public SteamWorkshopClient(IHttpFetcher http, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        _http = http;
        _logger = logger;
        _coalesce = new RequestCoalescer(logger);
    }

    /// <summary>拼列表页 URI（纯函数，自检直接覆盖）。页码对外从 0 开始，对 Steam 从 1 开始。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="query">查询条件。</param>
    public static Uri BuildBrowseUri(int appId, SteamWorkshopQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 0) + 1;
        var keyword = query.SearchText?.Trim();
        var hasKeyword = !string.IsNullOrEmpty(keyword);

        // 关键词与排序可以并存：服务端在有 searchtext 时照样按 browsesort 排序。
        // 只有「textsearch 这一档」在没有关键词时才无意义 —— 那时回退成 trend。
        var sort = SortValue(query.Sort);
        if (query.Sort == SteamWorkshopSort.TextSearch && !hasKeyword)
        {
            sort = SortValue(SteamWorkshopSort.Trend);
        }

        var builder = new StringBuilder(OnlineHosts.SteamWorkshopBrowseEndpoint.AbsoluteUri);
        builder.Append("?appid=").Append(appId.ToString(CultureInfo.InvariantCulture));
        builder.Append("&section=readytouseitems&browsesort=").Append(sort);
        builder.Append("&num_per_page=").Append(pageSize.ToString(CultureInfo.InvariantCulture));
        builder.Append("&p=").Append(page.ToString(CultureInfo.InvariantCulture));
        if (hasKeyword)
        {
            // 参数名是 searchtext（没有下划线）。
            builder.Append("&searchtext=").Append(Uri.EscapeDataString(keyword!));
        }

        // 时间范围只对「最热门 / 最受好评」生效（Steam 的时间范围下拉也只在这两档排序下出现）；
        // 参数名是 days（服务端回显成 trend_days）。-1 = Steam 自己的「All time / 发布至今」，
        // 原样透传（服务端把 0 当成「不传」，会回落成默认 7 天）。
        if (query.Days is { } days && SupportsDays(query.Sort))
        {
            builder.Append("&days=").Append(days.ToString(CultureInfo.InvariantCulture));
        }

        return new Uri(builder.ToString());
    }

    /// <summary>浏览一页创意工坊。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="query">查询条件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopPage>> BrowseAsync(
        int appId,
        SteamWorkshopQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        // key 用完整的列表 URI：翻页、换排序、换关键词都是不同请求，绝不能并成一个。
        var key = "browse:" + appId.ToString(CultureInfo.InvariantCulture) + "|" + BuildBrowseUri(appId, query).AbsoluteUri;
        return _coalesce.RunAsync(key, ct => BrowseCoreAsync(appId, query, ct), cancellationToken);
    }

    private async Task<AppResult<SteamWorkshopPage>> BrowseCoreAsync(
        int appId,
        SteamWorkshopQuery query,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(query.Page, 0);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);

        try
        {
            await using var stream = await _http
                .GetAsync(BuildBrowseUri(appId, query), cancellationToken)
                .ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var parsed = SteamWorkshopHtmlParser.Parse(html, page, pageSize);
            if (parsed is null)
            {
                _logger.Warn("Steam 创意工坊页面结构无法解析: appid=" + appId.ToString(CultureInfo.InvariantCulture));
                return AppResult<SteamWorkshopPage>.Fail(ErrorKind.Unsupported, "steam-workshop-parse");
            }

            return AppResult<SteamWorkshopPage>.Ok(parsed);
        }
        catch (NetworkTimeoutException ex)
        {
            // 连不上（DNS 被污染 / 代理不可达）：和「服务端返回了错误」是两种故障，
            // 界面要分开说，否则用户只会看到一句含糊的「加载失败」。
            _logger.Warn("Steam 创意工坊连接失败: appid=" + appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return AppResult<SteamWorkshopPage>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 没被取消却收到取消 = 连接层超时。真正的用户取消继续往上抛。
            _logger.Warn("Steam 创意工坊连接超时: appid=" + appId.ToString(CultureInfo.InvariantCulture));
            return AppResult<SteamWorkshopPage>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam 创意工坊请求失败: appid=" + appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return AppResult<SteamWorkshopPage>.Fail(ErrorKind.Unknown, "steam-workshop-http");
        }
    }

    /// <summary>取单个条目的完整描述。</summary>
    /// <param name="publishedFileId">Steam 已发布文件 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopItemDetail>> GetDetailAsync(
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);

        return _coalesce.RunAsync(
            "detail:" + publishedFileId,
            ct => GetDetailCoreAsync(publishedFileId, ct),
            cancellationToken);
    }

    private async Task<AppResult<SteamWorkshopItemDetail>> GetDetailCoreAsync(
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        var body = "itemcount=1&publishedfileids[0]=" + Uri.EscapeDataString(publishedFileId);

        try
        {
            var text = await _http
                .PostFormAsync(OnlineHosts.SteamPublishedFileDetailsEndpoint, body, cancellationToken)
                .ConfigureAwait(false);

            var (detail, result) = ParseDetailWithResult(text);
            if (detail is null)
            {
                // 记下 Steam 给的 EResult：9 表示「对匿名调用方不可见」（未公开 / 仅好友 / 私密），
                // 与「真的不存在」在界面上是同一句文案，只有日志能区分开。
                _logger.Warn("Steam 创意工坊详情不可用（item=" + publishedFileId + "）：" + DetailUnavailableReason(result));
                return AppResult<SteamWorkshopItemDetail>.Fail(ErrorKind.NotFound, "steam-workshop-detail:not-found");
            }

            return AppResult<SteamWorkshopItemDetail>.Ok(detail);
        }
        catch (NetworkTimeoutException ex)
        {
            _logger.Warn("Steam 创意工坊详情连接失败: " + ex.Message);
            return AppResult<SteamWorkshopItemDetail>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Steam 创意工坊详情连接超时");
            return AppResult<SteamWorkshopItemDetail>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam 创意工坊详情请求失败: " + ex.Message);
            return AppResult<SteamWorkshopItemDetail>.Fail(ErrorKind.Unknown, "steam-workshop-detail-http");
        }
        catch (JsonException ex)
        {
            _logger.Warn("Steam 创意工坊详情解析失败: " + ex.Message);
            return AppResult<SteamWorkshopItemDetail>.Fail(ErrorKind.Io, "steam-workshop-detail-bad-json");
        }
    }

    /// <summary>
    /// 取创意工坊「物品页」上的补充信息：作者主页、描述里的图片、首屏评论。
    /// <para>
    /// 这些字段 <c>GetPublishedFileDetails</c> 都不给，只能读页面；页面改版时解析会失败，
    /// 此时返回 Fail 让调用方静默降级，绝不影响详情主体。
    /// </para>
    /// </summary>
    /// <param name="publishedFileId">Steam 已发布文件 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopItemPage>> GetItemPageAsync(
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);

        return _coalesce.RunAsync(
            "item:" + publishedFileId,
            ct => GetItemPageCoreAsync(publishedFileId, ct),
            cancellationToken);
    }

    private async Task<AppResult<SteamWorkshopItemPage>> GetItemPageCoreAsync(
        string publishedFileId,
        CancellationToken cancellationToken)
    {
        var uri = new Uri(
            OnlineHosts.SteamCommunityItemEndpoint,
            "?id=" + Uri.EscapeDataString(publishedFileId));

        try
        {
            await using var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var parsed = SteamWorkshopItemPageParser.Parse(html);
            if (parsed is null)
            {
                _logger.Warn("Steam 创意工坊物品页结构无法解析: id=" + publishedFileId);
                return AppResult<SteamWorkshopItemPage>.Fail(ErrorKind.Unsupported, "steam-item-page-parse");
            }

            return AppResult<SteamWorkshopItemPage>.Ok(parsed);
        }
        catch (NetworkTimeoutException ex)
        {
            _logger.Warn("Steam 创意工坊物品页连接失败: id=" + publishedFileId + " " + ex.Message);
            return AppResult<SteamWorkshopItemPage>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Steam 创意工坊物品页连接超时: id=" + publishedFileId);
            return AppResult<SteamWorkshopItemPage>.Fail(ErrorKind.Unknown, "steam-workshop-network");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam 创意工坊物品页请求失败: id=" + publishedFileId + " " + ex.Message);
            return AppResult<SteamWorkshopItemPage>.Fail(ErrorKind.Unknown, "steam-item-page-http");
        }
    }

    /// <summary>
    /// 取一页创意工坊评论。
    /// <para>
    /// 物品页 HTML 只内嵌首屏评论，翻页是页面脚本请求
    /// <c>/comment/PublishedFile_Public/render/{作者}/{文件}/?start=…&amp;count=…</c>；
    /// 返回 JSON 的 <c>comments_html</c> 与物品页内嵌评论同构，所以复用同一份解析。
    /// </para>
    /// </summary>
    /// <param name="creatorSteamId">条目作者的 SteamID64（物品详情里的 <c>creator</c>）。</param>
    /// <param name="publishedFileId">Steam 已发布文件 id。</param>
    /// <param name="start">起始偏移（0 基）。</param>
    /// <param name="count">本页条数（服务端上限 50）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<AppResult<SteamWorkshopCommentPage>> GetCommentsAsync(
        string creatorSteamId,
        string publishedFileId,
        int start,
        int count,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(creatorSteamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);

        var key = "comments:" + creatorSteamId + "|" + publishedFileId
                  + "|" + Math.Max(0, start).ToString(CultureInfo.InvariantCulture)
                  + "|" + Math.Clamp(count, 1, MaxPageSize).ToString(CultureInfo.InvariantCulture);
        return _coalesce.RunAsync(
            key,
            ct => GetCommentsCoreAsync(creatorSteamId, publishedFileId, start, count, ct),
            cancellationToken);
    }

    private async Task<AppResult<SteamWorkshopCommentPage>> GetCommentsCoreAsync(
        string creatorSteamId,
        string publishedFileId,
        int start,
        int count,
        CancellationToken cancellationToken)
    {
        var safeStart = Math.Max(0, start);
        var safeCount = Math.Clamp(count, 1, MaxPageSize);

        var uri = new Uri(
            OnlineHosts.SteamCommunityCommentEndpoint.AbsoluteUri +
            Uri.EscapeDataString(creatorSteamId) + "/" +
            Uri.EscapeDataString(publishedFileId) + "/" +
            "?start=" + safeStart.ToString(CultureInfo.InvariantCulture) +
            "&count=" + safeCount.ToString(CultureInfo.InvariantCulture));

        try
        {
            await using var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var ok = root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                _logger.Warn("Steam 创意工坊评论接口返回失败: id=" + publishedFileId);
                return AppResult<SteamWorkshopCommentPage>.Fail(ErrorKind.Unsupported, "steam-comments-rejected");
            }

            var total = root.TryGetProperty("total_count", out var totalNode) && totalNode.TryGetInt32(out var totalValue)
                ? totalValue
                : 0;

            var html = root.TryGetProperty("comments_html", out var htmlNode) && htmlNode.ValueKind == JsonValueKind.String
                ? htmlNode.GetString()
                : null;

            var comments = SteamWorkshopItemPageParser.ParseComments(html, safeCount);
            return AppResult<SteamWorkshopCommentPage>.Ok(
                new SteamWorkshopCommentPage(comments, total, safeStart, safeCount));
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam 创意工坊评论请求失败: id=" + publishedFileId + " " + ex.Message);
            return AppResult<SteamWorkshopCommentPage>.Fail(ErrorKind.Unknown, "steam-comments-http");
        }
        catch (JsonException ex)
        {
            _logger.Warn("Steam 创意工坊评论解析失败: " + ex.Message);
            return AppResult<SteamWorkshopCommentPage>.Fail(ErrorKind.Io, "steam-comments-bad-json");
        }
    }

    /// <summary>探测某款游戏有没有 Steam 创意工坊（结果按 appId 缓存）。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<AppResult<bool>> HasWorkshopAsync(int appId, CancellationToken cancellationToken)
    {
        if (_workshopProbes.TryGetValue(appId, out var cached))
        {
            return AppResult<bool>.Ok(cached);
        }

        var hasWorkshop = await ProbeAppDetailsAsync(appId, cancellationToken).ConfigureAwait(false);
        if (hasWorkshop is null)
        {
            // appdetails 偶尔会被限流或返回坏 JSON。退一步直接抓工坊列表页：
            // 有工坊的游戏一定带 valve-ssr-data，没有的（CS2 那个 302 到 /workshop 的落地页）不带。
            hasWorkshop = await ProbeWorkshopPageAsync(appId, cancellationToken).ConfigureAwait(false);
        }

        if (hasWorkshop is null)
        {
            return AppResult<bool>.Fail(ErrorKind.Unknown, "steam-workshop-probe-http");
        }

        _workshopProbes[appId] = hasWorkshop.Value;
        return AppResult<bool>.Ok(hasWorkshop.Value);
    }

    /// <summary>用 appdetails 判定有没有创意工坊；网络或 JSON 出错返回 null，交给兜底探测。</summary>
    private async Task<bool?> ProbeAppDetailsAsync(int appId, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            OnlineHosts.SteamAppDetailsEndpoint.AbsoluteUri +
            "?appids=" + appId.ToString(CultureInfo.InvariantCulture) +
            "&filters=categories&cc=us&l=english");

        try
        {
            await using var stream = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            return ParseHasWorkshop(json, appId);
        }
        catch (NetworkTimeoutException ex)
        {
            _logger.Warn("Steam appdetails 探测连接失败: appid=" +
                appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Steam appdetails 探测连接超时: appid=" +
                appId.ToString(CultureInfo.InvariantCulture));
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam appdetails 探测失败: appid=" +
                appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.Warn("Steam appdetails 解析失败: " + ex.Message);
            return null;
        }
    }

    /// <summary>兜底探测：工坊列表页里能解析出 SSR 数据就算有创意工坊。</summary>
    private async Task<bool?> ProbeWorkshopPageAsync(int appId, CancellationToken cancellationToken)
    {
        var query = new SteamWorkshopQuery(SearchText: null, Sort: SteamWorkshopSort.Trend, Page: 0, PageSize: DefaultPageSize);

        try
        {
            await using var stream = await _http
                .GetAsync(BuildBrowseUri(appId, query), cancellationToken)
                .ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var html = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            return SteamWorkshopHtmlParser.Parse(html, 0, DefaultPageSize) is not null;
        }
        catch (NetworkTimeoutException ex)
        {
            _logger.Warn("Steam 创意工坊兜底探测连接失败: appid=" +
                appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.Warn("Steam 创意工坊兜底探测连接超时: appid=" +
                appId.ToString(CultureInfo.InvariantCulture));
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.Warn("Steam 创意工坊兜底探测失败: appid=" +
                appId.ToString(CultureInfo.InvariantCulture) + " " + ex.Message);
            return null;
        }
    }

    /// <summary>从 appdetails 响应判定有没有创意工坊（纯函数，自检覆盖）。</summary>
    /// <param name="json">响应正文。</param>
    /// <param name="appId">appid（响应以它为键）。</param>
    public static bool ParseHasWorkshop(string? json, int appId)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(json)?[appId.ToString(CultureInfo.InvariantCulture)];
            if (root?["success"] is not JsonValue success
                || !success.TryGetValue<bool>(out var ok)
                || !ok)
            {
                return false;
            }

            if (root["data"]?["categories"] is not JsonArray categories)
            {
                return false;
            }

            foreach (var entry in categories)
            {
                if (entry?["description"] is JsonValue description
                    && description.TryGetValue<string>(out var text)
                    && string.Equals(text, WorkshopCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>解析 GetPublishedFileDetails 响应（纯函数，自检覆盖）。</summary>
    /// <param name="json">响应正文。</param>
    /// <returns>解析成功且条目可见时返回详情；否则 null。需要区分失败原因时用 <see cref="ParseDetailWithResult"/>。</returns>
    public static SteamWorkshopItemDetail? ParseDetail(string? json) => ParseDetailWithResult(json).Detail;

    /// <summary>
    /// 解析 GetPublishedFileDetails 响应，并把「为什么没有详情」一并带出来。
    /// <para>
    /// Steam 对**匿名调用方**看不见的条目回 <c>{"result":9}</c>（FileNotFound）而不是 4xx，
    /// 隐藏 / 仅好友 / 私密 / 未完成提交的条目都长这样。旧实现直接把这种响应当成一条
    /// 标题描述全空的条目返回，界面上就成了一张空卡片 —— 所以这里必须先看 result。
    /// </para>
    /// </summary>
    /// <param name="json">响应正文。</param>
    /// <returns>
    /// <c>Detail</c> 为空时 <c>Result</c> 是 Steam 给的原因：<c>9</c> = 不可见 / 不存在，
    /// 其它非 1 值为对应 EResult，<c>-1</c> = 响应本身解析不出来。
    /// </returns>
    public static (SteamWorkshopItemDetail? Detail, int Result) ParseDetailWithResult(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, ResultUnknown);
        }

        try
        {
            if (JsonNode.Parse(json)?["response"]?["publishedfiledetails"] is not JsonArray array
                || array.Count == 0
                || array[0] is not JsonObject item)
            {
                return (null, ResultUnknown);
            }

            // result 缺省按 1（OK）处理：自检夹具与老版本响应里不一定带这个字段。
            var raw = LongOf(item, "result");
            var result = raw > 0 ? (int)Math.Min(raw, int.MaxValue) : 1;
            if (result != 1)
            {
                return (null, result);
            }

            var detail = new SteamWorkshopItemDetail(
                PublishedFileId: StringOf(item, "publishedfileid") ?? string.Empty,
                Title: StringOf(item, "title") ?? string.Empty,
                Description: StringOf(item, "description") ?? StringOf(item, "file_description") ?? string.Empty,
                PreviewUrl: StringOf(item, "preview_url"),
                FileSizeBytes: LongOf(item, "file_size"),
                CreatedUtc: FromUnixSeconds(LongOf(item, "time_created")),
                UpdatedUtc: FromUnixSeconds(LongOf(item, "time_updated")),
                Filename: StringOf(item, "filename"),
                CreatorSteamId: StringOf(item, "creator"),
                Tags: TagsOf(item),
                Views: LongOf(item, "views"),
                LifetimeSubscriptions: LongOf(item, "lifetime_subscriptions"));

            return (detail, result);
        }
        catch (JsonException)
        {
            return (null, ResultUnknown);
        }
    }

    /// <summary>响应里既没有 result 字段、也解析不出条目时用的哨兵值。</summary>
    public const int ResultUnknown = -1;

    /// <summary>把 <c>GetPublishedFileDetails</c> 的 result 翻成中文原因（界面 / 日志共用）。</summary>
    /// <param name="result">Steam 返回的 EResult；<see cref="ResultUnknown"/> 表示没解析出来。</param>
    public static string DetailUnavailableReason(int result) => result switch
    {
        9 => "这个条目对未登录 / 匿名调用方不可见（隐藏、仅好友或私密）；不等于它不存在",
        ResultUnknown => "Steam 没有返回可解析的条目详情",
        _ => "查不到这个创意工坊条目（EResult=" + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）",
    };

    /// <summary>取 <c>tags[].tag</c>（Steam 返回的是对象数组，旧代码直接丢弃了）。</summary>
    private static IReadOnlyList<string> TagsOf(JsonObject item)
    {
        if (item["tags"] is not JsonArray tags)
        {
            return [];
        }

        var values = new List<string>(tags.Count);
        foreach (var entry in tags)
        {
            if (entry?["tag"] is JsonValue value && value.TryGetValue<string>(out var tag)
                && !string.IsNullOrWhiteSpace(tag))
            {
                values.Add(tag.Trim());
            }
        }

        return values;
    }

    private static bool SupportsDays(SteamWorkshopSort sort) =>
        sort is SteamWorkshopSort.Trend or SteamWorkshopSort.TopRated;

    private static string SortValue(SteamWorkshopSort sort) => sort switch
    {
        SteamWorkshopSort.TopRated => "toprated",
        SteamWorkshopSort.MostRecent => "mostrecent",
        // Steam 现行 browse 端点只认这四个排序值：trend / toprated / mostrecent / lastupdated，
        // 外加「不重复订阅者总计」= totaluniquesubscribers。旧文档里的 mostupdated / mostsubscribed
        // 服务端不认，会静默回落成 trend（列表与「最热门」逐项相同），所以必须用真实值。
        SteamWorkshopSort.MostUpdated => "lastupdated",
        SteamWorkshopSort.MostSubscribed => "totaluniquesubscribers",
        SteamWorkshopSort.TextSearch => "textsearch",
        _ => "trend",
    };

    private static DateTimeOffset FromUnixSeconds(long seconds)
    {
        if (seconds <= 0)
        {
            return DateTimeOffset.UnixEpoch;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    private static string? StringOf(JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return value.ToJsonString().Trim('"');
    }

    private static long LongOf(JsonObject node, string name)
    {
        var text = StringOf(node, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
