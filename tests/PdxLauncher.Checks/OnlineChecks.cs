namespace PdxLauncher.Checks;

using System.Net.Http;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 联网层自检（不真联网）：GraphQL 解析、域名白名单、magic bytes 判型、.part 清理、
/// 代理优先级、主题三层合并与 5 天 TTL。所有 HTTP 均通过 fake IHttpFetcher 注入。
/// </summary>
public static class OnlineChecks
{
    private const string FixtureRelativePath = "online/graphql-response.json";
    private const string AssetPrefix = "https://metadata-assets.paradox-interactive.com/";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-online-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(tempRoot);
            CheckMetadataParsing(fs);
            CheckAssetFileTypeDetection();
            CheckWhitelist(fs, tempRoot);
            CheckDownloader(fs, tempRoot);
            CheckProxyResolver();
            CheckThemeResolver(fs, tempRoot);
            CheckThemeRefreshRetry(fs, tempRoot);
            CheckWashLayerPolicy();
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

    // ---------------------------------------------------------------- GraphQL

    private static void CheckMetadataParsing(IFileSystem fs)
    {
        var fixturePath = ChecksContext.FixturePath(FixtureRelativePath);
        if (!fs.FileExists(fixturePath))
        {
            ChecksContext.Check("GraphQL fixture 存在", false, fixturePath);
            return;
        }

        var json = fs.ReadAllText(fixturePath);
        ChecksContext.Check("GraphQL fixture 非空", json.Length > 5000, json.Length + " B");

        var fetcher = new FakeHttpFetcher(postResponse: json);
        var logger = new RecordingLogger();
        var client = new MetadataClient(fetcher, logger);

        var games = client.GetMetadataAsync(new[] { "ck3", "victoria3", "stellaris" }, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Equal("解析出 3 款游戏", 3, games.Count);

        var byId = new Dictionary<string, GameMetadataDto>(StringComparer.Ordinal);
        foreach (var game in games)
        {
            byId[game.Id] = game;
        }

        ChecksContext.Check("包含 victoria3", byId.ContainsKey("victoria3"));
        ChecksContext.Check("包含 stellaris", byId.ContainsKey("stellaris"));
        ChecksContext.Check("包含 ck3", byId.ContainsKey("ck3"));

        // 合计 13 个远程 URL：victoria3 3 个、stellaris 5 个、ck3 5 个。
        var totalUrls = 0;
        foreach (var game in games)
        {
            var settings = game.ThemeSettings;
            if (settings is null)
            {
                continue;
            }

            foreach (var url in EnumerateUrls(settings))
            {
                totalUrls++;
                ChecksContext.Check(
                    game.Id + " 的素材 URL 使用 metadata-assets 域名",
                    url.StartsWith(AssetPrefix, StringComparison.Ordinal),
                    url);
            }
        }

        ChecksContext.Equal("合计 13 个远程素材 URL", 13, totalUrls);

        // ck3 的按钮颜色实测值（hsla 原样保留，不做颜色转换）。
        var ck3 = byId["ck3"];
        ChecksContext.Equal("ck3 按钮颜色实测值", "hsla(53, 31%, 85%, 0.9)", ck3.ThemeSettings?.ButtonTextColor);
        ChecksContext.Equal("ck3 按钮悬停色实测值", "hsla(53, 36%, 95%, 1)", ck3.ThemeSettings?.ButtonHoverTextColor);
        ChecksContext.Equal<int?>("ck3 字重实测值", 200, ck3.ThemeSettings?.FontWeight);
        ChecksContext.Equal<double?>("ck3 字号实测值", 24d, ck3.ThemeSettings?.FontSize);
        ChecksContext.Equal("ck3 exePath 实测值", "../binaries/ck3", ck3.ExePath);
        ChecksContext.Equal("ck3 dlcPath 实测值", "../game", ck3.DlcPath);
        ChecksContext.Equal("ck3 exeArgs 数量", 1, ck3.ExeArgs.Count);

        var victoria3 = byId["victoria3"];
        ChecksContext.Check("victoria3 无 exePath", victoria3.ExePath is null);
        ChecksContext.Check("victoria3 有 background", victoria3.ThemeSettings?.Background is not null);
        ChecksContext.Check("victoria3 无 appIcon", victoria3.ThemeSettings?.AppIcon is null);

        var stellaris = byId["stellaris"];
        ChecksContext.Check("stellaris 有 appIcon", stellaris.ThemeSettings?.AppIcon is not null);
        ChecksContext.Equal(
            "stellaris browserDlcUrl 实测值",
            "https://store.steampowered.com/dlc/281990/Stellaris/",
            stellaris.BrowserDlcUrl);
        var stellarisFontUrl = stellaris.ThemeSettings?.ButtonFontPath;
        ChecksContext.Check(
            "stellaris 按钮字体 URL 前缀",
            stellarisFontUrl is not null
            && stellarisFontUrl.StartsWith(AssetPrefix + "stellaris/", StringComparison.Ordinal),
            stellarisFontUrl ?? "null");

        // 畸形 JSON / 非 data.games 结构必须容错返回空列表，不抛异常。
        var brokenClient = new MetadataClient(new FakeHttpFetcher(postResponse: "{ this is not json "), logger);
        var broken = brokenClient.GetMetadataAsync(new[] { "ck3" }, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("畸形 JSON 返回空列表", 0, broken.Count);

        var emptyClient = new MetadataClient(new FakeHttpFetcher(postResponse: "{\"data\":null}"), logger);
        var empty = emptyClient.GetMetadataAsync(new[] { "ck3" }, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("data 为 null 返回空列表", 0, empty.Count);

        var noIds = client.GetMetadataAsync(Array.Empty<string>(), CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("空 gameIds 直接返回空列表", 0, noIds.Count);

        // 请求体必须包含 operationName/query/variables 三要素。
        var sentBody = fetcher.LastPostBody ?? string.Empty;
        ChecksContext.Check("请求体含 operationName", sentBody.Contains("\"operationName\":\"GetMetadata\"", StringComparison.Ordinal));
        ChecksContext.Check("请求体含 variables.gameIds", sentBody.Contains("\"gameIds\":[\"ck3\",\"victoria3\",\"stellaris\"]", StringComparison.Ordinal));
        ChecksContext.Check("请求体含 query GetMetadata", sentBody.Contains("query GetMetadata", StringComparison.Ordinal));
        ChecksContext.Check(
            "请求发往 GraphQL 端点",
            fetcher.LastPostUri is not null && fetcher.LastPostUri.Host == "api.paradox-interactive.com",
            fetcher.LastPostUri?.ToString() ?? "null");
    }

    private static IEnumerable<string> EnumerateUrls(ThemeSettingsDto settings)
    {
        foreach (var url in new[]
                 {
                     settings.AppIcon,
                     settings.AppTaskbarIcon,
                     settings.Background,
                     settings.Logo,
                     settings.ButtonBackgroundImage,
                     settings.ButtonFontPath,
                 })
        {
            if (url is not null)
            {
                yield return url;
            }
        }
    }

    // ------------------------------------------------------------ magic bytes

    private static void CheckAssetFileTypeDetection()
    {
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };
        ChecksContext.Equal("magic: PNG", AssetFileType.Png, AssetFileTypeDetector.Detect(png));

        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46 };
        ChecksContext.Equal("magic: JPEG", AssetFileType.Jpeg, AssetFileTypeDetector.Detect(jpeg));

        var svg = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        ChecksContext.Equal("magic: SVG（XML 声明后含 <svg）", AssetFileType.Svg, AssetFileTypeDetector.Detect(svg));

        var svgUpper = System.Text.Encoding.UTF8.GetBytes("<SVG width=\"10\"></SVG>");
        ChecksContext.Equal("magic: SVG（大写 <SVG）", AssetFileType.Svg, AssetFileTypeDetector.Detect(svgUpper));

        var ttf = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x0C, 0x00, 0x80 };
        ChecksContext.Equal("magic: TTF", AssetFileType.Ttf, AssetFileTypeDetector.Detect(ttf));

        var woff = System.Text.Encoding.ASCII.GetBytes("wOFF\x00\x01\x00\x00");
        ChecksContext.Equal("magic: WOFF", AssetFileType.Woff, AssetFileTypeDetector.Detect(woff));

        var woff2 = System.Text.Encoding.ASCII.GetBytes("wOF2\x00\x01\x00\x00");
        ChecksContext.Equal("magic: WOFF2", AssetFileType.Woff2, AssetFileTypeDetector.Detect(woff2));

        // 官方公告（Braze content cards）的 main 图是 GIF。
        var gif = System.Text.Encoding.ASCII.GetBytes("GIF89a");
        ChecksContext.Equal("magic: GIF", AssetFileType.Gif, AssetFileTypeDetector.Detect(gif));

        var unknown = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x11, 0x22, 0x33, 0x44 };
        ChecksContext.Equal("magic: 无法识别", AssetFileType.Unknown, AssetFileTypeDetector.Detect(unknown));

        ChecksContext.Equal("扩展名: PNG", ".png", AssetFileTypeDetector.GetExtension(AssetFileType.Png));
        ChecksContext.Equal("扩展名: JPEG", ".jpg", AssetFileTypeDetector.GetExtension(AssetFileType.Jpeg));
        ChecksContext.Equal("扩展名: SVG", ".svg", AssetFileTypeDetector.GetExtension(AssetFileType.Svg));
        ChecksContext.Equal("扩展名: GIF", ".gif", AssetFileTypeDetector.GetExtension(AssetFileType.Gif));
        ChecksContext.Equal("扩展名: TTF", ".ttf", AssetFileTypeDetector.GetExtension(AssetFileType.Ttf));
        ChecksContext.Equal("扩展名: 未知为空", string.Empty, AssetFileTypeDetector.GetExtension(AssetFileType.Unknown));

        ChecksContext.Equal("1 KB 阈值", 1024, AssetFileTypeDetector.MinimumFileBytes);
        ChecksContext.Equal("40 MB 阈值", 40 * 1024 * 1024, AssetFileTypeDetector.MaximumFileBytes);
        ChecksContext.Equal("TTL 5 天毫秒数", 432_000_000L, ThemeResolver.CacheTtlMilliseconds);
        ChecksContext.Equal("失败短重试 10 分钟毫秒数", 600_000L, ThemeResolver.RefreshRetryDelayMilliseconds);
        ChecksContext.Equal("连接超时 30 秒", TimeSpan.FromSeconds(30), HttpFetcher.ConnectTimeout);
        ChecksContext.Equal("单体素材最多尝试 3 次", 3, AssetDownloader.MaxAttempts);
    }

