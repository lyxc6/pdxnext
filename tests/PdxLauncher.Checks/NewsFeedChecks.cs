namespace PdxLauncher.Checks;

using System.Net.Http;
using System.Text;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 官方公告（Braze content cards）自检：JSON 解析、可见时间窗与权重、缺 slot 容错、
/// 404 负缓存、离线不联网、6 小时 TTL、30 分钟失败退避、图片落盘与复用、
/// 图片域不可达时的短路 + 5 分钟重试、缓存往返。
/// 全部 HTTP 与素材下载均通过 fake 注入，绝不真联网。
/// </summary>
public static class NewsFeedChecks
{
    private const string Ck3Fixture = "online/contentcards-ck3.json";
    private const string ExpiredFixture = "online/contentcards-expired.json";
    private const string MinimalFixture = "online/contentcards-minimal.json";
    private const string BrokenFixture = "online/contentcards-broken.json";

    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdx-news-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckParse(fs, tempRoot);
            CheckVisibility(fs, tempRoot);
            CheckMissingSlots(fs, tempRoot);
            CheckNotFound(fs, tempRoot);
            CheckOffline(fs, tempRoot);
            CheckTtl(fs, tempRoot);
            CheckFailureBackoff(fs, tempRoot);
            CheckImages(fs, tempRoot);
            CheckImageFailureRetry(fs, tempRoot);
            CheckImageFastFailure(fs, tempRoot);
            CheckCacheRoundTrip(fs, tempRoot);
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

    // ------------------------------------------------------------ 解析

    private static void CheckParse(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        ChecksContext.Check("公告 fixture 存在且非空", json.Length > 1000, json.Length + " B");

        var logger = new RecordingLogger();
        var http = new FakeHttpFetcher(_ => JsonStream(json));
        var images = new FakeAssetDownloader(fs);
        var service = new NewsFeedService(
            fs,
            http,
            images,
            new FakeClock(Now),
            logger,
            Sub(fs, tempRoot, "parse"));

        var feed = Fetch(service, "ck3");
        ChecksContext.Equal("解析出 3 张公告", 3, feed.Items.Count);
        ChecksContext.Equal("公告顺序是 main/secondary-1/secondary-2", "main,secondary-1,secondary-2", Slots(feed));

        var main = feed.Find(NewsFeedSlots.Main);
        ChecksContext.Check("含 main 卡", main is not null);
        ChecksContext.Check(
            "main 图 URL 是 gif",
            main?.ImageUrl is not null && main.ImageUrl.Contains("original.gif", StringComparison.Ordinal),
            main?.ImageUrl ?? "null");
        ChecksContext.Check("main 空标题归一为 null", main?.Title is null);
        ChecksContext.Check("main 空正文归一为 null", main?.Text is null);
        ChecksContext.Check("main 链接可打开", main?.IsOpenable == true);
        ChecksContext.Check(
            "main 图已落盘",
            main?.ImagePath is not null && fs.FileExists(main.ImagePath),
            main?.ImagePath ?? "null");

        var secondary1 = feed.Find(NewsFeedSlots.Secondary1);
        ChecksContext.Check("secondary-1 有本地图", secondary1?.HasImage == true);
        var secondary2 = feed.Find(NewsFeedSlots.Secondary2);
        ChecksContext.Equal("secondary-2 是 pdxlaun 深链", "pdxlaun://accountonboarding", secondary2?.Link);
        ChecksContext.Check("pdxlaun 深链不可交给浏览器", secondary2?.IsOpenable == false);

        ChecksContext.Equal("接口只请求一次", 1, http.GetCallCount);
        ChecksContext.Equal("三张图各下载一次", 3, images.CallCount);
        ChecksContext.Check("缓存文件已写盘", fs.FileExists(service.CachePath));
        ChecksContext.Check(
            "缓存 JSON 是 camelCase",
            fs.FileExists(service.CachePath) && fs.ReadAllText(service.CachePath).Contains("imageUrl", StringComparison.Ordinal));
        ChecksContext.Check("首次获取不产生告警", logger.Warnings.Count == 0, string.Join(" | ", logger.Warnings));
    }

