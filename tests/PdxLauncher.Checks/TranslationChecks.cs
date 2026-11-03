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
            CheckMyMemoryTranslationEngine();
            CheckFreeEngineHostFallback();
            CheckFreeEngineBatching();
            CheckFreeEngineRateLimit();
            CheckMyMemoryQuota();
            CheckTranslationFailureNote();
            CheckMultiEngineFailureNotes();
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
        ChecksContext.Equal("MyMemory 档位归一化", TranslationEngineModes.MyMemory, TranslationEngineModes.Normalize("mymemory"));
        ChecksContext.Equal("MyMemory 档位大小写不敏感", TranslationEngineModes.MyMemory, TranslationEngineModes.Normalize("  MYMEMORY "));
        ChecksContext.Check("档位表含九个选项", TranslationEngineModes.All.Count == 9);
        ChecksContext.Check("档位表含微软", TranslationEngineModes.All.Contains(TranslationEngineModes.Microsoft));
        ChecksContext.Check("档位表含谷歌云", TranslationEngineModes.All.Contains(TranslationEngineModes.GoogleCloud));
        ChecksContext.Check("档位表含 MyMemory", TranslationEngineModes.All.Contains(TranslationEngineModes.MyMemory));
        ChecksContext.Equal("MyMemory 排在微软之前", TranslationEngineModes.MyMemory, TranslationEngineModes.All[3]);

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

    // ---------------------------------------------------------------- MyMemory

    private static void CheckMyMemoryTranslationEngine()
    {
        var logger = new RecordingLogger();
        var transport = new RecordingTransport(
            "{\"responseData\":{\"translatedText\":\"你好\"},\"responseStatus\":200}");
        var engine = new MyMemoryTranslationEngine(transport, logger);

        ChecksContext.Equal("MyMemory 引擎标识", "mymemory", engine.Id);
        ChecksContext.Check("MyMemory 不需要配置", engine.IsConfigured);

        var translated = engine.TranslateAsync("Hello world", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("MyMemory 返回译文", "你好", translated ?? string.Empty);

        var request = transport.Requests.LastOrDefault();
        ChecksContext.Check("MyMemory 发出了请求", request is not null);
        ChecksContext.Equal("MyMemory 用 GET", HttpMethod.Get, request?.Method ?? HttpMethod.Post);
        ChecksContext.Equal("MyMemory 主机", OnlineHosts.MyMemoryTranslationHost, request?.Uri.Host ?? string.Empty);
        ChecksContext.Check("MyMemory 源语言写 Autodetect", (request?.Uri.Query ?? string.Empty).Contains("Autodetect", StringComparison.Ordinal));
        ChecksContext.Check("MyMemory 中文目标语言写 zh-CN", (request?.Uri.Query ?? string.Empty).Contains("zh-CN", StringComparison.Ordinal));

        // 出错时 responseStatus 是字符串、responseData 本身也变成错误说明。
        transport.Responses.Clear();
        transport.Responses.Add("{\"responseData\":\"QUERY LENGTH LIMIT EXCEEDED. MAX ALLOWED QUERY : 500 CHARS\",\"responseStatus\":\"403\"}");
        var limited = engine.TranslateAsync("x", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("MyMemory 超长响应返回 null", limited is null);
        ChecksContext.Check("MyMemory 超长响应记状态码", logger.Warnings.Any(w => w.Contains("403", StringComparison.Ordinal)));

        // 非 2xx：只记状态码，不抛。
        transport.Responses.Clear();
        transport.Responses.Add(string.Empty);
        transport.StatusCode = 500;
        var failed = engine.TranslateAsync("x", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("MyMemory HTTP 失败返回 null", failed is null);
        ChecksContext.Check("MyMemory HTTP 失败只记状态码", logger.Warnings.Any(w => w.Contains("HTTP 500", StringComparison.Ordinal)));
        transport.StatusCode = 200;

        // 英文目标语言用 en。
        transport.Responses.Clear();
        transport.Responses.Add("{\"responseData\":{\"translatedText\":\"hi\"},\"responseStatus\":200}");
        engine.TranslateAsync("你好", "en-US", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("MyMemory 英文目标语言用 en", (transport.Requests.LastOrDefault()?.Uri.Query ?? string.Empty).Contains("%7Cen", StringComparison.Ordinal));

        // 切块：600 多字符按句读下刀，每块不超过上限，拼回去等于原文。
        var sentence = new string('a', 250) + "。";
        var longText = sentence + sentence + new string('b', 90);
        var chunks = MyMemoryTranslationEngine.SplitText(longText);
        ChecksContext.Check("MyMemory 长文本切成多块", chunks.Count > 1);
        ChecksContext.Check("MyMemory 每块不超过上限", chunks.All(chunk => chunk.Length <= MyMemoryTranslationEngine.MaxChunkChars));
        ChecksContext.Equal("MyMemory 切块拼回原文", longText, string.Concat(chunks));

        // 空文本不请求、原样返回。
        var beforeEmpty = transport.Requests.Count;
        var blank = engine.TranslateAsync("   ", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("MyMemory 空文本原样返回", "   ", blank ?? string.Empty);
        ChecksContext.Equal("MyMemory 空文本不发请求", beforeEmpty, transport.Requests.Count);

        // 批量：并发发出，结果必须与入参同序。
        var batchEngine = new MyMemoryTranslationEngine(new EchoTranslationTransport(), logger);
        var batch = batchEngine
            .TranslateBatchAsync(new[] { "one", "two", "three", "four", "five", "six" }, "zh-CN", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("MyMemory 批量条数一致", 6, batch.Count);
        ChecksContext.Equal("MyMemory 批量保序（首项）", "译:one", batch[0] ?? string.Empty);
        ChecksContext.Equal("MyMemory 批量保序（末项）", "译:six", batch[5] ?? string.Empty);

        // 首错短路：一台主机挂掉后，不必把剩下的几十条都白打一遍。
        var boom = new FailingTranslationTransport();
        var failingEngine = new MyMemoryTranslationEngine(boom, logger);
        var many = Enumerable.Range(0, 24).Select(index => "text" + index).ToArray();
        var failedBatch = failingEngine.TranslateBatchAsync(many, "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("MyMemory 批量全失败时返回 null", failedBatch.All(item => item is null));
        ChecksContext.Check("MyMemory 首错后短路", boom.Requests < many.Length);
    }

    // ---------------------------------------------------------------- 免费引擎多主机

    private static void CheckFreeEngineHostFallback()
    {
        var logger = new RecordingLogger();
        var health = new TranslationEndpointHealth();
        const string translatedBody = "[[\"你好\",\"en\"]]";

        // 首选主机 TLS 握手失败（抛网络异常），备用主机正常返回。
        var transport = new HostRoutingTransport(uri =>
            uri.Host == OnlineHosts.GoogleTranslateWebHost ? null : translatedBody);
        var engine = new FreeTranslationEngine(transport, logger, health);

        var translated = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("免费引擎主机回落出译文", "你好", translated ?? string.Empty);
        ChecksContext.Equal("免费引擎先试首选主机", OnlineHosts.GoogleTranslateWebHost, transport.Requests[0].Uri.Host);
        ChecksContext.Equal("免费引擎回落 clients5", OnlineHosts.Clients5TranslationHost, transport.Requests[1].Uri.Host);
        ChecksContext.Check("备用主机成功后不记失败", engine.LastFailure is null);
        ChecksContext.Check("成功的主机不进冷却名单", !health.IsDead(OnlineHosts.Clients5TranslationHost));
        ChecksContext.Check("连不上的主机进冷却名单", health.IsDead(OnlineHosts.GoogleTranslateWebHost));

        // 冷却期内不再白试一次超时：只打备用主机。
        transport.Requests.Clear();
        engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("冷却后只发一条请求", 1, transport.Requests.Count);
        ChecksContext.Equal("冷却后打的是备用主机", OnlineHosts.Clients5TranslationHost, transport.Requests[0].Uri.Host);

        // 冷却解除后重新给首选主机机会。
        health.MarkHealthy(OnlineHosts.GoogleTranslateWebHost);
        transport.Requests.Clear();
        engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("冷却解除后先试首选主机", OnlineHosts.GoogleTranslateWebHost, transport.Requests[0].Uri.Host);

        // 三台都不通：返回 null，且原因指向主机。
        var allDead = new TranslationEndpointHealth();
        foreach (var host in FreeTranslationEngine.Hosts)
        {
            allDead.MarkDead(host, TimeSpan.FromMinutes(10));
        }

        var deadTransport = new HostRoutingTransport(_ => null);
        var deadEngine = new FreeTranslationEngine(deadTransport, logger, allDead);
        var deadResult = deadEngine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("全部主机冷却时返回 null", deadResult is null);
        ChecksContext.Check("全部主机不可用时不发请求", deadTransport.Requests.Count == 0);
        ChecksContext.Check("全部主机不可用时记原因", !string.IsNullOrWhiteSpace(deadEngine.LastFailure));

        ChecksContext.Check(
            "首选翻译主机在放行名单里",
            OnlineHosts.IsAllowedHost(OnlineHosts.GoogleTranslateWebHost));
        ChecksContext.Check(
            "备用翻译主机在放行名单里",
            OnlineHosts.IsAllowedHost(OnlineHosts.Clients5TranslationHost));
    }

    private static void CheckFreeEngineBatching()
    {
        var logger = new RecordingLogger();
        var health = new TranslationEndpointHealth();
        var transport = new BatchEchoTransport();
        var engine = new FreeTranslationEngine(transport, logger, health);

        var three = engine.TranslateBatchAsync(new[] { "one", "two", "three" }, "zh-CN", CancellationToken.None)
            .GetAwaiter().GetResult();
        ChecksContext.Equal("批量合并成一条请求", 1, transport.Requests.Count);
        ChecksContext.Equal("批量结果保序", "译1|译2|译3", string.Join("|", three));
        ChecksContext.Equal("批量打的是 translate_a/t", "/translate_a/t", transport.Requests[0].Uri.AbsolutePath);
        ChecksContext.Equal("批量带三个 q 参数", 3, CountQueryValues(transport.Requests[0].Uri));

        transport.Requests.Clear();
        var many = Enumerable.Range(0, 20).Select(index => "text" + index).ToArray();
        var batch = engine.TranslateBatchAsync(many, "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("20 段不超过 3 条请求", transport.Requests.Count <= 3);
        ChecksContext.Check(
            "每组合并段数不超过上限",
            transport.Requests.All(request => CountQueryValues(request.Uri) <= FreeTranslationEngine.MaxTextsPerRequest));
        ChecksContext.Check("20 段每段都有译文", batch.All(item => item is not null));
    }

    private static void CheckFreeEngineRateLimit()
    {
        var logger = new RecordingLogger();
        var health = new TranslationEndpointHealth();
        var transport = new StatusRoutingTransport(new Dictionary<string, (int Status, string? Body)>
        {
            [OnlineHosts.GoogleTranslateWebHost] = (429, "<html>rate limited</html>"),
            [OnlineHosts.Clients5TranslationHost] = (200, "[[\"你好\",\"en\"]]"),
            [OnlineHosts.TranslationHost] = (200, "[[\"你好\",\"en\"]]"),
        });
        var engine = new FreeTranslationEngine(transport, logger, health);

        var translated = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("限流后换主机仍出译文", "你好", translated ?? string.Empty);
        ChecksContext.Check("429 的主机进短冷却", health.IsDead(OnlineHosts.GoogleTranslateWebHost));
        ChecksContext.Check("429 写进日志", logger.Warnings.Any(text => text.Contains("429", StringComparison.Ordinal)));

        // 冷却后下一条只打可用主机，不再撞限流那台。
        transport.Requests.Clear();
        engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("限流冷却后只发一条请求", 1, transport.Requests.Count);
        ChecksContext.Equal("限流冷却后打的是可用主机", OnlineHosts.Clients5TranslationHost, transport.Requests[0].Uri.Host);

        // 三台都 429：本批没有译文，原因里要能看见主机名。
        var allLimitedHealth = new TranslationEndpointHealth();
        var limitedTransport = new StatusRoutingTransport(
            FreeTranslationEngine.Hosts.ToDictionary(host => host, _ => (429, (string?)"quota")));
        var limitedEngine = new FreeTranslationEngine(limitedTransport, logger, allLimitedHealth);
        var none = limitedEngine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("三台都限流时返回 null", none is null);
        ChecksContext.Check(
            "三台都限流时原因带主机名",
            (limitedEngine.LastFailure ?? string.Empty).Contains(OnlineHosts.GoogleTranslateWebHost, StringComparison.Ordinal));
    }

    /// <summary>统计地址里 <c>q=</c> 参数个数。</summary>
    private static int CountQueryValues(Uri uri)
    {
        var query = uri.Query.TrimStart('?');
        return query.Length == 0
            ? 0
            : query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Count(part => part.StartsWith("q=", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- MyMemory 额度

    private static void CheckMyMemoryQuota()
    {
        var logger = new RecordingLogger();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero));
        var health = new TranslationEndpointHealth(() => clock.UtcNow);
        var transport = new RecordingTransport(string.Empty) { StatusCode = 429 };
        var engine = new MyMemoryTranslationEngine(transport, logger, health);

        var blocked = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("MyMemory 429 返回 null", blocked is null);
        ChecksContext.Check("MyMemory 429 记为额度耗尽", health.IsQuotaExhausted);
        ChecksContext.Check("MyMemory 429 记下恢复时间", health.QuotaResumeAt is not null);
        ChecksContext.Check(
            "MyMemory 429 原因提到额度",
            (engine.LastFailure ?? string.Empty).Contains("额度", StringComparison.Ordinal));

        // 额度用完后本进程内不再逐块重试。
        var before = transport.Requests.Count;
        var again = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("额度耗尽后不重复请求", again is null && transport.Requests.Count == before);

        // 恢复时间过去后重新尝试。
        clock.Advance(TimeSpan.FromMinutes(31));
        transport.StatusCode = 200;
        transport.Responses.Clear();
        transport.Responses.Add("{\"responseData\":{\"translatedText\":\"你好\"},\"responseStatus\":200}");
        var recovered = engine.TranslateAsync("hello", "zh-CN", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("恢复时间过后重新翻译", "你好", recovered ?? string.Empty);
        ChecksContext.Check("恢复后清掉额度标记", !health.IsQuotaExhausted);
    }

    // ---------------------------------------------------------------- 多引擎失败原因

    private static void CheckMultiEngineFailureNotes()
    {
        var logger = new RecordingLogger();
        var auto = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Auto },
            "zh-CN");

        // 「自动」会横向回落：每个失败的引擎都要在提示里留名，而不是只报第一个。
        var service = new TranslationService(
            new ITranslationEngine[]
            {
                new StubEngine("alpha", configured: true, succeed: false),
                new StubEngine("beta", configured: true, succeed: false),
            },
            null,
            logger);
        service.TranslateAsync(new[] { "a" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        var note = service.LastFailure ?? string.Empty;
        ChecksContext.Check("多引擎失败时列出 alpha", note.Contains("alpha", StringComparison.Ordinal));
        ChecksContext.Check("多引擎失败时列出 beta", note.Contains("beta", StringComparison.Ordinal));

        // 有一个成功就不该再挂失败原因。
        var mixed = new TranslationService(
            new ITranslationEngine[]
            {
                new StubEngine("broken", configured: true, succeed: false),
                new StubEngine("ok", configured: true, succeed: true),
            },
            null,
            logger);
        mixed.TranslateAsync(new[] { "b" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("有引擎成功时不记失败原因", mixed.LastFailure is null);
    }

    // ---------------------------------------------------------------- 失败原因

    private static void CheckTranslationFailureNote()
    {
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, logger);
        var auto = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Auto },
            "zh-CN");

        // 全部失败：LastFailure 要带引擎标识，页面才拼得出可读提示。
        var broken = new TranslationService(new ITranslationEngine[] { new StubEngine("broken", configured: true, succeed: false) }, cache, logger);
        broken.TranslateAsync(new[] { "a" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        var failure = broken.LastFailure;
        ChecksContext.Check("全失败时记下原因", !string.IsNullOrWhiteSpace(failure));
        ChecksContext.Check("原因里带引擎标识", (failure ?? string.Empty).Contains("broken", StringComparison.Ordinal));
        ChecksContext.Check("全失败时同时记日志", logger.Warnings.Any(w => w.Contains("翻译全部失败", StringComparison.Ordinal)));

        // 有任意一条成功：不该再挂旧原因。
        var healthy = new TranslationService(new ITranslationEngine[] { new StubEngine("ok", configured: true, succeed: true) }, null, logger);
        healthy.TranslateAsync(new[] { "b" }, auto, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("有成功的批次不记失败原因", healthy.LastFailure is null);

        // 一个引擎都没有：原因要说清是模式问题。
        var empty = new TranslationService(Array.Empty<ITranslationEngine>(), null, logger);
        var disabled = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Microsoft },
            "zh-CN");
        empty.TranslateAsync(new[] { "c" }, disabled, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("没有引擎时也记原因", !string.IsNullOrWhiteSpace(empty.LastFailure));
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

    /// <summary>把请求里的 q 原样译回去的假传输：用来验证批量结果的顺序。</summary>
    private sealed class EchoTranslationTransport : ITranslationTransport
    {
        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            var text = ReadQueryValue(uri, "q") ?? string.Empty;
            var body = System.Text.Json.JsonSerializer.Serialize(new
            {
                responseData = new { translatedText = "译:" + text },
                responseStatus = 200,
            });
            return Task.FromResult(new TranslationResponse(true, 200, body, string.Empty));
        }

        private static string? ReadQueryValue(Uri uri, string name)
        {
            foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = pair.IndexOf('=', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    continue;
                }

                if (string.Equals(pair[..separator], name, StringComparison.Ordinal))
                {
                    return Uri.UnescapeDataString(pair[(separator + 1)..]);
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 按主机名决定结果的假传输：返回 null 表示这次「网络层失败」（抛异常），
    /// 返回字符串表示 200 加该 body。
    /// </summary>
    private sealed class HostRoutingTransport : ITranslationTransport
    {
        private readonly Func<Uri, string?> _respond;

        public HostRoutingTransport(Func<Uri, string?> respond) => _respond = respond;

        public List<RecordedRequest> Requests { get; } = new();

        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Requests.Add(
                new RecordedRequest(
                    method,
                    uri,
                    jsonBody,
                    headers is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)));

            var body = _respond(uri);
            if (body is null)
            {
                throw new HttpRequestException("schannel: failed to receive handshake");
            }

            return Task.FromResult(new TranslationResponse(true, 200, body, string.Empty));
        }
    }

    /// <summary>按主机返回预设状态码与响应体：用来测 429 冷却与换主机。</summary>
    private sealed class StatusRoutingTransport : ITranslationTransport
    {
        private readonly IReadOnlyDictionary<string, (int Status, string? Body)> _responses;

        public StatusRoutingTransport(IReadOnlyDictionary<string, (int Status, string? Body)> responses) =>
            _responses = responses;

        public List<RecordedRequest> Requests { get; } = new();

        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Requests.Add(
                new RecordedRequest(
                    method,
                    uri,
                    jsonBody,
                    headers is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)));

            if (!_responses.TryGetValue(uri.Host, out var preset))
            {
                return Task.FromResult(TranslationResponse.Failure(404, "no-host"));
            }

            return preset.Status is >= 200 and < 300
                ? Task.FromResult(new TranslationResponse(true, preset.Status, preset.Body ?? string.Empty, string.Empty))
                : Task.FromResult(TranslationResponse.Failure(preset.Status, preset.Body ?? string.Empty));
        }
    }

    /// <summary>按请求里的 q 个数回一批 `[["译N","en"],…]`，用来验证合批与保序。</summary>
    private sealed class BatchEchoTransport : ITranslationTransport
    {
        public List<RecordedRequest> Requests { get; } = new();

        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Requests.Add(
                new RecordedRequest(
                    method,
                    uri,
                    jsonBody,
                    headers is null
                        ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        : new Dictionary<string, string>(headers, StringComparer.OrdinalIgnoreCase)));

            var count = uri.Query.TrimStart('?')
                .Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Count(part => part.StartsWith("q=", StringComparison.Ordinal));
            var items = Enumerable.Range(1, count).Select(index => "[\"译" + index + "\",\"en\"]");
            var body = "[" + string.Join(",", items) + "]";
            return Task.FromResult(new TranslationResponse(true, 200, body, string.Empty));
        }
    }

    /// <summary>每次都抛网络层异常的假传输：用来验证首错短路。</summary>
    private sealed class FailingTranslationTransport : ITranslationTransport
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public Task<TranslationResponse> SendAsync(
            HttpMethod method,
            Uri uri,
            string? jsonBody,
            CancellationToken cancellationToken,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            Interlocked.Increment(ref _requests);
            throw new HttpRequestException("boom");
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