    // ------------------------------------------------------------- 域名白名单

    private static void CheckWhitelist(IFileSystem fs, string tempRoot)
    {
        var logger = new RecordingLogger();
        var fetcher = new FakeHttpFetcher();
        var downloader = new AssetDownloader(fetcher, logger, fs);
        var target = Path.Combine(tempRoot, "whitelist", "x");

        var evil = downloader.DownloadAsync("https://evil.example/x.png", target, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("白名单拒绝 evil.example", evil is null);
        ChecksContext.Equal("被拒域名没有发起网络请求", 0, fetcher.GetCallCount);
        ChecksContext.Check(
            "被拒域名记录了 warn 日志",
            logger.Warnings.Any(w => w.Contains("白名单", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));

        var allowed = new[]
        {
            "https://metadata-assets.paradox-interactive.com/ck3/launcher-background-image-abc",
            "https://store.steampowered.com/api/appdetails?appids=1158310",
            "https://api.paradox-interactive.com/metadata/graphql",
            "https://braze-images.com/appboy/communication/marketing/content_cards_message_variations/images/x/original.gif?1",
        };
        foreach (var url in allowed)
        {
            ChecksContext.Check("白名单放行 " + new Uri(url).Host, OnlineHosts.IsAllowed(new Uri(url)));
        }

        ChecksContext.Check("拒绝 http 以外的 scheme（file://）", !OnlineHosts.IsAllowed(new Uri("file:///C:/x.png")));
        ChecksContext.Check("拒绝子域欺骗 evil.metadata-assets.paradox-interactive.com", !OnlineHosts.IsAllowed(new Uri("https://evil.metadata-assets.paradox-interactive.com/x")));
        ChecksContext.Check("拒绝公告图子域 cdn.braze-images.com", !OnlineHosts.IsAllowed(new Uri("https://cdn.braze-images.com/x.gif")));
        ChecksContext.Check("拒绝非绝对 URI", !OnlineHosts.IsAllowed(new Uri("/relative", UriKind.Relative)));

        downloader.Dispose();
    }

    // --------------------------------------------------------------- 下载器

    private static void CheckDownloader(IFileSystem fs, string tempRoot)
    {
        var logger = new RecordingLogger();
        var pngBytes = BuildAsset(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 8192);
        var fetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(pngBytes));
        var downloader = new AssetDownloader(fetcher, logger, fs);
        var dir = Path.Combine(tempRoot, "download");
        var target = Path.Combine(dir, "ck3-background");

        var pngPath = downloader.DownloadAsync(
                "https://metadata-assets.paradox-interactive.com/ck3/launcher-background-image-abc",
                target,
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("PNG 下载返回路径", pngPath is not null, pngPath ?? "null");
        ChecksContext.Equal<string?>("PNG 推断扩展名", ".png", Path.GetExtension(pngPath ?? string.Empty));
        ChecksContext.Check("PNG 落盘文件存在", pngPath is not null && fs.FileExists(pngPath));
        ChecksContext.Check("PNG 内容逐字节一致", pngPath is not null && fs.ReadAllBytes(pngPath).AsSpan().SequenceEqual(pngBytes));
        ChecksContext.Check("成功下载后没有残留 .part", !fs.FileExists(target + AssetDownloader.PartSuffix));

        // JPEG / SVG 各自推断出正确扩展名。
        var jpegBytes = BuildAsset(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, 5000);
        var jpegFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(jpegBytes));
        using (var jpegDownloader = new AssetDownloader(jpegFetcher, logger, fs))
        {
            var jpegPath = jpegDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/logo",
                    Path.Combine(dir, "ck3-logo"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Equal<string?>("JPEG 推断扩展名", ".jpg", Path.GetExtension(jpegPath ?? string.Empty));
        }

        var svgBytes = BuildAsset(
            System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"/>"),
            6000);
        var svgFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(svgBytes));
        using (var svgDownloader = new AssetDownloader(svgFetcher, logger, fs))
        {
            var svgPath = svgDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/icon",
                    Path.Combine(dir, "ck3-icon"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Equal<string?>("SVG 推断扩展名", ".svg", Path.GetExtension(svgPath ?? string.Empty));
        }

        // 回归：官方 cache 里 ck3-buttonBackgroundImage 只有 3,818 B（合法 PNG）。
        // 旧的 4 KB 下限会把它当坏文件丢掉，导致按钮背景图永远用安装目录那张。
        var smallButtonBytes = BuildAsset(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 3818);
        var smallButtonFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(smallButtonBytes));
        using (var smallButtonDownloader = new AssetDownloader(smallButtonFetcher, logger, fs))
        {
            var smallButtonPath = smallButtonDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/button-background-image-351bc9f80784cb561fce",
                    Path.Combine(dir, "ck3-button"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Equal<string?>("3,818 B 的官方按钮背景图被接受", ".png", Path.GetExtension(smallButtonPath ?? string.Empty));
            ChecksContext.Check(
                "3,818 B 的官方按钮背景图落盘",
                smallButtonPath is not null && fs.FileExists(smallButtonPath));
        }

        // 小于 1 KB：拒绝并清理 .part。
        var smallBytes = new byte[512];
        smallBytes[0] = 0x89;
        smallBytes[1] = 0x50;
        smallBytes[2] = 0x4E;
        smallBytes[3] = 0x47;
        var smallFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(smallBytes));
        using (var smallDownloader = new AssetDownloader(smallFetcher, logger, fs))
        {
            var smallTarget = Path.Combine(dir, "too-small");
            var small = smallDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/tiny",
                    smallTarget,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("小于 1 KB 被拒绝", small is null);
            ChecksContext.Check("小于 1 KB 清理了 .part", !fs.FileExists(smallTarget + AssetDownloader.PartSuffix));
        }

        // 超过 40 MB：边下边判，超限即中止并清理 .part（流是合成的，不占 40 MB 内存）。
        var hugeFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(0x89, long.MaxValue));
        using (var hugeDownloader = new AssetDownloader(hugeFetcher, logger, fs))
        {
            var hugeTarget = Path.Combine(dir, "too-big");
            var huge = hugeDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/huge",
                    hugeTarget,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("超过 40 MB 被拒绝", huge is null);
            ChecksContext.Check("超过 40 MB 清理了 .part", !fs.FileExists(hugeTarget + AssetDownloader.PartSuffix));

            // 强断言：下载目录里不允许残留任何 .part（超限路径曾因「在使用中删除」而漏删）。
            var leftovers = fs.EnumerateFiles(dir, "*.part", recursive: false).ToList();
            ChecksContext.Equal(
                "下载目录里没有任何 .part 残留",
                0,
                leftovers.Count);
        }

