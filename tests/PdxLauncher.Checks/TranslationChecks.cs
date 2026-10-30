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
        var service = new TranslationService(custom, builtin, cache, logger);

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
        var builtinResults = service.TranslateAsync(new[] { "c" }, builtinOnly, CancellationToken.None).GetAwaiter().GetResult();
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
        var cached = service.TranslateAsync(new[] { "c" }, builtinOnly, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("命中缓存返回同一译文", "builtin:c", cached.Count > 0 ? cached[0] ?? string.Empty : string.Empty);
        ChecksContext.Equal("命中缓存不再请求引擎", 0, builtin.Calls);
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