    // -------------------------------------------------------- 可见时间窗与权重

    private static void CheckVisibility(IFileSystem fs, string tempRoot)
    {
        var expiredJson = LoadJson(fs, ExpiredFixture);
        var expiredLogger = new RecordingLogger();
        var expired = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(expiredJson)),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            expiredLogger,
            Sub(fs, tempRoot, "visible-expired"));
        var expiredFeed = Fetch(expired, "ck3");
        ChecksContext.Check("全部超出时间窗 → 没有卡片", expiredFeed.IsEmpty);
        ChecksContext.Check("时间窗失效不算错误", expiredLogger.Warnings.Count == 0, string.Join(" | ", expiredLogger.Warnings));

        var minimalJson = LoadJson(fs, MinimalFixture);
        var minimal = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(minimalJson)),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            new RecordingLogger(),
            Sub(fs, tempRoot, "visible-minimal"));
        var minimalFeed = Fetch(minimal, "ck3");
        ChecksContext.Equal("只有 main 也能显示", 1, minimalFeed.Items.Count);
        var onlyMain = minimalFeed.Find(NewsFeedSlots.Main);
        ChecksContext.Equal("标题取自 title.en", "Patch 1.10", onlyMain?.Title);
        ChecksContext.Check("正文取自 text.en", onlyMain?.HasText == true);
        ChecksContext.Check("没有图时 HasImage 为假", onlyMain?.HasImage == false);

        const string weighted = """
            {
              "main": {
                "contentGroups": [
                  {
                    "settings": { "weight": 1, "visible": { "from": "2024-01-01T00:00:00Z", "until": "2099-12-31T23:59:59Z" } },
                    "contentItems": [ { "content": { "title": { "en": "低权重" } } } ]
                  },
                  {
                    "settings": { "weight": 5, "visible": { "from": "2024-01-01T00:00:00Z", "until": "2099-12-31T23:59:59Z" } },
                    "contentItems": [ { "content": { "title": { "en": "高权重" } } } ]
                  }
                ]
              }
            }
            """;
        var weightedFeed = Fetch(
            new NewsFeedService(
                fs,
                new FakeHttpFetcher(_ => JsonStream(weighted)),
                new FakeAssetDownloader(fs),
                new FakeClock(Now),
                new RecordingLogger(),
                Sub(fs, tempRoot, "visible-weight")),
            "ck3");
        ChecksContext.Equal("同 slot 取 weight 大的组", "高权重", weightedFeed.Find(NewsFeedSlots.Main)?.Title);
    }

    // ------------------------------------------------------------ 缺 slot 容错

    private static void CheckMissingSlots(IFileSystem fs, string tempRoot)
    {
        var cases = new (string Label, string Json)[]
        {
            ("顶层空对象", """{}"""),
            ("slot 无 contentGroups", """{"main":{}}"""),
            ("contentGroups 为空", """{"main":{"contentGroups":[]}}"""),
            ("contentItem 无 content", """{"main":{"contentGroups":[{"contentItems":[{"id":"x"}]}]}}"""),
            ("内容字段全空", """{"main":{"contentGroups":[{"contentItems":[{"content":{"title":{"en":""},"text":{"en":""}}}]}]}}"""),
        };

        var missingRoot = Sub(fs, tempRoot, "missing");
        var index = 0;
        foreach (var (label, json) in cases)
        {
            index++;
            var logger = new RecordingLogger();
            var service = new NewsFeedService(
                fs,
                new FakeHttpFetcher(_ => JsonStream(json)),
                new FakeAssetDownloader(fs),
                new FakeClock(Now),
                logger,
                missingRoot);
            var feed = Fetch(service, "missing" + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            ChecksContext.Check(label + " → 空结果不抛", feed.IsEmpty);
            ChecksContext.Check(label + " → 没有失败告警", logger.Warnings.Count == 0, string.Join(" | ", logger.Warnings));
        }

        const string partial = """{"secondary-1":{"contentGroups":[{"contentItems":[{"content":{"title":{"en":"次要卡"}}}]}]}}""";
        var partialFeed = Fetch(
            new NewsFeedService(
                fs,
                new FakeHttpFetcher(_ => JsonStream(partial)),
                new FakeAssetDownloader(fs),
                new FakeClock(Now),
                new RecordingLogger(),
                missingRoot),
            "partial");
        ChecksContext.Equal("只有次要卡也能解析", 1, partialFeed.Items.Count);
        ChecksContext.Equal("次要卡 slot 正确", "secondary-1", partialFeed.Items[0].Slot);

        var emptyIdHttp = new FakeHttpFetcher(_ => throw new HttpRequestException("不该联网"));
        var emptyId = new NewsFeedService(
            fs,
            emptyIdHttp,
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            new RecordingLogger(),
            missingRoot);
        ChecksContext.Check("空 gameId 直接返回空", Fetch(emptyId, string.Empty).IsEmpty);
        ChecksContext.Equal("空 gameId 不联网", 0, emptyIdHttp.GetCallCount);
    }

    // ------------------------------------------------------------ 404 负缓存

    private static void CheckNotFound(IFileSystem fs, string tempRoot)
    {
        var cacheRoot = Sub(fs, tempRoot, "notfound");
        var logger = new RecordingLogger();
        var service = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => throw new HttpRequestException("HTTP 404 Not Found")),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            logger,
            cacheRoot);

        var feed = Fetch(service, "victoria3");
        ChecksContext.Check("404 视为没有公告", feed.IsEmpty);
        ChecksContext.Check(
            "404 记一条 info",
            logger.Infos.Any(message => message.Contains("没有官方公告", StringComparison.Ordinal)),
            string.Join(" | ", logger.Infos));
        ChecksContext.Check("404 不算失败告警", logger.Warnings.Count == 0, string.Join(" | ", logger.Warnings));
        ChecksContext.Check(
            "404 负缓存已写盘",
            fs.FileExists(service.CachePath) && fs.ReadAllText(service.CachePath).Contains("victoria3", StringComparison.Ordinal));

        var secondHttp = new FakeHttpFetcher(_ => throw new HttpRequestException("HTTP 500 不该发生的重试"));
        var second = new NewsFeedService(
            fs,
            secondHttp,
            new FakeAssetDownloader(fs),
            new FakeClock(Now.AddHours(1)),
            new RecordingLogger(),
            cacheRoot);
        var secondFeed = Fetch(second, "victoria3");
        ChecksContext.Equal("负缓存 6 小时内不再请求", 0, secondHttp.GetCallCount);
        ChecksContext.Check("负缓存也返回空", secondFeed.IsEmpty);
    }

    // ------------------------------------------------------------ 关闭联网

    private static void CheckOffline(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var cacheRoot = Sub(fs, tempRoot, "offline");

        var offlineHttp = new FakeHttpFetcher(_ => throw new HttpRequestException("关闭联网时不该请求"));
        var offlineLogger = new RecordingLogger();
        var offline = new NewsFeedService(
            fs,
            offlineHttp,
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            offlineLogger,
            cacheRoot);
        var offlineFeed = Fetch(offline, "ck3", allowOnline: false);
        ChecksContext.Check("关闭联网且无缓存 → 空结果", offlineFeed.IsEmpty);
        ChecksContext.Equal("关闭联网不发请求", 0, offlineHttp.GetCallCount);
        ChecksContext.Check(
            "关闭联网写 info 日志",
            offlineLogger.Infos.Any(message => message.Contains("已关闭联网", StringComparison.Ordinal)),
            string.Join(" | ", offlineLogger.Infos));

        var warmHttp = new FakeHttpFetcher(_ => JsonStream(json));
        var warm = new NewsFeedService(
            fs,
            warmHttp,
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            new RecordingLogger(),
            cacheRoot);
        var warmFeed = Fetch(warm, "ck3");
        ChecksContext.Equal("联网预热到 3 张", 3, warmFeed.Items.Count);
        ChecksContext.Equal("预热请求 1 次", 1, warmHttp.GetCallCount);

        var offline2Http = new FakeHttpFetcher(_ => throw new HttpRequestException("关闭联网时不该请求"));
        var offline2 = new NewsFeedService(
            fs,
            offline2Http,
            new FakeAssetDownloader(fs),
            new FakeClock(Now.AddMinutes(5)),
            new RecordingLogger(),
            cacheRoot);
        var cachedFeed = Fetch(offline2, "ck3", allowOnline: false);
        ChecksContext.Equal("关闭联网仍显示缓存", 3, cachedFeed.Items.Count);
        ChecksContext.Equal("关闭联网读缓存不联网", 0, offline2Http.GetCallCount);
    }

    // ------------------------------------------------------------ 6 小时 TTL

    private static void CheckTtl(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var clock = new FakeClock(Now);
        var http = new FakeHttpFetcher(_ => JsonStream(json));
        var service = new NewsFeedService(
            fs,
            http,
            new FakeAssetDownloader(fs),
            clock,
            new RecordingLogger(),
            Sub(fs, tempRoot, "ttl"));

        ChecksContext.Equal("首次获取 3 张", 3, Fetch(service, "ck3").Items.Count);
        ChecksContext.Equal("首次请求 1 次", 1, http.GetCallCount);

        clock.Advance(TimeSpan.FromHours(1));
        ChecksContext.Equal("1 小时内仍返回 3 张", 3, Fetch(service, "ck3").Items.Count);
        ChecksContext.Equal("1 小时内不请求", 1, http.GetCallCount);

        clock.Advance(TimeSpan.FromHours(6));
        ChecksContext.Equal("7 小时后重新抓到 3 张", 3, Fetch(service, "ck3").Items.Count);
        ChecksContext.Equal("7 小时后请求 2 次", 2, http.GetCallCount);
    }

    // ----------------------------------------------------- 30 分钟失败退避

    private static void CheckFailureBackoff(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var clock = new FakeClock(Now);
        var http = new FakeHttpFetcher(_ => JsonStream(json));
        var logger = new RecordingLogger();
        var service = new NewsFeedService(
            fs,
            http,
            new FakeAssetDownloader(fs),
            clock,
            logger,
            Sub(fs, tempRoot, "backoff"));

        ChecksContext.Equal("初始 3 张", 3, Fetch(service, "ck3").Items.Count);

        clock.Advance(TimeSpan.FromHours(7));
        http.Handler = _ => throw new HttpRequestException("HTTP 500 boom");
        var afterFailure = Fetch(service, "ck3");
        ChecksContext.Equal("失败时沿用旧卡片", 3, afterFailure.Items.Count);
        ChecksContext.Equal("失败请求累计 2 次", 2, http.GetCallCount);
        ChecksContext.Check(
            "失败记 warn",
            logger.Warnings.Any(message => message.Contains("联网获取失败", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));

        Fetch(service, "ck3");
        ChecksContext.Equal("30 分钟内不再重试", 2, http.GetCallCount);
        ChecksContext.Check(
            "退避写 info 日志",
            logger.Infos.Any(message => message.Contains("不足", StringComparison.Ordinal)),
            string.Join(" | ", logger.Infos));

        clock.Advance(TimeSpan.FromMinutes(31));
        Fetch(service, "ck3");
        ChecksContext.Equal("超过 30 分钟恢复重试", 3, http.GetCallCount);
    }

    // ------------------------------------------------------------ 图片落盘

    private static void CheckImages(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var clock = new FakeClock(Now);
        var images = new FakeAssetDownloader(fs);
        var service = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(json)),
            images,
            clock,
            new RecordingLogger(),
            Sub(fs, tempRoot, "images"));

        var feed = Fetch(service, "ck3");
        ChecksContext.Equal("三张图各下载一次", 3, images.CallCount);
        ChecksContext.Check(
            "图片落在 news-cache/images 下",
            feed.Items.All(item => item.HasImage && item.ImagePath is not null && item.ImagePath.Contains("images", StringComparison.Ordinal)));
        ChecksContext.Check(
            "图片文件真实存在",
            feed.Items.All(item => item.HasImage && item.ImagePath is not null && fs.FileExists(item.ImagePath)));
        ChecksContext.Check(
            "只下载白名单域名",
            images.Urls.Count == 3 && images.Urls.All(url => url.Contains("braze-images.com", StringComparison.Ordinal)),
            string.Join(" | ", images.Urls));

        clock.Advance(TimeSpan.FromHours(7));
        Fetch(service, "ck3");
        ChecksContext.Equal("再抓一次时复用已下载的图", 3, images.CallCount);

        var failRoot = Sub(fs, tempRoot, "images-fail");
        var failLogger = new RecordingLogger();
        var failing = new FakeAssetDownloader(fs) { FailEverything = true };
        var failService = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(json)),
            failing,
            new FakeClock(Now),
            failLogger,
            failRoot);
        var failFeed = Fetch(failService, "ck3");
        ChecksContext.Equal("图片全失败仍有 3 张卡片", 3, failFeed.Items.Count);
        ChecksContext.Check("图片失败时 ImagePath 为 null", failFeed.Items.All(item => item.ImagePath is null));
        ChecksContext.Check(
            "图片失败记 info",
            failLogger.Infos.Any(message => message.Contains("下载失败", StringComparison.Ordinal)),
            string.Join(" | ", failLogger.Infos));
    }

    // ------------------------------------------------------------ 图片失败重试

    private static void CheckImageFailureRetry(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var cacheRoot = Sub(fs, tempRoot, "image-retry");
        var clock = new FakeClock(Now);
        var http = new FakeHttpFetcher(_ => JsonStream(json));
        var images = new FakeAssetDownloader(fs) { FailAsTimeout = true };
        var logger = new RecordingLogger();
        var service = new NewsFeedService(fs, http, images, clock, logger, cacheRoot);

        var first = Fetch(service, "ck3");
        ChecksContext.Equal("图片超时仍解析出 3 张卡片", 3, first.Items.Count);
        ChecksContext.Check("图片超时时全部无本地图", first.Items.All(item => item.ImagePath is null));
        ChecksContext.Equal("首张图超时后不再尝试剩余图片", 1, images.CallCount);
        ChecksContext.Check(
            "图片超时记 info",
            logger.Infos.Any(message => message.Contains("图片未取到", StringComparison.Ordinal)),
            string.Join(" | ", logger.Infos));

        var cacheText = fs.ReadAllText(service.CachePath);
        ChecksContext.Check(
            "图片失败写 lastImageFailureUtc",
            cacheText.Contains("lastImageFailureUtc", StringComparison.Ordinal),
            cacheText);
        ChecksContext.Check(
            "图片失败不推进 fetchedUtc",
            cacheText.Contains("0001-01-01", StringComparison.Ordinal),
            cacheText);

        clock.Advance(TimeSpan.FromMinutes(1));
        Fetch(service, "ck3");
        ChecksContext.Equal("图片失败 5 分钟内不重试接口", 1, http.GetCallCount);
        ChecksContext.Equal("图片失败 5 分钟内不重试图片", 1, images.CallCount);

        clock.Advance(TimeSpan.FromMinutes(5));
        Fetch(service, "ck3");
        ChecksContext.Equal("超过 5 分钟重新请求接口", 2, http.GetCallCount);
        ChecksContext.Equal("超过 5 分钟重新尝试图片", 2, images.CallCount);

        images.FailAsTimeout = false;
        clock.Advance(TimeSpan.FromMinutes(5));
        var recovered = Fetch(service, "ck3");
        ChecksContext.Equal("图片域恢复后 3 张卡都有图", 3, recovered.Items.Count(item => item.HasImage));
        ChecksContext.Equal("恢复后补齐 3 张图", 5, images.CallCount);

        clock.Advance(TimeSpan.FromHours(1));
        Fetch(service, "ck3");
        ChecksContext.Equal("图片补齐后 1 小时内不联网", 3, http.GetCallCount);
    }

    private static void CheckImageFastFailure(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var cacheRoot = Sub(fs, tempRoot, "image-fast-fail");
        var clock = new FakeClock(Now);
        var http = new FakeHttpFetcher(_ => JsonStream(json));
        var service = new NewsFeedService(
            fs,
            http,
            new FakeAssetDownloader(fs) { FailEverything = true },
            clock,
            new RecordingLogger(),
            cacheRoot);

        var feed = Fetch(service, "ck3");
        ChecksContext.Equal("图片 404 快速失败仍有 3 张卡片", 3, feed.Items.Count);

        var cacheText = fs.ReadAllText(service.CachePath);
        ChecksContext.Check(
            "快速失败不写 lastImageFailureUtc",
            !cacheText.Contains("lastImageFailureUtc", StringComparison.Ordinal),
            cacheText);

        clock.Advance(TimeSpan.FromMinutes(10));
        Fetch(service, "ck3");
        ChecksContext.Equal("快速失败仍按成功缓存 6 小时", 1, http.GetCallCount);
    }

    // ------------------------------------------------------------ 缓存往返

    private static void CheckCacheRoundTrip(IFileSystem fs, string tempRoot)
    {
        var json = LoadJson(fs, Ck3Fixture);
        var cacheRoot = Sub(fs, tempRoot, "roundtrip");
        var writer = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(json)),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            new RecordingLogger(),
            cacheRoot);
        var first = Fetch(writer, "ck3");
        ChecksContext.Equal("首次解析 3 张", 3, first.Items.Count);

        var readerHttp = new FakeHttpFetcher(_ => throw new HttpRequestException("缓存命中时不该联网"));
        var reader = new NewsFeedService(
            fs,
            readerHttp,
            new FakeAssetDownloader(fs),
            new FakeClock(Now.AddHours(1)),
            new RecordingLogger(),
            cacheRoot);
        var second = Fetch(reader, "ck3");
        ChecksContext.Equal("新实例从缓存读回 3 张", 3, second.Items.Count);
        ChecksContext.Equal("缓存读回顺序一致", Slots(first), Slots(second));
        ChecksContext.Equal("缓存读回深链一致", first.Find(NewsFeedSlots.Secondary2)?.Link, second.Find(NewsFeedSlots.Secondary2)?.Link);
        ChecksContext.Equal("缓存命中不联网", 0, readerHttp.GetCallCount);

        fs.WriteAllTextAtomic(writer.CachePath, "this is not json");
        var brokenCacheLogger = new RecordingLogger();
        var brokenCache = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(json)),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            brokenCacheLogger,
            cacheRoot);
        var brokenCacheFeed = Fetch(brokenCache, "ck3");
        ChecksContext.Equal("坏缓存不抛异常并重新抓取 3 张", 3, brokenCacheFeed.Items.Count);
        ChecksContext.Check(
            "坏缓存记 warn",
            brokenCacheLogger.Warnings.Any(message => message.Contains("缓存解析失败", StringComparison.Ordinal)),
            string.Join(" | ", brokenCacheLogger.Warnings));

        var brokenJsonLogger = new RecordingLogger();
        var brokenHttp = new NewsFeedService(
            fs,
            new FakeHttpFetcher(_ => JsonStream(LoadJson(fs, BrokenFixture))),
            new FakeAssetDownloader(fs),
            new FakeClock(Now),
            brokenJsonLogger,
            Sub(fs, tempRoot, "broken-http"));
        var brokenJsonFeed = Fetch(brokenHttp, "ck3");
        ChecksContext.Check("接口返回截断 JSON → 空结果不抛", brokenJsonFeed.IsEmpty);
        ChecksContext.Check(
            "截断 JSON 记 warn",
            brokenJsonLogger.Warnings.Any(message => message.Contains("联网获取失败", StringComparison.Ordinal)),
            string.Join(" | ", brokenJsonLogger.Warnings));
    }

    // ------------------------------------------------------------ 工具

    private static string Sub(IFileSystem fs, string root, string name)
    {
        var path = Path.Combine(root, name);
        fs.EnsureDirectory(path);
        return path;
    }

    private static string LoadJson(IFileSystem fs, string relative)
    {
        var path = ChecksContext.FixturePath(relative);
        return fs.FileExists(path) ? fs.ReadAllText(path) : string.Empty;
    }

    private static NewsFeed Fetch(NewsFeedService service, string gameId, bool allowOnline = true) =>
        service.GetAsync(CreateGame(gameId), allowOnline, CancellationToken.None).GetAwaiter().GetResult();

    private static string Slots(NewsFeed feed) => string.Join(",", feed.Items.Select(item => item.Slot));

    private static Stream JsonStream(string json) => new MemoryStream(Encoding.UTF8.GetBytes(json));

    private static GameInstallation CreateGame(string gameId) => new(
        GameId: gameId,
        DisplayName: gameId,
        Version: "test",
        InstallRoot: Path.Combine(Path.GetTempPath(), "pdx-news-game"),
        LauncherSettingsPath: Path.Combine(Path.GetTempPath(), "pdx-news-game", "launcher-settings.json"),
        Format: LoadFileFormat.DlcLoadV0,
        GameDataDir: Path.Combine(Path.GetTempPath(), "pdx-news-game", "data"),
        DlcPath: Path.Combine(Path.GetTempPath(), "pdx-news-game", "dlc"),
        ModDir: Path.Combine(Path.GetTempPath(), "pdx-news-game", "mod"),
        ExePath: Path.Combine(Path.GetTempPath(), "pdx-news-game", "binaries", "game.exe"),
        ExeArgs: Array.Empty<string>(),
        Alternatives: Array.Empty<AlternativeExecutable>(),
        Theme: new ThemeSpec(
            Background: null,
            Logo: null,
            AppIcon: null,
            AppTaskbarIcon: null,
            ButtonBackground: null,
            ButtonFont: null,
            ButtonTextColor: "#FFFFFF",
            ButtonHoverTextColor: "#FFFFFF",
            FontSize: 14,
            FontWeight: 400,
            TextShadow: null,
            Source: ThemeAssetSource.BuiltInPlaceholder),
        BrowserDlcUrl: null,
        BrowserModUrl: null,
        SteamAppId: null,
        Support: GameSupport.Full);

    // ------------------------------------------------------------ 替身

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = new();

        public List<string> Warnings { get; } = new();

        public List<string> Errors { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class FakeHttpFetcher : IHttpFetcher
    {
        public FakeHttpFetcher(Func<Uri, Stream> handler) => Handler = handler;

        public Func<Uri, Stream> Handler { get; set; }

        public int GetCallCount { get; private set; }

        public Uri? LastUri { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCallCount++;
            LastUri = uri;
            return Task.FromResult(Handler(uri));
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult("{}");

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException("未使用表单 POST");
    }

    private sealed class FakeAssetDownloader : IAssetDownloader
    {
        private static readonly byte[] PngBytes = BuildPng();

        private readonly IFileSystem _fs;

        public FakeAssetDownloader(IFileSystem fs, bool failEverything = false)
        {
            _fs = fs;
            FailEverything = failEverything;
        }

        public int CallCount { get; private set; }

        public bool FailEverything { get; set; }

        /// <summary>模拟图片域不可达：抛 OperationCanceledException（调用方并未取消），服务应把它当超时处理。</summary>
        public bool FailAsTimeout { get; set; }

        public List<string> Urls { get; } = new();

        public Task<string?> DownloadAsync(
            string url,
            string targetPathWithoutExtension,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Urls.Add(url);
            if (FailAsTimeout)
            {
                return Task.FromException<string?>(new OperationCanceledException("connect timeout"));
            }

            if (FailEverything)
            {
                return Task.FromResult<string?>(null);
            }

            var path = targetPathWithoutExtension + ".png";
            var directory = _fs.GetDirectoryName(path);
            if (directory is not null)
            {
                _fs.EnsureDirectory(directory);
            }

            _fs.WriteAllBytesAtomic(path, PngBytes);
            return Task.FromResult<string?>(path);
        }

        private static byte[] BuildPng()
        {
            var bytes = new byte[2048];
            bytes[0] = 0x89;
            bytes[1] = 0x50;
            bytes[2] = 0x4E;
            bytes[3] = 0x47;
            for (var i = 4; i < bytes.Length; i++)
            {
                bytes[i] = 0x5A;
            }

            return bytes;
        }
    }
}