        // magic bytes 无法识别：拒绝且不落盘。
        var garbage = BuildAsset(new byte[] { 0x11, 0x22, 0x33, 0x44 }, 8000);
        var garbageFetcher = new FakeHttpFetcher(streamFactory: _ => new SyntheticStream(garbage));
        using (var garbageDownloader = new AssetDownloader(garbageFetcher, logger, fs))
        {
            var garbageTarget = Path.Combine(dir, "garbage");
            var garbagePath = garbageDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/garbage",
                    garbageTarget,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("未知 magic 被拒绝", garbagePath is null);
            ChecksContext.Check("未知 magic 未留下文件", !fs.FileExists(garbageTarget));
        }

        // 取消必须传播 OperationCanceledException 且清理 .part。
        var cancelFetcher = new FakeHttpFetcher(streamFactory: _ => new SlowStream());
        using (var cancelDownloader = new AssetDownloader(cancelFetcher, logger, fs))
        using (var cts = new CancellationTokenSource())
        {
            var cancelTarget = Path.Combine(dir, "cancelled");
            var task = cancelDownloader.DownloadAsync(
                "https://metadata-assets.paradox-interactive.com/ck3/cancel",
                cancelTarget,
                cts.Token);
            cts.Cancel();
            var cancelled = false;
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            ChecksContext.Check("取消传播 OperationCanceledException", cancelled);
            ChecksContext.Check("取消后清理 .part", !fs.FileExists(cancelTarget + AssetDownloader.PartSuffix));
        }

        ChecksContext.Check("并发上限为 2", AssetDownloader.MaxConcurrentDownloads == 2);

