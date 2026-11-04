namespace PdxLauncher.Checks;

using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// Steam 创意工坊只读接口自检：列表 URI 拼装、SSR 数据解析、能力探测（appdetails）、
/// 详情表单请求、来源判定、出口白名单。全程不联网 —— HTTP 抽象用记录型 fake。
/// </summary>
public static class SteamWorkshopApiChecks
{
    private const string PublishedFileId = "2216623537";
    private const int Ck3AppId = 1158310;
    private const int Cs2AppId = 949230;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckBrowseUri();
        CheckSourceChooser();
        CheckHtmlParser();
        CheckAppDetails();
        CheckDetailParsing();
        CheckDetailRequest();
        CheckHasWorkshopCache();
        CheckHasWorkshopFallback();
        CheckOutboundWhitelist();
        CheckNetworkFailure();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 列表 URI

    private static void CheckBrowseUri()
    {
        var popular = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0)).AbsoluteUri;

        ChecksContext.Check("URI：appid", popular.Contains("appid=1158310", StringComparison.Ordinal), popular);
        ChecksContext.Check("URI：只看可用条目", popular.Contains("section=readytouseitems", StringComparison.Ordinal));
        ChecksContext.Check("URI：最热门排序", popular.Contains("browsesort=trend", StringComparison.Ordinal));
        ChecksContext.Check("URI：默认每页 30", popular.Contains("num_per_page=30", StringComparison.Ordinal));
        ChecksContext.Check("URI：对外第 0 页即 p=1", popular.Contains("p=1", StringComparison.Ordinal));

        var recent = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.MostRecent, 1)).AbsoluteUri;
        ChecksContext.Check("URI：最近更新排序", recent.Contains("browsesort=mostrecent", StringComparison.Ordinal));
        ChecksContext.Check("URI：第 1 页即 p=2", recent.Contains("p=2", StringComparison.Ordinal));

        var rated = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.TopRated, 0)).AbsoluteUri;
        ChecksContext.Check("URI：评分排序", rated.Contains("browsesort=toprated", StringComparison.Ordinal));

        var updated = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.MostUpdated, 0)).AbsoluteUri;
        ChecksContext.Check("URI：最新更新排序", updated.Contains("browsesort=lastupdated", StringComparison.Ordinal), updated);

        var subscribed = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.MostSubscribed, 0)).AbsoluteUri;
        ChecksContext.Check("URI：订阅者排序", subscribed.Contains("browsesort=totaluniquesubscribers", StringComparison.Ordinal), subscribed);

        // 时间范围只跟最热门/最受好评一起用；-1 是「发布至今」，必须原样传出去。
        var weekly = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0, 30, 7)).AbsoluteUri;
        ChecksContext.Check("URI：时间范围 days=7", weekly.Contains("days=7", StringComparison.Ordinal), weekly);

        var allTime = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.TopRated, 0, 30, -1)).AbsoluteUri;
        ChecksContext.Check("URI：发布至今 days=-1", allTime.Contains("days=-1", StringComparison.Ordinal), allTime);

        var noDays = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.MostUpdated, 0, 30, 30)).AbsoluteUri;
        ChecksContext.Check("URI：非热门排序不传 days", !noDays.Contains("days=", StringComparison.Ordinal), noDays);

        // 关键词与排序并存：服务端带 searchtext 时照样按 browsesort 排序，
        // 所以这里不能把排序改成 textsearch。
        var search = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery("my mod+y", SteamWorkshopSort.Trend, 2, 40)).AbsoluteUri;
        ChecksContext.Check("URI：关键词时保留排序", search.Contains("browsesort=trend", StringComparison.Ordinal), search);
        ChecksContext.Check("URI：关键词不改成 textsearch", !search.Contains("browsesort=textsearch", StringComparison.Ordinal), search);
        ChecksContext.Check("URI：searchtext 参数", search.Contains("searchtext=my%20mod%2By", StringComparison.Ordinal), search);
        ChecksContext.Check("URI：关键词时页码仍生效", search.Contains("p=3", StringComparison.Ordinal));
        ChecksContext.Check("URI：自定义每页条数", search.Contains("num_per_page=40", StringComparison.Ordinal));

        // 显式要 textsearch 时保留它；没有关键词的 textsearch 没有意义，回退成 trend。
        var explicitSearch = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery("mod", SteamWorkshopSort.TextSearch, 0)).AbsoluteUri;
        ChecksContext.Check("URI：显式 textsearch 保留", explicitSearch.Contains("browsesort=textsearch", StringComparison.Ordinal), explicitSearch);

        var orphanSearch = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.TextSearch, 0)).AbsoluteUri;
        ChecksContext.Check("URI：无关键词的 textsearch 回退 trend", orphanSearch.Contains("browsesort=trend", StringComparison.Ordinal), orphanSearch);

        // 「搜索相关度」不该带时间范围：Steam 只对 trend/toprated 认 days，
        // 传了也只是噪音，还可能被服务端当成非法参数把排序打回 trend。
        var relevance = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery("morgenrote", SteamWorkshopSort.TextSearch, 0, 30, 30)).AbsoluteUri;
        ChecksContext.Check("URI：相关度排序带 browsesort=textsearch", relevance.Contains("browsesort=textsearch", StringComparison.Ordinal), relevance);
        ChecksContext.Check("URI：相关度排序仍带关键词", relevance.Contains("searchtext=morgenrote", StringComparison.Ordinal), relevance);
        ChecksContext.Check("URI：相关度排序不传 days", !relevance.Contains("days=", StringComparison.Ordinal), relevance);

        // 超过服务端上限要钳到 50，否则 Steam 会回落成 30 让分页对不上。
        var clamped = SteamWorkshopClient.BuildBrowseUri(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0, 500)).AbsoluteUri;
        ChecksContext.Check("URI：每页条数钳到 50", clamped.Contains("num_per_page=50", StringComparison.Ordinal), clamped);

        ChecksContext.Check(
            "URI：负数页码归 0",
            SteamWorkshopClient.BuildBrowseUri(Ck3AppId, new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, -3))
                .AbsoluteUri.Contains("p=1", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 来源判定

    private static void CheckSourceChooser()
    {
        ChecksContext.Equal(
            "来源：独立版（无 appId）走 PDX",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(null, null));

        ChecksContext.Equal(
            "来源：有 appId 且有工坊走 Steam",
            WorkshopSource.Steam,
            WorkshopSourceChooser.Resolve(Ck3AppId, true));

        ChecksContext.Equal(
            "来源：有 appId 但无工坊走 PDX（CS2）",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(Cs2AppId, false));

        ChecksContext.Equal(
            "来源：还没探测时先按 PDX",
            WorkshopSource.Pdx,
            WorkshopSourceChooser.Resolve(Ck3AppId, null));

        ChecksContext.Equal(
            "来源：探测结果优先于 appId 是否存在",
            WorkshopSource.Steam,
            WorkshopSourceChooser.Resolve(Cs2AppId, true));
    }

    // ------------------------------------------------------------ SSR 解析

    private static void CheckHtmlParser()
    {
        var page = SteamWorkshopHtmlParser.Parse(BuildBrowseHtml(2), 0, 30);
        ChecksContext.Check("解析：返回非 null", page is not null);
        if (page is null)
        {
            return;
        }

        ChecksContext.Equal("解析：条目数", 2, page.Items.Count);
        ChecksContext.Equal("解析：总数", 2, page.TotalCount);
        ChecksContext.Equal("解析：总页数", 1, page.TotalPages);
        ChecksContext.Equal("解析：页码回填", 0, page.Page);
        ChecksContext.Equal("解析：每页条数回填", 30, page.PageSize);

        var first = page.Items[0];
        ChecksContext.Equal("解析：id", "1001", first.PublishedFileId);
        ChecksContext.Equal("解析：标题", "Mod A", first.Title);
        ChecksContext.Equal("解析：简介", "short a", first.ShortDescription);
        ChecksContext.Equal("解析：封面", "https://images.steamusercontent.com/a.jpg", first.PreviewUrl);
        ChecksContext.Equal("解析：作者", "76561197960265728", first.AuthorSteamId);
        ChecksContext.Equal("解析：订阅数", 1234L, first.Subscriptions);
        ChecksContext.Equal("解析：收藏数", 11L, first.Favorites);
        ChecksContext.Equal("解析：浏览数", 98765L, first.Views);
        ChecksContext.Equal("解析：体积（字符串字段）", 4096L, first.FileSizeBytes);
        ChecksContext.Equal("解析：星级", 4.5, first.StarRating);
        ChecksContext.Equal("解析：评分数", 42, first.TotalVotes);
        ChecksContext.Equal("解析：appid", Ck3AppId, first.ConsumerAppId);
        ChecksContext.Equal("解析：标签数", 2, first.Tags.Count);
        ChecksContext.Equal("解析：标签 id", "Economic", first.Tags[0].Tag);
        ChecksContext.Equal("解析：标签显示名", "Economy", first.Tags[0].DisplayName);
        ChecksContext.Equal(
            "解析：创建时间",
            DateTimeOffset.FromUnixTimeSeconds(1_600_000_000).UtcDateTime,
            first.CreatedUtc.UtcDateTime);

        // queryData 直接是对象（不带 queries 数组）时也要认。
        var flat = SteamWorkshopHtmlParser.Parse(BuildFlatHtml(), 1, 30);
        ChecksContext.Check("解析：queryData 为对象形态", flat is not null);
        ChecksContext.Equal("解析：对象形态条目数", 1, flat?.Items.Count ?? -1);

        ChecksContext.Check("解析：没有 SSR 脚本返回 null", SteamWorkshopHtmlParser.Parse("<html><body>nope</body></html>", 0, 30) is null);
        ChecksContext.Check("解析：null 入参返回 null", SteamWorkshopHtmlParser.Parse(null, 0, 30) is null);
        ChecksContext.Check("解析：空串返回 null", SteamWorkshopHtmlParser.Parse(string.Empty, 0, 30) is null);
        ChecksContext.Check("解析：queryData 是坏 JSON 返回 null", SteamWorkshopHtmlParser.Parse(BuildGarbageHtml(), 0, 30) is null);

        // 2026-10 的真实页面：renderContext 在顶层，queries 里前几项不是工坊数据。
        var real = SteamWorkshopHtmlParser.Parse(BuildTopLevelHtml(), 0, 30);
        ChecksContext.Check("解析：renderContext 在顶层的真实形态", real is not null);
        ChecksContext.Equal("解析：跳过没有 results 的 query", 1, real?.Items.Count ?? -1);
        ChecksContext.Equal("解析：真实形态总数", 17717, real?.TotalCount ?? -1);
        ChecksContext.Equal("解析：真实形态总页数", 591, real?.TotalPages ?? -1);

        var empty = SteamWorkshopHtmlParser.Parse(BuildBrowseHtml(0), 0, 30);
        ChecksContext.Check("解析：零条目仍然返回页", empty is not null);
        ChecksContext.Equal("解析：零条目计数", 0, empty?.Items.Count ?? -1);
    }

    // ------------------------------------------------------------ appdetails

    private static void CheckAppDetails()
    {
        ChecksContext.Check(
            "探测：CS2 分类里没有 Steam Workshop",
            !SteamWorkshopClient.ParseHasWorkshop(AppDetailsJson(Cs2AppId, true, ["Single-player", "Steam Achievements"]), Cs2AppId));

        ChecksContext.Check(
            "探测：CK3 分类里有 Steam Workshop",
            SteamWorkshopClient.ParseHasWorkshop(AppDetailsJson(Ck3AppId, true, ["Single-player", "Steam Workshop"]), Ck3AppId));

        ChecksContext.Check(
            "探测：success=false 一律当作没有",
            !SteamWorkshopClient.ParseHasWorkshop(AppDetailsJson(Ck3AppId, false, ["Steam Workshop"]), Ck3AppId));

        // CK3 的响应里没有 CS2 这个键，不能因为「别处有 Workshop」就判定有。
        ChecksContext.Check(
            "探测：按 appId 取键",
            !SteamWorkshopClient.ParseHasWorkshop(AppDetailsJson(Ck3AppId, true, ["Steam Workshop"]), Cs2AppId));

        ChecksContext.Check("探测：坏 JSON 当作没有", !SteamWorkshopClient.ParseHasWorkshop("{", Ck3AppId));
        ChecksContext.Check("探测：null 当作没有", !SteamWorkshopClient.ParseHasWorkshop(null, Ck3AppId));
        ChecksContext.Check("探测：缺 categories 当作没有", !SteamWorkshopClient.ParseHasWorkshop("{\"" + Ck3AppId + "\":{\"success\":true}}", Ck3AppId));
    }

    // ------------------------------------------------------------ 详情

    private static void CheckDetailParsing()
    {
        var detail = SteamWorkshopClient.ParseDetail(DetailJson("long text"));
        ChecksContext.Check("详情：非空", detail is not null);
        if (detail is null)
        {
            return;
        }

        ChecksContext.Equal("详情：id", PublishedFileId, detail.PublishedFileId);
        ChecksContext.Equal("详情：标题", "Big Mod", detail.Title);
        ChecksContext.Equal("详情：描述", "long text", detail.Description);
        ChecksContext.Equal("详情：体积", 8192L, detail.FileSizeBytes);
        ChecksContext.Equal(
            "详情：更新时间",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000).UtcDateTime,
            detail.UpdatedUtc.UtcDateTime);
        ChecksContext.Equal("详情：文件名", "big_mod.zip", detail.Filename);

        var fallback = SteamWorkshopClient.ParseDetail(DetailJson(null));
        ChecksContext.Equal("详情：没有 description 时回退 file_description", "file desc", fallback?.Description);

        ChecksContext.Check("详情：空数组返回 null", SteamWorkshopClient.ParseDetail("{\"response\":{\"publishedfiledetails\":[]}}") is null);
        ChecksContext.Check("详情：坏 JSON 返回 null", SteamWorkshopClient.ParseDetail("{") is null);
        ChecksContext.Check("详情：null 返回 null", SteamWorkshopClient.ParseDetail(null) is null);
    }

    private static void CheckDetailRequest()
    {
        var http = new CapturingHttpFetcher { FormResponse = DetailJson("long text") };
        var client = new SteamWorkshopClient(http, new NullLogger());

        var result = client.GetDetailAsync(PublishedFileId, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check("详情请求：成功", result.IsSuccess);
        ChecksContext.Equal(
            "详情请求：端点",
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            http.LastFormUri);
        ChecksContext.Equal(
            "详情请求：表单体精确匹配",
            "itemcount=1&publishedfileids[0]=2216623537",
            http.LastFormBody);
        ChecksContext.Equal("详情请求：标题", "Big Mod", result.Value?.Title);
        ChecksContext.Equal("详情请求：只发一次", 1, http.FormCalls);

        // 解析不出来时按 NotFound 报错，而不是返回空详情。
        var broken = new CapturingHttpFetcher { FormResponse = "{\"response\":{\"publishedfiledetails\":[]}}" };
        var failed = new SteamWorkshopClient(broken, new NullLogger())
            .GetDetailAsync(PublishedFileId, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("详情请求：空结果失败", !failed.IsSuccess);
        ChecksContext.Check("详情请求：空结果错误码", (failed.ErrorMessage ?? string.Empty).Contains("not-found", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 探测缓存

    private static void CheckHasWorkshopCache()
    {
        var http = new CapturingHttpFetcher();
        http.Responses["store.steampowered.com/api/appdetails"] = AppDetailsJson(Ck3AppId, true, ["Steam Workshop"]);
        var client = new SteamWorkshopClient(http, new NullLogger());

        var first = client.HasWorkshopAsync(Ck3AppId, CancellationToken.None).GetAwaiter().GetResult();
        var second = client.HasWorkshopAsync(Ck3AppId, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check("探测：成功", first.IsSuccess && second.IsSuccess);
        ChecksContext.Check("探测：CK3 有工坊", first.Value);
        ChecksContext.Check("探测：结果按 appId 缓存，只发一次请求", http.GetCalls == 1, http.GetCalls.ToString(CultureInfo.InvariantCulture));

        var html = BuildBrowseHtml(2);
        http.Responses["steamcommunity.com/workshop/browse/"] = html;
        var page = client.BrowseAsync(
            Ck3AppId,
            new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0),
            CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("浏览：成功", page.IsSuccess);
        ChecksContext.Equal("浏览：条目数", 2, page.Value?.Items.Count ?? -1);

        var noScript = new CapturingHttpFetcher();
        noScript.Responses["steamcommunity.com/workshop/browse/"] = "<html><body>no data</body></html>";
        var parseFail = new SteamWorkshopClient(noScript, new NullLogger())
            .BrowseAsync(Ck3AppId, new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0), CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Check("浏览：结构变了要失败而不是空列表", !parseFail.IsSuccess);
        ChecksContext.Check(
            "浏览：结构变化的错误码",
            (parseFail.ErrorMessage ?? string.Empty).Contains("steam-workshop-parse", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 探测兜底

    private static void CheckHasWorkshopFallback()
    {
        // appdetails 抽风（这里用没配 stub 的 fake 模拟请求失败）时，退一步抓工坊列表页。
        var fallback = new CapturingHttpFetcher();
        fallback.Responses["steamcommunity.com/workshop/browse/"] = BuildBrowseHtml(1);
        var fallbackProbe = new SteamWorkshopClient(fallback, new NullLogger())
            .HasWorkshopAsync(Ck3AppId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("探测兜底：appdetails 失败仍能判定有工坊", fallbackProbe.IsSuccess && fallbackProbe.Value);
        ChecksContext.Equal("探测兜底：先 appdetails 再工坊页", 2, fallback.GetCalls);

        // 列表页也没有 SSR 数据（CS2 那个 302 落地页）→ 没有工坊。
        var landing = new CapturingHttpFetcher();
        landing.Responses["steamcommunity.com/workshop/browse/"] = "<html><body>no data</body></html>";
        var landingProbe = new SteamWorkshopClient(landing, new NullLogger())
            .HasWorkshopAsync(Cs2AppId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("探测兜底：落地页没有 SSR 数据判为没有工坊", landingProbe.IsSuccess && !landingProbe.Value);

        // 两条路都挂 → 失败，交给视图模型退回 PDX 并提示。
        var dead = new CapturingHttpFetcher();
        var deadProbe = new SteamWorkshopClient(dead, new NullLogger())
            .HasWorkshopAsync(Ck3AppId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("探测兜底：两条路都失败时返回错误", !deadProbe.IsSuccess);
        ChecksContext.Check(
            "探测兜底：错误码",
            (deadProbe.ErrorMessage ?? string.Empty).Contains("steam-workshop-probe", StringComparison.Ordinal),
            deadProbe.ErrorMessage);
    }

    // ------------------------------------------------------------ 网络故障

    /// <summary>
    /// 连不上 Steam 时（DNS 被污染 / 代理不可达），必须当成「网络故障」而不是「用户取消」。
    /// <para>
    /// .NET 连不上时抛的是 <see cref="TaskCanceledException"/>（内含 TimeoutException），
    /// 不是 <see cref="HttpRequestException"/>。以前这里没人接，异常一路冒到视图模型，
    /// 又被当成「正常取消」静默吞掉 —— 页面永远停在「正在加载第 1 页…」，列表空白。
    /// </para>
    /// </summary>
    private static void CheckNetworkFailure()
    {
        // 超时（TaskCanceledException + TimeoutException）→ 失败码 steam-workshop-network，不抛异常。
        var timedOut = new CapturingHttpFetcher
        {
            GetException = new TaskCanceledException("connect timeout", new TimeoutException("timeout")),
        };
        var client = new SteamWorkshopClient(timedOut, new NullLogger());
        var result = client
            .BrowseAsync(Ck3AppId, new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("网络故障：超时返回失败而不是抛异常", !result.IsSuccess);
        ChecksContext.Equal(
            "网络故障：超时错误码",
            "steam-workshop-network",
            result.ErrorMessage);
        ChecksContext.Equal("网络故障：超时算未知错误", ErrorKind.Unknown, result.Kind);

        // 网络超时异常（HttpFetcher 翻译出来的那个）同样是网络故障。
        var networkTimeout = new CapturingHttpFetcher
        {
            GetException = new NetworkTimeoutException("steamcommunity.com", "连接超时（30 秒）：steamcommunity.com"),
        };
        var timeoutResult = new SteamWorkshopClient(networkTimeout, new NullLogger())
            .BrowseAsync(Ck3AppId, new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0), CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("网络故障：连接超时返回失败", !timeoutResult.IsSuccess);
        ChecksContext.Equal(
            "网络故障：连接超时错误码",
            "steam-workshop-network",
            timeoutResult.ErrorMessage);

        // 真正的用户取消必须继续往上抛，不能被当成「连不上」。
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledFetcher = new CapturingHttpFetcher
        {
            GetException = new OperationCanceledException(cancelled.Token),
        };
        var cancelledClient = new SteamWorkshopClient(cancelledFetcher, new NullLogger());
        var threw = false;
        try
        {
            cancelledClient
                .BrowseAsync(Ck3AppId, new SteamWorkshopQuery(null, SteamWorkshopSort.Trend, 0), cancelled.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        ChecksContext.Check("网络故障：用户取消仍然抛取消异常", threw);
    }

    // ------------------------------------------------------------ 白名单

    private static void CheckOutboundWhitelist()
    {
        ChecksContext.Check("白名单：steamcommunity.com 本身允许", OnlineHosts.IsAllowedHost("steamcommunity.com"));
        ChecksContext.Check("白名单：api.steampowered.com 允许", OnlineHosts.IsAllowedHost("api.steampowered.com"));
        ChecksContext.Check("白名单：伪造子域不允许", !OnlineHosts.IsAllowedHost("evil.steamcommunity.com"));
        ChecksContext.Check("白名单：后缀域名不允许", !OnlineHosts.IsAllowedHost("steamcommunity.com.evil.test"));

        // Steam 创意工坊的简介配图大量走 Imgur，这两个主机必须放行，但只放行精确主机。
        ChecksContext.Check("白名单：i.imgur.com 允许（工坊简介配图）", OnlineHosts.IsAllowedHost("i.imgur.com"));
        ChecksContext.Check("白名单：imgur.com 允许", OnlineHosts.IsAllowedHost("imgur.com"));
        ChecksContext.Check("白名单：imgur 子域不额外放行", !OnlineHosts.IsAllowedHost("evil.imgur.com"));

        // PostFormAsync 也必须过同一道白名单门：越界在发请求前就抛。
        var fetcher = new HttpFetcher(new NullLogger());
        try
        {
            fetcher.PostFormAsync(new Uri("https://evil.example.com/form"), "a=1", CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("表单 POST：越界主机被拒", false);
        }
        catch (HttpRequestException ex)
        {
            ChecksContext.Check(
                "表单 POST：越界主机被拒",
                ex.Message.StartsWith("host-not-allowed:", StringComparison.Ordinal),
                ex.Message);
        }
    }

    // ------------------------------------------------------------ 测试数据

    private static string BuildBrowseHtml(int itemCount)
    {
        var results = new JsonArray();
        for (var i = 0; i < itemCount; i++)
        {
            var id = (1001 + i).ToString(CultureInfo.InvariantCulture);
            results.Add(new JsonObject
            {
                ["publishedfileid"] = id,
                ["title"] = i == 0 ? "Mod A" : "Mod B",
                ["short_description"] = i == 0 ? "short a" : "short b",
                ["preview_url"] = "https://images.steamusercontent.com/a.jpg",
                ["creator"] = "76561197960265728",
                ["subscriptions"] = 1234,
                ["favorited"] = 11,
                ["lifetime_subscriptions"] = 2345,
                ["views"] = 98765,
                ["time_created"] = 1_600_000_000,
                ["time_updated"] = 1_700_000_000,
                ["file_size"] = "4096", // 实测是字符串
                ["star_rating"] = 4.5,
                ["total_votes"] = 42,
                ["consumer_appid"] = Ck3AppId,
                ["tags"] = new JsonArray
                {
                    new JsonObject { ["tag"] = "Economic", ["display_name"] = "Economy" },
                    new JsonObject { ["tag"] = "Gameplay", ["display_name"] = "Gameplay" },
                },
            });
        }

        var queryData = new JsonObject
        {
            ["queries"] = new JsonArray
            {
                new JsonObject
                {
                    ["state"] = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["total_count"] = itemCount,
                            ["total_pages"] = 1,
                            ["results"] = results,
                        },
                    },
                },
            },
        };

        return WrapSsHtml(queryData.ToJsonString());
    }

    private static string BuildFlatHtml()
    {
        var queryData = new JsonObject
        {
            ["total_count"] = 1,
            ["total_pages"] = 1,
            ["results"] = new JsonArray { new JsonObject { ["publishedfileid"] = "7", ["title"] = "Flat" } },
        };

        return WrapSsHtml(queryData.ToJsonString());
    }

    private static string BuildTopLevelHtml()
    {
        var queryData = new JsonObject
        {
            ["mutations"] = new JsonArray(),
            ["queries"] = new JsonArray
            {
                new JsonObject
                {
                    ["queryKey"] = new JsonArray { "AOWarningCookie" },
                    ["state"] = new JsonObject { ["data"] = null, ["status"] = "success" },
                },
                new JsonObject
                {
                    ["queryKey"] = new JsonArray { "apphub", Ck3AppId },
                    ["state"] = new JsonObject { ["data"] = new JsonObject { ["appid"] = Ck3AppId } },
                },
                new JsonObject
                {
                    ["queryKey"] = new JsonArray
                    {
                        "workshop_browse",
                        new JsonObject { ["appid"] = Ck3AppId, ["page"] = 1, ["num_per_page"] = 30 },
                        1,
                    },
                    ["state"] = new JsonObject
                    {
                        ["data"] = new JsonObject
                        {
                            ["eresult"] = 1,
                            ["current_page"] = 1,
                            ["total_pages"] = 591,
                            ["total_count"] = 17717,
                            ["results"] = new JsonArray
                            {
                                new JsonObject
                                {
                                    ["publishedfileid"] = "3827420629",
                                    ["title"] = "Shadows of the Cross",
                                    ["file_size"] = "27378997",
                                    ["creator"] = "76561197960281284",
                                    ["subscriptions"] = 469,
                                    ["favorited"] = 118,
                                    ["views"] = 2129,
                                    ["time_created"] = 1792975210,
                                    ["time_updated"] = 1793054096,
                                    ["star_rating"] = 4,
                                    ["total_votes"] = 31,
                                    ["consumer_appid"] = Ck3AppId,
                                },
                            },
                        },
                    },
                },
            },
        };

        var ssr = new JsonObject
        {
            ["route"] = "/workshop/browse",
            ["renderContext"] = new JsonObject { ["queryData"] = queryData.ToJsonString() },
        };

        return "<html><body><script type=\"application/json\" id=\"valve-ssr-data\" nonce=\"x\">" +
            ssr.ToJsonString() +
            "</script></body></html>";
    }

    private static string BuildGarbageHtml() =>
        "<html><body><script type=\"application/json\" id=\"valve-ssr-data\">" +
        "{\"props\":{\"pageProps\":{\"renderContext\":{\"queryData\":\"{not json\"}}}}" +
        "</script></body></html>";

    private static string WrapSsHtml(string queryDataJson)
    {
        var ssr = new JsonObject
        {
            ["props"] = new JsonObject
            {
                ["pageProps"] = new JsonObject
                {
                    ["renderContext"] = new JsonObject { ["queryData"] = queryDataJson },
                },
            },
        };

        return "<html><body><script type=\"application/json\" id=\"valve-ssr-data\">" +
            ssr.ToJsonString() +
            "</script></body></html>";
    }

    private static string AppDetailsJson(int appId, bool success, string[] categories)
    {
        var categoryArray = new JsonArray();
        for (var i = 0; i < categories.Length; i++)
        {
            categoryArray.Add(new JsonObject { ["id"] = i, ["description"] = categories[i] });
        }

        var root = new JsonObject
        {
            [appId.ToString(CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["success"] = success,
                ["data"] = new JsonObject
                {
                    ["steam_appid"] = appId,
                    ["categories"] = categoryArray,
                },
            },
        };

        return root.ToJsonString();
    }

    private static string DetailJson(string? description)
    {
        var item = new JsonObject
        {
            ["publishedfileid"] = PublishedFileId,
            ["title"] = "Big Mod",
            ["file_size"] = "8192",
            ["preview_url"] = "https://images.steamusercontent.com/big.jpg",
            ["time_created"] = 1_600_000_000,
            ["time_updated"] = 1_700_000_000,
            ["filename"] = "big_mod.zip",
            ["file_description"] = "file desc",
        };

        if (description is not null)
        {
            item["description"] = description;
        }

        var root = new JsonObject
        {
            ["response"] = new JsonObject
            {
                ["result"] = 1,
                ["publishedfiledetails"] = new JsonArray { item },
            },
        };

        return root.ToJsonString();
    }

    // ------------------------------------------------------------ fake

    /// <summary>记录请求并回放固定响应的 HTTP 抽象；PostJsonAsync 不该被创意工坊用到。</summary>
    private sealed class CapturingHttpFetcher : IHttpFetcher
    {
        public Dictionary<string, string> Responses { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// 让 GetAsync 抛出的异常（模拟连不上 / 超时）。优先于 <see cref="Responses"/> 回放。
        /// </summary>
        public Exception? GetException { get; set; }

        public string? LastFormUri { get; private set; }

        public string? LastFormBody { get; private set; }

        public string? FormResponse { get; set; }

        public int GetCalls { get; private set; }

        public int FormCalls { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCalls++;
            if (GetException is not null)
            {
                throw GetException;
            }

            var key = uri.Host + uri.AbsolutePath;
            foreach (var pair in Responses)
            {
                if (key.StartsWith(pair.Key, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(pair.Value)));
                }
            }

            throw new HttpRequestException("no-stub:" + uri.Host + uri.AbsolutePath);
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException("创意工坊不发 JSON POST");

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken)
        {
            FormCalls++;
            LastFormUri = uri.AbsoluteUri;
            LastFormBody = formBody;
            return Task.FromResult(FormResponse ?? "{}");
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
