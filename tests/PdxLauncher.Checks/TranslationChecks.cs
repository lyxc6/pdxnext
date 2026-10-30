namespace PdxLauncher.Checks;

using System.Net;
using System.Net.Http;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Storage;
using PdxLauncher.Core.Text;

/// <summary>
/// 翻译层自检（不真联网）：Markdown 结构保持、译文缓存读写与过期、
/// 引擎模式的选择与回落、选项归一化，以及域名授权闸门。
/// 所有 HTTP 均通过 fake ITranslationTransport 注入。
/// </summary>
public static class TranslationChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        try
        {
            CheckMarkdownTranslator();
            CheckTranslationOptions();
            CheckTranslationCache();
            CheckTranslationService();
            CheckEngineModes();
            CheckMicrosoftTranslatorEngine();
            CheckGoogleCloudTranslationEngine();
            CheckMultiEngineFallback();
            CheckTransportHostGate();
        }
        finally
        {
            // 本类不落盘，没有临时目录需要清理。
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- Markdown

    private static void CheckMarkdownTranslator()
    {
        var translator = new MarkdownTranslator((text, _) => Task.FromResult<string?>("译:" + text));
        var document = new MarkdownDocument(
            new MarkdownBlock[]
            {
                new MarkdownHeading(1, new MarkdownInline[] { new MarkdownTextInline("Title") }),
                new MarkdownParagraph(new MarkdownInline[] { new MarkdownTextInline("Hello") }),
                new MarkdownCodeBlock("var x = 1;", "cs"),
                new MarkdownRule(),
                new MarkdownListItem(0, true, 1, new MarkdownInline[] { new MarkdownTextInline("Item") }),
            },
            false);

        var translated = translator
            .TranslateAsync(document, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Equal("译文块数与原文一致", document.Blocks.Count, translated.Blocks.Count);

        var heading = translated.Blocks[0] as MarkdownHeading;
        ChecksContext.Check("标题仍是标题", heading is not null);
        ChecksContext.Equal("标题层级不变", 1, heading?.Level ?? 0);
        ChecksContext.Equal("标题文本已翻译", "译:Title", (heading?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);

        var paragraph = translated.Blocks[1] as MarkdownParagraph;
        ChecksContext.Equal("段落文本已翻译", "译:Hello", (paragraph?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);

        var code = translated.Blocks[2] as MarkdownCodeBlock;
        ChecksContext.Equal("代码块不翻译", "var x = 1;", code?.Text ?? string.Empty);

        var item = translated.Blocks[4] as MarkdownListItem;
        ChecksContext.Equal("列表序号不变", 1, item?.Number ?? 0);
        ChecksContext.Equal("列表文本已翻译", "译:Item", (item?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);

        // 译不出来时保留原文，绝不把内容改空。
        var failing = new MarkdownTranslator((_, _) => Task.FromResult<string?>(null));
        var kept = failing
            .TranslateAsync(
                new MarkdownDocument(new MarkdownBlock[] { new MarkdownParagraph(new MarkdownInline[] { new MarkdownTextInline("Keep me") }) }, false),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var keptParagraph = kept.Blocks.FirstOrDefault() as MarkdownParagraph;
        ChecksContext.Equal("译文为空时保留原文", "Keep me", (keptParagraph?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);

        // 连续纯文本合并成一次翻译请求。
        var calls = 0;
        var counting = new MarkdownTranslator((text, _) =>
        {
            calls++;
            return Task.FromResult<string?>("译:" + text);
        });
        counting
            .TranslateAsync(
                new MarkdownDocument(
                    new MarkdownBlock[]
                    {
                        new MarkdownParagraph(new MarkdownInline[]
                        {
                            new MarkdownTextInline("A"),
                            new MarkdownTextInline("B"),
                        }),
                    },
                    false),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("连续纯文本合并为一次请求", 1, calls);

        // 空文档不炸。
        var empty = translator.TranslateAsync(null, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("空文档返回空文档", empty.IsEmpty);
    }

    // ---------------------------------------------------------------- 选项

    private static void CheckTranslationOptions()
    {
        var settings = new AppSettings
        {
            TranslateEnabled = true,
            TranslateEngineMode = "custom",
            TranslateBaseUrl = "  https://api.example.com/v1  ",
            TranslateApiKey = "  sk-test  ",
            TranslateModel = "  ",
            TranslateTargetLanguage = "  ",
            TranslateCacheDays = 9999,
        };

        var options = TranslationOptions.FromSettings(settings, "en-US");
        ChecksContext.Equal("引擎模式归一化", TranslationEngineModes.Custom, options.EngineMode);
        ChecksContext.Equal("接口地址去空白", "https://api.example.com/v1", options.BaseUrl ?? string.Empty);
        ChecksContext.Equal("密钥去空白", "sk-test", options.ApiKey ?? string.Empty);
        ChecksContext.Equal("模型留空时用默认", TranslationModels.Default, options.Model);
        ChecksContext.Equal("目标语言跟随界面语言", TranslationOptions.English, options.TargetLanguage);
        ChecksContext.Equal("缓存天数夹到上限", 365, options.CacheDays);
        ChecksContext.Check("自填配置齐备", options.HasCustomConfiguration);

        var unknown = TranslationOptions.FromSettings(new AppSettings { TranslateEngineMode = "???", TranslateTargetLanguage = "fr" }, "zh-CN");
        ChecksContext.Equal("未知引擎模式落自动", TranslationEngineModes.Auto, unknown.EngineMode);
        ChecksContext.Equal("未知目标语言落中文", TranslationOptions.SimplifiedChinese, unknown.TargetLanguage);
        ChecksContext.Equal("界面中文时目标语言为中文", TranslationOptions.SimplifiedChinese, TranslationOptions.ResolveTargetLanguage("zh-CN"));
        ChecksContext.Equal("界面英文时目标语言为英文", TranslationOptions.English, TranslationOptions.ResolveTargetLanguage("en-US"));

        var none = TranslationOptions.FromSettings(null, "en-US");
        ChecksContext.Check("没有设置时翻译关闭", !none.Enabled);
    }

    // ---------------------------------------------------------------- 缓存

    private static void CheckTranslationCache()
    {
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, new RecordingLogger());

        ChecksContext.Equal("缓存文档键", JsonDocumentKeys.Translations, TranslationCache.DocumentKey);

        var key = TranslationCache.BuildKey("engine", "zh-CN", "hello");
        ChecksContext.Check("缓存键稳定", key == TranslationCache.BuildKey("engine", "zh-CN", "hello"));
        ChecksContext.Check("不同原文不同键", key != TranslationCache.BuildKey("engine", "zh-CN", "hello!"));

        cache
            .SetAsync("engine", "zh-CN", "hello", "你好", CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        var hit = cache.TryGetAsync("engine", "zh-CN", "hello", 30, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("缓存命中", "你好", hit ?? string.Empty);

        var miss = cache.TryGetAsync("engine", "en-US", "hello", 30, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("目标语言不同不命中", miss is null);

        // 超过保留天数后过期。
        clock.Advance(TimeSpan.FromDays(40));
        var expired = cache.TryGetAsync("engine", "zh-CN", "hello", 30, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("超过保留天数后过期", expired is null);

        // 同步清空。
        cache
            .SetAsync("engine", "zh-CN", "hello", "你好", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        cache.Clear();
        var cleared = cache.TryGetAsync("engine", "zh-CN", "hello", 30, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("清空后不再命中", cleared is null);
        ChecksContext.Check("清空后文档被删除", !documents.Exists(JsonDocumentKeys.Translations));
    }

    // ---------------------------------------------------------------- 服务

    private static void CheckTranslationService()
    {
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, logger);

        var custom = new StubEngine("custom", configured: true, succeed: false);
        var builtin = new StubEngine("builtin", configured: true, succeed: true);
        var service = new TranslationService(new ITranslationEngine[] { custom, builtin }, cache, logger);
        // 专档位由上层只把对应引擎放进来，测试里同样按档位给不同引擎列表。
        var builtinService = new TranslationService(new ITranslationEngine[] { builtin }, cache, logger);

        var auto = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Auto, TranslateBaseUrl = "https://api.example.com", TranslateApiKey = "k" },
            "zh-CN");

        var autoResults = service.TranslateAsync(new[] { "a" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("自动模式下自填失败后回落内置", "builtin:a", autoResults.Count > 0 ? autoResults[0] ?? string.Empty : string.Empty);
        ChecksContext.Check("自动模式确实调过自填引擎", custom.Calls > 0);
        ChecksContext.Check("自动模式确实调过内置引擎", builtin.Calls > 0);

        // 仅自填模式：失败就是失败，不回落。
        custom.Calls = 0;
        builtin.Calls = 0;
        var customOnly = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Custom, TranslateBaseUrl = "https://api.example.com", TranslateApiKey = "k" },
            "zh-CN");
        var customResults = service.TranslateAsync(new[] { "b" }, customOnly, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("仅自填模式失败时返回 null", customResults.Count > 0 && customResults[0] is null);
        ChecksContext.Equal("仅自填模式不调内置引擎", 0, builtin.Calls);

        // 仅内置模式：不碰自填引擎。
        custom.Calls = 0;
        builtin.Calls = 0;
        var builtinOnly = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Builtin },
            "zh-CN");
        var builtinResults = builtinService.TranslateAsync(new[] { "c" }, builtinOnly, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("仅内置模式用内置引擎翻译", "builtin:c", builtinResults.Count > 0 ? builtinResults[0] ?? string.Empty : string.Empty);
        ChecksContext.Equal("仅内置模式不碰自填引擎", 0, custom.Calls);

        // 关闭翻译时不请求任何引擎。
        custom.Calls = 0;
        builtin.Calls = 0;
        var disabled = TranslationOptions.FromSettings(new AppSettings { TranslateEnabled = false }, "zh-CN");
        var disabledResults = service.TranslateAsync(new[] { "d" }, disabled, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("关闭翻译时返回 null", disabledResults.Count > 0 && disabledResults[0] is null);
        ChecksContext.Equal("关闭翻译时不请求引擎", 0, custom.Calls + builtin.Calls);

        // 命中缓存不再请求引擎。
        builtin.Calls = 0;
        var cached = builtinService.TranslateAsync(new[] { "c" }, builtinOnly, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("命中缓存返回同一译文", "builtin:c", cached.Count > 0 ? cached[0] ?? string.Empty : string.Empty);
        ChecksContext.Equal("命中缓存不再请求引擎", 0, builtin.Calls);
    }

    // ---------------------------------------------------------------- 引擎档位

    private static void CheckEngineModes()
    {
        ChecksContext.Equal("微软档位归一化", TranslationEngineModes.Microsoft, TranslationEngineModes.Normalize("microsoft"));
        ChecksContext.Equal("微软档位大小写不敏感", TranslationEngineModes.Microsoft, TranslationEngineModes.Normalize("  MICROSOFT "));
        ChecksContext.Equal("谷歌云档位归一化", TranslationEngineModes.GoogleCloud, TranslationEngineModes.Normalize("google-cloud"));
        ChecksContext.Check("档位表含五个选项", TranslationEngineModes.All.Count == 5);
        ChecksContext.Check("档位表含微软", TranslationEngineModes.All.Contains(TranslationEngineModes.Microsoft));
        ChecksContext.Check("档位表含谷歌云", TranslationEngineModes.All.Contains(TranslationEngineModes.GoogleCloud));

        var options = TranslationOptions.FromSettings(
            new AppSettings
            {
                TranslateEnabled = true,
                TranslateEngineMode = TranslationEngineModes.Microsoft,
                TranslateMicrosoftKey = " ms-key ",
                TranslateMicrosoftRegion = " eastasia ",
                TranslateGoogleKey = " g-key ",
            },
            "zh-CN");
        ChecksContext.Check("微软凭据齐备", options.HasMicrosoftConfiguration);
        ChecksContext.Check("谷歌凭据齐备", options.HasGoogleCloudConfiguration);
        ChecksContext.Equal("微软密钥去空白", "ms-key", options.MicrosoftKey ?? string.Empty);
        ChecksContext.Equal("微软区域去空白", "eastasia", options.MicrosoftRegion ?? string.Empty);
        ChecksContext.Equal("谷歌密钥去空白", "g-key", options.GoogleKey ?? string.Empty);

        // 只填密钥不填区域时，微软不算配置齐备。
        var half = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateMicrosoftKey = "k" },
            "zh-CN");
        ChecksContext.Check("只填密钥不算微软配置齐备", !half.HasMicrosoftConfiguration);
    }

    // ---------------------------------------------------------------- 微软机翻

    private static void CheckMicrosoftTranslatorEngine()
    {
        var transport = new RecordingTransport("[{\"translations\":[{\"text\":\"你好\"}]}]");
        var logger = new RecordingLogger();
        var engine = new MicrosoftTranslatorEngine(transport, logger, "ms-secret", "eastasia");

        ChecksContext.Equal("微软引擎标识", "microsoft", engine.Id);
        ChecksContext.Check("密钥加区域才算配置好", engine.IsConfigured);

        var translated = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("微软引擎返回译文", "你好", translated ?? string.Empty);

        var request = transport.Requests.LastOrDefault();
        ChecksContext.Check("微软引擎发出了请求", request is not null);
        ChecksContext.Equal("微软接口用 POST", HttpMethod.Post, request?.Method ?? HttpMethod.Get);
        ChecksContext.Equal("微软接口主机", "api.cognitiveservices.azure.com", request?.Uri.Host ?? string.Empty);
        ChecksContext.Check("微软接口带 api-version", (request?.Uri.Query ?? string.Empty).Contains("api-version=3.0", StringComparison.Ordinal));
        ChecksContext.Check("中文目标语言用 zh-Hans", (request?.Uri.Query ?? string.Empty).Contains("to=zh-Hans", StringComparison.Ordinal));
        ChecksContext.Check("微软请求带订阅密钥头", request?.Headers.ContainsKey("Ocp-Apim-Subscription-Key") == true);
        ChecksContext.Check("微软请求带区域头", request?.Headers.ContainsKey("Ocp-Apim-Subscription-Region") == true);
        ChecksContext.Check("微软请求体是数组", (request?.Body ?? string.Empty).TrimStart().StartsWith("[", StringComparison.Ordinal));
        ChecksContext.Check("微软请求体带 Text 字段", (request?.Body ?? string.Empty).Contains("\"Text\"", StringComparison.Ordinal));

        // 英文目标语言用 en。
        transport.Responses.Clear();
        transport.Responses.Add("[{\"translations\":[{\"text\":\"hi\"}]}]");
        engine.TranslateAsync("hello", "en-US", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("英文目标语言用 en", (transport.Requests.LastOrDefault()?.Uri.Query ?? string.Empty).Contains("to=en", StringComparison.Ordinal));

        // 失败只记状态码，不把密钥写进日志。
        transport.Responses.Clear();
        transport.Responses.Add(string.Empty);
        transport.StatusCode = 401;
        var failed = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("微软接口失败时返回 null", failed is null);
        ChecksContext.Check("失败日志只记状态码", logger.Warnings.Any(w => w.Contains("401", StringComparison.Ordinal)));
        ChecksContext.Check("日志不含订阅密钥", !logger.Warnings.Any(w => w.Contains("ms-secret", StringComparison.Ordinal)));

        // 没配齐凭据时不发请求。
        var unconfigured = new MicrosoftTranslatorEngine(transport, logger, "only-key", null);
        ChecksContext.Check("缺区域时未配置", !unconfigured.IsConfigured);
        var before = transport.Requests.Count;
        var skipped = unconfigured.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("未配置时返回 null", skipped is null);
        ChecksContext.Equal("未配置时不发请求", before, transport.Requests.Count);
    }

    // ---------------------------------------------------------------- 谷歌云机翻

    private static void CheckGoogleCloudTranslationEngine()
    {
        var transport = new RecordingTransport("{\"data\":{\"translations\":[{\"translatedText\":\"你好\"}]}}");
        var logger = new RecordingLogger();
        var engine = new GoogleCloudTranslationEngine(transport, logger, "g-secret");

        ChecksContext.Equal("谷歌引擎标识", "google-cloud", engine.Id);
        ChecksContext.Check("有密钥就算配置好", engine.IsConfigured);

        var translated = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("谷歌引擎返回译文", "你好", translated ?? string.Empty);

        var request = transport.Requests.LastOrDefault();
        ChecksContext.Check("谷歌引擎发出了请求", request is not null);
        ChecksContext.Equal("谷歌接口用 POST", HttpMethod.Post, request?.Method ?? HttpMethod.Get);
        ChecksContext.Equal("谷歌接口主机", "translation.googleapis.com", request?.Uri.Host ?? string.Empty);
        ChecksContext.Check("谷歌接口带 key 参数", (request?.Uri.Query ?? string.Empty).Contains("key=g-secret", StringComparison.Ordinal));
        ChecksContext.Check("谷歌请求体带 q 字段", (request?.Body ?? string.Empty).Contains("\"q\"", StringComparison.Ordinal));
        ChecksContext.Check("谷歌请求体带中文目标语言", (request?.Body ?? string.Empty).Contains("zh-CN", StringComparison.Ordinal));
        ChecksContext.Check("谷歌请求体带 format", (request?.Body ?? string.Empty).Contains("\"format\"", StringComparison.Ordinal));

        // 英文目标语言。
        transport.Responses.Clear();
        transport.Responses.Add("{\"data\":{\"translations\":[{\"translatedText\":\"hi\"}]}}");
        engine.TranslateAsync("hello", "en-US", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("谷歌英文目标语言用 en", (transport.Requests.LastOrDefault()?.Body ?? string.Empty).Contains("\"target\":\"en\"", StringComparison.Ordinal));

        // 失败只记状态码，不把密钥写进日志。
        transport.Responses.Clear();
        transport.Responses.Add(string.Empty);
        transport.StatusCode = 403;
        var failed = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("谷歌接口失败时返回 null", failed is null);
        ChecksContext.Check("谷歌失败日志只记状态码", logger.Warnings.Any(w => w.Contains("403", StringComparison.Ordinal)));
        ChecksContext.Check("谷歌日志不含密钥", !logger.Warnings.Any(w => w.Contains("g-secret", StringComparison.Ordinal)));

        // 没配密钥时不发请求。
        var unconfigured = new GoogleCloudTranslationEngine(transport, logger, "  ");
        ChecksContext.Check("没有密钥时未配置", !unconfigured.IsConfigured);
        var before = transport.Requests.Count;
        var skipped = unconfigured.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("谷歌未配置时返回 null", skipped is null);
        ChecksContext.Equal("谷歌未配置时不发请求", before, transport.Requests.Count);
    }

    // ---------------------------------------------------------------- 多引擎回落

    private static void CheckMultiEngineFallback()
    {
        var logger = new RecordingLogger();
        var first = new StubEngine("first", configured: true, succeed: false);
        var second = new StubEngine("second", configured: true, succeed: true);
        var third = new StubEngine("third", configured: true, succeed: true);
        var service = new TranslationService(new ITranslationEngine[] { first, second, third }, null, logger);

        var auto = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Auto },
            "zh-CN");
        var autoResults = service.TranslateAsync(new[] { "a" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("自动模式由第二个引擎补齐", "second:a", autoResults.Count > 0 ? autoResults[0] ?? string.Empty : string.Empty);
        ChecksContext.Check("自动模式确实调过第一个引擎", first.Calls > 0);
        ChecksContext.Check("自动模式确实调过第二个引擎", second.Calls > 0);
        ChecksContext.Equal("成功后不再调后面的引擎", 0, third.Calls);

        // 专档位严格不回落。
        first.Calls = 0;
        second.Calls = 0;
        third.Calls = 0;
        var microsoftOnly = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Microsoft },
            "zh-CN");
        var strictResults = service.TranslateAsync(new[] { "b" }, microsoftOnly, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("专档位失败时返回 null", strictResults.Count > 0 && strictResults[0] is null);
        ChecksContext.Equal("专档位只调第一个引擎", 1, first.Calls);
        ChecksContext.Equal("专档位不调后面的引擎", 0, second.Calls + third.Calls);

        // 没配置好的引擎会被跳过。
        var unconfigured = new StubEngine("unconfigured", configured: false, succeed: true);
        var configured = new StubEngine("configured", configured: true, succeed: true);
        var mixed = new TranslationService(new ITranslationEngine[] { unconfigured, configured }, null, logger);
        var mixedResults = mixed.TranslateAsync(new[] { "c" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("跳过未配置的引擎", "configured:c", mixedResults.Count > 0 ? mixedResults[0] ?? string.Empty : string.Empty);
        ChecksContext.Equal("未配置的引擎没被调用", 0, unconfigured.Calls);
    }

    // ---------------------------------------------------------------- 域名闸门

    private static void CheckTransportHostGate()
    {
        using var transport = new TranslationHttpTransport(new RecordingLogger());

        var blocked = false;
        try
        {
            transport
                .SendAsync(HttpMethod.Get, new Uri("https://not-allowed.example.com/translate"), null, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (HttpRequestException ex)
        {
            blocked = ex.Message.Contains("host-not-allowed", StringComparison.Ordinal);
        }

        ChecksContext.Check("未授权域名被拦下", blocked);

        var authorized = OnlineHosts.AuthorizeBaseUrl("https://my-translate.example.com/v1");
        ChecksContext.Check("授权自填接口地址成功", authorized);
        ChecksContext.Check("授权后主机被放行", OnlineHosts.IsAllowedHost("my-translate.example.com"));
        ChecksContext.Check("授权不放行子域", !OnlineHosts.IsAllowedHost("sub.my-translate.example.com"));
        OnlineHosts.RevokeHost("my-translate.example.com");
        ChecksContext.Check("撤销授权后不放行", !OnlineHosts.IsAllowedHost("my-translate.example.com"));
    }

    // ---------------------------------------------------------------- 测试替身

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        string? Body,
        IReadOnlyDictionary<string, string> Headers);

    /// <summary>假传输：按预设队列返回响应，并记录每次请求。</summary>
    private sealed class RecordingTransport : ITranslationTransport
    {
        private readonly List<string> _responses = new();

        public RecordingTransport(params string[] responses)
        {
            _responses.AddRange(responses);
        }

        public List<RecordedRequest> Requests { get; } = new();

        public List<string> Responses => _responses;

        public int StatusCode { get; set; } = 200;

        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            var recorded = new RecordedRequest(
                method,
                uri,
                jsonBody,
                headers is null
                    ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase));
            Requests.Add(recorded);

            if (_responses.Count == 0)
            {
                return Task.FromResult(TranslationResponse.Failure(StatusCode, "no-response"));
            }

            var body = _responses.Count > 0 ? _responses[0] : string.Empty;
            if (_responses.Count > 0)
            {
                _responses.RemoveAt(0);
            }

            if (StatusCode < 200 || StatusCode >= 300)
            {
                return Task.FromResult(TranslationResponse.Failure(StatusCode, "http-error"));
            }

            return Task.FromResult(new TranslationResponse(true, StatusCode, body, string.Empty));
        }
    }

    private sealed class StubEngine : ITranslationEngine
    {
        private readonly bool _configured;
        private readonly bool _succeed;

        public StubEngine(string id, bool configured, bool succeed)
        {
            Id = id;
            _configured = configured;
            _succeed = succeed;
        }

        public string Id { get; }

        public bool IsConfigured => _configured;

        public int Calls { get; set; }

        public Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult<string?>(_succeed ? Id + ":" + text : null);
        }

        public Task<IReadOnlyList<string?>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken)
        {
            Calls++;
            var results = new string?[texts.Count];
            if (_succeed)
            {
                for (var i = 0; i < texts.Count; i++)
                {
                    results[i] = Id + ":" + texts[i];
                }
            }

            return Task.FromResult<IReadOnlyList<string?>>(results);
        }
    }

    private sealed class MemoryDocumentStore : IJsonDocumentStore
    {
        private readonly Dictionary<string, string> _documents = new(StringComparer.Ordinal);

        public bool TryRead(string key, out string? json)
        {
            if (_documents.TryGetValue(key, out var value))
            {
                json = value;
                return true;
            }

            json = null;
            return false;
        }

        public bool Write(string key, string json)
        {
            _documents[key] = json;
            return true;
        }

        public bool Delete(string key) => _documents.Remove(key);

        public bool Exists(string key) => _documents.ContainsKey(key);

        public long MeasureBytes(string prefix)
        {
            long total = 0;
            foreach (var pair in _documents)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    total += pair.Value.Length;
                }
            }

            return total;
        }

        public IReadOnlyList<string> List(string prefix) =>
            _documents.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
    }

    private sealed class MutableClock : IClock
    {
        private DateTimeOffset _now;

        public MutableClock(DateTimeOffset start) => _now = start;

        public DateTimeOffset UtcNow => _now;

        public void Advance(TimeSpan delta) => _now = _now.Add(delta);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? error = null) => Warnings.Add(message);
    }
}