        // 连接抖动重试：前两次抛 HttpRequestException，第三次成功。
        // 真实事故：到 metadata-assets 的 TLS 握手一次性超时，6 项素材全灭且被 5 天 TTL 锁住。
        var flakyFetcher = new FlakyHttpFetcher(pngBytes, failures: 2);
        using (var flakyDownloader = new AssetDownloader(flakyFetcher, logger, fs)
               {
                   RetryBaseDelay = TimeSpan.Zero,
               })
        {
            var retriedPath = flakyDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/retry",
                    Path.Combine(dir, "ck3-retry"),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("先失败两次后成功的下载返回路径", retriedPath is not null, retriedPath ?? "null");
            ChecksContext.Check(
                "先失败两次后成功的文件落盘",
                retriedPath is not null && fs.FileExists(retriedPath));
            ChecksContext.Equal("重试 2 次 → 总共尝试 3 次", 3, flakyFetcher.GetCallCount);
            ChecksContext.Check(
                "每次重试都记一条 warn",
                logger.Warnings.Count(w => w.Contains("素材下载重试", StringComparison.Ordinal)) == 2,
                string.Join(" | ", logger.Warnings));
        }

        // 一直失败：尝试满 3 次后放弃，返回 null、不留 .part，并留下「已重试 2 次」的 warn。
        var deadFetcher = new FlakyHttpFetcher(pngBytes, failures: int.MaxValue);
        using (var deadDownloader = new AssetDownloader(deadFetcher, logger, fs)
               {
                   RetryBaseDelay = TimeSpan.Zero,
               })
        {
            var deadTarget = Path.Combine(dir, "ck3-dead");
            var dead = deadDownloader.DownloadAsync(
                    "https://metadata-assets.paradox-interactive.com/ck3/dead",
                    deadTarget,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("重试用尽返回 null", dead is null);
            ChecksContext.Equal("重试用尽后不再尝试", AssetDownloader.MaxAttempts, deadFetcher.GetCallCount);
            ChecksContext.Check("重试用尽没有残留 .part", !fs.FileExists(deadTarget + AssetDownloader.PartSuffix));
            ChecksContext.Check(
                "重试用尽记 warn（含已重试次数）",
                logger.Warnings.Any(w => w.Contains("已重试 2 次", StringComparison.Ordinal)),
                string.Join(" | ", logger.Warnings));
        }

        // 调用方取消不能被重试吞掉：只尝试一次就上抛。
        var cancelDuringRetryFetcher = new FlakyHttpFetcher(pngBytes, failures: int.MaxValue);
        using (var cancelDuringRetryDownloader = new AssetDownloader(cancelDuringRetryFetcher, logger, fs)
               {
                   RetryBaseDelay = TimeSpan.Zero,
               })
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            var cancelledRetry = false;
            try
            {
                cancelDuringRetryDownloader.DownloadAsync(
                        "https://metadata-assets.paradox-interactive.com/ck3/cancel-retry",
                        Path.Combine(dir, "ck3-cancel-retry"),
                        cts.Token)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (OperationCanceledException)
            {
                cancelledRetry = true;
            }

            ChecksContext.Check("已取消的 token 不会进入重试", cancelledRetry);
        }

        downloader.Dispose();
    }

    // --------------------------------------------------------------- 代理

    private static void CheckProxyResolver()
    {
        // 用户设置优先于环境变量。
        var settings = new AppSettings { ProxyUrl = "http://127.0.0.1:7890" };
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["http_proxy"] = "http://10.0.0.1:3128",
            ["https_proxy"] = "http://10.0.0.1:3128",
        };
        var url = ProxyResolver.ResolveUrl(settings, name => env.TryGetValue(name, out var v) ? v : null);
        ChecksContext.Equal<string?>("代理: 用户设置优先", "http://127.0.0.1:7890", url);

        var proxy = ProxyResolver.Resolve(settings, environment: name => env.TryGetValue(name, out var v) ? v : null);
        ChecksContext.Check("代理: 构造出 WebProxy", proxy is not null);
        ChecksContext.Equal<string?>(
            "代理: 地址正确",
            "http://127.0.0.1:7890",
            proxy is System.Net.WebProxy web ? web.Address?.GetLeftPart(UriPartial.Authority) : null);

        // 「跟随系统」模式：没有系统代理时回落到环境变量，并补齐缺失的 scheme。
        // 默认模式是直连，所以这里必须显式指定 ProxyMode = System；
        // 同时注入「没有系统代理」，避免读到本机真实 WinINET 设置让断言随机器变化。
        var fallback = ProxyResolver.ResolveUrl(
            new AppSettings { ProxyMode = ProxyModes.System },
            name => name switch
            {
                "http_proxy" => "127.0.0.1:7890",
                _ => null,
            },
            () => null);
        ChecksContext.Equal<string?>("代理: 环境变量回落且补 scheme", "http://127.0.0.1:7890", fallback);

        // 两边都没有 → 直连（null）。
        var none = ProxyResolver.ResolveUrl(null, _ => null);
        ChecksContext.Check("代理: 都没有则直连", none is null);

        // 非法代理地址 → 记 warn 并直连。
        var logger = new RecordingLogger();
        var invalid = ProxyResolver.Resolve(
            new AppSettings { ProxyUrl = "::::" },
            logger,
            _ => null);
        ChecksContext.Check("代理: 非法地址回退直连", invalid is null);
        ChecksContext.Check(
            "代理: 非法地址记 warn",
            logger.Warnings.Any(w => w.Contains("代理", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));
    }

    // ------------------------------------------------------------ 主题三层

    private static void CheckThemeResolver(IFileSystem fs, string tempRoot)
    {
        var logger = new RecordingLogger();
        var clock = new FakeClock(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero));
        var cacheRoot = Path.Combine(tempRoot, "theme-cache");
        fs.EnsureDirectory(cacheRoot);

        var metadata = new CountingMetadataClient();
        var downloader = new CountingAssetDownloader(fs);
        var resolver = new ThemeResolver(fs, clock, logger, metadata, downloader, cacheRoot);
        var game = BuildGame();

