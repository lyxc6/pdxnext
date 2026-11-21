namespace PdxLauncher.Checks;

using System.Net;
using System.Net.Http;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation;
using PdxLauncher.Core.Online.Translation.Local;
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
            CheckMarkdownTranslatorImages();
            CheckMarkdownTranslatorBareLinks();
            CheckTranslationOptions();
            CheckTranslationCache();
            CheckPromptEchoGuard();
            CheckServiceIgnoresIdentityTranslation();
            CheckCacheScrubsPromptEcho();
            CheckIndexTranslatePromptOrder();
            CheckTranslatedChipFitsContent(fs);
            CheckTranslationService();
            CheckEngineModes();
            CheckMicrosoftTranslatorEngine();
            CheckGoogleCloudTranslationEngine();
            CheckMyMemoryTranslationEngine();
            CheckFreeEngineHostFallback();
            CheckFreeEngineBatching();
            CheckFreeEngineRateLimit();
            CheckMyMemoryQuota();
            CheckEndpointHealthClearHostCooldowns();
            CheckTranslationFailureNote();
            CheckMultiEngineFailureNotes();
            CheckMultiEngineFallback();
            CheckTransportHostGate();
            CheckMarkdownTranslatorBatch();
            CheckStreamingTranslation();
            CheckEngineActivityHooks();
            CheckCoordinatorRemoteBeforeLocal();
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

    /// <summary>图片节点经翻译必须原样保留：Steam 描述里的 [img] 就落在这些节点上。</summary>
    private static void CheckMarkdownTranslatorImages()
    {
        var translator = new MarkdownTranslator((text, _) => Task.FromResult<string?>("译:" + text));
        var document = SteamBbCodeParser.Parse(
            "[b]截图[/b]\n"
            + "[img]https://images.steamusercontent.com/ugc/one.jpg[/img]\n"
            + "[img]https://images.steamusercontent.com/ugc/two.png[/img]");

        var translated = translator.TranslateAsync(document, CancellationToken.None).GetAwaiter().GetResult();
        var before = FlattenInlines(document).OfType<MarkdownImageInline>().ToList();
        var after = FlattenInlines(translated).OfType<MarkdownImageInline>().ToList();

        ChecksContext.Check("原文里 [img] 解析出图片节点", before.Count >= 2);
        ChecksContext.Equal("翻译后图片节点数不变", before.Count, after.Count);
        ChecksContext.Equal("翻译后第一张图片 URL 不变", before[0].Url, after[0].Url);
        ChecksContext.Equal("翻译后第二张图片 URL 不变", before[1].Url, after[1].Url);
        ChecksContext.Equal("翻译后图片替代文本不变", before[1].Alt, after[1].Alt);
        ChecksContext.Check(
            "图片地址没有被当作文字翻译",
            after.All(image => !image.Url.StartsWith("译:", StringComparison.Ordinal)));
        ChecksContext.Check(
            "图片之外的文字仍然翻译",
            translated.ToPlainText().Contains("译:截图", StringComparison.Ordinal));
    }

    /// <summary>可见文本本身就是 URL 的链接必须原样保留，普通链接文字仍要翻译。</summary>
    private static void CheckMarkdownTranslatorBareLinks()
    {
        var calls = new List<string>();
        var translator = new MarkdownTranslator((text, _) =>
        {
            calls.Add(text);
            return Task.FromResult<string?>("译:" + text);
        });

        var document = SteamBbCodeParser.Parse(
            "地址 https://example.com/a?b=1 和 [url]https://example.com/b[/url] "
            + "以及 [url=https://example.com/c]说明[/url]");

        var translated = translator.TranslateAsync(document, CancellationToken.None).GetAwaiter().GetResult();
        var links = CollectLinks(translated);

        ChecksContext.Equal("两条裸链接加一条普通链接数量不变", 3, links.Count);
        ChecksContext.Equal("裸 URL 可见文本原样保留", "https://example.com/a?b=1", LinkText(links[0]));
        ChecksContext.Equal("裸 URL 目标地址原样保留", "https://example.com/a?b=1", links[0].Url);
        ChecksContext.Equal("[url] 自动文字原样保留", "https://example.com/b", LinkText(links[1]));
        ChecksContext.Equal("[url] 自动文字的目标地址不变", "https://example.com/b", links[1].Url);
        ChecksContext.Equal("普通链接文字仍然翻译", "译:说明", LinkText(links[2]));
        ChecksContext.Equal("普通链接目标地址不变", "https://example.com/c", links[2].Url);
        ChecksContext.Check(
            "裸 URL 没有送进翻译引擎",
            calls.All(call => !call.Contains("https://", StringComparison.Ordinal)));

        // 可见文本是 URL、但与目标地址不同：同样按「是 URL」跳过翻译。
        var mismatch = new MarkdownDocument(
            new MarkdownBlock[]
            {
                new MarkdownParagraph(new MarkdownInline[]
                {
                    new MarkdownLinkInline(
                        new MarkdownInline[] { new MarkdownTextInline("https://example.com/x") },
                        "https://example.com/y"),
                }),
            },
            false);
        var mismatchResult = translator.TranslateAsync(mismatch, CancellationToken.None).GetAwaiter().GetResult();
        var mismatchLink = CollectLinks(mismatchResult).FirstOrDefault();
        ChecksContext.Equal(
            "可见文本是 URL 时不被翻译",
            "https://example.com/x",
            mismatchLink is null ? string.Empty : LinkText(mismatchLink));
        ChecksContext.Equal(
            "可见文本是 URL 时目标地址不变",
            "https://example.com/y",
            mismatchLink?.Url ?? string.Empty);
    }

    /// <summary>A：整篇文档只调用一次批量委托，块结构、图片、裸链接语义不变。</summary>
    private static void CheckMarkdownTranslatorBatch()
    {
        var calls = new List<IReadOnlyList<string>>();
        var translator = new MarkdownTranslator((batch, _) =>
        {
            calls.Add(batch);
            return Task.FromResult<IReadOnlyList<string?>>(batch.Select(text => "译:" + text).ToArray());
        });

        var document = new MarkdownDocument(
            new MarkdownBlock[]
            {
                new MarkdownHeading(1, new MarkdownInline[] { new MarkdownTextInline("Title") }),
                new MarkdownParagraph(new MarkdownInline[] { new MarkdownTextInline("Hello"), new MarkdownTextInline(" world") }),
                new MarkdownCodeBlock("var x = 1;", "cs"),
                new MarkdownParagraph(new MarkdownInline[]
                {
                    new MarkdownLinkInline(new MarkdownInline[] { new MarkdownTextInline("https://example.com/a") }, "https://example.com/a"),
                }),
                new MarkdownListItem(0, false, 0, new MarkdownInline[] { new MarkdownTextInline("Item") }),
            },
            false);

        var translated = translator.TranslateAsync(document, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("整篇文档只发一次批量请求", 1, calls.Count);
        ChecksContext.Equal("批量收集三条可译文本", 3, calls[0].Count);
        ChecksContext.Equal("连续纯文本合并为一条", "Hello world", calls[0][1]);
        ChecksContext.Check("裸链接不送进批量", calls[0].All(text => !text.Contains("https://", StringComparison.Ordinal)));

        var heading = translated.Blocks[0] as MarkdownHeading;
        ChecksContext.Equal("批量翻译标题", "译:Title", (heading?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);
        var code = translated.Blocks[2] as MarkdownCodeBlock;
        ChecksContext.Equal("批量翻译不动代码块", "var x = 1;", code?.Text ?? string.Empty);
        var link = (translated.Blocks[3] as MarkdownParagraph)?.Inlines.FirstOrDefault() as MarkdownLinkInline;
        ChecksContext.Equal("批量翻译不动裸链接", "https://example.com/a", link?.Url ?? string.Empty);

        // 译不出来时保留原文（批量路径同样不能把内容改空）。
        var failing = new MarkdownTranslator((batch, _) =>
            Task.FromResult<IReadOnlyList<string?>>(new string?[batch.Count]));
        var kept = failing.TranslateAsync(document, CancellationToken.None).GetAwaiter().GetResult();
        var keptHeading = kept.Blocks[0] as MarkdownHeading;
        ChecksContext.Equal("批量译文为空时保留原文", "Title", (keptHeading?.Inlines.FirstOrDefault() as MarkdownTextInline)?.Text ?? string.Empty);
    }

    /// <summary>B：流式引擎每翻出一条立刻回调，不等整批（离线 2B 兜底时译名逐条出现）。</summary>
    private static void CheckStreamingTranslation()
    {
        var progress = new List<string>();
        var engine = new StreamingStubEngine();
        var service = new TranslationService(new ITranslationEngine[] { engine }, null, new RecordingLogger());
        var options = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Builtin },
            "zh-CN");

        var results = service.TranslateAsync(
            new[] { "a", "b", "c" },
            options,
            CancellationToken.None,
            (_, text) => progress.Add(text ?? string.Empty)).GetAwaiter().GetResult();

        ChecksContext.Check("流式引擎逐条回调", engine.Streamed.Count == 3);
        ChecksContext.Equal("流式回调带原文顺序", "a", engine.Streamed[0]);
        ChecksContext.Equal("流式回调带原文顺序（末条）", "c", engine.Streamed[2]);
        ChecksContext.Equal("界面进度收到每条译文", 3, progress.Count);
        ChecksContext.Equal("结果数组与入参一一对应", "stream:c", results[2] ?? string.Empty);

        // 回落时 only 未命中的子集交给下一个引擎，进度必须回填到正确的原文下标（不能错位）。
        var fallbackProgress = new Dictionary<int, string>();
        var partial = new StreamingStubEngine();
        var first = new PartialFailEngine();
        var fallbackService = new TranslationService(
            new ITranslationEngine[] { first, partial },
            null,
            new RecordingLogger());
        var fallbackOptions = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Auto },
            "zh-CN");
        var fallbackResults = fallbackService.TranslateAsync(
            new[] { "x", "y", "z" },
            fallbackOptions,
            CancellationToken.None,
            (i, text) => fallbackProgress[i] = text ?? string.Empty).GetAwaiter().GetResult();
        ChecksContext.Equal("回落成功后结果回到原文原位", "stream:y", fallbackResults[1] ?? string.Empty);
        ChecksContext.Equal("回落后进度回调也对准原文下标", "stream:y", fallbackProgress[1]);
        ChecksContext.Equal("首引擎成功的条目不被回落覆盖", "only:z", fallbackProgress[2]);
        ChecksContext.Check("首引擎成功的条目不误报回落进度", fallbackProgress[0] == "only:x");
    }

    /// <summary>D：在途钩子只在真有未命中文本时成对触发；缓存全命中不登记在途。</summary>
    private static void CheckEngineActivityHooks()
    {
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, logger);
        var engine = new StubEngine("ok", configured: true, succeed: true);
        var service = new TranslationService(new ITranslationEngine[] { engine }, cache, logger);
        var options = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Builtin },
            "zh-CN");

        var started = 0;
        var ended = 0;
        service.OnEngineActivityStarted = () => started++;
        service.OnEngineActivityEnded = () => ended++;

        // 先翻一次把译文写进缓存（这次当然要在途）；之后才重置计数验证命中路径。
        service.TranslateAsync(new[] { "cache-me" }, options, CancellationToken.None).GetAwaiter().GetResult();
        engine.Calls = 0;
        started = 0;
        ended = 0;

        service.TranslateAsync(new[] { "cache-me" }, options, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("缓存命中不登记在途", 0, started);
        ChecksContext.Equal("缓存命中不请求引擎", 0, engine.Calls);

        service.TranslateAsync(new[] { "fresh" }, options, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("未命中时登记一次在途", 1, started);
        ChecksContext.Equal("未命中时在途收尾一次", 1, ended);
    }

    /// <summary>C：自动档远程引擎排在本地模型之前（静态读源码，Coordinator 依赖 WinUI 无法在自检里构造）。</summary>
    private static void CheckCoordinatorRemoteBeforeLocal()
    {
        var root = FindWorkspaceRoot();
        var path = Path.Combine(root, "src", "PdxLauncher.App", "Services", "TranslationCoordinator.cs");
        var source = System.IO.File.ReadAllText(path);

        var remoteFree = source.IndexOf("engines.Add(new FreeTranslationEngine", StringComparison.Ordinal);
        var localFallback = source.LastIndexOf("engines.AddRange(_local.CreateEngines())", StringComparison.Ordinal);
        ChecksContext.Check("源码里有远程免费引擎", remoteFree >= 0);
        ChecksContext.Check("源码里有本地兜底引擎", localFallback >= 0);
        ChecksContext.Check("自动档远程排在本地兜底之前", remoteFree >= 0 && localFallback > remoteFree);
        ChecksContext.Check(
            "本地兜底只在自动档追加",
            source.Contains("mode == TranslationEngineModes.Auto && _local is not null", StringComparison.Ordinal));
    }

    // ---------------------------------------------------------------- 测试替身

    /// <summary>首个引擎：除 "y" 外都成功，用来验证回落到流式引擎时进度下标不错位。</summary>
    private sealed class PartialFailEngine : ITranslationEngine
    {
        public string Id => "partial";

        public bool IsConfigured => true;

        public Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(text == "y" ? null : "only:" + text);

        public Task<IReadOnlyList<string?>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string?>>(texts.Select(text => text == "y" ? null : "only:" + text).ToArray());
    }

    /// <summary>流式引擎替身：按条回调，记录已上报的原文。</summary>
    private sealed class StreamingStubEngine : IStreamingTranslationEngine
    {
        public string Id => "stream";

        public bool IsConfigured => true;

        public List<string> Streamed { get; } = new();

        public Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("stream:" + text);

        public Task<IReadOnlyList<string?>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string?>>(texts.Select(text => (string?)("stream:" + text)).ToArray());

        public Task TranslateBatchStreamingAsync(
            IReadOnlyList<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken,
            Action<int, string?> onItem)
        {
            for (var i = 0; i < texts.Count; i++)
            {
                Streamed.Add(texts[i]);
                onItem(i, "stream:" + texts[i]);
            }
            return Task.CompletedTask;
        }
    }

    /// <summary>按块拍平行内节点（含引用、链接与强调样式里的嵌套节点），供测试逐项比对。</summary>
    private static List<MarkdownInline> FlattenInlines(MarkdownDocument document)
    {
        var result = new List<MarkdownInline>();
        foreach (var block in document.Blocks)
        {
            AddBlockInlines(block, result);
        }

        return result;
    }

    private static void AddBlockInlines(MarkdownBlock block, List<MarkdownInline> result)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                AddInlines(heading.Inlines, result);
                break;
            case MarkdownParagraph paragraph:
                AddInlines(paragraph.Inlines, result);
                break;
            case MarkdownListItem item:
                AddInlines(item.Inlines, result);
                break;
            case MarkdownQuote quote:
                foreach (var child in quote.Blocks)
                {
                    AddBlockInlines(child, result);
                }

                break;
        }
    }

    private static void AddInlines(IReadOnlyList<MarkdownInline> inlines, List<MarkdownInline> result)
    {
        foreach (var inline in inlines)
        {
            result.Add(inline);
            switch (inline)
            {
                case MarkdownLinkInline link:
                    AddInlines(link.Inlines, result);
                    break;
                case MarkdownStyledInline styled:
                    AddInlines(styled.Inlines, result);
                    break;
            }
        }
    }

    /// <summary>按块拍平外层链接节点（不把链接里再嵌的链接当成第二个链接）。</summary>
    private static List<MarkdownLinkInline> CollectLinks(MarkdownDocument document)
    {
        var result = new List<MarkdownLinkInline>();
        foreach (var block in document.Blocks)
        {
            AddBlockLinks(block, result);
        }

        return result;
    }

    private static void AddBlockLinks(MarkdownBlock block, List<MarkdownLinkInline> result)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                AddInlineLinks(heading.Inlines, result);
                break;
            case MarkdownParagraph paragraph:
                AddInlineLinks(paragraph.Inlines, result);
                break;
            case MarkdownListItem item:
                AddInlineLinks(item.Inlines, result);
                break;
            case MarkdownQuote quote:
                foreach (var child in quote.Blocks)
                {
                    AddBlockLinks(child, result);
                }

                break;
        }
    }

    private static void AddInlineLinks(IReadOnlyList<MarkdownInline> inlines, List<MarkdownLinkInline> result)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownLinkInline link:
                    result.Add(link);
                    break;
                case MarkdownStyledInline styled:
                    AddInlineLinks(styled.Inlines, result);
                    break;
            }
        }
    }

    private static string LinkText(MarkdownLinkInline link) => FlattenText(link.Inlines);

    private static string FlattenText(IReadOnlyList<MarkdownInline> inlines) =>
        string.Concat(inlines.Select(InlineText));

    private static string InlineText(MarkdownInline inline) => inline switch
    {
        MarkdownTextInline text => text.Text,
        MarkdownCodeInline code => code.Text,
        MarkdownBreakInline => "\n",
        MarkdownImageInline image => image.Alt,
        MarkdownLinkInline link => FlattenText(link.Inlines),
        MarkdownStyledInline styled => FlattenText(styled.Inlines),
        _ => string.Empty,
    };

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

    // ---------------------------------------------------------------- 提示词回吐

    /// <summary>
    /// 模型把提示词当译文吐回来时的闸门：截断提示词尾巴、只留提示词时判失败、正常译文不动。
    /// 样本取自 2026-11-14 安装版缓存里的真实污染条目（`cache:translations`）。
    /// </summary>
    private static void CheckPromptEchoGuard()
    {
        const string polluted =
            "《十字架的阴影》（SOTC）——基督教扩展模组\n\n" +
            "【约束要求】\n" +
            "1. 【硬性要求】专有名词/术语对照: mod→模组、mods→模组、workshop→创意工坊\n\n" +
            "只输出译文，不要有任何额外说明。";

        ChecksContext.Check("回吐: 认出提示词痕迹", TranslationEchoGuard.ContainsPromptMarkup(polluted));
        ChecksContext.Equal(
            "回吐: 只剩原文前缀时保留原文（由 TranslationService 当没翻处理）",
            "《十字架的阴影》（SOTC）——基督教扩展模组",
            TranslationEchoGuard.Trim(polluted) ?? string.Empty);
        ChecksContext.Equal(
            "回吐: 译文 + 约束块只留译文",
            "纹章王冠",
            TranslationEchoGuard.Trim("纹章王冠\n\n【约束要求】\n1. 【硬性要求】专名/术语对照: mod→模组\n\n只输出译文，不要有任何额外说明。") ?? string.Empty);

        // 正常译文：术语词照留，句中出现「只输出译文」也不截（只认行首）。
        const string normal = "Workshop 里的 mod 需要一个 DLC 才能开局。";
        ChecksContext.Equal("回吐: 正常译文原样返回", normal, TranslationEchoGuard.Trim(normal) ?? string.Empty);
        ChecksContext.Check("回吐: 正常译文没有标记", !TranslationEchoGuard.ContainsPromptMarkup(normal));
        const string quoted = "他写道：只输出译文，不要解释。";
        ChecksContext.Equal("回吐: 句中的提示词字样不截断", quoted, TranslationEchoGuard.Trim(quoted) ?? string.Empty);

        ChecksContext.Check(
            "回吐: ChatML 记号被剥掉",
            TranslationEchoGuard.Trim("<|im_start|>assistant\n纹章王冠<|im_end|>") == "纹章王冠");

        ChecksContext.Check(
            "回吐: think 块被整段剥掉",
            TranslationEchoGuard.Trim("\u003Cthink\u003E先想想用词\u003C/think\u003E纹章王冠") == "纹章王冠");

        ChecksContext.Check("回吐: 空输出返回 null", TranslationEchoGuard.Trim("   ") is null);
        ChecksContext.Check("回吐: 只有提示词时返回 null", TranslationEchoGuard.Trim("【约束要求】\n只输出译文。") is null);
        ChecksContext.Check("回吐: 只输出 open think 也返回 null", TranslationEchoGuard.Trim("\u003Cthink\u003E我想想") is null);
    }

    /// <summary>
    /// 与原文逐字相同的「译文」由服务层当成没翻：不返回、不算失败、不写缓存。
    /// 这条保证中文简介 / 中文 mod 名不会因为「模型原样回一句」而在列表或详情页多出译名框与假失败提示。
    /// </summary>
    private static void CheckServiceIgnoresIdentityTranslation()
    {
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var cache = new TranslationCache(documents, clock, logger);
        var service = new TranslationService(new ITranslationEngine[] { new IdentityEngine() }, cache, logger);
        var options = TranslationOptions.FromSettings(
            new AppSettings { TranslateEnabled = true, TranslateEngineMode = TranslationEngineModes.Builtin },
            "zh-CN");

        var translated = new List<(int Index, string? Text)>();
        var results = service
            .TranslateAsync(["首都扩建", "Heraldic Crowns"], options, CancellationToken.None, (i, text) => translated.Add((i, text)))
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("原样回吐: 与原文相同的条目不出译文", results[0] is null);
        ChecksContext.Check("原样回吐: 整批都相同也不算失败", service.LastFailure is null, service.LastFailure ?? "null");
        ChecksContext.Check("原样回吐: 进度回调不发同文条目", translated.All(item => item.Index != 0));
        ChecksContext.Check(
            "原样回吐: 同文结果不写缓存",
            cache.TryGetAsync("identity", "zh-CN", "首都扩建", 30, CancellationToken.None).GetAwaiter().GetResult() is null);
        ChecksContext.Check(
            "原样回吐: 不同文的条目正常返回",
            results[1] == "Heraldic Crowns（译）");
    }

    /// <summary>
    /// 缓存自愈：旧版本存下的「原文 + 提示词」条目读盘时被清掉并落盘；正常条目与写入闸门不受影响。
    /// </summary>
    private static void CheckCacheScrubsPromptEcho()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var logger = new RecordingLogger();
        var documents = new MemoryDocumentStore();

        var polluted = "首都扩建\n\n【约束要求】\n1. 【硬性要求】专名/术语对照: mod→模组\n\n只输出译文，不要有任何额外说明。";
        var pollutedName = "纹章王冠\n\n【约束要求】\n只输出译文。";
        var cleanKey = TranslationCache.BuildKey("index-translate", "zh-CN", "Capital expansion");
        var pollutedKey = TranslationCache.BuildKey("index-translate", "zh-CN", "首都扩建");
        var modId = Guid.Parse("2a1f0f0e-9c3b-4f4f-9c4c-6c6f6e6d6e6f");
        var nameKey = modId.ToString("N") + "\nzh-CN";

        var document = new
        {
            version = 1,
            entries = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [cleanKey] = new { text = "首都扩张", fetchedUtc = "2026-11-18T00:00:00Z" },
                [pollutedKey] = new { text = polluted, fetchedUtc = "2026-11-14T14:36:15Z" },
            },
            names = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [nameKey] = new
                {
                    modId = modId.ToString(),
                    targetLanguage = "zh-CN",
                    sourceName = "Heraldic Crowns",
                    translation = pollutedName,
                    updatedUtc = "2026-11-14T14:36:15Z",
                },
            },
        };

        documents.Write(JsonDocumentKeys.Translations, JsonSerializer.Serialize(document));

        var cache = new TranslationCache(documents, clock, logger);
        var cleanHit = cache
            .TryGetAsync("index-translate", "zh-CN", "Capital expansion", 30, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("缓存自愈: 正常条目仍在", "首都扩张", cleanHit ?? string.Empty);

        var pollutedHit = cache
            .TryGetAsync("index-translate", "zh-CN", "首都扩建", 30, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("缓存自愈: 回吐条目读不出来", pollutedHit is null);

        var names = cache
            .GetNameTranslationsAsync("zh-CN", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("缓存自愈: 回吐的旧译名不回填", names.Count == 0, names.Count.ToString());

        var persisted = documents.TryRead(JsonDocumentKeys.Translations, out var json) ? json : null;
        ChecksContext.Check(
            "缓存自愈: 清理结果落盘（不再含提示词标记）",
            persisted is not null
                && !persisted.Contains("【约束要求】", StringComparison.Ordinal)
                && persisted.Contains("首都扩张", StringComparison.Ordinal),
            persisted ?? "无文档");
        ChecksContext.Check(
            "缓存自愈: 打了一条清理日志",
            logger.Warnings.Any(message => message.Contains("混入提示词", StringComparison.Ordinal)));

        // 写入闸门：回吐内容拒写；干净译文按去空白后的内容存入。
        var writeStore = new MemoryDocumentStore();
        var writeCache = new TranslationCache(writeStore, clock, new RecordingLogger());
        writeCache
            .SetAsync("engine", "zh-CN", "hello", polluted, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var refused = writeCache
            .TryGetAsync("engine", "zh-CN", "hello", 30, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("缓存写入: 回吐内容拒写", refused is null);

        writeCache
            .SetAsync("engine", "zh-CN", "hello", "  你好  ", CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        var trimmedHit = writeCache
            .TryGetAsync("engine", "zh-CN", "hello", 30, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("缓存写入: 译文去首尾空白后落盘", "你好", trimmedHit ?? string.Empty);
    }

    /// <summary>
    /// 本地引擎提示词形状：约束块与术语表必须在【源文】之前，源文是最后一段。
    /// 这是「模型顺着往下写、把约束当译文吐回来」的根因修复点，形状变了要立刻红。
    /// </summary>
    private static void CheckIndexTranslatePromptOrder()
    {
        var entries = new[]
        {
            new LocalGlossaryEntry("mod", "模组"),
            new LocalGlossaryEntry("workshop", "创意工坊"),
        };
        var prompt = IndexTranslateEngine.BuildPromptText("Hello mod", "zh", entries);
        var constraintIndex = prompt.IndexOf("【约束要求】", StringComparison.Ordinal);
        var sourceIndex = prompt.IndexOf("【源文】", StringComparison.Ordinal);
        var glossaryIndex = prompt.IndexOf("mod→模组", StringComparison.Ordinal);

        ChecksContext.Check("本地提示词: 有约束块", constraintIndex >= 0, prompt);
        ChecksContext.Check(
            "本地提示词: 约束块在【源文】之前",
            constraintIndex >= 0 && sourceIndex > constraintIndex,
            "约束=" + constraintIndex + "，源文=" + sourceIndex);
        ChecksContext.Check(
            "本地提示词: 术语表也在【源文】之前",
            glossaryIndex >= 0 && glossaryIndex < sourceIndex,
            "术语=" + glossaryIndex + "，源文=" + sourceIndex);
        ChecksContext.Check(
            "本地提示词: 源文收尾（模型没有可续写的指令）",
            prompt.TrimEnd().EndsWith("Hello mod", StringComparison.Ordinal),
            prompt);

        var bare = IndexTranslateEngine.BuildPromptText("Hello", "zh", Array.Empty<LocalGlossaryEntry>());
        ChecksContext.Check(
            "本地提示词: 无词条时同样把源文放最后",
            bare.TrimEnd().EndsWith("Hello", StringComparison.Ordinal)
                && !bare.Contains("【约束要求】", StringComparison.Ordinal),
            bare);
    }

    /// <summary>
    /// 行内译名框必须按内容取宽：纵向面板里默认 Stretch 会把描边框拉成通栏（1.3.0 的实机毛病）。
    /// 正文译文块（简介 / 更新日志 / 评论）故意保持整栏宽，不在这里断言。
    /// </summary>
    private static void CheckTranslatedChipFitsContent(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var root = FindWorkspaceRoot();
        var files = new[]
        {
            Path.Combine("src", "PdxLauncher.App", "Views", "WorkshopPage.xaml"),
            Path.Combine("src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml"),
            Path.Combine("src", "PdxLauncher.App", "Views", "ModsPage.xaml"),
            Path.Combine("src", "PdxLauncher.App", "Views", "AddModsPage.xaml"),
            Path.Combine("src", "PdxLauncher.App", "Views", "WorkshopDetailPage.xaml"),
        };

        var found = 0;
        foreach (var relative in files)
        {
            var path = Path.Combine(root, relative);
            if (!fs.FileExists(path))
            {
                ChecksContext.Check("译名框: 源文件存在（" + Path.GetFileName(relative) + "）", false, path);
                continue;
            }

            var xaml = fs.ReadAllText(path);
            var index = 0;
            while ((index = xaml.IndexOf("Text=\"{x:Bind TranslatedName", index, StringComparison.Ordinal)) >= 0)
            {
                found++;
                var borderStart = xaml.LastIndexOf("<Border", index, StringComparison.Ordinal);
                var borderEnd = borderStart >= 0 ? xaml.IndexOf('>', borderStart) : -1;
                var tag = borderStart >= 0 && borderEnd > borderStart
                    ? xaml[borderStart..(borderEnd + 1)]
                    : string.Empty;
                ChecksContext.Check(
                    "译名框: " + Path.GetFileName(relative) + " 按内容取宽（HorizontalAlignment=Left）",
                    tag.Contains("HorizontalAlignment=\"Left\"", StringComparison.Ordinal),
                    tag);
                index += 10;
            }
        }

        ChecksContext.Check(
            "译名框: 覆盖 6 处行内译名（工坊列表 / Mod 库 / 播放集两处 / 添加模组 / 详情合集子项）",
            found == 6,
            found.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
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

    // ---------------------------------------------------------------- 重试时的冷却清理

    /// <summary>
    /// 用户点「重试」时清掉主机的临时冷却，但**不能**清掉额度冷却。
    /// <para>
    /// 清错了的后果只在真机上可见：要么重试按钮点了等于没点（「暂时跳过」立刻又失败），
    /// 要么把服务端额度这个事实清掉，白打一串必然 429 的请求。编译器看不见，所以钉在这里。
    /// </para>
    /// </summary>
    private static void CheckEndpointHealthClearHostCooldowns()
    {
        var clock = new MutableClock(new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero));
        var health = new TranslationEndpointHealth(() => clock.UtcNow);

        foreach (var host in FreeTranslationEngine.Hosts)
        {
            health.MarkDead(host, TimeSpan.FromMinutes(10));
        }

        var resumeAt = clock.UtcNow.AddHours(2);
        health.MarkQuotaExhaustedUntil(resumeAt);

        ChecksContext.Check("清理前主机处于冷却", FreeTranslationEngine.Hosts.All(health.IsDead));
        ChecksContext.Check("清理前额度已标记用完", health.IsQuotaExhausted);

        health.ClearHostCooldowns();

        ChecksContext.Check("清理后主机冷却全部解除", FreeTranslationEngine.Hosts.All(host => !health.IsDead(host)));
        ChecksContext.Check("清理后额度冷却仍然保留", health.IsQuotaExhausted);
        ChecksContext.Equal("清理不动额度恢复时间", resumeAt, health.QuotaResumeAt ?? DateTimeOffset.MinValue);

        // 冷却能再次生效（不是把 MarkDead 一并废掉）。
        health.MarkDead(FreeTranslationEngine.Hosts[0], TimeSpan.FromMinutes(5));
        ChecksContext.Check("清理后还能重新标记冷却", health.IsDead(FreeTranslationEngine.Hosts[0]));

        // 自检用的 Reset 仍然连额度一起清。
        health.Reset();
        ChecksContext.Check("Reset 同时清掉额度标记", !health.IsQuotaExhausted);
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

    /// <summary>
    /// 模拟本地模型「原样回一句」：中文原文按原样返回（等价于没翻），英文加个后缀（真译文）。
    /// </summary>
    private sealed class IdentityEngine : ITranslationEngine
    {
        public string Id => "identity";

        public bool IsConfigured => true;

        public Task<string?> TranslateAsync(string text, string targetLanguage, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(Translate(text));

        public Task<IReadOnlyList<string?>> TranslateBatchAsync(
            IReadOnlyList<string> texts,
            string targetLanguage,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string?>>(texts.Select(text => (string?)Translate(text)).ToList());

        private static string Translate(string text) =>
            text.Any(ch => ch >= '\u2E80') ? text : text + "（译）";
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