        // 层 3：层 2 没有任何路径 → BuiltInPlaceholder。
        var bareGame = game with
        {
            Theme = new ThemeSpec(null, null, null, null, null, null, "#000000", "#111111", 20d, 300, null, ThemeAssetSource.BuiltInPlaceholder),
        };
        var bare = resolver.ResolveAsync(bareGame, allowOnlineRefresh: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("无缓存且层2无图 → BuiltInPlaceholder", ThemeAssetSource.BuiltInPlaceholder, bare.Source);

        // 层 2：无缓存 + 不联网 → InstallDir，且不发任何网络请求。
        var install = resolver.ResolveAsync(game, allowOnlineRefresh: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("无缓存 → InstallDir", ThemeAssetSource.InstallDir, install.Source);
        ChecksContext.Equal<string?>("层2 背景原样返回", game.Theme.Background, install.Background);
        ChecksContext.Equal("不联网时元数据调用次数", 0, metadata.CallCount);
        ChecksContext.Equal("不联网时下载调用次数", 0, downloader.CallCount);

        // 层 1：写入缓存 + 索引（mtime = 现在），不联网也必须用 OnlineCache 且零请求。
        var cachedPng = Path.Combine(cacheRoot, "ck3-background.png");
        fs.WriteAllBytesAtomic(cachedPng, BuildAsset(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 8192));
        var index = new AssetIndex { LastCheckedUtc = clock.UtcNow };
        index.Set(AssetKeys.Background, AssetPrefix + "ck3/launcher-background-image-abc", "ck3-background.png");
        index.Save(fs, logger, resolver.IndexPath("ck3"));

        var online = resolver.ResolveAsync(game, allowOnlineRefresh: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("有缓存 → OnlineCache", ThemeAssetSource.OnlineCache, online.Source);
        ChecksContext.Equal<string?>("缓存背景路径生效", cachedPng, online.Background);
        ChecksContext.Equal("命中缓存时元数据调用次数仍为 0", 0, metadata.CallCount);
        ChecksContext.Equal("命中缓存时下载调用次数仍为 0", 0, downloader.CallCount);
        ChecksContext.Equal("颜色永远取层2（不联网）", game.Theme.ButtonTextColor, online.ButtonTextColor);
        ChecksContext.Equal("字号永远取层2（不联网）", game.Theme.FontSize, online.FontSize);
        ChecksContext.Equal("字重永远取层2（不联网）", game.Theme.FontWeight, online.FontWeight);
        ChecksContext.Equal<string?>("字体路径取层2（缓存未覆盖）", game.Theme.ButtonFont, online.ButtonFont);

        // TTL 4 天：仍然新鲜 → 即使 allowOnlineRefresh=true 也不刷新。
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-4));
        metadata.Reset();
        downloader.Reset();
        var fresh = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("TTL 4 天 → 不刷新（元数据调用 0）", 0, metadata.CallCount);
        ChecksContext.Equal("TTL 4 天 → 不刷新（下载调用 0）", 0, downloader.CallCount);
        ChecksContext.Equal("TTL 4 天仍为 OnlineCache", ThemeAssetSource.OnlineCache, fresh.Source);

        // TTL 6 天 + URL 未变：必须刷新元数据但跳过下载，并把 lastCheckedUtc 推到当前时间。
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-6));
        metadata.Reset();
        downloader.Reset();
        var refreshed = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("TTL 6 天 → 触发元数据请求", 1, metadata.CallCount);
        ChecksContext.Equal("TTL 6 天 + URL 未变 → 不重复下载", 0, downloader.CallCount);
        ChecksContext.Equal("TTL 6 天刷新后仍为 OnlineCache", ThemeAssetSource.OnlineCache, refreshed.Source);
        ChecksContext.Equal<string?>("TTL 6 天刷新后背景仍是缓存文件", cachedPng, refreshed.Background);
        ChecksContext.Check(
            "刷新后索引记录了 lastCheckedUtc",
            AssetIndex.Load(fs, logger, resolver.IndexPath("ck3")).LastCheckedUtc == clock.UtcNow);

        // 关键回归：刷新成功后 5 天内必须零网络请求。
        // 旧实现用缓存文件 mtime 判 TTL，而「URL 未变跳过下载」永不更新 mtime，
        // 导致每次启动都判定过期 → 每次启动多一次 GraphQL 请求。这两条断言锁死该 bug。
        metadata.Reset();
        downloader.Reset();
        var afterRefresh = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("刷新后 5 天内再 Resolve 零元数据请求", 0, metadata.CallCount);
        ChecksContext.Equal("刷新后 5 天内再 Resolve 零下载请求", 0, downloader.CallCount);
        ChecksContext.Equal<string?>("刷新后仍用缓存背景", cachedPng, afterRefresh.Background);

        // 刷新失败（下载返回 null）必须静默保留旧图，不抛异常。
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-6));
        metadata.Reset();
        metadata.BackgroundUrl = AssetPrefix + "ck3/launcher-background-image-fail";
        downloader.Reset();
        downloader.FailEverything = true;
        var degraded = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("刷新失败保留旧图（仍为 OnlineCache）", ThemeAssetSource.OnlineCache, degraded.Source);
        ChecksContext.Equal<string?>("刷新失败保留旧图路径", cachedPng, degraded.Background);
        downloader.FailEverything = false;

        // TTL 过期且 URL 变了：必须下载新文件，并清理扩展名变化的旧文件名。
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-6));
        metadata.Reset();
        metadata.BackgroundUrl = AssetPrefix + "ck3/launcher-background-image-def";
        downloader.Reset();
        downloader.OutputExtension = ".jpg";
        var changed = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("URL 变化 → 触发下载", 1, downloader.CallCount);
        var changedPath = changed.Background;
        ChecksContext.Check("URL 变化后新缓存文件存在", changedPath is not null && fs.FileExists(changedPath), changedPath ?? "(null)");
        ChecksContext.Check("URL 变化后旧缓存文件被清理", !fs.FileExists(cachedPng), cachedPng);

        // 文件名必须带上 URL 的短 hash：URL 一变文件名就变，
        // BitmapLoader / WinUI / FontLoader 按路径做的缓存自然失效。
        var changedFileName = changedPath is null ? string.Empty : Path.GetFileName(changedPath);
        ChecksContext.Check(
            "URL 变化后缓存文件名带版本 hash",
            changedFileName.StartsWith("ck3-background-", StringComparison.Ordinal)
                && changedFileName.EndsWith(".jpg", StringComparison.Ordinal)
                && changedFileName.Length == "ck3-background-".Length + 8 + ".jpg".Length,
            changedFileName);
        ChecksContext.Check(
            "URL 变化后文件名确实变了",
            !string.Equals(changedFileName, Path.GetFileName(cachedPng), StringComparison.OrdinalIgnoreCase),
            changedFileName);

        var changedIndex = AssetIndex.Load(fs, logger, resolver.IndexPath("ck3"));
        ChecksContext.Equal<string?>(
            "URL 变化后索引记录新 URL",
            AssetPrefix + "ck3/launcher-background-image-def",
            changedIndex.Find(AssetKeys.Background)?.Url);
        ChecksContext.Equal<string?>(
            "URL 变化后索引记录新文件名",
            changedFileName,
            changedIndex.Find(AssetKeys.Background)?.FileName);
        downloader.OutputExtension = ".png";

        // 同一 URL 再刷新一次：文件名必须保持不变（否则每次启动都会重下）。
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-6));
        metadata.Reset();
        downloader.Reset();
        var again = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("同一 URL 再刷新不重复下载", 0, downloader.CallCount);
        ChecksContext.Equal<string?>("同一 URL 再刷新沿用同一缓存文件", changedPath, again.Background);

        // 损坏的索引必须容错：视为无缓存，退回层 2。
        fs.WriteAllTextAtomic(resolver.IndexPath("ck3"), "{ broken json ");
        var corrupt = resolver.ResolveAsync(game, allowOnlineRefresh: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("损坏索引 → 退回 InstallDir", ThemeAssetSource.InstallDir, corrupt.Source);
        ChecksContext.Check(
            "损坏索引记 warn",
            logger.Warnings.Any(w => w.Contains("索引", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));
        ChecksContext.Check(
            "损坏索引时 GetLastCheckedUtc 返回 null",
            resolver.GetLastCheckedUtc("ck3") is null);

        // GetLastCheckedUtc 是设置页「上次检查 / 下次检查」的唯一数据源。
        ChecksContext.Check(
            "空 gameId 的 GetLastCheckedUtc 不抛异常",
            resolver.GetLastCheckedUtc("  ") is null);
        var missingId = resolver.GetLastCheckedUtc("no-such-game");
        ChecksContext.Check("从未检查过的游戏 GetLastCheckedUtc 为 null", missingId is null);
        SetLastChecked(fs, logger, resolver, "ck3", clock.UtcNow.AddDays(-2));
        ChecksContext.Equal<DateTimeOffset?>(
            "GetLastCheckedUtc 读索引里的 lastCheckedUtc",
            clock.UtcNow.AddDays(-2),
            resolver.GetLastCheckedUtc("ck3"));

        // 默认缓存目录必须落在程序目录的 data\theme-cache（不写 C 盘）。
        var expectedRoot = Path.Combine(
            PdxLauncher.Core.Configuration.PdxDataRoot.Directory,
            "theme-cache");
        ChecksContext.Equal("默认缓存目录", expectedRoot, ThemeResolver.DefaultCacheRoot);
        ChecksContext.Equal("显式缓存目录生效", cacheRoot, resolver.CacheRoot);
    }

    /// <summary>
    /// 纱层判定（EU4 发灰的直接成因）：整幅图 alpha 恒为 51/255、RGB 却是一张完整油画时，
    /// 必须识别成「纱层」并摊平，否则卡片就是一片灰。反过来，真正的半透明美术不能被误摊平。
    /// </summary>
    private static void CheckWashLayerPolicy()
    {
        // 20% 均匀不透明度（EU4 实测口径）：1920×1080 全是 alpha=51。
        var uniform20 = new int[WashLayerPolicy.AlphaBucketCount];
        uniform20[51] = 2_073_600;
        ChecksContext.Check("均匀 20% alpha 判为纱层", WashLayerPolicy.IsWashLayer(uniform20, 2_073_600));

        var opaque = new int[WashLayerPolicy.AlphaBucketCount];
        opaque[255] = 2_073_600;
        ChecksContext.Check("完全不透明不是纱层", !WashLayerPolicy.IsWashLayer(opaque, 2_073_600));

        var semiArt = new int[WashLayerPolicy.AlphaBucketCount];
        semiArt[40] = 960_000;
        semiArt[255] = 40_000;
        ChecksContext.Check("4% 不透明像素的真半透明美术不是纱层", !WashLayerPolicy.IsWashLayer(semiArt, 1_000_000));

        var highPeak = new int[WashLayerPolicy.AlphaBucketCount];
        highPeak[200] = 1_000_000;
        ChecksContext.Check("峰值 200/255 超过上限，不算纱层", !WashLayerPolicy.IsWashLayer(highPeak, 1_000_000));

        var transparent = new int[WashLayerPolicy.AlphaBucketCount];
        transparent[0] = 1_000_000;
        ChecksContext.Check("全透明图不算纱层", !WashLayerPolicy.IsWashLayer(transparent, 1_000_000));

        ChecksContext.Check("像素数为 0 不算纱层", !WashLayerPolicy.IsWashLayer(uniform20, 0));
        ChecksContext.Check("直方图长度不对不算纱层", !WashLayerPolicy.IsWashLayer(new int[10], 100));

        // 采样：BGRA 里 alpha 在第 4 字节。
        var pixels = new byte[] { 10, 20, 30, 51, 40, 50, 60, 51, 70, 80, 90, 255 };
        var histogram = WashLayerPolicy.BuildAlphaHistogram(pixels);
        ChecksContext.Equal("直方图按第 4 字节统计", 2, histogram[51]);
        ChecksContext.Equal("直方图记录不透明像素", 1, histogram[255]);

        var sampled = WashLayerPolicy.BuildAlphaHistogram(pixels, stride: 2);
        ChecksContext.Equal("采样步长 2 只统计隔一个像素", 1, sampled[51]);

        var threw = false;
        try
        {
            WashLayerPolicy.BuildAlphaHistogram(new byte[7]);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        ChecksContext.Check("缓冲区长度不是 4 的倍数要报错", threw);
        ChecksContext.Equal("摊平后的 alpha 为全满", 255, WashLayerPolicy.FlattenedAlpha);
    }

    /// <summary>
    /// 刷新失败不得推进 5 天 TTL。真实事故：到 metadata-assets 的 TLS 握手一次性超时，
    /// 6 项素材全灭后仍写了 lastCheckedUtc，导致 eu4/ck3/hoi4/stellaris 的主题
    /// 被锁在「安装目录那张 20% 不透明底图」上整整 5 天（用户看到的就是发灰的卡片）。
    /// </summary>
    private static void CheckThemeRefreshRetry(IFileSystem fs, string tempRoot)
    {
        var logger = new RecordingLogger();
        var clock = new MutableClock(new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero));
        var cacheRoot = Path.Combine(tempRoot, "theme-cache-retry");
        fs.EnsureDirectory(cacheRoot);

        var metadata = new CountingMetadataClient();
        var downloader = new CountingAssetDownloader(fs);
        var resolver = new ThemeResolver(fs, clock, logger, metadata, downloader, cacheRoot);
        var game = BuildGame();
        var expectedRetryAt = clock.UtcNow.AddMilliseconds(ThemeResolver.RefreshRetryDelayMilliseconds);

        // 1) 部分失败（background 成功、logo 失败）：返回已下到的背景，但不推进 lastCheckedUtc。
        metadata.LogoUrl = AssetPrefix + "ck3/logo-broken";
        downloader.FailPredicate = url => url.Contains("/logo", StringComparison.Ordinal);
        var partial = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("部分失败 → 仍用下到的背景", ThemeAssetSource.OnlineCache, partial.Source);
        ChecksContext.Check(
            "部分失败 → 背景文件可用",
            partial.Background is not null && fs.FileExists(partial.Background),
            partial.Background ?? "(null)");

        var afterPartial = AssetIndex.Load(fs, logger, resolver.IndexPath("ck3"));
        ChecksContext.Check("部分失败不推进 lastCheckedUtc", afterPartial.LastCheckedUtc is null);
        ChecksContext.Equal<DateTimeOffset?>("部分失败记下 10 分钟后的重试时间", expectedRetryAt, afterPartial.RetryAfterUtc);
        ChecksContext.Check(
            "部分失败记 warn 并带成功项数",
            logger.Warnings.Any(w => w.Contains("主题刷新部分失败", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));

        // 2) 已有新鲜成功记录 + 未到点的重试标记：不联网；到点之后按 10 分钟重试。
        metadata.LogoUrl = null;
        downloader.FailPredicate = null;
        metadata.BackgroundUrl = afterPartial.Find(AssetKeys.Background)?.Url ?? metadata.BackgroundUrl;

        var gated = AssetIndex.Load(fs, logger, resolver.IndexPath("ck3"));
        gated.LastCheckedUtc = clock.UtcNow.AddDays(-1);
        gated.RetryAfterUtc = clock.UtcNow.AddMinutes(5);
        gated.Save(fs, logger, resolver.IndexPath("ck3"));

        metadata.Reset();
        downloader.Reset();
        resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("重试时间未到 → 不发元数据请求", 0, metadata.CallCount);

        clock.Advance(TimeSpan.FromMinutes(6));
        metadata.Reset();
        downloader.Reset();
        var afterRetry = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("重试时间已到 → 发一次元数据请求", 1, metadata.CallCount);
        ChecksContext.Equal("重试时间已到 → URL 未变不重复下载", 0, downloader.CallCount);
        ChecksContext.Equal("重试成功后仍是 OnlineCache", ThemeAssetSource.OnlineCache, afterRetry.Source);

        var afterRetryIndex = AssetIndex.Load(fs, logger, resolver.IndexPath("ck3"));
        ChecksContext.Check(
            "重试成功后 lastCheckedUtc 推进到当前时间",
            afterRetryIndex.LastCheckedUtc == clock.UtcNow,
            afterRetryIndex.LastCheckedUtc?.ToString("O") ?? "(null)");
        ChecksContext.Check("重试成功后清掉重试标记", afterRetryIndex.RetryAfterUtc is null);

        // 3) 自愈：旧版 bug 写出的「检查过却零素材」索引必须被忽略并重新联网
        //    （本机 data\theme-cache 里 5 个游戏的索引就是这种形状）。
        var poisoned = new AssetIndex { LastCheckedUtc = clock.UtcNow.AddHours(-1) };
        poisoned.Save(fs, logger, resolver.IndexPath("ck3"));
        logger.Infos.Clear();
        metadata.Reset();
        downloader.Reset();
        var healed = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("零素材索引 → 重新联网一次", 1, metadata.CallCount);
        ChecksContext.Equal("零素材索引 → 重新下载背景", 1, downloader.CallCount);
        ChecksContext.Equal("自愈后仍为 OnlineCache", ThemeAssetSource.OnlineCache, healed.Source);
        ChecksContext.Check(
            "自愈时记一条 info 说明原因",
            logger.Infos.Any(i => i.Contains("没有素材条目", StringComparison.Ordinal)),
            string.Join(" | ", logger.Infos));

        // 4) 全失败：依旧不推进 lastCheckedUtc，且不留半截缓存条目。
        var retryAtNow = clock.UtcNow.AddMilliseconds(ThemeResolver.RefreshRetryDelayMilliseconds);
        fs.WriteAllTextAtomic(resolver.IndexPath("ck3"), "{ }");
        downloader.Reset();
        downloader.FailEverything = true;
        metadata.Reset();
        var allFailed = resolver.ResolveAsync(game, allowOnlineRefresh: true, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        downloader.FailEverything = false;
        ChecksContext.Equal("全失败 → 退回层2 背景", game.Theme.Background, allFailed.Background);
        var afterAllFailed = AssetIndex.Load(fs, logger, resolver.IndexPath("ck3"));
        ChecksContext.Check("全失败不推进 lastCheckedUtc", afterAllFailed.LastCheckedUtc is null);
        ChecksContext.Equal<DateTimeOffset?>("全失败同样记下重试时间", retryAtNow, afterAllFailed.RetryAfterUtc);
        ChecksContext.Equal("全失败不写入任何素材条目", 0, afterAllFailed.Assets.Count);
    }

    /// <summary>把索引里的 lastCheckedUtc 改成指定时间，用来模拟「上次联网检查」的年龄。</summary>
    private static void SetLastChecked(IFileSystem fs, RecordingLogger logger, ThemeResolver resolver, string gameId, DateTimeOffset when)
    {
        var index = AssetIndex.Load(fs, logger, resolver.IndexPath(gameId));
        index.LastCheckedUtc = when;
        index.RetryAfterUtc = null;
        index.Save(fs, logger, resolver.IndexPath(gameId));
    }

    private static GameInstallation BuildGame() =>
        new(
            "ck3",
            "Crusader Kings III",
            "1.13.0",
            "C:\\Games\\CK3",
            "C:\\Games\\CK3\\launcher\\launcher-settings.json",
            LoadFileFormat.ContentLoadV1,
            "C:\\Users\\me\\Documents\\Paradox Interactive\\Crusader Kings III",
            "C:\\Games\\CK3\\game",
            "C:\\Users\\me\\Documents\\Paradox Interactive\\Crusader Kings III\\mod",
            "C:\\Games\\CK3\\binaries\\ck3.exe",
            new[] { "-gdpr-compliant" },
            Array.Empty<AlternativeExecutable>(),
            new ThemeSpec(
                "C:\\Games\\CK3\\launcher\\assets\\app-background.png",
                "C:\\Games\\CK3\\launcher\\assets\\logo.svg",
                null,
                null,
                "C:\\Games\\CK3\\launcher\\assets\\button.png",
                "C:\\Games\\CK3\\launcher\\assets\\fonts\\CormorantGaramond-Regular.ttf",
                "hsla(53, 31%, 85%, 0.9)",
                "hsla(53, 36%, 95%, 1)",
                24d,
                200,
                null,
                ThemeAssetSource.InstallDir),
            "https://store.steampowered.com/dlc/1158310/",
            null,
            1158310,
            GameSupport.Full);

    private static byte[] BuildAsset(byte[] head, int totalLength)
    {
        var data = new byte[Math.Max(totalLength, head.Length)];
        Buffer.BlockCopy(head, 0, data, 0, head.Length);
        for (var i = head.Length; i < data.Length; i++)
        {
            data[i] = (byte)(0x40 + (i % 0x20));
        }

        return data;
    }

    // ------------------------------------------------------------- 测试替身

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

        public DateTimeOffset UtcNow { get; }
    }

    /// <summary>可推进的时钟：用于验证「10 分钟短重试」到点前后的行为差异。</summary>
    private sealed class MutableClock : IClock
    {
        public MutableClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; private set; }

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    /// <summary>前 N 次 GetAsync 抛 HttpRequestException，之后返回合成 PNG；用于验证下载重试。</summary>
    private sealed class FlakyHttpFetcher : IHttpFetcher
    {
        private readonly byte[] _payload;
        private readonly int _failures;

        public FlakyHttpFetcher(byte[] payload, int failures)
        {
            _payload = payload;
            _failures = failures;
        }

        public int GetCallCount { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCallCount++;
            if (GetCallCount <= _failures)
            {
                throw new HttpRequestException("fake flaky 连接被重置");
            }

            return Task.FromResult<Stream>(new SyntheticStream(_payload));
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            Task.FromResult("{}");
    }

    private sealed class FakeHttpFetcher : IHttpFetcher
    {
        private readonly string? _postResponse;
        private readonly Func<Uri, Stream>? _streamFactory;

        public FakeHttpFetcher(string? postResponse = null, Func<Uri, Stream>? streamFactory = null)
        {
            _postResponse = postResponse;
            _streamFactory = streamFactory;
        }

        public int GetCallCount { get; private set; }

        public Uri? LastPostUri { get; private set; }

        public string? LastPostBody { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            GetCallCount++;
            if (_streamFactory is null)
            {
                throw new HttpRequestException("fake fetcher 未配置流");
            }

            return Task.FromResult(_streamFactory(uri));
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken)
        {
            LastPostUri = uri;
            LastPostBody = jsonBody;
            return Task.FromResult(_postResponse ?? "{}");
        }
    }

    /// <summary>合成字节流：先吐 head，再无限/定量重复填充；用于测试下载大小与 magic 判定。</summary>
    private sealed class SyntheticStream : Stream
    {
        private readonly byte[] _head;
        private readonly long _total;
        private long _position;

        public SyntheticStream(byte[] data)
            : this(data, data.LongLength)
        {
        }

        public SyntheticStream(byte fill, long total)
            : this(Array.Empty<byte>(), total, fill)
        {
        }

        private SyntheticStream(byte[] head, long total, byte fill = 0xAA)
        {
            _head = head;
            _total = total;
            Fill = fill;
        }

        private byte Fill { get; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _total;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_position >= _total)
            {
                return 0;
            }

            var want = (int)Math.Min(buffer.Length, _total - _position);
            for (var i = 0; i < want; i++)
            {
                var absolute = _position + i;
                buffer[i] = absolute < _head.Length ? _head[absolute] : Fill;
            }

            _position += want;
            return want;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Read(buffer.Span));
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>每次读都阻塞到取消，用于验证取消传播。</summary>
    private sealed class SlowStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => long.MaxValue;

        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingMetadataClient : IMetadataClient
    {
        public int CallCount { get; private set; }

        /// <summary>fake 元数据里 ck3 的 background URL；改它即可模拟「P 社换了素材」。</summary>
        public string BackgroundUrl { get; set; } = AssetPrefix + "ck3/launcher-background-image-abc";

        /// <summary>fake 元数据里的 logo URL；非空时远端素材变成 2 项，用于验证「部分失败」。</summary>
        public string? LogoUrl { get; set; }

        public void Reset() => CallCount = 0;

        public Task<IReadOnlyList<GameMetadataDto>> GetMetadataAsync(
            IReadOnlyList<string> gameIds,
            CancellationToken cancellationToken)
        {
            CallCount++;
            IReadOnlyList<GameMetadataDto> result = new[]
            {
                new GameMetadataDto(
                    "ck3",
                    "Crusader Kings III",
                    "../binaries/ck3",
                    new[] { "-gdpr-compliant" },
                    "../game",
                    "https://store.steampowered.com/dlc/1158310/",
                    null,
                    "1",
                    new ThemeSettingsDto(
                        null,
                        null,
                        BackgroundUrl,
                        LogoUrl,
                        null,
                        null,
                        "hsla(53, 31%, 85%, 0.9)",
                        "hsla(53, 36%, 95%, 1)",
                        24d,
                        200,
                        null)),
            };
            return Task.FromResult(result);
        }
    }

    private sealed class CountingAssetDownloader : IAssetDownloader
    {
        private readonly IFileSystem _fs;

        public CountingAssetDownloader(IFileSystem fs) => _fs = fs;

        public int CallCount { get; private set; }

        public bool FailEverything { get; set; }

        /// <summary>只让匹配的 URL 失败，用来模拟「部分素材下不下来」。</summary>
        public Func<string, bool>? FailPredicate { get; set; }

        /// <summary>成功时写出的扩展名；改成 .jpg 可模拟「magic 判型变化导致缓存文件名变化」。</summary>
        public string OutputExtension { get; set; } = ".png";

        public void Reset() => CallCount = 0;

        public Task<string?> DownloadAsync(
            string url,
            string targetPathWithoutExtension,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (FailEverything || (FailPredicate is not null && FailPredicate(url)))
            {
                return Task.FromResult<string?>(null);
            }

            var path = targetPathWithoutExtension + OutputExtension;
            _fs.WriteAllBytesAtomic(path, BuildAsset(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, 8192));
            return Task.FromResult<string?>(path);
        }
    }
}
